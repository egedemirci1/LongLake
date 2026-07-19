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
public partial class TerrainTreeGpuInstancer : MonoBehaviour
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

    private sealed class NearCandidateComparer : System.Collections.Generic.IComparer<NearCandidate>
    {
        public static readonly NearCandidateComparer Instance = new NearCandidateComparer();
        public int Compare(NearCandidate a, NearCandidate b) => a.distSq.CompareTo(b.distSq);
    }

    // Billboard instance çarpanı — SoftOcc materyal _Color zaten koyu; bunu açma.
    private static readonly Vector4 DarkForestBase = new Vector4(1f, 1f, 1f, 1f);

#if UNITY_EDITOR
    [ContextMenu("Rebuild Tree Batches")]
    public void RebuildFromContextMenu()
    {
        _terrain = GetComponent<Terrain>();
        BuildBatches();
    }
#endif
}
