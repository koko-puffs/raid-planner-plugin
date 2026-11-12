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

    private static readonly string[] Ranks = { "ENCDPS", "DPS" };

    private MeterTab tab = MeterTab.Damage;
    private int? fight;
    private int? player;
    private int? death;

    /// <summary>The numbers' source last drawn: when it changes (the meter's sample starts or ends) the picks made in the other are let go.</summary>
    private IMeterSource? lastSource;

    /// <summary>When the fight was copied as text (the button says so for a moment).</summary>
    private DateTime copiedAt;

    public MeterDetailWindow(Configuration config, Fonts fonts)
        : base("Raid Planner meter###RaidPlannerMeterDetail")
    {
        this.config = config;
        this.fonts = fonts;
        Size = new Vector2(1180, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
        // At least as wide as the five tabs and the head's buttons side by side; the table and the abilities sit side by side from about 1100.
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(980, 360), MaximumSize = new Vector2(2400, 1600) };
        Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse;
    }

    /// <summary>Where the numbers come from (the small meter's own).</summary>
    public Func<IMeterSource?>? Source { get; set; }

    /// <summary>Back to the small meter.</summary>
    public Action? ShowSmall { get; set; }

    /// <summary>Opens on a tab, a fight (null: the current), a player and a death.</summary>
    public void Show(MeterTab on, int? fightId, int? partyId, int? deathIndex)
    {
        tab = MeterLayout.Known(on);
        fight = fightId;
        player = partyId;
        death = deathIndex;
        lastSource = Source?.Invoke();
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
        if (!ReferenceEquals(m, lastSource))
        {
            // A sample's ids mean nothing in the real numbers (nor the other way): back to the current fight.
            lastSource = m;
            fight = null;
            player = null;
            death = null;
        }
        var e = fight is { } id ? m?.Get(id) ?? m?.Current : m?.Current;
        var win = ImGui.GetWindowPos();
        var top = ImGui.GetCursorScreenPos();
        var w = ImGui.GetWindowWidth();
        var dl = Kit.Dl;
        var left = win.X + S(18);
        var inner = w - S(36);
        var y = top.Y + S(12);

        // The tabs, with icons and the deaths and events counted.
        var deaths = e != null ? MeterView.Deaths(e).Count : 0;
        var events = e != null && m != null ? m.Events(e.Id) : Array.Empty<MeterEvent>();
        var tabs = new[]
        {
            new TabSpec("Damage", Icon.Sword), new TabSpec("Healing", Icon.Heart), new TabSpec("Taken", Icon.Shield),
            new TabSpec("Deaths", Icon.Skull, deaths > 0 ? deaths.ToString(System.Globalization.CultureInfo.InvariantCulture) : null),
            new TabSpec("Events", Icon.List, events.Count > 0 ? events.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) : null),
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
        // Copy as text: the fight shown as a mini-parse for chat (it says Copied for a moment).
        var copied = DateTime.UtcNow - copiedAt < TimeSpan.FromSeconds(2.5);
        x -= S(8) + S(HSm);
        if (Kit.IconButton("##detail-copy", new Vector2(x, cy - S(HSm) / 2), copied ? Icon.Check : Icon.Copy,
                copied ? "Copied to the clipboard" : "Copy as text: the fight, its length, each player's ENCDPS, share and deaths, a line each (for chat)", Btn.Quiet, BtnSize.Sm,
                e == null) && e != null)
        {
            ImGui.SetClipboardText(MeterText.Parse(e, config.MeterJobsOnly));
            copiedAt = DateTime.UtcNow;
        }
        var rw = Kit.SegmentedWidth(Ranks);
        x -= S(12) + rw;
        var rank = Kit.Segmented("##detail-rank", new Vector2(x, cy - S(HSm) / 2), Ranks, (int)config.MeterRank);
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
            // A rule, then where in the fight and the clock, as they fit beside the tabs (the clock first).
            var tabsRight = left + Kit.TabsWidth(tabs, TabSize.Md) + S(12);
            x -= S(12);
            dl.AddLine(new Vector2(x, cy - S(10)), new Vector2(x, cy + S(10)), Palette.U32(Palette.Line2), 1f);
            var clock = Format.Clock(e.Duration);
            var cw = Txt.Width(clock, Face.MonoSemibold, 18);
            var clockRoom = S(12) + cw + S(7) + S(6);
            if (MeterView.Where(e) is { } where)
            {
                var ww = Txt.Width(where, Face.Sans, 13);
                if (x - S(12) - ww - clockRoom >= tabsRight)
                {
                    x -= S(12) + ww;
                    Txt.Line(dl, new Vector2(x, y), where, Face.Sans, 13, Palette.Muted, H);
                }
            }
            if (x - clockRoom >= tabsRight)
            {
                x -= S(12) + cw;
                Txt.Line(dl, new Vector2(x, y), clock, Face.MonoSemibold, 18, Palette.White, H);
                Kit.BeatDot(dl, new Vector2(x - S(7) - S(3), cy), e.Live ? Palette.Ember : Palette.Faint, e.Live);
            }
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

        if (tab == MeterTab.Deaths)
        {
            // The deaths beside the picked one's recap (the recap has the more to say).
            var listW = MathF.Round(inner * 0.4f);
            var recapX = left + listW + S(18);
            DrawDeaths(dl, e, new Vector2(left, y), listW, bottom - y, new Vector2(recapX, y), left + inner - recapX);
        }
        else if (tab == MeterTab.Events)
        {
            // Each player's events counted beside the fight's events, oldest first.
            var leftW = MathF.Round(MathF.Min(MathF.Max(inner * 0.6f, S(600)), inner - S(18) - S(300)));
            DrawTable(dl, m!, e, new Vector2(left, y), leftW, bottom - y);
            var rightX = left + leftW + S(18);
            DrawEventLog(dl, e, events, new Vector2(rightX, y), left + inner - rightX, bottom - y);
        }
        else
        {
            // The table beside the picked player by ability when there's room for both (the table's
            // columns want about 700, the abilities' 400); narrower, the table alone, all its columns readable.
            var both = inner >= S(1060);
            var leftW = both ? MathF.Round(Math.Clamp(inner * 0.6f, S(640), inner - S(18) - S(400))) : inner;
            DrawTable(dl, m!, e, new Vector2(left, y), leftW, bottom - y);
            if (both)
            {
                var rightX = left + leftW + S(18);
                DrawAbilities(dl, m!, e, new Vector2(rightX, y), left + inner - rightX, bottom - y);
            }
        }

        // The footnote: what Crit, DH and DPS mean.
        var note = tab switch
        {
            MeterTab.Healing => "Healing is what the heals put back: the heal less the overheal. Shields aren't healing, so a scholar reads low here, as in ACT.",
            MeterTab.Taken => "Everything enemies and the floor did to each of you, bleeds and DoTs included: what the game took off your HP.",
            MeterTab.Deaths => "The wipe isn't anyone's death: the deaths in a wipe's last six seconds aren't counted against anyone. A raise is the dead member's HP coming back; who raised them is whose Raise was on them.",
            MeterTab.Events => "Casts are the GCDs used. Interrupts are the enemy casts stopped, credited to whoever's interrupt hit it in the second before (the game doesn't say who). Time dead leaves out the wipe.",
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
            new("Raises", 46, r => MeterText.Count(r.RaisesGiven)),
        },
        MeterTab.Taken => new Col[]
        {
            new("Taken", 80, r => Format.Thousands(r.Taken), true),
            new("A second", 62, r => Format.Thousands((long)Math.Round(r.TakenPerSecond))),
            new("%", 42, r => Format.Share(r.TakenShare)),
            new("Max hit", 68, r => Format.Thousands(r.MaxTaken)),
            new("Deaths", 40, r => r.Deaths.ToString(System.Globalization.CultureInfo.InvariantCulture), Red: r => r.Deaths > 0),
            new("Time dead", 62, r => MeterText.Dead(r.DeadSeconds), Red: r => r.DeadSeconds > 0),
        },
        MeterTab.Events => new Col[]
        {
            new("Casts", 46, r => MeterText.Count(r.Casts), true),
            new("Potions", 50, r => MeterText.Count(r.Potions)),
            new("Interrupts", 66, r => MeterText.Count(r.Interrupts)),
            new("Raises", 46, r => MeterText.Count(r.RaisesGiven)),
            new("Raised", 46, r => MeterText.Count(r.Raised)),
            new("Time dead", 62, r => MeterText.Dead(r.DeadSeconds), Red: r => r.DeadSeconds > 0),
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
        var (cols, list, cells, partyCells) = TableOf(e);
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
        // The player picked, else (none yet, or not in this fight: another pull picked) the top one.
        if (list.Count > 0 && !list[0].IsLimitBreak && !list.Exists(r => r.PartyId == player && !r.IsLimitBreak)) player = list[0].PartyId;
        var rowH = S(34);
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
            var nameRoom = jobX - S(10) - nameX - (r.Dead ? S(18) : 0);
            var nw = Txt.Line(dl, new Vector2(nameX, y), Txt.Fit(name, Face.SansMedium, 14, nameRoom), Face.SansMedium, 14, r.Dead ? Palette.Muted : Palette.White, 34);
            if (r.Dead) Icons.Draw(dl, Icon.Skull, new Vector2(nameX + nw + S(6), y + (rowH - S(12)) / 2), S(12), Palette.Danger);
            // The limit break's own action after its name, quieter (the mock-up's "LB2 at 0:58": the model has its biggest hit's).
            if (r.IsLimitBreak && r.MaxHitName.Length > 0 && nameRoom - nw - S(8) > S(30))
                Txt.Line(dl, new Vector2(nameX + nw + S(8), y), Txt.Fit(r.MaxHitName, Face.Sans, 12, nameRoom - nw - S(8)), Face.Sans, 12, Palette.Muted, 34);
            if (!r.IsLimitBreak && !config.MeterJobsOnly) Kit.Label(dl, new Vector2(jobX, y + (rowH - Txt.Natural(Face.Mono, 10)) / 2), r.Job, 10, 0.06f, Palette.Faint);
            for (var c = 0; c < cols.Length; c++)
            {
                var col = cols[c];
                var color = col.Red?.Invoke(r) == true ? Palette.Danger : col.Strong ? Palette.White : Palette.Text;
                Txt.LineRight(dl, edges[c], y, cells[i][c], col.Strong ? Face.MonoSemibold : Face.Mono, 13, color, 34);
            }
            y += rowH;
        }
        // The party at the foot.
        Kit.Rule(dl, at.X + S(6), at.X + w - S(6), y, Palette.Line3);
        Txt.Line(dl, new Vector2(nameX, y), "Party", Face.SansMedium, 14, Palette.White, 34);
        for (var c = 0; c < cols.Length; c++)
            Txt.LineRight(dl, edges[c], y, partyCells[c], cols[c].Strong ? Face.MonoSemibold : Face.Mono, 13, Palette.White, 34);
    }

    private (MeterEncounter? Of, MeterTab Tab, MeterRank Rank, Col[] Cols, List<MeterRow> Rows, string[][] Cells, string[] Party) table =
        (null, MeterTab.Damage, MeterRank.EncDps, Array.Empty<Col>(), new List<MeterRow>(), Array.Empty<string[]>(), Array.Empty<string>());

    /// <summary>
    /// The table's columns, its rows in order (the limit break last) and every cell in words, worked out
    /// once a snapshot (a few times a second while a fight is on), not each frame. The party's row leaves
    /// out what doesn't add up (crit, DH, the biggest hit, DPS: as the mock-up leaves them).
    /// </summary>
    private (Col[] Cols, List<MeterRow> Rows, string[][] Cells, string[] Party) TableOf(MeterEncounter e)
    {
        var rank = config.MeterRank;
        if (!ReferenceEquals(table.Of, e) || table.Tab != tab || table.Rank != rank)
        {
            var cols = Columns(e);
            // The events' counts in spot order (there's no one number to rank them by); the rest biggest first.
            var rows = tab == MeterTab.Events ? MeterView.InSpotOrder(e.Rows) : MeterView.Ranked(e.Rows, tab, rank);
            if (tab == MeterTab.Damage && MeterView.LimitBreak(e.Rows) is { } lb) rows.Add(lb);
            var cells = new string[rows.Count][];
            for (var i = 0; i < rows.Count; i++)
            {
                cells[i] = new string[cols.Length];
                for (var c = 0; c < cols.Length; c++) cells[i][c] = cols[c].Text(rows[i]);
            }
            var deaths = MeterView.Deaths(e).Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var party = new string[cols.Length];
            for (var c = 0; c < cols.Length; c++)
                party[c] = cols[c].Head switch
                {
                    "Deaths" => deaths,
                    "%" => "100.0",
                    "DPS" or "Crit" or "DH" or "Max hit" or "Max heal" or "Over" => string.Empty,
                    _ => cols[c].Text(e.Party),
                };
            table = (e, tab, rank, cols, rows, cells, party);
        }
        return (table.Cols, table.Rows, table.Cells, table.Party);
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
        var view = AbilitiesOf(abilities, total);
        var totalW = Txt.Width(view.Total, Face.MonoSemibold, 13);
        var title = config.MeterJobsOnly ? row.Job : $"{row.Name} · {row.Job}";
        Txt.Line(dl, new Vector2(tx, y), Txt.Fit(title, Face.SansSemibold, 15, right - totalW - S(12) - tx), Face.SansSemibold, 15, Palette.White, 15 * 1.25f);
        Txt.Line(dl, new Vector2(tx, y + S(15 * 1.25f)), Txt.Fit(view.Sub, Face.Sans, 13, right - tx), Face.Sans, 13, Palette.Muted, 19.5f);
        Txt.LineRight(dl, right, y, view.Total, Face.MonoSemibold, 13, Palette.White, 15 * 1.25f);
        y += S(38) + S(10);

        // Heads: Ability · Damage · % · Hits · Crit · DH · Max (no DH but for damage).
        var heads = tab switch
        {
            MeterTab.Healing => HealingHeads,
            MeterTab.Taken => TakenHeads,
            _ => DamageHeads,
        };
        var widths = tab == MeterTab.Damage ? DamageWidths : OtherWidths;
        Span<float> edges = stackalloc float[heads.Length];
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
        // As many as fit; the rest summed on the last line ("9 more") when they don't all fit.
        var fits = (int)Math.Max(1, (at.Y + h - S(8) - y) / rowH);
        var shown = abilities.Count <= fits ? abilities.Count : Math.Max(0, fits - 1);
        for (var n = 0; n < shown; n++)
        {
            var a = abilities[n];
            var nameRoom = ex - x;
            var nx = x;
            nx += Txt.Line(dl, new Vector2(nx, y), Txt.Fit(a.Name, Face.SansMedium, 13, nameRoom - (a.IsTick ? S(40) : 0) - (a.Estimated ? S(40) : 0)), Face.SansMedium, 13, Palette.White, 30);
            if (a.IsTick) nx += S(6) + Kit.Tag(dl, new Vector2(nx + S(6), y + (rowH - S(16)) / 2), "DoT", TagKind.Plain, 16);
            if (a.Estimated) Kit.Tag(dl, new Vector2(nx + S(6), y + (rowH - S(16)) / 2), "est.", TagKind.Plain, 16);
            var cells = view.Cells[n];
            for (var i = 0; i < cells.Length && i < edges.Length; i++)
                Txt.LineRight(dl, edges[i], y, cells[i], Face.Mono, 12, i == 0 ? Palette.White : Palette.Text, 30);
            // The bar under it, to the biggest.
            var ba = new Vector2(at.X + S(8) + S(10), y + rowH - S(2) - S(2));
            var bb = new Vector2(at.X + w - S(8) - S(10), y + rowH - S(2));
            dl.AddRectFilled(ba, bb, Palette.U32(Palette.Rgba(255, 255, 255, 0.04f)));
            if (view.Biggest > 0) dl.AddRectFilled(ba, new Vector2(ba.X + (bb.X - ba.X) * a.Amount / view.Biggest, bb.Y), Palette.U32(Palette.Rgba(255, 255, 255, 0.22f)));
            y += rowH;
        }
        if (abilities.Count > shown)
        {
            long amount = 0;
            double share = 0;
            var hits = 0;
            for (var n = shown; n < abilities.Count; n++)
            {
                amount += abilities[n].Amount;
                share += abilities[n].Share;
                hits += abilities[n].Hits;
            }
            Txt.Line(dl, new Vector2(x, y), $"{abilities.Count - shown} more", Face.SansMedium, 13, Palette.Muted, 30);
            Txt.LineRight(dl, edges[0], y, Format.Thousands(amount), Face.Mono, 12, Palette.Muted, 30);
            Txt.LineRight(dl, edges[1], y, Format.Share(share), Face.Mono, 12, Palette.Muted, 30);
            Txt.LineRight(dl, edges[2], y, hits.ToString(System.Globalization.CultureInfo.InvariantCulture), Face.Mono, 12, Palette.Muted, 30);
        }
        if (abilities.Count == 0) Txt.Line(dl, new Vector2(x, y), "Nothing yet.", Face.Sans, 13, Palette.Muted, 30);
    }

    private static readonly string[] DamageHeads = { "Damage", "%", "Hits", "Crit", "DH", "Max" };
    private static readonly string[] HealingHeads = { "Healing", "%", "Hits", "Crit", "Max" };
    private static readonly string[] TakenHeads = { "Taken", "%", "Hits", "Crit", "Max" };
    private static readonly float[] DamageWidths = { 64, 36, 30, 36, 36, 56 };
    private static readonly float[] OtherWidths = { 64, 36, 30, 36, 56 };

    private (IReadOnlyList<AbilityRow>? Of, MeterTab Tab, long Amount, string[][] Cells, long Biggest, string Sub, string Total) abilityView =
        (null, MeterTab.Damage, 0, Array.Empty<string[]>(), 0, string.Empty, string.Empty);

    /// <summary>
    /// The picked player's abilities in words (each line's cells, how many and how big, the total):
    /// worked out again only when the source gives a new list (it keeps one, a few times a second
    /// while a fight is on) or the tab changes, not each frame.
    /// </summary>
    private (string[][] Cells, long Biggest, string Sub, string Total) AbilitiesOf(IReadOnlyList<AbilityRow> abilities, long total)
    {
        if (!ReferenceEquals(abilityView.Of, abilities) || abilityView.Tab != tab || abilityView.Amount != total)
        {
            var cells = new string[abilities.Count][];
            long biggest = 0;
            var hits = 0;
            for (var i = 0; i < abilities.Count; i++)
            {
                var a = abilities[i];
                biggest = Math.Max(biggest, a.Amount);
                hits += a.Hits;
                var count = a.Hits.ToString(System.Globalization.CultureInfo.InvariantCulture);
                cells[i] = tab == MeterTab.Damage
                    ? new[] { Format.Thousands(a.Amount), Format.Share(a.Share), count, Rate(a.CritRate), Rate(a.DirectHitRate), Format.Thousands(a.Max) }
                    : new[] { Format.Thousands(a.Amount), Format.Share(a.Share), count, Rate(a.CritRate), Format.Thousands(a.Max) };
            }
            var by = tab == MeterTab.Taken ? "By what hit them" : "By ability";
            var sub = $"{by} · {CountOf(abilities.Count, "ability", "abilities")}, {CountOf(hits, "hit", "hits")}";
            abilityView = (abilities, tab, total, cells, biggest, sub, Format.Thousands(total));
        }
        return (abilityView.Cells, abilityView.Biggest, abilityView.Sub, abilityView.Total);
    }

    private static string Rate(double? r) => r is { } v ? Format.Share(v) : "–";

    private static string CountOf(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{Format.Thousands(n)} {many}";

    // ---------------------------------------------------------------------------------------------
    // The events.

    /// <summary>
    /// The fight's events (`.ab`'s slab), oldest first, scrolling: a 34px row each, the time in mono, the
    /// kind's mark in its colour, the member's spot, what happened, its kind in faint mono capitals on
    /// the right; a death's row in its red. Pressed, a death opens its recap (Deaths) and a member's
    /// event picks them in the table. Only the rows in view are drawn.
    /// </summary>
    private void DrawEventLog(ImDrawListPtr dl, MeterEncounter e, IReadOnlyList<MeterEvent> events, Vector2 at, float w, float h)
    {
        var r = S(Palette.RadiusLg);
        dl.AddRectFilled(at, at + new Vector2(w, h), Palette.U32(Palette.Slab), r);
        var x = at.X + S(18);
        var y = at.Y + S(12);
        Txt.Line(dl, new Vector2(x, y), "Events", Face.SansSemibold, 15, Palette.White, 15 * 1.25f);
        var sub = events.Count == 0 ? "None yet: deaths, raises, interrupts, potions, limit breaks and tank swaps show here" : MeterText.Counts(events, 4);
        Txt.Line(dl, new Vector2(x, y + S(15 * 1.25f)), Txt.Fit(sub, Face.Sans, 13, w - S(36)), Face.Sans, 13, Palette.Muted, 19.5f);
        var listTop = at.Y + S(12) + S(38) + S(10);
        Kit.Rule(dl, at.X + S(8), at.X + w - S(8), listTop - 1);
        var listH = at.Y + h - S(4) - listTop;
        if (listH > S(20) && events.Count > 0)
        {
            ImGui.SetCursorScreenPos(new Vector2(at.X + 1, listTop));
            var open = ImGui.BeginChild("##detail-events", new Vector2(w - 2, listH), false, ImGuiWindowFlags.None);
            Frame.Open.Push(false);
            try
            {
                if (open) EventRows(e, events, w - 2, listH);
            }
            finally
            {
                if (Frame.Open.Count > 0) Frame.Open.Pop();
                ImGui.EndChild();
            }
        }
        dl.AddRect(at + new Vector2(0.5f), at + new Vector2(w, h) - new Vector2(0.5f), Palette.U32(Palette.Line), r, ImDrawFlags.None, 1f);
    }

    /// <summary>The event log's row ids, made once each.</summary>
    private readonly List<string> eventIds = new();

    /// <summary>The event log's times in words, made again only for a new list of events.</summary>
    private (IReadOnlyList<MeterEvent>? Of, int Count, string[] Times) eventTimes = (null, 0, Array.Empty<string>());

    private void EventRows(MeterEncounter e, IReadOnlyList<MeterEvent> events, float w, float viewH)
    {
        if (!ReferenceEquals(eventTimes.Of, events) || eventTimes.Count != events.Count)
        {
            var times = new string[events.Count];
            for (var i = 0; i < times.Length; i++) times[i] = Format.Clock(events[i].At);
            eventTimes = (events, events.Count, times);
        }
        while (eventIds.Count < events.Count) eventIds.Add("##dev" + eventIds.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var dl = ImGui.GetWindowDrawList();
        var rowH = S(34);
        var start = ImGui.GetCursorScreenPos() + new Vector2(S(6), S(2));
        var rw = ImGui.GetContentRegionAvail().X - S(12);
        var scroll = ImGui.GetScrollY();
        var first = Math.Max(0, (int)(scroll / rowH) - 1);
        var last = Math.Min(events.Count, first + (int)(viewH / rowH) + 3);
        for (var i = first; i < last; i++)
        {
            var ev = events[i];
            var min = start + new Vector2(0, rowH * i);
            var max = min + new Vector2(rw, rowH);
            var picked = ev.PartyId != 0 && ev.PartyId == player;
            var (clicked, hovered, _) = Kit.Hit(eventIds[i], min, max - min);
            if (picked || hovered) dl.AddRectFilled(min, max, Palette.U32(picked ? Palette.Rgba(255, 255, 255, 0.05f) : Palette.Hover), S(Palette.RadiusSm));
            if (clicked)
            {
                if (ev.PartyId > 0) player = ev.PartyId;
                if (ev.Kind == MeterEventKind.Death)
                    for (var d = 0; d < e.Deaths.Count; d++)
                        if (e.Deaths[d].PartyId == ev.PartyId && Math.Abs(e.Deaths[d].At - ev.At) < 1)
                        {
                            death = d;
                            tab = MeterTab.Deaths;
                        }
            }
            var x = min.X + S(10);
            Txt.LineRight(dl, x + S(40), min.Y, eventTimes.Times[i], Face.Mono, 12, Palette.Muted, 34);
            x += S(40) + S(12);
            Icons.Draw(dl, MeterWindow.EventIcon(ev.Kind), new Vector2(x, min.Y + (rowH - S(15)) / 2), S(15), MeterWindow.ToneColor(MeterText.Tone(ev.Kind)));
            x += S(15) + S(10);
            if (ev.Slot.Length > 0)
            {
                Kit.Spot(dl, new Vector2(x + S(9), min.Y + rowH / 2), 18, ev.Slot, Palette.SpotColor(ev.Slot));
                x += S(18) + S(8);
            }
            var kind = Txt.Caps(MeterText.KindWord(ev.Kind));
            var kw = Txt.SpacedWidth(kind, Face.Mono, 9, 0.08f);
            Txt.Spaced(dl, new Vector2(max.X - S(10) - kw, min.Y + (rowH - Txt.Natural(Face.Mono, 9)) / 2), kind, Face.Mono, 9, Palette.Faint, 0.08f);
            var room = max.X - S(10) - kw - S(12) - x;
            if (room > S(20))
                Txt.Line(dl, new Vector2(x, min.Y), Txt.Fit(ev.Text, Face.Sans, 13, room), Face.Sans, 13, ev.Kind == MeterEventKind.Death ? Palette.DangerName : Palette.Text, 34);
            if (hovered && Txt.Width(ev.Text, Face.Sans, 13) > room) Kit.Tooltip(ev.Text);
        }
        ImGui.SetCursorScreenPos(start);
        ImGui.Dummy(new Vector2(rw, rowH * events.Count + S(4)));
    }

    // ---------------------------------------------------------------------------------------------
    // Deaths and the recap.

    /// <summary>Where a death is in the fight's own list (the index the small meter opens a recap by).</summary>
    private static int IndexIn(IReadOnlyList<DeathEntry> all, DeathEntry d)
    {
        for (var i = 0; i < all.Count; i++)
            if (ReferenceEquals(all[i], d))
                return i;
        return -1;
    }

    private void DrawDeaths(ImDrawListPtr dl, MeterEncounter e, Vector2 at, float w, float h, Vector2 recapAt, float recapW)
    {
        dl.AddRectFilled(at, at + new Vector2(w, h), Palette.U32(Palette.Slab), S(Palette.RadiusLg));
        dl.AddRect(at + new Vector2(0.5f), at + new Vector2(w, h) - new Vector2(0.5f), Palette.U32(Palette.Line), S(Palette.RadiusLg), ImDrawFlags.None, 1f);
        var all = e.Deaths.OrderBy(d => d.At).ToList();
        if (death == null || death >= e.Deaths.Count) death = all.Count > 0 ? IndexIn(e.Deaths, all.FirstOrDefault(d => !d.PartOfWipe) ?? all[0]) : null;
        var y = at.Y + S(6);
        var rowH = S(46);
        foreach (var d in all)
        {
            if (y + rowH > at.Y + h - S(6)) break;
            var index = IndexIn(e.Deaths, d);
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
            var sub = d.PartOfWipe ? $"The wipe · {d.KilledBy}" : MeterText.DeathLine(d);
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
    /// what made it worse), its amount (a hit red, a heal green), the HP before it as a bar against their
    /// max HP (<see cref="MeterView.RecapMax(DeathEntry)"/>); the killing blow on a red wash (<see cref="MeterView.KillingLine"/>).
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
        var headH = th + S(4) + S(19.5f);
        // Since 0.4.0: Weakness on as they died, and the raise (when, by whom, how long they were down).
        if (MeterText.AfterDeath(d) is { } after)
        {
            Txt.Line(dl, new Vector2(tx, y + headH), Txt.Fit(after, Face.SansMedium, 13, inner - S(38)), Face.SansMedium, 13,
                d.RaisedAt != null ? Palette.Ready : Palette.Muted, 19.5f);
            headH += S(19.5f);
        }
        y += MathF.Max(S(38), headH) + S(10);

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
        var max = MeterView.RecapMax(d);
        var killing = MeterView.KillingLine(d);
        var rowH = S(40);
        for (var i = 0; i < d.Recap.Count; i++)
        {
            var l = d.Recap[i];
            if (y + rowH > at.Y + h - S(30)) break;
            var kill = i == killing;
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
            if (here.Count > 0) Kit.MenuEyebrow($"{(DateTime.Now.Hour >= 17 ? "Tonight" : "Today")} · {here[0].Fight.Duty}", w);
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
