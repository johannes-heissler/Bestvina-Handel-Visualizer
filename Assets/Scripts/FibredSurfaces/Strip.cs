
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using QuikGraph;
using UnityEngine;
using FibredGraph = QuikGraph.UndirectedGraph<Junction, UnorientedStrip>;


public abstract class Strip: IEdge<Junction>, IDrawable
{
    public readonly FibredSurface fibredSurface;
    public FibredGraph graph => fibredSurface.graph;
    
    protected Strip(FibredSurface fibredSurface)
    {
        this.fibredSurface = fibredSurface;
    }
    
    public abstract Curve Curve { get; set; }

    /// <summary>
    /// f maps this strip into the fibred Surface, and it traces out the edgePath g(this) = edgePath;
    /// </summary>
    public abstract EdgePath EdgePath { get; set; }
    public abstract float OrderIndexStart { get; set; }
    public abstract float OrderIndexEnd { get; set; }
    
    [CanBeNull] public virtual Strip Dg => EdgePath.FirstOrDefault();


    public abstract Junction Target { get; set; }
    public abstract Junction Source { get; set; }
    
    public abstract UnorientedStrip UnderlyingEdge { get; }
    public abstract string Name { get; set; }
    public abstract Color Color { get; set; }
    IDrawable IDrawable.Copy() => Copy();
    public string ColorfulName => ((IDrawable)this).GetColorfulName();

    public void AssignNewColor() => Color = fibredSurface.NextEdgeColor();

    public virtual Junction OtherVertex(Junction vertex) => vertex == Source ? Target : Source;
    
    public abstract Strip Reversed();

    public EdgePoint this[int i] => new(this, i);

    public override string ToString() => Name; // $"{Name}: {Source} -> {Target} with g({Name}) = {EdgePath.ToString(250, 10)}";
    
    public string ToColorfulString()
    {
        return $"{ColorfulName}: {Source.ColorfulName} -> {Target.ColorfulName} with g({ColorfulName}) = {EdgePath.ToColorfulString(300, 15)}";
    }

    public static int SharedInitialSegment(IReadOnlyList<Strip> strips)
    {
        IEnumerable<Strip> initialSegment = strips[0].EdgePath;
        foreach (var strip in strips.Skip(1)) 
            initialSegment = initialSegment.Zip(strip.EdgePath, (a, b) => Equals(a, b) ? a : null);

        int i = initialSegment.TakeWhile(strip => strip != null).Count();
        if (i == 0) Debug.LogError("The edges do not have a common initial segment.");
        return i;

    }

    public virtual UnorientedStrip CopyUnoriented(FibredSurface fibredSurface = null, Curve curve = null, Junction source = null,
        Junction target = null, EdgePath edgePath = null, string name = null, float? orderIndexStart = null, float? orderIndexEnd = null)
    {
        var res = new UnorientedStrip(curve ?? Curve.Copy(), source ?? Source, target ?? Target, edgePath ?? EdgePath,
            fibredSurface ?? this.fibredSurface, orderIndexStart ?? this.OrderIndexStart, orderIndexEnd ?? this.OrderIndexEnd);
        if (name != null) res.Name = name;
        return res;
    }
    
    public abstract Strip Copy(FibredSurface fibredSurface = null, Curve curve = null, Junction source = null,
        Junction target = null, EdgePath edgePath = null, string name = null, float? orderIndexStart = null, float? orderIndexEnd = null);
}

public class UnorientedStrip : Strip
{
    public override Curve Curve { get; set; }
    public override EdgePath EdgePath { get; set; }
    public override UnorientedStrip UnderlyingEdge => this;

    public override float OrderIndexEnd { get; set; }
    public override float OrderIndexStart { get; set; }

    public UnorientedStrip([NotNull] Curve curve, [NotNull] Junction source, [NotNull] Junction target, EdgePath edgePath,
        [NotNull] FibredSurface fibredSurface, float orderIndexStart, float orderIndexEnd, bool newColor = false, bool newName = false, bool addToGraph = false) : base(fibredSurface)
    {
        Curve = curve;
        this.source = source;
        this.target = target;
        EdgePath = edgePath ?? new NormalEdgePath(this);
        this.OrderIndexEnd = orderIndexEnd;
        this.OrderIndexStart = orderIndexStart;
        if (newColor)
            Color = fibredSurface.NextEdgeColor();
        if (newName)
            Name = fibredSurface.NextEdgeName();
        if (addToGraph)
            fibredSurface.graph.AddVerticesAndEdge(this);
    }

    private Junction target;
    public override Junction Target
    {
        get => target;
        set {
            graph.RemoveEdge(UnderlyingEdge);
            target = value;
            graph.AddVerticesAndEdge(UnderlyingEdge);
        }
    }

    private Junction source;
    public override Junction Source
    {
        get => source;
        set { 
            graph.RemoveEdge(UnderlyingEdge);
            source = value; 
            // TODO: BUG in QuikGraph: Directly after this line, fibredSurface.peripheralSubgraph.ContainsEdge(e) != fibredSurface.peripheralSubgraph.Edges.Contains(e) !!
            // This is not the case if the target is changed instead, even though the code is identical. I cannot figure out a MWE.
            // Doesn't matter, this was undefined behavior anyways, because we only update the vertices for the graph (here), but not the subgraph.
            graph.AddVerticesAndEdge(UnderlyingEdge);
        }
    }
    
    public override string Name
    {
        get => Curve.Name;
        set {
            if (value.Any(char.IsUpper)) Debug.LogError("An unoriented strip should have a lowercase name!");
            Curve.Name = value.ToLower();
            if (reversed != null) reversed.Curve.Name = value + "'";
        }
    }

    public override Color Color
    {
        get => Curve.Color;
        set {
            Curve.Color = value;
            if (reversed != null) reversed.Curve.Color = value;
        }
    }

    private ReverseStrip reversed;
    public override Strip Reversed() => reversed ??= new ReverseStrip(this);

    public override Strip Copy(FibredSurface fibredSurface = null, Curve curve = null, Junction source = null,
        Junction target = null, EdgePath edgePath = null, string name = null, float? orderIndexStart = null, float? orderIndexEnd = null)
        => CopyUnoriented(fibredSurface, curve, source, target, edgePath, name, orderIndexStart, orderIndexEnd);
    
    public void ReplaceWithInverseEdge()
    {
        var name = Name;
        Curve = Curve.Reversed();
        Name = name;
        (source, target) = (target, source);
        (OrderIndexStart, OrderIndexEnd) = (OrderIndexEnd, OrderIndexStart);
        EdgePath = EdgePath.Inverse;
        foreach (var strip in graph.Edges) 
            strip.EdgePath = strip.EdgePath.Replace((UnorientedStrip e) => e == this ? Reversed() : e);
    }
}

public class ReverseStrip: Strip
{
    public override UnorientedStrip UnderlyingEdge { get; }
    
    public override Curve Curve
    {
        get => UnderlyingEdge.Curve.Reversed();
        set
        {
            var name = Name;
            var underlyingCurve = value.Reversed(); 
            // if (underlyingCurve.Name.EndsWith("'")) 
            //     underlyingCurve.Name = underlyingCurve.Name[..^1];
            UnderlyingEdge.Curve = underlyingCurve;
            Name = name;
        }
    }

    public override string Name
    {
        get => UnderlyingEdge.Name.ToUpper();
        set
        {
            if (value.Any(char.IsLower)) Debug.LogError("A reverse strip should have an uppercase Name!");
            UnderlyingEdge.Name = value.ToLower();
        }
    }

    public override Color Color
    {
        get => UnderlyingEdge.Color;
        set => UnderlyingEdge.Color = value;
    }

    // "<s>" + UnderlyingEdge.Name + "</s>"   (strikethrough in RichText; workaround for overbar; use for fancy display?)
    // For the moment, we just use the name of the underlying edge in UPPERCASE.

    public ReverseStrip(UnorientedStrip underlyingEdge): base(underlyingEdge.fibredSurface)
    {
        this.UnderlyingEdge = underlyingEdge;
    }

    public override Junction Source
    {
        get => UnderlyingEdge.Target;
        set => UnderlyingEdge.Target = value;
    }

    public override Junction Target
    {
        get => UnderlyingEdge.Source;
        set => UnderlyingEdge.Source = value;
    }

    public override EdgePath EdgePath
    {
        get => UnderlyingEdge.EdgePath.Inverse;
        set => UnderlyingEdge.EdgePath = value.Inverse;
    }

    public override float OrderIndexStart
    {
        get => UnderlyingEdge.OrderIndexEnd;
        set => UnderlyingEdge.OrderIndexEnd = value;
    }

    public override float OrderIndexEnd
    {
        get => UnderlyingEdge.OrderIndexStart;
        set => UnderlyingEdge.OrderIndexStart = value;
    }

    public override Strip Reversed() => UnderlyingEdge;

    public override Strip Copy(FibredSurface fibredSurface = null, Curve curve = null,
        Junction source = null, Junction target = null, EdgePath edgePath = null, string name = null, float? orderIndexStart = null, float? orderIndexEnd = null)
    {
        return UnderlyingEdge.CopyUnoriented(
            fibredSurface: fibredSurface,
            curve: curve?.Reversed(),
            source: target,
            target: source,
            edgePath: edgePath?.Inverse,
            name: name?.ToLower(),
            orderIndexStart: orderIndexEnd,
            orderIndexEnd: orderIndexStart
        ).Reversed();
    }

    public override Junction OtherVertex(Junction vertex) => UnderlyingEdge.OtherVertex(vertex);
}