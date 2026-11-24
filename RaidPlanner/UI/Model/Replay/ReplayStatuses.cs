using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace RaidPlanner.UI.Model;

/// <summary>A status over the pull on the dock's lanes (the site's <c>BuffSpan</c>): which, on whom, from and to, its most stacks.</summary>
public sealed record BuffSpan(string Key, uint Id, string Name, uint Icon, int Cat, int On, string Who, double Start, double End, int Stacks)
{
    public double End { get; set; } = End;
    public int Stacks { get; set; } = Stacks;
}

/// <summary>
/// A wave of one of the fight's debuffs on the party (the site's <c>DebuffWave</c>): everyone it went on within a few
/// seconds of the first, named once for a numbered family ("First to Fourth in Line"), its first's icon, who had it and
/// each part's whose.
/// </summary>
public sealed record DebuffWave(string Key, uint Id, string Name, uint Icon, double Start, double End, IReadOnlyList<string> Who,
    IReadOnlyList<(string Name, IReadOnlyList<string> Who)> Parts, int Stacks);

/// <summary>A member's own debuffs over the pull, for the dock filling the window: who, and a span each time one was on them.</summary>
public sealed record MemberDebuffs(int Id, string Name, string Slot, string Job, IReadOnlyList<BuffSpan> Spans);

/// <summary>
/// The fight's statuses in the replay (the site's replayStatuses.ts): which are on whom at a moment (the panel's and the
/// board's), the bosses' own buffs over the pull and the party's debuffs from the fight wave by wave for the dock's
/// lanes, and each member's own for the dock filling the window. Pure.
/// </summary>
public static class ReplayStatuses
{
    /// <summary>The most stacks a status has in the game's Status sheet: above, the number is a parameter, not a count.</summary>
    public const int MaxStacks = 16;

    private static readonly Regex NamelessRe = new(@"^Status \d+$", RegexOptions.CultureInvariant);

    /// <summary>A status the game gives no name (a fight's hidden counter): the recording says "Status 2273".</summary>
    public static bool IsNameless(string name) => NamelessRe.IsMatch(name);

    /// <summary>What a raised member carries for a while (left off the lanes).</summary>
    private static readonly HashSet<string> RaiseNames = new(StringComparer.OrdinalIgnoreCase) { "Weakness", "Brink of Death" };

    /// <summary>The party's own that a recording from before plugin 0.4 carries among the fight's (it doesn't say a status's category).</summary>
    private static readonly HashSet<string> OwnNames = new(StringComparer.OrdinalIgnoreCase) { "Well Fed", "Medicated", "Reduced Rates", "Raise", "Transcendent" };

    /// <summary>
    /// Whether a fight's status on a member is one the replay shows: a debuff (category 2); for a recording that doesn't
    /// say, anything but the party's own by name (food, a potion, Reduced Rates, their own buffs).
    /// </summary>
    public static bool MemberShows(ReplayTrack tr, ReplayStatus s) =>
        s.Cat != 0 ? s.Cat == 2 : !OwnNames.Contains(s.Name) && !tr.PartyOwnNames.Contains(s.Name);

    private static readonly Comparison<StatusNow> SoonestFirst = (a, b) =>
    {
        var x = float.IsNaN(a.Left) ? 1e9f : a.Left;
        var y = float.IsNaN(b.Left) ? 1e9f : b.Left;
        return x.CompareTo(y);
    };

    /// <summary>
    /// The fight's statuses on each one at <paramref name="now"/> (the site's <c>fightStatusesAt</c>), into
    /// <paramref name="into"/> by entity index (cleared first): a member's debuffs only (<see cref="MemberShows"/>), an
    /// enemy's own buffs, nameless ones left out, each with its icon, stacks and time left, the soonest gone first.
    /// </summary>
    public static void FightStatusesAt(ReplayTrack tr, double now, List<StatusNow>[] into)
    {
        foreach (var list in into) list.Clear();
        foreach (var s in tr.Statuses)
        {
            if (s.Start > now) break;
            if (s.End <= now || IsNameless(s.Name)) continue;
            if (!tr.Index.TryGetValue(s.On, out var k)) continue;
            if (tr.Entities[k].Kind == EntityKind.Party && !MemberShows(tr, s)) continue;
            var known = s.End < tr.End;
            into[k].Add(new StatusNow
            {
                Id = s.Status,
                Name = s.Name,
                Icon = s.Icon,
                Cat = s.Cat,
                Stacks = s.Stacks <= MaxStacks ? s.Stacks : 0,
                Left = known ? (float)Math.Max(0, s.End - now) : float.NaN,
                Share = known ? (float)Math.Clamp((s.End - now) / Math.Max(0.001, s.End - s.Start), 0, 1) : float.NaN,
            });
        }
        foreach (var list in into)
            if (list.Count > 1) list.Sort(SoonestFirst);
    }

    /// <summary>
    /// The bosses' own buffs over the pull (the site's <c>bossBuffLane</c>): the fight's statuses on a boss or an add,
    /// nameless ones left out; one refreshed or restacked on the same one (a new one within half a second of the last's
    /// end) is one span. In time order.
    /// </summary>
    public static List<BuffSpan> BossBuffLane(ReplayTrack tr)
    {
        var bosses = tr.Entities.Where(e => e.Kind is EntityKind.Boss or EntityKind.Add).GroupBy(e => e.Id)
            .ToDictionary(g => g.Key, g => g.First().Name is { Length: > 0 } n ? n : "Boss");
        var open = new Dictionary<(int, uint), BuffSpan>();
        var spans = new List<BuffSpan>();
        foreach (var s in tr.Statuses)
        {
            if (!bosses.TryGetValue(s.On, out var who) || IsNameless(s.Name) || s.End <= s.Start) continue;
            var key = (s.On, s.Status);
            var stacks = s.Stacks <= MaxStacks ? s.Stacks : 0;
            if (open.TryGetValue(key, out var had) && s.Start <= had.End + 0.5)
            {
                had.End = Math.Max(had.End, s.End);
                had.Stacks = Math.Max(had.Stacks, stacks);
                continue;
            }
            var span = new BuffSpan($"{s.On}|{s.Status}|{s.Start}", s.Status, s.Name, s.Icon, s.Cat, s.On, who, s.Start, s.End, stacks);
            spans.Add(span);
            open[key] = span;
        }
        return spans.OrderBy(x => x.Start).ToList();
    }

    /// <summary>A debuff gained this long (s) after its wave's first is still that wave.</summary>
    private const double WaveGap = 3;

    private static readonly string[] Ordinals = { "First", "Second", "Third", "Fourth", "Fifth", "Sixth", "Seventh", "Eighth" };
    private static readonly string[] Roman = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII" };

    /// <summary>
    /// A debuff's family (the site's <c>debuffFamily</c>): some come in numbered sets at once (TOP's First to Fourth in
    /// Line), the number a word of its own (First to Eighth, 1 to 8, I to VIII), one in the name. Its name with the number
    /// as "#" ("# in Line"), and the number (0: none).
    /// </summary>
    public static (string Key, int N) DebuffFamily(string name)
    {
        var words = name.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var at = new List<(int I, int N)>();
        for (var i = 0; i < words.Length; i++)
        {
            var w = words[i];
            var n = Array.IndexOf(Ordinals, w) + 1;
            if (n == 0) n = Array.IndexOf(Roman, w) + 1;
            if (n == 0 && w.Length == 1 && w[0] is >= '1' and <= '8') n = w[0] - '0';
            if (n > 0) at.Add((i, n));
        }
        if (at.Count != 1 || words.Length < 2) return (name, 0);
        words[at[0].I] = "#";
        return (string.Join(' ', words), at[0].N);
    }

    /// <summary>A family's name said once: "First to Fourth in Line", "First and Second in Line".</summary>
    private static string FamilyName(IReadOnlyList<(string Name, int N)> parts)
    {
        if (parts.Count == 1) return parts[0].Name;
        var key = DebuffFamily(parts[0].Name).Key;
        var at = Array.IndexOf(key.Split(' '), "#");
        string Word(string n)
        {
            var w = n.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            return at >= 0 && at < w.Length ? w[at] : string.Empty;
        }
        var first = Word(parts[0].Name);
        var last = Word(parts[^1].Name);
        var idx = key.IndexOf('#');
        return key[..idx] + $"{first} {(parts.Count == 2 ? "and" : "to")} {last}" + key[(idx + 1)..];
    }

    /// <summary>One of the fight's debuffs on a member, as the lanes count them: shown on them, not nameless nor a raise's after-effect.</summary>
    private static bool IsFightDebuff(ReplayTrack tr, ReplayStatus s) =>
        s.End > s.Start && !IsNameless(s.Name) && !RaiseNames.Contains(s.Name) && MemberShows(tr, s);

    private sealed class OpenWave
    {
        public required string Key;
        public required uint Id;
        public required string Name;
        public required uint Icon;
        public required double Start;
        public double End;
        public int Stacks;
        public readonly List<string> Who = new();
        public readonly HashSet<int> On = new();
        public readonly List<(string Name, int N, uint Id, uint Icon, List<string> Who)> Parts = new();
    }

    /// <summary>
    /// The party's debuffs from the fight over the pull (the site's <c>partyDebuffLane</c>): each time one went round one
    /// span, from the first to get it to the last to lose it (gained within 3 s of the wave's first, or a refresh on one
    /// who has it still), with who had it; a numbered family given at once one wave. In time order.
    /// </summary>
    public static List<DebuffWave> PartyDebuffLane(ReplayTrack tr)
    {
        var members = tr.Entities.Where(e => e.Kind == EntityKind.Party).GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First());
        var open = new Dictionary<string, OpenWave>();
        var waves = new List<OpenWave>();
        foreach (var s in tr.Statuses)
        {
            if (!members.TryGetValue(s.On, out var m) || !IsFightDebuff(tr, s)) continue;
            var stacks = s.Stacks <= MaxStacks ? s.Stacks : 0;
            var fam = DebuffFamily(s.Name);
            var who = m.Name is { Length: > 0 } n ? n : m.Slot;
            open.TryGetValue(fam.Key, out var wave);
            if (!(wave != null && (s.Start <= wave.Start + WaveGap || (wave.On.Contains(s.On) && s.Start <= wave.End + 0.5))))
            {
                wave = new OpenWave { Key = $"{fam.Key}|{s.Start}", Id = s.Status, Name = s.Name, Icon = s.Icon, Start = s.Start, End = s.End };
                waves.Add(wave);
                open[fam.Key] = wave;
            }
            wave.End = Math.Max(wave.End, s.End);
            wave.Stacks = Math.Max(wave.Stacks, stacks);
            if (wave.On.Add(s.On)) wave.Who.Add(who);
            var pi = wave.Parts.FindIndex(p => p.Name == s.Name);
            if (pi < 0)
            {
                wave.Parts.Add((s.Name, fam.N, s.Status, s.Icon, new List<string>()));
                pi = wave.Parts.Count - 1;
            }
            if (!wave.Parts[pi].Who.Contains(who)) wave.Parts[pi].Who.Add(who);
        }
        return waves.OrderBy(w => w.Start).Select(w =>
        {
            var parts = w.Parts.OrderBy(p => p.N).ToList();
            var first = parts[0];
            return new DebuffWave(w.Key, first.Id, FamilyName(parts.Select(p => (p.Name, p.N)).ToList()), first.Icon, w.Start, w.End, w.Who,
                parts.Select(p => (p.Name, (IReadOnlyList<string>)p.Who)).ToList(), w.Stacks);
        }).ToList();
    }

    /// <summary>
    /// Each member's debuffs from the fight over the pull (the site's <c>memberDebuffLanes</c>), in the party's order: the
    /// same as the waves', one span each time one was on them (a refresh within half a second one). A member with none
    /// still has a lane.
    /// </summary>
    public static List<MemberDebuffs> MemberDebuffLanes(ReplayTrack tr)
    {
        var out_ = new List<MemberDebuffs>();
        var spans = new Dictionary<int, List<BuffSpan>>();
        foreach (var e in tr.Entities.Where(e => e.Kind == EntityKind.Party))
        {
            if (spans.ContainsKey(e.Id)) continue;
            var list = new List<BuffSpan>();
            spans[e.Id] = list;
            out_.Add(new MemberDebuffs(e.Id, e.Name is { Length: > 0 } n ? n : e.Slot, e.Slot, e.Job, list));
        }
        var open = new Dictionary<(int, uint), BuffSpan>();
        foreach (var s in tr.Statuses)
        {
            if (!spans.TryGetValue(s.On, out var list) || !IsFightDebuff(tr, s)) continue;
            var stacks = s.Stacks <= MaxStacks ? s.Stacks : 0;
            var key = (s.On, s.Status);
            if (open.TryGetValue(key, out var had) && s.Start <= had.End + 0.5)
            {
                had.End = Math.Max(had.End, s.End);
                had.Stacks = Math.Max(had.Stacks, stacks);
                continue;
            }
            var who = out_.First(m => m.Id == s.On).Name;
            var span = new BuffSpan($"{s.On}|{s.Status}|{s.Start}", s.Status, s.Name, s.Icon, 2, s.On, who, s.Start, s.End, stacks);
            list.Add(span);
            open[key] = span;
        }
        return out_;
    }
}
