using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Shared helpers for the Tools > Elevator Game > School tools: school materials (URP Lit, tinted detail
/// textures), primitive-built props saved as prefabs, the "SchoolProps" root, idempotent placement, a free-spot
/// search that keeps props off walls, play zones, the spawn point and each other, floor-height and overlap
/// checks. Running on floors and finding building parts are EditorKit's.
/// Sizes passed to the prop builders are in real metres (scaled by SchoolPalette.WorldPerMetre).
/// </summary>
public static class SchoolKit
{
    public const string MaterialFolder = "Assets/Materials/School";
    public const string TextureFolder = "Assets/Textures/School";
    public const string PrefabFolder = "Assets/Prefabs/School";
    public const string PropsRootName = "SchoolProps";
    const float M = SchoolPalette.WorldPerMetre;

    /// <summary>The detail texture for a kind (set by SchoolTextures; null = untextured).</summary>
    public static Func<TexKind, Texture2D> TextureFor = _ => null;

    // ---------- materials ----------

    /// <summary>Gets or creates the URP Lit material `key` (Assets/Materials/School/key.mat) and sets it up.</summary>
    public static Material Mat(string key, Color color, TexKind tex = TexKind.Plain, float smoothness = 0.2f,
                               float metallic = 0f, Vector2? tiling = null, bool emissive = false)
    {
        EditorKit.EnsureFolder(MaterialFolder);
        string path = $"{MaterialFolder}/{key}.mat";
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = shader;
        mat.SetColor("_BaseColor", color);
        var texture = tex == TexKind.Plain ? null : TextureFor(tex);
        mat.SetTexture("_BaseMap", texture);
        mat.SetTextureScale("_BaseMap", tiling ?? Vector2.one);
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_Metallic", metallic);
        if (emissive)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * 1.5f);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        else mat.DisableKeyword("_EMISSION");
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>A material for one of a floor's surfaces (key e.g. "F3_floor").</summary>
    public static Material Mat(string key, Surface s) => Mat(key, s.color, s.tex, s.smoothness, s.metallic);

    /// <summary>A plain material in a floor's accent colour (key "F{floor}_{accent}").</summary>
    public static Material Accent(int floor, string accent, float smoothness = 0.25f, float metallic = 0f, bool emissive = false) =>
        Mat($"F{floor}_{accent}", SchoolPalette.Accent(floor, accent), TexKind.Plain, smoothness, metallic, null, emissive);

    // ---------- props ----------

    /// <summary>An empty prop root to build parts under (not in any scene until saved or placed).</summary>
    public static GameObject NewProp(string id) => new GameObject(id);

    /// <summary>
    /// Adds a primitive part: `center` and `size` in metres in the prop's space (y up from the floor), its own
    /// collider removed (the prop gets one box). Returns it for further tweaks.
    /// </summary>
    public static Transform Part(GameObject prop, string name, PrimitiveType type, Vector3 center, Vector3 size,
                                 Material mat, Vector3 euler = default)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(prop.transform, false);
        go.transform.localPosition = center * M;
        go.transform.localEulerAngles = euler;
        // Unity's cylinder is 2 tall: size.y is the full height
        go.transform.localScale = type == PrimitiveType.Cylinder ? new Vector3(size.x, size.y * 0.5f, size.z) * M : size * M;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go.transform;
    }

    /// <summary>A box part (the most common one).</summary>
    public static Transform Box(GameObject prop, string name, Vector3 center, Vector3 size, Material mat, Vector3 euler = default) =>
        Part(prop, name, PrimitiveType.Cube, center, size, mat, euler);

    /// <summary>A vertical cylinder part: `diameter` across, `height` tall, standing on `baseY` (metres).</summary>
    public static Transform Cyl(GameObject prop, string name, Vector3 at, float diameter, float height, Material mat, float baseY = 0f) =>
        Part(prop, name, PrimitiveType.Cylinder, new Vector3(at.x, baseY + height * 0.5f, at.z), new Vector3(diameter, height, diameter), mat);

    /// <summary>Flat world text (bilingual signs); `height` is the letter height in metres.</summary>
    public static Transform Label(GameObject prop, string name, string text, Vector3 center, float height, Color color, Vector3 euler = default)
    {
        var go = new GameObject(name);
        go.transform.SetParent(prop.transform, false);
        go.transform.localPosition = center * M;
        go.transform.localEulerAngles = euler;
        var tm = go.AddComponent<TextMesh>();
        tm.text = text;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.fontSize = 64;
        tm.characterSize = height * M / 6.4f;   // TextMesh letter height ≈ characterSize × fontSize / 10
        tm.color = color;
        var font = Resources.Load<Font>("Fonts/ZenMaruGothic-Bold");
        if (font != null)
        {
            tm.font = font;
            go.GetComponent<MeshRenderer>().sharedMaterial = TextMaterial(font);
        }
        return go.transform;
    }

    // The font's atlas with a depth-tested text shader: the built-in font material draws over everything, so
    // sign text showed through walls
    static Material TextMaterial(Font font)
    {
        var shader = Shader.Find("Elevator Game/World Text");
        if (shader == null) return font.material;
        EditorKit.EnsureFolder(MaterialFolder);
        string path = $"{MaterialFolder}/WorldText_{font.name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.shader = shader;
        mat.mainTexture = font.material.mainTexture;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>
    /// Fits one solid box collider round the prop's parts (unless `solid` is false, e.g. hanging lights), saves it
    /// as Assets/Prefabs/School/id.prefab, destroys the temporary object and returns the prefab.
    /// </summary>
    public static GameObject SavePrefab(GameObject prop, bool solid = true)
    {
        EditorKit.EnsureFolder(PrefabFolder);
        var renderers = prop.GetComponentsInChildren<Renderer>();
        if (solid && renderers.Length > 0)
        {
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            var box = prop.AddComponent<BoxCollider>();
            box.center = prop.transform.InverseTransformPoint(b.center);
            box.size = b.size;
        }
        var prefab = PrefabUtility.SaveAsPrefabAsset(prop, $"{PrefabFolder}/{prop.name}.prefab");
        UnityEngine.Object.DestroyImmediate(prop);
        return prefab;
    }

    // ---------- placement ----------

    /// <summary>The scene's "SchoolProps" root (created, with the SchoolDressing marker, if missing).</summary>
    public static Transform PropsRoot(Scene scene)
    {
        var root = scene.GetRootGameObjects().FirstOrDefault(r => r.GetComponent<SchoolDressing>() != null);
        if (root != null) return root.transform;
        root = new GameObject(PropsRootName, typeof(SchoolDressing));
        root.GetComponent<SchoolDressing>().adopted = true;   // a new root records from the start: nothing to adopt
        SceneManager.MoveGameObjectToScene(root, scene);
        Undo.RegisterCreatedObjectUndo(root, "School props");
        return root.transform;
    }

    // Every id the first versions of PropsLower / PropsUpper could place. A scene dressed before props were
    // recorded counts these as placed once, so the ones deleted by hand since then stay deleted.
    static readonly string[] LegacyIds =
    {
        "f1_directory", "f1_accent_wall", "f1_tactile", "f1_door_mat", "f1_umbrella", "f1_rail", "f1_exit_sign",
        "f2_counter", "f2_runner", "f2_shelf_1", "f2_shelf_2", "f2_shelf_3", "f2_library_sign", "f2_reading_table",
        "f3_drafting_1", "f3_drafting_2", "f3_drafting_3", "f3_whiteboard", "f3_pc_desk_1", "f3_pc_desk_2",
        "f3_arch_model", "f3_cabinet",
        "studioCam1", "studioCam2", "studioCam3", "studioCam4", "studioGrid", "studioCyc", "studioCurtain", "studioMonitor75",
        "subConsole", "subMonitorWall", "subRack1", "subRack2", "subChair1", "subChair2",
        "powerFrame", "powerMeterPanel", "powerSwitchgear", "powerSign",
        "careBed1", "careBed2", "careBed3", "careLift", "careShower1", "careShower2", "careShower3", "careSign",
        "ceBed", "ceMachine1", "ceMachine2", "ceMachine3", "ceIv1", "ceIv2", "ceIv3", "ceTrolley", "ceLamp", "ceSign",
        "itDesk1", "itDesk2", "itChair1", "itChair2", "itChair3", "itChair4", "itSign",
        "loungeTable1", "loungeTable2", "loungeChair1", "loungeChair2", "loungeChair3", "loungeChair4",
        "loungeChair5", "loungeChair6", "loungeChair7", "loungeChair8",
    };

    // The props root's record of placed ids, adopting a pre-record scene's props the first time (a root made
    // before ids were recorded: every legacy id counts as placed, even if all of them were deleted since)
    static SchoolDressing Record(Scene scene)
    {
        var root = PropsRoot(scene);
        var dressing = root.GetComponent<SchoolDressing>();
        if (dressing.adopted) return dressing;
        Undo.RecordObject(dressing, "School props");
        dressing.adopted = true;
        dressing.placed.AddRange(LegacyIds.Where(id => !dressing.placed.Contains(id)));
        foreach (Transform child in root)
            if (!dressing.placed.Contains(child.name)) dressing.placed.Add(child.name);
        EditorUtility.SetDirty(dressing);
        return dressing;
    }

    /// <summary>True when prop `id` was placed once and has since been deleted by hand: tools leave it out.</summary>
    public static bool IsDeleted(Scene scene, string id) =>
        PropsRoot(scene).Find(id) == null && Record(scene).placed.Contains(id);

    /// <summary>True when prop `id` needs no placing: it's there (maybe moved by hand) or was deleted by hand.</summary>
    public static bool IsPlaced(Scene scene, string id) => PropsRoot(scene).Find(id) != null || IsDeleted(scene, id);

    /// <summary>
    /// Puts an instance of `prefab` named `id` under the props root at `position` (world) facing `yaw` degrees.
    /// An existing child with that id is kept where it is (it may have been nudged by hand) unless `replace`.
    /// Returns null, placing nothing, for a prop deleted by hand.
    /// </summary>
    public static GameObject Place(Scene scene, string id, GameObject prefab, Vector3 position, float yaw, bool replace = false)
    {
        var root = PropsRoot(scene);
        var old = root.Find(id);
        if (old == null && IsDeleted(scene, id)) { Debug.Log($"School props: {id} was deleted by hand; not placed again."); return null; }
        if (old != null && !replace) return old.gameObject;
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        go.name = id;
        go.transform.SetParent(root, true);
        go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        Undo.RegisterCreatedObjectUndo(go, "Place school prop");
        var dressing = Record(scene);
        if (!dressing.placed.Contains(id))
        {
            Undo.RecordObject(dressing, "Place school prop");
            dressing.placed.Add(id);
            EditorUtility.SetDirty(dressing);
        }
        Physics.SyncTransforms();
        return go;
    }

    /// <summary>
    /// True the first time it's asked for `step` in this scene (then remembered): for one-off rearrangements, so
    /// running a tool again doesn't undo what was moved by hand afterwards.
    /// </summary>
    public static bool Once(Scene scene, string step)
    {
        var dressing = Record(scene);
        string key = "#" + step;
        if (dressing.placed.Contains(key)) return false;
        Undo.RecordObject(dressing, "School props");
        dressing.placed.Add(key);
        EditorUtility.SetDirty(dressing);
        return true;
    }

    /// <summary>Moves an existing prop to `position` / `yaw`; false (nothing done) when it isn't there.</summary>
    public static bool Move(Scene scene, string id, Vector3 position, float yaw)
    {
        var t = PropsRoot(scene).Find(id);
        if (t == null) return false;
        Undo.RecordObject(t, "Move school prop");
        t.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        Physics.SyncTransforms();
        return true;
    }

    /// <summary>Removes prop `id` for good: deleted now and remembered as deleted, so no tool places it again.</summary>
    public static void Retire(Scene scene, string id)
    {
        var dressing = Record(scene);
        var t = PropsRoot(scene).Find(id);
        if (t != null) Undo.DestroyObjectImmediate(t.gameObject);
        if (dressing.placed.Contains(id)) return;
        Undo.RecordObject(dressing, "Retire school prop");
        dressing.placed.Add(id);
        EditorUtility.SetDirty(dressing);
    }

    /// <summary>
    /// A clear spot on the floor near `anchor` for a footprint of `sizeMetres` (x wide, y tall, z deep) facing
    /// `yaw`: searched on rings from `minDist` to `maxDist` world units. Clear means: floor under it, nothing
    /// solid in the box (walls, furniture, earlier props), and away from play zones and the player's spawn.
    /// Returns null when nothing fits.
    /// </summary>
    public static Vector3? FreeSpot(Scene scene, Vector3 anchor, Vector3 sizeMetres, float yaw = 0f,
                                    float minDist = 0f, float maxDist = 60f)
    {
        Physics.SyncTransforms();
        var physics = scene.GetPhysicsScene();
        Vector3 half = sizeMetres * M * 0.5f;
        var rot = Quaternion.Euler(0f, yaw, 0f);
        var avoid = Avoid(scene);
        const float step = 3f;
        for (float r = minDist; r <= maxDist; r += step)
        {
            int count = r < 0.01f ? 1 : Mathf.Max(8, Mathf.CeilToInt(2f * Mathf.PI * r / step));
            for (int i = 0; i < count; i++)
            {
                float a = i * Mathf.PI * 2f / count;
                var p = anchor + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                if (!physics.Raycast(p + Vector3.up * 15f, Vector3.down, out var hit, 30f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (hit.normal.y < 0.9f) continue;   // not on a floor
                var ground = hit.point;
                var center = ground + Vector3.up * (half.y + 0.25f);   // just off the floor, so the floor itself never counts
                var footprint = new Bounds(ground + Vector3.up * half.y, rot * (half * 2f));
                footprint.size = new Vector3(Mathf.Abs(footprint.size.x), footprint.size.y, Mathf.Abs(footprint.size.z));
                if (avoid.Any(b => b.Intersects(footprint))) continue;
                if (physics.OverlapBox(center, half, new Collider[1], rot, ~0, QueryTriggerInteraction.Ignore) > 0) continue;
                return ground;
            }
        }
        return null;
    }

    // Play zones (grown a little) and a circle round the player's spawn, as world boxes to keep props out of
    static List<Bounds> Avoid(Scene scene)
    {
        var list = new List<Bounds>();
        foreach (var root in scene.GetRootGameObjects())
            foreach (var zone in root.GetComponentsInChildren<ElevatorPlayZone>(true))
                foreach (var c in zone.GetComponentsInChildren<Collider>(true))
                {
                    var b = c.bounds;
                    b.Expand(new Vector3(6f, 0f, 6f));
                    list.Add(b);
                }
        var character = FloorPlayerSetup.FindCharacter(scene);
        if (character != null) list.Add(new Bounds(character.transform.position, new Vector3(24f, 40f, 24f)));
        return list;
    }

    // ---------- floor height and overlaps ----------

    /// <summary>
    /// The walking floor's height under `at`: the highest upward-facing hit within `band` of y 0 on a ray down
    /// from y `top` (2 × `top` long), ignoring triggers and school props; 0 when there's none.
    /// </summary>
    public static float Ground(Scene scene, Vector3 at, float top = 15f, float band = 3f)
    {
        var props = PropsRoot(scene);
        var hits = new RaycastHit[32];
        int n = scene.GetPhysicsScene().Raycast(new Vector3(at.x, top, at.z), Vector3.down, hits, 2f * top, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        for (int i = 0; i < n; i++)
        {
            var h = hits[i];
            if (h.normal.y < 0.9f || Mathf.Abs(h.point.y) > band || h.collider.transform.IsChildOf(props)) continue;
            best = Mathf.Max(best, h.point.y);
        }
        return float.IsNegativeInfinity(best) ? 0f : best;
    }

    /// <summary>The highest upward floor hit straight under the player (their own collider ignored); `fallback` without one.</summary>
    public static float PlayerFloorY(Scene scene, float fallback)
    {
        var character = FloorPlayerSetup.FindCharacter(scene);
        if (character == null) return fallback;
        var hits = new RaycastHit[32];
        int n = scene.GetPhysicsScene().Raycast(character.transform.position + Vector3.up * 2f, Vector3.down, hits, 40f,
                                                ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        for (int i = 0; i < n; i++)
            if (hits[i].normal.y > 0.9f && !hits[i].collider.transform.IsChildOf(character.transform))
                best = Mathf.Max(best, hits[i].point.y);
        return float.IsNegativeInfinity(best) ? fallback : best;
    }

    /// <summary>
    /// Names of the solid colliders the prop's box collider touches, lifted off the floor and shrunk a hair (its
    /// own parts left out); empty when it's clear or has no box.
    /// </summary>
    public static List<string> OverlapsWith(Scene scene, Transform t)
    {
        var box = t.GetComponent<BoxCollider>();
        if (box == null) return new List<string>();
        Physics.SyncTransforms();
        const float lift = 0.3f, shrink = 0.1f;
        var half = Vector3.Scale(box.size, t.lossyScale) * 0.5f;
        half = new Vector3(Mathf.Max(0.05f, half.x - shrink), Mathf.Max(0.05f, half.y - lift * 0.5f), Mathf.Max(0.05f, half.z - shrink));
        var center = t.TransformPoint(box.center) + Vector3.up * (lift * 0.5f);
        var found = new Collider[32];
        int n = scene.GetPhysicsScene().OverlapBox(center, half, found, t.rotation, ~0, QueryTriggerInteraction.Ignore);
        return found.Take(n).Where(c => !c.transform.IsChildOf(t)).Select(c => c.name).Distinct().ToList();
    }

    // ---------- prop-building shorthands ----------

    public static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

    /// <summary>A plain material `key` in colour `hex`.</summary>
    public static Material HexMat(string key, string hex, float smoothness = 0.3f, float metallic = 0f, bool emissive = false) =>
        Mat(key, UiKit.Hex(hex), TexKind.Plain, smoothness, metallic, null, emissive);
}
