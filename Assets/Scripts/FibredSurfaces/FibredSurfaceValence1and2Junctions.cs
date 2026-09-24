using System;
using System.Collections.Generic;
using System.Linq;

public partial class FibredSurface
{
    public IEnumerable<Junction> GetValenceOneJunctions() =>
        from vertex in graph.Vertices where Star(vertex).Count() == 1 select vertex;
    
    
    public AlgorithmSuggestion ValenceOneJunctionsSuggestion()
    {
        var options = (
            from v in GetValenceOneJunctions()
            select (v.Name as object, v.ColorfulName)
        ).ToArray(); 
        if (options.Length == 0) return null;
        return new AlgorithmSuggestion(
            options: options,
            description: "Remove a valence-one junction. WHAT THE HECK?", buttons: new[] { AlgorithmSuggestion.valence1Button },
            allowMultipleSelection: true
        );
    }

    public void RemoveValenceOneJunction(Junction junction)
    {
        var star = Star(junction).ToArray();
        if (star.Length != 1) throw new($"Supposed valence-one junction has valence {star.Length}");
        var removedStrip = star.First();
        Junction otherJunction = removedStrip.Target;
        graph.RemoveVertex(junction);
        graph.RemoveEdge(removedStrip.UnderlyingEdge);
        foreach (var strip in Strips)
            strip.EdgePath = strip.EdgePath.Replace(edge => Equals(edge.UnderlyingEdge, removedStrip.UnderlyingEdge) ? null : edge); 
        foreach (var vertex in graph.Vertices)
            if (vertex.image == junction)
                vertex.image = otherJunction;
        // todo: isotopy or make the junction bigger to include the removed strip
    }

    public IEnumerable<Junction> GetValenceTwoJunctions() =>
        graph.Vertices.Where(vertex =>
        {
            var star = Star(vertex);
            return star.Count() == 2 && !star.First().Equals(star.Last().Reversed()); // no self-edge
        });

    public AlgorithmSuggestion ValenceTwoSuggestion()
    {
        var valenceTwoJunctions = GetValenceTwoJunctions().ToArray();
        if (valenceTwoJunctions.Length <= 0) return null;
        return new AlgorithmSuggestion(
            options: from v in valenceTwoJunctions select (v.Name as object, v.ToColorfulString()),
            description: "Remove a valence-two junction.", 
            buttons: new[] { AlgorithmSuggestion.valence2SelectedButton, AlgorithmSuggestion.valence2AllButton },
            allowMultipleSelection: true
        );
    }

    /// <summary>
    /// Removes a valence two junction by keeping one of the two strips and make it replace the concatenation of the two strips.
    /// The strip to be removed is chosen as the one in the prePeriphery, or the one with the larger width in the Frobenius-Perron eigenvector.
    /// </summary>
    /// <param name="junction"></param>
    /// <param name="removeStrip"></param>
    public void RemoveValenceTwoJunction(Junction junction, Strip removeStrip = null)
    {
        var star = Star(junction).ToArray();
        if (star.Length != 2) throw new($"Supposed valence-two junction has valence {star.Length}");
        if (star[0].Equals(star[1].Reversed()))
            throw new Exception("Cannot remove a valence-two junction that has a self-edge");

        if (removeStrip == null)
        {
            var prePeriphery = PrePeriphery();
            if (prePeriphery.Contains(star[0].UnderlyingEdge)) removeStrip = star[0];
            else if (prePeriphery.Contains(star[1].UnderlyingEdge)) removeStrip = star[1];
            else
            {
                FrobeniusPerron(true, out var λ, out var widths, out var lengths, out var matrix);
                removeStrip = widths[star[0].UnderlyingEdge] > widths[star[1].UnderlyingEdge] ? star[0] : star[1];
            }
        }

        Strip keptStrip = Equals(star[0].UnderlyingEdge, removeStrip.UnderlyingEdge) ? star[1] : star[0];
        Junction enlargedJunction = removeStrip.Source == junction ? removeStrip.Target : removeStrip.Source;
        // two options: a) isotope along removeStrip to move junctions that map there to the "enlargedJunction" (but don't actually enlarge it)
        // and enlarge keptStrip to include the removed strip: keptStrip' = removedStrip.Reverse u removeJunction u keptStrip.
        // for better visuals (fewer curves in junctions), we start with this option
        // or: b) include removedStrip and junction into the enlargedJunction and isotope along f(removeStrip), so that it lands in f(enlJun).
        // This isotopy could be faked pretty well! Just display f(keptStrip) as f(removedStrip).Reverse u f(removeJunction) u f(keptStrip)
        // and f(enlJun) as what it was before enlarging.
        // In both cases, the same thing happens combinatorially
        var name = keptStrip.Name; 
        if (name.ToLower().StartsWith(removeStrip.Name.ToLower()))
            name = removeStrip.Name;
        var newStrip = keptStrip.Copy(name: name,
            source: enlargedJunction,
            orderIndexStart: removeStrip.OrderIndexEnd,
            curve: removeStrip.Curve.Reversed().Concatenate(keptStrip.Curve),
            edgePath: removeStrip.EdgePath.Inverse.Concat(keptStrip.EdgePath)
        );
        newStrip.Color = keptStrip.Color;

        graph.RemoveVertex(junction); // removes the old edges as well
        graph.AddVerticesAndEdge(newStrip.UnderlyingEdge);

        foreach (var strip in Strips)
            strip.EdgePath = strip.EdgePath.Replace(e => 
                    Equals(e.UnderlyingEdge, removeStrip.UnderlyingEdge) ?
                        null : 
                        e.Equals(keptStrip) ?
                            newStrip :
                            e.Equals(keptStrip.Reversed()) ?
                                newStrip.Reversed() :
                                e
                );
        foreach (var vertex in graph.Vertices)
            if (vertex.image == junction)
                vertex.image = enlargedJunction;
    }

}