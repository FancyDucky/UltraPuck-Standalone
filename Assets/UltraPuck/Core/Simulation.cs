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
            // people draw first, so a Team Vote offer can hold cards bots aren't offered
            var order = new System.Collections.Generic.List<int>();
            foreach (var i in pickers) if (i >= 0 && i < s.Players.Length && !s.Players[i].Bot) order.Add(i);
            foreach (var i in pickers) if (i >= 0 && i < s.Players.Length && s.Players[i].Bot) order.Add(i);
            foreach (var i in order)
            {
                if (i < 0 || i >= s.Players.Length) continue;
                var p = s.Players[i];
                // Team Vote and Same Draw: the first picker on a team draws the offer, and the rest of the team sees the same cards
                var shared = T.TeamVote || T.SameDraw;
                if (shared && teamOffer[p.Team & 1] != null) p.Offer = (int[])teamOffer[p.Team & 1].Clone();
                else p.Offer = Cards.DrawOffer(s, i, Math.Max(1, Math.Min(5, T.CardsOffered)), T.CardsOff);
                // every switched-on card is one they can't have (only the Portal Gun on, and a teammate has it): cards
                // from the switched-off ones instead, so nobody's left without a pick
                if (p.Offer.Length == 0 && T.CardsOff != null && T.CardsOff.Length > 0)
                {
                    // (a shared hand: no utility if anyone on the team has one, since a Team Vote hands everyone the winner)
                    var not = StandInOff(p);
                    if (shared) foreach (var q in s.Players) if (not == null && q.Team == p.Team) not = StandInOff(q);
                    p.Offer = Cards.DrawOffer(s, i, Math.Max(1, Math.Min(5, T.CardsOffered)), not);
                }
                if (shared && teamOffer[p.Team & 1] == null) teamOffer[p.Team & 1] = p.Offer;
                p.Picked = -1;
                p.Hover = 0;
                p.PickKind = 0;
                p.Rerolls = 0;
                any |= p.Offer.Length > 0;
            }
            if (T.TeamVote || T.SameDraw) for (var team = 0; team < 2; team++) OnePickerGets(s, team, order);
            if (!any) return;
            s.Carrier = -1;
            s.Phase = Phase.Picking;
            s.PhaseTime = T.PickTime;
        }

        /// <summary>
        /// Team Vote / Same Draw: a card only one player a team may have (Portal Gun, Swap: "maxTeam") isn't in the team's
        /// shared hand. It goes into just one picker's hand (a person if there is one, at random), in place of the card in
        /// that spot; everyone else gets a fresh card there instead.
        /// </summary>
        private void OnePickerGets(GameState s, int team, System.Collections.Generic.List<int> order)
        {
            var pickers = new System.Collections.Generic.List<int>();
            foreach (var i in order) if ((s.Players[i].Team & 1) == team && s.Players[i].Offer.Length > 0) pickers.Add(i);
            if (pickers.Count == 0) return;
            // worked out from one fixed copy of the shared hand, one spot at a time (so it can never go round in circles)
            var shared = (int[])s.Players[pickers[0]].Offer.Clone();
            var lucky = new int[shared.Length];
            var fresh = new int[shared.Length];
            var used = new System.Collections.Generic.List<int>(shared);
            if (T.CardsOff != null) used.AddRange(T.CardsOff);
            var any = false;
            for (var k = 0; k < shared.Length; k++)
            {
                lucky[k] = -1; fresh[k] = -1;
                var card = shared[k];
                if (card < 0 || card >= Cards.Cards.Count || Cards.Cards[card].MaxPerTeam <= 0) continue;
                any = true;
                // a fresh card for that spot (not one already in the hand, nor another one-a-team card): may be none
                foreach (var c in Cards.DrawOffer(s, pickers[0], Cards.Cards.Count, used.ToArray()))
                    if (Cards.Cards[c].MaxPerTeam <= 0) { fresh[k] = c; used.Add(c); break; }
                if (fresh[k] < 0)
                {
                    // nothing switched on is left: a switched-off card instead, never a utility if anyone on the team has one
                    // (a Team Vote gives everyone the winner, so it would replace theirs)
                    var not = new System.Collections.Generic.List<int>(shared);
                    foreach (var i in pickers)
                    {
                        var u = StandInOff(s.Players[i]);
                        if (u != null) { not.AddRange(u); break; }
                    }
                    fresh[k] = AnyOtherCard(s, pickers[0], not.ToArray());
                    if (fresh[k] >= 0) used.Add(fresh[k]);
                }
                // the one who gets it: a person if any can, else a bot
                var people = new System.Collections.Generic.List<int>();
                var anyone = new System.Collections.Generic.List<int>();
                foreach (var i in pickers)
                {
                    if (Cards.TeamFull(s, s.Players[i], card) || Array.IndexOf(s.Players[i].Cards, card) >= 0) continue;
                    anyone.Add(i);
                    if (!s.Players[i].Bot) people.Add(i);
                }
                var from = people.Count > 0 ? people : anyone;
                if (from.Count > 0) lucky[k] = from[(int)(s.NextRandom() % (uint)from.Count)];
            }
            if (!any) return;
            // each picker's hand: the shared one, with the one-a-team card only for its one player, a fresh card (or
            // nothing) there for the rest
            foreach (var i in pickers)
            {
                var hand = new System.Collections.Generic.List<int>(shared.Length);
                for (var k = 0; k < shared.Length; k++)
                {
                    var card = shared[k];
                    var special = card >= 0 && card < Cards.Cards.Count && Cards.Cards[card].MaxPerTeam > 0;
                    if (!special) hand.Add(card);
                    else if (lucky[k] == i) hand.Add(card);
                    else if (fresh[k] >= 0) hand.Add(fresh[k]);
                }
                s.Players[i].Offer = hand.ToArray();
            }
        }

        /// <summary>
        /// A random card player <paramref name="i"/> may have that isn't one-a-team and isn't in <paramref name="not"/>,
        /// switched off or not (for when nothing switched on is left), or -1.
        /// </summary>
        private int AnyOtherCard(GameState s, int i, int[] not)
        {
            if (i < 0 || i >= s.Players.Length) return -1;
            var off = new System.Collections.Generic.List<int>();
            var utilities = StandInOff(s.Players[i]);
            if (utilities != null) off.AddRange(utilities);
            if (not != null) off.AddRange(not);
            foreach (var c in Cards.DrawOffer(s, i, Cards.Cards.Count, off.ToArray()))
                if (Cards.Cards[c].MaxPerTeam <= 0) return c;
            return -1;
        }

        /// <summary>
        /// Cards a stand-in card (handed out when nothing switched on is left) must not be: another utility for someone
        /// who has one (it would replace theirs without them choosing to).
        /// </summary>
        private int[] StandInOff(PlayerState p)
        {
            if (T.MultiUtility || Cards.UtilityHeld(p) == null) return null;
            var off = new System.Collections.Generic.List<int>();
            foreach (var c in Cards.Cards) if (Cards.IsUtilityIndex(c.Index)) off.Add(c.Index);
            return off.ToArray();
        }

        /// <summary>
        /// New Hand: a fresh offer for player <paramref name="i"/> (none of the cards just shown, unless too few are left).
        /// Team Vote / Same Draw: the whole team's shared hand is re-rolled, and everyone on it chooses again.
        /// </summary>
        private void Reroll(GameState s, int i)
        {
            var p = s.Players[i];
            p.Rerolls++;
            var count = Math.Max(1, Math.Min(5, T.CardsOffered));
            var off = new System.Collections.Generic.List<int>(p.Offer);
            if (T.CardsOff != null) off.AddRange(T.CardsOff);
            var hand = new System.Collections.Generic.List<int>(Cards.DrawOffer(s, i, count, off.ToArray()));
            if (hand.Count < count)
            {
                // not enough new cards left: top up with ones seen before
                var off2 = new System.Collections.Generic.List<int>(hand);
                if (T.CardsOff != null) off2.AddRange(T.CardsOff);
                hand.AddRange(Cards.DrawOffer(s, i, count - hand.Count, off2.ToArray()));
            }
            if (hand.Count == 0) return;
            if (!(T.TeamVote || T.SameDraw)) { p.Offer = hand.ToArray(); p.Hover = 0; return; }
            var order = new System.Collections.Generic.List<int>();
            for (var pass = 0; pass < 2; pass++)
                for (var j = 0; j < s.Players.Length; j++)
                {
                    var q = s.Players[j];
                    if (q.Team != p.Team || q.Offer.Length == 0 || q.PickKind != 0 || q.Bot != (pass == 1)) continue;
                    q.Offer = hand.ToArray(); q.Picked = -1; q.Hover = 0;
                    order.Add(j);
                }
            OnePickerGets(s, p.Team & 1, order);
        }

        /// <summary>Double Card: one random card for player <paramref name="i"/>, with the usual limits (never a card giver, never another utility for someone who has one).</summary>
        private void GiveRandom(GameState s, int i)
        {
            var p = s.Players[i];
            var off = new System.Collections.Generic.List<int>(Cards.CardGivers());
            if (T.CardsOff != null) off.AddRange(T.CardsOff);
            var utilities = StandInOff(p);
            if (utilities != null) off.AddRange(utilities);
            var got = Cards.DrawOffer(s, i, 1, off.ToArray());
            if (got.Length > 0) GiveCard(s, p, got[0]);
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
            var redrawn = new bool[s.Players.Length];
            // New Hand: a press of the utility button while still choosing re-rolls the offer (once a pick per copy held)
            for (var i = 0; i < s.Players.Length; i++)
            {
                var p = s.Players[i];
                var input = InputOf(inputs, i);
                var press = (input.Buttons & PlayerInput.Utility) != 0 && (p.PrevButtons & PlayerInput.Utility) == 0;
                if (!press || p.Offer.Length == 0 || p.Picked >= 0 || p.PickKind != 0 || p.Rerolls >= Cards.NewHands(p)) continue;
                Reroll(s, i);
                for (var j = 0; j < s.Players.Length; j++)
                    if (j == i || ((T.TeamVote || T.SameDraw) && s.Players[j].Team == p.Team)) redrawn[j] = true;
            }
            for (var i = 0; i < s.Players.Length; i++)
            {
                var p = s.Players[i];
                var input = InputOf(inputs, i);
                p.PrevButtons = input.Buttons;
                if (p.Offer.Length == 0 || p.Picked >= 0) continue;
                // the tick a hand was re-rolled: a pick this tick was for the old hand, so it waits for the new one
                if (redrawn[i]) { waiting = true; continue; }
                p.Hover = input.Hover <= p.Offer.Length ? input.Hover : (byte)0;
                if (input.Pick >= 1 && input.Pick <= p.Offer.Length) p.Picked = p.Offer[input.Pick - 1];
                else waiting = true;
            }
            s.PhaseTime -= dt;
            if (waiting && (T.PickTime <= Fix.Zero || s.PhaseTime > Fix.Zero)) return; // a pick time of 0: no limit

            // Team Vote: each team's most-voted card (a tie, or nobody voting, is settled at random) goes to everyone on it
            // (not when choosing from the deck after a Choose: everyone picks their own there)
            var deckRound = false;
            foreach (var p in s.Players) if (p.Offer.Length > 0 && p.PickKind == 1) deckRound = true;
            if (T.TeamVote && !deckRound) for (var team = 0; team < 2; team++) TeamVoteResult(s, team);

            // everyone chose (or time's up: a random card for anyone who didn't)
            var again = false;
            foreach (var p in s.Players)
            {
                if (p.Offer.Length == 0) continue;
                if (p.Picked < 0) p.Picked = p.Offer[(int)(s.NextRandom() % (uint)p.Offer.Length)];
                // one a teammate has just taken (only so many a team, like the Portal Gun): the next card offered instead
                if (Cards.TeamFull(s, p, p.Picked))
                {
                    var other = -1;
                    var at = Array.IndexOf(p.Offer, p.Picked);
                    for (var k = 1; k < p.Offer.Length && other < 0; k++)
                    {
                        var c = p.Offer[(at + k) % p.Offer.Length];
                        if (!Cards.TeamFull(s, p, c)) other = c;
                    }
                    if (other < 0) other = AnyOtherCard(s, Array.IndexOf(s.Players, p), p.Offer);
                    p.Picked = other;
                }
                var took = p.Picked;
                var wasDeck = p.PickKind == 1;
                if (took >= 0) GiveCard(s, p, took);
                p.Offer = new int[0];
                p.Picked = -1;
                p.Hover = 0;
                p.PickKind = 0;
                if (took < 0 || wasDeck) continue;
                var me = Array.IndexOf(s.Players, p);
                // Choose: straight on to picking any card from the deck (everyone else waits)
                if (Cards.IsChoose(took))
                {
                    var deck = Cards.DeckOffer(s, me, T.CardsOff);
                    if (deck.Length > 0) { p.Offer = deck; p.PickKind = 1; again = true; }
                }
                // Double Card: two random cards, by the same rules as Choose's deck
                else if (Cards.IsDouble(took)) for (var n = 0; n < 2; n++) GiveRandom(s, me);
            }
            Cards.Recompute(s);
            if (again)
            {
                s.PhaseTime = T.PickTime; // a fresh clock for the deck pick
                return;
            }
            PlacePylons(s);
            PruneRocks(s);
            if (T.FreePlay) s.Phase = Phase.Play; // free play: carry on where everyone stands
            else
            {
                FaceOff(s);
                s.PhaseTime += T.PickOutroTime; // the countdown waits for the cards to leave the screen
            }
        }

        private void TeamVoteResult(GameState s, int team)
        {
            // the team's shared hand (one player may have a one-a-team card in place of one of these: see OnePickerGets)
            int[] offer = null;
            foreach (var p in s.Players)
            {
                if (p.Team != team || p.Offer.Length == 0) continue;
                var special = false;
                foreach (var c in p.Offer) if (c >= 0 && c < Cards.Cards.Count && Cards.Cards[c].MaxPerTeam > 0) special = true;
                if (offer == null || !special) offer = p.Offer;
                if (!special) break;
            }
            if (offer == null) return;
            var votes = new int[offer.Length];
            var humans = false;
            // humans decide the vote (locked picks); bots only fill in when the team has no one human
            foreach (var p in s.Players)
                if (p.Team == team && !p.Bot && p.Offer.Length > 0 && p.Picked >= 0)
                { var k = Array.IndexOf(offer, p.Picked); if (k >= 0) { votes[k]++; humans = true; } }
            if (!humans)
            {
                // time ran out with nobody locked: use where humans were looking, else bot picks
                foreach (var p in s.Players)
                    if (p.Team == team && !p.Bot && p.Offer.Length > 0 && p.Hover >= 1 && p.Hover <= p.Offer.Length)
                    { var k = Array.IndexOf(offer, p.Offer[p.Hover - 1]); if (k >= 0) { votes[k]++; humans = true; } }
                if (!humans)
                    foreach (var p in s.Players)
                        if (p.Team == team && p.Bot && p.Offer.Length > 0 && p.Picked >= 0)
                        { var k = Array.IndexOf(offer, p.Picked); if (k >= 0) votes[k]++; }
            }
            var most = 0;
            foreach (var v in votes) most = Math.Max(most, v);
            var tied = new System.Collections.Generic.List<int>();
            for (var k = 0; k < offer.Length; k++) if (votes[k] == most) tied.Add(k);
            var won = offer[tied[(int)(s.NextRandom() % (uint)tied.Count)]];
            // everyone gets the winner, except whoever took their own one-a-team card (it's theirs alone)
            foreach (var p in s.Players)
                if (p.Team == team && p.Offer.Length > 0 && !(p.Picked >= 0 && Array.IndexOf(offer, p.Picked) < 0)) p.Picked = won;
        }

        /// <summary>
        /// A new match with one player per entry of <paramref name="teams"/> (0 or 1), waiting for the first face-off.
        /// <paramref name="seed"/> drives everything random in the match (bots' variety for now); online, every PC
        /// must use the same one. 0 keeps the default.
        /// </summary>
        public GameState NewGame(int[] teams, uint seed = 0, bool[] bots = null)
        {
            var s = new GameState { Players = new PlayerState[teams.Length] };
            if (seed != 0) s.Rng = seed;
            for (var i = 0; i < teams.Length; i++) s.Players[i] = new PlayerState { Team = teams[i] & 1, Bot = bots != null && i < bots.Length && bots[i] };
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
            // the team's own players (not Double Agents) take turns at the front spot: one further along each face-off
            var own = new int[2];
            foreach (var p in s.Players) if (p.Stats.DoubleAgent <= 0) own[p.Team & 1]++;
            var turn = s.KickOffTurn;
            s.KickOffTurn++;
            var count = new int[2];
            // everyone else first, then the Double Agents take the other team's next spots
            for (var pass = 0; pass < 2; pass++)
                foreach (var p in s.Players)
                {
                    var agent = p.Stats.DoubleAgent > 0;
                    if ((pass == 1) != agent) continue;
                    var side = agent ? 1 - p.Team : p.Team;
                    var k = count[side]++;
                    var slot = !agent && own[side] > 0 ? (k + turn) % own[side] : k;
                    var sp = FaceOffSpot(side, slot);
                    p.Pos = sp.Pos;
                    p.Facing = agent ? FaceOffSpot(p.Team, 0).Facing : sp.Facing; // agents still face the goal they attack
                    p.Vel = FixVec2.Zero;
                    p.DashCharge = MaxDashes(p);
                    p.DashTime = Fix.Zero;
                    p.DashCoast = Fix.Zero;
                    p.Charge = Fix.Zero;
                    p.NoPickup = Fix.Zero;
                    p.Stun = Fix.Zero;
                    p.GunCooldown = Fix.Zero; p.InvisCooldown = Fix.Zero; p.InvisibleTime = Fix.Zero; p.ShockCooldown = Fix.Zero; p.ShockHold = Fix.Zero;
                    p.SlowTime = Fix.Zero; p.HeldBy = -1; p.HeldTime = Fix.Zero; p.TossTime = Fix.Zero;
                    p.FastTime = Fix.Zero; p.Shoved = Fix.Zero; p.ShovedBy = -1;
                }
            s.PuckPos = Map.PuckSpot;
            s.PuckVel = FixVec2.Zero;
            s.Carrier = -1;
            s.PuckTeam = -1;
            s.PassFrom = -1;
            s.TeamTouch0 = -1; s.TeamTouch1 = -1;
            s.PuckHidden = Fix.Zero; s.PuckHiddenTeam = -1; s.CannonTeam = -1;
            s.Shots = new Shot[0];
            s.Ice = new IcePoint[0];
            s.IceLaying = Fix.Zero;
            s.Stones = new Stone[0];
            s.StoneLaying = Fix.Zero;
            s.Phase = Phase.Countdown;
            s.PhaseTime = T.CountdownTime;
            PlacePylons(s);
            s.Rocks = new Rock[0];
            s.Portals = new Portal[0];
            s.PortalShots = new Shot[0];
            s.PuckPortal = 0;
            var prizes = false;
            foreach (var p in s.Players)
            {
                p.PortalNext = 0; p.PortalCooldown = Fix.Zero; p.PortalVia = 0;
                if (p.SlotPrizeTime > Fix.Zero && (p.SlotPrize == CardLibrary.SlotGiant || p.SlotPrize == CardLibrary.SlotStrong)) prizes = true;
                p.SlotSpin = Fix.Zero; p.SlotWin = 0; p.SlotPrize = 0; p.SlotPrizeTime = Fix.Zero; p.SlotWait = Fix.Zero;
            }
            if (prizes && Cards != null) Cards.Recompute(s);
            HeadStart(s);
        }

        // the face-off group, front to back: (rows out from the back, places across the row). Slot 0 is the kick-off.
        private static readonly int[,] FaceOffShape =
        {
            { 3, 0 }, { 2, 1 }, { 2, -1 }, { 1, 0 }, { 1, 2 }, { 1, -2 }, { 0, 1 }, { 0, -1 },
        };

        /// <summary>
        /// Where the <paramref name="slot"/>-th player of <paramref name="team"/> stands at a face-off: in a tight group just
        /// in front of their own net, the kick-off (slot 0) at the front, nearest the puck. A slot that doesn't fit on the
        /// map (off the floor, or in the boards) falls back to the map's own spawn for it.
        /// </summary>
        public SpawnDef FaceOffSpot(int team, int slot)
        {
            team &= 1;
            // blue's group is red's reflected (or turned) the way the rink is, so neither side ever stands differently:
            // the nets either side of the middle line (x or y) are a mirror image; opposite through the middle, turned
            if (team == 1)
            {
                var c0 = Bot.GoalCentre(Map, 0);
                var c1 = Bot.GoalCentre(Map, 1);
                var red = FaceOffSpot(0, slot);
                if (c1.X == -c0.X && c1.Y == c0.Y)
                    return new SpawnDef { Team = 1, Pos = new FixVec2(-red.Pos.X, red.Pos.Y), Facing = new FixVec2(-red.Facing.X, red.Facing.Y) };
                if (c1.Y == -c0.Y && c1.X == c0.X)
                    return new SpawnDef { Team = 1, Pos = new FixVec2(red.Pos.X, -red.Pos.Y), Facing = new FixVec2(red.Facing.X, -red.Facing.Y) };
                if (c1.X == -c0.X && c1.Y == -c0.Y)
                    return new SpawnDef { Team = 1, Pos = -red.Pos, Facing = -red.Facing };
            }
            GoalDef net = null;
            foreach (var g in Map.Goals) if (g.Team == team) { net = g; break; }
            if (net == null || slot < 0 || slot >= FaceOffShape.GetLength(0)) return Map.SpawnFor(team, slot);
            var centre = new FixVec2((net.Min.X + net.Max.X) / 2, (net.Min.Y + net.Max.Y) / 2);
            FixVec2 front;
            if (net.HasFacing) front = net.Facing;
            else
            {
                var to = Map.PuckSpot - centre;
                // a net with no facing opens along the axis that points most at the puck
                front = Fix.Abs(to.X) >= Fix.Abs(to.Y) ? new FixVec2(to.X >= Fix.Zero ? Fix.One : -Fix.One, Fix.Zero)
                                                      : new FixVec2(Fix.Zero, to.Y >= Fix.Zero ? Fix.One : -Fix.One);
            }
            var half = Fix.Abs(front.X) * (net.Max.X - net.Min.X) / 2 + Fix.Abs(front.Y) * (net.Max.Y - net.Min.Y) / 2;
            var mouth = centre + front * half;
            var across = new FixVec2(-front.Y, front.X);
            var pos = mouth + front * (T.FaceOffBack + T.FaceOffRow * FaceOffShape[slot, 0])
                            + across * (T.FaceOffSpacing * FaceOffShape[slot, 1] / 2);
            if (!Map.Inside(pos) || !PlayerFits(pos)) return Map.SpawnFor(team, slot);
            return new SpawnDef { Team = team, Pos = pos, Facing = front };
        }

        /// <summary>Clamp into lo..hi; a range that's (just) the wrong way round, as on a net's back wall, means its middle.</summary>
        private static Fix ClampAlong(Fix u, Fix lo, Fix hi) { return lo > hi ? (lo + hi) / 2 : Fix.Clamp(u, lo, hi); }

        /// <summary>The net whose box <paramref name="p"/> (a point on a wall's middle line) is right next to, or null.</summary>
        private GoalDef NetNear(FixVec2 p)
        {
            foreach (var g in Map.Goals) if (g.Touches(p, WallHalf + Fix.Ratio(3, 10))) return g;
            return null;
        }

        /// <summary>Which way a net opens: its facing, or (a map that doesn't say) along the axis towards the puck's spot.</summary>
        private FixVec2 NetFront(GoalDef net)
        {
            if (net.HasFacing) return net.Facing;
            var centre = new FixVec2((net.Min.X + net.Max.X) / 2, (net.Min.Y + net.Max.Y) / 2);
            var to = Map.PuckSpot - centre;
            return Fix.Abs(to.X) >= Fix.Abs(to.Y) ? new FixVec2(to.X >= Fix.Zero ? Fix.One : -Fix.One, Fix.Zero)
                                                  : new FixVec2(Fix.Zero, to.Y >= Fix.Zero ? Fix.One : -Fix.One);
        }

        /// <summary>A plain-sized player at <paramref name="at"/> touches no wall.</summary>
        private bool PlayerFits(FixVec2 at)
        {
            var r = T.PlayerRadius + WallHalf;
            var r2 = r * r;
            foreach (var seg in Map.Segments) if (!seg.FarFrom(at, r) && (at - Closest(seg, at)).SqrLength < r2) return false;
            return true;
        }

        /// <summary>
        /// Add a card to a player. Unless MultiUtility is on, a new utility replaces any other utility they hold
        /// (same utility again just stacks another copy).
        /// </summary>
        public void GiveCard(GameState s, PlayerState p, int card)
        {
            if (Cards == null || card < 0 || card >= Cards.Cards.Count) return;
            if (!T.MultiUtility && Cards.IsUtilityIndex(card))
                p.Cards = CardLibrary.WithoutOtherUtilities(Cards, p.Cards, card);
            var cards = new int[p.Cards.Length + 1];
            Array.Copy(p.Cards, cards, p.Cards.Length);
            cards[cards.Length - 1] = card;
            p.Cards = cards;
        }

        /// <summary>
        /// After a swap: a Swap rock whose body no longer has the card goes to the teammate body that has it now (one with
        /// no rock of its own), so it's still yours to swap to.
        /// </summary>
        private static void RockOwners(GameState s)
        {
            for (var k = 0; k < s.Rocks.Length; k++)
            {
                var o = s.Rocks[k].Owner;
                if (o < 0 || o >= s.Players.Length || s.Players[o].Stats.Swap > 0) continue;
                for (var j = 0; j < s.Players.Length; j++)
                {
                    if (j == o || s.Players[j].Team != s.Players[o].Team || s.Players[j].Stats.Swap <= 0) continue;
                    var has = false;
                    foreach (var rk in s.Rocks) if (rk.Owner == j) has = true;
                    if (has) continue;
                    s.Rocks[k].Owner = j;
                    break;
                }
            }
        }

        /// <summary>Drop rocks whose owner no longer has Swap.</summary>
        private void PruneRocks(GameState s)
        {
            if (s.Rocks.Length == 0) return;
            var keep = new System.Collections.Generic.List<Rock>();
            foreach (var rk in s.Rocks)
                if (rk.Owner >= 0 && rk.Owner < s.Players.Length && s.Players[rk.Owner].Stats.Swap > 0) keep.Add(rk);
            if (keep.Count != s.Rocks.Length) s.Rocks = keep.ToArray();
        }

        /// <summary>How big a team's pylon goalie is with <paramref name="cards"/> Pylon Goalie cards: 55% bigger per card after the first (up to 4).</summary>
        public Fix PylonSize(int cards)
        {
            var r = T.PylonRadius;
            for (var k = 1; k < Math.Min(4, cards); k++) r = r * Fix.Ratio(155, 100);
            return r;
        }

        /// <summary>
        /// One pylon goalie in front of each net's mouth whose team has the Pylon Goalie card: 55% bigger for every
        /// copy after the first. It never stands in a goal box (see <see cref="KeepPylonOut"/>).
        /// </summary>
        public void PlacePylons(GameState s)
        {
            var list = new System.Collections.Generic.List<Pylon>();
            for (var team = 0; team < 2; team++)
            {
                var cards = 0;
                foreach (var p in s.Players) if (p.Team == team) cards += p.Stats.Pylons;
                if (cards <= 0) continue;
                var radius = PylonSize(cards);
                var goal = Bot.GoalCentre(Map, team);
                // in front of the net is the way its mouth faces, if the map says; otherwise towards the puck's spot.
                // A bigger one stands further out, so its edge is no closer to the net than a small one's.
                GoalDef net = null;
                foreach (var g in Map.Goals) if (g.Team == team && g.HasFacing) { net = g; break; }
                var front = net != null ? net.Facing : new FixVec2(Map.PuckSpot.X > goal.X ? Fix.One : -Fix.One, Fix.Zero);
                var py = new Pylon { Pos = goal + front * (T.PylonOut + radius - T.PylonRadius), Team = team, Radius = radius };
                KeepPylonOut(ref py);
                list.Add(py);
            }
            s.Pylons = list.ToArray();
        }

        /// <summary>
        /// Pylon goalies may not go into a goal box (so nobody can plug a net with one): push one back out of any box
        /// it reaches into, through the box's open side when the map says which way that is, and stop it going in.
        /// </summary>
        private void KeepPylonOut(ref Pylon py)
        {
            var r = py.Radius;
            foreach (var g in Map.Goals)
            {
                if (!g.Touches(py.Pos, r)) continue;
                FixVec2 n;
                if (!g.Contains(py.Pos))
                {
                    // its edge is over the box: back out to just touching it
                    var near = new FixVec2(Fix.Clamp(py.Pos.X, g.Min.X, g.Max.X), Fix.Clamp(py.Pos.Y, g.Min.Y, g.Max.Y));
                    var d = py.Pos - near;
                    var dist = d.Length;
                    n = dist.Raw > 0 ? d / dist : (g.HasFacing ? g.Facing : FixVec2.Right);
                    py.Pos = near + n * r;
                }
                else
                {
                    // its middle is inside: out through the mouth (or the nearest side, on a map that doesn't say)
                    Fix l = py.Pos.X - g.Min.X, rt = g.Max.X - py.Pos.X, lo = py.Pos.Y - g.Min.Y, hi = g.Max.Y - py.Pos.Y;
                    if (g.HasFacing) n = g.Facing;
                    else
                    {
                        var least = Fix.Min(Fix.Min(l, rt), Fix.Min(lo, hi));
                        n = least == l ? -FixVec2.Right : least == rt ? FixVec2.Right
                            : least == lo ? new FixVec2(Fix.Zero, -Fix.One) : new FixVec2(Fix.Zero, Fix.One);
                    }
                    // nets face straight along an axis: the distance to that side of the box, then the radius past it
                    var edge = n.X > Fix.Half ? rt : n.X < -Fix.Half ? l : n.Y > Fix.Zero ? hi : lo;
                    py.Pos += n * (edge + r);
                }
                var vn = FixVec2.Dot(py.Vel, n);
                if (vn < Fix.Zero) py.Vel -= n * vn;
            }
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

        /// <summary>
        /// Free play's puck drills (d-pad): <see cref="PlayerInput.PuckToMe"/> puts it on your stick;
        /// <see cref="PlayerInput.PuckAtMe"/> sends it at you from a few strides away; <see cref="PlayerInput.PuckAtTheirNet"/> /
        /// <see cref="PlayerInput.PuckAtOurNet"/> shoot it, from where it is, at the other team's net or your own.
        /// </summary>
        private void PuckDrill(GameState s, int i, byte drill)
        {
            var p = s.Players[i];
            var had = s.Carrier;
            s.PassFrom = -1;
            s.CannonTeam = -1;
            s.PuckHidden = Fix.Zero;
            s.PuckPortal = 0;
            StopLaying(s, false);
            s.StoneLaying = Fix.Zero;
            if (drill == PlayerInput.PuckToMe)
            {
                s.Carrier = i;
                s.PuckTeam = p.Team;
                s.PuckPos = CarrySpot(p);
                s.PuckVel = p.Vel;
                return;
            }
            s.Carrier = -1;
            s.PuckTeam = -1;
            if (had >= 0 && had < s.Players.Length) s.Players[had].NoPickup = Fix.Max(s.Players[had].NoPickup, Fix.Ratio(3, 10));
            if (drill == PlayerInput.PuckAtMe)
            {
                // from a spot a few strides away with a clear line to you (a random way round), straight at you
                var start = (int)(s.NextRandom() % 16u);
                var from = Map.PuckSpot;
                for (var k = 0; k < 16; k++)
                {
                    var angle = (start + k * 7) % 16;
                    var dir = Rotated(FixVec2.Right, angle);
                    var at = p.Pos + dir * Fix.FromInt(9);
                    if (Map.Inside(at) && PuckFits(p.Pos, at)) { from = at; break; }
                }
                s.PuckPos = from;
                var to = p.Pos - from;
                s.PuckVel = to.SqrLength.Raw > 0 ? to.Normalized * Fix.FromInt(16) : FixVec2.Zero;
                return;
            }
            // shot from where it is, at a net (the other team's, or your own to practise saves)
            var team = drill == PlayerInput.PuckAtTheirNet ? 1 - p.Team : p.Team;
            var net = Bot.GoalCentre(Map, team);
            var way = net - s.PuckPos;
            s.PuckVel = way.SqrLength.Raw > 0 ? way.Normalized * (T.ShotSpeed * Fix.Ratio(8, 5)) : FixVec2.Zero;
        }

        /// <summary><paramref name="v"/> turned by <paramref name="sixteenths"/> sixteenths of a full turn (exact, no trig).</summary>
        private static FixVec2 Rotated(FixVec2 v, int sixteenths)
        {
            // cos / sin of k × 22.5°, written out so every PC gets exactly the same numbers
            Fix[] c = { Fix.One, Fix.Ratio(9239, 10000), Fix.Ratio(7071, 10000), Fix.Ratio(3827, 10000), Fix.Zero };
            var k = ((sixteenths % 16) + 16) % 16;
            var quarter = k / 4;
            var r = k % 4;
            var cs = c[r]; var sn = c[4 - r];
            var x = v.X * cs - v.Y * sn;
            var y = v.X * sn + v.Y * cs;
            for (var q = 0; q < quarter; q++) { var t = x; x = -y; y = t; }
            return new FixVec2(x, y);
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
        /// <summary>
        /// Goal replays: once the replay is on, a press of shoot is a vote to skip it (bots vote straight away). When
        /// every player has voted, the celebration ends this tick. It's part of the game, so it's the same online.
        /// </summary>
        private void SkipVotes(GameState s, PlayerInput[] inputs, Fix dt)
        {
            if (T.CelebrateTime - s.PhaseTime < T.ReplaySkipFrom) return;
            var all = true;
            for (var i = 0; i < s.Players.Length; i++)
            {
                var p = s.Players[i];
                if (p.Bot) p.SkipVote = true;
                else if ((InputOf(inputs, i).Buttons & ~p.PrevButtons & PlayerInput.ShootCheck) != 0) p.SkipVote = true;
                if (!p.SkipVote) all = false;
            }
            if (all) s.PhaseTime = Fix.Min(s.PhaseTime, dt);
        }

        public int AddPlayer(GameState s, int team, bool bot = false)
        {
            team &= 1;
            var n = 0;
            foreach (var q in s.Players) if (q.Team == team) n++;
            var sp = FaceOffSpot(team, n);
            var players = new PlayerState[s.Players.Length + 1];
            Array.Copy(s.Players, players, s.Players.Length);
            players[players.Length - 1] = new PlayerState { Team = team, Pos = sp.Pos, Facing = sp.Facing, Bot = bot };
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
            // everything that points at a player: theirs is forgotten, everyone after them moves down one
            s.Carrier = Shift(s.Carrier, index);
            s.PassFrom = Shift(s.PassFrom, index);
            s.Touch1 = Shift(s.Touch1, index);
            s.Touch2 = Shift(s.Touch2, index);
            s.TeamTouch0 = Shift(s.TeamTouch0, index);
            s.TeamTouch1 = Shift(s.TeamTouch1, index);
            s.AutoCarrier = Shift(s.AutoCarrier, index);
            s.LastGoalPlayer = Shift(s.LastGoalPlayer, index);
            s.LastGoalBy = Shift(s.LastGoalBy, index);
            s.LastAssist = Shift(s.LastAssist, index);
            s.CannonFrom = Shift(s.CannonFrom, index);
            foreach (var p in players)
            {
                // Friendly Fire: whoever they carried is let go
                if (p.HeldBy == index) p.HeldTime = Fix.Zero;
                p.HeldBy = Shift(p.HeldBy, index);
                p.Pilot = Shift(p.Pilot, index);
                p.ShovedBy = Shift(p.ShovedBy, index);
            }
            // their Swap rock goes with them; a rock they were carrying drops
            var rocks = new System.Collections.Generic.List<Rock>();
            foreach (var rk in s.Rocks)
            {
                if (rk.Owner == index) continue;
                var r = rk;
                r.Owner = Shift(r.Owner, index);
                r.Carrier = Shift(r.Carrier, index);
                rocks.Add(r);
            }
            s.Rocks = rocks.ToArray();
            var shots = new System.Collections.Generic.List<Shot>();
            foreach (var sh in s.Shots)
            {
                if (sh.Owner == index) continue;
                var x = sh;
                x.Owner = Shift(x.Owner, index);
                shots.Add(x);
            }
            s.Shots = shots.ToArray();
            // their portals and portal shots go with them
            var portals = new System.Collections.Generic.List<Portal>();
            foreach (var po in s.Portals)
            {
                if (po.Owner == index) continue;
                var x = po;
                x.Owner = Shift(x.Owner, index);
                portals.Add(x);
            }
            s.Portals = portals.ToArray();
            var pshots = new System.Collections.Generic.List<Shot>();
            foreach (var sh in s.PortalShots)
            {
                if (sh.Owner == index) continue;
                var x = sh;
                x.Owner = Shift(x.Owner, index);
                pshots.Add(x);
            }
            s.PortalShots = pshots.ToArray();
        }

        /// <summary>A player number after player <paramref name="gone"/> left: -1 if it was them, one less if after them.</summary>
        private static int Shift(int i, int gone) { return i == gone ? -1 : i > gone ? i - 1 : i; }

        public Fix RadiusOf(PlayerState p) { return T.PlayerRadius * Fix.Clamp(p.Stats.Size, Fix.Ratio(1, 4), Fix.FromInt(4)); }
        private static Fix WeightOf(PlayerState p) { return Fix.Clamp(p.Stats.Weight, Fix.Ratio(1, 10), Fix.FromInt(10)); }

        // ------------------------------------------------------------------ one tick

        public void Step(GameState s, PlayerInput[] inputs)
        {
            var dt = T.Dt;
            s.Tick++;

            // the host's match-settings commands, sent as a Pick outside the card screen (even paused): add a bot,
            // back to the kick-off, fix the score
            for (int i = 0, n = s.Players.Length; i < n; i++)
            {
                if (s.Phase == Phase.Picking) break;
                HostCommand(s, InputOf(inputs, i).Pick);
            }

            // the host's pause: everything stands still (presses made meanwhile don't count afterwards)
            s.Paused = false;
            for (var i = 0; i < s.Players.Length; i++) if (InputOf(inputs, i).Has(PlayerInput.Pause)) s.Paused = true;
            if (s.Paused)
            {
                for (var i = 0; i < s.Players.Length; i++) s.Players[i].PrevButtons = InputOf(inputs, i).Buttons;
                return;
            }

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

            if (s.Phase == Phase.Scored && T.Replays) SkipVotes(s, inputs, dt);
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
                    var wasAt = p.Pos;
                    var wasIn = p.PortalVia != 0;
                    p.Pos += p.Vel * sub;
                    var inPortal = -1;
                    bool jumpedP;
                    FixVec2 nIn, nOut;
                    if (p.HeldBy < 0)
                    {
                        inPortal = PortalPass(s, ref p.Pos, ref p.Vel, RadiusOf(p), ref p.PortalVia, out jumpedP, out nIn, out nOut);
                        // out the other side facing the way you went in (turned with the portals)
                        if (jumpedP) { p.Facing = PortalTurn(p.Facing, nIn, nOut).Normalized; p.DashDir = PortalTurn(p.DashDir, nIn, nOut); }
                    }
                    else p.PortalVia = 0;
                    CollideWalls(ref p.Pos, ref p.Vel, RadiusOf(p), T.WallBounce, s, inPortal);
                    // (also the step it leaves the portal: part way into the boards, the wall's push mustn't send it out the far side)
                    if ((inPortal >= 0 || wasIn) && Escaped(wasAt, p.Pos)) { p.Pos = wasAt; p.Vel = FixVec2.Zero; p.PortalVia = 0; }
                    if (s.Stones.Length > 0) CollideStones(s, ref p.Pos, ref p.Vel, RadiusOf(p), T.WallBounce);
                    PushPylons(s, p);
                    PushRocks(s, p);
                }
                CarryMates(s);
                GrabTouching(s);
                MovePylons(s, sub);
                MoveRocks(s, sub);
                Bumps(s);
                MovePuck(s, sub);
                BouncePuckAndRocks(s); // rock ↔ puck (after both have moved)
                MoveShots(s, sub);
                if (s.PortalShots.Length > 0) MovePortalShots(s, sub);
                TeamTouches(s);
                if (s.Phase == Phase.Play) CheckGoal(s);
                NoteTouch(s);
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
            if (s.Stones.Length > 0 || s.StoneLaying > Fix.Zero) Stones(s, dt);

            // Invisible Shot runs out (or ends once someone has the puck); a Cannon Ball ends once it's slowed down
            if (s.PuckHidden > Fix.Zero) { s.PuckHidden -= dt; if (s.PuckHidden <= Fix.Zero || s.Carrier >= 0) { s.PuckHidden = Fix.Zero; s.PuckHiddenTeam = -1; } }
            if (s.CannonTeam >= 0 && (s.Carrier >= 0 || s.PuckVel.SqrLength < T.ShotSpeed * T.ShotSpeed)) s.CannonTeam = -1;

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

        /// <summary>A match-settings command from the host (see <see cref="PlayerInput.AddBotRed"/> and the rest); anything else is ignored.</summary>
        public void HostCommand(GameState s, byte cmd)
        {
            switch (cmd)
            {
                case PlayerInput.AddBotRed:
                case PlayerInput.AddBotBlue:
                    if (s.Players.Length >= 8) return;
                    AddPlayer(s, cmd == PlayerInput.AddBotBlue ? 1 : 0, true);
                    if (Cards != null) Cards.Recompute(s);
                    return;
                case PlayerInput.KickOff:
                    if (s.Phase == Phase.GameOver || s.Phase == Phase.Picking) return;
                    FaceOff(s);
                    return;
                case PlayerInput.RedUp: s.Score[0]++; return;
                case PlayerInput.RedDown: s.Score[0] = Math.Max(0, s.Score[0] - 1); return;
                case PlayerInput.BlueUp: s.Score[1]++; return;
                case PlayerInput.BlueDown: s.Score[1] = Math.Max(0, s.Score[1] - 1); return;
            }
            // taking a bot out (only ever a bot)
            if (cmd >= PlayerInput.RemoveBot && cmd < PlayerInput.RemoveBot + 8)
            {
                var idx = cmd - PlayerInput.RemoveBot;
                if (idx >= s.Players.Length || !s.Players[idx].Bot || s.Phase == Phase.Picking) return;
                RemovePlayer(s, idx);
                if (Cards != null) Cards.Recompute(s);
            }
        }

        // ------------------------------------------------------------------ who steers whom

        /// <summary>
        /// The controls each player gets this tick, from each person's own controls (<paramref name="raw"/>, by
        /// player; bots' entries are ignored). People can swap onto bot teammates (<see cref="PlayerInput.Swap"/>)
        /// and are handed to a bot teammate that picks up the puck (unless they turned that off); whoever nobody
        /// steers is played by the bot. On the card screen everyone is back on their own player. Changes who steers
        /// whom in <paramref name="s"/>, so call it once per tick, just before <see cref="Step"/>.
        /// </summary>
        public PlayerInput[] Route(GameState s, PlayerInput[] raw)
        {
            var n = s.Players.Length;
            var pilots = 0L; // who steered whom before (to notice a swap)
            for (var h = 0; h < n; h++) pilots = pilots * 9 + (s.Players[h].Pilot + 1);
            if (s.Phase == Phase.Picking || s.Phase == Phase.GameOver)
                foreach (var p in s.Players) p.Pilot = -1;
            else
            {
                for (var h = 0; h < n; h++)
                {
                    var p = s.Players[h];
                    if (p.Bot) continue;
                    var held = (InputOf(raw, h).Buttons & PlayerInput.Swap) != 0;
                    if (held && !p.SwapHeld) SwapToNext(s, h);
                    p.SwapHeld = held;
                }
                // a bot-run player just got the puck: hand them to a person on that team (one at random)
                var c = s.Carrier;
                if (c >= 0 && c < n && c != s.AutoCarrier && DriverOf(s, c) < 0)
                {
                    var people = new System.Collections.Generic.List<int>();
                    for (var h = 0; h < n; h++)
                        if (!s.Players[h].Bot && s.Players[h].Team == s.Players[c].Team && (InputOf(raw, h).Buttons & PlayerInput.NoAutoSwap) == 0) people.Add(h);
                    if (people.Count > 0)
                    {
                        var who = people.Count == 1 ? people[0] : people[(int)(s.NextRandom() % (uint)people.Count)];
                        s.Players[who].Pilot = who == c ? -1 : c;
                    }
                }
            }
            s.AutoCarrier = s.Carrier;
            // a swap: the cards go with the people (stats again), and a Swap rock with its card
            var now = 0L;
            for (var h = 0; h < n; h++) now = now * 9 + (s.Players[h].Pilot + 1);
            if (now != pilots && Cards != null) { Cards.Recompute(s); RockOwners(s); }
            var inputs = new PlayerInput[n];
            for (var i = 0; i < n; i++)
            {
                var d = DriverOf(s, i);
                if (d < 0) { inputs[i] = Bot.Think(this, s, i); continue; }
                var x = InputOf(raw, d);
                x.Buttons &= unchecked((byte)~(PlayerInput.Swap | PlayerInput.NoAutoSwap));
                inputs[i] = x;
            }
            return inputs;
        }

        /// <summary>The person steering player <paramref name="i"/> (themself, or someone swapped onto them), or -1 when the bot plays them.</summary>
        public static int DriverOf(GameState s, int i)
        {
            if (s == null) return -1;
            for (var h = 0; h < s.Players.Length; h++)
            {
                var p = s.Players[h];
                if (!p.Bot && (p.Pilot >= 0 ? p.Pilot : h) == i) return h;
            }
            return -1;
        }

        /// <summary>The player person <paramref name="h"/> is steering (their own, or a bot teammate), or -1 for a bot.</summary>
        public static int Steers(GameState s, int h)
        {
            if (s == null || h < 0 || h >= s.Players.Length || s.Players[h].Bot) return -1;
            var p = s.Players[h].Pilot;
            return p >= 0 && p < s.Players.Length ? p : h;
        }

        /// <summary>Person <paramref name="h"/> takes over the next bot-run teammate (by player number, wrapping round; their own player counts).</summary>
        private static void SwapToNext(GameState s, int h)
        {
            var me = s.Players[h];
            var cur = Steers(s, h);
            int first = -1, next = -1;
            for (var i = 0; i < s.Players.Length; i++)
            {
                if (i == cur || s.Players[i].Team != me.Team || DriverOf(s, i) >= 0) continue;
                if (first < 0) first = i;
                if (next < 0 && i > cur) next = i;
            }
            var pick = next >= 0 ? next : first;
            if (pick < 0) return;
            me.Pilot = pick == h ? -1 : pick;
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
            if (T.FreePlay && s.Phase == Phase.Play && input.Pick >= PlayerInput.PuckToMe && input.Pick <= PlayerInput.PuckAtOurNet) PuckDrill(s, i, input.Pick);

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
            p.SwapCooldown = Fix.Max(Fix.Zero, p.SwapCooldown - dt);
            p.PortalCooldown = Fix.Max(Fix.Zero, p.PortalCooldown - dt);
            SlotTimers(s, p, dt);
            p.SlowTime = Fix.Max(Fix.Zero, p.SlowTime - dt);
            p.FastTime = Fix.Max(Fix.Zero, p.FastTime - dt);
            p.TossTime = Fix.Max(Fix.Zero, p.TossTime - dt);
            // a thrown teammate lands once they've slowed to skating speed
            if (p.TossTime > Fix.Zero && p.HeldBy < 0 && p.Vel.SqrLength < T.TossEndSpeed * T.TossEndSpeed) { p.TossTime = Fix.Zero; p.DashTime = Fix.Zero; }
            var rockHeld = RockCarriedBy(s, i);
            var carryingRock = rockHeld >= 0;
            // Friendly Fire: the teammate this player is carrying (thrown anyway once held too long)
            var mate = MateHeldBy(s, i);
            if (mate >= 0)
            {
                var m = s.Players[mate];
                m.HeldTime += dt;
                if (m.HeldTime >= T.MateHoldMax) { ThrowMate(s, i, mate, false); mate = -1; }
            }
            var holdingMate = mate >= 0;
            var holding = carrying || carryingRock || holdingMate;
            // carried by a teammate, or flying after being thrown: no skating (you can still shoot the puck)
            var flying = p.HeldBy >= 0 || p.TossTime > Fix.Zero;
            p.Shoved = Fix.Max(Fix.Zero, p.Shoved - dt);
            if (p.Shoved <= Fix.Zero) p.ShovedBy = -1;
            var control = p.Stun > Fix.Zero || p.Shoved > Fix.Zero ? T.StunControl : Fix.One;
            var speed = p.Stats.MoveSpeed * (holding ? p.Stats.MoveSpeedWithPuck : Fix.One);
            if (p.SlowTime > Fix.Zero) speed *= Fix.One - p.Slow;          // Shockwave, the other team's ice
            if (p.FastTime > Fix.Zero) speed *= Fix.One + p.Fast;          // Team Play pass, your team's ice
            if (p.SlotPrizeTime > Fix.Zero && p.SlotPrize == CardLibrary.SlotSpeed) speed *= Fix.One + T.SlotSpeed; // Slot Machine
            if ((carrying || carryingRock) && input.Has(PlayerInput.ChargeBrake)) speed *= T.ChargeMoveSpeed; // charging a shot: slower (not while carrying a teammate)
            var push = flying ? Fix.Zero : T.MoveForce * speed * control;
            p.Vel += move * (push * dt);
            // getting going: an extra push, the further below top speed (along the stick) the stronger
            var stick = move.Length;
            if (stick > Fix.Ratio(1, 20) && T.Acceleration > Fix.Zero && push > Fix.Zero)
            {
                var top = push * stick / T.Drag;
                var along = FixVec2.Dot(p.Vel, move) / stick;
                var short_ = Fix.Clamp01(Fix.One - along / top);
                p.Vel += move * (push * T.Acceleration * short_ * dt);
            }
            var braking = !holding && input.Has(PlayerInput.ChargeBrake);
            p.DashCoast = Fix.Max(Fix.Zero, p.DashCoast - dt);
            // after a dash, lighter drag for a bit so you keep more speed when you chain them
            var drag = p.TossTime > Fix.Zero ? T.MateTossDrag : p.Shoved > Fix.Zero ? T.TossDrag : braking ? T.BrakeDrag : (p.DashCoast > Fix.Zero ? T.DashCoastDrag : T.Drag);
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
                // Puck Gun (when you don't have the puck, a rock or a teammate)
                if (p.Stats.PuckGun > 0 && p.GunCooldown <= Fix.Zero && !holding && !flying) FireGun(s, i);
                if (p.Stats.PortalGun > 0 && p.PortalCooldown <= Fix.Zero && !flying && !carrying) FirePortal(s, i); // not with the puck
                if (p.Stats.SlotMachine > 0 && p.SlotSpin <= Fix.Zero && p.SlotPrizeTime <= Fix.Zero && p.SlotWait <= Fix.Zero) SpinSlots(s, p);
                if (p.Stats.Swap > 0 && !carrying && !holdingMate && !flying) UseSwap(s, i); // no throw/teleport while holding the puck or a teammate
            }
            // Shockwave: hold the utility button to grow it, let go to fire it (no cooldown)
            if (p.Stats.Shockwave > 0 && !flying)
            {
                if (input.Has(PlayerInput.Utility)) p.ShockHold = Fix.Min(T.ShockChargeTime, p.ShockHold + dt);
                else if (p.ShockHold > Fix.Zero) { FireShockwave(s, i); p.ShockHold = Fix.Zero; }
            }
            else p.ShockHold = Fix.Zero;

            var step = dt * Fix.Clamp(p.Stats.ChargeSpeed, Fix.Ratio(1, 10), Fix.FromInt(10)) / T.ChargeTime;
            var full = Fix.One + Fix.Clamp(p.Stats.OverCharge, Fix.Zero, Fix.FromInt(5));
            if (!holding)
            {
                // pre-charge: holding charge/brake without the puck charges a shot (and still brakes), so a pass can
                // be fired on straight away (a one-timer). Let go without the puck and the charge is gone.
                p.Charge = input.Has(PlayerInput.ChargeBrake) && !flying ? Fix.Min(full, p.Charge + step) : Fix.Zero;
                // after a quick shot: wait until Shoot/check is let go before a hold can check
                if (!input.Has(PlayerInput.ShootCheck)) p.SuppressDash = false;
                // check: a dash that knocks people back. Not while carrying the puck or a rock, and not while dazed.
                // Holding the button keeps dashing: the next one as soon as there's a dash ready and the last one has finished.
                var dashWanted = !p.SuppressDash
                    && ((pressed & PlayerInput.ShootCheck) != 0 || (input.Has(PlayerInput.ShootCheck) && p.DashTime <= Fix.Zero));
                if (dashWanted && p.DashCharge >= Fix.One && p.Stun <= Fix.Zero && !flying)
                {
                    // the way you're skating if you're pushing the stick and have "dash where you move" on; else the way you face
                    var dashDir = input.Has(PlayerInput.DashMove) && move.SqrLength > deadzone * deadzone ? move.Normalized : p.Facing;
                    p.Vel += dashDir * (T.DashSpeed * p.Stats.DashPower);
                    p.DashDir = dashDir;
                    p.DashCharge -= Fix.One;
                    p.DashTime = T.DashHitTime;
                    p.DashCoast = T.DashCoast;
                }
                return;
            }

            // with the puck or a rock: shoot/check fires now; charge/brake charges until let go
            if ((pressed & PlayerInput.ShootCheck) != 0)
            {
                if (carrying) Shoot(s, i, false); else if (carryingRock) ShootRock(s, i, rockHeld, false); else ThrowMate(s, i, mate, false);
                p.SuppressDash = true;
                return;
            }
            // holding charge/brake as you pick it up carries on charging (a pre-charge keeps what it has)
            if (input.Has(PlayerInput.ChargeBrake)) p.Charge = Fix.Min(full, p.Charge + step);
            else if (p.Charge > Fix.Zero)
            {
                if (carrying) Shoot(s, i, true); else if (carryingRock) ShootRock(s, i, rockHeld, true); else ThrowMate(s, i, mate, true);
            }
        }

        /// <summary>Index of the rock this player is carrying, or -1.</summary>
        private static int RockCarriedBy(GameState s, int player)
        {
            for (var k = 0; k < s.Rocks.Length; k++) if (s.Rocks[k].Carrier == player) return k;
            return -1;
        }

        private static void TurnTowards(PlayerState p, FixVec2 want, Fix share)
        {
            var turned = (p.Facing + (want - p.Facing) * Fix.Min(Fix.One, share)).Normalized;
            p.Facing = turned.SqrLength.Raw > 0 ? turned : want;
        }

        /// <summary>A quick shot's speed: Power Shot makes it a full charge's (Over Charge doesn't add).</summary>
        private Fix QuickShotSpeed(PlayerState p) { return p.Stats.QuickShotFull > 0 ? T.ChargedShotMax : T.ShotSpeed * p.Stats.QuickShotPower; }

        private void Shoot(GameState s, int i, bool released)
        {
            var p = s.Players[i];
            // letting go of charge: the charge decides. Shoot/check: a quick shot, or the charge so far if that's harder.
            // 0..1: tap → full (Min → Max). Above 1 (Over Charge): scales a full shot — 1.5 → 1.5× Max, 2 → 2×, …
            var charged = p.Charge <= Fix.One
                ? T.ChargedShotMin + (T.ChargedShotMax - T.ChargedShotMin) * p.Charge
                : T.ChargedShotMax * p.Charge;
            var quick = QuickShotSpeed(p);
            var speed = (released ? charged : Fix.Max(quick, p.Charge > Fix.Zero ? charged : Fix.Zero)) * p.Stats.ShotPower;
            s.Carrier = -1;
            // from where the puck is (it may have been pushed aside), unless it isn't actually at the wedge yet
            var reach = RadiusOf(p) + T.PuckRadius + T.CarrySlack;
            if ((s.PuckPos - p.Pos).SqrLength > reach * reach || !PuckFits(p.Pos, s.PuckPos)) s.PuckPos = CarrySpot(p);
            s.PuckVel = p.Facing * speed + p.Vel * Fix.Half;
            s.PuckTeam = p.Team;
            s.PassFrom = i; // a pass, if a teammate gets it before an opponent touches it
            // noted for the sound side: a shot, and how hard next to a fully charged one
            p.ShotsFired++;
            p.ShotStrength = Fix.Clamp01(speed / T.ChargedShotMax);
            // Ice Trail: every shot lays ice behind the puck
            if (p.Stats.IceTrail > 0) StartIce(s, p);
            // Stone Wall: a full charge raises a wall along the puck's path
            if (released && p.Charge >= Fix.One && p.Stats.StoneWall > 0) StartStone(s);
            // Invisible Shot: a quick shot hides the puck from the other team for a moment
            s.PuckHidden = Fix.Zero; s.PuckHiddenTeam = -1;
            if (!released && p.Stats.GhostShot > 0) { s.PuckHidden = T.GhostShotTime; s.PuckHiddenTeam = p.Team; }
            // Cannon Ball: a full charge knocks back whoever on the other team catches it
            s.CannonTeam = released && p.Charge >= Fix.One && p.Stats.CannonBall > 0 ? p.Team : -1;
            s.CannonFrom = s.CannonTeam >= 0 ? i : -1;
            p.Charge = Fix.Zero;
            p.NoPickup = T.ShotPickupDelay;
        }

        // ------------------------------------------------------------------ Ice Trail

        // ------------------------------------------------------------------ Stone Wall

        /// <summary>Start raising a wall behind the puck that was just shot.</summary>
        private void StartStone(GameState s)
        {
            var speed = s.PuckVel.Length;
            if (speed < Fix.One) return;
            s.StoneDir = s.PuckVel / speed;
            s.StoneTip = s.PuckPos + s.StoneDir * Fix.Ratio(3, 10); // clear of the shooter's wedge
            s.StoneLaying = T.StoneLayTime;
            s.StoneLeft = T.StoneLength;
            s.StoneWalls++;
        }

        private void Stones(GameState s, Fix dt)
        {
            // raising: a piece at a time, a little behind the puck, until it's long enough, the time's up, someone
            // has the puck, or it turned (off the boards, a body): a wall must never trap its own puck
            if (s.StoneLaying > Fix.Zero)
            {
                s.StoneLaying -= dt;
                var speed = s.PuckVel.Length;
                if (s.Carrier >= 0 || s.StoneLaying <= Fix.Zero || s.StoneLeft <= Fix.Zero || speed < Fix.One)
                    s.StoneLaying = Fix.Zero;
                else if (FixVec2.Dot(s.PuckVel, s.StoneDir) < speed * Fix.Ratio(7, 10))
                {
                    // it turned (off the boards, a body): carry on along the new way, a fresh piece starting behind it
                    s.StoneDir = s.PuckVel / speed;
                    s.StoneTip = s.PuckPos - s.StoneDir * T.StoneGap;
                }
                else
                {
                    var end = s.PuckPos - s.PuckVel / speed * T.StoneGap;
                    var d = end - s.StoneTip;
                    var len = d.Length;
                    if (len >= T.StoneSpacing && FixVec2.Dot(d, s.StoneDir) > Fix.Zero)
                    {
                        if (len > s.StoneLeft) { end = s.StoneTip + d / len * s.StoneLeft; len = s.StoneLeft; }
                        if (FirstWallHit(s.StoneTip, end) < Fix.One || !Map.Inside(s.StoneTip)) s.StoneTip = end; // never through the boards: start again past them
                        else
                        {
                            var a = new Stone[s.Stones.Length + 1];
                            Array.Copy(s.Stones, a, s.Stones.Length);
                            a[s.Stones.Length] = new Stone { A = s.StoneTip, B = end, Life = T.StoneLife, Wall = s.StoneWalls };
                            s.Stones = a;
                            s.StoneTip = end;
                            s.StoneLeft -= len;
                        }
                    }
                }
            }
            // crumbling
            var gone = 0;
            for (var k = 0; k < s.Stones.Length; k++)
            {
                s.Stones[k].Life -= dt;
                if (s.Stones[k].Life <= Fix.Zero) gone++;
            }
            if (gone > 0)
            {
                var keep = new Stone[s.Stones.Length - gone];
                var j = 0;
                foreach (var st in s.Stones) if (st.Life > Fix.Zero) keep[j++] = st;
                s.Stones = keep;
            }
        }

        /// <summary>Keep a circle out of the Stone Walls (bouncing off them like the boards).</summary>
        private void CollideStones(GameState s, ref FixVec2 pos, ref FixVec2 vel, Fix r, Fix bounce)
        {
            r += WallHalf;
            foreach (var st in s.Stones)
            {
                var half = (st.B - st.A) * Fix.Half;
                var mid = st.A + half;
                var len2 = half.SqrLength;
                var c = len2.Raw == 0 ? mid : mid + half * Fix.Clamp(FixVec2.Dot(pos - mid, half) / len2, -Fix.One, Fix.One);
                var d = pos - c;
                if (d.SqrLength >= r * r) continue;
                var dist = d.Length;
                var n = dist.Raw > 0 ? d / dist : new FixVec2(-half.Y, half.X).Normalized;
                pos = c + n * r;
                var vn = FixVec2.Dot(vel, n);
                if (vn < Fix.Zero) vel -= n * (vn * (Fix.One + bounce));
            }
        }

        private void StartIce(GameState s, PlayerState shooter)
        {
            StopLaying(s, false); // a new trail: the last one carries on melting as it was
            s.IceTrails++;
            s.IceLaying = T.IceTime;
            s.IceAge = Fix.Zero;
            AddIce(s, new IcePoint { Pos = s.PuckPos, Trail = s.IceTrails, Team = shooter.Team, Slow = T.IceSlow * Math.Min(3, shooter.Stats.IceTrail), Laying = true, Melt = T.IceLife });
        }

        private static void AddIce(GameState s, IcePoint p)
        {
            if (s.Ice.Length >= 1200) return; // plenty: a trail is a few hundred points at most
            var a = new IcePoint[s.Ice.Length + 1];
            System.Array.Copy(s.Ice, a, s.Ice.Length);
            a[s.Ice.Length] = p;
            s.Ice = a;
        }

        /// <summary>The trail being laid is finished (time's up, or someone took the puck). What's there melts as it would.</summary>
        private void StopLaying(GameState s, bool pickedUp)
        {
            s.IceLaying = Fix.Zero;
            for (var i = 0; i < s.Ice.Length; i++) s.Ice[i].Laying = false;
        }

        private void Ice(GameState s, Fix dt)
        {
            // laying: a point every so often behind the loose puck, until the time's up or someone picks it up.
            // The first point lasts IceLife; the melting then creeps along the trail IceMeltPace times slower than it was laid.
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
                        p.Melt = T.IceLife + s.IceAge * (T.IceMeltPace - Fix.One);
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
            // on the ice: the other team is slowed, the team whose ice it is sped up (IceBoost), while they're on it
            foreach (var q in s.Players)
            {
                var reach = T.IceRadius + RadiusOf(q) * Fix.Half;
                Fix slow = Fix.Zero, fast = Fix.Zero;
                foreach (var p in s.Ice)
                {
                    if ((q.Pos - p.Pos).SqrLength > reach * reach) continue;
                    if (p.Team != q.Team) slow = Fix.Max(slow, p.Slow);
                    else fast = T.IceBoost;
                }
                if (slow > Fix.Zero) SlowDown(q, slow, Fix.Ratio(1, 5));
                if (fast > Fix.Zero) SpeedUp(q, fast, Fix.Ratio(1, 5));
            }
        }

        // ------------------------------------------------------------------ the puck

        /// <summary>
        /// Where a carried puck sits: just in front of the player, or as far out as it can go without touching a wall
        /// (at any angle), so it never sits in the boards and a shot from there can't start outside the rink.
        /// Pressed right up against a wall, it may sit inside the player's body (the drawing side puts an outline on it).
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

        /// <summary>True when the carried puck overlaps the carrier's body (for the outline on screen).</summary>
        public bool PuckClippedInCarrier(GameState s)
        {
            if (s == null || s.Carrier < 0 || s.Carrier >= s.Players.Length) return false;
            var c = s.Players[s.Carrier];
            var min = RadiusOf(c); // centre of puck inside the player disc → under the body
            return (s.PuckPos - c.Pos).SqrLength < min * min;
        }

        /// <summary>A puck at <paramref name="at"/> touches no wall, and no wall lies between it and <paramref name="from"/>.</summary>
        private bool PuckFits(FixVec2 from, FixVec2 at)
        {
            if (FirstWallHit(from, at) < Fix.One) return false;
            var r = T.PuckRadius + WallHalf;
            var r2 = r * r;
            foreach (var seg in Map.Segments) if (!seg.FarFrom(at, r) && (at - Closest(seg, at)).SqrLength < r2) return false;
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
                bool jumped;
                var puckWasIn = s.PuckPortal != 0;
                var inPortal = PortalPass(s, ref s.PuckPos, ref s.PuckVel, T.PuckRadius, ref s.PuckPortal, out jumped);
                if (inPortal < 0 && puckWasIn && Escaped(from, s.PuckPos)) { s.PuckPos = from; s.PuckVel = -s.PuckVel * T.PuckWallBounce; }
                if (inPortal >= 0)
                {
                    // in a portal: the wall there lets it in (no wall check), anything else still stops it
                    if (jumped) { StopLaying(s, false); s.StoneLaying = Fix.Zero; } // an Ice Trail / Stone Wall being laid ends here
                    CollideWalls(ref s.PuckPos, ref s.PuckVel, T.PuckRadius, T.PuckWallBounce, s, inPortal);
                    if (Escaped(from, s.PuckPos)) { s.PuckPos = from; s.PuckVel = -s.PuckVel * T.PuckWallBounce; s.PuckPortal = 0; }
                    CollidePylons(s, ref s.PuckPos, ref s.PuckVel, T.PuckRadius, T.PuckWallBounce);
                    if (s.Stones.Length > 0) CollideStones(s, ref s.PuckPos, ref s.PuckVel, T.PuckRadius, T.PuckWallBounce);
                    before = s.PuckPos;
                    continue;
                }
                // never through a wall: if the step crossed one, stop just before it and bounce
                var hit = FirstWallHit(from, s.PuckPos);
                if (hit < Fix.One) s.PuckPos = from;
                CollideWalls(ref s.PuckPos, ref s.PuckVel, T.PuckRadius, T.PuckWallBounce);
                if (hit < Fix.One && FixVec2.Dot(s.PuckVel, s.PuckPos - from) >= Fix.Zero) s.PuckVel = -s.PuckVel * T.PuckWallBounce;
                CollidePylons(s, ref s.PuckPos, ref s.PuckVel, T.PuckRadius, T.PuckWallBounce);
                if (s.Stones.Length > 0) CollideStones(s, ref s.PuckPos, ref s.PuckVel, T.PuckRadius, T.PuckWallBounce);
            }
            KeepPuckIn(s, before);

            // (a pass still counts if it brushes past an opponent or bounces off them: only an opponent taking the puck
            // ends it, at the pick-up below)

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
                    // already holding a Swap rock or a teammate: can't pick the puck up until they shoot/drop it
                    if (RockCarriedBy(s, i) >= 0 || MateHeldBy(s, i) >= 0) continue;
                    if (p.NoPickup > Fix.Zero || (d2 >= r * r && !(T.WedgePickup && OnWedge(p, s.PuckPos)))) continue;
                    if (best < 0 || d2 < bestD) { best = i; bestD = d2; ties = 1; }
                    else if (d2 == bestD && s.NextRandom() % (uint)(++ties) == 0) best = i; // fair among equals
                }
                if (best >= 0)
                {
                    var p = s.Players[best];
                    if (IsSave(s, best)) s.Players[Credit(s, best)].Saves++;
                    if (s.PassFrom >= 0 && s.PassFrom != best && s.PassFrom < s.Players.Length && s.Players[s.PassFrom].Team == p.Team)
                    {
                        Passed(s, s.PassFrom, best);
                        // a pass carries the teammate on: a push along the puck's way, more for a harder pass
                        var boost = s.PuckVel * T.PassBoost;
                        if (boost.SqrLength > T.PassBoostMax * T.PassBoostMax) boost = boost.Normalized * T.PassBoostMax;
                        p.Vel += boost;
                    }
                    s.PassFrom = -1;
                    s.Carrier = best;
                    s.PuckTeam = p.Team;
                    // Cannon Ball: catching the other team's full charge: it keeps going, and takes you with it along
                    // the shot (whatever way you were skating), gliding with little control for a moment. No hit pause.
                    if (s.CannonTeam >= 0 && s.CannonTeam != p.Team && s.PuckVel.SqrLength > T.ShotSpeed * T.ShotSpeed)
                    {
                        var light = Fix.One / WeightOf(p) * Fix.Clamp(p.Stats.KnockbackTaken, Fix.Ratio(1, 20), Fix.FromInt(10));
                        var dir = s.PuckVel.Normalized;
                        var along = FixVec2.Dot(p.Vel, dir);
                        var push = s.PuckVel.Length * T.CannonCarry * light;
                        // keep any sideways speed, replace the speed along the shot with the push (if it's harder)
                        p.Vel = p.Vel - dir * along + dir * Fix.Max(push, along);
                        p.Shoved = T.CannonTime;
                        p.ShovedBy = s.CannonFrom;
                    }
                    s.CannonTeam = -1;
                    s.PuckPos = CarrySpot(p); // (a pre-charge carries on: see Control)
                    s.PuckVel = p.Vel;
                    return;
                }
            }

            // players who can't pick it up push it out of their way. Several at once: the farthest first and the
            // nearest last (so the nearest decides where it ends up); an exact tie is a coin toss. Never by player order:
            // the last push wins, so a fixed order hands the puck to the same player again and again.
            var n0 = 0;
            for (var i = 0; i < s.Players.Length; i++)
            {
                var r = RadiusOf(s.Players[i]) + T.PuckRadius;
                var dd = (s.PuckPos - s.Players[i].Pos).SqrLength;
                if (dd >= r * r) continue;
                // insert by distance, farthest first
                var at = n0;
                while (at > 0 && (_pushD[at - 1] < dd || (_pushD[at - 1] == dd && (s.NextRandom() & 1) == 0))) { _pushOrder[at] = _pushOrder[at - 1]; _pushD[at] = _pushD[at - 1]; at--; }
                _pushOrder[at] = i; _pushD[at] = dd;
                n0++;
            }
            for (var k = 0; k < n0; k++)
            {
                var p = s.Players[_pushOrder[k]];
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
        /// Does a puck at <paramref name="puck"/> touch <paramref name="p"/>'s wedge (the arrowhead in front, as drawn)?
        /// Worked out across the player's facing with the side folded over (|y|), so both sides, and mirrored
        /// players, get exactly the same answer.
        /// </summary>
        private readonly int[] _pushOrder = new int[8];
        private readonly Fix[] _pushD = new Fix[8];

        private bool OnWedge(PlayerState p, FixVec2 puck) { return OnWedge(p, puck, T.PuckRadius); }

        /// <summary>Whether a circle of radius <paramref name="pr"/> at <paramref name="at"/> touches the player's wedge.</summary>
        private bool OnWedge(PlayerState p, FixVec2 at, Fix pr)
        {
            var R = RadiusOf(p);
            var d = at - p.Pos;
            var x = FixVec2.Dot(d, p.Facing);
            var tipX = R * T.WedgeTip;
            var backX = R * T.WedgeBack;
            if (x < backX - pr || x > tipX + pr) return false;
            var y = Fix.Abs(FixVec2.Cross(p.Facing, d));
            var halfW = R * T.WedgeHalfWidth;
            if (y > halfW + pr) return false;
            // inside the triangle: behind the slanted edge and in front of the back edge
            var span = tipX - backX;
            if (x >= backX && y * span <= halfW * (tipX - x)) return true;
            // near an edge: the slanted one (tip to back corner), or the back one
            var q = new FixVec2(x, y);
            var tip = new FixVec2(tipX, Fix.Zero);
            var corner = new FixVec2(backX, halfW);
            var e = corner - tip;
            var t = Fix.Clamp(FixVec2.Dot(q - tip, e) / e.SqrLength, Fix.Zero, Fix.One);
            if ((q - (tip + e * t)).SqrLength <= pr * pr) return true;
            var onBack = new FixVec2(backX, Fix.Min(y, halfW));
            return (q - onBack).SqrLength <= pr * pr;
        }

        /// <summary>
        /// Where the carried puck goes this substep. Out in the open it sits at the front of the wedge. Against a wall
        /// (or pylon) it pulls in along the facing — even into the carrier's body — so you keep it; another player's
        /// body can still shove it off the wedge and make you lose it.
        /// Returns false if the carrier lost control.
        /// </summary>
        private bool Carry(GameState s, Fix sub)
        {
            var ci = s.Carrier;
            var c = s.Players[ci];
            var reach = RadiusOf(c) + T.PuckRadius + Fix.Ratio(1, 50);
            var spot = c.Pos + c.Facing * reach;
            FixVec2 pos;
            var into = CarryPortal(s, c, spot);
            if (into >= 0)
            {
                // into a portal ahead of you: it slides in (drawn coming out of the other one), never far enough to
                // reach a net's box behind that wall; once you go through, it's in front of you again over there
                var P = s.Portals[into];
                var face = P.Pos + P.Normal * WallHalf;
                var depth = FixVec2.Dot(spot - face, P.Normal);
                var deepest = -(WallHalf * 2 - T.PuckRadius - Fix.Ratio(1, 20));
                s.PuckPos = depth < deepest ? spot + P.Normal * (deepest - depth) : spot;
                s.PuckVel = c.Vel;
                s.PuckPortal = P.Id;
                return true;
            }
            s.PuckPortal = 0;
            if (!CarryBlocked(s, ci, spot)) pos = spot;
            else
            {
                // boards / pylons: sit as far forward as fits (may clip into the carrier). Other bodies can still shove.
                pos = CarrySpot(c);
                var shoved = false;
                for (var j = 0; j < s.Players.Length; j++)
                {
                    if (j == ci) continue;
                    var q = s.Players[j];
                    var min = RadiusOf(q) + T.PuckRadius;
                    var d = pos - q.Pos;
                    if (d.SqrLength >= min * min) continue;
                    var dist = d.Length;
                    pos = q.Pos + (dist.Raw > 0 ? d / dist : c.Facing) * min;
                    shoved = true;
                }
                if (shoved)
                {
                    // after a shove: keep out of the boards, then slide round the wedge if squeezed into the carrier
                    var v = FixVec2.Zero;
                    CollideWalls(ref pos, ref v, T.PuckRadius, Fix.Zero);
                    CollidePylons(s, ref pos, ref v, T.PuckRadius, Fix.Zero);
                    CollideRocks(s, ref pos, ref v, T.PuckRadius, Fix.Zero);
                    var o = pos - c.Pos;
                    var od = o.Length;
                    if (od < reach - Fix.Ratio(1, 100))
                    {
                        var dir = od.Raw > 0 ? o / od : c.Facing;
                        var cross = FixVec2.Cross(c.Facing, dir);
                        var side = cross > Fix.Zero ? Fix.One : cross < Fix.Zero ? -Fix.One : ((s.NextRandom() & 1) == 0 ? Fix.One : -Fix.One);
                        var tangent = new FixVec2(-dir.Y, dir.X) * side;
                        var slid = dir * reach + tangent * (reach - od + Fix.Ratio(1, 50));
                        pos = c.Pos + slid.Normalized * reach;
                        v = FixVec2.Zero;
                        CollideWalls(ref pos, ref v, T.PuckRadius, Fix.Zero);
                        CollidePylons(s, ref pos, ref v, T.PuckRadius, Fix.Zero);
                        CollideRocks(s, ref pos, ref v, T.PuckRadius, Fix.Zero);
                    }
                }
            }

            if (FirstWallHit(c.Pos, pos) < Fix.One || !PuckFits(c.Pos, pos)) pos = CarrySpot(c);

            // still in control? Walls alone never take it (clipping into you is fine). Another body shoving it off the
            // wedge still does.
            var away = pos - c.Pos;
            var len = away.Length;
            var inCarrier = len <= RadiusOf(c);
            var lost = !inCarrier && (len > reach + T.CarrySlack || (len.Raw > 0 && FixVec2.Dot(away / len, c.Facing) < T.CarryHoldCos));
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

        /// <summary>
        /// The portal (an index) a carried puck at <paramref name="spot"/> is going into ahead of its carrier, or -1: the
        /// carrier is on that portal's side facing into it, and the puck's at or past its face, within its length.
        /// </summary>
        private int CarryPortal(GameState s, PlayerState c, FixVec2 spot)
        {
            if (s.Portals.Length < 2) return -1;
            var half = T.PortalLength * Fix.Half;
            for (var k = 0; k < s.Portals.Length; k++)
            {
                var P = s.Portals[k];
                if (Partner(s, k) < 0) continue;
                var face = P.Pos + P.Normal * WallHalf;
                if (FixVec2.Dot(c.Facing, P.Normal) >= Fix.Zero) continue;                     // facing away from it
                if (FixVec2.Dot(c.Pos - face, P.Normal) < -Fix.Ratio(1, 20)) continue;          // the carrier's past it
                if (FixVec2.Dot(spot - face, P.Normal) >= T.PuckRadius) continue;              // not reached it yet
                if (Fix.Abs(FixVec2.Dot(spot - face, new FixVec2(-P.Normal.Y, P.Normal.X))) > half) continue;
                return k;
            }
            return -1;
        }

        /// <summary>Is the puck part way into a portal, past its face (so in the wall there, not in anything behind it)?</summary>
        private bool PuckInPortal(GameState s)
        {
            if (s.PuckPortal == 0) return false;
            foreach (var P in s.Portals)
                if (P.Id == s.PuckPortal) return FixVec2.Dot(s.PuckPos - (P.Pos + P.Normal * WallHalf), P.Normal) < T.PuckRadius;
            return false;
        }

        /// <summary>Would a carried puck at <paramref name="at"/> touch the boards, a pylon or another player (or be the other side of a wall)?</summary>
        private bool CarryBlocked(GameState s, int carrier, FixVec2 at)
        {
            if (!PuckFits(s.Players[carrier].Pos, at)) return true;
            foreach (var py in s.Pylons)
            {
                var r = py.Radius + T.PuckRadius;
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
                if (j == s.Carrier || q.Team == c.Team || q.NoPickup > Fix.Zero || q.Stun > Fix.Zero || MateHeldBy(s, j) >= 0) continue;
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
            if (PuckInPortal(s)) return; // half way through a portal (on the back of a net): not in the net
            // Cannon Ball: a carrier the shot is still carrying off, shoved into their own net (puck or body in): a goal
            // for the shooter
            if (s.Carrier >= 0)
            {
                var c = s.Players[s.Carrier];
                var by = c.ShovedBy;
                if (c.Shoved > Fix.Zero && by >= 0 && by < s.Players.Length && s.Players[by].Team != c.Team)
                    foreach (var g in Map.Goals)
                        if (g.Team == c.Team && (g.Touches(s.PuckPos, T.PuckRadius) || g.Touches(c.Pos, RadiusOf(c))))
                        {
                            Goal(s, 1 - g.Team, by);
                            return;
                        }
            }
            foreach (var g in Map.Goals)
            {
                if (!g.Touches(s.PuckPos, T.PuckRadius)) continue; // the moment the puck touches the goal box
                // a defender may carry the puck into their own net (it's safe while they hold it); let it go in there
                // and it's a goal for the other team
                if (s.Carrier >= 0 && s.Players[s.Carrier].Team == g.Team) continue;
                Goal(s, 1 - g.Team, -1);
                return;
            }
        }

        /// <summary>A goal for <paramref name="scorer"/>: by <paramref name="by"/> if known (a Cannon Ball), else worked out.</summary>
        private void Goal(GameState s, int scorer, int by)
        {
            {
                s.Score[scorer]++;
                s.LastScorer = scorer;
                s.LastGoalPlayer = by >= 0 ? by : WhoScored(s, scorer);
                // stats: the goal is the shooter's (or carrier's) on the scoring team; an own goal is nobody's. The
                // assist is the teammate who had it just before them.
                NoteTouch(s);
                var credit = by >= 0 ? by : Shooter(s, scorer);
                // nobody on the scoring team shot or carried it in, but one of them touched it since the face-off:
                // theirs (the last to touch it). Only an own goal when none of them did.
                var touched = scorer == 0 ? s.TeamTouch0 : s.TeamTouch1;
                if (credit < 0 && touched >= 0 && touched < s.Players.Length && s.Players[touched].Team == scorer) credit = touched;
                s.LastAssist = -1;
                s.LastGoalBy = -1;
                if (credit >= 0)
                {
                    s.LastGoalBy = Credit(s, credit);
                    s.Players[s.LastGoalBy].Goals++;
                    if (by < 0 && s.Touch1 == credit && s.Touch2 >= 0 && s.Touch2 != credit && s.Touch2 < s.Players.Length && s.Players[s.Touch2].Team == scorer)
                    {
                        s.LastAssist = Credit(s, s.Touch2);
                        if (s.LastAssist != s.LastGoalBy) s.Players[s.LastAssist].Assists++;
                        else s.LastAssist = -1;
                    }
                }
                s.Touch1 = -1; s.Touch2 = -1;
                s.TeamTouch0 = -1; s.TeamTouch1 = -1;
                var blastAt = s.PuckPos;
                if (s.Carrier >= 0) { s.PuckVel = s.Players[s.Carrier].Vel; s.Carrier = -1; }
                s.Phase = Phase.Scored;
                s.PhaseTime = T.CelebrateTime;
                foreach (var p in s.Players) p.SkipVote = false;
                // goal explosion: shove everyone and loose rocks away from the puck (puck stays in the net)
                BlastFrom(s, blastAt, T.GoalBlastRange, T.DashKnockback * T.GoalBlastPush, -1, true, false, true);
            }
        }

        /// <summary>The scoring team's player who put it in (the last shooter, else the carrier), or -1 (an own goal).</summary>
        private static int Shooter(GameState s, int team)
        {
            if (s.PassFrom >= 0 && s.PassFrom < s.Players.Length && s.Players[s.PassFrom].Team == team) return s.PassFrom;
            if (s.Carrier >= 0 && s.Carrier < s.Players.Length && s.Players[s.Carrier].Team == team) return s.Carrier;
            return -1;
        }

        /// <summary>Who a stat goes to: the person steering player <paramref name="i"/> (swapped onto a bot, it's theirs), else the player.</summary>
        private static int Credit(GameState s, int i)
        {
            var d = DriverOf(s, i);
            return d >= 0 ? d : i;
        }

        /// <summary>Who touched the puck (carrying it, or it against them): the last of each team, for own goals.</summary>
        private void TeamTouches(GameState s)
        {
            if (s.Carrier >= 0) { Touched(s, s.Carrier); return; }
            var near = T.PuckRadius + Fix.Ratio(1, 20);
            for (var i = 0; i < s.Players.Length; i++)
            {
                var p = s.Players[i];
                var r = RadiusOf(p) + near;
                if ((p.Pos - s.PuckPos).SqrLength <= r * r) Touched(s, i);
            }
        }

        private static void Touched(GameState s, int i)
        {
            if (i < 0 || i >= s.Players.Length) return;
            if (s.Players[i].Team == 0) s.TeamTouch0 = i;
            else if (s.Players[i].Team == 1) s.TeamTouch1 = i;
        }

        /// <summary>Keep the last two players to hold the puck (for assists).</summary>
        private static void NoteTouch(GameState s)
        {
            if (s.Carrier < 0 || s.Carrier == s.Touch1) return;
            s.Touch2 = s.Touch1;
            s.Touch1 = s.Carrier;
        }

        /// <summary>
        /// A save: a player picking up a shot from the other team that was on its way into their own net (heading
        /// at it, and due there within about a second and a half).
        /// </summary>
        private bool IsSave(GameState s, int picker)
        {
            if (s.PassFrom < 0 || s.PassFrom >= s.Players.Length) return false;
            var p = s.Players[picker];
            if (s.Players[s.PassFrom].Team == p.Team) return false;
            var speed = s.PuckVel.Length;
            if (speed < Fix.FromInt(6)) return false;
            var to = Bot.GoalCentre(Map, p.Team) - s.PuckPos;
            var dist = to.Length;
            if (dist.Raw <= 0) return true;
            if (FixVec2.Dot(s.PuckVel, to) < speed * dist * Fix.Ratio(85, 100)) return false;
            return dist < speed * Fix.Ratio(3, 2);
        }

        /// <summary>Player to celebrate: the last passer/shooter on the scoring team, else whoever on that team is nearest the puck.</summary>
        private static int WhoScored(GameState s, int team)
        {
            if (s.PassFrom >= 0 && s.PassFrom < s.Players.Length && s.Players[s.PassFrom].Team == team) return s.PassFrom;
            if (s.Carrier >= 0 && s.Carrier < s.Players.Length && s.Players[s.Carrier].Team == team) return s.Carrier;
            var best = -1;
            var bestD = Fix.Zero;
            for (var i = 0; i < s.Players.Length; i++)
            {
                if (s.Players[i].Team != team) continue;
                var d = (s.Players[i].Pos - s.PuckPos).SqrLength;
                if (best < 0 || d < bestD) { best = i; bestD = d; }
            }
            return best;
        }

        /// <summary>
        /// Shove players (except <paramref name="skipPlayer"/>), and optionally the loose puck and loose Swap rocks,
        /// away from <paramref name="at"/>. Used by the goal explosion and Shockwave.
        /// </summary>
        private void BlastFrom(GameState s, FixVec2 at, Fix radius, Fix push, int skipPlayer, bool markHit, bool shovePuck, bool shoveRocks, bool knockLoose = false)
        {
            if (radius.Raw <= 0 || push.Raw <= 0) return;
            for (var j = 0; j < s.Players.Length; j++)
            {
                if (j == skipPlayer) continue;
                var q = s.Players[j];
                var d = q.Pos - at;
                var dist = d.Length;
                if (dist > radius + RadiusOf(q)) continue;
                FixVec2 n;
                if (dist.Raw > 0) n = d / dist;
                else
                {
                    var bits = s.NextRandom();
                    n = new FixVec2((bits & 1) == 0 ? Fix.One : -Fix.One, (bits & 2) == 0 ? Fix.One : -Fix.One).Normalized;
                }
                var falloff = Fix.One - Fix.Half * Fix.Clamp01(dist / radius);
                q.Vel += n * (push * falloff);
                if (markHit) Hit(q, -n, Fix.Half);
                if (knockLoose)
                {
                    // Shockwave: a carrier it reaches loses the puck (it pops out the way they were blown), and
                    // anyone carrying a teammate (Friendly Fire) drops them
                    if (s.Carrier == j) KnockLoose(s, j, n, -1);
                    DropMate(s, j);
                    q.HeldBy = -1; q.HeldTime = Fix.Zero;
                }
            }
            if (shovePuck && s.Carrier < 0)
            {
                var d = s.PuckPos - at;
                var dist = d.Length;
                if (dist <= radius + T.PuckRadius)
                {
                    FixVec2 n;
                    if (dist.Raw > 0) n = d / dist;
                    else
                    {
                        var bits = s.NextRandom();
                        n = new FixVec2((bits & 1) == 0 ? Fix.One : -Fix.One, (bits & 2) == 0 ? Fix.One : -Fix.One).Normalized;
                    }
                    var falloff = Fix.One - Fix.Half * Fix.Clamp01(dist / radius);
                    s.PuckVel += n * (push * falloff);
                }
            }
            if (!shoveRocks) return;
            for (var r = 0; r < s.Rocks.Length; r++)
            {
                var rk = s.Rocks[r];
                if (rk.Carrier >= 0) continue; // carried rocks ride with their player
                var d = rk.Pos - at;
                var dist = d.Length;
                if (dist > radius + T.RockRadius) continue;
                FixVec2 n;
                if (dist.Raw > 0) n = d / dist;
                else
                {
                    var bits = s.NextRandom();
                    n = new FixVec2((bits & 1) == 0 ? Fix.One : -Fix.One, (bits & 2) == 0 ? Fix.One : -Fix.One).Normalized;
                }
                var falloff = Fix.One - Fix.Half * Fix.Clamp01(dist / radius);
                rk.Vel += n * (push * falloff);
                s.Rocks[r] = rk;
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
                    if (a.HeldBy == j || b.HeldBy == i) continue; // Friendly Fire: carrying them
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
            if (a.Team == b.Team && (a.Stats.FriendlyFire > 0 || a.TossTime > Fix.Zero))
            {
                // Friendly Fire: checking a teammate with your wedge picks them up (puck and all) to throw; they're never dazed by
                // it and keep the puck. A thrown teammate landing on a teammate is just a bump, too.
                if (CanGrab(s, from, to) && GrabHeld(a) && OnWedge(a, b.Pos, RadiusOf(b) + T.MateGrabReach)) GrabMate(s, from, to);
                Hit(b, -n, Fix.Ratio(1, 2));
                Hit(a, n, Fix.Ratio(2, 5));
                return;
            }
            // a real check frees a carried teammate, and makes a carrier drop the one they hold
            s.Players[Credit(s, from)].Bumps++;
            b.HeldBy = -1; b.HeldTime = Fix.Zero;
            DropMate(s, to);
            // no daze from a check (only the Puck Gun stuns): the hit lag above is the pause
            Hit(b, -n, power);
            Hit(a, n, power * Fix.Half);
            if (s.Carrier == to)
            {
                // checking the carrier knocks the puck loose: it pops out the way the check was going, and nobody
                // owns it until someone picks it up (the checker has to wait a moment, so it doesn't just change hands)
                KnockLoose(s, to, n, from);
            }
        }

        // ------------------------------------------------------------------ Friendly Fire: carrying teammates

        /// <summary>Index of the teammate this player is carrying, or -1.</summary>
        private static int MateHeldBy(GameState s, int holder)
        {
            for (var j = 0; j < s.Players.Length; j++) if (s.Players[j].HeldBy == holder) return j;
            return -1;
        }

        /// <summary>Friendly Fire only picks a teammate up while charge/brake is held (this tick's buttons), so it's never by accident.</summary>
        private static bool GrabHeld(PlayerState a) { return (a.PrevButtons & PlayerInput.ChargeBrake) != 0; }

        /// <summary>Whether <paramref name="from"/> may pick up their teammate <paramref name="to"/>: hands free, nobody already carried or flying.</summary>
        private bool CanGrab(GameState s, int from, int to)
        {
            PlayerState a = s.Players[from], b = s.Players[to];
            if (s.Phase != Phase.Play || a.Team != b.Team || a.Stats.FriendlyFire <= 0) return false;
            if (a.HeldBy >= 0 || b.HeldBy >= 0 || a.TossTime > Fix.Zero || b.TossTime > Fix.Zero) return false;
            if (s.Carrier == from || RockCarriedBy(s, from) >= 0 || RockCarriedBy(s, to) >= 0) return false;
            return MateHeldBy(s, from) < 0 && MateHeldBy(s, to) < 0;
        }

        /// <summary>
        /// Friendly Fire: a teammate who touches your wedge is picked up, the same way the puck is. Of every pair, the
        /// closest goes first; an exact tie (two with the card touching each other) is settled by the match's random numbers,
        /// never by player order.
        /// </summary>
        private void GrabTouching(GameState s)
        {
            if (s.Phase != Phase.Play) return;
            var any = false;
            foreach (var p in s.Players) if (p.Stats.FriendlyFire > 0) { any = true; break; }
            if (!any) return;
            while (true)
            {
                int bestFrom = -1, bestTo = -1, ties = 0;
                var bestD = Fix.Zero;
                for (var i = 0; i < s.Players.Length; i++)
                {
                    var a = s.Players[i];
                    if (a.Stats.FriendlyFire <= 0 || a.NoPickup > Fix.Zero || a.HitLag > Fix.Zero || !GrabHeld(a)) continue;
                    for (var j = 0; j < s.Players.Length; j++)
                    {
                        if (j == i || s.Players[j].Team != a.Team) continue;
                        var d2 = (s.Players[j].Pos - a.Pos).SqrLength;
                        if (!OnWedge(a, s.Players[j].Pos, RadiusOf(s.Players[j]) + T.MateGrabReach) || !CanGrab(s, i, j)) continue;
                        if (bestFrom < 0 || d2 < bestD) { bestFrom = i; bestTo = j; bestD = d2; ties = 1; }
                        else if (d2 == bestD && s.NextRandom() % (uint)(++ties) == 0) { bestFrom = i; bestTo = j; }
                    }
                }
                if (bestFrom < 0) return;
                GrabMate(s, bestFrom, bestTo);
            }
        }

        private void GrabMate(GameState s, int from, int to)
        {
            PlayerState a = s.Players[from], b = s.Players[to];
            b.HeldBy = from;
            b.HeldTime = Fix.Zero;
            b.DashTime = Fix.Zero;
            b.Pos = MateSpot(a, b);
            b.Vel = a.Vel;
        }

        /// <summary>Where a carried teammate rides: just in front of their carrier, touching.</summary>
        private FixVec2 MateSpot(PlayerState holder, PlayerState mate)
        {
            return holder.Pos + holder.Facing * (RadiusOf(holder) + RadiusOf(mate) + Fix.Ratio(1, 50));
        }

        /// <summary>Carried teammates go where their carrier's arms are, facing the same way (they stop at the boards).</summary>
        private void CarryMates(GameState s)
        {
            for (var j = 0; j < s.Players.Length; j++)
            {
                var m = s.Players[j];
                if (m.HeldBy < 0) continue;
                if (m.HeldBy >= s.Players.Length || m.HeldBy == j) { m.HeldBy = -1; continue; }
                var h = s.Players[m.HeldBy];
                m.Pos = MateSpot(h, m);
                m.Vel = h.Vel;
                m.Facing = h.Facing; // facing out, the way they'll be thrown (and with their puck clear of the carrier)
                CollideWalls(ref m.Pos, ref m.Vel, RadiusOf(m), T.WallBounce);
            }
        }

        /// <summary><paramref name="holder"/> lets go of the teammate they carry (if any): they slide off with the carrier's speed.</summary>
        private void DropMate(GameState s, int holder)
        {
            var mate = MateHeldBy(s, holder);
            if (mate < 0) return;
            var m = s.Players[mate];
            m.HeldBy = -1;
            m.HeldTime = Fix.Zero;
        }

        /// <summary>Throw a carried teammate the way the player faces, like a shot (quick or charged) but slower. They keep the puck if they have it.</summary>
        private void ThrowMate(GameState s, int i, int mate, bool released)
        {
            if (mate < 0 || mate >= s.Players.Length) return;
            var p = s.Players[i];
            var m = s.Players[mate];
            if (m.HeldBy != i) return;
            var charged = p.Charge <= Fix.One
                ? T.ChargedShotMin + (T.ChargedShotMax - T.ChargedShotMin) * p.Charge
                : T.ChargedShotMax * p.Charge;
            var quick = QuickShotSpeed(p);
            var speed = (released ? charged : Fix.Max(quick, p.Charge > Fix.Zero ? charged : Fix.Zero)) * p.Stats.ShotPower;
            m.HeldBy = -1;
            m.HeldTime = Fix.Zero;
            m.Pos = MateSpot(p, m);
            CollideWalls(ref m.Pos, ref m.Vel, RadiusOf(m), T.WallBounce);
            m.Vel = p.Facing * speed + p.Vel * Fix.Half;
            m.Facing = p.Facing;
            m.TossTime = T.TossTime;
            m.DashTime = T.TossTime; // a thrown teammate checks whoever they hit
            SpeedUp(m, T.MateBoost, T.MateBoostTime); // and skates faster for a while after
            p.ShotsFired++;
            p.ShotStrength = Fix.Clamp01(speed / T.ChargedShotMax);
            p.Charge = Fix.Zero;
            p.NoPickup = T.ShotPickupDelay;
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
            s.PuckPos = CarrySpot(p);
            s.PuckVel = p.Vel;
        }

        // ------------------------------------------------------------------ card mechanics

        /// <summary>Puck Gun: fire a shot from in front of the player.</summary>
        private void FireGun(GameState s, int i)
        {
            var p = s.Players[i];
            // a bot (or a person's body the bot is running) waits longer between shots
            p.GunCooldown = T.GunCooldown + (p.Bot || DriverOf(s, i) < 0 ? T.GunBotExtra : Fix.Zero);
            var start = p.Pos + p.Facing * (RadiusOf(p) + T.ShotRadius + Fix.Ratio(1, 20));
            if (!PuckFits(p.Pos, start)) start = p.Pos; // against a wall: it starts from the middle and hits the wall at once
            var shots = new Shot[s.Shots.Length + 1];
            Array.Copy(s.Shots, shots, s.Shots.Length);
            shots[shots.Length - 1] = new Shot { Pos = start, Vel = p.Facing * T.GunSpeed + p.Vel * Fix.Half, Owner = i, Life = T.GunLife };
            s.Shots = shots;
        }

        // ------------------------------------------------------------------ Slot Machine

        /// <summary>Slot Machine: pull. What it lands on is settled now (by the match's random numbers): each prize 1 in SlotOdds.</summary>
        private void SpinSlots(GameState s, PlayerState p)
        {
            var odds = Math.Max(4, T.SlotOdds);
            var roll = (int)(s.NextRandom() % (uint)odds);
            p.SlotWin = roll < 4 ? roll + 1 : 0;
            // the three symbols, all prizes (1-4): a win shows three of its prize; a loss never three alike
            int a, b, c;
            if (p.SlotWin > 0) a = b = c = p.SlotWin;
            else
            {
                a = 1 + (int)(s.NextRandom() % 4u);
                b = 1 + (int)(s.NextRandom() % 4u);
                c = 1 + (int)(s.NextRandom() % 4u);
                if (a == b && b == c) c = c % 4 + 1;
            }
            p.SlotReels = a | (b << 4) | (c << 8);
            p.SlotSpin = T.SlotSpinTime;
            p.SlotSpins++;
        }

        /// <summary>Slot Machine: the spin landing (a prize starts), a prize running out, the wait after a loss.</summary>
        private void SlotTimers(GameState s, PlayerState p, Fix dt)
        {
            p.SlotWait = Fix.Max(Fix.Zero, p.SlotWait - dt);
            if (p.SlotPrizeTime > Fix.Zero)
            {
                p.SlotPrizeTime -= dt;
                if (p.SlotPrizeTime <= Fix.Zero)
                {
                    p.SlotPrizeTime = Fix.Zero;
                    var body = p.SlotPrize == CardLibrary.SlotGiant || p.SlotPrize == CardLibrary.SlotStrong;
                    p.SlotPrize = 0;
                    if (body && Cards != null) Cards.Recompute(s);
                }
            }
            if (p.SlotSpin <= Fix.Zero) return;
            p.SlotSpin -= dt;
            if (p.SlotSpin > Fix.Zero) return;
            p.SlotSpin = Fix.Zero;
            if (p.SlotWin == 0) { p.SlotWait = T.SlotWait; return; }
            p.SlotPrize = p.SlotWin;
            switch (p.SlotWin)
            {
                case CardLibrary.SlotSpeed: p.SlotPrizeTime = T.SlotSpeedTime; break;
                case CardLibrary.SlotGiant: p.SlotPrizeTime = T.SlotGiantTime; break;
                case CardLibrary.SlotInvisible: p.SlotPrizeTime = T.SlotInvisibleTime; p.InvisibleTime = Fix.Max(p.InvisibleTime, T.SlotInvisibleTime); break;
                default: p.SlotPrizeTime = T.SlotStrongTime; break;
            }
            if ((p.SlotWin == CardLibrary.SlotGiant || p.SlotWin == CardLibrary.SlotStrong) && Cards != null)
            {
                Cards.GiantBy = T.SlotGiant;
                Cards.StrongCards = T.SlotStrongCards;
                Cards.Recompute(s);
            }
        }

        // ------------------------------------------------------------------ Portal Gun

        /// <summary>Portal Gun: a shot from the player's middle, the way they face, in the colour that's next.</summary>
        private void FirePortal(GameState s, int i)
        {
            var p = s.Players[i];
            p.PortalCooldown = T.PortalCooldown;
            var shots = new Shot[s.PortalShots.Length + 1];
            Array.Copy(s.PortalShots, shots, s.PortalShots.Length);
            shots[shots.Length - 1] = new Shot { Pos = p.Pos, Vel = p.Facing * T.PortalShotSpeed, Owner = i, Life = T.PortalShotLife, Kind = p.PortalNext & 1 };
            s.PortalShots = shots;
            p.PortalNext = 1 - (p.PortalNext & 1);
        }

        /// <summary>
        /// Portal shots fly straight through players, the puck and everything else, until they reach a wall: a portal
        /// goes up there. One that flies into a net (or off the ice) fizzles.
        /// </summary>
        private void MovePortalShots(GameState s, Fix sub)
        {
            var keep = new System.Collections.Generic.List<Shot>(s.PortalShots.Length);
            foreach (var sh0 in s.PortalShots)
            {
                var sh = sh0;
                var from = sh.Pos;
                var to = sh.Pos + sh.Vel * sub;
                sh.Life -= sub;
                var gone = sh.Life <= Fix.Zero;
                if (!gone)
                {
                    // the wall first (the back of a net from outside is a wall like any other); a shot that's got into a
                    // net's box (through its mouth) fizzles
                    var hit = FirstWallHit(from, to);
                    var at = hit < Fix.One ? from + (to - from) * hit : to;
                    foreach (var g in Map.Goals) if (g.Contains(from) || g.Contains(at)) gone = true;
                    if (!gone && hit < Fix.One) { PlacePortal(s, sh, at, from); gone = true; }
                    else if (!Map.Inside(to)) gone = true;
                }
                sh.Pos = to;
                if (!gone) keep.Add(sh);
            }
            s.PortalShots = keep.ToArray();
        }

        /// <summary>
        /// A portal shot reached the wall at <paramref name="hit"/> (coming from <paramref name="from"/>): the portal is centred
        /// there on the wall line, slid along so all of it is on the wall (a wall too short for one: no portal). It replaces
        /// the shooter's portal of the same colour.
        /// </summary>
        private void PlacePortal(GameState s, Shot sh, FixVec2 hit, FixVec2 from)
        {
            // the wall line it hit, and how far along it
            int bw = -1;
            var bu = Fix.Zero;
            var bd = Fix.Zero;
            var bdir = FixVec2.Right;
            var bc = hit;
            Fix bsegAt = Fix.Zero, bsegLen = Fix.Zero;
            for (var w = 0; w < Map.WallLines.Count; w++)
            {
                var pts = Map.WallLines[w];
                var n = Map.WallClosed[w] ? pts.Length : pts.Length - 1;
                var along = Fix.Zero;
                for (var k = 0; k < n; k++)
                {
                    var a = pts[k];
                    var e = pts[(k + 1) % pts.Length] - a;
                    var len = e.Length;
                    if (len.Raw == 0) continue;
                    var t = Fix.Clamp(FixVec2.Dot(hit - a, e) / (len * len), Fix.Zero, Fix.One);
                    var c = a + e * t;
                    var d = (hit - c).SqrLength;
                    if (bw < 0 || d < bd) { bw = w; bd = d; bu = along + len * t; bdir = e / len; bc = c; bsegAt = along; bsegLen = len; }
                    along += len;
                }
            }
            if (bw < 0) return;
            var total = Map.LineLength(bw);
            var half = T.PortalLength * Fix.Half;
            if (total < T.PortalLength) return; // too short for a portal
            var nearNet = NetNear(bc);
            if (nearNet != null)
            {
                // on a net: only the outside of its back wall, all of the portal on it (never its sides or posts)
                var front = NetFront(nearNet);
                var box = new FixVec2((nearNet.Min.X + nearNet.Max.X) / 2, (nearNet.Min.Y + nearNet.Max.Y) / 2);
                if (Fix.Abs(FixVec2.Dot(bdir, front)) > Fix.Ratio(3, 10)) return;          // a side, not the back
                if (FixVec2.Dot(bc - box, front) >= Fix.Zero) return;                      // (in front of the net)
                if (FixVec2.Dot(from - bc, front) >= Fix.Zero) return;                     // from inside the net
                // clear of the rounded posts at its ends (a hit near one shifts it along, so it never bends round a post)
                if (bsegLen < T.PortalLength + WallHalf * 2 - Fix.Ratio(1, 10)) return;
                bu = ClampAlong(bu, bsegAt + half + WallHalf, bsegAt + bsegLen - half - WallHalf);
            }
            else
            {
                if (!Map.WallClosed[bw])
                {
                    if (total < T.PortalLength + WallHalf * 2 - Fix.Ratio(1, 10)) return;
                    bu = ClampAlong(bu, half + WallHalf, total - half - WallHalf); // clear of the rounded ends
                }
                // never round a corner onto a net
                for (var k = 0; k <= 12; k++)
                {
                    FixVec2 along;
                    if (NetNear(Map.PointAlong(bw, bu - half + T.PortalLength * k / 12, out along)) != null) return;
                }
            }
            // which side of the line it came from: the portal faces that way (the same side all along the line)
            var side = FixVec2.Dot(from - bc, new FixVec2(-bdir.Y, bdir.X)) >= Fix.Zero ? Fix.One : -Fix.One;
            FixVec2 dir;
            var centre = Map.PointAlong(bw, bu, out dir);
            var normal = new FixVec2(-dir.Y, dir.X) * side;
            // on (or overlapping) the other team's portal: it's gone, and this one goes up in its place
            var team = sh.Owner >= 0 && sh.Owner < s.Players.Length ? s.Players[sh.Owner].Team : -1;
            for (var k = 0; k < s.Portals.Length; k++)
            {
                var po = s.Portals[k];
                var theirs = po.Owner >= 0 && po.Owner < s.Players.Length ? s.Players[po.Owner].Team : -2;
                if (theirs == team || (po.Pos - centre).SqrLength >= T.PortalLength * T.PortalLength || FixVec2.Dot(po.Normal, normal) <= Fix.Zero) continue;
                centre = po.Pos; normal = po.Normal; bw = po.Wall; bu = po.At;
                var left = new System.Collections.Generic.List<Portal>(s.Portals);
                left.RemoveAt(k);
                s.Portals = left.ToArray();
                break;
            }
            // not on top of the shooter's other portal
            foreach (var po in s.Portals)
                if (po.Owner == sh.Owner && po.Colour != sh.Kind && (po.Pos - centre).SqrLength < T.PortalLength * T.PortalLength
                    && FixVec2.Dot(po.Normal, normal) > Fix.Zero) return;
            // a team never has more than its two up: this one, and the shooter's other colour. Any teammate's go
            // (a Portal Gun that changed hands leaves no strays)
            var list = new System.Collections.Generic.List<Portal>(s.Portals.Length + 1);
            foreach (var po in s.Portals)
            {
                var mine = po.Owner == sh.Owner;
                var mate = !mine && po.Owner >= 0 && po.Owner < s.Players.Length && s.Players[po.Owner].Team == team;
                if (mate || (mine && po.Colour == sh.Kind)) continue;
                list.Add(po);
            }
            list.Add(new Portal { Pos = centre, Normal = normal, Owner = sh.Owner, Colour = sh.Kind, Wall = bw, At = bu, Id = ++s.PortalsMade });
            s.Portals = list.ToArray();
        }

        /// <summary>
        /// The safety net for things in a portal (where some wall doesn't count): moving from <paramref name="from"/> to
        /// <paramref name="to"/> took it off the ice, or into a net it wasn't in.
        /// </summary>
        private bool Escaped(FixVec2 from, FixVec2 to)
        {
            if (!Map.Inside(to)) return true;
            foreach (var g in Map.Goals) if (g.Contains(to) && !g.Contains(from)) return true;
            return false;
        }

        /// <summary>The same player's other portal (the other colour), or -1 if it isn't up.</summary>
        private static int Partner(GameState s, int a)
        {
            for (var k = 0; k < s.Portals.Length; k++)
                if (k != a && s.Portals[k].Owner == s.Portals[a].Owner && s.Portals[k].Colour != s.Portals[a].Colour) return k;
            return -1;
        }

        /// <summary>
        /// Going through a portal, a bit at a time: something (a circle of radius <paramref name="r"/>) touching a portal's
        /// face while moving into it sinks into the wall there (the wall lets it in), and the part that's in comes out of the
        /// other portal (the drawing side shows both halves). Once its middle passes the face it's moved across: the same
        /// distance out of the other portal, the same speed at the same angle (turned the way the portals are turned), never
        /// slower than PortalExitSpeed. <paramref name="via"/> is the Id of the portal it's in (0 = none); it's kept
        /// until it's clear of the wall again. Returns the index of the portal it's in (walls there don't stop it), or -1.
        /// </summary>
        private int PortalPass(GameState s, ref FixVec2 pos, ref FixVec2 vel, Fix r, ref int via, out bool jumped)
        {
            FixVec2 nIn, nOut;
            return PortalPass(s, ref pos, ref vel, r, ref via, out jumped, out nIn, out nOut);
        }

        /// <summary>Turn <paramref name="v"/> the way going in at a portal facing <paramref name="nIn"/> and out of one facing <paramref name="nOut"/> turns things.</summary>
        private static FixVec2 PortalTurn(FixVec2 v, FixVec2 nIn, FixVec2 nOut)
        {
            var tIn = new FixVec2(-nIn.Y, nIn.X);
            var tOut = new FixVec2(-nOut.Y, nOut.X);
            return -nOut * FixVec2.Dot(v, nIn) - tOut * FixVec2.Dot(v, tIn);
        }

        private int PortalPass(GameState s, ref FixVec2 pos, ref FixVec2 vel, Fix r, ref int via, out bool jumped, out FixVec2 nIn, out FixVec2 nOut)
        {
            jumped = false;
            nIn = FixVec2.Zero; nOut = FixVec2.Zero;
            if (s.Portals.Length < 2) { via = 0; return -1; }
            var half = T.PortalLength * Fix.Half;
            var a = -1;
            if (via != 0) for (var k = 0; k < s.Portals.Length; k++) if (s.Portals[k].Id == via) { a = k; break; }
            if (a < 0 || Partner(s, a) < 0)
            {
                // not in one yet: touching a portal's face, within its length, and moving into it?
                via = 0; a = -1;
                // the bit of wall it's touching (measured from the wall itself, so the ends of a portal on a curve count)
                var reach = r + WallHalf + Fix.Ratio(1, 10);
                var touching = false;
                var best = Fix.Zero;
                var c = pos;
                foreach (var seg in Map.Segments)
                {
                    if (seg.FarFrom(pos, reach)) continue;
                    var q = Closest(seg, pos);
                    var dd = (pos - q).SqrLength;
                    if (dd >= reach * reach) continue;
                    if (!touching || dd < best) { touching = true; best = dd; c = q; }
                }
                if (!touching) return -1;
                for (var k = 0; k < s.Portals.Length && a < 0; k++)
                {
                    var P0 = s.Portals[k];
                    if (Partner(s, k) < 0) continue;
                    if ((c - P0.Pos).SqrLength > half * half) continue;                         // not this portal's stretch
                    if (FixVec2.Dot(pos - c, P0.Normal) <= Fix.Zero) continue;                  // the far side of that wall
                    if (FixVec2.Dot(vel, P0.Normal) >= Fix.Zero) continue;                      // not moving into it
                    a = k; via = P0.Id;
                }
                if (a < 0) return -1;
            }
            var b = Partner(s, a);
            var P = s.Portals[a];
            var Q = s.Portals[b];
            var fP = P.Pos + P.Normal * WallHalf;
            var fQ = Q.Pos + Q.Normal * WallHalf;
            var tP = new FixVec2(-P.Normal.Y, P.Normal.X);
            var tQ = new FixVec2(-Q.Normal.Y, Q.Normal.X);
            var depth = FixVec2.Dot(pos - fP, P.Normal);
            var across = FixVec2.Dot(pos - fP, tP);
            // clear of it (back out on its own side, or off past its end): an ordinary wall again
            if (depth > r + Fix.Ratio(3, 10) || Fix.Abs(across) > half + r) { via = 0; return -1; }
            if (depth >= Fix.Zero) return a;
            // its middle is through: over to the other portal, as far out of it as it was in, at the same place along
            // it (measured along that wall, so it follows a curve, and never past the portal's end)
            var vn = FixVec2.Dot(vel, P.Normal);
            var vt = FixVec2.Dot(vel, tP);
            across = Fix.Clamp(across, -half, half);
            FixVec2 dirQ;
            Map.PointAlong(Q.Wall, Q.At, out dirQ);
            var sgn = FixVec2.Dot(dirQ, tQ) >= Fix.Zero ? Fix.One : -Fix.One;
            FixVec2 dirAt;
            var onWall = Map.PointAlong(Q.Wall, Q.At - across * sgn, out dirAt);
            var nAt = new FixVec2(-dirAt.Y, dirAt.X);
            if (FixVec2.Dot(nAt, Q.Normal) < Fix.Zero) nAt = -nAt;
            pos = onWall + nAt * (WallHalf - depth);
            vel = Q.Normal * Fix.Max(-vn, T.PortalExitSpeed) - tQ * vt;
            via = Q.Id;
            P.Uses++; Q.Uses++;
            s.Portals[a] = P; s.Portals[b] = Q;
            jumped = true;
            nIn = P.Normal; nOut = Q.Normal;
            return b;
        }

        /// <summary>Shots fly straight; the first player they touch (not the shooter) is knocked back like a check (lighter players further) and drops the puck; walls and pylons stop them.</summary>
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
                foreach (var w in Map.Segments) if (!gone && !w.FarFrom(sh.Pos, sr) && (sh.Pos - Closest(w, sh.Pos)).SqrLength < sr * sr) gone = true;
                foreach (var py in s.Pylons)
                {
                    var r = py.Radius + T.ShotRadius;
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
                    Touched(s, sh.Owner);
                    gone = true;
                }
                if (!gone)
                    for (var j = 0; j < s.Players.Length; j++)
                    {
                        if (j == sh.Owner) continue;
                        var q = s.Players[j];
                        var r = RadiusOf(q) + T.ShotRadius;
                        if ((sh.Pos - q.Pos).SqrLength >= r * r) continue;
                        // a shootable check: no daze, a knockback that scales with how heavy they are (and Knockback Taken)
                        if (sh.Owner >= 0 && sh.Owner < s.Players.Length && s.Players[sh.Owner].Team != q.Team) s.Players[Credit(s, sh.Owner)].Bumps++;
                        var light = Fix.One / WeightOf(q) * Fix.Clamp(q.Stats.KnockbackTaken, Fix.Ratio(1, 20), Fix.FromInt(10));
                        q.Vel += sh.Vel.Normalized * (T.DashKnockback * light);
                        q.HitLag = Fix.Max(q.HitLag, T.HitLagMax * Fix.Ratio(3, 5));
                        Hit(q, (sh.Pos - q.Pos).Normalized, Fix.Ratio(3, 5));
                        q.HeldBy = -1; q.HeldTime = Fix.Zero;
                        DropMate(s, j);
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
                CollideWalls(ref py.Pos, ref py.Vel, py.Radius, T.WallBounce);
                KeepPylonOut(ref py);
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
                var min = r + py.Radius;
                if (d.SqrLength >= min * min) continue;
                var dist = d.Length;
                var n = dist.Raw > 0 ? d / dist : FixVec2.Right;
                // a bigger pylon is heavier too
                Fix wp = WeightOf(p), wy = T.PylonWeight * py.Radius / T.PylonRadius, sum = wp + wy;
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
                CollideWalls(ref py.Pos, ref v, py.Radius, T.WallBounce);
                py.Vel = v;
                KeepPylonOut(ref py);
                s.Pylons[k] = py;
            }
        }

        private void CollidePylons(GameState s, ref FixVec2 pos, ref FixVec2 vel, Fix r, Fix bounce)
        {
            foreach (var py in s.Pylons)
            {
                var d = pos - py.Pos;
                var min = r + py.Radius;
                if (d.SqrLength >= min * min) continue;
                var dist = d.Length;
                var n = dist.Raw > 0 ? d / dist : FixVec2.Right;
                pos = py.Pos + n * min;
                var vn = FixVec2.Dot(vel, n);
                if (vn < Fix.Zero) vel -= n * (vn * (Fix.One + bounce));
            }
        }

        /// <summary>
        /// Swap (utility): throw a rock ahead like a quick shot, or swap places with your rock and trade momentum with it.
        /// Players have no cooldown; bots wait <see cref="Tuning.SwapBotCooldown"/> between uses.
        /// </summary>
        private void UseSwap(GameState s, int i)
        {
            var a = s.Players[i];
            if (a.Stats.Swap <= 0) return;
            if (s.Carrier == i) return; // holding the puck: no throw or teleport
            // the bot playing them (a bot, or a person's own player while they steer a bot teammate) waits between uses
            var botRun = a.Bot || DriverOf(s, i) < 0;
            if (botRun && a.SwapCooldown > Fix.Zero) return;
            for (var k = 0; k < s.Rocks.Length; k++)
            {
                if (s.Rocks[k].Owner != i) continue;
                // someone is holding it: no swapping to it
                if (s.Rocks[k].Carrier >= 0) return;
                // swap places and momentum with the rock
                var rk = s.Rocks[k];
                var from = a.Pos;
                var to = rk.Pos;
                // you and the rock trade momentum: you take its, it takes yours
                var rVel = rk.Vel;
                a.Pos = to;
                rk.Vel = a.Vel;
                a.Vel = rVel;
                rk.Pos = from;
                rk.Carrier = -1;
                CollideWalls(ref a.Pos, ref a.Vel, RadiusOf(a), T.WallBounce);
                CollideWalls(ref rk.Pos, ref rk.Vel, T.RockRadius, T.WallBounce);
                s.Rocks[k] = rk;
                a.Swaps++; // clap + particle burst (throw does not count)
                a.SwapFrom = from;
                a.SwapTo = to;
                if (botRun) a.SwapCooldown = T.SwapBotCooldown;
                return;
            }
            // no rock yet: throw one out of the wedge like a quick shot
            var start = a.Pos + a.Facing * (RadiusOf(a) + T.RockRadius + Fix.Ratio(1, 20));
            var vel = a.Facing * T.RockSpeed + a.Vel * Fix.Half;
            var rocks = new Rock[s.Rocks.Length + 1];
            Array.Copy(s.Rocks, rocks, s.Rocks.Length);
            rocks[rocks.Length - 1] = new Rock { Pos = start, Vel = vel, Owner = i, Carrier = -1, Life = T.RockLife };
            s.Rocks = rocks;
            a.NoPickup = Fix.Max(a.NoPickup, T.ShotPickupDelay); // don't scoop your own throw back up
            if (botRun) a.SwapCooldown = T.SwapBotCooldown;
        }

        /// <summary>Shoot a carried Swap rock the same way as the puck (quick or charged).</summary>
        private void ShootRock(GameState s, int i, int rockIndex, bool released)
        {
            if (rockIndex < 0 || rockIndex >= s.Rocks.Length) return;
            var p = s.Players[i];
            var rk = s.Rocks[rockIndex];
            if (rk.Carrier != i) return;
            var charged = p.Charge <= Fix.One
                ? T.ChargedShotMin + (T.ChargedShotMax - T.ChargedShotMin) * p.Charge
                : T.ChargedShotMax * p.Charge;
            var quick = QuickShotSpeed(p);
            var speed = (released ? charged : Fix.Max(quick, p.Charge > Fix.Zero ? charged : Fix.Zero)) * p.Stats.ShotPower;
            var reach = RadiusOf(p) + T.RockRadius + T.CarrySlack;
            if ((rk.Pos - p.Pos).SqrLength > reach * reach) rk.Pos = RockCarrySpot(p);
            rk.Vel = p.Facing * speed + p.Vel * Fix.Half;
            rk.Carrier = -1;
            s.Rocks[rockIndex] = rk;
            p.ShotsFired++;
            p.ShotStrength = Fix.Clamp01(speed / T.ChargedShotMax);
            p.Charge = Fix.Zero;
            p.NoPickup = T.ShotPickupDelay;
        }

        private FixVec2 RockCarrySpot(PlayerState p)
        {
            var reach = RadiusOf(p) + T.RockRadius + Fix.Ratio(1, 50);
            if (RockFits(p.Pos, p.Pos + p.Facing * reach)) return p.Pos + p.Facing * reach;
            Fix lo = Fix.Zero, hi = reach;
            for (var k = 0; k < 12; k++)
            {
                var mid = (lo + hi) / 2;
                if (RockFits(p.Pos, p.Pos + p.Facing * mid)) lo = mid; else hi = mid;
            }
            return p.Pos + p.Facing * lo;
        }

        private bool RockFits(FixVec2 from, FixVec2 at)
        {
            if (FirstWallHit(from, at) < Fix.One) return false;
            var r = T.RockRadius + WallHalf;
            var r2 = r * r;
            foreach (var seg in Map.Segments) if (!seg.FarFrom(at, r) && (at - Closest(seg, at)).SqrLength < r2) return false;
            return true;
        }

        private void MoveRocks(GameState s, Fix sub)
        {
            for (var k = 0; k < s.Rocks.Length; k++)
            {
                var rk = s.Rocks[k];
                if (rk.Carrier >= 0)
                {
                    if (rk.Carrier >= s.Players.Length || s.Carrier == rk.Carrier)
                    {
                        // carrier gone, or they picked up the real puck: drop the rock
                        rk.Carrier = -1;
                        s.Rocks[k] = rk;
                    }
                    else
                    {
                        var c = s.Players[rk.Carrier];
                        rk.Pos = RockCarrySpot(c);
                        rk.Vel = c.Vel;
                        s.Rocks[k] = rk;
                        continue;
                    }
                }
                if (rk.Vel.SqrLength.Raw == 0) { /* still may be picked up below */ }
                else
                {
                    rk.Pos += rk.Vel * sub;
                    rk.Vel -= rk.Vel * Fix.Min(Fix.One, T.RockDrag * sub);
                    if (rk.Vel.SqrLength < Fix.Ratio(1, 400)) rk.Vel = FixVec2.Zero;
                    bool jumpedR;
                    var rockWas = rk.Pos - rk.Vel * sub;
                    var rockWasIn = rk.PortalVia != 0;
                    var inPortal = PortalPass(s, ref rk.Pos, ref rk.Vel, T.RockRadius, ref rk.PortalVia, out jumpedR);
                    CollideWalls(ref rk.Pos, ref rk.Vel, T.RockRadius, T.WallBounce, s, inPortal);
                    if ((inPortal >= 0 || rockWasIn) && !jumpedR && Escaped(rockWas, rk.Pos)) { rk.Pos = rockWas; rk.Vel = FixVec2.Zero; rk.PortalVia = 0; }
                    if (s.Stones.Length > 0) CollideStones(s, ref rk.Pos, ref rk.Vel, T.RockRadius, T.WallBounce);
                    s.Rocks[k] = rk;
                }
            }

            // pick-up: anyone not carrying the puck (or another rock) may take a loose rock into their wedge
            for (var k = 0; k < s.Rocks.Length && s.Phase == Phase.Play; k++)
            {
                var rk = s.Rocks[k];
                if (rk.Carrier >= 0) continue;
                int best = -1, ties = 0;
                var bestD = Fix.Zero;
                for (var i = 0; i < s.Players.Length; i++)
                {
                    if (s.Carrier == i || RockCarriedBy(s, i) >= 0 || MateHeldBy(s, i) >= 0) continue;
                    var p = s.Players[i];
                    var r = RadiusOf(p) + T.RockRadius;
                    var d2 = (rk.Pos - p.Pos).SqrLength;
                    if (p.NoPickup > Fix.Zero || (d2 >= r * r && !(T.WedgePickup && OnWedgeRock(p, rk.Pos)))) continue;
                    if (best < 0 || d2 < bestD) { best = i; bestD = d2; ties = 1; }
                    else if (d2 == bestD && s.NextRandom() % (uint)(++ties) == 0) best = i;
                }
                if (best < 0) continue;
                rk.Carrier = best;
                rk.Pos = RockCarrySpot(s.Players[best]);
                rk.Vel = s.Players[best].Vel;
                s.Rocks[k] = rk;
            }

            // a rock runs out RockLife after it was thrown (held or not): then it's gone and a new one can be thrown
            var gone = 0;
            for (var k = 0; k < s.Rocks.Length; k++)
            {
                var rk = s.Rocks[k];
                rk.Life -= sub;
                s.Rocks[k] = rk;
                if (rk.Life <= Fix.Zero) gone++;
            }
            if (gone > 0)
            {
                var keep = new Rock[s.Rocks.Length - gone];
                var j = 0;
                foreach (var rk in s.Rocks) if (rk.Life > Fix.Zero) keep[j++] = rk;
                s.Rocks = keep;
            }
        }

        /// <summary>Same as <see cref="OnWedge"/> but sized for a Swap rock.</summary>
        private bool OnWedgeRock(PlayerState p, FixVec2 rock)
        {
            var R = RadiusOf(p);
            var pr = T.RockRadius;
            var d = rock - p.Pos;
            var x = FixVec2.Dot(d, p.Facing);
            var tipX = R * T.WedgeTip;
            var backX = R * T.WedgeBack;
            if (x < backX - pr || x > tipX + pr) return false;
            var y = Fix.Abs(FixVec2.Cross(p.Facing, d));
            var halfW = R * T.WedgeHalfWidth;
            if (y > halfW + pr) return false;
            var span = tipX - backX;
            if (x >= backX && y * span <= halfW * (tipX - x)) return true;
            var q = new FixVec2(x, y);
            var tip = new FixVec2(tipX, Fix.Zero);
            var corner = new FixVec2(backX, halfW);
            var e = corner - tip;
            var t = Fix.Clamp(FixVec2.Dot(q - tip, e) / e.SqrLength, Fix.Zero, Fix.One);
            if ((q - (tip + e * t)).SqrLength <= pr * pr) return true;
            var onBack = new FixVec2(backX, Fix.Min(y, halfW));
            return (q - onBack).SqrLength <= pr * pr;
        }

        /// <summary>Players knock loose Swap rocks around (lighter and glidier than pylons).</summary>
        private void PushRocks(GameState s, PlayerState p)
        {
            var r = RadiusOf(p);
            for (var k = 0; k < s.Rocks.Length; k++)
            {
                var rk = s.Rocks[k];
                if (rk.Carrier >= 0) continue; // carried rocks sit in the wedge
                var d = p.Pos - rk.Pos;
                var min = r + T.RockRadius;
                if (d.SqrLength >= min * min) continue;
                var dist = d.Length;
                var n = dist.Raw > 0 ? d / dist : FixVec2.Right;
                Fix wp = WeightOf(p), wy = T.RockWeight, sum = wp + wy;
                var overlap = min - dist;
                p.Pos += n * (overlap * wy / sum);
                rk.Pos -= n * (overlap * wp / sum);
                var rv = FixVec2.Dot(p.Vel - rk.Vel, n);
                if (rv < Fix.Zero)
                {
                    var impulse = -(Fix.One + T.WallBounce) * rv * wp * wy / sum;
                    p.Vel += n * (impulse / wp);
                    rk.Vel -= n * (impulse / wy);
                }
                var v = rk.Vel;
                CollideWalls(ref rk.Pos, ref v, T.RockRadius, T.WallBounce);
                rk.Vel = v;
                s.Rocks[k] = rk;
            }
        }

        /// <summary>
        /// Loose rock ↔ puck. Both bounce by weight when the puck is free; a carried puck is knocked loose and
        /// shoved like a Puck Gun hit. Carried rocks are skipped (they sit in a wedge).
        /// </summary>
        private void BouncePuckAndRocks(GameState s)
        {
            for (var k = 0; k < s.Rocks.Length; k++)
            {
                var rk = s.Rocks[k];
                if (rk.Carrier >= 0) continue;
                var min = T.PuckRadius + T.RockRadius;
                var d = s.PuckPos - rk.Pos;
                if (d.SqrLength >= min * min) continue;
                var dist = d.Length;
                var n = dist.Raw > 0 ? d / dist : FixVec2.Right;

                if (s.Carrier >= 0)
                {
                    // like Puck Gun: knock it loose and shove the puck along the rock's way
                    var dir = rk.Vel.SqrLength.Raw > 0 ? rk.Vel.Normalized : n;
                    KnockLoose(s, s.Carrier, dir, -1);
                    s.PuckVel += dir * T.GunPuckPush;
                    s.PassFrom = -1;
                    var into = FixVec2.Dot(rk.Vel, n);
                    if (into > Fix.Zero) rk.Vel -= n * (into * (Fix.One + T.PuckWallBounce));
                    rk.Pos = s.PuckPos - n * min;
                    var v = rk.Vel;
                    CollideWalls(ref rk.Pos, ref v, T.RockRadius, T.WallBounce);
                    rk.Vel = v;
                }
                else
                {
                    // both loose: share the bounce (puck weighs 1, rock is lighter)
                    Fix wp = Fix.One, wy = T.RockWeight, sum = wp + wy;
                    var overlap = min - dist;
                    s.PuckPos += n * (overlap * wy / sum);
                    rk.Pos -= n * (overlap * wp / sum);
                    var rv = FixVec2.Dot(s.PuckVel - rk.Vel, n);
                    if (rv < Fix.Zero)
                    {
                        var impulse = -(Fix.One + T.PuckWallBounce) * rv * wp * wy / sum;
                        s.PuckVel += n * (impulse / wp);
                        rk.Vel -= n * (impulse / wy);
                    }
                    CollideWalls(ref s.PuckPos, ref s.PuckVel, T.PuckRadius, T.PuckWallBounce);
                    var v = rk.Vel;
                    CollideWalls(ref rk.Pos, ref v, T.RockRadius, T.WallBounce);
                    rk.Vel = v;
                }
                s.Rocks[k] = rk;
            }
        }

        /// <summary>Push a circle out of any loose rock it overlaps (used when placing a carried puck).</summary>
        private void CollideRocks(GameState s, ref FixVec2 pos, ref FixVec2 vel, Fix r, Fix bounce)
        {
            foreach (var rk in s.Rocks)
            {
                if (rk.Carrier >= 0) continue;
                var d = pos - rk.Pos;
                var min = r + T.RockRadius;
                if (d.SqrLength >= min * min) continue;
                var dist = d.Length;
                var n = dist.Raw > 0 ? d / dist : FixVec2.Right;
                pos = rk.Pos + n * min;
                var vn = FixVec2.Dot(vel, n);
                if (vn < Fix.Zero) vel -= n * (vn * (Fix.One + bounce));
            }
        }

        /// <summary>
        /// Shockwave (utility): shove every other player, the loose puck, and loose Swap rocks away from you.
        /// Ready on Triangle / Y when the card is held.
        /// </summary>
        private void FireShockwave(GameState s, int i)
        {
            var a = s.Players[i];
            var copies = Math.Min(3, a.Stats.Shockwave);
            if (copies <= 0) return;
            var radius = ShockRadius(a.ShockHold);
            var push = T.DashKnockback * T.ShockPush * (Fix.One + Fix.Ratio(1, 10) * (copies - 1));
            // held longer: pushes harder, up to ShockFullPush times as hard at a full charge
            var charged = T.ShockChargeTime > Fix.Zero ? Fix.Clamp01(a.ShockHold / T.ShockChargeTime) : Fix.One;
            push *= Fix.One + (T.ShockFullPush - Fix.One) * charged;
            BlastFrom(s, a.Pos, radius, push, i, true, true, true, true);
            a.Shocks++;
            a.ShockAt = a.Pos;
            a.ShockRadius = radius;
        }

        /// <summary>How big a shockwave is after holding the button this long (ShockMin to ShockMax, over ShockChargeTime).</summary>
        public Fix ShockRadius(Fix held)
        {
            var t = T.ShockChargeTime > Fix.Zero ? Fix.Clamp01(held / T.ShockChargeTime) : Fix.One;
            return T.ShockMin + (T.ShockMax - T.ShockMin) * t;
        }

        /// <summary>A stronger or longer slow replaces a weaker one.</summary>
        private static void SlowDown(PlayerState p, Fix slow, Fix time)
        {
            if (p.SlowTime > Fix.Zero && p.Slow >= slow && p.SlowTime >= time) return;
            p.Slow = Fix.Max(p.Slow * (p.SlowTime > Fix.Zero ? Fix.One : Fix.Zero), slow);
            p.SlowTime = Fix.Max(p.SlowTime, time);
        }

        /// <summary>Sped up by <paramref name="fast"/> (0.8 = 80%) for at least <paramref name="time"/> (the bigger boost wins).</summary>
        private static void SpeedUp(PlayerState p, Fix fast, Fix time)
        {
            if (p.FastTime > Fix.Zero && p.Fast >= fast && p.FastTime >= time) return;
            p.Fast = Fix.Max(p.FastTime > Fix.Zero ? p.Fast : Fix.Zero, fast);
            p.FastTime = Fix.Max(p.FastTime, time);
        }

        /// <summary>Team Play: a clean pass from <paramref name="passer"/> reached teammate <paramref name="to"/>.</summary>
        private void Passed(GameState s, int passer, int to)
        {
            var p = s.Players[passer];
            if (p.Stats.PassSpeed > 0)
            {
                // both of you skate faster for a bit
                SpeedUp(p, T.PassSpeedBoost, T.PassSpeedTime); p.Refreshes++;
                var q = s.Players[to];
                SpeedUp(q, T.PassSpeedBoost, T.PassSpeedTime); q.Refreshes++;
            }
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

        /// <summary>
        /// As below, but going through portal <paramref name="portal"/> (an index in <paramref name="s"/>'s portals, -1 for
        /// none): the wall it covers doesn't count.
        /// </summary>
        private void CollideWalls(ref FixVec2 pos, ref FixVec2 vel, Fix r, Fix bounce, GameState s, int portal)
        {
            if (portal < 0 || s == null || portal >= s.Portals.Length) { CollideWalls(ref pos, ref vel, r, bounce); return; }
            var at = s.Portals[portal].Pos;
            var along = new FixVec2(-s.Portals[portal].Normal.Y, s.Portals[portal].Normal.X);
            var skip = T.PortalLength * Fix.Half;
            var rr = r + WallHalf;
            var segs = Map.Segments;
            for (var pass = 0; pass < 2; pass++)
                for (var k = 0; k < segs.Count; k++)
                {
                    if (segs[k].FarFrom(pos, rr)) continue;
                    var c = Closest(segs[k], pos);
                    var d = pos - c;
                    if (d.SqrLength >= rr * rr) continue;
                    // the portal's own stretch of wall: only within its length, and only wall running along it (never a
                    // net's side or a corner round the end of it)
                    var e = segs[k].B - segs[k].A;
                    var el = e.Length;
                    if ((c - at).SqrLength <= skip * skip && el.Raw > 0 && Fix.Abs(FixVec2.Dot(e / el, along)) > Fix.Ratio(7, 10)) continue;
                    var dist = d.Length;
                    var n = dist.Raw > 0 ? d / dist : segs[k].Side;
                    pos = c + n * rr;
                    var vn = FixVec2.Dot(vel, n);
                    if (vn < Fix.Zero) vel -= n * (vn * (Fix.One + bounce));
                }
        }

        /// <summary>Push a circle out of any wall it overlaps and bounce its velocity off.</summary>
        private void CollideWalls(ref FixVec2 pos, ref FixVec2 vel, Fix r, Fix bounce)
        {
            r += WallHalf;
            var segs = Map.Segments;
            for (var pass = 0; pass < 2; pass++)
                for (var k = 0; k < segs.Count; k++)
                {
                    if (segs[k].FarFrom(pos, r)) continue; // nowhere near: the exact check below would skip it too
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
            var lo = new FixVec2(Fix.Min(p.X, q.X), Fix.Min(p.Y, q.Y));
            var hi = new FixVec2(Fix.Max(p.X, q.X), Fix.Max(p.Y, q.Y));
            foreach (var seg in Map.Segments)
            {
                if (seg.Max.X < lo.X || seg.Min.X > hi.X || seg.Max.Y < lo.Y || seg.Min.Y > hi.Y) continue; // boxes apart: can't cross
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
