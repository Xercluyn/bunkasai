using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Shared pieces for the minigames, so they all start and end the same way: a how-to-play card with a Start
/// button, a 3-2-1 countdown, a result hold that leaves on its own (or on click / Space) and small feedback
/// tweens. A game that has ended (won or lost) goes back to its floor, and its play spot there stays shut for
/// ReplayWait seconds (Ended / WaitLeft). Everything runs on unscaled time and is built with UiKit; pass `scale` for canvases whose reference
/// resolution isn't 1280×720 (4F uses 1920×1080: 1.5).
/// </summary>
public static class MinigameKit
{
    /// <summary>Seconds a win/result stays before the game leaves by itself.</summary>
    public const float ResultHold = 3f;

    /// <summary>Seconds an ended minigame can't be started again from its floor.</summary>
    public const float ReplayWait = 15f;

    // minigame scene → when it last ended (realtime, so it runs on through scene loads and pauses)
    static readonly System.Collections.Generic.Dictionary<string, float> endedAt =
        new System.Collections.Generic.Dictionary<string, float>();

    /// <summary>The minigame in `scene` has just ended (won or lost): its play spot waits ReplayWait seconds.</summary>
    public static void Ended(string scene)
    {
        if (!string.IsNullOrEmpty(scene)) endedAt[scene] = Time.realtimeSinceStartup;
    }

    /// <summary>Seconds left before the minigame in `scene` can be played again (0 when it can).</summary>
    public static float WaitLeft(string scene) =>
        !string.IsNullOrEmpty(scene) && endedAt.TryGetValue(scene, out var t)
            ? Mathf.Max(0f, ReplayWait - (Time.realtimeSinceStartup - t))
            : 0f;

    /// <summary>The 2D AudioSource on `host`, added if missing.</summary>
    public static AudioSource Audio(GameObject host)
    {
        var a = host.GetComponent<AudioSource>();
        if (a == null) a = host.AddComponent<AudioSource>();
        a.playOnAwake = false;
        a.spatialBlend = 0f;
        return a;
    }

    // ---------- how to play ----------
    /// <summary>A full-screen card: title, numbered rules, スタート！. Start, Space or Enter closes it and calls
    /// onStart. Text follows the language switch. Returns the card (destroyed when it closes).</summary>
    public static GameObject HowToPlay(Transform canvas, string titleJa, string titleEn,
        (string ja, string en)[] rules, Action onStart, float scale = 1f,
        string buttonJa = "スタート！", string buttonEn = "Start!")
    {
        var dim = UiKit.Img("HowToPlay", canvas, null, UiKit.WithAlpha(UiKit.Dim, 0.82f));
        dim.raycastTarget = true;   // blocks the game underneath
        UiKit.Stretch(dim.rectTransform);
        dim.transform.SetAsLastSibling();

        var panel = UiKit.Box("Panel", dim.transform, UiKit.Panel, 24f * scale, UiKit.Accent, 3f * scale).rectTransform;
        UiKit.Center(panel, 620f * scale, 300f * scale);
        var v = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        int pad = Mathf.RoundToInt(28f * scale);
        v.padding = new RectOffset(pad, pad, pad, pad);
        v.spacing = 12f * scale;
        v.childAlignment = TextAnchor.UpperCenter;
        v.childControlWidth = v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var kicker = UiKit.Label(panel, "", Mathf.RoundToInt(18 * scale), UiKit.Accent2);
        var title = UiKit.Display(UiKit.Label(panel, "", Mathf.RoundToInt(44 * scale), UiKit.Accent));
        UiKit.Fit(title, Mathf.RoundToInt(44 * scale), Mathf.RoundToInt(26 * scale));   // a long name shrinks onto one line
        title.gameObject.AddComponent<LayoutElement>().preferredHeight = 56f * scale;
        UiKit.HardShadow(title, UiKit.AccentShadow, 4f * scale);

        var ruleTexts = new Text[rules.Length];
        for (int i = 0; i < rules.Length; i++)
        {
            var row = UiKit.Box("Rule", panel, UiKit.Bg2, 12f * scale);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            int rp = Mathf.RoundToInt(10f * scale);
            h.padding = new RectOffset(rp, rp, rp, rp);
            h.spacing = 12f * scale;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            var num = UiKit.Display(UiKit.Label(row.transform, (i + 1).ToString(), Mathf.RoundToInt(24 * scale), UiKit.Accent));
            var ne = num.gameObject.AddComponent<LayoutElement>();
            ne.minWidth = ne.preferredWidth = 32f * scale;
            var t = UiKit.Label(row.transform, "", Mathf.RoundToInt(21 * scale), UiKit.Fg, TextAnchor.MiddleLeft);
            t.lineSpacing = 1.15f;
            t.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            ruleTexts[i] = t;
        }

        var hint = UiKit.Rect("Keys", panel);   // the start buttons, drawn by ButtonHints below
        hint.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f * scale;
        var button = UiKit.MainButton(panel, "", out var buttonText);
        buttonText.fontSize = Mathf.RoundToInt(24 * scale);
        button.GetComponent<LayoutElement>().preferredHeight = 60f * scale;

        var card = dim.gameObject.AddComponent<HowToPlayCard>();
        card.onStart = onStart;
        card.write = () =>
        {
            kicker.text = L.T("あそびかた", "HOW TO PLAY");
            title.text = L.T(titleJa, titleEn);
            for (int i = 0; i < rules.Length; i++) ruleTexts[i].text = L.T(rules[i].ja, rules[i].en);
            buttonText.text = L.T(buttonJa, buttonEn);
        };
        card.write();
        var keys = ButtonHints.Row(hint.transform, UiKit.Muted, TextAnchor.MiddleCenter,
            ButtonHints.Of("スタート", "Start", new[] { "Space", "Enter" }, ButtonHints.Pad.South));
        UiKit.Stretch(keys);
        button.onClick.AddListener(card.Close);
        GamepadInput.SetDefault(button, GamepadInput.CardPriority);
        GamepadInput.NoPad(button);   // the card's only button: the stick has nowhere to go but the game behind
        return dim.gameObject;
    }

    class HowToPlayCard : MonoBehaviour
    {
        public Action onStart, write;
        bool closed;
        float opened;

        void OnEnable()
        {
            opened = Time.unscaledTime;
            L.Changed += Rewrite;
        }

        void OnDisable() => L.Changed -= Rewrite;
        void Rewrite() => write?.Invoke();

        void Update()
        {
            var kb = Keyboard.current;
            if (Time.unscaledTime - opened > 0.3f &&
                ((kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) ||
                 GamepadInput.InteractPressed))
                Close();
        }

        public void Close()
        {
            if (closed) return;
            closed = true;
            Destroy(gameObject);
            onStart?.Invoke();
        }
    }

    // ---------- countdown ----------
    /// <summary>Big 3, 2, 1 punching in, then スタート！; calls onGo after about 2.6 s. Blocks clicks meanwhile.</summary>
    public static Coroutine Countdown(MonoBehaviour host, Transform canvas, Action onGo, float scale = 1f)
        => host.StartCoroutine(CountdownRoutine(host, canvas, onGo, scale));

    static IEnumerator CountdownRoutine(MonoBehaviour host, Transform canvas, Action onGo, float scale)
    {
        var layer = UiKit.Img("Countdown", canvas, null, UiKit.WithAlpha(UiKit.Dim, 0.35f));
        layer.raycastTarget = true;
        UiKit.Stretch(layer.rectTransform);
        layer.transform.SetAsLastSibling();
        var label = UiKit.Display(UiKit.Label(layer.transform, "", Mathf.RoundToInt(170 * scale), UiKit.Accent));
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        UiKit.HardShadow(label, UiKit.AccentShadow, 8f * scale);
        UiKit.Center(label.rectTransform, 900f * scale, 260f * scale);
        var audio = Audio(host.gameObject);

        string[] steps = { "3", "2", "1", null };
        foreach (var s in steps)
        {
            bool go = s == null;
            label.text = go ? L.T("スタート！", "GO!") : s;
            label.fontSize = Mathf.RoundToInt((go ? 120 : 170) * scale);
            label.color = go ? UiKit.Good : UiKit.Accent;
            audio.PlayOneShot(go ? Sfx.Go() : Sfx.Tick());
            float length = go ? 0.5f : 0.7f;
            for (float t = 0f; t < length; t += Time.unscaledDeltaTime)
            {
                if (layer == null) yield break;   // the game was left meanwhile
                float k = t / length;
                label.rectTransform.localScale = Vector3.one * Mathf.LerpUnclamped(1.6f, 1f, UiKit.EaseOutBack(Mathf.Clamp01(t / 0.35f)));
                var c = label.color;
                c.a = k < 0.75f ? 1f : 1f - (k - 0.75f) / 0.25f;
                label.color = c;
                yield return null;
            }
        }
        if (layer != null) UnityEngine.Object.Destroy(layer.gameObject);
        onGo?.Invoke();
    }

    // ---------- result hold ----------
    /// <summary>A thin bar along the bottom that fills over `seconds`, with 「クリック / Space で次へ」; then exit runs
    /// (once). A click, Space or Enter skips the wait.</summary>
    public static Coroutine ResultThenExit(MonoBehaviour host, Transform canvas, Action exit,
        float seconds = ResultHold, float scale = 1f)
        => host.StartCoroutine(ResultRoutine(canvas, exit, seconds, scale));

    static IEnumerator ResultRoutine(Transform canvas, Action exit, float seconds, float scale)
    {
        var holder = UiKit.Rect("ResultHold", canvas);
        holder.anchorMin = new Vector2(0.5f, 0f);
        holder.anchorMax = new Vector2(0.5f, 0f);
        holder.pivot = new Vector2(0.5f, 0f);
        holder.anchoredPosition = new Vector2(0f, 18f * scale);
        holder.sizeDelta = new Vector2(420f * scale, 44f * scale);
        holder.SetAsLastSibling();
        var label = ButtonHints.Row(holder, UiKit.Fg, TextAnchor.MiddleCenter,
            ButtonHints.Of("次へ", "Continue", new[] { ButtonHints.Click, "Space" }, ButtonHints.Pad.South));
        UiKit.Place(label, 0f, 0f, 420f * scale, 24f * scale);
        var track = UiKit.Box("Track", holder, UiKit.WithAlpha(UiKit.Dim, 0.7f), 4f * scale);
        UiKit.Place(track.rectTransform, 0f, 30f * scale, 420f * scale, 8f * scale);
        var fill = UiKit.Box("Fill", track.transform, UiKit.Accent, 4f * scale).rectTransform;
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = new Vector2(0f, 1f);
        fill.offsetMin = fill.offsetMax = Vector2.zero;

        for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
        {
            if (holder == null) yield break;
            fill.anchorMax = new Vector2(t / seconds, 1f);
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            bool skip = t > 0.4f &&
                ((kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) ||
                 (mouse != null && mouse.leftButton.wasPressedThisFrame) || GamepadInput.InteractPressed);
            if (skip) break;
            yield return null;
        }
        if (holder != null) UnityEngine.Object.Destroy(holder.gameObject);
        exit?.Invoke();
    }

    // ---------- feedback ----------
    /// <summary>Shake sideways by `px`, easing out over `dur`.</summary>
    public static void Shake(MonoBehaviour host, RectTransform rt, float px = 10f, float dur = 0.3f)
        => host.StartCoroutine(ShakeRoutine(rt, px, dur));

    // rects shaking now and where they rest, so a second shake on the same rect doesn't leave it offset
    static readonly System.Collections.Generic.Dictionary<RectTransform, (Vector2 home, int id)> shaking =
        new System.Collections.Generic.Dictionary<RectTransform, (Vector2, int)>();
    static int shakeIds;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void NewPlay()
    {
        shaking.Clear();
        endedAt.Clear();
    }

    static IEnumerator ShakeRoutine(RectTransform rt, float px, float dur)
    {
        if (rt == null) yield break;
        Vector2 home = shaking.TryGetValue(rt, out var running) ? running.home : rt.anchoredPosition;
        int id = ++shakeIds;
        shaking[rt] = (home, id);
        for (float t = 0f; t < dur; t += Time.unscaledDeltaTime)
        {
            if (rt == null || !shaking.TryGetValue(rt, out var now) || now.id != id) yield break;   // gone, or a newer shake took over
            float k = 1f - t / dur;
            rt.anchoredPosition = home + new Vector2(Mathf.Sin(t * 60f) * px * k, 0f);
            yield return null;
        }
        shaking.Remove(rt);
        if (rt != null) rt.anchoredPosition = home;
    }

    /// <summary>Scale up to `to` and back over `dur`.</summary>
    public static void Punch(MonoBehaviour host, RectTransform rt, float to = 1.15f, float dur = 0.2f)
        => host.StartCoroutine(PunchRoutine(rt, to, dur));

    static IEnumerator PunchRoutine(RectTransform rt, float to, float dur)
    {
        if (rt == null) yield break;
        Vector3 home = rt.localScale;
        for (float t = 0f; t < dur; t += Time.unscaledDeltaTime)
        {
            if (rt == null) yield break;
            rt.localScale = home * Mathf.Lerp(1f, to, Mathf.Sin(t / dur * Mathf.PI));
            yield return null;
        }
        if (rt != null) rt.localScale = home;
    }
}
