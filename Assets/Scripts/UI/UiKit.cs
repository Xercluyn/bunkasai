using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Helpers for building uGUI from code: the minigame palette (Assets/Minigame/style.css),
/// the game fonts (with an OS font fallback), easing curves, runtime-generated sprites and element factories.
/// </summary>
public static class UiKit
{
    // ---------- palette: IT-department navy + white, cyan accent ----------
    public static readonly Color Bg = Hex("#0b1a3a");
    public static readonly Color Bg2 = Hex("#10255a");
    public static readonly Color Panel = Hex("#163273");
    public static readonly Color Line = Hex("#2a55b0");
    public static readonly Color Fg = Hex("#ffffff");
    public static readonly Color Muted = Hex("#a9c4ec");
    public static readonly Color Accent = Hex("#22d3ee");
    public static readonly Color Accent2 = Hex("#7fb2ff");
    public static readonly Color Good = Hex("#34d399");
    public static readonly Color Bad = Hex("#ff5470");
    public static readonly Color CardBackA = Hex("#2563eb");
    public static readonly Color CardBackB = Hex("#1e3a8a");
    public static readonly Color CardFace = Hex("#ffffff");
    public static readonly Color CardInk = Hex("#0b1a3a");
    // Hard drop shadows under coloured text, dark ink for text on accent fills, and the modal dim.
    public static readonly Color AccentShadow = Hex("#0e7490");
    public static readonly Color Accent2Shadow = Hex("#1e40af");
    public static readonly Color GoodShadow = Hex("#047857");
    public static readonly Color BadShadow = Hex("#9f1239");
    public static readonly Color PanelShadow = Hex("#050d22");
    public static readonly Color Ink = Hex("#06263a");
    public static readonly Color Dim = Hex("#050d22");
    public static readonly Color MainFill = Hex("#3ee0f5");
    public static readonly Color MainShadow = Hex("#0e7490");
    public static readonly Color Light = Hex("#e0f7ff"); // lit windows, flashes
    public static readonly Color Highlight = Hex("#facc15"); // yellow, to stand out: the pad's choice, the quest guide

    public static Color Hex(string hex, float alpha = 1f)
    {
        ColorUtility.TryParseHtmlString(hex, out Color c);
        c.a = alpha;
        return c;
    }

    /// <summary>A palette colour at another opacity, e.g. WithAlpha(Dim, 0.7f) for a modal overlay.</summary>
    public static Color WithAlpha(Color c, float alpha) => new Color(c.r, c.g, c.b, alpha);

    // ---------- fonts ----------
    // The menu's type pair (Resources/Fonts, SIL Open Font License): Zen Maru Gothic for text, Dela Gothic One
    // for titles, big numbers and main buttons. Their importer falls back to OS fonts for missing glyphs
    // (GameFontImport). Without the files, everything uses the OS font as before.
    static Font jpFont, bodyFont, bodyMediumFont, displayFont;

    /// <summary>Dynamic OS font with Japanese glyphs; the later names act as fallbacks for missing characters.</summary>
    public static Font JpFont
    {
        get
        {
            if (jpFont == null)
            {
                jpFont = Font.CreateDynamicFontFromOSFont(new[]
                {
                    "Yu Gothic UI", "Yu Gothic", "Meiryo UI", "Meiryo", "MS Gothic",
                    "Hiragino Maru Gothic ProN", "Hiragino Sans", "Noto Sans CJK JP", "Noto Sans JP",
                    "Segoe UI Symbol", "Arial"
                }, 32);
                if (jpFont == null) jpFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            return jpFont;
        }
    }

    /// <summary>Zen Maru Gothic Bold: labels and body text.</summary>
    public static Font BodyFont => bodyFont ? bodyFont : (bodyFont = Resources.Load<Font>("Fonts/ZenMaruGothic-Bold"));
    /// <summary>Zen Maru Gothic Medium: the lighter text weight.</summary>
    public static Font BodyMediumFont => bodyMediumFont ? bodyMediumFont : (bodyMediumFont = Resources.Load<Font>("Fonts/ZenMaruGothic-Medium"));
    /// <summary>Dela Gothic One: titles, floor names, stamps and main buttons.</summary>
    public static Font DisplayFont => displayFont ? displayFont : (displayFont = Resources.Load<Font>("Fonts/DelaGothicOne-Regular"));

    /// <summary>Switch a label to the display face (the fonts carry their own weight, so no faux bold).</summary>
    public static Text Display(Text t)
    {
        if (DisplayFont != null) { t.font = DisplayFont; t.fontStyle = FontStyle.Normal; }
        return t;
    }

    // ---------- easing (t in 0..1) ----------
    public static float EaseOut(float t) { t = Mathf.Clamp01(t); return 1f - (1f - t) * (1f - t) * (1f - t); }
    public static float EaseIn(float t) { t = Mathf.Clamp01(t); return t * t * t; }
    public static float EaseInOut(float t) { t = Mathf.Clamp01(t); return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f; }
    /// <summary>Ease out with an overshoot past 1 before settling.</summary>
    public static float EaseOutBack(float t, float overshoot = 1.7f)
    {
        t = Mathf.Clamp01(t) - 1f;
        return 1f + (overshoot + 1f) * t * t * t + overshoot * t * t;
    }

    // ---------- sprites ----------
    const int RoundRadius = 32;
    static Sprite circle, ring, roundRect, glow, arrow, arrowRim, cardCover;

    public static Sprite Circle => circle ? circle : (circle = MakeSprite(256, (x, y, s) =>
        new Color(1f, 1f, 1f, DiscAlpha(x, y, s, s * 0.5f))));

    /// <summary>Circle outline, 8% of the diameter thick.</summary>
    public static Sprite Ring => ring ? ring : (ring = MakeSprite(256, (x, y, s) =>
        new Color(1f, 1f, 1f, DiscAlpha(x, y, s, s * 0.5f) * (1f - DiscAlpha(x, y, s, s * 0.42f)))));

    /// <summary>9-sliced rounded box; set Image.pixelsPerUnitMultiplier = 32 / radius.</summary>
    public static Sprite RoundRect => roundRect ? roundRect : (roundRect = MakeSprite(RoundRadius * 2 + 8, (x, y, s) =>
    {
        float cx = Mathf.Clamp(x, RoundRadius, s - RoundRadius);
        float cy = Mathf.Clamp(y, RoundRadius, s - RoundRadius);
        float d = Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy));
        return new Color(1f, 1f, 1f, Mathf.Clamp01(RoundRadius - d + 0.5f));
    }, new Vector4(RoundRadius, RoundRadius, RoundRadius, RoundRadius)));

    /// <summary>Soft radial glow: solid out to ~62% of the radius, fading to the edge.</summary>
    public static Sprite Glow => glow ? glow : (glow = MakeSprite(128, (x, y, s) =>
    {
        float r = s * 0.5f;
        float d = Vector2.Distance(new Vector2(x, y), new Vector2(r, r));
        float a = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(r * 0.62f, r, d));
        return new Color(1f, 1f, 1f, a);
    }));

    // the up-arrow's outline, in SVG units (0..100, y-down)
    static readonly Vector2[] ArrowPoly =
    {
        new Vector2(50, 8), new Vector2(88, 50), new Vector2(64, 50), new Vector2(64, 92),
        new Vector2(36, 92), new Vector2(36, 50), new Vector2(12, 50)
    };
    const float ArrowRimWidth = 7f;   // ArrowRim's thickness in SVG units (the arrow leaves 8 free at the edges)

    /// <summary>The up-arrow from script.js: SVG path "M50 8 L88 50 H64 V92 H36 V50 H12 Z".</summary>
    public static Sprite Arrow => arrow ? arrow : (arrow = ArrowSprite(p => InPolygon(ArrowPoly, p)));

    /// <summary>An even band hugging the outside of Arrow (same size and place): lay it under or over Arrow for a rim.</summary>
    public static Sprite ArrowRim => arrowRim ? arrowRim : (arrowRim = ArrowSprite(p =>
        !InPolygon(ArrowPoly, p) && DistanceToPolygon(ArrowPoly, p) <= ArrowRimWidth));

    // 128 px square sprite of the SVG-unit points where `inside` holds, 4x4 supersampled for smooth edges
    static Sprite ArrowSprite(Func<Vector2, bool> inside) => MakeSprite(128, (x, y, s) =>
    {
        int hits = 0;
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 4; j++)
            {
                float sx = (x - 0.5f + (i + 0.5f) / 4f) / s * 100f;
                float sy = (1f - (y - 0.5f + (j + 0.5f) / 4f) / s) * 100f;   // SVG is y-down
                if (inside(new Vector2(sx, sy))) hits++;
            }
        return new Color(1f, 1f, 1f, hits / 16f);
    });

    static float DistanceToPolygon(Vector2[] poly, Vector2 p)
    {
        float best = float.MaxValue;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            Vector2 a = poly[j], ab = poly[i] - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
        }
        return best;
    }

    /// <summary>Card back from style.css `.cover`: gradient, stripes, rings and a translucent border.</summary>
    public static Sprite CardCover => cardCover ? cardCover : (cardCover = MakeSprite(256, (x, y, s) =>
    {
        const float cssSize = 120f;              // roughly the card size in CSS px
        float k = s / cssSize;                   // texture px per CSS px
        float px = x, py = s - y;                // CSS coords (y-down)
        Vector2 p = new Vector2(px - s * 0.5f, py - s * 0.5f);

        // linear-gradient(160deg, back-a, back-b)
        float a160 = 160f * Mathf.Deg2Rad;
        Vector2 dir = new Vector2(Mathf.Sin(a160), -Mathf.Cos(a160));
        float len = s * (Mathf.Abs(Mathf.Sin(a160)) + Mathf.Abs(Mathf.Cos(a160)));
        Color c = Color.Lerp(CardBackA, CardBackB, Vector2.Dot(p, dir) / len + 0.5f);

        // repeating-linear-gradient(45deg, white .06 0 6px, transparent 6px 12px)
        float a45 = 45f * Mathf.Deg2Rad;
        float t = Vector2.Dot(new Vector2(px, py), new Vector2(Mathf.Sin(a45), -Mathf.Cos(a45))) / k;
        if (Mathf.Repeat(t, 12f) < 6f) c = Over(c, 0.06f);

        // radial rings (percentages of the farthest-corner radius)
        float ray = s * 0.7071f;
        float d = p.magnitude / ray;
        float ring = Mathf.Clamp01((d - 0.41f) / 0.01f) * Mathf.Clamp01((0.45f - d) / 0.01f);
        c = Over(c, 0.22f * ring);
        float disc = Mathf.Clamp01((0.31f - d) / 0.01f);
        c = Over(c, 0.14f * disc);

        // 3px border, white .18
        float r = s * 0.5f;
        if (p.magnitude > r - 3f * k) c = Over(c, 0.18f);

        c.a = DiscAlpha(x, y, s, r);
        return c;
    }));

    static Color Over(Color c, float whiteAlpha) => Color.Lerp(c, Color.white, whiteAlpha);

    static float DiscAlpha(float x, float y, int size, float radius)
    {
        float d = Vector2.Distance(new Vector2(x, y), new Vector2(size * 0.5f, size * 0.5f));
        return Mathf.Clamp01(radius - d + 0.5f);
    }

    public static bool InPolygon(Vector2[] poly, Vector2 p)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                inside = !inside;
        }
        return inside;
    }

    /// <summary>Square sprite from a per-pixel function; pixel(x, y, size) gets pixel-centre coordinates, y-up.</summary>
    public static Sprite MakeSprite(int size, Func<float, float, int, Color> pixel, Vector4 border = default)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                px[y * size + x] = pixel(x + 0.5f, y + 0.5f, size);
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, border);
    }

    // ---------- scene plumbing ----------
    public static void EnsureEventSystem()
    {
        if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null) return;
        var go = new GameObject("EventSystem", typeof(EventSystem));
        go.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
    }

    public static Canvas MakeCanvas(string name, int sortingOrder, Vector2 referenceResolution)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = referenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        return canvas;
    }

    // ---------- element factories ----------
    public static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    /// <summary>Anchor to the parent's top-left and place at (x, y) measured downward, CSS-style.</summary>
    public static RectTransform Place(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
        return rt;
    }

    /// <summary>Centre a square-ish element on its parent's centre.</summary>
    public static RectTransform Center(RectTransform rt, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(w, h);
        return rt;
    }

    public static RectTransform Stretch(RectTransform rt, float inset = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
        return rt;
    }

    public static Image Img(string name, Transform parent, Sprite sprite, Color color)
    {
        var img = Rect(name, parent).gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    /// <summary>Rounded box. With a border, the root is the border colour and a fill child is inset by borderWidth (ignored by layout groups).</summary>
    public static Image Box(string name, Transform parent, Color fill, float radius, Color? border = null, float borderWidth = 0f)
    {
        var root = Img(name, parent, null, border ?? fill);
        Round(root, radius);
        if (border.HasValue && borderWidth > 0f)
        {
            var inner = Img("Fill", root.transform, null, fill);
            Round(inner, Mathf.Max(0.01f, radius - borderWidth));
            Stretch(inner.rectTransform, borderWidth);
            inner.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        }
        return root;
    }

    public static void Round(Image img, float radius)
    {
        if (radius <= 0f) { img.sprite = null; img.type = Image.Type.Simple; return; }
        img.sprite = RoundRect;
        img.type = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = RoundRadius / radius;
    }

    /// <summary>
    /// A yellow (Highlight) rounded ring `width` wide just outside `face`, behind everything on it, following the
    /// corners of the first rounded image on it. Turn it on and off through the returned image.
    /// </summary>
    public static Image Outline(RectTransform face, float width)
    {
        float radius = 0f;
        foreach (var img in face.GetComponentsInChildren<Image>(true))
            if ((radius = RadiusOf(img)) > 0f) break;
        var ring = Box("Outline", face, Highlight, radius > 0f ? radius + width : 0f);
        Stretch(ring.rectTransform, -width);
        ring.transform.SetAsFirstSibling();
        ring.raycastTarget = false;
        ring.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        return ring;
    }

    /// <summary>The corner radius Round gave `img` (0 when it isn't rounded).</summary>
    public static float RadiusOf(Image img) =>
        img != null && img.sprite == roundRect && roundRect != null ? RoundRadius / img.pixelsPerUnitMultiplier : 0f;

    public static Text Label(Transform parent, string text, int size, Color color,
        TextAnchor anchor = TextAnchor.MiddleCenter, bool bold = true)
    {
        var t = Rect("Text", parent).gameObject.AddComponent<Text>();
        var face = bold ? BodyFont : BodyMediumFont;
        t.font = face != null ? face : JpFont;
        t.fontSize = size;
        t.fontStyle = bold && face == null ? FontStyle.Bold : FontStyle.Normal;
        t.color = color;
        t.alignment = anchor;
        t.supportRichText = true;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.text = text;
        return t;
    }

    /// <summary>Shrink instead of wrapping past the rect or spilling out of it: best fit takes the largest size
    /// in min..max that fits. Size the rect for the lines it may use (one line's height keeps it on one line).</summary>
    public static Text Fit(Text t, int max, int min)
    {
        t.resizeTextForBestFit = true;
        t.resizeTextMaxSize = max;
        t.resizeTextMinSize = min;
        t.fontSize = max;
        t.verticalOverflow = VerticalWrapMode.Truncate;
        return t;
    }

    public static Shadow HardShadow(Graphic g, Color color, float down)
    {
        var s = g.gameObject.AddComponent<Shadow>();
        s.effectColor = color;
        s.effectDistance = new Vector2(0f, -down);
        s.useGraphicAlpha = true;
        return s;
    }

    /// <summary>Big cyan button (style.css `.btn`).</summary>
    public static Button MainButton(Transform parent, string label, out Text text)
    {
        var button = ButtonRoot(parent, 54f, out RectTransform face, out UiPressFeedback fx);
        var bg = Box("Bg", face, MainFill, 14f);
        Stretch(bg.rectTransform);
        fx.shadow = HardShadow(bg, MainShadow, 6f);
        fx.shadowDepth = 6f;
        fx.depth = 4f;
        text = Display(Label(face, label, 20, Ink));
        Stretch(text.rectTransform);
        fx.AddSwap(bg, text);
        return button;
    }

    /// <summary>Pill-shaped secondary button (style.css `.btn-sub`); the border turns cyan on hover.</summary>
    public static Button SubButton(Transform parent, string label, out Text text)
    {
        var button = ButtonRoot(parent, 46f, out RectTransform face, out UiPressFeedback fx);
        var bg = Box("Bg", face, Panel, 23f, Line, 2f);
        Stretch(bg.rectTransform);
        fx.shadow = HardShadow(bg, PanelShadow, 4f);
        fx.shadowDepth = 4f;
        fx.depth = 3f;
        fx.border = bg;
        fx.borderNormal = Line;
        fx.borderHover = Accent;
        text = Label(face, label, 18, Fg);
        Stretch(text.rectTransform);
        fx.AddSwap(bg.transform.Find("Fill").GetComponent<Image>(), text);
        return button;
    }

    /// <summary>Pill button in any colour with a hard shadow underneath (the 2nd-floor game's buttons).</summary>
    public static Button ColorButton(Transform parent, string label, Color fill, Color ink, Color shadow,
        float height, int fontSize, out Text text)
    {
        var button = ButtonRoot(parent, height, out RectTransform face, out UiPressFeedback fx);
        var bg = Box("Bg", face, fill, height * 0.5f);
        Stretch(bg.rectTransform);
        fx.shadow = HardShadow(bg, shadow, 4f);
        fx.shadowDepth = 4f;
        fx.depth = 2f;
        text = Label(face, label, fontSize, ink);
        Stretch(text.rectTransform);
        fx.AddSwap(bg, text);
        return button;
    }

    static Button ButtonRoot(Transform parent, float height, out RectTransform face, out UiPressFeedback fx)
    {
        var root = Rect("Button", parent);
        root.sizeDelta = new Vector2(200f, height);
        var hit = root.gameObject.AddComponent<Image>();
        hit.color = Color.clear;                                  // invisible raycast target
        var button = root.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = hit;
        root.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
        face = Stretch(Rect("Face", root));
        fx = root.gameObject.AddComponent<UiPressFeedback>();
        fx.face = face;
        fx.button = button;
        return button;
    }

    /// <summary>A horizontal row of differently coloured text runs, e.g. 「ようこそ！」「1階ゲーム」「へ〜」.</summary>
    public static RectTransform Headline(Transform parent, int size, params (string text, Color color, Color shadow)[] parts)
    {
        var row = Rect("Headline", parent);
        var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;
        foreach (var (text, color, shadow) in parts)
        {
            var t = Label(row, text, size, color);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            HardShadow(t, shadow, 3f);
        }
        return row;
    }
}
