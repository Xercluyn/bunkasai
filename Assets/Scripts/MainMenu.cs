using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// The title menu, the first scene of the game. On the right a 3D school building with five
/// floors stands in the night, gently turning with the mouse; pointing at a floor lights its windows, slides it
/// out and sends the elevator car to it, and clicking it rides the elevator to that floor on its own (clearing its
/// quest wins the game, see GameEnding). On the left: the title, 「1階から5階まで遊ぶ」 for the full run, and a card
/// describing the floor under the pointer. A string of cyan and white lights swings across the top. Everything plays in
/// on opening: the title drops in line by line and the building rises. English / 日本語 in the corner switches
/// the language. Every visit starts a fresh session. Pointing at a floor blips (each floor a little higher) and
/// choosing one whooshes. With a gamepad, A / × on 「1階から5階まで遊ぶ」 starts the run, → moves onto the building,
/// ↑ ↓ pick a floor and A / × plays it, ← or B / ○ goes back, and Y / △ switches the language.
/// Everything is built at runtime, so the scene only needs a camera and this component
/// (Tools > Elevator Game > Create Menu Scene makes it).
/// </summary>
public class MainMenu : MonoBehaviour
{
    [Tooltip("The game's name at the top of the menu, one line per row.")]
    [TextArea] public string titleLinesJa = "学園祭\nエレベーター\nゲーム";
    [TextArea] public string titleLinesEn = "School Festival\nElevator Game";

    [Tooltip("The five floor scenes, first floor first.")]
    [SceneName] public string floor1 = "FirstScene";
    [SceneName] public string floor2 = "SecondScene";
    [SceneName] public string floor3 = "ThirdScene";
    [SceneName] public string floor4 = "FourthScene";
    [SceneName] public string floor5 = "FifthScene";

    static (string ja, string en)[] FloorGames => ElevatorRide.FloorGames;

    // the building: five floors of FloorH, the facade facing the camera (−z)
    const float FloorW = 6f, FloorH = 1.5f, FloorD = 4f, SlideOut = 0.45f, RiseFrom = -9f;
    // blue body and slabs, cool-white lit windows, a cyan elevator car in a navy shaft
    static readonly Color Body = UiKit.Panel, BodyLit = UiKit.Line, Slab = UiKit.Hex("#3a6bc9"),
        WindowDark = UiKit.Bg2, WindowLit = UiKit.Light, Shaft = UiKit.Dim,
        Car = UiKit.Accent, Ground = UiKit.Hex("#07122c");
    // floor labels: the game line wraps at LabelGameW, and labels keep ScreenMargin inside the screen edges
    const float LabelW = 170f, LabelH = 84f, LabelGameW = 160f, ScreenMargin = 12f;
    // the left column's width (full-run button and floor card); the language button, upper right
    const float CardW = 480f, LangW = 150f, LangH = 44f, LangRight = 24f, LangBottom = 24f;

    class Floor
    {
        public int n;
        public Transform root;
        public Renderer body;
        public Renderer[] windows;
        public Collider hit;
        public float slide, glow;
        public RectTransform label;
        public Text name, game;
    }

    // something that plays in when the menu opens
    class Entrance
    {
        public CanvasGroup group;
        public RectTransform rt;
        public Vector2 home;
        public float delay;
        public bool drop;   // drops from above with an overshoot; otherwise rises a little and fades
    }

    class Lantern
    {
        public RectTransform pivot;
        public Image glow;
        public float speed, phase;
    }

    readonly Floor[] floors = new Floor[5];
    readonly List<Entrance> entrances = new List<Entrance>();
    readonly List<Lantern> lanterns = new List<Lantern>();
    readonly Text[] titleLines = new Text[3];
    Transform building, car;
    Camera cam;
    int hover;
    float carY, opened, rise, lastBlip;
    bool leaving;

    Text subtitle, runLabel, hintText, infoNumber, infoFloor, infoGame, infoNote, langText;
    GameObject info, hint;
    Image cardBorder, pulse;
    RectTransform labelLayer, pulseRt;
    CanvasGroup labelGroup;

    string[] Floors => new[] { floor1, floor2, floor3, floor4, floor5 };

    void Awake()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        GameProgress.NewSession();
        GameProgress.SingleFloor = "";
        GameProgress.MenuScene = gameObject.scene.name;

        SetUpCamera();
        BuildBuilding();
        BuildUi();
        Write();
        L.Changed += Write;
        opened = Time.unscaledTime;
    }

    void OnDestroy()
    {
        L.Changed -= Write;
        GamepadInput.FocusPaused = false;
    }

    /* ---------- gamepad ---------- */
    Button runButton;
    bool padOnBuilding, textPad;
    int padFloor = 1;

    // → onto the building, ↑ ↓ between its floors, A / × plays the floor, ← or B / ○ back to the full-run button
    // (where A / × is the button's own press); Y / △ switches the language. True when a floor was chosen.
    bool PadInput()
    {
        if (GamepadInput.NorthPressed) L.Toggle();
        var s = GamepadInput.NavStep;
        if (!padOnBuilding)
        {
            if (s.x > 0)
            {
                padOnBuilding = true;
                GamepadInput.FocusPaused = true;   // no button holds the pad meanwhile
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            }
            return false;
        }
        if (s.y != 0) padFloor = Mathf.Clamp(padFloor + s.y, 1, 5);
        if (s.x < 0 || GamepadInput.BackPressed)
        {
            LeaveBuilding(true);
            return false;
        }
        if (GamepadInput.InteractPressed)
        {
            PlayFloor(padFloor);
            return true;
        }
        return false;
    }

    void LeaveBuilding(bool selectRun)
    {
        padOnBuilding = false;
        GamepadInput.FocusPaused = false;
        if (selectRun && EventSystem.current != null && runButton != null) EventSystem.current.SetSelectedGameObject(runButton.gameObject);
    }

    /* ---------- 3D ---------- */
    void SetUpCamera()
    {
        cam = Camera.main;
        if (cam == null) cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)) { tag = "MainCamera" }.GetComponent<Camera>();
        cam.orthographic = false;
        cam.fieldOfView = 34f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = UiKit.Bg;
        cam.transform.position = new Vector3(-2f, 5.2f, -17f);
        cam.transform.LookAt(new Vector3(-2f, 4.3f, 0f));   // the building sits right of centre, clear of the left column

        var sun = new GameObject("Moonlight").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = UiKit.Hex("#e6f0ff");
        sun.intensity = 1.2f;
        sun.transform.rotation = Quaternion.Euler(35f, 25f, 0f);   // from the front, above, a little left
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = UiKit.Hex("#4a6699");
    }

    void BuildBuilding()
    {
        building = new GameObject("Building").transform;
        building.position = new Vector3(0f, RiseFrom, 0f);   // rises into place as the menu opens

        Box("Ground", null, new Vector3(0f, -0.05f, 0f), new Vector3(60f, 0.1f, 40f), Ground);
        Box("Base", building, new Vector3(0f, 0.1f, 0f), new Vector3(FloorW + 0.8f, 0.2f, FloorD + 0.8f), Slab);

        for (int i = 0; i < 5; i++)
        {
            var f = new Floor { n = i + 1 };
            f.root = new GameObject("Floor" + f.n).transform;
            f.root.SetParent(building, false);
            f.root.localPosition = new Vector3(0f, 0.2f + i * FloorH, 0f);
            f.body = Box("Body", f.root, new Vector3(0f, FloorH * 0.5f, 0f), new Vector3(FloorW, FloorH - 0.16f, FloorD), Body);
            f.hit = f.body.GetComponent<Collider>();
            Box("Slab", f.root, new Vector3(0f, FloorH - 0.06f, 0f), new Vector3(FloorW + 0.3f, 0.12f, FloorD + 0.3f), Slab);
            // windows on the facade, two each side of the elevator shaft
            f.windows = new Renderer[4];
            float[] xs = { -2.25f, -1.25f, 1.25f, 2.25f };
            for (int w = 0; w < 4; w++)
                f.windows[w] = Box("Window", f.root, new Vector3(xs[w], FloorH * 0.5f, -FloorD * 0.5f - 0.02f), new Vector3(0.7f, 0.72f, 0.04f), WindowDark);
            floors[i] = f;
        }

        float top = 0.2f + 5 * FloorH;
        Box("Roof", building, new Vector3(0f, top + 0.15f, 0f), new Vector3(FloorW + 0.4f, 0.3f, FloorD + 0.4f), Slab);
        Box("Penthouse", building, new Vector3(-1.2f, top + 0.7f, 0.6f), new Vector3(2f, 0.9f, 1.8f), Body);
        // the elevator: a dark shaft down the middle of the facade and its glowing car
        Box("Shaft", building, new Vector3(0f, 0.2f + top * 0.5f, -FloorD * 0.5f - 0.03f), new Vector3(0.9f, top - 0.2f, 0.04f), Shaft);
        car = Box("Car", building, new Vector3(0f, 0.2f + FloorH * 0.5f, -FloorD * 0.5f - 0.08f), new Vector3(0.62f, 1.0f, 0.06f), Car).transform;
        carY = car.localPosition.y;
    }

    // A cube with its own colour (an instance of the default material, so no shader lookups)
    static Renderer Box(string name, Transform parent, Vector3 pos, Vector3 size, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        if (parent != null) go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = size;
        var r = go.GetComponent<Renderer>();
        r.material.color = color;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (name != "Body") Destroy(go.GetComponent<Collider>());   // only the floors' bodies take the pointer
        return r;
    }

    /* ---------- UI ---------- */
    void BuildUi()
    {
        var canvas = UiKit.MakeCanvas("MenuCanvas", 0, new Vector2(1280f, 720f));
        UiKit.EnsureEventSystem();
        var root = (RectTransform)canvas.transform;

        // floor labels follow the building (behind the rest of the UI)
        labelLayer = UiKit.Stretch(UiKit.Rect("FloorLabels", root));
        labelGroup = labelLayer.gameObject.AddComponent<CanvasGroup>();
        labelGroup.alpha = 0f;
        for (int i = 0; i < 5; i++)
        {
            var f = floors[i];
            f.label = UiKit.Rect("Label" + f.n, labelLayer);
            f.label.anchorMin = f.label.anchorMax = Vector2.zero;
            f.label.pivot = new Vector2(0f, 0.5f);
            f.label.sizeDelta = new Vector2(LabelW, LabelH);
            f.name = UiKit.Display(UiKit.Label(f.label, "", 30, UiKit.Fg, TextAnchor.UpperLeft));
            UiKit.HardShadow(f.name, UiKit.PanelShadow, 3f);
            UiKit.Place(f.name.rectTransform, 0f, 0f, LabelW, 42f);
            // up to two lines, so the label stays narrow enough to fit right of the building
            f.game = UiKit.Label(f.label, "", 14, UiKit.Muted, TextAnchor.UpperLeft);
            UiKit.Place(f.game.rectTransform, 2f, 42f, LabelGameW, 42f);
            UiKit.Fit(f.game, 14, 10);
        }

        BuildLanterns(root);

        // left column: badge, title, subtitle, the full-run button and the floor card
        var badgeRow = UiKit.Rect("Badge", root);
        Left(badgeRow, 104f, 470f, 28f);
        var pill = UiKit.Box("Pill", badgeRow, UiKit.Accent2, 14f);
        UiKit.Place(pill.rectTransform, 0f, 0f, 64f, 28f);
        UiKit.Stretch(UiKit.Label(pill.transform, "読祭", 16, UiKit.Bg).rectTransform);
        var kicker = UiKit.Label(badgeRow, "YOMISAI", 16, UiKit.Muted, TextAnchor.MiddleLeft);
        UiKit.Place(kicker.rectTransform, 78f, 0f, 300f, 28f);
        Enter(badgeRow, 0.1f, false);

        for (int i = 0; i < titleLines.Length; i++)
        {
            var line = UiKit.Display(UiKit.Label(root, "", 68, UiKit.Accent, TextAnchor.UpperLeft));
            line.horizontalOverflow = HorizontalWrapMode.Overflow;
            UiKit.HardShadow(line, UiKit.AccentShadow, 6f);
            Left(line.rectTransform, 138f + i * 74f, 560f, 84f);
            Enter(line.rectTransform, 0.25f + i * 0.15f, true);
            titleLines[i] = line;
        }

        subtitle = UiKit.Label(root, "", 20, UiKit.Muted, TextAnchor.UpperLeft);
        Left(subtitle.rectTransform, 390f, 520f, 30f);
        Enter(subtitle.rectTransform, 0.85f, false);

        // the full-run button, with a ring that pulses out from it now and then
        var runHolder = UiKit.Rect("Run", root);
        Left(runHolder, 438f, CardW, 76f);
        pulse = UiKit.Box("Pulse", runHolder, UiKit.Accent, 18f);
        pulseRt = UiKit.Stretch(pulse.rectTransform);
        pulse.color = Color.clear;
        var run = UiKit.MainButton(runHolder, "", out runLabel);
        UiKit.Fit(runLabel, 28, 18);
        UiKit.Stretch((RectTransform)run.transform);
        run.onClick.AddListener(PlayAll);
        // the gamepad starts here; its stick doesn't wander off through the UI (→ goes to the building instead)
        runButton = run;
        run.navigation = new Navigation { mode = Navigation.Mode.None };
        GamepadInput.SetDefault(run, GamepadInput.OverlayPriority);
        Enter(runHolder, 1.0f, false);

        // card: the floor under the pointer, or how to pick one
        var card = UiKit.Box("FloorCard", root, UiKit.Bg2, 18f, UiKit.Line, 2f);
        cardBorder = card;
        Left(card.rectTransform, 548f, CardW, 112f);
        Enter(card.rectTransform, 1.15f, false);
        var c = card.rectTransform;
        info = UiKit.Rect("Info", c).gameObject;
        UiKit.Stretch((RectTransform)info.transform);
        var tile = UiKit.Box("Number", info.transform, UiKit.Panel, 14f, UiKit.Accent, 2f);
        UiKit.Place(tile.rectTransform, 24f, 22f, 64f, 64f);
        infoNumber = UiKit.Display(UiKit.Label(tile.transform, "", 30, UiKit.Accent));
        UiKit.Stretch(infoNumber.rectTransform);
        // one line each: a long game name or the note shrinks to fit instead of wrapping into the next line
        const float textW = CardW - 106f - 18f;
        infoFloor = UiKit.Label(info.transform, "", 15, UiKit.Accent, TextAnchor.MiddleLeft);
        UiKit.Place(infoFloor.rectTransform, 106f, 16f, textW, 22f);
        UiKit.Fit(infoFloor, 15, 11);
        infoGame = UiKit.Label(info.transform, "", 24, UiKit.Fg, TextAnchor.MiddleLeft);
        UiKit.Place(infoGame.rectTransform, 106f, 39f, textW, 37f);
        UiKit.Fit(infoGame, 24, 15);
        infoNote = UiKit.Label(info.transform, "", 14, UiKit.Muted, TextAnchor.MiddleLeft, false);
        UiKit.Place(infoNote.rectTransform, 106f, 77f, textW, 22f);
        UiKit.Fit(infoNote, 14, 11);
        hint = UiKit.Rect("Hint", c).gameObject;
        UiKit.Stretch((RectTransform)hint.transform);
        hintText = UiKit.Label(hint.transform, "", 19, UiKit.Fg);
        hintText.lineSpacing = 1.2f;
        UiKit.Stretch(hintText.rectTransform, 16f);

        // language, lower right corner (the upper right is where the 5th floor's label goes)
        var lang = UiKit.SubButton(root, "", out langText);
        var lrt = (RectTransform)lang.transform;
        lrt.anchorMin = lrt.anchorMax = lrt.pivot = new Vector2(1f, 0f);
        lrt.anchoredPosition = new Vector2(-LangRight, LangBottom);
        lrt.sizeDelta = new Vector2(LangW, LangH);
        langText.fontSize = 17;
        lang.onClick.AddListener(L.Toggle);
        lang.navigation = new Navigation { mode = Navigation.Mode.None };   // the pad switches language with Y / △
    }

    // Twelve cyan and white lights on a cable across the top, each swinging from its cord and glowing
    void BuildLanterns(RectTransform root)
    {
        var line = UiKit.Img("String", root, null, UiKit.Line).rectTransform;
        line.anchorMin = new Vector2(0f, 1f);
        line.anchorMax = new Vector2(1f, 1f);
        line.pivot = new Vector2(0.5f, 1f);
        line.sizeDelta = new Vector2(0f, 2f);
        line.anchoredPosition = Vector2.zero;
        Color[] colors = { UiKit.Accent, UiKit.Accent2, WindowLit, UiKit.Accent2 };
        const int count = 12;
        for (int i = 0; i < count; i++)
        {
            var p = UiKit.Rect("Lantern", root);
            p.anchorMin = p.anchorMax = new Vector2((i + 0.5f) / count, 1f);
            p.pivot = new Vector2(0.5f, 1f);
            p.sizeDelta = new Vector2(60f, 80f);
            p.anchoredPosition = new Vector2(0f, -2f);
            float drop = 8f + (i % 3) * 10f;
            var cord = UiKit.Img("Cord", p, null, UiKit.Line).rectTransform;
            UiKit.Place(cord, 29f, 0f, 2f, drop);
            var color = colors[i % colors.Length];
            var glow = UiKit.Img("Glow", p, UiKit.Glow, new Color(color.r, color.g, color.b, 0.4f));
            UiKit.Place(glow.rectTransform, 0f, drop - 15f, 60f, 60f);
            var body = UiKit.Box("Body", p, color, 10f);
            UiKit.Place(body.rectTransform, 19f, drop, 22f, 30f);
            lanterns.Add(new Lantern { pivot = p, glow = glow, speed = Mathf.PI * 2f / (2.6f + (i % 4) * 0.4f), phase = i * 0.9f });
        }
    }

    // anchored to the left edge, `y` down from the top
    static void Left(RectTransform rt, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(64f, -y);
        rt.sizeDelta = new Vector2(w, h);
    }

    void Enter(RectTransform rt, float delay, bool drop)
    {
        var group = rt.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        entrances.Add(new Entrance { group = group, rt = rt, home = rt.anchoredPosition, delay = delay, drop = drop });
    }

    void Write()
    {
        var lines = L.T(titleLinesJa, titleLinesEn).Split('\n');
        for (int i = 0; i < titleLines.Length; i++)
        {
            titleLines[i].text = i < lines.Length ? lines[i] : "";
            titleLines[i].fontSize = L.English ? 48 : 68;
        }
        textPad = GamepadInput.UsingPad;
        subtitle.text = GamepadInput.T("ビルの階をクリックして遊ぶフロアを選ぼう", "Click a floor of the building to play it",
            "スティック → でビルの階を選ぼう", "Push the stick → to pick a floor of the building");
        runLabel.text = L.T("▶ 1階から5階まで遊ぶ", "▶ Play floors 1 to 5");
        hintText.text = GamepadInput.T("右のビルの階をクリックすると\nそのフロアだけ遊べるよ", "Click a floor of the building\nto play just that floor",
            $"→ でビルへ、↑↓ で階を選んで {GamepadInput.South}\n言語の切り替え：{GamepadInput.North}",
            $"→ to the building, ↑↓ for a floor, {GamepadInput.South} to play\nLanguage: {GamepadInput.North}");
        for (int i = 0; i < 5; i++)
        {
            floors[i].name.text = L.T((i + 1) + "階", (i + 1) + "F");
            floors[i].game.text = L.T(FloorGames[i].ja, FloorGames[i].en);
        }
        langText.text = (L.English ? "日本語" : "English") +   // the language it switches to
                        (textPad ? $" ({GamepadInput.North})" : "");
        ShowInfo();
    }

    void ShowInfo()
    {
        info.SetActive(hover > 0);
        hint.SetActive(hover == 0);
        cardBorder.color = hover > 0 ? UiKit.Accent : UiKit.Line;
        if (hover == 0) return;
        infoNumber.text = hover.ToString();
        infoFloor.text = GamepadInput.T($"{hover}階 · クリックでスタート", $"{hover}F · click to start",
            $"{hover}階 · {GamepadInput.South} でスタート　← で戻る", $"{hover}F · {GamepadInput.South} to start   ← back");
        infoGame.text = L.T(FloorGames[hover - 1].ja, FloorGames[hover - 1].en);
        infoNote.text = L.T("このフロアのクエストをクリアしたらゲームクリア！", "Clear this floor's quest to win the game!");
    }

    /* ---------- per frame ---------- */
    void Update()
    {
        float t = Time.unscaledTime;
        float since = t - opened;
        Animate(t, since);

        var mouse = Mouse.current;
        Vector2 mp = mouse != null ? mouse.position.ReadValue() : Vector2.zero;

        // the building rises into place, then turns gently, and a little toward the pointer
        rise = UiKit.EaseOut((since - 0.3f) / 0.7f);
        building.position = new Vector3(0f, Mathf.Lerp(RiseFrom, 0f, rise), 0f);
        float toward = mouse != null ? (mp.x / Mathf.Max(1f, Screen.width) - 0.5f) * 14f : 0f;
        building.rotation = Quaternion.Euler(0f, -12f + Mathf.Sin(t * 0.35f) * 5f + toward, 0f);

        // which floor is under the pointer (not when it's over the UI, nor before the building is up)
        int now = 0;
        bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        if (!leaving && rise >= 1f && mouse != null && !overUi && cam != null &&
            Physics.Raycast(cam.ScreenPointToRay(mp), out var hitInfo, 100f))
            foreach (var f in floors)
                if (hitInfo.collider == f.hit) now = f.n;
        // the gamepad runs its own cursor on the building (the mouse takes over again as soon as it moves)
        if (textPad != GamepadInput.UsingPad) Write();
        if (GamepadInput.UsingPad && !leaving && rise >= 1f)
        {
            if (PadInput()) return;
            now = padOnBuilding ? padFloor : 0;
        }
        else if (padOnBuilding && !GamepadInput.UsingPad) LeaveBuilding(false);
        if (now != hover)
        {
            // a soft blip on reaching a floor (not too often when the pointer runs down the building)
            if (now > 0 && t - lastBlip > 0.07f)
            {
                lastBlip = t;
                Sfx.PlayUi(Sfx.Blip(), 0.35f, 0.9f + 0.08f * now);
            }
            hover = now;
            ShowInfo();
        }
        if (hover > 0 && mouse != null && mouse.leftButton.wasPressedThisFrame) { PlayFloor(hover); return; }

        float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 10f);
        float scale = labelLayer.rect.width / Mathf.Max(1f, Screen.width);
        float column = LabelColumn(scale);
        foreach (var f in floors)
        {
            bool on = f.n == hover;
            f.slide = Mathf.Lerp(f.slide, on ? 1f : 0f, k);
            float pulseGlow = on ? 0.85f + 0.15f * Mathf.Sin(t * 6f) : 0f;
            f.glow = Mathf.Lerp(f.glow, on ? pulseGlow : 0.12f + 0.08f * Mathf.Sin(t * 1.3f + f.n), k);
            var p = f.root.localPosition;
            f.root.localPosition = new Vector3(p.x, p.y, -SlideOut * f.slide);
            f.body.material.color = Color.Lerp(Body, BodyLit, f.slide);
            foreach (var w in f.windows) w.material.color = Color.Lerp(WindowDark, WindowLit, f.glow);

            // label beside the floor's right edge, all five in one column
            Vector3 sp = cam.WorldToScreenPoint(LabelAnchor(f));
            float grow = 1f + 0.2f * f.slide;
            float half = LabelH * 0.5f * grow;
            float y = Mathf.Clamp(sp.y * scale, ScreenMargin + half, labelLayer.rect.height - ScreenMargin - half);
            f.label.anchoredPosition = new Vector2(column + 16f * f.slide, y);
            f.name.color = Color.Lerp(UiKit.Fg, UiKit.Accent, f.slide);
            f.label.localScale = Vector3.one * grow;
        }
        // the labels come in with the building
        labelGroup.alpha = rise;

        // the elevator car rides to the floor under the pointer (the first floor when none)
        float target = 0.2f + ((hover > 0 ? hover : 1) - 1) * FloorH + FloorH * 0.5f;
        carY = Mathf.Lerp(carY, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 5f));
        var cp = car.localPosition;
        car.localPosition = new Vector3(cp.x, carY, cp.z);
    }

    // A floor's label anchor: just beyond its right edge, at mid height
    Vector3 LabelAnchor(Floor f) =>
        f.root.TransformPoint(new Vector3(FloorW * 0.5f + 0.4f, FloorH * 0.5f, -FloorD * 0.5f));

    // The x (pivot at the left) shared by all floor labels: beside the building's furthest-right floor edge, pulled
    // left as a whole if any label (hovered size, slid out) would leave the screen or touch the language button
    float LabelColumn(float scale)
    {
        Rect area = labelLayer.rect;
        const float maxGrow = 1.2f, maxSlide = 16f;
        float w = LabelW * maxGrow + maxSlide, half = LabelH * 0.5f * maxGrow;
        float column = float.MinValue, limit = float.MaxValue;
        foreach (var f in floors)
        {
            Vector3 sp = cam.WorldToScreenPoint(LabelAnchor(f));
            column = Mathf.Max(column, sp.x * scale);
            float y = Mathf.Clamp(sp.y * scale, ScreenMargin + half, area.height - ScreenMargin - half);
            float right = area.width - ScreenMargin;
            if (y - half < LangBottom + LangH + ScreenMargin)   // in the button's rows: stay left of it
                right = area.width - LangRight - LangW - ScreenMargin;
            limit = Mathf.Min(limit, right - w);
        }
        return Mathf.Min(column, limit);
    }

    // The opening entrances, the lights and the button's pulse
    void Animate(float t, float since)
    {
        foreach (var e in entrances)
        {
            float k = Mathf.Clamp01((since - e.delay) / (e.drop ? 0.5f : 0.4f));
            e.group.alpha = Mathf.Clamp01(k * 1.6f);
            if (e.drop)
            {
                e.rt.anchoredPosition = e.home + new Vector2(0f, Mathf.LerpUnclamped(40f, 0f, UiKit.EaseOutBack(k)));
                e.rt.localScale = Vector3.one * Mathf.Lerp(1.1f, 1f, UiKit.EaseOut(k));
            }
            else e.rt.anchoredPosition = e.home - new Vector2(0f, 14f * (1f - UiKit.EaseOut(k)));
        }

        foreach (var l in lanterns)
        {
            l.pivot.localEulerAngles = new Vector3(0f, 0f, Mathf.Sin(t * l.speed + l.phase) * 7f);
            var g = l.glow.color;
            g.a = 0.32f + 0.08f * Mathf.Sin(t * 5.3f + l.phase * 2f) + 0.05f * Mathf.Sin(t * 11f + l.phase);
            l.glow.color = g;
        }

        // every 2 s once everything is in: a ring grows out of the button and fades
        float p = since > 1.6f ? Mathf.Repeat(since - 1.6f, 2f) / 1.2f : 1f;
        pulseRt.localScale = new Vector3(1f + 0.06f * p, 1f + 0.25f * p, 1f);
        pulse.color = new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, p < 1f ? 0.45f * (1f - p) : 0f);
    }

    void PlayAll()
    {
        if (leaving) return;
        leaving = true;
        GameProgress.NewSession();
        GameProgress.SingleFloor = "";
        leaving = ElevatorRide.Go(floor1, 1, 1);
        if (leaving) Chosen();
    }

    void PlayFloor(int n)
    {
        if (leaving) return;
        leaving = true;
        GameProgress.NewSession();
        GameProgress.SingleFloor = Floors[n - 1];
        leaving = ElevatorRide.Go(Floors[n - 1], 1, n);
        if (leaving) Chosen();
    }

    // off to the elevator: a quick rush, a little higher than the doors' own whoosh that follows
    static void Chosen() => Sfx.PlayUi(Sfx.Whoosh(), 0.7f, 1.25f);
}
