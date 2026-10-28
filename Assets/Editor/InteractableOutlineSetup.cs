using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > Elevator Game > Outline Interactables: gives the object behind every ElevatorPlayZone in the open
/// scene (the elevator, the arcade box) an orange InteractableOutline, and links the zone to it so the outline
/// lights up while the Play button shows. A zone's object is its Interactable field, or else the nearest
/// object with "elevator" or "arcade" in its name. On an elevator only its button is outlined: the small
/// separate piece of the elevator mesh nearest the zone, which is also where the Play button floats.
/// </summary>
public static class InteractableOutlineSetup
{
    const string MaterialPath = "Assets/Materials/InteractableOutline.mat";
    const string ShaderName = "Elevator Game/Interactable Outline";
    const string Title = "Outline Interactables";
    static readonly string[] Keywords = { "elevator", "arcade" };

    [MenuItem("Tools/Elevator Game/Outline Interactables")]
    static void OutlineAll() => OutlineAllZones(true);

    internal static void OutlineAllZones(bool dialogs)
    {
        var scene = SceneManager.GetActiveScene();
        var zones = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ElevatorPlayZone>(true)).ToList();
        if (zones.Count == 0)
        {
            if (dialogs) EditorUtility.DisplayDialog(Title, "No ElevatorPlayZone in the open scene, so nothing is interactable here.", "OK");
            return;
        }

        int done = 0;
        foreach (var zone in zones)
        {
            var target = zone.interactable != null ? zone.interactable : Guess(scene, zone);
            if (target == null)
            {
                Debug.LogWarning($"{zone.name}: couldn't tell which object it is for. Drag the object into its Interactable field and run {Title} again.", zone);
                continue;
            }
            Undo.RecordObject(zone, Title);
            zone.interactable = target;
            Apply(target);
            string what = target.name;
            if (target.name.ToLowerInvariant().Contains("elevator") && OutlineButtonOnly(zone, target)) what += "'s button";
            Debug.Log($"{zone.name}: outlining {what}.", target);
            done++;
        }
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"{scene.name}: {done} interactable object(s) outlined in orange. Save the scene.");
    }

    /// <summary>Adds (or refreshes) the orange outline on an object.</summary>
    internal static void Apply(GameObject target)
    {
        // Read/Write lets the outline copy the meshes at runtime
        foreach (var path in target.GetComponentsInChildren<Transform>(true)
                     .Select(t => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject))
                     .Where(p => !string.IsNullOrEmpty(p)).Distinct())
        {
            if (!(AssetImporter.GetAtPath(path) is ModelImporter importer) || importer.isReadable) continue;
            importer.isReadable = true;
            importer.SaveAndReimport();
            Debug.Log("Turned on Read/Write for " + path + ".");
        }

        var outline = target.GetComponent<InteractableOutline>();
        if (outline == null) outline = Undo.AddComponent<InteractableOutline>(target);
        Undo.RecordObject(outline, Title);
        outline.outlineMaterial = OutlineMaterial();
        EditorSceneManager.MarkSceneDirty(target.scene);
    }

    // Outline just the button and float the Play button over it; false if no separate button piece was found
    static bool OutlineButtonOnly(ElevatorPlayZone zone, GameObject elevator)
    {
        Vector3 at = zone.GetComponent<Collider>() != null ? zone.GetComponent<Collider>().bounds.center : zone.transform.position;
        MeshFilter bestMesh = null;
        Bounds bestPart = default;
        float bestDistance = float.MaxValue;
        foreach (var mf in elevator.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
            var pieces = Pieces(mf.sharedMesh);
            if (pieces.Count < 2) continue;
            // The biggest piece is the elevator itself. The call button is among the smallest of the rest: the
            // elevator also has bigger fittings above its doors that may be nearer the zone, so skip those.
            static float Volume(Bounds p) => p.size.x * p.size.y * p.size.z;
            var body = pieces.OrderByDescending(Volume).First();
            float smallest = pieces.Where(p => p != body).Select(Volume).DefaultIfEmpty(0f).Min();
            foreach (var p in pieces)
            {
                if (p == body || Volume(p) > smallest * 1.5f + 1e-12f) continue;
                float d = Vector3.Distance(at, mf.transform.TransformPoint(p.center));
                if (d < bestDistance) { bestDistance = d; bestMesh = mf; bestPart = p; }
            }
        }
        if (bestMesh == null) return false;

        var outline = elevator.GetComponent<InteractableOutline>();
        Undo.RecordObject(outline, Title);
        outline.partMesh = bestMesh;
        outline.partBounds = bestPart;

        var anchor = zone.transform.Find("ButtonAnchor");
        if (anchor == null)
        {
            anchor = new GameObject("ButtonAnchor").transform;
            anchor.SetParent(zone.transform, false);
            Undo.RegisterCreatedObjectUndo(anchor.gameObject, Title);
        }
        anchor.position = bestMesh.transform.TransformPoint(bestPart.center);
        if (zone.buttonTarget == null || zone.buttonTarget == anchor)
        {
            zone.buttonTarget = anchor;
        }
        return true;
    }

    // Local bounds of each separate piece of a mesh (triangles joined through shared corner positions)
    internal static List<Bounds> Pieces(Mesh mesh)
    {
        var verts = mesh.vertices;
        var ids = new Dictionary<Vector3, int>();
        var weld = new int[verts.Length];
        for (int i = 0; i < verts.Length; i++)
        {
            if (!ids.TryGetValue(verts[i], out int id)) ids[verts[i]] = id = ids.Count;
            weld[i] = id;
        }
        var parent = Enumerable.Range(0, ids.Count).ToArray();
        int Root(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }

        var tris = new List<int>();
        for (int s = 0; s < mesh.subMeshCount; s++)
            if (mesh.GetTopology(s) == MeshTopology.Triangles) tris.AddRange(mesh.GetTriangles(s));
        for (int i = 0; i < tris.Count; i += 3)
        {
            parent[Root(weld[tris[i + 1]])] = Root(weld[tris[i]]);
            parent[Root(weld[tris[i + 2]])] = Root(weld[tris[i]]);
        }

        var pieces = new Dictionary<int, Bounds>();
        foreach (int v in tris)
        {
            int r = Root(weld[v]);
            if (pieces.TryGetValue(r, out var b)) { b.Encapsulate(verts[v]); pieces[r] = b; }
            else pieces[r] = new Bounds(verts[v], Vector3.zero);
        }
        return pieces.Values.ToList();
    }

    static Material OutlineMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat != null) return mat;
        var shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            Debug.LogError($"Shader '{ShaderName}' not found (Assets/Shaders/InteractableOutline.shader).");
            return null;
        }
        if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
        mat = new Material(shader) { name = "InteractableOutline" };
        AssetDatabase.CreateAsset(mat, MaterialPath);
        return mat;
    }

    // Nearest object named like an elevator or arcade machine that has meshes, measured from the zone;
    // the whole group, not one of its parts that carry the same name
    static GameObject Guess(Scene scene, ElevatorPlayZone zone)
    {
        Vector3 at = zone.GetComponent<Collider>() != null ? zone.GetComponent<Collider>().bounds.center : zone.transform.position;
        return scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .Where(t => !t.IsChildOf(zone.transform) && Named(t) && (t.parent == null || !Named(t.parent)) &&
                        t.GetComponentInChildren<MeshFilter>() != null)
            .OrderBy(t => Vector3.Distance(at, t.GetComponentsInChildren<Renderer>().Select(r => r.bounds.center)
                .DefaultIfEmpty(t.position).First()))
            .Select(t => t.gameObject)
            .FirstOrDefault();
    }

    static bool Named(Transform t) => Keywords.Any(k => t.name.ToLowerInvariant().Contains(k));
}
