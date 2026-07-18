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
    [SerializeField] private float maxDrawDistance = 550f;
    [Tooltip("Bu mesafeden sonra LOD1 / billboard çizilir.")]
    [SerializeField] private float lodSwitchDistance = 18f;
    [SerializeField] private float shadowDistance = 8f;
    [Tooltip("Aynı anda çizilecek maksimum yüksek-poly (LOD0) near ağaç. Fazlası billboard olur.")]
    [SerializeField] private int maxNearFullLod = 20;

    [Header("Spatial Grid")]
    [Tooltip("Cull grid hücre boyutu (metre). Küçük = daha az ağaç taranır, daha fazla hücre.")]
    [SerializeField] private float cellSize = 40f;

    [Header("Background Trees")]
    [Tooltip("Prefab adında bu metin geçen prototype'lar daima billboard çizilir (LOD/gölge yok).")]
    [SerializeField] private string backgroundNameToken = "background";
    [Tooltip("Background ağaçlar için max draw distance. 0 = near ile aynı.")]
    [SerializeField] private float backgroundMaxDrawDistance = 520f;
    [Tooltip("Aynı anda çizilecek max background billboard (en yakın hücreler önce).")]
    [SerializeField] private int maxBackgroundDraw = 28000;
    [Tooltip("Background billboard'larda per-instance renk sapması (0 = kapalı, daha ucuz).")]
    [Range(0f, 0.45f)]
    [SerializeField] private float backgroundColorVariation = 0.08f;
    [Tooltip("Near LOD1 billboard material override — SoftOcc ile background'a eşler.")]
    [SerializeField] private Material nearBillboardMaterial;

    private const string SoftOccMaterialPath = "Assets/Art/Environment/TreeFir/M_AgacBackground_SoftOcc.mat";

    [Header("Cull Refresh")]
    [Tooltip("Kamera bu kadar metre hareket edince cull listesi yenilenir.")]
    [SerializeField] private float cullMoveThreshold = 10f;
    [Tooltip("Hareket olmasa bile en geç bu kadar frame'de bir cull yenilenir.")]
    [SerializeField] private int cullMaxAgeFrames = 5;

    [Header("Rendering")]
    [Tooltip("Ağaç gölgesi Stats Tris'i 2–4× şişirir (cascade). Kapalı tutmak önerilir.")]
    [SerializeField] private bool castShadows = false;
    [SerializeField] private bool receiveShadows = false;
    [Tooltip("Terrain'in kendi tree çizimini kapatır (ot/detail etkilenmez).")]
    [SerializeField] private bool disableTerrainTreeDrawing = true;

    [Header("Debug")]
    [SerializeField] private bool logOnStart = true;

    private Terrain _terrain;
    private float _savedTreeDistance;
    private bool _terrainTreesHidden;
    private PrototypeBatch[] _batches;
    private Camera _camera;
    private readonly Matrix4x4[] _chunkBuffer = new Matrix4x4[MaxInstancesPerBatch];
    private readonly Vector4[] _colorChunk = new Vector4[MaxInstancesPerBatch];
    private MaterialPropertyBlock _mpb;
    private float _gridOriginX;
    private float _gridOriginZ;
    private int _gridW;
    private int _gridH;
    private float _invCellSize;
    private readonly NearCandidate[] _nearCandidates = new NearCandidate[2048];
    private Vector3 _lastCullCamPos;
    private int _framesSinceCull = 99;
    private bool _hasCullCache;

    private struct NearCandidate
    {
        public int index;
        public float distSq;
    }

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
        public Vector4[] colors;
        public bool isBackground;

        // CSR grid: cellStart[cell], cellStart[cell+1] → cellTreeIndices aralığı
        public int[] cellStart;
        public int[] cellTreeIndices;

        public Matrix4x4[] lod0Shadow;
        public int lod0ShadowCount;
        public Matrix4x4[] lod0NoShadow;
        public int lod0NoShadowCount;
        public Matrix4x4[] lod1Visible;
        public Vector4[] lod1Colors;
        public int lod1Count;
    }

    private void Awake()
    {
        _terrain = GetComponent<Terrain>();
        _mpb = new MaterialPropertyBlock();
        BuildBatches();
    }

    private void OnDestroy()
    {
        RestoreTerrainTreeDrawing();
    }

    private void HideTerrainTreeDrawing()
    {
        if (!disableTerrainTreeDrawing || _terrainTreesHidden || _terrain == null)
            return;
        _savedTreeDistance = _terrain.treeDistance;
        _terrain.treeDistance = 0f;
        _terrainTreesHidden = true;
    }

    private void RestoreTerrainTreeDrawing()
    {
        if (!_terrainTreesHidden || _terrain == null)
            return;
        _terrain.treeDistance = _savedTreeDistance;
        _terrainTreesHidden = false;
    }

    private void LateUpdate()
    {
        if (_batches == null || _batches.Length == 0)
            return;

        if (_camera == null || !_camera.isActiveAndEnabled)
            _camera = ResolveCamera();
        if (_camera == null)
            return;

        // Native terrain tree'leri ancak GPU çizime hazır olunca kapat —
        // kamera spawn olmadan orman kaybolmasın.
        HideTerrainTreeDrawing();

        Vector3 camPos = _camera.transform.position;
        _framesSinceCull++;
        float moveSq = (camPos - _lastCullCamPos).sqrMagnitude;
        float moveThresh = Mathf.Max(2f, cullMoveThreshold);
        bool needsCull = !_hasCullCache
                         || moveSq >= moveThresh * moveThresh
                         || _framesSinceCull >= Mathf.Max(1, cullMaxAgeFrames);

        if (needsCull)
        {
            RebuildCullListsFromGrid(camPos);
            _lastCullCamPos = camPos;
            _framesSinceCull = 0;
            _hasCullCache = true;
        }

        for (int b = 0; b < _batches.Length; b++)
        {
            PrototypeBatch batch = _batches[b];
            if (!batch.lod0.IsValid)
                continue;

            if (batch.isBackground)
            {
                if (batch.lod1Count <= 0)
                    continue;
                // SoftOcc: MPB InstanceColor olmadan da çizilsin (billboard kaybolmasın).
                if (batch.lod1Colors != null && backgroundColorVariation > 0.001f)
                    DrawCachedColored(batch.lod0, batch.lod1Visible, batch.lod1Colors, batch.lod1Count);
                else
                    DrawCached(batch.lod0, batch.lod1Visible, batch.lod1Count, shadows: false);
                continue;
            }

            if (batch.lod0ShadowCount > 0)
                DrawCached(batch.lod0, batch.lod0Shadow, batch.lod0ShadowCount, shadows: true);
            if (batch.lod0NoShadowCount > 0)
                DrawCached(batch.lod0, batch.lod0NoShadow, batch.lod0NoShadowCount, shadows: false);

            LodMesh farLod = batch.lod1.IsValid ? batch.lod1 : batch.lod0;
            if (batch.lod1Count > 0 && farLod.IsValid)
            {
                // Near LOD1 billboard — SoftOcc; tint opsiyonel.
                if (batch.lod1Colors != null && backgroundColorVariation > 0.001f)
                    DrawCachedColored(farLod, batch.lod1Visible, batch.lod1Colors, batch.lod1Count);
                else
                    DrawCached(farLod, batch.lod1Visible, batch.lod1Count, shadows: false);
            }
        }
    }

    private void RebuildCullListsFromGrid(Vector3 camPos)
    {
        float nearMaxDist = maxDrawDistance;
        float nearMaxDistSq = nearMaxDist * nearMaxDist;
        float bgMaxDist = backgroundMaxDrawDistance > 0.01f ? backgroundMaxDrawDistance : maxDrawDistance;
        float bgMaxDistSq = bgMaxDist * bgMaxDist;
        float lodDistSq = lodSwitchDistance * lodSwitchDistance;
        float shadowDistSq = shadowDistance * shadowDistance;
        bool useShadows = castShadows;
        int nearFullLodBudget = Mathf.Max(0, maxNearFullLod);
        int bgBudget = Mathf.Max(1023, maxBackgroundDraw);

        float cullRadius = Mathf.Max(nearMaxDist, bgMaxDist);
        int camCx = Mathf.Clamp(Mathf.FloorToInt((camPos.x - _gridOriginX) * _invCellSize), 0, _gridW - 1);
        int camCz = Mathf.Clamp(Mathf.FloorToInt((camPos.z - _gridOriginZ) * _invCellSize), 0, _gridH - 1);
        int maxRing = Mathf.CeilToInt(cullRadius * _invCellSize) + 1;

        float camX = camPos.x;
        float camY = camPos.y;
        float camZ = camPos.z;

        for (int b = 0; b < _batches.Length; b++)
        {
            PrototypeBatch batch = _batches[b];
            if (batch.matrices == null || batch.cellStart == null)
                continue;

            int s0 = 0, s1 = 0, s2 = 0;
            int candCount = 0;
            Matrix4x4[] buf0 = batch.lod0Shadow;
            Matrix4x4[] buf1 = batch.lod0NoShadow;
            Matrix4x4[] buf2 = batch.lod1Visible;
            Vector4[] colorBuf = batch.lod1Colors;
            Vector4[] colors = batch.colors;
            Vector3[] positions = batch.positions;
            Matrix4x4[] matrices = batch.matrices;
            int[] cellStart = batch.cellStart;
            int[] cellTrees = batch.cellTreeIndices;
            bool isBackground = batch.isBackground;
            float maxDistSq = isBackground ? bgMaxDistSq : nearMaxDistSq;
            bool tintBg = isBackground && colorBuf != null && colors != null && backgroundColorVariation > 0.001f;
            bool tintNear = !isBackground && colorBuf != null && colors != null;

            // Kamera hücresinden dışa doğru halka — yakın orman önce dolar, bütçe aşımında uzak kesilir.
            for (int ring = 0; ring <= maxRing; ring++)
            {
                if (isBackground && s2 >= bgBudget)
                    break;

                int z0 = camCz - ring;
                int z1 = camCz + ring;
                int x0 = camCx - ring;
                int x1 = camCx + ring;

                for (int cz = z0; cz <= z1; cz++)
                {
                    if (cz < 0 || cz >= _gridH)
                        continue;
                    int row = cz * _gridW;
                    bool onZEdge = cz == z0 || cz == z1;

                    for (int cx = x0; cx <= x1; cx++)
                    {
                        if (cx < 0 || cx >= _gridW)
                            continue;
                        // İç halkadaki hücreleri tekrar tarama.
                        if (ring > 0 && !onZEdge && cx != x0 && cx != x1)
                            continue;

                        if (isBackground && s2 >= bgBudget)
                            break;

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

                            if (isBackground)
                            {
                                if (s2 >= bgBudget)
                                    break;
                                buf2[s2] = matrices[i];
                                if (tintBg)
                                    colorBuf[s2] = colors[i];
                                s2++;
                                continue;
                            }

                            if (distSq <= lodDistSq && candCount < _nearCandidates.Length)
                            {
                                _nearCandidates[candCount++] = new NearCandidate { index = i, distSq = distSq };
                            }
                            else
                            {
                                buf2[s2] = matrices[i];
                                if (tintNear)
                                    colorBuf[s2] = colors[i];
                                s2++;
                            }
                        }
                    }
                }
            }

            // En yakın N ağaç LOD0; kalanı billboard — yüksek-poly bütçesi.
            if (!isBackground && candCount > 0)
            {
                int keep = Mathf.Min(candCount, nearFullLodBudget);
                if (candCount > keep)
                    System.Array.Sort(_nearCandidates, 0, candCount, NearCandidateComparer.Instance);

                for (int c = 0; c < candCount; c++)
                {
                    int i = _nearCandidates[c].index;
                    float distSq = _nearCandidates[c].distSq;
                    if (c < keep)
                    {
                        if (useShadows && distSq <= shadowDistSq)
                            buf0[s0++] = matrices[i];
                        else
                            buf1[s1++] = matrices[i];
                    }
                    else
                    {
                        buf2[s2] = matrices[i];
                        if (tintNear)
                            colorBuf[s2] = colors[i];
                        s2++;
                    }
                }
            }

            batch.lod0ShadowCount = s0;
            batch.lod0NoShadowCount = s1;
            batch.lod1Count = s2;
            _batches[b] = batch;
        }
    }

    private sealed class NearCandidateComparer : System.Collections.Generic.IComparer<NearCandidate>
    {
        public static readonly NearCandidateComparer Instance = new NearCandidateComparer();
        public int Compare(NearCandidate a, NearCandidate b) => a.distSq.CompareTo(b.distSq);
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

    private void DrawCachedColored(LodMesh lod, Matrix4x4[] matrices, Vector4[] colors, int count)
    {
        int offset = 0;
        while (offset < count)
        {
            int chunk = Mathf.Min(MaxInstancesPerBatch, count - offset);
            FlushChunkColored(lod, matrices, colors, offset, chunk);
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

    private void FlushChunkColored(LodMesh lod, Matrix4x4[] matrices, Vector4[] colors, int offset, int count)
    {
        System.Array.Copy(matrices, offset, _chunkBuffer, 0, count);
        for (int i = 0; i < count; i++)
            _colorChunk[i] = colors != null ? colors[offset + i] : DarkForestBase;

        // MaterialPropertyBlock: SoftOcc _Color (koyu) × _InstanceColor (hafif çarpan).
        if (_mpb == null)
            _mpb = new MaterialPropertyBlock();
        _mpb.Clear();
        _mpb.SetVectorArray("_InstanceColor", _colorChunk);

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
                matProps = _mpb,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
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
        int nearTrees = 0;
        int backgroundTrees = 0;
        int withLod1 = 0;

        for (int i = 0; i < prototypes.Length; i++)
        {
            if (lists[i].Count == 0)
                continue;

            GameObject prefab = prototypes[i].prefab;
            bool isBackground = IsBackgroundPrototype(prefab);

            if (!TryGetLodMeshes(prefab, out LodMesh lod0, out LodMesh lod1))
            {
                Debug.LogWarning($"[TerrainTreeGpuInstancer] Prototype '{prefab?.name}' mesh/material bulunamadı, atlandı.");
                continue;
            }

            // SoftOcc: Terrain Soft Occlusion doğrulaması + URP GPU çizim.
            Material softOcc = ResolveNearBillboardMaterial();

            // Background prefab tek mesh (billboard); near'da lod1 uzak mesh.
            if (isBackground)
            {
                lod1 = default;
                if (softOcc != null)
                    lod0.materials = new[] { softOcc };
            }
            else if (lod1.IsValid)
            {
                withLod1++;
                if (softOcc != null)
                    lod1.materials = new[] { softOcc };
            }

            int n = lists[i].Count;
            Matrix4x4[] mats = lists[i].ToArray();
            Vector3[] pos = positions[i].ToArray();
            BuildGridForTrees(pos, cellCount, out int[] cellStart, out int[] cellTreeIndices);

            // Billboard tint: hem background hem near LOD1 için (görsel eşleşme).
            Vector4[] colors = null;
            Vector4[] lod1Colors = null;
            bool needsBillboardTint = isBackground || lod1.IsValid;
            if (needsBillboardTint)
            {
                colors = new Vector4[n];
                for (int t = 0; t < n; t++)
                    colors[t] = MakeBackgroundTint(pos[t], backgroundColorVariation);
                lod1Colors = new Vector4[n];
            }

            built.Add(new PrototypeBatch
            {
                lod0 = lod0,
                lod1 = lod1,
                matrices = mats,
                positions = pos,
                colors = colors,
                isBackground = isBackground,
                cellStart = cellStart,
                cellTreeIndices = cellTreeIndices,
                lod0Shadow = isBackground ? System.Array.Empty<Matrix4x4>() : new Matrix4x4[n],
                lod0NoShadow = isBackground ? System.Array.Empty<Matrix4x4>() : new Matrix4x4[n],
                lod1Visible = new Matrix4x4[n],
                lod1Colors = lod1Colors,
                lod0ShadowCount = 0,
                lod0NoShadowCount = 0,
                lod1Count = 0
            });
            totalTrees += n;
            if (isBackground)
                backgroundTrees += n;
            else
                nearTrees += n;
        }

        _batches = built.ToArray();
        _hasCullCache = false;
        _framesSinceCull = 99;

        if (logOnStart)
        {
            string nearMeshInfo = "n/a";
            for (int i = 0; i < _batches.Length; i++)
            {
                if (_batches[i].isBackground)
                    continue;
                Mesh m0 = _batches[i].lod0.mesh;
                Mesh m1 = _batches[i].lod1.mesh;
                nearMeshInfo =
                    $"LOD0={(m0 != null ? $"{m0.name} v={m0.vertexCount}" : "null")} " +
                    $"LOD1={(m1 != null ? $"{m1.name} v={m1.vertexCount} ok={_batches[i].lod1.IsValid}" : "null")}";
                break;
            }

            float bgDist = backgroundMaxDrawDistance > 0.01f ? backgroundMaxDrawDistance : maxDrawDistance;
            Debug.Log(
                $"[TerrainTreeGpuInstancer] {totalTrees} ağaç (near={nearTrees}, background={backgroundTrees}), " +
                $"{_batches.Length} prototype, grid {_gridW}x{_gridH} (cell={cell:F0}m). " +
                $"NearLOD={lodSwitchDistance:F0}m, MaxFullLOD={maxNearFullLod}, BgMax={bgDist:F0}m/{maxBackgroundDraw}, " +
                $"CullMove={cullMoveThreshold:F0}m/{cullMaxAgeFrames}f, {nearMeshInfo}.");
        }
    }

    private Material ResolveNearBillboardMaterial()
    {
        if (nearBillboardMaterial != null)
            return nearBillboardMaterial;

#if UNITY_EDITOR
        nearBillboardMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(SoftOccMaterialPath);
#endif
        if (nearBillboardMaterial == null)
            nearBillboardMaterial = Resources.Load<Material>("VFX/M_AgacBackground_SoftOcc");

        return nearBillboardMaterial;
    }

    private bool IsBackgroundPrototype(GameObject prefab)
    {
        if (prefab == null || string.IsNullOrEmpty(backgroundNameToken))
            return false;
        return prefab.name.IndexOf(backgroundNameToken, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // Billboard instance çarpanı — SoftOcc materyal _Color zaten koyu; bunu açma.
    private static readonly Vector4 DarkForestBase = new Vector4(1f, 1f, 1f, 1f);

    /// <summary>
    /// Konum hash'inden hafif ton çarpanı (1 civarı). Mutlak renk değil — materyali açmaz.
    /// </summary>
    private static Vector4 MakeBackgroundTint(Vector3 worldPos, float strength)
    {
        if (strength <= 0.001f)
            return DarkForestBase;

        uint h = HashPosition(worldPos);
        float u = (h & 1023u) / 1023f;
        float v = ((h >> 10) & 1023u) / 1023f;
        float w = ((h >> 20) & 1023u) / 1023f;

        float brightness = Mathf.Lerp(0.88f, 1.06f, u);
        float greenBias = Mathf.Lerp(-0.08f, 0.1f, v) * strength;
        float coolBias = Mathf.Lerp(-0.05f, 0.06f, w) * strength;

        float r = Mathf.Clamp(brightness * (1f - greenBias * 0.2f), 0.82f, 1.08f);
        float g = Mathf.Clamp(brightness * (1f + greenBias), 0.85f, 1.1f);
        float b = Mathf.Clamp(brightness * (1f + coolBias * 0.5f - greenBias * 0.1f), 0.8f, 1.06f);
        return new Vector4(r, g, b, 1f);
    }

    private static Camera ResolveCamera()
    {
        Camera main = Camera.main;
        if (main != null && main.isActiveAndEnabled)
            return main;

        Camera[] cameras = Camera.allCameras;
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera c = cameras[i];
            if (c != null && c.isActiveAndEnabled && c.CompareTag("MainCamera"))
                return c;
        }

        for (int i = 0; i < cameras.Length; i++)
        {
            Camera c = cameras[i];
            if (c != null && c.isActiveAndEnabled && c.enabled)
                return c;
        }

        return null;
    }

    private static uint HashPosition(Vector3 p)
    {
        unchecked
        {
            uint x = (uint)Mathf.RoundToInt(p.x * 7.1f);
            uint z = (uint)Mathf.RoundToInt(p.z * 7.1f);
            uint h = x * 374761393u + z * 668265263u;
            h = (h ^ (h >> 13)) * 1274126177u;
            return h ^ (h >> 16);
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
    public void RebuildFromContextMenu()
    {
        _terrain = GetComponent<Terrain>();
        BuildBatches();
    }
#endif
}
