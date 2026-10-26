using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>Tracks whether the pointer is hovering or pressing this UI element (CSS :hover / :active).</summary>
public class UiPointerState : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    public bool hovered;
    public bool pressed;

    public void OnPointerEnter(PointerEventData e) => hovered = true;
    public void OnPointerExit(PointerEventData e) { hovered = false; pressed = false; }
    public void OnPointerDown(PointerEventData e) => pressed = true;
    public void OnPointerUp(PointerEventData e) => pressed = false;

    void OnDisable() { hovered = false; pressed = false; }
}
