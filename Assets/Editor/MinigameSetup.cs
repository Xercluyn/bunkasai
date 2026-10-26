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
    const string MinigamePath = "Assets/Scenes/ElevatorButtonGame.unity";
    const string MinigameName = "ElevatorButtonGame";
    const string ElevatorLabel = "２階へ";

    [MenuItem("Tools/Elevator Game/Create Minigame Scene")]
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

        new GameObject("FloorMinigame", typeof(FloorMinigame));

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

    [MenuItem("Tools/Elevator Game/Add Elevator Play Zone")]
    static void AddElevatorPlayZone()
    {
        if (!AddElevatorPlayZoneIfElevator())
        {
            EditorUtility.DisplayDialog("Add Elevator Play Zone", "No object with \"elevator\" in its name (with meshes) in the open scene.", "OK");
            return;
        }
        InteractableOutlineSetup.OutlineAllZones(false);   // outline its button and float the button label over it
    }

    /// <summary>
    /// Puts the elevator's play zone (２階へ, loading the elevator minigame) on the floor in front of the elevator,
    /// sized to the player. An existing zone keeps its place and only gets its scene, label and elevator updated.
    /// False if the open scene has no elevator.
    /// </summary>
    internal static bool AddElevatorPlayZoneIfElevator()
    {
        var scene = SceneManager.GetActiveScene();
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .Where(t => t.GetComponentInParent<ElevatorPlayZone>(true) == null && t.GetComponentInChildren<Renderer>() != null).ToList();
        var elevator = all.FirstOrDefault(t => t.name.ToLowerInvariant() == "elevator")
                    ?? all.FirstOrDefault(t => t.name.ToLowerInvariant().Contains("elevator"));
        if (elevator == null || !TryGetBounds(elevator, out Bounds b)) return false;

        var zone = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ElevatorPlayZone>(true))
            .FirstOrDefault(z => z.interactable == elevator.gameObject || z.minigameScene == MinigameName || z.minigameScene == "Minigame");
        if (zone == null)
        {
            var player = Object.FindAnyObjectByType<PlayerMovement>();
            float h = player != null ? CharacterAnimationSetup.Height(player.gameObject) : 1.6f;
            if (h <= 0f) h = 1.6f;

            // Just outside whichever side of the elevator faces the middle of the floor
            Vector3 floorCenter = elevator.root != elevator && TryGetBounds(elevator.root, out Bounds fb) ? fb.center : b.center + Vector3.forward;
            Vector3 dir = floorCenter - b.center;
            dir.y = 0f;
            Vector3 normal = Mathf.Abs(dir.x) / Mathf.Max(b.extents.x, 0.01f) > Mathf.Abs(dir.z) / Mathf.Max(b.extents.z, 0.01f)
                ? new Vector3(Mathf.Sign(dir.x), 0f, 0f)
                : new Vector3(0f, 0f, Mathf.Sign(dir.z));
            float reach = Vector3.Scale(b.extents, normal).magnitude;
            Vector3 pos = b.center + normal * (reach + 0.47f * h);
            pos.y = b.min.y;

            var go = new GameObject("ElevatorPlayZone");
            Undo.RegisterCreatedObjectUndo(go, "Add Elevator Play Zone");
            go.transform.position = pos;
            zone = go.AddComponent<ElevatorPlayZone>();   // adds BoxCollider + kinematic Rigidbody
            var box = go.GetComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(0.95f * h, 1.9f * h, 0.95f * h);
            box.center = new Vector3(0f, 0.95f * h, 0f);
            var rb = go.GetComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            Debug.Log("Added ElevatorPlayZone in front of " + elevator.name + ". Move/resize its green box if it doesn't cover the floor in front of the button.", go);
        }

        Undo.RecordObject(zone, "Add Elevator Play Zone");
        zone.minigameScene = MinigameName;
        zone.buttonLabel = ElevatorLabel;
        zone.interactable = elevator.gameObject;
        if (!EditorBuildSettings.scenes.Any(s => s.path == MinigamePath)) AddToBuildSettings(MinigamePath);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = zone.gameObject;
        return true;
    }

    [MenuItem("Tools/Elevator Game/Add Third-Person Camera")]
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

    [MenuItem("Tools/Elevator Game/Add Quest Banner")]
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

    static bool TryGetBounds(Transform t, out Bounds bounds)
    {
        var renderers = t.GetComponentsInChildren<Renderer>();
        bounds = default;
        if (renderers.Length == 0) return false;
        bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        return true;
    }
}
