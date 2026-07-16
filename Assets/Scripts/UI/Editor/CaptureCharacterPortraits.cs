#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Ahu / Yaman prefab'larından portre PNG üretir (Tools/LongLake/Capture Character Portraits).
/// </summary>
public static class CaptureCharacterPortraits
{
    private const string AhuPrefab =
        "Assets/ReadyPlayerMe/Avatars/Ahu/2fac66e374c947c41bc74325c6e3d934/69503453403c000063195f31.prefab";
    private const string YamanPrefab =
        "Assets/ReadyPlayerMe/Avatars/Yaman/2fac66e374c947c41bc74325c6e3d934/695139d3220569853f86a35f.prefab";
    private const string OutDir = "Assets/Art/UI/Portraits";

    [MenuItem("Tools/LongLake/Capture Character Portraits")]
    public static void Capture()
    {
        Directory.CreateDirectory(OutDir);

        CaptureOne(AhuPrefab, Path.Combine(OutDir, "Ahu.png"), new Color(0.12f, 0.08f, 0.09f, 1f));
        CaptureOne(YamanPrefab, Path.Combine(OutDir, "Yaman.png"), new Color(0.07f, 0.10f, 0.12f, 1f));

        AssetDatabase.Refresh();

        // LobbyUI'ya otomatik bağla (MainMenu sahnesindeki component)
        AssignToLobbyUi(
            AssetDatabase.LoadAssetAtPath<Sprite>(Path.Combine(OutDir, "Ahu.png")),
            AssetDatabase.LoadAssetAtPath<Sprite>(Path.Combine(OutDir, "Yaman.png")));

        Debug.Log("[CaptureCharacterPortraits] Ahu.png + Yaman.png yazıldı ve LobbyUI'ya atandı.");
        EditorUtility.DisplayDialog(
            "Portreler hazır",
            "Ahu.png ve Yaman.png üretildi.\nAssets/Art/UI/Portraits\n\nLobbyUI portrait slotlarına bağlandı.",
            "Tamam");
    }

    private static void CaptureOne(string prefabPath, string pngPath, Color bg)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[CaptureCharacterPortraits] Prefab yok: {prefabPath}");
            return;
        }

        var root = new GameObject("_PortraitCaptureRoot");
        root.hideFlags = HideFlags.HideAndDontSave;

        try
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.SetParent(root.transform, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            // Animasyonu idle'da dondur
            foreach (var anim in instance.GetComponentsInChildren<Animator>(true))
            {
                anim.enabled = false;
                anim.Rebind();
            }

            Bounds bounds = CalculateBounds(instance);
            Transform head = FindBone(instance.transform, "Head")
                             ?? FindBone(instance.transform, "head")
                             ?? FindBone(instance.transform, "HeadTop_End");

            Vector3 focus = head != null
                ? head.position
                : new Vector3(bounds.center.x, bounds.min.y + bounds.size.y * 0.82f, bounds.center.z);

            var camGo = new GameObject("PortraitCam");
            camGo.transform.SetParent(root.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = bg;
            cam.fieldOfView = 28f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 20f;
            cam.allowHDR = false;
            cam.allowMSAA = true;

            // Hafif 3/4 açı + yukarıdan bakış
            Vector3 camPos = focus + new Vector3(0.18f, 0.06f, 0.95f);
            cam.transform.position = camPos;
            cam.transform.LookAt(focus + new Vector3(0f, 0.02f, 0f));

            // Key light
            var lightGo = new GameObject("KeyLight");
            lightGo.transform.SetParent(root.transform, false);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.color = new Color(1f, 0.96f, 0.9f);
            lightGo.transform.rotation = Quaternion.Euler(25f, -35f, 0f);

            int size = 768;
            var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 4;
            cam.targetTexture = rt;

            // Force render
            RenderTexture.active = rt;
            cam.Render();

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            tex.Apply();

            File.WriteAllBytes(pngPath, tex.EncodeToPNG());

            cam.targetTexture = null;
            RenderTexture.active = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);

            // Sprite import ayarları
            AssetDatabase.ImportAsset(pngPath);
            var importer = AssetImporter.GetAtPath(pngPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                importer.SaveAndReimport();
            }
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static Bounds CalculateBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return new Bounds(go.transform.position, Vector3.one);

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            b.Encapsulate(renderers[i].bounds);
        return b;
    }

    private static Transform FindBone(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name)
                return t;
        }
        return null;
    }

    private static void AssignToLobbyUi(Sprite ahu, Sprite yaman)
    {
        if (ahu == null || yaman == null) return;

        string[] guids = AssetDatabase.FindAssets("t:Prefab LobbyUI");
        // Scene objects: open MainMenu if needed via SerializedObject on loaded scenes
        var lobby = Object.FindFirstObjectByType<LobbyUI>(FindObjectsInactive.Include);
        if (lobby == null)
        {
            // Try load MainMenu additive temporarily
            string scenePath = "Assets/Sahneler/Levels/MainMenu.unity";
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                scenePath, UnityEditor.SceneManagement.OpenSceneMode.Additive);
            lobby = Object.FindFirstObjectByType<LobbyUI>(FindObjectsInactive.Include);
            if (lobby != null)
            {
                var so = new SerializedObject(lobby);
                so.FindProperty("ahuPortrait").objectReferenceValue = ahu;
                so.FindProperty("yamanPortrait").objectReferenceValue = yaman;
                so.ApplyModifiedPropertiesWithoutUndo();
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            }
            UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
            return;
        }

        var soLive = new SerializedObject(lobby);
        soLive.FindProperty("ahuPortrait").objectReferenceValue = ahu;
        soLive.FindProperty("yamanPortrait").objectReferenceValue = yaman;
        soLive.ApplyModifiedPropertiesWithoutUndo();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(lobby.gameObject.scene);
    }
}
#endif
