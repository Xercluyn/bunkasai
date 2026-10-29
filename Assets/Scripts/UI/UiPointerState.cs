using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Tracks whether the pointer is hovering or pressing this UI element (CSS :hover / :active), and whether a
/// gamepad has it selected (it then looks hovered).
/// </summary>
public class UiPointerState : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler,
                              ISelectHandler, IDeselectHandler
{
    public bool hovered;
    public bool pressed;
    public bool selected;

    /// <summary>Hovered by the mouse or selected with the gamepad.</summary>
    public bool Highlighted => hovered || selected;

    public void OnPointerEnter(PointerEventData e) => hovered = true;
    public void OnPointerExit(PointerEventData e) { hovered = false; pressed = false; }
    public void OnPointerDown(PointerEventData e) => pressed = true;
    public void OnPointerUp(PointerEventData e) => pressed = false;
    // a mouse click selects too; only gamepad / keyboard selection counts
    public void OnSelect(BaseEventData e) => selected = !(e is PointerEventData);
    public void OnDeselect(BaseEventData e) => selected = false;

    void OnDisable() { hovered = false; pressed = false; selected = false; }
}
