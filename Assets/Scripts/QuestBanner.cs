using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows the floor and its quest each time the scene starts (after the elevator ride, if there is one): first
/// the floor (1階 / 1F) pops in big in the centre and flies off, then the quest is revealed a few words at a time
/// over a cyan band, then both slide into a small panel in the upper-left corner for the rest of the level.
/// Once the quest is cleared (GameProgress.ClearQuest with this scene's name), クエストクリア！ stamps in instead,
/// with a ring burst, confetti and a 「3階のクエスト達成」 ribbon, and the corner panel turns green.
/// The player stays still (PlayerLock) from the start of the intro or celebration until the corner panel slides in.
/// The panel stays at full strength for a few seconds after it arrives or changes, then rests at 75% (glow pulses
/// bring it back up).
/// Add it with Tools > Elevator Game > Add Quest Banner.
/// </summary>
public class QuestBanner : MonoBehaviour
{
    [TextArea]
    public string questText = "ITエンジニア学科の教室でミニゲームをクリアしよう！";

    [Tooltip("Quest name for GameProgress. Empty = this scene's name.")]
    public string questId = "";

    [Tooltip("Which floor this is (shown as 1階 / 1F). 0 = work it out from the elevator's button (３階へ → 2).")]
    public int floor = 0;

    [Tooltip("Seconds the floor name stays before the quest appears.")]
    public float floorHold = 0.9f;

    [Tooltip("Seconds for the big text to appear.")]
    public float fadeIn = 0.5f;
    [Tooltip("Seconds the big text stays fully visible.")]
    public float hold = 1.2f;
    [Tooltip("Seconds for the big text to fade away.")]
    public float fadeOut = 0.8f;
    [Tooltip("Seconds for the corner quest to appear.")]
    public float cornerFadeIn = 0.5f;

    CanvasGroup intro, corner, bigGroup, kickerGroup, ribbonGroup;
    Text big, cornerTitle, cornerText, badgeText, kicker, ribbonText;
    Shadow bigShadow;
    RectTransform cornerPanel, badge, band, ring, ribbon;
    Image bandImage, ringImage;
    readonly Image[] confetti = new Image[12];
    AudioSource audioSource;
    AudioClip sfxWin;
    LayoutElement badgeSize;
    Image cornerBorder, stripe, attention;
    Coroutine nudge;
    CanvasGroup panelGroup;
    float loudUntil;   // the panel is at full strength until then (unscaled time), else QuietAlpha

    /// <summary>Width of the corner quest panel; QuestGuide's 次： line matches it.</summary>
    public const float CornerW = 380f;
    const float LoudFor = 5f, QuietAlpha = 0.75f, QuietSpeed = 2f;

    /// <summary>Bottom edge of the corner quest panel (1280×720 canvas units from the top), for the guide's
    /// 次： line under it. 0 while there is no panel.</summary>
    public static float CornerBottom;
    /// <summary>True while クエストクリア！ is on screen; GameEnding waits for it.</summary>
    public static bool Celebrating;
    /// <summary>True while the big intro or celebration has the screen (until the corner panel slides in); the
    /// quest guide hides meanwhile.</summary>
    public static bool Intro;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void NewPlay() => Celebrating = Intro = false;

    bool cleared;
    Coroutine show;
    int floorNumber;

    string Quest => string.IsNullOrEmpty(questId) ? gameObject.scene.name : questId;

    void Awake()
    {
        var canvas = UiKit.MakeCanvas("QuestCanvas", 20, new Vector2(1280f, 720f));

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        sfxWin = Sfx.Win();

        // Intro: dimmed screen, a band across the middle, the big text (floor, quest or クエストクリア！) and
        // the clear effects: ring, confetti, ribbon
        intro = Layer("Intro", canvas.transform);
        UiKit.Stretch(UiKit.Img("Dim", intro.transform, null, UiKit.WithAlpha(UiKit.Dim, 0.55f)).rectTransform);
        bandImage = UiKit.Img("Band", intro.transform, null, UiKit.WithAlpha(UiKit.Accent, 0.12f));
        band = bandImage.rectTransform;
        band.anchorMin = new Vector2(0f, 0.5f);
        band.anchorMax = new Vector2(1f, 0.5f);
        band.pivot = new Vector2(0f, 0.5f);
        band.sizeDelta = new Vector2(0f, 250f);
        ringImage = UiKit.Img("Ring", intro.transform, UiKit.Ring, Color.clear);
        ring = UiKit.Center(ringImage.rectTransform, 110f, 110f);
        for (int i = 0; i < confetti.Length; i++)
        {
            confetti[i] = UiKit.Img("Confetti", intro.transform, null, Color.clear);
            UiKit.Center(confetti[i].rectTransform, 16f, 16f);
        }
        kicker = UiKit.Label(intro.transform, "", 22, UiKit.Accent2);
        UiKit.Center(kicker.rectTransform, 600f, 34f).anchoredPosition = new Vector2(0f, 150f);
        kickerGroup = kicker.gameObject.AddComponent<CanvasGroup>();
        big = UiKit.Display(UiKit.Label(intro.transform, "", 220, UiKit.Accent));
        big.verticalOverflow = VerticalWrapMode.Truncate;
        big.resizeTextMinSize = 40;
        bigShadow = UiKit.HardShadow(big, UiKit.AccentShadow, 6f);
        bigGroup = big.gameObject.AddComponent<CanvasGroup>();
        var rb = UiKit.Box("Ribbon", intro.transform, UiKit.Panel, 26f, UiKit.Good, 3f);
        ribbon = UiKit.Center(rb.rectTransform, 420f, 52f);
        ribbonText = UiKit.Label(ribbon, "", 24, UiKit.Fg);
        UiKit.Stretch(ribbonText.rectTransform);
        ribbonGroup = rb.gameObject.AddComponent<CanvasGroup>();

        // Corner: panel in the upper left that grows to fit the wrapped quest text: a cyan frame and stripe,
        // the floor badge and QUEST on top, the quest in large type. A soft glow behind it pulses now and then
        // so it isn't overlooked; it never takes clicks.
        corner = Layer("Quest", canvas.transform);
        attention = UiKit.Img("Attention", corner.transform, UiKit.Glow, Color.clear);
        var panel = UiKit.Box("Panel", corner.transform, UiKit.WithAlpha(UiKit.Bg2, 0.94f), 11f, UiKit.Accent, 2.5f);
        cornerBorder = panel;
        cornerPanel = panel.rectTransform;
        UiKit.Place(cornerPanel, 16f, 16f, CornerW, 0f);
        UiKit.HardShadow(panel, UiKit.WithAlpha(UiKit.PanelShadow, 0.6f), 4f).useGraphicAlpha = false;
        panelGroup = panel.gameObject.AddComponent<CanvasGroup>();   // the quiet fade; the glow is a sibling
        panelGroup.blocksRaycasts = false;
        var v = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        v.padding = new RectOffset(22, 16, 9, 12);
        v.spacing = 5f;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        stripe = UiKit.Box("Stripe", cornerPanel, UiKit.Accent, 2.5f);
        stripe.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var srt = stripe.rectTransform;
        srt.anchorMin = new Vector2(0f, 0f);
        srt.anchorMax = new Vector2(0f, 1f);
        srt.pivot = new Vector2(0f, 0.5f);
        srt.offsetMin = new Vector2(8f, 11f);
        srt.offsetMax = new Vector2(13f, -11f);

        // header: [2階] QUEST
        var header = UiKit.Rect("Header", cornerPanel);
        var h = header.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 8f;
        h.childAlignment = TextAnchor.MiddleLeft;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;
        header.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;
        var b = UiKit.Box("Floor", header, UiKit.Accent, 6f);
        badge = b.rectTransform;
        badgeSize = b.gameObject.AddComponent<LayoutElement>();
        badgeSize.preferredHeight = 24f;
        badgeText = UiKit.Display(UiKit.Label(badge, "", 15, UiKit.Ink));
        badgeText.horizontalOverflow = HorizontalWrapMode.Overflow;
        UiKit.Stretch(badgeText.rectTransform);
        cornerTitle = UiKit.Label(header, "", 16, UiKit.Accent, TextAnchor.MiddleLeft);
        cornerTitle.horizontalOverflow = HorizontalWrapMode.Overflow;
        cornerTitle.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        cornerText = UiKit.Label(cornerPanel, questText, 20, UiKit.Fg, TextAnchor.UpperLeft);
        cornerText.lineSpacing = 1.05f;
        UiKit.HardShadow(cornerText, UiKit.WithAlpha(UiKit.PanelShadow, 0.6f), 1.5f);

        Apply(GameProgress.IsQuestCleared(Quest));
        intro.alpha = 0f;
        corner.alpha = 0f;
        L.Changed += Redraw;
    }

    void OnDestroy()
    {
        L.Changed -= Redraw;
        CornerBottom = 0f;
        Celebrating = Intro = false;
        PlayerLock.Set(this, false);
    }

    void LateUpdate()
    {
        CornerBottom = 16f + cornerPanel.rect.height;
        // the glow follows the panel's size
        var a = attention.rectTransform;
        a.anchorMin = a.anchorMax = a.pivot = new Vector2(0f, 1f);
        a.anchoredPosition = cornerPanel.anchoredPosition + new Vector2(-30f, 30f);
        a.sizeDelta = cornerPanel.rect.size + new Vector2(60f, 60f);
        // full strength while something is new, then it rests quieter
        float target = Time.unscaledTime < loudUntil ? 1f : QuietAlpha;
        panelGroup.alpha = Mathf.MoveTowards(panelGroup.alpha, target, QuietSpeed * Time.unscaledDeltaTime);
    }

    void Loud(float seconds) => loudUntil = Mathf.Max(loudUntil, Time.unscaledTime + seconds);

    void Redraw() => Apply(cleared);   // the language was switched

    void Start()
    {
        floorNumber = floor > 0 ? floor : DetectFloor();
        Apply(cleared);
        show = StartCoroutine(Show(false));
    }

    // From the floor's elevator: 「３階へ」 on an elevator going up means this is floor 2; the first floor's
    // elevator (its minigame) says 「２階へ」; a floor with play zones but no elevator going up is the fifth.
    int DetectFloor()
    {
        var zones = FindObjectsByType<ElevatorPlayZone>(FindObjectsSortMode.None);
        foreach (var z in zones)
        {
            bool elevator = z is ElevatorExitZone ||
                            (z.interactable != null && z.interactable.name.ToLowerInvariant().Contains("elevator"));
            int up = elevator ? Digit(z.buttonLabel) : 0;
            if (up > 1) return up - 1;
        }
        return zones.Length > 0 ? 5 : 0;
    }

    // The first digit in a label, full-width or not
    static int Digit(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        foreach (char c in s)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= '０' && c <= '９') return c - '０';
        }
        return 0;
    }

    /// <summary>Which floor this is (1–5), 0 if unknown. Set in Start.</summary>
    public int FloorNumber => floorNumber;

    string FloorName => floorNumber > 0 ? L.T(floorNumber + "階", floorNumber + "F") : "";

    // A quest can also be cleared on the floor itself (the second floor's ticket machine)
    void Update()
    {
        if (cleared || !GameProgress.IsQuestCleared(Quest)) return;
        Apply(true);
        if (show != null) StopCoroutine(show);
        show = StartCoroutine(Show(true));
    }

    void Apply(bool done)
    {
        cleared = done;
        big.text = done ? L.T("クエストクリア！", "Quest Clear!") : L.Tr(questText);
        big.color = done ? UiKit.Good : UiKit.Accent;
        bigShadow.effectColor = done ? UiKit.GoodShadow : UiKit.AccentShadow;
        ribbonText.text = floorNumber > 0 ? L.T(floorNumber + "階のクエスト達成", floorNumber + "F quest complete")
                                          : L.T("クエスト達成", "Quest complete");
        ribbon.sizeDelta = new Vector2(ribbonText.preferredWidth + 60f, 52f);
        kicker.text = L.T("クエスト", "QUEST");
        badge.gameObject.SetActive(floorNumber > 0);
        badgeText.text = FloorName;
        badgeSize.preferredWidth = badgeText.preferredWidth + 16f;
        cornerTitle.text = done ? L.T("クエスト　✓ クリア！", "QUEST　✓ CLEAR!") : L.T("クエスト", "QUEST");
        cornerText.text = L.Tr(questText);
        Color c = done ? UiKit.Good : UiKit.Accent;
        cornerTitle.color = c;
        cornerBorder.color = c;
        stripe.color = c;
        ((Image)badge.GetComponent<Image>()).color = c;
        cornerText.color = done ? UiKit.Muted : UiKit.Fg;
        Loud(LoudFor);
    }

    // Unscaled time, so a paused game doesn't freeze the banner. Mid-level the corner stays put.
    IEnumerator Show(bool keepCorner)
    {
        if (cleared) Celebrating = true;
        Intro = true;
        PlayerLock.Set(this, true);
        while (ElevatorRide.Busy) yield return null;   // the doors are still shut
        intro.gameObject.SetActive(true);
        intro.alpha = 0f;
        HideEffects();

        // arriving on a floor: its name pops in, then flies up and away
        if (!keepCorner && floorNumber > 0)
        {
            BigMode(220, false, 0.04f, 0.96f, 0.04f, 0.96f);
            big.text = FloorName;
            big.color = UiKit.Accent;
            bigShadow.effectColor = UiKit.AccentShadow;
            for (float t = 0f; t < fadeIn; t += Time.unscaledDeltaTime)
            {
                float k = t / fadeIn;
                intro.alpha = Mathf.Clamp01(k * 2f);
                bigGroup.alpha = Mathf.Clamp01(k * 1.6f);
                big.rectTransform.localScale = Vector3.one * Mathf.LerpUnclamped(1.4f, 1f, UiKit.EaseOutBack(k));
                big.rectTransform.anchoredPosition = new Vector2(0f, Mathf.Lerp(20f, 0f, UiKit.EaseOut(k)));
                yield return null;
            }
            intro.alpha = 1f;
            Settle();
            yield return new WaitForSecondsRealtime(floorHold);
            for (float t = 0f; t < 0.3f; t += Time.unscaledDeltaTime)
            {
                float k = UiKit.EaseIn(t / 0.3f);
                bigGroup.alpha = 1f - k;
                big.rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, 0.7f, k);
                big.rectTransform.anchoredPosition = new Vector2(0f, 60f * k);
                yield return null;
            }
        }

        if (cleared) yield return Celebrate();
        else yield return RevealQuest();
        intro.gameObject.SetActive(false);
        Celebrating = Intro = false;
        PlayerLock.Set(this, false);   // free to move as the corner panel arrives

        // the corner panel slides in from the left with a little overshoot
        if (!keepCorner || corner.alpha < 1f)
        {
            var home = cornerPanel.anchoredPosition = new Vector2(16f, -16f);
            float time = Mathf.Max(cornerFadeIn, 0.45f);
            for (float t = 0f; t < time; t += Time.unscaledDeltaTime)
            {
                float k = t / time;
                float e = UiKit.EaseOutBack(k);
                corner.alpha = Mathf.Clamp01(k * 2.5f);
                cornerPanel.anchoredPosition = home + Vector2.LerpUnclamped(new Vector2(-40f, 10f), Vector2.zero, e);
                cornerPanel.localScale = Vector3.one * Mathf.LerpUnclamped(0.9f, 1f, e);
                yield return null;
            }
            corner.alpha = 1f;
            cornerPanel.anchoredPosition = home;
            cornerPanel.localScale = Vector3.one;
        }
        Loud(LoudFor);
        if (nudge != null) StopCoroutine(nudge);
        nudge = StartCoroutine(Nudge());
    }

    // The quest over a cyan band: QUEST, then the text a few words at a time (each piece fades in where it
    // will stay, so nothing reflows), then everything fades.
    IEnumerator RevealQuest()
    {
        BigMode(96, true, 0.06f, 0.94f, 0.33f, 0.67f);
        Apply(false);
        bandImage.color = UiKit.WithAlpha(UiKit.Accent, 0.12f);
        string quest = big.text;
        string rgb = ColorUtility.ToHtmlStringRGB(big.color);
        var chunks = Chunks(quest);
        float reveal = 0.35f + chunks.Count * 0.09f + 0.2f;
        for (float t = 0f; t < reveal; t += Time.unscaledDeltaTime)
        {
            intro.alpha = Mathf.Max(intro.alpha, Mathf.Clamp01(t / 0.25f));
            band.localScale = new Vector3(UiKit.EaseOut(t / 0.35f), 1f, 1f);
            kickerGroup.alpha = Mathf.Clamp01((t - 0.05f) / 0.3f);
            big.text = Revealed(chunks, rgb, t - 0.15f);
            yield return null;
        }
        big.text = quest;
        band.localScale = Vector3.one;
        kickerGroup.alpha = 1f;
        yield return new WaitForSecondsRealtime(hold);
        for (float t = 0f; t < fadeOut; t += Time.unscaledDeltaTime)
        {
            float k = UiKit.EaseIn(t / fadeOut);
            intro.alpha = 1f - k;
            big.rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, 0.9f, k);
            yield return null;
        }
        intro.alpha = 0f;
    }

    // クエストクリア！: green band wipe, the stamp slams in (160% → squash → 104% → 100%, slightly tilted), a ring
    // bursts, confetti flies out, the floor ribbon slides up; about 2.7 s in all.
    IEnumerator Celebrate()
    {
        Celebrating = true;
        BigMode(96, false, 0f, 1f, 0.38f, 0.62f);
        Apply(true);
        bandImage.color = UiKit.WithAlpha(UiKit.Good, 0.16f);
        var stamp = big.rectTransform;
        bool played = false;
        Color[] palette = { UiKit.Accent, UiKit.Accent2, UiKit.Good, UiKit.Fg };
        const float total = 2.3f, fade = 0.4f;
        for (float t = 0f; t < total + fade; t += Time.unscaledDeltaTime)
        {
            intro.alpha = t < total ? Mathf.Clamp01(t / 0.3f) : 1f - UiKit.EaseIn((t - total) / fade);
            band.localScale = new Vector3(UiKit.EaseOut(t / 0.35f), 1f, 1f);

            float k = Mathf.Clamp01((t - 0.1f) / 0.45f);
            Vector2 sc;
            if (k < 0.45f) sc = Vector2.Lerp(new Vector2(1.6f, 1.6f), new Vector2(0.92f, 0.86f), UiKit.EaseIn(k / 0.45f));
            else if (k < 0.7f) sc = Vector2.Lerp(new Vector2(0.92f, 0.86f), new Vector2(1.04f, 1.04f), UiKit.EaseOut((k - 0.45f) / 0.25f));
            else sc = Vector2.Lerp(new Vector2(1.04f, 1.04f), Vector2.one, (k - 0.7f) / 0.3f);
            stamp.localScale = new Vector3(sc.x, sc.y, 1f);
            stamp.localEulerAngles = new Vector3(0f, 0f, Mathf.Lerp(6f, 2f, k));   // z is anticlockwise: tilted up
            bigGroup.alpha = Mathf.Clamp01(k / 0.45f);
            if (!played && k >= 0.45f)
            {
                audioSource.PlayOneShot(sfxWin);
                played = true;
            }

            float r = Mathf.Clamp01((t - 0.2f) / 0.7f);
            ring.localScale = Vector3.one * Mathf.Lerp(0.2f, 2.6f, UiKit.EaseOut(r));
            ringImage.color = new Color(UiKit.Accent.r, UiKit.Accent.g, UiKit.Accent.b, t < 0.2f ? 0f : 0.9f * (1f - r));

            for (int i = 0; i < confetti.Length; i++)
            {
                float c = Mathf.Clamp01((t - 0.25f - (i % 4) * 0.04f) / 0.9f);
                float angle = i / (float)confetti.Length * Mathf.PI * 2f;
                float dist = (240f + (i % 3) * 70f) * UiKit.EaseOut(c);
                var rt = confetti[i].rectTransform;
                rt.anchoredPosition = new Vector2(Mathf.Cos(angle) * dist, Mathf.Sin(angle) * dist * 0.6f);
                rt.localEulerAngles = new Vector3(0f, 0f, 45f + 180f * c);
                rt.localScale = Vector3.one * Mathf.Lerp(0.4f, 1f, c);
                var col = palette[i % palette.Length];
                col.a = c <= 0f ? 0f : c < 0.2f ? c / 0.2f : 1f - (c - 0.2f) / 0.8f;
                confetti[i].color = col;
            }

            float u = Mathf.Clamp01((t - 0.8f) / 0.35f);
            ribbonGroup.alpha = u;
            ribbon.anchoredPosition = new Vector2(0f, -120f - 24f * (1f - UiKit.EaseOut(u)));
            yield return null;
        }
        intro.alpha = 0f;
        Settle();
    }

    // big text: font size, best fit or not, and its box as anchors on the screen
    void BigMode(int size, bool bestFit, float x0, float x1, float y0, float y1)
    {
        var rt = big.rectTransform;
        rt.anchorMin = new Vector2(x0, y0);
        rt.anchorMax = new Vector2(x1, y1);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        big.fontSize = size;
        big.resizeTextForBestFit = bestFit;
        big.resizeTextMaxSize = size;
        Settle();
    }

    void Settle()
    {
        var rt = big.rectTransform;
        rt.localScale = Vector3.one;
        rt.localEulerAngles = Vector3.zero;
        bigGroup.alpha = 1f;
    }

    void HideEffects()
    {
        band.localScale = new Vector3(0f, 1f, 1f);
        ringImage.color = Color.clear;
        foreach (var c in confetti) c.color = Color.clear;
        kickerGroup.alpha = 0f;
        ribbonGroup.alpha = 0f;
        bigGroup.alpha = 0f;
    }

    // The quest split after particles and punctuation, so each piece can appear on its own
    static List<string> Chunks(string text)
    {
        var list = new List<string>();
        const string breaks = "のでをにがはと、！。 ";
        int start = 0;
        for (int i = 0; i < text.Length; i++)
            if (breaks.IndexOf(text[i]) >= 0 || i == text.Length - 1)
            {
                list.Add(text.Substring(start, i + 1 - start));
                start = i + 1;
            }
        return list;
    }

    // The whole text with each piece's alpha at time t (0.09 s apart, 0.2 s fade each), in the text's colour
    static string Revealed(List<string> chunks, string rgb, float t)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < chunks.Count; i++)
        {
            int a = Mathf.RoundToInt(255f * Mathf.Clamp01((t - i * 0.09f) / 0.2f));
            if (a >= 255) sb.Append(chunks[i]);
            else sb.Append("<color=#").Append(rgb).Append(a.ToString("x2")).Append('>').Append(chunks[i]).Append("</color>");
        }
        return sb.ToString();
    }

    // Draws the eye to the corner: a pop and three glows on arriving, then two glows every 30 s until the
    // quest is cleared
    IEnumerator Nudge()
    {
        int pulses = 3;
        while (true)
        {
            for (int i = 0; i < pulses; i++)
            {
                for (float t = 0f; t < 0.8f; t += Time.unscaledDeltaTime)
                {
                    float s = Mathf.Sin(t / 0.8f * Mathf.PI);
                    var c = cleared ? UiKit.Good : UiKit.Accent;
                    attention.color = new Color(c.r, c.g, c.b, 0.55f * s);
                    Loud(1.2f);
                    if (pulses == 3 && i == 0) cornerPanel.localScale = Vector3.one * (1f + 0.06f * s);
                    yield return null;
                }
                attention.color = Color.clear;
                cornerPanel.localScale = Vector3.one;
            }
            if (cleared) yield break;
            pulses = 2;
            yield return new WaitForSecondsRealtime(30f);
        }
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
}
