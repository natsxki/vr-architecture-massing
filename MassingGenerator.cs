using System;
using System.Collections.Generic;
using UnityEngine;

public class MassingGenerator : MonoBehaviour
{
    [Header("Generation Settings")]
    [Tooltip("Strict 1:100 scale should be 0.01. For classroom demo, 0.08 or 0.1 is easier to see.")]
    public float scaleFactor = 0.1f;

    [Tooltip("Prevents old Inspector values such as 0.01 from making the preview almost invisible during demo.")]
    public bool enforceMinimumVisibleScale = true;

    [Tooltip("Minimum scale used when enforceMinimumVisibleScale is enabled.")]
    public float minimumVisibleScaleFactor = 0.04f;

    public bool clearOldOptions = true;

    [Tooltip("Distance kept between two separated spaces before the passage is generated, in meters before scaling.")]
    public float separatedGapMeters = 4f;

    [Tooltip("Width of automatically generated passages, in meters before scaling.")]
    public float passageWidthMeters = 2f;

    [Tooltip("Height of automatically generated passages, in meters before scaling.")]
    public float passageHeightMeters = 3f;

    [Header("Root")]
    public Transform massingRoot;

    [Header("Colors")]
    public Color entranceColor = new Color(0.95f, 0.55f, 0.20f, 1f);
    public Color lightGalleryColor = new Color(0.95f, 0.90f, 0.30f, 1f);
    public Color soundGalleryColor = new Color(0.25f, 0.45f, 0.95f, 1f);
    public Color cafeColor = new Color(0.35f, 0.80f, 0.45f, 1f);
    public Color passageColor = new Color(0.72f, 0.72f, 0.72f, 1f);
    public Color defaultColor = new Color(0.75f, 0.75f, 0.75f, 1f);

    [Serializable]
    public class SingleMassingResponse
    {
        public string label;
        public float x;
        public float y;
        public float z;
        public float w;
        public float d;
        public float h;
        public string relation;
    }

    [Serializable]
    public class SpaceBox
    {
        public string label;
        public float x;
        public float y;
        public float z;
        public float w;
        public float d;
        public float h;
    }

    private class PlacedBox
    {
        public string label;
        public float x;
        public float y;
        public float z;
        public float w;
        public float d;
        public float h;
        public string relationToPrevious;
        public GameObject gameObject;
    }

    private readonly List<PlacedBox> placedBoxes = new List<PlacedBox>();
    private Vector3 currentBasePosition = Vector3.zero;
    private bool hasStarted = false;

    public void BeginIncrementalGeneration(Vector3 basePosition)
    {
        EnsureRoot();

        if (enforceMinimumVisibleScale && scaleFactor < minimumVisibleScaleFactor)
        {
            Debug.LogWarning("Scale Factor was too small for visible preview. Auto-adjusted from " + scaleFactor + " to " + minimumVisibleScaleFactor);
            scaleFactor = minimumVisibleScaleFactor;
        }

        currentBasePosition = basePosition;
        massingRoot.position = currentBasePosition;

        if (clearOldOptions)
        {
            ClearChildren(massingRoot);
        }

        placedBoxes.Clear();
        hasStarted = true;

        Debug.Log("Incremental massing generation started at: " + currentBasePosition);
    }

    public void GenerateSingleFromJson(string json, string expectedLabel, string relationToPrevious, Vector3 basePosition)
    {
        if (!hasStarted)
        {
            BeginIncrementalGeneration(basePosition);
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            Debug.LogError("Single massing JSON is empty.");
            return;
        }

        json = CleanJsonObjectString(json);

        SingleMassingResponse response = null;

        try
        {
            response = JsonUtility.FromJson<SingleMassingResponse>(json);
        }
        catch (Exception e)
        {
            Debug.LogError("Failed to parse single massing JSON. The LLM returned invalid JSON, so fallback dimensions will be used.");
            Debug.LogError(e.Message);
            Debug.LogError("Invalid JSON was: " + json);
            response = CreateFallbackResponse(expectedLabel, relationToPrevious);
        }

        if (response == null)
        {
            Debug.LogError("Parsed single massing JSON is null. Fallback dimensions will be used.");
            Debug.LogError("Invalid JSON was: " + json);
            response = CreateFallbackResponse(expectedLabel, relationToPrevious);
        }

        if (string.IsNullOrWhiteSpace(response.label))
        {
            response.label = expectedLabel;
        }

        // Keep labels stable for later color mapping and passage generation.
        response.label = expectedLabel;

        if (response.w <= 0f || response.d <= 0f || response.h <= 0f)
        {
            Debug.LogWarning("LLM returned invalid dimensions. Using fallback dimensions for " + expectedLabel);
            ApplyFallbackSize(response, expectedLabel);
        }

        string relation = NormalizeRelation(relationToPrevious);
        if (!string.IsNullOrWhiteSpace(response.relation))
        {
            relation = NormalizeRelation(response.relation);
        }

        if (placedBoxes.Count == 0)
        {
            relation = "none";
            response.x = 0f;
            response.z = 0f;
        }
        else
        {
            ApplyRelationPlacement(response, relation, placedBoxes[placedBoxes.Count - 1]);
        }

        PlacedBox placed = new PlacedBox
        {
            label = response.label,
            x = response.x,
            y = response.y,
            z = response.z,
            w = response.w,
            d = response.d,
            h = response.h,
            relationToPrevious = relation
        };

        placed.gameObject = CreateBox(placed, massingRoot, placed.label);
        placedBoxes.Add(placed);

        Debug.Log("Generated single massing: " + placed.label + " relation=" + relation);
    }

    private string CleanJsonObjectString(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        string cleaned = text.Trim();

        cleaned = cleaned.Replace("```json", "");
        cleaned = cleaned.Replace("```", "");
        cleaned = cleaned.Replace("“", "\"");
        cleaned = cleaned.Replace("”", "\"");
        cleaned = cleaned.Replace("‘", "'");
        cleaned = cleaned.Replace("’", "'");

        int start = cleaned.IndexOf('{');
        int end = cleaned.LastIndexOf('}');

        if (start >= 0 && end > start)
        {
            cleaned = cleaned.Substring(start, end - start + 1);
        }

        // Remove a very common LLM mistake: trailing commas before a closing brace.
        cleaned = cleaned.Replace(",}", "}");
        cleaned = cleaned.Replace(", ]", "]");
        cleaned = cleaned.Replace(",]", "]");

        return cleaned.Trim();
    }

    private SingleMassingResponse CreateFallbackResponse(string expectedLabel, string relationToPrevious)
    {
        SingleMassingResponse response = new SingleMassingResponse();
        response.label = expectedLabel;
        response.y = 0f;
        response.relation = NormalizeRelation(relationToPrevious);

        string key = NormalizeLabel(expectedLabel);

        if (key == "light_gallery")
        {
            response.x = -10f;
            response.z = 0f;
        }
        else if (key == "sound_gallery")
        {
            response.x = 0f;
            response.z = 10f;
        }
        else if (key == "cafe")
        {
            response.x = 10f;
            response.z = 0f;
        }
        else
        {
            response.x = 0f;
            response.z = 0f;
        }

        ApplyFallbackSize(response, expectedLabel);
        return response;
    }

    public void GeneratePassagesAfterAllSpaces()
    {
        if (placedBoxes.Count < 2)
        {
            Debug.LogWarning("Not enough spaces to generate passages.");
            return;
        }

        // Remove old passages first, so adjusting and regenerating will not duplicate them.
        for (int i = massingRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = massingRoot.GetChild(i);
            if (child.name.StartsWith("passage_"))
            {
                Destroy(child.gameObject);
            }
        }

        int passageIndex = 1;

        for (int i = 1; i < placedBoxes.Count; i++)
        {
            PlacedBox previous = placedBoxes[i - 1];
            PlacedBox current = placedBoxes[i];

            if (NormalizeRelation(current.relationToPrevious) == "separated")
            {
                CreatePassageBetween(previous, current, passageIndex);
                passageIndex++;
            }
        }

        Debug.Log("Passage generation finished. Passage count: " + (passageIndex - 1));
    }

    private void ApplyFallbackSize(SingleMassingResponse response, string expectedLabel)
    {
        string key = NormalizeLabel(expectedLabel);

        if (key == "entrance")
        {
            response.w = 14f;
            response.d = 10f;
            response.h = 4f;
        }
        else if (key == "light_gallery")
        {
            response.w = 6f;
            response.d = 8f;
            response.h = 12f;
        }
        else if (key == "sound_gallery")
        {
            response.w = 9f;
            response.d = 9f;
            response.h = 6f;
        }
        else if (key == "cafe")
        {
            response.w = 7f;
            response.d = 6f;
            response.h = 4f;
        }
        else
        {
            response.w = 8f;
            response.d = 8f;
            response.h = 5f;
        }
    }

    private void ApplyRelationPlacement(SingleMassingResponse current, string relation, PlacedBox previous)
    {
        float directionX = current.x - previous.x;
        float directionZ = current.z - previous.z;

        bool placeAlongX = Mathf.Abs(directionX) >= Mathf.Abs(directionZ);
        float gap = relation == "separated" ? separatedGapMeters : 0f;

        if (placeAlongX)
        {
            float sign = directionX >= 0f ? 1f : -1f;
            current.x = previous.x + sign * ((previous.w + current.w) * 0.5f + gap);
            current.z = previous.z;
        }
        else
        {
            float sign = directionZ >= 0f ? 1f : -1f;
            current.z = previous.z + sign * ((previous.d + current.d) * 0.5f + gap);
            current.x = previous.x;
        }

        current.y = 0f;
    }

    private void CreatePassageBetween(PlacedBox a, PlacedBox b, int passageIndex)
    {
        float dx = b.x - a.x;
        float dz = b.z - a.z;

        SpaceBox passage = new SpaceBox();
        passage.label = "passage";
        passage.y = 0f;
        passage.h = passageHeightMeters;

        if (Mathf.Abs(dx) >= Mathf.Abs(dz))
        {
            float sign = dx >= 0f ? 1f : -1f;
            float aEdge = a.x + sign * a.w * 0.5f;
            float bEdge = b.x - sign * b.w * 0.5f;
            float length = Mathf.Abs(bEdge - aEdge);

            passage.x = (aEdge + bEdge) * 0.5f;
            passage.z = a.z;
            passage.w = Mathf.Max(length, 0.1f);
            passage.d = passageWidthMeters;
        }
        else
        {
            float sign = dz >= 0f ? 1f : -1f;
            float aEdge = a.z + sign * a.d * 0.5f;
            float bEdge = b.z - sign * b.d * 0.5f;
            float length = Mathf.Abs(bEdge - aEdge);

            passage.x = a.x;
            passage.z = (aEdge + bEdge) * 0.5f;
            passage.w = passageWidthMeters;
            passage.d = Mathf.Max(length, 0.1f);
        }

        CreateBox(passage, massingRoot, "passage_" + passageIndex + "_" + a.label + "_to_" + b.label);
    }

    private GameObject CreateBox(PlacedBox box, Transform parent, string objectName)
    {
        SpaceBox space = new SpaceBox
        {
            label = box.label,
            x = box.x,
            y = box.y,
            z = box.z,
            w = box.w,
            d = box.d,
            h = box.h
        };

        return CreateBox(space, parent, objectName);
    }

    private GameObject CreateBox(SpaceBox box, Transform parent, string objectName)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = objectName;
        cube.transform.SetParent(parent, false);

        float scaledX = box.x * scaleFactor;
        float scaledY = box.y * scaleFactor;
        float scaledZ = box.z * scaleFactor;

        float scaledW = box.w * scaleFactor;
        float scaledD = box.d * scaleFactor;
        float scaledH = box.h * scaleFactor;

        cube.transform.localPosition = new Vector3(
            scaledX,
            scaledY + scaledH * 0.5f,
            scaledZ
        );

        cube.transform.localScale = new Vector3(
            scaledW,
            scaledH,
            scaledD
        );

        Renderer renderer = cube.GetComponent<Renderer>();
        renderer.material = CreateMaterial(GetColorForLabel(box.label));

        return cube;
    }

    private void EnsureRoot()
    {
        if (massingRoot == null)
        {
            GameObject rootObj = new GameObject("Incremental Massing Root");
            massingRoot = rootObj.transform;
        }
    }

    private string NormalizeRelation(string relation)
    {
        if (string.IsNullOrWhiteSpace(relation))
        {
            return "connected";
        }

        string key = relation.Trim().ToLower();

        if (key == "separated" || key == "separate" || key == "apart" || key == "gap" || key == "分开" || key == "不相接")
        {
            return "separated";
        }

        if (key == "none" || key == "first")
        {
            return "none";
        }

        return "connected";
    }

    private string NormalizeLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return "";
        }

        return label.Trim().ToLower().Replace(" ", "_");
    }

    private Color GetColorForLabel(string label)
    {
        string key = NormalizeLabel(label);

        if (key == "entrance" || key == "entrance_hall")
        {
            return entranceColor;
        }

        if (key == "light_gallery")
        {
            return lightGalleryColor;
        }

        if (key == "sound_gallery")
        {
            return soundGalleryColor;
        }

        if (key == "cafe" || key == "café")
        {
            return cafeColor;
        }

        if (key == "passage")
        {
            return passageColor;
        }

        return defaultColor;
    }

    private Material CreateMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");

        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material material = new Material(shader);

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        else if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }
        else
        {
            material.color = color;
        }

        return material;
    }

    private void ClearChildren(Transform root)
    {
        if (root == null)
        {
            return;
        }

        for (int i = root.childCount - 1; i >= 0; i--)
        {
            Destroy(root.GetChild(i).gameObject);
        }
    }
}
