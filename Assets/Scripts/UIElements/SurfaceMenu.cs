using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Random = UnityEngine.Random;

public enum MenuMode
{
    AddPoint,
    AddCurve,
    PreviewGrid,
    SelectPoint,
    SelectCurve,
}


/// <summary>
/// This is also AbstractSurfaceVisualizer in a sense. This should potentially be split into two classes.
/// </summary>
public class SurfaceMenu: MonoBehaviour 
{
    [SerializeField] private GameObject parametricSurfaceVisualizerUIPrefab;
    [SerializeField] private  GameObject parametricSurfaceVisualizerPrefab;
    [SerializeField] private  GameObject modelSurfaceVisualizerUIPrefab;
    [SerializeField] private GameObject modelSurfaceVisualizerPrefab;
    private RectTransform canvas;

    private readonly Dictionary<string, SurfaceVisualizer> visualizers = new();
    private readonly Dictionary<string, GameObject> visualizerGameObjects = new();

    private SurfaceMenu nextMenu, lastMenu;
    private Dictionary<string, Homeomorphism> forwardHomeos = new();
    private Dictionary<string, Homeomorphism> backwardsHomeos = new();

    public AbstractSurface surface;
    private string currentSurfaceName; 
    public GeodesicSurface geodesicSurface { get; set; }

    private List<Point> currentWaypoints;
    
    public MenuMode Mode
    {
        get => mainMenu.mode;
        set => mainMenu.mode = value;
    }
    
    private CameraManager cameraManager;
    [SerializeField] private MainMenu mainMenu;
    [SerializeField] private string currentCurveName;
    [SerializeField] private Color currentCurveColor;
    private readonly HashSet<(IDrawnsformable, string)> currentStuffShown = new();
    [SerializeField] private Color previewPointColor;
    [SerializeField] private Color previewCurveColor;
    [SerializeField] private float closenessThresholdBaseValue = 0.01f;
    private static int _visualizerPositionID;
    public CurveEditor curveEditor;
    public event Action<IDrawnsformable, string> StuffShown;
    public event Action<IDrawnsformable, string> StuffDeleted;


    public void Start()
    {
        curveEditor.CurveUpdated += UpdateCurve;
    }

    public void Initialize(AbstractSurface surface, RectTransform canvas, CameraManager cameraManager,
        MainMenu mainMenu, bool asynchronous = false)
    {
        var initializeCoroutine = InitializeCoroutine(surface, canvas, cameraManager, mainMenu);
        if (asynchronous)
            StartCoroutine(initializeCoroutine);
        else 
            while (initializeCoroutine.MoveNext())
            {}
    }

    public IEnumerator InitializeCoroutine(AbstractSurface surface, RectTransform canvas, CameraManager cameraManager, MainMenu mainMenu)
    {
        this.canvas = canvas;
        this.surface = surface;
        this.cameraManager = cameraManager;
        this.mainMenu = mainMenu;
        Dictionary<string, Vector3> drawingSurfacePositions = new(); 
        Dictionary<string, UIKamera> windowKameras = new();
        foreach (var (drawingSurfaceName, drawingSurface) in surface.drawingSurfaces)
        {
            string windowName = surface.windowAssignment[drawingSurface.Name];
            bool showOwnWindow = !windowKameras.ContainsKey(windowName);
            
            SurfaceVisualizer surfaceVisualizer;
            GameObject surfaceVisualizerGameObject;
            switch (drawingSurface)
            {
                case ParametricSurface parametricSurface:
                {
                    surfaceVisualizerGameObject = Instantiate(parametricSurfaceVisualizerPrefab);
                    
                    var parametricSurfaceVisualizer = 
                        surfaceVisualizerGameObject.GetComponentInChildren<ParametricSurfaceVisualizer>();
                    
                    var coroutine = parametricSurfaceVisualizer.InitializeCoroutine(parametricSurface);
                    while (coroutine.MoveNext())
                        yield return null;
                    surfaceVisualizer = parametricSurfaceVisualizer;
                    break;
                }
                // case Plane:
                case ModelSurface: // or Plane
                {
                    surfaceVisualizerGameObject = Instantiate(modelSurfaceVisualizerPrefab); // no parent                   

                    var modelSurfaceVisualizer = 
                        surfaceVisualizerGameObject.GetComponentInChildren<ModelSurfaceVisualizer>();
                    
                    modelSurfaceVisualizer.Initialize(drawingSurface); 
                    surfaceVisualizer = modelSurfaceVisualizer;
                    break;
                }
                // case Sphere:
                default:
                    throw new NotImplementedException(); // fine
                
                
            }
            yield return null; 
            if (showOwnWindow && this.geodesicSurface is null && drawingSurface is GeodesicSurface geodesicSurface)
                this.geodesicSurface = geodesicSurface;
            
            
            if (drawingSurfacePositions.ContainsKey(windowName))
                surfaceVisualizerGameObject.transform.position =
                    drawingSurfacePositions[windowName];
            else
                surfaceVisualizerGameObject.transform.position =
                    drawingSurfacePositions[windowName] =
                        new Vector3(100 * _visualizerPositionID++,0, 0);

                    
            var kamera = surfaceVisualizerGameObject.GetComponentInChildren<UIKamera>();
            if (showOwnWindow)
            {
                var surfaceVisualizerUI = Instantiate(parametricSurfaceVisualizerUIPrefab, transform);
                kamera.Initialize(surfaceVisualizerUI.GetComponentInChildren<RawImage>(), canvas, drawingSurface.MinimalPosition, drawingSurface.MaximalPosition);
                windowKameras.Add(windowName, kamera);
                cameraManager.AddKamera(kamera);
                mainMenu.UIMoved += () => kamera.UIMoved();
            }
            else 
            {
                // Don't actually use the camera nor the panel.
                // This will be displayed in the same window (with the same camera) as the surface with name surface.windowAssignment[drawingSurface.Name]
                kamera.gameObject.SetActive(false);
                kamera = windowKameras[windowName];
                kamera.MinimalPosition = VectorHelpers.Min(drawingSurface.MinimalPosition, kamera.MinimalPosition);
                kamera.MaximalPosition = VectorHelpers.Max(drawingSurface.MaximalPosition, kamera.MaximalPosition);
            }
            surfaceVisualizer.camera = kamera.Cam;

            float orthographicSize = kamera.Cam.orthographicSize;
            float orthographicNormalization= 1f / orthographicSize / orthographicSize;
            
            visualizers.Add(drawingSurfaceName, surfaceVisualizer);
            visualizerGameObjects.Add(drawingSurfaceName, surfaceVisualizerGameObject);
            surfaceVisualizer.MouseEvent += 
                (location, button) => {
                    if (location is null)
                    {
                        MouseEvent(null, drawingSurfaceName, button);
                        return;
                    }    

                    float currentOrthographicSize = kamera.Cam.orthographicSize;
                    float closenessThreshold = closenessThresholdBaseValue * orthographicNormalization * currentOrthographicSize * currentOrthographicSize;
                    foreach (var otherDrawingSurface in surface.drawingSurfaces.Values.Where(otherDrawingSurface => 
                                 surface.windowAssignment[otherDrawingSurface.Name] == windowName &&
                                 otherDrawingSurface != drawingSurface &&
                                 location.Value.AtLeast(otherDrawingSurface.MinimalPosition, ignoreZ: true) &&
                                 location.Value.AtMost(otherDrawingSurface.MaximalPosition, ignoreZ: true) 
                            ).Prepend(drawingSurface) // this is the one that the TooltipManager Raycast has hit. The Where enumerable is only traversed when this ClampPoint is null, i.e. we're actually outside the surface
                        )
                    {
                        var point = otherDrawingSurface.ClampPoint(location, closenessThreshold);
                        if (point == null) 
                            continue;
                        MouseEvent(point, otherDrawingSurface.Name, button);
                        return;
                    }
                    MouseEvent(null, drawingSurfaceName, button);
                };
        }
        if (this.geodesicSurface is null)
            throw new Exception("None of the surfaces provides geodesics");
        
    }

    public void Initialize(SurfaceMenu lastMenu, Dictionary<string, Homeomorphism> automorphisms, bool asynchronous = false)
    {
        var initializeCoroutine = InitializeCoroutine(lastMenu, automorphisms);
        if (asynchronous)
            StartCoroutine(initializeCoroutine);
        else 
            while (initializeCoroutine.MoveNext())
            {}
    }

    public IEnumerator InitializeCoroutine(SurfaceMenu lastMenu, Dictionary<string, Homeomorphism> automorphisms)
    {
        var initializeCoroutine = InitializeCoroutine(lastMenu.surface, lastMenu.canvas, lastMenu.cameraManager, lastMenu.mainMenu);
        while (initializeCoroutine.MoveNext())
            yield return null;
        lastMenu.nextMenu = this;
        lastMenu.forwardHomeos = automorphisms;
        this.lastMenu = lastMenu;
        this.backwardsHomeos = automorphisms.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Inverse
        );
        foreach (var (drawnStuff, drawingSurfaceName) in lastMenu.currentStuffShown)
        {
            Display(drawnStuff, drawingSurfaceName, preview: false, propagateBackwards: false, propagateToDrawingSurfaces: false);
            // currentStuffShown should contain drawnStuff once for every DrawingSurface (transformed)! 
            yield return null;
        }
    }

    public void SetMode(int mode) => SetMode((MenuMode) mode);
    public void SetMode(MenuMode mode)
    {
        switch (Mode)
        {
            case MenuMode.AddCurve:
                currentWaypoints = null;
                currentCurveName = null;
                break;
        }
        Mode = mode;
    }
    
    private  (IDrawnsformable, string) PreviewDrawable(Point location, string drawingSurfaceName)
    {
        switch (Mode)
        {
            case MenuMode.AddCurve:
                var lastPoint = currentWaypoints?.LastOrDefault();
                if (lastPoint is null || location is null || location == lastPoint)
                    return (location, drawingSurfaceName);
                var homeo = surface.GetHomeomorphism(drawingSurfaceName, geodesicSurface.Name);
                var previewSegment = geodesicSurface.GetGeodesic(lastPoint, location.ApplyHomeomorphism(homeo), "previewGeodesicSegment");
                previewSegment.Color = previewCurveColor;
                return (previewSegment, geodesicSurface.Name);
                // todo: Feature. Ends for curves, e.g. at points (-> vertices)
            case MenuMode.SelectPoint when location is not null:
            case MenuMode.AddPoint when location is not null:
                location.Color = previewPointColor;
                return (location, drawingSurfaceName);
            case MenuMode.PreviewGrid:
                // todo: Feature. Implement grid
                return (location, drawingSurfaceName); 
            default:
                return (null, drawingSurfaceName);
        }
    }

    private (IDrawnsformable, string) Drawable(Point location, string drawingSurfaceName)
    {
        if (location is null) return (null, drawingSurfaceName);
        switch (Mode) 
        {
            case MenuMode.AddCurve:
            {
                if (string.IsNullOrEmpty(currentCurveName)){
                    // todo: Feature. Ask user
                    currentCurveName = (
                        from i in Enumerable.Range(0, 52)
                        select ((char)('a' + i)).ToString()
                    ).FirstOrDefault(
                        c => !surface.drawingSurfaces.ContainsKey(c)
                    );
                    currentCurveColor = new Color(Random.value, Random.value, Random.value);
                }
                currentWaypoints ??= new();
                var homeo = surface.GetHomeomorphism(drawingSurfaceName, geodesicSurface.Name);
                var pointInGeodesicSurface = location?.ApplyHomeomorphism(homeo);
                currentWaypoints.Add(pointInGeodesicSurface);
                if (currentWaypoints.Count > 1)
                {
                    Curve curve = geodesicSurface.GetPathFromWaypoints(currentWaypoints, currentCurveName);
                    curve.Color = currentCurveColor;
                    return (curve, geodesicSurface.Name);
                }

                goto case MenuMode.AddPoint;
            }
            case MenuMode.AddPoint:
                return (location, drawingSurfaceName);
            default:
                return (null, drawingSurfaceName);
        }
    }

    public void UpdateCurve(Curve curve)
    {
        Display(curve); // in the SurfaceVisualizer, this reuses the same CurveVisualizer (if the curve still has the same name), just reinitinalizes it.
    }

    public void MouseEvent(Point location, string surfaceName, int button)
    {
        if (location is not null)
            Debug.Log($"Hovering over {surfaceName}");
        if (button < 0)
        {
            var (drawable, drawingSurfaceName) = PreviewDrawable(location, surfaceName);
            Display(drawable, drawingSurfaceName, preview: true);
        }

        if (button == 0)
        {
            var (drawable, drawingSurfaceName) = Drawable(location, surfaceName);
            Display(drawable, drawingSurfaceName, preview: false);
        }
        // todo: selection and deletion
    }

    /// <summary>
    /// Display the input on the drawing surface and display the transformed input on the other drawing surfaces.
    /// If not selected otherwise, this is also displayed on all other SurfaceMenus that are connected via homeomorphisms.
    /// </summary>
    /// <param name="input"></param>
    /// <param name="drawingSurfaceName"></param>
    /// <param name="preview">Mark as preview. I.e. it will be overwritten by the next preview of the same type (Point, Curve)</param>
    /// <param name="remove"></param>
    /// <param name="propagateBackwards">View the transformed versions on the previous SurfaceMenus</param>
    /// <param name="propagateForwards">View the transformed versions on the next SurfaceMenus</param>
    /// <param name="propagateToDrawingSurfaces">View the transformed versions on the other drawing surfaces</param>
    /// <param name="skipDrawingSurfaces">Do not propagate to these drawing surfaces</param>
    public void Display([CanBeNull] IDrawnsformable input,
        string drawingSurfaceName = null,
        bool preview = false,
        bool remove = false,
        bool propagateBackwards = true,
        bool propagateForwards = true,
        bool propagateToDrawingSurfaces = true,
        IEnumerable<string> skipDrawingSurfaces = null)
    {
        drawingSurfaceName ??= geodesicSurface.Name;
        propagateForwards = propagateForwards && nextMenu != null;
        string preferredForwardSurfaceName = forwardHomeos.Keys.FirstOrDefault();
        propagateBackwards = propagateBackwards && lastMenu != null;
        string preferredBackwardSurfaceName = backwardsHomeos.Keys.FirstOrDefault();
        var propagateToDrawingSurfacesSet = new HashSet<string>();
        if (propagateToDrawingSurfaces)
            propagateToDrawingSurfacesSet = surface.drawingSurfaces.Keys.ToHashSet();
        skipDrawingSurfaces ??= Enumerable.Empty<string>();
        propagateToDrawingSurfacesSet.ExceptWith(skipDrawingSurfaces);  
        propagateToDrawingSurfacesSet.Add(drawingSurfaceName);
        foreach (string otherDrawingSurfaceName in propagateToDrawingSurfacesSet)
        {
            var homeomorphism = surface.GetHomeomorphism(drawingSurfaceName, otherDrawingSurfaceName);
            var drawable = input?.ApplyHomeomorphism(homeomorphism);
            if (remove)
            {
                visualizers[otherDrawingSurfaceName].Remove(drawable);
                if (!preview && currentStuffShown.Remove((drawable, otherDrawingSurfaceName))) 
                    StuffDeleted?.Invoke(drawable, otherDrawingSurfaceName);
            }
            else
            {
                visualizers[otherDrawingSurfaceName].Display(drawable, preview);
                if (!preview)
                {
                    currentStuffShown.Add((drawable, otherDrawingSurfaceName));
                    StuffShown?.Invoke(drawable, otherDrawingSurfaceName);
                }
            }
                
            if (propagateBackwards && backwardsHomeos.TryGetValue(otherDrawingSurfaceName, out var homeo)) 
                lastMenu.Display(drawable?.ApplyHomeomorphism(homeo),
                    otherDrawingSurfaceName,
                    preview: preview,
                    remove: remove,
                    propagateBackwards: otherDrawingSurfaceName == preferredForwardSurfaceName,
                    propagateForwards: false, 
                    propagateToDrawingSurfaces: otherDrawingSurfaceName == preferredBackwardSurfaceName, 
                    skipDrawingSurfaces: backwardsHomeos.Keys
                );
            if (propagateForwards && forwardHomeos.TryGetValue(otherDrawingSurfaceName, out homeo)) 
                nextMenu.Display(drawable?.ApplyHomeomorphism(homeo),
                    otherDrawingSurfaceName,
                    preview: preview,
                    remove: remove,
                    propagateBackwards: false,
                    propagateForwards: otherDrawingSurfaceName == preferredForwardSurfaceName, 
                    propagateToDrawingSurfaces: otherDrawingSurfaceName == preferredForwardSurfaceName,
                    skipDrawingSurfaces: forwardHomeos.Keys
                );
        }

    }

    public (IDrawnsformable, string) GetCurve(string name) => currentStuffShown.FirstOrDefault(
        c => c.Item1 is Curve
    );

    public IEnumerator DeinitializeCoroutine() // TODO! DOES NOT RESET THE CAMERA MANAGER AND THE MAIN MENU'S UIMOVED EVENT
    {
        foreach (var (stuff, surfaceName) in currentStuffShown.ToArray())
        {
            Display(stuff, surfaceName, remove: true, propagateBackwards: false, propagateForwards: false, propagateToDrawingSurfaces: false);
            yield return null;
        }

        foreach (var visualizerGameObject in visualizerGameObjects.Values.ToArray())
        {
            Destroy(visualizerGameObject);
            yield return null;
        }
    }
}
