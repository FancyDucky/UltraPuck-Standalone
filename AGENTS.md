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
   `ULTRAPUCK_SHOTS_ONLY=title,music,players,online,ripple,replay,maps,agent,ice,clip,pairing,countdown,goal,cards,pausemenu,settings`
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
- Shaders: `Resources/UltraPuck/{Card,Ripple,Ice,ChaosGlitch}.shader` (`line` and `half` are reserved words in HLSL).
- Maps: `Assets/StreamingAssets/Maps/*.json`, generated by `Tools/make_rink.py`, `make_hockey_rink.py`,
  `make_curved_rinks.py` (edit the script, regenerate; don't hand-edit). Cards: `StreamingAssets/Cards/ultrapuck.json`
  + art in `Cards/Art/<id>.jpg` (optional `"finish"`, looks only: `CardDef.FinishNames`). Every eligible card is offered as often as any other (tier is frame colour / bot
  preference only). S Rink nets stand on the ice; U Rink is upright (open at the top).
  Card editor: `Card Editor.bat` (`Tools/card_editor.py` + `card_editor.html`, a local page on 127.0.0.1:8765).
  Arena maker: `Tools/arena_maker.html` (open it in a browser, no server): paint the four arena layers from a tileset on each rink's
  canvas grid and export the Custom_Arenas zip. Its rink walls come from the map files: `python Tools/arena_maker_rinks.py` after changing a rink.
  Its stat and finish dropdowns read `StatNames` / `CardDef.FinishNames` from `Core/Cards.cs`, so new ones show up by themselves; new card
  keys need adding to `ORDER` / the form in the html (unknown keys are kept as they are).
- Button pictures: Kenney Input Prompts (CC0) in `StreamingAssets/InputPrompts/` (`Xbox`, `PlayStation`, `Switch`,
  `Keyboard`, `Generic`). Hints (`Glyphs`) and utility card footnotes (`Press {utility} to activate`) load PNGs via
  `InputIcons` for the last device used. Empty Kenney placeholder folders are not used — drop packs here.
- Updater: `Game/Updater.cs` (Windows zip via PowerShell, Linux tar.gz via shell script).

## Handoff log (newest first; add an entry for every session)
- unreleased (Claude, 28 Nov 2026): **Head Start hands out the extra pucks too, protocol 97.** `Simulation.HeadStart`: after
  the puck goes to the nearest on the Head Start team, each extra puck (More Pucks) goes to the next nearest teammate
  (same order: Double Agents last, nobody raging); with nobody left to hold one it waits loose, still, just beside the
  player with the puck. Test `HeadStartGivesTheExtraPucksToo`. 301 tests.
- unreleased (Claude, 28 Nov 2026): **a chaos card is once a match, protocol 96.** `CardLibrary.ChaosRoll` leaves out
  every chaos card anyone in the match already holds (they change the arena for everyone, so a second copy did nothing
  for Giant Nets and doubled More Pucks' puck): once taken, only the other chaos cards can still fly in; with all taken,
  none. Checked in `ChaosCardsComeOnTopOfAHandAboutOneTimeInSix`.
- unreleased (Claude, 28 Nov 2026): **anyone can hold Start to end a card pick; bots can go after someone joins; the host
  can remove people.** `GameRunner.HostEndPick`: online, everyone in the match (not just the host) can hold the menu
  button; a client sends `PlayerInput.EndPick` with its own controls (the sim already ends the pick on anyone's).
  Removing a bot online: the quick way (`OnlineBotToRemove`, with the host's controls) only works for a bot with
  nothing but bots after it; otherwise `NetSession.BotLeavesAtReseat(team)` takes the team's last bot out at the next
  re-seat (handed to every PC, like a join), so "remove bot" works after someone joins part-way. Kicking:
  `NetSession.Kickable()` / `Kick(peer)`: the PC is told ("The host removed you from the game.", via Refuse) and
  dropped; mid-match a bot plays their players until the re-seat takes their seats out (`_kickedPeers`); their Steam
  accounts are turned away for the rest of the session (`_kickedAccounts`; by IP they could rejoin). Menu row
  "Remove a player" (press twice) in the lobby's host options and the pause menu's match settings
  (`MainMenu.KickRow`). Test `TheHostTakesOutABotBeforeSomeoneWhoJoinedAndRemovesThem`. No rules or message changes,
  protocol unchanged. 300 tests.
- 0.1.77 (Claude, 28 Nov 2026): **released** (chaos cards, Giant Nets, More Pucks, corner drop, pure black,
  card mouse-leave, arena maker / custom arenas). Chaos card sound defaults to H, the short stinger
  (`Settings.ChaosSound` 8). Release.ps1's `gh release create` failed on the notes (Windows PowerShell 5.1 splits a
  native argument at double quotes): published by hand with `--notes-file`; keep quotes out of `-Notes` or pass a file.
- unreleased (Claude, 27 Nov 2026): **chaos card drops in from the top; four more chaos sounds like C.** The corner
  chaos card now starts straight above its spot (off the top of the screen) and drops down into the top-right corner
  (`CardScreen.Deal`). New sounds in the style of C (whoosh into a detuned stinger that winds down like a tape
  stopping): `Sfx.ChaosE` deeper and longer, `F` the stinger stuttering as it winds down, `G` sharper and dissonant with
  a shimmer, `H` short and punchy; Settings > Sound > "Chaos card" lists all eight. Screenshot `chaos-drop` catches it
  on the way down. No rules change.
- unreleased (Claude, 27 Nov 2026): **chaos cards fly in from the corner, protocol 95.** Chaos cards are never in a
  normal draw any more (`CardLibrary.DrawOffer` skips them, so hands, New Hand, Choose's deck and Double Card never
  give one). Instead one pick in `Tuning.ChaosOdds` (6) rolls (`CardLibrary.ChaosRoll`, `s.NextRandom()`, only if
  chaos cards exist) and adds one at the END of the offer (a shared hand's for the whole team); picking works as for
  any card. New Hand keeps it. `CardScreen.Deal`: a chaos card last in an offer isn't in the row / fan; it flies in
  from off the top-right corner after the hand (`CardView.Corner`, smaller: `CornerSize` 0.68), playing
  `Sound.Chaos()`. Left / right steps onto it after the right-most card. Teammates' picks show on the left, so the
  right; the other team's minis (right edge, when both teams pick) can sit under it. Four sounds to choose from:
  `Sfx.ChaosA` glitch zap, `B` corrupted chime, `C` whoosh + stinger, `D` error bleeps; Settings > Sound >
  "Chaos card" (`Settings.ChaosSound`, default A, plays as you pick). Test
  `ChaosCardsComeOnTopOfAHandAboutOneTimeInSix`; `NewHandRerollsOncePerPick` and `TheTeamThatConcededPicksACard` use
  `ChaosOdds = 0` (they're about the plain hand). Screenshot scene `chaos` deals three and Giant Nets. 299 tests.
- unreleased (Claude, 27 Nov 2026): **a card the mouse leaves goes back to its own size.** Every card screen (pick,
  team vote, free play's editor, the card list): when the mouse has moved and is no longer over the card it raised,
  that card drops back (`CardScreen.MouseOver` / `MouseLeft`, `_mouseOff` for this screen only), but stays the one
  Enter / a button acts on. Moving with the keyboard or a controller, or the mouse coming back, raises it again; a
  new screen starts raised. Display only, no rules change.
- unreleased (Claude, 27 Nov 2026): **chaos glitch over the text (picture only); pure black card setting.** The
  chaos overlay is now drawn over the text (order 6). In the text area it only draws the picture's own pixels: some
  twitch frames the picture slips down 0.05-0.22 (`drop`) over the top of the title, and torn bands of the picture
  can land there; blocks, ghosts, frame and card-stock slices still never cover text. The steady aberration also
  shows in the text area right by the frame (`byFrame`), so the fringe runs all the way round (it was cut off by the
  text area). New card key `"pureBlack": true` (`CardDef.PureBlack`, card editor checkbox "Pure black (OLED)"): the
  body is #000 with no glare, sparkle or tier sheen on it (a card's own "finish" still shows); chaos cards' torn
  slices use black too. Screenshot scene `chaos` turns it on for More Pucks for that run only. No rules change.
- unreleased (Claude, 27 Nov 2026): **More Pucks chaos card, protocol 94.** `ultrapuck.morepucks` (B, max 1, white,
  owner's art), stat `ExtraPucks`: one extra puck per copy anyone holds (at most 4), placed still beside the kick-off
  spot at every face-off (across the ice, alternating sides: `Simulation.SyncExtraPucks`; free play re-syncs after a
  pick and on Reset puck). An extra puck is a `Rock` with `Puck` set, so it reuses the rocks' carrying, shooting,
  pick-up, walls, portals, Shockwave and puck bumps, with the puck's radius / weight / drag (`RockR` / `RockW` /
  `RockDragOf`), Owner -1, no running out. In a net it's a goal (safe in a defender's wedge like the puck); it and the
  puck trade places first, so the blast, sound, camera and replay happen where it went in; the goal is `Rock.Touch`'s
  (last carrier) if on the scoring team, else nobody's. Bots only go for it when it lands in their wedge (then they
  shoot it at the net, like a rock). Drawn as a puck (`RockViews`). Test `MorePucksPutsAnExtraPuckOnTheIceThatScores`;
  screenshot `chaos-pucks`. Checked: 298 tests, Windows build.
- unreleased (Claude, 27 Nov 2026): **Giant Nets: white accent, strong chromatic aberration.** Card colour `#FFFFFF`.
  `ChaosGlitch.shader` now always draws a steady colour split (`_Aberration`, 0.05 card units: red from one side,
  blue from the other): red / blue ghosts of the frame past the border, coloured fringes on its edges, the picture
  split into its colours (drawn over the picture, so it doesn't slide with the tilt). Steady, no flicker; the text
  area is still left alone. Twitches go on top with a wider split (1.5x + up to 0.1). Card editor preview: red / blue
  box-shadows on the frame and picture.
- unreleased (Claude, 27 Nov 2026): **chaos glitch is an occasional twitch; 2x Goal Size renamed Giant Nets.**
  The glitch (overlay and the picture's tear) now only plays in twitches of 0.5-2 s, about every 4.5 s with some
  skipped and at least 0.8 s calm between (`Twitch(t, seed)`, the same in `ChaosGlitch.shader` and `Card.shader`;
  `_GlitchTwitch` 1 on chaos cards' picture, 0 = the "glitch" finish runs all the time as before). Calm the rest of
  the time (about 75%); a twitch stutters (some of its frames hold still); the 2.7 s big burst is gone. Card editor
  preview twitches likewise. Giant Nets has the owner's art (`Cards/Art/ultrapuck.widenets.jpg`; id and stat
  unchanged, protocol stays 93). Screenshot scene `chaos` takes 8 pick shots 0.5 s apart. Also fixes the previous
  commit, which left `RinkNets(b)` after the end of GameRunner.cs (a staging slip; it didn't compile).
- unreleased (Claude, 27 Nov 2026): **chaos cards, 2x Goal Size, protocol 93.** Card file `"chaos": true`
  (`CardDef.Chaos`; card editor checkbox "Chaos card", previewed there with a CSS glitch): a card that changes the
  arena for everyone. Its face glitches out (`CardScreen.ChaosGlitch`, `Resources/UltraPuck/ChaosGlitch.shader`): a
  quad 3.6 x 3.9 over the 2.5 x 3.5 card (order 2: over the picture and frame, under text boxes / outlines / text)
  draws torn horizontal slices of the card with red / blue split apart, red and cyan ghost edges, noise blocks, scan
  lines, static and a flicker, 15 glitch frames a second with a big burst every ~2.7 s, spilling past the border. It
  never draws in `_TextRect` (just under the picture to the frame's inner edge), so the text stays clean. With no
  finish of its own, the picture also gets the "glitch" finish, in step (`Card.shader` `_GlitchSeed`, 0 for every
  other card). Off with Settings > Card effects. First chaos card **2x Goal Size** (`ultrapuck.widenets`, stat
  `WideNets`, B, max 1, no art yet: in `CardTests.AwaitingArt`): while anyone holds it every net is twice as wide.
  `MapDef.WithWideNets(2)` copies the map with each goal box grown across its mouth about its middle and the wall
  points within 0.6 of it (the net's sides, or the notch in the boards on the Test Rink) moved out with its sides.
  `Simulation.Map` is now a property: `BaseMap`, or `WideMap` (made once) when `Simulation.WideNets(s)`; every public
  call that takes a state switches first (`Use(s)`), so rollback re-sims see the right nets. `GameRunner.RinkNets`
  draws the rink again when `_sim.MapFor(state)` changes (`_rinkDrawn`). Custom arena pictures aren't redrawn (the
  wall lines are). Screenshot scene `chaos` (pick at three moments, editor grid, nets before / after; borrows the
  Portal Gun picture for the run). Test `TwoTimesGoalSizeMakesEveryNetTwiceAsWide`. Checked: 297 tests, Windows build.
- unreleased (Claude, 27 Nov 2026): **rink maker kept out of releases** (the user wants to finish it first).
  `GameRunner.RinkMakerOn`: on in the Unity editor, screenshot runs, `-rinkmaker` / `ULTRAPUCK_RINKMAKER=1`; off,
  no Custom rinks on the main menu, no LB / RB on the rink screen, no custom rinks loaded. Desktop shortcut
  "Ultra Puck (rink maker)" passes `-rinkmaker`. `release.ps1` leaves `Custom_Rinks` out of the zip. Don't turn it
  on for everyone until the user says so.
- unreleased (Claude, 27 Nov 2026): **rink maker, Custom Rinks.** Main menu "Custom rinks" opens a grid
  (`Game/RinkMaker.cs`, state Library): + makes a new rink, a rink opens in the maker, X / Del deletes (press twice).
  The rink screen's LB / RB (Q / E) opens the same grid to pick one to play (`PlayScreens.Page.Custom`, finished rinks
  only). The maker (GameRunner `Mode.Editor`, camera framed by `RinkMaker.Frame`) paints ice on a 100 x 56 grid of
  1-unit squares, places the red / blue nets (mouth on a grid corner, faces the middle; again on the same spot turns
  it) and the puck spot, mirror on by default, undo / redo, works with a controller cursor and with mouse + keyboard;
  Test plays the rink in free play vs a bot (`TestRink`, a temporary last map; the menu button comes back).
  `Core/RinkPlan.cs` turns the grid into an ordinary map file: only the biggest 4-connected patch of ice counts (the rest
  shown red), the outline is traced round it, staircases straightened (`Unstep`), corners rounded (radius 2.5), holes
  become pillars (closed walls after the outline; `RinkView` fills them with the background colour), spawns placed like
  the Hockey Rink's, a bot route on bent rinks, and the grid kept in an "editor" block to open it again. Unfinished rinks
  save as just the editor block. Files live in `Custom_Rinks` next to the exe (`ULTRAPUCK_RINK_DIR` overrides it, for
  screenshots). Custom maps sit after the game's own in `_maps` (`_builtinMaps`): left out of the fingerprint, `Rinks()`,
  online rink stepping and the host's map (hosting switches to the Hockey Rink). Screenshot scene `rinkmaker`, tests
  `RinkPlanTests`. Protocol unchanged.
  Wall part: thin wall lines on grid edges (`RinkPlan.SetWall`, across / up), drawn by dragging corner to corner;
  only where there's ice both sides and no net; saved as "across" / "up" run lengths in the editor block, written as
  straight open walls (`WallRuns`); spawns keep off them, the bots' route goes round them, and a rink whose walls cut
  one net off from the other isn't ready to play.
- 0.1.76 (Claude, 26 Nov 2026): **card pick changes, protocol 92.** Others' picks no longer dealt
  to watch: `CardScreen.Minis` shows each card someone on another PC (or a bot) has chosen as a mini card at the side
  (your team left, the other team right, names over them), a click shows it full size (`_bigMini`); with nobody here
  choosing, a "<team> is choosing cards" line (`MinisGUI`). (`WatchVotes` brings back dealing the other team's vote.)
  New Hand's re-roll is a request now (`PlayerInput.Reroll` = Pick 250, sent by `CardScreen.PickFor` until the
  re-roll is counted; a utility press already held at the pick's start used to be missed). The host holds the menu
  button through a pick to end it (`PlayerInput.EndPick` = Pick 249, `GameRunner.HostEndPick`; everyone left gets a
  random card, Choose's deck too). Choose decks keep a one-a-team card (Swap) for one teammate only, never after
  Double Card gave it (`Simulation.OneATeamInDecks`). Friendly Fire: a thrown teammate steers / brakes / uses cards
  (only being carried stops them; the throw is a glide boost). Loyal shows as "Loyalty" (id unchanged). Screenshot
  scene "minis". Watching: with nobody here choosing, one other player's hand is dealt to watch (`_watched` empty), the
  rest come in as minis; LB / RB (Q / E) step through the minis full size. A scorer swallowed by their own Black Hole:
  the celebration camera frames the hole (`GoalZoomHero` + `OutFrom`).
- 0.1.76 (Claude, 26 Nov 2026): **Forest Hockey look, switched off** (the user wants more work on it
  first). Built like Underwater: `ThemeBuilder.BuildForestHockey` (menu Ultra Puck > Forest) makes
  `Resources/UltraPuck/Themes/Forest Hockey.prefab` (an `UnderwaterTheme` with Floor = Grass.shader, Caustics slot =
  Dapple.shader (leaf shadows, multiply-blended), Rays in warm colours, trees / undergrowth sprites from Art/Forest
  (painted by a script, 100 px a unit), `Leaves` falling). `RinkView` now dresses any map with a "dressing", not just
  underwater; the rink picture has forest colours (`Underwater.ForestTheme`). Switched off by moving its map file to
  `Art/Forest/hockey-rink-forest.json.disabled` (see DISABLED.txt there): move it back to StreamingAssets/Maps to turn it
  on. Screenshot scenes: "forest" (needs the map file) and "arena-files" (every zip in ULTRAPUCK_ARENA_DIR).
- 0.1.75 (Claude, 26 Nov 2026): **custom arenas.** Zips in `Custom_Arenas` next to the exe
  (`ArenaSkin.Folder`, made with a README and `sample arena.json` on start; the project folder in the editor) hold
  Background / Arena Floor / Foreground / Shadows pictures (any png/jpg whose name says so; "guide" ignored) and an optional arena.json {name, rink}; rink =
  json, else a rink named in the zip's file name, else hockey-rink (`ArenaSkin.FromZip`). Only a look: `ArenaView`
  puts them on the rink as sprites (orders -50 background, 5 shadows, 45 foreground; the floor is a mesh of the rink's outline at order 1, over
  the ice and under the lines, so it never shows outside the walls) over `ArenaSkin.Canvas(map)`,
  the same rectangle the Desktop templates use (every screen shape's whole-rink view + 3%). Picked per rink (pref
  `arena_<map file>`) as extra looks on the rink screen (up / down: `LookCount` / `LookHere` / `LookLabel` /
  `StepLook`), plain look only; the online lobby has an "Arena" row (host) / shown row (others). Online: the host's
  arena name / hash / size ride in the lobby message; a joiner who wants it (Settings > Graphics > "Other players'
  arenas", `Settings.OtherArenas`, on by default) downloads it in 1 KB pieces (Msg.ArenaAsk / ArenaPart, up to 255
  pieces per ask in the lobby, 12 during a match, re-asks every 0.12 s, checked by FNV-1a hash, max 64 MB) and keeps
  it in `Custom_Arenas/_downloaded` (not offered to pick). Nothing in the simulation: replays and rollback unaffected.
  `GameRunner.RefreshArena` (every frame) decides what's shown. **NetSession.Version 91.** Tests:
  `ArenaZipsAreReadAndMatchedToTheirRink`, `TheHostsArenaDownloadsOverALossyNetwork`. Screenshots: `arena-pick`,
  `arena-match` (a made-up arena; screenshot runs don't read the folder).
- 0.1.74 (Claude, 25 Nov 2026): **card text outline.** Card file `"textOutline": true`
  (`CardDef.TextOutline`; card editor checkbox "Text outline", previewed there as an 8-way CSS text-shadow) gives
  each piece of the card's text a thin dark outline: eight black copies nudged round it (`CardScreen.TextOutline`,
  width scales with the text size, colour tags stripped, kept in step with its text in `Apply`, sorting order +4).
  (It was a one-way drop shadow, "textShadow", for one commit; the owner wanted an outline. No card used it.) Text
  boxes moved to order +3 so a card can have both (outline over the box, under the text). Screenshot `cardfx-1` puts
  it on Momentum for that run.
- 0.1.73 (Claude, 25 Nov 2026): **Momentum and Loyal cards; resolution arrows; puck bounce sound; goal removes the puck; Black Hole swallows; borealis / rainbow finishes; new display defaults.**
  MOMENTUM (`Stats.Momentum`, no max): while the loose puck is the shot of a player holding it (`s.PassFrom`), it
  never slows below `Tuning.MomentumPercent` (33) % x cards (capped at 100%) of the speed it was shot at
  (`GameState.ShotSpeed`, set in `Shoot`, cleared once someone has the puck; `Simulation.Momentum`, run right after
  drag). The other team touching it ends it (PassFrom goes -1). (This replaced a short-lived Ricochet card with
  1-600% / bounce-limit tuning sliders, never released.) LOYAL (`Stats.Loyal`, max 1): while the loose puck is their
  shot, it can go into their own net without counting (`CheckGoal` skips that net; it counts again once someone else
  touches it, since PassFrom changes). A Cannon Ball shoving them in is still a goal (its check comes first). Card art from the owner (`Cards/Art/ultrapuck.momentum.jpg`,
  `ultrapuck.loyal.jpg`); card text/tier/colour in `ultrapuck.json` are first guesses. Two seed-dependent tests now
  keep Double Card, Choose and New Hand out of their decks (adding cards changed what those seeds draw). Resolution
  list is now smallest first (left = lower, right = higher) and the row stops at each end. **NetSession.Version 90.**
  Tests: `MomentumKeepsAShareOfTheShotsSpeed`, `LoyalShotsDontGoInYourOwnNet`, `LoyalStillLetsACannonBallShoveYouIn`.
  PUCK BOUNCE SOUND: the sim counts the loose puck's real wall/pylon/Stone Wall bounces (`GameState.PuckKnocks`,
  speed going in `PuckKnockSpeed`, in the hash); `GameRunner.MatchSounds` plays `Sound.Bounce` for each, an octave
  lower and quieter for a slow puck (3 u/s .. ChargedShotMax). The default is a dry snap (`Sfx.BounceSnap`:
  a 40 ms 1500 Hz triangle tick + a noise click), picked by the owner after trying a wooden tok (`Sfx.BounceTok`,
  `SoundBank.Tok`, also rounder / brighter), a pure sine plink (`Sfx.BouncePlink`) and six bass-drum taps
  (`Sfx.BounceA..F`, `SoundBank.Kick`), all still choices: Sound settings "Puck bounce" (`Settings.PuckBounceSound`,
  pref `puck_bounce_snap`: 0 = Off, 1 snap, 2..4 toks, 5 plink, 6..11 drums), each choice plays as it's picked. AUDIO LATENCY: `ProjectSettings/AudioManager.asset` DSP buffer is now
  256 ("best latency", was 1024) so sounds land closer to what's on screen (the owner heard bounces late); the log
  says the buffer and its delay at start-up. If anyone reports crackling, try 512.
  DASH PREVIEW (Profile > Edit character): picking another trail starts a fresh dash at once; the player is drawn at
  radius 0.75 (was 1.5) and goes as far for its size as a real dash (`DashReach`: dash speed under the coasting drag,
  then the usual drag, about 9 player widths), with the same slowing-down curve.
  GOAL = PUCK GONE: `Simulation.Goal` stops the puck where it went in (vel 0, ShotSpeed 0), ends Ice Trail laying,
  Stone Wall laying, Invisible Shot and Cannon Ball; `MovePuck` / `BouncePuckAndRocks` leave it alone while
  Phase.Scored (no bounce sounds, nothing pushes it). BLACK HOLE goals now swallow: `GameState.HoleAt/HoleTime`
  (Tuning.HoleTime 1.7 s, as long as it's drawn), `Simulation.Swallow` knocks out anyone within `Tuning.HoleReach`
  (1.2) + their radius of it, marked `PlayerState.Swallowed` (no launch blast or flight, no out countdown; drawn as
  just gone), out for the rest of the celebration (the face-off / a free-play respawn brings them back). Tests:
  `ThePuckIsGoneOnceItsAGoal`, `ABlackHoleSwallowsPlayersWhoReachIt`.
  NEW DEFAULTS: camera shows the whole rink (`Settings.FollowCamera` default off), window Borderless
  (`Settings.WindowMode`; `BuildScript.Prepare` sets PlayerSettings.fullScreenMode to FullScreenWindow for the first
  frame: edit it there, Unity rewrites ProjectSettings.asset), resolution
  stays the monitor's own (0,0). Only people who never changed these get the new defaults.
  ANIMATED FINISHES "borealis" and "rainbow" (card file "finish", `CardDef.FinishNames`; Card.shader `_Special` 10 /
  11, from CSS the owner brought from ChatGPT): an animated fill in place of the card body's own colour, never on the
  picture (`SpecialArt` 0), text and frame on top as before. Borealis: four soft radial glows on #07152c on a layer
  1.9x the card that turns -25..55 deg, scales 0.9..1.15 and drifts, 4 s each way (8 s loop). Rainbow: a 110 deg
  7-stop gradient on a 4x background drifting to (100%, 85%), 5 s each way (10 s loop), with a 120 deg 8 px
  holographic grating overlay-blended on top. Smooth falloffs stand in for the CSS blur; no textures. Tunable per
  material: `_FxSpeed`, `_FxBright`, `_FxSat`, `_FxFoil` (grating), `_FxPhase` (all default 1 / 0, nothing sets
  them yet). Settings > Card effects off = the plain card (as for every finish). They use `_Time` like the other
  finishes (the game never changes Time.timeScale). Screenshots: `cardfx-1` / `cardfx-2` (put them on Momentum and
  Loyal for that run only). Test: `PuckBouncesAreCountedForTheirSound`. Screenshots: `cards-4` shows both cards.
- 0.1.72 (Claude, 25 Nov 2026): **smooth scrubbing on long replays.** Dragging along the
  replay time bar decoded a whole 120-frame chunk (~90 ms) whenever the mouse reached a chunk not in the 6-chunk
  cache (on a long match nearly every move), and did it for every GUI event, several a frame. Now the drag only sets
  `Tick` (`_scrubbed` makes `Frame` mark the jump), the frames are fetched once a frame, and while dragging
  `MatchReplay.FrameStateQuick` undoes the differences up to the frame and reads just that one (~5 ms). It keeps 8
  single frames plus 48 unpacked chunks (bytes, `RawChunk`, which `DecodeChunk` now shares) and decodes the whole
  chunk in the background after. The card-pick shading on the bar is worked out once in `Open` (`_picks`) instead of
  walking every frame on each draw. Then, still laggy: while dragging the frame is read on a worker thread
  (`ReplayViewer.Scrub` / `ScrubWork`, newest request wins) and the screen shows the newest frame read, still, so the
  main thread never waits; outside a drag `Fetch` uses `FrameStateQuick` too (no stall when you let go or play into
  a chunk that isn't decoded yet; it prefetches the next chunk past half way like `FrameState`). Test:
  `ScrubbingReadsTheSameFramesAsPlaying`.
  **RAGE rework.** The meter fills by the checker's heft over yours (`Simulation.Heft` = weight × Knockback Given,
  i.e. Heavyweight's "Weight +80%"; per check clamped `RageFillMin` 0.25 .. `RageFillMax` 3) up to a random whole
  3-5 (`RageNeedMin` / `RageNeedMax`, rolled with `s.NextRandom()` at the first check after a rage). `PlayerState.
  RageMeter` (int) is now `RageFill` (Fix) + `RageNeed`; `Tuning.RageChecks` is gone; `Enrage` takes the checker.
  Even checks: 3-5 to rage. The meter is no longer drawn (only the raging bar, and a smaller 26×3 cooldown bar;
  screenshot `rage-cooldown`). The near-rage twitch
  (from half full) now only moves the body (`PlayerView.TwitchOffset` / `TwitchTurn` on `_squash` / `_body`), not
  the player's root, so the aim line, charge ring, bars and the camera stay put. The card face still says
  "7 to rage" in `ultrapuck.json` (the owner had uncommitted edits there: left alone). **NetSession.Version 89.**
  Test: `RageFillsByTheWeightDifference` (and the rage test reworked).
  Files: `Core/MatchReplay.cs`, `Core/GameState.cs`, `Core/Tuning.cs`, `Core/Simulation.cs`,
  `Core/Net/NetSession.cs`, `Game/ReplayViewer.cs`, `Game/GameRunner.cs`, `Game/Views.cs`, `Game/Hud.cs`,
  `Tests/ReplayTests.cs`, `Tests/CardMechanicsTests.cs`, this file.
- 0.1.71 (Claude, 25 Nov 2026): **Steam profile pictures on replay banners.** The banner shows
  the scorer's Steam picture (framed, where the symbol was; the symbol when there's none: bots, no Steam, IP-only
  friends without Steam). `SteamNet.Avatar(id)` (large avatar via `SteamFriends.GetLargeFriendAvatar`, asks Steam with
  `RequestUserInformation` and retries each second until it's downloaded; cached). Each PC sends its first person's
  Steam account with the looks (`NetSession.SetLocalStyles(looks, accounts)` / `AccountOf`; NetSession.Version 87).
  `GameRunner.AccountOf`, `Hud.PictureOf`, `Banners.Draw(..., picture)`; the Edit character preview shows yours.
  Label drop shadows scale with the text (`Ui.ShadowFor(size)`: 1.5 / 2 from 36 px, shrinking below that but never under
  0.6 / 1; the owner's pick); the main menu subtitle and banner name lost their own offsets.
  **Titles**: Profile > Edit character > Title (`Cosmetics.TitleNames`: None, Legend, Master, Unstoppable, Rat, Destroyer,
  Developer; pref `look_title`; look bits 28-30, banner colour now bits 24-27). Shown over the name on the replay banner
  (in place of "GOAL"; None keeps "GOAL") and under it on the scoreboard (`Cosmetics.DrawTitle`, Developer in glowing
  green). Developer only for Steam account `Cosmetics.DeveloperAccount` (the owner's): not offered to others, and
  stripped from anyone else's look online (`Cosmetics.Allowed(style, account)` in `GameRunner.StyleOf`).
  **Black Hole** goal explosion: a lens quad (shader `Resources/UltraPuck/Lens.shader`: dark middle, picture bent round
  it, slight colour split on the rim) for 1.7 s with portal-style motes spiralling in (`StyleFx` bits `Pull`/`Swirl`),
  and it *pulls* everyone in instead of the goal blast pushing them away: a game rule, so it travels in the controls
  (`PlayerInput.Look` byte, bit `LookPull`, set by `LocalPlayers.LookFor` for this PC's first person; `Route` keeps it on
  the person's own player; `PlayerState.GoalPull`; `BlastFrom(..., inward)` at 60%). Goal explosions other than Classic
  and Supernova no longer ripple the rink. Profile previews: a new explosion first clears the last (`StyleFx.Clear`,
  `Effects.Finish`); on the Dash trail row a big player dashes back and forth every 2 s over a dimmed screen
  (`GameRunner.DashPreview`, `PreviewDash`; drawn at order +60). Replay viewer: holding left/right jumps to the next goal
  *or save*; goal markers as small as save markers; Speed shows an up/down d-pad (`PadButton.DPadUD`); the bar is
  scrubbed with the mouse read straight from the mouse (plus a hover line with the time), and moving the mouse shows the
  pointer. **Stealing** goes by the wedge as drawn (`OnWedge(q, puck, PuckRadius + StealReach)`; was a point at the front
  of the body, well short of the wedge's tip); someone being carried can't steal. **No knockback speed cap**:
  `KnockMaxSpeed` is now the safe speed for the usual substeps, and a tick with anyone faster is cut into 2/4/8/16x more
  steps so nobody goes through the boards. NetSession.Version 88. Tests `TheWedgeTipStealsThePuck`,
  `TheBlackHolePullsEveryoneIn`, `VeryFastPlayersStillBounceOffTheBoards`. Screenshots: `profile` has profile-title,
  boom-4-BlackHole-later and the dash preview; `replayview` has replay-scoreboard.
- 0.1.70 (Claude, 25 Nov 2026): **Profile: character looks and match replays.** Main menu >
  Profile > Edit character / Replays (`MainMenu.Page.Profile`, `Character`, `Replays`). *Looks* (`Game/Cosmetics.cs`,
  looks only): dash trail (9), goal explosion (9), replay banner (11) and its colour (Team or 11 colours; pictures with their own colours are hue-shifted to it), saved in PlayerPrefs `look_dash/banner/boom/banner_colour`, packed
  into one int (`Cosmetics.Pack`). The first person on a PC wears that PC's look (`GameRunner.StyleOf`); online each PC
  sends its people's looks (`Msg.Styles` / `StyleTable`, `NetSession.SetLocalStyles` / `StyleOf`; NetSession.Version 86).
  Particles in `StyleFx` (pooled; dash trails from `Show`, explosions replace `Effects.Explosion` by the scorer's look;
  streak/rim colour `PlayerView.DashColour`). Banners are drawn in code (`Banners`, procedural textures) and shown only in the
  in-match goal replays (`Hud.ScorerCard`, 280x52, bottom centre; not in saved-match replays). The Edit character page shows the rink behind:
  the bots wear your look (`PreviewLooks`), the explosion goes off on the right (`PreviewBoom`), the banner is drawn there.
  *Replays*: every non-free match is recorded (`Core/MatchReplay.cs`: a frame every 2 ticks, chunks of 120, each frame
  XOR'd with the one before, Deflate; encoded on a thread) with who's who (`Cast`) and markers (goals, saves, card picks).
  Every finished match goes into the match history (`persistentDataPath/History`, newest `GameRunner.HistoryKept` = 10 kept); the results screen's {savereplay} Save replay (Y / R), or Y / R on a Match history row, copies it into `persistentDataPath/Replays/*.upreplay`. Profile > Replays: Saved replays (Y deletes, twice), Match history, clips folder; list rows show rink, score, date, length and each team's players (file version 2 stores the first / last tick and each player's team in the description). States are
  written by `Core/ReplayCodec.cs`, which stores a description of the types (field names) so replays from older versions
  still load after GameState changes (fields matched by name; new ones keep their starting values). Watching:
  `Mode.Replay` + `ReplayViewer` (time bar with goal / save / card markers, card picks skipped, speed 0.1x-4x, left / right: a tap
  skips 5 s (a frame when paused), holding jumps goal to goal, camera: follow / whole rink / a player, Select hides the controls). Clips (X): handles on the time
  bar (LB / RB set start / end at the playhead, the first press starts a clip; or drag), saved as MP4 (`GameRunner.ExportClip`: `Time.captureDeltaTime` 1/60, each frame
  read back, `ClipWriter`: Windows Media Foundation H.264 through COM vtables, no install; Linux: ffmpeg if installed) to
  Videos/Ultra Puck, or copied to the clipboard as a file (`ClipWriter.CopyFile`: PowerShell `Set-Clipboard`; Linux
  wl-copy / xclip). Clips are the screen's size up to 1440p, H.264 High at about W x H x fps / 4 bits a second. Sound (Windows): the effects played meanwhile are written down (`Sound.ClipMode` / `Sound.Heard`) and mixed a frame at a time by `ClipMixer` (48 kHz stereo, AAC through Media Foundation); no charge hums. Music: replays (file version 3) note the song and how far into it (`MatchReplay.Music`, every 60 frames and on a change, from `Sound.SongNow` / `SongAt`); the clip mixes it in at `Sound.MusicMix` (setting Sound > Music in clips, pref `clip_music`, on). Unity streams its songs and can't hand over samples, so `BuildScript.CopyClipMusic` puts plain copies in the Windows build's StreamingAssets/ClipMusic (~39 MB) and `MusicReader` decodes them with Media Foundation's source reader. (Unity's `AudioRenderer` gave no samples in a built game.) Linux clips have no sound. Tests `ReplayTests`. Screenshot scenes `profile`, `replayview`.
- 0.1.69 (Claude, 25 Nov 2026): **knock-outs only for Featherweights.** A hard raging hit launches someone only if
  they hold a Featherweight card (`Simulation.Featherweight`), every time (`KnockOutChance` 1); respawn 5 s with a
  countdown on their own screen (`Hud.OutCountdown_`); the rage roar plays at 0.3. NetSession.Version 85.
- 0.1.68 (Claude, 24 Nov 2026): **RAGE card** (`ultrapuck.rage`, art from the owner's
  RAGE.jpg). Every check taken from an opponent (a body check in `Knock`, or a check into the puck you carry) fills a
  meter (`PlayerState.RageMeter`, `Simulation.Enrage`); at `Tuning.RageChecks` (7) you rage for `RageTime` (7 s):
  2.5x size and weight, +25% speed, 10x knockback given (no shot power: no puck while raging) (`CardLibrary.Recompute` while `RageTime` > 0),
  then `RageCooldown` (20 s) when checks don't count (`RageTimers`). Looks only: green while raging
  (`PlayerView.Disguise(own, rage)`), twitching from 4 checks (every 1.8 / 1.3 / 0.8 s at 4 / 5 / 6, timed by the
  match clock: `GameRunner.Twitch`), and a meter under the player for the card holder only (`Hud.RageMeters`: red
  notches; green bar while raging, grey during the cooldown). Raging: the dash's coast and hit window last 3x as long
  with lighter drag (`RageDashLonger`, `RageDashDrag`: about 3x as far), no picking up or stealing the puck (it drops
  when the rage starts; Head Start skips a raging player), and Stone Walls crumble on contact (`SmashStones`).
  **Knock-outs:** a raging check whose push (knockback x heavier x how square, the check's own numbers) is at least
  `Tuning.KnockOutSpeed` (200: a square raging dash into a plain player is about 280, into a Heavyweight about 160, a glancing one about 170), 1 time in
  `KnockOutChance` (1 since 0.1.69: every time; `s.NextRandom()` when it's more), launches them, and only if they hold a Featherweight card (`Featherweight`;
  everyone else bounces round the rink); a check that doesn't is capped at `KnockMaxSpeed` (80: the
  raging push went straight through the boards). Launched (`Launch`): they fly from where they were hit (`OutFrom`) across
  the rink and out where that line leaves it (`OutAt`), the blast and its boom when they get there; so does ending up outside
  the rink any other way (checked at the top of `Control`, not half way through a portal). `KnockOut` (public, for
  future cards that take people out): drops the puck / teammate / rock, parks them 1000 units off the ice (nothing
  reaches them), `OutTime` = `RespawnTime` (5 s; a big countdown on
  their own screen: `Hud.OutCountdown_`); `Respawn` puts them at their own face-off spot with `RespawnTime`
  (pillar) `RespawnShow` 1.2 s. Face-offs bring anyone out back. Bots don't pass to someone out. Looks: they fly on
  through the boards for half a second (`GameRunner.KnockOutEffects`), the camera leaves them out (`PlayerView.Out`),
  a blast bursts in from the screen edge they went out at (`Hud.Blasts_`: a flash and hard spiky rays, white, gold,
  team colour, and red with black cores), and a respawn pillar of light in the team colour (`Hud.Pillars_`). Sounds `Sfx.RageUp`,
  `Sfx.Launch`, `Sfx.Respawn`. NetSession.Version 82. Test
  `RageBuildsUpFromChecksAndWearsOff`; `TheTeamThatConcededPicksACard` now takes a plain card from each offer (a new
  card changed the draw). Screenshot scene `rage`.
- 0.1.67 (Claude, 24 Nov 2026): **mouse cursor size works.** 0.1.66's slider changed nothing: Windows shows a
  hardware cursor at its own pointer size. Sizes other than 100% now use `CursorMode.ForceSoftware` (`Crosshairs.UseGameCursor`).
- 0.1.66 (Claude, 24 Nov 2026): **Stone Wall, scoreboards, controls, cursor and
  crosshairs.** Stone Wall stands 11 s (was 5.5) and runs up to 45 long / 4.5 s of laying (was 30 / 3 s);
  NetSession.Version 79. Double Agent: a person sees their own name over themselves (`GameRunner.SeesOwnName`), unless
  someone on the other team plays on the same PC. Tab scoreboard: no cards on it any more (names and stats only),
  a Points column (the MVP rating: goal 3, assist 2, save 2, bump 1); holding it brings up the corner card list too,
  whatever its setting (`CornerCards(h, always)`), and letting go hides it again (unless the setting shows it). After-game table: the numbers now sit under their
  headings (they were left-aligned: `Ui.Font` hands out one shared style per size and the name label changed its
  alignment; the tables now take their own copies), a Points column, and room for the badges. Main menu: no mouse
  hover / clicks until it has slid in from the title screen and the button that left the title is let go
  (`MainMenu.MouseLocked`). **The mouse wheel only scrolls** (settings, main menu lists, the card grids): it no
  longer moves the focus; the view follows the focus only when the focus itself moves (`Menus._firstFocus`,
  `MainMenu._scrollFocus`, `CardScreen._editFollow`), and a row scrolled out of view loses its highlight.
  (`Ui.MouseMoved` also needs a real pointer move now: a wheel turn counted as one.) Controls tab: no "Press this row..." in the hints, no "Changes save
  straight away"; while rebinding only Esc cancels, so B / Circle can be bound (on a keyboard-only row a controller's B
  still cancels); Y / Triangle on a binding row clears it (`Controls.Clear`, `Pad.None`; a keyboard-only row loses
  its key). The crosshair: "Change crosshair" (Controls tab, with Crosshair size / colour moved there from Camera)
  opens a picker page (`Menus.Page.Crosshair`: 14 crosshairs, Apply / Cancel); pref `crosshair` (default 6, the ring
  with a cross); drawn by `Hud.DrawCrosshair`. The third is the game's original crosshair, drawn in code
  (`Crosshairs.Original`, `Hud.OriginalCrosshair`). Crosshair size 50-1000% in 10s (pref `crosshair_size`, default 100). Mouse cursor size 50-400% (Graphics tab, under Resolution; pref `cursor_size`):
  `Crosshairs.UseGameCursor` scales `Cursors/pointer-2x.png` (64 px) to 32 px x size, hotspot scaled with it.
  Any size but 100% uses `CursorMode.ForceSoftware`: Windows draws a hardware cursor at its own size (0.1.66's
  slider did nothing for that reason). The game's own mouse pointer (`Crosshairs.UseGameCursor`). Pictures
  are Kenney's CC0 Cursor and Crosshair packs (the PNG copies of the SVGs the owner named: Unity can't load SVG
  without a package) in `StreamingAssets/Cursors` and `StreamingAssets/Crosshairs`. Screenshot `settings-crosshair`.
- 0.1.66 (Claude, 24 Nov 2026): **Double Agent reworked, Backwards Rink.**
  *Double Agent*: each face-off the agent moves one spot further round the other side's group, the kick-off spot
  included (`Simulation.FaceOff`: the agents take spots `(KickOffTurn + j) % group size`, the home players take turns
  at the front of the spots left; with no agents nothing changes), and faces the puck like the group it stands with.
  It starts disguised (looks only): the other team's colour and a random opponent's name (`PlayerState.Disguised` /
  `DisguiseAs`, picked in `Borrow` with `s.NextRandom()` so every PC shows the same one, different names for several
  agents while there are enough). `DisguiseTime` counts from the puck drop (`Simulation.Disguises`); after
  `Tuning.DisguiseHold` (1 s) the colour hue-shifts back over `DisguiseFade` (1.5 s, `PlayerView.Disguise` /
  `HueShift`, driven by `GameRunner.DisguiseShown`) and the name tag switches back when it ends. Head Start's puck goes
  to a player on their own side before an agent. Changing team clears a disguise; `RemovePlayer` shifts `DisguiseAs`.
  Team Vote here gives the card to the whole team (the card is `max: 1`; this repo has no "one random player per copy"
  rule): they all go across, each on their own spot. *Backwards Rink* (`backwards-rink.json`,
  `make_hockey_rink.py --backwards`): the Hockey Rink with each net turned round where it stands, open towards the end
  wall (WIDEN 1.1 kept). New optional goal key `"faceoff"` (`GoalDef.FaceOff`): which side of the net the face-off
  group stands (here the middle side; otherwise the mouth side as before). Goals still count by the puck touching the
  box. NetSession.Version 78. Tests: `DoubleAgentTakesTurnsRoundTheOtherSidesSpots`, `DoubleAgentStartsInDisguise`,
  `SeveralDoubleAgentsShareTheOtherSide`, `TheBackwardsRinksNetsOpenTowardsTheEndWalls`,
  `TheBackwardsNetsScoreOnlyThroughTheMouth`, `BotsScoreOnTheBackwardsRink`, mirrored + face-off + net-size cases.
  Screenshot scene `agent` (agent-0/1-faceoff, agent-fading, agent-back on the Backwards Rink; named players with no
  controller, since bots' tags all say "Bot"). **Fixed (fairness):** a carried puck shoved dead in line with the
  carrier's facing picked a random side to slide round the wedge, which is fair to a turned rink but not a mirrored
  one (the Backwards Rink's kick-off hit it: the mirror test drifted); now it goes straight out (`Carry`).
  `BuildScript`: env var `ULTRAPUCK_BUILD_OUT=<folder>` builds there instead of Builds/Windows (for when the owner
  has the game open).
- 0.1.65 (Claude, 24 Nov 2026): **fixed: a slow mouse didn't move the menu focus.** The mouse counted
  as used only when it moved over 3 px in one frame, and at a few hundred fps a slow mouse never does. `InputMode` now
  tracks the pointer: on mouse and keyboard any move counts (`MouseMovedNow`, `Ui.MouseMovedNow`); after a controller
  it has to go 8 px in total first (a bumped desk still can't take the focus). The card screen's hover uses the same.
- 0.1.65 (Claude, 24 Nov 2026): **fixed: the game didn't start in the Unity editor's Play mode**
  (music, but a black or sky-blue Game view). `PortalSplit` made a `MaterialPropertyBlock` in a field initializer,
  and GameRunner makes a PortalSplit in one: the editor throws there ("CreateImpl is not allowed ... field
  initializer"), which left GameRunner's later fields null, so no map loaded and Awake failed. The block is now made
  on first use. Also `RinkGuide` (on the underwater prefab's root) now shows the real rink in the editor: `ShowRink` builds it
  with the game's own `RinkView` (new optional `dressWith` theme, so it doesn't put a second copy of the prefab in),
  hidden with HideAndDontSave and cleared in Play mode; with `ShowRink` off it draws a yellow outline instead (real
  lines, not a gizmo: the gizmo stopped being drawn after the editor's Play mode, and the Gizmos toggle hid it). Builds never checked this, so only the editor showed it. Keep Unity objects (materials, textures,
  property blocks, GameObjects) out of field initializers of anything GameRunner makes as a field.
- 0.1.65 (Claude, 24 Nov 2026): **intro and underwater polish.** Intro: the falling puck has the
  in-game trail (fading discs, `TitleScreen.TrailDots`) in white, stops dead (no bounce, no squash), and the beams
  glow (`Ui.Glow`: one smooth gradient texture drawn as a single 9-slice quad so the ends fade; layered boxes showed bands, and separate slices left a 1 px gap). Underwater: sand a bit darker (Sand.mat, `Underwater.Sand`), little grey
  pebbles in it (Sand.shader `Pebble`: flat grey, no shading or shadow, `_PebbleAmount` 0.45 of the way to grey; `_PebbleCell` / `_PebbleChance`), caustics at 35% of before (Caustics.mat
  strength 0.0525, no setting any more: the Caustics switch is gone), the light rays screen-blend and add up softly
  where shafts cross (no white blow-out), no drifting light lines on the water round the rink (Water.mat strength 0),
  bubbles 60% bigger and more see-through (FrontAlpha 0.18 / BackAlpha 0.4), and the wavy "Surface" strip along the top switched off in the prefab.
- 0.1.65 (Claude, 24 Nov 2026): **title animation reworked.** ULTRA and PUCK have a gap between
  them centred on the screen (`TitleScreen.Gap`; it closes as the name flies to the menu); the puck falls from the top
  through it (bouncing, `Fall`) and lands under the words, then the red / blue beams shoot out under the name. It's
  about 40% slower, and waits until 0.7 s after the screen appears *and* the window has kept its size for 0.45 s
  (`Clock`: the game going fullscreen used to eat the start of it). Screenshot scene `title` retimed (title-0/1/1b/2).
- 0.1.65 (Claude, 24 Nov 2026): **catching up on cards after joining part-way.** A player added
  mid-match (`Simulation.AddPlayer`: online re-seat or a host-added bot) gets `PlayerState.CatchingUp`; each tick
  outside the card screen `Simulation.CatchUp` gives them one offer at a time until their `PickRounds` reaches their
  team's `GameState.TeamRounds` (counted in `StartPicking`), with no time limit, while the match goes on; meanwhile
  their controls are emptied, they stand still and can't take the puck, and nobody can swap onto them (`Route`).
  Team Vote / Same Draw: they get the cards of the teammate with the most rounds straight away (one-a-team cards
  left out). Picks resolve through the shared `TakePicked` (Choose's deck, Double Card work as usual). The card
  screen shows for a local player with an offer outside Phase.Picking (`CardScreen.LocalCatchingUp`, no watching),
  and `LocalPlayers` sends its pick then too; its heading says "Catching up · N cards to go" with no timer. Bots
  catch up at once (no look-over delay there) and now send no pick while `PickHeld`; the bot standing in for a person
  who is away (the rollback marks them `Bot`) leaves that person's catch-up cards alone (`PlayerState.BotCatchUp`, set
  only for players added as bots). NetSession.Version 77. **Fixed: joining part-way by IP never got in** for bigger
  matches: `UdpTransport` read into a 2048-byte buffer, and the hand-over (state + inputs) is bigger, so it was
  thrown away every time (now 65536; test `RealUdpCarriesBigPackets`). Screenshot scenes `mainmenu`, `teampage`,
  `freepause`, `catchup`, and `hostlate` + `joinlate` (two copies with the same ULTRAPUCK_SHOTS folder: one hosts by
  IP until a card round, then the other joins part-way; `_shotRules` forces rounds / each-player picking / 3 s picks
  in that run only).
- 0.1.64 (Claude, 24 Nov 2026): **games stay in Find games on Steam while a match is on.** The host's lobby used to
  go unjoinable with "playing" = 1 and the search filtered those out, so anyone who left couldn't find the game again.
  Now `SteamNet.Describe` keeps it joinable, the search keeps in-progress lobbies, and the listing says "match on"
  (`Listing.Playing`); joining part-way (0.1.63) takes it from there.
- 0.1.63 (Claude, 24 Nov 2026): **Carry-through, match settings for everyone, joining part-way,
  change team / watch, subtitle shadow, free play arrows, match song "Final Hit".** *Carry-through* (match setting, `Settings.CarryThrough` /
  `CarryPercent` 5-100%, default on at 40% (prefs `carry_through_v2` / `carry_percent_v2`), the % slider is temporary for tuning): any pass or shot (`PassFrom` set, caught by
  someone else, either team) carries the catcher along like a gentle Cannon Ball (`Simulation.CarryThrough`: speed along
  the shot replaced by puck speed × % × weight / Knockback Taken if faster; no Shoved, no loss of control; not on top of
  a Cannon Ball). State `GameState.CarryOn` / `CarryPercent` (from `Tuning.CarryThrough` / `CarryPercent`, rules byte
  after the card list); the host changes it mid-match from the pause Match settings with host commands Pick 224 off /
  225 on / 226+n strength n×5% (`GameRunner.HostCommand` keeps only the newest strength queued). *Knock-away* (match
  setting, `Settings.KnockAwayPercent` 0-500% in 25s, default 350 (pref `knock_away_v2`; 100 = the old pop), a tuning slider): how hard a check (a `Knock`, or a
  dash into the carried puck) pops the puck away from its carrier (`KnockLoose(..., check: true)` scales
  `KnockLooseSpeed`; Shockwave / Puck Gun / rock knocks are unchanged). State `GameState.KnockPercent`, rules byte after
  Carry-through (% / 5), host command Pick 100+n = n×25%; NetSession.Version 76. *Match settings for
  everyone*: the lobby message carries the host's current rules (`NetSession.LobbyRules`, set by the host's game each
  frame); non-hosts get a read-only Match settings page in the pause menu (`Menus.ShownRules` / `RulesView` in
  Settings.cs, Carry-through shown live from the state) and read-only rows in the lobby Options (`MainMenu.Shown`).
  *Join part-way / change team / watch*: Hello is no longer refused mid-match; a newcomer waits in the lobby ("A match
  is on: ready up to join it") and gets a seat once ready. Pause menu has **Change team** (under Match settings,
  `Menus.Page.Team`): a row per person on this PC, red / blue / Just watching (online only; `Slot.Watching`, look bit 128
  `NetSession.Watching`). Locally it's `Simulation.ChangeTeam` at once. Online the host *re-seats* the match
  (`NetSession.Reseat`): on a settled tick it changes the game (RemovePlayer / ChangeTeam / AddPlayer, bots added
  mid-match get seats), bumps `Layout`, builds a new Rollback copying everyone's inputs, plays back to where it was,
  and hands every PC the state plus all inputs from that tick (`Msg.Rejoin`, now also used for this; clients keep their
  own newer inputs by seat, then catch up). Inputs / Away / Checksum carry the layout, stale ones are ignored; waits
  for card picks and for a bot removal to settle (`_botLeftTick`). Host commands / pause go with the host's first
  seated player. `BotLeftMatch` counts bots before the player (people can now come after bots). **NetSession.Version
  75.** Main menu subtitle shadow offset 0.6 / 0.8 (new `Ui.Label` overload with an offset). Free play pause: small ‹ ›
  arrows either side of the Bots and Rink values. Tests `CarryThroughTakesWhoeverGetsThePuckAlong`,
  `ChangingTeamMidMatch`, `JoiningChangingTeamAndWatchingMidMatch`. Not checked on screen or with real PCs.
- After 0.1.61, not released (Claude, 24 Nov 2026): **cards per row in the grid.** LB / RB (Q / E) in free play's
  card editor and Choose's deck change the cards a row, 2 to 5 (default 3, was a fixed 4), remembered in
  PlayerPrefs `card_grid_cols` (`CardScreen.ResizeGrid`). `EditCols` / `EditRows` / `EditScale` / pitches / top
  are now properties from `_cols` (`GridScales` 0.8 / 0.68 / 0.54 / 0.45 for 2-5, `GridRows` 2 / 2 / 3 / 3,
  centred on y -0.74 where 4 a row sat); the Cards page uses the same size but keeps LB / RB for sorting. The
  editor's "another person on this PC" moved from LB / RB to Y / Triangle (Tab on a keyboard). Display only.
  Checked: Windows build; not looked at on screen (the owner tries it on the Deck).
- After 0.1.60, not released (Claude, 24 Nov 2026): **button pictures on the Steam Deck.** The Deck's pad is
  `InputMode.Family.Generic`, whose pictures were looked up in `InputPrompts/Generic` (only plain shapes, no
  `xbox_*` files), so the card footnote "Press {utility} to use" showed an empty square and hints fell back to text
  ("Y"). `InputIcons.Subfolder` now sends Generic to the Xbox pictures (A/B/X/Y). Display only. Checked: Windows
  build; not seen on the Deck.
- After 0.1.59, not released (Claude, 24 Nov 2026): **Choose's deck no longer picked for you.** Taking Choose went
  straight on: the card screen keeps sending the pick (`_ready`) until its next `Frame`, and a frame runs several
  ticks, so the next tick took that same number from the deck. New `PlayerState.PickHeld` (set when a player is
  handed new cards: Choose's deck, a New Hand re-roll, incl. teammates' re-rolled shared hands; cleared in
  `StartPicking` / at the end): picks wait until that player's Pick input is back to 0. **NetSession.Version 74.**
  Test `TheChoosePickStillHeldDoesntPickFromTheDeck` (fails without it); the Choose tests let go before picking.
  Checked: 210 tests (205 pass, 5 skipped), Windows build.
- After 0.1.59, not released (Claude, 24 Nov 2026): **card pick log lines.** The owner sometimes gets no card
  screen when scored on (their own pick, not just bots'), not found by reading the code (the sim waits for the
  person; Route resets Pilot while picking). `CardScreen.LogPicks` writes "Ultra Puck cards: pick started / over
  after N s" (tick, the card screen's stage, `_ready` count, each picker's offer / pick, here / away / bot), and
  `Deal` writes "Ultra Puck cards: dealt ...". A pick that starts with the screen not Idle, or ends with no "dealt"
  line, points at the cause. Display/log only, no protocol bump. Checked: Windows build.
- After 0.1.58, not released (Claude, 24 Nov 2026): **goal explosion always shows; bots take a moment to pick.**
  The goal explosion/ripple fired only when a frame's two shown ticks straddled the goal (`a` not Scored, `b`
  Scored), but at 120 ticks/s a 60 fps frame (Steam Deck) runs ~2 ticks, so about half the goals were skipped.
  `GameRunner.Show` now fires live on the first frame whose state has more goals than `_boomGoals` (reset when the
  score goes back down, e.g. a new match), and replays once per replay (`Show(..., replay: true)`,
  `_replayBoomed`). Cards not showing after a goal: when the conceding team was all bots, they picked on the
  first tick, so the pick was over before the card screen saw it. Bots now look over their cards for
  `Tuning.BotPickDelay` (1.8 s; again after a New Hand re-roll; their highlight steps along the cards every 0.3 s,
  settling 0.5 s before taking it) in `Bot.PickCard`'s tier pick (not when following humans in a Team Vote).
  **NetSession.Version 73.** Tests `BotsPickTheirCards` (waits the delay, highlight moves) and
  `TeamVoteBotAlonePicksByTier` updated. Checked: 209 tests (204 pass, 5 skipped), Windows build. Not seen on the Deck.
- 0.1.58 (Claude, 24 Nov 2026): **Double Card reveal; clean grid exits.** Double Card records
  the cards it gave (`PlayerState.Gifts`, cleared for everyone in `StartPicking`; `Gifted` counts them), and the
  countdown waits `Tuning.GiftRevealTime` (2.5 s) more on top of `PickOutroTime` when anyone has Gifts. CardScreen
  `Reveal`: once the picked cards are gone and the phase is Countdown, it deals those cards face down (flipping up),
  everyone's side by side (names under each card if several took it), header "Double Card / <name> got two cards"
  (`RevealGUI`), holds `RevealHold` 2 s, then they drop. `_seenGifted` (cleared during each pick) stops it repeating.
  Grid exits (free-play editor, card list, Choose's deck): rows scrolled off screen now stay out of sight instead
  of flying across the screen face down, and the deck's on-screen rows drop in the editor's ripple (`Leave` with
  `_deck`). **NetSession.Version 72.** Test `DoubleCardsCardsAreShownBeforeTheCountdown`. Checked: 209 tests (204
  pass, 5 skipped), Windows build. Not looked at on screen.
- 0.1.57 (Claude, 23 Nov 2026): a dash counts as a check for 0.4 s (`Tuning.DashHitTime`, was
  0.25 s; players still gliding fast after it weren't checking). Protocol 70. Trying it out; the alternatives were a
  speed-based check window or a short grace period. Card art for Choose, New Hand, Double Card added.
  Portal Gun keeps its colour until a portal sticks (`PortalNext` flips in `PlacePortal`, not on firing). Protocol 71.
- 0.1.57 (Claude, 23 Nov 2026): **Choose, New Hand, Double Card cards.** New stats (no `Stats`
  fields; looked up on the card: `CardLibrary.IsChoose` / `IsDouble` / `NewHands`): `"Choose"`, `"DoubleCard"`,
  `"NewHand"`. **Choose** (S): once the pick resolves, that player goes straight on to a second pick
  (`PlayerState.PickKind` 1, fresh `PickTime`, everyone else waits) offered `CardLibrary.DeckOffer`: every card they
  could be offered (max, maxTeam, minTeam, noBots, requires, `CardsOff`), never Choose / Double Card
  (`CardGivers`), in deck order. Team Vote / Same Draw: the deck round is never a vote, each picks their own (sim
  `deckRound`, CardScreen `DeckRound` deals one at a time). CardScreen shows it in the free-play editor's grid
  (`_deck`, `EditHome` / `EditHomes`, up/down + wheel). **Double Card** (A): two different random cards
  (`Simulation.GiveRandom`: same limits, never a card giver, never a utility for someone who has one).
  **New Hand** (B, max 1): a press of Utility (Triangle / Y, edge on `PrevButtons`) while still choosing re-rolls the
  offer (`Simulation.Reroll`: none of the cards just shown unless too few are left), once a pick per copy held
  (`PlayerState.Rerolls`, reset in `StartPicking`); in Team Vote / Same Draw it re-rolls the whole team's hand and
  clears the team's locked picks (then `OnePickerGets` again). A pick on the re-roll tick is ignored. CardScreen
  redeals when the dealt hand (`_dealt`) no longer matches, drops `_ready` picks made from another offer
  (`_readyOffer`), and shows "{utility} to re-roll" under the cards (`Roller`). Bots re-roll a hand with nothing
  better than C (not in shared draws); bots on a deck take the best tier. Cards still held as cards (so they show
  up and count). No art yet (`Cards/Art/ultrapuck.choose.jpg`, `ultrapuck.newhand.jpg`, `ultrapuck.doublecard.jpg`).
  **NetSession.Version 69.** Tests: `ChooseOffersTheWholeDeckWithTheUsualLimits`,
  `ChooseWithTeamVoteLetsEveryonePickTheirOwn`, `DoubleCardGivesTwoOtherCards`, `NewHandRerollsOncePerPick`,
  `NewHandInATeamVoteRerollsTheTeamsHand`, `NewCardPicksAreTheSameEveryRun`.
- 0.1.57 (Claude, 23 Nov 2026): **per-card text boxes.** Card json `"textBoxes": true`
  (`CardDef.TextBoxes`) puts a snug black box (85%) behind each piece of the card's text (`CardScreen.TextBox`, sized
  from the TextMesh's `localBounds` every frame since the text is laid out late). Meant for busy finishes like
  starfield. Card editor: "Text boxes" tick box. Display only. Checked: tests, Windows build, cards screenshot.
- 0.1.57 (Claude, 23 Nov 2026): **starfield is black space; no shine sweep.** Card.shader
  starfield turns the card body black except its bright text (the picture only its darkest parts) with stars on top
  and a faint nebula; the tilt glare is mostly kept off that black (`spaceMask`). The timed diagonal shine sweep is off
  on every card (`_SweepStrength` 0 in `CardScreen`; the code that moves it is still there). Display only. Checked: 195
  tests (190 pass, 5 skipped), Windows build, cards screenshot with Swap / Stone Wall set to starfield.
- 0.1.57 (Claude, 23 Nov 2026): **per-card glare toggle.** Card json `"glare": false`
  (`CardDef.Glare`, default true) turns off the light sweep on that card's body and picture (`CardScreen`); display
  only, no protocol bump (the card file is in the fingerprint). Card editor: a Glare tick box (unticked writes
  `"glare": false`, ticked drops the key); starting the editor asks an already-running one to close and waits for the
  port. Checked: 195 tests (190 pass, 5 skipped), Windows build.
- 0.1.57 (Claude, 23 Nov 2026): **Controller pre-charge auto-aim; Steam Input warning as a
  toast.** A controller player holding charge without the puck (and not pushing the aim stick past 0.2, the sim's
  deadzone) aims at the nearest net they attack: at the nearest spot of its opening (between the posts, less the
  puck's radius + 0.15) that a straight shot goes in through (`GameRunner.ScoreableSpot`, mouth side from the goal's
  `facing`, or towards the rink's middle for the end-wall nets). Beside / behind the net, or more than ~70 degrees
  off straight in, it aims at the middle of the net. Walls between the player and the net aren't checked (S / U
  bends). Done on the input side only (`LocalPlayers.Sample`,
  `NetAim` hook set in `GameRunner` next to `PlayerWorld`, `PadCharging`): it is just an aim value like the stick's,
  so no rule change and no `NetSession.Version` bump. Keyboard/mouse and bots unchanged. Steam Input: can't be
  switched off from our side while we run as Steam's test app (480): its Steam Input settings belong to that app, the
  overlay isn't injected (so no virtual pad reaches us), and the only in-game route is reading pads through the
  `ISteamInput` action API with our own manifest (`SetInputActionManifestFilePath`), a big input rework. The real fix
  is our own app ID with Steam Input set to off/opt-out in Steamworks. Until then the warning is a 12 s toast
  (`Toasts.Show(text, seconds)`, new optional length) instead of the main menu message. Checked: 195 tests (190
  pass, 5 skipped), Windows build. Not tried with a controller or with Steam Input on.
- 0.1.57 (Claude, 23 Nov 2026): **Foil and Polychrome card finishes.** Optional card field
  `"finish": "foil" | "polychrome" | "holo" | "pearl"` (`CardDef.Finish`; anything else is a load error; holo / pearl
  give any card the S tier's holo + sparkles / the A tier's pearlescent). More finishes (shader `_Special` 1-9 + `_SpecialAmt`, table
  `SpecialNames` / `SpecialBody` / `SpecialArt` in CardScreen): starfield, gilded, prism, frosted, neon (frame only),
  aurora, molten, glitch (tears bands of the card sideways, outline and frame too, via a torn `uv` at the top of
  `frag`; colour split incl. the frame, coloured blocks), glass (9: see-through via a `glass` alpha factor, bright
  edges, a light streak). The full list the loader accepts is `CardDef.FinishNames`. Display only, so no
  `NetSession.Version` bump. Card.shader `_Metal` (brushed-metal grain plus a bright sheen band that slides with the
  tilt) and `_Poly` (turns every colour round the colour wheel in drifting waves, tints greys). They replace the
  tier's holo / pearl on the body and art (glare and sparkle stay), set in `CardScreen.Build` via `Finish(...)`, so
  card picks, free play and the Cards page all get them. With "Card effects" off, both are 0. No card uses
  them yet. The card editor session was told about the field. Test `CardsCanHaveAFinish`. Checked: 195 tests (190 pass,
  5 skipped), Windows build. Not looked at on screen.
- 0.1.57 (Claude, 23 Nov 2026): **card editor.** `Card Editor.bat` opens a local page showing every
  card as a card (tier frame, accent, art, flavor, face markup, footnote); click one to edit it live; new / duplicate /
  move / delete. Save checks it the way the loader does (ids, tiers, known stats, "x1.5"/"/1.6"/"+1" values), writes
  `ultrapuck.json` in the same layout under the same header, and copies the cards + art into `Builds/Windows` and
  `Builds/Linux` (no compile). "Publish to GitHub v<version>" patches the cards into `dist/` zip + tar.gz and
  `gh release upload --clobber`s them onto the current release (no version bump). Caveats: the updater only compares
  version numbers, so people already on that version keep their old cards; and card files are in
  `GameRunner.Fingerprint`, so mixed card files can't play online together. `python Tools/card_editor.py --check`
  tests that saving the file unchanged gives the same text. No game code changed.
- 0.1.56 (Claude, 23 Nov 2026): every net's opening 10% wider (`WIDEN = 1.1` on the
  mouth in make_rink.py, make_hockey_rink.py, make_curved_rinks.py; all five maps regenerated, depth unchanged;
  Hockey Rink opening 3.17 -> 3.49 inside the walls). Map files are in the online fingerprint, so no protocol bump.
- 0.1.55 (Claude, 23 Nov 2026): goal banner (Hud `GoalBanner`): "Name Scored!" in the START gold, slams in with a
  bounce and tilt, then pulses; an own goal says "<Team> Team Scored!"; assist underneath. Own goals: the sim keeps
  the last player of each team to touch the puck since the face-off (`TeamTouch0/1`, carrying or the loose puck
  against them, or their Puck Gun shot moving it); a goal nobody on the scoring team shot or carried in goes to that
  player, and is only an own goal when there's none. Portals: placing one drops every teammate's portals and your
  own of that colour, so a team has at most two up. Caustics layer grown out to the walls' outer edge (`RinkView.Grow`)
  and a Graphics setting "Caustics" (`Settings.Caustics`, `RinkView.Settle`). Slot reels: only the four prize symbols
  (sim and drawing). Protocol 68.
- 0.1.54 (Claude, 23 Nov 2026): **map variants (looks) and Underwater Hockey Rink.**
  A map file with a `"base"` (another map file), `"variant"` (its name) and `"theme"` is a look for that rink:
  `MapDef.BaseOf` / `MapDef.FromVariant` (the base's walls, nets and spawns; `MapDef.Theme` / `Variant`). GameRunner
  loads variants after the ordinary maps, each right after its base (`_mapBase`; `Looks(i)`, `Rinks()`, `StepRink`,
  `StepLook`, `RinkName`, `LookName`; `MapName` adds "(Underwater)"). The rink screen (PlayScreens `DrawMap`): left /
  right steps rinks, up / down the looks, shown as a small vertical carousel above the picture (`DrawLooks`). Menu
  "Rink" rows still step every map, looks included. `hockey-rink-underwater.json`: theme `underwater` (`Game/Underwater.cs`):
  floor shader `UltraPuck/Sand` (sand plus animated caustics, world space), backdrop `UltraPuck/Water`, light rays,
  wavy surface, swaying seaweed, coral, rocks and shells round the edges, rising bubbles, blue-grey boards, soft
  floor lines; sprites cut from the user's own sheet (now Art/Underwater/*.png). Rink pictures
  paint it sand and water. Screenshot scene `rinkpick`. Tuned to the user's reference: soft caustics, a blue-green
  cast on the sand (`_Tint`), sun rays over the arena (shader `UltraPuck/Rays`, additive, order 5, under the
  players), soft wall shadows (offset copies below each wall), and sea life placed by hand like the reference
  (fractions across the rink, blue haze on things further back), bubble streams up the sides.
  Each piece gets its own size (78-122%), a nudge and sometimes a flip, plus smaller bits between. Wall shadows
  use shader `UltraPuck/Shadow`: the sand writes stencil 1, the shadow draws only there and once a pixel (no
  darker joints round curves, nothing on the water). Screenshot scene `video` writes 30 fps JPG frames (with
  `Time.captureFramerate`) for ffmpeg.
  **The dressing is a prefab, edited in Unity:** `Resources/UltraPuck/Themes/Underwater Hockey.prefab` (named by the
  map file's `"dressing"`; `Underwater.Dress` instantiates it under the rink). Its root's `UnderwaterTheme` holds the
  floor material, wall colour, shadow (material, colour, offset), line colours; `RinkGuide` draws the rink outline as
  gizmos. Children: Water / Sun Rays quads, Surface (`SurfaceBob`), Reef (top/bottom) sprites (`Sway` on seaweed),
  `Bubbles`. Materials in `Art/Underwater/Materials` (Sand, Water, Rays, Shadow); sprites in `Art/Underwater`.
  Menu Ultra Puck > Underwater > Open Underwater Hockey; Rebuild (`Editor/ThemeBuilder.cs`) remakes the prefab from
  code, losing hand edits. The user edits it by hand now: don't rebuild over it.
  Water tint over the whole game (players, puck): `UnderwaterTheme.ScreenTint`, a big quad at sorting order 1000
  under the rink (the cards have their own camera, so stay clean); the sand's own `_TintAmount` is 0 now. Brighter
  puck there: `UnderwaterTheme.Puck` / `PuckEdge` (white, dark rim), via `PuckView.Look`. The caustics moved off
  the sand into `UltraPuck/Caustics` (additive, `Caustics.cginc` shared with Sand), a floor-shaped layer at order 28
  over the players; the tint is at order 29; the puck (30-33) is above both, so it stays bright white.
  Rink songs: a map file's `"music"` (`MapDef.Music`, a clip in Resources/UltraPuck/Music/Rinks): a match there
  starts with it, then the playlist; and it's the menu theme while that rink is the main menu's (`Sound.Music(match,
  rinkSong)`). Underwater Hockey: "Deep Focus". The main menu shows a random rink at launch (`_menuRink`, not
  saved; `PlayersRink()` puts your saved rink back when you go to a lobby, free play or online; screenshot runs
  keep the saved one). U Rink arms 8 shorter (make_curved_rinks.py `end` 13). Swap trades momentum again (you take
  the rock's, it takes yours): protocol 67.
- 0.1.53 (Claude, 23 Nov 2026): Shockwave max 1 a player; its note is just "Press {utility} to use."
- 0.1.52 (Claude, 23 Nov 2026): Shockwave charges faster (40%, then 20% more):
  `ShockChargeTime` 25/7 s (about 3.6 s to its full 6 units), and its push grows with the charge, up to
  `ShockFullPush` 3.5x a tap's at full. Shockwave sounds: `Sfx.ShockHum` (a loop like the charge hum,
  `Sound.ShockCharge`, pitch rising with the hold) and `Sfx.ShockBlast` (deeper and louder for a bigger wave).
  Ice Trail max 1 a player. **Version 66.**
- 0.1.51 (Claude, 23 Nov 2026): flavor text Stone Wall "This rocks!", Team Play "I'm open!".
  **Swap trail** setting (Gameplay, under Hit effects; `Settings.SwapTrail`, off by default): when someone on this PC
  swaps body (`Simulation.Steers` changes), `Effects.SwapTrail` flies a glowing head with a dotted trail from the body
  they left to the one they're on, ending in a ring (`GameRunner.SwapTrails`; screenshot scene `swaptrail`). The
  corner card list is hidden by default (`corner_cards` 0). Main menu: no version above the title (the one
  bottom-right stays), the choices start right under the title (top 160), no "Choose" hint.
- 0.1.50 (Claude, 22 Nov 2026): **Portal Gun fixes, Invisible Shot gone.** On a net a portal
  may only go on the outside of its back wall, all of it on that one segment (`NetNear` / `NetFront` in
  `PlacePortal`); anywhere else a portal that would reach round onto a net fizzles (the old post shot put one inside
  the net). Cards can say `"maxTeam"` (`CardDef.MaxPerTeam`, `CardLibrary.TeamFull`): not offered once that many
  teammates hold it, and two teammates taking it at once — the second gets the next card in their offer. Portal Gun
  is `maxTeam` 1. Going through a portal is gradual now: `PortalPass` lets a thing touching a portal's face (moving in)
  sink into that wall (`CollideWalls` overload skips the portal's stretch), records it in `PortalVia` / `PuckPortal`
  (portal Id, 0 = none), and moves it across once its middle passes the face, as far out of the other face as it
  was in, along the other portal's wall (`PointAlong`, so curves work, never past its end). Entry is measured from
  the real wall (curved ends let you in); only wall running along the portal, within its length, is skipped. A
  carried puck slides into a portal ahead of its carrier (`CarryPortal`, kept short of a net's box; `CheckGoal`
  ignores a puck in a portal), and a player coming out faces out (`PortalTurn`). Safety net `Escaped`: anything that
  ends a substep off the ice or newly in a net while in (or just leaving) a portal goes back to where it was.
  `PortalSplit` (Views.cs) draws things cut at one face (shader `UltraPuck/Clip`) plus a copy coming out of the
  other. The second team's portals are green / red (`PortalViews.Of(colour, team)`); no owner dot, no sparks on
  pass-through, the drifting glow motes stay. Invisible Shot card and art removed (its stat / code stay, unused).
  No portal shots while carrying the puck; on a net's back wall (and at the ends of any open wall) a portal stays
  `WallHalf` clear of the rounded ends (`ClampAlong`; the hockey nets' back is an exact fit, so it's centred).
  Free play d-pad puck drills (`PlayerInput.PuckToMe` / `PuckAtMe` / `PuckAtTheirNet` / `PuckAtOurNet`, 220-223 as
  a Pick, `Simulation.PuckDrill`; `LocalPlayers.PuckDrills` latches them and stops the d-pad moving you in free play).
  Main menu Cards page reads any device (`Local.Menu()`), sorts on the bumpers, and A–Z shows no numbers or stats.
  Count badges on cards fade under the focused card (`BadgeShow`) instead of flickering. Same Draw / Team Vote:
  a `maxTeam` card (Portal Gun, and now Swap) is never in the shared hand; `OnePickerGets` puts it in just one
  picker's hand (a person first) in place of one card, the rest get a fresh card there; in a Team Vote whoever took
  it keeps it and isn't counted in the vote (bots ignore them too); the card screen only shares a screen between
  local players with the same hand. A portal shot onto the other team's portal replaces it in exactly its place.
  Card notes (under the stat rows) are bigger (`NoteFont` / `NoteChar` / `NoteWrap`); rows squeeze up to fit.
  Portal Gun card: no rows, just "Only one person per team (green) can have this card. Use it wisely."
  Fixed a hang after a goal (Same Draw / Team Vote, the Portal Gun the only card switched on): `OnePickerGets`
  could loop for ever; it now works from a fixed copy of the shared hand. When nothing switched on is left for
  someone (that case, or a teammate holds the one-a-team card in Each player mode), they get stand-in cards from the
  switched-off ones (`AnyOtherCard` / `StandInOff`: never another utility for someone holding one). Test
  `PicksWithOnlyTheOneATeamCardLeft`. Shockwave card: Range 6 units, Charge Time 6 s, no knock-off line or cooldown.
  **Slot Machine** (`ultrapuck.slotmachine`, utility stat `SlotMachine`): the utility button spins (`SpinSlots`: outcome
  settled from the match RNG, each prize 1 in `SlotOdds` 30; `SlotReels` packs the three shown symbols, 1-4 prizes,
  5-8 fillers), `SlotSpinTime` 1.2 s; prizes (`CardLibrary.SlotSpeed/Giant/Invisible/Strong`): +120% speed 10 s, x2
  size and weight 10 s (in `Cards.Recompute`), invisible 12 s (`InvisibleTime`), 4 Over Charges 10 s (Recompute);
  one prize at a time, no wait after a loss (`SlotWait` 0), all cleared at face-offs. Sounds `Sfx.SlotStop` (each
  reel), `SlotWin`, `SlotDud`, played by `SlotViews`; grey reel border. `SlotViews` draws the mini reels under
  the player (drawn symbols; a picture in StreamingAssets/Cards/Slot/<name>.png would replace one). Bots spin about
  every 2 s. Free play's hint text at the bottom is gone. **Version 65.**
  A player got outside the rink by hugging the boards through a corner
  portal (before the safety net); the exact route wasn't reproduced in tests (`NoWayOffTheIceThroughPortals` fuzzes
  it). **Version 64.**
- 0.1.49 (Claude, 22 Nov 2026): **face-offs in front of the nets, Portal Gun.**
  Face-offs no longer use the maps' `spawns` (only as a fallback): `Simulation.FaceOffSpot(team, slot)` puts each team
  in a tight group (`FaceOffShape`, `FaceOffBack` 3 / `FaceOffRow` 1 / `FaceOffSpacing` 1.2) just in front of its own
  net's mouth (the goal's `facing`, or towards the puck on the Test Rink), slot 0 at the front. `GameState.KickOffTurn`
  goes up each face-off and rotates who stands in slot 0. **Portal Gun** (`ultrapuck.portalgun`, stat `PortalGun`,
  utility): shots (`GameState.PortalShots`, `Shot.Kind` = colour) go blue / orange alternately (`PortalNext`), pass
  through everything and make a `Portal` where they meet a wall line (`PlacePortal`: slid along an open line so all
  `PortalLength` 3.172 fits, fizzles into a net or on a too-short wall, replaces that colour). `ThroughPortal` sends
  players, the loose puck and loose rocks touching one portal (moving into the wall) out of the owner's other one,
  velocity turned with the portals, never slower than `PortalExitSpeed`, `PortalRepeat` before re-entry. Face-offs
  clear portals. `PortalViews` draws them (glow strip along the wall via `MapDef.PointAlong`, motes, sparks on
  `Portal.Uses`). Team Play text trimmed, its boost 50% (`PassSpeedBoost`). "Useless player" badge is now "Tried their
  best". **Card stats:** `Game/CardStats.cs` logs this PC's people's cards per online match (`NotePicks` in
  GameRunner, once a second in play, most of each card seen per "MatchId:Seed") to `card-stats.dat` in the save folder,
  plus a `CardStatsTable` of everyone met (per person id: Steam id or a random PC id). In the lobby
  (`Msg.CardStats` = 15, every 3 s) clients send their table to the host, the host sends its merged table to
  everyone; merging keeps the bigger count per person and card and never touches this PC's own rows. Main menu
  "Cards" opens `CardScreen.OpenBrowser`: the free-play grid, read-only, sorted by your picks / everyone's / A–Z
  (confirm or LB/RB or clicking the sort bar), each card showing its place, your picks and everyone's share.
  Screenshot runs never save (`CardStats.NoSave`). Screenshot scenes `portal` (also takes `faceoff`) and
  `browse`. **Version 63.**
- 0.1.48 (Claude, 22 Nov 2026): **Puck Gun on Triangle.** It fires on the utility button
  (Triangle / Y) instead of charge / brake, when you don't have the puck, a rock or a teammate; charge / brake only
  brakes now. `GunCooldown` 0.85 s (was 1), plus `GunBotExtra` 3 s on a body the bot runs. Bots press utility for it.
  **Version 62.**
- 0.1.47 (Claude, 22 Nov 2026): **your cards go with you when you swap.** `Cards.Recompute`
  gives a body the cards of whoever's steering it: a person on a bot teammate (Pilot) gives that body their cards and
  their own (bot-run) body gets the bot's, size and weight included (the bodies resize). `Simulation.Route` recomputes
  whenever any Pilot changes, and `RockOwners` hands a Swap rock to the teammate body that now has the Swap card.
  Cards stay stored on the person's own player (`Cards`), so picks and the card screen are unchanged. Cooldowns and
  effects already running stay with their body. **Version 61.**
- 0.1.47 (Claude, 22 Nov 2026): **bots play as a team and go round nets.** Passing: an open
  teammate is one with no opponent within 1.6 (was: no opponent nearer them than the passer, which in 2v2 was almost
  never), lane a bit narrower, up to 22 long (was 15), level or a little back is fine, any open teammate when under
  pressure, not straight back to whoever just passed (`Touch2`) unless pressed, and a teammate with a much better look
  gets it; otherwise a pass about 2 times in 3 when someone's open. Quick shots (no charge) when within 10 of the net,
  an opponent within 4, or with Power Shot / Invisible Shot, lined up with a clear lane. 2v2 bots over 3 minutes went
  from 3 passes and ~10 quick shots to ~29 and ~43 (`BotsPassAndTakeQuickShots`). Nets: Hockey and Big Rink goals
  now have `facing` (the maps and `make_hockey_rink.py`), so bots skate round to the mouth; `Bot.OutOfNets` moves any
  target inside a net out in front of it, `Bot.RoundNets` heads for the best corner round a net blocking the way. A
  puck held behind a Hockey Rink net: bots reach it 6/6 (they never went round before). `RoundNets` only on rinks
  without a route, and only for a net actually in the way (`CrossesBox`): on the U Rink it had made red win 70-42 over
  20 bot matches (now 114-106 over 40; Hockey 69-73 over 20; S 95-76 over 40, likely noise, worth a look). Chasing
  the puck from more than 5 away (8 if an opponent has it) a bot holds dash with `DashMove`, chaining dashes: 30 units
  in 2.55 s instead of ~4. Toggle cards page: All on / All off rows and an "N of M on" heading. **Version 60.**
- 0.1.46 (Claude, 21 Nov 2026): **anyone recolours their side; Shockwave looks.** Players
  screen: D-pad up / down (arrow keys on a keyboard; never the stick) steps your side to the next colour of all 8
  (skipping the other side's), for everyone on it, bots too (`LocalPlayers.RecolourSide`); left / right now moves you
  to the other side's colour instead of stepping red / blue. Online anyone can: a client sends `Msg.Recolour` (14), the
  host's `NetSession.RecolourSide` changes its own players, every client's players and the bots, and every lobby
  message carries the side colours plus a `Recolours` count; on a new count each PC puts its players in their seat's
  side colour (GameRunner), so a lost message can't leave anyone behind. Shockwave: held, a see-through solid blue disc
  grows round you; it goes off as a red ring. **Version 59.** The D-pad colour change didn't work for the owner in a
  real lobby, so it's switched off (`LocalPlayers.TeamColourButtons` = false, hint hidden); the online plumbing stays.
- 0.1.46 (Claude, 21 Nov 2026): **rock lasts 10 s; easier Team Play passes; brighter
  arrows.** Swap rock: `Rock.Life` = `RockLife` 10 s from the throw (held or not), then it's gone; it no longer vanishes
  when it stops. In the last `RockFlash` 3 s it flashes dark (a black cover, `RockViews.FlashDim`: ~1.5 a second rising
  to 8). Passes (Team Play, goal credit) no longer end when an opponent merely comes near the puck: only an opponent
  picking it up ends one. Speed-up arrows: bigger (0.3 radius), solid white most of their drift, thin dark edge.
  **Version 58.**
- 0.1.45 (Claude, 21 Nov 2026): **Shockwave grows from nothing.** `ShockMin` 0 (was 1.5): it
  grows from the player out to 6 units over 6 s (it reaches anyone whose edge is inside it). **Version 57.**
- 0.1.44 (Claude, 21 Nov 2026): **Shockwave held to grow; Stone Wall goes on after
  bounces; Heavyweight bigger.** Shockwave has no cooldown: holding the utility button grows it (`PlayerState.ShockHold`)
  and letting go fires it, radius `ShockMin` 1.5 to `ShockMax` 6 units over `ShockChargeTime` 6 s
  (`Simulation.ShockRadius`); push still +10% a copy. A thin ring shows the size while held (`PlayerView.ShockCharge`),
  the cooldown bar shows the charge. Bots hold until it's just big enough to reach the nearest foe. Stone Wall: when
  the puck turns (boards, a body) or a piece would cross the boards, a fresh piece starts behind it instead of stopping
  (still 30 long / 3 s at most); each piece stands 5.5 s. Heavyweight Size x1.16 (was x1.13). **Version 56.**
  Checked: 149 tests (144 pass, 5 skipped).
- 0.1.44 (Claude, 21 Nov 2026): **match settings add and remove bots.** Each team has a
  "Red bots" / "Blue bots" row: right adds one, left takes the last one off. Host command `PlayerInput.RemoveBot` +
  player (208..215), only ever a bot; online the host picks the player when the command is sent (`OnlineBotToRemove`:
  the last bot on that team with nothing but bots after it, so no person's number moves) and `NetSession.BotLeftMatch`
  drops its lobby bot and seat. `Simulation.RemovePlayer` now shifts every player reference (carrier, pass, touches,
  goal / assist, Cannon Ball, Shoved, rocks, shots). **NetSession.Version 53.** Checked: 147 tests (142 pass, 5
  skipped) incl. an online test where both PCs drop the bot and stay identical. Stone Wall stands 6 s (`StoneLife`, was 4). Carrying a teammate (holding charge) has no charging slow-down. **Version 55.**
- 0.1.44 (Claude, 21 Nov 2026): **Friendly Fire: hold to grab, throw like a shot.** A teammate
  is only picked up while charge/brake is held (`GrabHeld`, this tick's buttons; bots don't hold it, so they no longer
  grab by accident); letting go throws them (charged), Shoot throws quick. The throw is exactly a shot's speed (no more
  half speed / cap 30); they fly with `MateTossDrag` (the puck's 7/11) until down to `TossEndSpeed` 8 (or `TossTime` 3 s),
  then get +15% speed for 4 s (`MateBoost`, orange arrows). `TossDrag` is now only Cannon Ball's glide. Card has a face
  row and says hold charge. Invisible Shot hides the puck 0.5 s (`GhostShotTime`, was 1.5). Speed-up arrows are white (not orange). Friendly
  Fire's face has a Duration 4 s row; Swap's text is "Throw a rock and then swap places with it!". **NetSession.Version 52.**
  Checked: 145 tests (140 pass, 5 skipped).
- 0.1.43 (Claude, 21 Nov 2026): **Swap rock glides less and can't be pumped; Ice Trail 15 s,
  +60%; speed arrows.** `RockDrag` 0.18 -> 0.277 (35% less glide) -> 0.326 (15% less again; glide goes as 1 / drag).
  Swapping: you take the rock's momentum, the rock keeps its own (it used to take yours, so spam swapping sped it up).
  Ice Trail: `IceLife` 15, `IceMeltPace` 1 (every bit lasts 15 s); teammates on it get `IceBoost` +60% (enemies still
  -10% a copy). Sped up now shows three orange up arrows (like the slow arrows) instead of the orange ring; the slow
  arrows are solid black. **NetSession.Version 50.** Checked: 143 tests (138 pass, 5 skipped), Windows build, stone shot.
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
