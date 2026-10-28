using UnityEditor;
using UnityEngine;

/// <summary>
/// Import settings for the game fonts in Assets/Resources/Fonts (Zen Maru Gothic, Dela Gothic One): dynamic, with
/// fallbacks for glyphs they don't have (▶ ✓ ↻ and rare kanji): Dela falls back to Zen Maru, both to the OS fonts.
/// Runs when the fonts import; Tools > Elevator Game > Reimport Game Fonts applies it again.
/// </summary>
public class GameFontImport : AssetPostprocessor
{
    const string Folder = "Assets/Resources/Fonts";

    static readonly string[] OsFallbacks = { "Yu Gothic UI", "Meiryo UI", "Meiryo", "Segoe UI Symbol", "Arial" };

    public override uint GetVersion() => 1;

    void OnPreprocessAsset()
    {
        if (!assetPath.StartsWith(Folder) || !(assetImporter is TrueTypeFontImporter font)) return;
        font.fontTextureCase = FontTextureCase.Dynamic;
        font.includeFontData = true;
        var names = new System.Collections.Generic.List<string>();
        if (assetPath.Contains("DelaGothicOne")) names.Add("Zen Maru Gothic");
        names.AddRange(OsFallbacks);
        font.fontNames = names.ToArray();
        if (assetPath.Contains("DelaGothicOne"))
        {
            var zen = AssetDatabase.LoadAssetAtPath<Font>(Folder + "/ZenMaruGothic-Bold.ttf");
            if (zen != null) font.fontReferences = new[] { zen };
        }
    }

    [MenuItem("Tools/Elevator Game/Reimport Game Fonts")]
    static void Reimport()
    {
        // Zen Maru first, so Dela can reference it as its fallback
        foreach (var f in new[] { "ZenMaruGothic-Bold.ttf", "ZenMaruGothic-Medium.ttf", "DelaGothicOne-Regular.ttf" })
            AssetDatabase.ImportAsset(Folder + "/" + f, ImportAssetOptions.ForceUpdate);
        Debug.Log("Game fonts reimported.");
    }
}
