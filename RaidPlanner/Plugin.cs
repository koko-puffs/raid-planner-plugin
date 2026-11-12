using System;
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
/// Raid Planner: records the fights you're in and sends them to Koko's Raid Planner, where they
/// become plans, and shows your plans in the game. /raidplan opens the window; /raidplan stop ends
/// the pull being recorded; /raidplan settings opens Settings; /raidplan plan shows the plan
/// window, /raidplan next and prev move its step; /raidplan send last sends the newest pull.
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    private const string Command = "/raidplan";
    private const string BarTitle = "Raid Planner";

    private readonly Configuration config;
    private readonly RecordingStore store;
    private readonly FightRecorder recorder;

    /// <summary>The meter's game side; its <see cref="CombatMeter.Source"/> is what the meter's window reads.</summary>
    private readonly CombatMeter meter;
    private readonly SiteSession site;
    private readonly PlanService plans;
    private readonly Uploads uploads;
    private readonly Fonts fonts;
    private readonly WindowSystem windows = new("RaidPlanner");
    private readonly MainWindow window;
    private readonly PlanWindow planWindow;
    private readonly MeterWindow meterWindow;
    private readonly MeterDetailWindow meterDetail;
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
        config = Services.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
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
                    Services.Log.Error(ex, "Raid Planner couldn't read its recordings");
                }
            });
            var data = new GameData();
            recorder = new FightRecorder(config, data, store, version);
            recorder.Kept += OnKept;
            meter = new CombatMeter(config, data, recorder);
            site = new SiteSession(() => config.SiteToken);
            plans = new PlanService(config, data, recorder, site);
            uploads = new Uploads(config, data, recorder, site);

            fonts = new Fonts();
            window = new MainWindow(config, store, recorder, site, plans, uploads, fonts);
            planWindow = new PlanWindow(config, plans, recorder, data, fonts);
            meterWindow = new MeterWindow(config, fonts);
            meterDetail = new MeterDetailWindow(config, fonts);
            windows.AddWindow(window);
            windows.AddWindow(planWindow);
            windows.AddWindow(meterWindow);
            windows.AddWindow(meterDetail);
            window.TogglePlanWindow = () => planWindow.Toggle();
            window.PlanWindowOpen = () => planWindow.IsOpen;
            planWindow.OpenSettings = window.ShowAccount;
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
            // With recording off, a pull is still followed when the plan window shows this duty's plan.
            recorder.FollowWanted = () => planWindow.IsOpen && plans.ForThisDuty;

            Services.Commands.AddHandler(Command, new CommandInfo(OnCommand)
            {
                HelpMessage = "Open Raid Planner. /raidplan plan shows your plan, /raidplan next and /raidplan prev move its step; "
                    + "/raidplan meter shows or hides the meter, /raidplan meter unlock gives it the mouse back, /raidplan meter preview shows a sample fight in it; "
                    + "/raidplan send last sends the newest pull; /raidplan stop ends the pull being recorded (and keeps it); /raidplan settings opens Settings.",
            });
            Services.PluginInterface.UiBuilder.Draw += windows.Draw;
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

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "stop":
                if (recorder.Recording) recorder.Stop();
                else Services.Log.Information("Raid Planner: nothing is being recorded");
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
            default:
                window.Toggle();
                break;
        }
    }

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
        // Send each pull: by itself, quietly (the window says how it went).
        if (config.SendEachPull && site.Linked) window.SendPull(info, r =>
        {
            if (!r.Sent) PlanService.Print($"Couldn't send pull {info.Pull}: {r.Why}");
        });
    }

    /// <summary>/raidplan send last: the newest kept pull to the Raid Planner, said in chat.</summary>
    private void SendLast()
    {
        if (!site.Linked)
        {
            PlanService.Print("Link your Raid Planner account first: /raidplan settings, then Account.");
            return;
        }
        var all = store.All;
        if (all.Count == 0)
        {
            PlanService.Print("There's no pull to send yet.");
            return;
        }
        var info = all[0];
        if (!window.SendPull(info, r => PlanService.Print(r.Sent ? $"Sent {info.Name} to your Raid Planner." : $"Couldn't send {info.Name}: {r.Why}")))
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
        // The meter's window follows Settings' Show the meter (and /raidplan meter), or a sample.
        var meterWanted = config.ShowMeter || meterWindow.Preview != null;
        if (meterWindow.IsOpen != meterWanted) meterWindow.IsOpen = meterWanted;
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
                tip = $"Raid Planner is recording {b.Duty.Name}, pull {b.Pull}. Click to open the Raid Planner plugin.";
            }
            else if (justKept is { } k && DateTime.UtcNow < k.Until)
            {
                text = $"Pull {k.Info.Pull} kept";
                tip = $"Pull {k.Info.Pull} is kept. Click to open the Raid Planner plugin.";
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
            Services.Log.Warning(ex, "Raid Planner couldn't update the server info bar");
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
        Safely(() => Services.PluginInterface.UiBuilder.Draw -= windows.Draw);
        if (window != null)
        {
            Safely(() => Services.PluginInterface.UiBuilder.OpenMainUi -= window.ShowMain);
            Safely(() => Services.PluginInterface.UiBuilder.OpenConfigUi -= window.ShowSettings);
        }
        Safely(() => Services.Commands.RemoveHandler(Command));
        if (recorder != null) Safely(() => recorder.Kept -= OnKept);
        Safely(() => window?.Dispose());
        Safely(() => bar?.Remove());
        Safely(windows.RemoveAllWindows);
        Safely(() => plans?.Dispose());
        Safely(() => uploads?.Dispose());
        // Calls to the site under way stop here; a pull being saved is still waited for below.
        Safely(() => site?.Dispose());
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
            Services.Log.Warning(ex, "Raid Planner couldn't let go of something while unloading");
        }
    }
}
