using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Helpers shared by the Tools > Elevator Game tools: asset folders and generated meshes, running a tool on the
/// open floor or on every floor, the building's parts, hiding model parts, and renderer bounds.
/// </summary>
public static class EditorKit
{
    public const string GeneratedFolder = "Assets/Generated";

    // ---------- assets ----------

    public static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }

    /// <summary>`name` without the characters a file name can't hold.</summary>
    public static string FileName(string name) => string.Concat(name.Split(Path.GetInvalidFileNameChars()));

    /// <summary>True for an asset saved under Assets/Generated (a mesh an earlier tool run made).</summary>
    public static bool IsGenerated(UnityEngine.Object asset) => AssetDatabase.GetAssetPath(asset).StartsWith(GeneratedFolder + "/");

    /// <summary>Meshes from a model file or earlier fixes, not ones a script built.</summary>
    public static bool IsModelMesh(Mesh mesh) => IsGenerated(mesh) || AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(mesh)) is ModelImporter;

    /// <summary>Saves the mesh as folder/(name).asset, overwriting an earlier copy in place so references to it stay valid.</summary>
    public static Mesh SaveMesh(Mesh mesh, string folder)
    {
        EnsureFolder(folder);
        string path = $"{folder}/{FileName(mesh.name)}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
        EditorUtility.CopySerialized(mesh, existing);
        UnityEngine.Object.DestroyImmediate(mesh);
        EditorUtility.SetDirty(existing);
        return existing;
    }

    /// <summary>`name` with any trailing "(2s)" and `tags` cut off (repeatedly), so re-runs keep one name.</summary>
    public static string StripTags(string name, params string[] tags)
    {
        var tails = new[] { " " + WallThickness.DoubleSidedSuffix, WallThickness.DoubleSidedSuffix }.Concat(tags).ToArray();
        for (bool cut = true; cut;)
        {
            cut = false;
            foreach (var tail in tails)
                if (name.EndsWith(tail)) { name = name.Substring(0, name.Length - tail.Length); cut = true; }
        }
        return name.Trim();
    }

    // ---------- running on floors ----------

    /// <summary>False, with a dialog, while in Play mode.</summary>
    public static bool NotPlaying(string title)
    {
        if (!EditorApplication.isPlaying) return true;
        EditorUtility.DisplayDialog(title, "Stop Play Mode first.", "OK");
        return false;
    }

    /// <summary>Runs `apply` on the open scene if it's a floor; returns false (with a dialog) otherwise.</summary>
    public static bool OnOpenFloor(string title, Action<Scene, int> apply)
    {
        if (!NotPlaying(title)) return false;
        var scene = SceneManager.GetActiveScene();
        int floor = SceneLinks.FloorOf(scene);
        if (floor == 0) { EditorUtility.DisplayDialog(title, "Open one of the five floor scenes first.", "OK"); return false; }
        apply(scene, floor);
        EditorSceneManager.MarkSceneDirty(scene);
        return true;
    }

    /// <summary>Opens floors `from`..`to` in turn, runs `apply` and saves each, then reopens what was open.</summary>
    public static bool ForFloors(string title, int from, int to, Action<Scene, int> apply) =>
        ForFloors(title, from, to, (scene, n) => { apply(scene, n); return true; });

    /// <summary>Same, saving only the floors where `apply` returns true (it changed something).</summary>
    public static bool ForFloors(string title, int from, int to, Func<Scene, int, bool> apply) =>
        ForScenes(title, Enumerable.Range(from, to - from + 1).Select(SceneLinks.Floor), (scene, key) => apply(scene, (int)key + 1));

    /// <summary>
    /// Opens each scene of `keys` in turn and runs `apply`, saving it when that returns true, then reopens the
    /// scenes that were open. Missing scenes are skipped with a warning. False when it didn't run (Play mode, or
    /// the save prompt was cancelled).
    /// </summary>
    public static bool ForScenes(string title, IEnumerable<SceneLinks.Key> keys, Func<Scene, SceneLinks.Key, bool> apply)
    {
        if (!NotPlaying(title)) return false;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
        var setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            foreach (var key in keys)
            {
                if (!SceneLinks.Exists(key)) { Debug.LogWarning($"{title}: no scene for {key}."); continue; }
                var scene = EditorSceneManager.OpenScene(SceneLinks.Path(key), OpenSceneMode.Single);
                if (!apply(scene, key)) continue;
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
        }
        finally
        {
            // Back to the scenes that were open (an unsaved new scene can't be reopened)
            if (setup.Length > 0 && setup.All(s => !string.IsNullOrEmpty(s.path))) EditorSceneManager.RestoreSceneManagerSetup(setup);
            else Debug.Log($"{title}: the scene you started from was never saved, so the last scene stays open.");
        }
        return true;
    }

    // ---------- the building ----------

    /// <summary>The floor's building roots (FloorPlayerSetup.BuildingRoots, minus the scene's character).</summary>
    public static IEnumerable<GameObject> BuildingRoots(Scene scene)
    {
        var character = FloorPlayerSetup.FindCharacter(scene);
        return FloorPlayerSetup.BuildingRoots(scene, character != null ? character.transform : null);
    }

    /// <summary>Every transform of the floor's building (not the props, player or zones) whose name starts with `prefix`.</summary>
    public static List<Transform> FindInBuilding(Scene scene, string prefix) =>
        BuildingRoots(scene)
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .Where(t => t.name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

    /// <summary>
    /// The mesh parts an InteractableOutline draws round. Mesh tools leave them alone: the outline pushes the
    /// mesh out along its normals, which cancel on a double-sided one.
    /// </summary>
    public static HashSet<MeshFilter> OutlinedFilters(Scene scene) =>
        new HashSet<MeshFilter>(All<InteractableOutline>(scene)
            .SelectMany(o => o.partMesh != null ? new[] { o.partMesh } : o.GetComponentsInChildren<MeshFilter>(true)));

    /// <summary>Every T in the scene, inactive ones included.</summary>
    public static IEnumerable<T> All<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true));

    /// <summary>Hides a model part (recorded on its prefab instance, with undo); false when it was already hidden.</summary>
    public static bool Hide(GameObject go, string undoName)
    {
        if (!go.activeSelf) return false;
        Undo.RecordObject(go, undoName);
        go.SetActive(false);
        PrefabUtility.RecordPrefabInstancePropertyModifications(go);
        return true;
    }

    // ---------- geometry ----------

    /// <summary>The combined world bounds of the renderers; null when there are none.</summary>
    public static Bounds? RendererBounds(IEnumerable<Renderer> renderers)
    {
        Bounds? total = null;
        foreach (var r in renderers)
        {
            if (total == null) { total = r.bounds; continue; }
            var b = total.Value;
            b.Encapsulate(r.bounds);
            total = b;
        }
        return total;
    }

    /// <summary>The object's path in the Hierarchy, for log messages.</summary>
    public static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;
}
