using System.Collections.Generic;
using System.Linq;
using Dreamteck.Splines;
using UnityEngine;

public class CurveVisualizer : MonoBehaviour
{
    public GameObject splinePrefab;
    public List<SplineComputer> activeSplines = new(), inactiveSplines = new();
    [SerializeField] private float outwardOffset = 0.05f;

    /// <summary>
    /// We assume this curve to not have any (visual) jump points.
    /// </summary>
    /// <param name="curve"></param>
    /// <param name="resolution"></param>
    /// <param name="camera"></param>
    /// <param name="scale"></param>
    /// <param name="offset"></param>
    public void Initialize(Curve curve, float resolution, Camera camera, float scale = 1,
        Vector3 offset = new Vector3())
    {

        var boundaryTimes = curve.VisualJumpTimes.Prepend(0f).Append(curve.Length).ToArray();
        int activeSplinesCount = activeSplines.Count;
        for (int i = boundaryTimes.Length - 1; i < activeSplinesCount; i++)
        {
            var splineComputerTooMuch = activeSplines.Pop();
            splineComputerTooMuch.gameObject.SetActive(false);
            inactiveSplines.Add(splineComputerTooMuch);
        }
        for (int i = 0; i < boundaryTimes.Length - 1; i++)
        {
            float resolutionLocal = resolution;
            float length = boundaryTimes[i+1] - boundaryTimes[i];
            if (length < resolutionLocal) resolutionLocal = length; // continue;
            float ε = 1e-2f * resolutionLocal;
            length -= 2 * ε;
            if (length <= 0)
                continue;
            
            SplineComputer splineComputer;
            if (activeSplines.Count <= i)
            {
                if (inactiveSplines.Count == 0)
                {
                    var splineGameObject = Instantiate(splinePrefab, transform);
                    splineComputer = splineGameObject.GetComponent<SplineComputer>();
                    splineGameObject.GetComponent<ScaleWithCameraSpline>().camera = camera;
                    activeSplines.Add(splineComputer);
                }
                else
                {
                    splineComputer = inactiveSplines.Pop();
                    activeSplines.Add(splineComputer);
                }

                splineComputer.gameObject.SetActive(true);
            }
            else
                splineComputer = activeSplines[i];


            float pts = length / resolutionLocal;
            int pointsCount = Mathf.RoundToInt(pts);
            float newResolution = length / pointsCount;
            float start = boundaryTimes[i] + ε;
            
            // Plain loop instead of a LINQ query: chained Selects over anonymous types (from the `let` clauses)
            // cause "indirect call signature mismatch" under IL2CPP full generic sharing on WebGL.
            var points = new SplinePoint[pointsCount + 1];
            for (int index = 0; index <= pointsCount; index++)
            {
                float t = start + index * newResolution;
                var tangentSpace = curve.BasisAt(t);
                var basis = tangentSpace.basis;
                var position = tangentSpace.point.Position * scale + offset;
                var tangentVector = basis.a * scale * newResolution / 3; // /2 would be the guess for the position, /
                var normalVector = basis.c.normalized * scale;
                var positionOutside = position + normalVector * outwardOffset;
                points[index] = new SplinePoint(positionOutside,
                    positionOutside - tangentVector,
                    normalVector,
                    1f,
                    curve.Color);
            }
            splineComputer.SetPoints(points, SplineComputer.Space.Local);

            splineComputer.GetComponent<Renderer>().material.color = curve.Color;
            var sizeModifier = splineComputer.GetComponent<MeshGenerator>().sizeModifier;
            foreach (var sizeKey in sizeModifier.keys)
            {
                sizeKey.start = 1d - (1d - sizeKey.start) / curve.Length;
                sizeKey.end = 1d - (1d - sizeKey.end) / curve.Length;
            }
            
            splineComputer.Rebuild();
        }
    }
}
