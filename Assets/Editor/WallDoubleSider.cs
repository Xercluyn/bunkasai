using System.Collections.Generic;
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
    const string Title = "Make Walls Double-Sided";

    [MenuItem("Tools/Elevator Game/Make Walls Double-Sided", false, 81)]
    static void MakeDoubleSided()
    {
        if (!EditorKit.NotPlaying(Title)) return;
        var scene = SceneManager.GetActiveScene();
        if (Apply(scene) > 0) EditorSceneManager.MarkSceneDirty(scene);
    }

    [MenuItem("Tools/Elevator Game/Make Walls Double-Sided (All Floors)", false, 82)]
    static void MakeAllDoubleSided() => EditorKit.ForFloors(Title, 1, 5, (scene, _) => Apply(scene) > 0);

    // Swaps every building mesh in the scene for its double-sided copy; returns how many parts changed
    static int Apply(Scene scene)
    {
        var filters = EditorKit.BuildingRoots(scene)
            .SelectMany(r => r.GetComponentsInChildren<MeshFilter>(true))
            .Where(mf => mf.sharedMesh != null && EditorKit.IsModelMesh(mf.sharedMesh))
            .ToList();
        var outlined = EditorKit.OutlinedFilters(scene);

        string folder = EditorKit.GeneratedFolder + "/" + scene.name;
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
                    EditorKit.EnsureFolder(folder);
                    AssetDatabase.CreateAsset(both, AssetDatabase.GenerateUniqueAssetPath($"{folder}/{EditorKit.FileName(both.name)}.asset"));
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
}
