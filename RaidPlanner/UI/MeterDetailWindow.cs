using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using RaidPlanner.Core.Meter;
using RaidPlanner.UI.Model;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>
/// The meter opened up (the mock-ups' "Opened up" and "A death, and its recap"): the same four tabs
/// full size with icons and the deaths counted; the clock and where in the fight; which pull;
/// ENCDPS or DPS; then a table of every column, a row a player (the picked one under the reticle),
/// the limit break under them and the party at the foot, beside the picked player's numbers by
/// ability (DoT ticks their own row, est. when the game's tick was shared). On Deaths, the deaths and
/// the picked one's recap: its last hits and heals with the HP before each, the killing blow on a red wash.
/// </summary>
public sealed class MeterDetailWindow : Window
{
    private readonly Configuration config;
    private readonly Fonts fonts;
    private DateTime drawFailedAt;

    private MeterTab tab = MeterTab.Damage;
    private int? fight;
    private int? player;
    private int? death;

    public MeterDetailWindow(Configuration config, Fonts fonts)
        : base("Raid Planner meter###RaidPlannerMeterDetail")
    {
        this.config = config;
        this.fonts = fonts;
        Size = new Vector2(1180, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(760, 360), MaximumSize = new Vector2(2400, 1600) };
        Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse;
    }

    /// <summary>Where the numbers come from (the small meter's own).</summary>
    public Func<IMeterSource?>? Source { get; set; }

    /// <summary>Back to the small meter.</summary>
    public Action? ShowSmall { get; set; }

    /// <summary>Opens on a tab, a fight (null: the current), a player and a death.</summary>
    public void Show(MeterTab on, int? fightId, int? partyId, int? deathIndex)
    {
        tab = on;
        fight = fightId;
        player = partyId;
        death = deathIndex;
        IsOpen = true;
        BringToFront();
    }

    public override void PreDraw() => Theme.PushFrame();

    public override void PostDraw() => Theme.Pop();

    public override void Draw()
    {
        Kit.ResetFrame();
        Frame.ResetFrame();
        Theme.PushInside();
        try
        {
            using (fonts.Push(Face.Sans, 14))
            {
                try
                {
                    Frame.Window();
                    DrawInside();
                }
                finally
                {
                    Frame.Unwind();
                }
            }
        }
        catch (Exception ex)
        {
            if (DateTime.UtcNow - drawFailedAt > TimeSpan.FromSeconds(10))
            {
                drawFailedAt = DateTime.UtcNow;
                Services.Log.Error(ex, "Raid Planner's meter couldn't draw");
            }
        }
        finally
        {
            Theme.Pop();
        }
    }

    private void DrawInside()
    {
        var m = Source?.Invoke();
        var e = fight is { } id ? m?.Get(id) ?? m?.Current : m?.Current;
        var win = ImGui.GetWindowPos();
        var top = ImGui.GetCursorScreenPos();
        var w = ImGui.GetWindowWidth();
        var dl = Kit.Dl;
        var left = win.X + S(18);
        var inner = w - S(36);
        var y = top.Y + S(12);

        // The tabs, with icons and the deaths counted.
        var deaths = e != null ? MeterView.Deaths(e).Count : 0;
        var tabs = new[]
        {
            new TabSpec("Damage", Icon.Sword), new TabSpec("Healing", Icon.Heart), new TabSpec("Taken", Icon.Shield),
            new TabSpec("Deaths", Icon.Skull, deaths > 0 ? deaths.ToString(System.Globalization.CultureInfo.InvariantCulture) : null),
        };
        tab = (MeterTab)Kit.Tabs("##detail-tab", new Vector2(left, y), tabs, (int)tab, TabSize.Md);

        // The right of the row: Small, ENCDPS or DPS, which pull, a rule, where in the fight, the clock.
        var cy = y + S(H) / 2;
        var x = left + inner;
        const string small = "Small";
        var sw = Kit.ButtonWidth(small, BtnSize.Sm, Icon.PanelRight, Btn.Quiet);
        x -= sw;
        if (Kit.Button("##detail-small", new Vector2(x, cy - S(HSm) / 2), small, Btn.Quiet, BtnSize.Sm, Icon.PanelRight, 0, false, "Back to the small meter"))
        {
            IsOpen = false;
            ShowSmall?.Invoke();
        }
        var ranks = new[] { "ENCDPS", "DPS" };
        var rw = Kit.SegmentedWidth(ranks);
        x -= S(12) + rw;
        var rank = Kit.Segmented("##detail-rank", new Vector2(x, cy - S(HSm) / 2), ranks, (int)config.MeterRank);
        if (rank != (int)config.MeterRank)
        {
            config.MeterRank = (MeterRank)rank;
            config.Save();
        }
        if (e != null && m != null)
        {
            var pick = e.Live ? $"Pull {e.Pull} · now" : $"Pull {e.Pull}";
            var pw = Kit.ButtonWidth(pick, BtnSize.Sm) + S(15 + 6.4f);
            x -= S(12) + pw;
            if (Kit.Button("##detail-pick", new Vector2(x, cy - S(HSm) / 2), pick, Btn.Outline, BtnSize.Sm, null, pw)) ImGui.OpenPopup("##detail-picker");
            Icons.Draw(dl, Icon.ChevronDown, new Vector2(x + pw - S(11.2f) - S(15), cy - S(15) / 2), S(15), Palette.White);
            DrawPicker(m, e);
            // A rule, then where in the fight and the clock.
            x -= S(12);
            dl.AddLine(new Vector2(x, cy - S(10)), new Vector2(x, cy + S(10)), Palette.U32(Palette.Line2), 1f);
            if (MeterView.Where(e) is { } where)
            {
                var ww = Txt.Width(where, Face.Sans, 13);
                x -= S(12) + ww;
                Txt.Line(dl, new Vector2(x, y), where, Face.Sans, 13, Palette.Muted, H);
            }
            var clock = Format.Clock(e.Duration);
            var cw = Txt.Width(clock, Face.MonoSemibold, 18);
            x -= S(12) + cw;
            Txt.Line(dl, new Vector2(x, y), clock, Face.MonoSemibold, 18, Palette.White, H);
            Kit.BeatDot(dl, new Vector2(x - S(7) - S(3), cy), e.Live ? Palette.Ember : Palette.Faint, e.Live);
        }
        y += S(H) + S(12);

        var footH = S(19.5f) * 2 + S(10) + S(14);
        var bottom = win.Y + ImGui.GetWindowHeight() - footH;
        if (e == null)
        {
            var c = new Vector2(win.X + w / 2, y + (bottom - y) / 2 - S(30));
            Boss.Empty(dl, c, 58, BossMood.Asleep, 96);
            Kit.Callout(dl, new Vector2(left, c.Y + S(58)), "No fight yet", 13, inner, 1, 1.1f, center: true);
            return;
        }

        var leftW = MathF.Round(inner * 0.6f);
        var rightX = left + leftW + S(18);
        var rightW = left + inner - rightX;
        if (tab == MeterTab.Deaths) DrawDeaths(dl, e, new Vector2(left, y), leftW, bottom - y, new Vector2(rightX, y), rightW);
        else
        {
            DrawTable(dl, m!, e, new Vector2(left, y), leftW, bottom - y);
            DrawAbilities(dl, m!, e, new Vector2(rightX, y), rightW, bottom - y);
        }

        // The footnote: what Crit, DH and DPS mean.
        var note = tab switch
        {
            MeterTab.Healing => "Healing is what the heals put back: the heal less the overheal. Shields aren't healing, so a scholar reads low here, as in ACT.",
            MeterTab.Taken => "Everything enemies and the floor did to each of you, bleeds and DoTs included: what the game took off your HP.",
            MeterTab.Deaths => "The wipe isn't anyone's death: the deaths in a wipe's last six seconds aren't counted against anyone.",
            _ => "Crit and DH are the share of hits. DPS is each player's damage over their own time, from their first hit to their last: it's higher than ENCDPS for anyone who stopped.",
        };
        Txt.Flow(new Vector2(left, bottom + S(10)), inner, new[] { Run.Note(note) }, 19.5f, out _);
    }

    // ---------------------------------------------------------------------------------------------
    // The table.

    private sealed record Col(string Head, float Width, Func<MeterRow, string> Text, bool Strong = false, Func<MeterRow, bool>? Red = null);

    private Col[] Columns(MeterEncounter e) => tab switch
    {
        MeterTab.Healing => new Col[]
        {
            new("HPS", 62, r => Format.Thousands((long)Math.Round(r.Hps)), true),
            new("Healing", 80, r => Format.Thousands(r.Healing)),
            new("Over", 44, r => Format.PercentWhole(r.OverhealRate)),
            new("%", 42, r => Format.Share(r.HealingShare)),
            new("Crit", 44, r => Format.Share(r.HealCritRate)),
            new("Max heal", 68, r => Format.Thousands(r.MaxHeal)),
        },
        MeterTab.Taken => new Col[]
        {
            new("Taken", 80, r => Format.Thousands(r.Taken), true),
            new("A second", 62, r => Format.Thousands((long)Math.Round(r.TakenPerSecond))),
            new("%", 42, r => Format.Share(r.TakenShare)),
            new("Max hit", 68, r => Format.Thousands(r.MaxTaken)),
            new("Deaths", 40, r => r.Deaths.ToString(System.Globalization.CultureInfo.InvariantCulture), Red: r => r.Deaths > 0),
        },
        _ => new Col[]
        {
            new("ENCDPS", 62, r => Format.Thousands((long)Math.Round(r.EncDps)), config.MeterRank == MeterRank.EncDps),
            new("DPS", 60, r => r.IsLimitBreak ? "–" : Format.Thousands((long)Math.Round(r.Dps)), config.MeterRank == MeterRank.Dps),
            new("Damage", 80, r => Format.Thousands(r.Damage)),
            new("%", 42, r => Format.Share(r.DamageShare)),
            new("Crit", 44, r => r.IsLimitBreak ? "–" : Format.Share(r.CritRate)),
            new("DH", 44, r => r.IsLimitBreak ? "–" : Format.Share(r.DirectHitRate)),
            new("Max hit", 68, r => Format.Thousands(r.MaxHit)),
            new("Deaths", 40, r => r.IsLimitBreak ? string.Empty : r.Deaths.ToString(System.Globalization.CultureInfo.InvariantCulture), Red: r => r.Deaths > 0),
        },
    };

    /// <summary>
    /// The table (`.xg`, a slab): heads in mono capitals, a 34px row a player (the spot, the name, the
    /// job in mono, the columns in mono, the main number white), the limit break, the party's total
    /// under a stronger rule. The picked row on a white wash under the reticle; a death's name muted with a skull.
    /// </summary>
    private void DrawTable(ImDrawListPtr dl, IMeterSource m, MeterEncounter e, Vector2 at, float w, float h)
    {
        dl.AddRectFilled(at, at + new Vector2(w, h), Palette.U32(Palette.Slab), S(Palette.RadiusLg));
        dl.AddRect(at + new Vector2(0.5f), at + new Vector2(w, h) - new Vector2(0.5f), Palette.U32(Palette.Line), S(Palette.RadiusLg), ImDrawFlags.None, 1f);
        var cols = Columns(e);
        var left = at.X + S(6) + S(12);
        var right = at.X + w - S(6) - S(12);
        var gap = S(10);
        // The columns from the right: each right-aligned at its edge.
        var edges = new float[cols.Length];
        var x = right;
        for (var i = cols.Length - 1; i >= 0; i--)
        {
            edges[i] = x;
            x -= S(cols[i].Width) + gap;
        }
        var jobX = x - S(34) + S(10);
        var nameX = left + S(20) + gap;
        var y = at.Y + S(4);
        // Heads.
        var hy = y + (S(30) - Txt.Natural(Face.Mono, 10)) / 2;
        Kit.Label(dl, new Vector2(nameX, hy), "Player", 10, 0.08f, Palette.Faint);
        Kit.Label(dl, new Vector2(jobX, hy), "Job", 10, 0.08f, Palette.Faint);
        for (var i = 0; i < cols.Length; i++)
        {
            var hw = Txt.SpacedWidth(Txt.Caps(cols[i].Head), Face.Mono, 10, 0.08f);
            Kit.Label(dl, new Vector2(edges[i] - hw, hy), cols[i].Head, 10, 0.08f, Palette.Faint);
        }
        y += S(30);
        Kit.Rule(dl, at.X + S(6), at.X + w - S(6), y - 1);
        var rows = MeterView.Ranked(e.Rows, tab, config.MeterRank);
        if (player == null && rows.Count > 0) player = rows[0].PartyId;
        var rowH = S(34);
        var list = new List<MeterRow>(rows);
        if (tab == MeterTab.Damage && MeterView.LimitBreak(e.Rows) is { } lb) list.Add(lb);
        for (var i = 0; i < list.Count; i++)
        {
            var r = list[i];
            if (y + rowH > at.Y + h - S(40)) break;
            var min = new Vector2(at.X + S(6), y);
            var max = new Vector2(at.X + w - S(6), y + rowH);
            var picked = r.PartyId == player && !r.IsLimitBreak;
            var (clicked, hovered, _) = Kit.Hit($"##xg{r.PartyId}{r.IsLimitBreak}", min, max - min, r.IsLimitBreak);
            if (clicked) player = r.PartyId;
            if (picked) dl.AddRectFilled(min, max, Palette.U32(Palette.Rgba(255, 255, 255, 0.06f)), S(Palette.RadiusSm));
            else if (hovered) dl.AddRectFilled(min, max, Palette.U32(Palette.Hover), S(Palette.RadiusSm));
            else if (i > 0 && list[i - 1].PartyId != player) Kit.Rule(dl, min.X, max.X, y - 0.5f);
            Kit.Reticle(dl, $"##xg-ret{r.PartyId}", min, max, picked || hovered);
            if (r.IsLimitBreak) Kit.LimitMark(dl, new Vector2(left, y + (rowH - S(18)) / 2));
            else Kit.Spot(dl, new Vector2(left + S(10), y + rowH / 2), 20, r.Slot, Palette.RoleColor(r.Role));
            var name = r.IsLimitBreak ? "Limit break" : config.MeterJobsOnly ? r.Job : r.Name;
            var nw = Txt.Line(dl, new Vector2(nameX, y), Txt.Fit(name, Face.SansMedium, 14, jobX - S(10) - nameX - (r.Dead ? S(18) : 0)), Face.SansMedium, 14,
                r.Dead ? Palette.Muted : Palette.White, 34);
            if (r.Dead) Icons.Draw(dl, Icon.Skull, new Vector2(nameX + nw + S(6), y + (rowH - S(12)) / 2), S(12), Palette.Danger);
            if (!r.IsLimitBreak && !config.MeterJobsOnly) Kit.Label(dl, new Vector2(jobX, y + (rowH - Txt.Natural(Face.Mono, 10)) / 2), r.Job, 10, 0.06f, Palette.Faint);
            for (var c = 0; c < cols.Length; c++)
            {
                var col = cols[c];
                var text = col.Text(r);
                var color = col.Red?.Invoke(r) == true ? Palette.Danger : col.Strong ? Palette.White : Palette.Text;
                Txt.LineRight(dl, edges[c], y, text, col.Strong ? Face.MonoSemibold : Face.Mono, 13, color, 34);
            }
            y += rowH;
        }
        // The party at the foot.
        Kit.Rule(dl, at.X + S(6), at.X + w - S(6), y, Palette.Line3);
        Txt.Line(dl, new Vector2(nameX, y), "Party", Face.SansMedium, 14, Palette.White, 34);
        var p = e.Party;
        for (var c = 0; c < cols.Length; c++)
        {
            var col = cols[c];
            var text = col.Head switch
            {
                "Deaths" => MeterView.Deaths(e).Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "%" => "100.0",
                "Crit" or "DH" or "Max hit" or "Max heal" or "Over" => string.Empty,
                _ => col.Text(p),
            };
            Txt.LineRight(dl, edges[c], y, text, col.Strong ? Face.MonoSemibold : Face.Mono, 13, Palette.White, 34);
        }
    }

    /// <summary>
    /// By ability (`.ab`), for the picked player: the head (their spot, "Rook · DRG", how many abilities
    /// and hits, their total), then a 30px row an ability, biggest first (a DoT's ticks tagged, est. when
    /// shared), a slim bar each to the biggest; the rest summed as "9 more".
    /// </summary>
    private void DrawAbilities(ImDrawListPtr dl, IMeterSource m, MeterEncounter e, Vector2 at, float w, float h)
    {
        dl.AddRectFilled(at, at + new Vector2(w, h), Palette.U32(Palette.Slab), S(Palette.RadiusLg));
        dl.AddRect(at + new Vector2(0.5f), at + new Vector2(w, h) - new Vector2(0.5f), Palette.U32(Palette.Line), S(Palette.RadiusLg), ImDrawFlags.None, 1f);
        var row = e.Rows.FirstOrDefault(r => r.PartyId == player && !r.IsLimitBreak);
        if (row == null) return;
        var abilities = m.Abilities(e.Id, row.PartyId, tab);
        var x = at.X + S(8) + S(10);
        var right = at.X + w - S(8) - S(10);
        var y = at.Y + S(12);
        Kit.Spot(dl, new Vector2(x + S(13), y + S(19)), 26, row.Slot, Palette.RoleColor(row.Role));
        var tx = x + S(26) + S(12);
        var total = tab switch
        {
            MeterTab.Healing => row.Healing,
            MeterTab.Taken => row.Taken,
            _ => row.Damage,
        };
        var totalText = Format.Thousands(total);
        var totalW = Txt.Width(totalText, Face.MonoSemibold, 13);
        var title = config.MeterJobsOnly ? row.Job : $"{row.Name} · {row.Job}";
        Txt.Line(dl, new Vector2(tx, y), Txt.Fit(title, Face.SansSemibold, 15, right - totalW - S(12) - tx), Face.SansSemibold, 15, Palette.White, 15 * 1.25f);
        var hits = abilities.Sum(a => a.Hits);
        var by = tab == MeterTab.Taken ? "By what hit them" : "By ability";
        var sub = $"{by} · {CountOf(abilities.Count, "ability", "abilities")}, {CountOf(hits, "hit", "hits")}";
        Txt.Line(dl, new Vector2(tx, y + S(15 * 1.25f)), Txt.Fit(sub, Face.Sans, 13, right - tx), Face.Sans, 13, Palette.Muted, 19.5f);
        Txt.LineRight(dl, right, y, totalText, Face.MonoSemibold, 13, Palette.White, 15 * 1.25f);
        y += S(38) + S(10);

        // Heads: Ability · Damage · % · Hits · Crit · DH · Max.
        var heads = new[] { tab == MeterTab.Healing ? "Healing" : tab == MeterTab.Taken ? "Taken" : "Damage", "%", "Hits", "Crit", "DH", "Max" };
        var widths = new[] { 64f, 36, 30, 36, 36, 56 };
        if (tab != MeterTab.Damage)
        {
            heads = heads.Where(hd => hd != "DH").ToArray();
            widths = new[] { 64f, 36, 30, 36, 56 };
        }
        var edges = new float[heads.Length];
        var ex = right;
        for (var i = heads.Length - 1; i >= 0; i--)
        {
            edges[i] = ex;
            ex -= S(widths[i]) + S(8);
        }
        var hy = y + (S(26) - Txt.Natural(Face.Mono, 9)) / 2;
        Kit.Label(dl, new Vector2(x, hy), tab == MeterTab.Taken ? "What hit" : "Ability", 9, 0.08f, Palette.Faint);
        for (var i = 0; i < heads.Length; i++)
        {
            var hw = Txt.SpacedWidth(Txt.Caps(heads[i]), Face.Mono, 9, 0.08f);
            Kit.Label(dl, new Vector2(edges[i] - hw, hy), heads[i], 9, 0.08f, Palette.Faint);
        }
        y += S(26);
        Kit.Rule(dl, at.X + S(8), at.X + w - S(8), y - 1);
        var rowH = S(30);
        var room = (int)Math.Max(1, (at.Y + h - S(8) - y) / rowH) - 1;
        var biggest = abilities.Count > 0 ? abilities.Max(a => a.Amount) : 0;
        var shown = abilities.Take(abilities.Count > room ? Math.Max(0, room) : abilities.Count).ToList();
        foreach (var a in shown)
        {
            var nameRoom = ex - x;
            var nx = x;
            nx += Txt.Line(dl, new Vector2(nx, y), Txt.Fit(a.Name, Face.SansMedium, 13, nameRoom - (a.IsTick ? S(40) : 0) - (a.Estimated ? S(40) : 0)), Face.SansMedium, 13, Palette.White, 30);
            if (a.IsTick) nx += S(6) + Kit.Tag(dl, new Vector2(nx + S(6), y + (rowH - S(16)) / 2), "DoT", TagKind.Plain, 16);
            if (a.Estimated) Kit.Tag(dl, new Vector2(nx + S(6), y + (rowH - S(16)) / 2), "est.", TagKind.Plain, 16);
            var cells = tab == MeterTab.Damage
                ? new[] { Format.Thousands(a.Amount), Format.Share(a.Share), a.Hits.ToString(System.Globalization.CultureInfo.InvariantCulture), Rate(a.CritRate), Rate(a.DirectHitRate), Format.Thousands(a.Max) }
                : new[] { Format.Thousands(a.Amount), Format.Share(a.Share), a.Hits.ToString(System.Globalization.CultureInfo.InvariantCulture), Rate(a.CritRate), Format.Thousands(a.Max) };
            for (var i = 0; i < cells.Length; i++)
                Txt.LineRight(dl, edges[i], y, cells[i], Face.Mono, 12, i == 0 ? Palette.White : Palette.Text, 30);
            // The bar under it, to the biggest.
            var ba = new Vector2(at.X + S(8) + S(10), y + rowH - S(2) - S(2));
            var bb = new Vector2(at.X + w - S(8) - S(10), y + rowH - S(2));
            dl.AddRectFilled(ba, bb, Palette.U32(Palette.Rgba(255, 255, 255, 0.04f)));
            if (biggest > 0) dl.AddRectFilled(ba, new Vector2(ba.X + (bb.X - ba.X) * a.Amount / biggest, bb.Y), Palette.U32(Palette.Rgba(255, 255, 255, 0.22f)));
            y += rowH;
        }
        if (abilities.Count > shown.Count)
        {
            var rest = abilities.Skip(shown.Count).ToList();
            Txt.Line(dl, new Vector2(x, y), $"{rest.Count} more", Face.SansMedium, 13, Palette.Muted, 30);
            Txt.LineRight(dl, edges[0], y, Format.Thousands(rest.Sum(r => r.Amount)), Face.Mono, 12, Palette.Muted, 30);
            Txt.LineRight(dl, edges[1], y, Format.Share(rest.Sum(r => r.Share)), Face.Mono, 12, Palette.Muted, 30);
            Txt.LineRight(dl, edges[2], y, rest.Sum(r => r.Hits).ToString(System.Globalization.CultureInfo.InvariantCulture), Face.Mono, 12, Palette.Muted, 30);
        }
        if (abilities.Count == 0) Txt.Line(dl, new Vector2(x, y), "Nothing yet.", Face.Sans, 13, Palette.Muted, 30);
    }

    private static string Rate(double? r) => r is { } v ? Format.Share(v) : "–";

    private static string CountOf(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{Format.Thousands(n)} {many}";

    // ---------------------------------------------------------------------------------------------
    // Deaths and the recap.

    private void DrawDeaths(ImDrawListPtr dl, MeterEncounter e, Vector2 at, float w, float h, Vector2 recapAt, float recapW)
    {
        dl.AddRectFilled(at, at + new Vector2(w, h), Palette.U32(Palette.Slab), S(Palette.RadiusLg));
        dl.AddRect(at + new Vector2(0.5f), at + new Vector2(w, h) - new Vector2(0.5f), Palette.U32(Palette.Line), S(Palette.RadiusLg), ImDrawFlags.None, 1f);
        var all = e.Deaths.OrderBy(d => d.At).ToList();
        if (death == null || death >= e.Deaths.Count) death = all.Count > 0 ? e.Deaths.ToList().IndexOf(all.FirstOrDefault(d => !d.PartOfWipe) ?? all[0]) : null;
        var y = at.Y + S(6);
        var rowH = S(46);
        foreach (var d in all)
        {
            if (y + rowH > at.Y + h - S(6)) break;
            var index = e.Deaths.ToList().IndexOf(d);
            var min = new Vector2(at.X + S(6), y);
            var max = new Vector2(at.X + w - S(6), y + rowH);
            var picked = index == death;
            var (clicked, hovered, _) = Kit.Hit($"##dd{index}", min, max - min);
            if (clicked) death = index;
            if (picked || hovered) dl.AddRectFilled(min, max, Palette.U32(picked ? Palette.Rgba(255, 255, 255, 0.06f) : Palette.Hover), S(Palette.RadiusSm));
            Kit.Reticle(dl, $"##dd-ret{index}", min, max, picked || hovered);
            var x = min.X + S(12);
            var role = e.Rows.FirstOrDefault(r => r.PartyId == d.PartyId)?.Role;
            Kit.Spot(dl, new Vector2(x + S(10), y + rowH / 2), 20, d.Slot, Palette.RoleColor(role));
            x += S(20) + S(10);
            var name = config.MeterJobsOnly ? d.Slot : d.Name;
            var color = d.PartOfWipe ? Palette.Muted : Palette.White;
            Txt.Line(dl, new Vector2(x, y + S(6)), Txt.Fit(name, Face.SansMedium, 14, w * 0.4f), Face.SansMedium, 14, color, 18);
            var sub = d.PartOfWipe ? $"The wipe · {d.KilledBy}" : $"{d.KilledBy} · {Format.Thousands(d.Amount)} · {Format.Thousands(d.Overkill)} over";
            Txt.Line(dl, new Vector2(x, y + S(24)), Txt.Fit(sub, Face.Sans, 12, max.X - S(80) - x), Face.Sans, 12, Palette.Muted, 16);
            Txt.LineRight(dl, max.X - S(12), y, Format.Clock(d.At), Face.MonoSemibold, 12, d.PartOfWipe ? Palette.Muted : Palette.Danger, 46);
            y += rowH;
        }
        if (all.Count == 0) Txt.Line(dl, new Vector2(at.X + S(18), y), "Nobody died.", Face.Sans, 13, Palette.Muted, 30);

        // The recap.
        dl.AddRectFilled(recapAt, recapAt + new Vector2(recapW, h), Palette.U32(Palette.Slab), S(Palette.RadiusLg));
        dl.AddRect(recapAt + new Vector2(0.5f), recapAt + new Vector2(recapW, h) - new Vector2(0.5f), Palette.U32(Palette.Line), S(Palette.RadiusLg), ImDrawFlags.None, 1f);
        if (death is { } di && di < e.Deaths.Count) Recap(dl, e, e.Deaths[di], recapAt, recapW, h);
    }

    /// <summary>
    /// A death's recap (`.rc`): "Odette died at 2:51" as a small callout, the killing blow and how far
    /// over; then its last hits and heals, oldest first: the time to a tenth, what it was (and from whom,
    /// what made it worse), its amount (a hit red, a heal green), the HP before it as a bar; the killing
    /// blow on a red wash.
    /// </summary>
    private void Recap(ImDrawListPtr dl, MeterEncounter e, DeathEntry d, Vector2 at, float w, float h)
    {
        var x = at.X + S(18);
        var inner = w - S(36);
        var y = at.Y + S(14);
        var role = e.Rows.FirstOrDefault(r => r.PartyId == d.PartyId)?.Role;
        Kit.Spot(dl, new Vector2(x + S(13), y + S(18)), 26, d.Slot, Palette.RoleColor(role));
        var tx = x + S(26) + S(12);
        var who = config.MeterJobsOnly ? d.Slot : d.Name;
        var th = Kit.Callout(dl, new Vector2(tx, y), $"{who} died at {Format.Clock(d.At)}", 17, inner - S(38), 1, 1.05f);
        Txt.Line(dl, new Vector2(tx, y + th + S(4)), Txt.Fit($"{d.KilledBy} · {Format.Thousands(d.Amount)} · {Format.Thousands(d.Overkill)} over what was left", Face.Sans, 13, inner - S(38)),
            Face.Sans, 13, Palette.Muted, 19.5f);
        y += MathF.Max(S(38), th + S(4) + S(19.5f)) + S(10);

        // Heads.
        var left = at.X + S(6) + S(14);
        var right = at.X + w - S(6) - S(14);
        var hpW = S(120);
        var amountR = right - hpW - S(12);
        var whatX = left + S(50) + S(12);
        var hy = y + (S(26) - Txt.Natural(Face.Mono, 9)) / 2;
        Kit.Label(dl, new Vector2(left, hy), "Time", 9, 0.08f, Palette.Faint);
        Kit.Label(dl, new Vector2(whatX, hy), "What", 9, 0.08f, Palette.Faint);
        var aw = Txt.SpacedWidth(Txt.Caps("Amount"), Face.Mono, 9, 0.08f);
        Kit.Label(dl, new Vector2(amountR - aw, hy), "Amount", 9, 0.08f, Palette.Faint);
        var hw = Txt.SpacedWidth(Txt.Caps("HP before"), Face.Mono, 9, 0.08f);
        Kit.Label(dl, new Vector2(right - hw, hy), "HP before", 9, 0.08f, Palette.Faint);
        y += S(26);
        var max = MeterView.RecapMax(d.Recap);
        var rowH = S(40);
        for (var i = 0; i < d.Recap.Count; i++)
        {
            var l = d.Recap[i];
            if (y + rowH > at.Y + h - S(30)) break;
            var kill = i == d.Recap.Count - 1 && l.Amount < 0;
            var min = new Vector2(at.X + S(6), y);
            var rmax = new Vector2(at.X + w - S(6), y + rowH);
            if (kill) dl.AddRectFilled(min, rmax, Palette.U32(Palette.DangerWash), S(Palette.RadiusSm));
            else if (i > 0) Kit.Rule(dl, min.X, rmax.X, y - 0.5f);
            Txt.Line(dl, new Vector2(left, y), Format.ClockTenths(l.At), Face.Mono, 12, kill ? Palette.Danger : Palette.Text, 40);
            var whatTop = y + (rowH - S(13 * 1.2f) - S(2) - S(11 * 1.3f)) / 2;
            Txt.Line(dl, new Vector2(whatX, whatTop), Txt.Fit(l.What, Face.SansMedium, 13, amountR - S(80) - whatX), Face.SansMedium, 13, Palette.White, 13 * 1.2f);
            if (l.Note.Length > 0)
                Txt.Line(dl, new Vector2(whatX, whatTop + S(13 * 1.2f) + S(2)), Txt.Fit(l.Note, Face.Sans, 11, amountR - S(80) - whatX), Face.Sans, 11, Palette.Muted, 11 * 1.3f);
            if (l.Amount != 0)
                Txt.LineRight(dl, amountR, y, Format.Signed(l.Amount), Face.MonoSemibold, 12, l.Amount < 0 ? Palette.DangerSoft : Palette.Ready, 40);
            // The HP before, and its bar: low in warn, out in danger.
            var bx = right - hpW;
            var vy = y + (rowH - S(11) - S(4) - S(4)) / 2;
            Txt.LineRight(dl, right, vy, Format.Thousands(l.HpBefore), Face.Mono, 11, Palette.Muted, 11);
            var barY = vy + S(11) + S(4);
            dl.AddRectFilled(new Vector2(bx, barY), new Vector2(right, barY + S(4)), Palette.U32(Palette.Rgba(255, 255, 255, 0.08f)), S(2));
            var f = max > 0 ? Math.Clamp(l.HpBefore / (float)max, 0, 1) : 0;
            var col = l.HpBefore <= 0 ? Palette.Danger : f < 0.3f ? Palette.Warn : Palette.Rgba(255, 255, 255, 0.6f);
            if (f > 0) dl.AddRectFilled(new Vector2(bx, barY), new Vector2(bx + hpW * f, barY + S(4)), Palette.U32(col), S(2));
            y += rowH;
        }
        if (d.Recap.Count == 0) Txt.Line(dl, new Vector2(left, y), "Nothing was kept of what hit them.", Face.Sans, 13, Palette.Muted, 30);
        Txt.Flow(new Vector2(at.X + S(18), at.Y + h - S(30)), w - S(36), new[] { Run.Note("The last ten hits and heals are kept; these are the ones before the death.") }, 19.5f, out _);
    }

    /// <summary>Which pull, in the opened-up window: the same picker as the small meter's, without pace.</summary>
    private void DrawPicker(IMeterSource m, MeterEncounter shown)
    {
        ImGui.SetNextWindowSizeConstraints(new Vector2(S(260), 0), new Vector2(S(300), S(460)));
        if (!Kit.BeginMenu("##detail-picker")) return;
        try
        {
            var w = S(300) - S(11.2f);
            var dl = ImGui.GetWindowDrawList();
            var (here, others) = MeterView.Picker(m.Kept, shown, m.Best(shown.Content));
            if (here.Count > 0) Kit.MenuEyebrow($"Tonight · {here[0].Fight.Duty}", w);
            foreach (var r in here.Concat(others))
            {
                var f = r.Fight;
                var title = r.Best ? $"Pull {f.Pull} · the best" : $"Pull {f.Pull}";
                var (clicked, _, min, max) = Kit.MenuRow("##dpick" + f.Id, w, title, f.Content == shown.Content ? null : f.Duty, true, 16, S(60));
                var (slot, right) = Kit.MenuRowParts(min, max, true, 16);
                if (r.Shown) Kit.Tick(dl, slot + new Vector2(S(8)), 16);
                else if (r.Best) Icons.Draw(dl, Icon.Star, slot + new Vector2((S(16) - S(13)) / 2), S(13), Palette.White);
                Txt.LineRight(dl, right, min.Y, Format.Clock(f.Duration), Face.Mono, 12, Palette.Faint, (max.Y - min.Y) / S(1));
                if (clicked)
                {
                    fight = r.Live ? null : f.Id;
                    death = null;
                    ImGui.CloseCurrentPopup();
                }
            }
        }
        finally
        {
            Kit.EndMenu();
        }
    }
}
