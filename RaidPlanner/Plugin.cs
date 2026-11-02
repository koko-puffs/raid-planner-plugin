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

namespace RaidPlanner;

/// <summary>
/// Raid Planner: records the fights you're in and sends them to Koko's Raid Planner, where they
/// become plans. /raidplan opens the window; /raidplan stop ends the pull being recorded;
/// /raidplan settings opens Settings.
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    private const string Command = "/raidplan";
    private const string BarTitle = "Raid Planner";

    private readonly Configuration config;
    private readonly RecordingStore store;
    private readonly FightRecorder recorder;
    private readonly Fonts fonts;
    private readonly WindowSystem windows = new("RaidPlanner");
    private readonly MainWindow window;
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
            recorder = new FightRecorder(config, new GameData(), store, version);
            recorder.Kept += OnKept;

            fonts = new Fonts();
            window = new MainWindow(config, store, recorder, fonts);
            windows.AddWindow(window);

            Services.Commands.AddHandler(Command, new CommandInfo(OnCommand)
            {
                HelpMessage = "Open Raid Planner. /raidplan stop ends the pull being recorded (and keeps it); /raidplan settings opens Settings.",
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
            default:
                window.Toggle();
                break;
        }
    }

    private void OnKept(RecordingInfo info) => justKept = (info, DateTime.UtcNow + TimeSpan.FromSeconds(10));

    /// <summary>The server info bar: "● Rec 2:31" while a pull is recorded, "Pull 3 kept" for a moment after.</summary>
    private void OnUpdate(IFramework framework)
    {
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
                tip = $"Raid Planner is recording {b.Duty.Name}, pull {b.Pull}. Click to open it.";
            }
            else if (justKept is { } k && DateTime.UtcNow < k.Until)
            {
                text = $"Pull {k.Info.Pull} kept";
                tip = "Click to send it to the Raid Planner.";
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
        Safely(() => window?.Dispose());
        Safely(() => bar?.Remove());
        Safely(windows.RemoveAllWindows);
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
