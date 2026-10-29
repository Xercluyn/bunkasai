# Handoff — 学園祭 エレベーターゲーム (2026-10-28)

Where the project stands, so work can continue on another PC. (Claude's notes on the old PC don't travel with the repo; this file does.)

## Setting up on a new PC
1. Unity Hub → install **Unity 6000.6.4f1** → *Add project from disk* → this folder. The first open rebuilds `Library` (5–15 min).
2. Run **Tools > Elevator Game > Reimport Game Fonts** once (fallback fonts for ▶ ✓ ↻).
3. Run **Tools > Elevator Game > Apply Difficulty Values** once (it saves the scenes itself); this writes the tuned values into the scenes (see "Not done yet").
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

## Done 2026-10-28 (second session)
- **IT theme:** navy + white + cyan accent replaces Lantern Night.
  - Tokens in `UiKit.cs`: Bg, Panel, Accent `#22d3ee`, AccentShadow, Ink, Dim, MainFill, Light, …
  - Helpers `UiKit.Fit` (best-fit, shrink instead of wrap) and `UiKit.WithAlpha`.
  - Minigames keep their own local palettes, in the same family.
  - `InteractableOutline.color` is renamed to `tint` (default cyan), so the old orange saved in scenes is ignored.
- **Text overlaps fixed:**
  - Main menu: the floor card (the 2階 overlap) is now 480×112 with single-line best-fit; floor labels are clamped on screen; the title has new spacing.
  - Also fixed: GameEnding, Toast, QuestGuide, 2F best time / legend / hint, 4F congrats.
- **5F is harder:**
  - New values: 10 hallways, speed 7.5, prompt 1.1 s, good 0.25 s, perfect 0.08 s.
  - The clamps in code are removed.
  - Pressing while a ring shows but off-time is a MISS ("早い！"), and on a trap it is a hit.
  - The run camera is closer: it is computed from the aspect ratio so you see about 9.9 units ahead, and the next door (12.3 away) stays hidden.
  - The ring is clamped on screen.
- **Walls:**
  - New **Tools > Elevator Game > Make Walls Double-Sided (All Floors)**. The FBX walls have mixed face winding, and URP was culling half of them. The tool writes `Assets/Generated/<scene>/<mesh> (2s).asset`.
  - WallGapCloser can now find gaps along straight walls.
  - ThirdPersonCamera is pulled in by the walls.
  - Near clip is now 0.1 × scale.

**Run in Unity, in this order, then save:**
1. Close Wall Gaps on ThirdScene. It was never run there.
2. Make Walls Double-Sided (All Floors).
3. Set Up This Floor on each floor, to bake the near clip.
4. Apply Difficulty Values (was Apply Easy Mode), to write the new 5F values into ShinbunArcade.
5. Outline Interactables / the floor setups, if the camera background colours in the scenes still look old.

## Done 2026-10-28 (third session): the floors look like the real campus
- Source: facility photos and floor plans at yomiuririkou.ac.jp/iten (キャンパス施設紹介).
- Floor mapping:
  - 1F lobby;
  - 2F reception and library;
  - 3F 建築 (drafting and PC rooms);
  - 4F TV studio, sub-control room and power lab;
  - 5F 介護, 臨床工学, IT and the lounge.
- Everything lives in `Assets/Editor/School/`:
  - `SchoolPalette`: per-floor colours taken from the photos.
  - `SchoolKit`: materials, primitive props, placement and free-spot search.
  - `SchoolTextures`: procedural seamless PNGs in `Assets/Textures/School`.
  - `ShellDresser`: splits each shell into floor, wall and ceiling submeshes with world UVs, as `… dressed (2s)` meshes.
  - `PropRecolour`: recolours the furniture.
  - `PropsLower` / `PropsUpper`: campus props saved as prefabs in `Assets/Prefabs/School`, placed under a `SchoolProps` root.
  - `SchoolDresser`: the one-click menu.
- The `SchoolProps` root has the `SchoolDressing` marker, so the building tools skip it.
- `IndoorLightingSetup` now sets a light mood per floor, plus a blue studio wash on 4F.
- **Run:** Tools > Elevator Game > School > **Dress School (All Floors)**, after Make Walls Double-Sided.
  - Every step is safe to re-run.
  - Props you have moved by hand stay where they are. To re-place a prop, delete it under `SchoolProps`.
- **Not run in Unity yet.** Check:
  - prop placement, especially on 1F `door`, 5F `bed` and the size of the 4F studio;
  - sign text orientation;
  - tile scale.
- Compile without Unity with `Tools/compile.ps1`.

## Done 2026-10-29: layout pass
- **2F:** library chairs are brown (frames #7A5A3C, seats #6A4C30), darker than the #D8C8A8 tables. The reception counter's base takes the wall's slate colour.
- **3F** (`SchoolLayout3F`):
  - In the room in front of the architecture class, the merged FBX desks are hidden and replaced by 17 `f3_pcdesk_NN` at the same spots.
  - In the architecture class, `paper` and `leg2` are hidden. They are replaced by 7 rows of 3 joined drafting sets (`f3_draft_R_C`), running along X and facing north.
  - The model and cabinet are moved in, once.
- **5F:** floor, walls and furniture colours match 3F. `SchoolLayout5F` adds:
  - a silver laptop per seat in the itSign room (40) and a black one per seat in the ArcadeBox room (54);
  - care moved to the left (north) and CE to the right (south) of the shared room, each moved once.
- **Deleted props stay deleted:** `SchoolDressing.placed` records every id ever placed. Ids missing from `SchoolProps` were deleted by hand and are never placed again. To bring one back, remove its id from the list. One-off moves are recorded as `#step` entries, so re-runs keep your hand nudges.
- **Run:** Dress School (All Floors), or Recolour Furniture plus **Arrange Rooms** (now under School > Steps).

## Done 2026-10-29: second layout pass
- Sign text uses the depth-tested `Assets/Shaders/WorldText.shader`, so it no longer shows through walls.
- 2F: a copy of the floor's dark grey door stands beside the reception counter (`SchoolLayout2F`).
- 3F architecture class:
  - 5 rows of 3 short 2.4 m drafting sets with 2 stools each, centred with wide aisles;
  - the first and last rows are retired;
  - paperwork, rulers and study models on 9 tables (`f3_deco_*`);
  - `apron_short` shelves between the rows are hidden, and the ones at the back are painted white.
- 5F: `AlleyCutter` removes two wall panels (z −45.83 and z −20.39, x −171..−150). That connects the corridor, through the closed pocket, to the hallway behind the elevators. The source images are in `Assets/Problems`.
- Menu: **Arrange Rooms**. Dress School runs it too.

## Done 2026-10-29: editor-tools refactor (branch `editor-tools-refactor`)
- **`Assets/Editor/EditorKit.cs`** is the one home for the shared editor helpers:
  - `EnsureFolder`, `FileName`, `GeneratedFolder`, `IsGenerated`, `IsModelMesh`, `SaveMesh` (save in place), `StripTags` ("(2s)" and tag naming);
  - `NotPlaying`, `OnOpenFloor`, `ForFloors`, `ForScenes` (open each scene, apply, save only if changed, restore what was open);
  - `BuildingRoots(scene)`, `FindInBuilding`, `OutlinedFilters`, `All<T>`, `Hide`, `RendererBounds`, `PathOf`.
- **`SchoolKit`** keeps the school-only helpers, plus the merged `Ground`, `PlayerFloorY`, `OverlapsWith`, `HexMat` and `V`. `SchoolKit.FloorOf` is gone (use `SceneLinks.FloorOf`).
- Make Walls Double-Sided (All Floors), Set Up Elevators On All Floors and the difficulty tool now use `ForFloors` / `ForScenes`.
- **Two small deliberate differences:**
  - 5F Arrange Rooms checks overlaps with the 3F box (`OverlapsWith`).
  - PropsUpper's anchor height uses `PlayerFloorY`.
  - Both only matter on a scene that hasn't been dressed yet: 5F layout is a one-off `Once` step, and placed groups are skipped.
- **Menu:** top-level names are unchanged (runtime messages quote them). Priorities add separators: floor setup · zones · quests · minigame scenes · walls · School · character · project.
  - **School:** Dress School (This Floor / All Floors) on top; everything else under **School > Steps**: Dress Floor Surfaces, Recolour Furniture, List Floor Materials, Place School Props (one item for all floors), Arrange Rooms (2F door, 3F rooms, 5F rooms and the 5F alley), Repaint Textures.
  - **Apply Easy Mode** is now **Apply Difficulty Values**.
- **Kept on purpose:**
  - `FourthFloorArtWatcher`: it re-links the 4F images when the PNGs are replaced.
  - `SceneLinks` former names: Fix Scene Links needs them.
  - `IndoorLightingSetup.DropToFloor`: it measures a distance with back faces, which is not a floor-height lookup.
- **Over the 800-line limit (runtime, not split yet):** `SecondFloorGame.cs` 1392, `FourthFloorGame.cs` 1053, `FloorMinigame.cs` 889. Split them after the play-test. Every editor file is under 800.
- **Still to do:** in Unity, re-run Dress School (All Floors) and check with `git status` that the scenes don't change. Then merge the branch.

## Done 2026-10-29: 5F ending and festival posters (not run in Unity yet)
- **5F ending** (`ShinbunGame.Ending`, as in GAMEPLAY.txt), skippable with Space, click or Esc:
  - a pulsing extraction ring on the far helipad that triggers the pick-up;
  - the plane hooks the agent;
  - a cut to a low angle from the helipad deck as the plane climbs away.
  - It always plays. `quickFinish` now only swaps the results screen for the short goal card.
- **Festival posters** (`School/FestivalPosters.cs`; School > Steps > Hang Festival Posters, also run by Dress School):
  - **Assets:** `Assets/Models/School/festival_poster.fbx` (from bunkasai_asset/levels_asset/poster.fbx) and `Assets/Textures/School/festival_poster.jpeg`. The FBX's `Material` and `Material.001` are remapped to `FestivalPoster.mat` (the picture) and `FestivalPosterFrame.mat`.
  - **Per floor, along the spawn → zones route:** up to 4 wall posters (Cube.003), 2 floor stands (Cube.002) and 2 ceiling banners (Plane and Plane.001 back to back).
  - **Sizes** are set from the player's height.
  - **Ids** are `f{n}_poster_{wall|stand|banner}_{k}`, kept or left out like the other props.
  - **Check:** that posters aren't mirrored or upside down. The face direction comes from the picture faces' normals.

## Not done yet / next steps
1. **Play-test everything.** None of today's polish pass has run in Unity yet; it was only compile-checked. Watch for:
   - footsteps in time on 1F and 5F (tune `Footsteps.strideBodyHeights`, `volume`);
   - Esc pause freezing and restoring correctly (Time.timeScale);
   - E and click both starting minigames without pressing Esc;
   - each minigame going how-to → 3-2-1 → play → result → back to its floor;
   - 2F yen counted once at the ticket machine;
   - text clipping in English.
2. **Apply Difficulty Values** (menu tool; it saves the scenes). The floor banners still hold 2.5 / 2 s in the saved scenes; 5F is already clamped in code.
3. The four new scripts were committed without `.meta` files (Unity wasn't refreshed). Unity creates them on first open. Commit those `.meta` files afterwards.
4. Optional follow-ups the subagents suggested:
   - a cancellable `MinigameKit.Countdown` (games currently block restart during 3-2-1);
   - an amber "reminder" toast that still buzzes (`Toast.cs` only buzzes for `UiKit.Bad`).

## Working conventions (for Claude or anyone)
- **The Unity editor stays open while working.** Scene changes are made as **Tools > Elevator Game** menu tools (`Assets/Editor/*.cs`), not by hand-editing open `.unity` files.
- **Compile without Unity** with `powershell -File Tools/compile.ps1` (works from any clone path), which uses Unity's bundled Roslyn:
  - `Editor/Data/DotNetSdk/dotnet.exe` + `sdk/8.0.318/Roslyn/bincore/csc.dll`
  - references and defines from `Assembly-CSharp(.Editor).csproj`
  - sources split by `/Editor/`
- **Play mode runs without domain reload.** Any static needs a `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` reset.
- **New on-screen text** uses `L.T(ja, en)`. Texts saved in scenes go through `L.Tr` and need an entry in its table in `L.cs`.
- **Scenes are found by GUID** (`SceneLinks.cs`); `[SceneName]` fields are fixed automatically on rename.
- **`bunkasai_asset/`** (backup of the original Game3dku project) is local only and git-ignored.
