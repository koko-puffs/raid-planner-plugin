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
  party stands_ is on.
- **Waymarks**, once a second (`MarkingController.FieldMarkers`), recorded when they change.
- **Statuses on the party** put there by an enemy, a helper or no one (the environment), from each
  party member's own status list (the `BattleChara.StatusManager` field, never the virtual
  `GetStatusManager()`; 60 slots read in place). Each
  (party member, status) is kept in a set: one already known is passed over, so only a new one's
  source is looked at (a party member's or another player's or a pet's isn't recorded, only
  remembered so it isn't looked at again). One gone from a member whose list was read is recorded
  as gone. The names and icons come from the game's Status sheet, once each.
- **Deaths**: a party member's HP going to 0 (down already when the pull starts isn't one), with the
  last enemy action that hit them within 3 seconds (from the hits, below).
- **Targetability**: an enemy becoming targetable (the first time too, so the site sees a new boss
  come in), and one that has been targetable going untargetable (dead, gone, or a phase change);
  one that leaves the game is untargetable. One never targetable (a helper) isn't recorded. The
  pull's furthest phase counts one more each time every enemy that had been targetable was gone for
  8 seconds or more (`PhaseGap`) and then one became targetable, again or for the first time (a new
  boss). A shorter while away (a boss jumping off for a mechanic) isn't a phase. It's the site's
  rule (`phaseStarts`, `PHASE_GAP`), so the two count the same.

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

The site takes 3 MB, counted as the database writes the JSON out (`SiteLink.ServerSize`: a space
after every colon and comma, about a sixth more than the file). A pull bigger than that is sent
with its places kept less often (every second, then every two, then four), then the enemies'
places only, then none, and the window says it was thinned; the file itself is left whole. Each
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
`deaths` at most 48 of `{ t, slot, job, by }` (`by` the action's name, or null). No names. The
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
number is left out or given its usual value, text is cut short (names 200, notes 4,000, labels 40),
coordinates are kept within ±5,000, and there are caps: 200 plans, 400 steps, 600 objects a step,
500 cues, 16 arena outlines of 512 points. A plan without an id or steps isn't one.

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
server info bar doesn't show it, the main window's pill says "Following your plan · not recorded"
(or "… · recording starts with the next pull" when recording was turned on meanwhile: the pull isn't
switched mid-way), and `/raidplan stop` and Stop only end a recorded pull.

**Callouts** (_Callouts_ on): 5 seconds before a step's first cue (by the same clock), or as its
cast begins when that's later, its name and the first line of its notes go to chat with the
"Raid Planner" tag, and the chat's soft sound (as `<se.1>`, `UIGlobals.PlayChatSoundEffect`, only
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

**The window** (`PlanWindow`, a second Dalamud window): movable and resizable (340×520 at first),
the lock button in its corner fixes it where it is (`PlanWindowLocked`), and Escape doesn't close
it. It never takes focus as it appears, never comes to the front when clicked while locked, and
makes no window sounds, so it doesn't get in the game's way. The plan's name with the list and lock
buttons, "Opening the plan…" or why another plan couldn't be read (with Try again) under it, the
step's name (two lines at most), "Step 3 of 12 · 1:24" with an orange "Following the pull" while a
pull moves it, the board, the notes (scrolling), and previous / next with "3 / 12". Not linked,
loading, a refusal (with Try again), no plans, or no plan for this duty each have their line. What a
step draws is made once, when the plan or step changes (`StepView`): the objects capped and
coloured (`PlanBoard.Prepare`: 160 at most, players, waymarks and enemies kept first; the tooltip
over the board says how many weren't drawn), the words, and the plan's name, the step's title and
its eyebrow fitted to the width and the notes wrapped to it (again only when the width or scale
changes; only the lines in view are drawn). The empty window's line and the plans list's fitted
names are made again only when the plans' `Version` (or the width) changes, so a frame allocates
nothing. Drawing catches anything that goes wrong, logs it now and then, and
closes what it opened.

**The board** (`PlanBoard`): the site's board units (±500, the arena within ±420, y down, rotation
in degrees from north, clockwise) to a square that fits the window. The arena (circle, square,
rectangle by its aspect, a custom one's outlines, a custom one with none yet a square) in its floor
colour darkened; players as dots in their spot's colour with their label; area attacks by shape
(circles, including stacks, spreads, towers and the like; donuts; cones, filled in convex parts of
90° at most; proteans as the site's fan of a cone per bait, 8 of 30° unless set, or lines 60 wide
for its line variant; rectangles and line stacks, 60 wide unless set; crosses), translucent with an
edge; enemies as rings with their facing; waymarks as their lettered circle or numbered square;
head markers as small squares; tethers, lines and arrows, turned with the object as the site turns
them; text; anything else a small dot. Circles have 48 segments whatever their size. Drawn in the
site's order: an object's own layer (To front 7, To back -1), else its type's (`DefaultLayer`, the
site's `TYPE_INFO`: waymarks 0, area attacks 1, lines, tethers, text and spots 2, enemies 3, gazes
4, players 5, markers over heads 6), and in a layer as listed. Clipped to the board.

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
  "Following your plan · …" during a pull only followed; "Recording is off"). While recording, a card with the duty, the clock large, the casts and head
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
- **Settings** (`Views/Settings.cs`): back, _Settings_, the tabs **Recording** (_Record fights by
  themselves_, _In every duty_, _Record where the party stands_, _Keep the last_, _Send each pull_
  and _Track my progress_ (both waiting for a link), _Show it in the server info bar_, and
  _Recordings folder_ · Open), **Plans** (_Show my plan in duties_, _Callouts_, _Share my step with
  the site_, _Plan window_ · Show or Hide (as the window is), _Send my party to my roster_ · Send, with how that went and a
  line that it sends names to your own account only) and **Account** (the token row; _The Raid
  Planner_ · Open), each a card of rows (name, a line, the control; the longer words a tooltip).
  The version and _What's new_ at the foot.
- **The plan window**: see Plans, above.
- **The server info bar** (`Plugin.cs`): "● Rec 2:31" while recording, "Pull 3 kept" for 10
  seconds after; click it for the window.

## Layout

```
RaidPlanner/
  Plugin.cs, Services.cs, Configuration.cs
  Core/       game-free (tested): the recording, its builder, the store, spots, timeline, words,
              the site link and calls (SiteLink, SiteRpc, Pacing), plans (Plan, CueFollower),
              pull summaries and the roster (PullSummary)
  Game/       the recorder, its hooks, the game's data, plans in the game (PlanService),
              summaries and the roster (Uploads)
  UI/         the windows: MainWindow, PlanWindow and its board (PlanBoard); Kit/ (from Housing
              Studio's), Views/, Glyphs, Model/ (SVG paths, dashes)
RaidPlanner.Tests/  xunit tests for Core and UI/Model
tools/      the icon (icon.svg → images/icon.png)
docs/       PUBLISHING.md
```

## Commands

`/raidplan` opens the window; `/raidplan settings` opens Settings; `/raidplan stop` ends the pull
being recorded (and keeps it); `/raidplan plan` shows or hides the plan window; `/raidplan next` and
`/raidplan prev` move its step; `/raidplan send last` sends the newest kept pull.

## Not yet

- Not run in the game yet: everything in `Game/` and the plan window are compiled against Dalamud 15
  but untested.
- Summaries waiting to be sent are lost when the game closes.
- Head marker numbers are kept as the game sends them; the site names them from BossMod's data
  where it can (stack, spread, tankbuster…).

## Release notes

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
