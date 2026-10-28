# Handoff — 学園祭 エレベーターゲーム (2026-10-28)

Where the project stands, so work can continue on another PC. (Claude's notes on the old PC don't travel with the repo; this file does.)

## Setting up on a new PC
1. Unity Hub → install **Unity 6000.6.4f1** → *Add project from disk* → this folder. The first open rebuilds `Library` (5–15 min).
2. Run **Tools > Elevator Game > Reimport Game Fonts** once (fallback fonts for ▶ ✓ ↻).
3. Run **Tools > Elevator Game > Apply Easy Mode** once and save; this writes the easier values into the scenes (see "Not done yet").
4. Open `Assets/Scenes/MainMenu.unity` → Play.

## The game in one paragraph
5 floor scenes (`FirstScene` … `FifthScene`), the MainMenu scene first in Build Settings. The player (an onigiri) walks each floor; a zone with an **E / click** prompt opens that floor's minigame (`ElevatorButtonGame`, `SecondFloorGame`, `ThirdFloorGame`, `FourthFloorGame`, `ShinbunArcade`). Clearing it clears the floor's quest. Floors 2–4 then ride the elevator up; 5F ends the game. The menu also lets you play one floor alone. All UI is built from code (`Assets/Scripts/UI/UiKit.cs`). Every string goes through `L.T("日本語", "English")`. All sounds are synthesised in `Assets/Scripts/UI/Sfx.cs`; there are no audio files.

## Done today
**Redesign ("Menu A — Lantern Night")**
- Design canvas: https://claude.ai/artifact/MpsvQKwZz6tXFKtWanJdb6 (private; open it from your claude.ai account).
- The festival is **学園祭** (not 文化祭).
- Fonts are in `Assets/Resources/Fonts` (SIL Open Font License):
  - Zen Maru Gothic for all text.
  - Dela Gothic One for titles, floor names, stamps and main buttons (`UiKit.Display`).
- Animated main menu; `ElevatorRide` door transition between floors; new `QuestBanner` floor intro, quest reveal and クエストクリア！ celebration; `GameEnding` panel pop.

**Polish pass** (done by 5 parallel subagents on top of a shared foundation)
- **Foundation:**
  - `Sfx.cs`: noise and sine waves, clip cache, saved `Sfx.Volume`, `PlayUi`, new sounds Step, Land, Click, Pop, Tick, Go, Warn, Whoosh, Coin, Thud.
  - `PlayerLock.cs`: stops the player moving during the intro, menus, the ride and the ending.
  - `MinigameKit.cs`: HowToPlay card, 3-2-1 Countdown, ResultThenExit (3 s, skippable), Shake, Punch.
  - `PlayerMovement` exposes WorldHeight, Moving and Grounded.
- **Audio:**
  - `Footsteps.cs`: distance-based steps scaled to the character's height, a settle step on stopping, a landing thud.
  - Every UiKit button clicks; toasts pop or buzz.
  - Elevator whoosh and ding; menu blips; the 3F voice follows the volume setting.
- **Floor UX:**
  - **Esc opens `FloorPauseMenu`:** resume, volume, look speed (PlayerPrefs `sfxVolume`, `lookSens`), language, 最初から (level 1), main menu. The last two need two presses.
  - The corner buttons are gone. `FloorMenuButton` and `LevelOneMenu` are now empty shells, kept because scenes reference them.
  - The E prompt is also clickable.
  - A locked elevator shows a grey outline plus a reminder toast.
  - The controls guide collapses after 10 s.
  - The quest arrow is never hidden and has a label.
- **Minigames:**
  - Every game gets a how-to card, a 3-2-1 countdown, sounds, text of 18 px or more, and a 3 s result screen.
  - 1F: 55 s; the pause label is fixed.
  - 2F: 85 s; ↑ drops money back instead of destroying it.
  - 3F: 14 s per question; the menu pauses the timer.
  - 4F: hint after 1 mistake; 1.4× text.
  - 5F: the ring is red with ✕ on trap doors; the tutorial comes first; Quit asks twice; easy values are clamped in code (7 hallways, 1.3 s, 0.35 s window, run speed 6.5).

## Not done yet / next steps
1. **Play-test everything.** None of today's polish pass has run in Unity yet; it was only compile-checked. Watch for:
   - footsteps in time on 1F and 5F (tune `Footsteps.strideBodyHeights`, `volume`);
   - Esc pause freezing and restoring correctly (Time.timeScale);
   - E and click both starting minigames without pressing Esc;
   - each minigame going how-to → 3-2-1 → play → result → back to its floor;
   - 2F yen counted once at the ticket machine;
   - text clipping in English.
2. **Apply Easy Mode** (menu tool) and save the scenes. The floor banners still hold 2.5 / 2 s in the saved scenes; 5F is already clamped in code.
3. The four new scripts were committed without `.meta` files (Unity wasn't refreshed). Unity creates them on first open. Commit those `.meta` files afterwards.
4. Optional follow-ups the subagents suggested:
   - a cancellable `MinigameKit.Countdown` (games currently block restart during 3-2-1);
   - an amber "reminder" toast that still buzzes (`Toast.cs` only buzzes for `UiKit.Bad`).

## Working conventions (for Claude or anyone)
- **The Unity editor stays open while working.** Scene changes are made as **Tools > Elevator Game** menu tools (`Assets/Editor/*.cs`), not by hand-editing open `.unity` files.
- **Compile without Unity** using Unity's bundled Roslyn:
  - `Editor/Data/DotNetSdk/dotnet.exe` + `sdk/8.0.318/Roslyn/bincore/csc.dll`
  - references and defines from `Assembly-CSharp(.Editor).csproj`
  - sources split by `/Editor/`
- **Play mode runs without domain reload.** Any static needs a `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` reset.
- **New on-screen text** uses `L.T(ja, en)`. Texts saved in scenes go through `L.Tr` and need an entry in its table in `L.cs`.
- **Scenes are found by GUID** (`SceneLinks.cs`); `[SceneName]` fields are fixed automatically on rename.
- **`bunkasai_asset/`** (backup of the original Game3dku project) is local only and git-ignored.
