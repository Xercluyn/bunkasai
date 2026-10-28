using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A one-line message strip in the lower middle of the screen that fades after a few seconds
/// (the ticket machine's 「お金が足りません」, the elevator's 「クエストをクリアしてから」). Toast.Show from anywhere;
/// it builds its own canvas the first time in each scene.
/// </summary>
public class Toast : MonoBehaviour
{
    static Toast instance;

    Text text;
    CanvasGroup group;
    Coroutine fade;

    public static void Show(string message, Color color, float seconds = 2.5f)
    {
        if (instance == null) instance = Build();
        instance.Play(message, color, seconds);
    }

    static Toast Build()
    {
        var canvas = UiKit.MakeCanvas("ToastCanvas", 15, new Vector2(1280f, 720f));
        var toast = canvas.gameObject.AddComponent<Toast>();
        var panel = UiKit.Box("Toast", canvas.transform, UiKit.Panel, 14f, UiKit.Line, 2f);
        var rt = panel.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 150f);
        rt.sizeDelta = new Vector2(760f, 64f);
        toast.group = panel.gameObject.AddComponent<CanvasGroup>();
        toast.group.blocksRaycasts = false;
        toast.group.alpha = 0f;
        toast.text = UiKit.Label(panel.transform, "", 22, UiKit.Fg);
        UiKit.Stretch(toast.text.rectTransform, 12f);
        return toast;
    }

    void Play(string message, Color color, float seconds)
    {
        text.text = message;
        text.color = color;
        if (fade != null) StopCoroutine(fade);
        fade = StartCoroutine(Fade(seconds));
    }

    IEnumerator Fade(float seconds)
    {
        group.alpha = 1f;
        yield return new WaitForSecondsRealtime(seconds);
        for (float t = 0f; t < 0.5f; t += Time.unscaledDeltaTime)
        {
            group.alpha = 1f - t / 0.5f;
            yield return null;
        }
        group.alpha = 0f;
    }
}
