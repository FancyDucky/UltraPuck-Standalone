using UltraPuck.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace UltraPuck.Game
{
    /// <summary>
    /// Text on screen: the lobby (main menu), the score and match messages. Everything scales with the window and
    /// the UI size setting (in Settings, or [ and ] on the keyboard).
    /// </summary>
    internal sealed class Hud : MonoBehaviour
    {
        private bool _lobby, _free, _covered;
        /// <summary>A line under the score (online: ping, waiting, or a desync warning), or null.</summary>
        public string Banner;
        private LocalPlayers _local;
        private GameState _state;
        private Simulation _sim;
        private string _error;

        /// <param name="covered">A menu or the card screen is up: keep the score and messages out of the way.</param>
        public void Show(bool lobby, bool free, LocalPlayers local, GameState state, Simulation sim, string error, bool covered)
        {
            _lobby = lobby; _free = free; _local = local; _state = state; _sim = sim; _error = error; _covered = covered;
            var kb = Keyboard.current;
            if (kb != null && (kb.leftBracketKey.wasPressedThisFrame || kb.rightBracketKey.wasPressedThisFrame))
                Settings.UiScale += kb.rightBracketKey.wasPressedThisFrame ? 0.1f : -0.1f;
        }

        private void OnGUI()
        {
            GUI.depth = 10;
            Ui.Begin();
            float w = Ui.W, h = Ui.H;
            if (!string.IsNullOrEmpty(_error)) { Ui.Label(new Rect(0, h / 2 - 40, w, 80), _error, Ui.Font(24, true, TextAnchor.MiddleCenter), Color.white); return; }
            if (_lobby) { if (!_covered) Lobby(w, h); return; }
            if (_state == null || _covered) return;

            // score: team name, symbol and colour, so it reads without relying on colour
            var score = Palette.TeamName(0) + "    " + _state.Score[0] + "   :   " + _state.Score[1] + "    " + Palette.TeamName(1);
            Ui.Box(new Rect(w / 2 - 230, 12, 460, 50), new Color(0f, 0f, 0f, 0.55f), 14f);
            Ui.Swatch(new Rect(w / 2 - 216, 24, 26, 26), 0);
            Ui.Swatch(new Rect(w / 2 + 190, 24, 26, 26), 1);
            Ui.Label(new Rect(w / 2 - 190, 12, 380, 50), score, Ui.Font(23, true, TextAnchor.MiddleCenter), Color.white);

            if (!string.IsNullOrEmpty(Banner))
            {
                var warn = Banner.StartsWith("Desync") || Banner.StartsWith("Waiting");
                Ui.Label(new Rect(0, 64, w, 26), Banner, Ui.Font(15, warn, TextAnchor.MiddleCenter), warn ? new Color(1f, 0.6f, 0.5f) : new Color(1f, 1f, 1f, 0.5f));
            }

            if (_free)
            {
                Ui.Label(new Rect(0, 64, w, 26), "FREE PLAY", Ui.Font(15, true, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, 0.55f));
                Ui.Label(new Rect(0, h - 36, w, 28),
                    "View / R: puck back      Start / Esc: menu (bots, pick a card, settings)      A / Space on a new controller: join",
                    Ui.Font(15, false, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, 0.6f));
            }

            var big = Ui.Font(46, true, TextAnchor.MiddleCenter);
            switch (_state.Phase)
            {
                case Phase.Countdown:
                    if (!_free) Ui.Label(new Rect(0, h * 0.28f, w, 70), _state.Score[0] + _state.Score[1] == 0 ? "Ready..." : "Face-off", big, Color.white);
                    break;
                case Phase.Scored:
                    Ui.Label(new Rect(0, h * 0.28f, w, 70), "GOAL!  " + Palette.TeamName(_state.LastScorer) + " scores", big, Palette.Team(_state.LastScorer));
                    break;
                case Phase.GameOver:
                    Ui.Box(new Rect(w / 2 - 330, h * 0.28f, 660, 150), Ui.Panel, 20f);
                    Ui.Label(new Rect(0, h * 0.28f + 14, w, 70), Palette.TeamName(_state.LastScorer) + " wins!", big, Palette.Team(_state.LastScorer));
                    Ui.Label(new Rect(0, h * 0.28f + 90, w, 40), "Start / Enter: play again      B / Esc: main menu", Ui.Font(18, false, TextAnchor.MiddleCenter), Ui.TextDim);
                    break;
            }
        }

        private void Lobby(float w, float h)
        {
            Ui.Box(new Rect(w / 2 - 370, 30, 740, 214), Ui.Panel, 22f);
            Ui.Label(new Rect(0, 44, w, 60), "ULTRA PUCK", Ui.Font(50, true, TextAnchor.MiddleCenter), Color.white);
            var small = Ui.Font(18, false, TextAnchor.MiddleCenter);
            Ui.Label(new Rect(0, 106, w, 28), "Join: A on a controller, or Space on the keyboard   (up to " + LocalPlayers.MaxPlayers + ")", small, Ui.Text);
            Ui.Label(new Rect(0, 134, w, 28), "Left / right: pick a side      B / Esc: leave", small, Ui.TextDim);
            Ui.Label(new Rect(0, 162, w, 28), _local.Slots.Count > 0 ? "Start / Enter: play a match  (first to " + (_sim != null && _sim.T.GoalsToWin > 0 ? _sim.T.GoalsToWin : 5) + ", cards after every goal)" : "Waiting for players...", small, Ui.Text);
            Ui.Label(new Rect(0, 190, w, 28), "Y / F: free play      View / Tab: settings", small, Ui.Text);

            var y = h - 104 - _local.Slots.Count * 36;
            if (_local.Slots.Count > 0) Ui.Box(new Rect(w / 2 - 270, y - 12, 540, _local.Slots.Count * 36 + 22), new Color(0f, 0f, 0f, 0.55f), 14f);
            for (var i = 0; i < _local.Slots.Count; i++)
            {
                var s = _local.Slots[i];
                Ui.Swatch(new Rect(w / 2 - 252, y + i * 36 + 5, 24, 24), s.Team);
                Ui.Label(new Rect(w / 2 - 216, y + i * 36, 460, 34), "P" + (i + 1) + "   " + s.Label + "   ·   " + Palette.TeamName(s.Team) + " (" + Palette.SymbolName(s.Team) + ")", Ui.Font(18), Color.white);
            }

            var hint = Ui.Font(15, false, TextAnchor.MiddleCenter);
            Ui.Label(new Rect(0, h - 58, w, 24),
                "Controller: left stick move, right stick aim.  A / Cross / RB: shoot or check.  B / Circle / LB: charge shot or brake.  Y / Triangle: utility",
                hint, new Color(1f, 1f, 1f, 0.6f));
            Ui.Label(new Rect(0, h - 34, w, 24),
                "Keyboard: WASD move, arrows aim.  Space: shoot or check.  Shift: charge shot or brake.  E: utility.     [ ] UI size",
                hint, new Color(1f, 1f, 1f, 0.6f));
        }
    }
}
