using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;

namespace RaidPlanner.Core;

/// <summary>
/// A kept pull in a few numbers, for the site's progress tracker (<c>rp_plugin_pull</c>, contract
/// §3): the duty, the pull, how long and how it ended, how far it got (<c>reached</c>, the length;
/// <c>phase</c>, the furthest phase from 1) and who died to what. No names, only spots and jobs.
/// </summary>
public static class PullSummary
{
    /// <summary>The most deaths sent (the site keeps as many).</summary>
    public const int MaxDeaths = 48;

    public static JsonObject Build(Recording r, int phase)
    {
        var party = r.Party.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());
        var deaths = new JsonArray();
        foreach (var d in r.Events.OfType<DeathEvent>().OrderBy(d => d.T))
        {
            if (deaths.Count >= MaxDeaths) break;
            party.TryGetValue(d.Who, out var who);
            var by = d.By != 0 && r.Actions.TryGetValue(d.By, out var a) && !string.IsNullOrWhiteSpace(a.Name) ? a.Name.Trim() : null;
            deaths.Add(new JsonObject
            {
                ["t"] = CastEvent.Round(d.T, 2),
                ["slot"] = who?.Slot ?? string.Empty,
                ["job"] = who?.Job ?? string.Empty,
                ["by"] = by,
            });
        }
        var duration = CastEvent.Round(r.Duration, 2);
        return new JsonObject
        {
            ["content"] = r.Duty.Content,
            ["territory"] = r.Duty.Territory,
            ["duty"] = r.Duty.Name,
            ["pull"] = Math.Max(1, r.Pull),
            ["recordedAt"] = r.RecordedAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["duration"] = duration,
            ["outcome"] = Words.OutcomeKey(r.Outcome),
            ["reached"] = duration,
            ["phase"] = Math.Max(1, phase),
            ["deaths"] = deaths,
        };
    }
}

/// <summary>
/// Your party as the site's roster (<c>rp_plugin_roster</c>): spot → name, for the eight spots
/// only (MT, OT, H1, H2, M1, M2, R1, R2), given as the recording gives them (<see cref="Roles"/>).
/// Sent only when you press Send my party to my roster.
/// </summary>
public static class PartyRoster
{
    public static readonly string[] Spots = { "MT", "OT", "H1", "H2", "M1", "M2", "R1", "R2" };

    public const int MaxName = 40;

    /// <summary>The fewest members sent: fewer is a party the game isn't showing (it'd replace the roster with just you).</summary>
    public const int Fewest = 2;

    /// <summary>
    /// Why the party can't be sent as it's read, or null when it can. Outside a duty, a cross-world
    /// party isn't in the game's party list, so all that's read is you (or no one).
    /// </summary>
    public static string? WhyNot(int members) => members < Fewest ? "Your party can't be read here; try in the duty." : null;

    /// <param name="party">Each member's name and role (1 tank, 2 melee, 3 ranged, 4 healer), in the party list's order.</param>
    public static JsonObject Build(IReadOnlyList<(string Name, byte Role)> party)
    {
        var partiers = party.Select((m, i) => new Partier((ulong)i + 1, string.Empty, m.Role)).ToList();
        var roster = new JsonObject();
        foreach (var (who, slot) in Roles.Assign(partiers))
        {
            if (Array.IndexOf(Spots, slot) < 0 || roster.ContainsKey(slot)) continue;
            var name = (party[(int)who.Key - 1].Name ?? string.Empty).Trim();
            if (name.Length == 0) continue;
            roster[slot] = name.Length <= MaxName ? name : name[..MaxName].TrimEnd();
        }
        return roster;
    }
}

/// <summary>
/// Pulls' summaries waiting to go to the progress tracker: at most <c>most</c> (past it the oldest
/// goes), sent one at a time from the oldest. The one on its way is known by its client id, so when
/// its answer comes it's taken off only if it's still first (the oldest may have been dropped meanwhile).
/// </summary>
public sealed class SummaryQueue
{
    private readonly int most;
    private readonly Queue<(string Summary, string ClientId)> queue = new();
    private string? inFlight;

    public SummaryQueue(int most)
    {
        this.most = Math.Max(1, most);
    }

    public int Count => queue.Count;

    /// <summary>One is on its way.</summary>
    public bool Sending => inFlight != null;

    public void Add(string summary, string clientId)
    {
        while (queue.Count >= most) queue.Dequeue();
        queue.Enqueue((summary, clientId));
    }

    /// <summary>The next to send, now on its way; null while one is already, or with none waiting.</summary>
    public (string Summary, string ClientId)? Next()
    {
        if (inFlight != null || queue.Count == 0) return null;
        var next = queue.Peek();
        inFlight = next.ClientId;
        return next;
    }

    /// <summary>The call's answer: <paramref name="done"/> takes it off (sent, or refused for good), else it waits to go again.</summary>
    public void Answered(bool done)
    {
        if (inFlight == null) return;
        if (done && queue.Count > 0 && queue.Peek().ClientId == inFlight) queue.Dequeue();
        inFlight = null;
    }

    /// <summary>None waits any more (the one on its way still has its answer heard).</summary>
    public void Clear() => queue.Clear();
}
