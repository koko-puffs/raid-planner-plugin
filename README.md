# Raid Codex (Dalamud plugin)

Record the fights you're in, then make them into plans on
[Koko's Raid Codex](https://www.kokos-tools.com/raid-codex).

> **Status: new and untested in the game.** It compiles against Dalamud 15 (API 15) and its
> recording, file and site-link code is unit-tested, as is the site's side that turns a pull into
> a plan, but nobody has recorded a real fight with it yet. Expect rough edges, and send what's
> wrong.

## What it does

- **Records each pull by itself** in trials, raids, ultimates and criterion dungeons, from the pull until the fight ends (a wipe, a
  clear, or combat simply over): the bosses' casts and where they were aimed, where each attack
  landed and whom it hit (which tells a stack from a spread), head markers, tethers, waymarks, the
  arena changing, and where the bosses and (if you like) your party stood; how much each hit did
  and everyone's HP, and your party's own hits, heals and buffs, for the site's numbers; every action
  and potion your party used (the moments before the pull too), raises, interrupts and stuns, whom the
  boss was on and its enmity list, MP, barriers and the limit break gauge, for the site's replay,
  timelines and events; your own gear and stats as the pull began; and your party's names, home
  worlds and levels, the game's version and the server's time, for your logs.
- **Uploads every pull to your logs** on the Raid Codex as it ends, once you've linked your account
  (in the background, one at a time; pulls kept while you're offline go when you're back). Your
  logs are **public on the site**, with your party's names and worlds, as on FFLogs; you can make
  yours private there (Settings → Your logs), or turn _Upload my logs_ off.
- **Sends a pull to the Raid Codex** with one press, once you've linked your account. It waits
  on the Raid Codex's front page under _From the game_; _Make a plan_ turns it into steps, one for
  each moment something goes off, with exact shapes and arenas for the fights BossMod knows.
- **Or saves it as a file** to import on the site yourself (Import, on the Raid Codex's front page).
- **Shows a pull's timeline** before you send it: every mechanic in order, with its icon.
- **Replays a pull** on its arena, as the Raid Codex does: the party, the mechanics, HP, debuffs and deaths, with Play.
- **Shows your plan in the duty.** A small window with the step's callout, its board and its notes,
  which moves along with the pull by itself, and who uses which cooldown in it. Each step said in chat
  a few seconds before it comes, and the next few on screen, counting down to each (with yours: "You:
  Reprisal"), with a sound if you like.
- **A meter over the fight**: damage, healing, damage taken and deaths for your party, worked out
  in the game (no names leave it). Compact by default: the top eight with their job icons and bars in
  colours you can tell apart with colour blindness, short steady numbers (27.8k), you pinned at the
  foot when you're below them, and a details panel when you point at a row; in one of six themes
  (Ready Check, Mochi, Native, Readout, Neon, Grimoire). Pick an earlier pull, pace against your best,
  open it up for every column and a death's recap, see the fight's events (raises, interrupts,
  potions, limit breaks, tank swaps) and the limit break gauge, and copy it as text for chat. A
  cross-world party counts too, outside duties as well (on a target dummy with a friend).
- **For healers and tanks**: the Healing tab shows overheal and what your shields absorbed (an estimate); opened up,
  your heals by ability, and a Mitigation tab with every tank buster and raid-wide and what was up for it.
- **A party cooldown tracker**: your party's raid buffs, mitigation and Swiftcast, each with the
  time until it's ready (yours from the game's own timers, the others' from when they used them), and
  a cooldown your plan has someone use soon outlined ("planned in 8 s").
- **A ready check card**: when a ready check starts, who's fed and for how long (amber if their food runs
  out before your average pull here), your tinctures and food, and a warning when your gear needs repairing.
- **A death recap**: when you die (or anyone, if you like), what killed you, your last six seconds, and what
  mitigation was up, with a button to open it in the meter.
- **Tracks your progress** (once linked): how far each pull got, the boss's HP left and each spot's
  numbers, for the site's progress tracker.
- **Only watches.** It never acts in the game or sends anything to it.

## Installing it

1. In game, `/xlsettings` → **Experimental** → **Custom Plugin Repositories** → add
   `https://www.kokos-tools.com/dalamud/repo.json` (Koko's repository: already there if you use
   Decorium) → tick it → **Save and Close**.
2. `/xlplugins` → **All Plugins** → **Raid Codex** → **Install**.
3. `/raidcodex` (or `/rc`) opens it.

## Using it

The first time, a short **setup guide** opens: pick what you want on (recording, uploads, the meter, your plan
and callouts, the party's cooldowns, your progress), link your account, choose how the meter looks and
place the windows. You can run it again from Settings → Features.

1. Go into a duty and fight. The server info bar shows **● Rec 2:31** while a pull is recorded.
2. `/raidcodex`: **Home** says what's on and shows the latest pull in a sentence, with **Open in Review**;
   **Pulls** lists every pull kept. A pull goes to your Raid Codex as it ends once your account is linked
   (Settings → Account and privacy, with a token from the site's Settings → Raid Codex → In-game plugin), or
   **Send to Raid Codex** / **Save as file** on the pull.
3. On the site: _From the game_ → **Make a plan**. Pick where the party goes (where they stood,
   standard spots, or nowhere), check the arena, choose the steps, and it opens in the editor.

In a duty you've made a plan for, its window opens by itself. **Arrange windows** (Settings → Windows, or
`/raidcodex arrange`) shows every window at once to drag into place, then locks them.

`/raidcodex settings` opens Settings; `/raidcodex help` lists every command in chat. Each works
with `/rc` in place of `/raidcodex`. Nothing is ever bound to a key.

### Settings

Settings has a page for each thing you might want to change, and _Find a setting_ at the top. Each page
shows what most people change; the rest fold under one "N more" row.

- **Features**: one switch for each main part, what each is doing now, and the setup guide again.
- **Recording and uploads**: _Record my pulls_ (on), in raids and trials or every duty, _Record where the
  party stands_ (on; off keeps only the enemies and their attacks), _Keep the last_ 20 pulls (5 to 100;
  the oldest sent go first, pulls still to upload are held up to 200), _Upload my logs_ (on, once linked)
  with _Upload my older pulls_ and _Who sees my logs_ (the site's Settings → Your logs: your logs are
  public, as on FF Logs, unless you make them private), _Track my progress_ (on: how far each pull got,
  who died to what, the boss's HP left and each spot's numbers, no names). More: smoother replays, the
  server info bar, the recordings folder.
- **Damage meter**: when it shows (it stays open between pulls in duties, or folds to a line), its size (Detailed, or Compact) and theme with a live preview, role
  colours, ENCDPS or DPS, names or jobs only, a sample fight; more: which way it grows, its head and
  order, the limit break gauge, its background, when a fight ends, kept fights, pace, decorations, whole
  numbers, pets and the limit break's row.
- **Plan and callouts**: _Show my plan in duties_, the plan for this duty, _Say each step in chat_, the
  on-screen callouts (how far ahead, how many, a sound before each step) with a preview, _Share my step
  with the site_ (off) and _Send my party to my roster_ (only when you press it).
- **Party cooldowns**: the tracker (off), in duties or everywhere, yours too, and which cooldowns by kind.
- **Profiles**: a setup for each kind of duty (ultimates, savage, dungeons, the open world…): going in
  switches to it, leaving goes back. A profile changes only what you turn on in it, window places included.
- **Windows**: how big the windows are (_Fit to my screen_, on: bigger on a big screen, 150% on 4K; or a
  size from 80% to 200%, also `/raidcodex size 150`), _Arrange windows_, and for each window: show it, lock
  it, click-through, which way it grows, its background.
- **Account and privacy**: link or unlink your account, who sees your logs, and what goes to the site.
- **Advanced**: _Diagnostics_ (off; what it reads from the game goes to `diagnostics-<date>.log` in the
  recordings folder, with character names), kept fights, the setup guide.

## Building it

You need the .NET 10 SDK and Dalamud (XIVLauncher installs it; or set `DALAMUD_HOME`).

```sh
dotnet test RaidPlanner.Tests/RaidPlanner.Tests.csproj
dotnet build RaidPlanner.sln -c Release
```

For a dev build in the game: Dalamud Settings → Experimental → Dev Plugin Locations → the full path
to `RaidPlanner/bin/x64/Debug/RaidPlanner.dll`.

See [DETAILS.md](DETAILS.md) for how it works, [docs/PUBLISHING.md](docs/PUBLISHING.md) to
release it, and [docs/IN-GAME-CHECKS.md](docs/IN-GAME-CHECKS.md) for what to try in the game to confirm what it reads. It's a third-party plugin from its own repository, so Dalamud's team hasn't reviewed
it, and plugins are against Square Enix's terms of service: use it at your own risk.

Licence: AGPL-3.0 (see LICENSE). The bundled Geist, Geist Mono, Archivo, Zen Maru Gothic and Fraunces
fonts are under the SIL Open Font License 1.1.
