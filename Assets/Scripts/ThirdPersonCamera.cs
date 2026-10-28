using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Third-person camera (PUBG-style): follows the player from behind, over the right shoulder.
/// The mouse orbits it around the character and it pulls in when a wall is in the way. It never turns
/// by itself: WASD moves relative to the camera, so a camera that followed the character's facing
/// would keep turning A/D strafes into circles.
/// Put it on the Main Camera. Esc frees the cursor; click the game view to look around again.
/// </summary>
public class ThirdPersonCamera : MonoBehaviour
{
    [Tooltip("Character to follow. Defaults to the object with PlayerMovement.")]
    public Transform target;

    [Header("Framing (multiples of the character's height)")]
    public float distance = 1.6f;
    public float pivotHeight = 0.9f;
    public float shoulderOffset = 0.25f;

    [Header("Look")]
    [Tooltip("Degrees per pixel of mouse movement.")]
    public float mouseSensitivity = 0.12f;
    public float minPitch = -35f;
    public float maxPitch = 60f;
    public float startPitch = 12f;

    [Tooltip("Layers the camera can't pass through.")]
    public LayerMask obstacleMask = ~0;

    /// <summary>Set by UI that needs a free mouse cursor (e.g. the elevator Play button).</summary>
    public static bool uiNeedsCursor;

    /// <summary>Set while a panel that must be clicked is open (the ending). Kept apart from uiNeedsCursor,
    /// which play zones switch off when their button hides.</summary>
    public static bool panelOpen;

    // no domain reload on entering Play Mode: start each session with the cursor rules reset
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void NewSession() => uiNeedsCursor = panelOpen = false;

    float yaw, pitch, height = 1.8f, currentDistance;
    bool userFreedCursor;
    Collider[] targetColliders;
    Camera cam;

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
            Vector2 d = mouse.delta.ReadValue() * mouseSensitivity;
            yaw += d.x;
            pitch = Mathf.Clamp(pitch - d.y, minPitch, maxPitch);
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
            if (IsTarget(hit.collider) || hit.distance <= 0f) continue;   // ignore self and overlaps at the start
            best = Mathf.Min(best, hit.distance);
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

    void UpdateCursor()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame) userFreedCursor = true;
        if (userFreedCursor && !uiNeedsCursor && !panelOpen && mouse != null && mouse.leftButton.wasPressedThisFrame && !PointerOverUi())
            userFreedCursor = false;

        bool free = userFreedCursor || uiNeedsCursor || panelOpen;
        var mode = free ? CursorLockMode.None : CursorLockMode.Locked;
        if (Cursor.lockState != mode) Cursor.lockState = mode;
        Cursor.visible = free;
    }

    static bool PointerOverUi() =>
        EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
}
