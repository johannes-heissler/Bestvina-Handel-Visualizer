using System.Collections.Generic;
using System.Linq;
using UnityEngine;


public partial class Curve: IDrawnsformable<Curve>
{
    public readonly int id;
    private static int _lastId;
    protected Curve()
    {
        id = _lastId++;
    }
    
    private Color? color;

    public virtual Color Color
    {
        get => color ?? DefaultColor;
        set => color = value;
    }

    protected virtual Color DefaultColor => colors[id % colors.Count];

    public static readonly IReadOnlyList<Color> colors = new Color[]
    {
        new Color32(20, 71, 255, 255),
        new Color32(233, 30, 99, 255),
        new Color32(255, 193, 7, 255),
        new Color32(174, 51, 255, 255),
        new Color32(89, 128, 212, 255),
        new Color32(255, 123, 0, 255),
        new Color32(108, 108, 108, 255),
        new Color32(47, 196, 107, 255),
    };
}

public partial class TransformedCurve
{
    protected override Color DefaultColor => curve.Color;
}

public partial class ReverseCurve
{
    protected override Color DefaultColor => curve.Color;
}

public partial class ShiftedCurve
{
    protected override Color DefaultColor => curve.Color;
}

public partial class ConcatenatedCurve
{
    protected override Color DefaultColor => segments.First().Color;
}

public partial class ModelSurfaceSide
{
    protected override Color DefaultColor => curve.Color;
}