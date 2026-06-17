using UnityEngine;

public class AppFlowController : MonoBehaviour
{
    [Header("Managers")]
    public GeminiManager geminiManager;

    [Header("Testing & Fallback")]
    [Tooltip("Type a prompt here to test in the Unity Editor without voice.")]
    [TextArea]
    public string editorTestPrompt = "I want a large main exhibition hall in the center, surrounded by three smaller, quiet sensory rooms spread out like a star.";

    /// <summary>
    /// Link this method to your VR Canvas Button (OnClick) or Meta Poke Interactable.
    /// </summary>
    public void OnStartButtonPressed()
    {
        Debug.Log("Button Pressed: Starting Generation Flow...");

        // In the final Quest build, you will call your Meta Voice SDK (Wit.ai) here.
        // It will listen, transcribe, and return a string.
        
        // For the MVP and Editor testing, we immediately fire the fallback text:
        OnVoiceTranscriptionComplete(editorTestPrompt);
    }

    /// <summary>
    /// The Meta Voice SDK will fire an event when the user finishes speaking.
    /// Link that event to this method.
    /// </summary>
    public void OnVoiceTranscriptionComplete(string transcribedText)
    {
        if (string.IsNullOrWhiteSpace(transcribedText))
        {
            Debug.LogWarning("Transcribed text was empty. Aborting.");
            return;
        }

        geminiManager.RequestMassingOptions(transcribedText);
    }
}