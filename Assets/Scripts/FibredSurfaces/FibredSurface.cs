using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using FibredGraph = QuikGraph.UndirectedGraph<Junction, UnorientedStrip>;

/// <summary>
/// This implements the Bestvina-Handel algorithm.
/// All methods are in-place. Copy the graph before.
/// </summary>
public partial class FibredSurface : IPatchedDrawnsformable
{
    public readonly FibredGraph graph;
    public GeodesicSurface surface;
    
    /// <summary>
    /// The peripheral subgraph P contains one loop around every puncture of the surface apart from one orbit of punctures.
    /// P is assumed to have only valence-two vertices (that have higher valence in the main graph).
    /// We also assume that g acts on it as a graph automorphism.
    /// </summary>
    public readonly HashSet<UnorientedStrip> peripheralSubgraph;
    
    public HashSet<Junction> peripheralSubgraphVertices => peripheralSubgraph.SelectMany(e => new[] { e.Source, e.Target }).ToHashSet();
    
    public event Action<string> OnError;
    private void HandleInconsistentBehavior(string errorMessage) => OnError?.Invoke(errorMessage);

    
    
    public string Name { get; set; }

    public Color Color
    {
        get => graph.Vertices.First().Color;
        set => Debug.LogError("The graph color should not be set.");
    }
    
    public IEnumerable<IDrawnsformable> Patches => isTrainTrack 
        ? from edge in Strips select edge.Curve
        : graph.Vertices.Concat<IDrawnsformable>(from edge in Strips select edge.Curve);

    public IEnumerable<UnorientedStrip> Strips => graph.Edges;
    
    public IEnumerable<UnorientedStrip> StripsOrdered => graph.Vertices.SelectMany(
        v => StarOrdered(v).Select(e => e.UnderlyingEdge)
    ).Distinct().OrderByDescending(
        edge => edge.EdgePath.Count    
    );

    public IEnumerable<Strip> OrientedEdges => Strips.Concat<Strip>(Strips.Select(edge => edge.Reversed()));

    public IEnumerable<NamedEdgePath> UsedNamedEdgePaths => Strips.SelectMany(e => e.EdgePath.NamedEdgePaths).Distinct().
        OrderBy(v => v.name);

    
    
    public FibredSurface(FibredGraph graph, GeodesicSurface surface, HashSet<UnorientedStrip> peripheralSubgraph = null)
    {
        this.graph = graph;
        this.surface = surface;
        this.peripheralSubgraph = peripheralSubgraph ?? new HashSet<UnorientedStrip>();
    }

    public FibredSurface(IList<(Curve, string, string, string)> edgeDescriptions, GeodesicSurface surface,
        IEnumerable<string> peripheralEdges = null, IDictionary<string, IDrawnsformable> junctionDrawables = null)
    {
        this.surface = surface;
        junctionDrawables ??= new Dictionary<string, IDrawnsformable>();
        peripheralEdges ??= new List<string>();
        graph = new FibredGraph(true); // up here, so that the junctions reference this

        var vertexNames =
            (from v in edgeDescriptions select v.Item2)
            .Concat(from v in edgeDescriptions select v.Item3).ToHashSet();
        var junctions = new Dictionary<string, Junction>(
            from name in vertexNames
            select new KeyValuePair<string, Junction>(name,
                new Junction(this,
                    junctionDrawables.TryGetValue(name, out var displayable)
                        ? displayable
                        : edgeDescriptions.FirstOrDefault(tuple => tuple.Item2 == name).Item1?.StartPosition
                          ?? edgeDescriptions.FirstOrDefault(tuple => tuple.Item3 == name).Item1?.EndPosition, 
                    name: name,
                    color: NextVertexColor()) // image is set below
            )
        );
        // Plain loop instead of a LINQ `let` query (IL2CPP/WebGL signature-mismatch crash, see CurveVisualizer).
        var strips = new List<UnorientedStrip>();
        foreach (var (curve, source, target, _) in edgeDescriptions)
        {
            var startVector = curve.StartVelocity.Coordinates(surface);
            var endVector = -curve.EndVelocity.Coordinates(surface);
            strips.Add(new UnorientedStrip(
                curve,
                junctions[source],
                junctions[target],
                EdgePath.Empty,
                this,
                startVector.Angle(),
                endVector.Angle(),
                addToGraph: true
            ));
        }

        graph.AddVerticesAndEdgeRange(strips);
        
        // fix vertex targets
        foreach (var strip in OrientedEdges)
        {
            if (strip.Source.image != null && strip.Source.image != strip.Dg?.Source)
                Debug.LogError(
                    $"Two edges at the same vertex have images that don't start at the same vertex! g({strip.Name}) = {string.Join(' ', strip.EdgePath.Select(e => e.Name))} starts at o(g({strip.Name})) = {strip.Dg?.Source}, but we already set g(o({strip.Name})) = {strip.Source.image}.");
            strip.Source.image ??= strip.Dg?.Source;
        }

        var peripheralEdgesSet = peripheralEdges.Select(n => n.ToLower()).ToHashSet();
        peripheralSubgraph =
            Strips.Where(e => peripheralEdgesSet.Contains(e.Name) ).ToHashSet();
        
        SetMap(edgeDescriptions.ToDictionary(tuple => tuple.Item1.Name, tuple => tuple.Item4), GraphMapUpdateMode.Replace);
    }


    public bool Copyable => currentAlgorithmRun is null;
    
    IPatchedDrawnsformable IDrawnsformable<IPatchedDrawnsformable>.Copy() => Copy();

    public FibredSurface Copy()
    {
        var newGraph = new FibredGraph(true);
        var newPeripheralSubgraph = new HashSet<UnorientedStrip>();
        var newFibredSurface = new FibredSurface(newGraph, surface, newPeripheralSubgraph) { ignoreBeingReducible = ignoreBeingReducible };

        var newVertices = new Dictionary<Junction, Junction>(
            from oldJunction in graph.Vertices
            select new KeyValuePair<Junction, Junction>(oldJunction, 
                oldJunction.Copy(newFibredSurface)
            )
        );

        var newEdges = new Dictionary<UnorientedStrip, UnorientedStrip>(
            from oldStrip in Strips
            select new KeyValuePair<UnorientedStrip, UnorientedStrip>(oldStrip, 
                oldStrip.CopyUnoriented(newFibredSurface,
                    source: newVertices[oldStrip.Source],
                    target: newVertices[oldStrip.Target]
                )
            )
        );

        foreach (var (oldJunction, newJunction) in newVertices) 
            if (oldJunction.image != null)
                newJunction.image = newVertices[oldJunction.image];

        foreach (var (edge, newEdge) in newEdges)
            newEdge.EdgePath = edge.EdgePath.Replace(strip => newEdges[strip]);

        newGraph.AddVerticesAndEdgeRange(newEdges.Values);

        newPeripheralSubgraph.UnionWith(
            from edge in peripheralSubgraph select newEdges[edge]
        );

        return newFibredSurface;
    }

    private Curve GetBasicGeodesic(Point start, Point end, string name)
    {
        var surface = this.surface;
        if (surface is ModelSurface modelSurface)
            return modelSurface.GetBasicGeodesic(start, end, name);
        Debug.LogWarning($"The surface {surface} is not a model surface.");
        return surface.GetGeodesic(start, end, name);
    }
}