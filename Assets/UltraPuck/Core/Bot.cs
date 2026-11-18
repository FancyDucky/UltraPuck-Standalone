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
            if (s.Phase == Phase.Picking) return new PlayerInput { Pick = PickCard(sim, me) };
            var pos = me.Pos;
            var attack = GoalCentre(sim.Map, 1 - me.Team);
            var defend = GoalCentre(sim.Map, me.Team);
            var dir = attack.X > defend.X ? Fix.One : -Fix.One;

            // variety: a number that changes every quarter of a second, different for every player and match
            var roll = Hash((uint)s.Rng * 2654435761u ^ (uint)(i * 40503) ^ (uint)(s.Tick / 30) * 97u);
            var wobble = Fix.Ratio((int)(roll % 2001) - 1000, 1000); // -1..1

            FixVec2 target, aim = FixVec2.Zero;
            byte b = 0;
            if (s.Carrier == i)
            {
                // aim somewhere in the goal mouth, not always dead centre
                var spot = attack + new FixVec2(Fix.Zero, wobble * Fix.Ratio(3, 2));
                target = spot;
                var toGoal = spot - pos;
                aim = toGoal.Normalized;
                var aimed = FixVec2.Dot(me.Facing, aim) > Fix.Ratio(97, 100);
                var range = Fix.FromInt(12) + wobble * Fix.Two;
                // a net out on the ice: from beside or behind it, skate round to the front first (and don't shoot into its side)
                var net = AttackGoal(sim.Map, me.Team);
                if (net != null && net.HasFacing)
                {
                    // the map says which way the net opens: go round to that side
                    var f = net.Facing;
                    var off = pos - attack;
                    if (FixVec2.Dot(off, f) < Fix.Two)
                    {
                        var across = new FixVec2(-f.Y, f.X);
                        var side = FixVec2.Dot(off, across) >= Fix.Zero ? Fix.One : -Fix.One;
                        target = attack + f * Fix.FromInt(5) + across * (side * Fix.FromInt(4));
                        aim = FixVec2.Zero;
                        aimed = false;
                    }
                }
                else if (dir > Fix.Zero ? pos.X > attack.X - Fix.FromInt(2) : pos.X < attack.X + Fix.FromInt(2))
                {
                    target = new FixVec2(attack.X - dir * Fix.FromInt(5), pos.Y >= Fix.Zero ? Fix.FromInt(5) : Fix.FromInt(-5));
                    aim = FixVec2.Zero;
                    aimed = false;
                }
                // a wall between us and the goal (round a bend): follow the ice, don't shoot yet
                if (sim.Map.Route.Count > 1 && sim.Map.Blocked(pos, target))
                {
                    target = AlongRoute(sim.Map, pos, target);
                    aim = FixVec2.Zero;
                    aimed = false;
                }
                var ready = aimed && me.Charge > Fix.Ratio(3, 10) && (toGoal.Length < range || me.Charge >= Fix.One);
                // charging starts on a press, so if it hasn't started, let go for a tick and press again
                if (!ready && (me.Charge > Fix.Zero || (s.Tick & 1) == 0)) b |= PlayerInput.ChargeBrake;
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

            // utility cards: go invisible with the puck; with the Puck Gun ready, aim at an opponent carrying it and fire once lined up
            if (s.Carrier == i && me.Stats.Invisibility > 0 && me.InvisCooldown <= Fix.Zero && (s.Tick & 1) == 0) b |= PlayerInput.Utility;
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

        /// <summary>A bot takes the rarest card it's offered (the first of them on a tie). Returns the 1-based pick, or 0.</summary>
        private static byte PickCard(Simulation sim, PlayerState me)
        {
            if (me.Offer.Length == 0 || me.Picked >= 0 || sim.Cards == null) return 0;
            var best = 0;
            for (var k = 1; k < me.Offer.Length; k++)
                if (sim.Cards.Cards[me.Offer[k]].Tier < sim.Cards.Cards[me.Offer[best]].Tier) best = k;
            return (byte)(best + 1);
        }

        /// <summary>The goal a player on <paramref name="team"/> shoots at.</summary>
        private static GoalDef AttackGoal(MapDef map, int team)
        {
            foreach (var g in map.Goals) if (g.Team == 1 - team) return g;
            return null;
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
