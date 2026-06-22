using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Minimal compass rose — no background, just a bicolor cross and N/E/S/W labels.
/// North arm is red, South arm white, East/West arms pale blue.
/// The rose rotates with the player; labels counter-rotate to stay upright.
/// </summary>
public class CompassDisplay : MonoBehaviour
{
    private const float ArmLength    = 44f;   // center → tip of each arm
    private const float ArmThickness = 3f;
    private const float LabelDist    = 62f;   // center → label anchor

    private static readonly Color ColNorth = new Color(0.95f, 0.22f, 0.12f);  // red
    private static readonly Color ColSouth = new Color(0.92f, 0.92f, 0.92f);  // near-white
    private static readonly Color ColEW    = new Color(0.68f, 0.82f, 1.00f, 0.85f); // pale blue

    // -------------------------------------------------------------------------

    private GameObject        _compassCanvas;
    private RectTransform     _roseContainer;
    private TextMeshProUGUI[] _cardinalLabels;
    private Transform         _cam;

    // -------------------------------------------------------------------------

    private void Awake() => BuildCompass();

    private void Start()
    {
        _cam = Camera.main?.transform;

        TMP_FontAsset f = NotificationManager.GlobalFont;
        if (f != null && _cardinalLabels != null)
            foreach (var lbl in _cardinalLabels)
                if (lbl != null) lbl.font = f;
    }

    private void Update()
    {
        if (_cam == null) { _cam = Camera.main?.transform; return; }

        bool splash = AppStateManager.Instance != null
                   && AppStateManager.Instance.CurrentPhase == AppPhase.None;
        _compassCanvas.SetActive(!splash);
        if (splash) return;

        float heading = _cam.eulerAngles.y;   // 0–360, 0 = North

        // Rotate the rose so the correct direction faces the top
        _roseContainer.localRotation = Quaternion.Euler(0f, 0f, heading);

        // Counter-rotate each label to keep text upright
        Quaternion upright = Quaternion.Euler(0f, 0f, -heading);
        foreach (var lbl in _cardinalLabels)
            if (lbl != null) lbl.rectTransform.localRotation = upright;
    }

    // -------------------------------------------------------------------------

    private void BuildCompass()
    {
        Transform camParent = Camera.main ? Camera.main.transform : transform;

        _compassCanvas = new GameObject("CompassCanvas");
        _compassCanvas.transform.SetParent(camParent, false);
        _compassCanvas.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        _compassCanvas.AddComponent<CanvasScaler>();

        RectTransform crt = _compassCanvas.GetComponent<RectTransform>();
        crt.sizeDelta     = new Vector2(160f, 160f);
        crt.localScale    = Vector3.one * 0.001f;
        crt.localPosition = new Vector3(-0.20f, 0.17f, 0.65f); // top-left, slightly higher

        // ── Rotating rose ────────────────────────────────────────────────────
        GameObject roseGO = new GameObject("Rose");
        _roseContainer = roseGO.AddComponent<RectTransform>();
        _roseContainer.SetParent(_compassCanvas.transform, false);
        _roseContainer.sizeDelta        = Vector2.zero;
        _roseContainer.anchoredPosition = Vector2.zero;

        // North arm (red) — upper half of vertical bar
        MakeRect(_roseContainer, "ArmN",
                 new Vector2(0f, ArmLength * 0.5f),
                 new Vector2(ArmThickness, ArmLength),
                 ColNorth);

        // South arm (white) — lower half
        MakeRect(_roseContainer, "ArmS",
                 new Vector2(0f, -ArmLength * 0.5f),
                 new Vector2(ArmThickness, ArmLength),
                 ColSouth);

        // East arm (pale blue) — right half of horizontal bar
        MakeRect(_roseContainer, "ArmE",
                 new Vector2(ArmLength * 0.5f, 0f),
                 new Vector2(ArmLength, ArmThickness),
                 ColEW);

        // West arm (pale blue) — left half
        MakeRect(_roseContainer, "ArmW",
                 new Vector2(-ArmLength * 0.5f, 0f),
                 new Vector2(ArmLength, ArmThickness),
                 ColEW);

        // Diamond tips at N and S for classic compass-rose feel
        MakeDiamond(_roseContainer, "TipN", new Vector2(0f,  ArmLength + 5f), ColNorth, 11f);
        MakeDiamond(_roseContainer, "TipS", new Vector2(0f, -(ArmLength + 5f)), ColSouth, 9f);
        MakeDiamond(_roseContainer, "TipE", new Vector2( ArmLength + 3f, 0f), ColEW, 8f);
        MakeDiamond(_roseContainer, "TipW", new Vector2(-(ArmLength + 3f), 0f), ColEW, 8f);

        // Small center circle
        MakeRect(_roseContainer, "Center", Vector2.zero, new Vector2(5f, 5f),
                 new Color(1f, 1f, 1f, 0.85f));

        // Cardinal labels at the arm tips
        string[] names    = { "N",      "E",     "S",     "W"     };
        Color[]  colors   = { ColNorth, ColEW,   ColSouth, ColEW  };
        Vector2[] offsets = {
            new Vector2(0f,       LabelDist),
            new Vector2(LabelDist, 0f),
            new Vector2(0f,      -LabelDist),
            new Vector2(-LabelDist, 0f),
        };

        _cardinalLabels = new TextMeshProUGUI[4];
        for (int i = 0; i < 4; i++)
        {
            GameObject go = new GameObject(names[i]);
            go.transform.SetParent(roseGO.transform, false);
            TextMeshProUGUI lbl = go.AddComponent<TextMeshProUGUI>();
            lbl.text          = names[i];
            lbl.alignment     = TextAlignmentOptions.Center;
            lbl.fontSize      = names[i] == "N" ? 20f : 16f;
            lbl.fontStyle     = FontStyles.Bold;
            lbl.color         = colors[i];
            lbl.raycastTarget = false;
            RectTransform rt  = go.GetComponent<RectTransform>();
            rt.sizeDelta        = new Vector2(28f, 22f);
            rt.anchoredPosition = offsets[i];
            _cardinalLabels[i] = lbl;
        }

        // Fixed gold tick at the top — marks your current heading direction
        GameObject ind = new GameObject("HeadingTick");
        ind.transform.SetParent(_compassCanvas.transform, false);
        TextMeshProUGUI tick = ind.AddComponent<TextMeshProUGUI>();
        tick.text          = "▼";
        tick.alignment     = TextAlignmentOptions.Center;
        tick.fontSize      = 10f;
        tick.color         = new Color(1f, 0.80f, 0.15f, 0.95f);
        tick.raycastTarget = false;
        RectTransform tickRt = ind.GetComponent<RectTransform>();
        tickRt.sizeDelta        = new Vector2(16f, 16f);
        tickRt.anchoredPosition = new Vector2(0f, 70f);
    }

    // -------------------------------------------------------------------------

    private static void MakeRect(RectTransform parent, string name,
                                  Vector2 pos, Vector2 size, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<Image>().color = color;
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.sizeDelta        = size;
        rt.anchoredPosition = pos;
    }

    private static void MakeDiamond(RectTransform parent, string name,
                                     Vector2 pos, Color color, float size)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        TextMeshProUGUI lbl = go.AddComponent<TextMeshProUGUI>();
        lbl.text          = "◆";
        lbl.alignment     = TextAlignmentOptions.Center;
        lbl.fontSize      = size;
        lbl.color         = color;
        lbl.raycastTarget = false;
        RectTransform rt  = go.GetComponent<RectTransform>();
        rt.sizeDelta        = new Vector2(size * 1.8f, size * 1.8f);
        rt.anchoredPosition = pos;
    }
}
