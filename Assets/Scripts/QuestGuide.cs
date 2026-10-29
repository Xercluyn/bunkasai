using System.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows the player where to go next on a floor: a bobbing cyan marker with a label and distance over the
/// place (arcade box, studio console, ticket machine or elevator), an arrow at the edge of the screen when it's
/// out of view (with the same short label under it, kept clear of the quest panel and the controls guide), and a
/// "next" line under the quest panel. All of it hides while the floor intro or the elevator ride has the screen.
/// The target follows the quest:
/// not cleared → the floor's minigame (or, on the 2nd floor with enough money, the ticket machine);
/// cleared → the elevator up. Found from the floor's play zones, so it needs no setup.
/// Created at runtime by PlayerMovement.
/// </summary>
public class QuestGuide : MonoBehaviour
{
    public PlayerMovement player;

    enum Kind { None, Game, Ticket, Elevator }

    const float ArrowHalf = 26f;   // half the edge arrow (46) at the top of its pulse
    const float LabelGap = 8f;     // space between the edge arrow and its label
    const float MarkerTop = 84f;   // the marker's label top above its target point (34 + 34 tall + 6 bob + 10 room)

    RectTransform canvasRect, marker, markerBob, edge, edgeLabel, hintRect;
    Text markerText, edgeText, hintText;
    GameObject hint;
    ElevatorPlayZone[] zones = new ElevatorPlayZone[0];
    float nextScan;
    Camera cam;

    void Awake()
    {
        var canvas = UiKit.MakeCanvas("QuestGuideCanvas", 21, new Vector2(1280f, 720f));   // over the corner panels; hidden during the intro
        canvasRect = (RectTransform)canvas.transform;

        // marker over the target: label pill, then a ▼ pointing down at it
        marker = UiKit.Rect("Marker", canvasRect);
        marker.anchorMin = marker.anchorMax = new Vector2(0.5f, 0.5f);
        markerBob = UiKit.Rect("Bob", marker);
        var pill = UiKit.Box("Label", markerBob, UiKit.WithAlpha(UiKit.Bg2, 0.9f), 14f, UiKit.Accent, 2f);
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
        UiKit.HardShadow(down, UiKit.AccentShadow, 3f);

        // arrow on the screen edge when the target is out of view
        var e = UiKit.Img("EdgeArrow", canvasRect, UiKit.Arrow, UiKit.Accent);
        edge = e.rectTransform;
        edge.anchorMin = edge.anchorMax = new Vector2(0.5f, 0.5f);
        edge.sizeDelta = new Vector2(46f, 46f);
        UiKit.HardShadow(e, UiKit.AccentShadow, 3f);
        var el = UiKit.Box("EdgeLabel", canvasRect, UiKit.WithAlpha(UiKit.Bg2, 0.9f), 12f, UiKit.Accent, 2f);
        edgeLabel = el.rectTransform;
        edgeLabel.anchorMin = edgeLabel.anchorMax = new Vector2(0.5f, 0.5f);
        edgeText = UiKit.Label(edgeLabel, "", 15, UiKit.Fg);
        edgeText.horizontalOverflow = HorizontalWrapMode.Overflow;
        UiKit.Stretch(edgeText.rectTransform, 2f);

        // "next" line under the quest panel (upper left)
        var hb = UiKit.Box("Next", canvasRect, UiKit.WithAlpha(UiKit.Bg2, 0.88f), 12f, UiKit.Accent, 2f);
        var hrt = hb.rectTransform;
        UiKit.Place(hrt, 16f, 118f, QuestBanner.CornerW, 36f);
        hintRect = hrt;
        hintText = UiKit.Fit(UiKit.Label(hrt, "", 17, UiKit.Accent, TextAnchor.MiddleLeft), 17, 12);   // one line
        UiKit.Stretch(hintText.rectTransform, 4f);
        hintText.rectTransform.offsetMin = new Vector2(12f, 2f);
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
        bool busy = QuestBanner.Intro || ElevatorRide.Busy;   // the intro / ride has the screen
        hint.SetActive(kind != Kind.None && !busy);
        hintText.text = HintLine(kind, zone);
        // just under the quest panel, however tall its text makes it
        hintRect.anchoredPosition = new Vector2(16f, -(QuestBanner.CornerBottom > 0f ? QuestBanner.CornerBottom + 10f : 118f));

        bool show = !busy && kind != Kind.None && zone != null && cam != null && player != null && !zone.PlayerInside;
        marker.gameObject.SetActive(false);
        edge.gameObject.SetActive(false);
        edgeLabel.gameObject.SetActive(false);
        if (!show) return;

        Vector3 world = zone.TargetPosition();
        Vector3 sp = cam.WorldToScreenPoint(world);
        Vector2 half = canvasRect.rect.size * 0.5f;
        bool behind = sp.z < 0f;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, sp, null, out Vector2 local);
        if (behind) local = -local;   // mirrored when behind the camera

        const float margin = 56f;
        // the camera looks down on the floor, so far targets sit high on the screen: the marker only shows while
        // its label still fits under the top edge, else the edge arrow takes over
        bool onScreen = !behind && Mathf.Abs(local.x) < half.x - margin &&
                        local.y > -half.y + margin && local.y < half.y - MarkerTop;
        if (onScreen)
        {
            marker.gameObject.SetActive(true);
            marker.anchoredPosition = local;
            markerBob.anchoredPosition = new Vector2(0f, 6f * Mathf.Sin(Time.unscaledTime * 4f));
            markerText.text = MarkerLine(kind, zone) + "  " + Metres(world);
            var pill = (RectTransform)markerText.transform.parent;
            float w = markerText.preferredWidth + 28f;
            pill.sizeDelta = new Vector2(w, 34f);
            // near a side edge the label slides inwards, the ▼ stays on the target
            float over = Mathf.Max(0f, Mathf.Abs(local.x) + w * 0.5f - (half.x - 8f));
            pill.anchoredPosition = new Vector2(-Mathf.Sign(local.x) * over, 34f);
        }
        else
        {
            // clamp the direction onto the screen's edge
            Vector2 dir = local.sqrMagnitude > 0.01f ? local.normalized : Vector2.up;
            float sx = (half.x - margin) / Mathf.Max(0.0001f, Mathf.Abs(dir.x));
            float sy = (half.y - margin) / Mathf.Max(0.0001f, Mathf.Abs(dir.y));
            Vector2 p = dir * Mathf.Min(sx, sy);
            Vector2 limit = half - new Vector2(margin, margin);
            Rect quest = QuestCorner(half), controls = ControlsRect();
            p = KeepOff(p, quest, limit);
            p = KeepOff(p, controls, limit);
            edge.gameObject.SetActive(true);
            edge.anchoredPosition = p;
            edge.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f);   // the sprite points up
            float pulse = 1f + 0.12f * Mathf.Sin(Time.unscaledTime * 6f);
            edge.localScale = Vector3.one * pulse;

            // its label sits on the screen side of the arrow, inside the screen
            edgeText.text = MarkerLine(kind, zone);
            Vector2 size = new Vector2(edgeText.preferredWidth + 22f, 28f);
            edgeLabel.sizeDelta = size;
            // far enough along -dir that the label's box clears the (pulsing) arrow, however wide the text is
            float reach = Mathf.Abs(dir.x) * size.x * 0.5f + Mathf.Abs(dir.y) * size.y * 0.5f;
            Vector2 lp = p - dir * (ArrowHalf + reach + LabelGap);
            if (Mathf.Abs(dir.y) >= Mathf.Abs(dir.x)) lp.x = p.x;   // top/bottom edge: straight above or below it
            lp.x = Mathf.Clamp(lp.x, -half.x + size.x * 0.5f + 8f, half.x - size.x * 0.5f - 8f);
            lp.y = Mathf.Clamp(lp.y, -half.y + 22f, half.y - 22f);
            lp = SlideOff(lp, size, quest, half);
            lp = SlideOff(lp, size, controls, half);
            lp = ClearOfArrow(lp, size, p, half);
            edgeLabel.gameObject.SetActive(true);
            edgeLabel.anchoredPosition = lp;
        }
    }

    // The quest panel and the 次： line under it (upper left: 16..16 + CornerW across, down to the line's bottom), in this
    // canvas's centred coordinates; empty before the panel exists
    Rect QuestCorner(Vector2 half)
    {
        if (QuestBanner.CornerBottom <= 0f) return Rect.zero;
        float bottom = hint.activeSelf ? -hintRect.anchoredPosition.y + hintRect.rect.height : QuestBanner.CornerBottom;
        return Rect.MinMaxRect(-half.x + 16f, half.y - bottom, -half.x + 16f + QuestBanner.CornerW, half.y - 16f);
    }

    // The controls guide (upper right) in this canvas's coordinates; empty without one
    Rect ControlsRect()
    {
        var panel = player != null ? player.ControlsPanel : null;
        if (panel == null || !panel.gameObject.activeInHierarchy) return Rect.zero;
        var corners = new Vector3[4];
        panel.GetWorldCorners(corners);   // overlay canvases: screen pixels
        Vector2 a = canvasRect.InverseTransformPoint(corners[0]), b = canvasRect.InverseTransformPoint(corners[2]);
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    // If clamping or sliding pushed the label back onto the arrow at `arrow`, moves it straight above or below
    // the arrow (whichever side has room)
    static Vector2 ClearOfArrow(Vector2 p, Vector2 size, Vector2 arrow, Vector2 half)
    {
        Vector2 h = size * 0.5f + new Vector2(ArrowHalf + LabelGap, ArrowHalf + LabelGap);
        if (Mathf.Abs(p.x - arrow.x) >= h.x || Mathf.Abs(p.y - arrow.y) >= h.y) return p;
        float below = arrow.y - h.y, above = arrow.y + h.y;
        bool belowFits = below - size.y * 0.5f >= -half.y + 8f;
        return new Vector2(p.x, belowFits && (arrow.y > 0f || above + size.y * 0.5f > half.y - 8f) ? below : above);
    }

    // Moves a label of `size` centred at p sideways (or else down/up) until it no longer overlaps `r`
    static Vector2 SlideOff(Vector2 p, Vector2 size, Rect r, Vector2 half)
    {
        if (r.width <= 0f) return p;
        Vector2 h = size * 0.5f + new Vector2(8f, 6f);
        if (p.x + h.x <= r.xMin || p.x - h.x >= r.xMax || p.y + h.y <= r.yMin || p.y - h.y >= r.yMax) return p;
        float right = r.xMax + h.x, left = r.xMin - h.x;
        bool rightFits = right + size.x * 0.5f <= half.x, leftFits = left - size.x * 0.5f >= -half.x;
        if (rightFits && (!leftFits || right - p.x <= p.x - left)) return new Vector2(right, p.y);
        if (leftFits) return new Vector2(left, p.y);
        return new Vector2(p.x, r.yMin - h.y);
    }

    // Slides an edge arrow at p out of `r` (grown by the arrow's size), along the screen edge it sits on;
    // anywhere nearest if sliding would leave the screen
    static Vector2 KeepOff(Vector2 p, Rect r, Vector2 limit)
    {
        if (r.width <= 0f) return p;
        const float pad = 34f;
        r = Rect.MinMaxRect(r.xMin - pad, r.yMin - pad, r.xMax + pad, r.yMax + pad);
        if (!r.Contains(p)) return p;
        bool onTopOrBottom = Mathf.Abs(p.y) >= limit.y - 1f, onSide = Mathf.Abs(p.x) >= limit.x - 1f;
        Vector2 best = p;
        float bestD = float.MaxValue;
        for (int pass = 0; pass < 2 && bestD == float.MaxValue; pass++)
        {
            bool any = pass == 1;
            foreach (var c in new[]
            {
                (new Vector2(r.xMax, p.y), any || onTopOrBottom), (new Vector2(r.xMin, p.y), any || onTopOrBottom),
                (new Vector2(p.x, r.yMin), any || onSide), (new Vector2(p.x, r.yMax), any || onSide),
            })
            {
                if (!c.Item2 || Mathf.Abs(c.Item1.x) > limit.x || Mathf.Abs(c.Item1.y) > limit.y) continue;
                float d = (c.Item1 - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = c.Item1; }
            }
        }
        return best;
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
            case Kind.Ticket: return L.T("次：自販機でチケットを買おう", "Next: buy the ticket");
            case Kind.Elevator: return L.T("次：エレベーターで上へ", "Next: take the elevator up");
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
