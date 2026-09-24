using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class FibredSurface
{

    public static readonly IEnumerable<string> edgeNames = new[]
    {
        "a", "b", "c", "d", /*"e", "f", "g",*/ "x", "y", "z", "u", "h", "i", "j", "k", "l", "m", "n", "o",
        "α", "β", "γ", "δ", "ε", "ζ", "θ", "κ", "λ", "μ", "ξ", "π", "ρ", "σ", "τ", "φ", "ψ", "ω",
    }.Concat(from i in Enumerable.Range(1, 1000) select $"e{i}");

    public static readonly IEnumerable<string> vertexNames = new[]
    {
        "v", "w", "p", "q", "r", "s", "t",
    }.Concat(from i in Enumerable.Range(1, 1000) select $"v{i}");
    
    public static readonly IReadOnlyList<Color> edgeColors = Curve.colors;

    public static readonly IReadOnlyList<Color> vertexColors = new Color[]
    {
        Color.black,
        new Color32(26, 105, 58, 255),
        new Color32(122, 36, 0, 255),
        new Color(0.3f, 0.3f, 0.3f), 
        new Color32(50,6,99, 255)
    };


    private HashSet<string> usedEdgeNames;

    private void UpdateUsedEdgeNames()
    {
        usedEdgeNames = graph.Edges.Select(edge => edge.Name).Concat(
            UsedNamedEdgePaths.Select(edgePath => edgePath.name)).ToHashSet();
    }

    public string NextEdgeName()
    {
        UpdateUsedEdgeNames();
        return edgeNames.First(
            name => !usedEdgeNames.Contains(name)
        );
    }

    public string NextEdgeNameGreek() {
        UpdateUsedEdgeNames();
        return edgeNames.Concat(from i in Enumerable.Range(1, 1000) select $"ε{i}").First(
            name => !usedEdgeNames.Contains(name) && name[0] > 'z'
        );
    }

    public string NextVertexName()
    {
        var usedVertexNames = graph.Vertices.Select(vertex => vertex.Name).ToHashSet();
        return vertexNames.First(
            name => !usedVertexNames.Contains(name)    
        );
    }

    public Color NextEdgeColor()
    {
        var colorUsage = edgeColors.ToDictionary(c => c, c => 0);
        foreach (var strip in graph.Edges)
        {
            if (colorUsage.ContainsKey(strip.Color))
                colorUsage[strip.Color]++;
            else
            {
                colorUsage[strip.Color] = 1;
                Debug.LogWarning($"The color {strip.Color} of edge {strip} is not in the list of edge colors.");
            }
        }
        var (leastUsedColor, _) = colorUsage.Keys.ArgMin(c => colorUsage[c]);
        return leastUsedColor;
    }

    public Color NextVertexColor()
    {
        var colorUsage = vertexColors.ToDictionary(c => c, c => 0);
        foreach (var junction in graph.Vertices)
        {
            if (colorUsage.ContainsKey(junction.Color))
                colorUsage[junction.Color]++;
            else
            {
                colorUsage[junction.Color] = 1;
                Debug.LogWarning($"The color {junction.Color} of junction {junction} is not in the list of vertex colors.");   
            }
        }
        var (leastUsedColor, _) = colorUsage.Keys.ArgMin(c => colorUsage[c]);
        return leastUsedColor;
    }

}