using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

public partial class FibredSurface
{
    
    public Dictionary<Strip, EdgePath> ParseMap(string text)
    {
        string[] lines = text.Split(',', '\n');
        var graphMap = new Dictionary<string, string>();
        foreach (string line in lines)
        {
            string trim = line.Trim();
            if (trim == "") 
                continue;
            var match = Regex.Match(trim, @"g\s*\((.+)\)\s*=(.*)");
            if (!match.Success)
                match = Regex.Match(trim, @"(.+)->(.*)");
            if (!match.Success)
                match = Regex.Match(trim, @"(.+)↦(.*)");
            if (!match.Success)
                match = Regex.Match(trim, @"(.+):=(.*)");
            if (!match.Success)
                match = Regex.Match(trim, @"(.+)=(.*)");
            if (!match.Success)
                throw new ArgumentException("The input should be in the form \"g(a) = a B A, g(b) = ...\" or \" a -> a B A, b -> ...\", plus potentially definitions like \"x := a B A\".");

            string name = match.Groups[1].Value.Trim();
            string imageText = match.Groups[2].Value;
            graphMap[name] = imageText;
        }
        return ParseMap(graphMap);
    }

    public Dictionary<Strip, EdgePath> ParseMap(IReadOnlyDictionary<string, string> map)
    {
        var stripNames = Strips.Select(s => s.Name).ToHashSet();
        var definitionNames = map.Keys.Select(k => k.ToLower()).ToHashSet();
        
        var missingNames = stripNames.ToHashSet();
        missingNames.ExceptWith(definitionNames);
        definitionNames.ExceptWith(stripNames);
        
        var edgeDict = OrientedEdges.ToDictionary(e => e.Name);
        var definitions = new List<NamedEdgePath>();
        foreach (var definitionName in definitionNames)
            definitions.Add(new NamedEdgePath(
                    EdgePath.FromString(map[definitionName], Strips, definitions),
                    definitionName
                )
            );

        var result = new Dictionary<Strip, EdgePath>();
        foreach (var (name, edgePathText) in map)
            if (edgeDict.TryGetValue(name, out var strip))
                result[strip] = EdgePath.FromString(edgePathText, Strips, definitions);
        foreach (var missingKey in missingNames) 
            result[edgeDict[missingKey]] = new NormalEdgePath(edgeDict[missingKey]); 
            // identity on the other edges
        return result;
    }

    public void SetMap(string map, GraphMapUpdateMode mode) =>
        SetMap(ParseMap(map), mode);
    
    public void SetMap(IReadOnlyDictionary<string, string> map, GraphMapUpdateMode mode) =>
        SetMap(ParseMap(map), mode);
    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="enteredMap">This must contain exactly one of {e, e.Reversed()} for any edge of the graph</param>
    /// <param name="mode"></param>
    /// <exception cref="ArgumentException"></exception>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public void SetMap(IReadOnlyDictionary<Strip, EdgePath> enteredMap, GraphMapUpdateMode mode)
    {
        if (enteredMap.Keys.Any(e => e.graph != graph))
            throw new ArgumentException("The edges in the map must be from the same graph as this fibred surface.");
        switch (mode)
        {
            case GraphMapUpdateMode.Replace:
                foreach (var edge in enteredMap.Keys)
                    edge.EdgePath = enteredMap[edge];
                break;
            case GraphMapUpdateMode.Precompose:
                var oldMap = OrientedEdges.ToDictionary(e => e, e => e.EdgePath);
                foreach (var edge in enteredMap.Keys) 
                    edge.EdgePath = enteredMap[edge].Replace(e => oldMap[e]);
                break;
            case GraphMapUpdateMode.Postcompose:
                var fullDict = new Dictionary<Strip, EdgePath>(enteredMap);
                foreach (var (edge, image) in enteredMap) 
                    fullDict[edge.Reversed()] = image.Inverse;
                foreach (var edge in Strips) 
                    edge.EdgePath = edge.EdgePath.Replace(e => fullDict[e]);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }
    }

    public PushingPath ParsePointPush(string text)
    {
        var match = Regex.Match(text, @"p.*\((.+)\)", RegexOptions.IgnoreCase);
        if (!match.Success)
            throw new ArgumentException("The input should be in the form \"Push(a B A), start left\" or \"p_left(a B A)\" or similar.");
        var startLeft = text.Contains("left", StringComparison.OrdinalIgnoreCase);
        var edgePath = EdgePath.FromString(match.Groups[1].Value, Strips);
        return new PushingPath(edgePath, startLeft);
    }
}