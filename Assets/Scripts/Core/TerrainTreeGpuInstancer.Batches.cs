using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Partial: split for maintainability. Type identity unchanged.
public partial class TerrainTreeGpuInstancer : MonoBehaviour
{
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

}
