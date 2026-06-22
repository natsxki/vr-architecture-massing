using UnityEngine;

public class AppFlowController : MonoBehaviour
{
    public static AppFlowController Instance { get; private set; }

    [Header("Managers")]
    public GeminiManager geminiManager;

    [Header("Testing & Fallback")]
    [Tooltip("Type a prompt here to test in the Unity Editor without voice.")]
    [TextArea]
    public string editorTestPrompt = "I want a large main exhibition hall in the center, surrounded by three smaller, quiet sensory rooms spread out like a star.";

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // -------------------------------------------------------------------------

    /// <summary>
    /// Link this to a VR Canvas Button for editor / quick testing.
    /// </summary>
    public void OnStartButtonPressed()
    {
        Debug.Log("[AppFlow] Start button pressed (editor test).");
        OnVoiceTranscriptionComplete(editorTestPrompt);
    }

    /// <summary>
    /// Called by MuseumDescriptionPanel.OnOk() when the user is happy with
    /// their recorded description and wants to generate massing options.
    /// </summary>
    public void OnVoiceTranscriptionComplete(string transcribedText)
    {
        if (string.IsNullOrWhiteSpace(transcribedText))
        {
            NotificationManager.Instance?.ShowWarning("Description is empty — record something first.");
            return;
        }

        string iterationBase = OptionSelectionMenu.IterationBaseJson;
        OptionSelectionMenu.ClearIterationBase();
        geminiManager.RequestMassingOptions(transcribedText, iterationBase);
    }
}
