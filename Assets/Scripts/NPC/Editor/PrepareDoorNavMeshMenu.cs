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
public static partial class PrepareDoorNavMeshMenu
{
    private const string ScenePath = "Assets/Sahneler/Levels/CrashSite_Main.unity";

    [MenuItem("LongLake/NavMesh/Prepare Door Obstacles")]
    public static void PrepareObstaclesOnly()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            int doors = PrepareDoorsInScene(scene, addObstacles: true);
            int stripped = StripBrokenDoorGroupMeshColliders(scene);
            int thresholds = EnsureDoorThresholds(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            EditorUtility.DisplayDialog(
                "Hazır",
                $"{doors} kapı obstacle Size → (0.01, 0.001, 0.03).\n" +
                $"{stripped} boş/kırık Door_Group MeshCollider kaldırıldı.\n" +
                $"{thresholds} DoorThreshold eklendi/güncellendi.\n\n" +
                "Şimdi: LongLake → NavMesh → Bake CrashSite (Doors Open)",
                "Tamam");
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    [MenuItem("LongLake/NavMesh/Fix Door Obstacles (Safe Size)")]
    public static void FixDoorObstaclesSafeSize()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            int doors = PrepareDoorsInScene(scene, addObstacles: true);
            int stripped = StripBrokenDoorGroupMeshColliders(scene);
            int thresholds = EnsureDoorThresholds(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            EditorUtility.DisplayDialog(
                "Kapı NavMesh düzeltildi",
                $"{doors} obstacle Size → (0.01, 0.001, 0.03).\n" +
                $"{stripped} Door_Group boş MeshCollider silindi.\n" +
                $"{thresholds} DoorThreshold eklendi/güncellendi.\n\n" +
                "Sonra: LongLake → NavMesh → Bake CrashSite (Doors Open)",
                "Tamam");
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    [MenuItem("LongLake/NavMesh/Prepare Door Thresholds (No Bake)")]
    public static void PrepareDoorThresholdsOnly()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            int stripped = StripBrokenDoorGroupMeshColliders(scene);
            int n = EnsureDoorThresholds(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            EditorUtility.DisplayDialog(
                "Kapı eşikleri hazır",
                $"{n} Door_Group → Remove Object + DoorThreshold child.\n" +
                $"{stripped} boş MeshCollider silindi.\n\n" +
                "Sonra: LongLake → NavMesh → Bake CrashSite (Doors Open)",
                "Tamam");
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    [MenuItem("LongLake/NavMesh/Add Doorway Floors (No Bake)")]
    public static void AddDoorwayNavFloorsOnly()
    {
        PrepareDoorThresholdsOnly();
    }

    [MenuItem("LongLake/NavMesh/Bake CrashSite (Doors Open)")]
    public static void BakeWithDoorsOpen()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        var savedDoorRotations = new List<(Transform t, Quaternion rot, bool active)>();
        var savedDoorMeshColliders = new List<(MeshCollider col, bool enabled)>();

        try
        {
            PrepareDoorsInScene(scene, addObstacles: true);
            StripBrokenDoorGroupMeshColliders(scene);
            int skyBlockers = StripSkyNavMeshBlockers(scene);
            int thresholds = EnsureDoorThresholds(scene);

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

                    foreach (var mc in door.GetComponents<MeshCollider>())
                    {
                        savedDoorMeshColliders.Add((mc, mc.enabled));
                        mc.enabled = false;
                    }

                    var link = door.GetComponentInParent<NavMeshLink>(true);
                    if (link == null)
                    {
                        var group = door.transform;
                        while (group != null && (group.name == null || !group.name.StartsWith("Door_Group")))
                            group = group.parent;
                        if (group != null)
                            link = group.GetComponent<NavMeshLink>();
                    }
                    if (link != null)
                        link.activated = false;

                    if (knock)
                        door.transform.localRotation = Quaternion.Euler(0f, 0f, openZ);
                    else
                        door.transform.localRotation = Quaternion.Euler(0f, 0f, openY);
                }
            }

            var surface = EnsureNavMeshSurface(scene);
            surface.enabled = true;
            surface.agentTypeID = 0;
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = ~0;
            surface.defaultArea = 0;
            surface.ignoreNavMeshAgent = true;
            surface.ignoreNavMeshObstacle = true;
            surface.minRegionArea = 0.1f;

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

            foreach (var (col, wasEnabled) in savedDoorMeshColliders)
            {
                if (col != null)
                    col.enabled = wasEnabled;
            }

            EditorUtility.SetDirty(surface);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            EditorUtility.DisplayDialog(
                "NavMesh Bake tamam",
                $"{thresholds} kapı eşiği güncellendi.\n" +
                $"{skyBlockers} gökyüzü NavMesh blocker silindi.\n" +
                "Bake: eşikte mavi koridor.\n" +
                "Play: kapı açıkken NavMeshLink köprüsü de aktif.",
                "Tamam");
        }
        finally
        {
            foreach (var (t, rot, _) in savedDoorRotations)
            {
                if (t != null)
                    t.localRotation = rot;
            }

            foreach (var (col, wasEnabled) in savedDoorMeshColliders)
            {
                if (col != null)
                    col.enabled = wasEnabled;
            }

            EditorSceneManager.CloseScene(scene, true);
        }
    }

    /// <summary>Unity -executeMethod ile sahneye uygular (dialog yok).</summary>
    public static void BatchPrepareDoorThresholds()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        StripBrokenDoorGroupMeshColliders(scene);
        EnsureDoorThresholds(scene);
        EditorSceneManager.SaveScene(scene);
    }

    [MenuItem("LongLake/NavMesh/Remove Sky Blockers (No Bake)")]
    public static void RemoveSkyNavBlockersOnly()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            int n = StripSkyNavMeshBlockers(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            EditorUtility.DisplayDialog(
                "Gökyüzü blocker'lar",
                $"{n} collider silindi (havada dev düzlem).\n\n" +
                "Sonra: LongLake → Bake CrashSite NavMesh (Doors Open)",
                "Tamam");
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    [MenuItem("LongLake/NavMesh/Add Road Proxies (No Bake)")]
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

}
#endif
