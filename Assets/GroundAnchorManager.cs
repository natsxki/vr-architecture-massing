using System;
using UnityEngine;
using UnityEngine.XR;
// Note: using UnityEngine.XR (not UnityEngine.XR.Interaction.Toolkit)
// This uses the low-level XR device API which works with any XR headset.

/// <summary>
/// Manages ground cursor, anchor placement, and voice trigger signal.
/// Uses low-level UnityEngine.XR device polling — no InputActionReference needed.
///
/// Quest 3S right controller bindings:
///   Primary Trigger  -> place / replace anchor
///   B Button         -> cancel anchor
///   Grip             -> fire OnVoiceTrigger event (voice module entry point)
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
    /// Fired when grip is pressed with an active anchor.
    /// Speech module subscribes here:
    ///   FindObjectOfType&lt;GroundAnchorManager&gt;().OnVoiceTrigger += YourMethod;
    /// </summary>
    public event Action<Vector3> OnVoiceTrigger;

    // -------------------------------------------------------------------------
    // Private
    // -------------------------------------------------------------------------

    private GameObject _cursorDisc;
    private GameObject _crossMarker;

    // Rising-edge state
    private bool _triggerWasPressed = false;
    private bool _gripWasPressed    = false;
    private bool _bWasPressed       = false;

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

        // --- Rising edge ---
        bool triggerRising = triggerPressed && !_triggerWasPressed;
        bool gripRising    = gripPressed    && !_gripWasPressed;
        bool bRising       = bPressed       && !_bWasPressed;

        // --- Raycast ---
        bool hitGround = false;
        RaycastHit hit = default;
        if (rayOrigin != null)
        {
            Ray ray = new Ray(rayOrigin.position, rayOrigin.forward);
            hitGround = Physics.Raycast(ray, out hit, raycastMaxDistance, groundLayerMask);

            // Update cursor
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
        }

        // --- Place anchor ---
        if (triggerRising)
        {
            Debug.Log($"[AnchorSystem] Trigger pressed. hitGround={hitGround}");
            if (hitGround)
            {
                AnchorWorldPosition = hit.point;
                HasAnchor = true;
                _crossMarker.SetActive(true);
                _crossMarker.transform.position = hit.point + Vector3.up * crossYOffset;
                Debug.Log($"[AnchorSystem] *** Anchor PLACED at {AnchorWorldPosition} ***");
            }
            else
            {
                // Extra debug: cast without mask to identify what was hit
                Ray debugRay = new Ray(rayOrigin.position, rayOrigin.forward);
                if (Physics.Raycast(debugRay, out RaycastHit debugHit, raycastMaxDistance))
                    Debug.LogWarning($"[AnchorSystem] Ray hit '{debugHit.collider.name}' " +
                                     $"on layer '{LayerMask.LayerToName(debugHit.collider.gameObject.layer)}' " +
                                     $"— not your ground layer. Check groundLayerMask.");
                else
                    Debug.LogWarning("[AnchorSystem] Ray hit nothing. Is the ground collider enabled?");
            }
        }

        // --- Cancel anchor ---
        if (bRising)
        {
            Debug.Log($"[AnchorSystem] B pressed. Cancelling anchor (was {HasAnchor}).");
            HasAnchor = false;
            _crossMarker.SetActive(false);
            Debug.Log("[AnchorSystem] *** Anchor CANCELLED ***");
        }

        // --- Voice trigger ---
        if (gripRising)
        {
            Debug.Log($"[AnchorSystem] Grip pressed. HasAnchor={HasAnchor}");
            if (HasAnchor)
            {
                Debug.Log($"[AnchorSystem] *** OnVoiceTrigger FIRED. Anchor={AnchorWorldPosition} ***");
                OnVoiceTrigger?.Invoke(AnchorWorldPosition);
            }
            else
            {
                Debug.LogWarning("[AnchorSystem] Grip pressed but no anchor set. Place one first.");
            }
        }

        // --- Store previous state ---
        _triggerWasPressed = triggerPressed;
        _gripWasPressed    = gripPressed;
        _bWasPressed       = bPressed;
    }

    // -------------------------------------------------------------------------
    // Device discovery (retries every 2 s until found)
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
            Debug.Log($"[AnchorSystem] Right controller found: '{_rightController.name}' " +
                      $"| manufacturer: '{_rightController.manufacturer}'");
        }
        else
        {
            Debug.LogWarning("[AnchorSystem] Right controller not found. Retrying in 2s... " +
                             "(Is XR initialized? Is the headset awake?)");
        }
    }

    // -------------------------------------------------------------------------
    // Procedural mesh helpers
    // -------------------------------------------------------------------------

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