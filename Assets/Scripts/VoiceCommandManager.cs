using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Handles voice recording triggered by VR interactions, transcribes it via OpenAI Whisper,
/// and packages the transcribed text with spatial coordinates for the LLM.
/// </summary>
public class VoiceCommandManager : MonoBehaviour
{
    [Header("Library Mode (Silent Testing)")]
    public bool bypassVoiceRecording = true;
    [TextArea(3, 5)]
    public string hardcodedTestString = "Here I want to build the entrance hall which is wide and low. Light gallery is tall and narrow on the left.";

    [Header("VR Interaction Link")]
    [Tooltip("Reference to the script handling VR controller inputs. Will auto-find if left null.")]
    public GroundAnchorManager anchorManager;

    [Header("API Key")]
    [Tooltip("Drag Assets/LocalSecrets/whisper_key.txt here. TextAsset loads correctly on Quest (Android).")]
    public TextAsset whisperKeyAsset;

    [Header("OpenAI Whisper Settings")]
    public string transcriptionModel = "whisper-1";
    public string language = "en";

    [Header("Recording Settings")]
    public int maxRecordingSeconds = 30;

    [Header("Microphone Debug")]
    public int preferredMicrophoneIndex = 0;
    public float minRecordingSeconds = 0.8f;
    public float silenceRmsThreshold = 0.003f;
    public bool saveDebugWav = true;

    public static VoiceCommandManager Instance { get; private set; }

    private float recordingStartTime;
    private readonly List<string> _allTranscriptions = new List<string>();

    public void ClearTranscriptions() => _allTranscriptions.Clear();

    // Internal state
    private const int recordingSampleRate = 48000;
    private bool isRecording = false;
    private bool isUploading = false;
    private AudioClip recordedClip;
    private string microphoneDevice;
    private Vector3 currentAnchorPosition;
    private string openAIApiKey;

    private const string TranscriptionUrl = "https://api.openai.com/v1/audio/transcriptions";

    [Serializable]
    private class TranscriptionResponse
    {
        public string text;
    }
    
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        // ── Key loading with automatic fallbacks ──────────────────────────────
        // 1. Inspector TextAsset (preferred — works on Quest)
        // 2. Editor-only: load directly from Assets/LocalSecrets/ without a drag
        // 3. Runtime fallback: Assets/Resources/whisper_key.txt
#if UNITY_EDITOR
        if (whisperKeyAsset == null)
            whisperKeyAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(
                "Assets/LocalSecrets/whisper_key.txt");
#endif
        if (whisperKeyAsset == null)
            whisperKeyAsset = Resources.Load<TextAsset>("whisper_key");

        if (whisperKeyAsset != null)
        {
            openAIApiKey = whisperKeyAsset.text
                .Trim()
                .TrimStart('﻿'); // strip BOM if present
            string source = whisperKeyAsset.name;
            string prefix = openAIApiKey.Length >= 12 ? openAIApiKey.Substring(0, 12) : openAIApiKey;
            Debug.Log($"[VoiceCommandManager] Whisper key loaded from '{source}', length={openAIApiKey.Length}, prefix={prefix}…");
        }
        else
        {
            Debug.LogError("[VoiceCommandManager] Whisper key not found. " +
                           "Drag whisper_key.txt onto the Inspector field, " +
                           "or copy it to Assets/Resources/whisper_key.txt.");
        }

        // 1. Hook up the VR inputs
        if (anchorManager == null)
        {
            anchorManager = FindObjectOfType<GroundAnchorManager>();
        }

        if (anchorManager != null)
        {
            anchorManager.OnVoiceRecordStart += HandleVoiceRecordStart;
            anchorManager.OnVoiceRecordStop += HandleVoiceRecordStop;
            Debug.Log("[VoiceCommandManager] Successfully subscribed to VR controller events.");
        }
        else
        {
            Debug.LogError("[VoiceCommandManager] GroundAnchorManager not found in scene!");
        }

        // 2. Initialize Microphone
        InitMicrophone();
    }

    private void InitMicrophone()
    {
        if (Microphone.devices.Length == 0)
        {
            Debug.LogWarning("[VoiceCommandManager] No microphone detected.");
            return;
        }

        // Print available microphones for debugging
        Debug.Log("<color=cyan>--- Available Microphones ---</color>");
        for (int i = 0; i < Microphone.devices.Length; i++)
        {
            Debug.Log($"[{i}] {Microphone.devices[i]}");
        }

        int index = Mathf.Clamp(preferredMicrophoneIndex, 0, Microphone.devices.Length - 1);
        microphoneDevice = Microphone.devices[index];

        // Microphone.GetDeviceCaps(microphoneDevice, out int minFreq, out int maxFreq);

        // if (maxFreq > 0)
        // {
        //     recordingSampleRate = Mathf.Clamp(recordingSampleRate, minFreq, maxFreq);
        // }

        Debug.Log($"<color=green>[VoiceCommandManager] Selected microphone: {microphoneDevice}</color>");
        // Debug.Log($"[VoiceCommandManager] Device caps: minFreq={minFreq}, maxFreq={maxFreq}, usingSampleRate={recordingSampleRate}");
    }

    // -------------------------------------------------------------------------
    // VR Interaction Handlers
    // -------------------------------------------------------------------------

    private void HandleVoiceRecordStart()
    {
        if (isUploading)
        {
            NotificationManager.Instance?.ShowWarning("Still transcribing — please wait.");
            Debug.LogWarning("[VoiceCommandManager] Currently transcribing, please wait...");
            return;
        }

        // --- NEW LIBRARY MODE BYPASS ---
        if (bypassVoiceRecording)
        {
            isRecording = true;
            AppStateManager.Instance?.SetState(AppState.Recording);
            Debug.Log("<color=yellow>[VoiceCommandManager] Library Mode: Simulated recording started (holding grip).</color>");
            return;
        }
        // -------------------------------

        if (string.IsNullOrEmpty(openAIApiKey) || openAIApiKey == "YOUR_OPENAI_API_KEY_HERE")
        {
            NotificationManager.Instance?.ShowWarning("Whisper API key missing. Check LocalSecrets/whisper_key.txt.");
            Debug.LogError("[VoiceCommandManager] OpenAI API Key is missing!");
            return;
        }

        if (!isRecording && !string.IsNullOrEmpty(microphoneDevice))
        {
            Debug.Log("<color=yellow>[VoiceCommandManager] Recording Started...</color>");
            recordedClip = Microphone.Start(microphoneDevice, false, maxRecordingSeconds, recordingSampleRate);
            recordingStartTime = Time.realtimeSinceStartup;
            isRecording = true;
            AppStateManager.Instance?.SetState(AppState.Recording);
            StartCoroutine(CheckMicrophoneActuallyStarted());
        }
    }

    private IEnumerator CheckMicrophoneActuallyStarted()
    {
        float timeout = 1.0f;
        float start = Time.realtimeSinceStartup;

        while (Microphone.GetPosition(microphoneDevice) <= 0)
        {
            if (Time.realtimeSinceStartup - start > timeout)
            {
                Debug.LogWarning("[VoiceCommandManager] Microphone position is still 0 after 1 second. Device may not be recording.");
                yield break;
            }

            yield return null;
        }

        Debug.Log($"[VoiceCommandManager] Microphone started. Clip frequency={recordedClip.frequency}, channels={recordedClip.channels}");
    }

    private void HandleVoiceRecordStop(Vector3 position)
    {
        if (!isRecording)
        {
            return;
        }

        // --- NEW LIBRARY MODE BYPASS ---
        if (bypassVoiceRecording)
        {
            isRecording = false;
            currentAnchorPosition = position;
            AppStateManager.Instance?.SetState(AppState.Transcribing); // Keep UI flow consistent
            Debug.Log($"<color=green>[VoiceCommandManager] Library Mode: Sending hardcoded string to LLM.</color>");
            NotificationManager.Instance?.ShowStatus("Library Mode: Sending text to LLM...");
            
            // Send directly to Gemini, skipping Whisper
            ProcessCommandForLLM(hardcodedTestString, currentAnchorPosition);
            return;
        }
        // -------------------------------

        int recordingPosition = Microphone.GetPosition(microphoneDevice);
        Microphone.End(microphoneDevice);
        isRecording = false;

        currentAnchorPosition = position;

        if (recordedClip == null || recordingPosition <= 0)
        {
            Debug.LogWarning("[VoiceCommandManager] Recording failed: no samples captured.");
            return;
        }

        float durationFromPosition = recordingPosition / (float)recordedClip.frequency;
        float durationFromTime = Time.realtimeSinceStartup - recordingStartTime;

        Debug.Log($"[VoiceCommandManager] Recording stopped. samplePosition={recordingPosition}, clipFrequency={recordedClip.frequency}, durationBySamples={durationFromPosition:F2}s, durationByTime={durationFromTime:F2}s");

        if (durationFromPosition < minRecordingSeconds)
        {
            NotificationManager.Instance?.ShowWarning("Recording too short — hold the grip longer.");
            AppStateManager.Instance?.SetState(AppState.Idle);
            Debug.LogWarning($"[VoiceCommandManager] Recording too short ({durationFromPosition:F2}s). Not sending to Whisper.");
            return;
        }

        byte[] wavData = ConvertAudioClipToWav(
            recordedClip,
            recordingPosition,
            out float rms,
            out float peak
        );

        Debug.Log($"[VoiceCommandManager] Audio stats: RMS={rms:F6}, Peak={peak:F6}, WAV bytes={wavData.Length}");

        if (saveDebugWav)
        {
            string path = Path.Combine(Application.persistentDataPath, "last_recording.wav");
            File.WriteAllBytes(path, wavData);
            Debug.Log($"<color=cyan>[VoiceCommandManager] Debug WAV saved to: {path}</color>");
        }

        if (rms < silenceRmsThreshold || peak < 0.01f)
        {
            NotificationManager.Instance?.ShowWarning("No audio detected — check your microphone.");
            AppStateManager.Instance?.SetState(AppState.Idle);
            Debug.LogWarning($"[VoiceCommandManager] Audio is probably silent. RMS={rms:F6}, Peak={peak:F6}. Not sending to Whisper.");
            return;
        }

        AppStateManager.Instance?.SetState(AppState.Transcribing);
        NotificationManager.Instance?.ShowStatus("Sending to Whisper… transcribing your speech.");
        Debug.Log("<color=yellow>[VoiceCommandManager] Sending non-silent audio to Whisper...</color>");
        StartCoroutine(SendAudioToWhisper(wavData));
    }

    // -------------------------------------------------------------------------
    // Whisper API Integration
    // -------------------------------------------------------------------------

    private IEnumerator SendAudioToWhisper(byte[] wavData)
    {
        isUploading = true;
        Debug.Log("[VoiceCommandManager] Sending audio to OpenAI Whisper API...");

        List<IMultipartFormSection> formData = new List<IMultipartFormSection>
        {
            new MultipartFormDataSection("model", transcriptionModel),
            new MultipartFormDataSection("response_format", "json"),
            new MultipartFormFileSection("file", wavData, "recording.wav", "audio/wav")
        };

        if (!string.IsNullOrWhiteSpace(language))
        {
            formData.Add(new MultipartFormDataSection("language", language));
        }

        using (UnityWebRequest request = UnityWebRequest.Post(TranscriptionUrl, formData))
        {
            request.SetRequestHeader("Authorization", "Bearer " + openAIApiKey);

            yield return request.SendWebRequest();
            isUploading = false;

            string rawResponse = request.downloadHandler.text;
            Debug.Log($"[VoiceCommandManager] Whisper raw response: {rawResponse}");

            if (request.result != UnityWebRequest.Result.Success)
            {
                string msg = request.responseCode > 0
                    ? $"Whisper HTTP {request.responseCode}: {request.error}"
                    : $"Whisper network error: {request.error}";
                NotificationManager.Instance?.ShowWarning(msg);
                AppStateManager.Instance?.SetState(AppState.Idle);
                NotificationManager.Instance?.ClearStatus();
                Debug.LogError($"[VoiceCommandManager] result={request.result} code={request.responseCode} error={request.error}");
                Debug.LogError($"[VoiceCommandManager] Response: {rawResponse}");
                yield break;
            }

            TranscriptionResponse response = JsonUtility.FromJson<TranscriptionResponse>(rawResponse);

            if (response != null && !string.IsNullOrWhiteSpace(response.text))
            {
                string transcribedText = response.text.Trim();

                if (transcribedText.Equals("you", StringComparison.OrdinalIgnoreCase))
                {
                    NotificationManager.Instance?.ShowWarning("Couldn't hear you — try again.");
                    AppStateManager.Instance?.SetState(AppState.Idle);
                    NotificationManager.Instance?.ClearStatus();
                    Debug.LogWarning("[VoiceCommandManager] Whisper returned only \"You\" — likely silence.");
                    yield break;
                }

                NotificationManager.Instance?.ClearStatus();
                ProcessCommandForLLM(transcribedText, currentAnchorPosition);
            }
            else
            {
                NotificationManager.Instance?.ShowWarning("Transcription returned empty — try again.");
                AppStateManager.Instance?.SetState(AppState.Idle);
                NotificationManager.Instance?.ClearStatus();
                Debug.LogWarning("[VoiceCommandManager] Whisper returned empty text.");
            }
        }
    }

    // -------------------------------------------------------------------------
    // LLM Interface Pipeline
    // -------------------------------------------------------------------------

    private void ProcessCommandForLLM(string transcribedText, Vector3 targetPosition)
    {
        Debug.Log($"[VoiceCommandManager] Transcribed: \"{transcribedText}\" at {targetPosition}");

        _allTranscriptions.Add(transcribedText);
        string fallbackText = "• " + string.Join("\n• ", _allTranscriptions);

        const string sectionKey = "Design";
        string previousSummary = MuseumDescriptionPanel.Instance?.GetSectionText(sectionKey);

        NotificationManager.Instance?.ShowStatus("Refining description with AI…");

        if (GeminiManager.Instance != null)
        {
            GeminiManager.Instance.SummarizeDescription(previousSummary, transcribedText, fallbackText, summary =>
            {
                MuseumDescriptionPanel.Instance?.AddOrUpdateSection(sectionKey, summary);
                AppStateManager.Instance?.SetState(AppState.ReviewingTranscription);
                NotificationManager.Instance?.ClearStatus();
            });
        }
        else
        {
            MuseumDescriptionPanel.Instance?.AddOrUpdateSection(sectionKey, fallbackText);
            AppStateManager.Instance?.SetState(AppState.ReviewingTranscription);
            NotificationManager.Instance?.ClearStatus();
        }
    }

    // -------------------------------------------------------------------------
    // Audio Conversion Utilities
    // -------------------------------------------------------------------------

    private byte[] ConvertAudioClipToWav(AudioClip clip, int recordedSamplePosition, out float rms, out float peak)
    {
        int channels = clip.channels;
        int sampleRate = clip.frequency;

        int sampleFrames = Mathf.Clamp(recordedSamplePosition, 0, clip.samples);
        int sampleCount = sampleFrames * channels;

        float[] samples = new float[sampleCount];
        clip.GetData(samples, 0);

        double sumSquares = 0.0;
        peak = 0f;

        for (int i = 0; i < samples.Length; i++)
        {
            float abs = Mathf.Abs(samples[i]);
            if (abs > peak)
            {
                peak = abs;
            }

            sumSquares += samples[i] * samples[i];
        }

        rms = samples.Length > 0 ? Mathf.Sqrt((float)(sumSquares / samples.Length)) : 0f;

        byte[] pcmData = ConvertFloatSamplesToPCM16(samples);
        byte[] wavHeader = CreateWavHeader(pcmData.Length, channels, sampleRate);

        byte[] wavData = new byte[wavHeader.Length + pcmData.Length];
        Buffer.BlockCopy(wavHeader, 0, wavData, 0, wavHeader.Length);
        Buffer.BlockCopy(pcmData, 0, wavData, wavHeader.Length, pcmData.Length);

        return wavData;
    }

    private byte[] ConvertFloatSamplesToPCM16(float[] samples)
    {
        byte[] pcmData = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            float clampedSample = Mathf.Clamp(samples[i], -1f, 1f);
            short intSample = (short)(clampedSample * short.MaxValue);
            byte[] sampleBytes = BitConverter.GetBytes(intSample);
            pcmData[i * 2] = sampleBytes[0];
            pcmData[i * 2 + 1] = sampleBytes[1];
        }
        return pcmData;
    }

    private byte[] CreateWavHeader(int pcmDataLength, int channels, int sampleRate)
    {
        int headerSize = 44;
        int fileSize = headerSize + pcmDataLength - 8;
        int bitsPerSample = 16;
        int byteRate = sampleRate * channels * bitsPerSample / 8;
        short blockAlign = (short)(channels * bitsPerSample / 8);

        using (MemoryStream stream = new MemoryStream(headerSize))
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(fileSize);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)channels);
            writer.Write(sampleRate);
            writer.Write(byteRate);
            writer.Write(blockAlign);
            writer.Write((short)bitsPerSample);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(pcmDataLength);
            return stream.ToArray();
        }
    }

    private void OnDestroy()
    {
        if (isRecording && !string.IsNullOrEmpty(microphoneDevice))
        {
            Microphone.End(microphoneDevice);
        }

        if (anchorManager != null)
        {
            anchorManager.OnVoiceRecordStart -= HandleVoiceRecordStart;
            anchorManager.OnVoiceRecordStop -= HandleVoiceRecordStop;
        }
    }
}
