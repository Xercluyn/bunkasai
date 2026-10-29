using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The menu scene, Assets/Scenes/MainMenu.unity: a camera and MainMenu (play floors 1–5, or one floor on its own),
/// first in Build Settings so the built game opens on it, with the five floors added too.
/// Made by itself when the editor loads and the scene is missing (alongside the open scenes, which are left as
/// they are); Tools > Elevator Game > Create Menu Scene remakes it.
/// </summary>
[InitializeOnLoad]
public static class MenuSetup
{
    const string MenuPath = "Assets/Scenes/MainMenu.unity";
    const string Title = "Create Menu Scene";

    static MenuSetup() => EditorApplication.delayCall += CreateIfMissing;

    static void CreateIfMissing()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += CreateIfMissing;
            return;
        }
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MenuPath) != null) { PutFirst(); return; }
        if (Enumerable.Range(0, SceneManager.sceneCount).Any(i => string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path)))
        {
            Debug.LogWarning("The menu scene isn't made yet: save the open scene, then run Tools > Elevator Game > " + Title + ".");
            return;
        }
        Build();
    }

    [MenuItem("Tools/Elevator Game/" + Title, false, 141)]
    static void Create()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MenuPath) != null &&
            !EditorUtility.DisplayDialog(Title, MenuPath + " already exists. Replace it?", "Replace", "Cancel"))
            return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var open = SceneManager.GetSceneByPath(MenuPath);
        if (open.IsValid() && open.isLoaded)
        {
            // rebuild it in place
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Fill(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), MenuPath);
            PutFirst();
            return;
        }
        if (Enumerable.Range(0, SceneManager.sceneCount).Any(i => string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path)))
        {
            EditorUtility.DisplayDialog(Title, "Save the open scene first.", "OK");
            return;
        }
        Build();
    }

    // Makes the scene next to the open ones, saves it, and closes it again
    static void Build()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        Fill(scene);
        EditorSceneManager.SaveScene(scene, MenuPath);
        EditorSceneManager.CloseScene(scene, true);
        PutFirst();
        Debug.Log($"Created {MenuPath} and put it first in Build Settings. Open it and press Play to start from the menu.");
    }

    static void Fill(Scene scene)
    {
        var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)) { tag = "MainCamera" };
        var cam = camGo.GetComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = UiKit.Bg;
        camGo.transform.position = new Vector3(0f, 0f, -10f);
        SceneManager.MoveGameObjectToScene(camGo, scene);

        var go = new GameObject("MainMenu");   // builds its UI and EventSystem when the scene starts
        var menu = go.AddComponent<MainMenu>();
        menu.floor1 = SceneLinks.Name(SceneLinks.Key.Floor1);
        menu.floor2 = SceneLinks.Name(SceneLinks.Key.Floor2);
        menu.floor3 = SceneLinks.Name(SceneLinks.Key.Floor3);
        menu.floor4 = SceneLinks.Name(SceneLinks.Key.Floor4);
        menu.floor5 = SceneLinks.Name(SceneLinks.Key.Floor5);
        SceneManager.MoveGameObjectToScene(go, scene);
    }

    // the menu first in Build Settings, then every floor
    static void PutFirst()
    {
        var scenes = EditorBuildSettings.scenes.Where(s => AssetDatabase.LoadAssetAtPath<SceneAsset>(s.path) != null).ToList();
        bool ok = scenes.Count > 0 && scenes[0].path == MenuPath && Enumerable.Range(1, 5).All(n =>
            !SceneLinks.Exists(SceneLinks.Floor(n)) || scenes.Any(s => s.path == SceneLinks.Path(SceneLinks.Floor(n))));
        if (ok) return;
        scenes.RemoveAll(s => s.path == MenuPath);
        scenes.Insert(0, new EditorBuildSettingsScene(MenuPath, true));
        for (int n = 1; n <= 5; n++)
        {
            var key = SceneLinks.Floor(n);
            if (SceneLinks.Exists(key) && !scenes.Any(s => s.path == SceneLinks.Path(key)))
                scenes.Add(new EditorBuildSettingsScene(SceneLinks.Path(key), true));
        }
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
