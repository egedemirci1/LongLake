using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// agac-background prefab'ını (düz MeshRenderer + Soft Occlusion isimli shader) oluşturur
/// ve terrain prototype'a ekler.
/// Menü: LongLake → Setup Background Tree Prototype
/// </summary>
public static class SetupBackgroundTreePrototypeMenu
{
    private const string TerrainDataPath = "Assets/Art/Environment/Terrain/TD_MainLevel.asset";
    private const string NearPrefabPath = "Assets/Art/Environment/TreeFir/agac-root.prefab";
    private const string BillboardModelPath = "Assets/Art/Environment/TreeFir/agac-bilboard.fbx";
    private const string BackgroundPrefabPath = "Assets/Art/Environment/TreeFir/agac-background.prefab";
    private const string SoftOccMaterialPath = "Assets/Art/Environment/TreeFir/M_AgacBackground_SoftOcc.mat";
    private const string BillboardTexturePath = "Assets/Art/Environment/TreeFir/Billboard_Textures/bilboard.png";
    private const string SoftOccShaderPath = "Assets/Art/Environment/TreeFir/NatureSoftOcclusionLeaves.shader";

    [MenuItem("LongLake/Trees/Setup Background Prototype")]
    private static void Setup()
    {
        GameObject bgPrefab = CreateOrUpdateBackgroundPrefab();
        if (bgPrefab == null)
            return;

        var data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);
        if (data == null)
        {
            EditorUtility.DisplayDialog("Background Trees", "TD_MainLevel.asset bulunamadı.", "OK");
            return;
        }

        var nearPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(NearPrefabPath);
        var list = new System.Collections.Generic.List<TreePrototype>(
            data.treePrototypes ?? System.Array.Empty<TreePrototype>());

        bool hasBg = false;
        for (int i = 0; i < list.Count; i++)
        {
            var p = list[i].prefab;
            if (p == null) continue;
            if (p == bgPrefab || p.name.IndexOf("background", System.StringComparison.OrdinalIgnoreCase) >= 0)
                hasBg = true;
        }

        if (nearPrefab != null && list.Count == 0)
            list.Add(new TreePrototype { prefab = nearPrefab, bendFactor = 0f });

        if (!hasBg)
        {
            list.Add(new TreePrototype { prefab = bgPrefab, bendFactor = 0f });
        }
        else
        {
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i].prefab;
                if (p != null && p.name.IndexOf("background", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var tp = list[i];
                    tp.prefab = bgPrefab;
                    list[i] = tp;
                }
            }
        }

        data.treePrototypes = list.ToArray();
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();

        foreach (var instancer in Object.FindObjectsByType<TerrainTreeGpuInstancer>(FindObjectsSortMode.None))
            instancer.RebuildFromContextMenu();

        EditorUtility.DisplayDialog(
            "Background Trees",
            "agac-background hazır (Soft Occlusion isimli URP cutout shader).\n\n" +
            "Terrain Paint Trees → agac-background seçip dağlara bas.\n" +
            $"Prototype sayısı: {data.treePrototypes.Length}",
            "OK");
    }

    private static Material CreateOrUpdateSoftOccMaterial()
    {
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(SoftOccShaderPath);
        if (shader == null)
            shader = Shader.Find("Nature/Soft Occlusion Leaves");
        if (shader == null)
        {
            EditorUtility.DisplayDialog("Background Trees", "Nature/Soft Occlusion Leaves shader bulunamadı.", "OK");
            return null;
        }

        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(BillboardTexturePath);
        var mat = AssetDatabase.LoadAssetAtPath<Material>(SoftOccMaterialPath);
        if (mat == null)
        {
            mat = new Material(shader) { name = "M_AgacBackground_SoftOcc" };
            AssetDatabase.CreateAsset(mat, SoftOccMaterialPath);
        }
        else
        {
            mat.shader = shader;
        }

        if (tex != null)
        {
            if (mat.HasProperty("_MainTex"))
                mat.SetTexture("_MainTex", tex);
            if (mat.HasProperty("_BaseMap"))
                mat.SetTexture("_BaseMap", tex);
        }

        if (mat.HasProperty("_Cutoff"))
            mat.SetFloat("_Cutoff", 0.4f);
        // Koyu orman yeşili — per-instance tint üzerine biner / fallback.
        if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", new Color(0.07f, 0.11f, 0.06f, 1f));
        if (mat.HasProperty("_InstanceColor"))
            mat.SetColor("_InstanceColor", Color.white);

        mat.enableInstancing = true;
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Background Trees] SoftOcc material: shader='{mat.shader.name}' " +
                  $"(BillboardShader dependency + TreeTransparentCutout beklenir).");
        return mat;
    }

    private static GameObject CreateOrUpdateBackgroundPrefab()
    {
        var modelRoot = AssetDatabase.LoadAssetAtPath<GameObject>(BillboardModelPath);
        if (modelRoot == null)
        {
            EditorUtility.DisplayDialog("Background Trees", "agac-bilboard.fbx bulunamadı.", "OK");
            return null;
        }

        Mesh mesh = null;
        var filters = modelRoot.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < filters.Length; i++)
        {
            if (filters[i].sharedMesh == null) continue;
            if (filters[i].GetComponent<MeshRenderer>() == null) continue;
            mesh = filters[i].sharedMesh;
            break;
        }

        if (mesh == null)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(BillboardModelPath);
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is Mesh m)
                {
                    mesh = m;
                    break;
                }
            }
        }

        if (mesh == null)
        {
            EditorUtility.DisplayDialog("Background Trees", "agac-bilboard.fbx içinde mesh bulunamadı.", "OK");
            return null;
        }

        Material mat = CreateOrUpdateSoftOccMaterial();
        if (mat == null)
            return null;

        var temp = new GameObject("agac-background");
        temp.transform.localScale = new Vector3(2.5f, 2.5f, 2.5f);

        var filter = temp.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;

        var renderer = temp.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        renderer.allowOcclusionWhenDynamic = false;

        GameObject prefabAsset = PrefabUtility.SaveAsPrefabAsset(temp, BackgroundPrefabPath);
        Object.DestroyImmediate(temp);

        if (prefabAsset == null)
        {
            EditorUtility.DisplayDialog("Background Trees", "Prefab kaydedilemedi.", "OK");
            return null;
        }

        AssetDatabase.ImportAsset(BackgroundPrefabPath);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Background Trees] Prefab güncellendi: {BackgroundPrefabPath} mesh={mesh.name} shader={mat.shader.name}");
        return prefabAsset;
    }
}
