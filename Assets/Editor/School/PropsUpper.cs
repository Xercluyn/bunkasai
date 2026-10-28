using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using static SchoolKit;

/// <summary>
/// Tools > Elevator Game > School > Place School Props (4F-5F): builds the 4F (TV studio, sub-control, power lab) and
/// 5F (care room, clinical engineering, IT room, student lounge) props from primitives, saves them as prefabs and
/// places each room's set together on one free spot near an anchor object of the building, facing it.
/// Sizes are real metres (see SchoolKit); a prop's +Z is its front. Placed ids are stable and kept if moved by hand.
/// </summary>
public static class PropsUpper
{
    const string Title = "Place School Props (4F-5F)";
    const float M = SchoolPalette.WorldPerMetre;
    static readonly string[] StudioAnchor = { "swBtn22" };

    [MenuItem("Tools/Elevator Game/School/" + Title)]
    static void Menu() => OnOpenFloor(Title, (scene, floor) =>
    {
        if (floor < 4) Debug.Log($"{Title}: floor {floor} is dressed by PropsLower (1F-3F); nothing to do here.");
        else Apply(scene, floor);
    });

    /// <summary>Places the props of floor 4 or 5 in `scene`; does nothing for other floors.</summary>
    internal static void Apply(Scene scene, int floor)
    {
        if (floor == 4) { Studio(scene); SubControl(scene); PowerLab(scene); }
        else if (floor == 5) { CareRoom(scene); ClinicalRoom(scene); ItRoom(scene); Lounge(scene); }
        else return;
        AssetDatabase.SaveAssets();
    }

    // ---------- room sets (offsets in metres in the set's frame; the set's +Z faces its anchor) ----------

    static void Studio(Scene scene)
    {
        var cam = StudioCamera();
        var items = new List<Item>();
        float[] xs = { -2.1f, -0.7f, 0.7f, 2.1f };
        const float cycZ = 1.8f;
        for (int i = 0; i < xs.Length; i++)
        {
            float z = Mathf.Abs(xs[i]) > 1f ? -1.9f : -1.5f;   // a shallow arc, all aimed at the cyclorama
            items.Add(It("studioCam" + (i + 1), cam, xs[i], z, Mathf.Atan2(-xs[i], cycZ - z) * Mathf.Rad2Deg));
        }
        items.Add(It("studioGrid", LightingGrid(), 0f, -0.4f));
        items.Add(It("studioCyc", Cyclorama(), 0f, cycZ, 180f));
        items.Add(It("studioMonitor75", Monitor75(), 2.6f, -0.2f));
        PlaceGroup(scene, "TV studio", StudioAnchor, V(7.2f, 3.6f, 4.6f), 12f, 70f, items.ToArray());
        PlaceGroup(scene, "studio curtain", StudioAnchor, V(4.2f, 3.6f, 0.6f), 12f, 70f, It("studioCurtain", Curtain(), 0f, 0f));
    }

    static void SubControl(Scene scene)
    {
        var chair = OfficeChair("F4_CrimsonChair", A(4, "crimson"), A(4, "control"));
        var rack = Rack();
        PlaceGroup(scene, "sub-control", new[] { "chair_frame", "stool00_1_seat", "swBtn22" }, V(5.8f, 2.8f, 3.2f), 6f, 60f,
            It("subMonitorWall", MonitorWall(), 0f, -1.3f), It("subConsole", ConsoleDesk(), 0f, -0.6f),
            It("subRack1", rack, -2.55f, -1.1f), It("subRack2", rack, 2.55f, -1.1f),
            It("subChair1", chair, -0.9f, 0.4f, 180f), It("subChair2", chair, 0.9f, 0.4f, 180f));
    }

    static void PowerLab(Scene scene)
    {
        PlaceGroup(scene, "power lab", new[] { "cabinet", "locker_topbox", "shelf_board0" }, V(5.4f, 3.4f, 3.4f), 6f, 60f,
            It("powerFrame", PowerFrame(), -1.0f, 0f), It("powerMeterPanel", MeterPanel(), 1.0f, -1.3f),
            It("powerSwitchgear", Switchgear(), 2.1f, -1.1f),
            It("powerSign", Sign("F4_PowerLabSign", "電力実験室\nPOWER LAB", A(4, "control")), 1.6f, 1.2f));
    }

    static void CareRoom(Scene scene)
    {
        var bed = Bed("F5_CareBed", White(), A(5, "lavender"), A(5, "bedWood"));
        var chair = ShowerChair("F5_ShowerChair", White());
        var orange = ShowerChair("F5_ShowerChairOrange", H("F5_orange", "#E8803A", 0.4f));
        PlaceGroup(scene, "care room", new[] { "bed" }, V(5.6f, 2.1f, 3.6f), 6f, 60f,
            It("careBed1", bed, -1.7f, -0.6f), It("careBed2", bed, -0.5f, -0.6f), It("careBed3", bed, 0.7f, -0.6f),
            It("careLift", PatientLift(), 2.1f, -0.8f),
            It("careShower1", chair, -1.5f, 1.3f), It("careShower2", chair, -0.8f, 1.3f), It("careShower3", orange, -0.1f, 1.3f),
            It("careSign", Sign("F5_CareSign", "介護実習室\nCARE ROOM", A(5, "grey")), 1.9f, 1.3f));
    }

    static void ClinicalRoom(Scene scene)
    {
        var machine = MedicalMachine();
        var iv = IvPole();
        PlaceGroup(scene, "clinical engineering", new[] { "bodyBack" }, V(5.6f, 2.1f, 3.6f), 6f, 60f,
            It("ceBed", Bed("F5_HospitalBed", A(5, "bedGold", 0.45f, 0.5f), White(), Medical()), -1.8f, -0.6f),
            It("ceMachine1", machine, -0.6f, -1.3f), It("ceMachine2", machine, 0f, -1.3f), It("ceMachine3", machine, 0.6f, -1.3f),
            It("ceIv1", iv, 1.3f, -0.5f), It("ceIv2", iv, 1.9f, -0.5f), It("ceIv3", iv, 2.5f, -0.5f),
            It("ceTrolley", Trolley(), 0.2f, 0.5f), It("ceLamp", ExamLamp(), -0.7f, -0.2f, -90f),
            It("ceSign", Sign("F5_ClinicalSign", "臨床工学実習室\nCLINICAL ENG.", A(5, "grey")), 2.0f, 1.2f));
    }

    static void ItRoom(Scene scene)
    {
        var desk = ItDesk();
        var chair = OfficeChair("F5_BlueMeshChair", H("F5_blueMesh", "#2A5CC8", 0.2f), Black5());
        PlaceGroup(scene, "IT room", new[] { "laptopLogo", "monitot" }, V(4.2f, 2.1f, 4.2f), 6f, 60f,
            It("itDesk1", desk, 0f, -1.3f), It("itDesk2", desk, 0f, 0.8f),
            It("itChair1", chair, -0.45f, -0.55f, 180f), It("itChair2", chair, 0.45f, -0.55f, 180f),
            It("itChair3", chair, -0.45f, 1.55f, 180f), It("itChair4", chair, 0.45f, 1.55f, 180f),
            It("itSign", Sign("F5_ItSign", "ITエンジニア\nIT ENGINEER", A(5, "grey")), 1.6f, -0.25f));
    }

    static void Lounge(Scene scene)
    {
        var table = LoungeTable();
        var chairs = new[] { "teal", "mauve", "grey", "maroon" }.Select(c => LoungeChair("F5_LoungeChair_" + c, A(5, c))).ToArray();
        var items = new List<Item>();
        for (int t = 0; t < 2; t++)
        {
            float tx = t == 0 ? -1.3f : 1.3f;
            items.Add(It("loungeTable" + (t + 1), table, tx, 0f));
            for (int c = 0; c < 4; c++)
            {
                float z = c < 2 ? -0.7f : 0.7f;   // two chairs on each long side, facing the table
                items.Add(It("loungeChair" + (t * 4 + c + 1), chairs[(t + c) % 4], tx + (c % 2 == 0 ? -0.45f : 0.45f), z, z < 0f ? 0f : 180f));
            }
        }
        PlaceGroup(scene, "student lounge", new[] { "lunch-table" }, V(5.2f, 1.0f, 2.4f), 6f, 60f, items.ToArray());
    }

    // ---------- placement ----------

    readonly struct Item
    {
        public readonly string id;
        public readonly GameObject prefab;
        public readonly float x, z, yaw;

        public Item(string id, GameObject prefab, float x, float z, float yaw)
        {
            this.id = id; this.prefab = prefab; this.x = x; this.z = z; this.yaw = yaw;
        }
    }

    static Item It(string id, GameObject prefab, float x, float z, float yaw = 0f) => new Item(id, prefab, x, z, yaw);

    /// <summary>Finds one free spot for the whole set (footprint `sizeM`) near the first anchor found, then places its items.</summary>
    static void PlaceGroup(Scene scene, string group, string[] anchors, Vector3 sizeM, float minDist, float maxDist, params Item[] items)
    {
        if (items.All(i => IsPlaced(scene, i.id))) { Debug.Log($"{Title}: {group} already placed; kept as it is."); return; }
        var anchor = AnchorPoint(scene, anchors, out string used);
        string near = used ?? "the building centre";
        if (used == null)
        {
            maxDist *= 2f;
            Debug.LogWarning($"{Title}: {group}: no {string.Join(" / ", anchors)} in the building; searching from the building centre.");
        }
        if (!FindGroupSpot(scene, anchor, sizeM, minDist, maxDist, out var spot, out float yaw))
        {
            Debug.LogWarning($"{Title}: no free spot for {group} near {near} ({anchor}).");
            return;
        }
        var rot = Quaternion.Euler(0f, yaw, 0f);
        foreach (var it in items)
        {
            if (IsPlaced(scene, it.id)) continue;
            var go = Place(scene, it.id, it.prefab, spot + rot * new Vector3(it.x, 0f, it.z) * M, yaw + it.yaw);
            if (go != null) Debug.Log($"{Title}: placed {it.id} ({group}, near {near}) at {go.transform.position}.");
        }
    }

    // Tries the four 90° turns; prefers a spot whose set front then faces the anchor, else the first that fits
    static bool FindGroupSpot(Scene scene, Vector3 anchor, Vector3 sizeM, float minDist, float maxDist, out Vector3 spot, out float yaw)
    {
        spot = default;
        yaw = 0f;
        bool found = false;
        for (int k = 0; k < 4; k++)
        {
            float turn = k * 90f;
            var s = FloorSpot(scene, anchor, sizeM, turn, minDist, maxDist);
            if (s == null) continue;
            if (!found) { found = true; spot = s.Value; yaw = turn; }
            if (Mathf.Approximately(FacingYaw(s.Value, anchor), turn)) { spot = s.Value; yaw = turn; return true; }
        }
        return found;
    }

    // FreeSpot, skipping spots found on furniture tops: only the anchor's floor level counts
    static Vector3? FloorSpot(Scene scene, Vector3 anchor, Vector3 sizeM, float yaw, float minDist, float maxDist)
    {
        for (float from = minDist; from <= maxDist;)
        {
            var s = FreeSpot(scene, anchor, sizeM, yaw, from, maxDist);
            if (s == null || s.Value.y < anchor.y - 1f) return s;
            var flat = s.Value - anchor;
            flat.y = 0f;
            from = flat.magnitude + 1.5f;
        }
        return null;
    }

    /// <summary>The yaw (snapped to 90°) that turns a prop's +Z at `from` towards `to`.</summary>
    static float FacingYaw(Vector3 from, Vector3 to) =>
        Mathf.Repeat(Mathf.Round(Mathf.Atan2(to.x - from.x, to.z - from.z) * Mathf.Rad2Deg / 90f) * 90f, 360f);

    // Centre of the first matching anchor (else of the whole building), 2 units above the floor level
    static Vector3 AnchorPoint(Scene scene, string[] prefixes, out string used)
    {
        var character = FloorPlayerSetup.FindCharacter(scene);
        Bounds? box = null;
        used = null;
        foreach (var prefix in prefixes)
        {
            var hits = FindInBuilding(scene, prefix);
            if (hits.Count == 0) continue;
            used = prefix;
            box = BoundsOf(new[] { hits[0].gameObject }) ?? new Bounds(hits[0].position, Vector3.zero);
            break;
        }
        if (box == null) box = BoundsOf(FloorPlayerSetup.BuildingRoots(scene, character != null ? character.transform : null)) ?? new Bounds();
        var b = box.Value;
        return new Vector3(b.center.x, FloorY(scene, character, b.min.y) + 2f, b.center.z);
    }

    // The floor under the player (one level per floor scene); `fallback` when there's no player
    static float FloorY(Scene scene, GameObject character, float fallback)
    {
        if (character == null) return fallback;
        var origin = character.transform.position + Vector3.up;
        return scene.GetPhysicsScene().Raycast(origin, Vector3.down, out var hit, 40f, ~0, QueryTriggerInteraction.Ignore)
            ? hit.point.y : character.transform.position.y;
    }

    static Bounds? BoundsOf(IEnumerable<GameObject> roots)
    {
        Bounds? total = null;
        foreach (var r in roots.SelectMany(g => g.GetComponentsInChildren<Renderer>()))
        {
            if (total == null) { total = r.bounds; continue; }
            var b = total.Value;
            b.Encapsulate(r.bounds);
            total = b;
        }
        return total;
    }

    // ---------- materials and shared parts ----------

    static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);
    static Material A(int floor, string key, float smooth = 0.25f, float metal = 0f, bool glow = false) => Accent(floor, key, smooth, metal, glow);
    static Material H(string key, string hex, float smooth = 0.3f, float metal = 0f, bool glow = false) =>
        Mat(key, UiKit.Hex(hex), TexKind.Plain, smooth, metal, null, glow);

    static Material Dark4() => A(4, "grid", 0.4f, 0.4f);
    static Material Grey4() => A(4, "camera", 0.35f, 0.3f);
    static Material Screen4() => H("F4_screenBlue", "#142A60", 0.8f, 0f, true);
    static Material CableRed() => H("F4_cableRed", "#B01818", 0.4f);
    static Material Porcelain() => A(4, "porcelain", 0.7f);
    static Material White() => H("F5_white", "#F4F4F2", 0.4f);
    static Material Medical() => A(5, "medical", 0.45f);
    static Material Chrome() => A(5, "steel", 0.7f, 0.9f);
    static Material Black5() => A(5, "black", 0.4f);
    static Material Oak5() => H("F5_oak", "#D8BC8C", 0.35f);
    static Material Screen5() => H("F5_screen", "#0E2226", 0.85f, 0f, true);

    // four small castor wheels under the corners of a w x d footprint
    static void Castors(GameObject p, float w, float d, Material mat)
    {
        for (int i = 0; i < 4; i++)
            Cyl(p, "castor" + i, V((i % 2 == 0 ? -0.5f : 0.5f) * w, 0f, (i < 2 ? -0.5f : 0.5f) * d), 0.08f, 0.07f, mat);
    }

    // five-star office chair (gas column, arms) in `seat` upholstery
    static GameObject OfficeChair(string id, Material seat, Material frame)
    {
        var p = NewProp(id);
        for (int i = 0; i < 3; i++) Box(p, "star" + i, V(0f, 0.09f, 0f), V(0.62f, 0.04f, 0.06f), frame, V(0f, i * 60f, 0f));
        Castors(p, 0.5f, 0.5f, frame);
        Cyl(p, "column", Vector3.zero, 0.06f, 0.36f, frame, 0.1f);
        Box(p, "seat", V(0f, 0.5f, 0.02f), V(0.5f, 0.08f, 0.48f), seat);
        Box(p, "backPost", V(0f, 0.62f, -0.25f), V(0.06f, 0.3f, 0.04f), frame);
        Box(p, "back", V(0f, 0.86f, -0.23f), V(0.46f, 0.55f, 0.06f), seat, V(-8f, 0f, 0f));
        for (int s = -1; s <= 1; s += 2) Box(p, "arm" + s, V(0.26f * s, 0.67f, 0f), V(0.04f, 0.04f, 0.3f), frame);
        return SavePrefab(p);
    }

    // free-standing room sign: two-line bilingual text on a white board
    static GameObject Sign(string id, string text, Material frame)
    {
        var p = NewProp(id);
        Box(p, "foot", V(0f, 0.02f, 0f), V(0.5f, 0.04f, 0.35f), frame);
        Cyl(p, "post", Vector3.zero, 0.05f, 1.5f, frame, 0.04f);
        Box(p, "board", V(0f, 1.75f, 0f), V(1.5f, 0.55f, 0.04f), H("F45_signBoard", "#F6F6F4", 0.3f));
        Box(p, "stripe", V(0f, 1.49f, 0.005f), V(1.5f, 0.04f, 0.045f), H("F45_signStripe", "#2A5CC8", 0.3f));
        Label(p, "text", text, V(0f, 1.76f, 0.03f), 0.13f, UiKit.Hex("#1E2A4A"), V(0f, 180f, 0f));
        return SavePrefab(p);
    }

    // ---------- 4F: TV studio ----------

    static GameObject StudioCamera()
    {
        var p = NewProp("F4_StudioCamera");
        Material body = Grey4(), dark = Dark4(), gold = A(4, "pedestal", 0.45f, 0.6f);
        Cyl(p, "pedestal", Vector3.zero, 0.7f, 0.3f, gold);
        Cyl(p, "column", Vector3.zero, 0.14f, 0.85f, body, 0.3f);
        Cyl(p, "steeringRing", Vector3.zero, 0.55f, 0.03f, gold, 0.9f);
        Box(p, "panHead", V(0f, 1.18f, 0f), V(0.3f, 0.06f, 0.4f), dark);
        Box(p, "body", V(0f, 1.38f, 0f), V(0.35f, 0.35f, 0.6f), body);
        Part(p, "lens", PrimitiveType.Cylinder, V(0f, 1.38f, 0.4f), V(0.16f, 0.22f, 0.16f), dark, V(90f, 0f, 0f));
        Box(p, "lensHood", V(0f, 1.38f, 0.52f), V(0.22f, 0.2f, 0.04f), dark);
        Box(p, "viewfinder", V(0f, 1.64f, -0.12f), V(0.24f, 0.17f, 0.24f), dark);
        Box(p, "tally", V(0f, 1.75f, -0.05f), V(0.08f, 0.05f, 0.08f), A(4, "tally", 0.5f, 0f, true));
        for (int s = -1; s <= 1; s += 2)
            Box(p, "panBar" + s, V(0.13f * s, 1.25f, -0.52f), V(0.03f, 0.03f, 0.5f), dark, V(-12f, 0f, 0f));
        return SavePrefab(p);
    }

    // 4 x 4 m pipe grid at 4.2 m with eight fresnels tilted towards the set's front; not solid (overhead)
    static GameObject LightingGrid()
    {
        var p = NewProp("F4_LightingGrid");
        Material pipe = Dark4(), lens = H("F4_fresnelLens", "#5A5040", 0.6f, 0f, true);
        for (int i = 0; i <= 4; i++)
        {
            Box(p, "pipeZ" + i, V(-2f + i, 4.2f, 0f), V(0.05f, 0.05f, 4f), pipe);
            Box(p, "pipeX" + i, V(0f, 4.2f, -2f + i), V(4f, 0.05f, 0.05f), pipe);
        }
        for (int i = 0; i < 4; i++)
            Box(p, "hanger" + i, V(i % 2 == 0 ? -2f : 2f, 4.34f, i < 2 ? -2f : 2f), V(0.03f, 0.25f, 0.03f), pipe);
        for (int i = 0; i < 8; i++) Fresnel(p, "fresnel" + i, -1.5f + i % 4, i < 4 ? -1f : 1f, pipe, lens);
        return SavePrefab(p, false);
    }

    static void Fresnel(GameObject p, string name, float x, float z, Material body, Material lens)
    {
        var forward = V(0f, -0.574f, 0.819f);   // 35° down towards +Z
        var at = V(x, 3.84f, z);
        Box(p, name + "Yoke", V(x, 4.04f, z), V(0.03f, 0.3f, 0.03f), body);
        Box(p, name, at, V(0.28f, 0.28f, 0.36f), body, V(35f, 0f, 0f));
        Part(p, name + "Lens", PrimitiveType.Cylinder, at + forward * 0.19f, V(0.22f, 0.02f, 0.22f), lens, V(125f, 0f, 0f));
    }

    static GameObject Cyclorama()
    {
        var p = NewProp("F4_Cyclorama");
        var cyc = A(4, "cyc", 0.1f, 0f, true);
        Box(p, "panel", V(0f, 1.85f, 0f), V(6f, 3.5f, 0.1f), cyc);
        Box(p, "cove", V(0f, 0.05f, 0.25f), V(6f, 0.1f, 0.4f), cyc);
        for (int s = -1; s <= 1; s += 2) Box(p, "brace" + s, V(2.7f * s, 0.6f, -0.25f), V(0.08f, 1.2f, 0.4f), Dark4());
        return SavePrefab(p);
    }

    // black velvet on a rail: six folds, alternately set forward/back and angled
    static GameObject Curtain()
    {
        var p = NewProp("F4_Curtain");
        var cloth = A(4, "curtain", 0.05f);
        Box(p, "rail", V(0f, 3.55f, 0f), V(4.1f, 0.06f, 0.06f), Dark4());
        const float fold = 4f / 6f;
        for (int i = 0; i < 6; i++)
            Box(p, "fold" + i, V(-2f + fold * (i + 0.5f), 1.76f, i % 2 == 0 ? 0.04f : -0.04f), V(fold + 0.04f, 3.48f, 0.07f),
                cloth, V(0f, i % 2 == 0 ? 8f : -8f, 0f));
        return SavePrefab(p);
    }

    static GameObject Monitor75()
    {
        var p = NewProp("F4_Monitor75");
        Material grey = Grey4(), dark = Dark4();
        Box(p, "base", V(0f, 0.1f, 0f), V(0.9f, 0.05f, 0.6f), grey);
        Castors(p, 0.8f, 0.5f, dark);
        Box(p, "post", V(0f, 0.65f, -0.05f), V(0.12f, 1.1f, 0.1f), grey);
        Box(p, "bezel", V(0f, 1.6f, 0f), V(1.7f, 1.0f, 0.08f), dark);
        Box(p, "screen", V(0f, 1.6f, 0.042f), V(1.62f, 0.92f, 0.01f), Screen4());
        return SavePrefab(p);
    }

    // ---------- 4F: sub-control ----------

    static GameObject ConsoleDesk()
    {
        var p = NewProp("F4_ConsoleDesk");
        Box(p, "body", V(0f, 0.43f, -0.05f), V(4f, 0.86f, 0.7f), A(4, "console"));
        Box(p, "top", V(0f, 0.88f, 0f), V(4f, 0.04f, 0.8f), A(4, "control"));
        Box(p, "armrest", V(0f, 0.88f, 0.41f), V(4f, 0.06f, 0.08f), Dark4());
        ConsoleControls(p);
        return SavePrefab(p);
    }

    // 24 fader strips near the operator (+Z) with caps at varied levels, three rows of lit buttons behind them
    static void ConsoleControls(GameObject p)
    {
        Material strip = A(4, "fader"), cap = H("F4_faderCap", "#C8CACC", 0.4f);
        Material[] buttons =
        {
            H("F4_btnRed", "#D02020", 0.5f, 0f, true), H("F4_btnYellow", "#E8C020", 0.5f, 0f, true), H("F4_btnBlue", "#2050D0", 0.5f, 0f, true),
        };
        for (int c = 0; c < 24; c++)
        {
            float x = -1.84f + c * 0.16f;
            Box(p, "fader" + c, V(x, 0.905f, 0.15f), V(0.06f, 0.01f, 0.36f), strip);
            Box(p, "cap" + c, V(x, 0.92f, 0.15f + (c * 7 % 5 - 2) * 0.06f), V(0.05f, 0.03f, 0.03f), cap);
            for (int r = 0; r < 3; r++)
                Box(p, $"button{c}_{r}", V(x, 0.91f, -0.12f - r * 0.08f), V(0.045f, 0.025f, 0.045f), buttons[(c + r) % 3]);
        }
    }

    // rail frame holding 2 rows x 6 small monitors with dim blue screens
    static GameObject MonitorWall()
    {
        var p = NewProp("F4_MonitorWall");
        Material frame = A(4, "control"), dark = Dark4(), screen = Screen4();
        for (int s = -1; s <= 1; s += 2)
        {
            Box(p, "post" + s, V(2.0f * s, 1.4f, 0f), V(0.08f, 2.8f, 0.08f), frame);
            Box(p, "foot" + s, V(2.0f * s, 0.02f, 0f), V(0.1f, 0.04f, 0.5f), frame);
        }
        foreach (float y in new[] { 1.28f, 1.92f, 2.56f }) Box(p, "rail" + y, V(0f, y, -0.05f), V(4.08f, 0.06f, 0.04f), frame);
        for (int r = 0; r < 2; r++)
            for (int i = 0; i < 6; i++)
            {
                var at = V(-1.65f + i * 0.66f, 1.6f + r * 0.64f, 0f);
                Box(p, $"monitor{r}_{i}", at, V(0.62f, 0.4f, 0.06f), dark);
                Box(p, $"screen{r}_{i}", at + V(0f, 0f, 0.032f), V(0.56f, 0.34f, 0.01f), screen);
            }
        return SavePrefab(p);
    }

    // cream 19" rack with rack-unit lines, some fitted units and status LEDs
    static GameObject Rack()
    {
        var p = NewProp("F4_EquipmentRack");
        Material dark = A(4, "control"), led = H("F4_led", "#30D060", 0.5f, 0f, true);
        Box(p, "cabinet", V(0f, 1f, 0f), V(0.6f, 2f, 0.8f), A(4, "rack"));
        for (int u = 0; u < 16; u++)
        {
            float y = 0.16f + u * 0.11f;
            Box(p, "unitLine" + u, V(0f, y, 0.402f), V(0.52f, 0.012f, 0.006f), dark);
            if (u % 3 != 0) continue;
            Box(p, "unit" + u, V(0f, y + 0.055f, 0.403f), V(0.5f, 0.08f, 0.008f), dark);
            Box(p, "led" + u, V(0.2f, y + 0.055f, 0.409f), V(0.02f, 0.02f, 0.006f), led);
        }
        return SavePrefab(p);
    }

    // ---------- 4F: electric power lab ----------

    // 3 x 3 x 3 m galvanised frame, mesh cage on three sides, two transformers, insulators and cables
    static GameObject PowerFrame()
    {
        var p = NewProp("F4_PowerLabFrame");
        FramePipes(p, A(4, "galv", 0.4f, 0.7f));
        var cage = H("F4_cageMesh", "#5A5E62", 0.3f, 0.6f);
        CagePanel(p, "cageBack", V(0f, 0f, -1.5f), 3f, false, cage);
        CagePanel(p, "cageLeft", V(-1.5f, 0f, 0f), 3f, true, cage);
        CagePanel(p, "cageRight", V(1.5f, 0f, 0f), 3f, true, cage);
        Transformer(p, "transformerA", -0.6f, -0.6f);
        Transformer(p, "transformerB", 0.6f, -0.6f);
        InsulatorsAndCables(p);
        return SavePrefab(p);
    }

    static void FramePipes(GameObject p, Material galv)
    {
        for (int i = 0; i < 4; i++) Cyl(p, "post" + i, V(i % 2 == 0 ? -1.5f : 1.5f, 0f, i < 2 ? -1.5f : 1.5f), 0.08f, 3f, galv);
        for (int s = -1; s <= 1; s += 2)
        {
            Part(p, "beamX" + s, PrimitiveType.Cylinder, V(0f, 3f, 1.5f * s), V(0.08f, 3f, 0.08f), galv, V(0f, 0f, 90f));
            Part(p, "beamZ" + s, PrimitiveType.Cylinder, V(1.5f * s, 3f, 0f), V(0.08f, 3f, 0.08f), galv, V(90f, 0f, 0f));
            Part(p, "railZ" + s, PrimitiveType.Cylinder, V(1.5f * s, 1.2f, 0f), V(0.06f, 3f, 0.06f), galv, V(90f, 0f, 0f));
        }
        Part(p, "beamMid", PrimitiveType.Cylinder, V(0f, 3f, 0f), V(0.08f, 3f, 0.08f), galv, V(0f, 0f, 90f));
    }

    // safety mesh: thin bars every 0.25 m across and 0.3 m up, 2.4 m tall
    static void CagePanel(GameObject p, string name, Vector3 at, float width, bool alongZ, Material mat)
    {
        const float height = 2.4f;
        int bars = Mathf.RoundToInt(width / 0.25f);
        for (int i = 0; i <= bars; i++)
        {
            float t = -width / 2f + i * 0.25f;
            Box(p, $"{name}V{i}", at + (alongZ ? V(0f, height / 2f, t) : V(t, height / 2f, 0f)), V(0.012f, height, 0.012f), mat);
        }
        for (int j = 1; j <= 8; j++)
            Box(p, $"{name}H{j}", at + V(0f, j * 0.3f, 0f), alongZ ? V(0.012f, 0.012f, width) : V(width, 0.012f, 0.012f), mat);
    }

    // grey finned transformer with three porcelain bushings on top
    static void Transformer(GameObject p, string name, float x, float z)
    {
        var body = H("F4_transformer", "#7A7E84", 0.35f, 0.4f);
        Box(p, name, V(x, 0.3f, z), V(0.5f, 0.6f, 0.5f), body);
        for (int i = 0; i < 5; i++)
            for (int s = -1; s <= 1; s += 2)
                Box(p, $"{name}Fin{i}_{s}", V(x + 0.28f * s, 0.3f, z - 0.2f + i * 0.1f), V(0.06f, 0.5f, 0.02f), body);
        for (int i = -1; i <= 1; i++) Cyl(p, $"{name}Bushing{i}", V(x + i * 0.14f, 0f, z), 0.06f, 0.16f, Porcelain(), 0.6f);
    }

    static void InsulatorsAndCables(GameObject p)
    {
        Material copper = A(4, "copper", 0.5f, 0.8f), black = H("F4_cableBlack", "#151515", 0.4f);
        for (int i = 0; i < 5; i++)
        {
            float x = -1.2f + i * 0.6f;
            for (int d = 0; d < 4; d++) Cyl(p, $"insulator{i}_{d}", V(x, 0f, 0f), 0.16f, 0.04f, Porcelain(), 3.04f + d * 0.07f);
            Cyl(p, "cap" + i, V(x, 0f, 0f), 0.05f, 0.06f, copper, 3.3f);
        }
        for (int s = -1; s <= 1; s += 2)
        {
            var cable = s < 0 ? CableRed() : black;
            Box(p, "cableDrop" + s, V(0.6f * s, 1.92f, -0.3f), V(0.03f, 2.2f, 0.03f), cable);
            Box(p, "cableRun" + s, V(0.6f * s, 3.02f, -0.15f), V(0.03f, 0.03f, 0.33f), cable);
        }
    }

    // cream control panel: four round meters (black rims, needles) and a row of toggle switches
    static GameObject MeterPanel()
    {
        var p = NewProp("F4_MeterPanel");
        Material face = H("F4_dialFace", "#F6F6F0", 0.6f), dark = Dark4();
        Box(p, "cabinet", V(0f, 1.3f, 0f), V(0.8f, 2.6f, 0.3f), A(4, "meterPanel"));
        for (int i = 0; i < 4; i++)
        {
            float x = i % 2 == 0 ? -0.18f : 0.18f, y = i < 2 ? 2.0f : 1.6f;
            Part(p, "rim" + i, PrimitiveType.Cylinder, V(x, y, 0.16f), V(0.3f, 0.03f, 0.3f), dark, V(90f, 0f, 0f));
            Part(p, "dial" + i, PrimitiveType.Cylinder, V(x, y, 0.18f), V(0.26f, 0.02f, 0.26f), face, V(90f, 0f, 0f));
            Box(p, "needle" + i, V(x, y + 0.04f, 0.192f), V(0.01f, 0.1f, 0.004f), dark, V(0f, 0f, -25f + i * 15f));
        }
        for (int i = 0; i < 4; i++)
        {
            float x = -0.27f + i * 0.18f;
            Box(p, "switchPlate" + i, V(x, 1.15f, 0.155f), V(0.09f, 0.13f, 0.01f), dark);
            Box(p, "switch" + i, V(x, 1.17f, 0.18f), V(0.025f, 0.07f, 0.04f), i % 2 == 0 ? CableRed() : dark, V(-30f, 0f, 0f));
        }
        Box(p, "nameplate", V(0f, 2.35f, 0.152f), V(0.5f, 0.08f, 0.005f), dark);
        return SavePrefab(p);
    }

    static GameObject Switchgear()
    {
        var p = NewProp("F4_Switchgear");
        var dark = A(4, "control");
        Box(p, "plinth", V(0f, 0.04f, 0f), V(1.02f, 0.08f, 1.02f), dark);
        Box(p, "cabinet", V(0f, 0.54f, 0f), V(1f, 0.92f, 1f), H("F4_switchgear", "#8A9096", 0.35f, 0.5f));
        Box(p, "seam", V(0f, 0.54f, 0.502f), V(0.008f, 0.86f, 0.006f), dark);
        for (int s = -1; s <= 1; s += 2) Box(p, "handle" + s, V(0.07f * s, 0.6f, 0.52f), V(0.03f, 0.16f, 0.03f), dark);
        for (int i = 0; i < 4; i++) Box(p, "vent" + i, V(0.25f, 0.2f + i * 0.04f, 0.502f), V(0.3f, 0.015f, 0.006f), dark);
        return SavePrefab(p);
    }

    // ---------- 5F: care room / clinical engineering ----------

    // wheeled bed, foot end at +Z, head section raised; also the clinical hospital bed (gold frame)
    static GameObject Bed(string id, Material frame, Material mattress, Material boards)
    {
        var p = NewProp(id);
        var chrome = Chrome();
        Castors(p, 0.8f, 1.8f, Black5());
        for (int i = 0; i < 4; i++)
            Box(p, "leg" + i, V(i % 2 == 0 ? -0.4f : 0.4f, 0.2f, i < 2 ? -0.9f : 0.9f), V(0.05f, 0.26f, 0.05f), chrome);
        Box(p, "frame", V(0f, 0.38f, 0f), V(0.9f, 0.1f, 1.95f), frame);
        Box(p, "mattress", V(0f, 0.52f, 0.1f), V(0.86f, 0.18f, 1.7f), mattress);
        Box(p, "backrest", V(0f, 0.62f, -0.82f), V(0.86f, 0.16f, 0.4f), mattress, V(25f, 0f, 0f));
        Box(p, "pillow", V(0f, 0.78f, -0.86f), V(0.5f, 0.1f, 0.3f), White(), V(25f, 0f, 0f));
        Box(p, "headboard", V(0f, 0.65f, -1.0f), V(0.96f, 0.62f, 0.05f), boards);
        Box(p, "footboard", V(0f, 0.58f, 1.0f), V(0.96f, 0.46f, 0.05f), boards);
        for (int s = -1; s <= 1; s += 2) Box(p, "sideRail" + s, V(0.47f * s, 0.72f, -0.35f), V(0.03f, 0.22f, 0.9f), chrome);
        return SavePrefab(p);
    }

    // mobile patient lift: U base on castors, mast, boom (+Z) with a spreader bar and a yellow/blue sling
    static GameObject PatientLift()
    {
        var p = NewProp("F5_PatientLift");
        Material white = White(), chrome = Chrome(), dark = Black5();
        for (int s = -1; s <= 1; s += 2) Box(p, "leg" + s, V(0.3f * s, 0.08f, 0.25f), V(0.06f, 0.06f, 1.0f), white);
        Box(p, "rearBar", V(0f, 0.08f, -0.25f), V(0.66f, 0.06f, 0.06f), white);
        for (int i = 0; i < 4; i++) Cyl(p, "castor" + i, V(i % 2 == 0 ? -0.3f : 0.3f, 0f, i < 2 ? -0.22f : 0.72f), 0.08f, 0.05f, dark);
        Box(p, "mast", V(0f, 0.8f, -0.25f), V(0.08f, 1.4f, 0.08f), white);
        Box(p, "boom", V(0f, 1.48f, 0.2f), V(0.07f, 0.07f, 0.95f), white);
        Box(p, "actuator", V(0f, 1.1f, -0.08f), V(0.05f, 0.6f, 0.05f), chrome, V(-30f, 0f, 0f));
        Box(p, "handle", V(0f, 1.15f, -0.32f), V(0.5f, 0.035f, 0.035f), chrome);
        Box(p, "strap", V(0f, 1.33f, 0.62f), V(0.02f, 0.24f, 0.02f), dark);
        Box(p, "spreader", V(0f, 1.2f, 0.62f), V(0.5f, 0.04f, 0.04f), chrome);
        Box(p, "sling", V(0f, 0.88f, 0.62f), V(0.46f, 0.6f, 0.06f), H("F5_slingYellow", "#E8C838", 0.15f));
        Box(p, "slingInner", V(0f, 0.86f, 0.656f), V(0.3f, 0.4f, 0.02f), H("F5_slingBlue", "#3A6AC8", 0.15f));
        return SavePrefab(p);
    }

    static GameObject ShowerChair(string id, Material seat)
    {
        var p = NewProp(id);
        var chrome = Chrome();
        for (int i = 0; i < 4; i++) Cyl(p, "leg" + i, V(i % 2 == 0 ? -0.19f : 0.19f, 0f, i < 2 ? -0.17f : 0.17f), 0.03f, 0.46f, chrome);
        Box(p, "seat", V(0f, 0.48f, 0f), V(0.45f, 0.05f, 0.42f), seat);
        Box(p, "back", V(0f, 0.76f, -0.2f), V(0.42f, 0.3f, 0.03f), seat);
        for (int s = -1; s <= 1; s += 2)
        {
            Box(p, "backPost" + s, V(0.19f * s, 0.66f, -0.19f), V(0.025f, 0.36f, 0.025f), chrome);
            Box(p, "arm" + s, V(0.22f * s, 0.66f, 0f), V(0.03f, 0.03f, 0.38f), chrome);
        }
        return SavePrefab(p);
    }

    // white machine on wheels (dialysis-style): dark screen, pump slots, coloured knobs
    static GameObject MedicalMachine()
    {
        var p = NewProp("F5_MedicalMachine");
        var dark = Black5();
        Castors(p, 0.45f, 0.45f, dark);
        Box(p, "base", V(0f, 0.11f, 0f), V(0.55f, 0.08f, 0.55f), A(5, "grey"));
        Box(p, "body", V(0f, 0.8f, 0f), V(0.5f, 1.3f, 0.5f), Medical());
        Box(p, "screen", V(0f, 1.28f, 0.255f), V(0.36f, 0.24f, 0.01f), Screen5());
        for (int i = 0; i < 2; i++) Box(p, "pump" + i, V(0f, 0.85f - i * 0.2f, 0.255f), V(0.36f, 0.08f, 0.012f), dark);
        Material[] knobs = { H("F5_knobRed", "#D03030", 0.5f), H("F5_knobYellow", "#E8C030", 0.5f), H("F5_knobBlue", "#2A6AD0", 0.5f) };
        for (int i = 0; i < 3; i++)
            Part(p, "knob" + i, PrimitiveType.Cylinder, V(-0.12f + i * 0.12f, 1.06f, 0.265f), V(0.05f, 0.03f, 0.05f), knobs[i], V(90f, 0f, 0f));
        Box(p, "handle", V(0f, 1.47f, -0.15f), V(0.4f, 0.035f, 0.035f), Chrome());
        return SavePrefab(p);
    }

    // chrome drip stand: tripod foot, pole, five hooks, one bag with its line
    static GameObject IvPole()
    {
        var p = NewProp("F5_IvPole");
        Material chrome = Chrome(), bag = H("F5_ivBag", "#E4EEF4", 0.7f);
        for (int i = 0; i < 3; i++) Box(p, "foot" + i, V(0f, 0.04f, 0f), V(0.55f, 0.025f, 0.03f), chrome, V(0f, i * 60f, 0f));
        Cyl(p, "pole", Vector3.zero, 0.025f, 1.9f, chrome, 0.04f);
        for (int i = 0; i < 5; i++)
        {
            float a = i * 72f * Mathf.Deg2Rad;
            Box(p, "hook" + i, V(0.09f * Mathf.Sin(a), 1.93f, 0.09f * Mathf.Cos(a)), V(0.012f, 0.012f, 0.18f), chrome, V(0f, i * 72f, 0f));
        }
        Box(p, "bag", V(0f, 1.78f, 0.17f), V(0.12f, 0.2f, 0.03f), bag);
        Box(p, "line", V(0f, 1.33f, 0.17f), V(0.006f, 0.7f, 0.006f), bag);
        return SavePrefab(p);
    }

    static GameObject Trolley()
    {
        var p = NewProp("F5_Trolley");
        Material yellow = A(5, "trolley", 0.4f), chrome = Chrome();
        Castors(p, 0.52f, 0.38f, Black5());
        for (int i = 0; i < 4; i++)
            Box(p, "post" + i, V(i % 2 == 0 ? -0.28f : 0.28f, 0.53f, i < 2 ? -0.2f : 0.2f), V(0.03f, 0.9f, 0.03f), chrome);
        foreach (float y in new[] { 0.15f, 0.5f, 0.98f }) Box(p, "shelf" + y, V(0f, y, 0f), V(0.6f, 0.03f, 0.45f), yellow);
        Box(p, "drawer", V(0f, 0.86f, 0f), V(0.56f, 0.14f, 0.42f), yellow);
        Box(p, "handle", V(0f, 1.0f, -0.25f), V(0.5f, 0.03f, 0.03f), chrome);
        return SavePrefab(p);
    }

    // examination lamp on a stand, its head out on an arm towards +Z
    static GameObject ExamLamp()
    {
        var p = NewProp("F5_ExamLamp");
        Material white = Medical(), chrome = Chrome();
        for (int i = 0; i < 3; i++) Box(p, "foot" + i, V(0f, 0.04f, 0f), V(0.6f, 0.03f, 0.04f), chrome, V(0f, i * 60f, 0f));
        Cyl(p, "pole", Vector3.zero, 0.035f, 1.55f, chrome, 0.05f);
        Box(p, "arm", V(0f, 1.6f, 0.25f), V(0.04f, 0.04f, 0.55f), white);
        Box(p, "neck", V(0f, 1.57f, 0.5f), V(0.03f, 0.05f, 0.03f), chrome);
        Cyl(p, "head", V(0f, 0f, 0.5f), 0.36f, 0.1f, white, 1.45f);
        Part(p, "lens", PrimitiveType.Cylinder, V(0f, 1.445f, 0.5f), V(0.28f, 0.012f, 0.28f), H("F5_lampLens", "#FFF4D8", 0.8f, 0f, true));
        return SavePrefab(p);
    }

    // ---------- 5F: IT room / lounge ----------

    // light-oak desk with two all-in-one PCs, keyboards and headphones (users sit at +Z)
    static GameObject ItDesk()
    {
        var p = NewProp("F5_ItDesk");
        var leg = Chrome();
        Box(p, "top", V(0f, 0.72f, 0f), V(1.8f, 0.04f, 0.7f), Oak5());
        for (int s = -1; s <= 1; s += 2) Box(p, "leg" + s, V(0.86f * s, 0.35f, 0f), V(0.04f, 0.7f, 0.65f), leg);
        Box(p, "modesty", V(0f, 0.47f, -0.32f), V(1.68f, 0.35f, 0.02f), leg);
        for (int s = -1; s <= 1; s += 2) Workstation(p, "pc" + s, 0.45f * s);
        return SavePrefab(p);
    }

    static void Workstation(GameObject p, string n, float x)
    {
        Material shell = A(5, "imac", 0.6f, 0.3f), black = Black5();
        Box(p, n + "Foot", V(x, 0.745f, -0.14f), V(0.2f, 0.01f, 0.18f), shell);
        Box(p, n + "Stand", V(x, 0.84f, -0.2f), V(0.16f, 0.2f, 0.03f), shell, V(-10f, 0f, 0f));
        Box(p, n + "Body", V(x, 1.06f, -0.18f), V(0.6f, 0.44f, 0.03f), shell);
        Box(p, n + "Screen", V(x, 1.09f, -0.162f), V(0.56f, 0.33f, 0.004f), Screen5());
        Box(p, n + "Keyboard", V(x, 0.75f, 0.1f), V(0.42f, 0.02f, 0.14f), black);
        Box(p, n + "Band", V(x + 0.3f, 0.79f, 0.02f), V(0.03f, 0.03f, 0.16f), black);
        for (int s = -1; s <= 1; s += 2) Cyl(p, n + "Cup" + s, V(x + 0.3f, 0f, 0.02f + 0.08f * s), 0.08f, 0.04f, black, 0.74f);
    }

    static GameObject LoungeTable()
    {
        var p = NewProp("F5_LoungeTable");
        Box(p, "top", V(0f, 0.72f, 0f), V(1.8f, 0.04f, 0.9f), Oak5());
        for (int i = 0; i < 4; i++) Cyl(p, "leg" + i, V(i % 2 == 0 ? -0.8f : 0.8f, 0f, i < 2 ? -0.36f : 0.36f), 0.05f, 0.7f, Chrome());
        return SavePrefab(p);
    }

    static GameObject LoungeChair(string id, Material colour)
    {
        var p = NewProp(id);
        for (int i = 0; i < 4; i++) Cyl(p, "leg" + i, V(i % 2 == 0 ? -0.19f : 0.19f, 0f, i < 2 ? -0.19f : 0.19f), 0.025f, 0.43f, Chrome());
        Box(p, "seat", V(0f, 0.455f, 0f), V(0.44f, 0.05f, 0.44f), colour);
        Box(p, "back", V(0f, 0.72f, -0.21f), V(0.42f, 0.36f, 0.03f), colour, V(-6f, 0f, 0f));
        return SavePrefab(p);
    }
}
