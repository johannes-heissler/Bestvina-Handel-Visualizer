using System;
using JetBrains.Annotations;
using UnityEngine;

public partial class ModelSurfaceSide: Curve
{
    public readonly Curve curve;
    public readonly bool rightIsInside;
    public readonly float angle;
    public int vertexIndex = -1;
    public ModelSurfaceSide other;
    private ModelSurfaceSide reverseSide;
    

    public ModelSurfaceSide(Curve curve, bool rightIsInside)
    {
        this.curve = curve;
        this.rightIsInside = rightIsInside;
        angle = MathF.Atan2(curve.StartVelocity.vector.y, curve.StartVelocity.vector.x);
        if (curve.Surface is not ModelSurface)
            Debug.LogWarning($"The surface of a curve that we promote to a ModelSurfaceSide is not a ModelSurface: {curve.Name} has surface {curve.Surface.Name}");
    }

    public ModelSurfaceSide(ModelSurface.PolygonSide side, GeometryType geometryType, ModelSurface surface) :
        this(
            ModelSurface.baseGeometrySurfaces[geometryType]
                .GetGeodesic( side.start, side.end, side.label, surface),
            side.rightIsInside
        )
    { }

    [CanBeNull] private string _name;
    public override string Name
    {
        get => _name ?? curve.Name;// + '\'';
        set => _name = value;
    }
    public override float Length => curve.Length;
    public override Point EndPosition => curve.EndPosition;
    public override Point StartPosition => curve.StartPosition;
    public override TangentVector EndVelocity => curve.EndVelocity;
    public override TangentVector StartVelocity => curve.StartVelocity;
    public override Surface Surface => curve.Surface;

    public override Point ValueAt(float t) => new ModelSurfaceBoundaryPoint(this, t);

    public override TangentVector DerivativeAt(float t) => new(ValueAt(t), curve.DerivativeAt(t).vector);

    public void AddOther(ModelSurfaceSide newOtherSide)
    {
        if (other == newOtherSide) return;
        if (other != null)
            throw new Exception("Check your polygon! Three sides have the same label.");
        other = newOtherSide;
        other.other = this;
        other.Color = Color;
        if (other.rightIsInside == rightIsInside)
            Debug.Log("Check your polygon! Two sides with the same label have the surface on the same side." +
                      " This might mean that it is not orientable?");
        Debug.Log($"side {Name} (at {StartVelocity} got other side {other.Name} at {other.StartVelocity} with color {Color}");
    }
    
    public override Curve Reversed() => ReverseModelSide();
    
    /// <summary>
    /// The closest point on either this or the other curve.
    ///
    /// NOT TRUE ANYMORE:
    /// The Position (i.e. Positions.First()) will be the on the curve out of the two identified ones that is closest
    /// (this is the SideCurve that is returned inside the Point which is the ModelSurfaceBoundaryPoint).
    /// </summary>
    public override float GetClosestPoint(Vector3 point)
    {
        float t = curve.GetClosestPoint(point);
        float t2 = other.curve.GetClosestPoint(point);
        return curve[t].DistanceSquared(point) < other.curve[t2].DistanceSquared(point) ? t : t2;
    }

    public ModelSurfaceSide ReverseModelSide()
    {
        if (reverseSide != null) // this method will be called in AddOther from below in other.Reverse()!
            return reverseSide;
        reverseSide = new(curve.Reversed(), !rightIsInside);
        reverseSide.reverseSide = this;
        reverseSide.Color = Color;
        reverseSide.AddOther(other.ReverseModelSide());
        
        return reverseSide;
    }

    public override Curve ApplyHomeomorphism(Homeomorphism homeomorphism)
    {
        if (homeomorphism.isIdentity)
            return this;
        var transformedCurve = curve.ApplyHomeomorphism(homeomorphism);
        transformedCurve.Color = Color; // if this has a set color (not DefaultColor), then we have to copy it to the result
        if (homeomorphism.target is not ModelSurface) 
            return transformedCurve;
        
        bool orientationReversing = homeomorphism.df(StartPosition.Position).Determinant() < 0;
        
        var result = new ModelSurfaceSide(transformedCurve, 
            orientationReversing ? rightIsInside : !rightIsInside);
        result.other = new ModelSurfaceSide(other.curve.ApplyHomeomorphism(homeomorphism),
            orientationReversing ? other.rightIsInside : !other.rightIsInside) { Color = other.Color };
        result.other.other = result;
        return result;
    }

    public override Curve Copy()
    {
        var copy = new ModelSurfaceSide(curve.Copy(), rightIsInside) { Name = Name, Color = Color }; // in case the color was changed 
        var otherCopy = new ModelSurfaceSide(other.curve.Copy(), other.rightIsInside) { Name = other.Name };
        copy.AddOther(otherCopy); // this will also set the color
        return copy;
    }

    private Homeomorphism deckTransformation;
    public Homeomorphism DeckTransformation()
    {
        if (deckTransformation != null) return deckTransformation;
        if (curve.Surface is not ModelSurface modelSurface) throw new Exception("Model surface expected");
        string labelAddition = " [" + Name + "]";
        switch (other.curve)
        {
            case HyperbolicGeodesicSegment geodesic:
                if (curve is not HyperbolicGeodesicSegment geodesic2) throw new Exception("Geodesic expected");
                // the Möbius transformation saved in geodesic sends the usual geodesic i e^t to the geodesic
                // thus the Möbius transformation sending the first geodesic to the second is
                // geodesic2.MöbiusTransformation * geodesic.MöbiusTransformation.Inverse;
                var a = geodesic2.α * geodesic.δ - geodesic2.β * geodesic.γ;
                var b = geodesic2.β * geodesic.α - geodesic2.α * geodesic.β;
                var c = geodesic2.γ * geodesic.δ - geodesic2.δ * geodesic.γ;
                var d = geodesic2.δ * geodesic.α - geodesic2.γ * geodesic.β;
                deckTransformation = HyperbolicPlane.MöbiusTransformation(
                    a, b, c, d, modelSurface, modelSurface
                );
                deckTransformation.target = modelSurface.Copy(modelSurface.Name + labelAddition, deckTransformation, labelAddition);
                return deckTransformation;
            case FlatGeodesicSegment line: 
                if (curve is not FlatGeodesicSegment line2) throw new Exception("Geodesic expected");
                deckTransformation = EuclideanPlane.Isometry(
                    line.StartVelocity,
                    line2.StartVelocity,
                    modelSurface, modelSurface
                );
                deckTransformation.target = modelSurface.Copy(modelSurface.Name + labelAddition, deckTransformation, labelAddition);
                return deckTransformation;
            default:
                throw new NotImplementedException(); // implement Klein model for hyperbolic space?
        }
        
    }

    public float Rightness(Vector3 position) =>
        curve switch
        {
            HyperbolicGeodesicSegment geodesic => geodesic.Rightness(position),
            FlatGeodesicSegment line => line.Rightness(position),
            _ => throw new NotImplementedException()
        };
}