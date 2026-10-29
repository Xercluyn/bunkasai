using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using static EditorKit;
using static SchoolKit;

/// <summary>
/// Arrange Rooms on 3F (Tools > Elevator Game > School > Steps > Arrange Rooms): lays out the 3F architecture rooms like the real ones.
/// The room in front of the architecture class swaps its merged FBX desk block for 17 PC desks on the old desk
/// spots; the architecture class swaps its loose paper desks for 5 rows of short drafting tables joined end to
/// end in the middle of the room (stools on the south side, everyone facing the north wall, wide aisles both
/// sides), some of them with drawings, rulers and study models on top, and moves the site model and cabinet into
/// the corners once. The FBX model shelves in the way of the rows are hidden with what stands on them; the rest
/// are painted drafting white. New props are placed once and then kept where they are; props deleted by hand
/// stay deleted. Needs Place School Props to have built the f3_* prefabs first.
/// </summary>
public static class SchoolLayout3F
{
    const string Title = "Arrange 3F Rooms";
    const float M = SchoolPalette.WorldPerMetre;
    const string OnceStep = "layout3F_v1";
    const string OnceShortRows = "layout3F_v2";

    // Old desk centres in the room in front of the architecture class (world x, z): chairs south, facing +Z
    static readonly (float x, float z)[] PcDesks =
    {
        (-229.47f, -25.04f), (-216.53f, -25.04f),
        (-229.42f, -11.91f), (-216.48f, -11.91f), (-203.54f, -11.91f), (-190.59f, -11.91f), (-177.65f, -11.91f),
        (-229.37f, 1.21f), (-216.42f, 1.21f), (-203.48f, 1.21f), (-190.54f, 1.21f), (-177.60f, 1.21f),
        (-229.31f, 14.34f), (-216.37f, 14.34f), (-203.43f, 14.34f), (-190.49f, 14.34f), (-177.54f, 14.34f),
    };

    // Drafting rows: three 2.4 m sets joined along X per row (7.2 m, centred on x −241.4), rows front (north) to
    // back. Row numbers keep the ids of the first 7-row layout, whose rows 1 and 7 are gone.
    const float ShortWidth = 2.4f;
    static readonly float[] DraftX = { -241.4f - ShortWidth * M, -241.4f, -241.4f + ShortWidth * M };
    static readonly (int row, float z)[] DraftRows = { (2, 72f), (3, 84f), (4, 96f), (5, 108f), (6, 120f) };
    static readonly int[] RetiredRows = { 1, 7 };

    // Where the rows stand, aisle-wide: their tops and stools (z row − 1.0 m .. row + 0.45 m), grown by 1 m
    static readonly Rect RowsZone = Rect.MinMaxRect(
        DraftX[0] - ShortWidth * M * 0.5f - M, DraftRows[0].z - 1.0f * M - M,
        DraftX[2] + ShortWidth * M * 0.5f + M, DraftRows[DraftRows.Length - 1].z + 0.45f * M + M);

    // The architecture class, inside its walls (world x, z)
    static readonly Rect ArchRoom = Rect.MinMaxRect(-274.7f, 43.4f, -208.1f, 149.9f);

    static readonly Regex PaperDesk = new Regex(@"^paper(\.\d+)?$", RegexOptions.IgnoreCase);
    static readonly Regex DeskLeg = new Regex(@"^leg2(\.\d+)?$", RegexOptions.IgnoreCase);
    static readonly Regex ModelShelf = new Regex(@"^apron_short(\.\d+)?$", RegexOptions.IgnoreCase);

    // What one run did, for the summary line
    sealed class Report
    {
        public int placed, kept, deleted, hidden, replaced, whitened;
        public readonly List<string> overlaps = new List<string>();
    }

    /// <summary>Arranges floor 3's PC room and architecture class (no-op for other floors).</summary>
    internal static void Apply(Scene scene, int floor)
    {
        if (floor != 3) return;
        var pc = LoadPrefab("f3_pc_desk");
        var model = LoadPrefab("f3_arch_model");
        var cabinet = LoadPrefab("f3_cabinet");
        if (pc == null || model == null || cabinet == null) return;
        var drafting = ShortDraftingSet();
        var clutter = new[] { DeskPlans(), DeskBlueprint(), DeskStudyModel() };
        var towers = new[] { TowerModel("f3_deco_tower_mid", false), TowerModel("f3_deco_tower_high", true) };

        Undo.SetCurrentGroupName(Title);
        var report = new Report();
        report.hidden += Hide(FindInBuilding(scene, "table-chair.012").Where(t => t.name == "table-chair.012"));
        report.hidden += Hide(FindInBuilding(scene, "paper").Where(t => PaperDesk.IsMatch(t.name) && InArchRoom(t)));
        report.hidden += Hide(FindInBuilding(scene, "leg2").Where(t => DeskLeg.IsMatch(t.name) && InArchRoom(t)));
        Physics.SyncTransforms();

        foreach (var id in new[] { "f3_pc_desk_1", "f3_pc_desk_2", "f3_drafting_1", "f3_drafting_2", "f3_drafting_3" })
            Retire(scene, id);
        foreach (int r in RetiredRows)
            for (int c = 1; c <= DraftX.Length; c++) Retire(scene, DraftId(r, c));
        PlacePcDesks(scene, pc, report);
        if (Once(scene, OnceShortRows)) ShortenRows(scene, drafting, report);
        PlaceDrafting(scene, drafting, report);
        if (Once(scene, OnceStep))
        {
            Relocate(scene, "f3_arch_model", model, new Vector3(-214f, 0f, 136f), 270f, report);
            Relocate(scene, "f3_cabinet", cabinet, new Vector3(-272.3f, 0f, 140f), 90f, report);
        }
        Decorate(scene, clutter, report);
        PlaceTowers(scene, towers, report);
        ClearModelShelves(scene, report);
        Summarise(report);
    }

    static GameObject LoadPrefab(string name)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/{name}.prefab");
        if (prefab == null) Debug.LogWarning($"{Title}: {PrefabFolder}/{name}.prefab is missing. Run Tools > Elevator Game > School > Steps > Place School Props first.");
        return prefab;
    }

    // ---------- the two rooms ----------

    // One PC desk per old desk; yaw 180 puts the chairs (local +Z) south and the top (local z −0.35 m) on the old desk
    static void PlacePcDesks(Scene scene, GameObject prefab, Report report)
    {
        var rot = Quaternion.Euler(0f, 180f, 0f);
        for (int i = 0; i < PcDesks.Length; i++)
        {
            var desk = new Vector3(PcDesks[i].x, 0f, PcDesks[i].z);
            Put(scene, $"f3_pcdesk_{i + 1:00}", prefab, desk + rot * new Vector3(0f, 0f, 0.35f * M), 180f, report);
        }
    }

    static string DraftId(int row, int col) => $"f3_draft_{row}_{col}";

    // Yaw 180 puts the stools south and the top (local z −0.3 m) on the row line
    static Vector3 DraftOrigin(float x, float z) => new Vector3(x, 0f, z) + Quaternion.Euler(0f, 180f, 0f) * new Vector3(0f, 0f, 0.3f * M);

    // Rows of short drafting sets joined end to end
    static void PlaceDrafting(Scene scene, GameObject prefab, Report report)
    {
        foreach (var (row, z) in DraftRows)
            for (int c = 0; c < DraftX.Length; c++)
                Put(scene, DraftId(row, c + 1), prefab, DraftOrigin(DraftX[c], z), 180f, report);
    }

    // The one-off swap of the first layout's long sets for short ones on the new spots (sets deleted by hand stay out)
    static void ShortenRows(Scene scene, GameObject prefab, Report report)
    {
        var root = PropsRoot(scene);
        foreach (var (row, z) in DraftRows)
            for (int c = 0; c < DraftX.Length; c++)
            {
                string id = DraftId(row, c + 1);
                if (root.Find(id) == null) continue;   // never placed (placed below) or deleted by hand (left out)
                var at = DraftOrigin(DraftX[c], z);
                at.y = Ground(scene, at);
                var go = Place(scene, id, prefab, at, 180f, replace: true);
                report.replaced++;
                CheckOverlap(scene, go, report);
            }
    }

    // The one-off move of an older prop: moved if there, placed if never placed, left out if deleted by hand
    static void Relocate(Scene scene, string id, GameObject prefab, Vector3 at, float yaw, Report report)
    {
        if (IsDeleted(scene, id)) { Debug.Log($"{Title}: {id} was deleted by hand; not moved or placed."); report.deleted++; return; }
        if (!IsPlaced(scene, id)) { Put(scene, id, prefab, at, yaw, report); return; }
        at.y = Ground(scene, at);
        Move(scene, id, at, yaw);
        CheckOverlap(scene, PropsRoot(scene).Find(id).gameObject, report);
    }

    // Drawings, rulers and study models on about 3 tables in 5 (the same ones every run), riding on their table
    static void Decorate(Scene scene, GameObject[] clutter, Report report)
    {
        var root = PropsRoot(scene);
        foreach (var (row, _) in DraftRows)
            for (int c = 1; c <= DraftX.Length; c++)
            {
                if ((row * 7 + c * 3) % 5 >= 3) continue;
                string id = $"f3_deco_{row}_{c}";
                var table = root.Find(DraftId(row, c));
                if (IsDeleted(scene, id)) { report.deleted++; continue; }
                if (IsPlaced(scene, id)) { report.kept++; continue; }
                if (table == null) continue;   // no table to put it on
                var go = Place(scene, id, clutter[(row + c) % clutter.Length], table.position, table.eulerAngles.y);
                if (go != null) report.placed++;
            }
    }

    // Medium and high-rise study models on three tables Decorate leaves bare, spread over the room
    // (row, column, high-rise?)
    static readonly (int row, int col, bool high)[] TowerTables = { (2, 3, true), (4, 2, false), (6, 2, true) };

    static void PlaceTowers(Scene scene, GameObject[] towers, Report report)
    {
        var root = PropsRoot(scene);
        foreach (var (row, col, high) in TowerTables)
        {
            string id = $"f3_tower_{row}_{col}";
            if (IsDeleted(scene, id)) { report.deleted++; continue; }
            if (IsPlaced(scene, id)) { report.kept++; continue; }
            var table = root.Find(DraftId(row, col));
            if (table == null) continue;   // no table to put it on
            var go = Place(scene, id, towers[high ? 1 : 0], table.position, table.eulerAngles.y);
            if (go != null) report.placed++;
        }
    }

    // ---------- placement helpers ----------

    // Places a new layout prop on the floor (kept where it is if already there; nothing if deleted by hand)
    static void Put(Scene scene, string id, GameObject prefab, Vector3 at, float yaw, Report report)
    {
        if (IsDeleted(scene, id)) { report.deleted++; return; }
        if (IsPlaced(scene, id)) { report.kept++; return; }
        at.y = Ground(scene, at);
        var go = Place(scene, id, prefab, at, yaw);
        if (go == null) { report.deleted++; return; }
        report.placed++;
        CheckOverlap(scene, go, report);
    }

    // Warns (without moving it) when the prop's box collider, lifted off the floor and shrunk a hair, hits anything
    static void CheckOverlap(Scene scene, GameObject go, Report report)
    {
        var others = OverlapsWith(scene, go.transform);
        if (others.Count == 0) return;
        report.overlaps.Add(go.name);
        Debug.LogWarning($"{Title}: {go.name} at {go.transform.position} overlaps {string.Join(", ", others)}.", go);
    }

    // ---------- building furniture ----------

    // Hides building objects (recorded on their prefab instance); returns how many were newly hidden
    static int Hide(IEnumerable<Transform> targets) => targets.ToList().Count(t => EditorKit.Hide(t.gameObject, Title));

    // The FBX model shelves (apron_short): hidden, with the models on them, where they stand in or between the
    // drafting rows; painted drafting white elsewhere (their models left as they are)
    static void ClearModelShelves(Scene scene, Report report)
    {
        var white = Accent(3, "drafting", 0.4f);
        var hide = new List<Transform>();
        foreach (var shelf in FindInBuilding(scene, "apron_short").Where(t => ModelShelf.IsMatch(t.name) && InArchRoom(t)))
        {
            if (!shelf.gameObject.activeInHierarchy) { Debug.Log($"{Title}: {shelf.name} is already hidden; left as it is.", shelf); continue; }
            if (!(RendererBounds(shelf.GetComponentsInChildren<Renderer>()) is Bounds b)) continue;
            var footprint = Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z);
            if (footprint.Overlaps(RowsZone))
            {
                var onTop = StandingOn(scene, shelf, b);
                Debug.Log($"{Title}: {shelf.name} (bounds {b.min}..{b.max}) stands in the drafting rows: hidden, with " +
                          (onTop.Count == 0 ? "nothing on it." : $"{string.Join(", ", onTop.Select(t => t.name))} on it."), shelf);
                hide.Add(shelf);
                hide.AddRange(onTop);
            }
            else
            {
                int slots = Whiten(shelf, white);
                report.whitened += slots;
                Debug.Log($"{Title}: {shelf.name} (bounds {b.min}..{b.max}) is clear of the drafting rows: kept, " +
                          (slots == 0 ? "already white." : $"{slots} oak material slot(s) painted drafting white."), shelf);
            }
        }
        report.hidden += Hide(hide);
    }

    // Visible building objects whose bottom sits on the shelf's top (within 0.5 unit) and whose middle is over it
    static List<Transform> StandingOn(Scene scene, Transform shelf, Bounds b)
    {
        const float tolerance = 0.5f;
        return FindInBuilding(scene, "")
            .Where(t => t.gameObject.activeInHierarchy && !t.IsChildOf(shelf) && !shelf.IsChildOf(t))
            .Where(t =>
            {
                var r = t.GetComponent<Renderer>();
                if (r == null) return false;
                var o = r.bounds;
                return Mathf.Abs(o.min.y - b.max.y) <= tolerance &&
                       o.center.x > b.min.x && o.center.x < b.max.x && o.center.z > b.min.z && o.center.z < b.max.z &&
                       o.size.x <= b.size.x + 1f && o.size.z <= b.size.z + 1f;
            })
            .ToList();
    }

    // Swaps the oak material slots of an object's renderers for `white` (with undo); returns how many slots changed
    static int Whiten(Transform t, Material white)
    {
        int count = 0;
        foreach (var r in t.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            var swapped = mats.Select(m => m != null && m.name.IndexOf("oak", StringComparison.OrdinalIgnoreCase) >= 0 ? white : m).ToArray();
            int changed = mats.Where((m, i) => m != swapped[i]).Count();
            if (changed == 0) continue;
            Undo.RecordObject(r, Title);
            r.sharedMaterials = swapped;
            PrefabUtility.RecordPrefabInstancePropertyModifications(r);
            count += changed;
        }
        return count;
    }

    // Whether an object's middle (its renderers' bounds, else its position) is inside the architecture class
    static bool InArchRoom(Transform t)
    {
        var p = RendererBounds(t.GetComponentsInChildren<Renderer>())?.center ?? t.position;
        return ArchRoom.Contains(new Vector2(p.x, p.z));
    }

    static void Summarise(Report report)
    {
        string overlaps = report.overlaps.Count == 0 ? "no overlaps" : $"{report.overlaps.Count} overlapping (see warnings): {string.Join(", ", report.overlaps)}";
        Debug.Log($"{Title}: {report.placed} props placed, {report.replaced} drafting sets swapped for short ones, " +
                  $"{report.kept} already there and kept, {report.deleted} left out (deleted by hand), " +
                  $"{report.hidden} building objects hidden, {report.whitened} shelf material slots painted white; {overlaps}.");
    }

    // ---------- props of this layout ----------

    // The short drafting set: a 2.4 m white table (under-shelf, four legs) with two stools along its front
    static GameObject ShortDraftingSet()
    {
        var p = NewProp("f3_drafting_short");
        var white = Accent(3, "drafting", 0.4f);
        var chrome = Accent(3, "chrome", 0.7f, 0.9f);
        const float z = -0.3f, legX = ShortWidth * 0.5f - 0.08f;
        Box(p, "Top", V(0f, 0.73f, z), V(ShortWidth, 0.04f, 0.9f), white);
        Box(p, "Ruler", V(0f, 0.755f, z + 0.3f), V(ShortWidth - 0.2f, 0.01f, 0.04f), chrome);
        Box(p, "UnderShelf", V(0f, 0.2f, z), V(ShortWidth - 0.2f, 0.02f, 0.75f), white);
        foreach (var (x, dz) in new[] { (-legX, -0.4f), (legX, -0.4f), (-legX, 0.4f), (legX, 0.4f) })
            Box(p, $"Leg{x:+0;-0}{dz:+0;-0}", V(x, 0.355f, z + dz), V(0.05f, 0.71f, 0.05f), white);
        for (int i = 0; i < 2; i++) Stool(p, $"Stool{i}", V(-0.6f + i * 1.2f, 0f, 0.5f), chrome);
        return SavePrefab(p);
    }

    // A round black stool, 0.45 m, on a chrome post with a foot ring (as the long sets' stools)
    static void Stool(GameObject p, string name, Vector3 at, Material chrome)
    {
        var black = Accent(3, "stool", 0.35f);
        Cyl(p, $"{name}Base", at, 0.38f, 0.02f, black);
        Cyl(p, $"{name}Post", at, 0.04f, 0.4f, chrome, 0.02f);
        Cyl(p, $"{name}Ring", at, 0.32f, 0.02f, chrome, 0.15f);
        Cyl(p, $"{name}Seat", at, 0.35f, 0.05f, black, 0.4f);
    }

    // Table clutter, in the set's own frame (it is placed on its table's spot and turn): the top is at y 0.75 m,
    // spans x ±1.2 and z −0.75..0.15, the chrome ruler lies along z 0 and the seats are at x ±0.6, so things stay
    // in z −0.72..−0.05

    const float TopY = 0.75f;

    sealed class Clutter
    {
        public Material paper, ink, blueprint, blueInk, wood, clear, scale, pencil, lead, card, cream, white, tree;
    }

    static Clutter ClutterMats() => new Clutter
    {
        paper = Mat("F3_paper", UiKit.Hex("#F7F3E8"), TexKind.Plain, 0.15f),
        ink = Mat("F3_ink", UiKit.Hex("#3A3F46"), TexKind.Plain, 0.1f),
        blueprint = Mat("F3_blueprint", UiKit.Hex("#CFE3F5"), TexKind.Plain, 0.15f),
        blueInk = Mat("F3_blueInk", UiKit.Hex("#2F5E9E"), TexKind.Plain, 0.1f),
        wood = Accent(3, "oak", 0.35f),
        clear = Mat("F3_setSquare", UiKit.Hex("#B8C4CC"), TexKind.Plain, 0.85f),
        scale = Mat("F3_scaleRuler", UiKit.Hex("#E6E6DE"), TexKind.Plain, 0.4f),
        pencil = Mat("F3_pencil", UiKit.Hex("#E8B730"), TexKind.Plain, 0.4f),
        lead = Accent(3, "stool", 0.35f),
        card = Mat("F3_cardboard", UiKit.Hex("#B8936A"), TexKind.Plain, 0.1f),
        cream = Accent(3, "model", 0.2f),
        white = Accent(3, "drafting", 0.4f),   // the tables' smoothness: it's one shared material
        tree = Mat("F3_tree", UiKit.Hex("#4E8A4A"), TexKind.Plain, 0.2f),
    };

    // Plans on the left seat, a smaller plan with a scale ruler and set square on the right
    static GameObject DeskPlans()
    {
        var p = NewProp("f3_deco_plans");
        var m = ClutterMats();
        Sheet(p, "SheetL", V(-0.6f, 0f, -0.4f), 4f, 0.594f, 0.42f, m.paper, m.ink);
        TSquare(p, "TSquareL", V(-0.6f, 0f, -0.12f), 0f, 0.6f, m.wood);
        Pencil(p, "PencilL", V(-0.2f, 0f, -0.55f), 70f, m);
        Sheet(p, "SheetR", V(0.58f, 0f, -0.42f), -6f, 0.42f, 0.3f, m.paper, m.ink);
        ScaleRuler(p, "ScaleR", V(0.6f, 0f, -0.16f), 2f, m.scale);
        SetSquare(p, "SetSquareR", V(0.86f, 0f, -0.7f), 0f, 0.2f, m.clear);
        Pencil(p, "PencilR0", V(0.25f, 0f, -0.6f), 20f, m);
        Pencil(p, "PencilR1", V(0.28f, 0f, -0.66f), 35f, m);
        return SavePrefab(p, false);
    }

    // A blueprint and a set square on the left seat, a plan with T-square and scale ruler on the right
    static GameObject DeskBlueprint()
    {
        var p = NewProp("f3_deco_blueprint");
        var m = ClutterMats();
        Sheet(p, "BlueprintL", V(-0.6f, 0f, -0.4f), -3f, 0.594f, 0.42f, m.blueprint, m.blueInk);
        SetSquare(p, "SetSquareL", V(-1.12f, 0f, -0.72f), 0f, 0.18f, m.clear);
        Pencil(p, "PencilL", V(-0.22f, 0f, -0.3f), 80f, m);
        Sheet(p, "SheetR", V(0.55f, 0f, -0.4f), 5f, 0.594f, 0.42f, m.paper, m.ink);
        TSquare(p, "TSquareR", V(0.6f, 0f, -0.12f), 0f, 0.6f, m.wood);
        ScaleRuler(p, "ScaleR", V(1.0f, 0f, -0.45f), 90f, m.scale);
        return SavePrefab(p, false);
    }

    // A study model on a cardboard base beside its plan on the left seat, a plan with tools on the right
    static GameObject DeskStudyModel()
    {
        var p = NewProp("f3_deco_model");
        var m = ClutterMats();
        StudyModel(p, "ModelL", V(-0.6f, 0f, -0.42f), 8f, m);
        Sheet(p, "SheetL", V(-0.15f, 0f, -0.5f), -10f, 0.42f, 0.3f, m.paper, m.ink);
        Pencil(p, "PencilL", V(-0.15f, 0f, -0.2f), 10f, m);
        Sheet(p, "SheetR", V(0.6f, 0f, -0.4f), 2f, 0.594f, 0.42f, m.paper, m.ink);
        SetSquare(p, "SetSquareR", V(1.1f, 0f, -0.12f), 180f, 0.2f, m.clear);
        TSquare(p, "TSquareR", V(0.5f, 0f, -0.1f), 0f, 0.5f, m.wood);
        return SavePrefab(p, false);
    }

    // A 1:100-ish study model in the middle of the table, shared by both seats: a podium with a slim tower
    // (about 0.35 m, a 10-storey block; 0.6 m with a setback crown for the high-rise), a low neighbour, trees
    static GameObject TowerModel(string id, bool high)
    {
        var p = NewProp(id);
        var m = ClutterMats();
        var glass = Mat("F3_modelGlass", UiKit.Hex("#AFC6D6"), TexKind.Plain, 0.7f);
        var at = V(0f, 0f, -0.4f);
        const float baseTop = TopY + 0.006f;
        Flat(p, "Base", at, 0f, Vector3.zero, TopY + 0.003f, V(0.46f, 0.006f, 0.34f), m.card);
        Flat(p, "Podium", at, 0f, V(-0.04f, 0f, 0.02f), baseTop + 0.03f, V(0.26f, 0.06f, 0.2f), m.white);
        float tower = high ? 0.48f : 0.3f, podiumTop = baseTop + 0.06f;
        Flat(p, "Tower", at, 0f, V(-0.06f, 0f, 0.03f), podiumTop + tower * 0.5f, V(0.12f, tower, 0.12f), m.cream);
        // window bands: thin glass strips on the front and side every storey-ish
        int bands = Mathf.RoundToInt(tower / 0.04f);
        for (int i = 1; i < bands; i++)
        {
            float y = podiumTop + i * tower / bands;
            Flat(p, $"BandF{i}", at, 0f, V(-0.06f, 0f, -0.031f), y, V(0.118f, 0.008f, 0.004f), glass);
            Flat(p, $"BandS{i}", at, 0f, V(-0.001f, 0f, 0.03f), y, V(0.004f, 0.008f, 0.118f), glass);
        }
        if (high)
        {
            float crown = podiumTop + tower;
            Flat(p, "Setback", at, 0f, V(-0.06f, 0f, 0.03f), crown + 0.04f, V(0.08f, 0.08f, 0.08f), m.white);
            Flat(p, "Mast", at, 0f, V(-0.06f, 0f, 0.03f), crown + 0.12f, V(0.008f, 0.08f, 0.008f), m.lead);
        }
        Flat(p, "Neighbour", at, 0f, V(0.15f, 0f, -0.06f), baseTop + 0.07f, V(0.1f, 0.14f, 0.12f), m.white);
        var trees = new[] { (0.17f, 0.11f), (0.08f, 0.13f), (-0.19f, -0.12f), (0.19f, -0.14f) };
        for (int i = 0; i < trees.Length; i++)
            Flat(p, $"Tree{i}", at, 0f, V(trees[i].Item1, 0f, trees[i].Item2), baseTop + 0.02f,
                 V(0.035f, 0.04f, 0.035f), m.tree, default, PrimitiveType.Sphere);
        return SavePrefab(p, false);
    }

    // A part lying on the table: `local` (x, ·, z) is offset from `at` within the item turned `yaw`; y is its centre
    static void Flat(GameObject p, string name, Vector3 at, float yaw, Vector3 local, float y, Vector3 size, Material mat,
                     Vector3 euler = default, PrimitiveType type = PrimitiveType.Cube)
    {
        var o = at + Quaternion.Euler(0f, yaw, 0f) * new Vector3(local.x, 0f, local.z);
        Part(p, name, type, V(o.x, y, o.z), size, mat, V(euler.x, yaw + euler.y, euler.z));
    }

    // A drawing sheet `w` x `d` with a border, a building outline, two inner walls and a title block line
    static void Sheet(GameObject p, string name, Vector3 at, float yaw, float w, float d, Material paper, Material ink)
    {
        Flat(p, name, at, yaw, Vector3.zero, TopY + 0.0015f, V(w, 0.003f, d), paper);
        void Line(string part, float x, float z, float sx, float sz) =>
            Flat(p, name + part, at, yaw, V(x, 0f, z), TopY + 0.004f, V(sx, 0.002f, sz), ink);
        const float thin = 0.004f, wall = 0.008f;
        Line("BorderN", 0f, d * 0.5f - 0.02f, w - 0.04f, thin);
        Line("BorderS", 0f, -d * 0.5f + 0.02f, w - 0.04f, thin);
        Line("BorderW", -w * 0.5f + 0.02f, 0f, thin, d - 0.04f);
        Line("BorderE", w * 0.5f - 0.02f, 0f, thin, d - 0.04f);
        float cx = -w * 0.08f, cz = d * 0.05f, bw = w * 0.6f, bd = d * 0.55f;
        Line("WallN", cx, cz + bd * 0.5f, bw, wall);
        Line("WallS", cx, cz - bd * 0.5f, bw, wall);
        Line("WallW", cx - bw * 0.5f, cz, wall, bd);
        Line("WallE", cx + bw * 0.5f, cz, wall, bd);
        Line("InnerV", cx - bw * 0.1f, cz - bd * 0.2f, wall * 0.6f, bd * 0.6f);
        Line("InnerH", cx - bw * 0.3f, cz + bd * 0.1f, bw * 0.4f, wall * 0.6f);
        Line("Title", w * 0.5f - 0.1f, -d * 0.5f + 0.06f, 0.14f, thin);
    }

    // A light-wood T-square: a thin blade along local x with its head across the left end
    static void TSquare(GameObject p, string name, Vector3 at, float yaw, float length, Material wood)
    {
        Flat(p, name + "Blade", at, yaw, Vector3.zero, TopY + 0.002f, V(length, 0.004f, 0.05f), wood);
        Flat(p, name + "Head", at, yaw, V(-length * 0.5f + 0.02f, 0f, -0.05f), TopY + 0.008f, V(0.04f, 0.016f, 0.2f), wood);
    }

    // A clear-grey 45° set square drawn as its three bars, right angle at `at`, legs along local +x and +z
    static void SetSquare(GameObject p, string name, Vector3 at, float yaw, float size, Material clear)
    {
        const float bar = 0.02f, y = TopY + 0.0015f;
        Flat(p, name + "LegX", at, yaw, V(size * 0.5f, 0f, 0f), y, V(size, 0.003f, bar), clear);
        Flat(p, name + "LegZ", at, yaw, V(0f, 0f, size * 0.5f), y, V(bar, 0.003f, size), clear);
        Flat(p, name + "Hyp", at, yaw, V(size * 0.5f, 0f, size * 0.5f), y, V(size * 1.414f, 0.003f, bar), clear, V(0f, 45f, 0f));
    }

    // A triangular scale ruler, 30 cm (a bar turned 45° about its length, half sunk so it rests on a face)
    static void ScaleRuler(GameObject p, string name, Vector3 at, float yaw, Material mat) =>
        Flat(p, name, at, yaw, Vector3.zero, TopY + 0.009f, V(0.3f, 0.018f, 0.018f), mat, V(45f, 0f, 0f));

    static void Pencil(GameObject p, string name, Vector3 at, float yaw, Clutter m)
    {
        Flat(p, name, at, yaw, Vector3.zero, TopY + 0.0035f, V(0.17f, 0.007f, 0.007f), m.pencil);
        Flat(p, name + "Tip", at, yaw, V(0.09f, 0f, 0f), TopY + 0.0035f, V(0.012f, 0.004f, 0.004f), m.lead);
    }

    // White / cream massing blocks and a few tiny trees on a cardboard base
    static void StudyModel(GameObject p, string name, Vector3 at, float yaw, Clutter m)
    {
        const float baseTop = TopY + 0.006f;
        Flat(p, name + "Base", at, yaw, Vector3.zero, TopY + 0.003f, V(0.42f, 0.006f, 0.32f), m.card);
        var blocks = new[]
        {
            (-0.1f, -0.06f, 0.1f, 0.08f, 0.12f), (0.04f, -0.07f, 0.08f, 0.1f, 0.07f),
            (0.12f, 0.05f, 0.1f, 0.07f, 0.16f), (-0.08f, 0.08f, 0.12f, 0.06f, 0.05f),
        };
        for (int i = 0; i < blocks.Length; i++)
        {
            var (x, z, w, d, h) = blocks[i];
            Flat(p, $"{name}Block{i}", at, yaw, V(x, 0f, z), baseTop + h * 0.5f, V(w, h, d), i % 2 == 0 ? m.cream : m.white);
        }
        var trees = new[] { (-0.17f, -0.12f), (-0.17f, 0.12f), (0.17f, -0.12f), (0.02f, 0.13f), (0.18f, 0.13f) };
        for (int i = 0; i < trees.Length; i++)
            Flat(p, $"{name}Tree{i}", at, yaw, V(trees[i].Item1, 0f, trees[i].Item2), baseTop + 0.02f,
                 V(0.035f, 0.04f, 0.035f), m.tree, default, PrimitiveType.Sphere);
    }
}
