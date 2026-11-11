# Raid Planner (Dalamud plugin): the details

How the plugin works, where things are, and why. The site's side (turning a pull into a plan,
the account link, the encounter data) is in kokos-tools' DETAILS.md, under Raid Planner → _From
the game: the in-game plugin_.

## The idea

The plugin only writes down what happened; everything that turns it into steps is on the site
(`src/features/raid-planner/model/recording.ts`). So the plan-making can get better without a
plugin release, an old recording can be made into a plan again, and the plugin stays small and
quiet: it never acts in the game. The other way round, it shows your plans in the game (Plans,
below), and says how far each pull got for the site's progress tracker.

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
  party stands_ is on. Since 0.3.0 with their HP then (the enemies' `CurrentHp`, the party's
  `CharacterData.Health`), by the same rule; each enemy's max HP (the most seen) and each party
  member's as the pull began (the party list's) are kept with them.
- **Waymarks**, once a second (`MarkingController.FieldMarkers`), recorded when they change.
- **Statuses**, from each party member's and (since 0.3.0) each enemy's own status list (the
  `BattleChara.StatusManager` field, never the virtual `GetStatusManager()`; 60 slots read in
  place). Each (who it's on, status, whose) is classed once, when it's new (only then is its source
  looked into): an enemy's, a helper's or no one's (the environment) is a `status` event, on the
  party as before and since 0.3.0 on an enemy too (a boss's buff, a phase's marker: `on` its actor
  id); the party's (a member's, or their pet's or summon's, credited to the member) is a row of the
  combat block's `st` (below), with the action that put it on when the parser saw it go on; anyone
  else's (another player's, another party's pet's) is only remembered, so it isn't looked at again.
  One already on is checked each frame: its stacks (the status's `Param`) changing, or its time left
  going up by more than half a second (put on again), is recorded again, a `status` event with
  `re` (or another `st` gain); a self-cast status read with no time yet (the game shows −X until it
  confirms it) isn't a refresh when its time appears. One gone from someone whose list was read is
  recorded as gone. The names and icons come from the game's Status sheet, once each.
- **The party's casts** (since 0.3.0): each party member's cast bar (`GetCastInfo`), recorded once
  as it begins, back-dated, with its cast time; an instant isn't one. It's cancelled (`x`) unless
  their action goes off before their next cast or within a second and a half of its end (still
  casting as the pull ends isn't cancelled).
- **Deaths**: a party member's HP going to 0 (down already when the pull starts isn't one), with the
  last enemy action that hit them within 3 seconds (from the hits, below): since 0.3.0 one that did
  something (damage, or an instant death; a dodge or a hit they were invulnerable to isn't a cause),
  or the bleed whose tick did it (its status, `byStatus`; a tick that names no status isn't one).
  Of those, the last that could kill comes first (an instant death, or as much as the HP they had
  before it): the game takes a hit's damage off a moment after it says it landed, so a bleed's small
  tick can come between a lethal hit and the HP reaching 0, and that tick isn't the cause.
- **Targetability**: an enemy becoming targetable (the first time too, so the site sees a new boss
  come in), and one that has been targetable going untargetable (dead, gone, or a phase change);
  one that leaves the game is untargetable. One never targetable (a helper) isn't recorded. The
  pull's furthest phase counts one more each time every enemy that had been targetable was gone for
  8 seconds or more (`PhaseGap`) and then one became targetable, again or for the first time (a new
  boss). A shorter while away (a boss jumping off for a mechanic) isn't a phase. It's the site's
  rule (`phaseStarts`, `PHASE_GAP`), so the two count the same.

And from three hooks, on while a pull goes and (since 0.3.0) whenever the meter wants them
(FFXIVClientStructs' addresses, kept current with each Dalamud release; each detour calls the game's
own function first and changes nothing):

- **`ActionEffectHandler.Receive`**: an enemy's action landing, with where the caster stood, which
  way the action faced (the effect header's own `RotationInt`, 0 to 65535 for −π to π, as BossMod
  reads it; before 0.2.1 the caster's facing as the message was handled, which is wrong for a boss
  that turns as soon as a cone is snapshotted and south for a caster already gone), where the hit
  was centred, and the party members it hit. This is how a stack is told from
  a spread, and how the many mechanics with no cast bar (the game's invisible helpers resolving a
  head marker, a chain of exaflares) are seen at all. Since 0.3.0 with what it did to each of them
  (`dmg`, `fx`, `hp`: the damage, the hit's flags, their HP as the message came, read in the detour:
  the game takes the HP off a moment later). Every other action concerning the party goes to the
  combat parser (the meter's numbers, below): the party's own (and their pets' and limit breaks'),
  into the combat block's `fx`. The message is read into one buffer kept for it, most messages
  stop at the first question (the party's, or landing on the party?), and HP is read (the game's own
  `CharacterManager.LookupBattleCharaByEntityId`) only for a party member, someone the party heals
  and a lifesteal's caster.
- **`PacketDispatcher.HandleActorControlPacket`**, category 34: a head marker over someone (its
  icon number, read after the game has unscrambled it, as BossMod reads it); since 0.3.0 category
  23, a DoT's or HoT's tick (its target, status, 3 damage or 4 a heal, amount and source, in the
  order of FFXIVClientStructs' `StatusManager.ProcessHotDot`), to the parser: the combat block's
  `tick`, and a bleed's status as a death's cause.
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
combatant or a part of one with no owner: pets and summons are the party's own). In a field
operation's open zone (`GameData.IsFieldZone`: the TerritoryType sheet's TerritoryIntendedUse 41
Eureka, 48 the Bozjan Southern Front and Zadnor, 61 Occult Crescent's South and North Horn) the
object table also holds mobs other players are fighting and the next CE's or FATE's enemies, so
since 0.2.1 only the party's own fight is recorded there (`FightRecorder.Engaged`): an enemy is
written down from the frame it targets one of the party, one of the party targeted it or was
tethered to it the frame before, it's casting at one of the party or tethered to one, or it can't
be targeted (a helper casting the boss's mechanics, a boss away for a moment) and is within 70
yalms of you (across the biggest CE or Forked Tower arena); or when an action of its hits one of
the party, or comes from within 70 yalms of you (`OnHit`). Once in, it stays in. Everywhere else
(trials, raids, dungeons, Delubrum Reginae) every enemy is recorded, as before: there everything is
the fight, and a helper whose attack everyone dodged must not be lost. The site does the same for
recordings made before (its `fightOf`). When the pull ends they're sorted: the targetable
ones with the most HP (and any with half as much or more) are bosses, the other targetable ones
adds, and those never targetable helpers (only their attacks are kept, not where they stood).
The party is the party list (just you alone), each given a spot from their job's role
(`Core/Roles.cs`): tanks MT then OT, healers H1 then H2, melee M1 then M2, ranged R1 then R2, in
the list's order; a fourth melee takes a free ranged spot (and the other way round), past eight
P9, P10…

Each action's data is looked up once from the game's Action sheet (`Game/GameData.cs`): name,
cast type, reach, width, floor marker path and cast time, and since 0.3.0 its ActionCategory and
base recast, and kept in the recording: the enemies' actions, and the party's, their pets' and
limit breaks' (the combat block's). In a field operation's open zone the duty says so (`field`), so
the site never applies an encounter's data there.

**The combat block** (since 0.3.0; `Core/CombatLog.cs`, filled from the parser by
`FightRecorder.RecordingSink`): the party's own fight, beside the events (it never counts towards
their cap), each table at most 200,000 rows: `fx`, each of the party's action effects (damage,
heals with the recipient's HP before, a shield put on, a status put on with nothing else; pets'
credited to their owner and flagged, a limit break flagged, a reflection credited to the tank it
came from, a lifesteal on its caster); `tick`, DoT and HoT ticks (on the party, the party's, and on
an enemy in the recording from no one the game named; another player's on an enemy isn't kept);
`cast`, the party's casts; `st`, the statuses the party put on anyone (buffs, DoTs, Reprisal on the
boss). About 15 to 25 KB a minute in an eight-player raid.

At most 20,000 events and 4,800 frames are kept. A typical 20-minute pull is about 1.4 MB, and
about as much again with its combat block; one at the caps can be 4 to 6 MB, which is sent thinned
(see Sending).

## The file (`Core/Recording.cs`)

JSON, format `kokos-raid-recording`, version 1, `.raidrec.json`. World coordinates as the game
has them (x east, z south, yalms), facing in radians (0 south, a quarter turn east); positions to
2 decimals, facing to 3. Top level: `plugin`, `recordedAt`, `duration`, `outcome`, `pull`, `duty`
(`name`, `territory`, `content` = ContentFinderCondition id), `partyPositions`, `party`
(`id` 1…, `slot`, `job`, `role`), `actors` (`id` 101…, `name`, `kind` boss/add/helper, `hitbox`,
`dataId`), `actions` (by id: `name`, `castType`, `range`, `width`, `omen`, `cast`), `statuses`
(by id: `name`, `icon`; since 0.2.0), `events` in time order (`cast`, `hit`, `marker`, `tether` /
`untether`, `waymarks`, `mapfx`, and since 0.2.0 `status`, `death`, `target`; each with `t`,
seconds from the pull) and `frames` (`t`, `p`: by id, `[x, z, facing]`). `Recording.Parse` reads
one back; the tests check a pull goes through its file unchanged. Still version 1: older readers
leave out what they don't know.

The new events (the site's contract, §1):

- `{ "k": "status", "t", "on": partyId, "id": statusId, "src": actorId | 0, "dur": seconds, "gain": bool }`:
  `dur` is the time left when gained (0 without a timer, and when it goes).
- `{ "k": "death", "t", "who": partyId, "by": actionId | 0, "from": actorId | 0 }`.
- `{ "k": "target", "t", "actor": actorId, "on": bool }`.

Since 0.3.0 (round three, the site's contract §2; still version 1, every key optional and left out
when it has nothing, so older readers read on as before):

- `duty.field: true` in a field operation's open zone.
- `party[].maxHp` (as the pull began) and `actors[].maxHp` (the most seen).
- `actions{}` lists the party's actions too, with `cat` (ActionCategory: 9 a limit break, 15 PvP's)
  and `recast` (seconds); `statuses{}` the party's statuses too.
- `hit`: `dmg`, `fx` and `hp`, each as long as `tgts`: the damage each took (0 none), the hit's
  flags (1 crit, 2 direct hit, 4 blocked, 8 parried, 16 fully resisted, 32 invulnerable, 64 instant
  death, 128 dodged: the game's one kind of miss) and their HP just before (−1 unknown).
- frames: `p[id] = [x, z, facing, hp]`.
- `status`: `n` (its stacks or param; left out when 0) and `re: true` for one already on put on again
  or changing stacks (`gain` stays true for older readers; not a new gain); `on` can be an actor.
- `death`: `byStatus: true` when `by` is a status (a lethal tick).
- `combat` (above): `{ "v": 1, "fx": { "t", "a", "o", "r", "v", "k", "h" }, "tick": { "t", "on", "src",
  "s", "v", "heal" }, "cast": { "t", "src", "a", "dur", "x" }, "st": { "t", "on", "src", "s", "a", "dur",
  "n", "gain" }, "thinned": [ ... ] }`: columns of equal length; `t` in centiseconds from the row
  before (the first from the pull; written in time order, so never below 0); `k` the kind in its low
  3 bits (1 damage, 2 heal, 3 shield, 4 status only) and flags above (8 crit, 16 direct hit, 32 a
  pet's, 64 limit break, 128 reflected, 256 on the caster); `o` the party member credited; `r` the
  recipient (party or actor id, 0 not in the recording); `h` the recipient's HP before a heal (−1
  otherwise); a tick's `src` the party member credited, an enemy's actor id for a bleed, 0 when the
  game didn't say; `x` 1 for a cast cancelled; `gain` 1 or 0. A table dropped to fit the site's limit
  is left out and named in `thinned`.

`Recording.Parse` reads all of it back leniently: a hit's lists not as long as its targets, or a
combat table whose columns aren't all arrays of the same length, are left out; a value that isn't a
number reads as 0; a delta below 0 as 0.

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

## Talking to the site (`Core/SiteRpc.cs`)

Every call goes the one way: `SiteRpc` posts to one of the site's `rp_plugin_…` database functions
(no other name is called) with the publishable key, the token as `p_token` in the body (never in
the address or a header, never logged), a wait that runs out (15 seconds for the small calls), and
`SiteSession`'s stop when the plugin unloads. A refusal is shown in the site's words only when its
hint says they're written for players (`token`, `off`, `rate`, `size`, `recording`, `full`,
`plan`); anything else becomes a short line on what happened. `SiteSession` (made once, in
`Plugin.cs`) holds the one HttpClient (an answer bigger than 8 MB isn't read) and runs every call
on a worker; answers come back to the
framework thread through `RunOnFrameworkThread`, and are dropped when the account changed meanwhile
or the plugin unloaded. The functions, and their limits on the site (per person, across tokens):

| Function | For | Limit |
| --- | --- | --- |
| `rp_plugin_send` | a pull's recording | the site's own, by the hour |
| `rp_plugin_plans` / `rp_plugin_plan` | your plans, one plan | 240 an hour together |
| `rp_plugin_live` | the step you're on | calls within 200 ms of the last are ignored |
| `rp_plugin_pull` | a pull's summary | 120 an hour |
| `rp_plugin_roster` | your party's names | replaces your roster |

`Core/Pacing.cs` has the two pacers: `Backoff` (5 seconds doubling to 5 minutes, say, back to none
after a success) and `LiveShare` (below).

## Sending (`Core/SiteLink.cs`, `UI/Views/Send.cs`)

As Housing Studio's plugin sends a house: the token from the site (`rpp_` and 43 letters, pasted
in Settings → Account, kept masked, never logged), and a POST to the site's database function
`rp_plugin_send` with the site's publishable key, the token in the body. Its answer is the pull's
id there (the window then offers _Open it_, `/raid-planner?from-game=<id>`), or a refusal in the
site's own words with a hint: `token` (link again), `recording`, `size`, `off`, `rate`, `full`;
any other message (the database's own) isn't shown, only a word on what happened.

The site takes 5 MB since 0.3.0 (3 MB before; its migration raised it for the combat block),
counted as the database writes the JSON out (`SiteLink.ServerSize`: a space after every colon and
comma, about a sixth more than the file). A pull bigger than that is sent with its places kept less
often (every second, then every two, then four), then the enemies' places only, then none; then
without the combat block's casts, then without its ticks too (never its effects or statuses), the
block saying what it lost (`combat.thinned`); and the window says it was thinned. The file itself
is left whole, and goes into the request as it's written (checked as it's copied, never read into a
tree of nodes: a big pull's would be a hundred MB for the game's process to collect). Each
send carries the pull's own id (`p_client_id`, from its file and when it was recorded), so a send
that timed out after it arrived, sent again, is kept once; a site that doesn't take the id yet
gets the send again without it. The send reads the file and posts from a worker; it waits half a
minute and a quarter more for each MB, and stops when the plugin unloads (the list isn't touched
after).

With _Send each pull_ on (and linked), each kept pull is sent by itself as it's kept, the same way
(a failure is said in chat); `/raidplan send last` sends the newest kept pull and says in chat how
it went.

## Progress and your roster (`Game/Uploads.cs`, `Core/PullSummary.cs`)

With _Track my progress_ on (the default; it does nothing until you're linked), each kept pull's
summary goes to `rp_plugin_pull` with the pull's own id (`SiteLink.ClientId`, so a repeat is kept
once): `{ content, territory, duty, pull, recordedAt, duration, outcome, reached, phase, deaths }`,
where `reached` is the length, `phase` the furthest phase (from the builder's count, above) and
`deaths` at most 48 of `{ t, slot, job, by }` (`by` the action's name, or a lethal tick's status's,
or null). Since 0.3.0 also (`Core/PullStats.cs`, the site's contract §3):

- `boss_hp`: the HP % left (two decimals) of the fight's main boss at the end: of the bosses, the
  one targetable last (the most max HP among those), from the last frame with its HP; 0 on a clear,
  null when it isn't known.
- `phases`: when each phase began, whole seconds, the first 0 (the site's `phaseStarts`), as the
  recorder counted them while it recorded (the same count as `phase`, even past the events' cap).
- `stats` (at most 6 KB): `{ v: 1, slots, lb, hits, debuffs, names }`. `slots`, the party in the
  recording's order (the party list's), each with its spot: `dmg` (their damage, their pets' and DoTs' included; a tick the game sent without saying
  whose is shared between the party's DoTs on the target then, as the meter does), `heal` (what their
  heals restored: the heal less what went past the HP missing then; a HoT's from the frames' HP),
  `over`, `taken` (enemy hits and bleeds), `crit` and `dh` (of their direct hits, 0–1), `active`
  (seconds from their first damage to their last), `deaths` (not counting the wipe: a wiped pull's
  deaths within 6 seconds of its last), `dd` and `vuln` (damage downs and vulnerability ups gained,
  with their stacks: a new one counts its stacks, one put on again the stacks it gained or 1).
  `lb`, the limit break's damage. `hits`, the enemies' hits on the party (autos left out, as the
  timeline leaves them out; 400 at most) as `[deciseconds, action, slot mask, damage]`, the mask's
  bit i the i-th of `slots`, only those it landed on (a miss, a dodge or an invulnerable target isn't
  in it, as the site reads a recording's hits; a hit that landed on no one isn't sent); `debuffs`, the damage downs and vulnerability ups as they were gained
  (200 at most) as `[deciseconds, status, slot mask]`; `names`, those actions' and statuses' names
  by id. Over 6 KB, the latest hits go first, then the latest debuffs. Damage downs and
  vulnerability ups are the statuses so named on the game's Status sheet in English
  (`GameData.FailKinds`: every row named exactly "Damage Down" or "Vulnerability Up", as the site's
  `model/debuffs.ts` lists them; "Physical Vulnerability Up" and "Magic Vulnerability Up" aren't
  failures: tankbusters put them on the tanks on purpose).

No names. The
summary is made on the worker that writes the pull, so it's there whatever happens to the file.
They wait in a queue of 20 (`SummaryQueue`; past that the oldest goes), sent one at a time; with no
connection, the site down or a `rate` refusal, the same one is tried again later (30 seconds doubling
to 15 minutes); a refused token empties the queue; any other refusal drops that one. The one on its
way is known by its client id: its answer takes it off only if it's still first, since the oldest
can be dropped for room while its call is out. The queue is only in memory: what's waiting when the
game closes isn't sent.

_Send my party to my roster_ (Settings → Plans) sends `rp_plugin_roster` your party's names by spot
(`PartyRoster`: the recording's spots from `Core/Roles.cs`, the eight only, names cut to 40), which
replaces your roster on the site. It's the only call with names in it, and only when you press it.
It's refused with fewer than 2 names ("Your party can't be read here; try in the duty."): outside a
duty a cross-world party isn't in the game's party list, and sending what's read (just you) would
wipe the roster on the site down to one name.

## Plans (`Game/PlanService.cs`, `Core/Plan.cs`, `Core/CueFollower.cs`, `UI/PlanWindow.cs`, `UI/PlanBoard.cs`)

A plan made on the site from a pull carries `game` (the site's contract, §2): the duty's
ContentFinderCondition (`content`), and `cues`, each step's first mechanic (`action`) and when it
went off in that pull (`t`, seconds from the pull, as the site times a step: the cast's end or its
first hit). A cue names its step by the phase's `id` (`phase`): steps can be moved or deleted in the
editor afterwards, so a cue goes to the step with that id now, and is left out when no step has it
(that step was deleted). Only a cue with no `phase` at all goes by its `step` number, when that's in
range.

**Which plan.** Entering a duty (bound by duty, with a ContentFinderCondition), `PlanService` reads
your list of plans (`rp_plugin_plans`, kept for 3 minutes; read again when older, or by hand; after
a `rate` refusal or no answer it waits a minute, doubling to 15, before reading it by itself), then
picks the one you last picked for that duty, else your newest whose `content` is the duty's, and
reads it (`rp_plugin_plan`; the last 8 read are kept by id and `updated_at`, and read again only
when the list says it changed). Then it says once in chat "You have a plan for this duty: …" and,
with _Show my plan in duties_ on, opens the window (closed again when you leave, if it opened it).
The window's list button picks any of your plans, for any duty; picked in a duty, it's remembered
as that duty's (in the settings, the last 64 duties). Each plan asked for is numbered, and only the
newest one's answer is shown: a slower answer to an earlier pick is kept for later, never shown over
it (and the list being read again doesn't re-read the plan shown while another is being opened). A
plan that couldn't be read is remembered by id, so Try again reads that plan again (the list
otherwise). The same plan read again (it changed on the site, or picked again) keeps its step and,
mid-pull, what's been said and matched (`CueFollower.CarryFrom`), so nothing is said twice.

**Reading it** (`PlanJson`) trusts nothing: anything missing, of the wrong kind or not a finite
number is left out or given its usual value, text is cut short (names 200, callouts 200, notes
4,000, labels 40), coordinates are kept within ±5,000, and there are caps: 200 plans, 400 steps, 600
objects a step, 500 cues, 16 arena outlines of 512 points, 256 points a pen stroke. A plan without an
id or steps isn't one. Since 0.3.0 it reads each step's `callout` (what a raid lead says: the plan
window's big words and the chat line, `PlanStep.Big` falling back to the step's name without its
"P2 · "), the board's `order`, `reach`, `spacing`, `dashed`, `noIcon` and a pen's `points`, the
arena's `yalms`, `edge` (wall, death, drop) and `grid`, and a list entry's `steps` when the site
sends one. The phase a step is in ("P2") comes from the steps' names as the site's timeline reads
them (`PlanJson.Phases`: "P2 · …" or "Phase 2: …" starts one; the steps before the first are P1).

**Following a pull** (`CueFollower`, while a pull is recorded or followed and the plan is for this
duty): a pull starting goes back to the first step. A cast beginning (from the recorder's cast
reading) whose action is a cue's moves to that cue's step, as does a hit landing for an action that
came with no cast: of the cues with that action not matched yet, the one nearest where it was
expected (counted from the last cue matched), if within 12 seconds of it. A match that would pass
over an earlier cue not due yet (expected more than 5 seconds on) isn't taken: an action used again
before its own step (the same attack in an earlier step) is left to the clock, which catches up
instead. Nor does the action just matched take a later cue of its own that's more than 5 seconds
off (an exaflare's chain split into two steps). After any match its action is quiet: its hits for
7 s (a cast's own hits, a hit-only chain's next ones), and casts of it going off within 1 s of it
(several helpers casting together). A match is the truth, so it can move back. Between those, the
plan moves ahead by the cues' times, counted from the last cue matched, 2 seconds early, and only
forward. Until a cue has been matched in the pull, though, the clock doesn't move it and nothing is
called out: a trash pull in a duty with several (a dungeon, an alliance raid) stays on the first
step, quiet. The recorder tells it through four
events (`PullStarted`, `CastBegan`, `HitLanded`, `PullEnded`), each a null check on its hot path,
with a listener that throws logged and never let into the recording or the game.

With _Record fights by themselves_ off (or in a duty it doesn't record), a pull is still followed
when the plan window is open on a plan for this duty on the frame combat begins
(`FightRecorder.FollowWanted`; a plan opened or read mid-fight waits for the next pull, rather than
starting from 0 there): the recorder runs follow-only, with the same pull detection, hooks and
events, but the pull is never kept, sent or summed up (nothing is written, even on unloading). The
server info bar doesn't show it, the main window's tag says "Following your plan · not recorded"
(or "… · recording starts next pull" when recording was turned on meanwhile: the pull isn't
switched mid-way), and `/raidplan stop` and Stop only end a recorded pull.

**Saying each step in chat** (_Say each step in chat_ on; `Callouts` in the config): 5 seconds before
a step's first cue (by the same clock), or as its cast begins when that's later, its name and its
callout go to chat (`PlanJson.ChatLine`: "Exaflares: middle, step through the first."; the name alone
when the step has no callout; before 0.3.0 the first line of its notes) with the "Raid Planner" tag,
and the chat's soft sound (as `<se.1>`, `UIGlobals.PlayChatSoundEffect`, only
when the game's function for it was found). Each step once a pull; one the clock has passed by
3 seconds isn't said late.

**Sharing your step** (_Share my step with the site_, off): each step change sends
`rp_plugin_live` (`p_plan`; `p_step`, the phase's index from 0; and `p_phase`, its id, which the
site prefers), paced by `LiveShare`: sent once the step has held for 0.6 s (a trailing debounce: a
newer change starts the wait again, and the last one always goes, after a call on its way or a
failure too), one call at a time, only when it differs from what was last sent, and after a failure
not again for 5 seconds, doubling to 5 minutes. Each call is numbered: unlinking, linking again or
turning sharing off forgets the call on its way, and its late answer is ignored, so it can't hold
sharing up.

**The window** (`PlanWindow`, a second Dalamud window, 340×520 at first, 240×300 at the least):
Dalamud's title bar, then 10px down and 14 in. A header row 32 tall: the eyebrow as Present writes
it, in mono capitals ("13 / 19 · 2:44 · P2", the step's number two figures and in ember; the step's
written time and its phase when it has them), and two 32px square quiet icon buttons with 8px
corners: the plans list (washed white while its menu is open) and the lock (ember-washed when
locked; quiet and open when not; there even when not linked). Locked (`PlanWindowLocked`, also
Settings → Plan window → _Lock it where it is_), Dalamud's own moving and resizing are off and a
click doesn't bring it to the front. It never takes focus as it appears, Escape doesn't close it,
and it makes no window sounds. Under the header the step's callout big (Archivo Expanded 800 at
22px in capitals, lines 1.02 apart, three at most), its name under it (13px, muted) when the
callout isn't just the name; while a pull moves it, a slim cast bar "Following the pull" with the
pull's clock and "next in 16 s", filling to the next step by the plan's own clock
(`PlanService.PlanClock`, the follower's time once a cue has matched; `StepProgress`); then the
board, a square as big as leaves three lines for the notes (110 at the least); the notes (13px,
"- " lines as "• ", wrapped once per width, only the lines in view drawn), fading at the foot when
there's more, with the scrollbar; and at the foot 32px square previous and next round the count in
mono. Another plan on its way runs a slim cast bar under the header and keeps the old step until
it's in; one that couldn't be read says so in red with a quiet retry. At its smallest (under 270
wide or 400 tall) the callout is 16px, the board 110 and plain (no bezel), and the notes say "Notes:
2 lines, make it taller" instead of vanishing. With no plan to show, the Boss says why over a small
callout, one line and one button (`EmptyState`): not linked (asleep in a dashed ring; _Link your
account_, the telegraph, opens Settings → Account), reading (the party in a row over a running cast
bar), gone wrong (awake; the error's first sentence as the callout, the rest under it; _Try again_
outlined), no plans (asleep; _Open the Raid Planner_ outlined), no plan for this duty (its head
tilted; _Pick a plan_, the telegraph, opens the menu), not in a duty (_Pick a plan_). The plans menu
is the site's menu (raised #18181d, a white hairline at 12%, 14px corners, its shadow faked with
rings): this duty's plans first under an eyebrow ("This duty · …"), the one shown with a ready
tick and the others with the map, then "Other duties" with each plan's duty under its name; every
row keeps a 20px slot at the left so the names line up, "19 steps" in mono on the right when it's
known (the plan shown; others when the site's list says); _Read them again_ at the foot. What a step
draws is made once, when the plan or step changes (`StepView`). Drawing catches anything that goes
wrong, logs it now and then, and closes what it opened.

**The board** (`PlanBoard`, `BoardArt`): the board's own language, as the mock-ups draw it
(assets/board.js). The site's board units (±500, the arena within ±420, y down, rotation in degrees
from north, clockwise) to a square; the marks (rims, chevrons, badges) are sized by the arena's
yalms. The arena: its floor (#1b1c21, or its colour) lit a little in the middle, its grid (radial or
square, faint), its edge (a #3a3b45 wall, an ember death wall with a glow, or a dashed drop), and for
a round arena the bezel (a tick every 15°, longer at 45° and the cardinals) and the compass letters
outside it. A custom arena's outlines are drawn with a floor where they're convex enough. Telegraphs
(circles, donuts, cones in convex parts of 90°, rectangles, crosses, proteans): a soft fill (20 to
25%), a glow inside the rim (three faint rings) and a crisp hot rim; a later hit in a run (`order`
2 and on) hollow with a dashed rim, numbered with the board's badge. Stacks with their chevrons in,
spreads with their triangles out, defamations, tankbusters with the shield (four triangles for a
cleave), shared busters, towers (the soak fill, a dot a soaker or a tank's shield), proximity rings,
puddles, landmines, safe spots, exaflares (the first hit and the ones to come, hollow and dashed,
with a chevron the way it walks), gazes (the eye and its rays), knockbacks and draw-ins (rings of
chevrons out or in), knock-ups, side knockbacks, rotations, checkerboards, line of sight (a pillar
and the green shadow behind it), pen strokes, tethers and arrows over a dark line, lines (dashed
when set), text with a dark edge; enemies as the Boss's shield and face in their colour (a boss with
its hitbox ring, its back quarter dashed, and its facing triangle); players as spots on a dark halo,
you (your spot in the party now, from the party list and `Core/Roles.cs`) ringed white under the
reticle; waymarks as their lettered circle or numbered square; head markers (limit cut dots,
PlayStation shapes, signs). At most 160 objects (players, waymarks and enemies kept first; the
tooltip over the board says how many weren't drawn), in the site's layers, clipped to the board.

## The windows' look (`UI/Kit/`)

Since 0.3.0 every window is drawn in the planner's Ready Check (the site's
`src/features/raid-planner/styles/ready.css`, the mock-ups in kokos-tools'
`docs/proposals/raid-planner/mockup/pages/plugin.html` and `meter.html`), under Dalamud's own title
bar (only its colour, #0a0a0c, is the plugin's: its font and buttons stay Dalamud's, so their hit
boxes do). Every size, colour and font is in the kit: `Palette` (the tokens by their CSS names: void
#070708, basalt, slab #111115, raised #18181d, well #09090b, floor; white hairlines at 7, 12 and 20%;
words white #f5f5f6, text #d6d6db, muted #a0a0aa, faint #7c7c87; ember #ff8a3d with its light, deep,
on-ember, wash and line; ready #3ddc84, danger #ff5a5a, warn #ffcf4a; the roles' and intents'
colours; corners 6, 8, 10, 14, 20), `Theme` (heights 40, 32 for dense rows, 46; the motion's 90,
160, 280 and 420ms and its curves, `Model/Ease`; the window's padding), `Fonts`, `Txt` and the parts.

- **The ground**: basalt at 96% (`.dw`, the site's near-solid fallback for its glass: ImGui can't
  blur), a white hairline at 9% round it and 14px corners, a shadow faked with two rings of black
  on the background draw list; a hairline under the title bar.
- **Three voices** (`Fonts`, all through Dalamud's font atlas, at fixed sizes): Archivo Expanded
  ExtraBold (a static instance of Archivo at wdth 125, wght 800, made with fontTools; latin and
  latin-ext; OFL, `Resources/Fonts/OFL-Archivo.txt`) at 13, 16, 17 and 22px for the callout, in
  capitals written by the code; Geist 400 (11–14), 500 (12–14, the site's 550) and 600 (7, 8, 10,
  13–15, the site's 650 and 700); Geist Mono 500 (9–13) and 600 (10–13, 15, 16, 18 and 30 for the
  clock), for everything counted, so clocks hold still. Letter-spacing (eyebrows +0.09em, tags
  +0.05em, intents +0.1em) is drawn glyph by glyph. On a Japanese client Archivo and Geist are
  Dalamud's font (the callout drawn twice a pixel apart for its weight, and not put in capitals); the
  mono stays Geist Mono.
- **Buttons** (`Kit.Button`): 40 tall with 10px corners (32 and 8 in dense rows), 13px words at 550.
  One telegraph a screen (`Btn.Tele`): ember with a hot inner rim, on-ember words at 650; pointed at,
  lighter with a glow of two rings; pressed, a ring goes out and fades over 420ms; held, the shape
  dips to 0.97 (the words stay: text can't scale crisply). The rest outlined (a 20% hairline),
  quiet, danger (Stop, Delete), on (a white wash) or ember-washed (a lock that's locked). Disabled
  at 45%. Focus is a white ring 3px out, for the keyboard.
- **Controls**: the switch (34×20, white 14% with a white knob; on, ember with a dark knob, sliding
  over 160ms), segments (a well, the picked one raised with a hairline), the stepper (40 tall, the
  value in mono), the slider (a 2px track, ember to the value, a white knob with a soft ember ring),
  the field (a 40px well with a 12% hairline: white with a faint ring while you type, red when
  wrong; the caret and selection are ImGui's).
- **Marks**: tags (mono capitals in 22px boxes with 6px corners: ember with a beating dot while
  recording, plain, Wipe red, Clear white, Linked green), the ready tick, spots (round in their
  role's colour on a dark halo), intents (Dodge, Share, Apart, Tanks, Soak, Move, Look, Mark: a word
  in its colour after its shape), the eyebrow, square wells (32px, 8px corners: quiet, ember or
  danger), the reticle (four 9px corner brackets 5px outside what's pointed at, closing in over
  160ms), cast bars (a 6px track, 3 slim, filled with a square-ended gradient from deep to light
  ember, its hot end a 2px line with a glow: ImGui can't round a gradient), links (underlined in a
  faint line; ember-light when pointed at), tabs (words; the one you're on white with the ember cast
  bar under it, filling from the left over 280ms), the facts box (mono capitals over mono values),
  error and warning boxes, the Boss (`Kit/Boss.cs`: the site's enemy shield and face from
  board/enemyArt.ts, flattened and cut into triangles once, `Model/Polygon`, shaded per vertex; asleep
  with lids and z's, awake, tilted).
- **Toasts** (`Frame.Toast`): raised near-solid, 10px corners, a hairline, an icon in its colour
  (it worked green, it didn't red, news ember), at most one action in ember-light ("Open", "Why"), and
  a 2px bar along its foot counting its 2.6 seconds down in its colour; it waits while pointed at and
  fades over its last 0.3 s. It sits at the window's foot (above a footer) in a part of the window of
  its own, so it never paints over another window over this one.
- **The question** (`MainWindow.DrawConfirm`): a window of its own over the plugin's, which dims it
  under the title bar (rgb 4 4 5 at 74%) and takes every click; the site's dialog in its middle
  (raised, 20px corners, a 12% hairline, a shadow faked with rings, its title a small callout, its
  words at 14px, its answers on the right: a destructive one in the danger outline). Escape is No. It
  fades in over 280ms (it doesn't grow: text can't scale crisply).

## The main window (`UI/MainWindow.cs`, `UI/Views/`)

560×800 at first (440×520 at the least), its header 16px down and 20 in, its body scrolling (16
above, 18 below), a footer on the basalt band where a screen has one.

- **Home** (`Views/Home.cs`): the header's state as a tag (`StateNow`): "Rec · pull 12 · 2:31"
  ember with a beating dot while recording; "Waiting for the pull · Example Arena" in a duty that
  records; "Following your plan · not recorded" (or "· recording starts next pull") while a pull is
  only followed; "Recording is off"; "Not in a duty". On its right a quiet _Open the Raid Planner_
  and the Settings button. While recording, the ember box: "Recording · pull 12 tonight" in ember and
  "Started 23:38"; the duty as a small callout; the clock in Geist Mono 600 30px over a cast bar
  running to the plan's length when you have a plan for the duty (named for its step: "Platforms:
  draw-in · step 12 of your plan"; the length is its last cue's time) or to the longest pull kept
  here; what's being recorded in mono (Casts, Markers, the phase reached, Places "8 ×2 / s", or Off);
  _Stop now_ in the danger outline. A pull that wasn't kept says why after a _Not kept_ tag, for
  three minutes. Then **Latest pull** (its age as the site says it): its number as a small callout,
  the outcome as a tag, its length in mono, the duty under it when the header doesn't already say
  it; the facts (Length, Ended, Mechanics, Party "8 places", read from the file once on a worker);
  _What's in it →_; and the send (below). While recording the card is compact, with no telegraph
  (nothing on Home is the one thing to do then): the facts, a sent line or how it went, and dense
  _What's in it_, _Save as file_ and (unsent) _Send_. Then **Earlier pulls** ("Tonight · 10" when
  they're all from this evening, "Today · 10", else "10 pulls"; _All 10 →_ past six): 50px rows, a
  square well, "Pull 10" with its length in mono, how long ago (with the duty when it isn't the
  latest's), the outcome's tag, a ready tick once sent, the chevron; pointed at, a faint fill and the
  reticle. Before the first pull: the Boss asleep in a dashed ring, "Nothing pulled yet" and one
  line, three steps numbered as waymarks (1 red, 2 yellow, 3 blue squares), _Link your account_ (the
  telegraph, until linked) and _Open the Raid Planner_ outlined, and "Recording is off. Turn it on"
  when it is.
- **Sending** (`Views/Send.cs`, the latest pull and a pull's footer): not linked, _Link your account_
  is the telegraph and _Save as file_ outlined, "Link once, and each pull is a button away from being
  a plan."; linked, _Send to Raid Planner_ in the same place. Sending, it dims and holds still, a
  quiet _Stop_ beside it (it cancels the send), and a cast bar "Sending pull 12 · 0.6 MB" runs under
  it. Sent, a ready tick, "Sent. It's waiting in your Raid Planner." (or "Sent, with fewer of the
  party's places: it was too big whole."), _Open in Review_ and a quiet _Send again_ (or _Save as
  file_); sent in an earlier session, one line ("Sent yesterday at 23:52.") with _Open in Review ↗_.
  _Open in Review_ opens the plan for the pull's duty in Review on that pull
  (`/raid-planner/plan/<id>/review?pull=<sent id>`) when the plugin has that duty's plan, else From
  the game (`?from-game=<id>`). Failed, a red box that says what to do and the one button that does
  it (`SendFailure`, from SiteLink's words): the token (_Link again_, to Settings → Account), a plugin
  too old (_Save as file_ until then), busy or no answer (_Try again_), timed out or answered without
  the pull (_Open the Raid Planner_: it may be there already), too big or unreadable (_Save as
  file_), anything else (_Try again_; the site's own words for players are said as it says them).
  Toasts: "Sent to your Raid Planner" (Open), "Couldn't send it" (Why: the pull's screen), "Saved …",
  "Couldn't save it there", "Deleted pull 9", "Linked to…", "Unlinked from…".
- **A pull** (`Views/Pull.cs`): the 40px square back button, the eyebrow ("Pull 12 · tonight 23:42",
  `Format.Day`), the duty as a small callout, Delete as an outlined icon button in danger red at the
  far right (it asks first: "Delete this pull?", saying whether the site has a copy). The facts, then
  the pull to its own scale (a 6px track, a tick a mechanic, the phases the recording saw, `P1`,
  `P2`, from its target events by the recorder's rule, and a skull at each death), then **What's in
  it** ("4 waymarks · 2 tethers · 2 deaths", and "the wipe"): its slab of 42px rows, scrolling
  inside it and fading at its foot: the time in mono on a rail with a hollow dot, the mechanic drawn
  small in a 30px square well as the board draws it (`Glyphs`: circle, donut, cone, line, cross,
  stack, spread, tankbuster; a raidwide as rings; anything else a quiet ring), what it asks
  (Dodge, Share, Apart, Tanks) before its name, "×12" in mono; deaths as red rows with the spot and
  what hit them ("H2 died · Exaflare"; the wipe's own, in a wiped pull's last 6 seconds, left out:
  `Model/PullView`). Reading it, the party in a row over a running cast bar; a file that won't read,
  the Boss awake ("This pull won't open"); nothing in it, asleep ("Nothing went off"). The footnote
  in muted (it's read); the footer has the send with _Send to Raid Planner_ filling the row.
- **Settings** (`Views/Settings.cs`): the square back button, "Settings" as a small callout, the tabs
  _Recording_, _Plan window_, _Meter_, _Account_ over a hairline. Each a slab of rows: the setting's
  name (550 14px) over one muted line, its control at the right (switch, 40px stepper, dense segments
  or button); a sub-row 34 in; a row that can't be used dims its words and control and says why in
  the text colour ("Link your account first: Account", the link going there). **Recording**: _Record
  fights by themselves_, _In every duty_ (a sub-row), _Record where the party stands_, _Keep the
  last_, _Send each pull_ and _Track my progress_ (both waiting for a link), _Show it in the server
  info bar_, _Recordings folder_ · Open; a hook the game hides is said in an amber warning box.
  **Plan window**: _Show my plan in duties_, _Lock it where it is_, _Say each step in chat_ (the
  setting once called Callouts: on the site a callout is the step's big words), _Share my step with
  the site_ and _Send my party to my roster_ (both waiting for a link), _Plan window_ · Show or Hide;
  the roster's result as a sent line or a red box; the note on names. **Meter**: below. **Account**:
  linked, a ready tick, a green _Linked_ tag and the token masked in mono, _Unlink_ dense; not
  linked, the instructions, the 40px well field (the token in mono, masked) lined up with the other
  rows' controls with _Link_ as the telegraph, a bad token said in red with an icon under it, and
  _Get a token_; _The Raid Planner_ · Open; the note on keeping the token. At the foot, the version in
  mono and _What's new_.
- **The server info bar** (`Plugin.cs`): the game's own text: "● Rec 2:31" while recording, "Pull 3
  kept" for 10 seconds after; its tooltip says what a click does ("… Click to open the Raid Planner
  plugin."; it promised a send before 0.3.0). A click opens Home.

## The meter's numbers (`Core/Meter/`, `Game/CombatMeter.cs`)

How the meter (and the recording's combat block, and the summary's `stats`: one parser for all three)
works out who did what. Game-free and tested (`RaidPlanner.Tests/MeterTests.cs`); the game side only
reads the game's messages into plain values.

**The parser** (`CombatParser`, owned by the recorder, fed by the hooks; listeners: the meter's session
and, while a pull is recorded, the recording). Each effect of an action on a target is read as BossMod
and cactbot read them (`Effects.Decode`): its amount is the value, with its third byte when the game
says it has one; types 3, 5 and 6 are damage (5 blocked, 6 parried), 4 a heal (its crit in another
byte than damage's), 1 a miss, 2 fully resisted, 7 (or damage marked immune) invulnerable, 10 and 11
MP (never healing), 14 and 15 a status put on, 51 instant death. An effect comes from the caster,
or from the target when it says so (retaliation: Vengeance's damage back at the boss is the tank's,
flagged reflected), and lands on the target, or on the caster when it says so (lifesteal: Bloodbath's
heal is its user's). A pet's or summon's action is its owner's (the owner from the game), flagged; an
action whose ActionCategory is 9 or 15 is a limit break, flagged. The party is the party list's
entity ids (just you alone), read four times a second. Damage between party members isn't kept,
nor anything between two others; a barrier (a status named on `CombatLists.ShieldNames`, found on the
English Status sheet) is a shield put on, never healing. A tick (actor control 23) goes to the party
member the game names (a pet's owner); one that names no one, on an enemy, is shared evenly between
the party members with a DoT (`CombatLists.DotNames`) put on it within the last minute, and marked
estimated; another player's is never the party's. Nonsense is dropped and counted: a hit or a heal
over five million that isn't a limit break. A heal landing on an enemy (a combatant no one owns that
the game calls hostile) is dropped too, never the party's healing, but isn't nonsense: an enemy that
absorbs an element (as some of Eureka's do) is healed by the party's attacks. A FATE's escort or a
quest's ally isn't an enemy, and healing it is healing.

**Encounters** (`MeterSession`): one starts on the first damage between the party and an enemy, either
way (a DoT's tick never starts one); in combat before it, the meter is _waiting_. The party's heals,
shields and statuses in the second before are folded in. It ends when nobody in the party has been
in combat for _A fight ends after_ (4 seconds unless set), when the whole party is down (a wipe), when
the duty says wiped or completed (said within 15 seconds after it ended, it still sets how), or on
leaving the zone, logging out, or the meter going off. Its length runs from its first hit to its
last damage or death. An encounter during a recorded pull takes the pull's number; else they're
numbered from 1 in each zone. The last _Keep the last_ (20) are kept for the session, newest first.

**The numbers**, for each party member and the limit break (its own row, never anyone's damage; the
party's row is the sum of the rows, the limit break's included):
- **Damage**: every hit of theirs and their pets', blocked and parried ones too, a hit announced
  but never landed too (as in ACT), and their DoTs' ticks. **ENCDPS** is damage over the
  encounter's length (at least a second); **DPS** over their own active time, from their first damage
  to their last (so a player who died isn't marked down for the time they were dead); the limit
  break's DPS is its ENCDPS. A **share** is their damage over the party's, so the column adds to 100.
  Crit and direct hit rates are shares of their direct hits (the game's tick doesn't say whether it
  crit); the max hit is their biggest single one.
- **Healing**: what each heal (and HoT tick) restored: the heal less its **overheal**, what went past
  the HP the target was missing when the heal was announced (it reads a little high when a hit is on
  its way, as ACT's does). HPS is that over the encounter; overheal % is overheal over all healed.
  Shields aren't healing; lifesteal goes to whoever drew it; MP never.
- **Taken**: every enemy hit (or instant death) on them and every damage tick on them (bleeds, the
  boss's DoTs), as the game took it off (mitigation already in); a miss or an invulnerable hit is
  nothing.
- **Deaths**: HP reaching 0 (seen each frame; a raise and another 0 is another death), with a recap:
  their last ten hits, ticks, heals and shields, each with the HP before it, and their max HP, frozen
  at the death; the killing blow is the last hit or tick in the 3 seconds before that could kill (an
  instant death, or as much as the HP before it: the recording's rule, above), else the last hit or
  tick, and the overkill is its amount past the HP before it.
  In a wiped encounter, the deaths within 6 seconds of the last are the wipe, counted against no one.
- **By ability**, for one player and tab: each action (a pet's flagged), a DoT's or HoT's ticks on a
  line of their own (named by the action that put it on, else its status; marked estimated when any
  was shared out), with hits, crit and direct hit rates (none for ticks), and the biggest.

**Phases**: the recording's rule (`PhaseClock`): four times a second during an encounter the enemies'
targetability is read (in a duty every enemy, outside or in a field zone only those the party has
fought), and a new phase begins when every enemy that had been targetable was gone 8 seconds or
more and one is back.

**Pace**: the party's damage since the current phase began, against the compared encounter's (the
best kept of the same duty, or the one just before) at the same second of the same phase: how far
ahead (or behind). None until 10 seconds into a phase, outside a duty, or when the other never
reached this phase or left it (moved on or ended) before this second of it. The **best** is a clear,
else the one that reached the furthest phase, then lasted longest.

**When it stops**: after three nonsense effects the meter turns itself off for the session and says
why (`IMeterSource.Problem`): the game has moved something it reads.

**The game side** (`Game/CombatMeter.cs`): with _Show the meter_ on (in duties, or everywhere), it
listens to the parser and asks the recorder to keep the hooks on; each frame it tells the session the
zone, the recorder's pull, the party (names read once each and kept only in the plugin; HP from the
party list; in combat from the game's own look-up) and whether anyone's in combat. Reading the game
costs little: no allocation per message on the hot path (a test reads an eight-target action twenty
thousand times and allocates nothing), the object table only four times a second during an encounter.

## The meter's windows (`UI/MeterWindow.cs`, `UI/MeterDetailWindow.cs`)

Drawn from `IMeterSource` only (`Core/Meter/MeterModel.cs`; the plugin's is `CombatMeter.Source`);
`Model/MeterView` orders and words what they show. `/raidplan meter preview` shows a made-up fight
(the mock-ups' pull 12, `MeterPreview`) under a _Preview_ tag, to see the windows without one.

- **The meter** (`MeterWindow`, "Meter", 330×382 at first, translucent as Settings says: its
  background rgb 13 13 16 at _Background_, its rows solid, 10px corners). Shown with _Show the meter_
  (or `/raidplan meter`), in duties or everywhere. Its head (36 tall): the fight's clock in mono
  (600 15px) with the ember dot beating while a fight is on (a still faint dot otherwise), a _Wipe_
  or _Clear_ tag when the fight shown is over, the picker ("Pull 12 ⌄", 24 tall) and the lock (24px:
  ember-washed when locked, a pointer when click-through, quiet when open). Locked, it has no title
  bar and can't be moved or sized (its tabs and picker still answer); click-through, the mouse goes
  to the game (`/raidplan meter unlock` or Settings gives it back); open, Dalamud's title bar comes
  back with its menu, its × and the resize grip. Four tabs (27 tall, the ember bar under the one
  you're on): **Damage** (ENCDPS, or DPS in Settings, and the share; pace from 10 s into a phase
  against the picked pull: "PACE · P2 +0:41", a 3px bar with this pull in ember and the other's tick
  in white, "on pull 4 +3.4%"), **Healing** (HPS and overheal %), **Taken** (damage taken and its
  share), **Deaths** (each death's spot, name and job, its time in red, what killed them and how big;
  the wipe's left out; live, "7 alive"). Rows are 25 tall: the spot (18px), the name (or the job
  alone, _Jobs only_), the job in mono, the number in mono 600 13px, its second column, and a 2px bar
  under it to the top player's (grey; yours ember; a death's dimmer, its name muted with a red
  skull). The limit break has its own row under a hairline; the party's number at the foot ("Party ·
  165,000", "… HPS"), with where in the fight, "shields not counted", "incl. bleeds", "kept as pull
  12" or "click-through" on the right. Rows don't grow: a taller window only has room for more; past
  the room they're cut. Before any fight, the Boss asleep, "No fight yet", "It starts on the first
  hit."; in combat before the first hit, "0:00" with "Waiting for the first hit" and the party with
  dashes; between pulls it folds to one line (the last fight's length and tag, "Pull 12 · party
  163,188", a chevron that opens it again), or hides with _Hide out of combat_. Pressing a row opens
  the meter opened up on that player; a death, on its recap.
- **The picker** (the site's menu, 300 wide): the shown fight's duty's fights newest first ("Tonight
  · Example Arena"), the one shown ticked, the live one tagged _Now_, the best (the one that got
  furthest) starred ("Pull 4 · the best", added at the end when it's older), each with its length in
  mono and its outcome's tag; other duties' after; and _Pace against_ (Best, Last, Off) at its foot.
- **Opened up** (`MeterDetailWindow`, "Raid Planner meter", 1180×560 at first; `/raidplan meter
  big`): the four tabs full size with their icons and the deaths counted; the clock, where in the
  fight ("P2 · 0:41 in"), which pull ("Pull 12 · now ⌄"), ENCDPS or DPS, and _Small_ back to the
  meter. A slab with every column (Damage: ENCDPS, DPS, damage, %, crit, DH, max hit, deaths;
  Healing: HPS, healing, over, %, crit, max heal; Taken: taken, a second, %, max hit, deaths), a 34px
  row a player, the picked one on a white wash under the reticle, the limit break, the party's total
  under a stronger rule; beside it the picked player by ability (`IMeterSource.Abilities`), biggest
  first, a DoT's ticks tagged _DoT_ with blank crit and DH, _est._ when the game's tick was shared, a
  slim bar each, the rest summed as "9 more". On **Deaths**, the deaths (the wipe's muted, "The
  wipe") and the picked one's recap: "Odette died at 2:51" as a small callout, the killing blow and
  how far over, then its last hits and heals oldest first (the time to a tenth, what it was with its
  note, the amount red for a hit and green for a heal, the HP before as a bar against their max HP
  (`DeathEntry.MaxHp`; the most the recap saw when it isn't known), amber when low, red at none), the
  killing blow (the line the death names, not always the last) on a red wash. A line under each tab
  says how its numbers are worked out.
- **Settings → Meter**: _Show the meter_ (`/raidplan meter` too) with _Where_ (In duties,
  Everywhere) and _Hide out of combat_ under it; _The number_ (ENCDPS, DPS); _Pets in their owners'
  rows_ and _Limit break on its own row_ (always, as ACT counts them: shown on and dimmed); _Names_
  (Names, Jobs only); _Lock it where it is_ with _Click-through when locked_ under it;
  _Background_ (a slider with its value in mono); _A fight ends after_ (2 to 15 s) and _Keep the
  last_ (5 to 50), read by the meter's session; _Pace against_; _Use ACT's numbers_ (not in this
  version: dimmed, saying so); _Kept fights_ · Clear. When the meter has stopped itself
  (`IMeterSource.Problem`), an amber warning box says why.

## Layout

```
RaidPlanner/
  Plugin.cs, Services.cs, Configuration.cs
  Core/       game-free (tested): the recording, its builder and its combat block (CombatLog), the
              store, spots, timeline, words, the site link and calls (SiteLink, SiteRpc, Pacing),
              plans (Plan, CueFollower), pull summaries and their numbers (PullSummary, PullStats)
              and the roster; Meter/: the combat parser (Effects, CombatParser), the sums (Tally),
              phases (PhaseClock), the meter's session (MeterSession) and its model (MeterModel)
  Game/       the recorder, its hooks, the game's data, the meter's game side (CombatMeter), plans
              in the game (PlanService), summaries and the roster (Uploads)
  UI/         the windows: MainWindow (Views/: Home, Pull, Send, Settings), PlanWindow and its board
              (PlanBoard, BoardArt), MeterWindow and MeterDetailWindow (MeterPreview), Glyphs;
              Kit/ (tokens, fonts, text, controls, marks, the Boss, the frame); Model/ (numbers and
              words, curves, polygons, the screens' states, the meter's and a pull's views, SVG
              paths, dashes), tested
RaidPlanner.Tests/  xunit tests for Core and UI/Model
tools/      the icon (icon.svg → images/icon.png)
docs/       PUBLISHING.md
```

## Commands

`/raidplan` opens the window; `/raidplan settings` opens Settings; `/raidplan stop` ends the pull
being recorded (and keeps it); `/raidplan plan` shows or hides the plan window; `/raidplan next` and
`/raidplan prev` move its step; `/raidplan send last` sends the newest kept pull; `/raidplan meter`
shows or hides the meter, `/raidplan meter unlock` gives it the mouse back (and unlocks it),
`/raidplan meter big` opens it up, `/raidplan meter settings` opens Settings → Meter, `/raidplan meter
preview` shows a made-up fight in it (again to stop).

## Not yet

- Not run in the game yet: everything in `Game/` and every window are compiled against Dalamud 15
  but untested. The 0.3.0 look in particular wants checking in the game: the fonts' sizes and
  baselines (Archivo's line is 1.088 of its size, Geist's 1.3), letter-spacing drawn glyph by glyph,
  the Boss's triangles and shading, the toast's own part of the window over the body, the dialog's
  window under the title bar, the meter's click-through and translucent background, and the glyphs
  "●" (the info bar), "×", "→", "·", "–" and "−".
- The combat parser and the round-three recording (0.3.0) want checking in the game, side by side
  with ACT (or IINACT) on a striking dummy and in one real fight: damage by job within 1% and the
  same crit and direct hit rates; DoTs (BRD, SCH, WHM, SAM, DRG), and two players' DoTs on one dummy
  (does the game send a tick per source, or one summed that names no one? the parser shares the
  latter); a tick's arguments' order (source fourth, damage type fifth, as `ProcessHotDot`); pets
  (SMN, SCH, MCH, DRK's Living Shadow, AST's Earthly Star, NIN's Bunshin) in their owners' rows;
  LB1 to LB3 and the healer's LB3 raise; Vengeance and Damnation to the tank; Bloodbath's heal to its
  user; Lucid Dreaming not healing; Holmgang and Hallowed Ground taking nothing; an opener before
  the pull; HoT overheal; a wipe and a clear each ending one encounter. Also: the enemies' HP in
  frames, a hit's HP before, `CharacterData.InCombat`, statuses' `Param` as stacks and a refresh
  seen by the time left going up, the party's casts (cancelled ones too), the shield and DoT names
  found on the English Status sheet, and the cost of the hooks being on everywhere. And: the game's
  hostile flag (`CharacterData.IsHostile`) set on bosses and adds and not on a FATE's escort (an enemy
  healed is left out, an ally healed is healing), an element-absorbing enemy in Eureka healed by a hit
  (left out, the meter going on), and a death to a big hit with a bleed ticking just after it (the
  hit is the cause, in the recording and the meter's recap).
- Summaries waiting to be sent are lost when the game closes.
- Head marker numbers are kept as the game sends them; the site names them from BossMod's data
  where it can (stack, spread, tankbuster…).

## Release notes

### 0.3.0

- **The planner's own look, in every window.** Ready Check, as the site has it: basalt and white
  hairlines, 40px buttons with 10px corners, one ember telegraph a screen (Send to Raid Planner, or
  Link your account until you've linked), switches on in ember, the site's tabs with the ember bar,
  tags, cast bars for anything that's time, the reticle on what you point at, and three voices
  (Archivo callouts, Geist, Geist Mono for every number, so the recording's clock holds still).
- **Home**: the state as a tag ("Waiting for the pull", not "Ready"), the recording box with its
  clock running to your plan's length, the latest pull with its outcome as a tag and "8 places",
  the night's pulls as rows with a tick once sent, the Boss asleep before the first.
- **Sending says what to do**: a failure is a red box with the one button that fixes it (Link again,
  Try again, Open the Raid Planner, Save as file); a send under way shows a cast bar and can be
  stopped; Open in Review opens your plan in Review on that pull.
- **A pull**: the pull to its own scale (its phases, a skull at each death), each mechanic drawn as
  the board draws it, what it asks before its name, and deaths as rows (the wipe's left out).
- **The plan window** shows the step's callout big (the plugin now reads it), its phase in the
  eyebrow, a slim cast bar to the next step while following, and the board in the board's own
  language: exaflares, gazes, knockbacks, towers and the rest drawn as themselves, the boss as the
  Boss, you under the reticle. Chat says each step's callout ("Exaflares: middle, step through the
  first."); the setting is now _Say each step in chat_, and _Lock it where it is_ is in Settings.
- **The meter**: damage, healing, taken and deaths over the fight, with pace, a picker of the night's
  pulls, a lock and click-through, an opened-up window with every column and each player by ability,
  and a death's recap. Settings → Meter. Its numbers are worked out in the game from the game's own
  messages (the party's hits and heals, DoT and HoT ticks, pets in their owners' rows, the limit
  break on its own), as ACT works them out; it turns itself off rather than show nonsense.
- **Recordings carry the numbers** (still the same format, so older sites read them as before): what
  each enemy hit did to whom and their HP before, everyone's HP twice a second, the party's and the
  bosses' max HP, the stacks and refreshes of statuses, the enemies' own statuses, deaths to a bleed
  or an instant death, whether the zone is a field operation's, and a new combat block with the
  party's own fight (their hits, heals, shields, ticks, casts and the statuses they put on anyone),
  for the site's pull report. A pull too big to send loses its places first, then its casts, then
  its ticks; the site now takes 5 MB.
- **Progress carries more**: each pull's summary has the boss's HP left, when each phase began, and
  each spot's damage, healing, overheal, damage taken, crit and direct hit rates, active time,
  deaths (the wipe's left out), damage downs and vulnerability ups, with the enemies' hits on the
  party, so the site can chart them across every pull.
- Fixed: the Party fact cut to "Where th…"; the info bar's tooltip promising a send; plan names
  zig-zagging in the plans menu; links that didn't look like links; a bad token read like the
  instructions; rows off without saying why, or looking on when off; the token row 4px wider than
  the rest; toasts that looked alike and painted over other windows; footnotes too faint to read;
  notes vanishing in a small plan window; no lock until linked.

### 0.2.1

- **Field zones place right on the site.** In Occult Crescent (its CEs, FATEs and the Forked
  Tower), the Bozjan Southern Front, Zadnor and Eureka, a pull records only the enemies in your
  party's fight, not the mobs other players are fighting nearby or the next CE's enemies, so the
  site's board no longer draws them, makes steps of their attacks or moves the arena off the fight.
- An attack with no cast bar is drawn facing the way it went off (the game's own direction for the
  action), not the way its caster turned a moment later.

### 0.2.0

- **Your plans in the game.** In a duty you've made a plan for, a small window shows it: the step,
  its board and its notes. Put it anywhere and lock it there. During a pull it moves along by
  itself, from the boss's casts and the plan's times, once the first of its mechanics has gone off
  (a trash pull leaves it be). With recording off too, while the window shows the duty's plan as
  the pull begins; that pull isn't kept. Pick any of your plans from its list.
  `/raidplan plan`, `/raidplan next`, `/raidplan prev`.
- **Callouts**: a few seconds before each step's mechanic, its name and first note in chat, with a
  soft sound.
- **Share my step with the site**, so Present can follow the fight (off unless you turn it on).
- **Track my progress**: each kept pull's length, furthest phase and deaths go to the site's
  progress tracker.
- **Send each pull** by itself (off unless you turn it on), and `/raidplan send last`.
- **Send my party to my roster**, from Settings → Plans (in the duty, where the game shows your
  whole party).
- Recordings now have the statuses enemies put on the party, deaths and what caused them, and
  enemies becoming targetable and untargetable (how phases are told apart: every enemy gone 8
  seconds or more, then one back).

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
