using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > Elevator Game > Fix Indoor Lighting.
/// The lights exported with the building are area lights, which URP only supports in a lightmap bake, so
/// at runtime they give no light and the sun lights the inside instead. This turns them into realtime point
/// lights spaced to overlap (Forward+ renders this many), switches the directional light off, and replaces
/// the sky's blue ambient with an indoor one. On the floors the lamps and ambient take the floor's colour temperature
/// (warm-ish lobby, neutral 2F/4F, cool 3F/5F) and 4F gets a blue wash in the studio. Safe to run again after
/// changing the values below.
/// </summary>
public static class IndoorLightingSetup
{
    const string Title = "Fix Indoor Lighting";

    // How bright the floor is under the lamps, counting every lamp that reaches it (1 = full-strength white
    // light). Lamp intensity is worked out from this, the lamps' height and how much they overlap.
    const float FloorBrightness = 0.8f;
    const float RangeOfSpacing = 1.6f;   // light range in multiples of the distance to the nearest other light

    // 4F studio: one blue wash over the cyclorama, like the real studio's blue lighting
    const string WashName = "StudioBlueWash";
    const string WashAnchor = "swBtn22";
    const float WashHeightMetres = 2f;
    const float WashRangeMetres = 12f;
    const float WashOfLamp = 0.6f;       // wash intensity in multiples of a lamp's

    /// <summary>A floor's light: lamp colour and the trilight ambient (sky, equator, ground).</summary>
    readonly struct Mood
    {
        public readonly string name;
        public readonly Color lamp, sky, equator, ground;

        public Mood(string name, string lamp, string sky, string equator, string ground)
        {
            this.name = name;
            this.lamp = UiKit.Hex(lamp);
            this.sky = UiKit.Hex(sky);
            this.equator = UiKit.Hex(equator);
            this.ground = UiKit.Hex(ground);
        }
    }

    // Cool neutral, even indoor fill (the IT-department palette); also used for scenes that aren't floors
    static readonly Mood Cool = new Mood("cool", "#F4F8FF", "#8a96a8", "#5c6472", "#2c323c");
    static readonly Mood Neutral = new Mood("neutral 4500K", "#FFF4E8", "#8e929a", "#5e626a", "#2e3036");
    static readonly Mood WarmLobby = new Mood("slightly warm", "#FFF1DE", "#958f86", "#625c56", "#302c28");

    // 1F lobby downlights on the bronze ceiling, 2F/4F neutral, 3F/5F cool bright classrooms
    static Mood MoodOf(int floor) => floor == 1 ? WarmLobby : floor == 2 || floor == 4 ? Neutral : Cool;

    [MenuItem("Tools/Elevator Game/Fix Indoor Lighting")]
    internal static void Fix()
    {
        var scene = SceneManager.GetActiveScene();
        int floor = SchoolKit.FloorOf(scene);
        var mood = MoodOf(floor);
        var lights = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Light>(true)).ToList();
        // The building's lamps: area lights still to convert, and point lights from an earlier run (not the studio wash)
        var lamps = lights.Where(l => (l.type == LightType.Rectangle || l.type == LightType.Disc || l.type == LightType.Point) &&
                                      l.name != WashName).ToList();

        var character = FloorPlayerSetup.FindCharacter(scene);
        var ignore = character != null ? character.transform : null;
        float height = character != null ? CharacterAnimationSetup.Height(character) : 1.7f;

        // Each lamp's height above the floor below it; lamps over something lower (or nothing) use the typical height
        Physics.SyncTransforms();
        var drops = lamps.Select(l => DropToFloor(l.transform.position, ignore, 0.05f * height)).ToList();
        var found = drops.Where(d => d > 0f).OrderBy(d => d).ToList();
        float typical = found.Count > 0 ? found[found.Count / 2] : 2f * height;

        // Range from how far apart the lamps are, so neighbouring pools just overlap, and always past the floor.
        // A desk under a lamp mustn't make it count as lower.
        float spacing = SpacingOf(lamps);
        var floors = new Vector3[lamps.Count];
        var ranges = new float[lamps.Count];
        for (int i = 0; i < lamps.Count; i++)
        {
            float drop = Mathf.Max(drops[i], typical);
            floors[i] = lamps[i].transform.position + Vector3.down * drop;
            ranges[i] = Mathf.Max(spacing * RangeOfSpacing, drop * 2.5f);
        }
        float intensity = FloorBrightness / Mathf.Max(TypicalFloorLight(lamps, floors, ranges), 1e-9f);

        for (int i = 0; i < lamps.Count; i++)
        {
            var l = lamps[i];
            Undo.RecordObject(l, Title);
            l.type = LightType.Point;
            l.lightmapBakeType = LightmapBakeType.Realtime;
            l.range = ranges[i];
            l.intensity = intensity;
            // Non-floor scenes keep their lamps' colour, as before
            if (floor != 0) l.color = mood.lamp;
            l.shadows = LightShadows.None;   // hundreds of shadowed lights would be far too slow
            l.renderMode = LightRenderMode.Auto;
        }

        int suns = 0;
        foreach (var l in lights.Where(l => l.type == LightType.Directional && l.enabled))
        {
            Undo.RecordObject(l, Title);
            l.enabled = false;
            suns++;
        }

        // Even indoor fill in the floor's mood instead of the blue sky; dim sky reflections on shiny surfaces
        // (Lighting window > Environment; not undoable)
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = mood.sky;
        RenderSettings.ambientEquatorColor = mood.equator;
        RenderSettings.ambientGroundColor = mood.ground;
        RenderSettings.reflectionIntensity = 0.3f;

        string wash = floor == 4 ? PlaceStudioWash(scene, intensity) : "";

        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"{scene.name}: set {lamps.Count} lamp(s) as realtime point lights about {typical:0.#} above the floor " +
                  $"(intensity {intensity:0.#}, floor brightness {FloorBrightness}), " +
                  $"switched off {suns} directional light(s) and set a {(floor != 0 ? mood.name : "cool neutral")} indoor light{wash}. Save the scene.");
    }

    // Replaces the studio's blue wash light (a scene root, so it never counts as part of the building) above the anchor
    static string PlaceStudioWash(Scene scene, float lampIntensity)
    {
        foreach (var old in scene.GetRootGameObjects().Where(r => r.name == WashName))
            Undo.DestroyObjectImmediate(old);

        var anchor = SchoolKit.FindInBuilding(scene, WashAnchor).FirstOrDefault();
        if (anchor == null)
        {
            Debug.LogWarning($"{Title}: no '{WashAnchor}' in the building, so the studio's blue wash light was skipped.");
            return "";
        }
        var renderers = anchor.GetComponentsInChildren<Renderer>(true);
        var at = anchor.position;
        if (renderers.Length > 0)
        {
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            at = b.center;
        }

        var go = new GameObject(WashName);
        SceneManager.MoveGameObjectToScene(go, scene);
        go.transform.position = at + Vector3.up * WashHeightMetres * SchoolPalette.WorldPerMetre;
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.lightmapBakeType = LightmapBakeType.Realtime;
        light.color = UiKit.Hex("#4A5CFF");
        light.range = WashRangeMetres * SchoolPalette.WorldPerMetre;
        light.intensity = lampIntensity * WashOfLamp;
        light.shadows = LightShadows.None;
        light.renderMode = LightRenderMode.Auto;
        Undo.RegisterCreatedObjectUndo(go, Title);
        return $" and a blue wash over the studio near {anchor.name}";
    }

    // Light reaching the floor under a typical lamp from all lamps at intensity 1, with URP's point light
    // falloff (inverse square, faded to zero at the range) and the angle it hits the floor at
    static float TypicalFloorLight(List<Light> lamps, Vector3[] floors, float[] ranges)
    {
        var totals = new List<float>();
        for (int i = 0; i < lamps.Count; i++)
        {
            float total = 0f;
            for (int j = 0; j < lamps.Count; j++)
            {
                Vector3 toLamp = lamps[j].transform.position - floors[i];
                float d2 = toLamp.sqrMagnitude;
                if (d2 < 1e-6f || toLamp.y <= 0f) continue;
                float fade = Mathf.Clamp01(1f - Mathf.Pow(d2 / (ranges[j] * ranges[j]), 2f));
                total += fade * fade / d2 * (toLamp.y / Mathf.Sqrt(d2));
            }
            totals.Add(total);
        }
        if (totals.Count == 0) return 0f;
        totals.Sort();
        return totals[totals.Count / 2];
    }

    // Distance straight down to the nearest surface, skipping the lamp's own fitting just below it; 0 if none
    static float DropToFloor(Vector3 from, Transform ignore, float skip)
    {
        bool backfaces = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;   // single-sided floors count from either side
        try
        {
            float best = 0f;
            foreach (var hit in Physics.RaycastAll(from, Vector3.down, 1000f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.distance <= skip || (ignore != null && hit.collider.transform.IsChildOf(ignore))) continue;
                if (best == 0f || hit.distance < best) best = hit.distance;
            }
            return best;
        }
        finally
        {
            Physics.queriesHitBackfaces = backfaces;
        }
    }

    // Median distance from each light to its nearest neighbour; 0 with fewer than two lights
    static float SpacingOf(List<Light> lights)
    {
        if (lights.Count < 2) return 0f;
        var nearest = new List<float>();
        foreach (var a in lights)
        {
            float best = float.MaxValue;
            foreach (var b in lights)
                if (a != b) best = Mathf.Min(best, Vector3.Distance(a.transform.position, b.transform.position));
            if (best > 1e-4f) nearest.Add(best);
        }
        if (nearest.Count == 0) return 0f;
        nearest.Sort();
        return nearest[nearest.Count / 2];
    }
}
