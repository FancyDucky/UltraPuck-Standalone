using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace UltraPuck.Core.Net
{
    /// <summary>
    /// Moves packets between this PC and others. Packets may be lost, duplicated or arrive out of order; everything
    /// above copes with that. A connection is a small number: on the host, one per client; on a client, 0 is the host.
    /// Plain UDP for now (LAN, the same PC, or a virtual network like Tailscale); a Steam relay transport can
    /// implement the same thing later.
    /// </summary>
    public interface ITransport : IDisposable
    {
        void Send(int conn, byte[] data);
        /// <summary>The next packet that arrived, if any.</summary>
        bool Poll(out int conn, out byte[] data);
        /// <summary>Forget a connection (it left, or timed out).</summary>
        void Drop(int conn);
        /// <summary>A short description for the screen, e.g. "hosting on port 47777".</summary>
        string Describe();
    }

    /// <summary>UDP: the host listens on a port; clients send to the host's address.</summary>
    public sealed class UdpTransport : ITransport
    {
        public const int DefaultPort = 47777;

        private readonly Socket _socket;
        private readonly bool _host;
        private readonly Dictionary<EndPoint, int> _byEndpoint = new Dictionary<EndPoint, int>();
        private readonly Dictionary<int, EndPoint> _byConn = new Dictionary<int, EndPoint>();
        private int _nextConn = 1;
        // the biggest a UDP packet can be: a hand-over (the whole game, for someone joining part-way) runs to several KB,
        // and a packet bigger than the buffer is thrown away (it was 2048, so joining a big match never got through)
        private readonly byte[] _buffer = new byte[65536];
        private readonly int _port;

        private UdpTransport(bool host, int port)
        {
            _host = host;
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { Blocking = false };
            // Windows reports "connection reset" on UDP when a packet bounces; that must not break receiving
            try { _socket.IOControl(-1744830452, new byte[] { 0, 0, 0, 0 }, null); } catch { }
            _socket.Bind(new IPEndPoint(IPAddress.Any, host ? port : 0));
            _port = ((IPEndPoint)_socket.LocalEndPoint).Port;
        }

        /// <summary>Host a game: listen on <paramref name="port"/>.</summary>
        public static UdpTransport Host(int port = DefaultPort) { return new UdpTransport(true, port); }

        /// <summary>Join a game at "address" or "address:port".</summary>
        public static UdpTransport Join(string address)
        {
            var ep = Parse(address);
            var t = new UdpTransport(false, 0);
            t._byConn[0] = ep;
            t._byEndpoint[ep] = 0;
            return t;
        }

        public static IPEndPoint Parse(string address)
        {
            address = (address ?? "").Trim();
            var port = DefaultPort;
            var colon = address.LastIndexOf(':');
            if (colon > 0 && address.IndexOf(':') == colon)
            {
                int p;
                if (int.TryParse(address.Substring(colon + 1), out p)) port = p;
                address = address.Substring(0, colon);
            }
            if (address.Length == 0) throw new FormatException("type an address");
            IPAddress ip;
            if (!IPAddress.TryParse(address, out ip))
            {
                ip = null;
                foreach (var a in Dns.GetHostAddresses(address)) if (a.AddressFamily == AddressFamily.InterNetwork) { ip = a; break; }
                if (ip == null) throw new FormatException("couldn't find " + address);
            }
            return new IPEndPoint(ip, port);
        }

        public void Send(int conn, byte[] data)
        {
            EndPoint ep;
            if (!_byConn.TryGetValue(conn, out ep)) return;
            try { _socket.SendTo(data, ep); } catch (SocketException) { }
        }

        public bool Poll(out int conn, out byte[] data)
        {
            conn = -1; data = null;
            while (true)
            {
                EndPoint from = new IPEndPoint(IPAddress.Any, 0);
                int n;
                try
                {
                    if (_socket.Available <= 0) return false;
                    n = _socket.ReceiveFrom(_buffer, ref from);
                }
                catch (SocketException) { return false; }
                if (n <= 0) continue;
                int c;
                if (!_byEndpoint.TryGetValue(from, out c))
                {
                    if (!_host) continue; // a client only listens to its host
                    c = _nextConn++;
                    _byEndpoint[from] = c;
                    _byConn[c] = from;
                }
                conn = c;
                data = new byte[n];
                Array.Copy(_buffer, data, n);
                return true;
            }
        }

        public void Drop(int conn)
        {
            EndPoint ep;
            if (!_byConn.TryGetValue(conn, out ep)) return;
            _byConn.Remove(conn);
            _byEndpoint.Remove(ep);
        }

        public string Describe() { return _host ? "hosting on port " + _port : "joining " + _byConn[0]; }

        /// <summary>This PC's addresses that others on the same network (or a virtual network) can join.</summary>
        public static List<string> LocalAddresses()
        {
            var list = new List<string>();
            try
            {
                foreach (var a in Dns.GetHostAddresses(Dns.GetHostName()))
                    if (a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)) list.Add(a.ToString());
            }
            catch { }
            return list;
        }

        public void Dispose() { try { _socket.Close(); } catch { } }
    }

    /// <summary>
    /// A pretend network for tests: packets between in-memory transports arrive after a delay, with jitter (so
    /// out of order) and some lost, all from a seeded random so a test plays the same every run.
    /// </summary>
    public sealed class FakeNetwork
    {
        public double Now;
        public double Latency = 0.04, Jitter = 0.02, Loss = 0.05;
        private readonly Random _rng;
        private readonly List<Packet> _flying = new List<Packet>();
        private readonly List<FakeTransport> _nodes = new List<FakeTransport>();

        private struct Packet { public double At; public FakeTransport To; public int FromNode; public byte[] Data; }

        public FakeNetwork(int seed) { _rng = new Random(seed); }

        /// <summary>A node. Node 0 is the host; on clients, connection 0 means the host.</summary>
        public FakeTransport Add()
        {
            var t = new FakeTransport(this, _nodes.Count);
            _nodes.Add(t);
            return t;
        }

        internal void Send(FakeTransport from, int toNode, byte[] data)
        {
            if (toNode < 0 || toNode >= _nodes.Count || _rng.NextDouble() < Loss) return;
            _flying.Add(new Packet { At = Now + Latency + _rng.NextDouble() * Jitter, To = _nodes[toNode], FromNode = from.Node, Data = data });
        }

        /// <summary>Move the clock on and deliver what has arrived.</summary>
        public void Advance(double seconds)
        {
            Now += seconds;
            for (var i = 0; i < _flying.Count; i++)
            {
                var p = _flying[i];
                if (p.At > Now) continue;
                p.To.Inbox.Enqueue(new KeyValuePair<int, byte[]>(p.FromNode, p.Data));
                _flying.RemoveAt(i--);
            }
        }
    }

    public sealed class FakeTransport : ITransport
    {
        private readonly FakeNetwork _net;
        public readonly int Node;
        internal readonly Queue<KeyValuePair<int, byte[]>> Inbox = new Queue<KeyValuePair<int, byte[]>>();

        internal FakeTransport(FakeNetwork net, int node) { _net = net; Node = node; }

        // the host's connection numbers are the client nodes' numbers; a client's connection 0 is node 0, the host
        public void Send(int conn, byte[] data) { _net.Send(this, conn, data); }

        public bool Poll(out int conn, out byte[] data)
        {
            conn = -1; data = null;
            if (Inbox.Count == 0) return false;
            var p = Inbox.Dequeue();
            conn = p.Key; data = p.Value;
            return true;
        }

        public void Drop(int conn) { }
        public string Describe() { return "test network node " + Node; }
        public void Dispose() { }
    }
}
