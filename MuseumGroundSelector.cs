using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

public class MuseumGroundSelector : MonoBehaviour
{
    [Header("References")]
    public Transform cameraTransform;
    public SpeechToTextUI speechToTextUI;

    [Header("Ground Settings")]
    public float groundSize = 20f;
    public float groundDistanceFromCamera = 5f;
    public float groundHeightBelowCamera = 1.2f;
    public Color groundColor = new Color(0.45f, 0.9f, 0.45f, 1f);
    public Color markerColor = new Color(1f, 0.75f, 0.1f, 1f);

    [Tooltip("Size of the selected-position marker plane in Unity units.")]
    public float markerSize = 0.12f;

    [Tooltip("Marker lies directly on the selected ground point. Set to 0 to avoid floating above the terrain.")]
    public float markerYOffset = 0f;

    [Header("Terrain Settings")]
    public int terrainResolution = 80;
    public float terrainHeight = 0.8f;
    public float terrainNoiseScale = 0.16f;
    public float terrainSecondNoiseScale = 0.45f;
    public float terrainSecondNoiseWeight = 0.25f;

    [Header("Tree Settings")]
    public int treeCount = 22;
    public float treeMinHeight = 0.7f;
    public float treeMaxHeight = 1.4f;
    public float treeAvoidCenterRadius = 5f;

    [Tooltip("Keep a corridor in front of the camera free of trees, so trees do not block the UI or main view.")]
    public float treeViewClearHalfWidth = 3.2f;

    [Tooltip("Depth of the tree-free corridor in front of the camera.")]
    public float treeViewClearDepth = 8f;

    [Tooltip("Minimum horizontal distance from the camera to any generated tree.")]
    public float treeAvoidCameraRadius = 2.5f;

    public Color trunkColor = new Color(0.35f, 0.18f, 0.08f, 1f);
    public Color leafColor = new Color(0.12f, 0.45f, 0.16f, 1f);

    [Header("Hint UI")]
    public Vector3 hintLocalOffset = new Vector3(0f, 0.2f, 2.0f);

    private GameObject groundObject;
    private GameObject markerObject;

    private Material groundMaterial;
    private Material trunkMaterial;
    private Material leafMaterial;

    private Canvas hintCanvas;
    private Text hintText;

    private bool hasSelectedPosition = false;
    private bool wasXRTriggerPressed = false;
    private bool isInitialized = false;

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

        if (speechToTextUI == null)
        {
            speechToTextUI = FindObjectOfType<SpeechToTextUI>();
        }

        if (speechToTextUI != null)
        {
            speechToTextUI.HidePromptUI();
        }

        StartCoroutine(InitializeAfterCameraReady());
    }

    private IEnumerator InitializeAfterCameraReady()
    {
        // Wait for XR Camera Rig / CenterEyeAnchor to update its real pose.
        yield return null;
        yield return null;
        yield return null;

        CreateGround();
        CreateForest();
        CreateMarker();
        CreateHintUI();

        isInitialized = true;
    }

    void Update()
    {
        if (!isInitialized)
        {
            return;
        }

        UpdateHintUIPosition();

        if (!hasSelectedPosition)
        {
            HandleGroundSelection();
        }

#if UNITY_EDITOR
        if (Input.GetKeyDown(KeyCode.G) && !hasSelectedPosition)
        {
            Vector3 testPosition = groundObject.transform.position;
            testPosition.y += CalculateTerrainLocalY(0f, 0f);
            SelectPosition(testPosition);
        }
#endif
    }

    private void CreateGround()
    {
        Vector3 groundCenter = Vector3.zero;

        if (cameraTransform != null)
        {
            Vector3 forward = cameraTransform.forward;
            forward.y = 0f;

            if (forward.sqrMagnitude < 0.001f)
            {
                forward = Vector3.forward;
            }

            forward.Normalize();

            groundCenter = cameraTransform.position + forward * groundDistanceFromCamera;
            groundCenter.y = cameraTransform.position.y - groundHeightBelowCamera;
        }
        else
        {
            groundCenter = new Vector3(0f, -1.2f, 5f);
        }

        groundObject = new GameObject("Museum Terrain Ground");
        groundObject.transform.position = groundCenter;

        MeshFilter meshFilter = groundObject.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = groundObject.AddComponent<MeshRenderer>();
        MeshCollider meshCollider = groundObject.AddComponent<MeshCollider>();

        Mesh terrainMesh = GenerateTerrainMesh();
        meshFilter.mesh = terrainMesh;
        meshCollider.sharedMesh = terrainMesh;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        groundMaterial = CreateColoredMaterial(shader, groundColor);
        meshRenderer.material = groundMaterial;

        Debug.Log("Museum terrain ground created at: " + groundObject.transform.position);
    }

    private Mesh GenerateTerrainMesh()
    {
        Mesh mesh = new Mesh();
        mesh.name = "Procedural Terrain Mesh";

        int resolution = Mathf.Max(terrainResolution, 2);
        int vertexCountPerSide = resolution + 1;

        Vector3[] vertices = new Vector3[vertexCountPerSide * vertexCountPerSide];
        Vector2[] uvs = new Vector2[vertices.Length];
        int[] triangles = new int[resolution * resolution * 6];

        float halfSize = groundSize * 0.5f;
        int vertexIndex = 0;

        for (int z = 0; z <= resolution; z++)
        {
            for (int x = 0; x <= resolution; x++)
            {
                float percentX = x / (float)resolution;
                float percentZ = z / (float)resolution;

                float localX = Mathf.Lerp(-halfSize, halfSize, percentX);
                float localZ = Mathf.Lerp(-halfSize, halfSize, percentZ);
                float localY = CalculateTerrainLocalY(localX, localZ);

                vertices[vertexIndex] = new Vector3(localX, localY, localZ);
                uvs[vertexIndex] = new Vector2(percentX, percentZ);

                vertexIndex++;
            }
        }

        int triangleIndex = 0;

        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int bottomLeft = z * vertexCountPerSide + x;
                int bottomRight = bottomLeft + 1;
                int topLeft = bottomLeft + vertexCountPerSide;
                int topRight = topLeft + 1;

                triangles[triangleIndex++] = bottomLeft;
                triangles[triangleIndex++] = topLeft;
                triangles[triangleIndex++] = topRight;

                triangles[triangleIndex++] = bottomLeft;
                triangles[triangleIndex++] = topRight;
                triangles[triangleIndex++] = bottomRight;
            }
        }

        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    private float CalculateTerrainLocalY(float localX, float localZ)
    {
        float noise1 = Mathf.PerlinNoise(
            localX * terrainNoiseScale + 100f,
            localZ * terrainNoiseScale + 100f
        );

        float noise2 = Mathf.PerlinNoise(
            localX * terrainSecondNoiseScale + 300f,
            localZ * terrainSecondNoiseScale + 300f
        );

        float combinedNoise =
            (noise1 - 0.5f) +
            (noise2 - 0.5f) * terrainSecondNoiseWeight;

        float distanceFromCenter = new Vector2(localX, localZ).magnitude;

        // Make the edges flatter so the terrain does not look too abrupt.
        float edgeFade = Mathf.InverseLerp(
            groundSize * 0.5f,
            groundSize * 0.2f,
            distanceFromCenter
        );

        return combinedNoise * terrainHeight * edgeFade;
    }

    private void CreateForest()
    {
        if (groundObject == null)
        {
            return;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        trunkMaterial = CreateColoredMaterial(shader, trunkColor);
        leafMaterial = CreateColoredMaterial(shader, leafColor);

        Transform forestRoot = new GameObject("Procedural Forest").transform;

        System.Random random = new System.Random(12345);
        float halfSize = groundSize * 0.5f;

        int created = 0;
        int attempts = 0;
        int maxAttempts = treeCount * 10;

        while (created < treeCount && attempts < maxAttempts)
        {
            attempts++;

            float localX = Mathf.Lerp(
                -halfSize * 0.9f,
                halfSize * 0.9f,
                (float)random.NextDouble()
            );

            float localZ = Mathf.Lerp(
                -halfSize * 0.9f,
                halfSize * 0.9f,
                (float)random.NextDouble()
            );

            Vector2 localXZ = new Vector2(localX, localZ);

            if (localXZ.magnitude < treeAvoidCenterRadius)
            {
                continue;
            }

            float localY = CalculateTerrainLocalY(localX, localZ);
            Vector3 worldPosition = groundObject.transform.TransformPoint(
                new Vector3(localX, localY, localZ)
            );

            // Avoid placing trees directly in front of the user's view.
            // This prevents leaves/trunks from blocking the world-space UI.
            if (IsInsideCameraViewClearZone(worldPosition))
            {
                continue;
            }

            float height = Mathf.Lerp(
                treeMinHeight,
                treeMaxHeight,
                (float)random.NextDouble()
            );

            float radius = height * 0.18f;

            CreateTree(worldPosition, height, radius, forestRoot);

            created++;
        }
    }

    private bool IsInsideCameraViewClearZone(Vector3 worldPosition)
    {
        if (cameraTransform == null)
        {
            return false;
        }

        Vector3 cameraPosition = cameraTransform.position;

        Vector3 flatToTree = worldPosition - cameraPosition;
        flatToTree.y = 0f;

        if (flatToTree.magnitude < treeAvoidCameraRadius)
        {
            return true;
        }

        Vector3 forward = cameraTransform.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude < 0.001f)
        {
            forward = Vector3.forward;
        }

        forward.Normalize();

        Vector3 right = cameraTransform.right;
        right.y = 0f;

        if (right.sqrMagnitude < 0.001f)
        {
            right = Vector3.right;
        }

        right.Normalize();

        float forwardDistance = Vector3.Dot(flatToTree, forward);
        float sideDistance = Mathf.Abs(Vector3.Dot(flatToTree, right));

        bool isInFrontOfCamera = forwardDistance > 0f;
        bool isWithinClearDepth = forwardDistance < treeViewClearDepth;
        bool isWithinClearWidth = sideDistance < treeViewClearHalfWidth;

        return isInFrontOfCamera && isWithinClearDepth && isWithinClearWidth;
    }

    private void CreateTree(Vector3 basePosition, float height, float radius, Transform parent)
    {
        float trunkHeight = height * 0.45f;
        float leafSize = height * 0.55f;

        GameObject trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        trunk.name = "Tree Trunk";
        trunk.transform.SetParent(parent);
        trunk.transform.position = basePosition + Vector3.up * (trunkHeight * 0.5f);
        trunk.transform.localScale = new Vector3(
            radius * 0.45f,
            trunkHeight * 0.5f,
            radius * 0.45f
        );

        Renderer trunkRenderer = trunk.GetComponent<Renderer>();
        trunkRenderer.material = trunkMaterial;

        Collider trunkCollider = trunk.GetComponent<Collider>();
        if (trunkCollider != null)
        {
            trunkCollider.enabled = false;
        }

        GameObject leaves = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        leaves.name = "Tree Leaves";
        leaves.transform.SetParent(parent);
        leaves.transform.position = basePosition + Vector3.up * (trunkHeight + leafSize * 0.45f);
        leaves.transform.localScale = new Vector3(
            leafSize * 0.7f,
            leafSize,
            leafSize * 0.7f
        );

        Renderer leafRenderer = leaves.GetComponent<Renderer>();
        leafRenderer.material = leafMaterial;

        Collider leafCollider = leaves.GetComponent<Collider>();
        if (leafCollider != null)
        {
            leafCollider.enabled = false;
        }
    }

    private Material CreateColoredMaterial(Shader shader, Color color)
    {
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

    private void CreateMarker()
    {
        // Use a flat quad marker instead of a cylinder, so the selected point does not look like a 3D object.
        markerObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
        markerObject.name = "Selected Museum Position Marker";

        // Quad is vertical by default. Rotate it to lie flat on the XZ ground plane.
        markerObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        markerObject.transform.localScale = new Vector3(markerSize, markerSize, 1f);
        markerObject.SetActive(false);

        Renderer renderer = markerObject.GetComponent<Renderer>();

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material mat = CreateColoredMaterial(shader, markerColor);
        renderer.material = mat;

        Collider markerCollider = markerObject.GetComponent<Collider>();
        if (markerCollider != null)
        {
            markerCollider.enabled = false;
        }
    }

    private void CreateHintUI()
    {
        GameObject canvasObj = new GameObject("Position Selection Hint UI");

        hintCanvas = canvasObj.AddComponent<Canvas>();
        hintCanvas.renderMode = RenderMode.WorldSpace;

        Camera cam = GetSelectionCamera();
        if (cam != null)
        {
            hintCanvas.worldCamera = cam;
        }

        RectTransform canvasRect = hintCanvas.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(700, 120);
        canvasRect.localScale = new Vector3(0.002f, 0.002f, 0.002f);

        Image bg = canvasObj.AddComponent<Image>();
        bg.color = new Color(0.05f, 0.05f, 0.05f, 0.65f);
        bg.raycastTarget = false;

        GameObject textObj = new GameObject("Hint Text");
        textObj.transform.SetParent(canvasObj.transform, false);

        hintText = textObj.AddComponent<Text>();
        hintText.text = "Select a position to build the museum";
        hintText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        hintText.fontSize = 30;
        hintText.color = Color.white;
        hintText.alignment = TextAnchor.MiddleCenter;

        RectTransform textRect = hintText.GetComponent<RectTransform>();
        textRect.anchoredPosition = Vector2.zero;
        textRect.sizeDelta = new Vector2(660, 100);
    }

    private void UpdateHintUIPosition()
    {
        if (cameraTransform == null || hintCanvas == null || hasSelectedPosition)
        {
            return;
        }

        hintCanvas.transform.position = cameraTransform.TransformPoint(hintLocalOffset);
        hintCanvas.transform.rotation = Quaternion.LookRotation(
            hintCanvas.transform.position - cameraTransform.position
        );
    }

    private void HandleGroundSelection()
    {
        bool shouldSelect = false;
        Ray ray = new Ray();

#if UNITY_EDITOR
        if (Input.GetMouseButtonDown(0))
        {
            Camera cam = GetSelectionCamera();

            if (cam == null)
            {
                Debug.LogWarning("No valid camera found for ground selection.");
                return;
            }

            ray = cam.ScreenPointToRay(Input.mousePosition);
            shouldSelect = true;

            Debug.Log("Mouse clicked. Camera: " + cam.name);
        }
#endif

#if UNITY_ANDROID
        bool triggerPressed = GetXRTriggerPressed();

        if (triggerPressed && !wasXRTriggerPressed)
        {
            if (cameraTransform != null)
            {
                ray = new Ray(cameraTransform.position, cameraTransform.forward);
                shouldSelect = true;
            }
        }

        wasXRTriggerPressed = triggerPressed;
#endif

        if (!shouldSelect)
        {
            return;
        }

        if (groundObject == null)
        {
            Debug.LogWarning("Ground object does not exist.");
            return;
        }

        RaycastHit[] hits = Physics.RaycastAll(ray, 100f);

        if (hits.Length == 0)
        {
            Debug.Log("Ray did not hit any object.");
            return;
        }

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider != null && hit.collider.gameObject == groundObject)
            {
                SelectPosition(hit.point);
                Debug.Log("Terrain position selected: " + hit.point);
                return;
            }
        }

        Debug.Log("Ray hit something, but not the terrain ground.");
    }

    private float GetGroundSurfaceY()
    {
        if (groundObject == null)
        {
            return 0f;
        }

        return groundObject.transform.position.y;
    }

    private void SelectPosition(Vector3 position)
    {
        hasSelectedPosition = true;

        Vector3 markerPosition = position;
        markerPosition.y = position.y + markerYOffset;

        markerObject.transform.position = markerPosition;
        markerObject.SetActive(true);

        if (hintCanvas != null)
        {
            hintCanvas.gameObject.SetActive(false);
        }

        if (speechToTextUI != null)
        {
            speechToTextUI.OpenPromptUI(position);
        }
        else
        {
            Debug.LogWarning("Position selected, but SpeechToTextUI is not assigned.");
        }

        Debug.Log("Selected museum position: " + position);
    }

    private Camera GetSelectionCamera()
    {
        Camera cam = null;

        if (cameraTransform != null)
        {
            cam = cameraTransform.GetComponent<Camera>();

            if (cam == null)
            {
                cam = cameraTransform.GetComponentInChildren<Camera>();
            }

            if (cam == null && cameraTransform.parent != null)
            {
                cam = cameraTransform.parent.GetComponentInChildren<Camera>();
            }
        }

        if (cam == null)
        {
            cam = Camera.main;
        }

        return cam;
    }

    private bool GetXRTriggerPressed()
    {
        List<InputDevice> devices = new List<InputDevice>();

        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller,
            devices
        );

        foreach (InputDevice device in devices)
        {
            if (device.TryGetFeatureValue(CommonUsages.triggerButton, out bool pressed) && pressed)
            {
                return true;
            }
        }

        return false;
    }
}

