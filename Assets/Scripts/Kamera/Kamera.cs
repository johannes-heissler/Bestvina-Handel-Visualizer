using UnityEngine;
using System;
using System.Linq;
using UnityEngine.Events;

public class Kamera : MonoBehaviour
{
    [SerializeField] float wheelSensitivity = 1; 
    [SerializeField] float pinchSensitivity = 0.05f; 
    [SerializeField] float rotationSpeed = 1;
    [SerializeField] float mouseMovementSpeed = 0.1f;

    [field: SerializeField] public Camera Cam { get; private set; }

    [SerializeField] Kamera parentKamera;
    [SerializeField] protected Kamera childKamera;

    public CenterPointer centerPointer;
    private Vector3 minimalPosition = new(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
    private Vector3 maximalPosition = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);

    private bool pinching;
    [SerializeField] private bool rotateWithMouse;
    public float maxOrthographicSize = 6f;
    public float minOrthographicSize = 0.3f;

    public UnityEvent MouseEnteredViewport = new();
    public UnityEvent MouseExitedViewport = new();

    protected void Awake() {
        if (Cam == null && TryGetComponent<Camera>(out var cam))
            Cam = cam;
        if (centerPointer == null && TryGetComponent<CenterPointerToTransformComponent>(out var cp))
            centerPointer = cp.centerPointer;
    }

    const float movementSpeed = 5f;
    private bool isMouseInViewport;

    void Update()
    {
        // follow parent
        var thisTransform = transform;
        var goalPosition = thisTransform.position;
        var goalRotation = thisTransform.rotation;
        if (parentKamera != null) {
            var parentTransform = parentKamera.transform;
            goalPosition = parentTransform.position;
            goalRotation = parentTransform.rotation;
            SetOrthographicSize(parentKamera.Cam.orthographicSize);
        }
        else if (centerPointer?.position != null) 
            goalPosition = (Vector3) centerPointer.position;


        var movement = (goalPosition - thisTransform.position);
        var maxDistanceThisFrame = movementSpeed * Time.deltaTime;
        if (movement.sqrMagnitude > maxDistanceThisFrame * maxDistanceThisFrame)
            movement = movement.normalized * maxDistanceThisFrame;
        transform.position += movement;
        transform.rotation = Quaternion.Slerp(thisTransform.rotation, goalRotation, Time.deltaTime * 5);

        Vector3 mousePosition = Input.touchCount > 0 ? Input.touches.First().position : Input.mousePosition;

        var mouseInViewport = IsMouseInViewport(mousePosition);
        if (!isMouseInViewport && mouseInViewport)
            MouseEnteredViewport.Invoke();
        else if (isMouseInViewport && !mouseInViewport)
            MouseExitedViewport.Invoke();
        isMouseInViewport = mouseInViewport;
        if (!mouseInViewport) return;
        
        // Zoom by mouse wheel
        float newOrthographicSize = Cam.orthographicSize * MathF.Exp( - Input.GetAxis("Mouse ScrollWheel") * wheelSensitivity );
        SetOrthographicSize(newOrthographicSize);

        // Zoom by finger pinch is broken on WebGL. Zoom will be deactivated on mobile devices.
        if (Input.touchCount == 2)
        {
            pinching = true;
            Touch touchZero = Input.GetTouch(0);
            Touch touchOne = Input.GetTouch(1);
            
            if (touchZero.phase == TouchPhase.Moved && touchOne.phase == TouchPhase.Moved) {
                Vector2 touchZeroPrevPos = touchZero.position - touchZero.deltaPosition;
                Vector2 touchOnePrevPos = touchOne.position - touchOne.deltaPosition;
                
                float prevTouchDeltaMag = (touchZeroPrevPos - touchOnePrevPos).magnitude;
                float touchDeltaMag = (touchZero.position - touchOne.position).magnitude;
                float deltaMagnitudeDiff = prevTouchDeltaMag - touchDeltaMag;

                deltaMagnitudeDiff = Mathf.Clamp(deltaMagnitudeDiff, -5, 5);
                var orthographicSize = Cam.orthographicSize;
                orthographicSize -= deltaMagnitudeDiff * pinchSensitivity;
                SetOrthographicSize(orthographicSize);
            }
            
        }
        
        if (Input.touchCount == 0 && pinching) {
            pinching = false;
        }

        // If mouse or finger is down, rotate the camera
        if (Input.GetMouseButton(0) && !pinching)
        {
            var delta = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
            Move(delta * 5);
            // todo: UX. Clamp position
        }
        if (Input.touchCount == 1 && !pinching) {
            Touch touchZero = Input.GetTouch(0);
            Move(touchZero.deltaPosition);
        }
    }

    private void SetOrthographicSize(float orthographicSize)
    {
        Cam.orthographicSize = Math.Clamp(orthographicSize, minOrthographicSize, maxOrthographicSize);
    }

    private void Move(Vector2 delta)
    {
        if (rotateWithMouse)
        {
            delta *= rotationSpeed;
            // Create a rotation for each axis and multiply them together
            Quaternion rotation = Quaternion.Euler(-delta.y, delta.x, 0);
            transform.rotation *= rotation;
        }
        else
        {
            delta *= mouseMovementSpeed * Cam.orthographicSize;
            var transformLocalPosition = transform.localPosition - (transform.right * delta.x + transform.up * delta.y);
            transform.localPosition = transformLocalPosition.Clamp(minimalPosition, maximalPosition);
        }
    }

    public virtual bool IsMouseInViewport(Vector3 mousePosition, bool ignoreSubCameras = false)
    {
        return Cam.ScreenToViewportPoint(mousePosition) is { x: <= 1 and >= 0, y: >= 0 and <= 1 } || 
               ( !ignoreSubCameras && childKamera != null && childKamera.IsMouseInViewport(mousePosition, true) );
    }

    public void zoomIn() => SetOrthographicSize(Cam.orthographicSize - 3);

    public void zoomOut() => SetOrthographicSize(Cam.orthographicSize + 3);

    public virtual Ray ScreenPointToRay(Vector3 mousePosition) => Cam.ScreenPointToRay(mousePosition);
    public int cullingMask => Cam.cullingMask;

    // referenced from Dropdown
    public void SetMask(int mode) {
        Cam.cullingMask = mode switch {
            0 => // Group & Subgroup
                LayerMask.GetMask("Default", "Subgroup", "SubgroupOnly"),
            1 => // Group only
                LayerMask.GetMask("Default", "Subgroup"),
            2 => // Subgroup only
                LayerMask.GetMask("Subgroup", "SubgroupOnly"),
            _ => Cam.cullingMask
        };
    }

    public void LockTo(Kamera other) {
        if (parentKamera != null) parentKamera.childKamera = null;
        if (other != null) other.childKamera = this;
        parentKamera = other;
    }

    protected void Initialize(Vector3 minimalPosition, Vector3 maximalPosition)
    {
        MinimalPosition = minimalPosition;
        MaximalPosition = maximalPosition;
        InitializeOrthographicSize();

        SetOrthographicSize( maxOrthographicSize * 0.6f );
    }

    public Vector3 MinimalPosition
    {
        get => minimalPosition;
        set
        {
            if (!maximalPosition.AtLeast(minimalPosition))
                Debug.LogError("Maximal position should be at least as large as minimal position.");
            if (float.IsInfinity(value.x))
                value.x = maximalPosition.x - 1;
            if (float.IsInfinity(value.y))
                value.y = maximalPosition.y - 1;
            minimalPosition = value;
            InitializeOrthographicSize();
        }
    }

    public Vector3 MaximalPosition
    {
        get => maximalPosition;
        set
        {
            if (!maximalPosition.AtLeast(minimalPosition))
                Debug.LogError("Maximal position should be at least as large as minimal position.");
            if (float.IsInfinity(value.x))
                value.x = minimalPosition.x + 1;
            if (float.IsInfinity(value.y))
                value.y = minimalPosition.y + 1;
            maximalPosition = value;
            InitializeOrthographicSize();
        }
    }

    private void InitializeOrthographicSize()
    {
        minOrthographicSize = MathF.Min(maximalPosition.x - minimalPosition.x, 
            maximalPosition.y - minimalPosition.y) / 30;
        maxOrthographicSize = MathF.Max(maximalPosition.x - minimalPosition.x,
            maximalPosition.y - minimalPosition.y) * 0.6f;
        SetOrthographicSize(Cam.orthographicSize);
    }
}
