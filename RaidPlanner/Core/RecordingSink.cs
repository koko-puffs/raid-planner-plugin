using System;
using RaidPlanner.Core.Meter;

namespace RaidPlanner.Core;

/// <summary>
/// The combat parser's findings into a pull's combat block, by the recording's numbers (times from the
/// pull's start): the party's effects, their casts finishing, and ticks (on the party; the party's; and
/// on an enemy in the recording from no one the game named; another player's on an enemy isn't kept),
/// each tick with the HP before it that the game side read (a HoT's target's, a party member's; −1
/// otherwise). The game side (<c>Game/FightRecorder</c>) registers it with the parser while a pull is
/// recorded, next to the meter's session; game-free, so the tests feed it as the game does.
/// </summary>
public sealed class RecordingSink : ICombatSink
{
    private readonly Func<RecordingBuilder?> builder;
    private readonly Func<double> pullStart;

    /// <param name="builder">The pull being recorded (null for none).</param>
    /// <param name="pullStart">When it began, on the parser's clock.</param>
    public RecordingSink(Func<RecordingBuilder?> builder, Func<double> pullStart)
    {
        this.builder = builder;
        this.pullStart = pullStart;
    }

    public void Used(double t, uint member, uint caster, uint action)
    {
        if (member == caster && builder() is { } b && b.PartyId(member) is { } id) b.PartyUsed(t - pullStart(), id, action);
    }

    public void Effect(in PartyEffect e)
    {
        if (builder() is not { } b || b.PartyId(e.Member) is not { } origin) return;
        var kind = (int)e.Kind | (int)e.Flags;
        b.CombatFx(e.T - pullStart(), e.Action, origin, b.Who(e.Recipient), e.Amount, kind, e.Kind == FxKind.Heal ? e.HpBefore : -1);
    }

    public void Taken(in TakenHit e)
    {
        // The enemies' hits are the events' (the game side's OnHit), with what each did.
    }

    public void Tick(in TickEvent e, ReadOnlySpan<TickShare> shares)
    {
        if (builder() is not { } b) return;
        var on = b.Who(e.Target);
        if (on == 0) return;
        int src;
        if (e.SourceParty) src = b.PartyId(e.Source) ?? 0;
        else if (e.TargetParty) src = e.Source == 0 ? 0 : b.KnownActor(e.Source) ?? 0;
        else if (e.Source == 0) src = 0;
        else return; // someone else's DoT on an enemy
        b.CombatTick(e.T - pullStart(), on, src, e.Status, e.Amount, e.Heal, e.HpBefore);
    }
}
