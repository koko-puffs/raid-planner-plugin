using System;
using RaidPlanner.UI.Model;

namespace RaidPlanner.UI;

/// <summary>
/// Settings' rows for the ready check card and the death recap (since 0.9.0): Plan and callouts → Ready checks, Damage meter →
/// Death recap, and their windows' rows on Windows (show, lock, click-through, grows, background), drawn and reset here.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly string[] RecapWhoItems = { "Me", "Everyone" };

    /// <summary>The ready check card's sample: on now, and starting or stopping it (set by the plugin).</summary>
    public Func<bool>? ReadyCheckSampleOn { get; set; }

    public Action? ToggleReadyCheckSample { get; set; }

    /// <summary>The death recap's sample: on now, and starting or stopping it (set by the plugin).</summary>
    public Func<bool>? DeathRecapSampleOn { get; set; }

    public Action? ToggleDeathRecapSample { get; set; }

    private void DrawPrepRow(SettingEntry e, int i, bool sub)
    {
        var n = e.Name;
        var l = e.Line;
        switch (e.Id)
        {
            case SettingId.ReadyCheckShow:
                if (SwitchRow(i, "##rc-show", n, l, config.ReadyCheck.Show,
                        "As the game's ready check starts, a card beside the party list: each member's food and its time left (amber when it would run out before your average "
                        + "pull here is over, red with none), then your tinctures, your meals and your gear's condition (a warning under "
                        + $"{ReadyCheckView.GearLow}%). It goes when the pull starts, or 30 seconds after the check ends; its pin keeps it. It only reads the ready check: it "
                        + "never answers it.", pad: 10))
                {
                    config.ReadyCheck.Show = !config.ReadyCheck.Show;
                    config.Save();
                }
                break;
            case SettingId.ReadyCheckSample:
            {
                var on = ReadyCheckSampleOn?.Invoke() == true;
                if (ButtonRow(i, "##rc-sample", n, on ? "A made-up party under a Preview tag" : l, on ? "Stop the sample" : "Show a sample", Btn.Outline,
                        ToggleReadyCheckSample == null, null, "A made-up party's ready check, one of them short of food: to place the card without a ready check.",
                        icon: on ? Icon.Square : Icon.Play, pad: 10))
                    ToggleReadyCheckSample?.Invoke();
                break;
            }
            case SettingId.ReadyCheckLock:
                LockRow(config.ReadyCheck, "rc", i, n, l, "Locked, the ready check card has no title bar and can't be moved or sized.");
                break;
            case SettingId.ReadyCheckClickThrough:
                ClickThroughRow(config.ReadyCheck, "rc", i, n, l, sub);
                break;
            case SettingId.ReadyCheckGrows:
                GrowsRow(config.ReadyCheck, "rc", i, n, l);
                break;
            case SettingId.ReadyCheckBackground:
                BackgroundRow(config.ReadyCheck, "rc", i, n, l);
                break;

            case SettingId.DeathRecapShow:
                if (SwitchRow(i, "##dr-show", n, l, config.DeathRecap.Show,
                        "As a death comes, a card from the meter's own recap: what killed them and by how much, their last six seconds, what mitigation was up (and a tank's big "
                        + "cooldowns that weren't), and Open in the meter. It goes 8 seconds after, held while you point at it; its pin keeps it. A wipe's deaths are one line each. "
                        + "The meter listens while it's on, even with the meter's window off.", pad: 10))
                {
                    config.DeathRecap.Show = !config.DeathRecap.Show;
                    config.Save();
                }
                break;
            case SettingId.DeathRecapWho:
            {
                var who = SegmentRow(i, "##dr-who", n, l, RecapWhoItems, config.DeathRecapEveryone ? 1 : 0, sub, !config.DeathRecap.Show);
                if (who != (config.DeathRecapEveryone ? 1 : 0))
                {
                    config.DeathRecapEveryone = who == 1;
                    config.Save();
                }
                break;
            }
            case SettingId.DeathRecapSample:
            {
                var on = DeathRecapSampleOn?.Invoke() == true;
                if (ButtonRow(i, "##dr-sample", n, on ? "A made-up wipe under a Preview tag" : l, on ? "Stop the sample" : "Show a sample", Btn.Outline,
                        ToggleDeathRecapSample == null, null, "A made-up wipe's three deaths, one line each: press one to open its recap. To place it without dying.",
                        icon: on ? Icon.Square : Icon.Play, pad: 10))
                    ToggleDeathRecapSample?.Invoke();
                break;
            }
            case SettingId.DeathRecapLock:
                LockRow(config.DeathRecap, "dr", i, n, l, "Locked, the death recap has no title bar and can't be moved or sized.");
                break;
            case SettingId.DeathRecapClickThrough:
                ClickThroughRow(config.DeathRecap, "dr", i, n, l, sub);
                break;
            case SettingId.DeathRecapGrows:
                GrowsRow(config.DeathRecap, "dr", i, n, l);
                break;
            case SettingId.DeathRecapBackground:
                BackgroundRow(config.DeathRecap, "dr", i, n, l);
                break;
        }
    }

    /// <summary>Puts one of these rows back as a new configuration has it (Reset this page).</summary>
    private void ResetPrep(SettingId id, Configuration d)
    {
        var c = config;
        switch (id)
        {
            case SettingId.ReadyCheckShow: c.ReadyCheck.Show = d.ReadyCheck.Show; break;
            case SettingId.ReadyCheckLock: c.ReadyCheck.Locked = d.ReadyCheck.Locked; break;
            case SettingId.ReadyCheckClickThrough: c.ReadyCheck.ClickThrough = d.ReadyCheck.ClickThrough; break;
            case SettingId.ReadyCheckGrows: c.ReadyCheck.Grows = d.ReadyCheck.Grows; break;
            case SettingId.ReadyCheckBackground: c.ReadyCheck.Background = d.ReadyCheck.Background; break;
            case SettingId.DeathRecapShow: c.DeathRecap.Show = d.DeathRecap.Show; break;
            case SettingId.DeathRecapWho: c.DeathRecapEveryone = d.DeathRecapEveryone; break;
            case SettingId.DeathRecapLock: c.DeathRecap.Locked = d.DeathRecap.Locked; break;
            case SettingId.DeathRecapClickThrough: c.DeathRecap.ClickThrough = d.DeathRecap.ClickThrough; break;
            case SettingId.DeathRecapGrows: c.DeathRecap.Grows = d.DeathRecap.Grows; break;
            case SettingId.DeathRecapBackground: c.DeathRecap.Background = d.DeathRecap.Background; break;
        }
    }
}
