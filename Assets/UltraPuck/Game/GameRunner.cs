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
        private PlayScreens _play;
        private Menus _menus;
        private CardScreen _cardScreen;
        private Effects _effects;
        private ShotViews _shots;
        private PylonViews _pylons;
        private IceViews _ice;
        // what each player's effect counters were last frame, to spot new shockwaves and Team Play refreshes
        private readonly List<int> _seenShocks = new List<int>(), _seenRefreshes = new List<int>();
        private readonly float[] _cooldowns = new float[3];
        private float _shake;
        private readonly List<Vector2> _impactsThisFrame = new List<Vector2>();
        /// <summary>This frame's hits: where (x, y) and how hard (z).</summary>
        private readonly List<Vector3> _hitsThisFrame = new List<Vector3>();

        /// <summary>A taken card's ripple: rings that start one after another where the player is.</summary>
        private struct Ripple { public Vector2 At; public Color Colour; public float When, Size, Life; }
        private readonly List<Ripple> _ripples = new List<Ripple>();

        public static string MapsFolder { get { return Path.Combine(Application.streamingAssetsPath, "Maps"); } }
        public static string CardsFolder { get { return Path.Combine(Application.streamingAssetsPath, "Cards"); } }

        private void Awake()
        {
            Settings.ApplyGraphics();
            _hud = gameObject.AddComponent<Hud>();
            _menus = gameObject.AddComponent<Menus>();
            _cardScreen = gameObject.AddComponent<CardScreen>();
            _mainMenu = gameObject.AddComponent<MainMenu>();
            _play = gameObject.AddComponent<PlayScreens>();
            _play.Runner = this;
            _play.Local = _local;
            gameObject.AddComponent<Updater>();
            _mainMenu.Runner = this;
            _mainMenu.Local = _local;
            LoadMaps();
            if (_maps.Count == 0) { _error = "Couldn't load any map: " + _mapProblem; Debug.LogError(_error); return; }
            _cards = LoadCards();
            _fingerprint = Fingerprint();
            var saved = PlayerPrefs.GetString("map_file", "test-rink.json");
            BuildSims(Mathf.Max(0, _mapFiles.IndexOf(saved)));

            SteamNet.Init();
            LogDevices("at start");
            UnityEngine.InputSystem.InputSystem.onDeviceChange += (d, change) =>
            {
                Debug.Log("Ultra Puck: input device " + change + ": " + d.displayName + " (" + d.layout + ")");
                // Steam Input grabs controllers a moment after Steam starts (while we run as Steam's test app): say what to do
                if (d is UnityEngine.InputSystem.Gamepad && change == UnityEngine.InputSystem.InputDeviceChange.Removed && SteamNet.Ready && Time.realtimeSinceStartup - SteamNet.StartedAt < 10f)
                    _mainMenu.Message = "Steam took over your " + d.displayName + ". In Steam: Settings > Controller, turn off Steam Input for this kind of controller, then restart Ultra Puck. (Needed until the game has its own Steam page.)";
            };
            Invoke("LogDevicesLater", 3f);
            SteamNet.OnJoined = host => StartOnline(NetSession.Join(SteamTransport.Join(host), MakeOnlineSim, _fingerprint));
            SteamNet.OnFailed = why => _mainMenu.Message = why;

            _cardScreen.Local = _local;
            StartCoroutine(_cardScreen.PreloadArt(_cards)); // every card picture loaded before the first pick
            _cardScreen.PlayerOnScreen = i => i >= 0 && i < _players.Count && _players[i].Root != null ? _cam.WorldToScreenPoint(_players[i].Root.transform.position) : (Vector3?)null;
            _cardScreen.OnAbsorbed = (i, colour) =>
            {
                if (i < 0 || i >= _players.Count || _players[i].Root == null) return;
                Vector2 at = _players[i].Root.transform.position;
                // like a drop landing in water: a big ripple that bends the rink as it spreads, and a smaller echo
                _ripples.Add(new Ripple { At = at, Colour = colour, When = Time.unscaledTime, Size = 5.5f, Life = 1.5f });
                _ripples.Add(new Ripple { At = at, Colour = colour, When = Time.unscaledTime + 0.22f, Size = 3.2f, Life = 1.1f });
            };
            LocalCardPicks();
            _menus.OnMainMenu = GoMainMenu;
            _menus.BotCount = () => _local.BotCount;
            _menus.MaxBots = () => LocalPlayers.MaxPlayers - _local.HumanCount;
            _menus.SetBots = SetBots;
            _menus.OnGetCard = EditCards;
            _menus.Cards = _cards;
            _hud.PlayerName = NameOf;

            _rink = new RinkView(_map, _matchSim.T.WallThickness.ToFloat());
            _entities = new GameObject("Entities").transform;
            _puck = new PuckView(_sim.T.PuckRadius.ToFloat(), _entities);
            _effects = new Effects(_entities);
            _shots = new ShotViews(_entities);
            _pylons = new PylonViews(_entities);
            _ice = new IceViews(_entities);
            SetUpCamera();
            _mainMenu.Show(MainMenu.Page.Main);
            if (ShotDir != null) StartCoroutine(Shots());
            else if (_local.AddDemoPlayers() > 0) StartMatch(System.Environment.GetEnvironmentVariable("ULTRAPUCK_FREEPLAY") == "1");
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

        private void LogDevicesLater() { LogDevices("after 3 s"); }

        // ------------------------------------------------------------------ screenshots (for checking screens without playing)

        /// <summary>ULTRAPUCK_SHOTS=folder: set up each screen worth checking, save a picture of it there, then quit.</summary>
        private static readonly string ShotDir = System.Environment.GetEnvironmentVariable("ULTRAPUCK_SHOTS");
        /// <summary>Screenshots: show the whole rink, not the players' part of it.</summary>
        private bool _shotWholeRink;
        /// <summary>Screenshots: move the camera by this much (to see what moving the view does).</summary>
        private Vector2 _shotNudge;

        private System.Collections.IEnumerator Shots()
        {
            Directory.CreateDirectory(ShotDir);
            var only = System.Environment.GetEnvironmentVariable("ULTRAPUCK_SHOTS_ONLY");
            System.Func<string, bool> want = n => string.IsNullOrEmpty(only) || only.Contains(n);
            yield return new WaitForSecondsRealtime(1f);
            var kb = UnityEngine.InputSystem.Keyboard.current;

            if (want("players"))
            {
                // this PC's players screen: you (ready, working on a bot), a second person not ready yet, two bots
                GoLocalLobby();
                _local.Slots.Add(new LocalPlayers.Slot { Device = kb, Colour = 0, Symbol = 0, Name = "FancyDuck" });
                _local.Slots.Add(new LocalPlayers.Slot { Colour = 1, Symbol = 1, Name = "P2" });
                _local.AddBot(); _local.AddBot();
                _local.Slots[0].Ready = true; _local.Slots[0].Cursor = 1;
                yield return new WaitForSecondsRealtime(0.8f);
                yield return Shot("players");
                _local.Slots[1].Ready = true;
                yield return new WaitForSecondsRealtime(0.2f);
                yield return Shot("players-all-ready");
                _local.UnreadyAll();
                yield return new WaitForSecondsRealtime(0.2f);
                yield return Shot("players-not-ready");
                GoMainMenu();
            }

            if (want("online"))
            {
                // hosting (on this PC's network, no Steam): you ready, two bots; then the options panel
                HostIp();
                _local.Slots.Add(new LocalPlayers.Slot { Device = kb, Colour = 0, Symbol = 2, Name = "FancyDuck", Ready = true, Cursor = 0 });
                yield return new WaitForSecondsRealtime(0.3f);
                if (_session != null) _session.Bots = 2;
                yield return new WaitForSecondsRealtime(0.8f);
                yield return Shot("online");
                _mainMenu.ShowOptions(true);
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Shot("online-options");
                _mainMenu.ShowOptions(false);
                LeaveOnline(null);
                GoMainMenu();
            }

            if (want("ripple"))
            {
                // the card soaking into a player: the ripple a few moments in
                _local.Slots.Add(new LocalPlayers.Slot { Device = kb, Team = 0 });
                _local.Slots.Add(new LocalPlayers.Slot { Team = 1, Demo = true });
                _mainMenu.Show(MainMenu.Page.None);
                StartMatch(true);
                yield return new WaitForSecondsRealtime(1.5f);
                _cardScreen.OnAbsorbed(0, new Color(0.56f, 0.83f, 0.96f));
                yield return new WaitForSecondsRealtime(0.15f);
                yield return Shot("ripple-1");
                yield return new WaitForSecondsRealtime(0.2f);
                yield return Shot("ripple-2");
                yield return new WaitForSecondsRealtime(0.3f);
                yield return Shot("ripple-3");
                GoMainMenu();
            }

            if (want("replay"))
            {
                // a goal in a match with replays on, during the replay (the skip votes in the corner)
                _local.Slots.Add(new LocalPlayers.Slot { Device = kb, Team = 0, Colour = 0 });
                _local.Slots.Add(new LocalPlayers.Slot { Team = 1, Colour = 1, Demo = true });
                _local.AssignTeams();
                _mainMenu.Show(MainMenu.Page.None);
                StartMatch(false);
                _sim.T.Replays = true;
                _sim.T.CelebrateTime = Fix.FromInt(11);
                yield return new WaitForSecondsRealtime(4f);
                var g = _map.Goals.Find(x => x.Team == 1);
                _state.PuckPos = new FixVec2((g.Min.X + g.Max.X) * Fix.Half, (g.Min.Y + g.Max.Y) * Fix.Half);
                _state.PuckVel = FixVec2.Zero;
                _state.Carrier = -1;
                yield return new WaitForSecondsRealtime(3f);
                yield return Shot("replay");
                GoMainMenu();
            }

            if (want("pairing"))
            {
                // a virtual controller next to the keyboard: with shared controls they're both player 1; a second
                // controller is player 2. Logged and pictured on the players screen.
                var pad1 = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>("Test Controller 1");
                var pad2 = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>("Test Controller 2");
                GoLocalLobby();
                _local.JoinDevice(kb);
                _local.PairDevices();
                _local.JoinDevice(pad2); // pad1 went to player 1, so this one is a new player
                _local.PairDevices();
                for (var k = 0; k < _local.Slots.Count; k++) Debug.Log("Ultra Puck pairing: P" + (k + 1) + " = " + _local.Slots[k].Label);
                yield return new WaitForSecondsRealtime(0.8f);
                yield return Shot("pairing");
                GoMainMenu();
                UnityEngine.InputSystem.InputSystem.RemoveDevice(pad1);
                UnityEngine.InputSystem.InputSystem.RemoveDevice(pad2);
            }

            if (want("countdown"))
            {
                // a local match starting: 3, 2, 1
                _local.Slots.Add(new LocalPlayers.Slot { Device = kb, Team = 0, Colour = 0 });
                _local.Slots.Add(new LocalPlayers.Slot { Team = 1, Colour = 1, Demo = true });
                _local.AssignTeams();
                _mainMenu.Show(MainMenu.Page.None);
                StartMatch(false);
                yield return new WaitForSecondsRealtime(0.25f);
                yield return Shot("countdown-3");
                yield return new WaitForSecondsRealtime(1.7f);
                yield return Shot("countdown-1");
                GoMainMenu();
            }

            if (want("settings"))
            {
                // the Gameplay settings (the keyboard and controller options are at the top)
                _menus.OpenSettings(false);
                _menus.Tab = 1;
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Shot("settings-gameplay");
                _menus.Close();
            }

            if (want("maps"))
            {
                // every rink, whole, with bots playing on it for a few seconds
                _shotWholeRink = true;
                var startMap = _mapIndex; // choosing a rink saves it: put the player's own back afterwards
                for (var m = 0; m < _maps.Count; m++)
                {
                    UseMap(m);
                    _local.Slots.Clear();
                    for (var b = 0; b < 4; b++) _local.Slots.Add(new LocalPlayers.Slot { Team = b & 1, Colour = b & 1, Demo = true, Name = "Bot" });
                    _local.AssignTeams();
                    _mainMenu.Show(MainMenu.Page.None);
                    StartMatch(true);
                    yield return new WaitForSecondsRealtime(4f);
                    yield return Shot("map-" + Path.GetFileNameWithoutExtension(_mapFiles[m]));
                    GoMainMenu();
                }
                _shotWholeRink = false;
                UseMap(startMap);
            }

            if (want("ice"))
            {
                // a few ice trails, then the camera moved: the sheen should have slid along them
                _local.Slots.Add(new LocalPlayers.Slot { Device = kb, Team = 0 });
                _mainMenu.Show(MainMenu.Page.None);
                StartMatch(true);
                yield return new WaitForSecondsRealtime(1f);
                var ice = new List<IcePoint>();
                for (var t = 0; t < 3; t++)
                    for (var k = 0; k < 30; k++)
                    {
                        var a = new Vector2(-8f + t * 7f, -6f + t * 2f) + new Vector2(0.55f, 0.8f - t * 0.6f).normalized * (k * 0.4f);
                        ice.Add(new IcePoint { Pos = new FixVec2(Fix.FromDecimal((decimal)a.x), Fix.FromDecimal((decimal)a.y)), Trail = 900 + t, Team = 1, Slow = Fix.Ratio(1, 10), Melt = Fix.FromInt(60) });
                    }
                _state.Ice = ice.ToArray();
                _state.IceTrails = 903;
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Shot("ice-1");
                _shotNudge = new Vector2(3f, 1.5f);
                yield return new WaitForSecondsRealtime(1.5f);
                yield return Shot("ice-2");
                _shotNudge = Vector2.zero;
                GoMainMenu();
            }

            Debug.Log("Ultra Puck: screenshots done in " + ShotDir);
            Application.Quit();
        }

        private System.Collections.IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(ShotDir, name + ".png"));
            yield return null;
            yield return null;
        }

        /// <summary>The controllers the game can see, in the log (for working out controller problems).</summary>
        private static void LogDevices(string when)
        {
            var names = new List<string>();
            foreach (var g in UnityEngine.InputSystem.Gamepad.all) names.Add(g.displayName + " (" + g.layout + ")");
            Debug.Log("Ultra Puck: controllers " + when + ": " + (names.Count == 0 ? "none" : string.Join(", ", names.ToArray())));
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
            var normalSize = Mathf.Max((hi.y - lo.y) / 2f / keep, (hi.x - lo.x) / 2f / aspect, minHalfHeight);

            // Charge Zoom: while someone on this PC charges a shot, pull back until the goal they're attacking is in view
            var goal = Vector2.zero; var charge = 0f;
            var zooming = follow && Settings.ChargeZoom && LocalCharge(out goal, out charge);
            if (zooming)
            {
                var gLo = Vector2.Min(lo, goal - Vector2.one * 2.5f);
                var gHi = Vector2.Max(hi, goal + Vector2.one * 2.5f);
                // most of the way out early in the charge, so the net is in view while you line the shot up
                var k = Mathf.Clamp01(charge / 0.4f);
                k = 1f - (1f - k) * (1f - k);
                lo = Vector2.Lerp(lo, gLo, k);
                hi = Vector2.Lerp(hi, gHi, k);
            }

            // half the view height that fits the box below the score bar, not closer than the minimum, not wider than the rink
            var whole = Mathf.Max((mapMax.y - mapMin.y) / 2f / keep, (mapMax.x - mapMin.x) / 2f / aspect);
            var size = Mathf.Max((hi.y - lo.y) / 2f / keep, (hi.x - lo.x) / 2f / aspect, minHalfHeight);
            if (zooming && Settings.ChargeZoomMax > 0) size = Mathf.Min(size, normalSize * Settings.ChargeZoomMax / 100f);
            size = Mathf.Min(size, whole);

            // centre on the box, then keep the view inside the rink (centred when the view is wider than the rink)
            var halfW = size * aspect;
            var halfH = size * keep;
            var c = (lo + hi) / 2f;
            c.x = halfW * 2f >= mapMax.x - mapMin.x ? (mapMin.x + mapMax.x) / 2f : Mathf.Clamp(c.x, mapMin.x + halfW, mapMax.x - halfW);
            c.y = halfH * 2f >= mapMax.y - mapMin.y ? (mapMin.y + mapMax.y) / 2f : Mathf.Clamp(c.y, mapMin.y + halfH, mapMax.y - halfH);
            var pos = new Vector2(c.x, c.y + size * ScoreBarShare); // shift up so the score bar sits over empty space
            pos += _shotNudge;

            if (snap) { _camPos = pos; _camSize = size; _camVel = Vector2.zero; _camSizeVel = 0f; }
            else
            {
                var smooth = Mathf.Lerp(0.12f, 0.8f, Settings.CameraSmoothing);
                if (zooming) smooth *= 0.5f; // pulling back for a shot shouldn't lag behind it
                _camPos = Vector2.SmoothDamp(_camPos, pos, ref _camVel, smooth, Mathf.Infinity, Time.unscaledDeltaTime);
                _camSize = Mathf.SmoothDamp(_camSize, size, ref _camSizeVel, smooth * 1.3f, Mathf.Infinity, Time.unscaledDeltaTime);
            }

            // screen shake after hits: a quick random jolt that dies away (scaled with the zoom, so it feels the same)
            _shake = Mathf.MoveTowards(_shake * Mathf.Exp(-Time.unscaledDeltaTime * 9f), 0f, Time.unscaledDeltaTime * 0.05f);
            var amount = Settings.ShakeAmount;
            var jolt = _shake > 0.001f && amount > 0.01f ? Random.insideUnitCircle * _shake * amount * (_camSize / 12f) : Vector2.zero;
            _cam.transform.position = new Vector3(_camPos.x + jolt.x, _camPos.y + jolt.y, -10f);
            _cam.orthographicSize = _camSize;
            Shader.SetGlobalVector("_UltraPuckCam", new Vector4(_camPos.x, _camPos.y, 0f, 0f)); // the ice's reflection moves with the view
        }

        private void Update()
        {
            if (_sim == null) return;
            SteamNet.Update();
            if (_mode == Mode.Online) OnlineColours();
            RinkColours();
            LocalPlayers.Blocked = SteamNet.OverlayOpen;
            InputMode.Update();
            if (Updater.Busy) { AttractUpdate(); UpdateCamera(false); return; } // updating: the bots play on behind, nothing else
            var playing = _mode == Mode.Match || (_mode == Mode.Online && OnlinePlaying);
            if (_mode == Mode.Online) OnlineUpdate();
            else if (_menus.IsOpen) _menus.Tick(_local.Menu());
            else if (_mode == Mode.Menu)
            {
                _mainMenu.Tick();
                // only if the menu is still up: a button may just have started free play, and running the menu's
                // match now would put its bots (and its card-less rules) back in place of the new game
                if (_mode == Mode.Menu) AttractUpdate();
            }
            else if (_mode == Mode.Lobby) LobbyUpdate();
            else MatchUpdate();
            if (_mode == Mode.Menu && _menus.IsOpen) AttractUpdate();
            if (_menus.IsOpen && _mode == Mode.Match) Show(_state, _state, 0f); // paused: hold the picture still
            UpdateCamera(playing && Settings.FollowCamera && !_shotWholeRink);
            for (var k = _ripples.Count - 1; k >= 0; k--)
            {
                if (Time.unscaledTime < _ripples[k].When) continue;
                var r = _ripples[k];
                _effects.Ripple(r.At, r.Colour, r.Size, r.Life);
                _ripples.RemoveAt(k);
            }
            _effects.Update();
            _cardScreen.Sim = _sim;
            _cardScreen.Frame(playing ? _state : null);
            _hud.Show(false, _free, _local, _state, _sim, _error, _menus.IsOpen || _cardScreen.Showing || !playing);
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
            _play.Open(PlayScreens.Page.None);
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
            _play.Open(PlayScreens.Page.Join);
        }

        public void StartFreePlayFrom(UnityEngine.InputSystem.InputDevice device)
        {
            _mainMenu.Show(MainMenu.Page.None);
            _mainMenu.Message = null;
            _local.Slots.Clear(); // no bots to start with: add them from the pause menu when you want them
            _local.JoinDevice(device ?? (UnityEngine.InputSystem.InputDevice)UnityEngine.InputSystem.Keyboard.current);
            StartMatch(true);
        }

        public void OpenSettings() { _menus.OpenSettings(true); }

        // ------------------------------------------------------------------ online

        private bool OnlinePlaying { get { return _session != null && _session.Current == NetSession.Stage.Playing && _session.Match != null; } }

        private Simulation MakeOnlineSim()
        {
            // the host's choice of rink (everyone has the same list: the fingerprint check makes sure)
            if (_session != null && _session.Map != _mapIndex && _session.Map < _maps.Count) UseMap(_session.Map);
            return new Simulation(_map, RulesTuning(_session != null ? _session.Rules : null, false), _cards);
        }

        // ------------------------------------------------------------------ rinks

        private readonly List<MapDef> _maps = new List<MapDef>();
        private readonly List<string> _mapFiles = new List<string>();
        private string _mapProblem;
        private int _mapIndex;
        private RinkView _rink;

        public int MapIndex { get { return _mapIndex; } }
        public int MapCount { get { return _maps.Count; } }
        public string MapName(int i) { return i >= 0 && i < _maps.Count ? (string.IsNullOrEmpty(_maps[i].Name) ? _mapFiles[i] : _maps[i].Name) : "?"; }

        /// <summary>Every map in the Maps folder (the Test Rink first, then the rest by file name).</summary>
        private void LoadMaps()
        {
            var files = Directory.Exists(MapsFolder) ? Directory.GetFiles(MapsFolder, "*.json") : new string[0];
            System.Array.Sort(files, System.StringComparer.Ordinal);
            var list = new List<string>(files);
            var test = list.FindIndex(f => Path.GetFileName(f) == "test-rink.json");
            if (test > 0) { var t = list[test]; list.RemoveAt(test); list.Insert(0, t); }
            foreach (var f in list)
            {
                try { _maps.Add(MapDef.FromJson(File.ReadAllText(f))); _mapFiles.Add(Path.GetFileName(f)); }
                catch (System.Exception ex) { _mapProblem = Path.GetFileName(f) + ": " + ex.Message; Debug.LogWarning("Ultra Puck: couldn't load map " + _mapProblem); }
            }
        }

        private void BuildSims(int index)
        {
            _mapIndex = Mathf.Clamp(index, 0, _maps.Count - 1);
            _map = _maps[_mapIndex];
            _matchSim = new Simulation(_map, MatchTuning(false), _cards);
            _freeSim = new Simulation(_map, MatchTuning(true), _cards);
            _sim = _matchSim;
            _attractSim = new Simulation(_map, Tuning.ForFreePlay());
            _attract = _attractSim.NewGame(new[] { 0, 1, 0, 1 }, (uint)System.Environment.TickCount | 1u);
            _attractPrev = _attract.Clone();
        }

        // ------------------------------------------------------------------ match rules

        /// <summary>The rules from the Match settings (rounds, picking, cards offered, pick time, cards switched off).</summary>
        private Tuning MatchTuning(bool free) { return RulesTuning(PackRules(), free); }

        /// <summary>The match rules as a few bytes, to send to everyone online (the host's rules apply).</summary>
        private byte[] PackRules()
        {
            var b = new List<byte>();
            b.Add((byte)((Settings.Rounds ? 1 : 0) | (Settings.TeamVote ? 2 : 0) | (Settings.Replays ? 4 : 0)));
            b.Add((byte)Settings.CardsOffered);
            b.Add((byte)Mathf.Clamp(Settings.PickTime, 0, 255));
            b.Add((byte)Settings.MatchMode);
            b.Add((byte)Settings.GoalsToWin);
            b.Add((byte)Settings.MatchMinutes);
            var off = new List<byte>();
            if (_cards != null) foreach (var c in _cards.Cards) if (!Settings.CardOn(c.Id) && c.Index < 256) off.Add((byte)c.Index);
            b.Add((byte)off.Count);
            b.AddRange(off);
            return b.ToArray();
        }

        private static Tuning RulesTuning(byte[] rules, bool free)
        {
            var t = free ? Tuning.ForFreePlay() : new Tuning();
            if (rules == null || rules.Length < 4) return t;
            if (!free) t.CardsAfterGoals = (rules[0] & 1) != 0;
            if (!free && (rules[0] & 4) != 0) { t.Replays = true; t.CelebrateTime = Fix.FromInt(11); } // the explosion, then the replay
            t.TeamVote = (rules[0] & 2) != 0;
            t.CardsOffered = Mathf.Clamp(rules[1], 1, 5);
            t.PickTime = Fix.FromInt(rules[2]);
            if (rules.Length < 7) return t;
            if (!free)
            {
                // how it's won: first to N goals, or most goals when the clock runs out (level: next goal wins)
                if (rules[3] == 1) { t.TimeLimit = Fix.FromInt(Mathf.Clamp(rules[5], 1, 15) * 60); t.GoalsToWin = 0; }
                else t.GoalsToWin = Mathf.Clamp(rules[4], 1, 20);
            }
            var n = Mathf.Min(rules[6], rules.Length - 7);
            t.CardsOff = new int[n];
            for (var k = 0; k < n; k++) t.CardsOff[k] = rules[7 + k];
            return t;
        }

        /// <summary>Switch rinks (between matches): new rules objects, the new rink drawn, the camera reset.</summary>
        public void UseMap(int index)
        {
            if (_maps.Count == 0) return;
            index = ((index % _maps.Count) + _maps.Count) % _maps.Count;
            if (index == _mapIndex && _rink != null) return;
            BuildSims(index);
            PlayerPrefs.SetString("map_file", _mapFiles[_mapIndex]);
            if (_rink != null) Destroy(_rink.Root);
            _rink = new RinkView(_map, _matchSim.T.WallThickness.ToFloat());
            if (_session != null && _session.IsHost) _session.Map = _mapIndex;
            if (_cam != null) UpdateCamera(false, true);
        }

        /// <summary>Next / previous rink.</summary>
        public void StepMap(int d) { UseMap(_mapIndex + d); }

        /// <summary>A hash of the map and card files: everyone in an online game must have the same ones.</summary>
        private uint Fingerprint()
        {
            var h = 2166136261u;
            var files = new List<string>();
            foreach (var m in _mapFiles) files.Add(Path.Combine(MapsFolder, m));
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
            if (session.IsHost) session.Map = _mapIndex;
            _mainMenu.Session = session;
            _mainMenu.Message = null;
            _local.Slots.Clear();
            _mode = Mode.Online;
            // the host's people work on the host's bots; on someone else's game there are none to change
            _local.Bots = session.IsHost ? new SessionBots(session) : null;
            _mainMenu.Show(MainMenu.Page.OnlineLobby);
            _onlineMatchId = -1;
            OnlineCardPicks();
        }

        public void StartOnlineMatch()
        {
            if (_session == null || !_session.IsHost) return;
            _session.Rules = PackRules();
            _session.StartMatch((uint)System.Environment.TickCount * 2654435761u | 1u);
        }

        /// <summary>Leave the online game (with a reason to show, if it ended badly).</summary>
        public void LeaveOnline(string why)
        {
            if (_session != null) { _session.Dispose(); _session = null; }
            SteamNet.Leave();
            _mainMenu.Session = null;
            _local.Slots.Clear();
            _local.Bots = new LocalPlayers.SlotBots(_local);
            LocalCardPicks();
            _menus.Close();
            _mode = Mode.Menu;
            _sim = _matchSim;
            _mainMenu.Show(MainMenu.Page.Online);
            _mainMenu.Message = why;
        }

        /// <summary>The online host's bots, for the players screen.</summary>
        private sealed class SessionBots : LocalPlayers.IBotEditor
        {
            private readonly NetSession _s;
            public SessionBots(NetSession s) { _s = s; }
            public int Count { get { return _s.Bots; } }
            public void SwitchTeam(int i) { var l = _s.BotLook(i); _s.SetBot(i, (l & 7) == 1 ? 0 : 1, (l >> 3) & 7); }
            public void NextSymbol(int i) { var l = _s.BotLook(i); _s.SetBot(i, l & 7, (((l >> 3) & 7) + 1) % Symbols.Count); }
            public bool Add() { if (_s.Seats.Count >= NetSession.MaxPlayers) return false; _s.Bots = _s.Bots + 1; return true; }
            public void Remove(int i) { _s.RemoveBot(i); }
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
                if (_mainMenu.Current != MainMenu.Page.Friends) _mainMenu.Show(MainMenu.Page.OnlineLobby);
                _mainMenu.Message = s.Problem;
                if (_play.Typing) { _play.Tick(); AttractUpdate(); return; } // typing a name: the keyboard has the controls
                _mainMenu.Tick(); // before joining: pressing A to join mustn't also press the highlighted button
                if (_mode != Mode.Online || _session == null) return;
                var busy = _mainMenu.LobbyBusy; // the options panel or the invite page is on top
                if (!busy)
                {
                    _local.LobbyUpdate();
                    if (_local.NameRequest != null) { _play.OpenKeyboard(_local.NameRequest); _local.NameRequest = null; }
                    if (_local.LeaveLobbyHeld()) { LeaveOnline(null); return; }
                }
                var mine = new List<KeyValuePair<string, int>>();
                for (var k = 0; k < _local.Slots.Count; k++)
                {
                    var sl = _local.Slots[k];
                    var nm = !string.IsNullOrEmpty(sl.Name) ? sl.Name : Settings.PlayerName + (k > 0 ? " (" + (k + 1) + ")" : "");
                    mine.Add(new KeyValuePair<string, int>(nm, NetSession.Look(sl.Colour, sl.Symbol, sl.Ready)));
                }
                s.SetLocalPlayers(mine);
                if (!busy && s.IsHost && _local.Slots.Count > 0 && _local.StartPressed() && s.Seats.Count >= 2 && !s.TooManyTeams && s.AllReady) StartOnlineMatch();
                AttractUpdate();
                return;
            }

            // the match (everyone readies up again in the lobby afterwards)
            if (_mainMenu.Current != MainMenu.Page.None) _mainMenu.Show(MainMenu.Page.None);
            _local.UnreadyAll();
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
            var simClock = System.Diagnostics.Stopwatch.StartNew();
            while (_onlineAcc >= dt && steps < 12)
            {
                var inputs = _menus.IsOpen ? new PlayerInput[_local.Slots.Count] : _local.Sample(_sim, _state);
                if (!s.Step(inputs))
                {
                    // waiting for the others: keep the time owed (up to a quarter of a second) so we catch up when their
                    // controls arrive, instead of staying behind for good (which slows everyone else down to match)
                    _onlineAcc = Mathf.Min(_onlineAcc, 0.25f);
                    _netStalls++;
                    break;
                }
                _onlineAcc -= dt;
                steps++;
            }
            NetStats((float)simClock.Elapsed.TotalMilliseconds);
            _state = match.State;
            Recorder.Record(_state);
            if (ReplayFrame(false)) return;
            Show(match.Previous, match.State, Mathf.Clamp01(_onlineAcc / dt));
        }

        // network stats, written to the log every 10 s during an online match (for working out lag)
        private int _netStalls, _netFrames, _netRewinds;
        private float _netStatsAt, _netSimMs, _netWorstFrame;

        private void NetStats(float simMs)
        {
            _netFrames++;
            _netSimMs += simMs;
            _netWorstFrame = Mathf.Max(_netWorstFrame, Time.unscaledDeltaTime);
            if (Time.unscaledTime - _netStatsAt < 10f) return;
            var s = _session;
            var m = s != null ? s.Match : null;
            if (m != null && _netStatsAt > 0f)
            {
                var secs = Time.unscaledTime - _netStatsAt;
                Debug.Log("Ultra Puck net: " + s.Seats.Count + " players, ping " + Mathf.RoundToInt(s.Ping * 1000f) + " ms, ahead " + s.Advantage.ToString("0.0") +
                    " ticks, waited " + _netStalls + " times, rewinds " + (m.Rewinds - _netRewinds) + " (longest " + m.LongestRewind + " ticks), " +
                    Mathf.RoundToInt(_netFrames / secs) + " fps (worst frame " + Mathf.RoundToInt(_netWorstFrame * 1000f) + " ms), game " +
                    (_netSimMs / Mathf.Max(1, _netFrames)).ToString("0.00") + " ms a frame, confirmed " + (m.Tick - m.Confirmed) + " ticks back");
            }
            _netStatsAt = Time.unscaledTime;
            _netStalls = 0; _netFrames = 0; _netSimMs = 0f; _netWorstFrame = 0f;
            if (m != null) { _netRewinds = m.Rewinds; m.LongestRewind = 0; }
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
            _local.HoverFor = slot => _cardScreen.HoverFor(slot);
            _cardScreen.NameOf = NameOf;
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
            _local.HoverFor = slot => _session == null ? (byte)0 : _cardScreen.HoverFor(_session.PlayerOf(slot));
            _cardScreen.NameOf = NameOf;
        }

        // ------------------------------------------------------------------ lobby

        /// <summary>Setting up a local match: the join screen, then the rink, then how to win (see <see cref="PlayScreens"/>).</summary>
        private void LobbyUpdate()
        {
            if (_play.Current == PlayScreens.Page.None) _play.Open(PlayScreens.Page.Join);
            _play.Tick();
            if (_mode != Mode.Lobby) return;
            // everyone who has joined, standing at their spawn behind the screens
            _local.AssignTeams();
            _state = _matchSim.NewGame(_local.Teams());
            Show(_state, _state, 0f);
        }

        /// <summary>The play screens are done: start the match with the rink and rules they chose.</summary>
        public void StartLocalMatch()
        {
            _local.AssignTeams();
            StartMatch(false);
        }

        private readonly Dictionary<int, Texture2D> _previews = new Dictionary<int, Texture2D>();

        /// <summary>A small picture of rink <paramref name="i"/> for the rink select screen: the floor, walls and goals.</summary>
        public Texture2D MapPreview(int i)
        {
            if (i < 0 || i >= _maps.Count) return null;
            Texture2D tex;
            if (_previews.TryGetValue(i, out tex) && tex != null) return tex;
            var map = _maps[i];
            const int W = 384, H = 216;
            var min = V.Of(map.BoundsMin) - Vector2.one; var max = V.Of(map.BoundsMax) + Vector2.one;
            var scale = Mathf.Min(W / (max.x - min.x), H / (max.y - min.y));
            var off = new Vector2((W - (max.x - min.x) * scale) / 2f, (H - (max.y - min.y) * scale) / 2f);
            var outline = new List<Vector2>();
            foreach (var p in map.WallLines[0]) outline.Add(V.Of(p));
            var segs = new List<Vector2[]>();
            foreach (var sg in map.Segments) segs.Add(new[] { V.Of(sg.A), V.Of(sg.B) });
            var half = _matchSim.T.WallThickness.ToFloat() / 2f;
            var px = new Color32[W * H];
            for (var y = 0; y < H; y++)
                for (var x = 0; x < W; x++)
                {
                    var p = new Vector2((x + 0.5f - off.x) / scale + min.x, (y + 0.5f - off.y) / scale + min.y);
                    Color c = Color.clear;
                    if (InsidePoly(outline, p)) c = Palette.Floor;
                    foreach (var g in map.Goals)
                        if (p.x >= g.Min.X.ToFloat() && p.x <= g.Max.X.ToFloat() && p.y >= g.Min.Y.ToFloat() && p.y <= g.Max.Y.ToFloat()) c = Palette.Goal(g.Team);
                    foreach (var sg in segs)
                    {
                        var ab = sg[1] - sg[0];
                        var t = Mathf.Clamp01(Vector2.Dot(p - sg[0], ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                        if ((p - (sg[0] + ab * t)).sqrMagnitude <= half * half) { c = new Color(0.62f, 0.62f, 0.64f); break; }
                    }
                    px[y * W + x] = c;
                }
            tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px);
            tex.Apply();
            _previews[i] = tex;
            return tex;
        }

        private static bool InsidePoly(List<Vector2> poly, Vector2 p)
        {
            var inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) inside = !inside;
            return inside;
        }

        private void StartMatch(bool free)
        {
            _free = free;
            // the settings may have changed since: the rules come from them now
            _matchSim = new Simulation(_map, MatchTuning(false), _cards);
            _freeSim = new Simulation(_map, MatchTuning(true), _cards);
            _sim = free ? _freeSim : _matchSim;
            // a fresh seed every match, so games (and the cards offered) don't replay the same way each time
            var bots = new bool[_local.Slots.Count];
            for (var i = 0; i < bots.Length; i++) bots[i] = _local.Slots[i].Demo;
            _state = _sim.NewGame(_local.Teams(), (uint)System.Environment.TickCount * 2654435761u | 1u, bots);
            _prev = _state.Clone();
            _acc = 0f;
            Recorder.Clear();
            _replay = null;
            _mode = Mode.Match;
            _menus.FreePlay = free;
            foreach (var s in _local.Slots) s.Latched = 0;
        }

        private void BackToLobby()
        {
            // back to the join screen with everyone (and the bots) still there, to ready up again
            _local.UnreadyAll();
            _mode = Mode.Lobby;
            _play.Open(PlayScreens.Page.Join);
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

        /// <summary>Free play: the card editor, for everyone here (not the bots), to try any cards out.</summary>
        private void EditCards()
        {
            if (!_free || _state == null) return;
            var people = new List<int>();
            for (var i = 0; i < _local.Slots.Count && i < _state.Players.Length; i++) if (!_local.Slots[i].Demo) people.Add(i);
            _cardScreen.OpenEditor(_sim, _state, people);
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
            if (_cardScreen.Editing)
            {
                // free play's card editor: the match waits (the card screen runs it)
                Show(_state, _state, 0f);
                return;
            }
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
            else if (_local.SettingsPressed()) Settings.CornerCards = !Settings.CornerCards; // View / Tab: hide or show the card list

            _local.LatchPresses();
            var dt = 1f / _sim.T.TicksPerSecond;
            _acc += Mathf.Min(Time.deltaTime, 0.25f); // after a long hitch, don't try to catch up forever
            while (_acc >= dt)
            {
                _prev = _state.Clone();
                _sim.Step(_state, _local.Sample(_sim, _state));
                Recorder.Record(_state);
                _acc -= dt;
            }
            if (!_free && ReplayFrame(true)) return;
            Show(_prev, _state, _acc / dt);

            if (CardScreen.TestMode && _free && _state.Phase == Phase.Play && !_cardScreen.Showing)
            {
                _testPause += Time.deltaTime;
                if (_testPause > 1.5f) { _testPause = 0f; PickACard(); }
            }
        }

        private float _testPause;

        // ------------------------------------------------------------------ goal replays

        private ReplayBuffer _recorder;
        private ReplayBuffer Recorder { get { return _recorder ?? (_recorder = new ReplayBuffer(_sim.T.TicksPerSecond)); } }
        private GoalReplay _replay;
        private bool _replayPending;
        private float _replayStartAt;
        private int _goalTick;
        private Phase _lastPhase;

        /// <summary>
        /// After a goal (with replays on): the explosion plays live, then the replay. Returns true while the replay is
        /// what's on screen. <paramref name="local"/>: a game on this PC only, so A / Start may skip it (online
        /// everyone's game has to wait the same time, so it just plays).
        /// </summary>
        private bool ReplayFrame(bool local)
        {
            if (_state == null || _sim == null) return false;
            var tps = _sim.T.TicksPerSecond;
            if (_state.Phase != Phase.Scored)
            {
                _lastPhase = _state.Phase;
                _replay = null;
                _replayPending = false;
                _hud.Replay = false;
                return false;
            }
            if (_lastPhase != Phase.Scored)
            {
                _lastPhase = Phase.Scored;
                _goalTick = _state.Tick;
                _replayPending = _sim.T.Replays;
                _replayStartAt = Time.unscaledTime + 1.4f;
            }
            if (_replayPending && Time.unscaledTime >= _replayStartAt)
            {
                _replayPending = false;
                var frames = Recorder.Before(_goalTick + tps * 7 / 10, tps);
                _replay = new GoalReplay(frames, _state.LastScorer, _sim, _state.PhaseTime.ToFloat() - 0.4f);
            }
            if (_replay == null) return false;
            // skipping is a vote in the game itself (everyone presses shoot, bots always agree): the celebration
            // then ends, the phase changes, and the replay stops above
            GameState a, b;
            float t;
            _replay.Advance(Time.unscaledDeltaTime, tps, out a, out b, out t);
            if (_replay.Done || a == null)
            {
                _replay = null;
                _hud.Replay = false;
                return false;
            }
            _hud.Replay = true;
            _hud.ReplaySkippable = true;
            _hud.ReplaySlow = _replay.Slow < 0.9f;
            Show(a, b, t);
            return true;
        }

        /// <summary>A person on this PC (not a bot) who's charging a shot: the goal they're attacking and how far charged (0..1, Over Charge doesn't add).</summary>
        private bool LocalCharge(out Vector2 goal, out float charge)
        {
            goal = Vector2.zero; charge = 0f;
            if (_state == null || _sim == null || _state.Carrier < 0 || _state.Carrier >= _state.Players.Length) return false;
            var i = _state.Carrier;
            var p = _state.Players[i];
            if (p.Charge <= Fix.Zero) return false;
            bool human;
            if (_mode == Mode.Online && _session != null) human = i < _session.Seats.Count && !_session.Seats[i].Bot && _session.Seats[i].Peer == _session.MyPeer;
            else human = _mode == Mode.Match && i < _local.Slots.Count && !_local.Slots[i].Demo;
            if (!human) return false;
            goal = V.Of(Bot.GoalCentre(_sim.Map, 1 - p.Team));
            charge = Mathf.Clamp01(p.Charge.ToFloat());
            return true;
        }

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
        private bool CardEffects(int i, PlayerState p, Vector2 pos)
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
            _players[i].Status(hidden, faint, p.SlowTime > Fix.Zero, p.BoostTime > Fix.Zero, _cooldowns,
                p.Stun > Fix.Zero, p.DashTime.ToFloat() / Mathf.Max(0.01f, _sim.T.DashHitTime.ToFloat()), p.HitLag > Fix.Zero);

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
            return hidden;
        }

        /// <summary>Player <paramref name="i"/>'s symbol: their online seat's, or their slot's on this PC.</summary>
        private int SymbolOf(int i)
        {
            if (_mode == Mode.Online && _session != null) return i < _session.Seats.Count ? _session.Seats[i].Symbol : 0;
            return i < _local.Slots.Count ? _local.Slots[i].Symbol : 0;
        }

        /// <summary>Online: the teams' colours are the ones their players picked.</summary>
        private void OnlineColours()
        {
            if (_session == null) return;
            int c0 = -1, c1 = -1;
            foreach (var seat in _session.Seats) { if (seat.Team == 0 && c0 < 0) c0 = seat.Colour; if (seat.Team == 1 && c1 < 0) c1 = seat.Colour; }
            Palette.SetTeams(c0 < 0 ? (c1 == 0 ? 1 : 0) : c0, c1 < 0 ? (c0 == 1 ? 0 : 1) : c1);
        }

        private int _rinkColours = -1;

        /// <summary>The rink's goals are drawn in team colours: build it again when they change.</summary>
        private void RinkColours()
        {
            if (_rinkColours == Palette.Version || _map == null) return;
            _rinkColours = Palette.Version;
            if (_rink != null) Destroy(_rink.Root);
            _rink = new RinkView(_map, _matchSim.T.WallThickness.ToFloat());
        }

        /// <summary>The name over player <paramref name="i"/>: their online name, yours (then P2, P3...) on this PC, or Bot.</summary>
        private string NameOf(int i)
        {
            if (_mode == Mode.Online && _session != null) return i < _session.Seats.Count ? _session.Seats[i].Name : "";
            if (i >= _local.Slots.Count) return "";
            var s = _local.Slots[i];
            if (!string.IsNullOrEmpty(s.Name)) return s.Name;
            if (s.Demo) return "Bot";
            for (var k = 0; k < i; k++) if (!_local.Slots[k].Demo) return "P" + (i + 1);
            return Settings.PlayerName;
        }

        private void Show(GameState a, GameState b, float t)
        {
            _hud.Tags.Clear();
            _hud.Cam = _cam;
            while (_players.Count > b.Players.Length) { Destroy(_players[_players.Count - 1].Root); _players.RemoveAt(_players.Count - 1); }
            for (var i = 0; i < b.Players.Length; i++)
            {
                var pb = b.Players[i];
                var pa = i < a.Players.Length ? a.Players[i] : pb;
                var radius = _sim.RadiusOf(pb).ToFloat();
                var symbol = SymbolOf(i);
                var key = "Player" + pb.Team + "_" + Mathf.RoundToInt(radius * 1000f) + "_" + symbol + "_" + Palette.Version;
                if (i >= _players.Count || _players[i].Root.name != key)
                {
                    // new player, or their size, symbol or team colour changed: build them again
                    var view = new PlayerView(pb.Team, radius, _entities, symbol);
                    view.Root.name = key;
                    if (i < _players.Count) { Destroy(_players[i].Root); _players[i] = view; }
                    else _players.Add(view);
                }
                var pos = (V.Of(pb.Pos) - V.Of(pa.Pos)).sqrMagnitude > 4f ? V.Of(pb.Pos) : Vector2.Lerp(V.Of(pa.Pos), V.Of(pb.Pos), t);
                var facing = Vector2.Lerp(V.Of(pa.Facing), V.Of(pb.Facing), t);
                var full = Simulation.MaxDashes(pb).ToFloat();
                Vector2 hitAt;
                var hit = _players[i].Show(pos, facing, pb.DashCharge.ToFloat(), Mathf.RoundToInt(full), pb.Charge.ToFloat(), pb.Hits, pb.HitPower.ToFloat(), V.Of(pb.HitDir), out hitAt);
                if (!CardEffects(i, pb, pos))
                    _hud.Tags.Add(new Hud.Tag { World = new Vector3(pos.x, pos.y + radius + 0.62f, 0f), Name = NameOf(i), Team = pb.Team });
                if (hit > 0f)
                {
                    _hitsThisFrame.Add(new Vector3(hitAt.x, hitAt.y, hit));
                    _shake = Mathf.Max(_shake, 0.06f + 0.22f * hit * hit);
                }
            }
            // both players in a hit report it: one effect per contact, as hard as the harder report
            _hitsThisFrame.Sort((p, q) => q.z.CompareTo(p.z));
            foreach (var h in _hitsThisFrame)
            {
                var at = new Vector2(h.x, h.y);
                var near = false;
                foreach (var o in _impactsThisFrame) if ((o - at).sqrMagnitude < 0.6f) near = true;
                // small bumps only squash; sparks for every real hit; lightning only for the big ones (a much heavier checker)
                if (near || h.z < 0.35f || !Settings.HitEffects) continue;
                _effects.Sparks(at, h.z, new Color(1f, 0.85f, 0.4f));
                if (h.z >= 0.75f) _effects.Lightning(at, h.z, new Color(0.85f, 0.08f, 0.1f), new Color(0.16f, 0.02f, 0.03f));
                _impactsThisFrame.Add(at);
            }
            _hitsThisFrame.Clear();
            _impactsThisFrame.Clear();
            if (_seenShocks.Count > b.Players.Length) { _seenShocks.RemoveRange(b.Players.Length, _seenShocks.Count - b.Players.Length); _seenRefreshes.RemoveRange(b.Players.Length, _seenRefreshes.Count - b.Players.Length); }
            _shots.Show(b, _sim.T.ShotRadius.ToFloat());
            _pylons.Show(b, _sim.T.PylonRadius.ToFloat());
            _ice.Show(b, _sim.T.IceRadius.ToFloat());
            // a puck that just jumped (face-off) shouldn't slide across the rink
            var pa2 = V.Of(a.PuckPos); var pb2 = V.Of(b.PuckPos);
            // a goal: the puck blows up in the net, and stays gone until the next face-off
            if (b.Phase == Phase.Scored && a.Phase != Phase.Scored)
            {
                _effects.Explosion(pb2, b.LastScorer >= 0 ? Palette.Team(b.LastScorer) : Color.white);
                _shake = Mathf.Max(_shake, 0.7f);
            }
            _puck.Visible(b.Phase != Phase.Scored);
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
