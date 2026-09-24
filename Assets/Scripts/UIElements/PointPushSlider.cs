using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PointPushSlider : MonoBehaviour
{
    private PushingPath pushingPath;
    [SerializeField] private FibredSurfaceMenu fibredSurfaceMenu;
    [SerializeField] public GraphMapUpdateMode graphMapUpdateMode;
    [SerializeField] private TMP_Text previewText;
    private Dictionary<Strip, EdgePath> newGraphMap = new();
    [SerializeField] private GameObject variableSliderPrefab;
    private Slider[] variableSliders = {};
    private TMP_Text[] variableTexts = {};
    
    
    #region for unity editor
    [SerializeField] private float[] variables = {};
    [SerializeField] private bool update;
    private GameObject[] sliderGameObjects = {};

    private void Update()
    {
        if (!update) return;
        update = false;
        for (int i = 0; i < variables.Length; i++) 
            SetVariable(i, variables[i]);
        PreviewGraphMap();
    }
    
    #endregion

    // Called from UI
    public void PreviewGraphMap()
    {
        previewText.text = "Pushing path: " + pushingPath;
        if (pushingPath.Concrete) pushingPath.CalculateSelfIntersections();
        else previewText.text += "\nThe variables are not set, so self-intersections are not calculated yet.";
        newGraphMap = fibredSurfaceMenu.FibredSurface.Strips.ToDictionary(s => (Strip) s, s => pushingPath.Image(s));
        fibredSurfaceMenu.SetGraphMap(newGraphMap);
        // previewText.text += "\nVariables: " + pushingPath.variables.ToCommaSeparatedString();
        previewText.text += "\nMap: " + newGraphMap.ToCommaSeparatedString(kv => $"{kv.Key.ColorfulName} -> {kv.Value.ToColorfulString(150, 10)}");
    }

    public void SetVariable(int i, float value, bool update = false)
    {
        if (value > 0) pushingPath.variables.ElementAtOrDefault(i)?.SetValue(value); 
        else pushingPath.variables.ElementAtOrDefault(i)?.FreeVariable();
        variableTexts[i].text = pushingPath.variables.ElementAtOrDefault(i)?.ToString() ?? "Free";
        if (update) 
            PreviewGraphMap();
    }

    // Called from UI
    public void UpdateGraphMap(int mode = -1) =>
        fibredSurfaceMenu.UpdateGraphMap(newGraphMap, mode: Enum.IsDefined(typeof(GraphMapUpdateMode), mode) ? (GraphMapUpdateMode) mode : graphMapUpdateMode);
    

    public void Initialize(string pushingPathString)
    {
        try
        {
            Initialize(fibredSurfaceMenu.FibredSurface.ParsePointPush(pushingPathString));
            PreviewGraphMap();
        }
        catch (Exception e)
        {
            fibredSurfaceMenu.HandleError(e.Message);
        }
    }

    public void Initialize(PushingPath pushingPath)
    {
        variables = pushingPath.variables.Select(v => v.Value).ToArray();
        
        this.pushingPath = pushingPath;
        foreach (var sliderGameObject in sliderGameObjects) 
            Destroy(sliderGameObject);
        
        sliderGameObjects = new GameObject[variables.Length];
        variableSliders = new Slider[variables.Length];
        variableTexts = new TMP_Text[variables.Length];
        for (var i = 0; i < variables.Length; i++)
        {
            var variable = pushingPath.variables[i];
            var sliderGameObject = Instantiate(variableSliderPrefab, transform);
            sliderGameObjects[i] = sliderGameObject;
            
            var sliderComponent = sliderGameObject.GetComponentInChildren<Slider>();
            sliderComponent.value = variable.Value;
            variableSliders[i] = sliderComponent;
            
            var textComponent = sliderGameObject.GetComponentInChildren<TMP_Text>();
            textComponent.text = variable.ToString();
            variableTexts[i] = textComponent;
            
            int index = i; // capture the current value of i
            sliderComponent.onValueChanged.AddListener(value =>
            {
                SetVariable(index, value, update: true);
            });
        }
    }
}
