using System;
using System.Collections.Generic;
using System.Linq;
using RaidPlanner.Core;
using RaidPlanner.Core.Meter;

namespace RaidPlanner.UI;

/// <summary>
/// A made-up fight for looking at the meter's windows without one (`/raidplan meter preview`): the
/// mock-ups' pull 12 at 3:11, its numbers adding up as theirs do. Shown only when asked for, under
/// a Preview tag; never mixed with the real numbers.
/// </summary>
public sealed class MeterPreview : IMeterSource
{
    private readonly List<MeterEncounter> kept;

    public MeterPreview()
    {
        var (live, best) = (Fight(12, 191, true, null, 0), Fight(4, 255, false, Outcome.Wipe, -3600));
        kept = new List<MeterEncounter>
        {
            live,
            Fight(11, 252, false, Outcome.Wipe, -540),
            Fight(10, 178, false, Outcome.Wipe, -960),
            Fight(9, 232, false, Outcome.Wipe, -1380),
            best,
        };
    }

    public MeterEncounter? Current => kept[0];

    public bool InEncounter => true;

    public bool Waiting => false;

    public IReadOnlyList<MeterEncounter> Kept => kept;

    public MeterEncounter? Get(int id) => kept.FirstOrDefault(k => k.Id == id);

    public MeterEncounter? Best(uint content) => kept[^1];

    public void Clear()
    {
    }

    public PaceLine? Pace(int encounterId, PaceAgainst against) =>
        against == PaceAgainst.Off || encounterId != kept[0].Id ? null : new PaceLine(4, against, "P2", 41, 6_617_000, 6_400_000, 0.034);

    public IReadOnlyList<AbilityRow> Abilities(int encounterId, int partyId, MeterTab tab)
    {
        if (tab != MeterTab.Damage) return Array.Empty<AbilityRow>();
        (string Name, long V, double Pc, int Hits, double? C, double? Dh, long Max, bool Tick)[] a =
        {
            ("Drakesbane", 662734, 12.5, 15, 33.3, 46.7, 98610, false), ("Auto-attack", 614536, 11.6, 68, 26.5, 39.7, 21340, false),
            ("Raiden Thrust", 481989, 9.1, 15, 26.7, 40.0, 71880, false), ("Nastrond", 433790, 8.2, 6, 33.3, 50.0, 108930, false),
            ("Heavens' Thrust", 369525, 6.9, 8, 75.0, 50.0, 104270, false), ("Chaotic Spring", 284675, 5.3, 63, null, null, 6920, true),
            ("Lance Barrage", 273127, 5.1, 8, 25.0, 37.5, 69450, false), ("Fang and Claw", 273127, 5.1, 8, 25.0, 50.0, 74180, false),
            ("High Jump", 240994, 4.5, 6, 33.3, 50.0, 88560, false), ("Wheeling Thrust", 238986, 4.5, 7, 28.6, 42.9, 73920, false),
            ("Geirskogul", 200000, 3.8, 6, 33.3, 33.3, 61000, false), ("Stardiver", 180000, 3.4, 3, 33.3, 66.7, 98000, false),
        };
        return a.Select((x, i) => new AbilityRow((uint)(100 + i), x.Name, x.Tick, false, x.V, x.Pc / 100, x.Hits, x.C / 100, x.Dh / 100, x.Max, false)).ToList();
    }

    private static MeterEncounter Fight(int pull, double duration, bool live, Outcome? ended, double ago)
    {
        (string Slot, string Name, string Job, string Role, double Enc, double Dps, long Dmg, double Pc, double Crit, double Dh, long Max, int Deaths,
            double Hps, double Over, long Taken, double TakenPc)[] p =
            {
                ("M1", "Rook", "DRG", "dps", 27840, 28046, 5317440, 16.9, 31.4, 44.2, 112460, 0, 410, 0.63, 1086900, 8.3),
                ("R2", "Ilya", "SMN", "dps", 26910, 26995, 5139810, 16.3, 29.8, 41.7, 214830, 0, 1150, 0.44, 1037800, 7.9),
                ("M2", "Tamsin", "NIN", "dps", 26120, 26382, 4988920, 15.8, 30.6, 43.9, 158630, 0, 380, 0.58, 1052600, 8.0),
                ("R1", "Fenn", "BRD", "dps", 23480, 23517, 4484680, 14.2, 28.9, 46.1, 124380, 0, 240, 0.71, 1198300, 9.1),
                ("OT", "Brann", "DRK", "tank", 16930, 16939, 3233630, 10.3, 26.4, 35.2, 118940, 0, 1860, 0.41, 2486100, 19.0),
                ("MT", "Koko", "PLD", "tank", 15210, 15210, 2905110, 9.2, 27.1, 33.8, 126310, 0, 2310, 0.48, 3912400, 29.9),
                ("H1", "Sylvie", "WHM", "healer", 12460, 12632, 2379860, 7.6, 25.3, 31.6, 98470, 0, 8940, 0.31, 1004200, 7.7),
                ("H2", "Odette", "SCH", "healer", 9050, 10289, 1728550, 5.5, 24.8, 29.4, 61240, 1, 4120, 0.22, 1324700, 10.1),
            };
        var rows = p.Select((x, i) => new MeterRow(i + 1, x.Slot, x.Name, x.Job, x.Role, x.Slot == "MT", false, x.Dmg, x.Enc, x.Dps, x.Pc / 100, x.Crit / 100, x.Dh / 100,
            x.Max, "Drakesbane", (long)(x.Hps * duration), (long)(x.Hps * duration * x.Over / (1 - x.Over)), x.Hps, x.Over, x.Hps / 19410, 0.2, 40000,
            x.Taken, x.Taken / duration, x.TakenPc / 100, 104880, x.Deaths, x.Deaths > 0, x.Deaths > 0 ? 171 : null, x.Deaths > 0 ? 168 : duration)).ToList();
        rows.Add(new MeterRow(0, "LB", "Limit break", string.Empty, string.Empty, false, true, 1337000, 7000, 0, 0.042, 0, 0, 1337000, "Bladedance", 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, false, null, 0));
        var party = new MeterRow(0, string.Empty, "Party", string.Empty, string.Empty, false, false, 31515000, 165000, 165000, 1, 0.28, 0.4, 1337000, "Bladedance",
            3707310, 1200000, 19410, 0.3, 1, 0.2, 40000, 13103000, 13103000 / duration, 1, 104880, 1, false, null, duration);
        var recap = new List<RecapLine>
        {
            new(161.0, "Full HP", 0, 97210, "No shield on her"),
            new(165.2, "Exaflare", -38620, 97210, "Vulnerability Up from it, 1 stack"),
            new(166.4, "Embrace", 6120, 58590, "Eos, her fairy"),
            new(167.9, "Cure II", 21400, 64710, "Sylvie"),
            new(171.3, "Exaflare", -104880, 86110, "With Vulnerability Up: the killing blow"),
        };
        var deaths = new List<DeathEntry> { new(8, "H2", "Odette", 171.3, "Exaflare", 104880, 18770, false, recap) };
        return new MeterEncounter(pull, pull, "Example Arena", 1, ago, duration, live, ended, duration, "P2", 150, rows, party, deaths);
    }
}
