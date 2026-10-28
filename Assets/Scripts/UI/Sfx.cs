using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Synthesised sound effects (no audio files), shared by the floors and the minigames. Named clips are built once
/// and cached; Volume is the game-wide volume (saved, set from the pause menu); PlayUi plays a clip on a shared 2D
/// source for code that has no AudioSource of its own (buttons, toasts).
/// </summary>
public static class Sfx
{
    const int Rate = 44100;
    public enum Wave { Square, Sawtooth, Triangle, Noise, Sine }

    // ---------- volume ----------
    const string VolumeKey = "sfxVolume";

    /// <summary>Game-wide volume 0–1 (AudioListener.volume), saved across sessions.</summary>
    public static float Volume
    {
        get => PlayerPrefs.GetFloat(VolumeKey, 0.8f);
        set
        {
            value = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(VolumeKey, value);
            AudioListener.volume = value;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void ApplyVolume() => AudioListener.volume = Volume;

    // ---------- cache and the shared source ----------
    static readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();
    static AudioSource ui;

    // clips are destroyed when Play mode ends; domain reload is off, so forget them
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void NewPlay()
    {
        cache.Clear();
        ui = null;
    }

    /// <summary>The clip called `name`, built by `build` the first time.</summary>
    public static AudioClip Cached(string name, Func<AudioClip> build)
    {
        if (!cache.TryGetValue(name, out var clip) || clip == null)
            cache[name] = clip = build();
        return clip;
    }

    /// <summary>Play on a shared 2D source that survives scene loads (buttons, toasts, anything without a source).</summary>
    public static void PlayUi(AudioClip clip, float volume = 1f, float pitch = 1f)
    {
        if (clip == null) return;
        if (ui == null)
        {
            var go = new GameObject("SfxUi");
            UnityEngine.Object.DontDestroyOnLoad(go);
            ui = go.AddComponent<AudioSource>();
            ui.playOnAwake = false;
            ui.spatialBlend = 0f;
            ui.ignoreListenerPause = true;
        }
        if (Mathf.Approximately(pitch, 1f)) { ui.pitch = 1f; ui.PlayOneShot(clip, volume); return; }
        // a pitched one-shot needs its own source so it doesn't bend sounds already playing
        var one = ui.gameObject.AddComponent<AudioSource>();
        one.spatialBlend = 0f;
        one.pitch = pitch;
        one.PlayOneShot(clip, volume);
        UnityEngine.Object.Destroy(one, clip.length / Mathf.Max(0.1f, pitch) + 0.1f);
    }

    // ---------- the game's sounds ----------
    public static AudioClip Good() => Cached("good", () => Make("good",
        (660, 0f, .12f, Wave.Square, .12f, 0), (990, .09f, .2f, Wave.Square, .12f, 0)));

    public static AudioClip Bad() => Cached("bad", () => Make("bad",
        (220, 0f, .35f, Wave.Sawtooth, .14f, 90), (150, .05f, .4f, Wave.Square, .08f, 70)));

    public static AudioClip Win() => Cached("win", () => Make("win",
        (523, 0f, .22f, Wave.Square, .11f, 0), (659, .11f, .22f, Wave.Square, .11f, 0),
        (784, .22f, .22f, Wave.Square, .11f, 0), (1047, .33f, .22f, Wave.Square, .11f, 0),
        (1047, .45f, .5f, Wave.Triangle, .12f, 0)));

    /// <summary>A footstep, variant 0–3: a short filtered scuff over a soft low thump.</summary>
    public static AudioClip Step(int variant)
    {
        variant = Mathf.Abs(variant) % 4;
        return Cached("step" + variant, () => Make("step" + variant,
            (900 + variant * 160, 0f, .055f, Wave.Noise, .22f, 500 + variant * 60),
            (85 + variant * 9, 0f, .045f, Wave.Triangle, .18f, 60)));
    }

    /// <summary>Landing after a fall.</summary>
    public static AudioClip Land() => Cached("land", () => Make("land",
        (700, 0f, .12f, Wave.Noise, .26f, 250), (70, 0f, .1f, Wave.Triangle, .22f, 45)));

    /// <summary>A prompt or hint appearing.</summary>
    public static AudioClip Blip() => Cached("blip", () => Make("blip",
        (880, 0f, .07f, Wave.Sine, .12f, 1320)));

    /// <summary>A button press.</summary>
    public static AudioClip Click() => Cached("click", () => Make("click",
        (1800, 0f, .025f, Wave.Noise, .12f, 1200), (1200, 0f, .03f, Wave.Sine, .08f, 900)));

    /// <summary>Picking something up, a toast appearing.</summary>
    public static AudioClip Pop() => Cached("pop", () => Make("pop",
        (420, 0f, .09f, Wave.Sine, .16f, 900)));

    /// <summary>A right answer (the same as Good).</summary>
    public static AudioClip Correct() => Good();

    /// <summary>A wrong answer: softer than Bad.</summary>
    public static AudioClip Wrong() => Cached("wrong", () => Make("wrong",
        (320, 0f, .18f, Wave.Triangle, .16f, 200), (240, .07f, .16f, Wave.Triangle, .1f, 160)));

    /// <summary>Countdown 3, 2, 1.</summary>
    public static AudioClip Tick() => Cached("tick", () => Make("tick",
        (660, 0f, .12f, Wave.Square, .1f, 0)));

    /// <summary>Countdown スタート！</summary>
    public static AudioClip Go() => Cached("go", () => Make("go",
        (1320, 0f, .3f, Wave.Square, .1f, 0), (1980, .02f, .28f, Wave.Sine, .08f, 0)));

    /// <summary>One of the last seconds on a timer.</summary>
    public static AudioClip Warn() => Cached("warn", () => Make("warn",
        (1000, 0f, .05f, Wave.Square, .08f, 0)));

    /// <summary>A rush of air: doors, menus opening, leaving for a minigame.</summary>
    public static AudioClip Whoosh() => Cached("whoosh", () => Make("whoosh",
        (400, 0f, .35f, Wave.Noise, .2f, 2500)));

    /// <summary>Money collected.</summary>
    public static AudioClip Coin() => Cached("coin", () => Make("coin",
        (988, 0f, .08f, Wave.Square, .1f, 0), (1319, .07f, .25f, Wave.Square, .1f, 0)));

    /// <summary>Something heavy and worthless: a rock, a bump.</summary>
    public static AudioClip Thud() => Cached("thud", () => Make("thud",
        (200, 0f, .18f, Wave.Noise, .3f, 120), (60, 0f, .2f, Wave.Triangle, .25f, 40)));

    // ---------- synthesis ----------
    /// <summary>A clip from tones (freq Hz, start s, length s, wave, volume, pitch slide target or 0). For Noise, freq is
    /// the low-pass cutoff and slideTo slides it.</summary>
    public static AudioClip Make(string name, params (float freq, float at, float dur, Wave wave, float vol, float slideTo)[] tones)
    {
        float length = 0f;
        foreach (var t in tones) length = Mathf.Max(length, t.at + t.dur + 0.05f);
        var data = new float[Mathf.CeilToInt(length * Rate)];

        int seed = 17;
        foreach (char c in name) seed = seed * 31 + c;   // stable across runs (string.GetHashCode may not be)
        foreach (var (freq, at, dur, wave, vol, slideTo) in tones)
        {
            int start = Mathf.RoundToInt(at * Rate);
            int count = Mathf.RoundToInt((dur + 0.02f) * Rate);
            double phase = 0;
            var rng = new System.Random(seed++);   // fixed per tone, so a clip always sounds the same
            float lp = 0f;
            for (int i = 0; i < count && start + i < data.Length; i++)
            {
                float t = (float)i / Rate;
                float f = slideTo > 0 ? freq * Mathf.Pow(slideTo / freq, Mathf.Min(t / dur, 1f)) : freq;
                float osc;
                if (wave == Wave.Noise)
                {
                    // white noise through a one-pole low-pass at f
                    float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                    float a = 1f - Mathf.Exp(-2f * Mathf.PI * f / Rate);
                    lp += a * (white - lp);
                    osc = Mathf.Clamp(lp * 2.5f, -1f, 1f);
                }
                else
                {
                    phase += f / Rate;
                    float p = (float)(phase - Math.Floor(phase));
                    osc = wave == Wave.Square ? (p < 0.5f ? 1f : -1f)
                        : wave == Wave.Sawtooth ? 2f * p - 1f
                        : wave == Wave.Sine ? Mathf.Sin(2f * Mathf.PI * p)
                        : 4f * Mathf.Abs(p - 0.5f) - 1f;
                }
                // exponential attack to vol in 10ms (3ms for very short sounds), exponential decay to silence at dur
                float attack = Mathf.Min(0.01f, dur * 0.3f);
                float env = t < attack ? 0.0001f * Mathf.Pow(vol / 0.0001f, t / attack)
                          : t < dur ? vol * Mathf.Pow(0.0001f / vol, (t - attack) / (dur - attack))
                          : 0f;
                data[start + i] += osc * env;
            }
        }
        for (int i = 0; i < data.Length; i++) data[i] = Mathf.Clamp(data[i], -1f, 1f);

        var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
