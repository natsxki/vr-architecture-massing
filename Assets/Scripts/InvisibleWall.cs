using UnityEngine;

public class InvisibleBoundsGenerator : MonoBehaviour
{
    [Header("Bounds Settings")]
    [Tooltip("Height of the invisible walls (meters)")]
    public float wallHeight = 10f;
    
    [Tooltip("Thickness of the invisible walls (meters)")]
    public float wallThickness = 1f;

    [Tooltip("Check this option: Keep the mesh renderer to make the walls visible during testing. Uncheck: Make walls fully transparent.")]
    public bool showWallsForDebugging = false;

    [Tooltip("Is the object you attached a standard Unity 3D Plane? (The standard Plane has a base size of 10x10 meters)")]
    public bool isStandardUnityPlane = true;

    void Start()
    {
        GenerateBounds();
    }

    private void GenerateBounds()
    {
        // Auto-calculate actual physical dimensions (Unity's default Plane has a size of 10x10 meters when Scale is 1)
        float sizeMultiplier = isStandardUnityPlane ? 10f : 1f;
        float actualWidthX = transform.lossyScale.x * sizeMultiplier;
        float actualLengthZ = transform.lossyScale.z * sizeMultiplier;

        Vector3 center = transform.position;
        float halfWidth = actualWidthX * 0.5f;
        float halfLength = actualLengthZ * 0.5f;
        float halfHeight = wallHeight * 0.5f;
        float halfThickness = wallThickness * 0.5f;

        // Create a parent GameObject to hold the walls
        GameObject boundsRoot = new GameObject("Auto_Invisible_Bounds");
        boundsRoot.transform.SetParent(transform); 
        boundsRoot.transform.localPosition = Vector3.zero;

        // Generate North wall (+Z)
        CreateWall("Wall_North", 
            new Vector3(center.x, center.y + halfHeight, center.z + halfLength + halfThickness), 
            new Vector3(actualWidthX + wallThickness * 2, wallHeight, wallThickness), 
            boundsRoot.transform);

        // Generate South wall (-Z)
        CreateWall("Wall_South", 
            new Vector3(center.x, center.y + halfHeight, center.z - halfLength - halfThickness), 
            new Vector3(actualWidthX + wallThickness * 2, wallHeight, wallThickness), 
            boundsRoot.transform);

        // Generate East wall (+X)
        CreateWall("Wall_East", 
            new Vector3(center.x + halfWidth + halfThickness, center.y + halfHeight, center.z), 
            new Vector3(wallThickness, wallHeight, actualLengthZ), 
            boundsRoot.transform);

        // Generate West wall (-X)
        CreateWall("Wall_West", 
            new Vector3(center.x - halfWidth - halfThickness, center.y + halfHeight, center.z), 
            new Vector3(wallThickness, wallHeight, actualLengthZ), 
            boundsRoot.transform);
            
        Debug.Log($"Invisible walls have been generated for {gameObject.name}. Protected area: {actualWidthX}m x {actualLengthZ}m");
    }

    private void CreateWall(string objectName, Vector3 position, Vector3 scale, Transform parent)
    {
        // 生成默认的立方体
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = objectName;
        wall.transform.position = position;
        wall.transform.localScale = scale;
        wall.transform.SetParent(parent, true);

        // 根据设置决定是否隐身
        if (!showWallsForDebugging)
        {
            MeshRenderer renderer = wall.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                // 直接销毁渲染器组件，只保留 BoxCollider 物理碰撞体
                Destroy(renderer); 
            }
        }
    }
}