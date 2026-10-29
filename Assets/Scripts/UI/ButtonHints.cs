using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The minigames' control hints as buttons: each hint draws what to press like the real thing (the pad's coloured
/// face buttons, the stick, keyboard keycaps) followed by what it does. A row shows the keyboard + mouse or the
/// gamepad, whichever the player used last, redraws when they switch or change the language, and shrinks to fit
/// its rect; the rect's height sets the buttons' size.
/// </summary>
public static class ButtonHints
{
    public enum Pad { South, East, West, North, Menu, Select, Stick }

    // keys whose names change with the language
    public const string Click = "\u0001click", RightClick = "\u0001right", Drag = "\u0001drag";

    public sealed class Hint
    {
        internal string ja, en;
        internal string[] keys;   // null: hidden on keyboard + mouse
        internal Pad[] pad;       // null: hidden on the pad
    }

    /// <summary>Shown on both: `keys` on keyboard + mouse, `pad` on the gamepad.</summary>
    public static Hint Of(string ja, string en, string[] keys, params Pad[] pad) =>
        new Hint { ja = ja, en = en, keys = keys, pad = pad };

    /// <summary>Keyboard + mouse only; with no keys it's just the text.</summary>
    public static Hint Keys(string ja, string en, params string[] keys) => new Hint { ja = ja, en = en, keys = keys };

    /// <summary>Gamepad only.</summary>
    public static Hint OnPad(string ja, string en, params Pad[] pad) => new Hint { ja = ja, en = en, pad = pad };

    /// <summary>A row of hints under `parent`; place and size the returned rect.</summary>
    public static RectTransform Row(Transform parent, Color ink, TextAnchor align, params Hint[] hints)
    {
        var rt = UiKit.Rect("ButtonHints", parent);
        var row = rt.gameObject.AddComponent<ButtonHintRow>();
        row.hints = hints;
        row.ink = ink;
        row.align = align;
        return rt;
    }

    internal static string KeyName(string key) =>
        key == Click ? L.T("クリック", "Click") :
        key == RightClick ? L.T("右クリック", "Right-click") :
        key == Drag ? L.T("ドラッグ", "Drag") : key;
}

/// <summary>Draws a ButtonHints row (see ButtonHints.Row).</summary>
public sealed class ButtonHintRow : MonoBehaviour
{
    internal ButtonHints.Hint[] hints;
    internal Color ink;
    internal TextAnchor align;

    static readonly Color KeyFill = UiKit.Hex("#f1f6ff"), KeyEdge = UiKit.Hex("#9db3d1"), KeyShadow = UiKit.Hex("#5d76a0"),
        PadDark = UiKit.Hex("#262b36"), PadShadow = UiKit.Hex("#0b0e14", 0.7f);
    // Xbox: coloured buttons with white letters (dark on yellow); PlayStation: dark buttons with coloured symbols
    static readonly Color XboxA = UiKit.Hex("#2fa84f"), XboxB = UiKit.Hex("#e0393e"), XboxX = UiKit.Hex("#2b74e0"),
        XboxY = UiKit.Hex("#f0b90b"), PsCross = UiKit.Hex("#8aa6ff"), PsCircle = UiKit.Hex("#ff6b7a"),
        PsSquare = UiKit.Hex("#f08ad6"), PsTriangle = UiKit.Hex("#4fd6b0");

    bool pad, playStation, dirty = true;
    Vector2 builtSize;
    RectTransform content;

    void OnEnable()
    {
        L.Changed += MarkDirty;
        dirty = true;
    }

    void OnDisable() => L.Changed -= MarkDirty;

    void MarkDirty() => dirty = true;

    // built in LateUpdate, once the caller has placed the rect
    void LateUpdate()
    {
        var size = ((RectTransform)transform).rect.size;
        if (pad != GamepadInput.UsingPad || playStation != GamepadInput.IsPlayStation || size != builtSize) dirty = true;
        if (dirty && size.x > 0f && size.y > 0f) Build(size);
    }

    void Build(Vector2 size)
    {
        dirty = false;
        builtSize = size;
        pad = GamepadInput.UsingPad;
        playStation = GamepadInput.IsPlayStation;
        if (content != null) Destroy(content.gameObject);
        content = UiKit.Rect("Content", transform);

        float h = size.y, x = 0f;
        int font = Mathf.RoundToInt(h * 0.7f);
        bool first = true;
        foreach (var hint in hints)
        {
            int n = pad ? (hint.pad?.Length ?? -1) : (hint.keys?.Length ?? -1);
            if (n < 0) continue;
            if (!first) x += h * 0.8f;
            first = false;
            for (int i = 0; i < n; i++)
            {
                if (i > 0) x += h * 0.15f + Words("/", x + h * 0.15f, h, font) + h * 0.15f;
                x += pad ? PadButton(hint.pad[i], x, h) : Key(ButtonHints.KeyName(hint.keys[i]), x, h);
            }
            string words = L.T(hint.ja, hint.en);   // empty: just the buttons (they sit under the button they press)
            if (words.Length == 0) continue;
            if (n > 0) x += h * 0.25f;
            x += Words(words, x, h, font);
        }

        // shrink to fit the rect, then align in it
        float scale = x > size.x ? size.x / x : 1f;
        float ax = align == TextAnchor.MiddleLeft || align == TextAnchor.UpperLeft || align == TextAnchor.LowerLeft ? 0f
                 : align == TextAnchor.MiddleRight || align == TextAnchor.UpperRight || align == TextAnchor.LowerRight ? 1f : 0.5f;
        content.anchorMin = content.anchorMax = content.pivot = new Vector2(ax, 0.5f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = new Vector2(x, h);
        content.localScale = new Vector3(scale, scale, 1f);
    }

    // at x along the row, vertically centred
    RectTransform Put(RectTransform rt, float x, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = new Vector2(x, 0f);
        rt.sizeDelta = new Vector2(w, h);
        return rt;
    }

    float Words(string text, float x, float h, int font)
    {
        var t = UiKit.Label(content, text, font, ink, TextAnchor.MiddleLeft);
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        float w = t.preferredWidth + 1f;
        Put(t.rectTransform, x, w, h);
        return w;
    }

    // a keycap: light, outlined, with a hard shadow underneath
    float Key(string name, float x, float h)
    {
        float ch = h * 0.92f;
        var box = UiKit.Box("Key", content, KeyFill, ch * 0.25f, KeyEdge, Mathf.Max(1f, h * 0.06f));
        UiKit.HardShadow(box, KeyShadow, Mathf.Max(1.5f, h * 0.08f));
        var t = UiKit.Label(box.transform, name, Mathf.RoundToInt(ch * 0.58f), UiKit.CardInk);
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        UiKit.Stretch(t.rectTransform);
        float w = Mathf.Max(ch, t.preferredWidth + ch * 0.55f);
        Put(box.rectTransform, x, w, ch);
        return w;
    }

    float PadButton(ButtonHints.Pad b, float x, float h)
    {
        switch (b)
        {
            case ButtonHints.Pad.South: return playStation ? Disc("×", PadDark, PsCross, x, h, 0.8f) : Disc("A", XboxA, Color.white, x, h);
            case ButtonHints.Pad.East: return playStation ? Disc("○", PadDark, PsCircle, x, h, 0.8f) : Disc("B", XboxB, Color.white, x, h);
            case ButtonHints.Pad.West: return playStation ? Disc("□", PadDark, PsSquare, x, h, 0.75f) : Disc("X", XboxX, Color.white, x, h);
            case ButtonHints.Pad.North: return playStation ? Disc("△", PadDark, PsTriangle, x, h, 0.75f) : Disc("Y", XboxY, UiKit.CardInk, x, h);
            case ButtonHints.Pad.Menu: return playStation ? Pill("OPTIONS", x, h) : Disc("≡", PadDark, Color.white, x, h, 0.8f);
            case ButtonHints.Pad.Select: return Pill(GamepadInput.SelectName, x, h);
            default:   // the left stick: a ringed thumb pad
                float w = Disc("L", PadDark, Color.white, x, h, 0.5f);
                var ring = UiKit.Img("Ring", content, UiKit.Ring, UiKit.WithAlpha(Color.white, 0.55f));
                Put(ring.rectTransform, x + h * 0.1f, h * 0.8f, h * 0.8f);
                return w;
        }
    }

    float Disc(string glyph, Color fill, Color glyphColor, float x, float h, float glyphSize = 0.62f)
    {
        var disc = UiKit.Img("Button", content, UiKit.Circle, fill);
        UiKit.HardShadow(disc, PadShadow, Mathf.Max(1.5f, h * 0.08f));
        Put(disc.rectTransform, x, h, h);
        var t = UiKit.Label(disc.transform, glyph, Mathf.RoundToInt(h * glyphSize), glyphColor);
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        UiKit.Stretch(t.rectTransform);
        return h;
    }

    // a small dark button with a name on it (OPTIONS, View, SHARE)
    float Pill(string name, float x, float h)
    {
        float ch = h * 0.8f;
        var box = UiKit.Box("Button", content, PadDark, ch * 0.5f);
        UiKit.HardShadow(box, PadShadow, Mathf.Max(1.5f, h * 0.08f));
        var t = UiKit.Label(box.transform, name, Mathf.RoundToInt(ch * 0.55f), Color.white);
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        UiKit.Stretch(t.rectTransform);
        float w = t.preferredWidth + ch * 0.9f;
        Put(box.rectTransform, x, w, ch);
        return w;
    }
}
