using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// WASD / arrow-key movement (new Input System), camera-relative, always at running pace with the Run animation,
/// with a raycast ground check. Shows a small controls guide in the upper-right corner.
/// Attach to the character. A CharacterController is added automatically.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    public float speed = 6f;
    public float turnSpeed = 12f;
    public float gravity = -9.81f;

    [Tooltip("Resize the CharacterController capsule to fit the model (useful when the model is scaled).")]
    public bool autoFitCapsule = true;

    [Tooltip("Layers that count as ground.")]
    public LayerMask groundMask = ~0;

    [Tooltip("Ground speed at which the walk clip plays at its normal rate. 0 = estimate from the character's height.")]
    public float strideSpeed = 0f;

    [Tooltip("Fastest the walk clip may play (1.5 = brisk walk). Movement speed is capped to match, so feet never slide.")]
    public float maxWalkRate = 1.8f;

    [Tooltip("Show the 操作方法 (controls) guide in the upper-right corner.")]
    public bool showControls = true;

    [Tooltip("Show where to go next (marker over the arcade box / ticket machine / elevator, edge arrow, next-step line).")]
    public bool showGuide = true;

    [Tooltip("Overall speed boost on top of everything else; the run clip plays faster to match. 1.5 = 50% faster.")]
    public float speedBoost = 1.5f;

    [Tooltip("Moving runs at this multiple of Speed (capped so the run clip's feet don't slide).")]
    public float runMultiplier = 2f;

    [Tooltip("Ground speed at which the run clip plays at its normal rate. 0 = estimate from the character's height.")]
    public float runStrideSpeed = 0f;

    [Tooltip("Fastest the run clip may play. Running speed is capped to match, so feet never slide.")]
    public float maxRunRate = 1.56f;

    CharacterController controller;
    Animator animator;   // optional: "Speed" switches Idle/Walk/Run, "WalkRate"/"RunRate" set their playback rates
    bool hasWalkRate, hasRunRate, hasRunState;
    Transform cam;
    float verticalVelocity;
    float naturalWalkSpeed, naturalRunSpeed, worldHeight;
    bool running;

    static readonly int RunState = Animator.StringToHash("Run");

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponentInChildren<Animator>();

        // Back from a minigame: stand where the player left this floor
        if (GameProgress.TakeSpot(gameObject.scene.name, out Vector3 pos, out Quaternion rot))
        {
            transform.SetPositionAndRotation(pos, rot);
            Physics.SyncTransforms();
        }

        if (Camera.main != null) cam = Camera.main.transform;
        if (autoFitCapsule) FitCapsule();

        // A typical walk cycle covers about 0.75 body-heights per second
        worldHeight = controller.height * transform.lossyScale.y;
        naturalWalkSpeed = strideSpeed > 0f ? strideSpeed : Mathf.Max(0.01f, 0.75f * worldHeight);
        // ...and a run cycle about 2 body-heights per second
        naturalRunSpeed = runStrideSpeed > 0f ? runStrideSpeed : Mathf.Max(0.01f, 2f * worldHeight);
    }

    void Start()
    {
        if (showControls) BuildControlsGuide();
        if (showGuide) new GameObject("QuestGuide").AddComponent<QuestGuide>().player = this;
        new GameObject("FloorMenu").AddComponent<FloorMenuButton>();   // back to the main menu
        // the game's ending (全フロアクリア, 最初から) on the top floor, unless the scene already has one
        if (FindAnyObjectByType<GameEnding>(FindObjectsInactive.Include) == null)
        {
            var ending = new GameObject("GameEnding").AddComponent<GameEnding>();
            ending.automatic = true;
            ending.floor = gameObject.scene.name;
        }
        if (animator == null) return;
        foreach (var p in animator.parameters)
        {
            if (p.name == "WalkRate") hasWalkRate = true;
            if (p.name == "RunRate") hasRunRate = true;
        }
        hasRunState = animator.HasState(0, RunState);
    }

    // 操作方法 panel in the upper-right corner: how to move, look and free the mouse
    Text guideTitle;
    Text[] guideLines;

    void BuildControlsGuide()
    {
        var canvas = UiKit.MakeCanvas("ControlsCanvas", 19, new Vector2(1280f, 720f));
        var panel = UiKit.Box("Controls", canvas.transform, UiKit.Hex("#241e57", 0.85f), 12f, UiKit.Line, 2f);
        var rt = panel.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-16f, -16f);
        rt.sizeDelta = new Vector2(250f, 0f);
        var v = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(16, 16, 10, 12);
        v.spacing = 3f;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        panel.raycastTarget = false;

        guideTitle = UiKit.Label(panel.transform, "", 14, UiKit.Accent, TextAnchor.MiddleLeft);
        guideLines = new Text[4];
        for (int i = 0; i < guideLines.Length; i++)
            guideLines[i] = UiKit.Label(panel.transform, "", 16, UiKit.Fg, TextAnchor.MiddleLeft);
        WriteGuide();
        L.Changed += WriteGuide;
    }

    void WriteGuide()
    {
        guideTitle.text = L.T("操作方法", "CONTROLS");
        var lines = new[]
        {
            ("WASD / ↑↓←→", L.T("移動", "Move")),
            (L.T("マウス", "Mouse"), L.T("視点を回す", "Look around")),
            ("E / Enter", L.T("決定", "Interact")),
            ("Esc", L.T("マウスカーソルを出す", "Show the mouse cursor")),
        };
        for (int i = 0; i < lines.Length; i++)
            guideLines[i].text = $"<color=#ffffff>{lines[i].Item1}</color>　<color=#a9a2d9>{lines[i].Item2}</color>";
    }

    void OnDestroy() => L.Changed -= WriteGuide;

    void FitCapsule()
    {
        var renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);

        float s = Mathf.Max(0.0001f, transform.lossyScale.y);   // capsule values are in local space
        float height = b.size.y / s;
        float radius = Mathf.Min(b.size.x, b.size.z) * 0.25f / s;

        radius = Mathf.Min(radius, height * 0.5f);

        Vector3 c = transform.InverseTransformPoint(b.center);
        float bottom = c.y - height * 0.5f;
        // Humanoid clips put the feet on the origin; skinned-mesh bounds are padded below the feet,
        // which would rest the capsule on the floor and hold the character up.
        if (animator != null && animator.isHuman) bottom = 0f;

        // Keep the capsule's bottom a little above the feet so it never rests on the floor
        // (its skin width would hold the feet up); GroundDelta keeps the feet on the ground instead.
        float lift = radius * 0.5f;
        controller.height = Mathf.Max(height - lift, radius * 2f);
        controller.radius = radius;
        controller.center = new Vector3(c.x, bottom + lift + controller.height * 0.5f, c.z);
        controller.skinWidth = radius * 0.1f;
        controller.stepOffset = Mathf.Min(0.3f / s, controller.height * 0.3f);
    }

    void Update()
    {
        var kb = Keyboard.current;
        float h = 0f, v = 0f;
        if (kb != null)
        {
            // WASD or the arrow keys
            h = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1f : 0f);
            v = (kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed || kb.downArrowKey.isPressed ? 1f : 0f);
        }

        // Camera-relative directions (flattened onto the ground plane)
        Vector3 fwd = cam != null ? cam.forward : Vector3.forward;
        Vector3 right = cam != null ? cam.right : Vector3.right;
        fwd.y = 0f; right.y = 0f;
        fwd.Normalize(); right.Normalize();

        Vector3 dir = fwd * v + right * h;
        if (dir.sqrMagnitude > 1f) dir.Normalize();

        // Moving always runs (no Shift, no walk); only an animator without a Run state falls back to walking,
        // since running speed would slide its walk clip
        running = dir.sqrMagnitude > 0.01f && (animator == null || hasRunState);

        float moveSpeed = running ? speed * runMultiplier : speed;
        if (animator != null)
        {
            // Cap ground speed to what the walk/run cycle can match, then play it at that rate so the feet don't slide
            if (running)
            {
                moveSpeed = Mathf.Min(moveSpeed, naturalRunSpeed * maxRunRate) * speedBoost;
                if (hasRunRate) animator.SetFloat("RunRate", moveSpeed / naturalRunSpeed);
            }
            else
            {
                moveSpeed = Mathf.Min(moveSpeed, naturalWalkSpeed * maxWalkRate);
                if (hasWalkRate) animator.SetFloat("WalkRate", moveSpeed / naturalWalkSpeed);
            }
            animator.SetFloat("Speed", dir.magnitude * (running ? 2f : 1f));
        }

        // Vertical: raycast ground check (works regardless of model scale)
        float dy = GroundDelta();

        controller.Move(dir * moveSpeed * Time.deltaTime + Vector3.up * dy);

        // Face movement direction
        if (dir.sqrMagnitude > 0.001f)
        {
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(dir),
                turnSpeed * Time.deltaTime);
        }
    }

    // Returns how far to move vertically this frame so the character rests on the ground below it.
    float GroundDelta()
    {
        float y = transform.position.y;
        Vector3 origin = transform.position + Vector3.up * 1f;
        var hits = Physics.RaycastAll(origin, Vector3.down, 200f, groundMask, QueryTriggerInteraction.Ignore);

        bool found = false;
        float groundY = 0f, best = float.MaxValue;
        foreach (var hit in hits)
        {
            if (hit.collider == controller || hit.collider.transform.IsChildOf(transform)) continue; // ignore self
            if (hit.distance < best) { best = hit.distance; groundY = hit.point.y; found = true; }
        }

        if (found && y <= groundY + 0.03f && verticalVelocity <= 0f)
        {
            verticalVelocity = 0f;
            return groundY - y;                      // snap onto the ground
        }

        verticalVelocity += gravity * Time.deltaTime; // falling (or walking off an edge)
        float dy = verticalVelocity * Time.deltaTime;
        if (found) dy = Mathf.Max(dy, groundY - y);   // never fall below the ground
        return dy;
    }
}
