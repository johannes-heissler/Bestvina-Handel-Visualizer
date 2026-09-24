using System;
using System.Collections.Generic;
using System.Linq;

public class Inefficiency: EdgePoint
{
    public readonly List<Strip> edgesToFold;
    public int initialSegmentToFold;
    public readonly int order;
    public Inefficiency(Strip edge, int index, int order, List<Strip> edgesToFold, int initialSegmentToFold) : base(edge, index)
    {
        this.edgesToFold = edgesToFold;
        this.initialSegmentToFold = initialSegmentToFold;
        this.order = order;
    }

    public Inefficiency(EdgePoint edgePoint) : base(edgePoint.edge, edgePoint.index)
    {
        Strip a = edgePoint.DgBefore();
        Strip b = edgePoint.DgAfter();
        Strip aOld = a, bOld = b;
        for (int j = 0; j <= 2 * 1000; j++) // should be graph.Edges.Count but we don't have access to that here
        {
            if (a.Source != b.Source) throw new Exception("Bug: Two edges stopped starting from the same vertex after mapping with Dg.");

            if (!Equals(a, b))
            {
                aOld = a;
                bOld = b;
                a = a!.Dg;
                b = b!.Dg;
                continue;
            }

            order = j;
            if (j == 0) { // the case where the two edges are already the same.
                edgesToFold = new List<Strip> { a };
                initialSegmentToFold = 0;
                return;
            }

            if (AlwaysFoldAllEdgesWithShortSharedInitialSegment) // this is the way the algorithm is described in [BH]
#pragma warning disable CS0162 // Unreachable code detected
            {
                edgesToFold = (from e in FibredSurface.Star(aOld!.Source) where Equals(e.Dg, a) select e).OrderBy(s => s.Name).ToList<Strip>();
                initialSegmentToFold = Strip.SharedInitialSegment(edgesToFold);
            }
#pragma warning restore CS0162
            else // this is how it is handled in the first example in [BH]. This is "cooler" since the initial segment is longer.
            {
                initialSegmentToFold = Strip.SharedInitialSegment(new List<Strip> {aOld, bOld});
                var initialSegment = aOld.EdgePath.Take(initialSegmentToFold).ToList();
                edgesToFold = (from e in FibredSurface.Star(aOld.Source) where e.EdgePath.Take(initialSegmentToFold).SequenceEqual(initialSegment) select e).OrderBy(s => s.Name).ToList<Strip>();
            }
            return;
        }
        throw new Exception("Bug: Two edges in the same gate didn't eventually get mapped to the same edge under Dg.");

    }

    public bool FullFold => edgesToFold.Any(e => e.EdgePath.Count == initialSegmentToFold);

    public bool SameEdgesToFold(Inefficiency other) => edgesToFold.SequenceEqual(other.edgesToFold);

    protected override string ToString(bool colorful)
    {
        var fullyFoldedEdgesText = (from e in edgesToFold where e.EdgePath.Count == initialSegmentToFold select colorful ? e.ColorfulName : e.Name).ToCommaSeparatedString();
        var partiallyFoldedEdgesText = (from e in edgesToFold where e.EdgePath.Count > initialSegmentToFold select colorful ? e.ColorfulName : e.Name).ToCommaSeparatedString();

        var text = new System.Text.StringBuilder($"Inefficiency of order {order}: {ToShortString(colorful: colorful)}");
        var before = DgBefore();
        var after = DgAfter();
        for (int i = 1; i <= order; i++)
        {
            text.Append(" -> ...");
            text.AppendJoin(" ", before.EdgePath.Take(3).Reverse().Select(e => colorful ? e.Reversed().ColorfulName : e.Reversed().Name));
            text.Append("|");
            text.AppendJoin(" ", after.EdgePath.Take(3).Select(e => colorful ? e.ColorfulName : e.Name));
            text.Append("...");
            before = before.Dg; // cannot be null, can it?
            after = after.Dg;
        }
        text.Append("\nFold ").Append(fullyFoldedEdgesText);
        if (fullyFoldedEdgesText != "" && partiallyFoldedEdgesText != "")
            text.Append($" and initial segments of {partiallyFoldedEdgesText}");
        else if (partiallyFoldedEdgesText != "")
            text.Append($"initial segments of {partiallyFoldedEdgesText}");
        return $"{text} (based at {edgesToFold.First().Source.ColorfulName}).";
    }

    private const bool AlwaysFoldAllEdgesWithShortSharedInitialSegment = false;
}