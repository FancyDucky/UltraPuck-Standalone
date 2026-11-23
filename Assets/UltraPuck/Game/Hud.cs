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
        public struct Tag { public Vector3 World; public string Name; public int Team; public int Owner; }
        public readonly System.Collections.Generic.List<Tag> Tags = new System.Collections.Generic.List<Tag>();
        /// <summary>
        /// What a name tag shouldn't hide: each player (x, y, radius, player number) and the puck (player number -1),
        /// in the world. A tag over any of them (but its own player) fades out.
        /// </summary>
        public readonly System.Collections.Generic.List<Vector4> Under = new System.Collections.Generic.List<Vector4>();
        private readonly System.Collections.Generic.Dictionary<int, float> _tagAlpha = new System.Collections.Generic.Dictionary<int, float>();
        private float _tagsAt;
        private const float TagFaded = 0.22f, TagFull = 0.85f, TagFadeSpeed = 8f;
        public Camera Cam;
        /// <summary>The local lobby: the rink being played on (null when there's only one).</summary>
        public string MapName;
        /// <summary>A goal replay is on screen (and whether A skips it, and whether it's in slow motion right now).</summary>
        public bool Replay, ReplaySkippable, ReplaySlow;
        /// <summary>Full-screen wipe into / out of a goal replay (drawn on top of the match).</summary>
        public ReplayTransition Wipe;
        /// <summary>A player's name (for the corner card list).</summary>
        public System.Func<int, string> PlayerName;
        /// <summary>The name of a player's own seat (the person, even while they steer a bot): for goals and stats.</summary>
        public System.Func<int, string> SeatName;
        /// <summary>Draw a mouse reticle (keyboard players aiming with the mouse).</summary>
        public bool Reticle;
        /// <summary>When play last began after a countdown (for the START banner).</summary>
        public float StartAt = -10f;
        /// <summary>View / Tab is held in a match: the scoreboard (everyone's cards and stats) is up.</summary>
        public bool Board;

        private float _fpsTimer;
        private int _fpsFrames, _fpsShown;
        private GUIStyle _fpsStyle;

        /// <param name="covered">A menu or the card screen is up: keep the score and messages out of the way.</param>
        public void Show(bool lobby, bool free, LocalPlayers local, GameState state, Simulation sim, string error, bool covered)
        {
            _lobby = lobby; _free = free; _local = local; _state = state; _sim = sim; _error = error; _covered = covered;
            var kb = Keyboard.current;
            if (kb != null && !Menus.Typing && !LocalPlayers.Blocked && (kb.leftBracketKey.wasPressedThisFrame || kb.rightBracketKey.wasPressedThisFrame))
                Settings.UiScale += kb.rightBracketKey.wasPressedThisFrame ? 0.1f : -0.1f;
            // count once per Update (OnGUI runs more than once a frame)
            if (Settings.ShowFps)
            {
                _fpsFrames++;
                _fpsTimer += Time.unscaledDeltaTime;
                if (_fpsTimer >= 0.4f)
                {
                    _fpsShown = Mathf.RoundToInt(_fpsFrames / _fpsTimer);
                    _fpsFrames = 0;
                    _fpsTimer = 0f;
                }
            }
        }

        private void OnGUI()
        {
            GUI.depth = 10;
            Ui.Begin();
            float w = Ui.W, h = Ui.H;
            DrawFps();
            if (!string.IsNullOrEmpty(_error)) { Ui.Label(new Rect(0, h / 2 - 40, w, 80), _error, Ui.Font(24, true, TextAnchor.MiddleCenter), Color.white); return; }
            if (_lobby) { if (!_covered) Lobby(w, h); return; }
            if (Reticle) DrawReticle();
            if (_state == null || _covered) return;
            NameTags();
            if (Board && _state.Phase != Phase.GameOver) { Scoreboard(w, h); return; }
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
                    // after a card pick the countdown waits while the cards leave: nothing shown until it starts
                    if (!_free && (_sim == null || _state.PhaseTime <= _sim.T.CountdownTime))
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
                case Phase.Play:
                    if (!_free && Time.unscaledTime - StartAt < 0.9f)
                    {
                        // START: pops in big and fades out
                        var age = Time.unscaledTime - StartAt;
                        var size = Mathf.RoundToInt(Mathf.Lerp(130f, 96f, Ui.EaseOut(age / 0.3f)));
                        var a = 1f - Mathf.Clamp01((age - 0.55f) / 0.35f);
                        Ui.Label(new Rect(0, h * 0.28f - 40f, w, 140), "START!", Ui.Font(size, true, TextAnchor.MiddleCenter), new Color(1f, 0.85f, 0.25f, a));
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
                        // who scored, and who set it up
                        var line = GoalLine();
                        if (line != null)
                        {
                            // a backdrop just big enough for the names
                            var style = Ui.Font(16, true);
                            var tw = style.CalcSize(new GUIContent(line)).x;
                            Ui.Box(new Rect(24, 128, tw + 30f, 30), new Color(0f, 0f, 0f, 0.55f), 7f);
                            Ui.Box(new Rect(24, 128, 5, 30), Palette.Team(_state.LastScorer), 2.5f);
                            Ui.Label(new Rect(38, 128, tw + 10f, 30), line, style, Color.white);
                        }
                        if (ReplaySkippable)
                        {
                            // everyone has to want to skip it (bots always do)
                            int votes = 0, people = 0;
                            foreach (var p in _state.Players) { if (p.Bot) continue; people++; if (p.SkipVote) votes++; }
                            Glyphs.Hint(new Rect(24, h - 50, 500, 28), "{shoot} Skip" + (people > 1 ? "   " + votes + " / " + people : votes > 0 ? "   skipping" : ""), 16, new Color(1f, 1f, 1f, 0.75f), TextAnchor.MiddleLeft);
                        }
                    }
                    else
                    {
                        Ui.Label(new Rect(0, h * 0.28f, w, 70), "GOAL!  " + Palette.TeamName(_state.LastScorer) + " scores", big, Palette.Team(_state.LastScorer));
                        var line = GoalLine();
                        if (line != null) Ui.Label(new Rect(0, h * 0.28f + 66f, w, 34), line, Ui.Font(20, true, TextAnchor.MiddleCenter), Color.white);
                    }
                    break;
                case Phase.GameOver:
                    MatchStats(w, h, big);
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

        /// <summary>"Goal: Sam  ·  Assist: Alex" for the last goal, or null (an own goal).</summary>
        private string GoalLine()
        {
            if (_state == null || SeatName == null) return null;
            var by = _state.LastGoalBy;
            if (by < 0 || by >= _state.Players.Length) return null;
            var line = "Goal: " + SeatName(by);
            var a = _state.LastAssist;
            if (a >= 0 && a < _state.Players.Length) line += "   ·   Assist: " + SeatName(a);
            return line;
        }

        /// <summary>How much a player did, for MVP (and the least useful player): goals count most.</summary>
        private static int Rating(PlayerState p) { return p.Goals * 3 + p.Assists * 2 + p.Saves * 2 + p.Bumps; }

        /// <summary>After the match: who won, then everyone's goals, assists, saves and checks, the MVP and the least useful player.</summary>
        private void MatchStats(float w, float h, GUIStyle big)
        {
            var n = _state.Players.Length;
            const float rowH = 34f;
            var pw = Mathf.Min(760f, w - 40f);
            var ph = 190f + n * rowH;
            var x = w / 2f - pw / 2f;
            var y = Mathf.Max(70f, h / 2f - ph / 2f);
            Ui.Box(new Rect(x, y, pw, ph), Ui.Panel, 20f);
            Ui.Label(new Rect(x, y + 10f, pw, 64f), Palette.TeamName(_state.LastScorer) + " wins!", big, Palette.Team(_state.LastScorer));
            Ui.Label(new Rect(x, y + 66f, pw, 26f), _state.Score[0] + "  :  " + _state.Score[1], Ui.Font(20, true, TextAnchor.MiddleCenter), Ui.TextDim);

            // MVP: the best rating (more goals breaks a tie); the least useful: the lowest, if it isn't the MVP
            int mvp = -1, worst = -1;
            for (var i = 0; i < n; i++)
            {
                var p = _state.Players[i];
                if (mvp < 0 || Rating(p) > Rating(_state.Players[mvp]) || (Rating(p) == Rating(_state.Players[mvp]) && p.Goals > _state.Players[mvp].Goals)) mvp = i;
                if (worst < 0 || Rating(p) < Rating(_state.Players[worst])) worst = i;
            }
            if (worst == mvp || n < 2) worst = -1;

            // the table: the winners first, then the others
            string[] heads = { "Goals", "Assists", "Saves", "Bumps" };
            var colW = 82f;
            var cols = x + pw - 30f - colW * heads.Length;
            var ty = y + 104f;
            var head = Ui.Font(14, true, TextAnchor.MiddleCenter);
            for (var c = 0; c < heads.Length; c++) Ui.Label(new Rect(cols + c * colW, ty, colW, 24f), heads[c], head, Ui.TextDim);
            ty += 28f;
            var order = new System.Collections.Generic.List<int>();
            var win = _state.LastScorer < 0 ? 0 : _state.LastScorer;
            for (var pass = 0; pass < 2; pass++)
                for (var i = 0; i < n; i++) if ((_state.Players[i].Team == win) == (pass == 0)) order.Add(i);
            var cell = Ui.Font(18, true, TextAnchor.MiddleCenter);
            foreach (var i in order)
            {
                var p = _state.Players[i];
                var r = new Rect(x + 20f, ty, pw - 40f, rowH - 4f);
                Ui.Box(r, new Color(1f, 1f, 1f, 0.05f), 8f);
                Ui.Box(new Rect(r.x, r.y, 6f, r.height), Palette.Team(p.Team), 3f);
                var name = SeatName != null ? SeatName(i) : "P" + (i + 1);
                Ui.Label(new Rect(r.x + 18f, r.y, 220f, r.height), name, Ui.Font(18, true), Color.white);
                if (i == mvp) Badge(new Rect(r.x + 240f, r.y + 4f, 64f, r.height - 8f), "MVP", new Color(0.95f, 0.75f, 0.2f), new Color(0.15f, 0.1f, 0f));
                else if (i == worst) Badge(new Rect(r.x + 240f, r.y + 4f, 140f, r.height - 8f), "Tried their best", new Color(0.45f, 0.47f, 0.52f), Color.white);
                int[] v = { p.Goals, p.Assists, p.Saves, p.Bumps };
                for (var c = 0; c < v.Length; c++) Ui.Label(new Rect(cols + c * colW, r.y, colW, r.height), v[c].ToString(), cell, v[c] > 0 ? Color.white : new Color(1f, 1f, 1f, 0.35f));
                ty += rowH;
            }
            Glyphs.Hint(new Rect(x, y + ph - 46f, pw, 34f), "{start} Play again      {back} Main menu", 18, Ui.TextDim);
        }

        private static void Badge(Rect r, string text, Color bg, Color fg)
        {
            Ui.Box(r, bg, r.height / 2f);
            Ui.Label(r, text, Ui.Font(13, true, TextAnchor.MiddleCenter), fg, false);
        }

        /// <summary>A bar that fills while B / Escape is held to leave: always in the top-left corner (only its size comes from <paramref name="r"/>).</summary>
        /// <summary>Screenshots only: show the leaving bar this full.</summary>
        public static float ShotLeaving;

        public static void LeavingBar(Rect r, float t, string what)
        {
            t = Mathf.Max(t, ShotLeaving);
            if (t <= 0.02f) return;
            r = new Rect(20f, 20f, r.width, r.height);
            Ui.Box(r, new Color(0f, 0f, 0f, 0.6f), r.height / 2f);
            Ui.Box(new Rect(r.x, r.y, r.width * Mathf.Clamp01(t), r.height), new Color(0.9f, 0.3f, 0.3f, 0.8f), r.height / 2f);
            Ui.Label(r, what + "...", Ui.Font(14, true, TextAnchor.MiddleCenter), Color.white);
        }

        /// <summary>"Speed Up x2,  Puck Gun" — a player's cards, the same card counted.</summary>
        private string CardList(PlayerState p)
        {
            if (_sim == null || _sim.Cards == null) return "";
            var names = new System.Text.StringBuilder();
            var seen = new System.Collections.Generic.List<int>();
            foreach (var c in p.Cards)
            {
                if (seen.Contains(c) || c < 0 || c >= _sim.Cards.Cards.Count) continue;
                seen.Add(c);
                var n = 0;
                foreach (var d in p.Cards) if (d == c) n++;
                if (names.Length > 0) names.Append(",  ");
                names.Append(_sim.Cards.Cards[c].Name);
                if (n > 1) names.Append(" x" + n);
            }
            return names.ToString();
        }

        /// <summary>While View / Tab is held: the score, and everyone's cards and stats so far, team by team.</summary>
        private void Scoreboard(float w, float h)
        {
            var n = _state.Players.Length;
            const float rowH = 50f;
            var pw = Mathf.Min(860f, w - 40f);
            var ph = 120f + n * rowH + 8f;
            var x = w / 2f - pw / 2f;
            var y = Mathf.Max(76f, h / 2f - ph / 2f);
            Ui.Box(new Rect(x, y, pw, ph), new Color(0.07f, 0.075f, 0.09f, 0.94f), 18f);
            // the score, in the teams' colours
            var big = Ui.Font(34, true, TextAnchor.MiddleCenter);
            Ui.Label(new Rect(x, y + 12f, pw / 2f - 40f, 50f), Palette.TeamName(0) + "  " + _state.Score[0], new GUIStyle(big) { alignment = TextAnchor.MiddleRight }, Palette.Team(0));
            Ui.Label(new Rect(x + pw / 2f - 40f, y + 12f, 80f, 50f), ":", big, Color.white);
            Ui.Label(new Rect(x + pw / 2f + 40f, y + 12f, pw / 2f - 40f, 50f), _state.Score[1] + "  " + Palette.TeamName(1), new GUIStyle(big) { alignment = TextAnchor.MiddleLeft }, Palette.Team(1));

            string[] heads = { "Goals", "Assists", "Saves", "Bumps" };
            const float colW = 74f;
            var cols = x + pw - 26f - colW * heads.Length;
            var ty = y + 74f;
            var head = Ui.Font(13, true, TextAnchor.MiddleCenter);
            for (var c = 0; c < heads.Length; c++) Ui.Label(new Rect(cols + c * colW, ty, colW, 22f), heads[c], head, Ui.TextDim);
            ty += 26f;
            var cell = Ui.Font(18, true, TextAnchor.MiddleCenter);
            var small = new GUIStyle(Ui.Font(12)) { clipping = TextClipping.Clip };
            for (var team = 0; team < 2; team++)
                for (var i = 0; i < n; i++)
                {
                    var p = _state.Players[i];
                    if (p.Team != team) continue;
                    var r = new Rect(x + 18f, ty, pw - 36f, rowH - 6f);
                    Ui.Box(r, new Color(1f, 1f, 1f, 0.05f), 8f);
                    Ui.Box(new Rect(r.x, r.y, 5f, r.height), Palette.Team(p.Team), 2.5f);
                    var name = SeatName != null ? SeatName(i) : "P" + (i + 1);
                    Ui.Label(new Rect(r.x + 16f, r.y + 2f, cols - r.x - 24f, 24f), name, Ui.Font(17, true), Color.white);
                    var cards = CardList(p);
                    Ui.Label(new Rect(r.x + 16f, r.y + 24f, cols - r.x - 24f, 18f), cards.Length > 0 ? cards : "No cards yet", small, new Color(1f, 1f, 1f, cards.Length > 0 ? 0.7f : 0.35f), false);
                    int[] v = { p.Goals, p.Assists, p.Saves, p.Bumps };
                    for (var c = 0; c < v.Length; c++) Ui.Label(new Rect(cols + c * colW, r.y, colW, r.height), v[c].ToString(), cell, v[c] > 0 ? Color.white : new Color(1f, 1f, 1f, 0.35f));
                    ty += rowH;
                }
        }

        /// <summary>Everyone's cards, small, in the lower-left corner (Settings > Gameplay).</summary>
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

        /// <summary>Tiny FPS in the top-left, like Steam's overlay (doesn't follow UI size).</summary>
        private void DrawFps()
        {
            if (!Settings.ShowFps || Event.current.type != EventType.Repaint) return;
            if (_fpsStyle == null)
            {
                _fpsStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.UpperLeft,
                    richText = false,
                };
                _fpsStyle.normal.textColor = new Color(1f, 1f, 1f, 0.55f);
            }
            var prev = GUI.matrix;
            GUI.matrix = Matrix4x4.identity;
            GUI.Label(new Rect(6f, 4f, 64f, 18f), _fpsShown.ToString(), _fpsStyle);
            GUI.matrix = prev;
        }

        /// <summary>Names over the players, so it's easy to tell who is who.</summary>
        private void NameTags()
        {
            if (!Settings.NameTags || Cam == null || Event.current.type != EventType.Repaint) return;
            var k = Screen.height / 720f * Settings.UiScale;
            // small, plain text just above the check and cooldown bars (no box: it shouldn't cover anything)
            var style = Ui.Font(12, true, TextAnchor.LowerCenter);
            var dt = Mathf.Clamp(Time.unscaledTime - _tagsAt, 0f, 0.1f);
            _tagsAt = Time.unscaledTime;
            foreach (var t in Tags)
            {
                if (string.IsNullOrEmpty(t.Name)) continue;
                var sp = Cam.WorldToScreenPoint(t.World);
                var p = new Vector2(sp.x / k, (Screen.height - sp.y) / k);
                // fade while another player or the puck is under the text (so a crowd round the puck stays readable)
                var w = style.CalcSize(new GUIContent(t.Name)).x;
                var box = new Rect(p.x - w * 0.5f - 2f, p.y - 16f, w + 4f, 16f);
                var target = Covers(box, t.Owner, k) ? TagFaded : TagFull;
                float a;
                if (!_tagAlpha.TryGetValue(t.Owner, out a)) a = target;
                a = Mathf.MoveTowards(a, target, TagFadeSpeed * dt);
                _tagAlpha[t.Owner] = a;
                Ui.Label(new Rect(p.x - 100f, p.y - 18f, 200f, 18f), t.Name, style, new Color(1f, 1f, 1f, a));
            }
        }

        /// <summary>A name tag's box (UI units) is over a player other than <paramref name="owner"/>, or the puck.</summary>
        private bool Covers(Rect box, int owner, float k)
        {
            foreach (var u in Under)
            {
                if ((int)u.w == owner && owner >= 0) continue;
                var c = Cam.WorldToScreenPoint(new Vector3(u.x, u.y, 0f));
                var edge = Cam.WorldToScreenPoint(new Vector3(u.x + u.z, u.y, 0f));
                var centre = new Vector2(c.x / k, (Screen.height - c.y) / k);
                var r = Mathf.Abs(edge.x - c.x) / k;
                var near = new Vector2(Mathf.Clamp(centre.x, box.xMin, box.xMax), Mathf.Clamp(centre.y, box.yMin, box.yMax));
                if ((centre - near).sqrMagnitude <= r * r) return true;
            }
            return false;
        }
    }
}
