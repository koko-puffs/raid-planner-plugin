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
/// The party cooldown tracker (Settings → Cooldowns, `/raidplan cooldowns`): small rows over the
/// fight, one a party member's tracked cooldown, grouped by kind (raid buffs, party mitigation, the
/// healers', Swiftcast…) with a hairline between, then in spot order. A row: the action's own icon
/// (dimmed while it recharges), the member's spot, the action's name, and the time to ready in mono on
/// the right ("Ready" in green; "1/2" before it for charges), the recharge filling the row behind it as
/// the compact meter's bar does (yours ember). Yours are the game's own timers; the others' are
/// learned from their uses (<see cref="CooldownBook"/>), an estimate the row's tooltip says is one. The
/// limit break gauge last, when the party has one. A sample shows under a Preview tag.
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
        : base("Cooldowns###RaidPlannerCooldowns", config, fonts, () => config.Cooldowns, 250, new Vector2(0.14f, 0.32f)) => this.game = game;

    /// <summary>In a duty (shown only there unless Everywhere).</summary>
    public Func<bool>? InDuty { get; set; }

    /// <summary>The limit break gauge now (the meter's), or null.</summary>
    public Func<LimitBreakGauge?>? Gauge { get; set; }

    protected override string What => "Raid Planner's cooldown tracker couldn't draw";

    private bool Compact => Settings.Compact;

    private float RowCss => Compact ? 19 : 24;

    private float PadCss => 4;

    private float HeadCss => Previewing ? (Compact ? 22 : 26) : 0;

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
            lines = CooldownView.Lines(game.Party, game.Action, Config.CooldownPicks, game.Book, game.Own, now, Config.CooldownsMine, Config.CooldownsHideReady);
        }
        gauge = Config.CooldownsGauge ? Gauge?.Invoke() : null;
        return lines.Count > 0 || gauge != null;
    }

    /// <summary>The rows, the hairlines between kinds, the gauge's row, a little air at each end; the head while previewing.</summary>
    protected override float Measure(float widthCss) => HeadCss + PadCss * 2 + RowCss * (lines.Count + (gauge != null ? 1 : 0)) + Dividers();

    /// <summary>The space the hairlines between kinds take (2px each).</summary>
    private float Dividers()
    {
        var n = 0;
        for (var i = 1; i < lines.Count; i++)
            if (lines[i].Spec.Kind != lines[i - 1].Spec.Kind)
                n++;
        return n * 2 + (gauge != null && lines.Count > 0 ? 2 : 0);
    }

    protected override void DrawContent(ImDrawListPtr dl, Vector2 top, float w, float h)
    {
        var x0 = top.X;
        var y = top.Y;
        if (Previewing)
        {
            // The sample's head: the eyebrow and its Preview tag (pressed, it stops).
            var hh = S(HeadCss);
            Kit.Eyebrow(dl, new Vector2(x0 + S(10), y + (hh - Kit.EyebrowH) / 2), "Cooldowns");
            PreviewTag(dl, new Vector2(x0 + w - S(10) - Kit.TagWidth("Preview", TagKind.Plain), y + (hh - S(18)) / 2), "##cd-preview");
            y += hh;
            Kit.Rule(dl, x0, x0 + w, y - 1);
        }
        y += S(PadCss);
        var rowH = S(RowCss);
        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0 && lines[i].Spec.Kind != lines[i - 1].Spec.Kind)
            {
                Kit.Rule(dl, x0 + S(8), x0 + w - S(8), y + S(1) - 0.5f);
                y += S(2);
            }
            Row(dl, lines[i], x0, y, w, rowH, i);
            y += rowH;
        }
        if (gauge != null)
        {
            if (lines.Count > 0)
            {
                Kit.Rule(dl, x0 + S(8), x0 + w - S(8), y + S(1) - 0.5f);
                y += S(2);
            }
            GaugeRow(dl, gauge, x0, y, w, rowH);
        }
    }

    /// <summary>A cooldown's row: the recharge behind it, the icon, the spot, the name, the charges and the time to ready.</summary>
    private void Row(ImDrawListPtr dl, CooldownLine l, float left, float y, float w, float h, int index)
    {
        var compact = Compact;
        var min = new Vector2(left, y);
        var max = new Vector2(left + w, y + h);
        // The recharge, as the compact meter's bar: white at 5.5% (yours ember-washed) to how far it has come, its 1px line at its foot.
        var bx = left + S(6);
        var bw = w - S(12);
        if (l.Ready)
            dl.AddRectFilled(new Vector2(bx, y + 1), new Vector2(bx + bw, y + h - 1), Palette.U32(Palette.WithAlpha(Palette.Ready, 0.06f)), S(4));
        else
        {
            var fw = bw * l.Fill;
            if (fw >= 1)
            {
                dl.AddRectFilled(new Vector2(bx, y + 1), new Vector2(bx + fw, y + h - 1), Palette.U32(l.Who.IsYou ? Palette.EmberWash : Palette.Rgba(255, 255, 255, 0.055f)));
                var la = new Vector2(bx, y + h - 2);
                var lb = new Vector2(bx + fw, y + h - 1);
                if (l.Who.IsYou) Kit.CastGradient(dl, la, lb);
                else dl.AddRectFilled(la, lb, Palette.U32(Palette.Rgba(255, 255, 255, 0.24f)));
            }
        }
        var hovered = Kit.Pointed(min, max);
        if (hovered) Kit.Tooltip(CooldownView.Tip(l));
        var x = left + S(10);
        var icon = compact ? 15f : 18f;
        DrawIcon(dl, l, new Vector2(x, y + (h - S(icon)) / 2), icon);
        x += S(icon) + S(7);
        var spot = compact ? 14 : 16;
        Kit.Spot(dl, new Vector2(x + S(spot) / 2, y + h / 2), spot, l.Who.Slot, Palette.RoleColor(Core.Roles.RoleWord(l.Who.Role)), 1.5f);
        x += S(spot) + S(7);
        var (time, count) = WordsOf(l);
        var right = left + w - S(10);
        var tpx = compact ? 12 : 13;
        var tw = Txt.LineRight(dl, right, y, time, Face.MonoSemibold, tpx, l.Ready ? Palette.Ready : Palette.White, RowCss);
        right -= tw;
        if (count != null)
        {
            right -= S(6);
            right -= Txt.LineRight(dl, right, y, count, Face.Mono, compact ? 9 : 10, Palette.Faint, RowCss);
        }
        var npx = compact ? 11 : 12;
        var room = right - S(8) - x;
        if (room > S(16))
            Txt.Line(dl, new Vector2(x, y), Txt.Fit(l.Action.Name, Face.SansMedium, npx, room), Face.SansMedium, npx, l.Ready ? Palette.White : Palette.Muted, RowCss);
        _ = index;
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

    /// <summary>The limit break gauge's row: its mark, "Limit break", its bars (full ones ember, the one filling to its share).</summary>
    private void GaugeRow(ImDrawListPtr dl, LimitBreakGauge g, float left, float y, float w, float h)
    {
        var compact = Compact;
        var x = left + S(10);
        var icon = compact ? 15f : 18f;
        Kit.LimitMark(dl, new Vector2(x, y + (h - S(icon)) / 2), icon);
        x += S(icon) + S(7);
        var npx = compact ? 11 : 12;
        x += Txt.Line(dl, new Vector2(x, y), "Limit break", Face.SansMedium, npx, Palette.White, RowCss) + S(10);
        var right = left + w - S(10);
        if (!Equals(gaugeBars.Of, g)) gaugeBars = (g, MeterText.Bars(g));
        MeterWindow.DrawGauge(dl, new Vector2(x, y + h / 2), right - x, gaugeBars.Bars, compact ? 5 : 6);
        if (Kit.Pointed(new Vector2(left, y), new Vector2(left + w, y + h))) Kit.Tooltip(MeterText.GaugeTip(g));
    }
}
