using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Unity port of Assets/Minigame/FourthFloorGame (index.html + data.js + script.js + style.css), laid out as the
/// page shows in a 1920×1080 browser window: lanterns, title and progress, the message, the seven stalls in one
/// row, and the card tray under them. Drag each card (or tap it, then tap a stall) to the department's stall
/// that runs it; when every stall has its card the game is cleared. Untimed: a how-to-play card opens it, and a
/// mistake makes the right stall glow as a hint. Builds its whole UI at runtime from the PNGs in
/// FourthFloorGame/images, which the setup tool puts in `art`. Clearing it clears the floor's quest; the
/// congratulations hold a few seconds (a click or Space skips) and then the game goes back to the floor.
/// </summary>
public class FourthFloorGame : MonoBehaviour
{
    [Tooltip("Stall art, card art and card-back sprites from Assets/Minigame/FourthFloorGame/images (looked up by file name).")]
    public List<Sprite> art = new List<Sprite>();

    [Tooltip("Assets/Minigame/ThirdFloorGame/EmojiSheet.png, for the 🎉.")]
    public Texture2D emojiSheet;

    [Tooltip("The 3D floor scene this game returns to; its quest is cleared when every stall is filled.")]
    [SceneName] public string floorScene = "FourthFloorScene";

    /* ---------- data.js settings ---------- */
    const int CardsPerGame = 0;          // cardsPerGame: null
    const bool OnlyFeatured = true;      // onlyFeatured: one card per department (featured: true); false = all cards
    const int HintAfterMistakes = 1;     // hintAfterMistakes (the page: 3)

    // text sizes are the page's CSS px; the 1920×1080 canvas shows them at ×0.67 on a 1280×720 screen, so they grow
    static int S(int px) => Mathf.RoundToInt(px * 1.4f);

    // name is Japanese, en English; Name picks by the current language
    class Dept { public string id, name, en, color; public string Name => L.T(name, en); }
    class Food { public string id, name, en, dept; public bool featured; public string Name => L.T(name, en); }

    static readonly Dept[] DEPTS =
    {
        new Dept { id = "eizo-housou", name = "映像クリエイター学科・放送映像学科", en = "Video Creator / Broadcast Video", color = "#E0286E" },
        new Dept { id = "tv", name = "TVディレクター学科", en = "TV Director", color = "#C8283C" },
        new Dept { id = "it-team", name = "ITエンジニア学科・ITキャリア養成科", en = "IT Engineer / IT Career", color = "#1F78E8" },
        new Dept { id = "arch-team", name = "建築学科・SD研究会・建築研究会", en = "Architecture / SD Club / Architecture Club", color = "#9A6232" },
        new Dept { id = "denki", name = "電気電子学科", en = "Electrical & Electronics", color = "#F2B800" },
        new Dept { id = "rinsho", name = "臨床工学科", en = "Clinical Engineering", color = "#E04A52" },
        new Dept { id = "kaigo", name = "介護福祉学科", en = "Care & Welfare", color = "#D4508C" },
    };

    static readonly Food[] FOODS =
    {
        new Food { id = "churros", name = "チュロス", en = "Churros", dept = "eizo-housou", featured = true },
        new Food { id = "chocobanana", name = "チョコバナナ", en = "Choco Banana", dept = "it-team" },
        new Food { id = "icecream", name = "アイスクリーム", en = "Ice Cream", dept = "eizo-housou" },
        new Food { id = "yakisoba", name = "焼きそば", en = "Yakisoba", dept = "eizo-housou" },
        new Food { id = "yakitori", name = "焼き鳥", en = "Yakitori", dept = "kaigo" },
        new Food { id = "molkky", name = "モルック", en = "Mölkky", dept = "it-team" },
        new Food { id = "sui-gyoza", name = "水餃子", en = "Boiled Gyoza", dept = "tv", featured = true },
        new Food { id = "minigame", name = "ミニゲーム", en = "Minigames", dept = "it-team" },
        new Food { id = "escape", name = "脱出ゲーム", en = "Escape Game", dept = "eizo-housou" },
        new Food { id = "ai-newspaper", name = "AIで自分新聞", en = "My AI Newspaper", dept = "it-team", featured = true },
        new Food { id = "toast", name = "トースト", en = "Toast", dept = "rinsho" },
        new Food { id = "machi-sketch", name = "まちと本棚スケッチ", en = "Town & Bookshelf Sketch", dept = "arch-team", featured = true },
        new Food { id = "tenshin", name = "点心", en = "Dim Sum", dept = "it-team" },
        new Food { id = "chashitsu", name = "茶室", en = "Tea Room", dept = "arch-team" },
        new Food { id = "gyoza", name = "餃子", en = "Gyoza", dept = "kaigo" },
        new Food { id = "matcha", name = "抹茶", en = "Matcha", dept = "arch-team" },
        new Food { id = "myanmar-tea", name = "ミャンマーのお茶", en = "Myanmar Tea", dept = "arch-team" },
        new Food { id = "myanmar-sweets", name = "ミャンマーのスイーツ", en = "Myanmar Sweets", dept = "arch-team" },
        new Food { id = "electric-work", name = "電気工事", en = "Electrical Work", dept = "denki" },
        new Food { id = "irairabo", name = "イライラ棒", en = "Wire Loop Game", dept = "denki", featured = true },
        new Food { id = "medical-exhibit", name = "医療機器展示・体験", en = "Medical Device Demo", dept = "rinsho", featured = true },
        new Food { id = "yaki-gyoza", name = "焼き餃子", en = "Fried Gyoza", dept = "tv" },
        new Food { id = "elderly-sim", name = "高齢者疑似体験", en = "Elderly Experience", dept = "kaigo" },
        new Food { id = "teyoku", name = "手浴体験", en = "Hand Bath", dept = "kaigo", featured = true },
        new Food { id = "fukushi-yogu", name = "福祉用具展示", en = "Welfare Equipment", dept = "kaigo" },
    };

    /* ---------- data.js messages ---------- */
    static string Title => L.T("読祭 マッチングゲーム！", "Yomisai Matching Game!");
    static string MsgInstruction => L.T("カードを、やっている学科の屋台へ！（ドラッグ、またはカードをタップ → 屋台をタップ）",
        "Bring each card to the department that runs it! (drag it, or click a card, then a department)");
    static string MsgProgressLabel => L.T("せいかいの数：", "Correctly placed:");
    static string MsgSelected => L.T("「{food}」をえらんだよ。どの屋台かな？", "You picked \"{food}\". Which department is it?");
    static string MsgPickCardFirst => L.T("先にカードをえらんでね", "Pick a card first");
    static string MsgBoothFull => L.T("{dept}の屋台はもうかんせい！ ✓ ほかの屋台にためしてね", "{dept} is already done! ✓ Try another one");
    static string[] MsgCorrect => L.English
        ? new[] { "Correct! {food} is at {dept}!", "Nice! {dept}'s {food}!", "Spot on! You can try {food} at {dept}!" }
        : new[] { "せいかい！ {food}は{dept}だよ！", "ナイス！ {dept}の{food}！", "ぴったり！ {food}は{dept}で体験できるよ！" };
    static string[] MsgWrong => L.English
        ? new[] { "Not that one, try again!", "So close! Try again!", "Hmm, which department could it be? Try again!" }
        : new[] { "ちがうよ、もう一度！", "おしい！ もう一度ためしてね。", "うーん、どこの屋台かな？ もう一度！" };
    static string MsgHint => L.T("ヒント：ピカピカ光っている屋台を見てみて！", "Hint: look at the department that's glowing!");
    static string MsgCongratsTitle => L.T("ぜんぶ せいかい！ おめでとう！", "All correct! Congratulations!");
    static string MsgCongratsText => L.T("すべての屋台が正しいカードでそろったよ！", "Every department has its right card!");
    static string MsgCongratsHint => L.T("もうすぐフロアに戻るよ", "Heading back to the floor…");
    static string MsgAllDone => L.T("ぜんぶ せいかい！ おめでとう！", "All correct! Congratulations!");   // shown between two 🎉

    // style.css :root
    static readonly Color Night = UiKit.Hex("#14204A"), NightDeep = UiKit.Hex("#0A1030"), Lantern = UiKit.Hex("#E4392B"),
        LanternDeep = UiKit.Hex("#B1241A"), Gold = UiKit.Hex("#FFC83D"), Paper = UiKit.Hex("#FFF8E8"), Ink = UiKit.Hex("#2B1A12"),
        OkC = UiKit.Hex("#2E9E5B"), Mint = UiKit.Hex("#7CFFB2"), Done = UiKit.Hex("#3DDC84");

    /* ---------- the page at 1920 px wide (CSS px = canvas units), enlarged to fill a 1080 p screen ---------- */
    const float ScreenW = 1920f, ScreenH = 1080f;
    const float GameW = 1920f - 2f * 16f;                  // .game, with narrower side padding
    const float GameTop = 64f;                             // .game padding-top (min-width 1200)
    const float TopbarH = 86f;                             // title sign
    const float MsgY = GameTop + TopbarH + 12f, MsgH = 76f;    // tall enough for the enlarged message
    const float BoothsY = MsgY + MsgH + 14f, BoothGap = 16f, MaxBooth = 400f;
    const float AreaBottom = ScreenH - 76f;                // above the back button
    const float PanelW = 506f, PanelGap = 24f, MaxCardW = 240f;
    float CardW = 165f, CardH = 165f * 440f / 400f;        // card image 400 × 440, sized to fit the panel
    const float TrayColGap = 14f, TrayRowGap = 14f;
    const float CardHoverZoom = 1.5f;                      // a card under the pointer grows to read it
    const float TrayPadL = 2f + 12f + 4f, TrayPadT = 2f + 6f + 20f, TrayPadB = 10f + 12f + 2f;   // border + .tray-wrap + .tray

    class Booth
    {
        public Dept dept;
        public RectTransform root, body, zone;
        public Image glow;
        public UiPointerState pointer;
        public bool complete;
        public float hintUntil, okUntil, shakeUntil;
    }

    class Card
    {
        public Food food;
        public bool placed;
        public RectTransform root, face;
        public Image glow;
        public GameObject check;
        public CanvasGroup group;
        public UiPointerState pointer;
        public Vector2 target;
        public float shakeUntil;
    }

    readonly List<Booth> booths = new List<Booth>();
    readonly List<Card> cards = new List<Card>();
    readonly Dictionary<string, int> wrongStreak = new Dictionary<string, int>();
    Card selected, dragging;
    Booth hoverBooth;
    int total, correct, trayCols;
    float trayColW;
    Vector2 trayOffset;
    bool finished, leaving;
    Vector2 grabOffset;
    AudioSource audioSource;
    float messageShakeUntil;   // one MinigameKit.Shake at a time, or the message box would drift

    RectTransform canvasRoot, game, boothRow, tray, fx, ghost, progressFill, messageBox;
    Text progressText, message;
    Image messageBg;
    GameObject congrats, trayDone;
    readonly List<GameObject> messageParty = new List<GameObject>();
    Coroutine messagePop, bump;
    readonly List<RectTransform> lanterns = new List<RectTransform>();
    Dictionary<string, Sprite> sprites;
    Sprite party;

    /* ---------- setup ---------- */
    void Awake()
    {
        // the floor scene locks the cursor for mouse look; this game needs it
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        sprites = art.Where(s => s != null).GroupBy(s => s.name).ToDictionary(g => g.Key, g => g.First());
        if (sprites.Count == 0) Debug.LogWarning("FourthFloorGame: no art; run Tools > Elevator Game > Create Fourth Floor Game Scene.");
        if (emojiSheet != null) party = Sprite.Create(emojiSheet, new Rect(256, 0, 256, 256), new Vector2(0.5f, 0.5f), 100f);   // 🎉 cell

        audioSource = MinigameKit.Audio(gameObject);
        UiKit.EnsureEventSystem();
        Init();
        // untimed, so no countdown: the card just covers the game until スタート！
        MinigameKit.HowToPlay(canvasRoot, "マッチングゲーム", "Matching Game", new[]
        {
            ("カードを、その出し物をやっている学科の屋台へドラッグしよう", "Drag each card to the stall of the department that runs it"),
            ("タップでもOK：カードをタップ → 屋台をタップ", "Or tap a card, then tap a stall"),
            ("まちがえたら、正しい屋台がピカピカ光ってヒントになるよ", "Wrong stall? The right one glows as a hint"),
            ($"{booths.Count}つの屋台をぜんぶうめたらクリア！", $"Fill all {booths.Count} stalls to clear!"),
        }, null, 1.5f);
    }

    Sprite Art(string id) => sprites.TryGetValue(id, out var s) ? s : null;

    static string Fill(string s, string food = "", string dept = "") => s.Replace("{food}", food).Replace("{dept}", dept);

    static List<T> Shuffle<T>(IEnumerable<T> src)
    {
        var a = src.ToList();
        for (int i = a.Count - 1; i > 0; i--)
        {
            int j = UnityEngine.Random.Range(0, i + 1);
            (a[i], a[j]) = (a[j], a[i]);
        }
        return a;
    }

    // script.js init(): deck, stalls, tray
    void Init()
    {
        var pool = FOODS.AsEnumerable();
        if (OnlyFeatured && FOODS.Any(f => f.featured)) pool = FOODS.Where(f => f.featured);
        var deck = Shuffle(pool);
        if (CardsPerGame > 0 && CardsPerGame < deck.Count) deck = deck.Take(CardsPerGame).ToList();
        var used = new HashSet<string>(deck.Select(f => f.dept));
        var depts = DEPTS.Where(d => used.Contains(d.id)).ToList();

        BuildUi(depts, deck.Count);
        total = booths.Count;
        foreach (var f in deck) cards.Add(BuildCard(f));
        LayoutCards(true);
        UpdateProgress(false);
        SetMessage(MsgInstruction, "info");
    }

    void BuildUi(List<Dept> depts, int cardCount)
    {
        var canvas = UiKit.MakeCanvas("FourthFloorGameCanvas", 0, new Vector2(ScreenW, ScreenH));
        canvasRoot = (RectTransform)canvas.transform;

        // body: night gradient, a soft glow at the top, a few stars
        var sky = UiKit.Img("Sky", canvasRoot, UiKit.MakeSprite(64, (x, y, s) => Color.Lerp(NightDeep, Night, y / s)), Color.white);
        UiKit.Stretch(sky.rectTransform);
        var haze = UiKit.Img("Haze", canvasRoot, UiKit.Glow, UiKit.Hex("#3A4FA0", 0.9f)).rectTransform;
        haze.anchorMin = haze.anchorMax = new Vector2(0.5f, 1f);
        haze.sizeDelta = new Vector2(1500f, 650f);
        haze.anchoredPosition = new Vector2(0f, 0.08f * ScreenH);
        foreach (var (sx, sy, a) in new[] { (0.12f, 0.22f, 0.67f), (0.33f, 0.10f, 0.53f), (0.58f, 0.28f, 0.67f), (0.81f, 0.14f, 0.53f),
                     (0.92f, 0.36f, 0.67f), (0.06f, 0.58f, 0.4f), (0.70f, 0.64f, 0.4f) })
        {
            var star = UiKit.Img("Star", canvasRoot, UiKit.Circle, new Color(1f, 1f, 1f, a)).rectTransform;
            star.anchorMin = star.anchorMax = new Vector2(sx, 1f - sy);
            star.sizeDelta = new Vector2(2f, 2f);
        }
        BuildLanterns();

        game = UiKit.Rect("Game", canvasRoot);
        game.anchorMin = game.anchorMax = game.pivot = new Vector2(0.5f, 1f);
        game.anchoredPosition = Vector2.zero;
        game.sizeDelta = new Vector2(GameW, ScreenH);

        BuildTopbar();

        // .message: full-width pill
        messageBg = UiKit.Box("Message", game, new Color(1f, 1f, 1f, 0.12f), MsgH * 0.5f);
        messageBox = messageBg.rectTransform;
        UiKit.Place(messageBox, 0f, MsgY, GameW, MsgH);
        message = UiKit.Label(messageBox, "", S(28), Paper);
        message.resizeTextForBestFit = true;   // long English lines shrink a little instead of spilling out
        message.resizeTextMinSize = S(20);
        message.resizeTextMaxSize = S(28);
        UiKit.Stretch(message.rectTransform);
        message.rectTransform.offsetMin = new Vector2(20f, 4f);
        message.rectTransform.offsetMax = new Vector2(-20f, -4f);
        for (int i = 0; i < 2; i++)
        {
            var e = Emoji(messageBox, 34f);
            if (e != null) { e.SetActive(false); messageParty.Add(e); }
        }

        // the stalls on the left in two rows (4 + 3, the second centred), as big as the space allows; the cards
        // in a panel on the right
        float areaH = AreaBottom - BoothsY, stallsW = GameW - PanelGap - PanelW;
        boothRow = UiKit.Rect("Booths", game);
        int n = depts.Count;
        int rows = n > 4 ? 2 : 1;
        int cols = Mathf.CeilToInt(n / (float)rows);
        float bw = Mathf.Min(MaxBooth, (stallsW - (cols - 1) * BoothGap) / cols,
                             (areaH - (rows - 1) * BoothGap) / rows * 560f / 640f), bh = bw * 640f / 560f;
        float boothsH = rows * bh + (rows - 1) * BoothGap;
        UiKit.Place(boothRow, 0f, BoothsY + (areaH - boothsH) * 0.5f, stallsW, boothsH);
        for (int i = 0; i < n; i++)
        {
            int row = i / cols, col = i % cols, inRow = Mathf.Min(cols, n - row * cols);
            float rowW = inRow * bw + (inRow - 1) * BoothGap;
            booths.Add(BuildBooth(depts[i], (stallsW - rowW) * 0.5f + col * (bw + BoothGap), row * (bh + BoothGap), bw, bh));
        }

        // .tray-wrap / .tray: the column count giving the biggest cards, the grid centred in the panel
        var wrap = UiKit.Box("TrayWrap", game, new Color(0f, 0f, 0f, 0.44f), 22f, new Color(1f, 1f, 1f, 0.13f), 2f);
        UiKit.Place(wrap.rectTransform, GameW - PanelW, BoothsY, PanelW, areaH);
        float trayW = PanelW - 2f * TrayPadL, trayH = areaH - TrayPadT - TrayPadB;
        tray = UiKit.Rect("Tray", wrap.transform);
        UiKit.Place(tray, TrayPadL, TrayPadT, trayW, trayH);
        int count = Mathf.Max(1, cardCount);
        float best = 0f;
        for (int c = 1; c <= count; c++)
        {
            int r = Mathf.CeilToInt(count / (float)c);
            float byW = (trayW - (c - 1) * TrayColGap) / c;
            float byH = (trayH - (r - 1) * TrayRowGap) / r * 400f / 440f;
            float size = Mathf.Min(MaxCardW, byW, byH);
            if (size > best + 0.5f) { best = size; trayCols = c; }
        }
        CardW = best;
        CardH = best * 440f / 400f;
        trayColW = CardW;
        int trayRows = Mathf.CeilToInt(count / (float)trayCols);
        trayOffset = new Vector2((trayW - (trayCols * CardW + (trayCols - 1) * TrayColGap)) * 0.5f,
                                 (trayH - (trayRows * CardH + (trayRows - 1) * TrayRowGap)) * 0.5f);
        trayDone = Emoji(tray, 35f);   // .tray:empty::before
        if (trayDone != null) trayDone.SetActive(false);

        fx = UiKit.Stretch(UiKit.Rect("Fx", canvasRoot));

        // back to the 3D floor, in the empty space under the page
        var back = UiKit.ColorButton(canvasRoot, L.T("← フロアに戻る", "← Back to the floor"), Paper, Ink, new Color(0f, 0f, 0f, 0.5f), 56f, S(22), out var backText);
        var brt = (RectTransform)back.transform;
        brt.anchorMin = brt.anchorMax = brt.pivot = Vector2.zero;
        brt.anchoredPosition = new Vector2(28f, 12f);
        brt.sizeDelta = new Vector2(backText.preferredWidth + 48f, 56f);
        back.onClick.AddListener(Leave);

        BuildCongrats();
    }

    // .lanterns: nine lanterns on a string, spaced around, swaying from 12 px above
    void BuildLanterns()
    {
        var line = UiKit.Img("String", canvasRoot, null, new Color(0f, 0f, 0f, 0.44f)).rectTransform;
        line.anchorMin = new Vector2(0f, 1f);
        line.anchorMax = new Vector2(1f, 1f);
        line.pivot = new Vector2(0.5f, 1f);
        line.anchoredPosition = new Vector2(0f, -10f);
        line.sizeDelta = new Vector2(0f, 3f);
        const float pad = 0.02f * ScreenW, w = 40f, h = w / 0.82f;
        float slot = (ScreenW - 2f * pad) / 9f;
        for (int i = 0; i < 9; i++)
        {
            var pivot = UiKit.Rect("Lantern", canvasRoot);
            pivot.anchorMin = pivot.anchorMax = new Vector2(0f, 1f);
            pivot.sizeDelta = Vector2.zero;
            pivot.anchoredPosition = new Vector2(pad + (i + 0.5f) * slot, 0f);
            var glow = UiKit.Img("Glow", pivot, UiKit.Glow, UiKit.Hex("#FF6A3D", 0.33f)).rectTransform;
            glow.anchoredPosition = new Vector2(0f, -12f - h * 0.5f);
            glow.sizeDelta = new Vector2(w + 58f, h + 58f);
            var body = UiKit.Img("Body", pivot, UiKit.Circle, Lantern).rectTransform;
            body.anchoredPosition = new Vector2(0f, -12f - h * 0.5f);
            body.sizeDelta = new Vector2(w, h);
            var core = UiKit.Img("Core", body, UiKit.Glow, UiKit.Hex("#FF7B5C")).rectTransform;
            UiKit.Center(core, w * 0.7f, h * 0.6f).anchoredPosition = new Vector2(0f, h * 0.1f);
            for (int r = 1; r < 6; r++)   // paper ribs
                UiKit.Center(UiKit.Img("Rib", body, null, new Color(0f, 0f, 0f, 0.16f)).rectTransform, w * 0.9f, 1f)
                    .anchoredPosition = new Vector2(0f, h * (0.5f - r / 6f));
            foreach (float cy in new[] { h * 0.5f, -h * 0.5f })
                UiKit.Center(UiKit.Box("Cap", body, UiKit.Hex("#1c1210"), 3f).rectTransform, w * 0.56f, 6f).anchoredPosition = new Vector2(0f, cy);
            lanterns.Add(pivot);
        }
    }

    // .topbar: the title sign on the left, the progress on the right
    void BuildTopbar()
    {
        var title = UiKit.Box("Title", game, Lantern, 12f, Gold, 4f);
        var tt = UiKit.Label(title.transform, Title, 52, Paper);
        tt.horizontalOverflow = HorizontalWrapMode.Overflow;
        UiKit.HardShadow(tt, UiKit.Hex("#7d140c"), 2f).effectDistance = new Vector2(2f, -2f);
        UiKit.Stretch(tt.rectTransform);
        UiKit.Place(title.rectTransform, 0f, GameTop, tt.preferredWidth + 44f + 8f, TopbarH);
        title.rectTransform.localEulerAngles = new Vector3(0f, 0f, 1.2f);
        UiKit.HardShadow(title, new Color(0f, 0f, 0f, 0.44f), 6f).useGraphicAlpha = false;

        const float progW = 580f, progH = 78f;
        float x = GameW - progW, y = GameTop + (TopbarH - progH) * 0.5f;
        var plabel = UiKit.Label(game, MsgProgressLabel, S(22), Paper, TextAnchor.LowerLeft);
        plabel.horizontalOverflow = HorizontalWrapMode.Overflow;
        UiKit.Place(plabel.rectTransform, x, y, progW - 150f, 50f);
        progressText = UiKit.Label(game, "0 / 0", S(40), Gold, TextAnchor.LowerRight);
        progressText.horizontalOverflow = HorizontalWrapMode.Overflow;
        UiKit.Place(progressText.rectTransform, GameW - 150f, y, 150f, 52f);
        progressText.rectTransform.pivot = new Vector2(0.8f, 0.5f);
        progressText.rectTransform.anchoredPosition += new Vector2(0.8f * 150f, -26f);
        var bar = UiKit.Box("Bar", game, new Color(0f, 0f, 0f, 0.53f), 11f, new Color(1f, 1f, 1f, 0.27f), 2f);
        UiKit.Place(bar.rectTransform, x, y + 56f, progW, 22f);
        var fill = UiKit.Img("Fill", bar.transform, UiKit.MakeSprite(32, (px, py, s) => Color.Lerp(Gold, UiKit.Hex("#FF8A3D"), px / s)), Color.white);
        progressFill = UiKit.Stretch(fill.rectTransform, 2f);
        progressFill.pivot = new Vector2(0f, 0.5f);
    }

    // .congrats: dimmed screen, paper panel with the card back peeking over the top
    void BuildCongrats()
    {
        var dim = UiKit.Img("Congrats", canvasRoot, null, new Color(0f, 0f, 0f, 0.44f));
        dim.raycastTarget = true;   // blocks the game; MinigameKit.ResultThenExit takes the click
        UiKit.Stretch(dim.rectTransform);
        congrats = dim.gameObject;

        const float w = 600f, h = 300f;
        var panel = UiKit.Box("Panel", dim.transform, Paper, 28f, Gold, 5f);
        UiKit.Center(panel.rectTransform, w, h);
        panel.rectTransform.localScale = Vector3.one * 1.35f;   // enlarged with the rest
        UiKit.HardShadow(panel, new Color(0f, 0f, 0f, 0.53f), 10f).useGraphicAlpha = false;
        var p = panel.rectTransform;

        var back = UiKit.Img("CardBack", p, Art("card-back"), Color.white);
        back.preserveAspect = true;
        var brt = back.rectTransform;
        brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 1f);
        brt.anchoredPosition = new Vector2(0f, 42f);
        brt.sizeDelta = new Vector2(104f, 114f);
        brt.localEulerAngles = new Vector3(0f, 0f, 7f);
        if (back.sprite == null) back.enabled = false;

        var e = Emoji(p, 51f);
        if (e != null)
        {
            var ert = (RectTransform)e.transform;
            ert.anchorMin = ert.anchorMax = ert.pivot = new Vector2(0.5f, 1f);
            ert.anchoredPosition = new Vector2(0f, -78f);
        }
        var t = UiKit.Label(p, MsgCongratsTitle, S(30), Lantern);
        t.resizeTextForBestFit = true;
        t.resizeTextMinSize = 28;
        t.resizeTextMaxSize = S(30);
        Top(t.rectTransform, 134f, 56f, w - 48f);
        var body = UiKit.Label(p, MsgCongratsText, S(16), Ink);
        body.lineSpacing = 1.15f;
        Top(body.rectTransform, 194f, 56f, w - 48f);
        var hint = UiKit.Label(p, MsgCongratsHint, S(13), new Color(Ink.r, Ink.g, Ink.b, 0.6f), TextAnchor.MiddleCenter, false);
        Top(hint.rectTransform, 256f, 28f, w - 48f);
        congrats.SetActive(false);
    }

    // 🎉 from the emoji sheet (none without the sheet)
    GameObject Emoji(Transform parent, float size)
    {
        if (party == null) return null;
        var img = UiKit.Img("Party", parent, party, Color.white);
        img.preserveAspect = true;
        UiKit.Center(img.rectTransform, size, size);
        return img.gameObject;
    }

    static void Top(RectTransform rt, float y, float h, float w)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -y);
        rt.sizeDelta = new Vector2(w, h);
    }

    /* ---------- stalls and cards ---------- */
    // .booth.has-art: the stall picture, its counter (.dropzone) where the card lands
    Booth BuildBooth(Dept dept, float x, float y, float w, float h)
    {
        var b = new Booth { dept = dept };
        b.root = UiKit.Rect(dept.id, boothRow);
        UiKit.Place(b.root, x, y, w, h);
        var hit = b.root.gameObject.AddComponent<Image>();
        hit.color = Color.clear;
        b.pointer = b.root.gameObject.AddComponent<UiPointerState>();
        var trigger = b.root.gameObject.AddComponent<EventTrigger>();
        On(trigger, EventTriggerType.PointerClick, _ => OnBoothActivate(b));

        b.body = UiKit.Stretch(UiKit.Rect("Body", b.root));
        var sprite = Art(dept.id);
        if (sprite != null)
        {
            // drop-shadow glow: a gold / green copy of the picture just behind it
            b.glow = UiKit.Img("Glow", b.body, sprite, Color.clear);
            b.glow.preserveAspect = true;
            UiKit.Stretch(b.glow.rectTransform);
            b.glow.rectTransform.localScale = Vector3.one * 1.05f;
            var shadow = UiKit.Img("Shadow", b.body, sprite, new Color(0f, 0f, 0f, 0.4f));
            shadow.preserveAspect = true;
            UiKit.Stretch(shadow.rectTransform).anchoredPosition = new Vector2(0f, -6f);
            var pic = UiKit.Img("Art", b.body, sprite, Color.white);
            pic.preserveAspect = true;
            UiKit.Stretch(pic.rectTransform);
        }
        else
        {
            // .booth without art: a coloured board with the department name
            b.glow = UiKit.Img("Glow", b.body, UiKit.Glow, Color.clear);
            UiKit.Stretch(b.glow.rectTransform, -16f);
            var board = UiKit.Box("Board", b.body, UiKit.Hex(dept.color), 16f, Ink, 3f);
            UiKit.Stretch(board.rectTransform);
            var name = UiKit.Label(board.transform, dept.Name, S(22), Color.white);
            name.rectTransform.anchorMin = new Vector2(0f, 0.75f);
            name.rectTransform.anchorMax = Vector2.one;
            name.rectTransform.offsetMin = new Vector2(8f, 0f);
            name.rectTransform.offsetMax = new Vector2(-8f, -8f);
        }

        b.zone = UiKit.Rect("Dropzone", b.body);
        b.zone.anchorMin = new Vector2(0.129f, 1f - 0.52f - 0.222f);
        b.zone.anchorMax = new Vector2(0.129f + 0.735f, 1f - 0.52f);
        b.zone.offsetMin = b.zone.offsetMax = Vector2.zero;
        return b;
    }

    // .card.has-img: just the card picture with a drop shadow
    Card BuildCard(Food f)
    {
        var c = new Card { food = f };
        c.root = UiKit.Rect(f.id, tray);
        c.root.anchorMin = c.root.anchorMax = new Vector2(0f, 1f);
        c.root.pivot = new Vector2(0.5f, 0.5f);
        c.root.sizeDelta = new Vector2(CardW, CardH);
        var hit = c.root.gameObject.AddComponent<Image>();
        hit.color = Color.clear;
        c.pointer = c.root.gameObject.AddComponent<UiPointerState>();
        c.group = c.root.gameObject.AddComponent<CanvasGroup>();
        var trigger = c.root.gameObject.AddComponent<EventTrigger>();
        On(trigger, EventTriggerType.PointerClick, _ => ToggleSelect(c));
        On(trigger, EventTriggerType.BeginDrag, e => BeginDrag(c, (PointerEventData)e));
        On(trigger, EventTriggerType.Drag, e => Drag((PointerEventData)e));
        On(trigger, EventTriggerType.EndDrag, e => EndDrag(c, (PointerEventData)e));

        c.face = UiKit.Stretch(UiKit.Rect("Face", c.root));
        c.glow = BuildCardLook(c.face, f, 5f);
        // .card.selected::after: gold ✓ badge over the top-right corner
        var badge = UiKit.Img("Check", c.face, UiKit.Circle, Ink);
        var brt = badge.rectTransform;
        brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(1f, 1f);
        brt.anchoredPosition = new Vector2(12f, 12f);
        brt.sizeDelta = new Vector2(30f, 30f);
        UiKit.Stretch(UiKit.Img("Fill", brt, UiKit.Circle, Gold).rectTransform, 3f);
        UiKit.Stretch(UiKit.Label(brt, "✓", S(16), Ink).rectTransform);
        c.check = badge.gameObject;
        c.check.SetActive(false);
        return c;
    }

    // The card image (it has the name drawn on it), or a paper card with the name if the image is missing.
    // Returns the glow behind it (gold while selected).
    Image BuildCardLook(RectTransform parent, Food f, float shadowDrop)
    {
        var sprite = Art(f.id);
        var glow = UiKit.Img("Glow", parent, sprite, Color.clear);
        glow.preserveAspect = true;
        UiKit.Stretch(glow.rectTransform);
        glow.rectTransform.localScale = Vector3.one * 1.06f;
        var shadow = UiKit.Img("Shadow", parent, sprite, new Color(0f, 0f, 0f, 0.44f));
        shadow.preserveAspect = true;
        UiKit.Stretch(shadow.rectTransform).anchoredPosition = new Vector2(0f, -shadowDrop);
        var img = UiKit.Img("Art", parent, sprite, Color.white);
        img.preserveAspect = true;
        UiKit.Stretch(img.rectTransform);
        if (sprite == null)
        {
            // .card: paper, ink border, the name
            glow.sprite = UiKit.Glow;
            glow.preserveAspect = false;
            UiKit.Stretch(glow.rectTransform, -12f);
            shadow.color = new Color(0f, 0f, 0f, 0.53f);
            UiKit.Round(shadow, 18f);
            img.color = Ink;
            UiKit.Round(img, 18f);
            UiKit.Stretch(UiKit.Box("Paper", img.transform, Paper, 15f).rectTransform, 3f);
            var name = UiKit.Label(parent, f.Name, S(18), Ink);
            name.resizeTextForBestFit = true;
            name.resizeTextMinSize = 16;
            name.resizeTextMaxSize = S(18);
            UiKit.Stretch(name.rectTransform, 8f);
        }
        else if (L.English)
        {
            // the card art has its Japanese name printed along the bottom: cover that strip with the English name
            var strip = UiKit.Box("EnglishName", parent, UiKit.Hex("#FFFFFF"), 4f);
            var srt = strip.rectTransform;
            srt.anchorMin = new Vector2(0.05f, 0.05f);
            srt.anchorMax = new Vector2(0.95f, 0.26f);
            srt.offsetMin = srt.offsetMax = Vector2.zero;
            var en = UiKit.Label(srt, f.Name, S(18), Ink);
            en.resizeTextForBestFit = true;
            en.resizeTextMinSize = 10;   // low enough for the small card on a stall's counter
            en.resizeTextMaxSize = S(20);
            en.verticalOverflow = VerticalWrapMode.Truncate;
            en.lineSpacing = 0.9f;
            UiKit.Stretch(en.rectTransform, 1f);
        }
        return glow;
    }

    static void On(EventTrigger trigger, EventTriggerType type, Action<BaseEventData> action)
    {
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(e => action(e));
        trigger.triggers.Add(entry);
    }

    // .tray grid: left to right, top to bottom; the cards left close up the gaps
    void LayoutCards(bool instant)
    {
        var left = cards.Where(c => !c.placed).ToList();
        for (int i = 0; i < left.Count; i++)
        {
            int col = i % trayCols, row = i / trayCols;
            var pos = new Vector2(trayOffset.x + col * (trayColW + TrayColGap) + trayColW * 0.5f,
                                  -(trayOffset.y + row * (CardH + TrayRowGap) + CardH * 0.5f));
            left[i].target = pos;
            if (instant) left[i].root.anchoredPosition = pos;
        }
        if (trayDone != null) trayDone.SetActive(left.Count == 0);
    }

    /* ---------- script.js ---------- */
    void SetSelected(Card card)
    {
        selected = card;
        foreach (var c in cards)
            if (!c.placed) c.check.SetActive(c == card);
    }

    void ToggleSelect(Card card)
    {
        if (finished || card.placed) return;
        if (selected == card)
        {
            SetSelected(null);
            SetMessage(MsgInstruction, "info");
        }
        else
        {
            SetSelected(card);
            SetMessage(Fill(MsgSelected, card.food.Name), "info");
            audioSource.PlayOneShot(Sfx.Pop());
        }
    }

    void OnBoothActivate(Booth b)
    {
        if (finished) return;
        if (selected == null)
        {
            SetMessage(MsgPickCardFirst, "info");
            return;
        }
        if (!TryPlace(selected, b)) SetSelected(null);
    }

    void BeginDrag(Card c, PointerEventData e)
    {
        if (finished || c.placed || e.button != PointerEventData.InputButton.Left) return;
        dragging = c;
        // .card.ghost: a copy tilted 4° and scaled 1.08 that keeps the grab point under the pointer;
        // the card itself fades to .25
        RectTransformUtility.ScreenPointToLocalPointInRectangle(fx, e.pressPosition, null, out var press);
        grabOffset = (Vector2)fx.InverseTransformPoint(c.root.position) - press;
        ghost = UiKit.Rect("Ghost", fx);
        ghost.sizeDelta = c.root.sizeDelta;
        ghost.localScale = Vector3.one * 1.08f;
        ghost.localEulerAngles = new Vector3(0f, 0f, -4f);
        BuildCardLook(ghost, c.food, 18f);
        c.group.alpha = 0.25f;
        SetSelected(c);
        audioSource.PlayOneShot(Sfx.Pop());
        Drag(e);
    }

    void Drag(PointerEventData e)
    {
        if (dragging == null || ghost == null) return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(fx, e.position, null, out var local);
        ghost.anchoredPosition = local + grabOffset;
        var b = BoothAt(e.position);
        hoverBooth = b != null && !b.complete ? b : null;
    }

    void EndDrag(Card c, PointerEventData e)
    {
        if (dragging != c) return;
        dragging = null;
        hoverBooth = null;
        var target = BoothAt(e.position);
        bool ok = target != null && TryPlace(c, target);
        var g = ghost;
        ghost = null;
        if (ok)
        {
            if (g != null) Destroy(g.gameObject);
            c.group.alpha = 1f;
        }
        else
        {
            StartCoroutine(FlyBack(g, c));   // the ghost springs back to the card
            SetSelected(null);
        }
    }

    Booth BoothAt(Vector2 screen) =>
        booths.FirstOrDefault(b => RectTransformUtility.RectangleContainsScreenPoint(b.root, screen, null));

    bool TryPlace(Card card, Booth b)
    {
        if (finished || card.placed) return false;
        if (b.complete)
        {
            b.shakeUntil = Time.time + 0.5f;
            SetMessage(Fill(MsgBoothFull, dept: b.dept.Name), "info");
            return false;
        }
        if (card.food.dept == b.dept.id)
        {
            OnCorrect(card, b);
            return true;
        }
        OnWrong(card, b);
        return false;
    }

    void OnCorrect(Card card, Booth b)
    {
        card.placed = true;
        correct++;
        wrongStreak.Remove(card.food.id);
        SetSelected(null);

        // .card.placed in the counter: 23% of the stall's width, landing in
        card.check.SetActive(false);
        card.glow.color = Color.clear;
        card.root.SetParent(b.zone, false);
        card.root.anchorMin = card.root.anchorMax = new Vector2(0.5f, 0.5f);
        float w = b.root.rect.width * 0.23f;
        card.root.sizeDelta = new Vector2(w, w * 440f / 400f);
        card.root.anchoredPosition = Vector2.zero;
        card.face.anchoredPosition = Vector2.zero;
        card.face.localScale = Vector3.one;
        var shadow = card.face.Find("Shadow");
        if (shadow != null) ((RectTransform)shadow).anchoredPosition = new Vector2(0f, -2f);
        card.root.GetComponent<Image>().raycastTarget = false;
        card.group.blocksRaycasts = false;
        StartCoroutine(LandIn(card.root));

        b.complete = true;
        b.okUntil = Time.time + 0.7f;
        // .booth-check: green ✓ on the top-right corner
        float cs = Mathf.Clamp(b.root.rect.width * 0.15f, 30f, 60f);
        var check = UiKit.Img("Done", b.body, UiKit.Circle, Color.white).rectTransform;
        check.anchorMin = check.anchorMax = new Vector2(1f, 1f);
        check.anchoredPosition = new Vector2(b.root.rect.width * 0.03f - cs * 0.5f, -cs * 0.5f);
        check.sizeDelta = new Vector2(cs, cs);
        UiKit.Stretch(UiKit.Img("Fill", check, UiKit.Circle, UiKit.Hex("#22B14C")).rectTransform, 3f);
        UiKit.Stretch(UiKit.Label(check, "✓", Mathf.RoundToInt(b.root.rect.width * 0.08f), Color.white).rectTransform);
        StartCoroutine(CheckPop(check));
        Sparkle(b.root);
        audioSource.PlayOneShot(Sfx.Correct());
        MinigameKit.Punch(this, b.root, 1.08f, 0.25f);   // the root: Update drives the body's scale
        SetMessage(Fill(MsgCorrect[UnityEngine.Random.Range(0, MsgCorrect.Length)], card.food.Name, b.dept.Name), "ok");
        UpdateProgress(true);
        LayoutCards(false);

        if (correct >= total)
        {
            finished = true;
            GameProgress.ClearQuest(floorScene);   // the floor's QuestBanner shows クエストクリア！
            StartCoroutine(Later(0.4f, ShowCongrats));
        }
    }

    void OnWrong(Card card, Booth b)
    {
        wrongStreak[card.food.id] = (wrongStreak.TryGetValue(card.food.id, out int n) ? n : 0) + 1;
        b.shakeUntil = Time.time + 0.5f;
        card.shakeUntil = Time.time + 0.5f;
        SetMessage(MsgWrong[UnityEngine.Random.Range(0, MsgWrong.Length)], "ng");
        audioSource.PlayOneShot(Sfx.Wrong());
        if (Time.unscaledTime >= messageShakeUntil)
        {
            messageShakeUntil = Time.unscaledTime + 0.32f;
            MinigameKit.Shake(this, messageBox, 14f, 0.3f);
        }
        if (HintAfterMistakes > 0 && wrongStreak[card.food.id] >= HintAfterMistakes)
        {
            var right = booths.Where(x => x.dept.id == card.food.dept && !x.complete).ToList();
            if (right.Count > 0)
                StartCoroutine(Later(0.9f, () =>
                {
                    foreach (var r in right) r.hintUntil = Time.time + 2.8f;
                    SetMessage(MsgHint, "info");
                }));
        }
    }

    void UpdateProgress(bool doBump)
    {
        progressText.text = correct + " / " + total;
        progressFill.localScale = new Vector3(total > 0 ? (float)correct / total : 0f, 1f, 1f);
        if (doBump)
        {
            if (bump != null) StopCoroutine(bump);
            bump = StartCoroutine(Bump(progressText.rectTransform));
        }
    }

    void SetMessage(string msg, string kind, bool withParty = false)
    {
        message.text = msg;
        messageBg.color = kind == "ok" ? OkC : kind == "ng" ? UiKit.Hex("#FFE3DF") : new Color(1f, 1f, 1f, 0.12f);
        message.color = kind == "ok" ? Color.white : kind == "ng" ? UiKit.Hex("#8A1C12") : Paper;
        // 🎉 on both sides of the text
        float half = Mathf.Min(message.preferredWidth, GameW - 120f) * 0.5f + 24f;
        for (int i = 0; i < messageParty.Count; i++)
        {
            messageParty[i].SetActive(withParty);
            ((RectTransform)messageParty[i].transform).anchoredPosition = new Vector2(i == 0 ? -half : half, 0f);
        }
        if (messagePop != null) StopCoroutine(messagePop);
        messagePop = StartCoroutine(Pop(messageBox, 0.35f));
    }

    void ShowCongrats()
    {
        congrats.SetActive(true);
        congrats.transform.SetAsLastSibling();
        fx.SetAsLastSibling();
        StartCoroutine(Pop((RectTransform)congrats.transform.GetChild(0), 0.5f, 1.35f));
        Confetti(110);
        SetMessage(MsgAllDone, "ok", true);
        audioSource.PlayOneShot(Sfx.Win());
        MinigameKit.ResultThenExit(this, canvasRoot, Leave, MinigameKit.ResultHold, 1.5f);   // the game ends by itself
    }

    // once: the back button and the result hold can both ask
    void Leave()
    {
        if (leaving) return;
        if (!Application.CanStreamedLevelBeLoaded(floorScene))
        {
            Debug.LogError($"FourthFloorGame: '{floorScene}' is not in Build Settings. Run Tools > Elevator Game > Create Fourth Floor Game Scene.");
            return;
        }
        leaving = true;
        SceneManager.LoadScene(floorScene);
    }

    /* ---------- effects ---------- */
    void Confetti(int count)
    {
        string[] colors = { "#E4392B", "#FFC83D", "#2F6FDE", "#2E9E5B", "#D63C86", "#FFF8E8", "#7B4FD0" };
        for (int i = 0; i < count; i++)
        {
            var piece = UiKit.Box("Confetti", fx, UiKit.Hex(colors[i % colors.Length]), 2f).rectTransform;
            piece.anchorMin = piece.anchorMax = new Vector2(UnityEngine.Random.value, 1f);
            piece.sizeDelta = new Vector2(10f, 16f);
            piece.anchoredPosition = new Vector2(0f, 24f);
            StartCoroutine(Fall(piece, UnityEngine.Random.value * 160f - 80f, 360f + UnityEngine.Random.value * 720f,
                2.6f + UnityEngine.Random.value * 1.8f, UnityEngine.Random.value * 0.8f));
        }
    }

    IEnumerator Fall(RectTransform piece, float dx, float rot, float dur, float delay)
    {
        yield return new WaitForSeconds(delay);
        float h = canvasRoot.rect.height * 1.12f;
        for (float t = 0f; t < dur; t += Time.deltaTime)
        {
            float p = t / dur;
            piece.anchoredPosition = new Vector2(dx * p, 24f - h * p);
            piece.localEulerAngles = new Vector3(0f, 0f, -rot * p);
            yield return null;
        }
        Destroy(piece.gameObject);
    }

    // 12 ✦ bursting out of the stall
    void Sparkle(RectTransform around)
    {
        Vector2 centre = fx.InverseTransformPoint(around.TransformPoint(around.rect.center));
        for (int i = 0; i < 12; i++)
        {
            var s = UiKit.Label(fx, "✦", 28, Gold);
            s.horizontalOverflow = HorizontalWrapMode.Overflow;
            var rt = s.rectTransform;
            rt.sizeDelta = new Vector2(30f, 30f);
            float a = Mathf.PI * 2f * i / 12f, d = 50f + UnityEngine.Random.value * 50f;
            StartCoroutine(Burst(s, centre, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d));
        }
    }

    static IEnumerator Burst(Text s, Vector2 from, Vector2 delta)
    {
        for (float t = 0f; t < 0.75f; t += Time.deltaTime)
        {
            float p = 1f - Mathf.Pow(1f - t / 0.75f, 2f);
            s.rectTransform.anchoredPosition = from + delta * p;
            s.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.4f, 1.2f, p);
            var c = s.color;
            c.a = 1f - p;
            s.color = c;
            yield return null;
        }
        Destroy(s.gameObject);
    }

    // transform 0.28s cubic-bezier(0.3, 1.3, 0.5, 1): back over the card, straightened, then gone
    IEnumerator FlyBack(RectTransform g, Card c)
    {
        if (g != null)
        {
            Vector2 from = g.anchoredPosition, to = fx.InverseTransformPoint(c.root.position);
            Vector3 fromScale = g.localScale;
            float fromRot = -4f;
            for (float t = 0f; t < 0.3f; t += Time.deltaTime)
            {
                float p = Mathf.Clamp01(t / 0.28f);
                float e = 1f - Mathf.Pow(1f - p, 3f) + 0.12f * Mathf.Sin(p * Mathf.PI);   // slight overshoot
                g.anchoredPosition = Vector2.LerpUnclamped(from, to, e);
                g.localScale = Vector3.Lerp(fromScale, Vector3.one, p);
                g.localEulerAngles = new Vector3(0f, 0f, Mathf.Lerp(fromRot, 0f, p));
                yield return null;
            }
            Destroy(g.gameObject);
        }
        if (c.root != null) c.group.alpha = 1f;
    }

    // @keyframes pop: .9 → 1.05 → 1
    static IEnumerator Pop(RectTransform rt, float dur, float size = 1f)
    {
        for (float t = 0f; t < dur; t += Time.deltaTime)
        {
            float p = t / dur;
            rt.localScale = Vector3.one * size * (p < 0.6f ? Mathf.Lerp(0.9f, 1.05f, p / 0.6f) : Mathf.Lerp(1.05f, 1f, (p - 0.6f) / 0.4f));
            yield return null;
        }
        rt.localScale = Vector3.one * size;
    }

    // @keyframes bump: 1.35 at the middle
    static IEnumerator Bump(RectTransform rt)
    {
        for (float t = 0f; t < 0.4f; t += Time.deltaTime)
        {
            rt.localScale = Vector3.one * (1f + 0.35f * Mathf.Sin(t / 0.4f * Mathf.PI));
            yield return null;
        }
        rt.localScale = Vector3.one;
    }

    // @keyframes landIn: scale .3 → 1 with an overshoot
    static IEnumerator LandIn(RectTransform rt)
    {
        for (float t = 0f; t < 0.5f && rt != null; t += Time.deltaTime)
        {
            float q = t / 0.5f - 1f;
            rt.localScale = Vector3.one * Mathf.LerpUnclamped(0.3f, 1f, 1f + 2.70158f * q * q * q + 1.70158f * q * q);
            yield return null;
        }
        if (rt != null) rt.localScale = Vector3.one;
    }

    // @keyframes checkPop: from scale 0, rotate -40°
    static IEnumerator CheckPop(RectTransform rt)
    {
        for (float t = 0f; t < 0.55f; t += Time.deltaTime)
        {
            float q = t / 0.55f - 1f, e = 1f + 3.2f * q * q * q + 2.2f * q * q;
            rt.localScale = Vector3.one * Mathf.Max(0f, e);
            rt.localEulerAngles = new Vector3(0f, 0f, 40f * (1f - Mathf.Clamp01(t / 0.55f)));
            yield return null;
        }
        rt.localScale = Vector3.one;
        rt.localEulerAngles = Vector3.zero;
    }

    static IEnumerator Later(float seconds, Action action)
    {
        yield return new WaitForSeconds(seconds);
        action();
    }

    // @keyframes shake: -8, 8, -5, 5, 0 px over .5s
    static float Shake(float until)
    {
        float left = until - Time.time;
        if (left <= 0f) return 0f;
        float p = 1f - left / 0.5f;
        float[] k = { 0f, -8f, 8f, -5f, 5f, 0f };
        float f = p * 5f;
        int i = Mathf.Min(4, Mathf.FloorToInt(f));
        return Mathf.Lerp(k[i], k[i + 1], f - i);
    }

    /* ---------- per frame: lanterns, stall and card states (the CSS classes) ---------- */
    void Update()
    {
        var es = EventSystem.current;
        if (es != null && es.currentSelectedGameObject != null) es.SetSelectedGameObject(null);

        // @keyframes sway: ±4° from 12 px above the lantern
        for (int i = 0; i < lanterns.Count; i++)
        {
            float dur = i % 3 == 2 ? 5.1f : 4.2f, phase = i % 2 == 1 ? 2f : 0f;
            float s = Mathf.PingPong((Time.time + phase) / dur, 1f);
            lanterns[i].localEulerAngles = new Vector3(0f, 0f, Mathf.Lerp(4f, -4f, Mathf.SmoothStep(0f, 1f, s)));
        }

        bool hasSelection = selected != null;
        float k = 1f - Mathf.Exp(-Time.deltaTime * 20f);
        foreach (var b in booths)
        {
            bool ready = b == hoverBooth;                                   // .drop-ready while a card is dragged over it
            bool hover = b.pointer.hovered && dragging == null;             // .booth:hover
            bool hint = Time.time < b.hintUntil, ok = Time.time < b.okUntil;
            float lift = ready ? 6f : hover ? 3f : 0f;
            float scale = 1f;
            if (ready) scale = 1.04f;
            else if (ok) scale = 1f + 0.05f * Mathf.Sin(Mathf.Clamp01((0.7f - (b.okUntil - Time.time)) / 0.7f) * Mathf.PI);
            else if (hint)
            {
                float t = 2.8f - (b.hintUntil - Time.time);                // hintPulse: 0.9 s, three times
                if (t < 2.7f) scale = 1f + 0.05f * Mathf.Sin((t % 0.9f) / 0.9f * Mathf.PI);
            }
            b.body.localScale = Vector3.Lerp(b.body.localScale, Vector3.one * scale, k);
            b.body.anchoredPosition = new Vector2(Shake(b.shakeUntil), Mathf.Lerp(b.body.anchoredPosition.y, lift, k));

            // the drop-shadow filters on .booth-art
            Color glow = Color.clear;
            if (ok)
            {
                float p = Mathf.Clamp01((0.7f - (b.okUntil - Time.time)) / 0.7f);   // artOk: strongest at 40%
                glow = new Color(Mint.r, Mint.g, Mint.b, p < 0.4f ? p / 0.4f : 1f - (p - 0.4f) / 0.6f);
            }
            else if (hint) glow = Gold;
            else if (ready) glow = Gold;
            else if (b.complete) glow = new Color(Done.r, Done.g, Done.b, 0.67f);
            else if (hasSelection) glow = new Color(Gold.r, Gold.g, Gold.b, 0.6f);
            b.glow.color = Color.Lerp(b.glow.color, glow, k);
        }

        float m = 1f - Mathf.Exp(-Time.deltaTime * 14f);
        foreach (var c in cards)
        {
            if (c.placed) continue;
            c.root.anchoredPosition = Vector2.Lerp(c.root.anchoredPosition, c.target, m);
            bool sel = c == selected && dragging != c;
            // .card:hover lifts 4 px; .card.selected lifts 10 px and grows to 1.07 with a gold glow
            float lift = sel ? 10f : c.pointer.hovered && dragging == null ? 4f : 0f;
            c.face.anchoredPosition = new Vector2(Shake(c.shakeUntil), Mathf.Lerp(c.face.anchoredPosition.y, lift, k));
            // pointed at: zoomed up to read, drawn over its neighbours
            bool hover = c.pointer.hovered && dragging == null;
            if (hover && c.root.GetSiblingIndex() != c.root.parent.childCount - 1) c.root.SetAsLastSibling();
            float zoom = hover ? CardHoverZoom : sel ? 1.07f : 1f;
            c.face.localScale = Vector3.Lerp(c.face.localScale, Vector3.one * zoom, k);
            c.glow.color = sel ? Gold : Color.clear;
        }
    }
}
