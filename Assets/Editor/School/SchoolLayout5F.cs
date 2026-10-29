using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using static EditorKit;
using static SchoolKit;

/// <summary>
/// Tools > Elevator Game > School > Arrange 5F Rooms: puts a laptop on every student seat of the two IT rooms
/// (silver in the room with the IT sign, black in the arcade room), each set on its desk by a raycast, and once per
/// scene rearranges the care / clinical engineering props into the shared room the way the real one is laid out:
/// care (介護実習室) on the left (north) of the west entrance, clinical engineering (臨床工学実習室) on the right.
/// Props deleted by hand are never placed or moved back; laptops already there are kept where they are.
/// </summary>
public static class SchoolLayout5F
{
    const string Title = "Arrange 5F Rooms";
    const float M = SchoolPalette.WorldPerMetre;
    const float SeatOffset = 1.8f;           // seats sit ±1.8 units along a desk's long axis
    const float UserSetback = 0.25f * M;     // laptop centre this far in from the user-side desk edge

    [MenuItem("Tools/Elevator Game/School/" + Title)]
    static void Menu() => OnOpenFloor(Title, (scene, floor) =>
    {
        if (floor != 5) Debug.Log($"{Title}: only 5F has these rooms; nothing to do on floor {floor}.");
        else Apply(scene, floor);
    });

    /// <summary>Places the IT-room laptops and (once) lays out the care / CE room of floor 5; nothing for other floors.</summary>
    internal static void Apply(Scene scene, int floor)
    {
        if (floor != 5) return;
        Physics.SyncTransforms();
        PlaceLaptops(scene, "IT room 1", Laptop("F5_LaptopSilver", HexMat("F5_laptopSilver", "#C8CCD0", 0.6f, 0.7f), true), ItRoom1Seats());
        PlaceLaptops(scene, "IT room 2", Laptop("F5_LaptopBlack", HexMat("F5_laptopBlack", "#151515", 0.5f, 0.3f), false), ItRoom2Seats());
        // care first, so its old spots in the CE half are free; each room's one-off pass waits for its props
        ArrangeOnce(scene, "care room", "layout5F_care_v1", CareSpots);
        ArrangeOnce(scene, "clinical engineering", "layout5F_ce_v1", CeSpots);
        AssetDatabase.SaveAssets();
    }

    // Lays out one room the first time any of its props exists; later runs keep hand-moved props where they are
    static void ArrangeOnce(Scene scene, string room, string step, Spot[] spots)
    {
        var root = PropsRoot(scene);
        if (!spots.Any(s => root.Find(s.id) != null))
        {
            Debug.Log($"{Title}: no {room} props yet (run Place School Props first); its layout pass is left for later.");
            return;
        }
        if (Once(scene, step)) ArrangeRoom(scene, room, spots);
    }

    // ---------- laptop ----------

    // 0.33 x 0.23 m base with keyboard and trackpad, lid hinged at the back edge and open ~105°; the user is at +Z
    static GameObject Laptop(string id, Material body, bool logo)
    {
        var p = NewProp(id);
        Material keys = HexMat("F5_laptopKeys", "#1C1C1E", 0.3f), pad = HexMat("F5_laptopPad", "#6E7278", 0.6f, 0.4f);
        Box(p, "base", V(0f, 0.01f, 0f), V(0.33f, 0.02f, 0.23f), body);
        Box(p, "keyboard", V(0f, 0.0205f, -0.035f), V(0.28f, 0.002f, 0.1f), keys);
        Box(p, "trackpad", V(0f, 0.0205f, 0.068f), V(0.1f, 0.002f, 0.06f), pad);
        var tilt = Quaternion.Euler(-15f, 0f, 0f);   // leaning back past upright
        var hinge = V(0f, 0.02f, -0.115f);
        Vector3 OnLid(float up, float forward) => hinge + tilt * V(0f, up, forward);
        var lean = V(-15f, 0f, 0f);
        Box(p, "lid", OnLid(0.11f, 0f), V(0.33f, 0.22f, 0.01f), body, lean);
        Box(p, "screen", OnLid(0.115f, 0.0055f), V(0.3f, 0.19f, 0.002f), HexMat("F5_laptopScreen", "#1E3A5F", 0.85f, 0f, true), lean);
        if (logo) Box(p, "logo", OnLid(0.12f, -0.0055f), V(0.04f, 0.045f, 0.001f), HexMat("F5_laptopLogo", "#EDEFF2", 0.8f, 0.9f), lean);
        return SavePrefab(p, false);
    }

    // ---------- IT room seats ----------

    readonly struct Seat
    {
        public readonly string id;
        public readonly float x, z, yaw;
        public Seat(string id, float x, float z, float yaw) { this.id = id; this.x = x; this.z = z; this.yaw = yaw; }
    }

    // Two seats on one desk centred at (x, z): long axis along Z or X, users on the `side` (−1/+1) of the short axis
    static void AddDesk(List<Seat> seats, string id, float x, float z, bool alongZ, int side, float depth)
    {
        float inset = side * (depth * 0.5f - UserSetback);
        float yaw = alongZ ? (side < 0 ? 270f : 90f) : (side < 0 ? 180f : 0f);
        for (int s = 0; s < 2; s++)
        {
            float along = (s == 0 ? -1f : 1f) * SeatOffset;
            seats.Add(alongZ ? new Seat($"{id}_{s}", x + inset, z + along, yaw) : new Seat($"{id}_{s}", x + along, z + inset, yaw));
        }
    }

    // table-chair.014: 4 columns x 5 rows, users on the west; each column sits ~0.5 further −Z than the last
    static List<Seat> ItRoom1Seats()
    {
        float[] xs = { -232.8f, -242.7f, -252.5f, -262.3f };
        float[] zs = { -135.1f, -122.1f, -109.2f, -96.2f, -83.3f };
        var seats = new List<Seat>();
        for (int c = 0; c < xs.Length; c++)
            for (int r = 0; r < zs.Length; r++)
                AddDesk(seats, $"laptopS_{c}_{r}", xs[c], zs[r] - 0.5f * c, true, -1, 2.57f);
        return seats;
    }

    // table-chair.015-.021: west-facing and east-facing columns of four, and a south-facing row of three
    static List<Seat> ItRoom2Seats()
    {
        float[] zw = { -116.48f, -108.12f, -99.47f, -91.10f }, ze = { -116.43f, -108.06f, -99.42f, -91.05f };
        float[][] west = { new[] { -26.10f, -26.15f }, new[] { -7.16f, -7.20f }, new[] { 10.89f, 10.84f } };
        float[][] east = { new[] { -21.57f, -21.51f, -21.49f, -21.43f }, new[] { -2.63f, -2.57f, -2.55f, -2.48f },
                           new[] { 15.42f, 15.48f, 15.50f, 15.56f } };
        var seats = new List<Seat>();
        for (int c = 0; c < 3; c++)
            for (int r = 0; r < 4; r++)
            {
                AddDesk(seats, $"laptopB_W{c}_{r}", west[c][r < 2 ? 0 : 1], zw[r], true, -1, 2.52f);
                AddDesk(seats, $"laptopB_E{c}_{r}", east[c][r], ze[r], true, 1, 2.58f);
            }
        float[] xs = { -23.55f, -5.19f, 13.16f };
        for (int c = 0; c < xs.Length; c++) AddDesk(seats, $"laptopB_S{c}", xs[c], -122.65f, false, -1, 2.55f);
        return seats;
    }

    static void PlaceLaptops(Scene scene, string room, GameObject prefab, List<Seat> seats)
    {
        int placed = 0, kept = 0, skipped = 0;
        foreach (var s in seats)
        {
            if (IsPlaced(scene, s.id)) { kept++; continue; }
            float? y = DeskTopY(scene, s.x, s.z);
            if (y == null) { skipped++; Debug.LogWarning($"{Title}: {s.id}: no desk top under ({s.x}, {s.z}); skipped."); continue; }
            if (Place(scene, s.id, prefab, new Vector3(s.x, y.Value, s.z), s.yaw) != null) placed++;
        }
        Debug.Log($"{Title}: {room}: {placed} laptops placed, {kept} already there (or deleted by hand), {skipped} skipped.");
    }

    // The highest upward-facing desk-height surface at (x, z), ignoring school props and teacher desks (laptopLogo)
    static float? DeskTopY(Scene scene, float x, float z)
    {
        var hits = new RaycastHit[16];
        int n = scene.GetPhysicsScene().Raycast(new Vector3(x, 8f, z), Vector3.down, hits, 8f, ~0, QueryTriggerInteraction.Ignore);
        var props = PropsRoot(scene);
        float? best = null;
        for (int i = 0; i < n; i++)
        {
            var h = hits[i];
            if (h.normal.y <= 0.9f || h.point.y < 2.5f || h.point.y > 6f || h.collider.transform.IsChildOf(props)) continue;
            if (h.collider.name.StartsWith("laptopLogo")) return null;
            if (best == null || h.point.y > best.Value) best = h.point.y;
        }
        return best;
    }

    // ---------- care / clinical engineering layout (world units; room x 205.5..274.7, z −149.9..30) ----------

    readonly struct Spot
    {
        public readonly string id;
        public readonly float x, z, yaw;
        public Spot(string id, float x, float z, float yaw) { this.id = id; this.x = x; this.z = z; this.yaw = yaw; }
    }

    // Care, in the free strip x 230..250, z −40..0: beds side by side heads east, lift and shower chairs to the north
    static readonly Spot[] CareSpots =
    {
        new Spot("careBed1", 240f, -34f, 270f), new Spot("careBed2", 240f, -26f, 270f), new Spot("careBed3", 240f, -18f, 270f),
        new Spot("careLift", 233f, -6f, 180f),
        new Spot("careShower1", 242f, -6f, 180f), new Spot("careShower2", 245f, -6f, 180f), new Spot("careShower3", 248f, -6f, 180f),
        new Spot("careSign", 212f, -40f, 270f),
    };

    // CE round (239, −110), between the FBX bed columns: bed heads east, machines on a ring of 9 facing it,
    // drip stands between them, trolley and lamp at the foot (west) end
    static readonly Spot[] CeSpots =
    {
        new Spot("ceBed", 239f, -110f, 270f),
        new Spot("ceMachine1", 239f, -101f, 180f), new Spot("ceMachine2", 248f, -110f, 270f), new Spot("ceMachine3", 239f, -119f, 0f),
        new Spot("ceIv1", 245.4f, -103.6f, 0f), new Spot("ceIv2", 245.4f, -116.4f, 0f), new Spot("ceIv3", 233f, -102f, 0f),
        new Spot("ceTrolley", 231.5f, -114f, 90f), new Spot("ceLamp", 231.5f, -106.5f, 90f),
        new Spot("ceSign", 212f, -60f, 270f),
    };

    static readonly Vector2[] Nudges =
    {
        Vector2.zero, new Vector2(2f, 0f), new Vector2(-2f, 0f), new Vector2(0f, 2f), new Vector2(0f, -2f),
        new Vector2(4f, 0f), new Vector2(-4f, 0f), new Vector2(0f, 4f), new Vector2(0f, -4f),
    };

    static void ArrangeRoom(Scene scene, string room, Spot[] spots)
    {
        var root = PropsRoot(scene);
        foreach (var s in spots)
        {
            var t = root.Find(s.id);
            if (t == null) { Debug.Log($"{Title}: {s.id} isn't there (deleted by hand?); not moved."); continue; }
            if (!MoveClear(scene, t, s)) Debug.LogWarning($"{Title}: {s.id} ({room}) overlaps something at every nudge; left at ({s.x}, {s.z}).");
            Debug.Log($"{Title}: {room}: {s.id} at {t.position}, yaw {t.eulerAngles.y:0}.");
        }
    }

    // Moves the prop to the spot (or a nudge of it) whose box is clear; false, leaving it on the spot itself, if none is
    static bool MoveClear(Scene scene, Transform t, Spot s)
    {
        foreach (var n in Nudges)
        {
            float x = s.x + n.x, z = s.z + n.y;
            Move(scene, s.id, new Vector3(x, FloorY(scene, x, z), z), s.yaw);
            if (OverlapsWith(scene, t).Count == 0) return true;
        }
        Move(scene, s.id, new Vector3(s.x, FloorY(scene, s.x, s.z), s.z), s.yaw);
        return false;
    }

    // The floor (about y 0) under (x, z), ignoring school props
    static float FloorY(Scene scene, float x, float z) => Ground(scene, new Vector3(x, 0f, z), 3f, 1f);
}
