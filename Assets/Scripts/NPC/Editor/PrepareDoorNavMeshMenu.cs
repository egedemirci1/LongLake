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
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            EditorUtility.DisplayDialog(
                "Hazır",
                $"{doors} kapıya NavMeshObstacle eklendi.\n\n" +
                "Şimdi: LongLake → Bake CrashSite NavMesh (Doors Open)",
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

            // Bake sırasında kapıları açık konuma al — eşikte walkable koridor oluşsun.
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var door in root.GetComponentsInChildren<DoorController>(true))
                {
                    var so = new SerializedObject(door);
                    bool knock = so.FindProperty("knockOnlyForQuest")?.boolValue ?? false;
                    float openZ = so.FindProperty("knockOpenZRotation")?.floatValue ?? -110f;
                    float openY = so.FindProperty("openRotation")?.floatValue ?? 90f;

                    savedDoorRotations.Add((door.transform, door.transform.localRotation, door.gameObject.activeSelf));

                    // Obstacle bake'i etkilemesin diye geçici kapat.
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
            surface.agentTypeID = 0; // Humanoid — Agents sekmesindeki Radius/Height kullanılır
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
            surface.ignoreNavMeshAgent = true;
            surface.ignoreNavMeshObstacle = true;

            Undo.RegisterCompleteObjectUndo(surface, "Bake NavMesh");
            surface.BuildNavMesh();

            // Kapı rotasyonlarını geri al, obstacle'ları tekrar aç (kapalı = carve).
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
                "Kapılar açıkken bake edildi, sonra kapalı konuma alındı.\n\n" +
                "Kontrol: Scene görünümünde Navigation overlay / NavMeshSurface ile " +
                "kapı eşiğinde mavi yürünebilir koridor olmalı.\n\n" +
                "Play'de kapı kapalıyken Obstacle yolu keser; açılınca İsmail geçebilir.",
                "Tamam");
        }
        finally
        {
            // Hata olursa rotasyonları yine de geri almaya çalış.
            foreach (var (t, rot, _) in savedDoorRotations)
            {
                if (t != null)
                    t.localRotation = rot;
            }

            EditorSceneManager.CloseScene(scene, true);
        }
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

                if (door.TryGetComponent<Collider>(out var col))
                {
                    Bounds b = col.bounds;
                    Vector3 localSize = door.transform.InverseTransformVector(b.size);
                    localSize = new Vector3(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y), Mathf.Abs(localSize.z));
                    localSize.x = Mathf.Max(localSize.x, 0.25f);
                    localSize.y = Mathf.Max(localSize.y, 1.8f);
                    localSize.z = Mathf.Max(localSize.z, 0.25f);
                    obstacle.size = localSize;
                    obstacle.center = door.transform.InverseTransformPoint(b.center);
                }

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
