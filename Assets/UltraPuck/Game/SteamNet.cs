using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Steamworks;
using UltraPuck.Core.Net;
using UnityEngine;

namespace UltraPuck.Game
{
    /// <summary>
    /// Steam: starting it with the game, and finding each other through Steam lobbies (host a game, invite friends
    /// from the Steam overlay, browse open games, join from your friends list). The game itself then talks over
    /// <see cref="SteamTransport"/>, through Valve's relay network, so nobody needs to forward ports.
    ///
    /// For now this uses Valve's shared test app (480, "Spacewar") via steam_appid.txt; a real Steam app id
    /// replaces it before release. Both players need the Steam client running and logged in (different accounts).
    /// </summary>
    internal static class SteamNet
    {
        public static bool Ready { get; private set; }
        /// <summary>Why Steam isn't available, for the screen.</summary>
        public static string Problem { get; private set; }

        private static bool _tried;
        private static Callback<GameLobbyJoinRequested_t> _joinRequested;
        private static Callback<LobbyEnter_t> _lobbyEntered;
        private static CallResult<LobbyCreated_t> _created;
        private static CallResult<LobbyMatchList_t> _listed;

        /// <summary>The Steam lobby we're in (hosting or joined), or nil.</summary>
        public static CSteamID Lobby { get; private set; }
        public static bool InLobby { get { return Ready && Lobby.IsValid() && Lobby != CSteamID.Nil; } }

        /// <summary>Set when a lobby we asked to join (or were invited to) has been entered: the host's Steam id to connect to.</summary>
        public static Action<CSteamID> OnJoined;
        /// <summary>Set when our hosted lobby is ready (invites work from then on).</summary>
        public static Action OnHosted;
        public static Action<string> OnFailed;

        public sealed class Listing { public CSteamID Id; public string Name; public int Players; }
        public static readonly List<Listing> Found = new List<Listing>();
        public static bool Searching { get; private set; }

        /// <summary>Lobby key that marks Ultra Puck lobbies (the test app is shared with many other games).</summary>
        private const string Key = "ultrapuck";

        public static void Init()
        {
            if (_tried) return;
            _tried = true;
            if (Environment.GetEnvironmentVariable("ULTRAPUCK_NOSTEAM") == "1") { Problem = "Steam is turned off (ULTRAPUCK_NOSTEAM)."; return; }
            try
            {
                if (!Packsize.Test() || !DllCheck.Test()) { Problem = "Steam files are missing from the game folder."; return; }
                string message;
                var result = SteamAPI.InitEx(out message);
                if (result != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
                {
                    Problem = result == ESteamAPIInitResult.k_ESteamAPIInitResult_NoSteamClient
                        ? "Steam isn't running. Start Steam and log in to play online through Steam."
                        : "Couldn't start Steam: " + message;
                    return;
                }
                Ready = true;
                StartedAt = UnityEngine.Time.realtimeSinceStartup;
                SteamNetworkingUtils.InitRelayNetworkAccess(); // get the relay network warmed up early
                _joinRequested = Callback<GameLobbyJoinRequested_t>.Create(r => Join(r.m_steamIDLobby));
                _lobbyEntered = Callback<LobbyEnter_t>.Create(Entered);
                _created = CallResult<LobbyCreated_t>.Create(Created);
                _listed = CallResult<LobbyMatchList_t>.Create(Listed);
                Debug.Log("Ultra Puck: Steam ready as " + SteamFriends.GetPersonaName() + " (overlay " + (SteamUtils.IsOverlayEnabled() ? "on" : "off") + ")");
            }
            catch (Exception ex)
            {
                Problem = "Couldn't start Steam (" + ex.GetType().Name + ").";
                Debug.LogWarning("Ultra Puck: " + ex);
            }
        }

        /// <summary>A lobby to join from the command line (when the game was started by accepting an invite).</summary>
        public static void CheckLaunchInvite()
        {
            if (!Ready) return;
            var args = Environment.GetCommandLineArgs();
            for (var k = 0; k + 1 < args.Length; k++)
            {
                ulong id;
                if (args[k] == "+connect_lobby" && ulong.TryParse(args[k + 1], out id)) Join(new CSteamID(id));
            }
        }

        public static void Update() { if (Ready) SteamAPI.RunCallbacks(); }

        public static void Shutdown()
        {
            if (!Ready) return;
            Leave();
            SteamAPI.Shutdown();
            Ready = false;
        }

        public static string Name { get { return Ready ? SteamFriends.GetPersonaName() : "Player"; } }

        // ------------------------------------------------------------------ lobbies

        public static void Host(int maxPlayers)
        {
            if (!Ready) return;
            Leave();
            _created.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePublic, maxPlayers));
        }

        private static void Created(LobbyCreated_t r, bool failed)
        {
            if (failed || r.m_eResult != EResult.k_EResultOK) { if (OnFailed != null) OnFailed("Steam couldn't make a lobby (" + r.m_eResult + ")."); return; }
            Lobby = new CSteamID(r.m_ulSteamIDLobby);
            SteamMatchmaking.SetLobbyData(Lobby, Key, NetSession.Version.ToString());
            SteamMatchmaking.SetLobbyData(Lobby, "name", Name + "'s game");
            SteamMatchmaking.SetLobbyData(Lobby, "players", "1");
            if (OnHosted != null) OnHosted();
        }

        /// <summary>Host: keep the lobby's listing up to date (players, and whether a match is on).</summary>
        public static void Describe(int players, bool playing)
        {
            if (!InLobby || SteamMatchmaking.GetLobbyOwner(Lobby) != SteamUser.GetSteamID()) return;
            SteamMatchmaking.SetLobbyData(Lobby, "players", players.ToString());
            SteamMatchmaking.SetLobbyData(Lobby, "playing", playing ? "1" : "0");
            SteamMatchmaking.SetLobbyJoinable(Lobby, !playing);
        }

        /// <summary>Opens the Steam overlay's invite list for our lobby.</summary>
        public static void Invite()
        {
            if (InLobby) SteamFriends.ActivateGameOverlayInviteDialog(Lobby);
        }

        /// <summary>
        /// The Steam overlay only works when Steam started the game (from the library), so for a game you start
        /// yourself it's usually off. The game has its own friends list for invites either way.
        /// </summary>
        public static bool OverlayWorks { get { return Ready && SteamUtils.IsOverlayEnabled(); } }

        public sealed class Friend { public CSteamID Id; public string Name; public bool Playing; }

        /// <summary>Steam friends who are online, the ones in this game first.</summary>
        public static List<Friend> OnlineFriends()
        {
            var list = new List<Friend>();
            if (!Ready) return list;
            var app = SteamUtils.GetAppID();
            var n = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
            for (var k = 0; k < n; k++)
            {
                var id = SteamFriends.GetFriendByIndex(k, EFriendFlags.k_EFriendFlagImmediate);
                if (SteamFriends.GetFriendPersonaState(id) == EPersonaState.k_EPersonaStateOffline) continue;
                FriendGameInfo_t game;
                var playing = SteamFriends.GetFriendGamePlayed(id, out game) && game.m_gameID.AppID() == app;
                list.Add(new Friend { Id = id, Name = SteamFriends.GetFriendPersonaName(id), Playing = playing });
            }
            list.Sort((a, b) => a.Playing != b.Playing ? (a.Playing ? -1 : 1) : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        /// <summary>Sends a friend an invite to our lobby (it shows up in their Steam chat; with the game open, accepting joins).</summary>
        public static bool InviteFriend(CSteamID friend) { return InLobby && SteamMatchmaking.InviteUserToLobby(Lobby, friend); }

        /// <summary>When Steam started up (to spot Steam Input taking a controller straight afterwards).</summary>
        public static float StartedAt { get; private set; }

        public static void Join(CSteamID lobby)
        {
            if (!Ready) return;
            Leave();
            SteamMatchmaking.JoinLobby(lobby);
        }

        private static void Entered(LobbyEnter_t r)
        {
            var lobby = new CSteamID(r.m_ulSteamIDLobby);
            if (r.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                if (OnFailed != null) OnFailed("Couldn't join that game (it may be full or already playing).");
                return;
            }
            Lobby = lobby;
            var owner = SteamMatchmaking.GetLobbyOwner(lobby);
            if (owner == SteamUser.GetSteamID()) return; // our own, just made
            if (OnJoined != null) OnJoined(owner);
        }

        public static void Leave()
        {
            if (Ready && Lobby.IsValid() && Lobby != CSteamID.Nil) SteamMatchmaking.LeaveLobby(Lobby);
            Lobby = CSteamID.Nil;
        }

        public static void Search()
        {
            if (!Ready) return;
            Searching = true;
            SteamMatchmaking.AddRequestLobbyListStringFilter(Key, NetSession.Version.ToString(), ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListStringFilter("playing", "0", ELobbyComparison.k_ELobbyComparisonEqual);
            SteamMatchmaking.AddRequestLobbyListResultCountFilter(30);
            _listed.Set(SteamMatchmaking.RequestLobbyList());
        }

        private static void Listed(LobbyMatchList_t r, bool failed)
        {
            Searching = false;
            Found.Clear();
            if (failed) return;
            for (var k = 0; k < r.m_nLobbiesMatching; k++)
            {
                var id = SteamMatchmaking.GetLobbyByIndex(k);
                int players;
                int.TryParse(SteamMatchmaking.GetLobbyData(id, "players"), out players);
                var name = SteamMatchmaking.GetLobbyData(id, "name");
                Found.Add(new Listing { Id = id, Name = string.IsNullOrEmpty(name) ? "Ultra Puck game" : name, Players = Math.Max(1, players) });
            }
        }
    }

    /// <summary>
    /// The game's packets over Steam's networking: peer-to-peer connections between Steam accounts, carried by
    /// Valve's relays (so they work behind any router, no port forwarding). The host listens; clients connect to
    /// the host's Steam id. Packets are sent unreliably (the game re-sends what matters itself).
    /// </summary>
    internal sealed class SteamTransport : ITransport
    {
        private readonly bool _host;
        private HSteamListenSocket _listen = HSteamListenSocket.Invalid;
        private HSteamNetPollGroup _group = HSteamNetPollGroup.Invalid;
        private readonly Dictionary<HSteamNetConnection, int> _ids = new Dictionary<HSteamNetConnection, int>();
        private readonly Dictionary<int, HSteamNetConnection> _conns = new Dictionary<int, HSteamNetConnection>();
        private int _next = 1;
        private readonly Callback<SteamNetConnectionStatusChangedCallback_t> _status;
        private readonly IntPtr[] _messages = new IntPtr[64];
        private readonly Queue<KeyValuePair<int, byte[]>> _inbox = new Queue<KeyValuePair<int, byte[]>>();
        private readonly string _what;

        private SteamTransport(bool host, string what)
        {
            _host = host;
            _what = what;
            _status = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(StatusChanged);
        }

        public static SteamTransport Host()
        {
            var t = new SteamTransport(true, "hosting on Steam");
            t._listen = SteamNetworkingSockets.CreateListenSocketP2P(0, 0, null);
            t._group = SteamNetworkingSockets.CreatePollGroup();
            return t;
        }

        public static SteamTransport Join(CSteamID host)
        {
            var t = new SteamTransport(false, "playing with " + SteamFriends.GetFriendPersonaName(host) + " on Steam");
            var who = new SteamNetworkingIdentity();
            who.SetSteamID(host);
            var c = SteamNetworkingSockets.ConnectP2P(ref who, 0, 0, null);
            t._ids[c] = 0;
            t._conns[0] = c;
            return t;
        }

        private void StatusChanged(SteamNetConnectionStatusChangedCallback_t s)
        {
            var state = s.m_info.m_eState;
            if (_host && state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting && s.m_info.m_hListenSocket == _listen)
            {
                if (SteamNetworkingSockets.AcceptConnection(s.m_hConn) != EResult.k_EResultOK) { SteamNetworkingSockets.CloseConnection(s.m_hConn, 0, "accept failed", false); return; }
                SteamNetworkingSockets.SetConnectionPollGroup(s.m_hConn, _group);
                var id = _next++;
                _ids[s.m_hConn] = id;
                _conns[id] = s.m_hConn;
            }
            else if (state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer ||
                     state == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally)
            {
                SteamNetworkingSockets.CloseConnection(s.m_hConn, 0, "closed", false);
                int id;
                if (_ids.TryGetValue(s.m_hConn, out id)) { _ids.Remove(s.m_hConn); _conns.Remove(id); }
            }
        }

        public void Send(int conn, byte[] data)
        {
            HSteamNetConnection c;
            if (!_conns.TryGetValue(conn, out c)) return;
            var ptr = Marshal.AllocHGlobal(data.Length);
            try
            {
                Marshal.Copy(data, 0, ptr, data.Length);
                long number;
                SteamNetworkingSockets.SendMessageToConnection(c, ptr, (uint)data.Length, Constants.k_nSteamNetworkingSend_UnreliableNoDelay, out number);
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }

        public bool Poll(out int conn, out byte[] data)
        {
            if (_inbox.Count == 0) Fetch();
            conn = -1; data = null;
            if (_inbox.Count == 0) return false;
            var m = _inbox.Dequeue();
            conn = m.Key; data = m.Value;
            return true;
        }

        private void Fetch()
        {
            int n;
            if (_host) n = SteamNetworkingSockets.ReceiveMessagesOnPollGroup(_group, _messages, _messages.Length);
            else
            {
                HSteamNetConnection c;
                if (!_conns.TryGetValue(0, out c)) return;
                n = SteamNetworkingSockets.ReceiveMessagesOnConnection(c, _messages, _messages.Length);
            }
            for (var k = 0; k < n; k++)
            {
                var msg = Marshal.PtrToStructure<SteamNetworkingMessage_t>(_messages[k]);
                int id;
                if (_ids.TryGetValue(msg.m_conn, out id))
                {
                    var bytes = new byte[msg.m_cbSize];
                    Marshal.Copy(msg.m_pData, bytes, 0, msg.m_cbSize);
                    _inbox.Enqueue(new KeyValuePair<int, byte[]>(id, bytes));
                }
                SteamNetworkingMessage_t.Release(_messages[k]);
            }
        }

        public void Drop(int conn)
        {
            HSteamNetConnection c;
            if (!_conns.TryGetValue(conn, out c)) return;
            SteamNetworkingSockets.CloseConnection(c, 0, "dropped", true);
            _conns.Remove(conn);
            _ids.Remove(c);
        }

        public string Describe() { return _what; }

        public void Dispose()
        {
            foreach (var c in _conns.Values) SteamNetworkingSockets.CloseConnection(c, 0, "bye", true);
            _conns.Clear(); _ids.Clear();
            if (_listen != HSteamListenSocket.Invalid) SteamNetworkingSockets.CloseListenSocket(_listen);
            if (_group != HSteamNetPollGroup.Invalid) SteamNetworkingSockets.DestroyPollGroup(_group);
            _status.Dispose();
        }
    }
}
