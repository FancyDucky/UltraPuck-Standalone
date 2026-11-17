namespace UltraPuck.Core
{
    /// <summary>
    /// The game's numbers. Distances are in map units (a player is 1 unit across), times in seconds. Everyone in a
    /// match must use the same tuning (it will be part of the match settings online), and mods may change it.
    /// </summary>
    public sealed class Tuning
    {
        /// <summary>120: half the input delay of 60, and the game is cheap enough to run (and replay for rollback) twice as often.</summary>
        public int TicksPerSecond = 120;
        /// <summary>
        /// Movement and collisions are split into this many steps per tick, so a fast puck can't skip through a wall.
        /// 2 at 120 ticks = 240 collision steps a second, the same as 4 at 60.
        /// </summary>
        public int Substeps = 2;

        public Fix PlayerRadius = Fix.Ratio(1, 2);
        public Fix PuckRadius = Fix.Ratio(1, 4);

        // moving: the stick pushes with MoveForce; Drag slows you, so top speed = MoveForce / Drag
        public Fix MoveForce = Fix.FromInt(24);
        public Fix Drag = Fix.FromInt(3);
        public Fix BrakeDrag = Fix.FromInt(9);
        /// <summary>How fast you turn to face the move stick (share of the way per second, roughly).</summary>
        public Fix TurnRate = Fix.FromInt(14);
        /// <summary>How fast you turn to face the aim stick, when it's pushed (snappier, like a twin-stick shooter).</summary>
        public Fix AimTurnRate = Fix.FromInt(40);

        // checking (the dash you do with shoot/check when you don't have the puck)
        public Fix DashSpeed = Fix.FromInt(10);
        public Fix DashCooldown = Fix.Ratio(3, 2);
        public Fix DashHitTime = Fix.Ratio(1, 4);
        public Fix DashKnockback = Fix.FromInt(17);
        /// <summary>How long a checked player is dazed, and how much of their stick force they keep meanwhile.</summary>
        public Fix HitStun = Fix.Ratio(7, 20);
        public Fix StunControl = Fix.Ratio(1, 4);

        // bumping
        public Fix PlayerBounce = Fix.Ratio(4, 5);
        /// <summary>Closing speed at which a plain bump counts as a hit (for the animation), and the speed that counts as the hardest.</summary>
        public Fix BumpHitSpeed = Fix.FromInt(3);
        public Fix BumpHitMax = Fix.FromInt(14);
        public Fix WallBounce = Fix.Ratio(1, 2);

        // the puck
        public Fix PuckDrag = Fix.Ratio(7, 10);
        public Fix PuckWallBounce = Fix.Ratio(4, 5);
        public Fix PuckMaxSpeed = Fix.FromInt(40);
        /// <summary>A quick shot (shoot/check with the puck).</summary>
        public Fix ShotSpeed = Fix.FromInt(15);
        /// <summary>
        /// A charged shot's speed goes up in a straight line with how long you held charge: from
        /// <see cref="ChargedShotMin"/> for a tap to <see cref="ChargedShotMax"/> at <see cref="ChargeTime"/> seconds.
        /// Held half the time = half way. Holding longer than ChargeTime keeps it at full.
        /// </summary>
        public Fix ChargedShotMin = Fix.FromInt(2);
        public Fix ChargedShotMax = Fix.FromInt(30);
        public Fix ChargeTime = Fix.Ratio(9, 10);
        public Fix ShotPickupDelay = Fix.Ratio(3, 10);
        public Fix StolenPickupDelay = Fix.Ratio(1, 2);

        // match flow
        /// <summary>0 = no limit (free play).</summary>
        public int GoalsToWin = 5;

        // cards
        /// <summary>After every goal, the team that conceded picks a card (needs a card library).</summary>
        public bool CardsAfterGoals = true;
        public int CardsOffered = 3;
        /// <summary>Seconds to choose before a card is picked at random.</summary>
        public Fix PickTime = Fix.FromInt(30);
        public Fix CountdownTime = Fix.Ratio(3, 2);
        public Fix CelebrateTime = Fix.FromInt(2);

        /// <summary>
        /// Free play: practice with no winner. Goals still count, but only the puck goes back to its spot (players
        /// stay where they are), and anyone can call the puck back with <see cref="PlayerInput.ResetPuck"/>.
        /// </summary>
        public bool FreePlay;

        public Fix Dt { get { return Fix.Ratio(1, TicksPerSecond); } }

        /// <summary>The settings for free play: no goal limit, no countdown, a short celebration.</summary>
        public static Tuning ForFreePlay()
        {
            return new Tuning { FreePlay = true, GoalsToWin = 0, CountdownTime = Fix.Zero, CelebrateTime = Fix.One, CardsAfterGoals = false };
        }
    }
}
