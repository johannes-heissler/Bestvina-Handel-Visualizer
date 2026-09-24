using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class PushingPath : IPatchedDrawnsformable
{
    #region Entry classes
    private abstract class Entry
    {
        public virtual EdgePath AssociatedPath(PushingPath pushingPath) => EdgePath.Empty;
    }

    private class EdgeFollowing : Entry
    {
        public readonly Strip strip;
        private readonly bool followingLeft;

        public EdgeFollowing(Strip strip, bool followingLeft)
        {
            this.strip = strip;
            this.followingLeft = followingLeft;
        }

        public override EdgePath AssociatedPath(PushingPath pushingPath) => new NormalEdgePath( strip );

        public override string ToString() => strip.Name + (followingLeft ? "L" : "R");
    }

    private class EdgeCrossing : Entry
    {
        public readonly Strip crossedEdge;
        /// <summary>
        /// This is how the pushing path crosses the edge.
        /// If true, the edge gets dragged along the conjugation path along the left, then following the puncture word clockwise (following the edge to the right), then following the conjugation path backwards on the left. 
        /// </summary>
        public readonly bool rightToLeft;

        /// <summary>
        /// If the edge is crossed multiple times, then this determines the order.
        /// </summary>
        public readonly Variable positionAlongEdge;

        /// <summary>
        /// An edge is crossed near its start.
        /// </summary>
        public EdgeCrossing(Strip crossedEdge, bool rightToLeft, Variable positionAlongEdge)
        {
            this.crossedEdge = crossedEdge;
            this.rightToLeft = rightToLeft;
            this.positionAlongEdge = positionAlongEdge;
        }

        public override string ToString() => $"({crossedEdge.Name}" + (rightToLeft ? "<-" : "->") + ")";
    }

    private class SelfIntersection : Entry
    {
        public readonly bool rightToLeft;
        public readonly SelfIntersectionSecondTime secondTime;
        public readonly string name;

        public SelfIntersection(bool rightToLeft, string name, SelfIntersectionSecondTime secondTime)
        {
            this.rightToLeft = rightToLeft;
            this.name = name;
            this.secondTime = secondTime;
        }

        public override EdgePath AssociatedPath(PushingPath pushingPath)
        {
            int secondIndex = pushingPath.path.IndexOf(secondTime);
            if (secondIndex <= pushingPath.path.IndexOf(this))
                throw new InvalidOperationException("The second time of a self-intersection has to be after the first time in the path!");
            return (rightToLeft ? pushingPath.punctureWord : pushingPath.punctureWord.Inverse).Conjugate(
                pushingPath.ConjugationPath(secondIndex),
                true
            );
        }

        public override string ToString() => $"[{name}]";
    }

    private class SelfIntersectionSecondTime : Entry
    {
        public readonly string name;
        public int indexInPath = -1; 

        public SelfIntersectionSecondTime(string name)
        {
            this.name = name;
        }

        public override string ToString() => $"{{{name}}}";
    }
    #endregion

    #region Variable and Corner classes
    public class Variable
    {
        public readonly string name;

        private float? value;

        public bool Concrete => value.HasValue;

        public float Value => value ?? 1f;

        public Variable(string name, float? value = null)
        {
            this.value = value;
            this.name = name;
        }

        public void SetValue(float f)
        {
            value = f;
        }

        public void FreeVariable()
        {
            value = null;
        }

        public static implicit operator Variable(float f) => new($"implicitly converted constant {f}", f);

        public override string ToString() => Concrete ? $"{name} = {Value}" : $"{name} = ?";
    }
    
    private class CornerSegment
    {
        public bool Concrete => x.Concrete && y.Concrete;
        
        private readonly SegmentType type;

        [Flags]
        public enum SegmentType
        {
            XDownwards = 1,
            XUpwards = 2,
            YDownwards = 4,
            YUpwards = 8,
            InnerTurn = XDownwards | YDownwards,
            OuterTurn = XUpwards | YUpwards,
            HalfTurn = XUpwards | YDownwards,
            HalfTurnFromLeft = YUpwards | XDownwards
        }

        public readonly Strip xAxis;
        /// <summary>
        /// The yAxis is always assumed to be the successor of xAxis in the cyclic order.
        /// </summary>
        public readonly Strip yAxis;
        public readonly Variable x;
        public readonly Variable y;

        /// true iff the path comes in from the y direction and goes out in the x direction.
        public readonly bool flip;

        public readonly bool increasingInX;
        public readonly bool increasingInY;

        /// <summary>
        ///  this is always the index after the last edge-following / edge-crossing before the turn (the number of path entries already discovered at the time of this corner turn).
        /// </summary>
        public readonly int indexInPath;

        /// <summary>
        /// Creates a corner segment in the quadrant defined by <paramref name="xAxis"/> and <paramref name="yAxis"/>.
        /// These span a coordinate system (x, y).
        /// 
        /// <paramref name="flip"/> is the orientation of the corner segment, i.e. it is false iff <paramref name="yAxis"/> = σ(<paramref name="xAxis"/>), and true if <paramref name="yAxis"/> = σ^(-1)(<paramref name="xAxis"/>).
        ///
        /// The type describes the segments (as sets) from the viewpoint of the point (x,y).
        /// Thus, the direction that the outward path (along the yAxis) takes agrees with the one in the type (YUpwards/YDownwards), while the incoming direction is the opposite of the one in the type (If XUpwards, x decreases when coming in; as xAxis corresponds to the inverse of the incoming strip).
        /// </summary>
        public CornerSegment(Strip xAxis, Strip yAxis, Variable x, Variable y, SegmentType type, bool flip, int indexInPath, bool increasingInX = false)
        {
            this.flip = flip;
            this.indexInPath = indexInPath;
            this.xAxis = xAxis;
            this.yAxis = yAxis;
            this.x = x;
            this.y = y;
            this.type = type;
            this.increasingInX = increasingInX;
            
            bool hasX = (this.type & ( SegmentType.XDownwards | SegmentType.XUpwards )) != 0;
            bool hasY = (this.type & ( SegmentType.YDownwards | SegmentType.YUpwards )) != 0;
            bool errorCase = (increasingInX == type.HasFlag(SegmentType.XUpwards)) && hasY;
            
            this.increasingInY = type.HasFlag(SegmentType.YUpwards) ^ errorCase; // if we are in the error case, then the y direction is inverted; Else, it follows the type.
            
            if (!hasX)
                Debug.LogError($"The type {type} is not valid for a corner segment, as it doesn't specify any direction along the xAxis {xAxis.Name}. The xAxis must be traversed first.");
            if (increasingInX && type.HasFlag(SegmentType.XUpwards) && (type.HasFlag(SegmentType.YUpwards) || type.HasFlag(SegmentType.YDownwards)))
                throw new ArgumentException($"The flag {nameof(increasingInX)} = true is only possible for XDownward or if the corner segment does not follow along the yAxis. Thus the type {type} is invalid. The xAxis {xAxis.Name} must be traversed first.");
            // value of increasingInY doesn't matter if neither yUpward nor yDownwards, so we can assume one is set, and thus also that increasingInX =>  XDownward 
            
            if (!flip)
                return;
            
            (this.xAxis, this.yAxis) = (this.yAxis, this.xAxis);
            (this.x, this.y) = (this.y, this.x);
            (this.increasingInX, this.increasingInY) = (this.increasingInY, this.increasingInX);
            this.type = (this.type.HasFlag(SegmentType.XDownwards) ? SegmentType.YDownwards : 0) |
                        (this.type.HasFlag(SegmentType.XUpwards) ? SegmentType.YUpwards : 0) |
                        (this.type.HasFlag(SegmentType.YDownwards) ? SegmentType.XDownwards : 0) |
                        (this.type.HasFlag(SegmentType.YUpwards) ? SegmentType.XUpwards : 0);
        }

        internal class CornerSegmentIntersection
        {
            public readonly CornerSegment self;
            public readonly CornerSegment other;
            public readonly float x;
            public readonly float y;
            public readonly float timeAlongSelf;
            public readonly float timeAlongOther;
            public readonly bool rightToLeft; 

            public CornerSegmentIntersection(CornerSegment self, CornerSegment other, float x, float y, float timeAlongSelf, float timeAlongOther, bool rightToLeft)
            {
                this.self = self;
                this.other = other;
                this.x = x;
                this.y = y;
                this.timeAlongSelf = timeAlongSelf;
                this.timeAlongOther = timeAlongOther;
                this.rightToLeft = rightToLeft;
            }
        }
        internal static IEnumerable<CornerSegmentIntersection> CalculateIntersections(CornerSegment self, CornerSegment other)
        {
            if (self.xAxis != other.xAxis)
                yield break;

            // the segments selfY and otherX have at most one intersection, at (self.x, other.y).
            // if the values are the same, we treat the earlier one (self) as being ε larger
            // Thus, in the uninitialized case (all variables have no value, treated as 1), they are considered to be decreasing along γ.
            bool intersectionOfSelfYWithOtherX = (
                other.type.HasFlag(SegmentType.XUpwards) && self.x.Value >= other.x.Value ||
                other.type.HasFlag(SegmentType.XDownwards) && self.x.Value < other.x.Value
            ) && (
                self.type.HasFlag(SegmentType.YUpwards) && other.y.Value > self.y.Value ||
                self.type.HasFlag(SegmentType.YDownwards) && other.y.Value <= self.y.Value
            );
            
            // the same with self and other exchanged
            bool intersectionOfSelfXWithOtherY = (
                self.type.HasFlag(SegmentType.XUpwards) && other.x.Value > self.x.Value ||
                self.type.HasFlag(SegmentType.XDownwards) && other.x.Value <= self.x.Value
            ) && (
                other.type.HasFlag(SegmentType.YUpwards) && self.y.Value >= other.y.Value ||
                other.type.HasFlag(SegmentType.YDownwards) && self.y.Value < other.y.Value
            );
            
            if (intersectionOfSelfYWithOtherX)
            {
                // the basis (other.deriv, self.deriv) = (+- e_x, +- e_y) is negatively oriented iff exactly one of the basis vectors (e_x, e_y) is inverted
                bool negativelyOriented = other.increasingInX != self.increasingInY;

                var distanceFromSelfTurn = other.y.Value - self.y.Value;
                var distanceFromOtherTurn = self.x.Value - other.x.Value;
                
                
                yield return new CornerSegmentIntersection(self, other, self.x.Value, other.y.Value, 
                    timeAlongSelf: self.increasingInY ? distanceFromSelfTurn : - distanceFromSelfTurn, 
                    timeAlongOther: other.increasingInX ? distanceFromOtherTurn : - distanceFromOtherTurn, 
                    rightToLeft: negativelyOriented
                );
            }

            if (intersectionOfSelfXWithOtherY)
            {
                // the basis (self.deriv, other.deriv) = (+- e_x, +- e_y) is negatively oriented iff exactly one of the basis vectors (e_x, e_y) is inverted, i.e. exactly one of these is decreasing:
                bool negativelyOriented = self.increasingInX != other.increasingInY;
                
                var distanceFromSelfTurn = other.x.Value - self.x.Value;
                var distanceFromOtherTurn = self.y.Value - other.y.Value;
                
                yield return new CornerSegmentIntersection(self, other, other.x.Value, self.y.Value, 
                    timeAlongSelf: self.increasingInX ? distanceFromSelfTurn : - distanceFromSelfTurn, 
                    timeAlongOther: other.increasingInY ? distanceFromOtherTurn : - distanceFromOtherTurn, 
                    rightToLeft: !negativelyOriented
                );
            }
        }

        public Point ToPoint()
        {
            if (!Concrete)
                Debug.LogWarning("Converted a PointNearVertex with free variables to Point");
            if (yAxis.Curve.Surface is not GeodesicSurface surface)
                return yAxis.Source.Position;
            return surface.GetGeodesic(
                new TangentVector(
                    xAxis.Curve[x.Concrete ? x.Value : 1f],
                    yAxis.Curve.StartVelocity.vector
                ), y.Concrete ? y.Value : 1f, "Point near vertex segment", out _
            ).EndPosition;
        }

        public IDrawnsformable ToDrawnsformable() // TODO: Feature, this seems to not be correct atm. Shift parallel to the other axis!
        {
            if (!Concrete)
                Debug.LogWarning("Converted a PointNearVertex with free variables to Drawnsformable");
            if (yAxis.Curve.Surface is not GeodesicSurface surface)
                return yAxis.Source.Position; // todo?

            float normalize(float x, float doublemax) => Mathf.Atan(x) / Mathf.PI * doublemax;
            Curve xSegment = null;
            Curve ySegment = null;
            if (type.HasFlag(SegmentType.XDownwards))
                xSegment = new ShiftedCurve(xAxis.Curve.Restrict(0, normalize(x.Value, xAxis.Curve.Length / 2)), normalize(y.Value, 1));
            if (type.HasFlag(SegmentType.XUpwards))
                xSegment = new ShiftedCurve(xAxis.Curve.Restrict(normalize(x.Value, xAxis.Curve.Length / 2), xAxis.Curve.Length / 2), normalize(y.Value, 1));
            if (type.HasFlag(SegmentType.YDownwards))
                ySegment = new ShiftedCurve(yAxis.Curve.Restrict(0, normalize(y.Value, xAxis.Curve.Length / 2)),  normalize(x.Value, 1));
            if (type.HasFlag(SegmentType.YUpwards))
                ySegment = new ShiftedCurve(yAxis.Curve.Restrict(normalize(y.Value, yAxis.Curve.Length / 2), yAxis.Curve.Length / 2), normalize(x.Value, 1));
            // todo: unordered curve? will be displayed as ordered

            if (xSegment != null && ySegment != null)
                return new ConcatenatedCurve(new[] { xSegment, ySegment }, smoothed: true); // todo: check concatenation
            return (IDrawnsformable)(xSegment ?? ySegment) ?? ToPoint();
        }
    }
    
    #endregion

    #region Properties and Constructors

    

    private readonly List<Entry> pathWithoutSelfIntersections;

    private List<Entry> path;
    /// <summary>
    /// This is where the small loop around the marked point / puncture that all pushed curves follow gets isotoped in the graph.
    /// This is the path that starts where the pushingPath starts and follows all edges to the right, never crossing an edge.
    /// It thus runs around the marked point / puncture clockwise 
    /// </summary>
    public readonly EdgePath punctureWord;
    
    public readonly EdgePath edgePath;

    public bool Concrete => cornerSegments.All(pt => pt.Concrete);

    private readonly IReadOnlyList<CornerSegment> cornerSegments;
    public readonly IReadOnlyList<Variable> variables;
    public readonly bool startLeft;

    private PushingPath(List<Entry> path, EdgePath edgePath, IReadOnlyList<CornerSegment> cornerSegments, IReadOnlyList<Variable> variables,
        bool startLeft)
    {
        this.pathWithoutSelfIntersections = this.path = path;
        this.edgePath = edgePath;
        this.cornerSegments = cornerSegments;
        this.variables = variables;
        this.startLeft = startLeft;
        EdgePath boundaryWord;
        if (startLeft)
        {
            boundaryWord = FibredSurface.BoundaryWord(edgePath.First().Reversed());
            boundaryWord = new NormalEdgePath(boundaryWord.CyclicShift(1));
            // this is the boundary word s.t. its reverse, the one following all edges to the left, starts with the same edge as this pushing path.
        }
        else
            boundaryWord = FibredSurface.BoundaryWord(edgePath.First());
        punctureWord = new NamedEdgePath( boundaryWord, "ρ" );
    }
    
    /// <summary>
    /// This creates a PushingPath based at a marked point to the right (or left) of the start of the first edge.
    /// You should set Color and Name as well.
    /// The PushingPath will be created with free variables and will be inconcrete, thus having no idea about self-intersections. Call Concretize();
    /// </summary>
    public PushingPath(EdgePath edgePath, bool startLeft = false) :
        this(edgePath, 
            edgePath.First().graph.Vertices.ToDictionary(
                v => v, 
                v => FibredSurface.StarOrdered(v).ToList()
            ), startLeft)
    {  }

    private PushingPath(EdgePath edgePath, IReadOnlyDictionary<Junction, List<Strip>> stars, bool startLeft = false) :
        this(
            FindPathWithCrossingsButNoSelfIntersections(
                edgePath,
                stars,
                startLeft,
                out var cornerSegments,
                out var variables
            ),
            edgePath,
            cornerSegments, 
            variables,
            startLeft
        )
    { }
        
    #endregion

    #region Calculating the Crossings
    private static List<Entry> FindPathWithCrossingsButNoSelfIntersections(EdgePath edgePath, IReadOnlyDictionary<Junction, List<Strip>> stars,
        bool startLeft, out List<CornerSegment> cornerPoints, out List<Variable> variables)
    {
        variables = new List<Variable>(2 * edgePath.Count);
        cornerPoints = new List<CornerSegment>(2 * edgePath.Count);
        
        var path = new List<Entry>(2 * edgePath.Count);

        bool followingLeft = startLeft;

        Variable punctureDistanceM = new Variable($"M = Distance of puncture to the corner at the beginning of {edgePath.First()}", float.PositiveInfinity); // maxvalue? These values should only be compared, no arithmetic
        Variable currentDistanceToFollowedStrip = punctureDistanceM;
        // variables.Add(currentDistanceToFollowedStrip);

        // we currently insert the first half-edge-following last.
        List<Strip> edgesToFollow = edgePath.ToList();
        for (var index = 0; index < edgesToFollow.Count; index++)
        {
            var currentlyFollowedStrip = edgesToFollow[index];
            var nextStrip = edgesToFollow[(index + 1) % edgesToFollow.Count];

            var lastFollowingEdge = new EdgeFollowing(currentlyFollowedStrip, followingLeft);
            path.Add(lastFollowingEdge);

            var star = stars[currentlyFollowedStrip.Target].CyclicShift(currentlyFollowedStrip.Reversed()).Skip(1)
                .ToList();
            var otherAxisInArrivingQuadrant = followingLeft ? star[^1] : star[0];

            int indexInStar = star.IndexOf(nextStrip);

            if (indexInStar < 0) // doesn't contain the nextStrip
                throw new ArgumentException(
                    $"Your pushing path contains broken concatenation points or backtracking between {currentlyFollowedStrip.Name} and {nextStrip.Name}!");

            int edgesToCrossClockwise = star.Count - 1 - indexInStar + (!followingLeft ? 1 : 0);
            int edgesToCrossCounterClockwise = indexInStar + (followingLeft ? 1 : 0);

            bool turnAroundClockwise =
                edgesToCrossClockwise <
                edgesToCrossCounterClockwise; // todo? optimize intersections? probably way too hard (it's hard to optimize already)

            if (turnAroundClockwise)
            {
                star.Reverse();
                indexInStar = star.Count - 1 - indexInStar;
            }

            var edgesToCross = star.GetRange(0, indexInStar);

            Variable currentEdgeCrossingPosition = null;

            if (!followingLeft && turnAroundClockwise || followingLeft && !turnAroundClockwise)
            {
                // in these cases we have to cross the edge that we just followed before continuing with the edge crossings

                currentEdgeCrossingPosition = new Variable($"v_{variables.Count} = Distance to the end of {currentlyFollowedStrip.Name} when doing a half-turn through it.");
                variables.Add(currentEdgeCrossingPosition);
                
                var halfTurn = new CornerSegment(
                    currentlyFollowedStrip.Reversed(),
                    otherAxisInArrivingQuadrant,
                    currentEdgeCrossingPosition,
                    currentDistanceToFollowedStrip, // todo think about variable: could be chosen independent from the distance that the strip has at the beginning, but this would mean that we introduce self-intersections in the middle of the edge when permuting edge followings // the incoming distance might be modified
                    CornerSegment.SegmentType.HalfTurn,
                    flip: followingLeft,
                    indexInPath: path.Count
                );

                cornerPoints.Add(halfTurn);
                
                edgesToCross.Insert(0, currentlyFollowedStrip.Reversed());
                // the crossing loop will cross this edge at distance currentEdgeCrossingPosition

                followingLeft = !followingLeft; // This is the only time this changes. This change only affects the next edge followings, the loops below reference only turnaroundClockwise 
            }
            else
            {
                if (indexInStar == 0) // otherAxisInArrivingQuadrant == nextStrip, so we don't have to cross any edges.
                {
                    
                    // var max = float.MinValue;
                    // foreach (var t in path)
                    // {
                    //     if (t is not EdgeFollowing otherEdgeFollowing)
                    //         continue; //  todo: also check for turns?
                    //     if (!otherEdgeFollowing.strip.UnderlyingEdge.Equals(nextStrip.UnderlyingEdge))
                    //         continue;
                    //     var otherEdgeFollowingAlignedDistance =
                    //         otherEdgeFollowing.AlignedDistance(nextStrip);
                    //     if (otherEdgeFollowingAlignedDistance < 0)
                    //         continue;
                    //     if (otherEdgeFollowingAlignedDistance > max)
                    //         max = otherEdgeFollowingAlignedDistance;
                    // }
                    //
                    // currentDistanceToFollowedStrip = MathF.Sign(currentDistanceToFollowedStrip) *
                    //                                  (max == float.MinValue ? 1f : max * 1.333333333333f);

                    var nextDistanceToFollowedStrip = new Variable($"v_{variables.Count} = Distance to edge {otherAxisInArrivingQuadrant.Name} when turning " + (followingLeft ? "left" : "right" ) + $" after following {currentlyFollowedStrip.Name}");
                    variables.Add(nextDistanceToFollowedStrip);
                    
                    var outerTurn = new CornerSegment(
                        currentlyFollowedStrip.Reversed(),
                        otherAxisInArrivingQuadrant,
                        nextDistanceToFollowedStrip,
                        currentDistanceToFollowedStrip, // again, the incoming distance might be modified
                        CornerSegment.SegmentType.OuterTurn,
                        flip: followingLeft,
                        indexInPath: path.Count
                    );
                    cornerPoints.Add(outerTurn);
                    currentDistanceToFollowedStrip = nextDistanceToFollowedStrip;
                    continue;
                }

                // We run straight into the next edge, then continue with the crossing loop
                var incomingSegment = new CornerSegment(
                    currentlyFollowedStrip.Reversed(),
                    otherAxisInArrivingQuadrant,
                    0,
                    currentDistanceToFollowedStrip, 
                    CornerSegment.SegmentType.XUpwards,
                    flip: followingLeft,
                    indexInPath: path.Count
                );

                cornerPoints.Add(incomingSegment);

                currentEdgeCrossingPosition = currentDistanceToFollowedStrip; // also overwrites the variable reference
            }

            // crossing loop
            for (var i = 0; i < edgesToCross.Count; i++)
            {
                var edgeCrossed = edgesToCross[i];
                // this loop body starts right before the crossing of the edgeCrossed 

                var edgeCrossing = new EdgeCrossing(edgeCrossed, !turnAroundClockwise, currentEdgeCrossingPosition);

                path.Add(edgeCrossing);

                if (i == edgesToCross.Count - 1)
                    break; // breaks after continue as well   

                var nextEdgeToCross = edgesToCross[i + 1];

                var nextEdgeCrossingPosition = new Variable($"v_{variables.Count} = Distance along edge {nextEdgeToCross.Name} where it is intersected as the {i + 1}th edge crossed between the {index}th strip {currentlyFollowedStrip.Name} and the {index+1}th strip {nextStrip.Name}");
                variables.Add(nextEdgeCrossingPosition);

                var innerTurn = new CornerSegment(
                    nextEdgeToCross,
                    edgeCrossed,
                    nextEdgeCrossingPosition,
                    currentEdgeCrossingPosition,
                    CornerSegment.SegmentType.InnerTurn,
                    !turnAroundClockwise,
                    indexInPath: path.Count
                );

                cornerPoints.Add(innerTurn);

                currentEdgeCrossingPosition = nextEdgeCrossingPosition;

            }

            var crossedStrip = edgesToCross[^1]; // nonempty
            CornerSegment outgoingSegment;
            if (index < edgesToFollow.Count - 1)
                outgoingSegment = new CornerSegment(
                    nextStrip,
                    crossedStrip,
                    0,
                    currentEdgeCrossingPosition,
                    CornerSegment.SegmentType.XUpwards,
                    flip: !turnAroundClockwise,
                    indexInPath: path.Count
                );
            else // connect back to the puncture
                outgoingSegment = new CornerSegment(
                    nextStrip,
                    crossedStrip,
                    punctureDistanceM,
                    currentEdgeCrossingPosition,
                    CornerSegment.SegmentType.YUpwards | CornerSegment.SegmentType.XDownwards,
                    flip: !turnAroundClockwise,
                    indexInPath: path.Count
                );
            cornerPoints.Add(outgoingSegment);
            currentDistanceToFollowedStrip = currentEdgeCrossingPosition;
        }

        return path;
    }
    
    #endregion

    #region Calculating Self-Intersections


    public void CalculateSelfIntersections() { 
        
        path = new List<Entry>(pathWithoutSelfIntersections); 
        
        if (!Concrete)
            Debug.LogWarning("Evaluating self-intersections of PushingPath with free variables!");
        int index = 0;
        
        List<(Entry, int, float)> entriesToAdd = new(); 
        for (int i = 0; i < cornerSegments.Count; i++)
        {
            var currentPoint = cornerSegments[i];
            for (int j = i + 1; j < cornerSegments.Count; j++)
            {
                var laterPoint = cornerSegments[j];
                    
                foreach (var intersection in CornerSegment.CalculateIntersections(currentPoint, laterPoint))
                {
                    var name = $"self-int. {index++} at ({intersection.x:0.0}, {intersection.y:0.0}) in ({currentPoint.xAxis.Name}, {currentPoint.yAxis.Name})";
                    var secondIntersectionTime = new SelfIntersectionSecondTime(name); 
                    var selfIntersection = new SelfIntersection(
                        intersection.rightToLeft, 
                        name,
                        secondIntersectionTime
                    );
                    entriesToAdd.Add((selfIntersection, intersection.self.indexInPath, intersection.timeAlongSelf));
                    entriesToAdd.Add((secondIntersectionTime, intersection.other.indexInPath, intersection.timeAlongOther));
                }
                
            }
        }
        // sort by lexicographic order of (index in path, time along segment), so that we can insert them in the right order
        entriesToAdd.Sort((tuple1, tuple2) =>
        {
            var compare = tuple1.Item2.CompareTo(tuple2.Item2);
            if (compare != 0)
                return compare;
            return tuple1.Item3.CompareTo(tuple2.Item3);
        });

        var offset = 0;
        foreach (var (entry, indexInPath, _) in entriesToAdd) 
            path.Insert(indexInPath + offset++, entry);

    }
    #endregion
    
    #region Calculating the Map

    
    
    private EdgePath ConjugationPath(int startTime)
    {
        return EdgePath.Concatenate(path.GetRange(startTime, path.Count - startTime).Select(p => p.AssociatedPath(this)));
    }

    public EdgePath Image(UnorientedStrip strip)
    {
        if (!Concrete)
            Debug.LogWarning("Calculating the graph map of a point push with free variables!");
        
        return EdgePath.Concatenate(
                SortedConjugationPathsInEdge(strip, false)
                    .Append(new NormalEdgePath(strip))
                    .Concat(SortedConjugationPathsInEdge(strip.Reversed(), true)
            )
        );

        IEnumerable<EdgePath> SortedConjugationPathsInEdge(Strip edge, bool reverse) =>
            path
                .Enumerate()
                .Where(tuple =>
                    tuple.t is EdgeCrossing crossing && Equals(crossing.crossedEdge, edge)
                ).OrderBy(
                    tuple => ((EdgeCrossing) tuple.t).positionAlongEdge.Value * (reverse ? -1 : 1))
                .Select(tuple => 
                        (((EdgeCrossing) tuple.t).rightToLeft != reverse ? punctureWord : punctureWord.Inverse)
                        .Conjugate(ConjugationPath(tuple.i), true)
                    );
    }


    #endregion

    #region Fulfilling the Interfaces

    public string Name { get; set; }

    public Color Color // Copied from "virtual" implementation in PatchedDrawnsformable
    {
        get => Patches.FirstOrDefault()?.Color ?? Color.magenta;
        set
        {
            foreach (var patch in Patches)
                patch.Color = value;
        }
    }

    public IPatchedDrawnsformable Copy() => new PushingPath(path, edgePath, cornerSegments, variables, startLeft) { Name = Name, Color = Color } ; // todo: create new variables and new lists, this is not a deep copy!

    public IEnumerable<IDrawnsformable> Patches => from cornerSegment in cornerSegments select cornerSegment.ToDrawnsformable();

    public override string ToString() => path.ToCommaSeparatedString(" ");
    

    #endregion
}