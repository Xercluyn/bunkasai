using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Elevator Game > Setup Character Animation.
/// Takes the Mixamo downloads in Assets/Animations (an idle "With Skin", a walk and an optional run and
/// jump "Without Skin"), sets them up as Humanoid clips, builds the Idle/Walk/Run/Jump controller and
/// swaps the rigged model in for the current player.
/// </summary>
public static class CharacterAnimationSetup
{
    const string Folder = "Assets/Animations";
    const string ControllerPath = Folder + "/Player.controller";

    [MenuItem("Tools/Elevator Game/Setup Character Animation")]
    static void Setup()
    {
        string idlePath = FindFbx("idle");
        string walkPath = FindFbx("walk");
        if (idlePath == null || walkPath == null)
        {
            EditorUtility.DisplayDialog("Setup Character Animation",
                "Put the Mixamo downloads in " + Folder + ":\n\n" +
                "  • an Idle FBX downloaded \"With Skin\" (name containing \"idle\")\n" +
                "  • a Walking FBX downloaded \"Without Skin\", In Place (name containing \"walk\")", "OK");
            return;
        }

        // 1. Rigs: idle creates the Humanoid avatar, walk reuses it
        var idleImporter = (ModelImporter)AssetImporter.GetAtPath(idlePath);
        idleImporter.animationType = ModelImporterAnimationType.Human;
        idleImporter.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        idleImporter.importAnimation = true;
        idleImporter.SaveAndReimport();

        var avatar = AssetDatabase.LoadAllAssetsAtPath(idlePath).OfType<Avatar>().FirstOrDefault();
        if (avatar == null || !avatar.isHuman)
        {
            EditorUtility.DisplayDialog("Setup Character Animation",
                "Couldn't build a Humanoid avatar from " + idlePath + ".\n" +
                "Check that it was downloaded \"With Skin\", then open its Rig tab > Configure to fix the bone mapping.", "OK");
            return;
        }

        var walkImporter = ImportWithAvatar(walkPath, avatar);

        // 2. Clips: loop, and keep them in place (root motion baked into the pose)
        SetupClips(idleImporter, true);
        SetupClips(walkImporter, true);
        var idleClip = LoadClip(idlePath);
        var walkClip = LoadClip(walkPath);
        var runClip = SetupRun(avatar);
        var jumpClip = SetupJump(avatar);

        // 3. Controller: Speed switches Idle/Walk/Run; PlayerMovement plays Jump
        var controller = BuildController(idleClip, walkClip, runClip, jumpClip);

        // 4. Swap the rigged model in for the current player
        SwapPlayerModel(idlePath, avatar, controller);
    }

    static string FindFbx(string keyword)
    {
        if (!AssetDatabase.IsValidFolder(Folder)) return null;
        return AssetDatabase.FindAssets("t:Model", new[] { Folder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .FirstOrDefault(p => Path.GetFileNameWithoutExtension(p).ToLowerInvariant().Contains(keyword));
    }

    static ModelImporter ImportWithAvatar(string path, Avatar avatar)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
        importer.sourceAvatar = avatar;
        importer.importAnimation = true;
        importer.SaveAndReimport();
        return importer;
    }

    // Optional run clip: loops in place like the walk, but with the hips' side-to-side sway and twist
    // taken out so the character runs straight
    static AnimationClip SetupRun(Avatar avatar)
    {
        string runPath = FindFbx("run");
        if (runPath == null) return null;
        SetupClips(ImportWithAvatar(runPath, avatar), true, straight: true);
        return LoadClip(runPath);
    }

    // Optional jump clip: plays once and keeps its own up/down motion in the pose, so the
    // body rises and lands exactly as animated.
    static AnimationClip SetupJump(Avatar avatar)
    {
        string jumpPath = FindFbx("jump");
        if (jumpPath == null) return null;
        var importer = ImportWithAvatar(jumpPath, avatar);
        SetupClips(importer, false);
        var clip = LoadClip(jumpPath);

        // Fire OnJumpLand on the frame the feet touch down, so walking can cut the recovery short
        if (FindAirborne(clip, out float takeoff, out float land))
        {
            var clips = importer.clipAnimations;
            clips[0].events = new[]
            {
                new AnimationEvent
                {
                    functionName = "OnJumpLand",
                    time = land / clip.length       // importer event times are normalized
                }
            };
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            clip = LoadClip(jumpPath);
            Debug.Log($"Jump clip: feet leave the ground at {takeoff:0.00}s and land at {land:0.00}s.");
        }
        else
        {
            Debug.LogWarning("Couldn't find when the jump clip lands; the jump will play to its end before walking resumes.");
        }
        return clip;
    }

    // Sample the lower foot's height to find when both feet are off the ground.
    static bool FindAirborne(AnimationClip clip, out float takeoff, out float land)
    {
        takeoff = land = 0f;
        var bindings = AnimationUtility.GetCurveBindings(clip);
        AnimationCurve Curve(string prop) => bindings.Where(b => b.propertyName == prop)
            .Select(b => AnimationUtility.GetEditorCurve(clip, b)).FirstOrDefault();

        var left = Curve("LeftFootT.y");
        var right = Curve("RightFootT.y");
        var body = Curve("RootT.y");
        if ((left == null || right == null) && body == null) return false;
        float Height(float t) => left != null && right != null
            ? Mathf.Min(left.Evaluate(t), right.Evaluate(t))
            : body.Evaluate(t);

        const int samples = 300;
        float start = Height(0f), peak = start, peakTime = 0f;
        for (int i = 0; i <= samples; i++)
        {
            float t = clip.length * i / samples;
            float h = Height(t);
            if (h > peak) { peak = h; peakTime = t; }
        }
        if (peak - start < 1e-4f) return false;

        float threshold = start + 0.1f * (peak - start);
        takeoff = -1f;
        land = clip.length;
        for (int i = 0; i <= samples; i++)
        {
            float t = clip.length * i / samples;
            float h = Height(t);
            if (takeoff < 0f && h > threshold) takeoff = t;
            if (t > peakTime && h <= threshold) { land = t; break; }
        }
        return takeoff >= 0f && land > takeoff;
    }

    // straight: pull the clip's sideways/forward drift and hip twist out of the pose into root motion,
    // which is discarded (applyRootMotion is off), so the body stays centred and faces straight ahead.
    internal static void SetupClips(ModelImporter importer, bool loop, bool straight = false)
    {
        var clips = importer.defaultClipAnimations;
        foreach (var c in clips)
        {
            c.loopTime = loop;
            c.loopPose = loop;                 // blend the last frame into the first for a seamless cycle
            c.lockRootRotation = !straight;
            c.lockRootHeightY = true;          // baked: vertical motion (incl. a jump's height) stays in the pose
            c.lockRootPositionXZ = !straight;
            c.keepOriginalOrientation = !straight;   // straight: root follows the body's average facing
            c.keepOriginalPositionY = false;
            c.heightFromFeet = true;           // feet sit on the GameObject's origin, so the model doesn't float
            c.keepOriginalPositionXZ = !straight;    // straight: root follows the centre of mass
        }
        importer.clipAnimations = clips;
        importer.SaveAndReimport();
    }

    static AnimationClip LoadClip(string path) =>
        AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
            .First(c => !c.name.StartsWith("__preview__"));

    [MenuItem("Tools/Elevator Game/Rebuild Character Animator")]
    static void RebuildAnimator()
    {
        string idlePath = FindFbx("idle"), walkPath = FindFbx("walk");
        if (idlePath == null || walkPath == null)
        {
            EditorUtility.DisplayDialog("Rebuild Character Animator", "Couldn't find the idle/walk FBX files in " + Folder + ".", "OK");
            return;
        }
        var avatar = AssetDatabase.LoadAllAssetsAtPath(idlePath).OfType<Avatar>().FirstOrDefault();
        SetupClips((ModelImporter)AssetImporter.GetAtPath(idlePath), true);
        SetupClips((ModelImporter)AssetImporter.GetAtPath(walkPath), true);
        var runClip = avatar != null ? SetupRun(avatar) : null;
        var jumpClip = avatar != null ? SetupJump(avatar) : null;
        BuildController(LoadClip(idlePath), LoadClip(walkPath), runClip, jumpClip);
        Debug.Log("Rebuilt " + ControllerPath + " with Idle, Walk" + (runClip != null ? ", Run" : "") +
                  (jumpClip != null ? ", Jump" : "") + " states." +
                  (runClip == null ? " (No FBX with \"run\" in its name found in " + Folder + ".)" : "") +
                  (jumpClip == null ? " (No FBX with \"jump\" in its name found in " + Folder + ".)" : ""));
    }

    // Separate Idle and Walk states with short cross-fades. A blend tree would sync the 1 s walk
    // cycle to the 10 s idle while blending, which makes the walk stall and step backwards on stop.
    static AnimatorController BuildController(AnimationClip idle, AnimationClip walk, AnimationClip run, AnimationClip jump)
    {
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

        // No transitions: PlayerMovement cross-fades into Jump on Space and back out on landing
        if (jump != null) sm.AddState("Jump").motion = jump;

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

        CopyMaterials(oldGo, newGo);

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
