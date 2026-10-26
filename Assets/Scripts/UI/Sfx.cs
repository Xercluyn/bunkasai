using System;
using UnityEngine;

/// <summary>Synthesised sound effects (no audio files), shared by the minigames.</summary>
public static class Sfx
{
    const int Rate = 44100;
    public enum Wave { Square, Sawtooth, Triangle }

    public static AudioClip Good() => Make("good",
        (660, 0f, .12f, Wave.Square, .12f, 0), (990, .09f, .2f, Wave.Square, .12f, 0));

    public static AudioClip Bad() => Make("bad",
        (220, 0f, .35f, Wave.Sawtooth, .14f, 90), (150, .05f, .4f, Wave.Square, .08f, 70));

    public static AudioClip Win() => Make("win",
        (523, 0f, .22f, Wave.Square, .11f, 0), (659, .11f, .22f, Wave.Square, .11f, 0),
        (784, .22f, .22f, Wave.Square, .11f, 0), (1047, .33f, .22f, Wave.Square, .11f, 0),
        (1047, .45f, .5f, Wave.Triangle, .12f, 0));

    public static AudioClip Make(string name, params (float freq, float at, float dur, Wave wave, float vol, float slideTo)[] tones)
    {
        float length = 0f;
        foreach (var t in tones) length = Mathf.Max(length, t.at + t.dur + 0.05f);
        var data = new float[Mathf.CeilToInt(length * Rate)];

        foreach (var (freq, at, dur, wave, vol, slideTo) in tones)
        {
            int start = Mathf.RoundToInt(at * Rate);
            int count = Mathf.RoundToInt((dur + 0.02f) * Rate);
            double phase = 0;
            for (int i = 0; i < count && start + i < data.Length; i++)
            {
                float t = (float)i / Rate;
                float f = slideTo > 0 ? freq * Mathf.Pow(slideTo / freq, Mathf.Min(t / dur, 1f)) : freq;
                phase += f / Rate;
                float p = (float)(phase - Math.Floor(phase));
                float osc = wave == Wave.Square ? (p < 0.5f ? 1f : -1f)
                          : wave == Wave.Sawtooth ? 2f * p - 1f
                          : 4f * Mathf.Abs(p - 0.5f) - 1f;
                // exponential attack to vol in 10ms, exponential decay to silence at dur
                float env = t < 0.01f ? 0.0001f * Mathf.Pow(vol / 0.0001f, t / 0.01f)
                          : t < dur ? vol * Mathf.Pow(0.0001f / vol, (t - 0.01f) / (dur - 0.01f))
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
