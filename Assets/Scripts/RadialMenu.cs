using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

/// <summary>
/// True radial menu driven by the LEFT joystick.
///
///   X (left primaryButton)  → open / close
///   Left joystick           → point at item to highlight (angle-based sectors)
///   Release stick to centre → confirm highlighted item  (flick-and-release)
///
/// Canvas + all UI elements are created procedurally — no scene setup needed.
/// </summary>
public class RadialMenu : MonoBehaviour
{
    [Header("Positioning")]
    [Tooltip("Distance in front of camera when the menu opens.")]
    public float menuDistance = 0.6f;

    [Header("Timing")]
    [Tooltip("Seconds after opening before a joystick release can confirm a selection. " +
             "Prevents instant accidental confirm when X is pressed with stick already tilted.")]
    public float graceSeconds = 0.6f;

    [Header("Locomotion to pause while menu is open")]
    [Tooltip("Drag here any MonoBehaviour that moves the player (e.g. OVRPlayerController, " +
             "CharacterController wrapper, your own LocomotionController). " +
             "They will be disabled on open and re-enabled on close.")]
    public MonoBehaviour[] locomotionToBlock;

    // -------------------------------------------------------------------------
    // Layout constants (canvas units, canvas = 400×400 at scale 0.001)
    // -------------------------------------------------------------------------
    private const float ItemRadius    = 140f;   // distance from centre to item
    private const float IndicatorMax  = 75f;    // max travel radius of the joystick dot
    private const float SelectThresh  = 0.35f;  // stick magnitude → start highlighting
    private const float ConfirmThresh = 0.15f;  // stick magnitude < this → confirm

    // Items: label + angle (degrees, standard trig convention — 0° = right, 90° = up)
    // 4 items at 90° intervals
    private static readonly string[] Labels = { "Editing Mode", "Home", "Import JSON", "Quit" };
    private static readonly float[]  Angles = {  90f,        0f,     180f,          270f  };
    // 90°=top  0°=right  180°=left  270°=bottom

    // -------------------------------------------------------------------------
    // Runtime UI handles
    // -------------------------------------------------------------------------
    private Canvas          _canvas;
    private RectTransform   _joystickDot;
    private RectTransform   _indicatorLine;
    private Image           _indicatorLineImg;
    private TextMeshProUGUI _centreLabel;
    private readonly Image[]           _itemBGs    = new Image[4];
    private readonly TextMeshProUGUI[] _itemLabels = new TextMeshProUGUI[4];

    // -------------------------------------------------------------------------
    // Colors
    // -------------------------------------------------------------------------
    private static readonly Color ColBG        = new Color(0.04f, 0.05f, 0.12f, 0.93f);
    private static readonly Color ColRing      = new Color(0.20f, 0.30f, 0.55f, 0.40f);
    private static readonly Color ColNormal    = new Color(0.12f, 0.22f, 0.42f, 1.00f);
    private static readonly Color ColHighlight = new Color(0.25f, 0.55f, 1.00f, 1.00f);
    private static readonly Color ColLineIdle  = new Color(1.00f, 1.00f, 1.00f, 0.30f);
    private static readonly Color ColLineActive= new Color(0.40f, 0.75f, 1.00f, 0.90f);

    // -------------------------------------------------------------------------
    // State
    // -------------------------------------------------------------------------

    /// <summary>
    /// True while the radial menu is visible. Read directly by LocomotionController
    /// to block movement — does not require AppStateManager in the scene.
    /// </summary>
    public static bool IsOpen { get; private set; }

    private bool  _isOpen;
    private int   _highlighted  = -1;   // -1 = nothing highlighted
    private float _menuOpenTime = -999f; // time when menu last opened (for grace period)

    // -------------------------------------------------------------------------
    // Input
    // -------------------------------------------------------------------------
    private InputDevice _left;
    private bool _leftFound;
    private float _nextSearch;
    private bool  _xWasPressed;

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        BuildProceduralMenu();
        _canvas.gameObject.SetActive(false);
    }

    private void Start()
    {
        TMP_FontAsset f = NotificationManager.GlobalFont;
        if (f == null) return;
        if (_centreLabel != null) _centreLabel.font = f;
        for (int i = 0; i < _itemLabels.Length; i++)
            if (_itemLabels[i] != null) _itemLabels[i].font = f;
    }

    private void Update()
    {
        TryFindLeft();
        if (!_leftFound) return;

        // X button: toggle open/close
        _left.TryGetFeatureValue(CommonUsages.primaryButton, out bool xNow);
        if (xNow && !_xWasPressed) ToggleMenu();
        _xWasPressed = xNow;

        if (!_isOpen) return;

        // Left joystick drives radial selection
        _left.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 stick);
        HandleJoystick(stick);
    }

    // -------------------------------------------------------------------------
    // Joystick logic
    // -------------------------------------------------------------------------

    private void HandleJoystick(Vector2 stick)
    {
        float mag = stick.magnitude;

        // --- Highlight whichever sector the stick points into ---
        if (mag >= SelectThresh)
        {
            float angle = Mathf.Atan2(stick.y, stick.x) * Mathf.Rad2Deg;
            _highlighted = ClosestItem(angle);
        }

        // --- Stick snapped back to centre → confirm (only after grace period) ---
        bool graceOver = (Time.time - _menuOpenTime) >= graceSeconds;
        if (graceOver && mag < ConfirmThresh && _highlighted >= 0)
        {
            int chosen = _highlighted;
            _highlighted = -1;
            CloseMenu();
            ExecuteItem(chosen);
            return;
        }

        // --- Clear highlight if stick between the two thresholds (hysteresis) ---
        if (mag < SelectThresh && mag >= ConfirmThresh)
        {
            // leave _highlighted as-is so there's no flicker when moving between sectors
        }

        UpdateVisuals(stick, mag);
    }

    // -------------------------------------------------------------------------
    // Visuals
    // -------------------------------------------------------------------------

    private void UpdateVisuals(Vector2 stick, float mag)
    {
        // Joystick dot moves with the stick
        _joystickDot.anchoredPosition = new Vector2(
            Mathf.Clamp(stick.x, -1f, 1f) * IndicatorMax,
            Mathf.Clamp(stick.y, -1f, 1f) * IndicatorMax);

        // Indicator line extends from centre toward joystick direction
        if (mag > 0.05f)
        {
            float lineAngle = Mathf.Atan2(stick.y, stick.x) * Mathf.Rad2Deg;
            _indicatorLine.localRotation = Quaternion.Euler(0f, 0f, lineAngle);
            _indicatorLine.sizeDelta     = new Vector2(Mathf.Clamp01(mag) * IndicatorMax, 3f);
            _indicatorLine.gameObject.SetActive(true);
            _indicatorLineImg.color = _highlighted >= 0 ? ColLineActive : ColLineIdle;
        }
        else
        {
            _indicatorLine.gameObject.SetActive(false);
        }

        // Item panels: colour + smooth scale
        for (int i = 0; i < 4; i++)
        {
            bool active = (i == _highlighted);
            
            // 1. 先按原本的逻辑确定默认颜色
            Color targetColor = active ? ColHighlight : ColNormal;

            // 2. --- NEW EDITING MODE OVERRIDE ---
            // 如果是第0个按钮(Editing Mode)，并且编辑模式已激活
            if (i == 0 && AppStateManager.IsEditingModeActive)
            {
                // 没指着它时用深正红色，指着它（active）时用极具冲击力的亮红色
                targetColor = active ? new Color(1.0f, 0.2f, 0.2f, 0.95f) : new Color(0.75f, 0.1f, 0.1f, 0.9f);
            }
            // ------------------------------------

            // 3. 赋值颜色
            _itemBGs[i].color = targetColor;

            // 4. 保持原本的平滑缩放动画不变
            float targetScale = active ? 1.10f : 1f;
            _itemBGs[i].transform.localScale = Vector3.Lerp(
                _itemBGs[i].transform.localScale,
                Vector3.one * targetScale,
                Time.deltaTime * 14f);
        }

        // Centre label shows highlighted item name (or bullet when idle)
        _centreLabel.text = _highlighted >= 0 ? Labels[_highlighted] : "·";
    }

    private void ResetVisuals()
    {
        _joystickDot.anchoredPosition = Vector2.zero;
        _indicatorLine.gameObject.SetActive(false);
        for (int i = 0; i < 4; i++)
        {
            _itemBGs[i].color             = ColNormal;
            _itemBGs[i].transform.localScale = Vector3.one;
        }
        _centreLabel.text = "·";
    }

    // -------------------------------------------------------------------------
    // Open / close
    // -------------------------------------------------------------------------

    private void ToggleMenu()
    {
        _isOpen = !_isOpen;
        IsOpen  = _isOpen;
        _canvas.gameObject.SetActive(_isOpen);

        if (_isOpen)
        {
            _menuOpenTime = Time.time;          // start grace period clock

            Transform cam = Camera.main ? Camera.main.transform : transform;
            _canvas.transform.position = cam.position + cam.forward * menuDistance;
            _canvas.transform.rotation = Quaternion.LookRotation(cam.forward);
            _highlighted = -1;
            ResetVisuals();

            SetLocomotionEnabled(false);        // freeze player movement
        }
        else
        {
            SetLocomotionEnabled(true);
        }

        AppStateManager.Instance?.SetState(_isOpen ? AppState.MenuOpen : AppState.Idle);
    }

    public void CloseMenu()
    {
        _isOpen      = false;
        IsOpen       = false;
        _highlighted = -1;
        _canvas.gameObject.SetActive(false);
        SetLocomotionEnabled(true);             // unfreeze player movement
        AppStateManager.Instance?.SetState(AppState.Idle);
    }

    private void SetLocomotionEnabled(bool enabled)
    {
        if (locomotionToBlock == null) return;
        foreach (MonoBehaviour mb in locomotionToBlock)
            if (mb != null) mb.enabled = enabled;
    }

    // -------------------------------------------------------------------------
    // Actions
    // -------------------------------------------------------------------------

    private void ExecuteItem(int index)
    {
        switch (index)
        {
            case 0:
                AppStateManager.IsEditingModeActive = !AppStateManager.IsEditingModeActive;

                if (AppStateManager.IsEditingModeActive)
                {
                    Debug.Log("<color=cyan>[RadialMenu] Editing Mode Enabled.</color>");
                    NotificationManager.Instance?.ShowStatus("Editing Mode: Select a room with Trigger.");
                }
                else
                {
                    Debug.Log("<color=cyan>[RadialMenu] Editing Mode Disabled.</color>");
                    NotificationManager.Instance?.ShowStatus("Exited Editing Mode.");
                }

                // Restore state to Idle or let AppStateManager handle it, matching previous Tutorial logic
                AppStateManager.Instance?.SetState(AppState.Idle);
                
                return;
            case 1:
                StartCoroutine(FadeAndReset());
                break;
            case 2: JsonImportExport.Instance?.ImportLatest(); break;
            case 3:
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
                break;
        }
    }

    private System.Collections.IEnumerator FadeAndReset()
    {
        if (NotificationManager.Instance != null)
            yield return NotificationManager.Instance.FadeToBlack(0.45f);

        GroundAnchorManager.Instance?.ResetAnchor();
        MuseumDescriptionPanel.Instance?.ClearAll();
        VoiceCommandManager.Instance?.ClearTranscriptions();
        OptionSelectionMenu.ClearIterationBase();
        NotificationManager.Instance?.ReturnToSplash();

        yield return new WaitForSeconds(0.15f);

        if (NotificationManager.Instance != null)
            yield return NotificationManager.Instance.FadeFromBlack(0.6f);
    }

    // -------------------------------------------------------------------------
    // Utility
    // -------------------------------------------------------------------------

    private static int ClosestItem(float angle)
    {
        int   best     = 0;
        float bestDist = float.MaxValue;
        for (int i = 0; i < Angles.Length; i++)
        {
            float d = Mathf.Abs(Mathf.DeltaAngle(angle, Angles[i]));
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }

    private void TryFindLeft()
    {
        if (_leftFound) return;
        if (Time.time < _nextSearch) return;
        _nextSearch = Time.time + 2f;

        var list = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller, list);
        if (list.Count > 0) { _left = list[0]; _leftFound = true; }
    }

    // -------------------------------------------------------------------------
    // Procedural Canvas construction
    // -------------------------------------------------------------------------

    private void BuildProceduralMenu()
    {
        // Root canvas
        GameObject canvasGO = new GameObject("RadialMenuCanvas");
        canvasGO.transform.SetParent(transform);
        _canvas = canvasGO.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();

        RectTransform crt = canvasGO.GetComponent<RectTransform>();
        crt.sizeDelta  = new Vector2(400f, 400f);
        crt.localScale = Vector3.one * 0.001f;
        Transform cv = canvasGO.transform;

        // Dark background
        MakeStretchImage(cv, "BG", ColBG);

        // Decorative outer ring (slightly lighter, thin border feel)
        MakeCircleRing(cv, "Ring", ItemRadius + 40f, ColRing);

        // Sector dividers (5 thin lines at 36° from each item angle)
        for (int i = 0; i < 4; i++)
        {
            float divAngle = Angles[i] - 36f;   // sector boundary between items
            MakeSectorLine(cv, $"Divider{i}", divAngle);
        }

        // Indicator line (pivot = left edge → extends from centre outward)
        GameObject lineGO = new GameObject("IndicatorLine");
        lineGO.transform.SetParent(cv, false);
        _indicatorLineImg = lineGO.AddComponent<Image>();
        _indicatorLineImg.color = ColLineIdle;
        _indicatorLine = lineGO.GetComponent<RectTransform>();
        _indicatorLine.pivot           = new Vector2(0f, 0.5f);
        _indicatorLine.anchoredPosition = Vector2.zero;
        _indicatorLine.sizeDelta       = new Vector2(IndicatorMax, 3f);

        // Joystick dot (centre, moves with stick)
        GameObject dotGO = new GameObject("JoyDot");
        dotGO.transform.SetParent(cv, false);
        dotGO.AddComponent<Image>().color = Color.white;
        _joystickDot = dotGO.GetComponent<RectTransform>();
        _joystickDot.sizeDelta = new Vector2(16f, 16f);

        // Centre origin dot (static)
        GameObject originGO = new GameObject("Origin");
        originGO.transform.SetParent(cv, false);
        originGO.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.5f);
        RectTransform originRt = originGO.GetComponent<RectTransform>();
        originRt.sizeDelta = new Vector2(8f, 8f);

        // Centre label (shows highlighted item name)
        GameObject clGO = new GameObject("CentreLabel");
        clGO.transform.SetParent(cv, false);
        _centreLabel = clGO.AddComponent<TextMeshProUGUI>();
        _centreLabel.text      = "·";
        _centreLabel.alignment = TextAlignmentOptions.Center;
        _centreLabel.fontSize  = 19f;
        _centreLabel.color     = new Color(1f, 1f, 1f, 0.65f);
        clGO.GetComponent<RectTransform>().sizeDelta       = new Vector2(150f, 36f);
        clGO.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -26f);

        // Radial item panels
        for (int i = 0; i < 4; i++)
        {
            float  rad = Angles[i] * Mathf.Deg2Rad;
            Vector2 pos = new Vector2(Mathf.Cos(rad) * ItemRadius,
                                      Mathf.Sin(rad) * ItemRadius);

            GameObject itemGO = new GameObject($"Item{i}");
            itemGO.transform.SetParent(cv, false);
            _itemBGs[i] = itemGO.AddComponent<Image>();
            _itemBGs[i].color = ColNormal;
            RectTransform irt = itemGO.GetComponent<RectTransform>();
            irt.sizeDelta       = new Vector2(130f, 52f);
            irt.anchoredPosition = pos;

            // Icon dot (small colour square on left of label)
            GameObject iconGO = new GameObject("Icon");
            iconGO.transform.SetParent(itemGO.transform, false);
            iconGO.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.25f);
            RectTransform iconRt = iconGO.GetComponent<RectTransform>();
            iconRt.sizeDelta        = new Vector2(8f, 8f);
            iconRt.anchoredPosition = new Vector2(-52f, 0f);

            // Label
            GameObject txtGO = new GameObject("Label");
            txtGO.transform.SetParent(itemGO.transform, false);
            _itemLabels[i] = txtGO.AddComponent<TextMeshProUGUI>();
            _itemLabels[i].text      = Labels[i];
            _itemLabels[i].alignment = TextAlignmentOptions.Center;
            _itemLabels[i].fontSize  = 17f;
            _itemLabels[i].color     = Color.white;
            RectTransform trt = txtGO.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
        }
    }

    // Thin line from centre in a given direction (sector divider)
    private static void MakeSectorLine(Transform parent, string name, float angleDeg)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.08f);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.pivot            = new Vector2(0f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta        = new Vector2(ItemRadius + 30f, 1f);
        rt.localRotation    = Quaternion.Euler(0f, 0f, angleDeg);
    }

    // Thin ring around the items (using a square that approximates a circle border)
    private static void MakeCircleRing(Transform parent, string name, float radius, Color color)
    {
        // Approximate ring with 4 thin lines (N/S/E/W)
        float side = radius * 2f;
        float thickness = 1.5f;
        Vector2[] positions = { new Vector2(0, radius), new Vector2(0, -radius),
                                 new Vector2(radius, 0), new Vector2(-radius, 0) };
        Vector2[] sizes     = { new Vector2(side, thickness), new Vector2(side, thickness),
                                 new Vector2(thickness, side), new Vector2(thickness, side) };
        for (int i = 0; i < 4; i++)
        {
            GameObject go = new GameObject($"{name}_{i}");
            go.transform.SetParent(parent, false);
            go.AddComponent<Image>().color = color;
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.sizeDelta        = sizes[i];
            rt.anchoredPosition = positions[i];
        }
    }

    private static void MakeStretchImage(Transform parent, string name, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<Image>().color = color;
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}
