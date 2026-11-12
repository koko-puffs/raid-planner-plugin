using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.SubKinds;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using RaidPlanner.Core;

namespace RaidPlanner.Game;

/// <summary>
/// A party member as <see cref="PartyMembers"/> has them this frame: their entity id (0 when it isn't known: a cross-world
/// member the game gave none for and who isn't near), name, ClassJob row and level; their HP (the party list's, at any
/// distance; a cross-world member's from their character, when near), whether they're near (in the game's character list)
/// and in combat; and whether it's you.
/// </summary>
public readonly record struct PartyMember(uint EntityId, ulong ContentId, string Name, uint ClassJob, byte Level, uint Hp, uint MaxHp, bool Near, bool InCombat, bool IsYou)
{
    /// <summary>A key to find them again (their entity id, else their content id): what <see cref="Partier.Key"/> holds.</summary>
    public ulong Key => PartyPick.Valid(EntityId) ? EntityId : ContentId;
}

/// <summary>
/// Who the party is (since 0.5.0), the one place it's read: the meter, the recorder (and its parser's "is this the party's"),
/// the cooldown tracker, the plan window, Home and the roster all ask here. The rule is <see cref="PartyPick"/>'s: the game's
/// party list; else, outside a duty, the cross-world party's group you're in (the game keeps a cross-world party there, in
/// <c>InfoProxyCrossRealm</c>, and the party list is empty, so before 0.5.0 only you were counted: a friend's damage on a
/// target dummy was dropped); else just you.
///
/// The members are read at most four times a second (names make strings); their HP, whether they're near and in combat each
/// frame (once, however many ask). The cross-world read is the one reading here not checked in the game [verify]: it's
/// guarded (a group that makes no sense isn't used, and a read that throws stops it for a minute) and, with Diagnostics on,
/// each change of who the party is goes to the file (<c>party-read</c>). On the framework thread only.
/// </summary>
public static unsafe class PartyMembers
{
    /// <summary>How often the members themselves are read (seconds).</summary>
    private const double SeatsEvery = 0.25;

    private static readonly List<PartyMember> members = new(PartyPick.GroupSize * 3);
    private static readonly List<PartySeat> listBuffer = new(PartyPick.GroupSize);
    private static PartyChoice choice = new(PartyFrom.Alone, Array.Empty<PartySeat>());
    private static DateTime frame;
    private static long nextSeats;
    private static long crossBrokenUntil;
    private static string lastLogged = string.Empty;
    private static bool loggedWhileOn;

    /// <summary>Where the party lines go when Diagnostics is on (the recorder's; set as it's made, null after).</summary>
    public static DiagnosticsLog? Diagnostics { get; set; }

    /// <summary>Where the party was last read from.</summary>
    public static PartyFrom From => choice.From;

    /// <summary>The party this frame, in order (you included; just you when alone; empty at the title screen).</summary>
    public static List<PartyMember> Read()
    {
        var now = Services.Framework.LastUpdateUTC;
        if (now == frame && now != default) return members;
        frame = now;
        if (Services.Objects.LocalPlayer is not { } me)
        {
            members.Clear();
            choice = new PartyChoice(PartyFrom.Alone, Array.Empty<PartySeat>());
            return members;
        }
        var tick = Environment.TickCount64;
        if (tick >= nextSeats || members.Count == 0)
        {
            nextSeats = tick + (long)(SeatsEvery * 1000);
            ReadSeats(me);
        }
        Refresh(me.EntityId);
        return members;
    }

    /// <summary>How many are in the party this frame (1 when alone).</summary>
    public static int Count => Math.Max(1, Read().Count);

    /// <summary>The members' entity ids that are known, into <paramref name="into"/> (for the combat parser); how many.</summary>
    public static int Ids(Span<uint> into)
    {
        var n = 0;
        foreach (var m in Read())
            if (PartyPick.Valid(m.EntityId) && n < into.Length)
                into[n++] = m.EntityId;
        return n;
    }

    /// <summary>The party as <see cref="Partier"/>s with their jobs (the recorder's, the spots' and the roster's).</summary>
    public static List<Partier> Partiers(GameData data)
    {
        var list = new List<Partier>();
        foreach (var m in Read())
        {
            var (job, role) = data.Job(m.ClassJob);
            list.Add(new Partier(m.Key, job, role, m.MaxHp));
        }
        return list;
    }

    private static (long At, string? Spot) yourSpot = (long.MinValue, null);

    /// <summary>Your spot now (MT, H1…; the party's spots by <see cref="Roles"/>), read at most once a second; null at the title screen.</summary>
    public static string? YourSpot(GameData data)
    {
        var tick = Environment.TickCount64;
        if (tick - yourSpot.At < 1000) return yourSpot.Spot;
        string? spot = null;
        if (Services.Objects.LocalPlayer is { } me)
        {
            var party = Partiers(data);
            if (party.Count == 0)
            {
                var (job, role) = data.Job(me.ClassJob.RowId);
                party.Add(new Partier(me.EntityId, job, role));
            }
            foreach (var (who, slot) in Roles.Assign(party))
                if (who.Key == me.EntityId)
                    spot = slot;
        }
        yourSpot = (tick, spot);
        return spot;
    }

    /// <summary>Who the party is: the party list, the cross-world party's group or you (<see cref="PartyPick.Choose"/>).</summary>
    private static void ReadSeats(IPlayerCharacter me)
    {
        listBuffer.Clear();
        var i = 0;
        foreach (var m in Services.Party)
        {
            if (PartyPick.Valid(m.EntityId))
                listBuffer.Add(new PartySeat(m.EntityId, m.ContentId, m.Name.TextValue, m.ClassJob.RowId, m.Level, (ushort)m.World.RowId, 0, i));
            i++;
        }
        var self = new PartySeat(me.EntityId, ContentIdOf(me.Address), me.Name.TextValue, me.ClassJob.RowId, me.Level);
        // The cross-world party only when the party list is empty (it's the list's otherwise, even in a duty).
        var cross = listBuffer.Count == 0 ? ReadCrossWorld() : null;
        choice = PartyPick.Choose(listBuffer, cross, self, Resolve);
        members.Clear();
        foreach (var s in choice.Members)
            members.Add(new PartyMember(s.EntityId, s.ContentId, s.Name, s.ClassJob, s.Level, 0, 0, false, false, s.EntityId == me.EntityId));
        Log(cross);
    }

    /// <summary>Each member's HP, whether near and in combat, this frame.</summary>
    private static void Refresh(uint meId)
    {
        var manager = CharacterManager.Instance();
        for (var k = 0; k < members.Count; k++)
        {
            var m = members[k];
            BattleChara* bc = null;
            if (manager != null && PartyPick.Valid(m.EntityId)) bc = manager->LookupBattleCharaByEntityId(m.EntityId);
            var data = bc == null ? null : &((Character*)bc)->CharacterData;
            uint hp = data != null ? data->Health : 0, max = data != null ? data->MaxHealth : 0;
            members[k] = m with { Hp = hp, MaxHp = max, Near = bc != null, InCombat = data != null && data->InCombat, IsYou = m.EntityId == meId };
        }
        if (choice.From != PartyFrom.List) return;
        // The party list's HP holds at any distance: it's the one used for its members.
        foreach (var p in Services.Party)
            for (var k = 0; k < members.Count; k++)
                if (members[k].EntityId == p.EntityId)
                {
                    members[k] = members[k] with { Hp = p.CurrentHP, MaxHp = p.MaxHP };
                    break;
                }
    }

    /// <summary>
    /// The cross-world party as the game keeps it [verify: the groups, your group's index, each member's entity id as the
    /// object table has it], or null when it isn't read. Guarded: anything that makes no sense is left to
    /// <see cref="PartyPick.Choose"/> to refuse, and a read that throws stops it for a minute.
    /// </summary>
    private static CrossWorldRead? ReadCrossWorld()
    {
        if (Environment.TickCount64 < crossBrokenUntil) return null;
        try
        {
            var proxy = InfoProxyCrossRealm.Instance();
            if (proxy == null) return null;
            var inParty = proxy->IsInCrossRealmParty || proxy->IsCrossRealm;
            if (!inParty) return null;
            int groups = proxy->GroupCount;
            var list = new List<PartySeat>();
            var all = proxy->CrossRealmGroups;
            for (var g = 0; g < Math.Min(groups, Math.Min(all.Length, PartyPick.MostGroups)); g++)
            {
                ref var group = ref all[g];
                var seats = group.GroupMembers;
                var count = Math.Min((int)group.GroupMemberCount, seats.Length);
                for (var j = 0; j < count; j++)
                {
                    ref var m = ref seats[j];
                    list.Add(new PartySeat(m.EntityId, m.ContentId, m.NameString, m.ClassJobId, m.Level, (ushort)m.HomeWorld, g, j));
                }
            }
            return new CrossWorldRead(true, proxy->IsInAllianceRaid, proxy->LocalPlayerGroupIndex, groups, list);
        }
        catch (Exception ex)
        {
            crossBrokenUntil = Environment.TickCount64 + 60_000;
            Services.Log.Warning(ex, "Raid Planner couldn't read the cross-world party");
            if (Diagnostics is { On: true } d) d.Write("party-read", $"cross-world read failed: {ex.GetType().Name}");
            return null;
        }
    }

    /// <summary>A cross-world member the game gave no entity id for: found near you by their content id, else 0.</summary>
    private static uint Resolve(PartySeat seat)
    {
        if (seat.ContentId == 0) return 0;
        foreach (var o in Services.Objects.PlayerObjects)
            if (o is IPlayerCharacter pc && ContentIdOf(pc.Address) == seat.ContentId)
                return pc.EntityId;
        return 0;
    }

    private static ulong ContentIdOf(nint address) => address == 0 ? 0 : ((Character*)address)->ContentId;

    /// <summary>With Diagnostics on: who the party is, each time it changes (and once as Diagnostics is turned on).</summary>
    private static void Log(CrossWorldRead? cross)
    {
        if (Diagnostics is not { On: true } d)
        {
            loggedWhileOn = false;
            return;
        }
        var line = PartyPick.Describe(choice, cross, s => PartyPick.Valid(s.EntityId) && Services.Objects.SearchByEntityId(s.EntityId) != null);
        if (loggedWhileOn && line == lastLogged) return;
        loggedWhileOn = true;
        lastLogged = line;
        d.WriteText("party-read", line);
    }

    /// <summary>Forgets everything (the plugin unloading).</summary>
    public static void Reset()
    {
        members.Clear();
        listBuffer.Clear();
        choice = new PartyChoice(PartyFrom.Alone, Array.Empty<PartySeat>());
        frame = default;
        nextSeats = 0;
        Diagnostics = null;
        yourSpot = (long.MinValue, null);
        lastLogged = string.Empty;
        loggedWhileOn = false;
    }
}
