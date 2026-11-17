using System;

namespace UltraPuck.Core
{
    /// <summary>
    /// The game rules. <see cref="Step"/> moves a <see cref="GameState"/> forward one tick from everyone's inputs,
    /// using only fixed-point maths, so the same state and inputs give exactly the same result on every PC.
    /// Nothing here touches Unity: the Unity side only draws the state and collects inputs.
    /// </summary>
    public sealed class Simulation
    {
        public readonly MapDef Map;
        public readonly Tuning T;
        /// <summary>The cards in play, or null for no cards (then no picks happen after goals).</summary>
        public readonly CardLibrary Cards;

        public Simulation(MapDef map, Tuning tuning = null, CardLibrary cards = null)
        {
            if (map == null) throw new ArgumentNullException("map");
            Map = map;
            T = tuning ?? new Tuning();
            Cards = cards;
        }

        /// <summary>Stored checks a player can hold.</summary>
        public static Fix MaxDashes(PlayerState p) { return Fix.FromInt(1 + Math.Max(0, Math.Min(9, p.Stats.ExtraDashes))); }

        /// <summary>Seconds to recharge one check.</summary>
        public Fix DashRecharge(PlayerState p) { return T.DashCooldown * Fix.Clamp(p.Stats.DashCooldown, Fix.Ratio(1, 10), Fix.FromInt(5)); }

        // ------------------------------------------------------------------ cards

        /// <summary>
        /// Start a card pick for <paramref name="pickers"/> (player indexes): each is offered a few cards and play
        /// waits until they've all chosen (or the time runs out, which picks for them). Does nothing without cards.
        /// </summary>
        public void StartPicking(GameState s, System.Collections.Generic.IList<int> pickers)
        {
            if (Cards == null || Cards.Cards.Count == 0) return;
            var any = false;
            foreach (var i in pickers)
            {
                if (i < 0 || i >= s.Players.Length) continue;
                var p = s.Players[i];
                p.Offer = Cards.DrawOffer(s, i, T.CardsOffered);
                p.Picked = -1;
                any |= p.Offer.Length > 0;
            }
            if (!any) return;
            s.Carrier = -1;
            s.Phase = Phase.Picking;
            s.PhaseTime = T.PickTime;
        }

        /// <summary>The team that just conceded picks (after every goal, as in ROUNDS).</summary>
        private void PickAfterGoal(GameState s)
        {
            var pickers = new System.Collections.Generic.List<int>();
            for (var i = 0; i < s.Players.Length; i++) if (s.Players[i].Team != s.LastScorer) pickers.Add(i);
            StartPicking(s, pickers);
        }

        private void StepPicking(GameState s, PlayerInput[] inputs, Fix dt)
        {
            var waiting = false;
            for (var i = 0; i < s.Players.Length; i++)
            {
                var p = s.Players[i];
                var input = InputOf(inputs, i);
                p.PrevButtons = input.Buttons;
                if (p.Offer.Length == 0 || p.Picked >= 0) continue;
                if (input.Pick >= 1 && input.Pick <= p.Offer.Length) p.Picked = p.Offer[input.Pick - 1];
                else waiting = true;
            }
            s.PhaseTime -= dt;
            if (waiting && s.PhaseTime > Fix.Zero) return;

            // everyone chose (or time's up: a random card for anyone who didn't)
            foreach (var p in s.Players)
            {
                if (p.Offer.Length == 0) continue;
                if (p.Picked < 0) p.Picked = p.Offer[(int)(s.NextRandom() % (uint)p.Offer.Length)];
                var cards = new int[p.Cards.Length + 1];
                Array.Copy(p.Cards, cards, p.Cards.Length);
                cards[cards.Length - 1] = p.Picked;
                p.Cards = cards;
                p.Offer = new int[0];
                p.Picked = -1;
            }
            Cards.Recompute(s);
            if (T.FreePlay) s.Phase = Phase.Play; // free play: carry on where everyone stands
            else FaceOff(s);
        }

        /// <summary>
        /// A new match with one player per entry of <paramref name="teams"/> (0 or 1), waiting for the first face-off.
        /// <paramref name="seed"/> drives everything random in the match (bots' variety for now); online, every PC
        /// must use the same one. 0 keeps the default.
        /// </summary>
        public GameState NewGame(int[] teams, uint seed = 0)
        {
            var s = new GameState { Players = new PlayerState[teams.Length] };
            if (seed != 0) s.Rng = seed;
            for (var i = 0; i < teams.Length; i++) s.Players[i] = new PlayerState { Team = teams[i] & 1 };
            FaceOff(s);
            return s;
        }

        /// <summary>Everyone back to their spawn, the puck to its spot, and the countdown starts.</summary>
        public void FaceOff(GameState s)
        {
            var count = new int[2];
            foreach (var p in s.Players)
            {
                var sp = Map.SpawnFor(p.Team, count[p.Team]++);
                p.Pos = sp.Pos;
                p.Vel = FixVec2.Zero;
                p.Facing = sp.Facing;
                p.DashCharge = MaxDashes(p);
                p.DashTime = Fix.Zero;
                p.Charge = Fix.Zero;
                p.NoPickup = Fix.Zero;
                p.Stun = Fix.Zero;
            }
            s.PuckPos = Map.PuckSpot;
            s.PuckVel = FixVec2.Zero;
            s.Carrier = -1;
            s.PuckTeam = -1;
            s.Phase = Phase.Countdown;
            s.PhaseTime = T.CountdownTime;
        }

        /// <summary>The puck back to its spot, loose and still; nobody else moves.</summary>
        public void ResetPuck(GameState s)
        {
            s.PuckPos = Map.PuckSpot;
            s.PuckVel = FixVec2.Zero;
            s.Carrier = -1;
            s.PuckTeam = -1;
            foreach (var p in s.Players) { p.Charge = Fix.Zero; p.NoPickup = Fix.Zero; }
        }

        /// <summary>Someone joins mid-game (free play): a new player at their team's next spawn. Returns their index.</summary>
        public int AddPlayer(GameState s, int team)
        {
            team &= 1;
            var n = 0;
            foreach (var q in s.Players) if (q.Team == team) n++;
            var sp = Map.SpawnFor(team, n);
            var players = new PlayerState[s.Players.Length + 1];
            Array.Copy(s.Players, players, s.Players.Length);
            players[players.Length - 1] = new PlayerState { Team = team, Pos = sp.Pos, Facing = sp.Facing };
            s.Players = players;
            return players.Length - 1;
        }

        /// <summary>Someone leaves mid-game (free play). Everyone after them moves down one place.</summary>
        public void RemovePlayer(GameState s, int index)
        {
            if (index < 0 || index >= s.Players.Length) return;
            var players = new PlayerState[s.Players.Length - 1];
            for (int i = 0, j = 0; i < s.Players.Length; i++) if (i != index) players[j++] = s.Players[i];
            s.Players = players;
            if (s.Carrier == index) s.Carrier = -1;
            else if (s.Carrier > index) s.Carrier--;
        }

        public Fix RadiusOf(PlayerState p) { return T.PlayerRadius * Fix.Clamp(p.Stats.Size, Fix.Ratio(1, 4), Fix.FromInt(4)); }
        private static Fix WeightOf(PlayerState p) { return Fix.Clamp(p.Stats.Weight, Fix.Ratio(1, 10), Fix.FromInt(10)); }

        // ------------------------------------------------------------------ one tick

        public void Step(GameState s, PlayerInput[] inputs)
        {
            var dt = T.Dt;
            s.Tick++;

            if (s.Phase == Phase.Picking) { StepPicking(s, inputs, dt); return; }
            if (s.Phase == Phase.Countdown || s.Phase == Phase.GameOver)
            {
                for (var i = 0; i < s.Players.Length; i++) s.Players[i].PrevButtons = InputOf(inputs, i).Buttons;
                if (s.Phase == Phase.Countdown)
                {
                    s.PhaseTime -= dt;
                    if (s.PhaseTime <= Fix.Zero) s.Phase = Phase.Play;
                }
                return;
            }

            for (var i = 0; i < s.Players.Length; i++) Control(s, i, InputOf(inputs, i), dt);

            var sub = dt / T.Substeps;
            for (var k = 0; k < T.Substeps; k++)
            {
                foreach (var p in s.Players)
                {
                    p.Pos += p.Vel * sub;
                    CollideWalls(ref p.Pos, ref p.Vel, RadiusOf(p), T.WallBounce);
                }
                Bumps(s);
                MovePuck(s, sub);
                if (s.Phase == Phase.Play) CheckGoal(s);
            }

            // the puck slows down on its own
            s.PuckVel -= s.PuckVel * Fix.Min(Fix.One, T.PuckDrag * dt);
            s.PuckVel = s.PuckVel.ClampLength(T.PuckMaxSpeed);

            if (s.Phase == Phase.Scored)
            {
                s.PhaseTime -= dt;
                if (s.PhaseTime <= Fix.Zero)
                {
                    if (T.GoalsToWin > 0 && (s.Score[0] >= T.GoalsToWin || s.Score[1] >= T.GoalsToWin))
                    {
                        s.Phase = Phase.GameOver;
                        s.LastScorer = s.Score[0] >= T.GoalsToWin ? 0 : 1;
                    }
                    else if (T.FreePlay) { ResetPuck(s); s.Phase = Phase.Play; }
                    else
                    {
                        FaceOff(s);
                        if (T.CardsAfterGoals) PickAfterGoal(s); // after the face-off set-up, so play resumes from the spawns
                    }
                }
            }
        }

        private static PlayerInput InputOf(PlayerInput[] inputs, int i)
        {
            return inputs != null && i < inputs.Length ? inputs[i] : default(PlayerInput);
        }

        /// <summary>Stick, buttons, cooldowns and shooting for one player.</summary>
        private void Control(GameState s, int i, PlayerInput input, Fix dt)
        {
            var p = s.Players[i];
            var pressed = (byte)(input.Buttons & ~p.PrevButtons);
            p.PrevButtons = input.Buttons;
            if (T.FreePlay && (pressed & PlayerInput.ResetPuck) != 0) ResetPuck(s);

            var carrying = s.Carrier == i;

            // facing: the aim stick wins when it's pushed (twin-stick); otherwise you face where you move
            var move = input.Move;
            var aim = input.Aim;
            var deadzone = Fix.Ratio(1, 5);
            if (aim.SqrLength > deadzone * deadzone) TurnTowards(p, aim.Normalized, T.AimTurnRate * dt);
            else if (move.SqrLength > deadzone * deadzone) TurnTowards(p, move.Normalized, T.TurnRate * dt);

            p.Stun = Fix.Max(Fix.Zero, p.Stun - dt);
            var control = p.Stun > Fix.Zero ? T.StunControl : Fix.One;
            var speed = p.Stats.MoveSpeed * (carrying ? p.Stats.MoveSpeedWithPuck : Fix.One);
            p.Vel += move * (T.MoveForce * speed * control * dt);
            var braking = !carrying && input.Has(PlayerInput.ChargeBrake);
            var drag = braking ? T.BrakeDrag : T.Drag;
            p.Vel -= p.Vel * Fix.Min(Fix.One, drag * dt);

            var maxDashes = MaxDashes(p);
            if (p.DashCharge < maxDashes) p.DashCharge = Fix.Min(maxDashes, p.DashCharge + dt / DashRecharge(p));
            else if (p.DashCharge > maxDashes) p.DashCharge = maxDashes;
            p.DashTime = Fix.Max(Fix.Zero, p.DashTime - dt);
            p.NoPickup = Fix.Max(Fix.Zero, p.NoPickup - dt);

            if (!carrying)
            {
                p.Charge = Fix.Zero;
                // check: a dash that knocks people back. Not while carrying the puck, and not while dazed.
                if ((pressed & PlayerInput.ShootCheck) != 0 && p.DashCharge >= Fix.One && p.Stun <= Fix.Zero)
                {
                    p.Vel += p.Facing * (T.DashSpeed * p.Stats.DashPower);
                    p.DashCharge -= Fix.One;
                    p.DashTime = T.DashHitTime;
                }
                return;
            }

            // with the puck: shoot/check fires now (with whatever charge there is); charge/brake charges until let go
            if ((pressed & PlayerInput.ShootCheck) != 0) { Shoot(s, i, false); return; }
            var step = dt * Fix.Clamp(p.Stats.ChargeSpeed, Fix.Ratio(1, 10), Fix.FromInt(10)) / T.ChargeTime;
            var full = Fix.One + Fix.Clamp(p.Stats.OverCharge, Fix.Zero, Fix.FromInt(5));
            if ((pressed & PlayerInput.ChargeBrake) != 0) p.Charge = Fix.Min(full, step);
            else if (p.Charge > Fix.Zero)
            {
                if (input.Has(PlayerInput.ChargeBrake)) p.Charge = Fix.Min(full, p.Charge + step);
                else Shoot(s, i, true);
            }
        }

        private static void TurnTowards(PlayerState p, FixVec2 want, Fix share)
        {
            var turned = (p.Facing + (want - p.Facing) * Fix.Min(Fix.One, share)).Normalized;
            p.Facing = turned.SqrLength.Raw > 0 ? turned : want;
        }

        private void Shoot(GameState s, int i, bool released)
        {
            var p = s.Players[i];
            // letting go of charge: the charge decides. Shoot/check: a quick shot, or the charge so far if that's harder.
            var charged = T.ChargedShotMin + (T.ChargedShotMax - T.ChargedShotMin) * p.Charge;
            var quick = T.ShotSpeed * p.Stats.QuickShotPower;
            var speed = (released ? charged : Fix.Max(quick, p.Charge > Fix.Zero ? charged : Fix.Zero)) * p.Stats.ShotPower;
            s.Carrier = -1;
            s.PuckPos = CarrySpot(p);
            s.PuckVel = p.Facing * speed + p.Vel * Fix.Half;
            s.PuckTeam = p.Team;
            p.Charge = Fix.Zero;
            p.NoPickup = T.ShotPickupDelay;
        }

        // ------------------------------------------------------------------ the puck

        /// <summary>
        /// Where a carried puck sits: just in front of the player, or as far out as it can go without touching a wall
        /// (at any angle), so it never sits in the boards and a shot from there can't start outside the rink.
        /// Pressed right up against a wall, that's the player's own centre.
        /// </summary>
        private FixVec2 CarrySpot(PlayerState p)
        {
            var reach = RadiusOf(p) + T.PuckRadius + Fix.Ratio(1, 50);
            if (PuckFits(p.Pos, p.Pos + p.Facing * reach)) return p.Pos + p.Facing * reach;
            // the furthest spot that fits, found by halving (deterministic, a dozen steps is plenty)
            Fix lo = Fix.Zero, hi = reach;
            for (var k = 0; k < 12; k++)
            {
                var mid = (lo + hi) / 2;
                if (PuckFits(p.Pos, p.Pos + p.Facing * mid)) lo = mid; else hi = mid;
            }
            return p.Pos + p.Facing * lo;
        }

        /// <summary>A puck at <paramref name="at"/> touches no wall, and no wall lies between it and <paramref name="from"/>.</summary>
        private bool PuckFits(FixVec2 from, FixVec2 at)
        {
            if (FirstWallHit(from, at) < Fix.One) return false;
            var r2 = T.PuckRadius * T.PuckRadius;
            foreach (var seg in Map.Segments) if ((at - Closest(seg, at)).SqrLength < r2) return false;
            return true;
        }

        private void MovePuck(GameState s, Fix sub)
        {
            if (s.Carrier >= 0)
            {
                var c = s.Players[s.Carrier];
                s.PuckPos = CarrySpot(c);
                s.PuckVel = c.Vel;
                // an opponent checking straight into the carried puck takes it
                for (var j = 0; j < s.Players.Length; j++)
                {
                    var q = s.Players[j];
                    if (j == s.Carrier || q.Team == c.Team || q.DashTime <= Fix.Zero || q.NoPickup > Fix.Zero || s.Phase != Phase.Play) continue;
                    var reach = RadiusOf(q) + T.PuckRadius;
                    if ((s.PuckPos - q.Pos).SqrLength >= reach * reach) continue;
                    Hit(c, (q.Pos - c.Pos).Normalized, Fix.Half);
                    TakePuck(s, j);
                    return;
                }
                return;
            }

            var before = s.PuckPos;
            s.PuckPos += s.PuckVel * sub;
            CollideWalls(ref s.PuckPos, ref s.PuckVel, T.PuckRadius, T.PuckWallBounce);
            KeepPuckIn(s, before);

            // pick-up: of everyone touching the puck who may pick it up, the closest gets it. An exact tie (two players
            // reaching a still puck on the same tick, as at a face-off) is settled by the match's random numbers,
            // never by player order, so neither slot nor team has an edge.
            if (s.Phase == Phase.Play)
            {
                int best = -1, ties = 0;
                var bestD = Fix.Zero;
                for (var i = 0; i < s.Players.Length; i++)
                {
                    var p = s.Players[i];
                    var r = RadiusOf(p) + T.PuckRadius;
                    var d2 = (s.PuckPos - p.Pos).SqrLength;
                    if (d2 >= r * r || p.NoPickup > Fix.Zero) continue;
                    if (best < 0 || d2 < bestD) { best = i; bestD = d2; ties = 1; }
                    else if (d2 == bestD && s.NextRandom() % (uint)(++ties) == 0) best = i; // fair among equals
                }
                if (best >= 0)
                {
                    var p = s.Players[best];
                    s.Carrier = best;
                    s.PuckTeam = p.Team;
                    p.Charge = Fix.Zero;
                    s.PuckPos = CarrySpot(p);
                    s.PuckVel = p.Vel;
                    return;
                }
            }

            for (var i = 0; i < s.Players.Length; i++)
            {
                var p = s.Players[i];
                var r = RadiusOf(p) + T.PuckRadius;
                var d = s.PuckPos - p.Pos;
                if (d.SqrLength >= r * r) continue;
                // can't pick it up right now: it bounces off
                var dist = d.Length;
                var n = dist.Raw > 0 ? d / dist : p.Facing;
                var pushedFrom = s.PuckPos;
                s.PuckPos = p.Pos + n * r;
                var rel = s.PuckVel - p.Vel;
                var vn = FixVec2.Dot(rel, n);
                if (vn < Fix.Zero) s.PuckVel -= n * (vn * (Fix.One + T.PuckWallBounce));
                // a player against the boards mustn't push the puck through them
                CollideWalls(ref s.PuckPos, ref s.PuckVel, T.PuckRadius, T.PuckWallBounce);
                KeepPuckIn(s, pushedFrom);
            }
        }

        /// <summary>
        /// The safety net: if the puck ever ends up outside the rink (or its centre inside a wall), put it back where it
        /// was and send it back the way it came.
        /// </summary>
        private void KeepPuckIn(GameState s, FixVec2 lastGood)
        {
            if (Map.Inside(s.PuckPos)) return;
            s.PuckPos = Map.Inside(lastGood) ? lastGood : Map.PuckSpot;
            s.PuckVel = -s.PuckVel * T.PuckWallBounce;
        }

        private void CheckGoal(GameState s)
        {
            foreach (var g in Map.Goals)
            {
                if (!g.Contains(s.PuckPos)) continue;
                var scorer = 1 - g.Team;
                s.Score[scorer]++;
                s.LastScorer = scorer;
                if (s.Carrier >= 0) { s.PuckVel = s.Players[s.Carrier].Vel; s.Carrier = -1; }
                s.Phase = Phase.Scored;
                s.PhaseTime = T.CelebrateTime;
                return;
            }
        }

        // ------------------------------------------------------------------ bumps between players

        private void Bumps(GameState s)
        {
            var ps = s.Players;
            for (var i = 0; i < ps.Length; i++)
                for (var j = i + 1; j < ps.Length; j++)
                {
                    PlayerState a = ps[i], b = ps[j];
                    var d = b.Pos - a.Pos;
                    var minD = RadiusOf(a) + RadiusOf(b);
                    if (d.SqrLength >= minD * minD) continue;
                    var dist = d.Length;
                    var n = dist.Raw > 0 ? d / dist : FixVec2.Right;
                    Fix wa = WeightOf(a), wb = WeightOf(b), sum = wa + wb;

                    // a check into someone knocks them back, dazes them and knocks the puck loose
                    var closing = FixVec2.Dot(a.Vel - b.Vel, n);
                    bool aChecks = a.DashTime > Fix.Zero && closing > Fix.Zero, bChecks = b.DashTime > Fix.Zero && closing > Fix.Zero;
                    if (aChecks && bChecks)
                    {
                        // head-on: whoever is moving into the other harder wins; a dead heat is a coin toss, not player order
                        Fix aIn = FixVec2.Dot(a.Vel, n), bIn = -FixVec2.Dot(b.Vel, n);
                        if (aIn == bIn) aChecks = (s.NextRandom() & 1) == 0; else aChecks = aIn > bIn;
                        bChecks = !aChecks;
                    }
                    if (aChecks) Knock(s, i, j, n);
                    else if (bChecks) Knock(s, j, i, -n);
                    else if (closing >= T.BumpHitSpeed)
                    {
                        // a plain hard bump: no knockback, but both feel it
                        var power = Fix.Clamp01(closing / T.BumpHitMax);
                        Hit(a, n, power);
                        Hit(b, -n, power);
                    }

                    // push apart (the lighter one moves more) and bounce
                    var overlap = minD - dist;
                    a.Pos -= n * (overlap * wb / sum);
                    b.Pos += n * (overlap * wa / sum);
                    var rv = FixVec2.Dot(b.Vel - a.Vel, n);
                    if (rv < Fix.Zero)
                    {
                        var impulse = -(Fix.One + T.PlayerBounce) * rv * wa * wb / sum;
                        a.Vel -= n * (impulse / wa);
                        b.Vel += n * (impulse / wb);
                    }
                }
        }

        private void Knock(GameState s, int from, int to, FixVec2 n)
        {
            PlayerState a = s.Players[from], b = s.Players[to];
            a.DashTime = Fix.Zero; // one hit per dash
            b.Vel += n * (T.DashKnockback * WeightOf(a) / WeightOf(b) * Fix.Clamp(a.Stats.KnockbackGiven * b.Stats.KnockbackTaken, Fix.Ratio(1, 20), Fix.FromInt(10)));
            b.Stun = T.HitStun;
            Hit(b, -n, Fix.One);
            Hit(a, n, Fix.Ratio(3, 5));
            if (s.Carrier == to)
            {
                // checking the carrier: an opponent takes the puck; a teammate (or someone who can't pick up yet)
                // just pops it loose, gently, rather than sending it flying with the knockback
                if (a.Team != b.Team && a.NoPickup <= Fix.Zero && s.Phase == Phase.Play) TakePuck(s, from);
                else
                {
                    s.Carrier = -1;
                    s.PuckVel = a.Vel * Fix.Half;
                    b.NoPickup = T.StolenPickupDelay;
                    b.Charge = Fix.Zero;
                }
            }
        }

        /// <summary><paramref name="to"/> takes the puck off whoever has it.</summary>
        private void TakePuck(GameState s, int to)
        {
            if (s.Carrier >= 0)
            {
                var old = s.Players[s.Carrier];
                old.NoPickup = T.StolenPickupDelay;
                old.Charge = Fix.Zero;
            }
            var p = s.Players[to];
            s.Carrier = to;
            s.PuckTeam = p.Team;
            p.Charge = Fix.Zero;
            s.PuckPos = CarrySpot(p);
            s.PuckVel = p.Vel;
        }

        /// <summary>Note a hit for the drawing side (it plays an animation when <see cref="PlayerState.Hits"/> goes up).</summary>
        private static void Hit(PlayerState p, FixVec2 towards, Fix power)
        {
            p.Hits++;
            p.HitPower = power;
            p.HitDir = towards;
        }

        // ------------------------------------------------------------------ walls

        /// <summary>Push a circle out of any wall it overlaps and bounce its velocity off.</summary>
        private void CollideWalls(ref FixVec2 pos, ref FixVec2 vel, Fix r, Fix bounce)
        {
            var segs = Map.Segments;
            for (var pass = 0; pass < 2; pass++)
                for (var k = 0; k < segs.Count; k++)
                {
                    var c = Closest(segs[k], pos);
                    var d = pos - c;
                    if (d.SqrLength >= r * r) continue;
                    var dist = d.Length;
                    FixVec2 n;
                    if (dist.Raw > 0) n = d / dist;
                    else n = segs[k].Side; // exactly on the line: push out the side the wall was drawn with
                    pos = c + n * r;
                    var vn = FixVec2.Dot(vel, n);
                    if (vn < Fix.Zero) vel -= n * (vn * (Fix.One + bounce));
                }
        }

        private static FixVec2 Closest(WallSegment s, FixVec2 p)
        {
            var ab = s.B - s.A;
            var len2 = ab.SqrLength;
            if (len2.Raw == 0) return s.A;
            var t = Fix.Clamp01(FixVec2.Dot(p - s.A, ab) / len2);
            return s.A + ab * t;
        }

        /// <summary>How far along from <paramref name="p"/> to <paramref name="q"/> the first wall is (0..1), or 1 if none.</summary>
        public Fix FirstWallHit(FixVec2 p, FixVec2 q)
        {
            var best = Fix.One;
            var r = q - p;
            foreach (var seg in Map.Segments)
            {
                var sv = seg.B - seg.A;
                var denom = FixVec2.Cross(r, sv);
                if (denom.Raw == 0) continue;
                var ap = seg.A - p;
                var t = FixVec2.Cross(ap, sv) / denom;
                var u = FixVec2.Cross(ap, r) / denom;
                if (t >= Fix.Zero && t < best && u >= Fix.Zero && u <= Fix.One) best = t;
            }
            return best;
        }
    }
}
