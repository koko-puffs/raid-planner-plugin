using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using RaidPlanner.Core.Meter;
using RaidPlanner.UI.Model;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>
/// The death recap (since 0.9.0; Settings → Damage meter → Death recap, and Windows): when a death comes (yours, or everyone's as
/// Settings says), a card over the fight built from the meter's own death (the opened-up meter's Deaths tab:
/// <see cref="DeathRecapView"/>): who died, when and in which phase; "Killed by Latent Defect for 182,400 (41,200 more than they
/// had), a tank buster, with no mitigation up."; the last six seconds' hits, heals and barriers with the HP after each; what
/// mitigation was up at the hit and, for a tank, their big cooldowns that weren't; Weakness; and Open in the meter, which opens
/// the opened-up meter on that death. It goes 8 seconds after the last death came, held while the mouse is over it; its pin keeps
/// it (<see cref="DeathRecapQueue"/>). Several at once (a wipe) are one-line rows, newest on top; one pressed opens into its card.
/// The meter has to be listening (it does with the recap on, even with its window off). A sample shows under a Preview tag.
/// </summary>
public sealed class DeathRecapWindow : OverlayWindow
{
    private readonly DeathRecapQueue queue = new();
    private readonly long started = Stopwatch.GetTimestamp();
    private bool hovered;
    private bool sampleShown;
    private double lastLook = double.NegativeInfinity;

    public DeathRecapWindow(Configuration config, Fonts fonts)
        : base("Death recap###RaidPlannerDeathRecap", config, fonts, () => config.DeathRecap, 440, new Vector2(0.72f, 0.18f), ArrangePart.DeathRecap)
    {
    }

    /// <summary>The meter's numbers (set by the plugin: the real session's, never the meter's sample fight).</summary>
    public Func<IMeterSource?>? Source { get; set; }

    /// <summary>Opens the opened-up meter on a death (set by the plugin): the fight, the member, the death's index in the fight's list.</summary>
    public Action<int, int, int>? OpenInMeter { get; set; }

    protected override string What => "Raid Codex's death recap couldn't draw";

    private double Now => (Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency;

    // Sizes (CSS px): 10 from the window's edges; a card 16 in all round with 14 corners: its head 38 (a 28 badge), the killed-by
    // box 10 over and under its words (13 on 19.5), "The last 6 seconds" 16 and a row 31 a line, the chips 22 tall 6 apart, the
    // foot a small button's 32. A stacked death a row 40 tall, 8 apart; under the rows the foot 24.

    private const float Margin = 10, Pad = 16, HeadCss = 38, BoxPad = 10, TextLine = 19.5f, EyebrowCss = 16, LineCss = 31, ChipCss = 22, ChipGap = 6, FootCss = 32;

    private const float StackRow = 40, StackGap = 8, StackFoot = 24;

    protected override bool Wanted()
    {
        var now = Now;
        if (Previewing)
        {
            if (!sampleShown)
            {
                queue.Show(DeathRecapSample.Cards());
                sampleShown = true;
            }
            // The sample stays while it's shown (the time stands still).
            queue.Tick(now, true);
            return queue.Cards.Count > 0;
        }
        if (sampleShown)
        {
            sampleShown = false;
            queue.Clear();
        }
        // Not looked at for a while (the recap off, the window closed): what the encounter has then isn't new.
        if (now - lastLook > 1) queue.Restart();
        lastLook = now;
        queue.Feed(Source?.Invoke()?.Current, Config.DeathRecapEveryone);
        if (!Settings.Show)
        {
            if (queue.Cards.Count > 0) queue.Clear();
            return false;
        }
        queue.Tick(now, hovered);
        hovered = false;
        return queue.Cards.Count > 0;
    }

    private float CardW(float widthCss) => widthCss - Margin * 2;

    protected override float Measure(float widthCss)
    {
        var cards = queue.Cards;
        if (cards.Count == 0) return Margin * 2;
        var w = S(CardW(widthCss));
        if (cards.Count == 1) return Margin * 2 + Card(default, cards[0], Vector2.Zero, w, false, true) / S(1);
        var h = Margin * 2f;
        var full = queue.Full;
        for (var i = 0; i < cards.Count; i++)
        {
            if (i > 0) h += StackGap;
            h += full != null && ReferenceEquals(cards[i], full) ? Card(default, cards[i], Vector2.Zero, w, false, false) / S(1) : StackRow;
        }
        return h + StackFoot;
    }

    protected override void DrawContent(ImDrawListPtr dl, Vector2 top, float w, float h)
    {
        hovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
        var cards = queue.Cards;
        if (cards.Count == 0) return;
        var x = top.X + S(Margin);
        var cw = w - S(Margin) * 2;
        var y = top.Y + S(Margin);
        if (cards.Count == 1)
        {
            Card(dl, cards[0], new Vector2(x, y), cw, true, true);
            return;
        }
        var full = queue.Full;
        for (var i = 0; i < cards.Count; i++)
        {
            if (i > 0) y += S(StackGap);
            var c = cards[i];
            if (full != null && ReferenceEquals(c, full)) y += Card(dl, c, new Vector2(x, y), cw, true, false);
            else
            {
                StackedRow(dl, c, new Vector2(x, y), cw, i);
                y += S(StackRow);
            }
        }
        // Under the rows: when they go, and the pin.
        var right = x + cw;
        var fy = y + (S(StackFoot) - S(22)) / 2 + S(2);
        var pinLeft = Pin(dl, new Vector2(right - S(22), fy));
        Txt.LineRight(dl, pinLeft - S(6), fy, GoesWords(), Face.Sans, 12, Palette.Faint, 22);
        if (Previewing) PreviewTag(dl, new Vector2(x, fy + S(2)), "##dr-preview");
    }

    private string GoesWords() => queue.Pinned ? "Pinned" : Previewing ? "Goes 8 s after the last death" : DeathRecapView.GoesIn(queue.Left);

    /// <summary>The pin (mouse only): keeps them; ember while it's on. Returns its left edge.</summary>
    private float Pin(ImDrawListPtr dl, Vector2 at)
    {
        var size = S(22);
        var (clicked, over, _) = Kit.Hit("##dr-pin", at, new Vector2(size));
        var on = queue.Pinned;
        if (on) dl.AddRectFilled(at, at + new Vector2(size), Palette.U32(Palette.EmberWash), S(6));
        else if (over) dl.AddRectFilled(at, at + new Vector2(size), Palette.U32(Palette.HoverButton), S(6));
        var icon = S(14);
        Icons.Draw(dl, Icon.Pin, at + new Vector2((size - icon) / 2), icon, on ? Palette.Ember : over ? Palette.White : Palette.Faint);
        if (over) Kit.Tooltip(on ? "Pinned: it stays until you press this again." : "Keep it: it stays until you press this again.");
        if (clicked) queue.TogglePin();
        return at.X;
    }

    /// <summary>A death in a wipe's stack: a row on its own ground, its job, "Fenn Hollow · Hello World (raid-wide)", the time and a chevron; pressed, it opens.</summary>
    private void StackedRow(ImDrawListPtr dl, RecapCard c, Vector2 at, float w, int i)
    {
        var h = S(StackRow);
        var max = at + new Vector2(w, h);
        var (clicked, over, _) = Kit.Hit($"##dr-row{c.FightId}-{c.Index}", at, new Vector2(w, h));
        var bg = Math.Clamp(Settings.Background, 0, 1);
        dl.AddRectFilled(at, max, Palette.U32(Palette.Rgba(24, 24, 29, Math.Clamp(0.6f + 0.38f * bg, 0, 0.97f))), S(10));
        if (over) dl.AddRectFilled(at, max, Palette.U32(Palette.Hover), S(10));
        dl.AddRect(at, max, Palette.U32(Palette.Line2), S(10), ImDrawFlags.None, 1);
        PlateArt.JobBadge(dl, new Vector2(at.X + S(12) + S(10), at.Y + h / 2), 20, c.Job, c.Role, Config.MeterRoleColours);
        var right = max.X - S(12);
        var chevron = S(14);
        Icons.Draw(dl, Icon.ChevronDown, new Vector2(right - chevron, at.Y + (h - chevron) / 2), chevron, over ? Palette.White : Palette.Faint);
        right -= chevron + S(10);
        right -= Txt.LineRight(dl, right, at.Y, Format.Clock(c.At), Face.Mono, 12, Palette.Muted, StackRow) + S(12);
        var x = at.X + S(42);
        var name = c.Who(Config.MeterJobsOnly);
        var nw = Txt.Line(dl, new Vector2(x, at.Y), Txt.Fit(name, Face.SansSemibold, 13, MathF.Max(0, right - x)), Face.SansSemibold, 13, Palette.White, StackRow);
        var room = right - x - nw;
        if (room > S(40))
            Txt.Line(dl, new Vector2(x + nw, at.Y), Txt.Fit(" · " + DeathRecapView.RowWords(c), Face.Sans, 13, room), Face.Sans, 13, Palette.Muted, StackRow);
        if (over) Kit.Tooltip("Open the recap");
        if (clicked) queue.Toggle(c);
    }

    /// <summary>
    /// A death's card at <paramref name="at"/>, this wide (screen px): drawn, or (<paramref name="draw"/> false) only measured.
    /// <paramref name="alone"/>: the only one (its foot has when it goes and the pin); else one opened in a stack (a chevron folds it).
    /// Returns its height (screen px).
    /// </summary>
    private float Card(ImDrawListPtr dl, RecapCard c, Vector2 at, float w, bool draw, bool alone)
    {
        var h = CardHeight(c, w);
        if (!draw) return h;
        var max = at + new Vector2(w, h);
        var bg = Math.Clamp(Settings.Background, 0, 1);
        dl.AddRectFilled(at, max, Palette.U32(Palette.Rgba(24, 24, 29, Math.Clamp(0.62f + 0.36f * bg, 0, 0.97f))), S(14));
        dl.AddRect(at, max, Palette.U32(Palette.Line2), S(14), ImDrawFlags.None, 1);
        var left = at.X + S(Pad);
        var right = max.X - S(Pad);
        var inner = right - left;
        var y = at.Y + S(Pad);

        // The head: the job, "Velka Moor died", when; a skull, or (opened in a stack) a chevron that folds it.
        PlateArt.JobBadge(dl, new Vector2(left + S(14), y + S(HeadCss) / 2), 28, c.Job, c.Role, Config.MeterRoleColours, false, c.Slot);
        var tx = left + S(28) + S(12);
        var markW = S(22);
        var tw = right - markW - S(8) - tx;
        var who = c.Who(Config.MeterJobsOnly);
        Txt.Line(dl, new Vector2(tx, y), Txt.Fit(who + " died", Face.SansSemibold, 15, tw), Face.SansSemibold, 15, Palette.White, 20);
        Txt.Line(dl, new Vector2(tx, y + S(20)), Txt.Fit(DeathRecapView.When(c), Face.Sans, 12, tw), Face.Sans, 12, Palette.Muted, 17);
        var markAt = new Vector2(right - markW, y + (S(HeadCss) - markW) / 2);
        if (alone)
        {
            var skull = S(16);
            Icons.Draw(dl, Icon.Skull, markAt + new Vector2((markW - skull) / 2), skull, Palette.DangerSoft);
            if (Previewing) PreviewTag(dl, new Vector2(markAt.X - S(8) - Kit.TagWidth("Preview", TagKind.Plain), markAt.Y + (markW - S(18)) / 2), "##dr-preview");
        }
        else
        {
            var (fold, over, _) = Kit.Hit($"##dr-fold{c.FightId}-{c.Index}", markAt, new Vector2(markW));
            if (over) dl.AddRectFilled(markAt, markAt + new Vector2(markW), Palette.U32(Palette.HoverButton), S(6));
            var chevron = S(14);
            Icons.Draw(dl, Icon.ChevronDown, markAt + new Vector2((markW - chevron) / 2), chevron, over ? Palette.White : Palette.Faint);
            if (over) Kit.Tooltip("Fold it back into its row");
            if (fold) queue.Toggle(c);
        }
        y += S(HeadCss + 12);

        // Killed by …
        var runs = KilledRuns(c);
        var textH = Txt.Flow(Vector2.Zero, inner - S(24), runs, TextLine, out _, draw: false);
        var boxMax = new Vector2(right, y + S(BoxPad) * 2 + textH);
        dl.AddRectFilled(new Vector2(left, y), boxMax, Palette.U32(Palette.DangerWash), S(10));
        dl.AddRect(new Vector2(left, y), boxMax, Palette.U32(Palette.DangerEdge), S(10), ImDrawFlags.None, 1);
        Txt.Flow(new Vector2(left + S(12), y + S(BoxPad)), inner - S(24), runs, TextLine, out _);
        y = boxMax.Y + S(12);

        // The last seconds.
        Kit.Eyebrow(dl, new Vector2(left, y + (S(EyebrowCss) - Kit.EyebrowH) / 2), LastWords);
        y += S(EyebrowCss + 6);
        var hpMax = c.MaxHp;
        var jobsOnly = Config.MeterJobsOnly;
        for (var i = 0; i < c.Rows.Count; i++)
        {
            var r = c.Rows[i];
            var rowH = S(LineCss);
            if (r.Killing) dl.AddRectFilled(new Vector2(left - S(4), y), new Vector2(right + S(4), y + rowH), Palette.U32(Palette.DangerWash), S(6));
            else Kit.Rule(dl, left, right, y - 0.5f);
            var offW = Txt.Line(dl, new Vector2(left, y), DeathRecapView.Offset(r.Offset), Face.Mono, 11, r.Killing ? Palette.DangerSoft : Palette.Faint, LineCss);
            var ix = left + MathF.Max(S(46), offW + S(8));
            var icon = S(13);
            var (mark, ink) = r.Kind switch
            {
                RecapKind.Heal => (Icon.Heart, Palette.Ready),
                RecapKind.Shield => (Icon.Shield, Palette.Warn),
                _ => (Icon.Sword, Palette.DangerSoft),
            };
            Icons.Draw(dl, mark, new Vector2(ix, y + (rowH - icon) / 2), icon, ink);
            // At the right: the HP after it as a bar, the amount before it.
            var barW = S(64);
            var barX = right - barW;
            var barY = y + rowH / 2 - S(2);
            dl.AddRectFilled(new Vector2(barX, barY), new Vector2(right, barY + S(4)), Palette.U32(Palette.Track), S(2));
            var f = hpMax > 0 && r.HpAfter > 0 ? Math.Clamp(r.HpAfter / (float)hpMax, 0, 1) : 0;
            if (f > 0) dl.AddRectFilled(new Vector2(barX, barY), new Vector2(barX + barW * f, barY + S(4)), Palette.U32(f < 0.3f ? Palette.Warn : Palette.Ready), S(2));
            if (Kit.Pointed(new Vector2(barX - S(4), y), new Vector2(right, y + rowH)))
                Kit.Tooltip(r.HpAfter < 0 ? "Their HP then wasn't seen" : $"{Format.Thousands(r.HpAfter)} HP after it{(hpMax > 0 ? $" of {Format.Thousands(hpMax)}" : string.Empty)}");
            var amount = DeathRecapView.Amount(r);
            var aw = amount.Length > 0 ? Txt.LineRight(dl, barX - S(10), y, amount, Face.MonoSemibold, 12, r.Kind == RecapKind.Hit ? Palette.DangerSoft : r.Kind == RecapKind.Heal ? Palette.Ready : Palette.Warn, LineCss) : 0;
            var wx = ix + icon + S(8);
            var room = barX - S(10) - aw - S(10) - wx;
            if (room > S(20))
            {
                var nw = Txt.Line(dl, new Vector2(wx, y), Txt.Fit(r.What, Face.SansMedium, 13, room), Face.SansMedium, 13, Palette.White, LineCss);
                if (!jobsOnly && r.Who is { Length: > 0 } by && room - nw > S(40))
                    Txt.Line(dl, new Vector2(wx + nw, y), Txt.Fit(" · " + FirstName(by), Face.Sans, 13, room - nw), Face.Sans, 13, Palette.Muted, LineCss);
            }
            y += rowH;
        }
        if (c.Rows.Count == 0)
        {
            Txt.Line(dl, new Vector2(left, y), "Nothing was kept of what hit them.", Face.Sans, 12, Palette.Muted, LineCss);
            y += S(LineCss);
        }
        y += S(10);

        // Up at the hit, what a tank was missing, Weakness.
        y += Chips(dl, c, new Vector2(left, y), inner, true) + S(12);

        // The foot: Open in the meter; when it goes and the pin (alone).
        var sample = c.FightId < 1;
        if (Kit.Button($"##dr-open{c.FightId}-{c.Index}", new Vector2(left, y), "Open in the meter", Btn.Outline, BtnSize.Sm, null, 0, sample || OpenInMeter == null,
                sample ? "A sample's death isn't in the meter." : "The opened-up meter on this death: its whole recap, and the fight around it."))
            OpenInMeter?.Invoke(c.FightId, c.PartyId, c.Index);
        if (alone)
        {
            var py = y + (S(FootCss) - S(22)) / 2;
            var pinLeft = Pin(dl, new Vector2(right - S(22), py));
            Txt.LineRight(dl, pinLeft - S(6), py, GoesWords() + " ·", Face.Sans, 12, Palette.Faint, 22);
        }
        return h;
    }

    private const string LastWords = "The last 6 seconds";

    /// <summary>A name's first word ("Koko" of "Koko Nyan"), as the card's lines have them.</summary>
    private static string FirstName(string name)
    {
        var space = name.IndexOf(' ');
        return space > 0 ? name[..space] : name;
    }

    /// <summary>Each card's killed-by words, made once (not each frame).</summary>
    private readonly Dictionary<RecapCard, Run[]> killed = new(ReferenceEqualityComparer.Instance);

    /// <summary>"Killed by Latent Defect for 182,400 (41,200 more than they had), a tank buster, with no mitigation up."</summary>
    private Run[] KilledRuns(RecapCard c)
    {
        if (killed.TryGetValue(c, out var made)) return made;
        if (killed.Count > 32) killed.Clear();
        var (over, tail) = DeathRecapView.KilledWords(c);
        return killed[c] = new[]
        {
            new Run("Killed by ", Face.Sans, 13, Palette.DangerInk),
            new Run(c.KilledBy, Face.SansSemibold, 13, Palette.White),
            new Run(" for ", Face.Sans, 13, Palette.DangerInk),
            new Run(Format.Thousands(c.Amount), Face.MonoSemibold, 13, Palette.DangerSoft),
            new Run(over + tail, Face.Sans, 13, Palette.DangerInk),
        };
    }

    /// <summary>The chips under the lines, laid out (and drawn when <paramref name="draw"/>): their height (screen px).</summary>
    private float Chips(ImDrawListPtr dl, RecapCard c, Vector2 at, float width, bool draw)
    {
        var items = ChipItems(c);
        var x = at.X;
        var y = at.Y;
        var h = S(ChipCss);
        var label = "Up at the hit:";
        var lw = Txt.Width(label, Face.Sans, 12);
        if (draw) Txt.Line(dl, new Vector2(x, y), label, Face.Sans, 12, Palette.Muted, ChipCss);
        x += lw + S(8);
        foreach (var (text, kind) in items)
        {
            var cw = MathF.Min(width, Txt.Width(text, Face.SansMedium, 12) + S(16));
            if (x > at.X && x + cw > at.X + width)
            {
                x = at.X;
                y += h + S(ChipGap);
            }
            if (draw)
            {
                var (fill, edge, ink) = kind switch
                {
                    1 => (Palette.DangerWash, Palette.DangerEdge, Palette.DangerSoft),
                    2 => (Palette.WarnWash, Palette.WarnLine, Palette.WarnInk),
                    3 => (Palette.Tint, Palette.Line, Palette.Faint),
                    _ => (Palette.Tint, Palette.Line2, Palette.Text),
                };
                var min = new Vector2(x, y);
                dl.AddRectFilled(min, min + new Vector2(cw, h), Palette.U32(fill), S(6));
                dl.AddRect(min, min + new Vector2(cw, h), Palette.U32(edge), S(6), ImDrawFlags.None, 1);
                Txt.Line(dl, new Vector2(x + S(8), y), Txt.Fit(text, Face.SansMedium, 12, cw - S(16)), Face.SansMedium, 12, ink, ChipCss);
            }
            x += cw + S(6);
        }
        return y + h - at.Y;
    }

    /// <summary>The chips' words and kinds (0 plain, 1 danger, 2 warn, 3 quiet): what was up, what a tank was missing, Weakness.</summary>
    private static List<(string Text, int Kind)> ChipItems(RecapCard c)
    {
        var list = new List<(string, int)>();
        foreach (var u in c.Up) list.Add((u, 0));
        if (!c.MitigationKnown) list.Add(("Not seen: they were out of sight", 3));
        else if (c.Up.Count == 0) list.Add(("Nothing", 3));
        if (DeathRecapView.MissingLine(c.Missing) is { } missing) list.Add((missing, 1));
        if (c.Weakness) list.Add(("Had Weakness", 2));
        return list;
    }

    /// <summary>A card's height (screen px) at this width.</summary>
    private float CardHeight(RecapCard c, float w)
    {
        var inner = w - S(Pad) * 2;
        var h = S(Pad) + S(HeadCss + 12);
        h += S(BoxPad) * 2 + Txt.Flow(Vector2.Zero, inner - S(24), KilledRuns(c), TextLine, out _, draw: false) + S(12);
        h += S(EyebrowCss + 6) + S(LineCss) * Math.Max(1, c.Rows.Count) + S(10);
        h += Chips(default, c, Vector2.Zero, inner, false) + S(12);
        h += S(FootCss) + S(Pad);
        return h;
    }
}
