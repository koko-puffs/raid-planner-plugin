# Raid Planner (Dalamud plugin): the details

How the plugin works, where things are, and why. The site's side (turning a pull into a plan,
the account link, the encounter data) is in kokos-tools' DETAILS.md, under Raid Planner → _From
the game: the in-game plugin_.

## The idea

The plugin only writes down what happened; everything that turns it into steps is on the site
(`src/features/raid-planner/model/recording.ts`). So the plan-making can get better without a
plugin release, an old recording can be made into a plan again, and the plugin stays small and
quiet: it never acts in the game.

## What's recorded (`Game/FightRecorder.cs`, `Game/GameHooks.cs`, `Core/RecordingBuilder.cs`)

A pull starts when you go into combat in a fight (bound by duty, any of the game's three flags,
in a Duty Finder duty that isn't a dungeon, guildhest, PvP, quest battle, treasure hunt, deep
dungeon, Eureka or Bozja: `GameData.IsFight`, by the ContentType sheet's rows 2, 3, 6, 7, 9, 21,
26, 29; _In every duty_ in Settings records those too) with _Record fights by themselves_ on.
It goes on while you or anyone in your party is in combat (you can be down while they fight on:
still the same pull), and ends when that's been over for 2 seconds, at once when the duty is wiped
or completed (then that's its outcome: `wipe`, `clear`; else `ended`; with the whole party down it
waits up to 15 seconds for the game to call the wipe, which it does at the fade-out, and calls it a
wipe itself after that), when you leave the zone, log out, or press Stop (`/raidplan stop`; it
isn't started again until that combat ends). A pull that ended on its own without the game saying
how is held 10 seconds before it's saved: the game's "duty complete" comes a moment after the
kill's combat ends, and then it's a clear. A pull shorter than 10 seconds, or with no mechanic in
it (a named enemy cast, or a hit that isn't an auto-attack: a trash pack that only swung at the
tank isn't), isn't kept (the window says so). Pulls are numbered from 1 each time you enter a zone.

Unloading the plugin mid-pull (an update, a reload), or while one is held, keeps what was
recorded; the hooks are let go of first. A frame that can't be read is skipped (logged at most
every 10 seconds) and never stops the pull from ending.

Every frame while it goes (on the framework thread):

- **Enemy casts**, from each combatant's own cast info (FFXIVClientStructs `BattleChara.GetCastInfo`):
  recorded once as they begin, back-dated to when they began, with the action, cast time, where
  the caster stood and its facing at the cast, its target (someone in the recording) and where on
  the ground it was aimed.
- **Tethers**, from each party member's and enemy's own (`Character.Vfx.Tethers`): recorded as
  they appear and go.
- **Where everyone is**, twice a second: the enemies always, the party when _Record where the
  party stands_ is on.
- **Waymarks**, once a second (`MarkingController.FieldMarkers`), recorded when they change.

And from three hooks, on only while a pull goes (FFXIVClientStructs' addresses, kept current with
each Dalamud release; each detour calls the game's own function first and changes nothing):

- **`ActionEffectHandler.Receive`**: an enemy's action landing, with where the caster stood and
  faced, where the hit was centred, and the party members it hit. This is how a stack is told from
  a spread, and how the many mechanics with no cast bar (the game's invisible helpers resolving a
  head marker, a chain of exaflares) are seen at all.
- **`PacketDispatcher.HandleActorControlPacket`**, category 34: a head marker over someone (its
  icon number, read after the game has unscrambled it, as BossMod reads it).
- **`PacketDispatcher.HandleMapEffectPacket`**: the arena changing (its index and state, as one
  number: the state in the low half, the second half above it, as ACT logs and BossMod's modules
  compare). The game also sends several at once through three handlers FFXIVClientStructs doesn't
  name; they're found by BossMod's byte pattern (`40 55 41 57 48 83 EC ?? 48 83 B9`, three
  matches) and read with its offsets (4, 8 and 12 effects a message). That's the plugin's only
  byte pattern: if it stops matching after a patch, only batched arena changes are missed
  (Dalamud's log says so). Their hooks take four plain arguments and a result and hand them all on
  untouched, so the game's call is never changed even if a patch changes what the function takes;
  the message is read only while a pull is recorded, and not at all when its count is more than
  the message holds.

A hook that can't be made (the game changed under the plugin) is left out, the rest still
records, and Settings → Recording says what's missing.

Enemies are every combatant battle NPC seen (a hit from one not seen yet counts only if it's a
combatant or a part of one with no owner: pets and summons are the party's own). When the pull ends they're sorted: the targetable
ones with the most HP (and any with half as much or more) are bosses, the other targetable ones
adds, and those never targetable helpers (only their attacks are kept, not where they stood).
The party is the party list (just you alone), each given a spot from their job's role
(`Core/Roles.cs`): tanks MT then OT, healers H1 then H2, melee M1 then M2, ranged R1 then R2, in
the list's order; a fourth melee takes a free ranged spot (and the other way round), past eight
P9, P10…

Each action's data is looked up once from the game's Action sheet (`Game/GameData.cs`): name,
cast type, reach, width, floor marker path and cast time, and kept in the recording.

At most 20,000 events and 4,800 frames are kept. A typical 20-minute pull is about 1.4 MB; one
at the caps can be 4 to 6 MB, which is sent thinned (see Sending).

## The file (`Core/Recording.cs`)

JSON, format `kokos-raid-recording`, version 1, `.raidrec.json`. World coordinates as the game
has them (x east, z south, yalms), facing in radians (0 south, a quarter turn east); positions to
2 decimals, facing to 3. Top level: `plugin`, `recordedAt`, `duration`, `outcome`, `pull`, `duty`
(`name`, `territory`, `content` = ContentFinderCondition id), `partyPositions`, `party`
(`id` 1…, `slot`, `job`, `role`), `actors` (`id` 101…, `name`, `kind` boss/add/helper, `hitbox`,
`dataId`), `actions` (by id: `name`, `castType`, `range`, `width`, `omen`, `cast`), `events` in
time order (`cast`, `hit`, `marker`, `tether` / `untether`, `waymarks`, `mapfx`; each with `t`,
seconds from the pull) and `frames` (`t`, `p`: by id, `[x, z, facing]`). `Recording.Parse` reads
one back; the tests check a pull goes through its file unchanged.

## Kept pulls (`Core/RecordingStore.cs`)

In the plugin's config folder, `recordings/`, a file each (named for when, the duty and the
pull), `index.json` listing them (so the window opens without reading every file; made again
from the files when it's missing or can't be read), and `marks.json`, what can't be made again
from the files: each sent pull's id on the site, and the files that couldn't be deleted yet (held
open by something else: tried again at the next load, never listed again). Every write goes to a
temporary file, flushed to the disk, then put in place, so a crash or a power cut never leaves
half a file. A temporary file found at the load is put in place when it's a whole recording and
its own file is missing or empty (a crash between the flush and the move), else deleted. Names in
the index and the marks count only as plain file names ending `.raidrec.json` (never a folder, a
drive or the index), and unloading waits a few seconds for pulls still being written. A file that can't be read (not a
recording, a duplicate key, a value of the wrong kind) is left out of the list, never the list
itself, and so is an index entry.

Past _Keep the last_, as each new pull is kept, the oldest sent one the site still has goes first
(the site keeps a person's newest 30 sends, so a sent pull older than those counts as unsent here),
and only then the oldest of the rest. Lowering the number in Settings deletes nothing
then and there. The window reads the list without waiting: the store hands out a copy made at
each change, and a pull's file is written outside its lock.

## Sending (`Core/SiteLink.cs`, `UI/Views/Send.cs`)

As Housing Studio's plugin sends a house: the token from the site (`rpp_` and 43 letters, pasted
in Settings → Account, kept masked, never logged), and a POST to the site's database function
`rp_plugin_send` with the site's publishable key, the token in the body. Its answer is the pull's
id there (the window then offers _Open it_, `/raid-planner?from-game=<id>`), or a refusal in the
site's own words with a hint: `token` (link again), `recording`, `size`, `off`, `rate`, `full`;
any other message (the database's own) isn't shown, only a word on what happened.

The site takes 3 MB, counted as the database writes the JSON out (`SiteLink.ServerSize`: a space
after every colon and comma, about a sixth more than the file). A pull bigger than that is sent
with its places kept less often (every second, then every two, then four), then the enemies'
places only, then none, and the window says it was thinned; the file itself is left whole. Each
send carries the pull's own id (`p_client_id`, from its file and when it was recorded), so a send
that timed out after it arrived, sent again, is kept once; a site that doesn't take the id yet
gets the send again without it. The send reads the file and posts from a worker; it waits half a
minute and a quarter more for each MB, and stops when the plugin unloads (the list isn't touched
after).

## The window (`UI/`)

Made from Housing Studio's plugin kit (`UI/Kit/`), in the Raid Planner's look instead of the
studio's: a black window and near-black cards with hairline edges (`#1f1f22`), Geist (600 for
titles, tight), white pills for what goes ahead and outlined pills for the rest, the site's white
switch, its pick-one pill (a soft white fill on the picked one), its round orange icon wells (the
favicon's `#29160a` with `#ff8a3d`), and the planner's orange for what's live (recording) and
what's picked (`UI/Kit/Palette.cs` maps each to the site's CSS). Every screen below Home has the
round back arrow at its top left, as in Housing Studio's plugin.

- **Home** (`Views/Home.cs`): _Raid Planner_ and Settings, and a pill for what it's doing
  ("Recording · pull 3 · 2:31", orange, its dot breathing; "Ready · records the next pull in …";
  "Recording is off"). While recording, a card with the duty, the clock large, the casts and head
  markers so far, and _Stop now_. Then **Latest pull**: the duty (press it for the pull's screen),
  "Pull 3 · Wiped · See what's in it ›", the planner's fact boxes (Length, Mechanics, Party), the
  white **Send to Raid Planner** (_Link your account_ when not linked, which opens Settings →
  Account; _Send again_, outlined, once sent) and **Save as file**, and how the send went under
  them. Then **Earlier pulls** as rows (an icon well, the duty, "Pull 2 · 4:10 · Wiped · today
  20:58", a green _Sent_ chip), six and _Show all_. With none yet: the site's three steps (Go into
  a duty, Fight, Send it), _Link your account_ and _Open the Raid Planner_.
- **A pull** (`Views/Pull.cs`): back, "Pull 3 · today 21:04", the duty, Delete (asks first, and
  says whether the site has a copy). The facts (with how it ended), then **What's in it**: its
  mechanics in order as Present's timeline lists steps (the time, a rail with a dot, the name, ×
  how many, their icons: `UI/Glyphs.cs`, the planner's colours), read from the file on a worker
  (`Core/Timeline.cs`: each enemy cast, and each hit with no cast; the same action again within
  1.5 s is one line; autos left out). The footer has Send and Save, and a line on what Send does.
- **Settings** (`Views/Settings.cs`): back, _Settings_, the tabs **Recording** (the four switches
  and the stepper, and _Recordings folder_ · Open) and **Account** (the token row; _The Raid
  Planner_ · Open), each a card of rows (name, a line, the control; the longer words a tooltip).
  The version and _What's new_ at the foot.
- **The server info bar** (`Plugin.cs`): "● Rec 2:31" while recording, "Pull 3 kept" for 10
  seconds after; click it for the window.

## Layout

```
RaidPlanner/
  Plugin.cs, Services.cs, Configuration.cs
  Core/       game-free: the recording, its builder, the store, spots, timeline, words, site link (tested)
  Game/       the recorder, its hooks, the game's data
  UI/         the window: Kit/ (from Housing Studio's), Views/, Glyphs, Model/ (SVG paths, dashes)
RaidPlanner.Tests/  xunit tests for Core and UI/Model
tools/      the icon (icon.svg → images/icon.png)
docs/       PUBLISHING.md
```

## Not yet

- Not run in the game yet: everything in `Game/` is compiled against Dalamud 15 but untested.
- Head marker numbers are kept as the game sends them; the site names them from BossMod's data
  where it can (stack, spread, tankbuster…).

## Release notes

### 0.1.1

- Only fights are recorded by themselves now: trials, raids, ultimates and criterion dungeons.
  Dungeons and field operations are left out unless you turn on _In every duty_, so trash packs
  can't push out the pulls you keep. A pull of only auto-attacks isn't kept either.
- Unsent pulls are no longer deleted while a sent one could go instead, and lowering _Keep the
  last_ deletes nothing on the spot.
- Going down while the party fights on no longer splits the pull in two.
- A kill whose "duty complete" came a moment late is now saved as a clear.
- Pets' and summons' actions are no longer recorded as the boss's.
- Long pulls that were too big to send are now sent with fewer positions.
- Sending the same pull twice after a timeout keeps it once on the site.
- One unreadable file no longer empties the list, and sent marks survive a lost index.
- The window no longer stalls while a pull is saved, draws only the timeline rows in view, and
  sets the server info bar only when it changes.
- Smaller fixes: the question box no longer outlives the window or takes focus from other
  windows, a send stops when the plugin unloads, and a failing info bar no longer turns its
  setting off for good.

### 0.1.0

The first version: records pulls in duties, keeps them, shows their timeline, sends them to the
Raid Planner or saves them as files.
