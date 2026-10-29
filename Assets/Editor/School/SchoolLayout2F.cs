using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using static EditorKit;
using static SchoolKit;

/// <summary>
/// Arrange Rooms on 2F (Tools > Elevator Game > School > Steps > Arrange Rooms): adds one of the floor's own dark grey doors (copied
/// from the FBX door nearest the reception counter) flush against the wall the counter backs onto, about 1 m
/// past whichever end of the counter has free wall, facing the room like the counter. The door prefab is rebuilt
/// each run; the placed door is kept where it is once placed, and stays out if deleted by hand.
/// Needs Place School Props to have placed f2_counter first.
/// </summary>
public static class SchoolLayout2F
{
    const string Title = "Add 2F Reception Door";
    const float M = SchoolPalette.WorldPerMetre;
    const string DoorId = "f2_reception_door";
    const float GapMetres = 1f;

    // A wall-door's size range in world units (the 2F doors are about 5.0 wide × 9.5 tall)
    const float MinHeight = 7f, MaxHeight = 12f, MinWidth = 3f, MaxWidth = 7.5f;

    /// <summary>Adds the door beside floor 2's reception counter (no-op for other floors).</summary>
    internal static void Apply(Scene scene, int floor)
    {
        if (floor != 2) return;
        if (IsDeleted(scene, DoorId)) { Debug.Log($"{Title}: {DoorId} was deleted by hand; not placed again."); return; }
        var counter = PropsRoot(scene).Find("f2_counter");
        if (counter == null) { Debug.LogWarning($"{Title}: no f2_counter under the props root. Run Tools > Elevator Game > School > Steps > Place School Props first."); return; }
        var counterBox = counter.GetComponent<BoxCollider>();
        if (counterBox == null) { Debug.LogWarning($"{Title}: f2_counter has no box collider to measure it by."); return; }

        var template = NearestDoor(scene, counter.position);
        if (template == null) { Debug.LogWarning($"{Title}: no door-sized 'door*' object found in the 2F building."); return; }
        Undo.SetCurrentGroupName(Title);
        var prefab = BuildPrefab(scene, template);
        if (prefab == null) return;

        if (IsPlaced(scene, DoorId))
        {
            Debug.Log($"{Title}: prefab rebuilt from {PathOf(template)}; {DoorId} already placed, kept where it is.");
            return;
        }
        var spot = DoorSpot(scene, counter, counterBox, prefab.GetComponent<BoxCollider>());
        if (spot == null) { Debug.LogWarning($"{Title}: no free wall at either end of the counter for the door; nothing placed."); return; }
        var go = Place(scene, DoorId, prefab, spot.Value.position, spot.Value.yaw);
        if (go != null)
            Debug.Log($"{Title}: {DoorId} copied from {PathOf(template)} and placed at {spot.Value.position} yaw {spot.Value.yaw:0} " +
                      $"on the counter's {spot.Value.side} end ({GapMetres:0.#} m gap).", go);
    }

    // ---------- the template door ----------

    // The active, door-sized 'door*' object nearest the counter (an outer one when a door and its part both match)
    static Transform NearestDoor(Scene scene, Vector3 near)
    {
        var doors = FindInBuilding(scene, "door").Where(t => IsDoorSized(t)).ToList();
        var outer = doors.Where(t => !doors.Any(o => o != t && t.IsChildOf(o))).ToList();
        return outer.OrderBy(t => t.gameObject.activeInHierarchy ? 0 : 1)
                    .ThenBy(t => Vector3.Distance(Flat(WorldBounds(t).center), Flat(near)))
                    .FirstOrDefault();
    }

    static bool IsDoorSized(Transform t)
    {
        if (t.GetComponentsInChildren<MeshFilter>(true).Length == 0) return false;
        var b = WorldBounds(t);
        float wide = Mathf.Max(b.size.x, b.size.z);
        return b.size.y >= MinHeight && b.size.y <= MaxHeight && wide >= MinWidth && wide <= MaxWidth;
    }

    // ---------- the prefab ----------

    // A plain copy of the template turned so its face points along local +Z, with its bottom centre at the
    // origin, colliders stripped and one box fitted (SavePrefab); saved as f2_reception_door.prefab
    static GameObject BuildPrefab(Scene scene, Transform template)
    {
        var face = FaceDirection(scene, template);
        var copy = Object.Instantiate(template.gameObject);
        if (PrefabUtility.IsPartOfPrefabInstance(copy))
            PrefabUtility.UnpackPrefabInstance(PrefabUtility.GetOutermostPrefabInstanceRoot(copy), PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        copy.name = "Door";
        copy.SetActive(true);
        copy.transform.SetPositionAndRotation(template.position, template.rotation);
        copy.transform.localScale = template.lossyScale;
        copy.transform.RotateAround(template.position, Vector3.up, Vector3.SignedAngle(face, Vector3.forward, Vector3.up));
        foreach (var c in copy.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);

        var b = WorldBounds(copy.transform);
        var prop = NewProp(DoorId);
        prop.transform.position = new Vector3(b.center.x, b.min.y, b.center.z);
        copy.transform.SetParent(prop.transform, true);
        prop.transform.position = Vector3.zero;
        var prefab = SavePrefab(prop);
        Debug.Log($"{Title}: rebuilt {PrefabFolder}/{DoorId}.prefab from {PathOf(template)} ({b.size.x:0.00} wide × {b.size.y:0.00} tall × {b.size.z:0.00} deep).");
        return prefab;
    }

    // The door's face normal (flat, world): the horizontal direction its mesh is thinnest along, signed toward
    // the side with more open space in front of it (the room rather than the wall behind)
    static Vector3 FaceDirection(Scene scene, Transform door)
    {
        var corners = Corners(door);
        Vector3 best = Vector3.forward;
        float depth = float.PositiveInfinity;
        for (int deg = 0; deg < 180; deg++)
        {
            var d = Quaternion.Euler(0f, deg, 0f) * Vector3.forward;
            float min = float.PositiveInfinity, max = float.NegativeInfinity;
            foreach (var c in corners) { float p = Vector3.Dot(c, d); min = Mathf.Min(min, p); max = Mathf.Max(max, p); }
            if (max - min < depth) { depth = max - min; best = d; }
        }
        var center = WorldBounds(door).center;
        float ahead = OpenDistance(scene, door, center, best, depth);
        float behind = OpenDistance(scene, door, center, -best, depth);
        return behind > ahead ? -best : best;
    }

    // How far a horizontal ray from just outside the door's face travels before hitting something (not the door)
    static float OpenDistance(Scene scene, Transform door, Vector3 center, Vector3 dir, float depth)
    {
        const float range = 60f;
        var hits = new RaycastHit[16];
        int n = scene.GetPhysicsScene().Raycast(center + dir * (depth * 0.5f + 0.2f), dir, hits, range, ~0, QueryTriggerInteraction.Ignore);
        float nearest = range;
        for (int i = 0; i < n; i++)
            if (!hits[i].collider.transform.IsChildOf(door)) nearest = Mathf.Min(nearest, hits[i].distance);
        return nearest;
    }

    // ---------- where the door goes ----------

    struct Spot { public Vector3 position; public float yaw; public string side; }

    // Beside one end of the counter, against the wall it backs onto: each end is tried (wall found behind the
    // whole door width, facing the room, near the counter's wall line, and nothing in the door's box); when
    // both ends work, the one with more free wall beyond wins
    static Spot? DoorSpot(Scene scene, Transform counter, BoxCollider counterBox, BoxCollider doorBox)
    {
        Physics.SyncTransforms();
        var scale = counter.lossyScale;
        float halfWidth = counterBox.size.x * scale.x * 0.5f;
        float backZ = (counterBox.center.z - counterBox.size.z * 0.5f) * scale.z;
        var forward = Flat(counter.forward).normalized;
        var right = Vector3.Cross(Vector3.up, forward);
        var back = Flat(counter.TransformPoint(new Vector3(counterBox.center.x, 0f, 0f))) + forward * backZ;
        var counterWall = WallBehind(scene, back, forward, doorBox.size.y);
        float doorWidth = doorBox.size.x, doorDepth = doorBox.size.z, doorHeight = doorBox.size.y;
        float yaw = Mathf.Round(counter.eulerAngles.y);

        var options = new List<(Spot spot, bool roomy)>();
        foreach (var (sign, side) in new[] { (1f, "right"), (-1f, "left") })
        {
            float along = halfWidth + GapMetres * M + doorWidth * 0.5f;
            var mid = back + right * (sign * along);
            var wall = WallBehind(scene, mid, forward, doorHeight);
            if (wall == null) continue;
            if (counterWall != null && Mathf.Abs(wall.Value - counterWall.Value) > 2f) continue;   // a different wall or a recess
            bool edgesOnWall = new[] { -1f, 1f }.All(e =>
            {
                var edge = WallBehind(scene, mid + right * (e * (doorWidth * 0.5f - 0.2f)), forward, doorHeight);
                return edge != null && Mathf.Abs(edge.Value - wall.Value) < 0.5f;
            });
            if (!edgesOnWall) continue;

            var at = mid - forward * wall.Value + forward * (doorDepth * 0.5f);
            at.y = Ground(scene, at);
            var rot = Quaternion.Euler(0f, yaw, 0f);
            if (Blocked(scene, counter, at, new Vector3(doorWidth, doorHeight, doorDepth), rot)) continue;
            bool roomy = !Blocked(scene, counter, at + right * (sign * M), new Vector3(doorWidth, doorHeight, doorDepth), rot);
            options.Add((new Spot { position = at, yaw = yaw, side = side }, roomy));
        }
        if (options.Count == 0) return null;
        return options.OrderBy(o => o.roomy ? 0 : 1).First().spot;
    }

    // Distance back from `from` (at mid-door height) to a wall facing the room, or null when there's none close
    static float? WallBehind(Scene scene, Vector3 from, Vector3 forward, float doorHeight)
    {
        const float lead = 1f, range = 12f;
        var props = PropsRoot(scene);
        var origin = new Vector3(from.x, Ground(scene, from) + doorHeight * 0.5f, from.z) + forward * lead;
        var hits = new RaycastHit[16];
        int n = scene.GetPhysicsScene().Raycast(origin, -forward, hits, range, ~0, QueryTriggerInteraction.Ignore);
        float? best = null;
        for (int i = 0; i < n; i++)
        {
            var h = hits[i];
            if (h.collider.transform.IsChildOf(props)) continue;
            if (Vector3.Dot(Flat(h.normal).normalized, forward) < 0.9f) continue;
            float d = h.distance - lead;
            if (best == null || d < best.Value) best = d;
        }
        return best;
    }

    // Whether anything solid (not the floor, not the counter) is inside the door's box, lifted a little and shrunk
    static bool Blocked(Scene scene, Transform counter, Vector3 bottom, Vector3 size, Quaternion rot)
    {
        const float lift = 0.3f, shrink = 0.15f;
        var half = new Vector3(size.x * 0.5f - shrink, (size.y - lift) * 0.5f - shrink, size.z * 0.5f - shrink);
        var center = bottom + Vector3.up * (lift + half.y + shrink);
        var found = new Collider[16];
        int n = scene.GetPhysicsScene().OverlapBox(center, half, found, rot, ~0, QueryTriggerInteraction.Ignore);
        return found.Take(n).Any(c => !c.transform.IsChildOf(counter));
    }

    // ---------- geometry ----------

    // The world corners of every mesh under `t` (its mesh bounds, so tight for rotated parts)
    static List<Vector3> Corners(Transform t)
    {
        var list = new List<Vector3>();
        foreach (var mf in t.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            var b = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                list.Add(mf.transform.TransformPoint(c));
            }
        }
        return list;
    }

    static Bounds WorldBounds(Transform t)
    {
        var corners = Corners(t);
        if (corners.Count == 0) return new Bounds(t.position, Vector3.zero);
        var b = new Bounds(corners[0], Vector3.zero);
        foreach (var c in corners) b.Encapsulate(c);
        return b;
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
