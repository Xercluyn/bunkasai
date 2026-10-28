using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// The fifth floor's arcade game (Assets/Minigame/FifthFloorGame/GAMEPLAY.txt): a transport plane drops the
/// agent onto a rooftop helipad, the agent auto-runs along an apartment walkway delivering newspapers, and a
/// plane snatches the agent off the far helipad at the end.
///  - Green doors: throw (Space / click) as the shrinking ring meets the target ring. PERFECT / GOOD score
///    points and build a combo; running past without throwing is a MISS (no points lost, combo reset).
///  - Red doors: traps with the same ring. Throwing costs points; leaving them alone earns a small bonus.
///  - Yellow doors: plain rooms, nothing happens.
/// Clearing it marks the fifth floor's quest as done and returns there.
/// The scene is made by Tools > Elevator Game > Create Shinbun Arcade Scene, which fills in the models.
/// </summary>
public class ShinbunGame : MonoBehaviour
{
    [Header("Models (filled in by the Create Shinbun Arcade Scene tool)")]
    public GameObject hallwayPlain;
    public GameObject hallwayDeliver;
    public GameObject hallwayTrap;
    public GameObject padStart;
    public GameObject padEnd;
    public GameObject plane;
    public GameObject agent;
    public RuntimeAnimatorController agentAnimator;
    public Material buildingMaterial;
    public Material skylineMaterial;

    [Header("Course")]
    [Tooltip("Number of hallway pieces between the two helipads.")]
    public int hallways = 7;   // easy mode (was 14)

    [Tooltip("Back to the floor as soon as the run reaches the end, skipping the plane pickup and the results.")]
    public bool quickFinish = true;
    public float runSpeed = 7f;

    [Header("Agent")]
    [Tooltip("Agent height as a fraction of the coloured door (leaf and frame).")]
    [Range(0.3f, 1.2f)]
    public float agentSize = 0.8f;

    [Header("Timing (seconds)")]
    [Tooltip("How long the ring takes to close before the moment to press.")]
    public float promptTime = 1.3f;
    public float perfectWindow = 0.1f;
    public float goodWindow = 0.35f;
    [Tooltip("How long the agent stops to throw.")]
    public float throwTime = 0.7f;

    [Header("Scoring")]
    public int perfectPoints = 100;
    public int goodPoints = 50;
    [Tooltip("Extra points per combo step on each delivery.")]
    public int comboBonus = 10;
    public int trapPenalty = 100;
    public int dodgePoints = 30;

    [Header("Plane")]
    public float planeAltitude = 30f;
    public float planeSpeed = 40f;
    [Tooltip("Extra rotation if the plane model still flies sideways or upside down.")]
    public Vector3 planeRotationFix;

    [Tooltip("Scene to go back to; its quest is cleared when the mission is complete.")]
    [SceneName] public string returnScene = "FifthFloorScene";

    float agentHeight = 1.8f;
    float S => agentHeight / 1.8f;   // camera framing below was laid out for a 1.8 tall agent

    ShinbunLevel level;
    ShinbunHud hud;
    Camera cam;
    Transform agentT, planeT, skyline;
    Animator anim;
    Vector3 planeCenterLocal, planeTailLocal;
    AudioSource audioSource;
    AudioClip sfxGood, sfxBad, sfxWin;
    int score, combo, delivered, deliverTotal, perfects, trapsHit;
    string animState;

    IEnumerator Start()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        cam = Camera.main;
        audioSource = gameObject.AddComponent<AudioSource>();
        sfxGood = Sfx.Good();
        sfxBad = Sfx.Bad();
        sfxWin = Sfx.Win();

        var layout = MakeLayout(Mathf.Max(3, hallways));
        deliverTotal = layout.Count(k => k == DoorKind.Deliver);
        level = ShinbunLevel.Build(this, layout);
        hud = new ShinbunHud(this, () => Leave(false));
        UpdateScore();
        SpawnAgent();
        SpawnPlane();
        SpawnSkyline();

        yield return Intro();
        yield return Tutorial();
        yield return Run();
        if (quickFinish)
        {
            // reaching the end of the hallways wins: back to the floor right away
            audioSource.PlayOneShot(sfxWin);
            hud.Caption(L.T("ゴール！クリア！", "Goal! Cleared!"));
            yield return new WaitForSeconds(1.2f);
            Leave(true);
            yield break;
        }
        yield return Ending();

        audioSource.PlayOneShot(sfxWin);
        hud.ShowResults(score, delivered, deliverTotal, perfects, trapsHit,
            () => Leave(true), () => SceneManager.LoadScene(gameObject.scene.name));
    }

    // Plain first and last; in between mostly deliveries with some traps, never two traps in a row
    static List<DoorKind> MakeLayout(int count)
    {
        for (int attempt = 0; ; attempt++)
        {
            var list = new List<DoorKind> { DoorKind.Plain };
            for (int i = 1; i < count - 1; i++)
            {
                float r = Random.value;
                var kind = r < 0.45f ? DoorKind.Deliver : r < 0.7f ? DoorKind.Trap : DoorKind.Plain;
                if (kind == DoorKind.Trap && list[i - 1] == DoorKind.Trap) kind = DoorKind.Plain;
                list.Add(kind);
            }
            list.Add(DoorKind.Plain);
            int deliveries = list.Count(k => k == DoorKind.Deliver), traps = list.Count(k => k == DoorKind.Trap);
            if ((deliveries >= count / 4 && traps >= count / 7) || attempt > 50) return list;
        }
    }

    // ---------- setup ----------

    void SpawnAgent()
    {
        var go = Instantiate(agent);
        go.name = "Agent";
        agentT = go.transform;
        anim = go.GetComponentInChildren<Animator>();
        if (anim == null) anim = go.AddComponent<Animator>();
        anim.runtimeAnimatorController = agentAnimator;
        anim.applyRootMotion = false;
        anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        agentHeight = agentSize * level.doorFrameHeight;
        float h = HumanHeight(anim, go);
        if (h > 0f) agentT.localScale *= agentHeight / h;
    }

    // Height from the skeleton (head to feet), which skinned-mesh bounds can get wrong; bounds as a fallback
    static float HumanHeight(Animator a, GameObject go)
    {
        if (a != null && a.isHuman)
        {
            var head = a.GetBoneTransform(HumanBodyBones.Head);
            var left = a.GetBoneTransform(HumanBodyBones.LeftFoot);
            var right = a.GetBoneTransform(HumanBodyBones.RightFoot);
            if (head != null && left != null && right != null)
            {
                float span = head.position.y - Mathf.Min(left.position.y, right.position.y);
                if (span > 0f) return span * 1.15f;   // head bone sits below the top of the head, ankles above the floor
            }
        }
        return ShinbunLevel.BoundsOf(go).size.y;
    }

    void SpawnPlane()
    {
        planeT = Instantiate(plane).transform;
        planeT.name = "Transport Plane";
        planeT.localScale *= level.scale;
        ShinbunLevel.OrientPlane(planeT, level.runDir, planeRotationFix);

        // The agent rides at the open tail ramp, a little below the middle of the fuselage
        Bounds b = ShinbunLevel.BoundsOf(planeT.gameObject);
        float length = Mathf.Abs(Vector3.Dot(b.extents, level.runDir));
        planeCenterLocal = planeT.InverseTransformPoint(b.center);
        planeTailLocal = planeT.InverseTransformPoint(b.center - level.runDir * length * 0.8f - Vector3.up * b.extents.y * 0.15f);
    }

    // The skyline picture all the way round, far off and centred on the camera, so the city surrounds the
    // course in every direction. Above it the camera clears to the picture's top sky colour and below it
    // sits a disc in the city's base colour, so neither edge shows.
    void SpawnSkyline()
    {
        if (skylineMaterial == null) return;
        var tex = skylineMaterial.mainTexture;
        if (tex != null)
        {
            tex.wrapModeU = TextureWrapMode.Repeat;   // round the ring
            tex.wrapModeV = TextureWrapMode.Clamp;    // no dark seam from the bottom row along the top
        }
        float aspect = tex != null ? (float)tex.height / tex.width : 0.5f;
        float tileWidth = 2f * Mathf.PI * SkylineRadius / SkylineTiles;
        float height = tileWidth * aspect;

        skyline = new GameObject("Skyline").transform;
        var ring = new GameObject("Ring", typeof(MeshFilter), typeof(MeshRenderer));
        ring.transform.SetParent(skyline, false);
        ring.GetComponent<MeshFilter>().sharedMesh = RingMesh(SkylineRadius, height, SkylineTiles, 128);
        var r = ring.GetComponent<MeshRenderer>();
        r.sharedMaterial = skylineMaterial;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var ground = GameObject.CreatePrimitive(PrimitiveType.Quad);
        ground.name = "City Ground";
        Destroy(ground.GetComponent<Collider>());
        ground.transform.SetParent(skyline, false);
        ground.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // facing up
        ground.transform.localScale = Vector3.one * SkylineRadius * 2.2f;
        var gr = ground.GetComponent<Renderer>();
        gr.material = new Material(skylineMaterial) { mainTexture = null, color = UiKit.Hex("#355371") };
        gr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // The picture's rooftops sit about a third of the way up it; put them just at eye level
        skyline.position = new Vector3(0f, -0.38f * height, 0f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = UiKit.Hex("#58a8eb");
    }

    const float SkylineRadius = 600f;
    const int SkylineTiles = 8;

    // Open cylinder seen from inside: the picture repeated `tiles` times round it, bottom at y = 0
    static Mesh RingMesh(float radius, float height, int tiles, int segments)
    {
        var verts = new Vector3[(segments + 1) * 2];
        var uvs = new Vector2[verts.Length];
        for (int i = 0; i <= segments; i++)
        {
            float a = 2f * Mathf.PI * i / segments;
            var p = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            float u = -(float)i / segments * tiles;   // runs left to right when seen from inside
            verts[i * 2] = p;
            verts[i * 2 + 1] = p + Vector3.up * height;
            uvs[i * 2] = new Vector2(u, 0f);
            uvs[i * 2 + 1] = new Vector2(u, 1f);
        }
        var tris = new int[segments * 12];
        for (int i = 0, t = 0; i < segments; i++)
        {
            int b0 = i * 2, t0 = b0 + 1, b1 = b0 + 2, t1 = b0 + 3;
            // both windings, so it shows whichever way round the faces turn out
            int[] quad = { b0, t0, b1, b1, t0, t1, b0, b1, t0, b1, t1, t0 };
            foreach (int v in quad) tris[t++] = v;
        }
        var mesh = new Mesh { name = "Skyline Ring", vertices = verts, uv = uvs, triangles = tris };
        mesh.RecalculateBounds();
        return mesh;
    }

    void LateUpdate()
    {
        // Always centred on the camera, so it never gets closer
        if (skyline != null && cam != null)
        {
            var p = cam.transform.position;
            skyline.position = new Vector3(p.x, skyline.position.y, p.z);
        }
    }

    // ---------- 1. intro: drop from the plane onto the helipad ----------

    IEnumerator Intro()
    {
        Vector3 pad = level.startPad;
        PlacePlane(new Vector3(pad.x, pad.y + planeAltitude, pad.z) - level.runDir * 150f);
        agentT.SetParent(planeT, true);   // rides along, keeping its size
        agentT.position = planeT.TransformPoint(planeTailLocal);
        agentT.rotation = Quaternion.LookRotation(level.runDir);
        Play("Hang");

        // Watch from beside the helipad as the plane comes in
        Vector3 watch = pad + level.cameraSide * 26f + Vector3.up * 3f - level.runDir * 8f;
        SnapCamera(watch, PlaneCenter());
        while (Vector3.Dot(agentT.position - pad, level.runDir) < 0f)
        {
            MovePlane();
            MoveCamera(watch, Vector3.Lerp(PlaneCenter(), agentT.position, 0.5f), 4f);
            yield return null;
        }

        // Jump: fall onto the pad; press as the ring closes to land cleanly
        agentT.SetParent(null, true);
        agentT.rotation = Quaternion.LookRotation(level.runDir);
        hud.Caption(L.T("リングが重なったら スペース で着地！", "Press Space when the rings meet to land!"));
        const float gravity = 30f, maxFall = 40f;
        float v = 0f;
        bool judged = false;
        while (agentT.position.y > pad.y)
        {
            MovePlane();
            v = Mathf.Min(v + gravity * Time.deltaTime, maxFall);
            float left = TimeToFall(agentT.position.y - pad.y, v, gravity, maxFall);
            agentT.position += Vector3.down * v * Time.deltaTime;

            if (!judged && left <= promptTime)
            {
                hud.ShowPrompt(cam.WorldToScreenPoint(agentT.position + Vector3.up * agentHeight * 0.5f), 1f - left / promptTime);
                if (Pressed() && Judge(left, out bool perfect))
                {
                    judged = true;
                    hud.HidePrompt();
                    Score(perfect, perfect ? L.T("PERFECT 着地！", "PERFECT landing!") : L.T("GOOD 着地！", "GOOD landing!"));
                }
            }
            Vector3 follow = agentT.position + (level.cameraSide * 12f + Vector3.up * 1.5f) * S;
            MoveCamera(follow, agentT.position + Vector3.up * agentHeight * 0.5f, 5f);
            yield return null;
        }
        agentT.position = new Vector3(agentT.position.x, pad.y, agentT.position.z);
        hud.HidePrompt();
        hud.Caption("");
        if (!judged) Miss();

        Play("Land");
        for (float t = 0f; t < 1.3f; t += Time.deltaTime)
        {
            MovePlane();
            MoveCamera(agentT.position + (level.cameraSide * 9f + Vector3.up * 2f) * S, agentT.position + Vector3.up * S, 4f);
            yield return null;
        }
        Play("Idle");
    }

    // Seconds to fall h, starting at speed v, accelerating up to vmax
    static float TimeToFall(float h, float v, float g, float vmax)
    {
        if (h <= 0f) return 0f;
        if (v >= vmax) return h / vmax;
        float t1 = (vmax - v) / g, d1 = v * t1 + 0.5f * g * t1 * t1;
        return d1 >= h ? (-v + Mathf.Sqrt(v * v + 2f * g * h)) / g : t1 + (h - d1) / vmax;
    }

    // ---------- 2. tutorial, facing the landing spot ----------

    IEnumerator Tutorial()
    {
        Vector3 front = agentT.position + (level.runDir * 7f + level.cameraSide * 2f + Vector3.up * 2.2f) * S;
        Vector3 look = agentT.position + Vector3.up * 1.1f * S;
        bool go = false;
        var card = hud.ShowTutorial(() => go = true);
        while (!go)
        {
            var kb = Keyboard.current;
            if (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)) go = true;
            MovePlane();
            MoveCamera(front, look, 3f);
            yield return null;
        }
        Destroy(card);
    }

    // ---------- 3. auto-run along the hallways ----------

    IEnumerator Run()
    {
        hud.ShowHud(true);
        Play("Run");
        float throwUntil = 0f;
        Vector3 facing = level.runDir;
        while (Vector3.Dot(level.endPad - agentT.position, level.runDir) > 0f)
        {
            bool running = Time.time >= throwUntil;
            if (running)
            {
                if (animState != "Run") Play("Run");
                facing = level.runDir;
                agentT.position += level.runDir * runSpeed * Time.deltaTime;
            }
            agentT.rotation = Quaternion.Slerp(agentT.rotation, Quaternion.LookRotation(facing), 1f - Mathf.Exp(-12f * Time.deltaTime));

            var door = level.doors.FirstOrDefault(d => !d.resolved && d.kind != DoorKind.Plain);
            if (door != null)
            {
                // Seconds until the agent is level with the door (holds still while throwing)
                float t = Vector3.Dot(door.center - agentT.position, level.runDir) / runSpeed;
                if (t <= promptTime) hud.ShowPrompt(cam.WorldToScreenPoint(door.center), 1f - t / promptTime);
                else hud.HidePrompt();

                if (running && t <= promptTime && Pressed() && Judge(t, out bool perfect))
                {
                    door.resolved = true;
                    hud.HidePrompt();
                    if (door.kind == DoorKind.Deliver)
                    {
                        delivered++;
                        if (perfect) perfects++;
                        Score(perfect, perfect ? "PERFECT!" : "GOOD!");
                    }
                    else
                    {
                        TrapHit();
                    }
                    throwUntil = Time.time + throwTime;
                    facing = new Vector3(door.center.x, agentT.position.y, door.center.z) - agentT.position;
                    if (facing.sqrMagnitude < 1e-4f) facing = -level.cameraSide;
                    Play("Throw");
                    StartCoroutine(ThrowPaper(door.center));
                }
                else if (t < -goodWindow)
                {
                    door.resolved = true;
                    hud.HidePrompt();
                    if (door.kind == DoorKind.Deliver) Miss();
                    else Dodge();
                }
            }

            Vector3 camPos = agentT.position + (level.cameraSide * 8.5f + Vector3.up * 2.6f + level.runDir * 1.5f) * S;
            MoveCamera(camPos, agentT.position + (Vector3.up * 1.1f + level.runDir * 2.5f) * S, 5f);
            yield return null;
        }
        hud.HidePrompt();
    }

    // A folded newspaper arcs from the agent's hand into the door
    IEnumerator ThrowPaper(Vector3 target)
    {
        yield return new WaitForSeconds(0.25f);
        var paper = GameObject.CreatePrimitive(PrimitiveType.Cube);
        paper.name = "Newspaper";
        Destroy(paper.GetComponent<Collider>());
        paper.transform.localScale = new Vector3(0.4f, 0.08f, 0.3f) * S;
        var r = paper.GetComponent<Renderer>();
        r.material.color = UiKit.Hex("#f2efe6");
        Vector3 from = agentT.position + Vector3.up * agentHeight * 0.7f;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.35f)
        {
            paper.transform.position = Vector3.Lerp(from, target, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * 0.6f * S;
            paper.transform.rotation = Quaternion.Euler(0f, t * 720f, 0f);
            yield return null;
        }
        Destroy(r.material);
        Destroy(paper);
    }

    // ---------- 4. ending: skyhook pick-up from the far helipad ----------

    IEnumerator Ending()
    {
        Vector3 pad = level.endPad;
        agentT.position = pad;
        agentT.rotation = Quaternion.LookRotation(level.runDir);
        Play("Idle");
        hud.Caption(L.T("ヘリパッドに到着！迎えの飛行機を待とう", "Reached the helipad! Wait for the plane to pick you up"));

        // Low angle from the helipad floor, looking up at the agent and the sky
        Vector3 low = pad + (level.cameraSide * 5f - level.runDir * 4f + Vector3.up * 0.4f) * S;
        PlacePlane(new Vector3(pad.x, pad.y + planeAltitude * 0.7f, pad.z) - level.runDir * 170f);
        for (float t = 0f; t < 1.2f; t += Time.deltaTime)
        {
            MovePlane();
            MoveCamera(low, agentT.position + Vector3.up * agentHeight * 2.5f, 3f);
            yield return null;
        }
        Play("Hang");   // arms up, ready to be hooked

        bool hooked = false;
        float after = 0f;
        while (after < 3.5f)
        {
            MovePlane();
            Vector3 tail = planeT.TransformPoint(planeTailLocal);
            if (!hooked && Vector3.Dot(tail - agentT.position, level.runDir) >= 0f)
            {
                hooked = true;
                hud.Caption("");
                audioSource.PlayOneShot(sfxGood);
            }
            if (hooked)
            {
                after += Time.deltaTime;
                agentT.position = Vector3.MoveTowards(agentT.position, tail, 45f * Time.deltaTime);
                if ((agentT.position - tail).sqrMagnitude < 0.01f) agentT.position = tail;
            }
            Vector3 look = hooked ? Vector3.Lerp(agentT.position, PlaneCenter(), 0.35f) : PlaneCenter();
            MoveCamera(low, look, 3f);
            yield return null;
        }
    }

    // ---------- scoring ----------

    // True when the press is close enough to count; earlier presses are ignored
    bool Judge(float timeLeft, out bool perfect)
    {
        float off = Mathf.Abs(timeLeft);
        perfect = off <= perfectWindow;
        return off <= goodWindow;
    }

    void Score(bool perfect, string text)
    {
        score += (perfect ? perfectPoints : goodPoints) + combo * comboBonus;
        combo++;
        hud.Pop(text, perfect ? UiKit.Accent : UiKit.Hex("#5ec8ff"));
        audioSource.PlayOneShot(sfxGood);
        UpdateScore();
    }

    void Miss()
    {
        combo = 0;
        hud.Pop("MISS", UiKit.Bad);
        audioSource.PlayOneShot(sfxBad);
        UpdateScore();
    }

    void TrapHit()
    {
        trapsHit++;
        combo = 0;
        score = Mathf.Max(0, score - trapPenalty);
        hud.Pop(L.T($"トラップ！ -{trapPenalty}", $"Trap! -{trapPenalty}"), UiKit.Bad);
        audioSource.PlayOneShot(sfxBad);
        StartCoroutine(Shake(0.35f, 0.15f));
        UpdateScore();
    }

    void Dodge()
    {
        score += dodgePoints;
        hud.Pop(L.T($"回避！ +{dodgePoints}", $"Dodged! +{dodgePoints}"), UiKit.Good);
        UpdateScore();
    }

    void UpdateScore() => hud.SetScore(score, combo, delivered, deliverTotal);

    IEnumerator Shake(float seconds, float amount)
    {
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            cam.transform.position += Random.insideUnitSphere * amount * (1f - t / seconds);
            yield return null;
        }
    }

    void Leave(bool cleared)
    {
        if (cleared) GameProgress.ClearQuest(returnScene);
        if (!Application.CanStreamedLevelBeLoaded(returnScene))
        {
            Debug.LogError($"ShinbunGame: scene '{returnScene}' is not in Build Settings. " +
                           "Run Tools > Elevator Game > Create Shinbun Arcade Scene to add it.");
            return;
        }
        SceneManager.LoadScene(returnScene);
    }

    // ---------- helpers ----------

    static bool Pressed()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        return (kb != null && kb.spaceKey.wasPressedThisFrame) ||
               (mouse != null && mouse.leftButton.wasPressedThisFrame && !overUi);
    }

    void Play(string state)
    {
        animState = state;
        if (anim != null && anim.HasState(0, Animator.StringToHash(state))) anim.CrossFadeInFixedTime(state, 0.15f);
    }

    Vector3 PlaneCenter() => planeT.TransformPoint(planeCenterLocal);
    void PlacePlane(Vector3 center) => planeT.position += center - PlaneCenter();
    void MovePlane() => planeT.position += level.runDir * planeSpeed * Time.deltaTime;

    void SnapCamera(Vector3 position, Vector3 look)
    {
        cam.transform.position = position;
        cam.transform.rotation = Quaternion.LookRotation(look - position);
    }

    void MoveCamera(Vector3 position, Vector3 look, float sharpness)
    {
        float k = 1f - Mathf.Exp(-sharpness * Time.deltaTime);
        cam.transform.position = Vector3.Lerp(cam.transform.position, position, k);
        Vector3 dir = look - cam.transform.position;
        if (dir.sqrMagnitude > 1e-6f)
            cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, Quaternion.LookRotation(dir), k);
    }
}
