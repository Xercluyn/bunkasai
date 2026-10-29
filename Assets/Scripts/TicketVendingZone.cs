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
    AudioClip sfxGood;

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
    }

    protected override void Play()
    {
        if (GameProgress.Yen >= price)
        {
            GameProgress.Yen -= price;
            GameProgress.ClearQuest(Quest);   // QuestBanner switches to クエストクリア！
            audioSource.PlayOneShot(sfxGood);
            Toast.Show(L.T("チケットGET！エレベーターで３階へ！",
                "Ticket bought! Take the elevator to 3F!"), UiKit.Good);
        }
        else
        {
            Toast.Show(L.T($"お金が足りない（¥{GameProgress.Yen:#,0} / ¥{price:#,0}）。ゲームで稼ごう！",
                $"Not enough money (¥{GameProgress.Yen:#,0} / ¥{price:#,0}). Earn more in the game!"), UiKit.Bad);
        }
    }
}
