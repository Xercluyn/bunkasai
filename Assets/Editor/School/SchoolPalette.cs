using System.Collections.Generic;
using UnityEngine;

/// <summary>Greyscale detail textures the school materials are tinted from (painted by SchoolTextures).</summary>
public enum TexKind
{
    Plain, StoneTile, Carpet, Vinyl, MarbleVinyl, StudioFloor, Oak, BronzePanel, CeilingGrid, CeilingGridLight,
    SlatePanel, Plaster,
}

/// <summary>One surface: its tint, detail texture, the real size (metres) one texture repeat covers, and gloss.</summary>
public readonly struct Surface
{
    public readonly Color color;
    public readonly TexKind tex;
    public readonly float tileMetres;
    public readonly float smoothness;
    public readonly float metallic;

    public Surface(string hex, TexKind tex, float tileMetres = 1f, float smoothness = 0.2f, float metallic = 0f)
    {
        color = UiKit.Hex(hex);
        this.tex = tex;
        this.tileMetres = tileMetres;
        this.smoothness = smoothness;
        this.metallic = metallic;
    }
}

/// <summary>A floor's look: floor, walls, ceiling and pillars, plus named accent colours for props.</summary>
public sealed class FloorLook
{
    public Surface floor, wall, ceiling, pillar;
    public Dictionary<string, Color> accents = new Dictionary<string, Color>();
}

/// <summary>
/// The real campus (読売理工医療福祉専門学校, Korakuen), from the facility photos on yomiuririkou.ac.jp/iten:
/// 1F lobby, 2F reception + library, 3F architecture (drafting / PC rooms), 4F TV studio + sub-control +
/// electrical power lab, 5F care / clinical engineering / IT. Colours are approximate hex from the photos.
/// </summary>
public static class SchoolPalette
{
    /// <summary>The floors are modelled about 4.5x real size (doors 9.5 tall, ceilings 20.1).</summary>
    public const float WorldPerMetre = 4.5f;

    static readonly Dictionary<int, FloorLook> looks = new Dictionary<int, FloorLook>
    {
        [1] = new FloorLook   // entrance hall: stone tile, charcoal + white walls, bronze ceiling, salmon accent wall
        {
            floor = new Surface("#8A8A88", TexKind.StoneTile, 0.6f, 0.35f),
            wall = new Surface("#3A3A3C", TexKind.Plaster, 2f, 0.15f),
            ceiling = new Surface("#3A2E24", TexKind.BronzePanel, 1.2f, 0.5f, 0.6f),
            pillar = new Surface("#F0F0EE", TexKind.Plaster, 2f, 0.15f),
            accents = Accents(("salmon", "#E89A8C"), ("navy", "#1E2A4A"), ("white", "#F0F0EE"),
                              ("steel", "#B8B8B4"), ("tactile", "#E8D24A"), ("mat", "#4A4440"), ("exit", "#2FA84F")),
        },
        [2] = new FloorLook   // office counter + library: purple-grey vinyl, slate wall bays, white ceiling
        {
            floor = new Surface("#6F6A78", TexKind.Vinyl, 1.5f, 0.3f),
            wall = new Surface("#5E6268", TexKind.SlatePanel, 1.2f, 0.2f),
            ceiling = new Surface("#F2F2F2", TexKind.CeilingGrid, 0.6f, 0.1f),
            pillar = new Surface("#E8E8E6", TexKind.Plaster, 2f, 0.15f),
            accents = Accents(("runner", "#6C8CC0"), ("counter", "#F4F4F4"), ("counterBase", "#5E6268"),
                              ("signBoard", "#3E4044"), ("shelf", "#C8CACC"), ("oak", "#D8C8A8"), ("chair", "#7A5A3C"),
                              ("chairSeat", "#6A4C30"), ("glass", "#BFD8E8")),
        },
        [3] = new FloorLook   // drafting + PC rooms: grey tile carpet, white walls, white grid ceiling with LED strips
        {
            floor = new Surface("#7A7E82", TexKind.Carpet, 0.5f, 0.05f),
            wall = new Surface("#EEEEEC", TexKind.Plaster, 2f, 0.15f),
            ceiling = new Surface("#F4F4F2", TexKind.CeilingGridLight, 1.2f, 0.1f),
            pillar = new Surface("#E6E6E4", TexKind.Plaster, 2f, 0.15f),
            accents = Accents(("chair", "#2A5CC8"), ("oak", "#D9C3A0"), ("drafting", "#F4F4F4"), ("stool", "#222222"),
                              ("chrome", "#C8CCD0"), ("monitor", "#111214"), ("cabinet", "#EDEDEB"), ("model", "#E8DCC4")),
        },
        [4] = new FloorLook   // studio + sub-control + power lab: dark vinyl, white walls
        {
            floor = new Surface("#4A4C52", TexKind.Vinyl, 1.5f, 0.3f),
            wall = new Surface("#F0F0EE", TexKind.Plaster, 2f, 0.15f),
            ceiling = new Surface("#F2F2F2", TexKind.CeilingGridLight, 1.2f, 0.1f),
            pillar = new Surface("#E6E6E4", TexKind.Plaster, 2f, 0.15f),
            accents = Accents(("studioFloor", "#D8DAE0"), ("cyc", "#3A4AE0"), ("grid", "#111111"), ("curtain", "#101018"),
                              ("pedestal", "#A08850"), ("camera", "#8A8E94"), ("tally", "#E0302A"), ("console", "#3A3C40"),
                              ("fader", "#6A6C70"), ("crimson", "#A8203A"), ("rack", "#D8D0B8"), ("control", "#2C2E32"),
                              ("galv", "#A8ACB0"), ("meterPanel", "#E6DAB8"), ("copper", "#B87333"), ("porcelain", "#EDEDE8")),
        },
        [5] = new FloorLook   // care / clinical / IT: finished like 3F (grey tile carpet, white walls, grid ceiling)
        {
            floor = new Surface("#7A7E82", TexKind.Carpet, 0.5f, 0.05f),
            wall = new Surface("#EEEEEC", TexKind.Plaster, 2f, 0.15f),
            ceiling = new Surface("#F4F4F2", TexKind.CeilingGridLight, 1.2f, 0.1f),
            pillar = new Surface("#E6E6E4", TexKind.Plaster, 2f, 0.15f),
            accents = Accents(("lavender", "#9A88C8"), ("bedWood", "#D8A070"), ("bedGold", "#C8A858"), ("sage", "#9EB0AA"),
                              ("sand", "#C8B48C"), ("medical", "#F2F4F4"), ("steel", "#C0C4C8"), ("teal", "#3AA08A"),
                              ("mauve", "#B07890"), ("grey", "#7A7E86"), ("maroon", "#6A3A44"), ("trolley", "#E8C030"),
                              ("imac", "#E8E8EA"), ("black", "#151515")),
        },
    };

    static Dictionary<string, Color> Accents(params (string key, string hex)[] pairs)
    {
        var d = new Dictionary<string, Color>();
        foreach (var (key, hex) in pairs) d[key] = UiKit.Hex(hex);
        return d;
    }

    /// <summary>The look of floor 1–5; null for other numbers.</summary>
    public static FloorLook For(int floor) => looks.TryGetValue(floor, out var look) ? look : null;

    /// <summary>A floor's accent colour; magenta (easy to spot) when the key is unknown.</summary>
    public static Color Accent(int floor, string key) =>
        For(floor) != null && For(floor).accents.TryGetValue(key, out var c) ? c : Color.magenta;
}
