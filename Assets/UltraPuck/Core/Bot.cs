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
                byte hover, pick;
                PickCard(sim, s, me, out hover, out pick);
                // New Hand: a poor hand (nothing better than C) gets re-rolled first, when it's theirs alone to re-roll
                if (pick > 0 && me.PickKind == 0 && !sim.T.TeamVote && !sim.T.SameDraw && sim.Cards != null
                    && me.Rerolls < sim.Cards.NewHands(me) && sim.Cards.Cards[me.Offer[pick - 1]].Tier > CardTier.B)
                    return new PlayerInput { Buttons = PlayerInput.Utility, Hover = hover };
                return new PlayerInput { Pick = pick, Hover = hover };
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
            var mateHeld = -1;
            for (var j = 0; j < s.Players.Length; j++) if (s.Players[j].HeldBy == i) { mateHeld = j; break; }
            if (mateHeld >= 0)
            {
                // Friendly Fire: carrying a teammate: fire them straight at the puck (or whoever has it); if the one
                // being carried has the puck, at the net
                target = pos;
                aim = ((s.Carrier == mateHeld ? attack : s.PuckPos) - pos).Normalized;
                if (aim.SqrLength.Raw <= 0) aim = me.Facing;
                if (FixVec2.Dot(me.Facing, aim) > Fix.Ratio(9, 10) && (s.Tick & 1) == 0) b |= PlayerInput.ShootCheck;
            }
            else if (s.Carrier == i)
            {
                // aim somewhere in the goal mouth, not always dead centre (a net that says which way it opens: across its mouth)
                var net = AttackGoal(sim.Map, me.Team);
                var across0 = net != null && net.HasFacing ? Across(sim.Map, net, attack) : new FixVec2(Fix.Zero, Fix.One);
                var spot = attack + across0 * (wobble * Fix.Ratio(3, 2));
                // is the net covered? Try the spot, then either side of the mouth: the first with no opponent (or their
                // pylon) in the way is where we shoot. All blocked: the net is covered, look for a pass.
                var lane = sim.T.PlayerRadius + sim.T.PuckRadius;
                var covered = true;
                for (var k = 0; k < 3 && covered; k++)
                {
                    var tryAt = k == 0 ? spot : attack + across0 * (k == 1 ? Fix.Ratio(6, 5) : -Fix.Ratio(6, 5));
                    if (LaneClear(s, pos, tryAt, me.Team, lane)) { spot = tryAt; covered = false; }
                }
                target = spot;
                var toGoal = spot - pos;
                aim = toGoal.Normalized;
                var aimed = FixVec2.Dot(me.Facing, aim) > Fix.Ratio(97, 100);
                var range = Fix.FromInt(12) + wobble * Fix.Two;
                var shooting = true;
                // a wall between us and the goal, or a teammate who's open further ahead: pass instead of forcing a shot
                var pressured = OpponentNear(s, i, Fix.FromInt(4));
                var mate = PassTo(sim, s, i, attack, defend, covered, pressured);
                var blocked = sim.Map.Route.Count > 1 && sim.Map.Blocked(pos, spot);
                // a teammate with a much better look at the net than ours: give it to them
                var better = mate >= 0 && !covered && LaneClear(s, s.Players[mate].Pos, attack, me.Team, lane)
                             && (s.Players[mate].Pos - attack).SqrLength + Fix.FromInt(30) < toGoal.SqrLength;
                // pass when the net is covered, a wall's in the way, we're under pressure, a teammate's better placed,
                // or just to move it about (about half the time someone's open): team play, not everyone going alone
                if (mate >= 0 && (blocked || covered || pressured || better || (roll % 3u) != 0u))
                {
                    var to = s.Players[mate].Pos - pos;
                    aim = to.Normalized;
                    aimed = FixVec2.Dot(me.Facing, aim) > Fix.Ratio(92, 100);
                    // a quick pass (don't charge): lined up and not right on top of them
                    if (aimed && to.SqrLength > Fix.FromInt(4) && to.SqrLength < Fix.FromInt(484) && (s.Tick & 1) == 0)
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
                // a quick shot instead of charging: close in on the net, an opponent closing on us, or a card that makes
                // quick shots the better ones (Power Shot, Invisible Shot) - only with a clear lane and lined up
                var dist = toGoal.Length;
                var quickCard = me.Stats.QuickShotFull > 0 || me.Stats.GhostShot > 0;
                var quick = shooting && !covered && !blocked && aimed
                            && (dist < Fix.FromInt(10) || (OpponentNear(s, i, Fix.FromInt(4)) && dist < range + Fix.FromInt(4)) || (quickCard && dist < range + Fix.FromInt(6)));
                if (quick && (s.Tick & 1) == 0) { b |= PlayerInput.ShootCheck; shooting = false; }
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
                // a teammate has it: get open for a pass, up by the net they're attacking (one each side of it), on
                // whichever side has fewer opponents near and a clear lane from the carrier (on a bent rink: further along the ice)
                target = OpenSpot(sim, s, i, attack, dir, wobble);
                if (sim.Map.Route.Count > 1) target = RoutePart(sim.Map, s.Players[s.Carrier].Pos, me.Team == 0 ? sim.Map.Route.Count - 1 : 0, Fix.Ratio(3, 5) + wobble * Fix.Ratio(1, 10));
            }
            else if (NearestToPuck(s, i))
            {
                target = s.PuckPos;
                var gap = (s.PuckPos - pos).SqrLength;
                var close = gap < Fix.FromInt(9);
                if (s.Carrier >= 0 && close && me.DashCharge >= Fix.One) b |= PlayerInput.ShootCheck;
                // a long way to go: hold dash to chain dashes there, along the way we're skating. Going after a carrier,
                // only from further out, so there's a check ready (or nearly) when we arrive
                var far = s.Carrier >= 0 ? Fix.FromInt(64) : Fix.FromInt(25);
                if (gap > far && me.DashCharge >= Fix.One) b |= (byte)(PlayerInput.ShootCheck | PlayerInput.DashMove);
            }
            else if (s.Carrier < 0 && SupportRank(s, i) == 0 && sim.Map.Route.Count <= 1
                     && FixVec2.Dot(s.PuckPos - (attack + defend) * Fix.Half, attack - defend) > Fix.Zero)
            {
                // a loose puck up in their half and a teammate going for it: push up ready for the pass, not back
                target = OpenSpot(sim, s, i, attack, dir, wobble);
            }
            else
            {
                // hang back between the puck and our goal (on a bent rink: along the ice, not through the walls)
                target = defend + (s.PuckPos - defend) * (Fix.Ratio(1, 2) + wobble * Fix.Ratio(1, 10));
                if (sim.Map.Route.Count > 1) target = RoutePart(sim.Map, s.PuckPos, me.Team == 0 ? 0 : sim.Map.Route.Count - 1, Fix.Ratio(3, 5) + wobble * Fix.Ratio(1, 10));
                if (s.Carrier >= 0 && s.Players[s.Carrier].Team != me.Team
                    && (s.Players[s.Carrier].Pos - pos).SqrLength < Fix.FromInt(4) && me.DashCharge >= Fix.One)
                    b |= PlayerInput.ShootCheck;
            }

            // utility cards: shockwave when foes are close; go invisible with the puck; Puck Gun aims at a carrier and fires
            if (me.Stats.Shockwave > 0)
            {
                // hold it until it's just big enough to reach the nearest foe (one that's close), then let go
                var nearest = Fix.FromInt(1000);
                foreach (var q in s.Players)
                    if (q.Team != me.Team) nearest = Fix.Min(nearest, (q.Pos - pos).Length);
                var want = nearest + sim.T.PlayerRadius;
                if (me.ShockHold > Fix.Zero)
                {
                    if (want <= sim.T.ShockMax && sim.ShockRadius(me.ShockHold) < want) b |= PlayerInput.Utility;
                }
                else if (nearest < Fix.FromInt(4) && (s.Tick & 1) == 0) b |= PlayerInput.Utility;
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
            // Slot Machine: give it a spin now and then (about every 2 s) when it's free
            if (me.Stats.SlotMachine > 0 && me.SlotSpin <= Fix.Zero && me.SlotPrizeTime <= Fix.Zero && me.SlotWait <= Fix.Zero
                && (s.Tick + i * 29) % 240 == 0)
                b |= PlayerInput.Utility;
            // Portal Gun: now and then (about every 3 s), a portal wherever we're facing, until both are up
            if (me.Stats.PortalGun > 0 && me.PortalCooldown <= Fix.Zero && s.Carrier != i && (s.Tick + i * 37) % 360 == 0)
            {
                var mine = 0;
                foreach (var po in s.Portals) if (po.Owner == i) mine++;
                if (mine < 2) b |= PlayerInput.Utility;
            }
            if (me.Stats.PuckGun > 0 && me.GunCooldown <= Fix.Zero && s.Carrier >= 0 && s.Players[s.Carrier].Team != me.Team)
            {
                var to = s.Players[s.Carrier].Pos - pos;
                if (to.SqrLength < Fix.FromInt(144))
                {
                    aim = to.Normalized;
                    if (FixVec2.Dot(me.Facing, aim) > Fix.Ratio(95, 100) && (s.Tick & 1) == 0) b |= PlayerInput.Utility; // the utility button fires the Puck Gun
                }
            }

            // never aim for a spot inside a net (a defender hanging back with the puck behind their net): in front of it
            target = OutOfNets(sim, target);
            // on a bent rink, anywhere we can't see straight to: go by the ice
            if (s.Carrier != i && sim.Map.Route.Count > 1 && sim.Map.Blocked(pos, target)) target = AlongRoute(sim.Map, pos, target);
            // a net in the way (out on the ice): round its nearest corner, not into its back
            if (sim.Map.Route.Count <= 1 && sim.Map.Blocked(pos, target)) target = RoundNets(sim, pos, target); // (bent rinks: the route)

            var m = target - pos;
            if (m.SqrLength > Fix.One) m = m.Normalized;
            return new PlayerInput { MoveX = Axis(m.X), MoveY = Axis(m.Y), AimX = Axis(aim.X), AimY = Axis(aim.Y), Buttons = b };
        }

        /// <summary>
        /// Team Vote: hover follows whatever most humans on the team are on (a locked pick, else their hover). Pick
        /// only locks once a human has locked — otherwise the bot's cursor would freeze on the first card looked at
        /// (online spectators watch those cursors). If no human is on this team, or Team Vote is off: take the
        /// strongest card by tier (first on a tie).
        /// </summary>
        private static void PickCard(Simulation sim, GameState s, PlayerState me, out byte hover, out byte pick)
        {
            hover = 0;
            pick = 0;
            if (me.Offer.Length == 0 || me.Picked >= 0 || sim.Cards == null) return;
            if (sim.T.TeamVote && me.PickKind == 0)
            {
                var humans = false;
                var anyLocked = false;
                var votes = new int[me.Offer.Length];
                var anyLook = false;
                foreach (var p in s.Players)
                {
                    if (p.Team != me.Team || p.Offer.Length == 0 || p.Bot) continue;
                    // someone who took a card that was only in their hand (one a team) isn't in the vote
                    if (p.Picked >= 0 && System.Array.IndexOf(me.Offer, p.Picked) < 0) continue;
                    humans = true;
                    var idx = -1;
                    if (p.Picked >= 0) { idx = System.Array.IndexOf(me.Offer, p.Picked); anyLocked = true; }
                    else if (p.Hover >= 1 && p.Hover <= me.Offer.Length) idx = p.Hover - 1;
                    if (idx < 0) continue;
                    votes[idx]++;
                    anyLook = true;
                }
                if (humans)
                {
                    if (!anyLook) return; // wait for a human teammate to look / lock
                    var best = 0;
                    for (var k = 1; k < votes.Length; k++) if (votes[k] > votes[best]) best = k;
                    hover = (byte)(best + 1);
                    if (anyLocked) pick = hover; // lock with them; stay on hover-only until then
                    return;
                }
                // no human on this team: pick by tier like a normal bot pick
            }
            var tier = 0;
            for (var k = 1; k < me.Offer.Length; k++)
                if (sim.Cards.Cards[me.Offer[k]].Tier < sim.Cards.Cards[me.Offer[tier]].Tier) tier = k;
            hover = pick = (byte)(tier + 1);
        }

        /// <summary>How far out from a net's goal box its walls (and a player against them) reach.</summary>
        private static Fix NetClear(Simulation sim) { return sim.T.WallThickness + sim.T.PlayerRadius + Fix.Ratio(7, 10); }

        /// <summary>A spot inside (or right up against) a net that opens one way: moved out in front of its mouth instead.</summary>
        private static FixVec2 OutOfNets(Simulation sim, FixVec2 at)
        {
            foreach (var g in sim.Map.Goals)
            {
                if (!g.HasFacing || !g.Touches(at, sim.T.WallThickness + sim.T.PlayerRadius)) continue;
                var centre = new FixVec2((g.Min.X + g.Max.X) / 2, (g.Min.Y + g.Max.Y) / 2);
                var half = FixVec2.Dot(g.Max - centre, new FixVec2(Fix.Abs(g.Facing.X), Fix.Abs(g.Facing.Y)));
                return centre + g.Facing * (half + NetClear(sim));
            }
            return at;
        }

        /// <summary>
        /// A net (out on the ice) in the way to <paramref name="target"/>: head for whichever corner round it gets there
        /// soonest, that we can skate to in a straight line. Only nets count (a bend in the boards is the route's job), and
        /// a tie goes to the corner on our side of the net, so both teams choose alike on a mirrored rink.
        /// </summary>
        private static FixVec2 RoundNets(Simulation sim, FixVec2 pos, FixVec2 target)
        {
            var best = target;
            var bestCost = Fix.FromInt(100000);
            var clear = NetClear(sim);
            foreach (var g in sim.Map.Goals)
            {
                if (!g.HasFacing || !CrossesBox(pos, target, g.Min - new FixVec2(sim.T.WallThickness, sim.T.WallThickness), g.Max + new FixVec2(sim.T.WallThickness, sim.T.WallThickness))) continue;
                var centre = (g.Min + g.Max) * Fix.Half;
                for (var k = 0; k < 4; k++)
                {
                    var c = new FixVec2((k & 1) == 0 ? g.Min.X - clear : g.Max.X + clear, (k & 2) == 0 ? g.Min.Y - clear : g.Max.Y + clear);
                    if (!sim.Map.Inside(c) || sim.Map.Blocked(pos, c)) continue;
                    var cost = (c - pos).Length + (target - c).Length + (sim.Map.Blocked(c, target) ? Fix.FromInt(20) : Fix.Zero)
                               - FixVec2.Dot(pos - centre, c - centre) / Fix.FromInt(1000); // (the tie-break)
                    if (cost < bestCost) { bestCost = cost; best = c; }
                }
            }
            // already at that corner: carry on to the target (the next corner takes over once it's blocked again)
            return (best - pos).SqrLength < Fix.Ratio(1, 4) ? target : best;
        }

        /// <summary>Does the straight line from <paramref name="a"/> to <paramref name="b"/> pass through the box?</summary>
        private static bool CrossesBox(FixVec2 a, FixVec2 b, FixVec2 lo, FixVec2 hi)
        {
            Fix t0 = Fix.Zero, t1 = Fix.One;
            var d = b - a;
            for (var axis = 0; axis < 2; axis++)
            {
                var p = axis == 0 ? a.X : a.Y;
                var v = axis == 0 ? d.X : d.Y;
                var mn = axis == 0 ? lo.X : lo.Y;
                var mx = axis == 0 ? hi.X : hi.Y;
                if (v.Raw == 0) { if (p < mn || p > mx) return false; continue; }
                var ta = (mn - p) / v;
                var tb = (mx - p) / v;
                if (ta > tb) { var x = ta; ta = tb; tb = x; }
                t0 = Fix.Max(t0, ta);
                t1 = Fix.Min(t1, tb);
                if (t0 > t1) return false;
            }
            return true;
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
        /// <summary>
        /// An open teammate to pass to, or -1: a clear lane to them, nobody marking them, within a pass's reach.
        /// Normally only someone further up the ice; with the net <paramref name="covered"/>, anyone who has a clear
        /// shot of their own (that's the point of the pass). A teammate with a clear shot is preferred.
        /// </summary>
        private static int PassTo(Simulation sim, GameState s, int i, FixVec2 attack, FixVec2 defend, bool covered = false, bool pressured = false)
        {
            var me = s.Players[i];
            var myProg = FixVec2.Dot(me.Pos - defend, attack - defend);
            var lane = sim.T.PlayerRadius + sim.T.PuckRadius - Fix.Ratio(15, 100); // a pass can squeeze past a little closer than a shot
            var best = -1;
            var bestScore = Fix.Zero;
            for (var j = 0; j < s.Players.Length; j++)
            {
                if (j == i || s.Players[j].Team != me.Team) continue;
                var them = s.Players[j];
                if (them.HeldBy >= 0 || them.TossTime > Fix.Zero) continue;
                var prog = FixVec2.Dot(them.Pos - defend, attack - defend);
                var shot = LaneClear(s, them.Pos, attack, me.Team, lane);
                // not ahead of us (level, or a little back, is fine to keep it moving) / with the net covered, no shot either
                // (under pressure, any open teammate will do: a pass back to get out of trouble)
                if (!pressured && (covered ? !shot || prog <= myProg - Fix.FromInt(4) : prog <= myProg - Fix.FromInt(3))) continue;
                // not straight back to whoever just passed it to us, unless we're under pressure (no ping-pong)
                if (j == s.Touch2 && !pressured && !covered) continue;
                if (sim.Map.Blocked(me.Pos, them.Pos)) continue; // a wall in the way
                if ((them.Pos - me.Pos).SqrLength < Fix.FromInt(4)) continue; // too close
                if ((them.Pos - me.Pos).SqrLength > Fix.FromInt(484)) continue; // too far for a pass (22)
                if (!LaneClear(s, me.Pos, them.Pos, me.Team, lane)) continue; // it would be picked off
                // an opponent right on them: not open
                if (OpponentWithin(s, them.Pos, me.Team, Fix.Ratio(8, 5))) continue;
                var score = prog + (shot ? Fix.FromInt(20) : Fix.Zero) - (them.Pos - attack).Length / 2;
                if (best < 0 || score > bestScore) { best = j; bestScore = score; }
            }
            return best;
        }

        /// <summary>
        /// No opponent (or opponent's pylon) within <paramref name="width"/> of the straight line from
        /// <paramref name="from"/> to <paramref name="to"/> — a shot or pass along it would get through.
        /// </summary>
        private static bool LaneClear(GameState s, FixVec2 from, FixVec2 to, int team, Fix width)
        {
            var d = to - from;
            var len2 = d.SqrLength;
            if (len2.Raw <= 0) return true;
            for (var j = 0; j < s.Players.Length; j++)
            {
                var q = s.Players[j];
                if (q.Team == team) continue;
                if (Near(from, d, len2, q.Pos, width)) return false;
            }
            foreach (var py in s.Pylons)
                if (py.Team != team && Near(from, d, len2, py.Pos, width + py.Radius)) return false;
            return true;
        }

        private static bool Near(FixVec2 from, FixVec2 d, Fix len2, FixVec2 at, Fix width)
        {
            var t = FixVec2.Dot(at - from, d) / len2;
            if (t <= Fix.Zero || t >= Fix.One) return false;
            return (at - (from + d * t)).SqrLength < width * width;
        }

        /// <summary>
        /// Where to get open for a pass: beside the net we're attacking (high or low), on the side with the most room
        /// and a clear lane from the puck. A second teammate takes the other side, so they spread out.
        /// </summary>
        private static FixVec2 OpenSpot(Simulation sim, GameState s, int i, FixVec2 attack, Fix dir, Fix wobble)
        {
            var me = s.Players[i];
            var from = s.Carrier >= 0 ? s.Players[s.Carrier].Pos : s.PuckPos;
            var lane = sim.T.PlayerRadius + sim.T.PuckRadius;
            var spots = new FixVec2[2];
            var room = new Fix[2];
            for (var k = 0; k < 2; k++)
            {
                var side = k == 0 ? Fix.One : -Fix.One;
                spots[k] = new FixVec2(attack.X - dir * (Fix.FromInt(5) + wobble), attack.Y + side * Fix.FromInt(4));
                if (!sim.Map.Inside(spots[k])) spots[k] = new FixVec2(attack.X - dir * Fix.FromInt(6), attack.Y + side * Fix.FromInt(2));
                // room: distance to the nearest opponent (squared, capped), less if the lane from the puck is blocked
                var near = Fix.FromInt(100);
                foreach (var q in s.Players) if (q.Team != me.Team) near = Fix.Min(near, (q.Pos - spots[k]).SqrLength);
                room[k] = near - (LaneClear(s, from, spots[k], me.Team, lane) ? Fix.Zero : Fix.FromInt(50));
            }
            var pick = room[0] >= room[1] ? 0 : 1;
            // the other supporting teammate takes the other side
            if (OffPuckRank(s, i) % 2 == 1) pick = 1 - pick;
            return spots[pick];
        }

        /// <summary>0 for the first teammate of <paramref name="i"/> who isn't the one going for the puck, 1 for the next, …</summary>
        private static int SupportRank(GameState s, int i)
        {
            var r = 0;
            for (var j = 0; j < i; j++) if (s.Players[j].Team == s.Players[i].Team && !NearestToPuck(s, j)) r++;
            return r;
        }

        /// <summary>0 for the first teammate of <paramref name="i"/> who doesn't have the puck, 1 for the next, …</summary>
        private static int OffPuckRank(GameState s, int i)
        {
            var r = 0;
            for (var j = 0; j < i; j++) if (s.Players[j].Team == s.Players[i].Team && j != s.Carrier) r++;
            return r;
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

        /// <summary>Is an opponent of <paramref name="team"/> within <paramref name="reach"/> of <paramref name="at"/>?</summary>
        private static bool OpponentWithin(GameState s, FixVec2 at, int team, Fix reach)
        {
            foreach (var q in s.Players) if (q.Team != team && (q.Pos - at).SqrLength < reach * reach) return true;
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
