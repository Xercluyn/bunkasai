using System;
using System.Collections.Generic;
using UnityEngine;

public enum DoorKind { Plain, Deliver, Trap }

/// <summary>
/// Builds the newspaper-delivery course from the Game3dku models: the start helipad, a row of apartment
/// hallway pieces with plain (yellow), delivery (green) and trap (red) doors, and the end helipad.
/// Everything is measured from the meshes' own parts (walk path, door, asphalt, railing) instead of
/// hard-coded sizes, and scaled so a door is DoorHeight tall. The course runs along -X with the doors on
/// one side of the walk path; the camera watches from the other side.
/// </summary>
public class ShinbunLevel
{
    public const float DoorHeight = 2.2f;
    const float BuildingDepth = 150f;   // how far the buildings reach down, past the skyline's city ground

    public class Door
    {
        public DoorKind kind;
        public Vector3 center;     // middle of the door leaf
        public bool resolved;      // thrown at, missed or passed
    }

    public Transform root;
    public float scale;                       // applied to every model
    public float doorFrameHeight = DoorHeight; // the whole coloured door: leaf plus frame
    public readonly Vector3 runDir = Vector3.left;
    public Vector3 cameraSide;                // from the walk path toward the camera (away from the doors)
    public Vector3 startPad, endPad;          // middle of each helipad's landing surface
    public readonly List<Door> doors = new List<Door>();

    static readonly Color Green = UiKit.Good, Red = UiKit.Bad, Yellow = UiKit.Hex("#f2c232");   // yellow: plain rooms, named so on the tutorial card

    public static ShinbunLevel Build(ShinbunGame game, IList<DoorKind> layout)
    {
        var level = new ShinbunLevel { root = new GameObject("Level").transform };
        level.scale = MeasureScale(game.hallwayPlain);

        // Hallway pieces in a row toward -X, walk path on y = 0 and z = 0, starting at x = 0
        float cursor = 0f;
        Bounds course = default;
        var tinted = new Dictionary<(Material, Color), Material>();
        for (int i = 0; i < layout.Count; i++)
        {
            var prefab = layout[i] == DoorKind.Deliver ? game.hallwayDeliver
                       : layout[i] == DoorKind.Trap ? game.hallwayTrap : game.hallwayPlain;
            var piece = level.Spawn(prefab, $"Hallway {i + 1} ({layout[i]})");
            if (!PartBounds(piece, IsWalkPath, out Bounds walk)) walk = BoundsOf(piece);
            Bounds b = BoundsOf(piece);
            piece.transform.position += new Vector3(cursor - b.max.x, -walk.max.y, -walk.center.z);
            b = BoundsOf(piece);
            cursor = b.min.x;
            if (i == 0) course = b; else course.Encapsulate(b);

            if (PartBounds(piece, IsDoorPart, out Bounds frame) && (i == 0 || frame.size.y > level.doorFrameHeight))
                level.doorFrameHeight = frame.size.y;
            if (PartBounds(piece, IsDoorLeaf, out Bounds door))
            {
                level.doors.Add(new Door { kind = layout[i], center = door.center });
                if (i == 0) level.cameraSide = new Vector3(0f, 0f, door.center.z > 0f ? -1f : 1f);
            }
            Tint(piece, layout[i], tinted);
        }
        if (level.cameraSide == Vector3.zero) level.cameraSide = Vector3.forward;

        // Helipads at each end, railing on the far side, landing surface level with the walk path
        level.startPad = level.PlacePad(game.padStart, "Start Helipad", 0f, +1f, game.buildingMaterial);
        level.endPad = level.PlacePad(game.padEnd, "End Helipad", cursor, -1f, game.buildingMaterial);

        level.AddBuilding(course, game.buildingMaterial);
        return level;
    }

    // Scale that makes the plain hallway's door DoorHeight tall
    static float MeasureScale(GameObject hallway)
    {
        var probe = UnityEngine.Object.Instantiate(hallway);
        float h = PartBounds(probe, IsDoorLeaf, out Bounds door) ? door.size.y : BoundsOf(probe).size.y * 0.36f;
        UnityEngine.Object.DestroyImmediate(probe);
        return h > 1e-6f ? DoorHeight / h : 1f;
    }

    GameObject Spawn(GameObject prefab, string name)
    {
        var go = UnityEngine.Object.Instantiate(prefab, root);
        go.name = name;
        go.transform.localScale *= scale;
        return go;
    }

    // side = +1: the pad sits on the +X side of edgeX (start), -1: on the -X side (end)
    Vector3 PlacePad(GameObject prefab, string name, float edgeX, float side, Material material)
    {
        var pad = Spawn(prefab, name);
        if (!PartBounds(pad, n => n.Contains("asphalt"), out Bounds surface)) surface = BoundsOf(pad);
        // The railing must face away from the hallway
        if (PartBounds(pad, n => n == "wall", out Bounds rail) && Mathf.Sign(rail.center.x - surface.center.x) != side)
        {
            pad.transform.RotateAround(surface.center, Vector3.up, 180f);
            PartBounds(pad, n => n.Contains("asphalt"), out surface);
        }
        Bounds b = BoundsOf(pad);
        float x = side > 0f ? edgeX - b.min.x : edgeX - b.max.x;
        pad.transform.position += new Vector3(x, -surface.max.y, -surface.center.z);
        PartBounds(pad, n => n.Contains("asphalt"), out surface);

        // The helipad tower is short; carry it on down to the ground
        b = BoundsOf(pad);
        Block(name + " (tower)", material, b.min.x, b.max.x, -BuildingDepth, b.min.y + 0.5f, b.min.z, b.max.z);
        return new Vector3(surface.center.x, surface.max.y, surface.center.z);
    }

    // Apartment block under and behind the hallways, so the walkway isn't floating in the sky
    void AddBuilding(Bounds course, Material material)
    {
        Vector3 back = -cameraSide;
        float depth = course.size.z * 3f;
        float frontZ = course.center.z + back.z * course.extents.z;
        Block("Building (below)", material, course.min.x, course.max.x, -BuildingDepth, course.min.y,
              course.center.z - course.extents.z, course.center.z + course.extents.z);
        float z0 = frontZ, z1 = frontZ + back.z * depth;
        Block("Building (behind)", material, course.min.x, course.max.x, -BuildingDepth, course.max.y,
              Mathf.Min(z0, z1), Mathf.Max(z0, z1));
    }

    void Block(string name, Material material, float x0, float x1, float y0, float y1, float z0, float z1)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        UnityEngine.Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(root, false);
        go.transform.position = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f);
        go.transform.localScale = new Vector3(x1 - x0, y1 - y0, z1 - z0);
        if (material != null) go.GetComponent<Renderer>().sharedMaterial = material;
    }

    // Colour each door (leaf and frame) by what it means, so the three kinds are easy to tell apart
    static void Tint(GameObject piece, DoorKind kind, Dictionary<(Material, Color), Material> cache)
    {
        Color color = kind == DoorKind.Deliver ? Green : kind == DoorKind.Trap ? Red : Yellow;
        foreach (var r in piece.GetComponentsInChildren<MeshRenderer>())
        {
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                string n = PartName(mats[i]);
                if (n != "door" && n != "red" && n != "yellow") continue;
                Color c = n == "door" ? color * 0.8f : color;   // leaf a little darker than its frame
                c.a = 1f;
                if (!cache.TryGetValue((mats[i], c), out var tinted))
                {
                    tinted = new Material(mats[i]) { name = mats[i].name + " " + kind };
                    if (tinted.HasProperty("_BaseColor")) tinted.SetColor("_BaseColor", c);
                    if (tinted.HasProperty("_Color")) tinted.SetColor("_Color", c);
                    if (tinted.HasProperty("_BaseMap")) tinted.SetTexture("_BaseMap", null);
                    cache[(mats[i], c)] = tinted;
                }
                mats[i] = tinted;
                changed = true;
            }
            if (changed) r.sharedMaterials = mats;
        }
    }

    // ---------- the plane ----------

    /// <summary>
    /// The transport model is not exported level; turn it so its nose points along flight and its tail fin up.
    /// The nose is the end with the cockpit glass; the fin side sticks out further from the cockpit than the belly.
    /// </summary>
    public static void OrientPlane(Transform plane, Vector3 flight, Vector3 extraEuler)
    {
        Bounds all = BoundsOf(plane.gameObject);
        if (!PartBounds(plane.gameObject, n => n.Contains("glass"), out Bounds glass)) glass = all;

        Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
        Vector3 off = glass.center - all.center;
        int f = 0;
        for (int i = 1; i < 3; i++) if (Mathf.Abs(off[i]) > Mathf.Abs(off[f])) f = i;
        Vector3 nose = axes[f] * Mathf.Sign(off[f]);

        int a = (f + 1) % 3, b = (f + 2) % 3;
        int h = all.size[a] < all.size[b] ? a : b;   // wings are the widest; the shorter axis is height
        float toTop = all.max[h] - glass.center[h], toBottom = glass.center[h] - all.min[h];
        Vector3 up = axes[h] * (toTop > toBottom ? 1f : -1f);

        Quaternion fix = Quaternion.LookRotation(flight, Vector3.up) * Quaternion.Inverse(Quaternion.LookRotation(nose, up));
        plane.rotation = Quaternion.Euler(extraEuler) * fix * plane.rotation;
    }

    // ---------- measuring ----------

    static bool IsWalkPath(string n) => n.Contains("walkpath");
    static bool IsDoorLeaf(string n) => n == "door";
    static bool IsDoorPart(string n) => n == "door" || n == "red" || n == "yellow";

    /// <summary>Material name without Unity's " (Instance)" suffix, lower case.</summary>
    static string PartName(Material m) => m.name.Replace(" (Instance)", "").Trim().ToLowerInvariant();

    public static Bounds BoundsOf(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        Bounds b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        return b;
    }

    /// <summary>World bounds of the sub-meshes whose material name matches, e.g. a hallway's "door".</summary>
    public static bool PartBounds(GameObject go, Func<string, bool> match, out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
        {
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            var mats = r.sharedMaterials;
            var m = r.transform.localToWorldMatrix;
            for (int i = 0; i < mf.sharedMesh.subMeshCount && i < mats.Length; i++)
            {
                if (mats[i] == null || !match(PartName(mats[i]))) continue;
                Bounds local = mf.sharedMesh.GetSubMesh(i).bounds;
                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = local.center + Vector3.Scale(local.extents,
                        new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    Vector3 w = m.MultiplyPoint3x4(corner);
                    if (!found) { bounds = new Bounds(w, Vector3.zero); found = true; }
                    else bounds.Encapsulate(w);
                }
            }
        }
        return found;
    }
}
