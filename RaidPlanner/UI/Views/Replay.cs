using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using RaidPlanner.Core;
using RaidPlanner.UI.Model;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>
/// A pull's Replay (0.9.0; the site's Review › Replay, its design of 23 November 2026): the pull played back on its arena
/// (<see cref="ReplayBoard"/>), the moment down the right (the step it falls in, a card for each boss with its HP, its own
/// buffs and what it's casting, Other casts, the party as tiles with their HP, barrier and the fight's debuffs, and "Just
/// before", folded at first), and the transport along the bottom: the step before, Play, the step after, and the pull in
/// one frame of lanes (the phases with the moment's ember tag, the steps, the bosses' buffs, the party's debuffs, the
/// ruler with the deaths, the play head through them all), as tall as the lanes it has, its grow button filling the
/// window with a lane for each member. Pressing or dragging along the lanes moves the moment; nothing plays by itself,
/// and there's no speed (the site took its out). No key does anything (the plugin reads no keys). Worked out once a pull
/// on a worker (<see cref="ReplayModel"/>); a moment is worked out into kept lists (<see cref="ReplayScene"/>).
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The open pull's replay: its file, and what it shows once worked out (or why not).</summary>
    private sealed record ReplayState(string File, ReplayModel? Model, ReplayScene? Scene, string? Problem, IReadOnlyList<ResRow>? Res = null);

    private volatile ReplayState? replayState;

    /// <summary>Replay is picked on a pull's page (Summary · Replay).</summary>
    private bool pullReplay;

    private double replayT;
    private bool replayPlaying;
    private double replayLastFrame;
    private string? replayFocus;
    private bool replayExpanded;
    private bool replayRecentOpen;

    /// <summary>When the pull was last played to its end (ImGui's clock; −1 none): its end card shows for a moment.</summary>
    private double replayEndedAt = -1;

    /// <summary>When each death's skull rang and each member's tile flashed (ImGui's clock; −1 none), as they're played past.</summary>
    private double[] replayRingAt = Array.Empty<double>();

    private double[] replayFlashAt = Array.Empty<double>();

    /// <summary>The end card's hold, its fade, and a death's ring and flash (s).</summary>
    private const double EndHold = 2.6, EndFade = 0.3, RingFor = 0.48, FlashFor = 0.6;

    private void LoadReplay(RecordingInfo info)
    {
        if (replayState?.File == info.File) return;
        replayState = new ReplayState(info.File, null, null, null);
        replayT = 0;
        replayPlaying = false;
        replayFocus = null;
        replayExpanded = false;
        replayRecentOpen = false;
        replayEndedAt = -1;
        dockLanes = null;
        var file = info.File;
        // A plan of yours for this duty gives the real arena (its polygons, its centre); else it's worked out from the pull.
        var planArena = plans.Plan is { Game: { } g } plan && string.Equals(g.Duty, info.Duty, StringComparison.OrdinalIgnoreCase) && plan.Arena.Center != null
            ? plan.Arena
            : null;
        _ = Task.Run(() =>
        {
            try
            {
                var r = store.Read(info);
                var model = ReplayModel.Build(r, planArena);
                var scene = new ReplayScene(model.Track, model.Frame);
                scene.At(0);
                if (replayState?.File == file) replayState = new ReplayState(file, model, scene, null, r.Combat?.Res.ToArray());
            }
            catch (Exception ex)
            {
                Services.Log.Warning(ex, "Raid Codex couldn't work out a pull's replay");
                if (replayState?.File == file) replayState = new ReplayState(file, null, null, "Its replay couldn't be worked out. Its summary is still here.");
            }
        });
    }

    /// <summary>Replay under the pull's head: the board and the moment over the transport, filling what's left of the window.</summary>
    private void DrawReplay(RecordingInfo info)
    {
        LoadReplay(info);
        var state = replayState;
        if (state?.Model is not { } m || state.Scene is not { } sc)
        {
            Kit.Gap(14);
            if (state?.Problem is { } problem) EmptyBlock(BossMood.Awake, "This replay won't open", problem);
            else EmptyBlock(null, "Setting up the replay", null);
            return;
        }
        replayRes = state.Res;
        if (replayRingAt.Length != m.Deaths.Count) replayRingAt = new double[m.Deaths.Count];
        if (replayFlashAt.Length != m.Track.Entities.Count) replayFlashAt = new double[m.Track.Entities.Count];
        Clock(m);
        if (sc.T != replayT || replayPlaying) sc.At(replayT, ReducedMotion);

        Kit.Gap(14);
        var at = Kit.At;
        var w = Kit.Width;
        var lanes = Lanes(m, LaneWidth(w));
        var dockPx = S(ReplayLanes.DockPx(lanes.BossRows, lanes.PartyRows));
        // At least room for the smallest board over the dock: a short window scrolls rather than drawing the board over the title.
        var room = MathF.Max(MathF.Max(S(420), dockPx + S(14) + S(200)), Frame.RoomLeft());
        var dockH = replayExpanded ? room : dockPx;
        var mainH = room - dockH - S(14);
        if (!replayExpanded)
        {
            var panelW = S(332);
            var boardRoom = w - panelW - S(24);
            var size = MathF.Max(S(120), MathF.Min(boardRoom, mainH));
            var boardMin = new Vector2(at.X + (boardRoom - size) / 2, at.Y + (mainH - size) / 2);
            ReplayBoard.BoardSize = size;
            ReplayBoard.Draw(Kit.Dl, boardMin, size, m, sc, replayFocus);
            if (!m.HasPlaces)
                Txt.Line(Kit.Dl, new Vector2(at.X, at.Y), "The party's places weren't recorded: only the enemies play (Settings, Record where the party stands).",
                    Face.Sans, 12, Palette.Faint, 18);
            DrawEndCard(m, new Vector2(boardMin.X + size / 2, boardMin.Y + size - S(28)));
            DrawNow(info, m, sc, new Vector2(at.X + w - panelW, at.Y), panelW, mainH);
        }
        DrawDock(m, sc, lanes, new Vector2(at.X, at.Y + room - dockH), w, dockH);
        Kit.Reserve(at, w, room);
    }

    /// <summary>The clock: playing, it moves on by the frame's time (at most a quarter second), stopping at the end; a death played past rings and flashes.</summary>
    private void Clock(ReplayModel m)
    {
        var now = ImGui.GetTime();
        var dt = Math.Clamp(now - replayLastFrame, 0, 0.25);
        replayLastFrame = now;
        if (!replayPlaying) return;
        var end = Math.Max(0.1, m.Track.End);
        var from = replayT;
        var to = Math.Min(end, from + dt);
        for (var i = 0; i < m.Deaths.Count; i++)
            if (m.Deaths[i].T > from && m.Deaths[i].T <= to)
            {
                replayRingAt[i] = now;
                foreach (var d in m.Track.Deaths)
                    if (Math.Abs(d.T - m.Deaths[i].T) < 0.001 && m.Track.Index.TryGetValue(d.Who, out var k))
                        replayFlashAt[k] = now;
            }
        replayT = to;
        if (to >= end)
        {
            replayPlaying = false;
            replayEndedAt = now;
        }
    }

    private void ReplayToggle(ReplayModel m)
    {
        if (replayPlaying)
        {
            replayPlaying = false;
            return;
        }
        // At the end, Play starts again from the top.
        if (replayT >= m.Track.End - 0.05) replayT = 0;
        replayPlaying = true;
        replayEndedAt = -1;
        replayLastFrame = ImGui.GetTime();
    }

    private void ReplaySeek(ReplayModel m, double t)
    {
        replayT = Math.Clamp(t, 0, Math.Max(0, m.Track.End));
        replayLastFrame = ImGui.GetTime();
    }

    /// <summary>
    /// The pull's end (the site's ReplayEnd.vue): played to its end, how it ended over the board's foot for a moment, the
    /// duty's result in miniature: "Wiped 6:31 Pull 12 · at Knockback Wave · the boss at 38%". Fades in and out; gone as it
    /// plays again.
    /// </summary>
    private void DrawEndCard(ReplayModel m, Vector2 foot)
    {
        if (replayEndedAt < 0 || replayPlaying) return;
        var since = ImGui.GetTime() - replayEndedAt;
        if (since > EndHold) return;
        var old = Palette.Opacity;
        Palette.Opacity = old * (float)Math.Clamp(Math.Min(since / 0.28, (EndHold - since) / EndFade), 0, 1);
        var dl = Kit.Dl;
        var word = Txt.CalloutCaps(m.EndWord);
        var time = ReplayText.Clock(m.FoughtFor);
        var ww = Txt.Width(word, Face.Callout, 17);
        var tw = Txt.Width(time, Face.MonoSemibold, 13);
        var sw = Txt.Width(m.EndWords, Face.Sans, 13);
        var h = S(40);
        var width = S(16) + ww + S(10) + tw + S(12) + sw + S(16);
        var min = new Vector2(foot.X - width / 2, foot.Y - h - (ReducedMotion ? 0 : S(6) * (float)Math.Max(0, 1 - since / 0.28)));
        var wipe = m.EndWord == "Wiped";
        dl.AddRectFilled(min, min + new Vector2(width, h), Palette.U32(Palette.WithAlpha(Palette.Raised, 0.96f)), h / 2);
        dl.AddRect(min, min + new Vector2(width, h), Palette.U32(wipe ? Palette.DangerEdge : m.EndWord == "Clear" ? Palette.ReadyLine : Palette.Line2), h / 2, ImDrawFlags.None, 1f);
        var x = min.X + S(16);
        x += Txt.Line(dl, new Vector2(x, min.Y), word, Face.Callout, 17, wipe ? Palette.Danger : m.EndWord == "Clear" ? Palette.Ready : Palette.White, 40) + S(10);
        x += Txt.Line(dl, new Vector2(x, min.Y), time, Face.MonoSemibold, 13, Palette.White, 40) + S(12);
        Txt.Line(dl, new Vector2(x, min.Y), m.EndWords, Face.Sans, 13, Palette.Text, 40);
        Palette.Opacity = old;
    }

    // ---------------------------------------------------------------------------------------------
    // The moment, down the right (ReplayNow.vue).

    private static readonly string[] Percents = MakePercents();

    private static string[] MakePercents()
    {
        var p = new string[101];
        for (var i = 0; i <= 100; i++) p[i] = i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";
        return p;
    }

    private static string Pct(float share) => share < 0 ? "–" : Percents[(int)MathF.Round(Math.Clamp(share, 0, 1) * 100, MidpointRounding.AwayFromZero)];

    /// <summary>A member's name as the panel says it (their character's, else their spot).</summary>
    private static string NameOf(ReplayModel m, int id) => m.Track.Entity(id) is { Kind: EntityKind.Party } e ? e.Name : m.Track.Entity(id)?.Name ?? "someone";

    /// <summary>
    /// The moment's panel: the step in the callout's voice (one line, smaller type when long, then "…") and "Pull 12 at
    /// 2:51"; a card for each boss and Other casts; the party as tiles, two a row; "Just before", folded at first. Nothing
    /// changes size as it plays. It scrolls when the window is short.
    /// </summary>
    private void DrawNow(RecordingInfo info, ReplayModel m, ReplayScene sc, Vector2 pos, float width, float height)
    {
        ImGui.SetCursorScreenPos(pos);
        var open = ImGui.BeginChild("##replay-now", new Vector2(width, height), false, ImGuiWindowFlags.NoBackground);
        Frame.Open.Push(false);
        try
        {
            if (!open) return;
            var dl = ImGui.GetWindowDrawList();
            var top = ImGui.GetCursorScreenPos();
            var x0 = top.X;
            var w = width - (ImGui.GetScrollMaxY() > 0 ? S(8) : 0);
            var y = top.Y;
            var t = replayT;

            // The step's words, on one line whatever their length.
            var (title, caps, prefix) = StepWords(m, ReplayLanes.StepAt(m.Steps, t));
            var px = title.Length > 23 ? 13 : title.Length > 19 ? 16 : title.Length > 15 ? 17 : 22;
            var fitted = Txt.Fit(caps, Face.Callout, px, w);
            Txt.Line(dl, new Vector2(x0, y), fitted, Face.Callout, px, Palette.White, 26);
            if (!ReferenceEquals(fitted, caps) && Kit.Pointed(new Vector2(x0, y), new Vector2(x0 + w, y + S(26)))) Kit.Tooltip(title);
            y += S(26) + S(2);
            var sx = x0;
            if (prefix.Length > 0) sx += Txt.Line(dl, new Vector2(sx, y), prefix, Face.Sans, 13, Palette.Muted, 19);
            sx += Txt.Line(dl, new Vector2(sx, y), PullAtWords(info), Face.Sans, 13, Palette.Muted, 19);
            Txt.Line(dl, new Vector2(sx, y), ReplayText.Clock(t), Face.Mono, 13, Palette.Muted, 19);
            y += S(19) + S(12);

            // The bosses: a card each, its size fixed.
            var one = m.Cards.Count == 1;
            for (var ci = 0; ci < m.Cards.Count; ci++)
            {
                y += Card(dl, m, sc, m.Cards[ci], one, new Vector2(x0, y), w) + S(6.4f);
            }
            if (m.OtherCasts) y += Card(dl, m, sc, null, false, new Vector2(x0, y), w) + S(6.4f);
            if (!m.HasHp)
            {
                y += S(4);
                Txt.Line(dl, new Vector2(x0, y), "HP is in recordings from plugin 0.3 on.", Face.Sans, 12, Palette.Faint, 18);
                y += S(18);
            }
            y += S(8);

            // The party: tiles, two a row.
            Kit.Label(dl, new Vector2(x0, y + S(2)), "The party", 11, 0.12f, Palette.Faint);
            Txt.LineRight(dl, x0 + w, y, "debuffs · seconds left", Face.Mono, 11, Palette.Faint, 16);
            y += S(16) + S(6);
            var tileW = (w - S(6)) / 2;
            var tileH = S(73.6f);
            for (var j = 0; j < m.PartyOrder.Count; j++)
            {
                var tp = new Vector2(x0 + (j % 2) * (tileW + S(6)), y + (j / 2) * (tileH + S(6)));
                Tile(dl, m, sc, m.PartyOrder[j], tp, tileW, tileH);
            }
            y += (m.PartyOrder.Count + 1) / 2 * (tileH + S(6)) + S(8);

            // Just before: folded at first.
            y = Recent(dl, m, new Vector2(x0, y), w);
            ImGui.SetCursorScreenPos(top);
            ImGui.Dummy(new Vector2(1, y - top.Y + S(4)));
        }
        finally
        {
            if (Frame.Open.Count > 0) Frame.Open.Pop();
            ImGui.EndChild();
        }
    }

    private (ReplayModel? Model, int Step, string Title, string Caps, string Prefix) stepWords;

    /// <summary>A step's words for the panel, made once a step: its name (without "P2 · "), in the callout's capitals, and its phase ("P2 · ").</summary>
    private (string Title, string Caps, string Prefix) StepWords(ReplayModel m, int step)
    {
        if (!ReferenceEquals(stepWords.Model, m) || stepWords.Step != step)
        {
            var name = step < m.Steps.Count ? m.Steps[step].Name : "Pull";
            var title = PlanJson.WithoutPhase(name);
            var dot = name.IndexOf(" · ", StringComparison.Ordinal);
            var prefix = name.StartsWith('P') && dot > 1 ? name[..(dot + 3)] : string.Empty;
            stepWords = (m, step, title, Txt.CalloutCaps(title), prefix);
        }
        return (stepWords.Title, stepWords.Caps, stepWords.Prefix);
    }

    private string? pullAtFor;
    private string pullAtWords = string.Empty;

    /// <summary>"Pull 12 at ", made once a pull.</summary>
    private string PullAtWords(RecordingInfo info)
    {
        if (pullAtFor != info.File)
        {
            pullAtFor = info.File;
            pullAtWords = $"Pull {Math.Max(1, info.Pull)} at ";
        }
        return pullAtWords;
    }

    /// <summary>
    /// A boss's card (or, <paramref name="card"/> null, Other casts): its name, its own buffs (three, then "+2") and its HP;
    /// its HP bar; what it's casting now, a thin cast bar under its name (the time left; "Went off", "Interrupted" in
    /// green, "Stopped"), on whom and whether it can be interrupted, "+1" for more; "Not casting"; dimmed and "Away" while
    /// none of its actors is there. Returns its height.
    /// </summary>
    private float Card(ImDrawListPtr dl, ReplayModel m, ReplayScene sc, EnemyCard? card, bool one, Vector2 min, float w)
    {
        var h = card != null ? S(73.6f) : S(56);
        var max = min + new Vector2(w, h);
        // Its actor now: the first of its actors that's there.
        var idx = -1;
        if (card != null)
            foreach (var id in card.Ids)
                if (m.Track.Index.TryGetValue(id, out var k) && sc.Points[k].Here)
                {
                    idx = k;
                    break;
                }
        var away = card != null && idx < 0;
        var old = Palette.Opacity;
        if (away) Palette.Opacity = old * 0.55f;
        dl.AddRectFilled(min, max, Palette.U32(Palette.Rgba(255, 255, 255, 0.02f)), S(Palette.RadiusSm));
        dl.AddRect(min + new Vector2(0.5f), max - new Vector2(0.5f), Palette.U32(Palette.Rgba(255, 255, 255, 0.07f)), S(Palette.RadiusSm), ImDrawFlags.None, 1f);
        var x = min.X + S(11.2f);
        var y = min.Y + S(8.8f);
        var iw = w - S(22.4f);
        // Its name, buffs and HP.
        var hpShare = idx >= 0 ? ReplayBoard.HpShare(sc.Points[idx].Hp, m.Track.Entities[idx].MaxHp) : -1;
        var hpText = card == null || away ? string.Empty : Pct(hpShare);
        var hpW = Txt.Width(hpText, Face.Mono, 12);
        var buffs = idx >= 0 ? sc.StatusesOn[idx] : null;
        var buffW = 0f;
        if (buffs is { Count: > 0 })
            buffW = Math.Min(3, buffs.Count) * (S(15) + S(3)) + (buffs.Count > 3 ? Txt.Width(ReplayText.Plus(buffs.Count - 3), Face.Mono, 11) + S(3) : 0);
        var name = card?.Name ?? "Other casts";
        var nameRoom = iw - hpW - buffW - S(16);
        var nw = Txt.Line(dl, new Vector2(x, y), Txt.Fit(name, Face.SansMedium, 13, nameRoom), Face.SansMedium, 13, Palette.White, 20);
        if (buffs is { Count: > 0 })
        {
            var bx = x + nw + S(8);
            for (var i = 0; i < Math.Min(3, buffs.Count); i++)
            {
                var s = buffs[i];
                var bmin = new Vector2(bx, y);
                var bmax = bmin + new Vector2(S(15), S(20));
                ReplayIcons.Status(dl, bmin, bmax, s);
                if (Kit.Pointed(bmin, bmax)) StatusTip(s);
                bx += S(18);
            }
            if (buffs.Count > 3) Txt.Line(dl, new Vector2(bx, y), ReplayText.Plus(buffs.Count - 3), Face.Mono, 11, Palette.Muted, 20);
        }
        if (hpText.Length > 0) Txt.LineRight(dl, x + iw, y, hpText, Face.Mono, 12, Palette.Text, 20);
        y += S(20) + S(6);
        if (card != null)
        {
            dl.AddRectFilled(new Vector2(x, y), new Vector2(x + iw, y + S(3)), Palette.U32(Palette.Rgba(255, 255, 255, 0.06f)), S(2));
            if (hpShare > 0) dl.AddRectFilled(new Vector2(x, y), new Vector2(x + iw * hpShare, y + S(3)), Palette.U32(Palette.White), S(2));
            y += S(3) + S(6);
        }
        // What it's casting.
        var bottom = y + S(26);
        if (away) Txt.Line(dl, new Vector2(x, bottom - S(19)), "Away", Face.Sans, 12, Palette.WithAlpha(Palette.Faint, 0.75f), 19);
        else if (ShownCast(m, card, one) is var (cast, more) && cast != null)
        {
            var done = replayT >= cast.End;
            var timeText = cast.Stopped switch
            {
                CastStop.Interrupted when done => "Interrupted",
                CastStop.Cancelled when done => "Stopped",
                _ => done ? "Went off" : ReplayText.SecondsLeft(cast.End - replayT),
            };
            var timeColor = cast.Stopped == CastStop.Interrupted && done ? Palette.Ready : Palette.EmberHi;
            var tw = Txt.Width(timeText, Face.MonoSemibold, 11);
            var ly = bottom - S(3) - S(5) - S(17);
            var cx = x;
            var nameMax = iw - tw - S(10);
            cx += Txt.Line(dl, new Vector2(cx, ly), Txt.Fit(cast.Name, Face.SansMedium, 12, nameMax), Face.SansMedium, 12, Palette.EmberHi, 17);
            var onSlot = cast.Target != 0 && m.Track.Entity(cast.Target) is { Kind: EntityKind.Party } on ? on.Name : null;
            if (onSlot != null && x + nameMax - cx > S(40))
            {
                cx += Txt.Line(dl, new Vector2(cx, ly), " · on ", Face.Sans, 12, Palette.Faint, 17);
                cx += Txt.Line(dl, new Vector2(cx, ly), Txt.Fit(onSlot, Face.Sans, 12, MathF.Max(0, x + nameMax - cx)), Face.Sans, 12, Palette.Faint, 17);
            }
            if (card != null && cast.Interruptible && x + nameMax - cx > S(60))
                cx += Txt.Line(dl, new Vector2(cx, ly), Txt.Fit(onSlot != null ? ", can be interrupted" : " · can be interrupted", Face.Sans, 12, x + nameMax - cx),
                    Face.Sans, 12, Palette.Faint, 17);
            if (more > 0 && x + nameMax - cx > S(20)) Txt.Line(dl, new Vector2(cx + S(4), ly), ReplayText.Plus(more), Face.Mono, 11, Palette.Faint, 17);
            Txt.LineRight(dl, x + iw, ly, timeText, Face.MonoSemibold, 11, timeColor, 17);
            var by = bottom - S(3);
            var p = (float)Math.Clamp((replayT - cast.T) / Math.Max(0.1, cast.End - cast.T), 0, 1);
            dl.AddRectFilled(new Vector2(x, by), new Vector2(x + iw, by + S(3)), Palette.U32(Palette.Rgba(255, 255, 255, 0.08f)), S(2));
            dl.AddRectFilled(new Vector2(x, by), new Vector2(x + iw * p, by + S(3)), Palette.U32(done ? Palette.Rgba(255, 138, 61, 0.45f) : Palette.Ember), S(2));
        }
        else Txt.Line(dl, new Vector2(x, bottom - S(19)), "Not casting", Face.Sans, 12, Palette.WithAlpha(Palette.Faint, 0.75f), 19);
        Palette.Opacity = old;
        return h;
    }

    /// <summary>
    /// The cast a card shows now: of the casts on now (begun, and not 0.8 s past their end), the card's (every one when
    /// it's the only card; else its own actors', or for Other casts no card's), the latest begun still casting, else the
    /// last to finish; and how many more.
    /// </summary>
    private (ReplayCast? Cast, int More) ShownCast(ReplayModel m, EnemyCard? card, bool one)
    {
        var casts = m.Track.Casts;
        var now = replayT;
        ReplayCast? running = null, any = null;
        var count = 0;
        // From the first that could still be on.
        int lo = 0, hi = casts.Count;
        var from = now - m.Track.LongestCast - 1;
        while (lo < hi)
        {
            var mid = (lo + hi) >> 1;
            if (casts[mid].T < from) lo = mid + 1;
            else hi = mid;
        }
        for (var i = lo; i < casts.Count; i++)
        {
            var c = casts[i];
            if (c.T > now) break;
            if (now >= c.End + 0.8) continue;
            if (!Belongs(m, card, one, c.Src)) continue;
            count++;
            if (now < c.End && (running == null || c.T >= running.T)) running = c;
            if (any == null || c.T >= any.T) any = c;
        }
        return (running ?? any, Math.Max(0, count - 1));
    }

    private static bool Belongs(ReplayModel m, EnemyCard? card, bool one, int src)
    {
        if (card != null)
        {
            if (one) return true;
            foreach (var id in card.Ids)
                if (id == src)
                    return true;
            return false;
        }
        foreach (var c in m.Cards)
            foreach (var id in c.Ids)
                if (id == src)
                    return false;
        return true;
    }

    /// <summary>A status pointed at: its name, its seconds left, and the game's description.</summary>
    private static void StatusTip(in StatusNow s)
    {
        var desc = ReplayIcons.Description(s.Id);
        var left = float.IsNaN(s.Left) ? string.Empty : $", {Math.Ceiling(s.Left)} s left";
        Kit.Tooltip(desc.Length > 0 ? $"{s.Name}{left}\n{desc}" : s.Name + left);
    }

    /// <summary>
    /// A member's tile (two a row, its size fixed): their job icon, name and HP figure; their HP bar with their barrier after
    /// it; the fight's debuffs on them with the seconds each has left under its icon, as many as fit, "+N" in the last slot
    /// for more; dead, a skull and when. A death just played past washes it red. Pressing it views as them (the rest
    /// fade), again shows everyone.
    /// </summary>
    private void Tile(ImDrawListPtr dl, ReplayModel m, ReplayScene sc, int index, Vector2 min, float w, float h)
    {
        var e = m.Track.Entities[index];
        var max = min + new Vector2(w, h);
        var (clicked, hovered, _) = Kit.Hit(IdOf("##replay-tile", index), min, new Vector2(w, h));
        if (clicked) replayFocus = replayFocus == e.Slot ? null : e.Slot;
        var focus = replayFocus == e.Slot;
        var bg = focus ? Palette.Rgba(255, 255, 255, 0.08f) : hovered ? Palette.Rgba(255, 255, 255, 0.06f) : Palette.Rgba(255, 255, 255, 0.03f);
        dl.AddRectFilled(min, max, Palette.U32(bg), S(Palette.RadiusSm));
        var flash = replayFlashAt[index];
        if (flash > 0 && ImGui.GetTime() - flash < FlashFor)
            dl.AddRectFilled(min, max, Palette.U32(Palette.WithAlpha(Palette.Danger, 0.18f * (1 - (float)((ImGui.GetTime() - flash) / FlashFor)))), S(Palette.RadiusSm));
        dl.AddRect(min + new Vector2(0.5f), max - new Vector2(0.5f), Palette.U32(focus ? Palette.Rgba(255, 255, 255, 0.35f) : Palette.Rgba(255, 255, 255, 0.07f)),
            S(Palette.RadiusSm), ImDrawFlags.None, 1f);
        if (hovered) Kit.Tooltip(focus ? "Show everyone" : $"View as {e.Name}");
        var x = min.X + S(8);
        var y = min.Y + S(7.2f);
        var iw = w - S(16);
        var dead = !double.IsNaN(sc.DeadSince[index]);
        ref readonly var p = ref sc.Points[index];
        var hp = dead ? 0 : p.Here ? ReplayBoard.HpShare(p.Hp, e.MaxHp) : -1;
        PlateArt.JobIcon(dl, new Vector2(x, y), 18, e.Job, e.Role, Palette.SpotColor(e.Slot), null, dead, Palette.Muted);
        var hpText = dead ? string.Empty : Pct(hp);
        var hpW = Txt.Width(hpText, Face.Mono, 11);
        Txt.Line(dl, new Vector2(x + S(18) + S(6.4f), y), Txt.Fit(e.Name, Face.SansMedium, 12, iw - S(25) - hpW - S(6)), Face.SansMedium, 12,
            dead ? Palette.Muted : Palette.White, 18);
        if (hpText.Length > 0) Txt.LineRight(dl, x + iw, y, hpText, Face.Mono, 11, Palette.Text, 18);
        y += S(18) + S(5);
        dl.AddRectFilled(new Vector2(x, y), new Vector2(x + iw, y + S(3)), Palette.U32(Palette.Rgba(255, 255, 255, 0.06f)), S(2));
        if (hp > 0)
        {
            var tone = hp < 0.25f ? Palette.Danger : hp < 0.5f ? Palette.Warn : Palette.Ready;
            dl.AddRectFilled(new Vector2(x, y), new Vector2(x + iw * hp, y + S(3)), Palette.U32(tone), S(2));
            // Their barrier (% of their max HP), after their HP.
            var shield = Barrier(m, e.Id);
            if (shield > 0)
            {
                var sx = x + iw * hp;
                var ex = MathF.Min(x + iw, sx + iw * shield);
                if (ex > sx + 0.5f) dl.AddRectFilled(new Vector2(sx, y), new Vector2(ex, y + S(3)), Palette.U32(Palette.WithAlpha(Palette.Warn, 0.8f)), S(2));
            }
        }
        y += S(3) + S(5);
        if (dead)
        {
            Icons.Draw(dl, Icon.Skull, new Vector2(x, y + S(2)), S(13), Palette.Danger);
            var dx = x + S(18) + Txt.Line(dl, new Vector2(x + S(18), y), "Died ", Face.Mono, 11, Palette.Danger, 17);
            Txt.Line(dl, new Vector2(dx, y), ReplayText.Clock(sc.DeadSince[index]), Face.Mono, 11, Palette.Danger, 17);
            return;
        }
        var list = sc.StatusesOn[index];
        if (list.Count == 0) return;
        // As many as fit (19 px each), one fewer and "+N" when there are more.
        var slots = Math.Max(3, (int)MathF.Floor((iw / Scale + 4) / 19));
        var shown = list.Count <= slots ? list.Count : slots - 1;
        var sxp = x;
        for (var i = 0; i < shown; i++)
        {
            var s = list[i];
            var imin = new Vector2(sxp, y);
            var imax = imin + new Vector2(S(15), S(20));
            ReplayIcons.Status(dl, imin, imax, s);
            if (Kit.Pointed(imin, imax)) StatusTip(s);
            if (!float.IsNaN(s.Left))
            {
                var left = ReplayText.Number((int)Math.Ceiling(s.Left));
                var lw = Txt.Width(left, Face.Mono, 9);
                Txt.Line(dl, new Vector2(sxp + (S(15) - lw) / 2, y + S(21)), left, Face.Mono, 9, Palette.Muted, 10);
            }
            sxp += S(19);
        }
        if (list.Count > shown)
        {
            var more = ReplayText.Plus(list.Count - shown);
            Txt.Line(dl, new Vector2(sxp, y), more, Face.Mono, 11, Palette.Muted, 20);
            if (Kit.Pointed(new Vector2(sxp, y), new Vector2(sxp + S(24), y + S(20)))) Kit.Tooltip(MoreWords(list, shown));
        }
    }

    private static string MoreWords(List<StatusNow> list, int from)
    {
        var parts = new List<string>();
        for (var i = from; i < list.Count; i++)
            parts.Add(float.IsNaN(list[i].Left) ? list[i].Name : $"{list[i].Name}, {Math.Ceiling(list[i].Left)} s left");
        return string.Join(", ", parts);
    }

    /// <summary>A member's barrier now as a share of their max HP (the combat block's latest, plugin 0.4), 0 when none or not known.</summary>
    private float Barrier(ReplayModel m, int id)
    {
        if (barrierRows == null || !ReferenceEquals(barrierFor, m))
        {
            barrierFor = m;
            barrierRows = new Dictionary<int, List<(double T, int Pct)>>();
            if (replayRes != null)
                foreach (var r in replayRes)
                {
                    if (!barrierRows.TryGetValue(r.Who, out var list)) barrierRows[r.Who] = list = new List<(double, int)>();
                    list.Add((r.T, r.Shield));
                }
        }
        if (!barrierRows.TryGetValue(id, out var rows) || rows.Count == 0) return 0;
        int lo = 0, hi = rows.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) >> 1;
            if (rows[mid].T <= replayT) lo = mid + 1;
            else hi = mid;
        }
        return lo == 0 ? 0 : Math.Clamp(rows[lo - 1].Pct / 100f, 0, 1);
    }

    private Dictionary<int, List<(double T, int Pct)>>? barrierRows;
    private ReplayModel? barrierFor;

    /// <summary>The pull's barriers (its combat block's <c>res</c>), kept with its replay.</summary>
    private IReadOnlyList<ResRow>? replayRes;

    /// <summary>
    /// "Just before" (folded at first: its heading opens it, the latest thing faint beside it on one line): the last six
    /// things that happened up to the moment, the newest first, each a way back to just before it. Returns where it ends.
    /// </summary>
    private float Recent(ImDrawListPtr dl, ReplayModel m, Vector2 at, float w)
    {
        var log = m.Log;
        int lo = 0, hi = log.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) >> 1;
            if (log[mid].T <= replayT + 0.01) lo = mid + 1;
            else hi = mid;
        }
        var last = lo - 1;
        if (last < 0) return at.Y;
        var headH = S(28);
        var (clicked, hovered, _) = Kit.Hit("##replay-recent", at, new Vector2(w, headH));
        if (clicked) replayRecentOpen = !replayRecentOpen;
        if (hovered) dl.AddRectFilled(at, at + new Vector2(w, headH), Palette.U32(Palette.Hover), S(Palette.RadiusSm));
        Icons.Draw(dl, replayRecentOpen ? Icon.ChevronDown : Icon.ChevronRight, new Vector2(at.X + S(2), at.Y + (headH - S(14)) / 2), S(14), Palette.Faint);
        var lx = at.X + S(22);
        lx += Kit.Label(dl, new Vector2(lx, at.Y + (headH - Kit.EyebrowH) / 2), "Just before", 11, 0.12f, Palette.Faint) + S(8);
        if (!replayRecentOpen)
        {
            var latest = log[last];
            Txt.Line(dl, new Vector2(lx, at.Y), Txt.Fit(latest.Text, Face.Sans, 11, at.X + w - lx - S(6)), Face.Sans, 11, Palette.Faint, 28);
            return at.Y + headH;
        }
        var y = at.Y + headH + S(2);
        for (var i = last; i >= 0 && i > last - 6; i--)
        {
            var line = log[i];
            var rowH = S(26);
            var min = new Vector2(at.X, y);
            var (go, over, _) = Kit.Hit(IdOf("##replay-ev", i), min, new Vector2(w, rowH));
            if (over)
            {
                dl.AddRectFilled(min, min + new Vector2(w, rowH), Palette.U32(Palette.Hover), S(Palette.RadiusSm));
                Kit.Tooltip(line.Title != null ? $"{line.Title}. Go to {ReplayText.Clock(line.T)}" : $"Go to {ReplayText.Clock(line.T)}");
            }
            if (go) ReplaySeek(m, Math.Max(0, line.T - 1.5));
            var time = line.T < 0 ? "−" + ReplayModel.Secs(-line.T) : ReplayText.Clock(line.T);
            Txt.Line(dl, new Vector2(at.X + S(6.4f), y), time, Face.Mono, 12, Palette.Faint, 26);
            var color = line.Tone switch
            {
                LogTone.Bad => Palette.Danger,
                LogTone.Good => Palette.Ready,
                LogTone.Quiet => Palette.Muted,
                _ => Palette.Text,
            };
            var tx = at.X + S(6.4f) + S(49.6f) + S(8);
            Txt.Line(dl, new Vector2(tx, y), Txt.Fit(line.Text, Face.Sans, 13, at.X + w - tx - S(6)), Face.Sans, 13, color, 26);
            y += rowH + S(1);
        }
        return y;
    }

    // ---------------------------------------------------------------------------------------------
    // The transport along the bottom (ReplayDock.vue).

    /// <summary>A status on a lane: its icon and what it says.</summary>
    private sealed record LaneItem(string Key, uint Id, string Name, uint Icon, int Cat, int Stacks, string Count, string Words);

    /// <summary>The dock's lanes packed into rows as drawn, for one lane width (made again when it changes, or the dock grows).</summary>
    private sealed record DockLanes(float Width, bool Expanded, List<LaneBar<LaneItem>> Boss, int BossRows, List<LaneBar<LaneItem>> Party, int PartyRows,
        List<(string Name, List<LaneBar<LaneItem>> Bars, int Rows)> Members);

    private DockLanes? dockLanes;

    /// <summary>The lanes' width (px): the dock's, less its padding, the transport, the gutter and the frame's own padding.</summary>
    private static float LaneWidth(float dockW) => dockW - S(ReplayLanes.SlabPad) * 2 - S(144) - S(16) - S(65.6f) - 2 - S(8) * 2;

    private DockLanes Lanes(ReplayModel m, float width)
    {
        if (dockLanes is { } kept && MathF.Abs(kept.Width - width) < 0.5f && kept.Expanded == replayExpanded) return kept;
        var total = m.Total;
        float X(double t) => (float)(Math.Clamp(t / total, 0, 1) * width);
        var manyBosses = false;
        for (var i = 1; i < m.BossBuffs.Count; i++)
            if (m.BossBuffs[i].On != m.BossBuffs[0].On) manyBosses = true;
        var boss = ReplayLanes.Pack(Bars(m.BossBuffs, b => new LaneItem(b.Key, b.Id, b.Name, b.Icon, b.Cat, b.Stacks, string.Empty,
                $"{b.Name}{(manyBosses ? $" on {b.Who}" : string.Empty)}, {Format.Clock(b.Start)}–{Format.Clock(b.End)}")),
            replayExpanded ? int.MaxValue : ReplayLanes.BossRows, S(ReplayLanes.IconStep));
        var party = ReplayLanes.Pack(PartyBars(m), ReplayLanes.PartyRows, S(ReplayLanes.IconStep));
        var members = new List<(string, List<LaneBar<LaneItem>>, int)>();
        if (replayExpanded)
            foreach (var mm in m.Members)
            {
                var packed = ReplayLanes.Pack(Bars(mm.Spans, b => new LaneItem(b.Key, b.Id, b.Name, b.Icon, 2, b.Stacks, string.Empty,
                    $"{b.Name} on {mm.Name}, {Format.Clock(b.Start)}–{Format.Clock(b.End)}")), int.MaxValue, S(ReplayLanes.IconStep));
                members.Add((mm.Name, packed.Bars, packed.Rows));
            }
        return dockLanes = new DockLanes(width, replayExpanded, boss.Bars, boss.Rows, party.Bars, party.Rows, members);

        IEnumerable<(float, float, string, LaneItem)> Bars(IEnumerable<BuffSpan> spans, Func<BuffSpan, LaneItem> item)
        {
            foreach (var b in spans)
            {
                var from = X(b.Start);
                yield return (from, MathF.Max(from, X(b.End)), b.Key, item(b));
            }
        }

        IEnumerable<(float, float, string, LaneItem)> PartyBars(ReplayModel model)
        {
            foreach (var d in model.PartyWaves)
            {
                var from = X(d.Start);
                var whose = string.Join("; ", System.Linq.Enumerable.Select(d.Parts, p => $"{p.Name} on {string.Join(", ", p.Who)}"));
                yield return (from, MathF.Max(from, X(d.End)), d.Key,
                    new LaneItem(d.Key, d.Id, d.Name, d.Icon, 2, d.Stacks, d.Who.Count > 1 ? $"×{d.Who.Count}" : string.Empty,
                        $"{whose}, {Format.Clock(d.Start)}–{Format.Clock(d.End)}"));
            }
        }
    }

    /// <summary>
    /// The transport in its slab: the step before, Play (round, the telegraph), the step after; then the frame of lanes:
    /// its gutter (the grow button, the lanes' names), the phases with the moment's ember tag, the steps, the bosses'
    /// buffs, the party's debuffs (or, filling the window, a lane for each member), the ruler with the deaths, the play
    /// head. Pressing or dragging along the lanes moves the moment.
    /// </summary>
    private void DrawDock(ReplayModel m, ReplayScene sc, DockLanes lanes, Vector2 min, float w, float h)
    {
        var dl = Kit.Dl;
        var max = min + new Vector2(w, h);
        dl.AddRectFilled(min, max, Palette.U32(Palette.Slab), S(Palette.RadiusLg));
        dl.AddRect(min + new Vector2(0.5f), max - new Vector2(0.5f), Palette.U32(Palette.Line), S(Palette.RadiusLg), ImDrawFlags.None, 1f);
        var pad = S(ReplayLanes.SlabPad);
        var inner = min + new Vector2(pad);
        var innerW = w - pad * 2;
        var innerH = h - pad * 2;

        // The step before, Play, the step after.
        var playD = S(56);
        var by = replayExpanded ? inner.Y + innerH - S(H) - (playD - S(H)) / 2 : inner.Y + (innerH - S(H)) / 2;
        var step = ReplayLanes.StepAt(m.Steps, replayT);
        var bx = inner.X;
        if (Kit.IconButton("##replay-prev", new Vector2(bx, by), Icon.SkipBack, "The step before", Btn.Quiet, BtnSize.Md, replayT <= 0.05))
        {
            // Back to the start of the step you're in first, unless that's where it is.
            var target = m.Steps[step].Start;
            if (replayT - target <= 0.25 && step > 0) target = m.Steps[step - 1].Start;
            ReplaySeek(m, target);
        }
        bx += S(H) + S(4);
        var pc = new Vector2(bx + playD / 2, by + S(H) / 2);
        var (play, playOver, playHeld) = Kit.Hit("##replay-play", pc - new Vector2(playD / 2), new Vector2(playD));
        if (play) ReplayToggle(m);
        var fill = playHeld ? Palette.EmberDeep : playOver ? Palette.TeleHover : Palette.Ember;
        if (replayPlaying) dl.AddCircleFilled(pc, playD / 2 + S(4), Palette.U32(Palette.WithAlpha(Palette.Ember, 0.18f)), 40);
        dl.AddCircleFilled(pc, playD / 2, Palette.U32(fill), 40);
        dl.AddCircle(pc, playD / 2 - S(0.75f), Palette.U32(replayPlaying ? Palette.Rgba(255, 220, 190, 0.8f) : Palette.TeleRim), 40, S(1.5f));
        var ip = S(replayPlaying ? 20 : 22);
        Icons.Draw(dl, replayPlaying ? Icon.Pause : Icon.Play, pc - new Vector2(ip / 2 - (replayPlaying ? 0 : S(2)), ip / 2), ip, Palette.OnEmber, replayPlaying ? 2.4f : 1.75f);
        if (playOver) Kit.Tooltip(replayPlaying ? "Pause" : "Play the pull");
        bx += playD + S(4);
        if (Kit.IconButton("##replay-next", new Vector2(bx, by), Icon.SkipForward, "The step after", Btn.Quiet, BtnSize.Md, step >= m.Steps.Count - 1))
            ReplaySeek(m, Math.Min(m.Track.End, m.Steps[Math.Min(m.Steps.Count - 1, step + 1)].Start));
        var frameMin = new Vector2(inner.X + S(144) + S(16), inner.Y);
        var frameMax = new Vector2(inner.X + innerW, inner.Y + innerH);

        // The frame, and its gutter.
        dl.AddRectFilled(frameMin, frameMax, Palette.U32(Palette.Well), S(10));
        dl.AddRect(frameMin + new Vector2(0.5f), frameMax - new Vector2(0.5f), Palette.U32(Palette.Line2), S(10), ImDrawFlags.None, 1f);
        var gutterR = frameMin.X + 1 + S(65.6f);
        dl.AddLine(new Vector2(gutterR, frameMin.Y + 1), new Vector2(gutterR, frameMax.Y - 1), Palette.U32(Palette.Rgba(255, 255, 255, 0.06f)), 1f);
        var gx = frameMin.X + 1 + S(12);
        var top = frameMin.Y + 1 + S(8);
        // Fill the window with the lanes, and back: a small button where the phases' row starts.
        var growAt = new Vector2(gx - S(4), top);
        var (grow, growOver, _) = Kit.Hit("##replay-grow", growAt, new Vector2(S(20)));
        if (growOver)
        {
            dl.AddRectFilled(growAt, growAt + new Vector2(S(20)), Palette.U32(Palette.HoverButton), S(5));
            dl.AddRect(growAt, growAt + new Vector2(S(20)), Palette.U32(Palette.Line2), S(5), ImDrawFlags.None, 1f);
            Kit.Tooltip(replayExpanded ? "Shrink the timeline" : "Fill the window with the timeline");
        }
        Icons.Draw(dl, replayExpanded ? Icon.Minimize : Icon.Maximize, growAt + new Vector2(S(3.5f)), S(13), growOver ? Palette.White : Palette.Faint);
        if (grow) replayExpanded = !replayExpanded;

        var lx = gutterR + S(8);
        var lw = frameMax.X - 1 - S(8) - lx;
        var total = m.Total;
        float X(double t) => lx + (float)(Math.Clamp(t / total, 0, 1) * lw);
        var stepsTop = top + S(ReplayLanes.Phases) + S(ReplayLanes.Gap);
        var stepsBottom = stepsTop + S(ReplayLanes.Steps);
        var rulerBottom = frameMax.Y - 1 - S(8);
        var rulerTop = rulerBottom - S(ReplayLanes.Ruler);
        GutterLabel(dl, gx, stepsTop, S(ReplayLanes.Steps), "Steps");

        // The seek: pressing or dragging along the lanes.
        var (_, lineOver, held) = Kit.Hit("##replay-line", new Vector2(lx - S(8), top), new Vector2(lw + S(16), rulerBottom - top));
        if (held)
        {
            var f = Math.Clamp((ImGui.GetMousePos().X - lx) / MathF.Max(1, lw), 0, 1);
            ReplaySeek(m, Math.Min(m.Track.End, f * total));
        }
        if (lineOver || held) ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);

        // The phases.
        for (var i = 0; i < m.Phases.Count; i++)
        {
            var p0 = X(m.Phases[i].Start);
            var p1 = i + 1 < m.Phases.Count ? X(m.Phases[i + 1].Start) : lx + lw;
            dl.AddLine(new Vector2(p0 + 0.5f, top), new Vector2(p0 + 0.5f, top + S(20)), Palette.U32(Palette.Rgba(255, 255, 255, 0.2f)), 1f);
            Txt.Line(dl, new Vector2(p0 + S(6), top), Txt.Fit(m.Phases[i].Name, Face.Mono, 10, MathF.Max(0, p1 - p0 - S(8))), Face.Mono, 10, Palette.Faint, 20);
        }

        // The steps: tall blocks, the one the moment's in filling like a cast bar.
        var headX = X(replayT);
        for (var i = 0; i < m.Steps.Count; i++)
        {
            var s = m.Steps[i];
            var s0 = X(s.Start);
            var s1 = X(s.End);
            if (s1 - s0 < 1) continue;
            var bmin = new Vector2(s0, stepsTop);
            var bmax = new Vector2(MathF.Max(s0 + 1, s1 - S(2)), stepsBottom);
            var on = i == step;
            var past = i < step;
            var progress = past ? 1 : on ? (float)Math.Clamp((replayT - s.Start) / Math.Max(0.001, s.End - s.Start), 0, 1) : 0;
            var r = S(5);
            dl.AddRectFilled(bmin, bmax, Palette.U32(Palette.Rgba(255, 255, 255, 0.05f)), r);
            if (progress > 0)
                dl.AddRectFilled(bmin, new Vector2(bmin.X + (bmax.X - bmin.X) * progress, bmax.Y), Palette.U32(Palette.Rgba(255, 138, 61, 0.28f)), r,
                    progress >= 1 ? ImDrawFlags.RoundCornersAll : ImDrawFlags.RoundCornersLeft);
            var edge = on ? Palette.Ember : past ? Palette.Rgba(255, 138, 61, 0.25f) : Palette.Rgba(255, 255, 255, 0.1f);
            dl.AddRect(bmin + new Vector2(0.5f), bmax - new Vector2(0.5f), Palette.U32(edge), r, ImDrawFlags.None, 1f);
            var bw = bmax.X - bmin.X;
            if (bw > S(18))
            {
                dl.PushClipRect(bmin, bmax, true);
                Txt.Line(dl, new Vector2(bmin.X + S(7), bmin.Y + S(5)), ReplayText.Number(i + 1), Face.MonoSemibold, 11,
                    on ? Palette.White : past ? Palette.EmberHi : Palette.Muted, 14);
                if (bw >= S(75))
                    Txt.Line(dl, new Vector2(bmin.X + S(7), bmax.Y - S(5) - S(16)), Txt.Fit(s.Name, Face.SansMedium, 12, bw - S(14)), Face.SansMedium, 12,
                        on || past ? Palette.Text : Palette.Muted, 16);
                dl.PopClipRect();
            }
            if (Kit.Pointed(bmin, bmax) && !held) Kit.Tooltip($"Step {i + 1}: {s.Name}, {Format.Clock(s.Start)}");
        }
        foreach (var d in m.Deaths)
        {
            var dx = MathF.Round(X(d.T));
            dl.AddRectFilled(new Vector2(dx - S(1), stepsTop - S(2)), new Vector2(dx + S(1), stepsBottom + S(2)), Palette.U32(Palette.Rgba(255, 90, 90, 0.55f)));
        }
        // Past where the pull ended, when the steps go on: hatched behind a dashed line.
        var endX = X(m.Track.End);
        if (endX < lx + lw - 1)
        {
            dl.AddRectFilled(new Vector2(endX, stepsTop), new Vector2(lx + lw, stepsBottom), Palette.U32(Palette.WithAlpha(Palette.Well, 0.7f)));
            dl.PushClipRect(new Vector2(endX, stepsTop), new Vector2(lx + lw, stepsBottom), true);
            for (var hatch = endX - S(ReplayLanes.Steps); hatch < lx + lw; hatch += S(12))
                dl.AddLine(new Vector2(hatch, stepsBottom), new Vector2(hatch + S(ReplayLanes.Steps), stepsTop), Palette.U32(Palette.Rgba(255, 255, 255, 0.05f)), S(5));
            dl.PopClipRect();
            BoardArt.DashedLine(dl, new Vector2(endX, stepsTop), new Vector2(endX, stepsBottom), Palette.U32(Palette.Rgba(255, 255, 255, 0.35f)), 1f, S(3), S(3));
        }

        // The lanes: the bosses' buffs, the party's debuffs (or each member's, filling the window).
        var y = stepsBottom + S(ReplayLanes.Gap);
        var laneBottom = rulerTop - S(ReplayLanes.Gap);
        if (replayExpanded)
        {
            // Filling the window, the lanes scroll between the steps and the ruler.
            ImGui.SetCursorScreenPos(new Vector2(frameMin.X + 1, y));
            var open = ImGui.BeginChild("##replay-lanes", new Vector2(frameMax.X - frameMin.X - 2, MathF.Max(S(20), laneBottom - y)), false, ImGuiWindowFlags.NoBackground);
            Frame.Open.Push(false);
            try
            {
                if (open)
                {
                    var cdl = ImGui.GetWindowDrawList();
                    var start = ImGui.GetCursorScreenPos();
                    var ly = start.Y;
                    if (lanes.BossRows > 0) ly = Lane(cdl, gx, lx, ly, "Boss", false, lanes.Boss, lanes.BossRows, false, held);
                    for (var i = 0; i < lanes.Members.Count; i++)
                    {
                        var (name, bars, rows) = lanes.Members[i];
                        ly = Lane(cdl, gx, lx, ly, name, true, bars, Math.Max(1, rows), i % 2 == 1, held);
                    }
                    // The lanes' own seek (the child takes the pointer from the body's), and the play head through them.
                    var (_, laneOver, laneHeld) = Kit.Hit("##replay-lanes-seek", new Vector2(lx - S(8), start.Y), new Vector2(lw + S(16), MathF.Max(1, ly - start.Y)));
                    if (laneHeld)
                    {
                        var f = Math.Clamp((ImGui.GetMousePos().X - lx) / MathF.Max(1, lw), 0, 1);
                        ReplaySeek(m, Math.Min(m.Track.End, f * total));
                    }
                    if (laneOver || laneHeld) ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);
                    var chx = MathF.Round(X(replayT));
                    var cBottom = MathF.Max(ly, start.Y + laneBottom - y);
                    cdl.AddRectFilled(new Vector2(chx - S(2), start.Y), new Vector2(chx + S(2), cBottom), Palette.U32(Palette.Rgba(0, 0, 0, 0.6f)), S(1));
                    cdl.AddRectFilled(new Vector2(chx - S(1), start.Y), new Vector2(chx + S(1), cBottom), Palette.U32(Palette.White), S(1));
                    ImGui.SetCursorScreenPos(start);
                    ImGui.Dummy(new Vector2(1, ly - start.Y));
                }
            }
            finally
            {
                if (Frame.Open.Count > 0) Frame.Open.Pop();
                ImGui.EndChild();
            }
        }
        else
        {
            if (lanes.BossRows > 0) y = Lane(dl, gx, lx, y, "Boss", false, lanes.Boss, lanes.BossRows, false, held);
            if (lanes.PartyRows > 0) Lane(dl, gx, lx, y, "Party", false, lanes.Party, lanes.PartyRows, false, held);
        }

        // The ruler, its times and the deaths' skulls.
        var times = RulerTimes(total, lw / Scale);
        for (var i = 0; i < times.Count; i++)
        {
            var label = ReplayText.Clock(times[i]);
            var tw = Txt.Width(label, Face.Mono, 10);
            var tx = i == 0 ? X(times[i]) : X(times[i]) - tw / 2;
            Txt.Line(dl, new Vector2(tx, rulerTop + S(3)), label, Face.Mono, 10, Palette.Faint, 12);
        }
        for (var i = 0; i < m.Deaths.Count; i++)
        {
            var d = m.Deaths[i];
            var c = new Vector2(X(d.T), rulerBottom - S(1) - S(8));
            dl.AddCircleFilled(c, S(8), Palette.U32(Palette.Well), 16);
            Icons.Draw(dl, Icon.Skull, c - new Vector2(S(6.5f)), S(13), Palette.Danger);
            var rang = replayRingAt.Length > i ? ImGui.GetTime() - replayRingAt[i] : 99;
            if (replayRingAt.Length > i && replayRingAt[i] > 0 && rang < RingFor && !ReducedMotion)
            {
                var f = (float)(rang / RingFor);
                dl.AddCircle(c, S(12) * (0.67f + 0.83f * f), Palette.U32(Palette.WithAlpha(Palette.Danger, 0.9f * (1 - f))), 24, S(1.5f));
            }
            if (Kit.Pointed(c - new Vector2(S(8)), c + new Vector2(S(8))) && !held) Kit.Tooltip(d.Words);
        }

        // The moment's tag over the play head, held inside the row at its two ends.
        var stepName = m.Steps.Count > 0 ? m.Steps[step].Name : "Pull";
        var timeText = ReplayText.Clock(replayT);
        var tagNum = ReplayText.Number(step + 1);
        var tt = Txt.Width(timeText, Face.MonoSemibold, 11);
        var nw = Txt.Width(tagNum, Face.Sans, 11) + Txt.Width(" · ", Face.Sans, 11);
        var nameMax = MathF.Min(S(352), lw) - S(16) - tt - S(6) - nw;
        var nameFit = Txt.Fit(stepName, Face.Sans, 11, MathF.Max(S(20), nameMax));
        var tagW = S(8) + tt + S(6) + nw + Txt.Width(nameFit, Face.Sans, 11) + S(8);
        var f0 = (headX - lx) / MathF.Max(1, lw);
        var shift = f0 < 0.12f ? -MathF.Min(50, f0 / 0.12f * 50) : f0 > 0.88f ? -50 - MathF.Min(50, (f0 - 0.88f) / 0.12f * 50) : -50;
        var tagX = Math.Clamp(headX + tagW * shift / 100, lx, MathF.Max(lx, lx + lw - tagW));
        var tagMin = new Vector2(tagX, top);
        dl.AddRectFilled(tagMin, tagMin + new Vector2(tagW, S(20)), Palette.U32(Palette.Ember), S(6));
        var tx2 = tagX + S(8);
        tx2 += Txt.Line(dl, new Vector2(tx2, top), timeText, Face.MonoSemibold, 11, Palette.OnEmber, 20) + S(6);
        tx2 += Txt.Line(dl, new Vector2(tx2, top), tagNum, Face.Sans, 11, Palette.OnEmber, 20);
        tx2 += Txt.Line(dl, new Vector2(tx2, top), " · ", Face.Sans, 11, Palette.OnEmber, 20);
        Txt.Line(dl, new Vector2(tx2, top), nameFit, Face.Sans, 11, Palette.OnEmber, 20);

        // The play head, white, through every lane under the tag.
        var hx = MathF.Round(headX);
        dl.AddRectFilled(new Vector2(hx - S(2), top + S(20)), new Vector2(hx + S(2), rulerBottom), Palette.U32(Palette.Rgba(0, 0, 0, 0.6f)), S(1));
        dl.AddRectFilled(new Vector2(hx - S(1), top + S(20)), new Vector2(hx + S(1), rulerBottom), Palette.U32(Palette.White), S(1));
    }

    private List<double>? rulerTimes;
    private (double Total, int Width) rulerFor;

    private List<double> RulerTimes(double total, float width)
    {
        var key = (total, (int)width);
        if (rulerTimes == null || rulerFor != key)
        {
            rulerFor = key;
            rulerTimes = ReplayLanes.RulerTimes(total, width);
        }
        return rulerTimes;
    }

    /// <summary>A lane's name in the gutter, in mono capitals (a member's, filling the window, as the panel says it).</summary>
    private static void GutterLabel(ImDrawListPtr dl, float x, float y, float h, string text, bool name = false)
    {
        if (name) Txt.Line(dl, new Vector2(x, y + (h - S(16)) / 2), Txt.Fit(text, Face.SansMedium, 12, S(65.6f) + S(70) - S(20)), Face.SansMedium, 12, Palette.Text, 16);
        else Kit.Label(dl, new Vector2(x, y + (h - Kit.EyebrowH) / 2 + S(1)), text, 10, 0.08f, Palette.Faint);
    }

    /// <summary>
    /// A lane of statuses: a bar each from when it came to when it went, its icon at its start and (wide enough) its name
    /// and how many had it; several at once that found no row, one bar of their icons and "+n" (their names where there's
    /// room). Blue for a buff, red for a debuff. Returns where the next lane starts.
    /// </summary>
    private float Lane(ImDrawListPtr dl, float gx, float lx, float y, string label, bool member, List<LaneBar<LaneItem>> bars, int rows, bool shade, bool held)
    {
        var h = S(ReplayLanes.StatusLanePx(rows));
        if (shade) dl.AddRectFilled(new Vector2(gx - S(12), y), new Vector2(lx + (dockLanes?.Width ?? 0) + S(8), y + h), Palette.U32(Palette.Rgba(255, 255, 255, 0.025f)));
        if (member) GutterLabel(dl, gx, y, MathF.Min(h, S(ReplayLanes.Row)), label, true);
        else GutterLabel(dl, gx, y, h, label);
        var rowH = S(ReplayLanes.Row);
        foreach (var b in bars)
        {
            var bmin = new Vector2(lx + b.Left, y + b.Row * (rowH + S(ReplayLanes.RowGap)));
            var width = MathF.Max(b.Right - b.Left, ReplayLanes.BarMinPx(b.Items.Count, S(ReplayLanes.IconStep)));
            var bmax = bmin + new Vector2(MathF.Max(rowH, width), rowH);
            var debuff = b.Items[0].Cat == 2;
            var tone = debuff ? Palette.Rgba(255, 90, 90, 1) : Palette.Rgba(90, 150, 255, 1);
            dl.AddRectFilled(bmin, bmax, Palette.U32(Palette.Well), S(5));
            dl.AddRectFilled(bmin, bmax, Palette.U32(Palette.WithAlpha(tone, debuff ? 0.13f : 0.16f)), S(5));
            dl.AddRect(bmin + new Vector2(0.5f), bmax - new Vector2(0.5f), Palette.U32(Palette.WithAlpha(tone, debuff ? 0.5f : 0.55f)), S(5), ImDrawFlags.None, 1f);
            dl.PushClipRect(bmin, bmax, true);
            var bw = bmax.X - bmin.X;
            if (b.Items.Count == 1)
            {
                var it = b.Items[0];
                var icon = IconAt(bmin + new Vector2(S(1), S(1)), it);
                ReplayIcons.Status(dl, icon.Min, icon.Max, icon.Now);
                var x = icon.Max.X + S(5);
                if (it.Count.Length > 0 && bw > S(70))
                    x += Txt.Line(dl, new Vector2(x, bmin.Y), it.Count, Face.MonoSemibold, 10, Palette.Hex(0xffb4b4), 22) + S(5);
                if (bw > S(it.Count.Length > 0 ? 96 : 70))
                    Txt.Line(dl, new Vector2(x, bmin.Y), Txt.Fit(it.Name, Face.Sans, 11, bmax.X - S(6) - x), Face.Sans, 11, Palette.Text, 22);
            }
            else
            {
                var shown = Math.Min(ReplayLanes.ShownIcons, b.Items.Count);
                var named = bw >= shown * S(110) + (b.Items.Count > ReplayLanes.ShownIcons ? S(24) : 0);
                var x = bmin.X + S(1);
                var share = named ? (bw - S(4)) / shown : S(ReplayLanes.IconStep);
                for (var i = 0; i < shown; i++)
                {
                    var it = b.Items[i];
                    var icon = IconAt(new Vector2(x, bmin.Y + S(1)), it);
                    ReplayIcons.Status(dl, icon.Min, icon.Max, icon.Now);
                    if (named)
                    {
                        var nx = icon.Max.X + S(4);
                        if (it.Count.Length > 0) nx += Txt.Line(dl, new Vector2(nx, bmin.Y), it.Count, Face.MonoSemibold, 10, Palette.Hex(0xffb4b4), 22) + S(4);
                        Txt.Line(dl, new Vector2(nx, bmin.Y), Txt.Fit(it.Name, Face.Sans, 11, x + share - S(6) - nx), Face.Sans, 11, Palette.Text, 22);
                        if (i > 0) dl.AddLine(new Vector2(x - S(2), bmin.Y + S(4)), new Vector2(x - S(2), bmax.Y - S(4)), Palette.U32(Palette.Rgba(255, 255, 255, 0.14f)), 1f);
                    }
                    x += share;
                }
                if (b.Items.Count > ReplayLanes.ShownIcons)
                    Txt.Line(dl, new Vector2(x + S(2), bmin.Y), ReplayText.Plus(b.Items.Count - ReplayLanes.ShownIcons), Face.MonoSemibold, 10, Palette.Hex(0xffb4b4), 22);
            }
            dl.PopClipRect();
            if (Kit.Pointed(bmin, bmax) && !held) Kit.Tooltip(BarWords(b));
        }
        return y + h + S(ReplayLanes.Gap);
    }

    /// <summary>A lane's icon: 15 by 20 at a bar's start (the game's 3:4), as a status now.</summary>
    private static (Vector2 Min, Vector2 Max, StatusNow Now) IconAt(Vector2 at, LaneItem it) =>
        (at, at + new Vector2(S(15), S(20)), new StatusNow { Id = it.Id, Name = it.Name, Icon = it.Icon, Cat = it.Cat, Stacks = it.Stacks, Left = float.NaN, Share = float.NaN });

    private static readonly Dictionary<LaneBar<LaneItem>, string> BarWordsKept = new();

    private static readonly Dictionary<(string, int), string> Ids = new();

    /// <summary>An ImGui id with a number, made once.</summary>
    private static string IdOf(string prefix, int i)
    {
        if (Ids.TryGetValue((prefix, i), out var id)) return id;
        return Ids[(prefix, i)] = prefix + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>A bar's tooltip: each status on it, with whose and when, and the game's description for one alone.</summary>
    private static string BarWords(LaneBar<LaneItem> b)
    {
        if (BarWordsKept.TryGetValue(b, out var kept)) return kept;
        if (BarWordsKept.Count > 512) BarWordsKept.Clear();
        var words = string.Join("\n", System.Linq.Enumerable.Select(b.Items, x => x.Words));
        if (b.Items.Count == 1 && ReplayIcons.Description(b.Items[0].Id) is { Length: > 0 } desc) words += "\n" + desc;
        return BarWordsKept[b] = words;
    }
}
