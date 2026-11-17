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
            var teamOffer = new int[2][];
            foreach (var i in pickers)
            {
                if (i < 0 || i >= s.Players.Length) continue;
                var p = s.Players[i];
                // Team Vote: the first picker on a team draws the offer, and the rest of the team sees the same cards
                if (T.TeamVote && teamOffer[p.Team & 1] != null) p.Offer = (int[])teamOffer[p.Team & 1].Clone();
                else p.Offer = Cards.DrawOffer(s, i, Math.Max(1, Math.Min(5, T.CardsOffered)), T.CardsOff);
                if (T.TeamVote && teamOffer[p.Team & 1] == null) teamOffer[p.Team & 1] = p.Offer;
                p.Picked = -1;
                p.Hover = 0;
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
                p.Hover = input.Hover <= p.Offer.Length ? input.Hover : (byte)0;
                if (input.Pick >= 1 && input.Pick <= p.Offer.Length) p.Picked = p.Offer[input.Pick - 1];
                else waiting = true;
            }
            s.PhaseTime -= dt;
            if (waiting && (T.PickTime <= Fix.Zero || s.PhaseTime > Fix.Zero)) return; // a pick time of 0: no limit

            // Team Vote: each team's most-voted card (a tie, or nobody voting, is settled at random) goes to everyone on it
            if (T.TeamVote) for (var team = 0; team < 2; team++) TeamVoteResult(s, team);

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
                p.Hover = 0;
            }
            Cards.Recompute(s);
            PlacePylons(s);
            if (T.FreePlay) s.Phase = Phase.Play; // free play: carry on where everyone stands
            else FaceOff(s);
        }

        private static void TeamVoteResult(GameState s, int team)
        {
            int[] offer = null;
            foreach (var p in s.Players) if (p.Team == team && p.Offer.Length > 0) { offer = p.Offer; break; }
            if (offer == null) return;
            var votes = new int[offer.Length];
            foreach (var p in s.Players)
                if (p.Team == team && p.Offer.Length > 0 && p.Picked >= 0) { var k = Array.IndexOf(offer, p.Picked); if (k >= 0) votes[k]++; }
            var most = 0;
            foreach (var v in votes) most = Math.Max(most, v);
            var tied = new System.Collections.Generic.List<int>();
            for (var k = 0; k < offer.Length; k++) if (votes[k] == most) tied.Add(k);
            var won = offer[tied[(int)(s.NextRandom() % (uint)tied.Count)]];
            foreach (var p in s.Players) if (p.Team == team && p.Offer.Length > 0) p.Picked = won;
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
            s.Clock = T.TimeLimit;
            FaceOff(s);
            return s;
        }

        /// <summary>
        /// Everyone back to their spawn, the puck to its spot, and the countdown starts. Cards come in here too:
        /// Double Agents start on the other team's side, pylon goalies go up in front of the nets, and the team with
        /// the most Head Start cards starts with the puck.
        /// </summary>
        public void FaceOff(GameState s)
        {
            var count = new int[2];
            // everyone else first, then the Double Agents take the other team's next spawns
            for (var pass = 0; pass < 2; pass++)
                foreach (var p in s.Players)
                {
                    var agent = p.Stats.DoubleAgent > 0;
                    if ((pass == 1) != agent) continue;
                    var side = agent ? 1 - p.Team : p.Team;
                    var sp = Map.SpawnFor(side, count[side]++);
                    p.Pos = sp.Pos;
                    p.Facing = agent ? Map.SpawnFor(p.Team, 0).Facing : sp.Facing; // agents still face the goal they attack
                    p.Vel = FixVec2.Zero;
                    p.DashCharge = MaxDashes(p);
                    p.DashTime = Fix.Zero;
                    p.Charge = Fix.Zero;
                    p.NoPickup = Fix.Zero;
                    p.Stun = Fix.Zero;
                    p.GunCooldown = Fix.Zero; p.InvisCooldown = Fix.Zero; p.InvisibleTime = Fix.Zero; p.ShockCooldown = Fix.Zero;
                    p.SlowTime = Fix.Zero; p.BoostTime = Fix.Zero;
                }
            s.PuckPos = Map.PuckSpot;
            s.PuckVel = FixVec2.Zero;
            s.Carrier = -1;
            s.PuckTeam = -1;
            s.PassFrom = -1;
            s.Shots = new Shot[0];
            s.Ice = new IcePoint[0];
            s.IceLaying = Fix.Zero;
            s.Phase = Phase.Countdown;
            s.PhaseTime = T.CountdownTime;
            PlacePylons(s);
            HeadStart(s);
        }

        /// <summary>Pylon goalies in front of each net: one per Pylon Goalie card on that team, up to three.</summary>
        public void PlacePylons(GameState s)
        {
            var list = new System.Collections.Generic.List<Pylon>();
            for (var team = 0; team < 2; team++)
            {
                var n = 0;
                foreach (var p in s.Players) if (p.Team == team) n += p.Stats.Pylons;
                n = Math.Min(3, n);
                if (n == 0) continue;
                var goal = Bot.GoalCentre(Map, team);
                var inward = Map.PuckSpot.X > goal.X ? Fix.One : -Fix.One;
                var x = goal.X + inward * T.PylonOut;
                for (var k = 0; k < n; k++)
                {
                    // 1: middle; 2: either side; 3: middle and either side
                    Fix y = n == 1 ? Fix.Zero : n == 2 ? (k == 0 ? -T.PylonSpread : T.PylonSpread) : T.PylonSpread * Fix.Ratio(3, 2) * (k - 1);
                    list.Add(new Pylon { Pos = new FixVec2(x, goal.Y + y), Team = team });
                }
            }
            s.Pylons = list.ToArray();
        }

        /// <summary>Head Start: the team with the most copies starts holding the puck (a tie: it starts in the middle).</summary>
        private void HeadStart(GameState s)
        {
            int a = 0, b = 0;
            foreach (var p in s.Players) if (p.Team == 0) a += p.Stats.HeadStart; else b += p.Stats.HeadStart;
            if (a == b) return;
            var team = a > b ? 0 : 1;
            // whoever on that team is nearest the puck spot takes it (the first of them on a tie)
            var best = -1;
            var bestD = Fix.Zero;
            for (var i = 0; i < s.Players.Length; i++)
            {
                if (s.Players[i].Team != team) continue;
                var d = (s.Players[i].Pos - Map.PuckSpot).SqrLength;
                if (best < 0 || d < bestD) { best = i; bestD = d; }
            }
            if (best < 0) return;
            s.Carrier = best;
            s.PuckTeam = team;
            s.PuckPos = CarrySpot(s.Players[best]);
        }

        /// <summary>The puck back to its spot, loose and still; nobody else moves.</summary>
        public void ResetPuck(GameState s)
        {
            s.PuckPos = Map.PuckSpot;
            s.PuckVel = FixVec2.Zero;
            s.Carrier = -1;
            s.PuckTeam = -1;
            s.PassFrom = -1;
            StopLaying(s, false);
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

            // the match clock (timed matches): runs while the puck is in play; level at zero: next goal wins
            if (T.TimeLimit > Fix.Zero && s.Phase == Phase.Play && !s.Overtime)
            {
                s.Clock = Fix.Max(Fix.Zero, s.Clock - dt);
                if (s.Clock <= Fix.Zero)
                {
                    if (s.Score[0] != s.Score[1]) { s.Phase = Phase.GameOver; s.LastScorer = s.Score[0] > s.Score[1] ? 0 : 1; return; }
                    s.Overtime = true;
                }
            }

            var sub = dt / T.Substeps;
            for (var k = 0; k < T.Substeps; k++)
            {
                foreach (var p in s.Players)
                {
                    if (p.HitLag > Fix.Zero) continue; // frozen by a hit
                    p.Pos += p.Vel * sub;
                    CollideWalls(ref p.Pos, ref p.Vel, RadiusOf(p), T.WallBounce);
                    PushPylons(s, p);
                }
                MovePylons(s, sub);
                Bumps(s);
                MovePuck(s, sub);
                MoveShots(s, sub);
                if (s.Phase == Phase.Play) CheckGoal(s);
            }

            // shots run out
            if (s.Shots.Length > 0)
            {
                var alive = new System.Collections.Generic.List<Shot>();
                foreach (var sh in s.Shots) { var x = sh; x.Life -= dt; if (x.Life > Fix.Zero) alive.Add(x); }
                if (alive.Count != s.Shots.Length) s.Shots = alive.ToArray();
                else for (var k = 0; k < alive.Count; k++) s.Shots[k] = alive[k];
            }

            if (s.Ice.Length > 0 || s.IceLaying > Fix.Zero) Ice(s, dt);

            // the puck slows down on its own
            s.PuckVel -= s.PuckVel * Fix.Min(Fix.One, T.PuckDrag * dt);
            s.PuckVel = s.PuckVel.ClampLength(T.PuckMaxSpeed);

            if (s.Phase == Phase.Scored)
            {
                s.PhaseTime -= dt;
                if (s.PhaseTime <= Fix.Zero)
                {
                    if (s.Overtime || (T.TimeLimit <= Fix.Zero && T.GoalsToWin > 0 && (s.Score[0] >= T.GoalsToWin || s.Score[1] >= T.GoalsToWin)))
                    {
                        s.Phase = Phase.GameOver;
                        s.LastScorer = s.Score[0] > s.Score[1] ? 0 : 1;
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
            if (p.HitLag > Fix.Zero)
            {
                // frozen by a hit: no control until it's over (presses made now don't count later either)
                p.HitLag = Fix.Max(Fix.Zero, p.HitLag - dt);
                p.PrevButtons = input.Buttons;
                return;
            }
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
            p.GunCooldown = Fix.Max(Fix.Zero, p.GunCooldown - dt);
            p.InvisCooldown = Fix.Max(Fix.Zero, p.InvisCooldown - dt);
            p.InvisibleTime = Fix.Max(Fix.Zero, p.InvisibleTime - dt);
            p.ShockCooldown = Fix.Max(Fix.Zero, p.ShockCooldown - dt);
            p.SlowTime = Fix.Max(Fix.Zero, p.SlowTime - dt);
            p.BoostTime = Fix.Max(Fix.Zero, p.BoostTime - dt);
            var control = p.Stun > Fix.Zero ? T.StunControl : Fix.One;
            var speed = p.Stats.MoveSpeed * (carrying ? p.Stats.MoveSpeedWithPuck : Fix.One);
            if (p.SlowTime > Fix.Zero) speed *= Fix.One - p.Slow;          // Shockwave
            if (p.BoostTime > Fix.Zero) speed *= T.FriendlyBoost;           // Friendly Fire
            p.Vel += move * (T.MoveForce * speed * control * dt);
            var braking = !carrying && input.Has(PlayerInput.ChargeBrake);
            var drag = braking ? T.BrakeDrag : T.Drag;
            p.Vel -= p.Vel * Fix.Min(Fix.One, drag * dt);

            var maxDashes = MaxDashes(p);
            if (p.DashCharge < maxDashes) p.DashCharge = Fix.Min(maxDashes, p.DashCharge + dt / DashRecharge(p));
            else if (p.DashCharge > maxDashes) p.DashCharge = maxDashes;
            p.DashTime = Fix.Max(Fix.Zero, p.DashTime - dt);
            p.NoPickup = Fix.Max(Fix.Zero, p.NoPickup - dt);

            // utility (Y / Triangle): every utility card you have that's ready
            if ((pressed & PlayerInput.Utility) != 0)
            {
                if (p.Stats.Invisibility > 0 && p.InvisCooldown <= Fix.Zero) { p.InvisibleTime = T.InvisTime; p.InvisCooldown = T.InvisCooldown; }
            }

            if (!carrying)
            {
                p.Charge = Fix.Zero;
                // Puck Gun: brake fires it (when you don't have the puck)
                if ((pressed & PlayerInput.ChargeBrake) != 0 && p.Stats.PuckGun > 0 && p.GunCooldown <= Fix.Zero) FireGun(s, i);
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
            // holding charge/brake as you pick the puck up starts charging straight away
            if (input.Has(PlayerInput.ChargeBrake)) p.Charge = Fix.Min(full, p.Charge + step);
            else if (p.Charge > Fix.Zero) Shoot(s, i, true);
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
            // from where the puck is (it may have been pushed aside), unless it isn't actually at the wedge yet
            var reach = RadiusOf(p) + T.PuckRadius + T.CarrySlack;
            if ((s.PuckPos - p.Pos).SqrLength > reach * reach || !PuckFits(p.Pos, s.PuckPos)) s.PuckPos = CarrySpot(p);
            s.PuckVel = p.Facing * speed + p.Vel * Fix.Half;
            s.PuckTeam = p.Team;
            s.PassFrom = i; // a pass, if a teammate gets it before an opponent touches it
            // Ice Trail: a fully charged shot lays ice behind the puck
            if (released && p.Charge >= Fix.One && p.Stats.IceTrail > 0) StartIce(s, p);
            p.Charge = Fix.Zero;
            p.NoPickup = T.ShotPickupDelay;
        }

        // ------------------------------------------------------------------ Ice Trail

        private void StartIce(GameState s, PlayerState shooter)
        {
            StopLaying(s, false); // a new trail: the last one carries on melting as it was
            s.IceTrails++;
            s.IceLaying = T.IceTime;
            s.IceAge = Fix.Zero;
            AddIce(s, new IcePoint { Pos = s.PuckPos, Trail = s.IceTrails, Team = shooter.Team, Slow = T.IceSlow * Math.Min(3, shooter.Stats.IceTrail), Laying = true, Melt = T.IceDelay });
        }

        private static void AddIce(GameState s, IcePoint p)
        {
            if (s.Ice.Length >= 1200) return; // plenty: a trail is a few hundred points at most
            var a = new IcePoint[s.Ice.Length + 1];
            System.Array.Copy(s.Ice, a, s.Ice.Length);
            a[s.Ice.Length] = p;
            s.Ice = a;
        }

        /// <summary>
        /// The trail being laid is finished. <paramref name="pickedUp"/>: someone took the puck, so if the melting
        /// hasn't started yet it starts now (from the start of the trail, at the same slow pace).
        /// </summary>
        private void StopLaying(GameState s, bool pickedUp)
        {
            var early = pickedUp && s.IceLaying > Fix.Zero && s.IceAge < T.IceDelay ? T.IceDelay - s.IceAge : Fix.Zero;
            s.IceLaying = Fix.Zero;
            for (var i = 0; i < s.Ice.Length; i++)
            {
                if (!s.Ice[i].Laying) continue;
                s.Ice[i].Laying = false;
                s.Ice[i].Melt = Fix.Max(Fix.Ratio(1, 120), s.Ice[i].Melt - early);
            }
        }

        private void Ice(GameState s, Fix dt)
        {
            // laying: a point every so often behind the loose puck, until the time's up or someone picks it up.
            // Each point melts a while after the shot, the melting creeping along the trail slower than it was laid.
            if (s.IceLaying > Fix.Zero)
            {
                s.IceLaying -= dt;
                s.IceAge += dt;
                if (s.Carrier >= 0) StopLaying(s, true);
                else if (s.IceLaying <= Fix.Zero) StopLaying(s, false);
                else
                {
                    var last = s.Ice.Length - 1;
                    while (last >= 0 && !s.Ice[last].Laying) last--;
                    if (last < 0) StopLaying(s, false);
                    else if ((s.PuckPos - s.Ice[last].Pos).SqrLength >= T.IceSpacing * T.IceSpacing)
                    {
                        var p = s.Ice[last];
                        p.Pos = s.PuckPos;
                        p.Melt = T.IceDelay + s.IceAge * (T.IceMeltPace - Fix.One);
                        AddIce(s, p);
                    }
                }
            }
            // melting
            var melted = 0;
            for (var i = 0; i < s.Ice.Length; i++)
            {
                s.Ice[i].Melt -= dt;
                if (s.Ice[i].Melt <= Fix.Zero) melted++;
            }
            if (melted > 0)
            {
                var keep = new IcePoint[s.Ice.Length - melted];
                var j = 0;
                foreach (var p in s.Ice) if (p.Melt > Fix.Zero) keep[j++] = p;
                s.Ice = keep;
            }
            // opponents on the ice are slowed while they're on it
            foreach (var q in s.Players)
            {
                var reach = T.IceRadius + RadiusOf(q) * Fix.Half;
                var slow = Fix.Zero;
                foreach (var p in s.Ice)
                    if (p.Team != q.Team && p.Slow > slow && (q.Pos - p.Pos).SqrLength <= reach * reach) slow = p.Slow;
                if (slow > Fix.Zero) SlowDown(q, slow, Fix.Ratio(1, 5));
            }
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
            var r = T.PuckRadius + WallHalf;
            var r2 = r * r;
            foreach (var seg in Map.Segments) if ((at - Closest(seg, at)).SqrLength < r2) return false;
            return true;
        }

        private void MovePuck(GameState s, Fix sub)
        {
            if (s.Carrier >= 0)
            {
                var c = s.Players[s.Carrier];
                if (!Carry(s, sub)) return; // lost control of it
                if (s.Phase == Phase.Play && Steal(s)) return;
                c = s.Players[s.Carrier];
                // an opponent checking straight into the carried puck knocks it loose
                for (var j = 0; j < s.Players.Length; j++)
                {
                    var q = s.Players[j];
                    if (j == s.Carrier || q.Team == c.Team || q.DashTime <= Fix.Zero || s.Phase != Phase.Play) continue;
                    var reach = RadiusOf(q) + T.PuckRadius;
                    if ((s.PuckPos - q.Pos).SqrLength >= reach * reach) continue;
                    Hit(c, (q.Pos - c.Pos).Normalized, Fix.Half);
                    var dir = q.Vel.SqrLength.Raw > 0 ? q.Vel.Normalized : q.Facing;
                    KnockLoose(s, s.Carrier, dir, j);
                    q.DashTime = Fix.Zero; // that was the check
                    return;
                }
                return;
            }

            var before = s.PuckPos;
            var steps = 1;
            var travel = (s.PuckVel * sub).Length;
            var most = T.PuckRadius * Fix.Half;
            if (travel > most) steps = Math.Min(8, (travel / most).FloorToInt() + 1);
            var part = sub / steps;
            for (var k = 0; k < steps; k++)
            {
                var from = s.PuckPos;
                s.PuckPos += s.PuckVel * part;
                // never through a wall: if the step crossed one, stop just before it and bounce
                var hit = FirstWallHit(from, s.PuckPos);
                if (hit < Fix.One) s.PuckPos = from;
                CollideWalls(ref s.PuckPos, ref s.PuckVel, T.PuckRadius, T.PuckWallBounce);
                if (hit < Fix.One && FixVec2.Dot(s.PuckVel, s.PuckPos - from) >= Fix.Zero) s.PuckVel = -s.PuckVel * T.PuckWallBounce;
                CollidePylons(s, ref s.PuckPos, ref s.PuckVel, T.PuckRadius, T.PuckWallBounce);
            }
            KeepPuckIn(s, before);

            // a pass doesn't count once an opponent of the passer has touched (or nearly touched) the puck
            if (s.PassFrom >= 0 && s.PassFrom < s.Players.Length)
            {
                var passer = s.Players[s.PassFrom].Team;
                foreach (var q in s.Players)
                {
                    if (q.Team == passer) continue;
                    var reach = RadiusOf(q) + T.PuckRadius + T.PassTouch;
                    if ((s.PuckPos - q.Pos).SqrLength < reach * reach) { s.PassFrom = -1; break; }
                }
            }

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
                    if (s.PassFrom >= 0 && s.PassFrom != best && s.PassFrom < s.Players.Length && s.Players[s.PassFrom].Team == p.Team) Passed(s, s.PassFrom);
                    s.PassFrom = -1;
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
                // only a soft nudge: a player who can't pick it up yet (just shot it, or just lost it) shouldn't send it flying
                var rel = s.PuckVel - p.Vel;
                var vn = FixVec2.Dot(rel, n);
                if (vn < Fix.Zero) s.PuckVel -= n * (vn * Fix.Ratio(11, 10));
                // a player against the boards mustn't push the puck through them
                if (FirstWallHit(pushedFrom, s.PuckPos) < Fix.One) s.PuckPos = pushedFrom;
                CollideWalls(ref s.PuckPos, ref s.PuckVel, T.PuckRadius, T.PuckWallBounce);
                KeepPuckIn(s, pushedFrom);
            }
        }

        /// <summary>
        /// Where the carried puck goes this substep. Out in the open it sits at the front of the wedge. When something
        /// is in the way (the boards, a pylon, another player's body) it gets pushed about: into the boards it slides
        /// off to the side rather than into you, and once it's pushed far enough round (or away) you lose it.
        /// Returns false if the carrier lost control.
        /// </summary>
        private bool Carry(GameState s, Fix sub)
        {
            var ci = s.Carrier;
            var c = s.Players[ci];
            var reach = RadiusOf(c) + T.PuckRadius + Fix.Ratio(1, 50);
            var spot = c.Pos + c.Facing * reach;
            FixVec2 pos;
            if (!CarryBlocked(s, ci, spot)) pos = spot;
            else
            {
                // blocked: carry on from where it was, drifting back towards the front, kept at the wedge's edge
                pos = s.PuckPos + (spot - s.PuckPos) * T.CarryFollow;
                var off = pos - c.Pos;
                pos = off.SqrLength.Raw > 0 ? c.Pos + off.Normalized * reach : spot;
                var side = Fix.Zero;
                for (var iter = 0; iter < 3; iter++)
                {
                    var before = pos;
                    // other players' bodies push it
                    for (var j = 0; j < s.Players.Length; j++)
                    {
                        if (j == ci) continue;
                        var q = s.Players[j];
                        var min = RadiusOf(q) + T.PuckRadius;
                        var d = pos - q.Pos;
                        if (d.SqrLength >= min * min) continue;
                        var dist = d.Length;
                        pos = q.Pos + (dist.Raw > 0 ? d / dist : c.Facing) * min;
                    }
                    var v = FixVec2.Zero;
                    CollideWalls(ref pos, ref v, T.PuckRadius, Fix.Zero);
                    CollidePylons(s, ref pos, ref v, T.PuckRadius, Fix.Zero);
                    // squeezed back into the carrier: slide it round the wedge, to whichever side it's already on
                    var o = pos - c.Pos;
                    var od = o.Length;
                    if (od < reach - Fix.Ratio(1, 100))
                    {
                        var dir = od.Raw > 0 ? o / od : c.Facing;
                        if (side == Fix.Zero)
                        {
                            var cross = FixVec2.Cross(c.Facing, dir);
                            side = cross > Fix.Zero ? Fix.One : cross < Fix.Zero ? -Fix.One : ((s.NextRandom() & 1) == 0 ? Fix.One : -Fix.One);
                        }
                        var tangent = new FixVec2(-dir.Y, dir.X) * side;
                        var slid = dir * reach + tangent * (reach - od + Fix.Ratio(1, 50));
                        pos = c.Pos + slid.Normalized * reach;
                    }
                    if (pos == before) break;
                }
            }

            if (FirstWallHit(c.Pos, pos) < Fix.One) pos = CarrySpot(c);

            // still in control? Not if it's been pushed too far round the wedge or away from it
            var away = pos - c.Pos;
            var len = away.Length;
            var lost = len > reach + T.CarrySlack || (len.Raw > 0 && FixVec2.Dot(away / len, c.Facing) < T.CarryHoldCos);
            s.PuckPos = pos;
            s.PuckVel = c.Vel;
            if (!lost) return true;
            s.Carrier = -1;
            s.PassFrom = -1;
            s.PuckVel = c.Vel * Fix.Half + (len.Raw > 0 ? away / len : c.Facing) * T.LoseControlSpeed;
            c.NoPickup = Fix.Max(c.NoPickup, T.LoseControlDelay);
            c.Charge = Fix.Zero;
            return false;
        }

        /// <summary>Would a carried puck at <paramref name="at"/> touch the boards, a pylon or another player (or be the other side of a wall)?</summary>
        private bool CarryBlocked(GameState s, int carrier, FixVec2 at)
        {
            if (!PuckFits(s.Players[carrier].Pos, at)) return true;
            foreach (var py in s.Pylons)
            {
                var r = T.PylonRadius + T.PuckRadius;
                if ((at - py.Pos).SqrLength < r * r) return true;
            }
            for (var j = 0; j < s.Players.Length; j++)
            {
                if (j == carrier) continue;
                var r = RadiusOf(s.Players[j]) + T.PuckRadius;
                if ((at - s.Players[j].Pos).SqrLength < r * r) return true;
            }
            return false;
        }

        /// <summary>
        /// An opponent who gets the tip of their wedge onto the carried puck takes it (no bump needed). Of several,
        /// the closest; an exact tie is a coin toss. Returns true if the puck changed hands.
        /// </summary>
        private bool Steal(GameState s)
        {
            var c = s.Players[s.Carrier];
            int best = -1, ties = 0;
            var bestD = Fix.Zero;
            for (var j = 0; j < s.Players.Length; j++)
            {
                var q = s.Players[j];
                if (j == s.Carrier || q.Team == c.Team || q.NoPickup > Fix.Zero || q.Stun > Fix.Zero) continue;
                var tip = q.Pos + q.Facing * RadiusOf(q);
                var r = T.PuckRadius + T.StealReach;
                var d2 = (s.PuckPos - tip).SqrLength;
                if (d2 >= r * r) continue;
                if (best < 0 || d2 < bestD) { best = j; bestD = d2; ties = 1; }
                else if (d2 == bestD && s.NextRandom() % (uint)(++ties) == 0) best = j;
            }
            if (best < 0) return false;
            TakePuck(s, best);
            return true;
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
                if (!g.Touches(s.PuckPos, T.PuckRadius)) continue; // the moment the puck touches the goal box
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
                        // a plain hard bump: no knockback, but both feel it (never a lightning-sized hit: that's for checks)
                        var power = Fix.Clamp01(closing / T.BumpHitMax);
                        Hit(a, n, Fix.Min(power, Fix.Ratio(7, 10)));
                        Hit(b, -n, Fix.Min(power, Fix.Ratio(7, 10)));
                        if (power > Fix.Ratio(3, 10)) { var lag = T.HitLagMax * power * Fix.Half; a.HitLag = Fix.Max(a.HitLag, lag); b.HitLag = Fix.Max(b.HitLag, lag); }
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
            // how hard it was: the checker's speed into the other player, times how much heavier the checker is
            // (weight, Knockback Given and Taken). An even check is a solid hit; a much heavier checker's is a big one.
            var hard = Fix.Clamp(FixVec2.Dot(a.Vel - b.Vel, n) / (T.DashSpeed * Fix.Ratio(3, 2)), Fix.Ratio(2, 5), Fix.One);
            var heavier = WeightOf(a) / WeightOf(b) * Fix.Clamp(a.Stats.KnockbackGiven * b.Stats.KnockbackTaken, Fix.Ratio(1, 20), Fix.FromInt(10));
            var force = hard * heavier;
            var lag = T.HitLagMax * Fix.Clamp(force, Fix.Ratio(2, 5), Fix.One);
            a.HitLag = Fix.Max(a.HitLag, lag);
            b.HitLag = Fix.Max(b.HitLag, lag);
            b.Vel += n * (T.DashKnockback * heavier);
            // what the screen shows: an even check at full speed is 0.6; lightning needs a clearly heavier checker
            var power = Fix.Clamp(force * Fix.Ratio(3, 5), Fix.Ratio(2, 5), Fix.One);
            if (a.Team == b.Team && b.Stats.FriendlyFire > 0)
            {
                // Friendly Fire: a teammate's check launches you with a speed boost instead of dazing you (and you keep the puck)
                b.BoostTime = T.FriendlyTime;
                Hit(b, -n, Fix.Ratio(1, 2));
                Hit(a, n, Fix.Ratio(2, 5));
                return;
            }
            // no daze from a check (only the Puck Gun stuns): the hit lag above is the pause
            if (a.Team != b.Team) Shockwave(s, from, to);
            Hit(b, -n, power);
            Hit(a, n, power * Fix.Half);
            if (s.Carrier == to)
            {
                // checking the carrier knocks the puck loose: it pops out the way the check was going, and nobody
                // owns it until someone picks it up (the checker has to wait a moment, so it doesn't just change hands)
                KnockLoose(s, to, n, from);
            }
        }

        /// <summary>
        /// The carrier <paramref name="carrier"/> loses the puck: it pops loose in direction <paramref name="dir"/>.
        /// <paramref name="by"/> (the checker, or -1) can't grab it for a moment.
        /// </summary>
        private void KnockLoose(GameState s, int carrier, FixVec2 dir, int by)
        {
            var c = s.Players[carrier];
            s.Carrier = -1;
            s.PassFrom = -1;
            s.PuckVel = c.Vel * Fix.Half + dir * T.KnockLooseSpeed;
            c.NoPickup = T.StolenPickupDelay;
            c.Charge = Fix.Zero;
            if (by >= 0) s.Players[by].NoPickup = Fix.Max(s.Players[by].NoPickup, T.CheckerPickupDelay);
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
            s.PassFrom = -1;
            p.Charge = Fix.Zero;
            s.PuckPos = CarrySpot(p);
            s.PuckVel = p.Vel;
        }

        // ------------------------------------------------------------------ card mechanics

        /// <summary>Puck Gun: fire a shot from in front of the player.</summary>
        private void FireGun(GameState s, int i)
        {
            var p = s.Players[i];
            p.GunCooldown = T.GunCooldown;
            var start = p.Pos + p.Facing * (RadiusOf(p) + T.ShotRadius + Fix.Ratio(1, 20));
            if (!PuckFits(p.Pos, start)) start = p.Pos; // against a wall: it starts from the middle and hits the wall at once
            var shots = new Shot[s.Shots.Length + 1];
            Array.Copy(s.Shots, shots, s.Shots.Length);
            shots[shots.Length - 1] = new Shot { Pos = start, Vel = p.Facing * T.GunSpeed + p.Vel * Fix.Half, Owner = i, Life = T.GunLife };
            s.Shots = shots;
        }

        /// <summary>Shots fly straight; the first player they touch (not the shooter) is stunned and drops the puck; walls and pylons stop them.</summary>
        private void MoveShots(GameState s, Fix sub)
        {
            if (s.Shots.Length == 0) return;
            var keep = new System.Collections.Generic.List<Shot>(s.Shots.Length);
            foreach (var sh0 in s.Shots)
            {
                var sh = sh0;
                var from = sh.Pos;
                sh.Pos += sh.Vel * sub;
                var gone = FirstWallHit(from, sh.Pos) < Fix.One || !Map.Inside(sh.Pos);
                var sr = T.ShotRadius + WallHalf;
                foreach (var w in Map.Segments) if (!gone && (sh.Pos - Closest(w, sh.Pos)).SqrLength < sr * sr) gone = true;
                foreach (var py in s.Pylons)
                {
                    var r = T.PylonRadius + T.ShotRadius;
                    if (!gone && (sh.Pos - py.Pos).SqrLength < r * r) gone = true;
                }
                // the puck: a loose one gets knocked along; a carried one is knocked loose
                var pr = T.PuckRadius + T.ShotRadius;
                if (!gone && (s.Carrier < 0 || s.Carrier != sh.Owner) && (sh.Pos - s.PuckPos).SqrLength < pr * pr)
                {
                    var dir = sh.Vel.Normalized;
                    if (s.Carrier >= 0) KnockLoose(s, s.Carrier, dir, -1);
                    s.PuckVel += dir * T.GunPuckPush;
                    s.PassFrom = -1;
                    gone = true;
                }
                if (!gone)
                    for (var j = 0; j < s.Players.Length; j++)
                    {
                        if (j == sh.Owner) continue;
                        var q = s.Players[j];
                        var r = RadiusOf(q) + T.ShotRadius;
                        if ((sh.Pos - q.Pos).SqrLength >= r * r) continue;
                        q.Stun = Fix.Max(q.Stun, T.GunStun);
                        q.Vel = q.Vel * Fix.Ratio(3, 10) + sh.Vel.Normalized * T.GunKnockback;
                        Hit(q, (sh.Pos - q.Pos).Normalized, Fix.Ratio(4, 5));
                        if (s.Carrier == j)
                        {
                            s.Carrier = -1;
                            s.PassFrom = -1;
                            s.PuckVel = q.Vel;
                            q.NoPickup = T.StolenPickupDelay;
                            q.Charge = Fix.Zero;
                        }
                        gone = true;
                        break;
                    }
                if (!gone) keep.Add(sh);
            }
            s.Shots = keep.ToArray();
        }

        /// <summary>Pylon goalies are solid: push a circle out of any it overlaps and bounce it off.</summary>
        /// <summary>Pylon goalies slide when pushed, slow down quickly, and stop at the boards.</summary>
        private void MovePylons(GameState s, Fix sub)
        {
            for (var k = 0; k < s.Pylons.Length; k++)
            {
                var py = s.Pylons[k];
                if (py.Vel.SqrLength.Raw == 0) continue;
                py.Pos += py.Vel * sub;
                py.Vel -= py.Vel * Fix.Min(Fix.One, T.PylonDrag * sub);
                if (py.Vel.SqrLength < Fix.Ratio(1, 400)) py.Vel = FixVec2.Zero;
                CollideWalls(ref py.Pos, ref py.Vel, T.PylonRadius, T.WallBounce);
                s.Pylons[k] = py;
            }
        }

        /// <summary>A player and a pylon goalie push each other apart: the pylon is heavier, but it moves.</summary>
        private void PushPylons(GameState s, PlayerState p)
        {
            var r = RadiusOf(p);
            for (var k = 0; k < s.Pylons.Length; k++)
            {
                var py = s.Pylons[k];
                var d = p.Pos - py.Pos;
                var min = r + T.PylonRadius;
                if (d.SqrLength >= min * min) continue;
                var dist = d.Length;
                var n = dist.Raw > 0 ? d / dist : FixVec2.Right;
                Fix wp = WeightOf(p), wy = T.PylonWeight, sum = wp + wy;
                var overlap = min - dist;
                p.Pos += n * (overlap * wy / sum);
                py.Pos -= n * (overlap * wp / sum);
                var rv = FixVec2.Dot(p.Vel - py.Vel, n);
                if (rv < Fix.Zero)
                {
                    var impulse = -(Fix.One + T.WallBounce) * rv * wp * wy / sum;
                    p.Vel += n * (impulse / wp);
                    py.Vel -= n * (impulse / wy);
                }
                var v = py.Vel;
                CollideWalls(ref py.Pos, ref v, T.PylonRadius, T.WallBounce);
                py.Vel = v;
                s.Pylons[k] = py;
            }
        }

        private void CollidePylons(GameState s, ref FixVec2 pos, ref FixVec2 vel, Fix r, Fix bounce)
        {
            foreach (var py in s.Pylons)
            {
                var d = pos - py.Pos;
                var min = r + T.PylonRadius;
                if (d.SqrLength >= min * min) continue;
                var dist = d.Length;
                var n = dist.Raw > 0 ? d / dist : FixVec2.Right;
                pos = py.Pos + n * min;
                var vn = FixVec2.Dot(vel, n);
                if (vn < Fix.Zero) vel -= n * (vn * (Fix.One + bounce));
            }
        }

        /// <summary>
        /// Shockwave: <paramref name="from"/> checked <paramref name="to"/>. If they have the card and it's ready,
        /// <paramref name="to"/> and every opponent near them is slowed, and the others are pushed away.
        /// </summary>
        private void Shockwave(GameState s, int from, int to)
        {
            PlayerState a = s.Players[from], b = s.Players[to];
            var copies = Math.Min(3, a.Stats.Shockwave);
            if (copies <= 0 || a.ShockCooldown > Fix.Zero) return;
            a.ShockCooldown = T.ShockCooldown;
            var slow = T.ShockSlow + T.ShockSlowPerCopy * (copies - 1);
            var time = T.ShockTime + T.ShockTimePerCopy * (copies - 1);
            var radius = RadiusOf(b) * T.ShockRange * (Fix.One + Fix.Ratio(15, 100) * (copies - 1));
            SlowDown(b, slow, time);
            for (var j = 0; j < s.Players.Length; j++)
            {
                var q = s.Players[j];
                if (j == to || q.Team == a.Team) continue;
                var d = q.Pos - b.Pos;
                var dist = d.Length;
                if (dist > radius + RadiusOf(q)) continue;
                var n = dist.Raw > 0 ? d / dist : FixVec2.Right;
                var falloff = Fix.One - Fix.Half * Fix.Clamp01(dist / radius);
                q.Vel += n * (T.DashKnockback * T.ShockPush * falloff);
                SlowDown(q, slow, time);
                Hit(q, -n, Fix.Half);
            }
            a.Shocks++;
            a.ShockAt = b.Pos;
            a.ShockRadius = radius;
        }

        /// <summary>A stronger or longer slow replaces a weaker one.</summary>
        private static void SlowDown(PlayerState p, Fix slow, Fix time)
        {
            if (p.SlowTime > Fix.Zero && p.Slow >= slow && p.SlowTime >= time) return;
            p.Slow = Fix.Max(p.Slow * (p.SlowTime > Fix.Zero ? Fix.One : Fix.Zero), slow);
            p.SlowTime = Fix.Max(p.SlowTime, time);
        }

        /// <summary>Team Play: a clean pass from <paramref name="passer"/> reached a teammate.</summary>
        private void Passed(GameState s, int passer)
        {
            var p = s.Players[passer];
            if (p.Stats.PassRefreshTeam > 0)
            {
                foreach (var q in s.Players) if (q.Team == p.Team) Refresh(q);
            }
            else if (p.Stats.PassRefresh > 0) Refresh(p);
        }

        /// <summary>Every cooldown back to ready: checks, Puck Gun, Invisibility, Shockwave.</summary>
        private static void Refresh(PlayerState p)
        {
            p.DashCharge = MaxDashes(p);
            p.GunCooldown = Fix.Zero;
            if (p.InvisibleTime <= Fix.Zero) p.InvisCooldown = Fix.Zero;
            p.ShockCooldown = Fix.Zero;
            p.Refreshes++;
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
            r += WallHalf;
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

        /// <summary>
        /// The point on a wall nearest <paramref name="p"/>. Measured from the wall's middle, not an end, so a wall that
        /// is its own mirror image (one straight wall across both halves of the rink) gives exactly mirrored answers
        /// for mirrored points: the rules must never favour a side.
        /// </summary>
        /// <summary>Half the wall thickness: how far from a wall's middle line things stop.</summary>
        private Fix WallHalf { get { return T.WallThickness * Fix.Half; } }

        private static FixVec2 Closest(WallSegment s, FixVec2 p)
        {
            var half = (s.B - s.A) * Fix.Half;
            var mid = s.A + half;
            var len2 = half.SqrLength;
            if (len2.Raw == 0) return mid;
            var t = Fix.Clamp(FixVec2.Dot(p - mid, half) / len2, -Fix.One, Fix.One);
            return mid + half * t;
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
