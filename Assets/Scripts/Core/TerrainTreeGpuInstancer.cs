using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Terrain ağaçlarını RenderMeshInstanced ile çizer.
/// Mekânsal grid ile sadece kamera çevresindeki hücreler cull edilir —
/// tüm ormanı her birkaç karede taramak FPS spike yapmaz.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Terrain))]
public class TerrainTreeGpuInstancer : MonoBehaviour
{
    private const int MaxInstancesPerBatch = 1023;

    [Header("Draw Distance")]
    [SerializeField] private float maxDrawDistance = 750f;
    [Tooltip("Bu mesafeden sonra LOD1 / billboard çizilir.")]
    [SerializeField] private float lodSwitchDistance = 45f;
    [SerializeField] private float shadowDistance = 50f;

    [Header("Spatial Grid")]
    [Tooltip("Cull grid hücre boyutu (metre). Küçük = daha az ağaç taranır, daha fazla hücre.")]
    [SerializeField] private float cellSize = 32f;

    [Header("Rendering")]
    [SerializeField] private bool castShadows = true;
    [SerializeField] private bool receiveShadows = true;
    [Tooltip("Terrain'in kendi tree çizimini kapatır (ot/detail etkilenmez).")]
    [SerializeField] private bool disableTerrainTreeDrawing = true;

    [Header("Debug")]
    [SerializeField] private bool logOnStart = true;

    private Terrain _terrain;
    private float _savedTreeDistance;
    private PrototypeBatch[] _batches;
    private Camera _camera;
    private readonly Matrix4x4[] _chunkBuffer = new Matrix4x4[MaxInstancesPerBatch];
    private float _gridOriginX;
    private float _gridOriginZ;
    private int _gridW;
    private int _gridH;
    private float _invCellSize;

    private struct LodMesh
    {
        public Mesh mesh;
        public Material[] materials;
        public bool IsValid => mesh != null && materials != null && materials.Length > 0;
    }

    private struct PrototypeBatch
    {
        public LodMesh lod0;
        public LodMesh lod1;
        public Matrix4x4[] matrices;
        public Vector3[] positions;

        // CSR grid: cellStart[cell], cellStart[cell+1] → cellTreeIndices aralığı
        public int[] cellStart;
        public int[] cellTreeIndices;

        public Matrix4x4[] lod0Shadow;
        public int lod0ShadowCount;
        public Matrix4x4[] lod0NoShadow;
        public int lod0NoShadowCount;
        public Matrix4x4[] lod1Visible;
        public int lod1Count;
    }

    private void Awake()
    {
        _terrain = GetComponent<Terrain>();

        if (disableTerrainTreeDrawing && _terrain != null)
        {
            _savedTreeDistance = _terrain.treeDistance;
            _terrain.treeDistance = 0f;
        }

        BuildBatches();
    }

    private void OnDestroy()
    {
        if (_terrain != null && disableTerrainTreeDrawing)
            _terrain.treeDistance = _savedTreeDistance;
    }

    private void LateUpdate()
    {
        if (_batches == null || _batches.Length == 0)
            return;

        if (_camera == null || !_camera.isActiveAndEnabled)
            _camera = Camera.main;
        if (_camera == null)
            return;

        Vector3 camPos = _camera.transform.position;
        RebuildCullListsFromGrid(camPos);

        for (int b = 0; b < _batches.Length; b++)
        {
            PrototypeBatch batch = _batches[b];
            if (!batch.lod0.IsValid)
                continue;

            if (batch.lod0ShadowCount > 0)
                DrawCached(batch.lod0, batch.lod0Shadow, batch.lod0ShadowCount, shadows: true);
            if (batch.lod0NoShadowCount > 0)
                DrawCached(batch.lod0, batch.lod0NoShadow, batch.lod0NoShadowCount, shadows: false);

            LodMesh farLod = batch.lod1.IsValid ? batch.lod1 : batch.lod0;
            if (batch.lod1Count > 0 && farLod.IsValid)
                DrawCached(farLod, batch.lod1Visible, batch.lod1Count, shadows: false);
        }
    }

    private void RebuildCullListsFromGrid(Vector3 camPos)
    {
        float maxDist = maxDrawDistance;
        float maxDistSq = maxDist * maxDist;
        float lodDistSq = lodSwitchDistance * lodSwitchDistance;
        float shadowDistSq = shadowDistance * shadowDistance;
        bool useShadows = castShadows;

        int minCx = Mathf.FloorToInt((camPos.x - maxDist - _gridOriginX) * _invCellSize);
        int maxCx = Mathf.FloorToInt((camPos.x + maxDist - _gridOriginX) * _invCellSize);
        int minCz = Mathf.FloorToInt((camPos.z - maxDist - _gridOriginZ) * _invCellSize);
        int maxCz = Mathf.FloorToInt((camPos.z + maxDist - _gridOriginZ) * _invCellSize);

        minCx = Mathf.Clamp(minCx, 0, _gridW - 1);
        maxCx = Mathf.Clamp(maxCx, 0, _gridW - 1);
        minCz = Mathf.Clamp(minCz, 0, _gridH - 1);
        maxCz = Mathf.Clamp(maxCz, 0, _gridH - 1);

        float camX = camPos.x;
        float camY = camPos.y;
        float camZ = camPos.z;

        for (int b = 0; b < _batches.Length; b++)
        {
            PrototypeBatch batch = _batches[b];
            if (batch.matrices == null || batch.cellStart == null)
                continue;

            int s0 = 0, s1 = 0, s2 = 0;
            Matrix4x4[] buf0 = batch.lod0Shadow;
            Matrix4x4[] buf1 = batch.lod0NoShadow;
            Matrix4x4[] buf2 = batch.lod1Visible;
            Vector3[] positions = batch.positions;
            Matrix4x4[] matrices = batch.matrices;
            int[] cellStart = batch.cellStart;
            int[] cellTrees = batch.cellTreeIndices;

            for (int cz = minCz; cz <= maxCz; cz++)
            {
                int row = cz * _gridW;
                for (int cx = minCx; cx <= maxCx; cx++)
                {
                    int cell = row + cx;
                    int start = cellStart[cell];
                    int end = cellStart[cell + 1];
                    for (int t = start; t < end; t++)
                    {
                        int i = cellTrees[t];
                        float dx = positions[i].x - camX;
                        float dy = positions[i].y - camY;
                        float dz = positions[i].z - camZ;
                        float distSq = dx * dx + dy * dy + dz * dz;
                        if (distSq > maxDistSq)
                            continue;

                        if (distSq <= lodDistSq)
                        {
                            if (useShadows && distSq <= shadowDistSq)
                                buf0[s0++] = matrices[i];
                            else
                                buf1[s1++] = matrices[i];
                        }
                        else
                        {
                            buf2[s2++] = matrices[i];
                        }
                    }
                }
            }

            batch.lod0ShadowCount = s0;
            batch.lod0NoShadowCount = s1;
            batch.lod1Count = s2;
            _batches[b] = batch;
        }
    }

    private void DrawCached(LodMesh lod, Matrix4x4[] matrices, int count, bool shadows)
    {
        int offset = 0;
        while (offset < count)
        {
            int chunk = Mathf.Min(MaxInstancesPerBatch, count - offset);
            FlushChunk(lod, matrices, offset, chunk, shadows);
            offset += chunk;
        }
    }

    private void FlushChunk(LodMesh lod, Matrix4x4[] matrices, int offset, int count, bool shadows)
    {
        System.Array.Copy(matrices, offset, _chunkBuffer, 0, count);

        int subMeshCount = Mathf.Max(1, lod.mesh.subMeshCount);
        for (int sub = 0; sub < subMeshCount; sub++)
        {
            Material mat = lod.materials[Mathf.Min(sub, lod.materials.Length - 1)];
            if (mat == null)
                continue;

            if (!mat.enableInstancing)
                mat.enableInstancing = true;

            var rp = new RenderParams(mat)
            {
                shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                receiveShadows = receiveShadows,
                layer = gameObject.layer,
                renderingLayerMask = RenderingLayerMask.defaultRenderingLayerMask,
                camera = null,
                lightProbeUsage = LightProbeUsage.Off,
                reflectionProbeUsage = ReflectionProbeUsage.Off
            };

            Graphics.RenderMeshInstanced(rp, lod.mesh, sub, _chunkBuffer, count);
        }
    }

    private void BuildBatches()
    {
        if (_terrain == null || _terrain.terrainData == null)
        {
            _batches = System.Array.Empty<PrototypeBatch>();
            return;
        }

        TerrainData data = _terrain.terrainData;
        TreePrototype[] prototypes = data.treePrototypes;
        TreeInstance[] instances = data.treeInstances;
        if (prototypes == null || prototypes.Length == 0 || instances == null || instances.Length == 0)
        {
            _batches = System.Array.Empty<PrototypeBatch>();
            return;
        }

        var lists = new List<Matrix4x4>[prototypes.Length];
        var positions = new List<Vector3>[prototypes.Length];
        for (int i = 0; i < prototypes.Length; i++)
        {
            lists[i] = new List<Matrix4x4>(256);
            positions[i] = new List<Vector3>(256);
        }

        Vector3 terrainPos = _terrain.transform.position;
        Vector3 size = data.size;
        float cell = Mathf.Max(8f, cellSize);
        _invCellSize = 1f / cell;
        _gridOriginX = terrainPos.x;
        _gridOriginZ = terrainPos.z;
        _gridW = Mathf.Max(1, Mathf.CeilToInt(size.x / cell));
        _gridH = Mathf.Max(1, Mathf.CeilToInt(size.z / cell));
        int cellCount = _gridW * _gridH;

        for (int i = 0; i < instances.Length; i++)
        {
            TreeInstance tree = instances[i];
            int proto = tree.prototypeIndex;
            if (proto < 0 || proto >= prototypes.Length)
                continue;

            GameObject prefab = prototypes[proto].prefab;
            if (prefab == null)
                continue;

            Vector3 worldPos = new Vector3(
                tree.position.x * size.x + terrainPos.x,
                tree.position.y * size.y + terrainPos.y,
                tree.position.z * size.z + terrainPos.z);

            Vector3 prefabScale = prefab.transform.localScale;
            Vector3 scale = new Vector3(
                prefabScale.x * tree.widthScale,
                prefabScale.y * tree.heightScale,
                prefabScale.z * tree.widthScale);

            Quaternion rot = Quaternion.Euler(0f, tree.rotation * Mathf.Rad2Deg, 0f);
            lists[proto].Add(Matrix4x4.TRS(worldPos, rot, scale));
            positions[proto].Add(worldPos);
        }

        var built = new List<PrototypeBatch>(prototypes.Length);
        int totalTrees = 0;
        int withLod1 = 0;

        for (int i = 0; i < prototypes.Length; i++)
        {
            if (lists[i].Count == 0)
                continue;

            if (!TryGetLodMeshes(prototypes[i].prefab, out LodMesh lod0, out LodMesh lod1))
            {
                Debug.LogWarning($"[TerrainTreeGpuInstancer] Prototype '{prototypes[i].prefab?.name}' mesh/material bulunamadı, atlandı.");
                continue;
            }

            if (lod1.IsValid)
                withLod1++;

            int n = lists[i].Count;
            Matrix4x4[] mats = lists[i].ToArray();
            Vector3[] pos = positions[i].ToArray();
            BuildGridForTrees(pos, cellCount, out int[] cellStart, out int[] cellTreeIndices);

            built.Add(new PrototypeBatch
            {
                lod0 = lod0,
                lod1 = lod1,
                matrices = mats,
                positions = pos,
                cellStart = cellStart,
                cellTreeIndices = cellTreeIndices,
                lod0Shadow = new Matrix4x4[n],
                lod0NoShadow = new Matrix4x4[n],
                lod1Visible = new Matrix4x4[n],
                lod0ShadowCount = 0,
                lod0NoShadowCount = 0,
                lod1Count = 0
            });
            totalTrees += n;
        }

        _batches = built.ToArray();

        if (logOnStart)
        {
            Debug.Log(
                $"[TerrainTreeGpuInstancer] {totalTrees} ağaç, {_batches.Length} prototype, " +
                $"grid {_gridW}x{_gridH} (cell={cell:F0}m). Switch={lodSwitchDistance:F0}m, Max={maxDrawDistance:F0}m.");
        }
    }

    private void BuildGridForTrees(Vector3[] positions, int cellCount, out int[] cellStart, out int[] cellTreeIndices)
    {
        int[] counts = new int[cellCount];
        int n = positions.Length;

        for (int i = 0; i < n; i++)
        {
            int cell = CellIndex(positions[i].x, positions[i].z);
            counts[cell]++;
        }

        cellStart = new int[cellCount + 1];
        int sum = 0;
        for (int c = 0; c < cellCount; c++)
        {
            cellStart[c] = sum;
            sum += counts[c];
        }

        cellStart[cellCount] = sum;
        cellTreeIndices = new int[n];
        System.Array.Clear(counts, 0, counts.Length);

        for (int i = 0; i < n; i++)
        {
            int cell = CellIndex(positions[i].x, positions[i].z);
            int slot = cellStart[cell] + counts[cell]++;
            cellTreeIndices[slot] = i;
        }
    }

    private int CellIndex(float worldX, float worldZ)
    {
        int cx = Mathf.Clamp(Mathf.FloorToInt((worldX - _gridOriginX) * _invCellSize), 0, _gridW - 1);
        int cz = Mathf.Clamp(Mathf.FloorToInt((worldZ - _gridOriginZ) * _invCellSize), 0, _gridH - 1);
        return cz * _gridW + cx;
    }

    private static bool TryGetLodMeshes(GameObject prefab, out LodMesh lod0, out LodMesh lod1)
    {
        lod0 = default;
        lod1 = default;
        if (prefab == null)
            return false;

        LODGroup lodGroup = prefab.GetComponentInChildren<LODGroup>(true);
        if (lodGroup != null)
        {
            LOD[] lods = lodGroup.GetLODs();
            if (lods != null && lods.Length > 0)
            {
                lod0 = ExtractFromRenderers(lods[0].renderers);
                if (lods.Length > 1)
                    lod1 = ExtractFromRenderers(lods[1].renderers);
            }
        }

        if (!lod0.IsValid)
        {
            MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            MeshFilter best = null;
            MeshFilter worst = null;
            int bestVerts = -1;
            int worstVerts = int.MaxValue;

            for (int i = 0; i < filters.Length; i++)
            {
                Mesh m = filters[i].sharedMesh;
                if (m == null)
                    continue;
                int verts = m.vertexCount;
                if (verts > bestVerts)
                {
                    bestVerts = verts;
                    best = filters[i];
                }

                if (verts < worstVerts)
                {
                    worstVerts = verts;
                    worst = filters[i];
                }
            }

            if (best != null)
                lod0 = FromFilter(best);
            if (worst != null && worst != best)
                lod1 = FromFilter(worst);
        }

        return lod0.IsValid;
    }

    private static LodMesh ExtractFromRenderers(Renderer[] renderers)
    {
        if (renderers == null)
            return default;

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] is not MeshRenderer mr)
                continue;
            MeshFilter filter = mr.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
                continue;
            return FromRenderer(filter, mr);
        }

        return default;
    }

    private static LodMesh FromFilter(MeshFilter filter)
    {
        MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
        if (renderer == null)
            return default;
        return FromRenderer(filter, renderer);
    }

    private static LodMesh FromRenderer(MeshFilter filter, MeshRenderer renderer)
    {
        Material[] materials = renderer.sharedMaterials;
        if (materials == null || materials.Length == 0)
            return default;

        if (materials.Length > 1)
        {
            bool allSame = true;
            for (int i = 1; i < materials.Length; i++)
            {
                if (materials[i] != materials[0])
                {
                    allSame = false;
                    break;
                }
            }

            if (allSame)
                materials = new[] { materials[0] };
        }

        return new LodMesh
        {
            mesh = filter.sharedMesh,
            materials = materials
        };
    }

#if UNITY_EDITOR
    [ContextMenu("Rebuild Tree Batches")]
    private void RebuildFromContextMenu()
    {
        _terrain = GetComponent<Terrain>();
        BuildBatches();
    }
#endif
}
