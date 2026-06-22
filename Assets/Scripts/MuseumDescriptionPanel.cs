using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Left-side panel showing the accumulated museum description.
/// Each voice recording adds/updates a named section.
/// Buttons: Clear (per section) | Clear All | OK → Generate.
///
/// Unity setup:
///   - Create a World-Space Canvas anchored to the left of the camera (e.g. at -0.35, 0, 0.6
///     relative to the camera), parented to the Camera so it follows the user.
///   - Inside: a vertical LayoutGroup (sectionContainer) + a row of buttons at the bottom.
///   - The sectionRowPrefab needs:
///       · TextMeshProUGUI  (section label)
///       · Button           (clear button for that section) with a child TMP label "Clear"
///   - Assign clearAllButton and okButton in the inspector.
/// </summary>
public class MuseumDescriptionPanel : MonoBehaviour
{
    public static MuseumDescriptionPanel Instance { get; private set; }

    [Header("Section list")]
    public Transform  sectionContainer;    // VerticalLayoutGroup parent
    public GameObject sectionRowPrefab;    // Prefab: TMP label + Clear button


    // -------------------------------------------------------------------------

    private class SectionRow
    {
        public GameObject root;
        public TextMeshProUGUI label;
        public string text;
    }

    private readonly Dictionary<string, SectionRow> _rows = new Dictionary<string, SectionRow>();

    // -------------------------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

    }

    private void Start()
    {
        gameObject.SetActive(false); // hidden until Design phase

        // Move sectionContainer to top of canvas so it appears flush below the phase badge.
        // In the scene, the container is anchored to center (0,0) of a 1321×829 canvas,
        // which places it ~40 cm below camera origin. Re-anchor it to the canvas top.
        if (sectionContainer != null)
        {
            RectTransform scrt = sectionContainer.GetComponent<RectTransform>();
            if (scrt != null)
            {
                scrt.anchorMin        = new Vector2(0f, 1f);
                scrt.anchorMax        = new Vector2(1f, 1f);
                scrt.pivot            = new Vector2(0.5f, 1f);
                scrt.anchoredPosition = Vector2.zero;
                scrt.sizeDelta        = new Vector2(0f, 400f);
            }
        }

        if (AppStateManager.Instance != null)
            AppStateManager.Instance.OnPhaseChanged += OnPhaseChanged;
    }

    private void OnDestroy()
    {
        if (AppStateManager.Instance != null)
            AppStateManager.Instance.OnPhaseChanged -= OnPhaseChanged;
    }

    private void OnPhaseChanged(AppPhase oldPhase, AppPhase newPhase)
    {
        // Display is handled entirely by NotificationManager's description canvas.
        gameObject.SetActive(false);
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    public string GetSectionText(string sectionName)
        => _rows.TryGetValue(sectionName, out var row) ? row.text : null;

    /// <summary>Called by VoiceCommandManager after each successful transcription.</summary>
    public void AddOrUpdateSection(string sectionName, string text)
    {
        if (!_rows.ContainsKey(sectionName))
        {
            if (sectionRowPrefab == null || sectionContainer == null)
            {
                Debug.LogWarning("[MuseumDescriptionPanel] sectionRowPrefab or sectionContainer not assigned — skipping row creation.");
                return;
            }
            GameObject row = Instantiate(sectionRowPrefab, sectionContainer);
            var sr = new SectionRow
            {
                root  = row,
                label = row.GetComponentInChildren<TextMeshProUGUI>()
            };
            if (sr.label != null && NotificationManager.GlobalFont != null)
                sr.label.font = NotificationManager.GlobalFont;

            // Wire up the Clear button for this row
            Button clearBtn = row.GetComponentInChildren<Button>();
            string captured = sectionName;
            clearBtn.onClick.AddListener(() => ClearSection(captured));

            _rows[sectionName] = sr;
        }

        _rows[sectionName].text       = text;
        _rows[sectionName].label.text = text;
        NotificationManager.Instance?.SetDescription(text);
    }

    /// <summary>Called by DescriptionAction.Undo().</summary>
    public void RestoreSection(string sectionName, string prevText)
        => AddOrUpdateSection(sectionName, prevText);

    // -------------------------------------------------------------------------

    private void ClearSection(string sectionName)
    {
        if (!_rows.ContainsKey(sectionName)) return;

        UndoManager.Instance?.PushDescription(sectionName, _rows[sectionName].text);

        Destroy(_rows[sectionName].root);
        _rows.Remove(sectionName);
    }

    public void ClearAll()
    {
        foreach (string key in new List<string>(_rows.Keys))
            ClearSection(key);
        NotificationManager.Instance?.ClearDescription();
    }

    public void Submit()
    {
        if (_rows.Count == 0)
        {
            NotificationManager.Instance?.ShowWarning("Describe the museum first, then press Y.");
            return;
        }

        var sb = new StringBuilder();
        foreach (var kv in _rows)
            sb.AppendLine(kv.Value.text);

        AppFlowController.Instance?.OnVoiceTranscriptionComplete(sb.ToString().Trim());
    }
}
