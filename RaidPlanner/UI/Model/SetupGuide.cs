using System;
using System.Collections.Generic;
using System.Linq;

namespace RaidPlanner.UI.Model;

/// <summary>What the setup guide's last step says of a feature: its settings that matter, in a few words.</summary>
public readonly record struct SetupFacts(bool Linked, int RecordLevel, int Keep, bool Compact, string Theme, bool MeterEverywhere, bool CooldownsEverywhere = false);

/// <summary>
/// The first-time setup guide's rules (0.8.0, the main window's Setup): its five steps, when Next can go on (the
/// Account step waits for a link only when a feature picked needs one; Do this later skips it), what the Account
/// step says of the picks that need an account, and the Done step's line for each feature.
/// </summary>
public static class SetupGuide
{
    public const int Welcome = 0;
    public const int FeaturesStep = 1;
    public const int Account = 2;
    public const int Look = 3;
    public const int Done = 4;

    public static readonly IReadOnlyList<string> Steps = new[] { "Welcome", "Features", "Account", "Look", "Done" };

    public static int Clamp(int step) => Math.Clamp(step, Welcome, Done);

    /// <summary>The features on that do nothing until an account is linked, in the guide's order.</summary>
    public static List<Feature> NeedingAccount(FeatureSet set) => Features.All.Where(f => set[f] && Features.NeedsAccount(f)).ToList();

    /// <summary>Next goes on: always, but on the Account step while a feature picked needs an account not linked yet (Do this later skips it).</summary>
    public static bool CanGoOn(int step, FeatureSet set, bool linked) => step != Account || linked || NeedingAccount(set).Count == 0;

    /// <summary>"5 of 6 on".</summary>
    public static string CountLine(FeatureSet set) => $"{set.Count} of {Features.All.Count} on";

    /// <summary>
    /// The Account step's lead: which picks need an account, their names to stand out ("Two of your picks need a Raid
    /// Codex account: Upload my logs and Track my progress. Linking takes a token from the site, once."), or that none does.
    /// </summary>
    public static IReadOnlyList<Said> AccountLead(FeatureSet set)
    {
        var need = NeedingAccount(set);
        if (need.Count == 0)
            return new[] { new Said("None of your picks needs an account, so this is up to you: link one now to send pulls to your Raid Codex, or later in Settings › Account.") };
        var said = new List<Said>
        {
            new(need.Count == 1 ? "One of your picks needs a Raid Codex account: " : $"{PullGist.Number(need.Count)} of your picks need a Raid Codex account: "),
        };
        for (var i = 0; i < need.Count; i++)
        {
            if (i > 0) said.Add(new Said(i == need.Count - 1 ? " and " : ", "));
            said.Add(new Said(Features.Name(need[i]), true));
        }
        said.Add(new Said(". Linking takes a token from the site, once."));
        return said;
    }

    /// <summary>The Done step's line under a feature: what it'll do, as set ("Compact · Ready Check · in duties"), or where to turn it on.</summary>
    public static string DoneNote(Feature f, bool on, SetupFacts s)
    {
        if (!on) return "Turn it on any time in Settings › Features";
        return f switch
        {
            Feature.Record => $"{RecordLevels.Label(s.RecordLevel)} · keeps the last {s.Keep}",
            Feature.Upload => s.Linked ? "Linked · public, as on FF Logs" : "Waits for your account: Settings › Account",
            Feature.Meter => $"{(s.Compact ? "Compact" : "Detailed")} · {s.Theme} · {(s.MeterEverywhere ? "everywhere" : "in duties")}",
            Feature.Plan => "Shows your plan for the duty you’re in",
            Feature.Cooldowns => $"Your party’s raid buffs and mitigation, {(s.CooldownsEverywhere ? "everywhere" : "in duties")}",
            _ => s.Linked ? "How far each pull got, on the site" : "Waits for your account: Settings › Account",
        };
    }

    /// <summary>Home's chip for a feature: its short name ("Recording"; the plan's own name for Plan when there's one for the duty).</summary>
    public static string ChipName(Feature f, string? plan = null) => f switch
    {
        Feature.Record => "Recording",
        Feature.Upload => "Uploading",
        Feature.Meter => "Meter",
        Feature.Plan => plan is { Length: > 0 } ? $"Plan: {plan}" : "Plan",
        Feature.Cooldowns => "Cooldowns",
        _ => "Progress",
    };
}
