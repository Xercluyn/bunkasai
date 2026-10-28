using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// The third floor's quest: clear the animal-name quiz on the arcade box in 建築学科教室.
/// Tools > Elevator Game > Create Third Floor Game Scene: Assets/Scenes/ThirdFloorGame.unity with ThirdFloorGame
/// (port of Assets/Minigame/ThirdFloorGame) and its emoji sheet, both it and ThirdFloorScene in Build Settings.
/// Tools > Elevator Game > Set Up Third Floor Quest: on the open ThirdFloorScene, the quest banner and the ▶ プレイ
/// zone round the arcade box (the selected object, or one with "arcade" in its name, e.g. arcadeBox).
/// </summary>
public static class ThirdFloorSetup
{
    // scenes by GUID (SceneLinks), so renaming them doesn't break the tools
    internal static string FloorSceneName => SceneLinks.Name(SceneLinks.Key.Floor3);
    internal static string GameSceneName => SceneLinks.Name(SceneLinks.Key.Floor3Game);
    internal static string GameScenePath => SceneLinks.Path(SceneLinks.Key.Floor3Game);
    static string FloorScenePath => SceneLinks.Path(SceneLinks.Key.Floor3);
    const string SheetPath = "Assets/Minigame/ThirdFloorGame/EmojiSheet.png";
    const string QuestText = "建築学科の教室でクイズをクリアしましょう！";
    const string Title = "Set Up Third Floor Quest";

    [MenuItem("Tools/Elevator Game/Create Third Floor Game Scene")]
    static void CreateGameScene()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(GameScenePath) != null &&
            !EditorUtility.DisplayDialog("Create Third Floor Game Scene",
                GameScenePath + " already exists. Replace it?", "Replace", "Cancel"))
            return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var sheet = EmojiSheet();
        if (sheet == null) Debug.LogWarning("Couldn't load " + SheetPath + "; the quiz will have no pictures.");

        string previous = SceneManager.GetActiveScene().path;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)) { tag = "MainCamera" };
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = UiKit.Hex("#c9e7ff");
        camGo.transform.position = new Vector3(0f, 0f, -10f);

        var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        es.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();

        var game = new GameObject("ThirdFloorGame", typeof(ThirdFloorGame)).GetComponent<ThirdFloorGame>();
        game.emojiSheet = sheet;
        game.floorScene = FloorSceneName;

        EditorSceneManager.SaveScene(scene, GameScenePath);
        MinigameSetup.AddToBuildSettings(GameScenePath);
        MinigameSetup.AddToBuildSettings(FloorScenePath);
        Debug.Log($"Created {GameScenePath} and added it and {FloorScenePath} to Build Settings. " +
                  "Open ThirdFloorScene and run Tools > Elevator Game > " + Title + ".");

        if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous);
    }

    /// <summary>The colour emoji sheet (6×3 cells of 256 px), imported at its exact size so the cells line up.</summary>
    internal static Texture2D EmojiSheet()
    {
        if (AssetImporter.GetAtPath(SheetPath) is TextureImporter importer &&
            (importer.npotScale != TextureImporterNPOTScale.None || importer.mipmapEnabled ||
             importer.textureCompression != TextureImporterCompression.Uncompressed))
        {
            importer.textureType = TextureImporterType.Default;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(SheetPath);
    }

    [MenuItem("Tools/Elevator Game/Set Up Third Floor Quest")]
    static void SetUpQuest()
    {
        var scene = SceneManager.GetActiveScene();

        // the arcade box: what's selected, else anything named like an arcade (arcadeBox)
        var selected = Selection.activeGameObject;
        GameObject arcade = null;
        if (selected != null && selected.scene == scene && selected.GetComponentInChildren<Renderer>() != null &&
            selected.GetComponentInParent<ElevatorPlayZone>(true) == null && selected.GetComponent<PlayerMovement>() == null)
            arcade = selected;
        if (arcade == null)
            arcade = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                .FirstOrDefault(t => t.name.ToLowerInvariant().Contains("arcade") &&
                                     t.GetComponentInParent<ElevatorPlayZone>(true) == null &&
                                     t.GetComponentInChildren<Renderer>() != null)?.gameObject;
        if (arcade == null)
        {
            EditorUtility.DisplayDialog(Title, "Couldn't find the arcade box in the open scene. " +
                "Open ThirdFloorScene, select the arcade box in 建築学科教室 and run this again.", "OK");
            return;
        }

        // 1. Quest banner: big text that fades away, then pinned in the upper-left corner
        MinigameSetup.SetQuest(scene, QuestText, Title);   // cleared under the scene's name by a perfect quiz

        // 2. Arcade: ▶ プレイ → the animal quiz
        ShinbunSetup.AddArcadeZone(arcade);

        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"{scene.name}: quest 「{QuestText}」, ▶ プレイ on {arcade.name}. Save the scene.");
    }
}
