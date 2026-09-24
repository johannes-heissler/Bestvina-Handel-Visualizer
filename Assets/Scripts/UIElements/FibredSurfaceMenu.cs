using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using QuikGraph;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using MenuEdge = QuikGraph.TaggedEdge<FibredSurfaceMenu.MenuVertex, string>;



public class FibredSurfaceMenu : MonoBehaviour
{
    public class MenuVertex
    {
        public FibredSurface fibredSurface;
        public FibredSurface.AlgorithmSuggestion suggestion;

        public MenuVertex(FibredSurface fibredSurface, FibredSurface.AlgorithmSuggestion suggestion)
        {
            this.fibredSurface = fibredSurface;
            this.suggestion = suggestion;
        }
    }
    
    [SerializeField] internal SurfaceMenu surfaceMenu;
    private FibredSurface currentlyShownFibredSurface;

    
    [SerializeField] private Transform forwardButtonList;
    [SerializeField] private GameObject forwardButtonPrefab;
    [SerializeField] private GameObject backButton;
    [SerializeField] private GameObject suggestionButtonList;
    [SerializeField] private GameObject suggestionButtonPrefab;
    [SerializeField] private GameObject optionList;
    [SerializeField] private GameObject optionTogglePrefab;
    [SerializeField] private TMP_Text descriptionText;
    [SerializeField] private TMP_Text errorText;
    [SerializeField] private TMP_Text graphStatusText;
    [SerializeField] private ToggleGroup toggleGroup;
    [SerializeField] private UnityEvent<FibredSurface> OnFibredSurfaceChanged;
    private readonly AdjacencyGraph<MenuVertex, MenuEdge> fibredSurfaces = new();
    private MenuVertex rootVertex;
    public FibredSurface FibredSurface => currentVertex?.fibredSurface;
    
    private MenuVertex currentVertex;
    private MenuEdge ParentEdge() => 
        fibredSurfaces.Edges.FirstOrDefault(e => e.Target == currentVertex);

    public void Initialize(FibredSurface fibredSurface, SurfaceMenu surfaceMenu)
    {
        this.surfaceMenu = surfaceMenu;
        OnFibredSurfaceChanged.AddListener(surfaceMenu.curveEditor.UpdateDropdown);
        surfaceMenu.curveEditor.FibredSurfaceUpdated += () => UpdateUI(true);
        rootVertex = new MenuVertex(fibredSurface, null);
        fibredSurface.OnError += HandleError; // handle errors in the fibred surface
        fibredSurfaces.AddVertex(rootVertex);
        
        currentVertex = rootVertex;
        OnFibredSurfaceChanged.Invoke(fibredSurface);
        CreateUI();
    }
    
    public void Deinitialize()
    {
        if (FibredSurface != null)
            FibredSurface.OnError -= HandleError;
        OnFibredSurfaceChanged.RemoveAllListeners();
        fibredSurfaces.Clear();
        currentVertex = null;
        ClearUI();
    }

    // Called from UI
    public void SelectRoot()
    {
        if (rootVertex != null)
            UpdateSelectedSurface(rootVertex);
    }


    private void UpdateSelectedSurface(MenuVertex newVertex)
    {        
        this.currentVertex = newVertex;
        OnFibredSurfaceChanged.Invoke(newVertex.fibredSurface);
        UpdateUI(false);
    }

    private void ClearUI()
    {
        foreach (var toggle in toggleGroup.ActiveToggles().ToList())
            toggleGroup.UnregisterToggle(toggle);
        foreach (Transform child in forwardButtonList.transform.Cast<Transform>().ToList()) 
            Destroy(child.gameObject);
        foreach (Transform child in suggestionButtonList.transform.Cast<Transform>().ToList())
            Destroy(child.gameObject);
        foreach (Transform child in optionList.transform.Cast<Transform>().ToList())
            Destroy(child.gameObject);
        if (currentlyShownFibredSurface != null)
            surfaceMenu.Display(currentlyShownFibredSurface, currentlyShownFibredSurface.surface.Name, remove: true);
        currentlyShownFibredSurface = null;
    }

    private IEnumerator suggestionCoroutine;

    private void UpdateUI(bool reloadSuggestion = false)
    {
        ClearUI();
        CreateUI(reloadSuggestion);
    }
    
    private void CreateUI(bool reloadSuggestion = false)
    {
        StopCoroutine(nameof(LoadSuggestionLate));
        backButton.SetActive(ParentEdge() != null);
        foreach (var edge in fibredSurfaces.OutEdges(currentVertex))
        {
            var button = Instantiate(forwardButtonPrefab, forwardButtonList.transform);
            button.GetComponentInChildren<TextMeshProUGUI>().text = edge.Tag;
            var edgeTarget = edge.Target; 
            // avoid closure problem (https://stackoverflow.com/a/271440/2687128) // this was entirely suggested by Copilot (with the link!)
            button.GetComponent<Button>().onClick.AddListener(() => UpdateSelectedSurface(edgeTarget));
        }
        
        graphStatusText.text = FibredSurface.GraphString();
        if (suggestionCoroutine != null) 
            StopCoroutine(suggestionCoroutine);
        suggestionCoroutine = LoadSuggestionLate(reloadSuggestion);
        StartCoroutine(suggestionCoroutine);

        
        LayoutRebuilder.ForceRebuildLayoutImmediate(GetComponent<RectTransform>());
    }
    
    public void HandleError(string message)
    {
        Debug.LogError(message);    
        if (algorithmCoroutine != null)
            StopCoroutine(algorithmCoroutine);
        if (suggestionCoroutine != null)
            StopCoroutine(suggestionCoroutine);
        errorText.text += message + "\n";
        StartCoroutine(ClearErrorText());
        return;

        IEnumerator ClearErrorText()
        {
            yield return new WaitForSeconds(20f);
            errorText.text = errorText.text.Substring(message.Length + 1); // clear the error text after 10 seconds
        }
    }

    /// <summary>
    /// This is part of UpdateUI
    /// </summary>
    IEnumerator LoadSuggestionLate(bool reload = false)
    {
        descriptionText.text = "Displaying new fibred surface...";
        yield return new WaitForEndOfFrame();
        surfaceMenu.Display(FibredSurface, FibredSurface.surface.Name);
        currentlyShownFibredSurface = FibredSurface;
        yield return new WaitForEndOfFrame();
        yield return new WaitForEndOfFrame();
        
        var suggestion = currentVertex.suggestion;
        if (suggestion == null || reload)
        {
            descriptionText.text = "Loading next steps...";
            yield return new WaitForEndOfFrame();
            try
            {
                suggestion = currentVertex.suggestion = FibredSurface.NextSuggestion();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                HandleError(e.Message);
                yield break; // stop the coroutine if an error occurs
            }
        }
        descriptionText.text = "Displaying next steps...";
        yield return new WaitForEndOfFrame();
        
        descriptionText.text = suggestion.description ?? "";
            
        var optionToggles = new Dictionary<Toggle, (object, string)>();
        bool first = true;
        foreach (var option in suggestion.options ?? Array.Empty<(object, string)>())
        {
            var toggleGameObject = Instantiate(optionTogglePrefab, optionList.transform);
            toggleGameObject.GetComponentInChildren<TextMeshProUGUI>().text = option.Item2;
            var toggle = toggleGameObject.GetComponent<Toggle>();
            optionToggles[toggle] = option;
            if (!suggestion.allowMultipleSelection)
                // toggleGroup.RegisterToggle(toggle);
                toggle.group = toggleGroup; 
            if (first)
                toggle.isOn = true; // select the first option by default
            first = false;
        }
        // if (first) // suggestion.IsFinished, but without multiple enumeration
        //     yield break;

        foreach (var buttonText in suggestion.buttons ?? Array.Empty<string>())
        {
            var button = Instantiate(suggestionButtonPrefab, suggestionButtonList.transform);
            button.GetComponentInChildren<TextMeshProUGUI>().text = buttonText.AddDotsMiddle(250, 30);
            button.GetComponent<Button>().onClick.AddListener(() =>
            {
                var selection = (from toggleOptionPair in optionToggles
                    where toggleOptionPair.Key.isOn
                    select toggleOptionPair.Value).ToList();
                if (selection.Count == 0)
                    selection = new (optionToggles.Values.Take(1));
                
                DoSuggestion(buttonText, selection);
            });
        }
        LayoutRebuilder.ForceRebuildLayoutImmediate(GetComponent<RectTransform>());
    }

    // called from UI 
    private void DoSuggestion(string buttonText, IReadOnlyCollection<(object, string)> selection)
    {
        if (FibredSurface.Copyable)
        {
            var newFibredSurface = FibredSurface.Copy();
            newFibredSurface.OnError += HandleError; // handle errors in the fibred surface

            try
            {
                // often takes several seconds. Blocks the UI.
                newFibredSurface.ApplySuggestion(from tuple in selection select tuple.Item1, buttonText); 
            }
            catch (Exception e)
            {
                HandleError(e.Message);
                return;
            }

            var newVertex = new MenuVertex(newFibredSurface, null);
            fibredSurfaces.AddVerticesAndEdge(new MenuEdge(
                source: currentVertex, 
                target: newVertex, 
                tag: buttonText + "\n" + selection.Select(
                         e => e.Item2.AddDots(50)).Take(4).ToCommaSeparatedString() +
                     (selection.Count > 4 ? "..." : "")
                )
            );
            UpdateSelectedSurface(newVertex);
        }
        else
        {
            try
            {
                // often takes several seconds. Blocks the UI.
                FibredSurface.ApplySuggestion(from tuple in selection select tuple.Item1, buttonText); 
            }
            catch (Exception e)
            {
                HandleError(e.Message);
                return;
            }
            UpdateUI(reloadSuggestion: true); 
        }

    }

    public bool DoNextSuggestion()
    {
        var suggestion = currentVertex.suggestion ?? (currentVertex.suggestion = FibredSurface.NextSuggestion());
        var selection = suggestion.options.FirstOrDefault();
        if (selection == default) // suggestion.IsFinished, but without multiple enumeration
            return false;
        
        var firstButton = suggestion.buttons.First();
        if (firstButton == FibredSurface.AlgorithmSuggestion.ignoreReducibleButton)
            return false; // stop when reducibility is detected!
        
        DoSuggestion(firstButton, new []{ suggestion.options.First() });
        return true;
    }
    private Coroutine algorithmCoroutine;

    // called from UI
    public void StartAlgorithm()
    {
        algorithmCoroutine ??= StartCoroutine(Run());
        return;

        IEnumerator Run()
        {
            var limit = 20 * FibredSurface.graph.EdgeCount;
            for (int i = 0; i < limit; i++)
            {
                if(!DoNextSuggestion()) 
                    break;
                yield return null; // Wait for the next frame
            }
        }
    }

    // called from UI
    public void StopAlgorithm()
    {
        if (algorithmCoroutine == null) return;
        StopCoroutine(algorithmCoroutine);
        algorithmCoroutine = null;
    }
    
    public void ToggleAlgorithm()
    {
        if (algorithmCoroutine == null) StartAlgorithm();
        else StopAlgorithm();
    }
    public void SelectFibredSurface(FibredSurface fibredSurface, bool addIfNotPresent = false)
    {
        var menuVertex = fibredSurfaces.Vertices.FirstOrDefault(v => v.fibredSurface == fibredSurface);
        if (menuVertex == null)
        {
            if (!addIfNotPresent)
                throw new ArgumentException("The fibred surface is not present in the menu.");
            menuVertex = new MenuVertex(fibredSurface, null);
            fibredSurfaces.AddVerticesAndEdge(new MenuEdge(currentVertex, menuVertex, "Update fibred surface"));
        }
        UpdateSelectedSurface(menuVertex);
    }

    // called from UI
    public void BackButtonPressed()
    {
        var parent = ParentEdge()?.Source;
        if (parent != null)
            UpdateSelectedSurface(parent);
    }

    // UI called; should be differentiated more than being an overload
    public void UpdateGraphMap(string text, bool reset = false, GraphMapUpdateMode mode = GraphMapUpdateMode.Replace)
    {
        try
        {
            UpdateGraphMap(FibredSurface.ParseMap(text), reset, mode);
        }
        catch (Exception e)
        {
            HandleError(e.Message);
        }
    }

    public void UpdateGraphMap(IReadOnlyDictionary<string, string> map, bool reset = false,
        GraphMapUpdateMode mode = GraphMapUpdateMode.Replace)
    {
        try
        {
            UpdateGraphMap(FibredSurface.ParseMap(map), reset, mode);
        }
        catch (Exception e)
        {
            HandleError(e.Message);
        }
    }

    public void UpdateGraphMap(IReadOnlyDictionary<Strip, EdgePath> map, bool reset = false,
        GraphMapUpdateMode mode = GraphMapUpdateMode.Replace, bool selectFibredSurface = false)
    {
        if (FibredSurface == null)
            throw new InvalidOperationException("Cannot change the original fibred surface if no fibred surface is selected. Did you initialize the menu with any fibred surface?");
        if (map.Count > 0)
        {
            var fibredGraphUsedInMap = map.Keys.First().graph;
            if (FibredSurface.graph != fibredGraphUsedInMap)
            {
                if (!selectFibredSurface)
                    throw new ArgumentException("The map keys must be strips of the current fibred surface.");
                currentVertex = fibredSurfaces.Vertices.First(v => v.fibredSurface.graph == fibredGraphUsedInMap);
            }
        }
        var edges = FibredSurface.Strips.ToHashSet();
        edges.ExceptWith(from k in map.Keys select k.UnderlyingEdge);
        if (edges.Count != 0)
        {
            var enteredMapNew = new Dictionary<Strip, EdgePath>(map);
            foreach (var missingEdge in edges)
                enteredMapNew[missingEdge] = new NormalEdgePath(missingEdge); // identity on the other edges
            map = enteredMapNew;
        }

        var fibredSurfaceCopy = FibredSurface.Copy();
        fibredSurfaceCopy.OnError += HandleError; // handle errors in the fibred surface
        FibredSurface.SetMap(map, mode);

        MenuVertex newVertex = new MenuVertex(FibredSurface, null);
        if (true)
        {
            currentVertex.fibredSurface = fibredSurfaceCopy;
            currentVertex.suggestion = null; // reset the suggestion, so that it is recomputed
        }
        
        if (reset)
        {
            fibredSurfaces.Clear();
            fibredSurfaces.AddVertex(newVertex);
        }
        else
        {
            var text = (
                from edge in map.Keys
                where map[edge].Count != 1 || !Equals(map[edge].First(), edge.UnderlyingEdge)  
                select ((IDrawable)edge).ColorfulName + " -> " + map[edge].ToColorfulString(50, 10)
            ).ToCommaSeparatedString();
            fibredSurfaces.AddVerticesAndEdge(new MenuEdge(
                currentVertex,
                newVertex,
                $"{mode} map with {text}"
            ));
        }
        UpdateSelectedSurface(newVertex);
    }


    #region Referenced from UI
    private Dictionary<Strip, EdgePath> graphMap = null;
    private string graphMapString = null;
    public void SetGraphMap(string text)
    {
        graphMap = null;
        graphMapString = text;
    }

    public void SetGraphMap(Dictionary<Strip, EdgePath> map)
    {
        graphMap = map;
        graphMapString = null;
    }

    private void UpdateGraphMapFromVariables(GraphMapUpdateMode mode)
    {
        if (graphMap != null)
            UpdateGraphMap(graphMap, reset: false, mode: mode);
        else if (graphMapString != null)
            UpdateGraphMap(graphMapString, reset: false, mode: mode);
        else
            throw new InvalidOperationException("No graph map is set.");
    }
    
    public void ReplaceWithGraphMap() => UpdateGraphMapFromVariables(GraphMapUpdateMode.Replace);
    
    public void PrecomposeWithGraphMap() => UpdateGraphMapFromVariables(GraphMapUpdateMode.Precompose);
    
    public void PostcomposeWithGraphMap() => UpdateGraphMapFromVariables(GraphMapUpdateMode.Postcompose);
    #endregion

}

public enum GraphMapUpdateMode
{
    Precompose,
    Replace,
    Postcompose
}
