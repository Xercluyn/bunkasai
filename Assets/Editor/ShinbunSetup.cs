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
    const string ScenePath = "Assets/Scenes/ShinbunArcade.unity";
    const string FloorScenePath = "Assets/Scenes/FifthFloorScene.unity";
    const string SceneName = "ShinbunArcade";

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

    /// <summary>Puts (or refits) the プレイ zone round the arcade box; false if the open scene has none.</summary>
    internal static bool AddArcadeZoneIfArcade()
    {
        var scene = SceneManager.GetActiveScene();
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();
        var arcade = all.FirstOrDefault(t => t.name.ToLowerInvariant().Contains("arcade") &&
                                             t.GetComponentInParent<ElevatorPlayZone>(true) == null &&
                                             t.GetComponentInChildren<Renderer>() != null);
        if (arcade == null) return false;
        var renderers = arcade.GetComponentsInChildren<Renderer>();
        Bounds b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);

        var character = FloorPlayerSetup.FindCharacter(scene);
        float h = character != null ? CharacterAnimationSetup.Height(character) : 1.7f;

        // Reuse a zone made earlier, so running this again just refits it
        var zone = all.Select(t => t.GetComponent<ElevatorPlayZone>()).FirstOrDefault(z => z != null && z.minigameScene == SceneName);
        if (zone == null)
        {
            var go = new GameObject("ArcadePlayZone");
            Undo.RegisterCreatedObjectUndo(go, "Add Arcade Play Zone");
            zone = go.AddComponent<ElevatorPlayZone>();   // adds BoxCollider + kinematic Rigidbody
        }
        else
        {
            Undo.RecordObject(zone.transform, "Add Arcade Play Zone");
        }

        // Covers the floor around the box, reaching about half the player's height out from it
        zone.transform.position = new Vector3(b.center.x, b.min.y, b.center.z);
        zone.transform.rotation = Quaternion.identity;
        var box = zone.GetComponent<BoxCollider>();
        Undo.RecordObject(box, "Add Arcade Play Zone");
        box.isTrigger = true;
        box.size = new Vector3(b.size.x + 1.2f * h, 1.3f * h, b.size.z + 1.2f * h);
        box.center = new Vector3(0f, 0.65f * h, 0f);
        var rb = zone.GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        // The プレイ button floats over the top of the box
        var anchor = zone.transform.Find("PlayButtonAnchor");
        if (anchor == null)
        {
            anchor = new GameObject("PlayButtonAnchor").transform;
            anchor.SetParent(zone.transform, false);
            Undo.RegisterCreatedObjectUndo(anchor.gameObject, "Add Arcade Play Zone");
        }
        anchor.position = new Vector3(b.center.x, b.max.y, b.center.z);

        Undo.RecordObject(zone, "Add Arcade Play Zone");
        zone.minigameScene = SceneName;
        zone.buttonTarget = anchor;
        zone.screenOffset = new Vector2(0f, 40f);
        zone.interactable = arcade.gameObject;
        InteractableOutlineSetup.Apply(arcade.gameObject);   // orange outline that lights up with the Play button

        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = zone.gameObject;
        if (!EditorBuildSettings.scenes.Any(s => s.path == ScenePath))
            Debug.LogWarning($"{ScenePath} isn't in Build Settings yet; run Tools > Elevator Game > Create Shinbun Arcade Scene.");
        Debug.Log($"ArcadePlayZone placed around {arcade.name}; walking up to it shows ▶ プレイ, which loads {SceneName}. Save the scene.");
        return true;
    }
}
