using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using UnityEngine;

public partial class FibredSurface
{
    private static IEnumerable<(Strip, Strip)> InefficientConcatenations<T>(IEnumerable<Gate<T>> gates)
    {
        foreach (var gate in gates)
        {
            var edges = gate.Edges;
            for (var i = 0; i < edges.Count; i++)
            for (var j = i; j < edges.Count; j++)
                yield return (edges[i].Reversed(), edges[j]);
        }
    }

    public IEnumerable<Inefficiency> GetInefficiencies()
    {
        var gates = Gate.FindGates(graph);

        foreach (var (edge1, edge2) in InefficientConcatenations(gates))
        {
            var (edge1Rev, edge2Rev) = (edge1.Reversed(), edge2.Reversed());
            foreach (var strip in Strips)
            {
                for (int i = 1; i < strip.EdgePath.Count; i++)
                {
                    if (
                        (!strip.EdgePath[i - 1].Equals(edge1) || !strip.EdgePath[i].Equals(edge2)) && 
                        (!strip.EdgePath[i - 1].Equals(edge2Rev) || !strip.EdgePath[i].Equals(edge1Rev))
                    ) continue;
                    yield return new Inefficiency(new EdgePoint(strip, i));
                    goto found;
                }
            }
            // todo: valence two vertices with only two gates

            found: ;
        }
        
        // foreach (var edge in Strips) // inefficient!!
        // {
        //     for (int i = 0; i <= edge.EdgePath.Count; i++)
        //     {
        //         var inefficiency = CheckEfficiency(edge[i], gates);
        //         if (inefficiency != null)
        //         {
        //             if (i == 0 || i == edge.EdgePath.Count)
        //                 Debug.Log("Interpreted a valence-two gate-wise extremal vertex as an inefficiency.");
        //             yield return inefficiency;
        //         }
        //     }
        // }
    }

    AlgorithmSuggestion RemoveInefficiencySuggestion()
    {
        var f = GetInefficiencies().ToArray();
        if (f.Length == 0) return null;
        return new AlgorithmSuggestion(
            options: from inefficiency in f.OrderBy(inefficiency =>
                inefficiency.order + (inefficiency.FullFold ? 0 : 0.5f))
                select (inefficiency.ToSerializationString() as object, inefficiency.ToColorfulString()),
            description: "Remove an inefficiency.",
            buttons: new[] { AlgorithmSuggestion.inefficiencyAtOnceButton, AlgorithmSuggestion.inefficiencyInStepsButton, AlgorithmSuggestion.inefficiencyInFineStepsButton }
        );
    }

    [CanBeNull]
    public Inefficiency CheckEfficiency(EdgePoint edgePoint, List<Gate<Junction>> gates)
    {
        var a = edgePoint.DgBefore();
        var b = edgePoint.DgAfter();
        if (a == null || b == null) return null; // the edgePoints is actually a vertex of valence other than two

        if (!Equals(a.Source, b.Source))
            throw new($"The edge path is not an actual edge path at {edgePoint}!");
        if (!FindGate(a).Edges.Contains(b)) return null; // not inefficient

        return new Inefficiency(edgePoint);

        Gate<Junction> FindGate(Strip edge) => gates.First(gate => gate.Edges.Contains(edge));
        // The gates should form a partition of the set of oriented edges, so this should be well-defined.
    }

    /// <summary>
    /// At this point, we assume that the fibred surface is irreducible, i.e. that all inefficiencies are in H and that there are no
    /// valence-one junctions or edges that get mapped to junctions.
    /// </summary>
    public IEnumerator<AlgorithmSuggestion> RemoveInefficiencyInSteps(Inefficiency p, bool fineSteps = false)
    {
        if (p.order == 0 || p.edgesToFold.Count == Star(p.edgesToFold.First().Source).Count()) // backtrack or extremal valence 2 vertex
        {
            // The inefficiency is a back track or a valence two extremal vertex
            if (fineSteps)
                yield return new AlgorithmSuggestion(
                    PullTightString(GetLoosePositions(p.DgAfter())) + (
                       p.order != 0 ? $"This is a special case, as the inefficiency itself still has order { p.order}, but instead of folding the edges {p.edgesToFold.Select(e => e.ColorfulName).ToCommaSeparatedString()} we can just pull tight their origin which is an extremal vertex."
                      : "" )
                );
            PullTightAll(p.DgAfter());
            yield break;
        }

            

        var initialSegment = p.initialSegmentToFold;
        var edgesToFold = p.edgesToFold;

        while (true)
        {
            var edgeWithProblematicSplitPoint = edgesToFold.FirstOrDefault(edge =>
                Equals(p, new EdgePoint(edge, initialSegment))
            );
            if (edgeWithProblematicSplitPoint == null) break;
            if (fineSteps) // todo: feature. better text here
                yield return new AlgorithmSuggestion($"Decrease the length of the folded initial segment ({initialSegment} -> {initialSegment - 1}) because else the subdivision {edgeWithProblematicSplitPoint[initialSegment].ToColorfulString()} equals the inefficiency point at {p.ToShortString(colorful: true)}.");
            Debug.Log($"When folding the inefficiency (illegal turn) \"{p}\" we had to decrease the initial segment because the point where the illegal turn happened was the same as the subdivision / folding point");
            initialSegment--;
        }

        var updateEdgePoints = new List<EdgePoint>() { p };

        if (initialSegment == 0)
        {
            // the special case when one of the split points is at the point p
            // Split the edge c = Dg^{p.order}(p.edge) = Dg(p.edgesToFold[*]) at the first edge point.
            // If there is none, split g(c) = Dg(c) at the first edge point. If there is none ...
            var c = edgesToFold[0].Dg;
            var edgesToSplit = new List<Strip> { c };
            while (edgesToSplit[0].EdgePath.Count == 1 && edgesToSplit.Count <= 2 * graph.EdgeCount)
                edgesToSplit.Insert(0, edgesToSplit[0].Dg);
            if (edgesToSplit[0].EdgePath.Count == 1)
            {
                HandleInconsistentBehavior(
                    "Weird: This edge only gets mapped to single edges under g. This shouldn't happen at this stage unless f is efficient and permutes the edges, g is a homeo, the growth is λ=1 and f is periodic. But it isn't efficient because we are in this method.");
                yield break;
            }
            if (fineSteps) 
                yield return new AlgorithmSuggestion(
                    $"Split the edge c = {c!.ColorfulName}" + 
                        ( edgesToSplit.Count <= 1 ? "" :
                            " and its iterates " + edgesToSplit.Skip(1).Select(e => e.ColorfulName).ToCommaSeparatedString()
                        ) + " to shorten the initial segment to fold.");
            updateEdgePoints.AddRange(edgesToFold.Select(edge => new EdgePoint(edge, 0)));

            foreach (var edge in edgesToSplit) // we split the last edge first, so that we can split the one before and so on.
                SplitEdge(edge[1], updateEdgePoints);

            edgesToFold = updateEdgePoints.Skip(1).Select(edgePoint => edgePoint.edge).ToList();
            initialSegment = 1;
            // this relies on the EdgePath of these edges having updated during the split of the edge c. 
            // They started with edge.EdgePath = c,..., now they have edge.EdgePath = c1,c2,...
        }

        foreach (var suggestion in FoldInitialSegmentInSteps(edgesToFold, initialSegment, updateEdgePoints))
        {
            if (fineSteps)
                yield return suggestion;
        }

        // Now p is an inefficiency of degree one lower.

        var pNew = updateEdgePoints[0];
        var newInefficiency = CheckEfficiency(pNew, Gate.FindGates(graph));

        if (newInefficiency == null || newInefficiency.order != p.order - 1)
           throw new Exception($"Bug: The inefficiency was not turned into an efficiency of order one less: {p} was turned into {newInefficiency ?? pNew}");
        
        if (newInefficiency.order > 0)
            yield return new AlgorithmSuggestion($"Remove inefficiency {newInefficiency}");
        
        var subEnumerator = RemoveInefficiencyInSteps(newInefficiency, fineSteps);
        while (subEnumerator.MoveNext())
            yield return subEnumerator.Current;
    }

}