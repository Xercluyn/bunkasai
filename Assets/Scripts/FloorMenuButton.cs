using UnityEngine;

/// <summary>
/// Formerly the 「≡ メニュー」 button in the upper-right corner. Back to the main menu now lives in the pause menu
/// (FloorPauseMenu, Esc), so this builds nothing: it only makes sure the scene has a pause menu. Kept so scenes
/// and editor tools that refer to it still load.
/// </summary>
public class FloorMenuButton : MonoBehaviour
{
    void Awake() => FloorPauseMenu.Ensure();
}
