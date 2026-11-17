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
        public Fix PuckRadius = Fix.Ratio(27, 80); // 0.3375: 35% bigger than the first 0.25
        /// <summary>
        /// How thick the walls are. A map's wall line is the middle of the wall: things stop half this far from it,
        /// at the wall's drawn edge (and a fast puck has a whole wall's width to be caught in).
        /// </summary>
        public Fix WallThickness = Fix.Ratio(17, 25); // 0.68

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
        /// <summary>
        /// Hit lag: after a hit both players freeze for a moment (like a fighting game), longer for a harder hit. A full
        /// check freezes them this long; a bump, a share of it. They keep their speed and fly off when it ends.
        /// </summary>
        public Fix HitLagMax = Fix.Ratio(3, 20);
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
        public Fix PuckMaxSpeed = Fix.FromInt(69);
        /// <summary>A quick shot (shoot/check with the puck).</summary>
        public Fix ShotSpeed = Fix.Ratio(69, 4); // 17.25 (15, +15%)
        /// <summary>
        /// A charged shot's speed goes up in a straight line with how long you held charge: from
        /// <see cref="ChargedShotMin"/> for a tap to <see cref="ChargedShotMax"/> at <see cref="ChargeTime"/> seconds.
        /// Held half the time = half way. Holding longer than ChargeTime keeps it at full.
        /// </summary>
        public Fix ChargedShotMin = Fix.FromInt(5);
        public Fix ChargedShotMax = Fix.Ratio(207, 4); // 51.75 (45, +15%)
        public Fix ChargeTime = Fix.Ratio(9, 10);
        public Fix ShotPickupDelay = Fix.Ratio(1, 5);
        /// <summary>Checking the carrier knocks the puck loose: how fast it pops out, and how long the checker must wait to grab it.</summary>
        public Fix KnockLooseSpeed = Fix.FromInt(4), CheckerPickupDelay = Fix.Ratio(1, 4);
        /// <summary>How hard a Puck Gun shot knocks the puck.</summary>
        public Fix GunPuckPush = Fix.FromInt(24);
        /// <summary>How hard a Puck Gun shot shoves the player it hits.</summary>
        public Fix GunKnockback = Fix.FromInt(8);

        /// <summary>
        /// Holding the puck. It sits at the front of your wedge, but it's a real object: other players' bodies, the
        /// boards and pylons push it about. Pushed round past <see cref="CarryHoldCos"/> (the cosine of the angle from
        /// straight ahead) or further than <see cref="CarrySlack"/> away from you, you lose it.
        /// </summary>
        public Fix CarryHoldCos = Fix.Ratio(3, 4), CarrySlack = Fix.Ratio(3, 10);
        /// <summary>How fast a pushed-aside puck drifts back to the front of your wedge (share per substep).</summary>
        public Fix CarryFollow = Fix.Ratio(1, 4);
        /// <summary>Losing control of the puck: how fast it rolls off, and how long before you can grab it again.</summary>
        public Fix LoseControlSpeed = Fix.FromInt(3), LoseControlDelay = Fix.Ratio(3, 10);
        /// <summary>Stealing: an opponent whose wedge tip gets this close to the carried puck's edge takes it.</summary>
        public Fix StealReach = Fix.Ratio(3, 20);
        public Fix StolenPickupDelay = Fix.Ratio(1, 2);

        // match flow
        /// <summary>0 = no limit (free play).</summary>
        public int GoalsToWin = 5;

        // card mechanics
        /// <summary>Puck Gun: shot speed, how long a shot flies, cooldown, stun, and the shot's size.</summary>
        public Fix GunSpeed = Fix.FromInt(38), GunLife = Fix.One, GunCooldown = Fix.FromInt(2), GunStun = Fix.Ratio(5, 4), ShotRadius = Fix.Ratio(6, 25);
        /// <summary>Invisibility: how long, and the cooldown.</summary>
        public Fix InvisTime = Fix.FromInt(5), InvisCooldown = Fix.FromInt(15);
        /// <summary>Shockwave: cooldown; slow (30%, +5% per extra copy) for 2 s (+0.5 s per copy); reach in player radii; push (share of a check's).</summary>
        public Fix ShockCooldown = Fix.FromInt(7), ShockSlow = Fix.Ratio(3, 10), ShockSlowPerCopy = Fix.Ratio(5, 100);
        public Fix ShockTime = Fix.FromInt(2), ShockTimePerCopy = Fix.Half, ShockRange = Fix.FromInt(3), ShockPush = Fix.Ratio(4, 5);
        /// <summary>Friendly Fire: speed multiplier and how long it lasts.</summary>
        public Fix FriendlyBoost = Fix.Ratio(9, 5), FriendlyTime = Fix.FromInt(2);
        /// <summary>Team Play: how close an opponent may get to a passed puck before the pass no longer counts.</summary>
        public Fix PassTouch = Fix.Ratio(3, 10);
        /// <summary>Pylon goalies: size, distance out from the goal line, and spacing.</summary>
        public Fix PylonRadius = Fix.Half, PylonOut = Fix.Ratio(12, 5), PylonSpread = Fix.Ratio(11, 10);

        // cards
        /// <summary>After every goal, the team that conceded picks a card (needs a card library).</summary>
        public bool CardsAfterGoals = true;
        /// <summary>Team Vote: each team is offered one set of cards, everyone votes, and the whole team gets the winner (a tie is random).</summary>
        public bool TeamVote;
        /// <summary>Cards that may not be offered (library indexes), from the settings' card list.</summary>
        public int[] CardsOff = new int[0];
        public int CardsOffered = 3;
        /// <summary>Seconds to choose before a card is picked at random.</summary>
        public Fix PickTime = Fix.FromInt(30);
        public Fix CountdownTime = Fix.Ratio(3, 2);
        public Fix CelebrateTime = Fix.FromInt(2);
        /// <summary>A timed match: seconds on the clock (0 = play to <see cref="GoalsToWin"/> instead). Level at the end: next goal wins.</summary>
        public Fix TimeLimit = Fix.Zero;
        /// <summary>Goals are shown again (the celebration is long enough for the replay; the replay itself is just drawing).</summary>
        public bool Replays;

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
