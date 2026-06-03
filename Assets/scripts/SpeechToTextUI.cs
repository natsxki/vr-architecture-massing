using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Networking;

public class SpeechToTextUI : MonoBehaviour
{
    [Header("OpenAI Whisper API")]
    public string openAIApiKey = "YOUR_OPENAI_API_KEY_HERE";
    public string transcriptionModel = "whisper-1";
    public string language = "en";

    [Header("Recording Settings")]
    public int maxRecordingSeconds = 15;
    public int recordingSampleRate = 16000;

    [Header("UI Position")]
    public Transform cameraTransform;
    public Vector3 localOffset = new Vector3(0f, -0.15f, 1.4f);

    private GameObject canvasRoot;
    private Canvas canvas;

    private Text titleText;
    private Text positionText;
    private Text instructionText;
    private Text speechText;
    private Text statusText;

    private Button recordButton;
    private Button adjustButton;
    private Button confirmButton;

    private Button[] moduleButtons;

    private bool isRecording = false;
    private bool isUploading = false;
    private bool promptUIVisible = false;

    private Vector3 selectedPosition;

    private string finalText = "";
    private AudioClip recordedClip;
    private string microphoneDevice;

    private readonly string[] moduleNames =
    {
        "Entrance Hall",
        "Light Gallery",
        "Sound Gallery",
        "Caf¨¦"
    };

    private readonly string[] moduleKeys =
    {
        "entrance",
        "light_gallery",
        "sound_gallery",
        "cafe"
    };

    private readonly string[] moduleInstructions =
    {
        "Describe the entrance hall. Example: wide and low at the center.",
        "Describe the light gallery. Example: tall and narrow on the left.",
        "Describe the sound gallery. Example: quiet, enclosed, and deeper inside.",
        "Describe the caf¨¦. Example: small, open, near the entrance."
    };

    private string[] modulePrompts;
    private bool[] moduleCompleted;
    private int currentModuleIndex = 0;

    private const string TranscriptionUrl = "https://api.openai.com/v1/audio/transcriptions";

    [Serializable]
    private class TranscriptionResponse
    {
        public string text;
    }

    void Start()
    {
        if (cameraTransform == null)
        {
            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                cameraTransform = mainCam.transform;
            }
        }

        modulePrompts = new string[moduleNames.Length];
        moduleCompleted = new bool[moduleNames.Length];

        CreatePromptUI();
        HidePromptUI();

        InitMicrophone();
    }

    void Update()
    {
        UpdateUIPosition();

#if UNITY_EDITOR
        if (promptUIVisible)
        {
            if (Input.GetKeyDown(KeyCode.R))
            {
                ToggleRecording();
            }

            if (Input.GetKeyDown(KeyCode.C))
            {
                ConfirmFinalPrompt();
            }

            if (Input.GetKeyDown(KeyCode.A))
            {
                AdjustCurrentModule();
            }

            if (Input.GetKeyDown(KeyCode.Alpha1)) SelectModule(0);
            if (Input.GetKeyDown(KeyCode.Alpha2)) SelectModule(1);
            if (Input.GetKeyDown(KeyCode.Alpha3)) SelectModule(2);
            if (Input.GetKeyDown(KeyCode.Alpha4)) SelectModule(3);
        }
#endif
    }

    public void OpenPromptUI(Vector3 position)
    {
        selectedPosition = position;
        promptUIVisible = true;

        ResetPromptState();

        if (canvasRoot != null)
        {
            canvasRoot.SetActive(true);
        }

        positionText.text =
            $"Selected Position: ({selectedPosition.x:F2}, {selectedPosition.y:F2}, {selectedPosition.z:F2})";

        instructionText.text = moduleInstructions[currentModuleIndex];
        speechText.text = "Press Start Recording to describe the selected module.";
        statusText.text = "Prompt input mode started.";

        recordButton.interactable = true;
        adjustButton.gameObject.SetActive(false);
        confirmButton.gameObject.SetActive(false);

        UpdateModuleHighlight();
    }

    public void HidePromptUI()
    {
        promptUIVisible = false;

        if (canvasRoot != null)
        {
            canvasRoot.SetActive(false);
        }
    }

    private void ResetPromptState()
    {
        finalText = "";
        currentModuleIndex = 0;

        for (int i = 0; i < moduleNames.Length; i++)
        {
            modulePrompts[i] = "";
            moduleCompleted[i] = false;
        }
    }

    private void UpdateUIPosition()
    {
        if (!promptUIVisible || cameraTransform == null || canvas == null)
        {
            return;
        }

        canvas.transform.position = cameraTransform.TransformPoint(localOffset);
        canvas.transform.rotation = Quaternion.LookRotation(canvas.transform.position - cameraTransform.position);
    }

    private void SelectModule(int index)
    {
        currentModuleIndex = index;

        instructionText.text = moduleInstructions[index];

        if (moduleCompleted[index])
        {
            speechText.text = modulePrompts[index];
            statusText.text = $"Selected module: {moduleNames[index]}. Press Adjust to re-record.";
        }
        else
        {
            speechText.text = "Press Start Recording to describe this module.";
            statusText.text = $"Selected module: {moduleNames[index]}.";
        }

        UpdateModuleHighlight();
    }

    private void AdjustCurrentModule()
    {
        modulePrompts[currentModuleIndex] = "";
        moduleCompleted[currentModuleIndex] = false;

        instructionText.text = moduleInstructions[currentModuleIndex];
        speechText.text = "Press Start Recording to re-record this module.";
        statusText.text = $"Adjust mode: {moduleNames[currentModuleIndex]}.";

        recordButton.interactable = true;
        adjustButton.gameObject.SetActive(false);
        confirmButton.gameObject.SetActive(false);

        UpdateModuleHighlight();
    }

    private void OnModuleTranscriptionComplete(string text)
    {
        modulePrompts[currentModuleIndex] = text;
        moduleCompleted[currentModuleIndex] = true;

        if (AllModulesCompleted())
        {
            finalText = BuildFinalPrompt();

            speechText.text = finalText;
            instructionText.text = "Review the complete prompt.";
            statusText.text = "All modules completed. Use Adjust or Confirm.";

            recordButton.interactable = false;
            adjustButton.gameObject.SetActive(true);
            confirmButton.gameObject.SetActive(true);
        }
        else
        {
            currentModuleIndex = GetNextIncompleteModuleIndex();

            instructionText.text = moduleInstructions[currentModuleIndex];
            speechText.text = "Press Start Recording to describe this module.";
            statusText.text = $"Next module: {moduleNames[currentModuleIndex]}.";

            recordButton.interactable = true;
        }

        UpdateModuleHighlight();
    }

    private bool AllModulesCompleted()
    {
        for (int i = 0; i < moduleCompleted.Length; i++)
        {
            if (!moduleCompleted[i])
            {
                return false;
            }
        }

        return true;
    }

    private int GetNextIncompleteModuleIndex()
    {
        for (int i = 0; i < moduleCompleted.Length; i++)
        {
            if (!moduleCompleted[i])
            {
                return i;
            }
        }

        return currentModuleIndex;
    }

    private string BuildFinalPrompt()
    {
        string prompt =
            "Design a small sensory museum with four spaces. " +
            "Use simple rectangular volumes only. " +
            "Each space should be visualized as simple colored boxes. " +
            $"The selected generation position is ({selectedPosition.x:F2}, {selectedPosition.y:F2}, {selectedPosition.z:F2}).\n\n";

        for (int i = 0; i < moduleNames.Length; i++)
        {
            prompt += $"{moduleNames[i]} ({moduleKeys[i]}): {modulePrompts[i]}\n";
        }

        return prompt;
    }

    private void ConfirmFinalPrompt()
    {
        if (!AllModulesCompleted())
        {
            statusText.text = "Please complete all four modules first.";
            return;
        }

        finalText = BuildFinalPrompt();

        statusText.text = "Text has been sent to LLM for processing.";
        speechText.text = finalText;

        Debug.Log("Confirmed complete prompt:");
        Debug.Log(finalText);

        // Later:
        // StartCoroutine(SendPromptToLLM(finalText));
    }

    private void InitMicrophone()
    {
        if (Microphone.devices.Length == 0)
        {
            Debug.LogWarning("No microphone detected.");
            return;
        }

        microphoneDevice = Microphone.devices[0];
        Debug.Log("Using microphone: " + microphoneDevice);
    }

    private void ToggleRecording()
    {
        if (isUploading)
        {
            statusText.text = "Please wait. Audio is being transcribed.";
            return;
        }

        if (!isRecording)
        {
            StartCoroutine(StartRecording());
        }
        else
        {
            StopRecordingAndTranscribe();
        }
    }

    private IEnumerator StartRecording()
    {
        if (string.IsNullOrEmpty(openAIApiKey) || openAIApiKey == "YOUR_OPENAI_API_KEY_HERE")
        {
            statusText.text = "Please set your OpenAI API key in the Inspector.";
            yield break;
        }

        if (Microphone.devices.Length == 0)
        {
            statusText.text = "No microphone detected.";
            yield break;
        }

#if UNITY_ANDROID
        if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
        {
            yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);
        }

        if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
        {
            statusText.text = "Microphone permission denied.";
            yield break;
        }
#endif

        speechText.text = $"Recording {moduleNames[currentModuleIndex]}...";
        statusText.text = "Speak now.";

        recordButton.GetComponentInChildren<Text>().text = "Stop Recording";
        adjustButton.gameObject.SetActive(false);
        confirmButton.gameObject.SetActive(false);

        recordedClip = Microphone.Start(
            microphoneDevice,
            false,
            maxRecordingSeconds,
            recordingSampleRate
        );

        isRecording = true;
    }

    private void StopRecordingAndTranscribe()
    {
        if (!isRecording)
        {
            return;
        }

        int recordingPosition = Microphone.GetPosition(microphoneDevice);

        Microphone.End(microphoneDevice);

        isRecording = false;
        recordButton.GetComponentInChildren<Text>().text = "Start Recording";

        if (recordedClip == null || recordingPosition <= 0)
        {
            statusText.text = "No audio recorded.";
            speechText.text = "Please try recording again.";
            return;
        }

        statusText.text = $"Transcribing {moduleNames[currentModuleIndex]}...";
        speechText.text = "Sending audio to Whisper API...";

        byte[] wavData = ConvertAudioClipToWav(recordedClip, recordingPosition);
        StartCoroutine(SendAudioToWhisper(wavData));
    }

    private IEnumerator SendAudioToWhisper(byte[] wavData)
    {
        isUploading = true;
        recordButton.interactable = false;

        List<IMultipartFormSection> formData = new List<IMultipartFormSection>();

        formData.Add(new MultipartFormDataSection("model", transcriptionModel));
        formData.Add(new MultipartFormDataSection("language", language));
        formData.Add(new MultipartFormDataSection("response_format", "json"));

        formData.Add(
            new MultipartFormFileSection(
                "file",
                wavData,
                "recording.wav",
                "audio/wav"
            )
        );

        using (UnityWebRequest request = UnityWebRequest.Post(TranscriptionUrl, formData))
        {
            request.SetRequestHeader("Authorization", "Bearer " + openAIApiKey);

            yield return request.SendWebRequest();

            isUploading = false;
            recordButton.interactable = true;

            if (request.result != UnityWebRequest.Result.Success)
            {
                statusText.text = "Whisper API request failed.";
                speechText.text = request.error;

                Debug.LogError("Whisper API error: " + request.error);
                Debug.LogError("Response: " + request.downloadHandler.text);
                yield break;
            }

            string json = request.downloadHandler.text;
            TranscriptionResponse response = JsonUtility.FromJson<TranscriptionResponse>(json);

            if (response == null || string.IsNullOrWhiteSpace(response.text))
            {
                statusText.text = "No text was recognized.";
                speechText.text = "Please try again.";
                yield break;
            }

            string recognizedText = response.text.Trim();

            speechText.text = recognizedText;
            statusText.text = $"{moduleNames[currentModuleIndex]} recognized.";

            OnModuleTranscriptionComplete(recognizedText);
        }
    }

    private byte[] ConvertAudioClipToWav(AudioClip clip, int recordedSamplePosition)
    {
        int channels = clip.channels;
        int sampleRate = clip.frequency;
        int sampleCount = recordedSamplePosition * channels;

        float[] samples = new float[clip.samples * channels];
        clip.GetData(samples, 0);

        float[] trimmedSamples = new float[sampleCount];
        Array.Copy(samples, trimmedSamples, sampleCount);

        byte[] pcmData = ConvertFloatSamplesToPCM16(trimmedSamples);
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
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(fileSize);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

            writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)channels);
            writer.Write(sampleRate);
            writer.Write(byteRate);
            writer.Write(blockAlign);
            writer.Write((short)bitsPerSample);

            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(pcmDataLength);

            return stream.ToArray();
        }
    }

    private void CreatePromptUI()
    {
        canvasRoot = new GameObject("Prompt Input World UI");
        canvasRoot.transform.SetParent(transform);

        canvas = canvasRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 10;

        Camera eventCam = GetEventCamera();
        if (eventCam != null)
        {
            canvas.worldCamera = eventCam;
        }

        CanvasScaler scaler = canvasRoot.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10;

        canvasRoot.AddComponent<GraphicRaycaster>();

        RectTransform canvasRect = canvas.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(900, 520);
        canvasRect.localScale = new Vector3(0.0018f, 0.0018f, 0.0018f);

        Image bg = canvasRoot.AddComponent<Image>();
        bg.color = new Color(0.05f, 0.05f, 0.05f, 0.82f);
        bg.raycastTarget = false;

        titleText = CreateText(
            "Title",
            canvasRoot.transform,
            "Museum Prompt Input",
            30,
            new Vector2(0, 220),
            new Vector2(820, 50)
        );

        positionText = CreateText(
            "Position Text",
            canvasRoot.transform,
            "Selected Position:",
            18,
            new Vector2(0, 175),
            new Vector2(820, 35)
        );

        instructionText = CreateText(
            "Instruction Text",
            canvasRoot.transform,
            "Describe the current module.",
            18,
            new Vector2(0, 135),
            new Vector2(820, 40)
        );

        speechText = CreateText(
            "Speech Text",
            canvasRoot.transform,
            "Press Start Recording.",
            20,
            new Vector2(0, 45),
            new Vector2(620, 120)
        );

        statusText = CreateText(
            "Status Text",
            canvasRoot.transform,
            "Ready.",
            17,
            new Vector2(0, -45),
            new Vector2(620, 40)
        );

        CreateModuleButtons(canvasRoot.transform);

        recordButton = CreateButton(
            "Record Button",
            canvasRoot.transform,
            "Start Recording",
            new Vector2(-50, -190),
            new Vector2(220, 55)
        );

        adjustButton = CreateButton(
            "Adjust Button",
            canvasRoot.transform,
            "Adjust",
            new Vector2(360, -40),
            new Vector2(150, 55)
        );

        confirmButton = CreateButton(
            "Confirm Button",
            canvasRoot.transform,
            "Confirm",
            new Vector2(360, -110),
            new Vector2(150, 55)
        );

        recordButton.onClick.AddListener(ToggleRecording);
        adjustButton.onClick.AddListener(AdjustCurrentModule);
        confirmButton.onClick.AddListener(ConfirmFinalPrompt);

        EnsureEventSystemExists();
    }

    private void CreateModuleButtons(Transform parent)
    {
        moduleButtons = new Button[moduleNames.Length];

        float startY = 70f;
        float stepY = -55f;

        for (int i = 0; i < moduleNames.Length; i++)
        {
            int index = i;

            Button btn = CreateButton(
                "Module Button " + moduleNames[i],
                parent,
                moduleNames[i],
                new Vector2(-360, startY + stepY * i),
                new Vector2(170, 45)
            );

            btn.onClick.AddListener(() => SelectModule(index));
            moduleButtons[i] = btn;
        }
    }

    private Text CreateText(string name, Transform parent, string content, int fontSize, Vector2 anchoredPos, Vector2 size)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);

        Text text = obj.AddComponent<Text>();
        text.text = content;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;

        RectTransform rect = text.GetComponent<RectTransform>();
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;

        return text;
    }

    private Button CreateButton(string name, Transform parent, string label, Vector2 anchoredPos, Vector2 size)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);

        Image image = obj.AddComponent<Image>();
        image.color = new Color(0.2f, 0.45f, 0.85f, 1f);
        image.raycastTarget = true;

        Button button = obj.AddComponent<Button>();

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;

        Text btnText = CreateText("Text", obj.transform, label, 18, Vector2.zero, size);
        btnText.color = Color.white;
        btnText.raycastTarget = false;

        return button;
    }

    private void UpdateModuleHighlight()
    {
        if (moduleButtons == null)
        {
            return;
        }

        for (int i = 0; i < moduleButtons.Length; i++)
        {
            Image img = moduleButtons[i].GetComponent<Image>();

            if (i == currentModuleIndex)
            {
                img.color = new Color(1f, 0.65f, 0.1f, 1f);
            }
            else if (moduleCompleted[i])
            {
                img.color = new Color(0.15f, 0.6f, 0.25f, 1f);
            }
            else
            {
                img.color = new Color(0.2f, 0.45f, 0.85f, 1f);
            }
        }
    }

    private Camera GetEventCamera()
    {
        Camera cam = null;

        if (cameraTransform != null)
        {
            cam = cameraTransform.GetComponent<Camera>();

            if (cam == null)
            {
                cam = cameraTransform.GetComponentInChildren<Camera>();
            }
        }

        if (cam == null)
        {
            cam = Camera.main;
        }

        return cam;
    }

    private void EnsureEventSystemExists()
    {
        EventSystem eventSystem = FindObjectOfType<EventSystem>();

        if (eventSystem == null)
        {
            GameObject eventSystemObj = new GameObject("EventSystem");
            eventSystemObj.AddComponent<EventSystem>();
            eventSystemObj.AddComponent<StandaloneInputModule>();
        }
        else
        {
            if (eventSystem.GetComponent<StandaloneInputModule>() == null)
            {
                eventSystem.gameObject.AddComponent<StandaloneInputModule>();
            }
        }
    }

    private void OnDestroy()
    {
        if (isRecording && !string.IsNullOrEmpty(microphoneDevice))
        {
            Microphone.End(microphoneDevice);
        }
    }
}