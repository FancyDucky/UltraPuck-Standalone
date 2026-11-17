using System;
using System.Collections.Generic;

namespace UltraPuck.Core
{
    /// <summary>Rarity: S is the rarest and strongest, F the most common (and often a bit of a joke).</summary>
    public enum CardTier { S, A, B, C, D, F }

    /// <summary>One number a card changes: "MoveSpeed x1.15", "ExtraDashes +1", or an opponent's stat ("OpponentMoveSpeed x0.9").</summary>
    public sealed class StatMod
    {
        public string Stat;
        public bool Multiply;
        public Fix Value;
        /// <summary>Applies to every player on the other team instead of the card's owner.</summary>
        public bool Opponents;
    }

    /// <summary>A card: what it's called, what it says, how rare it is, and the numbers it changes.</summary>
    public sealed class CardDef
    {
        /// <summary>Position in the library: what game states store. The same on every PC as long as the card files are.</summary>
        public int Index;
        public string Id, Name, Text, Art;
        /// <summary>The card's accent colour as "#RRGGBB" (for its frame), or null for the default.</summary>
        public string Color;
        public CardTier Tier = CardTier.C;
        /// <summary>Most copies one player can hold (0 = no limit).</summary>
        public int MaxPerPlayer;
        /// <summary>Only offered once someone on your team has this card (an id), e.g. Team Play II needs Team Play.</summary>
        public string Requires;
        /// <summary>Only offered on teams at least this big (team cards are no use alone).</summary>
        public int MinTeam;
        public readonly List<StatMod> Mods = new List<StatMod>();
    }

    /// <summary>
    /// Every card in the game, read from JSON files (Assets/StreamingAssets/Cards/*.json; mods can add more files).
    /// A card file looks like:
    /// <code>
    /// { "cards": [ { "id": "ultrapuck.speedup", "name": "Speed Up", "tier": "A", "max": 0, "art": "ultrapuck.speedup",
    ///                "text": "[good]+15%[/] Move Speed", "stats": { "MoveSpeed": "x1.15" } } ] }
    /// </code>
    /// Stat values: "x1.5" multiplies, "/1.6" divides, "+1" or "-0.5" adds. Prefix a stat with "Opponent" to change
    /// the other team's instead. Text markup for the card face: [good]...[/], [bad]...[/], [note]...[/].
    /// Cards that need new mechanics (not just numbers) are added in code later.
    /// </summary>
    public sealed class CardLibrary
    {
        public readonly List<CardDef> Cards = new List<CardDef>();
        private readonly Dictionary<string, CardDef> _byId = new Dictionary<string, CardDef>();

        /// <summary>Stats a card file may change, so a typo is an error instead of a card that silently does nothing.</summary>
        public static readonly string[] StatNames =
        {
            "MoveSpeed", "MoveSpeedWithPuck", "ShotPower", "QuickShotPower", "ChargeSpeed", "OverCharge", "DashCooldown",
            "DashPower", "ExtraDashes", "Size", "Weight", "KnockbackGiven", "KnockbackTaken",
            "HeadStart", "DoubleAgent", "Pylons", "PuckGun", "Invisibility", "FriendlyFire", "Shockwave", "PassRefresh", "PassRefreshTeam",
        };

        public CardDef ById(string id) { CardDef c; return id != null && _byId.TryGetValue(id, out c) ? c : null; }

        /// <summary>Add every card in one JSON file (call once per file, in a fixed order, e.g. sorted by file name).</summary>
        public void AddJson(string json, string source)
        {
            var root = Json.Obj(Json.Parse(json), source);
            var list = Json.Arr(Json.Get(root, "cards"), source + ": cards");
            for (var k = 0; k < list.Count; k++)
            {
                var what = source + ": cards[" + k + "]";
                var o = Json.Obj(list[k], what);
                var c = new CardDef
                {
                    Id = Json.Str(Json.Get(o, "id"), null),
                    Name = Json.Str(Json.Get(o, "name"), "?"),
                    Text = Json.Str(Json.Get(o, "text"), ""),
                    Art = Json.Str(Json.Get(o, "art"), null),
                    Color = Json.Str(Json.Get(o, "color"), null),
                    MaxPerPlayer = Json.Get(o, "max") != null ? Json.Int(Json.Get(o, "max"), what + ".max") : 0,
                    Requires = Json.Str(Json.Get(o, "requires"), null),
                    MinTeam = Json.Get(o, "minTeam") != null ? Json.Int(Json.Get(o, "minTeam"), what + ".minTeam") : 0,
                };
                if (string.IsNullOrEmpty(c.Id)) throw new FormatException(what + ": needs an id");
                if (_byId.ContainsKey(c.Id)) throw new FormatException(what + ": a card with the id \"" + c.Id + "\" already exists");
                CardTier tier;
                if (!Enum.TryParse(Json.Str(Json.Get(o, "tier"), "C"), true, out tier)) throw new FormatException(what + ": tier must be S, A, B, C, D or F");
                c.Tier = tier;
                if (c.Art == null) c.Art = c.Id;
                var stats = Json.Get(o, "stats") as Dictionary<string, object>;
                if (stats != null)
                {
                    var keys = new List<string>(stats.Keys);
                    keys.Sort(string.CompareOrdinal); // a fixed order, whatever order the file lists them in
                    foreach (var key in keys) c.Mods.Add(ParseMod(key, stats[key], what));
                }
                c.Index = Cards.Count;
                Cards.Add(c);
                _byId[c.Id] = c;
            }
        }

        private static StatMod ParseMod(string key, object value, string what)
        {
            var m = new StatMod { Stat = key };
            if (key.StartsWith("Opponent", StringComparison.Ordinal)) { m.Opponents = true; m.Stat = key.Substring(8); }
            if (Array.IndexOf(StatNames, m.Stat) < 0) throw new FormatException(what + ": unknown stat \"" + key + "\"");
            var s = (value as string ?? (value is JsonNumber ? "+" + ((JsonNumber)value).Text : null));
            if (string.IsNullOrEmpty(s)) throw new FormatException(what + "." + key + ": expected text like \"x1.5\" or \"+1\"");
            s = s.Trim();
            if (s[0] == 'x' || s[0] == 'X' || s[0] == '*') { m.Multiply = true; m.Value = Fix.Parse(s.Substring(1)); }
            else if (s[0] == '/') { m.Multiply = true; m.Value = Fix.One / Fix.Parse(s.Substring(1)); }
            else { m.Multiply = false; m.Value = Fix.Parse(s[0] == '+' ? s.Substring(1) : s); }
            return m;
        }

        // ------------------------------------------------------------------ stats

        /// <summary>Everyone's stats worked out again from the cards held: their own, plus the other team's "Opponent" cards.</summary>
        public void Recompute(GameState s)
        {
            foreach (var p in s.Players)
            {
                var st = new Stats();
                foreach (var ci in p.Cards) Apply(ci, st, false);
                foreach (var q in s.Players)
                    if (q.Team != p.Team)
                        foreach (var ci in q.Cards) Apply(ci, st, true);
                p.Stats = st;
            }
        }

        private void Apply(int card, Stats st, bool opponents)
        {
            if (card < 0 || card >= Cards.Count) return;
            foreach (var m in Cards[card].Mods)
                if (m.Opponents == opponents) ApplyMod(st, m);
        }

        private static void ApplyMod(Stats st, StatMod m)
        {
            switch (m.Stat)
            {
                case "MoveSpeed": st.MoveSpeed = Op(st.MoveSpeed, m); break;
                case "MoveSpeedWithPuck": st.MoveSpeedWithPuck = Op(st.MoveSpeedWithPuck, m); break;
                case "ShotPower": st.ShotPower = Op(st.ShotPower, m); break;
                case "QuickShotPower": st.QuickShotPower = Op(st.QuickShotPower, m); break;
                case "ChargeSpeed": st.ChargeSpeed = Op(st.ChargeSpeed, m); break;
                case "OverCharge": st.OverCharge = Op(st.OverCharge, m); break;
                case "DashCooldown": st.DashCooldown = Op(st.DashCooldown, m); break;
                case "DashPower": st.DashPower = Op(st.DashPower, m); break;
                case "ExtraDashes": st.ExtraDashes = Op(Fix.FromInt(st.ExtraDashes), m).FloorToInt(); break;
                case "Size": st.Size = Op(st.Size, m); break;
                case "Weight": st.Weight = Op(st.Weight, m); break;
                case "KnockbackGiven": st.KnockbackGiven = Op(st.KnockbackGiven, m); break;
                case "KnockbackTaken": st.KnockbackTaken = Op(st.KnockbackTaken, m); break;
                case "HeadStart": st.HeadStart = Count(st.HeadStart, m); break;
                case "DoubleAgent": st.DoubleAgent = Count(st.DoubleAgent, m); break;
                case "Pylons": st.Pylons = Count(st.Pylons, m); break;
                case "PuckGun": st.PuckGun = Count(st.PuckGun, m); break;
                case "Invisibility": st.Invisibility = Count(st.Invisibility, m); break;
                case "FriendlyFire": st.FriendlyFire = Count(st.FriendlyFire, m); break;
                case "Shockwave": st.Shockwave = Count(st.Shockwave, m); break;
                case "PassRefresh": st.PassRefresh = Count(st.PassRefresh, m); break;
                case "PassRefreshTeam": st.PassRefreshTeam = Count(st.PassRefreshTeam, m); break;
            }
        }

        private static Fix Op(Fix v, StatMod m) { return m.Multiply ? v * m.Value : v + m.Value; }
        private static int Count(int v, StatMod m) { return Op(Fix.FromInt(v), m).FloorToInt(); }

        // ------------------------------------------------------------------ offers

        /// <summary>Does anyone on <paramref name="team"/> hold the card <paramref name="id"/>?</summary>
        private bool TeamHas(GameState s, int team, string id)
        {
            var want = ById(id);
            if (want == null) return false;
            foreach (var q in s.Players)
                if (q.Team == team)
                    foreach (var c in q.Cards) if (c == want.Index) return true;
            return false;
        }

        /// <summary>How often a tier turns up compared with the others.</summary>
        public static int Weight(CardTier t)
        {
            switch (t) { case CardTier.S: return 1; case CardTier.A: return 3; case CardTier.B: return 5; case CardTier.F: return 4; default: return 6; }
        }

        /// <summary>Up to <paramref name="count"/> different cards player <paramref name="i"/> may still take, picked at random by rarity.</summary>
        public int[] DrawOffer(GameState s, int i, int count)
        {
            var p = s.Players[i];
            var pool = new List<int>();
            var teamSize = 0;
            foreach (var q in s.Players) if (q.Team == p.Team) teamSize++;
            foreach (var c in Cards)
            {
                if (c.MinTeam > 0 && teamSize < c.MinTeam) continue;
                if (c.Requires != null && !TeamHas(s, p.Team, c.Requires)) continue;
                if (c.MaxPerPlayer > 0)
                {
                    var held = 0;
                    foreach (var h in p.Cards) if (h == c.Index) held++;
                    if (held >= c.MaxPerPlayer) continue;
                }
                pool.Add(c.Index);
            }
            var offer = new List<int>();
            while (offer.Count < count && pool.Count > 0)
            {
                var total = 0;
                foreach (var ci in pool) total += Weight(Cards[ci].Tier);
                var roll = (int)(s.NextRandom() % (uint)total);
                for (var k = 0; k < pool.Count; k++)
                {
                    roll -= Weight(Cards[pool[k]].Tier);
                    if (roll >= 0) continue;
                    offer.Add(pool[k]);
                    pool.RemoveAt(k);
                    break;
                }
            }
            return offer.ToArray();
        }
    }
}
