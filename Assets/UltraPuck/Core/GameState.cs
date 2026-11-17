using System;

namespace UltraPuck.Core
{
    /// <summary>
    /// One player's controls for one tick. This is all that will travel over the network for online play, so it's
    /// kept tiny: the two sticks as two bytes each, the buttons as bits, and a card pick.
    /// </summary>
    [Serializable]
    public struct PlayerInput : IEquatable<PlayerInput>
    {
        /// <summary>With the puck: shoot now. Without it: check (a body-check dash).</summary>
        public const byte ShootCheck = 1;
        /// <summary>With the puck: hold to charge a shot, let go to fire it. Without it: brake.</summary>
        public const byte ChargeBrake = 2;
        /// <summary>Use your utility power-up (none exist yet; cards will add them).</summary>
        public const byte Utility = 4;
        /// <summary>Free play only: bring the puck back to its spot.</summary>
        public const byte ResetPuck = 8;

        public sbyte MoveX, MoveY;   // -127..127, the left stick
        /// <summary>The right stick: when pushed, you face where it points (twin-stick aiming).</summary>
        public sbyte AimX, AimY;
        public byte Buttons;
        /// <summary>While picking a card: 1, 2, 3... = take that card from your offer; 0 = still choosing.</summary>
        public byte Pick;

        public bool Has(byte b) { return (Buttons & b) != 0; }

        /// <summary>The move stick as a vector (length 0..1).</summary>
        public FixVec2 Move { get { return Stick(MoveX, MoveY); } }
        /// <summary>The aim stick as a vector (length 0..1).</summary>
        public FixVec2 Aim { get { return Stick(AimX, AimY); } }

        private static FixVec2 Stick(sbyte x, sbyte y)
        {
            return new FixVec2(Fix.Ratio(x, 127), Fix.Ratio(y, 127)).ClampLength(Fix.One);
        }

        /// <summary>From a stick reading (any float is fine: it's rounded to a byte first, so every PC agrees).</summary>
        public static sbyte Axis(float v)
        {
            if (v > 1f) v = 1f; else if (v < -1f) v = -1f;
            return (sbyte)Math.Round(v * 127f);
        }

        public bool Equals(PlayerInput o) { return MoveX == o.MoveX && MoveY == o.MoveY && AimX == o.AimX && AimY == o.AimY && Buttons == o.Buttons && Pick == o.Pick; }
        public override bool Equals(object obj) { return obj is PlayerInput && Equals((PlayerInput)obj); }
        public override int GetHashCode() { return ((byte)MoveX | ((byte)MoveY << 8) | ((byte)AimX << 16) | ((byte)AimY << 24)) ^ (Buttons * 31 + Pick * 7919); }
    }

    /// <summary>
    /// What a player's cards (and mods) add up to. Rules read these everywhere, so a card only has to change a number.
    /// Multipliers start at 1, extras at 0. Worked out again from scratch whenever cards change (<see cref="CardLibrary.Recompute"/>).
    /// </summary>
    [Serializable]
    public sealed class Stats
    {
        public Fix MoveSpeed = Fix.One;
        /// <summary>Extra move speed multiplier while carrying the puck.</summary>
        public Fix MoveSpeedWithPuck = Fix.One;
        /// <summary>Every shot.</summary>
        public Fix ShotPower = Fix.One;
        /// <summary>Quick shots only (shoot/check with the puck).</summary>
        public Fix QuickShotPower = Fix.One;
        /// <summary>How fast a charge fills (2 = in half the time).</summary>
        public Fix ChargeSpeed = Fix.One;
        /// <summary>How far a charge can go past full (0.5 = up to 50% more power).</summary>
        public Fix OverCharge = Fix.Zero;
        /// <summary>Check recharge time (0.5 = recharges twice as fast).</summary>
        public Fix DashCooldown = Fix.One;
        public Fix DashPower = Fix.One;
        /// <summary>Checks you can store on top of the first.</summary>
        public int ExtraDashes;
        public Fix Size = Fix.One;
        /// <summary>Heavier: pushed less, pushes others more.</summary>
        public Fix Weight = Fix.One;
        /// <summary>How far your checks push people (on top of weight).</summary>
        public Fix KnockbackGiven = Fix.One;
        /// <summary>How far checks push you (on top of weight).</summary>
        public Fix KnockbackTaken = Fix.One;

        public Stats Clone() { return (Stats)MemberwiseClone(); }

        internal void Hash(ref ulong h)
        {
            GameState.Mix(ref h, MoveSpeed); GameState.Mix(ref h, MoveSpeedWithPuck); GameState.Mix(ref h, ShotPower);
            GameState.Mix(ref h, QuickShotPower); GameState.Mix(ref h, ChargeSpeed); GameState.Mix(ref h, OverCharge);
            GameState.Mix(ref h, DashCooldown); GameState.Mix(ref h, DashPower); GameState.Mix(ref h, ExtraDashes);
            GameState.Mix(ref h, Size); GameState.Mix(ref h, Weight); GameState.Mix(ref h, KnockbackGiven); GameState.Mix(ref h, KnockbackTaken);
        }
    }

    [Serializable]
    public sealed class PlayerState
    {
        public int Team;
        public FixVec2 Pos, Vel;
        public FixVec2 Facing = FixVec2.Right;
        /// <summary>Stored checks: 1 = one ready, 0.5 = halfway to the next. Up to 1 + <see cref="Stats.ExtraDashes"/>.</summary>
        public Fix DashCharge = Fix.One;
        /// <summary>Seconds left of the current check's hit window (a bump during it knocks people back).</summary>
        public Fix DashTime;
        /// <summary>Shot charge while holding charge with the puck, 0..1 (more with Over Charge; above 0 means a charge is going).</summary>
        public Fix Charge;
        /// <summary>Seconds left of being dazed after a check: you push the stick with much less force.</summary>
        public Fix Stun;
        /// <summary>
        /// Counts every hard hit this player has taken or given, so the drawing side can play a hit animation when
        /// it goes up. <see cref="HitPower"/> (0..1) and <see cref="HitDir"/> (from them towards what they hit) describe the last one.
        /// </summary>
        public int Hits;
        public Fix HitPower;
        public FixVec2 HitDir;
        /// <summary>Seconds before this player may pick up the puck again (after shooting or losing it).</summary>
        public Fix NoPickup;
        /// <summary>Last tick's buttons, so presses (not holds) can be told apart.</summary>
        public byte PrevButtons;
        public Stats Stats = new Stats();
        /// <summary>The cards this player has, as indexes into the match's <see cref="CardLibrary"/>, in the order taken.</summary>
        public int[] Cards = new int[0];
        /// <summary>While picking: the cards offered (library indexes); empty when not picking.</summary>
        public int[] Offer = new int[0];
        /// <summary>While picking: the card chosen, or -1.</summary>
        public int Picked = -1;

        public PlayerState Clone()
        {
            var p = (PlayerState)MemberwiseClone();
            p.Stats = Stats.Clone();
            p.Cards = (int[])Cards.Clone();
            p.Offer = (int[])Offer.Clone();
            return p;
        }
    }

    public enum Phase : byte
    {
        /// <summary>Everyone frozen at their spawns, counting down to the face-off.</summary>
        Countdown,
        Play,
        /// <summary>A goal was just scored; play carries on briefly before the reset.</summary>
        Scored,
        GameOver,
        /// <summary>Between goals: some players are choosing a card. Everything else waits.</summary>
        Picking,
    }

    /// <summary>
    /// Everything that changes during a match. A copy of this is a complete save of the moment, which is how
    /// rollback works: keep copies, and when a late input turns out different from the guess, go back and replay.
    /// </summary>
    [Serializable]
    public sealed class GameState
    {
        public int Tick;
        public PlayerState[] Players = new PlayerState[0];
        public FixVec2 PuckPos, PuckVel;
        /// <summary>Index of the player carrying the puck, or -1.</summary>
        public int Carrier = -1;
        /// <summary>Team that last shot or carried the puck (-1 = nobody since the face-off). The puck glows in their colour.</summary>
        public int PuckTeam = -1;
        public int[] Score = new int[2];
        public Phase Phase = Phase.Countdown;
        public Fix PhaseTime;
        /// <summary>Team that scored last (-1 = nobody yet), or the winner once the game is over.</summary>
        public int LastScorer = -1;
        public uint Rng = 0x9E3779B9u;

        public GameState Clone()
        {
            var s = (GameState)MemberwiseClone();
            s.Players = new PlayerState[Players.Length];
            for (var i = 0; i < Players.Length; i++) s.Players[i] = Players[i].Clone();
            s.Score = (int[])Score.Clone();
            return s;
        }

        /// <summary>Deterministic random number (xorshift), part of the state so rollback replays it the same.</summary>
        public uint NextRandom()
        {
            var x = Rng;
            x ^= x << 13; x ^= x >> 17; x ^= x << 5;
            Rng = x;
            return x;
        }

        /// <summary>A fingerprint of the whole state: two PCs with different checksums for the same tick have drifted apart.</summary>
        public ulong Checksum()
        {
            ulong h = 14695981039346656037UL;
            Mix(ref h, Tick); Mix(ref h, Carrier); Mix(ref h, PuckTeam); Mix(ref h, (int)Phase); Mix(ref h, PhaseTime); Mix(ref h, LastScorer); Mix(ref h, (long)Rng);
            Mix(ref h, PuckPos); Mix(ref h, PuckVel);
            foreach (var sc in Score) Mix(ref h, sc);
            foreach (var p in Players)
            {
                Mix(ref h, p.Team); Mix(ref h, p.Pos); Mix(ref h, p.Vel); Mix(ref h, p.Facing);
                Mix(ref h, p.DashCharge); Mix(ref h, p.DashTime); Mix(ref h, p.Charge); Mix(ref h, p.NoPickup); Mix(ref h, p.PrevButtons);
                Mix(ref h, p.Stun); Mix(ref h, p.Hits); Mix(ref h, p.HitPower); Mix(ref h, p.HitDir);
                p.Stats.Hash(ref h);
                Mix(ref h, p.Cards.Length); foreach (var c in p.Cards) Mix(ref h, c);
                Mix(ref h, p.Offer.Length); foreach (var c in p.Offer) Mix(ref h, c);
                Mix(ref h, p.Picked);
            }
            return h;
        }

        internal static void Mix(ref ulong h, long v)
        {
            for (var k = 0; k < 8; k++) { h ^= (byte)(v >> (k * 8)); h *= 1099511628211UL; }
        }
        internal static void Mix(ref ulong h, Fix v) { Mix(ref h, v.Raw); }
        internal static void Mix(ref ulong h, FixVec2 v) { Mix(ref h, v.X.Raw); Mix(ref h, v.Y.Raw); }
    }
}
