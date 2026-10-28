using UnityEngine;

/// <summary>
/// The elevator on floors 2–4: standing in front of it shows 「３階へ」 etc. Once this floor's quest is cleared,
/// the click takes the player up to the next floor; until then its outline is grey and a press gets a reminder to
/// clear the quest first (a toast with its soft "wrong" buzz, at most every 2 seconds).
/// (The first floor's elevator opens its button minigame instead, and the fifth floor has nowhere to go.)
/// Set up by Tools > Elevator Game > Add Elevator Play Zone.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class ElevatorExitZone : ElevatorPlayZone
{
    [Tooltip("The floor scene the elevator goes up to (must be in Build Settings).")]
    [SceneName] public string nextFloorScene = "";

    [Tooltip("Quest that must be cleared first. Empty = this scene's name (the floor's QuestBanner).")]
    public string questId = "";

    float nextReminder;

    string Quest => string.IsNullOrEmpty(questId) ? gameObject.scene.name : questId;

    // playing one floor from the menu: there's no going up
    protected override bool Available => !GameProgress.PlayingSingleFloor;

    protected override bool Unlocked => GameProgress.IsQuestCleared(Quest);

    void Reset()
    {
        minigameScene = "";
    }

    void Start()
    {
        var audioSource = GetComponent<AudioSource>();   // kept on the elevator for scenes set up with it; Toast and Sfx play the sounds
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
    }

    protected override void Play()
    {
        if (!GameProgress.IsQuestCleared(Quest))
        {
            if (Time.unscaledTime < nextReminder) return;   // mashing E doesn't stack reminders
            nextReminder = Time.unscaledTime + 2f;
            Toast.Show(L.T("まずはこの階のクエストをクリアしよう！", "Clear this floor's quest first!"), UiKit.Bad);   // buzzes Wrong itself
            return;
        }
        if (!Application.CanStreamedLevelBeLoaded(nextFloorScene))
        {
            Debug.LogError($"ElevatorExitZone: '{nextFloorScene}' is not in Build Settings. Run Tools > Elevator Game > Add Elevator Play Zone on this floor.");
            return;
        }
        // arrives at the next floor's start, in front of its elevator, after the ride
        var banner = FindAnyObjectByType<QuestBanner>();
        int here = banner != null ? banner.FloorNumber : 0;
        Sfx.PlayUi(Sfx.Click());
        ElevatorRide.Go(nextFloorScene, here, here > 0 ? here + 1 : 0);
    }
}
