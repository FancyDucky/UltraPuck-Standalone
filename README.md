# Ultra Puck

A top-down team puck brawler for local and online multiplayer, with ROUNDS-style cards, mod support and a map editor planned. Made in Unity 6 (6000.6.9f1).

## Playing (milestone 1: local)
- **Join:** A on a controller, or Space on the keyboard. Up to 4 players.
- **Lobby:** left / right picks a side, B / Esc leaves, Start / Enter starts the match. First to 5 goals wins.

The same buttons on Xbox and PlayStation pads.

| | Controller | Keyboard |
|---|---|---|
| Move | Left stick / D-pad | WASD |
| Aim (optional): face where the stick points; let go and you face where you move | Right stick | Arrow keys |
| Shoot / check: with the puck, shoot now; without it, check (dash into people, knocks the puck loose and dazes them) | A / Cross, or RB / R1 | Space / J |
| Charge / brake: with the puck, hold to charge and let go to fire; without it, brake | B / Circle, or LB / L1 | Left Shift / K |
| Utility (for power-up cards later) | Y / Triangle | E / L |
| Pause menu | Start / Options | Esc |
| UI size | | [ and ] |

- You can't check while you have the puck. The bar above a player is their check recharging.
- Checking into a loose puck picks it up. Checking an opponent who has the puck, or the puck they're carrying, takes it off them.
- The camera follows the action, zooming out as players spread and in as they bunch up. "Whole rink" in the pause menu turns that off.
- Hard hits squash and flash both players, burst a ring and shake the screen. Screen shake can be turned off in the pause menu.

### Online
From the main menu, **Online**:
- **Host on Steam / Find games on Steam:** through Steam's relay network, so no port forwarding. Invite friends from the lobby (Steam overlay), or they join from your Steam friends list. Both players need Steam running and logged in, on different accounts. Until the game has its own Steam app id, it uses Valve's test app 480 (`steam_appid.txt`), so Steam shows you as playing "Spacewar".
- **Host by IP / Join by IP:** plain UDP on port 47777. Use 127.0.0.1 for two copies on one PC, the host's 192.168... address on a home network, or a Tailscale / ZeroTier address across the internet.
- In the online lobby, people on each PC join with A / Space and pick sides as usual, so couch and online mix. The host adds bots and starts the match.

How it works:
- **Rollback netcode** (`Core/Net`). Every PC runs the whole game, and your own controls apply after 3 ticks (25 ms).
- **Prediction:** other players' controls are guessed until they arrive. A wrong guess is fixed by rewinding to that tick and replaying, all within one frame.
- **Resending:** controls are re-sent until confirmed, so lost packets don't matter.
- **Through the host:** everything goes through the host.
- **Time sync:** PCs that get ahead slow down slightly.
- **Desync check:** every second, the PCs compare a fingerprint of the game, so a desync shows at once.
- **Same files:** players with different map or card files are refused, because the games would drift apart.

### Cards
After every goal in a match, each player on the team that conceded picks one of three cards (ROUNDS style). Cards last the whole match.
- Cards are JSON in `Assets/StreamingAssets/Cards/*.json`, with art in `Cards/Art/<id>.jpg` or `.png`. Every file there is read in name order, so mods can add cards by dropping in a file. The format is described at the top of `ultrapuck.json`.
- Ported from the mod so far: the cards that only change numbers (Double Dash, Speed Up, Power Shot, Quick Recharge, Heavyweight, Featherweight, Over Charge, Slow Poke). Cards that need new mechanics come next.
- The pick screen: left / right (or the mouse) to look, the right stick (or mouse) tilts the card, A / Space / click takes it. Higher-tier cards have holo foil, sparkle and etched frames (Settings > Graphics > Card effects turns them off).

### Menus and settings
- **Pause (Start / Esc):** Resume, Settings, Main menu; in free play also Bots and Pick a card.
- **Settings** (pause menu, or View / Tab in the lobby), tabs switch with LB / RB or Q / E:
  - Graphics: window mode, V-Sync, frame rate limit, UI size, puck trail, card effects.
  - Gameplay: screen shake amount, hit effects, check bar (while recharging / always).
  - Camera: follow the action or whole rink, auto-camera zoom (close to wide), camera smoothing.

### Free play
Practice with no score limit. In the lobby, press **Y** on a controller or **F** on the keyboard (you join automatically if you haven't yet).
- Goals still count, but only the puck goes back to the middle; everyone stays where they are.
- **View (Select) / R:** bring the puck back to the middle.
- **Pause menu (Start / Esc):** number of computer players (left / right), screen shake, back to the lobby.
- **A / Space** on a controller or keyboard that isn't playing yet: join in.

## How it's built
- **`Assets/UltraPuck/Core`:** the game rules, in plain C# with no Unity code.
  - All maths is fixed-point (`Fix`, `FixVec2`), so the same inputs give exactly the same result on every PC. That is what rollback online play needs.
  - `Simulation.Step(state, inputs)` advances one 60 Hz tick.
  - `GameState.Clone()` is a full save of the moment, and `Checksum()` catches two PCs drifting apart.
  - `Stats` holds the per-player numbers that cards will change.
  - `Tuning` holds the game's numbers.
- **`Assets/UltraPuck/Game`:** the Unity side.
  - `GameRunner` runs the fixed ticks and draws smoothly between them.
  - `LocalPlayers` handles controllers and the keyboard.
  - `Views` holds the placeholder art.
  - `Hud` shows the text on screen.
- **Maps:** `Assets/StreamingAssets/Maps/*.json`. Walls are lists of points; the file also lists the goals, spawns and puck spot. See `test-rink.json`. The map editor will write the same format.
- **Accessibility:**
  - Every team has a shape (triangle / square) as well as a colour.
  - Team colours are from the colour-blind-safe Okabe-Ito set.
  - The UI scales with the window and with the UI size setting.

## Building and testing
- **Tests** (determinism, rollback, goals, walls):
  - In the editor: Window > General > Test Runner > EditMode.
  - From the command line:
    `Unity.exe -batchmode -projectPath . -runTests -testPlatform EditMode -testResults results.xml`
- **Windows build:**
  - In the editor: Ultra Puck > Build Windows.
  - From the command line:
    `Unity.exe -batchmode -quit -projectPath . -executeMethod UltraPuck.Editor.BuildScript.BuildWindows`
  - The game goes to `Builds/Windows/UltraPuck.exe`.
- **Demo:** set `ULTRAPUCK_DEMO=4` before starting the game, and 4 computer players play a match by themselves. Add `ULTRAPUCK_FREEPLAY=1` to start them in free play instead. Useful for checking things without controllers.
- **Screen tests:** `ULTRAPUCK_CARDTEST=1` opens the card pick screen over and over, choosing by itself; `ULTRAPUCK_MENUTEST=pause` or `=camera` opens that menu. For recording the screens without anyone at the controls.
- **Scripting backend:** Mono, not IL2CPP, so mods can be loaded as C# DLLs later.

## Next
1. Online play with rollback over Steam.
2. Card picking between goals.
3. Mod loading (cards and maps as data, C# mods).
4. Bots.
5. Map editor.
6. Menus and settings.
