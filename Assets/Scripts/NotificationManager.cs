using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;


public class NotificationManager : MonoBehaviour
{
    public static NotificationManager Instance { get; private set; }

    /// <summary>Global font applied to every procedural TMP element. Set from Inspector on the Managers GameObject.</summary>
    public static TMP_FontAsset GlobalFont { get; private set; }

    /// <summary>Fired when the player presses a trigger on the splash screen, or selects Tutorial from the radial menu.</summary>
    public static event System.Action OnTutorialRequested;

    /// <summary>Invoke from any script to request the tutorial.</summary>
    public static void RequestTutorial() => OnTutorialRequested?.Invoke();

    [Header("Optional: assign TMP objects that are children of the XR Camera")]
    public TextMeshProUGUI warningText;
    public TextMeshProUGUI statusText;

    [Header("Settings")]
    public float warningDuration = 5f;

    [Header("Font (applied to all UI text)")]
    [Tooltip("Drag a TMP Font Asset here — applied to every text element in the app.")]
    public TMP_FontAsset splashFont;

    // -------------------------------------------------------------------------
    // Private
    // -------------------------------------------------------------------------

    private Coroutine _warningTimer;
    private bool _warningActive;           // true while a timed warning is showing

    private InputDevice _left, _right;
    private bool _leftFound, _rightFound;
    private float _nextControllerSearch;

    private TextMeshProUGUI _phaseText;
    private TextMeshProUGUI _subHintText;
    private Image _phaseBg;
    private Image _stripeImage;
    private GameObject _phaseCanvas;

    // Description panel — appears below the phase badge in Design phase
    private TextMeshProUGUI _descText;
    private Image           _descStripeImage;
    private GameObject      _descCanvas;

    // Fade-to-black overlay
    private Image           _fadeImage;
    private GameObject      _fadeCanvas;

    private GameObject _splashCanvas;
    private TextMeshProUGUI _splashText;
    private TextMeshProUGUI _splashTutorialText;
    private bool _splashPrevAnyButton;
    private bool _splashPrevTrigger;
    private GameObject _notificationCanvas;

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        GlobalFont = splashFont; // set before any other script's Start() runs
    }

    private void Start()
    {
        if (warningText == null || statusText == null)
            BuildProceduralOverlays();

        // Apply Google font to error/status texts regardless of how they were created
        if (splashFont != null)
        {
            if (warningText != null) warningText.font = splashFont;
            if (statusText  != null) statusText.font  = splashFont;
        }

        // Hide everything until splash is dismissed
        if (warningText) warningText.gameObject.SetActive(false);
        if (statusText)  statusText.gameObject.SetActive(false);

        BuildPhaseDisplay();      // starts hidden
        BuildDescriptionArea();   // starts hidden, shown in Design phase
        BuildFadeOverlay();       // starts hidden, used for transitions
        BuildSplashOverlay();     // starts visible
    }

    private void Update()
    {
        TryFindControllers();

        if (IsSplashActive())
        {
            if (_notificationCanvas != null) _notificationCanvas.SetActive(false);
            if (_phaseCanvas != null)        _phaseCanvas.SetActive(false);
            if (_descCanvas != null)         _descCanvas.SetActive(false);
            if (warningText) warningText.gameObject.SetActive(false);
            if (statusText)  statusText.gameObject.SetActive(false);
            UpdateSplashPulse();
            CheckSplashDismiss();
            return;
        }

        if (_splashCanvas != null && _splashCanvas.activeSelf)
            _splashCanvas.SetActive(false);
        if (_notificationCanvas != null && !_notificationCanvas.activeSelf)
            _notificationCanvas.SetActive(true);

        if (!_warningActive && warningText != null)
            warningText.gameObject.SetActive(false);

        UpdatePhaseDisplay();
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    public void ShowWarning(string message)
    {
        if (!warningText) return;
        _warningActive = true;
        warningText.text = message;
        warningText.color = new Color(1f, 0.35f, 0.2f);
        warningText.gameObject.SetActive(true);
        if (_warningTimer != null) StopCoroutine(_warningTimer);
        _warningTimer = StartCoroutine(ExpireWarning());
        Debug.LogWarning("[Notification] " + message);
    }

    public void ShowStatus(string message)
    {
        if (!statusText) return;
        statusText.text = message;
        statusText.gameObject.SetActive(true);
        Debug.Log("[Status] " + message);
    }

    public void ClearStatus()
    {
        if (statusText) statusText.gameObject.SetActive(false);
    }

    /// <summary>Re-shows the splash screen. Call from Home button to fully reset the session.</summary>
    public void ReturnToSplash()
    {
        // Mark buttons as "already held" so the splash won't instantly re-dismiss
        // on the same frame the Home action fires.
        _splashPrevAnyButton = true;
        _splashPrevTrigger   = true;

        if (_splashCanvas != null) _splashCanvas.SetActive(true);

        AppStateManager.Instance?.SetState(AppState.Splash);
    }

    // -------------------------------------------------------------------------
    // Warning expiry
    // -------------------------------------------------------------------------

    private IEnumerator ExpireWarning()
    {
        yield return new WaitForSeconds(warningDuration);
        _warningActive = false;
        if (warningText) warningText.gameObject.SetActive(false);
    }

    // -------------------------------------------------------------------------
    // Controller discovery
    // -------------------------------------------------------------------------

    private void TryFindControllers()
    {
        if (_leftFound && _rightFound) return;
        if (Time.time < _nextControllerSearch) return;
        _nextControllerSearch = Time.time + 2f;

        var list = new List<InputDevice>();

        if (!_leftFound)
        {
            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller, list);
            if (list.Count > 0) { _left = list[0]; _leftFound = true; list.Clear(); }
        }
        if (!_rightFound)
        {
            InputDevices.GetDevicesWithCharacteristics(
                InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller, list);
            if (list.Count > 0) { _right = list[0]; _rightFound = true; }
        }
    }

    // -------------------------------------------------------------------------
    // Splash screen
    // -------------------------------------------------------------------------

    private bool IsSplashActive()
        => AppStateManager.Instance != null && AppStateManager.Instance.CurrentPhase == AppPhase.None;

    private void BuildSplashOverlay()
    {
        Transform camParent = Camera.main ? Camera.main.transform : transform;

        _splashCanvas = new GameObject("SplashCanvas");
        _splashCanvas.transform.SetParent(camParent, false);
        Canvas canvas = _splashCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        _splashCanvas.AddComponent<CanvasScaler>();

        RectTransform crt = _splashCanvas.GetComponent<RectTransform>();
        crt.sizeDelta     = new Vector2(700f, 280f);
        crt.localScale    = Vector3.one * 0.001f;
        crt.localPosition = new Vector3(0f, 0f, 0.7f);

        // Main line — "Press any button to start"
        GameObject textGO = new GameObject("SplashText");
        textGO.transform.SetParent(_splashCanvas.transform, false);
        _splashText = textGO.AddComponent<TextMeshProUGUI>();
        _splashText.text          = "Press any button to start";
        _splashText.alignment     = TextAlignmentOptions.Center;
        _splashText.fontSize      = 62f;
        _splashText.fontStyle     = FontStyles.Bold;
        _splashText.color         = Color.white;
        _splashText.raycastTarget = false;
        if (splashFont != null)
        {
            _splashText.font = splashFont;
        }
        else
        {
            Shader sdfShader = Shader.Find("TextMeshPro/Mobile/Distance Field")
                            ?? Shader.Find("TextMeshPro/Distance Field");
            if (sdfShader != null)
                _splashText.fontMaterial = new Material(_splashText.fontMaterial) { shader = sdfShader };
        }
        RectTransform trt = textGO.GetComponent<RectTransform>();
        trt.sizeDelta        = new Vector2(680f, 100f);
        trt.anchoredPosition = new Vector2(0f, 52f);   // upper half

        // Sub-line — "Press any trigger for a tutorial"
        GameObject tutGO = new GameObject("SplashTutorialText");
        tutGO.transform.SetParent(_splashCanvas.transform, false);
        _splashTutorialText = tutGO.AddComponent<TextMeshProUGUI>();
        _splashTutorialText.text          = "Press any trigger for a tutorial";
        _splashTutorialText.alignment     = TextAlignmentOptions.Center;
        _splashTutorialText.fontSize      = 32f;
        _splashTutorialText.fontStyle     = FontStyles.Normal;
        _splashTutorialText.color         = new Color(0.75f, 0.88f, 1f);
        _splashTutorialText.raycastTarget = false;
        if (splashFont != null) _splashTutorialText.font = splashFont;
        RectTransform tutRt = tutGO.GetComponent<RectTransform>();
        tutRt.sizeDelta        = new Vector2(680f, 60f);
        tutRt.anchoredPosition = new Vector2(0f, -42f); // lower half
    }

    private void UpdateSplashPulse()
    {
        if (_splashText == null) return;
        float alpha = Mathf.Lerp(0.1f, 1f, (Mathf.Sin(Time.time * 1.2f) + 1f) * 0.5f);
        Color c = _splashText.color; c.a = alpha; _splashText.color = c;

        if (_splashTutorialText != null)
        {
            Color c2 = _splashTutorialText.color; c2.a = alpha; _splashTutorialText.color = c2;
        }
    }

    private void CheckSplashDismiss()
    {
        if (!_leftFound && !_rightFound) return;

        bool anyNow     = false;
        bool triggerNow = false;

        if (_leftFound)
        {
            _left.TryGetFeatureValue(CommonUsages.primaryButton,   out bool x);
            _left.TryGetFeatureValue(CommonUsages.secondaryButton, out bool y);
            _left.TryGetFeatureValue(CommonUsages.trigger,         out float lt);
            _left.TryGetFeatureValue(CommonUsages.grip,            out float lg);
            if (lt > 0.5f) triggerNow = true;
            anyNow |= x || y || lt > 0.5f || lg > 0.5f;
        }
        if (_rightFound)
        {
            _right.TryGetFeatureValue(CommonUsages.primaryButton,   out bool a);
            _right.TryGetFeatureValue(CommonUsages.secondaryButton, out bool b);
            _right.TryGetFeatureValue(CommonUsages.trigger,         out float rt);
            _right.TryGetFeatureValue(CommonUsages.grip,            out float rg);
            if (rt > 0.5f) triggerNow = true;
            anyNow |= a || b || rt > 0.5f || rg > 0.5f;
        }

        // Rising edge only
        if (anyNow && !_splashPrevAnyButton)
        {
            if (triggerNow && !_splashPrevTrigger)
            {
                // Trigger specifically → tutorial entry point
                RequestTutorial();
                // Falls back to Idle until TutorialManager is implemented
                AppStateManager.Instance?.SetState(AppState.Idle);
            }
            else
            {
                AppStateManager.Instance?.SetState(AppState.Idle);
            }
        }

        _splashPrevAnyButton = anyNow;
        _splashPrevTrigger   = triggerNow;
    }

    // -------------------------------------------------------------------------
    // Phase badge (top-right, always visible)
    // -------------------------------------------------------------------------

    private void BuildPhaseDisplay()
    {
        Transform camParent = Camera.main ? Camera.main.transform : transform;

        _phaseCanvas = new GameObject("PhaseCanvas");
        GameObject canvasGO = _phaseCanvas;
        canvasGO.transform.SetParent(camParent, false);
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvasGO.AddComponent<CanvasScaler>();

        // Canvas is 155 units tall (0.155 m). Centre shifted to Y=0.09 so the top
        // edge stays at Y=0.165 (same as before) and the badge grows downward.
        RectTransform crt = canvasGO.GetComponent<RectTransform>();
        crt.sizeDelta     = new Vector2(380f, 155f);
        crt.localScale    = Vector3.one * 0.001f;
        crt.localPosition = new Vector3(0.10f, 0.09f, 0.65f);

        // Background pill
        GameObject bg = new GameObject("PhaseBG");
        bg.transform.SetParent(canvasGO.transform, false);
        _phaseBg = bg.AddComponent<Image>();
        _phaseBg.color = new Color(0f, 0f, 0f, 0.62f);
        RectTransform bgRt = bg.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;

        // Left accent stripe (full height)
        GameObject stripe = new GameObject("Stripe");
        stripe.transform.SetParent(canvasGO.transform, false);
        _stripeImage = stripe.AddComponent<Image>();
        _stripeImage.color = Color.white;
        RectTransform srt = stripe.GetComponent<RectTransform>();
        srt.sizeDelta        = new Vector2(4f, 141f);
        srt.anchoredPosition = new Vector2(-185f, 0f);

        // Phase text — upper portion (step + name)
        GameObject textGO = new GameObject("PhaseText");
        textGO.transform.SetParent(canvasGO.transform, false);
        _phaseText = textGO.AddComponent<TextMeshProUGUI>();
        _phaseText.alignment          = TextAlignmentOptions.Left;
        _phaseText.fontSize           = 26f;
        _phaseText.enableWordWrapping = false;
        if (splashFont != null) _phaseText.font = splashFont;
        RectTransform trt = textGO.GetComponent<RectTransform>();
        trt.sizeDelta        = new Vector2(356f, 78f);
        trt.anchoredPosition = new Vector2(8f, 31f);

        // Divider between phase name and sub-hint
        GameObject div = new GameObject("Divider");
        div.transform.SetParent(canvasGO.transform, false);
        div.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);
        RectTransform drt = div.GetComponent<RectTransform>();
        drt.sizeDelta        = new Vector2(346f, 1f);
        drt.anchoredPosition = new Vector2(8f, -5f);

        // Sub-hint text — lower portion
        GameObject hintGO = new GameObject("SubHint");
        hintGO.transform.SetParent(canvasGO.transform, false);
        _subHintText = hintGO.AddComponent<TextMeshProUGUI>();
        _subHintText.alignment          = TextAlignmentOptions.Left;
        _subHintText.fontSize           = 20f;
        _subHintText.enableWordWrapping = true;
        _subHintText.lineSpacing        = 2f;
        _subHintText.color              = new Color(0.88f, 0.92f, 1f, 0.75f);
        if (splashFont != null) _subHintText.font = splashFont;
        RectTransform hrt = hintGO.GetComponent<RectTransform>();
        hrt.sizeDelta        = new Vector2(356f, 54f);
        hrt.anchoredPosition = new Vector2(8f, -50f);

        canvasGO.SetActive(false); // hidden until splash dismissed
    }

    private void UpdatePhaseDisplay()
    {
        if (_phaseText == null) return;

        if (AppStateManager.Instance == null) return;
        AppPhase phase = AppStateManager.Instance.CurrentPhase;
        AppState state = AppStateManager.Instance.CurrentState;

        bool visible = phase != AppPhase.None;
        if (_phaseCanvas != null) _phaseCanvas.SetActive(visible);
        if (!visible) return;

        string step, name;
        Color color;
        switch (phase)
        {
            case AppPhase.PositionSelection:
                step  = "1 / 3";
                name  = "Position Selection";
                color = new Color(0.4f, 0.85f, 1f);
                break;
            case AppPhase.Design:
                step  = "2 / 3";
                name  = "Design";
                color = new Color(1f, 0.78f, 0.2f);
                break;
            case AppPhase.Generation:
                step  = "2 / 3";
                name  = "Generating…";
                color = new Color(0.85f, 0.45f, 1f);
                break;
            case AppPhase.Visualization:
                step  = "3 / 3";
                name  = "Visualization";
                color = new Color(0.35f, 1f, 0.55f);
                break;
            default:
                step  = "·";
                name  = "";
                color = Color.white;
                break;
        }

        // Step counter smaller + dimmer, phase name bold below it
        Color dim = new Color(color.r, color.g, color.b, 0.60f);
        string hexDim = ColorUtility.ToHtmlStringRGBA(dim);
        _phaseText.text  = $"<size=65%><color=#{hexDim}>{step}</color></size>\n<b>{name}</b>";
        _phaseText.color = color;

        if (_stripeImage != null) _stripeImage.color = color;
        if (_descStripeImage != null) _descStripeImage.color = color;
        if (_phaseBg != null)
            _phaseBg.color = new Color(color.r * 0.08f, color.g * 0.08f, color.b * 0.08f, 0.72f);

        // Sub-hint: short actionable text per state
        if (_subHintText != null)
            _subHintText.text = SubHint(state);

        RefreshDescVisibility(phase);
    }

    private static string SubHint(AppState state)
    {
        switch (state)
        {
            case AppState.Idle:
                return "R Trigger to place anchor";
            case AppState.PlacingAnchor:
                return "Hold R Grip to describe   •   B to reposition";
            case AppState.Recording:
                return "● Recording — release to stop";
            case AppState.Transcribing:
                return "Transcribing your recording…";
            case AppState.ReviewingTranscription:
                return "Y to generate   •   R Grip to add or modify";
            case AppState.GeneratingMassing:
                return "Generating options, please wait…";
            case AppState.ViewingOptions:
                return "L Trigger → Option A   •   R Trigger → Option B";
            default:
                return "";
        }
    }

    // -------------------------------------------------------------------------
    // Description panel  (below the phase badge, right side, Design phase only)
    // -------------------------------------------------------------------------

    private void BuildDescriptionArea()
    {
        Transform cam = Camera.main ? Camera.main.transform : transform;

        _descCanvas = new GameObject("DescCanvas");
        _descCanvas.transform.SetParent(cam, false);
        Canvas c = _descCanvas.AddComponent<Canvas>();
        c.renderMode = RenderMode.WorldSpace;
        _descCanvas.AddComponent<CanvasScaler>();

        // pivot=(0.5,1) → localPosition.y is the TOP of this canvas
        // Sits 2-3 mm below the phase badge bottom (badge bottom ≈ Y=0.0125)
        RectTransform rt = _descCanvas.GetComponent<RectTransform>();
        rt.sizeDelta     = new Vector2(380f, 145f);
        rt.localScale    = Vector3.one * 0.001f;
        rt.pivot         = new Vector2(0.5f, 1f);
        rt.localPosition = new Vector3(0.10f, 0.010f, 0.65f);

        // Dark background
        GameObject bg = new GameObject("BG");
        bg.transform.SetParent(_descCanvas.transform, false);
        var bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.62f);
        RectTransform bgRt = bg.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;

        // Left accent stripe (full height, tinted by phase colour in UpdatePhaseDisplay)
        GameObject stripe = new GameObject("DescStripe");
        stripe.transform.SetParent(_descCanvas.transform, false);
        _descStripeImage = stripe.AddComponent<Image>();
        RectTransform srt = stripe.GetComponent<RectTransform>();
        srt.anchorMin = new Vector2(0f, 0f); srt.anchorMax = new Vector2(0f, 1f);
        srt.pivot     = new Vector2(0f, 0.5f);
        srt.sizeDelta = new Vector2(4f, 0f);
        srt.anchoredPosition = Vector2.zero;

        // Description text
        GameObject textGO = new GameObject("DescText");
        textGO.transform.SetParent(_descCanvas.transform, false);
        _descText = textGO.AddComponent<TextMeshProUGUI>();
        _descText.alignment          = TextAlignmentOptions.Left;
        _descText.fontSize           = 16f;
        _descText.enableWordWrapping = true;
        _descText.lineSpacing        = 4f;
        _descText.color              = new Color(0.9f, 0.95f, 1f, 0.95f);
        if (splashFont != null) _descText.font = splashFont;
        RectTransform trt = textGO.GetComponent<RectTransform>();
        trt.anchorMin        = new Vector2(0.5f, 1f);
        trt.anchorMax        = new Vector2(0.5f, 1f);
        trt.sizeDelta        = new Vector2(356f, 135f);
        trt.anchoredPosition = new Vector2(8f, -72f);

        _descCanvas.SetActive(false);
    }

    /// <summary>Call from MuseumDescriptionPanel after each summary update.</summary>
    public void SetDescription(string text)
    {
        if (_descText != null) _descText.text = text ?? "";
        ResizeDescCanvas();
        RefreshDescVisibility(AppStateManager.Instance?.CurrentPhase ?? AppPhase.None);
    }

    // Resize the description canvas height to fit the current text content.
    private void ResizeDescCanvas()
    {
        if (_descText == null || _descCanvas == null) return;
        const float pad = 10f; // canvas units total (top + bottom)
        float textH   = _descText.GetPreferredValues(356f, 0f).y;
        float canvasH = Mathf.Clamp(textH + pad, 28f, 210f);

        RectTransform canvasRt = _descCanvas.GetComponent<RectTransform>();
        if (canvasRt != null) canvasRt.sizeDelta = new Vector2(canvasRt.sizeDelta.x, canvasH);

        RectTransform textRt = _descText.GetComponent<RectTransform>();
        if (textRt != null)
        {
            textRt.sizeDelta        = new Vector2(textRt.sizeDelta.x, canvasH - pad);
            textRt.anchoredPosition = new Vector2(8f, -canvasH * 0.5f);
        }
    }

    public void ClearDescription()
    {
        if (_descText != null) _descText.text = "";
        if (_descCanvas != null) _descCanvas.SetActive(false);
    }

    private void RefreshDescVisibility(AppPhase phase)
    {
        if (_descCanvas == null) return;
        bool hasText = _descText != null && !string.IsNullOrWhiteSpace(_descText.text);
        _descCanvas.SetActive(phase == AppPhase.Design && hasText);
    }

    // -------------------------------------------------------------------------
    // Fade-to-black overlay
    // -------------------------------------------------------------------------

    private void BuildFadeOverlay()
    {
        Transform cam = Camera.main ? Camera.main.transform : transform;

        _fadeCanvas = new GameObject("FadeCanvas");
        _fadeCanvas.transform.SetParent(cam, false);
        var c = _fadeCanvas.AddComponent<Canvas>();
        c.renderMode   = RenderMode.WorldSpace;
        c.sortingOrder = 999;
        _fadeCanvas.AddComponent<CanvasScaler>();

        RectTransform rt = _fadeCanvas.GetComponent<RectTransform>();
        rt.sizeDelta     = new Vector2(2000f, 2000f);
        rt.localScale    = Vector3.one * 0.001f;
        rt.localPosition = new Vector3(0f, 0f, 0.15f);

        GameObject img = new GameObject("FadeImg");
        img.transform.SetParent(_fadeCanvas.transform, false);
        _fadeImage = img.AddComponent<Image>();
        _fadeImage.color = new Color(0f, 0f, 0f, 0f);
        RectTransform irt = img.GetComponent<RectTransform>();
        irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one;
        irt.offsetMin = irt.offsetMax = Vector2.zero;

        _fadeCanvas.SetActive(false);
    }

    public Coroutine FadeToBlack(float duration)
    {
        if (_fadeCanvas != null) _fadeCanvas.SetActive(true);
        return StartCoroutine(FadeCoroutine(0f, 1f, duration));
    }

    public Coroutine FadeFromBlack(float duration)
        => StartCoroutine(FadeOutAndHide(duration));

    private IEnumerator FadeOutAndHide(float duration)
    {
        yield return FadeCoroutine(1f, 0f, duration);
        if (_fadeCanvas != null) _fadeCanvas.SetActive(false);
    }

    private IEnumerator FadeCoroutine(float from, float to, float duration)
    {
        if (_fadeImage == null) yield break;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            _fadeImage.color = new Color(0f, 0f, 0f, Mathf.Lerp(from, to, t / duration));
            yield return null;
        }
        _fadeImage.color = new Color(0f, 0f, 0f, to);
    }

    // -------------------------------------------------------------------------
    // Procedural overlay creation  (used when refs not set in Inspector)
    // -------------------------------------------------------------------------

    private void BuildProceduralOverlays()
    {
        Transform camParent = Camera.main ? Camera.main.transform : transform;

        _notificationCanvas = new GameObject("NotificationCanvas");
        GameObject canvasGO = _notificationCanvas;
        canvasGO.transform.SetParent(camParent, false);
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvasGO.AddComponent<CanvasScaler>();

        RectTransform crt = canvasGO.GetComponent<RectTransform>();
        crt.sizeDelta     = new Vector2(560f, 130f);
        crt.localScale    = Vector3.one * 0.001f;
        crt.localPosition = new Vector3(-0.26f, 0.14f, 0.65f);  // top-left corner

        // Warning text (top row)
        warningText = MakeTMP(canvasGO.transform, "WarningText",
                              new Vector2(0f, 38f), new Vector2(540f, 55f),
                              new Color(1f, 0.5f, 0.35f), 28f, FontStyles.Bold, splashFont);

        // Status text (bottom row)
        statusText = MakeTMP(canvasGO.transform, "StatusText",
                             new Vector2(0f, -32f), new Vector2(540f, 50f),
                             new Color(0.85f, 0.92f, 1f), 26f, FontStyles.Italic, splashFont);
    }

    private static TextMeshProUGUI MakeTMP(Transform parent, string name,
                                            Vector2 anchoredPos, Vector2 sizeDelta,
                                            Color color, float fontSize, FontStyles style,
                                            TMP_FontAsset font = null)
    {
        // Background pill
        GameObject bg = new GameObject(name + "_BG");
        bg.transform.SetParent(parent, false);
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.60f);
        RectTransform bgRt = bg.GetComponent<RectTransform>();
        bgRt.sizeDelta = sizeDelta + new Vector2(24f, 12f);
        bgRt.anchoredPosition = anchoredPos;

        // Text
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.alignment  = TextAlignmentOptions.Center;
        tmp.color      = color;
        tmp.fontSize   = fontSize;
        tmp.fontStyle  = style;
        if (font != null) tmp.font = font;

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.sizeDelta       = sizeDelta;
        rt.anchoredPosition = anchoredPos;
        return tmp;
    }
}
