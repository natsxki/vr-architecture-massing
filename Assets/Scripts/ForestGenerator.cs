using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

public class ForestGenerator : MonoBehaviour
{
    [Header("References")]
    public Transform cameraTransform;
    [Tooltip("Drag your manually created 3D Plane here.")]
    public Transform groundPlane;

    // ── Inner area (inside the invisible walls) ───────────────────────────────

    [Header("Tree Prefabs")]
    public GameObject[] treePrefabs;
    public float treeScaleMultiplier   = 1f;
    public bool  treeRandomYRotation   = true;

    [Header("Tree Settings")]
    public int   treeCount              = 22;
    public float treeAvoidCenterRadius  = 5f;
    public float treeViewClearHalfWidth = 3.2f;
    public float treeViewClearDepth     = 8f;
    public float treeAvoidCameraRadius  = 2.5f;

    [Header("Grass Prefabs")]
    public GameObject[] grassPrefabs;
    public float grassScaleMultiplier   = 1f;
    public bool  grassRandomYRotation   = true;

    [Header("Grass Settings")]
    public int   grassCount             = 300;
    public float grassAvoidCenterRadius = 1f;

    // ── Outer ring — ground extension ────────────────────────────────────────

    [Header("Outer Ring — Extent")]
    [Tooltip("How many times larger the outer visible area is vs the playable area.")]
    public float outerRingScale = 5f;

    [Header("Outer Ring — Terrain Shape")]
    [Tooltip("Vertex spacing of the procedural terrain mesh (meters). Lower = smoother but heavier.")]
    public float terrainResolution  = 2f;
    [Tooltip("Maximum height variation of the outer terrain (meters). Keep very small — 0.06 = 6 cm.")]
    [Range(0f, 0.15f)]
    public float terrainAmplitude   = 0.06f;
    [Tooltip("Flat buffer after the wall before any height variation starts (meters).")]
    public float terrainBlendStart  = 2f;
    [Tooltip("Width of the smooth ramp from flat to full amplitude (meters).")]
    public float terrainBlendWidth  = 10f;
    [Tooltip("Noise frequency — lower = broad rolling hills, higher = jagged.")]
    public float terrainFrequency   = 0.035f;

    [Header("Outer Ring — Trees")]
    public int   outerTreeCount            = 120;
    [Tooltip("Scale relative to treeScaleMultiplier (1 = same size as inner trees).")]
    public float outerTreeScaleMultiplier  = 1f;
    [Tooltip("Scale relative to treeScaleMultiplier for the dense border band.")]
    public float borderTreeScaleMultiplier = 1.1f;
    [Tooltip("Width of the dense border band right outside the playable area (meters).")]
    public float borderBandWidth           = 6f;
    public int   borderTreeCount           = 60;

    [Header("Outer Ring — Grass")]
    public int   outerGrassCount           = 500;
    public float outerGrassScaleMultiplier = 1f;

    [Header("Outer Ring — Rocks")]
    [Tooltip("Drag rock/boulder prefabs here (optional).")]
    public GameObject[] rockPrefabs;
    public float rockScaleMultiplier  = 1f;
    public bool  rockRandomYRotation  = true;
    public int   outerRockCount       = 40;
    public float rockAvoidInnerMargin = 2f;

    // ─────────────────────────────────────────────────────────────────────────

    private System.Random _rng;
    private float _innerHalfX, _innerHalfZ;
    private float _outerHalfX, _outerHalfZ;

    private void Start()
    {
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
        StartCoroutine(InitializeAfterCameraReady());
    }

    private IEnumerator InitializeAfterCameraReady()
    {
        yield return null;
        yield return null;
        yield return null;
        yield return StartCoroutine(CreateForest());
    }

    // ─────────────────────────────────────────────────────────────────────────

    private IEnumerator CreateForest()
    {
        if (groundPlane == null) { Debug.LogError("[ForestGenerator] Ground Plane not assigned."); yield break; }

        _rng = new System.Random(12345);

        _innerHalfX = groundPlane.lossyScale.x * 10f * 0.5f;
        _innerHalfZ = groundPlane.lossyScale.z * 10f * 0.5f;
        _outerHalfX = _innerHalfX * outerRingScale;
        _outerHalfZ = _innerHalfZ * outerRingScale;

        CreateOuterTerrain();
        yield return null;

        yield return StartCoroutine(SpawnInnerTrees());
        yield return StartCoroutine(SpawnInnerGrass());
        yield return StartCoroutine(SpawnBorderTrees());
        yield return StartCoroutine(SpawnOuterTrees());
        yield return StartCoroutine(SpawnOuterGrass());
        yield return StartCoroutine(SpawnOuterRocks());

        Debug.Log("[ForestGenerator] Scene ready.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Terrain height — zero at/inside walls, Perlin hills beyond
    // ─────────────────────────────────────────────────────────────────────────

    private float TerrainHeight(float ox, float oz)
    {
        // How far outside the rectangular playable boundary this point is
        float distOutX   = Mathf.Max(0f, Mathf.Abs(ox) - _innerHalfX);
        float distOutZ   = Mathf.Max(0f, Mathf.Abs(oz) - _innerHalfZ);
        float distOutside = Mathf.Max(distOutX, distOutZ);

        // Flat buffer for `terrainBlendStart` meters, then smooth ramp to full amplitude
        float blend = Mathf.SmoothStep(terrainBlendStart, terrainBlendStart + terrainBlendWidth, distOutside);
        if (blend <= 0f) return 0f;

        // Use local offsets (ox, oz) for noise so the pattern is centred on the
        // playable area rather than on world-space coordinates that may be biased.
        // Each octave is explicitly centred: subtract 0.5 before combining.
        float n1 = Mathf.PerlinNoise(ox * terrainFrequency + 73.4f,
                                      oz * terrainFrequency + 19.1f) - 0.5f;   // -0.5 … +0.5
        float n2 = (Mathf.PerlinNoise(ox * terrainFrequency * 2.7f + 131f,
                                       oz * terrainFrequency * 2.7f + 211f) - 0.5f) * 0.4f;
        float noise = (n1 + n2) / 1.4f;                                    // zero-mean, ≈ -0.5 … +0.5

        float amp = Mathf.Clamp(terrainAmplitude, 0f, 0.06f);             // hard cap at 6 cm
        return noise * 2f * amp * blend;                                   // -amp … +amp
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Procedural outer terrain mesh
    // ─────────────────────────────────────────────────────────────────────────

    private void CreateOuterTerrain()
    {
        int resX = Mathf.Max(4, Mathf.RoundToInt(_outerHalfX * 2f / terrainResolution));
        int resZ = Mathf.Max(4, Mathf.RoundToInt(_outerHalfZ * 2f / terrainResolution));
        int vx = resX + 1, vz = resZ + 1;

        Vector3[] verts = new Vector3[vx * vz];
        Vector2[] uvs   = new Vector2[verts.Length];

        // Pre-compute local X/Z for each grid column/row (reused below)
        float[] xs = new float[vx];
        float[] zs = new float[vz];
        for (int x = 0; x < vx; x++) xs[x] = Mathf.Lerp(-_outerHalfX, _outerHalfX, (float)x / resX);
        for (int z = 0; z < vz; z++) zs[z] = Mathf.Lerp(-_outerHalfZ, _outerHalfZ, (float)z / resZ);

        for (int z = 0; z < vz; z++)
            for (int x = 0; x < vx; x++)
            {
                int i    = z * vx + x;
                verts[i] = new Vector3(xs[x], TerrainHeight(xs[x], zs[z]), zs[z]);
                uvs[i]   = new Vector2((float)x / resX, (float)z / resZ);
            }

        // Only generate triangles for cells that are NOT fully inside the inner area.
        // This prevents the terrain mesh from competing with the inner ground plane,
        // which was causing z-fighting that buried inner grass/objects.
        var tris = new System.Collections.Generic.List<int>(resX * resZ * 3);
        for (int z = 0; z < resZ; z++)
        {
            for (int x = 0; x < resX; x++)
            {
                // Skip if all four corners of this cell lie inside the playable rectangle
                if (IsInsideRect(xs[x],   zs[z],   _innerHalfX, _innerHalfZ) &&
                    IsInsideRect(xs[x+1], zs[z],   _innerHalfX, _innerHalfZ) &&
                    IsInsideRect(xs[x],   zs[z+1], _innerHalfX, _innerHalfZ) &&
                    IsInsideRect(xs[x+1], zs[z+1], _innerHalfX, _innerHalfZ))
                    continue;

                int bl = z * vx + x;
                int br = bl + 1, tl = bl + vx, tr = tl + 1;
                tris.Add(bl); tris.Add(tl); tris.Add(br);
                tris.Add(br); tris.Add(tl); tris.Add(tr);
            }
        }

        Mesh mesh = new Mesh { name = "OuterTerrain" };
        if (verts.Length > 65535) mesh.indexFormat = IndexFormat.UInt32;
        mesh.vertices  = verts;
        mesh.uv        = uvs;
        mesh.triangles = tris.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        GameObject go = new GameObject("OuterGround");
        go.transform.position = groundPlane.position - Vector3.up * 0.004f;
        go.transform.SetParent(transform);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        if (groundPlane.TryGetComponent<Renderer>(out var ir))
            mr.sharedMaterial = ir.sharedMaterial;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Inner spawning (unchanged)
    // ─────────────────────────────────────────────────────────────────────────

    private IEnumerator SpawnInnerTrees()
    {
        if (treePrefabs == null || treePrefabs.Length == 0) yield break;
        Transform root = new GameObject("Procedural Forest").transform;
        int created = 0, attempts = 0, max = treeCount * 10;
        while (attempts < max && created < treeCount)
        {
            attempts++;
            float ox = RandRange(-_innerHalfX * 0.9f, _innerHalfX * 0.9f);
            float oz = RandRange(-_innerHalfZ * 0.9f, _innerHalfZ * 0.9f);
            if (new Vector2(ox, oz).magnitude < treeAvoidCenterRadius) continue;
            Vector3 pos = FlatPos(ox, oz);
            if (IsInsideCameraViewClearZone(pos)) continue;
            SpawnPrefab(treePrefabs, pos, treeScaleMultiplier, treeRandomYRotation, root);
            created++;
            if (created % 10 == 0) yield return null;
        }
        Debug.Log($"[ForestGenerator] Inner trees: {created}");
    }

    private IEnumerator SpawnInnerGrass()
    {
        if (grassPrefabs == null || grassPrefabs.Length == 0) yield break;
        Transform root = new GameObject("Procedural Grass").transform;
        int created = 0, attempts = 0, max = grassCount * 5;
        while (attempts < max && created < grassCount)
        {
            attempts++;
            float ox = RandRange(-_innerHalfX * 0.95f, _innerHalfX * 0.95f);
            float oz = RandRange(-_innerHalfZ * 0.95f, _innerHalfZ * 0.95f);
            if (new Vector2(ox, oz).magnitude < grassAvoidCenterRadius) continue;
            SpawnPrefab(grassPrefabs, FlatPos(ox, oz), grassScaleMultiplier, grassRandomYRotation, root);
            created++;
            if (created % 10 == 0) yield return null;
        }
        Debug.Log($"[ForestGenerator] Inner grass: {created}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Outer spawning — objects sit on the hilly terrain
    // ─────────────────────────────────────────────────────────────────────────

    private IEnumerator SpawnBorderTrees()
    {
        if (treePrefabs == null || treePrefabs.Length == 0) yield break;
        float outerX = _innerHalfX + borderBandWidth;
        float outerZ = _innerHalfZ + borderBandWidth;
        Transform root = new GameObject("Border Trees").transform;
        int created = 0, attempts = 0, max = borderTreeCount * 15;
        while (attempts < max && created < borderTreeCount)
        {
            attempts++;
            float ox = RandRange(-outerX, outerX);
            float oz = RandRange(-outerZ, outerZ);
            if (IsInsideRect(ox, oz, _innerHalfX, _innerHalfZ)) continue;
            if (!IsInsideRect(ox, oz, outerX, outerZ)) continue;
            SpawnPrefab(treePrefabs, TerrainPos(ox, oz),
                        treeScaleMultiplier * borderTreeScaleMultiplier, treeRandomYRotation, root);
            created++;
            if (created % 10 == 0) yield return null;
        }
        Debug.Log($"[ForestGenerator] Border trees: {created}");
    }

    private IEnumerator SpawnOuterTrees()
    {
        if (treePrefabs == null || treePrefabs.Length == 0) yield break;
        float startX = _innerHalfX + borderBandWidth;
        float startZ = _innerHalfZ + borderBandWidth;
        Transform root = new GameObject("Outer Forest").transform;
        int created = 0, attempts = 0, max = outerTreeCount * 10;
        while (attempts < max && created < outerTreeCount)
        {
            attempts++;
            float ox = RandRange(-_outerHalfX, _outerHalfX);
            float oz = RandRange(-_outerHalfZ, _outerHalfZ);
            if (IsInsideRect(ox, oz, startX, startZ)) continue;
            SpawnPrefab(treePrefabs, TerrainPos(ox, oz),
                        treeScaleMultiplier * outerTreeScaleMultiplier, treeRandomYRotation, root);
            created++;
            if (created % 10 == 0) yield return null;
        }
        Debug.Log($"[ForestGenerator] Outer trees: {created}");
    }

    private IEnumerator SpawnOuterGrass()
    {
        if (grassPrefabs == null || grassPrefabs.Length == 0) yield break;
        Transform root = new GameObject("Outer Grass").transform;
        int created = 0, attempts = 0, max = outerGrassCount * 5;
        while (attempts < max && created < outerGrassCount)
        {
            attempts++;
            float ox = RandRange(-_outerHalfX * 0.98f, _outerHalfX * 0.98f);
            float oz = RandRange(-_outerHalfZ * 0.98f, _outerHalfZ * 0.98f);
            if (IsInsideRect(ox, oz, _innerHalfX, _innerHalfZ)) continue;
            SpawnPrefab(grassPrefabs, TerrainPos(ox, oz),
                        outerGrassScaleMultiplier, grassRandomYRotation, root);
            created++;
            if (created % 10 == 0) yield return null;
        }
        Debug.Log($"[ForestGenerator] Outer grass: {created}");
    }

    private IEnumerator SpawnOuterRocks()
    {
        if (rockPrefabs == null || rockPrefabs.Length == 0) yield break;
        float mx = _innerHalfX + rockAvoidInnerMargin;
        float mz = _innerHalfZ + rockAvoidInnerMargin;
        Transform root = new GameObject("Outer Rocks").transform;
        int created = 0, attempts = 0, max = outerRockCount * 10;
        while (attempts < max && created < outerRockCount)
        {
            attempts++;
            float ox = RandRange(-_outerHalfX * 0.95f, _outerHalfX * 0.95f);
            float oz = RandRange(-_outerHalfZ * 0.95f, _outerHalfZ * 0.95f);
            if (IsInsideRect(ox, oz, mx, mz)) continue;
            SpawnPrefab(rockPrefabs, TerrainPos(ox, oz),
                        rockScaleMultiplier, rockRandomYRotation, root);
            created++;
            if (created % 10 == 0) yield return null;
        }
        Debug.Log($"[ForestGenerator] Outer rocks: {created}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>World position at ground plane level (flat inner area).</summary>
    private Vector3 FlatPos(float ox, float oz)
        => new Vector3(groundPlane.position.x + ox, groundPlane.position.y, groundPlane.position.z + oz);

    /// <summary>World position snapped to the procedural terrain height.</summary>
    private Vector3 TerrainPos(float ox, float oz)
        => new Vector3(groundPlane.position.x + ox,
                       groundPlane.position.y + TerrainHeight(ox, oz),
                       groundPlane.position.z + oz);

    private static bool IsInsideRect(float ox, float oz, float hx, float hz)
        => Mathf.Abs(ox) <= hx && Mathf.Abs(oz) <= hz;

    private float RandRange(float min, float max)
        => Mathf.Lerp(min, max, (float)_rng.NextDouble());

    private void SpawnPrefab(GameObject[] prefabs, Vector3 position,
                              float scale, bool randRot, Transform parent)
    {
        int idx = _rng.Next(0, prefabs.Length);
        if (prefabs[idx] == null) return;
        float yRot = randRot ? _rng.Next(0, 360) : 0f;
        Instantiate(prefabs[idx], position, Quaternion.Euler(0f, yRot, 0f), parent)
            .transform.localScale = Vector3.one * scale;
    }

    private bool IsInsideCameraViewClearZone(Vector3 worldPos)
    {
        if (cameraTransform == null) return false;
        Vector3 toTree = worldPos - cameraTransform.position; toTree.y = 0f;
        if (toTree.magnitude < treeAvoidCameraRadius) return true;
        Vector3 fwd = cameraTransform.forward; fwd.y = 0f;
        fwd = fwd.sqrMagnitude < 0.001f ? Vector3.forward : fwd.normalized;
        Vector3 right = cameraTransform.right; right.y = 0f;
        right = right.sqrMagnitude < 0.001f ? Vector3.right : right.normalized;
        float fd = Vector3.Dot(toTree, fwd);
        float sd = Mathf.Abs(Vector3.Dot(toTree, right));
        return fd > 0f && fd < treeViewClearDepth && sd < treeViewClearHalfWidth;
    }
}
