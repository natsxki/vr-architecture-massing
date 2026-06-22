using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

/// <summary>
/// 4-item radial menu that appears when the user grips a controller to select a
/// generated massing option (left grip = A, right grip = B) in ViewingOptions state.
/// Navigation: right thumbstick flick + release back to centre to confirm.
/// Items: Visualize (top) / Save JSON (right) / Iterate (bottom) / Cancel (left)
/// </summary>
public class OptionSelectionMenu : MonoBehaviour
{
    public static OptionSelectionMenu Instance { get; private set; }
    public static bool IsOpen { get; private set; }

    public static string IterationBaseJson { get; private set; }
    public static void ClearIterationBase() => IterationBaseJson = null;

    [Header("Placement")]
    public float menuDistance  = 0.65f;
    public float graceSeconds  = 0.5f;

    // ── Layout constants (400×400 canvas @ 0.001 scale = 0.4 m²) ─────────────
    private const float ItemRadius    = 130f;
    private const float IndicatorMax  = 70f;
    private const float SelectThresh  = 0.35f;
    private const float ConfirmThresh = 0.15f;

    private static readonly string[] Labels = { "Visualize", "Save JSON", "Iterate", "Cancel" };
    private static readonly float[]  Angles = { 90f,          0f,          270f,      180f    };

    private static readonly Color ColBG        = new Color(0.04f, 0.05f, 0.12f, 0.93f);
    private static readonly Color ColNormal    = new Color(0.12f, 0.22f, 0.42f, 1.00f);
    private static readonly Color ColHighlight = new Color(0.25f, 0.55f, 1.00f, 1.00f);
    private static readonly Color ColLineIdle  = new Color(1.00f, 1.00f, 1.00f, 0.30f);
    private static readonly Color ColLineActive= new Color(0.40f, 0.75f, 1.00f, 0.90f);

    // ── Runtime state ─────────────────────────────────────────────────────────
    private Canvas            _canvas;
    private RectTransform     _joystickDot;
    private RectTransform     _indicatorLine;
    private Image             _indicatorLineImg;
    private TextMeshProUGUI   _centreLabel;
    private readonly Image[]           _itemBGs    = new Image[4];
    private readonly TextMeshProUGUI[] _itemLabels = new TextMeshProUGUI[4];

    private bool         _isOpen;
    private int          _highlighted  = -1;
    private float        _menuOpenTime = -999f;
    private MassingOption _selectedOption;
    private int           _selectedIndex;

    private InputDevice _rightCtrl;
    private bool        _rightFound;
    private float       _nextSearchTime;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (Instance != null) return;
        var go = new GameObject("OptionSelectionMenu");
        go.AddComponent<OptionSelectionMenu>();
        Debug.Log("[OptionMenu] Auto-created OptionSelectionMenu singleton.");
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        BuildUI();
        _canvas.gameObject.SetActive(false);
    }

    private void Start()
    {
        if (GroundAnchorManager.Instance != null)
        {
            GroundAnchorManager.Instance.OnOptionASelected += () => Open(0);
            GroundAnchorManager.Instance.OnOptionBSelected += () => Open(1);
            Debug.Log("[OptionMenu] Subscribed to GroundAnchorManager option events.");
        }
        else
        {
            Debug.LogError("[OptionMenu] GroundAnchorManager.Instance is NULL at Start — subscription skipped! " +
                           "Make sure GroundAnchorManager is in the scene and enabled.");
        }
    }

    private void Update()
    {
        if (!_isOpen) return;

        // Keep menu centred in front of camera each frame
        if (Camera.main != null)
        {
            Transform cam = Camera.main.transform;
            _canvas.transform.position = cam.position + cam.forward * menuDistance;
            _canvas.transform.rotation = Quaternion.LookRotation(cam.forward);
        }

        TryFindRightCtrl();
        if (!_rightFound) return;

        _rightCtrl.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 stick);
        HandleJoystick(stick);
    }

    // ── Open / Close ─────────────────────────────────────────────────────────

    public void Open(int optionIndex)
    {
        Debug.Log($"[OptionMenu] Open({optionIndex}) called. _isOpen={_isOpen}");
        if (_isOpen) return;

        _selectedIndex  = optionIndex;
        _selectedOption = optionIndex == 0 ? MassingGenerator.LastOption1 : MassingGenerator.LastOption2;
        if (_selectedOption == null)
        {
            Debug.LogWarning($"[OptionMenu] LastOption{optionIndex + 1} is null — generate massings first.");
            return;
        }

        _highlighted  = -1;
        _menuOpenTime = Time.time;
        _isOpen       = true;
        IsOpen        = true;

        _centreLabel.text = optionIndex == 0 ? "Option A" : "Option B";
        ResetVisuals();

        _canvas.gameObject.SetActive(true);
        AppStateManager.Instance?.SetState(AppState.MenuOpen);
        NotificationManager.Instance?.ShowStatus("Tilt right stick to choose, release to confirm");
    }

    public void Close()
    {
        _isOpen      = false;
        IsOpen       = false;
        _highlighted = -1;
        _canvas.gameObject.SetActive(false);
        NotificationManager.Instance?.ClearStatus();
        AppStateManager.Instance?.SetState(AppState.ViewingOptions);
    }

    // ── Joystick navigation ───────────────────────────────────────────────────

    private void HandleJoystick(Vector2 stick)
    {
        float mag = stick.magnitude;

        if (mag >= SelectThresh)
            _highlighted = ClosestItem(Mathf.Atan2(stick.y, stick.x) * Mathf.Rad2Deg);

        bool graceOver = (Time.time - _menuOpenTime) >= graceSeconds;
        if (graceOver && mag < ConfirmThresh && _highlighted >= 0)
        {
            int chosen = _highlighted;
            _highlighted = -1;
            Close();
            Execute(chosen);
            return;
        }

        UpdateVisuals(stick, mag);
    }

    // ── Actions ───────────────────────────────────────────────────────────────

    private void Execute(int index)
    {
        switch (index)
        {
            case 0: DoVisualize(); break;
            case 1: DoSaveJSON();  break;
            case 2: DoIterate();   break;
            case 3: /* Cancel — menu already closed */ break;
        }
    }

    private void DoVisualize()
    {
        if (MassingGenerator.Instance == null) { Debug.LogError("[OptionMenu] MassingGenerator.Instance is null."); return; }
        MassingGenerator.Instance.VisualizeAtRealScale(_selectedIndex);
        NotificationManager.Instance?.ShowStatus("Real-scale view — press B to return to options");
    }

    private void DoSaveJSON()
    {
        if (_selectedOption == null) return;
        string json     = JsonConvert.SerializeObject(_selectedOption, Formatting.Indented);
        string filename = $"option_{(char)('A' + _selectedIndex)}_{System.DateTime.Now:yyyyMMdd_HHmmss}.json";
        string path     = Path.Combine(Application.persistentDataPath, filename);
        File.WriteAllText(path, json);
        NotificationManager.Instance?.ShowStatus($"Saved to: {filename}");
        Debug.Log($"[OptionMenu] JSON saved to {path}");
    }

    private void DoIterate()
    {
        if (_selectedOption == null) return;
        IterationBaseJson = JsonConvert.SerializeObject(_selectedOption);
        AppStateManager.Instance?.SetState(AppState.ReviewingTranscription);
        NotificationManager.Instance?.ShowStatus("Iteration mode — describe refinements, then press Y");
        Debug.Log("[OptionMenu] Iteration mode started. Base JSON stored.");
    }

    // ── Visuals ───────────────────────────────────────────────────────────────

    private void UpdateVisuals(Vector2 stick, float mag)
    {
        _joystickDot.anchoredPosition = new Vector2(
            Mathf.Clamp(stick.x, -1f, 1f) * IndicatorMax,
            Mathf.Clamp(stick.y, -1f, 1f) * IndicatorMax);

        if (mag > 0.05f)
        {
            float angle = Mathf.Atan2(stick.y, stick.x) * Mathf.Rad2Deg;
            _indicatorLine.localRotation = Quaternion.Euler(0f, 0f, angle);
            _indicatorLine.sizeDelta     = new Vector2(Mathf.Clamp01(mag) * IndicatorMax, 3f);
            _indicatorLine.gameObject.SetActive(true);
            _indicatorLineImg.color = _highlighted >= 0 ? ColLineActive : ColLineIdle;
        }
        else
        {
            _indicatorLine.gameObject.SetActive(false);
        }

        for (int i = 0; i < 4; i++)
        {
            bool active = (i == _highlighted);
            _itemBGs[i].color = active ? ColHighlight : ColNormal;
            float target = active ? 1.10f : 1f;
            _itemBGs[i].transform.localScale = Vector3.Lerp(
                _itemBGs[i].transform.localScale, Vector3.one * target, Time.deltaTime * 14f);
        }
    }

    private void ResetVisuals()
    {
        _joystickDot.anchoredPosition = Vector2.zero;
        _indicatorLine.gameObject.SetActive(false);
        for (int i = 0; i < 4; i++)
        {
            _itemBGs[i].color = ColNormal;
            _itemBGs[i].transform.localScale = Vector3.one;
        }
    }

    // ── UI construction ───────────────────────────────────────────────────────

    private void BuildUI()
    {
        GameObject canvasGO = new GameObject("OptionSelectionMenuCanvas");
        canvasGO.transform.SetParent(transform, false);
        _canvas = canvasGO.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        canvasGO.AddComponent<CanvasScaler>();

        RectTransform crt = canvasGO.GetComponent<RectTransform>();
        crt.sizeDelta  = new Vector2(400f, 400f);
        crt.localScale = Vector3.one * 0.001f;

        Transform cv = canvasGO.transform;

        // Background panel
        AddStretch(cv, "BG", ColBG);

        // Joystick direction line (pivot at left edge, rotates to point toward selection)
        GameObject lineGO = new GameObject("Line");
        lineGO.transform.SetParent(cv, false);
        _indicatorLineImg = lineGO.AddComponent<Image>();
        _indicatorLineImg.color = ColLineIdle;
        _indicatorLine = lineGO.GetComponent<RectTransform>();
        _indicatorLine.pivot            = new Vector2(0f, 0.5f);
        _indicatorLine.anchoredPosition = Vector2.zero;
        _indicatorLine.sizeDelta        = new Vector2(IndicatorMax, 3f);

        // Joystick position dot
        GameObject dotGO = new GameObject("Dot");
        dotGO.transform.SetParent(cv, false);
        dotGO.AddComponent<Image>().color = Color.white;
        _joystickDot = dotGO.GetComponent<RectTransform>();
        _joystickDot.sizeDelta = new Vector2(16f, 16f);

        // Centre label (shows selected option name)
        GameObject clGO = new GameObject("CentreLabel");
        clGO.transform.SetParent(cv, false);
        _centreLabel = clGO.AddComponent<TextMeshProUGUI>();
        _centreLabel.text      = "·";
        _centreLabel.alignment = TextAlignmentOptions.Center;
        _centreLabel.fontSize  = 22f;
        _centreLabel.color     = new Color(1f, 1f, 1f, 0.8f);
        RectTransform clRT = clGO.GetComponent<RectTransform>();
        clRT.sizeDelta        = new Vector2(180f, 40f);
        clRT.anchoredPosition = Vector2.zero;

        // Sub-hint below centre label
        GameObject hintGO = new GameObject("Hint");
        hintGO.transform.SetParent(cv, false);
        var hint = hintGO.AddComponent<TextMeshProUGUI>();
        hint.text      = "tilt stick · release to confirm";
        hint.alignment = TextAlignmentOptions.Center;
        hint.fontSize  = 12f;
        hint.color     = new Color(1f, 1f, 1f, 0.45f);
        RectTransform hRT = hintGO.GetComponent<RectTransform>();
        hRT.sizeDelta        = new Vector2(180f, 24f);
        hRT.anchoredPosition = new Vector2(0f, -26f);

        // 4 item panels at cardinal directions
        for (int i = 0; i < 4; i++)
        {
            float   rad = Angles[i] * Mathf.Deg2Rad;
            Vector2 pos = new Vector2(Mathf.Cos(rad) * ItemRadius, Mathf.Sin(rad) * ItemRadius);

            GameObject itemGO = new GameObject($"Item_{Labels[i]}");
            itemGO.transform.SetParent(cv, false);
            _itemBGs[i] = itemGO.AddComponent<Image>();
            _itemBGs[i].color = ColNormal;
            RectTransform irt = itemGO.GetComponent<RectTransform>();
            irt.sizeDelta        = new Vector2(120f, 50f);
            irt.anchoredPosition = pos;

            GameObject lblGO = new GameObject("Lbl");
            lblGO.transform.SetParent(itemGO.transform, false);
            _itemLabels[i] = lblGO.AddComponent<TextMeshProUGUI>();
            _itemLabels[i].text      = Labels[i];
            _itemLabels[i].alignment = TextAlignmentOptions.Center;
            _itemLabels[i].fontSize  = 18f;
            _itemLabels[i].color     = Color.white;
            RectTransform lrt = lblGO.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = lrt.offsetMax = Vector2.zero;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static int ClosestItem(float angle)
    {
        int best = 0; float bestDist = float.MaxValue;
        for (int i = 0; i < Angles.Length; i++)
        {
            float d = Mathf.Abs(Mathf.DeltaAngle(angle, Angles[i]));
            if (d < bestDist) { bestDist = d; best = i; }
        }
        return best;
    }

    private static void AddStretch(Transform parent, string name, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<Image>().color = color;
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private void TryFindRightCtrl()
    {
        if (_rightFound) return;
        if (Time.time < _nextSearchTime) return;
        _nextSearchTime = Time.time + 2f;
        var devices = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller, devices);
        if (devices.Count > 0) { _rightCtrl = devices[0]; _rightFound = true; }
    }
}
