#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

/// <summary>
/// AI Navigation paketinde klasik "Bake" sekmesi yok.
/// Bu menü NavMeshSurface ekler, kapıları bake dışı bırakır / açık konumda bake eder.
/// </summary>
public static class PrepareDoorNavMeshMenu
{
    private const string ScenePath = "Assets/Sahneler/Levels/CrashSite_Main.unity";

    [MenuItem("LongLake/Prepare Door NavMesh Obstacles")]
    public static void PrepareObstaclesOnly()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            int doors = PrepareDoorsInScene(scene, addObstacles: true);
            int stripped = StripBrokenDoorGroupMeshColliders(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            EditorUtility.DisplayDialog(
                "Hazır",
                $"{doors} kapı obstacle Size → (0.01, 0.001, 0.03).\n" +
                $"{stripped} boş/kırık Door_Group MeshCollider kaldırıldı.\n\n" +
                "Şimdi: LongLake → Bake CrashSite NavMesh (Doors Open)",
                "Tamam");
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    [MenuItem("LongLake/Fix Door NavMesh Obstacles (Safe Size)")]
    public static void FixDoorObstaclesSafeSize()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            int doors = PrepareDoorsInScene(scene, addObstacles: true);
            int stripped = StripBrokenDoorGroupMeshColliders(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            EditorUtility.DisplayDialog(
                "Kapı NavMesh düzeltildi",
                $"{doors} obstacle Size → (0.01, 0.001, 0.03).\n" +
                $"{stripped} Door_Group boş MeshCollider silindi.\n\n" +
                "Sahneyi kaydet, sonra Play / Bake.",
                "Tamam");
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    [MenuItem("LongLake/Bake CrashSite NavMesh (Doors Open)")]
    public static void BakeWithDoorsOpen()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        var savedDoorRotations = new List<(Transform t, Quaternion rot, bool active)>();

        try
        {
            PrepareDoorsInScene(scene, addObstacles: true);
            StripBrokenDoorGroupMeshColliders(scene);
            // Yol proxy / eşik köprüsü otomatik DEĞİL — tüm sahneyi bozuyordu.
            // Kapı geçidi için runtime NavMeshLink kullan (DoorController).

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var door in root.GetComponentsInChildren<DoorController>(true))
                {
                    var so = new SerializedObject(door);
                    bool knock = so.FindProperty("knockOnlyForQuest")?.boolValue ?? false;
                    float openZ = so.FindProperty("knockOpenZRotation")?.floatValue ?? -110f;
                    float openY = so.FindProperty("openRotation")?.floatValue ?? 90f;

                    savedDoorRotations.Add((door.transform, door.transform.localRotation, door.gameObject.activeSelf));

                    var obstacle = door.GetComponent<NavMeshObstacle>();
                    if (obstacle != null)
                        obstacle.enabled = false;

                    if (knock)
                        door.transform.localRotation = Quaternion.Euler(0f, 0f, openZ);
                    else
                        door.transform.localRotation = Quaternion.Euler(0f, 0f, openY);
                }
            }

            var surface = EnsureNavMeshSurface(scene);
            surface.agentTypeID = 0;
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = ~0;
            surface.defaultArea = 0;
            surface.ignoreNavMeshAgent = true;
            surface.ignoreNavMeshObstacle = true;
            surface.minRegionArea = 0.5f;

            Undo.RegisterCompleteObjectUndo(surface, "Bake NavMesh");
            surface.BuildNavMesh();

            foreach (var (t, rot, _) in savedDoorRotations)
            {
                if (t == null) continue;
                t.localRotation = rot;
                var obstacle = t.GetComponent<NavMeshObstacle>();
                if (obstacle != null)
                {
                    obstacle.enabled = true;
                    obstacle.carving = true;
                }
            }

            EditorUtility.SetDirty(surface);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            EditorUtility.DisplayDialog(
                "NavMesh Bake tamam",
                "Sade bake (kapılar açık, obstacle kapalı).\n" +
                "Yol proxy / eşik köprüsü EKLENMEDİ.\n\n" +
                "Kapı eşiği için: DoorController NavMeshLink (açılınca aktif).",
                "Tamam");
        }
        finally
        {
            foreach (var (t, rot, _) in savedDoorRotations)
            {
                if (t != null)
                    t.localRotation = rot;
            }

            EditorSceneManager.CloseScene(scene, true);
        }
    }

    [MenuItem("LongLake/Add Walkable Proxies Under Roads (No Bake)")]
    public static void AddRoadProxiesOnly()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            int n = EnsureRoadAndPadNavProxies(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            EditorUtility.DisplayDialog(
                "Proxy'ler hazır",
                $"{n} walkable BoxCollider eklendi/güncellendi.\n\n" +
                "Sonra: LongLake → Bake CrashSite NavMesh (Doors Open)",
                "Tamam");
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    /// <summary>
    /// SplineExtrude asfalt / yol mesh'leri NavMesh bake'de sık atlanır.
    /// Renderer bounds'a göre ince, düz BoxCollider child ekler (Default layer).
    /// </summary>
    private static int EnsureRoadAndPadNavProxies(UnityEngine.SceneManagement.Scene scene)
    {
        const string proxyName = "NavMeshBakeFloor";
        int count = 0;

        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var extrude in root.GetComponentsInChildren<UnityEngine.Splines.SplineExtrude>(true))
            {
                if (EnsureProxyUnder(extrude.transform, proxyName, extrude.GetComponent<Renderer>()))
                    count++;
            }

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                if (n == null) continue;
                string lower = n.ToLowerInvariant();
                if (!(lower.Contains("asphalt") || lower.Contains("asfalt")))
                    continue;
                if (t.GetComponent<UnityEngine.Splines.SplineExtrude>() != null)
                    continue; // already handled
                if (EnsureProxyUnder(t, proxyName, t.GetComponent<Renderer>()))
                    count++;
            }
        }

        // Safiye evi önü — bilinen hedef civarına pad
        EnsureStandalonePad(
            scene,
            "NavMeshBakePad_Safiye",
            new Vector3(1610.107f, 119.9632f, 643.2148f),
            new Vector3(24f, 0.4f, 24f));
        count++;

        return count;
    }

    private static bool EnsureProxyUnder(Transform parent, string proxyName, Renderer renderer)
    {
        if (parent == null) return false;

        Transform existing = parent.Find(proxyName);
        GameObject proxyGo;
        if (existing != null)
            proxyGo = existing.gameObject;
        else
        {
            proxyGo = new GameObject(proxyName);
            Undo.RegisterCreatedObjectUndo(proxyGo, "Create NavMesh bake proxy");
            proxyGo.transform.SetParent(parent, false);
        }

        proxyGo.layer = 0; // Default — NotWalkable layer'dan çık
        proxyGo.transform.localPosition = Vector3.zero;
        proxyGo.transform.localRotation = Quaternion.identity;
        proxyGo.transform.localScale = Vector3.one;

        var box = proxyGo.GetComponent<BoxCollider>();
        if (box == null)
            box = Undo.AddComponent<BoxCollider>(proxyGo);

        Bounds b;
        if (renderer != null)
            b = renderer.bounds;
        else if (parent.TryGetComponent<Collider>(out var col))
            b = col.bounds;
        else
            b = new Bounds(parent.position, new Vector3(20f, 1f, 20f));

        // World bounds → parent local
        Vector3 localCenter = parent.InverseTransformPoint(b.center);
        Vector3 localSize = parent.InverseTransformVector(b.size);
        localSize = new Vector3(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y), Mathf.Abs(localSize.z));

        // İnce yürünebilir plaka — yüksekliği sabitle, XY/XZ yayılımını koru
        float height = Mathf.Clamp(localSize.y, 0.2f, 1.5f);
        if (localSize.y < 0.5f)
            height = 0.5f;
        // Yol çok yassıysa yatay eksenleri büyütme; olduğu gibi kullan
        localSize.y = height;
        localCenter.y = Mathf.Max(localCenter.y, height * 0.5f);

        box.isTrigger = false;
        box.center = localCenter;
        box.size = new Vector3(
            Mathf.Max(localSize.x, 2f),
            height,
            Mathf.Max(localSize.z, 2f));

        var modifier = proxyGo.GetComponent<NavMeshModifier>();
        if (modifier == null)
            modifier = Undo.AddComponent<NavMeshModifier>(proxyGo);
        modifier.overrideArea = true;
        modifier.area = 0; // Walkable

        EditorUtility.SetDirty(proxyGo);
        return true;
    }

    private static void EnsureStandalonePad(
        UnityEngine.SceneManagement.Scene scene,
        string name,
        Vector3 worldPos,
        Vector3 size)
    {
        GameObject pad = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            var t = root.transform.Find(name);
            if (t != null) { pad = t.gameObject; break; }
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name) { pad = child.gameObject; break; }
            }
            if (pad != null) break;
        }

        if (pad == null)
        {
            pad = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(pad, "Create Safiye nav pad");
            SceneManagerMoveToScene(pad, scene);
        }

        pad.layer = 0;
        pad.transform.position = worldPos;
        pad.transform.rotation = Quaternion.identity;
        pad.transform.localScale = Vector3.one;

        var box = pad.GetComponent<BoxCollider>();
        if (box == null)
            box = Undo.AddComponent<BoxCollider>(pad);
        box.center = Vector3.zero;
        box.size = size;
        box.isTrigger = false;

        var modifier = pad.GetComponent<NavMeshModifier>();
        if (modifier == null)
            modifier = Undo.AddComponent<NavMeshModifier>(pad);
        modifier.overrideArea = true;
        modifier.area = 0;

        EditorUtility.SetDirty(pad);
    }

    private static int PrepareDoorsInScene(UnityEngine.SceneManagement.Scene scene, bool addObstacles)
    {
        int doors = 0;
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var door in root.GetComponentsInChildren<DoorController>(true))
            {
                doors++;

                // NavigationStatic artık deprecated; bake NavMeshSurface + CollectSources ile yapılır.
                // Kapılar bake'e Obstacle / açık rotasyon ile dahil edilir — static flag gerekmez.

                if (!addObstacles) continue;

                var obstacle = door.GetComponent<NavMeshObstacle>();
                if (obstacle == null)
                    obstacle = Undo.AddComponent<NavMeshObstacle>(door.gameObject);

                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.carving = true;
                obstacle.carveOnlyStationary = false;
                // Eski ince kapı carve boyutu.
                DoorController.ApplyThinDoorObstacleSize(obstacle);

                var so = new SerializedObject(door);
                var obstProp = so.FindProperty("navMeshObstacle");
                if (obstProp != null)
                    obstProp.objectReferenceValue = obstacle;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(door);
            }
        }

        return doors;
    }

    /// <summary>
    /// Door_Group* üzerindeki Mesh=None / anlamsız MeshCollider'ları kaldırır
    /// (Scene'de koca bounds + NavMesh deliği üretiyorlardı).
    /// </summary>
    private static int StripBrokenDoorGroupMeshColliders(UnityEngine.SceneManagement.Scene scene)
    {
        int removed = 0;
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || t.name == null) continue;
                if (!t.name.StartsWith("Door_Group", System.StringComparison.Ordinal))
                    continue;

                foreach (var mc in t.GetComponents<MeshCollider>())
                {
                    if (mc == null) continue;
                    if (mc.sharedMesh != null) continue;
                    Undo.DestroyObjectImmediate(mc);
                    removed++;
                }
            }
        }

        return removed;
    }

    private static NavMeshSurface EnsureNavMeshSurface(UnityEngine.SceneManagement.Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var existing = root.GetComponentInChildren<NavMeshSurface>(true);
            if (existing != null)
                return existing;
        }

        var go = new GameObject("NavMeshSurface");
        SceneManagerMoveToScene(go, scene);
        var surface = Undo.AddComponent<NavMeshSurface>(go);
        return surface;
    }

    private static void SceneManagerMoveToScene(GameObject go, UnityEngine.SceneManagement.Scene scene)
    {
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
    }
}
#endif
