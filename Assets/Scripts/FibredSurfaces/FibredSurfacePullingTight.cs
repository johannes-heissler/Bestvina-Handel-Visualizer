using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class FibredSurface
{
    /// <summary>
    /// These are the positions where the graph map is not tight, so we can pull tight here.
    /// We have to assume that there are no invariant subforests because the isotopy would touch them!
    /// </summary>
    public IEnumerable<(Strip, EdgePoint[], Junction[])> GetLoosePositions()
    {
        var backTracksDict = OrientedEdges.ToDictionary(e => e, e => new List<EdgePoint>());
        var extremalVerticesDict = OrientedEdges.ToDictionary(e => e, e => new List<Junction>());
    
        foreach (var edgePoint in GetBackTracks()) 
            backTracksDict[edgePoint.DgAfter()].Add(edgePoint);

        foreach (var extremalVertex in GetExtremalVertices()) 
            extremalVerticesDict[Star(extremalVertex).First().Dg!].Add(extremalVertex);
    
        foreach (var strip in OrientedEdges)
        {
            var backTracks = backTracksDict[strip];
            var extremalVertices = extremalVerticesDict[strip];
            if (backTracks.Count > 0 || extremalVertices.Count > 0)
                yield return (strip, backTracks.ToArray(), extremalVertices.ToArray());
        }
    }

    public (Strip, EdgePoint[], Junction[]) GetLoosePositions(Strip strip)
    {
        var backTracks = GetBackTracks(strip).ToArray();
        var extremalVertices = GetExtremalVertices(strip).ToArray();
        return (strip, backTracks, extremalVertices);
    }

    public AlgorithmSuggestion TighteningSuggestion()
    {
        var options = (
            from loosePosition in GetLoosePositions()
            select (loosePosition.Item1.Name as object, PullTightString(loosePosition))
        ).ToArray();
        if (options.Length == 0) return null;
        return new AlgorithmSuggestion(
            options, 
            description: "Pull tight at one or more edges.",
            buttons: new[] { AlgorithmSuggestion.tightenAllButton, AlgorithmSuggestion.tightenSelectedButton }, 
            allowMultipleSelection: true
        );
    }

    private static string PullTightString((Strip, EdgePoint[], Junction[]) loosePositions) =>
        $"In edge {loosePositions.Item1.ColorfulName} there are the {loosePositions.Item2.Length} backtracks " +
        loosePositions.Item2.Select(edgePoint => edgePoint.ToShortString(3, 2, colorful: true)).Take(50).ToCommaSeparatedString() + 
        (loosePositions.Item2.Length > 50 ? "..." : "") +
        " and " + (loosePositions.Item3.Length > 0 ? "the" : "no") +
        (loosePositions.Item3.Length > 1 ? loosePositions.Item3.Length + " extremal vertices " : " extremal vertex ") +
        loosePositions.Item3.Take(50).ToCommaSeparatedString(v => v.ToColorfulString()) +
        (loosePositions.Item3.Length > 50 ? "..." : "");

    IEnumerable<Junction> GetExtremalVertices(Strip edge = null)
    {
        if (edge != null)
            return graph.Vertices.Where(vertex =>
            {
                var star = Star(vertex);
                // only null if vertex has valence 0, but then the graph is only a vertex and the surface is a disk.
                return star.Any() && star.All(strip => Equals(strip.Dg, edge));
            });
        return graph.Vertices.Where(vertex =>
        {
            var star = Star(vertex);
            var firstOutgoingEdge = star.FirstOrDefault();
            // only null if vertex has valence 0, but then the graph is only a vertex and the surface is a disk.
            return firstOutgoingEdge != null && firstOutgoingEdge.Dg != null &&
                   star.All(strip => Equals(strip.Dg, firstOutgoingEdge.Dg));
        });
    }


    IEnumerable<EdgePoint> GetBackTracks(Strip edge = null, EdgePoint[] testedEdgePoints = null)
    {
        // FirstOrDefault() gets called > 300 times on this in a typical call to PullTightAll (takes > 1 second) 


        foreach (var strip in Strips)
        {
            // actually, testedEdgePoints should contain exactly one edgePoint on each UnorientedStrip.
            var testedUntilIndex = testedEdgePoints?.Max(ep => ep.AlignedIndex(strip));
            // only internal points: Valence-2 extremal vertices are found in parallel anyways.
            var testFromIndex = testedUntilIndex > 0 ? testedUntilIndex.Value : 1;
            Strip lastEdge = null;
            int currentIndex = testFromIndex;
            foreach (var currentEdge in strip.EdgePath.Skip(testFromIndex - 1))
            {
                if (lastEdge == null) 
                {
                    lastEdge = currentEdge;
                    continue;
                }
                
                if (currentEdge != strip.EdgePath[currentIndex] || lastEdge != strip.EdgePath[currentIndex - 1])
                    Debug.LogError($"The edges in the strip {strip} are not in the expected order at index {currentIndex}: {lastEdge} and {currentEdge}."); 
                if (Equals(currentEdge, lastEdge.Reversed()) && (edge == null || Equals(currentEdge, edge))) 
                    yield return new EdgePoint(strip, currentIndex);
                currentIndex++;
                lastEdge = currentEdge;
            }
        }
    }

    private void PullTightExtremalVertex(Junction vertex, bool all = false)
    {
        vertex.image = null;
        var star = Star(vertex).ToArray();
        int initialSegment = all ? Strip.SharedInitialSegment(star) : 1;
        foreach (var strip in star)
        {
            strip.EdgePath = strip.EdgePath.Skip(initialSegment);
            vertex.image ??= strip.Dg?.Source;
            // for self-loops, this takes one from both ends.
        }
        // isotopy: Move vertex and shorten the strips (only the homeomorphism is changed, not the graph)
        // todo? update EdgePoints?
    }

    private void PullTightBackTrack(EdgePoint backTrack, IList<EdgePoint> updateEdgePoints = null, bool all = false)
    {
        updateEdgePoints ??= new List<EdgePoint>();
        if (!Equals(backTrack.DgBefore(), backTrack.DgAfter()))
        {
            Debug.LogWarning($"Assumed Backtrack at {backTrack} is not a backtrack (anymore)!");
            return;
        }

        var strip = backTrack.edge;
        var i = backTrack.index;
        if (i == 0)
        {
            PullTightExtremalVertex(strip.Source, all);
            return;
        }

        var backtrackingSegment = 1;
        if (all)
        {
            while (i + backtrackingSegment < strip.EdgePath.Count &&
                   i - backtrackingSegment >= 1 &&
                   Equals(strip.EdgePath[i + backtrackingSegment], strip.EdgePath[i - backtrackingSegment - 1].Reversed()))
                backtrackingSegment++;
        }
        
        strip.EdgePath = strip.EdgePath.Take(i - backtrackingSegment).Concat(strip.EdgePath.Skip(i + backtrackingSegment));

        for (int k = 0; k < updateEdgePoints.Count; k++)
        {
            var j = updateEdgePoints[k].AlignedIndex(strip, out var reverse);
            if (j < i) continue;
            if (j <= i - backtrackingSegment) 
                continue;
            var newIndex = i - backtrackingSegment;
            if (j >= i + backtrackingSegment) 
                newIndex = j - 2 * backtrackingSegment;
            var res = new EdgePoint(strip, newIndex);
            updateEdgePoints[k] = reverse ? res.Reversed() : res;
        }
    }


    public void PullTightAll(string edgeName) => 
        PullTightAll(OrientedEdges.FirstOrDefault(e => e.Name == edgeName));

    /// <summary>
    /// Pull tight all backtracks in the edge, or all backtracks at once -- in this case, we pull tight the maximal segment at once
    /// </summary>
    /// <param name="strip"></param>
    public void PullTightAll(Strip strip = null)
    {
        var limit = Strips.Sum(e => e.EdgePath.Count) + graph.Vertices.Count();
        var testedEdgePoints = ( from e in Strips select new EdgePoint(e, 0) ).ToArray();
        
        for (int i = 0; i < limit; i++)
        {
            var extremalVertex = GetExtremalVertices(strip).FirstOrDefault();
            if (extremalVertex != null)
            {
                PullTightExtremalVertex(extremalVertex, all: strip == null);
                continue;
            }
            
            var backTrack = GetBackTracks(strip, testedEdgePoints).FirstOrDefault();
            if (backTrack != null)
            {
                PullTightBackTrack(backTrack, testedEdgePoints, all: strip == null);
                continue;
            }

            break;
        }
    }

}