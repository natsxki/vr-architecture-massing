using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MassingGenerator : MonoBehaviour
{
    public static MassingGenerator Instance { get; private set; }

    // Stored so OptionSelectionMenu can read which design was selected
    public static MassingOption LastOption1 { get; private set; }
    public static MassingOption LastOption2 { get; private set; }

    [Header("Placement")]
    public float distanceInFront = 1.5f;
    public float optionSpacing   = 1.2f;

    [Header("Visualization")]
    [Tooltip("Scale factor when entering real-scale view.")]
    public float realScaleFactor = 50f;

    private GameObject currentOption1Parent;
    private GameObject currentOption2Parent;

    // -------------------------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void GenerateMassings(MuseumMassingResult data)
    {
        UndoManager.Instance?.PushMassing(currentOption1Parent, currentOption2Parent);

        if (currentOption1Parent) currentOption1Parent.SetActive(false);
        if (currentOption2Parent) currentOption2Parent.SetActive(false);

        LastOption1 = data.option1;
        LastOption2 = data.option2;

        currentOption1Parent = new GameObject("Option_1_Massing");
        currentOption2Parent = new GameObject("Option_2_Massing");

        PlaceInFrontOfUser(currentOption1Parent, currentOption2Parent);

        BuildMuseum(data.option1, currentOption1Parent.transform);
        BuildMuseum(data.option2, currentOption2Parent.transform);

        AddFloatingLabel(currentOption1Parent.transform, "◀ Option A\nLeft Trigger");
        AddFloatingLabel(currentOption2Parent.transform, "Option B ▶\nRight Trigger");

        JsonImportExport.LastResult = data;
        AppStateManager.Instance?.SetState(AppState.ViewingOptions);
        NotificationManager.Instance?.ClearStatus();
        Debug.Log("[MassingGenerator] Generated successfully.");
    }

    // -------------------------------------------------------------------------
    // Visualization at real scale
    // -------------------------------------------------------------------------

    public void VisualizeAtRealScale(int optionIndex)
    {
        GameObject target = optionIndex == 0 ? currentOption1Parent : currentOption2Parent;
        GameObject other  = optionIndex == 0 ? currentOption2Parent : currentOption1Parent;

        if (target == null) return;

        if (other != null) other.SetActive(false);

        target.transform.localScale = Vector3.one * realScaleFactor;

        if (Camera.main != null)
        {
            Vector3 fwd = Camera.main.transform.forward; fwd.y = 0; fwd.Normalize();
            Vector3 pos = Camera.main.transform.position + fwd * (realScaleFactor * 0.2f);
            pos.y = 0f;
            target.transform.position = pos;
        }

        // Hide floating labels — they'd be enormous at real scale
        foreach (Transform child in target.transform)
        {
            if (child.name == "OptionLabel")
                child.gameObject.SetActive(false);
        }

        AppStateManager.Instance?.SetState(AppState.ViewingOptions);
        Debug.Log($"[MassingGenerator] Visualising option {optionIndex + 1} at {realScaleFactor}x scale.");
    }

    // -------------------------------------------------------------------------
    // Undo support
    // -------------------------------------------------------------------------

    public void RestorePrevious(GameObject prev1, GameObject prev2)
    {
        if (currentOption1Parent) Destroy(currentOption1Parent);
        if (currentOption2Parent) Destroy(currentOption2Parent);

        currentOption1Parent = prev1;
        currentOption2Parent = prev2;

        if (currentOption1Parent) currentOption1Parent.SetActive(true);
        if (currentOption2Parent) currentOption2Parent.SetActive(true);

        AppStateManager.Instance?.SetState(prev1 != null ? AppState.ViewingOptions : AppState.Idle);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private void PlaceInFrontOfUser(GameObject opt1, GameObject opt2)
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 forward = cam.transform.forward; forward.y = 0; forward.Normalize();
        Vector3 right   = cam.transform.right;   right.y   = 0; right.Normalize();

        Vector3 center = cam.transform.position + forward * distanceInFront;
        center.y = 0f;

        opt1.transform.position = center - right * optionSpacing;
        opt2.transform.position = center + right * optionSpacing;
    }

    private void BuildMuseum(MassingOption optionData, Transform parentNode)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Standard");

        for (int i = 0; i < optionData.rooms.Count; i++)
        {
            var room = optionData.rooms[i];

            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = room.roomName;
            cube.transform.SetParent(parentNode);
            cube.transform.localScale    = new Vector3(room.scaleX, room.scaleY, room.scaleZ);
            cube.transform.localPosition = new Vector3(room.posX, room.posY + room.scaleY * 0.5f, room.posZ);

            var mat = new Material(shader);
            Color c = RoomColor(room.roomName);
            mat.color = c;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);

            cube.GetComponent<MeshRenderer>().material = mat;
        }
    }

    private void AddFloatingLabel(Transform parent, string labelText)
    {
        // World-space canvas — same scaling convention as the rest of the project
        // (canvas sizeDelta in "units", localScale 0.001 → 1 unit = 1 mm world)
        GameObject canvasGO = new GameObject("OptionLabel");
        canvasGO.transform.SetParent(parent, false);
        canvasGO.transform.localPosition = new Vector3(0f, 0.5f, 0f);

        Canvas c = canvasGO.AddComponent<Canvas>();
        c.renderMode = RenderMode.WorldSpace;
        canvasGO.AddComponent<CanvasScaler>();

        RectTransform crt = canvasGO.GetComponent<RectTransform>();
        crt.sizeDelta  = new Vector2(600f, 200f);  // 0.6 m × 0.2 m world
        crt.localScale = Vector3.one * 0.001f;

        // Text label
        GameObject textGO = new GameObject("Text");
        textGO.transform.SetParent(canvasGO.transform, false);
        var tmp = textGO.AddComponent<TextMeshProUGUI>();
        tmp.text               = labelText;
        tmp.fontSize           = 55f;   // 55 × 0.001 = 5.5 cm characters
        tmp.color              = Color.white;
        tmp.alignment          = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;
        tmp.fontStyle          = FontStyles.Bold;

        RectTransform trt = textGO.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;

        canvasGO.AddComponent<Billboard>();
    }

    private static Color RoomColor(string roomName)
    {
        float hue = (Mathf.Abs(roomName.ToLowerInvariant().GetHashCode()) % 1000) / 1000f;
        return Color.HSVToRGB(hue, 0.55f, 0.85f);
    }
}
