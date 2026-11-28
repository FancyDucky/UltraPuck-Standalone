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
        /// <summary>Not a button: the player's "dash where you move" setting (a dash goes along the move stick when it's pushed).</summary>
        public const byte DashMove = 32;
        /// <summary>Swap to a bot teammate (a press): take over the next one on your team, cycling back to your own player.</summary>
        public const byte Swap = 64;
        /// <summary>Not a button: the player turned off being handed to a bot teammate that picks up the puck.</summary>
        public const byte NoAutoSwap = 128;
        /// <summary>A <see cref="Pick"/> outside the card screen: the host adds a bot to red / blue mid-match.</summary>
        public const byte AddBotRed = 201, AddBotBlue = 202;
        /// <summary>Host commands the same way: back to the kick-off, and fix the score (red / blue, up / down).</summary>
        public const byte KickOff = 203, RedUp = 204, RedDown = 205, BlueUp = 206, BlueDown = 207;
        /// <summary>The host takes a bot out mid-match: <c>RemoveBot + its player number</c> (208..215).</summary>
        public const byte RemoveBot = 208;
        /// <summary>
        /// On the card screen: <c>EndPick</c> is the host ending the card pick now (anyone still choosing gets a random
        /// card, Choose's deck too); <c>Reroll</c> is New Hand's re-roll, sent until the new cards arrive.
        /// </summary>
        public const byte EndPick = 249, Reroll = 250;
        /// <summary>
        /// Free play's puck drills (the d-pad), as a <see cref="Pick"/> from that player: the puck to you, the puck sent at
        /// you, shot at the other team's net, shot at your own.
        /// </summary>
        public const byte PuckToMe = 220, PuckAtMe = 221, PuckAtTheirNet = 222, PuckAtOurNet = 223;
        /// <summary>
        /// The host's Carry-through match setting, mid-match: <c>CarryOff</c> / <c>CarryOn</c>, and
        /// <c>CarryStrength + n</c> (n = 0..20) for a strength of n × 5% of the puck's speed.
        /// </summary>
        public const byte CarryOff = 224, CarryOn = 225, CarryStrength = 226;
        /// <summary>
        /// The host's Knock-away match setting, mid-match: <c>KnockAway + n</c> (n = 0..20) for a check knocking the puck
        /// loose at n × 25% of its usual speed. (Picks 100..200 are free outside the card screen.)
        /// </summary>
        public const byte KnockAway = 100;
        /// <summary><see cref="Look"/> bit: this person's goal explosion is the Black Hole, which pulls everyone in instead of blasting them away.</summary>
        public const byte LookPull = 1;

        public sbyte MoveX, MoveY;   // -127..127, the left stick
        /// <summary>The right stick: when pushed, you face where it points (twin-stick aiming).</summary>
        public sbyte AimX, AimY;
        public byte Buttons;
        /// <summary>While picking a card: 1, 2, 3... = take that card from your offer; 0 = still choosing.</summary>
        public byte Pick;
        /// <summary>While picking: the card you're looking at (1, 2, 3...; 0 = none), so the other team can watch you choose.</summary>
        public byte Hover;
        /// <summary>Not a control: the bits of the person's look that change the game (<see cref="LookPull"/>).</summary>
        public byte Look;

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

        public bool Equals(PlayerInput o) { return MoveX == o.MoveX && MoveY == o.MoveY && AimX == o.AimX && AimY == o.AimY && Buttons == o.Buttons && Pick == o.Pick && Hover == o.Hover && Look == o.Look; }
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
        /// <summary>...and the whole team's (Team Play).</summary>
        public int PassRefreshTeam;
        /// <summary>Power Shot: a quick shot is as hard as a full charge (Over Charge doesn't add to it).</summary>
        public int QuickShotFull;
        /// <summary>Invisible Shot: a quick shot hides the puck from the other team for a moment.</summary>
        public int GhostShot;
        /// <summary>Cannon Ball: a full charge keeps going through whoever on the other team catches it.</summary>
        public int CannonBall;
        /// <summary>Team Play: a clean pass to a teammate speeds you both up.</summary>
        public int PassSpeed;
        /// <summary>Stone Wall: a full charge raises a wall along the puck's path.</summary>
        public int StoneWall;
        /// <summary>Utility: shoot a blue portal, then an orange one, onto the walls; anything going into one comes out the other.</summary>
        public int PortalGun;
        /// <summary>Utility: spin a slot machine for a short power-up (speed, giant, invisible, strong).</summary>
        public int SlotMachine;
        /// <summary>RAGE: checks you take fill a meter; full, you rage (bigger, heavier, faster, harder) for a while.</summary>
        public int Rage;
        /// <summary>Momentum: your shot doesn't slow below 33% of its speed (per card). Loyal: your shot into your own net bounces out (no own goal).</summary>
        public int Momentum, Loyal;
        /// <summary>Giant Nets (a chaos card): while anyone holds it, every net is twice as wide (Simulation.WideNets).</summary>
        public int WideNets;
        /// <summary>More Pucks (a chaos card): an extra puck at every kick-off, one per copy anyone holds (Simulation.ExtraPucks).</summary>
        public int ExtraPucks;
        /// <summary>Laser Sights: while charging a shot, a green laser shows where it will go (Simulation.LaserPath; only drawn).</summary>
        public int LaserSight;

        public Stats Clone() { return (Stats)MemberwiseClone(); }

        internal void Hash(ref ulong h)
        {
            GameState.Mix(ref h, MoveSpeed); GameState.Mix(ref h, MoveSpeedWithPuck); GameState.Mix(ref h, ShotPower);
            GameState.Mix(ref h, QuickShotPower); GameState.Mix(ref h, ChargeSpeed); GameState.Mix(ref h, OverCharge);
            GameState.Mix(ref h, QuickShotFull); GameState.Mix(ref h, GhostShot); GameState.Mix(ref h, CannonBall);
            GameState.Mix(ref h, DashCooldown); GameState.Mix(ref h, DashPower); GameState.Mix(ref h, ExtraDashes);
            GameState.Mix(ref h, Size); GameState.Mix(ref h, Weight); GameState.Mix(ref h, KnockbackGiven); GameState.Mix(ref h, KnockbackTaken);
            GameState.Mix(ref h, HeadStart); GameState.Mix(ref h, DoubleAgent); GameState.Mix(ref h, Pylons); GameState.Mix(ref h, PuckGun);
            GameState.Mix(ref h, Invisibility); GameState.Mix(ref h, FriendlyFire); GameState.Mix(ref h, Shockwave);
            GameState.Mix(ref h, Swap); GameState.Mix(ref h, PassRefresh); GameState.Mix(ref h, PassRefreshTeam); GameState.Mix(ref h, IceTrail);
            GameState.Mix(ref h, PassSpeed); GameState.Mix(ref h, StoneWall); GameState.Mix(ref h, PortalGun); GameState.Mix(ref h, SlotMachine); GameState.Mix(ref h, Rage); GameState.Mix(ref h, Momentum); GameState.Mix(ref h, Loyal); GameState.Mix(ref h, WideNets); GameState.Mix(ref h, ExtraPucks); GameState.Mix(ref h, LaserSight);
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
        /// <summary>Which way the last dash went (for the dash lines; it may not be the way they face).</summary>
        public FixVec2 DashDir;
        /// <summary>Seconds left of lighter drag after a dash (keeps speed up when chaining checks).</summary>
        public Fix DashCoast;
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
        /// <summary>While picking: 0 = a normal offer, 1 = Choose (the offer is the whole deck).</summary>
        public byte PickKind;
        /// <summary>New Hand: re-rolls used in this pick.</summary>
        public int Rerolls;
        /// <summary>
        /// Just handed new cards (Choose's deck, a re-rolled hand): the pick they were still sending was for the old
        /// cards, so picks wait until it's let go (Pick back to 0).
        /// </summary>
        public bool PickHeld;
        /// <summary>Double Card: the cards it gave in the last pick (shown before the countdown), and how many times it has (so a new one is seen).</summary>
        public int[] Gifts = new int[0];
        public int Gifted;
        /// <summary>Card rounds this player has picked in (Choose's deck pick not counted again).</summary>
        public int PickRounds;
        /// <summary>
        /// Joined part-way: picking a card for each of their team's card rounds they missed (individual picking), or
        /// about to be given the team's cards (Team Vote / Same Draw). They can't move or touch the puck until done.
        /// </summary>
        public bool CatchingUp;
        /// <summary>
        /// Added part-way as a bot: the bot picks its catch-up cards. (A person away for a moment is flagged <see cref="Bot"/>
        /// while the bot plays them, but their catch-up cards wait for them.)
        /// </summary>
        public bool BotCatchUp;
        /// <summary>
        /// Double Agent's disguise, looks only: true from a face-off on the other side until the colour has faded back
        /// (<see cref="DisguiseTime"/> past Tuning's hold + fade). Shown in the other team's colour, under
        /// <see cref="DisguiseAs"/>'s name (a player index; -1: no name to borrow).
        /// </summary>
        public bool Disguised;
        public int DisguiseAs = -1;
        /// <summary>Seconds of play since the disguised face-off (counts from the puck drop, not the countdown).</summary>
        public Fix DisguiseTime;

        // card effects (seconds left, 0 = ready / not active)
        public Fix GunCooldown, InvisCooldown, InvisibleTime, ShockCooldown;
        /// <summary>Shockwave: how long the utility button has been held (it fires, this big, when let go).</summary>
        public Fix ShockHold;
        /// <summary>Slowed by a shockwave: for how long, and by how much (0.3 = 30% slower).</summary>
        public Fix SlowTime, Slow;
        /// <summary>Friendly Fire: the teammate carrying this player (-1 = nobody), and for how long so far.</summary>
        public int HeldBy = -1;
        public Fix HeldTime;
        /// <summary>Friendly Fire: seconds left of flying after being thrown (no skating; checks whoever it hits).</summary>
        public Fix TossTime;
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
        /// <summary>
        /// A person steering a bot teammate: the player their controls drive (-1 = their own). Their own player is
        /// run by the bot meanwhile. Part of the game, so it's the same on every PC online.
        /// </summary>
        public int Pilot = -1;
        /// <summary>The swap button was held last tick (so a press swaps once).</summary>
        public bool SwapHeld;
        /// <summary>Their goal explosion pulls everyone in (the Black Hole look, from their controls' <see cref="PlayerInput.Look"/>).</summary>
        public bool GoalPull;
        /// <summary>Match stats (for the screen after the match): goals, assists, saves and checks landed.</summary>
        public int Goals, Assists, Saves, Bumps;
        /// <summary>Cannon Ball: seconds left of being carried off by a caught shot (little control, little drag).</summary>
        public Fix Shoved;
        /// <summary>Cannon Ball: who shot the ball that's carrying this player off (-1 = nobody).</summary>
        public int ShovedBy = -1;
        /// <summary>Sped up (a Team Play pass, your team's Ice Trail): for how long, and by how much (0.8 = 80% faster).</summary>
        public Fix FastTime, Fast;
        /// <summary>Portal Gun: the colour the next shot makes (0 blue, 1 orange), and the wait before the next shot.</summary>
        public int PortalNext;
        public Fix PortalCooldown;
        /// <summary>Going through a portal: the Id of the portal it's in (0 = none).</summary>
        public int PortalVia;
        /// <summary>Bots: this player is the one their team sends after the puck (see Bot.AssignRoles).</summary>
        public bool BotChase;
        /// <summary>
        /// Slot Machine: time left of the spin going on (0 = none), what it'll land on (<see cref="SlotWin"/>: 0 = nothing,
        /// else the prize), the three symbols it shows (4 bits each, for the drawing side), how many spins so far (so the
        /// drawing side sees a new one), the prize being enjoyed and for how long, and the wait after a losing spin.
        /// </summary>
        public Fix SlotSpin;
        public int SlotWin, SlotReels, SlotSpins, SlotPrize;
        public Fix SlotPrizeTime, SlotWait;
        /// <summary>RAGE: how full the meter is (each check adds the checker's weight over yours), how full it has to get
        /// this time (a random whole number in Tuning.RageNeedMin .. RageNeedMax, rolled at the first check; 0: not yet), the
        /// rage's seconds left, and the cooldown after it (no rage builds up meanwhile).</summary>
        public Fix RageFill;
        public int RageNeed;
        public Fix RageTime, RageCooldown;
        /// <summary>
        /// Knocked out of the rink: seconds until they're back (0 = playing). Meanwhile they're parked far off the ice.
        /// <see cref="OutAt"/> is where they went through the boards and <see cref="OutDir"/> which way (for the blast).
        /// <see cref="Launches"/> / <see cref="Respawns"/> count them (for the effects and sounds), and
        /// <see cref="RespawnTime"/> runs down from Tuning.RespawnShow after coming back (the pillar of light).
        /// </summary>
        public Fix OutTime, RespawnTime;
        public FixVec2 OutAt, OutDir, OutFrom;
        public int Launches, Respawns;
        /// <summary>Out because a Black Hole goal swallowed them (gone where it is, no flight out of the rink).</summary>
        public bool Swallowed;

        public PlayerState Clone()
        {
            var p = (PlayerState)MemberwiseClone();
            p.Stats = Stats.Clone();
            p.Cards = (int[])Cards.Clone();
            p.Offer = (int[])Offer.Clone();
            p.Gifts = (int[])Gifts.Clone();
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
        /// <summary>Portal Gun shots: the portal it makes (0 blue, 1 orange).</summary>
        public int Kind;
    }

    /// <summary>
    /// A Portal Gun portal, on a wall. <see cref="Pos"/> is its middle, on the wall's centre line; <see cref="Normal"/>
    /// points off the wall towards the side it was shot from. It runs <see cref="Tuning.PortalLength"/> along wall line
    /// <see cref="Wall"/> (of the map), centred <see cref="At"/> along it, so it bends with a curved wall. Anything
    /// going into it comes out of the same player's other portal, once both are up.
    /// </summary>
    [Serializable]
    public struct Portal
    {
        public FixVec2 Pos, Normal;
        public int Owner;
        /// <summary>0 blue, 1 orange.</summary>
        public int Colour;
        public int Wall;
        public Fix At;
        /// <summary>Counts everything that's gone in or out (for the drawing side's sparks).</summary>
        public int Uses;
        /// <summary>Which portal this is (every new one gets the next number), so the drawing side can tell a new one.</summary>
        public int Id;
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

    /// <summary>
    /// A piece of a Stone Wall: a short straight wall (as thick as the rink's) that everything bumps into, until
    /// <see cref="Life"/> runs out and it crumbles. Pieces of one wall share <see cref="Wall"/>, oldest first.
    /// </summary>
    [Serializable]
    public struct Stone
    {
        public FixVec2 A, B;
        public Fix Life;
        public int Wall;
    }

    /// <summary>A pylon goalie: a solid dummy in front of a net.</summary>
    [Serializable]
    public struct Pylon
    {
        public FixVec2 Pos;
        /// <summary>Pushed pylons slide (and slow down quickly).</summary>
        public FixVec2 Vel;
        public int Team;
        /// <summary>How big it is: every Pylon Goalie card on the team makes it 55% bigger.</summary>
        public Fix Radius;
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
        /// <summary>Seconds left before it disappears (it flashes for the last few).</summary>
        public Fix Life;
        /// <summary>Going through a portal: the Id of the portal it's in (0 = none).</summary>
        public int PortalVia;
        /// <summary>
        /// An extra puck (More Pucks chaos card) instead of a Swap rock: the puck's size, weight and glide, nobody's
        /// (Owner -1), never runs out, and scores in a net like the puck. Back to the kick-off at every face-off.
        /// </summary>
        public bool Puck;
        /// <summary>An extra puck: the last player who carried it (its goal is theirs if they're on the scoring team), or -1.</summary>
        public int Touch;
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
        /// <summary>Card rounds each team has had (red, blue): what a player joining part-way catches up to.</summary>
        public int[] TeamRounds = new int[2];
        public Phase Phase = Phase.Countdown;
        public Fix PhaseTime;
        /// <summary>Team that scored last (-1 = nobody yet), or the winner once the game is over.</summary>
        public int LastScorer = -1;
        /// <summary>Player who scored last (-1 = nobody yet). Used to zoom the camera on them after a goal.</summary>
        public int LastGoalPlayer = -1;
        /// <summary>Who set up the last goal (the teammate who passed it to the scorer), or -1.</summary>
        public int LastAssist = -1;
        /// <summary>Who the last goal counts for (the person who put it in; -1 for an own goal).</summary>
        public int LastGoalBy = -1;
        /// <summary>The last two players to have held the puck (newest first), for assists.</summary>
        public int Touch1 = -1, Touch2 = -1;
        /// <summary>The last player of each team (0, 1) to touch the puck since the face-off, or -1: a goal is only an own
        /// goal when nobody on the scoring team touched it.</summary>
        public int TeamTouch0 = -1, TeamTouch1 = -1;
        /// <summary>The carrier the last time bots were handed to people (so a new carrier is noticed once).</summary>
        public int AutoCarrier = -1;
        /// <summary>Invisible Shot: seconds the loose puck stays hidden from everyone but <see cref="PuckHiddenTeam"/>.</summary>
        public Fix PuckHidden;
        public int PuckHiddenTeam = -1;
        /// <summary>Cannon Ball: the team whose full-charge shot this is (-1: none); an opponent catching it is knocked back.</summary>
        public int CannonTeam = -1;
        /// <summary>Cannon Ball: the player who shot it.</summary>
        public int CannonFrom = -1;
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
        /// <summary>Stone Wall: the walls standing, and the one being raised behind the puck (time and length left, its end, its way).</summary>
        public Stone[] Stones = new Stone[0];
        public Fix StoneLaying, StoneLeft;
        public FixVec2 StoneTip, StoneDir;
        public int StoneWalls;
        /// <summary>Team Play: who last let go of the puck, while it hasn't been touched by an opponent since (-1 = no pass going).</summary>
        public int PassFrom = -1;
        /// <summary>Counts face-offs: each one, the front spot (the kick-off) goes to the next player on each team.</summary>
        public int KickOffTurn;
        /// <summary>Portal Gun: the portals on the walls, the shots on their way, and how many portals have been made.</summary>
        public Portal[] Portals = new Portal[0];
        public Shot[] PortalShots = new Shot[0];
        public int PortalsMade;
        /// <summary>The loose puck going through a portal: the Id of the portal it's in (0 = none).</summary>
        public int PuckPortal;
        /// <summary>
        /// Carry-through (a match setting the host can change mid-match): a pass or shot that reaches another player
        /// takes them along at <see cref="CarryPercent"/>% of the puck's speed. Starts from the match rules.
        /// </summary>
        public bool CarryOn;
        public int CarryPercent;
        /// <summary>
        /// Knock-away (a match setting the host can change mid-match): a check knocks the carried puck loose at this % of
        /// its usual speed. Starts from the match rules.
        /// </summary>
        public int KnockPercent;
        /// <summary>Momentum: the speed the loose puck was shot at (zero once someone has it).</summary>
        public Fix ShotSpeed;
        /// <summary>
        /// Noted for the sound side: how many times the loose puck has bounced off a wall, a pylon or a Stone Wall, and
        /// how fast it was going into the latest one.
        /// </summary>
        public int PuckKnocks;
        public Fix PuckKnockSpeed;
        /// <summary>A Black Hole goal: where it is, and how long it keeps swallowing anyone who reaches it.</summary>
        public FixVec2 HoleAt;
        public Fix HoleTime;

        public GameState Clone()
        {
            var s = (GameState)MemberwiseClone();
            s.Players = new PlayerState[Players.Length];
            for (var i = 0; i < Players.Length; i++) s.Players[i] = Players[i].Clone();
            s.Score = (int[])Score.Clone();
            s.TeamRounds = (int[])TeamRounds.Clone();
            s.Shots = (Shot[])Shots.Clone();
            s.Pylons = (Pylon[])Pylons.Clone();
            s.Rocks = (Rock[])Rocks.Clone();
            s.Ice = (IcePoint[])Ice.Clone();
            s.Stones = (Stone[])Stones.Clone();
            s.Portals = (Portal[])Portals.Clone();
            s.PortalShots = (Shot[])PortalShots.Clone();
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
            Mix(ref h, Tick); Mix(ref h, Carrier); Mix(ref h, PuckTeam); Mix(ref h, (int)Phase); Mix(ref h, PhaseTime); Mix(ref h, LastScorer); Mix(ref h, LastGoalPlayer); Mix(ref h, LastAssist); Mix(ref h, LastGoalBy); Mix(ref h, Touch1); Mix(ref h, Touch2); Mix(ref h, TeamTouch0); Mix(ref h, TeamTouch1); Mix(ref h, AutoCarrier); Mix(ref h, PuckHidden); Mix(ref h, PuckHiddenTeam); Mix(ref h, CannonTeam); Mix(ref h, (long)Rng);
            Mix(ref h, PuckPos); Mix(ref h, PuckVel); Mix(ref h, PassFrom); Mix(ref h, KickOffTurn);
            foreach (var sh in Shots) { Mix(ref h, sh.Pos); Mix(ref h, sh.Vel); Mix(ref h, sh.Owner); Mix(ref h, sh.Life); }
            foreach (var py in Pylons) { Mix(ref h, py.Pos); Mix(ref h, py.Vel); Mix(ref h, py.Team); Mix(ref h, py.Radius); }
            foreach (var rk in Rocks) { Mix(ref h, rk.Pos); Mix(ref h, rk.Vel); Mix(ref h, rk.Owner); Mix(ref h, rk.Carrier); Mix(ref h, rk.Life); Mix(ref h, rk.PortalVia); Mix(ref h, rk.Puck ? 1 : 0); Mix(ref h, rk.Touch); }
            foreach (var po in Portals) { Mix(ref h, po.Pos); Mix(ref h, po.Normal); Mix(ref h, po.Owner); Mix(ref h, po.Colour); Mix(ref h, po.Wall); Mix(ref h, po.At); Mix(ref h, po.Uses); Mix(ref h, po.Id); }
            foreach (var sh in PortalShots) { Mix(ref h, sh.Pos); Mix(ref h, sh.Vel); Mix(ref h, sh.Owner); Mix(ref h, sh.Life); Mix(ref h, sh.Kind); }
            Mix(ref h, PortalsMade); Mix(ref h, PuckPortal); Mix(ref h, CarryOn ? 1 : 0); Mix(ref h, CarryPercent); Mix(ref h, KnockPercent); Mix(ref h, ShotSpeed); Mix(ref h, PuckKnocks); Mix(ref h, PuckKnockSpeed); Mix(ref h, HoleAt); Mix(ref h, HoleTime);
            Mix(ref h, IceLaying); Mix(ref h, IceAge); Mix(ref h, IceTrails);
            Mix(ref h, CannonFrom); Mix(ref h, StoneLaying); Mix(ref h, StoneLeft); Mix(ref h, StoneTip); Mix(ref h, StoneDir); Mix(ref h, StoneWalls);
            foreach (var st in Stones) { Mix(ref h, st.A); Mix(ref h, st.B); Mix(ref h, st.Life); Mix(ref h, st.Wall); }
            foreach (var ice in Ice) { Mix(ref h, ice.Pos); Mix(ref h, ice.Trail); Mix(ref h, ice.Team); Mix(ref h, ice.Slow); Mix(ref h, ice.Melt); Mix(ref h, ice.Laying ? 1 : 0); }
            foreach (var sc in Score) Mix(ref h, sc);
            foreach (var r in TeamRounds) Mix(ref h, r);
            foreach (var p in Players)
            {
                Mix(ref h, p.Team); Mix(ref h, p.Pos); Mix(ref h, p.Vel); Mix(ref h, p.Facing);
                Mix(ref h, p.DashCharge); Mix(ref h, p.DashDir); Mix(ref h, p.DashTime); Mix(ref h, p.DashCoast); Mix(ref h, p.Charge); Mix(ref h, p.NoPickup); Mix(ref h, p.PrevButtons); Mix(ref h, p.SuppressDash ? 1 : 0); Mix(ref h, p.Bot ? 1 : 0); Mix(ref h, p.SkipVote ? 1 : 0);
                Mix(ref h, p.Stun); Mix(ref h, p.HitLag); Mix(ref h, p.Hits); Mix(ref h, p.HitPower); Mix(ref h, p.HitDir);
                Mix(ref h, p.ShotsFired); Mix(ref h, p.ShotStrength);
                p.Stats.Hash(ref h);
                Mix(ref h, p.Cards.Length); foreach (var c in p.Cards) Mix(ref h, c);
                Mix(ref h, p.Offer.Length); foreach (var c in p.Offer) Mix(ref h, c);
                Mix(ref h, p.Picked); Mix(ref h, p.PickKind); Mix(ref h, p.Rerolls); Mix(ref h, p.PickHeld ? 1 : 0);
                Mix(ref h, p.Gifted); Mix(ref h, p.Gifts.Length); foreach (var c in p.Gifts) Mix(ref h, c);
                Mix(ref h, p.PickRounds); Mix(ref h, p.CatchingUp ? 1 : 0); Mix(ref h, p.BotCatchUp ? 1 : 0);
                Mix(ref h, p.Disguised ? 1 : 0); Mix(ref h, p.DisguiseAs); Mix(ref h, p.DisguiseTime);
                Mix(ref h, p.GunCooldown); Mix(ref h, p.InvisCooldown); Mix(ref h, p.InvisibleTime); Mix(ref h, p.ShockCooldown);
                Mix(ref h, p.SlowTime); Mix(ref h, p.Slow); Mix(ref h, p.HeldBy); Mix(ref h, p.HeldTime); Mix(ref h, p.TossTime); Mix(ref h, p.Shocks); Mix(ref h, p.ShockAt);
                Mix(ref h, p.ShockRadius); Mix(ref h, p.Refreshes); Mix(ref h, p.Swaps); Mix(ref h, p.SwapFrom); Mix(ref h, p.SwapTo); Mix(ref h, p.SwapCooldown);
                Mix(ref h, p.Pilot); Mix(ref h, p.SwapHeld ? 1 : 0); Mix(ref h, p.GoalPull ? 1 : 0); Mix(ref h, p.Goals); Mix(ref h, p.Assists); Mix(ref h, p.Saves); Mix(ref h, p.Bumps); Mix(ref h, p.Shoved);
                Mix(ref h, p.ShovedBy); Mix(ref h, p.FastTime); Mix(ref h, p.Fast); Mix(ref h, p.ShockHold);
                Mix(ref h, p.PortalNext); Mix(ref h, p.PortalCooldown); Mix(ref h, p.PortalVia); Mix(ref h, p.BotChase ? 1 : 0);
                Mix(ref h, p.SlotSpin); Mix(ref h, p.SlotWin); Mix(ref h, p.SlotReels); Mix(ref h, p.SlotSpins); Mix(ref h, p.SlotPrize);
                Mix(ref h, p.SlotPrizeTime); Mix(ref h, p.SlotWait);
                Mix(ref h, p.RageFill); Mix(ref h, p.RageNeed); Mix(ref h, p.RageTime); Mix(ref h, p.RageCooldown);
                Mix(ref h, p.OutTime); Mix(ref h, p.RespawnTime); Mix(ref h, p.OutAt); Mix(ref h, p.OutDir); Mix(ref h, p.OutFrom); Mix(ref h, p.Launches); Mix(ref h, p.Respawns); Mix(ref h, p.Swallowed ? 1 : 0);
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
