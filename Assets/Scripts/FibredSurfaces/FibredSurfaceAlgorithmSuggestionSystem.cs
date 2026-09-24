using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using FibredGraph = QuikGraph.UndirectedGraph<Junction, UnorientedStrip>;

public partial class FibredSurface
{

    public class AlgorithmSuggestion
    {
        // TODO: Feature. Add a way to highlight the edges that are selected in the options or even add an animation
         public readonly IReadOnlyCollection<(object, string)> options;
         public readonly string description;
         public readonly IReadOnlyCollection<string> buttons;
         public readonly bool allowMultipleSelection;
        
        public AlgorithmSuggestion(string description)
        {
            this.description = description;
            this.buttons = new[] { generalSubroutineContinueButton };
            this.options = Array.Empty<(object, string)>();
        }
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="options">A list of options, each of which will be displayed as a toggleable button. Each option is of the form (technical option needed for the ApplySuggestions method, (colorful) description for display on the button).</param>
        /// <param name="description">The description for the suggestion at the top of the list.</param>
        /// <param name="buttons">The buttons at the bottom of the list.</param>
        /// <param name="allowMultipleSelection">If one can select multiple options.</param>
        public AlgorithmSuggestion(IEnumerable<(object, string)> options, string description, IEnumerable<string> buttons, bool allowMultipleSelection = false)
        {
            this.options = options as IReadOnlyCollection<(object, string)> ?? options.ToArray();
            this.description = description;
            this.buttons = buttons as IReadOnlyCollection<string> ?? buttons.ToArray();
            this.allowMultipleSelection = allowMultipleSelection;
        }

        public const string peripheralInefficiencyButton = "Fold Selected";
        public const string generalSubroutineContinueButton = "Continue";
        public const string collapseInvariantSubforestContinueButton = "Contract component";
        public const string trainTrackButton = "Convert to train track";
        public const string subforestAtOnceButton = "Collapse at once";
        public const string subforestInStepsButton = "Collapse in steps";
        public const string valence1Button = "Remove valence-one junction";
        public const string inefficiencyAtOnceButton = "Remove at once";
        public const string inefficiencyInStepsButton = "Remove in steps";
        public const string inefficiencyInFineStepsButton = "Remove in fine steps";
        public const string foldMoveButton = "Move";
        public const string foldMoveAltButton = "Move, but ignore the edge along which we move";
        public const string tightenSelectedButton = "Tighten selected";
        public const string tightenAllButton = "Tighten all";
        public const string absorbPAtOnceButton = "Absorb at once";
        public const string absorbPInStepsButton = "Absorb in steps";
        public const string valence2SelectedButton = "Remove Selected";
        public const string valence2AllButton = "Remove All";
        public const string ignoreReducibleButton = "Ignore and continue";
        public const string reductionKeepInnerButton = "Reduce to subgraph";
        public const string reductionKeepOuterButton = "Reduce subgraph to periphery";
    }

    IEnumerator<AlgorithmSuggestion> currentAlgorithmRun;
    IEnumerable<object> selectedOptionsDuringAlgorithmPause; // this is to communicate with the currentAlgorithmicRun.
    string selectedButtonDuringAlgorithmPause; // this is to communicate with the currentAlgorithmicRun.
    public AlgorithmSuggestion NextSuggestion()
    {
        // todo? add a set of ITransformables to highlight with each option?
        // todo? OnHover?
        
        if(isTrainTrack)
            return new AlgorithmSuggestion(
                options: new (object, string)[] { },
                description: "What a nice train track.",
                buttons: new string[] { }
            );
        
        if (currentAlgorithmRun != null)
            return currentAlgorithmRun.Current;
        
        var nextSuggestion = InvariantSubforestsSuggestion();
        if (nextSuggestion != null)
            return nextSuggestion;
        
        nextSuggestion = TighteningSuggestion();
        if (nextSuggestion != null)
            return nextSuggestion;
        
        nextSuggestion = ValenceOneJunctionsSuggestion();
        if (nextSuggestion != null)
            return nextSuggestion;
        
        nextSuggestion = AbsorbIntoPeripherySuggestion();
        if (nextSuggestion != null)
            return nextSuggestion;

        nextSuggestion = FiniteOrderSuggestion();
        if (nextSuggestion != null)
            return nextSuggestion;
        
        if (!ignoreBeingReducible)
        {
            nextSuggestion = ReducibilitySuggestion();
            if (nextSuggestion != null)
                return nextSuggestion;
        }

        nextSuggestion = ValenceTwoSuggestion();
        if (nextSuggestion != null)
            return nextSuggestion;
        
        nextSuggestion = RemovePeripheralInefficiencySuggestion();
        if (nextSuggestion != null)
            return nextSuggestion;
        
        nextSuggestion = RemoveInefficiencySuggestion();
        if (nextSuggestion != null)
            return nextSuggestion;
        
        if (!isTrainTrack /*&& !ignoreBeingReducible*/)
            return new AlgorithmSuggestion(
                options: new (object, string)[] { },
                description: "The algorithm is finished. The graph map is a train track map and we can thus turn the fibred surface into a train track.",
                buttons: new[] { AlgorithmSuggestion.trainTrackButton }
            );
                    
        return new AlgorithmSuggestion(
            options: new (object, string)[] { },
            description: "The algorithm is finished.",
            buttons: new string[] { }
        );
    }

    private bool ignoreBeingReducible;
    private bool isTrainTrack = false;
    public void ApplySuggestion(IEnumerable<object> suggestion, string button)
    {
        switch (button)
        {
            case AlgorithmSuggestion.foldMoveButton:
            case AlgorithmSuggestion.foldMoveAltButton: // continue the algorithmic steps started with AlgorithmSuggestion.inefficiencyInStepsButton
            case AlgorithmSuggestion.generalSubroutineContinueButton:
            case AlgorithmSuggestion.collapseInvariantSubforestContinueButton:
            // TODO: Add the ones for collapsing into Q
                if (currentAlgorithmRun == null)
                    throw new ArgumentException("There is no current algorithmic list to continue.");

                selectedOptionsDuringAlgorithmPause = suggestion;
                selectedButtonDuringAlgorithmPause = button;
                
                // the MoveNext() runs the RemoveInefficiencyInSteps method until the next suggestion is returned or until the end.
                if (!currentAlgorithmRun.MoveNext()) 
                    currentAlgorithmRun = null; // done
                
                selectedOptionsDuringAlgorithmPause = null;
                selectedButtonDuringAlgorithmPause = null;
                break;
            case AlgorithmSuggestion.subforestAtOnceButton:
                if (suggestion.FirstOrDefault() is not IEnumerable<string> subforestEdges)
                    break;
                using (var localAlgorithmicRun = CollapseSubforest(subforestEdges))
                {
                    while (localAlgorithmicRun.MoveNext())
                    { } // run until the end
                }

                break;
            case AlgorithmSuggestion.subforestInStepsButton:
                if (suggestion.FirstOrDefault() is not IEnumerable<string> subforestEdges2)
                    break;
                // only select one subforest at a time because they might intersect and then the other subforests wouldn't be inside the new graph anymore.
                
                
                currentAlgorithmRun = CollapseSubforest(subforestEdges2);
                if (!currentAlgorithmRun.MoveNext())
                {
                    currentAlgorithmRun.Dispose();
                    currentAlgorithmRun = null; // if it had just one step 
                }

                break;
            case AlgorithmSuggestion.tightenSelectedButton:
                foreach (var o in suggestion)
                {
                    if (o is not string name)
                    {
                        Debug.LogError($"Weird type of suggestion for Tighten Selected: {o}");
                        continue;
                    }
                    PullTightAll(name);
                }
                break;
            case AlgorithmSuggestion.tightenAllButton:
                PullTightAll();
                break;
            case AlgorithmSuggestion.valence1Button:
                foreach (var o in suggestion)
                    if (o is string name)
                        RemoveValenceOneJunction(graph.Vertices.First(v => v.Name == name));    
                    else
                        Debug.LogError($"Weird type of suggestion for Valence-1 Removal: {o}");
                break;
            case AlgorithmSuggestion.absorbPAtOnceButton:
                if (suggestion.FirstOrDefault() is not FibredGraph QwithoutP)
                    break;
                foreach (var _ in AbsorbIntoPeriphery(QwithoutP))
                {} // run until the end
                break;
            case AlgorithmSuggestion.absorbPInStepsButton:
                if (suggestion.FirstOrDefault() is not FibredGraph QwithoutP2)
                    break;
                currentAlgorithmRun = AbsorbIntoPeriphery(QwithoutP2).GetEnumerator();
                if (!currentAlgorithmRun.MoveNext())
                {
                    currentAlgorithmRun.Dispose();
                    currentAlgorithmRun = null; // if it had just one step 
                }
                break;
            case AlgorithmSuggestion.valence2SelectedButton: // valence 2 junctions
                foreach (var o in suggestion.ToArray())
                {
                    if (o is string name)
                        RemoveValenceTwoJunction(graph.Vertices.First(v => v.Name == name), null);
                    else
                        Debug.LogError($"Weird type of suggestion for Remove Selected Valence-2 Vertex: {o}");
                    
                    // at some point there might be invariant subforests!
                    if (GetInvariantSubforests().Any())
                        break;
                }

                break;
            case AlgorithmSuggestion.valence2AllButton: // valence 2 junctions
                var maxSteps = graph.VertexCount;
                for (int i = 0; i < maxSteps; i++)
                {
                    var junction = GetValenceTwoJunctions().FirstOrDefault();
                    // at some point there might be invariant subforests!
                    if (GetInvariantSubforests().Any())
                        break;
                    
                    if (junction == null) break;
                    RemoveValenceTwoJunction(junction);
                }

                break;
            case AlgorithmSuggestion.peripheralInefficiencyButton: // peripheral inefficiencies
                var firstOption = suggestion.FirstOrDefault();
                if (firstOption is not IEnumerable<string> edges)
                {
                    Debug.LogError($"Weird type of option for Fold Selected: {firstOption}");
                    break;
                }
                var edgeDict = OrientedEdges.ToDictionary(e => e.Name);
                var edgesToFold = edges.Select(name => edgeDict[name]).ToArray();
                RemovePeripheralInefficiency(edgesToFold); // todo!
                break;
            case AlgorithmSuggestion.inefficiencyAtOnceButton: 
                var inefficiency = ExtractInefficiencyFromSuggestion();                
                var algorithmicList = RemoveInefficiencyInSteps(inefficiency, fineSteps: false);
                while (algorithmicList.MoveNext())
                { }
                break;
            case AlgorithmSuggestion.inefficiencyInStepsButton: 
                inefficiency = ExtractInefficiencyFromSuggestion();
                currentAlgorithmRun = RemoveInefficiencyInSteps(inefficiency, fineSteps: false);
                if (!currentAlgorithmRun.MoveNext()) 
                    currentAlgorithmRun = null; // if it had just one step (degree zero, so shouldn't happen)
                break;
            case AlgorithmSuggestion.inefficiencyInFineStepsButton:
                inefficiency = ExtractInefficiencyFromSuggestion();
                currentAlgorithmRun = RemoveInefficiencyInSteps(inefficiency, fineSteps: true);
                if (!currentAlgorithmRun.MoveNext()) 
                    currentAlgorithmRun = null; // if it had just one step (degree zero, so shouldn't happen)
                break;
            case AlgorithmSuggestion.ignoreReducibleButton:
                ignoreBeingReducible = true;
                break;
            case AlgorithmSuggestion.reductionKeepInnerButton:
                if (suggestion.FirstOrDefault() is not IEnumerable<string> preservedSubgraphEdges)
                    break;
                
                currentAlgorithmRun = ReduceToSubgraph(preservedSubgraphEdges).GetEnumerator();
                if (!currentAlgorithmRun.MoveNext())
                {
                    currentAlgorithmRun.Dispose();
                    currentAlgorithmRun = null; // if it had just one step 
                }
                break;
            case AlgorithmSuggestion.reductionKeepOuterButton:
                if (suggestion.FirstOrDefault() is not IEnumerable<string> preservedSubgraphEdges2)
                    break;
                currentAlgorithmRun = ReduceSubgraphToPeriphery(preservedSubgraphEdges2).GetEnumerator();
                if (!currentAlgorithmRun.MoveNext())
                {
                    currentAlgorithmRun.Dispose();
                    currentAlgorithmRun = null; // if it had just one step 
                }
                break;
            case AlgorithmSuggestion.trainTrackButton:
                ConvertToTrainTrack();
                isTrainTrack = true;
                break;
            default:
                HandleInconsistentBehavior("Unknown button.");
                break;
        }
        // sanity tests
        CheckIntegrityOfGraphMap();
        return;

        Inefficiency ExtractInefficiencyFromSuggestion()
        {
            var selectedOption = suggestion.FirstOrDefault();
            if (selectedOption is not string inefficiencySerializationString)
                throw new ArgumentException($"Weird type of suggestion for Remove Inefficiency: {selectedOption}");
            
            return new Inefficiency(EdgePoint.Deserialize(inefficiencySerializationString, OrientedEdges));
        }
    }

    public bool ApplyNextSuggestion()
    {
        var suggestion = NextSuggestion();
        if (suggestion == null) return false; // done!
        ApplySuggestion(from tuple in suggestion.options select tuple.Item1, suggestion.buttons.First()); // selected ones (all!)
        return true;
    }

    public void BestvinaHandelAlgorithm()
    {
        var limit = 20 * graph.EdgeCount;
        for (int i = 0; i < limit; i++)
        {
            if (!ApplyNextSuggestion()) break;
        }
        // todo: save result (reducible / growth)
    }

    private void CheckIntegrityOfGraphMap()
    {
        var edgeWithBrokenEdgePath = Strips.FirstOrDefault(s =>
            s.EdgePath.Count != 0 && !Enumerable.Range(0, s.EdgePath.Count - 1)
                .All(i => s.EdgePath[i].Target == s.EdgePath[i + 1].Source));
        if (edgeWithBrokenEdgePath != null)
            HandleInconsistentBehavior($"The edge {edgeWithBrokenEdgePath} has a broken edge path.");
        var brokenSelfEdge = Strips.FirstOrDefault(s => s.Source == s.Target && s.EdgePath.Count == 0);
        if (brokenSelfEdge != null)
            HandleInconsistentBehavior(
                $"The edge {brokenSelfEdge} is a self-edge that gets mapped into a vertex! This should not happen as we assume that the fibred surface is embedded as a deformation retract of the surface and thus no loop should be mapped to a vertex (no non-forest into a forest).");
        var duplicateName = graph.Edges.FirstDuplicate(e => e.Name);
        if (duplicateName != null)
            HandleInconsistentBehavior($"The name of {duplicateName} is used twice.");
        // var weirdCurveName = Strips.FirstOrDefault(s => s.Reversed().Curve.Name != s.Name + "'");
        // if (weirdCurveName != null)
        //     HandleInconsistentBehavior($"The inverse curve of {weirdCurveName} has a weird name: {weirdCurveName.Reversed().Curve.Name} instead of {weirdCurveName.Name}'");
        var brokenVertex = graph.Vertices.FirstOrDefault(v => v.fibredSurface != this);
        if (brokenVertex != null)
            HandleInconsistentBehavior($"The vertex {brokenVertex} doesn't refer to this graph.");
        var brokenVertexMapEdge = OrientedEdges.FirstOrDefault(e => e.Dg != null && e.Source.image != e.Dg.Source);
        if (brokenVertexMapEdge != null)
            HandleInconsistentBehavior($"The edge {brokenVertexMapEdge.Name} starts at {brokenVertexMapEdge.Source} with g({brokenVertexMapEdge.Source}) = {brokenVertexMapEdge.Source.image}, but g({brokenVertexMapEdge.Name}) starts at o(Dg({brokenVertexMapEdge.Name})) = o({brokenVertexMapEdge.Dg?.Name}) = {brokenVertexMapEdge.Dg?.Source}");
        var brokenVertexGraphAssociation = graph.Vertices.FirstOrDefault(v => v.fibredSurface != this);
        if (brokenVertexGraphAssociation != null)
            HandleInconsistentBehavior($"The vertex {brokenVertexGraphAssociation} doesn't refer to this graph.");
        var brokenEdgeGraphAssociation = Strips.FirstOrDefault(e => e.graph != graph);
        if (brokenEdgeGraphAssociation != null)
            HandleInconsistentBehavior($"The edge {brokenEdgeGraphAssociation} doesn't refer to this graph.");
        for (int k = 1; k < 2; k++)
        {
            foreach (var vertex in graph.Vertices)
            {
                var star = StarOrdered(vertex).ToList();
                var starts = (from edge in star select edge.EdgePath.Take(k)).ToHashSet();
                foreach (var start in starts)
                {
                    var edges = (from edge in star where edge.EdgePath.Take(k).Equals(start) select edge).ToArray();
                    if (!IsConnectedSet(star, edges))
                        HandleInconsistentBehavior($"The edges {string.Join(", ", edges.Select(e => e.Name))} are not connected in the star of {vertex.Name}, but all start with the same edge path {string.Join(" ", start.Select(e => e.Name))}.");
                }
            }
        }
        var brokenJumpPoints = Strips.FirstOrDefault(e => e.Curve.VisualJumpPoints.Count() != e.Curve.VisualJumpTimes.Count());
        if (brokenJumpPoints != null)
            HandleInconsistentBehavior($"The edge {brokenJumpPoints} has a broken jump point.");
    }


}