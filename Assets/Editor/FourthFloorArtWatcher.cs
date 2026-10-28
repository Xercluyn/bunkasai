using System.Linq;
using UnityEditor;

/// <summary>
/// Keeps the fourth floor's matching game showing its pictures when the PNGs in
/// Assets/Minigame/FourthFloorGame/images are replaced (copying the folder in again gives them new GUIDs and
/// default texture settings): imports them as sprites, then puts them back on the game scene's FourthFloorGame.
/// Also checks once after each script reload. Waits for Play mode to end before touching the scene.
/// </summary>
[InitializeOnLoad]
class FourthFloorArtWatcher : AssetPostprocessor
{
    static bool scheduled;

    static FourthFloorArtWatcher() => EditorApplication.delayCall += () =>
    {
        if (!FourthFloorSetup.ArtUpToDate()) Schedule();
    };

    void OnPreprocessTexture()
    {
        if (!FourthFloorSetup.InImages(assetPath)) return;
        var importer = (TextureImporter)assetImporter;
        if (!FourthFloorSetup.IsSprite(importer)) FourthFloorSetup.MakeSprite(importer);
    }

    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (imported.Concat(deleted).Concat(moved).Any(FourthFloorSetup.InImages)) Schedule();
    }

    static void Schedule()
    {
        if (scheduled) return;
        scheduled = true;
        EditorApplication.delayCall += Run;
    }

    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            // after Play mode
            EditorApplication.playModeStateChanged += AfterPlay;
            return;
        }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += Run;
            return;
        }
        scheduled = false;
        if (!FourthFloorSetup.ArtUpToDate()) FourthFloorSetup.RefreshArt();
    }

    static void AfterPlay(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.playModeStateChanged -= AfterPlay;
        EditorApplication.delayCall += Run;
    }
}
