using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

public class HintDisplay : MonoBehaviour
{
    [Header("Positioning")]
    [Tooltip("World-space offset from the left controller where the card appears.")]
    public Vector3 offsetFromController = new Vector3(0f, 0.14f, 0.04f);

    // -------------------------------------------------------------------------
    private bool _hintsEnabled;
    private InputDevice _leftDevice;
    private bool _leftFound;
    private float _nextSearch;
    private bool _leftGripWasPressed;

    private Transform _leftControllerTransform; // scene GameObject anchor if found

    private GameObject _hintPanel;
    private TextMeshProUGUI _hintText;
    private TextMeshProUGUI _headerText;

    // -------------------------------------------------------------------------
    // Phase-based hint content
    // -------------------------------------------------------------------------

    private static string HintHeader(AppPhase phase)
    {
        switch (phase)
        {
            case AppPhase.PositionSelection: return "Position Selection";
            case AppPhase.Design:           return "Design";
            case AppPhase.Generation:       return "Generation";
            case AppPhase.Visualization:    return "Visualization";
            default:                        return "";
        }
    }

    private static string HintBody(AppPhase phase)
    {
        switch (phase)
        {
            case AppPhase.PositionSelection:
                return "<b>Right Trigger</b>  →  place museum anchor\n" +
                       "<b>B</b>  →  cancel selected position";

            case AppPhase.Design:
                return "<b>Hold Right Grip</b>  →  record description\n" +
                       "<b>Release Grip</b>  →  stop & transcribe\n" +
                       "<b>Y</b>  →  confirm summary & generate\n" +
                       "<b>B</b>  →  back to position selection\n\n" +
                       "<size=82%><color=#7aaad4>e.g. \"A bright central hall, a quiet café\nto the left, three sensory rooms around\"</color></size>";

            case AppPhase.Generation:
                return "AI is generating massing options…\nPlease wait.";

            case AppPhase.Visualization:
                return "<b>Left Trigger</b>  →  select Option A\n" +
                       "<b>Right Trigger</b>  →  select Option B\n" +
                       "<b>Tilt Right Stick + release</b>  →  choose action\n" +
                       "   Visualize  /  Iterate  /  Cancel";

            default:
                return "";
        }
    }

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        BuildProceduralHint();
        _hintPanel.SetActive(false);
    }

    private void Start()
    {
        // Apply global font
        TMP_FontAsset f = NotificationManager.GlobalFont;
        if (f != null)
        {
            if (_hintText   != null) _hintText.font   = f;
            if (_headerText != null) _headerText.font = f;
        }

        // Try to find left controller anchor in scene hierarchy
        GameObject found = GameObject.Find("Left Controller")
                        ?? GameObject.Find("LeftHand Controller")
                        ?? GameObject.Find("LeftControllerAnchor")
                        ?? GameObject.Find("LeftHandAnchor");
        if (found != null)
        {
            _leftControllerTransform = found.transform;
            Debug.Log($"[HintDisplay] Left controller anchor auto-set to '{found.name}'.");
        }
    }

    private void Update()
    {
        TryFindLeftDevice();
        if (!_leftFound) return;

        // Suppress during splash
        if (AppStateManager.Instance != null &&
            AppStateManager.Instance.CurrentPhase == AppPhase.None)
        {
            if (_hintsEnabled) SetHintsActive(false);
            return;
        }

        // Toggle on left grip press (rising edge) — no longer conflicts with option selection
        _leftDevice.TryGetFeatureValue(CommonUsages.gripButton, out bool gripNow);
        if (gripNow && !_leftGripWasPressed) Toggle();
        _leftGripWasPressed = gripNow;

        if (!_hintsEnabled || _hintPanel == null) return;

        // Stick to left controller
        FollowController();

        // Update text for current phase
        RefreshText();
    }

    // -------------------------------------------------------------------------

    private void Toggle()
    {
        SetHintsActive(!_hintsEnabled);
    }

    private void SetHintsActive(bool active)
    {
        _hintsEnabled = active;
        if (_hintPanel) _hintPanel.SetActive(active);
    }

    private void FollowController()
    {
        Vector3 worldPos;

        if (_leftControllerTransform != null)
        {
            // Use scene transform — most accurate
            worldPos = _leftControllerTransform.TransformPoint(offsetFromController);
        }
        else if (_leftDevice.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 devPos) &&
                 _leftDevice.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion devRot))
        {
            // Fallback: XR device pose
            worldPos = devPos + devRot * offsetFromController;
        }
        else
        {
            return;
        }

        _hintPanel.transform.position = worldPos;

        // Always face the camera
        if (Camera.main != null)
        {
            _hintPanel.transform.LookAt(Camera.main.transform);
            _hintPanel.transform.Rotate(0f, 180f, 0f);
        }
    }

    private void RefreshText()
    {
        if (_hintText == null || AppStateManager.Instance == null) return;

        AppPhase phase = AppStateManager.Instance.CurrentPhase;
        if (_headerText != null) _headerText.text = HintHeader(phase);
        _hintText.text = HintBody(phase);
    }

    // -------------------------------------------------------------------------
    // Device discovery
    // -------------------------------------------------------------------------

    private void TryFindLeftDevice()
    {
        if (_leftFound) return;
        if (Time.time < _nextSearch) return;
        _nextSearch = Time.time + 2f;

        var list = new System.Collections.Generic.List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller, list);
        if (list.Count > 0) { _leftDevice = list[0]; _leftFound = true; }
    }

    // -------------------------------------------------------------------------
    // Procedural canvas
    // -------------------------------------------------------------------------

    private void BuildProceduralHint()
    {
        // Canvas — 420 × 220, scale 0.001 → ~42 cm × 22 cm in world space
        GameObject canvasGO = new GameObject("HintCanvas");
        canvasGO.transform.SetParent(transform);
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvasGO.AddComponent<CanvasScaler>();

        RectTransform crt = canvasGO.GetComponent<RectTransform>();
        crt.sizeDelta  = new Vector2(420f, 230f);
        crt.localScale = Vector3.one * 0.001f;

        // Dark background
        GameObject bg = new GameObject("BG");
        bg.transform.SetParent(canvasGO.transform, false);
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0.04f, 0.06f, 0.12f, 0.88f);
        RectTransform bgRt = bg.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;

        // Thin top accent line
        GameObject accent = new GameObject("Accent");
        accent.transform.SetParent(canvasGO.transform, false);
        Image accentImg = accent.AddComponent<Image>();
        accentImg.color = new Color(0.4f, 0.75f, 1f, 0.9f);
        RectTransform acRt = accent.GetComponent<RectTransform>();
        acRt.anchorMin = new Vector2(0f, 1f);
        acRt.anchorMax = new Vector2(1f, 1f);
        acRt.pivot     = new Vector2(0.5f, 1f);
        acRt.sizeDelta = new Vector2(0f, 3f);
        acRt.anchoredPosition = Vector2.zero;

        // Header — phase name
        GameObject headerGO = new GameObject("Header");
        headerGO.transform.SetParent(canvasGO.transform, false);
        _headerText = headerGO.AddComponent<TextMeshProUGUI>();
        _headerText.alignment  = TextAlignmentOptions.Center;
        _headerText.fontSize   = 18f;
        _headerText.fontStyle  = FontStyles.Bold;
        _headerText.color      = new Color(0.5f, 0.85f, 1f);
        _headerText.text       = "";
        RectTransform hRt = headerGO.GetComponent<RectTransform>();
        hRt.sizeDelta        = new Vector2(400f, 36f);
        hRt.anchoredPosition = new Vector2(0f, 95f);

        // Divider line under header
        GameObject divider = new GameObject("Divider");
        divider.transform.SetParent(canvasGO.transform, false);
        Image divImg = divider.AddComponent<Image>();
        divImg.color = new Color(1f, 1f, 1f, 0.12f);
        RectTransform dvRt = divider.GetComponent<RectTransform>();
        dvRt.sizeDelta        = new Vector2(380f, 1f);
        dvRt.anchoredPosition = new Vector2(0f, 74f);

        // Body text — hint lines
        GameObject textGO = new GameObject("HintText");
        textGO.transform.SetParent(canvasGO.transform, false);
        _hintText = textGO.AddComponent<TextMeshProUGUI>();
        _hintText.alignment  = TextAlignmentOptions.Center;
        _hintText.fontSize   = 15f;
        _hintText.color      = new Color(0.88f, 0.92f, 1f);
        _hintText.lineSpacing = 8f;
        _hintText.text        = "";
        RectTransform tRt = textGO.GetComponent<RectTransform>();
        tRt.sizeDelta        = new Vector2(400f, 160f);
        tRt.anchoredPosition = new Vector2(0f, -18f);

        _hintPanel = canvasGO;
    }
}
