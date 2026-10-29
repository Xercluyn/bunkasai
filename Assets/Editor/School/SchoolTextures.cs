using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The greyscale detail textures the school materials are tinted from (Assets/Textures/School/(kind).png).
/// Each one is painted on first use, 512x512 and seamless (periodic noise, wrap-around seams), centred on light
/// grey so the material's _BaseColor gives the hue. Painting is deterministic (fixed seeds).
/// Tools > Elevator Game > School > Steps > Repaint Textures paints them all again.
/// </summary>
public static class SchoolTextures
{
    const int N = 512;

    [InitializeOnLoadMethod]
    static void Hook() => SchoolKit.TextureFor = Get;

    /// <summary>The texture for `kind`, painted first if it isn't on disk yet (null for Plain).</summary>
    public static Texture2D Get(TexKind kind)
    {
        if (kind == TexKind.Plain) return null;
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(PathOf(kind));
        if (tex != null) return tex;
        Paint(kind);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(PathOf(kind));
    }

    [MenuItem("Tools/Elevator Game/School/Steps/Repaint Textures", false, 150)]
    static void RepaintAll()
    {
        int count = 0;
        foreach (TexKind kind in Enum.GetValues(typeof(TexKind)))
        {
            if (kind == TexKind.Plain) continue;
            Paint(kind);
            count++;
        }
        Debug.Log($"Repaint Textures: painted {count} texture(s) in {SchoolKit.TextureFolder}.");
    }

    static string PathOf(TexKind kind) => $"{SchoolKit.TextureFolder}/{kind}.png";

    static void Paint(TexKind kind)
    {
        Func<float, float, float> painter = kind switch
        {
            TexKind.StoneTile => StoneTile,
            TexKind.Carpet => Carpet,
            TexKind.Vinyl => Vinyl,
            TexKind.MarbleVinyl => MarbleVinyl,
            TexKind.StudioFloor => StudioFloor,
            TexKind.Oak => Oak,
            TexKind.BronzePanel => BronzePanel,
            TexKind.CeilingGrid => CeilingGrid,
            TexKind.CeilingGridLight => CeilingGridLight,
            TexKind.SlatePanel => SlatePanel,
            _ => Plaster,
        };
        Save(PathOf(kind), painter);
    }

    // ---------- painters: (u, v) in [0, 1) -> grey ----------

    // 2x2 large stone tiles per repeat, thin darker grout, mottled
    static float StoneTile(float u, float v)
    {
        float tone = (Hash(Cell(u, 2), Cell(v, 2), 1) - 0.5f) * 0.08f;
        float mottle = (Fbm(u, v, 8, 4, 2) - 0.5f) * 0.12f;
        float grout = Mathf.Max(Seam(u, 2, 2.5f), Seam(v, 2, 2.5f));
        return Mathf.Lerp(0.86f + tone + mottle, 0.6f, grout);
    }

    // Fine speckle, 4x4 carpet tiles with faint seams and a slight tone per tile
    static float Carpet(float u, float v)
    {
        float speckle = (Hash(Px(u), Px(v), 3) - 0.5f) * 0.18f;
        float tone = (Hash(Cell(u, 4), Cell(v, 4), 5) - 0.5f) * 0.05f;
        float soft = (Fbm(u, v, 16, 2, 4) - 0.5f) * 0.06f;
        float seam = Mathf.Max(Seam(u, 4, 1.5f), Seam(v, 4, 1.5f));
        return Mathf.Lerp(0.85f + speckle + tone + soft, 0.74f, seam);
    }

    static float Vinyl(float u, float v) => 0.88f + (Fbm(u, v, 4, 5, 6) - 0.5f) * 0.16f;

    // Near-white with thin light-grey veins (integer frequencies keep it seamless)
    static float MarbleVinyl(float u, float v)
    {
        float warp = Fbm(u, v, 4, 5, 7);
        float s = Mathf.Abs(Mathf.Sin((u + v + warp * 1.5f) * Mathf.PI * 2f));
        float vein = Mathf.Pow(1f - s, 18f);
        return 0.94f + (Fbm(u, v, 8, 3, 8) - 0.5f) * 0.05f - vein * 0.18f;
    }

    static float StudioFloor(float u, float v) => 0.96f + (Fbm(u, v, 4, 3, 9) - 0.5f) * 0.03f;

    // Four planks across v with grain running along u, staggered plank ends, dark gaps
    static float Oak(float u, float v)
    {
        int row = Cell(v, 4);
        float warp = Fbm(u, v, 4, 4, 11 + row);
        float grain = Mathf.Sin((v * 48f + warp * 3f) * Mathf.PI * 2f) * 0.05f;
        float tone = 0.82f + (Hash(row, 1, 12) - 0.5f) * 0.1f + grain + (Fbm(u, v, 32, 2, 13) - 0.5f) * 0.04f;
        float gap = Mathf.Max(Seam(v, 4, 1.5f), Seam(Frac(u + Hash(row, 0, 10)), 1, 1.5f));
        return Mathf.Lerp(tone, 0.55f, gap);
    }

    // 2x2 large panels, dark seams, faint brushed streaks along v; mid-light so the tint gives the darkness
    static float BronzePanel(float u, float v)
    {
        float tone = (Hash(Cell(u, 2), Cell(v, 2), 14) - 0.5f) * 0.06f;
        float brushed = (Noise(u, v, 128, 4, 15) - 0.5f) * 0.08f;
        float seam = Mathf.Max(Seam(u, 2, 3f), Seam(v, 2, 3f));
        return Mathf.Lerp(0.8f + tone + brushed, 0.45f, seam);
    }

    // 2x2 ceiling tiles (600 mm) with light seams and tiny perforation dots
    static float CeilingGrid(float u, float v)
    {
        float tile = 0.95f + (Fbm(u, v, 8, 3, 16) - 0.5f) * 0.03f;
        float dx = Px(u) % 8 - 3.5f, dy = Px(v) % 8 - 3.5f;
        if (dx * dx + dy * dy < 1.5f) tile = 0.86f;
        float seam = Mathf.Max(Seam(u, 2, 2.5f), Seam(v, 2, 2.5f));
        return Mathf.Lerp(tile, 0.8f, seam);
    }

    // CeilingGrid with one tile row turned into a pure white LED band
    static float CeilingGridLight(float u, float v)
    {
        float inRow = Frac(v * 2f);
        if (Cell(v, 2) == 0 && inRow > 0.3f && inRow < 0.7f && Seam(u, 2, 2.5f) < 0.5f) return 1f;
        return CeilingGrid(u, v);
    }

    // Vertical bay seams every half texture, subtle vertical streaks
    static float SlatePanel(float u, float v)
    {
        float streak = (Noise(u, v, 64, 2, 17) - 0.5f) * 0.1f;
        float soft = (Fbm(u, v, 8, 3, 18) - 0.5f) * 0.05f;
        return Mathf.Lerp(0.85f + streak + soft, 0.6f, Seam(u, 2, 2.5f));
    }

    static float Plaster(float u, float v) => 0.94f + (Fbm(u, v, 8, 4, 19) - 0.5f) * 0.05f;

    // ---------- seamless noise helpers ----------

    static float Frac(float t) => t - Mathf.Floor(t);
    static int Px(float t) => Mathf.FloorToInt(t * N);
    static int Cell(float t, int cells) => Mod(Mathf.FloorToInt(t * cells), cells);
    static int Mod(int a, int m) => (a % m + m) % m;

    static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }

    // Value noise on a lattice of px x py cells that wraps, so it tiles across the texture
    static float Noise(float u, float v, int px, int py, int seed)
    {
        float x = u * px, y = v * py;
        int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
        float fx = x - ix, fy = y - iy;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        int x0 = Mod(ix, px), x1 = Mod(ix + 1, px), y0 = Mod(iy, py), y1 = Mod(iy + 1, py);
        float a = Mathf.Lerp(Hash(x0, y0, seed), Hash(x1, y0, seed), fx);
        float b = Mathf.Lerp(Hash(x0, y1, seed), Hash(x1, y1, seed), fx);
        return Mathf.Lerp(a, b, fy);
    }

    // Octaves of wrapping noise, each twice the frequency (so still seamless), in [0, 1]
    static float Fbm(float u, float v, int period, int octaves, int seed)
    {
        float sum = 0f, amp = 0.5f, total = 0f;
        for (int o = 0; o < octaves; o++, period *= 2, amp *= 0.5f)
        {
            sum += Noise(u, v, period, period, seed + o * 31) * amp;
            total += amp;
        }
        return sum / total;
    }

    // 1 on the border lines between `cells` cells (wrapping at 0/1), fading out over `widthPx` pixels
    static float Seam(float t, int cells, float widthPx)
    {
        float c = t * cells;
        float d = Mathf.Abs(c - Mathf.Round(c)) * N / cells;
        return 1f - Mathf.SmoothStep(0f, 1f, d / widthPx);
    }

    // ---------- writing ----------

    static void Save(string path, Func<float, float, float> painter)
    {
        EditorKit.EnsureFolder(SchoolKit.TextureFolder);
        var pixels = new Color32[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                byte g = (byte)Mathf.RoundToInt(Mathf.Clamp01(painter((x + 0.5f) / N, (y + 0.5f) / N)) * 255f);
                pixels[y * N + x] = new Color32(g, g, g, 255);
            }
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, true);
        tex.SetPixels32(pixels);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.mipmapEnabled = true;
        importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = 4;
        importer.maxTextureSize = N;
        importer.SaveAndReimport();
    }
}
