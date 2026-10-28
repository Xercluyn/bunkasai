using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Unity port of Assets/Minigame/SecondFloorGame (index.html + script.js + style.css): a swinging hook pulls
/// coins and notes out of the ground; collect ¥3,000 within 85 seconds. The start card (with the legend) leads into
/// a 3-2-1 countdown; ↑ lets a held valuable drop back and throws junk away. Builds its whole UI at runtime, so the
/// scene only needs a camera, an EventSystem and this component. Winning adds the money to GameProgress.Yen
/// (once), which buys the third-floor ticket at the second floor's vending machine, then the game leaves by itself.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class SecondFloorGame : MonoBehaviour
{
    const float W = 800f, H = 520f, GROUND = 118f, PX = 400f, PY = 96f;
    // easy mode: more time, richer field, slower swing, faster hook (was 50 s, ×1.6 value, 6 rocks, 1.8 / 520 / 560 / 620)
    const float TIME = 85f;
    const float FieldValue = 3f, SwingSpeed = 1.3f, DropSpeed = 700f, ReelBoost = 1.4f;
    static readonly int[] GOALS = { 3000 };
    const string BestKey = "yen-best-2r";

    [Tooltip("The 3D floor scene this game returns to.")]
    [SceneName] public string floorScene = "SecondFloorScene";

    /* kind: coin | bill | bundle | rock | purse */
    class Kind
    {
        public string kind, face, edge, txt, ink, color;
        public float r, w, h, wt;
        public int val;
        public bool hole, toy;
    }

    static readonly Dictionary<string, Kind> TYPES = new Dictionary<string, Kind>
    {
        ["c1"] = new Kind { kind = "coin", r = 14, val = 1, wt = 1.0f, face = "#dfe3e8", edge = "#9aa1aa", txt = "1", ink = "#5b6470" },
        ["c5"] = new Kind { kind = "coin", r = 15, val = 5, wt = 1.0f, face = "#d9b24a", edge = "#9c7a22", txt = "5", ink = "#6b4f12", hole = true },
        ["c10"] = new Kind { kind = "coin", r = 16, val = 10, wt = 1.1f, face = "#c27a48", edge = "#8a4d26", txt = "10", ink = "#5c3015" },
        ["c50"] = new Kind { kind = "coin", r = 16, val = 50, wt = 1.1f, face = "#cfd4da", edge = "#8d949c", txt = "50", ink = "#4d5560", hole = true },
        ["c100"] = new Kind { kind = "coin", r = 17, val = 100, wt = 1.2f, face = "#c8ced6", edge = "#858c96", txt = "100", ink = "#3f4752" },
        ["c500"] = new Kind { kind = "coin", r = 19, val = 500, wt = 1.3f, face = "#e0c46c", edge = "#a6872e", txt = "500", ink = "#5e4710" },
        ["b1k"] = new Kind { kind = "bill", r = 25, w = 70, h = 34, val = 1000, wt = 2.4f, color = "#9cc0d6", ink = "#24516b", txt = "1000" },
        ["b10k"] = new Kind { kind = "bill", r = 27, w = 76, h = 36, val = 10000, wt = 2.8f, color = "#d9c595", ink = "#5d4620", txt = "10000" },
        ["bundle"] = new Kind { kind = "bundle", r = 36, w = 78, h = 40, val = 30000, wt = 4.8f, color = "#d9c595", ink = "#5d4620" },
        ["rock"] = new Kind { kind = "rock", r = 34, val = 0, wt = 5.5f },
        ["toy"] = new Kind { kind = "bill", r = 30, w = 70, h = 34, val = 0, wt = 3.2f, color = "#a8d8a0", ink = "#2f5a2a", txt = L.T("子ども銀行", "TOY BANK"), toy = true },
        ["purse"] = new Kind { kind = "purse", r = 24, wt = 2.2f },
    };
    static readonly HashSet<string> JUNK = new HashSet<string> { "rock", "toy" };

    static readonly (string t, float x, float y)[] DEMO =
    {
        ("b1k", 170, 250), ("bundle", 570, 410), ("c500", 650, 240), ("c100", 300, 420), ("rock", 120, 430),
        ("purse", 450, 300), ("b10k", 360, 200), ("c5", 520, 200), ("c10", 700, 340)
    };

    class Item
    {
        public string t;
        public float x, y, r, rot, ph;
        public float hx, hy;   // where it lies in the ground; a released item sinks back there
        public RectTransform rt;
        public Image star1, star2;
    }

    class Hook { public float a, len = 30f; public int dir = 1; public string state = "swing"; public Item held; }
    class Pop { public float x, y, life; public Text text; }
    class Bit
    {
        public bool money;
        public float x, y, vx, vy, rot, vr, w, h, ph;
        public RectTransform rt;
    }

    // game state (script.js S)
    int goal, money;
    float time, jackpotT = -1f;
    bool running, paused, playing;   // playing: a round's field is on the stage (S.items)
    bool counting, won, leaving;     // counting: 3-2-1 before the clock runs; won: the yen is paid, the game is leaving
    int warnSec;                     // last whole second the timer showed, for the last-10-seconds beeps
    float overlayAt;                 // when the card appeared, so a held Space doesn't press it at once
    readonly List<Item> items = new List<Item>();
    readonly List<Pop> pops = new List<Pop>();
    readonly List<Bit> party = new List<Bit>();
    Hook hook;
    int round;
    float totalUsed, bestTime;

    // UI
    Transform canvasRoot;
    RectTransform stage, itemsLayer, heldLayer, popLayer, partyLayer, hookLine, claw, wheel;
    Image clawImg, jackpotDim, jackpotFlash;
    RectTransform jackpotRays, jackpotText, jackpotLayer;
    Text scoreVal, goalVal, timeVal, bestVal, goalSign, resetText;
    RectTransform progFill;
    Image resetBg;
    Button pauseBtn, resetBtn;
    Text pauseText;
    UiPointerState stagePointer;
    bool stageWasPressed;

    GameObject overlay, legend, winGlow;
    Image cardBorder, cardFill, overlayDim;
    Text ovTitle, ovText, ovBtnText;
    Shadow ovTitleShadow;
    LayoutElement ovBtnLayout;
    Action ovAction;
    Coroutine titlePop, resetTimer;

    AudioSource audioSource;
    AudioClip sfxFanfare, sfxJackpot;

    static readonly CultureInfo Ja = CultureInfo.InvariantCulture;
    static string Yen(float n) => "¥" + Mathf.Floor(n).ToString("#,0", Ja);

    /* ---------- setup ---------- */
    void Awake()
    {
        // the floor scene locks the cursor for mouse look; this game needs it
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        audioSource = GetComponent<AudioSource>();
        audioSource.playOnAwake = false;
        sfxFanfare = Fanfare();
        sfxJackpot = JackpotSound();
        bestTime = PlayerPrefs.GetFloat(BestKey, 0f);

        UiKit.EnsureEventSystem();
        BuildUi();

        NewGame();
        Hud();
        ShowBest();
        ShowDemo();
        SetOverlay(L.T("2階ゲーム", "2F Game"), L.T($"↓かタップでフックを落として、お金をつかもう！\n{(int)TIME}秒以内に ¥3,000 集めたらクリア！", $"Press ↓ or tap to drop the hook and grab money!\nCollect ¥3,000 within {(int)TIME} seconds to clear!"),
            L.T("スタート！", "Start!"), StartGame, true, false);
    }

    void BuildUi()
    {
        var canvas = UiKit.MakeCanvas("SecondFloorGameCanvas", 0, new Vector2(880f, 760f));
        var root = (RectTransform)canvas.transform;
        canvasRoot = root;
        UiKit.Stretch(UiKit.Img("Background", root, null, UiKit.Hex("#1d1026")).rectTransform);

        var wrap = UiKit.Rect("Wrap", root);
        UiKit.Center(wrap, W, 716f);

        // header
        var h1 = UiKit.Label(wrap, L.T("2階<color=#f4e6c4>ゲーム</color>", "2F <color=#f4e6c4>Game</color>"), 30, UiKit.Hex("#f2c14e"), TextAnchor.MiddleLeft);
        UiKit.Place(h1.rectTransform, 0f, 0f, 400f, 36f);

        // HUD
        const float gap = 6f, statH = 76f, hudY = 44f;
        float sw = (W - 3f * gap) / 4f;
        var money = Stat(wrap, 0f, hudY, sw, statH, L.T("所持金", "MONEY"), out scoreVal);
        var barBg = UiKit.Box("Bar", money, UiKit.Hex("#ffffff", 0.08f), 3f);
        UiKit.Place(barBg.rectTransform, 10f, 64f, sw - 20f, 6f);
        progFill = UiKit.Stretch(UiKit.Box("Fill", barBg.transform, UiKit.Hex("#7fd3b5"), 3f).rectTransform);
        progFill.pivot = new Vector2(0f, 0.5f);
        Stat(wrap, sw + gap, hudY, sw, statH, L.T("目標金額", "GOAL"), out goalVal);
        goalVal.color = UiKit.Hex("#7fd3b5");
        Stat(wrap, 2f * (sw + gap), hudY, sw, statH, L.T("残り時間", "TIME LEFT"), out timeVal);
        var best = Stat(wrap, 3f * (sw + gap), hudY, sw, statH, L.T("ベストタイム", "BEST TIME"), out bestVal);
        bestVal.fontSize = 20;
        resetBg = UiKit.Box("Reset", best, UiKit.Hex("#ffffff", 0.08f), 6f);
        resetBg.raycastTarget = true;
        UiKit.Place(resetBg.rectTransform, sw - 122f, 34f, 112f, 32f);
        resetBtn = resetBg.gameObject.AddComponent<Button>();
        resetBtn.transition = Selectable.Transition.None;
        resetBtn.targetGraphic = resetBg;
        resetBtn.onClick.AddListener(ResetBest);
        resetText = UiKit.Label(resetBg.transform, L.T("リセット", "Reset"), 18, UiKit.Hex("#b9a3bd"));
        resetText.resizeTextForBestFit = true;   // 「もう一度押す」 / "Press again" just fit
        resetText.resizeTextMinSize = 14;
        resetText.resizeTextMaxSize = 18;
        UiKit.Stretch(resetText.rectTransform, 2f);

        // stage
        const float stageY = hudY + statH + 10f;
        UiKit.Place(UiKit.Box("StageRing", wrap, UiKit.Hex("#ffffff", 0.08f), 12f).rectTransform, -3f, stageY - 3f, W + 6f, H + 6f);
        stage = UiKit.Rect("Stage", wrap);
        UiKit.Place(stage, 0f, stageY, W, H);
        stage.gameObject.AddComponent<RectMask2D>();
        BuildStage();

        // controls
        float cy = stageY + H + 12f;
        var hint = UiKit.Label(wrap, L.T("発射：↓ / Space / タップ　はなす：↑\n一時停止：P　リスタート：R",
            "Drop: ↓ / Space / tap   Let go: ↑\nPause: P   Restart: R"), 18,
            UiKit.Hex("#b9a3bd"), TextAnchor.MiddleLeft, false);
        hint.lineSpacing = 1.05f;
        Color mint = UiKit.Hex("#7fd3b5"), mintInk = UiKit.Hex("#123a2e"), mintShadow = UiKit.Hex("#3f8f74");
        var fire = UiKit.ColorButton(wrap, L.T("発射", "Drop"), UiKit.Hex("#f2c14e"), UiKit.Hex("#3a1d10"), UiKit.Hex("#a9771f"), 44f, 18, out var fireText);
        pauseBtn = UiKit.ColorButton(wrap, L.T("一時停止", "Pause"), mint, mintInk, mintShadow, 44f, 18, out pauseText);
        var restart = UiKit.ColorButton(wrap, L.T("リスタート", "Restart"), mint, mintInk, mintShadow, 44f, 18, out var restartText);
        float x = W;
        foreach (var (b, t) in new[] { (fire, fireText), (pauseBtn, pauseText), (restart, restartText) })
        {
            float bw = t.preferredWidth + 52f;
            x -= bw;
            UiKit.Place((RectTransform)b.transform, x, cy + 2f, bw, 44f);
            x -= 10f;
        }
        UiKit.Place(hint.rectTransform, 0f, cy, x - 4f, 48f);   // the room left of the buttons
        fire.onClick.AddListener(() => { if (!running) { if (overlay.activeSelf) ovAction?.Invoke(); } else Fire(); });
        pauseBtn.onClick.AddListener(TogglePause);
        pauseBtn.interactable = false;
        restart.onClick.AddListener(RestartGame);

        // back to the 3D floor; above everything so it always works
        var back = UiKit.SubButton(root, L.T("← 戻る", "← Back"), out var backText);
        UiKit.Place((RectTransform)back.transform, 16f, 12f, backText.preferredWidth + 60f, 46f);
        back.onClick.AddListener(Leave);
    }

    RectTransform Stat(RectTransform parent, float x, float y, float w, float h, string label, out Text val)
    {
        var box = UiKit.Box("Stat", parent, UiKit.Hex("#ffffff", 0.05f), 6f, UiKit.Hex("#ffffff", 0.1f), 1f).rectTransform;
        UiKit.Place(box, x, y, w, h);
        var l = UiKit.Label(box, label, 18, UiKit.Hex("#b9a3bd"), TextAnchor.MiddleLeft);
        l.horizontalOverflow = HorizontalWrapMode.Overflow;
        UiKit.Place(l.rectTransform, 10f, 5f, w - 20f, 24f);
        val = UiKit.Label(box, "", 24, UiKit.Hex("#f4e6c4"), TextAnchor.MiddleLeft);
        val.horizontalOverflow = HorizontalWrapMode.Overflow;
        UiKit.Place(val.rectTransform, 10f, 30f, w - 20f, 32f);
        return box;
    }

    void BuildStage()
    {
        // background (sky, dirt, awning, ground strip) baked once into a texture
        var bg = UiKit.Img("Ground", stage, BackgroundSprite(), Color.white);
        UiKit.Stretch(bg.rectTransform);
        bg.raycastTarget = true;   // clicking the stage fires the hook
        stagePointer = bg.gameObject.AddComponent<UiPointerState>();

        Sign(30f, 56f, 170f, "#f2c14e", L.T("お金を掘ろう", "Dig for money"), 18);
        goalSign = Sign(W - 210f, 56f, 180f, "#7fd3b5", "", 18);

        itemsLayer = Layer("Items");

        // machine: box, spinning wheel, pivot
        var box = UiKit.Box("Machine", stage, UiKit.Hex("#2b1832"), 8f, UiKit.Hex("#f2c14e"), 2f);
        UiKit.Place(box.rectTransform, PX - 36f, PY - 46f, 72f, 40f);
        wheel = Piece(stage, "Wheel", WheelSprite, Color.white, 0f, 0f, 28f, 28f).rectTransform;
        At(wheel, PX, PY - 26f);
        At(Piece(stage, "Pivot", UiKit.Circle, UiKit.Hex("#f2c14e"), 0f, 0f, 10f, 10f).rectTransform, PX, PY);

        // hook: rope pointing down from the pivot, claw at its tip
        hookLine = UiKit.Img("Rope", stage, null, UiKit.Hex("#f4e6c4")).rectTransform;
        hookLine.anchorMin = hookLine.anchorMax = new Vector2(0f, 1f);
        hookLine.pivot = new Vector2(0.5f, 1f);
        hookLine.anchoredPosition = new Vector2(PX, -PY);
        clawImg = Piece(stage, "Claw", ClawSprite(0.7f), Color.white, 0f, 0f, 48f, 48f);
        claw = clawImg.rectTransform;

        heldLayer = Layer("Held");
        popLayer = Layer("Pops");

        // jackpot: dim, turning rays, white flash, JACKPOT! ¥10,000
        jackpotLayer = Layer("Jackpot");
        jackpotDim = UiKit.Img("Dim", jackpotLayer, null, Color.clear);
        UiKit.Stretch(jackpotDim.rectTransform);
        jackpotRays = Piece(jackpotLayer, "Rays", RaysSprite, Color.white, 0f, 0f, 1800f, 1800f).rectTransform;
        At(jackpotRays, W / 2f, 250f);
        jackpotFlash = UiKit.Img("Flash", jackpotLayer, null, Color.clear);
        UiKit.Stretch(jackpotFlash.rectTransform);
        jackpotText = UiKit.Rect("Text", jackpotLayer);
        At(jackpotText, W / 2f, 235f);
        var jp = Txt(jackpotText, "JACKPOT!", 0f, 0f, 92, UiKit.Hex("#f2c14e"));
        Outline(jp, UiKit.Hex("#a9771f"), 5f);
        var jy = Txt(jackpotText, "¥10,000", 0f, 86f, 46, UiKit.Hex("#f4e6c4"));
        Outline(jy, UiKit.Hex("#2b1832"), 4f);
        jackpotLayer.gameObject.SetActive(false);

        partyLayer = Layer("Party");
        BuildOverlay();
    }

    RectTransform Layer(string name) => UiKit.Stretch(UiKit.Rect(name, stage));

    Text Sign(float x, float y, float w, string color, string text, int size)
    {
        var box = UiKit.Box("Sign", stage, UiKit.Hex("#2b1832"), 6f, UiKit.Hex(color), 2f);
        UiKit.Place(box.rectTransform, x, y, w, 40f);
        var t = UiKit.Label(box.transform, text, size, UiKit.Hex(color));
        UiKit.Stretch(t.rectTransform);
        return t;
    }

    static void Outline(Text t, Color color, float width)
    {
        var o = t.gameObject.AddComponent<Outline>();
        o.effectColor = color;
        o.effectDistance = new Vector2(width, -width);
    }

    void BuildOverlay()
    {
        overlayDim = UiKit.Img("Overlay", stage, null, UiKit.Hex("#140a1b", 0.94f));
        overlayDim.raycastTarget = true;   // blocks clicks to the stage underneath
        UiKit.Stretch(overlayDim.rectTransform);
        overlay = overlayDim.gameObject;

        winGlow = UiKit.Img("WinGlow", overlay.transform, UiKit.Glow, UiKit.Hex("#5a2f52", 0.8f)).gameObject;
        UiKit.Center((RectTransform)winGlow.transform, 900f, 640f);

        cardBorder = UiKit.Box("Card", overlay.transform, UiKit.Hex("#2b1832"), 16f, UiKit.Hex("#ffffff", 0.12f), 2f);
        cardFill = cardBorder.transform.GetChild(0).GetComponent<Image>();
        var card = cardBorder.rectTransform;
        card.anchorMin = card.anchorMax = card.pivot = new Vector2(0.5f, 0.5f);
        var v = card.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(24, 24, 20, 20);
        v.spacing = 12f;
        v.childAlignment = TextAnchor.UpperCenter;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = v.childForceExpandHeight = false;
        var fit = card.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ovTitle = UiKit.Label(card, "", 34, UiKit.Hex("#f2c14e"));
        ovTitle.horizontalOverflow = HorizontalWrapMode.Overflow;
        ovTitleShadow = UiKit.HardShadow(ovTitle, UiKit.Hex("#a9771f"), 3f);

        ovText = UiKit.Label(card, "", 20, UiKit.Hex("#f4e6c4"), TextAnchor.UpperCenter, false);
        ovText.lineSpacing = 1.2f;
        ovText.gameObject.AddComponent<LayoutElement>().preferredWidth = 520f;

        var lg = UiKit.Rect("Legend", card);
        var lv = lg.gameObject.AddComponent<VerticalLayoutGroup>();
        lv.spacing = 4f;
        lv.childControlWidth = lv.childControlHeight = true;
        lv.childForceExpandWidth = lv.childForceExpandHeight = false;
        foreach (var (what, how) in new[]
                 {
                     (L.T("硬貨（1〜500円）", "Coins (¥1–500)"), L.T("軽くてすぐ戻る", "Light, quick to reel in")), (L.T("千円札", "¥1,000 note"), L.T("そこそこ軽い", "Fairly light")), (L.T("一万円札", "¥10,000 note"), L.T("深いところに… ジャックポット！", "Deep down… JACKPOT!")),
                     (L.T("石・子ども銀行券", "Rocks, toy notes"), L.T("重くて価値なし", "Heavy and worthless")), (L.T("がま口", "Coin purse"), L.T("中身はお楽しみ", "What's inside? Surprise!")),
                     (L.T("↑ キー", "↑ key"), L.T("お金ははなして地面に戻す、ガラクタは捨てる", "Lets money drop back; throws junk away"))
                 })
        {
            var row = UiKit.Rect("Row", lg);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 14f;
            h.childAlignment = TextAnchor.UpperLeft;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            LegendCell(row, what, UiKit.Hex("#f4e6c4"), 190f);
            LegendCell(row, how, UiKit.Hex("#b9a3bd"), 316f);
        }
        legend = lg.gameObject;

        var btn = UiKit.ColorButton(card, "", UiKit.Hex("#f2c14e"), UiKit.Hex("#3a1d10"), UiKit.Hex("#a9771f"), 46f, 18, out ovBtnText);
        ovBtnLayout = btn.GetComponent<LayoutElement>();
        btn.onClick.AddListener(() => ovAction?.Invoke());
    }

    // fixed width, wraps onto a second line rather than clipping (the row takes the taller cell's height)
    static void LegendCell(RectTransform row, string text, Color color, float width)
    {
        var t = UiKit.Label(row, text, 18, color, TextAnchor.UpperLeft, false);
        var le = t.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = le.minWidth = width;
        le.minHeight = 24f;
    }

    void SetOverlay(string title, string text, string button, Action action, bool showLegend, bool win)
    {
        ovTitle.text = title;
        ovText.text = text;
        ovBtnText.text = button;
        ovBtnLayout.preferredWidth = ovBtnText.preferredWidth + 52f;
        ovAction = action;
        legend.SetActive(showLegend);

        // .overlay.win: warm glow, gold-bordered card, bigger popping title
        overlayDim.color = win ? UiKit.Hex("#1d1026", 0.6f) : UiKit.Hex("#140a1b", 0.94f);
        winGlow.SetActive(win);
        cardBorder.color = win ? UiKit.Hex("#f2c14e") : UiKit.Hex("#ffffff", 0.12f);
        cardFill.color = win ? UiKit.Hex("#1d1026", 0.9f) : UiKit.Hex("#2b1832");
        ovTitle.fontSize = win ? 42 : 34;
        ovTitleShadow.enabled = win;
        overlay.SetActive(true);
        overlayAt = Time.unscaledTime;
        if (titlePop != null) StopCoroutine(titlePop);
        ovTitle.rectTransform.localScale = Vector3.one;
        if (win) titlePop = StartCoroutine(PopTitle());
    }

    // @keyframes pop: scale .4 → 1 with an overshoot, .6s
    IEnumerator PopTitle()
    {
        var group = ovTitle.GetComponent<CanvasGroup>();
        if (group == null) group = ovTitle.gameObject.AddComponent<CanvasGroup>();
        for (float t = 0f; t < 0.6f; t += Time.unscaledDeltaTime)
        {
            float p = t / 0.6f, q = p - 1f;
            float e = 1f + 2.70158f * q * q * q + 1.70158f * q * q;   // ease-out-back
            ovTitle.rectTransform.localScale = Vector3.one * Mathf.LerpUnclamped(0.4f, 1f, e);
            group.alpha = Mathf.Clamp01(p * 2f);
            yield return null;
        }
        ovTitle.rectTransform.localScale = Vector3.one;
        group.alpha = 1f;
    }

    /* ---------- game ---------- */
    void NewGame()
    {
        goal = GOALS[round];
        money = 0;
        time = TIME;
        warnSec = int.MaxValue;
        running = paused = false;
        jackpotT = -1f;
    }

    // Build the field so the valuables on it are always worth well over the goal.
    List<Item> Layout(int g)
    {
        var result = new List<Item>();
        var pool = new List<string>();
        void Add(string t, int n) { for (int i = 0; i < n; i++) pool.Add(t); }
        Add("c1", 3); Add("c5", 2); Add("c10", 3); Add("c50", 2); Add("c100", 2); Add("c500", 2);
        Add("b1k", 4); Add("b10k", 1); Add("bundle", g >= 15000 ? 1 : 0);
        Add("rock", 3); Add("toy", 1); Add("purse", 2);
        int Sum() => pool.Sum(t => TYPES[t].val);
        while (Sum() < g * FieldValue)
        {
            if (g - Sum() > 40000 && pool.Count(t => t == "bundle") < 4) pool.Add("bundle");
            else pool.Add("b1k");
        }
        pool = pool.OrderByDescending(t => TYPES[t].r).ToList();   // place big things first so they fit
        foreach (var t in pool)
        {
            var T = TYPES[t];
            bool deep = t == "b10k" || t == "bundle";
            for (int k = 0; k < 400; k++)
            {
                float minY = deep ? 350f : 160f;
                float x = 24f + T.r + Rand() * (W - 48f - 2f * T.r);
                if (t == "b10k") x = 170f + Rand() * 460f;
                float y = minY + Rand() * (H - 14f - T.r - minY);
                float pad = k < 300 ? 8f : 2f;
                if (result.All(o => Vector2.Distance(new Vector2(o.x, o.y), new Vector2(x, y)) > o.r + T.r + pad))
                {
                    result.Add(new Item
                    {
                        t = t, x = x, y = y, hx = x, hy = y, r = T.r,
                        rot = (Rand() - 0.5f) * (T.kind == "bill" ? 0.6f : 0.3f), ph = Rand() * 6f
                    });
                    break;
                }
            }
        }
        return result;
    }

    static float Rand() => UnityEngine.Random.value;

    void StartGame()
    {
        round = 0;
        totalUsed = 0f;
        BeginRound();
    }

    void BeginRound()
    {
        NewGame();
        ClearField();
        foreach (var o in Layout(goal)) { ShowItem(o, itemsLayer); items.Add(o); }
        hook = new Hook();
        playing = true;
        ClearParty();
        pauseText.text = L.T("一時停止", "Pause");
        overlay.SetActive(false);
        Hud();
        // the field shows during 3-2-1; the clock and the hook start on スタート！
        counting = true;
        MinigameKit.Countdown(this, canvasRoot, () =>
        {
            counting = false;
            running = true;
            Hud();
        });
    }

    void Hud()
    {
        scoreVal.text = Yen(money);
        goalVal.text = Yen(goal);
        goalSign.text = L.T("目標 ", "GOAL ") + Yen(goal);
        progFill.localScale = new Vector3(Mathf.Min(1f, (float)money / goal), 1f, 1f);
        timeVal.text = Mathf.CeilToInt(time).ToString(Ja);
        timeVal.color = time <= 10f ? UiKit.Hex("#d94a3d") : UiKit.Hex("#f4e6c4");
        pauseBtn.interactable = running;
    }

    void Fire()
    {
        if (running && !paused && hook.state == "swing") hook.state = "out";
    }

    // はなす: money the hook holds drops back into the ground, junk is thrown away; with nothing held, the hook
    // is pulled back right away if it is still going down
    void Cut()
    {
        if (!running || paused || jackpotT >= 0f) return;
        if (hook.held != null)
        {
            var o = hook.held;
            hook.held = null;
            if (JUNK.Contains(o.t))
            {
                Destroy(o.rt.gameObject);
                PopText(L.T("ポイ！", "Tossed!"), UiKit.Hex("#8d8292"));
                audioSource.PlayOneShot(Sfx.Whoosh());
            }
            else
            {
                o.rt.SetParent(itemsLayer, false);   // sinks back to (hx, hy) in Step
                items.Add(o);
                audioSource.PlayOneShot(Sfx.Pop());
            }
            Hud();
        }
        else if (hook.state == "out") hook.state = "back";
    }

    Vector2 Tip() => new Vector2(PX + Mathf.Sin(hook.a) * hook.len, PY + Mathf.Cos(hook.a) * hook.len);

    void StartJackpot(Item o)
    {
        hook.state = "jackpot";
        var p = Tip();
        o.x = p.x;
        o.y = p.y;
        jackpotT = 0f;
        Collect(o, false);
        ClearParty();
        Celebrate(true);
        audioSource.PlayOneShot(sfxJackpot);
        Hud();
    }

    void Step(float dt)
    {
        var h = hook;
        if (jackpotT >= 0f)
        {
            jackpotT += dt;
            StepPops(dt);
            if (jackpotT >= 1.9f)
            {
                DropHeld();
                Hud();
                EndGame(true);
            }
            return;
        }
        time -= dt;
        // a beep on each of the last 10 seconds (a purse's +10 s lifts the mark again)
        int sec = Mathf.CeilToInt(time);
        if (sec < warnSec)
        {
            warnSec = sec;
            if (sec >= 1 && sec <= 10) audioSource.PlayOneShot(Sfx.Warn());
        }
        else if (sec > warnSec) warnSec = sec;
        // released items sink back to where they lay
        foreach (var o in items)
            if (o.x != o.hx || o.y != o.hy)
            {
                var p = Vector2.MoveTowards(new Vector2(o.x, o.y), new Vector2(o.hx, o.hy), 700f * dt);
                o.x = p.x;
                o.y = p.y;
            }
        if (h.state == "swing")
        {
            h.a += h.dir * dt * SwingSpeed;
            if (h.a > 1.25f) { h.a = 1.25f; h.dir = -1; }
            if (h.a < -1.25f) { h.a = -1.25f; h.dir = 1; }
        }
        else if (h.state == "out")
        {
            h.len += DropSpeed * dt;
            var p = Tip();
            if (p.x < 0f || p.x > W || p.y > H) h.state = "back";
            for (int i = 0; i < items.Count; i++)
            {
                var o = items[i];
                if (Vector2.Distance(new Vector2(o.x, o.y), p) < o.r)
                {
                    h.held = o;
                    items.RemoveAt(i);
                    o.rt.SetParent(heldLayer, false);
                    h.state = "back";
                    if (o.t == "b10k") StartJackpot(o);
                    else audioSource.PlayOneShot(JUNK.Contains(o.t) ? Sfx.Thud() : Sfx.Pop());
                    break;
                }
            }
        }
        else if (h.state == "back")
        {
            float sp = (h.held != null ? 560f / TYPES[h.held.t].wt : 620f) * ReelBoost;
            h.len -= sp * dt;
            if (h.held != null)
            {
                var p = Tip();
                h.held.x = p.x + Mathf.Sin(h.a) * h.held.r * 0.8f;
                h.held.y = p.y + Mathf.Cos(h.a) * h.held.r * 0.8f;
            }
            if (h.len <= 30f)
            {
                h.len = 30f;
                h.state = "swing";
                if (h.held != null)
                {
                    Collect(h.held);
                    DropHeld();
                    if (money >= goal) { Hud(); EndGame(true); return; }
                }
            }
        }
        StepPops(dt);
        bool left = items.Any(o => !JUNK.Contains(o.t));
        if (time <= 0f || (!left && h.state == "swing" && h.held == null))
        {
            time = Mathf.Max(0f, time);
            Hud();
            EndGame(false);
            return;
        }
        Hud();
    }

    void DropHeld()
    {
        if (hook.held == null) return;
        Destroy(hook.held.rt.gameObject);
        hook.held = null;
    }

    void StepPops(float dt)
    {
        for (int i = pops.Count - 1; i >= 0; i--)
        {
            var p = pops[i];
            p.life -= dt * 0.8f;
            p.y -= 30f * dt;
            if (p.life <= 0f) { Destroy(p.text.gameObject); pops.RemoveAt(i); }
        }
    }

    // banks what the hook brought up; `sound` off for the jackpot, which has its own
    void Collect(Item o, bool sound = true)
    {
        int v;
        string txt = null;
        Color c = UiKit.Hex("#f2c14e");
        if (o.t == "purse")
        {
            if (Rand() < 0.2f) { time += 10f; txt = L.T("時間 +10秒", "Time +10s"); c = UiKit.Hex("#9fb4f2"); v = 0; }
            else v = new[] { 200, 300, 500, 1000, 2000 }[UnityEngine.Random.Range(0, 5)];
        }
        else v = TYPES[o.t].val;
        if (v > 0)
        {
            money += v;
            txt = "+" + Yen(v);
            if (v < 100) c = UiKit.Hex("#dfe3e8");
            if (sound) audioSource.PlayOneShot(Sfx.Coin());
        }
        else if (txt == null) { txt = "¥0"; c = UiKit.Hex("#8d8292"); }
        else if (sound) audioSource.PlayOneShot(Sfx.Good());   // the purse's extra time
        PopText(txt, c);
    }

    // a floating label by the machine that rises and fades (StepPops)
    void PopText(string txt, Color c)
    {
        var label = UiKit.Label(popLayer, txt, 26, c, TextAnchor.MiddleLeft);
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0f, 1f);
        label.rectTransform.pivot = new Vector2(0f, 0.5f);
        label.rectTransform.sizeDelta = new Vector2(10f, 30f);
        pops.Add(new Pop { x = PX + 60f, y = PY - 18f, life = 1f, text = label });
    }

    void EndGame(bool ok)
    {
        running = false;
        paused = false;
        pauseBtn.interactable = false;
        pauseText.text = L.T("一時停止", "Pause");
        if (ok)
        {
            if (won) return;
            float used = Mathf.Max(0f, TIME - time);
            totalUsed += used;
            if (round < GOALS.Length - 1)
            {
                SetOverlay(L.T("ラウンド" + (round + 1) + " クリア！", "Round " + (round + 1) + " Clear!"),
                    L.T(used.ToString("0.0", Ja) + " 秒で " + Yen(money) + " 集めました。次のラウンド" + (round + 2) +
                        "の目標は " + Yen(GOALS[round + 1]) + " です。",
                        "You collected " + Yen(money) + " in " + used.ToString("0.0", Ja) + "s. The goal for round " + (round + 2) +
                        " is " + Yen(GOALS[round + 1]) + "."),
                    L.T("ラウンド" + (round + 2) + "へ", "To round " + (round + 2)), () => { round++; BeginRound(); }, false, false);
            }
            else
            {
                won = true;   // no restart from here: the yen below is paid exactly once
                GameProgress.Yen += money;   // the vending machine on the floor sells the ticket for this
                bool isBest = bestTime <= 0f || totalUsed < bestTime;
                if (isBest)
                {
                    bestTime = totalUsed;
                    PlayerPrefs.SetFloat(BestKey, totalUsed);
                }
                ShowBest();
                SetOverlay(L.T("おめでとうございます！", "Congratulations!"),
                    L.T("目標の " + Yen(goal) + " を達成しました！ " + totalUsed.ToString("0.0", Ja) + " 秒で " + Yen(money) +
                        " 集めました。" + (isBest ? "ベストタイム更新！" : "") +
                        "\nフロアに戻って、エレベーター近くの自動販売機で３階へのチケットを買おう！",
                        "You reached the goal of " + Yen(goal) + "! You collected " + Yen(money) + " in " +
                        totalUsed.ToString("0.0", Ja) + "s." + (isBest ? " New best time!" : "") +
                        "\nGo back to the floor and buy a ticket to 3F at the vending machine by the elevator!"),
                    L.T("フロアに戻る", "Back to the floor"), Leave, false, true);
                Celebrate(false);
                MinigameKit.ResultThenExit(this, canvasRoot, Leave);   // the game ends by itself
            }
        }
        else
        {
            audioSource.PlayOneShot(Sfx.Bad());
            SetOverlay(time <= 0f ? L.T("タイムアップ", "Time's Up") : L.T("お金が尽きました", "No Money Left"),
                L.T("目標 " + Yen(goal) + " まであと " + Yen(goal - money) + "でした。もう一回挑戦しよう！",
                    "You were " + Yen(goal - money) + " short of the " + Yen(goal) + " goal. Try again!"),
                L.T("もう一回", "Try Again"), StartGame, false, false);
        }
    }

    /* ---------- pause ---------- */
    void PauseGame()
    {
        if (!running || paused) return;
        paused = true;
        pauseText.text = L.T("再開", "Resume");
        SetOverlay(L.T("一時停止中", "Paused"), L.T("ゲームは止まっています。再開を押すと続きから始まります。", "The game is paused. Press Resume to carry on."), L.T("再開", "Resume"), ResumeGame, true, false);
    }

    void ResumeGame()
    {
        if (!running || !paused) return;
        paused = false;
        pauseText.text = L.T("一時停止", "Pause");
        overlay.SetActive(false);
    }

    void TogglePause()
    {
        if (!running) return;
        if (paused) ResumeGame(); else PauseGame();
    }

    // not during 3-2-1 (its own スタート！ would follow) nor once won (the game is leaving with the yen)
    void RestartGame()
    {
        if (counting || won) return;
        ClearParty();
        StartGame();
    }

    void OnApplicationFocus(bool focus)
    {
        if (!focus) PauseGame();
    }

    void ShowBest() => bestVal.text = bestTime > 0f ? bestTime.ToString("0.0", Ja) + L.T("秒", "s") : "—";

    // first press arms it (もう一度押す), a second press within 3 seconds clears the best time
    void ResetBest()
    {
        if (resetTimer == null)
        {
            resetTimer = StartCoroutine(ResetArmed());
            return;
        }
        StopCoroutine(resetTimer);
        resetTimer = null;
        bestTime = 0f;
        PlayerPrefs.DeleteKey(BestKey);
        ShowBest();
        ResetLook(false);
    }

    IEnumerator ResetArmed()
    {
        ResetLook(true);
        yield return new WaitForSecondsRealtime(3f);
        ResetLook(false);
        resetTimer = null;
    }

    void ResetLook(bool sure)
    {
        resetText.text = sure ? L.T("もう一度押す", "Press again") : L.T("リセット", "Reset");
        resetText.color = sure ? UiKit.Hex("#f4e6c4") : UiKit.Hex("#b9a3bd");
        resetBg.color = sure ? UiKit.Hex("#d94a3d") : UiKit.Hex("#ffffff", 0.08f);
    }

    // once: the win card's button and the result hold can both ask
    void Leave()
    {
        if (leaving) return;
        if (!Application.CanStreamedLevelBeLoaded(floorScene))
        {
            Debug.LogError($"SecondFloorGame: '{floorScene}' is not in Build Settings. Run Tools > Elevator Game > Create Second Floor Game Scene.");
            return;
        }
        leaving = true;
        SceneManager.LoadScene(floorScene);
    }

    /* ---------- loop ---------- */
    void Update()
    {
        // like script.js's blur(): a clicked button must not stay selected, or Space would press it again
        var es = UnityEngine.EventSystems.EventSystem.current;
        if (es != null && es.currentSelectedGameObject != null) es.SetSelectedGameObject(null);

        var kb = Keyboard.current;
        if (kb != null)
        {
            if (running)
            {
                if (kb.pKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame) TogglePause();
                else if (!paused)
                {
                    if (kb.downArrowKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) Fire();
                    if (kb.upArrowKey.wasPressedThisFrame) Cut();
                }
            }
            if (kb.rKey.wasPressedThisFrame) RestartGame();
            // the card's button by keyboard too (the win card leaves through the result hold instead)
            else if ((!running || paused) && !counting && !won && overlay.activeSelf && Time.unscaledTime - overlayAt > 0.4f &&
                     (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
                ovAction?.Invoke();
        }

        bool pressed = stagePointer.pressed;
        if (pressed && !stageWasPressed && running) Fire();
        stageWasPressed = pressed;

        float dt = Mathf.Min(0.05f, Time.deltaTime);
        if (running && !paused) Step(dt);
        if (party.Count > 0) StepParty(dt);
        Render(Time.time);
    }

    void Render(float t)
    {
        foreach (var o in items) RenderItem(o, t);

        bool showHook = playing && hook != null;
        hookLine.gameObject.SetActive(showHook);
        claw.gameObject.SetActive(showHook);
        wheel.localEulerAngles = new Vector3(0f, 0f, -(showHook ? hook.len * 0.05f : 0f) * Mathf.Rad2Deg);
        if (showHook)
        {
            float deg = hook.a * Mathf.Rad2Deg;
            hookLine.sizeDelta = new Vector2(2f, hook.len);
            hookLine.localEulerAngles = new Vector3(0f, 0f, deg);
            var tip = Tip();
            At(claw, tip.x, tip.y);
            claw.localEulerAngles = new Vector3(0f, 0f, deg);
            clawImg.sprite = ClawSprite(hook.held != null ? 0.25f : 0.7f);
            if (hook.held != null) RenderItem(hook.held, t);
        }

        foreach (var p in pops)
        {
            At(p.text.rectTransform, p.x, p.y);
            var c = p.text.color;
            c.a = Mathf.Max(0f, p.life);
            p.text.color = c;
        }

        RenderJackpot();
        foreach (var p in party)
        {
            At(p.rt, p.x, p.y);
            p.rt.localEulerAngles = new Vector3(0f, 0f, -p.rot * Mathf.Rad2Deg);
            if (!p.money) p.rt.localScale = new Vector3(1f, Mathf.Cos(t * 6f + p.ph), 1f);
        }
    }

    void RenderItem(Item o, float t)
    {
        At(o.rt, o.x, o.y);
        o.rt.localEulerAngles = new Vector3(0f, 0f, -o.rot * Mathf.Rad2Deg);
        var T = TYPES[o.t];
        if (o.t == "b10k")
        {
            float s = (Mathf.Sin(t * 3f + o.ph) + 1f) / 2f;
            Twinkle(o.star1, T.w / 2f - 4f, -T.h / 2f + 2f, 3f + s * 4f, Color.white, 0.35f + s * 0.6f);
            Twinkle(o.star2, -T.w / 2f + 6f, T.h / 2f - 3f, 2f + (1f - s) * 3f, Color.white, 0.35f + s * 0.6f);
        }
        else if (o.t == "bundle")
        {
            float s = (Mathf.Sin(t * 2.4f + o.ph) + 1f) / 2f;
            Twinkle(o.star1, T.w / 2f - 2f, -T.h / 2f, 3f + s * 4f, UiKit.Hex("#fff0b4"), 0.3f + s * 0.6f);
        }
    }

    static void Twinkle(Image star, float x, float y, float r, Color c, float alpha)
    {
        if (star == null) return;
        star.rectTransform.anchoredPosition = new Vector2(x, -y);
        star.rectTransform.sizeDelta = new Vector2(2f * r, 2f * r);
        c.a = alpha;
        star.color = c;
    }

    void RenderJackpot()
    {
        bool on = playing && jackpotT >= 0f;
        jackpotLayer.gameObject.SetActive(on);
        if (!on) return;
        float jt = jackpotT, k = Mathf.Min(1f, jt / 0.25f);
        jackpotDim.color = UiKit.Hex("#140a1b", 0.6f * k);
        jackpotRays.localEulerAngles = new Vector3(0f, 0f, -jt * 0.8f * Mathf.Rad2Deg);
        jackpotFlash.color = new Color(1f, 1f, 1f, jt < 0.25f ? 0.8f * (1f - jt / 0.25f) : 0f);
        float pop = Mathf.Min(1f, jt / 0.3f), ease = 1f + Mathf.Sin(pop * Mathf.PI) * 0.25f * (1f - pop + 0.2f);
        float pulse = 1f + Mathf.Sin(jt * 14f) * 0.05f;
        jackpotText.localScale = Vector3.one * (pop * ease * pulse);
    }

    /* ---------- celebration: falling money + confetti, and a short fanfare ---------- */
    void Celebrate(bool silent)
    {
        ClearParty();
        string[] kinds = { "c500", "c100", "c10", "c5", "b1k", "b10k" };
        string[] cols = { "#f2c14e", "#d94a3d", "#7fd3b5", "#9fb4f2", "#f4e6c4", "#f2a0b5" };
        for (int i = 0; i < 110; i++)
        {
            var p = new Bit
            {
                money = i % 3 == 0,
                x = Rand() * W, y = -20f - Rand() * H * 1.2f, vy = 60f + Rand() * 120f, vx = (Rand() - 0.5f) * 40f,
                rot = Rand() * 6f, vr = (Rand() - 0.5f) * 4f, w = 6f + Rand() * 6f, h = 10f + Rand() * 8f, ph = Rand() * 6f
            };
            if (p.money)
            {
                var o = new Item { t = kinds[i % kinds.Length], ph = p.ph };
                ShowItem(o, partyLayer);
                p.rt = o.rt;
            }
            else
            {
                var img = UiKit.Img("Confetti", partyLayer, null, UiKit.Hex(cols[i % cols.Length]));
                p.rt = img.rectTransform;
                p.rt.anchorMin = p.rt.anchorMax = new Vector2(0f, 1f);
                p.rt.sizeDelta = new Vector2(p.w, p.h);
            }
            party.Add(p);
        }
        if (!silent) audioSource.PlayOneShot(sfxFanfare);
    }

    void StepParty(float dt)
    {
        foreach (var p in party)
        {
            p.y += p.vy * dt;
            p.x += p.vx * dt + Mathf.Sin(p.ph + p.y * 0.02f) * 20f * dt;
            p.rot += p.vr * dt;
            if (p.y > H + 40f) { p.y = -30f; p.x = Rand() * W; }
        }
    }

    void ClearParty()
    {
        foreach (var p in party) if (p.rt != null) Destroy(p.rt.gameObject);
        party.Clear();
    }

    void ClearField()
    {
        foreach (Transform c in itemsLayer) Destroy(c.gameObject);
        foreach (Transform c in heldLayer) Destroy(c.gameObject);
        foreach (var p in pops) Destroy(p.text.gameObject);
        items.Clear();
        pops.Clear();
    }

    // idle field behind the start screen
    void ShowDemo()
    {
        foreach (var (t, x, y) in DEMO)
        {
            var o = new Item { t = t, x = x, y = y, hx = x, hy = y, rot = ((int)x % 7 - 3) * 0.08f, ph = x };
            ShowItem(o, itemsLayer);
            items.Add(o);   // nothing plays until スタート, so these only twinkle
        }
    }

    /* ---------- drawing ---------- */
    // Canvas coordinates (x right, y down from the stage's top-left) → anchored position
    static void At(RectTransform rt, float x, float y)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
    }

    // A centred child image at (x, y), y measured downward like the canvas
    static Image Piece(Transform parent, string name, Sprite sprite, Color color, float x, float y, float w, float h)
    {
        var img = UiKit.Img(name, parent, sprite, color);
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
        return img;
    }

    static Image BoxAt(Transform parent, Color fill, float radius, Color? border, float bw, float x, float y, float w, float h)
    {
        var img = UiKit.Box("Box", parent, fill, radius, border, bw);
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
        return img;
    }

    static Text Txt(Transform parent, string s, float x, float y, int size, Color color, bool bold = true)
    {
        var t = UiKit.Label(parent, s, Mathf.Max(1, size), color, TextAnchor.MiddleCenter, bold);
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(10f, size * 1.4f);
        return t;
    }

    // Builds an item's look as a group of images under parent, placed at (o.x, o.y)
    void ShowItem(Item o, Transform parent)
    {
        var T = TYPES[o.t];
        o.r = T.r;
        o.rt = UiKit.Rect(o.t, parent);
        o.rt.pivot = new Vector2(0.5f, 0.5f);
        o.rt.sizeDelta = Vector2.zero;
        var root = o.rt;
        float r = T.r, w = T.w, h = T.h;
        var shadow = UiKit.Hex("#000000", 0.27f);

        switch (T.kind)
        {
            case "coin":
                Piece(root, "Shadow", UiKit.Circle, shadow, 2f, 3f, 2f * r, 2f * r);
                Piece(root, "Edge", UiKit.Circle, UiKit.Hex(T.edge), 0f, 0f, 2f * r, 2f * r);
                Piece(root, "Face", UiKit.Circle, UiKit.Hex(T.face), 0f, 0f, 2f * r - 5f, 2f * r - 5f);
                Piece(root, "Rim", UiKit.Ring, UiKit.Hex(T.edge), 0f, 0f, 2f * r - 9f, 2f * r - 9f);
                if (T.hole)
                {
                    Piece(root, "Hole", UiKit.Circle, UiKit.Hex("#2b1832"), 0f, 0f, r * 0.56f, r * 0.56f);
                    Txt(root, T.txt, 0f, r * 0.6f, Mathf.RoundToInt(r * 0.55f), UiKit.Hex(T.ink));
                }
                else Txt(root, T.txt, 0f, 1f, Mathf.RoundToInt(r * (T.txt.Length > 2 ? 0.62f : 0.85f)), UiKit.Hex(T.ink));
                Piece(root, "Shine", UiKit.Circle, UiKit.Hex("#ffffff", 0.33f), -r * 0.35f, -r * 0.4f, r * 0.56f, r * 0.28f)
                    .rectTransform.localEulerAngles = new Vector3(0f, 0f, 0.6f * Mathf.Rad2Deg);
                break;

            case "bill":
                BoxAt(root, UiKit.Hex("#000000", 0.25f), 3f, null, 0f, 2f, 3f, w, h);
                BillBody(root, T, w, h);
                if (T.toy)
                {
                    Txt(root, L.T("子ども銀行", "TOY BANK"), -w * 0.06f, -h * 0.12f, Mathf.RoundToInt(h * 0.24f), UiKit.Hex(T.ink));
                    Txt(root, L.T("ニセ札", "FAKE"), -w * 0.06f, h * 0.22f, Mathf.RoundToInt(h * 0.22f), UiKit.Hex(T.ink), false);
                }
                else
                {
                    Txt(root, T.txt, -w * 0.08f, -h * 0.06f, Mathf.RoundToInt(h * (T.txt.Length > 4 ? 0.34f : 0.4f)), UiKit.Hex(T.ink));
                    Txt(root, L.T("日本銀行券", "BANK OF JAPAN"), -w * 0.08f, h * 0.3f, Mathf.RoundToInt(h * 0.19f), UiKit.Hex(T.ink), false);
                }
                if (o.t == "b10k")
                {
                    o.star1 = Piece(root, "Star", StarSprite, Color.clear, 0f, 0f, 1f, 1f);
                    o.star2 = Piece(root, "Star", StarSprite, Color.clear, 0f, 0f, 1f, 1f);
                }
                break;

            case "bundle":
                BoxAt(root, shadow, 4f, null, 0f, 3f, 9f, w, h + 8f);
                for (int i = 4; i >= 1; i--)
                    BoxAt(root, UiKit.Hex(i % 2 == 1 ? "#cbb682" : "#e3d2a6"), 3f, UiKit.Hex("#5d4620", 0.38f), 1f, 0f, i * 2.2f, w, h);
                BillBody(root, T, w, h);
                Txt(root, "10000", -w * 0.08f, -h * 0.12f, Mathf.RoundToInt(h * 0.32f), UiKit.Hex(T.ink));
                BoxAt(root, UiKit.Hex("#f4e6c4"), 0f, UiKit.Hex("#d94a3d"), 1.5f, 0f, 5f, 14f, h + 12f);
                Txt(root, "¥30,000", 0f, 4f, 10, UiKit.Hex("#d94a3d"), false).rectTransform.localEulerAngles = new Vector3(0f, 0f, -90f);
                o.star1 = Piece(root, "Star", StarSprite, Color.clear, 0f, 0f, 1f, 1f);
                break;

            case "rock":
            {
                var sprite = RockSprite(Mathf.FloorToInt(o.ph * 10f));
                float s = 2f * r * RockExtent;
                Piece(root, "Shadow", sprite, shadow, 2f, 4f, s, s);
                Piece(root, "Rock", sprite, Color.white, 0f, 0f, s, s);
                break;
            }

            case "purse":
                Piece(root, "Shadow", UiKit.Circle, shadow, 2f, 8f, 2f * r, 1.6f * r);
                Piece(root, "Purse", PurseSprite, Color.white, 0f, 0f, 2f * r * PurseExtent, 2f * r * PurseExtent);
                Txt(root, "?", 0f, r * 0.42f, 18, UiKit.Hex("#f4e6c4"));
                break;
        }
        At(o.rt, o.x, o.y);
        o.rt.localEulerAngles = new Vector3(0f, 0f, -o.rot * Mathf.Rad2Deg);
    }

    // note body with its frame, portrait oval and watermark
    static void BillBody(Transform root, Kind T, float w, float h)
    {
        var ink = UiKit.Hex(T.ink);
        BoxAt(root, UiKit.Hex(T.color), 3f, UiKit.Hex(T.ink, 0.53f), 1f, 0f, 0f, w, h);
        BoxAt(root, UiKit.Hex(T.color), 2f, UiKit.Hex(T.ink, 0.33f), 1f, 0f, 0f, w - 6f, h - 6f);
        Piece(root, "Portrait", UiKit.Circle, new Color(ink.r, ink.g, ink.b, 0.25f), w * 0.24f, 0f, h * 0.52f, h * 0.64f);
        Piece(root, "Watermark", UiKit.Ring, new Color(ink.r, ink.g, ink.b, 0.31f), -w * 0.3f, 0f, h * 0.44f, h * 0.44f);
    }

    /* ---------- generated sprites ---------- */
    const float RockExtent = 1.05f, PurseExtent = 1.15f;
    static Sprite star, purse, wheelSprite, rays, background;
    static readonly Dictionary<int, Sprite> rocks = new Dictionary<int, Sprite>();
    static readonly Dictionary<float, Sprite> claws = new Dictionary<float, Sprite>();

    // Sprite drawn by f(p), p in [-extent, extent]² with y pointing down (canvas style), 3×3 supersampled
    static Sprite Shape(int px, float extent, Func<Vector2, Color> f)
    {
        return UiKit.MakeSprite(px, (x, y, s) =>
        {
            float r = 0f, g = 0f, b = 0f, a = 0f;
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                {
                    float u = ((x - 0.5f + (i + 0.5f) / 3f) / s * 2f - 1f) * extent;
                    float v = -((y - 0.5f + (j + 0.5f) / 3f) / s * 2f - 1f) * extent;
                    var c = f(new Vector2(u, v));
                    r += c.r * c.a; g += c.g * c.a; b += c.b * c.a; a += c.a;
                }
            return a > 0f ? new Color(r / a, g / a, b / a, a / 9f) : Color.clear;
        });
    }

    static Color Over(Color under, Color over)
    {
        float a = over.a + under.a * (1f - over.a);
        if (a <= 0f) return Color.clear;
        Color c = (over * over.a + under * under.a * (1f - over.a)) / a;
        c.a = a;
        return c;
    }

    static float SegDist(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
        return Vector2.Distance(p, a + ab * t);
    }

    static float PolyDist(Vector2 p, IList<Vector2> pts, bool closed)
    {
        float d = float.MaxValue;
        int n = closed ? pts.Count : pts.Count - 1;
        for (int i = 0; i < n; i++) d = Mathf.Min(d, SegDist(p, pts[i], pts[(i + 1) % pts.Count]));
        return d;
    }

    static List<Vector2> Quad(Vector2 a, Vector2 c, Vector2 b, int n = 16)
    {
        var pts = new List<Vector2>();
        for (int i = 0; i <= n; i++)
        {
            float t = (float)i / n, m = 1f - t;
            pts.Add(m * m * a + 2f * m * t * c + t * t * b);
        }
        return pts;
    }

    static Sprite StarSprite => star ? star : (star = Shape(64, 1f, p =>
    {
        var pts = new Vector2[8];
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI / 4f, q = i % 2 == 1 ? 0.3f : 1f;
            pts[i] = new Vector2(Mathf.Cos(a) * q, Mathf.Sin(a) * q);
        }
        return UiKit.InPolygon(pts, p) ? Color.white : Color.clear;
    }));

    // nine-cornered rock; its corners depend on the variant like script.js's o.ph
    static Sprite RockSprite(int variant)
    {
        variant %= 7;
        if (rocks.TryGetValue(variant, out var s) && s) return s;
        var pts = new Vector2[9];
        for (int i = 0; i < 9; i++)
        {
            float a = i / 9f * Mathf.PI * 2f, k = 0.82f + ((i * 37 + variant) % 7) / 30f;
            pts[i] = new Vector2(Mathf.Cos(a) * k, Mathf.Sin(a) * k * 0.85f);
        }
        var crack = new[] { new Vector2(-0.4f, -0.3f), new Vector2(-0.05f, -0.05f), new Vector2(0.2f, -0.35f) };
        Color light = UiKit.Hex("#8a7f8f"), dark = UiKit.Hex("#4b4250"), line = UiKit.Hex("#2b1832");
        s = Shape(128, RockExtent, p =>
        {
            float edge = PolyDist(p, pts, true);
            bool inside = UiKit.InPolygon(pts, p);
            if (edge < 0.022f) return line;
            if (!inside) return Color.clear;
            var c = Color.Lerp(light, dark, Mathf.Clamp01((p.x + p.y + 2f) / 4f));
            if (PolyDist(p, crack, false) < 0.015f) c = Over(c, new Color(1f, 1f, 1f, 0.15f));
            return c;
        });
        rocks[variant] = s;
        return s;
    }

    // がま口: red pouch, lighter flap with a gold frame and two clasp balls (units of the radius)
    static Sprite PurseSprite
    {
        get
        {
            if (purse) return purse;
            var body = Quad(new Vector2(-1f, 0f), new Vector2(-1f, 0.95f), new Vector2(0f, 0.95f));
            body.AddRange(Quad(new Vector2(0f, 0.95f), new Vector2(1f, 0.95f), new Vector2(1f, 0f)));
            var flap = Quad(new Vector2(-1f, 0f), new Vector2(0f, -0.55f), new Vector2(1f, 0f));
            var bodyPts = body.ToArray();
            var flapPts = flap.ToArray();
            Color red = UiKit.Hex("#c0503f"), pink = UiKit.Hex("#d8685a"), gold = UiKit.Hex("#e0c46c");
            const float u = 1f / 24f;
            purse = Shape(128, PurseExtent, p =>
            {
                Color c = Color.clear;
                if (UiKit.InPolygon(bodyPts, p)) c = red;
                if (UiKit.InPolygon(flapPts, p)) c = pink;
                if (PolyDist(p, flap, false) < 1.5f * u) c = gold;
                if (Vector2.Distance(p, new Vector2(-4f * u, -0.32f)) < 4f * u ||
                    Vector2.Distance(p, new Vector2(4f * u, -0.32f)) < 4f * u) c = gold;
                return c;
            });
            return purse;
        }
    }

    static Sprite WheelSprite => wheelSprite ? wheelSprite : (wheelSprite = Shape(64, 14f, p =>
    {
        bool ring = Mathf.Abs(p.magnitude - 12f) < 1f;
        bool spoke = (Mathf.Abs(p.y) < 1f && Mathf.Abs(p.x) < 12f) || (Mathf.Abs(p.x) < 1f && Mathf.Abs(p.y) < 12f);
        return ring || spoke ? UiKit.Hex("#f4e6c4") : Color.clear;
    }));

    static Sprite ClawSprite(float open)
    {
        if (claws.TryGetValue(open, out var s) && s) return s;
        var left = Quad(Vector2.zero, new Vector2(-14f * open - 6f, 8f), new Vector2(-6f, 20f));
        var right = Quad(Vector2.zero, new Vector2(14f * open + 6f, 8f), new Vector2(6f, 20f));
        Color gold = UiKit.Hex("#f2c14e");
        s = Shape(96, 24f, p =>
        {
            if (p.x >= -6f && p.x <= 6f && p.y >= -4f && p.y <= 2f) return gold;
            return PolyDist(p, left, false) < 2f || PolyDist(p, right, false) < 2f ? gold : Color.clear;
        });
        claws[open] = s;
        return s;
    }

    // 16 alternating gold / white beams, as many as script.js's drawJackpot
    static Sprite RaysSprite => rays ? rays : (rays = Shape(512, 1f, p =>
    {
        if (p.magnitude > 1f) return Color.clear;
        float half = Mathf.Atan2(45f, 900f), theta = Mathf.Atan2(p.y, p.x);
        for (int i = 0; i < 16; i++)
        {
            float centre = (i + 1) * Mathf.PI * 2f / 16f;
            if (Mathf.Abs(Mathf.DeltaAngle(theta * Mathf.Rad2Deg, centre * Mathf.Rad2Deg)) < half * Mathf.Rad2Deg)
                return i % 2 == 1 ? new Color(0.949f, 0.757f, 0.306f, 0.30f) : new Color(1f, 1f, 1f, 0.12f);
        }
        return Color.clear;
    }));

    // script.js bg(): sky and dirt gradients, wavy strata, sparkling specks, ground strip and the shop awning
    static Sprite BackgroundSprite()
    {
        if (background) return background;
        const int k = 2;   // texels per canvas px
        int tw = (int)W * k, th = (int)H * k;
        var px = new Color[tw * th];
        Color sky0 = UiKit.Hex("#3a2147"), sky1 = UiKit.Hex("#5a2f52"), dirt0 = UiKit.Hex("#3b2140"), dirt1 = UiKit.Hex("#1f1026");
        Color cream = UiKit.Hex("#f4e6c4"), red = UiKit.Hex("#d94a3d"), strata = new Color(1f, 1f, 1f, 0.05f);
        for (int ty = 0; ty < th; ty++)
            for (int tx = 0; tx < tw; tx++)
            {
                float x = (tx + 0.5f) / k, y = H - (ty + 0.5f) / k;   // canvas coords, y down
                Color c = y < GROUND ? Color.Lerp(sky0, sky1, y / GROUND) : Color.Lerp(dirt0, dirt1, (y - GROUND) / (H - GROUND));
                if (y >= GROUND)
                    for (float yy = GROUND + 60f; yy < H; yy += 90f)
                        if (Mathf.Abs(y - (yy + Mathf.Sin(x * 0.03f + yy) * 4f)) < 0.5f) c = Over(c, strata);
                if (y >= GROUND - 4f && y < GROUND) c = x % 24f < 12f ? red : cream;
                int i = Mathf.FloorToInt(x / 40f);
                Color awning = i % 2 == 1 ? cream : red;
                if (y < 26f || Vector2.Distance(new Vector2(x, y), new Vector2(i * 40f + 20f, 26f)) < 20f) c = awning;
                px[ty * tw + tx] = c;
            }

        // specks in the dirt
        var rng = new System.Random(2);
        for (int n = 0; n < 140; n++)
        {
            float sx = (float)rng.NextDouble() * W, sy = GROUND + (float)rng.NextDouble() * (H - GROUND);
            float size = (float)rng.NextDouble() * 2f + 0.5f;
            Color sc = rng.NextDouble() > 0.8 ? new Color(0.949f, 0.757f, 0.306f, 0.19f) : new Color(1f, 1f, 1f, 0.06f);
            for (int ty = Mathf.FloorToInt((H - sy - size) * k); ty < Mathf.CeilToInt((H - sy) * k); ty++)
                for (int tx = Mathf.FloorToInt(sx * k); tx < Mathf.CeilToInt((sx + size) * k); tx++)
                    if (tx >= 0 && tx < tw && ty >= 0 && ty < th) px[ty * tw + tx] = Over(px[ty * tw + tx], sc);
        }

        var tex = new Texture2D(tw, th, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        tex.SetPixels(px);
        tex.Apply();
        background = Sprite.Create(tex, new Rect(0, 0, tw, th), new Vector2(0.5f, 0.5f), 100f);
        return background;
    }

    /* ---------- sounds (script.js fanfare / jackpotSound) ---------- */
    static AudioClip Fanfare() => Sfx.Make("fanfare",
        (523.25f, 0f, .14f, Sfx.Wave.Triangle, .18f, 0f), (659.25f, .14f, .14f, Sfx.Wave.Triangle, .18f, 0f),
        (783.99f, .28f, .14f, Sfx.Wave.Triangle, .18f, 0f), (1046.5f, .42f, .5f, Sfx.Wave.Triangle, .18f, 0f),
        (783.99f, .95f, .14f, Sfx.Wave.Triangle, .18f, 0f), (1046.5f, 1.1f, .7f, Sfx.Wave.Triangle, .18f, 0f));

    static AudioClip JackpotSound()
    {
        var tones = new List<(float, float, float, Sfx.Wave, float, float)>();
        float[] run = { 523.25f, 659.25f, 783.99f, 1046.5f, 1318.5f, 1568f, 2093f };
        for (int i = 0; i < run.Length; i++) tones.Add((run[i], i * .085f, .18f, Sfx.Wave.Square, .09f, 0f));
        foreach (float f in new[] { 523.25f, 659.25f, 783.99f, 1046.5f }) tones.Add((f, .65f, 1.2f, Sfx.Wave.Triangle, .12f, 0f));
        for (int i = 0; i < 14; i++) tones.Add((1800f + Rand() * 1800f, .2f + i * .095f, .12f, Sfx.Wave.Triangle, .07f, 0f));
        return Sfx.Make("jackpot", tones.ToArray());
    }
}
