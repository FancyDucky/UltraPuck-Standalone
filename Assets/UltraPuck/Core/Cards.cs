using System;
using System.Collections.Generic;

namespace UltraPuck.Core
{
    /// <summary>
    /// How strong a card is: S the strongest, F the weakest (and often a bit of a joke). It sets the card's frame
    /// colour and which card a bot takes; it doesn't change how often a card is offered (they're all as likely).
    /// </summary>
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

    /// <summary>One row on the card face under the rule: label on the left, value on the right.</summary>
    public sealed class FaceStat
    {
        public string Label, Value;
    }

    /// <summary>A card: what it's called, what it says, how strong it is, and the numbers it changes.</summary>
    public sealed class CardDef
    {
        /// <summary>Position in the library: what game states store. The same on every PC as long as the card files are.</summary>
        public int Index;
        public string Id, Name, Text, Art;
        /// <summary>Short line under the title (above the coloured rule), or null / empty for none.</summary>
        public string Flavor;
        /// <summary>The card's accent colour as "#RRGGBB" (for its frame), or null for the default.</summary>
        public string Color;
        public CardTier Tier = CardTier.C;
        /// <summary>Optional special look, display only: "foil" (metal sheen), "polychrome" (shifting rainbow), "holo" (the S tier's rainbow holo + sparkles) or "pearl" (the A tier's pearlescent), else null (the tier's own look).</summary>
        public string Finish;
        /// <summary>The light sweep across the card as it tilts, display only; "glare": false turns it off.</summary>
        public bool Glare = true;
        /// <summary>Every "finish" a card file may use (see Card.shader for what each looks like).</summary>
        public static readonly string[] FinishNames =
            { "foil", "polychrome", "holo", "pearl", "starfield", "gilded", "prism", "frosted", "neon", "aurora", "molten", "glitch" };
        /// <summary>Most copies one player can hold (0 = no limit).</summary>
        public int MaxPerPlayer;
        /// <summary>Most players on one team who may hold it (0 = no limit): "maxTeam".</summary>
        public int MaxPerTeam;
        /// <summary>Only offered once someone on your team has this card (an id), e.g. a card that builds on another.</summary>
        public string Requires;
        /// <summary>Only offered on teams at least this big (team cards are no use alone).</summary>
        public int MinTeam;
        /// <summary>Never offered to bots.</summary>
        public bool NoBots;
        /// <summary>Stat rows under the rule (label left, value right). Empty = use <see cref="Text"/> as a free block.</summary>
        public readonly List<FaceStat> Face = new List<FaceStat>();
        public readonly List<StatMod> Mods = new List<StatMod>();
    }

    /// <summary>
    /// Every card in the game, read from JSON files (Assets/StreamingAssets/Cards/*.json; mods can add more files).
    /// A card file looks like:
    /// <code>
    /// { "cards": [ { "id": "ultrapuck.speedup", "name": "Speed Up", "tier": "A", "flavor": "…",
    ///                "face": [ { "label": "Move Speed", "value": "[good]+15%[/]" } ],
    ///                "stats": { "MoveSpeed": "x1.15" } } ] }
    /// </code>
    /// Stat values: "x1.5" multiplies, "/1.6" divides, "+1" or "-0.5" adds. Prefix a stat with "Opponent" to change
    /// the other team's instead. Face value markup: [good]...[/], [bad]...[/], [note]...[/], [gold]...[/].
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
            "HeadStart", "DoubleAgent", "Pylons", "PuckGun", "Invisibility", "FriendlyFire", "Shockwave", "Swap", "PassRefresh", "PassRefreshTeam", "IceTrail",
            "QuickShotFull", "GhostShot", "CannonBall", "PassSpeed", "StoneWall", "PortalGun", "SlotMachine",
        };

        /// <summary>Stats that need the utility button (Triangle / Y). Used to offer only one utility at a time.</summary>
        public static readonly string[] UtilityStats = { "PuckGun", "Invisibility", "Shockwave", "Swap", "PortalGun", "SlotMachine" };

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
                    Flavor = Json.Str(Json.Get(o, "flavor"), null),
                    Art = Json.Str(Json.Get(o, "art"), null),
                    Color = Json.Str(Json.Get(o, "color"), null),
                    MaxPerPlayer = Json.Get(o, "max") != null ? Json.Int(Json.Get(o, "max"), what + ".max") : 0,
                    Requires = Json.Str(Json.Get(o, "requires"), null),
                    MinTeam = Json.Get(o, "minTeam") != null ? Json.Int(Json.Get(o, "minTeam"), what + ".minTeam") : 0,
                    MaxPerTeam = Json.Get(o, "maxTeam") != null ? Json.Int(Json.Get(o, "maxTeam"), what + ".maxTeam") : 0,
                    NoBots = Json.Get(o, "noBots") is bool && (bool)Json.Get(o, "noBots"),
                    Glare = !(Json.Get(o, "glare") is bool) || (bool)Json.Get(o, "glare"),
                };
                if (string.IsNullOrEmpty(c.Id)) throw new FormatException(what + ": needs an id");
                if (_byId.ContainsKey(c.Id)) throw new FormatException(what + ": a card with the id \"" + c.Id + "\" already exists");
                CardTier tier;
                if (!Enum.TryParse(Json.Str(Json.Get(o, "tier"), "C"), true, out tier)) throw new FormatException(what + ": tier must be S, A, B, C, D or F");
                c.Tier = tier;
                var finish = Json.Str(Json.Get(o, "finish"), null);
                if (!string.IsNullOrEmpty(finish))
                {
                    finish = finish.ToLowerInvariant();
                    if (Array.IndexOf(CardDef.FinishNames, finish) < 0) throw new FormatException(what + ": finish must be one of " + string.Join(", ", CardDef.FinishNames));
                    c.Finish = finish;
                }
                if (c.Art == null) c.Art = c.Id;
                if (Json.Get(o, "face") != null)
                {
                    var face = Json.Arr(Json.Get(o, "face"), what + ".face");
                    for (var f = 0; f < face.Count; f++)
                    {
                        var fo = Json.Obj(face[f], what + ".face[" + f + "]");
                        c.Face.Add(new FaceStat
                        {
                            Label = Json.Str(Json.Get(fo, "label"), ""),
                            Value = Json.Str(Json.Get(fo, "value"), ""),
                        });
                    }
                }
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
            // your cards go with you: a person steering a bot teammate gives that body their cards, and their own body
            // (run by the bot meanwhile) gets the bot's - size, weight and all
            var n = s.Players.Length;
            var from = new int[n];
            for (var i = 0; i < n; i++) from[i] = i;
            for (var h = 0; h < n; h++)
            {
                var j = s.Players[h].Pilot;
                if (s.Players[h].Bot || j < 0 || j >= n || j == h) continue;
                from[j] = h;
                from[h] = j;
            }
            for (var i = 0; i < n; i++)
            {
                var p = s.Players[i];
                var st = new Stats();
                foreach (var ci in s.Players[from[i]].Cards) Apply(ci, st, false);
                foreach (var q in s.Players)
                    if (q.Team != p.Team)
                        foreach (var ci in q.Cards) Apply(ci, st, true);
                // a Slot Machine prize that changes the body: Giant (bigger and heavier), Strong (Over Charge cards)
                if (p.SlotPrizeTime > Fix.Zero && p.SlotPrize == SlotGiant) { st.Size *= GiantBy; st.Weight *= GiantBy; }
                if (p.SlotPrizeTime > Fix.Zero && p.SlotPrize == SlotStrong)
                {
                    var oc = ById("ultrapuck.overcharge");
                    if (oc != null) for (var k = 0; k < StrongCards; k++) Apply(oc.Index, st, false);
                }
                p.Stats = st;
            }
        }

        /// <summary>Slot Machine prizes (<see cref="PlayerState.SlotPrize"/>).</summary>
        public const int SlotSpeed = 1, SlotGiant = 2, SlotInvisible = 3, SlotStrong = 4;
        /// <summary>Set from the match's tuning by the simulation: how much bigger Giant makes you, how many Over Charge cards Strong is.</summary>
        public Fix GiantBy = Fix.Two;
        public int StrongCards = 4;

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
                case "Swap": st.Swap = Count(st.Swap, m); break;
                case "PortalGun": st.PortalGun = Count(st.PortalGun, m); break;
                case "SlotMachine": st.SlotMachine = Count(st.SlotMachine, m); break;
                case "IceTrail": st.IceTrail = Count(st.IceTrail, m); break;
                case "QuickShotFull": st.QuickShotFull = Count(st.QuickShotFull, m); break;
                case "GhostShot": st.GhostShot = Count(st.GhostShot, m); break;
                case "CannonBall": st.CannonBall = Count(st.CannonBall, m); break;
                case "PassRefresh": st.PassRefresh = Count(st.PassRefresh, m); break;
                case "PassRefreshTeam": st.PassRefreshTeam = Count(st.PassRefreshTeam, m); break;
                case "PassSpeed": st.PassSpeed = Count(st.PassSpeed, m); break;
                case "StoneWall": st.StoneWall = Count(st.StoneWall, m); break;
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

        /// <summary>Does this card use the utility button?</summary>
        public bool IsUtility(CardDef c)
        {
            if (c == null) return false;
            foreach (var m in c.Mods)
                if (!m.Opponents && Array.IndexOf(UtilityStats, m.Stat) >= 0) return true;
            return false;
        }

        public bool IsUtilityIndex(int index) { return index >= 0 && index < Cards.Count && IsUtility(Cards[index]); }

        /// <summary>The first utility card this player holds, or null.</summary>
        public CardDef UtilityHeld(PlayerState p)
        {
            if (p == null) return null;
            foreach (var ci in p.Cards) if (IsUtilityIndex(ci)) return Cards[ci];
            return null;
        }

        /// <summary>
        /// Taking <paramref name="offered"/> would replace another utility already held (one-utility mode).
        /// Same utility again (extra copy) does not count as a replace.
        /// </summary>
        public bool WouldReplaceUtility(PlayerState p, int offered)
        {
            if (!IsUtilityIndex(offered)) return false;
            var held = UtilityHeld(p);
            return held != null && held.Index != offered;
        }

        /// <summary>
        /// Drop every utility card that isn't <paramref name="keep"/> (used when one utility is allowed at a time).
        /// </summary>
        public static int[] WithoutOtherUtilities(CardLibrary lib, int[] cards, int keep)
        {
            if (cards == null || cards.Length == 0) return cards ?? new int[0];
            var keepList = new List<int>();
            foreach (var c in cards)
                if (!lib.IsUtilityIndex(c) || c == keep) keepList.Add(c);
            return keepList.ToArray();
        }

        /// <summary>Can <paramref name="p"/> not take <paramref name="card"/> because enough of their teammates have it ("maxTeam")?</summary>
        public bool TeamFull(GameState s, PlayerState p, int card)
        {
            if (card < 0 || card >= Cards.Count || Cards[card].MaxPerTeam <= 0) return false;
            var holders = 0;
            foreach (var q in s.Players)
            {
                if (q == p || q.Team != p.Team) continue;
                if (Array.IndexOf(q.Cards, card) >= 0) holders++;
            }
            return holders >= Cards[card].MaxPerTeam;
        }

        /// <summary>
        /// Up to <paramref name="count"/> different cards player <paramref name="i"/> may still take, picked at random:
        /// every card they could be offered is as likely as any other, whatever its tier.
        /// Other utilities stay in the pool — picking one replaces the utility you already have (unless MultiUtility).
        /// </summary>
        public int[] DrawOffer(GameState s, int i, int count, int[] off = null)
        {
            var p = s.Players[i];
            var pool = new List<int>();
            var teamSize = 0;
            foreach (var q in s.Players) if (q.Team == p.Team) teamSize++;
            foreach (var c in Cards)
            {
                if (off != null && Array.IndexOf(off, c.Index) >= 0) continue;
                if (c.MinTeam > 0 && teamSize < c.MinTeam) continue;
                if (c.NoBots && p.Bot) continue;
                if (c.Requires != null && !TeamHas(s, p.Team, c.Requires)) continue;
                if (c.MaxPerPlayer > 0)
                {
                    var held = 0;
                    foreach (var h in p.Cards) if (h == c.Index) held++;
                    if (held >= c.MaxPerPlayer) continue;
                }
                if (TeamFull(s, p, c.Index)) continue;
                pool.Add(c.Index);
            }
            var offer = new List<int>();
            while (offer.Count < count && pool.Count > 0)
            {
                var k = (int)(s.NextRandom() % (uint)pool.Count);
                offer.Add(pool[k]);
                pool.RemoveAt(k);
            }
            return offer.ToArray();
        }
    }
}
