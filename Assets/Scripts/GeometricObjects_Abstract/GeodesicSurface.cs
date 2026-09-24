using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public abstract class GeodesicSurface: Surface
{
    protected GeodesicSurface(string name, int genus, bool is2D, IEnumerable<Point> punctures = null) : base(name, genus, is2D, punctures){}

    public abstract Curve GetGeodesic(Point start, Point end, string name, GeodesicSurface surface = null);
    public abstract Curve GetGeodesic(TangentVector startVelocity, float length, string name, out float speedFactor, GeodesicSurface surface = null);

    public virtual Curve GetPathFromWaypoints(IEnumerable<Point> points, string name)
    {
        var pointArray = points.ToArray();
        var geodesicSegments = new List<Curve>(pointArray.Length - 1);
        for (int i = 0; i < pointArray.Length - 1; i++)
            geodesicSegments.Add(GetGeodesic(pointArray[i], pointArray[i + 1], name));
        
        return new ConcatenatedCurve(geodesicSegments, name, smoothed: true);
        // done: shouldn't smooth at the visual jump points, only at the actual concatenation points above. This could be done with "ignoreSubConcatenatedCurves", as we do, but only if we reintroduce nested concatenated curves. Currently, there is no distinction between the kinds of concatenations; Apart from the "angle jump" property of singular points, if it works
    }

    public virtual float Distance(Point startPoint, Point endPoint) => MathF.Sqrt(DistanceSquared(startPoint, endPoint));
    public virtual float DistanceBasic(Vector3 u, Vector3 v) => MathF.Sqrt(DistanceSquaredBasic(u, v));


    public virtual float DistanceSquared(Point startPoint, Point endPoint) =>
        startPoint.Positions.CartesianProduct(endPoint.Positions).Min(
            positions => DistanceSquaredBasic(positions.Item1, positions.Item2)      
        );
    
    public abstract float DistanceSquaredBasic(Vector3 startPoint, Vector3 endPoint);
    
    
    public virtual float CurveLength(Curve curve)
    {
        var res = 0.1f;
        while (res > curve.Length / 4) res /= 2;
        var (lengths, lastLength, lastTimeInterval) = ArclengthFromTimeList(curve, res);
        return lastLength + lengths[^1];
    }

    static (List<float>, float, float) ArclengthFromTimeList(Curve curve, float res)
    {
        if (curve.Surface is not GeodesicSurface surface)
        {
            Debug.LogError("The curve must be on a GeodesicSurface to calculate the arclength parameterization.");
            int normalIntervals = Mathf.FloorToInt(curve.Length / res);
            float remainingLength = curve.Length - normalIntervals * res;
            return (Enumerable.Range(0, normalIntervals).Select(i => i * res).ToList(), remainingLength, remainingLength);

        }
        var lengths = new List<float> { 0f }; 
        // l -> t: times[i] = l[i * res]

        float T = curve.Length;
        var l = 0f;
        var lastPoint = curve.StartPosition;
        for (float t = res; t < T; t += res)
        {
            var p = curve[t];
            l += surface.Distance(p, lastPoint);
            lastPoint = p;
            lengths.Add(l);
        }
        var lastLength = surface.Distance(curve.EndPosition, lastPoint);
        var lastTimeInterval = curve.Length - (lengths.Count - 1) * res;
        return (lengths, lastLength, lastTimeInterval);
    }
    
    public static Func<float, float> ArclengthFromTime(Curve curve, float res = 0.05f)
    {
        var (lengths, lastLength, lastTimeInterval) = ArclengthFromTimeList(curve, res);
        return ListToFunction(lengths, lastLength, lastTimeInterval, res);
    }

    public static Func<float, float> TimeFromArclength(Curve curve, float res = 0.05f)
    {
        var (lengths, lastLength, lastTimeInterval) = ArclengthFromTimeList(curve, res);
        var f = ListToInverseDerivative(lengths, lastLength, lastTimeInterval, res);
        return x => f(x).Item1;
    }
    
    public static (Func<float, (float, float)>, float) TimeFromArclengthParametrization(Curve curve, float res = 0.05f)
    {
        var (lengths, lastLength, lastTimeInterval) = ArclengthFromTimeList(curve, res);
        var f = ListToInverseDerivative(lengths, lastLength, lastTimeInterval, res);
        return (f, lastLength + lengths[^1]);
    }

    static Func<float, float> ListToFunction(List<float> lengths, float lastLength, float lastTimeInterval, float res) =>
        time =>
        {
            // todo: check
            int n = Mathf.FloorToInt(time / res);
            float deltaT = time - n * res;
            if (n < 0)
                return 0;
            if (n >= 0 && n < lengths.Count - 1)
                return lengths[n] + (lengths[n + 1] - lengths[n]) * deltaT / res;
            if (n == lengths.Count - 1)
                return lengths[n] + lastLength * deltaT / lastTimeInterval;
            return lastLength + lengths[^1];
        };

    static Func<float, (float, float)> ListToInverseDerivative(List<float> lengths, float lastLength,
        float lastTimeInterval, float res) =>
        length =>
        { // todo: check
            float dtdl = lastTimeInterval / lastLength;

            if (length >= lengths[^1])
                return (res * (lengths.Count - 1) + (length - lengths[^1]) * dtdl, dtdl);
            
            int i = 1;
            while (length > lengths[i]) 
                i++;
            
            dtdl = res / (lengths[i] - lengths[i - 1]);
            return (res * (i - 1) + (length - lengths[i - 1]) * dtdl, dtdl);
        };

    public static Curve ByArclength(Curve curve)
    {
        if (curve.Surface is not GeodesicSurface surface)
        {
            Debug.LogError("The curve must be on a GeodesicSurface to calculate the arclength parameterization.");
            return curve;
        }

        var res = 0.1f;
        while (res > curve.Length / 4) res /= 2;
        var (lengths, lastLength, lastTimeInterval) = ArclengthFromTimeList(curve, res);
        var ArclengthFromTime = ListToFunction(lengths, lastLength, lastTimeInterval, res);
        // t -> l
        var TimeFromArclength = ListToInverseDerivative(lengths, lastLength, lastTimeInterval, res);

        return new ParametrizedCurve(curve.Name + " by arclength", lastLength + lengths[^1], surface, DerivativeAt, 
            from jumpTime in curve.VisualJumpTimes select ArclengthFromTime(jumpTime)) { 
            // todo: Feature / Bug. These jump times are not accurate enough for displaying the curve probably...
            // When displaying the curve, one could display the original curve?
            Color = curve.Color
        };

        
        TangentVector DerivativeAt(float length)
        {
            var (t, dtdl) = TimeFromArclength(length);
            return dtdl * curve.DerivativeAt(t);
        }
    }

    /// <summary>
    /// Computes the times of intersection of two geodesics. The curves have to be Geodesics on this surface!
    /// </summary>
    /// <returns>null if there is no intersection (in finite time) or if the curves are not geodesics</returns>
    public abstract (float t1, float t2)? GetIntersectionOfGeodesics(Curve geodesic1, Curve geodesic2);

    public virtual (float t1, float t2)? GetIntersectionWithGeodesic(Curve curve1, float min1, float max1,
        Curve geodesic)
    {
        var geodesic1 = GetGeodesic(curve1[min1], curve1[max1], "approximating geodesic", this);
        var intersection = GetIntersectionOfGeodesics(geodesic1, geodesic);
        if (intersection == null) return null;
        var (t1, t2) = intersection.Value;
        return (min1 + t1, t2);
    }
    
    public virtual (float t1, float t2)? GetIntersection(Curve curve1, float min1, float max1, Curve curve2, float min2, float max2)
    {
        var geodesic1 = GetGeodesic(curve1[min1], curve1[max1], "approximating geodesic", this);
        var geodesic2 = GetGeodesic(curve2[min2], curve2[max2], "approximating geodesic", this);
        var intersection = GetIntersectionOfGeodesics(geodesic1, geodesic2);
        if (intersection == null) return null;
        var (t1, t2) = intersection.Value;
        return (min1 + t1, min2 + t2);
    }
    
    public virtual ((Vector3, Vector3), float) ClosestPosition(Point a, Point b) =>
        a.Positions.CartesianProduct(b.Positions).ArgMin(
            positions => DistanceSquaredBasic(positions.Item1, positions.Item2)        );
    
    public virtual (int, int, float) ClosestPositionIndices(Point a, Point b)
    {
        var otherPositions = b.Positions.ToArray();
        var (n, dist) = a.Positions.CartesianProduct(otherPositions).ArgMinIndex( 
            positions => DistanceSquaredBasic(positions.Item1, positions.Item2)
        );
        return (n / otherPositions.Length, n % otherPositions.Length, dist);
    }
}