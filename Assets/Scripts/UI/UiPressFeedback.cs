using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Button feel from style.css: the face sinks and its hard shadow shrinks while pressed,
/// the border can change colour on hover, and a disabled button fades to 40%.
/// Hovered (mouse) or selected (pad), the face grows a little and swapped fill / ink pairs trade colours.
/// A click sounds on press and a faint tick when the pointer moves onto an enabled button.
/// </summary>
[RequireComponent(typeof(UiPointerState))]
[RequireComponent(typeof(CanvasGroup))]
public class UiPressFeedback : MonoBehaviour, IPointerDownHandler, IPointerEnterHandler, ISelectHandler, ISubmitHandler
{
    const float BlendSpeed = 12f;
    public RectTransform face;
    public Button button;
    public Shadow shadow;
    public float shadowDepth = 6f;
    public float depth = 4f;

    public Graphic border;
    public Color borderNormal = UiKit.Line;
    public Color borderHover = UiKit.Accent;

    public float hoverScale = 1.07f;
    public bool hoverFx = true;   // off: no grow, no colour swap

    [Range(0f, 1f)] public float clickVolume = 0.8f;
    [Range(0f, 1f)] public float hoverVolume = 0.25f;

    UiPointerState pointer;
    CanvasGroup group;
    Vector2 home;
    bool homeSet;
    float blend, eased;

    // one graphic of a fill / ink pair: base is its resting colour, last what this component wrote
    class Tone { public Graphic g; public Color baseC, last; }
    readonly List<(Tone fill, Tone ink)> swaps = new List<(Tone, Tone)>();

    // sweeping across a row of buttons ticks once, not once per button
    static float lastHover = -1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void NewPlay() => lastHover = -1f;

    void Awake()
    {
        pointer = GetComponent<UiPointerState>();
        group = GetComponent<CanvasGroup>();
    }

    bool On => button == null || button.interactable;

    /// <summary>On hover the fill takes the ink's colour and the ink the fill's (a see-through fill gives Bg ink).
    /// A colour the game writes later becomes the new resting colour.</summary>
    public void AddSwap(Graphic fill, Graphic ink)
    {
        if (fill == null || ink == null) return;
        swaps.Add((new Tone { g = fill, baseC = fill.color, last = fill.color },
                   new Tone { g = ink, baseC = ink.color, last = ink.color }));
    }

    public void OnPointerDown(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left || !On) return;
        Sfx.PlayUi(Sfx.Click(), clickVolume);
    }

    public void OnPointerEnter(PointerEventData e) => Tick();

    // moving the gamepad selection onto it ticks like a hover
    public void OnSelect(BaseEventData e)
    {
        if (!(e is PointerEventData)) Tick();
    }

    // A / × on the selected button clicks like the mouse
    public void OnSubmit(BaseEventData e)
    {
        if (On) Sfx.PlayUi(Sfx.Click(), clickVolume);
    }

    void Tick()
    {
        if (button == null || !button.interactable || hoverVolume <= 0f) return;
        if (Time.unscaledTime - lastHover < 0.08f) return;
        lastHover = Time.unscaledTime;
        Sfx.PlayUi(Sfx.Blip(), hoverVolume, 1.4f);
    }

    void LateUpdate()
    {
        bool on = On;
        bool down = on && pointer.pressed;
        bool lit = on && hoverFx && pointer.Highlighted;

        if (!on || !hoverFx) blend = 0f;   // disabled or locked: snap back, never blend
        else blend = Mathf.MoveTowards(blend, lit ? 1f : 0f, BlendSpeed * Time.unscaledDeltaTime);
        eased = Mathf.SmoothStep(0f, 1f, blend);

        if (face != null)
        {
            if (!homeSet) { home = face.anchoredPosition; homeSet = true; }
            float s = Mathf.Lerp(1f, hoverScale, eased);
            // grow about the centre even when the pivot sits at a corner
            Vector2 grow = Vector2.Scale(face.pivot - new Vector2(0.5f, 0.5f), face.rect.size) * (s - 1f);
            face.anchoredPosition = home + grow + new Vector2(0f, down ? -depth : 0f);
            face.localScale = Vector3.one * s;
        }
        if (shadow != null) shadow.effectDistance = new Vector2(0f, down ? -(shadowDepth - depth) : -shadowDepth);
        if (border != null) border.color = on && pointer.Highlighted ? borderHover : borderNormal;
        foreach (var (fill, ink) in swaps) Blend(fill, ink);
        group.alpha = on ? 1f : 0.4f;
    }

    void Blend(Tone fill, Tone ink)
    {
        if (fill.g == null || ink.g == null) return;
        if (fill.g.color != fill.last) fill.baseC = fill.g.color;
        if (ink.g.color != ink.last) ink.baseC = ink.g.color;
        Color fillHover = Opaque(ink.baseC);
        Color inkHover = fill.baseC.a < 0.5f ? UiKit.Bg : Opaque(fill.baseC);
        fill.g.color = fill.last = Color.Lerp(fill.baseC, fillHover, eased);
        ink.g.color = ink.last = Color.Lerp(ink.baseC, inkHover, eased);
    }

    static Color Opaque(Color c) => new Color(c.r, c.g, c.b, 1f);
}
