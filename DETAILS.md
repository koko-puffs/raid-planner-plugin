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
dungeon, Eureka, Bozja or the Occult Crescent: `GameData.IsFight`, by the ContentType sheet's rows
2, 3, 6, 7, 9, 21, 26, 29, 38; _In every duty_ in Settings records those too, except the Occult
Crescent) with _Record fights by themselves_ on. **The Occult Crescent is never recorded** (since
0.3.0): South and North Horn, their critical engagements and FATEs and the Forked Tower, by the
territory's intended use (61, `GameData.IsOccultCrescent`), even with _In every duty_ on; the meter
still works there, and an open plan window still follows the pull without keeping it.
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

**Who the party is** (since 0.5.0, `Game/PartyMembers.cs`, the rule in `Core/PartyPick.cs`, tested
with fakes): one place reads it, and the recorder (its party, the parser's "is this the party's",
whether the fight goes on, whether everyone's down), the meter, the cooldown tracker, the plan window,
Home and the roster all ask it. The game's party list (Dalamud's `IPartyList`) when it has anyone: a
normal party, and any party in a duty (cross-world ones included); a member in another zone (the game
gives them no entity id) keeps their place by content id, for the spots, Home's count and the roster,
but nothing can be credited to them. Else, outside a duty, **the
cross-world party's group you're in**: the game keeps a cross-world party only in
`InfoProxyCrossRealm` there, and the party list is empty (before 0.5.0 only you were counted, so a
friend's damage on a target dummy was dropped). In a cross-world alliance only your own group counts
(the light party, as the party list has it in a duty): the group the game says is yours, or the one
you're found in (by entity id or content id). The group isn't used when you aren't in it, it has only
you, or it has more than 8 or the groups number more than 6 (a misread). Someone the game gave no
entity id for is looked for near you by their content id (the object table's characters), else kept
by name and job only (no damage can be credited to them until they're found). Else just you. The
members are read at most four times a second; their HP (the party list's at any distance; a
cross-world member's from their character, when near), whether they're near and in combat, once a
frame however many ask. A member whose HP isn't known (a cross-world member who isn't near) is left out
of "everyone's down", the recorder's and the meter's alike (only those whose HP is known count), and the
meter keeps them as they were last seen (alive when never seen; a new zone forgets), so walking away from a dead friend and back isn't
a raise and a second death. Your spot (the plan window's reticle, the callouts' "You:" line) is worked
out from the same party once a second. The cross-world read [verify: the groups, your group's index, the members'
entity ids as the object table has them] is guarded: a read that throws stops it for a minute (logged
once), and with Diagnostics on each change of who the party is goes to the file (`party-read`).

**Your gear** (since 0.5.0, `Game/GearReader.cs`): as a recorded pull starts (not one only followed),
each item in the game's equipped-items container (Dalamud's `IGameInventory`, `EquippedItems`: the item's base row,
its high-quality flag; its name and item level from the Item sheet; the soul crystal too) and your
attributes then (`PlayerState.Attributes` by the game's attribute ids: STR, DEX, VIT, INT, MND,
critical hit, direct hit, determination, skill and spell speed, tenacity, piety) [verify: the
container's places, the attributes' indices]. The average item level is worked out as the character
window does [verify]: the 12 places that count (not the waist's nor the soul crystal), an empty one 0,
a two-handed main hand counted again in the off hand's place, over 12, rounded down; it's the items'
own, before any level sync, while the attributes are as the game had them (synced). A read that
throws gives no gear (logged); the pull is recorded as ever. With Diagnostics on, what was read goes
to the file (`gear`).

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
  order of FFXIVClientStructs' `StatusManager.ProcessHotDot`), with the target's HP read for a heal
  or a party member, to the parser: the combat block's `tick` (that HP its `h`), and a bleed's
  status as a death's cause.
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
The party is `PartyMembers`' (the party list, or since 0.5.0 a cross-world party's group outside a
duty; just you alone: above), each given a spot from their job's role
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
an enemy in the recording from no one the game named; another player's on an enemy isn't kept),
each with the HP before it the hook read (a HoT's target's, a party member's);
`cast`, the party's casts; `st`, the statuses the party put on anyone (buffs, DoTs, Reprisal on the
boss). About 15 to 25 KB a minute in an eight-player raid.

**Since 0.4.0** (the site's contract, round four; every reading of the game that hasn't been checked
in game yet is marked [verify] in its code comment and listed under _Not yet_):

- **Every action and item the party uses** (`combat.use`): the parser hands on each party member's
  (or their pet's) action as it goes off, whatever it did, with its first target, how many targets it
  had and flags from its effects (1 a pet's, 2 an item, 4 a limit break, 8 it interrupted a target's
  cast: effect 76, 16 a target resisted a status it carries: effect 55, 32 it missed every target:
  only effects 1, 2 or 7, 64 no target, 128 a provoke: effect 24, 256 the healers' limit break 3
  raising: effect 50). Auto-attacks (ActionCategory 1) are left out (they're `fx` already). An item
  (the effect header's action type 2 [verify for others' items], its id +1,000,000 when high quality
  [verify]) is a use and nothing else; the items' names and icons are kept (`combat.items`). An action
  resolved in two messages (the same global sequence) is one use. A ground-placed action (the Action
  sheet's TargetArea: Sacred Soil, Asylum, Earthly Star, Doton…) is also an `area` row with where the
  hook says it was put.
- **Before the pull** (`combat.pre`, `combat.cd`): out of combat in a duty a pull would be recorded in,
  the hooks stay on and the party's uses of the last 30 seconds are kept (`Core/Meter/CastWatch.cs`'s
  `PrePull`), with the game's countdown (`AgentCountDownSettingDialog`: `Active`, `TimeRemaining`;
  `CountdownWatch`). At the pull's start they're copied in: the uses with their times before it
  (negative), and the countdown that reached (or would reach) zero within 30 seconds of the start, its
  length and how late the pull began after zero (negative: before). Both are let go of on a new zone.
  A pull only followed keeps nothing.
- **Casts stopped** (actor control 15 [verify that its fourth argument 1 is interrupted]): a party
  member's cast still waiting gets its `why` (1 cancelled, 2 interrupted) and is cancelled; an enemy's
  cast the recording holds, said stopped before a quarter second from its end (the game says it on
  some casts that went off), is a `cancel` event, credited when interrupted to the party member whose
  action carried the interrupt effect on that enemy in the second before, else whose Interject, Head
  Graze, Low Blow or Leg Sweep hit it then (`CastWatch`; by English name). An enemy's cast says whether
  it can be interrupted (`CastInfo.Interruptible`: `int`).
- **Raises**: a dead member's HP above 0 again is a `raise` event, at that frame (when they accepted),
  credited to the Raise status put on them since they died (its source and time [verify that the
  raiser is its source]), else the healers' limit break 3 (`lb`), else the last raise (Raise,
  Resurrection, Ascend, Verraise, Egeiro) used on them while down; else no one. Weakness and Brink of
  Death are recorded as statuses already (a `status` event from no one, or a party `st` row).
- **The enemies' targets**: each targetable enemy's target (its `TargetObjectId`) each frame, an
  `aggro` event when it changed (0 none, or someone not in the recording).
- **The enmity list**, once a second: the recording player's target's list (`UIState.Hate`, the
  party members on it, each a % of the top [verify]), a `hate` row for a member whose place changed or
  whose % moved 5 or more, −1 for one who left it; only for a target in the recording.
- **MP and barriers** (`res`): each party member's `Mana` and `ShieldValue` (% of max HP), a row the
  first time, then when MP moved 500 or the barrier changed, at most every half second each.
- **The limit break gauge** (`lb`): `LimitBreakController`'s units and bars × units a bar [verify], a
  row on change.
- **Head signs**, once a second with the waymarks (`MarkingController.Markers`, 17 slots [verify their
  order]): a `signs` event when any changed.
- **Each enemy hit's targets**: their barrier before it (`sh`) and where they stood as it arrived
  (`px`, `pz`), read with their HP; and the hit's flags 256 (knocked back or drawn in: effects 31–36),
  512 (partly absorbed: a damage effect's `Param4` & 4 [verify]) and 1024 (a status it carries
  resisted: effect 55). **Fixed**: until 0.4.0 effect 55 was read as "fully resisted" (16), so a hit
  that did damage and had its stun resisted counted as not landed; it's 1024 now, and 16 is only the
  game's full resist (effect 2).
- **A damage effect's bonus %** (`fx.b`: its `Param2`, the battle log's "(+61%)", signed), what the
  site tells a missed positional by; and each action's cooldown groups, charges and ground placement,
  each status's category and whether it can be cleansed (from the sheets), and for the recording
  player's own non-GCD actions their recast as the game works it out (`ActionManager.GetAdjustedRecastTime`
  and `GetMaxCharges` [verify: milliseconds; traits at the synced level]), asked as the pull is kept.
- **The recording player's spot** (`party[].self`).

About 13 to 20 KB a minute more in an eight-player raid. The hooks' detours never throw into the game:
everything they do is caught and logged at most every 10 seconds.

At most 20,000 events and 4,800 frames are kept (9,600 at four a second: Settings → Recording →
_Smoother replays_, since 0.4.0; off). A typical 20-minute pull is about 1.4 MB, and about as much
again with its combat block; one at the caps can be 4 to 6 MB, which is sent thinned (see Sending).

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
  "s", "v", "heal", "h" }, "cast": { "t", "src", "a", "dur", "x" }, "st": { "t", "on", "src", "s", "a", "dur",
  "n", "gain" }, "thinned": [ ... ] }`: columns of equal length; `t` in centiseconds from the row
  before (the first from the pull; written in time order, so never below 0); `k` the kind in its low
  3 bits (1 damage, 2 heal, 3 shield, 4 status only) and flags above (8 crit, 16 direct hit, 32 a
  pet's, 64 limit break, 128 reflected, 256 on the caster); `o` the party member credited; `r` the
  recipient (party or actor id, 0 not in the recording); `h` the recipient's HP before a heal (−1
  otherwise); a tick's `src` the party member credited, an enemy's actor id for a bleed, 0 when the
  game didn't say, and its `h` (since the alignment round; optional: a file without it reads as all
  −1) the HP of the one it ticked on just before it, as the hook read it (a HoT's target's, a party
  member's; −1 otherwise); `x` 1 for a cast cancelled; `gain` 1 or 0. A table dropped to fit the
  site's limit is left out and named in `thinned`.

Since 0.4.0 (round four, the site's contract §3; still version 1, no new top-level key, every key
optional; `combat.v` stays 1):

- Events: `{ "k": "cancel", "t", "src": actorId, "action", "int"?: true, "by"?: partyId, "with"?: actionId }`;
  `{ "k": "raise", "t", "who": partyId, "by": partyId | 0, "at"?: seconds, "lb"?: true }`;
  `{ "k": "aggro", "t", "actor": actorId, "on": partyId | actorId | 0 }`;
  `{ "k": "signs", "t", "marks": { "<0–16>": partyId | actorId } }` (all signs as they now are).
- `cast` (an enemy's): `int: true` when it can be interrupted.
- `hit`: `sh` (barrier % before, −1 unknown), `px`, `pz` (two decimals; null unknown), each as long
  as `tgts`; `fx` bits 256, 512, 1024 (above).
- `party[].self: true` (the recording player's spot); `actions{}`: `cdg`, `cdg2` (cooldown groups; 58
  the GCD's), `charges` (more than 1), `rc` (the recording player's own recast, seconds), `area: true`;
  `statuses{}`: `cat` (1 beneficial, 2 detrimental), `dispel: true`.
- `combat`: `fx.b` (bonus %, 0 default) and `cast.why` (0, 1 cancelled, 2 interrupted), optional
  columns; new tables `use { t, src, a, tgt, n, f }`, `area { t, src, a, x, z }`, `hate { t, on, who,
  pct }`, `res { t, id, mp, sh }`, `lb { t, v, max }`, `pre { from: 30, t, src, a, tgt, f }` (its deltas
  count from `from` seconds before the start: a row's time is the sum / 100 − `from`), `cd { len, end }`
  and `items { "<id as sent>": { name, icon, hq } }`; `thinned` can name `hate`, `res`, `area`, `cast`,
  `use`, `tick`. A combat block read from an older file (none of the new tables) is written back
  without them (`CombatLog.Extended`), so it never seems to say its party used nothing.

Since 0.5.0 (round six, SPEC5 §3.6; still version 1, one new optional top-level key): `gear`, the
recording player's as the pull began (above), left out when it couldn't be read:
`{ "job", "ilvl", "items": [{ "slot", "id", "hq", "name" }], "stats": { "str", "dex", "vit", "int",
"mnd", "crit", "dh", "det", "sks", "sps", "ten", "pie" } }`. `slot` is the equipped-items container's
place (0 main hand, 1 off hand, 2 head, 3 body, 4 hands, 6 legs, 7 feet, 8 ears, 9 neck, 10 wrists, 11
and 12 rings, 13 the soul crystal), `id` the Item sheet's row, `name` the item's in the client's
language; a stat is written only when above 0. Read back leniently (`Gear.Read`): an item with a place
outside 0–13 (or a second at a place), no id, or a stat not a whole number above 0 or not one of the
twelve keys is left out; `gear` that isn't an object reads as none.

`Recording.Parse` reads all of it back leniently: a hit's lists not as long as its targets, or a
combat table whose columns aren't all arrays of the same length, are left out; an optional column not
as long as its table reads as its default; a value that isn't a number reads as 0; a delta below 0 as 0.

## Diagnostics (`Core/DiagnosticsLog.cs`; Settings → Recording → _Diagnostics_)

Off by default, and **off again each time the plugin loads** (it's never saved: the file holds names and
grows while it's on). While on, the raw values behind each reading that hasn't been checked in the game
(the [verify] notes above and in SPEC4 §7) go to **`diagnostics-<yyyy-MM-dd>.log`** in the recordings folder
(the plugin's config folder's `recordings/`, beside the pulls). Plain text, one line per reading:
`21:03:04.123 | zone 1122 <duty or place> | pull 3 @ 12.50 | <kind> | <numbers>` (`no pull` outside one).
The kinds:

- `party` (a pull's start): you, where the party was read from (since 0.5.0: `list`, `cross-world` or
  `alone`), and each member's entity id, **name**, job and number in the recording, so the ids in other
  lines can be read.
- `party-read` (since 0.5.0; each change of who the party is, and once as Diagnostics is turned on):
  where from, what the cross-world party read said (in one, an alliance, your group's index, how many
  groups and members) and why it wasn't used when it wasn't, then each member's entity id, **name**, job
  id, level, whether a content id was read (`cid`, never the id itself) and whether they're near. A
  cross-world read that threw says so (`cross-world read failed`).
- `gear` (since 0.5.0, a recorded pull's start): your job, average item level, whether the main hand was
  taken as two-handed, each item's place and row (`hq`), and the attributes read.
- `cancel-cast`: actor control 15 as sent: the actor, party or not, its arguments 1–6 (2 the action type,
  3 the action, 4 interrupted?).
- `party-hit`: each of the party's damage effects (autos left out): action, caster, target, the effect's
  type, Param0, Param1, the bonus % (Param2, signed), Param3, Param4, the value.
- `enemy-hit`: an enemy's hit on a party member: every effect on them as sent `[type p0 p1 p2 p3 p4
  value]` (knockbacks 31–36; Param4 & 4), their HP and the barrier read.
- `action-type`: a party member's message that isn't an action (an item: type 2, its id as sent, the
  high-quality +1,000,000).
- `ground`: a party member's ground-placed action and the place the message carried (x, y, z).
- `status`: the Raise, Weakness or Brink of Death status gained by someone: on whom (**name**), its source as
  the game has it (id, **name**), whose kind the recorder took it for, its param, time left, their HP.
- `hate` (once a second in a pull): the enmity list's target, length and every entry (entity id, enmity).
- `lb-gauge` (on change, in a pull): current units, bar count, units a bar.
- `signs` (on change, in a pull): the head signs' slots holding someone (slot, entity id, **name**).
- `own-recast` (each action you use): the game's recast and how far into it, the adjusted recast (ms),
  the most and current charges, and the sheet's recast, cooldown groups and charges.
- `enemy-cast` (each new enemy cast in a pull): the action, its cast time, interruptible, its target.
- `countdown` (starting and stopping, before a pull in a recorded duty): active and the time left.

Readings come only while the plugin reads them (a pull recorded, before one in a recorded duty, or the
meter or cooldown tracker on). Cheap: off, the hooks ask one bool; on, lines are gathered in memory and
written on a worker at a pull's end, a zone change, turning it off, unloading, or past 64 K characters. A
file stops at 5 MB: it's moved aside to `diagnostics-<date>.1.log` (one older part kept) and a new one
begun; the newest six diagnostics files are kept, older ones deleted. Nothing in it throws: a line that
can't be made or written is lost. **The file contains character names** (yours and your party's, and
anyone a status line is about): look before you share it.

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
comma, about a sixth more than the file). A pull bigger than that is thinned, each step only while
it's still too big (0.4.0's order): (1) a file with four frames a second down to two; (2) the combat
block's enmity list (`hate`); (3) its MP and barriers (`res`); (4) the places kept less often (every
second, then every two, then four), then the enemies' places only at those steps, then none; (5) its
ground placements (`area`); (6) the party's casts (`cast`); (7) what the party used (`use`); (8) the
ticks (`tick`). Never the effects (`fx`, with their bonus %), statuses, gauge, pre-pull, countdown,
items or events. Frames are thinned by time (a frame kept as it crosses each step: `SiteLink.ByTime`),
not by count; an empty table isn't dropped. The block says what it lost (`combat.thinned`); and the
window says it was thinned. The file itself
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
where `reached` is the length (the site reads a wipe's as its last death, when that's within 20 s
of the end: the recording runs on past the wipe), `phase` the furthest phase (from the builder's count, above) and
`deaths` at most 48 of `{ t, slot, job, by }` (`by` the action's name, or a lethal tick's status's,
or null). Since 0.3.0 also (`Core/PullStats.cs`, the site's contract §3):

- `boss_hp`: the HP % left (two decimals) of the fight's main boss at the end: of the bosses, the
  one targetable last (the most max HP among those), from the last frame with its HP; 0 on a clear,
  null when it isn't known.
- `phases`: when each phase began, whole seconds, the first 0 (the site's `phaseStarts`), as the
  recorder counted them while it recorded (the same count as `phase`, even past the events' cap).
- `stats` (at most 7 KB as the server measures it, `PullStats.Size`: Postgres's jsonb text, a space after
  each `:` and `,`, so the server's 8,192-byte check always has a kilobyte to spare; 6 KB compact before the
  round-five review, which a summary of small numbers could have taken past 8,192): `{ v: 1, slots, lb, hits, debuffs, names }`. `slots`, the party in the
  recording's order (the party list's), each with its spot: `dmg` (their damage, their pets' and DoTs' included; a tick the game sent without saying
  whose is shared between the party's DoTs on the target then, as the meter does), `heal` (what their
  heals restored: the heal less what went past the HP missing then; a HoT's tick against the HP read
  as it ticked, its row's `h`, or in a file from before that the last frame's),
  `over`, `taken` (enemy hits and bleeds), `crit` and `dh` (of their direct hits, 0–1), `active`
  (seconds from their first damage to their last), `deaths` (not counting the wipe: a wiped pull's
  deaths within 6 seconds of its last), `dd` and `vuln` (damage downs and vulnerability ups gained,
  with their stacks: a new one counts its stacks, one put on again the stacks it gained or 1).
  `lb`, the limit break's damage. `len` (since the alignment round; left out with no damage to
  measure by), the fight's length in seconds (two decimals) the numbers are over, the meter's rule
  (below): from the first damage between the party and an enemy (either way; a tick never starts
  it) to a wipe's last death, else to the last damage between them, ticks included; the site works
  each spot's ENCDPS out over it (the server keeps it once its `…_rp_stats_len` migration is in).
  `hits`, the enemies' hits on the party (autos left out: "Attack",
  "Auto-attack" and "Shot", as the site's `AUTO_ATTACK` names them; and a single target's hit on
  someone not a tank, unless it's a cast buster; 400 at most) as `[deciseconds, action, slot mask, damage]`, the mask's
  bit i the i-th of `slots`, only those it landed on (a miss, a dodge or an invulnerable target isn't
  in it, as the site reads a recording's hits; a hit that landed on no one isn't sent); `debuffs`, the damage downs and vulnerability ups as they were gained
  (200 at most) as `[deciseconds, status, slot mask]`; `names`, those actions' and statuses' names
  by id. Over the size, see the end of this list for what goes first. Damage downs and
  vulnerability ups are the statuses so named on the game's Status sheet in English
  (`GameData.FailKinds`: every row named exactly "Damage Down" or "Vulnerability Up", as the site's
  `model/debuffs.ts` lists them; "Physical Vulnerability Up" and "Magic Vulnerability Up" aren't
  failures: tankbusters put them on the tanks on purpose).
- Since 0.4.0 (`PullStats.Extras`), each `slots` entry also, each left out when 0: `self: true` (the
  recording player's spot), `gcds` (their GCDs used: actions whose cooldown group, or additional one,
  is the GCD's, 58; their pets' not), `pots` (items used, before the pull too), `raises` (raise events
  they're credited with), `raised` (raised), `dead` (seconds dead, one decimal: from each death not the
  wipe's to their raise or the fight's end, `len`'s end, whichever came first: a raise after the fight
  isn't time dead in it), `ints` (enemy casts they're credited with
  interrupting). And two new keys (the site's `…_rp_round_four` migration accepts them): `ph`, the damage
  by phase, `[[start, d0, d1, …], …]` aligned with `phases` (≤ 50; each `slots` entry's damage in it, pets
  and DoTs in, the limit break out: each spot's adds up to its `dmg`); `x`, small extras (≤ 2 KB):
  `int` `[interrupted, interruptible casts]`, `swaps` (tank swaps on the bosses, the site's rule: their
  targets as spans, each to the next, the last to the pull as fought, `PullStats.FoughtLength`: a wipe's
  last death when the recording ran on 20 s or less past it; those of a second or more on a tank and not
  inside one of their casts aimed at that tank, from half a second before it to a second and a half after
  its end; one tank's after another's is a swap: `PullStats.TankSwaps`), `lbUses` (`[deciseconds,
  action]`, the first 10), `cd` (the countdown's `end`) and, since 0.5.0, `ilvl` (the recording player's
  average item level as the pull began, `gear.ilvl`; only when the gear was read, for the fight page's Personal
  bests; it goes with `x` when that's trimmed). Over the size: `ph` goes first, from its latest
  phases (a fight with dozens of phases; `phases` keeps every start, so a phase kept still has its end
  there), then the latest hits, then the latest debuffs, then `x`. The hits (who failed what) are what
  Progress is about; with 50 phases, `ph` left room for only about 20 of them.

The site reads these as the meter means them, and works the same numbers out from the pull's
recording by the same rules (Review); `AlignmentTests` (`RaidPlanner.Tests/AlignmentTests.cs`) plays
a whole pull with every kind of number in it (a party listed out of spot order, DoTs with ticks that
named no one, a HoT, a shield, misses, dodges and an invulnerability, stacks of Vulnerability Up, a
bleed, an instant death and a wipe) through the plugin as the game does (the game's messages read by
the combat parser, the meter's session and the recording's `RecordingSink` listening, the game's
status lists read each frame for the recording and the parser alike), checks that the meter after
the fight reads as the summary (every spot's damage, healing, overheal, taken, deaths, crit and
direct hit rates, active time, ENCDPS, HPS and DPS, the limit break, the length and the deaths; since
0.4.0 also GCDs, potions (the pre-pull one included, both ways), raises
given and taken, time dead and interrupts; the pull now also has uses before it and the countdown, an
interrupt credited and an interruptible cast that went off, a stun resisted and one landed, a provoke
and a tank swap, a non-tank holding the boss, the BRD raised and dying again with Weakness, potions,
the gauge, the enmity list, MP and barriers, signs, knockbacks, an absorbed buster and bonus %; the
meter's numbers file has the new counts, the deaths' raises and the Events tab's list) and,
with `RP_ALIGN_OUT` set to the site's `src/features/raid-planner/model/__fixtures__` folder, writes
the recording, its summary and the meter's numbers there (`align.raidrec.json`, `align.summary.json`,
`align.meter.json`) for the site's `alignment.spec.ts`, which checks the meter's, Review's and
Progress's numbers against each other (amounts exactly, the length to the hundredth, rates to a
billionth). Write them again when the summary, the file or the meter changes (as `RP_FIXTURE_OUT`
writes the shared `sample.raidrec.json`). Since 0.5.0 both recordings carry the BLM's `gear`
(`RecordingTests.SampleGear`: 7.0's Dark Horse Champion's set, the items' ids, names and item levels
the Item sheet's, the attributes made up but of a level-100 caster's size).

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
It's refused with fewer than 2 names ("Your party can't be read here; try in the duty."): sending what's
read (just you) would wipe the roster on the site down to one name. Since 0.5.0 the party is
`PartyMembers`' (above), so a cross-world party outside a duty can be sent too.

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
id or steps isn't one. A real arena's rings (since 0.5.0, below) are read as the site cleans them. Since 0.3.0 it reads each step's `callout` (what a raid lead says: the plan
window's big words and the chat line, `PlanStep.Big` falling back to the step's name without its
"P2 · "), the board's `order`, `reach`, `spacing`, `dashed`, `noIcon` and a pen's `points`, the
arena's `yalms`, `edge` (wall, death, drop) and `grid`, and a list entry's `steps` when the site
sends one. The phase a step is in ("P2") comes from the steps' names as the site's timeline reads
them (`PlanJson.Phases`: "P2 · …" or "Phase 2: …" starts one; the steps before the first are P1).

**Planned cooldowns** (since 0.5.0; SPEC5 §3.1, the site's `phase.cooldowns`, written in Edit from
model/mitigation.ts): who uses which mitigation, shield or big heal in a step, and when. Each entry is
`{ id, spot, action, name, at?, note? }`: the spot (MT, OT, H1, H2, M1, M2, R1, R2, or P1 to P24 in an
alliance's plan), the Action row (0 not known), its English name, `at` seconds after the step's own time
(−60 to 600; negative is before it) and a short note. Read as the site normalizes them (`PlanStep.Cooldowns`,
field by field as model/cooldowns.ts `normalizeCooldowns`): an entry needs a spot it knows and a name; the
action a whole number (0 when it isn't a number above 0), `at` to a tenth within −60 … 600 (0 when it isn't
a number), both rounded as the site's `Math.round` (halves up), name and note one line (80 and 60 at most);
at most 24 a step; in the order they go (by `at`, then the spots' order). An older plan, or a step without the key, has none. `rp_plugin_plan` sends them (and
each step's `callout`, which it never sent before: the site's migration
`20261112105739_rp_plugin_plan_cooldowns.sql`, applied on 12 November 2026). Placed on the plan's clock (`UI/Model/PlannedUses.cs`): a step's time is its earliest cue's (as
the callouts time a step), plus `at`; a step with no cue has no time, so its cooldowns are only listed in
the window, never marked or said.

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
when the plan window is open on a plan for this duty on the frame combat begins, or (since 0.4.0)
the on-screen callouts are on with this duty's plan picked: they run on the plan's clock
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
(`PlanService.PlanClock`, the follower's time once a cue has matched; `StepProgress`); since 0.5.0 the
step's planned cooldowns, a row each (20 tall): the spot in its role's colour (the board's spot, 16),
the action (SansSemibold 13), its note muted after it and its time off the step's in mono on the right
("−3 s", "+4 s"; none at the step's own time), yours (your spot now) washed in ember and in white;
four rows at most (past that the fourth says "+3 more"), fewer when the window is short (as many as
leave the board its 110 above the footer, the last "+N more"), each row's tooltip all of it ("MT ·
Reprisal −3 s · for Akh Morn"), the "more" row's every one; at its smallest, or with room for under two
rows, one line instead, yours ("You: Reprisal −3 s") or how many ("4 cooldowns planned"); then the board, a square as big as leaves three lines for the notes (110 at the least); the notes (13px,
"- " lines as "• ", wrapped once per width, only the lines in view drawn), fading at the foot when
there's more, with the scrollbar; and at the foot 32px square previous and next round the count in
mono. Another plan on its way runs a slim cast bar under the header and keeps the old step until
it's in; one that couldn't be read says so in red with a quiet retry. At its smallest (under 270
wide or 400 tall), as the mock-up draws it, the callout is 16px (three lines at most still), the
list is the only button, the board 110 and plain (no bezel), there's no previous and next
(`/raidplan next` and `prev` still move it), and the notes say "Notes: 2 lines, make it taller"
instead of vanishing. A callout or notes in Japanese or Chinese (no spaces) wrap where a line fills
(`Format.Wrap`), not cut on their first line; what's measured before the fonts are built isn't kept. With no plan to show, the Boss says why over a small
callout, one line and one button (`EmptyState`): not linked (asleep in a dashed ring; _Link your
account_, the telegraph, opens Settings → Account), reading (the party in a row over a running cast
bar), gone wrong (awake; the error's first sentence as the callout, the rest under it; _Try again_
outlined), no plans (asleep; _Open the Raid Planner_ outlined), no plan for this duty (its head
tilted; _Pick a plan_, the telegraph, opens the menu), not in a duty (_Pick a plan_). The plans menu
is the site's menu (raised #18181d, a white hairline at 12%, 14px corners, its shadow faked with
rings in the menu's own draw list, so they show over the window it opens from): this duty's plans first under an eyebrow ("This duty · …"), the one shown with a ready
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
outside it. A custom arena's outlines are drawn with their floor (each cut into triangles once, so a
concave one, an L or a cross, fills as drawn) and their edge. Telegraphs
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
you (your spot in the party now, from `PartyMembers` and `Core/Roles.cs`, read once a second) ringed white under the
reticle; waymarks as their lettered circle or numbered square; head markers (limit cut dots,
PlayStation shapes, signs). At most 160 objects (players, waymarks and enemies kept first; the
tooltip over the board says how many weren't drawn), in the site's layers, clipped to the board.

**A real arena** (since 0.5.0; the site's `shape: 'poly'`, its model/rings.ts, from 12 November 2026):
the arena's exact shape from the game or the encounter data, as polygons with holes. `rp_plugin_plan`
sends the plan's arena as stored, so it comes as `rings` (`[polygon][its outline, then its holes][x0,
y0, x1, y1, …]`, yalms from the board's centre, x east, y south, as the objects), `yalms` (twice its
reach), `aspect` 1, `outlines: []`, and, only for information, `center` (`{ x, z }`, the world point at
the board's centre) and `duty` (the ContentFinderCondition it was picked from), either of which may be
missing; never its floor picture (nor a step's own arena, as before). `Core/Rings.cs` reads the rings
as the site's `cleanRings` does, never throwing: numbers only, two to a corner, a corner that isn't a
finite number dropped with its pair (an odd one left over too), kept within ±500 yalms and to 0.01; a
ring of fewer than three corners dropped, a polygon whose outline isn't one dropped with its holes (a bad
hole after a good outline is just skipped); at most 32 polygons of 32 rings of 512 corners, 4,096
corners in all, the first kept. A `poly` with no ring left is drawn as a plain square (as the site does);
without `yalms` its size is twice the rings' reach, rounded up to a tenth; `edge` missing is a wall. On
the board a corner is at its value × 840 / yalms. Rings aren't closed and are read **even-odd**: a point
is on the floor when, in any one polygon, it's inside an odd number of that polygon's rings
(`PlanArena.OnFloor`, as the site's `inRings`; the other shapes answer too), so which way a ring goes
doesn't matter, a hole is a ring inside its outline, platforms that overlap are floor where they
overlap, and a split arena's gap isn't floor.

Drawn as the site draws it (as a custom arena's outlines, holes even-odd): each polygon's floor in the
arena's colour, then the grid, then the rim along every ring, outlines and holes alike, in the edge's
style (the #3a3b45 wall, the ember death wall with its glow, the dashed drop); no bezel and no compass
(as the board's other shapes but the round one). ImGui fills only convex shapes and has no even-odd
rule, so `UI/Model/ArenaFloor.cs` cuts each polygon once, the first time the plan's step is shown, and
keeps it with the arena (never in a frame): a scanline, slabs at every corner's height and wherever two
edges cross, the edges through a slab paired off left to right (even-odd), each pair a trapezoid of two
triangles, joined with the one above while the same two edges bound it. It needs no winding and takes
holes, touching rings, overlapping polygons (each fills, as the site's paths do) and rings crossing
themselves; its work is bounded (24,576 triangles, a fixed amount of looking), so a broken shape draws
in part and never holds the game up (a real 4,096-corner arena takes about 60 ms, once). Made with it, in
board units: each ring's corners for the rim, a drop's dashes along every ring (0.9 on, 0.6 off in the
marks' units, as the board dashes a drop elsewhere; 8,192 at most, longer past that), and the grid cut to
the floor, even-odd along each line (the square grid's lines at −½, 0 and ½ of the half-width, the radial
grid's rings every quarter and its four diameters, out to the floor's furthest corner), as the site clips
its grid to the arena. A frame only scales and moves what's made, and gives ImGui the triangles in
batches well under its 65,536 corners a draw.

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
  latin-ext; OFL, `Resources/Fonts/OFL-Archivo.txt`) at 13, 16, 17, 22 and (since 0.4.0, the on-screen
  callouts' first step; latin only, so a callout in Chinese or Korean is drawn at 22) 30px for the callout, in
  capitals written by the code; Geist 400 (11–14), 500 (12–14, the site's 550) and 600 (7, 8, 10,
  13–15, the site's 650 and 700); Geist Mono 500 (9–13) and 600 (10–13, 15, 16, 18 and 30 for the
  clock), for everything counted, so clocks hold still. Letter-spacing (eyebrows +0.09em, tags
  +0.05em, intents +0.1em) is drawn glyph by glyph. 31 fonts on most clients (some 20,000 glyphs);
  a size not loaded is drawn at the nearest (worked out once). On a Japanese client Archivo and Geist
  are Dalamud's font from 11px up (the callout drawn twice a pixel apart for its weight, and not put
  in capitals; the mono capitals stay capitals), the faces of one size sharing one font (8, not 17:
  each carries the kanji); the spot labels (7–10px) stay Geist; the mono stays Geist Mono, and text
  with Japanese in it (a duty's name in an eyebrow, a job's abbreviation) is drawn in the sans
  instead (`Txt.For`). Dalamud's language's extra glyphs (Chinese, Korean) are added only to the
  faces a plan's own words are drawn in (`Fonts.PlanText`: the callout at 16 and 22, Sans 12 and
  13, SansMedium 14, SansSemibold 13): everything else is the game's words or the windows' own.
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
  words at 14px, its answers on the right: a destructive one in the danger outline). Escape is No (while it has the keyboard). It
  fades in over 280ms (it doesn't grow: text can't scale crisply).

## The main window (`UI/MainWindow.cs`, `UI/Views/`)

560×800 at first (460×520 at the least), its header 16px down and 20 in, its body scrolling (16
above, 18 below), a footer on the basalt band where a screen has one.

- **Home** (`Views/Home.cs`): the header's state as a tag (`StateNow`): "Rec · pull 12 · 2:31"
  ember with a beating dot while recording; "Waiting for the pull · Example Arena" in a duty that
  records; "Following your plan · not recorded" (or "· recording starts next pull") while a pull is
  only followed; "Recording is off"; "Not in a duty". On its right a quiet _Open the Raid Planner_
  and the Settings button. While recording, the ember box: "Recording · pull 12 tonight" in ember and
  "Started 23:38"; the duty as a small callout; the clock in Geist Mono 600 30px over a cast bar
  running to the plan's length when you have a plan for the duty (named for its step: "Platforms:
  draw-in · step 12 of your plan"; the length is its last cue's time) or to the longest pull kept
  here; what's being recorded in mono (Casts, Markers, Tethers as each is put on, Places "8 ×2 / s", or Off);
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
  quiet _Stop_ beside it (it cancels the send; so does _Unlink_), and a cast bar "Sending pull 12 · 0.6 MB" runs under
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
  what hit them ("H2 died · Exaflare"; the wipe's own, a wiped pull's deaths within 6 seconds of its
  last, left out: `Model/PullView` by `PullStats.WipeDeaths`, the rule the summary, the meter and the
  site keep; before the review it counted back from the recording's end, which runs on past the wipe). Reading it, the party in a row over a running cast bar; a file that won't read,
  the Boss awake ("This pull won't open"); nothing in it, asleep ("Nothing went off"). The footnote
  in muted (it's read); the footer has the send with _Send to Raid Planner_ filling the row.
- **Settings** (`Views/Settings.cs`): the square back button, "Settings" as a small callout, the tabs
  _Recording_, _Plan window_, _Meter_, _Cooldowns_ (0.4.0), _Account_ over a hairline (dense, 32 tall
  at 13px, when the window is too narrow for them at 40). Each a slab of rows: the setting's
  name (550 14px) over one muted line, its control at the right (switch, 40px stepper, dense segments
  or button); a sub-row 34 in; a row that can't be used dims its words and control and says why in
  the text colour ("Link your account first: Account", the link going there). **Recording**: _Record
  fights by themselves_, _In every duty_ (a sub-row), _Record where the party stands_ with _Smoother
  replays_ under it (0.4.0; off while places aren't kept), _Keep the
  last_, _Send each pull_ and _Track my progress_ (both waiting for a link), _Show it in the server
  info bar_, _Recordings folder_ · Open; a hook the game hides is said in an amber warning box.
  **Plan window**: _Show my plan in duties_, _Lock it where it is_, _Say each step in chat_ (the
  setting once called Callouts: on the site a callout is the step's big words), _Share my step with
  the site_ and _Send my party to my roster_ (both waiting for a link), _Plan window_ · Show or Hide;
  the roster's result as a sent line or a red box; the note on names; then **On-screen callouts**
  (0.4.0, a section of its own: below, The overlays). **Meter**: below. **Cooldowns**: below, The
  overlays. **Account**:
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
action whose ActionCategory is 9 or 15 is a limit break, flagged. The party is `PartyMembers`' entity
ids (the party list's; since 0.5.0, outside a duty, a cross-world party's group: above, What's
recorded; just you alone), read four times a second: **cross-world parties count in the meter
everywhere** (before 0.5.0, outside a duty only your own numbers were). Damage between party members isn't kept,
nor anything between two others; a barrier (a status named on `CombatLists.ShieldNames`, found on the
English Status sheet) is a shield put on, never healing. A tick (actor control 23) goes to the party
member the game names (a pet's owner); a damage tick that names no one, on an enemy, is shared
between the party members with one of the jobs' DoTs (`CombatLists.DotNames`) on it then (the tick's
own status when it names one): a whole share each, the remainder to the first in the party list (the
recording's lowest party id), marked estimated; with nobody to share it, it's nobody's; a HoT's tick
that names no one is nobody's; another player's is never the party's. Which DoTs are on whom is what
the game's status lists say (`CombatParser.StatusOn`): the recorder reads every enemy's list each
frame of a pull (for the recording's `combat.st` and the parser at once), and with no pull going
and the meter listening, every enemy's four times a second in combat; a DoT is on from the read
that first has it to the read of its target that no longer does (an enemy out of sight keeps
its own, for a minute). The summary and the site share a tick by the same rows (`combat.st`), so all
three agree. Nonsense is dropped and counted: a hit or a heal over five million that isn't a limit
break. A heal landing on an enemy (a combatant no one owns that
the game calls hostile) is dropped too, never the party's healing, but isn't nonsense: an enemy that
absorbs an element (as some of Eureka's do) is healed by the party's attacks. A FATE's escort or a
quest's ally isn't an enemy, and healing it is healing.

**Encounters** (`MeterSession`): one starts on the first damage between the party and an enemy, either
way (a DoT's tick never starts one); in combat before it, the meter is _waiting_. The party's heals,
shields and statuses in the second before are folded in. It ends when nobody in the party has been
in combat for _A fight ends after_ (4 seconds unless set), when the whole party is down (a wipe), when
the duty says wiped or completed (said within 15 seconds after it ended, it still sets how), or on
leaving the zone, logging out, or the meter going off. Its length (what ENCDPS, HPS and damage taken
a second are over) runs from that first damage to its end: a wipe's last death, else the last damage
between the party and an enemy, ticks included (ACT's convention); a wipe the duty calls a moment
after it ended takes its last death as its end. The summary's `stats.len` and the site's numbers
(Review's and Progress's) use the same rule (`PullStats.Length`). An encounter during a recorded
pull takes the pull's number; else they're numbered from 1 in each zone. The last _Keep the last_ (20) are kept for the session, newest first.

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
  the HP the target was missing when the heal was announced, or the tick came (it reads a little
  high when a hit is on its way, as ACT's does; the recording keeps a tick's HP, `combat.tick.h`, so
  the summary and the site measure a HoT's tick the same). HPS is that over the encounter's length;
  overheal % is overheal over all healed. Shields aren't healing; lifesteal goes to whoever drew it; MP never.
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

**Since 0.4.0** the session also takes what the party used (`ICombatSink.Use`; the parser hands a use
on after its effects, so an encounter its first hit starts has begun, and the meter folds in uses from
the second before as it does effects), and the game side tells it more (below). For each member:
**Casts** (GCDs used: `CombatKinds.Gcd`, the cooldown groups; pets' not), **Potions** (items used in the
encounter, and those in the 30 seconds before it began: the pre-pull tincture, its event at its own time
before 0, said "before the pull"; as the summary's `pots` count them), **Raises given** and **Raised** (a dead
member's HP above 0 again, credited as the recording credits it: to the Raise status a party member put on
them while down [verify that the raiser is its source], else the last raise used on them while down, or the
healers' limit break 3 on everyone down then), **Time dead** (each death
not the wipe's to when they were up, or the encounter's end), **Interrupts** (an enemy's cast the game
said was interrupted, credited as the recording credits it: `CastWatch`). Each death carries when they
were up again and who raised them, and whether Weakness or Brink of Death was on them. And the
encounter's **events** (`IMeterSource.Events`, the newest 200, oldest first; their text has names, so it
stays in the game): deaths (the wipe's said so), raises, interrupts, interruptible casts that went off (a
quarter second past their end with no word of them stopping), stuns, sleeps, binds, heavies and silences
a party member's action put on someone outside the party (its message's status-only effect: one that came
with damage isn't seen, nor one with no status effect; the game side never reads whether such a target is
hostile, so it goes by not the party's; the site counts the same, `pullEvents.ts`), a status resisted, potions,
limit breaks, tank swaps (a boss's target held a second or more by a tank after another tank, not as its
cast's target) and a non-tank holding a boss (said when it moves off, with how long). **The limit break
gauge** (`IMeterSource.LimitBreak`): bars full, the bars there are, the units in the one filling, units a
bar; none outside a party that fills one.

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
zone, the recorder's pull, the party (`PartyMembers`', since 0.5.0 a cross-world party's group too;
names read once each and kept only in the plugin; HP from the party list, or a cross-world member's
character when near; in combat from the game's own look-up) and whether anyone's in combat. Since 0.4.0 also
each frame the limit break gauge (`LimitBreakController`) and, during an encounter, which party members
carry Weakness or Brink of Death (their status lists); four times a second with the targetability, the
enemies' casts (and whether they can be interrupted) and the bosses' targets (the enemies with half the
biggest's max HP or more; whether they're casting at that one); and every cast the game says stopped
(`FightRecorder.CastStopped`, recorded pull or not). Reading the game costs little: no allocation per message on the hot path (a test reads an eight-target action twenty
thousand times and allocates nothing), the object table only four times a second during an encounter.

## The meter's windows (`UI/MeterWindow.cs`, `UI/MeterDetailWindow.cs`)

Drawn from `IMeterSource` only (`Core/Meter/MeterModel.cs`; the plugin's is `CombatMeter.Source`);
`Model/MeterView` orders and words what they show, `Model/MeterLayout` sizes and places the small
one (both tested).

- **The meter** (`MeterWindow`, "Meter", 330 wide at first, translucent as Settings says: its
  background rgb 13 13 16 at _Background_, its rows solid, 10px corners). Shown with _Show the meter_
  (or `/raidplan meter`), in duties or everywhere. Its head (36 tall): the fight's clock in mono
  (600 15px) with the ember dot beating while a fight is on (a still faint dot otherwise), a _Wipe_
  or _Clear_ tag when the fight shown is over, the picker ("Pull 12 ⌄", 24 tall) and the lock (24px:
  ember-washed when locked, a pointer when click-through, quiet when open); the buttons are laid
  first and the tags only drawn as far as they fit before them. Locked, it has no title
  bar and can't be moved or sized (its tabs and picker still answer); click-through, the mouse goes
  to the game (`/raidplan meter unlock` or Settings gives it back); open, Dalamud's title bar comes
  back with its menu, its × and the resize grip. Five tabs (27 tall, the ember bar under the one
  you're on; a meter too narrow for the five has compact's tab menu in their row): **Damage** (ENCDPS, or DPS in Settings, and the share; pace from 10 s into a phase
  against the picked pull: "PACE · P2 +0:41", a 3px bar with this pull in ember and the other's tick
  in white, "on pull 4 +3.4%"), **Healing** (HPS and overheal %), **Taken** (damage taken and its
  share), **Deaths** (each death's spot, name and job, its time in red, what killed them and how big,
  since 0.4.0 "Weakness" when they died with it on and "raised by H1 after 0:11" when they were
  (`MeterText.DeathLine`); the wipe's left out; live, "7 alive"; past the room cut, and said: "· 3 not
  shown"), **Events** (0.4.0, below). Rows are 25 tall: the spot (18px), the name (or the job
  alone, _Jobs only_), the job in mono, the number in mono 600 13px, its second column, and a 2px bar
  under it to the top player's (grey; yours ember; a death's dimmer, its name muted with a red
  skull). The limit break has its own row under a hairline; the party's number at the foot ("Party ·
  165,000", "… HPS"), with where in the fight, "shields not counted", "incl. bleeds", "kept as pull
  12" or "click-through" on the right. A total of ten million or more is shortened in the 66px column ("13.1M";
  "3,912,400" whole). The rows' words and bars, and the fight's other words (its clock, which pull,
  its deaths' lines), are worked out once a snapshot (the source makes one four times a second
  during a fight), not each frame (`MeterView.Lines`, and the window's own words for the fight), and
  the kit keeps the capitals it has made (`Txt.Caps`) and the words it has cut to fit (`Txt.Fit`, by
  face, size and whole pixels; since 0.4.0), so tags, labels and cut names make no strings each frame. Before any fight, the
  Boss asleep, "No fight yet", "It starts on the first hit." and a quiet _See a sample_ link (the
  sample fight, below); stopped (`IMeterSource.Problem`), the
  Boss awake, "The meter has stopped" and why (`MeterView.StoppedLine`; a kept fight can still be
  picked); in combat before the first hit, "0:00" with "Waiting for the first hit" and the party with
  dashes; between pulls it folds to one line (the last fight's length and tag, "Pull 12 · party
  163,188", a chevron that opens it again), or hides with _Hide out of combat_. A new pull shows itself, whatever was picked before it.
  Pressing a row opens the meter opened up on that player; a death, on its recap.
- **Events** (0.4.0; `IMeterSource.Events`, the session's newest 200 a fight): the fight's events as a
  feed, the newest nearest the head (growing up too), a 23px row each (20 compact): the time in mono
  (11px, faint, in a 30px column), the kind's Lucide mark in its colour (`MeterText.Tone`: a death's
  skull, a cast that went off's warning and aggro lost red; a raise's heart and an interrupt green;
  the limit break's sparkles and a potion's flask ember; a stun's bolt, a resist's shield and a tank
  swap's arrows muted), the member's spot, and the event's own words (the session's, names and all:
  they stay in the game), cut to fit, a death's in its red; pointed at, the kind, the time and the
  whole line. Pressed, the meter opens up on Events, picking the member (a death, on its recap). Past
  the room the oldest are cut (64 are kept for the feed); the foot counts them by kind, the most said
  first ("2 deaths · 1 raise · 3 interrupts"), and an empty fight says what will show. Compact's tab
  menu counts them as it does deaths.
- **The limit break gauge** (0.4.0; `IMeterSource.LimitBreak`; Settings → Meter → _Limit break gauge_:
  Head, Foot or Off, Foot at first): while a fight is on, "LB" in faint mono 9 and a bar each the party
  has (16 wide, 3 apart, 5 tall; 4 compact): a full one the cast bar's ember gradient with a faint
  ember-light ring, the one filling its share in deep ember, the rest a track (`MeterText.Bars`,
  worked out again only when the gauge changes). In the head after the clock and its tags, as room
  allows before the buttons; in the foot right-aligned before what it says on the right, when there's
  room after the party's number (on Events, after its counts). Pointed at: "Limit break: 2 of 3 bars,
  the next 25% full." None outside a party that fills one, or for a kept fight.
- **Copy as text** (0.4.0; the picker's _Copy as text_ row, with the copy mark, under the fights):
  the fight shown, as a mini-parse in ACT's spirit, to the clipboard (`MeterText.Parse`), a line
  each: "Example Arena · Pull 12 · 3:31 · Wipe in P2 · Party 165,000 ENCDPS" (a live one "So far";
  a clear without the phase), then each player biggest first, "Rook (DRG) 27,840 · 16.9% · 0
  deaths" (with _Jobs only_, "M1 DRG …": no names), the wipe's deaths not counted, and the limit
  break last when it was used ("Limit break 7,000 · 4.2%"). The foot says "copied to the clipboard"
  for 2.5 seconds.
- **Its height**: folded, empty or stopped, the window is as tall as what it shows (ImGui fits it,
  its width kept). In a fight (or waiting for one) it's as tall as its party needs
  (`MeterSizes.Needed`: the head, the tabs, pace's room when pace is on, the column heads, a row
  each, the limit break's room and the foot, so it doesn't jump when pace or the limit break comes:
  364 for eight, the title bar's height on top when it shows), and never taller than the height you
  gave it by its edge (`MeterHeight`, `MeterCompactHeight` in compact, kept once you let go: none
  until you do; one taller than the party needs goes back to what they need, and rows past one
  shorter are cut; 0.3.0 kept 382, the height it first opened to, by itself, so a 382 saved by it is
  read as none). A bigger party makes it taller, a smaller one shorter (a member joining mid-fight
  adds a row).
- **Where it stays put** (_Grows_, `MeterGrows`): as its height changes by itself (folding, opening,
  a bigger party, compact), growing **down** keeps its top edge where it is, growing **up** its foot.
  Its anchor (`MeterAnchorX`, `MeterAnchorY`, screen px: its top-left corner, or its bottom-left
  growing up, `MeterAnchorFor`) is read from where the window is the first time it's drawn (so one
  saved by an older version keeps its place), moved to the other corner when Grows changes (so it
  doesn't move then), and follows the window while you move or size it (unlocked; saved once you let
  go, not each frame). Each frame the meter is placed from it (`MeterLayout.Place`, ImGui's
  SetNextWindowPos before Dalamud's Begin: it never sets the meter's Position), kept whole on the
  game's screen while the anchor is on it (moved in as little as it takes, its head's edge first
  when it's taller than the screen, and back onto it after the resolution shrinks; the anchor itself
  stays, so it goes back as it shrinks; with Dalamud's multi-monitor windows on, an anchor off the
  game's screen is on another monitor, and left alone); while you hold it (the
  left button went down on it and is still down) it's neither placed nor sized.
- **Growing up, the head at the foot**: the clock, the picker, the lock (and compact's tab menu)
  sit at the foot, where it's anchored, and everything else is the other way up from them (as WoW's
  meters that grow up have it): the tabs over the head, pace over them, the column heads over those,
  the rows reading up from them (the top player nearest the head), the limit break over the rows and
  the party's line at the top; on Deaths the first death nearest the head, "7 alive" over them; the
  empty and stopped states the Boss and its words over the tabs; the waiting state likewise. A row
  reads as ever (its bar still under its words); each band's hairline is on its side away from the
  head, the limit break's and the foot's toward it; the column heads hug the rows (at their band's
  top). The menus open upwards (below).
- **Compact** (`MeterCompact`; Settings, and the picker's foot): the same meter with less air
  (`MeterSizes.Compact`): 20px rows (the spot 16, the name at 12px, the job in mono 9, the number in
  mono 600 12px, the share in mono 10px), the bar drawn as the row's background (6 in from each side,
  a pixel short of the row's edges, white at 5.5%, yours ember-washed, a death's at 3%, with a 1px
  line of the bar's colour along its foot: grey, yours the ember gradient), a 30px head (the clock at
  13px) with a hairline on its far side, 22px pace, 16px column heads, a 21px limit break, a 22px foot
  (mono 10), 10 in at the sides and 6 between columns, and a death on one 20px line (its spot, name,
  job, what killed them and how big cut to fit, its time in red). The four tabs are one dropdown by the
  lock (the tab's word and a chevron, the picker's button), its menu the site's (200 wide): Damage,
  Healing, Taken, Deaths with the opened-up window's icons, the one you're on ticked, the deaths
  counted. The eight and the limit break in 271px instead of 364: where the normal meter shows 8
  rows, compact shows 12. The opened-up window is the same either way.
- **The picker** (the site's menu, 300 wide, 6px off the head and 14 in from the meter's side: down
  from a head on top, up from one at the foot, the other way when there isn't room; compact's tab
  menu the same, by its button): the shown fight's duty's fights newest first ("Tonight
  · Example Arena"), the one shown ticked, the live one tagged _Now_, the best (the one that got
  furthest) starred ("Pull 4 · the best", added at the end when it's older), each with its length in
  mono and its outcome's tag; other duties' after; and _Pace against_ (Best, Last, Off) and _Compact_
  (a switch) at its foot. While the sample is on, _Stop the sample_ first.
- **The sample fight** (`Model/MeterPreview`): Settings → Meter's _Show a sample fight_, the empty
  meter's _See a sample_, or `/raidplan meter preview` show the meter mid-fight with made-up numbers,
  to place and size it: the mock-ups' pull 12 in Example Arena, eight players, played from 2:15 with
  its clock running and its numbers moving four times a second (bursts in the buffs at the pull and
  at 2:00, a raidwide every 24 seconds, each player's rate rising and falling with their rotation;
  ENCDPS at 3:11 is the mock-ups'), the limit break used at 0:58, P2 from 2:30, pace against pull 4
  (11 with Last) from 10 seconds into each phase, Odette dying at 2:51 and Fenn at 3:22, and the wipe
  at 3:31 (the rest within two seconds: the wipe's, counted against no one); it stays on the wipe
  for 8 seconds and plays again. Its earlier pulls (11, 10, 9, and 4, the best) are in the picker,
  and the opened-up window has its abilities. Since 0.4.0 it has events too (tinctures at the pull and
  at 2:00, a stun on an add, the limit break, an interrupt at 1:24 and a cast that went off, a tank
  swap, the boss turning to a healer, the deaths), a limit break gauge (three bars filling 350 units a
  second, two spent at 0:58), each player's GCDs, tinctures, interrupts and time dead, and in the
  earlier pulls long enough Odette raised by Sylvie (H1) nine seconds after she died. Under the _Preview_ tag (pressed, it stops; in the
  foot when the head hasn't room), with _Stop the sample_ in the picker; the button, the command or
  the meter's × again stop it too. The windows read it instead of the meter's own numbers, never
  both, and what was picked in one (a pull, a player) is let go of in the other. It doesn't start
  while a real fight is on (in combat or after its first hit), and one starting ends it at once.
- **Opened up** (`MeterDetailWindow`, "Raid Planner meter", 1180×560 at first, 980×360 at the least
  since 0.4.0, for the fifth tab; `/raidplan meter big`): the five tabs full size with their icons and
  the deaths and events counted; the clock,
  where in the fight ("P2 · 0:41 in"; these two only as they fit beside the tabs), which pull ("Pull
  12 · now ⌄"), ENCDPS or DPS, a quiet square _Copy as text_ (0.4.0: the copy mark, a tick while it
  says "Copied to the clipboard" for 2.5 s; the small meter's text) and _Small_ back to the meter
  (turning _Show the meter_ on if it was
  off). A slab with every column (Damage: ENCDPS, DPS, damage, %, crit, DH, max hit, deaths;
  Healing: HPS, healing, over, %, crit, max heal, raises given (0.4.0); Taken: taken, a second, %,
  max hit, deaths, time dead (0.4.0, red when any; the wipe's not counted); a count of none is "–"), a 34px
  row a player, the picked one (the top one when none is, or the one picked isn't in this fight) on
  a white wash under the reticle, the limit break (its biggest hit's action after its name), the
  party's total under a stronger rule (no DPS, crit, DH or biggest hit: they don't add up); its cells
  worked out once a snapshot. Beside it, from about 1100 wide (narrower, the table alone), the picked
  player by ability (`IMeterSource.Abilities`), biggest
  first, a DoT's ticks tagged _DoT_ with blank crit and DH, _est._ when the game's tick was shared, a
  slim bar each, the rest summed as "9 more". On **Deaths**, the deaths (the wipe's muted, "The
  wipe"; two fifths of the width) and the picked one's recap: "Odette died at 2:51" as a small callout, the killing blow and
  how far over, then its last hits and heals oldest first (the time to a tenth, what it was with its
  note, the amount red for a hit and green for a heal, the HP before as a bar against their max HP
  (`DeathEntry.MaxHp`; the most the recap saw when it isn't known), amber when low, red at none), the
  killing blow (the line the death names, not always the last) on a red wash. Since 0.4.0 a death's
  row says Weakness and its raise after what killed them, and the recap says under its killing blow
  "Died with Weakness · Raised at 3:02 by H1 · 0:11 dead" (green), or "Not raised" (muted)
  (`MeterText.AfterDeath`). On **Events** (0.4.0), the table in spot order with each player's
  GCDs (_Casts_), _Potions_, _Interrupts_, _Raises_ given, times _Raised_ and _Time dead_ (the party's
  sums at the foot), three fifths of the width, beside the fight's events, oldest first, in a slab
  that scrolls (a 34px row each, only those in view drawn: the time in mono, the kind's mark in its
  colour, the spot, the event's words, its kind in faint mono capitals on the right); pressed, a member's
  event picks them in the table, a death's opens its recap on Deaths. A line under each tab
  says how its numbers are worked out (Events: casts are GCDs, an interrupt is credited to whoever's
  interrupt hit the cast in the second before, time dead leaves out the wipe).
- **Settings → Meter**: _Show the meter_ (`/raidplan meter` too) with _Where_ (In duties,
  Everywhere) and _Hide out of combat_ under it; _The number_ (ENCDPS, DPS); _Pets in their owners'
  rows_ and _Limit break on its own row_ (always, as ACT counts them: shown on and dimmed); _Names_
  (Names, Jobs only); _Lock it where it is_ with _Click-through when locked_ under it; _Grows_
  (Down, Up: which edge stays put, the head going with it); _Limit break gauge_ (Head, Foot, Off;
  0.4.0); _Compact_;
  _Background_ (a slider with its value in mono, 20% to 100%, as the meter draws it); _See it in a
  fight_ · _Show a sample fight_ (_Stop the sample_ while it's on; dimmed while a real fight is on,
  "After this fight"); _A fight ends after_ (2 to 15 s) and _Keep the
  last_ (5 to 50), read by the meter's session; _Pace against_; _Use ACT's numbers_ (not in this
  version: dimmed, saying so); _Kept fights_ · Clear. When the meter has stopped itself
  (`IMeterSource.Problem`), an amber warning box says why.

## The overlays: cooldowns and callouts (`UI/OverlayWindow.cs`, `UI/CooldownWindow.cs`, `UI/PartyCooldowns.cs`, `UI/CalloutWindow.cs`)

Since 0.4.0 two small windows sit over the fight as the meter does (`OverlayWindow`): their background
rgb 13 13 16 at their own _Background_ (their words solid), 10px corners, as tall as what they show
(worked out before ImGui's Begin, `Measure`), as wide as you make them (`OverlaySettings.Width`, kept
once you let go), kept at their anchor as their height changes (`MeterLayout.Place`: the top edge
growing down, the foot growing up, on the game's screen; the anchor read from where the window is the
first time, moved to the other corner when Grows changes, following you while you move it). Before
you've placed one it goes where `OverlayLayout.FirstAnchor` says (the callouts centred a fifth down
the screen, the tracker at the left a third down). Unlocked, Dalamud's title bar ("Cooldowns",
"Callouts") with its × (the setting's _Show_, or the sample's stop); locked, no title bar, no moving
or sizing; click-through when locked, the mouse goes to the game (`/raidplan cooldowns unlock`,
`/raidplan callouts unlock`). They never take focus, make no window sounds and ignore Escape; while
you hold one (unlocked) it's neither placed nor sized. A sample of each shows under a _Preview_ tag
(pressed, it stops), from Settings or `… preview`. Each has its settings apart in the config
(`Configuration.Cooldowns`, `.CalloutOverlay`: shown, where, locked, click-through, Grows, compact,
background, width, anchor), and the plugin keeps each window open while it's shown or sampled.

**The party cooldown tracker** (`CooldownWindow`; Settings → Cooldowns; `/raidplan cooldowns`; off at
first, in duties or everywhere). Small rows, one a party member's tracked cooldown, by kind (raid
buffs, party mitigation, the healers' party heals and mitigation, Swiftcast, the tanks' Provoke,
Shirk and invulnerabilities, interrupts and stuns), then as the catalogue lists them, then in spot
order, a hairline between kinds. A row (24 tall, 19 compact): the action's own icon from the game
(`ITextureProvider`, its corners rounded 4px, at 45% while it recharges; its kind's Lucide mark in a
quiet square while it loads), the member's spot (16, 14 compact) in their role's colour, the action's
name in the client's language (white when ready, muted while recharging), and on the right the time
to ready in mono ("Ready" in green, "42" in seconds rounded up, "1:05"), "1/2" before it in faint mono
for an action with charges. Behind it the recharge fills the row as the compact meter's bar does:
white at 5.5% (yours ember-washed) to how far it has come, its 1px line along the foot (yours the
ember gradient); a ready row is washed green at 6%. Pointed at, whose it is and how it's known. The
limit break gauge last (_Limit break gauge_, on at first: "Limit break" and its bars, the meter's,
from `IMeterSource.LimitBreak` while the meter's numbers are read). Shown when it has a row; with
_Hide those ready_, only what's recharging is listed (a row comes when it's used, goes when it's back);
_Yours too_ (on) lists your own.

- **What's tracked** (`Model/Cooldowns.cs`, `CooldownCatalog`): each cooldown by its English names on
  the Action sheet (a row of any of them counts: Mug and Dokumori are one; Physis and Physis II),
  the jobs that have it, and whether it's on at first: the twelve raid buffs (Battle Litany,
  Brotherhood, Arcane Circle, Dokumori, Battle Voice, Radiant Finale, Technical Step, Searing Light,
  Embolden, Starry Muse, Divination, Chain Stratagem); Reprisal, Addle, Feint, Troubadour, Tactician,
  Shield Samba, Dismantle, Divine Veil, Passage of Arms, Shake It Off, Dark Missionary, Heart of Light,
  Magick Barrier (on), Tempera Coat, Nature's Minne, Improvisation, Mantra (off); Temperance,
  Expedient, Fey Illumination, Collective Unconscious, Neutral Sect, Kerachole, Holos, Panhaima (on),
  Liturgy of the Bell, Asylum, Plenary Indulgence, Sacred Soil, Summon Seraph, Seraphism,
  Macrocosmos, Earthly Star, Physis II, Philosophia (off); Swiftcast for the raisers (on); Provoke,
  Shirk, Hallowed Ground, Holmgang, Living Dead, Superbolide (off); Interject, Head Graze, Low Blow,
  Leg Sweep (off). Settings → Cooldowns lists them by kind with a switch each (`CooldownPicks`
  keeps only what you changed) and the kind's head turning them all on or off ("Turn all on", "Turn all off").
- **How it knows** (`PartyCooldowns`, the game side): the Action sheet read once in English on a
  worker (`CooldownCatalog.Match`, tested: rows that are the player's and not PvP, by name, each
  row's id to its cooldown; for a name with no player's row on the sheet, a job's own row (a job
  category and a level): Starry Muse, placed through Scenic Muse, isn't marked the player's but is
  the id the game says was used. A pet's row (the fairy's Fey Illumination) and a follow-up's (the
  bell going off) don't count: a second "use" a moment later would teach a recast that short. The
  row standing for it gives the icon, the base recast and the charges, and whether it's a role
  action: one of its first name, with a recast, a job's, the lowest id, in that order; and the
  lowest level of its job rows is when it's first had: Mug's 15 for Dokumori). A cooldown above a
  member's level now (the party list's, synced; yours your character's) isn't listed. While the tracker is shown where it may be, it listens to the recorder's combat parser
  (`ICombatSink.Used`: every action a party member or their pet uses, as the game's message has it,
  zero targets too) and asks the recorder to keep its hooks on (`FightRecorder.HooksWanted`). The
  party is `PartyMembers`' (the party list, or since 0.5.0 a cross-world party's group), read four times a second, its spots by `Core/Roles`. A new zone forgets
  every use, and so does the duty starting again after a wipe (`DutyRecommenced`: the game resets
  recasts then).
- **Your own** are the game's timers each frame (`ActionManager`): the recast timer of the action's
  own row (not what it turns into for a moment: Technical Step's while dancing); with one charge its
  time left exactly (total less elapsed); with charges, the game's count (`GetCurrentCharges`) and the
  next charge by the recast `GetAdjustedRecastTime` gives (your traits at this level), the maximum by
  `GetMaxCharges(id, 0)`.
- **The others'** (`CooldownBook`, game-free and tested): each (member, cooldown) keeps its charges
  and when the charge being recharged began; charges come back one a recast, one at a time. The
  recast is the sheet's base, or for a role action you have too (Reprisal, Addle, Feint, Swiftcast…)
  the game's for you; until a use comes more than a second before the recast says it could
  (SPEC4 §2.13: a trait, a level the sheet doesn't know), when the time since the recharge began is
  learned as their recast. A use within a second of their last of the same cooldown is the same one
  seen twice (a pet's echo). One they haven't used since the tracker began is taken to be ready, and
  its tooltip says so; a learned or base recast says it's an estimate.
- **Planned soon** (since 0.5.0): while your plan follows a pull (its clock running), a row whose
  member the plan has use that cooldown in the next 15 seconds (or did in the last 2:
  `PlannedUses.Next`, the soonest) is outlined in ember (1px, 4px corners) with "planned in 8 s"
  ("planned now" at its time) in ember mono before its time, and its tooltip adds "Planned: Reprisal at
  3:02, for Akh Morn." The plan's entry is the row's cooldown when its action is the row's, or one of the
  cooldown's rows on the sheet, or its English name is one of the cooldown's (`CooldownView.Matches`).
  Such a row is listed even when ready with _Hide those ready_ on. Only tracked cooldowns (the catalogue,
  as picked) are marked: a planned Rampart, which the tracker doesn't follow, isn't. The sample marks
  the MT's Reprisal every half a minute.

**The on-screen callouts** (`CalloutWindow`; Settings → Plan window → On-screen callouts;
`/raidplan callouts`; on at first). While your plan follows a pull (`PlanService.Following`, its
clock running: a cue has matched, `PlanService.PlanClock`; with recording off, the callouts being on
is enough for a pull to be followed, as the plan window being open is), its next steps over the fight, each with
a countdown to when it goes off: a step's time is its earliest cue's (`CalloutTimeline.StepTimes`;
a step with no cue has none and isn't shown), counted on the plan's own clock, which the cue follower
keeps to the fight (a cast matched moves it). The steps going off within _How far ahead_ (15 s at
first, 5 to 60), at most _How many_ (3, 1 to 5), soonest first; one that has just gone off stays two
seconds as "NOW" (`CalloutTimeline.Upcoming`). The first big, as Present's: its callout in Archivo
Expanded ExtraBold at 30px in capitals (22 compact, or for a callout in Chinese or Korean, which the
30 hasn't the glyphs for), two lines at most, white; its countdown in mono 600 at 30 (18 compact)
right-aligned in a 70px column before it (54 compact), white, ember in its last seconds (the sound's
lead, else 3) and as "NOW"; the step's name under it (12px, muted) when the callout isn't just the
name; a slim cast bar under them filling as it comes (over the time looked ahead). The next ones
under it at 17px (16 compact) on one line each in the text colour, their countdowns in mono 16 (13)
muted. 12px down and 14 in; 12 between the first and the next, 8 between the rest. Since 0.5.0, under
the first step's name, your spot's planned cooldowns in it (`CalloutLine.You`, `PlannedUses.YouLine`;
your spot is `PartyMembers.YourSpot`, read once a second): "You:" in ember and what to use in white
(SansSemibold 13, cut to the width): "You: Reprisal", several joined ("You: Rampart, Reprisal +4 s"),
each with its time off the step's when it has one; none when the step has nothing for you. The sample
says one ("You: Reprisal −3 s").

- **The sound** (_A sound before each step_, off at first): _How early_ (3 s, 1 to 10) before each
  step goes off, one of the game's own sound effects, _Which sound_ (`<se.1>` to `<se.16>`, 2 at
  first; stepping it plays it, and _Hear it_ · Play plays it), through the game's UI as the chat
  callouts' chime (`UIGlobals.PlayChatSoundEffect`, only when the game's function for it was found),
  so it follows the game's own sound settings; no text-to-speech. Once a step a pull
  (`CalloutSounds`): a step the clock passes without it (the plan moved on by a match, or it was past
  when the plan engaged) is never played late; the same plan read again mid-pull keeps what's been
  heard, and so does a plan switched away from and back to in the same pull (what's heard is kept by
  plan); a new pull (`FightRecorder.PullStarted`) plays them again. Not in the sample.
- A plan is read once for the callouts (`CalloutPlan`: each step's time and words, the timed steps
  in the order they go off), so a frame makes nothing but its lines. A countdown still to come
  reads at least "1" (never "0"), as the tracker's times do.
- **Settings → Plan window → On-screen callouts**: _Show the callouts_, _How far ahead_, _How many_, _A
  sound before each step_, _Which sound_, _How early_, _Hear it_, then the window's own: _Lock it
  where it is_ with _Click-through when locked_, _Grows_, _Compact_, _Background_ (0 to 100%, 45% at
  first), and _See them_ · _Show a sample_ (made-up steps a few seconds apart, counting down and
  starting again every 50 seconds). **Settings → Cooldowns** has the same window rows, the sample
  (_See it_: the meter's sample party, each cooldown used now and then), _Where_, _Yours too_, _Hide
  those ready_, _Limit break gauge_, then the kinds.

## Layout

```
RaidPlanner/
  Plugin.cs, Services.cs, Configuration.cs
  Core/       game-free (tested): the recording, its builder and its combat block (CombatLog), the
              store, spots, timeline, words, the site link and calls (SiteLink, SiteRpc, Pacing),
              plans (Plan, a real arena's rings: Rings, CueFollower), pull summaries and their numbers (PullSummary, PullStats),
              the roster and the diagnostics file (DiagnosticsLog), who the party is (PartyPick),
              the gear (Recording: Gear); Meter/: the combat parser (Effects, CombatParser), the sums (Tally),
              phases (PhaseClock), the meter's session (MeterSession) and its model (MeterModel),
              which is which (CombatLists, CombatKinds), who interrupted, the moments before a pull
              and the countdown (CastWatch: CastWatch, PrePull, CountdownWatch)
  Game/       the recorder, its hooks, the game's data, the meter's game side (CombatMeter), plans
              in the game (PlanService), summaries and the roster (Uploads), the party (PartyMembers)
              and your gear (GearReader)
  UI/         the windows: MainWindow (Views/: Home, Pull, Send, Settings), PlanWindow and its board
              (PlanBoard, BoardArt), MeterWindow and MeterDetailWindow, the overlays (OverlayWindow:
              CooldownWindow with its game side PartyCooldowns, CalloutWindow), Glyphs;
              Kit/ (tokens, fonts, text, controls, marks, the Boss, the frame); Model/ (numbers and
              words, curves, polygons, the screens' states, the meter's and a pull's views, the
              meter's sizes and place (MeterLayout) and its sample fight (MeterPreview), its
              0.4.0 words (MeterText), the cooldown tracker's catalogue, book and rows (Cooldowns),
              the callouts' timeline (CalloutTimeline), the plan's cooldowns on its clock
              (PlannedUses), a real arena's floor cut into triangles (ArenaFloor), the overlays'
              settings (Overlay),
              /raidplan's words (Commands), SVG
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
preview` (or `meter sample`) shows the sample fight in it (again to stop; not while a real fight is on).
What follows `/raidplan` is read in lower case with runs of spaces as one (`CommandWords.Normalize`).
Since 0.4.0: `/raidplan cooldowns` (or `cds`, `cooldown`) shows or hides the party cooldown tracker and
`/raidplan callouts` the on-screen callouts (each says in chat where it will show); after either,
`unlock` gives it the mouse back (and shows it), `preview` (or `sample`) shows or stops its sample,
`settings` opens Settings on its tab (Cooldowns, Plan window).

## Not yet

- Not run in the game yet: everything in `Game/` and every window are compiled against Dalamud 15
  but untested. The 0.3.0 look in particular wants checking in the game: the fonts' sizes and
  baselines (Archivo's line is 1.088 of its size, Geist's 1.3), letter-spacing drawn glyph by glyph,
  the Boss's triangles and shading, the toast's own part of the window over the body, the dialog's
  window under the title bar, the meter's click-through and translucent background, and the glyphs
  "●" (the info bar), "×", "→", "·", "–" and "−". Also: the meter folding to its line and opening
  to its height again (ImGui's auto-fit with the width set; a window saved folded when the game
  closed); the meter's anchor (growing down and up: folding, opening, compact, a bigger party, the
  lock's title bar coming and going, a resolution change, a meter saved by 0.3.0 keeping its place),
  moving and sizing it by its title bar, edges and grip while it's placed every frame (it lets go
  while the left button is held on it), the height you give it by its edge being kept, the menus
  opening upwards from a head at the foot (a pivot on a popup), compact's 20px rows and 7px spot
  labels being readable; the menus' shadow rings over the window under them; a Japanese client's fonts (8 of
  Dalamud's, shared by size) and its eyebrows with a duty's name; the font atlas's size and build time
  with Dalamud set to Chinese or Korean (the extra glyphs on six faces); a concave custom arena.
- The 0.4.0 windows want checking in the game: the two overlays' placing (their first place, growing
  down and up, the title bar coming and going as they lock, being held while moved, their width kept,
  click-through), the game's action icons (`ITextureProvider.GetFromGameIcon`) and their rounded
  corners, the 30px Archivo (its size in the atlas; a Japanese client's, Dalamud's font at 30), the
  callouts' countdown against a real pull, the sound (`UIGlobals.PlayChatSoundEffect` with 1 to 16 as
  `<se.1>` to `<se.16>`; whether it follows the game's volume and the chat's sound setting), the
  meter's fifth tab in a 330px meter (and compact's menu in its place when narrower), the gauge's bars
  in the head and the foot, Copy as text pasted into chat (ImGui's clipboard), the opened-up window at
  980 wide. And the tracker's readings: your own recasts from `ActionManager` (`GetRecastTime` and
  `GetRecastTimeElapsed` of the action's own row; `GetAdjustedRecastTime` in milliseconds with your
  traits at the synced level; `GetMaxCharges(id, 0)` as the charges at your level; `GetCurrentCharges`;
  a recast group shared by a row replaced at a lower level, such as Mug and Dokumori), the others'
  uses arriving through the parser for every party member in range (pets' too, as their owners'), the
  game resetting recasts when the duty recommences after a wipe, and Swiftcast's 40 s (a role trait)
  learned for the others from yours.
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
- The round-four recording (0.4.0) wants checking in the game (with BossMod's replay or ACT beside it):
  other members' items arriving as action messages with action type 2, and a high-quality one's id
  +1,000,000; actor control 15's fourth argument 1 meaning interrupted, and the "sent on completion"
  case kept out by the quarter second before the bar's end; Interject carrying effect 76 and Low Blow on
  an immune boss carrying 55 with value 2; the Raise status on the dead member with the raiser as its
  source; Weakness's and Brink of Death's source (no one, or the member); `UIState.Hate`'s `Enmity` as
  0–100 of the top, for your target only; `LimitBreakController`'s `BarCount × BarUnits` as the most and
  `CurrentUnits` as the gauge's total (10,000 a bar); knockback effects (31–36) absent under Arm's Length
  or Surecast, and a damage effect's `Param4` & 4 meaning partly absorbed; `GetAdjustedRecastTime` in
  milliseconds with traits at the synced level and `GetMaxCharges(id, 0)`; `MarkingController.Markers`'
  order (attack 1–5, bind 1–3, ignore 1–2, square, circle, cross, triangle, attack 6–8); the hooks on out
  of combat in a recorded duty costing nothing noticeable, and the pre-pull rows and countdown appearing;
  the hook's target position for ground actions; a damage effect's `Param2` matching the battle log's
  bonus %; the enemies' `TargetObjectId` as their target; the countdown agent's first reading (its
  length rounded up).
- The 0.5.0 readings want checking in the game (Diagnostics' `party-read` and `gear` lines show them):
  `InfoProxyCrossRealm` outside a duty in a cross-world party (`IsInCrossRealmParty`, `GroupCount`,
  `LocalPlayerGroupIndex`, each group's `GroupMemberCount` and members), the members' `EntityId` matching
  their characters' in the object table (and filled for a member in another zone), the party list really
  being empty then (and filled again in a duty, so the cross-world read isn't used there), a friend's
  damage on a target dummy showing in the meter, and a cross-world alliance's groups (yours chosen); the
  equipped-items container's places (0 main hand … 13 soul crystal) as Dalamud's `IGameInventory` gives
  them, its items' base rows and HQ flag, `PlayerState.Attributes` indexed by the game's attribute ids (and the values the character
  window shows, synced in a synced duty), and the average item level as the character window works it
  out (a two-handed weapon counted twice). And the windows: the plan window's cooldown rows, the
  callouts' "You:" line and the tracker's ember outline, against a real plan with cooldowns.
- Summaries waiting to be sent are lost when the game closes.
- Head marker numbers are kept as the game sends them; the site names them from BossMod's data
  where it can (stack, spread, tankbuster…).

## Release notes

### 0.5.0

- **Cross-world parties count in the meter everywhere.** Outside a duty the game keeps a cross-world
  party apart from its party list, which is then empty, so the meter (and the cooldown tracker, the plan
  window's spot, Home and _Send my party to my roster_) counted only you: a friend hitting the same target
  dummy didn't show. Now one place reads the party (`Game/PartyMembers.cs`): the party list, else the
  cross-world party's group you're in (in an alliance, your own group), else just you, and everything
  uses it, the recorder's "is this the party's" test too. The cross-world read is guarded and, with
  Diagnostics on, logged (`party-read`). A member out of sight (whose HP the game doesn't give) is kept
  as they were last seen, so they're never taken for dead, nor raised and killed again.
- **Your gear in the recording.** A recorded pull keeps your job, average item level, each equipped item
  (its row, HQ and name) and your attributes as it began (`gear`), for the site's "Your gear" card and
  the item level beside your personal bests. The pull's summary carries the average item level too
  (`x.ilvl`, only when the gear was read), for the fight page. Diagnostics logs what was read (`gear`).
- **Planned cooldowns from your plan** (made in the site's editor: who uses which mitigation, shield or
  big heal in a step, and when):
  - the plan window lists the step's, yours washed in ember;
  - the on-screen callouts say yours under the coming step ("You: Reprisal −3 s");
  - the cooldown tracker outlines a cooldown the plan has its member use in the next 15 seconds in ember,
    with "planned in 8 s" (shown even with _Hide those ready_ on).
  They're read as the site reads them (an entry with a value that isn't a number keeps it as 0, as the
  site shows it). They need the site's update to `rp_plugin_plan` (it now sends each step's `cooldowns`, and its
  `callout`, which it never sent before, so the plan window's big words and the chat line now use a
  step's callout rather than its name).
- The cooldown tracker's and the callouts' samples show a planned cooldown.
- **Real arenas in the plan window.** A plan whose arena is a real arena's exact shape (the site's new
  `poly` arenas: the game's walkable outline or the encounter data's, polygons with holes) is drawn as
  it is: each platform's floor with its holes cut out, the grid kept to the floor, and the rim (wall,
  death wall or drop) along every outline and hole. Its shape is read leniently (anything malformed is
  left out, never an error) and cut into triangles once, when the step is first shown, so drawing stays
  as cheap as before.

### 0.4.0

- **A party cooldown tracker.** Settings → Cooldowns (or `/raidplan cooldowns`): small rows over the
  fight with your party's raid buffs, party mitigation, the healers' big cooldowns and Swiftcast (and,
  if you want them, Provoke, Shirk, invulnerabilities, interrupts and stuns), each with its icon and
  the time until it's ready. Yours are read from the game's own timers; the others' are worked out
  from when they used them, learning their real recast as they go. Pick which ones you want; lock it,
  make it click-through, grow it up or down, compact, and a sample to place it.
- **On-screen callouts from your plan.** While your plan follows a pull, its next steps' callouts sit
  over the fight, the next one big as Present shows it, each counting down to when it goes off, with
  an optional sound a few seconds before (one of the game's own, `<se.1>` to `<se.16>`). Settings →
  Plan window → On-screen callouts (or `/raidplan callouts`): how far ahead, how many, the sound, and
  the window's lock, click-through, Grows, compact and a sample.
- **The meter's Events tab**: deaths, raises, interrupts, casts that went off, stuns, potions, limit
  breaks and tank swaps as a feed, the newest nearest the head; the opened-up window lists them all
  beside each player's GCDs, potions, interrupts, raises and time dead.
- **Raises and Weakness** in the meter's Deaths and in a death's recap: who raised them, when, and how
  long they were down.
- **The limit break gauge** in the meter's head or foot (Settings → Meter → _Limit break gauge_).
- **Copy as text**: the meter's pull picker and the opened-up window copy the fight as a mini-parse
  (its length, each player's ENCDPS, share and deaths, a line each), for chat.
- **New columns** in the opened-up window: raises given (Healing), time dead (Taken).
- Settings → Recording → _Smoother replays_ (under _Record where the party stands_): places four times
  a second.
- The meter's sample fight has events, a gauge and the new numbers too.
- **Recorded too** (the site's contract, round four; every new key optional, format still version 1):
  every action and item the party uses (and those in the 30 seconds before the pull, with the
  countdown), ground placements, enemy casts stopped and who interrupted them, whether a cast could be
  interrupted, raises (who, when it landed, the healers' limit break 3), the enemies' targets, your
  target's enmity list, the party's MP and barriers, the limit break gauge, head signs, each enemy hit's
  barriers and where its targets stood, knockbacks, absorbed hits, a status resisted, a hit's bonus %
  (positionals), why a party cast stopped, the actions' cooldown groups and charges, your own recasts,
  the statuses' categories, and your spot. About 13–20 KB a minute more.
- **The summary** carries each spot's GCDs, potions, raises given and taken, time dead and interrupts,
  the damage by phase (`ph`) and a few extras (`x`: interrupts of interruptible casts, tank swaps, limit
  breaks, the countdown).
- **Sending a big pull** thins it in a new order: four frames a second to two, then the enmity list
  and MP, then the frames as before, then ground placements, casts, uses, ticks (frames by time now).
- **Fixed**: a hit that did damage and had a status resisted (a stun on a boss immune to it) counted as
  not landed (the game's "status resisted" was read as "fully resisted").
- **Settings → Recording → Diagnostics** (off; off again each time the plugin loads): while on, the raw
  values behind the readings not yet checked in the game (a cast stopped, a hit's bonus %, items' action
  type, the Raise and Weakness statuses' sources, the enmity list, the gauge, head signs, your recasts,
  ground placements) go to `diagnostics-<date>.log` in the recordings folder, for checking the plugin
  against the game. It holds character names. Capped at 5 MB a file, the newest six kept.
- **Fixed in review (the summary)**: its size is measured as the server measures it (jsonb text, a space
  after each `:` and `,`), at most 7 KB, so the 8,192-byte check can't be passed; past it the damage by
  phase goes first (from its latest phases), then the hits, then the debuffs, then `x` (the hits went
  first before). Time dead stops at the fight's end even when the raise came after it. Tank swaps use the
  site's window round a cast (half a second before to a second and a half after) and end the last span
  where the pull was fought (a wipe's last death), so a boss turning back after a buster on the other
  tank, or in the seconds after the wipe, is no swap. The meter counts a stun that came with damage
  (Shield Bash's): the parser now tells its listeners of every status put on (`ICombatSink.StatusOn`), not
  only those that are rows of their own; the meter's time dead stops at the encounter's end too.
- **Fixed in review**: the meter never counted a stun (the game side doesn't read whether an enemy the
  party only puts a status on is hostile, and the meter asked); now any control status put on someone
  outside the party is one. The meter's potions include the pre-pull one (30 s before the encounter, at
  its own time), as the summary's `pots` do. A raise is credited in the meter as in the recording (the
  Raise status's source first, then the raise used). The Events feed keeps the newest 200 events (the
  first 200 before, so a long pull's live feed stopped). Head signs on no one or past the 17 no longer
  make a `signs` event every second. A detour that can't read a message says so once in a while, not on
  every message. The parser's memory of statuses put on is pruned at most once a second and bounded. An
  enemy cast with no length the game gave is taken as instant. The test fixtures' Weakness is no
  longer marked as cleansable (the sheet says it isn't).
- **Fixed before release** (the windows): the cooldown tracker found no row for Starry Muse (the
  sheet doesn't mark it the player's: it's placed through Scenic Muse), so a pictomancer's never
  showed; a cooldown above a member's level now (synced) isn't listed as ready; with recording off,
  the on-screen callouts follow a pull when they're on (before, only with the plan window open); a
  plan switched away from and back to mid-pull doesn't sound a step twice; `/raidplan` reads its
  words in any case and spacing, and `cds unlock`, `cds preview`, `cds settings` work as
  `cooldowns …`; the limit break gauge in the meter's foot no longer runs over the Healing tab's
  "HPS"; a countdown a hair from ready reads "1", not "0"; the sample's earlier pulls' tinctures agree
  with their events; and less is made each frame (words cut to fit kept, the callouts' plan read once, the opened-up
  window's event log's times and Settings → Cooldowns' words made once).

### 0.3.1

- **The meter grows the way you choose.** Settings → Meter → _Grows_: down keeps its top edge where
  you put it (as before), up keeps its foot there, for a meter sitting on your hotbars. Growing up,
  the clock and its buttons go to the foot with it, and the rows read up from them, the top player
  nearest, and its menus open upwards. It stays on the screen as it grows, and remembers where it is
  across restarts. The pull picker now opens off the meter's head (the mock-up's place), not at the mouse.
- **The meter fits its party.** In a fight it's as tall as the party needs, up to the height you give
  it (before, it opened to the height you gave it, whatever the party).
- **Compact.** Settings → Meter → _Compact_ (or the switch at the foot of the pull picker): 20px rows
  with the bars behind them and the four tabs in one menu by the lock, so the eight and the limit
  break fit in 271px instead of 364.
- **See it in a fight.** _Show a sample fight_ in Settings → Meter (or _See a sample_ on the empty
  meter, or `/raidplan meter preview`) plays a made-up pull in the meter under a _Preview_ tag: the
  clock running, the numbers moving, a limit break, pace, deaths and the wipe. A real fight ends it.
- **The reticle's corners are never cut off.** A row that runs a list's or menu's full width (the
  plan window's plans menu, the pulls, the meter's rows) kept its corners just outside the area the
  window shows, so they were clipped; a corner that would fall outside it is now drawn just inside its
  edge.

### 0.3.0

- **The Occult Crescent isn't recorded.** South and North Horn, their critical engagements and
  FATEs and the Forked Tower are left out, even with _In every duty_ on (until now its fights were
  recorded by themselves, trash packs included). The meter still works there.
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
  party, so the site can chart them across every pull, and the fight's length the numbers are over.
- **The same pull reads the same** in the meter after the fight, in the site's Review and in its
  Progress tab: one length for ENCDPS, HPS and damage taken a second (the first damage to a wipe's
  last death, else the last damage); a tick that named no one shared by the game's status lists
  (the DoTs on the target then, the remainder to the first in the party), never by when a DoT was put
  on; and a HoT's overheal against the HP the game had as it ticked, which recordings now keep.
- Fixed: the Party fact cut to "Where th…"; the info bar's tooltip promising a send; plan names
  zig-zagging in the plans menu; links that didn't look like links; a bad token read like the
  instructions; rows off without saying why, or looking on when off; the token row 4px wider than
  the rest; toasts that looked alike and painted over other windows; footnotes too faint to read;
  notes vanishing in a small plan window; no lock until linked; a callout or notes in Japanese or
  Chinese (no spaces) cut on their first line instead of wrapping; a concave custom arena's floor
  spilling out of it.

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
