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
/// /raidcodex cooldowns shows the party cooldown tracker, /raidcodex callouts the on-screen callouts.
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
            var data = new GameData();
            recorder = new FightRecorder(config, data, store, version);
            recorder.Kept += OnKept;
            meter = new CombatMeter(config, data, recorder);
            site = new SiteSession(() => config.SiteToken);
            plans = new PlanService(config, data, recorder, site);
            uploads = new Uploads(config, data, recorder, site);
            logs = new LogUploader(config, store, recorder, site, version);

            fonts = new Fonts();
            window = new MainWindow(config, store, recorder, site, plans, uploads, logs, fonts);
            planWindow = new PlanWindow(config, plans, recorder, data, fonts);
            meterWindow = new MeterWindow(config, fonts);
            meterDetail = new MeterDetailWindow(config, fonts);
            cooldowns = new PartyCooldowns(data, recorder);
            cooldownWindow = new CooldownWindow(config, fonts, cooldowns);
            calloutWindow = new CalloutWindow(config, fonts, plans);
            windows.AddWindow(window);
            windows.AddWindow(planWindow);
            windows.AddWindow(meterWindow);
            windows.AddWindow(meterDetail);
            windows.AddWindow(cooldownWindow);
            windows.AddWindow(calloutWindow);
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
            window.TogglePlanWindow = () => planWindow.Toggle();
            window.PlanWindowOpen = () => planWindow.IsOpen;
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
            default:
                window.Toggle();
                break;
        }
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

    /// <summary>The server info bar: "● Rec 2:31" while a pull is recorded, "Pull 3 kept" for a moment after.</summary>
    private void OnUpdate(IFramework framework)
    {
        // A real fight ends the sample at once: its numbers are never shown with the real ones.
        if (meterWindow.Preview != null && RealFightOn) meterWindow.Preview = null;
        // The meter's window follows Settings' Show the meter (and /raidcodex meter), or a sample.
        var meterWanted = config.ShowMeter || meterWindow.Preview != null;
        if (meterWindow.IsOpen != meterWanted) meterWindow.IsOpen = meterWanted;
        // The overlays follow their settings (and their samples); the tracker's game side listens while it's shown where it may be.
        if (cooldownWindow.IsOpen != cooldownWindow.WantedOpen) cooldownWindow.IsOpen = cooldownWindow.WantedOpen;
        if (calloutWindow.IsOpen != calloutWindow.WantedOpen) calloutWindow.IsOpen = calloutWindow.WantedOpen;
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
