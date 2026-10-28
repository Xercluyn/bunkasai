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

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void NewPlay() => holders.Clear();

    public static void Set(object who, bool locked)
    {
        if (who == null) return;
        if (locked) holders.Add(who);
        else holders.Remove(who);
    }

    /// <summary>True while anything holds the lock, or the elevator ride is running.</summary>
    public static bool Locked
    {
        get
        {
            if (ElevatorRide.Busy) return true;
            if (holders.Count == 0) return false;
            holders.RemoveWhere(h => h is Object o && o == null);
            return holders.Count > 0;
        }
    }
}
