using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;

public abstract class EdgePath : IReadOnlyList<Strip>, IEquatable<EdgePath>
{
    public static readonly EdgePath Empty = new NormalEdgePath();

    // public static implicit operator EdgePath(List<Strip> list) => new NormalEdgePath(list.ToArray());
    public static implicit operator EdgePath(Strip[] list) => new NormalEdgePath(list);
    
    public virtual EdgePath Conjugate(EdgePath c, bool left)
    {
        if (IsEmpty) return Empty;
        if (c.IsEmpty) return this;
        return new ConjugateEdgePath(this, c, left);
    }

    private EdgePath inverse = null;
    public EdgePath Inverse => inverse ??= GetInverse();

    protected abstract EdgePath GetInverse();

    public abstract IEnumerator<Strip> GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    
    public EdgePath Replace(Func<UnorientedStrip, Strip> newEdges) => 
        Replace(
            strip => strip is ReverseStrip
                ? newEdges(strip.UnderlyingEdge).Reversed()
                : newEdges(strip.UnderlyingEdge)
        );

    public abstract EdgePath Replace(Func<Strip, Strip> newEdges);
    
    public EdgePath Replace(Func<UnorientedStrip, EdgePath> newEdges) => 
        Replace(
            strip => strip is ReverseStrip
                ? newEdges(strip.UnderlyingEdge).Inverse
                : newEdges(strip.UnderlyingEdge)
        );
    
    public abstract EdgePath Replace(Func<Strip, EdgePath> newEdgePaths);

    public abstract EdgePath Concat(EdgePath other);

    public abstract EdgePath Skip(int i);

    public abstract EdgePath Take(int i);
    
    private IReadOnlyList<NamedEdgePath> namedEdgePaths;
    public IReadOnlyList<NamedEdgePath> NamedEdgePaths =>
        namedEdgePaths ??= GetUsedNamedEdgePaths();

    protected abstract IReadOnlyList<NamedEdgePath> GetUsedNamedEdgePaths();

    public abstract int Count { get; }

    public abstract bool IsEmpty { get; }

    public virtual Strip this[int index]
    {
        get
        {
            if (TryGetElementAt(ref index, out var result))
                return result;
            throw new IndexOutOfRangeException();
        }
    }

    protected internal abstract bool TryGetElementAt(ref int i, out Strip result);
    
    public virtual EdgePath Image => new NestedEdgePath(from strip in this select strip.EdgePath);
    
    public virtual EdgePath CancelBacktracking(bool cyclic = false) {
        List<Strip> result = new List<Strip>();
        foreach (var e in this)
        {
            if (result.Count > 0 && result[^1].Equals(e.Reversed()))
                result.RemoveAt(result.Count - 1);
            else
                result.Add(e);
        }    
        if (cyclic)
            while (result.Count > 1 && result[^1].Equals(result[0].Reversed()))
            {
                result.RemoveAt(result.Count - 1);
                result.RemoveAt(0);
            }
        return new NormalEdgePath(result);
    }

    public static EdgePath FromString(string text, IEnumerable<UnorientedStrip> strips,
        IEnumerable<NamedEdgePath> definitionList = null)
    {
        var stripDict = strips.Concat(
            strips.Select(edge => edge.Reversed())
        ).ToDictionary(
            strip => strip.Name
        );
        definitionList ??= Array.Empty<NamedEdgePath>();
        var definitions = definitionList.Concat(
            definitionList.Select(definition => (NamedEdgePath)definition.Inverse)
        ).ToDictionary(
            namedEdgePath => namedEdgePath.name
        );
        return FromString(text, stripDict, definitions);
    }

    public static EdgePath FromString(string text, IReadOnlyDictionary<string, Strip> stripDict,
        IReadOnlyDictionary<string, NamedEdgePath> definitions = null)
    {
        if (text.Count(c => c == '(') != text.Count(c => c == ')'))
            throw new ArgumentException("The expression for the edge path contains a different number of closing and opening parentheses");
        var result = new List<EdgePath>();
        var currentlyReadNormalEdgePath = new List<Strip>();
        var currentlyReadName = "";
        Strip lastStrip = null;
        EdgePath lastThing = null;
        int i = 0;
        for (; i < text.Length; i++)
        {
            var c = text[i];
            switch (c)
            {
                case '*':
                case '·':
                case ' ':
                case '\t':
                case '\n':
                    PushLastThing();
                    CloseLastVariable();
                    PushLastVariable();
                    break;
                case '\'':
                    CloseLastVariable();
                    if (lastStrip != null)
                    {
                        lastStrip = lastStrip.Reversed();
                        break;
                    }
                    if (lastThing != null)
                    {
                        lastThing = lastThing.Inverse;
                        break;
                    }
                    throw new ArgumentException($"Misplaced ' in your input at location {i}");
                case '^':
                case '°':
                    CloseLastVariable();
                    PromoteLastVariableToLastThing();
                    PushLastNormalEdgePath(); // the edge path before the last variable
                    var nextThing = ReadNextThing();
                    if (c == '^')
                        lastThing = lastThing.Conjugate(nextThing, left: false);
                    else
                        lastThing = nextThing.Conjugate(lastThing, left: true);
                    break;
                case '(':
                    PushLastThing();
                    CloseLastVariable();
                    PushLastVariable();
                    PushLastNormalEdgePath();
                    lastThing = ReadParentheses();
                    break;
                case ')':
                    throw new ArgumentException(
                        $"There is an unmatched closing parenthesis in your input at location {i}");
                default:
                    PushLastThing();
                    PushLastVariable();
                    currentlyReadName += c;
                    break;
            }
        }
        PushLastThing();
        CloseLastVariable();
        PushLastVariable();
        PushLastNormalEdgePath();
        
        return result.Count == 1 ? result[0] : new NestedEdgePath(result);

        void CloseLastVariable() => CloseLastVariableRef(ref lastStrip, ref lastThing);
        
        void CloseLastVariableRef(ref Strip lastStrip, ref EdgePath lastThing)
        {
            if (currentlyReadName == "")
                return;
            if (!stripDict.TryGetValue(currentlyReadName, out lastStrip))
            {
                if (!definitions.TryGetValue(currentlyReadName, out var lastNamedEdgePath))
                    throw new ArgumentException($"Unknown strip {currentlyReadName} in your input at location {i}. Please separate different edges with a space or a *.");
                PushLastNormalEdgePath();
                lastThing = lastNamedEdgePath;
            }
            currentlyReadName = "";
        }

        
        void PushLastVariable()
        {
            if (lastStrip != null)
            {
                currentlyReadNormalEdgePath.Add(lastStrip);
                lastStrip = null;
            }
            else if (lastThing is NamedEdgePath)
                PushLastThing();
        }

        void PushLastNormalEdgePath()
        {
            if (currentlyReadNormalEdgePath.Count > 0) 
                result.Add(new NormalEdgePath(currentlyReadNormalEdgePath.ToArray()));
            currentlyReadNormalEdgePath.Clear();
        }

        void PushLastThing()
        {
            if (lastThing != null)
                result.Add(lastThing);
            lastThing = null;
        }
        
        void PromoteLastVariableToLastThing()
        {
            if (lastStrip != null)
                lastThing = new NormalEdgePath(lastStrip);
            lastStrip = null;
        }

        EdgePath ReadParentheses()
        {
            var startIndex = i + 1;
            int nestingLevel = 1;
            while (nestingLevel > 0)
            {
                i++;
                switch (text[i])
                {
                    case '(':
                        nestingLevel++;
                        break;
                    case ')':
                        nestingLevel--;
                        break;
                }
            }
            return FromString(text.Substring(startIndex, i - startIndex), stripDict, definitions);
        }

        EdgePath ReadNextThing()
        {
            i++;
            for (; i < text.Length; i++)
            {
                var c = text[i];
                switch (c)
                {
                    case '*':
                    case '·':
                    case ' ':
                        if (CloseAndReturnLastVariable(out var readStrip))
                            return readStrip;
                        break;
                    case '\'':
                        if (!CloseAndReturnLastVariable(out readStrip))
                            throw new ArgumentException($"Misplaced ' in your input at location {i}");
                        var stripReversed = readStrip.Inverse;
                        return stripReversed;
                    case '°':
                    case '^':
                        if (!CloseAndReturnLastVariable(out readStrip))
                            throw new ArgumentException($"There are consecutive ° and ^ symbols in your input at location {i}");
                        i--; // read the ° or ^ again. (left-grouping)
                        return readStrip;
                    case '(':
                        if (!CloseAndReturnLastVariable(out readStrip)) 
                            return ReadParentheses();
                        i--; // read the ( again.
                        return readStrip;
                    case ')':
                        throw new ArgumentException(
                            $"There is an unexpected closing parenthesis in your input at location {i} after a ° or ^");
                    default:
                        currentlyReadName += c;
                        break;
                }
            }

            if (CloseAndReturnLastVariable(out var strip))
                return strip;
            throw new ArgumentException("Your input ended unexpectedly.");

            bool CloseAndReturnLastVariable(out EdgePath edgePath)
            {
                Strip potentialLastStrip = null;
                EdgePath potentialLastThing = null;
                CloseLastVariableRef(ref potentialLastStrip, ref potentialLastThing);
                if (potentialLastStrip != null)
                {
                    edgePath = new NormalEdgePath(potentialLastStrip);
                    return true;
                }
                if (potentialLastThing is NamedEdgePath namedEdgePath)
                {
                    edgePath = namedEdgePath;
                    return true;
                }
                edgePath = null;
                return false;
            }
        }
    }

    public override string ToString() => ToString(150, 10);

    public virtual string ToString(int maxLength, int tail) => ToColoredString(maxLength, tail, strip => strip.Name); // don't remove parentheses


    public string ToColorfulString(int maxLength, int tail)
    {
        var result = ToColoredString(maxLength, tail, strip => strip.ColorfulName);
        if (result.StartsWith('(') && result.EndsWith(')'))
            result = result[1..^1]; 
        return result;
    }

    protected internal abstract string ToColoredString(int maxLength, int tail, Func<Strip, string> colorfulName);

    protected internal abstract int ExpectedStringLength();

    public static EdgePath Concatenate(IEnumerable<EdgePath> paths)
    {
        var subPaths = new List<EdgePath>();
        var currentlyReadNormalEdgePath = new List<Strip>();
        foreach (var path in paths)
        {
            switch (path)
            {
                case NamedEdgePath:
                case ConjugateEdgePath:
                    PushNormalEdgePath();
                    subPaths.Add(path);
                    break;
                case NestedEdgePath nestedEdgePath:
                    PushNormalEdgePath();
                    subPaths.AddRange(nestedEdgePath.subPaths);
                    break;
                case NormalEdgePath normalEdgePath:
                    currentlyReadNormalEdgePath.AddRange(normalEdgePath);
                    break;
                default:
                    throw new NotImplementedException($"The EdgePath type {path.GetType().Name} is not supported in Concatenate.");
            }
        }
        PushNormalEdgePath();
        
        if (subPaths.Count >= 2)
            return new NestedEdgePath(subPaths);
        return subPaths.Count == 1 ? subPaths[0] : Empty;
        
        void PushNormalEdgePath()
        {
            if (currentlyReadNormalEdgePath.Count <= 0) return;
            subPaths.Add(new NormalEdgePath(currentlyReadNormalEdgePath.ToArray()));
            currentlyReadNormalEdgePath.Clear();
        }
    }

    public bool Equals(EdgePath other) => this.SequenceEqual(other);

    public override bool Equals(object obj) => ReferenceEquals(this, obj) || obj is EdgePath other && Equals(other);

    public static bool operator ==(EdgePath left, EdgePath right) => Equals(left, right);

    public static bool operator !=(EdgePath left, EdgePath right) => !Equals(left, right);
}

public class NamedEdgePath : EdgePath
{
    public readonly EdgePath value;
    public readonly string name;

    public NamedEdgePath(EdgePath value, string name)
    {
        this.value = value;
        this.name = name;
    }

    protected override EdgePath GetInverse() => new NamedEdgePath(value.Inverse, name.ReverseUpper());

    public override IEnumerator<Strip> GetEnumerator() => value.GetEnumerator();

    public override EdgePath Replace(Func<Strip, Strip> newEdges) => value.Replace(newEdges);

    public override EdgePath Replace(Func<Strip, EdgePath> newEdgePaths) => value.Replace(newEdgePaths);

    public override EdgePath Concat(EdgePath other) => other switch
    {
        NestedEdgePath n and not ConjugateEdgePath => new NestedEdgePath(n.subPaths.Prepend(this)),
        _ => new NestedEdgePath(this, other)
    };

    public override EdgePath Skip(int i) => i <= 0 ? this : value.Skip(i);

    public override EdgePath Take(int i) => i >= Count ? this : value.Take(i);

    protected override IReadOnlyList<NamedEdgePath> GetUsedNamedEdgePaths() => new []{this};

    public override int Count => value.Count;

    public override bool IsEmpty => value.IsEmpty;

    protected internal override bool TryGetElementAt(ref int i, out Strip result) => value.TryGetElementAt(ref i, out result);

    protected internal override string ToColoredString(int maxLength, int tail, Func<Strip, string> colorfulName) => name;

    protected internal override int ExpectedStringLength() => name.Length;
}

public class ConjugateEdgePath : NestedEdgePath // todo: Clean code: remove this inheritance.
{
    private readonly EdgePath inner;
    private readonly EdgePath outer;
    private readonly bool leftConjugation;

    public ConjugateEdgePath(EdgePath inner, EdgePath outer, bool leftConjugation): base(
        leftConjugation ?
            new []{ outer, inner, outer.Inverse } :
            new []{ outer.Inverse, inner, outer }
    )
    {
        this.inner = inner;
        this.outer = outer;
        this.leftConjugation = leftConjugation;
    }


    protected override EdgePath GetInverse() => new ConjugateEdgePath(inner.Inverse, outer, leftConjugation);

    public override EdgePath Replace(Func<Strip, Strip> newEdges) => 
        inner.Replace(newEdges).Conjugate(
            outer.Replace(newEdges),
            leftConjugation
        );

    public override EdgePath Replace(Func<Strip, EdgePath> newEdgePaths) => 
        inner.Replace(newEdgePaths).Conjugate(
            outer.Replace(newEdgePaths),
            leftConjugation
        );

    public override EdgePath Concat(EdgePath other) => 
        other switch
        {
            EdgePath { IsEmpty: true } => this,
            ConjugateEdgePath otherConjugate when otherConjugate.outer.Equals(outer) && otherConjugate.leftConjugation == leftConjugation || otherConjugate.outer.Equals(outer.Inverse) && otherConjugate.leftConjugation != leftConjugation => new ConjugateEdgePath(inner.Concat(otherConjugate.inner), outer,  leftConjugation),
            NestedEdgePath nestedEdgePath and not ConjugateEdgePath => new NestedEdgePath(nestedEdgePath.subPaths.Prepend(this)),
            _ => new NestedEdgePath(this, other)
        };

    protected override IReadOnlyList<NamedEdgePath> GetUsedNamedEdgePaths() => inner.NamedEdgePaths.Concat(outer.NamedEdgePaths).ToArray();

    public override int Count => inner.Count + outer.Count * 2;

    public override string ToString(int maxLength, int tail) => base.ToString(maxLength, tail);

    protected internal override string ToColoredString(int maxLength, int tail, Func<Strip, string> colorfulName)
    {
        int innerExpectedLength = inner.ExpectedStringLength();
        int outerExpectedLength = outer.ExpectedStringLength();
        int smallerLength = Math.Min(innerExpectedLength, outerExpectedLength);
        int average = maxLength / 2;
        if (smallerLength < average)
            average += average - smallerLength;
        var innerString = inner.ToColoredString(average, average / 4, colorfulName);
        while (innerString.StartsWith('(') && innerString.EndsWith(')'))
            innerString = innerString[1..^1]; // remove parentheses
        var outerString = outer.ToColoredString(average, average / 4, colorfulName);
        while (outerString.StartsWith('(') && outerString.EndsWith(')'))
            outerString = outerString[1..^1]; // remove parentheses
        var innerWithParentheses = innerExpectedLength > 2 ? $"({innerString})" : innerString;
        var outerWithParentheses = outerExpectedLength > 2 ? $"({outerString})" : outerString;
        return leftConjugation
            ? $"{outerWithParentheses}°{innerWithParentheses}"
            : $"{innerWithParentheses}^{outerWithParentheses}";
    }

    protected internal override int ExpectedStringLength() => inner.ExpectedStringLength() + outer.ExpectedStringLength() + 3;
}

/// <summary>
/// This is the tree-like structure of EdgePaths
/// </summary>
public class NestedEdgePath : EdgePath
{
    [NotNull] protected internal readonly EdgePath[] subPaths;

    public NestedEdgePath(params EdgePath[] subPaths)
    {
        this.subPaths = subPaths;
    }

    public NestedEdgePath(IEnumerable<EdgePath> subPaths) : this(subPaths.ToArray())
    {   }


    protected override EdgePath GetInverse() => new NestedEdgePath(subPaths.Reverse().Select(path => path.Inverse));

    public override IEnumerator<Strip> GetEnumerator() => subPaths.SelectMany(edgePath => edgePath).GetEnumerator();


    public override EdgePath Replace(Func<Strip, Strip> newEdges) =>
        EdgePath.Concatenate(subPaths.Select(
            path => path.Replace(newEdges)
        ));

    public override EdgePath Replace(Func<Strip, EdgePath> newEdgePaths) =>
        EdgePath.Concatenate(subPaths.Select(
            path => path.Replace(newEdgePaths)
        ));

    public override EdgePath Concat(EdgePath other) =>
        other switch
        {
            NestedEdgePath nestedEdgePath and not ConjugateEdgePath => 
                new NestedEdgePath(subPaths.Concat(nestedEdgePath.subPaths)),
            NormalEdgePath normalEdgePath when subPaths[^1] is NormalEdgePath rightmostNormalEdgePath =>
                new NestedEdgePath(subPaths[..^1].Append(rightmostNormalEdgePath.Concat(normalEdgePath))), 
            _ =>
                new NestedEdgePath(subPaths.Append(other))
        };

    public override EdgePath Skip(int i)
    {
        int j = 0;
        for (; j < subPaths.Length; j++)
        {
            var count = subPaths[j].Count;
            if (i < count) break;      
            i -= count;
        }

        if (j == subPaths.Length)
            return Empty;

        return EdgePath.Concatenate(subPaths[(j + 1)..].Prepend(subPaths[j].Skip(i)));
    }

    public override EdgePath Take(int i)
    {
        int j = 0;
        for (; j < subPaths.Length; j++)
        {
            var count = subPaths[j].Count;
            if (i < count) break;
            i -= count;
        }

        if (j == subPaths.Length)
            return this;

        return Concatenate(subPaths[..j].Append(subPaths[j].Take(i)));
    }

    private int? count;
    protected override IReadOnlyList<NamedEdgePath> GetUsedNamedEdgePaths() => subPaths.SelectMany(p => p.NamedEdgePaths).ToArray();

    public override int Count => count ??= subPaths.Sum(path => path.Count);

    public override bool IsEmpty => subPaths.Length == 0;

    protected internal override bool TryGetElementAt(ref int i, out Strip result)
    {
        foreach (var edgePath in subPaths)
        {
            if (edgePath.TryGetElementAt(ref i, out result))
                return true;
        }

        result = null;
        return false;
    }

    protected internal override string ToColoredString(int maxLength, int tail, Func<Strip, string> colorfulName)
    {
        if (IsEmpty) return "";
        var lengths = (from path in subPaths select path.ExpectedStringLength()).ToArray();
        
        int internalMaxLength = maxLength;
        int internalTail = tail;
        if (lengths.Sum() > maxLength) {
            var average1 = maxLength / subPaths.Length;
            var lengthLeftOver = lengths.Where(l => average1 > l).Sum(l => average1 - l);
            var tooLongSubPaths = lengths.Count(l => average1 < l);
            internalMaxLength = tooLongSubPaths > 0 ? average1 + lengthLeftOver / tooLongSubPaths : maxLength;
            internalTail = Math.Min(internalMaxLength / 3, tail);
        }

        return "(" + subPaths.ToCommaSeparatedString(path => path.ToColoredString(internalMaxLength, internalTail, colorfulName), " ") + ")";
    }

    protected internal override int ExpectedStringLength() => subPaths.Sum(path => path.ExpectedStringLength());
}

class NormalEdgePath : EdgePath
{
    /// <summary>
    /// This is a normal edge path as before. Every EdgePath is built like a tree consisting of EdgePaths, and the leaves are EdgePaths where normalEdgePath is not null
    /// </summary>
    private readonly Strip[] edges;

    public NormalEdgePath(params Strip[] strips)
    {
        edges = strips;
    }

    public NormalEdgePath(IEnumerable<Strip> strips) : this(strips.ToArray())
    {   }

    protected override EdgePath GetInverse()
    {
        Strip[] reversedNormalEdgePath = new Strip[edges.Length];
        for (int i = 0; i < edges.Length; i++)
            reversedNormalEdgePath[i] = edges[^(i + 1)].Reversed();
        return new NormalEdgePath(reversedNormalEdgePath);
    }

    public override IEnumerator<Strip> GetEnumerator() => ((IEnumerable<Strip>)edges).GetEnumerator();

    public override EdgePath Replace(Func<Strip,Strip> newEdges) =>
        new NormalEdgePath(edges.Select(newEdges).Where(e => e != null));

    public override EdgePath Replace(Func<Strip, EdgePath> newEdgePaths) =>
        EdgePath.Concatenate(edges.Select(newEdgePaths).Where(e => e != null));

    public override EdgePath Concat(EdgePath other) =>
        other switch
        {
            NormalEdgePath normalEdgePath => new NormalEdgePath(edges.Concat(normalEdgePath.edges)),
            ConjugateEdgePath => new NestedEdgePath(this, other),
            NestedEdgePath nestedEdgePath => new NestedEdgePath(nestedEdgePath.subPaths.Prepend(this)),
            _ => throw new NotImplementedException()
        };

    public override EdgePath Skip(int i) => i >= Count ? Empty : new NormalEdgePath(edges[i..]);

    public override EdgePath Take(int i) => i >= Count ? this : new NormalEdgePath(edges[..i]);

    protected override IReadOnlyList<NamedEdgePath> GetUsedNamedEdgePaths() => Array.Empty<NamedEdgePath>();

    public override int Count => edges.Length;

    public override bool IsEmpty => edges.Length == 0;

    protected internal override bool TryGetElementAt(ref int i, out Strip result)
    {
        var c = Count;
        if (i >= c)
        {
            i -= c;
            result = null;
            return false;
        }
        result = edges[i];
        return true;
    }

    protected internal override string ToColoredString(int maxLength, int tail, Func<Strip, string> colorfulName)
    {
        if (ExpectedStringLength() <= maxLength)
            return edges.ToCommaSeparatedString(colorfulName, " ");
        if (tail > maxLength)
            tail = maxLength;
        var initialCount = (maxLength - tail) / 2;
        var terminalCount = tail / 2;
        var initialEdges = edges[0..initialCount].ToCommaSeparatedString(colorfulName, " ");
        var terminalEdges = edges[^terminalCount..].ToCommaSeparatedString(colorfulName, " ");
        return $"{initialEdges} ... {terminalEdges}";
    }

    protected internal override int ExpectedStringLength() => Count * 2;
}

