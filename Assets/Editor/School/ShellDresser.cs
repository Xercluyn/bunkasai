using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > Elevator Game > School > Steps > Dress Floor Surfaces.
/// Gives the floor's shell (the one mesh holding floor + walls), its ceilings and its pillars the floor's school
/// materials (SchoolPalette). Each triangle is sorted by its world-space facing into floor / wall / ceiling
/// submeshes and gets UVs box-projected from world positions in real metres per texture repeat, so tiles keep
/// their real size however the part is scaled. The copies are double-sided and saved in Assets/Generated/(scene)
/// as "(name) dressed (2s)"; running again rebuilds them in place. Colliders keep their mesh.
/// </summary>
public static class ShellDresser
{
    const string Title = "Dress Floor Surfaces";
    const string DressedTag = " dressed";
    const float M = SchoolPalette.WorldPerMetre;
    const float Cover = 0.5f;   // share of the building's X and Z a shell or ceiling spans
    const float Flat = 0.7f;    // |world normal.y| above this is a floor or ceiling face
    static readonly Regex PillarName = new Regex(@"^Cube\.\d+$");

    enum Kind { None, Shell, Ceiling, Pillar }

    // What one run needs: the look, the materials and the meshes made so far
    sealed class Job
    {
        public FloorLook look;
        public string folder;
        public Material[] shellMats;
        public Material ceilingMat, pillarMat;
        public readonly Dictionary<(Mesh, Vector3), Mesh> pillars = new Dictionary<(Mesh, Vector3), Mesh>();
        public readonly HashSet<string> names = new HashSet<string>();
    }

    [MenuItem("Tools/Elevator Game/School/Steps/Dress Floor Surfaces", false, 110)]
    static void DressOpen() => EditorKit.OnOpenFloor(Title, Apply);

    [MenuItem("Tools/Elevator Game/School/Steps/Dress Floor Surfaces (All Floors)", false, 111)]
    static void DressAll() => EditorKit.ForFloors(Title, 1, 5, Apply);

    /// <summary>Dresses the shell, ceilings and pillars of `scene`, which is floor `floor`.</summary>
    internal static void Apply(Scene scene, int floor)
    {
        var look = SchoolPalette.For(floor);
        if (look == null) return;
        var filters = Candidates(scene);
        if (filters.Count == 0) { Debug.LogWarning($"{Title}: no building meshes found in {scene.name}."); return; }
        var building = Union(filters);
        var job = new Job
        {
            look = look,
            folder = $"{EditorKit.GeneratedFolder}/{scene.name}",
            shellMats = new[]
            {
                SchoolKit.Mat($"F{floor}_floor", look.floor), SchoolKit.Mat($"F{floor}_wall", look.wall),
                SchoolKit.Mat($"F{floor}_ceiling", look.ceiling),
            },
            ceilingMat = SchoolKit.Mat($"F{floor}_ceiling", look.ceiling),
            pillarMat = SchoolKit.Mat($"F{floor}_pillar", look.pillar),
        };

        var counts = new Dictionary<Kind, int> { [Kind.Shell] = 0, [Kind.Ceiling] = 0, [Kind.Pillar] = 0 };
        long triangles = 0;
        try
        {
            for (int i = 0; i < filters.Count; i++)
            {
                var mf = filters[i];
                EditorUtility.DisplayProgressBar(Title, mf.name, (float)i / filters.Count);
                var kind = Classify(mf, building, floor);
                if (kind == Kind.None) continue;
                if (!mf.sharedMesh.isReadable)
                {
                    Debug.LogWarning($"{Title}: can't read mesh '{mf.sharedMesh.name}'. Turn on Read/Write in its model's import settings.", mf);
                    continue;
                }
                triangles += Dress(mf, kind, job);
                counts[kind]++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"{scene.name}: dressed {counts[Kind.Shell]} shell(s), {counts[Kind.Ceiling]} ceiling(s) and " +
                  $"{counts[Kind.Pillar]} pillar(s) for floor {floor} ({triangles} triangle(s) drawn). Meshes are in {job.folder}.");
    }

    // ---------- finding the parts ----------

    // Mesh parts of the building from a model or earlier fixes, minus outlined parts and play zones
    static List<MeshFilter> Candidates(Scene scene)
    {
        var outlined = EditorKit.OutlinedFilters(scene);
        return EditorKit.BuildingRoots(scene)
            .SelectMany(r => r.GetComponentsInChildren<MeshFilter>(true))
            .Where(mf => mf.sharedMesh != null && mf.GetComponent<MeshRenderer>() != null && !outlined.Contains(mf) &&
                         mf.GetComponentInParent<ElevatorPlayZone>(true) == null && EditorKit.IsModelMesh(mf.sharedMesh))
            .ToList();
    }

    // World bounds of the visible parts
    static Bounds Union(List<MeshFilter> filters)
    {
        Bounds? all = null;
        foreach (var mf in filters)
        {
            var r = mf.GetComponent<MeshRenderer>();
            if (!r.enabled || !mf.gameObject.activeInHierarchy) continue;
            if (all == null) all = r.bounds;
            else { var b = all.Value; b.Encapsulate(r.bounds); all = b; }
        }
        return all ?? new Bounds();
    }

    // Ceilings by name (1F's roof plane is Cube.001), then anything spanning the building: with side faces it's the
    // shell, flat ones are a ceiling in the upper half and a floor below. Small Cube.NNN boxes are pillars.
    static Kind Classify(MeshFilter mf, Bounds building, int floor)
    {
        string name = mf.name;
        if (name.StartsWith("Ceiling", StringComparison.OrdinalIgnoreCase) || (floor == 1 && name == "Cube.001")) return Kind.Ceiling;
        var b = mf.GetComponent<MeshRenderer>().bounds;
        if (b.size.x >= Cover * building.size.x && b.size.z >= Cover * building.size.z && mf.sharedMesh.isReadable)
        {
            if (HasSideFaces(mf)) return Kind.Shell;
            return b.center.y > building.center.y ? Kind.Ceiling : Kind.Shell;
        }
        return PillarName.IsMatch(name) ? Kind.Pillar : Kind.None;
    }

    static bool HasSideFaces(MeshFilter mf)
    {
        var mesh = mf.sharedMesh;
        var verts = mesh.vertices;
        var normalSpace = mf.transform.localToWorldMatrix.inverse.transpose;
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            if (mesh.GetTopology(s) != MeshTopology.Triangles) continue;
            var tris = mesh.GetTriangles(s);
            for (int i = 0; i < tris.Length; i += 3)
            {
                var n = FaceNormal(verts[tris[i]], verts[tris[i + 1]], verts[tris[i + 2]], normalSpace);
                if (n != Vector3.zero && Mathf.Abs(n.y) <= Flat) return true;
            }
        }
        return false;
    }

    // ---------- dressing ----------

    // Swaps in the dressed mesh and materials; returns the dressed triangle count
    static int Dress(MeshFilter mf, Kind kind, Job job)
    {
        Mesh dressed;
        Material[] mats;
        var l2w = mf.transform.localToWorldMatrix;
        switch (kind)
        {
            case Kind.Shell:
                var tiles = new[] { job.look.floor.tileMetres * M, job.look.wall.tileMetres * M, job.look.ceiling.tileMetres * M };
                dressed = Save(Build(mf.sharedMesh, l2w, l2w.inverse.transpose, ShellSubmesh, tiles, Name(mf.sharedMesh, job)), job.folder);
                mats = job.shellMats;
                break;
            case Kind.Ceiling:
                var ceiling = new[] { job.look.ceiling.tileMetres * M };
                dressed = Save(Build(mf.sharedMesh, l2w, l2w.inverse.transpose, _ => 0, ceiling, Name(mf.sharedMesh, job)), job.folder);
                mats = new[] { job.ceilingMat };
                break;
            default:
                dressed = Pillar(mf, job);
                mats = new[] { job.pillarMat };
                break;
        }
        Undo.RecordObject(mf, Title);
        mf.sharedMesh = dressed;
        var renderer = mf.GetComponent<MeshRenderer>();
        Undo.RecordObject(renderer, Title);
        renderer.sharedMaterials = mats;
        int count = 0;
        for (int s = 0; s < dressed.subMeshCount; s++) count += (int)dressed.GetIndexCount(s) / 3;
        return count;
    }

    static int ShellSubmesh(Vector3 n) => n.y > Flat ? 0 : n.y < -Flat ? 2 : 1;

    // Pillars share meshes: UVs come from the mesh scaled by the part's scale (not its position), so parts with the
    // same mesh and scale share one dressed copy
    static Mesh Pillar(MeshFilter mf, Job job)
    {
        var source = mf.sharedMesh;
        var s = mf.transform.lossyScale;
        var key = (source, new Vector3(Mathf.Round(s.x * 1000f), Mathf.Round(s.y * 1000f), Mathf.Round(s.z * 1000f)));
        if (job.pillars.TryGetValue(key, out var made) && made != null) return made;
        var inverse = new Vector3(1f / NonZero(s.x), 1f / NonZero(s.y), 1f / NonZero(s.z));
        var tiles = new[] { job.look.pillar.tileMetres * M };
        made = Save(Build(source, Matrix4x4.Scale(s), Matrix4x4.Scale(inverse), _ => 0, tiles, Name(source, job)), job.folder);
        job.pillars[key] = made;
        return made;
    }

    static float NonZero(float v) => Mathf.Abs(v) < 1e-6f ? (v < 0f ? -1e-6f : 1e-6f) : v;

    /// <summary>
    /// Unwelded copy of `source` (made double-sided first if it isn't): each triangle gets its own three vertices,
    /// goes to submesh `submeshOf(world normal)` and gets UVs box-projected from `space` positions divided by that
    /// submesh's repeat size in `tiles` (world units).
    /// </summary>
    static Mesh Build(Mesh source, Matrix4x4 space, Matrix4x4 normalSpace, Func<Vector3, int> submeshOf, float[] tiles, string name)
    {
        var mesh = WallThickness.IsDoubleSided(source) ? source
            : WallDoubleSider.DoubleSided(source, source.name + " " + WallThickness.DoubleSidedSuffix);
        try { return Unweld(mesh, space, normalSpace, submeshOf, tiles, name); }
        finally { if (mesh != source) UnityEngine.Object.DestroyImmediate(mesh); }
    }

    static Mesh Unweld(Mesh mesh, Matrix4x4 space, Matrix4x4 normalSpace, Func<Vector3, int> submeshOf, float[] tiles, string name)
    {
        var verts = mesh.vertices;
        var normals = mesh.normals;
        var colors = mesh.colors;
        var uv2 = new List<Vector2>();
        mesh.GetUVs(1, uv2);
        bool hasNormals = normals.Length == verts.Length, hasColors = colors.Length == verts.Length, hasUv2 = uv2.Count == verts.Length;

        var outVerts = new List<Vector3>();
        var outNormals = new List<Vector3>();
        var outUv = new List<Vector2>();
        var outUv2 = new List<Vector2>();
        var outColors = new List<Color>();
        var tris = tiles.Select(_ => new List<int>()).ToArray();
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            if (mesh.GetTopology(s) != MeshTopology.Triangles) continue;
            var idx = mesh.GetTriangles(s);
            for (int i = 0; i < idx.Length; i += 3)
            {
                var n = FaceNormal(verts[idx[i]], verts[idx[i + 1]], verts[idx[i + 2]], normalSpace);
                if (n == Vector3.zero) continue;   // degenerate: draws nothing
                var local = Vector3.Cross(verts[idx[i + 1]] - verts[idx[i]], verts[idx[i + 2]] - verts[idx[i]]).normalized;
                int sub = submeshOf(n);
                for (int k = 0; k < 3; k++)
                {
                    int v = idx[i + k];
                    tris[sub].Add(outVerts.Count);
                    outVerts.Add(verts[v]);
                    outNormals.Add(hasNormals ? normals[v] : local);
                    outUv.Add(Project(space.MultiplyPoint3x4(verts[v]), n, tiles[sub]));
                    if (hasUv2) outUv2.Add(uv2[v]);
                    if (hasColors) outColors.Add(colors[v]);
                }
            }
        }

        var result = new Mesh { name = name, indexFormat = outVerts.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        result.SetVertices(outVerts);
        result.SetNormals(outNormals);
        result.SetUVs(0, outUv);
        if (hasUv2) result.SetUVs(1, outUv2);
        if (hasColors) result.SetColors(outColors);
        result.subMeshCount = tiles.Length;
        for (int s = 0; s < tiles.Length; s++) result.SetTriangles(tris[s], s, false);
        result.RecalculateBounds();
        result.RecalculateTangents();
        return result;
    }

    // World-space face normal (inverse-transpose, so non-uniform scale is handled); doubles, since a part scaled
    // ~2500x has tiny local triangles. Zero for a degenerate triangle.
    static Vector3 FaceNormal(Vector3 a, Vector3 b, Vector3 c, Matrix4x4 normalSpace)
    {
        double ex = b.x - a.x, ey = b.y - a.y, ez = b.z - a.z, fx = c.x - a.x, fy = c.y - a.y, fz = c.z - a.z;
        double nx = ey * fz - ez * fy, ny = ez * fx - ex * fz, nz = ex * fy - ey * fx;
        double len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
        if (len <= 0.0) return Vector3.zero;
        var w = normalSpace.MultiplyVector(new Vector3((float)(nx / len), (float)(ny / len), (float)(nz / len)));
        double wl = Math.Sqrt((double)w.x * w.x + (double)w.y * w.y + (double)w.z * w.z);
        return wl <= 0.0 ? Vector3.zero : new Vector3((float)(w.x / wl), (float)(w.y / wl), (float)(w.z / wl));
    }

    // Box projection: flat faces use XZ, walls the plane across their main horizontal axis
    static Vector2 Project(Vector3 p, Vector3 n, float tile)
    {
        float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
        var uv = ay >= ax && ay >= az ? new Vector2(p.x, p.z) : ax > az ? new Vector2(p.z, p.y) : new Vector2(p.x, p.y);
        return uv / tile;
    }

    // ---------- assets ----------

    // "(base) dressed (2s)", with the base stripped of earlier tags so re-runs keep one name; numbered on a clash
    static string Name(Mesh source, Job job)
    {
        string baseName = EditorKit.StripTags(source.name, DressedTag), name = baseName;
        for (int k = 2; !job.names.Add(name); k++) name = $"{baseName} {k}";
        return name + DressedTag + " " + WallThickness.DoubleSidedSuffix;
    }

    // Saved over an earlier dressed copy in place, so references to it stay valid
    static Mesh Save(Mesh mesh, string folder) => EditorKit.SaveMesh(mesh, folder);
}
