#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

// Partial: split for maintainability. Type identity unchanged.
public static partial class PrepareDoorNavMeshMenu
{
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
    /// Door_Group* üzerindeki tüm MeshCollider'ları kaldırır (boş mesh dahil).
    /// Bake'te duvar gibi davranıp eşiği blokluyorlardı.
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
                    Undo.DestroyObjectImmediate(mc);
                    removed++;
                }
            }
        }

        return removed;
    }

    /// <summary>
    /// Door_Group: Mode Remove Object (bake'ten duvar collider'ları çıkar).
    /// DoorThreshold child: yatay BoxCollider + Walkable — eşikte mavi koridor.
    /// </summary>
    private static int EnsureDoorThresholds(UnityEngine.SceneManagement.Scene scene)
    {
        const string thresholdName = "DoorThreshold";
        const string legacyFloorName = "DoorwayNavFloor";
        Vector3 defaultThroughA = new Vector3(0f, 0.61f, 0f);
        Vector3 defaultThroughB = new Vector3(0f, -0.59f, 0f);

        int count = 0;
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var doorGroup in root.GetComponentsInChildren<Transform>(true))
            {
                if (doorGroup == null || doorGroup.name == null) continue;
                if (!doorGroup.name.StartsWith("Door_Group", System.StringComparison.Ordinal))
                    continue;

                Vector3 localThroughA = defaultThroughA;
                Vector3 localThroughB = defaultThroughB;
                float linkWidth = 1.6f;
                var doorCtrl = doorGroup.GetComponentInChildren<DoorController>(true);
                if (doorCtrl != null)
                {
                    var so = new SerializedObject(doorCtrl);
                    localThroughA = so.FindProperty("doorwayLinkStartPoint")?.vector3Value ?? defaultThroughA;
                    localThroughB = so.FindProperty("doorwayLinkEndPoint")?.vector3Value ?? defaultThroughB;
                    linkWidth = so.FindProperty("doorwayLinkWidth")?.floatValue ?? 1.6f;
                }

                var groupModifier = doorGroup.GetComponent<NavMeshModifier>();
                if (groupModifier == null)
                    groupModifier = Undo.AddComponent<NavMeshModifier>(doorGroup.gameObject);
                groupModifier.ignoreFromBuild = true;
                groupModifier.applyToChildren = true;
                groupModifier.overrideArea = false;
                EditorUtility.SetDirty(doorGroup.gameObject);

                if (doorCtrl != null)
                    EnsureDoorPanelExcludedFromBake(doorCtrl);

                Transform legacy = doorGroup.Find(legacyFloorName);
                if (legacy != null)
                {
                    legacy.name = thresholdName;
                    EditorUtility.SetDirty(legacy.gameObject);
                }

                Transform existing = doorGroup.Find(thresholdName);
                GameObject thresholdGo;
                if (existing != null)
                    thresholdGo = existing.gameObject;
                else
                {
                    thresholdGo = new GameObject(thresholdName);
                    Undo.RegisterCreatedObjectUndo(thresholdGo, "Create DoorThreshold");
                    thresholdGo.transform.SetParent(doorGroup, false);
                }

                thresholdGo.layer = 0;
                thresholdGo.transform.localScale = Vector3.one;

                Vector3 worldA = doorGroup.TransformPoint(localThroughA);
                Vector3 worldB = doorGroup.TransformPoint(localThroughB);
                Vector3 mid = (worldA + worldB) * 0.5f;
                Vector3 through = worldA - worldB;
                if (through.sqrMagnitude < 0.01f)
                    through = doorGroup.forward;
                through.Normalize();

                thresholdGo.transform.SetParent(null, true);
                thresholdGo.transform.position = mid;
                thresholdGo.transform.rotation = Quaternion.LookRotation(through, Vector3.up);
                thresholdGo.transform.SetParent(doorGroup, true);

                var box = thresholdGo.GetComponent<BoxCollider>();
                if (box == null)
                    box = Undo.AddComponent<BoxCollider>(thresholdGo);

                float depth = Mathf.Max(1.5f, Vector3.Distance(worldA, worldB) + 0.4f);
                box.center = Vector3.zero;
                box.size = new Vector3(Mathf.Max(linkWidth, 1.5f), 0.28f, depth);
                box.isTrigger = false;

                var thresholdModifier = thresholdGo.GetComponent<NavMeshModifier>();
                if (thresholdModifier == null)
                    thresholdModifier = Undo.AddComponent<NavMeshModifier>(thresholdGo);
                thresholdModifier.ignoreFromBuild = false;
                thresholdModifier.applyToChildren = false;
                thresholdModifier.overrideArea = true;
                thresholdModifier.area = 0;

                EnsureDoorGroupNavMeshLink(doorGroup, localThroughA, localThroughB, linkWidth);

                EditorUtility.SetDirty(thresholdGo);
                count++;
            }
        }

        return count;
    }

    private static void EnsureDoorPanelExcludedFromBake(DoorController door)
    {
        if (door == null) return;

        var modifier = door.GetComponent<NavMeshModifier>();
        if (modifier == null)
            modifier = Undo.AddComponent<NavMeshModifier>(door.gameObject);
        modifier.ignoreFromBuild = true;
        modifier.applyToChildren = false;
        modifier.overrideArea = false;
        EditorUtility.SetDirty(door);
    }

    private static void EnsureDoorGroupNavMeshLink(
        Transform doorGroup,
        Vector3 localStart,
        Vector3 localEnd,
        float width)
    {
        var link = doorGroup.GetComponent<NavMeshLink>();
        if (link == null)
            link = Undo.AddComponent<NavMeshLink>(doorGroup.gameObject);

        link.agentTypeID = 0;
        link.startPoint = localStart;
        link.endPoint = localEnd;
        link.width = Mathf.Max(width, 1.4f);
        link.bidirectional = true;
        link.autoUpdate = false;
        link.area = 0;
        link.activated = false;

        var doorCtrl = doorGroup.GetComponentInChildren<DoorController>(true);
        if (doorCtrl != null)
        {
            var so = new SerializedObject(doorCtrl);
            var linkProp = so.FindProperty("doorwayNavMeshLink");
            var useLinkProp = so.FindProperty("useDoorwayNavMeshLink");
            if (linkProp != null)
                linkProp.objectReferenceValue = link;
            if (useLinkProp != null)
                useLinkProp.boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(doorCtrl);
        }

        EditorUtility.SetDirty(link);
    }

}
#endif
