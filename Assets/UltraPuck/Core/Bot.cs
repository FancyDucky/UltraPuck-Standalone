namespace UltraPuck.Core
{
    /// <summary>
    /// A computer player. It only reads the state and returns controls, using the same fixed-point maths as the
    /// rules, so a bot plays exactly the same on every PC (bots in online games, and rollback replays, stay in step).
    ///
    /// What it does: the player nearest the loose puck chases it and checks whoever has it; the next one hangs back
    /// between the puck and its own goal; with the puck it carries it towards the other goal, aiming with the aim
    /// stick, charges and lets go once lined up. Teammates of the carrier spread out. A little variety, seeded from
    /// the match, keeps every game from playing out the same.
    /// </summary>
    public static class Bot
    {
        public static PlayerInput Think(Simulation sim, GameState s, int i)
        {
            if (sim == null || s == null || i < 0 || i >= s.Players.Length) return default(PlayerInput);
            var me = s.Players[i];
            if (s.Phase == Phase.Picking)
            {
                var pick = PickCard(sim, s, me);
                return new PlayerInput { Pick = pick, Hover = pick };
            }
            var pos = me.Pos;
            var attack = GoalCentre(sim.Map, 1 - me.Team);
            var defend = GoalCentre(sim.Map, me.Team);
            var dir = attack.X > defend.X ? Fix.One : -Fix.One;

            // variety: a number that changes every quarter of a second, different for every player and match
            var roll = Hash((uint)s.Rng * 2654435761u ^ (uint)(i * 40503) ^ (uint)(s.Tick / 30) * 97u);
            var wobble = Fix.Ratio((int)(roll % 2001) - 1000, 1000); // -1..1

            FixVec2 target, aim = FixVec2.Zero;
            byte b = 0;
            var rockHeld = -1;
            for (var k = 0; k < s.Rocks.Length; k++) if (s.Rocks[k].Carrier == i) { rockHeld = k; break; }
            if (s.Carrier == i)
            {
                // aim somewhere in the goal mouth, not always dead centre (a net that says which way it opens: across its mouth)
                var net = AttackGoal(sim.Map, me.Team);
                var spot = attack + new FixVec2(Fix.Zero, wobble * Fix.Ratio(3, 2));
                if (net != null && net.HasFacing) spot = attack + Across(sim.Map, net, attack) * (wobble * Fix.Ratio(3, 2));
                target = spot;
                var toGoal = spot - pos;
                aim = toGoal.Normalized;
                var aimed = FixVec2.Dot(me.Facing, aim) > Fix.Ratio(97, 100);
                var range = Fix.FromInt(12) + wobble * Fix.Two;
                var shooting = true;
                // a wall between us and the goal, or a teammate who's open further ahead: pass instead of forcing a shot
                var mate = PassTo(sim, s, i, attack, defend);
                var blocked = sim.Map.Route.Count > 1 && sim.Map.Blocked(pos, spot);
                var pressured = OpponentNear(s, i, Fix.FromInt(4));
                if (mate >= 0 && (blocked || pressured || (roll % 5u) == 0u))
                {
                    var to = s.Players[mate].Pos - pos;
                    aim = to.Normalized;
                    aimed = FixVec2.Dot(me.Facing, aim) > Fix.Ratio(92, 100);
                    // a quick pass (don't charge): lined up and not right on top of them
                    if (aimed && to.SqrLength > Fix.FromInt(4) && to.SqrLength < Fix.FromInt(225) && (s.Tick & 1) == 0)
                        b |= PlayerInput.ShootCheck;
                    shooting = false;
                    // skate a little towards them so the pass isn't from a standstill facing the wrong way
                    target = s.Players[mate].Pos;
                }
                // a net out on the ice: from beside or behind it, skate round to the front first (and don't shoot into its side)
                if (shooting && net != null && net.HasFacing)
                {
                    // the map says which way the net opens: go round to that side
                    var f = net.Facing;
                    var off = pos - attack;
                    if (FixVec2.Dot(off, f) < Fix.Two)
                    {
                        var across = Across(sim.Map, net, attack);
                        var side = FixVec2.Dot(off, across) >= Fix.Zero ? Fix.One : -Fix.One;
                        target = attack + f * Fix.FromInt(5) + across * (side * Fix.FromInt(4));
                        aim = FixVec2.Zero;
                        aimed = false;
                    }
                }
                else if (shooting && (dir > Fix.Zero ? pos.X > attack.X - Fix.FromInt(2) : pos.X < attack.X + Fix.FromInt(2)))
                {
                    target = new FixVec2(attack.X - dir * Fix.FromInt(5), pos.Y >= Fix.Zero ? Fix.FromInt(5) : Fix.FromInt(-5));
                    aim = FixVec2.Zero;
                    aimed = false;
                }
                // a wall between us and the goal (round a bend): follow the ice, don't shoot yet
                if (shooting && blocked)
                {
                    target = AlongRoute(sim.Map, pos, target);
                    aim = FixVec2.Zero;
                    aimed = false;
                }
                if (shooting)
                {
                    var ready = aimed && me.Charge > Fix.Ratio(3, 10) && (toGoal.Length < range || me.Charge >= Fix.One);
                    // charging starts on a press, so if it hasn't started, let go for a tick and press again
                    if (!ready && (me.Charge > Fix.Zero || (s.Tick & 1) == 0)) b |= PlayerInput.ChargeBrake;
                }
            }
            else if (rockHeld >= 0)
            {
                // carrying a Swap rock: skate toward the attack goal and shoot it (don't hold it forever)
                target = attack;
                aim = (attack - pos).Normalized;
                if (FixVec2.Dot(me.Facing, aim) > Fix.Ratio(9, 10) && (s.Tick & 1) == 0) b |= PlayerInput.ShootCheck;
            }
            else if (s.Carrier >= 0 && s.Players[s.Carrier].Team == me.Team)
            {
                // a teammate has it: get open up the rink, one high and one low (on a bent rink: further along the ice)
                var y = Fix.FromInt(TeamRank(s, i) % 2 == 0 ? 5 : -5);
                target = new FixVec2(dir * Fix.FromInt(7), y);
                if (sim.Map.Route.Count > 1) target = RoutePart(sim.Map, s.Players[s.Carrier].Pos, me.Team == 0 ? sim.Map.Route.Count - 1 : 0, Fix.Ratio(1, 2) + wobble * Fix.Ratio(1, 10));
            }
            else if (NearestToPuck(s, i))
            {
                target = s.PuckPos;
                var close = (s.PuckPos - pos).SqrLength < Fix.FromInt(9);
                if (s.Carrier >= 0 && close && me.DashCharge >= Fix.One) b |= PlayerInput.ShootCheck;
            }
            else
            {
                // hang back between the puck and our goal (on a bent rink: along the ice, not through the walls)
                target = defend + (s.PuckPos - defend) * (Fix.Ratio(2, 5) + wobble * Fix.Ratio(1, 10));
                if (sim.Map.Route.Count > 1) target = RoutePart(sim.Map, s.PuckPos, me.Team == 0 ? 0 : sim.Map.Route.Count - 1, Fix.Ratio(3, 5) + wobble * Fix.Ratio(1, 10));
                if (s.Carrier >= 0 && s.Players[s.Carrier].Team != me.Team
                    && (s.Players[s.Carrier].Pos - pos).SqrLength < Fix.FromInt(4) && me.DashCharge >= Fix.One)
                    b |= PlayerInput.ShootCheck;
            }

            // utility cards: shockwave when foes are close; go invisible with the puck; Puck Gun aims at a carrier and fires
            if (me.Stats.Shockwave > 0 && me.ShockCooldown <= Fix.Zero)
            {
                // fire when a foe is inside about one shockwave (player radius × ShockRange)
                var reachSq = sim.T.PlayerRadius * sim.T.ShockRange;
                reachSq *= reachSq;
                var near = false;
                foreach (var q in s.Players)
                    if (q.Team != me.Team && (q.Pos - pos).SqrLength < reachSq) { near = true; break; }
                if (near && (s.Tick & 1) == 0) b |= PlayerInput.Utility;
            }
            if (s.Carrier == i && me.Stats.Invisibility > 0 && me.InvisCooldown <= Fix.Zero && (s.Tick & 1) == 0) b |= PlayerInput.Utility;
            // Swap: bots wait SwapBotCooldown between uses. Throw when a foe is ahead; teleport when the rock is usefully far.
            if (me.Stats.Swap > 0 && me.SwapCooldown <= Fix.Zero && rockHeld < 0)
            {
                var hasRock = false;
                Rock rock = default(Rock);
                foreach (var rk in s.Rocks) if (rk.Owner == i) { hasRock = true; rock = rk; break; }
                if (!hasRock)
                {
                    var foeAhead = false;
                    foreach (var q in s.Players)
                    {
                        if (q.Team == me.Team) continue;
                        var to = q.Pos - pos;
                        if (to.SqrLength < Fix.FromInt(100) && FixVec2.Dot(to.Normalized, me.Facing) > Fix.Half) { foeAhead = true; break; }
                    }
                    if (foeAhead && (s.Tick & 7) == 0) b |= PlayerInput.Utility;
                }
                else
                {
                    var d2 = (rock.Pos - pos).SqrLength;
                    // swap when the rock is clearly away and closer to the attack goal than we are (or very far)
                    if (d2 > Fix.FromInt(25) && (s.Tick & 7) == 0
                        && ((rock.Pos - attack).SqrLength < (pos - attack).SqrLength || d2 > Fix.FromInt(64)))
                        b |= PlayerInput.Utility;
                }
            }
            if (me.Stats.PuckGun > 0 && me.GunCooldown <= Fix.Zero && s.Carrier >= 0 && s.Players[s.Carrier].Team != me.Team)
            {
                var to = s.Players[s.Carrier].Pos - pos;
                if (to.SqrLength < Fix.FromInt(144))
                {
                    aim = to.Normalized;
                    if (FixVec2.Dot(me.Facing, aim) > Fix.Ratio(95, 100) && (s.Tick & 1) == 0) b |= PlayerInput.ChargeBrake; // brake fires the Puck Gun
                }
            }

            // on a bent rink, anywhere we can't see straight to: go by the ice
            if (s.Carrier != i && sim.Map.Route.Count > 1 && sim.Map.Blocked(pos, target)) target = AlongRoute(sim.Map, pos, target);

            var m = target - pos;
            if (m.SqrLength > Fix.One) m = m.Normalized;
            return new PlayerInput { MoveX = Axis(m.X), MoveY = Axis(m.Y), AimX = Axis(aim.X), AimY = Axis(aim.Y), Buttons = b };
        }

        /// <summary>
        /// Team Vote: follow whatever most humans on the team are on (a locked pick, else their hover). If no human is
        /// on this team yet (1v1 bot side, or all-bot team), or Team Vote is off: take the strongest card by tier
        /// (first on a tie). Returns the 1-based pick, or 0 while waiting for a human teammate to look.
        /// </summary>
        private static byte PickCard(Simulation sim, GameState s, PlayerState me)
        {
            if (me.Offer.Length == 0 || me.Picked >= 0 || sim.Cards == null) return 0;
            if (sim.T.TeamVote)
            {
                var humans = false;
                var votes = new int[me.Offer.Length];
                var anyLook = false;
                foreach (var p in s.Players)
                {
                    if (p.Team != me.Team || p.Offer.Length == 0) continue;
                    if (!p.Bot) humans = true;
                    if (p.Bot) continue;
                    var idx = -1;
                    if (p.Picked >= 0) idx = System.Array.IndexOf(me.Offer, p.Picked);
                    else if (p.Hover >= 1 && p.Hover <= me.Offer.Length) idx = p.Hover - 1;
                    if (idx < 0) continue;
                    votes[idx]++;
                    anyLook = true;
                }
                if (humans)
                {
                    if (!anyLook) return 0; // wait for a human teammate to look / lock
                    var best = 0;
                    for (var k = 1; k < votes.Length; k++) if (votes[k] > votes[best]) best = k;
                    return (byte)(best + 1);
                }
                // no human on this team: pick by tier like a normal bot pick
            }
            var tier = 0;
            for (var k = 1; k < me.Offer.Length; k++)
                if (sim.Cards.Cards[me.Offer[k]].Tier < sim.Cards.Cards[me.Offer[tier]].Tier) tier = k;
            return (byte)(tier + 1);
        }

        /// <summary>The goal a player on <paramref name="team"/> shoots at.</summary>
        private static GoalDef AttackGoal(MapDef map, int team)
        {
            foreach (var g in map.Goals) if (g.Team == 1 - team) return g;
            return null;
        }

        /// <summary>
        /// Along the mouth of a net that says which way it opens (its middle at <paramref name="centre"/>): the way
        /// that leans towards the puck's spot, so on a rink that's a mirror image of itself the two nets' "across"
        /// mirror each other and neither team is favoured.
        /// </summary>
        private static FixVec2 Across(MapDef map, GoalDef net, FixVec2 centre)
        {
            var across = new FixVec2(-net.Facing.Y, net.Facing.X);
            return FixVec2.Dot(across, map.PuckSpot - centre) < Fix.Zero ? -across : across;
        }

        /// <summary>
        /// A teammate further towards the goal than us, with a clear line and no opponent sitting on them: pass to
        /// them. -1 if nobody's open.
        /// </summary>
        private static int PassTo(Simulation sim, GameState s, int i, FixVec2 attack, FixVec2 defend)
        {
            var me = s.Players[i];
            var myProg = FixVec2.Dot(me.Pos - defend, attack - defend);
            var best = -1;
            var bestProg = Fix.Zero;
            for (var j = 0; j < s.Players.Length; j++)
            {
                if (j == i || s.Players[j].Team != me.Team) continue;
                var them = s.Players[j];
                var prog = FixVec2.Dot(them.Pos - defend, attack - defend);
                if (prog <= myProg + Fix.FromInt(2)) continue; // not ahead of us
                if (sim.Map.Blocked(me.Pos, them.Pos)) continue; // a wall in the way
                if ((them.Pos - me.Pos).SqrLength < Fix.FromInt(4)) continue; // too close
                if ((them.Pos - me.Pos).SqrLength > Fix.FromInt(225)) continue; // too far for a pass
                // an opponent nearer them than we are: not open
                if (OpponentNearerThan(s, them.Pos, me.Team, me.Pos)) continue;
                if (best < 0 || prog > bestProg) { best = j; bestProg = prog; }
            }
            return best;
        }

        /// <summary>Is any opponent of <paramref name="i"/> within <paramref name="reach"/> of them?</summary>
        private static bool OpponentNear(GameState s, int i, Fix reach)
        {
            var me = s.Players[i];
            var r2 = reach * reach;
            for (var j = 0; j < s.Players.Length; j++)
            {
                if (s.Players[j].Team == me.Team) continue;
                if ((s.Players[j].Pos - me.Pos).SqrLength < r2) return true;
            }
            return false;
        }

        /// <summary>Is an opponent of <paramref name="team"/> closer to <paramref name="at"/> than <paramref name="from"/> is?</summary>
        private static bool OpponentNearerThan(GameState s, FixVec2 at, int team, FixVec2 from)
        {
            var mine = (from - at).SqrLength;
            for (var j = 0; j < s.Players.Length; j++)
            {
                if (s.Players[j].Team == team) continue;
                if ((s.Players[j].Pos - at).SqrLength < mine) return true;
            }
            return false;
        }

        /// <summary>The next stop on the route from <paramref name="pos"/> towards <paramref name="target"/>: a few points along.</summary>
        private static FixVec2 AlongRoute(MapDef map, FixVec2 pos, FixVec2 target)
        {
            var from = map.NearestOnRoute(pos);
            var to = map.NearestOnRoute(target);
            if (from == to) return target;
            var step = to > from ? 1 : -1;
            var next = from;
            for (var k = 0; k < 3 && next != to; k++) next += step;
            // off the route (in a corner): get back onto it first
            return map.Blocked(pos, map.Route[next]) ? map.Route[from] : map.Route[next];
        }

        /// <summary>The route point <paramref name="share"/> of the way from the one nearest <paramref name="from"/> to route index <paramref name="end"/>.</summary>
        private static FixVec2 RoutePart(MapDef map, FixVec2 from, int end, Fix share)
        {
            var a = map.NearestOnRoute(from);
            var k = a + (share * (end - a)).FloorToInt();
            if (k < 0) k = 0;
            if (k >= map.Route.Count) k = map.Route.Count - 1;
            return map.Route[k];
        }

        /// <summary>The middle of the goal that <paramref name="team"/> defends.</summary>
        public static FixVec2 GoalCentre(MapDef map, int team)
        {
            foreach (var g in map.Goals)
                if (g.Team == team) return new FixVec2((g.Min.X + g.Max.X) / 2, (g.Min.Y + g.Max.Y) / 2);
            return map.PuckSpot;
        }

        /// <summary>Is <paramref name="i"/> the closest of their team to the puck? (Ties go to the lower slot.)</summary>
        private static bool NearestToPuck(GameState s, int i)
        {
            var mine = (s.Players[i].Pos - s.PuckPos).SqrLength;
            for (var j = 0; j < s.Players.Length; j++)
            {
                if (j == i || s.Players[j].Team != s.Players[i].Team) continue;
                var d = (s.Players[j].Pos - s.PuckPos).SqrLength;
                if (d < mine || (d == mine && j < i)) return false;
            }
            return true;
        }

        /// <summary>0 for the first player on their team, 1 for the second, and so on.</summary>
        private static int TeamRank(GameState s, int i)
        {
            var r = 0;
            for (var j = 0; j < i; j++) if (s.Players[j].Team == s.Players[i].Team) r++;
            return r;
        }

        /// <summary>A stick value as a byte, rounded the same way either side of zero.</summary>
        private static sbyte Axis(Fix v)
        {
            var neg = v.Raw < 0;
            var a = neg ? -v.Raw : v.Raw;
            var r = (a * 127 + Fix.OneRaw / 2) >> Fix.Shift;
            if (r > 127) r = 127;
            return (sbyte)(neg ? -r : r);
        }

        private static uint Hash(uint x)
        {
            x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16;
            return x;
        }
    }
}
