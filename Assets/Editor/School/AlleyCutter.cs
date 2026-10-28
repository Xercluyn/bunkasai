using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > Elevator Game > School > Cut 5F Alley.
/// Opens an alley from the 5F corridor, just east of the elevator hall, to the hallway behind the elevators. That
/// hallway (x −171.15..−150.29, running north from z −20.39, door.035 on its east wall) dead-ends 25 units short of
/// the corridor; between them is a closed pocket (x −171.15..−127.59, z −45.83..−20.39, floor and ceiling but no
/// way in). Two wall panels of the same width are cut: the corridor's north wall (z −45.83) and the hallway's end
/// wall (z −20.39), so the pocket becomes the alley; the pocket's east half is walled off (x −150.29, in line with
/// the hallway's east wall) so the alley runs straight. Every wall triangle of the shell (Plane.002, dressed or not)
/// whose centroid lies in a hole box goes, both windings; whatever of the removed faces lies outside the hole (a
/// lintel when a hole is lower than the wall) is put back double-sided with ShellDresser's box-projected wall UVs.
/// The copy is saved as "(base) alley (2s)" next to the source and also given to the shell's MeshCollider; at
/// runtime BuildingCollision uses the "(2s)" mesh as it is, so the player walks through. Separate building parts
/// standing in a hole are hidden. Running again finds nothing left to cut and changes nothing; run it after Dress
/// Floor Surfaces.
/// </summary>
public static class AlleyCutter
{
    const string Title = "Cut 5F Alley";
    const string GeneratedFolder = "Assets/Generated";
    const string AlleyTag = " alley";
    const string ShellName = "Plane.002";
    const int AlleyFloor = 5;
    const float WallFace = 0.3f;   // |world normal.y| below this is a wall face
    const int WallSubmesh = 1;     // ShellDresser's floor / wall / ceiling layout

    // The holes, world space: each wall plane ±0.5, x inset a little from the corners (−171.15) and pillars
    // (Cube.053 / Cube.016 at −150.29) so none is touched, floor to the top of the wall (20.14)
    static readonly Bounds[] Holes =
    {
        Box(new Vector3(-171.0f, -0.5f, -46.33f), new Vector3(-150.45f, 20.6f, -45.33f)),   // corridor's north wall
        Box(new Vector3(-171.0f, -0.5f, -20.89f), new Vector3(-150.45f, 20.6f, -19.89f)),   // hallway's end wall
    };

    // Walls added, double-sided (world rectangles, zero thickness across x): the pocket's east half would be an
    // alcove off the alley, so it's closed off in line with the hallway's east wall and the two pillars, leaving a
    // straight alley
    static readonly Bounds[] Closers =
    {
        Box(new Vector3(-150.29f, 0f, -45.83f), new Vector3(-150.29f, 20.14f, -20.39f)),
    };

    [MenuItem("Tools/Elevator Game/School/Cut 5F Alley")]
    static void CutOpen() => SchoolKit.OnOpenFloor(Title, Apply);

    /// <summary>Cuts the alley into floor 5's shell; nothing for other floors or an already cut shell.</summary>
    internal static void Apply(Scene scene, int floor)
    {
        if (floor != AlleyFloor) return;
        var look = SchoolPalette.For(floor);
        float tile = (look != null ? look.wall.tileMetres : 1f) * SchoolPalette.WorldPerMetre;
        var roots = BuildingRoots(scene).ToList();
        var shells = roots.SelectMany(r => r.GetComponentsInChildren<MeshFilter>(true))
            .Where(mf => mf.sharedMesh != null && mf.sharedMesh.name.StartsWith(ShellName)).ToList();
        if (shells.Count == 0) { Debug.LogWarning($"{Title}: no '{ShellName}' shell in {scene.name}."); return; }

        foreach (var hole in Holes) Debug.Log($"{Title}: hole box min {hole.min} max {hole.max} (world).");
        foreach (var mf in shells) Cut(mf, scene, tile);
        HideInHole(roots, shells);
        AssetDatabase.SaveAssets();
    }

    static IEnumerable<GameObject> BuildingRoots(Scene scene)
    {
        var character = FloorPlayerSetup.FindCharacter(scene);
        return FloorPlayerSetup.BuildingRoots(scene, character != null ? character.transform : null);
    }

    // ---------- cutting ----------

    // True when the mesh already has a wall face in the plane of closing wall `wall` (added by an earlier run)
    static bool HasWall(Mesh mesh, Matrix4x4 l2w, Bounds wall)
    {
        var near = wall;
        near.Expand(new Vector3(0.6f, -1f, -1f));   // ±0.3 across the plane, a little inside its edges
        var verts = mesh.vertices;
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            if (mesh.GetTopology(s) != MeshTopology.Triangles) continue;
            var tris = mesh.GetTriangles(s);
            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 a = l2w.MultiplyPoint3x4(verts[tris[i]]), b = l2w.MultiplyPoint3x4(verts[tris[i + 1]]),
                        c = l2w.MultiplyPoint3x4(verts[tris[i + 2]]);
                var n = Vector3.Cross(b - a, c - a);
                if (n.sqrMagnitude > 0f && Mathf.Abs(n.normalized.x) > 0.9f && near.Contains((a + b + c) / 3f)) return true;
            }
        }
        return false;
    }

    // A thin "hole" just past the far end of `wall` along z, so Patch fills in the whole of `wall` as its left strip
    static Bounds OutsideOf(Bounds wall) =>
        new Bounds(new Vector3(wall.center.x, wall.center.y, wall.max.z + 10f), new Vector3(0.01f, wall.size.y, 1f));

    static int HoleOf(Vector3 p)
    {
        for (int h = 0; h < Holes.Length; h++)
            if (Holes[h].Contains(p)) return h;
        return -1;
    }

    static void Cut(MeshFilter mf, Scene scene, float tile)
    {
        var source = mf.sharedMesh;
        if (!source.isReadable) { Debug.LogWarning($"{Title}: can't read mesh '{source.name}'. Turn on Read/Write in its model's import settings.", mf); return; }

        var l2w = mf.transform.localToWorldMatrix;
        var verts = source.vertices;
        var kept = new List<int>[source.subMeshCount];
        int removed = 0, removedSub = -1;
        var cutAreas = new Bounds?[Holes.Length];   // per hole, the world bounds of the faces removed
        for (int s = 0; s < source.subMeshCount; s++)
        {
            kept[s] = new List<int>();
            if (source.GetTopology(s) != MeshTopology.Triangles) continue;
            var tris = source.GetTriangles(s);
            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 a = l2w.MultiplyPoint3x4(verts[tris[i]]), b = l2w.MultiplyPoint3x4(verts[tris[i + 1]]),
                        c = l2w.MultiplyPoint3x4(verts[tris[i + 2]]);
                var n = Vector3.Cross(b - a, c - a);
                bool wall = n.sqrMagnitude > 0f && Mathf.Abs(n.normalized.y) < WallFace;
                int h = wall ? HoleOf((a + b + c) / 3f) : -1;
                if (h >= 0)
                {
                    removed++;
                    if (removedSub < 0) removedSub = s;
                    var area = cutAreas[h] ?? new Bounds(a, Vector3.zero);
                    area.Encapsulate(a); area.Encapsulate(b); area.Encapsulate(c);
                    cutAreas[h] = area;
                    continue;
                }
                kept[s].Add(tris[i]); kept[s].Add(tris[i + 1]); kept[s].Add(tris[i + 2]);
            }
        }
        var missing = Closers.Where(c => !HasWall(source, l2w, c)).ToList();
        // Nothing in the holes and every closing wall there: already done, so the mesh stays as it is
        if (removed == 0 && missing.Count == 0) { Debug.Log($"{Title}: '{source.name}' is already cut and closed; nothing to do.", mf); return; }

        var cut = Object.Instantiate(source);
        cut.name = AlleyName(source.name);
        int sub = source.subMeshCount > WallSubmesh ? WallSubmesh : Mathf.Max(0, removedSub);
        int patches = 0;
        var w2l = mf.transform.worldToLocalMatrix;
        for (int h = 0; h < Holes.Length; h++)
            if (cutAreas[h] != null) patches += Patch(cut, kept, sub, Holes[h], cutAreas[h].Value, w2l, tile);
        foreach (var wall in missing) patches += Patch(cut, kept, sub, OutsideOf(wall), wall, w2l, tile);
        for (int s = 0; s < kept.Length; s++) cut.SetTriangles(kept[s], s, false);
        cut.RecalculateBounds();
        if (patches > 0 && cut.tangents.Length > 0) cut.RecalculateTangents();

        var saved = Save(cut, FolderFor(source, scene));
        Undo.RecordObject(mf, Title);
        mf.sharedMesh = saved;
        var col = mf.GetComponent<MeshCollider>();
        if (col != null)
        {
            Undo.RecordObject(col, Title);
            col.sharedMesh = saved;
        }
        int before = 0, after = 0;
        for (int s = 0; s < source.subMeshCount; s++) before += (int)source.GetIndexCount(s) / 3;
        for (int s = 0; s < saved.subMeshCount; s++) after += (int)saved.GetIndexCount(s) / 3;
        string spans = string.Join(", ", cutAreas.Select((a, h) => a == null ? $"hole {h}: none" : $"hole {h}: {a.Value.min}..{a.Value.max}"));
        Debug.Log($"{Title}: cut {removed} triangle(s) from '{source.name}' ({spans}), " +
                  $"added {patches} patch triangle(s); {before} -> {after} triangle(s). Saved '{saved.name}'" +
                  (col != null ? ", collider updated." : "."), mf);
    }

    /// <summary>
    /// Puts back, double-sided, the parts of the removed faces' rectangle (`area`, world) that lie outside `hole`:
    /// the strips left, right, below and above it in the wall plane. Appends vertices to `mesh` and triangles to
    /// kept[sub]; returns the triangles added.
    /// </summary>
    static int Patch(Mesh mesh, List<int>[] kept, int sub, Bounds hole, Bounds area, Matrix4x4 w2l, float tile)
    {
        // The wall plane is across the hole's thinnest axis; u is the other horizontal axis
        int normalAxis = hole.size.x < hole.size.z ? 0 : 2, uAxis = 2 - normalAxis;
        float plane = area.center[normalAxis];
        float u0 = area.min[uAxis], u1 = area.max[uAxis], v0 = area.min.y, v1 = area.max.y;
        float h0 = Mathf.Clamp(hole.min[uAxis], u0, u1), h1 = Mathf.Clamp(hole.max[uAxis], u0, u1);
        float t0 = Mathf.Clamp(hole.min.y, v0, v1), t1 = Mathf.Clamp(hole.max.y, v0, v1);
        var strips = new[]
        {
            (u0, h0, v0, v1), (h1, u1, v0, v1),   // left and right, full height
            (h0, h1, v0, t0), (h0, h1, t1, v1),   // below and above the hole
        };

        var verts = new List<Vector3>(); mesh.GetVertices(verts);
        var normals = new List<Vector3>(); mesh.GetNormals(normals);
        var uvs = new List<Vector2>(); mesh.GetUVs(0, uvs);
        bool hasNormals = normals.Count == verts.Count, hasUv = uvs.Count == verts.Count;
        int start = verts.Count, added = 0;
        const float Min = 0.5f;   // thinner strips are the box's inset from the corner and pillar: hidden in them anyway
        foreach (var (a0, a1, b0, b1) in strips)
        {
            if (a1 - a0 < Min || b1 - b0 < Min) continue;
            var corners = new Vector3[4];
            for (int k = 0; k < 4; k++)
            {
                var p = Vector3.zero;
                p[normalAxis] = plane;
                p[uAxis] = k == 0 || k == 3 ? a0 : a1;
                p.y = k < 2 ? b0 : b1;
                corners[k] = p;
            }
            var worldNormal = Vector3.zero;
            worldNormal[normalAxis] = 1f;
            foreach (int side in new[] { 1, -1 })
            {
                int baseIndex = verts.Count;
                var localNormal = w2l.inverse.transpose.MultiplyVector(worldNormal * side).normalized;
                foreach (var p in corners)
                {
                    verts.Add(w2l.MultiplyPoint3x4(p));
                    if (hasNormals) normals.Add(localNormal);
                    if (hasUv) uvs.Add(new Vector2(p[uAxis], p.y) / tile);   // ShellDresser.Project for a wall face
                }
                // Wind each copy to face its own way in world space
                var cross = Vector3.Cross(corners[1] - corners[0], corners[2] - corners[0]);
                bool flip = Vector3.Dot(cross, worldNormal * side) < 0f;
                int[] quad = flip ? new[] { 0, 2, 1, 0, 3, 2 } : new[] { 0, 1, 2, 0, 2, 3 };
                foreach (int q in quad) kept[sub].Add(baseIndex + q);
                added += 2;
            }
        }
        if (verts.Count == start) return 0;

        // Colours and lightmap UVs are padded by copying the first vertex, so the arrays stay aligned (tangents are
        // recalculated by the caller)
        var colors = new List<Color>(); mesh.GetColors(colors);
        var uv2 = new List<Vector2>(); mesh.GetUVs(1, uv2);
        if (verts.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(verts);
        if (hasNormals) mesh.SetNormals(normals);
        if (hasUv) mesh.SetUVs(0, uvs);
        if (colors.Count == start) { colors.AddRange(Enumerable.Repeat(colors[0], verts.Count - start)); mesh.SetColors(colors); }
        if (uv2.Count == start) { uv2.AddRange(Enumerable.Repeat(uv2[0], verts.Count - start)); mesh.SetUVs(1, uv2); }
        return added;
    }

    // ---------- separate parts in the hole ----------

    // Hides building parts (pillars, ceiling bits, furniture from the model) whose centre lies in a hole
    static void HideInHole(List<GameObject> roots, List<MeshFilter> shells)
    {
        var skip = new HashSet<GameObject>(shells.Select(mf => mf.gameObject));
        int hidden = 0;
        foreach (var r in roots.SelectMany(root => root.GetComponentsInChildren<Renderer>(true)))
        {
            var go = r.gameObject;
            if (skip.Contains(go) || !go.activeSelf || HoleOf(r.bounds.center) < 0) continue;
            Undo.RecordObject(go, Title);
            go.SetActive(false);
            PrefabUtility.RecordPrefabInstancePropertyModifications(go);
            Debug.Log($"{Title}: hid '{go.name}' (it stood in a hole).", go);
            hidden++;
        }
        Debug.Log($"{Title}: {hidden} separate part(s) hidden in the holes.");
    }

    // ---------- assets ----------

    // "(base) alley (2s)", the base stripped of an earlier alley tag and the double-sided suffix
    static string AlleyName(string name)
    {
        string baseName = name;
        string[] tails = { " " + WallThickness.DoubleSidedSuffix, WallThickness.DoubleSidedSuffix, AlleyTag };
        for (bool cut = true; cut;)
        {
            cut = false;
            foreach (var tail in tails)
                if (baseName.EndsWith(tail)) { baseName = baseName.Substring(0, baseName.Length - tail.Length); cut = true; }
        }
        return baseName.Trim() + AlleyTag + " " + WallThickness.DoubleSidedSuffix;
    }

    // Next to a generated source; a raw model mesh goes to Assets/Generated/(scene)
    static string FolderFor(Mesh source, Scene scene)
    {
        string path = AssetDatabase.GetAssetPath(source);
        return path.StartsWith(GeneratedFolder + "/") ? Path.GetDirectoryName(path).Replace('\\', '/') : $"{GeneratedFolder}/{scene.name}";
    }

    // Saves the mesh as an asset, overwriting an earlier cut in place so references to it stay valid
    static Mesh Save(Mesh mesh, string folder)
    {
        SchoolKit.EnsureFolder(folder);
        string path = $"{folder}/{string.Concat(mesh.name.Split(Path.GetInvalidFileNameChars()))}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
        EditorUtility.CopySerialized(mesh, existing);
        Object.DestroyImmediate(mesh);
        EditorUtility.SetDirty(existing);
        return existing;
    }

    static Bounds Box(Vector3 min, Vector3 max)
    {
        var b = new Bounds();
        b.SetMinMax(min, max);
        return b;
    }
}
