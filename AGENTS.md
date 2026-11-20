# Working on Ultra Puck (instructions for AI assistants)

You're helping build **Ultra Puck**, an original top-down air-hockey/ice-hockey brawler made in **Unity 6000.6.9f1**
(C#, Mono, built-in render pipeline). Players skate, carry the puck in a wedge, check (dash) into each other, charge
shots, and pick power-up cards after goals. Online play is rollback netcode over Steam or IP. Up to 8 players.

Another assistant (Claude) also works on this project. **Keep the handoff log at the bottom of this file up to date**
so whoever works next knows what changed.

## Golden rules
1. **The rules are deterministic.** Everything in `Assets/UltraPuck/Core/` (Simulation, GameState, Bot, MapDef,
   Cards, Net) uses fixed-point maths (`Fix` Q48.16, `FixVec2`), never `float`, `UnityEngine`, `Random`, time, or
   dictionary/hash ordering. Every PC must compute bit-identical games. Order-dependent logic must not favour a
   player slot or a side (ties go to `s.NextRandom()`); the mirror and fairness tests check this.
2. **Any change to the rules, maps, cards or network messages: bump `NetSession.Version`**
   (`Core/Net/NetSession.cs`) and add a word to its history comment. New state fields go into `GameState.Clone()`
   (if not value types) and `GameState.Checksum()`.
3. **Run the tests** before saying something works (all must pass; 5 are `[Explicit]` benchmarks and are skipped):
   `"C:\Program Files\Unity\Hub\Editor\6000.6.9f1\Editor\Unity.exe" -batchmode -projectPath . -runTests -testPlatform EditMode -testResults <out.xml> -logFile <log>`
   Tests live in `Assets/UltraPuck/Tests/` (CoreTests, CardMechanicsTests, CardTests, NetTests, PerfTests,
   SoundTests). Set env var `ULTRAPUCK_SOUNDS=<folder>` while running them to save every sound effect there as a .wav.
4. **Build** to check it compiles: `-batchmode -quit -projectPath . -executeMethod UltraPuck.Editor.BuildScript.BuildWindows`
   (also `BuildLinux`). Close the Unity editor first if it has the project open.
5. **Look at visual changes** with the built-in screenshot mode instead of guessing: run
   `Builds/Windows/UltraPuck.exe -screen-width 1280 -screen-height 720 -screen-fullscreen 0 -logFile <somewhere else>`
   with env vars `ULTRAPUCK_SHOTS=<folder>`, `ULTRAPUCK_NOSTEAM=1`, `ULTRAPUCK_NOUPDATE=1`, optional
   `ULTRAPUCK_SHOTS_ONLY=title,music,players,online,ripple,replay,maps,ice,clip,pairing,countdown,goal,cards,pausemenu,settings`
   (`music` takes no pictures: it skips match songs to their end and the log shows the next one starting).
   Scenes are in `GameRunner.Shots()`; add one for new screens. Always pass `-logFile` (don't overwrite the owner's
   Player.log) and never change saved settings (PlayerPrefs) from a scene.
   The screenshot mode is silent (so it doesn't blast music at whoever is at the PC). For each picture the log has a
   line `Ultra Puck sound at <name>: ...` with how many times each sound effect has played so far, what the music is
   doing, and how loud the output is. `ULTRAPUCK_SHOTS_VOLUME=0.3` plays the run out loud; `0.001` can't be heard
   but still shows a non-zero output level in the log (at 0 the level always reads 0).
6. **Don't commit or push unless asked.** Releases: when the owner says "Release X", run
   `powershell -File Tools/release.ps1 -Bump -Notes "<plain-English notes>"` (builds Windows + Linux, publishes a
   GitHub release, every copy auto-updates), then `git add -A`, commit (`0.1.N: summary`), push.
7. **Keep the game original**: no names, art, levels or text from other games (it was inspired by ClusterPuck 99 but
   must not copy it).
8. Write UI text and notes in plain, friendly English (no jargon).

## Where things are
- `Core/Simulation.cs` the whole rule set (Step, Control, puck carry/pickup, wedge pickup, checks, goals, ice, cards).
  `Core/Tuning.cs` all numbers. `Core/GameState.cs` state + `PlayerInput` (button bits incl. `Pause`).
- `Core/Bot.cs` bots (bent rinks follow the map's `route`). `Core/MapDef.cs` map JSON loader (walls, goals with
  `facing`, spawns, `route`).
- `Core/Net/` NetSession (host-star lobby/match, inputs relayed by host), Rollback (prediction, rewinds gathered every
  4 ticks, 48-tick prediction), transports.
- `Game/GameRunner.cs` the Unity side: modes (Menu/Lobby/Match/Online), camera, effects, replays, rink pictures,
  screenshot scenes. `Game/Views.cs` drawing (players, puck, ice, effects incl. `Ripple` distortion).
- `Game/LocalPlayers.cs` devices: keyboard + first controller share player 1 by default (`Slot.Extra`).
  `Game/Controls.cs` rebindable keys/buttons (Settings > Controls). Mouse aims for keyboard players in a match.
- `Game/TitleScreen.cs` the "press any button" screen the game starts on (drawing, and the game's name wherever the
  menus show it); it is `MainMenu.Page.Title`, left through `MainMenu.LeaveTitle()` (`LocalPlayers.AnyButton()`).
- `Game/PlayScreens.cs` local players screen (shared team view also used online), `Game/MainMenu.cs` menus + online
  lobby, `Game/Menus.cs` pause/settings/match settings, `Game/CardScreen.cs` 3D card pick, `Game/Hud.cs`.
- Sound: `Game/Sound.cs`. `SoundBank` makes every sound effect in code (`Sfx` enum; no sound files); `Sound` plays
  them and the music (`Sound.Play`, `Sound.Move/Step/Select/Back/Deny` for menus, `Sound.Charge`, `Sound.Music`).
  Music is files in `Resources/UltraPuck/Music/`: `MenuTheme.mp3` plays in the title screen, menus and lobbies;
  every song in `Music/Match/` is the match playlist (`Sound.Music(bool match)`: each match starts on a random song,
  the next starts when one ends, never the same twice in a row: `Playlist.Next`). Drop a song in either place and
  it's used, no code needed (`Editor/MusicImport.cs` sets the import options; strip cover pictures first with
  `ffmpeg -i in.mp3 -map 0:a -c:a copy out.mp3`). A file named after an effect in `Resources/UltraPuck/Sounds/`
  (e.g. `Goal.wav`) replaces the made-up one. Match sounds are worked out in `GameRunner.MatchSounds` from the state
  on screen (counters `Hits`, `ShotsFired`, a drop in `DashCharge`, the phase turning `Scored`), never inside Core.
  Volumes: `Settings.Volume` (whole game, starts at 30%), `MusicVolume`, `SoundVolume`, and
  `PauseMusicWhenUnfocused` (Settings > Sound tab). Pause menu quiets music to half unless the Music slider is
  focused (`Sound.QuietMusic` / `Sound.HearingMusic`).
- Shaders: `Resources/UltraPuck/{Card,Ripple,Ice}.shader` (`line` is a reserved word in HLSL).
- Maps: `Assets/StreamingAssets/Maps/*.json`, generated by `Tools/make_rink.py`, `make_hockey_rink.py`,
  `make_curved_rinks.py` (edit the script, regenerate; don't hand-edit). Cards: `StreamingAssets/Cards/ultrapuck.json`
  + art in `Cards/Art/<id>.jpg`. Every eligible card is offered as often as any other (tier is frame colour / bot
  preference only). S Rink nets stand on the ice; U Rink is upright (open at the top).
- Updater: `Game/Updater.cs` (Windows zip via PowerShell, Linux tar.gz via shell script).

## Handoff log (newest first; add an entry for every session)
- 0.1.30 (Composer, 19 Nov 2026): **online mouse aim, reticle options, menu mouse, freeplay rink**. (1) Online
  mouse aim used the local slot index as the match player — wrong when you join someone; now uses
  `NetSession.PlayerOf`. (2) Settings > Camera: reticle style / size / colour. (3) System cursor shows on menus and
  the card pick. (4) Menu clicks use `Ui.TakeClick` (more reliable). (5) Free play pause: pick a rink and Change
  rink. No rules change: **NetSession.Version still 23.**
  Files: `Game/GameRunner.cs`, `Settings.cs`, `Hud.cs`, `Menus.cs`, `Ui.cs`, `MainMenu.cs`, `PlayScreens.cs`; this file.
  Checked: 98 tests (93 pass, 5 skipped), Windows + Linux builds, GitHub release v0.1.30.
  **Not checked online by a person:** mouse aim when joining a host.
- 0.1.29 (Composer, 19 Nov 2026): **no clip outline** — carried puck against a wall still sits in the player, but
  draws on top of them (higher sort order) instead of a bright outline. No rules change: **NetSession.Version 23.**
  Files: `Game/Views.cs`; this file.
- 0.1.28 (Composer, 19 Nov 2026): **controls rebind, mouse aim, swap fix, wall-carry, no dash after quick shot,
  pad/KBM mode, slower title**. Mouse aim / reticle only in keyboard mode (last device flips pad ↔ KBM). Quick shot
  sets `SuppressDash` until Shoot is let go. Against a wall the carried puck pulls into the carrier. **NetSession.Version 23.**
  Files: `Game/Controls.cs`; Core + Game + tests; this file.
  Checked: 98 tests (93 pass, 5 skipped), Windows + Linux builds, GitHub release v0.1.28.
- After 0.1.27, not released (Composer, 19 Nov 2026): **swap fix, shorter aim line + slider, Controls rebind,
  mouse aim**. (1) Square / X (and the rebound Swap key, default F) now latches every frame like other buttons, so a
  short tap still swaps onto a bot teammate even between ticks; team comes from the live match state when available.
  (2) Shot aim line default length is 60% of the old one (`Settings.AimLine`, Camera tab slider). (3) New Settings >
  Controls tab: rebind shoot / charge / utility / swap / puck-back / move / aim (keys and pad; move/aim keys are
  keyboard-only). (4) Keyboard players aim with the mouse (right stick still wins if pushed); system cursor hides in
  a match and a crosshair reticle is drawn; mouse left/right also shoot / charge while playing. No rules change:
  **NetSession.Version still 22.**
  Files: new `Game/Controls.cs`; `Game/LocalPlayers.cs`, `Game/Settings.cs`, `Game/Views.cs`, `Game/Menus.cs`,
  `Game/Hud.cs`, `Game/GameRunner.cs` (mouse aim wiring, `settings-camera` + `settings-controls` shots); this file.
  Checked: 97 tests (92 pass, 5 skipped), Windows + Linux builds, screenshots (Controls tab, Camera aim-line
  slider at 60%, no left/right arrows on bind rows).
  **Not checked:** swap / rebind / mouse aim / reticle by a real person with a pad and keyboard.
- After 0.1.27, not released (Composer, 19 Nov 2026): **title screen entrance slower**. Letter drop, puck slide,
  colour lines and the "press any button" prompt all take longer (about half as fast again), so each beat is
  easier to see. Screenshot waits for `title` updated. Also fixed a compile error in unfinished `Game/Controls.cs`
  (pad-bind compare). No rules change: **NetSession.Version still 22.**
  Files: `Game/TitleScreen.cs`, `Game/MainMenu.cs` (ignore presses for 0.5 s), `Game/GameRunner.cs` (title shots),
  `Game/Controls.cs`, this file.
  Checked: Windows build, title screenshots (title-1 mid-entrance, title-2 settled with prompt).
  **Still open from earlier asks (not done this pass):** Square/X swap latch fix, aim line shorter + slider,
  controls rebind page, mouse aim with a reticle for keyboard.

- 0.1.27 (Composer, 19 Nov 2026): **equal card odds, bigger nets, S/U rinks, shot aim line, music pause rules,
  bot passes, Square/X swap, team-vote cursors**. (1) `CardLibrary.DrawOffer` picks uniformly (tier is label only).
  (2) Nets 30% bigger on every map (opening and depth inside the walls); S Rink nets stand on the ice with room
  behind; U Rink is an upright U (open at the top, nets up each arm facing down). (3) Carrier shows a short aim
  line out of the wedge. (4) Pause menu halves music unless you're on the Music slider; new Sound setting
  "Pause music when unfocused" (off by default so the next song still starts when one ends in the background).
  (5) Bots pass to open teammates ahead. (6) Square/X swaps you onto a bot teammate; a bot picking up the puck
  auto-swaps a random human on that team onto them. (7) Team Vote deals one shared draw and shows each voter's
  name under the card their cursor is on. **NetSession.Version 22.**
  Files: `Core/Cards.cs`, `Core/Bot.cs`, `Core/Simulation.cs` (pylons by facing), `Core/Net/NetSession.cs`;
  `Tools/make_*.py` + all four map JSONs; `Game/Views.cs`, `Game/Sound.cs`, `Game/Settings.cs`, `Game/Menus.cs`,
  `Game/LocalPlayers.cs`, `Game/GameRunner.cs`, `Game/CardScreen.cs`; tests; this file.
  Checked: 97 tests (92 pass, 5 skipped), Windows + Linux builds, map/goal/settings screenshots.
  **Not checked by ear:** music-at-half in the pause menu, next-song-while-unfocused. Team-vote cursors and
  Square/X swap need a person with a controller. Aim line looked at in match screenshots only briefly.

- After 0.1.25 / released as 0.1.26 (Claude, 18 Nov 2026, evening): **match music, title screen, two sounds
  redone**. (1) Four of the owner's songs are the match playlist, in `Resources/UltraPuck/Music/Match/` (Clipped
  Square Wave, Tense Arpeggio, Tense Arpeggio 2 [the owner's "song2.mp3", a second take with the same title], Tense
  Minor Pulse; cover pictures stripped). Every match starts a new random song, the next one starts when a song ends,
  never the same twice in a row; back in the menus the theme carries on. The `MatchTheme` idea from the entry below
  is gone: `Sound.Music(bool match)` now. (2) New "press any button" screen on start-up (`Game/TitleScreen.cs`,
  `MainMenu.Page.Title`): the name drops in letter by letter over the bots' match, a puck slides in, a red and a
  blue line shoot out, then the name flies to its place on the main menu. Only on a normal start (not the
  screenshot mode or the `ULTRAPUCK_DEMO/MENUTEST/CARDTEST` starts), and not when coming back to the menu later.
  (3) Dash sound is now a low cartoon whoosh (a band of wind sweeping up and down, `SoundBank.Wind`); taking a card
  is a lower, warmer chime with a soft knock. No rules change: **NetSession.Version still 21.**
  Files: `Game/Sound.cs`, new `Game/TitleScreen.cs`, `Game/MainMenu.cs`, `Game/LocalPlayers.cs` (AnyButton),
  `Game/GameRunner.cs` (title on start, `title` + `music` screenshot scenes, dash level), `Tests/SoundTests.cs`
  (playlist test), the four songs, this file.
  Checked: 84 tests (79 pass, 5 skipped benchmarks), Windows + Linux builds, full screenshot run (title pictures
  looked at; the log shows a new song starting each time one ends, never a repeat, and the theme resuming).
  **Not checked:** nobody has listened to anything (the two redone sounds are the third guess at what the owner
  wants; levels are `GameRunner.MatchSounds` for the dash and `CardScreen.Leave` for the card). The title screen
  was never left by a real button press by me, only by the screenshot scene calling `LeaveTitle()`. In one
  screenshot run the title was left by itself between two pictures: probably the owner touching the keyboard or
  mouse at that moment, but unproven; it now logs `Ultra Puck: title screen: a button on <device>` so a repeat can
  be traced. Songs ending was only checked by jumping to a second before the end, never by playing one through.
  Last two changes of the evening, at the owner's request: the "SKATE · CHECK · SCORE" line under the name on the
  title screen is gone (`TitleScreen.Draw`), and the goal explosion plays at 0.55 instead of full level
  (`GameRunner.MatchSounds`; the owner found it too loud). Checked after these two: 84 tests (79 pass, 5 skipped),
  Windows + Linux builds, title screenshots (the line is gone). The goal level was edited while that test run was
  already going, so the tests may have been compiled just before it (they don't touch that line); both builds were
  compiled after it. Nobody has heard the quieter goal yet.
- After 0.1.25, not released or committed (Claude, 18 Nov 2026): **sound**. The owner's song "Bit-Driven Groove" is
  the menu theme (`Resources/UltraPuck/Music/MenuTheme.mp3`, cover picture stripped, audio untouched): it plays in the
  menus and lobbies, fades out when a match starts and carries on from the same spot afterwards. Matches have no
  music yet (the owner will pick a match song: save it as `MatchTheme.mp3` in the same folder). Whole-game volume
  starts at 30%; new Settings > Sound tab (Volume, Music, Sound effects). Sound effects, all made in code: charging
  a shot (a hum that climbs), shot, dash, bump, heavy check, goal explosion, card dealt / picked / absorbed, menu
  move / choose / back / "can't do that". New state `PlayerState.ShotsFired` + `ShotStrength` (in the checksum) so
  a shot can be told from a puck knocked loose. **NetSession.Version 21.**
  Files: `Core/GameState.cs`, `Core/Simulation.cs` (Shoot), `Core/Net/NetSession.cs`; new `Game/Sound.cs`,
  `Editor/MusicImport.cs`, `Tests/SoundTests.cs`; `Game/Settings.cs`, `Menus.cs`, `MainMenu.cs`, `PlayScreens.cs`,
  `LocalPlayers.cs`, `CardScreen.cs`, `GameRunner.cs` (MatchSounds, music, `settings-sound` screenshots, sound lines
  in the log); `Tests/CoreTests.cs`, `Tests/UltraPuck.Tests.asmdef` (now also references UltraPuck.Game); this file.
  Checked: 83 tests (78 pass, 5 skipped benchmarks), Windows + Linux builds, screenshot run (log shows the theme
  playing/pausing/resuming and dash, shot, hit, goal, charge, card deal and absorb sounds going off).
  **Not checked: nobody has listened to any of it.** The effects were only looked at as numbers and spectrograms,
  so how they sound, and how loud they are next to each other and the music (levels are in `GameRunner.MatchSounds`,
  `Sound.Move/Select/...` and `Sound.MusicLevel`), needs the owner's ears. Menu sounds and the card-pick sound need
  a person pressing buttons, so they have only been compiled, never set off. Nothing was tested online or with a
  controller. Not done (not asked for): sounds for the countdown, puck hitting walls, Puck Gun, shockwave, game over.
  Later the same day, at the owner's request: the dash is now an airy swoosh (breathy hiss only, no thump, played
  quieter: 0.42 for your own, 0.28 for others) and taking a card is one short, soft chime instead of four bell
  notes (0.42 s, played at 0.55). Only `Game/Sound.cs` (`SoundBank.Render`: Dash, CardPick, new `Swell` helper),
  the dash level in `GameRunner.MatchSounds` and the pick level in `CardScreen.Leave`; no rules change, Version
  still 21. Tests, both builds and a cards + maps screenshot run redone; still not listened to by anyone.
  Noticed, not changed: in `GameRunner.Show` the goal explosion picture is started by comparing the last two ticks,
  so on a frame that runs two or more ticks (below about 120 fps) a goal could be missed; the goal sound uses its
  own check and isn't affected.
- 0.1.25 (Claude): card offers >3 fan in an arc; online host's pause freezes everyone (Pause input bit); pause menu
  Match settings page (rink, rules, restart). NetSession.Version 20.
- Earlier (Claude): rollback perf (wall bounding-box skips, gathered rewinds), wedge pickups, pass boost, fair
  loose-puck pushes, own-net carry rule, 3 s countdown, deeper nets, S/U rinks, Linux build + auto-update, shared
  keyboard/controller, ice shader, goal shockwave, hold-to-dash, quicker acceleration.
