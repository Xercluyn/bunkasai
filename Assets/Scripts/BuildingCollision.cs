using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Gives every mesh under this object a collider that blocks from both sides, so walls and floors
/// whose faces point the "wrong" way (a common export issue) still stop the player and its ground check.
/// Put it on the building's root (Tools > Elevator Game > Setup Building); the model needs
/// Read/Write enabled so its meshes can be read at runtime.
/// </summary>
public class BuildingCollision : MonoBehaviour
{
    static readonly Dictionary<Mesh, Mesh> doubleSided = new Dictionary<Mesh, Mesh>();

    void Awake()
    {
        foreach (var mf in GetComponentsInChildren<MeshFilter>(true))
            Apply(mf.gameObject, mf.sharedMesh);

        foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.sharedMesh == null) continue;
            var baked = new Mesh { name = smr.sharedMesh.name };
            smr.BakeMesh(baked, false);   // unscaled: the collider applies the transform's scale itself
            Apply(smr.gameObject, baked);
        }
    }

    static void Apply(GameObject go, Mesh mesh)
    {
        if (mesh == null) return;
        if (!mesh.isReadable)
        {
            Debug.LogWarning($"BuildingCollision: can't read mesh '{mesh.name}'. Turn on Read/Write in its model's import settings.", go);
            return;
        }

        var col = go.GetComponent<MeshCollider>();
        if (col == null) col = go.AddComponent<MeshCollider>();
        col.convex = false;
        col.isTrigger = false;
        col.enabled = true;
        col.sharedMesh = DoubleSided(mesh);
    }

    // Copy of the mesh with every triangle also added facing the other way
    static Mesh DoubleSided(Mesh mesh)
    {
        if (WallThickness.IsDoubleSided(mesh)) return mesh;   // Make Walls Double-Sided already did it
        if (doubleSided.TryGetValue(mesh, out var cached) && cached != null) return cached;

        var tris = new List<int>();
        for (int s = 0; s < mesh.subMeshCount; s++)
            if (mesh.GetTopology(s) == MeshTopology.Triangles) tris.AddRange(mesh.GetTriangles(s));

        int n = tris.Count;
        for (int i = 0; i < n; i += 3)
        {
            tris.Add(tris[i]);
            tris.Add(tris[i + 2]);
            tris.Add(tris[i + 1]);
        }

        var result = new Mesh { name = mesh.name + " (collision)" };
        if (mesh.vertexCount > 65535) result.indexFormat = IndexFormat.UInt32;
        result.vertices = mesh.vertices;
        result.SetTriangles(tris, 0);
        result.RecalculateBounds();

        doubleSided[mesh] = result;
        return result;
    }
}
