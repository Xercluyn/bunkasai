using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using static EditorKit;

/// <summary>
/// Tools > Elevator Game > Build Windows Game (.exe): builds every scene enabled in Build Settings (MainMenu first)
/// into Builds/Windows/GakuensaiElevator.exe for 64-bit Windows, then opens the folder. Ship the whole folder
/// (the .exe needs its _Data folder and UnityPlayer.dll next to it); Builds/ is git-ignored.
/// </summary>
public static class WindowsBuild
{
    const string Title = "Build Windows Game";
    const string Folder = "Builds/Windows";
    const string ExeName = "GakuensaiElevator.exe";

    [MenuItem("Tools/Elevator Game/Build Windows Game (.exe)", false, 160)]
    static void Build()
    {
        if (!NotPlaying(Title)) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;   // cancelled
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled && File.Exists(s.path)).Select(s => s.path).ToArray();
        if (scenes.Length == 0)
        {
            EditorUtility.DisplayDialog(Title, "No scenes are enabled in File > Build Profiles (Build Settings).", "OK");
            return;
        }

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = Path.Combine(Folder, ExeName),
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.None,
        };
        var report = BuildPipeline.BuildPlayer(options);
        var summary = report.summary;
        if (summary.result != BuildResult.Succeeded)
        {
            Debug.LogError($"{Title}: {summary.result} with {summary.totalErrors} error(s); see the Console above.");
            EditorUtility.DisplayDialog(Title, $"The build {summary.result.ToString().ToLowerInvariant()} " +
                                              $"({summary.totalErrors} error(s)). See the Console.", "OK");
            return;
        }
        string exe = Path.GetFullPath(options.locationPathName);
        Debug.Log($"{Title}: built {scenes.Length} scene(s) into {exe} ({summary.totalSize / (1024f * 1024f):0.#} MB, " +
                  $"{summary.totalTime.TotalSeconds:0} s).");
        EditorUtility.RevealInFinder(exe);
    }
}
