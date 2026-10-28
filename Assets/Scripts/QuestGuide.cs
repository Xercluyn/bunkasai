using System.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows the player where to go next on a floor: a bobbing amber marker with a label and distance over the
/// place (arcade box, studio console, ticket machine or elevator), an arrow at the edge of the screen when it's
/// out of view, and a "next" line under the quest panel. The target follows the quest:
/// not cleared → the floor's minigame (or, on the 2nd floor with enough money, the ticket machine);
/// cleared → the elevator up. Found from the floor's play zones, so it needs no setup.
/// Created at runtime by PlayerMovement.
/// </summary>
public class QuestGuide : MonoBehaviour
{
    public PlayerMovement player;

    enum Kind { None, Game, Ticket, Elevator }

    RectTransform canvasRect, marker, markerBob, edge, hintRect;
    Text markerText, hintText;
    GameObject hint;
    ElevatorPlayZone[] zones = new ElevatorPlayZone[0];
    float nextScan;
    Camera cam;

    void Awake()
    {
        var canvas = UiKit.MakeCanvas("QuestGuideCanvas", 18, new Vector2(1280f, 720f));   // under the quest banner's intro
        canvasRect = (RectTransform)canvas.transform;

        // marker over the target: label pill, then a ▼ pointing down at it
        marker = UiKit.Rect("Marker", canvasRect);
        marker.anchorMin = marker.anchorMax = new Vector2(0.5f, 0.5f);
        markerBob = UiKit.Rect("Bob", marker);
        var pill = UiKit.Box("Label", markerBob, UiKit.Hex("#241e57", 0.9f), 14f, UiKit.Accent, 2f);
        var prt = pill.rectTransform;
        prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0f);
        prt.pivot = new Vector2(0.5f, 0f);
        prt.anchoredPosition = new Vector2(0f, 34f);
        prt.sizeDelta = new Vector2(220f, 34f);
        markerText = UiKit.Label(prt, "", 17, UiKit.Fg);
        markerText.horizontalOverflow = HorizontalWrapMode.Overflow;
        UiKit.Stretch(markerText.rectTransform, 4f);
        var down = UiKit.Img("Arrow", markerBob, UiKit.Arrow, UiKit.Accent);
        var drt = down.rectTransform;
        drt.anchoredPosition = new Vector2(0f, 14f);
        drt.sizeDelta = new Vector2(30f, 30f);
        drt.localEulerAngles = new Vector3(0f, 0f, 180f);
        UiKit.HardShadow(down, UiKit.Hex("#b3560b"), 3f);

        // arrow on the screen edge when the target is out of view
        var e = UiKit.Img("EdgeArrow", canvasRect, UiKit.Arrow, UiKit.Accent);
        edge = e.rectTransform;
        edge.anchorMin = edge.anchorMax = new Vector2(0.5f, 0.5f);
        edge.sizeDelta = new Vector2(46f, 46f);
        UiKit.HardShadow(e, UiKit.Hex("#b3560b"), 3f);

        // "next" line under the quest panel (upper left)
        var hb = UiKit.Box("Next", canvasRect, UiKit.Hex("#241e57", 0.85f), 12f, UiKit.Accent, 2f);
        var hrt = hb.rectTransform;
        UiKit.Place(hrt, 16f, 118f, 500f, 42f);
        hintRect = hrt;
        hintText = UiKit.Label(hrt, "", 20, UiKit.Accent, TextAnchor.MiddleLeft);
        UiKit.Stretch(hintText.rectTransform, 4f);
        hintText.rectTransform.offsetMin = new Vector2(14f, 2f);
        hint = hb.gameObject;

        foreach (var g in canvasRect.GetComponentsInChildren<Graphic>()) g.raycastTarget = false;   // never blocks clicks
    }

    void Update()
    {
        if (Time.unscaledTime >= nextScan)
        {
            zones = FindObjectsByType<ElevatorPlayZone>(FindObjectsSortMode.None);
            nextScan = Time.unscaledTime + 1f;
        }
        if (cam == null) cam = Camera.main;

        var (kind, zone) = Pick();
        hint.SetActive(kind != Kind.None);
        hintText.text = HintLine(kind, zone);
        // just under the quest panel, however tall its text makes it
        hintRect.anchoredPosition = new Vector2(16f, -(QuestBanner.CornerBottom > 0f ? QuestBanner.CornerBottom + 10f : 118f));

        bool show = kind != Kind.None && zone != null && cam != null && player != null && !zone.PlayerInside;
        marker.gameObject.SetActive(false);
        edge.gameObject.SetActive(false);
        if (!show) return;

        Vector3 world = zone.TargetPosition();
        Vector3 sp = cam.WorldToScreenPoint(world);
        Vector2 half = canvasRect.rect.size * 0.5f;
        bool behind = sp.z < 0f;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, sp, null, out Vector2 local);
        if (behind) local = -local;   // mirrored when behind the camera

        const float margin = 56f;
        bool onScreen = !behind && Mathf.Abs(local.x) < half.x - margin && Mathf.Abs(local.y) < half.y - margin;
        if (onScreen)
        {
            marker.gameObject.SetActive(true);
            marker.anchoredPosition = local;
            markerBob.anchoredPosition = new Vector2(0f, 6f * Mathf.Sin(Time.unscaledTime * 4f));
            markerText.text = MarkerLine(kind, zone) + "  " + Metres(world);
            ((RectTransform)markerText.transform.parent).sizeDelta = new Vector2(markerText.preferredWidth + 28f, 34f);
        }
        else
        {
            // clamp the direction onto the screen's edge
            Vector2 dir = local.sqrMagnitude > 0.01f ? local.normalized : Vector2.up;
            float sx = (half.x - margin) / Mathf.Max(0.0001f, Mathf.Abs(dir.x));
            float sy = (half.y - margin) / Mathf.Max(0.0001f, Mathf.Abs(dir.y));
            edge.gameObject.SetActive(true);
            edge.anchoredPosition = dir * Mathf.Min(sx, sy);
            edge.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f);   // the sprite points up
            float pulse = 1f + 0.12f * Mathf.Sin(Time.unscaledTime * 6f);
            edge.localScale = Vector3.one * pulse;
        }
    }

    // What to aim for now, from the quest's state and the floor's play zones
    (Kind, ElevatorPlayZone) Pick()
    {
        string quest = player != null ? player.gameObject.scene.name : gameObject.scene.name;   // the floor's quest
        if (GameProgress.IsQuestCleared(quest))
        {
            if (GameProgress.PlayingSingleFloor) return (Kind.None, null);   // one floor from the menu: that's the win
            var up = zones.OfType<ElevatorExitZone>().FirstOrDefault(z => z.isActiveAndEnabled);
            return up != null ? (Kind.Elevator, up) : (Kind.None, null);   // the 5th floor: the ending takes over
        }
        var ticket = zones.OfType<TicketVendingZone>().FirstOrDefault(z => z.isActiveAndEnabled);
        if (ticket != null && GameProgress.Yen >= ticket.price) return (Kind.Ticket, ticket);
        var game = zones.FirstOrDefault(z => z != null && z.isActiveAndEnabled && z.GetType() == typeof(ElevatorPlayZone) &&
                                             !string.IsNullOrEmpty(z.minigameScene));
        if (game == null) return (Kind.None, null);
        return (IsElevator(game) ? Kind.Elevator : Kind.Game, game);   // on the 1st floor the elevator is the game
    }

    static bool IsElevator(ElevatorPlayZone z) =>
        z.interactable != null && z.interactable.name.ToLowerInvariant().Contains("elevator");

    static string MarkerLine(Kind kind, ElevatorPlayZone zone)
    {
        switch (kind)
        {
            case Kind.Ticket: return L.T("チケットを買おう！", "Buy the ticket!");
            case Kind.Elevator: return L.T("エレベーターへ！", "To the elevator!");
            default: return L.T("ゲームはここ！", "Play here!");
        }
    }

    string HintLine(Kind kind, ElevatorPlayZone zone)
    {
        switch (kind)
        {
            case Kind.Ticket: return L.T("次：自動販売機でチケットを買おう", "Next: buy the ticket at the vending machine");
            case Kind.Elevator: return L.T("次：エレベーターで上の階へ", "Next: take the elevator up");
            case Kind.Game:
                bool needsMoney = zones.OfType<TicketVendingZone>().Any(z => z.isActiveAndEnabled);
                return needsMoney ? L.T("次：ゲームで ¥3,000 集めよう", "Next: earn ¥3,000 in the game")
                                  : L.T("次：ゲームをクリアしよう", "Next: clear the game");
            default: return "";
        }
    }

    // Distance in metres, taking the player's height as 1.6 m (floors are modelled at different scales)
    string Metres(Vector3 world)
    {
        var cc = player.GetComponent<CharacterController>();
        float h = cc != null ? cc.height * player.transform.lossyScale.y : 1.6f;
        float m = Vector3.Distance(player.transform.position, world) * 1.6f / Mathf.Max(0.01f, h);
        return Mathf.RoundToInt(m) + "m";
    }
}
