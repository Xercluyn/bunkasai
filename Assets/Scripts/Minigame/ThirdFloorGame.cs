using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Unity port of Assets/Minigame/ThirdFloorGame (index.html + script.js + style.css): see an animal, hear its
/// English name and pick it from three words within 14 seconds; three rounds, two right clears the quiz.
/// Opens on the shared how-to-play card and a 3-2-1 countdown (every replay counts down too); the ☰ menu and
/// its how-to-play card pause the round timer. Builds its whole UI at runtime. The animal pictures come from
/// EmojiSheet.png (the colour emoji, 6×3 cells of 256 px), the voice from Speech (Windows' system voices) and
/// the effects from Sfx. Clearing the quiz clears the floor's quest, holds the result and returns to the floor.
/// </summary>
public class ThirdFloorGame : MonoBehaviour
{
    // easy mode: 3 questions, 14 s each, 2 right clears it (was 5, 5 s, all 5)
    const int ROUNDS = 3, PASS = 2;
    const float TIME = 14f, NEXT = 1.9f;
    const int WARN_FROM = 3;   // red timer and a beep each second from here down

    [Tooltip("Assets/Minigame/ThirdFloorGame/EmojiSheet.png")]
    public Texture2D emojiSheet;

    [Tooltip("The 3D floor scene this game returns to; its quest is cleared by a perfect score.")]
    [SceneName] public string floorScene = "ThirdFloorScene";

    // Emoji sheet cells, left to right, top to bottom
    enum E { Cow, Cat, Mouse, Dog, Chicken, Pig, Horse, Sheep, Duck, Rabbit, Trophy, Seedling, Speaker, Party, Star, Confetti, Sparkles, Glow }

    static readonly (string w, E e)[] POOL =
    {
        ("Cow", E.Cow), ("Cat", E.Cat), ("Mouse", E.Mouse), ("Dog", E.Dog), ("Chicken", E.Chicken),
        ("Pig", E.Pig), ("Horse", E.Horse), ("Sheep", E.Sheep), ("Duck", E.Duck), ("Rabbit", E.Rabbit)
    };

    // style.css palette
    static readonly Color Bg1 = UiKit.Hex("#c9e7ff"), Bg2 = UiKit.Hex("#eaf6ff"), Ink = UiKit.Hex("#24456e"),
        Ink2 = UiKit.Hex("#5b82ad"), LineC = UiKit.Hex("#a9d3f3"), Ok = UiKit.Hex("#5fd39b"), BadC = UiKit.Hex("#ff8a8a"),
        Gold = UiKit.Hex("#ffd36b"), Sun = UiKit.Hex("#fff1b8"), Ledge = UiKit.Hex("#b3d9f5");
    static readonly (Color c, Color cd)[] OptColors =
    {
        (UiKit.Hex("#c6f2e1"), UiKit.Hex("#85d8b8")), (UiKit.Hex("#ffd9e0"), UiKit.Hex("#f4a6b7")),
        (UiKit.Hex("#fff1b8"), UiKit.Hex("#f0d070"))
    };

    class Question { public string w; public E e; public string res = ""; }

    class Opt
    {
        public string w;
        public RectTransform root;
        public Image top;
        public Shadow ledge;
        public Text text;
        public int index;
    }

    List<Question> q = new List<Question>();
    int idx, score, gen, lastWarn;
    bool locked, muted, timing;
    bool held, menuOpen, helpOpen, intro, leaving;   // held: the round timer is paused by the menu / help card
    float timerStart, frozenLeft;
    readonly List<Opt> opts = new List<Opt>();
    Sprite[] cells;

    // UI
    RectTransform canvasRoot, column, quiz, end, optsBox, emojiRt, menu, cloudsLayer;
    Image[] dots;
    Image ring, emoji, trophy;
    Text tnum, endTitle, endMsg, muteLbl;
    GameObject menuBlocker, homeBtn, againBtn, backBtn;
    Coroutine popRoutine;
    AudioSource sfx;
    readonly List<(RectTransform rt, float speed, float offset)> clouds = new List<(RectTransform, float, float)>();
    readonly List<(RectTransform rt, float x, float dur, float delay, float born)> falling = new List<(RectTransform, float, float, float, float)>();

    /* ---------- setup ---------- */
    void Awake()
    {
        // the floor scene locks the cursor for mouse look; this game needs it
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        cells = new Sprite[18];
        if (emojiSheet != null)
            for (int i = 0; i < cells.Length; i++)
            {
                int col = i % 6, row = i / 6;
                cells[i] = Sprite.Create(emojiSheet, new Rect(col * 256, emojiSheet.height - (row + 1) * 256, 256, 256),
                    new Vector2(0.5f, 0.5f), 100f);
            }
        else Debug.LogWarning("ThirdFloorGame: Emoji Sheet not set; run Tools > Elevator Game > Create Third Floor Game Scene.");

        sfx = MinigameKit.Audio(gameObject);
        UiKit.EnsureEventSystem();
        BuildUi();

        // nothing runs until the how-to-play card is closed and the countdown is over
        quiz.gameObject.SetActive(false);
        intro = true;
        MinigameKit.HowToPlay(canvasRoot, "どうぶつ えいご クイズ", "Animal English Quiz", Rules(), () =>
        {
            intro = false;
            Begin();
        });
        backBtn.transform.SetAsLastSibling();   // ← 戻る still works over the card
    }

    static (string ja, string en)[] Rules() => new[]
    {
        ("絵を見て、その動物の英語の名前を選ぼう。", "Look at the picture and pick the animal's English name."),
        ($"問題は{ROUNDS}問。1問{(int)TIME}秒だよ。", $"{ROUNDS} questions, {(int)TIME} seconds each."),
        ($"{PASS}問正解でクリア！", $"Get {PASS} right to clear it!"),
        ("絵をタップすると英語が聞けるよ。", "Tap the picture to hear the word."),
    };

    Sprite Cell(E e) => cells[(int)e];

    void BuildUi()
    {
        var canvas = UiKit.MakeCanvas("ThirdFloorGameCanvas", 0, new Vector2(900f, 740f));
        canvasRoot = (RectTransform)canvas.transform;

        // body: linear-gradient(bg1, bg2) and drifting clouds
        var sky = UiKit.Img("Sky", canvasRoot, UiKit.MakeSprite(64, (x, y, s) => Color.Lerp(Bg2, Bg1, y / s)), Color.white);
        UiKit.Stretch(sky.rectTransform);
        cloudsLayer = UiKit.Stretch(UiKit.Rect("Clouds", canvasRoot));
        Cloud(110f, 34f, 0.08f, 0.85f, 80f, 30f);
        Cloud(150f, 44f, 0.42f, 0.6f, 110f, 70f);
        Cloud(90f, 28f, 0.78f, 0.7f, 95f, 10f);

        const float W = 460f;
        column = UiKit.Rect("App", canvasRoot);
        UiKit.Center(column, W, 690f);

        // header: title card and the ☰ menu button
        var title = Card(column, 0f, 0f, W - 60f, 72f, 26f, 5f);
        var tt = UiKit.Label(title, L.T("かわいい どうぶつの\nなまえを まなぼう", "Learn the names of\ncute animals"), 21, Ink);
        tt.lineSpacing = 1.1f;
        UiKit.Stretch(tt.rectTransform);
        var menuBtn = Pressable(column, W - 50f, 8f, 50f, 55f, Color.white, Ledge, 18f, 5f, "☰", 24, Ink, out _, out _, out _);
        menuBtn.onClick.AddListener(() =>
        {
            if (menu.gameObject.activeSelf) CloseMenu();
            else
            {
                var r = (RectTransform)menuBtn.transform;
                OpenMenu(r.TransformPoint(new Vector3(r.rect.xMax - 210f, r.rect.yMin - 8f)));
            }
        });

        // quiz: dots + timer, hint, picture, options
        quiz = UiKit.Rect("Quiz", column);
        UiKit.Stretch(quiz);
        dots = new Image[ROUNDS];
        for (int i = 0; i < ROUNDS; i++)
        {
            dots[i] = UiKit.Img("Dot", quiz, UiKit.Circle, LineC);
            UiKit.Place(dots[i].rectTransform, i * 28f, 107f, 20f, 20f);
            var inner = UiKit.Img("Fill", dots[i].transform, UiKit.Circle, Color.white);
            UiKit.Stretch(inner.rectTransform, 3f);
        }
        var timer = UiKit.Img("Timer", quiz, UiKit.Circle, Ledge);
        UiKit.Place(timer.rectTransform, W - 62f, 86f, 62f, 66f);
        var timerFace = UiKit.Img("Face", timer.transform, UiKit.Circle, Color.white);
        UiKit.Place(timerFace.rectTransform, 0f, 0f, 62f, 62f);
        var track = UiKit.Img("Track", timerFace.transform, UiKit.Ring, UiKit.Hex("#e1f0fc"));
        UiKit.Stretch(track.rectTransform, 5f);
        ring = UiKit.Img("Ring", timerFace.transform, UiKit.Ring, Ink);
        UiKit.Stretch(ring.rectTransform, 5f);
        ring.type = Image.Type.Filled;
        ring.fillMethod = Image.FillMethod.Radial360;
        ring.fillOrigin = (int)Image.Origin360.Top;
        ring.fillClockwise = false;   // the stroke shrinks back toward 12 o'clock, like the SVG dash offset
        tnum = UiKit.Label(timerFace.transform, ((int)TIME).ToString(), 28, Ink);
        UiKit.Stretch(tnum.rectTransform);

        var hint = UiKit.Label(quiz, L.T("絵をタップして音を聞こう。正しい答えを選んでね。", "Tap the picture to hear it. Pick the right answer!"), 18, Ink2);
        hint.resizeTextForBestFit = true;   // the English line is long: shrink rather than clip
        hint.resizeTextMinSize = 15;
        hint.resizeTextMaxSize = 18;
        UiKit.Place(hint.rectTransform, 0f, 162f, W, 28f);

        // picture card: sun, animal, speaker
        var pic = Card(quiz, 0f, 200f, W, 262f, 36f, 8f);
        var picHit = pic.parent.GetComponent<Image>();
        picHit.raycastTarget = true;
        var picBtn = pic.parent.gameObject.AddComponent<Button>();
        picBtn.transition = Selectable.Transition.None;
        picBtn.targetGraphic = picHit;
        picBtn.onClick.AddListener(PlaySound);
        UiKit.Center(UiKit.Img("SunHalo", pic, UiKit.Circle, UiKit.Hex("#fff7d6")).rectTransform, 240f, 240f);
        UiKit.Center(UiKit.Img("Sun", pic, UiKit.Circle, Sun).rectTransform, 134f, 134f);
        emoji = UiKit.Img("Animal", pic, null, Color.white);
        emoji.preserveAspect = true;
        emojiRt = UiKit.Center(emoji.rectTransform, 190f, 190f);
        var spk = UiKit.Img("Speaker", pic, UiKit.Circle, Bg2);
        var srt = spk.rectTransform;
        srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(1f, 0f);
        srt.anchoredPosition = new Vector2(-16f, 12f);
        srt.sizeDelta = new Vector2(46f, 46f);
        UiKit.Center(UiKit.Img("Icon", srt, Cell(E.Speaker), Color.white).rectTransform, 30f, 30f);

        optsBox = UiKit.Rect("Opts", quiz);
        UiKit.Place(optsBox, 0f, 480f, W, 200f);

        // end card
        var endCard = Card(column, 0f, 92f, W, 590f, 36f, 8f);
        end = (RectTransform)endCard.parent;
        trophy = UiKit.Img("Trophy", endCard, null, Color.white);
        trophy.preserveAspect = true;
        var trt = trophy.rectTransform;
        trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0.5f, 1f);
        trt.anchoredPosition = new Vector2(0f, -60f);
        trt.sizeDelta = new Vector2(150f, 150f);
        endTitle = UiKit.Label(endCard, "", 42, Ink);
        endTitle.resizeTextForBestFit = true;   // 「ざんねん…もう一回！」 / "So close, try again!" are wide
        endTitle.resizeTextMinSize = 26;
        endTitle.resizeTextMaxSize = 42;
        Top(endTitle.rectTransform, 230f, 56f, W - 40f);
        endMsg = UiKit.Label(endCard, "", 19, Ink2);
        endMsg.lineSpacing = 1.3f;
        Top(endMsg.rectTransform, 296f, 90f, W - 60f);
        var again = Pressable(endCard, 0f, 0f, 260f, 56f, Gold, UiKit.Hex("#e0a93a"), 28f, 6f, L.T("もう一度あそぶ", "Play again"), 22, UiKit.Hex("#6a4400"), out _, out _, out _);
        Top((RectTransform)again.transform, 400f, 50f, 260f);
        again.onClick.AddListener(Begin);
        againBtn = again.gameObject;
        var home = Pressable(endCard, 0f, 0f, 260f, 56f, Ok, UiKit.Hex("#33a874"), 28f, 6f, L.T("フロアに戻る", "Back to the floor"), 22, Color.white, out _, out _, out _);
        Top((RectTransform)home.transform, 474f, 50f, 260f);
        home.onClick.AddListener(Leave);
        homeBtn = home.gameObject;
        end.gameObject.SetActive(false);

        BuildMenu();

        // back to the 3D floor, always on top
        var back = UiKit.ColorButton(canvasRoot, L.T("← 戻る", "← Back"), Color.white, Ink, Ledge, 46f, 18, out var backText);
        UiKit.Place((RectTransform)back.transform, 16f, 12f, backText.preferredWidth + 52f, 46f);
        back.onClick.AddListener(Leave);
        backBtn = back.gameObject;
    }

    // White card with a solid ledge underneath (box-shadow: 0 Npx 0 #b3d9f5). Returns the white face.
    static RectTransform Card(RectTransform parent, float x, float y, float w, float h, float radius, float ledge)
    {
        var shadow = UiKit.Box("Card", parent, Ledge, radius);
        UiKit.Place(shadow.rectTransform, x, y, w, h + ledge);
        var face = UiKit.Box("Face", shadow.transform, Color.white, radius);
        UiKit.Place(face.rectTransform, 0f, 0f, w, h);
        return face.rectTransform;
    }

    static void Top(RectTransform rt, float y, float h, float w)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -y);
        rt.sizeDelta = new Vector2(w, h);
    }

    // Button with a coloured face on a darker ledge (border-bottom) that it sinks into while pressed; h includes the ledge
    static Button Pressable(RectTransform parent, float x, float y, float w, float h, Color face, Color ledge,
        float radius, float depth, string label, int size, Color ink, out Text text, out Image top, out Shadow ledgeShadow)
    {
        var root = UiKit.Rect("Button", parent);
        UiKit.Place(root, x, y, w, h - depth);
        var hit = root.gameObject.AddComponent<Image>();
        hit.color = Color.clear;
        var button = root.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = hit;
        var faceRt = UiKit.Stretch(UiKit.Rect("Face", root));
        top = UiKit.Box("Top", faceRt, face, radius);
        UiKit.Stretch(top.rectTransform);
        ledgeShadow = UiKit.HardShadow(top, ledge, depth);
        text = UiKit.Label(faceRt, label, size, ink);
        UiKit.Stretch(text.rectTransform);
        var fx = root.gameObject.AddComponent<UiPressFeedback>();
        fx.face = faceRt;
        fx.button = button;
        fx.shadow = ledgeShadow;
        fx.shadowDepth = depth;
        fx.depth = depth * 0.45f;
        return button;
    }

    // .cloud: a white pill with two round puffs on top, drifting left to right
    void Cloud(float w, float h, float top, float alpha, float duration, float delay)
    {
        var c = UiKit.Rect("Cloud", cloudsLayer);
        c.anchorMin = c.anchorMax = new Vector2(0f, 1f - top);
        c.pivot = Vector2.zero;
        c.sizeDelta = new Vector2(w, h);
        var col = new Color(1f, 1f, 1f, alpha);
        UiKit.Stretch(UiKit.Box("Body", c, col, h * 0.5f).rectTransform);
        Puff(c, col, w * 0.13f, h, w * 0.45f);
        Puff(c, col, w * 0.53f, h, w * 0.34f);
        clouds.Add((c, 1f / duration, delay / duration));
    }

    // circle of diameter d centred at (x + d/2, y): half of it pokes out above the body
    static void Puff(RectTransform cloud, Color col, float x, float y, float d)
    {
        var p = UiKit.Img("Puff", cloud, UiKit.Circle, col).rectTransform;
        p.anchorMin = p.anchorMax = p.pivot = Vector2.zero;
        p.anchoredPosition = new Vector2(x, y - d * 0.5f);
        p.sizeDelta = new Vector2(d, d);
    }

    void BuildMenu()
    {
        // full-screen catcher: a click outside the menu closes it
        var blocker = UiKit.Img("MenuBlocker", canvasRoot, null, Color.clear);
        blocker.raycastTarget = true;
        UiKit.Stretch(blocker.rectTransform);
        var bb = blocker.gameObject.AddComponent<Button>();
        bb.transition = Selectable.Transition.None;
        bb.onClick.AddListener(CloseMenu);
        menuBlocker = blocker.gameObject;

        var box = UiKit.Box("Menu", canvasRoot, Color.white, 20f);
        menu = box.rectTransform;
        menu.anchorMin = menu.anchorMax = new Vector2(0.5f, 0.5f);
        menu.pivot = new Vector2(0f, 1f);
        menu.sizeDelta = new Vector2(220f, 4 * 46f + 12f);
        UiKit.HardShadow(box, Ledge, 6f);
        MenuItem(L.T("もう一度 きく", "Hear it again"), 0, out _, () => PlaySound());
        MenuItem(L.T("おとを OFFにする", "Sound OFF"), 1, out muteLbl, ToggleMute);
        MenuItem(L.T("はじめから", "Start over"), 2, out _, () => { Speech.Stop(); Begin(); });
        MenuItem(L.T("あそびかた", "How to play"), 3, out _, ShowHelp);
        menu.gameObject.SetActive(false);
        menuBlocker.SetActive(false);
    }

    void MenuItem(string label, int i, out Text text, Action action)
    {
        var row = UiKit.Box("Item", menu, Color.white, 14f);
        row.raycastTarget = true;
        UiKit.Place(row.rectTransform, 6f, 6f + i * 46f, 208f, 44f);
        var b = row.gameObject.AddComponent<Button>();
        var colors = b.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = Bg2;
        colors.pressedColor = Bg1;
        colors.selectedColor = Color.white;
        b.colors = colors;
        b.targetGraphic = row;
        b.onClick.AddListener(() => { CloseMenu(); action(); });
        text = UiKit.Label(row.transform, label, 18, Ink, TextAnchor.MiddleLeft);
        UiKit.Place(text.rectTransform, 14f, 0f, 190f, 44f);
    }

    /* ---------- menu, help (both pause the round timer) ---------- */
    void OpenMenu(Vector3 worldPos)
    {
        if (intro || helpOpen || leaving) return;   // not over the how-to-play card or the countdown
        menuOpen = true;
        HoldTimer();
        menuBlocker.SetActive(true);
        menuBlocker.transform.SetAsLastSibling();
        menu.gameObject.SetActive(true);
        menu.SetAsLastSibling();
        // keep it inside the screen
        Vector2 local = canvasRoot.InverseTransformPoint(worldPos);
        var half = canvasRoot.rect.size * 0.5f;
        local.x = Mathf.Clamp(local.x, -half.x + 8f, half.x - menu.sizeDelta.x - 8f);
        local.y = Mathf.Clamp(local.y, -half.y + menu.sizeDelta.y + 8f, half.y - 8f);
        menu.anchoredPosition = local;
    }

    void CloseMenu()
    {
        menu.gameObject.SetActive(false);
        menuBlocker.SetActive(false);
        if (!menuOpen) return;
        menuOpen = false;
        ReleaseTimer();
    }

    // the same how-to-play card as the start; its スタート！ carries on with the paused round
    void ShowHelp()
    {
        helpOpen = true;
        HoldTimer();
        MinigameKit.HowToPlay(canvasRoot, "どうぶつ えいご クイズ", "Animal English Quiz", Rules(), () =>
        {
            helpOpen = false;
            ReleaseTimer();
        });
        backBtn.transform.SetAsLastSibling();
    }

    void HoldTimer()
    {
        if (!timing) return;
        StopTimer();
        held = true;
    }

    void ReleaseTimer()
    {
        if (!held || menuOpen || helpOpen) return;
        held = false;
        timing = true;
        timerStart = Time.time - (TIME - frozenLeft);
    }

    void ToggleMute()
    {
        muted = !muted;
        muteLbl.text = muted ? L.T("おとを ONにする", "Sound ON") : L.T("おとを OFFにする", "Sound OFF");
        if (muted) Speech.Stop();
        Toast.Show(muted ? L.T("おとを けしたよ", "Sound is off") : L.T("おとを ONにしたよ", "Sound is on"), UiKit.Fg);
    }

    /* ---------- speech ---------- */
    void Say(string t, bool force = false)
    {
        if (muted && !force) return;
        Speech.Say(t, "en", -1);
    }

    // The follow-up line after the English word: Japanese, or English in English mode
    void SayJa(string ja, string en)
    {
        if (muted) return;
        if (L.English) Speech.Say(en, "en", -1, false);
        else Speech.Say(ja, "ja", -1, false);   // queued after the English line, like speechSynthesis
    }

    void PlaySound()
    {
        if (idx < q.Count) Say(q[idx].w, true);
    }

    /* ---------- quiz (script.js) ---------- */
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

    void Dots()
    {
        for (int i = 0; i < ROUNDS; i++)
        {
            var res = i < q.Count ? q[i].res : "";
            Color border = res == "ok" ? UiKit.Hex("#33a874") : res == "bad" ? UiKit.Hex("#d65a5a") : i == idx ? Ink : LineC;
            Color fill = res == "ok" ? Ok : res == "bad" ? BadC : i == idx ? Sun : Color.white;
            dots[i].color = border;
            dots[i].transform.GetChild(0).GetComponent<Image>().color = fill;
        }
    }

    void StartTimer()
    {
        lastWarn = 0;
        if (menuOpen || helpOpen)
        {
            // the next question came up while paused: it waits with a full clock
            timing = false;
            held = true;
            frozenLeft = TIME;
            tnum.text = ((int)TIME).ToString();
            ring.color = tnum.color = Ink;
            return;
        }
        timing = true;
        timerStart = Time.time;
    }

    void StopTimer()
    {
        if (!timing) return;
        timing = false;
        frozenLeft = Mathf.Max(0f, TIME - (Time.time - timerStart));
    }

    // 3-2-1, then a fresh quiz (first start, もう一度あそぶ, はじめから)
    void Begin()
    {
        if (intro) return;
        intro = true;
        gen++;   // cancels pending next() calls from the last game
        timing = held = false;
        foreach (var f in falling) Destroy(f.rt.gameObject);
        falling.Clear();
        end.gameObject.SetActive(false);
        quiz.gameObject.SetActive(false);
        MinigameKit.Countdown(this, canvasRoot, () =>
        {
            intro = false;
            StartQuiz();
        });
    }

    void StartQuiz()
    {
        gen++;   // cancels pending next() calls from the last game
        timing = held = false;
        q = Shuffle(POOL).Take(ROUNDS).Select(a => new Question { w = a.w, e = a.e }).ToList();
        idx = 0;
        score = 0;
        locked = false;
        foreach (var f in falling) Destroy(f.rt.gameObject);
        falling.Clear();
        end.gameObject.SetActive(false);
        quiz.gameObject.SetActive(true);
        Show();
    }

    void Show()
    {
        locked = false;
        Dots();
        var cur = q[idx];
        StartTimer();
        emoji.sprite = Cell(cur.e);
        if (popRoutine != null) StopCoroutine(popRoutine);
        popRoutine = StartCoroutine(Pop());

        foreach (var o in opts) Destroy(o.root.gameObject);
        opts.Clear();
        var wrong = Shuffle(POOL.Where(p => p.w != cur.w)).Take(2).Select(p => p.w);
        int i = 0;
        foreach (var w in Shuffle(wrong.Prepend(cur.w)))
        {
            var (c, cd) = OptColors[i];
            var b = Pressable(optsBox, 0f, i * 66f, optsBox.sizeDelta.x, 56f, c, cd, 24f, 7f, w, 30, Ink, out var text, out var top, out var ledge);
            var o = new Opt { w = w, root = (RectTransform)b.transform, top = top, ledge = ledge, text = text, index = i };
            b.onClick.AddListener(() => Choose(o, cur));
            opts.Add(o);
            i++;
        }
        int g = gen;
        StartCoroutine(Later(0.25f, () => { if (g == gen && idx < q.Count && q[idx] == cur) Say(cur.w); }));
    }

    void Lock(Question cur)
    {
        // buttons stay enabled-looking (style.css doesn't fade them); `locked` ignores further clicks
        foreach (var o in opts)
            if (o.w == cur.w) SetOpt(o, true, false);
    }

    void TimeUp(Question cur)
    {
        if (locked) return;
        locked = true;
        Lock(cur);
        cur.res = "bad";
        sfx.PlayOneShot(Sfx.Wrong());
        Say("Time is up! It is a " + cur.w);
        Dots();
        int g = gen;
        StartCoroutine(Later(NEXT, () => { if (g == gen) Next(); }));
    }

    void Next()
    {
        idx++;
        if (idx >= ROUNDS) Finish(); else Show();
    }

    void Choose(Opt o, Question cur)
    {
        if (locked) return;
        locked = true;
        StopTimer();
        Lock(cur);
        if (o.w == cur.w)
        {
            score++;
            cur.res = "ok";
            sfx.PlayOneShot(Sfx.Correct());
            Say("Correct! " + cur.w);
        }
        else
        {
            SetOpt(o, false, true);
            StartCoroutine(Shake(o.root));
            cur.res = "bad";
            sfx.PlayOneShot(Sfx.Wrong());
            Say("It is a " + cur.w);
        }
        Dots();
        int g = gen;
        StartCoroutine(Later(NEXT, () => { if (g == gen) Next(); }));
    }

    // .opt.ok / .opt.bad colours; disabled buttons stay full colour like the CSS
    void SetOpt(Opt o, bool ok, bool bad)
    {
        var (c, cd) = OptColors[o.index];
        o.top.color = ok ? Ok : bad ? BadC : c;
        o.ledge.effectColor = ok ? UiKit.Hex("#33a874") : bad ? UiKit.Hex("#d65a5a") : cd;
        o.text.color = ok || bad ? Color.white : Ink;
    }

    void Finish()
    {
        timing = held = false;
        Dots();
        quiz.gameObject.SetActive(false);
        end.gameObject.SetActive(true);
        bool perfect = score >= PASS;   // cleared
        homeBtn.SetActive(perfect);
        againBtn.SetActive(!perfect);   // a clear leaves on its own
        if (perfect)
        {
            GameProgress.ClearQuest(floorScene);   // the floor's QuestBanner shows クエストクリア！
            trophy.sprite = Cell(E.Trophy);
            endTitle.text = L.T("やったね！", "Congratulations!");
            endMsg.text = L.T($"おめでとう！{ROUNDS}問中{score}問正解！クイズクリア！ ({score} / {ROUNDS})",
                $"Well done! {score} of {ROUNDS} right. Quiz cleared! ({score} / {ROUNDS})");
            Confetti();
            sfx.PlayOneShot(Sfx.Win());
            Say("Congratulations!");
            MinigameKit.ResultThenExit(this, canvasRoot, Leave, MinigameKit.ResultHold);   // the game ends by itself
        }
        else
        {
            trophy.sprite = Cell(E.Seedling);
            endTitle.text = L.T("ざんねん…もう一回！", "So close, try again!");
            endMsg.text = L.T($"{ROUNDS}問中{score}問正解。{PASS}問正解でクリアだよ。もう一度がんばろう！",
                $"{score} of {ROUNDS} right. Get {PASS} right to clear it. Try again!");
            sfx.PlayOneShot(Sfx.Bad());
            Say("So close!");
            SayJa("もう一度がんばろう。", "Let's try again.");
        }
    }

    void Confetti()
    {
        E[] em = { E.Party, E.Star, E.Confetti, E.Sparkles, E.Glow };
        for (int i = 0; i < 28; i++)
        {
            var img = UiKit.Img("Fall", canvasRoot, Cell(em[i % em.Length]), Color.white);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(34f, 34f);
            float x = UnityEngine.Random.value * 0.96f;
            falling.Add((rt, x, 2.5f + UnityEngine.Random.value * 2.5f, UnityEngine.Random.value * 1.2f, Time.time));
            rt.anchoredPosition = new Vector2(-100f, 100f);
        }
    }

    void Leave()
    {
        if (leaving) return;   // the result hold and 「フロアに戻る」 can both fire
        Speech.Stop();
        if (!Application.CanStreamedLevelBeLoaded(floorScene))
        {
            Debug.LogError($"ThirdFloorGame: '{floorScene}' is not in Build Settings. Run Tools > Elevator Game > Create Third Floor Game Scene.");
            return;
        }
        leaving = true;
        SceneManager.LoadScene(floorScene);
    }

    /* ---------- loop ---------- */
    void Update()
    {
        // a clicked button must not stay selected, or Space/Enter would press it again
        var es = UnityEngine.EventSystems.EventSystem.current;
        if (es != null && es.currentSelectedGameObject != null) es.SetSelectedGameObject(null);

        // timer: 14 → 0 in whole seconds, ring shrinking smoothly; red with a beep a second over the last 3
        float left = timing ? Mathf.Max(0f, TIME - (Time.time - timerStart)) : frozenLeft;
        if (timing)
        {
            int n = Mathf.Max(0, (int)TIME - Mathf.FloorToInt(Time.time - timerStart));
            tnum.text = n.ToString();
            bool low = n <= WARN_FROM;
            ring.color = tnum.color = low ? BadC : Ink;
            if (low && n >= 1 && n != lastWarn)
            {
                lastWarn = n;
                sfx.PlayOneShot(Sfx.Warn());
            }
            if (Time.time - timerStart >= TIME)
            {
                StopTimer();
                TimeUp(q[idx]);
            }
        }
        else if (!locked && !held && quiz.gameObject.activeSelf) left = TIME;
        ring.fillAmount = left / TIME;

        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame) CloseMenu();
        if (mouse != null && mouse.rightButton.wasPressedThisFrame)
        {
            // contextmenu: the menu opens at the pointer
            OpenMenu(canvasRoot.TransformPoint(ScreenToCanvas(mouse.position.ReadValue())));
        }

        // clouds drift across, the trophy bobs, confetti falls
        float width = canvasRoot.rect.width;
        foreach (var (rt, speed, offset) in clouds)
        {
            float p = Mathf.Repeat(Time.time * speed + offset, 1f);
            rt.anchoredPosition = new Vector2(-180f + p * (width + 360f), 0f);
        }
        if (end.gameObject.activeSelf)
        {
            float b = Mathf.Sin(Mathf.Repeat(Time.time / 1.2f, 1f) * Mathf.PI);
            trophy.rectTransform.anchoredPosition = new Vector2(0f, -60f + 14f * b);
            trophy.rectTransform.localScale = Vector3.one * (1f + 0.05f * b);
        }
        float height = canvasRoot.rect.height;
        for (int i = falling.Count - 1; i >= 0; i--)
        {
            var f = falling[i];
            float t = (Time.time - f.born - f.delay) / f.dur;
            if (t > 1f) { Destroy(f.rt.gameObject); falling.RemoveAt(i); continue; }
            t = Mathf.Max(0f, t);
            f.rt.anchoredPosition = new Vector2(f.x * width, 40f - t * height * 1.15f);
            f.rt.localEulerAngles = new Vector3(0f, 0f, -360f * t);
        }
    }

    Vector3 ScreenToCanvas(Vector2 screen)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRoot, screen, null, out var local);
        return local;
    }

    // .pic.pop #emoji: scale .6 → 1, ease-out .35s
    IEnumerator Pop()
    {
        for (float t = 0f; t < 0.35f; t += Time.deltaTime)
        {
            float p = 1f - Mathf.Pow(1f - t / 0.35f, 3f);
            emojiRt.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, p);
            yield return null;
        }
        emojiRt.localScale = Vector3.one;
    }

    // @keyframes shake: -8px, +8px over .35s
    static IEnumerator Shake(RectTransform rt)
    {
        float y = rt.anchoredPosition.y;
        for (float t = 0f; t < 0.35f && rt != null; t += Time.deltaTime)
        {
            float p = t / 0.35f;
            float x = p < 0.25f ? Mathf.Lerp(0f, -8f, p / 0.25f)
                    : p < 0.75f ? Mathf.Lerp(-8f, 8f, (p - 0.25f) / 0.5f)
                    : Mathf.Lerp(8f, 0f, (p - 0.75f) / 0.25f);
            rt.anchoredPosition = new Vector2(x, y);
            yield return null;
        }
        if (rt != null) rt.anchoredPosition = new Vector2(0f, y);
    }

    void OnDestroy() => Speech.Stop();

    static IEnumerator Later(float seconds, Action action)
    {
        yield return new WaitForSeconds(seconds);
        action();
    }
}
