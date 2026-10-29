using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using static EditorKit;

/// <summary>
/// Tools > Elevator Game > Apply Difficulty Values: writes the tuning that's saved in scenes (the code-side values
/// are constants in each minigame). Shorter quest-banner intro on every floor (and its floor number for 1階 / 1F),
/// a quicker ending, and the fifth floor's newspaper game set to its current, harder tuning (the scene keeps
/// whatever was saved until this runs). Opens each scene, saves the ones it changed, then reopens the scenes you had
/// open; the dialog and the Console list what was written where.
/// </summary>
public static class EasyModeSetup
{
    const string Title = "Apply Difficulty Values";
    const float BannerHold = 1.2f, BannerFadeOut = 0.8f, EndingDelay = 2.5f;
    const int Hallways = 10;
    const float PromptTime = 1.1f, GoodWindow = 0.25f, PerfectWindow = 0.08f, RunSpeed = 7.5f;

    [MenuItem("Tools/Elevator Game/" + Title, false, 140)]
    static void Apply()
    {
        var report = new List<string>();
        var keys = Enumerable.Range(1, 5).Select(SceneLinks.Floor).Append(SceneLinks.Key.Floor5Game);
        ForScenes(Title, keys, (scene, key) =>
        {
            var applied = key == SceneLinks.Key.Floor5Game ? TuneShinbun(scene) : TuneFloor(scene, (int)key + 1);
            if (applied.Count > 0) report.Add($"• {scene.name}: " + string.Join("; ", applied));
            return applied.Count > 0;
        });

        string message = report.Count > 0 ? "Saved:\n\n" + string.Join("\n", report) : "Nothing to change.";
        Debug.Log($"{Title} — " + message);
        EditorUtility.DisplayDialog(Title, message, "OK");
    }

    static List<string> TuneFloor(Scene scene, int n)
    {
        int banners = 0, endings = 0;
        foreach (var banner in All<QuestBanner>(scene))
        {
            banner.hold = BannerHold;
            banner.floor = n;   // 1階 / 1F before the quest
            banner.fadeOut = BannerFadeOut;
            EditorUtility.SetDirty(banner);
            banners++;
        }
        foreach (var ending in All<GameEnding>(scene))
        {
            ending.delay = EndingDelay;
            EditorUtility.SetDirty(ending);
            endings++;
        }
        var applied = new List<string>();
        if (banners > 0) applied.Add($"QuestBanner ×{banners}: hold {BannerHold} s, fadeOut {BannerFadeOut} s, floor {n}");
        if (endings > 0) applied.Add($"GameEnding ×{endings}: delay {EndingDelay} s");
        return applied;
    }

    static List<string> TuneShinbun(Scene scene)
    {
        int games = 0;
        foreach (var game in All<ShinbunGame>(scene))
        {
            game.hallways = Hallways;
            game.promptTime = PromptTime;
            game.goodWindow = GoodWindow;
            game.perfectWindow = PerfectWindow;
            game.runSpeed = RunSpeed;
            EditorUtility.SetDirty(game);
            games++;
        }
        var applied = new List<string>();
        if (games > 0)
            applied.Add($"ShinbunGame ×{games}: hallways {Hallways}, promptTime {PromptTime} s, " +
                        $"goodWindow {GoodWindow} s, perfectWindow {PerfectWindow} s, runSpeed {RunSpeed}");
        return applied;
    }
}
