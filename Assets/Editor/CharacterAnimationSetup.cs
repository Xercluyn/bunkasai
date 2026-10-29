using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Elevator Game > Setup Character Animation.
/// Takes the character in Assets/Animations: the model is the FBX with "idle" in its name, and its clips are
/// found by name ("idle", "walk", "run"), either inside that FBX (a Blender export with All Actions) or as
/// separate FBXs (Mixamo "Without Skin" downloads). Imports it as Humanoid when Unity can map the bones, else as
/// Generic (a Blender rig plays its own actions as they are), builds the Idle/Walk/Run controller and swaps the
/// rigged model in for the current player. With no walk clip, the Walk state plays the run slowed down.
/// </summary>
public static class CharacterAnimationSetup
{
    const string Folder = "Assets/Animations";
    const string ControllerPath = Folder + "/Player.controller";
    const string MaterialFolder = "Assets/Materials/Character";
    const string TextureFolder = "Assets/Textures/Character";
    const float RunAsWalkSpeed = 0.6f;   // the Walk state's speed when it borrows the run clip

    // The character's clips, and the model's avatar
    sealed class Clips
    {
        public AnimationClip idle, walk, run;
        public Avatar avatar;
    }

    [MenuItem("Tools/Elevator Game/Setup Character Animation", false, 120)]
    static void Setup()
    {
        string modelPath = FindFbx("idle");
        if (modelPath == null)
        {
            EditorUtility.DisplayDialog("Setup Character Animation",
                "Put the character in " + Folder + " as an FBX with \"idle\" in its name (e.g. onigiri_idle.fbx):\n\n" +
                "  • Blender: one FBX with the mesh, armature and the actions \"Idle\" and \"Run\" (All Actions)\n" +
                "  • Mixamo: the Idle downloaded \"With Skin\", plus walk / run FBXs \"Without Skin\", In Place", "OK");
            return;
        }

        var clips = Prepare(modelPath);
        if (clips == null) return;
        var controller = BuildController(clips);
        SwapPlayerModel(modelPath, clips.avatar, controller);
    }

    // Rig, materials and clips of the model at `modelPath`; null (with a dialog) when there's no idle or run
    static Clips Prepare(string modelPath)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
        var avatar = ImportRig(importer, modelPath);
        if (avatar == null)
        {
            EditorUtility.DisplayDialog("Setup Character Animation",
                $"Couldn't build an avatar from {modelPath}. Check that it has an armature and was exported with the mesh.", "OK");
            return null;
        }
        ExtractMaterials(importer, modelPath);

        var clips = new Clips { avatar = avatar };
        SetupClips(importer, true);
        clips.idle = FindClip(modelPath, "idle", onlyClip: true);
        clips.walk = FindClip(modelPath, "walk") ?? SeparateClip("walk", modelPath, avatar, false);
        clips.run = FindClip(modelPath, "run") ?? SeparateClip("run", modelPath, avatar, true);
        if (clips.idle == null || (clips.walk == null && clips.run == null))
        {
            var names = AllClips(modelPath).Select(c => c.name);
            EditorUtility.DisplayDialog("Setup Character Animation",
                $"{modelPath} needs an idle clip and a walk or run clip, named so (found: {string.Join(", ", names)}).\n" +
                "In Blender, name the actions Idle and Run and export with Bake Animation > All Actions.", "OK");
            return null;
        }
        Debug.Log($"Character: {modelPath} imported as {(avatar.isHuman ? "Humanoid" : "Generic")}; " +
                  $"idle '{clips.idle.name}', walk '{(clips.walk != null ? clips.walk.name : $"(run at {RunAsWalkSpeed}x)")}', " +
                  $"run '{(clips.run != null ? clips.run.name : "(none)")}'.");
        return clips;
    }

    // Humanoid when Unity can map the skeleton (Mixamo rigs), otherwise Generic with the avatar from this model
    static Avatar ImportRig(ModelImporter importer, string path)
    {
        if (importer.animationType != ModelImporterAnimationType.Generic)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = true;
            importer.SaveAndReimport();
            var human = LoadAvatar(path);
            if (human != null && human.isValid && human.isHuman) return human;
            Debug.Log($"Character: {path} has no Humanoid skeleton Unity can map; importing it as Generic.");
        }
        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = true;
        importer.SaveAndReimport();
        var generic = LoadAvatar(path);
        return generic != null && generic.isValid ? generic : null;
    }

    static Avatar LoadAvatar(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();

    // Embedded textures and materials out of the FBX into editable assets (once; later imports keep the remap)
    static void ExtractMaterials(ModelImporter importer, string path)
    {
        if (HasOwnMaterials(path)) return;
        EditorKit.EnsureFolder(TextureFolder);
        importer.ExtractTextures(TextureFolder);
        AssetDatabase.Refresh();
        EditorKit.EnsureFolder(MaterialFolder);
        foreach (var mat in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().ToList())
        {
            string to = AssetDatabase.GenerateUniqueAssetPath($"{MaterialFolder}/{EditorKit.FileName(mat.name)}.mat");
            string error = AssetDatabase.ExtractAsset(mat, to);
            if (!string.IsNullOrEmpty(error)) Debug.LogWarning($"Character: couldn't extract material {mat.name}: {error}");
        }
        AssetDatabase.WriteImportSettingsIfDirty(path);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
    }

    /// <summary>True when the model's materials have been extracted (they're its own, not ones to copy over).</summary>
    internal static bool HasOwnMaterials(string path) =>
        AssetImporter.GetAtPath(path) is ModelImporter mi && mi.GetExternalObjectMap().Keys.Any(k => k.type == typeof(Material));

    static string FindFbx(string keyword)
    {
        if (!AssetDatabase.IsValidFolder(Folder)) return null;
        return AssetDatabase.FindAssets("t:Model", new[] { Folder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .FirstOrDefault(p => Path.GetFileNameWithoutExtension(p).ToLowerInvariant().Contains(keyword));
    }

    // A clip in its own FBX (Mixamo "Without Skin"), on the model's avatar; null when there's none
    static AnimationClip SeparateClip(string keyword, string modelPath, Avatar avatar, bool straight)
    {
        string path = FindFbx(keyword);
        if (path == null || path == modelPath) return null;
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        importer.animationType = avatar.isHuman ? ModelImporterAnimationType.Human : ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
        importer.sourceAvatar = avatar;
        importer.importAnimation = true;
        importer.SaveAndReimport();
        SetupClips(importer, true, straight);
        return FindClip(path, keyword, onlyClip: true);
    }

    static IEnumerable<AnimationClip> AllClips(string path) =>
        AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__"));

    // The clip whose name contains `keyword` ("Armature|Run", "Idle", ...); with onlyClip, a file's single clip
    // counts too (Mixamo names every clip "mixamo.com")
    static AnimationClip FindClip(string path, string keyword, bool onlyClip = false)
    {
        var all = AllClips(path).ToList();
        return all.FirstOrDefault(c => c.name.ToLowerInvariant().Contains(keyword)) ?? (onlyClip && all.Count == 1 ? all[0] : null);
    }

    // Every clip loops. Humanoid clips also keep the body in place: root motion baked into the pose, feet on the
    // origin. straight: pull the clip's sideways/forward drift and hip twist out of the pose into root motion,
    // which is discarded (applyRootMotion is off), so the body stays centred and faces straight ahead. Generic
    // clips (Blender actions made in place) play as they were made.
    internal static void SetupClips(ModelImporter importer, bool loop, bool straight = false)
    {
        bool human = importer.animationType == ModelImporterAnimationType.Human;
        var clips = importer.defaultClipAnimations;   // the file's own takes: a replaced FBX drops the old clip list
        foreach (var c in clips)
        {
            c.loopTime = loop;
            c.loopPose = loop;                 // blend the last frame into the first for a seamless cycle
            if (!human) continue;
            bool run = straight || c.name.ToLowerInvariant().Contains("run");
            c.lockRootRotation = !run;
            c.lockRootHeightY = true;          // baked: vertical motion stays in the pose
            c.lockRootPositionXZ = !run;
            c.keepOriginalOrientation = !run;  // straight: root follows the body's average facing
            c.keepOriginalPositionY = false;
            c.heightFromFeet = true;           // feet sit on the GameObject's origin, so the model doesn't float
            c.keepOriginalPositionXZ = !run;   // straight: root follows the centre of mass
        }
        importer.clipAnimations = clips;
        importer.SaveAndReimport();
    }

    [MenuItem("Tools/Elevator Game/Rebuild Character Animator", false, 121)]
    static void RebuildAnimator()
    {
        string modelPath = FindFbx("idle");
        if (modelPath == null)
        {
            EditorUtility.DisplayDialog("Rebuild Character Animator", "Couldn't find the character FBX (\"idle\" in its name) in " + Folder + ".", "OK");
            return;
        }
        var clips = Prepare(modelPath);
        if (clips == null) return;
        BuildController(clips);
        Debug.Log("Rebuilt " + ControllerPath + " with Idle, Walk" + (clips.run != null ? ", Run" : "") + " states.");
    }

    // Separate Idle and Walk states with short cross-fades. A blend tree would sync the 1 s walk
    // cycle to the 10 s idle while blending, which makes the walk stall and step backwards on stop.
    static AnimatorController BuildController(Clips clips)
    {
        var idle = clips.idle;
        var walk = clips.walk ?? clips.run;   // no walk: the run, slowed down
        var run = clips.run;
        // Rebuild in place so scene references to the controller stay valid
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)
                      ?? AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        foreach (var p in controller.parameters) controller.RemoveParameter(p);
        var sm = controller.layers[0].stateMachine;
        foreach (var s in sm.states) sm.RemoveState(s.state);
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
            if (o is BlendTree) Object.DestroyImmediate(o, true);

        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter(new AnimatorControllerParameter
        {
            name = "WalkRate",
            type = AnimatorControllerParameterType.Float,
            defaultFloat = 1f
        });
        controller.AddParameter(new AnimatorControllerParameter
        {
            name = "RunRate",
            type = AnimatorControllerParameterType.Float,
            defaultFloat = 1f
        });

        var idleState = sm.AddState("Idle");
        idleState.motion = idle;
        var walkState = sm.AddState("Walk");
        walkState.motion = walk;
        walkState.speedParameter = "WalkRate";   // PlayerMovement matches this to ground speed
        walkState.speedParameterActive = true;
        walkState.speed = clips.walk != null ? 1f : RunAsWalkSpeed;
        sm.defaultState = idleState;

        // Speed is 0 standing, up to 1 walking and 2 running. Transitions are checked in the order they
        // are added, so the direct Idle<->Run fades come before the ones through Walk.
        if (run != null)
        {
            var runState = sm.AddState("Run");
            runState.motion = run;
            runState.speedParameter = "RunRate";   // PlayerMovement matches this to ground speed
            runState.speedParameterActive = true;
            AddTransition(idleState, runState, AnimatorConditionMode.Greater, 0.15f, 1.5f);
            AddTransition(runState, idleState, AnimatorConditionMode.Less, 0.2f);
            AddTransition(runState, walkState, AnimatorConditionMode.Less, 0.2f, 1.5f);
            AddTransition(walkState, runState, AnimatorConditionMode.Greater, 0.2f, 1.5f);
        }
        AddTransition(idleState, walkState, AnimatorConditionMode.Greater, 0.15f);
        AddTransition(walkState, idleState, AnimatorConditionMode.Less, 0.2f);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    static void AddTransition(AnimatorState from, AnimatorState to, AnimatorConditionMode mode, float seconds, float threshold = 0.1f)
    {
        var t = from.AddTransition(to);
        t.hasExitTime = false;
        t.hasFixedDuration = true;
        t.duration = seconds;
        t.interruptionSource = TransitionInterruptionSource.Destination;   // can turn back mid-fade
        t.AddCondition(mode, threshold, "Speed");
    }

    static void SwapPlayerModel(string modelPath, Avatar avatar, AnimatorController controller)
    {
        var oldMove = Object.FindAnyObjectByType<PlayerMovement>();
        if (oldMove == null)
        {
            EditorUtility.DisplayDialog("Setup Character Animation",
                "Imported the clips and created " + ControllerPath + ", but found no PlayerMovement in the open scene. " +
                "Open the floor scene with the player (e.g. FirstFloorScene) and run this again.", "OK");
            return;
        }
        var oldGo = oldMove.gameObject;

        var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        var newGo = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, oldGo.scene);
        newGo.name = oldGo.name;
        newGo.tag = oldGo.tag;
        newGo.layer = oldGo.layer;
        newGo.transform.SetParent(oldGo.transform.parent, false);
        newGo.transform.SetPositionAndRotation(oldGo.transform.position, oldGo.transform.rotation);
        newGo.transform.SetSiblingIndex(oldGo.transform.GetSiblingIndex());

        // Mixamo changes units, so match the old character's height
        float oldHeight = Height(oldGo), newHeight = Height(newGo);
        if (oldHeight > 0f && newHeight > 0f)
            newGo.transform.localScale = newGo.transform.localScale * (oldHeight / newHeight);

        // A model with its own (extracted) materials keeps them; otherwise reuse the old character's by name
        if (!HasOwnMaterials(modelPath)) CopyMaterials(oldGo, newGo);

        var animator = newGo.GetComponent<Animator>();
        if (animator == null) animator = newGo.AddComponent<Animator>();
        animator.avatar = avatar;
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;   // CharacterController does the moving
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        // PlayerMovement brings its CharacterController; autoFitCapsule resizes it on Awake
        var newMove = newGo.AddComponent<PlayerMovement>();
        EditorUtility.CopySerialized(oldMove, newMove);

        Undo.RegisterCreatedObjectUndo(newGo, "Setup Character Animation");
        Undo.DestroyObjectImmediate(oldGo);
        EditorSceneManager.MarkSceneDirty(newGo.scene);
        Selection.activeGameObject = newGo;
        Debug.Log("Character animation set up: " + newGo.name + " now uses " + ControllerPath + ". Save the scene.");
    }

    internal static float Height(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return 0f;
        Bounds b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        return b.size.y;
    }

    // Reuse the original materials (matched by name) in case the Mixamo export dropped them
    internal static void CopyMaterials(GameObject from, GameObject to)
    {
        var byName = new Dictionary<string, Material>();
        foreach (var r in from.GetComponentsInChildren<Renderer>())
            foreach (var m in r.sharedMaterials)
                if (m != null) byName[m.name] = m;
        if (byName.Count == 0) return;

        foreach (var r in to.GetComponentsInChildren<Renderer>())
        {
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] != null && byName.TryGetValue(mats[i].name, out var match) && match != mats[i])
                {
                    mats[i] = match;
                    changed = true;
                }
            }
            if (changed) r.sharedMaterials = mats;
        }
    }
}
