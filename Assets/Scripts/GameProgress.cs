using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What the player has done this session, kept across scene loads: cleared quests, money, and where the player
/// left a floor to play a minigame so they come back to the same spot.
/// </summary>
public static class GameProgress
{
    static readonly HashSet<string> clearedQuests = new HashSet<string>();

    /// <summary>Money won in the second floor's game; spent on the third-floor ticket.</summary>
    public static int Yen;

    /// <summary>The floor scene picked on the menu to play on its own (clearing its quest wins the game);
    /// empty for the full run, floor 1 to 5.</summary>
    public static string SingleFloor = "";
    public static bool PlayingSingleFloor => !string.IsNullOrEmpty(SingleFloor);

    /// <summary>The menu scene (set by MainMenu when it opens); the game ends back there.</summary>
    public static string MenuScene = "MainMenu";

    static string returnScene;
    static Vector3 returnPosition;
    static Quaternion returnRotation;

    // Every press of Play is a new game. The project enters Play Mode without a domain reload (Project Settings >
    // Editor > Enter Play Mode Options), so statics would otherwise keep the last session's quests and money.
    // 最初から calls NewSession too; it keeps the mode (full run or one floor).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void NewPlay()
    {
        NewSession();
        SingleFloor = "";
    }

    public static void NewSession()
    {
        clearedQuests.Clear();
        Yen = 0;
        returnScene = null;
    }

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

    /// <summary>Back to the menu with a fresh session; the first scene in Build Settings if the menu isn't there.</summary>
    public static void ToMenu()
    {
        NewSession();
        SingleFloor = "";
        if (!string.IsNullOrEmpty(MenuScene) && Application.CanStreamedLevelBeLoaded(MenuScene))
            UnityEngine.SceneManagement.SceneManager.LoadScene(MenuScene);
        else
            UnityEngine.SceneManagement.SceneManager.LoadScene(0);
    }
}
