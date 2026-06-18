using System.Collections;
using UnityEngine;

public class MuseumEnvironmentGenerator : MonoBehaviour
{
    [Header("References")]
    public Transform cameraTransform;
    
    [Tooltip("Drag your manually created 3D Plane here.")]
    public Transform groundPlane;

    [Header("Forest Settings")]
    // 移除了 forestSize 变量，现在将自动读取 groundPlane 的尺寸
    public int treeCount = 22;
    public float treeMinHeight = 0.7f;
    public float treeMaxHeight = 1.4f;
    public float treeAvoidCenterRadius = 5f;

    [Tooltip("Keep a corridor in front of the camera free of trees, so trees do not block the main view.")]
    public float treeViewClearHalfWidth = 3.2f;

    [Tooltip("Depth of the tree-free corridor in front of the camera.")]
    public float treeViewClearDepth = 8f;

    [Tooltip("Minimum horizontal distance from the camera to any generated tree.")]
    public float treeAvoidCameraRadius = 2.5f;

    public Color trunkColor = new Color(0.35f, 0.18f, 0.08f, 1f);
    public Color leafColor = new Color(0.12f, 0.45f, 0.16f, 1f);

    private Material trunkMaterial;
    private Material leafMaterial;

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

        StartCoroutine(InitializeAfterCameraReady());
    }

    private IEnumerator InitializeAfterCameraReady()
    {
        // Wait for XR Camera Rig to update its real pose.
        yield return null;
        yield return null;
        yield return null;

        CreateForest();
    }

    private void CreateForest()
    {
        if (groundPlane == null)
        {
            Debug.LogError("Ground Plane is not assigned! Please assign the manual ground plane in the Inspector.");
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

        // 核心修改：自动读取 Plane 的尺寸
        // Unity 默认的 Plane 在 Scale 为 1 时，物理尺寸是 10x10 米
        float actualWidthX = groundPlane.lossyScale.x * 10f;
        float actualLengthZ = groundPlane.lossyScale.z * 10f;

        float halfX = actualWidthX * 0.5f;
        float halfZ = actualLengthZ * 0.5f;

        int created = 0;
        int attempts = 0;
        int maxAttempts = treeCount * 10;

        while (created < treeCount && attempts < maxAttempts)
        {
            attempts++;

            // 乘以 0.9f 是为了留出 10% 的边缘安全区，防止树木长在草地外面一半
            float offsetX = Mathf.Lerp(-halfX * 0.9f, halfX * 0.9f, (float)random.NextDouble());
            float offsetZ = Mathf.Lerp(-halfZ * 0.9f, halfZ * 0.9f, (float)random.NextDouble());

            Vector2 localXZ = new Vector2(offsetX, offsetZ);

            if (localXZ.magnitude < treeAvoidCenterRadius)
            {
                continue;
            }

            // Calculate tree position based on the manually placed ground plane
            Vector3 worldPosition = groundPlane.position + new Vector3(offsetX, 0f, offsetZ);
            worldPosition.y = groundPlane.position.y;

            if (IsInsideCameraViewClearZone(worldPosition))
            {
                continue;
            }

            float height = Mathf.Lerp(treeMinHeight, treeMaxHeight, (float)random.NextDouble());
            float radius = height * 0.18f;

            CreateTree(worldPosition, height, radius, forestRoot);
            created++;
        }
        
        Debug.Log($"Procedural Forest generated on manual ground. Auto-detected area size: {actualWidthX}m x {actualLengthZ}m");
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
        forward = forward.sqrMagnitude < 0.001f ? Vector3.forward : forward.normalized;

        Vector3 right = cameraTransform.right;
        right.y = 0f;
        right = right.sqrMagnitude < 0.001f ? Vector3.right : right.normalized;

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
        trunk.transform.localScale = new Vector3(radius * 0.45f, trunkHeight * 0.5f, radius * 0.45f);

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
        leaves.transform.localScale = new Vector3(leafSize * 0.7f, leafSize, leafSize * 0.7f);

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
}