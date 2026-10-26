using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > Elevator Game > Close Wall Gaps.
/// Walls in the model that stop just short of each other leave see-through holes. This finds every open
/// edge of the building's meshes, looks a short way past it along its face, and stretches the edge to meet
/// the surface it finds there, with a little overlap. Gaps wider than the reach, such as doorways and
/// windows, are left open. The fixed meshes are saved in Assets/Generated/(scene name) and the swap can be undone.
/// </summary>
public static class WallGapCloser
{
    // Furthest an edge is stretched, in multiples of the player's height (doorways are much wider)
    const float ReachOfHeight = 0.2f;
    const string GeneratedFolder = "Assets/Generated";
    const string Title = "Close Wall Gaps";

    [MenuItem("Tools/Elevator Game/Close Wall Gaps")]
    static void CloseGaps()
    {
        var scene = SceneManager.GetActiveScene();
        var character = FloorPlayerSetup.FindCharacter(scene);
        var ignore = character != null ? character.transform : null;
        float height = character != null ? CharacterAnimationSetup.Height(character) : 0f;
        if (height <= 0f)
        {
            EditorUtility.DisplayDialog(Title, "Couldn't find chara01 in the open scene; it sets how far walls may be stretched.", "OK");
            return;
        }
        float reach = ReachOfHeight * height;

        FloorPlayerSetup.EnsureColliders(scene, ignore);   // Read/Write and generated colliders on the model
        var filters = FloorPlayerSetup.BuildingRoots(scene, ignore)
            .SelectMany(r => r.GetComponentsInChildren<MeshFilter>(true))
            .Where(mf => mf.sharedMesh != null).ToList();

        // Measure against the original walls; parts without a collider get a temporary one
        var temporary = new List<MeshCollider>();
        foreach (var mf in filters)
        {
            if (mf.GetComponent<Collider>() != null) continue;
            var c = mf.gameObject.AddComponent<MeshCollider>();
            c.sharedMesh = Original(mf);
            temporary.Add(c);
        }
        Physics.SyncTransforms();

        var results = new List<(MeshFilter mf, Mesh mesh, int moved)>();
        bool backfaces = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;   // single-sided walls count from either side
        try
        {
            for (int i = 0; i < filters.Count; i++)
            {
                var mf = filters[i];
                if (EditorUtility.DisplayCancelableProgressBar(Title, mf.name, (float)i / filters.Count)) return;
                var fixedMesh = Close(mf, Original(mf), reach, ignore, out int moved);
                if (fixedMesh != null) results.Add((mf, fixedMesh, moved));
            }
        }
        finally
        {
            Physics.queriesHitBackfaces = backfaces;
            EditorUtility.ClearProgressBar();
            foreach (var c in temporary) Object.DestroyImmediate(c);
        }

        string folder = GeneratedFolder + "/" + scene.name;
        if (results.Count > 0) EnsureFolder(folder);
        foreach (var (mf, mesh, _) in results)
        {
            var saved = Save(mf, mesh, folder);
            Undo.RecordObject(mf, Title);
            mf.sharedMesh = saved;
            var col = mf.GetComponent<MeshCollider>();
            if (col != null)
            {
                Undo.RecordObject(col, Title);
                col.sharedMesh = saved;
            }
        }
        AssetDatabase.SaveAssets();

        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"{scene.name}: closed gaps up to {reach:0.###} wide by stretching {results.Sum(r => r.moved)} edge point(s) " +
                  $"on {results.Count} mesh(es). Fixed meshes are in {folder}. Save the scene.");
    }

    // The model's own mesh, also when an earlier run already swapped in a fixed copy
    static Mesh Original(MeshFilter mf)
    {
        if (!AssetDatabase.GetAssetPath(mf.sharedMesh).StartsWith(GeneratedFolder + "/")) return mf.sharedMesh;
        var source = PrefabUtility.GetCorrespondingObjectFromSource(mf);
        return source != null && source.sharedMesh != null ? source.sharedMesh : mf.sharedMesh;
    }

    // Copy of the mesh with its open edges stretched onto whatever surface lies just past them; null if nothing moved
    static Mesh Close(MeshFilter mf, Mesh mesh, float reach, Transform ignore, out int moved)
    {
        moved = 0;
        if (!mesh.isReadable) return null;
        var verts = mesh.vertices;
        int n = verts.Length;

        // Weld by position so vertices split at hard edges and UV seams move together
        var weld = new int[n];
        var ids = new Dictionary<Vector3, int>();
        var firstIndex = new List<int>();
        for (int i = 0; i < n; i++)
        {
            if (!ids.TryGetValue(verts[i], out int id))
            {
                id = firstIndex.Count;
                ids[verts[i]] = id;
                firstIndex.Add(i);
            }
            weld[i] = id;
        }

        // Edges used by exactly one triangle are open; keep the triangle's third corner to tell inside from outside
        long m = firstIndex.Count;
        var edges = new Dictionary<long, (int a, int b, int c, int count)>();
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            if (mesh.GetTopology(s) != MeshTopology.Triangles) continue;
            var tris = mesh.GetTriangles(s);
            for (int i = 0; i < tris.Length; i += 3)
                for (int e = 0; e < 3; e++)
                {
                    int a = tris[i + e], b = tris[i + (e + 1) % 3], c = tris[i + (e + 2) % 3];
                    long key = Mathf.Min(weld[a], weld[b]) * m + Mathf.Max(weld[a], weld[b]);
                    edges[key] = edges.TryGetValue(key, out var found)
                        ? (found.a, found.b, found.c, found.count + 1)
                        : (a, b, c, 1);
                }
        }

        // Each open edge points outward along its face, away from the rest of its triangle
        var l2w = mf.transform.localToWorldMatrix;
        var outward = new Dictionary<int, List<Vector3>>();
        foreach (var e in edges.Values)
        {
            if (e.count != 1) continue;
            Vector3 A = l2w.MultiplyPoint3x4(verts[e.a]), B = l2w.MultiplyPoint3x4(verts[e.b]), C = l2w.MultiplyPoint3x4(verts[e.c]);
            Vector3 normal = Vector3.Cross(B - A, C - A);
            if (normal.sqrMagnitude < 1e-12f || (B - A).sqrMagnitude < 1e-12f) continue;
            Vector3 dir = Vector3.Cross(B - A, normal).normalized;
            if (Vector3.Dot(dir, C - A) > 0f) dir = -dir;
            foreach (int v in new[] { weld[e.a], weld[e.b] })
            {
                if (!outward.TryGetValue(v, out var list)) outward[v] = list = new List<Vector3>();
                if (!list.Any(d => Vector3.Dot(d, dir) > 0.95f)) list.Add(dir);
            }
        }
        if (outward.Count == 0) return null;   // closed mesh

        // Stretch each edge point along each of its outward directions to the surface past it
        var shift = new Dictionary<int, Vector3>();
        float overlap = reach * 0.05f;
        foreach (var kv in outward)
        {
            Vector3 p = l2w.MultiplyPoint3x4(verts[firstIndex[kv.Key]]);
            Vector3 total = Vector3.zero;
            foreach (var dir in kv.Value)
                if (GapTo(p, dir, reach, ignore, out float gap)) total += dir * (gap + overlap);
            if (total != Vector3.zero) shift[kv.Key] = total;
        }
        if (shift.Count == 0) return null;
        moved = shift.Count;

        var w2l = mf.transform.worldToLocalMatrix;
        for (int i = 0; i < n; i++)
            if (shift.TryGetValue(weld[i], out var s)) verts[i] += w2l.MultiplyVector(s);

        var result = Object.Instantiate(mesh);
        result.name = mesh.name;
        result.vertices = verts;
        result.RecalculateBounds();
        return result;
    }

    // Distance from an open edge to the surface just past it. A ray finds walls across the gap; a thin
    // sphere also finds a wall continuing in the same plane, which a ray along that plane would slip past.
    static bool GapTo(Vector3 p, Vector3 dir, float reach, Transform ignore, out float gap)
    {
        float skin = reach * 0.01f;
        Vector3 start = p + dir * skin;
        gap = float.MaxValue;
        foreach (var hit in Physics.RaycastAll(start, dir, reach, ~0, QueryTriggerInteraction.Ignore))
            if (!Ignored(hit, ignore)) gap = Mathf.Min(gap, hit.distance + skin);
        if (gap < float.MaxValue) return true;

        float radius = reach * 0.1f;
        foreach (var hit in Physics.SphereCastAll(start, radius, dir, reach, ~0, QueryTriggerInteraction.Ignore))
            if (!Ignored(hit, ignore)) gap = Mathf.Min(gap, hit.distance + radius + skin);
        return gap <= reach;
    }

    // Skips the player and anything already touching the edge where it starts
    static bool Ignored(RaycastHit hit, Transform ignore) =>
        hit.distance <= 0f || (ignore != null && hit.collider.transform.IsChildOf(ignore));

    static Mesh Save(MeshFilter mf, Mesh mesh, string folder)
    {
        // Overwrite the copy from an earlier run so scene references stay valid
        var current = mf.sharedMesh;
        if (AssetDatabase.GetAssetPath(current).StartsWith(GeneratedFolder + "/"))
        {
            EditorUtility.CopySerialized(mesh, current);
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(current);
            return current;
        }

        string name = string.Concat(mesh.name.Split(Path.GetInvalidFileNameChars()));
        string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{(name.Length > 0 ? name : "mesh")}.asset");
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    static void EnsureFolder(string folder)
    {
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
