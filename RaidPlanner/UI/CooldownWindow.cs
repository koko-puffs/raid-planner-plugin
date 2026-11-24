using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using RaidPlanner.Core.Meter;
using RaidPlanner.UI.Model;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>
/// The party cooldown tracker (Settings → Cooldowns, `/raidcodex cooldowns`): small rows over the
/// fight, one a party member's tracked cooldown, grouped by kind (raid buffs, party mitigation, the
/// healers', Swiftcast…), then in spot order. Since 0.8.3 in the callouts' look (<see cref="DrawContent"/>):
/// a row: the action's own icon (dimmed while it recharges), the member's job, the action's name, and at
/// the right the time to ready in mono before a ring that empties as it comes back ("Ready" in green
/// with the ring whole; "1/2" before it for charges; yours ember). Yours are the game's own timers; the others' are
/// learned from their uses (<see cref="CooldownBook"/>), an estimate the row's tooltip says is one. The
/// limit break gauge last, when the party has one. A sample shows under a Preview tag. Since 0.5.0, while your plan follows a
/// pull, a cooldown the plan has its member use in the next 15 seconds is washed ember with a "PLAN 8 s" chip.
/// </summary>
public sealed class CooldownWindow : OverlayWindow
{
    private readonly PartyCooldowns? game;
    private List<CooldownLine> lines = new();

    /// <summary>When the rows were worked out last (the tracker's clock).</summary>
    private double linesAt = double.NegativeInfinity;
    private LimitBreakGauge? gauge;

    /// <summary>The gauge's bars, worked out again only when the gauge changes.</summary>
    private (LimitBreakGauge? Of, float[] Bars) gaugeBars = (null, Array.Empty<float>());
    private readonly long started = Stopwatch.GetTimestamp();

    /// <summary>The words drawn for each line, made again only when they change (no strings each frame).</summary>
    private readonly Dictionary<(uint Member, string Key), (int Seconds, int Charges, string Time, string? Count)> words = new();

    public CooldownWindow(Configuration config, Fonts fonts, PartyCooldowns? game)
        : base("Cooldowns###RaidPlannerCooldowns", config, fonts, () => config.Cooldowns, 250, new Vector2(0.14f, 0.32f), ArrangePart.Cooldowns) => this.game = game;

    /// <summary>In a duty (shown only there unless Everywhere).</summary>
    public Func<bool>? InDuty { get; set; }

    /// <summary>
    /// Your plan while it follows a pull, and its clock (set by the plugin; since 0.5.0): a row whose cooldown the plan has its
    /// member use in the next 15 seconds is outlined in ember with "planned in 8 s".
    /// </summary>
    public Func<(Core.Plan Plan, double Clock)?>? PlanNow { get; set; }

    /// <summary>The plan's cooldowns placed on its clock, read once a plan.</summary>
    private PlannedUses? planned;

    /// <summary>A planned use's chip's words by its second ("8 s"; −1: "now"), made once each.</summary>
    private readonly Dictionary<int, string> plannedWords = new();

    /// <summary>The limit break gauge now (the meter's), or null.</summary>
    public Func<LimitBreakGauge?>? Gauge { get; set; }

    protected override string What => "Raid Codex's cooldown tracker couldn't draw";

    private bool Compact => Settings.Compact;

    // Sizes (CSS px; smaller rows' in brackets), since 0.8.3 the callouts' look: 10 in all round; each cooldown a row 28
    // [22] tall on its own dark ground with 8px corners, 3 apart, 8 between kinds; the icon 20 [16], the job 16 [14], a
    // countdown ring 18 [14] at the right with the time before it.

    private float RowCss => Compact ? 22 : 28;

    private const float PadCss = 10;

    private const float RowGap = 3;

    private const float KindGap = 8;

    private float HeadCss => Previewing ? 24 : 0;

    private float IconCss => Compact ? 16 : 20;

    private float JobCss => Compact ? 14 : 16;

    private float RingCss => Compact ? 14 : 18;

    protected override bool Wanted()
    {
        var s = Settings;
        if (Previewing)
        {
            var t = (Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency;
            lines = CooldownSample.At(t, Config.CooldownPicks, Config.CooldownsMine, Config.CooldownsHideReady);
            linesAt = double.NegativeInfinity;
            gauge = Config.CooldownsGauge ? new LimitBreakGauge(2, 3, (int)(t * 220 % 10000), 10000) : null;
            return true;
        }
        // The game side listens while it's wanted (the plugin says so each frame: shown, in a duty or everywhere).
        if (game == null || !game.Wanted || !s.Show || (!s.Everywhere && InDuty?.Invoke() != true)) return false;
        // Worked out again 15 times a second, not every frame (the rows' bars move a pixel in half a second or so).
        var now = game.Now;
        if (now - linesAt >= 1 / 15.0 || now < linesAt)
        {
            linesAt = now;
            lines = CooldownView.Lines(game.Party, game.Action, Config.CooldownPicks, game.Book, game.Own, now, Config.CooldownsMine, Config.CooldownsHideReady,
                Planned(game), game.RecastOf);
        }
        gauge = Config.CooldownsGauge ? Gauge?.Invoke() : null;
        return lines.Count > 0 || gauge != null;
    }

    /// <summary>What the plan has each row's member use soon (null with no plan following a pull, or none planned).</summary>
    private Func<CooldownMember, CooldownSpec, CooldownAction, (PlannedUse Use, double In)?>? Planned(PartyCooldowns g)
    {
        if (PlanNow?.Invoke() is not { } now) return null;
        var uses = planned is { } p && ReferenceEquals(p.Plan, now.Plan) ? p : planned = new PlannedUses(now.Plan);
        if (uses.All.Count == 0) return null;
        var clock = now.Clock;
        return (m, spec, a) => uses.Next(m.Slot, c => CooldownView.Matches(c, spec, a, g.KeyOf), clock) is { } u ? (u, u.At - clock) : null;
    }

    /// <summary>The rows with their gaps (wider between kinds), the gauge's row, the padding; the head while previewing.</summary>
    protected override float Measure(float widthCss)
    {
        var rows = lines.Count + (gauge != null ? 1 : 0);
        if (rows == 0) return HeadCss + PadCss * 2;
        return HeadCss + PadCss * 2 + RowCss * rows + RowGap * (rows - 1) + KindBreaks() * (KindGap - RowGap);
    }

    /// <summary>How many times the kind changes down the rows (the gauge counts as one more).</summary>
    private int KindBreaks()
    {
        var n = 0;
        for (var i = 1; i < lines.Count; i++)
            if (lines[i].Spec.Kind != lines[i - 1].Spec.Kind)
                n++;
        return n + (gauge != null && lines.Count > 0 ? 1 : 0);
    }

    /// <summary>
    /// Since 0.8.3 in the callouts' look (until then rows on the window's ground with the recharge filling behind them and
    /// hairlines between kinds): each cooldown on a dark row of its own, its time in a ring at the right that empties as it
    /// comes back (yours ember, the others' white; ready, the ring whole and green with "Ready"), a little more space
    /// between kinds, and one the plan has its member use soon washed ember with an ember edge and a "PLAN" chip.
    /// </summary>
    protected override void DrawContent(ImDrawListPtr dl, Vector2 top, float w, float h)
    {
        var left = top.X + S(PadCss);
        var right = top.X + w - S(PadCss);
        var y = top.Y + S(PadCss);
        if (Previewing)
        {
            Kit.Eyebrow(dl, new Vector2(left, y + (S(18) - Kit.EyebrowH) / 2), "Cooldowns");
            PreviewTag(dl, new Vector2(right - Kit.TagWidth("Preview", TagKind.Plain), y), "##cd-preview");
            y += S(HeadCss);
        }
        var rowH = S(RowCss);
        var ground = Palette.Rgba(13, 13, 16, Math.Clamp(0.3f + 0.4f * Math.Clamp(Settings.Background, 0, 1), 0, 0.9f));
        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0) y += S(lines[i].Spec.Kind != lines[i - 1].Spec.Kind ? KindGap : RowGap);
            Row(dl, lines[i], left, y, right - left, rowH, ground);
            y += rowH;
        }
        if (gauge != null)
        {
            if (lines.Count > 0) y += S(KindGap);
            GaugeRow(dl, gauge, left, y, right - left, rowH, ground);
        }
    }

    /// <summary>A cooldown's row: its ground, the icon, the member's job (their spot in its tooltip), the name, a planned use's chip, the charges, the time and its ring.</summary>
    private void Row(ImDrawListPtr dl, CooldownLine l, float left, float y, float w, float h, Vector4 ground)
    {
        var compact = Compact;
        var min = new Vector2(left, y);
        var max = new Vector2(left + w, y + h);
        var r = S(8);
        dl.AddRectFilled(min, max, Palette.U32(ground), r);
        var planned = l.Planned;
        if (planned != null)
        {
            dl.AddRectFilled(min, max, Palette.U32(Palette.EmberWash), r);
            dl.AddRect(min, max, Palette.U32(Palette.EmberLine), r, ImDrawFlags.None, 1);
        }
        else if (l.Ready) dl.AddRectFilled(min, max, Palette.U32(Palette.WithAlpha(Palette.Ready, 0.06f)), r);
        if (Kit.Pointed(min, max)) Kit.Tooltip(CooldownView.Tip(l));
        var x = left + S(compact ? 6 : 8);
        DrawIcon(dl, l, new Vector2(x, y + (h - S(IconCss)) / 2), IconCss);
        x += S(IconCss) + S(7);
        PlateArt.JobBadge(dl, new Vector2(x + S(JobCss) / 2, y + h / 2), (int)JobCss, l.Who.Job, Core.Roles.RoleWord(l.Who.Role), Config.MeterRoleColours, false, l.Who.Slot);
        x += S(JobCss) + S(7);

        // At the right: the ring, the time before it, the charges before that.
        var ringR = S(RingCss) / 2;
        var rx = left + w - S(compact ? 7 : 9) - ringR;
        var ringInk = l.Ready ? Palette.Ready : l.Who.IsYou || planned != null ? Palette.Ember : Palette.WithAlpha(Palette.White, 0.75f);
        Kit.CountdownRing(dl, new Vector2(rx, y + h / 2), ringR, S(compact ? 2 : 2.5f), l.Ready ? 1 : 1 - l.Fill, ringInk);
        var edge = rx - ringR - S(6);
        var (time, count) = WordsOf(l);
        var tpx = compact ? 12 : 13;
        if (l.Ready) edge -= Txt.LineRight(dl, edge, y, time, Face.MonoSemibold, tpx, Palette.Ready, RowCss);
        else edge -= SecondsRight(dl, edge, y, time, tpx, compact ? 9 : 10, l.Who.IsYou ? Palette.EmberHi : Palette.White);
        if (count != null)
        {
            edge -= S(5);
            edge -= Txt.LineRight(dl, edge, y, count, Face.Mono, compact ? 9 : 10, Palette.Faint, RowCss);
        }
        // Planned soon (since 0.5.0; since 0.8.3 a chip): "PLAN 8 s".
        if (planned is { } p)
        {
            var chipH = compact ? 16f : 18f;
            var words = PlannedWords(p.In);
            var cw = Txt.SpacedWidth(PlanWord, Face.Mono, 9, 0.09f) + Txt.Width(words, Face.SansSemibold, 13) + S(16);
            if (edge - S(6) - cw - x >= S(28))
            {
                edge -= S(6) + cw;
                Kit.Chip(dl, new Vector2(edge, y + (h - S(chipH)) / 2), PlanWord, words, cw, chipH, 13, 9);
            }
        }
        var npx = compact ? 12 : 13;
        var room = edge - S(8) - x;
        if (room > S(16))
            Txt.Line(dl, new Vector2(x, y), Txt.Fit(l.Action.Name, Face.SansMedium, npx, room), Face.SansMedium, npx, l.Ready ? Palette.White : Palette.Text, RowCss);
    }

    private const string PlanWord = "PLAN";

    /// <summary>A time right-aligned at <paramref name="right"/> in its row: "42" with a small "s" after it, or "1:05" as it is. Returns its width.</summary>
    private float SecondsRight(ImDrawListPtr dl, float right, float rowTop, string text, int px, int unitPx, Vector4 ink)
    {
        var digits = text.Length <= 2;
        var uw = digits ? Txt.Width("s", Face.Mono, unitPx) + S(1) : 0;
        var tw = Txt.Width(text, Face.MonoSemibold, px);
        var top = rowTop + (S(RowCss) - Txt.Natural(Face.MonoSemibold, px)) / 2;
        Txt.Draw(dl, new Vector2(right - uw - tw, top), text, Face.MonoSemibold, px, ink);
        if (digits)
            Txt.Draw(dl, new Vector2(right - uw + S(1), top + Txt.Ascent(Face.MonoSemibold, px) - Txt.Ascent(Face.Mono, unitPx)), "s", Face.Mono, unitPx, Palette.Muted);
        return tw + uw;
    }

    private string PlannedWords(double inSeconds)
    {
        var key = inSeconds <= 0 ? -1 : (int)Math.Ceiling(inSeconds - 1e-9);
        if (plannedWords.TryGetValue(key, out var text)) return text;
        if (plannedWords.Count > 128) plannedWords.Clear();
        return plannedWords[key] = key < 0 ? "now" : key.ToString(System.Globalization.CultureInfo.InvariantCulture) + " s";
    }

    /// <summary>The row's words ("42", "Ready", "1:05"; "1/2"), made again only when the second or the charges change.</summary>
    private (string Time, string? Count) WordsOf(CooldownLine l)
    {
        var seconds = l.Ready ? -1 : (int)Math.Ceiling(l.ReadyIn - 1e-9);
        var key = (l.Who.Id, l.Spec.Key);
        if (words.TryGetValue(key, out var had) && had.Seconds == seconds && had.Charges == l.Charges) return (had.Time, had.Count);
        if (words.Count > 512) words.Clear();
        var made = (seconds, l.Charges, CooldownView.Time(l.ReadyIn), CooldownView.Charges(l));
        words[key] = made;
        return (made.Item3, made.Item4);
    }

    /// <summary>
    /// The action's own icon from the game (dimmed while it recharges), in a 6px-cornered square; while
    /// it loads, or for the sample, its kind's mark in a quiet well.
    /// </summary>
    private static void DrawIcon(ImDrawListPtr dl, CooldownLine l, Vector2 at, float size)
    {
        var s = S(size);
        if (l.Action.Icon != 0)
        {
            try
            {
                var wrap = Services.Textures.GetFromGameIcon(new GameIconLookup(l.Action.Icon)).GetWrapOrEmpty();
                if (wrap.Width > 1)
                {
                    var tint = l.Ready ? Palette.U32(Palette.White) : Palette.U32(Palette.Rgba(255, 255, 255, 0.45f));
                    dl.AddImageRounded(wrap.Handle, at, at + new Vector2(s), Vector2.Zero, Vector2.One, tint, S(4));
                    return;
                }
            }
            catch (Exception)
            {
                // An icon the game can't give: its kind's mark instead.
            }
        }
        dl.AddRectFilled(at, at + new Vector2(s), Palette.U32(Palette.Tint), S(4));
        var mark = l.Spec.Kind switch
        {
            CooldownKind.RaidBuff => Icon.Sparkles,
            CooldownKind.Mitigation => Icon.Shield,
            CooldownKind.Healer => Icon.Heart,
            CooldownKind.Raise => Icon.Hourglass,
            CooldownKind.Tank => Icon.Swords,
            _ => Icon.Zap,
        };
        var inner = s * 0.72f;
        Icons.Draw(dl, mark, at + new Vector2((s - inner) / 2), inner, l.Ready ? Palette.White : Palette.Muted);
    }

    /// <summary>The limit break gauge's row, on its own ground: its mark, "Limit break", its bars (full ones ember, the one filling to its share).</summary>
    private void GaugeRow(ImDrawListPtr dl, LimitBreakGauge g, float left, float y, float w, float h, Vector4 ground)
    {
        var compact = Compact;
        dl.AddRectFilled(new Vector2(left, y), new Vector2(left + w, y + h), Palette.U32(ground), S(8));
        var x = left + S(compact ? 6 : 8);
        Kit.LimitMark(dl, new Vector2(x, y + (h - S(IconCss)) / 2), IconCss);
        x += S(IconCss) + S(7);
        var npx = compact ? 12 : 13;
        x += Txt.Line(dl, new Vector2(x, y), "Limit break", Face.SansMedium, npx, Palette.White, RowCss) + S(10);
        var right = left + w - S(compact ? 7 : 9);
        if (!Equals(gaugeBars.Of, g)) gaugeBars = (g, MeterText.Bars(g));
        MeterWindow.DrawGauge(dl, new Vector2(x, y + h / 2), right - x, gaugeBars.Bars, compact ? 5 : 6);
        if (Kit.Pointed(new Vector2(left, y), new Vector2(left + w, y + h))) Kit.Tooltip(MeterText.GaugeTip(g));
    }
}
