using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 「≡ メニュー」 in the upper-right corner of every floor, under the controls guide (and under level 1's English /
/// 最初から row): back to the main menu, which ends this game. It asks for a second press within 3 seconds so it
/// isn't hit by accident. Esc frees the mouse to click it. Created by PlayerMovement.
/// </summary>
public class FloorMenuButton : MonoBehaviour
{
    Text label, hintText;
    RectTransform rt;
    Coroutine armed;

    void Awake()
    {
        var canvas = UiKit.MakeCanvas("FloorMenuCanvas", 19, new Vector2(1280f, 720f));
        UiKit.EnsureEventSystem();
        var button = UiKit.SubButton(canvas.transform, "", out label);
        rt = (RectTransform)button.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
        button.onClick.AddListener(Press);

        var hint = UiKit.Label(rt, "", 13, UiKit.Muted, TextAnchor.UpperRight, false);
        var hrt = hint.rectTransform;
        hrt.anchorMin = hrt.anchorMax = hrt.pivot = new Vector2(1f, 0f);
        hrt.anchoredPosition = new Vector2(0f, -22f);
        hrt.sizeDelta = new Vector2(220f, 18f);
        hintText = hint;

        Write();
        L.Changed += Write;
    }

    void Start()
    {
        // level 1 has its English / 最初から row where this would go: sit one row lower there
        bool levelOneRow = FindAnyObjectByType<LevelOneMenu>() != null;
        rt.anchoredPosition = new Vector2(-16f, levelOneRow ? -216f : -162f);
    }

    void Write()
    {
        label.text = armed != null ? L.T("もう一度押すとメニューへ", "Press again for menu") : L.T("≡ メニュー", "≡ Menu");
        rt.sizeDelta = new Vector2(Mathf.Max(150f, label.preferredWidth + 48f), 46f);
        hintText.text = L.T("Escでマウスを使う", "Esc frees the mouse");
    }

    void Press()
    {
        if (armed == null)
        {
            armed = StartCoroutine(Arm());
            Write();   // after armed is set, so it asks for the second press
            return;
        }
        StopCoroutine(armed);
        armed = null;
        ThirdPersonCamera.panelOpen = false;
        GameProgress.ToMenu();
    }

    IEnumerator Arm()
    {
        yield return new WaitForSecondsRealtime(3f);
        armed = null;
        Write();
    }

    void OnDestroy() => L.Changed -= Write;
}
