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
        /// <summary>Full-screen wipe into / out of a goal replay (drawn on top of the match).</summary>
        public ReplayTransition Wipe;
        /// <summary>A player's name (for the corner card list).</summary>
        public System.Func<int, string> PlayerName;
        /// <summary>Draw a mouse reticle (keyboard players aiming with the mouse).</summary>
        public bool Reticle;

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
            if (Reticle) DrawReticle();
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
            if (_state.Paused)
            {
                // online: the host has paused (they see their own menu; everyone else sees this)
                Ui.Box(new Rect(0, 0, w, h), new Color(0f, 0f, 0f, 0.35f), 0f);
                Ui.Label(new Rect(0, h * 0.4f - 40f, w, 70), "Paused", big, Color.white);
                Ui.Label(new Rect(0, h * 0.4f + 26f, w, 30), "The host paused the game", Ui.Font(20, false, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, 0.7f));
            }
            switch (_state.Phase)
            {
                case Phase.Countdown:
                    if (!_free)
                    {
                        // 3, 2, 1: each number pops in big and shrinks back as its second runs out
                        var left = _state.PhaseTime.ToFloat();
                        var n = Mathf.Max(1, Mathf.CeilToInt(left - 0.001f));
                        var part = Mathf.Clamp01(n - left); // 0 as the number appears, 1 as it ends
                        var size = Mathf.RoundToInt(Mathf.Lerp(120f, 84f, Ui.EaseOut(part / 0.35f)));
                        var a = 1f - Mathf.Clamp01((part - 0.8f) / 0.2f) * 0.6f;
                        Ui.Label(new Rect(0, h * 0.28f - 40f, w, 140), n.ToString(), Ui.Font(size, true, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, a));
                        Ui.Label(new Rect(0, h * 0.28f + 96f, w, 40), _state.Score[0] + _state.Score[1] == 0 ? "Get ready" : "Face-off", Ui.Font(24, true, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, 0.75f));
                    }
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
            if (Wipe != null && Wipe.Active) Wipe.Draw();
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

        /// <summary>A target under the mouse for keyboard aiming (system cursor is hidden).</summary>
        private void DrawReticle()
        {
            if (Event.current.type != EventType.Repaint) return;
            var m = Mouse.current;
            if (m == null) return;
            var sp = m.position.ReadValue();
            var k = Screen.height / 720f * Settings.UiScale;
            var p = new Vector2(sp.x / k, (Screen.height - sp.y) / k);
            var team = 0;
            if (_local != null)
                foreach (var s in _local.Slots) if (!s.Demo) { team = s.Team; break; }
            var c = Settings.ReticleTint(team); c.a = 0.92f;
            var dim = new Color(0f, 0f, 0f, 0.35f);
            var scale = 0.7f + Settings.ReticleSize * 0.9f; // ~0.7 .. 1.6
            var r = 14f * scale;
            var gap = 5f * scale;
            var arm = 10f * scale;
            var thick = Mathf.Max(1.5f, 2f * scale);
            var style = Settings.ReticleStyle;
            if (style == 0 || style == 1 || style == 2)
            {
                // soft shadow ring (crosshair, circle, dot)
                Ui.Box(new Rect(p.x - r - 1f, p.y - r - 1f, (r + 1f) * 2f, (r + 1f) * 2f), dim, r + 1f);
            }
            if (style == 0 || style == 1)
                Ui.Box(new Rect(p.x - r, p.y - r, r * 2f, r * 2f), new Color(c.r, c.g, c.b, 0.18f), r);
            if (style == 0 || style == 3)
            {
                Ui.Box(new Rect(p.x - arm - gap, p.y - thick * 0.5f, arm, thick), c, 1f);
                Ui.Box(new Rect(p.x + gap, p.y - thick * 0.5f, arm, thick), c, 1f);
                Ui.Box(new Rect(p.x - thick * 0.5f, p.y - arm - gap, thick, arm), c, 1f);
                Ui.Box(new Rect(p.x - thick * 0.5f, p.y + gap, thick, arm), c, 1f);
            }
            if (style == 0 || style == 2)
            {
                var d = style == 2 ? 4f * scale : 1.5f * scale;
                Ui.Box(new Rect(p.x - d, p.y - d, d * 2f, d * 2f), c, d);
            }
            else if (style == 1)
                Ui.Box(new Rect(p.x - 1.5f * scale, p.y - 1.5f * scale, 3f * scale, 3f * scale), c, 1.5f * scale);
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
