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
        /// <summary>A name over a player; <see cref="Mine"/>: someone playing on this PC (their name pulses at the kick-off).</summary>
        public struct Tag { public Vector3 World; public string Name; public int Team; public int Owner; public bool Mine; }
        public readonly System.Collections.Generic.List<Tag> Tags = new System.Collections.Generic.List<Tag>();
        /// <summary>
        /// What a name tag shouldn't hide: each player (x, y, radius, player number) and the puck (player number -1),
        /// in the world. A tag over any of them (but its own player) fades out.
        /// </summary>
        public readonly System.Collections.Generic.List<Vector4> Under = new System.Collections.Generic.List<Vector4>();

        /// <summary>RAGE: a meter under a player (only the card holder's own, on their own screen).</summary>
        public struct RageBar { public Vector3 World; public float Raging, Cooling; }

        /// <summary>A knock-out: the blast bursting in from the edge of the screen the way they flew out (lasts about a second).</summary>
        public sealed class Blast { public Vector2 World, Dir; public Color Colour; public float At; public int Seed; }
        public readonly System.Collections.Generic.List<Blast> Blasts = new System.Collections.Generic.List<Blast>();

        /// <summary>A respawn: a pillar of light in the team's colour round the player (<see cref="Pillar.T"/> 0 .. 1 through it).</summary>
        public struct Pillar { public Vector3 World; public float Radius, T; public Color Colour; }
        public readonly System.Collections.Generic.List<Pillar> Pillars = new System.Collections.Generic.List<Pillar>();

        /// <summary>Seconds until a player on this PC who was knocked out comes back (0: nobody's out).</summary>
        public float OutCountdown;
        public readonly System.Collections.Generic.List<RageBar> Rages = new System.Collections.Generic.List<RageBar>();
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
        /// <summary>A player's Profile look (for their replay banner) and symbol.</summary>
        public System.Func<int, int> StyleOf, SymbolOf;
        /// <summary>A player's Steam profile picture (null: none), shown on their banner instead of their symbol.</summary>
        public System.Func<int, Texture> PictureOf;
        /// <summary>After the match: 0 no replay to save, 1 one to save, 2 saved.</summary>
        public int ReplaySave;
        /// <summary>Draw a mouse reticle (keyboard players aiming with the mouse).</summary>
        public bool Reticle;
        /// <summary>When play last began after a countdown (for the START banner).</summary>
        public float StartAt = -10f;
        /// <summary>When the goal on show was scored (for its banner's animation), and which score it was.</summary>
        private float _goalAt = -10f;
        private int _goalKey = -1;

        /// <summary>The countdown's "START!" gold: the goal banner is the same.</summary>
        private static readonly Color Gold = new Color(1f, 0.85f, 0.25f);
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
                _fpsTimer += Clock.Dt;
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
            Pillars_();
            NameTags();
            RageMeters();
            Blasts_();
            OutCountdown_(w, h);
            // holding the scoreboard button brings up the corner card list too (whatever the setting); let go and it goes
            if (Board && _state.Phase != Phase.GameOver) { Scoreboard(w, h); CornerCards(h, true); return; }
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
                    if (!_free && Clock.Now - StartAt < 0.9f)
                    {
                        // START: pops in big and fades out
                        var age = Clock.Now - StartAt;
                        var size = Mathf.RoundToInt(Mathf.Lerp(130f, 96f, Ui.EaseOut(age / 0.3f)));
                        var a = 1f - Mathf.Clamp01((age - 0.55f) / 0.35f);
                        Ui.Label(new Rect(0, h * 0.28f - 40f, w, 140), "START!", Ui.Font(size, true, TextAnchor.MiddleCenter), new Color(Gold.r, Gold.g, Gold.b, a));
                    }
                    break;
                case Phase.Scored:
                    if (Replay)
                    {
                        // a "REPLAY" badge that pulses, and a bar top and bottom like a broadcast
                        Ui.Box(new Rect(0, 0, w, 10), new Color(0f, 0f, 0f, 0.6f), 0f);
                        Ui.Box(new Rect(0, h - 10, w, 10), new Color(0f, 0f, 0f, 0.6f), 0f);
                        var pulse = 0.7f + 0.3f * Mathf.Sin(Clock.Now * 6f);
                        Ui.Box(new Rect(24, 80, 150, 40), new Color(0.85f, 0.15f, 0.15f, 0.9f * pulse), 8f);
                        Ui.Label(new Rect(24, 80, 150, 40), "REPLAY", Ui.Font(22, true, TextAnchor.MiddleCenter), Color.white);
                        if (ReplaySlow) Ui.Label(new Rect(184, 80, 300, 40), "slow motion", Ui.Font(17, false), new Color(1f, 1f, 1f, 0.7f));
                        // who scored (on their banner), and who set it up
                        if (!ScorerCard(w, h, 1f))
                        {
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
                        GoalBanner(w, h);
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
        /// <summary>
        /// "Name Scored!" (or "Red Team Scored!" for an own goal) in the START gold: slams in big with a bounce and a
        /// tilt, then keeps a gentle pulse and sway. The assist underneath.
        /// </summary>
        private void GoalBanner(float w, float h)
        {
            var key = _state.Score[0] * 1000 + _state.Score[1];
            if (key != _goalKey) { _goalKey = key; _goalAt = Clock.Now; }
            var age = Clock.Now - _goalAt;
            var by = _state.LastGoalBy;
            var who = by >= 0 && by < _state.Players.Length && SeatName != null ? SeatName(by) : Palette.TeamName(_state.LastScorer) + " Team";
            var text = who + " Scored!";
            // in: from huge and see-through down past its size and back (a slam); then a slow pulse
            var pop = Ui.EaseOutBack(age / 0.45f);
            var scale = Mathf.LerpUnclamped(2.2f, 1f, pop) * (1f + 0.04f * Mathf.Sin(age * 5f) * Mathf.Clamp01((age - 0.45f) * 3f));
            // (a long name: smaller, so it still fits across the screen once it's settled)
            var fit = Mathf.Min(1f, w * 0.9f / Mathf.Max(1f, Ui.Font(104, true, TextAnchor.MiddleCenter).CalcSize(new GUIContent(text)).x));
            var size = Mathf.Clamp(Mathf.RoundToInt(104f * fit * scale), 20, 300);
            var a = Mathf.Clamp01(age / 0.15f);
            var tilt = Mathf.Lerp(-12f, 0f, Ui.EaseOut(age / 0.5f)) + 2.5f * Mathf.Sin(age * 3.2f) * Mathf.Clamp01((age - 0.5f) * 2f);
            var centre = new Vector2(w / 2f, h * 0.3f);
            var keep = GUI.matrix;
            GUIUtility.RotateAroundPivot(tilt, centre);
            Ui.Label(new Rect(0, centre.y - 90f, w, 180f), text, Ui.Font(size, true, TextAnchor.MiddleCenter), new Color(Gold.r, Gold.g, Gold.b, a));
            GUI.matrix = keep;
            var assist = _state.LastAssist;
            if (assist >= 0 && assist < _state.Players.Length && SeatName != null)
            {
                var b = Mathf.Clamp01((age - 0.4f) / 0.3f);
                Ui.Label(new Rect(0, centre.y + 70f, w, 40), "Assist: " + SeatName(assist), Ui.Font(24, true, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, b));
            }
        }

        /// <summary>
        /// The scorer's replay banner (their Profile look) along the bottom left: their name, "GOAL", and the assist.
        /// False if there's no scorer to show (an own goal).
        /// </summary>
        private bool ScorerCard(float w, float h, float a)
        {
            if (_state == null || SeatName == null) return false;
            var by = _state.LastGoalBy;
            if (by < 0 || by >= _state.Players.Length) return false;
            if (a <= 0f) return true;
            var look = StyleOf != null ? StyleOf(by) : 0;
            var assist = _state.LastAssist;
            var below = assist >= 0 && assist < _state.Players.Length ? "Assist: " + SeatName(assist) : null;
            // centred along the bottom, above the skip hint
            var bw = Mathf.Min(280f, w - 48f);
            Banners.Draw(new Rect(w / 2f - bw / 2f, h - 118f, bw, 52f), Cosmetics.Banner(look), Cosmetics.BannerTint(look), _state.Players[by].Team, SeatName(by), Cosmetics.Title(look), below, a,
                SymbolOf != null ? SymbolOf(by) : -1, PictureOf != null ? PictureOf(by) : null);
            return true;
        }

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
            var pw = Mathf.Min(880f, w - 40f);
            var ph = 212f + n * rowH;
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
            string[] heads = { "Goals", "Assists", "Saves", "Bumps", "Points" };
            var colW = 76f;
            var cols = x + pw - 30f - colW * heads.Length;
            var ty = y + 104f;
            // own copies: Ui.Font hands out one shared style per size, and other labels change its alignment
            var head = new GUIStyle(Ui.Font(14, true, TextAnchor.MiddleCenter));
            for (var c = 0; c < heads.Length; c++) Ui.Label(new Rect(cols + c * colW, ty, colW, 24f), heads[c], head, c == heads.Length - 1 ? Ui.Text : Ui.TextDim);
            ty += 28f;
            var order = new System.Collections.Generic.List<int>();
            var win = _state.LastScorer < 0 ? 0 : _state.LastScorer;
            for (var pass = 0; pass < 2; pass++)
                for (var i = 0; i < n; i++) if ((_state.Players[i].Team == win) == (pass == 0)) order.Add(i);
            var cell = new GUIStyle(Ui.Font(18, true, TextAnchor.MiddleCenter));
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
                int[] v = { p.Goals, p.Assists, p.Saves, p.Bumps, Rating(p) };
                for (var c = 0; c < v.Length; c++) Ui.Label(new Rect(cols + c * colW, r.y, colW, r.height), v[c].ToString(), cell, v[c] > 0 ? Color.white : new Color(1f, 1f, 1f, 0.35f));
                ty += rowH;
            }
            Ui.Label(new Rect(x, ty + 2f, pw, 20f), "Points: goal 3, assist 2, save 2, bump 1", Ui.Font(12, false, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, 0.35f));
            var save = ReplaySave == 1 ? "      {savereplay} Save replay" : ReplaySave == 2 ? "      Replay saved" : "";
            Glyphs.Hint(new Rect(x, y + ph - 46f, pw, 34f), "{start} Play again" + save + "      {back} Main menu", 18, Ui.TextDim);
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

        /// <summary>While View / Tab is held: the score, and everyone's cards and stats so far, team by team.</summary>
        private void Scoreboard(float w, float h)
        {
            var n = _state.Players.Length;
            const float rowH = 44f;
            var pw = Mathf.Min(900f, w - 40f);
            var ph = 120f + n * rowH + 8f;
            var x = w / 2f - pw / 2f;
            var y = Mathf.Max(76f, h / 2f - ph / 2f);
            Ui.Box(new Rect(x, y, pw, ph), new Color(0.07f, 0.075f, 0.09f, 0.94f), 18f);
            // the score, in the teams' colours
            var big = Ui.Font(34, true, TextAnchor.MiddleCenter);
            Ui.Label(new Rect(x, y + 12f, pw / 2f - 40f, 50f), Palette.TeamName(0) + "  " + _state.Score[0], new GUIStyle(big) { alignment = TextAnchor.MiddleRight }, Palette.Team(0));
            Ui.Label(new Rect(x + pw / 2f - 40f, y + 12f, 80f, 50f), ":", big, Color.white);
            Ui.Label(new Rect(x + pw / 2f + 40f, y + 12f, pw / 2f - 40f, 50f), _state.Score[1] + "  " + Palette.TeamName(1), new GUIStyle(big) { alignment = TextAnchor.MiddleLeft }, Palette.Team(1));

            string[] heads = { "Goals", "Assists", "Saves", "Bumps", "Points" };
            const float colW = 70f;
            var cols = x + pw - 26f - colW * heads.Length;
            var ty = y + 74f;
            var head = new GUIStyle(Ui.Font(13, true, TextAnchor.MiddleCenter));
            for (var c = 0; c < heads.Length; c++) Ui.Label(new Rect(cols + c * colW, ty, colW, 22f), heads[c], head, c == heads.Length - 1 ? Ui.Text : Ui.TextDim);
            ty += 26f;
            var cell = new GUIStyle(Ui.Font(18, true, TextAnchor.MiddleCenter));
            for (var team = 0; team < 2; team++)
                for (var i = 0; i < n; i++)
                {
                    var p = _state.Players[i];
                    if (p.Team != team) continue;
                    var r = new Rect(x + 18f, ty, pw - 36f, rowH - 6f);
                    Ui.Box(r, new Color(1f, 1f, 1f, 0.05f), 8f);
                    Ui.Box(new Rect(r.x, r.y, 5f, r.height), Palette.Team(p.Team), 2.5f);
                    var name = SeatName != null ? SeatName(i) : "P" + (i + 1);
                    // their title (Profile) under the name
                    var title = StyleOf != null ? Cosmetics.Title(StyleOf(i)) : 0;
                    if (title > 0)
                    {
                        Ui.Label(new Rect(r.x + 16f, r.y + 1f, cols - r.x - 24f, 22f), name, Ui.Font(17, true), Color.white);
                        Cosmetics.DrawTitle(new Rect(r.x + 16f, r.y + 21f, cols - r.x - 24f, 14f), title, 11, TextAnchor.MiddleLeft, 1f);
                    }
                    else Ui.Label(new Rect(r.x + 16f, r.y, cols - r.x - 24f, r.height), name, Ui.Font(17, true), Color.white);
                    int[] v = { p.Goals, p.Assists, p.Saves, p.Bumps, Rating(p) };
                    for (var c = 0; c < v.Length; c++) Ui.Label(new Rect(cols + c * colW, r.y, colW, r.height), v[c].ToString(), cell, v[c] > 0 ? Color.white : new Color(1f, 1f, 1f, 0.35f));
                    ty += rowH;
                }
        }

        /// <summary>Everyone's cards, small, in the lower-left corner (Settings > Gameplay).</summary>
        private void CornerCards(float h, bool always = false)
        {
            if ((!Settings.CornerCards && !always) || _sim == null || _sim.Cards == null) return;
            var lines = new System.Collections.Generic.List<int>();
            for (var i = 0; i < _state.Players.Length; i++)
            {
                var any = false;
                foreach (var c in _state.Players[i].Cards) any |= !_sim.Cards.IsChoose(c) && !_sim.Cards.IsDouble(c);
                if (any) lines.Add(i);
            }
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
                    if (_sim.Cards.IsChoose(c) || _sim.Cards.IsDouble(c)) continue; // (they only lead to the cards they gave)
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
            var c = Settings.ReticleTint(team); c.a = 0.95f;
            var scale = 1.1f * Settings.CrosshairSize / 100f; // 100%: the size the original crosshair had by default
            DrawCrosshair(Settings.Crosshair, p, 34f * scale, c);
        }

        /// <summary>
        /// Crosshair <paramref name="which"/> centred on <paramref name="at"/>, <paramref name="size"/> across, in
        /// <paramref name="colour"/> with a soft dark copy behind it so it reads on the light ice too.
        /// </summary>
        public static void DrawCrosshair(int which, Vector2 at, float size, Color colour)
        {
            if (Crosshairs.IsOriginal(which)) { OriginalCrosshair(at, size / 34f, colour); return; }
            var tex = Crosshairs.Get(which);
            if (tex == null) return;
            var r = new Rect(at.x - size / 2f, at.y - size / 2f, size, size);
            var keep = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, colour.a * 0.45f);
            GUI.DrawTexture(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), tex);
            GUI.color = colour;
            GUI.DrawTexture(r, tex);
            GUI.color = keep;
        }

        /// <summary>The game's first crosshair: a soft dark ring, a faint disc of the colour, a cross with a gap, and a dot.</summary>
        private static void OriginalCrosshair(Vector2 p, float scale, Color c)
        {
            var r = 14f * scale;
            var gap = 5f * scale;
            var arm = 10f * scale;
            var thick = Mathf.Max(1.5f, 2f * scale);
            Ui.Box(new Rect(p.x - r - 1f, p.y - r - 1f, (r + 1f) * 2f, (r + 1f) * 2f), new Color(0f, 0f, 0f, 0.35f * c.a), r + 1f);
            Ui.Box(new Rect(p.x - r, p.y - r, r * 2f, r * 2f), new Color(c.r, c.g, c.b, 0.18f * c.a), r);
            Ui.Box(new Rect(p.x - arm - gap, p.y - thick * 0.5f, arm, thick), c, 1f);
            Ui.Box(new Rect(p.x + gap, p.y - thick * 0.5f, arm, thick), c, 1f);
            Ui.Box(new Rect(p.x - thick * 0.5f, p.y - arm - gap, thick, arm), c, 1f);
            Ui.Box(new Rect(p.x - thick * 0.5f, p.y + gap, thick, arm), c, 1f);
            var d = 1.5f * scale;
            Ui.Box(new Rect(p.x - d, p.y - d, d * 2f, d * 2f), c, d);
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

        // soft-edged pictures for the light effects, made once
        private static Texture2D _streak, _glow, _beam, _spike;

        private static void MakeLightTextures()
        {
            if (_streak != null) return;
            const int n = 64;
            _streak = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            _glow = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            _beam = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            _spike = new Texture2D(n * 4, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n * 4; x++)
                {
                    // a spike: a hard-edged sliver, full width at its base (left), narrowing to a sharp point at its end
                    float u = (x + 0.5f) / (n * 4), v = Mathf.Abs((y + 0.5f) / n * 2f - 1f);
                    var halfWidth = 1f - u;
                    var a = Mathf.Clamp01((halfWidth - v) * n * 0.6f); // (a pixel or so of softening on the edge only)
                    _spike.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            _spike.Apply();
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    var across = 1f - Mathf.Abs(v * 2f - 1f);
                    // a streak: bright at its start (left), thinning and fading to its end; soft along its sides
                    _streak.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Pow(across, 1.6f) * Mathf.Pow(1f - u, 1.3f)));
                    var d = Mathf.Clamp01(new Vector2(u * 2f - 1f, v * 2f - 1f).magnitude);
                    _glow.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Pow(1f - d, 2.2f)));
                    // a beam of light: soft at its sides, strongest at the bottom (texture y 0) fading towards the top
                    var side = 1f - Mathf.Abs(u * 2f - 1f);
                    // (softly in at its very bottom too, so it melts into the glow instead of ending in a line)
                    _beam.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Pow(side, 0.9f) * Mathf.Pow(1f - v, 1.4f) * Mathf.SmoothStep(0f, 1f, v / 0.12f)));
                }
            _streak.Apply(); _glow.Apply(); _beam.Apply();
        }

        private Vector2 ToCanvas(Vector3 world)
        {
            var k = Screen.height / 720f * Settings.UiScale;
            var sp = Cam.WorldToScreenPoint(world);
            return new Vector2(sp.x / k, (Screen.height - sp.y) / k);
        }

        private static void Tex(Rect r, Texture2D tex, Color c)
        {
            var keep = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, tex);
            GUI.color = keep;
        }

        /// <summary>
        /// Knock-out blasts: where the flight leaves the screen, a burst of hard, spiky rays shoots back in (white and
        /// gold, the team's colour, and red rays with black cores), with a flash at the edge; quick in, then fading.
        /// </summary>
        private void Blasts_()
        {
            if (Cam == null || Blasts.Count == 0) return;
            if (Event.current.type != EventType.Repaint) return;
            MakeLightTextures();
            const float life = 1.0f;
            for (var b = Blasts.Count - 1; b >= 0; b--)
            {
                var bl = Blasts[b];
                var age = Clock.Now - bl.At;
                if (age < 0f) continue; // still flying there
                if (age > life) { Blasts.RemoveAt(b); continue; }
                var k = age / life;
                // where the flight crosses the edge of the screen (canvas units; y down)
                var from = ToCanvas(new Vector3(bl.World.x, bl.World.y, 0f));
                var dir = new Vector2(bl.Dir.x, -bl.Dir.y).normalized;
                if (dir.sqrMagnitude < 0.5f) dir = Vector2.right;
                float tx = dir.x > 0.001f ? (Ui.W - from.x) / dir.x : dir.x < -0.001f ? -from.x / dir.x : float.MaxValue;
                float ty = dir.y > 0.001f ? (Ui.H - from.y) / dir.y : dir.y < -0.001f ? -from.y / dir.y : float.MaxValue;
                var edge = from + dir * Mathf.Max(0f, Mathf.Min(tx, ty));
                edge = new Vector2(Mathf.Clamp(edge.x, 0f, Ui.W), Mathf.Clamp(edge.y, 0f, Ui.H));
                var back = Mathf.Atan2(-dir.y, -dir.x) * Mathf.Rad2Deg; // pointing back into the screen
                var grow = 1f - Mathf.Pow(1f - Mathf.Clamp01(k / 0.12f), 3f);
                var fade = 1f - Mathf.Clamp01((k - 0.3f) / 0.7f);
                // the flash where it went out
                var flash = Mathf.Lerp(150f, 70f, k);
                Tex(new Rect(edge.x - flash, edge.y - flash, flash * 2f, flash * 2f), _glow, new Color(1f, 1f, 1f, fade));
                var rng = new System.Random(bl.Seed);
                var keep = GUI.matrix;
                var red = new Color(0.95f, 0.08f, 0.06f);
                for (var s = 0; s < 14; s++)
                {
                    var kind = s % 4; // 0 white, 1 gold, 2 team colour, 3 red with a black core
                    var spread = (float)(rng.NextDouble() * 2.0 - 1.0) * (kind == 3 ? 26f : 20f);
                    var len = Mathf.Lerp(500f, 1150f, (float)rng.NextDouble()) * grow * (1f - Mathf.Abs(spread) / 70f);
                    var wide = Mathf.Lerp(22f, 54f, (float)rng.NextDouble()) * (1f - 0.5f * k);
                    Color col = kind == 0 ? Color.white : kind == 1 ? new Color(1f, 0.85f, 0.3f) : kind == 2 ? Color.Lerp(bl.Colour, Color.white, 0.15f) : red;
                    col.a = fade;
                    GUI.matrix = keep;
                    GUIUtility.RotateAroundPivot(back + spread, edge * GUI.matrix.m00);
                    Tex(new Rect(edge.x - 10f, edge.y - wide / 2f, len, wide), _spike, col);
                    if (kind == 3) Tex(new Rect(edge.x - 10f, edge.y - wide * 0.22f, len * 0.92f, wide * 0.44f), _spike, new Color(0f, 0f, 0f, fade));
                }
                // the core: one big white spike straight back in
                GUI.matrix = keep;
                GUIUtility.RotateAroundPivot(back, edge * GUI.matrix.m00);
                var core = 70f * (1f - 0.45f * k);
                Tex(new Rect(edge.x - 20f, edge.y - core / 2f, 950f * grow, core), _spike, new Color(1f, 1f, 1f, fade));
                GUI.matrix = keep;
            }
        }

        /// <summary>Knocked out (a player on this PC): "Knocked out!" and the seconds until they're back, big, in the middle.</summary>
        private void OutCountdown_(float w, float h)
        {
            if (OutCountdown <= 0f) return;
            var n = Mathf.CeilToInt(OutCountdown - 0.001f);
            var into = OutCountdown - Mathf.Floor(OutCountdown); // each number pops in as it changes
            var pop = 1f + 0.25f * Mathf.Clamp01((into - 0.8f) / 0.2f);
            Ui.Label(new Rect(0f, h * 0.30f, w, 40f), "Knocked out!", Ui.Font(28, true, TextAnchor.MiddleCenter), new Color(1f, 0.35f, 0.3f));
            var size = Mathf.RoundToInt(64f * pop);
            Ui.Label(new Rect(0f, h * 0.30f + 40f, w, 90f), n.ToString(), Ui.Font(size, true, TextAnchor.MiddleCenter), Color.white);
            Ui.Label(new Rect(0f, h * 0.30f + 128f, w, 30f), "Back in a moment", Ui.Font(18, false, TextAnchor.MiddleCenter), Ui.TextDim);
        }

        /// <summary>
        /// Respawns: a pillar of light in the team's colour rising round the player from a glowing ring on the ice,
        /// with motes drifting up through it; it rises fast, holds, and fades away.
        /// </summary>
        private void Pillars_()
        {
            if (Cam == null || Pillars.Count == 0 || Event.current.type != EventType.Repaint) return;
            MakeLightTextures();
            foreach (var pl in Pillars)
            {
                var c = ToCanvas(pl.World);
                // (the player's size on screen, made bigger: a pillar the size of the player is too small to see)
                var r = Mathf.Max(18f, Mathf.Abs(ToCanvas(pl.World + new Vector3(pl.Radius, 0f, 0f)).x - c.x) * 1.8f);
                var rise = 1f - Mathf.Pow(1f - Mathf.Clamp01(pl.T / 0.18f), 3f);
                var fade = 1f - Mathf.Clamp01((pl.T - 0.6f) / 0.4f);
                var col = pl.Colour;
                var light = Color.Lerp(col, Color.white, 0.45f);
                // a soft glow on the ice where the beam comes down
                var ringW = r * 3.2f;
                var ringH = r * 1.1f;
                Tex(new Rect(c.x - ringW, c.y - ringH + r * 0.55f, ringW * 2f, ringH * 2f), _glow, new Color(col.r, col.g, col.b, 0.8f * fade));
                // the beam: wide and coloured, a white core inside it
                var h = r * 10f * rise;
                var w = r * 2.6f;
                Tex(new Rect(c.x - w / 2f, c.y + r * 0.6f - h, w, h), _beam, new Color(light.r, light.g, light.b, 0.85f * fade));
                Tex(new Rect(c.x - w / 2f, c.y + r * 0.6f - h, w, h), _beam, new Color(col.r, col.g, col.b, 0.6f * fade));
                Tex(new Rect(c.x - w * 0.2f, c.y + r * 0.6f - h * 0.9f, w * 0.4f, h * 0.9f), _beam, new Color(1f, 1f, 1f, 0.7f * fade));
                // motes drifting up
                for (var m = 0; m < 8; m++)
                {
                    var phase = (pl.T * 2.2f + m * 0.137f) % 1f;
                    var x = c.x + Mathf.Sin(m * 2.3f) * w * 0.38f;
                    var y = c.y + r * 0.4f - phase * r * 8f * rise;
                    var s = r * 0.22f * (1f - phase);
                    Tex(new Rect(x - s, y - s, s * 2f, s * 2f), _glow, new Color(1f, 1f, 1f, 0.9f * fade * (1f - phase)));
                }
            }
        }

        /// <summary>
        /// RAGE bars: while raging a green bar running down; while cooling down a grey bar running down (no rage builds up
        /// until it's gone). How close the next rage is isn't shown (only the twitching hints at it).
        /// </summary>
        private void RageMeters()
        {
            if (Cam == null || Rages.Count == 0 || Event.current.type != EventType.Repaint) return;
            var k = Screen.height / 720f * Settings.UiScale;
            const float w = 44f, h = 6f;
            foreach (var r in Rages)
            {
                if (r.Raging <= 0f && r.Cooling <= 0f) continue;
                var sp = Cam.WorldToScreenPoint(r.World);
                var c = new Vector2(sp.x / k, (Screen.height - sp.y) / k);
                if (r.Raging > 0f)
                {
                    var box = new Rect(c.x - w / 2f, c.y, w, h);
                    Ui.Box(new Rect(box.x - 1.5f, box.y - 1.5f, box.width + 3f, box.height + 3f), new Color(0f, 0f, 0f, 0.6f), 4f);
                    Ui.Box(new Rect(box.x, box.y, box.width * r.Raging, box.height), new Color(0.35f, 0.9f, 0.3f), 3f);
                    continue;
                }
                // the cooldown: smaller (it's only a reminder)
                const float cw = 26f, ch = 3f;
                var cool = new Rect(c.x - cw / 2f, c.y + 1f, cw, ch);
                Ui.Box(new Rect(cool.x - 1f, cool.y - 1f, cool.width + 2f, cool.height + 2f), new Color(0f, 0f, 0f, 0.5f), 2.5f);
                Ui.Box(new Rect(cool.x, cool.y, cool.width * r.Cooling, cool.height), new Color(1f, 1f, 1f, 0.35f), 1.5f);
            }
        }

        /// <summary>Names over the players, so it's easy to tell who is who.</summary>
        private void NameTags()
        {
            if (!Settings.NameTags || Cam == null || Event.current.type != EventType.Repaint) return;
            var k = Screen.height / 720f * Settings.UiScale;
            // small, plain text just above the check and cooldown bars (no box: it shouldn't cover anything)
            var style = Ui.Font(12, true, TextAnchor.LowerCenter);
            var dt = Mathf.Clamp(Clock.Now - _tagsAt, 0f, 0.1f);
            _tagsAt = Clock.Now;
            // the kick-off: your own name grows and shrinks twice, so you can see which one you are and where you start
            var into = _sim != null && _state.Phase == Phase.Countdown ? (_sim.T.CountdownTime - _state.PhaseTime).ToFloat() : -1f;
            foreach (var t in Tags)
            {
                if (string.IsNullOrEmpty(t.Name)) continue;
                var sp = Cam.WorldToScreenPoint(t.World);
                var p = new Vector2(sp.x / k, (Screen.height - sp.y) / k);
                var pulse = t.Mine && into >= 0f && into < 1.6f ? 1f + 0.8f * Mathf.Abs(Mathf.Sin(into / 0.8f * Mathf.PI)) : 1f;
                if (pulse > 1.01f)
                {
                    var big = Ui.Font(Mathf.RoundToInt(12f * pulse), true, TextAnchor.LowerCenter);
                    Ui.Label(new Rect(p.x - 200f, p.y - 18f * pulse, 400f, 18f * pulse), t.Name, big, Color.white);
                    _tagAlpha[t.Owner] = TagFull;
                    continue;
                }
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
