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
    internal sealed partial class GameRunner : MonoBehaviour
    {
        private enum Mode { Menu, Lobby, Match, Online, Replay, Editor }

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
        /// <summary>Who had the puck last tick (for auto-swapping onto a bot teammate that just picked it up).</summary>
        private int _seenCarrier = -1;

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
        /// <summary>The Profile looks' particles (dash trails, goal explosions), and each player's running count for their trail.</summary>
        private StyleFx _styleFx;
        private float[] _dashAcc = new float[16];
        private ReplayViewer _viewer;
        private ShotViews _shots;
        private PylonViews _pylons;
        private RockViews _rocks;
        private readonly List<int> _seenSwaps = new List<int>();
        private IceViews _ice;
        private StoneViews _stones;
        private PortalViews _portals;
        private SlotViews _slots;
        private readonly PortalSplit _split = new PortalSplit();
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
            Sound.Create(gameObject, ShotDir != null ? ShotVolume : -1f); // taking screenshots: silent unless asked
            Sound.Music(false);
            _hud = gameObject.AddComponent<Hud>();
            _menus = gameObject.AddComponent<Menus>();
            _cardScreen = gameObject.AddComponent<CardScreen>();
            _mainMenu = gameObject.AddComponent<MainMenu>();
            _play = gameObject.AddComponent<PlayScreens>();
            _play.Runner = this;
            _play.Local = _local;
            _maker = gameObject.AddComponent<RinkMaker>();
            _maker.Runner = this;
            gameObject.AddComponent<Updater>();
            gameObject.AddComponent<Toasts>();
            _mainMenu.Runner = this;
            _mainMenu.Local = _local;
            LoadMaps();
            if (_maps.Count == 0) { _error = "Couldn't load any map: " + _mapProblem; Debug.LogError(_error); return; }
            _builtinMaps = _maps.Count;
            LoadCustomMaps(); // the rinks from the rink maker, after the game's own
            if (ShotDir == null) _arenas = ArenaSkin.LoadAll(PlainMapFiles(), RinkNameOf); // (screenshot runs show only their own test arena)
            _cards = LoadCards();
            InputIcons.Warm(); // Kenney button pictures for hints and utility card prompts
            _fingerprint = Fingerprint();
            var saved = PlayerPrefs.GetString("map_file", "test-rink.json");
            // behind the main menu: a random rink (not saved: the one you picked comes back when you go to play).
            // Screenshot runs keep the saved one, so their pictures don't change.
            _menuRink = ShotDir == null && _maps.Count > 1;
            BuildSims(_menuRink ? UnityEngine.Random.Range(0, _builtinMaps) : Mathf.Max(0, _mapFiles.IndexOf(saved)));

            SteamNet.Init();
            LogDevices("at start");
            UnityEngine.InputSystem.InputSystem.onDeviceChange += (d, change) =>
            {
                Debug.Log("Ultra Puck: input device " + change + ": " + d.displayName + " (" + d.layout + ")");
                if (change == UnityEngine.InputSystem.InputDeviceChange.Added || change == UnityEngine.InputSystem.InputDeviceChange.Reconnected) _local.DeviceAdded(d);
                // Steam Input grabs controllers a moment after Steam starts (while we run as Steam's test app, whose Steam Input
                // settings we can't change): say what to do in a toast, top right, that doesn't block the menu
                if (d is UnityEngine.InputSystem.Gamepad && change == UnityEngine.InputSystem.InputDeviceChange.Removed && SteamNet.Ready && Time.realtimeSinceStartup - SteamNet.StartedAt < 10f)
                    Toasts.Show("Steam took your " + d.displayName + ". To play with it: Steam > Settings > Controller, turn Steam Input off, then restart Ultra Puck.", 12f);
            };
            Invoke("LogDevicesLater", 3f);
            SteamNet.OnJoined = host => StartOnline(NetSession.Join(SteamTransport.Join(host), MakeOnlineSim, _fingerprint));
            SteamNet.OnFailed = why => _mainMenu.Message = why;

            _cardScreen.Local = _local;
            StartCoroutine(_cardScreen.PreloadArt(_cards)); // every card picture loaded before the first pick
            Crosshairs.UseGameCursor(); // the game's own mouse pointer
            StartCoroutine(WarmPreviews()); // and every rink's picture for the rink screen
            _cardScreen.PlayerOnScreen = i => i >= 0 && i < _players.Count && _players[i].Root != null ? _cam.WorldToScreenPoint(_players[i].Root.transform.position) : (Vector3?)null;
            _cardScreen.OnAbsorbed = (i, colour) =>
            {
                if (i < 0 || i >= _players.Count || _players[i].Root == null) return;
                Vector2 at = _players[i].Root.transform.position;
                Sound.Play(Sfx.CardAbsorb, 0.75f, 1f, Pan(at));
                // like a drop landing in water: a big ripple that bends the rink as it spreads, and a smaller echo
                _ripples.Add(new Ripple { At = at, Colour = colour, When = Clock.Now, Size = 5.5f, Life = 1.5f });
                _ripples.Add(new Ripple { At = at, Colour = colour, When = Clock.Now + 0.22f, Size = 3.2f, Life = 1.1f });
            };
            LocalCardPicks();
            _menus.OnMainMenu = GoMainMenu;
            _menus.BotCount = () => _local.BotCount;
            _menus.MaxBots = () => LocalPlayers.MaxPlayers - _local.HumanCount;
            _menus.SetBots = SetBots;
            _menus.OnGetCard = EditCards;
            // the pause menu's match settings: the host online, anyone in a match here
            _menus.CanEditMatch = () => _mode == Mode.Online ? _session != null && _session.IsHost : _mode == Mode.Match && !_free;
            _menus.CanAddBot = () => _state != null && _state.Players.Length < LocalPlayers.MaxPlayers && _state.Phase != Phase.Picking
                && (_mode != Mode.Online || (_session != null && _session.IsHost && _local.Slots.Count > 0));
            _menus.HostCommand = HostCommand;
            // everyone online can look at the match settings; only the host changes them (the others see the host's)
            _menus.CanViewMatch = () => _mode == Mode.Online && OnlinePlaying;
            _menus.ShownRules = () => _mode == Mode.Online && _session != null && !_session.IsHost ? _session.LobbyRules : null;
            _menus.LiveState = () => InMatch ? _state : null;
            // changing team (or watching, online) from the pause menu
            _menus.TeamPlayers = TeamPlayers;
            _menus.TeamPlayerName = k => { var at = TeamPlayers(); return k < at.Count ? SlotName(at[k]) : ""; };
            _menus.TeamOfPlayer = k => { var at = TeamPlayers(); return k < at.Count ? SlotTeam(at[k]) : 0; };
            _menus.SetTeamOf = (k, team) => { var at = TeamPlayers(); if (k < at.Count) SetSlotTeam(at[k], team); };
            _menus.CanWatch = () => _mode == Mode.Online;
            _menus.Score = t => _state != null ? _state.Score[t & 1] + PendingScore(t) : 0;
            _menus.AddBotNow = AddBotNow;
            _menus.RemoveBotNow = RemoveBotNow;
            _menus.CanRemoveBot = CanRemoveBot;
            _menus.TeamBots = t => { var n = 0; if (_state != null) foreach (var p in _state.Players) if (p.Bot && (p.Team & 1) == (t & 1)) n++; return n; };
            _menus.TeamCount = t => { var n = 0; if (_state != null) foreach (var p in _state.Players) if (p.Team == t) n++; return n; };
            _menus.TeamName = t => Palette.TeamName(t);
            _menus.MapCount = () => _mode == Mode.Online ? _builtinMaps : PlayableMaps; // (online: only the game's own rinks)
            _menus.MapName = MapName;
            _menus.CurrentMap = () => _mapIndex;
            _menus.OnRestart = map =>
            {
                if (map != _mapIndex) UseMap(map);
                if (_mode == Mode.Online) StartOnlineMatch();
                else StartMatch(_free); // free play stays free play when you change rink
            };
            _menus.Cards = _cards;
            _hud.PlayerName = NameOf;
            _hud.SeatName = SeatName;

            _rink = new RinkView(_map, _matchSim.T.WallThickness.ToFloat()); _rinkDrawn = _map;
            _entities = new GameObject("Entities").transform;
            _puck = new PuckView(_sim.T.PuckRadius.ToFloat(), _entities);
            _effects = new Effects(_entities);
            _styleFx = new StyleFx(_entities, _effects);
            _viewer = gameObject.AddComponent<ReplayViewer>();
            _viewer.OnClose = CloseReplay;
            _viewer.OnExport = (from, to, copy) => StartCoroutine(ExportClip(from, to, copy));
            _viewer.NameOf = SeatName;
            _hud.StyleOf = StyleOf;
            // the Black Hole explosion pulls everyone in, so the game itself has to know (it goes with the controls)
            _local.LookFor = slot => slot == FirstPerson() && Cosmetics.MyBoom == Cosmetics.BlackHoleBoom ? PlayerInput.LookPull : (byte)0;
            _hud.PictureOf = i => SteamNet.Avatar(AccountOf(i));
            _hud.SymbolOf = SymbolOf;
            _shots = new ShotViews(_entities);
            _pylons = new PylonViews(_entities);
            _rocks = new RockViews(_entities);
            _ice = new IceViews(_entities);
            _stones = new StoneViews(_entities, _effects);
            _portals = new PortalViews(_entities, _effects);
            _slots = new SlotViews(_entities);
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
            else _mainMenu.Show(MainMenu.Page.Title); // a normal start: "press any button" first
        }

        private void LogDevicesLater() { LogDevices("after 3 s"); }

        // ------------------------------------------------------------------ screenshots (for checking screens without playing)

        /// <summary>ULTRAPUCK_SHOTS=folder: set up each screen worth checking, save a picture of it there, then quit.</summary>
        private static readonly string ShotDir = System.Environment.GetEnvironmentVariable("ULTRAPUCK_SHOTS");

        private static int _rinkMaker = -1;
        /// <summary>
        /// The rink maker (Custom rinks) is still being finished, so it's off in releases: on in the Unity editor, in
        /// screenshot runs, and with -rinkmaker on the command line (or ULTRAPUCK_RINKMAKER=1). Off, the main menu has no
        /// Custom rinks, the rink screen no LB / RB to them, and no custom rinks are loaded.
        /// </summary>
        public static bool RinkMakerOn
        {
            get
            {
                if (_rinkMaker < 0)
                    _rinkMaker = Application.isEditor || ShotDir != null || System.Environment.GetEnvironmentVariable("ULTRAPUCK_RINKMAKER") == "1"
                                 || System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-rinkmaker") >= 0 ? 1 : 0;
                return _rinkMaker == 1;
            }
        }
        /// <summary>
        /// Screenshots are silent, unless ULTRAPUCK_SHOTS_VOLUME says how loud to play them (0..1; 0.3 is the game's
        /// normal volume). The player's saved volume is left alone either way.
        /// </summary>
        private static float ShotVolume
        {
            get
            {
                float v;
                var text = System.Environment.GetEnvironmentVariable("ULTRAPUCK_SHOTS_VOLUME");
                return float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) ? Mathf.Clamp01(v) : 0f;
            }
        }
        /// <summary>Screenshots: show the whole rink, not the players' part of it.</summary>
        private bool _shotWholeRink;
        /// <summary>Screenshots: nobody presses anything (bots and devices alike), so a set-up scene stays as it was set.</summary>
        private bool _shotNoInput;
        /// <summary>Screenshots: move the camera by this much (to see what moving the view does).</summary>
        private Vector2 _shotNudge;

        private System.Collections.IEnumerator Shots()
        {
            Directory.CreateDirectory(ShotDir);
            var only = System.Environment.GetEnvironmentVariable("ULTRAPUCK_SHOTS_ONLY");
            System.Func<string, bool> want = n => string.IsNullOrEmpty(only) || only.Contains(n);
            yield return new WaitForSecondsRealtime(1f);
            var kb = UnityEngine.InputSystem.Keyboard.current;

            if (want("title"))
            {
                // the screen the game starts on: partway through its entrance, settled, then handing over to the main menu
                _mainMenu.TitleStays = true;
                _mainMenu.Show(MainMenu.Page.Title);
                // (it waits ~1.2 s for the window first: the letters, the puck falling, landing, the beams, all done)
                yield return new WaitForSecondsRealtime(2.4f);
                yield return Shot("title-0");
                yield return new WaitForSecondsRealtime(0.75f);
                yield return Shot("title-1");
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Shot("title-1b");
                yield return new WaitForSecondsRealtime(3.2f);
                yield return Shot("title-2");
                _mainMenu.LeaveTitle();
                yield return new WaitForSecondsRealtime(0.2f);
                yield return Shot("title-3");
                yield return new WaitForSecondsRealtime(0.9f);
                yield return Shot("title-4");
            }

            if (want("music"))
            {
                // nothing to look at: this one is read in the log. In a match, the song that's playing is jumped to
                // a second before its end, four times over: each time another song must start, never the same one
                // again. Then back to the menu, where the theme must carry on.
                _local.Slots.Add(new LocalPlayers.Slot { Device = kb, Team = 0 });
                _local.Slots.Add(new LocalPlayers.Slot { Team = 1, Demo = true });
                _mainMenu.Show(MainMenu.Page.None);
                StartMatch(true);
                for (var k = 1; k <= 4; k++)
                {
                    yield return new WaitForSecondsRealtime(1.5f);
                    Debug.Log("Ultra Puck sound at music-" + k + ": " + Sound.Report());
                    Sound.SkipToSongEnd(1f);
                    yield return new WaitForSecondsRealtime(2.5f);
                }
                Debug.Log("Ultra Puck sound at music-5: " + Sound.Report());
                GoMainMenu();
                yield return new WaitForSecondsRealtime(1.5f);
                Debug.Log("Ultra Puck sound at music-menu: " + Sound.Report());
            }

            if (want("video"))
            {
                // frames for a short video (30 a second, the game stepped exactly that much each): the rink screen going
                // from Classic to Underwater, then a match there from the countdown
                var startMap = _mapIndex;
                var dir = Path.Combine(ShotDir, "video");
                Directory.CreateDirectory(dir);
                GoLocalLobby();
                _local.Slots.Add(new LocalPlayers.Slot { Device = kb, Colour = 0, Symbol = 0, Name = "FancyDuck" });
                var hockey = _mapFiles.IndexOf("hockey-rink.json");
                if (hockey >= 0) UseMap(hockey);
                _play.Open(PlayScreens.Page.Map);
                yield return new WaitForSecondsRealtime(1f);
                Time.captureFramerate = 30;
                yield return VideoFrames(dir, 40);
                StepLook(1);
                yield return VideoFrames(dir, 60);
                _play.Open(PlayScreens.Page.None);
                _local.Slots.Clear();
                for (var b = 0; b < 4; b++) _local.Slots.Add(new LocalPlayers.Slot { Team = b & 1, Colour = b & 1, Demo = true, Name = "Bot" });
                _local.AssignTeams();
                _mainMenu.Show(MainMenu.Page.None);
                _shotWholeRink = true;
                StartMatch(false);
                yield return VideoFrames(dir, 420);
                Time.captureFramerate = 0;
                _shotWholeRink = false;
                GoMainMenu();
                UseMap(startMap);
                Debug.Log("Ultra Puck video: " + _videoFrame + " frames in " + dir);
            }

            if (want("forest"))
            {
                // the Forest look of the Hockey Rink, with bots playing on it
                var startMap = _mapIndex;
                var forest = _mapFiles.IndexOf("hockey-rink-forest.json");
                if (forest >= 0)
                {
                    UseMap(forest);
                    GoLocalLobby();
                    _play.Open(PlayScreens.Page.None);
                    _local.Slots.Clear();
                    for (var b = 0; b < 4; b++) _local.Slots.Add(new LocalPlayers.Slot { Team = b & 1, Colour = b & 1, Demo = true, Name = "Bot" });
                    _local.AssignTeams();
                    _mainMenu.Show(MainMenu.Page.None);
                    _shotWholeRink = true;
                    StartMatch(true);
                    yield return new WaitForSecondsRealtime(4f);
                    yield return Shot("forest-1");
                    yield return new WaitForSecondsRealtime(1.5f);
                    yield return Shot("forest-2");
                    _shotWholeRink = false;
                    GoMainMenu();
                }
                UseMap(startMap);
            }

            if (want("rinkpick"))
            {
                // the rink screen with a rink that has looks (the Hockey Rink: Classic and Underwater), on each look
                var startMap = _mapIndex;
                GoLocalLobby();
                _local.Slots.Add(new LocalPlayers.Slot { Device = kb, Colour = 0, Symbol = 0, Name = "FancyDuck" });
                var hockey = _mapFiles.IndexOf("hockey-rink.json");
                if (hockey >= 0) UseMap(hockey);
                _play.Open(PlayScreens.Page.Map);
                yield return new WaitForSecondsRealtime(0.8f);
                yield return Shot("rinkpick-classic");
                StepLook(1);
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Shot("rinkpick-underwater");
                // and the underwater rink itself, with bots playing on it
                _play.Open(PlayScreens.Page.None);
                _local.Slots.Clear();
                for (var b = 0; b < 4; b++) _local.Slots.Add(new LocalPlayers.Slot { Team = b & 1, Colour = b & 1, Demo = true, Name = "Bot" });
                _local.AssignTeams();
                _mainMenu.Show(MainMenu.Page.None);
                _shotWholeRink = true;
                StartMatch(true);
                yield return new WaitForSecondsRealtime(4f);
                yield return Shot("underwater-1");
                yield return new WaitForSecondsRealtime(1.5f);
                yield return Shot("underwater-2");
                _shotWholeRink = false;
                GoMainMenu();
                UseMap(startMap);
            }

            if (want("arenas"))
            {
                // a custom arena (made here, as a zip in memory, like one from the Custom_Arenas folder): picked on the
                // rink screen with up / down, then on the rink with bots playing (background, shadows, foreground)
                var startMap = _mapIndex;
                var hockey = _mapFiles.IndexOf("hockey-rink.json");
                if (hockey >= 0) UseMap(hockey);
                _shotArena = ArenaSkin.FromZip(TestArenaZip(_map), "Test Arena.zip", PlainMapFiles(), RinkNameOf);
                _shotArena.Name = "Test Arena";
                _arenas.Add(_shotArena);
                GoLocalLobby();
                _local.Slots.Add(new LocalPlayers.Slot { Device = kb, Colour = 0, Symbol = 0, Name = "FancyDuck" });
                _play.Open(PlayScreens.Page.Map);
                yield return new WaitForSecondsRealtime(0.8f);
                yield return Shot("arena-pick");
                _play.Open(PlayScreens.Page.None);
                _local.Slots.Clear();
                for (var b = 0; b < 4; b++) _local.Slots.Add(new LocalPlayers.Slot { Team = b & 1, Colour = b & 1, Demo = true, Name = "Bot" });
                _local.AssignTeams();
                _mainMenu.Show(MainMenu.Page.None);
                _shotWholeRink = true;
                StartMatch(true);
                yield return new WaitForSecondsRealtime(3f);
                yield return Shot("arena-match");
                _shotWholeRink = false;
                GoMainMenu();
                _arenas.Remove(_shotArena);
                _shotArena = null;
                UseMap(startMap);
            }

            // arena designs: every zip in the folder ULTRAPUCK_ARENA_DIR names, on its rink with bots playing
            // ("arena-<zip name>.png"), to look at a design exactly as the game draws it
            var designs = System.Environment.GetEnvironmentVariable("ULTRAPUCK_ARENA_DIR");
            if (want("arena-files") && !string.IsNullOrEmpty(designs) && System.IO.Directory.Exists(designs))
            {
                var startMap = _mapIndex;
                foreach (var zip in System.IO.Directory.GetFiles(designs, "*.zip"))
                {
                    var a = ArenaSkin.FromZip(System.IO.File.ReadAllBytes(zip), System.IO.Path.GetFileName(zip), PlainMapFiles(), RinkNameOf);
                    if (a == null || a.Rink == null) { Debug.LogWarning("Ultra Puck: no arena in " + zip); continue; }
                    var rink = _mapFiles.IndexOf(a.Rink);
                    if (rink >= 0) UseMap(rink);
                    _shotArena = a;
                    GoLocalLobby();
                    _play.Open(PlayScreens.Page.None);
                    _local.Slots.Clear();
                    for (var b = 0; b < 4; b++) _local.Slots.Add(new LocalPlayers.Slot { Team = b & 1, Colour = b & 1, Demo = true, Name = "Bot" });
                    _local.AssignTeams();
                    _mainMenu.Show(MainMenu.Page.None);
                    _shotWholeRink = true;
                    StartMatch(true);
                    yield return new WaitForSecondsRealtime(3f);
                    yield return Shot("arena-" + System.IO.Path.GetFileNameWithoutExtension(zip));
                    _shotWholeRink = false;
                    GoMainMenu();
                    _shotArena = null;
                    a.Unload();
                }
                UseMap(startMap);
            }

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
                _local.Slots.Add(new LocalPlayers.Slot { Team = 0, Colour = 0, Demo = true });
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
                _state.PassFrom = 0; _state.Touch1 = 0; _state.Touch2 = 2; // P1 shot it, set up by the bot teammate
                yield return new WaitForSecondsRealtime(1.2f); // GOAL! then the wipe in
                yield return Shot("replay-wipe");
                yield return new WaitForSecondsRealtime(4f); // into the replay
                yield return Shot("replay");
                GoMainMenu();
            }

            yield return ProfileShots(want, kb);

            if (want("countdown"))
            {
                // the countdown (3), START as play begins, then the scoreboard (View / Tab held)
                _local.Slots.Clear();
                _local.Slots.Add(new LocalPlayers.Slot { Device = kb, Team = 0, Colour = 0 });
                _local.Slots.Add(new LocalPlayers.Slot { Team = 0, Colour = 0, Demo = true });
                _local.Slots.Add(new LocalPlayers.Slot { Team = 1, Colour = 1, Demo = true });
                _local.AssignTeams();
                _mainMenu.Show(MainMenu.Page.None);
                StartMatch(false);
                _shotNoInput = true;
                yield return new WaitForSecondsRealtime(0.25f);
                yield return Shot("countdown");
                while (_state != null && _state.Phase == Phase.Countdown) yield return null;
                yield return new WaitForSecondsRealtime(0.2f);
                yield return Shot("start");
                _state.Players[0].Cards = new[] { 0, 1, 1 };
                _state.Players[2].Cards = new[] { 2 };
                _state.Players[0].Goals = 2; _state.Players[1].Assists = 1; _state.Players[2].Saves = 3; _state.Players[2].Bumps = 2;
                _state.Score[0] = 2; _state.Score[1] = 1;
                _shotBoard = true;
                yield return new WaitForSecondsRealtime(0.3f);
                yield return Shot("scoreboard");
                _shotBoard = false;
                _shotNoInput = false;
                GoMainMenu();
            }

            if (want("gameover"))
            {
                // after the match: everyone's stats, the MVP and the least useful player
                _local.Slots.Clear();
                _local.Slots.Add(new LocalPlayers.Slot { Device = kb, Team = 0, Colour = 0 });
                _local.Slots.Add(new LocalPlayers.Slot { Team = 0, Colour = 0, Demo = true });
                _local.Slots.Add(new LocalPlayers.Slot { Team = 1, Colour = 1, Demo = true });
                _local.Slots.Add(new LocalPlayers.Slot { Team = 1, Colour = 1, Demo = true });
                _local.AssignTeams();
                _mainMenu.Show(MainMenu.Page.None);
                StartMatch(false);
                yield return new WaitForSecondsRealtime(1f);
                int[,] st = { { 3, 1, 0, 4 }, { 1, 2, 1, 2 }, { 2, 0, 3, 1 }, { 0, 0, 0, 0 } };
                for (var i = 0; i < 4 && i < _state.Players.Length; i++)
                {
                    var p = _state.Players[i];
                    p.Goals = st[i, 0]; p.Assists = st[i, 1]; p.Saves = st[i, 2]; p.Bumps = st[i, 3];
                }
                _state.Score[0] = 4; _state.Score[1] = 2;
                _state.LastScorer = 0;
                _state.Phase = Phase.GameOver;
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Shot("gameover");
                // the pause menu's match settings: add a bot to either team
                _menus.OpenPause("Match");
                _menus.OpenMatch();
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Shot("match-settings");
                _menus.Close();
                GoMainMenu();
                // the players screen: the leaving bar (top left) and a "left the game" pop-up (top right)
                GoLocalLobby();
                Hud.ShotLeaving = 0.6f;
                Toasts.Show("Sam left the game. Back to the lobby.");
                yield return new WaitForSecondsRealtime(0.8f);
                yield return Shot("toast-leaving");
                Hud.ShotLeaving = 0f;
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

            if (want("goal"))
            {
                // the puck put in the blue net: the explosion a moment in, and a little later
                _local.Slots.Add(new LocalPlayers.Slot { Device = kb, Team = 0, Colour = 0 });
                _local.Slots.Add(new LocalPlayers.Slot { Team = 1, Colour = 1, Demo = true });
                _local.AssignTeams();
                _mainMenu.Show(MainMenu.Page.None);
                StartMatch(false);
                yield return new WaitForSecondsRealtime(3.5f);
                var g = _map.Goals.Find(x => x.Team == 1);
                _state.Carrier = -1;
                _state.PuckPos = new FixVec2((g.Min.X + g.Max.X) * Fix.Half, (g.Min.Y + g.Max.Y) * Fix.Half);
                _state.PuckVel = FixVec2.Zero;
                yield return new WaitForSecondsRealtime(0.18f);
                yield return Shot("goal-1");
                yield return new WaitForSecondsRealtime(0.25f);
                yield return Shot("goal-2");
                GoMainMenu();
            }

            if (want("cards"))
            {
                // a card pick with 4 and with 5 offered: they should fan out in an arc
                foreach (var count in new[] { 4, 5 })
                {
                    _local.Slots.Add(new LocalPlayers.Slot { Device = kb, Team = 0 });
                    _mainMenu.Show(MainMenu.Page.None);
                    StartMatch(true);
                    _sim.T.CardsOffered = count;
                    yield return new WaitForSecondsRealtime(0.8f);
                    PickACard();
                    if (count == 4 && _cards != null && _state.Players.Length > 0)
                    {
                        // the newest cards, so their faces get looked at
                        var ids = new[] { "ultrapuck.momentum", "ultrapuck.loyal", "ultrapuck.portalgun", "ultrapuck.stonewall" };
                        var offer = new List<int>();
                        foreach (var id in ids) { var c = _cards.ById(id); if (c != null) offer.Add(c.Index); }
                        _state.Players[0].Offer = offer.ToArray();
                    }
                    yield return new WaitForSecondsRealtime(2.2f);
                    yield return Shot("cards-" + count);
                    GoMainMenu();
                    yield return new WaitForSecondsRealtime(0.5f);
                }
            }

            if (want("rinkmaker"))
            {
                // the rink maker: the Custom Rinks screen, making a rink (a pillar, a bump, some stray ice), testing it,
                // and the rink screen's Custom Rinks (LB / RB)
                OpenCustomRinks();
                yield return new WaitForSecondsRealtime(0.8f);
                yield return Shot("rinkmaker-library");
                _maker.CloseLibrary();
                var plan = RinkPlan.Starter();
                plan.Name = "Pillar Park";
                for (var cy = 4; cy < 8; cy++) for (var cx = -3; cx < 3; cx++) plan.SetIce(cx, cy, false);
                for (var cy = 13; cy < 17; cy++) for (var cx = -7; cx < 7; cx++) plan.SetIce(cx, cy, true);
                for (var cy = 18; cy < 21; cy++) for (var cx = 30; cx < 34; cx++) plan.SetIce(cx, cy, true);
                // wall lines: a mirrored L in each half, and a short divider in front of each net
                foreach (var side in new[] { -1, 1 })
                {
                    for (var x = 8; x < 14; x++) plan.SetWall(side < 0 ? -x - 1 : x, -6, false, true);
                    for (var y = -6; y < -2; y++) plan.SetWall(side * 14, y, true, true);
                    for (var y = 5; y < 9; y++) plan.SetWall(side * 17, y, true, true);
                }
                EditRink(plan, null);
                yield return new WaitForSecondsRealtime(1.2f);
                yield return Shot("rinkmaker-edit");
                TestRink(plan, kb);
                yield return new WaitForSecondsRealtime(3f);
                yield return Shot("rinkmaker-test");
                BackToMaker();
                yield return new WaitForSecondsRealtime(0.8f);
                yield return Shot("rinkmaker-back");
                // saved (only with ULTRAPUCK_RINK_DIR set: a screenshot run doesn't touch the real folder), and an unfinished one
                if (!string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("ULTRAPUCK_RINK_DIR")))
                {
                    CustomRinks.Save(plan, null);
                    var half = RinkPlan.Starter();
                    half.Name = "Half Done";
                    half.Nets[1].Placed = false;
                    CustomRinks.Save(half, null);
                }
                MakerClosed();
                yield return new WaitForSecondsRealtime(0.8f);
                yield return Shot("rinkmaker-saved");
                _maker.CloseLibrary();
                GoMainMenu();
                yield return new WaitForSecondsRealtime(0.5f);
                _local.Slots.Add(new LocalPlayers.Slot { Device = kb, Team = 0 });
                GoLocalLobby();
                _play.Open(PlayScreens.Page.Map);
                yield return new WaitForSecondsRealtime(0.8f);
                yield return Shot("rinkmaker-rinkscreen");
                OpenCustomPick();
                _play.Open(PlayScreens.Page.Custom);
                yield return new WaitForSecondsRealtime(0.8f);
                yield return Shot("rinkmaker-pick");
                _maker.CloseLibrary();
                GoMainMenu();
                yield return new WaitForSecondsRealtime(0.5f);
            }

            if (want("editor"))
            {
                // free play's card editor, two a row, on the bottom row (the raised card stays on screen) and
                // three a row; the controls down the left side
                _local.Slots.Add(new LocalPlayers.Slot { Device = kb, Team = 0 });
                _mainMenu.Show(MainMenu.Page.None);
                StartMatch(true);
                yield return new WaitForSecondsRealtime(0.8f);
                EditCards();
                yield return new WaitForSecondsRealtime(1.2f);
                _cardScreen.ShotGrid(2, 3);
                yield return new WaitForSecondsRealtime(1.2f);
                yield return Shot("editor-2");
                _cardScreen.ShotGrid(3, 4);
                yield return new WaitForSecondsRealtime(1.2f);
                yield return Shot("editor-3");
                GoMainMenu();
                yield return new WaitForSecondsRealtime(0.5f);
            }

            if (want("minis"))
            {
                // others' picks at the side: you choosing while bots on both teams pick (theirs come in small, your
                // teammates' on the left, the other team's on the right), one clicked up to full size, and the other
                // team picking on its own (nothing dealt to watch, just who's choosing and their picks)
                _local.Slots.Add(new LocalPlayers.Slot { Device = kb, Team = 0, Name = "FancyDuck" });
                _local.Slots.Add(new LocalPlayers.Slot { Team = 0, Demo = true, Name = "Bot" });
                _local.Slots.Add(new LocalPlayers.Slot { Team = 1, Demo = true, Name = "Bot" });
                _local.Slots.Add(new LocalPlayers.Slot { Team = 1, Demo = true, Name = "Bot" });
                _mainMenu.Show(MainMenu.Page.None);
                StartMatch(true);
                _sim.T.CardsOffered = 3;
                yield return new WaitForSecondsRealtime(0.8f);
                _sim.StartPicking(_state, new[] { 0, 1, 2, 3 });
                yield return new WaitForSecondsRealtime(3.5f);
                yield return Shot("minis-choosing");
                _cardScreen.ShotBigMini();
                yield return new WaitForSecondsRealtime(1f);
                yield return Shot("minis-big");
                _cardScreen.ShotBigMini();
                _cardScreen.ShotConfirm = true;
                yield return new WaitForSecondsRealtime(3f);
                _sim.StartPicking(_state, new[] { 2, 3 });
                yield return new WaitForSecondsRealtime(1.2f);
                yield return Shot("minis-watching");
                yield return new WaitForSecondsRealtime(2.5f);
                yield return Shot("minis-watching-2");
                GoMainMenu();
                yield return new WaitForSecondsRealtime(0.5f);
            }

            if (want("cardfx"))
            {
                // the animated finishes (borealis, rainbow), put on two cards for this run only (not saved), at two moments
                _local.Slots.Add(new LocalPlayers.Slot { Device = kb, Team = 0 });
                _mainMenu.Show(MainMenu.Page.None);
                StartMatch(true);
                _sim.T.CardsOffered = 4;
                yield return new WaitForSecondsRealtime(0.8f);
                var fx = new[] { "ultrapuck.momentum", "ultrapuck.loyal" };
                var was = new string[fx.Length];
                for (var k = 0; k < fx.Length; k++) { var c = _cards != null ? _cards.ById(fx[k]) : null; if (c != null) { was[k] = c.Finish; c.Finish = k == 0 ? "borealis" : "rainbow"; } }
                // and the text outline on the first (as "textOutline": true would)
                var outlineCard = _cards != null ? _cards.ById(fx[0]) : null;
                var hadOutline = outlineCard != null && outlineCard.TextOutline;
                if (outlineCard != null) outlineCard.TextOutline = true;
                PickACard();
                if (_cards != null && _state.Players.Length > 0)
                {
                    var offer = new List<int>();
                    foreach (var id in new[] { "ultrapuck.momentum", "ultrapuck.loyal", "ultrapuck.portalgun", "ultrapuck.stonewall" }) { var c = _cards.ById(id); if (c != null) offer.Add(c.Index); }
                    _state.Players[0].Offer = offer.ToArray();
                }
                yield return new WaitForSecondsRealtime(2.2f);
                yield return Shot("cardfx-1");
                yield return new WaitForSecondsRealtime(2.5f);
                yield return Shot("cardfx-2");
                for (var k = 0; k < fx.Length; k++) { var c = _cards != null ? _cards.ById(fx[k]) : null; if (c != null) c.Finish = was[k]; }
                if (outlineCard != null) outlineCard.TextOutline = hadOutline;
                GoMainMenu();
                yield return new WaitForSecondsRealtime(0.5f);
            if (want("chaos"))
            {
                // chaos cards: 2x Goal Size glitching in a pick (three moments, to catch a burst), in free play's card
                // editor grid, then held, with both nets twice as wide
                _local.Slots.Add(new LocalPlayers.Slot { Device = kb, Team = 0 });
                _mainMenu.Show(MainMenu.Page.None);
                StartMatch(true);
                _sim.T.CardsOffered = 3;
                yield return new WaitForSecondsRealtime(0.8f);
                PickACard();
                var wide = _cards != null ? _cards.ById("ultrapuck.widenets") : null;
                // (no picture of its own yet: borrow one for this run, so the glitch has something to tear)
                var wideArt = wide != null ? wide.Art : null;
                if (wide != null && !File.Exists(Path.Combine(Application.streamingAssetsPath, "Cards/Art/" + wide.Art + ".jpg"))) wide.Art = "ultrapuck.portalgun";
                if (wide != null && _state.Players.Length > 0)
                {
                    var offer = new List<int>();
                    foreach (var id in new[] { "ultrapuck.momentum", "ultrapuck.widenets", "ultrapuck.loyal" }) { var c = _cards.ById(id); if (c != null) offer.Add(c.Index); }
                    _state.Players[0].Offer = offer.ToArray();
                }
                yield return new WaitForSecondsRealtime(2.2f);
                yield return Shot("chaos-pick-1");
                yield return new WaitForSecondsRealtime(0.37f);
                yield return Shot("chaos-pick-2");
                yield return new WaitForSecondsRealtime(0.61f);
                yield return Shot("chaos-pick-3");
                GoMainMenu();
                yield return new WaitForSecondsRealtime(0.5f);
                _local.Slots.Add(new LocalPlayers.Slot { Device = kb, Team = 0 });
                _mainMenu.Show(MainMenu.Page.None);
                StartMatch(true);
                yield return new WaitForSecondsRealtime(0.8f);
                EditCards();
                yield return new WaitForSecondsRealtime(1.2f);
                _cardScreen.ShotGrid(3, 999);
                yield return new WaitForSecondsRealtime(1.2f);
                yield return Shot("chaos-editor");
                if (wide != null) wide.Art = wideArt;
                GoMainMenu();
                yield return new WaitForSecondsRealtime(0.5f);
                _local.Slots.Add(new LocalPlayers.Slot { Device = kb, Team = 0 });
                _mainMenu.Show(MainMenu.Page.None);
                StartMatch(true);
                yield return new WaitForSecondsRealtime(2f);
                yield return Shot("chaos-nets-before");
                if (wide != null && _state.Players.Length > 0) { _sim.GiveCard(_state, _state.Players[0], wide.Index); _cards.Recompute(_state); }
                yield return new WaitForSecondsRealtime(1.2f);
                yield return Shot("chaos-nets");
                GoMainMenu();
                yield return new WaitForSecondsRealtime(0.5f);
            }

            }

            if (want("samedraw"))
            {
                // Same Draw: you and a bot teammate get the same cards; the bot's cursor locks in (green border)
                _local.Slots.Add(new LocalPlayers.Slot { Device = kb, Team = 0, Colour = 0 });
                _local.Slots.Add(new LocalPlayers.Slot { Team = 0, Colour = 0, Demo = true });
                _local.Slots.Add(new LocalPlayers.Slot { Team = 1, Colour = 1, Demo = true });
                _mainMenu.Show(MainMenu.Page.None);
                StartMatch(true);
                _sim.T.SameDraw = true;
                yield return new WaitForSecondsRealtime(0.8f);
                _sim.StartPicking(_state, new List<int> { 0, 1 });
                yield return new WaitForSecondsRealtime(2.4f);
                yield return Shot("samedraw");
                GoMainMenu();
                yield return new WaitForSecondsRealtime(0.5f);
            }

            if (want("pausemenu"))
            {
                // a local match's pause menu and its match settings page
                _local.Slots.Add(new LocalPlayers.Slot { Device = kb, Team = 0, Colour = 0 });
                _local.Slots.Add(new LocalPlayers.Slot { Team = 1, Colour = 1, Demo = true });
                _local.AssignTeams();
                _mainMenu.Show(MainMenu.Page.None);
                StartMatch(false);
                yield return new WaitForSecondsRealtime(0.5f);
                _menus.FreePlay = false;
                _menus.OpenPause("Shot");
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Shot("pause");
                _menus.ShowMatchSettings();
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Shot("match-settings");
                _menus.ShowCardList();
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Shot("card-list");
                _menus.Close();
                GoMainMenu();
            }

            if (want("mainmenu"))
            {
                // the main menu settled: the name and the small line under it (its shadow hugging the letters)
                GoMainMenu();
                yield return new WaitForSecondsRealtime(2f);
                yield return Shot("main-menu");
            }

            if (want("teampage"))
            {
                // a local match's pause menu: Change team (under Match settings), then the match settings scrolled to the
                // bottom (Carry-through and Knock-away)
                _local.Slots.Add(new LocalPlayers.Slot { Device = kb, Team = 0, Colour = 0 });
                _local.Slots.Add(new LocalPlayers.Slot { Team = 1, Colour = 1, Demo = true });
                _local.AssignTeams();
                _mainMenu.Show(MainMenu.Page.None);
                StartMatch(false);
                yield return new WaitForSecondsRealtime(0.5f);
                _menus.FreePlay = false;
                _menus.OpenPause("Shot");
                _menus.ShotFocus("Change team");
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Shot("pause-change-team");
                _menus.OpenTeam();
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Shot("change-team");
                _menus.ShowMatchSettings();
                _menus.ShotFocus("");
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Shot("match-settings-bottom");
                _menus.Close();
                GoMainMenu();
                yield return new WaitForSecondsRealtime(0.5f);
            }

            if (want("freepause"))
            {
                // free play's pause menu: the bot count and rink with arrows either side
                _local.Slots.Add(new LocalPlayers.Slot { Device = kb, Team = 0, Colour = 0 });
                _mainMenu.Show(MainMenu.Page.None);
                StartMatch(true);
                yield return new WaitForSecondsRealtime(0.5f);
                _menus.FreePlay = true;
                _menus.OpenPause("Free play");
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Shot("freeplay-pause");
                _menus.Close();
                GoMainMenu();
                yield return new WaitForSecondsRealtime(0.5f);
            }

            if (want("catchup"))
            {
                // a local match where you've just come in part-way: two card rounds to catch up on while the bots play
                _local.Slots.Add(new LocalPlayers.Slot { Device = kb, Team = 0, Colour = 0 });
                _local.Slots.Add(new LocalPlayers.Slot { Team = 0, Colour = 0, Demo = true });
                _local.Slots.Add(new LocalPlayers.Slot { Team = 1, Colour = 1, Demo = true });
                _local.AssignTeams();
                _mainMenu.Show(MainMenu.Page.None);
                StartMatch(false);
                _sim.T.TeamVote = false; _sim.T.SameDraw = false;
                yield return new WaitForSecondsRealtime(4.5f); // past the countdown
                _state.TeamRounds[0] = 2;
                _state.Players[0].CatchingUp = true;
                yield return new WaitForSecondsRealtime(2.4f);
                yield return Shot("catchup-local-1");
                _cardScreen.ShotConfirm = true;
                yield return new WaitForSecondsRealtime(3.4f);
                yield return Shot("catchup-local-2");
                _cardScreen.ShotConfirm = true;
                yield return new WaitForSecondsRealtime(3.0f);
                yield return Shot("catchup-local-done");
                Debug.Log("Ultra Puck shots: catch-up local: cards " + _state.Players[0].Cards.Length + ", catching up " + Simulation.CatchingUp(_state, 0));
                GoMainMenu();
                yield return new WaitForSecondsRealtime(0.5f);
            }

            if (want("hostlate"))
            {
                // two copies of the game (see joinlate): this one hosts by IP with bots until a card round has been, then
                // says which side had it (a file next to the shots) for the other copy to join part-way
                _shotRules = true;
                HostIp();
                _local.Slots.Add(new LocalPlayers.Slot { Device = kb, Colour = 0, Symbol = 2, Name = "Host", Ready = true, Cursor = 0 });
                yield return new WaitForSecondsRealtime(0.5f);
                if (_session != null) _session.Bots = 3;
                yield return new WaitForSecondsRealtime(2f);
                StartOnlineMatch();
                var waited = 0f;
                while (waited < 300f && (_state == null || _session == null || !OnlinePlaying || _state.Phase == Phase.Picking || _state.TeamRounds[0] + _state.TeamRounds[1] == 0))
                { yield return new WaitForSecondsRealtime(1f); waited += 1f; }
                var team = _state != null && _state.TeamRounds[1] > _state.TeamRounds[0] ? 1 : 0;
                Debug.Log("Ultra Puck shots: host: card rounds " + (_state != null ? _state.TeamRounds[0] + "/" + _state.TeamRounds[1] : "none") + " after " + waited + " s");
                yield return Shot("host-after-round");
                File.WriteAllText(Path.Combine(ShotDir, "late-team.txt"), _session.TeamColours[team].ToString());
                var players = _state.Players.Length;
                waited = 0f;
                while (waited < 120f && (_state == null || _state.Players.Length <= players)) { yield return new WaitForSecondsRealtime(1f); waited += 1f; }
                yield return new WaitForSecondsRealtime(2f);
                yield return Shot("host-joiner-picking");
                yield return new WaitForSecondsRealtime(40f); // the other copy takes its shots
                yield return Shot("host-later");
                Debug.Log("Ultra Puck shots: host: players " + _state.Players.Length + ", last one's cards " + _state.Players[_state.Players.Length - 1].Cards.Length);
                LeaveOnline(null);
                GoMainMenu();
            }

            if (want("joinlate"))
            {
                // the other copy (see hostlate): joins the match part-way once a card round has been
                var file = Path.Combine(ShotDir, "late-team.txt");
                var waited = 0f;
                while (waited < 360f && !File.Exists(file)) { yield return new WaitForSecondsRealtime(1f); waited += 1f; }
                int colour;
                int.TryParse(File.Exists(file) ? File.ReadAllText(file).Trim() : "0", out colour);
                JoinIp("127.0.0.1");
                _local.Slots.Add(new LocalPlayers.Slot { Device = kb, Colour = colour, Symbol = 4, Name = "Late", Ready = false, Cursor = 0 });
                yield return new WaitForSecondsRealtime(2.5f);
                yield return Shot("join-lobby");
                _mainMenu.ShowOptions(true);
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Shot("join-lobby-options");
                _mainMenu.ShowOptions(false);
                _local.Slots[0].Ready = true;
                waited = 0f;
                while (waited < 60f && !OnlinePlaying) { yield return new WaitForSecondsRealtime(0.5f); waited += 0.5f; }
                yield return new WaitForSecondsRealtime(2.4f);
                yield return Shot("join-catchup-1");
                _cardScreen.ShotConfirm = true;
                yield return new WaitForSecondsRealtime(3.4f);
                yield return Shot("join-catchup-2");
                for (var k = 0; k < 6 && _state != null && _session != null && Simulation.CatchingUp(_state, _session.PlayerOf(0)); k++)
                { _cardScreen.ShotConfirm = true; yield return new WaitForSecondsRealtime(3f); }
                yield return new WaitForSecondsRealtime(1f);
                yield return Shot("join-caught-up");
                if (_state != null && _session != null)
                {
                    var me = _session.PlayerOf(0);
                    Debug.Log("Ultra Puck shots: joiner: player " + me + ", cards " + (me >= 0 ? _state.Players[me].Cards.Length : -1) + ", team rounds "
                        + _state.TeamRounds[0] + "/" + _state.TeamRounds[1] + ", catching up " + Simulation.CatchingUp(_state, me) + ", problem " + _session.Problem);
                }
                _menus.FreePlay = false;
                _menus.OpenPause("Online  ·  the match keeps going");
                _menus.ShotFocus("Change team");
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Shot("join-pause");
                _menus.ShowMatchSettings();
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Shot("join-match-settings");
                _menus.ShotFocus("");
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Shot("join-match-settings-bottom");
                _menus.OpenTeam();
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Shot("join-change-team");
                _menus.Close();
                LeaveOnline(null);
                GoMainMenu();
            }

            if (want("settings"))
            {
                // the Gameplay settings (the keyboard and controller options are at the top)
                _menus.OpenSettings(false);
                _menus.Tab = 0;
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Shot("settings-graphics");
                _menus.Tab = 1;
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Shot("settings-gameplay");
                // Camera: aim line length is here
                _menus.Tab = 2;
                yield return new WaitForSecondsRealtime(0.4f);
                yield return Shot("settings-camera");
                // the Sound settings: volume, music, sound effects
                _menus.Tab = 3;
                yield return new WaitForSecondsRealtime(0.4f);
                yield return Shot("settings-sound");
                // Controls: rebind keys and buttons
                _menus.Tab = 4;
                InputMode.ShotOverride = InputMode.Family.Keyboard;
                yield return new WaitForSecondsRealtime(0.4f);
                yield return Shot("settings-controls");
                // the same with a controller in hand: only its buttons, as pictures
                InputMode.ShotOverride = InputMode.Family.PlayStation;
                yield return new WaitForSecondsRealtime(0.3f);
                yield return Shot("settings-controls-ps");
                InputMode.ShotOverride = InputMode.Family.Xbox;
                yield return new WaitForSecondsRealtime(0.3f);
                yield return Shot("settings-controls-xbox");
                InputMode.ShotOverride = InputMode.Family.Keyboard;
                // the crosshair picker (looking only: nothing is applied)
                _menus.ShowCrosshairs();
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Shot("settings-crosshair");
                InputMode.ShotOverride = null;
                _menus.Close();
                // the same from the players screen, where there are six tabs across (Match as well)
                _menus.OpenSettings(true);
                _menus.Tab = 3;
                yield return new WaitForSecondsRealtime(0.4f);
                yield return Shot("settings-sound-lobby");
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

            if (want("rage"))
            {
                // RAGE: the meter under your own player at 5 of 7 (twitching), then raging (big and green)
                _shotWholeRink = false;
                _local.Slots.Clear();
                _local.Slots.Add(new LocalPlayers.Slot { Device = kb, Team = 0, Colour = 0, Name = "Ava" });
                _local.Slots.Add(new LocalPlayers.Slot { Team = 1, Colour = 1, Demo = true });
                _local.AssignTeams();
                _mainMenu.Show(MainMenu.Page.None);
                StartMatch(false);
                yield return null;
                _shotNoInput = true;
                _state.Players[0].Cards = new[] { _sim.Cards.ById("ultrapuck.rage").Index };
                _sim.Cards.Recompute(_state);
                while (_state.Phase == Phase.Countdown) yield return null;
                _state.Players[0].RageFill = Fix.FromInt(4); _state.Players[0].RageNeed = 5;
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Shot("rage-meter");
                _state.Players[0].RageFill = Fix.Zero; _state.Players[0].RageNeed = 0;
                _state.Players[0].RageTime = _sim.T.RageTime;
                _sim.Cards.Recompute(_state);
                yield return new WaitForSecondsRealtime(1f);
                yield return Shot("rage-on");
                _state.Players[0].RageTime = Fix.Zero; _state.Players[0].RageCooldown = Fix.FromInt(14);
                _sim.Cards.Recompute(_state);
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Shot("rage-cooldown");
                _state.Players[0].RageCooldown = Fix.Zero;
                // a knock-out through the right-hand boards: the blast in from the screen edge, then the respawn pillar
                var right = new FixVec2(_sim.Map.BoundsMax.X - Fix.One, Fix.Zero);
                _state.Players[1].Pos = right - new FixVec2(Fix.Two, Fix.Zero);
                _sim.KnockOut(_state, 1, right, FixVec2.Right);
                yield return new WaitForSecondsRealtime(0.12f);
                yield return Shot("knockout-1");
                yield return new WaitForSecondsRealtime(0.25f);
                yield return Shot("knockout-2");
                while (_state.Players[1].OutTime > Fix.Zero) yield return null;
                yield return new WaitForSecondsRealtime(0.15f);
                yield return Shot("respawn-1");
                yield return new WaitForSecondsRealtime(0.35f);
                yield return Shot("respawn-2");
                // the countdown on your own screen when it's you who's out
                _sim.KnockOut(_state, 0, new FixVec2(_sim.Map.BoundsMin.X + Fix.One, Fix.Zero), new FixVec2(-Fix.One, Fix.Zero));
                yield return new WaitForSecondsRealtime(1.4f);
                yield return Shot("knockout-countdown");
                _shotNoInput = false;
                GoMainMenu();
            }

            if (want("agent"))
            {
                // Double Agent on the Backwards Rink: the face-off groups behind the turned-round nets, Ava starting on
                // blue's side in blue under a blue player's name, then shifting back to orange and her own name
                var startMap = _mapIndex;
                var back = _mapFiles.IndexOf("backwards-rink.json");
                if (back >= 0) UseMap(back, false); // only for the pictures: not saved as the rink to play on
                _shotWholeRink = true;
                _local.Slots.Clear();
                _local.Slots.Add(new LocalPlayers.Slot { Device = kb, Team = 0, Colour = 0, Name = "Ava" });
                // people with no controller (not bots, whose tags all say "Bot"), so the borrowed name shows
                _local.Slots.Add(new LocalPlayers.Slot { Team = 0, Colour = 0, Name = "Ben" });
                _local.Slots.Add(new LocalPlayers.Slot { Team = 1, Colour = 1, Name = "Cleo" });
                _local.Slots.Add(new LocalPlayers.Slot { Team = 1, Colour = 1, Name = "Dex" });
                _local.AssignTeams();
                _mainMenu.Show(MainMenu.Page.None);
                StartMatch(false);
                yield return null;
                _shotNoInput = true;
                for (var round = 0; round < 2; round++)
                {
                    _state.Players[0].Cards = new[] { _sim.Cards.ById("ultrapuck.doubleagent").Index };
                    _sim.Cards.Recompute(_state);
                    _sim.FaceOff(_state);
                    yield return new WaitForSecondsRealtime(1f);
                    yield return Shot("agent-" + round + "-faceoff");
                }
                while (_state.Phase == Phase.Countdown) yield return null;
                yield return new WaitForSecondsRealtime(1.75f);
                yield return Shot("agent-fading");
                yield return new WaitForSecondsRealtime(1.2f);
                yield return Shot("agent-back");
                _shotNoInput = false;
                _shotWholeRink = false;
                GoMainMenu();
                UseMap(startMap, false);
            }

            if (want("clip"))
            {
                // carried puck pulled into the player against the boards (drawn on top of the body)
                _shotWholeRink = true;
                _local.Slots.Add(new LocalPlayers.Slot { Device = kb, Team = 0, Colour = 0, Name = "FancyDuck" });
                _local.AssignTeams();
                _mainMenu.Show(MainMenu.Page.None);
                StartMatch(true);
                yield return new WaitForSecondsRealtime(0.4f);
                var top = _sim.Map.BoundsMax.Y;
                _state.Players[0].Pos = new FixVec2(Fix.Zero, top - Fix.FromInt(2));
                _state.Players[0].Facing = new FixVec2(Fix.Zero, Fix.One);
                _state.Players[0].Vel = FixVec2.Zero;
                _state.Carrier = 0;
                _state.PuckPos = _state.Players[0].Pos;
                for (var t = 0; t < 120; t++)
                {
                    _sim.Step(_state, new[] { new PlayerInput { MoveY = 127 } });
                    if (_sim.PuckClippedInCarrier(_state)) break;
                }
                Debug.Log("Ultra Puck clip shot: clipped=" + _sim.PuckClippedInCarrier(_state) + " carrier=" + _state.Carrier
                    + " puck=" + _state.PuckPos + " player=" + _state.Players[0].Pos);
                yield return new WaitForSecondsRealtime(0.2f);
                yield return Shot("puck-clip");
                _shotWholeRink = false;
                GoMainMenu();
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

            if (want("stone"))
            {
                // Stone Wall: one standing, one crumbling; and a crowd round the puck (their name tags fade)
                _shotWholeRink = true;
                _local.Slots.Add(new LocalPlayers.Slot { Device = kb, Team = 0, Colour = 0 });
                _local.Slots.Add(new LocalPlayers.Slot { Team = 0, Colour = 2, Demo = true });
                _local.Slots.Add(new LocalPlayers.Slot { Team = 1, Colour = 1, Demo = true });
                _local.Slots.Add(new LocalPlayers.Slot { Team = 1, Colour = 1, Demo = true });
                _local.AssignTeams();
                _mainMenu.Show(MainMenu.Page.None);
                StartMatch(false);
                yield return new WaitForSecondsRealtime(3.5f);
                _shotNoInput = true;
                System.Func<double, double, FixVec2> at = (x, y) => new FixVec2(Fix.FromDecimal((decimal)x), Fix.FromDecimal((decimal)y));
                var stones = new List<Stone>();
                for (var k = 0; k < 10; k++)
                    stones.Add(new Stone { A = at(-14 + k * 0.8, -7 + k * 0.35), B = at(-14 + (k + 1) * 0.8, -7 + (k + 1) * 0.35), Life = Fix.FromDecimal(3.5m - k * 0.05m), Wall = 900 });
                for (var k = 0; k < 6; k++)
                    stones.Add(new Stone { A = at(-12 + k * 0.8, 6), B = at(-12 + (k + 1) * 0.8, 6), Life = Fix.FromDecimal(0.38m - k * 0.01m), Wall = 901 });
                _state.Stones = stones.ToArray();
                _state.StoneWalls = 901;
                // the crowd
                var spots = new[] { at(6, 0.6), at(4.9, 2.4), at(7.2, 2.6), at(6.1, 3.9) };
                for (var j = 0; j < _state.Players.Length && j < spots.Length; j++) { _state.Players[j].Pos = spots[j]; _state.Players[j].Vel = FixVec2.Zero; }
                _state.Carrier = -1;
                _state.PuckPos = at(6, 2.3);
                _state.PuckVel = FixVec2.Zero;
                // sped up (orange arrows) and slowed (black arrows)
                if (_state.Players.Length > 2) { _state.Players[1].Fast = Fix.Ratio(6, 10); _state.Players[1].FastTime = Fix.FromInt(9); _state.Players[2].Slow = Fix.Ratio(1, 10); _state.Players[2].SlowTime = Fix.FromInt(9); }
                yield return new WaitForSecondsRealtime(0.15f);
                yield return Shot("stone-1");
                yield return new WaitForSecondsRealtime(0.3f);
                yield return Shot("stone-2");
                _shotNoInput = false;
                _shotWholeRink = false;
                GoMainMenu();
            }

            if (want("browse"))
            {
                // the main menu's card list, with some made-up pick counts from two other players (not saved)
                var fake = new CardStatsTable();
                var names = new[] { "Alex", "Sam" };
                for (var who = 0; who < names.Length; who++)
                {
                    var person = new CardStatsTable.Person { Name = names[who] };
                    for (var c = 0; c < _cards.Cards.Count; c++) if ((c + who) % 3 != 0) person.Picks[_cards.Cards[c].Id] = 1 + (c * 7 + who * 3) % 9;
                    fake.People["test:" + who] = person;
                }
                CardStats.In(fake.ToBytes());
                OpenCards();
                yield return new WaitForSecondsRealtime(2f);
                yield return Shot("card-stats");
                _cardScreen.CloseBrowser();
                yield return new WaitForSecondsRealtime(1.5f);
            }

            if (want("slots"))
            {
                // Slot Machine reels: one spinning, one stopping, one landed on a win, one landed on nothing
                _shotWholeRink = true;
                _local.Slots.Add(new LocalPlayers.Slot { Device = kb, Team = 0, Colour = 0 });
                _local.Slots.Add(new LocalPlayers.Slot { Team = 0, Colour = 0, Demo = true });
                _local.Slots.Add(new LocalPlayers.Slot { Team = 1, Colour = 1, Demo = true });
                _local.Slots.Add(new LocalPlayers.Slot { Team = 1, Colour = 1, Demo = true });
                _local.AssignTeams();
                _mainMenu.Show(MainMenu.Page.None);
                StartMatch(false);
                yield return new WaitForSecondsRealtime(3.5f);
                _shotNoInput = true;
                var sm = _sim.Cards.ById("ultrapuck.slotmachine").Index;
                System.Func<double, double, FixVec2> at = (x, y) => new FixVec2(Fix.FromDecimal((decimal)x), Fix.FromDecimal((decimal)y));
                var spots = new[] { at(-12, 4), at(-4, 4), at(4, 4), at(12, 4) };
                for (var j = 0; j < _state.Players.Length && j < 4; j++)
                {
                    var p = _state.Players[j];
                    p.Cards = new[] { sm };
                    p.Pos = spots[j]; p.Vel = FixVec2.Zero;
                }
                _sim.Cards.Recompute(_state);
                var spin = _sim.T.SlotSpinTime;
                // spinning, nearly stopped (two reels landed), a win on Speed Up, three different (a loss)
                int[] reels = { 1 | (5 << 4) | (7 << 8), 2 | (2 << 4) | (6 << 8), 1 | (1 << 4) | (1 << 8), 3 | (8 << 4) | (4 << 8) };
                Fix[] left = { spin * Fix.Ratio(9, 10), spin * Fix.Ratio(15, 100), Fix.Ratio(1, 120), Fix.Ratio(1, 120) };
                int[] won = { 0, 0, 1, 0 };
                for (var j = 0; j < _state.Players.Length && j < 4; j++)
                {
                    var p = _state.Players[j];
                    p.SlotReels = reels[j]; p.SlotSpin = left[j]; p.SlotWin = won[j]; p.SlotSpins++;
                }
                yield return new WaitForSecondsRealtime(0.25f);
                yield return Shot("slots-1");
                yield return new WaitForSecondsRealtime(0.4f);
                yield return Shot("slots-2");
                _shotNoInput = false;
                _shotWholeRink = false;
                GoMainMenu();
            }

            if (want("swaptrail"))
            {
                _shotWholeRink = true;
                _local.Slots.Add(new LocalPlayers.Slot { Device = kb, Team = 0, Colour = 0 });
                _local.Slots.Add(new LocalPlayers.Slot { Team = 0, Colour = 0, Demo = true });
                _local.AssignTeams();
                _mainMenu.Show(MainMenu.Page.None);
                StartMatch(true);
                yield return new WaitForSecondsRealtime(0.6f);
                _shotNoInput = true;
                _state.Players[0].Pos = new FixVec2(Fix.FromInt(-12), Fix.FromInt(-4));
                _state.Players[1].Pos = new FixVec2(Fix.FromInt(10), Fix.FromInt(5));
                yield return new WaitForSecondsRealtime(0.2f);
                _effects.SwapTrail(_players[0].Root.transform.localPosition, _players[1].Root.transform, Palette.Team(0));
                yield return new WaitForSecondsRealtime(0.18f);
                yield return Shot("swaptrail-1");
                yield return new WaitForSecondsRealtime(0.3f);
                yield return Shot("swaptrail-2");
                _shotNoInput = false;
                _shotWholeRink = false;
                GoMainMenu();
            }

            if (want("portal"))
            {
                // the face-off groups in front of the nets; then two portals (one on a curved corner, one on the back of
                // a net) and the puck going through
                _shotWholeRink = true;
                _local.Slots.Add(new LocalPlayers.Slot { Device = kb, Team = 0, Colour = 0 });
                _local.Slots.Add(new LocalPlayers.Slot { Team = 0, Colour = 0, Demo = true });
                _local.Slots.Add(new LocalPlayers.Slot { Team = 0, Colour = 0, Demo = true });
                _local.Slots.Add(new LocalPlayers.Slot { Team = 1, Colour = 1, Demo = true });
                _local.Slots.Add(new LocalPlayers.Slot { Team = 1, Colour = 1, Demo = true });
                _local.Slots.Add(new LocalPlayers.Slot { Team = 1, Colour = 1, Demo = true });
                _local.AssignTeams();
                _mainMenu.Show(MainMenu.Page.None);
                StartMatch(false);
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Shot("faceoff");
                yield return new WaitForSecondsRealtime(3f);
                _shotNoInput = true;
                System.Func<double, double, FixVec2> at = (x, y) => new FixVec2(Fix.FromDecimal((decimal)x), Fix.FromDecimal((decimal)y));
                var mn = _sim.Map.BoundsMin; var mx = _sim.Map.BoundsMax;
                var corner = new FixVec2(mn.X + Fix.FromInt(3), mx.Y - Fix.FromInt(3));
                var from0 = at(-14, 2);
                var behind = Fix.Zero;
                foreach (var g in _sim.Map.Goals) if (g.Team == 1) behind = g.Max.X + Fix.FromInt(3);
                _state.PortalShots = new[]
                {
                    new Shot { Pos = from0, Vel = (corner - from0).Normalized * _sim.T.PortalShotSpeed, Owner = 0, Life = Fix.FromInt(3), Kind = 0 },
                    new Shot { Pos = new FixVec2(behind, Fix.Zero), Vel = new FixVec2(-_sim.T.PortalShotSpeed, Fix.Zero), Owner = 0, Life = Fix.FromInt(3), Kind = 1 },
                };
                yield return new WaitForSecondsRealtime(0.8f);
                Debug.Log("Ultra Puck portal shot: portals=" + _state.Portals.Length);
                if (_state.Portals.Length > 1)
                {
                    // slowly, so the shots catch them half way: the puck into one portal, a player into the other
                    var wh = _sim.T.WallThickness * Fix.Half;
                    var po = _state.Portals[0];
                    _state.Carrier = -1;
                    _state.PuckPos = po.Pos + po.Normal * (wh + Fix.Ratio(45, 100));
                    _state.PuckVel = -po.Normal * Fix.FromInt(3);
                    var pq = _state.Portals[1];
                    var who = _state.Players.Length - 1;
                    _state.Players[who].Pos = pq.Pos + pq.Normal * (wh + Fix.Ratio(7, 10));
                    _state.Players[who].Vel = -pq.Normal * Fix.FromInt(3);
                }
                yield return new WaitForSecondsRealtime(0.2f);
                yield return Shot("portal-1");
                yield return new WaitForSecondsRealtime(0.15f);
                yield return Shot("portal-2");
                _shotNoInput = false;
                _shotWholeRink = false;
                GoMainMenu();
            }

            if (want("carry"))
            {
                // Friendly Fire: orange carrying a teammate who has the puck, then the throw; blue's pylon goalie with
                // three cards (55% bigger each) in front of its net
                _shotWholeRink = true;
                _local.Slots.Add(new LocalPlayers.Slot { Device = kb, Team = 0, Colour = 0 });
                _local.Slots.Add(new LocalPlayers.Slot { Team = 0, Colour = 2, Demo = true });
                _local.Slots.Add(new LocalPlayers.Slot { Team = 1, Colour = 1, Demo = true });
                _local.AssignTeams();
                _mainMenu.Show(MainMenu.Page.None);
                StartMatch(false);
                yield return new WaitForSecondsRealtime(3.5f);
                Debug.Log("Ultra Puck carry shot: cards " + (_sim.Cards != null));
                int ff = _sim.Cards.ById("ultrapuck.friendlyfire").Index, pg = _sim.Cards.ById("ultrapuck.pylongoalie").Index;
                // whoever ended up on player 0's team is the one carried; the other side gets the pylon cards
                int mate = -1, foe = -1;
                for (var j = 1; j < _state.Players.Length; j++)
                    if (_state.Players[j].Team == _state.Players[0].Team) { if (mate < 0) mate = j; } else if (foe < 0) foe = j;
                if (mate < 0 || foe < 0) { Debug.Log("Ultra Puck carry shot: teams didn't come out 2v1"); mate = 1; foe = 2; }
                _state.Players[0].Cards = new[] { ff };
                _state.Players[foe].Cards = new[] { pg, pg, pg };
                _sim.Cards.Recompute(_state);
                _sim.PlacePylons(_state);
                _state.Players[0].Pos = new FixVec2(Fix.FromInt(-4), Fix.FromInt(-3));
                _state.Players[0].Facing = FixVec2.Right;
                _state.Players[0].Vel = FixVec2.Zero;
                _state.Players[mate].HeldBy = 0;
                _state.Players[foe].Pos = new FixVec2(Fix.FromInt(-12), Fix.FromInt(8));
                _state.Carrier = mate;
                _state.PuckTeam = _state.Players[0].Team;
                _seenCarrier = mate; // (no handing the human over to the carrier)
                _shotNoInput = true;
                _state.PuckPos = _state.Players[0].Pos + FixVec2.Right * Fix.Two;
                Debug.Log("Ultra Puck carry shot: pylons=" + _state.Pylons.Length + (_state.Pylons.Length > 0 ? " radius=" + _state.Pylons[0].Radius : ""));
                yield return new WaitForSecondsRealtime(0.2f);
                yield return new WaitForSecondsRealtime(0.1f);
                yield return Shot("carry-1");
                // the throw: a half charge let go
                _state.Players[0].Charge = Fix.Half;
                _sim.Step(_state, new PlayerInput[_state.Players.Length]);
                Debug.Log("Ultra Puck carry shot: held by " + _state.Players[mate].HeldBy + ", toss " + _state.Players[mate].TossTime + ", carrier " + _state.Carrier + " (mate " + mate + "), vel " + _state.Players[mate].Vel);
                yield return new WaitForSecondsRealtime(0.25f);
                yield return Shot("carry-2");
                _shotNoInput = false;
                _shotWholeRink = false;
                GoMainMenu();
            }

            if (want("dash"))
            {
                // dashing up while looking right (Dash direction: Where you move): the streaks trail below, not to the left
                _shotWholeRink = true;
                _local.Slots.Add(new LocalPlayers.Slot { Device = kb, Team = 0, Colour = 0 });
                _local.AssignTeams();
                _mainMenu.Show(MainMenu.Page.None);
                StartMatch(true);
                yield return new WaitForSecondsRealtime(0.6f);
                _shotNoInput = true;
                var me = _state.Players[0];
                me.Pos = FixVec2.Zero;
                me.Facing = FixVec2.Right;
                me.Vel = new FixVec2(Fix.Zero, Fix.FromInt(3));
                me.DashDir = new FixVec2(Fix.Zero, Fix.One);
                me.DashTime = _sim.T.DashHitTime * Fix.Ratio(3, 10);
                me.DashCoast = Fix.Zero;
                yield return null;
                yield return Shot("dash");
                _shotNoInput = false;
                _shotWholeRink = false;
                GoMainMenu();
            }

            Debug.Log("Ultra Puck: screenshots done in " + ShotDir);
            Application.Quit();
        }

        private int _videoFrame;

        /// <summary>Screenshot runs: <paramref name="count"/> frames in a row, as JPGs numbered on from the last (for a video).</summary>
        private System.Collections.IEnumerator VideoFrames(string dir, int count)
        {
            for (var k = 0; k < count; k++)
            {
                yield return new WaitForEndOfFrame();
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(dir, "f" + _videoFrame.ToString("D5") + ".jpg"), tex.EncodeToJPG(90));
                Destroy(tex);
                _videoFrame++;
            }
        }

        private System.Collections.IEnumerator Shot(string name)
        {
            // nobody can hear a screenshot run: the log says which sounds each scene set off, and what the music is doing
            Debug.Log("Ultra Puck sound at " + name + ": " + Sound.Report());
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
            var goalHero = GoalZoomHero();
            if (goalHero)
            {
                // ease onto the scorer (not a hard cut, not a close-up) and keep following them
                var at = V.Of3(_players[_state.LastGoalPlayer].Root.transform.position);
                // swallowed by their own Black Hole: watch the hole they went into, not where their body was
                var scorer = _state.Players[_state.LastGoalPlayer];
                if (scorer.Swallowed && scorer.OutTime > Fix.Zero) at = V.Of(scorer.OutFrom);
                var pad = 5.5f;
                lo = at - Vector2.one * pad;
                hi = at + Vector2.one * pad;
                minHalfHeight = Mathf.Min(minHalfHeight, 6.8f);
            }
            else if (_mode == Mode.Replay && _viewer.Following >= 0 && _viewer.Following < _players.Count && _players[_viewer.Following].Root != null)
            {
                // a replay's camera on one player
                var at = V.Of3(_players[_viewer.Following].Root.transform.position);
                lo = at - Vector2.one * 6f;
                hi = at + Vector2.one * 6f;
                minHalfHeight = Mathf.Min(minHalfHeight, 7.5f);
            }
            else if (follow && _players.Count > 0)
            {
                lo = hi = V.Of3(_puck.Root.transform.position);
                foreach (var p in _players)
                {
                    if (p.Out) continue; // knocked out of the rink: the camera stays on the ice
                    var at = V.Of3(p.Root.transform.position);
                    lo = Vector2.Min(lo, at); hi = Vector2.Max(hi, at);
                }
                lo -= Vector2.one * padding; hi += Vector2.one * padding;
            }
            var normalSize = Mathf.Max((hi.y - lo.y) / 2f / keep, (hi.x - lo.x) / 2f / aspect, minHalfHeight);

            // Charge Zoom: while someone on this PC charges a shot, pull back until the goal they're attacking is in view
            var goal = Vector2.zero; var charge = 0f;
            var zooming = follow && !goalHero && Settings.ChargeZoom && LocalCharge(out goal, out charge);
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
            if (goalHero)
            {
                // stay locked on the scorer — never jump the view to rink centre
                if (halfW * 2f < mapMax.x - mapMin.x) c.x = Mathf.Clamp(c.x, mapMin.x + halfW, mapMax.x - halfW);
                if (halfH * 2f < mapMax.y - mapMin.y) c.y = Mathf.Clamp(c.y, mapMin.y + halfH, mapMax.y - halfH);
            }
            else
            {
                c.x = halfW * 2f >= mapMax.x - mapMin.x ? (mapMin.x + mapMax.x) / 2f : Mathf.Clamp(c.x, mapMin.x + halfW, mapMax.x - halfW);
                c.y = halfH * 2f >= mapMax.y - mapMin.y ? (mapMin.y + mapMax.y) / 2f : Mathf.Clamp(c.y, mapMin.y + halfH, mapMax.y - halfH);
            }
            var pos = new Vector2(c.x, c.y + size * ScoreBarShare); // shift up so the score bar sits over empty space
            pos += _shotNudge;

            if (snap) { _camPos = pos; _camSize = size; _camVel = Vector2.zero; _camSizeVel = 0f; }
            else
            {
                var smooth = Mathf.Lerp(0.12f, 0.8f, Settings.CameraSmoothing);
                if (zooming) smooth *= 0.5f; // pulling back for a shot shouldn't lag behind it
                if (goalHero) smooth = Mathf.Max(smooth, 0.45f); // ease in on the scorer (no hard cut)
                _camPos = Vector2.SmoothDamp(_camPos, pos, ref _camVel, smooth, Mathf.Infinity, Clock.Dt);
                _camSize = Mathf.SmoothDamp(_camSize, size, ref _camSizeVel, smooth * 1.3f, Mathf.Infinity, Clock.Dt);
            }

            // screen shake after hits: a quick random jolt that dies away (scaled with the zoom, so it feels the same)
            _shake = Mathf.MoveTowards(_shake * Mathf.Exp(-Clock.Dt * 9f), 0f, Clock.Dt * 0.05f);
            var amount = Settings.ShakeAmount;
            var jolt = _shake > 0.001f && amount > 0.01f ? Random.insideUnitCircle * _shake * amount * (_camSize / 12f) : Vector2.zero;
            _cam.transform.position = new Vector3(_camPos.x + jolt.x, _camPos.y + jolt.y, -10f);
            _cam.orthographicSize = _camSize;
            Shader.SetGlobalVector("_UltraPuckCam", new Vector4(_camPos.x, _camPos.y, 0f, 0f)); // the ice's reflection moves with the view
        }

        /// <summary>Live celebration before the replay wipe: frame the player who scored.</summary>
        private bool GoalZoomHero()
        {
            if (_state == null || _puck == null || _puck.Root == null) return false;
            if (_state.Phase != Phase.Scored) return false;
            if (_replay != null || _wipe.Active || _replayExit) return false;
            if (Clock.Now >= _goalZoomUntil) return false;
            var i = _state.LastGoalPlayer;
            return i >= 0 && i < _players.Count && _players[i].Root != null;
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
            WireMouseAim(playing);
            if (_mode == Mode.Editor)
            {
                // the rink maker: it runs the screen (and the camera) itself
                _maker.Tick();
                if (_mode == Mode.Editor)
                {
                    _maker.Frame(_cam);
                    _hud.enabled = false;
                    Cursor.visible = true;
                    Sound.Music(false, "");
                    _cardScreen.Frame(null);
                    return;
                }
            }
            _hud.enabled = true;
            if (_mode == Mode.Online) OnlineUpdate();
            else if (_menus.IsOpen) _menus.Tick(_local.Menu());
            else if (_mode == Mode.Menu)
            {
                if (_maker.Current == RinkMaker.State.Library) _maker.Tick(); // the Custom Rinks screen
                else _mainMenu.Tick();
                // only if the menu is still up: a button may just have started free play, and running the menu's
                // match now would put its bots (and its card-less rules) back in place of the new game
                if (_mode == Mode.Menu) AttractUpdate();
            }
            else if (_mode == Mode.Lobby) LobbyUpdate();
            else if (_mode == Mode.Replay) ReplayUpdate();
            else MatchUpdate();
            if (_mode == Mode.Menu && _menus.IsOpen) AttractUpdate();
            if (_menus.IsOpen && _mode == Mode.Match) Show(_state, _state, 0f); // paused: hold the picture still
            UpdateCamera(_mode == Mode.Replay ? _viewer.CamMode != 1 : playing && Settings.FollowCamera && !_shotWholeRink);
            for (var k = _ripples.Count - 1; k >= 0; k--)
            {
                if (Clock.Now < _ripples[k].When) continue;
                var r = _ripples[k];
                _effects.Ripple(r.At, r.Colour, r.Size, r.Life);
                _ripples.RemoveAt(k);
            }
            DashPreview();
            _effects.Update();
            _styleFx.Update();
            if (ShotDir != null) CardStats.NoSave = true; // screenshot runs leave the player's card stats alone
            NotePicks();
            CardStats.Tick();
            // free play: the d-pad runs the puck drills (not with a menu or the cards up)
            LocalPlayers.PuckDrills = _free && _state != null && !_menus.IsOpen && !_cardScreen.Showing;
            _cardScreen.Sim = _sim;
            HostEndPick(playing);
            _cardScreen.Frame(playing ? _state : null);
            // the theme plays through the menus and lobbies; a match plays its own songs, one after another (see Sound)
            // (a rink's own song: first in a match there, and the theme while it's the main menu's rink)
            Sound.Music(InMatch, _map != null && (InMatch || _mode == Mode.Menu) ? _map.Music : "");
            Sound.QuietMusic = _menus.IsOpen; // pause menu (and settings from it): music at half volume
            Sound.HearingMusic = _menus.HearingMusic; // unless you're on the Music slider
            _hud.Show(false, _free, _local, _state, _sim, _error, _menus.IsOpen || _cardScreen.Showing || (!playing && _mode != Mode.Replay));
            _hud.ReplaySave = _finished == null || _mode == Mode.Replay ? 0 : _finishedSaved ? 2 : 1;
            // hold View / Tab in a match: the scoreboard (cards and stats)
            _hud.Board = InMatch && !_free && !_menus.IsOpen && !_cardScreen.Showing && !Menus.Typing && (_local.SettingsHeld() || _shotBoard);
            _hud.Banner = _mode == Mode.Online && _session != null && OnlinePlaying ? OnlineBanner() : null;
        }

        /// <summary>Mouse aiming for keyboard players, and a reticle instead of the system cursor in a match.</summary>
        private void WireMouseAim(bool playing)
        {
            var menus = _menus.IsOpen;
            var cards = _cardScreen.Showing;
            var open = menus || cards;
            _local.AllowMouseButtons = playing && !open && !LocalPlayers.Blocked;
            _local.MouseWorld = () =>
            {
                if (_cam == null) return null;
                var m = UnityEngine.InputSystem.Mouse.current;
                if (m == null) return null;
                var sp = m.position.ReadValue();
                // distance from the camera to the rink plane (z = 0)
                var w = _cam.ScreenToWorldPoint(new Vector3(sp.x, sp.y, Mathf.Abs(_cam.transform.position.z)));
                return new Vector2(w.x, w.y);
            };
            // local slot → match player (online seats aren't in the same order as this PC's slots)
            _local.PlayerWorld = i =>
            {
                var p = i;
                if (_mode == Mode.Online && _session != null)
                {
                    p = _session.PlayerOf(i);
                    if (p < 0) return Vector2.zero;
                }
                if (_state == null || p < 0 || p >= _state.Players.Length) return Vector2.zero;
                var steer = Simulation.Steers(_state, p); // swapped onto a bot teammate: aim from them
                if (steer >= 0) p = steer;
                return V.Of(_state.Players[p].Pos);
            };
            // controller pre-charge auto-aim: the way to a scoreable spot in the net this slot's skater attacks
            _local.NetAim = i =>
            {
                var p = i;
                if (_mode == Mode.Online && _session != null) p = _session.PlayerOf(i);
                if (_state == null || _sim == null || p < 0 || p >= _state.Players.Length || _state.Phase != Phase.Play) return null;
                var steer = Simulation.Steers(_state, p);
                if (steer >= 0) p = steer;
                if (_state.Carrier == p) return null;
                var me = V.Of(_state.Players[p].Pos);
                var team = _state.Players[p].Team;
                UltraPuck.Core.GoalDef net = null;
                var near = float.MaxValue;
                foreach (var g in _sim.Map.Goals)
                {
                    if (g.Team == team) continue; // our own net
                    var d = ((V.Of(g.Min) + V.Of(g.Max)) * 0.5f - me).sqrMagnitude;
                    if (d < near) { near = d; net = g; }
                }
                if (net == null) return null;
                var at = ScoreableSpot(net, me, _sim.T.PuckRadius.ToFloat());
                return (at - me).sqrMagnitude < 0.0001f ? (Vector2?)null : (at - me).normalized;
            };
            // reticle in a match (KBM); system cursor for menus and the card pick
            var reticle = playing && !open && !InputMode.Pad && _local.AnyKeyboardHuman();
            _hud.Reticle = reticle;
            if (menus || cards) Cursor.visible = true;
            else if (reticle) Cursor.visible = false;
        }

        private void OnApplicationQuit()
        {
            CardStats.Save();
            if (_session != null) _session.Dispose();
            SteamNet.Shutdown();
        }

        // ------------------------------------------------------------------ main menu

        /// <summary>Bots playing a match behind the main menu.</summary>
        private void AttractUpdate()
        {
            var dt = 1f / _attractSim.T.TicksPerSecond;
            _attractAcc += Mathf.Min(Clock.Dt, 0.25f);
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
            DropRecording();
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
            PlayersRink();
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
            PlayersRink();
            _local.Slots.Clear(); // no bots to start with: add them from the pause menu when you want them
            _local.JoinDevice(device ?? (UnityEngine.InputSystem.InputDevice)UnityEngine.InputSystem.Keyboard.current);
            StartMatch(true);
        }

        public void OpenSettings() { _menus.OpenSettings(true); }

        /// <summary>The main menu's card list (with how often each is picked online).</summary>
        public void OpenCards()
        {
            if (_cards == null || _cards.Cards.Count == 0 || _cardScreen.Showing) { Debug.Log("Ultra Puck: card list not opened (cards " + (_cards != null) + ", card screen busy " + _cardScreen.Showing + ")"); return; }
            _mainMenu.Show(MainMenu.Page.None);
            _cardScreen.OnBrowseClosed = () => _mainMenu.Show(MainMenu.Page.Main);
            _cardScreen.OpenBrowser(_cards);
        }

        private float _picksAt;

        /// <summary>Online: the cards this PC's people hold, noted for the card stats (once a second, in play).</summary>
        private void NotePicks()
        {
            if (_mode != Mode.Online || _session == null || _state == null || _sim == null || _sim.Cards == null || _state.Phase != Phase.Play) return;
            if (Clock.Now < _picksAt) return;
            _picksAt = Clock.Now + 1f;
            var held = new Dictionary<string, int>();
            for (var i = 0; i < _state.Players.Length && i < _session.Seats.Count; i++)
            {
                var seat = _session.Seats[i];
                if (seat.Bot || seat.Peer != _session.MyPeer || _state.Players[i].Bot) continue;
                foreach (var c in _state.Players[i].Cards)
                {
                    if (c < 0 || c >= _sim.Cards.Cards.Count) continue;
                    var id = _sim.Cards.Cards[c].Id;
                    int n;
                    held.TryGetValue(id, out n);
                    held[id] = n + 1;
                }
            }
            if (held.Count > 0) CardStats.SeeMatch(_session.MatchId + ":" + _session.Seed, held);
        }

        // ------------------------------------------------------------------ online

        private bool OnlinePlaying { get { return _session != null && _session.Current == NetSession.Stage.Playing && _session.Match != null; } }

        private Simulation MakeOnlineSim()
        {
            // the host's choice of rink (everyone has the same list: the fingerprint check makes sure; custom rinks aren't on it)
            if (_session != null && _session.Map != _mapIndex && _session.Map < _builtinMaps) UseMap(_session.Map);
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
        public string MapName(int i)
        {
            if (i < 0 || i >= _maps.Count) return "?";
            var name = string.IsNullOrEmpty(_maps[i].Name) ? _mapFiles[i] : _maps[i].Name;
            return string.IsNullOrEmpty(_maps[i].Variant) ? name : name + " (" + _maps[i].Variant + ")";
        }

        /// <summary>A rink's name without its look ("Hockey Rink"), and the look's ("Underwater", or "Classic").</summary>
        public string RinkName(int i) { return i >= 0 && i < _maps.Count ? (string.IsNullOrEmpty(_maps[i].Name) ? _mapFiles[i] : _maps[i].Name) : "?"; }
        public string LookName(int i) { return i >= 0 && i < _maps.Count && !string.IsNullOrEmpty(_maps[i].Variant) ? _maps[i].Variant : "Classic"; }

        /// <summary>The map file each map is a look of (its own for an ordinary map).</summary>
        private readonly List<string> _mapBase = new List<string>();

        /// <summary>Rink <paramref name="i"/>'s looks (the plain one first, then its variants), as map indexes.</summary>
        public List<int> Looks(int i)
        {
            var list = new List<int>();
            if (i < 0 || i >= _maps.Count) return list;
            for (var k = 0; k < _maps.Count; k++) if (_mapBase[k] == _mapBase[i]) list.Add(k);
            return list;
        }

        /// <summary>The plain rinks (no variants), as map indexes, in order.</summary>
        public List<int> Rinks()
        {
            var list = new List<int>();
            for (var k = 0; k < _builtinMaps; k++) if (string.IsNullOrEmpty(_maps[k].Variant)) list.Add(k); // (the game's own: custom rinks have their own screen)
            return list;
        }

        /// <summary>The rink screen: the next / previous rink (its plain look).</summary>
        public void StepRink(int d)
        {
            var rinks = Rinks();
            if (rinks.Count == 0) return;
            var here = rinks.IndexOf(Looks(_mapIndex).Count > 0 ? Looks(_mapIndex)[0] : _mapIndex);
            UseMap(rinks[((Mathf.Max(0, here) + d) % rinks.Count + rinks.Count) % rinks.Count]);
        }

        /// <summary>The rink screen: the next / previous look of this rink (its own looks, then its custom arenas).</summary>
        public void StepLook(int d)
        {
            var n = LookCount();
            if (n < 2) return;
            var to = ((LookHere() + d) % n + n) % n;
            var looks = Looks(_mapIndex);
            var arenas = ArenasFor(_mapIndex);
            if (to < looks.Count) { ChooseArena(_mapIndex, null); UseMap(looks[to]); }
            else { var plain = looks.Count > 0 ? looks[0] : _mapIndex; ChooseArena(plain, arenas[to - looks.Count]); UseMap(plain); }
        }

        /// <summary>Screenshots: a made-up arena for <paramref name="map"/> as a zip (a striped background, a mown-grass
        /// floor over the whole canvas (only the rink's part shows), a dark band and pillars in front along the edges, a
        /// soft shadow under them).</summary>
        private static byte[] TestArenaZip(MapDef map)
        {
            var at = ArenaSkin.Canvas(map);
            const float ppu = 16f;
            int w = Mathf.RoundToInt(at.width * ppu), h = Mathf.RoundToInt(at.height * ppu);
            var back = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var front = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var shade = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var floor = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var bp = new Color32[w * h]; var fp = new Color32[w * h]; var sp = new Color32[w * h]; var gp = new Color32[w * h];
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                {
                    var i = y * w + x;
                    float u = x / (float)w, v = y / (float)h;
                    var stripe = ((x + y) / 24) % 2 == 0 ? 0.06f : 0f;
                    bp[i] = new Color(0.1f + stripe, 0.32f + 0.2f * v + stripe, 0.18f + stripe, 1f);
                    gp[i] = (int)(u * 14f) % 2 == 0 ? new Color(0.36f, 0.62f, 0.26f, 1f) : new Color(0.3f, 0.55f, 0.22f, 1f);
                    // in front: a dark band along the bottom and a pillar in each top corner
                    var band = v < 0.16f;
                    var pillar = v > 0.55f && (u < 0.13f || u > 0.87f);
                    fp[i] = band || pillar ? new Color(0.05f, 0.05f, 0.08f, 1f) : new Color(0f, 0f, 0f, 0f);
                    // its shadow, falling just above the band
                    var d = Mathf.Clamp01((v - 0.16f) / 0.06f);
                    sp[i] = v >= 0.16f && v < 0.22f ? new Color(0f, 0f, 0f, 0.45f * (1f - d)) : new Color(0f, 0f, 0f, 0f);
                }
            back.SetPixels32(bp); front.SetPixels32(fp); shade.SetPixels32(sp); floor.SetPixels32(gp);
            var files = new[] { new KeyValuePair<string, byte[]>("Background.png", back.EncodeToPNG()), new KeyValuePair<string, byte[]>("Foreground.png", front.EncodeToPNG()),
                new KeyValuePair<string, byte[]>("Shadows.png", shade.EncodeToPNG()), new KeyValuePair<string, byte[]>("Floor.png", floor.EncodeToPNG()), new KeyValuePair<string, byte[]>("arena.json", System.Text.Encoding.UTF8.GetBytes("{ \"name\": \"Test Arena\", \"rink\": \"hockey-rink\" }")) };
            Destroy(back); Destroy(front); Destroy(shade); Destroy(floor);
            using (var m = new MemoryStream())
            {
                using (var zip = new System.IO.Compression.ZipArchive(m, System.IO.Compression.ZipArchiveMode.Create, true))
                    foreach (var f in files)
                        using (var s = zip.CreateEntry(f.Key).Open()) s.Write(f.Value, 0, f.Value.Length);
                return m.ToArray();
            }
        }

        /// <summary>How many looks this rink has (its own looks and its custom arenas), which one is on, and each one's name.</summary>
        public int LookCount() { return Looks(_mapIndex).Count + ArenasFor(_mapIndex).Count; }
        public int LookHere()
        {
            var looks = Looks(_mapIndex);
            var a = ChosenArena(_mapIndex);
            if (a != null) return looks.Count + ArenasFor(_mapIndex).IndexOf(a);
            return Mathf.Max(0, looks.IndexOf(_mapIndex));
        }
        public string LookLabel(int k)
        {
            var looks = Looks(_mapIndex);
            if (k < looks.Count) return LookName(looks[k]);
            var arenas = ArenasFor(_mapIndex);
            return k - looks.Count < arenas.Count ? arenas[k - looks.Count].Name : "?";
        }

        // ------------------------------------------------------------------ custom rinks (the rink maker)

        private RinkMaker _maker;
        /// <summary>How many maps came with the game: the custom rinks (and a rink being tested, last) come after them.</summary>
        private int _builtinMaps;
        /// <summary>The last map is a rink from the maker being tested.</summary>
        private bool _testMapLoaded;
        /// <summary>Playing a rink from the maker to test it (the menu button goes back to the maker).</summary>
        private bool _testing;
        /// <summary>The main menu's rink before the maker opened, to go back to.</summary>
        private int _beforeMaker = -1;
        private const string CustomPrefix = "custom:", TestMapFile = "rink-maker-test";

        public float WallThickness { get { return _matchSim.T.WallThickness.ToFloat(); } }
        private int PlayableMaps { get { return _maps.Count - (_testMapLoaded ? 1 : 0); } }
        /// <summary>The rink chosen is one from the maker.</summary>
        public bool MapIsCustom { get { return _mapIndex >= _builtinMaps; } }

        /// <summary>The finished rinks in Custom_Rinks as maps, after the game's own (any there before, and a test map, dropped).</summary>
        private void LoadCustomMaps()
        {
            while (_maps.Count > _builtinMaps)
            {
                var k = _maps.Count - 1;
                _maps.RemoveAt(k); _mapFiles.RemoveAt(k); _mapBase.RemoveAt(k);
                Texture2D old;
                if (_previews.TryGetValue(k, out old)) { if (old != null) Destroy(old); _previews.Remove(k); }
            }
            _testMapLoaded = false;
            if (!RinkMakerOn) return; // (not in releases yet)
            foreach (var e in CustomRinks.Load())
            {
                if (e.Map == null) continue;
                _maps.Add(e.Map); _mapFiles.Add(CustomPrefix + e.File); _mapBase.Add(CustomPrefix + e.File);
            }
        }

        /// <summary>The custom rinks read again (one was saved or deleted), and the rink on now rebuilt.</summary>
        private void ReloadCustomRinks(int show)
        {
            var file = show >= 0 && show < _mapFiles.Count ? _mapFiles[show] : null;
            LoadCustomMaps();
            var at = file != null ? _mapFiles.IndexOf(file) : -1;
            ForceMap(at >= 0 ? at : 0);
        }

        /// <summary>UseMap, even if it's the same index (the map there may have changed).</summary>
        private void ForceMap(int index)
        {
            if (_rink != null) { Destroy(_rink.Root); _rink = null; }
            UseMap(index, false);
        }

        /// <summary>The rink, the players and the arena on or off (the maker draws its own rink).</summary>
        private void ShowWorld(bool on)
        {
            if (_rink != null) _rink.Root.SetActive(on);
            if (_entities != null) _entities.gameObject.SetActive(on);
            if (_arenaView != null) _arenaView.Root.SetActive(on);
        }

        /// <summary>The main menu's Custom Rinks: the grid of rinks made so far, + to make a new one.</summary>
        public void OpenCustomRinks()
        {
            _mainMenu.Show(MainMenu.Page.None);
            _mainMenu.Message = null;
            _maker.OpenLibrary(false);
        }

        /// <summary>The rink screen's Custom Rinks (LB / RB): pick one of them to play on.</summary>
        public void OpenCustomPick() { _maker.OpenLibrary(true); }
        public void TickMaker() { _maker.Tick(); }

        /// <summary>Out of the Custom Rinks screen: back to the rink screen, or the main menu.</summary>
        public void LibraryClosed(bool pick)
        {
            if (pick) _play.Open(PlayScreens.Page.Map);
            else _mainMenu.Show(MainMenu.Page.Main);
        }

        /// <summary>A custom rink picked on the rink screen: on to the rules.</summary>
        public void CustomRinkPicked(string file)
        {
            var at = _mapFiles.IndexOf(CustomPrefix + file);
            if (at < 0) { Sound.Deny(); _maker.OpenLibrary(true); return; }
            UseMap(at);
            _play.Open(PlayScreens.Page.Rules);
        }

        /// <summary>A rink was deleted (or saved) from the Custom Rinks screen.</summary>
        public void CustomRinksChanged() { ReloadCustomRinks(_mapIndex); }

        /// <summary>Into the maker, with a rink (a new one: <paramref name="file"/> null).</summary>
        public void EditRink(RinkPlan plan, string file)
        {
            if (_beforeMaker < 0) _beforeMaker = _mapIndex;
            _mainMenu.Show(MainMenu.Page.None);
            _mode = Mode.Editor;
            ShowWorld(false);
            _maker.OpenEditor(plan, file);
        }

        /// <summary>Out of the maker: back to the Custom Rinks screen, the main menu's rink behind it again.</summary>
        public void MakerClosed()
        {
            _testing = false;
            _local.Slots.Clear();
            _mode = Mode.Menu;
            var back = _beforeMaker >= 0 && _beforeMaker < _builtinMaps ? _beforeMaker : 0;
            _beforeMaker = -1;
            LoadCustomMaps();
            ForceMap(back);
            ShowWorld(true);
            _sim = _attractSim;
            UpdateCamera(false, true);
            _maker.OpenLibrary(false);
        }

        /// <summary>The maker's Test: free play on the rink against a bot; the menu button comes back.</summary>
        public void TestRink(RinkPlan plan, UnityEngine.InputSystem.InputDevice device)
        {
            MapDef map;
            try { map = MapDef.FromJson(plan.ToJson()); }
            catch (System.Exception ex) { Debug.LogWarning("Ultra Puck: the rink to test couldn't be loaded: " + ex.Message); Toasts.Show("Couldn't test it: " + ex.Message, 5f); return; }
            LoadCustomMaps(); // (drops an older test map)
            _maps.Add(map); _mapFiles.Add(TestMapFile); _mapBase.Add(TestMapFile);
            _testMapLoaded = true;
            ForceMap(_maps.Count - 1);
            _maker.StartTesting();
            ShowWorld(true);
            _local.Slots.Clear();
            _local.Slots.Add(new LocalPlayers.Slot { Device = device ?? UnityEngine.InputSystem.Keyboard.current, Team = 0, Colour = 0 });
            _local.Slots.Add(new LocalPlayers.Slot { Team = 1, Colour = 1, Demo = true, Name = "Bot" });
            _testing = true;
            StartMatch(true);
            UpdateCamera(false, true);
        }

        private void BackToMaker()
        {
            Sound.Back();
            DropRecording();
            _testing = false;
            _local.Slots.Clear();
            _free = false;
            _menus.FreePlay = false;
            _mode = Mode.Editor;
            ShowWorld(false);
            _maker.ResumeEditing();
        }

        // ------------------------------------------------------------------ custom arenas

        /// <summary>The arena skins in the Custom_Arenas folder.</summary>
        private List<ArenaSkin> _arenas = new List<ArenaSkin>();
        private ArenaView _arenaView;
        private ArenaSkin _arenaShown;
        private int _arenaMap = -1;
        /// <summary>Online, not hosting: the host's arena once this PC has it (its hash looked for, to look only once).</summary>
        private ArenaSkin _hostArena;
        private ulong _hostArenaFor;
        /// <summary>Screenshots: the arena to show, whatever's saved.</summary>
        private ArenaSkin _shotArena;

        private List<string> PlainMapFiles()
        {
            var list = new List<string>();
            foreach (var i in Rinks()) list.Add(_mapFiles[i]);
            return list;
        }

        private string RinkNameOf(string file) { return RinkName(_mapFiles.IndexOf(file)); }

        /// <summary>Rink <paramref name="i"/>'s custom arenas (any of its looks: they're all the same rink).</summary>
        private List<ArenaSkin> ArenasFor(int i)
        {
            var list = new List<ArenaSkin>();
            if (i < 0 || i >= _mapBase.Count) return list;
            foreach (var a in _arenas) if (a.Rink == _mapBase[i]) list.Add(a);
            return list;
        }

        /// <summary>The arena picked for rink <paramref name="i"/> (saved per rink), shown on its plain look only; null for none.</summary>
        private ArenaSkin ChosenArena(int i)
        {
            if (i < 0 || i >= _mapBase.Count) return null;
            var looks = Looks(i);
            if (looks.Count > 0 && looks[0] != i) return null;
            if (_shotArena != null) return _shotArena.Rink == _mapBase[i] ? _shotArena : null;
            var file = PlayerPrefs.GetString("arena_" + _mapBase[i], "");
            if (file.Length == 0) return null;
            foreach (var a in ArenasFor(i)) if (a.File == file) return a;
            return null;
        }

        private void ChooseArena(int i, ArenaSkin a)
        {
            if (i < 0 || i >= _mapBase.Count || ShotDir != null) return;
            PlayerPrefs.SetString("arena_" + _mapBase[i], a != null ? a.File : "");
        }

        /// <summary>Online lobby, hosting: the arena for this rink, and changing it (none, then each of its arenas).</summary>
        public bool HasArenas { get { return _arenas.Count > 0; } }
        public string ArenaLabel() { var a = ChosenArena(_mapIndex); return a != null ? a.Name : ArenasFor(_mapIndex).Count > 0 ? "None" : "None (none for this rink)"; }
        public void StepArena(int d)
        {
            var looks = Looks(_mapIndex);
            var plain = looks.Count > 0 ? looks[0] : _mapIndex;
            var arenas = ArenasFor(plain);
            if (arenas.Count == 0) return;
            var here = ChosenArena(_mapIndex);
            var at = here != null ? arenas.IndexOf(here) + 1 : 0; // 0 = none
            var to = ((at + d) % (arenas.Count + 1) + arenas.Count + 1) % (arenas.Count + 1);
            ChooseArena(plain, to == 0 ? null : arenas[to - 1]);
            if (plain != _mapIndex) UseMap(plain);
        }

        /// <summary>Online lobby, joined: the host's arena, and whether it's here yet.</summary>
        public string HostArenaText()
        {
            var s = _session;
            if (s == null || s.ArenaHash == 0) return "None";
            var name = string.IsNullOrEmpty(s.ArenaName) ? "Custom" : s.ArenaName;
            if (!Settings.OtherArenas) return name + " (off in Settings)";
            if (_hostArena != null && _hostArena.Hash == s.ArenaHash) return name;
            if (s.ArenaSize > NetSession.ArenaMax) return name + " (too big to send)";
            return name + " (downloading " + Mathf.FloorToInt(s.ArenaProgress * 100f) + "%)";
        }

        /// <summary>
        /// Every frame: which arena should be on the rink, and put it there if it changed. Online, joined: the host's
        /// (downloaded from them if need be, when Settings allows). Otherwise: the one picked for this rink. Hosting
        /// also tells everyone which it is.
        /// </summary>
        private void RefreshArena()
        {
            ArenaSkin want = null;
            var s = _session;
            if (s != null && !s.IsHost && s.Current != NetSession.Stage.Closed)
            {
                s.WantArena = false;
                if (Settings.OtherArenas && s.ArenaHash != 0)
                {
                    if (_hostArenaFor != s.ArenaHash)
                    {
                        _hostArenaFor = s.ArenaHash;
                        _hostArena = _arenas.Find(a => a.Hash == s.ArenaHash) ?? ArenaSkin.FromDownloads(s.ArenaHash, PlainMapFiles(), RinkNameOf);
                    }
                    if (_hostArena == null && s.ArenaDone != null)
                    {
                        var bytes = s.ArenaDone;
                        s.ArenaDone = null;
                        try
                        {
                            var a = ArenaSkin.FromZip(bytes, (s.ArenaName ?? "arena") + ".zip", PlainMapFiles(), RinkNameOf);
                            if (a != null && a.Hash == s.ArenaHash) { ArenaSkin.Keep(a); _hostArena = a; }
                            if (a != null) Debug.Log("Ultra Puck: got the host's arena " + a.Name + " (" + bytes.Length / 1024 + " KB)");
                        }
                        catch (System.Exception ex) { Debug.LogWarning("Ultra Puck: the host's arena couldn't be read: " + ex.Message); }
                    }
                    if (_hostArena == null) s.WantArena = true;
                    var looks = Looks(_mapIndex);
                    if (_hostArena != null && _hostArena.Rink == _mapBase[_mapIndex] && (looks.Count == 0 || looks[0] == _mapIndex)) want = _hostArena;
                }
            }
            else
            {
                want = ChosenArena(_mapIndex);
                if (s != null && s.IsHost) s.SetArena(want != null ? want.Name : null, want != null ? want.Bytes : null);
            }
            if (want == _arenaShown && _arenaMap == _mapIndex && (want == null) == (_arenaView == null)) return;
            if (_arenaView != null) { _arenaView.Destroy(); _arenaView = null; }
            if (_arenaShown != null && _arenaShown != want) _arenaShown.Unload();
            _arenaShown = want;
            _arenaMap = _mapIndex;
            if (want != null) _arenaView = new ArenaView(_map, want);
        }

        /// <summary>Every map in the Maps folder (the Test Rink first, then the rest by file name).</summary>
        private void LoadMaps()
        {
            var files = Directory.Exists(MapsFolder) ? Directory.GetFiles(MapsFolder, "*.json") : new string[0];
            System.Array.Sort(files, System.StringComparer.Ordinal);
            var list = new List<string>(files);
            var test = list.FindIndex(f => Path.GetFileName(f) == "test-rink.json");
            if (test > 0) { var t = list[test]; list.RemoveAt(test); list.Insert(0, t); }
            // ordinary maps first; then each variant (a look for one of them) right after its base
            var variants = new List<string>();
            foreach (var f in list)
            {
                try
                {
                    var text = File.ReadAllText(f);
                    if (MapDef.BaseOf(text) != null) { variants.Add(f); continue; }
                    _maps.Add(MapDef.FromJson(text)); _mapFiles.Add(Path.GetFileName(f)); _mapBase.Add(Path.GetFileName(f));
                }
                catch (System.Exception ex) { _mapProblem = Path.GetFileName(f) + ": " + ex.Message; Debug.LogWarning("Ultra Puck: couldn't load map " + _mapProblem); }
            }
            foreach (var f in variants)
            {
                try
                {
                    var text = File.ReadAllText(f);
                    var baseFile = MapDef.BaseOf(text);
                    var at = _mapFiles.IndexOf(baseFile);
                    if (at < 0) throw new System.Exception("its base map \"" + baseFile + "\" isn't there");
                    var m = MapDef.FromVariant(text, File.ReadAllText(Path.Combine(MapsFolder, baseFile)));
                    // after the base and any variants of it already in
                    var pos = at + 1;
                    while (pos < _maps.Count && _mapBase[pos] == baseFile) pos++;
                    _maps.Insert(pos, m); _mapFiles.Insert(pos, Path.GetFileName(f)); _mapBase.Insert(pos, baseFile);
                }
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
        /// <summary>Screenshots (hostlate): rounds mode on, each player picking, 3 s to pick, whatever the settings say (they aren't touched).</summary>
        private static bool _shotRules;

        private byte[] PackRules()
        {
            var b = new List<byte>();
            b.Add((byte)((Settings.Rounds ? 1 : 0) | (Settings.TeamVote ? 2 : 0) | (Settings.Replays ? 4 : 0) | (Settings.MultiUtility ? 8 : 0) | (Settings.PickMode == 1 ? 16 : 0)));
            if (_shotRules) b[0] = (byte)((b[0] | 1) & ~(2 | 4 | 16));
            b.Add((byte)Settings.CardsOffered);
            b.Add((byte)(_shotRules ? 3 : Mathf.Clamp(Settings.PickTime, 0, 255)));
            b.Add((byte)Settings.MatchMode);
            b.Add((byte)Settings.GoalsToWin);
            b.Add((byte)Settings.MatchMinutes);
            var off = new List<byte>();
            if (_cards != null) foreach (var c in _cards.Cards) if (!Settings.CardOn(c.Id) && c.Index < 256) off.Add((byte)c.Index);
            b.Add((byte)off.Count);
            b.AddRange(off);
            // after the card list (older rules simply end there): Carry-through, 0 = off, else its strength in %
            b.Add((byte)(Settings.CarryThrough ? Settings.CarryPercent : 0));
            // then Knock-away, in 5% steps (0..100 = 0..500%)
            b.Add((byte)(Settings.KnockAwayPercent / 5));
            return b.ToArray();
        }

        private static Tuning RulesTuning(byte[] rules, bool free)
        {
            var t = free ? Tuning.ForFreePlay() : new Tuning();
            if (rules == null || rules.Length < 4) return t;
            if (!free) t.CardsAfterGoals = (rules[0] & 1) != 0;
            if (!free && (rules[0] & 4) != 0) { t.Replays = true; t.CelebrateTime = Fix.FromInt(11); } // the explosion, then the replay
            t.TeamVote = (rules[0] & 2) != 0;
            t.SameDraw = !t.TeamVote && (rules[0] & 16) != 0;
            t.MultiUtility = (rules[0] & 8) != 0;
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
            if (rules.Length > 7 + n && rules[7 + n] > 0) { t.CarryThrough = true; t.CarryPercent = Mathf.Clamp(rules[7 + n], 1, 100); }
            if (rules.Length > 8 + n) t.KnockAwayPercent = Mathf.Clamp(rules[8 + n] * 5, 0, 500);
            return t;
        }

        /// <summary>Switch rinks (between matches): new rules objects, the new rink drawn, the camera reset.</summary>
        public void UseMap(int index) { UseMap(index, true); }

        /// <summary><see cref="UseMap(int)"/>; <paramref name="remember"/>: as the rink you play on (saved), not just the menu's backdrop.</summary>
        private void UseMap(int index, bool remember)
        {
            if (_maps.Count == 0) return;
            index = ((index % _maps.Count) + _maps.Count) % _maps.Count;
            if (remember) _menuRink = false;
            if (index == _mapIndex && _rink != null) return;
            BuildSims(index);
            if (remember) PlayerPrefs.SetString("map_file", _mapFiles[_mapIndex]);
            if (_rink != null) Destroy(_rink.Root);
            _rink = new RinkView(_map, _matchSim.T.WallThickness.ToFloat()); _rinkDrawn = _map;
            if (_session != null && _session.IsHost) _session.Map = _mapIndex;
            if (_cam != null) UpdateCamera(false, true);
        }

        /// <summary>The main menu shows a random rink (any of them, looks too), not the one you play on.</summary>
        private bool _menuRink;

        /// <summary>Leaving the main menu to play: back to the rink you picked last time.</summary>
        private void PlayersRink()
        {
            if (!_menuRink) return;
            _menuRink = false;
            var i = _mapFiles.IndexOf(PlayerPrefs.GetString("map_file", "test-rink.json"));
            UseMap(Mathf.Max(0, i), true);
        }

        /// <summary>Next / previous rink.</summary>
        public void StepMap(int d) { UseMap(((Mathf.Min(_mapIndex, _builtinMaps - 1) + d) % _builtinMaps + _builtinMaps) % _builtinMaps); } // (online: the game's own rinks)

        /// <summary>A hash of the map and card files: everyone in an online game must have the same ones.</summary>
        /// <summary>
        /// Where a controller player's pre-charge aims in <paramref name="net"/>: the nearest spot of the opening (between
        /// the posts, leaving room for the puck) that a straight shot from <paramref name="me"/> goes in through. From
        /// behind or beside the net, or at too tight an angle, the middle of the net instead.
        /// </summary>
        private static Vector2 ScoreableSpot(UltraPuck.Core.GoalDef net, Vector2 me, float puckRadius)
        {
            Vector2 min = V.Of(net.Min), max = V.Of(net.Max), mid = (min + max) * 0.5f;
            // which way the mouth opens: the map says, else (nets in the end walls) towards the middle of the rink
            Vector2 f;
            if (net.HasFacing) f = V.Of(net.Facing);
            else f = Mathf.Abs(mid.x) >= Mathf.Abs(mid.y) ? new Vector2(-Mathf.Sign(mid.x), 0f) : new Vector2(0f, -Mathf.Sign(mid.y));
            if (f.sqrMagnitude < 0.5f) return mid;
            var alongX = Mathf.Abs(f.x) >= Mathf.Abs(f.y); // mouth faces along x: the opening runs along y
            // the opening: the box's edge on the mouth side, posts at the box's sides
            var mouth = alongX ? new Vector2(f.x > 0 ? max.x : min.x, mid.y) : new Vector2(mid.x, f.y > 0 ? max.y : min.y);
            float lo = alongX ? min.y : min.x, hi = alongX ? max.y : max.x;
            var room = puckRadius + 0.15f; // the puck's width plus a little for aim wobble
            lo += room; hi -= room;
            if (lo > hi) return mid;
            var ahead = Vector2.Dot(me - mouth, f); // how far in front of the opening
            if (ahead < 0.3f) return mid; // beside or behind the net: no straight way in
            var across = Mathf.Clamp(alongX ? me.y : me.x, lo, hi);
            var spot = alongX ? new Vector2(mouth.x, across) : new Vector2(across, mouth.y);
            var shot = (spot - me).normalized;
            if (Vector2.Dot(shot, -f) < 0.34f) return mid; // more than ~70 degrees off straight in: too tight
            return spot;
        }

        private uint Fingerprint()
        {
            var h = 2166136261u;
            var files = new List<string>();
            for (var k = 0; k < _builtinMaps; k++) files.Add(Path.Combine(MapsFolder, _mapFiles[k])); // (not the custom rinks: they stay on this PC)
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
            PlayersRink();
            _session = session;
            if (_mapIndex >= _builtinMaps) UseMap(Mathf.Max(0, _mapFiles.IndexOf("hockey-rink.json"))); // custom rinks are for this PC only
            if (session.IsHost) session.Map = _mapIndex;
            _mainMenu.Session = session;
            _mainMenu.Message = null;
            _local.Slots.Clear();
            _mode = Mode.Online;
            // the host's people work on the host's bots; on someone else's game there are none to change
            _local.Bots = session.IsHost ? new SessionBots(session) : null;
            session.CardStatsOut = CardStats.Out;
            session.CardStatsIn = CardStats.In;
            _local.OnlineSides = () => session.TeamColours;
            _local.OnlineRecolour = session.RequestRecolour;
            _seenRecolours = 0;
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
            _local.OnlineSides = null; _local.OnlineRecolour = null;
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
        private int _seenRecolours;
        private bool _sampleFailed;
        private float _onlineAcc;

        private void OnlineUpdate()
        {
            var s = _session;
            s.Update(Time.realtimeSinceStartupAsDouble);
            if (s.Notice != null) { Toasts.Show(s.Notice); s.Notice = null; }
            if (s.Current == NetSession.Stage.Closed) { LeaveOnline(s.Problem ?? "The game ended."); return; }
            SteamNet.Describe(s.Seats.Count, s.Current == NetSession.Stage.Playing);

            if (!OnlinePlaying)
            {
                // the online lobby: people on this PC join and pick sides here; bots play behind the menu
                if (_menus.IsOpen) _menus.Close();
                if (_mainMenu.Current != MainMenu.Page.Friends) _mainMenu.Show(MainMenu.Page.OnlineLobby);
                _mainMenu.Message = s.Problem;
                _mainMenu.Tick(); // before joining: pressing A to join mustn't also press the highlighted button
                if (_mode != Mode.Online || _session == null) return;
                var busy = _mainMenu.LobbyBusy; // the options panel or the invite page is on top
                if (!busy)
                {
                    _local.LobbyUpdate();
                    if (_local.LeaveLobbyHeld()) { Sound.Back(); LeaveOnline(null); return; }
                }
                // a side got a new colour (anyone may ask): put this PC's players in their side's colour
                if (s.Recolours != _seenRecolours)
                {
                    _seenRecolours = s.Recolours;
                    for (var k = 0; k < _local.Slots.Count; k++)
                        foreach (var seat in s.Seats)
                            if (!seat.Bot && seat.Peer == s.MyPeer && seat.Local == k) { _local.Slots[k].Colour = s.TeamColours[seat.Team & 1]; break; }
                }
                foreach (var sl in _local.Slots) sl.Watching = false; // (watching is for the match that's on)
                SendLocalPlayers(s);
                if (s.IsHost) s.LobbyRules = PackRules();
                if (!busy && s.IsHost && _local.Slots.Count > 0 && _local.StartPressed() && s.Seats.Count >= 2 && !s.TooManyTeams && s.AllReady) { Sound.Select(); StartOnlineMatch(); }
                AttractUpdate();
                return;
            }

            // the match (everyone readies up again in the lobby afterwards)
            if (_mainMenu.Current != MainMenu.Page.None) _mainMenu.Show(MainMenu.Page.None);
            _local.UnreadyAll();
            SendLocalPlayers(s); // (someone changing team or going to watch)
            if (s.IsHost) s.LobbyRules = PackRules();
            var match = s.Match;
            if (s.MatchId != _onlineMatchId) { _onlineMatchId = s.MatchId; _onlineAcc = 0f; foreach (var sl in _local.Slots) sl.Latched = 0; _sim = match.Sim; _state = match.State; BeginRecording(); }
            _sim = match.Sim;
            _state = match.State;

            if (_menus.IsOpen) _menus.Tick(_local.Menu());
            else if (_state.Phase == Phase.GameOver)
            {
                if (_local.SaveReplayPressed()) SaveLastReplay();
                if (s.IsHost && _local.StartPressed()) { Sound.Select(); StartOnlineMatch(); return; }
                if (_local.BackPressed()) { Sound.Back(); if (s.IsHost) s.BackToLobby(); else LeaveOnline(null); return; }
            }
            else if (_local.MenuPressed() && !_cardScreen.Showing)
            {
                // online: the host's pause stops the game for everyone; anyone else just stands still while theirs is open
                Sound.Select();
                _menus.FreePlay = false;
                _menus.OpenPause(s.IsHost ? "Online  ·  paused for everyone" : "Online  ·  the match keeps going");
            }

            _local.LatchPresses();
            var dt = 1f / _sim.T.TicksPerSecond;
            _onlineAcc += Mathf.Min(Clock.Dt, 0.25f);
            // if we're ahead of the others, slow down a touch so nobody has to rewind further than needed
            var ahead = s.Advantage;
            if (ahead > 1.5f) _onlineAcc -= Clock.Dt * Mathf.Min(0.25f, (ahead - 1.5f) * 0.04f);
            var steps = 0;
            var simClock = System.Diagnostics.Stopwatch.StartNew();
            while (_onlineAcc >= dt && steps < 12)
            {
                PlayerInput[] inputs;
                // never let reading the controls stop the match: if it fails, this PC plays no input for the tick
                // (everyone else would otherwise sit waiting for this PC)
                try { inputs = _menus.IsOpen ? new PlayerInput[_local.Slots.Count] : _local.Sample(_sim, _state); }
                catch (System.Exception ex) { inputs = new PlayerInput[_local.Slots.Count]; if (!_sampleFailed) { _sampleFailed = true; Debug.LogException(ex); } }
                // the host's commands and pause go with the first of the host's people who has a seat (not one watching)
                var cmdSlot = -1;
                for (var k = 0; k < inputs.Length && cmdSlot < 0; k++) if (s.PlayerOf(k) >= 0) cmdSlot = k;
                if (s.IsHost && _menus.IsOpen && cmdSlot >= 0) inputs[cmdSlot].Buttons |= PlayerInput.Pause; // the host's menu pauses everyone
                // (during a card pick only the host's End the pick: the rest wait for play)
                var sendCmd = _pendingCmds.Count > 0 && cmdSlot >= 0 && (_state.Phase != Phase.Picking || _pendingCmds.Peek() == PlayerInput.EndPick);
                var leaving = -1; // a bot being taken out: which player (worked out now, as the game stands)
                if (sendCmd)
                {
                    var cmd = _pendingCmds.Peek();
                    if (cmd == RemoveRedSoon || cmd == RemoveBlueSoon)
                    {
                        leaving = OnlineBotToRemove(cmd == RemoveBlueSoon ? 1 : 0);
                        if (leaving < 0) { _pendingCmds.Dequeue(); sendCmd = false; }
                        else cmd = (byte)(PlayerInput.RemoveBot + leaving);
                    }
                    if (sendCmd) inputs[cmdSlot].Pick = cmd;
                }
                if (!s.Step(inputs))
                {
                    // waiting for the others: keep the time owed (up to a quarter of a second) so we catch up when their
                    // controls arrive, instead of staying behind for good (which slows everyone else down to match)
                    _onlineAcc = Mathf.Min(_onlineAcc, 0.25f);
                    _netStalls++;
                    break;
                }
                if (sendCmd) { _pendingCmds.Dequeue(); if (leaving >= 0) s.BotLeftMatch(leaving); }
                _onlineAcc -= dt;
                steps++;
            }
            NetStats((float)simClock.Elapsed.TotalMilliseconds);
            _state = match.State;
            Recorder.Record(_state);
            RecordFrame();
            if (ReplayFrame(false)) return;
            Show(match.Previous, match.State, Mathf.Clamp01(_onlineAcc / dt));
        }

        // network stats, written to the log every 10 s during an online match (for working out lag)
        private int _netStalls, _netFrames, _netRewinds;
        private long _netMsgs, _netBytes;
        private float _netStatsAt, _netSimMs, _netWorstFrame;

        private void NetStats(float simMs)
        {
            _netFrames++;
            _netSimMs += simMs;
            _netWorstFrame = Mathf.Max(_netWorstFrame, Clock.Dt);
            if (Clock.Now - _netStatsAt < 10f) return;
            var s = _session;
            var m = s != null ? s.Match : null;
            if (m != null && _netStatsAt > 0f)
            {
                var secs = Clock.Now - _netStatsAt;
                Debug.Log("Ultra Puck net: " + s.Seats.Count + " players, ping " + Mathf.RoundToInt(s.Ping * 1000f) + " ms, ahead " + s.Advantage.ToString("0.0") +
                    " ticks, waited " + _netStalls + " times, rewinds " + (m.Rewinds - _netRewinds) + " (longest " + m.LongestRewind + " ticks), " +
                    Mathf.RoundToInt(_netFrames / secs) + " fps (worst frame " + Mathf.RoundToInt(_netWorstFrame * 1000f) + " ms), game " +
                    (_netSimMs / Mathf.Max(1, _netFrames)).ToString("0.00") + " ms a frame, confirmed " + (m.Tick - m.Confirmed) + " ticks back, sent " +
                    Mathf.RoundToInt((s.MessagesSent - _netMsgs) / secs) + " messages a second (" + ((s.BytesSent - _netBytes) / secs / 1024f).ToString("0.0") + " KB/s)");
            }
            _netStatsAt = Clock.Now;
            _netStalls = 0; _netFrames = 0; _netSimMs = 0f; _netWorstFrame = 0f;
            if (m != null) { _netRewinds = m.Rewinds; m.LongestRewind = 0; }
            if (s != null) { _netMsgs = s.MessagesSent; _netBytes = s.BytesSent; }
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
            var half = _matchSim.T.WallThickness.ToFloat() / 2f;
            var px = new Color32[W * H];
            Color32 floor = Palette.Floor, wall = new Color(0.62f, 0.62f, 0.64f);
            var wet = map.Theme == Underwater.Theme;
            if (map.Theme == Underwater.ForestTheme)
            {
                // in the forest: grass, wooden boards, darker forest floor all round
                floor = Underwater.Grass; wall = Underwater.Wood;
                for (var y = 0; y < H; y++)
                {
                    Color32 c = Color.Lerp(Underwater.ForestDark, Underwater.ForestLight, (float)y / H);
                    for (var x = 0; x < W; x++) px[y * W + x] = c;
                }
            }
            else if (wet)
            {
                // under the sea: sand, blue-grey boards, deep water all round (lighter towards the top)
                floor = Underwater.Sand; wall = Underwater.Wall;
                for (var y = 0; y < H; y++)
                {
                    Color32 c = Color.Lerp(Underwater.DeepWater, Underwater.ShallowWater, (float)y / H);
                    for (var x = 0; x < W; x++) px[y * W + x] = c;
                }
            }
            System.Func<float, int> toX = wx => Mathf.RoundToInt((wx - min.x) * scale + off.x);
            System.Func<float, int> toY = wy => Mathf.RoundToInt((wy - min.y) * scale + off.y);

            // the floor, a row at a time: where the row crosses the outline, fill between pairs of crossings
            var xs = new List<float>();
            for (var y = 0; y < H; y++)
            {
                var wy = (y + 0.5f - off.y) / scale + min.y;
                xs.Clear();
                for (int e = 0, f = outline.Count - 1; e < outline.Count; f = e++)
                {
                    Vector2 a = outline[e], b = outline[f];
                    if ((a.y > wy) == (b.y > wy)) continue;
                    xs.Add(a.x + (wy - a.y) * (b.x - a.x) / (b.y - a.y));
                }
                xs.Sort();
                for (var k = 0; k + 1 < xs.Count; k += 2)
                {
                    int x0 = Mathf.Max(0, Mathf.CeilToInt((xs[k] - min.x) * scale + off.x - 0.5f)), x1 = Mathf.Min(W - 1, Mathf.FloorToInt((xs[k + 1] - min.x) * scale + off.x - 0.5f));
                    for (var x = x0; x <= x1; x++) px[y * W + x] = floor;
                }
            }
            // the goals: their boxes
            foreach (var g in map.Goals)
            {
                Color32 gc = Palette.Goal(g.Team);
                int x0 = Mathf.Max(0, toX(g.Min.X.ToFloat())), x1 = Mathf.Min(W - 1, toX(g.Max.X.ToFloat()));
                int y0 = Mathf.Max(0, toY(g.Min.Y.ToFloat())), y1 = Mathf.Min(H - 1, toY(g.Max.Y.ToFloat()));
                for (var y = y0; y <= y1; y++) for (var x = x0; x <= x1; x++) px[y * W + x] = gc;
            }
            // the walls: each piece paints only the pixels round it
            foreach (var sg in map.Segments)
            {
                Vector2 a = V.Of(sg.A), b = V.Of(sg.B), ab = b - a;
                var len2 = Mathf.Max(1e-6f, ab.sqrMagnitude);
                int x0 = Mathf.Max(0, toX(Mathf.Min(a.x, b.x) - half) - 1), x1 = Mathf.Min(W - 1, toX(Mathf.Max(a.x, b.x) + half) + 1);
                int y0 = Mathf.Max(0, toY(Mathf.Min(a.y, b.y) - half) - 1), y1 = Mathf.Min(H - 1, toY(Mathf.Max(a.y, b.y) + half) + 1);
                for (var y = y0; y <= y1; y++)
                    for (var x = x0; x <= x1; x++)
                    {
                        var p = new Vector2((x + 0.5f - off.x) / scale + min.x, (y + 0.5f - off.y) / scale + min.y);
                        var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
                        if ((p - (a + ab * t)).sqrMagnitude <= half * half) px[y * W + x] = wall;
                    }
            }
            tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px);
            tex.Apply();
            _previews[i] = tex;
            return tex;
        }

        /// <summary>Draws every rink's preview in the background at start, one a frame, so the rink screen opens at once.</summary>
        private System.Collections.IEnumerator WarmPreviews()
        {
            for (var i = 0; i < _maps.Count; i++)
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                MapPreview(i);
                Debug.Log("Ultra Puck: rink picture " + _mapFiles[i] + " in " + clock.ElapsedMilliseconds + " ms");
                yield return null;
            }
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
            _seenCarrier = -1;
            Recorder.Clear();
            _replay = null;
            _replayPending = false;
            _replayExit = false;
            _replayStarted = false;
            _replayHoldA = _replayHoldB = null;
            _wipe.Stop();
            _mode = Mode.Match;
            _menus.FreePlay = free;
            foreach (var s in _local.Slots) s.Latched = 0;
            if (free) DropRecording(); else BeginRecording();
        }

        private void BackToLobby()
        {
            DropRecording();
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

        // online: a bot the host asked for, sent with the host's controls on the next tick (so every PC adds it on the same tick)
        private readonly Queue<byte> _pendingCmds = new Queue<byte>();
        private bool _shotBoard; // screenshots: the scoreboard as if View were held

        /// <summary>Match settings (the host): kick-off or a score fix. Online it goes with the host's controls, one a tick.</summary>
        // the host holding the menu button through a card pick ends it (someone stuck, or no time limit)
        private const float EndPickHold = 1.5f;
        private float _endPickHeld;
        private bool _endPickSent;

        private void HostEndPick(bool playing)
        {
            var can = playing && _state != null && _state.Phase == Phase.Picking && !_free && !_menus.IsOpen
                && (_mode == Mode.Match || (_mode == Mode.Online && _session != null && _session.IsHost));
            if (!can) { _endPickHeld = 0f; _endPickSent = false; _cardScreen.HostEnd = -1f; return; }
            if (!_local.MenuHeld()) { _endPickHeld = 0f; _endPickSent = false; }
            else if (!_endPickSent)
            {
                _endPickHeld += Time.unscaledDeltaTime;
                if (_endPickHeld >= EndPickHold)
                {
                    _endPickSent = true;
                    Sound.Select();
                    Debug.Log("Ultra Puck cards: the host ended the pick");
                    HostCommand(PlayerInput.EndPick);
                }
            }
            _cardScreen.HostEnd = _endPickSent ? 1f : Mathf.Clamp01(_endPickHeld / EndPickHold);
        }

        private void HostCommand(byte cmd)
        {
            if (_state == null) return;
            if (_mode == Mode.Online)
            {
                if (_session == null || !_session.IsHost) return;
                var range = cmd >= PlayerInput.CarryStrength && cmd <= PlayerInput.CarryStrength + 20 ? PlayerInput.CarryStrength
                    : cmd >= PlayerInput.KnockAway && cmd <= PlayerInput.KnockAway + 20 ? PlayerInput.KnockAway : -1;
                if (range >= 0)
                {
                    // dragging a slider: only its newest value needs to go
                    var keep = new List<byte>(_pendingCmds);
                    keep.RemoveAll(c => c >= range && c <= range + 20);
                    _pendingCmds.Clear();
                    foreach (var c in keep) _pendingCmds.Enqueue(c);
                }
                _pendingCmds.Enqueue(cmd);
                return;
            }
            _sim.HostCommand(_state, cmd);
            if (cmd == PlayerInput.KickOff) { _seenCarrier = -1; _prev = _state.Clone(); }
        }

        /// <summary>Score changes queued but not sent yet (so the menu shows the score it will be).</summary>
        private int PendingScore(int team)
        {
            var d = 0;
            foreach (var c in _pendingCmds)
            {
                if (team == 0) d += c == PlayerInput.RedUp ? 1 : c == PlayerInput.RedDown ? -1 : 0;
                else d += c == PlayerInput.BlueUp ? 1 : c == PlayerInput.BlueDown ? -1 : 0;
            }
            return d;
        }

        /// <summary>Match settings: a bot joins <paramref name="team"/> mid-match.</summary>
        private void AddBotNow(int team)
        {
            if (_state == null) return;
            if (_mode == Mode.Online)
            {
                if (_session == null || !_session.IsHost) return;
                _pendingCmds.Enqueue(team == 1 ? PlayerInput.AddBotBlue : PlayerInput.AddBotRed);
                _session.BotJoinedMatch(team);
                return;
            }
            if (!_local.AddBotTo(team)) return;
            _sim.AddPlayer(_state, team & 1, true);
            if (_cards != null) _cards.Recompute(_state);
            _prev = _state.Clone();
        }

        // (queued, not sent: the bot to take out is picked when the command goes)
        private const byte RemoveRedSoon = 230, RemoveBlueSoon = 231;

        /// <summary>Match settings: take a bot off <paramref name="team"/> mid-match (the last one on it).</summary>
        private void RemoveBotNow(int team)
        {
            if (_state == null || !CanRemoveBot(team)) return;
            if (_mode == Mode.Online)
            {
                if (_session == null || !_session.IsHost) return;
                _pendingCmds.Enqueue(team == 1 ? RemoveBlueSoon : RemoveRedSoon);
                return;
            }
            for (var i = Mathf.Min(_local.Slots.Count, _state.Players.Length) - 1; i >= 0; i--)
            {
                if (!_local.Slots[i].Demo || (_state.Players[i].Team & 1) != (team & 1)) continue;
                _local.Slots.RemoveAt(i);
                _sim.RemovePlayer(_state, i);
                break;
            }
            _seenCarrier = -1;
            if (_cards != null) _cards.Recompute(_state);
            _prev = _state.Clone();
        }

        /// <summary>Is there a bot on <paramref name="team"/> that can be taken out now?</summary>
        private bool CanRemoveBot(int team)
        {
            if (_state == null || _state.Phase == Phase.Picking) return false;
            if (_mode == Mode.Online) return _session != null && _session.IsHost && OnlineBotToRemove(team) >= 0;
            for (var i = 0; i < _local.Slots.Count && i < _state.Players.Length; i++)
                if (_local.Slots[i].Demo && (_state.Players[i].Team & 1) == (team & 1)) return true;
            return false;
        }

        /// <summary>
        /// Online: the last bot on <paramref name="team"/> that can go, or -1. Only one with nothing but bots after it, so
        /// no person's player number moves (every PC lines its people up by number).
        /// </summary>
        private int OnlineBotToRemove(int team)
        {
            if (_session == null || _state == null) return -1;
            var seats = _session.Seats;
            for (var i = _state.Players.Length - 1; i >= 0; i--)
            {
                var seatBot = i >= seats.Count || seats[i].Bot;
                if (!seatBot || !_state.Players[i].Bot) return -1; // a person (here or away): nothing before them can go
                if ((_state.Players[i].Team & 1) == (team & 1)) return i;
            }
            return -1;
        }

        /// <summary>Online: tell the session who plays on this PC (names, colours, ready, watching).</summary>
        private void SendLocalPlayers(NetSession s)
        {
            var mine = new List<KeyValuePair<string, int>>();
            for (var k = 0; k < _local.Slots.Count; k++)
            {
                var sl = _local.Slots[k];
                mine.Add(new KeyValuePair<string, int>(SlotName(k), NetSession.Look(sl.Colour, sl.Symbol, sl.Ready, sl.Watching)));
            }
            s.SetLocalPlayers(mine);
            var looks = new List<int>();
            var accounts = new List<ulong>();
            for (var k = 0; k < _local.Slots.Count; k++) { looks.Add(k == FirstPerson() ? Cosmetics.Mine : 0); accounts.Add(k == FirstPerson() ? SteamNet.MyId : 0); }
            s.SetLocalStyles(looks, accounts);
        }

        /// <summary>The name of the person on this PC's slot <paramref name="k"/>.</summary>
        private string SlotName(int k)
        {
            if (k < 0 || k >= _local.Slots.Count) return "";
            var sl = _local.Slots[k];
            return !string.IsNullOrEmpty(sl.Name) ? sl.Name : Settings.PlayerName + (k > 0 ? " (" + (k + 1) + ")" : "");
        }

        /// <summary>The people on this PC who can change team from the pause menu (their slots).</summary>
        private List<int> TeamPlayers()
        {
            var list = new List<int>();
            if (!InMatch || _free) return list;
            for (var k = 0; k < _local.Slots.Count; k++) if (!_local.Slots[k].Demo) list.Add(k);
            return list;
        }

        /// <summary>Slot <paramref name="k"/>'s team in the match (-1 watching). Online, what they asked for until the host has done it.</summary>
        private int SlotTeam(int k)
        {
            var sl = _local.Slots[k];
            if (_mode == Mode.Online && _session != null)
            {
                if (sl.Watching) return -1;
                var t = _session.TeamColours[0] == sl.Colour ? 0 : _session.TeamColours[1] == sl.Colour ? 1 : -1;
                if (t >= 0) return t;
                var p = _session.PlayerOf(k);
                return p >= 0 && p < _session.Seats.Count ? _session.Seats[p].Team : 0;
            }
            return k < _state.Players.Length ? _state.Players[k].Team : sl.Team;
        }

        /// <summary>
        /// Pause menu: slot <paramref name="k"/> goes to <paramref name="team"/> (-1 = watch, online only). Online the host
        /// re-seats the match (soon, once any card pick is over); here it's straight away.
        /// </summary>
        private void SetSlotTeam(int k, int team)
        {
            if (k < 0 || k >= _local.Slots.Count || _state == null) return;
            var sl = _local.Slots[k];
            if (_mode == Mode.Online && _session != null)
            {
                if (team < 0) { sl.Watching = true; return; }
                sl.Watching = false;
                sl.Colour = _session.TeamColours[team & 1];
                return;
            }
            if (team < 0 || k >= _state.Players.Length || _state.Phase == Phase.Picking) { Sound.Deny(); return; }
            if (_state.Players[k].Team == (team & 1)) return;
            sl.Team = team & 1;
            sl.Colour = Palette.TeamColour(team & 1);
            _sim.ChangeTeam(_state, k, team & 1);
            if (_cards != null) _cards.Recompute(_state);
            _prev = _state.Clone();
        }

        /// <summary>Free play: add or remove computer players until there are <paramref name="want"/>.</summary>
        private void SetBots(int want)
        {
            want = Mathf.Clamp(want, 0, LocalPlayers.MaxPlayers - _local.HumanCount);
            while (_local.BotCount < want && _local.AddBot()) _sim.AddPlayer(_state, _local.Slots[_local.Slots.Count - 1].Team, true);
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
                if (_local.SaveReplayPressed()) SaveLastReplay();
                if (_local.StartPressed()) { Sound.Select(); StartMatch(false); return; }
                if (_local.BackPressed()) { Sound.Back(); BackToLobby(); return; }
            }
            else if (_testing && _local.MenuPressed() && !_cardScreen.Showing) { BackToMaker(); return; } // testing a rink: straight back to the maker
            else if (_local.MenuPressed() && !_cardScreen.Showing)
            {
                Sound.Select();
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
                // who steers whom (swaps onto bot teammates, being handed a bot that gets the puck) is worked out in
                // Route, part of the game, so it plays the same here and online
                _sim.Step(_state, _shotNoInput ? new PlayerInput[_state.Players.Length] : _sim.Route(_state, _local.Sample(_sim, _state)));
                _seenCarrier = _state.Carrier;
                Recorder.Record(_state);
                RecordFrame();
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
        private bool _replayExit;
        private bool _replayStarted;
        private float _replayStartAt;
        private float _goalZoomUntil;
        private int _goalTick;
        private Phase _lastPhase;
        private readonly ReplayTransition _wipe = new ReplayTransition();
        private ReplayTransition.Style _wipeStyle;
        private GameState _replayHoldA, _replayHoldB;
        private float _replayHoldT;

        /// <summary>
        /// After a goal (with replays on): the explosion plays live, then a team-colour wipe, the replay, and the same
        /// wipe back out. Returns true while the replay or its wipes are what's on screen.
        /// </summary>
        private bool ReplayFrame(bool local)
        {
            if (_state == null || _sim == null) return false;
            _hud.Wipe = _wipe;
            var tps = _sim.T.TicksPerSecond;
            var dt = Clock.Dt;

            if (_state.Phase != Phase.Scored)
            {
                // celebration over (cards, face-off, game over, skip): drop the wipe immediately — never run it
                // over the card pick or the next face-off
                _lastPhase = _state.Phase;
                _replay = null;
                _replayPending = false;
                _replayExit = false;
                _replayStarted = false;
                _replayHoldA = _replayHoldB = null;
                _wipe.Stop();
                _hud.Replay = false;
                return false;
            }

            if (_lastPhase != Phase.Scored)
            {
                _lastPhase = Phase.Scored;
                _goalTick = _state.Tick;
                _replayPending = _sim.T.Replays;
                _replayExit = false;
                _replayStarted = false;
                _replayHoldA = _replayHoldB = null;
                _wipeStyle = ReplayTransition.PickStyle(_goalTick + _state.LastScorer * 17);
                _replayStartAt = Clock.Now + 3f; // zoom on the scorer, then wipe in
                _goalZoomUntil = _replayStartAt;
            }

            if (_replayPending && Clock.Now >= _replayStartAt)
            {
                _replayPending = false;
                _wipe.Begin(_wipeStyle, ReplayTransition.GreyStripeColours());
            }

            if (_wipe.Active) _wipe.Tick(dt);

            // cover the live goal, then start the replay underneath and reveal it
            if (!_replayExit && _wipe.Active && _replay == null)
            {
                if (_wipe.Covered)
                {
                    var frames = Recorder.Before(_goalTick + tps * 7 / 10, tps);
                    _replay = new GoalReplay(frames, _state.LastScorer, _sim, _state.PhaseTime.ToFloat() - 0.4f);
                    _replayStarted = true;
                }
                Show(_state, _state, 0f);
                return true;
            }

            if (_replayExit)
            {
                if (_wipe.Covered) _replayHoldA = _replayHoldB = null;
                if (_wipe.Active)
                {
                    if (_replayHoldA != null) Show(_replayHoldA, _replayHoldB ?? _replayHoldA, _replayHoldT, true);
                    else Show(_state, _state, 0f);
                    _hud.Replay = _replayHoldA != null;
                    return true;
                }
                _replayExit = false;
                _replayStarted = false;
                _hud.Replay = false;
                return false;
            }

            if (_replay == null) return false;

            GameState a, b;
            float t;
            _replay.Advance(dt, tps, out a, out b, out t);
            if (_replay.Done || a == null)
            {
                _replay = null;
                _replayExit = true;
                _wipe.Begin(_wipeStyle, ReplayTransition.GreyStripeColours());
                if (_replayHoldA != null) Show(_replayHoldA, _replayHoldB ?? _replayHoldA, _replayHoldT, true);
                else Show(_state, _state, 0f);
                return true;
            }
            _replayHoldA = a;
            _replayHoldB = b;
            _replayHoldT = t;
            _hud.Replay = true;
            _hud.ReplaySkippable = true;
            _hud.ReplaySlow = _replay.Slow < 0.9f;
            Show(a, b, t, true);
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
            if (!IsLocalPerson(i)) return false;
            goal = V.Of(Bot.GoalCentre(_sim.Map, 1 - p.Team));
            charge = Mathf.Clamp01(p.Charge.ToFloat());
            return true;
        }

        /// <summary>
        /// Double Agent: player <paramref name="i"/> is a person playing on this PC and nobody here plays for the other
        /// team, so their borrowed name isn't needed on this screen.
        /// </summary>
        private bool SeesOwnName(GameState b, int i)
        {
            if (!IsLocalPerson(i)) return false;
            for (var j = 0; j < b.Players.Length; j++) if (b.Players[j].Team != b.Players[i].Team && IsLocalPerson(j)) return false;
            return true;
        }

        /// <summary>Match player <paramref name="i"/> is a person playing on this PC (not a bot, not someone online).</summary>
        private bool IsLocalPerson(int i)
        {
            if (i < 0) return false;
            // swapped onto a bot teammate: that's the one you play now
            var d = _state != null && i < _state.Players.Length ? Simulation.DriverOf(_state, i) : i;
            if (d < 0) return false;
            if (_mode == Mode.Online && _session != null) return d < _session.Seats.Count && !_session.Seats[d].Bot && _session.Seats[d].Peer == _session.MyPeer;
            return _mode == Mode.Match && d < _local.Slots.Count && !_local.Slots[d].Demo;
        }

        // ------------------------------------------------------------------ sounds

        /// <summary>A match is on (here or online), not the menus or a lobby.</summary>
        private bool InMatch { get { return _mode == Mode.Match || (_mode == Mode.Online && OnlinePlaying); } }

        // what the match looked like the last time its sounds were worked out
        private int _heardTick = int.MinValue;
        private Phase _heardPhase;
        private int _heardCount, _heardKnocks;
        private float _heardMovingAt;
        private readonly List<int> _heardHits = new List<int>(), _heardShots = new List<int>(), _heardSwaps = new List<int>();
        private readonly List<float> _heardDashes = new List<float>();

        /// <summary>Where a spot on the rink sits between the speakers: a little left or right of centre, never hard over.</summary>
        private float Pan(Vector2 at)
        {
            if (_cam == null) return 0f;
            return Mathf.Clamp((at.x - _camPos.x) / Mathf.Max(1f, _camSize * _cam.aspect), -1f, 1f) * 0.5f;
        }

        /// <summary>
        /// The match's sounds, from what's on screen now next to what was there last frame: new hits, shots and
        /// dashes (the players' counters going up), a goal, and the hum of a shot being charged. Goal replays are
        /// heard as well as seen. When the picture jumps (a new match, a replay starting or ending) there's nothing
        /// to compare with, so that frame is silent.
        /// </summary>
        private void MatchSounds(GameState s)
        {
            var n = s.Players.Length;
            var jump = _heardTick == int.MinValue || s.Tick < _heardTick || s.Tick - _heardTick > 60 || n != _heardHits.Count;
            if (s.Tick != _heardTick) _heardMovingAt = Clock.Now;
            if (!jump)
            {
                bool orange, blue;
                LocalTeams(out orange, out blue);
                var hit = 0f;
                var hitAt = Vector2.zero;
                for (var i = 0; i < n; i++)
                {
                    var p = s.Players[i];
                    var at = V.Of(p.Pos);
                    var mine = IsLocalPerson(i);
                    // both players in a hit report it: one sound, as hard as the harder report
                    if (p.Hits > _heardHits[i] && p.HitPower.ToFloat() > hit) { hit = p.HitPower.ToFloat(); hitAt = at; }
                    if (p.ShotsFired > _heardShots[i])
                    {
                        // a harder shot is louder and lower
                        var k = p.ShotStrength.ToFloat();
                        Sound.Play(Sfx.Shoot, Mathf.Lerp(0.5f, 1f, k) * (mine ? 1f : 0.75f), Mathf.Lerp(1.15f, 0.85f, k) * Random.Range(0.97f, 1.03f), Pan(at));
                    }
                    if (_heardDashes[i] - p.DashCharge.ToFloat() > 0.5f)
                    {
                        // an invisible opponent's dash would give them away
                        var unseen = p.InvisibleTime > Fix.Zero && (p.Team == 0 ? blue && !orange : orange && !blue);
                        if (!unseen) Sound.Play(Sfx.Dash, mine ? 0.55f : 0.36f, Random.Range(0.94f, 1.06f), Pan(at));
                    }
                    if (p.Swaps > _heardSwaps[i])
                        Sound.Play(Sfx.Swap, mine ? 0.7f : 0.5f, Random.Range(0.97f, 1.03f), Pan(at));
                }
                // the big checks (the ones with lightning) get the heavy sound; light bumps a soft thud
                if (hit >= 0.75f) Sound.Play(Sfx.BigHit, Mathf.Lerp(0.8f, 1f, (hit - 0.75f) / 0.25f), Random.Range(0.95f, 1.05f), Pan(hitAt));
                else if (hit > 0.1f) Sound.Play(Sfx.Hit, Mathf.Lerp(0.25f, 0.85f, hit / 0.75f), Mathf.Lerp(1.2f, 0.92f, hit / 0.75f) * Random.Range(0.96f, 1.04f), Pan(hitAt));
                // the puck off the boards: higher and louder the faster it was going
                if (s.PuckKnocks > _heardKnocks && _sim != null)
                {
                    var v = s.PuckKnockSpeed.ToFloat();
                    if (v > 3f) Sound.Bounce((v - 3f) / Mathf.Max(1f, _sim.T.ChargedShotMax.ToFloat() - 3f), Pan(V.Of(s.PuckPos)));
                }
                if (s.Phase == Phase.Scored && _heardPhase != Phase.Scored) Sound.Play(Sfx.Goal, 0.55f, 1f, Pan(V.Of(s.PuckPos))); // it's a long, thick sound: at full level it drowned everything else
            }

            // the countdown: a beep on each number, a brighter one (and START on screen) as play begins
            var count = s.Phase == Phase.Countdown && _sim != null && s.PhaseTime <= _sim.T.CountdownTime ? Mathf.CeilToInt(s.PhaseTime.ToFloat() - 0.001f) : 0;
            if (count > 0 && count != _heardCount && !_hud.Replay) Sound.Play(Sfx.CountBeep, 0.6f, 1f, 0f);
            if (s.Phase == Phase.Play && _heardPhase == Phase.Countdown && !_free && !jump)
            {
                Sound.Play(Sfx.CountGo, 0.7f, 1f, 0f);
                _hud.StartAt = Clock.Now;
            }
            _heardCount = count;

            // a Shockwave being held hums, higher as it grows (the loudest one: yours, if you're holding one)
            if (!s.Paused && Clock.Now - _heardMovingAt < 0.15f && _sim != null)
                for (var k = 0; k < n; k++)
                    if (s.Players[k].ShockHold > Fix.Zero && s.Players[k].InvisibleTime <= Fix.Zero)
                        Sound.ShockCharge(s.Players[k].ShockHold.ToFloat() / Mathf.Max(0.01f, _sim.T.ShockChargeTime.ToFloat()),
                            IsLocalPerson(k) ? 1f : 0.6f, Pan(V.Of(s.Players[k].Pos)));

            // charging a shot hums while the game is actually running (not paused, not waiting for other players)
            var c = s.Carrier;
            if (c >= 0 && c < n && s.Players[c].Charge > Fix.Zero && !s.Paused && Clock.Now - _heardMovingAt < 0.15f)
                Sound.Charge(s.Players[c].Charge.ToFloat(), IsLocalPerson(c) ? 1f : 0.6f, Pan(V.Of(s.Players[c].Pos)));

            _heardTick = s.Tick;
            _heardPhase = s.Phase;
            _heardKnocks = s.PuckKnocks;
            _heardHits.Clear(); _heardShots.Clear(); _heardDashes.Clear(); _heardSwaps.Clear();
            foreach (var p in s.Players)
            {
                _heardHits.Add(p.Hits); _heardShots.Add(p.ShotsFired); _heardDashes.Add(p.DashCharge.ToFloat());
                _heardSwaps.Add(p.Swaps);
            }
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
            // hidden from a screen with only opponents on it; an outline for a teammate (or nobody, like the menu).
            // Replays always show the outline so everyone can follow who was invisible.
            var hidden = invisible && opponentHere && !teammateHere && !_hud.Replay;
            var faint = invisible && !hidden;
            var gunFull = _sim.T.GunCooldown.ToFloat() + (p.Bot || Simulation.DriverOf(_state, i) < 0 ? _sim.T.GunBotExtra.ToFloat() : 0f);
            _cooldowns[0] = p.Stats.PuckGun > 0 ? 1f - p.GunCooldown.ToFloat() / Mathf.Max(0.01f, gunFull)
                : p.Stats.PortalGun > 0 ? 1f - p.PortalCooldown.ToFloat() / Mathf.Max(0.01f, _sim.T.PortalCooldown.ToFloat())
                : p.Stats.SlotMachine > 0 ? SlotBar(p)
                : -1f;
            _cooldowns[1] = p.Stats.Invisibility > 0 ? (invisible ? 0f : 1f - p.InvisCooldown.ToFloat() / _sim.T.InvisCooldown.ToFloat()) : -1f;
            _cooldowns[2] = p.Stats.Shockwave > 0 ? (p.ShockHold > Fix.Zero ? p.ShockHold.ToFloat() / Mathf.Max(0.01f, _sim.T.ShockChargeTime.ToFloat()) : 1f)
                : p.Stats.Swap > 0 ? 1f // Swap has no cooldown — always ready
                : -1f;
            _players[i].Status(hidden, faint, p.SlowTime > Fix.Zero, p.FastTime > Fix.Zero || (p.SlotPrizeTime > Fix.Zero && p.SlotPrize == CardLibrary.SlotSpeed), _cooldowns,
                p.Stun > Fix.Zero, p.DashTime.ToFloat() / Mathf.Max(0.01f, _sim.T.DashHitTime.ToFloat()), p.HitLag > Fix.Zero,
                _state != null && _state.Carrier == i, V.Of(p.DashDir));

            // Shockwave being held: a faint ring showing how big it'll be
            _players[i].ShockCharge(p.ShockHold > Fix.Zero && !hidden ? _sim.ShockRadius(p.ShockHold).ToFloat() : 0f);
            while (_seenShocks.Count <= i) { _seenShocks.Add(-1); _seenRefreshes.Add(-1); _seenSwaps.Add(-1); }
            if (_seenShocks[i] >= 0 && p.Shocks > _seenShocks[i])
            {
                // the blast: bigger waves boom deeper and louder
                var big = Mathf.Clamp01(p.ShockRadius.ToFloat() / Mathf.Max(0.01f, _sim.T.ShockMax.ToFloat()));
                Sound.Play(Sfx.ShockBlast, Mathf.Lerp(0.45f, 0.95f, big), Mathf.Lerp(1.25f, 0.85f, big), Pan(pos));
            }
            if (_seenShocks[i] >= 0 && p.Shocks > _seenShocks[i] && Settings.HitEffects)
            {
                // thin expanding ring stuck to the caster, sized to the real hit radius
                _effects.Shockwave(_players[i].Root.transform, p.ShockRadius.ToFloat(), new Color(1f, 0.25f, 0.22f), 0.35f);
                _shake = Mathf.Max(_shake, 0.22f);
            }
            if (_seenSwaps[i] >= 0 && p.Swaps > _seenSwaps[i] && Settings.HitEffects)
            {
                _effects.SwapBurst(V.Of(p.SwapFrom), V.Of(p.SwapTo));
                _shake = Mathf.Max(_shake, 0.18f);
            }
            if (_seenRefreshes[i] >= 0 && p.Refreshes > _seenRefreshes[i])
                _effects.Ring(pos, 0.6f, new Color(0.35f, 0.95f, 0.55f), 1.3f, 0.5f);
            _seenShocks[i] = p.Shocks;
            _seenRefreshes[i] = p.Refreshes;
            _seenSwaps[i] = p.Swaps;
            return hidden;
        }

        private readonly List<int> _steering = new List<int>();

        /// <summary>
        /// Swap trail (a setting, off by default): when someone on this PC swaps to another body (a bot teammate, or back
        /// to their own), a trail flies from the body they left to the one they're on now.
        /// </summary>
        private void SwapTrails(GameState b)
        {
            while (_steering.Count < b.Players.Length) _steering.Add(-1);
            for (var h = 0; h < b.Players.Length; h++)
            {
                var now = b.Players[h].Bot ? -1 : Simulation.Steers(b, h);
                var was = _steering[h];
                _steering[h] = now;
                if (!Settings.SwapTrail || was < 0 || now < 0 || was == now || !IsLocalPerson(now)) continue;
                if (was >= _players.Count || now >= _players.Count || _players[was].Root == null || _players[now].Root == null) continue;
                var from = (Vector2)_players[was].Root.transform.localPosition;
                _effects.SwapTrail(from, _players[now].Root.transform, Palette.Team(b.Players[now].Team));
            }
            if (_steering.Count > b.Players.Length) _steering.RemoveRange(b.Players.Length, _steering.Count - b.Players.Length);
        }

        /// <summary>
        /// Slot Machine's bar under the player: a prize running down while it lasts, filling while a spin goes and during
        /// the wait after a loss, full when it's ready to spin.
        /// </summary>
        private float SlotBar(PlayerState p)
        {
            var T = _sim.T;
            if (p.SlotPrizeTime > Fix.Zero)
            {
                var full = p.SlotPrize == CardLibrary.SlotSpeed ? T.SlotSpeedTime : p.SlotPrize == CardLibrary.SlotGiant ? T.SlotGiantTime
                    : p.SlotPrize == CardLibrary.SlotInvisible ? T.SlotInvisibleTime : T.SlotStrongTime;
                return p.SlotPrizeTime.ToFloat() / Mathf.Max(0.01f, full.ToFloat());
            }
            if (p.SlotSpin > Fix.Zero) return 1f - p.SlotSpin.ToFloat() / Mathf.Max(0.01f, T.SlotSpinTime.ToFloat());
            if (p.SlotWait > Fix.Zero) return 1f - p.SlotWait.ToFloat() / Mathf.Max(0.01f, T.SlotWait.ToFloat());
            return 1f;
        }

        /// <summary>Player <paramref name="i"/>'s symbol: their online seat's, or their slot's on this PC.</summary>
        private int SymbolOf(int i)
        {
            if (_mode == Mode.Replay && _viewer.Replay != null) { var c = ReplayCast(); return c != null && i < c.Symbols.Length ? c.Symbols[i] : i & 7; }
            if (_mode == Mode.Online && _session != null) return i < _session.Seats.Count ? _session.Seats[i].Symbol : i & 7;
            return i < _local.Slots.Count ? _local.Slots[i].Symbol : i & 7;
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
            _rink = new RinkView(_map, _matchSim.T.WallThickness.ToFloat()); _rinkDrawn = _map;
        }

        /// <summary>The map the rink on screen was drawn from (the loaded one, or its wide-net copy).</summary>
        private MapDef _rinkDrawn;

        /// <summary>2x Goal Size (a chaos card): draw the rink again when the nets on screen change size.</summary>
        private void RinkNets(GameState b)
        {
            if (_rink == null || _sim == null || b == null) return;
            var want = _sim.MapFor(b);
            if (_rinkDrawn == null) _rinkDrawn = _map;
            if (want == _rinkDrawn) return;
            Destroy(_rink.Root);
            _rink = new RinkView(want, _sim.T.WallThickness.ToFloat());
            _rinkDrawn = want;
        }

        /// <summary>The name over player <paramref name="i"/>: their online name, yours (then P2, P3...) on this PC, or Bot.</summary>
        private string NameOf(int i)
        {
            // the name goes with the person: over the bot teammate they swapped onto, and "Bot" over their own player meanwhile
            if (_state != null && i >= 0 && i < _state.Players.Length && _state.Phase != Phase.Picking)
            {
                var d = Simulation.DriverOf(_state, i);
                if (d < 0) return "Bot";
                i = d;
            }
            return SeatName(i);
        }

        /// <summary>The name of player <paramref name="i"/>'s own seat.</summary>
        private string SeatName(int i)
        {
            if (_mode == Mode.Replay && _viewer.Replay != null) { var c = ReplayCast(); return c != null && i < c.Names.Length ? c.Names[i] : "Bot"; }
            if (_mode == Mode.Online && _session != null) return i < _session.Seats.Count ? _session.Seats[i].Name : "Bot";
            if (i >= _local.Slots.Count) return "";
            var s = _local.Slots[i];
            if (!string.IsNullOrEmpty(s.Name)) return s.Name;
            if (s.Demo) return "Bot";
            for (var k = 0; k < i; k++) if (!_local.Slots[k].Demo) return "P" + (i + 1);
            return Settings.PlayerName;
        }

        /// <summary>
        /// Goals blown up so far in what's shown live (the scored total), and whether the replay on screen has had its
        /// explosion: a frame often covers two ticks or more (120 a second against 60 fps), so the goal is noticed on
        /// the first frame that shows it, not only when that frame's two ticks straddle it.
        /// </summary>
        private int _boomGoals;
        private bool _replayBoomed;

        /// <summary>
        /// How much of a Double Agent's own colour shows (0: all the other team's, 1: their own), from the sim's disguise
        /// clock (blended between the two ticks on screen), so every PC shows the same.
        /// </summary>
        private float DisguiseShown(PlayerState pa, PlayerState pb, float t)
        {
            if (!pb.Disguised) return 1f;
            var time = pa.Disguised ? Mathf.Lerp(pa.DisguiseTime.ToFloat(), pb.DisguiseTime.ToFloat(), t) : pb.DisguiseTime.ToFloat();
            return Mathf.Clamp01((time - _sim.T.DisguiseHold.ToFloat()) / Mathf.Max(0.01f, _sim.T.DisguiseFade.ToFloat()));
        }

        private readonly List<int> _seenLaunches = new List<int>(), _seenRespawns = new List<int>();
        private readonly List<bool> _seenRaging = new List<bool>();
        // knock-out booms waiting for the flight to reach the boards: (when, x, y)
        private readonly List<Vector3> _launchBooms = new List<Vector3>();
        /// <summary>How fast a knocked-out player is shown flying (units a second).</summary>
        private const float LaunchSpeed = 80f;

        /// <summary>
        /// Knock-outs, respawns and rages for player <paramref name="i"/>: the blast and its sound as they go out, the
        /// pillar of light and its sound as they come back, the roar as a rage starts. (Counters in the state: a rewind
        /// that takes one back just catches up, so nothing goes off twice.)
        /// </summary>
        private void KnockOutEffects(int i, PlayerState pa, PlayerState pb, Vector2 pos, float radius, float t)
        {
            while (_seenLaunches.Count <= i) { _seenLaunches.Add(-1); _seenRespawns.Add(-1); _seenRaging.Add(false); }
            if (_seenLaunches[i] >= 0 && pb.Launches > _seenLaunches[i])
            {
                // the blast (and its boom) when the flight leaves the rink
                var delay = (V.Of(pb.OutAt) - V.Of(pb.OutFrom)).magnitude / LaunchSpeed;
                _hud.Blasts.Add(new Hud.Blast { World = V.Of(pb.OutAt), Dir = V.Of(pb.OutDir), Colour = Palette.Team(pb.Team), At = Clock.Now + delay, Seed = i * 7919 + pb.Launches });
                _launchBooms.Add(new Vector3(Clock.Now + delay, pb.OutAt.X.ToFloat(), pb.OutAt.Y.ToFloat()));
                Sound.Play(Sfx.BigHit, 1f, 0.8f, Pan(pos));
            }
            for (var k = _launchBooms.Count - 1; k >= 0; k--)
                if (Clock.Now >= _launchBooms[k].x)
                {
                    Sound.Play(Sfx.Launch, 0.95f, Random.Range(0.97f, 1.03f), Pan(new Vector2(_launchBooms[k].y, _launchBooms[k].z)));
                    _shake = Mathf.Max(_shake, 0.45f);
                    _launchBooms.RemoveAt(k);
                }
            if (_seenRespawns[i] >= 0 && pb.Respawns > _seenRespawns[i])
                Sound.Play(Sfx.Respawn, 0.75f, 1f, Pan(pos));
            var raging = pb.RageTime > Fix.Zero;
            if (raging && !_seenRaging[i] && _seenLaunches[i] >= 0)
            {
                Sound.Play(Sfx.RageUp, 0.3f, Random.Range(0.97f, 1.03f), Pan(pos));
                _shake = Mathf.Max(_shake, 0.3f);
            }
            if (pb.OutTime > Fix.Zero && !pb.Swallowed && IsLocalPerson(i)) _hud.OutCountdown = Mathf.Max(_hud.OutCountdown, pb.OutTime.ToFloat());
            _seenLaunches[i] = pb.Launches;
            _seenRespawns[i] = pb.Respawns;
            _seenRaging[i] = raging;
            // the pillar of light while they come back
            if (pb.RespawnTime > Fix.Zero && pb.OutTime <= Fix.Zero)
            {
                var left = pa.RespawnTime > Fix.Zero ? Mathf.Lerp(pa.RespawnTime.ToFloat(), pb.RespawnTime.ToFloat(), t) : pb.RespawnTime.ToFloat();
                _hud.Pillars.Add(new Hud.Pillar { World = new Vector3(pos.x, pos.y, 0f), Radius = radius, T = 1f - left / _sim.T.RespawnShow.ToFloat(), Colour = Palette.Team(pb.Team) });
            }
        }

        /// <summary>RAGE: how green player <paramref name="p"/> is (in over its first and out over its last third of a second).</summary>
        private float RageShown(PlayerState p)
        {
            if (p.RageTime <= Fix.Zero) return 0f;
            var left = p.RageTime.ToFloat();
            var gone = _sim.T.RageTime.ToFloat() - left;
            return Mathf.Clamp01(Mathf.Min(gone, left) / 0.3f);
        }

        /// <summary>
        /// RAGE: past half full the player twitches, more often the nearer they are (every 1.8 s down to 0.8 s): a quick
        /// shake to the side and a jerk of the head, timed by the match clock so every PC sees it alike. Only the body
        /// moves (<see cref="PlayerView.TwitchOffset"/>): the player's place, aim line, bars and the camera don't.
        /// </summary>
        private void Twitch(GameState b, int i, float t, float radius, Vector2 facing, out Vector2 offset, out float turnDeg)
        {
            offset = Vector2.zero; turnDeg = 0f;
            var p = b.Players[i];
            if (p.Stats.Rage <= 0 || p.RageTime > Fix.Zero || p.RageNeed <= 0) return;
            var full = Mathf.Clamp01(p.RageFill.ToFloat() / p.RageNeed);
            if (full < 0.5f) return;
            var near = Mathf.Clamp01((full - 0.5f) / 0.4f);
            var interval = Mathf.Lerp(1.8f, 0.8f, near);
            var now = (b.Tick + t) / _sim.T.TicksPerSecond + i * 0.37f;
            var phase = now % interval;
            const float length = 0.22f;
            if (phase > length) return;
            var k = phase / length;
            var shake = Mathf.Sin(k * Mathf.PI * 6f) * (1f - k);
            var side = facing.sqrMagnitude > 0.0001f ? new Vector2(-facing.y, facing.x).normalized : Vector2.up;
            offset = side * shake * radius * Mathf.Lerp(0.14f, 0.24f, near);
            turnDeg = shake * 12f;
        }

        private void Show(GameState a, GameState b, float t, bool replay = false)
        {
            _hud.Tags.Clear();
            _hud.Rages.Clear();
            _hud.Pillars.Clear();
            _hud.OutCountdown = 0f;
            _hud.Under.Clear();
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
                Vector2 twitchBy; float twitchTurn;
                Twitch(b, i, t, radius, facing, out twitchBy, out twitchTurn);
                _players[i].TwitchOffset = twitchBy;
                _players[i].TwitchTurn = twitchTurn;
                // knocked out: flying from where they were hit, out through the boards, then gone until the respawn
                var outNow = pb.OutTime > Fix.Zero;
                var flown = outNow ? (_sim.T.RespawnTime - pb.OutTime).ToFloat() * LaunchSpeed : 0f;
                var flight = outNow ? (V.Of(pb.OutAt) - V.Of(pb.OutFrom)).magnitude + 4f : 0f;
                if (outNow) pos = V.Of(pb.OutFrom) + V.Of(pb.OutDir) * flown;
                if (outNow && pb.Swallowed) flight = 0f; // swallowed by a Black Hole: just gone
                _players[i].Out = outNow;
                if (_players[i].Root.activeSelf != (!outNow || flown < flight)) _players[i].Root.SetActive(!outNow || flown < flight);
                KnockOutEffects(i, pa, pb, pos, radius, t);
                var full = Simulation.MaxDashes(pb).ToFloat();
                Vector2 hitAt;
                var hit = _players[i].Show(pos, facing, pb.DashCharge.ToFloat(), Mathf.RoundToInt(full), pb.Charge.ToFloat(), pb.Hits, pb.HitPower.ToFloat(), V.Of(pb.HitDir), out hitAt);
                _players[i].Disguise(DisguiseShown(pa, pb, t), RageShown(pb));
                // RAGE: the meter, for the person holding the card only
                if (pb.Stats.Rage > 0 && IsLocalPerson(i))
                    _hud.Rages.Add(new Hud.RageBar
                    {
                        World = new Vector3(pos.x, pos.y - radius - 0.32f, 0f),
                        Raging = pb.RageTime > Fix.Zero ? pb.RageTime.ToFloat() / _sim.T.RageTime.ToFloat() : 0f,
                        Cooling = pb.RageCooldown > Fix.Zero ? pb.RageCooldown.ToFloat() / _sim.T.RageCooldown.ToFloat() : 0f,
                    });
                var look = StyleOf(i);
                _players[i].DashColour = Cosmetics.DashTint(Cosmetics.Dash(look), Palette.Team(pb.Team));
                var hiddenNow = CardEffects(i, pb, pos);
                if (!hiddenNow && !outNow)
                {
                    if (i >= _dashAcc.Length) System.Array.Resize(ref _dashAcc, i + 8);
                    var dashing = Mathf.Clamp01(pb.DashTime.ToFloat() / Mathf.Max(0.01f, _sim.T.DashHitTime.ToFloat()));
                    _styleFx.DashTrail(Cosmetics.Dash(look), pos, V.Of(pb.DashDir), radius, dashing, Palette.Team(pb.Team), ref _dashAcc[i]);
                }
                // Slot Machine: the reels under them while they spin (and a moment after, to see what came up)
                _slots.Show(i, pb, pos, radius, _sim.T.SlotSpinTime.ToFloat(), hiddenNow);
                if (!hiddenNow && !outNow)
                {
                    // Double Agent: under a borrowed name from the other team until the colour has shifted back
                    // (but not to themselves: a person playing here sees their own name, unless someone on the other
                    // team is at this PC too and would see it)
                    var named = pb.Disguised && pb.DisguiseAs >= 0 && pb.DisguiseAs < b.Players.Length && !SeesOwnName(b, i) ? pb.DisguiseAs : i;
                    _hud.Tags.Add(new Hud.Tag { World = new Vector3(pos.x, pos.y + radius + 0.62f, 0f), Name = NameOf(named), Team = pb.Team, Owner = i });
                    _hud.Under.Add(new Vector4(pos.x, pos.y, radius, i));
                }
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
            _slots.Trim(b.Players.Length);
            if (_seenShocks.Count > b.Players.Length)
            {
                _seenShocks.RemoveRange(b.Players.Length, _seenShocks.Count - b.Players.Length);
                _seenRefreshes.RemoveRange(b.Players.Length, _seenRefreshes.Count - b.Players.Length);
                if (_seenSwaps.Count > b.Players.Length) _seenSwaps.RemoveRange(b.Players.Length, _seenSwaps.Count - b.Players.Length);
            }
            _shots.Show(b, _sim.T.ShotRadius.ToFloat());
            _pylons.Show(b, _sim.T.PylonRadius.ToFloat());
            _rocks.Show(b, _sim.T.RockRadius.ToFloat(), SymbolOf, i => Palette.Team(b.Players[i].Team), IsLocalPerson, _sim.T.RockFlash.ToFloat());
            _ice.Show(b, _sim.T.IceRadius.ToFloat());
            _stones.Show(b, _sim.T.WallThickness.ToFloat() * 0.5f, _sim.T.StoneLife.ToFloat());
            _portals.Show(b, _sim.Map, _sim.T.PortalLength.ToFloat(), _sim.T.WallThickness.ToFloat() * 0.5f, _sim.T.PortalShotRadius.ToFloat());
            // a puck that just jumped (face-off) shouldn't slide across the rink
            var pa2 = V.Of(a.PuckPos); var pb2 = V.Of(b.PuckPos);
            // a goal: the puck blows up in the net, and stays gone until the next face-off
            var boom = false;
            if (replay)
            {
                if (b.Phase != Phase.Scored) _replayBoomed = false;
                else if (!_replayBoomed) { _replayBoomed = true; boom = true; }
            }
            else
            {
                var goals = b.Score[0] + b.Score[1];
                if (b.Phase != Phase.Scored && goals < _boomGoals) _boomGoals = goals; // a new match (or a score fixed down)
                if (b.Phase == Phase.Scored && goals > _boomGoals) { _boomGoals = goals; boom = true; }
                _replayBoomed = false;
            }
            if (boom)
            {
                // the scorer's own goal explosion (Profile); an own goal gets the usual one
                var boomLook = b.LastGoalBy >= 0 && b.LastGoalBy < b.Players.Length ? Cosmetics.Boom(StyleOf(b.LastGoalBy)) : 0;
                _styleFx.Goal(boomLook, pb2, b.LastScorer >= 0 ? Palette.Team(b.LastScorer) : Color.white);
                _shake = Mathf.Max(_shake, 0.7f);
            }
            _puck.Visible(b.Phase != Phase.Scored);
            // Invisible Shot: hidden from a screen with only the other team on it; an outline for the shooter's
            // team (and in replays, so everyone can follow it)
            var ghost = 0;
            if (b.PuckHidden > Fix.Zero && b.PuckHiddenTeam >= 0 && b.Carrier < 0)
            {
                bool red, blue;
                LocalTeams(out red, out blue);
                var mine = b.PuckHiddenTeam == 0 ? red : blue;
                var theirs = b.PuckHiddenTeam == 0 ? blue : red;
                ghost = theirs && !mine && !_hud.Replay ? 2 : 1;
            }
            _puck.Ghost(ghost);
            var puckAt = (pb2 - pa2).sqrMagnitude > 4f ? pb2 : Vector2.Lerp(pa2, pb2, t);
            _puck.Look(_rink != null ? _rink.Dress : null);
            if (_rink != null) _rink.Settle();
            RefreshArena();
            _puck.Show(puckAt, b.PuckTeam);
            // anything half way through a portal: cut at one, coming out of the other
            var wallHalf = _sim.T.WallThickness.ToFloat() * 0.5f;
            SwapTrails(b);
            _split.Begin();
            for (var i = 0; i < b.Players.Length && i < _players.Count; i++) _split.Show(_players[i].Root, b.Players[i].PortalVia, b, wallHalf);
            _split.Show(_puck.Root, b.PuckPortal, b, wallHalf);
            for (var k = 0; k < b.Rocks.Length; k++) _split.Show(_rocks.At(k), b.Rocks[k].Carrier < 0 ? b.Rocks[k].PortalVia : 0, b, wallHalf);
            _split.End();
            if (b.Phase != Phase.Scored && ghost != 2) _hud.Under.Add(new Vector4(puckAt.x, puckAt.y, _sim.T.PuckRadius.ToFloat(), -1));
            if (InMatch) MatchSounds(b); // the bots playing behind the menus are silent
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
            RinkNets(b);
