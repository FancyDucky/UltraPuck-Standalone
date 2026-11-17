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

        /// <summary>A name floating over a player (filled in by GameRunner each frame).</summary>
        public struct Tag { public Vector3 World; public string Name; public int Team; }
        public readonly System.Collections.Generic.List<Tag> Tags = new System.Collections.Generic.List<Tag>();
        public Camera Cam;
        /// <summary>The local lobby: the rink being played on (null when there's only one).</summary>
        public string MapName;
        /// <summary>A goal replay is on screen (and whether A skips it, and whether it's in slow motion right now).</summary>
        public bool Replay, ReplaySkippable, ReplaySlow;
        /// <summary>A player's name (for the corner card list).</summary>
        public System.Func<int, string> PlayerName;

        /// <param name="covered">A menu or the card screen is up: keep the score and messages out of the way.</param>
        public void Show(bool lobby, bool free, LocalPlayers local, GameState state, Simulation sim, string error, bool covered)
        {
            _lobby = lobby; _free = free; _local = local; _state = state; _sim = sim; _error = error; _covered = covered;
            var kb = Keyboard.current;
            if (kb != null && !Menus.Typing && !LocalPlayers.Blocked && (kb.leftBracketKey.wasPressedThisFrame || kb.rightBracketKey.wasPressedThisFrame))
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
            NameTags();
            CornerCards(h);

            // score: team name, symbol and colour, so it reads without relying on colour
            var score = Palette.TeamName(0) + "    " + _state.Score[0] + "   :   " + _state.Score[1] + "    " + Palette.TeamName(1);
            Ui.Box(new Rect(w / 2 - 230, 12, 460, 50), new Color(0f, 0f, 0f, 0.55f), 14f);
            Ui.Swatch(new Rect(w / 2 - 216, 24, 26, 26), 0);
            Ui.Swatch(new Rect(w / 2 + 190, 24, 26, 26), 1);
            Ui.Label(new Rect(w / 2 - 190, 12, 380, 50), score, Ui.Font(23, true, TextAnchor.MiddleCenter), Color.white);
            // a timed match: the clock under the score (overtime: next goal wins)
            if (_sim != null && _sim.T.TimeLimit > Fix.Zero && !_free)
            {
                var left = Mathf.CeilToInt(_state.Clock.ToFloat());
                var clock = _state.Overtime ? "OVERTIME  ·  next goal wins" : (left / 60) + ":" + (left % 60).ToString("00");
                var hurry = !_state.Overtime && left <= 10;
                Ui.Box(new Rect(w / 2 - 110, 64, 220, 30), new Color(0f, 0f, 0f, 0.55f), 10f);
                Ui.Label(new Rect(w / 2 - 110, 64, 220, 30), clock, Ui.Font(18, true, TextAnchor.MiddleCenter), hurry || _state.Overtime ? new Color(1f, 0.55f, 0.45f) : Color.white);
            }

            if (!string.IsNullOrEmpty(Banner))
            {
                var warn = Banner.StartsWith("Desync") || Banner.StartsWith("Waiting");
                Ui.Label(new Rect(0, 64, w, 26), Banner, Ui.Font(15, warn, TextAnchor.MiddleCenter), warn ? new Color(1f, 0.6f, 0.5f) : new Color(1f, 1f, 1f, 0.5f));
            }

            if (_free)
            {
                Ui.Label(new Rect(0, 64, w, 26), "FREE PLAY", Ui.Font(15, true, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, 0.55f));
                Glyphs.Hint(new Rect(0, h - 36, w, 28),
                    "{reset} Puck back      {menu} Menu (bots, pick a card, settings)      " + (InputMode.Pad ? "{join} on another controller: join" : "{join} Join on the keyboard"),
                    15, new Color(1f, 1f, 1f, 0.6f));
            }

            var big = Ui.Font(46, true, TextAnchor.MiddleCenter);
            switch (_state.Phase)
            {
                case Phase.Countdown:
                    if (!_free) Ui.Label(new Rect(0, h * 0.28f, w, 70), _state.Score[0] + _state.Score[1] == 0 ? "Ready..." : "Face-off", big, Color.white);
                    break;
                case Phase.Scored:
                    if (Replay)
                    {
                        // a "REPLAY" badge that pulses, and a bar top and bottom like a broadcast
                        Ui.Box(new Rect(0, 0, w, 10), new Color(0f, 0f, 0f, 0.6f), 0f);
                        Ui.Box(new Rect(0, h - 10, w, 10), new Color(0f, 0f, 0f, 0.6f), 0f);
                        var pulse = 0.7f + 0.3f * Mathf.Sin(Time.unscaledTime * 6f);
                        Ui.Box(new Rect(24, 80, 150, 40), new Color(0.85f, 0.15f, 0.15f, 0.9f * pulse), 8f);
                        Ui.Label(new Rect(24, 80, 150, 40), "REPLAY", Ui.Font(22, true, TextAnchor.MiddleCenter), Color.white);
                        if (ReplaySlow) Ui.Label(new Rect(184, 80, 300, 40), "slow motion", Ui.Font(17, false), new Color(1f, 1f, 1f, 0.7f));
                        if (ReplaySkippable)
                        {
                            // everyone has to want to skip it (bots always do)
                            int votes = 0, people = 0;
                            foreach (var p in _state.Players) { if (p.Bot) continue; people++; if (p.SkipVote) votes++; }
                            Glyphs.Hint(new Rect(24, h - 50, 500, 28), "{shoot} Skip" + (people > 1 ? "   " + votes + " / " + people : votes > 0 ? "   skipping" : ""), 16, new Color(1f, 1f, 1f, 0.75f), TextAnchor.MiddleLeft);
                        }
                    }
                    else Ui.Label(new Rect(0, h * 0.28f, w, 70), "GOAL!  " + Palette.TeamName(_state.LastScorer) + " scores", big, Palette.Team(_state.LastScorer));
                    break;
                case Phase.GameOver:
                    Ui.Box(new Rect(w / 2 - 330, h * 0.28f, 660, 150), Ui.Panel, 20f);
                    Ui.Label(new Rect(0, h * 0.28f + 14, w, 70), Palette.TeamName(_state.LastScorer) + " wins!", big, Palette.Team(_state.LastScorer));
                    Glyphs.Hint(new Rect(0, h * 0.28f + 90, w, 40), "{start} Play again      {back} Main menu", 18, Ui.TextDim);
                    break;
            }
        }

        private void Lobby(float w, float h)
        {
            Ui.Box(new Rect(w / 2 - 370, 30, 740, 214), Ui.Panel, 22f);
            Ui.Label(new Rect(0, 44, w, 60), "ULTRA PUCK", Ui.Font(50, true, TextAnchor.MiddleCenter), Color.white);
            var small = Ui.Font(18, false, TextAnchor.MiddleCenter);
            Glyphs.Hint(new Rect(0, 106, w, 28), "{join} Join   (up to " + LocalPlayers.MaxPlayers + " players)", 18, Ui.Text);
            Glyphs.Hint(new Rect(0, 134, w, 28), "{side} Pick a side      {leave} Leave your seat      Hold {leave} Main menu", 18, Ui.TextDim);
            if (_local.Slots.Count > 0) Glyphs.Hint(new Rect(0, 162, w, 28), "{start} Play a match  (first to " + (_sim != null && _sim.T.GoalsToWin > 0 ? _sim.T.GoalsToWin : 5) + ", cards after every goal)", 18, Ui.Text);
            else Ui.Label(new Rect(0, 162, w, 28), "Waiting for players...", small, Ui.Text);
            Glyphs.Hint(new Rect(0, 190, w, 28), "{freeplay} Free play      {settings} Settings" + (MapName != null ? "      {tabs} Rink: " + MapName : ""), 18, Ui.Text);
            LeavingBar(new Rect(w / 2 - 150, 252, 300, 26), _local.NobodyLeaving, "Back to the main menu");

            var y = h - 104 - _local.Slots.Count * 36;
            if (_local.Slots.Count > 0) Ui.Box(new Rect(w / 2 - 270, y - 12, 540, _local.Slots.Count * 36 + 22), new Color(0f, 0f, 0f, 0.55f), 14f);
            for (var i = 0; i < _local.Slots.Count; i++)
            {
                var s = _local.Slots[i];
                Ui.Swatch(new Rect(w / 2 - 252, y + i * 36 + 5, 24, 24), s.Team);
                Ui.Label(new Rect(w / 2 - 216, y + i * 36, 460, 34), "P" + (i + 1) + "   " + NameOf(i, s) + "   ·   " + Palette.TeamName(s.Team), Ui.Font(18), Color.white);
            }

            Glyphs.Hint(new Rect(0, h - 40, w, 28),
                "{move} Move    {aim} Aim    {shoot} Shoot / check    {charge} Charge / brake    {utility} Utility" + (InputMode.Pad ? "" : "      [ ] UI size"),
                16, new Color(1f, 1f, 1f, 0.7f));
        }

        /// <summary>The name shown for a local lobby seat: yours for the first person, then P2, P3... (not the controller's model).</summary>
        private string NameOf(int i, LocalPlayers.Slot s)
        {
            if (s.Demo) return "Bot";
            for (var k = 0; k < i; k++) if (!_local.Slots[k].Demo) return "Guest";
            return Settings.PlayerName;
        }

        /// <summary>A bar that fills while B / Escape is held to leave.</summary>
        public static void LeavingBar(Rect r, float t, string what)
        {
            if (t <= 0.02f) return;
            Ui.Box(r, new Color(0f, 0f, 0f, 0.6f), r.height / 2f);
            Ui.Box(new Rect(r.x, r.y, r.width * Mathf.Clamp01(t), r.height), new Color(0.9f, 0.3f, 0.3f, 0.8f), r.height / 2f);
            Ui.Label(r, what + "...", Ui.Font(14, true, TextAnchor.MiddleCenter), Color.white);
        }

        /// <summary>Everyone's cards, small, in the lower-left corner (Settings > Gameplay, or View / Tab in a match).</summary>
        private void CornerCards(float h)
        {
            if (!Settings.CornerCards || _sim == null || _sim.Cards == null) return;
            var lines = new System.Collections.Generic.List<int>();
            for (var i = 0; i < _state.Players.Length; i++) if (_state.Players[i].Cards.Length > 0) lines.Add(i);
            if (lines.Count == 0) return;
            var style = Ui.Font(13);
            var y = h - 46f - lines.Count * 20f;
            foreach (var i in lines)
            {
                var p = _state.Players[i];
                var names = new System.Text.StringBuilder();
                // the same card more than once: "Speed Up x2"
                var seen = new System.Collections.Generic.List<int>();
                foreach (var c in p.Cards)
                {
                    if (seen.Contains(c)) continue;
                    seen.Add(c);
                    var n = 0;
                    foreach (var d in p.Cards) if (d == c) n++;
                    if (names.Length > 0) names.Append(",  ");
                    names.Append(_sim.Cards.Cards[c].Name);
                    if (n > 1) names.Append(" x" + n);
                }
                var who = PlayerName != null ? PlayerName(i) : "P" + (i + 1);
                Ui.Swatch(new Rect(14f, y + 3f, 14f, 14f), p.Team);
                Ui.Label(new Rect(34f, y, 900f, 20f), "<b>" + who + "</b>   " + names, style, new Color(1f, 1f, 1f, 0.75f));
                y += 20f;
            }
        }

        /// <summary>Names over the players, so it's easy to tell who is who.</summary>
        private void NameTags()
        {
            if (!Settings.NameTags || Cam == null || Event.current.type != EventType.Repaint) return;
            var k = Screen.height / 720f * Settings.UiScale;
            // small, plain text just above the check and cooldown bars (no box: it shouldn't cover anything)
            var style = Ui.Font(12, true, TextAnchor.LowerCenter);
            foreach (var t in Tags)
            {
                if (string.IsNullOrEmpty(t.Name)) continue;
                var sp = Cam.WorldToScreenPoint(t.World);
                var p = new Vector2(sp.x / k, (Screen.height - sp.y) / k);
                Ui.Label(new Rect(p.x - 100f, p.y - 18f, 200f, 18f), t.Name, style, new Color(1f, 1f, 1f, 0.85f));
            }
        }
    }
}
