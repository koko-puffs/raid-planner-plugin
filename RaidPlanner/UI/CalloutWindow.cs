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
/// The on-screen callouts (Settings → Plan window, `/raidplan callouts`): while your plan follows a
/// pull, its next few steps over the fight, each with a countdown to when it goes off (the plan's
/// own times, as the cue follower moves its clock: <see cref="PlanService.PlanClock"/>). The first big
/// and clear, as Present's callout (Archivo Expanded ExtraBold at 30px in capitals, its countdown in
/// mono 30 and ember in its last seconds or "Now", the step's name under it, a slim cast bar running to
/// it); the next ones smaller under it. An optional sound a few seconds before each step: one of the
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
        : base("Callouts###RaidPlannerCallouts", config, fonts, () => config.CalloutOverlay, 460, new Vector2(0.5f, 0.2f)) => this.plans = plans;

    protected override string What => "Raid Planner's callouts couldn't draw";

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

    // Sizes (CSS px): 12 down and 14 in; the first step's countdown column 70 wide (54 compact), the rest 46 (38).

    private float FirstPx(string text) => Compact || !BigFits(text) ? 22 : 30;

    private int NextPx => Compact ? 16 : 17;

    private float Col(bool first) => first ? Compact ? 54 : 70 : Compact ? 38 : 46;

    /// <summary>The 30px callout has latin glyphs only (and a Japanese client's): text in Chinese or Korean is drawn at 22, which has them.</summary>
    private static bool BigFits(string text)
    {
        if (Txt.Fonts.Japanese) return true;
        foreach (var c in text)
            if (c >= 0x2E80)
                return false;
        return true;
    }

    private float TextWidth(float widthCss, bool first) => widthCss - 14 * 2 - Col(first) - 12;

    protected override float Measure(float widthCss)
    {
        var h = 12f + (Previewing ? 24 : 0);
        for (var i = 0; i < lines.Count; i++)
        {
            var first = i == 0;
            var l = lines[i];
            var px = first ? (int)FirstPx(l.Big) : NextPx;
            var tw = S(TextWidth(widthCss, first));
            h += Kit.CalloutHeight(l.Big, px, tw, first ? 2 : 1, 1.02f) / S(1);
            if (first && l.Name != null) h += 4 + 12 * 1.3f;
            if (first && l.You != null) h += 5 + YouPx * 1.3f;
            if (first) h += 8 + 3;
            h += i < lines.Count - 1 ? (first ? 12 : 8) : 0;
        }
        return h + 12;
    }

    protected override void DrawContent(ImDrawListPtr dl, Vector2 top, float w, float h)
    {
        var left = top.X + S(14);
        var y = top.Y + S(12);
        if (Previewing)
        {
            Kit.Eyebrow(dl, new Vector2(left, y + (S(18) - Kit.EyebrowH) / 2), "Callouts");
            PreviewTag(dl, new Vector2(top.X + w - S(14) - Kit.TagWidth("Preview", TagKind.Plain), y), "##co-preview");
            y += S(24);
        }
        var ahead = Math.Clamp(Config.CalloutAhead, 5, 60);
        var lead = Config.CalloutSound ? Math.Clamp(Config.CalloutSoundLead, 1, 10) : 3;
        for (var i = 0; i < lines.Count; i++)
        {
            var first = i == 0;
            var l = lines[i];
            var col = S(Col(first));
            var tx = left + col + S(12);
            var tw = S(TextWidth(w / S(1), first));
            var px = first ? (int)FirstPx(l.Big) : NextPx;
            var lineH = S(px * 1.02f);
            // The countdown, right-aligned in its column on the first line's middle: ember in its last seconds, "Now" ember.
            var soon = l.In <= lead;
            var cpx = first ? Compact ? 18 : 30 : Compact ? 13 : 16;
            var text = Countdown(l);
            var ch = Txt.Natural(Face.MonoSemibold, cpx);
            Txt.Draw(dl, new Vector2(left + col - Txt.Width(text, Face.MonoSemibold, cpx), y + (lineH - ch) / 2), text, Face.MonoSemibold, cpx,
                soon ? Palette.Ember : first ? Palette.White : Palette.Muted);
            // The callout, in capitals: the first big and white, the next smaller and quieter.
            var bigH = Kit.Callout(dl, new Vector2(tx, y), l.Big, px, tw, first ? 2 : 1, 1.02f, first ? Palette.White : Palette.Text);
            y += bigH;
            if (first)
            {
                if (l.Name != null)
                {
                    y += S(4);
                    Txt.Line(dl, new Vector2(tx, y), Txt.Fit(l.Name, Face.Sans, 12, tw), Face.Sans, 12, Palette.Muted, 12 * 1.3f);
                    y += S(12 * 1.3f);
                }
                // Your planned cooldowns in it (since 0.5.0): "You:" in ember, what to use in white.
                if (l.You != null)
                {
                    y += S(5);
                    YouLine(dl, new Vector2(tx, y), l.You, tw);
                    y += S(YouPx * 1.3f);
                }
                // A slim cast bar running to it over the time looked ahead.
                y += S(8);
                var fraction = l.Now ? 1 : (float)Math.Clamp(1 - l.In / ahead, 0, 1);
                Kit.CastBar(dl, new Vector2(tx, y), tw, fraction, 3);
                y += S(3);
            }
            if (i < lines.Count - 1) y += S(first ? 12 : 8);
        }
    }

    /// <summary>"You: …" is SansSemibold 13 (the plan's own words: a face built with every language's glyphs).</summary>
    private const int YouPx = 13;

    private string? youOf;
    private string youRest = string.Empty;

    /// <summary>"You: Reprisal": the "You:" in ember, the rest in white, cut to the width.</summary>
    private void YouLine(ImDrawListPtr dl, Vector2 at, string text, float width)
    {
        const string Lead = "You:";
        var px = YouPx;
        if (!text.StartsWith(Lead, StringComparison.Ordinal))
        {
            Txt.Line(dl, at, Txt.Fit(text, Face.SansSemibold, px, width), Face.SansSemibold, px, Palette.White, px * 1.3f);
            return;
        }
        var lw = Txt.Line(dl, at, Lead, Face.SansSemibold, px, Palette.Ember, px * 1.3f);
        // The rest cut off once a line, not each frame.
        if (!ReferenceEquals(youOf, text))
        {
            youOf = text;
            youRest = text[Lead.Length..];
        }
        var rest = youRest;
        Txt.Line(dl, at + new Vector2(lw, 0), Txt.Fit(rest, Face.SansSemibold, px, MathF.Max(0, width - lw)), Face.SansSemibold, px, Palette.White, px * 1.3f);
    }

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
            Services.Log.Warning(ex, "Raid Planner couldn't play the callouts' sound");
        }
    }
}
