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
   (also `BuildLinux`). Close the Unity editor first if it has the project open.    Owner shortcut: double-click
   `Build Windows.bat` (or `powershell -File Tools/build-windows.ps1`) after editing cards/maps so the built game
   picks up `StreamingAssets`. For face text / art / maps only, `Sync Cards.bat` copies StreamingAssets without a
   Unity compile. Card `face` rows are display only — Shockwave slow/range/duration/cooldown in play come from
   `Core/Tuning.cs` (must match the face, then full build).
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
6. **Don't commit or push unless asked.** Releases: double-click `Release.bat` (asks what changed, bumps the
   version, builds Windows + Linux, publishes a GitHub release, commits and pushes), or
   `powershell -File Tools/release.ps1 -Bump -Notes "<plain-English notes>"` then `git add -A`, commit
   (`0.1.N: summary`), push.
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
- Button pictures: Kenney Input Prompts (CC0) in `StreamingAssets/InputPrompts/` (`Xbox`, `PlayStation`, `Switch`,
  `Keyboard`, `Generic`). Hints (`Glyphs`) and utility card footnotes (`Press {utility} to activate`) load PNGs via
  `InputIcons` for the last device used. Empty Kenney placeholder folders are not used — drop packs here.
- Updater: `Game/Updater.cs` (Windows zip via PowerShell, Linux tar.gz via shell script).

## Handoff log (newest first; add an entry for every session)
- 0.1.42 (Claude, 21 Nov 2026): **online match options, Same Draw, everyone's cursors.**
  The online lobby's Options (host) has every match setting: Match mode (Score / Time), Score to win 1-10 or Time
  1:00-10:00 (`MainMenu.Row.LabelNow`), Rink, Rounds, Replays, Picking, Cards offered, Pick time, Multiple
  utilities, Bots; the panel is capped to the screen and scrolls with a scroll bar (mouse wheel too). The settings /
  pause Match settings pages draw a scroll bar when rows don't fit. Picking is now `Settings.PickMode` (0 Each player,
  1 Same draw, 2 Team vote; old `team_vote` pref migrates); rules byte 0 bit 16 = `Tuning.SameDraw`: a team shares one
  draw (like Team Vote) but each takes their own card. The card screen treats Same Draw like Team Vote: cursors for
  everyone on the team with the same cards (local, online, bots; `FollowTeammates`), held until the whole team has
  chosen, a green border round a locked cursor; the other team watches all their cursors. Free play's card editor
  scrolls one row further on the last row (`EditMaxTop`). Stone Wall: up to 30 long / 3 s (was 10 / 1 s). New shot
  scene `samedraw`. **NetSession.Version 49.** Checked: 143 tests (138 pass, 5 skipped), Windows build, online /
  samedraw / pausemenu shots.
- 0.1.42 (Claude, 21 Nov 2026): **name tags fade; Swap rock, Ice Trail, Team Play, Cannon
  Ball changes; Stone Wall card.** Name tags fade to 0.22 while another player or the puck is under them (`Hud.Under`;
  a hidden Invisible Shot puck doesn't count). Swap rock: a loose rock slower than `RockStopSpeed` (0.5) is removed
  (after pick-ups, so a still rock can still be scooped up); no swapping to a rock someone holds. Ice Trail: every shot
  lays it; teammates on it are sped up as much as opponents are slowed (`PlayerState.Fast`/`FastTime`, the orange
  boost ring); `IceLife` 9, `IceMeltPace` 4 (3x longer). The card face said -33% but the code was and is 10% a copy.
  Team Play is now `PassSpeed`: a clean pass gives passer and receiver +80% speed for 3 s (old PassRefresh stats
  still work for mods). Cannon Ball: `ShovedBy` / `CannonFrom`; a carrier still being shoved (`Shoved` > 0) whose
  puck or body touches their own goal box scores for the shooter (`Goal()` split out of `CheckGoal`). Stone Wall
  (`ultrapuck.stonewall`, art from the owner): a full charge raises `GameState.Stones` pieces 0.9 behind the puck
  along its path (max 10 long / 1 s, stops when the puck turns, is caught, or would cross a wall); each piece stands
  4 s; players, puck and rocks bump it (`CollideStones`); `StoneViews` draws chunky stones and grit when it crumbles.
  New shot scene `stone`. **NetSession.Version 48.** Checked: 142 tests (137 pass, 5 skipped), Windows build,
  stone / cards shots.
- 0.1.42 (Claude, 21 Nov 2026): **online sends at a fixed rate, not every frame.** With 6
  players at ~590 fps the host sent ~590 input messages a second to each PC (each up to 60 ticks per player), Steam
  queued it (2.2 s ping, confirmed stuck 49 ticks back) and everyone saw "Waiting for the other players" in slow
  motion; capping fps at 120 fixed it. Now `NetSession.SendRate` = 120 a second whatever the frame rate; each message
  carries a player's newest `ResendTicks` (16) ticks, and older unconfirmed ones go again every `RepairEvery` (0.2 s).
  Steam SendRateMin/Max raised to 256 KB/s / 1 MB/s. The 10 s net log line adds messages a second and KB/s sent.
  Same messages, so no version bump. Checked: 134 tests (129 pass, 5 skipped), incl. 6 PCs at 600 fps over a laggy
  network (128 messages/s, 74 KB/s per PC, no waits, identical games).
- 0.1.41 (Claude, 21 Nov 2026): **Cannon Ball really shoves; Invisible Shot; darker lines.**
  Cannon Ball: the catcher's speed along the shot is replaced by `CannonCarry` (0.8) × the puck's speed (by weight /
  Knockback Taken), sideways speed kept, and `PlayerState.Shoved` (`CannonTime` 0.5 s) gives toss-like low drag and
  stun-level control so a skating bot can't just cancel it. No hit lag, no hit flash. Card face "Knock Back +100%".
  "Invisible Quick Shot" renamed "Invisible Shot" (the title didn't fit). Rink side lines darker (0.3 of the team
  colour). **NetSession.Version 47.** Checked: 133 tests (128 pass, 5 skipped), Windows build, cards / maps shots.
- 0.1.40 (Claude, 21 Nov 2026): **Power Shot = full charge; Invisible Quick Shot; Cannon Ball;
  menu strip gone; team-coloured lines.** Power Shot (max 1): `Stats.QuickShotFull` makes a quick shot
  `ChargedShotMax` (Over Charge doesn't add; `QuickShotSpeed()` used for puck, rock and teammate throws). New cards
  `ultrapuck.ghostshot` (GhostShot: a quick shot sets `GameState.PuckHidden` 1.5 s / `PuckHiddenTeam`; ends on any
  pickup; `PuckView.Ghost` draws an outline for that team, nothing for a screen with only the other team, outline in
  replays) and `ultrapuck.cannonball` (CannonBall: a released full charge sets `CannonTeam`; an opponent catching it
  while it's faster than a quick shot is carried along by `CannonCarry` × the puck's speed, scaled by weight / Knockback
  Taken). Art from the user's desktop. Main menu: the 120 px see-through strip beside the panel is gone. Rink side lines
  are a dark shade of the team defending that end (centre line grey). **NetSession.Version 46.** Checked: 133 tests
  (128 pass, 5 skipped), Windows build, title / cards / maps screenshots. Not looked at: the invisible-puck outline in play.
- 0.1.39 (Claude, 21 Nov 2026): **round posts; Big Rink.** Open walls (the nets' posts) are
  drawn with round joints and ends like the outer walls (`Draw.Line` no longer gets `square` for them; 20-segment
  caps), matching the round collision they always had. New map `big-rink.json` ("Big Rink"): the Hockey Rink 70%
  bigger every way (110.2 x 45.9), nets the same size, four spawns a side; made by `Tools/make_hockey_rink.py out --big`
  (without `--big` the script still makes the Hockey Rink exactly). No rule change (the map fingerprint covers online).
  Checked: 130 tests (125 pass, 5 skipped), Windows build, `maps` screenshots.
- 0.1.38 (Claude, 21 Nov 2026): **Resolution setting; Friendly Fire text; bots throw
  teammates; Swap spam fix.** Graphics tab "Resolution" (`Settings.Resolution`, PlayerPrefs res_w / res_h, 0 = the
  monitor's own; `Settings.Resolutions()` lists the monitor's sizes). Friendly Fire card: plain text "Allows you to pick
  up and shoot teammates as if they were the puck." (the face entry with a "text" key wasn't read by the card loader).
  Bots carrying a teammate aim at the puck (the net if the carried one has it) and fire at once. A person's own player
  run by the bot while they steer a bot teammate now waits `SwapBotCooldown` between Swap uses (it was spamming
  teleports). **NetSession.Version 45.** Checked: 130 tests (125 pass, 5 skipped), Windows build, settings-graphics shot.
- 0.1.37 part 2 (Claude, 21 Nov 2026): **countdown sounds + START, leave/rejoin mid-match,
  host kick-off / score, hold-for-scoreboard.** Countdown beeps (Sfx.CountBeep / CountGo) and a START! banner; after a
  card pick the countdown waits `Tuning.PickOutroTime` (1.5 s, in the sim) so the cards finish leaving before 3.
  Online leave: the host marks the seat Away and `Rollback.SetAway(p, from, until)` has the bot play it from the first
  tick without their controls (`Msg.Away` keeps everyone told). Coming back: Hello is accepted mid-match when a seat is
  away; the host gives the seat back (same name first) and sends `Msg.Rejoin` with the game at a settled tick
  (`StateCodec`: reflection over every GameState field) and the away spans; the new `Rollback` starts at that tick.
  Cards and stats stay with the seat. NetTests.ThreePcsThroughTheHost now checks leave, bots, rejoin and identical
  games. Host commands in Pick 203-207 (kick-off, red/blue score up/down) from Match settings. View/Tab held: a
  scoreboard (score, cards, stats); no longer toggles the corner list. Replay goal banner sized to the text; the
  "Replaces your …" line sits under the pick timer; the players-screen Continue chip stays grey.
  **NetSession.Version 44.** Checked: 128 tests (123 pass, 5 skipped), Windows build, screenshots (countdown, start,
  scoreboard, match-settings, replay, players). Not checked: online with real PCs, Linux build.
- 0.1.37 part 1 (Claude, 21 Nov 2026): **online freeze fix, swapping in the sim, mid-match bots, match stats.**
  Freeze: a controller that dropped out and came back left its slot holding a dead InputDevice; reading it threw every
  frame and stopped `OnlineUpdate` before `s.Step`, so that PC never sent controls and everyone else sat on "Waiting for
  the other players" (the user's Player.log: 291k such exceptions, 434 MB). Now `LocalPlayers.Live()` guards every read,
  `DeviceAdded` hands a returning pad back to its slot, and the online sample is wrapped in try/catch. Net: lobby
  messages older than the newest are ignored and an old "in the lobby" can't knock a PC out of a newer match; the host
  resends Start to everyone once a second while playing. Swapping onto bots is now part of the game:
  `Simulation.Route` (call before Step) uses `PlayerState.Pilot`, input bits `Swap = 64` / `NoAutoSwap = 128`; auto-swap
  to a bot that picks up the puck (Controls "Auto swap to bots"); back home on the card screen; works online. The old
  slot-swapping in LocalPlayers is gone. Host adds bots mid-match from Match settings (online via the host's
  `Pick = AddBotRed/AddBotBlue` outside picking). Stats: Goals / Assists / Saves / Bumps per player, `LastGoalBy` /
  `LastAssist`; game-over table with MVP and "Useless player"; goal and assist on the goal / replay screen. Puck Gun:
  check-like knockback by weight, no stun, 1 s, `noBots` card flag (Team Vote: people draw first). Charging with the
  puck 30% slower; puck drag 7/11 (10% more glide). Bots: shoot at an open part of the net, pass when it's covered and a
  mate is open with a clear lane, get open beside the net, push up for loose pucks. Swap rock line only for its owner.
  Players screens bold without shadows (`Ui.Flat`), leaving bar top left, `Toasts` (top right) for leave messages.
  **NetSession.Version 43.** Checked: 126 tests (121 pass, 5 skipped), Windows build, screenshots (gameover,
  match-settings, toast-leaving, replay, players). Not checked: online with real PCs, a real controller reconnect, Linux build.
- 0.1.36 (Claude, 21 Nov 2026): **Controls tab button pictures; Pylon Goalie 55%.** Controls rebind rows show
  only the device in use (InputMode.Current): Kenney key art on keyboard, that pad's buttons on a controller
  (`Glyphs.BindingHint`, hint tokens {bindkey:N} / {bindpad:N} / {bindpad2:N}; move / aim rows show the sticks
  on a pad). New PadButton LT / RT with Kenney trigger art. Key pictures are drawn cropped to their drawn part
  (`InputIcons.Used`) so Space / Shift are readable (footer hints too). `InputMode.ShotOverride` for screenshots
  (settings-controls, -ps, -xbox). Pylon Goalie 55% bigger per card (card json had lost its stats line: restored).
  **NetSession.Version 42.** Checked: 119 tests (114 pass, 5 skipped), Windows + Linux builds, screenshots. GitHub release v0.1.36.
- 0.1.35 (Claude, 21 Nov 2026, part 2): **pre-charge; dash where you move; back home for cards; one Team Play.**
  Holding charge/brake without the puck charges a shot (still brakes); puck/rock/teammate pickups keep the charge,
  so a pass can be one-timed by letting go. Controls tab (top row) "Dash direction" (Settings.DashWhereMoving, default
  Where you move) sends input bit `PlayerInput.DashMove` (32); a check then dashes along the move stick, or the
  facing when the stick is centred. `PlayerState.DashDir` (checksummed) records the dash's way so the dash lines
  (Views Status dashDir) follow it, not the facing; screenshot scene `dash`. `LocalPlayers.ReturnHome()` (Slot.Home tracks whose player a swapped human
  really is) runs every tick of Phase.Picking, so nobody picks cards while driving a bot. Team Play now refreshes
  the whole team (tier S); Team Play II card + art removed. NetSession.Version 41. GitHub release v0.1.35.
  Checked: 119 tests (114 pass, 5 skipped).
- 0.1.35 (Claude, 21 Nov 2026, part 1): **Friendly Fire carry/throw; Shockwave knocks the puck loose; one growing
  Pylon Goalie kept out of the nets.** Friendly Fire no longer boosts: a player with the card picks up a teammate who
  touches their wedge, like the puck (`GrabTouching` with `OnWedge(p, at, r)`, slack `MateGrabReach`; a check
  on the wedge works too; body/back contact doesn't). State
  `PlayerState.HeldBy`/`HeldTime`; `CanGrab`/`GrabMate`/`CarryMates`/`ThrowMate`/`DropMate` in Simulation. The carried one rides in front facing the same way and keeps the puck; shoot/charge throws them
  (shot speed × `MateThrow`, max `MateThrowMax`), auto-throw after `MateHoldMax` 3 s. Thrown = `TossTime` 0.75 s
  of no skating, low drag, and it counts as a check (DashTime) on opponents; on teammates just a bump. A real
  check / Shockwave frees them. Holders can't pick up the puck/rocks, steal or Swap. Shockwave (`BlastFrom`
  knockLoose) knocks the puck off any carrier it reaches. Pylon Goalie: one per team, `Pylon.Radius` = 0.5 ×
  1.35^(cards−1) (cap 4 cards), heavier when bigger; `KeepPylonOut` stops it entering any goal box. Views scale
  pylons; no ring on carried/thrown players (the old boost ring is never shown now). Card texts updated. New screenshot scene
  `carry` (+ `_shotNoInput`). **NetSession.Version 41.**
  Files: Core (Simulation, GameState, Tuning, NetSession), Game (GameRunner, Views), cards json, tests; this file.
  Checked: 118 tests (113 pass, 5 skipped), Windows + Linux builds, carry screenshots. Released in v0.1.35.
- 0.1.34 (Composer, 21 Nov 2026): **team-vote bot cursors; swap/puck; bot swap; rock line; goal opening;
  dash coast; match songs.** Team-vote bots hover-follow without locking until a human locks (online watchers
  see cursors move). No Swap while holding the puck. Square/X (+ F) and auto-swap fixed. Camera "Swap rock
  line" + Graphics Show FPS. Softer goal zoom. Goal boxes at the net opening. Dash CD 1.65 s + coast.
  Three match songs (Determination, One last fight, Stand your ground). **NetSession.Version 40.**
  GitHub release v0.1.34.
  Files: Core + Game + maps + music + tests; Release.bat; this file.
  Checked: 113 tests (108 pass, 5 skipped), Windows + Linux builds, release published.
- 0.1.33 (Composer, 20 Nov 2026): **scorer cam follow; lobby rename off; goal blast + Shockwave rocks.**
  Goal zoom centres and follows the scorer only (snap then stick). Lobby name keyboard disabled (hints too).
  Goal explosion shove + LastGoalPlayer; Shockwave shoves puck/rocks; 3 s before replay; thinner horizontal wipe.
  **NetSession.Version 37.** GitHub release v0.1.33.
  Files: Core + Game (camera, lobby, wipe) + tests; this file.
  Checked: 111 tests (106 pass, 5 skipped), Windows + Linux builds, release published.
- After 0.1.32, not released (Composer, 20 Nov 2026): **goal blast + scorer zoom; thinner wipe; Shockwave
  shoves puck/rocks.** Goal explosion shoves everyone (and loose Swap rocks) away from the net; `LastGoalPlayer`
  remembered. Camera zooms on the scorer for 3 s before the replay wipe (`ReplaySkipFrom` 3 s). Horizontal wipe
  uses thinner stripes (7). Shockwave also shoves the loose puck and loose rocks. **NetSession.Version 37.**
  Files: `Core/Simulation.cs`, `GameState.cs`, `Tuning.cs`, `Net/NetSession.cs`; `Game/GameRunner.cs`,
  `ReplayTransition.cs`; tests; this file.
  Checked: 111 tests (106 pass, 5 skipped), Windows build.
- After 0.1.32, not released (Composer, 20 Nov 2026): **card absorb on team vote; bot-alone team picks.**
  Team Vote leave dives the locked card into the player again (absorb + ripple). Bots with no human teammate
  pick by tier so 1v1 isn't stuck waiting. **NetSession.Version 36.**
  Files: `Game/CardScreen.cs`; `Core/Bot.cs`, `Net/NetSession.cs`; `Tests/CardMechanicsTests.cs`; this file.
  Checked: 109 tests (104 pass, 5 skipped), Windows build.
- 0.1.32 (Composer, 20 Nov 2026): **team-vote bots follow humans; grey replay wipe; rock = owner look.**
  Team Vote tallies humans only (bots copy majority hover/pick). Replay blinds are grey / dark grey. Swap rocks
  show the thrower's symbol on a darker shade of their team colour. **NetSession.Version 35.** GitHub release
  v0.1.32.
  Files: `Core/Bot.cs`, `Simulation.cs`, `Net/NetSession.cs`; `Game/ReplayTransition.cs`, `Views.cs`, `GameRunner.cs`;
  `Tests/CardMechanicsTests.cs`; this file.
  Checked: 108 tests (103 pass, 5 skipped), Windows + Linux builds, release published.
- 0.1.31 (Composer, 20 Nov 2026): **Swap card, one-utility rule, Shockwave remake, Kenney prompts.** New Swap
  utility (throw rock / teleport with momentum + clap reverb; rocks pickable, shootable, bounce off puck). One
  utility at a time (match setting for multiple; hover replace hint). Shockwave: push everyone, 2× shove, ring FX.
  Kenney button icons; utility cards show Press {utility}. Free-play rink auto-applies; Sync Cards / Build Windows
  bats. **NetSession.Version 34.** GitHub release v0.1.31.
  Checked: 107 tests (102 pass, 5 skipped), Windows + Linux builds, release published.
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
