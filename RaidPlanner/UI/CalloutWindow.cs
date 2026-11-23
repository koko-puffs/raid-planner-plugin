using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Client.UI;
using RaidPlanner.Core;
using RaidPlanner.Game;
using RaidPlanner.UI.Model;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>
/// The on-screen callouts (Settings → Plan window, `/raidcodex callouts`): while your plan follows a
/// pull, its next few steps over the fight, each with a countdown to when it goes off (the plan's
/// own times, as the cue follower moves its clock: <see cref="PlanService.PlanClock"/>). Since 0.8.2 the
/// first on a card, its countdown in a ring that empties as it comes, the mechanic's name over its
/// callout (Archivo Expanded ExtraBold in capitals) and your cooldowns in a chip; the next ones as rows
/// under it (<see cref="DrawContent"/>). An optional sound a few seconds before each step: one of the
/// game's own &lt;se.N&gt; sound effects, once a step, a pull. A sample shows under a Preview tag.
/// </summary>
public sealed class CalloutWindow : OverlayWindow
{
    private readonly PlanService? plans;
    private readonly CalloutSounds sounds = new();
    private readonly long started = Stopwatch.GetTimestamp();
    private List<CalloutLine> lines = new();

    /// <summary>The plan followed, read once (its steps' times and words, in time order).</summary>
    private CalloutPlan? read;
    private DateTime soundFailedAt;

    /// <summary>The countdowns' words, made again only when their second changes.</summary>
    private readonly Dictionary<int, (int Seconds, string Text)> countdowns = new();

    public CalloutWindow(Configuration config, Fonts fonts, PlanService? plans)
        : base("Callouts###RaidPlannerCallouts", config, fonts, () => config.CalloutOverlay, 460, new Vector2(0.5f, 0.2f), ArrangePart.Callouts) => this.plans = plans;

    protected override string What => "Raid Codex's callouts couldn't draw";

    private bool Compact => Settings.Compact;

    /// <summary>Your spot now (set by the plugin; since 0.5.0, for your planned cooldowns under the first step: "You: Reprisal").</summary>
    public Func<string?>? YourSpot { get; set; }

    /// <summary>A new pull: every step's sound can play again.</summary>
    public void NewPull() => sounds.Reset();

    protected override bool Wanted()
    {
        var ahead = Math.Clamp(Config.CalloutAhead, 5, 60);
        var count = Math.Clamp(Config.CalloutCount, 1, 5);
        if (Previewing)
        {
            lines = CalloutSample.At((Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency, ahead, count);
            return true;
        }
        if (!Settings.Show || plans?.Plan is not { } plan || !plans.Following || plans.PlanClock is not { } clock) return false;
        var cp = read is { } r && ReferenceEquals(r.Plan, plan) ? r : read = new CalloutPlan(plan);
        if (Config.CalloutSound && sounds.Due(plan, cp.Times, clock, Math.Clamp(Config.CalloutSoundLead, 1, 10)) != null) Play(Config.CalloutSoundId);
        // Into the list kept here: nothing made each frame but the lines.
        CalloutTimeline.Upcoming(cp, clock, ahead, count, lines, YourSpot?.Invoke());
        return lines.Count > 0;
    }

    // Sizes (CSS px; smaller callouts' in brackets), since 0.8.2: the window's 12 down and 14 in; the first step on a card
    // (12 [8] over and under, 12 [9] in at its left, 14 [12] at its right, 12 corners), its countdown in a ring 64 [44]
    // wide, 14 [11] from its words; the next steps as rows 32 [26] tall, 4 apart, 8 under the card, their times centred
    // under the ring.

    private float Ring => Compact ? 44 : 64;

    private float RingStroke => Compact ? 3 : 4;

    private float CardPadY => Compact ? 8 : 12;

    private float CardPadL => Compact ? 9 : 12;

    private float CardPadR => Compact ? 12 : 14;

    private float RingGap => Compact ? 11 : 14;

    private float RowH => Compact ? 26 : 32;

    private const float RowGap = 4;

    private const float CardToRows = 8;

    /// <summary>The "You" chip's height (CSS px) and its gap over it.</summary>
    private const float ChipH = 22, ChipGap = 8;

    /// <summary>The first step's callout: 30 (22 smaller); Chinese or Korean, 22 (16), the sizes built with their glyphs.</summary>
    private int BigPx(string text) => BigFits(text) ? Compact ? 22 : 30 : Compact ? 16 : 22;

    /// <summary>The next steps' words: SansSemibold 15 (13 smaller, and for Chinese or Korean: the size built with their glyphs).</summary>
    private int RowPx(string text) => Compact || !BigFits(text) ? 13 : 15;

    /// <summary>The 30px callout has latin glyphs only (and a Japanese client's): text in Chinese or Korean is drawn at 22, which has them.</summary>
    private static bool BigFits(string text)
    {
        if (Txt.Fonts.Japanese) return true;
        foreach (var c in text)
            if (c >= 0x2E80)
                return false;
        return true;
    }

    /// <summary>The first step's words' width (CSS px) in a window this wide.</summary>
    private float CardTextCss(float widthCss) => widthCss - 14 * 2 - CardPadL - CardPadR - Ring - RingGap;

    /// <summary>The mechanic's name over the callout (CSS px tall): the eyebrow's mono capitals, or Sans 12 for words they have no glyphs for.</summary>
    private static float NameCss(string name) => BigFits(name) ? Kit.EyebrowH / S(1) : 12 * 1.3f;

    /// <summary>The first step's words (CSS px tall): the name, the callout (two lines at most), your cooldowns' chip.</summary>
    private float CardTextH(CalloutLine l, float tw)
    {
        var h = Kit.CalloutHeight(l.Big, BigPx(l.Big), tw, 2, 1.04f) / S(1);
        if (l.Name != null) h += NameCss(l.Name) + 3;
        if (l.You != null) h += ChipGap + ChipH;
        return h;
    }

    private float CardH(CalloutLine l, float tw) => MathF.Max(Ring, CardTextH(l, tw)) + CardPadY * 2;

    protected override float Measure(float widthCss)
    {
        var h = 12f + (Previewing ? 24 : 0);
        if (lines.Count > 0) h += CardH(lines[0], S(CardTextCss(widthCss)));
        var rows = lines.Count - 1;
        if (rows > 0) h += CardToRows + rows * RowH + (rows - 1) * RowGap;
        return h + 12;
    }

    /// <summary>
    /// Since 0.8.2 (until then a countdown column beside the words, the first big and the rest in smaller capitals): the
    /// first step on a card of its own, its countdown in a ring that empties as it comes (ember in its last seconds, the
    /// card washed ember; at its time, "NOW" on an ember disc and the card's edge ember), the mechanic's name over the
    /// callout and your cooldowns in an ember chip under it; the next steps as quiet rows, their times under the ring.
    /// The card and rows are a little more solid than the window's background, so the words read over a busy fight.
    /// </summary>
    protected override void DrawContent(ImDrawListPtr dl, Vector2 top, float w, float h)
    {
        var left = top.X + S(14);
        var right = top.X + w - S(14);
        var y = top.Y + S(12);
        if (Previewing)
        {
            Kit.Eyebrow(dl, new Vector2(left, y + (S(18) - Kit.EyebrowH) / 2), "Callouts");
            PreviewTag(dl, new Vector2(right - Kit.TagWidth("Preview", TagKind.Plain), y), "##co-preview");
            y += S(24);
        }
        if (lines.Count == 0) return;
        var ahead = Math.Clamp(Config.CalloutAhead, 5, 60);
        var lead = Config.CalloutSound ? Math.Clamp(Config.CalloutSoundLead, 1, 10) : 3;
        var bg = Math.Clamp(Settings.Background, 0, 1);
        var ringX = left + S(CardPadL) + S(Ring) / 2;
        var textX = left + S(CardPadL) + S(Ring) + S(RingGap);

        // The first step's card.
        var l = lines[0];
        var tw = S(CardTextCss(w / S(1)));
        var cardH = S(CardH(l, tw));
        var min = new Vector2(left, y);
        var max = new Vector2(right, y + cardH);
        var now = l.Now;
        var soon = !now && l.In <= lead;
        var r = S(12);
        dl.AddRectFilled(min, max, Palette.U32(Palette.Rgba(24, 24, 29, Math.Clamp(0.55f + 0.4f * bg, 0, 0.95f))), r);
        if (now || soon) dl.AddRectFilled(min, max, Palette.U32(Palette.WithAlpha(Palette.Ember, now ? 0.2f : 0.11f)), r);
        if (now) dl.AddRect(min - new Vector2(S(2.5f)), max + new Vector2(S(2.5f)), Palette.U32(Palette.WithAlpha(Palette.Ember, 0.16f)), r + S(2.5f), ImDrawFlags.None, S(3));
        dl.AddRect(min, max, Palette.U32(now ? Palette.Ember : soon ? Palette.EmberLine : Palette.Line2), r, ImDrawFlags.None, 1);
        CountdownRing(dl, new Vector2(ringX, y + cardH / 2), l, now, soon, ahead);
        // The words, centred down the card beside the ring.
        var ty = y + (cardH - S(CardTextH(l, tw))) / 2;
        if (l.Name != null)
        {
            var nameInk = now || soon ? Palette.EmberHi : Palette.Faint;
            if (BigFits(l.Name)) Kit.Eyebrow(dl, new Vector2(textX, ty), l.Name, null, tw, nameInk);
            else Txt.Line(dl, new Vector2(textX, ty), Txt.Fit(l.Name, Face.Sans, 12, tw), Face.Sans, 12, nameInk, 12 * 1.3f);
            ty += S(NameCss(l.Name) + 3);
        }
        ty += Kit.Callout(dl, new Vector2(textX, ty), l.Big, BigPx(l.Big), tw, 2, 1.04f, Palette.White);
        if (l.You != null) YouChip(dl, new Vector2(textX, ty + S(ChipGap)), l.You, tw);
        y += cardH;

        // The next steps.
        if (lines.Count > 1) y += S(CardToRows);
        var rowInk = Palette.Rgba(13, 13, 16, Math.Clamp(0.3f + 0.4f * bg, 0, 0.9f));
        var rowH = S(RowH);
        for (var i = 1; i < lines.Count; i++)
        {
            var n = lines[i];
            dl.AddRectFilled(new Vector2(left, y), new Vector2(right, y + rowH), Palette.U32(rowInk), S(8));
            RowTime(dl, ringX, y, n, n.In <= lead);
            var px = RowPx(n.Big);
            var room = right - S(CardPadR) - textX;
            var said = Txt.Fit(n.Big, Face.SansSemibold, px, room);
            var used = Txt.Line(dl, new Vector2(textX, y), said, Face.SansSemibold, px, n.Now ? Palette.White : Palette.Text, RowH);
            // Its mechanic's name after it, faint, where there's room.
            if (n.Name != null && room - used - S(8) >= S(40))
                Txt.Line(dl, new Vector2(textX + used + S(8), y), Txt.Fit(n.Name, Face.Sans, 13, room - used - S(8)), Face.Sans, 13, Palette.Faint, RowH);
            y += rowH + S(RowGap);
        }
    }

    /// <summary>
    /// The first step's countdown: a ring on a dark well that empties as the step comes (its arc the time left of the
    /// time looked ahead, from twelve o'clock), the seconds in it with a small "s"; ember in its last seconds; at its
    /// time an ember disc with "NOW".
    /// </summary>
    private void CountdownRing(ImDrawListPtr dl, Vector2 c, CalloutLine l, bool now, bool soon, double ahead)
    {
        var outer = S(Ring) / 2;
        if (now)
        {
            dl.AddCircleFilled(c, outer - S(1), Palette.U32(Palette.Ember), 48);
            dl.AddCircle(c, outer + S(1), Palette.U32(Palette.WithAlpha(Palette.Ember, 0.35f)), 48, S(2));
            var npx = Compact ? 11 : 13;
            var nw = Txt.Width(NowWord, Face.MonoSemibold, npx);
            Txt.Draw(dl, new Vector2(c.X - nw / 2, c.Y - Txt.Natural(Face.MonoSemibold, npx) / 2), NowWord, Face.MonoSemibold, npx, Palette.OnEmber);
            return;
        }
        var stroke = S(RingStroke);
        var radius = outer - stroke / 2;
        dl.AddCircleFilled(c, radius, Palette.U32(Palette.Rgba(9, 9, 11, 0.6f)), 48);
        dl.AddCircle(c, radius, Palette.U32(Palette.Track), 48, stroke);
        var left = (float)Math.Clamp(l.In / ahead, 0, 1);
        if (left > 0.002f)
        {
            const float Top = -MathF.PI / 2;
            dl.PathArcTo(c, radius, Top, Top + MathF.Tau * left, Math.Max(3, (int)(48 * left)));
            dl.PathStroke(Palette.U32(Palette.Ember), ImDrawFlags.None, stroke);
        }
        var text = Countdown(l);
        var ink = soon ? Palette.Ember : Palette.White;
        var unitInk = soon ? Palette.EmberHi : Palette.Muted;
        if (text.Length <= 2)
            Seconds(dl, c, text, Compact ? 18 : 30, Compact ? 10 : 11, ink, unitInk);
        else
        {
            // "1:05": smaller, with no "s".
            var px = Compact ? 12 : 16;
            var tw = Txt.Width(text, Face.MonoSemibold, px);
            Txt.Draw(dl, new Vector2(c.X - tw / 2, c.Y - Txt.Natural(Face.MonoSemibold, px) / 2), text, Face.MonoSemibold, px, ink);
        }
    }

    private const string NowWord = "NOW";

    /// <summary>A step's seconds and a small "s" after them, centred on <paramref name="c"/>, their baselines lined up.</summary>
    private static void Seconds(ImDrawListPtr dl, Vector2 c, string text, int px, int unitPx, Vector4 ink, Vector4 unitInk)
    {
        var nw = Txt.Width(text, Face.MonoSemibold, px);
        var uw = Txt.Width("s", Face.Mono, unitPx);
        var x = c.X - (nw + S(1) + uw) / 2;
        var top = c.Y - Txt.Natural(Face.MonoSemibold, px) / 2;
        Txt.Draw(dl, new Vector2(x, top), text, Face.MonoSemibold, px, ink);
        var baseline = top + Txt.Ascent(Face.MonoSemibold, px);
        Txt.Draw(dl, new Vector2(x + nw + S(1), baseline - Txt.Ascent(Face.Mono, unitPx)), "s", Face.Mono, unitPx, unitInk);
    }

    /// <summary>A next step's time, centred under the ring: muted, ember in its last seconds; "NOW" ember.</summary>
    private void RowTime(ImDrawListPtr dl, float cx, float rowTop, CalloutLine n, bool soon)
    {
        var c = new Vector2(cx, rowTop + S(RowH) / 2);
        var text = Countdown(n);
        var px = Compact ? 13 : 15;
        if (n.Now || text.Length > 2)
        {
            var tpx = Compact ? 11 : 12;
            var tw = Txt.Width(text, Face.MonoSemibold, tpx);
            Txt.Draw(dl, new Vector2(cx - tw / 2, c.Y - Txt.Natural(Face.MonoSemibold, tpx) / 2), text, Face.MonoSemibold, tpx, n.Now ? Palette.Ember : Palette.Muted);
            return;
        }
        Seconds(dl, c, text, px, Compact ? 10 : 11, soon ? Palette.Ember : Palette.Muted, soon ? Palette.EmberHi : Palette.Faint);
    }

    /// <summary>"You: Reprisal" as a chip: an ember-washed pill with an ember edge, "YOU" in ember mono capitals, what to use in white.</summary>
    private void YouChip(ImDrawListPtr dl, Vector2 at, string text, float width)
    {
        const string Lead = "You:";
        if (!ReferenceEquals(youOf, text))
        {
            youOf = text;
            youRest = text.StartsWith(Lead, StringComparison.Ordinal) ? text[Lead.Length..].Trim() : text;
        }
        var h = S(ChipH);
        var lw = Txt.SpacedWidth(YouWord, Face.Mono, 10, 0.09f);
        var inner = S(7) + lw + S(6);
        var rest = Txt.Fit(youRest, Face.SansSemibold, YouPx, MathF.Max(0, width - inner - S(9)));
        var cw = inner + Txt.Width(rest, Face.SansSemibold, YouPx) + S(9);
        var max = at + new Vector2(MathF.Min(cw, width), h);
        dl.AddRectFilled(at, max, Palette.U32(Palette.EmberWash), h / 2);
        dl.AddRect(at, max, Palette.U32(Palette.EmberLine), h / 2, ImDrawFlags.None, 1);
        Txt.Spaced(dl, new Vector2(at.X + S(7), at.Y + (h - Txt.Natural(Face.Mono, 10)) / 2), YouWord, Face.Mono, 10, Palette.Ember, 0.09f);
        Txt.Line(dl, new Vector2(at.X + inner, at.Y), rest, Face.SansSemibold, YouPx, Palette.White, ChipH);
    }

    /// <summary>"You: …" is SansSemibold 13 (the plan's own words: a face built with every language's glyphs).</summary>
    private const int YouPx = 13;

    private const string YouWord = "YOU";

    private string? youOf;
    private string youRest = string.Empty;

    private string Countdown(CalloutLine l)
    {
        // "Now" is its own (−1): a countdown a hair over 0 reads "0", not the "NOW" kept for it.
        var seconds = l.Now ? -1 : (int)Math.Ceiling(l.In - 1e-9);
        if (countdowns.TryGetValue(l.Step, out var had) && had.Seconds == seconds) return had.Text;
        if (countdowns.Count > 256) countdowns.Clear();
        var text = CalloutTimeline.Countdown(l.In);
        if (text == "Now") text = Txt.Caps(text);
        countdowns[l.Step] = (seconds, text);
        return text;
    }

    /// <summary>
    /// One of the game's own sound effects, as &lt;se.N&gt; in a macro (1–16), through the game's UI
    /// (<c>UIGlobals.PlayChatSoundEffect</c>, as the plugin's chat callouts' chime), when the game's
    /// function for it is there (never called through a missing address).
    /// </summary>
    public unsafe void Play(int effect)
    {
        try
        {
            if ((nint)UIGlobals.MemberFunctionPointers.PlaySoundEffect == 0) return;
            UIGlobals.PlayChatSoundEffect((uint)Math.Clamp(effect, 1, 16));
        }
        catch (Exception ex)
        {
            if (DateTime.UtcNow - soundFailedAt < TimeSpan.FromMinutes(1)) return;
            soundFailedAt = DateTime.UtcNow;
            Services.Log.Warning(ex, "Raid Codex couldn't play the callouts' sound");
        }
    }
}
