using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Gives single-sided parts of the building (walls and floors modelled as one flat face) real thickness:
/// a back face is added behind each one and the open edges are closed off, so they stay visible from
/// both sides. Closed meshes are left alone. Runs before BuildingCollision so the colliders get the thick
/// walls too. Put it on the building's root (Tools > Elevator Game > Setup Building); the model needs
/// Read/Write enabled.
/// </summary>
[DefaultExecutionOrder(-100)]
public class WallThickness : MonoBehaviour
{
    [Tooltip("Wall thickness in world units. 0 = 8% of the player's height.")]
    public float thickness = 0f;

    /// <summary>Ends the name of a mesh that already has every face both ways round (Make Walls Double-Sided).</summary>
    public const string DoubleSidedSuffix = "(2s)";

    public static bool IsDoubleSided(Mesh mesh) => mesh != null && mesh.name.EndsWith(DoubleSidedSuffix);

    static readonly Dictionary<(Mesh, float), Mesh> solidified = new Dictionary<(Mesh, float), Mesh>();

    void Awake()
    {
        float t = thickness > 0f ? thickness : AutoThickness();
        if (t <= 0f) return;

        foreach (var mf in GetComponentsInChildren<MeshFilter>(true))
        {
            var mesh = mf.sharedMesh;
            if (mesh == null || IsDoubleSided(mesh)) continue;   // already seen from both sides; a back shell would sit askew
            if (!mesh.isReadable)
            {
                Debug.LogWarning($"WallThickness: can't read mesh '{mesh.name}'. Turn on Read/Write in its model's import settings.", mf);
                continue;
            }

            // Thickness is in world units; the mesh is offset in its own space
            Vector3 s = mf.transform.lossyScale;
            float scale = (Mathf.Abs(s.x) + Mathf.Abs(s.y) + Mathf.Abs(s.z)) / 3f;
            float local = t / Mathf.Max(scale, 1e-6f);

            if (!solidified.TryGetValue((mesh, local), out var solid) || solid == null)
                solidified[(mesh, local)] = solid = Solidify(mesh, local);
            if (solid != null) mf.sharedMesh = solid;
        }
    }

    float AutoThickness()
    {
        var player = FindAnyObjectByType<PlayerMovement>();
        if (player != null && TryGetHeight(player.gameObject, out float h)) return 0.08f * h;
        return TryGetHeight(gameObject, out float bh) ? 0.005f * bh : 0f;
    }

    static bool TryGetHeight(GameObject go, out float height)
    {
        height = 0f;
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return false;
        Bounds b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        height = b.size.y;
        return height > 0f;
    }

    // Copy of the mesh with a back shell offset behind every face and side walls along its open edges.
    // Returns null for a closed mesh, which already has an inside and an outside.
    static Mesh Solidify(Mesh mesh, float depth)
    {
        var verts = mesh.vertices;
        int n = verts.Length;
        var normals = mesh.normals;
        if (normals.Length != n)
        {
            var tmp = Instantiate(mesh);
            tmp.RecalculateNormals();   // some exported parts (e.g. doors) have no normals
            normals = tmp.normals;
            Destroy(tmp);
        }

        // Weld by position so vertices split at hard edges and UV seams move together and edges match up
        var weld = new int[n];
        var ids = new Dictionary<Vector3, int>();
        var normalSum = new List<Vector3>();
        for (int i = 0; i < n; i++)
        {
            if (!ids.TryGetValue(verts[i], out int id))
            {
                id = normalSum.Count;
                ids[verts[i]] = id;
                normalSum.Add(Vector3.zero);
            }
            weld[i] = id;
            normalSum[id] += normals[i];
        }

        // Edges used by exactly one triangle are open, remembered in the winding of that triangle
        long m = normalSum.Count;
        var edges = new Dictionary<long, (int a, int b, int sub, int count)>();
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            if (mesh.GetTopology(s) != MeshTopology.Triangles) continue;
            var tris = mesh.GetTriangles(s);
            for (int i = 0; i < tris.Length; i += 3)
                for (int e = 0; e < 3; e++)
                {
                    int a = tris[i + e], b = tris[i + (e + 1) % 3];
                    int wa = weld[a], wb = weld[b];
                    long key = Mathf.Min(wa, wb) * m + Mathf.Max(wa, wb);
                    edges[key] = edges.TryGetValue(key, out var found)
                        ? (found.a, found.b, found.sub, found.count + 1)
                        : (a, b, s, 1);
                }
        }

        var open = new List<(int a, int b, int sub)>();
        foreach (var e in edges.Values) if (e.count == 1) open.Add((e.a, e.b, e.sub));
        if (open.Count == 0) return null;

        // Back shell: every vertex pushed behind its face, facing the other way
        var newVerts = new Vector3[n * 2];
        var newNormals = new Vector3[n * 2];
        for (int i = 0; i < n; i++)
        {
            Vector3 dir = normalSum[weld[i]].sqrMagnitude > 1e-12f ? normalSum[weld[i]].normalized : normals[i];
            newVerts[i] = verts[i];
            newNormals[i] = normals[i];
            newVerts[n + i] = verts[i] - dir * depth;
            newNormals[n + i] = -normals[i];
        }

        var result = new Mesh { name = mesh.name + " (thick)" };
        if (n * 2 > 65535) result.indexFormat = IndexFormat.UInt32;
        result.vertices = newVerts;
        result.normals = newNormals;
        result.uv = Doubled(mesh.uv, n);
        result.uv2 = Doubled(mesh.uv2, n);
        result.colors = Doubled(mesh.colors, n);
        result.subMeshCount = mesh.subMeshCount;

        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            if (mesh.GetTopology(s) != MeshTopology.Triangles)
            {
                result.SetIndices(mesh.GetIndices(s), mesh.GetTopology(s), s, false);
                continue;
            }
            var front = mesh.GetTriangles(s);
            var tris = new List<int>(front.Length * 2);
            tris.AddRange(front);
            for (int i = 0; i < front.Length; i += 3)
            {
                tris.Add(n + front[i]);
                tris.Add(n + front[i + 2]);
                tris.Add(n + front[i + 1]);
            }
            // Side walls join each open edge a->b to its copy on the back shell
            foreach (var e in open)
            {
                if (e.sub != s) continue;
                tris.Add(e.b); tris.Add(e.a); tris.Add(n + e.a);
                tris.Add(e.b); tris.Add(n + e.a); tris.Add(n + e.b);
            }
            result.SetTriangles(tris, s, false);
        }

        result.RecalculateBounds();
        if (mesh.uv.Length == n) result.RecalculateTangents();
        return result;
    }

    static T[] Doubled<T>(T[] values, int n)
    {
        if (values == null || values.Length != n) return new T[0];
        var result = new T[n * 2];
        values.CopyTo(result, 0);
        values.CopyTo(result, n);
        return result;
    }
}
