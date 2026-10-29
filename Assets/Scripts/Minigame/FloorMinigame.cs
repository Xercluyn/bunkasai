using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Unity port of Assets/Minigame (index.html + script.js + style.css): find three "↑" cards
/// among twelve within 55 seconds and 10 taps. スタート (and every restart) runs the shared 3-2-1
/// countdown before the clock starts; a win holds for MinigameKit.ResultHold (skippable) and then
/// rides the elevator to the next floor. Builds its whole UI at runtime, so the scene only needs a
/// camera, an EventSystem and this component.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class FloorMinigame : MonoBehaviour
{
    const float TOTAL_TIME = 55f;      // easy mode (was 30, then 45)
    const int NEED = 3;
    const float FLIP = 0.3f;
    const float HOLD = 0.5f;      // how long a wrong card stays face-up before turning back
    const int MAX_TAPS = 10;           // easy mode (was 7)

    enum Dir { Up, Right, Down, Left }
    static readonly float[] ROT = { 0f, 90f, 180f, 270f };
    enum State { Idle, Countdown, Playing, Paused, Over }
    const int WARN_FROM = 5;      // warning beep once a second over the last seconds

    [Tooltip("Scene loaded by 「次のステージへ」. Leave empty until the next level exists.")]
    [SceneName] public string nextLevelScene = "";

    [Tooltip("The 3D floor scene (used by 「← 戻る」, and by a win when no next level is set).")]
    [SceneName] public string floorScene = "FirstFloorScene";

    class Card
    {
        public int index;
        public Dir dir;
        public RectTransform root, inner, coverSide, faceSide, arrow;
        public Image coverShadow, coverRing, faceGlow, faceShadow, faceRing, faceBorder, arrowImg, flash, focus;
        public CanvasGroup faceGroup;
        public UiPointerState pointer;
        public bool flipped, good, bad, ghost, disabled, hasArrow;
        public float angle, lift, shadowDown = 6f, grow = 1f;
        public Coroutine anim;
    }

    readonly List<Card> cards = new List<Card>();
    Dir[] deck;
    int score, taps;
    State state = State.Idle;
    float startAt, pausedElapsed;
    int lastWarn;                 // the last whole second that beeped
    bool leaving;
    readonly List<Card> pendingBad = new List<Card>();   // wrong cards waiting to turn back
    readonly List<Coroutine> flipTimers = new List<Coroutine>();
    Coroutine endTimer;

    // UI references
    RectTransform board;
    Text timeVal, scoreVal, tapVal;
    RectTransform timeBar;
    Image timeBarImg, timeMeterBorder, tapMeterBorder;
    Image[] scorePips, tapPips;
    Button passBtn, restartPlayBtn;
    Text passBtnText;
    Transform canvasRoot;
    GameObject startScreen, winScreen, loseScreen, passScreen;
    Text winTime, loseReason, loseScore, loseNote, passTime, passScore, passTaps;
    bool timeDanger, tapDanger;
    float cardSize;

    AudioSource audioSource;
    AudioClip sfxGood, sfxBad, sfxWin;
    Confetti confetti;

    /* ---------- deck ---------- */
    static void Shuffle<T>(IList<T> a)
    {
        for (int i = a.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (a[i], a[j]) = (a[j], a[i]);
        }
    }

    static Dir[] MakeDeck()
    {
        var others = new[] { Dir.Down, Dir.Left, Dir.Right };
        var wrong = new List<Dir>(others);
        wrong.AddRange(others);
        wrong.Add(others[UnityEngine.Random.Range(0, 3)]);   // 7, all three kinds present
        Shuffle(wrong);
        var all = new List<Dir> { Dir.Up, Dir.Up, Dir.Up, Dir.Up, Dir.Up };
        all.AddRange(wrong);
        Shuffle(all);
        return all.ToArray();
    }

    /* ---------- setup ---------- */
    void Awake()
    {
        // the floor scene locks the cursor for mouse look; the card game needs it
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        audioSource = GetComponent<AudioSource>();
        audioSource.playOnAwake = false;
        sfxGood = Sfx.Good();
        sfxBad = Sfx.Bad();
        sfxWin = Sfx.Win();

        UiKit.EnsureEventSystem();
        BuildUi();

        // idle board behind the start screen
        deck = MakeDeck();
        BuildBoard();
        foreach (var c in cards) c.disabled = true;
        RenderScore();
        RenderTaps();
        RenderTime(TOTAL_TIME);
    }

    void BuildUi()
    {
        var canvas = UiKit.MakeCanvas("MinigameCanvas", 0, new Vector2(900f, 740f));
        var root = (RectTransform)canvas.transform;
        canvasRoot = root;

        // body background
        var bg = UiKit.Img("Background", root, null, UiKit.Bg);
        UiKit.Stretch(bg.rectTransform);
        var haze = UiKit.Img("Haze", bg.transform, UiKit.Glow, new Color(UiKit.Panel.r, UiKit.Panel.g, UiKit.Panel.b, 0.9f));
        haze.rectTransform.anchorMin = haze.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        haze.rectTransform.sizeDelta = new Vector2(1500f, 700f);
        haze.rectTransform.anchoredPosition = new Vector2(0f, -40f);

        // .wrap column
        const float W = 560f;
        var wrap = UiKit.Rect("Wrap", root);
        UiKit.Center(wrap, W, 678f);

        // header
        var header = UiKit.Headline(wrap, 34,
            (L.T("エレベーターボタン探し", "Elevator Button Hunt"), UiKit.Accent2, UiKit.Accent2Shadow));
        UiKit.Place(header, 0f, 0f, W, 40f);

        // HUD
        float mw = (W - 16f) / 3f;
        BuildMeter(wrap, 0f, mw, L.T("残り時間", "TIME LEFT"), out timeVal, out timeMeterBorder, out var timeBottom);
        var barBg = UiKit.Box("TimerBar", timeBottom, UiKit.Bg2, 3f);
        UiKit.Place(barBg.rectTransform, 0f, 0f, mw - 28f, 6f);
        timeBarImg = UiKit.Box("Fill", barBg.transform, UiKit.Accent, 3f);
        timeBar = UiKit.Stretch(timeBarImg.rectTransform);
        timeBar.pivot = new Vector2(0f, 0.5f);

        BuildMeter(wrap, mw + 8f, mw, L.T("発見数", "FOUND"), out scoreVal, out _, out var scoreBottom);
        scorePips = BuildPips(scoreBottom, 3, 18f);

        BuildMeter(wrap, (mw + 8f) * 2f, mw, L.T("残り回数", "TAPS LEFT"), out tapVal, out tapMeterBorder, out var tapBottom);
        tapPips = BuildPips(tapBottom, MAX_TAPS, 9f);

        // board
        const float pad = 16f, gap = 14f, border = 2f;
        cardSize = (W - 2f * border - 2f * pad - 3f * gap) / 4f;
        float boardH = 3f * cardSize + 2f * gap + 2f * pad + 2f * border;
        var boardBox = UiKit.Box("Board", wrap, UiKit.Bg2, 20f, UiKit.Line, border);
        UiKit.Place(boardBox.rectTransform, 0f, 160f, W, boardH);
        board = boardBox.rectTransform;

        // actions
        float actionsY = 160f + boardH + 14f;
        restartPlayBtn = UiKit.SubButton(wrap, L.T("↻ リスタート", "↻ Restart"), out var rpText);
        passBtn = UiKit.SubButton(wrap, L.T("続ける ▶", "Continue ▶"), out passBtnText);
        float w1 = rpText.preferredWidth + 60f, w2 = passBtnText.preferredWidth + 60f;   // sized for the longer label
        passBtnText.text = L.T("一時停止", "Pause");
        float ax = (W - (w1 + 12f + w2)) * 0.5f;
        UiKit.Place((RectTransform)restartPlayBtn.transform, ax, actionsY, w1, 46f);
        UiKit.Place((RectTransform)passBtn.transform, ax + w1 + 12f, actionsY, w2, 46f);
        restartPlayBtn.interactable = passBtn.interactable = false;
        restartPlayBtn.onClick.AddListener(StartGame);   // リスタート: new shuffle, timer and taps reset
        passBtn.onClick.AddListener(() => { if (state == State.Paused) Resume(); else Pause(); });   // 一時停止 / 続ける

        var foot = ButtonHints.Row(wrap, UiKit.Muted, TextAnchor.MiddleCenter,
            ButtonHints.Keys("「↑」を3枚見つけよう！", "Find three \"↑\" cards!"),
            ButtonHints.Keys("めくる", "Flip", ButtonHints.Click),
            ButtonHints.OnPad("選ぶ", "Choose", ButtonHints.Pad.Stick),
            ButtonHints.OnPad("めくる", "Flip", ButtonHints.Pad.South),
            ButtonHints.OnPad("一時停止", "Pause", ButtonHints.Pad.Menu));
        UiKit.Place(foot, 0f, actionsY + 46f + 10f, W, 26f);

        BuildOverlays(root);

        // back to the 3D floor; above the overlays so it always works
        var back = UiKit.SubButton(root, L.T("← 戻る", "← Back"), out var backText);
        var brt = (RectTransform)back.transform;
        UiKit.Place(brt, 16f, 12f, backText.preferredWidth + 60f, 46f);
        back.onClick.AddListener(() => SceneManager.LoadScene(floorScene));

        var confettiLayer = UiKit.Rect("Confetti", root);
        UiKit.Stretch(confettiLayer);
        confetti = new Confetti(confettiLayer);
    }

    void BuildMeter(RectTransform parent, float x, float w, string label, out Text val, out Image borderImg, out RectTransform bottom)
    {
        borderImg = UiKit.Box("Meter", parent, UiKit.Panel, 14f, UiKit.Line, 2f);
        var m = borderImg.rectTransform;
        UiKit.Place(m, x, 48f, w, 106f);
        var l = UiKit.Label(m, label, 18, UiKit.Muted, TextAnchor.MiddleLeft);
        l.horizontalOverflow = HorizontalWrapMode.Overflow;
        UiKit.Place(l.rectTransform, 14f, 8f, w - 28f, 24f);
        val = UiKit.Label(m, "", 32, UiKit.Fg, TextAnchor.MiddleLeft);
        val.horizontalOverflow = HorizontalWrapMode.Overflow;
        UiKit.Place(val.rectTransform, 14f, 34f, w - 28f, 34f);
        val.rectTransform.pivot = new Vector2(0.3f, 0.5f);   // pulse grows from near the digits
        val.rectTransform.anchoredPosition += new Vector2(0.3f * (w - 28f), -17f);
        bottom = UiKit.Rect("Bottom", m);
        UiKit.Place(bottom, 14f, 78f, w - 28f, 20f);
    }

    Image[] BuildPips(RectTransform parent, int count, float width)
    {
        var pips = new Image[count];
        for (int k = 0; k < count; k++)
        {
            pips[k] = UiKit.Box("Pip", parent, UiKit.Bg2, 5f, UiKit.Line, 2f);
            UiKit.Place(pips[k].rectTransform, k * (width + 6f), 0f, width, 18f);
        }
        return pips;
    }

    void BuildOverlays(RectTransform root)
    {
        // Start screen
        var start = Overlay(root, "StartScreen", out startScreen);
        UiKit.Headline(start, 38, (L.T("ようこそ！", "Welcome to"), UiKit.Accent, UiKit.AccentShadow));
        UiKit.Headline(start, 34,   // the long name on its own line, a little smaller
            (L.T("エレベーターボタン探し", "Elevator Button Hunt"), UiKit.Accent2, UiKit.Accent2Shadow),
            (L.T("へ〜", "!"), UiKit.Accent, UiKit.AccentShadow));
        var rules = Column(start, "Rules", 8f);
        Rule(rules, "↑", UiKit.Good, L.T("「↑」のカードを3枚見つけよう！", "Find the three \"↑\" cards!"));
        Rule(rules, "✕", UiKit.Bad, L.T("違うカードは戻るよ。場所を覚えよう！", "Wrong cards flip back. Remember them!"));
        Rule(rules, MAX_TAPS.ToString(), UiKit.Accent, L.T($"めくれるのは{MAX_TAPS}回まで", $"Only {MAX_TAPS} taps"));
        Rule(rules, ((int)TOTAL_TIME).ToString(), UiKit.Bad, L.T($"制限時間は{(int)TOTAL_TIME}秒！", $"{(int)TOTAL_TIME} seconds!"));
        var startBtn = UiKit.MainButton(start, L.T("スタート！", "Start!"), out _);
        startBtn.onClick.AddListener(StartGame);
        GamepadInput.SetDefault(startBtn, GamepadInput.OverlayPriority);

        // Win screen
        var win = Overlay(root, "WinScreen", out winScreen);
        UiKit.Headline(win, 38, (L.T("おめでとう！", "Congratulations!"), UiKit.Good, UiKit.GoodShadow));
        var winStats = StatRow(win);
        winTime = Stat(winStats, L.T("クリアタイム", "CLEAR TIME"), L.T("0.0秒", "0.0s"));
        Stat(winStats, L.T("発見数", "FOUND"), "3/3");
        var nextBtn = UiKit.MainButton(win, L.T("次のステージへ", "Next Stage"), out _);
        nextBtn.onClick.AddListener(NextLevel);
        GamepadInput.SetDefault(nextBtn, GamepadInput.OverlayPriority);

        // Game over screen
        var lose = Overlay(root, "LoseScreen", out loseScreen);
        UiKit.Headline(lose, 38, (L.T("ゲームオーバー！", "Game Over!"), UiKit.Bad, UiKit.BadShadow));
        var loseStats = StatRow(lose);
        loseReason = Stat(loseStats, L.T("理由", "REASON"), L.T("時間切れ", "Time's up"));
        loseReason.fontSize = 20;
        loseScore = Stat(loseStats, L.T("発見数", "FOUND"), "0/3");
        loseNote = Reason(lose, "");
        var againBtn = UiKit.MainButton(lose, L.T("もう一度", "Try Again"), out _);
        againBtn.onClick.AddListener(StartGame);
        GamepadInput.SetDefault(againBtn, GamepadInput.OverlayPriority);

        // Pass screen
        var pass = Overlay(root, "PassScreen", out passScreen);
        UiKit.Headline(pass, 38, (L.T("一時停止中", "Paused"), UiKit.Accent, UiKit.AccentShadow));
        var passStats = StatRow(pass);
        passTime = Stat(passStats, L.T("残り時間", "TIME LEFT"), L.T("0.0秒", "0.0s"));
        passScore = Stat(passStats, L.T("発見数", "FOUND"), "0/3");
        passTaps = Stat(passStats, L.T("残り回数", "TAPS LEFT"), "7/7");
        Reason(pass, L.T("一時停止中。「続ける」で再開！", "Paused. \"Continue\" to resume."));
        var continueBtn = UiKit.MainButton(pass, L.T("続ける ▶", "Continue ▶"), out _);
        continueBtn.onClick.AddListener(Resume);
        GamepadInput.SetDefault(continueBtn, GamepadInput.OverlayPriority);
        UiKit.SubButton(pass, L.T("↻ リスタート", "↻ Restart"), out _).onClick.AddListener(StartGame);

        startScreen.SetActive(true);
        winScreen.SetActive(false);
        loseScreen.SetActive(false);
        passScreen.SetActive(false);
    }

    // full-screen dim + centred .panel with a vertical stack
    RectTransform Overlay(RectTransform root, string name, out GameObject overlay)
    {
        var dim = UiKit.Img(name, root, null, new Color(UiKit.Dim.r, UiKit.Dim.g, UiKit.Dim.b, 0.78f));
        dim.raycastTarget = true;   // blocks clicks to the board underneath
        UiKit.Stretch(dim.rectTransform);
        overlay = dim.gameObject;

        var panel = UiKit.Box("Panel", dim.transform, UiKit.Panel, 22f, UiKit.Line, 3f).rectTransform;
        UiKit.Center(panel, 480f, 300f);
        var v = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(25, 25, 27, 27);
        v.spacing = 14f;
        v.childAlignment = TextAnchor.UpperCenter;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        panel.gameObject.AddComponent<CanvasGroup>();
        return panel;
    }

    static RectTransform Column(RectTransform parent, string name, float spacing)
    {
        var col = UiKit.Rect(name, parent);
        var v = col.gameObject.AddComponent<VerticalLayoutGroup>();
        v.spacing = spacing;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        return col;
    }

    static void Rule(RectTransform list, string icon, Color iconColor, string text)
    {
        var li = UiKit.Box("Rule", list, UiKit.Bg2, 12f);
        var h = li.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(10, 10, 8, 8);
        h.spacing = 10f;
        h.childAlignment = TextAnchor.UpperLeft;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;

        var b = UiKit.Label(li.transform, icon, 20, iconColor, TextAnchor.UpperCenter);
        b.horizontalOverflow = HorizontalWrapMode.Overflow;
        var be = b.gameObject.AddComponent<LayoutElement>();
        be.minWidth = be.preferredWidth = 30f;
        var t = UiKit.Label(li.transform, text, 19, UiKit.Fg, TextAnchor.UpperLeft);
        t.lineSpacing = 1.15f;
        t.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
    }

    static RectTransform StatRow(RectTransform parent)
    {
        var row = UiKit.Rect("Stats", parent);
        var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 10f;
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;
        return row;
    }

    static Text Stat(RectTransform row, string label, string value)
    {
        var box = UiKit.Box("Stat", row, UiKit.Bg2, 12f);
        var v = box.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(14, 14, 8, 8);
        v.spacing = 2f;
        v.childAlignment = TextAnchor.UpperCenter;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        box.gameObject.AddComponent<LayoutElement>().minWidth = 110f;

        var l = UiKit.Label(box.transform, label, 18, UiKit.Muted);
        l.horizontalOverflow = HorizontalWrapMode.Overflow;
        var s = UiKit.Label(box.transform, value, 26, UiKit.Fg);
        s.horizontalOverflow = HorizontalWrapMode.Overflow;
        return s;
    }

    static Text Reason(RectTransform parent, string text)
    {
        var t = UiKit.Label(parent, text, 18, UiKit.Muted, TextAnchor.UpperCenter, false);
        t.lineSpacing = 1.15f;
        return t;
    }

    void ShowOverlay(GameObject overlay)
    {
        overlay.SetActive(true);
        StartCoroutine(Pop(overlay.transform.GetChild(0)));
    }

    // style.css @keyframes pop: from scale .85, opacity .4 over .35s
    IEnumerator Pop(Transform panel)
    {
        var group = panel.GetComponent<CanvasGroup>();
        for (float t = 0f; t < 0.35f; t += Time.unscaledDeltaTime)
        {
            float e = Ease.CubicBezier(0.3f, 1.5f, 0.5f, 1f, t / 0.35f);
            panel.localScale = Vector3.one * Mathf.LerpUnclamped(0.85f, 1f, e);
            group.alpha = Mathf.Lerp(0.4f, 1f, e);
            yield return null;
        }
        panel.localScale = Vector3.one;
        group.alpha = 1f;
    }

    /* ---------- board ---------- */
    void BuildBoard()
    {
        foreach (var c in cards)
        {
            if (c.anim != null) StopCoroutine(c.anim);
            Destroy(c.root.gameObject);
        }
        cards.Clear();

        float s = cardSize;
        for (int i = 0; i < deck.Length; i++)
        {
            int col = i % 4, row = i / 4;
            var c = new Card { index = i, dir = deck[i] };

            c.root = UiKit.Rect("card-" + i, board);
            UiKit.Place(c.root, 18f + col * (s + 14f), 18f + row * (s + 14f), s, s);
            var hit = c.root.gameObject.AddComponent<Image>();
            hit.sprite = UiKit.Circle;
            hit.color = Color.clear;
            var button = c.root.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = hit;
            button.onClick.AddListener(() => Flip(c));
            c.pointer = c.root.gameObject.AddComponent<UiPointerState>();
            if (i == 5) GamepadInput.SetDefault(button, GamepadInput.BoardPriority);   // the pad starts near the middle

            // the gamepad's cursor: a cyan halo behind the card, face up or down
            c.focus = UiKit.Img("Focus", c.root, UiKit.Circle, UiKit.Accent);
            UiKit.Center(c.focus.rectTransform, s + 18f, s + 18f);
            c.focus.raycastTarget = false;

            c.inner = UiKit.Stretch(UiKit.Rect("Inner", c.root));

            // cover (back)
            c.coverSide = UiKit.Stretch(UiKit.Rect("Cover", c.inner));
            var soft = UiKit.Img("SoftShadow", c.coverSide, UiKit.Glow, new Color(0f, 0f, 0f, 0.4f));
            UiKit.Center(soft.rectTransform, s * 1.3f, s * 1.3f).anchoredPosition = new Vector2(0f, -10f);
            c.coverShadow = UiKit.Img("Shadow", c.coverSide, UiKit.Circle, UiKit.PanelShadow);
            UiKit.Center(c.coverShadow.rectTransform, s, s);
            c.coverRing = UiKit.Img("Ring", c.coverSide, UiKit.Circle, UiKit.Accent);
            UiKit.Center(c.coverRing.rectTransform, s + 6f, s + 6f);
            UiKit.Center(UiKit.Img("Back", c.coverSide, UiKit.CardCover, Color.white).rectTransform, s, s);
            var q = UiKit.Label(c.coverSide, "?", 42, UiKit.Accent);
            q.alignByGeometry = true;   // centre the glyph itself, not the font's line box
            UiKit.Stretch(q.rectTransform);
            UiKit.HardShadow(q, UiKit.AccentShadow, 3f);

            // face
            c.faceSide = UiKit.Stretch(UiKit.Rect("Face", c.inner));
            c.faceGroup = c.faceSide.gameObject.AddComponent<CanvasGroup>();
            c.faceGlow = UiKit.Img("Glow", c.faceSide, UiKit.Glow, Color.clear);
            UiKit.Center(c.faceGlow.rectTransform, s * 1.6f, s * 1.6f);
            var fsoft = UiKit.Img("SoftShadow", c.faceSide, UiKit.Glow, new Color(0f, 0f, 0f, 0.4f));
            UiKit.Center(fsoft.rectTransform, s * 1.3f, s * 1.3f).anchoredPosition = new Vector2(0f, -10f);
            c.faceShadow = UiKit.Img("Shadow", c.faceSide, UiKit.Circle, UiKit.Muted);
            UiKit.Center(c.faceShadow.rectTransform, s, s).anchoredPosition = new Vector2(0f, -6f);
            c.faceRing = UiKit.Img("Ring", c.faceSide, UiKit.Circle, Color.clear);
            UiKit.Center(c.faceRing.rectTransform, s + 6f, s + 6f);
            c.faceBorder = UiKit.Img("Border", c.faceSide, UiKit.Circle, UiKit.Light);
            UiKit.Center(c.faceBorder.rectTransform, s, s);
            UiKit.Center(UiKit.Img("Fill", c.faceSide, UiKit.Circle, UiKit.CardFace).rectTransform, s - 6f, s - 6f);
            c.arrowImg = UiKit.Img("Arrow", c.faceSide, UiKit.Arrow, UiKit.CardInk);
            c.arrow = UiKit.Center(c.arrowImg.rectTransform, (s - 6f) * 0.56f, (s - 6f) * 0.56f);
            c.flash = UiKit.Img("Flash", c.faceSide, UiKit.Circle, Color.clear);   // brief red flash on a wrong card
            UiKit.Center(c.flash.rectTransform, s + 6f, s + 6f);

            cards.Add(c);
            ApplyCard(c, true);
        }
    }

    void Reveal(Card c)
    {
        c.arrow.localEulerAngles = new Vector3(0f, 0f, -ROT[(int)c.dir]);   // CSS rotate is clockwise
        c.hasArrow = true;
        c.flipped = true;
        c.disabled = true;
        Animate(c, 180f);
    }

    /* ---------- play ---------- */
    void Hide(Card c)
    {
        c.flipped = false;
        Animate(c, 0f);
        // clear the arrow and glow once the card is face-down again
        flipTimers.Add(After(FLIP, () =>
        {
            if (c.flipped) return;   // tapped again while turning back
            c.good = c.bad = false;
            c.hasArrow = false;
        }));
        // looks exactly like an untouched card and can be tapped again
        c.disabled = false;
    }

    void Flip(Card c)
    {
        if (state != State.Playing || c.disabled) return;
        taps++;
        RenderTaps();
        Reveal(c);
        if (c.dir == Dir.Up)
        {
            score++;
            c.good = true;
            audioSource.PlayOneShot(sfxGood);
            RenderScore();
            if (score >= NEED) { Finish("win"); return; }
            if (taps >= MAX_TAPS) Finish("taps");
            return; // correct cards stay face-up
        }

        c.bad = true;
        audioSource.PlayOneShot(sfxBad);
        StartCoroutine(WrongFeedback(c));

        if (taps >= MAX_TAPS) { Finish("taps"); return; }
        // turns back on its own; other cards can be tapped meanwhile
        pendingBad.Add(c);
        flipTimers.Add(After(FLIP + HOLD, () =>
        {
            if (state != State.Playing) return;
            Hide(c);
            pendingBad.Remove(c);
        }));
    }

    void RenderTaps()
    {
        int left = MAX_TAPS - taps;
        tapVal.text = left + Small("/" + MAX_TAPS);
        for (int k = 0; k < tapPips.Length; k++) SetPip(tapPips[k], k < left, UiKit.Accent);
        tapDanger = left <= 2;
    }

    void RenderScore()
    {
        scoreVal.text = score + Small("/3");
        for (int k = 0; k < scorePips.Length; k++) SetPip(scorePips[k], k < score, UiKit.Good);
    }

    void RenderTime(float left)
    {
        float t = Mathf.Max(0f, left);
        timeVal.text = t.ToString("0.0") + Small(L.T("秒", "s"));
        timeBar.localScale = new Vector3(t / TOTAL_TIME, 1f, 1f);
        timeDanger = t < 10f;
    }

    static readonly string MutedHex = ColorUtility.ToHtmlStringRGB(UiKit.Muted);
    static string Small(string s) => "<size=20><color=#" + MutedHex + ">" + s + "</color></size>";

    static void SetPip(Image pip, bool on, Color onColor)
    {
        pip.color = on ? onColor : UiKit.Line;
        pip.transform.GetChild(0).GetComponent<Image>().color = on ? onColor : UiKit.Bg2;
    }

    void Update()
    {
        if (state == State.Playing)
        {
            float left = TOTAL_TIME - (Time.time - startAt);
            RenderTime(left);
            int sec = Mathf.CeilToInt(left);
            if (sec >= 1 && sec <= WARN_FROM && sec != lastWarn)
            {
                lastWarn = sec;
                audioSource.PlayOneShot(Sfx.Warn());
            }
            if (left <= 0f) Finish("time");
        }

        UpdateDanger(timeVal, timeMeterBorder, timeDanger);
        UpdateDanger(tapVal, tapMeterBorder, tapDanger);
        timeBarImg.color = timeDanger ? UiKit.Bad : UiKit.Accent;

        foreach (var c in cards) ApplyCard(c, false);
        confetti.Update(Time.deltaTime);

        // gamepad Menu / OPTIONS: 一時停止 / 続ける
        if (GamepadInput.MenuPressed)
        {
            if (state == State.Playing) Pause();
            else if (state == State.Paused) Resume();
        }
    }

    // .meter.danger: red border and a pulsing value
    static void UpdateDanger(Text val, Image border, bool danger)
    {
        border.color = danger ? UiKit.Bad : UiKit.Line;
        val.color = danger ? UiKit.Bad : UiKit.Fg;
        float pulse = danger ? Mathf.SmoothStep(0f, 1f, Mathf.PingPong(Time.time / 0.5f, 1f)) : 0f;
        val.rectTransform.localScale = Vector3.one * (1f + 0.06f * pulse);
    }

    // Push card state into its visuals: flip angle, hover lift, glow and ghost.
    void ApplyCard(Card c, bool instant)
    {
        bool showFace = c.angle > 90f;
        c.coverSide.gameObject.SetActive(!showFace);
        c.faceSide.gameObject.SetActive(showFace);

        // .card:not([disabled]):not(.flipped):hover / :active on the cover
        bool live = !c.disabled && !c.flipped;
        bool hover = live && c.pointer.Highlighted;
        c.focus.enabled = c.pointer.selected || hover;
        bool press = live && c.pointer.pressed;
        float liftTarget = press ? -2f : hover ? 4f : 0f;
        float shadowTarget = press ? 3f : hover ? 10f : 6f;
        float k = instant ? 1f : 1f - Mathf.Exp(-Time.deltaTime / 0.06f);   // ~.18s transition
        c.lift = Mathf.Lerp(c.lift, liftTarget, k);
        c.shadowDown = Mathf.Lerp(c.shadowDown, shadowTarget, k);
        // the flip squeezes x; a hovered card also grows a little about its centre
        c.grow = Mathf.Lerp(c.grow, hover && !press ? 1.06f : 1f, k);
        c.inner.localScale = new Vector3(Mathf.Max(0.001f, Mathf.Abs(Mathf.Cos(c.angle * Mathf.Deg2Rad))) * c.grow, c.grow, 1f);
        c.coverSide.anchoredPosition = new Vector2(0f, c.lift);
        c.coverShadow.rectTransform.anchoredPosition = new Vector2(0f, -c.shadowDown);
        c.coverRing.enabled = hover && !press;

        Color glowColor = c.good ? UiKit.Good : c.bad ? UiKit.Bad : Color.clear;
        bool lit = c.good || c.bad;
        c.faceGlow.color = lit ? new Color(glowColor.r, glowColor.g, glowColor.b, c.good ? 0.55f : 0.6f) : Color.clear;
        c.faceRing.color = lit ? glowColor : Color.clear;
        c.faceBorder.color = lit ? glowColor : UiKit.Light;
        c.faceShadow.color = c.good ? UiKit.GoodShadow : c.bad ? UiKit.BadShadow : UiKit.Muted;
        c.arrowImg.enabled = c.hasArrow;
        c.arrowImg.color = c.good ? UiKit.GoodShadow : c.bad ? UiKit.Bad : UiKit.CardInk;
        c.faceGroup.alpha = c.ghost ? 0.55f : 1f;
    }

    void Animate(Card c, float target)
    {
        if (c.anim != null) StopCoroutine(c.anim);
        c.anim = StartCoroutine(FlipAnim(c, target));
    }

    // .inner transition: transform .6s cubic-bezier(.4,.1,.2,1.1)
    IEnumerator FlipAnim(Card c, float target)
    {
        float from = c.angle;
        for (float t = 0f; t < FLIP; t += Time.deltaTime)
        {
            c.angle = Mathf.LerpUnclamped(from, target, Ease.CubicBezier(0.4f, 0.1f, 0.2f, 1.1f, t / FLIP));
            yield return null;
        }
        c.angle = target;
        c.anim = null;
    }

    // wrong card: a firm shake and a red flash, started as the flip lands
    IEnumerator WrongFeedback(Card c)
    {
        yield return new WaitForSeconds(FLIP * 0.9f);
        if (c.root == null) yield break;   // board rebuilt meanwhile
        MinigameKit.Shake(this, c.root, 10f, 0.35f);
        for (float t = 0f; t < 0.4f; t += Time.deltaTime)
        {
            if (c.flash == null) yield break;
            c.flash.color = new Color(UiKit.Bad.r, UiKit.Bad.g, UiKit.Bad.b, 0.75f * (1f - t / 0.4f));
            yield return null;
        }
        if (c.flash != null) c.flash.color = Color.clear;
    }

    void StartGame()
    {
        if (endTimer != null) StopCoroutine(endTimer);
        confetti.Stop();
        startScreen.SetActive(false);
        winScreen.SetActive(false);
        loseScreen.SetActive(false);
        passScreen.SetActive(false);
        ClearFlipTimers();
        pendingBad.Clear();
        deck = MakeDeck();
        score = 0;
        taps = 0;
        BuildBoard();
        RenderScore();
        RenderTaps();
        RenderTime(TOTAL_TIME);
        // the clock starts after 3-2-1; the countdown layer blocks the board meanwhile
        state = State.Countdown;
        passBtn.interactable = restartPlayBtn.interactable = false;
        passBtnText.text = L.T("一時停止", "Pause");
        lastWarn = 0;
        MinigameKit.Countdown(this, canvasRoot, () =>
        {
            if (state != State.Countdown) return;
            state = State.Playing;
            passBtn.interactable = restartPlayBtn.interactable = true;
            startAt = Time.time;
        });
    }

    /* ---------- pass = pause, then continue from the same spot ---------- */
    void Pause()
    {
        if (state != State.Playing) return;
        state = State.Paused;
        pausedElapsed = Time.time - startAt;
        ClearFlipTimers();
        foreach (var c in pendingBad) Hide(c);
        pendingBad.Clear();
        passBtnText.text = L.T("続ける ▶", "Continue ▶");
        passTime.text = Mathf.Max(0f, TOTAL_TIME - pausedElapsed).ToString("0.0") + L.T("秒", "s");
        passScore.text = score + "/3";
        passTaps.text = (MAX_TAPS - taps) + "/" + MAX_TAPS;
        ShowOverlay(passScreen);
    }

    void Resume()
    {
        if (state != State.Paused) return;
        passScreen.SetActive(false);
        state = State.Playing;
        passBtnText.text = L.T("一時停止", "Pause");
        startAt = Time.time - pausedElapsed;
    }

    void Finish(string result)
    {
        if (state != State.Playing) return;
        state = State.Over;
        passBtn.interactable = false;
        restartPlayBtn.interactable = false;
        passBtnText.text = L.T("一時停止", "Pause");
        ClearFlipTimers();
        float elapsed = Mathf.Min(TOTAL_TIME, Time.time - startAt);
        RenderTime(TOTAL_TIME - elapsed);
        foreach (var c in cards) if (!c.flipped) c.disabled = true;

        if (result == "win")
        {
            winTime.text = elapsed.ToString("0.0") + L.T("秒", "s");
            endTimer = StartCoroutine(Delay(FLIP + 0.25f, () =>
            {
                ShowOverlay(winScreen);
                audioSource.PlayOneShot(sfxWin);
                confetti.Burst();
                // holds a moment (click / Space skips), then straight on to the next floor
                endTimer = MinigameKit.ResultThenExit(this, canvasRoot, NextLevel, MinigameKit.ResultHold);
            }));
        }
        else
        {
            if (result == "time") audioSource.PlayOneShot(sfxBad);
            loseReason.text = result == "time" ? L.T("時間切れ", "Time's up") : L.T("回数切れ", "Out of taps");
            loseScore.text = score + "/3";
            loseNote.text = result == "time"
                ? L.T("時間切れ！もう一度！", "Out of time. Try again!")
                : L.T("回数切れ！もう一度！", "Out of taps. Try again!");
            // show where every arrow was
            endTimer = StartCoroutine(Delay(result == "time" ? 0.15f : FLIP + 0.35f, () =>
            {
                foreach (var c in cards)
                {
                    if (c.flipped) continue;
                    c.good = c.bad = false;
                    Reveal(c);
                    c.ghost = true;
                }
                endTimer = StartCoroutine(Delay(FLIP + 0.5f, () => ShowOverlay(loseScreen)));
            }));
        }
    }

    void NextLevel()
    {
        if (leaving) return;   // the result hold and 「次のステージへ」 can both fire
        leaving = true;
        if (GameProgress.PlayingSingleFloor)
        {
            // only the first floor was picked on the menu: winning here clears it (the ending shows there)
            GameProgress.ClearQuest(floorScene);
            SceneManager.LoadScene(floorScene);
            return;
        }
        if (!string.IsNullOrEmpty(nextLevelScene) && Application.CanStreamedLevelBeLoaded(nextLevelScene))
        {
            ElevatorRide.Go(nextLevelScene, 1, 2);   // the first floor's elevator rides up to the second
            return;
        }
        Debug.LogWarning("FloorMinigame: next level scene not set or not in Build Settings; returning to the floor.");
        SceneManager.LoadScene(floorScene);
    }

    /* ---------- timers (setTimeout equivalents) ---------- */
    Coroutine After(float seconds, Action action) => StartCoroutine(Delay(seconds, action));

    static IEnumerator Delay(float seconds, Action action)
    {
        yield return new WaitForSeconds(seconds);
        action();
    }

    void ClearFlipTimers()
    {
        foreach (var t in flipTimers) if (t != null) StopCoroutine(t);
        flipTimers.Clear();
    }

    /* ---------- easing ---------- */
    static class Ease
    {
        /// <summary>CSS cubic-bezier(x1, y1, x2, y2) evaluated at progress u.</summary>
        public static float CubicBezier(float x1, float y1, float x2, float y2, float u)
        {
            u = Mathf.Clamp01(u);
            float lo = 0f, hi = 1f, t = u;
            for (int i = 0; i < 20; i++)   // bisection on x(t) = u
            {
                t = (lo + hi) * 0.5f;
                if (Bez(x1, x2, t) < u) lo = t; else hi = t;
            }
            return Bez(y1, y2, t);
        }

        static float Bez(float p1, float p2, float t)
        {
            float m = 1f - t;
            return 3f * m * m * t * p1 + 3f * m * t * t * p2 + t * t * t;
        }
    }

    /* ---------- confetti ---------- */
    class Confetti
    {
        static readonly Color[] Colors =
        {
            UiKit.Accent, UiKit.Fg, UiKit.Accent2,
            UiKit.CardBackA, UiKit.Light, UiKit.Good
        };

        class Bit { public RectTransform rt; public float x, y, vx, vy, w, h, r, vr; public int life; }

        readonly RectTransform layer;
        readonly List<Bit> parts = new List<Bit>();
        float acc;
        bool running;

        public Confetti(RectTransform layer) { this.layer = layer; }

        public void Burst()
        {
            var size = layer.rect.size;
            if (parts.Count == 0)
                for (int k = 0; k < 180; k++)
                {
                    var img = UiKit.Img("Bit", layer, null, Colors[k % Colors.Length]);
                    img.rectTransform.anchorMin = img.rectTransform.anchorMax = new Vector2(0f, 1f);
                    parts.Add(new Bit { rt = img.rectTransform });
                }

            for (int k = 0; k < parts.Count; k++)
            {
                var p = parts[k];
                bool fromLeft = k % 2 == 0;
                p.x = fromLeft ? -10f : size.x + 10f;
                p.y = size.y * (0.55f + UnityEngine.Random.value * 0.3f);
                p.vx = (fromLeft ? 1f : -1f) * (4f + UnityEngine.Random.value * 7f);
                p.vy = -(9f + UnityEngine.Random.value * 9f);
                p.w = 6f + UnityEngine.Random.value * 6f;
                p.h = 8f + UnityEngine.Random.value * 8f;
                p.r = UnityEngine.Random.value * Mathf.PI;
                p.vr = (UnityEngine.Random.value - 0.5f) * 0.3f;
                p.life = 0;
                p.rt.gameObject.SetActive(true);
            }
            acc = 0f;
            running = true;
        }

        public void Stop()
        {
            running = false;
            foreach (var p in parts) p.rt.gameObject.SetActive(false);
        }

        // script.js steps once per animation frame; run that same step at 60 Hz
        public void Update(float dt)
        {
            if (!running) return;
            float height = layer.rect.height;
            acc += dt;
            int alive = 0;
            while (acc >= 1f / 60f)
            {
                acc -= 1f / 60f;
                alive = 0;
                foreach (var p in parts)
                {
                    p.life++;
                    p.vy += 0.28f; p.vx *= 0.99f; p.vy *= 0.99f;
                    p.x += p.vx; p.y += p.vy; p.r += p.vr;
                    if (p.y < height + 30f) alive++;
                }
                if (alive == 0) { Stop(); return; }
            }
            foreach (var p in parts)
            {
                float flap = Mathf.Abs(Mathf.Cos(p.life * 0.15f));
                p.rt.anchoredPosition = new Vector2(p.x, -p.y);
                p.rt.sizeDelta = new Vector2(p.w, p.h * flap + 1f);
                p.rt.localEulerAngles = new Vector3(0f, 0f, -p.r * Mathf.Rad2Deg);
            }
        }
    }
}
