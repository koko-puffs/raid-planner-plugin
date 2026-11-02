# Publishing the Raid Planner plugin

How to turn this folder into its own repository, release it, and let players install it from a
custom plugin repository.

## 1. Make the repository

1. On GitHub, create **koko-puffs/raid-planner-plugin** (public, so `raw.githubusercontent.com`
   and release downloads work for everyone). Don't add a README, licence or .gitignore; they're here.
2. Pick the licence. `LICENSE` is **AGPL-3.0**, the norm for Dalamud plugins (Dalamud itself is
   AGPL-3.0, and the official repository expects open source). If you'd rather use another licence,
   replace `LICENSE` now; the bundled fonts keep their own OFL licences either way.
3. Move this folder over, keeping its history. From the kokos-tools checkout:

   ```sh
   git subtree split --prefix=raid-planner-plugin -b plugin-export
   mkdir ../raid-planner-plugin && cd ../raid-planner-plugin
   git init -b main
   git pull ../kokos-tools plugin-export
   git remote add origin https://github.com/koko-puffs/raid-planner-plugin.git
   git push -u origin main
   ```

   (Or just copy the folder's contents, hidden `.github/` and `.gitignore` included, into a fresh
   repo and push.) Afterwards, delete `raid-planner-plugin/` from kokos-tools if you
   like, and drop its ignores from `.vercelignore`, `.oxlintrc.json`, `eslint.config.ts` and
   `vitest.config.ts`.
4. In the new repo: **Settings → Actions → General → Workflow permissions → Read and write
   permissions** → Save. The release workflow needs it to create releases and update `repo.json`.

The **Build** workflow now runs on every push: it downloads the current Dalamud, runs the tests, builds
the plugin and keeps the zip as an artifact.

## 2. Release a version

1. Bump `<Version>` in `RaidPlanner/RaidPlanner.csproj` if you like (the workflow overrides it
   with the tag anyway).
2. Tag and push:

   ```sh
   git tag v0.1.0
   git push origin v0.1.0
   ```

   (Or Actions → Release → Run workflow, with a version.)
3. The **Release** workflow builds with DalamudPackager, creates the GitHub release **v0.1.0** with
   `RaidPlanner.zip`, then commits an updated `repo.json` to `main`: `AssemblyVersion`,
   `DalamudApiLevel`, the three `DownloadLink*` URLs pointing at that release's zip, and `LastUpdate`.
4. Check it:
   - The release page has `RaidPlanner.zip` (with `RaidPlanner.dll`, `RaidPlanner.json`, the
     licences).
   - `https://raw.githubusercontent.com/koko-puffs/raid-planner-plugin/main/repo.json` shows the new
     version and links (raw.githubusercontent caches for a few minutes), and so, within about ten
     minutes more, does `https://www.kokos-tools.com/dalamud/repo.json` (see below).
   - `https://raw.githubusercontent.com/koko-puffs/raid-planner-plugin/main/RaidPlanner/images/icon.png`
     loads (it's the installer's icon).

## Koko's plugin repository

Players add one address, `https://www.kokos-tools.com/dalamud/repo.json`, and get every plugin Koko
makes, now and later. It's served by the kokos-tools site (`api/dalamud-repo.js` there): it reads each
plugin's own `repo.json` from GitHub (this repository's included: the release workflow keeps it up to
date, as above) and hands Dalamud them all as one list, cached for about ten minutes. This plugin is
already in its `PLUGIN_REPOS`: until this repository exists and has a release, the site lists only
Housing Studio. Players change nothing.

## 3. Install it in the game

Players (and you, for testing):

1. In game, type `/xlsettings` (or Dalamud Settings from the system menu).
2. **Experimental** tab → **Custom Plugin Repositories** → paste
   `https://www.kokos-tools.com/dalamud/repo.json` → press **+**
   → make sure it's ticked (enabled) → **Save and Close**. (If you added this plugin's own
   `raw.githubusercontent.com/…/repo.json` earlier, remove that one, or Dalamud lists the plugin twice.)
3. `/xlplugins` → **All Plugins** → search **Raid Planner** → **Install**.
4. `/raidplan` opens it.

For testing a build before releasing: Dalamud Settings → Experimental → **Dev Plugin Locations**, add
the full path to `RaidPlanner/bin/x64/Debug/RaidPlanner.dll`, then enable it under Dev Tools →
Installed Dev Plugins.

## 4. Things to know

- **Not vetted.** Custom repositories aren't reviewed by goatcorp (Dalamud's maintainers). Dalamud
  shows a warning when you add one; players install at their own trust. Say plainly in the repo's
  README what the plugin does.
- **Square Enix's terms of service.** Any third-party tool is against them. This one only reads (it
  never acts in the game), but it reads network messages through hooks, and it's new; it lives in
  Koko's custom repository alongside Housing Studio's. Don't talk about it in the game.
- **After every game patch** the API level may go up and the game's code moves:
  - When Dalamud releases a new API (e.g. 16), update `Dalamud.NET.Sdk/15.0.0` in
    `RaidPlanner.csproj` to the new SDK version, fix what the compiler flags, and release; the
    workflow fills `DalamudApiLevel` from the build. Until then Dalamud won't load the old plugin.
  - The plugin uses no byte patterns of its own: its three hooks take FFXIVClientStructs' addresses
    (action effects, actor control, map effects), which come with each Dalamud release. If one
    can't be made, the plugin records without it and Settings → Recording says so.
  - During Dalamud's testing period for a patch, `stg/latest.zip` (see `DALAMUD_ZIP` in
    `release.yml`) has the next API, for building ahead of time.
- **Submitting to the official repository**: see dalamud.dev's submission guide, and its AI usage
  policy (this code was written with AI assistance and would need disclosing).
