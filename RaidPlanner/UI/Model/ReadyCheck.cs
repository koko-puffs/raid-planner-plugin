using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RaidPlanner.Core;

namespace RaidPlanner.UI.Model;

/// <summary>A party member's food as the ready check reads it: fed (and for how long), no food, or not seen (out of range, another zone).</summary>
public enum FoodState
{
    Fed,
    None,
    Unseen,
}

/// <summary>A ready check row's colour: green, amber (runs out before an average pull is over), red (no food), quiet (not seen).</summary>
public enum FoodTone
{
    Good,
    Short,
    Bad,
    Unknown,
}

/// <summary>A party member at a ready check (since 0.9.0): who, their job and role, their food and its seconds left, whether it's you.</summary>
public readonly record struct ReadyMember(string Name, string Job, string Role, FoodState Food, double Left, bool IsYou = false);

/// <summary>
/// Yours at a ready check (since 0.9.0): the tinctures and meals in your inventory, and your equipped gear's lowest condition
/// (0–100%; null when it couldn't be read).
/// </summary>
public readonly record struct ReadyYou(int Tinctures, int Meals, int? GearLowest);

/// <summary>A member's row: them, its colour, and its words ("52:10", "No food", "can't see").</summary>
public sealed record ReadyRow(ReadyMember Member, FoodTone Tone, string Text);

/// <summary>
/// The ready check's card (since 0.9.0): the rows, the headline ("7 of 8 fed · one runs out mid-pull"), the average pull's line,
/// whether it's the one-line state (everyone fed for longer than an average pull, gear fine) and its words, and your gear's
/// warning.
/// </summary>
public sealed record ReadyCard(IReadOnlyList<ReadyRow> Rows, string Headline, string? Average, bool Compact, string CompactTitle, string CompactLine, string? Gear,
    int Fed, int Short);

/// <summary>The ready check's card worked out (since 0.9.0), away from the game and ImGui so it's tested.</summary>
public static class ReadyCheckView
{
    /// <summary>Your gear is warned about when its lowest condition is under this (%).</summary>
    public const int GearLow = 20;

    private static readonly CultureInfo En = CultureInfo.InvariantCulture;

    private static readonly string[] Small = { "no", "one", "two", "three", "four", "five", "six", "seven", "eight" };

    /// <summary>A count in words up to eight ("one", "two"), then figures.</summary>
    public static string Number(int n) => n >= 0 && n < Small.Length ? Small[n] : n.ToString(En);

    /// <summary>
    /// The average length of your kept pulls of this duty (seconds), from the kept pulls' list (<see cref="RecordingInfo"/>): null
    /// when there are none (then nothing is amber, only the time is shown).
    /// </summary>
    public static double? AveragePull(IEnumerable<RecordingInfo> kept, string? duty)
    {
        if (string.IsNullOrEmpty(duty)) return null;
        double sum = 0;
        var n = 0;
        foreach (var r in kept)
        {
            if (r.Duration <= 0 || !string.Equals(r.Duty, duty, StringComparison.Ordinal)) continue;
            sum += r.Duration;
            n++;
        }
        return n == 0 ? null : sum / n;
    }

    /// <summary>Food's time left: "52:10", "6:05", "1:00:00".</summary>
    public static string FoodTime(double seconds) => Format.Clock(Math.Max(0, seconds));

    /// <summary>A row's colour: amber when the food would run out before an average pull is over (with one known).</summary>
    public static FoodTone Tone(ReadyMember m, double? average) => m.Food switch
    {
        FoodState.None => FoodTone.Bad,
        FoodState.Unseen => FoodTone.Unknown,
        _ => average is { } a && m.Left < a ? FoodTone.Short : FoodTone.Good,
    };

    /// <summary>The card for these members, your things and the average pull (null: none kept here).</summary>
    public static ReadyCard Card(IReadOnlyList<ReadyMember> members, ReadyYou you, double? average)
    {
        var rows = new List<ReadyRow>(members.Count);
        var fed = 0;
        var shortOnes = 0;
        var none = 0;
        var unseen = 0;
        double shortest = double.PositiveInfinity;
        foreach (var m in members)
        {
            var tone = Tone(m, average);
            var text = m.Food switch
            {
                FoodState.None => "No food",
                FoodState.Unseen => "can't see",
                _ => FoodTime(m.Left),
            };
            rows.Add(new ReadyRow(m, tone, text));
            switch (m.Food)
            {
                case FoodState.Fed:
                    fed++;
                    shortest = Math.Min(shortest, m.Left);
                    if (tone == FoodTone.Short) shortOnes++;
                    break;
                case FoodState.None:
                    none++;
                    break;
                default:
                    unseen++;
                    break;
            }
        }
        var all = members.Count;
        var headline = fed == all && all > 0 ? "Everyone's fed" : $"{fed.ToString(En)} of {all.ToString(En)} fed";
        if (shortOnes > 0) headline += $" · {Number(shortOnes)} {(shortOnes == 1 ? "runs" : "run")} out mid-pull";
        else if (unseen > 0 && none == 0) headline += $" · {Number(unseen)} out of sight";
        var averageLine = average is { } a ? $"Your average pull here is {Format.Clock(a)}." : null;
        var gear = GearWarning(you.GearLowest);
        var compact = all > 0 && fed == all && shortOnes == 0 && gear == null;
        var minutes = double.IsPositiveInfinity(shortest) ? 0 : (int)Math.Floor(shortest / 60);
        var shortestWords = minutes >= 1 ? $"The shortest has {Words.Count(minutes, "minute")} left" : "The shortest has less than a minute left";
        var tinctures = you.Tinctures > 0 ? $"you have {Words.Count(you.Tinctures, "tincture")}" : "you have no tinctures";
        return new ReadyCard(rows, headline, averageLine, compact, "Everyone's fed", $"{shortestWords} · {tinctures}", gear, fed, shortOnes);
    }

    /// <summary>Your gear's warning when its lowest condition is under <see cref="GearLow"/>%: "Gear 18%: repair soon" ("Gear 0%: broken, repair it").</summary>
    public static string? GearWarning(int? lowest) => lowest switch
    {
        null => null,
        <= 0 => "Gear 0%: broken, repair it",
        < GearLow => $"Gear {lowest.Value.ToString(En)}%: repair soon",
        _ => null,
    };

    /// <summary>"Tinctures ×14", "Food ×22".</summary>
    public static string Times(string what, int n) => $"{what} ×{Math.Max(0, n).ToString(En)}";
}

/// <summary>
/// When the ready check card shows (since 0.9.0), worked out away from the game so it's tested: from the frame a ready check is
/// seen going on (someone's answer still awaited), while it goes on, and <see cref="Linger"/> seconds after it ends; it goes at
/// once when the pull starts (combat). A ready check seen for longer than <see cref="Longest"/> counts as over (the game's
/// lasts 30 seconds; this keeps a reading that stuck from holding the card up). Pinned (mouse only), it stays until the pin is
/// pressed again. A ready check that starts in combat isn't shown.
/// </summary>
public sealed class ReadyCheckWatch
{
    /// <summary>The card stays this long after the ready check ends (seconds).</summary>
    public const double Linger = 30;

    /// <summary>A ready check seen going on longer than this is taken to be over (seconds).</summary>
    public const double Longest = 60;

    private bool was;
    private int wasAwaiting;
    private double startedAt = double.NaN;
    private double endedAt = double.NaN;

    /// <summary>The card shows.</summary>
    public bool Showing { get; private set; }

    /// <summary>The ready check is going on (as last read).</summary>
    public bool Checking { get; private set; }

    /// <summary>Kept by the pin.</summary>
    public bool Pinned { get; private set; }

    /// <summary>Seconds left before the card goes once the check is over (null while it goes on, or pinned).</summary>
    public double? Left { get; private set; }

    /// <summary>
    /// A frame at <paramref name="now"/> (seconds, a steady clock): whether a ready check is going on as the game says, and whether
    /// the party's in combat. True on the frame one starts (the card's facts are read again then).
    /// </summary>
    public bool Frame(double now, bool checking, bool inCombat) => Frame(now, checking ? 1 : 0, inCombat);

    /// <summary>
    /// As <see cref="Frame(double, bool, bool)"/>, with how many answers are awaited: more awaited than the frame before is a new
    /// check too, so one the game left "awaiting" (someone who never answered) doesn't hide every check after it.
    /// </summary>
    public bool Frame(double now, int awaiting, bool inCombat)
    {
        var started = false;
        var checking = awaiting > 0;
        if (checking && (!was || awaiting > wasAwaiting))
        {
            startedAt = now;
            endedAt = double.NaN;
            if (!inCombat)
            {
                Showing = true;
                started = true;
            }
        }
        was = checking;
        wasAwaiting = awaiting;
        var on = checking && now - startedAt <= Longest;
        if (Checking && !on) endedAt = now;
        Checking = on;
        Left = null;
        if (!Showing || Pinned) return started;
        if (inCombat)
        {
            Showing = false;
            return started;
        }
        if (!on)
        {
            if (double.IsNaN(endedAt)) endedAt = now;
            var left = Linger - (now - endedAt);
            if (left <= 0) Showing = false;
            else Left = left;
        }
        return started;
    }

    /// <summary>The pin pressed: kept, or (unpinned) going as it would have (after the check, a few seconds from now at least).</summary>
    public void TogglePin(double now)
    {
        Pinned = !Pinned;
        if (Pinned || Checking) return;
        // Unpinned after the check: it goes a few seconds from now, not at once.
        endedAt = Math.Max(double.IsNaN(endedAt) ? now : endedAt, now - Linger + 5);
    }

    /// <summary>The card goes now (and the pin's let go).</summary>
    public void Hide()
    {
        Showing = false;
        Pinned = false;
    }
}

/// <summary>The ready check's sample (Settings, Arrange windows): a made-up party, nothing real.</summary>
public static class ReadyCheckSample
{
    public static readonly ReadyMember[] Members =
    {
        new("Velka Moor", "PLD", "tank", FoodState.Fed, 3130, true),
        new("Rook Ashen", "DRK", "tank", FoodState.Fed, 2463),
        new("Koko Nyan", "WHM", "healer", FoodState.Fed, 2324),
        new("Odette Vale", "SCH", "healer", FoodState.Fed, 365),
        new("Ilya Wren", "DRG", "dps", FoodState.None, 0),
        new("Tamsin Brae", "NIN", "dps", FoodState.Fed, 1650),
        new("Fenn Hollow", "BRD", "dps", FoodState.Fed, 2652),
        new("Sylvie Moss", "SMN", "dps", FoodState.Fed, 2038),
    };

    public static readonly ReadyYou You = new(14, 22, 18);

    /// <summary>The sample's average pull: 6:40.</summary>
    public const double Average = 400;
}
