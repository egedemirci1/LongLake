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
            int thresholds = EnsureDoorThresholds(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            EditorUtility.DisplayDialog(
                "Hazır",
                $"{doors} kapı obstacle Size → (0.01, 0.001, 0.03).\n" +
                $"{stripped} boş/kırık Door_Group MeshCollider kaldırıldı.\n" +
                $"{thresholds} DoorThreshold eklendi/güncellendi.\n\n" +
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
            int thresholds = EnsureDoorThresholds(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            EditorUtility.DisplayDialog(
                "Kapı NavMesh düzeltildi",
                $"{doors} obstacle Size → (0.01, 0.001, 0.03).\n" +
                $"{stripped} Door_Group boş MeshCollider silindi.\n" +
                $"{thresholds} DoorThreshold eklendi/güncellendi.\n\n" +
                "Sonra: LongLake → Bake CrashSite NavMesh (Doors Open)",
                "Tamam");
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    [MenuItem("LongLake/Prepare Door Thresholds (No Bake)")]
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
                "Sonra: LongLake → Bake CrashSite NavMesh (Doors Open)",
                "Tamam");
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    [MenuItem("LongLake/Add Doorway Nav Floors (No Bake)")]
    public static void AddDoorwayNavFloorsOnly()
    {
        PrepareDoorThresholdsOnly();
    }

    [MenuItem("LongLake/Bake CrashSite NavMesh (Doors Open)")]
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

    [MenuItem("LongLake/Remove Sky NavMesh Blockers (No Bake)")]
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

    /// <summary>
    /// Havada kalan dev düz collider'lar (Mesh=None, NavMeshBake proxy vb.) NavMesh'i gökyüzüne taşır.
    /// </summary>
    private static int StripSkyNavMeshBlockers(UnityEngine.SceneManagement.Scene scene)
    {
        int removed = 0;
        var toRemove = new List<Collider>();

        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var col in root.GetComponentsInChildren<Collider>(true))
            {
                if (col == null || col.isTrigger) continue;
                if (ShouldStripSkyBlocker(col, out _))
                    toRemove.Add(col);
            }
        }

        foreach (var col in toRemove)
        {
            if (col == null) continue;
            Debug.LogWarning($"[NavMesh] Sky blocker removed: {GetColliderPath(col)}", col.gameObject);
            Undo.DestroyObjectImmediate(col);
            removed++;
        }

        return removed;
    }

    private static bool ShouldStripSkyBlocker(Collider col, out string reason)
    {
        reason = string.Empty;
        Bounds b = col.bounds;

        if (col is MeshCollider meshCol && meshCol.sharedMesh == null)
        {
            reason = "MeshCollider (Mesh=None)";
            return true;
        }

        if (b.size.y > 6f)
            return false;

        bool wide = b.size.x >= 20f || b.size.z >= 20f;
        if (!wide)
            return false;

        float terrainY = SampleTerrainHeightEditor(b.center.x, b.center.z);
        if (float.IsNegativeInfinity(terrainY))
            return false;

        float above = b.center.y - terrainY;
        if (above <= 12f)
            return false;

        string n = col.gameObject.name ?? string.Empty;
        if (n.StartsWith("NavMeshBake", System.StringComparison.Ordinal))
        {
            reason = $"NavMeshBake proxy {above:F0}m above terrain";
            return true;
        }

        if (above > 18f)
        {
            reason = $"flat collider {above:F0}m above terrain (size {b.size})";
            return true;
        }

        return false;
    }

    private static float SampleTerrainHeightEditor(float worldX, float worldZ)
    {
        float best = float.NegativeInfinity;
        var terrains = Terrain.activeTerrains;
        if (terrains == null || terrains.Length == 0)
            return best;

        var probe = new Vector3(worldX, 0f, worldZ);
        foreach (var terrain in terrains)
        {
            if (terrain == null || terrain.terrainData == null) continue;

            Vector3 tp = terrain.transform.position;
            Vector3 size = terrain.terrainData.size;
            float lx = worldX - tp.x;
            float lz = worldZ - tp.z;
            if (lx < 0f || lz < 0f || lx > size.x || lz > size.z)
                continue;

            float h = terrain.SampleHeight(probe) + tp.y;
            if (h > best)
                best = h;
        }

        return best;
    }

    private static string GetColliderPath(Collider col)
    {
        if (col == null) return "?";
        var parts = new List<string>();
        Transform t = col.transform;
        while (t != null)
        {
            parts.Add(t.name);
            t = t.parent;
        }
        parts.Reverse();
        return string.Join("/", parts);
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
