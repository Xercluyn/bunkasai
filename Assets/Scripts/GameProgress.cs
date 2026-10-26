using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What the player has done this session, kept across scene loads: cleared quests, and where the player
/// left a floor to play a minigame so they come back to the same spot.
/// </summary>
public static class GameProgress
{
    static readonly HashSet<string> clearedQuests = new HashSet<string>();

    static string returnScene;
    static Vector3 returnPosition;
    static Quaternion returnRotation;

    public static bool IsQuestCleared(string quest) => clearedQuests.Contains(quest);
    public static void ClearQuest(string quest) => clearedQuests.Add(quest);

    /// <summary>Remember where the player stands before a minigame scene replaces this floor.</summary>
    public static void RememberSpot(Transform player)
    {
        returnScene = player.gameObject.scene.name;
        returnPosition = player.position;
        returnRotation = player.rotation;
    }

    /// <summary>The remembered spot, once, if it belongs to this scene.</summary>
    public static bool TakeSpot(string scene, out Vector3 position, out Quaternion rotation)
    {
        position = returnPosition;
        rotation = returnRotation;
        if (returnScene != scene) return false;
        returnScene = null;
        return true;
    }
}
