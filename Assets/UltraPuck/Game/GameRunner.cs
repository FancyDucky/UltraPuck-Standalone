using System.Collections.Generic;
using System.IO;
using UltraPuck.Core;
using UltraPuck.Core.Net;
using UnityEngine;

namespace UltraPuck.Game
{
    /// <summary>
    /// Runs the game: the main menu (with bots playing behind it), the local lobby, local matches and free play at a
    /// fixed 120 ticks a second drawn smoothly in between, online games (Steam or IP, with rollback), the pause menu
    /// and settings, and the card pick screen. Created automatically when the game starts (see <see cref="Boot"/>).
    /// </summary>
    internal sealed class GameRunner : MonoBehaviour
    {
        private enum Mode { Menu, Lobby, Match, Online }

        private Mode _mode = Mode.Menu;
        private MainMenu _mainMenu;
        private NetSession _session;
        private uint _fingerprint;
        // the match the bots play behind the main menu
        private Simulation _attractSim;
        private GameState _attract, _attractPrev;
        private float _attractAcc;
        private MapDef _map;
        private CardLibrary _cards;
        /// <summary>The rules in use: <see cref="_matchSim"/> (first to 5, cards after goals) or <see cref="_freeSim"/> (free play).</summary>
        private Simulation _sim, _matchSim, _freeSim;
        private bool _free;
        private GameState _state, _prev;
        private float _acc;
        private string _error;

        private readonly LocalPlayers _local = new LocalPlayers();
        private Transform _entities;
        private readonly List<PlayerView> _players = new List<PlayerView>();
        private PuckView _puck;
        private Camera _cam;
        private Hud _hud;
        private Menus _menus;
        private CardScreen _cardScreen;
        private Effects _effects;
        private ShotViews _shots;
        private PylonViews _pylons;
        // what each player's effect counters were last frame, to spot new shockwaves and Team Play refreshes
        private readonly List<int> _seenShocks = new List<int>(), _seenRefreshes = new List<int>();
        private readonly float[] _cooldowns = new float[3];
        private float _shake;
        private readonly List<Vector2> _impactsThisFrame = new List<Vector2>();

        public static string MapsFolder { get { return Path.Combine(Application.streamingAssetsPath, "Maps"); } }
        public static string CardsFolder { get { return Path.Combine(Application.streamingAssetsPath, "Cards"); } }

        private void Awake()
        {
            Settings.ApplyGraphics();
            _hud = gameObject.AddComponent<Hud>();
            _menus = gameObject.AddComponent<Menus>();
            _cardScreen = gameObject.AddComponent<CardScreen>();
            _mainMenu = gameObject.AddComponent<MainMenu>();
            gameObject.AddComponent<Updater>();
            _mainMenu.Runner = this;
            _mainMenu.Local = _local;
            try
            {
                _map = MapDef.FromJson(File.ReadAllText(Path.Combine(MapsFolder, "test-rink.json")));
            }
            catch (System.Exception ex)
            {
                _error = "Couldn't load the map: " + ex.Message;
                Debug.LogError(_error);
                return;
            }
            _cards = LoadCards();
            _matchSim = new Simulation(_map, null, _cards);
            _freeSim = new Simulation(_map, Tuning.ForFreePlay(), _cards);
            _sim = _matchSim;
            _fingerprint = Fingerprint();
            _attractSim = new Simulation(_map, Tuning.ForFreePlay());
            _attract = _attractSim.NewGame(new[] { 0, 1, 0, 1 }, (uint)System.Environment.TickCount | 1u);
            _attractPrev = _attract.Clone();

            SteamNet.Init();
            SteamNet.OnJoined = host => StartOnline(NetSession.Join(SteamTransport.Join(host), MakeOnlineSim, _fingerprint));
            SteamNet.OnFailed = why => _mainMenu.Message = why;

            _cardScreen.Local = _local;
            LocalCardPicks();
            _menus.OnMainMenu = GoMainMenu;
            _menus.BotCount = () => _local.BotCount;
            _menus.MaxBots = () => LocalPlayers.MaxPlayers - _local.HumanCount;
            _menus.SetBots = SetBots;
            _menus.OnGetCard = PickACard;

            new RinkView(_map);
            _entities = new GameObject("Entities").transform;
            _puck = new PuckView(_sim.T.PuckRadius.ToFloat(), _entities);
            _effects = new Effects(_entities);
            _shots = new ShotViews(_entities);
            _pylons = new PylonViews(_entities);
            SetUpCamera();
            _mainMenu.Show(MainMenu.Page.Main);
            if (_local.AddDemoPlayers() > 0) StartMatch(System.Environment.GetEnvironmentVariable("ULTRAPUCK_FREEPLAY") == "1");
            else if (System.Environment.GetEnvironmentVariable("ULTRAPUCK_MENUTEST") != null)
            {
                // ULTRAPUCK_MENUTEST=pause or camera: open that screen over free play, for recording it
                _local.Slots.Add(new LocalPlayers.Slot { Device = UnityEngine.InputSystem.Keyboard.current, Team = 0 });
                _local.Slots.Add(new LocalPlayers.Slot { Team = 1, Demo = true });
                StartMatch(true);
                if (System.Environment.GetEnvironmentVariable("ULTRAPUCK_MENUTEST") == "pause") _menus.OpenPause("Free play");
                else { _menus.OpenSettings(false); _menus.Tab = 2; }
            }
            else if (CardScreen.TestMode)
            {
                // ULTRAPUCK_CARDTEST=1: the keyboard player and a bot in free play, straight into a card pick
                _local.Slots.Add(new LocalPlayers.Slot { Device = UnityEngine.InputSystem.Keyboard.current, Team = 0 });
                _local.Slots.Add(new LocalPlayers.Slot { Team = 1, Demo = true });
                StartMatch(true);
                PickACard();
            }
        }

        /// <summary>Every card file in StreamingAssets/Cards, in name order (so every PC numbers the cards the same).</summary>
        private CardLibrary LoadCards()
        {
            var lib = new CardLibrary();
            try
            {
                if (!Directory.Exists(CardsFolder)) return lib;
                var files = Directory.GetFiles(CardsFolder, "*.json");
                System.Array.Sort(files, System.StringComparer.Ordinal);
                foreach (var f in files) lib.AddJson(File.ReadAllText(f), Path.GetFileName(f));
                Debug.Log("Ultra Puck: " + lib.Cards.Count + " cards from " + files.Length + " file(s)");
            }
            catch (System.Exception ex)
            {
                // a broken card file shouldn't stop the game: play without cards and say why
                Debug.LogError("Ultra Puck: couldn't load the cards: " + ex.Message);
                _error = null;
                return null;
            }
            return lib;
        }

        private void SetUpCamera()
        {
            _cam = Camera.main;
            if (_cam == null) _cam = new GameObject("Camera").AddComponent<Camera>();
            _cam.orthographic = true;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = Palette.Background;
            _cam.cullingMask &= ~(1 << CardScreen.Layer);
            _cam.transform.position = new Vector3(0f, 0f, -10f);
            _cam.transform.rotation = Quaternion.identity;
            UpdateCamera(false, true);
        }

        // ------------------------------------------------------------------ camera

        private const float RinkMargin = 1.2f, ScoreBarShare = 0.09f;
        private Vector2 _camPos, _camVel;
        private float _camSize, _camSizeVel;

        /// <summary>
        /// The dynamic camera: frames every player and the puck with room around them, zooming out as they spread
        /// and in as they bunch up, eased so it never jerks; never shows more than the whole rink, and never pans
        /// past its edges. The zoom and smoothing settings set how close it gets and how quickly it follows. With
        /// <paramref name="follow"/> off (lobby, or the "whole rink" setting) it shows the whole rink.
        /// </summary>
        private void UpdateCamera(bool follow, bool snap = false)
        {
            var mapMin = V.Of(_map.BoundsMin) - Vector2.one * RinkMargin;
            var mapMax = V.Of(_map.BoundsMax) + Vector2.one * RinkMargin;
            var aspect = Mathf.Max(0.1f, _cam.aspect);
            var keep = 1f - ScoreBarShare; // the part of the screen below the score bar
            var zoom = Settings.CameraZoom;
            var padding = Mathf.Lerp(3.5f, 9f, zoom);        // room around the action
            var minHalfHeight = Mathf.Lerp(7.5f, 14f, zoom); // the closest it gets

            Vector2 lo = mapMin, hi = mapMax;
            if (follow && _players.Count > 0)
            {
                lo = hi = V.Of3(_puck.Root.transform.position);
                foreach (var p in _players)
                {
                    var at = V.Of3(p.Root.transform.position);
                    lo = Vector2.Min(lo, at); hi = Vector2.Max(hi, at);
                }
                lo -= Vector2.one * padding; hi += Vector2.one * padding;
            }

            // half the view height that fits the box below the score bar, not closer than the minimum, not wider than the rink
            var whole = Mathf.Max((mapMax.y - mapMin.y) / 2f / keep, (mapMax.x - mapMin.x) / 2f / aspect);
            var size = Mathf.Max((hi.y - lo.y) / 2f / keep, (hi.x - lo.x) / 2f / aspect, minHalfHeight);
            size = Mathf.Min(size, whole);

            // centre on the box, then keep the view inside the rink (centred when the view is wider than the rink)
            var halfW = size * aspect;
            var halfH = size * keep;
            var c = (lo + hi) / 2f;
            c.x = halfW * 2f >= mapMax.x - mapMin.x ? (mapMin.x + mapMax.x) / 2f : Mathf.Clamp(c.x, mapMin.x + halfW, mapMax.x - halfW);
            c.y = halfH * 2f >= mapMax.y - mapMin.y ? (mapMin.y + mapMax.y) / 2f : Mathf.Clamp(c.y, mapMin.y + halfH, mapMax.y - halfH);
            var pos = new Vector2(c.x, c.y + size * ScoreBarShare); // shift up so the score bar sits over empty space

            if (snap) { _camPos = pos; _camSize = size; _camVel = Vector2.zero; _camSizeVel = 0f; }
            else
            {
                var smooth = Mathf.Lerp(0.12f, 0.8f, Settings.CameraSmoothing);
                _camPos = Vector2.SmoothDamp(_camPos, pos, ref _camVel, smooth, Mathf.Infinity, Time.unscaledDeltaTime);
                _camSize = Mathf.SmoothDamp(_camSize, size, ref _camSizeVel, smooth * 1.3f, Mathf.Infinity, Time.unscaledDeltaTime);
            }

            // screen shake after hits: a quick random jolt that dies away (scaled with the zoom, so it feels the same)
            _shake = Mathf.MoveTowards(_shake * Mathf.Exp(-Time.unscaledDeltaTime * 9f), 0f, Time.unscaledDeltaTime * 0.05f);
            var amount = Settings.ShakeAmount;
            var jolt = _shake > 0.001f && amount > 0.01f ? Random.insideUnitCircle * _shake * amount * (_camSize / 12f) : Vector2.zero;
            _cam.transform.position = new Vector3(_camPos.x + jolt.x, _camPos.y + jolt.y, -10f);
            _cam.orthographicSize = _camSize;
        }

        private void Update()
        {
            if (_sim == null) return;
            SteamNet.Update();
            if (Updater.Busy) { AttractUpdate(); UpdateCamera(false); return; } // updating: the bots play on behind, nothing else
            var playing = _mode == Mode.Match || (_mode == Mode.Online && OnlinePlaying);
            if (_mode == Mode.Online) OnlineUpdate();
            else if (_menus.IsOpen) _menus.Tick(_local.Menu());
            else if (_mode == Mode.Menu) { _mainMenu.Tick(); AttractUpdate(); }
            else if (_mode == Mode.Lobby) LobbyUpdate();
            else MatchUpdate();
            if (_mode == Mode.Menu && _menus.IsOpen) AttractUpdate();
            if (_menus.IsOpen && _mode == Mode.Match) Show(_state, _state, 0f); // paused: hold the picture still
            UpdateCamera(playing && Settings.FollowCamera);
            _effects.Update();
            _cardScreen.Sim = _sim;
            _cardScreen.Frame(playing ? _state : null);
            _hud.Show(_mode == Mode.Lobby, _free, _local, _state, _sim, _error, _menus.IsOpen || _cardScreen.Showing || !playing && _mode != Mode.Lobby);
            _hud.Banner = _mode == Mode.Online && _session != null && OnlinePlaying ? OnlineBanner() : null;
        }

        private void OnApplicationQuit()
        {
            if (_session != null) _session.Dispose();
            SteamNet.Shutdown();
        }

        // ------------------------------------------------------------------ main menu

        /// <summary>Bots playing a match behind the main menu.</summary>
        private void AttractUpdate()
        {
            var dt = 1f / _attractSim.T.TicksPerSecond;
            _attractAcc += Mathf.Min(Time.unscaledDeltaTime, 0.25f);
            var inputs = new PlayerInput[_attract.Players.Length];
            while (_attractAcc >= dt)
            {
                _attractPrev = _attract.Clone();
                for (var i = 0; i < inputs.Length; i++) inputs[i] = Bot.Think(_attractSim, _attract, i);
                _attractSim.Step(_attract, inputs);
                _attractAcc -= dt;
            }
            _sim = _attractSim;
            _state = _attract;
            Show(_attractPrev, _attract, _attractAcc / dt);
        }

        public void GoMainMenu()
        {
            if (_session != null) LeaveOnline(null);
            _local.Slots.Clear();
            _mode = Mode.Menu;
            _free = false;
            _sim = _matchSim;
            _mainMenu.Show(MainMenu.Page.Main);
        }

        public void GoLocalLobby()
        {
            _mainMenu.Show(MainMenu.Page.None);
            _mainMenu.Message = null;
            _local.Slots.Clear();
            _sim = _matchSim;
            _mode = Mode.Lobby;
        }

        public void StartFreePlayFrom(UnityEngine.InputSystem.InputDevice device)
        {
            _mainMenu.Show(MainMenu.Page.None);
            _mainMenu.Message = null;
            _local.Slots.Clear();
            _local.JoinDevice(device ?? (UnityEngine.InputSystem.InputDevice)UnityEngine.InputSystem.Keyboard.current);
            StartMatch(true);
        }

        public void OpenSettings() { _menus.OpenSettings(true); }

        // ------------------------------------------------------------------ online

        private bool OnlinePlaying { get { return _session != null && _session.Current == NetSession.Stage.Playing && _session.Match != null; } }

        private Simulation MakeOnlineSim() { return new Simulation(_map, null, _cards); }

        /// <summary>A hash of the map and card files: everyone in an online game must have the same ones.</summary>
        private uint Fingerprint()
        {
            var h = 2166136261u;
            var files = new List<string> { Path.Combine(MapsFolder, "test-rink.json") };
            if (Directory.Exists(CardsFolder)) { var c = Directory.GetFiles(CardsFolder, "*.json"); System.Array.Sort(c, System.StringComparer.Ordinal); files.AddRange(c); }
            foreach (var f in files)
                foreach (var b in File.ReadAllBytes(f)) { h ^= b; h *= 16777619u; }
            return h;
        }

        public void HostSteam()
        {
            if (!SteamNet.Ready) return;
            StartOnline(NetSession.Host(SteamTransport.Host(), MakeOnlineSim, _fingerprint));
            SteamNet.Host(NetSession.MaxPlayers);
        }

        public void JoinSteam(Steamworks.CSteamID lobby)
        {
            _mainMenu.Message = null;
            SteamNet.Join(lobby); // OnJoined connects once Steam has us in the lobby
        }

        public void HostIp()
        {
            try { StartOnline(NetSession.Host(UdpTransport.Host(), MakeOnlineSim, _fingerprint)); }
            catch (System.Exception ex) { _mainMenu.Message = "Couldn't host: " + ex.Message + " (is another copy already hosting on this PC?)"; }
        }

        public void JoinIp(string address)
        {
            try { StartOnline(NetSession.Join(UdpTransport.Join(address), MakeOnlineSim, _fingerprint)); }
            catch (System.Exception ex) { _mainMenu.Message = "Couldn't join: " + ex.Message; }
        }

        private void StartOnline(NetSession session)
        {
            if (_session != null) _session.Dispose();
            _session = session;
            _mainMenu.Session = session;
            _mainMenu.Message = null;
            _local.Slots.Clear();
            _mode = Mode.Online;
            _mainMenu.Show(MainMenu.Page.OnlineLobby);
            _onlineMatchId = -1;
            OnlineCardPicks();
        }

        public void StartOnlineMatch()
        {
            if (_session == null || !_session.IsHost) return;
            _session.StartMatch((uint)System.Environment.TickCount * 2654435761u | 1u);
        }

        /// <summary>Leave the online game (with a reason to show, if it ended badly).</summary>
        public void LeaveOnline(string why)
        {
            if (_session != null) { _session.Dispose(); _session = null; }
            SteamNet.Leave();
            _mainMenu.Session = null;
            _local.Slots.Clear();
            LocalCardPicks();
            _menus.Close();
            _mode = Mode.Menu;
            _sim = _matchSim;
            _mainMenu.Show(MainMenu.Page.Online);
            _mainMenu.Message = why;
        }

        private int _onlineMatchId = -1;
        private float _onlineAcc;

        private void OnlineUpdate()
        {
            var s = _session;
            s.Update(Time.realtimeSinceStartupAsDouble);
            if (s.Current == NetSession.Stage.Closed) { LeaveOnline(s.Problem ?? "The game ended."); return; }
            SteamNet.Describe(s.Seats.Count, s.Current == NetSession.Stage.Playing);

            if (!OnlinePlaying)
            {
                // the online lobby: people on this PC join and pick sides here; bots play behind the menu
                if (_menus.IsOpen) _menus.Close();
                _mainMenu.Show(MainMenu.Page.OnlineLobby);
                _mainMenu.Message = s.Problem;
                if (_local.Slots.Count == 0 && _local.BackFromNobody()) { LeaveOnline(null); return; }
                _local.LobbyUpdate();
                var mine = new List<KeyValuePair<string, int>>();
                for (var k = 0; k < _local.Slots.Count; k++)
                    mine.Add(new KeyValuePair<string, int>(SteamNet.Name + (k > 0 ? " (" + (k + 1) + ")" : ""), _local.Slots[k].Team));
                s.SetLocalPlayers(mine);
                if (s.IsHost && _local.Slots.Count > 0 && _local.StartPressed() && s.Seats.Count >= 2) StartOnlineMatch();
                _mainMenu.Tick();
                AttractUpdate();
                return;
            }

            // the match
            if (_mainMenu.Current != MainMenu.Page.None) _mainMenu.Show(MainMenu.Page.None);
            var match = s.Match;
            if (s.MatchId != _onlineMatchId) { _onlineMatchId = s.MatchId; _onlineAcc = 0f; foreach (var sl in _local.Slots) sl.Latched = 0; }
            _sim = match.Sim;
            _state = match.State;

            if (_menus.IsOpen) _menus.Tick(_local.Menu());
            else if (_state.Phase == Phase.GameOver)
            {
                if (s.IsHost && _local.StartPressed()) { StartOnlineMatch(); return; }
                if (_local.BackPressed()) { if (s.IsHost) s.BackToLobby(); else LeaveOnline(null); return; }
            }
            else if (_local.MenuPressed() && !_cardScreen.Showing)
            {
                // online the game doesn't stop for a menu: you just stand still while it's open
                _menus.FreePlay = false;
                _menus.OpenPause("Online  ·  the match keeps going");
            }

            _local.LatchPresses();
            var dt = 1f / _sim.T.TicksPerSecond;
            _onlineAcc += Mathf.Min(Time.unscaledDeltaTime, 0.25f);
            // if we're ahead of the others, slow down a touch so nobody has to rewind further than needed
            var ahead = s.Advantage;
            if (ahead > 1.5f) _onlineAcc -= Time.unscaledDeltaTime * Mathf.Min(0.25f, (ahead - 1.5f) * 0.04f);
            var steps = 0;
            while (_onlineAcc >= dt && steps < 12)
            {
                var inputs = _menus.IsOpen ? new PlayerInput[_local.Slots.Count] : _local.Sample(_sim, _state);
                if (!s.Step(inputs)) { _onlineAcc = Mathf.Min(_onlineAcc, dt); break; } // waiting for the others
                _onlineAcc -= dt;
                steps++;
            }
            _state = match.State;
            Show(match.Previous, match.State, Mathf.Clamp01(_onlineAcc / dt));
        }

        private string OnlineBanner()
        {
            var s = _session;
            var ping = Mathf.RoundToInt(s.Ping * 1000f);
            var text = "Ping " + ping + " ms";
            if (s.DesyncTick >= 0) text = s.Problem;
            else if (!s.Match.CanAdvance) text = "Waiting for the other players...";
            return text;
        }

        /// <summary>Card picks in a local game: player numbers are controller slots.</summary>
        private void LocalCardPicks()
        {
            _cardScreen.SlotOf = p => p;
            _local.PickFor = slot => _cardScreen.PickFor(slot);
        }

        /// <summary>Card picks online: this PC's players are some of the match's players.</summary>
        private void OnlineCardPicks()
        {
            _cardScreen.SlotOf = p =>
            {
                if (_session == null || p < 0 || p >= _session.Seats.Count) return -1;
                var seat = _session.Seats[p];
                return !seat.Bot && seat.Peer == _session.MyPeer ? seat.Local : -1;
            };
            _local.PickFor = slot => _session == null ? (byte)0 : _cardScreen.PickFor(_session.PlayerOf(slot));
        }

        // ------------------------------------------------------------------ lobby

        private void LobbyUpdate()
        {
            if (_local.SettingsPressed()) { _menus.OpenSettings(true); return; }
            if (_local.Slots.Count == 0 && _local.BackFromNobody()) { GoMainMenu(); return; }
            _local.LobbyUpdate();
            // show everyone who has joined standing at their spawn
            _state = _matchSim.NewGame(_local.Teams());
            Show(_state, _state, 0f);
            if (_local.FreePlayPressed()) StartMatch(true);
            else if (_local.Slots.Count > 0 && _local.StartPressed()) StartMatch(false);
        }

        private void StartMatch(bool free)
        {
            _free = free;
            _sim = free ? _freeSim : _matchSim;
            // a fresh seed every match, so games (and the cards offered) don't replay the same way each time
            _state = _sim.NewGame(_local.Teams(), (uint)System.Environment.TickCount * 2654435761u | 1u);
            _prev = _state.Clone();
            _acc = 0f;
            _mode = Mode.Match;
            _menus.FreePlay = free;
            foreach (var s in _local.Slots) s.Latched = 0;
        }

        private void BackToLobby()
        {
            // computer players only exist in free play (or the ULTRAPUCK_DEMO test)
            for (var i = _local.LastBot(); i >= 0; i = _local.LastBot()) _local.Slots.RemoveAt(i);
            _mode = Mode.Lobby;
            _free = false;
            _sim = _matchSim;
        }

        // ------------------------------------------------------------------ free play

        private void FreePlayJoins()
        {
            if (_local.JoinMidGame() == 0) return;
            for (var i = _state.Players.Length; i < _local.Slots.Count; i++) _sim.AddPlayer(_state, _local.Slots[i].Team);
            _prev = _state.Clone(); // nothing to slide between
        }

        /// <summary>Free play: add or remove computer players until there are <paramref name="want"/>.</summary>
        private void SetBots(int want)
        {
            want = Mathf.Clamp(want, 0, LocalPlayers.MaxPlayers - _local.HumanCount);
            while (_local.BotCount < want && _local.AddBot()) _sim.AddPlayer(_state, _local.Slots[_local.Slots.Count - 1].Team);
            while (_local.BotCount > want)
            {
                var i = _local.LastBot();
                _local.Slots.RemoveAt(i);
                _sim.RemovePlayer(_state, i);
            }
            if (_cards != null) _cards.Recompute(_state);
            _prev = _state.Clone();
        }

        /// <summary>Free play: everyone here (not the bots) picks a card, to try the cards out.</summary>
        private void PickACard()
        {
            if (!_free || _state == null || _state.Phase == Phase.Picking) return;
            var people = new List<int>();
            for (var i = 0; i < _local.Slots.Count && i < _state.Players.Length; i++) if (!_local.Slots[i].Demo) people.Add(i);
            _sim.StartPicking(_state, people);
        }

        // ------------------------------------------------------------------ match

        private void MatchUpdate()
        {
            if (!_free && _state.Phase == Phase.GameOver)
            {
                if (_local.StartPressed()) { StartMatch(false); return; }
                if (_local.BackPressed()) { BackToLobby(); return; }
            }
            else if (_local.MenuPressed() && !_cardScreen.Showing)
            {
                _menus.FreePlay = _free;
                _menus.OpenPause(_free ? "Free play" : Palette.TeamName(0) + "  " + _state.Score[0] + " : " + _state.Score[1] + "  " + Palette.TeamName(1));
                foreach (var s in _local.Slots) s.Latched = 0; // presses in the menu don't carry into play
                return;
            }
            if (_free) FreePlayJoins();

            _local.LatchPresses();
            var dt = 1f / _sim.T.TicksPerSecond;
            _acc += Mathf.Min(Time.deltaTime, 0.25f); // after a long hitch, don't try to catch up forever
            while (_acc >= dt)
            {
                _prev = _state.Clone();
                _sim.Step(_state, _local.Sample(_sim, _state));
                _acc -= dt;
            }
            Show(_prev, _state, _acc / dt);

            if (CardScreen.TestMode && _free && _state.Phase == Phase.Play && !_cardScreen.Showing)
            {
                _testPause += Time.deltaTime;
                if (_testPause > 1.5f) { _testPause = 0f; PickACard(); }
            }
        }

        private float _testPause;

        /// <summary>Draw the moment <paramref name="t"/> of the way from <paramref name="a"/> to <paramref name="b"/>.</summary>
        /// <summary>Which teams have a person on this PC (to decide who can see an invisible player).</summary>
        private void LocalTeams(out bool orange, out bool blue)
        {
            orange = blue = false;
            if (_mode == Mode.Online && _session != null)
            {
                foreach (var seat in _session.Seats) if (!seat.Bot && seat.Peer == _session.MyPeer) { if (seat.Team == 0) orange = true; else blue = true; }
                return;
            }
            if (_mode != Mode.Match) return;
            foreach (var sl in _local.Slots) if (!sl.Demo) { if (sl.Team == 0) orange = true; else blue = true; }
        }

        /// <summary>Card effects for one player: invisibility, slowed / boosted, cooldown bars, shockwave and Team Play bursts.</summary>
        private void CardEffects(int i, PlayerState p, Vector2 pos)
        {
            bool orange, blue;
            LocalTeams(out orange, out blue);
            var invisible = p.InvisibleTime > Fix.Zero;
            var teammateHere = p.Team == 0 ? orange : blue;
            var opponentHere = p.Team == 0 ? blue : orange;
            // hidden from a screen with only opponents on it; an outline for a screen with a teammate (or nobody, like the menu)
            var hidden = invisible && opponentHere && !teammateHere;
            var faint = invisible && !hidden;
            _cooldowns[0] = p.Stats.PuckGun > 0 ? 1f - p.GunCooldown.ToFloat() / _sim.T.GunCooldown.ToFloat() : -1f;
            _cooldowns[1] = p.Stats.Invisibility > 0 ? (invisible ? 0f : 1f - p.InvisCooldown.ToFloat() / _sim.T.InvisCooldown.ToFloat()) : -1f;
            _cooldowns[2] = p.Stats.Shockwave > 0 ? 1f - p.ShockCooldown.ToFloat() / _sim.T.ShockCooldown.ToFloat() : -1f;
            _players[i].Status(hidden, faint, p.SlowTime > Fix.Zero, p.BoostTime > Fix.Zero, _cooldowns);

            while (_seenShocks.Count <= i) { _seenShocks.Add(-1); _seenRefreshes.Add(-1); }
            if (_seenShocks[i] >= 0 && p.Shocks > _seenShocks[i] && Settings.HitEffects)
            {
                _effects.Ring(V.Of(p.ShockAt), 1f, new Color(0.45f, 0.8f, 1f), p.ShockRadius.ToFloat() * 1.15f, 0.45f);
                _shake = Mathf.Max(_shake, 0.25f);
            }
            if (_seenRefreshes[i] >= 0 && p.Refreshes > _seenRefreshes[i])
                _effects.Ring(pos, 0.6f, new Color(0.35f, 0.95f, 0.55f), 1.3f, 0.5f);
            _seenShocks[i] = p.Shocks;
            _seenRefreshes[i] = p.Refreshes;
        }

        private void Show(GameState a, GameState b, float t)
        {
            while (_players.Count > b.Players.Length) { Destroy(_players[_players.Count - 1].Root); _players.RemoveAt(_players.Count - 1); }
            for (var i = 0; i < b.Players.Length; i++)
            {
                var pb = b.Players[i];
                var pa = i < a.Players.Length ? a.Players[i] : pb;
                var radius = _sim.RadiusOf(pb).ToFloat();
                var key = "Player" + pb.Team + "_" + Mathf.RoundToInt(radius * 1000f);
                if (i >= _players.Count || _players[i].Root.name != key)
                {
                    // new player, or their size changed (cards): build them again
                    var view = new PlayerView(pb.Team, radius, _entities);
                    view.Root.name = key;
                    if (i < _players.Count) { Destroy(_players[i].Root); _players[i] = view; }
                    else _players.Add(view);
                }
                var pos = (V.Of(pb.Pos) - V.Of(pa.Pos)).sqrMagnitude > 4f ? V.Of(pb.Pos) : Vector2.Lerp(V.Of(pa.Pos), V.Of(pb.Pos), t);
                var facing = Vector2.Lerp(V.Of(pa.Facing), V.Of(pb.Facing), t);
                var full = Simulation.MaxDashes(pb).ToFloat();
                Vector2 hitAt;
                var hit = _players[i].Show(pos, facing, pb.DashCharge.ToFloat(), Mathf.RoundToInt(full), pb.Charge.ToFloat(), pb.Hits, pb.HitPower.ToFloat(), V.Of(pb.HitDir), out hitAt);
                CardEffects(i, pb, pos);
                if (hit > 0f)
                {
                    // both players in a hit report it; one ring per contact is enough
                    var near = false;
                    foreach (var h in _impactsThisFrame) if ((h - hitAt).sqrMagnitude < 0.6f) near = true;
                    // small bumps only squash; a ring is for the real hits
                    if (!near && hit >= 0.35f && Settings.HitEffects) { _effects.Impact(hitAt, hit); _impactsThisFrame.Add(hitAt); }
                    _shake = Mathf.Max(_shake, 0.06f + 0.22f * hit * hit);
                }
            }
            _impactsThisFrame.Clear();
            if (_seenShocks.Count > b.Players.Length) { _seenShocks.RemoveRange(b.Players.Length, _seenShocks.Count - b.Players.Length); _seenRefreshes.RemoveRange(b.Players.Length, _seenRefreshes.Count - b.Players.Length); }
            _shots.Show(b, _sim.T.ShotRadius.ToFloat());
            _pylons.Show(b, _sim.T.PylonRadius.ToFloat());
            // a puck that just jumped (face-off) shouldn't slide across the rink
            var pa2 = V.Of(a.PuckPos); var pb2 = V.Of(b.PuckPos);
            _puck.Show((pb2 - pa2).sqrMagnitude > 4f ? pb2 : Vector2.Lerp(pa2, pb2, t), b.PuckTeam);
        }
    }

    internal static class Boot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Start()
        {
            if (Object.FindAnyObjectByType<GameRunner>() != null) return;
            new GameObject("UltraPuck").AddComponent<GameRunner>();
        }
    }
}
