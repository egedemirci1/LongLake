using UnityEditor;
using UnityEngine;

/// <summary>
/// Terrain detail çimlerini kurar / onarır (Grass_A, GrassDry_A, Fern_A) — boyut 3x.
/// Menü: LongLake → Setup Terrain Grass Details (3x)
/// </summary>
public static class ScaleTerrainDetailsMenu
{
    private const float Scale = 3f;
    private const string TerrainDataPath = "Assets/Art/Environment/Terrain/TD_MainLevel.asset";

    private static readonly string[] PrefabPaths =
    {
        "Assets/ThirdParty/TerrainSampleAssets/Prefabs/Grass_A.prefab",
        "Assets/ThirdParty/TerrainSampleAssets/Prefabs/GrassDry_A.prefab",
        "Assets/ThirdParty/TerrainSampleAssets/Prefabs/Fern_A.prefab",
    };

    [MenuItem("LongLake/Terrain/Setup Grass Details (3x)")]
    private static void SetupGrassDetails()
    {
        var data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);
        if (data == null)
        {
            EditorUtility.DisplayDialog("Terrain Grass", "TD_MainLevel.asset bulunamadı.", "OK");
            return;
        }

        // Prefab scale 1 olmalı — boyut DetailPrototype min/max ile gelir
        foreach (string path in PrefabPaths)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go == null)
                continue;
            go.transform.localScale = Vector3.one;
            EditorUtility.SetDirty(go);
        }

        var list = new System.Collections.Generic.List<DetailPrototype>();
        foreach (string path in PrefabPaths)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogError($"[TerrainGrass] Prefab yok: {path}");
                continue;
            }

            list.Add(MakePrototype(prefab));
        }

        if (list.Count == 0)
        {
            EditorUtility.DisplayDialog("Terrain Grass", "Hiçbir grass prefab yüklenemedi.", "OK");
            return;
        }

        // Mevcut boyalı detail map'leri koru: aynı sayıda prototip varsa sadece güncelle
        DetailPrototype[] existing = data.detailPrototypes;
        if (existing != null && existing.Length == list.Count)
        {
            for (int i = 0; i < list.Count; i++)
                existing[i] = ApplyDensityAndSize(existing[i], list[i]);
            data.detailPrototypes = existing;
        }
        else
        {
            data.detailPrototypes = list.ToArray();
        }

        // InstanceCountMode: fırça Opacity = doğrudan yoğunluk (CoverageMode'da az ekler)
        data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);

        // Scene/prefab Terrain density = 1 (önce 0.1–0.3 idi → çok seyrek)
        foreach (Terrain t in Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
        {
            if (t != null && t.terrainData == data)
                t.detailObjectDensity = 1f;
        }

        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();

        // Scene'deki terrain'i yenile
        foreach (Terrain t in Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
        {
            if (t != null && t.terrainData == data)
                t.Flush();
        }

        Debug.Log($"[TerrainGrass] {list.Count} detail mesh kuruldu (boyut ×{Scale}, density=1, InstanceCountMode).");
        EditorUtility.DisplayDialog(
            "Terrain Grass",
            $"{list.Count} detail güncellendi.\n" +
            "Detail Density = 1, Scatter = InstanceCountMode.\n\n" +
            "Paint Details → Opacity 1, Target Strength yüksek tut.\n" +
            "Hâlâ seyrekse fırçayı birkaç kez aynı yere bas.",
            "OK");
    }

    private static DetailPrototype MakePrototype(GameObject prefab)
    {
        return ApplyDensityAndSize(new DetailPrototype
        {
            prototype = prefab,
            prototypeTexture = null,
            usePrototypeMesh = true,
            useInstancing = true,
            renderMode = DetailRenderMode.VertexLit,
            noiseSpread = 0.1f,
            healthyColor = Color.white,
            dryColor = new Color(0.8f, 0.75f, 0.6f, 1f),
        }, null);
    }

    private static DetailPrototype ApplyDensityAndSize(DetailPrototype dst, DetailPrototype src)
    {
        if (src != null)
            dst.prototype = src.prototype;

        dst.usePrototypeMesh = true;
        dst.useInstancing = true;
        dst.renderMode = DetailRenderMode.VertexLit;
        dst.minWidth = 1f * Scale;
        dst.maxWidth = 2f * Scale;
        dst.minHeight = 1f * Scale;
        dst.maxHeight = 2f * Scale;
        dst.useDensityScaling = true;
        dst.density = 1f;
        dst.targetCoverage = 1f;
        dst.alignToGround = 1f;
        dst.positionJitter = 0.1f;
        return dst;
    }
}
