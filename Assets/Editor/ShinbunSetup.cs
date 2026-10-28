using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > Elevator Game > Create Shinbun Arcade Scene: sets up the fifth floor's arcade game from the models in
/// Assets/Minigame/FifthFloorGame/Art (clip import settings, the agent's animator, the skyline material) and
/// creates Assets/Scenes/ShinbunArcade.unity with ShinbunGame wired up, both scenes in Build Settings.
/// Tools > Elevator Game > Add Arcade Play Zone: puts the プレイ trigger around the arcade box on the open floor.
/// </summary>
public static class ShinbunSetup
{
    const string Art = "Assets/Minigame/FifthFloorGame/Art";
    const string ControllerPath = "Assets/Minigame/FifthFloorGame/Shinbun.controller";
    const string SkylinePath = "Assets/Minigame/FifthFloorGame/Skyline.mat";
    // scenes by GUID (SceneLinks), so renaming them doesn't break the tools
    static string ScenePath => SceneLinks.Path(SceneLinks.Key.Floor5Game);
    static string FloorScenePath => SceneLinks.Path(SceneLinks.Key.Floor5);
    static string SceneName => SceneLinks.Name(SceneLinks.Key.Floor5Game);

    // Animator state, clip file, loops, keep the body centred while looping
    static readonly (string state, string file, bool loop, bool straight)[] Clips =
    {
        ("Idle", "Breathing Idle", true, false),
        ("Run", "Run", true, true),
        ("Throw", "Frisbee Throw", false, false),
        ("Land", "Hard Landing", false, false),
        ("Hang", "Jump To Freehang", false, false),
    };

    [MenuItem("Tools/Elevator Game/Create Shinbun Arcade Scene")]
    static void CreateScene()
    {
        if (!AssetDatabase.IsValidFolder(Art))
        {
            EditorUtility.DisplayDialog("Create Shinbun Arcade Scene", "Couldn't find " + Art + ".", "OK");
            return;
        }
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null &&
            !EditorUtility.DisplayDialog("Create Shinbun Arcade Scene", ScenePath + " already exists. Replace it?", "Replace", "Cancel"))
            return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var controller = BuildController();
        var skyline = BuildSkyline();

        string previous = SceneManager.GetActiveScene().path;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var cam = Camera.main;
        if (cam != null)
        {
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 1500f;
        }

        var game = new GameObject("ShinbunGame").AddComponent<ShinbunGame>();
        game.hallwayPlain = Model("Hallway");
        game.hallwayDeliver = Model("HallwayShinbun");
        game.hallwayTrap = Model("HallwayTRAP");
        game.padStart = Model("LZ_Start");
        game.padEnd = Model("LZ_End");
        game.plane = Model("IL78TRANSPORT");
        game.agent = Model("T-Pose");
        game.agentAnimator = controller;
        game.buildingMaterial = AssetDatabase.LoadAssetAtPath<Material>(Art + "/Grey.mat");
        game.skylineMaterial = skyline;
        game.returnScene = SceneLinks.Name(SceneLinks.Key.Floor5);

        EditorSceneManager.SaveScene(scene, ScenePath);
        MinigameSetup.AddToBuildSettings(ScenePath);
        MinigameSetup.AddToBuildSettings(FloorScenePath);
        Debug.Log($"Created {ScenePath} and added it and {FloorScenePath} to Build Settings. " +
                  "Open FifthFloorScene and run Tools > Elevator Game > Add Arcade Play Zone.");

        if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous);
    }

    static GameObject Model(string file) => AssetDatabase.LoadAssetAtPath<GameObject>($"{Art}/{file}.fbx");

    // Clips play in place (the game moves the agent); states are cross-faded from code, so no transitions
    static AnimatorController BuildController()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)
                      ?? AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var sm = controller.layers[0].stateMachine;
        foreach (var s in sm.states) sm.RemoveState(s.state);

        foreach (var (state, file, loop, straight) in Clips)
        {
            string path = $"{Art}/{file}.fbx";
            if (AssetImporter.GetAtPath(path) is ModelImporter importer)
                CharacterAnimationSetup.SetupClips(importer, loop, straight);
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            var s = sm.AddState(state);
            s.motion = clip;
            if (state == "Idle") sm.defaultState = s;
            if (clip == null) Debug.LogWarning($"Shinbun animator: no clip in {path} for the {state} state.");
        }
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    static Material BuildSkyline()
    {
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "/preview.png");
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (texture == null || shader == null) return null;
        var mat = AssetDatabase.LoadAssetAtPath<Material>(SkylinePath);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, SkylinePath);
        }
        mat.shader = shader;
        mat.SetTexture("_BaseMap", texture);
        mat.SetColor("_BaseColor", Color.white);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    [MenuItem("Tools/Elevator Game/Add Arcade Play Zone")]
    static void AddArcadeZone()
    {
        if (!AddArcadeZoneIfArcade())
            EditorUtility.DisplayDialog("Add Arcade Play Zone", "Couldn't find the arcade box (an object with \"arcade\" in its name) in the open scene.", "OK");
    }

    [MenuItem("Tools/Elevator Game/Add Ending To Fifth Floor")]
    static void AddEndingMenu()
    {
        if (!AddEndingIfFifthFloor())
            EditorUtility.DisplayDialog("Add Ending To Fifth Floor", "Open the fifth floor's scene first: the ending (全フロアクリア, 最初から) goes there.", "OK");
    }

    /// <summary>
    /// On the fifth floor, adds (or updates) GameEnding: after its quest, 全フロアクリア with 最初から back to the
    /// first floor. False on any other scene.
    /// </summary>
    internal static bool AddEndingIfFifthFloor()
    {
        var scene = SceneManager.GetActiveScene();
        if (SceneLinks.FloorOf(scene) != 5) return false;
        var ending = Object.FindAnyObjectByType<GameEnding>(FindObjectsInactive.Include);
        if (ending == null)
        {
            var go = new GameObject("GameEnding");
            SceneManager.MoveGameObjectToScene(go, scene);
            Undo.RegisterCreatedObjectUndo(go, "Add Ending");
            ending = go.AddComponent<GameEnding>();
        }
        Undo.RecordObject(ending, "Add Ending");
        ending.firstFloorScene = SceneLinks.Name(SceneLinks.Key.Floor1);
        MinigameSetup.AddToBuildSettings(SceneLinks.Path(SceneLinks.Key.Floor1));
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = ending.gameObject;
        Debug.Log($"{scene.name}: after its quest, 全フロアクリア with 最初から (back to {ending.firstFloorScene}). Save the scene.");
        return true;
    }

    /// <summary>
    /// Puts (or refits) the プレイ zone round the arcade box; false if the open scene has none. The game it loads
    /// depends on the floor: the coin game on the second, the animal quiz on the third, the newspaper game elsewhere.
    /// </summary>
    internal static bool AddArcadeZoneIfArcade() => AddArcadeZone(null);

    // Each floor's arcade game: scene name, scene path, and the menu item that creates it
    static (string game, string path, string menu) ArcadeGameFor(Scene floorScene)
    {
        switch (SceneLinks.FloorOf(floorScene))
        {
            case 2: return (SecondFloorSetup.GameSceneName, SecondFloorSetup.GameScenePath, "Create Second Floor Game Scene");
            case 3: return (ThirdFloorSetup.GameSceneName, ThirdFloorSetup.GameScenePath, "Create Third Floor Game Scene");
            case 4: return (FourthFloorSetup.GameSceneName, FourthFloorSetup.GameScenePath, "Create Fourth Floor Game Scene");
            default: return (SceneName, ScenePath, "Create Shinbun Arcade Scene");
        }
    }

    /// <summary>Same, for a given arcade box (null = the object with "arcade" in its name).</summary>
    internal static bool AddArcadeZone(GameObject arcade)
    {
        var scene = SceneManager.GetActiveScene();
        if (arcade == null)
            arcade = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                .FirstOrDefault(t => t.name.ToLowerInvariant().Contains("arcade") &&
                                     t.GetComponentInParent<ElevatorPlayZone>(true) == null &&
                                     t.GetComponentInChildren<Renderer>() != null)?.gameObject;
        if (arcade == null) return false;

        var (game, gamePath, menu) = ArcadeGameFor(scene);

        var zone = FitZone<ElevatorPlayZone>(arcade, "ArcadePlayZone",
            z => z.GetType() == typeof(ElevatorPlayZone) && (z.minigameScene == game || z.interactable == arcade));
        Undo.RecordObject(zone, "Add Arcade Play Zone");
        zone.minigameScene = game;
        zone.buttonLabel = "▶ プレイ";

        if (!EditorBuildSettings.scenes.Any(s => s.path == gamePath))
            Debug.LogWarning($"{gamePath} isn't in Build Settings yet; run Tools > Elevator Game > {menu}.");
        Debug.Log($"ArcadePlayZone placed around {arcade.name}; walking up to it shows ▶ プレイ, which loads {game}. Save the scene.");
        return true;
    }

    /// <summary>
    /// Puts (or refits) a trigger zone of type T on the floor round target, reaching about half the player's
    /// height out from it, with its button floating over the top and target outlined in orange.
    /// An existing zone matching `existing` is reused, so running a setup again just refits it.
    /// `area` (world space) fits it round one part of target instead, e.g. a console on a desk inside a merged
    /// room mesh; the zone then also reaches down to the floor under the part.
    /// </summary>
    internal static T FitZone<T>(GameObject target, string name, System.Func<T, bool> existing, Bounds? area = null) where T : ElevatorPlayZone
    {
        var scene = target.scene;
        var renderers = target.GetComponentsInChildren<Renderer>();
        Bounds b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);

        var character = FloorPlayerSetup.FindCharacter(scene);
        float h = character != null ? CharacterAnimationSetup.Height(character) : 1.7f;
        if (area.HasValue)
        {
            b = area.Value;
            // reach down to the floor the player stands on: the lowest surface under the part (desk first, then floor)
            float floor = b.min.y - 0.6f * h;
            Physics.SyncTransforms();
            bool backfaces = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            var hits = Physics.RaycastAll(b.center, Vector3.down, 2f * h, ~0, QueryTriggerInteraction.Ignore);
            Physics.queriesHitBackfaces = backfaces;
            if (hits.Length > 0) floor = hits.Min(x => x.point.y);
            b.Encapsulate(new Vector3(b.center.x, floor, b.center.z));
        }

        var zone = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true)).FirstOrDefault(existing);
        if (zone == null)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            Undo.RegisterCreatedObjectUndo(go, "Add " + name);
            zone = go.AddComponent<T>();   // adds BoxCollider + kinematic Rigidbody
        }
        else
        {
            Undo.RecordObject(zone.transform, "Add " + name);
        }

        zone.transform.position = new Vector3(b.center.x, b.min.y, b.center.z);
        zone.transform.rotation = Quaternion.identity;
        var box = zone.GetComponent<BoxCollider>();
        Undo.RecordObject(box, "Add " + name);
        box.isTrigger = true;
        box.size = new Vector3(b.size.x + 1.2f * h, 1.3f * h, b.size.z + 1.2f * h);
        box.center = new Vector3(0f, 0.65f * h, 0f);
        var rb = zone.GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        // Anchor over the top of the object, for the quest guide's marker
        var anchor = zone.transform.Find("PlayButtonAnchor");
        if (anchor == null)
        {
            anchor = new GameObject("PlayButtonAnchor").transform;
            anchor.SetParent(zone.transform, false);
            Undo.RegisterCreatedObjectUndo(anchor.gameObject, "Add " + name);
        }
        anchor.position = new Vector3(b.center.x, b.max.y, b.center.z);

        Undo.RecordObject(zone, "Add " + name);
        zone.buttonTarget = anchor;
        zone.interactable = target;
        InteractableOutlineSetup.Apply(target);   // orange outline that lights up with the button

        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = zone.gameObject;
        return zone;
    }
}
