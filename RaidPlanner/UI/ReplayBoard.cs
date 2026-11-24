using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using RaidPlanner.Core;
using RaidPlanner.UI.Model;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>
/// Replay's board (the site's board in Replay, `RaidBoard.vue` with `replay.ts`'s objects and `ReplayOverlay.vue` over
/// them): the pull's arena (the plan board's, a real arena's polygons when a plan for the duty gives one), the waymarks,
/// the party's ground effects as faint green floors with their names, each mechanic's telegraph built up by brightness
/// as it's cast, the tethers, the enemies (bosses named, turned to their facing) with their HP as a white ring and a
/// dashed line to whom each is on, the head signs, and the party as their spots' colours with the game's job icons on
/// them, their HP as a ring (green, amber under half, red under a quarter), a red ring and × on the dead, a green ring as
/// someone's raised, a red flash as a hit lands; the fight's statuses as the game's icons beside each member (two and
/// "+2", a ring for the time left) and over each enemy, and head markers as a small badge over the head. Board units are
/// the site's (±500, the arena within ±420, y down). Nothing is allocated as it's drawn.
/// </summary>
public static class ReplayBoard
{
    private const float ArenaExtent = 420;

    /// <summary>A member's token's radius, board units (the site's 22).</summary>
    private const float Token = 22;

    private static readonly List<DrawnObj> NoObjects = new();
    private static readonly List<DrawnObj> Marks = new(8);
    private static ReplayWaymarks? marksFor;

    /// <summary>The waymarks' colours: A and 1 red, B and 2 yellow, C and 3 blue, D and 4 purple.</summary>
    private static uint WaymarkColor(string name) => name switch
    {
        "A" or "1" => 0xe0473c,
        "B" or "2" => 0xe9c43f,
        "C" or "3" => 0x3f8be9,
        _ => 0xb45cdd,
    };

    /// <summary>The waymarks as the plan board draws them, made again only when they change.</summary>
    private static List<DrawnObj> WaymarkObjects(ReplayScene sc)
    {
        var now = sc.WaymarksNow;
        if (ReferenceEquals(now, marksFor)) return Marks;
        marksFor = now;
        Marks.Clear();
        if (now == null) return Marks;
        foreach (var (name, x, z) in now.Marks)
        {
            var o = new BoardObj(BoardKind.Waymark, "waymark", sc.Frame.BoardX(x), sc.Frame.BoardY(z), 0, 30, WaymarkColor(name), 1, name, name, 0, 0, 0, 0, 0, 0, null);
            Marks.Add(new DrawnObj(o, Palette.Hex(o.Color), name));
        }
        return Marks;
    }

    /// <summary>Draws the board in a square from <paramref name="min"/>, <paramref name="size"/> px across; <paramref name="focus"/> a spot viewed as (the rest faded).</summary>
    public static void Draw(ImDrawListPtr dl, Vector2 min, float size, ReplayModel m, ReplayScene sc, string? focus)
    {
        // The arena and the waymarks, as the plan board draws them.
        PlanBoard.Draw(dl, min, size, m.Arena, sc.WaymarksNow != null ? WaymarkObjects(sc) : NoObjects);
        var max = min + new Vector2(size);
        dl.PushClipRect(min, max, true);
        var old = Palette.Opacity;
        try
        {
            var k = size / 1000f;
            var c = min + new Vector2(size / 2);
            var unit = ArenaExtent * k / MathF.Max(1, m.Arena.Yalms);
            var tr = sc.Track;
            Vector2 P(float bx, float by) => c + new Vector2(bx, by) * k;
            Vector2 W(in ReplayPoint p) => c + new Vector2(sc.Frame.BoardX(p.X), sc.Frame.BoardY(p.Z)) * k;

            // The party's ground effects, under everything.
            foreach (var a in tr.Areas)
            {
                if (a.T > sc.T || a.Until <= sc.T) continue;
                var p = P(sc.Frame.BoardX(a.X), sc.Frame.BoardY(a.Z));
                var r = MathF.Max(8, a.Radius * sc.Frame.Upy) * k;
                dl.AddCircleFilled(p, r, Palette.U32(Palette.ReadyWash), 48);
                BoardArt.DashedArc(dl, p, r, 0, 360, Palette.U32(Palette.ReadyLine), S(1.5f), S(5), S(4));
                Outlined(dl, p + new Vector2(0, r - S(10)), a.Name, Palette.Ready);
            }

            // The telegraphs.
            var rim = MathF.Max(1, 0.17f * unit);
            foreach (var t in sc.Teles) DrawTele(dl, P(t.X, t.Y), k, unit, rim, t);

            // The tethers.
            foreach (var t in sc.TethersNow) BoardArt.Tether(dl, P(t.X1, t.Y1), P(t.X2, t.Y2), Palette.Hex(0xc084fc), unit, false);

            // The enemies, with their HP and whom each is on.
            for (var i = 0; i < tr.Entities.Count; i++)
            {
                var e = tr.Entities[i];
                ref readonly var p = ref sc.Points[i];
                if (!p.Here || e.Kind is EntityKind.Party or EntityKind.Helper) continue;
                Palette.Opacity = old * (focus != null ? 0.6f : 1);
                var at = W(in p);
                var r = MathF.Max(e.Kind == EntityKind.Boss ? 40 : 24, MathF.Round(e.Hitbox * sc.Frame.Upy)) * k;
                if (sc.TargetOf[i] != 0 && tr.Index.TryGetValue(sc.TargetOf[i], out var ti) && sc.Points[ti].Here)
                    BoardArt.DashedLine(dl, at, W(in sc.Points[ti]), Palette.U32(Palette.WithAlpha(Palette.White, 0.55f)), S(1.6f), S(6), S(4));
                Enemy(dl, at, MathF.Max(S(6), r), ReplayShapes.PlanRotation(p.Rot), e.Kind, unit);
                var hp = HpShare(p.Hp, e.MaxHp);
                if (hp >= 0)
                {
                    var ring = r + S(7);
                    dl.AddCircle(at, ring, Palette.U32(Palette.Rgba(0, 0, 0, 0.55f)), 48, S(3));
                    Arc(dl, at, ring, hp, Palette.U32(Palette.White), S(3));
                }
                if (e.Kind == EntityKind.Boss && e.Name.Length > 0) Outlined(dl, at + new Vector2(0, r + S(8) + S(7)), e.Name, Palette.White);
                Statuses(dl, sc.StatusesOn[i], at, r + S(7), true, sc, i);
            }
            Palette.Opacity = old;

            // The head signs, over the head.
            if (sc.SignsNow is { } signs)
                foreach (var (index, on) in signs.Marks)
                    if (tr.Index.TryGetValue(on, out var si) && sc.Points[si].Here)
                        Sign(dl, W(in sc.Points[si]) - new Vector2(0, 34 * k + S(6)), index);

            // The party: who's everyone who isn't everyone (what all the living have at once, the board leaves out).
            var everyone = CommonStatus(sc, m);
            for (var j = 0; j < m.PartyOrder.Count; j++)
            {
                var i = m.PartyOrder[j];
                var e = tr.Entities[i];
                ref readonly var p = ref sc.Points[i];
                if (!p.Here) continue;
                Palette.Opacity = old * (focus != null && focus != e.Slot ? 0.3f : 1);
                var at = W(in p);
                var r = MathF.Max(S(6), Token * k);
                var dead = !double.IsNaN(sc.DeadSince[i]);
                var hit = sc.HitAgo[i];
                if (!double.IsNaN(hit) && hit < ReplayScene.HitFor)
                {
                    var f = (float)(hit / ReplayScene.HitFor);
                    dl.AddCircle(at, r + S(3), Palette.U32(Palette.WithAlpha(Palette.Danger, 1 - f)), 32, S(3 + 6 * f));
                }
                // The token: a dark halo, its spot's colour, the game's job icon on it.
                var col = Palette.SpotColor(e.Slot);
                dl.AddCircleFilled(at, r * 1.22f, Palette.U32(Palette.WithAlpha(Palette.OnLight, 0.85f)), 28);
                dl.AddCircleFilled(at, r, Palette.U32(dead ? Palette.Hex(0x3a3a42) : col), 28);
                dl.AddCircle(at, r, Palette.U32(focus == e.Slot ? Palette.White : Palette.Rgba(255, 255, 255, 0.28f)), 28, focus == e.Slot ? S(2) : 1f);
                var icon = r * 1.45f;
                PlateArt.JobIcon(dl, at - new Vector2(icon / 2), icon / Scale, e.Job, e.Role, col, null, dead, Palette.Muted);
                if (dead)
                {
                    dl.AddCircle(at, r + S(4), Palette.U32(Palette.Danger), 32, S(2.2f));
                    var d = r * 0.5f;
                    dl.AddLine(at - new Vector2(d), at + new Vector2(d), Palette.U32(Palette.Danger), S(3));
                    dl.AddLine(at + new Vector2(d, -d), at + new Vector2(-d, d), Palette.U32(Palette.Danger), S(3));
                }
                else if (HpShare(p.Hp, e.MaxHp) is var hp and >= 0)
                {
                    var ring = r + S(5);
                    dl.AddCircle(at, ring, Palette.U32(Palette.Rgba(0, 0, 0, 0.55f)), 32, S(3));
                    Arc(dl, at, ring, hp, Palette.U32(hp < 0.25f ? Palette.Danger : hp < 0.5f ? Palette.Warn : Palette.Ready), S(3));
                }
                if (!double.IsNaN(sc.Raised[i]))
                {
                    var f = (float)(sc.Raised[i] / ReplayScene.RaisedFor);
                    var grow = ReducedMotion ? 4 : (float)sc.Raised[i] * 10;
                    dl.AddCircle(at, r + S(6 + grow), Palette.U32(Palette.WithAlpha(Palette.Ready, 1 - f)), 32, S(2.5f));
                }
                Outlined(dl, at + new Vector2(0, r + S(7) + S(7)), e.Slot, Palette.White);
                if (!dead) Statuses(dl, sc.StatusesOn[i], at, r + S(5), false, sc, i, everyone);
            }
            Palette.Opacity = old;

            // Head markers: a badge over the head.
            foreach (var b in sc.Badges)
            {
                var e = tr.Entities[b.Entity];
                ref readonly var p = ref sc.Points[b.Entity];
                var enemy = e.Kind != EntityKind.Party;
                var r = enemy ? MathF.Max(e.Kind == EntityKind.Boss ? 40 : 24, MathF.Round(e.Hitbox * sc.Frame.Upy)) * k : MathF.Max(S(6), Token * k);
                Palette.Opacity = old * (focus != null && (enemy || focus != e.Slot) ? (enemy ? 0.6f : 0.3f) : 1);
                var px = S(Math.Clamp(0.6f * Token * k / Scale, 7, 9));
                var at = W(in p) - new Vector2(0, r + S(enemy ? 8.5f : 6.5f) + S(1.5f) + px);
                Badge(dl, at, px, b);
            }
        }
        finally
        {
            Palette.Opacity = old;
            dl.PopClipRect();
        }
    }

    /// <summary>HP as a share of the most (0–1), or −1 when either isn't known.</summary>
    public static float HpShare(float hp, long max) => !float.IsFinite(hp) || max <= 0 ? -1 : Math.Clamp(hp / max, 0, 1);

    /// <summary>A ring's share drawn from its top, clockwise.</summary>
    private static void Arc(ImDrawListPtr dl, Vector2 c, float r, float share, uint col, float width)
    {
        if (share <= 0) return;
        dl.PathClear();
        BoardArt.Arc(dl, c, r, 0, 360 * share, Math.Max(8, (int)(48 * share)));
        dl.PathStroke(col, ImDrawFlags.None, width);
    }

    /// <summary>What every living member has at once (TOP's Remote Glitch on all eight), which tells no one apart: a status id, else 0.</summary>
    private static uint CommonStatus(ReplayScene sc, ReplayModel m)
    {
        var living = 0;
        List<StatusNow>? first = null;
        foreach (var i in m.PartyOrder)
            if (sc.Points[i].Here && double.IsNaN(sc.DeadSince[i]))
            {
                living++;
                first ??= sc.StatusesOn[i];
            }
        if (living < 2 || first == null) return 0;
        foreach (var s in first)
        {
            var all = true;
            foreach (var i in m.PartyOrder)
            {
                if (!sc.Points[i].Here || !double.IsNaN(sc.DeadSince[i])) continue;
                var has = false;
                foreach (var x in sc.StatusesOn[i])
                    if (x.Id == s.Id)
                    {
                        has = true;
                        break;
                    }
                if (!has)
                {
                    all = false;
                    break;
                }
            }
            if (all) return s.Id;
        }
        return 0;
    }

    /// <summary>
    /// One's statuses on the board (the site's <c>boardStatuses</c>): a member's beside their token on the right, clear of
    /// its HP ring, two and "+2"; an enemy's in a row centred over its HP ring. Each the game's icon on a dark edge with a
    /// white ring for its time left. None when the board is too small for them.
    /// </summary>
    private static void Statuses(ImDrawListPtr dl, List<StatusNow> list, Vector2 at, float ringR, bool enemy, ReplayScene sc, int index, uint skip = 0)
    {
        if (list.Count == 0) return;
        var k = 1000f / MathF.Max(1, BoardSize) * Scale;
        if (k > 4.5f) return;
        var h = S(Math.Clamp(0.9f * 44 / k, 18, 24));
        var w = h * 0.75f;
        var gap = S(2);
        var n = 0;
        foreach (var s in list)
            if (s.Id != skip) n++;
        if (n == 0) return;
        var shown = Math.Min(2, n);
        var rest = n - shown;
        var moreText = rest > 0 ? ReplayText.Plus(rest) : null;
        var moreW = moreText != null ? Txt.Width(moreText, Face.SansSemibold, 10) + gap : 0;
        float left, top;
        if (enemy)
        {
            var width = shown * w + (shown - 1) * gap + moreW;
            var marked = false;
            foreach (var b in sc.Badges)
                if (b.Entity == index) marked = true;
            left = at.X - width / 2;
            top = at.Y - ringR - S(4) - (marked ? S(20) : 0) - h;
        }
        else
        {
            left = at.X + ringR + S(4.5f);
            top = at.Y - h / 2;
        }
        var x = left;
        var drawn = 0;
        foreach (var s in list)
        {
            if (s.Id == skip) continue;
            if (drawn == shown) break;
            var min = new Vector2(x, top);
            var max = min + new Vector2(w, h);
            dl.AddRectFilled(min - new Vector2(S(1.25f)), max + new Vector2(S(1.25f)), Palette.U32(Palette.Rgba(0, 0, 0, 0.6f)), S(2));
            ReplayIcons.Status(dl, min, max, s);
            if (!float.IsNaN(s.Share)) ShareRing(dl, min - new Vector2(S(1.25f)), max + new Vector2(S(1.25f)), s.Share, Palette.U32(Palette.White), S(1.3f));
            x += w + gap;
            drawn++;
        }
        if (moreText != null) Outlined(dl, new Vector2(x + Txt.Width(moreText, Face.SansSemibold, 10) / 2, top + h / 2), moreText, Palette.White);
    }

    /// <summary>The board's size in px as last drawn (the status icons' size follows it).</summary>
    public static float BoardSize { get; set; } = 600;

    /// <summary>A ring round a box for the share of a status's time left: from its top's middle, clockwise.</summary>
    public static void ShareRing(ImDrawListPtr dl, Vector2 min, Vector2 max, float share, uint col, float width)
    {
        if (share <= 0) return;
        var w = max.X - min.X;
        var h = max.Y - min.Y;
        var total = 2 * (w + h);
        var left = share * total;
        Span<Vector2> pts = stackalloc Vector2[7];
        var n = 0;
        var at = new Vector2(min.X + w / 2, min.Y);
        pts[n++] = at;
        // Its sides in order from the top's middle: right half of the top, right side, bottom, left side, left half of the top.
        Span<Vector2> corners = stackalloc Vector2[] { new(max.X, min.Y), new(max.X, max.Y), new(min.X, max.Y), new(min.X, min.Y), new(min.X + w / 2, min.Y) };
        foreach (var next in corners)
        {
            var len = Vector2.Distance(at, next);
            if (len >= left)
            {
                pts[n++] = at + (next - at) * (left / MathF.Max(0.001f, len));
                break;
            }
            pts[n++] = next;
            left -= len;
            at = next;
        }
        if (n >= 2) dl.AddPolyline(ref pts[0], n, col, ImDrawFlags.None, width);
    }

    /// <summary>A head marker's badge (the site's <c>headMarkerGlyph</c>): a disc in its kind's colour on a dark edge, a limit cut's number or a small pictogram.</summary>
    private static void Badge(ImDrawListPtr dl, Vector2 c, float r, in BadgeNow b)
    {
        var kind = b.Kind != MarkerMeaning.None ? b.Kind : b.Role switch
        {
            MechRole.Spread => MarkerMeaning.Spread,
            MechRole.Stack => MarkerMeaning.Stack,
            MechRole.LineStack => MarkerMeaning.LineStack,
            MechRole.Tankbuster or MechRole.SharedTankbuster => MarkerMeaning.Tankbuster,
            _ => MarkerMeaning.None,
        };
        var col = kind switch
        {
            MarkerMeaning.Spread => Palette.Apart,
            MarkerMeaning.Stack or MarkerMeaning.LineStack => Palette.Share,
            MarkerMeaning.Tankbuster => Palette.Tanks,
            MarkerMeaning.LimitCut => Palette.Hex(0x5ad1ff),
            MarkerMeaning.Target or MarkerMeaning.RotateCw or MarkerMeaning.RotateCcw => Palette.Dodge,
            _ => Palette.Hex(0xc3c8d4),
        };
        var ink = kind is MarkerMeaning.Spread or MarkerMeaning.Tankbuster ? Palette.White : Palette.OnLight;
        dl.AddCircleFilled(c, r + S(1.5f), Palette.U32(Palette.Rgba(0, 0, 0, 0.7f)), 20);
        dl.AddCircleFilled(c, r, Palette.U32(col), 20);
        var g = r * 1.35f;
        switch (kind)
        {
            case MarkerMeaning.LimitCut when b.Number > 0:
            {
                var text = ReplayText.Number(b.Number);
                var size = Txt.Measure(text, Face.MonoSemibold, 10);
                Txt.Draw(dl, c - size / 2, text, Face.MonoSemibold, 10, ink);
                break;
            }
            case MarkerMeaning.Spread:
                Icons.Draw(dl, Icon.Move, c - new Vector2(g / 2), g, ink, 2.4f);
                break;
            case MarkerMeaning.Stack:
            case MarkerMeaning.LineStack:
                dl.AddCircleFilled(c, r * 0.32f, Palette.U32(ink), 12);
                dl.AddCircle(c, r * 0.68f, Palette.U32(ink), 16, MathF.Max(1, r * 0.16f));
                break;
            case MarkerMeaning.Tankbuster:
                Icons.Draw(dl, Icon.Shield, c - new Vector2(g / 2), g, ink, 2.4f);
                break;
            case MarkerMeaning.RotateCw:
                Icons.Draw(dl, Icon.RefreshCw, c - new Vector2(g / 2), g, ink, 2.4f);
                break;
            case MarkerMeaning.RotateCcw:
                Icons.Draw(dl, Icon.Undo, c - new Vector2(g / 2), g, ink, 2.4f);
                break;
            case MarkerMeaning.Target:
                dl.AddCircle(c, r * 0.45f, Palette.U32(ink), 12, MathF.Max(1, r * 0.18f));
                dl.AddCircleFilled(c, r * 0.14f, Palette.U32(ink), 8);
                break;
            default:
                dl.AddTriangleFilled(c + new Vector2(-r * 0.45f, -r * 0.3f), c + new Vector2(r * 0.45f, -r * 0.3f), c + new Vector2(0, r * 0.4f), Palette.U32(ink));
                break;
        }
    }

    /// <summary>A head sign (the party's target markers, by the game's index): attack 1–8, bind 1–3, ignore 1–2 as a tile with its number, the shapes as themselves.</summary>
    private static void Sign(ImDrawListPtr dl, Vector2 c, int index)
    {
        var s = S(7);
        var (col, text) = index switch
        {
            <= 4 => (Palette.Hex(0xff6b6b), ReplayText.Number(index + 1)),
            <= 7 => (Palette.Hex(0x5aa2ff), ReplayText.Number(index - 4)),
            <= 9 => (Palette.Hex(0x9a9aa6), ReplayText.Number(index - 7)),
            >= 14 => (Palette.Hex(0xff6b6b), ReplayText.Number(index - 8)),
            _ => (Palette.Hex(index switch { 10 => 0xff7ad9u, 11 => 0xff5468u, 12 => 0x5aa2ffu, _ => 0x45d483u }), (string?)null),
        };
        dl.AddRectFilled(c - new Vector2(s), c + new Vector2(s), Palette.U32(Palette.WithAlpha(Palette.OnLight, 0.85f)), S(2));
        dl.AddRect(c - new Vector2(s), c + new Vector2(s), Palette.U32(col), S(2), ImDrawFlags.None, S(1.2f));
        if (text != null)
        {
            var size = Txt.Measure(text, Face.MonoSemibold, 10);
            Txt.Draw(dl, c - size / 2, text, Face.MonoSemibold, 10, col);
            return;
        }
        var u = Palette.U32(col);
        var h = s * 0.55f;
        var w = S(1.4f);
        switch (index)
        {
            case 10:
                dl.AddRect(c - new Vector2(h), c + new Vector2(h), u, 0, ImDrawFlags.None, w);
                break;
            case 11:
                dl.AddCircle(c, h, u, 12, w);
                break;
            case 12:
                dl.AddLine(c - new Vector2(h), c + new Vector2(h), u, w);
                dl.AddLine(c + new Vector2(h, -h), c + new Vector2(-h, h), u, w);
                break;
            default:
                dl.AddTriangle(c + new Vector2(0, -h), c + new Vector2(h, h * 0.75f), c + new Vector2(-h, h * 0.75f), u, w);
                break;
        }
    }

    /// <summary>
    /// An enemy (the plan board's): a boss with its hitbox ring (its back quarter dashed) and its facing triangle, then the
    /// Boss's shield and face; an add, the shield alone.
    /// </summary>
    private static void Enemy(ImDrawListPtr dl, Vector2 p, float hit, float rot, EntityKind kind, float unit)
    {
        var boss = kind == EntityKind.Boss;
        var col = boss ? Palette.Boss : Palette.Hex(0xe0483e);
        if (boss)
        {
            dl.AddCircleFilled(p, hit, Palette.U32(Palette.WithAlpha(col, 0.14f)), 40);
            dl.PathClear();
            BoardArt.Arc(dl, p, hit, rot + 225 - 360, rot + 135, 30);
            dl.PathStroke(Palette.U32(col), ImDrawFlags.None, MathF.Max(1, 0.2f * unit));
            BoardArt.DashedArc(dl, p, hit, rot + 135, rot + 225, Palette.U32(Palette.WithAlpha(col, 0.5f)), MathF.Max(1, 0.12f * unit), 0.4f * unit, 0.4f * unit);
            BoardArt.Triangle(dl, BoardArt.Pol(p, hit + 0.5f * unit, rot), rot, MathF.Max(S(5), 1.3f * unit), Palette.U32(col));
        }
        var face = (boss ? hit * 0.72f : hit * 0.86f) * 2 * 1.25f;
        Boss.Draw(dl, p, MathF.Max(S(12), face), BossMood.Awake, col, boss ? EnemyKind.Boss : EnemyKind.Add, rot * MathF.PI / 180f);
    }

    /// <summary>A telegraph as the plan board draws its kind, its colour as bright as it's built up.</summary>
    private static void DrawTele(ImDrawListPtr dl, Vector2 p, float k, float unit, float rim, in Tele t)
    {
        var col = Palette.Hex(t.Color, t.Bright);
        var r = MathF.Max(S(2), t.Size * k);
        switch (t.Kind)
        {
            case BoardKind.Circle:
                BoardArt.TeleCircle(dl, p, r, col, rim);
                break;
            case BoardKind.Donut:
                BoardArt.TeleDonut(dl, p, r, t.Inner * k, col, rim);
                break;
            case BoardKind.Cone:
                BoardArt.TeleCone(dl, p, r, t.Angle, t.Rot, col, rim);
                break;
            case BoardKind.Rect:
            {
                var w = t.Width * k;
                var l = t.Length * k;
                BoardArt.TeleRect(dl, p, -w / 2, -l / 2, w / 2, l / 2, t.Rot, col, rim);
                break;
            }
            case BoardKind.LineStack:
            {
                var w = t.Width * k;
                var l = t.Length * k;
                BoardArt.TeleRect(dl, p, -w / 2, -l, w / 2, 0, t.Rot, col, rim);
                var chev = Palette.U32(col);
                for (var i = 1; i <= 2; i++)
                {
                    var f = 0.3f * i;
                    BoardArt.Chevron(dl, BoardArt.Turn(p, new Vector2(-w / 2 - 0.5f * unit, -l * f), t.Rot), t.Rot + 90, 1.3f * unit, chev, MathF.Max(1, 0.26f * unit));
                    BoardArt.Chevron(dl, BoardArt.Turn(p, new Vector2(w / 2 + 0.5f * unit, -l * f), t.Rot), t.Rot - 90, 1.3f * unit, chev, MathF.Max(1, 0.26f * unit));
                }
                break;
            }
            case BoardKind.Cross:
                BoardArt.TeleCross(dl, p, t.Width * k, t.Length * k / 2, t.Rot, col, rim);
                break;
            case BoardKind.Stack:
                BoardArt.Stack(dl, p, r, col, unit, rim);
                break;
            case BoardKind.Spread:
                BoardArt.Spread(dl, p, r, col, unit, rim);
                break;
            case BoardKind.Defamation:
                BoardArt.Defamation(dl, p, r, col, unit, rim);
                break;
            case BoardKind.Tankbuster:
                BoardArt.Buster(dl, p, r, col, unit, rim, t.Variant == "cleave");
                break;
            case BoardKind.SharedBuster:
                BoardArt.SharedBuster(dl, p, r, col, unit, rim);
                break;
            case BoardKind.Tower:
                BoardArt.Tower(dl, p, r, col, unit, 1, false);
                break;
            case BoardKind.Knockback:
                BoardArt.Flow(dl, p, r, unit, rim, false);
                break;
            case BoardKind.SideKnockback:
                BoardArt.SideKnockback(dl, p, t.Width * k, t.Length * k, t.Rot, 90, unit, rim);
                break;
            case BoardKind.Gaze:
                BoardArt.Gaze(dl, p, MathF.Max(S(4), r / 1.75f));
                break;
            case BoardKind.Rotation:
                BoardArt.Rotation(dl, p, r, t.Variant == "ccw", unit);
                break;
        }
    }

    /// <summary>Words centred on a point with a dark edge round them (as the board writes names).</summary>
    public static void Outlined(ImDrawListPtr dl, Vector2 p, string text, Vector4 color, Face face = Face.SansSemibold, int px = 10)
    {
        var size = Txt.Measure(text, face, px);
        var at = Txt.Snap(p - size / 2);
        var dark = Palette.WithAlpha(Palette.OnLight, 0.9f);
        Txt.Draw(dl, at + new Vector2(-1, 0), text, face, px, dark);
        Txt.Draw(dl, at + new Vector2(1, 0), text, face, px, dark);
        Txt.Draw(dl, at + new Vector2(0, -1), text, face, px, dark);
        Txt.Draw(dl, at + new Vector2(0, 1), text, face, px, dark);
        Txt.Draw(dl, at, text, face, px, color);
    }
}

/// <summary>Short words Replay draws every frame, made once: numbers, "+3", times.</summary>
public static class ReplayText
{
    private static readonly string[] Numbers = new string[1000];
    private static readonly string[] Pluses = new string[100];
    private static readonly Dictionary<long, string> Clocks = new();
    private static readonly Dictionary<long, string> Tenths = new();

    /// <summary>"7" (0 to 999), kept.</summary>
    public static string Number(int n)
    {
        if (n is < 0 or >= 1000) return n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Numbers[n] ??= n.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>"+3", kept.</summary>
    public static string Plus(int n) => n is > 0 and < 100 ? Pluses[n] ??= "+" + Number(n) : "+" + Number(n);

    /// <summary>"2:51" for a moment (whole seconds), kept.</summary>
    public static string Clock(double t)
    {
        var s = (long)Math.Floor(Math.Max(0, double.IsFinite(t) ? t : 0));
        if (Clocks.TryGetValue(s, out var v)) return v;
        if (Clocks.Count > 20000) Clocks.Clear();
        return Clocks[s] = Format.Clock(s);
    }

    /// <summary>"2.1 s" for seconds left (tenths), kept.</summary>
    public static string SecondsLeft(double t)
    {
        var tenths = (long)Math.Round(Math.Max(0, t) * 10);
        if (Tenths.TryGetValue(tenths, out var v)) return v;
        if (Tenths.Count > 20000) Tenths.Clear();
        return Tenths[tenths] = (tenths / 10.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s";
    }
}

/// <summary>
/// The game's icons in Replay: a status's (its stack count's own icon when it has one a count, from the Status sheet's
/// most stacks), else a square in its category's colour with its first letter (the site's StatusIcon); and its
/// description, from the Status sheet, once.
/// </summary>
public static class ReplayIcons
{
    private static readonly Dictionary<uint, (int MaxStacks, string Description)> Sheet = new();

    private static (int MaxStacks, string Description) Facts(uint id)
    {
        if (Sheet.TryGetValue(id, out var f)) return f;
        f = (0, string.Empty);
        try
        {
            if (Services.Data.GetExcelSheet<Lumina.Excel.Sheets.Status>().TryGetRow(id, out var row))
                f = (row.MaxStacks, row.Description.ExtractText().Replace("\r", string.Empty));
        }
        catch (Exception)
        {
            // A sheet that can't be read: no stacks, no description.
        }
        return Sheet[id] = f;
    }

    /// <summary>A status's description from the game's sheet (empty when it hasn't one).</summary>
    public static string Description(uint id) => Facts(id).Description;

    /// <summary>Draws a status's icon in a box (3:4, as the game's are).</summary>
    public static void Status(ImDrawListPtr dl, Vector2 min, Vector2 max, in StatusNow s)
    {
        var most = s.Icon != 0 ? Facts(s.Id).MaxStacks : 0;
        var icon = s.Icon;
        var counted = false;
        if (icon != 0 && most > 1 && s.Stacks >= 2 && s.Stacks <= most)
        {
            icon = (uint)(icon + s.Stacks - 1);
            counted = true;
        }
        if (icon != 0)
        {
            try
            {
                var wrap = Services.Textures.GetFromGameIcon(new GameIconLookup(icon)).GetWrapOrEmpty();
                if (wrap.Width > 1)
                {
                    dl.AddImage(wrap.Handle, min, max, Vector2.Zero, Vector2.One, Palette.U32(Vector4.One));
                    if (!counted && s.Stacks >= 2) Count(dl, max, s.Stacks);
                    return;
                }
            }
            catch (Exception)
            {
                // An icon the game can't give: the square instead.
            }
        }
        var tone = s.Cat == 2 ? Palette.Hex(0xa8342e) : s.Cat == 1 ? Palette.Hex(0x2f62b8) : Palette.Hex(0x55555e);
        dl.AddRectFilled(min, max, Palette.U32(tone), S(1.5f));
        var letter = s.Name.Length > 0 ? Letters[char.ToUpperInvariant(s.Name[0]) & 0x7f] ??= char.ToUpperInvariant(s.Name[0]).ToString() : "?";
        var size = Txt.Measure(letter, Face.SansSemibold, 10);
        Txt.Draw(dl, (min + max) / 2 - size / 2, letter, Face.SansSemibold, 10, Palette.White);
        if (s.Stacks >= 2) Count(dl, max, s.Stacks);
    }

    private static readonly string?[] Letters = new string?[128];

    private static void Count(ImDrawListPtr dl, Vector2 max, int n)
    {
        var text = ReplayText.Number(n);
        var size = Txt.Measure(text, Face.SansSemibold, 10);
        ReplayBoard.Outlined(dl, max - size / 2 + new Vector2(S(1), -S(1)), text, Palette.White);
    }
}
