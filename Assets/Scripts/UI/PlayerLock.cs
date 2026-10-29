using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stops the player from walking while something has the screen: the floor intro, the pause menu, the ending
/// panel, a zone that is leaving for its minigame. Each holder locks and unlocks for itself; the player stays
/// still while any holder remains, and during the elevator ride. Holders that were destroyed (a scene load) no
/// longer count.
/// </summary>
public static class PlayerLock
{
    static readonly HashSet<object> holders = new HashSet<object>();
    static int releasedFrame = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void NewPlay()
    {
        holders.Clear();
        releasedFrame = -1;
    }

    public static void Set(object who, bool locked)
    {
        if (who == null) return;
        if (locked) holders.Add(who);
        else if (holders.Remove(who)) releasedFrame = Time.frameCount;
    }

    /// <summary>A holder let go this frame: the press that closed its panel mustn't also act on the floor.</summary>
    public static bool JustReleased => releasedFrame == Time.frameCount;

    /// <summary>True while anything holds the lock, the elevator ride is running, or for the rest of the frame a
    /// holder lets go in (so A / Enter on このまま見て回る doesn't also start the game next to the player).</summary>
    public static bool Locked
    {
        get
        {
            if (ElevatorRide.Busy || JustReleased) return true;
            if (holders.Count == 0) return false;
            holders.RemoveWhere(h => h is Object o && o == null);
            return holders.Count > 0;
        }
    }
}
