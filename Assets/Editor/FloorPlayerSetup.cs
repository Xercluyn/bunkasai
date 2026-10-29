using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > Elevator Game > Setup Player On This Floor.
/// Gives the open floor scene the player: building colliders, the rigged onigiri with the Idle/Walk/Run
/// animator, PlayerMovement (WASD running, collision) and the third-person camera. The character is
/// sized to the building: CharacterToCeiling of the ceiling height above it, or FloorHeight on a floor
/// with no ceiling (the first floor, modelled in real-world metres). Without a player in the scene onigiri is
/// placed on the floor in the middle of the building.
/// Tools > Elevator Game > Set Up This Floor (All) also adds the elevator / arcade play zones and outlines.
/// </summary>
public static class FloorPlayerSetup
{
    const string ModelPath = "Assets/Animations/onigiri_idle.fbx";
    const string UnriggedModelPath = "Assets/onigiri.fbx";
    const string ControllerPath = "Assets/Animations/Player.controller";
    const string Title = "Setup Player On This Floor";

    const float FloorHeight = 1.6f;            // character height on a floor without a ceiling, in metres
    const float CharacterToCeiling = 0.58f;    // character height / ceiling height (the fifth floor's)
    const float NearClip = 0.1f, FarClip = 1000f;   // camera clip planes for a FloorHeight tall character

    [MenuItem("Tools/Elevator Game/Setup Player On This Floor", false, 1)]
    static void SetupPlayer() => Setup(true);

    // After the character model is replaced: every floor's player re-sized, re-animated and showing the model's
    // own materials, each floor saved
    [MenuItem("Tools/Elevator Game/Setup Player On All Floors", false, 1)]
    static void SetupPlayerAllFloors() => EditorKit.ForFloors("Setup Player On All Floors", 1, 5, (scene, _) =>
    {
        if (!Setup(false)) { Debug.LogWarning($"{scene.name}: couldn't set up the player (no building found)."); return false; }
        if (CharacterAnimationSetup.HasOwnMaterials(ModelPath)) ResetMaterials(FindCharacter(scene));
        return true;
    });

    // Drops material overrides on the player's renderers (from older models), so the model's own materials show
    static void ResetMaterials(GameObject character)
    {
        if (character == null) return;
        foreach (var r in character.GetComponentsInChildren<Renderer>(true))
            if (PrefabUtility.IsPartOfPrefabInstance(r) && PrefabUtility.GetObjectOverrides(character).Any(o => o.instanceObject == r))
                PrefabUtility.RevertObjectOverride(r, InteractionMode.AutomatedAction);
    }

    [MenuItem("Tools/Elevator Game/Set Up This Floor (All)", false, 0)]
    static void SetupAll()
    {
        if (!Setup(true)) return;
        MinigameSetup.AddElevatorPlayZoneIfElevator();
        ShinbunSetup.AddArcadeZoneIfArcade();
        ShinbunSetup.AddEndingIfFifthFloor();
        InteractableOutlineSetup.OutlineAllZones(false);
        Debug.Log($"{SceneManager.GetActiveScene().name}: floor set up. Save the scene.");
    }

    internal static bool Setup(bool dialogs)
    {
        var scene = SceneManager.GetActiveScene();
        var character = FindCharacter(scene);
        var building = FindBuilding(scene, character != null ? character.transform : null);
        if (building == null)
        {
            if (dialogs) EditorUtility.DisplayDialog(Title, "Couldn't find the building model in the open scene.", "OK");
            return false;
        }
        if (character == null) character = PlaceCharacter(scene, building);

        // 1. Collision: two-sided colliders on every part of the building
        EnsureColliders(scene, character.transform);

        // 2. Swap in the rigged, animated onigiri and size it to the building
        character = PrepareCharacter(character);
        Vector3 feet = character.transform.position;
        float buildingHeight = TryGetBounds(building, character.transform, out Bounds bb) ? bb.size.y : 0f;
        float floorY = FloorBelow(feet + Vector3.up * (0.05f * buildingHeight), character.transform, buildingHeight * 2f)
                       ?? feet.y;
        float ceiling = CeilingAbove(new Vector3(feet.x, floorY, feet.z), character.transform, buildingHeight);
        float targetHeight = ceiling > 0f ? CharacterToCeiling * ceiling : FloorHeight;

        float current = CharacterAnimationSetup.Height(character);
        if (current > 0f) character.transform.localScale *= targetHeight / current;
        character.transform.position = new Vector3(feet.x, floorY, feet.z);

        // 3. Movement + animation
        SetupComponents(character);

        // 4. Third-person camera, clip planes scaled to the character
        SetupCamera(scene, targetHeight / FloorHeight);

        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = character;
        Debug.Log($"{scene.name}: onigiri is now {targetHeight:0.###} tall " +
                  (ceiling > 0f ? $"under a {ceiling:0.###} ceiling. " : "(no ceiling found, real-world size). ") +
                  "WASD running, animations, collision and the camera are set up. Save the scene.");
        return true;
    }

    // A new onigiri standing on the floor in the middle of the building
    static GameObject PlaceCharacter(Scene scene, GameObject building)
    {
        TryGetBounds(building, null, out Bounds b);
        EnsureColliders(scene, null);   // the floor needs its colliders to be found
        Physics.SyncTransforms();
        var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), scene);
        go.name = "onigiri";
        Undo.RegisterCreatedObjectUndo(go, Title);
        float y = FloorBelow(b.center, go.transform, b.size.y * 2f) ?? b.min.y;
        go.transform.position = new Vector3(b.center.x, y, b.center.z);
        Debug.Log($"{scene.name}: placed onigiri in the middle of {building.name}. Move it to where the player should start.", go);
        return go;
    }

    // Every visible root except the character, camera, lights and the school props: the building and anything
    // placed with it
    internal static IEnumerable<GameObject> BuildingRoots(Scene scene, Transform character) =>
        scene.GetRootGameObjects().Where(root =>
            root.transform != character && root.GetComponent<SchoolDressing>() == null &&
            root.GetComponentInChildren<Camera>(true) == null &&
            root.GetComponent<Light>() == null && root.GetComponentInChildren<PlayerMovement>(true) == null &&
            TryGetBounds(root, character, out _));

    internal static GameObject FindCharacter(Scene scene)
    {
        var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).ToList();
        var move = all.FirstOrDefault(g => g.GetComponent<PlayerMovement>() != null);
        if (move != null) return move;
        return all.FirstOrDefault(g => PrefabUtility.IsAnyPrefabInstanceRoot(g) && IsCharacterModel(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(g)))
            ?? all.FirstOrDefault(g => g.name.ToLowerInvariant().StartsWith("onigiri"));
    }

    static bool IsCharacterModel(string path) => path == ModelPath || path == UnriggedModelPath;

    // The biggest visible root that isn't the character, camera or a light
    static GameObject FindBuilding(Scene scene, Transform character)
    {
        GameObject best = null;
        float bestSize = 0f;
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.transform == character || root.GetComponent<Camera>() != null || root.GetComponent<Light>() != null) continue;
            if (!TryGetBounds(root, character, out Bounds b)) continue;
            if (b.size.magnitude > bestSize) { bestSize = b.size.magnitude; best = root; }
        }
        return best;
    }

    [MenuItem("Tools/Elevator Game/Setup Building (Collision + Thick Walls)", false, 2)]
    static void AddBuildingCollision()
    {
        var scene = SceneManager.GetActiveScene();
        var character = FindCharacter(scene);
        int count = EnsureColliders(scene, character != null ? character.transform : null);
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"{scene.name}: {count} object(s) now get thick single-sided walls and two-sided collision on every mesh. Save the scene.");
    }

    // Every visible root except the character, camera and lights gets WallThickness (single-sided walls
    // get a back face) and BuildingCollision (every mesh blocks from both sides). Returns how many roots were covered.
    internal static int EnsureColliders(Scene scene, Transform character)
    {
        int count = 0;
        foreach (var root in BuildingRoots(scene, character))
        {
            // Read/Write lets BuildingCollision read the meshes at runtime; Generate Colliders covers the editor view
            foreach (var path in root.GetComponentsInChildren<Transform>(true)
                         .Select(t => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject))
                         .Where(p => !string.IsNullOrEmpty(p)).Distinct())
            {
                if (!(AssetImporter.GetAtPath(path) is ModelImporter importer)) continue;
                if (importer.addCollider && importer.isReadable) continue;
                importer.addCollider = true;
                importer.isReadable = true;
                importer.SaveAndReimport();
                Debug.Log("Turned on Generate Colliders and Read/Write for " + path + ".");
            }

            if (root.GetComponent<WallThickness>() == null) Undo.AddComponent<WallThickness>(root);
            if (root.GetComponent<BuildingCollision>() == null) Undo.AddComponent<BuildingCollision>(root);
            count++;
        }
        return count;
    }

    static GameObject PrepareCharacter(GameObject old)
    {
        GameObject go = old;
        if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(old) != ModelPath)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            go = (GameObject)PrefabUtility.InstantiatePrefab(asset, old.scene);
            go.name = old.name;
            go.layer = old.layer;
            go.transform.SetParent(old.transform.parent, false);
            go.transform.SetPositionAndRotation(old.transform.position, old.transform.rotation);
            go.transform.SetSiblingIndex(old.transform.GetSiblingIndex());
            // A model with its own (extracted) materials keeps them. Otherwise reuse the old character's, and
            // restore Mixamo's dropped colours from the unrigged model
            if (!CharacterAnimationSetup.HasOwnMaterials(ModelPath))
            {
                CharacterAnimationSetup.CopyMaterials(old, go);
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(UnriggedModelPath);
                if (source != null) CharacterAnimationSetup.CopyMaterials(source, go);
            }
            Undo.RegisterCreatedObjectUndo(go, Title);
            Undo.DestroyObjectImmediate(old);
        }
        else
        {
            Undo.RecordObject(go.transform, Title);
        }
        return go;
    }

    static void SetupComponents(GameObject go)
    {
        var animator = go.GetComponent<Animator>();
        if (animator == null) animator = Undo.AddComponent<Animator>(go);
        Undo.RecordObject(animator, Title);
        animator.avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
        animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        animator.applyRootMotion = false;   // CharacterController does the moving
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        // PlayerMovement brings its CharacterController; autoFitCapsule resizes it on Awake. An existing one keeps its settings.
        if (go.GetComponent<PlayerMovement>() == null) Undo.AddComponent<PlayerMovement>(go);
    }

    static void SetupCamera(Scene scene, float scale)
    {
        var cams = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>(true)).ToList();
        var cam = cams.FirstOrDefault(c => c.CompareTag("MainCamera")) ?? cams.FirstOrDefault();
        if (cam == null)
        {
            Debug.LogWarning("No camera in the scene; add one and run Tools > Elevator Game > Add Third-Person Camera.");
            return;
        }

        var follow = cam.GetComponent<ThirdPersonCamera>();
        if (follow == null) follow = Undo.AddComponent<ThirdPersonCamera>(cam.gameObject);
        Undo.RecordObject(follow, Title);
        follow.target = null;   // finds the PlayerMovement character on Start

        Undo.RecordObject(cam, Title);
        cam.nearClipPlane = Mathf.Max(0.05f, NearClip * scale);
        cam.farClipPlane = Mathf.Max(cam.nearClipPlane * 10f, FarClip * scale);
    }

    static bool TryGetBounds(GameObject go, Transform exclude, out Bounds bounds)
    {
        var found = EditorKit.RendererBounds(go.GetComponentsInChildren<Renderer>()
            .Where(r => exclude == null || !r.transform.IsChildOf(exclude)));
        bounds = found ?? default;
        return found != null;
    }

    // Height of the nearest floor below origin, ignoring the character; null if there's none.
    static float? FloorBelow(Vector3 origin, Transform ignore, float maxDist)
    {
        return NearestHit(origin, Vector3.down, ignore, maxDist, out float dist) ? origin.y - dist : (float?)null;
    }

    // Floor-to-ceiling height above floor; 0 if there's no ceiling.
    static float CeilingAbove(Vector3 floor, Transform ignore, float maxDist)
    {
        float lift = Mathf.Max(0.001f, maxDist * 0.001f);
        return NearestHit(floor + Vector3.up * lift, Vector3.up, ignore, maxDist, out float dist) ? dist + lift : 0f;
    }

    static bool NearestHit(Vector3 origin, Vector3 dir, Transform ignore, float maxDist, out float best)
    {
        best = float.MaxValue;
        bool backfaces = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;   // single-sided floors and ceilings count from either side
        try
        {
            foreach (var hit in Physics.RaycastAll(origin, dir, maxDist, ~0, QueryTriggerInteraction.Ignore))
            {
                if ((ignore != null && hit.collider.transform.IsChildOf(ignore)) || hit.distance <= 0f) continue;
                best = Mathf.Min(best, hit.distance);
            }
        }
        finally
        {
            Physics.queriesHitBackfaces = backfaces;
        }
        return best < float.MaxValue;
    }
}
