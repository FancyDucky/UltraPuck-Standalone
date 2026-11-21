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
        /// <summary>Held by the online host while their pause menu is open: the whole game stands still for everyone.</summary>
        public const byte Pause = 16;

        public sbyte MoveX, MoveY;   // -127..127, the left stick
        /// <summary>The right stick: when pushed, you face where it points (twin-stick aiming).</summary>
        public sbyte AimX, AimY;
        public byte Buttons;
        /// <summary>While picking a card: 1, 2, 3... = take that card from your offer; 0 = still choosing.</summary>
        public byte Pick;
        /// <summary>While picking: the card you're looking at (1, 2, 3...; 0 = none), so the other team can watch you choose.</summary>
        public byte Hover;

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

        public bool Equals(PlayerInput o) { return MoveX == o.MoveX && MoveY == o.MoveY && AimX == o.AimX && AimY == o.AimY && Buttons == o.Buttons && Pick == o.Pick && Hover == o.Hover; }
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

        // cards with their own rules: copies held (0 = don't have it)
        /// <summary>The team with the most starts face-offs holding the puck.</summary>
        public int HeadStart;
        /// <summary>Start face-offs on the other team's side.</summary>
        public int DoubleAgent;
        /// <summary>Pylon goalies in front of your net.</summary>
        public int Pylons;
        /// <summary>Utility: fire a shot that stuns.</summary>
        public int PuckGun;
        /// <summary>Utility: turn invisible to opponents.</summary>
        public int Invisibility;
        /// <summary>A teammate's check boosts you instead of dazing you.</summary>
        public int FriendlyFire;
        /// <summary>Utility: shove every other player away (stronger with more copies).</summary>
        public int Shockwave;
        /// <summary>Utility: throw a rock, then swap places with it (no cooldown).</summary>
        public int Swap;
        /// <summary>Fully charged shots leave a trail of ice that slows opponents (10% per copy).</summary>
        public int IceTrail;
        /// <summary>A clean pass to a teammate restores your cooldowns (Team Play).</summary>
        public int PassRefresh;
        /// <summary>...and the whole team's (Team Play II).</summary>
        public int PassRefreshTeam;

        public Stats Clone() { return (Stats)MemberwiseClone(); }

        internal void Hash(ref ulong h)
        {
            GameState.Mix(ref h, MoveSpeed); GameState.Mix(ref h, MoveSpeedWithPuck); GameState.Mix(ref h, ShotPower);
            GameState.Mix(ref h, QuickShotPower); GameState.Mix(ref h, ChargeSpeed); GameState.Mix(ref h, OverCharge);
            GameState.Mix(ref h, DashCooldown); GameState.Mix(ref h, DashPower); GameState.Mix(ref h, ExtraDashes);
            GameState.Mix(ref h, Size); GameState.Mix(ref h, Weight); GameState.Mix(ref h, KnockbackGiven); GameState.Mix(ref h, KnockbackTaken);
            GameState.Mix(ref h, HeadStart); GameState.Mix(ref h, DoubleAgent); GameState.Mix(ref h, Pylons); GameState.Mix(ref h, PuckGun);
            GameState.Mix(ref h, Invisibility); GameState.Mix(ref h, FriendlyFire); GameState.Mix(ref h, Shockwave);
            GameState.Mix(ref h, Swap); GameState.Mix(ref h, PassRefresh); GameState.Mix(ref h, PassRefreshTeam); GameState.Mix(ref h, IceTrail);
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
        /// <summary>Hit lag left (seconds): frozen in place after a hit, keeping the speed for when it ends.</summary>
        public Fix HitLag;
        /// <summary>
        /// Counts every hard hit this player has taken or given, so the drawing side can play a hit animation when
        /// it goes up. <see cref="HitPower"/> (0..1) and <see cref="HitDir"/> (from them towards what they hit) describe the last one.
        /// </summary>
        public int Hits;
        public Fix HitPower;
        public FixVec2 HitDir;
        /// <summary>
        /// Counts every shot this player has taken, so the sound side can play one when it goes up.
        /// <see cref="ShotStrength"/> (0..1) is how hard the last one was: 1 = a fully charged shot.
        /// </summary>
        public int ShotsFired;
        public Fix ShotStrength;
        /// <summary>Seconds before this player may pick up the puck again (after shooting or losing it).</summary>
        public Fix NoPickup;
        /// <summary>Last tick's buttons, so presses (not holds) can be told apart.</summary>
        public byte PrevButtons;
        /// <summary>
        /// After a quick shot: ignore Shoot/check for dashing until that button is let go, so a short tap doesn't
        /// also check the moment the puck leaves.
        /// </summary>
        public bool SuppressDash;
        /// <summary>A computer player (it votes to skip goal replays straight away).</summary>
        public bool Bot;
        /// <summary>During a goal replay: this player pressed shoot to skip it. When everyone has, it ends.</summary>
        public bool SkipVote;
        public Stats Stats = new Stats();
        /// <summary>The cards this player has, as indexes into the match's <see cref="CardLibrary"/>, in the order taken.</summary>
        public int[] Cards = new int[0];
        /// <summary>While picking: the cards offered (library indexes); empty when not picking.</summary>
        public int[] Offer = new int[0];
        /// <summary>While picking: the card chosen, or -1.</summary>
        public int Picked = -1;
        /// <summary>While picking: the offered card this player is looking at (1-based, 0 = none). Only for showing.</summary>
        public byte Hover;

        // card effects (seconds left, 0 = ready / not active)
        public Fix GunCooldown, InvisCooldown, InvisibleTime, ShockCooldown;
        /// <summary>Slowed by a shockwave: for how long, and by how much (0.3 = 30% slower).</summary>
        public Fix SlowTime, Slow;
        /// <summary>Boosted by a teammate's check (Friendly Fire).</summary>
        public Fix BoostTime;
        /// <summary>Counts shockwaves this player sent out, and where / how big the last one was (for the drawing side).</summary>
        public int Shocks;
        public FixVec2 ShockAt;
        public Fix ShockRadius;
        /// <summary>Counts Swap teleports (for the clap and dark-blue particle bursts at both spots).</summary>
        public int Swaps;
        public FixVec2 SwapFrom, SwapTo;
        /// <summary>Bots only: wait this long between Swap uses (throw or teleport).</summary>
        public Fix SwapCooldown;
        /// <summary>Counts Team Play cooldown refreshes (for the drawing side's flash).</summary>
        public int Refreshes;

        public PlayerState Clone()
        {
            var p = (PlayerState)MemberwiseClone();
            p.Stats = Stats.Clone();
            p.Cards = (int[])Cards.Clone();
            p.Offer = (int[])Offer.Clone();
            return p;
        }
    }

    /// <summary>A Puck Gun shot in flight.</summary>
    [Serializable]
    public struct Shot
    {
        public FixVec2 Pos, Vel;
        public int Owner;
        public Fix Life;
    }

    /// <summary>
    /// A bit of an Ice Trail. Points of one trail are next to each other in <see cref="GameState.Ice"/>, oldest
    /// first. <see cref="Melt"/> is the time left before it melts.
    /// </summary>
    [Serializable]
    public struct IcePoint
    {
        public FixVec2 Pos;
        /// <summary>Which trail it belongs to (they're drawn separately), and the team whose ice it is.</summary>
        public int Trail, Team;
        /// <summary>How much it slows opponents on it.</summary>
        public Fix Slow;
        public Fix Melt;
        /// <summary>Still being laid behind the puck (not melting yet).</summary>
        public bool Laying;
    }

    /// <summary>A pylon goalie: a solid dummy in front of a net.</summary>
    [Serializable]
    public struct Pylon
    {
        public FixVec2 Pos;
        /// <summary>Pushed pylons slide (and slow down quickly).</summary>
        public FixVec2 Vel;
        public int Team;
    }

    /// <summary>A Swap rock: thrown by a player, knockable / pickable / shootable by everyone, then swapped with.</summary>
    [Serializable]
    public struct Rock
    {
        public FixVec2 Pos, Vel;
        /// <summary>Player who threw it (still theirs for Swap teleport).</summary>
        public int Owner;
        /// <summary>Who is carrying it in their wedge, or -1 if loose.</summary>
        public int Carrier;
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
        /// <summary>A timed match: time left on the clock (counts down only while the puck is in play).</summary>
        public Fix Clock;
        /// <summary>A timed match ended level: the next goal wins.</summary>
        public bool Overtime;
        /// <summary>The host has paused the game (online): nothing moves and no clock runs until they resume.</summary>
        public bool Paused;
        public uint Rng = 0x9E3779B9u;

        /// <summary>Puck Gun shots in flight.</summary>
        public Shot[] Shots = new Shot[0];
        /// <summary>Pylon goalies standing in front of the nets (from the Pylon Goalie card).</summary>
        public Pylon[] Pylons = new Pylon[0];
        /// <summary>Swap rocks currently on the ice.</summary>
        public Rock[] Rocks = new Rock[0];
        /// <summary>Ice Trail: the ice on the rink.</summary>
        public IcePoint[] Ice = new IcePoint[0];
        /// <summary>Ice Trail: time left laying the trail behind the puck (0 = not laying one).</summary>
        public Fix IceLaying;
        /// <summary>Ice Trail: how long ago the trail being laid was shot.</summary>
        public Fix IceAge;
        /// <summary>Ice Trail: number of the newest trail.</summary>
        public int IceTrails;
        /// <summary>Team Play: who last let go of the puck, while it hasn't been touched by an opponent since (-1 = no pass going).</summary>
        public int PassFrom = -1;

        public GameState Clone()
        {
            var s = (GameState)MemberwiseClone();
            s.Players = new PlayerState[Players.Length];
            for (var i = 0; i < Players.Length; i++) s.Players[i] = Players[i].Clone();
            s.Score = (int[])Score.Clone();
            s.Shots = (Shot[])Shots.Clone();
            s.Pylons = (Pylon[])Pylons.Clone();
            s.Rocks = (Rock[])Rocks.Clone();
            s.Ice = (IcePoint[])Ice.Clone();
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
            Mix(ref h, Clock); Mix(ref h, Overtime ? 1 : 0); Mix(ref h, Paused ? 1 : 0);
            Mix(ref h, Tick); Mix(ref h, Carrier); Mix(ref h, PuckTeam); Mix(ref h, (int)Phase); Mix(ref h, PhaseTime); Mix(ref h, LastScorer); Mix(ref h, (long)Rng);
            Mix(ref h, PuckPos); Mix(ref h, PuckVel); Mix(ref h, PassFrom);
            foreach (var sh in Shots) { Mix(ref h, sh.Pos); Mix(ref h, sh.Vel); Mix(ref h, sh.Owner); Mix(ref h, sh.Life); }
            foreach (var py in Pylons) { Mix(ref h, py.Pos); Mix(ref h, py.Vel); Mix(ref h, py.Team); }
            foreach (var rk in Rocks) { Mix(ref h, rk.Pos); Mix(ref h, rk.Vel); Mix(ref h, rk.Owner); Mix(ref h, rk.Carrier); }
            Mix(ref h, IceLaying); Mix(ref h, IceAge); Mix(ref h, IceTrails);
            foreach (var ice in Ice) { Mix(ref h, ice.Pos); Mix(ref h, ice.Trail); Mix(ref h, ice.Team); Mix(ref h, ice.Slow); Mix(ref h, ice.Melt); Mix(ref h, ice.Laying ? 1 : 0); }
            foreach (var sc in Score) Mix(ref h, sc);
            foreach (var p in Players)
            {
                Mix(ref h, p.Team); Mix(ref h, p.Pos); Mix(ref h, p.Vel); Mix(ref h, p.Facing);
                Mix(ref h, p.DashCharge); Mix(ref h, p.DashTime); Mix(ref h, p.Charge); Mix(ref h, p.NoPickup); Mix(ref h, p.PrevButtons); Mix(ref h, p.SuppressDash ? 1 : 0); Mix(ref h, p.Bot ? 1 : 0); Mix(ref h, p.SkipVote ? 1 : 0);
                Mix(ref h, p.Stun); Mix(ref h, p.HitLag); Mix(ref h, p.Hits); Mix(ref h, p.HitPower); Mix(ref h, p.HitDir);
                Mix(ref h, p.ShotsFired); Mix(ref h, p.ShotStrength);
                p.Stats.Hash(ref h);
                Mix(ref h, p.Cards.Length); foreach (var c in p.Cards) Mix(ref h, c);
                Mix(ref h, p.Offer.Length); foreach (var c in p.Offer) Mix(ref h, c);
                Mix(ref h, p.Picked);
                Mix(ref h, p.GunCooldown); Mix(ref h, p.InvisCooldown); Mix(ref h, p.InvisibleTime); Mix(ref h, p.ShockCooldown);
                Mix(ref h, p.SlowTime); Mix(ref h, p.Slow); Mix(ref h, p.BoostTime); Mix(ref h, p.Shocks); Mix(ref h, p.ShockAt);
                Mix(ref h, p.ShockRadius); Mix(ref h, p.Refreshes); Mix(ref h, p.Swaps); Mix(ref h, p.SwapFrom); Mix(ref h, p.SwapTo); Mix(ref h, p.SwapCooldown);
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
