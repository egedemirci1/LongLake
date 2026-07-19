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
