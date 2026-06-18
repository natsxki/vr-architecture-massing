using System;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Manages ground cursor, anchor placement, and voice trigger signal.
/// Uses low-level UnityEngine.XR device polling — no InputActionReference needed.
///
/// Quest 3S right controller bindings:
///   Primary Trigger      -> place / replace anchor (ONLY if Y is approx 0)
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
    public LayerMask groundLayerMask = 1;

    [Tooltip("Ray shoots from here. Assign your Right Controller Transform. Falls back to Camera if null.")]
    public Transform rayOrigin;

    public float raycastMaxDistance = 50f;

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
    // Public state  (read by LLM / speech teammates)
    // -------------------------------------------------------------------------

    public bool    HasAnchor           { get; private set; }
    public Vector3 AnchorWorldPosition { get; private set; }

    /// <summary>
    /// Fired when grip is pressed (and held) with an active anchor.
    /// Speech module subscribes here to START recording.
    /// </summary>
    public event Action OnVoiceRecordStart;

    /// <summary>
    /// Fired when grip is released. Passes the anchor coordinates.
    /// Speech module subscribes here to STOP recording and send data to LLM.
    /// </summary>
    public event Action<Vector3> OnVoiceRecordStop;

    // -------------------------------------------------------------------------
    // Private
    // -------------------------------------------------------------------------

    private GameObject _cursorDisc;
    private GameObject _crossMarker;
    private LineRenderer _pointerLine;

    // Rising/Falling edge state
    private bool _triggerWasPressed = false;
    private bool _gripWasPressed    = false;
    private bool _bWasPressed       = false;

    // Recording state lock
    private bool _isRecording       = false;

    // Device search throttle
    private InputDevice _rightController;
    private bool _deviceFound    = false;
    private float _nextSearchTime = 0f;

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        if (rayOrigin == null && Camera.main != null)
        {
            rayOrigin = Camera.main.transform;
            Debug.LogWarning("[AnchorSystem] Ray Origin not assigned — falling back to Main Camera. " +
                             "For accurate ground pointing, assign the Right Controller Transform.");
        }

        BuildCursorDisc();
        BuildCrossMarker();
        BuildPointerLine();
        
        _cursorDisc.SetActive(false);
        _crossMarker.SetActive(false);

        Debug.Log("[AnchorSystem] Awake complete.");
    }

    private void Update()
    {
        TryFindRightController();
        if (!_deviceFound) return;

        // --- Read analog values ---
        _rightController.TryGetFeatureValue(CommonUsages.trigger, out float triggerVal);
        _rightController.TryGetFeatureValue(CommonUsages.grip,    out float gripVal);
        _rightController.TryGetFeatureValue(CommonUsages.secondaryButton, out bool bPressed);

        bool triggerPressed = triggerVal >= analogThreshold;
        bool gripPressed    = gripVal    >= analogThreshold;

        // --- Rising & Falling edges ---
        bool triggerRising = triggerPressed && !_triggerWasPressed;
        bool bRising       = bPressed       && !_bWasPressed;
        
        bool gripRising    = gripPressed    && !_gripWasPressed;
        bool gripFalling   = !gripPressed   && _gripWasPressed;

        // --- Raycast & Pointer Line ---
        bool hitGround = false;
        RaycastHit hit = default;
        if (rayOrigin != null)
        {
            _pointerLine.enabled = true;
            _pointerLine.SetPosition(0, rayOrigin.position);

            Ray ray = new Ray(rayOrigin.position, rayOrigin.forward);
            hitGround = Physics.Raycast(ray, out hit, raycastMaxDistance, groundLayerMask);

            if (hitGround)
            {
                _pointerLine.SetPosition(1, hit.point);
                
                _cursorDisc.SetActive(true);
                _cursorDisc.transform.position = hit.point + Vector3.up * cursorYOffset;
                _cursorDisc.transform.rotation = Quaternion.FromToRotation(Vector3.up, hit.normal);
            }
            else
            {
                // Shoot line to max distance if nothing is hit
                _pointerLine.SetPosition(1, rayOrigin.position + rayOrigin.forward * raycastMaxDistance);
                _cursorDisc.SetActive(false);
            }
        }

        // --- Place anchor ---
        if (triggerRising)
        {
            if (hitGround)
            {
                // Check if the hit point is on the Y=0 plane (using a small 1cm tolerance for float precision)
                if (Mathf.Abs(hit.point.y) < 0.01f)
                {
                    AnchorWorldPosition = hit.point;
                    HasAnchor = true;
                    _crossMarker.SetActive(true);
                    _crossMarker.transform.position = hit.point + Vector3.up * crossYOffset;
                    Debug.Log($"[AnchorSystem] *** Anchor PLACED at {AnchorWorldPosition} ***");
                }
                else
                {
                    Debug.LogWarning($"[AnchorSystem] Anchor placement ignored. Y coordinate is {hit.point.y:F4}, not exactly 0.");
                }
            }
        }

        // --- Cancel anchor ---
        if (bRising)
        {
            HasAnchor = false;
            _crossMarker.SetActive(false);
            
            // Safety measure: if user cancels anchor while recording, abort recording
            if (_isRecording)
            {
                _isRecording = false;
                Debug.LogWarning("[AnchorSystem] Anchor cancelled during recording. Aborting voice record.");
            }
            
            Debug.Log("[AnchorSystem] *** Anchor CANCELLED ***");
        }

        // --- Voice Trigger Logic (Press and Hold) ---
        if (gripRising)
        {
            if (HasAnchor)
            {
                _isRecording = true;
                Debug.Log("[AnchorSystem] *** Voice Recording STARTED ***");
                OnVoiceRecordStart?.Invoke();
            }
            else
            {
                Debug.LogWarning("[AnchorSystem] Grip pressed but no anchor set. Place one first.");
            }
        }

        if (gripFalling)
        {
            if (_isRecording)
            {
                _isRecording = false;
                Debug.Log($"[AnchorSystem] *** Voice Recording STOPPED. Sending Data. Anchor={AnchorWorldPosition} ***");
                OnVoiceRecordStop?.Invoke(AnchorWorldPosition);
            }
        }

        // --- Store previous state ---
        _triggerWasPressed = triggerPressed;
        _gripWasPressed    = gripPressed;
        _bWasPressed       = bPressed;
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

    // -------------------------------------------------------------------------
    // Procedural mesh helpers
    // -------------------------------------------------------------------------

    private void BuildPointerLine()
    {
        GameObject lineObj = new GameObject("PointerLine");
        lineObj.transform.SetParent(this.transform);
        _pointerLine = lineObj.AddComponent<LineRenderer>();
        
        _pointerLine.startWidth = lineWidth;
        _pointerLine.endWidth = lineWidth;
        _pointerLine.positionCount = 2;
        
        // Use an Unlit shader for the line so it remains bright and visible
        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.color = lineColor;
        _pointerLine.material = mat;
        _pointerLine.enabled = false;
    }

    private void BuildCursorDisc()
    {
        _cursorDisc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        _cursorDisc.name = "GroundCursor";
        Destroy(_cursorDisc.GetComponent<Collider>());
        _cursorDisc.transform.localScale = new Vector3(cursorDiameter, 0.005f, cursorDiameter);

        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.color = cursorColor;
        mat.SetFloat("_Surface", 1);
        mat.renderQueue = 3000;
        _cursorDisc.GetComponent<Renderer>().material = mat;
    }

    private void BuildCrossMarker()
    {
        _crossMarker = new GameObject("AnchorCross");
        for (int i = 0; i < 2; i++)
        {
            var arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
            arm.name = i == 0 ? "CrossArm_A" : "CrossArm_B";
            arm.transform.SetParent(_crossMarker.transform, false);
            Destroy(arm.GetComponent<Collider>());
            arm.transform.localScale    = new Vector3(crossArmLength, 0.01f, crossArmWidth);
            arm.transform.localRotation = Quaternion.Euler(0, i * 90f, 0);

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = crossColor;
            arm.GetComponent<Renderer>().material = mat;
        }
    }
}