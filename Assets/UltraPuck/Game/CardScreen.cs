using System.Collections.Generic;
using System.IO;
using UltraPuck.Core;
using UnityEngine;

namespace UltraPuck.Game
{
    /// <summary>
    /// The card pick screen. When a local player is offered cards, they're dealt face down from below, flip over,
    /// and float in a row. The focused card lifts towards you and tilts after the mouse or the right stick, which
    /// moves the holo foil, glare and sparkle across it like a real trading card. Picking one pops it, the others
    /// flip back over and drop away, and the pick goes to the game. Several local players pick one after another
    /// (or, with Team Vote, vote together on the same cards with their names under their cursors).
    /// Cards are real 3D objects on their own camera, drawn over the game.
    /// </summary>
    internal sealed class CardScreen : MonoBehaviour
    {
        public const int Layer = 5; // the built-in "UI" layer; the game camera doesn't draw it

        public Simulation Sim;
        public LocalPlayers Local;

        private Camera _cam;
        private Shader _shader;
        private Font _font;
        private Transform _root;
        private Material _dimMat;
        private float _dim;

        private readonly List<CardView> _cards = new List<CardView>();
        private readonly Dictionary<int, byte> _ready = new Dictionary<int, byte>();
        private readonly Dictionary<string, Texture2D> _art = new Dictionary<string, Texture2D>();

        private enum Stage { Idle, Choosing, Leaving, Editing }
        private Stage _stage = Stage.Idle;
        private int _slot = -1, _focus, _chosen = -1;
        private float _stageAt, _lastHover;
        private int _testStep;
        private GameState _state;

        /// <summary>
        /// ULTRAPUCK_CARDTEST=1 (for recording the screen with nobody at the controls): the focus moves along by
        /// itself and a card is taken after a few seconds, then another pick starts.
        /// </summary>
        public static readonly bool TestMode = System.Environment.GetEnvironmentVariable("ULTRAPUCK_CARDTEST") == "1";

        /// <summary>The pick a local player confirmed (1-based) for the game to read, or 0 while they're still choosing.</summary>
        public byte PickFor(int player) { byte b; return _ready.TryGetValue(player, out b) ? b : (byte)0; }

        /// <summary>The card a local player is looking at right now (1-based), sent along so the other team can watch.</summary>
        public byte HoverFor(int player)
        {
            int k;
            if (_stage == Stage.Choosing && _cursors.TryGetValue(player, out k)) return (byte)(k + 1);
            return _stage == Stage.Choosing && !_watching && !_teamVote && _slot == player ? (byte)(_focus + 1) : (byte)0;
        }

        /// <summary>Where match player <c>p</c> is on screen (pixels), for the taken card to fly into; null if not shown.</summary>
        public System.Func<int, Vector3?> PlayerOnScreen;

        /// <summary>A taken card reached match player <c>p</c> (with the card's colour): the game shows the ripple.</summary>
        public System.Action<int, Color> OnAbsorbed;

        /// <summary>A player's name for the header (set by GameRunner).</summary>
        public System.Func<int, string> NameOf;

        // watching someone else choose (the other team, or a bot): their offer, their highlight, then their pick
        private bool _watching;
        private int[] _watchOffer = new int[0];
        private readonly HashSet<int> _watched = new HashSet<int>();

        // Team Vote: everyone on the team looks at the same cards; each local voter has their own cursor
        private bool _teamVote;
        private readonly Dictionary<int, int> _cursors = new Dictionary<int, int>(); // player -> card index
        private readonly List<int> _voters = new List<int>(); // local humans still choosing on this draw

        /// <summary>
        /// Which local controller slot plays match player <c>p</c> (-1: not on this PC). The same number in a local
        /// game; online, the game sets this from the seats.
        /// </summary>
        public System.Func<int, int> SlotOf = p => p;

        /// <summary>The local slot of the player choosing now.</summary>
        private int Slot { get { return SlotOf(_slot); } }

        public bool Showing { get { return _stage != Stage.Idle; } }

        /// <summary>Free play's card editor is open (the match waits).</summary>
        public bool Editing { get { return _stage == Stage.Editing; } }

        private void Awake()
        {
            _shader = Resources.Load<Shader>("UltraPuck/Card");
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var go = new GameObject("CardCamera");
            _cam = go.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.Depth;
            _cam.cullingMask = 1 << Layer;
            _cam.depth = 10;
            _cam.fieldOfView = 30f;
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 50f;
            _cam.transform.position = new Vector3(0f, 0f, -14f);
            _root = new GameObject("Cards").transform;
            // a dark veil over the game while picking
            var dim = Quad("Dim", _root, new Vector2(80f, 50f));
            dim.transform.localPosition = new Vector3(0f, 0f, 8f);
            _dimMat = Mat(new Vector2(80f, 50f), 0f);
            _dimMat.SetColor("_Color", Color.black); _dimMat.SetColor("_Color2", Color.black);
            _dimMat.SetFloat("_Glare", 0f);
            dim.GetComponent<MeshRenderer>().sharedMaterial = _dimMat;
            dim.GetComponent<MeshRenderer>().sortingOrder = -1000;
        }

        // ------------------------------------------------------------------ every frame

        public void Frame(GameState s)
        {
            _state = s;
            var picking = s != null && s.Phase == Phase.Picking && Sim != null && Sim.Cards != null;
            if (!picking)
            {
                // the picks are in: a shared draw still dives into this PC's first chooser
                if (_stage == Stage.Choosing) { if (_teamVote && !_watching) LeaveTeamVote(); else Leave(_watching ? WatchedPick(s) : -1); }
                _ready.Clear();
                _watched.Clear();
                _cursors.Clear();
                _voters.Clear();
                if (_stage == Stage.Editing) { if (_browse) BrowseInput(); else EditInput(); }
            }
            else
            {
                // forget confirmed picks once the game has taken them
                var done = new List<int>();
                foreach (var kv in _ready) if (kv.Key >= s.Players.Length || s.Players[kv.Key].Picked >= 0 || s.Players[kv.Key].Offer.Length == 0) done.Add(kv.Key);
                // (a shared draw keeps their cursor up, locked, until the whole team has chosen)
                foreach (var k in done) { _ready.Remove(k); if (!_teamVote) { _cursors.Remove(k); _voters.Remove(k); } }
                if (_stage == Stage.Idle && !_editing)
                {
                    if (Sim.T.TeamVote || Sim.T.SameDraw) BeginTeamVote(s);
                    else
                    {
                        var next = NextPicker(s);
                        if (next >= 0) { _watching = false; _teamVote = false; Deal(next, s.Players[next].Offer); }
                        else
                        {
                            var w = NextWatched(s);
                            if (w >= 0) { _watching = true; _teamVote = false; _watchOffer = (int[])s.Players[w].Offer.Clone(); _watched.Add(w); Deal(w, _watchOffer); }
                        }
                    }
                }
            }

            // the veil lifts as a taken card dives into its player, so the ripple on the rink can be seen
            var diving = _stage == Stage.Leaving && _chosen >= 0 && !_editing && Time.unscaledTime - _stageAt > 0.5f && AbsorbPoint() != null;
            var want = _stage != Stage.Idle && !diving ? 0.68f : 0f;
            _dim = Mathf.MoveTowards(_dim, want, Time.unscaledDeltaTime * 2.5f);
            _dimMat.SetFloat("_Alpha", _dim);
            _root.gameObject.SetActive(_dim > 0.001f || _cards.Count > 0);

            if (_stage == Stage.Choosing) { if (_teamVote) ChooseTeamVote(); else Choose(); }
            Animate();
            Motes();
            if (_stage == Stage.Leaving && (Time.unscaledTime - _stageAt > (_editing ? 1.1f : _chosen >= 0 ? 1.25f : 0.85f) || (_absorbed && Time.unscaledTime - _absorbedAt > 0.1f)))
            {
                if (_chosen >= 0 && _slot >= 0 && !_watching && !_teamVote) _ready[_slot] = (byte)(_chosen + 1);
                _watching = false;
                _teamVote = false;
                _cursors.Clear();
                _voters.Clear();
                _editing = false;
                var browsed = _browse;
                _browse = false;
                foreach (var c in _cards) { Destroy(c.Root.gameObject); Destroy(c.Shadow.gameObject); }
                foreach (var m in _motes) if (m.Go != null) Destroy(m.Go);
                _motes.Clear();
                _cards.Clear();
                _stage = Stage.Idle;
                _slot = -1;
                if (browsed && OnBrowseClosed != null) OnBrowseClosed();
            }
        }

        /// <summary>The first local person (not a bot) still to choose.</summary>
        private int NextPicker(GameState s)
        {
            for (var i = 0; i < s.Players.Length; i++)
            {
                var slot = SlotOf(i);
                if (slot < 0 || slot >= Local.Slots.Count || Local.Slots[slot].Demo || _ready.ContainsKey(i)) continue;
                var p = s.Players[i];
                if (p.Offer.Length > 0 && p.Picked < 0) return i;
            }
            return -1;
        }

        /// <summary>Someone else still choosing (not a person on this PC) whom we haven't watched yet this time.</summary>
        private int NextWatched(GameState s)
        {
            for (var i = 0; i < s.Players.Length; i++)
            {
                if (_watched.Contains(i)) continue;
                var slot = SlotOf(i);
                if (slot >= 0 && slot < Local.Slots.Count && !Local.Slots[slot].Demo) continue; // on this PC: they pick themselves
                var p = s.Players[i];
                if (p.Offer.Length > 0 && p.Picked < 0) return i;
            }
            return -1;
        }

        /// <summary>Which of the watched offer was taken (from the pick, or the card they ended up with), or -1.</summary>
        private int WatchedPick(GameState s)
        {
            if (s == null || _slot < 0 || _slot >= s.Players.Length) return -1;
            var p = s.Players[_slot];
            var card = p.Picked >= 0 ? p.Picked : p.Offer.Length == 0 && p.Cards.Length > 0 ? p.Cards[p.Cards.Length - 1] : -1;
            return card < 0 ? -1 : System.Array.IndexOf(_watchOffer, card);
        }

        /// <summary>Watching: follow their highlight, and show their pick once they've made it.</summary>
        private void Watch()
        {
            if (_state == null || _slot < 0 || _slot >= _state.Players.Length) return;
            var p = _state.Players[_slot];
            var since = Time.unscaledTime - _stageAt;
            if (p.Hover >= 1 && p.Hover <= _cards.Count && since > 0.5f) SetFocus(p.Hover - 1);
            if (since > 1.1f)
            {
                var pick = WatchedPick(_state);
                if (pick >= 0) { SetFocus(pick); Leave(pick); }
            }
        }

        /// <summary>
        /// Team Vote: deal one shared offer for the team that's picking, and let every local person on it vote at
        /// once (their cursors and names show under the cards). If nobody here is picking, watch the other side.
        /// </summary>
        private void BeginTeamVote(GameState s)
        {
            // a local human still to vote?
            var local = new List<int>();
            int[] offer = null;
            for (var i = 0; i < s.Players.Length; i++)
            {
                var p = s.Players[i];
                if (p.Offer.Length == 0 || p.Picked >= 0 || _ready.ContainsKey(i)) continue;
                var slot = SlotOf(i);
                if (slot < 0 || slot >= Local.Slots.Count || Local.Slots[slot].Demo) continue;
                // only those with the same hand share the screen (one may have a one-a-team card: they get their own turn)
                if (offer != null && !SameCards(offer, p.Offer)) continue;
                local.Add(i);
                if (offer == null) offer = p.Offer;
            }
            if (local.Count > 0 && offer != null)
            {
                _watching = false;
                _teamVote = true;
                _voters.Clear();
                _voters.AddRange(local);
                _cursors.Clear();
                var mid = offer.Length / 2;
                foreach (var i in local) _cursors[i] = mid;
                _shared = (int[])offer.Clone();
                Deal(local[0], offer);
                FollowTeammates();
                return;
            }
            // nobody here: watch someone else (same offer for the whole team, so one deal is enough)
            var w = NextWatched(s);
            if (w < 0) return;
            _watching = true;
            _teamVote = true;
            _watchOffer = (int[])s.Players[w].Offer.Clone();
            _watched.Add(w);
            _cursors.Clear();
            _voters.Clear();
            // every remote voter on that team gets a cursor from their Hover
            for (var i = 0; i < s.Players.Length; i++)
            {
                var p = s.Players[i];
                if (p.Team != s.Players[w].Team || p.Offer.Length == 0 || p.Picked >= 0) continue;
                _watched.Add(i);
                if (p.Hover >= 1 && p.Hover <= p.Offer.Length) _cursors[i] = p.Hover - 1;
                else _cursors[i] = p.Offer.Length / 2;
            }
            Deal(w, _watchOffer);
        }

        /// <summary>Team Vote: each local voter moves their own cursor and confirms; when all have voted, the cards go.</summary>
        private void ChooseTeamVote()
        {
            if (_cards.Count == 0) return;
            if (_watching) { WatchTeamVote(); return; }
            var since = Time.unscaledTime - _stageAt;
            var readyGate = since > 0.55f;
            for (var n = _voters.Count - 1; n >= 0; n--)
            {
                var player = _voters[n];
                if (Voted(player)) continue;
                var slot = SlotOf(player);
                if (slot < 0 || slot >= Local.Slots.Count) continue;
                var m = Local.MenuFor(slot);
                int focus;
                if (!_cursors.TryGetValue(player, out focus)) focus = _cards.Count / 2;
                if (m.X != 0)
                {
                    var to = Mathf.Clamp(focus + m.X, 0, _cards.Count - 1);
                    if (to != focus) { _cursors[player] = to; Sound.Move(); }
                }
                // mouse: the first local voter still choosing steers with it (keyboard players especially)
                if (n == 0)
                {
                    var mouse = UnityEngine.InputSystem.Mouse.current;
                    if (mouse != null && mouse.delta.ReadValue().sqrMagnitude > 0.5f && Time.unscaledTime - _lastHover > 0.12f)
                    {
                        var mp = mouse.position.ReadValue();
                        for (var k = 0; k < _cards.Count; k++)
                            if (ScreenRect(_cards[k]).Contains(mp)) { if (k != focus) { _cursors[player] = k; focus = k; Sound.Move(); _lastHover = Time.unscaledTime; } break; }
                    }
                    if (mouse != null && mouse.leftButton.wasPressedThisFrame && ScreenRect(_cards[_cursors[player]]).Contains(mouse.position.ReadValue()))
                        m.Confirm = true;
                }
                if (m.Confirm && readyGate)
                {
                    _ready[player] = (byte)(_cursors[player] + 1);
                    Sound.Play(Sfx.CardPick, 0.55f);
                }
            }
            // lift the cards that have any cursor on them a little (the primary focus is the first voter's)
            if (_voters.Count > 0 && _cursors.ContainsKey(_voters[0])) _focus = _cursors[_voters[0]];
            // everyone here has chosen, and the rest of the team (online, bots) too: put the cards away
            var othersIn = FollowTeammates();
            var allIn = _voters.Count > 0 && othersIn;
            foreach (var v in _voters) if (!Voted(v)) allIn = false;
            if (allIn) LeaveTeamVote();
        }

        /// <summary>The cards dealt for a shared draw (Team Vote / Same Draw) to this PC's choosers.</summary>
        private int[] _shared = new int[0];

        /// <summary>A local chooser has confirmed (waiting for the game) or the game has their pick.</summary>
        private bool Voted(int player)
        {
            return _ready.ContainsKey(player) || (_state != null && player >= 0 && player < _state.Players.Length && _state.Players[player].Picked >= 0);
        }

        /// <summary>
        /// A shared draw: a cursor for everyone else on the team with the same cards (online players, bots), from
        /// what they're looking at; locked on their pick once they've made it. True once they've all chosen.
        /// </summary>
        private bool FollowTeammates()
        {
            if (_state == null || _voters.Count == 0 || _voters[0] >= _state.Players.Length) return true;
            var team = _state.Players[_voters[0]].Team;
            var since = Time.unscaledTime - _stageAt;
            var all = true;
            for (var i = 0; i < _state.Players.Length; i++)
            {
                if (_voters.Contains(i)) continue;
                var p = _state.Players[i];
                if (p.Team != team || p.Offer.Length == 0 || !SameCards(p.Offer, _shared)) continue;
                if (p.Picked >= 0)
                {
                    var at = System.Array.IndexOf(_shared, p.Picked);
                    _cursors[i] = at >= 0 ? at : 0;
                    continue;
                }
                all = false;
                if (p.Hover >= 1 && p.Hover <= _cards.Count && since > 0.3f) _cursors[i] = p.Hover - 1;
                else if (!_cursors.ContainsKey(i)) _cursors[i] = Mathf.Max(0, _cards.Count / 2);
            }
            return all;
        }

        private static bool SameCards(int[] a, int[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (var k = 0; k < a.Length; k++) if (a[k] != b[k]) return false;
            return true;
        }

        /// <summary>Watching a team vote: follow everyone's Hover, and leave once they've all picked.</summary>
        private void WatchTeamVote()
        {
            if (_state == null) return;
            var since = Time.unscaledTime - _stageAt;
            var anyLeft = false;
            foreach (var i in new List<int>(_cursors.Keys))
            {
                if (i < 0 || i >= _state.Players.Length) continue;
                var p = _state.Players[i];
                if (p.Offer.Length == 0) continue;
                if (p.Picked >= 0)
                {
                    var offer = _watchOffer.Length > 0 ? _watchOffer : p.Offer;
                    var at = System.Array.IndexOf(offer, p.Picked);
                    _cursors[i] = at >= 0 ? at : 0;
                    continue;
                }
                anyLeft = true;
                if (p.Hover >= 1 && p.Hover <= _cards.Count && since > 0.5f) _cursors[i] = p.Hover - 1;
            }
            if (_cursors.Count > 0) { var e = _cursors.Keys.GetEnumerator(); e.MoveNext(); _focus = _cursors[e.Current]; }
            if (!anyLeft && since > 1.1f) LeaveTeamVote();
        }

        /// <summary>
        /// Team Vote: put the cards away. Locals: the card they locked (first voter) dives into that player and
        /// absorbs like a normal pick. Watching the other side: cards just leave (no dive).
        /// </summary>
        private void LeaveTeamVote()
        {
            var chosen = -1;
            if (!_watching)
            {
                byte pick;
                if (_voters.Count > 0 && _ready.TryGetValue(_voters[0], out pick) && pick >= 1)
                    chosen = pick - 1;
                else if (_focus >= 0 && _focus < _cards.Count)
                    chosen = _focus;
            }
            _chosen = chosen;
            _absorbed = false;
            _stage = Stage.Leaving;
            _stageAt = Time.unscaledTime;
            // confirm already played CardPick — just the dive / leave motion here
            for (var k = 0; k < _cards.Count; k++)
            {
                var c = _cards[k];
                if (k == chosen)
                {
                    c.ScaleVel += 7f;
                    c.RotVel.z += (k % 2 == 0 ? 1f : -1f) * 140f;
                    c.SweepAt = Time.unscaledTime;
                    c.GlowTarget = 1.2f;
                    Burst(c);
                }
                else c.Delay = Time.unscaledTime - _stageAt + 0.05f * Mathf.Abs(k - Mathf.Max(0, chosen));
            }
        }

        /// <summary>Does any team-vote cursor sit on card <paramref name="k"/>?</summary>
        private bool CursorOn(int k)
        {
            foreach (var kv in _cursors) if (kv.Value == k) return true;
            return false;
        }

        // ------------------------------------------------------------------ dealing, choosing, leaving

        private void Deal(int slot, int[] offer)
        {
            _slot = slot;
            _focus = offer.Length / 2;
            _chosen = -1;
            _testStep = 0;
            _stage = Stage.Choosing;
            _stageAt = Time.unscaledTime;
            // three in a row; more fan out in an arc (like a rainbow), a little smaller, so they all fit on screen
            var n = offer.Length;
            _dealScale = n <= 3 ? 1f : n == 4 ? 0.9f : 0.82f;
            for (var k = 0; k < offer.Length; k++)
            {
                var def = Sim.Cards.Cards[offer[k]];
                var v = Build(def, k);
                float x, y = -0.3f;
                v.Fan = 0f;
                if (n <= 3) x = (k - (n - 1) / 2f) * 3.05f;
                else
                {
                    const float radius = 10f;
                    var spread = n == 4 ? 34f : 44f;                                   // degrees, edge to edge
                    var a = (k / (float)(n - 1) - 0.5f) * spread * Mathf.Deg2Rad;
                    x = Mathf.Sin(a) * radius;
                    y = -0.1f - (1f - Mathf.Cos(a)) * radius;
                    v.Fan = -a * Mathf.Rad2Deg;                                         // tilted along the arc
                }
                v.Home = new Vector3(x, y, 0f);
                v.Pos = new Vector3(x * 0.25f, -7.5f, 1f);
                v.Rot = new Vector3(0f, 180f, Random.Range(-28f, 28f));
                v.Scale = 0.75f;
                v.Delay = 0.12f + k * 0.11f;
                _cards.Add(v);
            }
        }

        private void Choose()
        {
            if (_cards.Count == 0) return;
            if (_watching) { Watch(); return; }
            var m = Local.MenuFor(Slot);
            if (TestMode)
            {
                // one scripted step at a time: right, left, left, then take it
                var since = Time.unscaledTime - _stageAt;
                m = new LocalPlayers.MenuInput();
                var steps = new[] { 2.2f, 3.6f, 4.4f, 6f };
                var dirs = new[] { 1, -1, -1, 0 };
                if (_testStep < steps.Length && since > steps[_testStep])
                {
                    if (dirs[_testStep] == 0) m.Confirm = true; else m.X = dirs[_testStep];
                    _testStep++;
                }
            }
            if (m.X != 0) SetFocus(Mathf.Clamp(_focus + m.X, 0, _cards.Count - 1));

            // mouse: hover to focus, click to pick (keyboard players especially)
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null)
            {
                var mp = mouse.position.ReadValue();
                // keep the focused card while the mouse is on it (cards overlap a little when one is lifted),
                // and don't flick between cards faster than the eye can follow
                if (mouse.delta.ReadValue().sqrMagnitude > 0.5f && !ScreenRect(_cards[_focus]).Contains(mp) && Time.unscaledTime - _lastHover > 0.12f)
                    for (var k = 0; k < _cards.Count; k++) if (ScreenRect(_cards[k]).Contains(mp)) { SetFocus(k); _lastHover = Time.unscaledTime; break; }
                if (mouse.leftButton.wasPressedThisFrame && ScreenRect(_cards[_focus]).Contains(mp)) m.Confirm = true;
            }
            // dealt cards need a moment before they can be taken, so a held button doesn't pick by accident
            if (m.Confirm && Time.unscaledTime - _stageAt > 0.55f) Leave(_focus);
        }

        private void SetFocus(int k)
        {
            if (k == _focus) return;
            var dir = k > _focus ? 1f : -1f;
            _focus = k;
            Sound.Move();
            var c = _cards[k];
            // a little kick so the change is felt (capped, so rapid changes can't build up)
            c.RotVel.z = Mathf.Clamp(c.RotVel.z - dir * 160f, -200f, 200f);
            c.ScaleVel = Mathf.Min(c.ScaleVel + 2.5f, 3f);
            c.SweepAt = Time.unscaledTime;
            c.NextSweep = Time.unscaledTime + 2.6f;
        }

        private void Leave(int chosen)
        {
            _chosen = chosen;
            _absorbed = false;
            _stage = Stage.Leaving;
            _stageAt = Time.unscaledTime;
            if (chosen >= 0 && chosen < _cards.Count) Sound.Play(Sfx.CardPick, 0.55f);
            for (var k = 0; k < _cards.Count; k++)
            {
                var c = _cards[k];
                if (k == chosen)
                {
                    c.ScaleVel += 7f;
                    c.RotVel.z += (k % 2 == 0 ? 1f : -1f) * 140f;
                    c.SweepAt = Time.unscaledTime;
                    c.GlowTarget = 1.2f;
                    Burst(c);
                }
                else c.Delay = Time.unscaledTime - _stageAt + 0.05f * Mathf.Abs(k - Mathf.Max(0, chosen));
            }
        }

        private void Animate()
        {
            var t = Time.unscaledTime;
            var dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
            var since = t - _stageAt;
            var tilt = PointerTilt();
            var live = _stage == Stage.Choosing || _stage == Stage.Editing;
            var size = _editing ? EditScale : _dealScale;
            if (_stage == Stage.Editing) EditHomes();
            for (var k = 0; k < _cards.Count; k++)
            {
                var c = _cards[k];
                Vector3 tp; Vector3 tr; float ts;
                if (live && !c.Dealt && since >= c.Delay)
                {
                    // its turn: off the deck with a flick (in the editor, only the rows on screen are heard)
                    c.Dealt = true;
                    var row = k / EditCols - _editTop;
                    if (!_editing || (row >= 0 && row < EditRows))
                        Sound.Play(Sfx.CardDeal, 0.5f, 1f + 0.04f * (k % 5), Mathf.Clamp(c.Home.x / 8f, -0.5f, 0.5f));
                }
                if (live && since < c.Delay)
                {
                    // still in the deck, waiting its turn
                    tp = c.Pos; tr = c.Rot; ts = c.Scale;
                }
                else if (_stage == Stage.Leaving && k == _chosen)
                {
                    // the pick: pops up front, comes at you, then dives down into the player who took it, who
                    // soaks it up in a burst of its colour (no player on screen: it flies up and away instead)
                    var into = AbsorbPoint();
                    if (since < 0.32f) { tp = new Vector3(0f, 0.2f, -2.2f); tr = Vector3.zero; ts = 1.32f; }
                    else if (since < 0.56f) { tp = new Vector3(0f, 0.5f, -5.5f); tr = new Vector3(-8f, 0f, 0f); ts = 1.4f; }
                    else if (into == null) { tp = new Vector3(0f, 9f, -1f); tr = Vector3.zero; ts = 0.6f; }
                    else
                    {
                        tp = into.Value;
                        tr = new Vector3(35f, 0f, (k % 2 == 0 ? 1f : -1f) * 160f);
                        ts = _absorbed ? 0.05f : 0.16f;
                        if (!_absorbed && ((c.Pos - into.Value).magnitude < 1.1f || since > 1.05f))
                        {
                            // gone the moment it reaches them
                            _absorbed = true;
                            _absorbedAt = t;
                            c.Root.gameObject.SetActive(false);
                            c.Shadow.gameObject.SetActive(false);
                            // the player soaks it up: ripples spread out across the rink from them, like water
                            if (OnAbsorbed != null) OnAbsorbed(_slot, c.Accent);
                        }
                    }
                }
                else if (_stage == Stage.Leaving)
                {
                    var go = since >= c.Delay;
                    tp = go ? c.Home + new Vector3(0f, -8.5f, 1.5f) : c.Home;
                    tr = go ? new Vector3(-20f, 180f, (k % 4 - 1) * 20f) : c.Rot;
                    ts = (go ? 0.8f : 1f) * size;
                }
                else
                {
                    var focused = k == _focus || (_teamVote && CursorOn(k));
                    var bob = Mathf.Sin(t * 1.7f + c.Phase) * 0.06f;
                    tp = c.Home + new Vector3(0f, bob * size + (focused ? 0.25f * size : 0f), focused ? (_editing ? -2.2f : -0.9f) : 0f);
                    tr = new Vector3(0f, 0f, Mathf.Sin(t * 1.2f + c.Phase) * 1.8f + c.Fan * (focused ? 0.25f : 1f));
                    if (focused) tr += new Vector3(-tilt.y * 16f, tilt.x * 18f, 0f);
                    else tr += new Vector3(Mathf.Sin(t * 0.9f + c.Phase) * 3f, Mathf.Cos(t * 0.8f + c.Phase) * 4f, 0f);
                    ts = (focused ? (_editing ? 1.3f : 1.12f) : 0.95f) * size;
                    // in the editor, cards you don't have sit back a little
                    if (_editing && !_browse && !focused && _counts[_who][k] == 0) ts *= 0.92f;
                }
                if (live)
                {
                    var focusedNow = k == _focus;
                    c.GlowTarget = (focusedNow ? 0.35f : 0f) + (_editing && !_browse && _counts[_who][k] > 0 ? 0.45f : 0f);
                    if (!c.Landed && since > c.Delay + 0.32f) { c.Landed = true; c.SweepAt = t; c.NextSweep = t + 2.6f + c.Phase * 0.3f; }
                    if (c.Landed && t > c.NextSweep && (focusedNow || c.Rare)) { c.SweepAt = t; c.NextSweep = t + (focusedNow ? 2.6f : 4.5f); }
                    // the top-tier cards shed little sparks of light
                    if (c.Rare && c.Landed && Settings.CardEffects)
                    {
                        c.MoteTimer -= dt;
                        if (c.MoteTimer <= 0f) { c.MoteTimer = 0.12f + Random.value * 0.1f; Spark(c); }
                    }
                }
                c.Step(tp, tr, ts, dt);
                c.Apply(_stage == Stage.Leaving ? (k == _chosen ? 1000 : k * 10) : k == _focus ? 1000 : k * 10, _cam);
                RefreshPrompt(c);
            }
        }

        /// <summary>Where the focused card should lean: towards the mouse when it's over the card, else the picker's right stick, else a slow drift.</summary>
        private bool _absorbed;
        /// <summary>How big the dealt cards are (smaller when more than three are offered).</summary>
        private float _dealScale = 1f;
        private float _absorbedAt;

        /// <summary>Where the player who took the card is, in the cards' space (on the table, under the cards), or null.</summary>
        private Vector3? AbsorbPoint()
        {
            if (PlayerOnScreen == null || _slot < 0) return null;
            var at = PlayerOnScreen(_slot);
            if (at == null || at.Value.z <= 0f) return null;
            var ray = _cam.ScreenPointToRay(new Vector3(at.Value.x, at.Value.y, 0f));
            if (Mathf.Abs(ray.direction.z) < 1e-4f) return null;
            var t = (0.5f - ray.origin.z) / ray.direction.z;
            return ray.origin + ray.direction * t;
        }

        private Vector2 PointerTilt()
        {
            var t = Time.unscaledTime;
            var drift = new Vector2(Mathf.Sin(t * 1.1f) * 0.45f, Mathf.Cos(t * 0.85f) * 0.3f);
            if (_cards.Count == 0 || _focus >= _cards.Count) return drift;
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null)
            {
                var r = ScreenRect(_cards[_focus]);
                var mp = mouse.position.ReadValue();
                if (r.Contains(mp)) return new Vector2((mp.x - r.center.x) / (r.width / 2f), (mp.y - r.center.y) / (r.height / 2f));
            }
            var stick = Local.AimOf(_editing ? _slot : Slot);
            return stick.sqrMagnitude > 0.04f ? stick : drift;
        }

        /// <summary>A card's area on screen (pixels, bottom-left origin), from its resting spot.</summary>
        private Rect ScreenRect(CardView c)
        {
            var s = Mathf.Max(0.5f, c.Scale);
            var a = _cam.WorldToScreenPoint(c.Root.position + new Vector3(-1.25f * s, -1.75f * s, 0f));
            var b = _cam.WorldToScreenPoint(c.Root.position + new Vector3(1.25f * s, 1.75f * s, 0f));
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        // ------------------------------------------------------------------ sparks

        private sealed class Mote
        {
            public GameObject Go;
            public Material Mat;
            public Vector3 Pos, Vel;
            public float Age, Life, Size, Drag;
            public Color Colour;
            public CardView Card; // sparks that ride along with a card; null for free ones
        }

        private readonly List<Mote> _motes = new List<Mote>();

        private Mote NewMote(Transform parent, Vector3 pos, Vector3 vel, float life, float size, Color colour, float drag)
        {
            var go = Quad("Spark", parent, new Vector2(size, size));
            var m = Mat(new Vector2(size, size), size / 2f);
            m.SetColor("_Color", colour);
            m.SetFloat("_Shadow", size / 2f);
            m.SetFloat("_SrcBlend", 5f); m.SetFloat("_DstBlend", 1f);
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            go.transform.localPosition = pos;
            var mote = new Mote { Go = go, Mat = m, Pos = pos, Vel = vel, Life = life, Size = size, Colour = colour, Drag = drag };
            _motes.Add(mote);
            return mote;
        }

        /// <summary>A gentle spark rising off a rare card.</summary>
        private void Spark(CardView c)
        {
            var gold = new Color(1f, 0.85f, 0.45f);
            var col = Color.Lerp(gold, c.Accent, Random.value * 0.5f);
            var pos = new Vector3(Random.Range(-1.2f, 1.2f), Random.Range(-1.7f, 0.6f), -0.03f);
            var m = NewMote(c.Tilt, pos, new Vector3(Random.Range(-0.15f, 0.15f), Random.Range(0.5f, 1.1f), 0f), Random.Range(0.9f, 1.6f), Random.Range(0.08f, 0.16f), col, 0.6f);
            m.Card = c;
        }

        /// <summary>A burst of sparks in the card's colour when it's taken.</summary>
        private void Burst(CardView c)
        {
            if (!Settings.CardEffects) return;
            for (var k = 0; k < 36; k++)
            {
                var a = Random.value * Mathf.PI * 2f;
                var speed = Random.Range(3f, 8f);
                var col = Color.Lerp(c.Accent, Color.white, Random.value * 0.6f);
                NewMote(_root, c.Root.localPosition + new Vector3(0f, 0f, -0.5f), new Vector3(Mathf.Cos(a) * speed, Mathf.Sin(a) * speed, 0f), Random.Range(0.45f, 0.9f), Random.Range(0.1f, 0.22f), col, 3.5f);
            }
        }

        private void Motes()
        {
            var dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
            for (var i = _motes.Count - 1; i >= 0; i--)
            {
                var m = _motes[i];
                m.Age += dt;
                if (m.Go == null || m.Age >= m.Life) { if (m.Go != null) Destroy(m.Go); _motes.RemoveAt(i); continue; }
                m.Vel *= Mathf.Exp(-m.Drag * dt);
                m.Pos += m.Vel * dt;
                m.Go.transform.localPosition = m.Pos;
                var k = m.Age / m.Life;
                var fade = Mathf.Min(1f, m.Age / 0.12f) * (1f - k * k);
                m.Mat.SetFloat("_Alpha", fade);
                m.Go.transform.localScale = Vector3.one * (0.6f + 0.4f * Mathf.Sin(Mathf.Min(1f, k * 2f) * Mathf.PI * 0.5f));
                var r = m.Go.GetComponent<MeshRenderer>();
                r.sortingOrder = m.Card != null ? m.Card.Order + 6 : 300;
                if (m.Card != null) r.enabled = Vector3.Dot(m.Card.Tilt.forward, _cam.transform.forward) > 0f;
            }
        }

        // ------------------------------------------------------------------ header text

        private void OnGUI()
        {
            if (_stage == Stage.Idle) return;
            if (!_browse && (_state == null || _slot < 0 || _slot >= _state.Players.Length)) return; // (the card list needs no game)
            GUI.depth = 5;
            Ui.Begin();
            if (_editing) { if (_browse) BrowseGUI(); else EditGUI(); return; }
            var team = _state.Players[_slot].Team;
            var appear = Ui.EaseOut((Time.unscaledTime - _stageAt) / 0.4f);
            var a = _stage == Stage.Leaving ? 1f - Ui.EaseOut((Time.unscaledTime - _stageAt) / 0.3f) : appear;
            var y = 34f + (1f - appear) * -20f;
            if (_teamVote)
            {
                Ui.Swatch(new Rect(Ui.W / 2f - 150f, y + 6f, 26f, 26f), team);
                var vote = Sim != null && Sim.T.TeamVote;
                Ui.Label(new Rect(Ui.W / 2f - 115f, y, 400f, 40f), Palette.TeamName(team) + (vote ? "  ·  team vote" : "  ·  same draw"), Ui.Font(22, true), new Color(1f, 1f, 1f, a));
                Ui.Label(new Rect(0f, y + 40f, Ui.W, 50f), _watching ? (vote ? "Watching the vote..." : "Watching them choose...") : vote ? "Vote for a card" : "Choose a card",
                    Ui.Font(40, true, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, a));
            }
            else
            {
                var name = NameOf != null ? NameOf(_slot) : "";
                if (string.IsNullOrEmpty(name)) name = "P" + (_slot + 1);
                var who = name + "  ·  " + Palette.TeamName(team);
                Ui.Swatch(new Rect(Ui.W / 2f - 150f, y + 6f, 26f, 26f), team);
                Ui.Label(new Rect(Ui.W / 2f - 115f, y, 400f, 40f), who, Ui.Font(22, true), new Color(1f, 1f, 1f, a));
                Ui.Label(new Rect(0f, y + 40f, Ui.W, 50f), _watching ? (_stage == Stage.Leaving && _chosen >= 0 ? name + " took a card" : name + " is choosing...") : "Choose a card",
                    Ui.Font(40, true, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, a));
            }

            // time left
            var noLimit = Sim.T.PickTime <= Fix.Zero;
            var left = noLimit ? 1f : Mathf.Clamp01(_state.PhaseTime.ToFloat() / Mathf.Max(0.01f, Sim.T.PickTime.ToFloat()));
            var bar = new Rect(Ui.W / 2f - 160f, y + 98f, 320f, 6f);
            Ui.Box(bar, new Color(1f, 1f, 1f, 0.12f * a), 3f);
            Ui.Box(new Rect(bar.x, bar.y, bar.width * left, bar.height), new Color(1f, 1f, 1f, (left < 0.2f ? 0.9f : 0.6f) * a), 3f);

            if (_teamVote && _stage == Stage.Choosing) DrawVoteCursors(a);

            if (_watching) { Ui.Label(new Rect(0f, Ui.H - 46f, Ui.W, 30f), _teamVote && Sim.T.TeamVote ? "Watching the other team vote" : "Watching the other team pick", Ui.Font(17, false, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, 0.6f * a)); return; }

            // hovering a different utility while you already have one: say it clearly replaces yours
            var replace = UtilityReplaceHint();
            if (replace != null)
                Ui.Label(new Rect(0f, y + 110f, Ui.W, 26f), replace, Ui.Font(16, true, TextAnchor.MiddleCenter), new Color(1f, 0.85f, 0.45f, 0.95f * a)); // above the cards, under the timer line

            var device = Slot >= 0 && Slot < Local.Slots.Count ? Local.Slots[Slot].Device : null;
            var hint = device is UnityEngine.InputSystem.Keyboard ? "{look} Look      {join} Take it  (or click)" : "{side} Look      {aim} Tilt      {confirm} Take it";
            Glyphs.Hint(new Rect(0f, Ui.H - 46f, Ui.W, 30f), hint, 17, new Color(1f, 1f, 1f, 0.6f * a), TextAnchor.MiddleCenter, device);
        }

        /// <summary>When the focused offer would replace the player's utility, a short plain-English line; else null.</summary>
        private string UtilityReplaceHint()
        {
            if (Sim == null || Sim.T.MultiUtility || Sim.Cards == null || _state == null) return null;
            if (_slot < 0 || _slot >= _state.Players.Length || _focus < 0 || _focus >= _cards.Count) return null;
            if (_stage != Stage.Choosing) return null;
            var p = _state.Players[_slot];
            // which library card is under the focus (offer index → library index)
            var offered = -1;
            if (p.Offer != null && _focus < p.Offer.Length) offered = p.Offer[_focus];
            else if (_teamVote && p.Offer != null && _focus < p.Offer.Length) offered = p.Offer[_focus];
            if (offered < 0) return null;
            if (!Sim.Cards.WouldReplaceUtility(p, offered)) return null;
            var held = Sim.Cards.UtilityHeld(p);
            var next = Sim.Cards.Cards[offered];
            if (held == null || next == null) return null;
            return "Replaces your " + held.Name + "  ·  you can only hold one utility";
        }

        /// <summary>Team Vote: each voter's name under the card their cursor is on (stacked if several share one).</summary>
        private void DrawVoteCursors(float a)
        {
            var under = new Dictionary<int, List<int>>();
            foreach (var kv in _cursors)
            {
                var card = kv.Value;
                if (card < 0 || card >= _cards.Count) continue;
                List<int> list;
                if (!under.TryGetValue(card, out list)) { list = new List<int>(); under[card] = list; }
                list.Add(kv.Key);
            }
            foreach (var kv in under)
            {
                var r = ScreenRect(_cards[kv.Key]);
                // ScreenRect is screen pixels (bottom-left); Ui is a 1280x720 canvas
                var scale = Screen.height / 720f * Settings.UiScale;
                var cx = r.center.x / scale;
                var top = (Screen.height - r.yMin) / scale;
                var row = 0;
                foreach (var player in kv.Value)
                {
                    var locked = _ready.ContainsKey(player) || (_state != null && player < _state.Players.Length && _state.Players[player].Picked >= 0);
                    var name = NameOf != null ? NameOf(player) : "";
                    if (string.IsNullOrEmpty(name)) name = "P" + (player + 1);
                    var pteam = _state != null && player < _state.Players.Length ? _state.Players[player].Team : 0;
                    var tw = Mathf.Max(80f, name.Length * 9f + 28f);
                    var yy = top + 10f + row * 30f;
                    // locked in: a green border round their cursor
                    if (locked) Ui.Box(new Rect(cx - tw / 2f - 3f, yy - 3f, tw + 6f, 28f), new Color(0.3f, 0.92f, 0.42f, a), 7f);
                    Ui.Box(new Rect(cx - tw / 2f, yy, tw, 22f), new Color(0f, 0f, 0f, (locked ? 0.85f : 0.55f) * a), 4f);
                    Ui.Swatch(new Rect(cx - tw / 2f + 4f, yy + 3f, 16f, 16f), pteam);
                    Ui.Label(new Rect(cx - tw / 2f + 24f, yy, tw - 28f, 22f), name, Ui.Font(15, true), new Color(1f, 1f, 1f, (locked ? 1f : 0.85f) * a));
                    row++;
                }
            }
        }

        // ------------------------------------------------------------------ free play's card editor

        // every card, four to a row and three rows on screen (scrolling); A adds a copy, B takes one away,
        // Start applies, holding B cancels. LB / RB: another person on this PC.
        private const int EditCols = 4, EditRows = 3;
        private const float EditScale = 0.54f, EditPitchX = 1.62f, EditPitchY = 2.06f, EditTopY = 1.32f;
        private const float CancelWait = 0.35f, CancelFill = 0.6f;
        private bool _editing;
        private Simulation _editSim;
        private readonly List<int> _people = new List<int>();
        private int _who, _editTop;
        private int[][] _counts;
        private float _cancelHeld;

        /// <summary>Open the card editor for everyone on this PC (not the bots) in free play.</summary>
        public void OpenEditor(Simulation sim, GameState s, IList<int> people)
        {
            if (_stage != Stage.Idle || sim.Cards == null || sim.Cards.Cards.Count == 0 || people.Count == 0) return;
            Sim = sim;
            _editSim = sim;
            _state = s;
            _people.Clear();
            _people.AddRange(people);
            _counts = new int[_people.Count][];
            for (var k = 0; k < _people.Count; k++)
            {
                _counts[k] = new int[sim.Cards.Cards.Count];
                foreach (var c in s.Players[_people[k]].Cards) if (c >= 0 && c < _counts[k].Length) _counts[k][c]++;
            }
            _who = 0;
            _slot = _people[0];
            _editing = true;
            _watching = false;
            _chosen = -1;
            _focus = 0;
            _editTop = 0;
            _cancelHeld = 0f;
            _stage = Stage.Editing;
            _stageAt = Time.unscaledTime;
            // dealt from below, face down, the rows on screen one after another (the rest wait off screen)
            for (var k = 0; k < sim.Cards.Cards.Count; k++)
            {
                var v = Build(sim.Cards.Cards[k], k);
                v.Home = EditHome(k);
                v.Pos = new Vector3(v.Home.x * 0.3f, -7.5f, 1f);
                v.Rot = new Vector3(0f, 180f, Random.Range(-28f, 28f));
                v.Scale = 0.75f * EditScale;
                var row = k / EditCols;
                v.Delay = row < EditRows ? 0.1f + row * 0.12f + (k % EditCols) * 0.05f : 0.5f;
                _cards.Add(v);
            }
        }

        private int EditCount { get { return _cards.Count; } }
        private int EditRowCount { get { return (EditCount + EditCols - 1) / EditCols; } }

        /// <summary>Where a card sits in the grid, scrolled to the rows on screen (others wait above or below).</summary>
        private Vector3 EditHome(int k)
        {
            var row = k / EditCols - _editTop;
            var x = (k % EditCols - (EditCols - 1) / 2f) * EditPitchX;
            var y = EditTopY - row * EditPitchY;
            if (row < 0) y += 4f;
            if (row >= EditRows) y -= 4f;
            return new Vector3(x, y, 0f);
        }

        private void EditHomes()
        {
            var row = _focus / EditCols;
            if (row < _editTop) _editTop = row;
            if (row >= _editTop + EditRows) _editTop = row - EditRows + 1;
            // on the last row, one step further, so it isn't cut off by the bottom of the screen
            if (row == EditRowCount - 1 && EditRowCount >= EditRows) _editTop = Mathf.Max(_editTop, row - EditRows + 2);
            _editTop = Mathf.Clamp(_editTop, 0, EditMaxTop);
            for (var k = 0; k < _cards.Count; k++) _cards[k].Home = EditHome(k);
        }

        /// <summary>How far the editor's grid scrolls: one row past the end, so the last row sits clear of the bottom.</summary>
        private int EditMaxTop { get { return EditRowCount >= EditRows ? EditRowCount - EditRows + 1 : 0; }
        }

        private int EditMax(int card)
        {
            var m = _editSim.Cards.Cards[card].MaxPerPlayer;
            return m > 0 ? m : 9;
        }

        private void EditInput()
        {
            if (_state == null || _people.Exists(i => i >= _state.Players.Length)) { EditClose(false); return; }
            var m = Local.MenuJoined();
            var ready = Time.unscaledTime - _stageAt > 0.4f; // a moment for the deal, so a held button doesn't add one
            // Start (or Enter) applies, first: in the menus it also counts as Back (or Confirm), which here would
            // take a card away (or add one) just before saving
            if (ready && Local.StartPressed()) { EditClose(true); return; }
            if (m.Tab != 0 && _people.Count > 1) { _who = (_who + m.Tab + _people.Count) % _people.Count; _slot = _people[_who]; foreach (var c in _cards) c.ScaleVel += 1.5f; Sound.Move(); }
            if (m.X != 0) SetFocus(Mathf.Clamp(_focus + m.X, 0, EditCount - 1));
            if (m.Y != 0)
            {
                var to = _focus - m.Y * EditCols;
                if (to >= 0 && to < EditCount) SetFocus(to);
                else if (m.Y < 0 && _focus / EditCols < EditRowCount - 1) SetFocus(EditCount - 1); // down onto a short last row
            }

            // mouse: hover to focus, left click adds, right click takes away, the wheel scrolls
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null)
            {
                var mp = mouse.position.ReadValue();
                if (mouse.delta.ReadValue().sqrMagnitude > 0.5f && !ScreenRect(_cards[_focus]).Contains(mp) && Time.unscaledTime - _lastHover > 0.12f)
                    for (var k = 0; k < _cards.Count; k++)
                    {
                        var row = k / EditCols - _editTop;
                        if (row < 0 || row >= EditRows || !ScreenRect(_cards[k]).Contains(mp)) continue;
                        SetFocus(k); _lastHover = Time.unscaledTime; break;
                    }
                var over = ScreenRect(_cards[_focus]).Contains(mp);
                if (ready && over && mouse.leftButton.wasPressedThisFrame) EditAdd(_focus, 1);
                if (ready && over && mouse.rightButton.wasPressedThisFrame) EditAdd(_focus, -1);
                var wheel = mouse.scroll.ReadValue().y;
                if (wheel != 0f)
                {
                    var r = Mathf.Clamp(_focus / EditCols + (wheel < 0f ? 1 : -1), 0, EditRowCount - 1);
                    SetFocus(Mathf.Min(EditCount - 1, r * EditCols + _focus % EditCols));
                }
            }
            if (ready && m.Confirm) EditAdd(_focus, 1);
            if (ready && m.Back) EditAdd(_focus, -1);

            // hold B (or Escape) to cancel: the bar only starts after a moment, so taking cards away doesn't trip it
            var held = false;
            foreach (var s in Local.Slots)
            {
                if (s.Demo) continue;
                if (Local.AnyHeld(s, g => g.buttonEast.isPressed, k => k.escapeKey.isPressed || k.backspaceKey.isPressed)) held = true;
            }
            _cancelHeld = held ? _cancelHeld + Time.unscaledDeltaTime : 0f;
            if (CancelProgress >= 1f) EditClose(false);
        }

        private float CancelProgress { get { return Mathf.Clamp01((_cancelHeld - CancelWait) / CancelFill); } }

        private void EditAdd(int card, int d)
        {
            var counts = _counts[_who];
            var c = _cards[card];
            var was = counts[card];
            counts[card] = Mathf.Clamp(was + d, 0, EditMax(card));
            if (counts[card] == was)
            {
                // can't: a little shake
                c.RotVel.z += (Random.value < 0.5f ? -1f : 1f) * 220f;
                Sound.Deny();
                return;
            }
            if (d > 0) Sound.Select(); else Sound.Back();
            // one utility at a time: adding a different utility clears the others for this player
            if (d > 0 && _editSim != null && !_editSim.T.MultiUtility && _editSim.Cards.IsUtilityIndex(card))
                for (var i = 0; i < counts.Length; i++)
                    if (i != card && _editSim.Cards.IsUtilityIndex(i)) counts[i] = 0;
            if (d > 0)
            {
                // a pop and a puff of sparks in its colour
                c.ScaleVel += 4.5f;
                c.SweepAt = Time.unscaledTime;
                if (Settings.CardEffects)
                    for (var k = 0; k < 14; k++)
                    {
                        var a = Random.value * Mathf.PI * 2f;
                        var speed = Random.Range(2f, 5f);
                        NewMote(_root, c.Root.localPosition + new Vector3(0f, 0f, -0.6f), new Vector3(Mathf.Cos(a) * speed, Mathf.Sin(a) * speed, 0f), Random.Range(0.35f, 0.7f), Random.Range(0.06f, 0.14f), Color.Lerp(c.Accent, Color.white, Random.value * 0.6f), 4f);
                    }
            }
            else
            {
                // a dip and a twist
                c.ScaleVel -= 3.5f;
                c.RotVel.z += (card % 2 == 0 ? 1f : -1f) * 120f;
            }
        }

        private void EditClose(bool apply)
        {
            foreach (var s in Local.Slots) s.Latched = 0; // the buttons pressed here don't carry into play
            if (apply) Sound.Select(); else Sound.Back();
            if (apply && _state != null)
            {
                for (var k = 0; k < _people.Count; k++)
                {
                    if (_people[k] >= _state.Players.Length) continue;
                    var p = _state.Players[_people[k]];
                    // keep the cards they still have in the order they got them, then the new ones
                    var left = (int[])_counts[k].Clone();
                    var list = new List<int>();
                    foreach (var c in p.Cards) if (c >= 0 && c < left.Length && left[c] > 0) { list.Add(c); left[c]--; }
                    for (var c = 0; c < left.Length; c++) for (var n = 0; n < left[c]; n++) list.Add(c);
                    p.Cards = list.ToArray();
                }
                _editSim.Cards.Recompute(_state);
                _editSim.PlacePylons(_state); // Pylon Goalies show up (or go) straight away
            }
            // everything drops away: the rows on screen in a quick ripple, the rest at once
            _chosen = -1;
            _stage = Stage.Leaving;
            _stageAt = Time.unscaledTime;
            for (var k = 0; k < _cards.Count; k++)
            {
                var row = k / EditCols - _editTop;
                _cards[k].Delay = row >= 0 && row < EditRows ? 0.03f * (row * EditCols + k % EditCols) : 0f;
            }
            _applied = apply;
        }

        private bool _applied;

        private float[] _badgeShow = new float[0];

        /// <summary>
        /// How much to show badge <paramref name="slot"/> of card <paramref name="k"/> (at <paramref name="badge"/>, GUI
        /// units): it fades out while the card in front (the focused one) covers that badge, and back in after, so a
        /// focused card bobbing near it never makes it flicker.
        /// </summary>
        private float BadgeShow(int k, int slot, Rect badge)
        {
            var i = k * 2 + slot;
            if (_badgeShow.Length != _cards.Count * 2) { _badgeShow = new float[_cards.Count * 2]; for (var j = 0; j < _badgeShow.Length; j++) _badgeShow[j] = 1f; }
            if (i < 0 || i >= _badgeShow.Length) return 1f;
            if (Event.current == null || Event.current.type == EventType.Repaint)
            {
                var covered = false;
                if (k != _focus && _focus >= 0 && _focus < _cards.Count)
                {
                    var f = ScreenRect(_cards[_focus]);
                    var k2 = Screen.height / Ui.H;
                    var front = new Rect(f.xMin / k2, (Screen.height - f.yMax) / k2, f.width / k2, f.height / k2);
                    covered = front.Overlaps(badge);
                }
                _badgeShow[i] = Mathf.MoveTowards(_badgeShow[i], covered ? 0f : 1f, Time.unscaledDeltaTime * 7f);
            }
            return _badgeShow[i];
        }

        private void EditGUI()
        {
            var leaving = _stage == Stage.Leaving;
            var since = Time.unscaledTime - _stageAt;
            var a = leaving ? 1f - Ui.EaseOut(since / 0.3f) : Ui.EaseOut(since / 0.4f);
            if (a <= 0.01f) return;
            float w = Ui.W, h = Ui.H;
            var y = 18f + (1f - a) * -20f;
            var who = _people[_who];
            var name = NameOf != null ? NameOf(who) : "";
            if (string.IsNullOrEmpty(name)) name = "P" + (who + 1);
            var total = 0;
            foreach (var n in _counts[_who]) total += n;
            Ui.Label(new Rect(0f, y, w, 46f), leaving ? (_applied ? "Cards applied" : "No changes") : "Your cards", Ui.Font(36, true, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, a));
            var sub = name + "  ·  " + (total == 1 ? "1 card" : total + " cards") + (_people.Count > 1 ? "  ·  LB / RB: another player" : "");
            Ui.Label(new Rect(0f, y + 42f, w, 26f), sub, Ui.Font(17, false, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, 0.7f * a));
            if (leaving) return;

            // how many of each you have, on the cards (the focused one last, since it sits in front)
            var k2 = Screen.height / Ui.H;
            for (var pass = 0; pass < 2; pass++)
                for (var k = 0; k < _cards.Count; k++)
                {
                    if ((k == _focus) != (pass == 1)) continue;
                    var n = _counts[_who][k];
                    var row = k / EditCols - _editTop;
                    if (n == 0 || row < 0 || row >= EditRows) continue;
                    var r = ScreenRect(_cards[k]);
                    var badge = new Rect(r.xMax / k2 - 34f, (Screen.height - r.yMax) / k2 - 10f, 46f, 30f);
                    var show = BadgeShow(k, 0, badge);
                    if (show <= 0.01f) continue;
                    Ui.Box(badge, new Color(1f, 1f, 1f, show), 15f);
                    Ui.Label(badge, "×" + n, Ui.Font(18, true, TextAnchor.MiddleCenter), new Color(0.08f, 0.08f, 0.09f, show), false);
                }
            // scroll position
            if (EditRowCount > EditRows)
            {
                var track = new Rect(w - 30f, 130f, 6f, h - 200f);
                Ui.Box(track, new Color(1f, 1f, 1f, 0.12f), 3f);
                var thumbH = track.height * EditRows / EditRowCount;
                var at = (float)_editTop / Mathf.Max(1, EditMaxTop);
                Ui.Box(new Rect(track.x, track.y + (track.height - thumbH) * at, track.width, thumbH), new Color(1f, 1f, 1f, 0.6f), 3f);
            }
            // what the focused card is at, and the controls
            var focusDef = _editSim.Cards.Cards[_focus];
            var max = focusDef.MaxPerPlayer > 0 ? " of " + focusDef.MaxPerPlayer : "";
            Ui.Label(new Rect(0f, h - 70f, w, 24f), focusDef.Name + ":  " + _counts[_who][_focus] + max, Ui.Font(16, true, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, 0.8f));
            Glyphs.Hint(new Rect(0f, h - 44f, w, 30f), "{nav} Card      {confirm} Add      {back} Remove      {start} Apply      Hold {back} Cancel", 16, new Color(1f, 1f, 1f, 0.7f));
            var cancel = CancelProgress;
            if (cancel > 0f) Hud.LeavingBar(new Rect(w / 2f - 150f, h - 104f, 300f, 24f), cancel, "Cancel the changes");
        }

        // ------------------------------------------------------------------ the main menu's card list (pick stats)

        private bool _browse;
        private CardLibrary _browseLib;
        /// <summary>Card index at each place in the grid (the browser sorts them).</summary>
        private readonly List<int> _order = new List<int>();
        private int _sort;
        private static readonly string[] SortNames = { "Your favourites", "Everyone's favourites", "A to Z" };
        private int _personalTotal, _globalTotal;
        /// <summary>The main menu's card list closed (the menu comes back).</summary>
        public System.Action OnBrowseClosed;

        /// <summary>Open the card list from the main menu: every card, sorted by how often it's picked online.</summary>
        public void OpenBrowser(CardLibrary lib)
        {
            if (_stage != Stage.Idle || lib == null || lib.Cards.Count == 0) return;
            _browseLib = lib;
            _browse = true;
            _editing = true;
            _watching = false;
            _people.Clear();
            _counts = new[] { new int[lib.Cards.Count] };
            _who = 0;
            _slot = -1;
            _chosen = -1;
            _focus = 0;
            _editTop = 0;
            _cancelHeld = 0f;
            _personalTotal = 0; _globalTotal = 0;
            foreach (var c in lib.Cards) { _personalTotal += CardStats.Personal(c.Id); _globalTotal += CardStats.Global(c.Id); }
            _sort = _personalTotal > 0 ? 0 : _globalTotal > 0 ? 1 : 2;
            _stage = Stage.Editing;
            _stageAt = Time.unscaledTime;
            SortOrder();
            for (var p = 0; p < _order.Count; p++)
            {
                var v = Build(lib.Cards[_order[p]], p);
                v.Home = EditHome(p);
                v.Pos = new Vector3(v.Home.x * 0.3f, -7.5f, 1f);
                v.Rot = new Vector3(0f, 180f, Random.Range(-28f, 28f));
                v.Scale = 0.75f * EditScale;
                var row = p / EditCols;
                v.Delay = row < EditRows ? 0.1f + row * 0.12f + (p % EditCols) * 0.05f : 0.5f;
                _cards.Add(v);
            }
        }

        private void SortOrder()
        {
            var cards = _browseLib.Cards;
            _order.Clear();
            for (var k = 0; k < cards.Count; k++) _order.Add(k);
            var mine = new int[cards.Count];
            var all = new int[cards.Count];
            for (var k = 0; k < cards.Count; k++) { mine[k] = CardStats.Personal(cards[k].Id); all[k] = CardStats.Global(cards[k].Id); }
            _order.Sort((a, b) =>
            {
                int d = 0;
                if (_sort == 0) { d = mine[b].CompareTo(mine[a]); if (d == 0) d = all[b].CompareTo(all[a]); }
                else if (_sort == 1) { d = all[b].CompareTo(all[a]); if (d == 0) d = mine[b].CompareTo(mine[a]); }
                if (d == 0) d = string.Compare(cards[a].Name, cards[b].Name, System.StringComparison.OrdinalIgnoreCase);
                return d != 0 ? d : a.CompareTo(b);
            });
        }

        /// <summary>A new sort: the cards fly to their new places, and the focus stays on the same card.</summary>
        private void Resort(int d)
        {
            var focusCard = _focus >= 0 && _focus < _order.Count ? _order[_focus] : -1;
            var byCard = new Dictionary<int, CardView>();
            for (var p = 0; p < _order.Count; p++) byCard[_order[p]] = _cards[p];
            _sort = (_sort + d + SortNames.Length) % SortNames.Length;
            SortOrder();
            for (var p = 0; p < _order.Count; p++) _cards[p] = byCard[_order[p]];
            _focus = Mathf.Max(0, _order.IndexOf(focusCard));
            _editTop = 0;
            EditHomes();
            foreach (var c in _cards) c.ScaleVel += 1.2f;
            Sound.Move();
        }

        private void BrowseInput()
        {
            // the main menu's controls: any controller or the keyboard (nobody has joined a game here)
            var m = Local.Menu();
            var ready = Time.unscaledTime - _stageAt > 0.4f;
            if (ready && m.Back) { BrowseClose(); return; }
            if (ready && m.Tab != 0) Resort(m.Tab);
            if (m.X != 0) SetFocus(Mathf.Clamp(_focus + m.X, 0, EditCount - 1));
            if (m.Y != 0)
            {
                var to = _focus - m.Y * EditCols;
                if (to >= 0 && to < EditCount) SetFocus(to);
                else if (m.Y < 0 && _focus / EditCols < EditRowCount - 1) SetFocus(EditCount - 1);
            }
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null)
            {
                var mp = mouse.position.ReadValue();
                if (mouse.delta.ReadValue().sqrMagnitude > 0.5f && !ScreenRect(_cards[_focus]).Contains(mp) && Time.unscaledTime - _lastHover > 0.12f)
                    for (var k = 0; k < _cards.Count; k++)
                    {
                        var row = k / EditCols - _editTop;
                        if (row < 0 || row >= EditRows || !ScreenRect(_cards[k]).Contains(mp)) continue;
                        SetFocus(k); _lastHover = Time.unscaledTime; break;
                    }
                if (ready && mouse.rightButton.wasPressedThisFrame) { BrowseClose(); return; }
                if (ready && mouse.leftButton.wasPressedThisFrame && new Rect(Ui.W / 2f - 220f, 58f, 440f, 34f).Contains(GuiMouse(mp))) Resort(1);
                var wheel = mouse.scroll.ReadValue().y;
                if (wheel != 0f)
                {
                    var r = Mathf.Clamp(_focus / EditCols + (wheel < 0f ? 1 : -1), 0, EditRowCount - 1);
                    SetFocus(Mathf.Min(EditCount - 1, r * EditCols + _focus % EditCols));
                }
            }
        }

        /// <summary>A mouse position (pixels, bottom-left origin) in the GUI's own units (top-left origin).</summary>
        private static Vector2 GuiMouse(Vector2 mp)
        {
            var k = Screen.height / Ui.H;
            return new Vector2(mp.x / k, (Screen.height - mp.y) / k);
        }

        /// <summary>Close the main menu's card list (if it's open).</summary>
        public void CloseBrowser() { if (_browse && _stage == Stage.Editing) BrowseClose(); }

        private void BrowseClose()
        {
            foreach (var s in Local.Slots) s.Latched = 0;
            Sound.Back();
            _chosen = -1;
            _stage = Stage.Leaving;
            _stageAt = Time.unscaledTime;
            for (var k = 0; k < _cards.Count; k++)
            {
                var row = k / EditCols - _editTop;
                _cards[k].Delay = row >= 0 && row < EditRows ? 0.03f * (row * EditCols + k % EditCols) : 0f;
            }
        }

        private void BrowseGUI()
        {
            var leaving = _stage == Stage.Leaving;
            var since = Time.unscaledTime - _stageAt;
            var a = leaving ? 1f - Ui.EaseOut(since / 0.3f) : Ui.EaseOut(since / 0.4f);
            if (a <= 0.01f) return;
            float w = Ui.W, h = Ui.H;
            var y = 18f + (1f - a) * -20f;
            Ui.Label(new Rect(0f, y, w, 46f), "Cards", Ui.Font(36, true, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, a));
            var sortRect = new Rect(w / 2f - 220f, y + 40f, 440f, 34f);
            Ui.Box(sortRect, new Color(1f, 1f, 1f, 0.1f * a), 17f);
            Ui.Label(sortRect, "Sorted by:  " + SortNames[_sort], Ui.Font(18, true, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, 0.9f * a));
            // A to Z is just the cards: no places, no pick counts
            var stats = _sort != 2;
            var from = CardStats.PeopleCount;
            var sub = "Picks in online matches  ·  you: " + _personalTotal + "  ·  everyone: " + _globalTotal
                      + (from > 0 ? " (from " + (from == 1 ? "1 player" : from + " players") + " met so far)" : "");
            if (stats) Ui.Label(new Rect(0f, y + 78f, w, 22f), sub, Ui.Font(14, false, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, 0.6f * a));
            if (leaving) return;

            // each card's numbers, under it: place, your picks, everyone's (and their share)
            var k2 = Screen.height / Ui.H;
            for (var p = 0; p < _cards.Count && stats; p++)
            {
                var row = p / EditCols - _editTop;
                if (row < 0 || row >= EditRows) continue;
                var r = ScreenRect(_cards[p]);
                var id = _browseLib.Cards[_order[p]].Id;
                int mine = CardStats.Personal(id), all = CardStats.Global(id);
                var share = _globalTotal > 0 ? Mathf.RoundToInt(100f * all / _globalTotal) + "%" : "–";
                var place = new Rect(r.xMin / k2 - 12f, (Screen.height - r.yMax) / k2 - 12f, 40f, 30f);
                var showPlace = BadgeShow(p, 0, place);
                if (showPlace > 0.01f)
                {
                    Ui.Box(place, new Color(0.08f, 0.08f, 0.09f, 0.85f * showPlace), 15f);
                    Ui.Label(place, (p + 1).ToString(), Ui.Font(16, true, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, showPlace), false);
                }
                var pw = Mathf.Min(r.width / k2 - 6f, 170f);
                var line = new Rect(r.center.x / k2 - pw / 2f, (Screen.height - r.yMin) / k2 - 30f, pw, 22f);
                var showLine = BadgeShow(p, 1, line);
                if (p == _focus || showLine <= 0.01f) continue; // (the focused one is described at the bottom)
                Ui.Box(line, new Color(0.06f, 0.06f, 0.07f, 0.88f * showLine), 11f);
                Ui.Label(line, "You " + mine + "  ·  All " + all + " (" + share + ")", Ui.Font(12, true, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, 0.9f * showLine), false);
            }
            // the focused card, in words, on a dark strip along the bottom (with the controls)
            Ui.Box(new Rect(0f, h - 84f, w, 84f), new Color(0.03f, 0.03f, 0.04f, 0.9f), 0f);
            if (stats && _focus >= 0 && _focus < _order.Count)
            {
                var def = _browseLib.Cards[_order[_focus]];
                int mine = CardStats.Personal(def.Id), all = CardStats.Global(def.Id);
                var share = _globalTotal > 0 ? "  (" + Mathf.RoundToInt(100f * all / _globalTotal) + "% of all picks)" : "";
                Ui.Label(new Rect(0f, h - 74f, w, 26f), "#" + (_focus + 1) + "  " + def.Name + ":  you picked it " + mine + (mine == 1 ? " time" : " times")
                         + "  ·  everyone " + all + share, Ui.Font(17, true, TextAnchor.MiddleCenter), new Color(1f, 1f, 1f, 0.85f));
            }
            if (EditRowCount > EditRows)
            {
                var track = new Rect(w - 30f, 130f, 6f, h - 200f);
                Ui.Box(track, new Color(1f, 1f, 1f, 0.12f), 3f);
                var thumbH = track.height * EditRows / EditRowCount;
                var at = (float)_editTop / Mathf.Max(1, EditMaxTop);
                Ui.Box(new Rect(track.x, track.y + (track.height - thumbH) * at, track.width, thumbH), new Color(1f, 1f, 1f, 0.6f), 3f);
            }
            Glyphs.Hint(new Rect(0f, h - 44f, w, 30f), "{nav} Card      {tabs} Sort      {back} Back", 16, new Color(1f, 1f, 1f, 0.7f));
        }

        // ------------------------------------------------------------------ building a card

        private static readonly Color[] TierColors =
        {
            new Color(0.96f, 0.77f, 0.26f), // S gold
            new Color(0.73f, 0.55f, 1f),    // A purple
            new Color(0.36f, 0.66f, 0.96f), // B blue
            new Color(0.42f, 0.84f, 0.54f), // C green
            new Color(0.63f, 0.63f, 0.63f), // D grey
            new Color(0.76f, 0.48f, 0.35f), // F copper
        };

        /// <summary>A black box sized to one piece of text (its size is only known once Unity has laid the text out).</summary>
        private sealed class TextBox
        {
            public Renderer Text;
            public Transform Box;
            public Material Mat;
            private Vector2 _size;

            public void Fit()
            {
                var b = Text.localBounds;
                var size = b.size.x < 0.001f ? Vector2.zero : new Vector2(b.size.x + 0.07f, b.size.y + 0.035f);
                if (size == _size) return;
                _size = size;
                var at = Text.transform.localPosition + b.center;
                Box.localPosition = new Vector3(at.x, at.y, -0.0055f);
                Box.localScale = new Vector3(size.x, size.y, 1f);
                Mat.SetVector("_Size", new Vector4(Mathf.Max(size.x, 0.001f), Mathf.Max(size.y, 0.001f), 0.035f, 0f));
            }
        }

        private sealed class CardView
        {
            public Transform Root, Tilt;
            public Vector3 Home, Pos, PosVel, Rot, RotVel;
            /// <summary>Tilt along the fan when more than three are offered (degrees).</summary>
            public float Fan;
            public float Scale = 1f, ScaleVel, Delay, Phase;
            public readonly List<Renderer> Front = new List<Renderer>(), Back = new List<Renderer>(), Texts = new List<Renderer>(), BackTexts = new List<Renderer>();
            /// <summary>Each front part's place in the stack (body under picture).</summary>
            public readonly List<int> FrontOrder = new List<int>();
            public readonly List<Material> Finishes = new List<Material>();
            public Renderer Shadow;
            public Material ShadowMat;
            // effects
            public Material Glow, BodyMat, ArtMat;
            public Transform ArtT;
            public Vector3 ArtHome;
            /// <summary>Kenney button picture on "Press … to activate" (utility cards); updated when the input device changes.</summary>
            public MeshRenderer PromptIcon;
            public Material PromptMat;
            public string PromptAction;
            public InputMode.Family PromptFamily = (InputMode.Family)(-1);
            public Color Accent;
            public bool Rare;
            /// <summary>"textBoxes" cards: a snug black box behind each piece of text.</summary>
            public readonly List<TextBox> TextBoxes = new List<TextBox>();
            public float SweepAt = -10f, NextSweep, GlowBoost, GlowTarget, MoteTimer;
            public bool Landed;
            /// <summary>Has left the deck (its deal sound has played).</summary>
            public bool Dealt;
            public int Order;

            /// <summary>Springs towards the targets, overshooting a little, so every move has some bounce.</summary>
            public void Step(Vector3 tp, Vector3 tr, float ts, float dt)
            {
                PosVel += (tp - Pos) * (150f * dt);
                PosVel *= Mathf.Exp(-14f * dt);
                Pos += PosVel * dt;
                RotVel += (tr - Rot) * (210f * dt);
                RotVel *= Mathf.Exp(-16f * dt);
                Rot += RotVel * dt;
                ScaleVel += (ts - Scale) * (260f * dt);
                ScaleVel *= Mathf.Exp(-17f * dt);
                Scale = Mathf.Clamp(Scale + ScaleVel * dt, 0.04f, 1.6f);
                RotVel = Vector3.ClampMagnitude(RotVel, 900f);
            }

            public void Apply(int order, Camera cam)
            {
                Root.localPosition = Pos;
                Root.localScale = Vector3.one * Scale;
                Tilt.localRotation = Quaternion.Euler(Rot);
                // which side faces the camera decides what's drawn (text is double-sided, so hide the far side's)
                var front = Vector3.Dot(Tilt.forward, cam.transform.forward) > 0f;
                for (var k = 0; k < Front.Count; k++) { Front[k].enabled = front; Front[k].sortingOrder = order + FrontOrder[k]; }
                foreach (var r in Texts) { r.enabled = front; r.sortingOrder = order + 5; }
                foreach (var r in Back) { r.enabled = !front; r.sortingOrder = order + 1; }
                foreach (var r in BackTexts) { r.enabled = !front; r.sortingOrder = order + 5; }
                // the finishes follow the tilt (and a little of the movement), like light on a real card
                var tilt = new Vector4(Mathf.DeltaAngle(0f, Rot.y) / 22f + PosVel.x * 0.04f, -Mathf.DeltaAngle(0f, Rot.x) / 22f + PosVel.y * 0.03f, 0f, 0f);
                foreach (var m in Finishes) m.SetVector("_Tilt", tilt);
                foreach (var b in TextBoxes) b.Fit();
                // the shadow stays flat on the "table" and softens as the card lifts
                Shadow.transform.localPosition = new Vector3(Pos.x + 0.18f, Pos.y - 0.3f, 1.2f);
                Shadow.transform.localScale = Vector3.one * Scale * (1f + Mathf.Max(0f, -Pos.z) * 0.08f);
                ShadowMat.SetFloat("_Alpha", Mathf.Clamp01(0.55f - Mathf.Max(0f, -Pos.z) * 0.12f));
                Shadow.sortingOrder = order - 1;
                Order = order;

                // the picture slides a touch against the tilt, so it seems to sit deeper than the frame
                ArtT.localPosition = ArtHome + new Vector3(-tilt.x * 0.05f, -tilt.y * 0.05f, 0f);
                // a band of light crossing the card
                var sweep = (Time.unscaledTime - SweepAt) / 0.65f;
                var at = sweep < 1f ? Mathf.Lerp(-0.4f, 1.4f, Ui.EaseOut(sweep)) : -3f;
                BodyMat.SetFloat("_Sweep", at);
                ArtMat.SetFloat("_Sweep", at);
                // the border glow breathes, and brightens on the focused card
                GlowBoost = Mathf.Lerp(GlowBoost, GlowTarget, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 8f));
                var breathe = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2.4f + Phase);
                Glow.SetFloat("_Alpha", Settings.CardEffects ? (Rare ? 0.42f : 0.28f) + 0.12f * breathe + GlowBoost : 0f);
            }
        }

        private CardView Build(CardDef def, int index)
        {
            var v = new CardView { Phase = index * 2.1f + Random.value };
            v.Root = new GameObject("Card " + def.Name).transform;
            v.Root.SetParent(_root, false);
            v.Tilt = new GameObject("Tilt").transform;
            v.Tilt.SetParent(v.Root, false);
            var accent = ParseColor(def.Color, TierColors[(int)def.Tier]);
            var fx = Settings.CardEffects;
            int tier = (int)def.Tier;

            // shadow on the table
            var shadow = Quad("Shadow", _root, new Vector2(2.9f, 3.9f));
            v.ShadowMat = Mat(new Vector2(2.9f, 3.9f), 0.35f);
            v.ShadowMat.SetColor("_Color", new Color(0f, 0f, 0f, 1f));
            v.ShadowMat.SetFloat("_Shadow", 0.45f);
            v.Shadow = shadow.GetComponent<MeshRenderer>();
            v.Shadow.sharedMaterial = v.ShadowMat;
            shadow.transform.SetParent(v.Root.parent, false);

            // a soft glow around the border in the card's colour
            var glow = Part(v, "Glow", new Vector2(3.15f, 4.15f), new Vector3(0f, 0f, 0.01f), -1, 0.5f);
            glow.SetColor("_Color", new Color(accent.r, accent.g, accent.b, 0.9f));
            glow.SetFloat("_Shadow", 0.42f);
            glow.SetFloat("_SrcBlend", 5f); glow.SetFloat("_DstBlend", 1f); // additive: it lights up what's behind
            v.Glow = glow;
            v.Accent = accent;
            v.Rare = tier == 0;

            // body: dark card stock with a border in the card's colour
            var body = Part(v, "Body", new Vector2(2.5f, 3.5f), Vector3.zero, 0, 0.17f);
            body.SetColor("_Color", new Color(0.16f, 0.16f, 0.18f));
            body.SetColor("_Color2", new Color(0.075f, 0.075f, 0.085f));
            body.SetFloat("_Border", 0.075f);
            body.SetColor("_BorderColor", accent);
            // S: rainbow holo + sparkles. A: pearlescent (no holo). Lower tiers: soft glare only.
            // A card file's "finish" (foil / polychrome / holo / pearl) takes the place of the tier's holo / pearl.
            // "holo" / "pearl" give any card the S / A tier's look; no finish = the tier's own.
            var metal = def.Finish == "foil";
            var poly = def.Finish == "polychrome";
            var holo = def.Finish == "holo" || (def.Finish == null && tier == 0);
            var pearl = def.Finish == "pearl" || (def.Finish == null && tier == 1);
            Finish(body, fx,
                foil: holo ? 0.55f : 0f,
                glare: !def.Glare ? 0f : tier == 1 ? 0.55f : 0.45f,
                sparkle: holo ? 0.45f : 0f,
                pearl: pearl ? 1f : 0f,
                metal: metal ? 0.85f : 0f,
                poly: poly ? 0.8f : 0f);
            Special(body, fx, def.Finish, true);
            v.Finishes.Add(body);
            body.SetFloat("_Rim", fx ? (tier <= 1 ? 0.7f : 0.4f) : 0f);
            body.SetFloat("_SweepStrength", 0f); // no timed shine sweep (the owner preferred without it)
            v.BodyMat = body;

            // the picture
            var art = Part(v, "Art", new Vector2(2.02f, 2.02f), new Vector3(0f, 0.6f, -0.003f), 1, 0.12f);
            art.SetTexture("_MainTex", Art(def.Art));
            art.SetFloat("_Border", 0.018f);
            art.SetColor("_BorderColor", new Color(0f, 0f, 0f, 1f));
            Finish(art, fx,
                foil: holo ? 0.75f : 0f,
                glare: !def.Glare ? 0f : tier == 1 ? 0.5f : tier == 0 ? 0.42f : 0.28f,
                sparkle: holo ? 0.9f : 0f,
                pearl: pearl ? 1f : 0f,
                metal: metal ? 0.6f : 0f,
                poly: poly ? 0.65f : 0f);
            Special(art, fx, def.Finish, false);
            v.Finishes.Add(art);
            art.SetFloat("_SweepStrength", 0f);
            v.ArtMat = art;
            v.ArtT = v.Tilt.Find("Art");
            v.ArtHome = v.ArtT.localPosition;

            // title, flavour, coloured rule, then face rows (label left / value right) and optional footnote
            var hasFlavor = !string.IsNullOrEmpty(def.Flavor);
            var hasFace = def.Face != null && def.Face.Count > 0;
            Text(v, def.Name, new Vector3(0f, hasFlavor ? -0.54f : -0.66f, -0.006f), 170, 0.0158f, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter, v.Texts);
            if (hasFlavor)
                Text(v, Markup(def.Flavor), new Vector3(0f, -0.76f, -0.006f), 110, 0.0094f, new Color(0.78f, 0.78f, 0.82f), FontStyle.Normal, TextAnchor.MiddleCenter, v.Texts);
            var ruleY = hasFlavor ? -0.88f : -0.87f;
            var rule = Part(v, "Rule", new Vector2(1.55f, 0.022f), new Vector3(0f, ruleY, -0.005f), 2, 0f);
            rule.SetColor("_Color", new Color(accent.r, accent.g, accent.b, 0.85f));
            rule.SetColor("_Color2", new Color(accent.r, accent.g, accent.b, 0.85f));
            rule.SetFloat("_Glare", 0f);
            if (hasFace)
            {
                var rows = def.Face.Count;
                var step = rows >= 5 ? 0.125f : rows >= 4 ? 0.138f : 0.15f;
                var y = ruleY - 0.12f;
                // the note under the rows (bigger than it was, to be readable): squeeze the rows up a little if it
                // wouldn't fit above the card's bottom edge
                if (!string.IsNullOrEmpty(def.Text))
                {
                    var lines = NoteLines(def.Text, NoteWrap);
                    var need = lines * NoteFont * NoteChar * 0.145f + 0.05f;
                    var fit = (y - 0.02f - need - NoteBottom) / rows;
                    step = Mathf.Clamp(Mathf.Min(step, fit), 0.105f, step);
                }
                const float xL = -0.78f, xR = 0.78f;
                var font = rows >= 5 ? 100 : 112;
                var ch = rows >= 5 ? 0.0084f : 0.0092f;
                for (var i = 0; i < rows; i++)
                {
                    var row = def.Face[i];
                    Text(v, row.Label ?? "", new Vector3(xL, y, -0.006f), font, ch, new Color(0.92f, 0.92f, 0.95f), FontStyle.Normal, TextAnchor.MiddleLeft, v.Texts);
                    Text(v, Markup(row.Value), new Vector3(xR, y, -0.006f), font, ch, new Color(0.92f, 0.92f, 0.95f), FontStyle.Normal, TextAnchor.MiddleRight, v.Texts);
                    y -= step;
                }
                if (!string.IsNullOrEmpty(def.Text))
                    Footnote(v, def.Text, y - 0.02f, NoteFont, NoteChar, new Color(0.84f, 0.84f, 0.88f), NoteWrap);
            }
            else if (!string.IsNullOrEmpty(def.Text))
                Footnote(v, def.Text, -0.95f, 120, 0.0099f, new Color(0.88f, 0.88f, 0.9f));

            // "textBoxes": a black box behind each piece of text, just around the words (easier to read over a busy
            // finish like starfield)
            if (def.TextBoxes)
                foreach (var t in v.Texts)
                {
                    var m = Part(v, "TextBox", Vector2.one, Vector3.zero, 4, 0.035f);
                    m.SetColor("_Color", new Color(0f, 0f, 0f, 0.85f));
                    m.SetColor("_Color2", new Color(0f, 0f, 0f, 0.85f));
                    m.SetFloat("_Glare", 0f);
                    v.TextBoxes.Add(new TextBox { Text = t, Box = v.Tilt.GetChild(v.Tilt.childCount - 1), Mat = m });
                }

            // the back: dark with the game's name
            var back = Part(v, "Back", new Vector2(2.5f, 3.5f), Vector3.zero, 1, 0.17f, true);
            back.SetColor("_Color", new Color(0.13f, 0.13f, 0.15f));
            back.SetColor("_Color2", new Color(0.06f, 0.06f, 0.07f));
            back.SetFloat("_Border", 0.075f);
            back.SetColor("_BorderColor", new Color(0.3f, 0.3f, 0.33f));
            Finish(back, fx, foil: 0f, glare: 0.35f, sparkle: 0f, pearl: 0f);
            v.Finishes.Add(back);
            var backText = Text(v, "ULTRA\nPUCK", new Vector3(0f, 0f, 0.006f), 160, 0.017f, new Color(1f, 1f, 1f, 0.3f), FontStyle.Bold, TextAnchor.MiddleCenter, v.BackTexts);
            backText.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            return v;
        }

        // the shader's special finishes (_Special 1..8, see Card.shader) and how strong each is on the body / the picture
        private static readonly string[] SpecialNames = { "starfield", "gilded", "prism", "frosted", "neon", "aurora", "molten", "glitch" };
        private static readonly float[] SpecialBody = { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f };
        private static readonly float[] SpecialArt = { 0.8f, 0.5f, 0.7f, 0.8f, 0f, 0.6f, 0.5f, 1f };

        private static void Special(Material m, bool fx, string finish, bool body)
        {
            var k = finish == null ? -1 : System.Array.IndexOf(SpecialNames, finish);
            m.SetFloat("_Special", k + 1);
            m.SetFloat("_SpecialAmt", fx && k >= 0 ? (body ? SpecialBody[k] : SpecialArt[k]) : 0f);
        }

        private static void Finish(Material m, bool fx, float foil, float glare, float sparkle, float pearl, float metal = 0f, float poly = 0f)
        {
            m.SetFloat("_Metal", fx ? metal : 0f);
            m.SetFloat("_Poly", fx ? poly : 0f);
            m.SetFloat("_Foil", fx ? foil : 0f);
            m.SetFloat("_Pearl", fx ? pearl : 0f);
            m.SetFloat("_Glare", fx ? glare : Mathf.Min(glare, 0.12f)); // a card with "glare": false has none either way
            m.SetFloat("_Sparkle", fx ? sparkle : 0f);
        }

        private Material Part(CardView v, string name, Vector2 size, Vector3 at, int order, float radius, bool back = false)
        {
            var go = Quad(name, v.Tilt, size);
            go.transform.localPosition = at;
            if (back) go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var m = Mat(size, radius);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.sortingOrder = order;
            if (back) v.Back.Add(r); else { v.Front.Add(r); v.FrontOrder.Add(order); }
            return m;
        }

        private TextMesh Text(CardView v, string text, Vector3 at, int fontSize, float charSize, Color c, FontStyle style, TextAnchor anchor, List<Renderer> into)
        {
            var go = new GameObject("Text");
            go.layer = Layer;
            go.transform.SetParent(v.Tilt, false);
            go.transform.localPosition = at;
            var tm = go.AddComponent<TextMesh>();
            tm.font = _font;
            tm.fontSize = fontSize;
            tm.characterSize = charSize;
            tm.fontStyle = style;
            tm.anchor = anchor;
            tm.alignment = anchor == TextAnchor.MiddleLeft || anchor == TextAnchor.UpperLeft || anchor == TextAnchor.LowerLeft
                ? TextAlignment.Left
                : anchor == TextAnchor.MiddleRight || anchor == TextAnchor.UpperRight || anchor == TextAnchor.LowerRight
                    ? TextAlignment.Right
                    : TextAlignment.Center;
            tm.richText = true;
            tm.color = c;
            tm.text = text;
            tm.lineSpacing = 1.05f;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = _font.material;
            into.Add(r);
            return tm;
        }

        private GameObject Quad(string name, Transform parent, Vector2 size)
        {
            var go = new GameObject(name);
            go.layer = Layer;
            go.transform.SetParent(parent, false);
            var mesh = new Mesh();
            float w = size.x / 2f, h = size.y / 2f;
            mesh.vertices = new[] { new Vector3(-w, -h, 0f), new Vector3(w, -h, 0f), new Vector3(w, h, 0f), new Vector3(-w, h, 0f) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            mesh.triangles = new[] { 0, 3, 2, 0, 2, 1 };
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>();
            return go;
        }

        private Material Mat(Vector2 size, float radius)
        {
            var m = new Material(_shader != null ? _shader : Shader.Find("Sprites/Default"));
            m.SetVector("_Size", new Vector4(size.x, size.y, radius, 0f));
            return m;
        }

        /// <summary>
        /// Load every card's picture ahead of time, one a frame, so a card pick never stops to read and decode one
        /// (a big picture takes a moment, and online that moment makes everyone else wait).
        /// </summary>
        public System.Collections.IEnumerator PreloadArt(CardLibrary cards)
        {
            if (cards == null) yield break;
            foreach (var c in cards.Cards)
            {
                if (c.Art != null && !_art.ContainsKey(c.Art)) Art(c.Art);
                yield return null;
            }
        }

        public Texture2D Art(string id)
        {
            Texture2D tex;
            if (id != null && _art.TryGetValue(id, out tex)) return tex;
            tex = null;
            var dir = Path.Combine(Application.streamingAssetsPath, Path.Combine("Cards", "Art"));
            foreach (var ext in new[] { ".png", ".jpg" })
            {
                var path = Path.Combine(dir, (id ?? "") + ext);
                if (!File.Exists(path)) continue;
                var clock = System.Diagnostics.Stopwatch.StartNew();
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { anisoLevel = 4, filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp };
                tex.LoadImage(File.ReadAllBytes(path));
                Debug.Log("Ultra Puck: card art " + id + " (" + tex.width + "x" + tex.height + ") loaded in " + clock.ElapsedMilliseconds + " ms");
                break;
            }
            if (tex == null) tex = Texture2D.grayTexture;
            if (id != null) _art[id] = tex;
            return tex;
        }

        // ------------------------------------------------------------------ text helpers

        /// <summary>
        /// Footnote under the face rows. If the text has {utility} (or another action), draws "Press [button] to activate"
        /// with a Kenney icon that follows the device in use; any other words stay as normal text.
        /// </summary>
        // the note under a card's stat rows: size, and characters a line
        private const int NoteFont = 108, NoteWrap = 30;
        private const float NoteChar = 0.0089f, NoteBottom = -1.66f;

        /// <summary>How many lines a note takes (its words wrapped, plus the "Press [button] to use." line if it has one).</summary>
        private static int NoteLines(string raw, int wrap)
        {
            var action = ActionIn(raw);
            var text = raw;
            if (action != null)
            {
                var press = raw.IndexOf("Press", System.StringComparison.Ordinal);
                text = press > 0 ? raw.Substring(0, press).Trim() : "";
            }
            var lines = text.Length > 0 ? Wrap(Markup(text), wrap).Split((char)10).Length : 0;
            return lines + (action != null ? 1 : 0);
        }

        private void Footnote(CardView v, string raw, float y, int fontSize, float charSize, Color colour, int wrap = 34)
        {
            var action = ActionIn(raw);
            if (action == null)
            {
                Text(v, Wrap(Markup(raw), wrap), new Vector3(0f, y, -0.006f), fontSize, charSize, colour, FontStyle.Normal, TextAnchor.UpperCenter, v.Texts);
                return;
            }
            // words before "Press {…}" (a description) go above it, wrapped like any footnote
            var press = raw.IndexOf("Press", System.StringComparison.Ordinal);
            var before = press > 0 ? raw.Substring(0, press).Trim() : "";
            if (before.Length > 0)
            {
                var wrapped = Wrap(Markup(before), wrap);
                Text(v, wrapped, new Vector3(0f, y, -0.006f), fontSize, charSize, colour, FontStyle.Normal, TextAnchor.UpperCenter, v.Texts);
                y -= wrapped.Split((char)10).Length * fontSize * charSize * 0.145f + 0.03f;
            }
            // "Press" [icon] "to use." — tight gaps so the glyph sits with the words
            const float icon = 0.18f, gap = 0.02f;
            var mid = y - 0.02f;
            Text(v, "Press", new Vector3(-(icon * 0.5f + gap), mid, -0.006f), fontSize, charSize, colour, FontStyle.Normal, TextAnchor.MiddleRight, v.Texts);
            var go = Quad("PromptIcon", v.Tilt, new Vector2(icon, icon));
            go.transform.localPosition = new Vector3(0f, mid, -0.007f);
            var r = go.GetComponent<MeshRenderer>();
            r.sortingOrder = 6;
            v.PromptMat = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Transparent"));
            r.sharedMaterial = v.PromptMat;
            v.PromptIcon = r;
            v.PromptAction = action;
            v.PromptFamily = (InputMode.Family)(-1);
            v.Front.Add(r); // face-up only (same as body art), not in Texts so sorting stays above the words
            v.FrontOrder.Add(6);
            Text(v, "to use.", new Vector3(icon * 0.5f + gap, mid, -0.006f), fontSize, charSize, colour, FontStyle.Normal, TextAnchor.MiddleLeft, v.Texts);
            RefreshPrompt(v);
        }

        private static string ActionIn(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            var open = raw.IndexOf('{');
            var close = open >= 0 ? raw.IndexOf('}', open) : -1;
            if (open < 0 || close < 0) return null;
            return raw.Substring(open + 1, close - open - 1);
        }

        private static void RefreshPrompt(CardView v)
        {
            if (v.PromptIcon == null || v.PromptMat == null || string.IsNullOrEmpty(v.PromptAction)) return;
            InputMode.Update();
            if (v.PromptFamily == InputMode.Current) return;
            v.PromptFamily = InputMode.Current;
            var tex = InputIcons.ForAction(v.PromptAction, InputMode.Current);
            if (tex == null)
            {
                // no Kenney file: blank the quad (the word "Press … activate" still reads)
                v.PromptMat.mainTexture = Texture2D.whiteTexture;
                v.PromptMat.color = new Color(1f, 1f, 1f, 0.15f);
                return;
            }
            v.PromptMat.mainTexture = tex;
            v.PromptMat.color = Color.white;
        }

        /// <summary>Card markup to colours: [good] green, [bad] red, [note] grey, [gold] bright gold (big buffs).</summary>
        private static string Markup(string s)
        {
            return (s ?? "")
                .Replace("[good]", "<color=#7BE38A>")
                .Replace("[bad]", "<color=#FF7A7A>")
                .Replace("[note]", "<color=#A0A0A8>")
                .Replace("[gold]", "<color=#FFD24D>")
                .Replace("[/]", "</color>");
        }

        /// <summary>Wraps text to about <paramref name="width"/> letters a line (colour tags don't count).</summary>
        private static string Wrap(string s, int width)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var para in s.Split('\n'))
            {
                if (sb.Length > 0) sb.Append('\n');
                var line = 0;
                foreach (var word in para.Split(' '))
                {
                    var visible = Ui.StripColors(word).Length;
                    if (line > 0 && line + 1 + visible > width) { sb.Append('\n'); line = 0; }
                    else if (line > 0) { sb.Append(' '); line++; }
                    sb.Append(word);
                    line += visible;
                }
            }
            return sb.ToString();
        }

        private static Color ParseColor(string hex, Color fallback)
        {
            Color c;
            return hex != null && ColorUtility.TryParseHtmlString(hex, out c) ? c : fallback;
        }
    }
}
