using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Trigger area in front of the elevator button (or any machine, like the fifth floor's arcade box).
/// While the player stands inside it, a prompt like 「E　▶ プレイ」 appears at the bottom centre of the screen
/// (with a blip) and the object's cyan outline lights up; pressing E, Enter or the left mouse button (or clicking
/// the prompt) leaves for the minigame scene a moment later, with a click and a whoosh. Nothing happens while the
/// player is locked (intro, pause menu, ending). An object that can't be used yet (the elevator before the quest
/// is cleared) gets a dim grey outline instead.
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
    Text labelText, keysText;
    bool leaving;

    /// <summary>True from a zone starting its minigame until the scene changes; the pause menu stays shut.</summary>
    public static bool Leaving { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void NewPlay() => Leaving = false;

    /// <summary>False hides the prompt even with the player in the zone (e.g. the ticket is already bought).</summary>
    protected virtual bool Available => true;

    /// <summary>False greys the object's outline: it can be walked up to but not used yet (the locked elevator).</summary>
    protected virtual bool Unlocked => true;

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
            var badge = UiKit.Box("Key", face, UiKit.Ink, 8f);
            UiKit.Place(badge.rectTransform, 12f, 10f, 34f, 34f);
            var key = UiKit.Label(badge.transform, "E", 20, UiKit.MainFill);
            UiKit.Stretch(key.rectTransform);
            labelText.rectTransform.offsetMin = new Vector2(56f, 0f);
            labelText.rectTransform.offsetMax = new Vector2(-20f, 0f);

            // 「E / クリック」 under the button
            keysText = UiKit.Label(buttonRect, "", 15, UiKit.Fg);
            var krt = keysText.rectTransform;
            krt.anchorMin = krt.anchorMax = new Vector2(0.5f, 0f);
            krt.pivot = new Vector2(0.5f, 1f);
            krt.anchoredPosition = new Vector2(0f, -8f);
            krt.sizeDelta = new Vector2(300f, 20f);
            UiKit.HardShadow(keysText, UiKit.WithAlpha(UiKit.PanelShadow, 0.8f), 2f);
            Relabel();
        }

        playButton.onClick.AddListener(TryPlay);
        playButton.gameObject.SetActive(false);
        L.Changed += Relabel;
    }

    void OnEnable() => zones.Add(this);

    void OnDestroy()
    {
        L.Changed -= Relabel;
        PlayerLock.Set(this, false);
        if (leaving) leaving = Leaving = false;
    }

    // the language was switched
    void Relabel()
    {
        if (labelText == null) return;
        labelText.text = L.Tr(buttonLabel);
        keysText.text = L.T("E / クリック", "E / Click");
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
        PlayerLock.Set(this, false);
        if (leaving) leaving = Leaving = false;
        if (outline != null) outline.focused = false;
        if (playButton != null) playButton.gameObject.SetActive(false);
    }

    void Update()
    {
        if (outline != null) outline.locked = !Unlocked;
        bool show = Wants && Nearest();
        if (playButton.gameObject.activeSelf != show)
        {
            playButton.gameObject.SetActive(show);
            if (outline != null) outline.focused = show;
            if (show) Sfx.PlayUi(Sfx.Blip(), 0.6f);
        }
        if (!show) return;

        var kb = Keyboard.current;
        var mouse = Mouse.current;
        bool key = kb != null && (kb.eKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame);
        // a click while looking around (cursor locked, nothing on screen under it) works like E
        bool click = mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState == CursorLockMode.Locked &&
                     !(EventSystem.current != null && EventSystem.current.IsPointerOverGameObject());
        if (key || click) TryPlay();
    }

    // E, a click or the prompt: not while the player is locked or already on the way
    void TryPlay()
    {
        if (leaving || PlayerLock.Locked) return;
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
        StartCoroutine(Leave(minigameScene));
    }

    // click and whoosh, hold the player still, then load (a moment later, so the sounds and press register)
    IEnumerator Leave(string scene)
    {
        leaving = Leaving = true;
        PlayerLock.Set(this, true);
        Sfx.PlayUi(Sfx.Click());
        Sfx.PlayUi(Sfx.Whoosh(), 0.7f);
        yield return new WaitForSecondsRealtime(0.25f);
        SceneManager.LoadScene(scene);
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
