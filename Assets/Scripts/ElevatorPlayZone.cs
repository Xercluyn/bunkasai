using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Trigger area in front of the elevator button (or any machine, like the fifth floor's arcade box).
/// While the player stands inside it, a Play button appears on screen; clicking it loads the minigame scene.
/// Size and move the BoxCollider so it covers the floor right in front of the button.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
[RequireComponent(typeof(Rigidbody))]
public class ElevatorPlayZone : MonoBehaviour
{
    [Tooltip("Scene loaded when Play is clicked (must be in Build Settings).")]
    public string minigameScene = "ElevatorButtonGame";

    [Tooltip("Text on the generated button, e.g. ２階へ for the elevator.")]
    public string buttonLabel = "▶ プレイ";

    [Tooltip("The elevator button itself. The Play button floats above it on screen. Defaults to this zone.")]
    public Transform buttonTarget;

    [Tooltip("The object this zone is for (elevator, arcade box). Its InteractableOutline lights up while Play shows.")]
    public GameObject interactable;

    [Tooltip("Only show Play when the player is also facing the button.")]
    public bool requireFacing = false;

    [Tooltip("Optional: your own UI Button. If empty, one is created at runtime.")]
    public Button playButton;

    [Tooltip("Screen offset of the generated Play button from the button's position (canvas units).")]
    public Vector2 screenOffset = new Vector2(0f, 90f);

    PlayerMovement player;
    InteractableOutline outline;
    RectTransform buttonRect, canvasRect;
    Camera cam;
    bool generated;

    void Reset() => ConfigurePhysics();

    void Awake()
    {
        ConfigurePhysics();
        cam = Camera.main;
        if (interactable != null) outline = interactable.GetComponent<InteractableOutline>();
        UiKit.EnsureEventSystem();

        if (playButton == null)
        {
            var canvas = UiKit.MakeCanvas("PlayPromptCanvas", 10, new Vector2(1280f, 720f));
            canvasRect = (RectTransform)canvas.transform;
            playButton = UiKit.MainButton(canvas.transform, buttonLabel, out _);
            buttonRect = (RectTransform)playButton.transform;
            buttonRect.anchorMin = buttonRect.anchorMax = Vector2.zero;   // positioned in canvas units from bottom-left
            buttonRect.pivot = new Vector2(0.5f, 0f);
            buttonRect.sizeDelta = new Vector2(200f, 54f);
            generated = true;
        }

        playButton.onClick.AddListener(Play);
        playButton.gameObject.SetActive(false);
    }

    void ConfigurePhysics()
    {
        GetComponent<BoxCollider>().isTrigger = true;
        var rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;   // lets the CharacterController fire trigger events
        rb.useGravity = false;
    }

    void OnTriggerEnter(Collider other)
    {
        var p = other.GetComponentInParent<PlayerMovement>();
        if (p != null) player = p;
    }

    void OnTriggerExit(Collider other)
    {
        var p = other.GetComponentInParent<PlayerMovement>();
        if (p != null && p == player) player = null;
    }

    void OnDisable()
    {
        player = null;
        ThirdPersonCamera.uiNeedsCursor = false;
        if (outline != null) outline.focused = false;
        if (playButton != null) playButton.gameObject.SetActive(false);
    }

    void Update()
    {
        bool show = player != null && (!requireFacing || IsFacing());
        if (playButton.gameObject.activeSelf != show)
        {
            playButton.gameObject.SetActive(show);
            ThirdPersonCamera.uiNeedsCursor = show;   // free the mouse so the button can be clicked
            if (outline != null) outline.focused = show;
        }
        if (show && generated) FollowTarget();
    }

    bool IsFacing()
    {
        Vector3 to = TargetPosition() - player.transform.position;
        Vector3 fwd = player.transform.forward;
        to.y = 0f; fwd.y = 0f;
        return to.sqrMagnitude < 0.0001f || Vector3.Dot(fwd.normalized, to.normalized) > 0.5f;
    }

    Vector3 TargetPosition() => buttonTarget != null ? buttonTarget.position : GetComponent<BoxCollider>().bounds.center;

    // Keep the generated button just above the elevator button on screen, clamped inside the view.
    void FollowTarget()
    {
        if (cam == null) cam = Camera.main;
        Vector2 size = canvasRect.rect.size;
        Vector2 pos = new Vector2(size.x * 0.5f, size.y * 0.2f);   // fallback: lower middle

        if (cam != null)
        {
            Vector3 sp = cam.WorldToScreenPoint(TargetPosition());
            if (sp.z > 0f)
            {
                float scale = canvasRect.localScale.x;
                pos = new Vector2(sp.x, sp.y) / scale + screenOffset;
            }
        }

        Vector2 half = buttonRect.sizeDelta * 0.5f;
        pos.x = Mathf.Clamp(pos.x, half.x + 16f, size.x - half.x - 16f);
        pos.y = Mathf.Clamp(pos.y, 16f, size.y - buttonRect.sizeDelta.y - 16f);
        buttonRect.anchoredPosition = pos;
    }

    void Play()
    {
        if (!Application.CanStreamedLevelBeLoaded(minigameScene))
        {
            Debug.LogError($"ElevatorPlayZone: scene '{minigameScene}' is not in Build Settings. " +
                           "Run Tools > Elevator Game > Create Minigame Scene.");
            return;
        }
        if (player != null) GameProgress.RememberSpot(player.transform);   // come back here afterwards
        SceneManager.LoadScene(minigameScene);
    }

    void OnDrawGizmos()
    {
        var box = GetComponent<BoxCollider>();
        if (box == null) return;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.24f, 0.94f, 0.54f, 0.25f);
        Gizmos.DrawCube(box.center, box.size);
        Gizmos.color = new Color(0.24f, 0.94f, 0.54f, 1f);
        Gizmos.DrawWireCube(box.center, box.size);
    }
}
