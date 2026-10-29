using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// The two background songs as data, rendered with MusicSynth: Menu "Onimaru's Festival" (bouncy chip, taiko and wood
/// block) and Floor "Blue Afternoon" (a soft pad under gameplay). Each call returns one mono loop, exactly one loop
/// long and seamless with AudioSource.loop. No UnityEngine calls and fixed seeds: Music renders these on a worker thread.
/// </summary>
public static class MusicScore
{
    public const int MenuRate = 44100, FloorRate = 22050;

    // ---------- menu: "Onimaru's Festival" (104 BPM, C major, 16 bars) ----------
    const int MStep = 6361, MBars = 16;   // samples per 16th at 104 BPM, 44.1 kHz
    const double MBass = 0.22, MChip = 0.07, MChipIntro = 0.035, MPad = 0.04, MTaiko = 0.35, MWood = 0.08,
        MShaker = 0.03, MBell = 0.04, MLead = 0.11, MLeadB = 0.08;
    const double MPadAttack = 0.35, MPadRelease = 0.6, MPadLfoHz = 0.2, MPadLfoDepth = 0.1, MPadLowPass = 1800;
    const double MShakerHumanise = 0.003, MDelayFeedback = 0.35, MDelayMix = 0.3, MDelayDamp = 5000, MDrive = 1.2, MPeak = 0.8;
    const int MDelaySteps = 3;   // a dotted 8th

    /// <summary>Pitch classes: the bass note, then the chord tones from the root up.</summary>
    static readonly Dictionary<string, (int bass, int[] tones)> Chords = new Dictionary<string, (int, int[])>
    {
        ["C"] = (0, new[] { 0, 4, 7 }), ["Am"] = (9, new[] { 9, 0, 4 }), ["F"] = (5, new[] { 5, 9, 0 }),
        ["G"] = (7, new[] { 7, 11, 2 }), ["Em"] = (4, new[] { 4, 7, 11 }), ["C/E"] = (4, new[] { 0, 4, 7 }),
        ["Dm7"] = (2, new[] { 2, 5, 9, 0 }), ["G7sus4"] = (7, new[] { 7, 0, 2, 5 }),
    };

    /// <summary>One chord per bar; "a+b" splits the bar in halves.</summary>
    static readonly string[] MenuChords =
    {
        "C", "Am", "F", "G",  "C", "Am", "F", "G",  "F", "G", "Em", "Am",  "F", "G", "C/E+Dm7", "G7sus4+G",
    };

    static readonly int[] ArpShape = { 0, 1, 2, 3, 2, 1 };   // R-3-5-8-5-3, 3 = the root an octave up

    // taiko velocity per 16th: the groove, the B section (bars 9–12), and the fill bars (8 and 16)
    static readonly double[] TaikoMain = { 1, 0, 0, 0, 0, 0, .55, 0, .9, 0, 0, 0, 0, 0, .5, 0 };
    static readonly double[] TaikoB = { 1, 0, 0, 0, 0, 0, 0, 0, .9, 0, 0, 0, 0, 0, 0, 0 };
    static readonly double[] TaikoFill = { 1, 0, 0, 0, 0, 0, .55, 0, .9, 0, 0, 0, .5, .63, .77, .9 };

    /// <summary>Lead: (bar 1-based, "16th note 16ths, …").</summary>
    static readonly (int bar, string notes)[] MenuLead =
    {
        (5, "0 E5 2, 2 G5 2, 4 C6 4, 8 B5 2, 10 G5 2, 12 A5 4"),
        (6, "0 A5 3, 3 G5 1, 4 E5 4, 8 C5 2, 10 E5 2, 12 D5 4"),
        (7, "0 C5 2, 2 F5 2, 4 A5 4, 8 G5 2, 10 F5 2, 12 E5 4"),
        (8, "0 D5 4, 4 G5 2, 6 A5 2, 8 B5 4, 12 D6 4"),
        (9, "0 C6 8, 8 A5 8"),
        (10, "0 B5 8, 8 D6 8"),
        (11, "0 G5 8, 8 B5 8"),
        (12, "0 C6 12, 12 E6 4"),
        (13, "0 A5 2, 2 C6 2, 4 A5 2, 6 G5 2, 8 F5 4, 12 A5 4"),
        (14, "0 G5 2, 2 B5 2, 4 D6 4, 8 C6 2, 10 B5 2, 12 G5 4"),
        (15, "0 E6 6, 6 C6 2, 8 D6 4, 12 A5 4"),
        (16, "0 G5 2, 2 C6 2, 4 D6 6, 12 B5 2, 14 D6 2"),
    };

    /// <summary>Lantern bells: (bar 1-based, notes struck together on beat 1).</summary>
    static readonly (int bar, string notes)[] MenuBells = { (1, "C6 E6"), (9, "A5 C6") };

    /// <summary>Renders the menu loop: mono, exactly one loop long, seamless when played with AudioSource.loop.</summary>
    public static float[] Menu()
    {
        const int L = MStep * 16 * MBars;
        var s = new MusicSynth(MenuRate, L, 104);
        float[] dry = new float[L], send = new float[L], pad = new float[L];
        for (int bar = 0; bar < MBars; bar++)
        {
            string[] halves = MenuChords[bar].Split('+');
            int steps = 16 / halves.Length;
            for (int h = 0; h < halves.Length; h++)
            {
                var chord = Chords[halves[h]];
                int first = h * steps;
                foreach (int pc in chord.tones)
                    s.Pad(pad, MAt(bar, first), steps * MStep, MusicSynth.Hz(60 + pc), MPad, MPadAttack, MPadRelease, MPadLfoHz, MPadLfoDepth);
                for (int st = first; st < first + steps; st++)
                {
                    MenuBass(s, dry, chord, bar, st);
                    MenuArp(s, send, chord, bar, st, st - first);
                }
            }
            MenuDrums(s, dry, bar);
        }
        foreach (var (bar, notes) in MenuBells)
            foreach (string n in notes.Split(' '))
                s.Bell(send, MAt(bar - 1, 0), MusicSynth.Hz(MusicSynth.Note(n)), MBell);
        foreach (var (bar, row) in MenuLead)
            foreach (var (step, note, len) in Row(row))
                s.Lead(send, MAt(bar - 1, step), len * MStep, MusicSynth.Hz(note), bar >= 9 && bar <= 12 ? MLeadB : MLead);

        s.LowPass(pad, MPadLowPass);
        s.Delay(send, dry, MDelaySteps * MStep, MDelayFeedback, MDelayMix, MDelayDamp);
        s.Mix(dry, send, 1);
        s.Mix(dry, pad, 1);
        return s.Master(dry, MDrive, MPeak);
    }

    static int MAt(int bar, int step) => (bar * 16 + step) * MStep;

    static bool MenuB(int bar) => bar >= 8 && bar < 12;   // bars 9–12, 0-based

    /// <summary>Bouncy bass: root 0, octave 3, root 6, fifth 8, octave 10, root with a boing 14.</summary>
    static void MenuBass(MusicSynth s, float[] bus, (int bass, int[] tones) chord, int bar, int step)
    {
        int root = 36 + chord.bass;   // octave 2
        int note;
        switch (step)
        {
            case 0: case 6: case 14: note = root; break;
            case 3: case 10: note = root + 12; break;
            case 8: note = Above(root, chord.tones[2]); break;
            default: return;
        }
        s.Bass(bus, MAt(bar, step), MusicSynth.Hz(note), MBass, step == 14);
    }

    /// <summary>Chip arpeggio from C5 up, in 16ths (8ths in the B section), half level in the intro.</summary>
    static void MenuArp(MusicSynth s, float[] bus, (int bass, int[] tones) chord, int bar, int step, int inChord)
    {
        bool b = MenuB(bar);
        if (b && inChord % 2 == 1) return;
        int root = 72 + chord.tones[0], third = Above(root, chord.tones[1]), fifth = Above(third, chord.tones[2]);
        int shape = ArpShape[(b ? inChord / 2 : inChord) % ArpShape.Length];
        int note = shape == 0 ? root : shape == 1 ? third : shape == 2 ? fifth : root + 12;
        s.Chip(bus, MAt(bar, step), MusicSynth.Hz(note), bar < 4 ? MChipIntro : MChip);
    }

    /// <summary>Taiko from bar 5 (fills in bars 8 and 16), wood block on 2 and 4 (+ the "and" of 2 in the B section),
    /// and a soft humanised shaker on every 16th.</summary>
    static void MenuDrums(MusicSynth s, float[] bus, int bar)
    {
        var taiko = MenuB(bar) ? TaikoB : bar == 7 || bar == 15 ? TaikoFill : TaikoMain;
        for (int step = 0; step < 16; step++)
        {
            int at = MAt(bar, step);
            if (bar >= 4 && taiko[step] > 0) s.Taiko(bus, at, MTaiko * taiko[step]);
            if (step == 4 || step == 12 || (step == 7 && MenuB(bar))) s.Wood(bus, at, MWood);
            s.Shaker(bus, at + (int)Math.Round(s.Jitter(MShakerHumanise) * MenuRate), MShaker * (step % 2 == 1 ? 1 : 0.5));
        }
    }

    // ---------- floor: "Blue Afternoon" (78 BPM, C major with a Lydian colour, 32 bars) ----------
    const int FStep = 4240, FBars = 32, FChordBars = 2;   // samples per 16th at 78 BPM, 22.05 kHz
    const double FPad = 0.035, FSub = 0.10, FAir = 0.015, FTaiko = 0.05, FMarimba = 0.10;
    const double FPadAttack = 1.8, FPadRelease = 2.5, FPadLfoHz = 0.08, FPadLfoDepth = 0.15, FPadLowPass = 1100;
    const double FSubAttack = 0.5, FSubRelease = 1.2, FAirAttack = 1.5, FAirRelease = 2.0, FTaikoLowPass = 250;
    const double FMarimbaHumanise = 0.010, FMarimbaVelocity = 0.10;
    const double FDelayFeedback = 0.4, FDelayMix = 0.35, FDelayDamp = 2500, FMixLowPass = 3000, FDrive = 1.0, FPeak = 0.7;
    const int FDelaySteps = 6;   // a dotted quarter

    /// <summary>Voicings, lowest note first (the lowest is the root).</summary>
    static readonly Dictionary<string, string> Voicings = new Dictionary<string, string>
    {
        ["Cmaj9"] = "C3 G3 B3 D4 E4", ["Am9"] = "A2 E3 G3 B3 C4", ["Fmaj7"] = "F2 C3 E3 A3",
        ["G6sus4"] = "G2 D3 E3 C4", ["Fmaj9"] = "F2 C3 E3 G3 A3", ["Em7"] = "E2 B2 D3 G3",
        ["Dm9"] = "D2 A2 C3 E3 F3", ["Fmaj7#11"] = "F2 C3 E3 A3 B3",
    };

    /// <summary>One chord per 2 bars.</summary>
    static readonly string[] FloorChords =
    {
        "Cmaj9", "Am9", "Fmaj7", "G6sus4",  "Cmaj9", "Am9", "Fmaj7", "G6sus4",   // 1–16
        "Fmaj9", "Em7", "Dm9", "G6sus4",  "Cmaj9", "Am9", "Fmaj7#11", "G6sus4",  // 17–32
    };

    /// <summary>Faint "air" tones (the chord's 3rd and 7th, octave 5) over chords 9–12 (bars 17–24).</summary>
    const int AirFrom = 8;
    static readonly string[] FloorAir = { "A5 E5", "G5 D5", "F5 C5", "C5 E5" };

    static readonly int[] FloorTaikoBars = { 1, 9, 17, 25 };

    /// <summary>Marimba: (bar 1-based, "beat note beats, …" with 1-based beats).</summary>
    static readonly (int bar, string notes)[] Marimba =
    {
        (9, "1 E5 2, 3 G5 2"), (10, "1 D5 2, 3 E5 2"), (11, "1 C5 2, 3 E5 1, 4 A5 1"), (12, "1 G5 4"),
        (13, "1 A5 2, 3 C6 2"), (14, "1 G5 2, 3 E5 2"), (15, "1 D5 3"),
        (25, "1 G5 2, 3 E5 2"), (26, "2 D5 2, 4 C5 1"), (27, "1 E5 3"), (28, "1 C5 4"),
    };

    /// <summary>Renders the floor loop: mono, exactly one loop long, seamless when played with AudioSource.loop.</summary>
    public static float[] Floor()
    {
        const int L = FStep * 16 * FBars, ChordLen = FStep * 16 * FChordBars;
        var s = new MusicSynth(FloorRate, L, 78);
        float[] dry = new float[L], send = new float[L], pad = new float[L];
        for (int c = 0; c < FloorChords.Length; c++)
        {
            int at = c * ChordLen;
            string[] notes = Voicings[FloorChords[c]].Split(' ');
            foreach (string n in notes)
                s.Pad(pad, at, ChordLen, MusicSynth.Hz(MusicSynth.Note(n)), FPad, FPadAttack, FPadRelease, FPadLfoHz, FPadLfoDepth);
            s.Swell(dry, at, ChordLen, MusicSynth.Hz(36 + MusicSynth.Note(notes[0]) % 12), FSub, FSubAttack, FSubRelease);
            if (c >= AirFrom && c < AirFrom + FloorAir.Length)
                foreach (string n in FloorAir[c - AirFrom].Split(' '))
                    s.Swell(send, at, ChordLen, MusicSynth.Hz(MusicSynth.Note(n)), FAir, FAirAttack, FAirRelease);
        }
        foreach (int bar in FloorTaikoBars) s.Taiko(dry, FAt(bar - 1, 0), FTaiko, FTaikoLowPass);
        foreach (var (bar, row) in Marimba)
            foreach (var (beat, note, beats) in Row(row))
            {
                int at = FAt(bar - 1, (beat - 1) * 4) + (int)Math.Round(s.Jitter(FMarimbaHumanise) * FloorRate);
                double vol = FMarimba * (1 + s.Jitter(FMarimbaVelocity));
                s.Marimba(send, at, MusicSynth.Hz(note), vol, beats * 4 * FStep);
            }

        s.LowPass(pad, FPadLowPass);
        s.Delay(send, dry, FDelaySteps * FStep, FDelayFeedback, FDelayMix, FDelayDamp);
        s.Mix(dry, send, 1);
        s.Mix(dry, pad, 1);
        s.LowPass(dry, FMixLowPass);
        return s.Master(dry, FDrive, FPeak);
    }

    static int FAt(int bar, int step) => (bar * 16 + step) * FStep;

    // ---------- helpers ----------
    /// <summary>The first MIDI note above `below` with pitch class pc.</summary>
    static int Above(int below, int pc)
    {
        int m = below + 1;
        while (m % 12 != pc) m++;
        return m;
    }

    /// <summary>"0 E5 2, 2 G5 2" → (position, MIDI note, length).</summary>
    static (int pos, int note, int len)[] Row(string row)
    {
        string[] items = row.Split(',');
        var notes = new (int, int, int)[items.Length];
        for (int i = 0; i < items.Length; i++)
        {
            string[] f = items[i].Trim().Split(' ');
            notes[i] = (int.Parse(f[0], CultureInfo.InvariantCulture), MusicSynth.Note(f[1]), int.Parse(f[2], CultureInfo.InvariantCulture));
        }
        return notes;
    }
}
