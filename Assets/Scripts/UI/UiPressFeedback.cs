using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Button feel from style.css: the face sinks and its hard shadow shrinks while pressed,
/// the border can change colour on hover, and a disabled button fades to 40%.
/// A click sounds on press and a faint tick when the pointer moves onto an enabled button.
/// </summary>
[RequireComponent(typeof(UiPointerState))]
[RequireComponent(typeof(CanvasGroup))]
public class UiPressFeedback : MonoBehaviour, IPointerDownHandler, IPointerEnterHandler, ISelectHandler, ISubmitHandler
{
    const float SelectedScale = 1.06f;   // a gamepad-selected button stands out a little
    public RectTransform face;
    public Button button;
    public Shadow shadow;
    public float shadowDepth = 6f;
    public float depth = 4f;

    public Graphic border;
    public Color borderNormal = UiKit.Line;
    public Color borderHover = UiKit.Accent;

    [Range(0f, 1f)] public float clickVolume = 0.8f;
    [Range(0f, 1f)] public float hoverVolume = 0.25f;

    UiPointerState pointer;
    CanvasGroup group;

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

        if (face != null) face.anchoredPosition = new Vector2(0f, down ? -depth : 0f);
        if (shadow != null) shadow.effectDistance = new Vector2(0f, down ? -(shadowDepth - depth) : -shadowDepth);
        if (border != null) border.color = on && pointer.Highlighted ? borderHover : borderNormal;
        if (face != null) face.localScale = Vector3.one * (on && pointer.selected ? SelectedScale : 1f);
        group.alpha = on ? 1f : 0.4f;
    }
}
