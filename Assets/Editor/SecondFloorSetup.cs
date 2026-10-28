using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// The second floor's quest: earn ¥3,000 in the arcade's coin game, then buy the third-floor ticket.
/// Tools > Elevator Game > Create Second Floor Game Scene: Assets/Scenes/SecondFloorGame.unity with SecondFloorGame
/// (port of Assets/Minigame/SecondFloorGame), both it and SecondFloorScene in Build Settings.
/// Tools > Elevator Game > Set Up Second Floor Quest: on the open SecondFloorScene, the quest banner, the ▶ プレイ
/// zone round the arcade box (the selected object, or one with "arcade" in its name) and the チケットを購入 zone
/// round the ticket vending machine.
/// </summary>
public static class SecondFloorSetup
{
    // scenes by GUID (SceneLinks), so renaming them doesn't break the tools
    internal static string FloorSceneName => SceneLinks.Name(SceneLinks.Key.Floor2);
    internal static string GameSceneName => SceneLinks.Name(SceneLinks.Key.Floor2Game);
    internal static string GameScenePath => SceneLinks.Path(SceneLinks.Key.Floor2Game);
    static string FloorScenePath => SceneLinks.Path(SceneLinks.Key.Floor2);
    const string QuestText = "３階へのチケットを買いましょう！";
    const string Title = "Set Up Second Floor Quest";

    [MenuItem("Tools/Elevator Game/Create Second Floor Game Scene")]
    static void CreateGameScene()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(GameScenePath) != null &&
            !EditorUtility.DisplayDialog("Create Second Floor Game Scene",
                GameScenePath + " already exists. Replace it?", "Replace", "Cancel"))
            return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        string previous = SceneManager.GetActiveScene().path;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)) { tag = "MainCamera" };
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = UiKit.Hex("#1d1026");
        camGo.transform.position = new Vector3(0f, 0f, -10f);

        var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        es.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();

        new GameObject("SecondFloorGame", typeof(SecondFloorGame)).GetComponent<SecondFloorGame>().floorScene = FloorSceneName;

        EditorSceneManager.SaveScene(scene, GameScenePath);
        MinigameSetup.AddToBuildSettings(GameScenePath);
        MinigameSetup.AddToBuildSettings(FloorScenePath);
        Debug.Log($"Created {GameScenePath} and added it and {FloorScenePath} to Build Settings. " +
                  "Open SecondFloorScene, select the arcade box and run Tools > Elevator Game > Set Up Second Floor Quest.");

        if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous);
    }

    [MenuItem("Tools/Elevator Game/Set Up Second Floor Quest")]
    static void SetUpQuest()
    {
        var scene = SceneManager.GetActiveScene();
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .Where(t => t.GetComponentInParent<ElevatorPlayZone>(true) == null && t.GetComponentInChildren<Renderer>() != null)
            .ToList();

        // the vending machine: "ticket-vending" in second.fbx
        var vending = all.FirstOrDefault(t => t.name.ToLowerInvariant().Contains("vending"))
                   ?? all.FirstOrDefault(t => t.name.ToLowerInvariant().Contains("ticket"));

        // the arcade box: what's selected, else anything named like an arcade
        var selected = Selection.activeGameObject;
        GameObject arcade = null;
        if (selected != null && selected.scene == scene && selected.GetComponentInChildren<Renderer>() != null &&
            selected.GetComponentInParent<ElevatorPlayZone>(true) == null && (vending == null || selected != vending.gameObject))
            arcade = selected;
        if (arcade == null) arcade = all.FirstOrDefault(t => t.name.ToLowerInvariant().Contains("arcade"))?.gameObject;

        if (arcade == null && vending == null)
        {
            EditorUtility.DisplayDialog(Title, "Couldn't find the arcade box or the ticket vending machine in the open scene. " +
                "Open SecondFloorScene, select the arcade box in the Hierarchy and run this again.", "OK");
            return;
        }

        // 1. Quest banner (big text first, then the upper-left corner)
        MinigameSetup.SetQuest(scene, QuestText, Title);   // cleared under the scene's name by the ticket machine

        // 2. Arcade: ▶ プレイ → the 2nd-floor coin game
        if (arcade != null) ShinbunSetup.AddArcadeZone(arcade);
        else Debug.LogWarning("No arcade box: select it in the Hierarchy and run " + Title + " again.");

        // 3. Ticket machine: チケットを購入 once the player has ¥3,000
        if (vending != null)
        {
            var zone = ShinbunSetup.FitZone<TicketVendingZone>(vending.gameObject, "TicketVendingZone", z => true);
            Undo.RecordObject(zone, Title);
            zone.buttonLabel = "チケットを購入";
            zone.minigameScene = "";
            zone.price = 3000;
            if (zone.GetComponent<AudioSource>() == null) Undo.AddComponent<AudioSource>(zone.gameObject);
        }
        else Debug.LogWarning("No object named like \"ticket-vending\" in the scene, so there's no チケットを購入 zone.");

        if (!EditorBuildSettings.scenes.Any(s => s.path == GameScenePath))
            Debug.LogWarning($"{GameScenePath} isn't in Build Settings yet; run Tools > Elevator Game > Create Second Floor Game Scene.");
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"{scene.name}: quest 「{QuestText}」" +
                  (arcade != null ? $", ▶ プレイ on {arcade.name}" : "") +
                  (vending != null ? $", チケットを購入 on {vending.name}" : "") + ". Save the scene.");
    }
}
