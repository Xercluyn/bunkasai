using UnityEngine;

/// <summary>
/// The second floor's ticket machine: walking up to it shows チケットを購入. With at least `price` yen
/// (won in the arcade's 2nd-floor game) the click buys the third-floor ticket and clears this floor's quest;
/// otherwise a message says how much is missing. After the purchase the button no longer appears.
/// Set up by Tools > Elevator Game > Set Up Second Floor Quest.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class TicketVendingZone : ElevatorPlayZone
{
    [Tooltip("Ticket price in yen.")]
    public int price = 3000;

    [Tooltip("Quest cleared by buying the ticket. Empty = this scene's name (the floor's QuestBanner).")]
    public string questId = "";

    AudioSource audioSource;
    AudioClip sfxGood, sfxBad;

    string Quest => string.IsNullOrEmpty(questId) ? gameObject.scene.name : questId;

    protected override bool Available => !GameProgress.IsQuestCleared(Quest);

    void Reset()
    {
        buttonLabel = "チケットを購入";
        minigameScene = "";
    }

    void Start()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        sfxGood = Sfx.Win();
        sfxBad = Sfx.Bad();
    }

    protected override void Play()
    {
        if (GameProgress.Yen >= price)
        {
            GameProgress.Yen -= price;
            GameProgress.ClearQuest(Quest);   // QuestBanner switches to クエストクリア！
            audioSource.PlayOneShot(sfxGood);
            Toast.Show(L.T("３階へのチケットを購入しました！エレベーターで３階へ行こう！",
                "You bought a ticket to 3F! Take the elevator up to the 3rd floor!"), UiKit.Good);
        }
        else
        {
            audioSource.PlayOneShot(sfxBad);
            Toast.Show(L.T($"お金が足りません（¥{GameProgress.Yen:#,0} / ¥{price:#,0}）。アーケードで稼ごう！",
                $"Not enough money (¥{GameProgress.Yen:#,0} / ¥{price:#,0}). Earn some at the arcade!"), UiKit.Bad);
        }
    }
}
