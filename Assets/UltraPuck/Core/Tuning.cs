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
        /// <summary>Move speed while charging a shot with the puck (or a rock / teammate): 30% slower.</summary>
        public Fix ChargeMoveSpeed = Fix.Ratio(7, 10);
        /// <summary>
        /// Extra push when getting going: this share of <see cref="MoveForce"/> on top, full from a standstill and
        /// fading to nothing at top speed (along the stick). Quicker off the mark, same top speed, same glide.
        /// </summary>
        public Fix Acceleration = Fix.One;
        /// <summary>How fast you turn to face the move stick (share of the way per second, roughly).</summary>
        public Fix TurnRate = Fix.FromInt(14);
        /// <summary>How fast you turn to face the aim stick, when it's pushed (snappier, like a twin-stick shooter).</summary>
        public Fix AimTurnRate = Fix.FromInt(40);

        // checking (the dash you do with shoot/check when you don't have the puck)
        public Fix DashSpeed = Fix.FromInt(10);
        /// <summary>Seconds to refill one stored check (10% longer than the old 1.5 s).</summary>
        public Fix DashCooldown = Fix.Ratio(33, 20); // 1.65
        public Fix DashHitTime = Fix.Ratio(1, 4);
        /// <summary>After a dash: this long of lighter drag so you keep more speed when chaining checks.</summary>
        public Fix DashCoast = Fix.Ratio(11, 20); // 0.55 s
        /// <summary>Drag while <see cref="DashCoast"/> is active (normal skating drag is <see cref="Drag"/> = 3).</summary>
        public Fix DashCoastDrag = Fix.Ratio(19, 10); // 1.9
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
        /// <summary>The puck's drag: 0.7 / 1.1, so it glides 10% further.</summary>
        public Fix PuckDrag = Fix.Ratio(7, 11);
        public Fix PuckWallBounce = Fix.Ratio(4, 5);
        /// <summary>
        /// Hard cap on puck speed. Sized so Over Charge can land: a full charge is <see cref="ChargedShotMax"/> (~52),
        /// and four Over Charge copies push the meter to 3× that (~156).
        /// </summary>
        public Fix PuckMaxSpeed = Fix.FromInt(165);
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
        /// <summary>Invisible Shot: how long the puck stays hidden.</summary>
        public Fix GhostShotTime = Fix.Half;
        /// <summary>Cannon Ball: the catcher is sent off along the shot at this share of the puck's speed, gliding (little control) this long.</summary>
        public Fix CannonCarry = Fix.Ratio(8, 10), CannonTime = Fix.Half;
        /// <summary>Team Play: after a clean pass the passer and the teammate are this much faster (0.8 = 80%) for this long.</summary>
        public Fix PassSpeedBoost = Fix.Ratio(8, 10), PassSpeedTime = Fix.FromInt(3);
        /// <summary>
        /// Stone Wall: raised along the puck's path for up to <see cref="StoneLength"/> (or <see cref="StoneLayTime"/>, or
        /// until the puck turns or is caught), kept <see cref="StoneGap"/> behind it, in pieces <see cref="StoneSpacing"/>
        /// long; each piece stands <see cref="StoneLife"/> and then crumbles.
        /// </summary>
        public Fix StoneLife = Fix.Ratio(11, 2), StoneLength = Fix.FromInt(30), StoneLayTime = Fix.FromInt(3), StoneSpacing = Fix.Ratio(4, 5), StoneGap = Fix.Ratio(9, 10);
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
        public Fix GunSpeed = Fix.FromInt(38), GunLife = Fix.One, GunCooldown = Fix.One, GunStun = Fix.Ratio(5, 4), ShotRadius = Fix.Ratio(6, 25);
        /// <summary>Invisibility: how long, and the cooldown.</summary>
        public Fix InvisTime = Fix.FromInt(5), InvisCooldown = Fix.FromInt(15);
        /// <summary>
        /// Shockwave (utility): no cooldown. Hold the button to grow it and let go to fire: from <see cref="ShockMin"/>
        /// (a tap) to <see cref="ShockMax"/> units across its radius after <see cref="ShockChargeTime"/> seconds. Push is a
        /// share of a check's knockback; it shoves every other player, the loose puck and loose Swap rocks away.
        /// (<see cref="ShockCooldown"/> / <see cref="ShockRange"/> are from before, kept for old tuning files.)
        /// </summary>
        public Fix ShockCooldown = Fix.FromInt(6), ShockRange = Fix.FromInt(5), ShockPush = Fix.Two;
        public Fix ShockMin = Fix.Ratio(3, 2), ShockMax = Fix.FromInt(6), ShockChargeTime = Fix.FromInt(6);
        /// <summary>Goal explosion: shove everyone (and loose rocks) away from the puck; reach in world units, push as a share of a check's knockback.</summary>
        public Fix GoalBlastRange = Fix.FromInt(12), GoalBlastPush = Fix.FromInt(3);
        /// <summary>
        /// Ice Trail: the puck lays ice for up to <see cref="IceTime"/>. The start of the trail lasts
        /// <see cref="IceLife"/>, then the melting creeps along it <see cref="IceMeltPace"/> times slower than it was
        /// laid (so each later bit lasts a little longer). How much it slows per copy, how wide it is, and how far
        /// apart its points are.
        /// </summary>
        public Fix IceTime = Fix.FromInt(3), IceLife = Fix.FromInt(15), IceMeltPace = Fix.One, IceBoost = Fix.Ratio(6, 10), IceSlow = Fix.Ratio(1, 10), IceRadius = Fix.Ratio(9, 20), IceSpacing = Fix.Ratio(2, 5);
        /// <summary>A pass caught by a teammate carries them on: this share of the puck's speed, up to the most.</summary>
        public Fix PassBoost = Fix.Ratio(3, 20), PassBoostMax = Fix.FromInt(6);
        /// <summary>
        /// The wedge at the front of a player (as drawn): its tip this many radii ahead, its back edge this far ahead
        /// and this wide either side. A loose puck touching it is picked up, like touching the body.
        /// </summary>
        public Fix WedgeTip = Fix.Ratio(9, 5), WedgeBack = Fix.Ratio(3, 5), WedgeHalfWidth = Fix.Ratio(7, 10);
        public bool WedgePickup = true;
        /// <summary>
        /// Friendly Fire: a teammate touching your wedge while you hold charge/brake is picked up; they're thrown exactly
        /// like a shot (as fast as the puck would go). A thrown teammate flies with <see cref="MateTossDrag"/> (the puck's)
        /// and no skating, checking whoever they hit, until they're down to <see cref="TossEndSpeed"/> (or
        /// <see cref="TossTime"/> at most); then they're <see cref="MateBoost"/> faster for <see cref="MateBoostTime"/>.
        /// Held longer than <see cref="MateHoldMax"/>: they're thrown anyway. (<see cref="TossDrag"/> is Cannon Ball's glide.)
        /// </summary>
        public Fix TossTime = Fix.FromInt(3), TossDrag = Fix.One, MateHoldMax = Fix.FromInt(3);
        public Fix MateTossDrag = Fix.Ratio(7, 11), TossEndSpeed = Fix.FromInt(8), MateBoost = Fix.Ratio(15, 100), MateBoostTime = Fix.FromInt(4);
        /// <summary>Friendly Fire: how far past touching your wedge a teammate can be and still be picked up.</summary>
        public Fix MateGrabReach = Fix.Ratio(1, 10);
        /// <summary>Team Play: how close an opponent may get to a passed puck before the pass no longer counts.</summary>
        public Fix PassTouch = Fix.Ratio(3, 10);
        /// <summary>Pylon goalies: size, distance out from the goal line, and spacing.</summary>
        public Fix PylonRadius = Fix.Half, PylonOut = Fix.Ratio(12, 5), PylonSpread = Fix.Ratio(11, 10);
        /// <summary>Pylon goalies can be pushed: how heavy they are (a player is 1) and how fast they slow down.</summary>
        public Fix PylonWeight = Fix.Two, PylonDrag = Fix.FromInt(6);
        /// <summary>Swap rock: size, weight (lighter than a player), how fast it slows, throw speed (like a quick shot).</summary>
        // Swap rock: light and very glidey so anyone can knock it around; throw still uses quick-shot speed
        // (drag 0.326: glides 35% less far than the old 0.18, then 15% less again)
        public Fix RockRadius = Fix.Ratio(2, 5), RockWeight = Fix.Ratio(12, 100), RockDrag = Fix.Ratio(326, 1000), RockSpeed = Fix.Ratio(69, 4);
        /// <summary>Bots wait this long between Swap uses so they don't spam throw/teleport.</summary>
        public Fix SwapBotCooldown = Fix.FromInt(3);
        /// <summary>A loose Swap rock slower than this has stopped: it's gone (so it can't be left as a safe spot to swap back to).</summary>
        public Fix RockStopSpeed = Fix.Half;

        // cards
        /// <summary>After every goal, the team that conceded picks a card (needs a card library).</summary>
        public bool CardsAfterGoals = true;
        /// <summary>Team Vote: each team is offered one set of cards, everyone votes, and the whole team gets the winner (a tie is random).</summary>
        public bool TeamVote;
        /// <summary>Same Draw: everyone on a team is offered the same cards, and each takes their own.</summary>
        public bool SameDraw;
        /// <summary>Allow more than one utility card per player. Off: taking a new utility replaces the one you have.</summary>
        public bool MultiUtility;
        /// <summary>Cards that may not be offered (library indexes), from the settings' card list.</summary>
        public int[] CardsOff = new int[0];
        public int CardsOffered = 3;
        /// <summary>Seconds to choose before a card is picked at random.</summary>
        public Fix PickTime = Fix.FromInt(30);
        /// <summary>The count before a face-off: 3, 2, 1, go.</summary>
        public Fix CountdownTime = Fix.FromInt(3);
        /// <summary>After a card pick: this long before the countdown starts, so the cards finish flying off first.</summary>
        public Fix PickOutroTime = Fix.Ratio(3, 2);
        public Fix CelebrateTime = Fix.FromInt(2);
        /// <summary>A timed match: seconds on the clock (0 = play to <see cref="GoalsToWin"/> instead). Level at the end: next goal wins.</summary>
        public Fix TimeLimit = Fix.Zero;
        /// <summary>Goals are shown again (the celebration is long enough for the replay; the replay itself is just drawing).</summary>
        public bool Replays;
        /// <summary>How long after a goal the replay starts; from then on, everyone pressing shoot skips it.</summary>
        public Fix ReplaySkipFrom = Fix.FromInt(3);

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
