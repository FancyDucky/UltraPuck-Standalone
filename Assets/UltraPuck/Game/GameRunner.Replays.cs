using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UltraPuck.Core;
using UltraPuck.Core.Net;
using UnityEngine;

namespace UltraPuck.Game
{
    /// <summary>
    /// Profile looks (who wears what) and match replays: every match is written down as it's played; after the game
    /// it can be saved (Profile > Replays), watched again, and clips of it saved as MP4s or copied to the clipboard.
    /// </summary>
    internal sealed partial class GameRunner
    {
        public static string ReplaysFolder { get { return Path.Combine(Application.persistentDataPath, "Replays"); } }

        /// <summary>The last <see cref="HistoryKept"/> matches, kept by themselves (screenshot runs keep theirs in the screenshot folder).</summary>
        public static string HistoryFolder { get { return ShotDir != null ? Path.Combine(ShotDir, "History") : Path.Combine(Application.persistentDataPath, "History"); } }
        public const int HistoryKept = 10;

        /// <summary>Where the match just played went in the history (to copy into the saved replays).</summary>
        private string _finishedPath;

        /// <summary>Where clips are saved: Videos/Ultra Puck (Windows), ~/Videos/Ultra Puck (Linux).</summary>
        public static string ClipsFolder
        {
            get
            {
                var videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
                if (string.IsNullOrEmpty(videos)) videos = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Videos");
                return Path.Combine(videos, "Ultra Puck");
            }
        }

        // ------------------------------------------------------------------ looks

        /// <summary>The Profile > Edit character page is open: the bots behind it wear this PC's look, to show it off.</summary>
        public bool PreviewLooks;

        /// <summary>The first person (not a bot) playing on this PC: they wear this PC's look.</summary>
        private int FirstPerson()
        {
            for (var k = 0; k < _local.Slots.Count; k++) if (!_local.Slots[k].Demo) return k;
            return -1;
        }

        /// <summary>Player <paramref name="i"/>'s look (see <see cref="Cosmetics"/>): theirs online, this PC's for the first person here, plain for the rest.</summary>
        private int StyleOf(int i)
        {
            // (a replay keeps the looks as they were shown: the Developer title was checked then)
            if (_mode == Mode.Replay && _viewer.Replay != null) { var c = ReplayCast(); return c != null && i < c.Styles.Length ? c.Styles[i] : 0; }
            if (_mode == Mode.Online && _session != null) return Cosmetics.Allowed(_session.StyleOf(i), _session.AccountOf(i));
            if (_mode == Mode.Menu) return PreviewLooks ? Cosmetics.Mine : 0;
            return i >= 0 && i == FirstPerson() ? Cosmetics.Mine : 0;
        }

        /// <summary>Player <paramref name="i"/>'s Steam account, for their profile picture (0: none, a bot, or a replay).</summary>
        private ulong AccountOf(int i)
        {
            if (_mode == Mode.Replay) return 0;
            if (_mode == Mode.Online && _session != null) return _session.AccountOf(i);
            return i >= 0 && i == FirstPerson() ? SteamNet.MyId : 0;
        }

        /// <summary>Profile page: set off this PC's goal explosion where it can be seen, to the right of the menu.</summary>
        public void PreviewBoom()
        {
            if (_cam == null) return;
            // the last one stops first, so they don't pile up while flicking through them
            _styleFx.Clear();
            _effects.Finish();
            _styleFx.Goal(Cosmetics.MyBoom, PreviewSpot(), Palette.Team(0));
            _shake = Mathf.Max(_shake, 0.4f);
        }

        /// <summary>Where the Profile page shows things off: to the right of the menu.</summary>
        private Vector2 PreviewSpot()
        {
            var w = _cam.ScreenToWorldPoint(new Vector3(Screen.width * 0.7f, Screen.height * 0.46f, Mathf.Abs(_cam.transform.position.z)));
            return new Vector2(w.x, w.y);
        }

        // ------------------------------------------------------------------ the dash trail preview

        /// <summary>Profile > Edit character, on the Dash trail row: a big player dashing back and forth over a dimmed screen.</summary>
        public bool PreviewDash;
        private PlayerView _dashView;
        private StyleFx _dashFx;
        private GameObject _dashDim;
        private float _dashClock, _dashTrailAcc, _dashDimShown;
        private int _dashShownStyle = -1;
        private const float DashPreviewRadius = 1.5f, DashEvery = 2f, DashFor = 0.4f, DashAcross = 6f;
        private const int DashPreviewOrder = 60;

        private void DashPreview()
        {
            var on = PreviewDash && _mode == Mode.Menu && _cam != null && !_menus.IsOpen;
            _dashDimShown = Mathf.MoveTowards(_dashDimShown, on ? 1f : 0f, Clock.Dt * 5f);
            if (_dashView == null)
            {
                if (!on) return;
                _dashView = new PlayerView(0, DashPreviewRadius, _entities, 0);
                // over everything else on the rink (and the dimming)
                foreach (var rend in _dashView.Root.GetComponentsInChildren<Renderer>(true)) rend.sortingOrder += DashPreviewOrder;
                _dashFx = new StyleFx(_entities, _effects) { OrderBoost = DashPreviewOrder };
                _dashDim = Draw.Make("PreviewDim", _entities, Draw.Rect(new Vector2(-500f, -500f), new Vector2(500f, 500f), Color.black), DashPreviewOrder - 2).gameObject;
                _dashClock = 0f;
            }
            Draw.Recolor(_dashDim.GetComponent<MeshFilter>().sharedMesh, new Color(0f, 0f, 0f, 0.62f * _dashDimShown));
            if (_dashDim.activeSelf != _dashDimShown > 0f) _dashDim.SetActive(_dashDimShown > 0f);
            if (_dashView.Root.activeSelf != on) _dashView.Root.SetActive(on);
            if (!on) { _dashFx.Clear(); _dashClock = 0f; return; }
            // another trail picked: the old one's bits go at once
            if (Cosmetics.MyDash != _dashShownStyle) { _dashFx.Clear(); _dashShownStyle = Cosmetics.MyDash; }
            // across and back: a dash every couple of seconds, each the other way
            _dashClock += Clock.Dt;
            var round = Mathf.FloorToInt(_dashClock / DashEvery);
            var t = _dashClock - round * DashEvery;
            var right = round % 2 == 0;
            var dir = right ? Vector2.right : Vector2.left;
            var from = PreviewSpot() + Vector2.up * 1.5f + dir * (-DashAcross / 2f);
            var k = Ui.EaseOut(Mathf.Clamp01(t / DashFor));
            var pos = from + dir * (DashAcross * k);
            var dash = t < DashFor ? 1f - t / DashFor : 0f;
            var team = Palette.Team(0);
            _dashView.DashColour = Cosmetics.DashTint(Cosmetics.MyDash, team);
            Vector2 hitAt;
            _dashView.Show(pos, dir, 1f, 1, 0f, 0, 0f, Vector2.zero, out hitAt);
            _dashView.Status(false, false, false, false, null, false, dash, false, false, dir);
            _dashFx.DashTrail(Cosmetics.MyDash, pos, dir, DashPreviewRadius, dash, team, ref _dashTrailAcc);
            _dashFx.Update();
        }

        // ------------------------------------------------------------------ recording a match

        private MatchReplay.Recorder _recording;
        /// <summary>The match just played (finished at game over), and whether it has been saved.</summary>
        private MatchReplay _finished;
        private bool _finishedSaved;

        private void BeginRecording()
        {
            DropRecording();
            _finished = null;
            _finishedSaved = false;
            if (ShotDir != null && !_shotRecord) return; // screenshot runs don't record (except the replay scene)
            try
            {
                var meta = new MatchReplay
                {
                    GameVersion = Updater.Current,
                    NetVersion = NetSession.Version,
                    WhenUtcTicks = DateTime.UtcNow.Ticks,
                    MapFile = _mapFiles[_mapIndex],
                    MapName = MapName(_mapIndex),
                    Rules = _mode == Mode.Online && _session != null && _session.LobbyRules != null ? _session.LobbyRules : PackRules(),
                    Colour0 = Palette.TeamColour(0),
                    Colour1 = Palette.TeamColour(1),
                    TicksPerSecond = _sim.T.TicksPerSecond,
                };
                _recording = new MatchReplay.Recorder(meta);
            }
            catch (Exception e) { Debug.LogWarning("Ultra Puck: can't record this match: " + e.Message); _recording = null; }
        }

        private void DropRecording()
        {
            if (_recording != null) _recording.Abandon();
            _recording = null;
        }

        private void RecordFrame()
        {
            if (_recording == null || _state == null) return;
            _recording.Add(_state, CastNow(), Sound.SongNow, Sound.SongAt);
            if (_state.Phase != Phase.GameOver) return;
            // the end: everything written so far is the replay
            try { _finished = _recording.Finish(); }
            catch (Exception e) { Debug.LogWarning("Ultra Puck: " + e.Message); _finished = null; }
            _recording = null;
            _finishedSaved = false;
            _finishedPath = null;
            if (_finished != null) KeepInHistory(_finished);
        }

        /// <summary>Into the match history; only the newest <see cref="HistoryKept"/> stay.</summary>
        private void KeepInHistory(MatchReplay rep)
        {
            try
            {
                Directory.CreateDirectory(HistoryFolder);
                _finishedPath = Path.Combine(HistoryFolder, DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + " " + Safe(rep.MapName) + MatchReplay.Extension);
                rep.Save(_finishedPath);
                var all = ReplayFiles(HistoryFolder);
                for (var k = HistoryKept; k < all.Count; k++) File.Delete(all[k]);
            }
            catch (Exception e) { Debug.LogWarning("Ultra Puck: couldn't keep the match in the history: " + e.Message); _finishedPath = null; }
        }

        /// <summary>A match from the history into the saved replays (kept for good). Returns false if it couldn't.</summary>
        public static bool SaveFromHistory(string path)
        {
            try
            {
                Directory.CreateDirectory(ReplaysFolder);
                var to = Path.Combine(ReplaysFolder, Path.GetFileName(path));
                if (!File.Exists(to)) File.Copy(path, to);
                return true;
            }
            catch (Exception e) { Toasts.Show("Couldn't save the replay: " + e.Message, 6f); return false; }
        }

        /// <summary>Who's who right now, for the replay.</summary>
        private MatchReplay.Cast CastNow()
        {
            var n = _state.Players.Length;
            var c = new MatchReplay.Cast { Names = new string[n], Symbols = new int[n], Styles = new int[n], Teams = new int[n] };
            for (var i = 0; i < n; i++) { c.Names[i] = SeatName(i); c.Symbols[i] = SymbolOf(i); c.Styles[i] = StyleOf(i); c.Teams[i] = _state.Players[i].Team; }
            return c;
        }

        /// <summary>Game over: save the match that was just played to Profile > Replays.</summary>
        private void SaveLastReplay()
        {
            if (_finished == null || _finishedSaved) return;
            try
            {
                if (_finishedPath != null && File.Exists(_finishedPath)) { if (!SaveFromHistory(_finishedPath)) return; }
                else
                {
                    Directory.CreateDirectory(ReplaysFolder);
                    var name = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + " " + Safe(_finished.MapName) + MatchReplay.Extension;
                    _finished.Save(Path.Combine(ReplaysFolder, name));
                }
                _finishedSaved = true;
                Sound.Select();
                Toasts.Show("Replay saved. Watch it in Profile > Replays.", 4f);
            }
            catch (Exception e)
            {
                Sound.Deny();
                Toasts.Show("Couldn't save the replay: " + e.Message, 6f);
            }
        }

        private static string Safe(string s)
        {
            if (string.IsNullOrEmpty(s)) return "Match";
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '-');
            return s;
        }

        // ------------------------------------------------------------------ watching one

        private int _beforeReplay0, _beforeReplay1;

        /// <summary>The replays in <paramref name="folder"/>, newest first.</summary>
        public static List<string> ReplayFiles(string folder)
        {
            var list = new List<string>();
            try
            {
                if (Directory.Exists(folder)) list.AddRange(Directory.GetFiles(folder, "*" + MatchReplay.Extension));
            }
            catch (Exception e) { Debug.LogWarning("Ultra Puck: can't list replays: " + e.Message); }
            list.Sort((a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
            return list;
        }

        public void OpenReplay(string path)
        {
            _replayFrom = _mainMenu.Current == MainMenu.Page.History ? MainMenu.Page.History : MainMenu.Page.Saved;
            MatchReplay rep;
            try { rep = MatchReplay.Load(path); }
            catch (Exception e) { Sound.Deny(); Toasts.Show("Couldn't open that replay: " + e.Message, 6f); return; }
            var map = _mapFiles.IndexOf(rep.MapFile);
            if (map < 0) { Sound.Deny(); Toasts.Show("That replay is on a rink this version doesn't have (" + rep.MapName + ").", 6f); return; }
            Sound.Select();
            UseMap(map, false);
            _menuRink = true; // (the rink you picked comes back when you go to play)
            _sim = new Simulation(_map, RulesTuning(rep.Rules, false), _cards);
            _beforeReplay0 = Palette.TeamColour(0);
            _beforeReplay1 = Palette.TeamColour(1);
            Palette.SetTeams(rep.Colour0, rep.Colour1);
            _mainMenu.Show(MainMenu.Page.None);
            _mode = Mode.Replay;
            _boomGoals = 0;
            _replayBoomed = false;
            _heardTick = int.MinValue;
            _styleFx.Clear();
            _viewer.Open(rep, path);
            _state = _viewer.B;
            UpdateCamera(true, true);
        }

        private void CloseReplay()
        {
            _viewer.Close();
            _styleFx.Clear();
            Palette.SetTeams(_beforeReplay0, _beforeReplay1);
            _mode = Mode.Menu;
            _sim = _matchSim;
            _mainMenu.Show(_replayFrom);
        }

        /// <summary>The list a replay was opened from (back there when it's closed).</summary>
        private MainMenu.Page _replayFrom = MainMenu.Page.Saved;

        private MatchReplay.Cast ReplayCast()
        {
            var r = _viewer.Replay;
            return r == null ? null : r.CastAt(r.FrameAt(_viewer.Tick));
        }

        private void ReplayUpdate()
        {
            if (_viewer.Replay == null) return;
            if (_viewer.Exporting) _viewer.Advance(1f / ClipFps);
            else _viewer.Frame(_local);
            if (_viewer.Replay == null || _mode != Mode.Replay) return; // closed
            if (_viewer.Jumped) { _styleFx.Clear(); _heardTick = int.MinValue; }
            _state = _viewer.B;
            Show(_viewer.A, _viewer.B, _viewer.T, true);
            if (_viewer.Audible) MatchSounds(_viewer.B);
        }

        // ------------------------------------------------------------------ clips

        private const int ClipFps = 60;
        private bool _exporting;

        /// <summary>
        /// Save the replay from <paramref name="from"/> to <paramref name="to"/> (ticks) as an MP4: it's played through at
        /// normal speed with the game's clock stepped exactly a sixtieth of a second a frame (however long the frame
        /// really takes), each picture is read back and handed to the encoder. Esc / B stops it.
        /// </summary>
        private IEnumerator ExportClip(float from, float to, bool copy)
        {
            if (_exporting) yield break;
            _exporting = true;
            var rep = _viewer.Replay;
            var folder = _shotClipFolder ?? ClipsFolder;
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "Ultra Puck " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".mp4");
            // the screen's own size up to 1440p (even sizes, the screen's shape)
            var outH = Mathf.Min(Screen.height, 1440);
            var outW = Mathf.RoundToInt(outH * Screen.width / (float)Screen.height);
            outW -= outW % 2; outH -= outH % 2;
            string problem;
            var writer = ClipWriter.Open(path, outW, outH, ClipFps, ClipMixer.Rate, ClipMixer.Channels, out problem);
            if (writer == null)
            {
                _exporting = false;
                Sound.Deny();
                Toasts.Show("Couldn't save the clip: " + problem, 8f);
                yield break;
            }

            var grab = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
            var scaled = new RenderTexture(outW, outH, 0, RenderTextureFormat.ARGB32);
            var read = new Texture2D(outW, outH, TextureFormat.RGBA32, false);
            var vsync = QualitySettings.vSyncCount;
            var target = Application.targetFrameRate;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            Time.captureDeltaTime = 1f / ClipFps; // the game's clock (and its effects) run at the clip's pace
            Clock.BeginFixed();
            // the sound: the game's effects (no music), mixed a frame's worth at a time in step with the pictures
            var sound = writer.HasSound;
            var mixer = sound ? new ClipMixer(ClipFps) : null;
            Sound.Heard.Clear();
            Sound.ClipMode = sound;

            _viewer.Exporting = true;
            _viewer.Seek(from);
            _viewer.PlayForExport();
            _styleFx.Clear();
            _heardTick = int.MinValue;
            var frames = 0;
            var cancelled = false;
            var started = Time.realtimeSinceStartup;
            Debug.Log("Ultra Puck: saving a clip, " + ((to - from) / rep.TicksPerSecond).ToString("0.0") + " s at " + outW + "x" + outH + " to " + path);
            while (_viewer.Replay == rep && _viewer.Tick < to)
            {
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshotIntoRenderTexture(grab);
                // the screen comes out upside down where pictures start at the top (Direct3D, Metal, Vulkan)
                if (SystemInfo.graphicsUVStartsAtTop) Graphics.Blit(grab, scaled, new Vector2(1f, -1f), new Vector2(0f, 1f));
                else Graphics.Blit(grab, scaled);
                var keep = RenderTexture.active;
                RenderTexture.active = scaled;
                read.ReadPixels(new Rect(0, 0, outW, outH), 0, 0, false);
                RenderTexture.active = keep;
                if (!writer.Add(read.GetRawTextureData())) break;
                if (sound)
                {
                    // the song that was playing at this moment of the match (if wanted)
                    float songAt = 0f;
                    var song = Settings.MusicInClips ? rep.SongAt(_viewer.Tick, out songAt) : "";
                    writer.AddSound(mixer.Frame(song, songAt, Sound.MusicMix));
                }
                frames++;
                if (frames == 2) Debug.Log("Ultra Puck: clip frame time " + Clock.Dt.ToString("0.0000") + " (game clock), real " + Time.unscaledDeltaTime.ToString("0.0000"));
                var kb = UnityEngine.InputSystem.Keyboard.current;
                var stop = kb != null && kb.escapeKey.wasPressedThisFrame;
                foreach (var g in UnityEngine.InputSystem.Gamepad.all) stop |= g.buttonEast.wasPressedThisFrame;
                if (stop) { cancelled = true; break; }
            }

            Sound.ClipMode = false;
            Sound.Heard.Clear();
            if (mixer != null) mixer.Dispose();
            Clock.EndFixed();
            Time.captureDeltaTime = 0f;
            QualitySettings.vSyncCount = vsync;
            Application.targetFrameRate = target;
            _viewer.Exporting = false;
            var failed = writer.Finish();
            writer.Dispose();
            Destroy(grab); Destroy(scaled); Destroy(read);
            _exporting = false;
            Debug.Log("Ultra Puck: clip " + frames + " frames in " + (Time.realtimeSinceStartup - started).ToString("0.0") + " s" + (failed != null ? ", failed: " + failed : "") + (cancelled ? " (stopped)" : ""));

            if (cancelled || failed != null || frames == 0)
            {
                try { if (File.Exists(path)) File.Delete(path); } catch (Exception) { }
                if (cancelled) Toasts.Show("Clip not saved (stopped).", 3f);
                else { Sound.Deny(); Toasts.Show("Couldn't save the clip: " + (failed ?? "no frames"), 8f); }
                yield break;
            }
            Sound.Select();
            if (copy && ClipWriter.CopyFile(path)) Toasts.Show("Clip copied: paste it anywhere (it's also in Videos > Ultra Puck).", 6f);
            else if (copy) Toasts.Show("Couldn't copy the clip, but it's saved in Videos > Ultra Puck.", 6f);
            else Toasts.Show("Clip saved to Videos > Ultra Puck.", 5f);
        }

        // ------------------------------------------------------------------ screenshots

        private bool _shotRecord;
        private string _shotClipFolder;

        /// <summary>Screenshot scenes "profile" (the Profile pages, every explosion and trail) and "replayview" (recording, watching, a clip).</summary>
        private IEnumerator ProfileShots(Func<string, bool> want, UnityEngine.InputSystem.Keyboard kb)
        {
            if (want("profile"))
            {
                GoMainMenu();
                Cosmetics.ShotLook = Cosmetics.Pack(4, 2, 1, 0, Cosmetics.DeveloperTitle);
                _mainMenu.Show(MainMenu.Page.Profile);
                yield return new WaitForSecondsRealtime(0.7f);
                yield return Shot("profile");
                _mainMenu.Show(MainMenu.Page.Character);
                yield return new WaitForSecondsRealtime(2.5f);
                yield return Shot("profile-character");
                _mainMenu.FocusRow(4);
                yield return new WaitForSecondsRealtime(0.4f);
                yield return Shot("profile-title");
                _mainMenu.FocusRow(1);
                for (var k = 1; k < Cosmetics.BoomNames.Length; k++)
                {
                    Cosmetics.ShotLook = Cosmetics.Pack(4, k % Cosmetics.BannerNames.Length, k, 0, k % Cosmetics.TitleNames.Length);
                    yield return new WaitForSecondsRealtime(1.2f);
                    PreviewBoom();
                    yield return new WaitForSecondsRealtime(k == 4 ? 0.7f : 0.3f);
                    yield return Shot("boom-" + k + "-" + Cosmetics.BoomNames[k].Replace(" ", ""));
                    if (k == 4) { yield return new WaitForSecondsRealtime(0.6f); yield return Shot("boom-4-BlackHole-later"); }
                }
                // the dash preview: the big player, a moment into a dash
                _mainMenu.FocusRow(0);
                for (var k = 1; k < Cosmetics.DashNames.Length; k++)
                {
                    Cosmetics.ShotLook = Cosmetics.Pack(k, (k + 5) % Cosmetics.BannerNames.Length, 0);
                    _dashClock = DashEvery - 0.05f;
                    yield return new WaitForSecondsRealtime(0.25f);
                    yield return Shot("dash-" + k + "-" + Cosmetics.DashNames[k]);
                }
                for (var k = 0; k < Cosmetics.BannerNames.Length; k++)
                {
                    Cosmetics.ShotLook = Cosmetics.Pack(0, k, 0, k == 0 ? 0 : (k * 5) % Cosmetics.BannerColourNames.Length);
                    yield return new WaitForSecondsRealtime(0.3f);
                    yield return Shot("banner-" + k);
                }
                Cosmetics.ShotLook = -1;
                _mainMenu.Show(MainMenu.Page.Main);
            }

            if (want("replayview"))
            {
                // a short match, recorded and saved in the screenshot folder (not the player's replays), then watched
                Cosmetics.ShotLook = Cosmetics.Pack(4, 4, 2, 0, Cosmetics.DeveloperTitle);
                _local.Slots.Clear();
                _local.Slots.Add(new LocalPlayers.Slot { Device = kb, Team = 0, Colour = 0 });
                _local.Slots.Add(new LocalPlayers.Slot { Team = 1, Colour = 1, Demo = true });
                _local.Slots.Add(new LocalPlayers.Slot { Team = 0, Colour = 0, Demo = true });
                _local.Slots.Add(new LocalPlayers.Slot { Team = 1, Colour = 1, Demo = true });
                _local.AssignTeams();
                _mainMenu.Show(MainMenu.Page.None);
                _shotRecord = true;
                StartMatch(false);
                _shotRecord = false;
                yield return new WaitForSecondsRealtime(6f);
                _shotBoard = true;
                yield return new WaitForSecondsRealtime(0.2f);
                yield return Shot("replay-scoreboard");
                _shotBoard = false;
                var g = _map.Goals.Find(x => x.Team == 1);
                _state.PuckPos = new FixVec2((g.Min.X + g.Max.X) * Fix.Half, (g.Min.Y + g.Max.Y) * Fix.Half);
                _state.PuckVel = FixVec2.Zero;
                _state.Carrier = -1;
                _state.PassFrom = 0; _state.Touch1 = 0; _state.Touch2 = 2;
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Shot("replay-live-goal");
                yield return new WaitForSecondsRealtime(9f);
                _state.Phase = Phase.GameOver;
                _state.LastScorer = 0;
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Shot("replay-gameover");
                if (_finished == null) { Debug.LogWarning("Ultra Puck shots: no replay recorded"); GoMainMenu(); Cosmetics.ShotLook = -1; yield break; }
                var path = Path.Combine(ShotDir, "shots" + MatchReplay.Extension);
                _finished.Save(path);
                Debug.Log("Ultra Puck shots: replay " + _finished.Frames + " frames, " + new FileInfo(path).Length + " bytes, " + _finished.Markers.Count + " markers");
                GoMainMenu();
                _mainMenu.Show(MainMenu.Page.Replays);
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Shot("replays-menu");
                _mainMenu.Show(MainMenu.Page.History);
                yield return new WaitForSecondsRealtime(0.6f);
                yield return Shot("replays-history");
                OpenReplay(path);
                yield return new WaitForSecondsRealtime(1.5f);
                yield return Shot("replayview");
                var goal = _viewer.Replay.Markers.Find(m => m.Kind == MatchReplay.MarkerKind.Goal);
                var goalTick = _viewer.Replay.Ticks[Mathf.Clamp(goal.Frame, 0, _viewer.Replay.Frames - 1)];
                _viewer.Seek(goalTick - 60);
                yield return new WaitForSecondsRealtime(1.6f);
                yield return Shot("replayview-goal");
                _viewer.Seek(goalTick + 120);
                _viewer.StartClip();
                yield return new WaitForSecondsRealtime(0.4f);
                yield return Shot("replayview-clip");
                _shotClipFolder = ShotDir;
                yield return ExportClip(goalTick - 240, goalTick + 120, false);
                _shotClipFolder = null;
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Shot("replayview-saved");
                CloseReplay();
                GoMainMenu();
                Cosmetics.ShotLook = -1;
            }
        }

        /// <summary>Open a folder in the file manager.</summary>
        public static void OpenFolder(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                Application.OpenURL(new Uri(path).AbsoluteUri);
            }
            catch (Exception e) { Toasts.Show("Couldn't open the folder: " + e.Message, 5f); }
        }
    }
}
