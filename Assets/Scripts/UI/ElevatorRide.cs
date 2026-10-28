using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The ride between floors: elevator doors close over the screen, the floor indicator counts up while the next
/// scene loads behind them (with a gentle shake and passing light bars), the display flashes and 「3階」 stamps in
/// with that floor's game (a two-tone chime), then the doors open on the new floor; the doors whoosh both ways. Lives across the scene load and removes itself;
/// QuestBanner waits for it (Busy) before showing the floor's name and quest.
/// Used by the menu, the elevators going up (ElevatorExitZone) and the first floor's minigame.
/// </summary>
public class ElevatorRide : MonoBehaviour
{
    /// <summary>What each floor's game is, first floor first.</summary>
    public static readonly (string ja, string en)[] FloorGames =
    {
        ("エレベーターのボタンゲーム", "Elevator button game"),
        ("コインを集めてチケットを買おう", "Collect coins, buy a ticket"),
        ("建築学科の動物クイズ", "Animal quiz"),
        ("スタジオの屋台マッチング", "Stall matching game"),
        ("新聞配達ラン", "Newspaper run"),
    };

    /// <summary>True from the doors closing until they have opened again.</summary>
    public static bool Busy { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void NewPlay() => Busy = false;

    const float DoorTime = 0.45f, CountTime = 1.2f;
    static readonly Color Door = UiKit.Hex("#3a3288"), DoorEdge = UiKit.Accent, IndicatorBg = UiKit.Hex("#0c0a20");

    string scene;
    int from, to;
    RectTransform left, right, content, lines;
    CanvasGroup contentGroup, stampGroup;
    Text number, stampFloor, stampGame;
    RectTransform stamp;
    AudioSource audioSource;

    /// <summary>Ride to `sceneName`. `toFloor` 1–5 shows the floor and its game (0: doors only); the indicator
    /// counts from `fromFloor` (0: starts on the destination). False if the scene isn't in Build Settings.</summary>
    public static bool Go(string sceneName, int fromFloor, int toFloor)
    {
        if (Busy) return true;
        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError($"ElevatorRide: '{sceneName}' is not in Build Settings. Run Tools > Elevator Game > Create Menu Scene.");
            return false;
        }
        var ride = new GameObject("ElevatorRide").AddComponent<ElevatorRide>();
        ride.scene = sceneName;
        ride.from = fromFloor > 0 ? fromFloor : toFloor;
        ride.to = toFloor;
        DontDestroyOnLoad(ride.gameObject);
        Busy = true;
        return true;
    }

    void Awake()
    {
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.ignoreListenerPause = true;

        var canvas = UiKit.MakeCanvas("RideCanvas", 200, new Vector2(1280f, 720f));
        canvas.transform.SetParent(transform, false);
        var root = (RectTransform)canvas.transform;
        // blocks clicks to whatever is underneath while it runs
        var blocker = UiKit.Img("Blocker", root, null, Color.clear);
        blocker.raycastTarget = true;
        UiKit.Stretch(blocker.rectTransform);

        left = DoorPanel(root, 0f);
        right = DoorPanel(root, 1f);

        // passing light bars while riding
        lines = UiKit.Rect("Lines", root);
        lines.anchorMin = new Vector2(0f, 0f);
        lines.anchorMax = new Vector2(1f, 0f);
        lines.pivot = new Vector2(0.5f, 0f);
        lines.sizeDelta = new Vector2(0f, 720f + 240f);
        for (int i = 0; i < 4; i++)
        {
            var bar = UiKit.Img("Bar", lines, null, UiKit.Hex("#f2efff", 0.08f)).rectTransform;
            bar.anchorMin = new Vector2(0f, 0f);
            bar.anchorMax = new Vector2(1f, 0f);
            bar.pivot = new Vector2(0.5f, 0f);
            bar.sizeDelta = new Vector2(0f, 20f);
            bar.anchoredPosition = new Vector2(0f, i * 240f);
        }
        lines.gameObject.SetActive(false);

        // the indicator and the stamp, centred
        content = UiKit.Stretch(UiKit.Rect("Content", root));
        contentGroup = content.gameObject.AddComponent<CanvasGroup>();
        contentGroup.alpha = 0f;
        var display = UiKit.Box("Indicator", content, IndicatorBg, 16f, UiKit.Hex("#5a51b8"), 3f).rectTransform;
        display.anchorMin = display.anchorMax = display.pivot = new Vector2(0.5f, 0.5f);
        display.anchoredPosition = new Vector2(0f, 110f);
        display.sizeDelta = new Vector2(240f, 110f);
        var arrow = UiKit.Img("Arrow", display, UiKit.Arrow, UiKit.Accent).rectTransform;
        UiKit.Place(arrow, 30f, 37f, 36f, 36f);
        number = UiKit.Display(UiKit.Label(display, "", 64, UiKit.Accent));
        UiKit.Place(number.rectTransform, 76f, 0f, 150f, 110f);

        stamp = UiKit.Rect("Stamp", content);
        stamp.anchorMin = stamp.anchorMax = stamp.pivot = new Vector2(0.5f, 0.5f);
        stamp.anchoredPosition = new Vector2(0f, -70f);
        stamp.sizeDelta = new Vector2(900f, 170f);
        stampGroup = stamp.gameObject.AddComponent<CanvasGroup>();
        stampGroup.alpha = 0f;
        stampFloor = UiKit.Display(UiKit.Label(stamp, "", 96, UiKit.Fg));
        UiKit.HardShadow(stampFloor, UiKit.Hex("#15122f"), 6f);
        UiKit.Place(stampFloor.rectTransform, 0f, 0f, 900f, 112f);
        stampGame = UiKit.Label(stamp, "", 26, UiKit.Hex("#ffd27a"));
        UiKit.Place(stampGame.rectTransform, 0f, 118f, 900f, 40f);
    }

    RectTransform DoorPanel(RectTransform root, float side)
    {
        var door = UiKit.Img(side < 0.5f ? "DoorLeft" : "DoorRight", root, null, Door).rectTransform;
        door.anchorMin = new Vector2(side < 0.5f ? 0f : 0.5f, 0f);
        door.anchorMax = new Vector2(side < 0.5f ? 0.5f : 1f, 1f);
        door.offsetMin = door.offsetMax = Vector2.zero;
        var edge = UiKit.Img("Edge", door, null, DoorEdge).rectTransform;
        edge.anchorMin = new Vector2(side < 0.5f ? 1f : 0f, 0f);
        edge.anchorMax = new Vector2(side < 0.5f ? 1f : 0f, 1f);
        edge.pivot = new Vector2(side < 0.5f ? 1f : 0f, 0.5f);
        edge.sizeDelta = new Vector2(6f, 0f);
        edge.anchoredPosition = Vector2.zero;
        return door;
    }

    // 0 = open (off screen), 1 = shut
    void SetDoors(float shut)
    {
        float w = ((RectTransform)left.parent).rect.width * 0.5f;
        left.anchoredPosition = new Vector2(-w * (1f - shut), 0f);
        right.anchoredPosition = new Vector2(w * (1f - shut), 0f);
    }

    IEnumerator Start()
    {
        SetDoors(0f);
        bool showFloor = to >= 1 && to <= 5;
        number.text = from + "F";
        if (showFloor)
        {
            stampFloor.text = L.T(to + "階", to + "F");
            stampGame.text = L.T(FloorGames[to - 1].ja, FloorGames[to - 1].en);
        }

        // doors close
        audioSource.PlayOneShot(Sfx.Whoosh(), 0.8f);
        for (float t = 0f; t < DoorTime; t += Time.unscaledDeltaTime)
        {
            SetDoors(UiKit.EaseInOut(t / DoorTime));
            yield return null;
        }
        SetDoors(1f);
        audioSource.PlayOneShot(DoorsShut());

        // ride: load behind the doors while the number counts
        var load = SceneManager.LoadSceneAsync(scene);
        lines.gameObject.SetActive(true);
        int steps = Mathf.Abs(to - from);
        int dir = to >= from ? 1 : -1;
        float ride = showFloor ? CountTime : 0.3f;
        for (float t = 0f; t < ride || (load != null && !load.isDone); t += Time.unscaledDeltaTime)
        {
            contentGroup.alpha = Mathf.Clamp01(t / 0.2f) * (showFloor ? 1f : 0f);
            float k = Mathf.Clamp01(t / ride);
            if (steps > 0) number.text = (from + dir * Mathf.Min(steps, Mathf.FloorToInt(k * steps + 0.0001f))) + "F";
            // shake and light bars
            content.anchoredPosition = new Vector2(0f, t < ride ? Mathf.Sin(t * 70f) * 2f : 0f);
            lines.anchoredPosition = new Vector2(0f, -Mathf.Repeat(t * 700f, 240f));
            yield return null;
        }
        number.text = to + "F";
        content.anchoredPosition = Vector2.zero;
        lines.gameObject.SetActive(false);

        if (showFloor)
        {
            // ding: the display flashes and the floor stamps in
            audioSource.PlayOneShot(Ding());
            for (float t = 0f; t < 0.9f; t += Time.unscaledDeltaTime)
            {
                float flash = Mathf.Abs(Mathf.Sin(t / 0.3f * Mathf.PI));
                number.color = Color.Lerp(UiKit.Accent, UiKit.Hex("#fff3d6"), flash);
                float s = Mathf.Clamp01(t / 0.35f);
                stampGroup.alpha = Mathf.Clamp01(s * 1.7f);
                stamp.localScale = Vector3.one * (s < 0.6f ? Mathf.Lerp(1.5f, 0.95f, UiKit.EaseOut(s / 0.6f))
                                                            : Mathf.Lerp(0.95f, 1f, (s - 0.6f) / 0.4f));
                yield return null;
            }
            number.color = UiKit.Accent;
            stamp.localScale = Vector3.one;
            yield return new WaitForSecondsRealtime(0.35f);
            for (float t = 0f; t < 0.2f; t += Time.unscaledDeltaTime)
            {
                contentGroup.alpha = 1f - t / 0.2f;
                yield return null;
            }
        }
        contentGroup.alpha = 0f;

        // doors open on the new floor
        audioSource.PlayOneShot(Sfx.Whoosh(), 0.8f);
        for (float t = 0f; t < DoorTime; t += Time.unscaledDeltaTime)
        {
            SetDoors(1f - UiKit.EaseInOut(t / DoorTime));
            yield return null;
        }
        Busy = false;
        Destroy(gameObject);
    }

    // the doors meeting: a soft low thunk
    static AudioClip DoorsShut() => Sfx.Cached("rideDoors", () => Sfx.Make("rideDoors",
        (180, 0f, .12f, Sfx.Wave.Triangle, .18f, 120), (500, 0f, .06f, Sfx.Wave.Noise, .12f, 200)));

    // the arrival chime: two bell-like tones falling a major third, each with a quiet octave shimmer
    static AudioClip Ding() => Sfx.Cached("rideDing", () => Sfx.Make("rideDing",
        (1319, 0f, .6f, Sfx.Wave.Sine, .17f, 0), (2638, 0f, .25f, Sfx.Wave.Sine, .04f, 0),
        (1047, .2f, .9f, Sfx.Wave.Sine, .16f, 0), (2094, .2f, .3f, Sfx.Wave.Sine, .035f, 0)));

    void OnDestroy() => Busy = false;
}
