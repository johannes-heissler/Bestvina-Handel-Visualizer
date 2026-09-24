
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Serialization;
using Random = System.Random;

public class MainMenu: MonoBehaviour
{
    
    [SerializeField] private GameObject surfaceMenuPrefab;
    public List<SurfaceMenu> surfaceMenus = new();
    [SerializeField] private RectTransform canvas;
    [FormerlySerializedAs("surfaceParameters")] [SerializeField] private string parameters;
    [SerializeField] private CameraManager cameraManager;
    [SerializeField] public MenuMode mode = MenuMode.AddPoint;
    [SerializeField] private TMP_Dropdown curveDropdown;
    private FibredSurface fibredSurface;
    public event Action UIMoved;
    public virtual void OnUIMoved() => UIMoved?.Invoke();
    public string selectedCurve;
    [SerializeField] private FibredSurfaceMenu fibredSurfaceMenu;

    public void InitializeExample(string exampleName, bool showDeckTransformations = false)
    {
        switch (exampleName)
        {
            case "Embedded Torus":
                Initialize("g=1,#", showDeckTransformations, true);
                break;
            case "Half Twist":
                Initialize("g=1,p=2,P=0", showDeckTransformations, true);
                var bbb = fibredSurface.Strips.First(strip => strip.Name == "b");
                bbb.ReplaceWithInverseEdge();
                fibredSurfaceMenu.UpdateGraphMap("a -> c A b c B");
                break;
            case "Reducible Map": // todo: this is not a valid map: the boundary words are not preserved up to isotopy
                Initialize("g=2, p=1", showDeckTransformations, true);
                fibredSurfaceMenu.UpdateGraphMap( "a \u21a6 a b a, b \u21a6 a b a b a, c -> c d c, d -> c d c d c", mode: GraphMapUpdateMode.Replace);
                break;
            case "Bestvina-Handel example 6.1.":
                Initialize("g=2,p=1,P=0", showDeckTransformations, true);
                var c = fibredSurface.Strips.First(strip => strip.Name == "c");
                var d = fibredSurface.Strips.First(strip => strip.Name == "d");
                c.ReplaceWithInverseEdge();
                c.Name = "d";
                d.ReplaceWithInverseEdge();
                d.Name = "c";
                fibredSurfaceMenu.UpdateGraphMap(new Dictionary<string, string>
                {
                    ["a"] = "a B A b D C A",
                    ["b"] = "a c d B a b c d B",
                    ["c"] = "c c d B",
                    ["d"] = "b c d B"
                });
                break;
            case "Bestvina-Handel example 6.2.":
                Initialize("g=0, p=4, P=3", showDeckTransformations, true);
                
                var aa = fibredSurface.Strips.First(strip => strip.Name == "a");
                var bb = fibredSurface.Strips.First(strip => strip.Name == "b");
                var cc = fibredSurface.Strips.First(strip => strip.Name == "c");
                var α = fibredSurface.Strips.First(strip => strip.Name == "α");
                var β = fibredSurface.Strips.First(strip => strip.Name == "β");
                var γ = fibredSurface.Strips.First(strip => strip.Name == "γ");
                α.Name = "γ";
                γ.Name = "α";
                aa.Name = "c";
                cc.Name = "a";
                
                
                // collapse the invariant subforest corresponding to b
                var algorithmSuggestion = fibredSurface.NextSuggestion();
                fibredSurface.ApplySuggestion(new [] {
                      algorithmSuggestion.options.First(option => option.Item2.Contains(">b<")).Item1
                    },
                algorithmSuggestion.buttons.FirstOrDefault()
                );
                fibredSurfaceMenu.UpdateGraphMap(new Dictionary<string, string>
                {
                    ["a"] = "β c γ C a",
                    ["c"] = "β c γ C a α A c Γ C β c γ C a α A c Γ",
                });
                break;
            case "Bestvina-Handel example 6.3.":
                Initialize("g=0, p=5, P=0", showDeckTransformations, true);
                
                // var aa = fibredSurface.Strips.First(strip => strip.Name == "a");
                // var bb = fibredSurface.Strips.First(strip => strip.Name == "b");
                // var cc = fibredSurface.Strips.First(strip => strip.Name == "c");
                // var α = fibredSurface.Strips.First(strip => strip.Name == "α");
                // var β = fibredSurface.Strips.First(strip => strip.Name == "β");
                // var γ = fibredSurface.Strips.First(strip => strip.Name == "γ");
                // α.Name = "γ";
                // γ.Name = "α";
                // aa.Name = "c";
                // cc.Name = "a";
                //
                //
                // // collapse the invariant subforest corresponding to b
                // var algorithmSuggestion = fibredSurface.NextSuggestion();
                // fibredSurface.ApplySuggestion(new [] {
                //       algorithmSuggestion.options.First(option => option.Item2.Contains(">b<")).Item1
                //     },
                // algorithmSuggestion.buttons.FirstOrDefault()
                // );
                // fibredSurfaceMenu.UpdateGraphMap(new Dictionary<string, string>
                // {
                //     ["a"] = "β c γ C a",
                //     ["c"] = "β c γ C a α A c Γ C β c γ C a α A c Γ",
                // });
                break;
            case "Point Push example":
                Initialize("g=2,p=1,P=0", showDeckTransformations, true);
                
                var a = fibredSurface.Strips.First(strip => strip.Name == "a");
                var b = fibredSurface.Strips.First(strip => strip.Name == "b");
                a.ReplaceWithInverseEdge();
                b.ReplaceWithInverseEdge();
        
                // Opposite orientation of the side curves (following along the edge e, the side ε crosses from the left)
                // fibredSurfaceMenu.UpdateGraphMap("a \u21a6 B a D c d C b", mode: GraphMapUpdateMode.Postcompose); // Push(α)
                // fibredSurfaceMenu.UpdateGraphMap("c \u21a6 b A B a D c d", mode: GraphMapUpdateMode.Postcompose); // Push(γ)
                // fibredSurfaceMenu.UpdateGraphMap("b \u21a6 c D C d A b a", mode: GraphMapUpdateMode.Postcompose); // Push(β rev)
                // fibredSurfaceMenu.UpdateGraphMap("d \u21a6 c d C b A B a", mode: GraphMapUpdateMode.Postcompose); // Push(δ)
                fibredSurfaceMenu.UpdateGraphMap("a \u21a6 b a B c D C d", mode: GraphMapUpdateMode.Postcompose); // Push(α)
                fibredSurfaceMenu.UpdateGraphMap("c \u21a6 d A b a B c D", mode: GraphMapUpdateMode.Postcompose); // Push(γ)
                fibredSurfaceMenu.UpdateGraphMap("b \u21a6 a D c d C b A", mode: GraphMapUpdateMode.Postcompose); // Push(β rev)
                fibredSurfaceMenu.UpdateGraphMap("d \u21a6 C d A b a B c", mode: GraphMapUpdateMode.Postcompose); // Push(δ)

                fibredSurfaceMenu.SelectFibredSurface(fibredSurface);
                fibredSurfaceMenu.UpdateGraphMap($"ρ := D C d A b a B c\n" +
                                                 $"d \u21a6 d ρ\n" +
                                                 $"b \u21a6 a°Ρ b\n" +
                                                 $"c \u21a6 c (d ρ C a)°ρ\n" +
                                                 $"a \u21a6 a (Ρ D c d ρ (C a )°ρ d ρ C a)°ρ",
                    mode: GraphMapUpdateMode.Replace);
                // original (moving start point along d, thus shifting some d's)
                // fibredSurfaceMenu.UpdateGraphMap($"ρ := C d A b a B c D\n" +
                //                              $"d \u21a6 ρ d\n" +
                //                              $"b \u21a6 (a D)°(Ρ) b\n" +
                //                              $"c \u21a6 c (ρ d C a D)°(ρ)\n" +
                //                              $"a \u21a6 a (D Ρ c ρ d (C a D)°(ρ) ρ d C a D)°(ρ)",
                //     mode: GraphMapUpdateMode.Replace);
                break;
            case "General Point Push example (one way)":
                Initialize("g=2,p=1,P=0", showDeckTransformations, true);
                
                var a2 = fibredSurface.Strips.First(strip => strip.Name == "a");
                var b2 = fibredSurface.Strips.First(strip => strip.Name == "b");
                a2.ReplaceWithInverseEdge();
                b2.ReplaceWithInverseEdge();

                var pointPush = new PushingPath(EdgePath.FromString("C b d' c d d c' a", fibredSurface.Strips), startLeft: true);
                var r = new Random();
                pointPush.variables[0].SetValue(5);
                pointPush.variables[1].SetValue(4);
                pointPush.variables[2].SetValue(3);
                pointPush.variables[3].SetValue(2);
                
                pointPush.CalculateSelfIntersections(); // todo
                fibredSurfaceMenu.surfaceMenu.Display(pointPush, fibredSurface.surface.Name);
                
                Debug.Log(pointPush.ToString());
                
                var pushingMapString = fibredSurface.Strips.ToLineSeparatedString(strip => $"g({strip.Name}) = { pointPush.Image(strip) }");
                Debug.Log(pushingMapString);
                
                
                fibredSurfaceMenu.UpdateGraphMap(fibredSurface.Strips.ToDictionary(e => (Strip) e, e => pointPush.Image(e)),
                    mode: GraphMapUpdateMode.Replace, selectFibredSurface: true);
                
                StartCoroutine(UpdatePointPushCoroutine());

                break;

                IEnumerator UpdatePointPushCoroutine()
                {
                    while (!Input.GetKey(KeyCode.Escape))
                    {
                        bool changed = Input.GetKeyUp(KeyCode.E);

                        if (Input.GetKeyUp(KeyCode.M))
                        {
                            foreach (var variable in pointPush.variables)
                                variable.SetValue((float) r.NextDouble()/3);
                            changed = true;
                        }

                        if (changed)
                        {
                            pointPush.CalculateSelfIntersections();
                            fibredSurfaceMenu.UpdateGraphMap(fibredSurface.Strips.ToDictionary(e => (Strip) e, e => pointPush.Image(e)),
                                mode: GraphMapUpdateMode.Replace, selectFibredSurface: true);
                            fibredSurfaceMenu.surfaceMenu.Display(pointPush, fibredSurface.surface.Name);
                            
                        } 
                        yield return null; // waits for next frame
                    }
                }
            break;
            case "Random mapping class in genus 2":
                Initialize("g=2,p=1,P=0", showDeckTransformations, true);
                // Bestvina-Handel ex. 6.1. setup
                var c2 = fibredSurface.Strips.First(strip => strip.Name == "c");
                var d2 = fibredSurface.Strips.First(strip => strip.Name == "d");
                c2.ReplaceWithInverseEdge();
                c2.Name = "d";
                d2.ReplaceWithInverseEdge();
                d2.Name = "c";
                var random = new Random();
                var maps = new List<string>()
                {
                    "c -> c d", "c -> c D",
                    "b -> a b", "b -> A b",
                    "d -> C d", "d -> c d",
                    "a -> a B", "a -> a b",
                    "a -> c B a, d -> d C b", "a -> b C a, d -> d B c",
                };
                for (int i = 0; i < 10; i++)
                {
                    fibredSurfaceMenu.UpdateGraphMap(maps[random.Next(maps.Count)], mode: GraphMapUpdateMode.Postcompose);
                }
                break;
        }
        
    }
    
    public void Initialize(string parameters = null, bool showDeckTransformations = false, bool initializeFibredSurfaceMenu = false)
    {
        if (parameters != null)
            this.parameters = parameters;

        AbstractSurface abstractSurface;
        try
        {
            (abstractSurface, fibredSurface) = SurfaceGenerator.CreateSurface(Parameter.FromString(this.parameters));
        }
        catch (Exception e)
        {
            if (fibredSurfaceMenu != null)
                fibredSurfaceMenu.HandleError(e.Message);
            return;
        }
        
        Initialize(abstractSurface, showDeckTransformations, asynchronous: false, OnDone: initializeFibredSurfaceMenu ? () => InitializeFibredSurfaceMenu(fibredSurface) : (Action) null);

    }
    
    private void InitializeFibredSurfaceMenu(FibredSurface fibredSurface)
    {
        if (fibredSurfaceMenu.FibredSurface is not null || fibredSurface is null) return;
        
        this.fibredSurface = fibredSurface;
        fibredSurfaceMenu.gameObject.SetActive(true);
        fibredSurfaceMenu.Initialize(fibredSurface, surfaceMenus[0]);
    }


    private void Initialize(AbstractSurface surface, bool addDeckTransformations = true, bool asynchronous = false,
        Action OnDone = null)
    {
        var initializeCoroutine = InitializeCoroutine(surface, addDeckTransformations, OnDone);
        if (asynchronous)
            StartCoroutine(initializeCoroutine);
        else
            while (initializeCoroutine.MoveNext()) {}
    }
    
    private IEnumerator InitializeCoroutine(AbstractSurface surface, bool addDeckTransformations, Action onDone)
    {
        if (addDeckTransformations && surface.drawingSurfaces.Values.FirstOrDefault(
                surf => surf is ModelSurface 
            ) is ModelSurface primaryModelSurface)
        {
            foreach (var side in primaryModelSurface.AllSideCurves)
            {
                surface.AddHomeomorphism(
                    side.DeckTransformation(),
                    drawTargetInSameWindowAsSource: true
                );
            }
            
            // var modelChange = modelSurface.SwitchHyperbolicModel();
            // if (modelChange != null)
            // {
            //     surface.AddHomeomorphism(modelChange);
            //     if (modelChange.target is ModelSurface modelChangeTarget)
            //         foreach (var side in modelChangeTarget.AllSideCurves) 
            //             surface.AddHomeomorphism(
            //                 side.DeckTransformation(),
            //                 drawTargetInSameWindowAsSource: true
            //             );
            // }
            // var toKleinModel = modelSurface.ToKleinModel();
            // if (toKleinModel != null)
            // {
            //     surface.AddHomeomorphism(toKleinModel);
            //     // if (ToKleinModel.target is ModelSurface modelChangeTarget)
            //     //     foreach (var side in modelChangeTarget.AllSideCurves) 
            //     //         surface.AddHomeomorphism(
            //     //             side.DeckTransformation(), // not implemented for Klein model. It would act as if it is a euclidean plane
            //     //             drawTargetInSameWindowAsSource: true
            //     //         );
            // }
        }

        foreach (var menu in surfaceMenus.ToArray())
        {
            var deinitializeCoroutine = menu.DeinitializeCoroutine();
            while (deinitializeCoroutine.MoveNext()) 
                yield return null;
            Destroy(menu.gameObject);
            surfaceMenus.Remove(menu);
            yield return null;
        }
           
        var gameObject = Instantiate(surfaceMenuPrefab, transform);
        var surfaceMenu = gameObject.GetComponentInChildren<SurfaceMenu>();
        
        var coroutine = surfaceMenu.InitializeCoroutine(surface, canvas, cameraManager, this);
        while (coroutine.MoveNext()) 
            yield return null;
        
        surfaceMenu.StuffShown += OnStuffShown;
        surfaceMenu.StuffDeleted += OnStuffDeleted;
        surfaceMenus.Add(surfaceMenu);
        if (surface.drawingSurfaces.Values.FirstOrDefault(
                surf => surf is ModelSurface
            ) is ModelSurface primaryModelSurface1)
        {
            foreach (ModelSurfaceSide side in primaryModelSurface1.sides)
            {
                surfaceMenu.Display(side, primaryModelSurface1.Name, preview: false, propagateToDrawingSurfaces: true);
                yield return null; 
                // todo
            }

            foreach (var puncture in primaryModelSurface1.punctures)
            {
                // todo: Feature: Add styles for displayed points. Here: style: cross, colors all different or so? 
                // Only for punctures that are not vertices?
            }
        }
        // foreach (var (surfaceName, drawingSurface) in surface.drawingSurfaces)
        // {
        //     foreach (var puncture in drawingSurface.punctures)
        //     {
        //         surfaceMenu.Display(puncture, surfaceName, preview: false, propagateToDrawingSurfaces: false); 
        //         // Todo: feature: Add styles for displayed points. Here: style: cross
        //     }
        //     
        //     if (drawingSurface is not ModelSurface modelSurface) continue;
        //     foreach (ModelSurfaceSide side in modelSurface.sides)
        //     {
        //         surfaceMenu.Display(side, surfaceName, preview: false, propagateToDrawingSurfaces: false);
        //         // Todo: feature: Add styles for displayed curves. Here: style: dotted
        //         // surfaceMenu.Display(side.other, surfaceName, preview: false);
        //     }
        //
        // }

        onDone?.Invoke();
    }

    private void OnStuffShown(IDrawnsformable stuff, string surface)
    {
        if (stuff is Curve curve && curveDropdown.options.All(option => option.text != curve.Name))
        {
            curveDropdown.options.Add(new TMP_Dropdown.OptionData(curve.Name, null, curve.Color));
        }
    }
    
    private void OnStuffDeleted(IDrawnsformable stuff, string surface)
    {
        if (stuff is not Curve curve) return;
        var index = curveDropdown.options.FindIndex(option => option.text == curve.Name);
        if (index == -1) return;
        curveDropdown.options.RemoveAt(index);
        if (selectedCurve != curve.Name) return;
        selectedCurve = curveDropdown.options[0].text;
        curveDropdown.value = 0;
    }

    public void DropdownValueChanged()
    {
        selectedCurve = curveDropdown.options[curveDropdown.value].text;
    }

    public void DehnTwistButtonClicked()
    {
        var surfaceMenu = surfaceMenus[^1];
        var (res, surfaceName) = surfaceMenu.GetCurve(selectedCurve);
        if (res is not Curve curve) return;
        AddMenuFromAutomorphism(AutomorphismType.DehnTwist, surfaceName, curve);
    }

    private void AddMenuFromAutomorphism(AutomorphismType type, string surfaceName, params IDrawnsformable[] parameters)
    {
        var surfaceMenu = surfaceMenus[^1];
        Dictionary<string, Homeomorphism> automorphisms = new();
        foreach (var (drawingSurfaceName, drawingSurface) in surfaceMenu.surface.drawingSurfaces)
        {
            var homeomorphism = surfaceMenu.surface.GetHomeomorphism(surfaceName, drawingSurfaceName);
            var transformedParams = from s in parameters select s.ApplyHomeomorphism(homeomorphism);
            var automorphism = drawingSurface.GetAutomorphism(type, transformedParams.ToArray());
            if (automorphism == null) continue;
            automorphisms[drawingSurfaceName] = automorphism;
        }

        if (automorphisms.Count == 0)
        {
            Debug.Log($"No automorphisms found in the surfaces for the type {type} and parameters {parameters}");
            return;
        }
        var gameObject = Instantiate(surfaceMenuPrefab, transform);
        var menu = gameObject.GetComponent<SurfaceMenu>();
        menu.Initialize(surfaceMenu, automorphisms);
        surfaceMenus.Add(menu);
    }
}