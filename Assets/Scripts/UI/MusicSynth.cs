using System;

/// <summary>
/// A small offline synth for the background music (see MusicScore). Voices add notes into mono float buses that all
/// wrap at Length, so a note's tail spills onto the start and a rendered loop is seamless; the filters and the delay
/// run circularly over two passes and keep the second (steady state at sample 0). Pure System code with no UnityEngine
/// calls, so it is safe on a worker thread; use one instance per render (it owns its noise generator).
/// </summary>
public sealed class MusicSynth
{
    const int SineSize = 4096;
    static readonly float[] sineTable = BuildSine();   // read-only after the type initialiser: thread-safe
    const double Ln1000 = 6.907755;                    // a "decay" below is the time to fall 60 dB
    const double TailDecays = 1.6;                     // a decaying voice stops at 1.6 decays (about -96 dB)
    const double MarimbaDamp = 0.25;                   // seconds a held marimba bar takes to be damped

    /// <summary>Samples per second, and the loop length every bus wraps at.</summary>
    public readonly int Rate, Length;
    readonly Random rng;

    public MusicSynth(int rate, int length, int seed)
    {
        Rate = rate;
        Length = length;
        rng = new Random(seed);
    }

    // ---------- pitch and randomness ----------
    /// <summary>MIDI note from a name: "C5", "F#3", "Bb4" (C4 = 60).</summary>
    public static int Note(string name)
    {
        int pc = "C D EF G A B".IndexOf(char.ToUpperInvariant(name[0]));
        if (pc < 0) throw new ArgumentException("bad note " + name);
        int i = 1;
        if (name[i] == '#') { pc++; i++; }
        else if (name[i] == 'b') { pc--; i++; }
        return 12 * (int.Parse(name.Substring(i)) + 1) + pc;
    }

    public static double Hz(int midi) => 440.0 * Math.Pow(2, (midi - 69) / 12.0);

    /// <summary>Uniform in ±range, from this render's fixed-seed generator.</summary>
    public double Jitter(double range) => (rng.NextDouble() * 2 - 1) * range;

    /// <summary>A whole number of samples (at least 1) for a time in seconds.</summary>
    public int Samples(double seconds) => Math.Max(1, (int)Math.Round(seconds * Rate));

    // ---------- voices ----------
    /// <summary>IT pluck: odd harmonics 1/3/5/7 at 1/k, band-limited, 3 ms attack and an exponential decay.</summary>
    public void Chip(float[] bus, int at, double f, double vol, double decay = 0.14)
    {
        int n = Samples(decay * TailDecays), a = Samples(0.003), j = Wrap(at), top = 1;
        while (top < 7 && (top + 2) * f < 0.45 * Rate) top += 2;
        double inc = f / Rate, env = vol, k = DecayStep(decay);
        for (int i = 0; i < n; i++, env *= k)
        {
            double p = i * inc, s = 0;
            for (int h = 1; h <= top; h += 2) s += Sin(p * h) / h;
            bus[j] += (float)(s * env * Rise(i, a));
            if (++j == Length) j = 0;
        }
    }

    /// <summary>Soft chord tone: 3 voices at -7/0/+7 cents (70% sine + 30% triangle), slow attack and release past
    /// `len` samples, and a slow amplitude LFO.</summary>
    public void Pad(float[] bus, int at, int len, double f, double vol, double attack, double release, double lfoHz, double lfoDepth)
    {
        int a = Samples(attack), r = Samples(release), n = len + r, j = Wrap(at);
        double p0 = rng.NextDouble(), p1 = rng.NextDouble(), p2 = rng.NextDouble(), lfo = rng.NextDouble();
        double i0 = f * Cents(-7) / Rate, i1 = f / Rate, i2 = f * Cents(7) / Rate, lfoInc = lfoHz / Rate;
        double g = vol / Math.Sqrt(3);   // three detuned voices add up about √3 louder
        for (int i = 0; i < n; i++, lfo += lfoInc)
        {
            double s = PadWave(ref p0, i0) + PadWave(ref p1, i1) + PadWave(ref p2, i2);
            bus[j] += (float)(s * g * Env(i, a, len, r) * (1 + lfoDepth * Sin(lfo)));
            if (++j == Length) j = 0;
        }
    }

    /// <summary>A plain sine that swells in and out (the floor's sub and "air" tones).</summary>
    public void Swell(float[] bus, int at, int len, double f, double vol, double attack, double release)
    {
        int a = Samples(attack), r = Samples(release), n = len + r, j = Wrap(at);
        double inc = f / Rate;
        for (int i = 0; i < n; i++)
        {
            bus[j] += (float)(Sin(i * inc) * vol * Env(i, a, len, r));
            if (++j == Length) j = 0;
        }
    }

    /// <summary>Staccato bass (~90 ms): triangle + sine body; `boing` slides up 2 semitones over 30 ms (Onimaru's bounce).</summary>
    public void Bass(float[] bus, int at, double f, double vol, bool boing = false)
    {
        int n = Samples(0.09), a = Samples(0.004), r = Samples(0.03), slide = Samples(0.03), j = Wrap(at);
        double p = 0, up = Math.Pow(2, 2 / 12.0) - 1;
        for (int i = 0; i < n; i++)
        {
            double fi = boing ? f * (1 + up * Smooth(Math.Min(1.0, (double)i / slide))) : f;
            p += fi / Rate;
            if (p >= 1) p -= 1;
            bus[j] += (float)((0.6 * Tri(p) + 0.6 * Sin(p)) * vol * Env(i, a, n - r, r));
            if (++j == Length) j = 0;
        }
    }

    /// <summary>Melody: sine + 0.3 × 2nd harmonic, a slight droop, and a 5.5 Hz ±12 cent vibrato fading in after 150 ms.</summary>
    public void Lead(float[] bus, int at, int len, double f, double vol)
    {
        int a = Samples(0.012), r = Samples(0.08), n = len + r, j = Wrap(at);
        int vibFrom = Samples(0.15), vibFade = Samples(0.25);
        double p = 0, vib = 0, vibInc = 5.5 / Rate, droop = 1, k = Math.Exp(-1.0 / (0.4 * Rate));
        const double CentToRatio = 0.000577623;   // ln 2 / 1200: 1 cent ≈ 1 + this
        for (int i = 0; i < n; i++, vib += vibInc, droop *= k)
        {
            double depth = 12 * Math.Max(0.0, Math.Min(1.0, (double)(i - vibFrom) / vibFade));
            p += f * (1 + depth * Sin(vib) * CentToRatio) / Rate;
            if (p >= 1) p -= 1;
            double s = (Sin(p) + 0.3 * Sin(2 * p)) / 1.3;
            bus[j] += (float)(s * vol * (0.75 + 0.25 * droop) * Env(i, a, len, r));
            if (++j == Length) j = 0;
        }
    }

    /// <summary>Lantern bell: partials f and 2.76f, 2 ms attack, 1.2 s decay.</summary>
    public void Bell(float[] bus, int at, double f, double vol) =>
        Partials(bus, at, vol, 0.002, int.MaxValue, f, 1.2, 2.76 * f, 0.35, 0.5);

    /// <summary>Marimba: partials f and 3.9f, 8 ms attack, 1.4 s decay; damped after `hold` samples.</summary>
    public void Marimba(float[] bus, int at, double f, double vol, int hold = int.MaxValue) =>
        Partials(bus, at, vol, 0.008, hold, f, 1.4, 3.9 * f, 0.15, 0.3);

    /// <summary>Wood block "kon": 820 + 1270 Hz, 50 ms decay.</summary>
    public void Wood(float[] bus, int at, double vol) =>
        Partials(bus, at, vol, 0.001, int.MaxValue, 820, 0.05, 1270, 0.6, 0.04);

    /// <summary>Taiko "don": a sine falling 120→52 Hz over 0.12 s with a 0.4 s decay, over a 25 ms low-passed noise
    /// thump. lowPassHz > 0 muffles the whole hit (distance).</summary>
    public void Taiko(float[] bus, int at, double vol, double lowPassHz = 0)
    {
        int n = Samples(0.4 * TailDecays), a = Samples(0.002), sweep = Samples(0.12), burst = Samples(0.025), j = Wrap(at);
        double p = 0, env = 1, k = DecayStep(0.4), thump = 0, tc = Coef(400), y = 0, yc = lowPassHz > 0 ? Coef(lowPassHz) : 1;
        for (int i = 0; i < n; i++, env *= k)
        {
            p += (i < sweep ? 120 * Math.Pow(52 / 120.0, (double)i / sweep) : 52) / Rate;
            double s = Sin(p) * env * Rise(i, a);
            if (i < burst)
            {
                thump += tc * (Noise() - thump);
                double q = 1 - (double)i / burst;
                s += 1.2 * thump * q * q;
            }
            y += yc * (s - y);
            bus[j] += (float)(y * vol);
            if (++j == Length) j = 0;
        }
    }

    /// <summary>Shaker: noise high-passed at ~3.5 kHz (noise minus its one-pole low-pass), 35 ms decay.</summary>
    public void Shaker(float[] bus, int at, double vol)
    {
        int n = Samples(0.035 * TailDecays), a = Samples(0.002), j = Wrap(at);
        double lp = 0, c = Coef(3500), env = vol, k = DecayStep(0.035);
        for (int i = 0; i < n; i++, env *= k)
        {
            double w = Noise();
            lp += c * (w - lp);
            bus[j] += (float)((w - lp) * env * Rise(i, a));
            if (++j == Length) j = 0;
        }
    }

    // ---------- effects (circular, so the loop point is steady state) ----------
    /// <summary>One-pole low-pass in place.</summary>
    public void LowPass(float[] bus, double hz)
    {
        double c = Coef(hz), y = 0;
        for (int i = 0; i < Length; i++) y += c * (bus[i] - y);   // warm-up pass: the filter state at the loop end
        for (int i = 0; i < Length; i++) bus[i] = (float)(y += c * (bus[i] - y));
    }

    /// <summary>Feedback delay of `send` (the repeats only) added into `dst` at `mix`; repeats are softened by a
    /// low-pass at dampHz in the feedback path.</summary>
    public void Delay(float[] send, float[] dst, int delay, double feedback, double mix, double dampHz)
    {
        if (delay <= 0 || delay >= Length) throw new ArgumentOutOfRangeException(nameof(delay));
        var line = new float[Length];
        double c = Coef(dampHz), y = 0;
        for (int pass = 0; pass < 2; pass++)   // the first pass fills the line from the loop's end
            for (int i = 0, k = Length - delay; i < Length; i++)
            {
                y += c * (line[k] - y);
                line[i] = (float)(send[i] + feedback * y);
                if (++k == Length) k = 0;
            }
        for (int i = 0, k = Length - delay; i < Length; i++)
        {
            dst[i] += (float)(mix * line[k]);
            if (++k == Length) k = 0;
        }
    }

    /// <summary>dst += src × gain.</summary>
    public void Mix(float[] dst, float[] src, double gain)
    {
        for (int i = 0; i < Length; i++) dst[i] += (float)(src[i] * gain);
    }

    /// <summary>Drive, soft clip x/(1+|x|), then normalise to `peak`. Returns the bus.</summary>
    public float[] Master(float[] bus, double drive, double peak)
    {
        double max = 0;
        for (int i = 0; i < Length; i++)
        {
            double x = bus[i] * drive;
            x /= 1 + Math.Abs(x);
            bus[i] = (float)x;
            max = Math.Max(max, Math.Abs(x));
        }
        if (max > 0)
        {
            float g = (float)(peak / max);
            for (int i = 0; i < Length; i++) bus[i] *= g;
        }
        return bus;
    }

    // ---------- helpers ----------
    /// <summary>Two decaying sine partials (the second at relative level a2) with an attack and an optional damping.</summary>
    void Partials(float[] bus, int at, double vol, double attack, int hold, double f1, double d1, double f2, double a2, double d2)
    {
        int a = Samples(attack), damp = Samples(MarimbaDamp), tail = Samples(d1 * TailDecays), j = Wrap(at);
        int n = hold < tail - damp ? hold + damp : tail;
        double e1 = 1, e2 = a2, k1 = DecayStep(d1), k2 = DecayStep(d2), inc1 = f1 / Rate, inc2 = f2 / Rate;
        for (int i = 0; i < n; i++, e1 *= k1, e2 *= k2)
        {
            double env = Rise(i, a) * (i < hold ? 1 : 1 - Smooth((double)(i - hold) / damp));
            bus[j] += (float)((e1 * Sin(i * inc1) + e2 * Sin(i * inc2)) * vol * env);
            if (++j == Length) j = 0;
        }
    }

    static float[] BuildSine()
    {
        var t = new float[SineSize + 2];
        for (int i = 0; i < t.Length; i++) t[i] = (float)Math.Sin(2 * Math.PI * i / SineSize);
        return t;
    }

    /// <summary>sin(2π·cycles), from the table with linear interpolation.</summary>
    static double Sin(double cycles)
    {
        double x = (cycles - Math.Floor(cycles)) * SineSize;
        int i = (int)x;
        return sineTable[i] + (x - i) * (sineTable[i + 1] - sineTable[i]);
    }

    /// <summary>Triangle in phase with Sin (0 at phase 0, +1 at a quarter).</summary>
    static double Tri(double p)
    {
        double q = p + 0.25;
        return 1 - 4 * Math.Abs(q - Math.Floor(q) - 0.5);
    }

    static double PadWave(ref double p, double inc)
    {
        double s = 0.7 * Sin(p) + 0.3 * Tri(p);
        p += inc;
        if (p >= 1) p -= 1;
        return s;
    }

    /// <summary>0→1 along half a cosine (no corner at either end).</summary>
    static double Smooth(double x) => 0.5 - 0.5 * Sin(0.5 * x + 0.25);

    static double Rise(int i, int attack) => i < attack ? Smooth((double)i / attack) : 1;

    /// <summary>Attack over `a` samples, hold to `len`, release over `r`.</summary>
    static double Env(int i, int a, int len, int r) => Rise(i, a) * (i < len ? 1 : 1 - Smooth((double)(i - len) / r));

    static double Cents(double c) => Math.Pow(2, c / 1200);

    double DecayStep(double decay) => Math.Exp(-Ln1000 / (decay * Rate));   // per-sample multiplier
    double Coef(double hz) => 1 - Math.Exp(-2 * Math.PI * hz / Rate);      // one-pole low-pass coefficient
    double Noise() => rng.NextDouble() * 2 - 1;
    int Wrap(int i) { i %= Length; return i < 0 ? i + Length : i; }
}
