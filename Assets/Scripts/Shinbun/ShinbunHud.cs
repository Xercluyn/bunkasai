using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Screen UI for the newspaper-delivery arcade game, built with UiKit in the minigame style: score panel,
/// the Cytus-style timing ring, judgement pop-ups, the picture tutorial and the results screen.
/// </summary>
public class ShinbunHud
{
    readonly MonoBehaviour host;
    readonly RectTransform canvas;
    CanvasGroup hud, prompt;
    Text score, combo, delivered, feedback, caption;
    RectTransform promptRoot, approach;
    Coroutine feedbackAnim;

    public ShinbunHud(MonoBehaviour host, Action onQuit)
    {
        this.host = host;
        UiKit.EnsureEventSystem();
        canvas = (RectTransform)UiKit.MakeCanvas("ShinbunCanvas", 10, new Vector2(1280f, 720f)).transform;

        // Timing ring: a fixed target ring and an approach ring that shrinks onto it
        prompt = Group("Prompt");
        promptRoot = UiKit.Rect("Ring", prompt.transform);
        promptRoot.anchorMin = promptRoot.anchorMax = promptRoot.pivot = new Vector2(0.5f, 0.5f);
        promptRoot.sizeDelta = new Vector2(150f, 150f);
        var glow = UiKit.Img("Glow", promptRoot, UiKit.Glow, UiKit.Hex("#ffb526", 0.25f));
        UiKit.Center(glow.rectTransform, 210f, 210f);
        UiKit.Stretch(UiKit.Img("Target", promptRoot, UiKit.Ring, UiKit.Hex("#ffffff", 0.9f)).rectTransform);
        approach = UiKit.Img("Approach", promptRoot, UiKit.Ring, UiKit.Accent).rectTransform;
        UiKit.Stretch(approach);
        var key = UiKit.Label(promptRoot, "SPACE", 20, UiKit.Fg);
        UiKit.HardShadow(key, UiKit.Hex("#0c0a24"), 2f);
        UiKit.Stretch(key.rectTransform);
        prompt.alpha = 0f;

        // Score panel, upper left
        hud = Group("Hud");
        var panel = UiKit.Box("Score", hud.transform, UiKit.Panel, 12f, UiKit.Line, 2f);
        UiKit.Place(panel.rectTransform, 16f, 16f, 250f, 0f);
        var v = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(18, 18, 10, 12);
        v.spacing = 2f;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        UiKit.Label(panel.transform, "スコア", 12, UiKit.Muted, TextAnchor.MiddleLeft);
        score = UiKit.Label(panel.transform, "0", 30, UiKit.Fg, TextAnchor.MiddleLeft);
        delivered = UiKit.Label(panel.transform, "", 15, UiKit.Good, TextAnchor.MiddleLeft);
        combo = UiKit.Label(panel.transform, "", 15, UiKit.Accent, TextAnchor.MiddleLeft);
        hud.alpha = 0f;

        // Quit, upper right (always clickable)
        var quit = UiKit.SubButton(canvas, "やめる", out _);
        var qr = (RectTransform)quit.transform;
        qr.anchorMin = qr.anchorMax = qr.pivot = new Vector2(1f, 1f);
        qr.anchoredPosition = new Vector2(-16f, -16f);
        qr.sizeDelta = new Vector2(120f, 46f);
        quit.onClick.AddListener(() => onQuit());

        // Judgement pop-up and a caption line near the bottom
        feedback = UiKit.Label(canvas, "", 64, UiKit.Accent);
        UiKit.Center(feedback.rectTransform, 900f, 100f).anchoredPosition = new Vector2(0f, 170f);
        UiKit.HardShadow(feedback, UiKit.Hex("#0c0a24"), 4f);
        caption = UiKit.Label(canvas, "", 26, UiKit.Fg);
        UiKit.Center(caption.rectTransform, 1100f, 60f).anchoredPosition = new Vector2(0f, -280f);
        UiKit.HardShadow(caption, UiKit.Hex("#0c0a24"), 3f);
    }

    CanvasGroup Group(string name)
    {
        var rt = UiKit.Stretch(UiKit.Rect(name, canvas));
        var g = rt.gameObject.AddComponent<CanvasGroup>();
        g.blocksRaycasts = false;
        return g;
    }

    // ---------- HUD ----------

    public void ShowHud(bool show) => hud.alpha = show ? 1f : 0f;

    public void SetScore(int value, int comboCount, int done, int total)
    {
        score.text = value.ToString();
        delivered.text = $"配達　{done} / {total}";
        combo.text = comboCount > 1 ? $"{comboCount} コンボ！" : "";
    }

    public void Caption(string text) => caption.text = text;

    // ---------- timing ring ----------

    /// <summary>Ring at a screen position; progress 0 = just appeared (approach ring 3x), 1 = press now.</summary>
    public void ShowPrompt(Vector3 screenPoint, float progress)
    {
        if (screenPoint.z < 0f) { HidePrompt(); return; }
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, screenPoint, null, out Vector2 local);
        promptRoot.anchoredPosition = local;
        float s = Mathf.Lerp(3f, 1f, Mathf.Clamp01(progress));
        approach.localScale = new Vector3(s, s, 1f);
        prompt.alpha = Mathf.Clamp01(progress * 4f);
    }

    public void HidePrompt() => prompt.alpha = 0f;

    // ---------- judgement pop-up ----------

    public void Pop(string text, Color color)
    {
        if (feedbackAnim != null) host.StopCoroutine(feedbackAnim);
        feedbackAnim = host.StartCoroutine(PopAnim(text, color));
    }

    IEnumerator PopAnim(string text, Color color)
    {
        feedback.text = text;
        for (float t = 0f; t < 0.9f; t += Time.deltaTime)
        {
            float grow = Mathf.Clamp01(t / 0.15f);
            feedback.transform.localScale = Vector3.one * Mathf.Lerp(1.5f, 1f, 1f - (1f - grow) * (1f - grow));
            color.a = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.3f;
            feedback.color = color;
            yield return null;
        }
        feedback.text = "";
    }

    // ---------- tutorial ----------

    /// <summary>The how-to-play card with a picture of each door; calls onStart from its button.</summary>
    public GameObject ShowTutorial(Action onStart)
    {
        var dim = UiKit.Img("Tutorial", canvas, null, UiKit.Hex("#120f2b", 0.6f));
        dim.raycastTarget = true;   // keep clicks off the game while reading
        UiKit.Stretch(dim.rectTransform);

        var card = UiKit.Box("Card", dim.transform, UiKit.Bg2, 22f, UiKit.Line, 2f);
        UiKit.Center(card.rectTransform, 900f, 0f);
        var v = card.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(36, 36, 26, 28);
        v.spacing = 14f;
        v.childAlignment = TextAnchor.UpperCenter;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        UiKit.Headline(card.transform, 34,
            ("新聞", UiKit.Accent, UiKit.Hex("#b3560b")),
            ("配達ミッション", UiKit.Fg, UiKit.Hex("#0c0a24")));
        UiKit.Label(card.transform, "エージェントはアパートの廊下を自動で走ります。ドアの色を見て新聞を届けよう！",
            17, UiKit.Muted, TextAnchor.MiddleCenter, false);

        var row = UiKit.Rect("Doors", card.transform);
        var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 18f;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = true;
        h.childForceExpandHeight = false;
        DoorCard(row, Green, "緑のドア", "リングが重なった瞬間に\n新聞を投げ入れよう！");
        DoorCard(row, Red, "赤のドア", "トラップ！\n投げずにスルーしよう");
        DoorCard(row, Yellow, "黄色のドア", "ふつうの部屋。\nなにも起きない");

        UiKit.Label(card.transform, "<color=#ffb526>スペース</color> または <color=#ffb526>クリック</color> で投げる　/　PERFECT・GOOD で得点、緑を逃すと MISS",
            16, UiKit.Fg, TextAnchor.MiddleCenter, false);

        var start = UiKit.MainButton(card.transform, "▶ スタート", out _);
        start.onClick.AddListener(() => onStart());
        return dim.gameObject;
    }

    static readonly Color Green = UiKit.Hex("#2fd16a"), Red = UiKit.Hex("#e8333f"), Yellow = UiKit.Hex("#f2c232");

    static void DoorCard(Transform parent, Color color, string title, string text)
    {
        var box = UiKit.Box("Door", parent, UiKit.Panel, 16f, UiKit.Line, 2f);
        var v = box.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(14, 14, 16, 16);
        v.spacing = 8f;
        v.childAlignment = TextAnchor.UpperCenter;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;

        // Picture: a door in its frame with a knob
        var pic = UiKit.Rect("Picture", box.transform);
        pic.gameObject.AddComponent<LayoutElement>().preferredHeight = 120f;
        var frame = UiKit.Box("Frame", pic, color, 8f);
        UiKit.Center(frame.rectTransform, 74f, 116f);
        var leaf = UiKit.Box("Leaf", frame.transform, Color.Lerp(color, Color.black, 0.28f), 5f);
        UiKit.Stretch(leaf.rectTransform, 8f);
        leaf.rectTransform.offsetMin = new Vector2(8f, 0f);
        var knob = UiKit.Img("Knob", leaf.transform, UiKit.Circle, UiKit.Hex("#fdfbff"));
        knob.rectTransform.anchorMin = knob.rectTransform.anchorMax = new Vector2(0.78f, 0.45f);
        knob.rectTransform.sizeDelta = new Vector2(10f, 10f);

        var t = UiKit.Label(box.transform, title, 20, color);
        UiKit.HardShadow(t, UiKit.Hex("#0c0a24"), 2f);
        UiKit.Label(box.transform, text, 15, UiKit.Fg, TextAnchor.UpperCenter, false);
    }

    // ---------- results ----------

    public void ShowResults(int finalScore, int done, int total, int perfects, int traps, Action onReturn, Action onRetry)
    {
        ShowHud(false);
        HidePrompt();
        Caption("");
        var dim = UiKit.Img("Results", canvas, null, UiKit.Hex("#120f2b", 0.7f));
        dim.raycastTarget = true;
        UiKit.Stretch(dim.rectTransform);

        var card = UiKit.Box("Card", dim.transform, UiKit.Bg2, 22f, UiKit.Line, 2f);
        UiKit.Center(card.rectTransform, 560f, 0f);
        var v = card.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(36, 36, 28, 30);
        v.spacing = 12f;
        v.childAlignment = TextAnchor.UpperCenter;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        UiKit.Headline(card.transform, 38, ("ミッションコンプリート！", UiKit.Good, UiKit.Hex("#13703c")));
        UiKit.Label(card.transform, "スコア", 14, UiKit.Muted);
        var s = UiKit.Label(card.transform, finalScore.ToString(), 56, UiKit.Accent);
        UiKit.HardShadow(s, UiKit.Hex("#b3560b"), 4f);
        UiKit.Label(card.transform, $"配達成功　{done} / {total}　　PERFECT　{perfects}　　トラップ　{traps}", 18, UiKit.Fg);

        var back = UiKit.MainButton(card.transform, "5階に戻る", out _);
        back.onClick.AddListener(() => onReturn());
        var retry = UiKit.SubButton(card.transform, "もう一度", out _);
        retry.onClick.AddListener(() => onRetry());
    }
}
