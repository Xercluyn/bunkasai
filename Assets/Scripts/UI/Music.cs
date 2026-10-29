using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Background music, synthesised in code (MusicScore): the menu theme on the main menu and behind the ending panel,
/// a soft loop on the floors, silence in the minigames. Lives on a DontDestroyOnLoad object made before the first
/// scene; two looping 2D sources crossfade between tracks, the floor loop resumes where it left off after a minigame,
/// and the pause menu, the クエストクリア！ jingle and the elevator ride duck it. Fades run on unscaled time. Loudness
/// already follows AudioListener.volume (Sfx.Volume), so the levels here are relative to it.
/// </summary>
public class Music : MonoBehaviour
{
    enum Track { None, Menu, Floor }

    // the floor loop keeps its own level (0.14); the menu theme sits a little above it
    const float MenuLevel = 0.22f, FloorLevel = 0.14f, EndingLevel = MenuLevel * 0.8f;
    const float FadeOut = 1f, FadeInFloor = 2.5f, FadeInResume = 2f, FadeInMenu = 0.6f, FadeLeave = 0.25f;
    const float DuckDown = 0.15f, PauseDuck = 0.5f, PauseUp = 0.4f, CelebrateDuck = 0.25f, CelebrateUp = 1f,
        RideDuck = 0.7f, RideUp = 0.4f;

    // rendered samples: deterministic, so they are kept across play sessions (no domain reload) to skip re-rendering
    static volatile float[] menuData, floorData;
    static volatile int generation;   // bumped each play session: a render started in an earlier one is dropped
    static Music instance;
    static AudioClip menuClip, floorClip;
    static Track context;
    static int saved = -1;   // floor loop position to resume at after a minigame; -1 = from the start

    readonly AudioSource[] src = new AudioSource[2];
    readonly Track[] track = new Track[2];
    readonly float[] gain = new float[2], speed = new float[2];
    int cur = -1;   // the source playing the wanted track, -1 = none yet
    float duck = 1f, upSpeed = 1f;
    Task menuTask, floorTask;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void NewPlay()
    {
        generation++;
        instance = null;
        menuClip = floorClip = null;
        context = Track.None;
        saved = -1;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        if (instance != null) return;
        var go = new GameObject("Music");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<Music>();
    }

    void Awake()
    {
        for (int i = 0; i < 2; i++)
        {
            var s = src[i] = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = true;
            s.spatialBlend = 0f;
            s.ignoreListenerPause = true;
            s.priority = 0;
            s.volume = 0f;
        }
#if UNITY_WEBGL && !UNITY_EDITOR
        // no worker threads on WebGL: render now
        menuData ??= MusicScore.Menu();
        floorData ??= MusicScore.Floor();
#else
        int g = generation;
        if (menuData == null) menuTask = Task.Run(() => { var d = MusicScore.Menu(); if (g == generation) menuData = d; });
        if (floorData == null) floorTask = Task.Run(() => { var d = MusicScore.Floor(); if (g == generation) floorData = d; });
#endif
        // fires for the first scene too (subscribed before it loads); Start checks again in case it didn't
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void Start() => context = Detect();

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (instance == this) instance = null;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode) => context = Detect();

    // minigames first: their scenes may hold floor pieces too
    static Track Detect()
    {
        if (Has<FloorMinigame>() || Has<SecondFloorGame>() || Has<ThirdFloorGame>() || Has<FourthFloorGame>() || Has<ShinbunGame>())
            return Track.None;
        if (Has<MainMenu>()) return Track.Menu;
        return Has<PlayerMovement>() || Has<QuestBanner>() ? Track.Floor : Track.None;
    }

    static bool Has<T>() where T : Object => FindAnyObjectByType<T>() != null;

    void Update()
    {
        Collect(ref menuTask);
        Collect(ref floorTask);
        if (menuClip == null && menuData != null) menuClip = MakeClip("MusicMenu", menuData, MusicScore.MenuRate);
        if (floorClip == null && floorData != null) floorClip = MakeClip("MusicFloor", floorData, MusicScore.FloorRate);

        Track want = GameEnding.Showing ? Track.Menu : context;
        float level = want == Track.Menu ? (GameEnding.Showing ? EndingLevel : MenuLevel) : FloorLevel;
        if (cur >= 0 && track[cur] != want) Release(want);
        if (cur < 0 && want != Track.None) Begin(want, level);

        float dt = Time.unscaledDeltaTime;
        bool leaving = ElevatorPlayZone.Leaving;
        for (int i = 0; i < 2; i++)
        {
            bool on = i == cur;
            float target = on && !leaving ? level : 0f;
            float rate = on && leaving ? level / FadeLeave : speed[i];
            gain[i] = Mathf.MoveTowards(gain[i], target, rate * dt);
            if (!on && gain[i] <= 0f && src[i].isPlaying)
            {
                src[i].Stop();
                track[i] = Track.None;
            }
            src[i].volume = gain[i] * duck;
        }
        Duck(dt);
    }

    // the wanted track changed: fade the current one out (a floor loop left for silence keeps its place)
    void Release(Track want)
    {
        if (track[cur] == Track.Floor) saved = want == Track.None ? src[cur].timeSamples : -1;
        speed[cur] = Mathf.Max(gain[cur], 0.01f) / FadeOut;
        cur = -1;
    }

    // start the wanted track on the quieter source; waits (called again next frame) while its clip is still rendering
    void Begin(Track want, float level)
    {
        if (want == Track.Menu) saved = -1;
        // the floor loop still fading out (back from a minigame very fast): bring it back up where it is
        for (int i = 0; i < 2; i++)
            if (want == Track.Floor && track[i] == Track.Floor && src[i].isPlaying)
            {
                cur = i;
                speed[i] = level / FadeInResume;
                return;
            }
        var clip = want == Track.Menu ? menuClip : floorClip;
        if (clip == null) return;
        int n = gain[0] <= gain[1] ? 0 : 1;
        bool resume = want == Track.Floor && saved >= 0;
        var s = src[n];
        s.Stop();
        s.clip = clip;
        s.Play();
        s.timeSamples = resume ? Mathf.Clamp(saved, 0, clip.samples - 1) : 0;
        gain[n] = 0f;
        track[n] = want;
        speed[n] = level / (want == Track.Menu ? FadeInMenu : resume ? FadeInResume : FadeInFloor);
        cur = n;
        saved = -1;
    }

    // the lowest duck that applies wins; each comes back up at its own pace
    void Duck(float dt)
    {
        float target = 1f, up = 0f;
        if (FloorPauseMenu.Open) { target = PauseDuck; up = PauseUp; }
        if (QuestBanner.Celebrating && CelebrateDuck < target) { target = CelebrateDuck; up = CelebrateUp; }
        if (ElevatorRide.Busy && RideDuck < target) { target = RideDuck; up = RideUp; }
        if (target < 1f) upSpeed = (1f - target) / up;
        float rate = duck > target ? (1f - target) / DuckDown : upSpeed;
        duck = Mathf.MoveTowards(duck, target, rate * dt);
    }

    // a finished render task: nothing to do but report a failure (the task itself fills the cache)
    static void Collect(ref Task task)
    {
        if (task == null || !task.IsCompleted) return;
        if (task.IsFaulted) Debug.LogException(task.Exception);
        task = null;
    }

    static AudioClip MakeClip(string name, float[] data, int rate)
    {
        var clip = AudioClip.Create(name, data.Length, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
