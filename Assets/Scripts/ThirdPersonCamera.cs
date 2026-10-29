using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Third-person camera: follows the player from behind and above, looking down so the floor around them shows.
/// The mouse orbits it around the character and it pulls in when a wall is in the way. It never turns
/// by itself: WASD moves relative to the camera, so a camera that followed the character's facing
/// would keep turning A/D strafes into circles.
/// Put it on the Main Camera. The cursor is locked for looking around, and free while the pause menu
/// (FloorPauseMenu, Esc) or a panel that must be clicked (the ending) is open.
/// </summary>
public class ThirdPersonCamera : MonoBehaviour
{
    [Tooltip("Character to follow. Defaults to the object with PlayerMovement.")]
    public Transform target;

    // Far back and from above, so the floor around the small (scale 0.8) onigiri shows; the character stays
    // near the middle of the screen, clear of the corner panels and the prompt at the bottom
    const float WideDistance = 3.2f, WidePivotHeight = 1.1f, WideShoulderOffset = 0.1f;
    const float WideMinPitch = -20f, WideMaxPitch = 70f, WideStartPitch = 28f, WideFieldOfView = 65f;

    [Header("Framing (multiples of the character's height)")]
    public float distance = WideDistance;
    public float pivotHeight = WidePivotHeight;
    public float shoulderOffset = WideShoulderOffset;

    [Header("Look")]
    public float minPitch = WideMinPitch;
    public float maxPitch = WideMaxPitch;
    public float startPitch = WideStartPitch;
    [Tooltip("Vertical field of view set on the camera at Start. 0 keeps the Camera's own.")]
    public float fieldOfView = WideFieldOfView;

    [Tooltip("Layers the camera can't pass through.")]
    public LayerMask obstacleMask = ~0;

    // 0 in scenes saved with the old close, over-the-shoulder framing (1.6 / 0.9 / 0.25, pitch 12); UpgradeFraming
    // moves those to the wide view once, so the floors need no setup tool run
    const int FramingVersion = 1;
    [SerializeField, HideInInspector] int framingVersion;

    /// <summary>Set while a panel that must be clicked is open (the ending): frees the cursor.</summary>
    public static bool panelOpen;

    // Right-stick look at full tilt, in degrees per second, at the default 視点感度
    const float PadYawSpeed = 180f, PadPitchSpeed = 110f, DefaultLookSpeed = 0.12f;

    // no domain reload on entering Play Mode: start each session with the cursor rules reset
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void NewSession() => panelOpen = false;

    float yaw, pitch, height = 1.8f, currentDistance;
    Collider[] targetColliders;
    Camera cam;

    void Reset() => framingVersion = FramingVersion;   // a new component already has the wide defaults
    void OnValidate() => UpgradeFraming();
    void Awake() => UpgradeFraming();

    void UpgradeFraming()
    {
        if (framingVersion >= FramingVersion) return;
        framingVersion = FramingVersion;
        distance = WideDistance;
        pivotHeight = WidePivotHeight;
        shoulderOffset = WideShoulderOffset;
        minPitch = WideMinPitch;
        maxPitch = WideMaxPitch;
        startPitch = WideStartPitch;
        fieldOfView = WideFieldOfView;
    }

    void Start()
    {
        if (target == null)
        {
            var player = FindAnyObjectByType<PlayerMovement>();
            if (player != null) target = player.transform;
        }
        if (target == null) { enabled = false; return; }

        // PlayerMovement has already fitted the capsule in Awake, so it gives the character's size
        var cc = target.GetComponent<CharacterController>();
        if (cc != null) height = cc.height * target.lossyScale.y;
        targetColliders = target.GetComponentsInChildren<Collider>();
        cam = GetComponent<Camera>();
        if (cam != null && fieldOfView > 0f) cam.fieldOfView = fieldOfView;

        yaw = target.eulerAngles.y;
        pitch = startPitch;
        currentDistance = distance * height;
    }

    void OnDisable()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void LateUpdate()
    {
        UpdateCursor();

        var mouse = Mouse.current;
        if (Cursor.lockState == CursorLockMode.Locked && mouse != null)
        {
            Vector2 d = mouse.delta.ReadValue() * FloorPauseMenu.LookSpeed;   // the pause menu's 視点感度
            yaw += d.x;
            pitch = Mathf.Clamp(pitch - d.y, minPitch, maxPitch);
        }
        // the gamepad's right stick: degrees per second, scaled by the same 視点感度 (paused: deltaTime is 0)
        var stick = GamepadInput.Look;
        if (stick != Vector2.zero && !panelOpen && !PlayerLock.Locked)
        {
            float rate = FloorPauseMenu.LookSpeed / DefaultLookSpeed * Time.deltaTime;
            yaw += stick.x * PadYawSpeed * rate;
            pitch = Mathf.Clamp(pitch - stick.y * PadPitchSpeed * rate, minPitch, maxPitch);
        }

        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 head = target.position + Vector3.up * (pivotHeight * height);
        Vector3 shoulder = Quaternion.Euler(0f, yaw, 0f) * Vector3.right * (shoulderOffset * height);
        Vector3 desired = head + shoulder - rot * Vector3.forward * (distance * height);

        // Pull in instantly when something blocks the view, ease back out when it clears
        Vector3 toCam = desired - head;
        float want = toCam.magnitude;
        float allowed = ClearDistance(head, toCam / Mathf.Max(want, 0.0001f), want);
        currentDistance = allowed < currentDistance
            ? allowed
            : Mathf.Lerp(currentDistance, allowed, 1f - Mathf.Exp(-6f * Time.deltaTime));

        transform.SetPositionAndRotation(head + toCam.normalized * currentDistance, rot);
    }

    float ClearDistance(Vector3 origin, Vector3 dir, float maxDist)
    {
        // Wide enough to keep the whole near clip plane in front of walls, or they get cut away and look see-through
        float radius = Mathf.Max(0.1f * height, NearPlaneReach());
        float best = maxDist;
        foreach (var hit in Physics.SphereCastAll(origin, radius, dir, maxDist, obstacleMask, QueryTriggerInteraction.Ignore))
        {
            if (IsTarget(hit.collider)) continue;
            if (hit.distance > 0f) { best = Mathf.Min(best, hit.distance); continue; }
            // Already touching at the pivot (hugging a wall): the sweep reports no distance, so a ray finds
            // whether that wall lies behind the character; one beside it, along the view, doesn't count
            if (hit.collider.Raycast(new Ray(origin, dir), out var ray, maxDist))
                best = Mathf.Min(best, Mathf.Max(0f, ray.distance - radius));
        }
        return best;
    }

    // Distance from the camera to the corners of its near clip plane
    float NearPlaneReach()
    {
        if (cam == null) return 0f;
        float near = cam.nearClipPlane;
        float halfH = near * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float halfW = halfH * cam.aspect;
        return Mathf.Sqrt(near * near + halfH * halfH + halfW * halfW);
    }

    bool IsTarget(Collider c)
    {
        foreach (var t in targetColliders) if (t == c) return true;
        return c.transform.IsChildOf(target);
    }

    // Locked unless a menu or panel needs the mouse. Asked for again every frame, so a click back into the game
    // view relocks it after the editor or the browser let it go.
    void UpdateCursor()
    {
        bool free = FloorPauseMenu.Open || panelOpen;
        var mode = free ? CursorLockMode.None : CursorLockMode.Locked;
        if (Cursor.lockState != mode) Cursor.lockState = mode;
        Cursor.visible = free;
    }
}
