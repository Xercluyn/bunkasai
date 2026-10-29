using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Screen UI for the newspaper-delivery arcade game, built with UiKit in the minigame style: score panel,
/// the Cytus-style timing ring (green "throw now" on delivery doors, red ✕ "don't throw" on traps),
/// judgement pop-ups, the picture tutorial, the goal card and the results screen. Quit asks for a second
/// press within a few seconds.
/// </summary>
public class ShinbunHud
{
    const float QuitConfirmSeconds = 3f;
    const float QuitWidth = 120f, QuitArmedWidth = 260f;
    const float RingMargin = 110f, KeyDrop = 50f;   // ring's half size plus some air; the label hangs below it

    static readonly Color TargetWhite = Fade(UiKit.Fg, 0.9f);
    static readonly Color TargetPink = Fade(Color.Lerp(UiKit.Bad, UiKit.Fg, 0.6f), 0.9f);

    readonly MonoBehaviour host;
    readonly RectTransform canvas;
    CanvasGroup hud, prompt;
    Text score, combo, delivered, feedback, caption, key, quitText;
    Image glow, target, approachImg;
    GameObject cross;
    RectTransform promptRoot, approach, quitRect;
    Button quit;
    Coroutine feedbackAnim, quitDisarm;
    DoorKind promptKind = DoorKind.Plain;
    float quitArmedUntil = -1f;
    readonly Action onQuit;

    /// <summary>The screen canvas, for the shared MinigameKit overlays.</summary>
    public Transform Canvas => canvas;

    public ShinbunHud(MonoBehaviour host, Action onQuit)
    {
        this.host = host;
        this.onQuit = onQuit;
        UiKit.EnsureEventSystem();
        canvas = (RectTransform)UiKit.MakeCanvas("ShinbunCanvas", 10, new Vector2(1280f, 720f)).transform;

        // Timing ring: a fixed target ring and an approach ring that shrinks onto it; colours set per door
        prompt = Group("Prompt");
        promptRoot = UiKit.Rect("Ring", prompt.transform);
        promptRoot.anchorMin = promptRoot.anchorMax = promptRoot.pivot = new Vector2(0.5f, 0.5f);
        promptRoot.sizeDelta = new Vector2(150f, 150f);
        glow = UiKit.Img("Glow", promptRoot, UiKit.Glow, Color.clear);
        UiKit.Center(glow.rectTransform, 210f, 210f);
        target = UiKit.Img("Target", promptRoot, UiKit.Ring, TargetWhite);
        UiKit.Stretch(target.rectTransform);
        approachImg = UiKit.Img("Approach", promptRoot, UiKit.Ring, UiKit.Good);
        approach = approachImg.rectTransform;
        UiKit.Stretch(approach);

        // ✕ across a trap's ring, drawn as two bars so it doesn't depend on the font having the glyph
        cross = UiKit.Rect("Cross", promptRoot).gameObject;
        UiKit.Stretch((RectTransform)cross.transform);
        foreach (float angle in new[] { 45f, -45f })
        {
            var bar = UiKit.Box("Bar", cross.transform, UiKit.Bad, 7f);
            UiKit.Center(bar.rectTransform, 110f, 16f).localRotation = Quaternion.Euler(0f, 0f, angle);
        }
        cross.SetActive(false);

        // What to do, under the ring
        key = UiKit.Label(promptRoot, "", 24, UiKit.Good);
        key.horizontalOverflow = HorizontalWrapMode.Overflow;
        UiKit.HardShadow(key, UiKit.PanelShadow, 3f);
        UiKit.Center(key.rectTransform, 360f, 40f).anchoredPosition = new Vector2(0f, -112f);
        SetPromptKind(DoorKind.Deliver);
        prompt.alpha = 0f;

        // Score panel, upper left
        hud = Group("Hud");
        var panel = UiKit.Box("Score", hud.transform, UiKit.Panel, 12f, UiKit.Line, 2f);
        UiKit.Place(panel.rectTransform, 16f, 16f, 280f, 0f);
        var v = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(18, 18, 10, 12);
        v.spacing = 2f;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        UiKit.Label(panel.transform, L.T("スコア", "SCORE"), 18, UiKit.Muted, TextAnchor.MiddleLeft);
        score = UiKit.Label(panel.transform, "0", 34, UiKit.Fg, TextAnchor.MiddleLeft);
        delivered = UiKit.Label(panel.transform, "", 20, UiKit.Good, TextAnchor.MiddleLeft);
        combo = UiKit.Label(panel.transform, "", 20, UiKit.Accent, TextAnchor.MiddleLeft);
        hud.alpha = 0f;

        // Quit, upper right (always clickable); the first press only arms it
        quit = UiKit.SubButton(canvas, L.T("やめる", "Quit"), out quitText);
        quitRect = (RectTransform)quit.transform;
        quitRect.anchorMin = quitRect.anchorMax = quitRect.pivot = new Vector2(1f, 1f);
        quitRect.anchoredPosition = new Vector2(-16f, -16f);
        quitRect.sizeDelta = new Vector2(QuitWidth, 46f);
        BestFit(quitText, 12);
        quit.onClick.AddListener(PressQuit);
        quitText.text = QuitLabel;

        // Judgement pop-up and a caption line near the bottom
        feedback = UiKit.Label(canvas, "", 64, UiKit.Accent);
        UiKit.Center(feedback.rectTransform, 900f, 100f).anchoredPosition = new Vector2(0f, 170f);
        BestFit(feedback, 36);
        UiKit.HardShadow(feedback, UiKit.PanelShadow, 4f);
        caption = UiKit.Label(canvas, "", 26, UiKit.Fg);
        UiKit.Center(caption.rectTransform, 1100f, 60f).anchoredPosition = new Vector2(0f, -280f);
        BestFit(caption, 18);
        UiKit.HardShadow(caption, UiKit.PanelShadow, 3f);
    }

    // Shrinks a fixed-size label's text (down to minSize) instead of letting a long string spill out
    static Text BestFit(Text t, int minSize)
    {
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Truncate;
        t.resizeTextMinSize = minSize;
        t.resizeTextMaxSize = t.fontSize;
        t.resizeTextForBestFit = true;
        return t;
    }

    // A label in a layout group that keeps one line's height and shrinks to fit the card's width
    static Text FitLine(Transform parent, string text, int size, int minSize, Color color, Color shadow, float down)
    {
        var t = BestFit(UiKit.Label(parent, text, size, color), minSize);
        UiKit.HardShadow(t, shadow, down);
        t.gameObject.AddComponent<LayoutElement>().preferredHeight = size * 1.3f;
        return t;
    }

    static Color Fade(Color c, float alpha) => new Color(c.r, c.g, c.b, alpha);

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
        delivered.text = L.T($"配達　{done} / {total}", $"DELIVERED　{done} / {total}");
        combo.text = comboCount > 1 ? L.T($"{comboCount} コンボ！", $"{comboCount} combo!") : "";
    }

    public void Caption(string text) => caption.text = text;

    // ---------- quit ----------

    /// <summary>Quit's click (or the gamepad's B / ○): the first press arms it, a second one within the time quits.</summary>
    public void PressQuit()
    {
        if (!quit.gameObject.activeInHierarchy) return;
        if (Time.unscaledTime < quitArmedUntil) onQuit();
        else ArmQuit();
    }

    // 「やめる」, with the pad's button while the pad is in use
    static string QuitLabel => GamepadInput.T("やめる", "Quit", $"やめる（{GamepadInput.East}）", $"Quit ({GamepadInput.East})");

    /// <summary>Rewrites Quit's label for the current input device (unless it's armed).</summary>
    public void RefreshQuit()
    {
        if (quitDisarm == null) quitText.text = QuitLabel;
    }

    void ArmQuit()
    {
        quitArmedUntil = Time.unscaledTime + QuitConfirmSeconds;
        quitText.text = L.T("もう一度押すと終了", "Press again to quit");
        quitText.color = UiKit.Bad;
        quitRect.sizeDelta = new Vector2(QuitArmedWidth, quitRect.sizeDelta.y);
        if (quitDisarm != null) host.StopCoroutine(quitDisarm);
        quitDisarm = host.StartCoroutine(DisarmQuit());
    }

    IEnumerator DisarmQuit()
    {
        while (Time.unscaledTime < quitArmedUntil) yield return null;
        quitText.text = QuitLabel;
        quitText.color = UiKit.Fg;
        quitRect.sizeDelta = new Vector2(QuitWidth, quitRect.sizeDelta.y);
        quitDisarm = null;
    }

    /// <summary>Hide Quit for good (once the run is cleared, leaving must clear the quest).</summary>
    public void HideQuit() => quit.gameObject.SetActive(false);

    // ---------- timing ring ----------

    /// <summary>Ring at a screen position; progress 0 = just appeared (approach ring 3x), 1 = press now.
    /// Delivery doors (and the landing) get a green "press" ring, traps a red ✕ "don't throw" ring.</summary>
    public void ShowPrompt(Vector3 screenPoint, float progress, DoorKind kind = DoorKind.Deliver)
    {
        if (screenPoint.z < 0f) { HidePrompt(); return; }
        SetPromptKind(kind);
        key.text = kind == DoorKind.Trap
            ? L.T("まって！投げないで", "WAIT! Don't throw")
            : GamepadInput.T("SPACE / クリック！", "SPACE / Click!", $"{GamepadInput.South} ボタン！", $"{GamepadInput.South}!");
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas, screenPoint, null, out Vector2 local);
        // Kept on screen (with room for the label underneath) even if the door is at or past the edge
        Vector2 half = canvas.rect.size * 0.5f;
        local.x = Mathf.Clamp(local.x, -half.x + RingMargin, half.x - RingMargin);
        local.y = Mathf.Clamp(local.y, -half.y + RingMargin + KeyDrop, half.y - RingMargin);
        promptRoot.anchoredPosition = local;
        float s = Mathf.Lerp(3f, 1f, Mathf.Clamp01(progress));
        approach.localScale = new Vector3(s, s, 1f);
        prompt.alpha = Mathf.Clamp01(progress * 4f);
    }

    void SetPromptKind(DoorKind kind)
    {
        if (kind == promptKind) return;
        promptKind = kind;
        bool trap = kind == DoorKind.Trap;
        Color c = trap ? UiKit.Bad : UiKit.Good;
        approachImg.color = c;
        glow.color = new Color(c.r, c.g, c.b, 0.3f);
        target.color = trap ? TargetPink : TargetWhite;
        key.color = c;
        cross.SetActive(trap);
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
        var dim = UiKit.Img("Tutorial", canvas, null, Fade(UiKit.Dim, 0.6f));
        dim.raycastTarget = true;   // keep clicks off the game while reading
        UiKit.Stretch(dim.rectTransform);

        var card = UiKit.Box("Card", dim.transform, UiKit.Bg2, 22f, UiKit.Line, 2f);
        UiKit.Center(card.rectTransform, 980f, 0f);
        var v = card.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(36, 36, 26, 28);
        v.spacing = 14f;
        v.childAlignment = TextAnchor.UpperCenter;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        UiKit.Headline(card.transform, 34,
            (L.T("新聞", "Newspaper"), UiKit.Accent, UiKit.AccentShadow),
            (L.T("配達ミッション", " Delivery Mission"), UiKit.Fg, UiKit.PanelShadow));
        UiKit.Label(card.transform, L.T("まずは飛行機から着地の練習！そのあとエージェントは廊下を自動で走ります。ドアの色を見て新聞を届けよう！",
            "First, a practice landing from the plane! Then the agent runs down the hallway by itself. Watch the door colours and deliver the papers!"),
            19, UiKit.Muted, TextAnchor.MiddleCenter, false);

        var row = UiKit.Rect("Doors", card.transform);
        var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 18f;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = true;
        h.childForceExpandHeight = false;
        DoorCard(row, Green, L.T("緑のドア", "Green door"), L.T("緑のリングが重なったら\n新聞を投げ入れよう！", "Throw the paper in\nwhen the green rings meet!"));
        DoorCard(row, Red, L.T("赤のドア", "Red door"), L.T("赤いリングはトラップ！\n投げずにスルーしよう", "Red ring = trap!\nRun past, don't throw"));
        DoorCard(row, Yellow, L.T("黄色のドア", "Yellow door"), L.T("ふつうの部屋。\nなにも起きない", "An ordinary room.\nNothing happens"));

        string accent = "#" + ColorUtility.ToHtmlStringRGB(UiKit.Accent);
        var throwHelp = UiKit.Label(card.transform, "", 18, UiKit.Fg, TextAnchor.MiddleCenter, false);
        GamepadInput.Bind(throwHelp, () => GamepadInput.T(
            $"<color={accent}>スペース</color> または <color={accent}>クリック</color> で投げる　/　緑のドアを逃す・早すぎると MISS",
            $"<color={accent}>Space</color> or <color={accent}>click</color> to throw　/　Missing a green door, or throwing too early, is a MISS",
            $"<color={accent}>{GamepadInput.South}</color> ボタンで投げる　/　緑のドアを逃す・早すぎると MISS　/　やめる：{GamepadInput.East}",
            $"<color={accent}>{GamepadInput.South}</color> to throw　/　Missing a green door, or throwing too early, is a MISS　/　Quit: {GamepadInput.East}"));

        var start = UiKit.MainButton(card.transform, L.T("▶ スタート", "▶ Start"), out _);
        start.onClick.AddListener(() => onStart());
        GamepadInput.SetDefault(start, GamepadInput.CardPriority);
        return dim.gameObject;
    }

    static readonly Color Green = UiKit.Good, Red = UiKit.Bad, Yellow = UiKit.Hex("#f2c232");

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
        var knob = UiKit.Img("Knob", leaf.transform, UiKit.Circle, UiKit.Fg);
        knob.rectTransform.anchorMin = knob.rectTransform.anchorMax = new Vector2(0.78f, 0.45f);
        knob.rectTransform.sizeDelta = new Vector2(10f, 10f);

        var t = UiKit.Label(box.transform, title, 20, color);
        UiKit.HardShadow(t, UiKit.PanelShadow, 2f);
        UiKit.Label(box.transform, text, 18, UiKit.Fg, TextAnchor.UpperCenter, false);
    }

    // ---------- goal (quick finish) ----------

    /// <summary>A centred "Goal! Cleared!" card with the score; stays until the scene changes.</summary>
    public void ShowGoal(int finalScore, int done, int total)
    {
        HidePrompt();
        Caption("");
        var card = UiKit.Box("Goal", canvas, UiKit.Bg2, 22f, UiKit.Good, 3f);
        UiKit.Center(card.rectTransform, 560f, 0f);
        var v = card.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(36, 36, 24, 26);
        v.spacing = 8f;
        v.childAlignment = TextAnchor.UpperCenter;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        card.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        UiKit.Display(FitLine(card.transform, L.T("ゴール！クリア！", "Goal! Cleared!"), 46, 28, UiKit.Good, UiKit.GoodShadow, 4f));
        UiKit.Label(card.transform, L.T($"スコア　{finalScore}", $"SCORE　{finalScore}"), 30, UiKit.Accent);
        UiKit.Label(card.transform, L.T($"配達成功　{done} / {total}", $"Delivered　{done} / {total}"), 22, UiKit.Fg);
        MinigameKit.Punch(host, card.rectTransform, 1.1f, 0.3f);
    }

    // ---------- results ----------

    public void ShowResults(int finalScore, int done, int total, int perfects, int traps, Action onReturn, Action onRetry)
    {
        ShowHud(false);
        HidePrompt();
        Caption("");
        var dim = UiKit.Img("Results", canvas, null, Fade(UiKit.Dim, 0.7f));
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

        FitLine(card.transform, L.T("ミッションコンプリート！", "Mission Complete!"), 38, 24, UiKit.Good, UiKit.GoodShadow, 3f);
        UiKit.Label(card.transform, L.T("スコア", "SCORE"), 18, UiKit.Muted);
        var s = UiKit.Label(card.transform, finalScore.ToString(), 56, UiKit.Accent);
        UiKit.HardShadow(s, UiKit.AccentShadow, 4f);
        UiKit.Label(card.transform, L.T($"配達成功　{done} / {total}　　PERFECT　{perfects}　　トラップ　{traps}",
            $"Delivered　{done} / {total}　　PERFECT　{perfects}　　Traps　{traps}"), 18, UiKit.Fg);

        var back = UiKit.MainButton(card.transform, L.T("5階に戻る", "Back to 5F"), out _);
        back.onClick.AddListener(() => onReturn());
        GamepadInput.SetDefault(back, GamepadInput.OverlayPriority);
        var retry = UiKit.SubButton(card.transform, L.T("もう一度", "Try Again"), out _);
        retry.onClick.AddListener(() => onRetry());
    }
}
