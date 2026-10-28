using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The end of the game: on the fifth floor once its quest is cleared (back from the newspaper game), or, when
/// one floor was picked on the menu, once that floor's quest is cleared. A panel says the game is cleared and
/// offers メニューへ (back to the menu, a new game) or staying to look around; the floor's 「≡ メニュー」 button
/// (FloorMenuButton) stays in the corner either way. The panel waits for the クエストクリア！ celebration, then pops in.
/// Added by Tools > Elevator Game > Add Ending To Fifth Floor, or at runtime by PlayerMovement on every floor:
/// a runtime one only shows on the top floor (quest cleared and no elevator going up) or in one-floor mode.
/// </summary>
public class GameEnding : MonoBehaviour
{
    [Tooltip("Where the game starts again when there's no menu scene.")]
    [SceneName] public string firstFloorScene = "";

    [Tooltip("Quest that ends the game. Empty = this scene's name (the floor's QuestBanner).")]
    public string questId = "";

    [Tooltip("Seconds to wait after arriving, so the クエストクリア！ banner shows first.")]
    public float delay = 2.5f;

    /// <summary>Created by PlayerMovement rather than placed in the scene: decides for itself whether this is the top floor.</summary>
    [System.NonSerialized] public bool automatic;
    /// <summary>The floor it belongs to (a runtime one is created in the active scene).</summary>
    [System.NonSerialized] public string floor;

    GameObject panel;
    CanvasGroup panelGroup;
    RectTransform box;
    Text title, text;

    string Quest => !string.IsNullOrEmpty(questId) ? questId : !string.IsNullOrEmpty(floor) ? floor : gameObject.scene.name;

    void Awake()
    {
        var canvas = UiKit.MakeCanvas("EndingCanvas", 30, new Vector2(1280f, 720f));
        UiKit.EnsureEventSystem();

        // centred panel over a dimmed screen
        var dim = UiKit.Img("Ending", canvas.transform, null, UiKit.Hex("#120f2b", 0.7f));
        dim.raycastTarget = true;
        UiKit.Stretch(dim.rectTransform);
        panel = dim.gameObject;
        panelGroup = panel.AddComponent<CanvasGroup>();
        box = UiKit.Box("Panel", dim.transform, UiKit.Panel, 24f, UiKit.Accent, 3f).rectTransform;
        UiKit.Center(box, 640f, 330f);
        title = UiKit.Display(UiKit.Label(box, "", 56, UiKit.Accent));
        UiKit.HardShadow(title, UiKit.Hex("#b3560b"), 5f);
        UiKit.Place(title.rectTransform, 0f, 30f, 640f, 76f);
        text = UiKit.Label(box, "", 22, UiKit.Fg);
        UiKit.Place(text.rectTransform, 40f, 118f, 560f, 70f);
        var again = UiKit.MainButton(box, L.T("メニューへ", "Back to menu"), out _);
        UiKit.Place((RectTransform)again.transform, 90f, 228f, 220f, 56f);
        again.onClick.AddListener(Restart);
        var stay = UiKit.SubButton(box, L.T("このまま見て回る", "Keep exploring"), out var stayText);
        UiKit.Place((RectTransform)stay.transform, 330f, 233f, Mathf.Max(220f, stayText.preferredWidth + 48f), 46f);
        stay.onClick.AddListener(Close);
        panel.SetActive(false);
    }

    IEnumerator Start()
    {
        while (!GameProgress.IsQuestCleared(Quest)) yield return null;
        // a floor with an elevator going up isn't the end of the game, unless it was picked on its own
        bool single = GameProgress.PlayingSingleFloor;
        if (automatic && !single && FindAnyObjectByType<ElevatorExitZone>() != null)
        {
            Destroy(gameObject);
            yield break;
        }
        if (single)
        {
            string at = FloorLabel();
            title.text = L.T("クリア！", "Cleared!");
            text.text = L.T($"おめでとう！{at}のクエストをクリアしました。", $"Congratulations! You cleared the quest on {at}.");
        }
        else
        {
            title.text = L.T("全フロアクリア！", "All Floors Clear!");
            text.text = L.T("おめでとう！学園祭のすべてのクエストをクリアしました。", "Congratulations! You cleared every quest of the school festival.");
        }
        yield return new WaitForSecondsRealtime(delay);
        while (QuestBanner.Celebrating) yield return null;
        panel.SetActive(true);
        ThirdPersonCamera.panelOpen = true;   // free the mouse so the buttons can be clicked

        // dim fades in, the panel pops: 85% → 103% → 100%
        for (float t = 0f; t < 0.45f; t += Time.unscaledDeltaTime)
        {
            float k = t / 0.45f;
            panelGroup.alpha = Mathf.Clamp01(k * 3f);
            box.localScale = Vector3.one * Mathf.LerpUnclamped(0.85f, 1f, UiKit.EaseOutBack(k));
            yield return null;
        }
        panelGroup.alpha = 1f;
        box.localScale = Vector3.one;
    }

    void Close()
    {
        panel.SetActive(false);
        ThirdPersonCamera.panelOpen = false;
    }

    // 「２階」 / "2F" from the floor's quest banner; "this floor" without one
    string FloorLabel()
    {
        var banner = FindAnyObjectByType<QuestBanner>();
        int n = banner != null ? banner.FloorNumber : 0;
        return n > 0 ? L.T(n + "階", n + "F") : L.T("このフロア", "this floor");
    }

    void Restart()
    {
        ThirdPersonCamera.panelOpen = false;
        // no quests, no money: a new game from the menu (or the first floor if there's no menu)
        if (!string.IsNullOrEmpty(GameProgress.MenuScene) && Application.CanStreamedLevelBeLoaded(GameProgress.MenuScene))
        {
            GameProgress.ToMenu();
            return;
        }
        GameProgress.NewSession();
        GameProgress.SingleFloor = "";
        if (!string.IsNullOrEmpty(firstFloorScene) && Application.CanStreamedLevelBeLoaded(firstFloorScene))
            SceneManager.LoadScene(firstFloorScene);
        else
            SceneManager.LoadScene(0);
    }

    void OnDestroy() => ThirdPersonCamera.panelOpen = false;
}
