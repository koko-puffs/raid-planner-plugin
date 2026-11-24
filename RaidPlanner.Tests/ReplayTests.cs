using RaidPlanner.Core;
using RaidPlanner.UI.Model;
using Xunit;

namespace RaidPlanner.Tests;

/// <summary>
/// 0.9.0's Replay, mirroring the site's specs (components/review/replay.spec.ts, replayLanes.spec.ts,
/// replayStatuses.spec.ts, model/replay.ts's): a pull's track, its moments on the board, the dock's lanes and steps,
/// the fight's statuses, and its events. Made-up pulls; no real people.
/// </summary>
public class ReplayTests
{
    private static readonly BoardFrame Frame = new(100, 100, 10);

    /// <summary>A small pull: MT (1, a tank) and H2 (2, a healer), the boss (101, hitbox 5), frames every half second at their places.</summary>
    private static Recording Pull(Action<Recording>? more = null, double length = 60, (float X, float Z)? mt = null, (float X, float Z)? h2 = null)
    {
        var r = new Recording { PluginVersion = "0.8.3", Duration = length, Outcome = Outcome.Wipe, Pull = 7, PartyPositions = true, Duty = new DutyInfo("Test Arena", 1, 2) };
        r.Party.Add(new PartyMember(1, "MT", "WAR", "tank", 100_000, Name: "Ayla Fenn"));
        r.Party.Add(new PartyMember(2, "H2", "SGE", "healer", 80_000, Name: "Rook Varo"));
        r.Actors.Add(new Actor(101, "The Test Boss", ActorKind.Boss, 5, 9001, 1_000_000));
        r.Actors.Add(new Actor(150, "", ActorKind.Helper, 0.5f, 9002));
        var (mx, mz) = mt ?? (100f, 95f);
        var (hx, hz) = h2 ?? (96f, 104f);
        for (var t = 0.0; t <= length; t += 0.5)
            r.Frames.Add(new Frame(t, new Dictionary<int, (float, float, float)> { [1] = (mx, mz, 0), [2] = (hx, hz, 0), [101] = (100, 100, 0) },
                new Dictionary<int, long> { [1] = 50_000, [2] = 80_000, [101] = 500_000 }));
        more?.Invoke(r);
        return r;
    }

    private static ReplayScene Scene(Recording r, double t, bool still = false)
    {
        var sc = new ReplayScene(ReplayTrack.Build(r), Frame);
        sc.At(t, still);
        return sc;
    }

    // ---------------------------------------------------------------------------------------------
    // Shapes and places.

    [Fact]
    public void The_games_data_gives_the_shapes()
    {
        Assert.Equal(new ReplayShape(ShapeKind.Circle, R: 6), ReplayShapes.FromGame(new ActionInfo("A", 2, 6, 0, "", 3)));
        Assert.Equal(ShapeKind.Target, ReplayShapes.FromGame(new ActionInfo("A", 2, 0, 0, "", 3))!.Kind);
        var cone = ReplayShapes.FromGame(new ActionInfo("A", 3, 40, 0, "gl_fan120_1bf", 3))!;
        Assert.Equal((ShapeKind.Cone, 40f, 120f, true), (cone.Kind, cone.R, cone.Angle, cone.Hitbox));
        Assert.Equal(90, ReplayShapes.OmenAngle("general_1bf"));
        var line = ReplayShapes.FromGame(new ActionInfo("A", 4, 50, 8, "", 3))!;
        Assert.Equal((ShapeKind.Rect, 50f, 8f, true), (line.Kind, line.Front, line.Width, line.Hitbox));
        Assert.False(ReplayShapes.FromGame(new ActionInfo("A", 12, 50, 0, "", 3))!.Hitbox);
        Assert.Equal(10, ReplayShapes.FromGame(new ActionInfo("A", 10, 20, 0, "", 3))!.Inner);
        Assert.Equal(ShapeKind.Charge, ReplayShapes.FromGame(new ActionInfo("A", 8, 0, 6, "", 3))!.Kind);
        Assert.Null(ReplayShapes.FromGame(new ActionInfo("A", 5, 0, 0, "", 3)));
        // The site's fixes: Seiryu's rings and the adds' Explosion.
        var r = Pull(p => p.Actions[14352] = new ActionInfo("Great Typhoon", 2, 28, 0, "", 3));
        Assert.Equal(new ReplayShape(ShapeKind.Donut, R: 28, Inner: 20), ReplayShapes.Of(r, 14352));
    }

    [Fact]
    public void Names_an_unnamed_auto_attack_and_says_the_rest_by_number()
    {
        var actions = new Dictionary<uint, ActionInfo> { [31742] = new("", 1, 0, 0, "", 0, Category: 1), [5] = new("  Flare ", 2, 5, 0, "", 3) };
        Assert.Equal("Auto-attack", ReplayShapes.ActionName(actions, 31742));
        Assert.Equal("Flare", ReplayShapes.ActionName(actions, 5));
        Assert.Equal("Action 9", ReplayShapes.ActionName(actions, 9));
        Assert.True(ReplayShapes.IsAutoAttack(" attack"));
        Assert.False(ReplayShapes.IsAutoAttack("Attack Order"));
    }

    [Fact]
    public void Knows_which_plugins_recorded_facing()
    {
        Assert.False(ReplayShapes.FacingRecorded("0.7.9"));
        Assert.True(ReplayShapes.FacingRecorded("0.7.10"));
        Assert.True(ReplayShapes.FacingRecorded("0.8.0"));
        Assert.Equal(180, ReplayShapes.PlanRotation(0));
        Assert.Equal(90, ReplayShapes.PlanRotation(MathF.PI / 2));
        // A caster on a square arena's east rim aims west across it; near the middle, no aim.
        var arena = new ArenaChoice("square", 40, 40, 100, 100);
        Assert.Equal(MathF.Atan2(-1, 0), ReplayShapes.AcrossArena(arena, 118, 101));
        Assert.Null(ReplayShapes.AcrossArena(arena, 101, 100));
    }

    [Fact]
    public void Reads_everyone_between_samples_and_away_in_a_gap()
    {
        var r = Pull(length: 4);
        r.Frames.Clear();
        foreach (var (t, x, rot) in new[] { (0.0, 100f, 0f), (0.5, 105f, MathF.PI / 4), (1.0, 110f, MathF.PI / 2), (10.0, 120f, 0f), (10.5, 120f, 0f), (11.0, 120f, 0f) })
            r.Frames.Add(new Frame(t, new Dictionary<int, (float, float, float)> { [1] = (x, 100, rot) }));
        var tr = ReplayTrack.Build(r);
        var at = new ReplayPoint[tr.Entities.Count];
        var mt = tr.Index[1];
        tr.At(0.25, at);
        Assert.True(at[mt].Here);
        Assert.Equal(102.5, at[mt].X, 3);
        Assert.Equal(MathF.PI / 8, at[mt].Rot, 3);
        // 9 s between samples is being away: gone a sample and a half after the last.
        tr.At(5, at);
        Assert.False(at[mt].Here);
        tr.At(10, at);
        Assert.True(at[mt].Here);
        Assert.Equal(2, tr.Rate);
    }

    // ---------------------------------------------------------------------------------------------
    // Casts on the board.

    private static void Flare(Recording r, ShapeOver shape = ShapeOver.Circle, int target = 0, bool stopped = false)
    {
        r.Actions[7] = shape switch
        {
            ShapeOver.Raidwide => new ActionInfo("Explosion", 2, 6, 0, "", 4),
            _ => new ActionInfo("Flare", 2, 6, 0, "", 4),
        };
        r.Events.Add(new CastEvent(10, 101, shape == ShapeOver.Raidwide ? 14347u : 7u, 4, 100, 100, 0, target, 105, 100));
        if (shape == ShapeOver.Raidwide) r.Actions[14347] = new ActionInfo("Explosion", 2, 6, 0, "", 4);
        if (stopped) r.Events.Add(new CancelEvent(12, 101, 7, true, 1, 7538));
    }

    private enum ShapeOver
    {
        Circle,
        Raidwide,
    }

    [Fact]
    public void Fills_as_it_casts_goes_off_lingers_and_is_gone()
    {
        Assert.Equal(-1, ReplayScene.CastProgress(10, 14, false, 9));
        Assert.Equal(0.5f, ReplayScene.CastProgress(10, 14, false, 12));
        Assert.Equal(1, ReplayScene.CastProgress(10, 14, false, 14.1));
        Assert.Equal(-1, ReplayScene.CastProgress(10, 14, false, 14 + ReplayScene.Linger + 0.01));
        // Stopped: gone as it stops, no flash.
        Assert.Equal(-1, ReplayScene.CastProgress(10, 14, true, 14.1));
    }

    [Fact]
    public void Draws_a_circle_where_it_was_aimed_by_brightness_only()
    {
        var r = Pull(p => Flare(p));
        var mid = Scene(r, 12).Teles.Single();
        Assert.Equal((BoardKind.Circle, 50f, 0f, 60f), (mid.Kind, mid.X, mid.Y, mid.Size));
        var full = Scene(r, 14.2).Teles.Single();
        Assert.True(mid.Bright < full.Bright);
        // Nothing grows; with reduced motion it's full from the start.
        Assert.Equal(mid.Size, full.Size);
        Assert.Equal(1, Scene(r, 12, still: true).Teles.Single().Bright);
        Assert.Empty(Scene(r, 15).Teles);
    }

    [Fact]
    public void Puts_a_stack_on_whom_its_on_following_them()
    {
        // Cast at H2; its hit on both of them at once says it's a stack.
        var r = Pull(p =>
        {
            Flare(p, target: 2);
            p.Events.Add(new HitEvent(14, 101, 7, 100, 100, 0, 96, 104, new[] { 1, 2 }));
        });
        var t = Scene(r, 12).Teles.Single();
        Assert.Equal((BoardKind.Stack, -40f, 40f), (t.Kind, t.X, t.Y));
    }

    [Fact]
    public void Draws_nothing_on_the_floor_for_a_raidwide_and_stops_with_its_cast()
    {
        Assert.Empty(Scene(Pull(p => Flare(p, ShapeOver.Raidwide)), 12).Teles);
        var stopped = Pull(p => Flare(p, stopped: true));
        Assert.Single(Scene(stopped, 11).Teles);
        Assert.Empty(Scene(stopped, 12.1).Teles);
        var tr = ReplayTrack.Build(stopped);
        Assert.Equal((CastStop.Interrupted, 12.0), (tr.Casts[0].Stopped, tr.Casts[0].End));
    }

    [Fact]
    public void Takes_a_cast_seen_twice_as_one()
    {
        var r = Pull(p =>
        {
            Flare(p);
            p.Events.Add(new CastEvent(10.3, 101, 7, 4, 100, 100, 0, 0, 105, 100));
        });
        Assert.Single(ReplayTrack.Build(r).Casts);
    }

    [Fact]
    public void Draws_a_hit_that_came_with_no_cast_as_it_lands_the_way_it_faced()
    {
        var r = Pull(p =>
        {
            p.Actions[9] = new ActionInfo("Exaflare", 3, 10, 0, "gl_fan90_1bf", 0);
            p.Events.Add(new HitEvent(20, 150, 9, 110, 100, MathF.PI / 2, 0, 0, Array.Empty<int>()));
        });
        var tr = ReplayTrack.Build(r);
        Assert.True(tr.Hits[0].Loose);
        var t = Scene(r, 20.1).Teles.Single();
        Assert.Equal((BoardKind.Cone, 100f, 0f, 90f, 90f), (t.Kind, t.X, t.Y, t.Rot, t.Angle));
        Assert.Empty(Scene(r, 20 + ReplayScene.Linger + 0.01).Teles);
        Assert.Empty(Scene(r, 19.9).Teles);
    }

    [Fact]
    public void A_hit_from_its_cast_isnt_loose_and_points_the_cast_its_way()
    {
        var r = Pull(p =>
        {
            p.Actions[9] = new ActionInfo("Cleave", 3, 10, 0, "gl_fan90_1bf", 4);
            p.Events.Add(new CastEvent(10, 101, 9, 4, 100, 100, 0, 0, null, null));
            p.Events.Add(new HitEvent(14.2, 101, 9, 100, 100, MathF.PI, 100, 100, new[] { 1 }));
        });
        var tr = ReplayTrack.Build(r);
        Assert.False(tr.Hits[0].Loose);
        Assert.Equal(MathF.PI, tr.Casts[0].Rot, 3);
    }

    [Fact]
    public void A_floor_line_that_hit_someone_behind_its_caster_runs_through_it()
    {
        // The site's throughLines: MT stands behind the helper (it faces south, MT is north of it), H2 in front.
        Recording Line(params int[] hit) => Pull(p =>
        {
            p.Actions[9] = new ActionInfo("Fortune-blade Sigil", 12, 40, 8, "", 3);
            p.Events.Add(new CastEvent(10, 150, 9, 3, 100, 100, 0, 0, null, null));
            p.Events.Add(new HitEvent(13.1, 150, 9, 100, 100, 0, 100, 100, hit));
        });
        var through = ReplayTrack.Build(Line(1));
        Assert.Equal((20f, 20f, false), (through.Casts[0].Shape!.Front, through.Casts[0].Shape!.Back, through.Casts[0].Shape!.Hitbox));
        var t = new ReplayScene(through, Frame);
        t.At(12);
        var tele = t.Teles.Single();
        Assert.Equal((BoardKind.Rect, 0f, 0f, 400f), (tele.Kind, tele.X, tele.Y, tele.Length));
        // MT behind and untouched: it only goes forward.
        var forward = ReplayTrack.Build(Line(2)).Casts[0].Shape!;
        Assert.Equal((40f, 0f), (forward.Front, forward.Back));
    }

    [Fact]
    public void Turns_a_casts_telegraph_with_its_caster()
    {
        var r = Pull(p =>
        {
            p.Actions[9] = new ActionInfo("Sweep", 3, 10, 0, "gl_fan90_1bf", 4);
            p.Events.Add(new CastEvent(10, 101, 9, 4, 100, 100, 0, 0, null, null));
            p.Events.Add(new TurnEvent(11, 101, MathF.PI / 2, 100, 100));
        });
        var at = Scene(r, 11.5).Teles.Single();
        Assert.Equal(90, at.Rot, 1);
        Assert.Equal(180, Scene(r, 10.5).Teles.Single().Rot, 1);
    }

    [Fact]
    public void Knows_whos_dead_raised_and_whom_each_enemy_is_on()
    {
        var r = Pull(p =>
        {
            p.Events.Add(new DeathEvent(20, 2, 7, 101));
            p.Events.Add(new RaiseEvent(30, 2, 1));
            p.Events.Add(new AggroEvent(0, 101, 1));
            p.Events.Add(new AggroEvent(25, 101, 2));
        });
        var sc = Scene(r, 22);
        var h2 = sc.Track.Index[2];
        var boss = sc.Track.Index[101];
        Assert.Equal(20, sc.DeadSince[h2]);
        Assert.Equal(1, sc.TargetOf[boss]);
        sc.At(30.5);
        Assert.True(double.IsNaN(sc.DeadSince[h2]));
        Assert.Equal(0.5, sc.Raised[h2], 3);
        Assert.Equal(2, sc.TargetOf[boss]);
    }

    // ---------------------------------------------------------------------------------------------
    // Head markers.

    private static ReplayHit Hit(double t = 12, MechRole role = MechRole.Spread, ShapeKind kind = ShapeKind.Circle, int on = 1, string name = "Serpent Descending",
        bool loose = true, float hx = 0, float hz = 0) =>
        new()
        {
            T = t,
            Src = 150,
            Action = 9,
            Name = name,
            Shape = new ReplayShape(kind, R: 5, Role: role),
            Loose = loose,
            Hx = hx,
            Hz = hz,
            Targets = new[] { new HitTarget(on, 100, 100, 1, 0, true) },
        };

    private static readonly IReadOnlySet<int> Party = new HashSet<int> { 1, 2 };

    [Fact]
    public void Links_a_marker_to_the_next_mechanic_on_the_one_marked()
    {
        var ms = new List<ReplayMarker> { new() { T = 10, On = 1, Icon = 139 } };
        ReplayTrack.LinkMarkers(ms, new[] { Hit() }, Array.Empty<ReplayCast>(), Party);
        Assert.Equal((0, 12.0, MechRole.Spread), (ms[0].Hit, ms[0].Until, ms[0].Role));
    }

    [Fact]
    public void Never_to_a_raidwide_a_knockback_an_auto_someone_else_or_too_late()
    {
        var ms = new List<ReplayMarker> { new() { T = 10, On = 1, Icon = 139 } };
        ReplayTrack.LinkMarkers(ms, new[]
        {
            Hit(role: MechRole.Raidwide, kind: ShapeKind.Raidwide),
            Hit(role: MechRole.Knockback, kind: ShapeKind.Knockback),
            Hit(name: "Attack", kind: ShapeKind.Target, role: MechRole.Tankbuster),
            Hit(on: 2),
            Hit(t: 23),
        }, Array.Empty<ReplayCast>(), Party);
        Assert.Equal(-1, ms[0].Hit);
    }

    [Fact]
    public void A_puddle_where_they_stood_a_limit_cut_further_never_past_their_next_marker()
    {
        var puddle = new List<ReplayMarker> { new() { T = 10, On = 1, Icon = 139 } };
        ReplayTrack.LinkMarkers(puddle, new[] { Hit(role: MechRole.None, hx: 100.5f, hz: 100) }, Array.Empty<ReplayCast>(), Party);
        Assert.Equal(0, puddle[0].Hit);
        var two = new List<ReplayMarker> { new() { T = 10, On = 1, Icon = 139 }, new() { T = 11, On = 1, Icon = 139 } };
        ReplayTrack.LinkMarkers(two, new[] { Hit() }, Array.Empty<ReplayCast>(), Party);
        Assert.Equal((-1, 0), (two[0].Hit, two[1].Hit));
        var cut = new List<ReplayMarker> { new() { T = 10, On = 1, Icon = 79, Meaning = MarkerMeaning.LimitCut, Number = 1 } };
        ReplayTrack.LinkMarkers(cut, new[] { Hit(t: 35) }, Array.Empty<ReplayCast>(), Party);
        Assert.Equal(0, cut[0].Hit);
        Assert.Equal(1, ReplayTrack.LimitCutNumber(79));
        Assert.Equal(8, ReplayTrack.LimitCutNumber(86));
        Assert.Equal(0, ReplayTrack.LimitCutNumber(87));
    }

    [Fact]
    public void Draws_the_mechanic_on_the_one_marked_and_keeps_a_badge_only_for_a_limit_cut()
    {
        var r = Pull(p =>
        {
            p.Actions[9] = new ActionInfo("Serpent Descending", 2, 5, 0, "", 0);
            p.Events.Add(new MarkerEvent(10, 2, 139));
            p.Events.Add(new MarkerEvent(10, 1, 80));
            // Two hits, one each, where they stood: a spread.
            p.Events.Add(new HitEvent(14, 150, 9, 100, 100, 0, 96, 104, new[] { 2 }, Px: new[] { 96f }, Pz: new[] { 104f }));
            p.Events.Add(new HitEvent(14, 150, 9, 100, 100, 0, 100, 95, new[] { 1 }, Px: new[] { 100f }, Pz: new[] { 95f }));
        });
        var sc = Scene(r, 12);
        Assert.Equal(2, sc.Teles.Count(t => t.Kind == BoardKind.Spread));
        Assert.Contains(sc.Teles, t => t.Kind == BoardKind.Spread && t.X == -40 && t.Y == 40);
        var badge = Assert.Single(sc.Badges);
        Assert.Equal((MarkerMeaning.LimitCut, 2), (badge.Kind, badge.Number));
        sc.At(15.1);
        Assert.Empty(sc.Badges);
    }

    [Fact]
    public void Says_a_rotation_markers_way_round_and_finds_rotations_by_their_steps()
    {
        Assert.Equal(Spin.Cw, ReplayRotations.IconSpin(167));
        Assert.Equal(Spin.Ccw, ReplayRotations.IconSpin(168));
        Assert.Equal(Spin.None, ReplayRotations.IconSpin(1));
        var r = Pull(p =>
        {
            p.Actions[9] = new ActionInfo("Spiral", 3, 20, 0, "gl_fan30_1bf", 0);
            for (var i = 0; i < 5; i++) p.Events.Add(new HitEvent(20 + i, 150, 9, 100, 100, -i * 0.5f, 0, 0, Array.Empty<int>()));
        });
        var rot = Assert.Single(ReplayTrack.Build(r).Rotations);
        Assert.Equal((Spin.Cw, 5, 20.0, 24.0), (rot.Dir, rot.Steps, rot.T, rot.End));
        Assert.Contains(Scene(r, 22).Teles, t => t.Kind == BoardKind.Rotation);
    }

    // ---------------------------------------------------------------------------------------------
    // The fight's statuses.

    private static Recording WithStatuses(params (int On, uint Id, string Name, int Cat, double Start, double Dur)[] list) =>
        Pull(p =>
        {
            foreach (var (on, id, name, cat, start, dur) in list)
            {
                p.Statuses[id] = new StatusInfo(name, 210000 + id, cat);
                p.Events.Add(new StatusEvent(start, on, id, 101, (float)dur, true));
            }
        });

    [Fact]
    public void Keeps_a_members_fight_debuffs_and_an_enemys_own_buffs_never_nameless_ones()
    {
        var r = WithStatuses((1, 1, "Doom", 2, 5, 10), (1, 2, "Well Fed", 1, 5, 10), (101, 3, "Damage Up", 1, 5, 10), (101, 4, "Status 2273", 0, 5, 10),
            (2, 5, "Prey", 2, 6, 3));
        r.Statuses.Remove(4);
        var sc = Scene(r, 7);
        var tr = sc.Track;
        Assert.Equal(new[] { "Doom" }, sc.StatusesOn[tr.Index[1]].Select(s => s.Name));
        Assert.Equal(new[] { "Damage Up" }, sc.StatusesOn[tr.Index[101]].Select(s => s.Name));
        var prey = sc.StatusesOn[tr.Index[2]].Single();
        Assert.Equal((2f, 2 / 3f), (prey.Left, prey.Share));
        // Gone, or not yet.
        sc.At(4);
        Assert.Empty(sc.StatusesOn[tr.Index[1]]);
        sc.At(16);
        Assert.Empty(sc.StatusesOn[tr.Index[1]]);
    }

    [Fact]
    public void Before_plugin_04_a_members_debuffs_are_all_but_their_food()
    {
        var r = WithStatuses((1, 1, "Doom", 0, 5, 10), (1, 2, "Well Fed", 0, 5, 10));
        var sc = Scene(r, 7);
        Assert.Equal(new[] { "Doom" }, sc.StatusesOn[sc.Track.Index[1]].Select(s => s.Name));
    }

    [Fact]
    public void A_status_lasting_to_the_end_has_no_time_left()
    {
        var sc = Scene(WithStatuses((101, 3, "Invincibility", 1, 5, 0)), 7);
        var s = sc.StatusesOn[sc.Track.Index[101]].Single();
        Assert.True(float.IsNaN(s.Left));
        Assert.True(float.IsNaN(s.Share));
    }

    [Fact]
    public void A_bosss_refresh_or_restack_is_one_span_on_the_lane()
    {
        var tr = ReplayTrack.Build(WithStatuses((101, 3, "Damage Up", 1, 5, 4), (101, 3, "Damage Up", 1, 9.2, 4), (101, 4, "Status 9", 1, 5, 4),
            (101, 3, "Damage Up", 1, 30, 4)));
        var lane = ReplayStatuses.BossBuffLane(tr);
        Assert.Equal(new[] { (5.0, 13.2), (30.0, 34.0) }, lane.Select(b => (b.Start, Math.Round(b.End, 3))));
        Assert.Equal("The Test Boss", lane[0].Who);
    }

    [Fact]
    public void Knows_a_family_by_its_name_with_the_number_left_out()
    {
        Assert.Equal(("# in Line", 2), ReplayStatuses.DebuffFamily("Second in Line"));
        Assert.Equal(("Limit Cut #", 3), ReplayStatuses.DebuffFamily("Limit Cut 3"));
        Assert.Equal(("Phase #", 4), ReplayStatuses.DebuffFamily("Phase IV"));
        Assert.Equal(("Prey", 0), ReplayStatuses.DebuffFamily("Prey"));
        Assert.Equal(("First", 0), ReplayStatuses.DebuffFamily("First"));
    }

    [Fact]
    public void Makes_first_to_fourth_in_line_given_at_once_one_wave()
    {
        var tr = ReplayTrack.Build(WithStatuses((1, 3005, "Second in Line", 2, 10, 30), (2, 3004, "First in Line", 2, 10, 20),
            (1, 3007, "Fourth in Line", 2, 10.5, 49.5), (2, 3006, "Third in Line", 2, 11, 39)));
        var w = Assert.Single(ReplayStatuses.PartyDebuffLane(tr));
        Assert.Equal(("First to Fourth in Line", 3004u, 10.0, 60.0), (w.Name, w.Id, w.Start, w.End));
        Assert.Equal(new[] { "Ayla Fenn", "Rook Varo" }, w.Who);
        Assert.Equal(new[] { "First in Line", "Second in Line", "Third in Line", "Fourth in Line" }, w.Parts.Select(p => p.Name));
        var two = ReplayTrack.Build(WithStatuses((1, 3004, "First in Line", 2, 10, 20), (2, 3005, "Second in Line", 2, 10, 20)));
        Assert.Equal("First and Second in Line", ReplayStatuses.PartyDebuffLane(two)[0].Name);
    }

    [Fact]
    public void Leaves_off_the_lanes_buffs_nameless_ones_and_a_raises_weakness()
    {
        var tr = ReplayTrack.Build(WithStatuses((1, 1, "Regen", 1, 5, 4), (1, 43, "Weakness", 2, 30, 60), (1, 4, "Status 9", 2, 5, 4)));
        Assert.Empty(ReplayStatuses.PartyDebuffLane(tr));
        var lanes = ReplayStatuses.MemberDebuffLanes(tr);
        Assert.Equal(new[] { "Ayla Fenn", "Rook Varo" }, lanes.Select(l => l.Name));
        Assert.All(lanes, l => Assert.Empty(l.Spans));
    }

    // ---------------------------------------------------------------------------------------------
    // The dock.

    [Fact]
    public void The_dock_is_slimmer_with_no_lanes_and_taller_a_row_at_a_time()
    {
        var bare = ReplayLanes.LanesPx(0, 0);
        Assert.Equal(ReplayLanes.Frame + ReplayLanes.Phases + ReplayLanes.Steps + ReplayLanes.Ruler + 2 * ReplayLanes.Gap, bare);
        Assert.Equal(bare + ReplayLanes.Gap + ReplayLanes.Row, ReplayLanes.LanesPx(1, 0));
        Assert.Equal(bare + ReplayLanes.Gap + 2 * ReplayLanes.Row + ReplayLanes.RowGap, ReplayLanes.LanesPx(2, 0));
        Assert.Equal(bare + ReplayLanes.Gap + ReplayLanes.Row, ReplayLanes.LanesPx(0, 1));
        Assert.True(ReplayLanes.LanesPx(2, 2) > ReplayLanes.LanesPx(2, 1));
        Assert.Equal(bare + 24, ReplayLanes.DockPx(0, 0));
    }

    [Fact]
    public void The_ruler_labels_every_30_s_further_apart_when_narrow()
    {
        Assert.Equal(new double[] { 0, 30, 60, 90, 120 }, ReplayLanes.RulerTimes(120, 0));
        Assert.Equal(new double[] { 0, 60, 120 }, ReplayLanes.RulerTimes(120, 150));
        Assert.Equal(120, ReplayLanes.RulerTimes(700, 0)[1]);
    }

    private static (float, float, string, string) It(string key, float from, float to) => (from, to, key, key);

    [Fact]
    public void Packs_bars_as_drawn()
    {
        var (bars, rows) = ReplayLanes.Pack(new[] { It("a", 0, 10), It("b", 30, 40), It("c", 60, 200) }, 2);
        Assert.Equal(1, rows);
        Assert.All(bars, b => Assert.Equal(0, b.Row));
        // A short status is still its icon wide.
        Assert.Equal(2, ReplayLanes.Pack(new[] { It("a", 0, 2), It("b", 10, 30) }, 2).Rows);
        // No row left: it joins the bar on the row free soonest.
        var crowd = ReplayLanes.Pack(new[] { It("a", 0, 300), It("b", 5, 100), It("c", 10, 400), It("d", 50, 120) }, 2);
        Assert.Equal(2, crowd.Rows);
        Assert.Equal(new[] { ("a,d", 0, 300f), ("b,c", 1, 400f) }, crowd.Bars.Select(b => (string.Join(",", b.Items), b.Row, b.Right)));
        Assert.Equal(22, ReplayLanes.BarMinPx(1));
        Assert.Equal(66, ReplayLanes.BarMinPx(3));
        Assert.Equal(110, ReplayLanes.BarMinPx(7));
    }

    [Fact]
    public void Steps_are_the_mechanics_together_never_across_phases()
    {
        var kinds = new[] { MechanicKind.Circle };
        var entries = new List<TimelineEntry>
        {
            new(10, "Flare", 1, kinds), new(11, "Holy", 1, kinds), new(12, "Quake", 1, kinds), new(30, "Flare", 1, kinds), new(70, "Ruin", 1, kinds),
        };
        var steps = ReplayLanes.StepsOf(entries, new List<double> { 0, 60 });
        Assert.Equal(new[] { "Pull", "Flare + Holy + 1 more", "Flare", "P2 · Ruin" }, steps.Select(s => s.Name));
        Assert.Equal(new[] { 0.0, 10, 30, 70 }, steps.Select(s => s.Start));
        Assert.Equal(new[] { 10.0, 30, 70 }, steps.Take(3).Select(s => s.End));
        // The last runs on for the steps' usual length (23 s here), 8 to 30.
        Assert.Equal(93, steps[^1].End);
        Assert.Equal(new[] { ("P1", 0.0), ("P2", 70.0) }, ReplayLanes.PhasesOf(steps).Select(p => (p.Name, p.Start)));
        Assert.Equal(1, ReplayLanes.StepAt(steps, 9.6));
        Assert.Equal(0, ReplayLanes.StepAt(steps, 9.4));
        Assert.Empty(ReplayLanes.PhasesOf(ReplayLanes.StepsOf(entries.Take(3).ToList(), new List<double> { 0 })));
    }

    // ---------------------------------------------------------------------------------------------
    // The whole pull.

    [Fact]
    public void A_pull_has_its_cards_its_end_and_its_events()
    {
        var r = Pull(p =>
        {
            Flare(p, target: 2);
            p.Events.Add(new HitEvent(14, 101, 7, 100, 100, 0, 96, 104, new[] { 1, 2 }, Damage: new long[] { 40_000, 30_000 }));
            p.Events.Add(new DeathEvent(20, 2, 7, 101));
            p.Events.Add(new DeathEvent(58, 1, 7, 101));
            p.Events.Add(new MarkerEvent(30, 1, 139));
            p.Events.Add(new MarkerEvent(30, 2, 139));
        });
        var m = ReplayModel.Build(r);
        Assert.Equal("The Test Boss", Assert.Single(m.Cards).Name);
        Assert.False(m.OtherCasts);
        Assert.Equal(2, m.Deaths.Count);
        Assert.Equal("Rook Varo died at 0:20", m.Deaths[0].Words);
        Assert.Equal("Wiped", m.EndWord);
        Assert.Equal("Pull 7 · at Flare · the boss at 50%", m.EndWords);
        Assert.Equal(58, m.FoughtFor);
        var texts = m.Log.Select(l => l.Text).ToList();
        Assert.Contains("Flare hit Ayla Fenn, Rook Varo for 70k", texts);
        Assert.Contains("Rook Varo died to Flare", texts);
        Assert.Contains("Ayla Fenn went down in the wipe", texts);
        Assert.Contains("Head markers over Ayla Fenn, Rook Varo", texts);
        // Tanks first, then healers.
        Assert.Equal(new[] { "MT", "H2" }, m.PartyOrder.Select(i => m.Track.Entities[i].Slot));
        Assert.True(m.HasHp && m.HasPlaces);
    }

    [Fact]
    public void Says_the_end_as_the_site_does()
    {
        Assert.Equal("Pull 14 · nobody died", ReplayModel.EndCardWords(Outcome.Clear, "Pull 14", null, 0, 0));
        Assert.Equal("Pull 14 · 2 deaths", ReplayModel.EndCardWords(Outcome.Clear, "Pull 14", null, 0, 2));
        Assert.Equal("Pull 12 · at Knockback Wave · the boss at 38%", ReplayModel.EndCardWords(Outcome.Wipe, "Pull 12", "Knockback Wave", 38.2, 3));
        Assert.Equal("Pull 3", ReplayModel.EndCardWords(Outcome.Ended, "Pull 3", null, 50, 0));
        Assert.Equal("4.5 s", ReplayModel.Secs(4.5));
        Assert.Equal("12 s", ReplayModel.Secs(12.2));
        Assert.Equal("1:05", ReplayModel.Secs(65));
    }

    [Fact]
    public void The_sample_pull_replays_from_start_to_end()
    {
        var r = RecordingTests.Sample();
        var m = ReplayModel.Build(r);
        Assert.NotEmpty(m.Steps);
        Assert.Equal("Pull", m.Steps[0].Name);
        var sc = new ReplayScene(m.Track, m.Frame);
        for (var t = 0.0; t <= m.Track.End; t += 0.25) sc.At(t);
        Assert.True(m.Total >= m.Track.End);
    }

    [Fact]
    public void The_arena_is_worked_out_from_the_fight()
    {
        var a = ReplayTrack.Build(Pull()).Arena;
        Assert.Equal((100f, 100f), (a.Cx, a.Cz));
        Assert.InRange(a.Width, 20, 80);
        var plan = a.ToPlan();
        Assert.Equal(new ArenaCenter(100, 100), plan.Center);
        Assert.Equal(840f / plan.Yalms, BoardFrame.Of(plan, a).Upy);
    }
}
