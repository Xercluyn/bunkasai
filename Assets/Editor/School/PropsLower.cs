using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using static EditorKit;
using static SchoolKit;

/// <summary>
/// Tools > Elevator Game > School > Steps > Place School Props, floors 1–3: the 1F lobby (floor guide, salmon wall,
/// tactile strip, door mat, umbrella stand, rail, exit sign), the 2F reception + library (counter with glass,
/// carpet runner, bookshelves, reading table, sign) and the 3F architecture rooms (drafting tables + stools,
/// rolling whiteboard, PC desks, a model on a plinth, a steel cabinet). Prefabs are rebuilt on every run;
/// instances already placed stay where they are. Floors 4–5 are PropsUpper's.
/// </summary>
public static class PropsLower
{
    const string Title = "Place School Props";
    const float M = SchoolPalette.WorldPerMetre;

    /// <summary>Builds and places floor 1–3's props (no-op for other floors).</summary>
    internal static void Apply(Scene scene, int floor)
    {
        if (floor < 1 || floor > 3) return;
        var ctx = new Ctx(scene, floor);
        if (floor == 1) Floor1(ctx);
        else if (floor == 2) Floor2(ctx);
        else Floor3(ctx);
    }

    // ---------- placement ----------

    // One run on one floor: the walking floor's height and the building's centre (what props face)
    sealed class Ctx
    {
        public readonly Scene scene;
        public readonly int floor;
        public readonly float level;
        public readonly Vector3 room;

        public Ctx(Scene scene, int floor)
        {
            this.scene = scene;
            this.floor = floor;
            var b = RendererBounds(FindInBuilding(scene, "").Select(t => t.GetComponent<Renderer>()).Where(r => r != null));
            var c = b?.center ?? Vector3.zero;
            level = PlayerFloorY(scene, b?.min.y ?? 0f);
            room = new Vector3(c.x, level, c.z);
        }
    }

    // Where to look for room: a point on the floor, the yaw (90° steps) props line up with, and what it is
    sealed class Spot
    {
        public string name;
        public Vector3 center;
        public float yaw;
        public bool found;
    }

    static float Snap(float yaw) => Mathf.Round(yaw / 90f) * 90f;

    // The first building object named `prefix`…; the building's centre when there is none
    static Spot Anchor(Ctx ctx, string prefix)
    {
        var found = FindInBuilding(ctx.scene, prefix);
        if (found.Count == 0)
        {
            Debug.LogWarning($"{Title} F{ctx.floor}: no '{prefix}' in the building, using the building centre.");
            return new Spot { name = $"{prefix} (missing, building centre)", center = ctx.room, yaw = 0f, found = false };
        }
        var t = found[0];
        var c = RendererBounds(t.GetComponentsInChildren<Renderer>(true))?.center ?? t.position;
        return new Spot { name = t.name, center = new Vector3(c.x, ctx.level, c.z), yaw = Snap(t.eulerAngles.y), found = true };
    }

    // Halfway between two anchors, lined up along the way from `from` to `to`
    static Spot Between(Spot from, Spot to)
    {
        var d = to.center - from.center;
        return new Spot
        {
            name = $"{from.name} -> {to.name}", center = (from.center + to.center) * 0.5f,
            yaw = Snap(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg), found = from.found && to.found,
        };
    }

    // A FreeSpot on the walking floor (not on a table or counter top: those are skipped by searching further out)
    static Vector3? Find(Ctx ctx, Spot at, Vector3 size, float yaw, float minDist)
    {
        float max = at.found ? 60f : 120f;
        float min = minDist;
        while (min <= max)
        {
            var p = FreeSpot(ctx.scene, at.center, size, yaw, min, max);
            if (p == null) return null;
            if (p.Value.y < ctx.level + 1f) return p;
            var flat = p.Value - at.center;
            flat.y = 0f;
            min = flat.magnitude + 3f;
        }
        return null;
    }

    // `yaw` or its opposite, whichever turns the prop's front (+Z) towards `target` (same footprint either way)
    static float Face(Vector3 from, Vector3 target, float yaw)
    {
        var forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        return Vector3.Dot(forward, target - from) >= 0f ? yaw : yaw + 180f;
    }

    // Slides a prop of depth `depthM` back until its back touches a wall within `reachM` behind it
    static Vector3 BackToWall(Ctx ctx, Vector3 ground, float yaw, float depthM, float reachM = 1.5f)
    {
        var back = Quaternion.Euler(0f, yaw, 0f) * Vector3.back;
        var origin = ground + Vector3.up * M + back * (depthM * 0.5f * M);
        if (ctx.scene.GetPhysicsScene().Raycast(origin, back, out var hit, reachM * M, ~0, QueryTriggerInteraction.Ignore)
            && Mathf.Abs(hit.normal.y) < 0.3f)
            return ground + back * Mathf.Max(0f, hit.distance - 0.02f * M);
        return ground;
    }

    /// Places prefab instance `id` near `at` (kept if it's there already): footprint `size` metres lined up with
    /// the anchor (or `yaw`), front turned to the room centre; `toWall` pushes it back against a wall.
    static Transform Put(Ctx ctx, string id, GameObject prefab, Spot at, Vector3 size, float? yaw = null,
                         bool toWall = false, float minDist = 0f)
    {
        var old = PropsRoot(ctx.scene).Find(id);
        if (old != null) { Debug.Log($"{Title} F{ctx.floor}: {id} already placed, kept where it is."); return old; }
        if (IsDeleted(ctx.scene, id)) { Debug.Log($"{Title} F{ctx.floor}: {id} was deleted by hand; not placed again."); return null; }
        float y = yaw ?? at.yaw;
        var p = Find(ctx, at, size, y, minDist);
        if (p == null) { Debug.LogWarning($"{Title} F{ctx.floor}: no free spot for {id} near '{at.name}'."); return null; }
        y = Face(p.Value, ctx.room, y);
        var pos = toWall ? BackToWall(ctx, p.Value, y, size.z) : p.Value;
        var go = Place(ctx.scene, id, prefab, pos, y);
        Debug.Log($"{Title} F{ctx.floor}: placed {id} near '{at.name}' at {pos} (yaw {y % 360f:0}).", go);
        return go.transform;
    }

    // ---------- shared parts ----------

    static Material Paint(int floor, string key, string hex, float smoothness = 0.25f, float metallic = 0f) =>
        HexMat($"F{floor}_{key}", hex, smoothness, metallic);

    // A label on a face pointing +Z (TextMesh reads from -Z, so it is turned round)
    static void FrontLabel(GameObject p, string name, string text, Vector3 at, float height, Color color) =>
        Label(p, name, text, at, height, color, V(0f, 180f, 0f));

    // A box of a sub-assembly (chair, stool) standing at `at`, turned `yaw` inside the prop
    static void SubBox(GameObject p, string name, Vector3 at, float yaw, Vector3 local, Vector3 size, Material mat)
    {
        var rot = Quaternion.Euler(0f, yaw, 0f);
        Box(p, name, at + rot * local, size, mat, V(0f, yaw, 0f));
    }

    // A panel `w` x `h` with its bottom at `bottom` on a weighted base and post; returns the panel's front z
    static float SignStand(GameObject p, float w, float h, float bottom, Material panel, Material steel)
    {
        Box(p, "Base", V(0f, 0.01f, 0f), V(Mathf.Min(w, 0.5f), 0.02f, 0.35f), steel);
        Cyl(p, "Post", V(0f, 0f, -0.05f), 0.04f, bottom + h * 0.5f, steel);
        Box(p, "Panel", V(0f, bottom + h * 0.5f, 0f), V(w, h, 0.05f), panel);
        return 0.03f;
    }

    // ---------- 1F lobby ----------

    static void Floor1(Ctx ctx)
    {
        var lift = Anchor(ctx, "elevator");
        var door = Anchor(ctx, "door");
        var entrance = door.found ? door : lift;
        Put(ctx, "f1_directory", Directory(), lift, V(0.65f, 1.95f, 0.4f), toWall: true, minDist: 3f);
        Put(ctx, "f1_accent_wall", AccentWall(), lift, V(4f, 4f, 0.15f), toWall: true, minDist: 6f);
        var path = door.found ? Between(door, lift) : lift;
        Put(ctx, "f1_tactile", TactileStrip(), path, V(0.3f, 0.02f, 8f));
        Put(ctx, "f1_door_mat", DoorMat(), entrance, V(2f, 0.02f, 1.4f));
        Put(ctx, "f1_umbrella", UmbrellaStand(), entrance, V(0.55f, 1.05f, 0.35f), toWall: true, minDist: 3f);
        Put(ctx, "f1_rail", Rail(), lift, V(2.1f, 0.95f, 0.1f), minDist: 6f);
        Put(ctx, "f1_exit_sign", ExitSign(), entrance, V(0.6f, 2.55f, 0.35f), toWall: true, minDist: 3f);
    }

    static GameObject Directory()
    {
        var p = NewProp("f1_directory");
        float z = SignStand(p, 0.6f, 1.6f, 0.3f, Accent(1, "navy"), Accent(1, "steel", 0.6f, 0.9f));
        var white = SchoolPalette.Accent(1, "white");
        FrontLabel(p, "Title", "フロアガイド  FLOOR GUIDE", V(0f, 1.78f, z), 0.04f, white);
        string[] lines = { "5F 介護・臨床・IT", "4F スタジオ・電気", "3F 建築", "2F 受付・図書室", "1F ロビー" };
        for (int i = 0; i < lines.Length; i++)
            FrontLabel(p, $"Line{5 - i}", lines[i], V(0f, 1.58f - i * 0.28f, z), 0.055f, white);
        return SavePrefab(p);
    }

    static GameObject AccentWall()
    {
        var p = NewProp("f1_accent_wall");
        Box(p, "Panel", V(0f, 2.05f, 0f), V(4f, 3.9f, 0.1f), Accent(1, "salmon", 0.15f));
        Box(p, "Skirting", V(0f, 0.05f, 0.01f), V(4f, 0.1f, 0.12f), Accent(1, "white"));
        return SavePrefab(p);
    }

    static GameObject TactileStrip()
    {
        var p = NewProp("f1_tactile");
        var yellow = Accent(1, "tactile", 0.3f);
        Box(p, "Strip", V(0f, 0.005f, 0f), V(0.3f, 0.01f, 8f), yellow);
        for (int i = 0; i < 4; i++)   // the raised guide bars of a linear tactile block
            Box(p, $"Bar{i}", V(-0.105f + i * 0.07f, 0.015f, 0f), V(0.025f, 0.01f, 7.9f), yellow);
        return SavePrefab(p, false);
    }

    static GameObject DoorMat()
    {
        var p = NewProp("f1_door_mat");
        Box(p, "Mat", V(0f, 0.01f, 0f), V(2f, 0.02f, 1.4f), Accent(1, "mat", 0.05f));
        return SavePrefab(p, false);
    }

    static GameObject UmbrellaStand()
    {
        var p = NewProp("f1_umbrella");
        var black = Paint(1, "black", "#1A1A1C", 0.4f);
        var white = Accent(1, "white", 0.4f);
        Box(p, "Stand", V(0f, 0.3f, 0f), V(0.5f, 0.6f, 0.3f), black);
        for (int i = 0; i < 6; i++)
        {
            var at = V(-0.16f + (i % 3) * 0.16f, 0f, i < 3 ? -0.07f : 0.07f);
            Cyl(p, $"Umbrella{i}", at, 0.03f, 0.8f + (i % 2) * 0.1f, white, 0.2f);
        }
        return SavePrefab(p);
    }

    static GameObject Rail()
    {
        var p = NewProp("f1_rail");
        var steel = Accent(1, "steel", 0.85f, 0.9f);
        foreach (float x in new[] { -1f, 1f })
        {
            Cyl(p, $"Post{(x < 0 ? "L" : "R")}", V(x, 0f, 0f), 0.05f, 0.9f, steel);
            Cyl(p, $"Foot{(x < 0 ? "L" : "R")}", V(x, 0f, 0f), 0.1f, 0.01f, steel);
        }
        Part(p, "TopTube", PrimitiveType.Cylinder, V(0f, 0.9f, 0f), V(0.05f, 2.05f, 0.05f), steel, V(0f, 0f, 90f));
        Part(p, "MidTube", PrimitiveType.Cylinder, V(0f, 0.45f, 0f), V(0.03f, 2f, 0.03f), steel, V(0f, 0f, 90f));
        return SavePrefab(p);
    }

    static GameObject ExitSign()
    {
        var p = NewProp("f1_exit_sign");
        var steel = Accent(1, "steel", 0.6f, 0.9f);
        Cyl(p, "Base", Vector3.zero, 0.35f, 0.03f, steel);
        Cyl(p, "Pole", Vector3.zero, 0.04f, 2.3f, steel);
        Box(p, "Sign", V(0f, 2.4f, 0f), V(0.6f, 0.2f, 0.08f), Accent(1, "exit", 0.4f, 0f, true));
        var white = SchoolPalette.Accent(1, "white");
        FrontLabel(p, "Front", "EXIT 非常口", V(0f, 2.4f, 0.045f), 0.08f, white);
        Label(p, "Back", "EXIT 非常口", V(0f, 2.4f, -0.045f), 0.08f, white);
        return SavePrefab(p);
    }

    // ---------- 2F reception + library ----------

    static void Floor2(Ctx ctx)
    {
        var desk = Anchor(ctx, "frontdesk");
        var counter = PlaceCounter(ctx, desk);
        var runnerAt = counter == null ? desk : new Spot
        {
            name = "f2_counter", found = true, yaw = Snap(counter.eulerAngles.y),
            center = counter.position + counter.forward * ((0.35f + 4.3f) * M),
        };
        Put(ctx, "f2_runner", Runner(), runnerAt, V(2f, 0.01f, 8f), runnerAt.yaw);
        var books = Anchor(ctx, "book-shelf");
        for (int i = 1; i <= 3; i++)
            Put(ctx, $"f2_shelf_{i}", Bookshelf(i), books, V(1.05f, 1.8f, 0.4f), toWall: true, minDist: 3f);
        Put(ctx, "f2_library_sign", LibrarySign(), books, V(1.4f, 1.9f, 0.4f), minDist: 3f);
        Put(ctx, "f2_reading_table", ReadingTable(), Anchor(ctx, "library-table"), V(1.9f, 0.95f, 2.1f), minDist: 3f);
    }

    // The 7 m counter if it fits, else the 4 m one (both prefabs rebuilt; the instance keeps id f2_counter)
    static Transform PlaceCounter(Ctx ctx, Spot desk)
    {
        var wide = Counter(7f);
        var narrow = Counter(4f);
        var placed = PropsRoot(ctx.scene).Find("f2_counter");
        if (placed != null) { Debug.Log($"{Title} F2: f2_counter already placed, kept where it is."); return placed; }
        return Find(ctx, desk, V(7.1f, 2.6f, 0.7f), desk.yaw, 3f) != null
            ? Put(ctx, "f2_counter", wide, desk, V(7.1f, 2.6f, 0.7f), toWall: true, minDist: 3f)
            : Put(ctx, "f2_counter", narrow, desk, V(4.1f, 2.6f, 0.7f), toWall: true, minDist: 3f);
    }

    static GameObject Counter(float width)
    {
        var p = NewProp($"f2_counter_{width:0}m");
        // The slate wall's colour and texture, tiled like the wall (1.2 m repeats) on the front face
        var slate = Mat($"F2_counterBase_{width:0}m", SchoolPalette.Accent(2, "counterBase"), TexKind.SlatePanel, 0.2f, 0f,
                        new Vector2(width / 1.2f, 1.06f / 1.2f));
        Box(p, "Base", V(0f, 0.53f, 0f), V(width, 1.06f, 0.55f), slate);
        Box(p, "Top", V(0f, 1.08f, 0.02f), V(width + 0.04f, 0.04f, 0.62f), Accent(2, "counter", 0.5f));
        CounterWindow(p, width);
        return SavePrefab(p);
    }

    // Six glass panes in a thin white frame standing on the counter's back edge, with the reception sign above
    static void CounterWindow(GameObject p, float width)
    {
        var frame = Accent(2, "counter", 0.5f);
        var glass = Accent(2, "glass", 0.9f);
        const float bottom = 1.1f, top = 2.3f, z = -0.15f;
        float pane = width / 6f, h = top - bottom;
        Box(p, "RailLow", V(0f, bottom + 0.02f, z), V(width, 0.04f, 0.05f), frame);
        Box(p, "RailHigh", V(0f, top - 0.02f, z), V(width, 0.04f, 0.05f), frame);
        for (int i = 0; i <= 6; i++)
            Box(p, $"Mullion{i}", V(-width * 0.5f + i * pane, bottom + h * 0.5f, z), V(0.04f, h, 0.05f), frame);
        for (int i = 0; i < 6; i++)
            Box(p, $"Pane{i}", V(-width * 0.5f + (i + 0.5f) * pane, bottom + h * 0.5f, z), V(pane - 0.04f, h - 0.08f, 0.01f), glass);
        Box(p, "SignBoard", V(0f, top + 0.18f, z), V(1.6f, 0.32f, 0.04f), Accent(2, "signBoard", 0.3f));
        FrontLabel(p, "Sign", "受付  RECEPTION", V(0f, top + 0.18f, z + 0.025f), 0.12f, SchoolPalette.Accent(2, "counter"));
    }

    static GameObject Runner()
    {
        var p = NewProp("f2_runner");
        Box(p, "Runner", V(0f, 0.005f, 0f), V(2f, 0.01f, 8f), Accent(2, "runner", 0.05f));
        return SavePrefab(p, false);
    }

    static Material[] BookMats() => new[]
    {
        Paint(2, "book_navy", "#2A3A6A"), Paint(2, "book_red", "#A8323A"), Paint(2, "book_green", "#3A6A4A"),
        Paint(2, "book_cream", "#E8DCC0"), Paint(2, "book_brown", "#7A5238"), Paint(2, "book_grey", "#8A8C90"),
    };

    // A light grey steel shelf unit, 5 boards of books (seeded, so a rebuild looks the same)
    static GameObject Bookshelf(int n)
    {
        var p = NewProp($"f2_bookshelf_{n}");
        var steel = Accent(2, "shelf", 0.4f, 0.5f);
        const float w = 1f, h = 1.8f, d = 0.35f, t = 0.02f;
        Box(p, "SideL", V(-w * 0.5f + t * 0.5f, h * 0.5f, 0f), V(t, h, d), steel);
        Box(p, "SideR", V(w * 0.5f - t * 0.5f, h * 0.5f, 0f), V(t, h, d), steel);
        Box(p, "Back", V(0f, h * 0.5f, -d * 0.5f + t * 0.5f), V(w, h, t), steel);
        Box(p, "Top", V(0f, h - t * 0.5f, 0f), V(w, t, d), steel);
        var rng = new System.Random(n * 7919);
        var mats = BookMats();
        for (int i = 0; i < 5; i++)
        {
            float y = 0.06f + i * 0.345f;
            Box(p, $"Board{i}", V(0f, y, 0f), V(w - 2f * t, t, d), steel);
            Books(p, rng, mats, i, y + t * 0.5f, w - 2f * t - 0.02f, d);
        }
        return SavePrefab(p);
    }

    static void Books(GameObject p, System.Random rng, Material[] mats, int row, float y, float span, float d)
    {
        float x = -span * 0.5f;
        for (int k = 0; ; k++)
        {
            float bw = 0.03f + (float)rng.NextDouble() * 0.03f;
            if (x + bw > span * 0.5f) break;
            float bh = 0.2f + (float)rng.NextDouble() * 0.1f;
            if (rng.NextDouble() > 0.07)   // a gap now and then
                Box(p, $"Book{row}_{k}", V(x + bw * 0.5f, y + bh * 0.5f, 0.01f), V(bw * 0.95f, bh, d * 0.8f), mats[rng.Next(mats.Length)]);
            x += bw;
        }
    }

    static GameObject LibrarySign()
    {
        var p = NewProp("f2_library_sign");
        float z = SignStand(p, 1.4f, 0.4f, 1.45f, Paint(2, "navy", "#1E2A4A"), Accent(2, "shelf", 0.5f, 0.8f));
        FrontLabel(p, "Text", "図書室  LIBRARY", V(0f, 1.65f, z), 0.12f, SchoolPalette.Accent(2, "counter"));
        return SavePrefab(p);
    }

    static GameObject ReadingTable()
    {
        var p = NewProp("f2_reading_table");
        var oak = Accent(2, "oak", 0.35f);
        var leg = Accent(2, "shelf", 0.5f, 0.6f);
        Box(p, "Top", V(0f, 0.73f, 0f), V(1.8f, 0.04f, 0.9f), oak);
        foreach (var (x, z) in new[] { (-0.82f, -0.38f), (0.82f, -0.38f), (-0.82f, 0.38f), (0.82f, 0.38f) })
            Box(p, $"Leg{x:+0;-0}{z:+0;-0}", V(x, 0.355f, z), V(0.05f, 0.71f, 0.05f), leg);
        var blue = Accent(2, "chair", 0.3f);
        for (int i = 0; i < 4; i++)
        {
            bool far = i >= 2;
            Chair(p, $"Chair{i}", V(i % 2 == 0 ? -0.45f : 0.45f, 0f, far ? 0.68f : -0.68f), far ? 180f : 0f, blue, leg);
        }
        return SavePrefab(p);
    }

    // A library chair facing local +Z (seat, back, four legs)
    static void Chair(GameObject p, string name, Vector3 at, float yaw, Material seat, Material leg)
    {
        SubBox(p, $"{name}Seat", at, yaw, V(0f, 0.45f, 0f), V(0.42f, 0.04f, 0.42f), seat);
        SubBox(p, $"{name}Back", at, yaw, V(0f, 0.72f, -0.2f), V(0.42f, 0.36f, 0.03f), seat);
        foreach (var (x, z) in new[] { (-0.18f, -0.18f), (0.18f, -0.18f), (-0.18f, 0.18f), (0.18f, 0.18f) })
            SubBox(p, $"{name}Leg{x:+0;-0}{z:+0;-0}", at, yaw, V(x, 0.215f, z), V(0.03f, 0.43f, 0.03f), leg);
    }

    // ---------- 3F architecture ----------

    static void Floor3(Ctx ctx)
    {
        var paper = Anchor(ctx, "paper");
        var drafting = DraftingTable();
        for (int i = 1; i <= 3; i++)
            Put(ctx, $"f3_drafting_{i}", drafting, paper, V(3.7f, 0.8f, 1.7f), minDist: 3f);
        Put(ctx, "f3_whiteboard", Whiteboard(), Anchor(ctx, "whiteboard"), V(2.2f, 1.95f, 0.7f), minDist: 3f);
        var pcs = Anchor(ctx, "table-chair");
        var pc = PcDesk();
        for (int i = 1; i <= 2; i++)
            Put(ctx, $"f3_pc_desk_{i}", pc, pcs, V(1.9f, 1.2f, 1.5f), toWall: true, minDist: 3f);
        var models = Anchor(ctx, "house-models-shelf");
        Put(ctx, "f3_arch_model", ArchModel(), models, V(1.05f, 1.2f, 0.85f), minDist: 3f);
        Put(ctx, "f3_cabinet", Cabinet(), models, V(0.95f, 1.8f, 0.5f), toWall: true, minDist: 3f);
    }

    // A long white drafting table (under-shelf, four legs) with three stools along its front
    static GameObject DraftingTable()
    {
        var p = NewProp("f3_drafting_set");
        var white = Accent(3, "drafting", 0.4f);
        var chrome = Accent(3, "chrome", 0.7f, 0.9f);
        const float z = -0.3f;
        Box(p, "Top", V(0f, 0.73f, z), V(3.6f, 0.04f, 0.9f), white);
        Box(p, "Ruler", V(0f, 0.755f, z + 0.3f), V(3.4f, 0.01f, 0.04f), chrome);
        Box(p, "UnderShelf", V(0f, 0.2f, z), V(3.4f, 0.02f, 0.75f), white);
        foreach (var (x, dz) in new[] { (-1.72f, -0.4f), (1.72f, -0.4f), (-1.72f, 0.4f), (1.72f, 0.4f) })
            Box(p, $"Leg{x:+0;-0}{dz:+0;-0}", V(x, 0.355f, z + dz), V(0.05f, 0.71f, 0.05f), white);
        for (int i = 0; i < 3; i++) Stool(p, $"Stool{i}", V(-1.2f + i * 1.2f, 0f, 0.5f), chrome);
        return SavePrefab(p);
    }

    // A round black stool, 0.45 m, on a chrome post with a foot ring
    static void Stool(GameObject p, string name, Vector3 at, Material chrome)
    {
        var black = Accent(3, "stool", 0.35f);
        Cyl(p, $"{name}Base", at, 0.38f, 0.02f, black);
        Cyl(p, $"{name}Post", at, 0.04f, 0.4f, chrome, 0.02f);
        Cyl(p, $"{name}Ring", at, 0.32f, 0.02f, chrome, 0.15f);
        Cyl(p, $"{name}Seat", at, 0.35f, 0.05f, black, 0.4f);
    }

    static GameObject Whiteboard()
    {
        var p = NewProp("f3_whiteboard");
        var chrome = Accent(3, "chrome", 0.7f, 0.9f);
        var black = Accent(3, "stool", 0.35f);
        Box(p, "Board", V(0f, 1.35f, 0f), V(2f, 1f, 0.03f), Paint(3, "board", "#FAFAFA", 0.85f));
        Box(p, "TrimTop", V(0f, 1.86f, 0f), V(2.04f, 0.03f, 0.04f), chrome);
        Box(p, "Tray", V(0f, 0.85f, 0.04f), V(1.2f, 0.03f, 0.08f), chrome);
        foreach (float x in new[] { -1.03f, 1.03f })
        {
            string s = x < 0 ? "L" : "R";
            Box(p, $"Post{s}", V(x, 1.02f, 0f), V(0.04f, 1.86f, 0.04f), chrome);
            Box(p, $"Foot{s}", V(x, 0.1f, 0f), V(0.04f, 0.04f, 0.6f), chrome);
            foreach (float z in new[] { -0.27f, 0.27f })
                Part(p, $"Castor{s}{z:+0;-0}", PrimitiveType.Cylinder, V(x, 0.04f, z), V(0.07f, 0.03f, 0.07f), black, V(0f, 0f, 90f));
        }
        return SavePrefab(p);
    }

    // A light oak desk on white legs with two monitors, a keyboard each, a mini tower and two mesh chairs
    static GameObject PcDesk()
    {
        var p = NewProp("f3_pc_desk");
        var white = Accent(3, "drafting", 0.4f);
        var black = Accent(3, "monitor", 0.5f);
        const float z = -0.35f;
        Box(p, "Top", V(0f, 0.73f, z), V(1.8f, 0.04f, 0.7f), Accent(3, "oak", 0.35f));
        foreach (var (x, dz) in new[] { (-0.85f, -0.3f), (0.85f, -0.3f), (-0.85f, 0.3f), (0.85f, 0.3f) })
            Box(p, $"Leg{x:+0;-0}{dz:+0;-0}", V(x, 0.355f, z + dz), V(0.04f, 0.71f, 0.04f), white);
        Box(p, "Tower", V(0.65f, 0.21f, z - 0.05f), V(0.18f, 0.4f, 0.4f), black);
        var chrome = Accent(3, "chrome", 0.7f, 0.9f);
        foreach (float x in new[] { -0.45f, 0.45f })
        {
            string s = x < 0 ? "L" : "R";
            Monitor(p, $"Monitor{s}", V(x, 0.75f, z - 0.2f), black);
            Box(p, $"Keyboard{s}", V(x, 0.76f, z + 0.15f), V(0.45f, 0.02f, 0.15f), black);
            MeshChair(p, $"Chair{s}", V(x, 0f, 0.4f), 180f, chrome);
        }
        return SavePrefab(p);
    }

    static void Monitor(GameObject p, string name, Vector3 deskAt, Material black)
    {
        Box(p, $"{name}Foot", deskAt + V(0f, 0.005f, 0f), V(0.2f, 0.01f, 0.15f), black);
        Box(p, $"{name}Neck", deskAt + V(0f, 0.07f, 0f), V(0.04f, 0.13f, 0.03f), black);
        Box(p, $"{name}Screen", deskAt + V(0f, 0.3f, 0.02f), V(0.55f, 0.35f, 0.03f), black);
        Box(p, $"{name}Glass", deskAt + V(0f, 0.3f, 0.036f), V(0.52f, 0.32f, 0.004f), Paint(3, "screen", "#1A2430", 0.9f));
    }

    // A blue mesh office chair facing local +Z: round base (the five-star base, simplified), gas lift, seat, back
    static void MeshChair(GameObject p, string name, Vector3 at, float yaw, Material chrome)
    {
        var blue = Accent(3, "chair", 0.3f);
        Cyl(p, $"{name}Base", at, 0.6f, 0.05f, Accent(3, "monitor", 0.5f), 0.03f);
        Cyl(p, $"{name}Lift", at, 0.06f, 0.36f, chrome, 0.08f);
        SubBox(p, $"{name}Seat", at, yaw, V(0f, 0.47f, 0f), V(0.48f, 0.07f, 0.46f), blue);
        SubBox(p, $"{name}Back", at, yaw, V(0f, 0.85f, -0.22f), V(0.46f, 0.5f, 0.04f), blue);
    }

    // A white plinth carrying a site model: cream / white massing blocks and tiny green trees
    static GameObject ArchModel()
    {
        var p = NewProp("f3_arch_model");
        Box(p, "Plinth", V(0f, 0.45f, 0f), V(1f, 0.9f, 0.8f), Paint(3, "plinth", "#F4F4F4", 0.3f));
        Box(p, "Site", V(0f, 0.91f, 0f), V(0.9f, 0.02f, 0.7f), Paint(3, "site", "#C8C8C0", 0.2f));
        var cream = Accent(3, "model", 0.2f);
        var white = Accent(3, "drafting", 0.2f);
        var blocks = new[]
        {
            (-0.28f, -0.18f, 0.22f, 0.2f, 0.28f), (-0.02f, -0.2f, 0.18f, 0.16f, 0.14f), (0.25f, -0.15f, 0.2f, 0.24f, 0.2f),
            (-0.25f, 0.15f, 0.16f, 0.18f, 0.1f), (0.05f, 0.12f, 0.24f, 0.14f, 0.18f), (0.3f, 0.18f, 0.12f, 0.12f, 0.08f),
        };
        for (int i = 0; i < blocks.Length; i++)
        {
            var (x, z, w, d, h) = blocks[i];
            Box(p, $"Block{i}", V(x, 0.92f + h * 0.5f, z), V(w, h, d), i % 2 == 0 ? cream : white);
        }
        var green = Paint(3, "tree", "#4E8A4A", 0.2f);
        for (int i = 0; i < 8; i++)
        {
            var at = V(-0.4f + i * 0.115f, 0f, i % 2 == 0 ? 0.31f : -0.02f);
            Part(p, $"Tree{i}", PrimitiveType.Sphere, at + V(0f, 0.96f, 0f), V(0.06f, 0.07f, 0.06f), green);
        }
        return SavePrefab(p);
    }

    static GameObject Cabinet()
    {
        var p = NewProp("f3_cabinet");
        var chrome = Accent(3, "chrome", 0.7f, 0.9f);
        Box(p, "Body", V(0f, 0.9f, 0f), V(0.9f, 1.8f, 0.45f), Accent(3, "cabinet", 0.45f, 0.3f));
        Box(p, "Kick", V(0f, 0.04f, 0.2f), V(0.88f, 0.08f, 0.06f), Paint(3, "kick", "#5A5C60", 0.3f));
        Box(p, "Seam", V(0f, 0.95f, 0.226f), V(0.006f, 1.7f, 0.004f), Paint(3, "seam", "#8A8C90", 0.3f));
        Box(p, "HandleL", V(-0.04f, 1f, 0.235f), V(0.02f, 0.15f, 0.02f), chrome);
        Box(p, "HandleR", V(0.04f, 1f, 0.235f), V(0.02f, 0.15f, 0.02f), chrome);
        return SavePrefab(p);
    }
}
