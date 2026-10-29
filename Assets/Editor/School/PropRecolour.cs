using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > Elevator Game > School > Recolour Furniture.
/// The floor models' furniture uses the colours it was exported with. This swaps those embedded materials (matched
/// by name, ignoring ".001"-style suffixes and "pasted__" prefixes) for school materials in the real campus colours,
/// one set per floor (F{floor}_furn_key). The shell (floor, walls, ceiling), outline hulls, the elevator, doors and the
/// vending machines are left alone, and materials that are already school ones are skipped, so it is safe to re-run
/// (each run first rewrites the floor's existing F{floor}_furn_* materials, so colour changes reach swapped slots too).
/// </summary>
public static class PropRecolour
{
    const string Title = "Recolour Furniture";
    const string Cycle = "cycle";   // key for the mixed-colour lounge seats

    static readonly HashSet<string> Shell = new HashSet<string> { "Material", "Material.001", "walls", "walls.009", "M_ceiling" };
    static readonly string[] CycleKeys = { "teal", "mauve", "grey", "maroon" };
    static readonly string[] CycleHex = { "#3AA08A", "#B07890", "#7A7E86", "#6A3A44" };

    readonly struct Rule
    {
        public readonly Regex pattern;
        public readonly int floor;            // 0 = every floor
        public readonly string key, hex, objectPrefix;
        public readonly TexKind tex;
        public readonly float smoothness, metallic;

        public Rule(string pattern, int floor, string key, string hex, TexKind tex = TexKind.Plain,
                    float smoothness = 0.3f, float metallic = 0f, string objectPrefix = null)
        {
            this.pattern = new Regex("^(?:" + pattern + ")$");
            this.floor = floor;
            this.key = key;
            this.hex = hex;
            this.tex = tex;
            this.smoothness = smoothness;
            this.metallic = metallic;
            this.objectPrefix = objectPrefix;
        }
    }

    // First match wins: floor-specific rules come before the shared furniture pack (mat_*; the books mat_b_* keep their colours)
    static readonly Rule[] Rules = Join(new[]
    {
        // 1F lobby
        new Rule("black table", 1, "charcoal", "#2C2C2E"),
        new Rule(@"Material\.002", 1, "tableWhite", "#F0F0EE", objectPrefix: "square-table"),
        new Rule("M_binSteel|M_steel", 1, "steel", "#B8B8B4", TexKind.Plain, 0.6f, 0.8f),
        new Rule(".*", 1, "mat", "#4A4440", TexKind.Plain, 0.05f, 0f, "headShadow"),
        // 2F reception + library: brown library chairs, darker than the beige tables (the ticket machine's mat_blue stays)
        new Rule("M_cabinet", 2, "cabinet", "#F4F4F4"),
        new Rule("mat_chair", 2, "chair", AccentHex(2, "chair"), TexKind.Plain, 0.2f),
        new Rule("mat_blue", 2, "chairSeat", AccentHex(2, "chairSeat"), TexKind.Plain, 0.15f, 0f, "library"),
    },
    Architecture(3),
    new[]
    {
        // 4F studio / sub-control / power lab
        new Rule("fabric_mat", 4, "crimson", "#A8203A", TexKind.Plain, 0.1f),
        new Rule("walnut_mat", 4, "walnut", "#5A3E2C", TexKind.Oak),
        new Rule("deskBlack", 4, "console", "#3A3C40"),
        new Rule("equipGrey", 4, "fader", "#6A6C70", TexKind.Plain, 0.3f, 0.2f),
        new Rule("locker_mat", 4, "locker", "#C8CACC", TexKind.Plain, 0.4f, 0.3f),
        new Rule("tabletop_mat", 4, "tabletop", "#F4F4F4"),
        new Rule("stool_mat", 4, "stool", "#222222"),
        new Rule("shelf_mat|metal_mat", 4, "galv", "#A8ACB0", TexKind.Plain, 0.5f, 0.7f),
        // 5F care / clinical / IT
        new Rule("M_sheet", 5, "lavender", "#9A88C8", TexKind.Plain, 0.1f),
        new Rule("M_bedFrame", 5, "bedGold", "#C8A858", TexKind.Plain, 0.5f, 0.6f),
        new Rule("M_bedBeige", 5, "bedWood", "#D8A070", TexKind.Oak),
        new Rule("M_pillow", 5, "pillow", "#F4F4F4", TexKind.Plain, 0.1f),
        new Rule("M_machine", 5, "medical", "#F2F4F4", TexKind.Plain, 0.4f),
        new Rule("M_dialyzer", 5, "dialyzer", "#E8F0F4", TexKind.Plain, 0.4f),
        new Rule("M_tableTop", 5, "tableTop", "#E4D6B8", TexKind.Oak),
        new Rule("M_seat(Green|Brown|Grey)", 5, Cycle, null, TexKind.Plain, 0.15f),
        new Rule("steelBeige_mat", 5, "meterPanel", "#E6DAB8", TexKind.Plain, 0.4f, 0.3f),
    },
    Architecture(5),   // the rest of 5F's furniture is finished like 3F
    new[]
    {
        // The shared classroom furniture pack, on every floor that has it
        new Rule("mat_oak|mat_wood", 0, "oak", "#D8C8A8", TexKind.Oak),
        new Rule("mat_chair", 0, "chair", "#2A5CC8", TexKind.Plain, 0.2f),
        new Rule("mat_steel", 0, "shelf", "#C8CACC", TexKind.Plain, 0.4f, 0.3f),
        new Rule("mat_chrome", 0, "chrome", "#C8CCD0", TexKind.Plain, 0.7f, 0.9f),
        new Rule("mat_stool", 0, "stool", "#222222"),
    });

    // The 3F architecture rooms' furniture colours, for the given floor
    static Rule[] Architecture(int floor) => new[]
    {
        new Rule("meshSeat_mat", floor, "chair", "#2A5CC8", TexKind.Plain, 0.15f),
        new Rule("mapleTop_mat|dt_wood_mat|oak_mat|chair_wood_mat|mat_oak|mat_wood", floor, "oak", "#D9C3A0", TexKind.Oak),
        new Rule("dt_wood_dark_mat|oak_dark_mat", floor, "oakDark", "#B89A70", TexKind.Oak),
        new Rule("pedestalGrey_mat|silverRod_mat", floor, "chrome", "#C8CCD0", TexKind.Plain, 0.7f, 0.9f),
        new Rule("blackPlastic_mat", floor, "monitor", "#111214", TexKind.Plain, 0.4f),
        new Rule("tableWhite_mat", floor, "drafting", "#F4F4F4"),
        new Rule("M_plywood", floor, "plywood", "#D8C49C", TexKind.Oak, 0.2f),
    };

    static Rule[] Join(params Rule[][] groups) => groups.SelectMany(g => g).ToArray();

    static string AccentHex(int floor, string key) => "#" + ColorUtility.ToHtmlStringRGB(SchoolPalette.Accent(floor, key));

    [MenuItem("Tools/Elevator Game/School/Recolour Furniture")]
    static void RecolourOpen() => EditorKit.OnOpenFloor(Title, Apply);

    [MenuItem("Tools/Elevator Game/School/Recolour Furniture (All Floors)")]
    static void RecolourAll() => EditorKit.ForFloors(Title, 1, 5, Apply);

    [MenuItem("Tools/Elevator Game/School/List Floor Materials")]
    static void ListMaterials()
    {
        var scene = SceneManager.GetActiveScene();
        var counts = new Dictionary<string, int>();
        foreach (var r in Renderers(scene))
            foreach (var name in r.sharedMaterials.Where(m => m != null).Select(m => m.name).Distinct())
                counts[name] = counts.TryGetValue(name, out var n) ? n + 1 : 1;
        var lines = counts.OrderByDescending(p => p.Value).ThenBy(p => p.Key).Select(p => $"  {p.Key} -> {p.Value}");
        Debug.Log($"{scene.name}: {counts.Count} material(s) in the building (name -> renderers):\n" + string.Join("\n", lines));
    }

    /// <summary>Swaps the floor's furniture materials for school ones and logs how many slots each key took.</summary>
    internal static void Apply(Scene scene, int floor)
    {
        var made = Refreshed(floor);
        int refreshed = made.Count;
        var counts = new Dictionary<string, int>();
        int renderersChanged = 0, seat = 0;

        foreach (var r in Renderers(scene).OrderBy(r => EditorKit.PathOf(r.transform)))
        {
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var m = mats[i];
                if (m == null || Shell.Contains(m.name) || IsSchool(m)) continue;
                var rule = Match(BaseName(m.name), floor, r.transform);
                if (rule == null) continue;

                string key = rule.Value.key, hex = rule.Value.hex;
                if (key == Cycle)
                {
                    key = CycleKeys[seat % CycleKeys.Length];
                    hex = CycleHex[seat % CycleHex.Length];
                    seat++;
                }
                mats[i] = MatFor(made, floor, key, hex, rule.Value);
                counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;
                changed = true;
            }
            if (!changed) continue;
            Undo.RecordObject(r, Title);
            r.sharedMaterials = mats;
            renderersChanged++;
        }

        int slots = counts.Values.Sum();
        string detail = string.Join(", ", counts.OrderBy(p => p.Key).Select(p => $"{p.Key} x{p.Value}"));
        Debug.Log($"{scene.name}: recoloured {slots} material slot(s) on {renderersChanged} renderer(s)" +
                  (slots > 0 ? $": {detail}." : " (nothing new to change).") +
                  $" Refreshed {refreshed} existing F{floor}_furn_* material(s) to the current colours. Use List Floor Materials to see what's left. Save the scene.");
    }

    // Slots already swapped are skipped, so a changed colour reaches them only by updating their material: every
    // existing F{floor}_furn_* asset is rewritten from the first rule (in rule order) that uses its key on this floor
    static Dictionary<string, Material> Refreshed(int floor)
    {
        var made = new Dictionary<string, Material>();
        foreach (var rule in Rules.Where(r => r.floor == 0 || r.floor == floor))
        {
            if (rule.key != Cycle) { RefreshOne(made, floor, rule.key, rule.hex, rule); continue; }
            for (int i = 0; i < CycleKeys.Length; i++) RefreshOne(made, floor, CycleKeys[i], CycleHex[i], rule);
        }
        return made;
    }

    static void RefreshOne(Dictionary<string, Material> made, int floor, string key, string hex, Rule rule)
    {
        if (made.ContainsKey(key)) return;
        if (AssetDatabase.LoadAssetAtPath<Material>($"{SchoolKit.MaterialFolder}/{MatKey(floor, key)}.mat") == null) return;
        MatFor(made, floor, key, hex, rule);
    }

    // Every mesh renderer of the building, without outline hulls or anything belonging to the elevator
    static IEnumerable<MeshRenderer> Renderers(Scene scene)
    {
        var character = FloorPlayerSetup.FindCharacter(scene);
        return FloorPlayerSetup.BuildingRoots(scene, character != null ? character.transform : null)
            .SelectMany(root => root.GetComponentsInChildren<MeshRenderer>(true))
            .Where(r => !IsOutlineHull(r) && r.GetComponentInParent<ElevatorPlayZone>(true) == null &&
                        r.GetComponentInParent<ElevatorExitZone>(true) == null);
    }

    static bool IsOutlineHull(MeshRenderer r) =>
        r.name == "Outline" && r.transform.parent != null && r.transform.parent.GetComponent<InteractableOutline>() != null;

    static bool IsSchool(Material m)
    {
        string path = AssetDatabase.GetAssetPath(m);
        return !string.IsNullOrEmpty(path) && path.StartsWith(SchoolKit.MaterialFolder + "/");
    }

    // "pasted__mat_oak1.003 (Instance)" -> "mat_oak1" (pack duplicates keep their digit; patterns allow none, so strip it too)
    static string BaseName(string name)
    {
        name = name.Replace(" (Instance)", "");
        name = Regex.Replace(name, @"\.\d+$", "");
        while (name.StartsWith("pasted__")) name = name.Substring("pasted__".Length);
        return Regex.IsMatch(name, @"^mat_[a-z]+\d$") ? name.Substring(0, name.Length - 1) : name;
    }

    static Rule? Match(string baseName, int floor, Transform t)
    {
        foreach (var rule in Rules)
        {
            if (rule.floor != 0 && rule.floor != floor) continue;
            if (!rule.pattern.IsMatch(baseName)) continue;
            if (rule.objectPrefix != null && !HasAncestorNamed(t, rule.objectPrefix)) continue;
            return rule;
        }
        return null;
    }

    static bool HasAncestorNamed(Transform t, string prefix)
    {
        for (; t != null; t = t.parent)
            if (t.name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    static Material MatFor(Dictionary<string, Material> made, int floor, string key, string hex, Rule rule)
    {
        if (made.TryGetValue(key, out var mat)) return mat;
        // "furn_" keeps these apart from the props' accent materials (F3_oak etc.), which use other settings
        mat = SchoolKit.Mat(MatKey(floor, key), UiKit.Hex(hex), rule.tex, rule.smoothness, rule.metallic);
        made[key] = mat;
        return mat;
    }

    static string MatKey(int floor, string key) => $"F{floor}_furn_{key}";
}
