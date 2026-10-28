using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > Elevator Game > Make Walls Double-Sided.
/// Some faces in the floor models are wound the wrong way round. Blender draws both sides of every face,
/// but Unity only draws the front, so those faces (and any wall seen from behind) disappear as holes.
/// This gives every mesh of the building each face twice, once facing each way with its own normals, so it
/// is lit correctly from both sides. The meshes are saved in Assets/Generated/(scene name) as "(2s)" copies
/// and the swap can be undone. Run Close Wall Gaps first; it keeps walls double-sided if run again later.
/// Colliders keep their mesh: BuildingCollision already makes collision two-sided.
/// </summary>
public static class WallDoubleSider
{
    const string GeneratedFolder = "Assets/Generated";
    const string Title = "Make Walls Double-Sided";

    [MenuItem("Tools/Elevator Game/Make Walls Double-Sided")]
    static void MakeDoubleSided()
    {
        if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(Title, "Stop Play Mode first.", "OK"); return; }
        var scene = SceneManager.GetActiveScene();
        if (Apply(scene) > 0) EditorSceneManager.MarkSceneDirty(scene);
    }

    [MenuItem("Tools/Elevator Game/Make Walls Double-Sided (All Floors)")]
    static void MakeAllDoubleSided()
    {
        if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(Title, "Stop Play Mode first.", "OK"); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var setup = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            for (int n = 1; n <= 5; n++)
            {
                var key = SceneLinks.Floor(n);
                if (!SceneLinks.Exists(key)) { Debug.LogWarning($"{Title}: no scene for floor {n}."); continue; }
                var scene = EditorSceneManager.OpenScene(SceneLinks.Path(key), OpenSceneMode.Single);
                if (Apply(scene) > 0) EditorSceneManager.SaveScene(scene);
            }
        }
        finally
        {
            // Back to the scenes that were open (an unsaved new scene can't be reopened)
            if (setup.Length > 0 && setup.All(s => !string.IsNullOrEmpty(s.path))) EditorSceneManager.RestoreSceneManagerSetup(setup);
            else Debug.Log($"{Title}: the scene you started from was never saved, so the last floor stays open.");
        }
    }

    // Swaps every building mesh in the scene for its double-sided copy; returns how many parts changed
    static int Apply(Scene scene)
    {
        var character = FloorPlayerSetup.FindCharacter(scene);
        var ignore = character != null ? character.transform : null;
        var filters = FloorPlayerSetup.BuildingRoots(scene, ignore)
            .SelectMany(r => r.GetComponentsInChildren<MeshFilter>(true))
            .Where(mf => mf.sharedMesh != null && IsModelMesh(mf.sharedMesh))
            .ToList();

        // Outlined parts are left alone: their outline pushes the mesh out along its normals, which cancel on a double-sided one
        var outlined = new HashSet<MeshFilter>(scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<InteractableOutline>(true))
            .SelectMany(o => o.partMesh != null ? new[] { o.partMesh } : o.GetComponentsInChildren<MeshFilter>(true)));

        string folder = GeneratedFolder + "/" + scene.name;
        var made = new Dictionary<Mesh, Mesh>();   // parts sharing a mesh share its copy
        int parts = 0, skipped = 0;
        long triangles = 0;
        try
        {
            for (int i = 0; i < filters.Count; i++)
            {
                var mf = filters[i];
                var mesh = mf.sharedMesh;
                EditorUtility.DisplayProgressBar(Title, mf.name, (float)i / filters.Count);
                if (WallThickness.IsDoubleSided(mesh) || outlined.Contains(mf)) { skipped++; continue; }
                if (!mesh.isReadable)
                {
                    Debug.LogWarning($"{Title}: can't read mesh '{mesh.name}'. Turn on Read/Write in its model's import settings.", mf);
                    skipped++;
                    continue;
                }

                if (!made.TryGetValue(mesh, out var both))
                {
                    both = DoubleSided(mesh, mesh.name + " " + WallThickness.DoubleSidedSuffix);
                    EnsureFolder(folder);
                    string name = string.Concat(both.name.Split(Path.GetInvalidFileNameChars()));
                    AssetDatabase.CreateAsset(both, AssetDatabase.GenerateUniqueAssetPath($"{folder}/{name}.asset"));
                    made[mesh] = both;
                    triangles += TriangleCount(mesh);
                }
                Undo.RecordObject(mf, Title);
                mf.sharedMesh = both;
                parts++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
        AssetDatabase.SaveAssets();

        Debug.Log($"{scene.name}: made {made.Count} mesh(es) on {parts} part(s) double-sided ({triangles} triangle(s), now drawn both ways); " +
                  $"{skipped} part(s) skipped (already double-sided, outlined or unreadable). Meshes are in {folder}.");
        return parts;
    }

    // Meshes from a model file or earlier fixes (Close Wall Gaps), not ones a script built
    static bool IsModelMesh(Mesh mesh)
    {
        string path = AssetDatabase.GetAssetPath(mesh);
        return path.StartsWith(GeneratedFolder + "/") || AssetImporter.GetAtPath(path) is ModelImporter;
    }

    /// <summary>
    /// Copy of the mesh with every triangle twice: as it is, and wound the other way on copied vertices
    /// whose normals point the other way. Submeshes, UVs and colours are kept.
    /// </summary>
    internal static Mesh DoubleSided(Mesh mesh, string name)
    {
        int n = mesh.vertexCount;
        var normals = mesh.normals;
        if (normals.Length != n)
        {
            var tmp = Object.Instantiate(mesh);
            tmp.RecalculateNormals();   // some exported parts (e.g. doors) have no normals
            normals = tmp.normals;
            Object.DestroyImmediate(tmp);
        }

        var result = new Mesh { name = name };
        if (n * 2 > 65535) result.indexFormat = IndexFormat.UInt32;
        result.vertices = Doubled(mesh.vertices, n);
        result.normals = Doubled(normals, n).Select((v, i) => i < n ? v : -v).ToArray();
        var tangents = mesh.tangents;
        if (tangents.Length == n)
            // Same tangent, flipped handedness: with the normal reversed the bitangent keeps following the texture
            result.tangents = Doubled(tangents, n).Select((t, i) => i < n ? t : new Vector4(t.x, t.y, t.z, -t.w)).ToArray();
        var colors = mesh.colors;
        if (colors.Length == n) result.colors = Doubled(colors, n);
        var uvs = new List<Vector4>();
        for (int c = 0; c < 8; c++)
        {
            mesh.GetUVs(c, uvs);
            if (uvs.Count == n) result.SetUVs(c, Doubled(uvs.ToArray(), n));
        }

        result.subMeshCount = mesh.subMeshCount;
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            var topology = mesh.GetTopology(s);
            var indices = mesh.GetIndices(s);   // with the submesh's base vertex applied
            if (topology != MeshTopology.Triangles)
            {
                result.SetIndices(indices, topology, s, false);
                continue;
            }
            var tris = new int[indices.Length * 2];
            indices.CopyTo(tris, 0);
            for (int i = 0; i < indices.Length; i += 3)
            {
                tris[indices.Length + i] = n + indices[i];
                tris[indices.Length + i + 1] = n + indices[i + 2];
                tris[indices.Length + i + 2] = n + indices[i + 1];
            }
            result.SetTriangles(tris, s, false);
        }
        result.RecalculateBounds();
        return result;
    }

    static long TriangleCount(Mesh mesh)
    {
        long count = 0;
        for (int s = 0; s < mesh.subMeshCount; s++)
            if (mesh.GetTopology(s) == MeshTopology.Triangles) count += mesh.GetIndexCount(s) / 3;
        return count;
    }

    static T[] Doubled<T>(T[] values, int n)
    {
        var result = new T[n * 2];
        values.CopyTo(result, 0);
        values.CopyTo(result, n);
        return result;
    }

    static void EnsureFolder(string folder)
    {
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
