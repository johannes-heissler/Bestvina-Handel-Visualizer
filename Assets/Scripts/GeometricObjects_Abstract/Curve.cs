using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Vector3 = UnityEngine.Vector3;


public abstract partial class Curve : ITransformable<Curve> // even IDrawnsformable in other part.
{
    public abstract string Name { get; set; }

    public abstract float Length { get; }

    public virtual Point StartPosition => ValueAt(0);
    public virtual Point EndPosition => ValueAt(Length);
    public virtual TangentVector StartVelocity => DerivativeAt(0);
    public virtual TangentVector EndVelocity => DerivativeAt(Length);

    public abstract Surface Surface { get; }
    public virtual IEnumerable<float> VisualJumpTimes => Enumerable.Empty<float>();

    const float jumpTimeTolerance = 0.2f;
    public virtual IEnumerable<(float, ModelSurfaceBoundaryPoint)> VisualJumpPoints
    {
        get
        {
            foreach (float t in VisualJumpTimes)
            {
                var pt = this[t];
                if (pt is ModelSurfaceBoundaryPoint boundaryPoint)
                {
                    yield return (t, boundaryPoint);
                    continue;
                }

                var point = Surface.ClampPoint(pt, jumpTimeTolerance);
                if (point is not ModelSurfaceBoundaryPoint sidePoint)
                {
                    Debug.LogError($"In the curve {Name} of type {GetType()}, the point at time {t}, {pt} is saved as a visual jump point, but the point is not clamped to model surface boundary points, but to {point}!");
                    continue;
                }
                var jumpPoint = FindIntersectionPointWithSideCloseTo(t, sidePoint.side);
                if (jumpPoint.HasValue)
                    yield return jumpPoint.Value;
            }
        }
    }

    protected virtual (float, ModelSurfaceBoundaryPoint)? FindIntersectionPointWithSideCloseTo(float t, ModelSurfaceSide side)
    {
        // shouldn't happen unless VisualJumpTimes is already empty (atm this could happen in TransformedCurve, but there it doesn't matter)
         if (Surface is not ModelSurface modelSurface)
             return null; 
         
        var startVelocity = DerivativeAt(Mathf.Clamp(t, 0, Length));
        

        var geodesicForward = modelSurface.GeometrySurface.GetGeodesic(startVelocity, jumpTimeTolerance, "forward test geodesic", out var speedFactor);
        var intersectionTimesForwardThisSide = modelSurface.GeometrySurface.GetIntersectionOfGeodesics(geodesicForward, side.curve);
        var intersectionTimesForwardOtherSide = modelSurface.GeometrySurface.GetIntersectionOfGeodesics(geodesicForward, side.other.curve);
        var geodesicBackward = modelSurface.GeometrySurface.GetGeodesic(startVelocity,- jumpTimeTolerance,"backward test geodesic", out var speedFactorBackwards);
        var intersectionTimesBackwardThisSide = modelSurface.GeometrySurface.GetIntersectionOfGeodesics(geodesicBackward, side.curve);
        var intersectionTimesBackwardOtherSide = modelSurface.GeometrySurface.GetIntersectionOfGeodesics(geodesicBackward, side.other.curve);
        
        if (!speedFactor.ApproximateEquals(speedFactorBackwards))
            Debug.LogError($"The speed factor of the forward and backward geodesics  with start vector {startVelocity} disagree: {speedFactor} and {speedFactorBackwards}!");
        
        if (intersectionTimesForwardThisSide.HasValue)
            return (t + intersectionTimesForwardThisSide.Value.t1 / speedFactor, new ModelSurfaceBoundaryPoint(side, intersectionTimesForwardThisSide.Value.t2));
        if (intersectionTimesForwardOtherSide.HasValue)
            return (t + intersectionTimesForwardOtherSide.Value.t1 / speedFactor, new ModelSurfaceBoundaryPoint(side.other, intersectionTimesForwardOtherSide.Value.t2));
        if (intersectionTimesBackwardThisSide.HasValue)
            return (t - intersectionTimesBackwardThisSide.Value.t1 / speedFactorBackwards, new ModelSurfaceBoundaryPoint(side.other, intersectionTimesBackwardThisSide.Value.t2));
        if (intersectionTimesBackwardOtherSide.HasValue)         
            return (t - intersectionTimesBackwardOtherSide.Value.t1 / speedFactorBackwards, new ModelSurfaceBoundaryPoint(side, intersectionTimesBackwardOtherSide.Value.t2));


        Debug.LogWarning($"In the curve {Name} of type {GetType()}, the point at time {t}, is close to {side}, but the intersection with the side is empty!");
        return null;
    }

    public virtual IEnumerable<(string, bool)> SideCrossingWord =>
        VisualJumpPoints.Select(p =>
        {
            var side = p.Item2.side;
            return (((IDrawable) side).ColorfulName, side.Surface is ModelSurface surface && !surface.sides.Contains(side));
        }); // this is a bit hacky. One should rather save this in form of the (inverse) marking into the strips, i.e. for each strip save the word in the "original" graph that its embedding isotopes to, which is equivalent to the Side crossing word, because the "original" graph is the dual of the model surface sides (not exactly -> todo) 

    public virtual Point this[float t] => ValueAt(t);

    public abstract Point ValueAt(float t);
    public abstract TangentVector DerivativeAt(float t);

    public virtual Curve Concatenate(Curve curve) => new ConcatenatedCurve(new Curve[] { this, curve });

    protected Curve reverseCurve;

    public virtual Curve Reversed() => reverseCurve ??= new ReverseCurve(this);

    /// <summary>
    /// Get the closest point in spline segment. Used for projection
    /// Copied and slightly modified from Dreamteck Splines
    /// </summary>
    private float GetClosestTimeInternal(int iterations, Point point, float start, float end, int slices)
    {
        // TODO: Performance. This is responsible for most of the time when the program freezes. DistanceSquared optimizes over all positions, this creates extra overhead
        if (start >= end)
            throw new ArgumentException("Start time must be before the end time");
        while (--iterations >= 0)
        {
            var closestTime = 0.0f;
            var closestDistance = Mathf.Infinity;
            var tick = (end - start) / slices;
            var t = start;
            while (true)
            {
                float dist = point.DistanceSquared(ValueAt(t));
                if (dist < closestDistance)
                {
                    closestDistance = dist;
                    closestTime = t;
                }

                if (t >= end) break;
                t = Mathf.Clamp(t + tick, start, end);
            }

            start = Mathf.Clamp(closestTime - tick, start, end);
            end = Mathf.Clamp(closestTime + tick, start, end);
        }

        float startDist = point.DistanceSquared(ValueAt(start));
        float endDist = point.DistanceSquared(ValueAt(end));
        if (startDist < endDist) return start;
        if (endDist < startDist) return end;
        return (start + end) / 2;
    }

    private const int GetClosestPointIterations = 4;

    public virtual float GetClosestPoint(Vector3 point) =>
        GetClosestTimeInternal(GetClosestPointIterations, point, 0, Length, 10);

    public virtual Curve ApplyHomeomorphism(Homeomorphism homeomorphism) =>
        homeomorphism.isIdentity ? this : new TransformedCurve(this, homeomorphism);

    /// <summary>
    /// A right-handed basis at the point on the curve at time t, with the tangent vector as the first vector and the normal vector of the surface as the third vector. Thus, the second vector points "to the right" of the curve.
    /// </summary>
    public virtual TangentSpace BasisAt(float t)
    {
        var (point, tangent) = DerivativeAt(t);
        var (_, basis) = Surface.BasisAt(point);
        return new TangentSpace(
            point,
            new Matrix3x3(
                tangent,
                Vector3.Cross(basis.c, tangent),
                basis.c
            )
        );
    }

    protected const float restrictTolerance = 0.0001f;

    /// <summary>
    /// Restrict the curve to a subinterval. This is computed with a tolerance: If start > -ε and end &lt; Length + ε and start &lt; end + ε, the restriction is valid. Else, an ArgumentOutOfRangeException is thrown.
    /// If start &lt; ε and end > Length - ε, the original curve is returned.
    /// </summary>
    /// <param name="start"></param>
    /// <param name="end"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public virtual Curve Restrict(float start, float? end = null)
    {
        var stop = end ?? Length;
        if (start < -restrictTolerance || stop > Length + restrictTolerance || start > stop + restrictTolerance)
            throw new ArgumentOutOfRangeException(
                $"Invalid restriction in curve {this}: {start} to {end} for length {Length}");
        if (stop > Length)
            stop = Length;
        if (start < 0)
            start = 0;
        if (start < restrictTolerance && stop > Length - restrictTolerance)
            return this;
        return new RestrictedCurve(this, start, stop) { Color = Color };
    }

    public abstract Curve Copy();

    public Curve AdjustStartVector(TangentVector newStartVector, float cutoff)
    {
        var newCurve = Restrict(cutoff);
        var end = newCurve.StartVelocity;
        var interpolatingSegment = new SplineSegment(
            newStartVector,
            end,
            cutoff,
            Surface,
            Name + " adjusted start vector"
        );
        var result = interpolatingSegment.Concatenate(newCurve);
        result.Color = Color;
        result.Name = Name;
        return result;
    }
}