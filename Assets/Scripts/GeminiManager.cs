using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class GeminiManager : MonoBehaviour
{
    public static GeminiManager Instance { get; private set; }

    [Header("API Key")]
    [Tooltip("Drag Assets/LocalSecrets/gemini_key.txt here. TextAsset loads correctly on Quest (Android).")]
    public TextAsset geminiKeyAsset;

    [Header("OpenAI Fallback")]
    [Tooltip("Drag Assets/LocalSecrets/whisper_key.txt here — same OpenAI key used by Whisper.")]
    public TextAsset openAIKeyAsset;

    private string apiKey;
    private string _openAIApiKey;
    private const string OpenAIChatUrl   = "https://api.openai.com/v1/chat/completions";
    private const string OpenAIModel     = "gpt-4o-mini";

    private static readonly string[] GeminiModels =
    {
        "gemini-2.5-flash-lite",
        "gemini-2.0-flash",
        "gemini-1.5-flash"
    };
    private static int _modelIndex = 0;
    private string CurrentEndpoint =>
        $"https://generativelanguage.googleapis.com/v1beta/models/{GeminiModels[_modelIndex]}:generateContent";

    private void RotateModel()
    {
        _modelIndex = (_modelIndex + 1) % GeminiModels.Length;
        Debug.LogWarning($"[GeminiManager] Rotating to model: {GeminiModels[_modelIndex]}");
        NotificationManager.Instance?.ShowStatus($"Retrying with {GeminiModels[_modelIndex]}…");
    }

    [Header("References")]
    public MassingGenerator generator;


    private const string systemPrompt = 
    "You are an architectural massing generator for a VR application. Generate two distinct spatial layouts for a sensory museum consisting strictly of orthogonal, rectangular volumes. " +
    "CRITICAL MATHEMATICAL & ARCHITECTURAL RULES: " +
    "1. Scale & Pivot: You are generating data for Unity cubes. Cubes are positioned based on their CENTER PIVOT. Dimensions (scaleX, scaleY, scaleZ) MUST be strictly between 0.05 and 0.3. " +
    "2. Strict Mathematical Adjacency (NO OVERLAP, NO GAPS): To place Room B adjacent to Room A along the X-axis, the distance between their center posX values MUST be exactly half of Room A's scaleX plus half of Room B's scaleX. " +
    "Formula: posX_B = posX_A +/- ((scaleX_A / 2) + (scaleX_B / 2)). " +
    "The exact same formula applies to the Z-axis for posZ. You MUST mathematically calculate these center-to-center distances to ensure walls touch perfectly without intersecting. " +
    "3. Grounding: Set posY to 0.0 for all ground-level rooms to ensure they sit perfectly flat on the table. " +
    "4. Continuous Layout: All rooms MUST physically connect to form a single continuous building. If spatial separation is conceptually needed, you must autonomously generate connecting 'Corridor' rooms using the exact adjacency math above. " +

    "Output your response ONLY as a raw JSON object matching this schema exactly, with no markdown code blocks, no backticks, no extra text: " +
    "{ \"option1\": { \"optionName\": \"\", \"architecturalConcept\": \"\", \"rooms\": [ { \"roomName\": \"\", \"posX\": 0.0, \"posY\": 0.0, \"posZ\": 0.0, \"scaleX\": 0.0, \"scaleY\": 0.0, \"scaleZ\": 0.0 } ] }, \"option2\": { } }";



    // -------------------------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
#if UNITY_EDITOR
        if (geminiKeyAsset == null)
            geminiKeyAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(
                "Assets/LocalSecrets/gemini_key.txt");
#endif
        if (geminiKeyAsset == null)
            geminiKeyAsset = Resources.Load<TextAsset>("gemini_key");

        if (geminiKeyAsset != null)
        {
            apiKey = geminiKeyAsset.text.Trim().TrimStart('﻿');
            Debug.Log($"[GeminiManager] Gemini key loaded, length={apiKey.Length}");
        }
        else
        {
            Debug.LogError("[GeminiManager] Gemini key not found. " +
                           "Drag gemini_key.txt onto the Inspector field, " +
                           "or copy it to Assets/Resources/gemini_key.txt.");
        }

#if UNITY_EDITOR
        if (openAIKeyAsset == null)
            openAIKeyAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(
                "Assets/LocalSecrets/whisper_key.txt");
#endif
        if (openAIKeyAsset == null)
            openAIKeyAsset = Resources.Load<TextAsset>("whisper_key");

        if (openAIKeyAsset != null)
        {
            _openAIApiKey = openAIKeyAsset.text.Trim().TrimStart('﻿');
            Debug.Log($"[GeminiManager] OpenAI fallback key loaded, length={_openAIApiKey.Length}");
        }
    }

    public void RequestMassingOptions(string userSpokenText, string iterationBaseJson = null)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            NotificationManager.Instance?.ShowWarning("Gemini API key missing. Check LocalSecrets/gemini_key.txt.");
            return;
        }

        AppStateManager.Instance?.SetState(AppState.GeneratingMassing);
        NotificationManager.Instance?.ShowStatus("Sending prompt to Gemini AI…");
        Debug.Log("Sending to Gemini: " + userSpokenText);
        StartCoroutine(SendRequestToGemini(userSpokenText, iterationBaseJson));
    }

    // -------------------------------------------------------------------------
    // Keyword summariser — called after each Whisper transcription
    // -------------------------------------------------------------------------

    public void SummarizeDescription(string previousSummary, string newTranscription, string fallbackText, System.Action<string> onComplete)
    {
        StartCoroutine(SummarizeCoroutine(previousSummary, newTranscription, fallbackText, onComplete));
    }

    private IEnumerator SummarizeCoroutine(string previousSummary, string newTranscription, string fallbackText, System.Action<string> onComplete)
    {
        string input = string.IsNullOrWhiteSpace(previousSummary)
            ? newTranscription
            : $"Previous summary: {previousSummary}\nNew spoken addition: {newTranscription}";

        var payload = new
        {
            system_instruction = new
            {
                parts = new[] { new { text =
                    "You extract spatial design requirements from spoken museum descriptions into a concise JSON keyword map. " +
                    "Identify place types explicitly or implicitly mentioned (hall, cafe, gallery, sensory room, lobby, garden, corridor, passageway, or any room type the user describes) and their qualities as comma-separated keywords. " +
                    "If a previous summary is provided, merge it with the new input — new input takes priority for conflicts. " +
                    "Output ONLY raw JSON, no markdown, no extra text. Example: {\"hall\": \"large, bright, central\", \"cafe\": \"cozy, quiet\"}."
                } }
            },
            contents = new[] { new { parts = new[] { new { text = input } } } },
            generationConfig = new { response_mime_type = "application/json" }
        };

        string jsonPayload = JsonConvert.SerializeObject(payload);

        for (int attempt = 0; attempt < GeminiModels.Length; attempt++)
        {
            if (attempt > 0)
            {
                RotateModel();
                yield return new WaitForSeconds(2f);
            }

            using (UnityWebRequest request = new UnityWebRequest($"{CurrentEndpoint}?key={apiKey}", "POST"))
            {
                request.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonPayload));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        Debug.Log($"[GeminiManager] Summarise raw response:\n{request.downloadHandler.text}");
                        JObject resp    = JObject.Parse(request.downloadHandler.text);
                        string  raw     = ExtractResponseText(resp)
                                          ?? throw new System.Exception("No text content in summarise response.");
                        Debug.Log($"[GeminiManager] Summarise extracted text:\n{raw}");
                        string  summary = FormatSummaryJson(raw.Trim());
                        Debug.Log($"[GeminiManager] Summarise formatted:\n{summary}");
                        onComplete?.Invoke(summary);
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"[GeminiManager] Summarise parse error: {e.Message}\n{request.downloadHandler.text}");
                        onComplete?.Invoke(fallbackText);
                    }
                    yield break;
                }

                Debug.LogWarning($"[GeminiManager] {GeminiModels[_modelIndex]} summarise failed ({request.responseCode})");
                if (attempt == GeminiModels.Length - 1)
                {
                    if (!string.IsNullOrEmpty(_openAIApiKey))
                        yield return StartCoroutine(SummarizeWithOpenAI(input, fallbackText, onComplete));
                    else
                    {
                        NotificationManager.Instance?.ShowWarning("AI unavailable — showing your recordings instead.");
                        onComplete?.Invoke(fallbackText);
                    }
                }
            }
        }
    }

    // -------------------------------------------------------------------------

    private IEnumerator SendRequestToGemini(string promptText, string iterationBaseJson = null)
    {
        string userContent = string.IsNullOrEmpty(iterationBaseJson)
            ? promptText
            : $"Refine the following existing design by incorporating the new requirements below.\n\nExisting design JSON to build upon:\n{iterationBaseJson}\n\nNew requirements to incorporate:\n{promptText}";

        var requestPayload = new
        {
            system_instruction = new
            {
                parts = new[]
                {
                    new { text = systemPrompt }               }
            },
            contents = new[]
            {
                new { parts = new[] { new { text = userContent } } }
            },
            generationConfig = new { response_mime_type = "application/json" }
        };

        string jsonPayload = JsonConvert.SerializeObject(requestPayload);
        NotificationManager.Instance?.ShowStatus("Calculating massing options… almost there.");

        for (int attempt = 0; attempt < GeminiModels.Length; attempt++)
        {
            if (attempt > 0)
            {
                RotateModel();
                yield return new WaitForSeconds(2f);
            }

            string url = $"{CurrentEndpoint}?key={apiKey}";
            using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
            {
                request.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonPayload));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    Debug.Log($"[GeminiManager] Massing raw response ({GeminiModels[_modelIndex]}):\n{request.downloadHandler.text}");
                    NotificationManager.Instance?.ShowStatus("Placing rooms in the scene…");
                    ParseAndGenerate(request.downloadHandler.text);
                    yield break;
                }

                Debug.LogWarning($"[GeminiManager] {GeminiModels[_modelIndex]} failed ({request.responseCode}): {request.error}");

                if (attempt == GeminiModels.Length - 1)
                {
                    if (!string.IsNullOrEmpty(_openAIApiKey))
                    {
                        NotificationManager.Instance?.ShowStatus("Gemini unavailable — trying OpenAI…");
                        yield return StartCoroutine(SendRequestToOpenAI(userContent));
                    }
                    else
                    {
                        NotificationManager.Instance?.ShowWarning("All AI models unavailable — try again later.");
                        NotificationManager.Instance?.ClearStatus();
                        AppStateManager.Instance?.SetState(AppState.ReviewingTranscription);
                    }
                }
            }
        }
    }

    // -------------------------------------------------------------------------
    // OpenAI fallback coroutines
    // -------------------------------------------------------------------------

    private IEnumerator SendRequestToOpenAI(string userContent)
    {
        var payload = new
        {
            model    = OpenAIModel,
            messages = new[]
            {
                new { role = "system", content =
                    "You are an architectural massing generator for a VR tabletop application. " +
                    "The user will describe a sensory museum. Generate two distinct spatial layouts " +
                    "consisting strictly of orthogonal, rectangular volumes. " +
                    "CRITICAL RULES: Miniature tabletop scale — entire museum fits on a 1×1 m table; " +
                    "room dimensions (scaleX/Y/Z) strictly between 0.05 and 0.3; " +
                    "all rooms must physically connect — add corridor/passageway rooms if needed; " +
                    "set posY=0.0 for ground-level rooms; calculate posX/posZ so edges touch. " +
                    "Output ONLY raw JSON — no markdown, no backticks, no extra text — matching exactly: " +
                    "{\"option1\":{\"optionName\":\"\",\"architecturalConcept\":\"\",\"rooms\":[{\"roomName\":\"\",\"posX\":0.0,\"posY\":0.0,\"posZ\":0.0,\"scaleX\":0.0,\"scaleY\":0.0,\"scaleZ\":0.0}]},\"option2\":{}}" },
                new { role = "user", content = userContent }
            },
            response_format = new { type = "json_object" }
        };

        string jsonPayload = JsonConvert.SerializeObject(payload);

        using (UnityWebRequest request = new UnityWebRequest(OpenAIChatUrl, "POST"))
        {
            request.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonPayload));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + _openAIApiKey);

            yield return request.SendWebRequest();

            string raw = request.downloadHandler.text;
            Debug.Log($"[GeminiManager] OpenAI massing response:\n{raw}");

            if (request.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    JObject resp    = JObject.Parse(raw);
                    string  content = (string)resp["choices"][0]["message"]["content"]
                                      ?? throw new System.Exception("No content in OpenAI response.");
                    content = StripMarkdownFences(content.Trim());
                    NotificationManager.Instance?.ShowStatus("Placing rooms in the scene…");

                    MuseumMassingResult result;
                    try   { result = JsonConvert.DeserializeObject<MuseumMassingResult>(content); }
                    catch { result = JsonConvert.DeserializeObject<MuseumMassingResult>(content.Replace("'", "\"")); }

                    if (result?.option1 == null || result?.option2 == null)
                        throw new System.Exception("option1 or option2 missing.");

                    generator.GenerateMassings(result);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[GeminiManager] OpenAI massing parse error: {e.Message}\n{raw}");
                    NotificationManager.Instance?.ShowWarning($"OpenAI parse error — try again.");
                    NotificationManager.Instance?.ClearStatus();
                    AppStateManager.Instance?.SetState(AppState.ReviewingTranscription);
                }
            }
            else
            {
                Debug.LogError($"[GeminiManager] OpenAI fallback failed ({request.responseCode}): {request.error}\n{raw}");
                NotificationManager.Instance?.ShowWarning($"OpenAI also failed ({request.responseCode}) — try again.");
                NotificationManager.Instance?.ClearStatus();
                AppStateManager.Instance?.SetState(AppState.ReviewingTranscription);
            }
        }
    }

    private IEnumerator SummarizeWithOpenAI(string input, string fallbackText, System.Action<string> onComplete)
    {
        var payload = new
        {
            model    = OpenAIModel,
            messages = new[]
            {
                new { role = "system", content =
                    "You extract spatial design requirements from spoken museum descriptions into a concise JSON keyword map. " +
                    "Identify place types (hall, cafe, gallery, sensory room, lobby, garden, corridor, passageway, or any room type described) " +
                    "and their qualities as comma-separated keywords. " +
                    "If a previous summary is provided, merge it with the new input — new input takes priority for conflicts. " +
                    "Output ONLY raw JSON, no markdown, no extra text. Example: {\"hall\":\"large, bright, central\",\"cafe\":\"cozy, quiet\"}." },
                new { role = "user", content = input }
            },
            response_format = new { type = "json_object" }
        };

        string jsonPayload = JsonConvert.SerializeObject(payload);

        using (UnityWebRequest request = new UnityWebRequest(OpenAIChatUrl, "POST"))
        {
            request.uploadHandler   = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonPayload));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + _openAIApiKey);

            yield return request.SendWebRequest();

            string raw = request.downloadHandler.text;
            Debug.Log($"[GeminiManager] OpenAI summarise response:\n{raw}");

            if (request.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    JObject resp    = JObject.Parse(raw);
                    string  content = (string)resp["choices"][0]["message"]["content"]
                                      ?? throw new System.Exception("No content.");
                    string  summary = FormatSummaryJson(content.Trim());
                    onComplete?.Invoke(summary);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[GeminiManager] OpenAI summarise parse error: {e.Message}\n{raw}");
                    onComplete?.Invoke(fallbackText);
                }
            }
            else
            {
                Debug.LogError($"[GeminiManager] OpenAI summarise failed ({request.responseCode}): {raw}");
                onComplete?.Invoke(fallbackText);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// Gemini 2.5 thinking models return a "thought" part before the real answer.
    /// This finds the first part that is NOT a thought and returns its text.
    private static string ExtractResponseText(JObject response)
    {
        var parts = response["candidates"]?[0]?["content"]?["parts"];
        if (parts == null) return null;
        foreach (var part in parts)
        {
            bool isThought = part["thought"]?.Value<bool>() ?? false;
            if (!isThought && part["text"] != null)
                return (string)part["text"];
        }
        return null;
    }

    /// Converts {"hall":"large, bright","cafe":"cozy"} → "hall: large, bright\ncafe: cozy"
    private string FormatSummaryJson(string json)
    {
        try
        {
            var dict = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
            var sb   = new StringBuilder();
            foreach (var kv in dict)
                sb.AppendLine($"{kv.Key}: {kv.Value}");
            return sb.ToString().Trim();
        }
        catch
        {
            return json;
        }
    }

    // -------------------------------------------------------------------------

    private void ParseAndGenerate(string rawResponse)
    {
        string cleanJson = null;
        try
        {
            JObject jsonResponse = JObject.Parse(rawResponse);

            var candidates = jsonResponse["candidates"];
            if (candidates == null || !candidates.HasValues)
            {
                string blocked = jsonResponse["promptFeedback"]?["blockReason"]?.ToString() ?? "unknown";
                NotificationManager.Instance?.ShowWarning($"Gemini blocked: {blocked}");
                NotificationManager.Instance?.ClearStatus();
                AppStateManager.Instance?.SetState(AppState.ReviewingTranscription);
                Debug.LogError($"[GeminiManager] No candidates. blockReason={blocked}\nRaw: {rawResponse}");
                return;
            }

            cleanJson = ExtractResponseText(jsonResponse)
                        ?? throw new System.Exception("ExtractResponseText returned null.");

            Debug.Log($"[GeminiManager] Extracted JSON (first 300 chars): {cleanJson.Substring(0, Mathf.Min(300, cleanJson.Length))}");

            // Strip markdown code fences if the model added them despite being told not to
            cleanJson = StripMarkdownFences(cleanJson);

            MuseumMassingResult result = null;
            try
            {
                result = JsonConvert.DeserializeObject<MuseumMassingResult>(cleanJson);
            }
            catch
            {
                string fixedJson = cleanJson.Replace("'", "\"");
                result = JsonConvert.DeserializeObject<MuseumMassingResult>(fixedJson);
                Debug.LogWarning("[GeminiManager] Used single-quote fallback.");
            }

            if (result?.option1 == null || result?.option2 == null)
                throw new System.Exception("option1 or option2 missing after deserialise.");

            if (generator == null)
                throw new System.Exception("MassingGenerator reference not assigned on GeminiManager.");

            Debug.Log($"[GeminiManager] Parsed OK: 1={result.option1.optionName} 2={result.option2.optionName}");
            generator.GenerateMassings(result);
        }
        catch (System.Exception e)
        {
            string preview = cleanJson != null
                ? cleanJson.Substring(0, Mathf.Min(120, cleanJson.Length))
                : rawResponse.Substring(0, Mathf.Min(120, rawResponse.Length));

            NotificationManager.Instance?.ShowWarning($"Parse error: {e.Message}");
            NotificationManager.Instance?.ClearStatus();
            AppStateManager.Instance?.SetState(AppState.ReviewingTranscription);
            Debug.LogError($"[GeminiManager] Parse error: {e.Message}\nJSON preview: {preview}\nFull raw: {rawResponse}");
        }
    }

    private static string StripMarkdownFences(string text)
    {
        text = text.Trim();
        if (!text.StartsWith("```")) return text;
        int firstNewline = text.IndexOf('\n');
        int lastFence    = text.LastIndexOf("```");
        if (firstNewline < 0 || lastFence <= firstNewline) return text;
        return text.Substring(firstNewline + 1, lastFence - firstNewline - 1).Trim();
    }

}
