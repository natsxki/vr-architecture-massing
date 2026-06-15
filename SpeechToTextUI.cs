using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
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

    [Header("LLM API")]
    public string llmApiKey = "YOUR_LLM_API_KEY_HERE";

    [Tooltip("For DeepSeek OpenAI-compatible API, use: https://api.deepseek.com/chat/completions")]
    public string llmApiUrl = "https://api.deepseek.com/chat/completions";

    [Tooltip("For DeepSeek, use deepseek-chat. For other providers, replace this with the correct model name.")]
    public string llmModel = "deepseek-chat";

    [Header("Massing Generation")]
    public MassingGenerator massingGenerator;

    [Header("Recording Settings")]
    public int maxRecordingSeconds = 15;
    public int recordingSampleRate = 16000;

    [Header("UI Position")]
    public Transform cameraTransform;
    public Vector3 localOffset = new Vector3(0f, -0.15f, 1.4f);

    [Header("Finish Behavior")]
    public bool hideUIAfterAllSpacesGenerated = true;
    public float hideUIDelaySeconds = 1.2f;

    private GameObject canvasRoot;
    private Canvas canvas;

    private Text titleText;
    private Text positionText;
    private Text instructionText;
    private Text speechText;
    private Text statusText;

    private Button recordButton;
    private Button adjustButton;
    private Button relationButton;
    private Button finishButton;
    private Button[] moduleButtons;

    private bool isRecording = false;
    private bool isUploading = false;
    private bool isSendingToLLM = false;
    private bool promptUIVisible = false;

    private Vector3 selectedPosition;

    private AudioClip recordedClip;
    private string microphoneDevice;

    private readonly string[] moduleNames =
    {
        "Entrance Hall",
        "Light Gallery",
        "Sound Gallery",
        "Cafe"
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
        "Describe the sound gallery. Example: enclosed, and deeper inside.",
        "Describe the cafe. Example: small, open, near the entrance."
    };

    private string[] modulePrompts;
    private bool[] moduleCompleted;
    private string[] moduleRelations;
    private int currentModuleIndex = 0;

    private const string TranscriptionUrl = "https://api.openai.com/v1/audio/transcriptions";

    [Serializable]
    private class TranscriptionResponse
    {
        public string text;
    }

    [Serializable]
    private class ChatCompletionRequest
    {
        public string model;
        public ChatMessage[] messages;
        public ResponseFormat response_format;
        public int max_tokens;
        public float temperature;
        public bool stream;
    }

    [Serializable]
    private class ChatMessage
    {
        public string role;
        public string content;

        public ChatMessage(string role, string content)
        {
            this.role = role;
            this.content = content;
        }
    }

    [Serializable]
    private class ResponseFormat
    {
        public string type;

        public ResponseFormat(string type)
        {
            this.type = type;
        }
    }

    [Serializable]
    private class ChatCompletionResponse
    {
        public Choice[] choices;
    }

    [Serializable]
    private class Choice
    {
        public ChatMessage message;
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

        if (massingGenerator == null)
        {
            massingGenerator = FindObjectOfType<MassingGenerator>();
        }

        modulePrompts = new string[moduleNames.Length];
        moduleCompleted = new bool[moduleNames.Length];
        moduleRelations = new string[moduleNames.Length];

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
            if (Input.GetKeyDown(KeyCode.R)) ToggleRecording();
            if (Input.GetKeyDown(KeyCode.A)) AdjustCurrentModule();
            if (Input.GetKeyDown(KeyCode.F)) FinishAndGeneratePassages();
            if (Input.GetKeyDown(KeyCode.T)) ToggleCurrentRelation();
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

        if (massingGenerator == null)
        {
            massingGenerator = FindObjectOfType<MassingGenerator>();
        }

        if (massingGenerator != null)
        {
            massingGenerator.BeginIncrementalGeneration(selectedPosition);
        }

        if (canvasRoot != null)
        {
            canvasRoot.SetActive(true);
        }

        positionText.text = $"Selected Position: ({selectedPosition.x:F2}, {selectedPosition.y:F2}, {selectedPosition.z:F2})";
        instructionText.text = moduleInstructions[currentModuleIndex];
        speechText.text = "Press Start Recording to describe the current space. After transcription, this space will be generated immediately.";
        statusText.text = "Step-by-step prompt input mode started.";

        recordButton.interactable = true;
        adjustButton.gameObject.SetActive(false);
        finishButton.gameObject.SetActive(false);

        UpdateRelationButton();
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

    private IEnumerator HidePromptUIAfterDelay()
    {
        yield return new WaitForSeconds(hideUIDelaySeconds);
        HidePromptUI();
    }

    private void ResetPromptState()
    {
        currentModuleIndex = 0;
        isSendingToLLM = false;

        for (int i = 0; i < moduleNames.Length; i++)
        {
            modulePrompts[i] = "";
            moduleCompleted[i] = false;
            moduleRelations[i] = i == 0 ? "none" : "connected";
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
        if (index < 0 || index >= moduleNames.Length)
        {
            return;
        }

        currentModuleIndex = index;
        instructionText.text = moduleInstructions[index];

        if (moduleCompleted[index])
        {
            speechText.text = modulePrompts[index];
            statusText.text = $"Selected module: {moduleNames[index]}. Press Adjust to re-record and regenerate from this module.";
        }
        else
        {
            speechText.text = "Press Start Recording to describe this module.";
            statusText.text = $"Selected module: {moduleNames[index]}.";
        }

        UpdateRelationButton();
        UpdateModuleHighlight();
    }

    private void ToggleCurrentRelation()
    {
        if (currentModuleIndex == 0)
        {
            moduleRelations[currentModuleIndex] = "none";
            UpdateRelationButton();
            return;
        }

        moduleRelations[currentModuleIndex] =
            moduleRelations[currentModuleIndex] == "connected" ? "separated" : "connected";

        UpdateRelationButton();
    }

    private void UpdateRelationButton()
    {
        if (relationButton == null)
        {
            return;
        }

        Text text = relationButton.GetComponentInChildren<Text>();

        if (currentModuleIndex == 0)
        {
            relationButton.interactable = false;
            text.text = "Relation: None";
        }
        else
        {
            relationButton.interactable = !isUploading && !isSendingToLLM;
            text.text = moduleRelations[currentModuleIndex] == "separated"
                ? "Relation: Separated"
                : "Relation: Connected";
        }
    }

    private void AdjustCurrentModule()
    {
        if (isSendingToLLM || isUploading)
        {
            statusText.text = "Please wait. The system is processing the current instruction.";
            return;
        }

        // For simple classroom demo: adjusting starts the whole generation again from this selected position.
        // This avoids keeping outdated downstream spaces and passages.
        ResetPromptState();

        if (massingGenerator != null)
        {
            massingGenerator.BeginIncrementalGeneration(selectedPosition);
        }

        instructionText.text = moduleInstructions[currentModuleIndex];
        speechText.text = "Adjustment mode: start again from the entrance hall.";
        statusText.text = "Previous preview cleared. Re-record the spaces one by one.";

        recordButton.interactable = true;
        adjustButton.gameObject.SetActive(false);
        finishButton.gameObject.SetActive(false);
        UpdateRelationButton();
        UpdateModuleHighlight();
    }

    private void OnModuleTranscriptionComplete(string text)
    {
        modulePrompts[currentModuleIndex] = text;
        moduleCompleted[currentModuleIndex] = true;

        string relation = moduleRelations[currentModuleIndex];
        string moduleKey = moduleKeys[currentModuleIndex];
        string moduleName = moduleNames[currentModuleIndex];

        statusText.text = $"Generating {moduleName} preview...";
        speechText.text = text;

        StartCoroutine(SendSingleModuleToLLM(currentModuleIndex, text, relation, moduleKey));
    }

    private void OnSingleModuleGenerated(int generatedModuleIndex)
    {
        if (AllModulesCompleted())
        {
            if (massingGenerator != null)
            {
                massingGenerator.GeneratePassagesAfterAllSpaces();
            }

            instructionText.text = "All four spaces have been generated.";
            statusText.text = "All spaces generated. Closing the input panel.";
            recordButton.interactable = false;
            adjustButton.gameObject.SetActive(false);
            finishButton.gameObject.SetActive(false);
            UpdateModuleHighlight();

            if (hideUIAfterAllSpacesGenerated)
            {
                StartCoroutine(HidePromptUIAfterDelay());
            }

            return;
        }

        currentModuleIndex = GetNextIncompleteModuleIndex();
        instructionText.text = moduleInstructions[currentModuleIndex];
        speechText.text = "Preview generated. Now describe the next space.";
        statusText.text = $"Next module: {moduleNames[currentModuleIndex]}. Choose relation first if needed.";

        recordButton.interactable = true;
        adjustButton.gameObject.SetActive(true);
        finishButton.gameObject.SetActive(false);

        UpdateRelationButton();
        UpdateModuleHighlight();
    }

    private bool AllModulesCompleted()
    {
        for (int i = 0; i < moduleCompleted.Length; i++)
        {
            if (!moduleCompleted[i]) return false;
        }
        return true;
    }

    private int GetNextIncompleteModuleIndex()
    {
        for (int i = 0; i < moduleCompleted.Length; i++)
        {
            if (!moduleCompleted[i]) return i;
        }
        return currentModuleIndex;
    }

    private void FinishAndGeneratePassages()
    {
        if (!AllModulesCompleted())
        {
            statusText.text = "Please complete all four spaces first.";
            return;
        }

        if (massingGenerator != null)
        {
            massingGenerator.GeneratePassagesAfterAllSpaces();
            statusText.text = "Passages regenerated.";
        }
    }

    private IEnumerator SendSingleModuleToLLM(int moduleIndex, string userText, string relation, string expectedLabel)
    {
        if (string.IsNullOrEmpty(llmApiKey) || llmApiKey == "YOUR_LLM_API_KEY_HERE")
        {
            statusText.text = "Please set your LLM API key in the Inspector.";
            yield break;
        }

        if (massingGenerator == null)
        {
            massingGenerator = FindObjectOfType<MassingGenerator>();
        }

        if (massingGenerator == null)
        {
            statusText.text = "MassingGenerator not found in the scene.";
            yield break;
        }

        isSendingToLLM = true;
        recordButton.interactable = false;
        adjustButton.interactable = false;
        relationButton.interactable = false;
        finishButton.interactable = false;

        string systemPrompt = BuildSingleModuleSystemPrompt(expectedLabel, relation);
        string userPrompt = BuildSingleModuleUserPrompt(moduleIndex, userText, relation);

        ChatCompletionRequest requestBody = new ChatCompletionRequest
        {
            model = llmModel,
            messages = new ChatMessage[]
            {
                new ChatMessage("system", systemPrompt),
                new ChatMessage("user", userPrompt)
            },
            response_format = new ResponseFormat("json_object"),
            max_tokens = 500,
            temperature = 0.1f,
            stream = false
        };

        string jsonBody = JsonUtility.ToJson(requestBody);

        using (UnityWebRequest request = new UnityWebRequest(llmApiUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + llmApiKey);

            yield return request.SendWebRequest();

            isSendingToLLM = false;
            adjustButton.interactable = true;
            finishButton.interactable = true;

            if (request.result != UnityWebRequest.Result.Success)
            {
                statusText.text = "LLM request failed. You can record this module again.";
                speechText.text = request.responseCode + " " + request.error;

                Debug.LogError("LLM request error: " + request.error);
                Debug.LogError("LLM response code: " + request.responseCode);
                Debug.LogError("LLM response: " + request.downloadHandler.text);

                // Mark this module as not completed because the massing was not generated.
                moduleCompleted[moduleIndex] = false;

                // Restore UI state so the user can try again.
                isSendingToLLM = false;
                recordButton.interactable = true;
                adjustButton.interactable = true;
                finishButton.interactable = AllModulesCompleted();

                if (adjustButton != null)
                {
                    adjustButton.gameObject.SetActive(true);
                }

                if (finishButton != null)
                {
                    finishButton.gameObject.SetActive(AllModulesCompleted());
                }

                UpdateRelationButton();
                UpdateModuleHighlight();

                yield break;
            }

            string responseText = request.downloadHandler.text;
            ChatCompletionResponse response = JsonUtility.FromJson<ChatCompletionResponse>(responseText);

            if (response == null || response.choices == null || response.choices.Length == 0 ||
                response.choices[0].message == null || string.IsNullOrWhiteSpace(response.choices[0].message.content))
            {
                statusText.text = "LLM returned empty content. You can record this module again.";
                speechText.text = responseText;

                moduleCompleted[moduleIndex] = false;

                isSendingToLLM = false;
                recordButton.interactable = true;
                adjustButton.interactable = true;
                finishButton.interactable = AllModulesCompleted();

                if (adjustButton != null)
                {
                    adjustButton.gameObject.SetActive(true);
                }

                if (finishButton != null)
                {
                    finishButton.gameObject.SetActive(AllModulesCompleted());
                }

                UpdateRelationButton();
                UpdateModuleHighlight();

                yield break;
            }

            Debug.Log("Raw LLM content:");
            Debug.Log(response.choices[0].message.content);

            string massingJson = ExtractJsonObject(response.choices[0].message.content);
            Debug.Log("Single massing JSON:");
            Debug.Log(massingJson);

            massingGenerator.GenerateSingleFromJson(massingJson, expectedLabel, relation, selectedPosition);
            statusText.text = moduleNames[moduleIndex] + " preview generated.";
            speechText.text = massingJson;

            OnSingleModuleGenerated(moduleIndex);
        }
    }

    private string BuildSingleModuleUserPrompt(int moduleIndex, string userText, string relation)
    {
        string prompt =
            "Current required label: " + moduleKeys[moduleIndex] + "\n" +
            "Current space name: " + moduleNames[moduleIndex] + "\n" +
            "Relation to previous space: " + relation + "\n" +
            "User description: " + userText + "\n\n" +

            "Important instruction:\n" +
            "Even if the user description is abstract, emotional, vague, incomplete, or not directly geometric, " +
            "you must infer a reasonable architectural box using the semantic mapping rules from the system prompt. " +
            "Return only valid JSON.\n\n" +

            "Existing spaces already generated:\n";

        for (int i = 0; i < moduleIndex; i++)
        {
            prompt += "- " + moduleKeys[i] + ": " + modulePrompts[i] + "\n";
        }

        if (moduleIndex == 0)
        {
            prompt += "- none\n";
        }

        return prompt;
    }

    private string BuildSingleModuleSystemPrompt(string expectedLabel, string relation)
    {
        return
            "You are a robust architectural massing JSON generator for a VR prototype.\n\n" +

            "Your task:\n" +
            "Convert the user's verbal design description into ONE simple rectangular architectural volume.\n" +
            "The user may use abstract, emotional, atmospheric, or incomplete words. You must always infer reasonable massing dimensions from them.\n\n" +

            "Output rules:\n" +
            "1. Output ONLY one valid JSON object.\n" +
            "2. Do not output markdown.\n" +
            "3. Do not output explanation.\n" +
            "4. Do not use comments.\n" +
            "5. Do not use trailing commas.\n" +
            "6. All string values must use double quotes.\n" +
            "7. All numeric values must be numbers, not strings.\n" +
            "8. If the user description is vague, still generate a reasonable default volume.\n\n" +

            "Required JSON schema:\n" +
            "{\"label\":\"" + expectedLabel + "\",\"x\":0,\"y\":0,\"z\":0,\"w\":10,\"d\":8,\"h\":5,\"relation\":\"" + relation + "\"}\n\n" +

            "Hard constraints:\n" +
            "1. The label must be exactly \"" + expectedLabel + "\".\n" +
            "2. The relation must be exactly \"" + relation + "\".\n" +
            "3. Use meters as units.\n" +
            "4. Use only one rectangular box.\n" +
            "5. Keep y = 0.\n" +
            "6. Width w maps to x size.\n" +
            "7. Depth d maps to z size.\n" +
            "8. Height h maps to y size.\n" +
            "9. Keep x between -18 and 18.\n" +
            "10. Keep z between -18 and 18.\n" +
            "11. Keep w between 4 and 18.\n" +
            "12. Keep d between 4 and 18.\n" +
            "13. Keep h between 3 and 16.\n\n" +

            "Spatial vocabulary mapping:\n" +
            "- left / western / side = negative x.\n" +
            "- right / eastern / opposite side = positive x.\n" +
            "- front / entrance side / before = negative z.\n" +
            "- back / rear / deep / inside / hidden = positive z.\n" +
            "- center / central / main = x near 0 and z near 0.\n" +
            "- near entrance = close to x 0, z 0.\n" +
            "- far / isolated / separated / remote = larger absolute x or z.\n\n" +

            "Atmosphere-to-massing mapping:\n" +
            "- open / public / welcoming / social / active: wider w, shallower d, lower or medium h.\n" +
            "- calm / quiet / intimate / private / enclosed: smaller w, deeper d, lower or medium h.\n" +
            "- mysterious / immersive / dark / hidden: deeper z position, deeper d, medium or tall h, more enclosed proportions.\n" +
            "- bright / luminous / spiritual / vertical / impressive: taller h, narrower w, medium d.\n" +
            "- flexible / multi-use / neutral: medium w, medium d, medium h.\n" +
            "- compressed / narrow / tunnel-like: small w, large d, low or medium h.\n" +
            "- monumental / grand / dramatic: large w, large h, medium or large d.\n" +
            "- relaxing / casual / comfortable: medium w, medium d, low h.\n\n" +

            "Program-specific defaults:\n" +
            "- entrance: usually wide, low, public, and close to the center. Good default: w=14,d=9,h=4,x=0,z=0.\n" +
            "- light_gallery: usually bright, tall, narrow, and visually open. Good default: w=6,d=8,h=12,x=-8,z=4.\n" +
            "- sound_gallery: usually quiet, enclosed, immersive, and deeper inside. Good default: w=8,d=10,h=6,x=8,z=8.\n" +
            "- cafe: usually small or medium, social, open, and near the entrance. Good default: w=8,d=6,h=4,x=0,z=-8.\n\n" +

            "Decision process you must follow silently:\n" +
            "1. Identify the current program type from the required label.\n" +
            "2. Extract any spatial words from the user description.\n" +
            "3. Extract any atmosphere words from the user description.\n" +
            "4. Convert atmosphere words into width, depth, and height.\n" +
            "5. Convert spatial words into x and z direction.\n" +
            "6. If information is missing, use the program-specific default.\n" +
            "7. Return only the final JSON object.\n\n" +

            "Example for vague input:\n" +
            "User description: a mysterious immersive space\n" +
            "Valid output:\n" +
            "{\"label\":\"" + expectedLabel + "\",\"x\":8,\"y\":0,\"z\":10,\"w\":8,\"d\":12,\"h\":7,\"relation\":\"" + relation + "\"}\n\n" +

            "Example for abstract input:\n" +
            "User description: a bright spiritual room\n" +
            "Valid output:\n" +
            "{\"label\":\"" + expectedLabel + "\",\"x\":-8,\"y\":0,\"z\":4,\"w\":6,\"d\":8,\"h\":13,\"relation\":\"" + relation + "\"}\n\n" +

            "Now generate the JSON object.";
    }

    private string ExtractJsonObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        string cleaned = text.Trim();
        cleaned = cleaned.Replace("```json", "");
        cleaned = cleaned.Replace("```", "");
        cleaned = cleaned.Replace("¡°", "\"");
        cleaned = cleaned.Replace("¡±", "\"");
        cleaned = cleaned.Replace("¡®", "'");
        cleaned = cleaned.Replace("¡¯", "'");

        int start = cleaned.IndexOf('{');
        int end = cleaned.LastIndexOf('}');

        if (start >= 0 && end > start)
        {
            cleaned = cleaned.Substring(start, end - start + 1);
        }

        cleaned = cleaned.Replace(",}", "}");
        cleaned = cleaned.Replace(", ]", "]");
        cleaned = cleaned.Replace(",]", "]");

        return cleaned.Trim();
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

        if (isSendingToLLM)
        {
            statusText.text = "Please wait. LLM is generating the current massing.";
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
        finishButton.gameObject.SetActive(false);

        recordedClip = Microphone.Start(microphoneDevice, false, maxRecordingSeconds, recordingSampleRate);
        isRecording = true;
    }

    private void StopRecordingAndTranscribe()
    {
        if (!isRecording) return;

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
        relationButton.interactable = false;

        List<IMultipartFormSection> formData = new List<IMultipartFormSection>();
        formData.Add(new MultipartFormDataSection("model", transcriptionModel));
        formData.Add(new MultipartFormDataSection("language", language));
        formData.Add(new MultipartFormDataSection("response_format", "json"));
        formData.Add(new MultipartFormFileSection("file", wavData, "recording.wav", "audio/wav"));

        using (UnityWebRequest request = UnityWebRequest.Post(TranscriptionUrl, formData))
        {
            request.SetRequestHeader("Authorization", "Bearer " + openAIApiKey);
            yield return request.SendWebRequest();

            isUploading = false;
            recordButton.interactable = true;
            UpdateRelationButton();

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
        scaler.dynamicPixelsPerUnit = 18;
        canvasRoot.AddComponent<GraphicRaycaster>();

        RectTransform canvasRect = canvas.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(1000, 760);
        canvasRect.localScale = new Vector3(0.0016f, 0.0016f, 0.0016f);

        Image bg = canvasRoot.AddComponent<Image>();
        bg.color = new Color(0.03f, 0.04f, 0.05f, 0.88f);
        bg.raycastTarget = false;

        CreatePanel("Header Panel", canvasRoot.transform, new Vector2(0, 320), new Vector2(920, 95), new Color(0.08f, 0.10f, 0.13f, 0.95f));

        titleText = CreateText("Title", canvasRoot.transform, "Museum Prompt Input", 34, new Vector2(0, 340), new Vector2(860, 45));
        positionText = CreateText("Position Text", canvasRoot.transform, "Selected Position:", 17, new Vector2(0, 300), new Vector2(860, 32));

        CreatePanel("Module Panel", canvasRoot.transform, new Vector2(0, 230), new Vector2(920, 80), new Color(0.06f, 0.07f, 0.09f, 0.9f));
        CreateModuleButtons(canvasRoot.transform);

        relationButton = CreateButton("Relation Button", canvasRoot.transform, "Relation: Connected", new Vector2(0, 160), new Vector2(310, 48));
        relationButton.onClick.AddListener(ToggleCurrentRelation);

        CreatePanel("Instruction Panel", canvasRoot.transform, new Vector2(0, 75), new Vector2(920, 110), new Color(0.10f, 0.12f, 0.16f, 0.92f));
        instructionText = CreateText("Instruction Text", canvasRoot.transform, "Describe the current module.", 22, new Vector2(0, 75), new Vector2(840, 85));

        CreatePanel("Speech Panel", canvasRoot.transform, new Vector2(0, -80), new Vector2(920, 165), new Color(0.04f, 0.05f, 0.06f, 0.95f));
        speechText = CreateText("Speech Text", canvasRoot.transform, "Press Start Recording.", 21, new Vector2(0, -80), new Vector2(840, 130));

        CreatePanel("Status Panel", canvasRoot.transform, new Vector2(0, -200), new Vector2(920, 55), new Color(0.09f, 0.09f, 0.10f, 0.95f));
        statusText = CreateText("Status Text", canvasRoot.transform, "Ready.", 18, new Vector2(0, -200), new Vector2(840, 40));

        recordButton = CreateButton("Record Button", canvasRoot.transform, "Start Recording", new Vector2(-260, -305), new Vector2(230, 58));
        adjustButton = CreateButton("Adjust Button", canvasRoot.transform, "Restart", new Vector2(0, -305), new Vector2(180, 58));
        finishButton = CreateButton("Finish Button", canvasRoot.transform, "Passages", new Vector2(230, -305), new Vector2(180, 58));

        recordButton.onClick.AddListener(ToggleRecording);
        adjustButton.onClick.AddListener(AdjustCurrentModule);
        finishButton.onClick.AddListener(FinishAndGeneratePassages);

        EnsureEventSystemExists();
    }

    private Image CreatePanel(string name, Transform parent, Vector2 anchoredPos, Vector2 size, Color color)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        Image image = obj.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;
        return image;
    }

    private void CreateModuleButtons(Transform parent)
    {
        moduleButtons = new Button[moduleNames.Length];
        float startX = -330f;
        float stepX = 220f;
        float y = 230f;

        for (int i = 0; i < moduleNames.Length; i++)
        {
            int index = i;
            Button btn = CreateButton("Module Button " + moduleNames[i], parent, moduleNames[i], new Vector2(startX + stepX * i, y), new Vector2(190, 48));
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
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = Mathf.Max(12, fontSize - 6);
        text.resizeTextMaxSize = fontSize;

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
        image.color = new Color(0.16f, 0.36f, 0.72f, 1f);
        image.raycastTarget = true;

        Button button = obj.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = new Color(0.16f, 0.36f, 0.72f, 1f);
        colors.highlightedColor = new Color(0.25f, 0.50f, 0.95f, 1f);
        colors.pressedColor = new Color(0.10f, 0.22f, 0.48f, 1f);
        colors.selectedColor = new Color(0.22f, 0.45f, 0.85f, 1f);
        colors.disabledColor = new Color(0.25f, 0.25f, 0.25f, 0.65f);
        button.colors = colors;

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;

        Text btnText = CreateText("Text", obj.transform, label, 18, Vector2.zero, size);
        btnText.color = Color.white;
        btnText.fontStyle = FontStyle.Bold;
        btnText.raycastTarget = false;

        return button;
    }

    private void UpdateModuleHighlight()
    {
        if (moduleButtons == null) return;

        for (int i = 0; i < moduleButtons.Length; i++)
        {
            Image img = moduleButtons[i].GetComponent<Image>();

            if (i == currentModuleIndex)
            {
                img.color = new Color(1f, 0.68f, 0.18f, 1f);
            }
            else if (moduleCompleted[i])
            {
                img.color = new Color(0.18f, 0.62f, 0.34f, 1f);
            }
            else
            {
                img.color = new Color(0.16f, 0.36f, 0.72f, 1f);
            }
        }
    }

    private Camera GetEventCamera()
    {
        Camera cam = null;

        if (cameraTransform != null)
        {
            cam = cameraTransform.GetComponent<Camera>();
            if (cam == null) cam = cameraTransform.GetComponentInChildren<Camera>();
        }

        if (cam == null) cam = Camera.main;
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
        else if (eventSystem.GetComponent<StandaloneInputModule>() == null)
        {
            eventSystem.gameObject.AddComponent<StandaloneInputModule>();
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
