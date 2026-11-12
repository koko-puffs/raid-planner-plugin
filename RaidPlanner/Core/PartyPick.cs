using System;
using System.Collections.Generic;
using System.Linq;

namespace RaidPlanner.Core;

/// <summary>Where the party was read from: the game's party list, the cross-world party's group, or just you.</summary>
public enum PartyFrom
{
    /// <summary>The game's party list (Dalamud's <c>IPartyList</c>: a normal party, and any party inside a duty).</summary>
    List,

    /// <summary>The cross-world party's own group, outside a duty (the game keeps it apart from the party list).</summary>
    CrossWorld,

    /// <summary>No party: just you.</summary>
    Alone,
}

/// <summary>
/// One member as the game side read them: from the party list, the cross-world party's group or yourself.
/// <paramref name="EntityId"/> 0 when it isn't known (the game side tries to find them in the object table first).
/// <paramref name="Group"/> is the cross-world group they're in (0 to 5; 0 outside an alliance), <paramref name="Index"/>
/// their place in it (the order they're listed in).
/// </summary>
public sealed record PartySeat(uint EntityId, ulong ContentId, string Name, uint ClassJob, byte Level, ushort HomeWorld = 0, int Group = 0, int Index = 0);

/// <summary>
/// The cross-world party as the game keeps it (<c>InfoProxyCrossRealm</c>, read by the game side): whether you're in one, whether
/// it's an alliance, which group is yours, and every group's members (each with its <see cref="PartySeat.Group"/>).
/// </summary>
public sealed record CrossWorldRead(bool InParty, bool Alliance, int LocalGroup, int Groups, IReadOnlyList<PartySeat> Members);

/// <summary>What was chosen, and (for a cross-world read that wasn't used) why not, for Diagnostics.</summary>
public sealed record PartyChoice(PartyFrom From, IReadOnlyList<PartySeat> Members, string? Why = null)
{
    /// <summary>The members' entity ids that are known (for the combat parser), in order.</summary>
    public IEnumerable<uint> Ids => Members.Where(m => PartyPick.Valid(m.EntityId)).Select(m => m.EntityId);
}

/// <summary>
/// The one rule for who the party is (since 0.5.0), used by everything that reads it (the meter, the recorder and its
/// parser, the cooldown tracker, the plan window, Home and the roster). Game-free, so it's tested with fakes:
/// <list type="number">
/// <item>The game's party list when it has anyone (a normal party; any party in a duty, cross-world or not); a member in another
/// zone (no entity id) keeps their place, known by content id or name.</item>
/// <item>Else the cross-world party's group you're in (outside a duty the game keeps a cross-world party only there, and the party
/// list is empty): in an alliance only your own group (the light party, as the party list has it in a duty), in the order the game
/// lists them. Someone whose entity id the game didn't give is looked for (<c>resolve</c>: the object table, by content id),
/// else kept by name and job alone. Not used when you aren't in it (a stale or misread group) or it has only you.</item>
/// <item>Else just you.</item>
/// </list>
/// </summary>
public static class PartyPick
{
    /// <summary>The id the game gives objects that exist only on your screen.</summary>
    public const uint InvalidId = 0xE0000000;

    /// <summary>A light party's most members (a group in the cross-world party).</summary>
    public const int GroupSize = 8;

    /// <summary>The cross-world party's most groups (an alliance of six).</summary>
    public const int MostGroups = 6;

    public static bool Valid(uint entityId) => entityId is not (0 or InvalidId);

    /// <param name="list">
    /// The party list's members. One whose entity id isn't valid (a member in another zone: the game gives <see cref="InvalidId"/>) keeps
    /// their place by content id or name, with entity id 0, as a cross-world member who isn't near does; the list is used when
    /// anyone in it has a valid id.
    /// </param>
    /// <param name="cross">The cross-world party as read, or null when it wasn't (or couldn't be) read.</param>
    /// <param name="me">You (null at the title screen).</param>
    /// <param name="resolve">Finds a member the cross-world party gave no entity id for (0 when they aren't near).</param>
    public static PartyChoice Choose(IReadOnlyList<PartySeat> list, CrossWorldRead? cross, PartySeat? me, Func<PartySeat, uint>? resolve = null)
    {
        if (list.Any(m => Valid(m.EntityId)))
            return new PartyChoice(PartyFrom.List, Distinct(list.Select(m => Valid(m.EntityId) ? m : m with { EntityId = 0 })));
        var alone = me == null ? Array.Empty<PartySeat>() : new[] { me };
        if (cross == null) return new PartyChoice(PartyFrom.Alone, alone);
        var (group, why) = CrossGroup(cross, me, resolve);
        return group != null ? new PartyChoice(PartyFrom.CrossWorld, group) : new PartyChoice(PartyFrom.Alone, alone, why);
    }

    /// <summary>Your group of the cross-world party, or why it isn't used.</summary>
    private static (List<PartySeat>? Group, string? Why) CrossGroup(CrossWorldRead cross, PartySeat? me, Func<PartySeat, uint>? resolve)
    {
        if (!cross.InParty) return (null, null);
        if (cross.Groups is < 1 or > MostGroups) return (null, $"cross-world groups {cross.Groups}");
        if (me == null) return (null, "no local player");
        bool IsMe(PartySeat m) => (Valid(me.EntityId) && m.EntityId == me.EntityId) || (me.ContentId != 0 && m.ContentId == me.ContentId);
        // Your group: the one the game says, when you're in it; else the one you're found in.
        var mine = cross.Members.Where(m => m.Group == cross.LocalGroup).ToList();
        if (!mine.Any(IsMe))
        {
            var found = cross.Members.FirstOrDefault(IsMe);
            if (found == null) return (null, "you aren't in the cross-world party's groups");
            mine = cross.Members.Where(m => m.Group == found.Group).ToList();
        }
        if (mine.Count > GroupSize) return (null, $"cross-world group of {mine.Count}");
        if (mine.Count < 2) return (null, "the cross-world group has only you");
        var seats = new List<PartySeat>(mine.Count);
        foreach (var m in mine.OrderBy(m => m.Index))
        {
            if (IsMe(m))
            {
                // You as the game has you now (your entity id is certain; the group's copy may lag a job change).
                seats.Add(m with { EntityId = me.EntityId, ClassJob = me.ClassJob != 0 ? me.ClassJob : m.ClassJob, Level = me.Level != 0 ? me.Level : m.Level });
                continue;
            }
            var id = Valid(m.EntityId) ? m.EntityId : resolve?.Invoke(m) ?? 0;
            seats.Add(m with { EntityId = Valid(id) ? id : 0 });
        }
        return (Distinct(seats), null);
    }

    /// <summary>Each member once (by entity id, else content id, else name), in order.</summary>
    private static List<PartySeat> Distinct(IEnumerable<PartySeat> seats)
    {
        var ids = new HashSet<uint>();
        var cids = new HashSet<ulong>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<PartySeat>();
        foreach (var m in seats)
        {
            if (Valid(m.EntityId) && !ids.Add(m.EntityId)) continue;
            if (m.ContentId != 0 && !cids.Add(m.ContentId)) continue;
            if (!Valid(m.EntityId) && m.ContentId == 0 && (m.Name.Length == 0 || !names.Add(m.Name))) continue;
            result.Add(m);
        }
        return result;
    }

    /// <summary>A short line for Diagnostics: where from, and each member's id, name, job id and whether they're near.</summary>
    public static string Describe(PartyChoice choice, CrossWorldRead? cross, Func<PartySeat, bool> near)
    {
        var text = new System.Text.StringBuilder();
        text.Append(choice.From switch { PartyFrom.List => "list", PartyFrom.CrossWorld => "cross-world", _ => "alone" });
        if (cross != null)
            text.Append(System.FormattableString.Invariant(
                $" (cross-world: in {cross.InParty} alliance {cross.Alliance} group {cross.LocalGroup} of {cross.Groups}, {cross.Members.Count} members)"));
        if (choice.Why != null) text.Append(" not used: ").Append(choice.Why);
        foreach (var m in choice.Members)
            text.Append(System.FormattableString.Invariant($" [{m.EntityId:X8} {m.Name} job {m.ClassJob} lv {m.Level}{(m.ContentId != 0 ? " cid" : string.Empty)}{(near(m) ? " near" : string.Empty)}]"));
        return text.ToString();
    }
}
