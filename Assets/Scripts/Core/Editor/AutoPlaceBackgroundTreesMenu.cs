using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// TL_Grass terrain katmanına billboard-only background ağaçları kümeli biçimde yerleştirir.
/// Mevcut near tree instance'larını korur; yalnızca background prototype instance'larını yeniler.
/// </summary>
public static class AutoPlaceBackgroundTreesMenu
{
    private const string TerrainDataPath = "Assets/Art/Environment/Terrain/TD_MainLevel.asset";
    private const string GrassLayerName = "TL_Grass";
    private const string BackgroundToken = "background";

    // ~3.5m grid → TL_Grass üzerinde 120k'ye yaklaşır; Perlin ile kümeli dağılım.
    private const int Seed = 7182026;
    private const float CandidateSpacing = 3.5f;
    private const float PositionJitter = 1.4f;
    private const float GrassThreshold = 0.45f;
    private const float MaxSlope = 42f;
    private const float EdgePadding = 12f;
    private const float ClusterNoiseScale = 0.012f;
    private const float ClusterThreshold = 0.28f;
    private const int MaxBackgroundTrees = 120000;

    [MenuItem("LongLake/Trees/Auto Place Background on TL_Grass")]
    private static void Place()
    {
        var data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);
        if (data == null)
        {
            EditorUtility.DisplayDialog("Background Trees", "TD_MainLevel.asset bulunamadı.", "OK");
            return;
        }

        int grassLayer = FindTerrainLayer(data, GrassLayerName);
        if (grassLayer < 0)
        {
            EditorUtility.DisplayDialog(
                "Background Trees",
                $"{GrassLayerName} terrain layer bulunamadı.",
                "OK");
            return;
        }

        int backgroundPrototype = FindBackgroundPrototype(data);
        if (backgroundPrototype < 0)
        {
            EditorUtility.DisplayDialog(
                "Background Trees",
                "Background prototype bulunamadı.\n\nÖnce LongLake → Setup Background Tree Prototype çalıştır.",
                "OK");
            return;
        }

        bool confirmed = EditorUtility.DisplayDialog(
            "Auto Place Background Trees",
            "TL_Grass üzerinde background ağaçları otomatik yerleştirilecek.\n\n" +
            "• Mevcut near ağaçlar korunur\n" +
            "• Mevcut background ağaçlar yenilenir\n" +
            "• Dağılım kümeli ve deterministiktir\n" +
            $"• Maksimum {MaxBackgroundTrees:N0} background ağaç\n\n" +
            "Devam edilsin mi?",
            "Yerleştir",
            "İptal");
        if (!confirmed)
            return;

        Undo.RegisterCompleteObjectUndo(data, "Auto Place Background Trees");

        try
        {
            float[,,] alpha = data.GetAlphamaps(
                0, 0, data.alphamapWidth, data.alphamapHeight);

            var instances = new List<TreeInstance>(data.treeInstanceCount + 8000);
            TreeInstance[] existing = data.treeInstances;
            for (int i = 0; i < existing.Length; i++)
            {
                if (existing[i].prototypeIndex != backgroundPrototype)
                    instances.Add(existing[i]);
            }

            int nearCount = instances.Count;
            int added = GenerateBackgroundTrees(
                data, alpha, grassLayer, backgroundPrototype, instances);

            data.SetTreeInstances(instances.ToArray(), true);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();

            foreach (var terrain in UnityEngine.Object.FindObjectsByType<Terrain>(
                         FindObjectsSortMode.None))
            {
                if (terrain.terrainData == data)
                    terrain.Flush();
            }

            foreach (var instancer in UnityEngine.Object.FindObjectsByType<TerrainTreeGpuInstancer>(
                         FindObjectsSortMode.None))
            {
                instancer.RebuildFromContextMenu();
            }

            EditorUtility.DisplayDialog(
                "Background Trees",
                $"Yerleştirme tamamlandı.\n\n" +
                $"Korunan near ağaç: {nearCount:N0}\n" +
                $"Yeni background ağaç: {added:N0}\n" +
                $"Toplam: {instances.Count:N0}\n\n" +
                "Sonucu Scene View'da kontrol et. Geri almak için Ctrl+Z kullanabilirsin.",
                "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    [MenuItem("LongLake/Trees/Remove All Background Trees")]
    private static void RemoveBackgroundTrees()
    {
        var data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);
        if (data == null)
            return;

        int backgroundPrototype = FindBackgroundPrototype(data);
        if (backgroundPrototype < 0)
            return;

        Undo.RegisterCompleteObjectUndo(data, "Remove Background Trees");
        var kept = new List<TreeInstance>(data.treeInstanceCount);
        TreeInstance[] existing = data.treeInstances;
        for (int i = 0; i < existing.Length; i++)
        {
            if (existing[i].prototypeIndex != backgroundPrototype)
                kept.Add(existing[i]);
        }

        data.SetTreeInstances(kept.ToArray(), true);
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
        EditorUtility.DisplayDialog(
            "Background Trees",
            $"{existing.Length - kept.Count:N0} background ağaç kaldırıldı.",
            "OK");
    }

    private static int GenerateBackgroundTrees(
        TerrainData data,
        float[,,] alpha,
        int grassLayer,
        int prototypeIndex,
        List<TreeInstance> output)
    {
        var random = new System.Random(Seed);
        Vector3 size = data.size;
        int added = 0;
        int stepsX = Mathf.CeilToInt((size.x - EdgePadding * 2f) / CandidateSpacing);
        int stepsZ = Mathf.CeilToInt((size.z - EdgePadding * 2f) / CandidateSpacing);
        int totalSteps = Mathf.Max(1, stepsX * stepsZ);
        int processed = 0;

        for (int z = 0; z < stepsZ && added < MaxBackgroundTrees; z++)
        {
            if ((z & 15) == 0)
            {
                EditorUtility.DisplayProgressBar(
                    "Background Trees",
                    $"TL_Grass taranıyor… {added:N0} ağaç",
                    processed / (float)totalSteps);
            }

            for (int x = 0; x < stepsX && added < MaxBackgroundTrees; x++)
            {
                processed++;

                float worldX = EdgePadding + x * CandidateSpacing +
                               RandomRange(random, -PositionJitter, PositionJitter);
                float worldZ = EdgePadding + z * CandidateSpacing +
                               RandomRange(random, -PositionJitter, PositionJitter);

                float nx = Mathf.Clamp01(worldX / size.x);
                float nz = Mathf.Clamp01(worldZ / size.z);

                float grassWeight = SampleAlpha(alpha, nx, nz, grassLayer);
                if (grassWeight < GrassThreshold)
                    continue;

                float slope = data.GetSteepness(nx, nz);
                if (slope > MaxSlope)
                    continue;

                // Büyük Perlin kümeleri + küçük varyasyon: homojen duvar görünümünü kırar.
                float cluster = Mathf.PerlinNoise(
                    worldX * ClusterNoiseScale + 37.1f,
                    worldZ * ClusterNoiseScale + 91.7f);
                if (cluster < ClusterThreshold)
                    continue;

                float clusterChance = Mathf.InverseLerp(ClusterThreshold, 0.82f, cluster);
                float placementChance = Mathf.Lerp(0.35f, 0.98f, clusterChance) * Mathf.Lerp(0.55f, 1f, grassWeight);
                if (random.NextDouble() > placementChance)
                    continue;

                float height = data.GetInterpolatedHeight(nx, nz);
                float heightScale = RandomRange(random, 0.8f, 1.4f);
                float widthScale = heightScale * RandomRange(random, 0.82f, 1.12f);

                output.Add(new TreeInstance
                {
                    position = new Vector3(nx, height / size.y, nz),
                    prototypeIndex = prototypeIndex,
                    widthScale = widthScale,
                    heightScale = heightScale,
                    rotation = RandomRange(random, 0f, Mathf.PI * 2f),
                    color = Color.white,
                    lightmapColor = Color.white
                });
                added++;
            }
        }

        return added;
    }

    private static float SampleAlpha(float[,,] alpha, float nx, float nz, int layer)
    {
        int width = alpha.GetLength(1);
        int height = alpha.GetLength(0);
        int x = Mathf.Clamp(Mathf.RoundToInt(nx * (width - 1)), 0, width - 1);
        int z = Mathf.Clamp(Mathf.RoundToInt(nz * (height - 1)), 0, height - 1);
        return alpha[z, x, layer];
    }

    private static int FindTerrainLayer(TerrainData data, string layerName)
    {
        TerrainLayer[] layers = data.terrainLayers;
        for (int i = 0; i < layers.Length; i++)
        {
            if (layers[i] != null &&
                string.Equals(layers[i].name, layerName, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static int FindBackgroundPrototype(TerrainData data)
    {
        TreePrototype[] prototypes = data.treePrototypes;
        for (int i = 0; i < prototypes.Length; i++)
        {
            GameObject prefab = prototypes[i].prefab;
            if (prefab != null &&
                prefab.name.IndexOf(BackgroundToken, StringComparison.OrdinalIgnoreCase) >= 0)
                return i;
        }

        return -1;
    }

    private static float RandomRange(System.Random random, float min, float max)
    {
        return Mathf.Lerp(min, max, (float)random.NextDouble());
    }
}
