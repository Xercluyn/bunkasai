using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The game's scenes found by GUID, so renaming or moving a scene file doesn't lose track of it.
/// The setup tools ask here for a floor's or minigame's current name and path instead of hard-coding them.
/// Renaming a scene also rewrites every [SceneName] field that held its old name (in all scenes) and fixes
/// Build Settings; Tools > Elevator Game > Fix Scene Links does the same by hand, including names from before
/// this existed (FirstFloorScene → whatever the first floor's scene is called now).
/// </summary>
public static class SceneLinks
{
    public enum Key { Floor1, Floor2, Floor3, Floor4, Floor5, Floor1Game, Floor2Game, Floor3Game, Floor4Game, Floor5Game }

    // GUID of each scene (from its .meta), where a new one is created, and the names it has had
    static readonly Dictionary<Key, (string guid, string defaultPath, string[] formerNames)> Scenes =
        new Dictionary<Key, (string, string, string[])>
        {
            [Key.Floor1] = ("99c9720ab356a0642a771bea13969a05", "Assets/Scenes/FirstFloorScene.unity", new[] { "FirstFloorScene" }),
            [Key.Floor2] = ("f17762ce360a1bc43997559743d9db38", "Assets/Scenes/SecondFloorScene.unity", new[] { "SecondFloorScene" }),
            [Key.Floor3] = ("9f765e58049eb7c4ebc3796713c96cbb", "Assets/Scenes/ThirdFloorScene.unity", new[] { "ThirdFloorScene" }),
            [Key.Floor4] = ("e490e8f17060158419ac3818b71bba92", "Assets/Scenes/FourthFloorScene.unity", new[] { "FourthFloorScene" }),
            [Key.Floor5] = ("b82bc47e763a68a4e95c1c74e917520e", "Assets/Scenes/FifthFloorScene.unity", new[] { "FifthFloorScene" }),
            [Key.Floor1Game] = ("ae0c61ac58167de42a7e52d241645d6a", "Assets/Scenes/ElevatorButtonGame.unity", new[] { "ElevatorButtonGame", "Minigame" }),
            [Key.Floor2Game] = ("50555f3c505f06943b68e69507a05e62", "Assets/Scenes/SecondFloorGame.unity", new[] { "SecondFloorGame" }),
            [Key.Floor3Game] = ("3199b8bcc497a00409e6d129470ec45e", "Assets/Scenes/ThirdFloorGame.unity", new[] { "ThirdFloorGame" }),
            [Key.Floor4Game] = ("bcad0d2488c6a154499f4cc20ff3dd8a", "Assets/Scenes/FourthFloorGame.unity", new[] { "FourthFloorGame" }),
            [Key.Floor5Game] = ("61f09f8976ba92742bd15f632466ab0e", "Assets/Scenes/ShinbunArcade.unity", new[] { "ShinbunArcade" }),
        };

    /// <summary>The scene's current path; for a minigame scene that doesn't exist yet, where to create it.</summary>
    public static string Path(Key key)
    {
        var (guid, defaultPath, _) = Scenes[key];
        string path = AssetDatabase.GUIDToAssetPath(guid);
        return !string.IsNullOrEmpty(path) && AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null ? path : defaultPath;
    }

    public static string Name(Key key) => System.IO.Path.GetFileNameWithoutExtension(Path(key));

    public static bool Exists(Key key) => AssetDatabase.LoadAssetAtPath<SceneAsset>(Path(key)) != null;

    public static Key Floor(int n) => (Key)(n - 1);
    public static Key Game(int n) => (Key)(n + 4);

    /// <summary>1–5 for a floor scene, 0 otherwise.</summary>
    public static int FloorOf(Scene scene)
    {
        for (int n = 1; n <= 5; n++)
            if (scene.path == Path(Floor(n))) return n;
        return 0;
    }

    /* ---------- keeping [SceneName] fields and Build Settings in step ---------- */

    [MenuItem("Tools/Elevator Game/Fix Scene Links", false, 142)]
    static void FixAll()
    {
        // every name a scene has had → what it's called now
        var map = new Dictionary<string, string>();
        foreach (var key in Scenes.Keys)
        {
            string now = Name(key);
            foreach (var old in Scenes[key].formerNames)
                if (old != now) map[old] = now;
        }
        int changed = Relink(map);
        EditorUtility.DisplayDialog("Fix Scene Links",
            changed > 0 ? $"Updated {changed} scene link(s) to the current scene names. Build Settings are up to date. Save any open scenes marked with *."
                        : "All scene links already use the current scene names. Build Settings are up to date.", "OK");
    }

    /// <summary>Rewrites marked fields holding an old name, in every scene under Assets; returns how many changed.</summary>
    internal static int Relink(Dictionary<string, string> oldToNew)
    {
        FixBuildSettings();
        if (oldToNew.Count == 0) return 0;
        int changed = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Scene", new[] { "Assets" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var scene = SceneManager.GetSceneByPath(path);
            bool wasOpen = scene.IsValid() && scene.isLoaded;
            if (!wasOpen)
            {
                // only open scenes that actually mention an old name
                string text = File.ReadAllText(path);
                if (!oldToNew.Keys.Any(old => text.Contains(": " + old))) continue;
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            }

            int here = RelinkScene(scene, oldToNew);
            changed += here;
            if (here > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                if (!wasOpen) EditorSceneManager.SaveScene(scene);   // open scenes are left for the user to save
                Debug.Log($"SceneLinks: {path}: updated {here} scene link(s).");
            }
            if (!wasOpen) EditorSceneManager.CloseScene(scene, true);
        }
        return changed;
    }

    static int RelinkScene(Scene scene, Dictionary<string, string> oldToNew)
    {
        int changed = 0;
        foreach (var mb in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true)))
        {
            if (mb == null) continue;
            for (var type = mb.GetType(); type != null && type != typeof(MonoBehaviour); type = type.BaseType)
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (field.FieldType != typeof(string) || field.GetCustomAttribute<SceneNameAttribute>() == null) continue;
                    var value = (string)field.GetValue(mb);
                    if (value == null || !oldToNew.TryGetValue(value, out var now)) continue;
                    Undo.RecordObject(mb, "Fix Scene Links");
                    field.SetValue(mb, now);
                    EditorUtility.SetDirty(mb);
                    changed++;
                }
        }
        return changed;
    }

    /// <summary>Build Settings entries follow their scene by GUID; entries for deleted scenes are dropped.</summary>
    internal static void FixBuildSettings()
    {
        var scenes = EditorBuildSettings.scenes;
        bool dirty = false;
        var fixedList = new List<EditorBuildSettingsScene>();
        foreach (var s in scenes)
        {
            string path = AssetDatabase.GUIDToAssetPath(s.guid);
            if (string.IsNullOrEmpty(path) || AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null) { dirty = true; continue; }
            if (path != s.path) dirty = true;
            fixedList.Add(new EditorBuildSettingsScene(path, s.enabled));
        }
        if (dirty) EditorBuildSettings.scenes = fixedList.ToArray();
    }

    // A scene renamed in the Project window: rewrite links to its old name once the import is done
    class RenameWatcher : AssetPostprocessor
    {
        static readonly Dictionary<string, string> pending = new Dictionary<string, string>();

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            for (int i = 0; i < moved.Length; i++)
            {
                if (!moved[i].EndsWith(".unity")) continue;
                string from = System.IO.Path.GetFileNameWithoutExtension(movedFrom[i]);
                string to = System.IO.Path.GetFileNameWithoutExtension(moved[i]);
                if (from != to) pending[from] = to;
                else FixBuildSettings();
            }
            if (pending.Count > 0) EditorApplication.delayCall += Flush;
        }

        static void Flush()
        {
            if (pending.Count == 0) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += Flush;   // try again when the editor is idle
                return;
            }
            var map = new Dictionary<string, string>(pending);
            pending.Clear();
            int changed = Relink(map);
            Debug.Log($"SceneLinks: scene renamed ({string.Join(", ", map.Select(p => p.Key + " → " + p.Value))}); " +
                      $"{changed} link(s) updated. Save any open scenes marked with *.");
        }
    }
}
