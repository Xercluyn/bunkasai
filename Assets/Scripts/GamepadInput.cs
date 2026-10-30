using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock;
using UnityEngine.UI;

/// <summary>
/// The floors' gamepad controls, for Xbox, PlayStation and other pads the Input System knows:
///  - left stick moves;
///  - right stick looks around;
///  - A / × interacts (like E);
///  - Menu / OPTIONS opens the pause menu, and B / ○ closes it.
/// UsingPad tells whether the pad or the keyboard and mouse was used last, so on-screen hints can show the right
/// buttons; ButtonName gives the label of a face button on the connected pad.
/// </summary>
public static class GamepadInput
{
    const float StickDeadzone = 0.3f;   // past this a tilt counts (the game always runs, so no slow creep)

    static bool usingPad;
    static int polledFrame = -1;

    // no domain reload on entering Play Mode: start each session on keyboard and mouse
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void NewPlay()
    {
        usingPad = false;
        polledFrame = -1;
        FocusPaused = false;
        heldStep = step = Vector2Int.zero;
        stepFrame = -1;
    }

    static Gamepad Pad => Gamepad.current;

    /// <summary>Left stick, zero inside the dead zone.</summary>
    public static Vector2 Move => Stick(Pad?.leftStick);

    /// <summary>Right stick, zero inside the dead zone.</summary>
    public static Vector2 Look => Stick(Pad?.rightStick);

    public static bool InteractPressed => Pad != null && Pad.buttonSouth.wasPressedThisFrame;
    public static bool MenuPressed => Pad != null && Pad.startButton.wasPressedThisFrame;
    public static bool BackPressed => Pad != null && Pad.buttonEast.wasPressedThisFrame;
    public static bool WestPressed => Pad != null && Pad.buttonWest.wasPressedThisFrame;
    public static bool NorthPressed => Pad != null && Pad.buttonNorth.wasPressedThisFrame;
    public static bool SelectPressed => Pad != null && Pad.selectButton.wasPressedThisFrame;
    public static bool DpadUpPressed => Pad != null && Pad.dpad.up.wasPressedThisFrame;
    public static bool DpadDownPressed => Pad != null && Pad.dpad.down.wasPressedThisFrame;

    /// <summary>A PlayStation pad (DualShock 4 or DualSense) is the current one.</summary>
    public static bool IsPlayStation => Pad is DualShockGamepad;

    // the face buttons' labels on the connected pad: Xbox letters, or the PlayStation symbols
    public static string South => IsPlayStation ? "×" : "A";
    public static string East => IsPlayStation ? "○" : "B";
    public static string West => IsPlayStation ? "□" : "X";
    public static string North => IsPlayStation ? "△" : "Y";
    public static string SelectName => IsPlayStation ? "SHARE" : "View";

    /// <summary>The interact button's label: A, or × on a PlayStation pad.</summary>
    public static string InteractName => South;

    /// <summary>The menu button's label: ≡ (Xbox Menu), or OPTIONS on a PlayStation pad.</summary>
    public static string MenuName => IsPlayStation ? "OPTIONS" : "≡";

    /// <summary>`keys` or, while the pad is in use, `pad` (each Japanese / English).</summary>
    public static string T(string keysJa, string keysEn, string padJa, string padEn) =>
        UsingPad ? L.T(padJa, padEn) : L.T(keysJa, keysEn);

    /// <summary>
    /// Keeps `text` showing make(): written now and again whenever the player switches between the pad and keyboard +
    /// mouse or changes the language, so button hints always name the right buttons.
    /// </summary>
    public static void Bind(Text text, Func<string> make)
    {
        if (text == null) return;
        var live = text.GetComponent<PadText>() ?? text.gameObject.AddComponent<PadText>();
        live.make = make;
        live.Refresh();
    }

    /// <summary>
    /// Marks `button` as what the gamepad selects on its screen. While the pad is in use the highest-priority default
    /// that is showing and enabled gets selected whenever nothing usable is, or when a screen of higher priority
    /// opens over the current one (board 0 &lt; overlays 10 &lt; how-to cards 20 &lt; the pause menu 30).
    /// </summary>
    public static void SetDefault(Selectable button, int priority)
    {
        if (button == null) return;
        var d = button.GetComponent<PadDefault>() ?? button.gameObject.AddComponent<PadDefault>();
        d.priority = priority;
    }

    public const int BoardPriority = 0, OverlayPriority = 10, CardPriority = 20, MenuPriority = 30;

    /// <summary>While true the pad's default buttons aren't selected automatically (a screen runs its own cursor).</summary>
    public static bool FocusPaused;

    static Vector2Int heldStep;
    static int stepFrame = -1;
    static Vector2Int step;

    /// <summary>
    /// One step per push of the D-pad or the left stick this frame (x: −1 left / +1 right, y: −1 down / +1 up), for
    /// screens that move their own cursor; holding doesn't repeat.
    /// </summary>
    public static Vector2Int NavStep
    {
        get
        {
            if (stepFrame == Time.frameCount) return step;
            stepFrame = Time.frameCount;
            var pad = Pad;
            if (pad == null) return step = heldStep = Vector2Int.zero;
            var v = pad.leftStick.ReadValue();
            var held = new Vector2Int(v.x > 0.5f ? 1 : v.x < -0.5f ? -1 : 0, v.y > 0.5f ? 1 : v.y < -0.5f ? -1 : 0);
            step = new Vector2Int(held.x != heldStep.x ? held.x : 0, held.y != heldStep.y ? held.y : 0);
            heldStep = held;
            if (pad.dpad.left.wasPressedThisFrame) step.x = -1;
            if (pad.dpad.right.wasPressedThisFrame) step.x = 1;
            if (pad.dpad.up.wasPressedThisFrame) step.y = 1;
            if (pad.dpad.down.wasPressedThisFrame) step.y = -1;
            return step;
        }
    }

    /// <summary>True when the pad was used more recently than the keyboard and mouse.</summary>
    public static bool UsingPad
    {
        get
        {
            Poll();
            return usingPad;
        }
    }

    static Vector2 Stick(UnityEngine.InputSystem.Controls.StickControl stick)
    {
        if (stick == null) return Vector2.zero;
        var v = stick.ReadValue();
        return v.sqrMagnitude < StickDeadzone * StickDeadzone ? Vector2.zero : Vector2.ClampMagnitude(v, 1f);
    }

    static void Poll()
    {
        if (polledFrame == Time.frameCount) return;
        polledFrame = Time.frameCount;
        var pad = Pad;
        if (pad != null && (Move != Vector2.zero || Look != Vector2.zero || pad.buttonSouth.wasPressedThisFrame ||
                            pad.buttonEast.wasPressedThisFrame || pad.buttonWest.wasPressedThisFrame ||
                            pad.buttonNorth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame ||
                            pad.dpad.ReadValue() != Vector2.zero))
        {
            usingPad = true;
            return;
        }
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if ((kb != null && kb.anyKey.wasPressedThisFrame) ||
            (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.delta.ReadValue().sqrMagnitude > 25f)))
            usingPad = false;
    }

    // ---------- keeping a UI selection for the pad ----------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartFocus()
    {
        var go = new GameObject("GamepadFocus");
        go.hideFlags = HideFlags.HideInHierarchy;
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<PadFocus>();
    }

    sealed class PadFocus : MonoBehaviour
    {
        int lastPriority = int.MinValue;
        bool padSelected;   // the selection is ours, to drop when the mouse takes over
        bool wasPad;

        void Update()
        {
            var es = EventSystem.current;
            if (es == null) return;
            var current = es.currentSelectedGameObject;
            bool pad = UsingPad;
            if (!pad)
            {
                if (padSelected && current != null) es.SetSelectedGameObject(null);
                padSelected = false;
                lastPriority = int.MinValue;
                wasPad = false;
                return;
            }
            // just switched to the pad: a button the mouse clicked (and so left selected) mustn't take A / ×
            if (!wasPad && !padSelected && current != null)
            {
                es.SetSelectedGameObject(null);
                current = null;
            }
            wasPad = true;
            if (FocusPaused) return;

            PadDefault best = null;
            foreach (var d in PadDefault.all)
                if (d.Usable && (best == null || d.priority > best.priority)) best = d;
            bool valid = current != null && current.activeInHierarchy &&
                         current.TryGetComponent<Selectable>(out var s) && s.IsInteractable();
            if (best == null) return;
            // a card, overlay or menu on top keeps the pad: the stick can't wander onto what's behind it
            if (valid && best.priority > BoardPriority && !current.transform.IsChildOf(ScreenRoot(best.transform)))
                valid = false;
            if (valid && best.priority <= lastPriority) return;
            es.SetSelectedGameObject(best.gameObject);
            lastPriority = best.priority;
            padSelected = true;
        }

        // the screen `t` is on: its ancestor right under the root canvas (a how-to card, an overlay, a menu)
        static Transform ScreenRoot(Transform t)
        {
            var canvas = t.GetComponentInParent<Canvas>();
            var root = canvas != null ? canvas.rootCanvas.transform : null;
            while (t.parent != null && t.parent != root) t = t.parent;
            return t;
        }
    }

    /// <summary>The stick and D-pad never select `s` (the pad reaches it through its own button, or not at all).</summary>
    public static void NoPad(Selectable s)
    {
        if (s != null) s.navigation = new Navigation { mode = Navigation.Mode.None };
    }

    /// <summary>
    /// The stick and D-pad move only between `items`, laid out `cols` to a row (left to right, top to bottom),
    /// wrapping at the edges, so they never land on another button of the screen.
    /// </summary>
    public static void Grid(IList<Selectable> items, int cols)
    {
        int n = items.Count;
        if (n == 0 || cols < 1) return;
        int rows = (n + cols - 1) / cols;
        for (int i = 0; i < n; i++)
        {
            int r = i / cols, c = i % cols;
            int rowLen = Math.Min(cols, n - r * cols);
            Selectable At(int row, int col) => items[Math.Min(row * cols + col, Math.Min(n, (row + 1) * cols) - 1)];
            items[i].navigation = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnLeft = rowLen > 1 ? items[r * cols + (c + rowLen - 1) % rowLen] : null,
                selectOnRight = rowLen > 1 ? items[r * cols + (c + 1) % rowLen] : null,
                selectOnUp = rows > 1 ? At((r + rows - 1) % rows, c) : null,
                selectOnDown = rows > 1 ? At((r + 1) % rows, c) : null,
            };
        }
    }
}

/// <summary>A screen's default button for the gamepad (see GamepadInput.SetDefault).</summary>
public sealed class PadDefault : MonoBehaviour
{
    public int priority;
    internal static readonly List<PadDefault> all = new List<PadDefault>();
    Selectable selectable;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void NewPlay() => all.Clear();

    void Awake() => selectable = GetComponent<Selectable>();
    void OnEnable() => all.Add(this);
    void OnDisable() => all.Remove(this);

    internal bool Usable => selectable != null && isActiveAndEnabled && selectable.IsInteractable();
}

/// <summary>A label that rewrites itself when the input device or the language changes (GamepadInput.Bind).</summary>
public sealed class PadText : MonoBehaviour
{
    public Func<string> make;
    Text text;
    bool pad;

    void Awake() => text = GetComponent<Text>();

    void OnEnable()
    {
        L.Changed += Refresh;
        Refresh();
    }

    void OnDisable() => L.Changed -= Refresh;

    void Update()
    {
        if (pad != GamepadInput.UsingPad) Refresh();
    }

    public void Refresh()
    {
        if (text == null) text = GetComponent<Text>();
        pad = GamepadInput.UsingPad;
        if (text != null && make != null) text.text = make();
    }
}
