using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using RaidPlanner.Game;
using RaidPlanner.UI.Model;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>
/// The ready check card (since 0.9.0; Settings → Plan and callouts → Ready checks, and Windows): as the game's ready check starts
/// (read only: it's never answered, <see cref="ReadyCheckReader"/>), a card beside the party list says who's fed and for how
/// long (green; amber when it would run out before your average pull of this duty is over; red with no food; "can't see" for one
/// out of range), with "7 of 8 fed · one runs out mid-pull" and your average pull over them, then yours: the tinctures and meals
/// in your inventory and your gear's condition (a warning under <see cref="ReadyCheckView.GearLow"/>%). When everyone's fed for
/// longer than an average pull and your gear is fine, it's one line instead ("Everyone's fed · …"). It goes when the pull starts,
/// or 30 seconds after the ready check ends (<see cref="ReadyCheckWatch"/>); its pin (mouse only) keeps it. A sample shows under a
/// Preview tag. In the callouts' look: a card on its own dark ground.
/// </summary>
public sealed class ReadyCheckWindow : OverlayWindow
{
    private readonly ReadyCheckReader? reader;
    private readonly ReadyCheckWatch watch = new();
    private readonly long started = Stopwatch.GetTimestamp();
    private ReadyCard? card;
    private double membersAt = double.NegativeInfinity;
    private double youAt = double.NegativeInfinity;
    private List<ReadyMember> members = new();
    private ReadyYou you;
    private double? average;
    private bool previewPinned;

    public ReadyCheckWindow(Configuration config, Fonts fonts, ReadyCheckReader? reader)
        : base("Ready check###RaidPlannerReadyCheck", config, fonts, () => config.ReadyCheck, 340, new Vector2(0.3f, 0.3f), ArrangePart.ReadyCheck) => this.reader = reader;

    /// <summary>The party's in combat (set by the plugin): the pull has started.</summary>
    public Func<bool>? InCombat { get; set; }

    /// <summary>Your average pull here (set by the plugin; null with none kept for this duty).</summary>
    public Func<double?>? AveragePull { get; set; }

    protected override string What => "Raid Codex's ready check card couldn't draw";

    private double Now => (Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency;

    // Sizes (CSS px): the card 10 in from the window's edges, 16 in all round inside; its head 18 tall, the headline 20 and the
    // average's line 17; each member a row 31 tall with a hairline between; yours in a well 12 in; the foot 16.

    private const float Margin = 10, Pad = 16, HeadCss = 18, HeadlineCss = 20, AverageCss = 17, RowCss = 31, YouPad = 12, YouLine = 20, FootCss = 16;

    /// <summary>The one-line state: a 36 badge, the title 20 and its words (two lines of 17 at most), 14 in all round.</summary>
    private const float CompactPad = 14, BadgeCss = 36, CompactLineCss = 17;

    protected override bool Wanted()
    {
        var now = Now;
        if (Previewing)
        {
            you = ReadyCheckSample.You;
            youAt = double.NegativeInfinity;
            card = ReadyCheckView.Card(ReadyCheckSample.Members, you, ReadyCheckSample.Average);
            return true;
        }
        if (reader == null || !Settings.Show)
        {
            if (watch.Showing) watch.Hide();
            card = null;
            return false;
        }
        var fight = InCombat?.Invoke() == true;
        if (watch.Frame(now, reader.Awaiting(), fight))
        {
            // A new ready check: everything read afresh, the average pull too.
            membersAt = youAt = double.NegativeInfinity;
            average = AveragePull?.Invoke();
        }
        if (!watch.Showing)
        {
            card = null;
            return false;
        }
        // Read again twice a second (the members' food) and every two seconds (your inventory and gear), not every frame.
        if (now - membersAt >= 0.5)
        {
            membersAt = now;
            members = reader.Members();
            card = null;
        }
        if (now - youAt >= 2)
        {
            youAt = now;
            you = reader.You();
            card = null;
        }
        card ??= ReadyCheckView.Card(members, you, average);
        return card.Rows.Count > 0;
    }

    private bool Pinned => Previewing ? previewPinned : watch.Pinned;

    protected override float Measure(float widthCss)
    {
        if (card is not { } c) return Margin * 2;
        var inner = widthCss - Margin * 2 - Pad * 2;
        if (c.Compact) return Margin * 2 + CompactHeight(c, widthCss);
        var h = Pad + HeadCss + 8 + HeadlineCss + (c.Average != null ? 2 + AverageCss : 0) + 10 + c.Rows.Count * RowCss + 12;
        h += YouHeight(c) + 12 + FootCss + Pad;
        return Margin * 2 + h;
    }

    private static float YouHeight(ReadyCard c) => YouPad + 16 + 6 + YouLine + (c.Gear != null ? 6 + YouLine : 0) + YouPad;

    private float CompactTextCss(float widthCss) => widthCss - Margin * 2 - CompactPad * 2 - BadgeCss - 12 - 30;

    private float CompactHeight(ReadyCard c, float widthCss)
    {
        var lines = Math.Clamp(Txt.Wrap(c.CompactLine, Face.Sans, 12, S(CompactTextCss(widthCss)), 2).Count, 1, 2);
        return CompactPad * 2 + MathF.Max(BadgeCss, HeadlineCss + 2 + lines * CompactLineCss);
    }

    protected override void DrawContent(ImDrawListPtr dl, Vector2 top, float w, float h)
    {
        if (card is not { } c) return;
        var min = top + new Vector2(S(Margin));
        var max = new Vector2(top.X + w - S(Margin), top.Y + h - S(Margin));
        var bg = Math.Clamp(Settings.Background, 0, 1);
        dl.AddRectFilled(min, max, Palette.U32(Palette.Rgba(24, 24, 29, Math.Clamp(0.6f + 0.38f * bg, 0, 0.97f))), S(14));
        dl.AddRect(min, max, Palette.U32(Palette.Line2), S(14), ImDrawFlags.None, 1);
        if (c.Compact) DrawCompact(dl, c, min, max);
        else DrawFull(dl, c, min, max);
    }

    /// <summary>The pin at the top right of the card (mouse only): keeps it; ember while it's on. Returns its left edge.</summary>
    private float Pin(ImDrawListPtr dl, float right, float y)
    {
        var size = S(22);
        var at = new Vector2(right - size, y);
        var (clicked, hovered, _) = Kit.Hit("##rc-pin", at, new Vector2(size));
        var on = Pinned;
        if (on) dl.AddRectFilled(at, at + new Vector2(size), Palette.U32(Palette.EmberWash), S(6));
        else if (hovered) dl.AddRectFilled(at, at + new Vector2(size), Palette.U32(Palette.HoverButton), S(6));
        var icon = S(14);
        Icons.Draw(dl, Icon.Pin, at + new Vector2((size - icon) / 2), icon, on ? Palette.Ember : hovered ? Palette.White : Palette.Faint);
        if (hovered) Kit.Tooltip(on ? "Pinned: it stays until you press this again." : "Keep it: it stays after the pull starts, until you press this again.");
        if (clicked)
        {
            if (Previewing) previewPinned = !previewPinned;
            else watch.TogglePin(Now);
        }
        var left = at.X;
        if (Previewing) left -= S(8) + PreviewTag(dl, new Vector2(left - S(8) - Kit.TagWidth("Preview", TagKind.Plain), y + (size - S(18)) / 2), "##rc-preview");
        return left;
    }

    private void DrawFull(ImDrawListPtr dl, ReadyCard c, Vector2 min, Vector2 max)
    {
        var left = min.X + S(Pad);
        var right = max.X - S(Pad);
        var y = min.Y + S(Pad);
        var pinLeft = Pin(dl, right, y - S(2));
        Kit.Eyebrow(dl, new Vector2(left, y + (S(HeadCss) - Kit.EyebrowH) / 2), "Ready check", null, pinLeft - S(8) - left, Palette.Ember);
        y += S(HeadCss + 8);
        Txt.Line(dl, new Vector2(left, y), Txt.Fit(c.Headline, Face.SansSemibold, 15, right - left), Face.SansSemibold, 15, Palette.White, HeadlineCss);
        y += S(HeadlineCss);
        if (c.Average != null)
        {
            y += S(2);
            Txt.Line(dl, new Vector2(left, y), Txt.Fit(c.Average, Face.Sans, 12, right - left), Face.Sans, 12, Palette.Muted, AverageCss);
            y += S(AverageCss);
        }
        y += S(10);

        // The members.
        var jobsOnly = Config.MeterJobsOnly;
        for (var i = 0; i < c.Rows.Count; i++)
        {
            var r = c.Rows[i];
            Kit.Rule(dl, left, right, y - 0.5f);
            var rowH = S(RowCss);
            PlateArt.JobBadge(dl, new Vector2(left + S(10), y + rowH / 2), 20, r.Member.Job, r.Member.Role, Config.MeterRoleColours);
            var ink = r.Tone switch
            {
                FoodTone.Good => Palette.Ready,
                FoodTone.Short => Palette.Warn,
                FoodTone.Bad => Palette.Danger,
                _ => Palette.Faint,
            };
            var face = r.Tone == FoodTone.Unknown ? Face.Mono : Face.MonoSemibold;
            var tw = Txt.LineRight(dl, right, y, r.Text, face, 12, ink, RowCss);
            var icon = S(13);
            if (r.Tone != FoodTone.Unknown) Icons.Draw(dl, Icon.Utensils, new Vector2(right - tw - S(6) - icon, y + (rowH - icon) / 2), icon, ink);
            var nameX = left + S(30);
            var room = right - tw - S(6) - icon - S(10) - nameX;
            var name = jobsOnly || r.Member.Name.Length == 0 ? r.Member.Job : r.Member.Name;
            if (room > S(16)) Txt.Line(dl, new Vector2(nameX, y), Txt.Fit(name, Face.SansMedium, 13, room), Face.SansMedium, 13, r.Member.IsYou ? Palette.White : Palette.Text, RowCss);
            if (r.Tone == FoodTone.Short && Kit.Pointed(new Vector2(left, y), new Vector2(right, y + rowH)))
                Kit.Tooltip("Their food runs out before your average pull here is over.");
            y += rowH;
        }
        y += S(12);

        // Yours, in a well.
        var boxMin = new Vector2(left, y);
        var boxMax = new Vector2(right, y + S(YouHeight(c)));
        dl.AddRectFilled(boxMin, boxMax, Palette.U32(Palette.Rgba(9, 9, 11, 0.75f)), S(10));
        dl.AddRect(boxMin, boxMax, Palette.U32(Palette.Line), S(10), ImDrawFlags.None, 1);
        var bx = left + S(YouPad);
        var by = y + S(YouPad);
        Kit.Eyebrow(dl, new Vector2(bx, by + (S(16) - Kit.EyebrowH) / 2), "You");
        by += S(16 + 6);
        var x = bx;
        x += Thing(dl, new Vector2(x, by), Icon.Flask, ReadyCheckView.Times("Tinctures", you.Tinctures), Palette.Text) + S(16);
        x += Thing(dl, new Vector2(x, by), Icon.Utensils, ReadyCheckView.Times("Food", you.Meals), Palette.Text) + S(16);
        if (c.Gear == null && you.GearLowest is { } g) Thing(dl, new Vector2(x, by), Icon.Wrench, $"Gear {g}%", Palette.Muted);
        if (c.Gear != null)
        {
            by += S(YouLine + 6);
            Thing(dl, new Vector2(bx, by), Icon.Wrench, c.Gear, Palette.Warn, Face.SansMedium);
        }
        y = boxMax.Y + S(12);

        // The foot: when it goes.
        Foot(dl, left, right, y);
    }

    /// <summary>An icon and its words on a line of yours. Returns the width.</summary>
    private static float Thing(ImDrawListPtr dl, Vector2 at, Icon icon, string text, Vector4 ink, Face face = Face.Sans)
    {
        var size = S(13);
        Icons.Draw(dl, icon, new Vector2(at.X, at.Y + (S(YouLine) - size) / 2), size, ink == Palette.Text ? Palette.Muted : ink);
        return size + S(6) + Txt.Line(dl, new Vector2(at.X + size + S(6), at.Y), text, face, 13, ink, YouLine);
    }

    /// <summary>"Goes when the pull starts", with a thin bar emptying over the 30 seconds after the check ends; "Pinned" while it's kept.</summary>
    private void Foot(ImDrawListPtr dl, float left, float right, float y)
    {
        string words;
        float fill;
        if (Pinned)
        {
            words = "Pinned: unpin it to let it go";
            fill = 0;
        }
        else if (!Previewing && watch.Left is { } l)
        {
            words = $"Goes when the pull starts, or in {Math.Max(0, (int)Math.Ceiling(l))} s";
            fill = (float)(l / ReadyCheckWatch.Linger);
        }
        else
        {
            words = "Goes when the pull starts";
            fill = 1;
        }
        var tw = Txt.LineRight(dl, right, y, Txt.Fit(words, Face.Sans, 12, (right - left) * 0.75f), Face.Sans, 12, Palette.Faint, FootCss);
        var barR = right - tw - S(8);
        if (barR - left < S(20)) return;
        var barY = y + S(FootCss) / 2 - S(1);
        dl.AddRectFilled(new Vector2(left, barY), new Vector2(barR, barY + S(2)), Palette.U32(Palette.Track), S(1));
        if (fill > 0) dl.AddRectFilled(new Vector2(left, barY), new Vector2(left + (barR - left) * Math.Clamp(fill, 0, 1), barY + S(2)), Palette.U32(Palette.Ember), S(1));
    }

    /// <summary>The one-line state: a green tick in a well, "Everyone's fed", the shortest food and your tinctures, the pin.</summary>
    private void DrawCompact(ImDrawListPtr dl, ReadyCard c, Vector2 min, Vector2 max)
    {
        var left = min.X + S(CompactPad);
        var right = max.X - S(CompactPad);
        var y = min.Y + S(CompactPad);
        var badge = S(BadgeCss);
        var bMin = new Vector2(left, y);
        dl.AddRectFilled(bMin, bMin + new Vector2(badge), Palette.U32(Palette.ReadyWash), S(9));
        dl.AddRect(bMin, bMin + new Vector2(badge), Palette.U32(Palette.ReadyLine), S(9), ImDrawFlags.None, 1);
        var icon = S(16);
        Icons.Draw(dl, Icon.Check, bMin + new Vector2((badge - icon) / 2), icon, Palette.Ready);
        var pinLeft = Pin(dl, right, y);
        var tx = left + badge + S(12);
        var tw = MathF.Min(pinLeft - S(8) - tx, S(CompactTextCss(WidthCss)));
        Txt.Line(dl, new Vector2(tx, y), Txt.Fit(c.CompactTitle, Face.SansSemibold, 15, tw), Face.SansSemibold, 15, Palette.White, HeadlineCss);
        var ly = y + S(HeadlineCss + 2);
        foreach (var line in Txt.Wrap(c.CompactLine, Face.Sans, 12, S(CompactTextCss(WidthCss)), 2))
        {
            Txt.Line(dl, new Vector2(tx, ly), line, Face.Sans, 12, Palette.Muted, CompactLineCss);
            ly += S(CompactLineCss);
        }
        if (Kit.Pointed(min, max))
            Kit.Tooltip(c.Average is { } a ? $"{c.Headline}. {a} Your gear is fine." : $"{c.Headline}. Your gear is fine.");
    }
}
