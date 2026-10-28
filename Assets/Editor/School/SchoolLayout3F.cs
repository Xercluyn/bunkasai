using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using static SchoolKit;

/// <summary>
/// Tools > Elevator Game > School > Arrange 3F Rooms: lays out the 3F architecture rooms like the real ones.
/// The room in front of the architecture class swaps its merged FBX desk block for 17 PC desks on the old desk
/// spots; the architecture class swaps its loose paper desks for 7 rows of drafting tables joined end to end
/// (stools on the south side, everyone facing the north wall), and moves the site model and cabinet into the
/// corners once. New props are placed once and then kept where they are; props deleted by hand stay deleted.
/// Needs Place School Props to have built the f3_* prefabs first.
/// </summary>
public static class SchoolLayout3F
{
    const string Title = "Arrange 3F Rooms";
    const float M = SchoolPalette.WorldPerMetre;
    const string OnceStep = "layout3F_v1";

    // Old desk centres in the room in front of the architecture class (world x, z): chairs south, facing +Z
    static readonly (float x, float z)[] PcDesks =
    {
        (-229.47f, -25.04f), (-216.53f, -25.04f),
        (-229.42f, -11.91f), (-216.48f, -11.91f), (-203.54f, -11.91f), (-190.59f, -11.91f), (-177.65f, -11.91f),
        (-229.37f, 1.21f), (-216.42f, 1.21f), (-203.48f, 1.21f), (-190.54f, 1.21f), (-177.60f, 1.21f),
        (-229.31f, 14.34f), (-216.37f, 14.34f), (-203.43f, 14.34f), (-190.49f, 14.34f), (-177.54f, 14.34f),
    };

    // Drafting rows: three 3.6 m sets joined along X per row, rows front (north) to back
    static readonly float[] DraftX = { -262.4f, -246.2f, -230.0f };
    static readonly float[] DraftZ = { 60f, 72f, 84f, 96f, 108f, 120f, 132f };

    // The architecture class, inside its walls (world x, z)
    static readonly Rect ArchRoom = Rect.MinMaxRect(-274.7f, 43.4f, -208.1f, 149.9f);

    static readonly Regex PaperDesk = new Regex(@"^paper(\.\d+)?$", RegexOptions.IgnoreCase);
    static readonly Regex DeskLeg = new Regex(@"^leg2(\.\d+)?$", RegexOptions.IgnoreCase);

    [MenuItem("Tools/Elevator Game/School/" + Title)]
    static void Menu() => OnOpenFloor(Title, (scene, floor) =>
    {
        if (floor != 3) { Debug.Log($"{Title}: only 3F has a layout, nothing to do on floor {floor}."); return; }
        Apply(scene, floor);
    });

    // What one run did, for the summary line
    sealed class Report
    {
        public int placed, kept, deleted, hidden;
        public readonly List<string> overlaps = new List<string>();
    }

    /// <summary>Arranges floor 3's PC room and architecture class (no-op for other floors).</summary>
    internal static void Apply(Scene scene, int floor)
    {
        if (floor != 3) return;
        var pc = LoadPrefab("f3_pc_desk");
        var drafting = LoadPrefab("f3_drafting_set");
        var model = LoadPrefab("f3_arch_model");
        var cabinet = LoadPrefab("f3_cabinet");
        if (pc == null || drafting == null || model == null || cabinet == null) return;

        Undo.SetCurrentGroupName(Title);
        var report = new Report();
        report.hidden += Hide(FindInBuilding(scene, "table-chair.012").Where(t => t.name == "table-chair.012"));
        report.hidden += Hide(FindInBuilding(scene, "paper").Where(t => PaperDesk.IsMatch(t.name) && InArchRoom(t)));
        report.hidden += Hide(FindInBuilding(scene, "leg2").Where(t => DeskLeg.IsMatch(t.name) && InArchRoom(t)));
        Physics.SyncTransforms();

        foreach (var id in new[] { "f3_pc_desk_1", "f3_pc_desk_2", "f3_drafting_1", "f3_drafting_2", "f3_drafting_3" })
            Retire(scene, id);
        PlacePcDesks(scene, pc, report);
        PlaceDrafting(scene, drafting, report);
        if (Once(scene, OnceStep))
        {
            Relocate(scene, "f3_arch_model", model, new Vector3(-214f, 0f, 136f), 270f, report);
            Relocate(scene, "f3_cabinet", cabinet, new Vector3(-272.3f, 0f, 140f), 90f, report);
        }
        Summarise(report);
    }

    static GameObject LoadPrefab(string name)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabFolder}/{name}.prefab");
        if (prefab == null) Debug.LogWarning($"{Title}: {PrefabFolder}/{name}.prefab is missing. Run Tools > Elevator Game > School > Place School Props first.");
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

    // Rows of drafting sets joined end to end; yaw 180 puts the stools south, the top (local z −0.3 m) on the row line
    static void PlaceDrafting(Scene scene, GameObject prefab, Report report)
    {
        var rot = Quaternion.Euler(0f, 180f, 0f);
        for (int r = 0; r < DraftZ.Length; r++)
            for (int c = 0; c < DraftX.Length; c++)
            {
                var top = new Vector3(DraftX[c], 0f, DraftZ[r]);
                Put(scene, $"f3_draft_{r + 1}_{c + 1}", prefab, top + rot * new Vector3(0f, 0f, 0.3f * M), 180f, report);
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

    // The walking floor's height under `at`: the highest upward-facing hit near y 0, ignoring triggers and props
    static float Ground(Scene scene, Vector3 at)
    {
        var props = PropsRoot(scene);
        var hits = new RaycastHit[32];
        int n = scene.GetPhysicsScene().Raycast(at + Vector3.up * (15f - at.y), Vector3.down, hits, 30f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        for (int i = 0; i < n; i++)
        {
            var h = hits[i];
            if (h.normal.y < 0.9f || Mathf.Abs(h.point.y) > 3f || h.collider.transform.IsChildOf(props)) continue;
            best = Mathf.Max(best, h.point.y);
        }
        return float.IsNegativeInfinity(best) ? 0f : best;
    }

    // Warns (without moving it) when the prop's box collider, lifted off the floor and shrunk a hair, hits anything
    static void CheckOverlap(Scene scene, GameObject go, Report report)
    {
        var box = go.GetComponent<BoxCollider>();
        if (box == null) return;
        var t = go.transform;
        Physics.SyncTransforms();
        const float lift = 0.3f, shrink = 0.1f;
        var half = Vector3.Scale(box.size, t.lossyScale) * 0.5f;
        half = new Vector3(half.x - shrink, half.y - lift * 0.5f, half.z - shrink);
        var center = t.TransformPoint(box.center) + Vector3.up * (lift * 0.5f);
        var found = new Collider[32];
        int n = scene.GetPhysicsScene().OverlapBox(center, half, found, t.rotation, ~0, QueryTriggerInteraction.Ignore);
        var others = found.Take(n).Where(c => !c.transform.IsChildOf(t)).Select(c => c.name).Distinct().ToList();
        if (others.Count == 0) return;
        report.overlaps.Add(go.name);
        Debug.LogWarning($"{Title}: {go.name} at {t.position} overlaps {string.Join(", ", others)}.", go);
    }

    // ---------- building furniture ----------

    // Hides building objects (recorded on their prefab instance); returns how many were newly hidden
    static int Hide(IEnumerable<Transform> targets)
    {
        int count = 0;
        foreach (var t in targets.ToList())
        {
            var go = t.gameObject;
            if (!go.activeSelf) continue;
            Undo.RecordObject(go, Title);
            go.SetActive(false);
            PrefabUtility.RecordPrefabInstancePropertyModifications(go);
            count++;
        }
        return count;
    }

    // Whether an object's middle (its renderers' bounds, else its position) is inside the architecture class
    static bool InArchRoom(Transform t)
    {
        var renderers = t.GetComponentsInChildren<Renderer>();
        Vector3 p = t.position;
        if (renderers.Length > 0)
        {
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            p = b.center;
        }
        return ArchRoom.Contains(new Vector2(p.x, p.z));
    }

    static void Summarise(Report report)
    {
        string overlaps = report.overlaps.Count == 0 ? "no overlaps" : $"{report.overlaps.Count} overlapping (see warnings): {string.Join(", ", report.overlaps)}";
        Debug.Log($"{Title}: {report.placed} props placed, {report.kept} already there and kept, " +
                  $"{report.deleted} left out (deleted by hand), {report.hidden} building objects hidden; {overlaps}.");
    }
}
