using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using RaidPlanner.Core;
using RaidPlanner.Game;
using RaidPlanner.UI;
using RaidPlanner.UI.Model;

namespace RaidPlanner;

/// <summary>
/// Raid Codex: records the fights you're in and sends them to Koko's Raid Codex, where they
/// become plans, and shows your plans in the game. /raidcodex opens the window; /raidcodex stop ends
/// the pull being recorded; /raidcodex settings opens Settings; /raidcodex plan shows the plan
/// window, /raidcodex next and prev move its step; /raidcodex send last sends the newest pull;
/// /raidcodex cooldowns shows the party cooldown tracker, /raidcodex callouts the on-screen callouts;
/// /raidcodex arrange shows every window to drag into place.
/// /raidplan (its name before 0.5.1) still does all of it.
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    private const string BarTitle = "Raid Codex";

    private readonly Configuration config;
    private readonly RecordingStore store;
    private readonly FightRecorder recorder;

    /// <summary>The meter's game side; its <see cref="CombatMeter.Source"/> is what the meter's window reads.</summary>
    private readonly CombatMeter meter;
    private readonly SiteSession site;
    private readonly PlanService plans;
    private readonly Uploads uploads;

    /// <summary>Upload my logs (since 0.6.0): each kept pull whole to the site's logs.</summary>
    private readonly LogUploader logs;
    private readonly Fonts fonts;
    private readonly WindowSystem windows = new("RaidPlanner");

    /// <summary>The plugin's windows and overlays drawn, their time watched (<see cref="Game.Stalls"/>).</summary>
    private void DrawWindows()
    {
        var started = Game.Stalls.Start();
        FollowScale();
        windows.Draw();
        Game.Stalls.End("drawing its windows and overlays", started);
    }
    private readonly MainWindow window;
    private readonly PlanWindow planWindow;
    private readonly MeterWindow meterWindow;
    private readonly MeterDetailWindow meterDetail;

    /// <summary>The party cooldown tracker's game side and its window (0.4.0).</summary>
    private readonly PartyCooldowns cooldowns;

    private readonly CooldownWindow cooldownWindow;

    /// <summary>The on-screen callouts (0.4.0).</summary>
    private readonly CalloutWindow calloutWindow;

    /// <summary>The ready check card and the death recap (0.9.0).</summary>
    private readonly ReadyCheckWindow readyCheckWindow;

    private readonly DeathRecapWindow deathRecapWindow;

    /// <summary>Arrange windows (0.8.0): its toolbar (with Done, Cancel, Reset places) and its options panel.</summary>
    private readonly ArrangeWindow arrangeWindow;

    /// <summary>The game's own data (duties, actions…), read once each.</summary>
    private readonly GameData data;

    /// <summary>Settings profiles (0.9.0): the one for the duty you're in put in effect, and the notice as it is.</summary>
    private readonly ProfileSwitcher profileSwitcher;

    private readonly ProfileNotice profileNotice;

    private IDtrBarEntry? bar;

    /// <summary>The last pull kept, shown in the bar for a moment ("Pull 3 kept").</summary>
    private (RecordingInfo Info, DateTime Until)? justKept;

    /// <summary>What the bar shows now: set again only when it changes (setting it redraws the game's bar).</summary>
    private string? barText;

    /// <summary>The bar couldn't be used this session (another plugin took its name): not tried again, the setting left as it is.</summary>
    private bool barBroken;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<Services>();
        // A fresh install's settings start with the setup guide due (0.8.0); saved ones are an existing player's.
        config = Services.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration { Setup = UI.Model.SetupState.Pending };
        // 0.9.0: a settings profile left in effect (the game closed or the plugin reloaded in a duty): your usual settings
        // back before anything reads them; the duty you're in puts it in again (FollowProfiles).
        var profileWasOn = UI.Model.ProfileLayer.Recover(config, config);
        if (profileWasOn) config.Save();
        // Settings saved by 0.5.x (version 1): Upload my logs on, from now (0.6.0); a new one starts from now too. Before 0.7.0: the meter compact.
        if (config.Migrate(DateTimeOffset.Now)) config.Save();
        try
        {
            var version = typeof(Plugin).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            store = new RecordingStore(Path.Combine(Services.PluginInterface.GetPluginConfigDirectory(), "recordings")) { Keep = Math.Clamp(config.Keep, 5, 100) };
            // The list is read off the framework thread (it may read files when its index is missing).
            _ = Task.Run(() =>
            {
                try
                {
                    store.Load();
                }
                catch (Exception ex)
                {
                    Services.Log.Error(ex, "Raid Codex couldn't read its recordings");
                }
            });
            data = new GameData();
            recorder = new FightRecorder(config, data, store, version);
            recorder.Kept += OnKept;
            meter = new CombatMeter(config, data, recorder);
            site = new SiteSession(() => config.SiteToken);
            plans = new PlanService(config, data, recorder, site);
            uploads = new Uploads(config, data, recorder, site);
            logs = new LogUploader(config, store, recorder, site, version);

            // The windows' size (Settings → Windows → Size): fitting the screen, the scale it gave last time (the screen
            // isn't known until the first frame: FollowScale moves it then if it's another).
            var wasTotal = WindowScaleWas();
            var units = Dalamud.Interface.Utility.ImGuiHelpers.GlobalScale;
            UiScale.Start(config.WindowSize == WindowSize.Fit
                ? Math.Clamp(MathF.Round(wasTotal / (units > 0 ? units : 1) * 20) / 20, 1, WindowSize.FitMost)
                : WindowSize.Factor(config.WindowSize, 0, 1), wasTotal);
            fonts = new Fonts(UiScale.Factor);
            window = new MainWindow(config, store, recorder, site, plans, uploads, logs, fonts);
            planWindow = new PlanWindow(config, plans, recorder, data, fonts);
            meterWindow = new MeterWindow(config, fonts);
            meterDetail = new MeterDetailWindow(config, fonts);
            cooldowns = new PartyCooldowns(data, recorder);
            cooldownWindow = new CooldownWindow(config, fonts, cooldowns);
            calloutWindow = new CalloutWindow(config, fonts, plans);
            // 0.9.0: the ready check card (read only: it never answers the game's) and the death recap (the meter's own deaths).
            var prep = new PrepData();
            meter.Prep = prep;
            readyCheckWindow = new ReadyCheckWindow(config, fonts, new ReadyCheckReader(data, prep))
            {
                InCombat = () => Services.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.InCombat],
                AveragePull = () => ReadyCheckView.AveragePull(store.All, recorder.DutyHere?.Name),
            };
            deathRecapWindow = new DeathRecapWindow(config, fonts)
            {
                Source = () => meter.Source,
                OpenInMeter = (fight, member, death) => meterDetail.Show(Core.Meter.MeterTab.Deaths, fight, member, death),
            };
            // The recap reads the meter's deaths: the meter listens while it's on, even with its window off.
            meter.AlsoWanted = () => config.DeathRecap.Show;
            arrangeWindow = new ArrangeWindow(config, fonts, meterWindow, planWindow, calloutWindow, cooldownWindow)
            {
                ReadyCheck = readyCheckWindow,
                DeathRecap = deathRecapWindow,
            };
            windows.AddWindow(window);
            windows.AddWindow(planWindow);
            windows.AddWindow(meterWindow);
            windows.AddWindow(meterDetail);
            windows.AddWindow(cooldownWindow);
            windows.AddWindow(calloutWindow);
            windows.AddWindow(readyCheckWindow);
            windows.AddWindow(deathRecapWindow);
            // Last: drawn over the overlays it arranges.
            windows.AddWindow(arrangeWindow);
            windows.AddWindow(arrangeWindow.Panel);
            // Settings profiles (0.9.0): switched as you go in and out of duties; the notice's Undo keeps your usual settings there.
            profileSwitcher = new ProfileSwitcher(config, config);
            profileNotice = new ProfileNotice(fonts);
            windows.AddWindow(profileNotice);
            profileNotice.Undo = () =>
            {
                // Not while windows are arranged: Arrange windows' Cancel would put the profile's places into your usual ones.
                if (ArrangeMode.On) return;
                if (profileSwitcher.Undo() is { } undone) ProfileSwitched(undone);
            };
            window.DutyKindHere = () => profileSwitcher.Kind;
            window.PlanPlaceBack = PutPlanBack;
            // The plan window where the settings say (ImGui remembers it where a profile may have had it as the game closed).
            PutPlanBack();
            // The tracker listens to the recorder's parser and keeps its hooks on while it's shown.
            recorder.HooksWanted = () => cooldowns.WantsHooks;
            cooldownWindow.InDuty = () => recorder.DutyHere != null;
            // The meter's gauge, read for the tracker when the meter's off (it would freeze otherwise).
            cooldownWindow.Gauge = meter.GaugeNow;
            // Since 0.5.0: the plan's cooldowns, while it follows a pull (the tracker's "planned in 8 s"; the callouts' "You: Reprisal").
            cooldownWindow.PlanNow = () => plans.Plan is { } p && plans.PlanClock is { } c ? (p, c) : null;
            calloutWindow.YourSpot = () => PartyMembers.YourSpot(data);
            // Each pull, every step's callout sound can play again.
            recorder.PullStarted += OnPullStarted;
            window.CooldownsSampleOn = () => cooldownWindow.Previewing;
            window.ToggleCooldownsSample = () => cooldownWindow.Previewing = !cooldownWindow.Previewing;
            window.CalloutsSampleOn = () => calloutWindow.Previewing;
            window.ToggleCalloutsSample = () => calloutWindow.Previewing = !calloutWindow.Previewing;
            window.PlaySound = calloutWindow.Play;
            window.ReadyCheckSampleOn = () => readyCheckWindow.Previewing;
            window.ToggleReadyCheckSample = () => readyCheckWindow.Previewing = !readyCheckWindow.Previewing;
            window.DeathRecapSampleOn = () => deathRecapWindow.Previewing;
            window.ToggleDeathRecapSample = () => deathRecapWindow.Previewing = !deathRecapWindow.Previewing;
            window.TogglePlanWindow = () => planWindow.Toggle();
            window.PlanWindowOpen = () => planWindow.IsOpen;
            // Arrange windows (0.8.0): Settings → Windows and the setup guide start it; /raidcodex arrange too.
            // The main window steps aside while the windows are arranged (it would cover them), and comes back after.
            var mainAside = false;
            window.StartArrange = () =>
            {
                if (!ArrangeMode.On) mainAside = window.IsOpen;
                window.StepAside();
                arrangeWindow.Start();
            };
            arrangeWindow.Ended = () =>
            {
                if (mainAside) window.IsOpen = true;
                mainAside = false;
            };
            window.Arranging = () => ArrangeMode.On;
            planWindow.OpenSettings = window.ShowAccount;
            // A fresh install: the setup guide opens once (0.8.0); it's Done when finished or skipped.
            if (UI.Model.SetupState.Due(config.Setup)) window.ShowSetup();
            // The meter's windows read the meter's numbers only (CombatMeter's session), or a made-up fight when previewed.
            var meterSource = () => meterWindow.Preview ?? meter.Source;
            meterWindow.Source = () => meter.Source;
            meterWindow.InDuty = () => recorder.DutyHere != null;
            meterWindow.OpenDetail = (tab, fight, member, death) => meterDetail.Show(tab, fight, member, death);
            meterWindow.OpenSettings = window.ShowMeterSettings;
            meterWindow.StartSample = StartSample;
            arrangeWindow.StartSample = StartSample;
            meterDetail.Source = meterSource;
            // Back to the small meter: it follows Show the meter (OnUpdate), so that's turned on too, or it would close again next frame.
            meterDetail.ShowSmall = () =>
            {
                if (!config.ShowMeter)
                {
                    config.ShowMeter = true;
                    config.Save();
                }
                meterWindow.IsOpen = true;
            };
            window.Meter = meterSource;
            window.MeterProblem = () => meter.Source.Problem;
            // Settings → Meter's sample fight: on and off, and not while a real fight is on.
            window.SampleOn = () => meterWindow.Preview != null;
            window.SampleWaits = () => RealFightOn;
            window.ToggleSample = () =>
            {
                if (meterWindow.Preview != null) meterWindow.Preview = null;
                else StartSample();
            };
            plans.ShowWindow = open => planWindow.IsOpen = open;
            // With recording off, a pull is still followed when this duty's plan is shown: in the plan window, or as the
            // on-screen callouts (they need the plan's clock, which only a followed pull runs).
            recorder.FollowWanted = () => (planWindow.IsOpen || config.CalloutOverlay.Show) && plans.ForThisDuty;

            // /raidcodex, /rc and /raidplan (from before): each does the same (CommandWords.Names). Dalamud's
            // list shows /raidcodex only. Another plugin may have /rc already: then it's theirs, /raidcodex still works.
            foreach (var name in CommandWords.Names)
            {
                var info = new CommandInfo(OnCommand) { HelpMessage = CommandWords.HelpFor(name), ShowInHelp = CommandWords.Listed(name) };
                if (Services.Commands.AddHandler(name, info)) commands.Add(name);
                else Services.Log.Warning($"Raid Codex: {name} is taken by another plugin; /raidcodex still works");
            }
            Services.PluginInterface.UiBuilder.Draw += DrawWindows;
            Services.PluginInterface.UiBuilder.OpenMainUi += window.ShowMain;
            Services.PluginInterface.UiBuilder.OpenConfigUi += window.ShowSettings;
            Services.Framework.Update += OnUpdate;
        }
        catch
        {
            // Dalamud doesn't dispose a plugin whose constructor threw: what was hooked into the game is let go of here.
            Dispose();
            throw;
        }
    }

    /// <summary>The commands registered (one another plugin has already isn't: it isn't ours to remove).</summary>
    private readonly List<string> commands = new();

    private void OnCommand(string command, string args)
    {
        // Lower case, one space between words, "cds" as "cooldowns" (CommandWords.Normalize).
        switch (CommandWords.Normalize(args))
        {
            case "stop":
                if (recorder.Recording) recorder.Stop();
                else Services.Log.Information("Raid Codex: nothing is being recorded");
                break;
            case "help":
            case "?":
                foreach (var line in CommandWords.HelpLines()) PlanService.Print(line);
                break;
            case "settings":
            case "config":
                window.ShowSettings();
                break;
            case "plan":
                planWindow.Toggle();
                break;
            case "next":
                plans.Move(1);
                break;
            case "prev":
            case "previous":
                plans.Move(-1);
                break;
            case "send last":
            case "send":
                SendLast();
                break;
            case "meter":
                // Shows or hides it (Settings' Show the meter).
                config.ShowMeter = !config.ShowMeter;
                config.Save();
                meterWindow.IsOpen = config.ShowMeter;
                break;
            case "meter unlock":
                // Unlock saves the settings, this included.
                config.ShowMeter = true;
                meterWindow.Unlock();
                meterWindow.IsOpen = true;
                break;
            case "meter settings":
                window.ShowMeterSettings();
                break;
            case "meter big":
            case "meter open":
                meterDetail.Show(config.MeterTab, null, null, null);
                break;
            case "meter preview":
            case "meter sample":
                // A sample fight, under a Preview tag, to see the windows mid-fight without one (again to stop it).
                if (meterWindow.Preview != null) meterWindow.Preview = null;
                else if (!StartSample()) PlanService.Print("The meter's sample waits until this fight is over.");
                break;
            case "cooldowns":
                Toggle(config.Cooldowns, cooldownWindow);
                break;
            case "cooldowns unlock":
                config.Cooldowns.Show = true;
                cooldownWindow.Unlock();
                break;
            case "cooldowns preview":
            case "cooldowns sample":
                cooldownWindow.Previewing = !cooldownWindow.Previewing;
                break;
            case "cooldowns settings":
                window.ShowSettingsOn(SettingsTab.Cooldowns);
                break;
            case "callouts":
                Toggle(config.CalloutOverlay, calloutWindow);
                break;
            case "callouts unlock":
                config.CalloutOverlay.Show = true;
                calloutWindow.Unlock();
                break;
            case "callouts preview":
            case "callouts sample":
                calloutWindow.Previewing = !calloutWindow.Previewing;
                break;
            case "callouts settings":
                window.ShowSettingsOn(SettingsTab.Plans);
                break;
            case "arrange":
            case "arrange windows":
                // Every window that's on, shown to drag into place (Done locks them; again while on, nothing new); the
                // main window steps aside as from Settings.
                window.StartArrange?.Invoke();
                break;
            case var size when size == "size" || size.StartsWith("size ", StringComparison.Ordinal):
                SetSize(size.Length > 4 ? size[5..] : string.Empty);
                break;
            default:
                window.Toggle();
                break;
        }
    }

    /// <summary>
    /// Screen px per CSS px the windows were drawn at when the plugin last ran: Dalamud's scale alone before 0.8.1 (or if
    /// the file says something odd).
    /// </summary>
    private float WindowScaleWas()
    {
        if (config.WindowScaleWas is >= 0.25f and <= 8f) return config.WindowScaleWas;
        var units = Dalamud.Interface.Utility.ImGuiHelpers.GlobalScale;
        return units > 0 ? units : 1;
    }

    /// <summary>The game's screen height (px) as last drawn, for /raidcodex size's answer.</summary>
    private float screenHeight;

    private DateTime scaleFailedAt;

    /// <summary>
    /// Each frame, before the windows draw: the scale the size setting and the screen want (Settings → Windows → Size), the
    /// fonts built at it once it's held still (<see cref="Fonts.Follow"/>), and the scale drawn at kept for next time.
    /// </summary>
    private void FollowScale()
    {
        try
        {
            // Minimised (no screen to speak of): the size stays as it is, not fitted to nothing.
            var height = Dalamud.Bindings.ImGui.ImGui.GetMainViewport().Size.Y;
            if (height >= 200) screenHeight = height;
            if (screenHeight > 0 || config.WindowSize != WindowSize.Fit)
            {
                var wanted = WindowSize.Factor(config.WindowSize, screenHeight, Dalamud.Interface.Utility.ImGuiHelpers.GlobalScale);
                UiScale.Want(wanted);
                fonts.Follow(wanted, Dalamud.Bindings.ImGui.ImGui.GetTime());
            }
            var total = Theme.Scale;
            if (MathF.Abs(total - config.WindowScaleWas) > 0.001f)
            {
                config.WindowScaleWas = total;
                config.Save();
            }
        }
        catch (Exception ex)
        {
            if (DateTime.UtcNow - scaleFailedAt < TimeSpan.FromSeconds(30)) return;
            scaleFailedAt = DateTime.UtcNow;
            Services.Log.Error(ex, "Raid Codex couldn't follow the windows' size");
        }
    }

    /// <summary>/raidcodex size: fit (the screen) or a size picked (80–200%), said in chat; otherwise what it is now.</summary>
    private void SetSize(string words)
    {
        var picked = WindowSize.Parse(words);
        if (picked is { } p)
        {
            config.WindowSize = p;
            config.Save();
        }
        else if (words.Length > 0)
        {
            PlanService.Print($"{CommandWords.Main} size takes fit, or a size from {WindowSize.Smallest} to {WindowSize.Largest} (%).");
            return;
        }
        var factor = WindowSize.Factor(config.WindowSize, screenHeight, Dalamud.Interface.Utility.ImGuiHelpers.GlobalScale);
        PlanService.Print(config.WindowSize == WindowSize.Fit
            ? $"The windows fit your screen: {WindowSize.Words(factor)}."
            : $"The windows are {WindowSize.Words(factor)}.");
    }

    /// <summary>/raidcodex cooldowns, /raidcodex callouts: shown or hidden (their setting), said in chat; a sample showing stops.</summary>
    private void Toggle(OverlaySettings o, OverlayWindow w)
    {
        w.Previewing = false;
        o.Show = !o.Show;
        config.Save();
        var what = ReferenceEquals(w, cooldownWindow) ? "The cooldown tracker" : "The callouts";
        PlanService.Print(o.Show ? $"{what} will show {(ReferenceEquals(w, cooldownWindow) ? (o.Everywhere ? "everywhere" : "in duties") : "while your plan follows a pull")}." : $"{what} won't show.");
    }

    private void OnPullStarted() => calloutWindow.NewPull();

    /// <summary>A real fight is on (or the party is in combat waiting for its first hit): a sample would end at once.</summary>
    private bool RealFightOn => meter.Source.InEncounter || meter.Source.Waiting;

    /// <summary>The meter's sample fight, its numbers made up (<see cref="MeterPreview"/>); not while a real fight is on. False when it didn't start.</summary>
    private bool StartSample()
    {
        if (RealFightOn) return false;
        meterWindow.Preview = new MeterPreview();
        meterWindow.IsOpen = true;
        return true;
    }

    private void OnKept(RecordingInfo info)
    {
        justKept = (info, DateTime.UtcNow + TimeSpan.FromSeconds(10));
        // Since 0.6.0 Upload my logs sends it (LogUploader: it's in the store now, so it's waiting there).
    }

    /// <summary>/raidcodex send last: the newest kept pull to the Raid Codex, said in chat.</summary>
    private void SendLast()
    {
        if (!site.Linked)
        {
            PlanService.Print("Link your Raid Codex account first: /raidcodex settings, then Account.");
            return;
        }
        var all = store.All;
        if (all.Count == 0)
        {
            PlanService.Print("There's no pull to send yet.");
            return;
        }
        var info = all[0];
        if (!window.SendPull(info, r => PlanService.Print(r.Sent ? $"Sent {info.Name} to your Raid Codex." : $"Couldn't send {info.Name}: {r.Why}")))
        {
            PlanService.Print("That pull is on its way already.");
            return;
        }
        PlanService.Print($"Sending {info.Name}…");
    }

    /// <summary>The profiles haven't been asked yet since the plugin loaded: a profile put in effect then is put back silently (a reload in a duty).</summary>
    private bool profilesFirst = true;

    private DateTime profilesFailedAt;

    /// <summary>
    /// Each frame: the kind of duty you're in (<see cref="DutyKinds.Of"/>, from the game's data), and the profile it
    /// wants put in effect, or your usual settings back (<see cref="ProfileSwitcher"/>); not while Arrange windows is on
    /// (its Cancel puts back what it found) nor before you're logged in.
    /// </summary>
    private void FollowProfiles()
    {
        try
        {
            var wait = ArrangeMode.On || !Services.ClientState.IsLoggedIn;
            var territory = Services.ClientState.TerritoryType;
            var change = profileSwitcher.Update(territory, DutyKindOf(territory), wait);
            var silent = profilesFirst;
            if (!wait) profilesFirst = false;
            if (change == null) return;
            ProfileSwitched(change);
            if (silent) return;
            if (change.Entered is { } p)
            {
                if (ProfileItems.Chips(p).Count > 0) profileNotice.Show(ProfileItems.Summary(p), true);
            }
            else if (change.Left is { } left && ProfileItems.Chips(left).Count > 0) profileNotice.Show("Back to your usual settings", false);
        }
        catch (Exception ex)
        {
            if (DateTime.UtcNow - profilesFailedAt < TimeSpan.FromSeconds(30)) return;
            profilesFailedAt = DateTime.UtcNow;
            Services.Log.Error(ex, "Raid Codex couldn't switch its settings profile");
        }
    }

    /// <summary>
    /// The kind of duty a zone is: a duty from the Duty Finder by its data; somewhere else, the open world, unless you're
    /// in a duty all the same (a quest's: other duties).
    /// </summary>
    private DutyKind DutyKindOf(uint territory)
    {
        var facts = data.Facts(territory);
        if (facts is { ContentType: > 0 }) return DutyKinds.Of(facts);
        var inDuty = Services.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.BoundByDuty]
            || Services.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.BoundByDuty56]
            || Services.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.BoundByDuty95];
        return inDuty ? DutyKinds.Of(facts ?? new DutyFacts(0, false, false, string.Empty)) : DutyKind.OpenWorld;
    }

    /// <summary>A profile came into effect or went: the settings saved, and the plan window put where they say (its place isn't ImGui's then).</summary>
    private void ProfileSwitched(ProfileChange change)
    {
        config.Save();
        if (change.Places) PutPlanBack();
    }

    private void PutPlanBack()
    {
        if (config.PlanPlaced && config.PlanPlaceW > 0 && config.PlanPlaceH > 0)
            planWindow.PutAt(new System.Numerics.Vector2(config.PlanPlaceX, config.PlanPlaceY), new System.Numerics.Vector2(config.PlanPlaceW, config.PlanPlaceH));
    }

    /// <summary>The server info bar: "● Rec 2:31" while a pull is recorded, "Pull 3 kept" for a moment after.</summary>
    private void OnUpdate(IFramework framework)
    {
        FollowProfiles();
        // A real fight ends the sample at once: its numbers are never shown with the real ones.
        if (meterWindow.Preview != null && RealFightOn) meterWindow.Preview = null;
        // The meter's window follows Settings' Show the meter (and /raidcodex meter), or a sample.
        var meterWanted = config.ShowMeter || meterWindow.Preview != null;
        if (meterWindow.IsOpen != meterWanted) meterWindow.IsOpen = meterWanted;
        // The overlays follow their settings (and their samples); the tracker's game side listens while it's shown where it may be.
        if (cooldownWindow.IsOpen != cooldownWindow.WantedOpen) cooldownWindow.IsOpen = cooldownWindow.WantedOpen;
        if (calloutWindow.IsOpen != calloutWindow.WantedOpen) calloutWindow.IsOpen = calloutWindow.WantedOpen;
        if (readyCheckWindow.IsOpen != readyCheckWindow.WantedOpen) readyCheckWindow.IsOpen = readyCheckWindow.WantedOpen;
        if (deathRecapWindow.IsOpen != deathRecapWindow.WantedOpen) deathRecapWindow.IsOpen = deathRecapWindow.WantedOpen;
        try
        {
            cooldowns.Wanted = config.Cooldowns.Show && !cooldownWindow.Previewing && (config.Cooldowns.Everywhere || recorder.DutyHere != null);
        }
        catch (Exception ex)
        {
            cooldowns.Wanted = false;
            Services.Log.Debug(ex, "Raid Codex couldn't tell where the cooldown tracker may show");
        }
        cooldowns.Update();
        try
        {
            if (!config.ShowInBar || barBroken)
            {
                HideBar();
                return;
            }
            string? text = null;
            string? tip = null;
            if (recorder.Current is { } b)
            {
                text = $"● Rec {Words.Clock(recorder.Elapsed)}";
                tip = $"Raid Codex is recording {b.Duty.Name}, pull {b.Pull}. Click to open the Raid Codex plugin.";
            }
            else if (justKept is { } k && DateTime.UtcNow < k.Until)
            {
                text = $"Pull {k.Info.Pull} kept";
                tip = $"Pull {k.Info.Pull} is kept. Click to open the Raid Codex plugin.";
            }
            if (text == null)
            {
                HideBar();
                return;
            }
            bar ??= Services.DtrBar.Get(BarTitle);
            bar.OnClick ??= _ => window.ShowMain();
            if (text != barText)
            {
                barText = text;
                bar.Text = text;
                bar.Tooltip = tip ?? string.Empty;
            }
            bar.Shown = true;
        }
        catch (Exception ex)
        {
            Services.Log.Warning(ex, "Raid Codex couldn't update the server info bar");
            barBroken = true;
        }
    }

    private void HideBar()
    {
        if (bar == null || barText == null) return;
        bar.Shown = false;
        barText = null;
    }

    public void Dispose()
    {
        // Unloaded while arranging: every window put back as the mode found it.
        if (arrangeWindow != null) Safely(arrangeWindow.Cancel);
        // A settings profile in effect goes out (kept in it), your usual settings saved: the file is never left with a
        // profile's values as yours (an older version reading it would keep them). Loading again in the duty puts it back.
        if (profileSwitcher != null)
            Safely(() =>
            {
                if (ProfileLayer.Deactivate(config, config)) config.Save();
            });
        Safely(() => Services.Framework.Update -= OnUpdate);
        Safely(() => Services.PluginInterface.UiBuilder.Draw -= DrawWindows);
        if (window != null)
        {
            Safely(() => Services.PluginInterface.UiBuilder.OpenMainUi -= window.ShowMain);
            Safely(() => Services.PluginInterface.UiBuilder.OpenConfigUi -= window.ShowSettings);
        }
        foreach (var name in commands) Safely(() => Services.Commands.RemoveHandler(name));
        if (recorder != null) Safely(() => recorder.Kept -= OnKept);
        if (recorder != null) Safely(() => recorder.PullStarted -= OnPullStarted);
        if (recorder != null) Safely(() => recorder.HooksWanted = null);
        Safely(() => window?.Dispose());
        Safely(() => bar?.Remove());
        Safely(windows.RemoveAllWindows);
        Safely(() => plans?.Dispose());
        Safely(() => uploads?.Dispose());
        Safely(() => logs?.Dispose());
        // Calls to the site under way stop here; a pull being saved is still waited for below.
        Safely(() => site?.Dispose());
        Safely(() => cooldowns?.Dispose());
        Safely(() => meter?.Dispose());
        Safely(() => recorder?.Dispose());
        Safely(() => fonts?.Dispose());
    }

    private static void Safely(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Services.Log.Warning(ex, "Raid Codex couldn't let go of something while unloading");
        }
    }
}
