using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The floors' pause menu: Esc (gamepad Menu / OPTIONS) opens it over a dimmed screen and Esc (or 続ける) closes it. While it is open the game
/// stands still (Time.timeScale 0), the player can't move and the mouse is free. It has 続ける, a 音量 (volume) and a
/// 視点感度 (look speed) stepper, English / 日本語, 最初から (level 1 only, where the scene has a LevelOneMenu) and
/// メインメニューへ; the last two ask for a second press within 3 seconds so they aren't hit by accident.
/// It doesn't open during the elevator ride, while the ending panel is up or while a zone is leaving for its
/// minigame. Created by PlayerMovement (one per scene).
/// </summary>
public class FloorPauseMenu : MonoBehaviour
{
    /// <summary>True while the menu is on screen; ThirdPersonCamera frees the mouse for it.</summary>
    public static bool Open { get; private set; }

    const string LookKey = "lookSens";
    static readonly float[] LookSteps = { 0.06f, 0.09f, 0.12f, 0.16f, 0.20f };
    const int LookDefault = 2;
    static float? lookSpeed;

    /// <summary>Camera degrees per pixel of mouse movement (視点感度, saved).</summary>
    public static float LookSpeed => lookSpeed ?? (lookSpeed = PlayerPrefs.GetFloat(LookKey, LookSteps[LookDefault])).Value;

    static FloorPauseMenu instance;
    static int closedFrame = -1;

    /// <summary>True on the frame the menu closed: that press (A / × on 続ける) mustn't also start a zone.</summary>
    public static bool ClosedThisFrame => closedFrame == Time.frameCount;

    // no domain reload on entering Play Mode: never start a session paused
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void NewPlay()
    {
        Open = false;
        lookSpeed = null;
        instance = null;
        closedFrame = -1;
        Time.timeScale = 1f;
    }

    /// <summary>Make sure this scene has a pause menu.</summary>
    public static void Ensure()
    {
        if (instance == null) new GameObject("FloorPauseMenu").AddComponent<FloorPauseMenu>();
    }

    GameObject root, resumeButton;
    CanvasGroup group;
    RectTransform box, menuButton, restartButton;
    Text title, resumeText, volumeLabel, lookLabel, langText, restartText, menuText, hintText;
    Image[] volumeBars, lookBars;
    Coroutine armedRestart, armedMenu, pop;
    bool canRestart;

    const float W = 560f, H = 470f;

    void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;

        var canvas = UiKit.MakeCanvas("PauseCanvas", 40, new Vector2(1280f, 720f));
        canvas.transform.SetParent(transform, false);
        UiKit.EnsureEventSystem();

        // dimmed screen that takes every click, the panel in the middle
        var dim = UiKit.Img("Pause", canvas.transform, null, UiKit.WithAlpha(UiKit.Dim, 0.72f));
        dim.raycastTarget = true;
        UiKit.Stretch(dim.rectTransform);
        root = dim.gameObject;
        group = root.AddComponent<CanvasGroup>();
        box = UiKit.Box("Panel", dim.transform, UiKit.Panel, 24f, UiKit.Accent, 3f).rectTransform;
        UiKit.Center(box, W, H);
        UiKit.HardShadow(box.GetComponent<Image>(), UiKit.WithAlpha(UiKit.PanelShadow, 0.6f), 6f).useGraphicAlpha = false;

        title = UiKit.Display(UiKit.Label(box, "", 48, UiKit.Accent));
        UiKit.HardShadow(title, UiKit.AccentShadow, 5f);
        UiKit.Place(title.rectTransform, 0f, 22f, W, 64f);

        var resume = UiKit.MainButton(box, "", out resumeText);
        resumeButton = resume.gameObject;
        GamepadInput.SetDefault(resume, GamepadInput.MenuPriority);
        UiKit.Place((RectTransform)resume.transform, (W - 300f) * 0.5f, 100f, 300f, 56f);
        resume.onClick.AddListener(Close);

        volumeBars = Stepper(180f, out volumeLabel, StepVolume);
        lookBars = Stepper(236f, out lookLabel, StepLook);

        var lang = UiKit.SubButton(box, "", out langText);
        UiKit.Place((RectTransform)lang.transform, (W - 300f) * 0.5f, 302f, 300f, 46f);
        lang.onClick.AddListener(L.Toggle);

        var restart = UiKit.SubButton(box, "", out restartText);
        restartButton = (RectTransform)restart.transform;
        restart.onClick.AddListener(PressRestart);
        var menu = UiKit.SubButton(box, "", out menuText);
        menuButton = (RectTransform)menu.transform;
        menu.onClick.AddListener(PressMenu);

        hintText = UiKit.Label(box, "", 15, UiKit.Muted, TextAnchor.MiddleCenter, false);
        UiKit.Place(hintText.rectTransform, 0f, 424f, W, 24f);

        root.SetActive(false);
        Write();
        L.Changed += Write;
    }

    // 「音量　[−] ▮▮▮▮▯ [+]」: a label, the minus button, five bars, the plus button
    Image[] Stepper(float y, out Text label, System.Action<int> step)
    {
        label = UiKit.Label(box, "", 20, UiKit.Fg, TextAnchor.MiddleLeft);
        UiKit.Place(label.rectTransform, 48f, y, 190f, 46f);
        var minus = UiKit.SubButton(box, "−", out _);
        UiKit.Place((RectTransform)minus.transform, 250f, y, 46f, 46f);
        minus.onClick.AddListener(() => step(-1));
        var bars = new Image[5];
        for (int i = 0; i < bars.Length; i++)
        {
            bars[i] = UiKit.Box("Bar", box, UiKit.Line, 4f);
            UiKit.Place(bars[i].rectTransform, 310f + i * 30f, y + 11f, 24f, 24f);
        }
        var plus = UiKit.SubButton(box, "+", out _);
        UiKit.Place((RectTransform)plus.transform, 466f, y, 46f, 46f);
        plus.onClick.AddListener(() => step(1));
        return bars;
    }

    void Update()
    {
        var kb = Keyboard.current;
        bool toggle = (kb != null && kb.escapeKey.wasPressedThisFrame) || GamepadInput.MenuPressed;
        if (Open && GamepadInput.BackPressed) toggle = true;   // B / ○ closes it too
        if (!toggle) return;
        if (Open) Close();
        else if (CanOpen) Show();
    }

    // the ride, the ending panel and a zone leaving for its minigame keep the screen
    static bool CanOpen => !ElevatorRide.Busy && !ThirdPersonCamera.panelOpen && !ElevatorPlayZone.Leaving && !PlayerLock.JustReleased;

    void Show()
    {
        Open = true;
        Time.timeScale = 0f;
        PlayerLock.Set(this, true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        canRestart = FindAnyObjectByType<LevelOneMenu>() != null;   // level 1: a new game starts here
        Disarm();
        Write();
        root.SetActive(true);
        // on the gamepad, 続ける starts selected so the stick / D-pad can move between the buttons
        var es = EventSystem.current;
        if (es != null) es.SetSelectedGameObject(GamepadInput.UsingPad ? resumeButton : null);
        Sfx.PlayUi(Sfx.Whoosh(), 0.6f);
        if (pop != null) StopCoroutine(pop);
        pop = StartCoroutine(Pop());
    }

    // dim fades in, the panel pops: 90% → 103% → 100% (unscaled: the game is paused)
    IEnumerator Pop()
    {
        for (float t = 0f; t < 0.3f; t += Time.unscaledDeltaTime)
        {
            float k = t / 0.3f;
            group.alpha = Mathf.Clamp01(k * 3f);
            box.localScale = Vector3.one * Mathf.LerpUnclamped(0.9f, 1f, UiKit.EaseOutBack(k));
            yield return null;
        }
        group.alpha = 1f;
        box.localScale = Vector3.one;
        pop = null;
    }

    void Close()
    {
        if (!Open) return;
        Resume();
        root.SetActive(false);
        closedFrame = Time.frameCount;
        var es = EventSystem.current;
        if (es != null) es.SetSelectedGameObject(null);
    }

    // the game runs again (also when this menu goes away with its scene)
    void Resume()
    {
        Open = false;
        Time.timeScale = 1f;
        PlayerLock.Set(this, false);
        Disarm();
    }

    void OnDisable()
    {
        if (Open) Resume();
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
        L.Changed -= Write;
        if (Open) Resume();
        Time.timeScale = 1f;
    }

    void StepVolume(int d)
    {
        Sfx.Volume = Mathf.Clamp(VolumeLevel + d, 0, 5) / 5f;
        Write();
        Sfx.PlayUi(Sfx.Click());   // how loud it is now
    }

    static int VolumeLevel => Mathf.RoundToInt(Sfx.Volume * 5f);

    void StepLook(int d)
    {
        int i = Mathf.Clamp(LookLevel + d, 0, LookSteps.Length - 1);
        lookSpeed = LookSteps[i];
        PlayerPrefs.SetFloat(LookKey, LookSteps[i]);
        PlayerPrefs.Save();
        Write();
    }

    // the step nearest the saved speed
    static int LookLevel
    {
        get
        {
            int best = LookDefault;
            for (int i = 0; i < LookSteps.Length; i++)
                if (Mathf.Abs(LookSteps[i] - LookSpeed) < Mathf.Abs(LookSteps[best] - LookSpeed)) best = i;
            return best;
        }
    }

    void Write()
    {
        title.text = L.T("ポーズ", "PAUSED");
        resumeText.text = L.T("▶ 続ける", "▶ Resume");
        volumeLabel.text = L.T("音量", "Volume");
        lookLabel.text = L.T("視点感度", "Look speed");
        langText.text = L.English ? "日本語" : "English";   // the language it switches to
        restartText.text = armedRestart != null ? L.T("もう一度で最初から", "Again to restart") : L.T("↻ 最初から", "↻ Restart");
        menuText.text = armedMenu != null ? L.T("もう一度でメニューへ", "Again for menu") : L.T("メインメニューへ", "Main menu");
        hintText.text = L.T("Esc でゲームに戻る", "Esc: back to the game");
        Fill(volumeBars, VolumeLevel);
        Fill(lookBars, LookLevel + 1);

        // 最初から and メインメニューへ side by side on level 1, メインメニューへ alone elsewhere
        restartButton.gameObject.SetActive(canRestart);
        if (canRestart)
        {
            UiKit.Place(restartButton, 36f, 362f, 236f, 46f);
            UiKit.Place(menuButton, 288f, 362f, 236f, 46f);
        }
        else UiKit.Place(menuButton, (W - 300f) * 0.5f, 362f, 300f, 46f);
    }

    static void Fill(Image[] bars, int count)
    {
        for (int i = 0; i < bars.Length; i++) bars[i].color = i < count ? UiKit.Accent : UiKit.Line;
    }

    void PressRestart()
    {
        if (armedRestart == null)
        {
            armedRestart = StartCoroutine(Arm(true));
            Write();   // after armed is set, so it asks for the second press
            return;
        }
        Resume();
        GameProgress.NewSession();   // no quests, no money: a new game
        SceneManager.LoadScene(gameObject.scene.name);
    }

    void PressMenu()
    {
        if (armedMenu == null)
        {
            armedMenu = StartCoroutine(Arm(false));
            Write();
            return;
        }
        Resume();
        ThirdPersonCamera.panelOpen = false;
        GameProgress.ToMenu();
    }

    IEnumerator Arm(bool restart)
    {
        yield return new WaitForSecondsRealtime(3f);
        if (restart) armedRestart = null;
        else armedMenu = null;
        Write();
    }

    void Disarm()
    {
        if (armedRestart != null) StopCoroutine(armedRestart);
        if (armedMenu != null) StopCoroutine(armedMenu);
        armedRestart = armedMenu = null;
    }
}
