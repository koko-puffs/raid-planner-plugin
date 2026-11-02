# Raid Planner (Dalamud plugin)

Record the fights you're in, then make them into plans on
[Koko's Raid Planner](https://www.kokos-tools.com/raid-planner).

> **Status: new and untested in the game.** It compiles against Dalamud 15 (API 15) and its
> recording, file and site-link code is unit-tested, as is the site's side that turns a pull into
> a plan, but nobody has recorded a real fight with it yet. Expect rough edges, and send what's
> wrong.

## What it does

- **Records each pull by itself** in trials, raids, ultimates and criterion dungeons, from the pull until the fight ends (a wipe, a
  clear, or combat simply over): the bosses' casts and where they were aimed, where each attack
  landed and whom it hit (which tells a stack from a spread), head markers, tethers, waymarks, the
  arena changing, and where the bosses and (if you like) your party stood. Names are never kept,
  only jobs.
- **Sends a pull to the Raid Planner** with one press, once you've linked your account. It waits
  on the planner's front page under _From the game_; _Make a plan_ turns it into steps, one for
  each moment something goes off, with exact shapes and arenas for the fights BossMod knows.
- **Or saves it as a file** to import on the site yourself (Import, on the planner's front page).
- **Shows a pull's timeline** before you send it: every mechanic in order, with its icon.
- **Only watches.** It never acts in the game or sends anything to it.

## Installing it

1. In game, `/xlsettings` → **Experimental** → **Custom Plugin Repositories** → add
   `https://www.kokos-tools.com/dalamud/repo.json` (Koko's repository: already there if you use
   Housing Studio) → tick it → **Save and Close**.
2. `/xlplugins` → **All Plugins** → **Raid Planner** → **Install**.
3. `/raidplan` opens it.

## Using it

1. Go into a duty and fight. The server info bar shows **● Rec 2:31** while a pull is recorded.
2. `/raidplan`: the latest pull is at the top. **Send to Raid Planner** (link your account first:
   Settings → Account, with a token from the site's Settings → Raid Planner → In-game plugin), or
   **Save as file**.
3. On the site: _From the game_ → **Make a plan**. Pick where the party goes (where they stood,
   standard spots, or nowhere), check the arena, choose the steps, and it opens in the editor.

`/raidplan stop` ends the pull being recorded (and keeps it). `/raidplan settings` opens Settings.

### Settings

- **Record fights by themselves**: on. Off, nothing is recorded.
- **In every duty**: off. On, dungeons and field operations are recorded too.
- **Record where the party stands**: on. Off, only the enemies and their attacks are kept, for
  working out your own strats.
- **Keep the last** 20 pulls (5 to 100). Past that, the oldest sent ones go first.
- **Show it in the server info bar**: on.

## Building it

You need the .NET 10 SDK and Dalamud (XIVLauncher installs it; or set `DALAMUD_HOME`).

```sh
dotnet test RaidPlanner.Tests/RaidPlanner.Tests.csproj
dotnet build RaidPlanner.sln -c Release
```

For a dev build in the game: Dalamud Settings → Experimental → Dev Plugin Locations → the full path
to `RaidPlanner/bin/x64/Debug/RaidPlanner.dll`.

See [DETAILS.md](DETAILS.md) for how it works, and [docs/PUBLISHING.md](docs/PUBLISHING.md) to
release it. It's a third-party plugin from its own repository, so Dalamud's team hasn't reviewed
it, and plugins are against Square Enix's terms of service: use it at your own risk.

Licence: AGPL-3.0 (see LICENSE). The bundled Geist fonts are under the SIL Open Font License 1.1.
