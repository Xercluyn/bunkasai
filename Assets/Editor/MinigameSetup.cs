using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

/// <summary>One-click setup for the elevator minigame: Tools > Elevator Game.</summary>
public static class MinigameSetup
{
    // Scenes by GUID (SceneLinks), so renaming them doesn't break the tools
    static string MinigamePath => SceneLinks.Path(SceneLinks.Key.Floor1Game);
    static string MinigameName => SceneLinks.Name(SceneLinks.Key.Floor1Game);
    const string ElevatorLabel = "２階へ";

    [MenuItem("Tools/Elevator Game/Create Minigame Scene", false, 60)]
    static void CreateMinigameScene()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MinigamePath) != null &&
            !EditorUtility.DisplayDialog("Create Minigame Scene",
                MinigamePath + " already exists. Replace it?", "Replace", "Cancel"))
            return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

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

        var minigame = new GameObject("FloorMinigame", typeof(FloorMinigame)).GetComponent<FloorMinigame>();
        minigame.nextLevelScene = SceneLinks.Name(SceneLinks.Key.Floor2);
        minigame.floorScene = SceneLinks.Name(SceneLinks.Key.Floor1);

        EditorSceneManager.SaveScene(scene, MinigamePath);
        AddToBuildSettings(MinigamePath);
        Debug.Log("Created " + MinigamePath + " and added it to Build Settings.");

        if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous);
    }

    // Also drops scenes that have since been deleted
    internal static void AddToBuildSettings(string path)
    {
        var scenes = EditorBuildSettings.scenes.Where(s => AssetDatabase.LoadAssetAtPath<SceneAsset>(s.path) != null).ToList();
        if (!scenes.Any(s => s.path == path)) scenes.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    // The way up, floor by floor: level 1's elevator is its minigame (winning it goes to floor 2); on floors 2–4 the
    // elevator goes up once the floor's quest is cleared; floor 5 is the top. Floors are told apart by scene GUID.
    static readonly string[] UpLabel = { "", "２階へ", "３階へ", "４階へ", "５階へ" };   // by the floor you are on

    [MenuItem("Tools/Elevator Game/Add Elevator Play Zone", false, 20)]
    static void AddElevatorPlayZone()
    {
        var active = SceneManager.GetActiveScene();
        var floor = active.name;
        int n = SceneLinks.FloorOf(active);
        if (n == 5)
        {
            EditorUtility.DisplayDialog("Add Elevator Play Zone", "The fifth floor is the top floor, so its elevator doesn't go anywhere.", "OK");
            return;
        }
        if (n == 0)
        {
            EditorUtility.DisplayDialog("Add Elevator Play Zone", $"{floor} isn't one of the five floors, so there's no next floor to go to.", "OK");
            return;
        }
        if (!AddElevatorPlayZoneIfElevator())
        {
            EditorUtility.DisplayDialog("Add Elevator Play Zone", "No object with \"elevator\" in its name (with meshes) in the open scene.", "OK");
            return;
        }
        InteractableOutlineSetup.OutlineAllZones(false);   // outline its button and float the button label over it
        Debug.Log($"{floor}: elevator set up. Save the scene.");
    }

    const string FirstFloorQuest = "エレベーターで2階に行きましょう！";

    // Level 1's quest banner: big text that fades away, then pinned in the upper-left corner
    [MenuItem("Tools/Elevator Game/Set Up First Floor Quest", false, 40)]
    static void SetUpFirstFloorQuest()
    {
        var scene = SceneManager.GetActiveScene();
        if (SceneLinks.FloorOf(scene) != 1)
        {
            EditorUtility.DisplayDialog("Set Up First Floor Quest", "Open the first floor's scene first.", "OK");
            return;
        }
        MinigameSetup.SetQuest(scene, FirstFloorQuest, "Set Up First Floor Quest");
        AddLevelOneMenu(scene, "Set Up First Floor Quest");
        Debug.Log($"{scene.name}: quest 「{FirstFloorQuest}」, language toggle and 最初から. Save the scene.");
    }

    // English / 日本語 and 最初から under the controls guide
    static void AddLevelOneMenu(Scene scene, string undoName)
    {
        if (Object.FindAnyObjectByType<LevelOneMenu>(FindObjectsInactive.Include) != null) return;
        var go = new GameObject("LevelOneMenu");
        SceneManager.MoveGameObjectToScene(go, scene);
        Undo.RegisterCreatedObjectUndo(go, undoName);
        go.AddComponent<LevelOneMenu>();
        EditorSceneManager.MarkSceneDirty(scene);
    }

    // Floors 1–4 in one go: opens each floor scene, (re)fits its elevator zone and button outline, saves it.
    // The first floor also gets its quest banner.
    [MenuItem("Tools/Elevator Game/Set Up Elevators On All Floors", false, 21)]
    static void SetUpAllElevators()
    {
        const string title = "Set Up Elevators On All Floors";
        var done = new System.Collections.Generic.List<string>();
        bool ran = EditorKit.ForFloors(title, 1, 4, (scene, n) =>
        {
            if (!AddElevatorPlayZoneIfElevator())
            {
                Debug.LogWarning($"{scene.name}: no elevator found; skipped.");
                return false;
            }
            InteractableOutlineSetup.OutlineAllZones(false);
            if (n == 1)
            {
                SetQuest(scene, FirstFloorQuest, title);
                AddLevelOneMenu(scene, title);
            }
            done.Add(scene.name);
            return true;
        });
        if (!ran) return;
        EditorUtility.DisplayDialog(title,
            done.Count > 0 ? "Elevators set up and saved on: " + string.Join(", ", done) + "." : "No floor with an elevator was found.", "OK");
    }

    /// <summary>
    /// Puts the elevator's play zone on the floor in front of the elevator, sized to the player: on the first floor
    /// ２階へ loading the elevator minigame (whose win then goes to the second floor); on floors 2–4 an
    /// ElevatorExitZone up to the next floor once the quest is cleared. An existing zone keeps its place.
    /// False if the open scene has no elevator or is the fifth floor.
    /// </summary>
    internal static bool AddElevatorPlayZoneIfElevator()
    {
        var scene = SceneManager.GetActiveScene();
        int floorNumber = SceneLinks.FloorOf(scene);
        bool first = floorNumber == 1;
        if (floorNumber < 1 || floorNumber > 4) return false;   // the fifth floor, or not a floor

        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .Where(t => t.GetComponentInParent<ElevatorPlayZone>(true) == null && t.GetComponentInChildren<Renderer>() != null).ToList();
        var elevator = all.FirstOrDefault(t => t.name.ToLowerInvariant() == "elevator")
                    ?? all.FirstOrDefault(t => t.name.ToLowerInvariant().Contains("elevator"));
        if (elevator == null || !TryGetBounds(elevator, out Bounds b)) return false;

        var zone = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ElevatorPlayZone>(true))
            .FirstOrDefault(z => z.interactable == elevator.gameObject || z.minigameScene == MinigameName || z.minigameScene == "Minigame" ||
                                 z is ElevatorExitZone);
        var type = first ? typeof(ElevatorPlayZone) : typeof(ElevatorExitZone);
        if (zone != null && zone.GetType() != type)
        {
            // wrong kind for this floor (e.g. a copy of the first floor's minigame zone): swap it, keeping its box
            var go = zone.gameObject;
            Undo.DestroyObjectImmediate(zone);
            zone = (ElevatorPlayZone)Undo.AddComponent(go, type);
        }
        // Floors 2–4 are always refitted (zones from before could sit behind the elevator); the first floor's
        // working zone keeps its place
        bool place = zone == null || !first;
        if (zone == null)
        {
            var go = new GameObject("ElevatorPlayZone");
            Undo.RegisterCreatedObjectUndo(go, "Add Elevator Play Zone");
            zone = (ElevatorPlayZone)go.AddComponent(type);   // adds BoxCollider + kinematic Rigidbody
        }
        if (place) FitInFrontOf(zone, elevator, b);

        Undo.RecordObject(zone, "Add Elevator Play Zone");
        zone.interactable = elevator.gameObject;
        if (first)
        {
            zone.minigameScene = MinigameName;
            zone.buttonLabel = ElevatorLabel;
            if (!EditorBuildSettings.scenes.Any(s => s.path == MinigamePath)) AddToBuildSettings(MinigamePath);
            WireFirstFloorMinigame();
        }
        else
        {
            var exit = (ElevatorExitZone)zone;
            var next = SceneLinks.Floor(floorNumber + 1);
            exit.minigameScene = "";
            exit.buttonLabel = UpLabel[floorNumber];
            exit.nextFloorScene = SceneLinks.Name(next);
            exit.questId = "";
            if (SceneLinks.Exists(next)) AddToBuildSettings(SceneLinks.Path(next));
            else Debug.LogWarning($"{SceneLinks.Path(next)} doesn't exist; the elevator on {scene.name} has nowhere to go.");
        }
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = zone.gameObject;
        return true;
    }

    // The zone covers the floor between the elevator and the player's start, which is in front of the elevator.
    // Without a player in the scene it goes on the side facing the middle of the floor.
    static void FitInFrontOf(ElevatorPlayZone zone, Transform elevator, Bounds b)
    {
        var player = Object.FindAnyObjectByType<PlayerMovement>();
        float h = player != null ? CharacterAnimationSetup.Height(player.gameObject) : 1.6f;
        if (h <= 0f) h = 1.6f;

        Vector3 target = player != null ? player.transform.position
                       : elevator.root != elevator && TryGetBounds(elevator.root, out Bounds fb) ? fb.center : b.center + Vector3.forward;
        Vector3 dir = target - b.center;
        dir.y = 0f;
        bool alongX = Mathf.Abs(dir.x) / Mathf.Max(b.extents.x, 0.01f) > Mathf.Abs(dir.z) / Mathf.Max(b.extents.z, 0.01f);
        Vector3 normal = alongX ? new Vector3(Mathf.Sign(dir.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(dir.z));
        Vector3 side = alongX ? Vector3.forward : Vector3.right;
        float reach = Vector3.Scale(b.extents, normal).magnitude;

        // deep enough to reach the start spot, wide enough to take it in if it's off to one side
        float depth = 0.95f * h, width = 0.95f * h, lateral = 0f;
        if (player != null)
        {
            float toStart = Vector3.Dot(dir, normal) - reach;
            depth = Mathf.Clamp(toStart + 0.5f * h, 0.95f * h, 3f * h);
            lateral = Vector3.Dot(dir, side);
            width = Mathf.Clamp(Mathf.Abs(lateral) + 0.95f * h, 0.95f * h, 2.5f * h);
            lateral *= 0.5f;   // centre between the elevator's middle and the start
        }
        Vector3 pos = b.center + normal * (reach + depth * 0.5f) + side * lateral;
        pos.y = player != null ? player.transform.position.y : b.min.y;

        Undo.RecordObject(zone.transform, "Add Elevator Play Zone");
        zone.transform.SetPositionAndRotation(pos, Quaternion.identity);
        var box = zone.GetComponent<BoxCollider>();
        Undo.RecordObject(box, "Add Elevator Play Zone");
        box.isTrigger = true;
        box.size = alongX ? new Vector3(depth, 1.9f * h, width) : new Vector3(width, 1.9f * h, depth);
        box.center = new Vector3(0f, 0.95f * h, 0f);
        var rb = zone.GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        Debug.Log($"{zone.name}: placed on the {(player != null ? "player's start" : "floor's middle")} side of {elevator.name}. " +
                  "Move/resize its green box if it doesn't cover the floor in front of the doors.", zone);
    }

    // Level 1's quest is the elevator minigame itself: winning it (次のステージへ) goes up to the second floor
    static void WireFirstFloorMinigame()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MinigamePath) == null) return;
        var open = SceneManager.GetSceneByPath(MinigamePath);
        bool wasOpen = open.IsValid() && open.isLoaded;
        var game = wasOpen ? open : EditorSceneManager.OpenScene(MinigamePath, OpenSceneMode.Additive);
        var minigame = game.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<FloorMinigame>(true)).FirstOrDefault();
        string secondFloor = SceneLinks.Name(SceneLinks.Key.Floor2), firstFloor = SceneLinks.Name(SceneLinks.Key.Floor1);
        if (minigame != null && (minigame.nextLevelScene != secondFloor || minigame.floorScene != firstFloor))
        {
            Undo.RecordObject(minigame, "Add Elevator Play Zone");
            minigame.nextLevelScene = secondFloor;
            minigame.floorScene = firstFloor;
            EditorSceneManager.MarkSceneDirty(game);
            EditorSceneManager.SaveScene(game);
            Debug.Log($"{MinigameName}: winning now goes up to {secondFloor}.");
        }
        if (!wasOpen) EditorSceneManager.CloseScene(game, true);
        AddToBuildSettings(SceneLinks.Path(SceneLinks.Key.Floor2));
    }

    [MenuItem("Tools/Elevator Game/Add Third-Person Camera", false, 3)]
    static void AddThirdPersonCamera()
    {
        var cam = Camera.main != null ? Camera.main : Object.FindAnyObjectByType<Camera>();
        if (cam == null)
        {
            EditorUtility.DisplayDialog("Add Third-Person Camera", "No camera found in the open scene.", "OK");
            return;
        }
        if (cam.GetComponent<ThirdPersonCamera>() == null) Undo.AddComponent<ThirdPersonCamera>(cam.gameObject);
        EditorSceneManager.MarkSceneDirty(cam.gameObject.scene);
        Selection.activeGameObject = cam.gameObject;
        Debug.Log("ThirdPersonCamera is on " + cam.name + ". Save the scene.");
    }

    [MenuItem("Tools/Elevator Game/Add Quest Banner", false, 23)]
    static void AddQuestBanner()
    {
        var existing = Object.FindAnyObjectByType<QuestBanner>();
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            EditorGUIUtility.PingObject(existing.gameObject);
            Debug.Log("A QuestBanner already exists in this scene; selected it.");
            return;
        }

        var go = new GameObject("QuestBanner", typeof(QuestBanner));
        Undo.RegisterCreatedObjectUndo(go, "Add Quest Banner");
        EditorSceneManager.MarkSceneDirty(go.scene);
        Selection.activeGameObject = go;
        Debug.Log("Added QuestBanner. Change its Quest Text in the Inspector for other floors. Save the scene.");
    }

    /// <summary>
    /// The floor's quest banner (big text that fades away, then pinned in the upper-left corner), added if missing,
    /// with its quest cleared under the scene's name.
    /// </summary>
    internal static QuestBanner SetQuest(Scene scene, string text, string undoName)
    {
        var banner = Object.FindAnyObjectByType<QuestBanner>(FindObjectsInactive.Include);
        if (banner == null)
        {
            var go = new GameObject("QuestBanner");
            SceneManager.MoveGameObjectToScene(go, scene);
            Undo.RegisterCreatedObjectUndo(go, undoName);
            banner = go.AddComponent<QuestBanner>();
        }
        Undo.RecordObject(banner, undoName);
        banner.questText = text;
        banner.questId = "";
        banner.floor = SceneLinks.FloorOf(scene);   // 1階 / 1F before the quest
        EditorSceneManager.MarkSceneDirty(scene);
        return banner;
    }

    static bool TryGetBounds(Transform t, out Bounds bounds)
    {
        var found = EditorKit.RendererBounds(t.GetComponentsInChildren<Renderer>());
        bounds = found ?? default;
        return found != null;
    }
}
