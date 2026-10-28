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
/// ending, and a shorter, more forgiving newspaper game on the fifth floor. Opens each scene, saves it, then
/// reopens the scene you had open.
/// </summary>
public static class EasyModeSetup
{
    const float BannerHold = 1.2f, BannerFadeOut = 0.8f, EndingDelay = 2.5f;
    const int Hallways = 7;
    const float PromptTime = 1.3f, GoodWindow = 0.35f;

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
            int changed = 0;
            foreach (var banner in All<QuestBanner>(scene))
            {
                banner.hold = BannerHold;
                banner.floor = n;   // 1階 / 1F before the quest
                banner.fadeOut = BannerFadeOut;
                EditorUtility.SetDirty(banner);
                changed++;
            }
            foreach (var ending in All<GameEnding>(scene))
            {
                ending.delay = EndingDelay;
                EditorUtility.SetDirty(ending);
                changed++;
            }
            Save(scene, changed, report);
        }

        if (SceneLinks.Exists(SceneLinks.Key.Floor5Game))
        {
            var scene = EditorSceneManager.OpenScene(SceneLinks.Path(SceneLinks.Key.Floor5Game), OpenSceneMode.Single);
            int changed = 0;
            foreach (var game in All<ShinbunGame>(scene))
            {
                game.hallways = Hallways;
                game.promptTime = PromptTime;
                game.goodWindow = GoodWindow;
                EditorUtility.SetDirty(game);
                changed++;
            }
            Save(scene, changed, report);
        }

        if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous);
        EditorUtility.DisplayDialog("Apply Easy Mode",
            report.Count > 0 ? "Easy settings saved in: " + string.Join(", ", report) + "." : "Nothing to change.", "OK");
    }

    static IEnumerable<T> All<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true));

    static void Save(Scene scene, int changed, List<string> report)
    {
        if (changed == 0) return;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        report.Add(scene.name);
    }
}
