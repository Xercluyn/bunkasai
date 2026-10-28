using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// The fourth floor's game: the festival-stall matching game, played at the studio's audio switcher.
/// Tools > Elevator Game > Create Fourth Floor Game Scene: Assets/Scenes/FourthFloorGame.unity with FourthFloorGame
/// (port of Assets/Minigame/FourthFloorGame) and its art, both it and FourthFloorScene in Build Settings.
/// Tools > Elevator Game > Set Up Fourth Floor Quest: on the open FourthFloorScene, the quest banner and ▶ プレイ at
/// the fader console by the studio door. The studio (purple monitor wall) is one merged mesh, swBtn22 in fourth.fbx, so the console
/// is found as a separate piece of that mesh and only it gets the cyan outline, like the elevator's button.
/// </summary>
public static class FourthFloorSetup
{
    // scenes by GUID (SceneLinks), so renaming them doesn't break the tools
    internal static string FloorSceneName => SceneLinks.Name(SceneLinks.Key.Floor4);
    internal static string GameSceneName => SceneLinks.Name(SceneLinks.Key.Floor4Game);
    internal static string GameScenePath => SceneLinks.Path(SceneLinks.Key.Floor4Game);
    static string FloorScenePath => SceneLinks.Path(SceneLinks.Key.Floor4);
    const string ImagesPath = "Assets/Minigame/FourthFloorGame/images";
    const string Title = "Set Up Fourth Floor Quest";
    const string QuestText = "スタジオでゲームをクリアしましょう！";

    // The fader console in swBtn22, measured in fourth.fbx: its centre and size as fractions of the whole studio
    // mesh's bounds, so they survive the import's unit scale and axis flip
    static readonly Vector3 ConsoleCentre = new Vector3(0.7228f, 0.2965f, 0.3790f);
    static readonly Vector3 ConsoleSize = new Vector3(0.207f, 0.0236f, 0.1335f);

    [MenuItem("Tools/Elevator Game/Create Fourth Floor Game Scene")]
    static void CreateGameScene()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(GameScenePath) != null &&
            !EditorUtility.DisplayDialog("Create Fourth Floor Game Scene",
                GameScenePath + " already exists. Replace it?", "Replace", "Cancel"))
            return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var art = LoadArt();
        if (art.Count == 0) Debug.LogWarning("No images found in " + ImagesPath + "; the stalls and cards will be plain.");
        var sheet = ThirdFloorSetup.EmojiSheet();

        string previous = SceneManager.GetActiveScene().path;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)) { tag = "MainCamera" };
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = UiKit.Bg;
        camGo.transform.position = new Vector3(0f, 0f, -10f);

        var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        es.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();

        var game = new GameObject("FourthFloorGame", typeof(FourthFloorGame)).GetComponent<FourthFloorGame>();
        game.art = art;
        game.emojiSheet = sheet;
        game.floorScene = FloorSceneName;

        EditorSceneManager.SaveScene(scene, GameScenePath);
        MinigameSetup.AddToBuildSettings(GameScenePath);
        MinigameSetup.AddToBuildSettings(FloorScenePath);
        Debug.Log($"Created {GameScenePath} ({art.Count} images) and added it and {FloorScenePath} to Build Settings. " +
                  "Open FourthFloorScene and run Tools > Elevator Game > " + Title + ".");

        if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous);
    }

    // stall, card and card-back PNGs as sprites
    internal static List<Sprite> LoadArt()
    {
        var art = new List<Sprite>();
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ImagesPath }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer && !IsSprite(importer))
            {
                MakeSprite(importer);
                importer.SaveAndReimport();
            }
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null) art.Add(sprite);
        }
        return art;
    }

    internal static bool InImages(string path) => path.Replace('\\', '/').StartsWith(ImagesPath + "/");

    internal static bool IsSprite(TextureImporter t) =>
        t.textureType == TextureImporterType.Sprite && t.spriteImportMode == SpriteImportMode.Single && !t.mipmapEnabled;

    internal static void MakeSprite(TextureImporter t)
    {
        t.textureType = TextureImporterType.Sprite;
        t.spriteImportMode = SpriteImportMode.Single;
        t.alphaIsTransparency = true;
        t.mipmapEnabled = false;
    }

    // The game scene lists every PNG in the images folder (by GUID), and they are all sprites
    internal static bool ArtUpToDate()
    {
        if (!SceneLinks.Exists(SceneLinks.Key.Floor4Game)) return true;
        string text;
        try { text = System.IO.File.ReadAllText(GameScenePath); }
        catch { return true; }
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ImagesPath }))
        {
            if (!text.Contains(guid)) return false;
            if (AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) is TextureImporter t && !IsSprite(t)) return false;
        }
        return true;
    }

    /// <summary>Puts the images folder's sprites into the game scene's FourthFloorGame (after the PNGs were replaced).</summary>
    [MenuItem("Tools/Elevator Game/Refresh Fourth Floor Game Art")]
    internal static void RefreshArt()
    {
        if (!SceneLinks.Exists(SceneLinks.Key.Floor4Game)) return;
        var art = LoadArt();
        var scene = SceneManager.GetSceneByPath(GameScenePath);
        bool wasOpen = scene.IsValid() && scene.isLoaded;
        if (!wasOpen) scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Additive);
        int fixedCount = 0;
        foreach (var game in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<FourthFloorGame>(true)))
        {
            game.art = new List<Sprite>(art);
            EditorUtility.SetDirty(game);
            fixedCount++;
        }
        if (fixedCount > 0)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        if (!wasOpen) EditorSceneManager.CloseScene(scene, true);
        Debug.Log($"{GameSceneName}: {art.Count} images from {ImagesPath} set on the matching game.");
    }

    [MenuItem("Tools/Elevator Game/" + Title)]
    static void AddZone()
    {
        var scene = SceneManager.GetActiveScene();
        var studio = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MeshFilter>(true))
            .FirstOrDefault(mf => mf.name.ToLowerInvariant().Contains("swbtn") && mf.sharedMesh != null);
        if (studio == null)
        {
            EditorUtility.DisplayDialog(Title, "Couldn't find the studio (swBtn22 from fourth.fbx) in the open scene. Open FourthFloorScene and run this again.", "OK");
            return;
        }

        // Read/Write and the outline component first: the console is found in the mesh's triangles
        InteractableOutlineSetup.Apply(studio.gameObject);
        var mesh = studio.sharedMesh;
        if (!mesh.isReadable)
        {
            EditorUtility.DisplayDialog(Title, $"Turn on Read/Write for the model of {studio.name} and run this again.", "OK");
            return;
        }

        if (!FindConsole(mesh, out Bounds local, out float score))
        {
            EditorUtility.DisplayDialog(Title, $"Couldn't find the fader console in {studio.name}'s mesh.", "OK");
            return;
        }
        if (score > 0.08f)
            Debug.LogWarning($"The fader console match in {studio.name} is loose (score {score:0.000}); check the cyan outline lands on the right console.");

        // the console's box in world space
        var t = studio.transform;
        var world = new Bounds(t.TransformPoint(local.center), Vector3.zero);
        for (int i = 0; i < 8; i++)
            world.Encapsulate(t.TransformPoint(local.center + Vector3.Scale(local.extents,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));

        var zone = ShinbunSetup.FitZone<ElevatorPlayZone>(studio.gameObject, "SwitcherPlayZone",
            z => z.minigameScene == GameSceneName, world);
        Undo.RecordObject(zone, Title);
        zone.minigameScene = GameSceneName;
        zone.buttonLabel = "▶ プレイ";
        var anchor = zone.buttonTarget;
        Undo.RecordObject(anchor, Title);
        anchor.position = new Vector3(world.center.x, world.max.y, world.center.z);   // over the console, not the desk

        // outline just the console
        var outline = studio.GetComponent<InteractableOutline>();
        Undo.RecordObject(outline, Title);
        outline.partMesh = studio;
        outline.partBounds = local;

        // quest banner, cleared under the scene's name when every stall is filled
        MinigameSetup.SetQuest(scene, QuestText, Title);

        if (!EditorBuildSettings.scenes.Any(s => s.path == GameScenePath))
            Debug.LogWarning($"{GameScenePath} isn't in Build Settings yet; run Tools > Elevator Game > Create Fourth Floor Game Scene.");
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = zone.gameObject;
        Debug.Log($"{scene.name}: quest 「{QuestText}」, ▶ プレイ at the fader console in {studio.name}, loading {GameSceneName}. Save the scene.");
    }

    // The mesh piece whose centre and size, as fractions of the mesh bounds, best match the fader console's.
    // The import may mirror x (and z, depending on the axis settings), so each mirroring is tried.
    static bool FindConsole(Mesh mesh, out Bounds best, out float bestScore)
    {
        best = default;
        bestScore = float.MaxValue;
        var all = mesh.bounds;
        var size = all.size;
        if (size.x <= 0f || size.y <= 0f || size.z <= 0f) return false;
        foreach (var p in InteractableOutlineSetup.Pieces(mesh))
        {
            Vector3 c = new Vector3((p.center.x - all.min.x) / size.x, (p.center.y - all.min.y) / size.y, (p.center.z - all.min.z) / size.z);
            Vector3 s = new Vector3(p.size.x / size.x, p.size.y / size.y, p.size.z / size.z);
            float sizeScore = Mathf.Abs(s.x - ConsoleSize.x) + Mathf.Abs(s.y - ConsoleSize.y) + Mathf.Abs(s.z - ConsoleSize.z);
            foreach (bool fx in new[] { true, false })
                foreach (bool fz in new[] { false, true })
                {
                    var e = new Vector3(fx ? 1f - ConsoleCentre.x : ConsoleCentre.x, ConsoleCentre.y, fz ? 1f - ConsoleCentre.z : ConsoleCentre.z);
                    float score = Mathf.Abs(c.x - e.x) + Mathf.Abs(c.y - e.y) + Mathf.Abs(c.z - e.z) + sizeScore;
                    if (score < bestScore) { bestScore = score; best = p; }
                }
        }
        return bestScore < float.MaxValue;
    }
}
