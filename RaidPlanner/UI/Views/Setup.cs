using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using RaidPlanner.Core;
using RaidPlanner.UI.Model;
using static RaidPlanner.UI.Theme;

namespace RaidPlanner.UI;

/// <summary>
/// The first-time setup guide (0.8.0, the mock-ups' plug-setup): five steps in the main window, no tabs; the progress
/// (five segments, the steps' names under them) at the top, Back (quiet) and the step's one button at the foot.
/// Welcome (what the plugin does; Skip keeps the recommended setup), Features (a quick start, then a card and a switch
/// per main part, changed at once), Account (the three numbered steps to link, what's shared; Do this later), Look
/// (the meter's size and three themes, where the windows go, Arrange them on my screen), Done (what's on, the samples
/// to try, Finish). Nothing in it is bound to a key: typing the token is the only keyboard use.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The guide's step (0–4).</summary>
    private int setupStep;

    private static readonly string[] SetupBodies = { "##setup0", "##setup1", "##setup2", "##setup3", "##setup4" };

    private static readonly string[] PresetItems = { "Recommended", "Everything", "Recording only" };

    private static readonly FeaturePreset[] PresetOrder = { FeaturePreset.Recommended, FeaturePreset.Everything, FeaturePreset.RecordingOnly };

    /// <summary>The three themes the guide offers (the rest are in Settings).</summary>
    private static readonly MeterThemeId[] GuideThemes = { MeterThemeId.ReadyCheck, MeterThemeId.Readout, MeterThemeId.Grimoire };

    private static readonly string[] FeatureSwitchIds = { "##fs0", "##fs1", "##fs2", "##fs3", "##fs4", "##fs5" };

    private void DrawSetup()
    {
        setupStep = SetupGuide.Clamp(setupStep);
        Frame.BeginHeader(18);
        Kit.StepsBar(SetupGuide.Steps, setupStep);
        Frame.EndHeader();

        Frame.BeginBody(SetupBodies[setupStep], null, 22);
        switch (setupStep)
        {
            case SetupGuide.Welcome:
                SetupWelcome();
                break;
            case SetupGuide.FeaturesStep:
                SetupFeatures();
                break;
            case SetupGuide.Account:
                SetupAccount();
                break;
            case SetupGuide.Look:
                SetupLook();
                break;
            default:
                SetupDone();
                break;
        }
        Frame.BeginBottom("##setup-foot", 18);
        SetupFoot();
        Frame.EndBottom();
        Frame.EndBody();
    }

    /// <summary>Turns the main parts on and off as picked, and saves; Upload my logs as its own switch does (from now on).</summary>
    private void ApplyFeatures(FeatureSet next)
    {
        var wasUpload = config.UploadLogs;
        config.SetFeatures(next);
        if (next.Upload != wasUpload) logs.SetOn(next.Upload);
        else config.Save();
    }

    /// <summary>Done (finished or skipped): the guide won't open again by itself, and Home opens.</summary>
    private void FinishSetup()
    {
        MarkSetupDone();
        // The samples its last step showed stop with it.
        if (CalloutsSampleOn?.Invoke() == true) ToggleCalloutsSample?.Invoke();
        if (SampleOn?.Invoke() == true) ToggleSample?.Invoke();
        setupStep = 0;
        screen = Screen.Home;
    }

    /// <summary>The guide won't open again by itself (finished, skipped, closed, or left for another screen).</summary>
    private void MarkSetupDone()
    {
        if (config.Setup == SetupState.Done) return;
        config.Setup = SetupState.Done;
        config.Save();
    }

    // ---------------------------------------------------------------------------------------------
    // The foot: Back and the step's button.

    private void SetupFoot()
    {
        var at = Kit.At;
        var w = Kit.Width;
        var dl = Kit.Dl;
        Kit.Rule(dl, at.X, at.X + w, at.Y);
        Kit.Reserve(at, w, S(14));
        var row = Kit.At;
        var h = Kit.ButtonHeight();
        var set = config.FeaturesNow();
        if (setupStep == SetupGuide.Welcome)
        {
            if (Kit.Button("##setup-skip", row, "Skip: keep the recommended setup", Btn.Quiet, BtnSize.Md, null, 0, false,
                    "Records, uploads once you link your account, the meter and your plan on screen. All of it can be changed in Settings."))
                Later(() =>
                {
                    // A fresh install's switches are already the recommended ones; run again from Settings, Skip leaves
                    // yours as they are (it would otherwise turn uploads back on, which are public).
                    if (SetupState.Due(config.Setup)) ApplyFeatures(Features.Preset(FeaturePreset.Recommended));
                    FinishSetup();
                });
            const string go = "Set it up";
            var gw = Kit.ButtonWidth(go, BtnSize.Md, null, Btn.Tele);
            if (Kit.Button("##setup-go", new Vector2(row.X + w - gw, row.Y), go, Btn.Tele)) setupStep++;
            Kit.Reserve(row, w, h);
            return;
        }

        if (Kit.Button("##setup-back", row, "Back", Btn.Quiet, BtnSize.Md, Icon.ChevronLeft)) setupStep--;
        var last = setupStep == SetupGuide.Done;
        var next = last ? "Finish" : "Next";
        var nextIcon = last ? Icon.Check : (Icon?)null;
        var nw = Kit.ButtonWidth(next, BtnSize.Md, nextIcon, Btn.Tele);
        var can = SetupGuide.CanGoOn(setupStep, set, Linked);
        var right = row.X + w - nw;
        if (Kit.Button("##setup-next", new Vector2(right, row.Y), next, Btn.Tele, BtnSize.Md, nextIcon, 0, !can, can ? null : "Link your account first, or do it later"))
        {
            if (last) Later(FinishSetup);
            else setupStep++;
        }
        right -= S(10);
        if (setupStep == SetupGuide.FeaturesStep)
        {
            var count = SetupGuide.CountLine(set);
            Txt.LineRight(dl, right, row.Y, count, Face.Sans, 12, Palette.Faint, H);
        }
        else if (setupStep == SetupGuide.Account && !can)
        {
            const string later = "Do this later";
            var lw = Kit.ButtonWidth(later, BtnSize.Md, null, Btn.Quiet);
            if (Kit.Button("##setup-later", new Vector2(right - lw, row.Y), later, Btn.Quiet, BtnSize.Md, null, 0, false,
                    "Uploads wait until you link it: Settings › Account, any time"))
                setupStep++;
        }
        Kit.Reserve(row, w, h);
    }

    // ---------------------------------------------------------------------------------------------
    // Shared bits.

    /// <summary>A step's title (a callout at 22) and its lead under it.</summary>
    private static void SetupHead(string title, IReadOnlyList<Run> lead, float gap = 8)
    {
        var at = Kit.At;
        Kit.Reserve(at, Kit.Width, Kit.Callout(Kit.Dl, at, title, 22, Kit.Width, 2, 1.05f));
        Kit.Gap(gap);
        Txt.Para(lead, 19.5f);
    }

    private static Run[] Lead(string text, int px = 13) => new[] { new Run(text, Face.Sans, px, Palette.Muted) };

    /// <summary>A sentence with its names standing out (white 600), the rest muted.</summary>
    private static Run[] LeadOf(IReadOnlyList<Said> said)
    {
        var runs = new Run[said.Count];
        for (var i = 0; i < said.Count; i++)
            runs[i] = said[i].Strong ? new Run(said[i].Text, Face.SansSemibold, 13, Palette.White) : new Run(said[i].Text, Face.Sans, 13, Palette.Muted);
        return runs;
    }

    /// <summary>An eyebrow in muted mono capitals at the cursor.</summary>
    private static void SetupEyebrow(string text, Vector4? color = null)
    {
        var at = Kit.At;
        Kit.Eyebrow(Kit.Dl, at, text, null, Kit.Width, color ?? Palette.Muted);
        Kit.Reserve(at, Kit.Width, Kit.EyebrowH);
    }

    /// <summary>A box at the cursor (the well with a hairline) with an icon and words in it, wrapped; returns nothing, takes its room.</summary>
    private static void InfoBox(Icon icon, IReadOnlyList<Run> runs)
    {
        var at = Kit.At;
        var w = Kit.Width;
        var dl = Kit.Dl;
        var padX = S(14);
        var padY = S(12);
        var tw = w - padX * 2 - S(16) - S(10);
        var th = Txt.Flow(Vector2.Zero, tw, runs, 18.5f, out _, draw: false);
        var h = padY * 2 + MathF.Max(th, S(18.5f));
        Kit.Box(dl, at, at + new Vector2(w, h), Palette.Well, Palette.Line, Palette.RadiusMd);
        Icons.Draw(dl, icon, new Vector2(at.X + padX, at.Y + padY + (S(18.5f) - S(16)) / 2), S(16), Palette.Muted);
        Txt.Flow(new Vector2(at.X + padX + S(16) + S(10), at.Y + padY), tw, runs, 18.5f, out _);
        Kit.Reserve(at, w, h);
    }

    // ---------------------------------------------------------------------------------------------
    // 1 Welcome.

    private static readonly (Icon Icon, string Title, string Line)[] WelcomeLines =
    {
        (Icon.CircleDot, "It records each pull by itself", "Nothing to start or stop. Pulls are kept on this PC, even without an account."),
        (Icon.Upload, "Every pull, again, on the site", "Replay where everyone stood, see who died to what, and turn a pull into a plan."),
        (Icon.Map, "Your plan, step by step, in the fight", "The next mechanics and your own cooldowns on screen, with a sound if you like."),
    };

    private void SetupWelcome()
    {
        Kit.Gap(4);
        SetupEyebrow("First time here", Palette.Ember);
        Kit.Gap(10);
        SetupHead("Welcome to Raid Codex",
            Lead("The plugin keeps every pull of your raids, shows a damage meter while you fight, and puts your static’s plan on screen. Let’s set it up the way you like.", 14), 10);
        Kit.Gap(18);
        Kit.BeginPanel("##welcome-lines", 18, 18, 18, Palette.Slab, Palette.Line);
        for (var i = 0; i < WelcomeLines.Length; i++)
        {
            if (i > 0) Kit.Gap(18);
            var (icon, title, line) = WelcomeLines[i];
            var at = Kit.At;
            var w = Kit.Width;
            var dl = Kit.Dl;
            Kit.SoftWell(dl, at, 40, icon, Palette.EmberWash, Palette.Ember);
            var x = at.X + S(40) + S(14);
            var room = at.X + w - x;
            Txt.Line(dl, new Vector2(x, at.Y + S(2)), Txt.Fit(title, Face.SansSemibold, 14, room), Face.SansSemibold, 14, Palette.White, 19);
            var lh = Txt.Flow(new Vector2(x, at.Y + S(2) + S(19) + S(3)), room, Lead(line), 18.5f, out _);
            Kit.Reserve(at, w, MathF.Max(S(40), S(2) + S(19) + S(3) + lh));
        }
        Kit.EndPanel();
        Kit.Gap(18);
        InfoBox(Icon.Info, Lead("About a minute. Everything here can be changed later in Settings, and nothing is ever bound to a key."));
    }

    // ---------------------------------------------------------------------------------------------
    // 2 Features.

    private void SetupFeatures()
    {
        SetupHead("What should it do?", Lead("The recommended ones are on already. Turn on only what you’ll use: each can be switched later in Settings › Features."));
        Kit.Gap(18);
        var set = config.FeaturesNow();
        var preset = Features.PresetOf(set);
        var selected = preset is { } p ? Array.IndexOf(PresetOrder, p) : -1;
        var at = Kit.At;
        var w = Kit.Width;
        var dl = Kit.Dl;
        const string from = "Start from";
        var fw = Txt.Width(from, Face.SansMedium, 13);
        var segW = Kit.SegmentedWidth(PresetItems);
        var oneRow = fw + S(12) + segW <= w;
        Txt.Line(dl, at, from, Face.SansMedium, 13, Palette.Muted, HSm);
        var segAt = oneRow ? new Vector2(at.X + fw + S(12), at.Y) : new Vector2(at.X, at.Y + S(HSm) + S(4));
        var picked = Kit.Segmented("##setup-preset", segAt, PresetItems, selected);
        if (picked != selected && picked >= 0) ApplyFeatures(Features.Preset(PresetOrder[picked]));
        Kit.Reserve(at, w, oneRow ? S(HSm) : S(HSm) * 2 + S(4));
        Kit.Gap(12);
        set = config.FeaturesNow();
        for (var i = 0; i < Features.All.Count; i++)
        {
            if (i > 0) Kit.Gap(8);
            FeatureCard(i, Features.All[i], set);
        }
    }

    /// <summary>
    /// A main part's card: its well (ember on, dim off), its name with "Needs an account" (or "Account linked") for
    /// those that need one, its line, and its switch, which changes it at once.
    /// </summary>
    private void FeatureCard(int index, Feature f, FeatureSet set)
    {
        var on = set[f];
        var at = Kit.At;
        var w = Kit.Width;
        var dl = Kit.Dl;
        var padX = S(14);
        var padY = S(12);
        var x = at.X + padX + S(38) + S(14);
        var sw = Kit.SwitchSize;
        var room = at.X + w - padX - sw.X - S(14) - x;
        var name = Features.Name(f);
        var nameW = Txt.Width(name, Face.SansSemibold, 14);
        string? tag = Features.NeedsAccount(f) ? Linked ? "Account linked" : "Needs an account" : null;
        var tagW = tag != null ? Kit.ToneTagWidth(tag) : 0;
        var tagBelow = tag != null && nameW + S(8) + tagW > room;
        var lineRuns = new[] { new Run(Features.Line(f), Face.Sans, 13, on ? Palette.Muted : Palette.Faint) };
        var lineH = Txt.Flow(Vector2.Zero, room, lineRuns, 18, out _, draw: false);
        var wordsH = S(20) + (tagBelow ? S(24) : 0) + S(3) + lineH;
        var h = padY * 2 + MathF.Max(S(38), wordsH);
        Kit.Box(dl, at, at + new Vector2(w, h), on ? Palette.Slab : Palette.Well, on ? Palette.EmberLine : Palette.Line, 12);
        Kit.FeatureWell(dl, new Vector2(at.X + padX, at.Y + (h - S(38)) / 2), 38, f, on);
        var y = at.Y + (h - wordsH) / 2;
        Txt.Line(dl, new Vector2(x, y), Txt.Fit(name, Face.SansSemibold, 14, room), Face.SansSemibold, 14, on ? Palette.White : Palette.Muted, 20);
        if (tag != null)
        {
            var tp = tagBelow ? new Vector2(x, y + S(20) + S(2)) : new Vector2(x + nameW + S(8), y);
            Kit.ToneTag(dl, tp, tag, Linked ? Tone.Ready : Tone.Warn);
        }
        y += S(20) + (tagBelow ? S(24) : 0) + S(3);
        Txt.Flow(new Vector2(x, y), room, lineRuns, 18, out _);
        if (Kit.Switch(FeatureSwitchIds[index], new Vector2(at.X + w - padX - sw.X, at.Y + (h - sw.Y) / 2), on))
            ApplyFeatures(set.With(f, !on));
        Kit.Reserve(at, w, h);
    }

    // ---------------------------------------------------------------------------------------------
    // 3 Account.

    private static readonly string[] AccountSteps =
    {
        "Sign in on the site and open Settings › In-game plugin",
        "Copy the token it shows you",
        "Paste it here",
    };

    private void SetupAccount()
    {
        var set = config.FeaturesNow();
        SetupHead(Linked ? "Your account is linked" : "Link your account", Linked
            ? Lead("Pulls you send, and the picks that need an account, go to your Raid Codex.")
            : LeadOf(SetupGuide.AccountLead(set)));
        Kit.Gap(18);
        if (Linked) SetupLinked();
        else SetupLinkSteps();
        Kit.Gap(18);
        Kit.BeginPanel("##setup-shared", 14, 16, 14, Palette.Well, Palette.Line, 12);
        var at = Kit.At;
        var w = Kit.Width;
        var dl = Kit.Dl;
        Icons.Draw(dl, Icon.Eye, new Vector2(at.X, at.Y + (S(19) - S(15)) / 2), S(15), Palette.Muted);
        Txt.Line(dl, new Vector2(at.X + S(15) + S(8), at.Y), Txt.Fit("What’s shared, and with whom", Face.SansSemibold, 13, w - S(23)), Face.SansSemibold, 13, Palette.White, 19);
        Kit.Reserve(at, w, S(19));
        Kit.Gap(10);
        Txt.Para(Lead("Your pulls: the fight, where everyone stood, casts, damage and deaths, with the party’s names, jobs and home worlds. Never your chat."), 19.5f);
        Kit.Gap(10);
        Txt.Para(Lead("They’re public on the Raid Codex, as logs are on FF Logs. To keep your character’s name out, hide it on the site under Settings › Your logs."), 19.5f);
        Kit.EndPanel();
    }

    /// <summary>Linked: a ready tick, "Linked to your Raid Codex" and the token masked in mono.</summary>
    private void SetupLinked()
    {
        Kit.BeginPanel("##setup-linked", 16, 18, 16, Palette.Slab, Palette.ReadyLine);
        var at = Kit.At;
        var w = Kit.Width;
        var dl = Kit.Dl;
        Kit.Tick(dl, new Vector2(at.X + S(12), at.Y + S(20)), 24);
        var x = at.X + S(24) + S(14);
        Txt.Line(dl, new Vector2(x, at.Y), Txt.Fit("Linked to your Raid Codex", Face.SansSemibold, 14, at.X + w - x), Face.SansSemibold, 14, Palette.White, 20);
        Txt.Line(dl, new Vector2(x, at.Y + S(22)), SiteLink.Masked(config.SiteToken), Face.Mono, 12, Palette.Muted, 18);
        Kit.Reserve(at, w, S(40));
        Kit.EndPanel();
    }

    /// <summary>The three steps, numbered on waymarks: Get a token (the site's page), copy it, paste it here and Link. A bad token is said in red.</summary>
    private void SetupLinkSteps()
    {
        Kit.BeginPanel("##setup-link", 18, 18, 18, Palette.Slab, Palette.Line);
        var w = Kit.Width;
        var dl = Kit.Dl;
        for (var i = 0; i < AccountSteps.Length; i++)
        {
            if (i > 0) Kit.Gap(14);
            var at = Kit.At;
            var rowH = i == 0 ? S(HSm) : S(24);
            Kit.Waymark(dl, new Vector2(at.X, at.Y + (rowH - S(24)) / 2), i + 1);
            var x = at.X + S(24) + S(12);
            var room = at.X + w - x;
            if (i == 0)
            {
                const string get = "Get a token";
                var gw = Kit.ButtonWidth(get, BtnSize.Sm, Icon.ExternalLink);
                room -= gw + S(12);
                if (Kit.Button("##setup-get", new Vector2(at.X + w - gw, at.Y), get, Btn.Outline, BtnSize.Sm, Icon.ExternalLink, 0, false,
                        "Opens the site's Settings › In-game plugin, where a token is made (shown once, revoked there)."))
                    Dalamud.Utility.Util.OpenLink(SiteLink.TokensUrl);
            }
            Txt.Line(dl, new Vector2(x, at.Y), Txt.Fit(AccountSteps[i], Face.SansMedium, 13, room), Face.SansMedium, 13, Palette.White, rowH / S(1));
            Kit.Reserve(at, w, rowH);
        }
        Kit.Gap(8);
        var fp = Kit.At;
        var fx = fp.X + S(36);
        var inner = fp.X + w - fx;
        const string link = "Link";
        var linkW = Kit.ButtonWidth(link, BtnSize.Md, Icon.Link, Btn.On);
        var fieldW = MathF.Max(S(120), inner - linkW - S(8));
        var text = tokenDraft;
        if (Kit.Field("##setup-token", new Vector2(fx, fp.Y), fieldW, ref text, "rpp_…", Icon.Link, 128, ImGuiInputTextFlags.Password, tokenProblem != null, Face.Mono, 13))
        {
            tokenDraft = text;
            tokenProblem = null;
        }
        if (Kit.Button("##setup-link-btn", new Vector2(fx + fieldW + S(8), fp.Y), link, Btn.On, BtnSize.Md, Icon.Link, 0, tokenDraft.Trim().Length == 0))
            LinkToken();
        Kit.Reserve(fp, w, S(H));
        if (tokenProblem != null)
        {
            Kit.Gap(8);
            var ep = Kit.At;
            Icons.Draw(dl, Icon.CircleX, new Vector2(fx, ep.Y + (S(18) - S(Kit.IconPx)) / 2), S(Kit.IconPx), Palette.Danger);
            Txt.Line(dl, new Vector2(fx + S(Kit.IconPx + 6.4f), ep.Y), Txt.Fit(tokenProblem, Face.Sans, 13, inner - S(Kit.IconPx + 6.4f)), Face.Sans, 13, Palette.DangerSoft, 18);
            Kit.Reserve(ep, w, S(18));
        }
        Kit.EndPanel();
    }

    // ---------------------------------------------------------------------------------------------
    // 4 Look.

    private void SetupLook()
    {
        SetupHead("Make it yours", Lead("Pick how the meter looks. Where the windows sit you choose on your screen: the plugin shows them all at once to drag into place."));
        Kit.Gap(18);
        SetupEyebrow("The meter");
        Kit.Gap(10);
        var at = Kit.At;
        var w = Kit.Width;
        var dl = Kit.Dl;
        Txt.Line(dl, at, "Size", Face.SansMedium, 14, Palette.White, HSm);
        var segW = Kit.SegmentedWidth(SizeItems);
        var size = config.MeterCompact ? 0 : 1;
        var pickedSize = Kit.Segmented("##setup-size", new Vector2(at.X + w - segW, at.Y), SizeItems, size);
        if (pickedSize != size)
        {
            config.MeterCompact = pickedSize == 0;
            config.Save();
        }
        Kit.Reserve(at, w, S(HSm));
        Kit.Gap(10);
        ThemeTiles();
        Kit.Gap(10);
        var np = Kit.At;
        Txt.Line(dl, np, Txt.Fit("Three more themes in Settings › Damage meter.", Face.Sans, 12, w), Face.Sans, 12, Palette.Faint, 17);
        Kit.Reserve(np, w, S(17));
        Kit.Gap(18);
        SetupEyebrow("Where they go");
        Kit.Gap(10);
        var set = config.FeaturesNow();
        ScreenPicture(set);
        Kit.Gap(12);
        var row = Kit.At;
        var arranging = Arranging?.Invoke() == true;
        var label = arranging ? "Arranging on your screen…" : "Arrange them on my screen";
        var none = !set.Meter && !set.Plan && !set.Cooldowns;
        var bw = Kit.ButtonWidth(label, BtnSize.Md, Icon.Move);
        if (Kit.Button("##setup-arrange", row, label, Btn.Outline, BtnSize.Md, Icon.Move, 0, StartArrange == null || arranging || none,
                none ? "Nothing is on your screen: the meter, the plan and the cooldowns are off" : "Shows every window at once to drag where you want it"))
            StartArrange?.Invoke();
        const string later = "Or later: Settings › Windows";
        var lw = Txt.Width(later, Face.Sans, 12);
        if (bw + S(12) + lw <= w)
        {
            Txt.Line(dl, new Vector2(row.X + bw + S(12), row.Y), later, Face.Sans, 12, Palette.Faint, H);
            Kit.Reserve(row, w, S(H));
        }
        else
        {
            Kit.Reserve(row, w, S(H));
            Kit.Gap(6);
            var lp = Kit.At;
            Txt.Line(dl, lp, later, Face.Sans, 12, Palette.Faint, 17);
            Kit.Reserve(lp, w, S(17));
        }
    }

    private static readonly string[] ThemeTileIds = { "##tile0", "##tile1", "##tile2" };

    /// <summary>Three theme tiles side by side: the theme's ground with three bars in its colours, its name; the one picked edged in ember.</summary>
    private void ThemeTiles()
    {
        var at = Kit.At;
        var w = Kit.Width;
        var dl = Kit.Dl;
        var gap = S(8);
        var tileW = (w - gap * (GuideThemes.Length - 1)) / GuideThemes.Length;
        var pad = S(10);
        var swatchH = S(8) * 2 + S(5) * 3 + S(4) * 2;
        var tileH = pad * 2 + swatchH + S(8) + S(16);
        string? tip = null;
        for (var i = 0; i < GuideThemes.Length; i++)
        {
            var t = MeterThemes.Of(GuideThemes[i]);
            var min = new Vector2(at.X + (tileW + gap) * i, at.Y);
            var max = min + new Vector2(tileW, tileH);
            var picked = config.MeterTheme == t.Id;
            var (clicked, hovered, _) = Kit.Hit(ThemeTileIds[i], min, max - min);
            Kit.Box(dl, min, max, hovered && !picked ? Palette.Raised : Palette.Slab, picked ? Palette.Ember : hovered ? Palette.Line3 : Palette.Line2);
            var smin = min + new Vector2(pad);
            var smax = new Vector2(max.X - pad, smin.Y + swatchH);
            dl.AddRectFilled(smin, smax, Palette.U32(t.Win.GradTop ?? t.Win.Bg with { W = 1 }), S(6));
            var bars = new[] { (t.Roles.Dps, 0.92f), (t.Roles.Tank, 0.74f), (t.Roles.Healer, 0.46f) };
            for (var b = 0; b < bars.Length; b++)
            {
                var y = smin.Y + S(8) + (S(5) + S(4)) * b;
                var bw = (smax.X - smin.X - S(16)) * bars[b].Item2;
                dl.AddRectFilled(new Vector2(smin.X + S(8), y), new Vector2(smin.X + S(8) + bw, y + S(5)), Palette.U32(bars[b].Item1 with { W = 1 }), S(2));
            }
            Txt.Line(dl, new Vector2(min.X + pad, smax.Y + S(8)), Txt.Fit(t.Name, Face.SansMedium, 12, tileW - pad * 2), Face.SansMedium, 12,
                picked ? Palette.White : Palette.Muted, 16);
            if (hovered) tip = t.Blurb;
            if (clicked && !picked)
            {
                config.MeterTheme = t.Id;
                config.Save();
            }
        }
        if (tip != null) Kit.Tooltip(tip);
        Kit.Reserve(at, w, tileH);
    }

    /// <summary>
    /// Where they go: a picture of your screen with a dashed box for each window that's on (the callouts at the top in
    /// ember, the plan on the left, the cooldowns and the meter on the right).
    /// </summary>
    private static void ScreenPicture(FeatureSet set)
    {
        var at = Kit.At;
        var w = Kit.Width;
        var dl = Kit.Dl;
        var h = S(196);
        var max = at + new Vector2(w, h);
        Kit.Box(dl, at, max, Palette.Hex(0x141821), Palette.Line2, 12);
        // A light from above, as the game's sky.
        dl.AddRectFilledMultiColor(at + new Vector2(S(1), S(12)), new Vector2(max.X - S(1), at.Y + h * 0.6f),
            Palette.U32(Palette.Hex(0x1d2330, 0.9f)), Palette.U32(Palette.Hex(0x1d2330, 0.9f)), Palette.U32(Palette.Hex(0x141821, 0)), Palette.U32(Palette.Hex(0x141821, 0)));

        void Window(Vector2 min, Vector2 size, string label, bool ember)
        {
            var m = min + size;
            dl.AddRectFilled(min, m, Palette.U32(ember ? Palette.Rgba(255, 138, 61, 0.12f) : Palette.Rgba(255, 255, 255, 0.05f)), S(6));
            Kit.DashedRect(dl, min, m, ember ? Palette.EmberHi : Palette.Rgba(255, 255, 255, 0.45f));
            var lw = Txt.Width(label, Face.SansMedium, 12);
            Txt.Line(dl, new Vector2(min.X + (size.X - lw) / 2, min.Y), label, Face.SansMedium, 12, ember ? Palette.EmberHi : Palette.Text, size.Y / S(1));
        }

        if (set.Plan)
        {
            Window(new Vector2(at.X + (w - S(170)) / 2, at.Y + S(18)), new Vector2(S(170), S(30)), "Callouts", true);
            Window(new Vector2(at.X + S(14), at.Y + S(64)), new Vector2(S(110), S(92)), "Plan", false);
        }
        if (set.Cooldowns) Window(new Vector2(max.X - S(14) - S(120), at.Y + S(58)), new Vector2(S(120), S(40)), "Party cooldowns", false);
        if (set.Meter) Window(new Vector2(max.X - S(14) - S(120), max.Y - S(14) - S(72)), new Vector2(S(120), S(72)), "Meter", false);
        if (!set.Plan && !set.Cooldowns && !set.Meter)
        {
            const string none = "Nothing on your screen: the windows are off";
            var nw = Txt.Width(none, Face.Sans, 12);
            Txt.Line(dl, new Vector2(at.X + (w - nw) / 2, at.Y), none, Face.Sans, 12, Palette.Faint, 196);
        }
        Txt.Line(dl, new Vector2(at.X + S(14), max.Y - S(14) - S(16)), "Your screen", Face.Sans, 11, Palette.Faint, 16);
        Kit.Reserve(at, w, h);
    }

    // ---------------------------------------------------------------------------------------------
    // 5 Done.

    private void SetupDone()
    {
        var at = Kit.At;
        var dl = Kit.Dl;
        var c = at + new Vector2(S(24), S(24));
        dl.AddCircleFilled(c, S(24), Palette.U32(Palette.Rgba(61, 220, 132, 0.12f)), 32);
        dl.AddCircle(c, S(24) - 0.5f, Palette.U32(Palette.Rgba(61, 220, 132, 0.4f)), 32, 1f);
        Icons.Draw(dl, Icon.Check, c - new Vector2(S(12)), S(24), Palette.Ready, 2f);
        Kit.Reserve(at, Kit.Width, S(48));
        Kit.Gap(12);
        var set = config.FeaturesNow();
        SetupHead("You’re set", Lead(set.Record
            ? "The next pull in a raid or trial is recorded by itself. Here’s what’s on:"
            : "Recording is off, so no pull is kept until you turn it on. Here’s what’s on:"), 10);
        Kit.Gap(18);
        var facts = new SetupFacts(Linked, config.RecordLevel, config.Keep, config.MeterCompact, MeterThemes.Of(config.MeterTheme).Name, config.MeterEverywhere, config.Cooldowns.Everywhere);
        Kit.BeginPanel("##setup-done", 4, 16, 4, Palette.Slab, Palette.Line);
        for (var i = 0; i < Features.All.Count; i++)
        {
            var f = Features.All[i];
            var on = set[f];
            var p = Kit.At;
            var w = Kit.Width;
            var h = S(10) * 2 + S(19) + S(2) + S(16);
            if (i > 0) Kit.Rule(dl, p.X, p.X + w, p.Y);
            Kit.FeatureWell(dl, new Vector2(p.X, p.Y + (h - S(30)) / 2), 30, f, on, true);
            var tag = on ? "On" : "Off";
            var tw = on ? Kit.ToneTagWidth(tag) : Kit.TagWidth(tag, TagKind.Default);
            if (on) Kit.ToneTag(dl, new Vector2(p.X + w - tw, p.Y + (h - S(20)) / 2), tag, Tone.Ready);
            else Kit.Tag(dl, new Vector2(p.X + w - tw, p.Y + (h - S(20)) / 2), tag, TagKind.Default, 20);
            var x = p.X + S(30) + S(12);
            var room = p.X + w - tw - S(12) - x;
            var top = p.Y + S(10);
            Txt.Line(dl, new Vector2(x, top), Txt.Fit(Features.Name(f), Face.SansMedium, 14, room), Face.SansMedium, 14, on ? Palette.White : Palette.Muted, 19);
            Txt.Line(dl, new Vector2(x, top + S(21)), Txt.Fit(SetupGuide.DoneNote(f, on, facts), Face.Sans, 12, room), Face.Sans, 12, Palette.Faint, 16);
            Kit.Reserve(p, w, h);
        }
        Kit.EndPanel();
        Kit.Gap(18);
        SetupEyebrow("Try it before your next pull");
        Kit.Gap(10);
        var sampleOn = SampleOn?.Invoke() == true;
        var waits = SampleWaits?.Invoke() == true;
        var sample = sampleOn ? "Stop the sample fight" : "Play a sample fight on the meter";
        var callOn = CalloutsSampleOn?.Invoke() == true;
        var callouts = callOn ? "Hide the sample callouts" : "Show sample callouts";
        var wrap = new Wrap(8);
        var sw = Kit.ButtonWidth(sample, BtnSize.Sm, Icon.Play);
        if (Kit.Button("##setup-sample", wrap.Next(sw, S(HSm)), sample, Btn.Outline, BtnSize.Sm, sampleOn ? Icon.Square : Icon.Play, 0,
                ToggleSample == null || (waits && !sampleOn), waits && !sampleOn ? "After this fight: a real one would end it at once" : "A made-up fight on the meter, as it looks in a duty"))
            ToggleSample?.Invoke();
        var cw = Kit.ButtonWidth(callouts, BtnSize.Sm, Icon.Megaphone);
        if (Kit.Button("##setup-callouts", wrap.Next(cw, S(HSm)), callouts, Btn.Outline, BtnSize.Sm, Icon.Megaphone, 0, ToggleCalloutsSample == null,
                "A plan's callouts on your screen, as they come in a fight"))
            ToggleCalloutsSample?.Invoke();
        wrap.End();
        Kit.Gap(10);
        Txt.Para(new[]
        {
            new Run("Type ", Face.Sans, 12, Palette.Faint),
            new Run("/raidcodex", Face.MonoSemibold, 12, Palette.Text),
            new Run(" to open this window again, or ", Face.Sans, 12, Palette.Faint),
            new Run("/raidcodex help", Face.MonoSemibold, 12, Palette.Text),
            new Run(" for every command.", Face.Sans, 12, Palette.Faint),
        }, 18);
    }
}
