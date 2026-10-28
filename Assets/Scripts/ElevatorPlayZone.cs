using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Trigger area in front of the elevator button (or any machine, like the fifth floor's arcade box).
/// While the player stands inside it, a prompt like 「E　▶ プレイ」 appears at the bottom centre of the screen
/// and the object's orange outline lights up; pressing E (or Enter, or clicking it) loads the minigame scene.
/// Size and move the BoxCollider so it covers the floor right in front of the button.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
[RequireComponent(typeof(Rigidbody))]
public class ElevatorPlayZone : MonoBehaviour
{
    [Tooltip("Scene loaded when Play is clicked (must be in Build Settings).")]
    [SceneName] public string minigameScene = "ElevatorButtonGame";

    [Tooltip("Text on the generated prompt, e.g. ２階へ for the elevator.")]
    public string buttonLabel = "▶ プレイ";

    [Tooltip("The elevator button / top of the machine: where the quest guide's marker points. Defaults to this zone.")]
    public Transform buttonTarget;

    [Tooltip("The object this zone is for (elevator, arcade box). Its InteractableOutline lights up while the prompt shows.")]
    public GameObject interactable;

    [Tooltip("Only show the prompt when the player is also facing the button.")]
    public bool requireFacing = false;

    [Tooltip("Optional: your own UI Button. If empty, a prompt is created at the bottom centre of the screen.")]
    public Button playButton;

    // Zones in the scene: where two overlap (the second floor's arcade and ticket machine), only the nearest prompts
    static readonly List<ElevatorPlayZone> zones = new List<ElevatorPlayZone>();

    protected PlayerMovement player;
    InteractableOutline outline;
    RectTransform buttonRect;
    Text labelText;

    /// <summary>False hides the prompt even with the player in the zone (e.g. the ticket is already bought).</summary>
    protected virtual bool Available => true;

    void Reset() => ConfigurePhysics();

    void Awake()
    {
        ConfigurePhysics();
        if (interactable != null) outline = interactable.GetComponent<InteractableOutline>();
        UiKit.EnsureEventSystem();

        if (playButton == null)
        {
            var canvas = UiKit.MakeCanvas("PlayPromptCanvas", 10, new Vector2(1280f, 720f));
            playButton = UiKit.MainButton(canvas.transform, "", out labelText);
            buttonRect = (RectTransform)playButton.transform;
            buttonRect.anchorMin = buttonRect.anchorMax = buttonRect.pivot = new Vector2(0.5f, 0f);
            buttonRect.anchoredPosition = new Vector2(0f, 64f);   // under the toasts

            // key badge on the left of the label
            var face = labelText.transform.parent;
            var badge = UiKit.Box("Key", face, UiKit.Hex("#3a1a00"), 8f);
            UiKit.Place(badge.rectTransform, 12f, 10f, 34f, 34f);
            var key = UiKit.Label(badge.transform, "E", 20, UiKit.Hex("#ffc340"));
            UiKit.Stretch(key.rectTransform);
            labelText.rectTransform.offsetMin = new Vector2(56f, 0f);
            labelText.rectTransform.offsetMax = new Vector2(-20f, 0f);
            Relabel();
        }

        playButton.onClick.AddListener(Play);
        playButton.gameObject.SetActive(false);
        L.Changed += Relabel;
    }

    void OnEnable() => zones.Add(this);

    void OnDestroy() => L.Changed -= Relabel;

    // the language was switched
    void Relabel()
    {
        if (labelText == null) return;
        labelText.text = L.Tr(buttonLabel);
        buttonRect.sizeDelta = new Vector2(Mathf.Max(200f, labelText.preferredWidth + 76f), 54f);
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
        zones.Remove(this);
        player = null;
        if (outline != null) outline.focused = false;
        if (playButton != null) playButton.gameObject.SetActive(false);
    }

    void Update()
    {
        bool show = Wants && Nearest();
        if (playButton.gameObject.activeSelf != show)
        {
            playButton.gameObject.SetActive(show);
            if (outline != null) outline.focused = show;
        }
        if (!show) return;

        var kb = Keyboard.current;
        if (kb != null && (kb.eKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
            Play();
    }

    bool Wants => player != null && Available && (!requireFacing || IsFacing());

    float Distance()
    {
        Vector3 d = TargetPosition() - player.transform.position;
        d.y = 0f;
        return d.sqrMagnitude;
    }

    // No other zone the player is in is closer (ties go to the first in the list)
    bool Nearest()
    {
        float mine = Distance();
        foreach (var z in zones)
        {
            if (z == this || !z.Wants) continue;
            float theirs = z.Distance();
            if (theirs < mine || (theirs == mine && zones.IndexOf(z) < zones.IndexOf(this))) return false;
        }
        return true;
    }

    bool IsFacing()
    {
        Vector3 to = TargetPosition() - player.transform.position;
        Vector3 fwd = player.transform.forward;
        to.y = 0f; fwd.y = 0f;
        return to.sqrMagnitude < 0.0001f || Vector3.Dot(fwd.normalized, to.normalized) > 0.5f;
    }

    /// <summary>The elevator button / top of the machine (QuestGuide points here).</summary>
    internal Vector3 TargetPosition() => buttonTarget != null ? buttonTarget.position : GetComponent<BoxCollider>().bounds.center;

    /// <summary>The player is standing in the zone.</summary>
    internal bool PlayerInside => player != null;

    protected virtual void Play()
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
