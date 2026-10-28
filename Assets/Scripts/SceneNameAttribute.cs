using UnityEngine;

/// <summary>
/// Marks a string field that holds a scene's name (the scene to load, the floor to return to). When a scene is
/// renamed in the Project window, the editor's SceneLinks rewrites every marked field that held the old name,
/// so links between floors and minigames survive renames.
/// </summary>
public class SceneNameAttribute : PropertyAttribute { }
