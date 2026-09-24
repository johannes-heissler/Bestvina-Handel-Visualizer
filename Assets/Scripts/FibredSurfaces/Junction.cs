using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class Junction: PatchedDrawnsformable, IEquatable<Junction>
{
    public readonly FibredSurface fibredSurface;
    
    /// <summary>
    /// f maps this junction into the image junction, i.e. g(this) = image;
    /// </summary>
    public Junction image;

    private static int _lastId = 0;
    private readonly int id = _lastId++;
    
    public Junction(FibredSurface fibredSurface, IEnumerable<IDrawnsformable> drawables, string name = null, Junction image = null, Color? color = null, bool addToGraph = false) :
        base(drawables)
    {
        this.fibredSurface = fibredSurface;
        this.image = image ?? this;
        Color = color ?? fibredSurface?.NextVertexColor() ?? FibredSurface.vertexColors[id % FibredSurface.vertexColors.Count];
        Name = name ?? fibredSurface?.NextVertexName() ?? $"v{id}";
        if (addToGraph && fibredSurface != null)
            fibredSurface.graph.AddVertex(this);
    }
    
    public Junction(FibredSurface fibredSurface, IDrawnsformable drawable, string name = null, Junction image = null, Color? color = null, bool addToGraph = false) : 
        this(fibredSurface, new[] {drawable}, name, image, color, addToGraph)
    { }

    public Point Position => Patches.FirstOrDefault(v => v is Point) as Point;

    public string ColorfulName => ((IDrawable)this).GetColorfulName();

    public Junction Copy(FibredSurface fibredSurface = null, string name = null, Junction image = null, Color? color = null, IEnumerable<IDrawnsformable> patches = null) =>
        new(
            fibredSurface ?? this.fibredSurface,
            patches ?? from patch in Patches select patch.Copy(),
            name ?? Name,
            image ?? this.image,
            color ?? Color
        );

    public void AddDrawable(IDrawnsformable drawable)
    {
        patches.Add(drawable);
        drawable.Color = Color;
    }
    
    public void AddCurveAsPartOfJunction(Curve curve)
    {
        AddDrawable(curve);
        patches.RemoveAll(drawable => Equals(curve.StartPosition, drawable) || Equals(curve.EndPosition, drawable));
    }

    public override string ToString() => Name;

    public bool Equals(Junction other) => id == other?.id;

    public override bool Equals(object obj) => obj switch
    {
        Junction other => id == other.id,
        _ => false
    };

    public override int GetHashCode() => id;

    public string ToColorfulString(IReadOnlyList<Gate<Junction>> gates = null)
    {
        if (gates == null)
        {
            return
                $"{ColorfulName} with edges {FibredSurface.StarOrdered(this).Select(e => e.ColorfulName).ToCommaSeparatedString()}";
        }

        var starOrdered = FibredSurface.StarOrdered(this).ToArray();
        if (starOrdered.Length == 0) return ColorfulName;
        var firstGate = gates.First(gate => gate.Edges.Contains(starOrdered[^1]));
        var starOrderedShifted = starOrdered.CyclicShift(edge => !firstGate.Edges.Contains(edge));

        Gate<Junction> currentGate = null;
        var res = "";
        foreach (var edge in starOrderedShifted)
        {
            if (currentGate?.Edges.Contains(edge) != true)
            {
                currentGate = gates.First(gate => gate.Edges.Contains(edge));
                res += "}, {" + edge.ColorfulName;
            }
            else
                res += ", " + edge.ColorfulName;
        }
        return ColorfulName + " with gates " + res[3..] + "}";
    }
}