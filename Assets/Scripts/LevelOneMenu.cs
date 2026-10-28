using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Level 1's two buttons under the 操作方法 guide (upper right): English / 日本語 switches the whole game's
/// language, and 最初から starts a new game here (all quests and money cleared). 最初から asks for a second press
/// within 3 seconds so it isn't hit by accident. Esc frees the mouse to click them.
/// Added by Tools > Elevator Game > Set Up First Floor Quest.
/// </summary>
public class LevelOneMenu : MonoBehaviour
{
    Text langText, restartText;
    Coroutine armed;

    void Awake()
    {
        var canvas = UiKit.MakeCanvas("LevelOneMenuCanvas", 19, new Vector2(1280f, 720f));
        UiKit.EnsureEventSystem();

        var lang = UiKit.SubButton(canvas.transform, "", out langText);
        Corner((RectTransform)lang.transform, -16f - 150f - 8f, 140f);
        lang.onClick.AddListener(L.Toggle);

        var restart = UiKit.SubButton(canvas.transform, "", out restartText);
        Corner((RectTransform)restart.transform, -16f, 150f);
        restart.onClick.AddListener(Restart);

        Write();
        L.Changed += Write;
    }

    // top-right, just under the controls guide
    static void Corner(RectTransform rt, float x, float w)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(x, -162f);
        rt.sizeDelta = new Vector2(w, 46f);
    }

    void Write()
    {
        langText.text = L.English ? "日本語" : "English";   // the language it switches to
        restartText.text = armed != null ? L.T("もう一度押す", "Press again") : L.T("↻ 最初から", "↻ Restart");
    }

    void Restart()
    {
        if (armed == null)
        {
            armed = StartCoroutine(Arm());
            Write();   // after armed is set, so it reads もう一度押す
            return;
        }
        StopCoroutine(armed);
        armed = null;
        GameProgress.NewSession();   // no quests, no money: a new game
        SceneManager.LoadScene(gameObject.scene.name);
    }

    IEnumerator Arm()
    {
        yield return new WaitForSecondsRealtime(3f);
        armed = null;
        Write();
    }

    void OnDestroy() => L.Changed -= Write;
}
