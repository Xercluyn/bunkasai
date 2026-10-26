using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Button feel from style.css: the face sinks and its hard shadow shrinks while pressed,
/// the border can change colour on hover, and a disabled button fades to 40%.
/// </summary>
[RequireComponent(typeof(UiPointerState))]
[RequireComponent(typeof(CanvasGroup))]
public class UiPressFeedback : MonoBehaviour
{
    public RectTransform face;
    public Button button;
    public Shadow shadow;
    public float shadowDepth = 6f;
    public float depth = 4f;

    public Graphic border;
    public Color borderNormal = Color.white;
    public Color borderHover = Color.white;

    UiPointerState pointer;
    CanvasGroup group;

    void Awake()
    {
        pointer = GetComponent<UiPointerState>();
        group = GetComponent<CanvasGroup>();
    }

    void LateUpdate()
    {
        bool on = button == null || button.interactable;
        bool down = on && pointer.pressed;

        if (face != null) face.anchoredPosition = new Vector2(0f, down ? -depth : 0f);
        if (shadow != null) shadow.effectDistance = new Vector2(0f, down ? -(shadowDepth - depth) : -shadowDepth);
        if (border != null) border.color = on && pointer.hovered ? borderHover : borderNormal;
        group.alpha = on ? 1f : 0.4f;
    }
}
