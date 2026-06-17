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
    [Header("VR Interaction Link")]
    [Tooltip("Reference to the script handling VR controller inputs. Will auto-find if left null.")]
    public GroundAnchorManager anchorManager;

    [Header("OpenAI Whisper Settings")]
    // public string openAIApiKey = "YOUR_OPENAI_API_KEY_HERE";
    public string transcriptionModel = "whisper-1";
    public string language = "en"; 

    [Header("Recording Settings")]
    public int maxRecordingSeconds = 30;

    [Header("Microphone Debug")]
    public int preferredMicrophoneIndex = 0;
    public float minRecordingSeconds = 0.8f;
    public float silenceRmsThreshold = 0.003f;
    public bool saveDebugWav = true;

    private float recordingStartTime;

    // Internal state
    private const int recordingSampleRate = 48000;
    private bool isRecording = false;
    private bool isUploading = false;
    private AudioClip recordedClip;
    private string microphoneDevice;
    private Vector3 currentAnchorPosition;

    private const string TranscriptionUrl = "https://api.openai.com/v1/audio/transcriptions";

    [Serializable]
    private class TranscriptionResponse
    {
        public string text;
    }
    public static string LoadOpenAIApiKey()
    {
        string path = Path.Combine(Application.dataPath, "LocalSecrets/whisper_key.txt");

        if (!File.Exists(path))
        {
            Debug.LogError("API key file not found: " + path);
            return null;
        }

        string apiKey = File.ReadAllText(path).Trim();

        if (string.IsNullOrEmpty(apiKey))
        {
            Debug.LogError("API key file is empty.");
            return null;
        }

        return apiKey;
    }

    string openAIApiKey = LoadOpenAIApiKey();
    
    private void Start()
    {
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
            Debug.LogWarning("[VoiceCommandManager] Currently transcribing, please wait...");
            return;
        }

        if (string.IsNullOrEmpty(openAIApiKey) || openAIApiKey == "YOUR_OPENAI_API_KEY_HERE")
        {
            Debug.LogError("[VoiceCommandManager] OpenAI API Key is missing!");
            return;
        }

        if (!isRecording && !string.IsNullOrEmpty(microphoneDevice))
        {
            Debug.Log("<color=yellow>[VoiceCommandManager] Recording Started...</color>");
            recordedClip = Microphone.Start(
            microphoneDevice,
            false,
            maxRecordingSeconds,
            recordingSampleRate
        );

        recordingStartTime = Time.realtimeSinceStartup;
        isRecording = true;

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
            Debug.LogWarning($"[VoiceCommandManager] Audio is probably silent. RMS={rms:F6}, Peak={peak:F6}. Not sending to Whisper.");
            return;
        }

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
                Debug.LogError($"[VoiceCommandManager] Whisper API Error: {request.error}");
                Debug.LogError($"[VoiceCommandManager] Response: {rawResponse}");
                yield break;
            }

            TranscriptionResponse response = JsonUtility.FromJson<TranscriptionResponse>(rawResponse);

            if (response != null && !string.IsNullOrWhiteSpace(response.text))
            {
                string transcribedText = response.text.Trim();

                if (transcribedText.Equals("you", StringComparison.OrdinalIgnoreCase))
                {
                    Debug.LogWarning("[VoiceCommandManager] Whisper returned only \"You\". This usually indicates silence, very short audio, or wrong microphone input.");
                }

                ProcessCommandForLLM(transcribedText, currentAnchorPosition);
            }
            else
            {
                Debug.LogWarning("[VoiceCommandManager] Whisper returned empty text.");
            }
        }
    }

    // -------------------------------------------------------------------------
    // LLM Interface Pipeline
    // -------------------------------------------------------------------------

    /// <summary>
    /// This is the entry point for your future LLM integration.
    /// </summary>
    private void ProcessCommandForLLM(string transcribedText, Vector3 targetPosition)
    {
        Debug.Log("\n==================================================");
        Debug.Log("<color=cyan><b>[READY FOR LLM]</b></color>");
        Debug.Log($"<b>Transcribed Text:</b> \"{transcribedText}\"");
        Debug.Log($"<b>Target Coordinate:</b> X:{targetPosition.x:F2}, Y:{targetPosition.y:F2}, Z:{targetPosition.z:F2}");
        Debug.Log("==================================================\n");

        // TODO: In the next step, you will call your Gemini API script from here.
        // Example: llmManager.SendPrompt(transcribedText, targetPosition);
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