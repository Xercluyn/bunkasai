using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > Elevator Game > Apply Easy Mode: the easy, ~5-minute settings for values that are saved in scenes
/// (the code-side ones are constants in each minigame). Shorter quest-banner intro on every floor (and its floor
/// number for 1階 / 1F), a quicker
/// ending, and the fifth floor's newspaper game set to its current tuning (a little longer, quicker and tighter
/// than the old easy values; the scene keeps whatever was saved until this runs). Opens each scene, saves it,
/// then reopens the scene you had open; the dialog and the Console list what was written where.
/// </summary>
public static class EasyModeSetup
{
    const float BannerHold = 1.2f, BannerFadeOut = 0.8f, EndingDelay = 2.5f;
    const int Hallways = 10;
    const float PromptTime = 1.1f, GoodWindow = 0.25f, PerfectWindow = 0.08f, RunSpeed = 7.5f;

    [MenuItem("Tools/Elevator Game/Apply Easy Mode")]
    static void Apply()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        string previous = SceneManager.GetActiveScene().path;
        var report = new List<string>();

        for (int n = 1; n <= 5; n++)
        {
            var key = SceneLinks.Floor(n);
            if (!SceneLinks.Exists(key)) continue;
            var scene = EditorSceneManager.OpenScene(SceneLinks.Path(key), OpenSceneMode.Single);
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
            Save(scene, applied, report);
        }

        if (SceneLinks.Exists(SceneLinks.Key.Floor5Game))
        {
            var scene = EditorSceneManager.OpenScene(SceneLinks.Path(SceneLinks.Key.Floor5Game), OpenSceneMode.Single);
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
            Save(scene, applied, report);
        }

        if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous);
        string message = report.Count > 0
            ? "Easy settings saved:\n\n" + string.Join("\n", report)
            : "Nothing to change.";
        Debug.Log("Apply Easy Mode — " + message);
        EditorUtility.DisplayDialog("Apply Easy Mode", message, "OK");
    }

    static IEnumerable<T> All<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true));

    static void Save(Scene scene, List<string> applied, List<string> report)
    {
        if (applied.Count == 0) return;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        report.Add($"• {scene.name}: " + string.Join("; ", applied));
    }
}
