using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using static EditorKit;
using static SchoolKit;

/// <summary>
/// School > Steps > Hang Festival Posters: the 学園祭 poster (Assets/Models/School/festival_poster.fbx, picture
/// Assets/Textures/School/festival_poster.jpeg) along each floor's player route, where it's easy to see:
///  - wall posters (Cube.003) on walls in view of the spawn point, the quest zones and the elevator, and on the
///    walls beside the way from the spawn to them;
///  - floor stands (Cube.002) near the spawn and the quest zone, turned to the player;
///  - two-faced banners (Plane and Plane.001 back to back) hung from the ceiling over the route, above head height.
/// Every spot is checked: the wall continues behind the whole poster, nothing stands in front of it, and it's in
/// view from the route; doors, elevators, windows and the minigame objects are left bare. Sizes follow the
/// player's height, so every floor's scale works. Posters are kept where they are once placed and stay out if
/// deleted by hand. Dress School runs it after Arrange Rooms.
/// </summary>
public static class FestivalPosters
{
    const string Title = "Hang Festival Posters";
    const string ModelPath = "Assets/Models/School/festival_poster.fbx";
    const string TexturePath = TextureFolder + "/festival_poster.jpeg";
    const string StandPart = "Cube.002", WallPart = "Cube.003", BannerPart = "Plane", BannerBackPart = "Plane.001";
    const string PosterSlot = "Material", FrameSlot = "Material.001";   // the FBX's material names

    // How many per floor
    const int WallCount = 4, StandCount = 2, BannerCount = 2;

    // In multiples of the player's height (the floors are scaled differently)
    const float WallHeight = 0.8f, WallCentre = 0.78f;     // poster height, and its centre above the floor
    const float StandHeight = 1.0f;
    const float BannerMax = 0.9f, BannerMin = 0.35f;       // banner height range
    const float HeadRoom = 1.05f;                           // banners hang above this
    const float Eye = 0.6f;                                 // where the route's view rays start
    const float AnchorReach = 4f, SideReach = 3.5f;         // how far walls may be from what looks at them

    // Bump when the sizes change: the next run takes the floor's posters down once and hangs them again at the
    // new size (ones deleted by hand stay out)
    const string SizeStep = "posters_size_v2";
    const float MinWallDistance = 0.6f;
    const float Spacing = 3f;                               // between two posters of a kind
    const float SampleStep = 0.5f;                          // along the route

    enum AnchorKind { Elevator, Quest, Spawn }

    sealed class Anchor
    {
        public string name;
        public Vector3 point;   // on the floor
        public AnchorKind kind;
    }

    sealed class Spot
    {
        public Vector3 position;
        public float yaw, size, score;
        public string near;
    }

    sealed class Parts
    {
        public GameObject wall, stand, banner;
        public float wallAspect, bannerAspect;   // width / height
        public Vector2 standFootprint;           // width, depth per unit of height
    }

    /// <summary>Hangs the posters on floor `floor` (any of the five).</summary>
    internal static void Apply(Scene scene, int floor)
    {
        var parts = BuildParts();
        if (parts == null) return;
        var character = FloorPlayerSetup.FindCharacter(scene);
        float h = character != null ? CharacterAnimationSetup.Height(character) : 0f;
        if (h <= 0f) { Debug.LogWarning($"{Title}: no player in {scene.name}; its height sets the poster sizes. Run Setup Player On This Floor first."); return; }

        if (Once(scene, SizeStep))
        {
            string prefix = $"f{floor}_poster_";
            var old = PropsRoot(scene).Cast<Transform>().Where(t => t.name.StartsWith(prefix)).Select(t => t.name).ToList();
            foreach (var id in old) Forget(scene, id);
            if (old.Count > 0) Debug.Log($"{Title}: {scene.name}: took down {old.Count} poster(s) to hang them again at the new size.");
        }

        bool backfaces = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;   // single-sided walls and ceilings count from either side
        try
        {
            Physics.SyncTransforms();
            var run = new Survey(scene, floor, character.transform, h);
            int walls = Put(run, "wall", parts.wall, run.Walls(parts.wallAspect, WallCount));
            int stands = Put(run, "stand", parts.stand, run.Stands(parts.standFootprint, StandCount));
            int banners = Put(run, "banner", parts.banner, run.Banners(parts.bannerAspect, BannerCount));
            string rejects = string.Join(", ", run.rejects.OrderByDescending(r => r.Value).Select(r => $"{r.Key} {r.Value}"));
            Debug.Log($"{Title}: {scene.name}: {walls} wall poster(s), {stands} floor stand(s), {banners} ceiling banner(s) placed " +
                      $"({run.anchors.Count} route point(s), {run.samples.Count} route sample(s), player {h:0.#} tall); posters already there " +
                      $"or deleted by hand are left as they are. Spots turned down: {(rejects.Length > 0 ? rejects : "none")}.");
        }
        finally
        {
            Physics.queriesHitBackfaces = backfaces;
        }
    }

    // Places spot k as f{floor}_poster_{kind}_{k}, unless that id is already placed or was deleted by hand
    static int Put(Survey run, string kind, GameObject prefab, List<Spot> spots)
    {
        int placed = 0;
        for (int k = 0; k < spots.Count; k++)
        {
            string id = $"f{run.floor}_poster_{kind}_{k + 1}";
            if (IsPlaced(run.scene, id)) continue;
            var s = spots[k];
            var go = Place(run.scene, id, prefab, s.position, s.yaw);
            if (go == null) continue;
            go.transform.localScale = Vector3.one * s.size;   // the prefab is one unit tall
            Physics.SyncTransforms();
            placed++;
            Debug.Log($"{Title}: placed {id} near {s.near} at {s.position}, yaw {s.yaw:0}, {s.size:0.##} tall.", go);
        }
        return placed;
    }

    // ---------- finding the spots ----------

    sealed class Survey
    {
        public readonly Scene scene;
        public readonly int floor;
        public readonly float h, floorY;
        public readonly List<Anchor> anchors = new List<Anchor>();
        public readonly List<(Vector3 at, Vector3 dir, float along)> samples = new List<(Vector3, Vector3, float)>();
        readonly Transform me, props;
        readonly HashSet<Transform> bare = new HashSet<Transform>();   // objects that get no poster
        readonly PhysicsScene physics;
        public readonly Dictionary<string, int> rejects = new Dictionary<string, int>();   // why spots were turned down

        bool Reject(string why)
        {
            rejects[why] = rejects.TryGetValue(why, out int n) ? n + 1 : 1;
            return false;
        }

        public Survey(Scene scene, int floor, Transform me, float h)
        {
            this.scene = scene;
            this.floor = floor;
            this.me = me;
            this.h = h;
            physics = scene.GetPhysicsScene();
            props = PropsRoot(scene);
            floorY = PlayerFloorY(scene, me.position.y);
            anchors.Add(new Anchor { name = "the spawn", point = OnFloor(me.position), kind = AnchorKind.Spawn });
            foreach (var zone in All<ElevatorPlayZone>(scene))
            {
                var c = zone.GetComponent<Collider>();
                bool elevator = zone is ElevatorExitZone ||
                                (zone.interactable != null && zone.interactable.name.ToLowerInvariant().Contains("elevator"));
                anchors.Add(new Anchor
                {
                    name = zone.name, point = OnFloor(c != null ? c.bounds.center : zone.transform.position),
                    kind = elevator ? AnchorKind.Elevator : AnchorKind.Quest,
                });
                if (zone.interactable != null) bare.Add(zone.interactable.transform);
            }
            TraceRoutes();
        }

        Vector3 OnFloor(Vector3 p) => new Vector3(p.x, floorY, p.z);
        Vector3 EyeAt(Vector3 p) => new Vector3(p.x, floorY + Eye * h, p.z);

        // The nearest solid hit (not the player); props and everything else count, so they block the view
        bool Cast(Vector3 from, Vector3 dir, float range, out RaycastHit nearest)
        {
            var hits = new RaycastHit[32];
            int n = physics.Raycast(from, dir, hits, range, ~0, QueryTriggerInteraction.Ignore);
            nearest = default;
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (hits[i].distance <= 0f || hits[i].collider.transform.IsChildOf(me)) continue;
                if (hits[i].distance < best) { best = hits[i].distance; nearest = hits[i]; }
            }
            return best < float.MaxValue;
        }

        bool Clear(Vector3 from, Vector3 to) => !Cast(from, (to - from).normalized, Vector3.Distance(from, to) * 0.97f, out _);

        // Building surface a poster may go on: not a prop, door, elevator, window or minigame object. Names are
        // checked on the part and its parent only: a building's group names would rule out whole floors
        bool Postable(Collider c)
        {
            if (c.transform.IsChildOf(props)) return false;
            for (var t = c.transform; t != null; t = t.parent)
                if (bare.Contains(t) || t.GetComponent<InteractableOutline>() != null || t.GetComponent<ElevatorPlayZone>() != null) return false;
            for (var t = c.transform; t != null && t != c.transform.parent?.parent; t = t.parent)
            {
                string n = t.name.ToLowerInvariant();
                if (n.Contains("door") || n.Contains("elevator") || n.Contains("window") || n.Contains("glass")) return false;
            }
            return true;
        }

        // Spawn → each zone at eye height, cut where something blocks the straight way from either end
        void TraceRoutes()
        {
            var spawn = anchors[0];
            foreach (var a in anchors.Skip(1))
            {
                Vector3 from = EyeAt(spawn.point), to = EyeAt(a.point);
                Vector3 dir = to - from;
                float length = dir.magnitude;
                if (length < h) continue;
                dir /= length;
                float open = Cast(from, dir, length, out var hit) ? hit.distance : length;
                float back = Cast(to, -dir, length, out hit) ? hit.distance : length;
                for (float d = h; d < length - h * 0.5f; d += SampleStep * h)
                    if (d <= open - h * 0.5f || length - d <= back - h * 0.5f) samples.Add((from + dir * d, dir, d / length));
            }
        }

        // ---------- wall posters ----------

        public List<Spot> Walls(float aspect, int count)
        {
            var cands = new List<(Spot spot, Anchor from)>();
            // walls in view of each route point, all round it
            foreach (var a in anchors)
                for (int i = 0; i < 32; i++)
                {
                    var dir = Quaternion.Euler(0f, i * 11.25f, 0f) * Vector3.forward;
                    var s = WallSpot(EyeAt(a.point), dir, AnchorReach * h, aspect, a.name);
                    if (s != null) cands.Add((s, a));
                }
            // walls beside the way between them
            foreach (var (at, dir, _) in samples)
                foreach (float side in new[] { -1f, 1f })
                {
                    var s = WallSpot(at, Vector3.Cross(Vector3.up, dir) * side, SideReach * h, aspect, "the route");
                    if (s != null) { s.score += 0.5f; cands.Add((s, null)); }
                }

            var picks = new List<Spot>();
            // one for each elevator, quest zone and the spawn first, then the best of the rest
            foreach (var a in anchors.OrderBy(a => a.kind))
            {
                if (picks.Count >= count) break;
                var best = cands.Where(c => c.from == a && FarFrom(picks, c.spot)).OrderBy(c => c.spot.score).FirstOrDefault();
                if (best.spot != null) picks.Add(best.spot);
            }
            foreach (var c in cands.OrderBy(c => c.spot.score))
            {
                if (picks.Count >= count) break;
                if (FarFrom(picks, c.spot)) picks.Add(c.spot);
            }
            return picks;
        }

        bool FarFrom(List<Spot> picks, Spot s) => picks.All(p => Vector3.Distance(p.position, s.position) >= Spacing * h);

        // A poster on the wall a view ray hits: facing back along the ray, centred at WallCentre, the wall flat and
        // continuous behind the whole poster and nothing in front of it
        Spot WallSpot(Vector3 from, Vector3 dir, float range, float aspect, string near)
        {
            if (!Cast(from, dir, range, out var hit)) return null;   // open space: nothing to turn down
            if (hit.distance < MinWallDistance * h) { Reject("too close"); return null; }
            if (Mathf.Abs(hit.normal.y) > 0.3f) { Reject("not a wall"); return null; }
            if (!Postable(hit.collider)) { Reject("prop, door or minigame object"); return null; }
            var n = new Vector3(hit.normal.x, 0f, hit.normal.z).normalized;
            if (Vector3.Dot(n, dir) > 0f) n = -n;            // a back face: turn it toward the viewer
            if (Vector3.Dot(n, -dir) < 0.4f) { Reject("too glancing"); return null; }

            float tall = WallHeight * h, halfW = aspect * tall * 0.5f, halfH = tall * 0.5f;
            var centre = new Vector3(hit.point.x, floorY + WallCentre * h, hit.point.z);
            var side = Vector3.Cross(Vector3.up, n);
            float lead = 0.3f * h;
            foreach (var (sx, sy) in new[] { (0f, 0f), (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
            {
                var p = centre + side * (sx * halfW * 0.85f) + Vector3.up * (sy * halfH * 0.85f) + n * lead;
                if (!Cast(p, -n, lead * 2f, out var back) || Mathf.Abs(back.distance - lead) > 0.08f * h || !Postable(back.collider))
                {
                    Reject("wall too small or uneven");
                    return null;
                }
            }
            float depth = 0.05f * h;
            var boxCentre = centre + n * (0.06f * h + depth);
            if (physics.OverlapBox(boxCentre, new Vector3(halfW * 0.9f, halfH * 0.9f, depth), new Collider[1], Quaternion.LookRotation(n),
                                   ~0, QueryTriggerInteraction.Ignore) > 0) { Reject("something in front of the wall"); return null; }
            return new Spot
            {
                position = centre + n * (0.005f * h), yaw = Quaternion.LookRotation(n).eulerAngles.y,
                size = tall, score = hit.distance / h, near = near,
            };
        }

        // ---------- floor stands ----------

        // Near the spawn and the quest zones (else the elevator), turned to the spawn's side, in view of it
        public List<Spot> Stands(Vector2 footprint, int count)
        {
            var spawn = anchors[0];
            var order = new[] { spawn }.Concat(anchors.Where(a => a.kind == AnchorKind.Quest))
                                       .Concat(anchors.Where(a => a.kind == AnchorKind.Elevator));
            var picks = new List<Spot>();
            float tall = StandHeight * h;
            float wide = Mathf.Max(footprint.x, footprint.y) * tall;
            var sizeM = new Vector3(wide, tall, wide) / SchoolPalette.WorldPerMetre;
            float reach = Mathf.Max(4f * h, 20f), step = Mathf.Max(0.5f * h, 3f);   // FreeSpot keeps 12 units clear of the spawn
            foreach (var a in order)
            {
                if (picks.Count >= count) break;
                var look = a == spawn ? EyeAt(spawn.point) : EyeAt(Vector3.Lerp(a.point, spawn.point, 0.3f));
                var s = StandNear(a, look, sizeM, tall, reach, step, picks);
                if (s != null) picks.Add(s);
                else Reject($"no stand spot near {a.name}");
            }
            return picks;
        }

        // The nearest clear floor spot round the anchor, ring by ring, that the player sees from `look`
        Spot StandNear(Anchor a, Vector3 look, Vector3 sizeM, float tall, float reach, float step, List<Spot> picks)
        {
            for (float r = step; r <= reach; r += step)
            {
                int count = Mathf.Max(8, Mathf.CeilToInt(2f * Mathf.PI * r / step));
                for (int i = 0; i < count; i++)
                {
                    float angle = i * Mathf.PI * 2f / count;
                    var at = a.point + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * r;
                    var p = FreeSpot(scene, at, sizeM, 0f, 0f, 0f);   // just this point
                    if (p == null) continue;
                    if (Mathf.Abs(p.Value.y - floorY) > 0.3f * h) continue;                    // on furniture, or another level
                    if (!Clear(look, p.Value + Vector3.up * tall * 0.6f)) continue;            // out of sight
                    var toward = look - p.Value;
                    toward.y = 0f;
                    var s = new Spot
                    {
                        position = p.Value, yaw = Quaternion.LookRotation(toward.sqrMagnitude > 1e-4f ? toward : Vector3.forward).eulerAngles.y,
                        size = tall, near = a.name,
                    };
                    if (FarFrom(picks, s)) return s;
                }
            }
            return null;
        }

        // ---------- ceiling banners ----------

        // Over the route, nearest the middle of each way first: hung from the ceiling, its bottom above head height
        public List<Spot> Banners(float aspect, int count)
        {
            var picks = new List<Spot>();
            foreach (var (at, dir, along) in samples.OrderBy(s => Mathf.Abs(s.along - 0.5f)))
            {
                if (picks.Count >= count) break;
                if (!Cast(at, Vector3.up, 3f * h, out var top)) { Reject("no ceiling above the route"); continue; }
                if (Mathf.Abs(top.normal.y) < 0.7f || !Postable(top.collider)) { Reject("not a ceiling"); continue; }
                float tall = Mathf.Min(BannerMax * h, top.point.y - 0.02f * h - (floorY + HeadRoom * h));
                if (tall < BannerMin * h) { Reject("ceiling too low for a banner"); continue; }
                var hang = new Vector3(at.x, top.point.y - 0.02f * h, at.z);
                var facing = Quaternion.LookRotation(-dir);
                float halfW = aspect * tall * 0.5f;
                var boxCentre = hang + Vector3.down * (tall * 0.5f + 0.02f * h);
                if (physics.OverlapBox(boxCentre, new Vector3(halfW, tall * 0.5f - 0.02f * h, 0.05f * h), new Collider[1], facing,
                                       ~0, QueryTriggerInteraction.Ignore) > 0) { Reject("banner would touch something"); continue; }
                var s = new Spot { position = hang, yaw = facing.eulerAngles.y, size = tall, near = "the route" };
                if (picks.All(p => Vector3.Distance(p.position, s.position) >= 1.5f * Spacing * h)) picks.Add(s);
            }
            return picks;
        }
    }

    // ---------- the poster prefabs ----------

    enum Origin { Bottom, Back, Top }

    // The texture and model imported, the picture and frame materials, and one prefab per part (one unit tall,
    // front along +Z): stand from its bottom, wall poster from its back, banner from its top
    static Parts BuildParts()
    {
        if (!File.Exists(ModelPath) || !File.Exists(TexturePath))
        {
            Debug.LogWarning($"{Title}: {ModelPath} or {TexturePath} is missing (copies of bunkasai_asset/levels_asset/poster.fbx and festival_poster.jpeg).");
            return null;
        }
        var texture = ImportTexture();
        var poster = PosterMaterial(texture);
        var frame = Mat("FestivalPosterFrame", new Color(0.65f, 0.72f, 0.63f), TexKind.Plain, 0.35f);
        ImportModel(poster, frame);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null) { Debug.LogWarning($"{Title}: couldn't load {ModelPath}."); return null; }

        var parts = new Parts();
        parts.wall = BuildPart(model, "festival_poster_wall", WallPart, null, Origin.Back, false, poster, out var size);
        parts.wallAspect = size.x;
        parts.stand = BuildPart(model, "festival_poster_stand", StandPart, null, Origin.Bottom, true, poster, out size);
        parts.standFootprint = new Vector2(size.x, size.z);
        parts.banner = BuildPart(model, "festival_poster_banner", BannerPart, BannerBackPart, Origin.Top, false, poster, out size);
        parts.bannerAspect = size.x;
        return parts.wall != null && parts.stand != null && parts.banner != null ? parts : null;
    }

    static Texture2D ImportTexture()
    {
        if (AssetImporter.GetAtPath(TexturePath) is TextureImporter ti &&
            (ti.textureType != TextureImporterType.Default || !ti.sRGBTexture || ti.maxTextureSize != 2048 || ti.wrapMode != TextureWrapMode.Clamp))
        {
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.mipmapEnabled = true;
            ti.maxTextureSize = 2048;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.anisoLevel = 4;
            ti.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
    }

    // URP Lit with the poster picture, lit a little from within so it reads under the floor lights
    static Material PosterMaterial(Texture2D texture)
    {
        var mat = Mat("FestivalPoster", Color.white, TexKind.Plain, 0.25f);
        mat.SetTexture("_BaseMap", texture);
        mat.EnableKeyword("_EMISSION");
        mat.SetTexture("_EmissionMap", texture);
        mat.SetColor("_EmissionColor", Color.white * 0.35f);
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // Read/Write (the faces are measured), no colliders, and the FBX's two materials remapped to ours
    static void ImportModel(Material poster, Material frame)
    {
        if (!(AssetImporter.GetAtPath(ModelPath) is ModelImporter mi)) return;
        var map = mi.GetExternalObjectMap();
        var posterId = new AssetImporter.SourceAssetIdentifier(typeof(Material), PosterSlot);
        var frameId = new AssetImporter.SourceAssetIdentifier(typeof(Material), FrameSlot);
        bool remapped = map.TryGetValue(posterId, out var p) && p == poster && map.TryGetValue(frameId, out var f) && f == frame;
        if (remapped && mi.isReadable && !mi.addCollider) return;
        mi.isReadable = true;
        mi.addCollider = false;
        mi.AddRemap(posterId, poster);
        mi.AddRemap(frameId, frame);
        mi.SaveAndReimport();
    }

    // A copy of part `name` (and `backName` turned round behind it, for a two-faced banner) as prefab `id`
    static GameObject BuildPart(GameObject model, string id, string name, string backName, Origin origin, bool solid,
                                Material poster, out Vector3 size)
    {
        size = Vector3.one;
        var copy = Object.Instantiate(model);
        try
        {
            var front = Child(copy, name);
            if (front == null) { Debug.LogWarning($"{Title}: no '{name}' in {ModelPath}."); return null; }
            var holder = new GameObject("Holder").transform;
            front.SetParent(holder, true);
            var facing = Front(front, poster);
            if (backName != null)
            {
                var back = Child(copy, backName);
                if (back == null) { Debug.LogWarning($"{Title}: no '{backName}' in {ModelPath}."); Object.DestroyImmediate(holder.gameObject); return null; }
                back.SetParent(holder, true);
                PutBehind(front, back, facing, poster);
            }

            // Front to +Z, one unit tall, origin where it attaches
            holder.rotation = Quaternion.Euler(0f, Vector3.SignedAngle(facing, Vector3.forward, Vector3.up), 0f) * holder.rotation;
            var b = RendererBounds(holder.GetComponentsInChildren<Renderer>()).Value;
            holder.localScale *= 1f / Mathf.Max(b.size.y, 1e-6f);
            holder.position *= 1f / Mathf.Max(b.size.y, 1e-6f);
            b = RendererBounds(holder.GetComponentsInChildren<Renderer>()).Value;
            var attach = origin == Origin.Bottom ? new Vector3(b.center.x, b.min.y, b.center.z)
                       : origin == Origin.Back ? new Vector3(b.center.x, b.center.y, b.min.z)
                       : new Vector3(b.center.x, b.max.y, b.center.z);
            holder.position -= attach;
            size = b.size;
            foreach (var c in holder.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);

            var prop = NewProp(id);
            holder.SetParent(prop.transform, true);
            holder.name = name;
            var prefab = SavePrefab(prop, solid);
            Debug.Log($"{Title}: rebuilt {PrefabFolder}/{id}.prefab from {name}{(backName != null ? " + " + backName : "")} " +
                      $"({size.x:0.###} wide × {size.z:0.###} deep per unit of height).");
            return prefab;
        }
        finally
        {
            Object.DestroyImmediate(copy);
        }
    }

    static Transform Child(GameObject root, string name) =>
        root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

    // The horizontal way the picture faces: the area-weighted normal of the picture's faces; for a picture on both
    // sides (it cancels out), the part's thinnest horizontal axis
    static Vector3 Front(Transform part, Material poster)
    {
        Vector3 sum = Vector3.zero;
        float area = 0f;
        foreach (var mf in part.GetComponentsInChildren<MeshFilter>(true))
        {
            var r = mf.GetComponent<MeshRenderer>();
            var mesh = mf.sharedMesh;
            if (r == null || mesh == null || !mesh.isReadable) continue;
            var verts = mesh.vertices;
            var mats = r.sharedMaterials;
            for (int s = 0; s < mesh.subMeshCount && s < mats.Length; s++)
            {
                if (mats[s] != poster || mesh.GetTopology(s) != MeshTopology.Triangles) continue;
                var tris = mesh.GetTriangles(s);
                for (int i = 0; i < tris.Length; i += 3)
                {
                    Vector3 a = mf.transform.TransformPoint(verts[tris[i]]), b = mf.transform.TransformPoint(verts[tris[i + 1]]),
                            c = mf.transform.TransformPoint(verts[tris[i + 2]]);
                    var n = Vector3.Cross(b - a, c - a);
                    sum += n;
                    area += n.magnitude;
                }
            }
        }
        var flat = new Vector3(sum.x, 0f, sum.z);
        if (area > 0f && flat.magnitude > 0.2f * area) return flat.normalized;
        var bounds = RendererBounds(part.GetComponentsInChildren<Renderer>()) ?? new Bounds();
        Debug.Log($"{Title}: {part.name} has the picture on both sides (or none found); its thinnest side is taken as the front.");
        return bounds.size.x < bounds.size.z ? Vector3.right : Vector3.forward;
    }

    // Turns `back` round and puts it right behind `front`, so the banner shows the picture from both sides
    static void PutBehind(Transform front, Transform back, Vector3 facing, Material poster)
    {
        var backFacing = Front(back, poster);
        var fb = RendererBounds(front.GetComponentsInChildren<Renderer>()).Value;
        var bb = RendererBounds(back.GetComponentsInChildren<Renderer>()).Value;
        back.RotateAround(bb.center, Vector3.up, Vector3.SignedAngle(backFacing, -facing, Vector3.up));
        bb = RendererBounds(back.GetComponentsInChildren<Renderer>()).Value;
        float depth = Mathf.Abs(Vector3.Dot(fb.size, new Vector3(Mathf.Abs(facing.x), 0f, Mathf.Abs(facing.z))));
        back.position += fb.center - facing * depth - bb.center;
    }
}
