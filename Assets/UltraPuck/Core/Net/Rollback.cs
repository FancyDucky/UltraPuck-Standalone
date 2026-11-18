using System;

namespace UltraPuck.Core.Net
{
    /// <summary>
    /// Rollback netcode for one match. Every PC runs the whole game itself. Your own controls are used straight
    /// away (after a tiny input delay); other players' controls arrive over the network a little later, so until
    /// they do, the game guesses they're still doing what they last did. When the real controls arrive and differ
    /// from the guess, the game rewinds to that tick and replays to now with the right controls — in one frame,
    /// so you just see the other player's movement corrected. Because the rules are deterministic, every PC ends
    /// up with exactly the same game.
    ///
    /// Bots need no network at all: every PC works out their controls from the same game state.
    /// </summary>
    public sealed class Rollback
    {
        /// <summary>Ticks of history kept (about 2 s at 120 a second); more than the furthest we ever rewind.</summary>
        public const int Ring = 256;
        /// <summary>How far ahead of the slowest player's confirmed controls we may run before waiting (0.4 s).</summary>
        public const int MaxPrediction = 48;
        /// <summary>
        /// Rewind at most this often (ticks). With several players, late controls arrive nearly every tick; replaying
        /// for each one costs the game many times over (too much for a Steam Deck), so corrections are gathered and
        /// done together a few ticks later. It changes nothing about the game, only when this PC shows the fix.
        /// </summary>
        public const int RewindEvery = 4;
        private int _lastRewind = int.MinValue / 2;

        public readonly Simulation Sim;
        public readonly int Players;
        public readonly int InputDelay;
        private readonly bool[] _local, _bot;

        private GameState _state, _prev;
        private readonly GameState[] _saved = new GameState[Ring];
        private readonly PlayerInput[,] _input = new PlayerInput[Ring, 8];
        private readonly int[,] _inputTick = new int[Ring, 8];   // which tick a slot of _input holds (-1 = none)
        private readonly PlayerInput[,] _used = new PlayerInput[Ring, 8];
        private readonly int[] _known;                           // every input up to here has arrived, per player
        private readonly PlayerInput[] _last;                    // the newest input we have, for guessing
        private int _tick;                                       // the next tick to simulate
        private int _rewindTo = int.MaxValue;

        /// <summary>Rewinds done so far and the longest one (ticks), for the network stats.</summary>
        public int Rewinds, LongestRewind;

        /// <param name="local">Players controlled on this PC.</param>
        /// <param name="bots">Players run by the bot on every PC.</param>
        public Rollback(Simulation sim, GameState start, bool[] local, bool[] bots, int inputDelay)
        {
            Sim = sim;
            Players = start.Players.Length;
            if (Players > 8) throw new ArgumentException("at most 8 players");
            _local = local; _bot = bots;
            InputDelay = inputDelay;
            _state = start;
            _prev = start.Clone();
            _known = new int[Players];
            _last = new PlayerInput[Players];
            for (var k = 0; k < Ring; k++) for (var p = 0; p < 8; p++) _inputTick[k, p] = -1;
            // the first few ticks (before anyone's delayed input lands) are "no input" for everyone
            for (var p = 0; p < Players; p++)
            {
                for (var t = 0; t < inputDelay; t++) Store(p, t, default(PlayerInput));
                _known[p] = inputDelay - 1;
            }
        }

        public GameState State { get { return _state; } }
        /// <summary>The state one tick before <see cref="State"/> (for smooth drawing between ticks).</summary>
        public GameState Previous { get { return _prev; } }
        /// <summary>The next tick to be simulated.</summary>
        public int Tick { get { return _tick; } }
        /// <summary>Every player's controls are known up to and including this tick.</summary>
        public int Confirmed
        {
            get
            {
                var c = int.MaxValue;
                for (var p = 0; p < Players; p++) if (!_bot[p]) c = Math.Min(c, _known[p]);
                return c == int.MaxValue ? _tick - 1 : c;
            }
        }

        public bool IsLocal(int p) { return _local[p]; }
        public bool IsBot(int p) { return _bot[p]; }
        public int KnownUpTo(int p) { return _known[p]; }

        /// <summary>We may simulate another tick without guessing too far ahead.</summary>
        public bool CanAdvance { get { return _tick - Confirmed <= MaxPrediction; } }

        /// <summary>The tick a local input given now applies to.</summary>
        public int LocalInputTick { get { return _tick + InputDelay; } }

        /// <summary>An input for a player at a tick, from this PC or the network. Re-sent duplicates are ignored.</summary>
        public void SetInput(int p, int tick, PlayerInput input)
        {
            if (p < 0 || p >= Players || _bot[p]) return;
            if (tick <= _tick - Ring + 1 || tick >= _tick + Ring - 1) return; // far outside the window: stale or nonsense
            var k = tick & (Ring - 1);
            if (_inputTick[k, p] == tick) return; // already have it
            Store(p, tick, input);
            // advance the "known up to" mark over any run we now have
            while (_inputTick[(_known[p] + 1) & (Ring - 1), p] == _known[p] + 1) _known[p]++;
            // if we already simulated that tick with a guess that was wrong, rewind to it
            if (tick < _tick && !_used[k, p].Equals(input)) _rewindTo = Math.Min(_rewindTo, tick);
        }

        private void Store(int p, int tick, PlayerInput input)
        {
            var k = tick & (Ring - 1);
            _input[k, p] = input;
            _inputTick[k, p] = tick;
        }

        /// <summary>An input we have for a player at a tick (for sending it on), if we have it.</summary>
        public bool TryGetInput(int p, int tick, out PlayerInput input)
        {
            var k = tick & (Ring - 1);
            input = _input[k, p];
            return _inputTick[k, p] == tick;
        }

        /// <summary>Simulate one tick (first replaying anything a late input showed we got wrong).</summary>
        public void Advance()
        {
            // gather corrections: rewind now only if it's been a few ticks, or the mistake is getting old
            if (_rewindTo < _tick && (_tick - _lastRewind >= RewindEvery || _tick - _rewindTo >= Ring / 2))
            {
                _lastRewind = _tick;
                var from = Math.Max(_rewindTo, _tick - Ring + 1);
                Rewinds++;
                LongestRewind = Math.Max(LongestRewind, _tick - from);
                _state = _saved[from & (Ring - 1)].Clone();
                for (var t = from; t < _tick; t++) Simulate(t);
                _rewindTo = int.MaxValue;
            }
            _prev = _state.Clone();
            Simulate(_tick);
            _tick++;
        }

        private readonly PlayerInput[] _frame = new PlayerInput[8];

        private void Simulate(int t)
        {
            var k = t & (Ring - 1);
            _saved[k] = _state.Clone();
            var inputs = new PlayerInput[Players];
            for (var p = 0; p < Players; p++)
            {
                PlayerInput i;
                if (_bot[p]) i = Bot.Think(Sim, _state, p);
                else if (_inputTick[k, p] == t) i = _input[k, p];
                else i = Guess(p, t);
                inputs[p] = i;
                _used[k, p] = i;
            }
            Sim.Step(_state, inputs);
        }

        /// <summary>No input yet: assume they're still holding what they last held (sticks and buttons, but not a fresh card pick).</summary>
        private PlayerInput Guess(int p, int t)
        {
            var newest = Math.Min(_known[p], t - 1);
            PlayerInput i;
            if (newest >= 0 && TryGetInput(p, newest, out i)) { i.Pick = 0; return i; }
            return default(PlayerInput);
        }

        /// <summary>The saved state at a tick that's still in the history (for checksums), or null.</summary>
        public GameState SavedAt(int tick)
        {
            if (tick < 0 || tick >= _tick || tick <= _tick - Ring) return null;
            // a late input has shown an earlier guess was wrong and the replay hasn't run yet (it runs on the next
            // Advance): states after that tick are still the old guess, so they aren't ready to compare
            if (tick > _rewindTo) return null;
            var s = _saved[tick & (Ring - 1)];
            return s != null && s.Tick == tick ? s : null;
        }
    }
}
