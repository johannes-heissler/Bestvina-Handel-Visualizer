using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;


public partial class ModelSurface : GeodesicSurface
{
    const float tau = 2 * MathF.PI;

    #region local types

    public record PolygonSide
    {
        private static IEnumerator<Color> colors = Curve.colors.EndlessLoop().GetEnumerator();

        public readonly string label;
        public readonly Vector2 start, end;
        public readonly bool rightIsInside;
        public readonly Color color;

        public PolygonSide(string label, Vector2 start, Vector2 end, bool rightIsInside, Color color)
        {
            this.label = label;
            this.start = start;
            this.end = end;
            this.rightIsInside = rightIsInside;
            this.color = color;
        }

        public static Color NextColor() => colors.MoveNext() ? colors.Current : Color.white;

        public PolygonSide ApplyHomeomorphism(Homeomorphism homeomorphism, string labelAddition = "") =>
            new(
                label + labelAddition,
                homeomorphism.f(start),
                homeomorphism.f(end),
                rightIsInside,
                color
            );
    }

    #endregion

    public readonly GeometryType geometryType;
    public readonly List<ModelSurfaceSide> sides = new();

    public IEnumerable<ModelSurfaceSide> AllSideCurves => sides.Concat(
        from side in sides
        select side.other
    );

    public readonly List<ModelSurfaceVertex> vertices = new();
    public readonly List<PolygonSide> sidesAsParameters;

    public static readonly IReadOnlyDictionary<GeometryType, GeodesicSurface> baseGeometrySurfaces = new Dictionary<GeometryType, GeodesicSurface>
    {
        [GeometryType.Flat] = new EuclideanPlane(),
        [GeometryType.HyperbolicDisk] = new HyperbolicPlane(diskModel: true),
        [GeometryType.HyperbolicPlane] = new HyperbolicPlane(diskModel: false),
        [GeometryType.Spherical] = null // todo?
    };

    public override Vector3 MinimalPosition { get; }
    public override Vector3 MaximalPosition { get; }

    public GeodesicSurface GeometrySurface => baseGeometrySurfaces[geometryType]; // Euclidean or Hyperbolic plane.

    public ModelSurface Copy(string name) => new(name, Genus, punctures.Count, geometryType, sidesAsParameters);

    /// <summary>
    /// 
    /// </summary>
    /// <param name="name"></param>
    /// <param name="homeomorphism">A homeomorphism of the base surface. The geometry type is set depending on if this is HyperbolicPlane etc.</param>
    /// <param name="labelAddition"></param>
    /// <returns></returns>
    public ModelSurface Copy(string name, Homeomorphism homeomorphism, string labelAddition = "") =>
        new(name,
            Genus,
            punctures.Count,
            geometryType: homeomorphism.target switch
            {
                HyperbolicPlane { diskModel: true } => GeometryType.HyperbolicDisk,
                HyperbolicPlane { diskModel: false } => GeometryType.HyperbolicPlane,
                EuclideanPlane => GeometryType.Flat,
                _ => geometryType
            },
            (
                from sideParameter in sidesAsParameters
                select sideParameter.ApplyHomeomorphism(homeomorphism, labelAddition)
            ).ToList()
        );

    public ModelSurface(string name,
        int genus,
        int punctures,
        GeometryType geometryType,
        List<PolygonSide> identifiedSides
    ) : base(name, genus, true)
    {
        this.geometryType = geometryType;
        sidesAsParameters = identifiedSides;

        foreach (var side in identifiedSides)
        {
            var newSide = new ModelSurfaceSide(side, geometryType, this);
            newSide.Color = side.color;
            var oldSide = this.sides.FirstOrDefault(existingSide => existingSide.Name == newSide.Name);
            if (oldSide != null)
            {
                oldSide?.AddOther(newSide);
                newSide.Name += "*";
            }
            else
                sides.Add(newSide);
        }

        if (sides.Any(side => side.other == null))
            throw new Exception("Check your polygon! Some sides are not paired.");

        #region find vertices

        var polygonVertices = new List<(Vector3, List<ModelSurfaceSide>)>();

        void AddDirectedEdgeToAStar(ModelSurfaceSide side)
        {
            var curve = side.curve;
            var position = curve.StartPosition.Position;
            var succeeded = false;
            foreach (var (pos, vertexStar) in polygonVertices)
            {
                if (!pos.ApproximatelyEquals(position)) continue;
                vertexStar.Add(side);
                succeeded = true;
                break;
            }

            if (!succeeded)
                polygonVertices.Add((curve.StartPosition.Position, new() { side }));
        }

        foreach (var curve in this.AllSideCurves)
        {
            AddDirectedEdgeToAStar(curve);
            AddDirectedEdgeToAStar(curve.ReverseModelSide());
        }

        foreach ((_, List<ModelSurfaceSide> identifiedGeodesics) in polygonVertices)
        {
            identifiedGeodesics.Sort((a, b) => a.angle.CompareTo(b.angle));
            // increasing index means turning left
        }

        MinimalPosition = new(polygonVertices.Min(pair => pair.Item1.x), polygonVertices.Min(pair => pair.Item1.y)); // todo: This is not correct for hyperbolic sides, thus hovering is not detected over some surfaces!!
        MaximalPosition = new(polygonVertices.Max(pair => pair.Item1.x), polygonVertices.Max(pair => pair.Item1.y));
        

        while (true)
        {
            var vertexIndex = vertices.Count;

            if (vertexIndex > 4 * sides.Count)
                throw new Exception("what the heck?");
            var oldEdge = polygonVertices
                .Select(polygonVertex => polygonVertex.Item2.FirstOrDefault(edge => edge.vertexIndex == -1))
                .FirstOrDefault(edge => edge != null);

            if (oldEdge == null) // all edges have been assigned to a vertex
                break;


            var vertex = new ModelSurfaceVertex();
            vertices.Add(vertex);


            while (true)
            {
                bool TryAddEdgeToVertex(ModelSurfaceSide modelSurfaceSide)
                {
                    if (modelSurfaceSide.vertexIndex == vertexIndex)
                    {
                        if (modelSurfaceSide != vertex.boundaryCurves[0])
                            throw new Exception(
                                "Check your polygon! The supposed cyclic order of edges closed back in on itself, but not where it should.");
                        return false; // we have come full circle, this vertex is finished
                    }

                    if (modelSurfaceSide.vertexIndex != -1)
                        throw new Exception(
                            "Check your polygon! Some edges seem to be assigned to more than one vertex.");

                    if (vertex.boundaryCurves.Contains(modelSurfaceSide))
                        throw new Exception("what the heck");

                    if (vertex.boundaryCurves.Count > 4 * sides.Count)
                        throw new Exception("what the heck?");

                    modelSurfaceSide.vertexIndex = vertexIndex;
                    vertex.boundaryCurves.Add(modelSurfaceSide);

                    return true;
                }

                if (!TryAddEdgeToVertex(oldEdge)) break;

                var edgesAtCurrentPolygonVertex = polygonVertices.First(
                    pair => pair.Item2.Contains(oldEdge)
                ).Item2;

                var i = edgesAtCurrentPolygonVertex.IndexOf(oldEdge);
                var turnDirection = oldEdge.rightIsInside ? +1 : -1;
                var j = i + turnDirection;
                if (j < 0)
                    j += edgesAtCurrentPolygonVertex.Count; // modulo in C# is not always positive...
                if (j == edgesAtCurrentPolygonVertex.Count)
                    j = 0;
                var newEdge = edgesAtCurrentPolygonVertex[j];

                if (newEdge.rightIsInside == oldEdge.rightIsInside)
                    throw new Exception(
                        "Check your polygon! Two edges next to each other on a vertex don't agree on what is the inside of the polygon.");

                var angle = newEdge.angle - oldEdge.angle;
                if (angle < 0)
                    angle += tau;
                vertex.angles.Add(angle);

                // we add both edges to the vertex (i.e. this and other, then we continue turning)
                if (!TryAddEdgeToVertex(newEdge)) break;

                oldEdge = newEdge.other;
            }
        }

        #endregion

        // todo: think about this ordering
        var vert = vertices.OrderByDescending(vertex => MathF.Abs(tau / vertex.angles.Sum() - 1));
        this.punctures.AddRange((
            from vertex in vert
            select vertex //.boundaryCurves.First().curve.StartPosition
        ).Take(punctures));
        if (this.punctures.Count == punctures)
            return;
        var center = polygonVertices.Select(pair => pair.Item1).Aggregate((a, b) => a + b) / polygonVertices.Count;
        var radius = polygonVertices.Select(pair => pair.Item1).Max(pos => Vector3.Distance(pos, center));

        // Plain loop instead of a LINQ `let` query (IL2CPP/WebGL signature-mismatch crash, see CurveVisualizer).
        int missingPunctures = punctures - this.punctures.Count;
        for (int attempt = 0, added = 0; attempt < 100 * missingPunctures && added < missingPunctures; attempt++)
        {
            var randomPuncture = ClampPoint((Vector3)Random.insideUnitCircle * radius + center, 0.01f);
            // todo: this MUST NOT be disjoint from the surface. Also it should be approximately the size of the surface
            if (randomPuncture == null) continue;
            this.punctures.Add(randomPuncture);
            added++;
        }

        // if (geometryType is GeometryType.HyperbolicDisk or GeometryType.HyperbolicPlane)
        // {
        //     MinimalPosition = 
        // }
    }


    /// <summary>
    /// This actually doesn't necessarily preserve the locations of the old punctures (if newly added corners have worse
    /// angles than the ones already there)
    /// </summary>
    /// <param name="addedGenus"></param>
    /// <param name="addedPunctures"></param>
    /// <param name="extraBoundaries"></param>
    public ModelSurface WithAddedBoundaries(int addedGenus, int addedPunctures,
        IEnumerable<PolygonSide> extraBoundaries)
    {
        return new(Name, Genus + addedGenus, punctures.Count + addedPunctures, geometryType,
            sidesAsParameters.Concat(extraBoundaries).ToList());

        // todo: implement (this should be basically the same as the constructor and be called from there)
        // todo: check if the inside of the polygon agrees! All rays (from boundary curves) into the "inside" should hit boundary curves on their "inside" side!
        // the corresponding homeomorphisms must be defined elsewhere (where this is called from)
    }

    public Curve GetBasicGeodesic(Point startPoint, Point endPoint, string name, GeodesicSurface surface = null)
    {
        surface ??= this;
        // var ((startPos, endPos), _) = startPoint.ClosestPosition(endPoint, surface);
        return GeometrySurface.GetGeodesic(startPoint, endPoint, name, surface);
    }

    public Curve GetBasicGeodesic(TangentVector tangentVector, float length, string name, out float speedFactor, GeodesicSurface surface = null) => 
        GeometrySurface.GetGeodesic(tangentVector, length, name, out speedFactor, surface ?? this);

    public override Curve GetGeodesic(Point startPoint, Point endPoint, string name, GeodesicSurface surface = null)
    {
        if (startPoint is not IModelSurfacePoint)
            startPoint = ClampPoint(startPoint.Position, 0.001f);
        if (startPoint is null)
            throw new Exception("The start point is not on the surface.");
        if (endPoint is not IModelSurfacePoint)
            endPoint = ClampPoint(endPoint.Position, 0.001f);
        if (endPoint is null)
            throw new Exception("The end point is not on the surface.");
        surface ??= this;

        var centerPoint = DistanceMinimizer(startPoint, endPoint, GeometrySurface);
        if (centerPoint == null)
        {
            var ((startPos, endPos), _) = surface.ClosestPosition(startPoint, endPoint);
            return GetBasicGeodesic(startPos, endPos, name, surface);
        }

        var firstSegment = GetBasicGeodesic(startPoint, centerPoint, name + "pt 1", surface);
        var secondSegment = GetBasicGeodesic(centerPoint.SwitchSide(), endPoint, name + "pt 2", surface);
        return new ConcatenatedCurve(new[] { firstSegment, secondSegment },
            name); //.Smoothed(); // todo?: this doesn't work as expected. Shouldn't be necessary, bc. there shouldn't really be an angle jump
    }

    public override Curve GetGeodesic(TangentVector startVelocity, float length, string name, out float speedFactor, GeodesicSurface surface = null)
    {
        if (length < 0)
        {
            startVelocity = -startVelocity;
            length = -length;
        }
        List<Curve> segments = new();
        var currentStartVector = startVelocity;
        int i = 0;
        Vector3 lastPos = startVelocity.point.Position;
        speedFactor = 1f;
        while (length > 0)
        {
            var currentSegment = GetBasicGeodesic(currentStartVector, length, name + $"pt {i}", out var speedFactor2, surface);
            if (speedFactor == 1f)
                speedFactor = speedFactor2;

            var intersectionTime = float.MaxValue;

            ModelSurfaceSide side = null;
            var sideTime = 0f;
            
            foreach (var side1 in AllSideCurves)
            {
                var times = GeometrySurface.GetIntersectionOfGeodesics(side1, currentSegment);
                if (times?.t2 <= 0)
                {
                    // todo: switch to other side and recompute the intersections, if the vector actually points through this side.
                    continue;
                }
                if (times == null)
                    continue;
                var value = times.Value.t2;
                if (value > intersectionTime) continue;
                intersectionTime = value;
                side = side1;
                sideTime = times.Value.t1;
            }
            
            var t = MathF.Min(intersectionTime, length);
            var p = side != null ? side[sideTime] : currentSegment[t];
            
            i++;
            length -= t;
            
            segments.Add(currentSegment.Restrict(0, t));
            if (length <= 0)
                break;

            var endTangentVec = currentSegment.DerivativeAt(t);
            Vector3 pPos = p.Position;
            Vector3 previousLastPos = lastPos;
            lastPos = pPos;
            if (previousLastPos.ApproximatelyEquals(pPos))
                Debug.LogWarning(
                    $"Weird: The boundary point {p} was visited twice in succession by the geodesic starting at {startVelocity}, the second time after time {t:g2}, and both times from the same side!?");
            var newPositionIndex = 0;
            if (p is ModelSurfaceBoundaryPoint pt)
            {
                p = pt.SwitchSide();
                newPositionIndex = pt.PositionCountOfFirstCurve;
            }

            if (p is ModelSurfaceVertex vertex)
            {
                newPositionIndex = 1;
                // todo? not actually thought through, use angles!
                p = vertex.Positions.ElementAt(newPositionIndex);
            }

            if (pPos.ApproximatelyEquals(p))
                throw new Exception("Bug: The position wasn't updated to the other side.");
            currentStartVector =
                new TangentVector(p, endTangentVec.vector,
                    primaryPositionIndex: newPositionIndex); // switch to other side
        }
        if (segments.Count > 1)
            return new ConcatenatedCurve(segments, name);
        return segments.First();
    }

    private ModelSurfaceBoundaryPoint DistanceMinimizer(Point startPoint, Point endPoint,
        GeodesicSurface baseGeometrySurface)
    {
        // TODO implement for vertices?
        if (startPoint is ModelSurfaceVertex vertex)
            startPoint = vertex.boundaryCurves.First().ValueAt(0.01f);
        if (endPoint is ModelSurfaceVertex vertex2)
            endPoint = vertex2.boundaryCurves.First().ValueAt(0.01f);
        if (startPoint is not IModelSurfacePoint start || endPoint is not IModelSurfacePoint end)
            throw new("Start and end point should have the type IModelSurfacePoint");
        var (optimalSide, _) = DistanceMinimizingDeckTransformation1Side(startPoint, endPoint);
        if (optimalSide == null)
            return null;
        var geodesic = baseGeometrySurface.GetGeodesic(startPoint,
            endPoint.ApplyHomeomorphism(optimalSide.DeckTransformation()), "DistanceMinimizer");
        var (tGeodesic, tSide) = baseGeometrySurface.GetIntersectionOfGeodesics(geodesic, optimalSide.curve) ?? throw new Exception("There should be an intersection point here.");
        return new ModelSurfaceBoundaryPoint(optimalSide, tSide);
    }

    private const float preferBasicGeodesicTolerance = 0.01f;

    private (ModelSurfaceSide optimalSide, float shortestLengthSquared) DistanceMinimizingDeckTransformation1Side(
        Point startPoint,
        Point endPoint)
    {
        ModelSurfaceSide optimalSide = null;
        float shortestLength = GeometrySurface.DistanceSquared(startPoint, endPoint);
        foreach (var side in AllSideCurves)
        {
            float distance = GeometrySurface.DistanceSquared(startPoint, endPoint.ApplyHomeomorphism(side.DeckTransformation()));

            if (distance >= shortestLength ||
                optimalSide == null && distance >= shortestLength - preferBasicGeodesicTolerance)
                continue;
            shortestLength = distance;
            optimalSide = side;
        }

        return (optimalSide, shortestLength);
    }


    // sped up by 50 times from using GetGeodesic(....).Length ^ 2
    public override float DistanceSquared(Point startPoint, Point endPoint) =>
        DistanceMinimizingDeckTransformation1Side(startPoint, endPoint).Item2;

    public override float DistanceSquaredBasic(Vector3 startPoint, Vector3 endPoint) =>
        GeometrySurface.DistanceSquaredBasic(startPoint, endPoint);


    public override (float t1, float t2)? GetIntersectionOfGeodesics(Curve geodesic1, Curve geodesic2) // not needed really, is it?
    {
        IEnumerable<Curve> geodesic1Segments = geodesic1 is ConcatenatedCurve concatenatedCurve1 ? concatenatedCurve1.Segments : new[] { geodesic1 };
        Curve[] geodesic2Segments = geodesic2 is ConcatenatedCurve concatenatedCurve2 ? concatenatedCurve2.Segments as Curve[] ?? concatenatedCurve2.Segments.ToArray(): new[] { geodesic2 };
        var time1 = 0f;
        foreach (var segment1 in geodesic1Segments)
        {
            var time2 = 0f;
            foreach (var segment2 in geodesic2Segments)
            {
                var intersection = GeometrySurface.GetIntersectionOfGeodesics(segment1, segment2);
                if (intersection != null)
                {
                    var (tSegment1, tSegment2) = intersection.Value;
                    return (tSegment1 + time1, tSegment2 + time2); // todo: parametrization speed! 
                }
                time2 += segment2.Length;
            }
            time1 += segment1.Length;
        }

        return null;
    }

    public override Point ClampPoint(Vector3? point, float closenessThreshold) =>
        ClampPoint(point, closenessThreshold, allowVertices: true);

    private Point ClampPoint(Vector3? pos, float closenessThreshold, bool allowVertices)
    {
        const float negativeTolerance = -1e-4f;
        const float closenessFactor = 0.2f;
        if (pos == null) return null;
        var p = pos.Value;
        var distances = new Dictionary<ModelSurfaceSide, float>();
        foreach (var side in AllSideCurves)
        {
            var rightness = side.Rightness(p);
            var closeness = rightness * (side.rightIsInside ? closenessFactor : -closenessFactor);
            if (closeness < negativeTolerance)
                return null;
            distances[side] = closeness * closeness;
        }
        Debug.Log("the point " + p + " is " + string.Join(", ", distances.Select(kv => $"{kv.Key.Name}: {kv.Value:g2}")) + " close to the sides");
        
        var closeSides = distances.Keys.Where(x => distances[x] < closenessThreshold)
            .ToList();

        if (closeSides.Count == 0)
            return new ModelSurfaceInteriorPoint(p);

        if (allowVertices)
            foreach (var closestSide in closeSides)
            {
                float t = closestSide.GetClosestPoint(p);
                if (t < closestSide.Length * 0.1f)
                    return vertices[closestSide.vertexIndex];
                if (t > closestSide.Length * 0.9f)
                    return vertices[closestSide.ReverseModelSide().vertexIndex];
            }

        return new ModelSurfaceInteriorPoint(p).ClosestBoundaryPoints(closeSides[0]);
    }

    /// <summary>
    /// This is the constant basis (e_x, e_y, -e_z) of the model surface. The normal is pointing towards the camera.
    /// This is opposite oriented, i.e. right-handed, because Unity uses a left-handed coordinate system.
    /// </summary>
    /// <param name="position"></param>
    /// <returns></returns>
    public override TangentSpace BasisAt(Point position) => new(position, Matrix3x3.InvertZ);

    public Homeomorphism SwitchHyperbolicModel()
    {
        Homeomorphism baseHomeomorphism;
        string nameAddition;
        switch (geometryType)
        {
            case GeometryType.HyperbolicDisk:
                baseHomeomorphism = HyperbolicPlane.CayleyTransform.Inverse;
                nameAddition = " [halfplane]";
                break;
            case GeometryType.HyperbolicPlane:
                baseHomeomorphism = HyperbolicPlane.CayleyTransform;
                nameAddition = " [disk]";
                break;
            default:
                return null;
        }

        var newSurface = Copy(Name + nameAddition, baseHomeomorphism, nameAddition);
        return new Homeomorphism(this, newSurface, baseHomeomorphism.f, baseHomeomorphism.fInv, baseHomeomorphism.df,
            baseHomeomorphism.dfInv, "Cayley transform");
    }

    public Homeomorphism ToKleinModel()
    {
        if (geometryType != GeometryType.HyperbolicDisk)
            return null;
        Homeomorphism baseHomeomorphism = HyperbolicPlane.ToKleinModel;
        var nameAddition = " [Klein]";
        var newSurface = Copy(Name + nameAddition, baseHomeomorphism, nameAddition);
        return new Homeomorphism(this, newSurface, baseHomeomorphism.f, baseHomeomorphism.fInv, baseHomeomorphism.df,
            baseHomeomorphism.dfInv, "Disk To Klein model");
    }
}