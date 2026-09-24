using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

public partial class FibredSurface
{
    class MovementForFolding
    {
        public readonly IList<Strip> edges;
        public readonly Strip preferredEdge;
        public readonly IReadOnlyDictionary<Junction, IEnumerable<(string, bool)>> vertexMovements; // the vertices that are folded and how they are moved
        public readonly int l; // number of side crossings along f that the resulting edge will have
        private readonly List<(string, bool)> c;
        private int badness = -1;
        private readonly Dictionary<Strip,int> edgeCancellations;
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="edges">edges to fold as ordered in the cyclic order of the star</param>
        /// <param name="preferredEdge"></param>
        /// <param name="l"></param>
        /// <exception cref="ArgumentException"></exception>
        public MovementForFolding(IList<Strip> edges, Strip preferredEdge, int l)
        {
            this.edges = edges; 
            this.preferredEdge = preferredEdge;
            this.l = l;
            
            c = preferredEdge.Curve.SideCrossingWord.Take(l).ToList();
            if (c.Count < l)
                throw new ArgumentException($"The edge {preferredEdge} has not enough side crossings: {c.Count} < {l}", nameof(preferredEdge));

            edgeCancellations =
                edges.ToDictionary(edge => edge, edge => edge.Curve.SideCrossingWord.SharedInitialSegmentLength(c));
            
            vertexMovements = new Dictionary<Junction, IEnumerable<(string, bool)>>(
                from edge in edges
                let sharedInitialSegment= edgeCancellations[edge]
                select new KeyValuePair<Junction, IEnumerable<(string, bool)>>(
                    edge.Target, // vertex
                    edge.Curve.SideCrossingWord.Skip(sharedInitialSegment).Inverse()
                        .Concat( c.Skip(sharedInitialSegment) )
                    // the movement of the vertex as a word in the sides of the model surface that it crosses
                    // with the skips this Concat is exactly ConcatWithCancellation
                )
            );
            
        }

        public int Badness {
            get
            {
                if (badness != -1)
                    return badness;
                
                var count = 0;
                foreach (var edge in edges.First().graph.Edges)
                {
                    if (edges.Contains(edge)) continue;
                    
                    var sideCrossingWord = edge.Curve.SideCrossingWord;
                    if (vertexMovements.TryGetValue(edge.Source, out var movement)) 
                        sideCrossingWord = movement.Inverse().ConcatWithCancellation(sideCrossingWord);
                    if (vertexMovements.TryGetValue(edge.Target, out var movement2))
                        sideCrossingWord = sideCrossingWord.ConcatWithCancellation(movement2);
                    count += sideCrossingWord.Count();
                }

                count += l; // l is the number of side crossings along the edge that results from folding
                badness = count;
                return count;
            }
        }

        public void MoveVerticesForFolding(bool removeEdges = false, bool ignoreGivenEdges = false) 
        {
            var fibredSurface = edges.First().fibredSurface;
            // shorten the preferred curve if necessary
            var preferredCurve = preferredEdge.Curve;
            var (t0l, t1l) = preferredCurve.VisualJumpTimes.Prepend(0f).Skip(l);
            var timeFEnd = (t1l + t0l) / 2; 
            if (t1l == 0) // preferredCurve.VisualJumpTimes has only l elements, i.e. the curve ends before crossing another side
                timeFEnd = preferredCurve.Length;

            if (timeFEnd < preferredCurve.Length)
                fibredSurface.MoveJunction(preferredEdge.Reversed(), preferredCurve.Length - timeFEnd);
            preferredCurve = preferredEdge.Curve; // = preferredCurve.Restrict(0, timeFEnd);
            
            var stringWidths = (
                from edge in edges 
                select baseShiftStrength * MathF.Sqrt(Star(edge.Target).Count()) * Mathf.Clamp(edge.Curve.Length, 0.01f, 1f)
            ).ToArray();
            var preferredEdgeIndex = edges.IndexOf(preferredEdge);
            var edgeIndex = -1;

            foreach (var edge in edges)
            {
                edgeIndex++;
                if (Equals(edge, preferredEdge))
                    continue;
                var vertex = edge.Target;
                var backwardsCurve = edge.Curve.Reversed();
                
                
                var sharedSegment = edgeCancellations[edge];
                var n = backwardsCurve.SideCrossingWord.Count() - sharedSegment; 
                // pull back the vertex for n steps (through n sides) along the edge.
                // Then the edge agrees with the shared initial segment with the preferred edge.
            
                var timeX = 0f; // the time along the backwards curve "close" to the preferred edge, where the edges attached to the vertex will turn to the preferred edge
                if (n > 0)
                {
                    var (t0x, t1x) = backwardsCurve.VisualJumpTimes.Skip(n - 1);
                    if (t1x == 0) // backwardsCurve.VisualJumpTimes has only n elements, i.e. the curve ends before crossing another side (sharedSegment = 0)
                        t1x = backwardsCurve.Length;
                    timeX = (t0x + 2 * t1x) / 3; // a bit closer to the last side crossing along the original edge that is shared with the preferred edge 
                    // todo: fixed distance to last crossing (or the source vertex if there is no crossing)? Shorter for the preferred edge in that case for better visuals?

                    fibredSurface.MoveJunction(vertex, backwardsCurve, timeX, edge.Reversed(), ignoreGivenEdge: ignoreGivenEdges, movingAlongGivenEdge: true);
                    // moves along backwardsCurve and shortens the edge itself (unless we removed it)
                }

                var (t0, t1) = preferredCurve.VisualJumpTimes.Prepend(0f).Skip(sharedSegment); 
                // the same as the Skip(sharedSegment - 1) but with the first element 0 for the case sharedSegment = 0
                var timeF = (t0 * 3 + t1) / 4; // a bit closer to the last side crossing along the preferred edge that is shared with the original edge 
                if (t1 == 0) // preferredCurve.VisualJumpTimes has only sharedSegment elements, i.e. the curve ends before crossing another side (l = sharedSegment = length of preferredCurve.VisualJumpPoints)
                    timeF = preferredCurve.Length;

                Curve restCurve = null;
                Point timeFPoint = preferredCurve[timeF];
                if (l > sharedSegment)
                {
                    float relativeShiftStrength;
                    if (edgeIndex < preferredEdgeIndex)
                        relativeShiftStrength = stringWidths[edgeIndex] / 2f + stringWidths[(edgeIndex + 1)..preferredEdgeIndex].Sum();
                    else 
                        relativeShiftStrength = - stringWidths[edgeIndex] / 2f - stringWidths[(preferredEdgeIndex + 1)..edgeIndex].Sum();
                        
                    restCurve = relativeShiftStrength != 0
                        ? new ShiftedCurve(preferredCurve.Restrict(timeF), relativeShiftStrength, ShiftedCurve.ShiftType.FixedEndpoint)
                        : preferredCurve.Restrict(timeF);
                    timeFPoint = restCurve.StartPosition;
                }
                
                // turning from the backwards curve to the preferred curve
                var intermediateCurve = fibredSurface.GetBasicGeodesic(
                    backwardsCurve[timeX],
                    timeFPoint,
                    "intermediate");

                Curve forwardMovementCurve = intermediateCurve;
                if (restCurve != null)
                    forwardMovementCurve = new ConcatenatedCurve(new[]
                    {
                        intermediateCurve,
                        restCurve
                    }, smoothed: true);

                
                if (forwardMovementCurve.Length > 1e-3) 
                    fibredSurface.MoveJunction(vertex, forwardMovementCurve, forwardMovementCurve.Length, edge.Reversed(), ignoreGivenEdge: ignoreGivenEdges, movingAlongGivenEdge: false); 
                // this prolongs the edge (unless we removed it) 
                
                // todo: addedShiftStrength positive or negative depending on the direction of the edge and the cyclic order.
                // More than one (better: add up the widths) if several vertices are moved along this edge, so that all of the edges are disjoint (hard)
                
                if (removeEdges)
                    fibredSurface.graph.RemoveEdge(edge.UnderlyingEdge);
            }
        }

        public override string ToString() {
            var sb = new StringBuilder();
            sb.AppendLine($"Preferred edge: {preferredEdge.ColorfulName} - Keep the first {l}/{preferredEdge.Curve.SideCrossingWord.Count()} side crossings.");
            // sb.AppendLine($"  {string.Join(", ", c)}");
            sb.AppendLine($"Vertex movements to converge at {preferredEdge.Target.ColorfulName}");
            foreach (var (vertex, movement) in vertexMovements)
                if (movement.Any())
                    sb.AppendLine($"Move vertex {vertex.ColorfulName} across {(from sideCrossing in movement select sideCrossing.Item1 + (sideCrossing.Item2 ? "'" : "")).ToCommaSeparatedString()}");
            sb.AppendLine($"Total number of side crossings afterwards: {Badness}");
            return sb.ToString(); 
        }
    }

    public void MoveJunction(Strip e, float? length = null) => MoveJunction(e.Source, e.Curve, length ?? e.Curve.Length, e);

    
    const float baseShiftStrength = 0.2f;

    public enum MoveJunctionShiftType
    {
        ToTheRight,
        StrictlyToTheRight,
        ToTheLeft,
        StrictlyToTheLeft,
        Symmetric
    }
    public void MoveJunction(Junction v, Curve curve, float length, Strip e = null, bool ignoreGivenEdge = false, bool movingAlongGivenEdge = true, MoveJunctionShiftType shiftType = MoveJunctionShiftType.Symmetric) 
    {
        v.Patches = new []{ curve[length] };
        
        var precompositionCurve = curve.Restrict(0, length).Reversed();
        var star = StarOrdered(v, e, removeFirstEdgeIfProvided: ignoreGivenEdge || movingAlongGivenEdge ).ToList();
        if (e != null && !ignoreGivenEdge && movingAlongGivenEdge)
        {
            var name = e.Name;
            e.Curve = e.Curve.Restrict(length);
            e.Name = name;
        }
 
        float shift = baseShiftStrength / MathF.Sqrt(star.Count) * Mathf.Clamp(precompositionCurve.Length, 0.01f, 1f);
        
        for (var i = 0; i < star.Count; i++)
        {
            var edge = star[i];

            // this shiftStrength is counted towards the right of precompositionCurve, i.e. to the left of curve
            var shiftStrength = shift * shiftType switch
            {   
                MoveJunctionShiftType.StrictlyToTheLeft => star.Count - i,
                MoveJunctionShiftType.ToTheLeft => star.Count - i - 1,
                MoveJunctionShiftType.StrictlyToTheRight => -i - 1,
                MoveJunctionShiftType.ToTheRight => -i,
                MoveJunctionShiftType.Symmetric => star.Count - 1 - i - i,
                _ => throw new ArgumentOutOfRangeException(nameof(shiftType), shiftType, null)
            };
            var shiftedCurve = shiftStrength != 0 ?
                new ShiftedCurve(precompositionCurve, shiftStrength, ShiftedCurve.ShiftType.SymmetricFixedEndpoints)
                : precompositionCurve;

            var name = edge.Name;
            edge.Curve = new ConcatenatedCurve(new[]
            {
                shiftedCurve,
                // surface.GetGeodesic(shiftedCurve.EndPosition, edge.Curve.StartPosition, ""),
                edge.Curve
            }) { Color = edge.Color }; // smooth?
            edge.Name = name;
        }
        
    }

}