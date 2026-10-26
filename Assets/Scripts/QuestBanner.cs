using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows the floor's quest each time the scene starts: first big in the centre of the screen, slowly
/// fading away, then pinned as a small panel in the upper-left corner for the rest of the level.
/// Once the quest is cleared (GameProgress.ClearQuest with this scene's name), it shows クエストクリア！ instead.
/// Add it with Tools > Elevator Game > Add Quest Banner.
/// </summary>
public class QuestBanner : MonoBehaviour
{
    [TextArea]
    public string questText = "ITエンジニア学科の教室でミニゲームをクリアしよう！";

    [Tooltip("Quest name for GameProgress. Empty = this scene's name.")]
    public string questId = "";

    [Tooltip("Seconds for the big text to appear.")]
    public float fadeIn = 0.5f;
    [Tooltip("Seconds the big text stays fully visible.")]
    public float hold = 2.5f;
    [Tooltip("Seconds for the big text to fade away.")]
    public float fadeOut = 2f;
    [Tooltip("Seconds for the corner quest to appear.")]
    public float cornerFadeIn = 0.5f;

    CanvasGroup intro, corner;

    void Awake()
    {
        var canvas = UiKit.MakeCanvas("QuestCanvas", 20, new Vector2(1280f, 720f));
        bool cleared = GameProgress.IsQuestCleared(string.IsNullOrEmpty(questId) ? gameObject.scene.name : questId);

        // Intro: dimmed screen with the quest as large as it fits, leaving a small margin
        intro = Layer("Intro", canvas.transform);
        UiKit.Stretch(UiKit.Img("Dim", intro.transform, null, UiKit.Hex("#120f2b", 0.55f)).rectTransform);
        var big = UiKit.Label(intro.transform, cleared ? "クエストクリア！" : questText, 220, cleared ? UiKit.Good : UiKit.Accent);
        var rt = big.rectTransform;
        rt.anchorMin = new Vector2(0.04f, 0.04f);
        rt.anchorMax = new Vector2(0.96f, 0.96f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        big.verticalOverflow = VerticalWrapMode.Truncate;
        big.resizeTextForBestFit = true;
        big.resizeTextMinSize = 40;
        big.resizeTextMaxSize = 220;
        UiKit.HardShadow(big, UiKit.Hex(cleared ? "#13703c" : "#b3560b"), 6f);

        // Corner: panel in the upper left that grows to fit the wrapped quest text
        corner = Layer("Quest", canvas.transform);
        var panel = UiKit.Box("Panel", corner.transform, UiKit.Panel, 12f, UiKit.Line, 2f);
        UiKit.Place(panel.rectTransform, 16f, 16f, 460f, 0f);
        var v = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(18, 18, 12, 14);
        v.spacing = 4f;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        UiKit.Label(panel.transform, cleared ? "クエスト　✓ クリア！" : "クエスト", 14, cleared ? UiKit.Good : UiKit.Accent, TextAnchor.MiddleLeft);
        UiKit.Label(panel.transform, questText, 20, cleared ? UiKit.Muted : UiKit.Fg, TextAnchor.UpperLeft);

        intro.alpha = 0f;
        corner.alpha = 0f;
    }

    // Unscaled time, so a paused game doesn't freeze the banner
    IEnumerator Start()
    {
        yield return Fade(intro, 0f, 1f, fadeIn);
        yield return new WaitForSecondsRealtime(hold);
        yield return Fade(intro, 1f, 0f, fadeOut);
        intro.gameObject.SetActive(false);
        yield return Fade(corner, 0f, 1f, cornerFadeIn);
    }

    // Full-screen layer that never takes clicks, so the game stays playable underneath
    static CanvasGroup Layer(string name, Transform parent)
    {
        var rt = UiKit.Stretch(UiKit.Rect(name, parent));
        var group = rt.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
        return group;
    }

    static IEnumerator Fade(CanvasGroup group, float from, float to, float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            group.alpha = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / seconds));
            yield return null;
        }
        group.alpha = to;
    }
}
