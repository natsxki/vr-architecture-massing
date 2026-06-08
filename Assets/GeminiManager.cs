using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json; 
using Newtonsoft.Json.Linq;

public class GeminiManager : MonoBehaviour
{
    [Header("API Settings")]
    public string apiKey = "AQ.Ab8RN6I6JpDbMDkT8jYL7TP7R-vrQIcovzRfLHl-Gr4i-_244A";
    private string endpoint = "https://generativelanguage.googleapis.com/v1beta/models/gemini-flash-latest:generateContent";

    [Header("References")]
    public MassingGenerator generator;

    public void RequestMassingOptions(string userSpokenText)
    {
        Debug.Log("Sending to Gemini: " + userSpokenText);
        StartCoroutine(SendRequestToGemini(userSpokenText));
    }

    private IEnumerator SendRequestToGemini(string promptText)
    {
        string url = $"{endpoint}?key={apiKey}";

        // Constructing the payload dynamically to avoid string formatting issues
        var requestPayload = new
        {
            system_instruction = new
            {
                parts = new[] { 
                    new { text = "You are an architectural massing generator for a VR tabletop application. The user will describe a sensory museum. Generate two distinct spatial layouts consisting strictly of orthogonal, rectangular volumes. CRITICAL ARCHITECTURAL & PROCEDURAL RULES: ; Miniature Tabletop Scale: The entire museum must fit on a 1x1 meter table. Individual room dimensions (scaleX, scaleY, scaleZ) MUST be strictly between 0.05 and 0.3. ; Adjacency & Corridors: All generated rooms MUST physically connect to form a single continuous building. If the architectural layout requires spacing between main rooms, you MUST autonomously generate additional connecting rooms (e.g., named 'Passageway' or 'Corridor') to link them together. No floating or disconnected geometry is allowed. ; Grounding: Set posY to 0.0 for all ground-level rooms to ensure they sit perfectly flat on the table.; Layout: Calculate posX and posZ carefully so that rooms sit adjacent to each other with edges touching or slightly intersecting, avoiding massive overlaps.; Output your response ONLY as a raw JSON object matching this schema exactly, with no markdown code blocks: { 'option1': { 'optionName': '', 'architecturalConcept': '', 'rooms': [ { 'roomName': '', 'posX': 0.0, 'posY': 0.0, 'posZ': 0.0, 'scaleX': 0.0, 'scaleY': 0.0, 'scaleZ': 0.0 } ] }, 'option2': { ... } }." } 
                }
            },
            contents = new[]
            {
                new { parts = new[] { new { text = promptText } } }
            },
            generationConfig = new
            {
                response_mime_type = "application/json"
            }
        };

        string jsonPayload = JsonConvert.SerializeObject(requestPayload);

        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                ParseAndGenerate(request.downloadHandler.text);
            }
            else
            {
                Debug.LogError($"Gemini API Error: {request.error}\n{request.downloadHandler.text}");
            }
        }
    }

    private void ParseAndGenerate(string rawResponse)
        {
            try 
            {
                // We use JObject here instead of 'dynamic' to keep the Unity compiler happy
                JObject jsonResponse = JObject.Parse(rawResponse);
                string cleanJson = (string)jsonResponse["candidates"][0]["content"]["parts"][0]["text"];

                MuseumMassingResult result = JsonConvert.DeserializeObject<MuseumMassingResult>(cleanJson);
                
                Debug.Log($"Successfully parsed concepts: 1. {result.option1.architecturalConcept} | 2. {result.option2.architecturalConcept}");
                
                generator.GenerateMassings(result);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to parse JSON. Error: {e.Message}\nRaw Response: {rawResponse}");
            }
        }
}