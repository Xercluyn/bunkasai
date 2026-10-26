using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// WASD movement, Shift to run and Space to jump (new Input System), camera-relative, with a raycast ground check.
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

    [Tooltip("Holding Shift moves at this multiple of the walking speed.")]
    public float runMultiplier = 2f;

    [Tooltip("Ground speed at which the run clip plays at its normal rate. 0 = estimate from the character's height.")]
    public float runStrideSpeed = 0f;

    [Tooltip("Fastest the run clip may play. Running speed is capped to match, so feet never slide.")]
    public float maxRunRate = 1.56f;

    [Tooltip("Jump height in multiples of the character's height, used only when there is no jump animation.")]
    public float jumpHeight = 0.35f;

    enum JumpPhase { None, Animated, Air }

    CharacterController controller;
    Animator animator;   // optional: "Speed" switches Idle/Walk/Run, "WalkRate"/"RunRate" set their playback rates
    bool hasWalkRate, hasRunRate, hasRunState, hasJumpState;
    Transform cam;
    float verticalVelocity;
    float naturalWalkSpeed, naturalRunSpeed, worldHeight;
    bool grounded, running;
    JumpPhase jump;
    float jumpTimer;
    bool jumpLanded;

    static readonly int JumpState = Animator.StringToHash("Jump");
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
        if (animator == null) return;
        foreach (var p in animator.parameters)
        {
            if (p.name == "WalkRate") hasWalkRate = true;
            if (p.name == "RunRate") hasRunRate = true;
        }
        hasRunState = animator.HasState(0, RunState);
        hasJumpState = animator.HasState(0, JumpState);
    }

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
            h = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
            v = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
        }

        // Camera-relative directions (flattened onto the ground plane)
        Vector3 fwd = cam != null ? cam.forward : Vector3.forward;
        Vector3 right = cam != null ? cam.right : Vector3.right;
        fwd.y = 0f; right.y = 0f;
        fwd.Normalize(); right.Normalize();

        Vector3 dir = fwd * v + right * h;
        if (dir.sqrMagnitude > 1f) dir.Normalize();

        // Shift runs; with an animator but no Run state it would slide the walk, so it walks instead
        running = kb != null && kb.shiftKey.isPressed && dir.sqrMagnitude > 0.01f && (animator == null || hasRunState);

        float moveSpeed = running ? speed * runMultiplier : speed;
        if (animator != null)
        {
            // Cap ground speed to what the walk/run cycle can match, then play it at that rate so the feet don't slide
            if (running)
            {
                moveSpeed = Mathf.Min(moveSpeed, naturalRunSpeed * maxRunRate);
                if (hasRunRate) animator.SetFloat("RunRate", moveSpeed / naturalRunSpeed);
            }
            else
            {
                moveSpeed = Mathf.Min(moveSpeed, naturalWalkSpeed * maxWalkRate);
                if (hasWalkRate) animator.SetFloat("WalkRate", moveSpeed / naturalWalkSpeed);
            }
            animator.SetFloat("Speed", dir.magnitude * (running ? 2f : 1f));
        }

        UpdateJump(kb != null && kb.spaceKey.wasPressedThisFrame, dir);

        // Vertical: raycast ground check (works regardless of model scale)
        float dy = GroundDelta();
        if (jump == JumpPhase.Air && grounded) EndJump(dir);

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

    // Space plays the jump clip. The clip itself carries the jump's height, so the body rises and
    // lands exactly as animated; without a Jump state, a plain physics jump is used instead.
    void UpdateJump(bool pressed, Vector3 dir)
    {
        if (pressed && grounded && jump == JumpPhase.None)
        {
            if (hasJumpState)
            {
                jump = JumpPhase.Animated;
                jumpLanded = false;
                jumpTimer = 0f;
                animator.CrossFadeInFixedTime(JumpState, 0.1f);
            }
            else
            {
                verticalVelocity = Mathf.Sqrt(2f * -gravity * jumpHeight * worldHeight);
                jump = JumpPhase.Air;
            }
        }

        if (jump == JumpPhase.Animated)
        {
            jumpTimer += Time.deltaTime;
            var state = animator.GetCurrentAnimatorStateInfo(0);
            bool inJump = state.shortNameHash == JumpState && !animator.IsInTransition(0);
            bool ending = inJump && state.normalizedTime >= 1f - 0.15f / Mathf.Max(state.length, 0.01f);
            // Walking cuts the landing recovery short; standing still lets it play out
            if ((jumpLanded && dir.sqrMagnitude > 0.01f) || ending || jumpTimer > 5f) EndJump(dir);
        }
    }

    // Animation event on the jump clip at the frame its feet touch down
    // (added by Tools > Elevator Game > Rebuild Character Animator).
    public void OnJumpLand() => jumpLanded = true;

    void EndJump(Vector3 dir)
    {
        jump = JumpPhase.None;
        if (hasJumpState)
            animator.CrossFadeInFixedTime(dir.sqrMagnitude <= 0.01f ? "Idle" : running ? "Run" : "Walk", 0.15f);
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
            grounded = true;
            verticalVelocity = 0f;
            return groundY - y;                      // snap onto the ground
        }

        grounded = false;
        verticalVelocity += gravity * Time.deltaTime; // falling (or walking off an edge)
        float dy = verticalVelocity * Time.deltaTime;
        if (found) dy = Mathf.Max(dy, groundY - y);   // never fall below the ground
        return dy;
    }
}
