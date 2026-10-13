using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Mouse-and-keyboard camera for walking through a level.
/// 3D: hold the right mouse button to look, and fly with WASD.
/// 2D: pan across the view, and zoom with the scroll wheel.
/// On Play, other cameras in the scene are disabled.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
[AddComponentMenu("Game Structure/Fly Camera")]
public class FlyCameraController : MonoBehaviour
{
    public enum ViewMode
    {
        [InspectorName("3D Fly")]
        Fly3D,
        [InspectorName("2D Walk")]
        Walk2D
    }

    [Header("Mode")]
    [Tooltip("3D Fly turns and moves through the scene. 2D Walk pans and zooms on screen and does not change Z.")]
    [SerializeField] ViewMode viewMode = ViewMode.Fly3D;

    [Header("Move")]
    [Tooltip("Base speed in world units per second. In 3D, the scroll wheel changes this. In 2D, this is the keyboard pan speed.")]
    [SerializeField] float moveSpeed = 8f;

    [Tooltip("Speed multiplier while Shift is held.")]
    [SerializeField] float fastMultiplier = 4f;

    [Tooltip("Lowest speed the scroll wheel can set in 3D.")]
    [SerializeField] float minSpeed = 0.5f;

    [Tooltip("Highest speed the scroll wheel can set in 3D.")]
    [SerializeField] float maxSpeed = 80f;

    [Tooltip("How many units per second each scroll notch adds or removes in 3D.")]
    [SerializeField] float scrollSpeedStep = 1.5f;

    [Header("3D Look")]
    [Tooltip("Mouse sensitivity in degrees per pixel. Used in 3D only.")]
    [SerializeField] float lookSensitivity = 0.12f;

    [SerializeField] float minPitch = -89f;
    [SerializeField] float maxPitch = 89f;

    [Header("2D Zoom")]
    [Tooltip("Scale applied per scroll notch. 0.1 means one notch becomes 90% or about 111%.")]
    [SerializeField] float zoomStep = 0.1f;

    [SerializeField] float minOrthographicSize = 0.5f;
    [SerializeField] float maxOrthographicSize = 40f;

    [Tooltip("How many scroll notches per second Q and E apply while held.")]
    [SerializeField] float keyboardZoomRate = 6f;

    [Header("Hint")]
    [Tooltip("Show the control list in the corner during Play.")]
    [SerializeField] bool showControlsHint = true;

    float _yaw;
    float _pitch;
    bool _looking;
    bool _escapeLatched;
    bool _camerasTakenOver;
    GUIStyle _hintStyle;
    Texture2D _hintBackground;

    void OnEnable()
    {
        ReadAnglesFromTransform();
        ApplyProjection();
    }

    void OnValidate()
    {
        ApplyProjection();
    }

    void OnDisable()
    {
        _looking = false;
        ReleaseCursor();
        if (_hintBackground != null)
        {
            Destroy(_hintBackground);
            _hintBackground = null;
            _hintStyle = null;
        }
    }

    void Start()
    {
        DisableOtherCameras();
    }

    void LateUpdate()
    {
        if (_camerasTakenOver)
            return;

        _camerasTakenOver = true;
        DisableOtherCameras();
    }

    void DisableOtherCameras()
    {
        int disabledCameras = 0;
        Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (Camera camera in cameras)
        {
            if (camera == null || BelongsToThisCamera(camera.transform))
                continue;

            if (camera.enabled)
            {
                camera.enabled = false;
                disabledCameras++;
            }

            FlyCameraController other = camera.GetComponent<FlyCameraController>();
            if (other != null && other != this)
                other.enabled = false;
        }

        AudioListener[] listeners = FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (AudioListener listener in listeners)
        {
            if (listener == null || BelongsToThisCamera(listener.transform))
                continue;

            listener.enabled = false;
        }

        if (disabledCameras > 0)
            Debug.Log($"Fly Camera disabled {disabledCameras} other camera(s).", this);
    }

    bool BelongsToThisCamera(Transform other)
    {
        return other == transform || other.IsChildOf(transform);
    }

    void Update()
    {
        ApplyProjection();
        if (viewMode == ViewMode.Walk2D)
        {
            _looking = false;
            UpdateWalk2D();
            return;
        }

        UpdateFly3D();
    }

    void UpdateFly3D()
    {
        bool rightMouse = ReadRightMouse();
        if (ReadEscape())
            _escapeLatched = true;
        if (!rightMouse)
            _escapeLatched = false;

        bool looking = rightMouse && !_escapeLatched;
        if (looking && !_looking)
            ReadAnglesFromTransform();
        _looking = looking;
        ApplyCursor(looking);

        if (looking)
        {
            Vector2 look = ReadLookPixels();
            _yaw += look.x * lookSensitivity;
            _pitch = Mathf.Clamp(_pitch - look.y * lookSensitivity, minPitch, maxPitch);
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        float scroll = ReadScrollNotches();
        if (Mathf.Abs(scroll) > 0.001f)
            moveSpeed = Mathf.Clamp(moveSpeed + scroll * scrollSpeedStep, minSpeed, maxSpeed);

        Vector3 wish = ReadMoveAxes();
        if (wish.sqrMagnitude > 1f)
            wish.Normalize();

        float speed = moveSpeed * (ReadFast() ? fastMultiplier : 1f);
        Vector3 world = transform.right * wish.x + Vector3.up * wish.y + transform.forward * wish.z;
        transform.position += world * (speed * Time.unscaledDeltaTime);
    }

    void UpdateWalk2D()
    {
        ReleaseCursor();
        Camera cam = GetComponent<Camera>();
        float dt = Time.unscaledDeltaTime;

        float notches = ReadScrollNotches() + ReadZoomKeys() * keyboardZoomRate * dt;
        ZoomAtCursor(cam, notches);

        Vector3 axes = ReadMoveAxes();
        Vector2 pan = new Vector2(axes.x, axes.z);
        if (pan.sqrMagnitude > 1f)
            pan.Normalize();

        FlattenAxes(out Vector3 right, out Vector3 up);
        float speed = moveSpeed * (ReadFast() ? fastMultiplier : 1f);
        Vector3 delta = (right * pan.x + up * pan.y) * (speed * dt);

        if (ReadRightMouse() || ReadMiddleMouse())
        {
            Vector2 pixels = ReadLookPixels();
            float unitsPerPixel = (cam.orthographicSize * 2f) / Mathf.Max(1, Screen.height);
            delta += (right * -pixels.x + up * -pixels.y) * unitsPerPixel;
        }

        Vector3 position = transform.position + delta;
        position.z = transform.position.z;
        transform.position = position;
    }

    void OnGUI()
    {
        if (!showControlsHint)
            return;

        if (_hintStyle == null)
        {
            _hintBackground = new Texture2D(1, 1);
            _hintBackground.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.72f));
            _hintBackground.Apply();

            _hintStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 14,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
                padding = new RectOffset(10, 10, 8, 8)
            };
            _hintStyle.normal.textColor = Color.white;
            _hintStyle.normal.background = _hintBackground;
        }

        string text = viewMode == ViewMode.Walk2D
            ? "Walk Camera 2D\n" +
              "WASD / arrows: pan\n" +
              "Hold right or middle mouse and drag: pan\n" +
              "Scroll zooms toward the cursor    Q / E: zoom out / in    Shift: faster keys"
            : "Fly Camera\n" +
              "Hold right mouse and move: look\n" +
              "WASD / arrows: move    Q / E: down / up\n" +
              "Shift: faster    scroll: change speed    Esc: release mouse";
        GUI.Label(new Rect(12f, 12f, 560f, 96f), text, _hintStyle);
    }

    void ApplyProjection()
    {
        Camera cam = GetComponent<Camera>();
        if (cam == null)
            return;

        bool orthographic = viewMode == ViewMode.Walk2D;
        if (cam.orthographic != orthographic)
            cam.orthographic = orthographic;
    }

    void ZoomAtCursor(Camera cam, float notches)
    {
        if (Mathf.Abs(notches) < 0.001f)
            return;

        float step = Mathf.Clamp(zoomStep, 0.01f, 0.5f);
        float minSize = Mathf.Min(minOrthographicSize, maxOrthographicSize);
        float maxSize = Mathf.Max(minOrthographicSize, maxOrthographicSize);
        Vector2 screen = ReadMouseScreen();
        Vector3 before = ScreenToWorld2D(cam, screen);
        cam.orthographicSize = Mathf.Clamp(
            cam.orthographicSize * Mathf.Pow(1f - step, notches),
            minSize,
            maxSize);
        Vector3 after = ScreenToWorld2D(cam, screen);
        Vector3 position = transform.position + (before - after);
        position.z = transform.position.z;
        transform.position = position;
    }

    void FlattenAxes(out Vector3 right, out Vector3 up)
    {
        right = transform.right;
        up = transform.up;
        right.z = 0f;
        up.z = 0f;
        if (right.sqrMagnitude < 0.0001f)
            right = Vector3.right;
        else
            right.Normalize();
        if (up.sqrMagnitude < 0.0001f)
            up = Vector3.up;
        else
            up.Normalize();
    }

    static Vector3 ScreenToWorld2D(Camera cam, Vector2 screen)
    {
        float depth = Mathf.Abs(cam.transform.position.z);
        if (depth < cam.nearClipPlane)
            depth = cam.nearClipPlane;
        Vector3 world = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, depth));
        world.z = 0f;
        return world;
    }

    void ReadAnglesFromTransform()
    {
        Vector3 euler = transform.eulerAngles;
        _yaw = euler.y;
        _pitch = euler.x;
        if (_pitch > 180f)
            _pitch -= 360f;
    }

    void ApplyCursor(bool looking)
    {
        if (looking)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            return;
        }

        ReleaseCursor();
    }

    static void ReleaseCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    bool UseNewInput
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null || Mouse.current != null;
#else
            return false;
#endif
        }
    }

    bool ReadRightMouse()
    {
#if ENABLE_INPUT_SYSTEM
        if (UseNewInput && Mouse.current != null)
            return Mouse.current.rightButton.isPressed;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetMouseButton(1);
#else
        return false;
#endif
    }

    bool ReadMiddleMouse()
    {
#if ENABLE_INPUT_SYSTEM
        if (UseNewInput && Mouse.current != null)
            return Mouse.current.middleButton.isPressed;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetMouseButton(2);
#else
        return false;
#endif
    }

    bool ReadEscape()
    {
#if ENABLE_INPUT_SYSTEM
        if (UseNewInput && Keyboard.current != null)
            return Keyboard.current.escapeKey.wasPressedThisFrame;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(KeyCode.Escape);
#else
        return false;
#endif
    }

    bool ReadFast()
    {
#if ENABLE_INPUT_SYSTEM
        if (UseNewInput && Keyboard.current != null)
        {
            return Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#else
        return false;
#endif
    }

    float ReadZoomKeys()
    {
#if ENABLE_INPUT_SYSTEM
        if (UseNewInput && Keyboard.current != null)
            return Axis(Keyboard.current.eKey.isPressed, Keyboard.current.qKey.isPressed);
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Axis(Input.GetKey(KeyCode.E), Input.GetKey(KeyCode.Q));
#else
        return 0f;
#endif
    }

    Vector2 ReadLookPixels()
    {
#if ENABLE_INPUT_SYSTEM
        if (UseNewInput && Mouse.current != null)
            return Mouse.current.delta.ReadValue();
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        // The legacy Mouse X/Y axes are scaled by 0.1. Convert back to pixels so sensitivity stays in degrees per pixel.
        return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 10f;
#else
        return Vector2.zero;
#endif
    }

    Vector2 ReadMouseScreen()
    {
#if ENABLE_INPUT_SYSTEM
        if (UseNewInput && Mouse.current != null)
            return Mouse.current.position.ReadValue();
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.mousePosition;
#else
        return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
#endif
    }

    float ReadScrollNotches()
    {
#if ENABLE_INPUT_SYSTEM
        if (UseNewInput && Mouse.current != null)
        {
            float y = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(y) < 0.01f)
                return 0f;
            // One notch is often 120 on Windows, and about ±1 on macOS or a trackpad.
            return Mathf.Abs(y) >= 10f ? y / 120f : y;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetAxis("Mouse ScrollWheel") * 10f;
#else
        return 0f;
#endif
    }

    Vector3 ReadMoveAxes()
    {
#if ENABLE_INPUT_SYSTEM
        if (UseNewInput && Keyboard.current != null)
        {
            Keyboard kb = Keyboard.current;
            float x = Axis(kb.dKey.isPressed || kb.rightArrowKey.isPressed, kb.aKey.isPressed || kb.leftArrowKey.isPressed);
            float y = Axis(kb.eKey.isPressed || kb.spaceKey.isPressed, kb.qKey.isPressed || kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed);
            float z = Axis(kb.wKey.isPressed || kb.upArrowKey.isPressed, kb.sKey.isPressed || kb.downArrowKey.isPressed);
            return new Vector3(x, y, z);
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        float legacyX = Mathf.Clamp(Input.GetAxisRaw("Horizontal"), -1f, 1f);
        float legacyZ = Mathf.Clamp(Input.GetAxisRaw("Vertical"), -1f, 1f);
        float legacyY = Axis(
            Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.Space),
            Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl));
        return new Vector3(legacyX, legacyY, legacyZ);
#else
        return Vector3.zero;
#endif
    }

    static float Axis(bool positive, bool negative)
    {
        float value = (positive ? 1f : 0f) - (negative ? 1f : 0f);
        return Mathf.Clamp(value, -1f, 1f);
    }
}
