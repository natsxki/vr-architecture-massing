using System;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Manages ground cursor, anchor placement, and voice trigger signal.
/// Uses low-level UnityEngine.XR device polling — no InputActionReference needed.
///
/// Quest 3S right controller bindings:
///   Primary Trigger      -> place / replace anchor
///   B Button             -> cancel anchor
///   Grip (Press & Hold)  -> fire OnVoiceRecordStart
///   Grip (Release)       -> fire OnVoiceRecordStop (passes anchor position)
/// </summary>
public class GroundAnchorManager : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector fields
    // -------------------------------------------------------------------------

    [Header("Raycast Setup")]
    [Tooltip("Layer mask for the ground plane. Set your lawn GameObject to a dedicated layer and assign it here.")]
    public LayerMask groundLayerMask = ~0;

    [Tooltip("Ray shoots from here. Assign your Right Controller Transform. Falls back to RightControllerAnchor if null.")]
    public Transform rayOrigin;

    public float raycastMaxDistance = 50f;

    [Header("Ground Filter")]
    [Tooltip("Minimum Y component of hit normal to count as ground (1 = flat, 0.7 = up to ~45° slope).")]
    public float minGroundNormalY = 0.7f;
    [Tooltip("Maximum world Y position of a valid ground hit. Keeps anchor on the floor, not on tables or walls.")]
    public float maxGroundHitY = 0.3f;

    [Header("Pointer Line")]
    public float lineWidth = 0.005f;
    public Color lineColor = new Color(1f, 1f, 1f, 0.4f);

    [Header("Cursor")]
    public float cursorDiameter = 0.3f;
    public float cursorYOffset  = 0.005f;

    [Header("Anchor Marker (Red X)")]
    public float crossArmLength = 0.25f;
    public float crossArmWidth  = 0.04f;
    public float crossYOffset   = 0.005f;

    [Header("Colors")]
    public Color cursorColor = new Color(1f, 1f, 1f, 0.55f);
    public Color crossColor  = Color.red;

    [Header("Input Thresholds")]
    [Tooltip("Analog trigger/grip value above which we treat it as pressed.")]
    public float analogThreshold = 0.7f;

    // -------------------------------------------------------------------------
    // Public state
    // -------------------------------------------------------------------------

    public static GroundAnchorManager Instance { get; private set; }

    public bool    HasAnchor           { get; private set; }
    public Vector3 AnchorWorldPosition { get; private set; }

    public event Action OnVoiceRecordStart;
    public event Action<Vector3> OnVoiceRecordStop;

    // Fired when the user grips a controller in ViewingOptions to choose an option
    public event Action OnOptionASelected;
    public event Action OnOptionBSelected;

    // -------------------------------------------------------------------------
    // Private
    // -------------------------------------------------------------------------

    private GameObject   _cursorDisc;
    private GameObject   _crossMarker;
    private LineRenderer _pointerLine;

    private bool _triggerWasPressed;
    private bool _gripWasPressed;
    private bool _leftGripWasPressed;
    private bool _bWasPressed;
    private bool _yWasPressed;
    private bool _isRecording;

    private InputDevice _rightController;
    private bool  _deviceFound;
    private float _nextSearchTime;

    private InputDevice _leftController;
    private bool  _leftDeviceFound;
    private float _nextLeftSearchTime;

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        BuildCursorDisc();
        BuildCrossMarker();
        BuildPointerLine();

        _cursorDisc.SetActive(false);
        _crossMarker.SetActive(false);
    }

    /// <summary>Clears anchor state and hides all markers. Call before returning to splash.</summary>
    public void ResetAnchor()
    {
        HasAnchor    = false;
        _isRecording = false;
        _crossMarker.SetActive(false);
        _cursorDisc.SetActive(false);
        _pointerLine.enabled      = false;
        _triggerWasPressed        = false;
        _gripWasPressed           = false;
        _leftGripWasPressed       = false;
        _bWasPressed              = false;
        _yWasPressed              = false;
    }

    private void Start()
    {
        if (rayOrigin != null) return;

        GameObject found = GameObject.Find("Right Controller")
                        ?? GameObject.Find("RightHand Controller")
                        ?? GameObject.Find("RightControllerAnchor")
                        ?? GameObject.Find("RightHandAnchor");

        if (found != null)
        {
            rayOrigin = found.transform;
            Debug.Log($"[AnchorSystem] Ray origin auto-set to '{found.name}'.");
        }
        else if (Camera.main != null)
        {
            rayOrigin = Camera.main.transform;
            Debug.LogWarning("[AnchorSystem] Controller anchor not found — falling back to camera. Assign Ray Origin in the Inspector.");
        }
    }

    private void Update()
    {
        TryFindRightController();
        TryFindLeftController();
        if (!_deviceFound) return;

        // Dormant during splash screen
        if (AppStateManager.Instance != null && AppStateManager.Instance.CurrentState == AppState.Splash)
        {
            _pointerLine.enabled = false;
            _cursorDisc.SetActive(false);
            return;
        }

        // --- Read inputs ---
        _rightController.TryGetFeatureValue(CommonUsages.trigger,         out float triggerVal);
        _rightController.TryGetFeatureValue(CommonUsages.grip,            out float gripVal);
        _rightController.TryGetFeatureValue(CommonUsages.secondaryButton, out bool bPressed);

        bool yPressed      = false;
        float leftGripVal  = 0f;
        if (_leftDeviceFound)
        {
            _leftController.TryGetFeatureValue(CommonUsages.secondaryButton, out yPressed);
            _leftController.TryGetFeatureValue(CommonUsages.grip, out leftGripVal);
        }

        bool triggerPressed  = triggerVal  >= analogThreshold;
        bool gripPressed     = gripVal     >= analogThreshold;
        bool leftGripPressed = leftGripVal >= analogThreshold;

        bool triggerRising  = triggerPressed  && !_triggerWasPressed;
        bool bRising        = bPressed        && !_bWasPressed;
        bool gripRising     = gripPressed     && !_gripWasPressed;
        bool gripFalling    = !gripPressed    && _gripWasPressed;
        bool yRising        = yPressed        && !_yWasPressed;
        bool leftGripRising = leftGripPressed && !_leftGripWasPressed;

        // --- Pointer, cursor and trigger locked in design/review states ---
        bool designLocked = IsDesignLocked();
        bool hitGround = false;
        RaycastHit hit = default;

        if (!designLocked)
        {
            if (rayOrigin == null)
            {
                _pointerLine.enabled = false;
                _cursorDisc.SetActive(false);
                _triggerWasPressed = triggerPressed;
                _gripWasPressed    = gripPressed;
                _bWasPressed       = bPressed;
                return;
            }

            Debug.DrawRay(rayOrigin.position, rayOrigin.forward * raycastMaxDistance, Color.green);

            Ray ray = new Ray(rayOrigin.position, rayOrigin.forward);
            bool hitAnything = Physics.Raycast(ray, out hit, raycastMaxDistance, groundLayerMask);

            hitGround = hitAnything
                        && hit.normal.y >= minGroundNormalY
                        && hit.point.y  <= maxGroundHitY;

            _pointerLine.enabled = true;
            _pointerLine.SetPosition(0, rayOrigin.position);
            _pointerLine.SetPosition(1, hitAnything ? hit.point : rayOrigin.position + rayOrigin.forward * raycastMaxDistance);

            if (hitGround)
            {
                _cursorDisc.SetActive(true);
                _cursorDisc.transform.position = hit.point + Vector3.up * cursorYOffset;
                _cursorDisc.transform.rotation = Quaternion.FromToRotation(Vector3.up, hit.normal);
            }
            else
            {
                _cursorDisc.SetActive(false);
            }

            if (triggerRising && hitGround)
            {
                UndoManager.Instance?.PushAnchor(AnchorWorldPosition, HasAnchor);
                AnchorWorldPosition = hit.point;
                HasAnchor = true;
                _crossMarker.SetActive(true);
                _crossMarker.transform.position = hit.point + Vector3.up * crossYOffset;
                AppStateManager.Instance?.SetState(AppState.PlacingAnchor);
                Debug.Log($"[AnchorSystem] Anchor placed at {AnchorWorldPosition}");
            }
        }
        else
        {
            _pointerLine.enabled = false;
            _cursorDisc.SetActive(false);
        }

        // --- B Button: cancel anchor (always available) ---
        if (bRising && HasAnchor)
        {
            UndoManager.Instance?.PushAnchor(AnchorWorldPosition, HasAnchor);
            HasAnchor = false;
            _crossMarker.SetActive(false);
            _isRecording = false;
            AppStateManager.Instance?.SetState(AppState.Idle);
            Debug.Log("[AnchorSystem] Anchor cancelled");
        }

        // --- Option selection (ViewingOptions only): grips choose A / B ---
        var currentState = AppStateManager.Instance?.CurrentState;
        bool inViewingOptions = currentState == AppState.ViewingOptions;

        if (gripRising || leftGripRising)
            Debug.Log($"[AnchorSystem] Grip detected — state={currentState}, inViewingOptions={inViewingOptions}, leftGrip={leftGripRising}, rightGrip={gripRising}");

        if (inViewingOptions)
        {
            if (leftGripRising) { Debug.Log("[AnchorSystem] Option A selected"); OnOptionASelected?.Invoke(); }
            if (gripRising)     { Debug.Log("[AnchorSystem] Option B selected"); OnOptionBSelected?.Invoke(); }
        }

        // --- Grip: voice recording (only when anchor placed and NOT in ViewingOptions) ---
        if (!inViewingOptions)
        {
            if (gripRising && HasAnchor)
            {
                _isRecording = true;
                OnVoiceRecordStart?.Invoke();
                Debug.Log("[AnchorSystem] Voice recording started");
            }

            if (gripFalling && _isRecording)
            {
                _isRecording = false;
                OnVoiceRecordStop?.Invoke(AnchorWorldPosition);
                Debug.Log("[AnchorSystem] Voice recording stopped");
            }
        }

        // --- Y Button (left controller): submit description to Gemini ---
        if (yRising)
        {
            Debug.Log("[AnchorSystem] Y pressed — submitting to Gemini.");
            MuseumDescriptionPanel.Instance?.Submit();
        }

        // --- Store previous frame ---
        _triggerWasPressed  = triggerPressed;
        _gripWasPressed     = gripPressed;
        _leftGripWasPressed = leftGripPressed;
        _bWasPressed        = bPressed;
        _yWasPressed        = yPressed;
    }

    // -------------------------------------------------------------------------
    // State helpers
    // -------------------------------------------------------------------------

    private static bool IsDesignLocked()
    {
        // Pointer and anchor-move are only allowed in PositionSelection phase (Idle state).
        // Once an anchor is placed the phase advances to Design or beyond — lock everything
        // until B is pressed to cancel the anchor and return to Idle.
        var phase = AppStateManager.Instance?.CurrentPhase;
        return phase != AppPhase.None && phase != AppPhase.PositionSelection;
    }

    // -------------------------------------------------------------------------
    // Device discovery
    // -------------------------------------------------------------------------

    private void TryFindRightController()
    {
        if (_deviceFound) return;
        if (Time.time < _nextSearchTime) return;
        _nextSearchTime = Time.time + 2f;

        var devices = new System.Collections.Generic.List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller, devices);

        if (devices.Count > 0)
        {
            _rightController = devices[0];
            _deviceFound     = true;
            Debug.Log($"[AnchorSystem] Right controller found: '{_rightController.name}'");
        }
    }

    private void TryFindLeftController()
    {
        if (_leftDeviceFound) return;
        if (Time.time < _nextLeftSearchTime) return;
        _nextLeftSearchTime = Time.time + 2f;

        var devices = new System.Collections.Generic.List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller, devices);

        if (devices.Count > 0)
        {
            _leftController  = devices[0];
            _leftDeviceFound = true;
            Debug.Log($"[AnchorSystem] Left controller found: '{_leftController.name}'");
        }
    }

    // -------------------------------------------------------------------------
    // Procedural mesh helpers
    // -------------------------------------------------------------------------

    private void BuildPointerLine()
    {
        GameObject lineObj = new GameObject("PointerLine");
        lineObj.transform.SetParent(this.transform);
        _pointerLine = lineObj.AddComponent<LineRenderer>();

        _pointerLine.startWidth    = lineWidth;
        _pointerLine.endWidth      = lineWidth;
        _pointerLine.positionCount = 2;

        Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit")
                          ?? Shader.Find("Unlit/Color");
        var mat = new Material(unlitShader);
        mat.color = lineColor;
        _pointerLine.material = mat;
        _pointerLine.enabled  = false;
    }

    private void BuildCursorDisc()
    {
        _cursorDisc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        _cursorDisc.name = "GroundCursor";
        Destroy(_cursorDisc.GetComponent<Collider>());
        _cursorDisc.transform.localScale = new Vector3(cursorDiameter, 0.005f, cursorDiameter);

        Shader litShader = Shader.Find("Universal Render Pipeline/Lit")
                        ?? Shader.Find("Standard");
        var mat = new Material(litShader);
        mat.color = cursorColor;
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1);
        mat.renderQueue = 3000;
        _cursorDisc.GetComponent<Renderer>().material = mat;
    }

    private void BuildCrossMarker()
    {
        _crossMarker = new GameObject("AnchorCross");

        Shader litShader = Shader.Find("Universal Render Pipeline/Lit")
                        ?? Shader.Find("Standard");

        for (int i = 0; i < 2; i++)
        {
            var arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
            arm.name = i == 0 ? "CrossArm_A" : "CrossArm_B";
            arm.transform.SetParent(_crossMarker.transform, false);
            Destroy(arm.GetComponent<Collider>());
            arm.transform.localScale    = new Vector3(crossArmLength, 0.01f, crossArmWidth);
            arm.transform.localRotation = Quaternion.Euler(0, i * 90f, 0);

            var mat = new Material(litShader);
            mat.color = crossColor;
            arm.GetComponent<Renderer>().material = mat;
        }
    }

    // -------------------------------------------------------------------------
    // Undo support
    // -------------------------------------------------------------------------

    public void RestoreAnchor(Vector3 prevPos, bool hadAnchor)
    {
        HasAnchor           = hadAnchor;
        AnchorWorldPosition = prevPos;
        _crossMarker.SetActive(hadAnchor);
        if (hadAnchor)
            _crossMarker.transform.position = prevPos + Vector3.up * crossYOffset;
        AppStateManager.Instance?.SetState(hadAnchor ? AppState.PlacingAnchor : AppState.Idle);
    }
}
