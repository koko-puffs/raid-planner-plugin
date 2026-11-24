# Raid Codex (Dalamud plugin): the details

How the plugin works, where things are, and why. The site's side (turning a pull into a plan,
the account link, the encounter data) is in kokos-tools' DETAILS.md, under Raid Codex → _From
the game: the in-game plugin_.

## The idea

The plugin only writes down what happened; everything that turns it into steps is on the site
(`src/features/raid-codex/model/recording.ts`). So the plan-making can get better without a
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
wipe itself after that), when you leave the zone, log out, or press Stop (`/raidcodex stop`; it
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

**Names, worlds, the game and the server's time** (since 0.6.0, for the site's logs): as a pull starts,
each member's character name, home world (`PartyMembers`: the party list's `World`, the cross-world
party's `HomeWorld`, your own `HomeWorld`; the row's name from the World sheet in English, `GameData.WorldName`)
and level go into the recording with their spot; never their content id. The game's version
(`Game/GameClock.cs`: `Framework.GameVersionString`, read once) and the server's time
(`Framework.GetServerTime`, whole seconds, made exact by `Core/ServerClock.cs`: the recorder watches it
each frame, and the moment it ticks over to the next second is that second's start on the computer's
clock, so the server's time is the computer's plus that offset, within a frame) [verify: both, in the
game]. Each is left out when it can't be read (a member's name or world unread, the server's time more
than a day off the computer's).

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
  recorded as gone; since 0.7.8 so is every status of someone not read for 5 seconds (gone from the game's objects:
  an enemy that left), as of the last time they were read (`RecordingBuilder.StatusLost`), where before it lasted to
  the pull's end. The names and icons come from the game's Status sheet, once each.
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
- **Turns** (since 0.7.4): each enemy (bosses, adds and helpers) while it's at a mechanic: its cast bar up, or within
  1.5 seconds (`TurnLinger`) of its cast's end or its last hit (an auto-attack isn't a mechanic; a stopped cast's
  mechanic ends 1.5 seconds after it stopped). Its first frame there is only remembered; then a turn of 2°
  (`TurnStep`) or a move of a yalm (`TurnMove`) from how it last stood is a `turn` event with its facing and place, at most every tenth of a second each and 3,000 a
  pull (`MaxTurns`, apart from the 20,000 events'). What shows a rotation's steps (a boss turning between cleaves, a
  cone or line turning over its cast) and a helper's heading, which the frames don't keep. Away from a mechanic a boss
  turning to its tank isn't kept (the frames have that).
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
  icon number, read after the game has unscrambled it, as BossMod reads it): a party member, or an enemy or helper (a
  rotation's "clockwise" over the boss); since 0.7.4 one over a helper the frames haven't met yet is taken in (a
  combatant or part with no owner; in a field zone within 70 yalms of you), and the message's second argument, whom
  it points at ([verify]: BossMod's reading), is kept when it's someone in the recording; since 0.3.0 category
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
  the message holds. Since 0.7.8 each message is checked before it's read (`Game/MemoryCheck.cs`): a plausible
  address, and memory Windows (or Wine) says is committed and readable for all it could hold (`VirtualQuery`; only the
  address when that can't be asked), since a bad read there would crash the game. A handler whose message fails the
  check, whose count is too high three times, or that's called over 200 times in a second (not map effects) is
  ignored for the rest of the session, said once in /xllog: batched arena changes are then missed, the game never at
  risk.

A hook that can't be made (the game changed under the plugin) is left out, the rest still
records, and Settings → Recording says what's missing. Since 0.7.10 each hook is turned on and off on its own (one that
throws no longer leaves the others as they were), /xllog says when the game's function for attacks already jumped
somewhere else as its hook was made (another program's hook was there first: Dalamud hooks where the jump leads, so if
that other hook doesn't hand the game's call on, no attack reaches the plugin), and a pull of 30 s and more in which the
attacks' hook had none says so, with the hook's state (made, on, where, how many attacks since loading).

Enemies are every combatant battle NPC seen, and since 0.7.5 every helper too: the game's invisible
actors (BNpcBase 9020 and their kin) that cast nearly every real AoE, a battle NPC of sub-kind 11
(`FightRecorder.HelperKind`; Dalamud's `BattleNpcSubKind` has no name for it, BossMod's
`ActorType.Helper` is 0x20B). Until 0.7.5 only sub-kinds 5 (combatant) and 1 (a part of one) were
read, so every helper's cast, hit on the party, status and marker was left out: a replay drew
almost no mechanics, and Survival missed the damage they did (recordings from then can't get them
back). A hit from one not seen yet counts only if it's a combatant, a part of one or a helper with
no owner: pets and summons are the party's own. In a field
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
adds, and those never targetable helpers (only their attacks are kept, not where they stood). Since 0.7.8 a game
helper (sub-kind 11) not targetable yet isn't put in the frames at all as they're taken, rather than taken out of
them all at the end (dozens a frame through a whole ultimate); one that becomes targetable is in them from then on.
An enemy seen without a name has it read again at most once a second (`NeedsName(key, t)`), not every frame: most
helpers never have one, and reading it makes strings. A frame keeps the recorder's dictionaries as they are when
nothing in them is left out (`Positions`), not a copy of them. Parts of an enemy (sub-kind 1) were never read each
frame (their hits, statuses and markers come through the hooks), before 0.7.5 or since.
The party is `PartyMembers`' (the party list, or since 0.5.0 a cross-world party's group outside a
duty; just you alone: above), each given a spot from their job's role
(`Core/Roles.cs`): tanks MT then OT, healers H1 then H2, melee M1 then M2, ranged R1 then R2, in
the list's order; a fourth melee takes a free ranged spot (and the other way round), past eight
P9, P10… Since 0.6.0 each also keeps their name, home world and level (above).

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
- **Head signs**, once a second with the waymarks (`MarkingController.Markers`, 17 slots, in the
  Marker sheet's row order, row − 1: attack 1–5, bind 1–3, ignore 1–2, square, circle, cross, triangle, attack 6–8;
  checked against the sheet in 0.7.4's sweep): a `signs` event when any changed.
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
_Smoother replays_, since 0.4.0; on since 0.7.6, turned on once for settings saved before it, `SmoothDefault`). A typical 20-minute pull is about 1.4 MB, and about as much
again with its combat block; one at the caps can be 4 to 6 MB, which is sent thinned (see Sending).
Since 0.7.7 the 20,000 is shared out (`RecordingBuilder.MaxMany`, `DeathRoom`): the many (statuses, aggro and,
since 0.7.8, auto-attacks: in 0.7.7 every hit) stop at 16,000, so the rest is kept for the few that show the fight
(casts, since 0.7.8 any other hit, since a helper's AoE often has no cast, markers, tethers, targets, map effects, waymarks, signs, cancels); deaths and raises go 1,000 past
it. A hit not kept still counts as a death's cause.
Before, whichever came first filled it, and a pull at the cap lost its late mechanics and deaths alike. The
turns (3,000, `MaxTurns`) never count toward it; nor does the site's ranker, which doesn't rank a pull with
20,000 or more events but its turns (it may have lost what came late).

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

Since 0.6.0 (logs phase 2, the site's SPEC10 contract §2; still version 1, every key optional and left
out when not read): on each `party[]` member `name` (the character's, as the game shows it), `world`
(their home world's name) and `lvl` (their level as the pull began), each cut to 40 characters
(`PartyMember.Clean`); top level `game` (the game's version, "2026.10.30.0000.0000") and `startedAt`
(the game server's time as the pull began, ISO 8601 UTC with milliseconds, "2026-11-02T19:59:58.640Z":
the same for the whole party, so the site tells two players' recordings of one pull from two pulls;
`recordedAt` stays the computer's clock, to the second). The logs' Worker adds `party[].char` (its
character id) to what it stores; the plugin reads it as nothing. Names are in the file, so a file
saved or shared carries them (the site blanks them in its own copies while names aren't public).

Since 0.5.0 (round six, SPEC5 §3.6; still version 1, one new optional top-level key): `gear`, the
recording player's as the pull began (above), left out when it couldn't be read:
`{ "job", "ilvl", "items": [{ "slot", "id", "hq", "name" }], "stats": { "str", "dex", "vit", "int",
"mnd", "crit", "dh", "det", "sks", "sps", "ten", "pie" } }`. `slot` is the equipped-items container's
place (0 main hand, 1 off hand, 2 head, 3 body, 4 hands, 6 legs, 7 feet, 8 ears, 9 neck, 10 wrists, 11
and 12 rings, 13 the soul crystal), `id` the Item sheet's row, `name` the item's in the client's
language; a stat is written only when above 0. Read back leniently (`Gear.Read`): an item with a place
outside 0–13 (or a second at a place), no id, or a stat not a whole number above 0 or not one of the
twelve keys is left out; `gear` that isn't an object reads as none.

Since 0.7.4 (the mechanics sweep; still version 1, every key optional):

- `{ "k": "turn", "t", "actor": actorId, "rot": radians, "x", "z" }`: an enemy turning or moving at a mechanic (What's
  recorded, Turns); facing to 3 decimals, its place to 2. None in a pull before 0.7.4.
- `marker`: `tgt` (a party or actor id), whom the game says it points at; left out for none, someone not in the
  recording, or the marked one itself.

`Recording.Parse` reads all of it back leniently: a hit's lists not as long as its targets, or a
combat table whose columns aren't all arrays of the same length, are left out; an optional column not
as long as its table reads as its default; a value that isn't a number reads as 0; a delta below 0 as 0.

## Stalls (`Game/Stalls.cs`; since 0.7.6)

The plugin's own time on the game's thread is watched, always (it costs a timestamp or two a frame). The
parts timed:
- the recorder's frame (`FightRecorder.OnUpdate`);
- the game's messages its hooks read, summed over the frame (`GameHooks.TakeSpentMs`, the game's own function not
  counted);
- the meter's frame and the plan's frame;
- drawing its windows and overlays (`Plugin.DrawWindows`).

A part over 20 ms in one frame is written to /xllog as a warning ("Raid Codex: the recorder's frame took 63 ms in one
frame"), each part at most every 5 s. So is a garbage collection that paused the process that long
(`GC.GetTotalPauseDuration` between frames). That pause is the runtime's, shared with every plugin, and the warning
says so, with the managed memory then; since 0.7.9 also what kind (a full blocking or background collection, a
generation 0 or 1), whether it compacted, how much of the heap was gaps, and whether quiet collections were on.

**Quiet collections** (`Game/QuietCollections.cs`; since 0.7.9). While a pull goes (recorded or followed) the runtime is
asked not to stop the game for a full collection (`GCSettings.LatencyMode = SustainedLowLatency`: a full one runs in the
background instead), and set back as it was when the pull ends or the plugin unloads. After a 970 ms pause in a trial's
second phase: every plugin shares one heap (3.2 GB that night, mostly other plugins' files: a character-sync plugin had
just finished an upload), and whichever allocates when a full collection is due stops the game for it. The setting is
the process's, so it's only taken when nothing else has asked for a low latency, and only given back if it's still
ours. It can't stop a collection a plugin asks for itself (`GC.Collect`) or one the system needs short of memory; those
are still said in /xllog. It costs a little memory while on, never time.

Work that's the game's thread's only because it was simple to put there is kept off it:
- **A pull's end** (`FightRecorder.Keep`): only `RecordingBuilder.ReadOwnRecasts` there. `HasMechanics` and `Finish`
  run on the save's worker; a pull not `current` any more is touched by nothing else.
- **An upload's marks** (`LogUploader.Finish`): `MarkSent` and `MarkRefused` on a worker.
- **Uploads:** none starts while `FightRecorder.InPull`.

## Diagnostics (`Core/DiagnosticsLog.cs`; Settings → Advanced → _Diagnostics_)

Off by default, and **kept as you leave it** (`Configuration.Diagnostics`, since 0.7.5; until then it went off
again each time the plugin loaded, which the user didn't want: "I have also noticed that diagnostics turns itself
off. Why? Please don't do that."). The file holds names and grows while it's on (a file stops at 5 MB, the newest
six kept). While on, the raw values behind each reading that hasn't been checked in the game
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
- `hot-dot` (since 0.7.10): actor control 23 (since 0.8.2 also 1540 and 1541, the ticks) as sent, whatever its second argument: the actor, party or not, its
  arguments 1–6 (as read: 1 the status, 2 the kind, 3 damage or 4 a heal, 3 the amount, 4 or 5 the source). A 0.7.8
  recording of a 7-minute fight with three jobs' DoTs up had no `tick` rows at all. A 0.8.0 log of a White Mage on a
  housing ward's striking dummy (Dia ticking for most of a minute, Regen) had none for the dummy or the White Mage
  either: the ticks don't come as this message there. The only ones were another player's Lucid Dreaming, every 3 s:
  `args 0 11 550 <id> 0 0`, so the layout read is right (no status, kind 11 MP gained, 550, a source), and kind 11
  isn't a heal (it's left out).
  0.8.1 wrote every other actor-control category about the party or an enemy (`actor-control`) and each number
  flying off a character (`fly-text`); its log of a White Mage in a dungeon (Holminster Switch) showed where the ticks
  come: **actor control 1541 for a DoT's** (on the enemy, every 3 s while Dia was on it: `args 0 10017 <the White
  Mage's id> 4294967295`) and **1540 for a HoT's** (on the one healed: Regen's `args 0 36328 <her id> 0`, the heals the
  same as the numbers shown): the status (0 in every one), the amount, the source. Since 0.8.2 those are read as the
  ticks (`GameHooks.HotTick`, `DotTick`), 23 still too should one come; this line writes all three categories as sent
  (`cat`), and the two 0.8.1 lines are gone.
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
written on a worker at a pull's end, a zone change, turning it off, unloading, or past 64 K characters. Since
0.7.8 what's gathered and what's still waiting for the disk count together toward 256 KB (`MostBuffered`; before, the
worker's queue had no cap): past it new lines are dropped and the file says how many; and the busiest lines (each hit,
each enemy cast, the enmity list) are made only when they'd be kept (`DiagnosticsLog.Room()`), not formatted and
then dropped. A
file stops at 5 MB: it's moved aside to `diagnostics-<date>.1.log` (one older part kept) and a new one
begun; the newest six diagnostics files are kept, older ones deleted. Nothing in it throws: a line that
can't be made or written is lost. **The file contains character names** (yours and your party's, and
anyone a status line is about): look before you share it.

## Kept pulls (`Core/RecordingStore.cs`)

In the plugin's config folder, `recordings/`, a file each (named for when, the duty and the
pull), `index.json` listing them (so the window opens without reading every file; made again
from the files when it's missing or can't be read; since 0.7.8 each entry has its party's count, `partyCount`, so
the window says "N places" without reading the pull's file; an entry from before reads it once, on a worker; older
versions ignore it), and `marks.json`, what can't be made again
from the files: each sent pull's id on the site, and the files that couldn't be deleted yet (held
open by something else: tried again at the next load, never listed again). Every write goes to a
temporary file, flushed to the disk, then put in place, so a crash or a power cut never leaves
half a file. A temporary file found at the load is put in place when it's a whole recording and
its own file is missing or empty (a crash between the flush and the move), else deleted; since 0.7.8 never one a save
is still writing (`saving`: before, a load at that moment could put it in place under the save, whose move then failed
and deleted the pull), and an empty pull file left by a crash during a save (not in the index, not being saved) is
deleted. Deleting a pull (the window's) takes it off the list at once and does the disk work on a worker
(`DeleteLater`); every index and marks write is one at a time, always of the latest list (`Persist`, its own lock),
and no save, mark or delete holds the list's lock while it writes, so the window never waits on the disk. The marks
are written before the files let go of are deleted (a crash between leaves them marked gone, deleted at the next load),
and a load keeps a mark made in memory that's still waiting for its write. Names in
the index and the marks count only as plain file names ending `.raidrec.json` (never a folder, a
drive or the index), and unloading waits a few seconds for pulls still being written. A file that can't be read (not a
recording, a duplicate key, a value of the wrong kind) is left out of the list, never the list
itself, and so is an index entry.

Past _Keep the last_, as each new pull is kept, the oldest one the site has (sent or uploaded: since
0.6.0 it keeps them all; before, only a person's newest 30 sends, so an older sent pull counted as
unsent) goes first, and only then the oldest of the rest. Since 0.6.0 a pull still to be uploaded
(`RecordingStore.Waiting`, the uploader's: Upload my logs on, linked, not sent, not refused, since Upload
since) isn't let go of until there are 200 (`MostHeld`), and then the oldest goes. `marks.json` also
keeps the pulls the logs refused for good (`refused`: file → why), so they're never tried again, and the
index has it too (`uploadRefused`). Lowering the number in Settings deletes nothing
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

Since 0.6.0 one call goes elsewhere: a pull's upload goes to the logs' Worker (`https://logs.kokos-tools.com/v1/upload`,
below), with the token as `Authorization: Bearer` (the Worker's contract; still never in the address, never logged).

`Core/Pacing.cs` has the two pacers: `Backoff` (5 seconds doubling to 5 minutes, say, back to none
after a success) and `LiveShare` (below).

## Sending (`Core/SiteLink.cs`, `UI/Views/Send.cs`)

As Decorium's plugin sends a house: the token from the site (`rpp_` and 43 letters, pasted
in Settings → Account, kept masked, never logged), and a POST to the site's database function
`rp_plugin_send` with the site's publishable key, the token in the body. Its answer is the pull's
id there (the window then offers _Open it_, `/raid-codex?from-game=<id>`), or a refusal in the
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
not by count; an empty table isn't dropped. Since 0.7.7 each step is measured, not written out: the
step's file goes through `Utf8JsonWriter` into a stream that only counts its bytes (`SiteLink.Measure`, with
the server's added spaces for a send), and only the step that fits is written as text; before, each step
was a whole string (up to a dozen of them, several MB each, for one long pull). The writer holds what it
writes until it's flushed, so `Recording.Write` hands it on every 64 KB (`Recording.Spill`, in the events', the
frames' and the combat block's loops): a measure of a 2.5 MB file allocates under 1 MB, not 15. The steps, their order and
the file sent are the same (`ThinningTests` checks both against the text at every step's size). The block
says what it lost (`combat.thinned`); and the window says it was thinned. The file itself
is left whole, and goes into the request as it's written (checked as it's copied, never read into a
tree of nodes: a big pull's would be a hundred MB for the game's process to collect). Each
send carries the pull's own id (`p_client_id`, from its file and when it was recorded), so a send
that timed out after it arrived, sent again, is kept once; a site that doesn't take the id yet
gets the send again without it. The send reads the file and posts from a worker; it waits half a
minute and a quarter more for each MB, and stops when the plugin unloads (the list isn't touched
after).

`/raidcodex send last` sends the newest kept pull and says in chat how it went. Since 0.6.0 _Send each
pull_ is gone: _Upload my logs_ (below) sends every pull. A pull sent by hand and uploaded too is kept
once on the site (the same client id; the upload's answer is `duplicate`).

## Uploading (`Core/LogUpload.cs`, `Game/LogUploader.cs`; since 0.6.0)

With _Upload my logs_ on (the default) and linked, every kept pull goes whole to your logs on the site:
`POST https://logs.kokos-tools.com/v1/upload` (the site's SPEC10 contract §1), `Authorization: Bearer <token>`
(the same token; 0.5.x-era tokens work), `Content-Type: application/json`, `Content-Encoding: gzip`,
`User-Agent: RaidPlanner/<version>`, the body gzipped JSON `{ "v": 1, "clientId", "name", "summary",
"recording" }`: `clientId` the pull's own (`SiteLink.ClientId`, the one its summary goes to `rp_plugin_pull`
with, so the site joins them), `name` the pull's (≤ 120), `summary` the summary made as the pull was kept
(kept in memory for the newest 40; null for one kept before the plugin loaded: the server makes its own
anyway), `recording` the file as written (copied in unparsed, as a send's). Whole: thinned as a send is
(`SiteLink.Fit`, the same order) only past 8 MB less 64 KB (`LogUpload.MaxRecording`; the site takes 8 MB
ungzipped, 10 MB gzipped: its Worker has 128 MB, and a recording read there takes several times its size),
which no ordinary pull comes near (a ten-minute savage pull is under 1 MB). The file is read, gzipped and sent on a worker;
it waits half a minute and a quarter more for each MB, and stops when the plugin unloads.

What's waiting is worked out each time from the kept pulls, never kept apart (`UploadQueue`): those not sent
(no `SentId`; an upload's answer is the same inbox id a send's is, so an uploaded pull is marked sent, shows
the tick and opens with _Open it_), not refused, and ended at or after _Upload since_ (`UploadPrefs.Wants`). So
it's the same after a restart, and pulls kept offline go when the site's back. One at a time, the oldest first,
checked four times a second on the framework thread. The answers (contract §1):

| Answer | Then |
| --- | --- |
| 200 `ok` (or `duplicate`: there already) | marked sent with its `id`; the next |
| 400 / 413 / 415 (`recording`, `size`) | marked refused (`marks.json`), never tried again; the next |
| 401 `token`, 403 `full` | every upload stops (said once in chat; Settings shows why) until linked again, Upload my logs turned on again, or Upload my older pulls |
| 403 `off` | everything waits half an hour |
| 429 `rate` (60 sends an hour, shared with `rp_plugin_send`) | waits its `Retry-After` (1 s to 65 min) |
| 503 `busy` | waits its `Retry-After`, and longer each time |
| other, no connection, no answer | 30 seconds doubling to 15 minutes (`Backoff`) |

A pull that fails three times in a row (no answer, or the site busy or erring: not the hourly limit or saving
off) is set aside half an hour, doubling to six hours each time it fails again, and the next pull goes first
(`UploadQueue.Aside`), so one pull the site can't take never holds up the rest; a success or Resume forgets it.
Since 0.7.8 setting a pull aside starts the wait over only when its failures were its own (its file unreadable,
another 4xx: `UploadAnswer.ThisPull`); when they were the connection's or the site's (no connection, no answer, 5xx,
busy, the hourly limit, and a 4xx about the way there: 403, 407, 408, 421, 425) the wait keeps growing to 15 minutes
until an upload goes through, where before each pull in
turn was read, compressed and sent again back to back. A pull just uploaded or refused isn't picked again until the
list shows its mark (or the mark fails: `UploadQueue.Settled`), so a slow disk can't send it twice.
A clock set back by more than a wait counts as waited. An answer for a call made before the token changed is
ignored. _Upload since_ (`Configuration.UploadSince`) is set when 0.6.0 first loads (a 0.5 configuration,
version 1, is moved to version 2: Upload my logs on, from then on; `UploadPrefs.Migrate`) and each time _Upload
my logs_ is turned on, so pulls kept before (or while it was off) go only with _Upload my older pulls_, which sets
it to the beginning of time. The site's answer says whether a pull is public now (`public`); the plugin only logs
it. Your logs are public on the site, with your party's names and worlds, unless you make them private there
(Settings → Your logs, `SiteLink.LogsSettingsUrl`); Settings → Recording says so and links there.

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
with `RP_ALIGN_OUT` set to the site's `src/features/raid-codex/model/__fixtures__` folder, writes
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

_Send my party to my roster_ (Settings → Plan and callouts) sends `rp_plugin_roster` your party's names by spot
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
switched mid-way), and `/raidcodex stop` and Stop only end a recorded pull.

**Saying each step in chat** (_Say each step in chat_ on; `Callouts` in the config): 5 seconds before
a step's first cue (by the same clock), or as its cast begins when that's later, its name and its
callout go to chat (`PlanJson.ChatLine`: "Exaflares: middle, step through the first."; the name alone
when the step has no callout; before 0.3.0 the first line of its notes) with the "Raid Codex" tag,
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
Settings → Windows → Plan window → _Lock it where it is_), Dalamud's own moving and resizing are off and a
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
(`/raidcodex next` and `prev` still move it), and the notes say "Notes: 2 lines, make it taller"
instead of vanishing. A callout or notes in Japanese or Chinese (no spaces) wrap where a line fills
(`Format.Wrap`), not cut on their first line; what's measured before the fonts are built isn't kept. With no plan to show, the Boss says why over a small
callout, one line and one button (`EmptyState`): not linked (asleep in a dashed ring; _Link your
account_, the telegraph, opens Settings → Account), reading (the party in a row over a running cast
bar), gone wrong (awake; the error's first sentence as the callout, the rest under it; _Try again_
outlined), no plans (asleep; _Open the Raid Codex_ outlined), no plan for this duty (its head
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

Since 0.3.0 every window is drawn in the Raid Codex's Ready Check (the site's
`src/features/raid-codex/styles/ready.css`, the mock-ups in kokos-tools'
`docs/proposals/raid-codex/mockup/pages/plugin.html` and `meter.html`), under Dalamud's own title
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
- **Three voices** (`Fonts`, all through Dalamud's font atlas, at fixed sizes, times the windows' size: Size, below): Archivo Expanded
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
  instead (`Txt.For`). Since 0.7.0 the compact meter's theme brings its own faces, loaded only while
  it's picked (above, _The meter's windows_, Compact). Dalamud's language's extra glyphs (Chinese, Korean) are added only to the
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
- **Size** (since 0.8.1; `Kit/UiScale.cs`, `Model/WindowSize.cs`; Settings → Windows → Size, `/raidcodex size`):
  every size is drawn at Dalamud's global scale times the plugin's own (`Theme.S`, `Theme.Scale`), so the
  windows, their words, icons and spacing grow and shrink together. _Fit to my screen_ (on at first,
  `Configuration.WindowSize` 0) makes them as big as the screen is tall over 1440px (in tens of %: 150% on 4K,
  200% from 2880px, never smaller than 100%), counting what Dalamud's own scale gives already (Dalamud at 150% on
  4K: the plugin adds nothing); off, the size picked (80–200%) is on top of Dalamud's. The fonts are built again
  at a new scale once it's held still 0.4 s (a stepper pressed a few times builds once), as a whole new set beside
  the old one, drawn with from the frame every font of it is built (`Fonts.Follow`): `UiScale.Factor` moves on that
  frame, so the words and the windows move together, and what's measured in a font (keyed on `S(1)` and
  `Fonts.Generation`) is measured again. Dalamud's window sizes are in its own units (it multiplies them by its
  scale), so the plugin gives them times its own (`Theme.Units`, `CssLimits` for the smallest and largest); the
  windows ImGui sizes (the main window, the plan window, the opened-up meter) grow or shrink when the whole scale
  (Dalamud's times the plugin's, so a change of Dalamud's own that fitting makes up for moves nothing) moves, by its
  ratio, from the size they were last drawn at, kept on the screen (`ScaleFollower`); their smallest sizes are kept
  to 95% of the screen's width and 90% of its height (`CssLimits.Within`). The overlays and the meter, sized by the
  plugin every frame, follow by themselves (their saved widths and heights are CSS px). The whole scale drawn at is
  kept (`Configuration.WindowScaleWas`; before 0.8.1, Dalamud's alone): ImGui's remembered sizes are at it, so until
  a window has been drawn its limits are at it (not clamped by the new scale's) and the follower then grows it, and
  the meter reads its first width at it; fitting starts the fonts at it (the screen isn't known before the first
  frame). A minimised game (a screen under 200px tall) keeps the size it has. The title bars stay Dalamud's own size
  (its font and buttons, as above).
- **Toasts** (`Frame.Toast`): raised near-solid, 10px corners, a hairline, an icon in its colour
  (it worked green, it didn't red, news ember), at most one action in ember-light ("Open", "Why"), and
  a 2px bar along its foot counting its 2.6 seconds down in its colour; it waits while pointed at and
  fades over its last 0.3 s. It sits at the window's foot (above a footer) in a part of the window of
  its own, so it never paints over another window over this one.
- **The question** (`MainWindow.DrawConfirm`): a window of its own over the plugin's, which dims it
  under the title bar (rgb 4 4 5 at 74%) and takes every click; the site's dialog in its middle
  (raised, 20px corners, a 12% hairline, a shadow faked with rings, its title a small callout, its
  words at 14px, its answers on the right: a destructive one in the danger outline). No key answers it (the plugin reads no keys). It
  fades in over 280ms (it doesn't grow: text can't scale crisply).

## The main window (`UI/MainWindow.cs`, `UI/Views/`)

1040×880 at first and 1040×520 at the least (since 0.8.1; 560×800 and 460×520 before), the least kept to 95% of the screen's width and 90% of its height, the first size too, its header 16px down and 20 in, its body scrolling (16
above, 18 below), a footer on the basalt band where a screen has one.

- **The tabs** (0.8.0, `MainWindow.NavTabs`): _Home_ · _Pulls_ · _Settings_ along the top of every
  screen but the setup guide, the ember cast bar under the one you're on (a pull is the Pulls tab's),
  a hairline across the window under them; _Open the Raid Codex ↗_ quiet at their right (its icon
  alone when the words don't fit, nothing when that doesn't either). They replace Home's old header
  (the state tag, _Open the Raid Codex_, the Settings button) and the back buttons.
- **Home** (`Views/Home.cs`; 0.8.0, the mock-ups' plug-main): between pulls, a _Not kept_ tag and why
  for three minutes after a pull that wasn't kept, then the **status card** (`StatusNow`): a well and
  "Ready for the next pull" (a pull has gone in this zone) or "Waiting for the pull" (none yet), with
  "Example Arena · pull 13" under it (`FightRecorder.PullsHere` + 1); "Following your plan" while a
  pull is only followed; "Recording is off" (with _Turn it on_); "Not recorded here" in a duty that
  isn't recorded; "Not in a duty". Under it a **chip per main part** (`SetupGuide.ChipName`:
  Recording, Uploading, Meter, "Plan: <the plan's name>" when you have one for the duty, Cooldowns,
  Progress): a 6px dot green when on, faint when off, yellow when it's on but waits for your account;
  each opens its Settings page (Recording, Meter, Plan window, Cooldowns; Uploading and Progress
  Recording). Then **Latest pull**: its number as a small callout, the outcome's tag, its length in
  mono; the duty and when, when the status card doesn't name it; the pull in a sentence in plain
  words (`Model/PullGist.Sentence`: "Wiped at **Hello World 2**, P3. Three deaths, the first Velka
  Moor to Latent Defect.", the mechanic in white; "Cleared in 9:41. No deaths."; before its file is
  read, "Wiped after 6:31."); the top part opens the pull (the reticle when pointed at). Under it its
  upload in a row (`DrawUploadLine`): a green tick and "Uploaded 2 minutes ago" with _Open in Review_;
  "Waiting to upload" (or why it's held, stopped or refused, `UploadWaitLine`) with _Send now_;
  "Kept on this PC" with _Link your account_; sending, the cast bar and a quiet _Stop_; failed, the
  red box and its one button. Then **Tonight** (`Model/PullNights`: "Tonight · 12 pulls", "Last night",
  "Friday night", a date; a night runs to 6:00): up to five of that night's earlier pulls as rows,
  and _All pulls →_ to the Pulls tab. While a pull is recorded, the **ember card**: the beating dot
  and "Recording pull 13" in ember mono capitals, _Stop and keep it_ in the danger outline (it ends
  the pull here and keeps it); the duty and when it started; the clock in Geist Mono 600 30px and
  what it runs to ("of 9:41 in your plan", the plan's last cue, `Model/PlanProgress.Length`; else "of
  7:42, the longest here"; else "the first pull here"); the cast bar; "Step 7 of your plan
  **Program Loop**" and on the right "next: Pantokrator in 0:18" (`PlanProgress.Next`: the next
  step's first cue, else its written time). Under it chips for what's recorded ("312 casts · 40
  markers · 6 tethers", "Places of 8 players" or "Places off", "Uploads when it ends" or "Kept on this
  PC when it ends"), then the latest pull dimmed to one line ("The rest waits until the pull ends:
  nothing here needs you while you fight."). Before the first pull: the status card, then the Boss
  asleep, "Nothing pulled yet" and one line, three steps numbered as waymarks (1 red, 2 yellow, 3
  blue squares), and _Link your account_ (the telegraph) until linked.
- **A pull's facts** (`Model/PullGist`, `Views/Pulls.cs`): the phase it ended in (of the phases the
  recording saw, `PullView.PhaseStarts`), the mechanic it ended at (the last before the wipe's first
  death, `PullView.WipeAt`, else the end), its deaths before the wipe and the first of them (by name
  when the recording has it, else the spot, and what killed them), the party kept. Read from the
  pull's file on a worker, one file at a time, only for rows in view, and never while a pull is
  recorded or followed (its read would cost the game a frame); a pull's own screen fills it from its
  own read. Kept for the session.
- **Pulls** (`Views/Pulls.cs`, 0.8.0): every kept pull, the newest first, by night (each its heading in
  mono capitals: "Tonight · 12 pulls"), each night's rows in a slab (`Kit.PullListRow`): the pull's
  number in mono, its length in mono with a _Best_ tag (each duty's fastest clear that night, else its
  longest pull, `PullNights.Best`), how long ago and where it ended ("14 minutes ago · P3", or "at
  Hello World"; the duty first when it isn't the night's), the outcome's tag, a green tick once it's on
  your Raid Codex (else a faint "waiting" while Upload my logs will send it), a chevron; pointed at, a
  faint fill and the reticle; pressed, the pull. A line on what's kept on top; none yet, the Boss asleep.
- **Sending** (`Views/Send.cs`, a pull not yet on your Raid Codex; Home's latest pull has it in a row, above): not linked, _Link your account_
  is the telegraph and _Save as file_ outlined, "Link once, and each pull is a button away from being
  a plan."; linked, _Send to Raid Codex_ in the same place. Sending, it dims and holds still, a
  quiet _Stop_ beside it (it cancels the send; so does _Unlink_), and a cast bar "Sending pull 12 · 0.6 MB" runs under
  it. Sent, a ready tick, "Sent. It's waiting in your Raid Codex." (or "Sent, with fewer of the
  party's places: it was too big whole."), _Open in Review_ and a quiet _Send again_ (or _Save as
  file_); sent in an earlier session, one line ("Sent yesterday at 23:52.") with _Open in Review ↗_.
  _Open in Review_ opens the plan for the pull's duty in Review on that pull
  (`/raid-codex/plan/<id>/review?pull=<sent id>`) when the plugin has that duty's plan, else From
  the game (`?from-game=<id>`). Failed, a red box that says what to do and the one button that does
  it (`SendFailure`, from SiteLink's words): the token (_Link again_, to Settings → Account), a plugin
  too old (_Save as file_ until then), busy or no answer (_Try again_), timed out or answered without
  the pull (_Open the Raid Codex_: it may be there already), too big or unreadable (_Save as
  file_), anything else (_Try again_; the site's own words for players are said as it says them).
  Toasts: "Sent to your Raid Codex" (Open), "Couldn't send it" (Why: the pull's screen), "Saved …",
  "Couldn't save it there", "Deleted pull 9", "Linked to…", "Unlinked from…".
- **A pull** (`Views/Pull.cs`; 0.8.0, the mock-ups' "A pull"): under the tabs a row with _All pulls_
  (back to the Pulls tab), _Save as file_ and _Delete_ (it asks first: "Delete this pull?", saying
  whether your Raid Codex has a copy; deleted, the Pulls tab). The eyebrow ("Tonight 23:42 · The Omega
  Protocol (Ultimate)", `Format.Day`), "Pull 12" as a callout at 22 with the outcome's tag and the
  length in mono at the right; the pull in a sentence (as Home's, then how far it got against the
  pull before it in the same duty that night: "2 seconds further than pull 11.", "1:12 short of pull
  11."); the pull to its own scale (a 6px track, a tick a mechanic, the phases the recording saw, `P1`,
  `P2`, from its target events by the recorder's rule, and a skull at each death). Its upload: on your
  Raid Codex, a box with a tick, "On your Raid Codex since 23:49" (`PullGist.SinceLine`) and _Open in
  Review_ as the telegraph, with _Send another copy_ under it; else where Upload my logs stands with
  it, and the send (below) with _Send to Raid Codex_ filling the row. Then **What happened** ("4
  waymarks · 2 tethers · 2 deaths", and "the wipe"): its slab of 42px rows filling what's left of the
  window, scrolling inside it and fading at its foot: the time in mono on a rail with a hollow dot,
  the mechanic drawn small in a 30px square well as the board draws it (`Glyphs`: circle, donut, cone,
  line, cross, stack, spread, tankbuster; a raidwide as rings; anything else a quiet ring), what it
  asks (Dodge, Share, Apart, Tanks) before its name, "×12" in mono; deaths as red rows with the job,
  who (by name when the recording has it, else the spot) and what killed them ("Velka Moor died to
  Latent Defect"; the wipe's own, a wiped pull's deaths within 6 seconds of its last, left out:
  `Model/PullView` by `PullStats.WipeDeaths`, the rule the summary, the meter and the site keep).
  Reading it, the party in a row over a running cast bar; a file that won't read, the Boss awake
  ("This pull won't open"); nothing in it, asleep ("Nothing went off").
- **A pull's Replay** (`Views/Replay.cs`, `UI/ReplayBoard.cs`, `Model/Replay/`; 0.9.0): a _Summary · Replay_ switch at the
  right of the pull's title row (with _Open on the Raid Codex_ beside it while Replay is shown, once the pull is there).
  Replay is the site's Review › Replay as it stands on 23 November 2026 (docs/tools/raid-codex/review-replay.md in
  kokos-tools), played back in the window, filling what's left of it: the board on the left, the moment down the
  right, the transport along the bottom. Nothing plays by itself; Play does, at 1× (the site has no speed control any
  more, so neither does this). No key does anything: Play, the step buttons, pressing or dragging along the lanes, the
  tiles and _Just before_ are all pressed.
  - **Worked out once** (`ReplayModel.Build`, on a worker as the switch is first picked, from the pull's file): the
    track (`ReplayTrack`, the site's `model/replay.ts`): everyone's places, facing and HP as sampled (2 or 4 a second;
    0.7.4's turns sampled in between for a boss or an add), eased between samples (`At`, facing the shorter way round;
    someone is away across a gap of more than 3 s); the enemies' casts with their shapes, a cast seen twice taken as
    one, stopped ones ended where they stopped; their hits (`Loose`: no cast of their action ending a second before to
    2.5 s after, and not an auto); head markers (a limit cut's number from the game's icons 79–86, a turn's way round
    from BossMod's rotate icons) and the mechanic each led to (`LinkMarkers`, the site's rule: the first spread, stack,
    buster or line stack on the one marked before their next marker, within 12 s, a limit cut's 30; or a circle where
    they stood that hit at most two; or a single-target buster with its own cast); tethers as spans, waymarks, head
    signs, each enemy's target, the party's ground effects (Sacred Soil 15 s, Asylum 24, …), deaths and raises, the
    fight's statuses as spans, and the rotations (`ReplayRotations`, the site's `rotation.ts`: three or more landings
    from one spot turned the same way by about the same step, a cast its caster turned through, a rotate marker).
    Facing as the site fixes it: a cast whose caster didn't turn points where its own hit went (plugin 0.2.1 on);
    through plugin 0.7.9, whose casts all faced south, the caster's frames, its last turn, the facings of the same
    action's casters beside it, or straight across the arena (`AcrossArena`). The arena (`ReplayArena.Guess`, the
    site's `guessArena` without its encounter data): where the bosses (else the party) mostly stood, snapped to 100,
    100 within 15 yalms, its size how far the party and the mechanics went (20 to 80 across), square when people stood
    in its corners; a plan of yours for the duty (the one open, its arena with a centre: a real arena's polygons) is
    used in its place. Shapes from the game's data (`ReplayShapes.FromGame`: cast types 1–13, a cone's angle from its
    omen's `fanNNN`, the site's few fixes for Seiryu); a floor line that runs through its caster (`ThroughLines`, the
    site's `throughLines`: someone behind its caster within its width hit by it as it went off, and no more left
    untouched there; or, no one ever there, a helper inside the arena whose line is longer than the arena is wide)
    drawn centred on it, the same length; the site also reads a mechanic's role from BossMod's encounter
    data, which the plugin hasn't, so a circle put on players is told a stack or a spread from who its hits landed on
    (`InferRoles`, the timeline's rule). The steps (`ReplayLanes.StepsOf`, as the site's plan made from a pull has
    them): "Pull" at 0, then a step each time something goes off on the plugin's timeline, mechanics within 1.5 s
    sharing one, never more than 8 s nor across a phase, "P2 · " in a later phase; the last runs on for the steps'
    usual length (8 to 30 s). The bosses' buffs, the party's debuff waves and each member's own (`ReplayStatuses`), the
    cards, the deaths and the events for _Just before_ (`LogOf`) are made then too.
  - **A moment** (`ReplayScene.At`, the site's `replay.ts`): its lists are kept and filled again, so playing allocates
    nothing. Each cast's telegraph (`castObjects`' rules: a stack, spread, buster or tower on whom it's on and
    following them, a line stack from its caster, a knockback a ring as far as it reaches or a side push across its
    rectangle, a gaze, circles, cones, donuts, lines, crosses and charges where they began and aimed, turning with a
    caster that turns; none for a raidwide) built by brightness only (30% as it begins to full as it goes off, 0.45 s
    and gone; one stopped goes as it stops; full from the start with reduced motion); a loose hit's shape as it lands;
    a linked marker drawn as its mechanic on the one marked until it goes off; a rotation's arrow round the one
    turning (clockwise orange, anticlockwise blue). Who's dead, raised (1.6 s) or hit (0.4 s), whom each enemy is on,
    head markers' badges (the latest on each for 5 s; one drawn as its mechanic keeps a badge only for a limit cut's
    number or a turn), and the fight's statuses on each (`FightStatusesAt`: a member's debuffs, category 2, or for a
    recording from before 0.4 everything but the party's own by name; an enemy's own buffs; nameless "Status 2273"
    left out; the soonest gone first; a number above 16 a parameter, not stacks).
  - **The board** (`ReplayBoard`): the arena and the waymarks as the plan board draws them; the party's ground effects
    as faint green floors with their names; the telegraphs in the board's kinds and the site's palette; tethers; the
    enemies (a boss's hitbox ring, facing and name; an add's shield), their HP a white ring and a dashed line to whom
    each is on; head signs over the head; the party as their spots' colours with the game's job icon on each, their
    HP a ring (green, amber under half, red under a quarter), a red ring and × on the dead, a green ring as someone's
    raised, a red flash as a hit lands; each member's debuffs as the game's icons beside their token (two and "+2",
    a white ring round each for the time left, none that every living member has at once), an enemy's buffs in a row
    over it; head markers as a small disc over the head in their kind's colour (spread, stack, buster, limit cut's
    number, target, a turn's arrow, a plain pointer for one not known). Viewing as someone (their tile) fades the
    rest. Played to its end it says how the pull ended over the board's foot for 2.6 s ("Wiped 6:31 · Pull 12 · at
    Knockback Wave · the boss at 38%").
  - **The moment** (down the right, 332 px): the step in the callout's voice on one line (smaller past 15, 19 and 23
    letters, then "…"), "P2 · Pull 12 at 2:51"; a card for each boss (and each add with buffs of its own; one a
    name), its HP, its own buffs (three and "+2") and what it's casting under a thin cast bar (the time left, "Went
    off", "Interrupted" in green, "Stopped"; on whom; "can be interrupted"; "+1" for more), "Not casting", dimmed and
    "Away" while none of its actors is there; with two cards or more, _Other casts_ for the helpers' and adds'. The
    party as tiles, two a row (tanks, healers, DPS): job icon, name, HP %, an HP bar with their barrier after it (the
    combat block's `res`, plugin 0.4), the fight's debuffs with the seconds each has left under its icon (as many as
    fit, "+N" in the last slot), dead a skull and when; pressing one views as them; a death played past washes it red.
    Then _Just before_, folded at first (the latest thing faint beside it): the last six things that happened (a
    phase beginning, deaths, raises, interrupts, an interruptible cast that went off, a hit on half the party or a
    buster, the fight's debuffs and the bosses' buffs going on, head markers by their meaning or the mechanic they
    led to), each pressed going to 1.5 s before it. Nothing in it changes size as the pull plays.
  - **The transport** (along the bottom, the site's direction A): the step before (to the start of the step you're
    in first), Play (round, 56 px, the telegraph; Pause while playing), the step after; then one frame of lanes with
    their names in a gutter: the phases with the moment on an ember tag riding over the play head ("2:48 5 · Name",
    held inside the row at its ends); the steps as 56 px blocks, number and (75 px wide or more) name, the played ones
    warm, the moment's ember-edged and filling like a cast bar, a death a red line through them, hatched past where
    the pull ended; the bosses' buffs (blue, a debuff red; at most two rows) and the party's debuffs (a bar each time
    one went round, "×2", a numbered family one wave, "First to Fourth in Line"; at most three rows), packed where
    they're drawn (`ReplayLanes.Pack`: a bar on the first row clear of the last, at least its icon wide; with no row
    left, joined to the bar free soonest, its icons side by side, four and "+n"); the ruler every 30 s (further
    apart when narrow) with a red skull at each death, rung once as it's played past; the play head, white. A lane
    with nothing is left out, and the dock is as tall as its lanes. Its small button at the gutter's top fills the
    window with the lanes (every status its own row, a lane for each member, scrolling) and back.
- **Settings** (`Views/Settings.cs`, its rows in `Views/SettingsRows.cs`; 0.8.0): under the window's tabs (Home,
  Pulls, Settings). The window is never narrower than 1040 (above), so the sidebar is always there unless the screen
  can't fit that (in 0.8.0 it was widened to 820 the first time Settings showed). From 700px wide a **sidebar** (220 wide, on the slab, a hairline at its right): _Find a
  setting_ (the 40px well field with the search icon; a quiet × beside it clears it), then the eight pages with
  their icons (the one you're on lit, its icon ember, its words 600): _Features_, _Recording and uploads_,
  _Damage meter_, _Plan and callouts_, _Party cooldowns_, _Windows_, _Account and privacy_, _Advanced_; at its
  foot the version in mono and _What's new_. Narrower, a page picker (the page's icon and title and a chevron,
  opening a menu of the pages) and the search share a row above the page instead, and the version and _What's
  new_ go to the page's foot. The pages, their sections, every row's name and line, and the search are
  `Model/SettingsIndex` (tested); a row is drawn by what it is (`SettingId`) wherever it's shown.
  - **A page**: its title as a small callout, one muted line saying what it's for (the meter's, the plan's and
    the cooldowns' end "Where it sits: Windows", a link), and _Reset this page_ (quiet, top right; it asks first,
    then puts the page's settings back to a new configuration's: not its buttons, not the windows' Show rows
    on Windows, never where a window sits, never _Upload my logs_, which on would upload pulls that are public on
    the site; Features and Account have none). Then its sections: an eyebrow (mono
    capitals, muted; a faint hint at its right, as Uploads' "Linked to your Raid Codex") over a slab of rows,
    each the setting's name (550 14px) over one muted line, its control at the right (switch, 40px stepper,
    dense segments, slider or button); a sub-row 34 in; a row that can't be used dims its words and control and
    says why in the text colour ("Link your account first: Account", the link going there; "With Record where
    the party stands on."). The rarely changed settings fold under one dashed row, "3 more: smoother replays,
    the server info bar, recordings folder" (its words the rows inside; closed at first, open for the session
    once pressed, a chevron up while open), opening their slab under it. The notes, 13px muted, at the foot.
  - **Find a setting**: typing lists every row whose name or line has each word typed (case, quotes and
    accents don't matter), and each cooldown by its name, jobs or kind, grouped by page and section ("Damage
    meter · The numbers") with _Go ›_ to the page; each row works there as on its page (the theme tiles, the
    account and the cooldowns' kinds, drawn in their page's own panels, are a _Go_ row). A row shown on two
    pages (the meter's _Grows_ and _Background_ on Damage meter and Windows) is found once, at its home.
  - **Features**: the six main parts as cards in two columns (one when narrow): a well with its icon (ember
    while on), its name, its switch (Upload my logs through `LogUploader.SetOn`, the rest `SetFeatures`; Plan
    and callouts is the plan window and the callouts together: on while either is, and written only when its own
    switch changes, so turning another part on or off never touches one left as you set it), its line (`Features.Line`), a status tag
    (`FeatureStatus`: "Ready" green, "Recording" or "3 waiting to upload" ember, "In duties", "Everywhere" or
    "Plan: TOP v3" plain, "Off" faint, "Needs an account" or "Stopped" amber) and _Settings ›_ to its page
    (_Link ›_ to Account while it needs an account, its switch dimmed). Under them a box: "Not sure what you
    need? The setup guide walks through it again." with _Run the setup guide_.
  - **Recording and uploads**: **Recording**: _Record my pulls_, then _Which duties_ (Raids and trials, Every
    duty: the old _In every duty_), _Record where the party stands_ and _Keep the last_ under it. **Uploads**:
    _Upload my logs_ (waiting for a link), _Upload my older pulls_ · Upload (its line what's waiting, what
    holds it up, or why it stopped; dimmed until linked with Upload my logs on), _Who sees my logs_ · Change
    on the site (the site's Settings → Your logs), _Track my progress_ (waiting for a link). More: _Smoother
    replays_ (dimmed while places aren't kept), _Show it in the server info bar_, _Recordings folder_ · Open.
    A hook the game hides is said in an amber warning box (here and on Advanced).
  - **Damage meter**, **Plan and callouts**, **Party cooldowns**: below (The meter's windows, The overlays).
  - **Windows**: first **Size** (since 0.8.1; above, The windows' look, Size): _Fit to my screen_ (a switch, on)
    and under it _Window size_, a stepper from 80% to 200% in tens, dimmed while the windows fit the screen (it
    shows what fitting gives then: "Fitting your screen. Turn that off to pick."; turning fitting off starts the
    stepper there). Then a box saying _Arrange windows_ shows every window at once to drag each into place, with the
    button (`StartArrange`; "Arranging…" while it's on); then a section each for the **Damage meter** (_Show the
    meter_, _Lock it where it is_ with _Click-through when locked_ under it, _Grows_, _Background_), the **Plan
    window** (_Show my plan in duties_, _Lock it where it is_, _The plan window_ · Show or Hide), the
    **Callouts**, the **Party cooldowns** and (since 0.9.0) the **Ready check** and the **Death recap** (each: its
    Show, _Lock it where it is_, _Click-through when locked_, _Grows_, _Background_).
  - **Account and privacy**: **Your account**: linked, a ready tick, a green _Linked_ tag and the token masked
    in mono, _Unlink_ dense; not linked, the instructions, the 40px well field (the token in mono, masked) with
    _Link_ as the telegraph, a bad token said in red with an icon under it, and _Get a token_; _The Raid
    Codex_ · Open. **Privacy**: _Who sees my logs_ · Change on the site. The notes: keeping the token, and
    what goes to the site (logs with names, progress without, the roster only when pressed, the step only,
    the meter's numbers never).
  - **Advanced**: **Diagnostics**: _Diagnostics_, _Recordings folder_ · Open. **Damage meter**: _Use ACT's
    numbers_ (not in this version: dimmed, saying so), _Kept fights_ · Clear. **Setup**: _The setup guide_ ·
    Run the setup guide.
- **The server info bar** (`Plugin.cs`): the game's own text: "● Rec 2:31" while recording, "Pull 3
  kept" for 10 seconds after; its tooltip says what a click does ("… Click to open the Raid Codex
  plugin."; it promised a send before 0.3.0). A click opens Home.

## The setup guide (`UI/Views/Setup.cs`, `UI/Model/SetupGuide.cs`; since 0.8.0)

A fresh install (no settings saved: `Configuration.Setup` Pending) opens it once in the main window;
settings saved before 0.8.0 are an existing player's and never see it (`SetupState.Migrate`). It waits
for the game: on the title screen the window isn't drawn while it's on the guide
(`MainWindow.DrawConditions`). Finishing it, skipping it, closing the window on it, or leaving it for another
screen (a command, Dalamud's buttons, the server info bar) makes it Done; stepping aside for Arrange windows
doesn't. Settings → Features can run it again (`ShowSetup`); run again, _Skip_ leaves your switches as they are
(on a fresh install they're already the recommended ones). _Finish_ stops the samples its last step showed. Five steps, no tabs: at the top a segment a step
(4px, ember up to the one you're on) with the steps' names under them (Welcome, Features, Account,
Look, Done, the one you're on in white); at the foot, under a hairline, _Back_ (quiet) and the step's
one button (the telegraph). Nothing in it reads a key: typing the token is the only keyboard use.

1. **Welcome**: "First time here" in ember, "Welcome to Raid Codex", the lead, three lines with ember
   wells (it records each pull by itself; every pull, again, on the site; your plan, step by step, in
   the fight), and "About a minute. Everything here can be changed later in Settings, and nothing is
   ever bound to a key." _Skip: keep the recommended setup_ (quiet: `FeaturePreset.Recommended`, Done,
   Home) and _Set it up_.
2. **Features**: "What should it do?"; _Start from_ Recommended · Everything · Recording only (a
   segment applies its preset; none is picked when the switches are no preset, `Features.PresetOf`);
   a card per main part (its well, ember when on; its name; "Needs an account" in yellow for Upload my
   logs and Track my progress, "Account linked" in green once linked; its line; its switch). Each
   switch changes the setting at once and saves (`Configuration.SetFeatures`; Upload my logs as its
   own switch does, from now on: `LogUploader.SetOn`). "5 of 6 on" beside _Next_.
3. **Account**: "Link your account" and which picks need it, by name (`SetupGuide.AccountLead`), or
   that none does. Three steps on waymark squares: sign in on the site and open Settings › In-game
   plugin (_Get a token ↗_, the site's page for it), copy the token, paste it here (the masked well
   field and _Link_, the same linking as Settings → Account: `LinkToken`, a bad token said in red).
   Linked, a ready tick, "Linked to your Raid Codex" and the token masked (the site tells the plugin
   no account name). Then "What's shared, and with whom". While a pick needs an account and none is
   linked, _Next_ waits (`SetupGuide.CanGoOn`) and _Do this later_ skips the step.
4. **Look**: "Make it yours"; the meter's _Size_ (Compact · Detailed; Detailed on a fresh install,
   `MeterCompact` false); three theme tiles (Ready Check, Readout, Grimoire: the theme's ground with
   three bars in its colours, its name; the one picked edged in ember; picking sets `MeterTheme`),
   "Three more themes in Settings › Damage meter."; **Where they go**, a picture of your screen with
   a dashed box for each window that's on (Callouts in ember and Plan with Plan and callouts, Party
   cooldowns, Meter), and _Arrange them on my screen_ (`StartArrange`: every overlay shown at once to
   drag into place; "Arranging on your screen…" while it is), "Or later: Settings › Windows".
5. **Done**: a green tick in a ring, "You're set", and a row per main part with an _On_ (green) or
   _Off_ tag and a short note (`SetupGuide.DoneNote`: "Raids and trials · keeps the last 20",
   "Linked · public, as on FF Logs", "Detailed · Ready Check · in duties", "Turn it on any time in Settings ›
   Features"…). _Play a sample fight on the meter_ (the meter's sample; _Stop the sample fight_ while
   it plays, waiting while a real fight is on) and _Show sample callouts_; "Type /raidcodex to open
   this window again, or /raidcodex help for every command." _Finish_: Done, saved, Home.

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
flagged reflected; damage from the target is always at the caster, whether or not the game also sets the "to the
caster" bit), and lands on the target, or on the caster when it says so (lifesteal: Bloodbath's
heal is its user's). A pet's or summon's action is its owner's (the owner from the game), flagged; an
action whose ActionCategory is 9 or 15 is a limit break, flagged. The party is `PartyMembers`' entity
ids (the party list's; since 0.5.0, outside a duty, a cross-world party's group: above, What's
recorded; just you alone), read four times a second: **cross-world parties count in the meter
everywhere** (before 0.5.0, outside a duty only your own numbers were). Damage between party members isn't kept,
nor anything between two others; a barrier (a status named on `CombatLists.ShieldNames`, found on the
English Status sheet) is a shield put on, never healing. A tick (actor control 1541 a DoT's, 1540 a HoT's, since 0.8.2; 23 before) goes to the party
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
  overheal % is overheal over all healed. Shields aren't healing; lifesteal goes to whoever drew it; MP never. The
  healers' limit break's healing is the limit break's, never the party's (it has no row on the Healing tab, and the
  site leaves it out), so the party's healing is its rows' (since 0.7.4).
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
were up again and who raised them, and whether Weakness or Brink of Death was on them; since 0.9.0 also the
mitigations up as the killing blow landed, whether a tank buster or a raid-wide killed them, and the phase (the
death recap's: see The ready check card and the death recap). And the
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

**Since 0.9.0, for healers and tanks** (`Core/Meter/Guard.cs`; nothing new is recorded: the recording's format is as it was):
- **Shields absorbed** (`MeterRow.Absorbed`, and a second over the encounter's length, as HPS): an **estimate** of what the
  barriers each member put on soaked of enemies' hits. The game doesn't say: a hit's amount is the whole hit (the barrier's
  part included), and a member's barrier is a whole % of their max HP (`ShieldValue`), read with their HP as an enemy's hit
  on them arrives (`TakenHit.Shield`, the batch's `Shield` the hooks already read). That reading is the barrier *before*
  the hit (the game takes a hit off a moment after its message, HP and barrier alike), so a hit's absorbed is
  min(hit, barrier) (`ShieldBook`); hits that come before the game has shown the last one's effect (the same % read again
  within 1.5 s) share that barrier, what the earlier took coming off it first. So it's right to about 1% of max HP a hit,
  and a hit's own "partly absorbed" flag (`Param4 & 4`, now also in `TakenHit.Fx` as `FxAbsorbed`; still unverified) isn't
  needed. It's **credited** to whoever's barrier was on them: their status list read each frame during an encounter (the
  barriers by name, `CombatLists.ShieldNames` and the death recap's catalogue's, each with its source; a pet's is its owner's), shared
  evenly between several (`GuardRules.Split`); for a member whose list isn't read (out of sight), the barriers party
  members put on them in the last 30 s (their actions' effects). A barrier of no one in the party (or none named) is no
  one's. Never part of Healing (as before, and as the site counts it): it's beside it.
- **Heals cast** (`MeterProtection.GcdHeals`, `OgcdHeals`): each use of a heal or barrier once, however many it landed on
  (its effects come at one time), on or off the GCD (`CombatKinds.Gcd`); pets' and the limit break's left out. Each heal's
  casts and overheal are in the Healing breakdown too (`AbilityRow.Casts`, `Over`; a HoT's ticks count as its casts).
- **Self-healing**: what their heals on themselves put back (the overheal left out): their heals and lifesteal on
  themselves, their HoTs' ticks on themselves; by ability.
- **Big hits** (`MeterProtection.Hits`): one enemy action's hits on the party within 0.75 s are one hit; a member's part
  of 10% of their max HP or more is kept (600 an encounter at most). It's a **buster** on one or two (and fewer than the
  party) when the biggest took 30% of their max HP or more (the whole hit, a barrier's part included), a **raid-wide** on
  three or more and more than half the party when they took 12% of their max HP or more on average; anything else isn't
  listed (`GuardRules.Classify`). Each carries what was **up** for them: the statuses on them that mitigate (by name,
  `CombatLists.MitigationOf`: the invulnerabilities, `ShieldNames` and the death recap's catalogue, `Mitigations`, one list
  for both so they agree; who put each on), and the party's Reprisal, Addle,
  Feint or Dismantle on the enemy whose hit it was (the catalogue's; read four times a second with the targetability,
  fresh in the last second), or, for a hit from an enemy not read (a boss's invisible helper, as many raid-wides are), on
  every enemy read then; a barrier that soaked some but isn't named is "Barrier". Its **verdict** (`GuardRules.Verdict`):
  _Died_ when they died within 3 s of it, _Mitigated_ with an invulnerability or two or more up, _Light_ with one,
  _Unmitigated_ with none. A hit under an invulnerability that did nothing isn't seen (the parser keeps no 0 hit), so isn't listed.
- **Mitigation up** (`HeldSeconds`, `CoveredSeconds`, `Uptime`): each frame a boss is on a member (the bosses' targets,
  not a cast's), the time since the last frame is time they held it, and time their own mitigation was up when they
  carried one of theirs (mitigation, a barrier or an invulnerability they put on themselves) or their Reprisal (Addle…)
  was on a boss they held.
The game side reads each party member's status list each frame during an encounter (the list it already read for Weakness)
and the enemies' four times a second (with the targetability); the status kinds are found by English name on the Status
sheet, as the other lists (`CombatKinds.Mitigation`, `GameData`).

**Phases**: the recording's rule (`PhaseClock`): four times a second during an encounter the enemies'
targetability is read (in a duty every enemy, outside or in a field zone only those the party has
fought), and a new phase begins when every enemy that had been targetable was gone 8 seconds or
more and one is back.

**Pace**: the party's damage since the current phase began, against the compared encounter's (the
best kept of the same duty, or the one just before) at the same second of the same phase: how far
ahead (or behind). None until 10 seconds into a phase, outside a duty, or when the other never
reached this phase or left it (moved on or ended) before this second of it. The **best** is a clear,
else the one that reached the furthest phase, then lasted longest.

**When it stops**: after three nonsense effects the meter turns itself off and says why (`IMeterSource.Problem`):
the game has moved something it reads. Since 0.7.8 they count by stretch (`Core/Meter/AnomalyGuard.cs`): from the
meter starting to listen, an encounter's end or a zone change to the next, and only while the meter listens (not the
recorder alone). A stop lasts until the next zone, where it tries again; the third in a session lasts until the plugin
reloads. Before, three in a whole night (the recorder's too) turned it off until a reload. A layout the game really
moved gives nonsense on nearly every hit, so it still stops within a pull, and never shows it.

**The game side** (`Game/CombatMeter.cs`): with _Show the meter_ on (in duties, or everywhere), it
listens to the parser and asks the recorder to keep the hooks on; each frame it tells the session the
zone, the recorder's pull, the party (`PartyMembers`', since 0.5.0 a cross-world party's group too;
names read once each and kept only in the plugin; HP from the party list, or a cross-world member's
character when near; in combat from the game's own look-up) and whether anyone's in combat. Since 0.4.0 also
each frame the limit break gauge (`LimitBreakController`) and, during an encounter, which party members
carry Weakness or Brink of Death (their status lists; since 0.9.0 with the death recap's mitigations on them:
`CombatMeter.Statuses`); four times a second with the targetability, the
enemies' casts (and whether they can be interrupted) and the bosses' targets (the enemies with half the
biggest's max HP or more; whether they're casting at that one; since 0.9.0 the party's Reprisal, Addle, Feint and
Dismantle on any of them); and every cast the game says stopped
(`FightRecorder.CastStopped`, recorded pull or not). Reading the game costs little: no allocation per message on the hot path (a test reads an eight-target action twenty
thousand times and allocates nothing), the object table only four times a second during an encounter.

## The meter's windows (`UI/MeterWindow.cs`, `UI/MeterDetailWindow.cs`)

Drawn from `IMeterSource` only (`Core/Meter/MeterModel.cs`; the plugin's is `CombatMeter.Source`);
`Model/MeterView` orders and words what they show, `Model/MeterLayout` sizes and places the small
one (both tested). Since 0.7.0 the small one is **compact by default**, in one of six themes (below,
_Compact_); off, it's the normal meter described first here. Since 0.7.3 the normal meter and the
opened-up window are drawn in the same theme (below, _The normal meter in the theme_); the colours,
faces and sizes the paragraphs on the normal meter give are Ready Check's.

- **The normal meter in the theme** (since 0.7.3; `MeterWindow.Normal.cs`, `ThemeSkin.cs`): a bigger
  sibling of the theme's compact meter, every part of it the theme's, its features and behaviour as
  below (its columns, tabs, pace, growing up or down, sizing by its edge with the resize zones kept
  clear, the lock). Its ground and frame are the theme's (`PlateMeter.Ground`, the Background slider
  never under the theme's floor, its sparkles and Readout's scanlines as Decorations has them; the
  window's own background and edge are clear), the head on the theme's band (`PlateMeter.HeadBand`:
  Native's title band, Neon's horizon, Grimoire's rules; a plate for Ready Check's and Neon's head)
  with the theme's clock (Mochi's pink pill with its heart, Native's diamond and the tab's word in
  Jupiter, Readout's prompt and cursor, Grimoire's red capital, else the beating dot; Neon's glows).
  Its faces by role (`DetailFonts.ForNormal`, tested: Ready Check's are the normal meter's sizes as
  they were; a theme's its compact faces, as far from those sizes as they are from Ready Check's
  compact ones), every word measured and centred by its ink. The rows on the theme's plates, 25 tall
  or as its faces need (`MeterSizes.For`, tested: Mochi 29, Native 27, Grimoire 26), 1px apart: the
  job's icon in the theme's socket and role colours, the name (yours a weight heavier), the job in the
  heads' capitals, the number and the second column in columns as wide as the widest words shown
  (never under 66 and 40; the job's under 30), Mochi's heart's place kept on every row; the bar in the
  theme's style along its track (`PlateColumns.Track`: before the second column, and for a hard-ended
  bar, `ThemeBar.BeforeNumber`, before the job and the number too); you in the theme's mark
  (`PlateMeter.YouMark`; Readout's reverse video), the dead muted with its death mark (struck through
  for the dagger, glitched faintly for Neon). The tabs in the theme's words with its accent under the
  one you're on (Ready Check's the ember cast gradient), pace (its label in the
  heads' capitals, the bar in the accent, ahead in good or bad), the limit break's row (its mark in the
  theme's colour, Mochi's star, no plate), the foot (on a plate for Ready Check, Neon and Native, else a
  hairline; the limit break's pips as compact's), the Preview and outcome tags in its chips (Readout's
  in brackets), the dropdowns, the lock and the fold's chevron, and the picker's and tab menu's rows, the
  pace segments and the Compact switch, in its panel, chips, accent and lines (`ThemeSkin`). Deaths and
  Events on plates too; the empty meter's Boss with the theme's words. Ready Check is today's meter
  refined to match compact Ready Check: its rows on plates with the bar in the role's colour at 30% and
  its 2px foot, the ember notch for you.

- **The meter** (`MeterWindow`, "Meter", 330 wide at first, translucent as Settings says: its
  background rgb 13 13 16 at _Background_, its rows solid, 10px corners). Shown with _Show the meter_
  (or `/raidcodex meter`), in duties or everywhere. Its head (36 tall): the fight's clock in mono
  (600 15px) with the ember dot beating while a fight is on (a still faint dot otherwise), a _Wipe_
  or _Clear_ tag when the fight shown is over, the picker ("Pull 12 ⌄", 24 tall) and the lock (24px:
  ember-washed when locked, a pointer when click-through, quiet when open); the buttons are laid
  first and the tags only drawn as far as they fit before them. Locked, it has no title
  bar and can't be moved or sized (its tabs and picker still answer); click-through, the mouse goes
  to the game (`/raidcodex meter unlock` or Settings gives it back); open, Dalamud's title bar comes
  back with its menu, its × and the resize grip. Dalamud has ImGui resize a window from every edge
  and both bottom corners (`ConfigWindowsResizeFromEdges`), and ImGui takes a press there in Begin,
  before the window's buttons; so its buttons are kept out of that reach (`Model/ResizeZones`,
  `UI/Kit/ResizeGuard.cs`, tested): `ResizeReach` works out ImGui 1.88's grip (drawn
  `floor(max(font × 1.35, rounding + 1 + font × 0.2))` big at the font at Begin, taking a press three
  quarters of that in from the corner; the edges 4px either side), and a button level with a bottom
  grip sits the grip's reach and 4px more in from the side (the head at the bottom, whichever way it grows:
  the lock about 20px in at 100%, the picker beside it; the foot's _Preview_ tag with the head on top), one by an edge
  clear of its band; and while the mouse is over one of the head's or foot's buttons (where they were
  last frame, and not mid-drag) the window gets `NoResize` that frame, so a press on them is theirs;
  everywhere else it sizes as ever. Five tabs (27 tall, the ember bar under the one
  you're on; a meter too narrow for the five has a tab menu in their row): **Damage** (ENCDPS, or DPS in Settings, and the share; pace from 10 s into a phase
  against the picked pull: "PACE · P2 +0:41", a 3px bar with this pull in ember and the other's tick
  in white, "on pull 4 +3.4%"), **Healing** (HPS and overheal %), **Taken** (damage taken and its
  share), **Deaths** (each death's spot, name and job, its time in red, what killed them and how big,
  since 0.4.0 "Weakness" when they died with it on and "raised by H1 after 0:11" when they were
  (`MeterText.DeathLine`); the wipe's left out; live, "7 alive"; past the room cut, and said: "· 3 not
  shown"), **Events** (0.4.0, below). Rows are 25 tall: the job's icon (18px, since 0.7.0: below; the spot
  in its tooltip), the name (or the job
  alone, _Jobs only_), the job in mono, the number in mono 600 13px, its second column, and a 2px bar
  under it to the top player's (grey; yours ember; a death's dimmer, its name muted with a red
  skull). The limit break has its own row under a hairline; the party's number at the foot ("Party ·
  165,000", "… HPS"), with where in the fight, "26% over" (the party's overheal, Healing; "shields not counted" before 0.9.0), "incl. bleeds", "kept as pull
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
  dashes; between pulls in a duty it stays open on the pull just done (since 0.8.2, _Keep it open in duties_,
  `MeterOpenInDuties`, on at first); out of duties, or with that off, it folds to one line between pulls (the last
  fight's length and tag, "Pull 12 · party 163,188", a chevron that opens it again), or hides with _Hide out of
  combat_. A new pull shows itself, whatever was picked before it.
  Pressing a row opens the meter opened up on that player; a death, on its recap.
- **Events** (0.4.0; `IMeterSource.Events`, the session's newest 200 a fight): the fight's events as a
  feed, the newest where the top player goes (at the top highest first, at the bottom lowest first; _Order_), a 23px row each: the time in mono
  (11px, faint, in a 30px column), the kind's Lucide mark in its colour (`MeterText.Tone`: a death's
  skull, a cast that went off's warning and aggro lost red; a raise's heart and an interrupt green;
  the limit break's sparkles and a potion's flask ember; a stun's bolt, a resist's shield and a tank
  swap's arrows muted), the member's job icon, and the event's own words (the session's, names and all:
  they stay in the game), cut to fit, a death's in its red; pointed at, the kind, the time and the
  whole line. Pressed, the meter opens up on Events, picking the member (a death, on its recap). Past
  the room the oldest are cut (64 are kept for the feed); the foot counts them by kind, the most said
  first ("2 deaths · 1 raise · 3 interrupts"), and an empty fight says what will show. The tab
  menu counts them as it does deaths.
- **The limit break gauge** (0.4.0; `IMeterSource.LimitBreak`; Settings → Damage meter → _Limit break gauge_:
  Head, Foot or Off, Foot at first): while a fight is on, "LB" in faint mono 9 and a bar each the party
  has (16 wide, 3 apart, 5 tall; compact's pips are its own, below): a full one the cast bar's ember gradient with a faint
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
- **Its height** (since 0.7.4 fitted to what it shows, `MeterFit`, tested): folded, empty or stopped, the
  window is as tall as what it shows (ImGui fits it, its width kept). In a fight it's as tall as what it
  draws: the head, the tabs, pace's row only once pace shows, 6px under them, a row each, the limit
  break's row only once it was used, and the foot (122 for one player with neither, 350 for eight with
  both); waiting, the head, the tabs, 6px under them, the party with dashes and the foot (since 0.8.2 there are
  no column heads, ENCDPS and % and the like, 20 tall, between the tabs and the rows); Deaths, a row
  each (as tall as its faces), then the alive line live or the foot (and "Nobody died" with none); Events,
  a 23px row each (the newest 64) and the foot (and its line with none); a list never taller than the
  party's fight on Damage, so a long one doesn't stretch it. Until 0.7.4 it kept room for pace and the
  limit break before they came (`MeterSizes.Needed`'s 364 for eight from the start), which showed as an
  empty band at the edge away from the head (a meter growing up with one player: 53px over its row). The
  anchored edge stays put and the other moves as rows, pace or the limit break come and go. It's never
  taller than the height you gave it by its edge (`MeterHeight`, the title bar's height in it; kept once
  you let go: none until you do): the most it grows to, the rows past it cut. One sized taller than what it
  shows goes back to what it shows (and keeps that height as its most); 0.3.0 kept 382, the height it first
  opened to, by itself, so a 382 saved by it is read as none. A member joining mid-fight adds a row.
  While the mouse is on it (or on one of its menus, the pull picker or the tab menu), the normal meter and
  compact keep the height they had as it came (`MeterLayout.Hold`, tested): a tab switch or a row coming or
  going doesn't move the head and tabs from under the mouse (they would, with the head at the edge that isn't
  anchored: Grows Up with the head on top). Rows that don't fit the held height are cut, as under a height
  you gave it; fewer leave room at the far side. A row come mid-fight waits too. Once the mouse is off it, it
  settles to what it shows. Sizing it by its edge isn't held: the drag wins, and once you let go it's fitted
  under the height you gave it. Click-through, it's never held (the mouse is the game's).
- **Where it stays put** (_Grows_, `MeterGrows`): as its height changes by itself (folding, opening,
  a bigger party, compact), growing **down** keeps its top edge where it is, growing **up** its bottom
  edge. Since 0.7.4 that's all Grows does: where the head sits and the rows' order are their own (below).
  Its anchor (`MeterAnchorX`, `MeterAnchorY`, screen px: its top-left corner, or its bottom-left
  growing up, `MeterAnchorFor`) is read from where the window is the first time it's drawn (so one
  saved by an older version keeps its place), moved to the other corner when Grows changes (so it
  doesn't move then), and follows the window while you move or size it (unlocked; saved once you let
  go, not each frame). Each frame the meter is placed from it (`MeterLayout.Place`, ImGui's
  SetNextWindowPos before Dalamud's Begin: it never sets the meter's Position), kept whole on the
  game's screen while the anchor is on it (moved in as little as it takes, its anchored edge first
  when it's taller than the screen, and back onto it after the resolution shrinks; the anchor itself
  stays, so it goes back as it shrinks; with Dalamud's multi-monitor windows on, an anchor off the
  game's screen is on another monitor, and left alone); while you hold it (the
  left button went down on it and is still down) it's neither placed nor sized.
- **The head and the order** (since 0.7.4; _Head_, `MeterHead`, and _Order_, `MeterOrder`; `MeterArrange`,
  tested; until then growing up flipped both with Grows): the head (the clock, the picker, the lock, and a
  narrow meter's tab menu; the tabs and pace beside it; compact's head) sits at the
  **top** or the **bottom**, whichever way the meter grows, and the foot (the limit break's row and the
  party's line) at the other edge. With the head at the bottom everything else is the other way up from
  it: the tabs over the head, pace over them, the rows, the limit break over them and the party's line at the top; the empty and stopped
  states the Boss and its words over the tabs, waiting likewise; each band's hairline on its side away from
  the head, the limit break's and the foot's toward it; the themes' head bands turned to face the rows
  (Native's title band inset from the window's edge it's against and its gilt rule toward the rows, Neon's
  horizon fading away from the rows, Grimoire's two rules on the rows' side). The rows read **highest
  first** (the top player at the top of the list) or **lowest first** (at its bottom), reading down the
  window, wherever the head is (`MeterArrange.FromHead`: each row's place counted from the head; cut past
  the room from the lowest); the first death and the newest event go where the top player does, waiting's
  party (in spot order) and compact's rows too, compact's pinned you staying at the end of the list away
  from the top player (below). A row reads as ever (its bar still under its words). The menus open away
  from the head (up from one at the bottom, down from one on top; below). An older configuration is moved
  once (`MeterArrangeVersion` 0 → 1, `Configuration.Migrate`): growing up becomes Up, the head at the
  Bottom and Lowest first, exactly its old look; growing down Down, Top, Highest first, a new meter's too.
- **Compact** (`MeterCompact`, on by default since 0.7.0; `PlateMeter`, `MeterWindow.Plate.cs`, its
  model `Model/MeterPlate.cs` and `Model/MeterTheme.cs`, tested): round four's variant A, "Plate" (the
  proposal: kokos-tools `docs/proposals/raid-codex/meter/METER.md`), in place of 0.3–0.6's compact (20px
  rows, the bar as the row's background). A configuration from before 0.7.0 is moved to it once
  (`MeterLook` 0 → 1: compact on whatever it was, the old compact's height let go); after that the switch
  is yours, and the normal meter stays as above. Ready Check's sizes (each theme has its own, below):
  - **The head** (24 tall, on a plate): the clock (mono 600 13px) with the beating dot (a hollow one
    waiting, none after), a _Wipe_ or _Clear_ chip when it's over, the phase in a hairline chip ("P2 ·
    0:41"; "in P2" after), then on the right the limit break's pips (8 × 4, 2 apart: full ones in the
    theme's colour, the one filling to its share over a track; _Limit break gauge_ Off hides them;
    pointed at, the gauge's words), the menu ("DPS ⌄", "HPS ⌄", "Taken ⌄", "Deaths ⌄", "Events ⌄"; the
    tab's short word) and the lock. The left is drawn only as far as it fits before them.
  - **The menu** (300 wide, off the head as the picker opens): the five tabs (the one you're on ticked,
    deaths and events counted), the fights kept as the picker lists them, _Copy as text_, and at its foot
    _Pace against_, _Compact_ and _One line_. It replaces the tab row, the picker's button and pace's row.
  - **The rows**: at most eight, the top eight; when you're ranked below the last row shown (ninth or
    lower: the window is never shorter than its eight), the last row is you, pinned at the end of the list away
    from the top player (the bottom highest first, the top lowest first) past a dashed rule, with
    your real rank after your name ("#9"); no alliance view (`PlateRows.Select`). Each row (20 tall, 1
    apart, 4px corners) sits on a near-solid plate (#18181d at 92%, whatever the Background), so its
    words never touch the game; the bar fills its track from the plate's left to its share of the top
    player's (the track ends 3px before the share column, so a full bar never runs under the share:
    `PlateColumns.Track`; Mochi keeps its heart's place on every row, so the shares line up; Readout's
    cells are their own; since 0.7.2 a bar whose end is a hard edge, Native's HP bar, Mochi's pill and
    Neon's bright cap, `ThemeBar.BeforeNumber`, ends before the number column too, so the number and the
    share sit on the plate beside it as the game's party list has them; Ready Check's soft 30% fill and
    Grimoire's underline still run under the number), in the role's colour at 30% with a solid 2px line at its foot; the job's icon (16px, the game's own:
    `ITextureProvider.GetFromGameIcon(62100 + the ClassJob row)`, `JobIcons`; the role's mark in a
    socket while it loads or for a job it doesn't know), the name (Geist 500 12, cut to fit; yours a
    weight heavier; _Jobs only_ the job), then in right-aligned columns the number (mono 600 12 in
    38px) and the share (mono 10 in 32). A column is at least its theme's width and at least as wide as
    its widest sample measured in the face actually drawn ("88.8k", "8.88M", whole "888,888"; the share
    "88.8", "100.0", "100%"), and the number's at least the widest number the rows have (`PlateColumns`,
    tested; measured once a theme, scale, Whole and font build: `Fonts.Generation` counts the atlas's
    builds), so a face wider in the game than in the mock-ups never runs one number into the next. You: an ember notch at the left and an ember outline at 75%. A
    death: the bar loses its colour, the icon goes grey, the name muted, a red skull and how long they've
    been down ("0:20", to the fight's clock) before the number; for two seconds as it happens the plate
    flashes red at 28%, fading. Pointed at, the row lightens and the details panel opens (below);
    pressed, the opened-up window on that player, as before.
  - **Numbers in three figures** (`MeterNumber.Short`): 27.8k, 9.05k, 165k, 1.23M, rounded (a rounding
    that reaches the next unit takes it: 9,996 is 10.0k), so a number's last digits don't change at every
    snapshot; _Whole numbers_ (Settings) writes them out (27,840) and widens the column by 10.
  - **Ranks hold** (`RankHold`): live, a row moves above one shown above it only once it has been ahead
    for a second without a break (two players a few hundred apart don't trade places four times a
    second); one that has held past several moves past them all; a member joining goes in at their place
    at once; a new fight, tab or number starts from the true order; a kept fight is in its true order.
    Between snapshots the rows slide to their places (160ms a place) and the bars ease (250ms); at once
    with Dalamud's reduced motion.
  - **The limit break's row** under a hairline (when it was used: no room is kept for it before, unlike
    the normal meter's), its mark, "Limit break", its number a size smaller and its share; **the foot**
    (18 tall, on a plate with the limit break's row): "Party 165k" (HPS on Healing), and on the right pace
    while live on Damage ("pace +3.4%", green ahead, red behind; pointed at, "Pace · P2 +0:41 · on pull 4
    +3.4%"), "kept as pull 12" when it's over, "copied", "click-through", or the sample's _Preview_ tag
    (pressed, it stops).
  - **Deaths and Events** in compact: a row each on its plate (the top eight that fit): a death's job
    icon, name, what killed them and how big (cut to fit), its time in the death colour; an event's time,
    its kind's mark in the theme's colours (bad, good, accent, muted), the member's icon and words. The
    foot counts them. Pressed, as the normal meter's.
  - **Its size**: never set by hand: its window can't be resized, unlocked or locked
    (`MeterLayout.Resizable`, tested; `ImGuiWindowFlags.NoResize`: a size given it would only snap
    back). As wide as its theme (248, below), as tall as its rows need (`PlateSizes.Needed`: Ready
    Check's eight and the limit break 238, against the old compact's 272 and the normal meter's 364; a
    light party with no limit break yet 133), the title bar's height on top when it shows. A height
    the old compact was given (`MeterCompactHeight`, 0.4.0 to 0.6.0) or a width 0.7.0
    let you give it (`MeterCompactWidth`) is still read from the settings but ignored, and cleared once
    (`Configuration.Migrate`). Grows down or up, its head at the top or the bottom (at the bottom
    everything else the other way up from it) and its rows highest or lowest first, as the normal meter
    (above, _The head and the order_); its anchor is shared, and it's moved by its title bar.
  - **One line** (`MeterCollapsed`, the menu's _One line_; and between pulls, folded, as the normal
    meter folds; and before any fight): one 26px line on a plate: the dot and the clock (the outcome when
    it's over, else the phase), then on the right you ("You #6" with an ember bar and your number), the
    deaths (the death mark and a count), the pips, and a chevron that opens it (by hand: _One line_ off).
    Before any fight it says "No fight yet" with _See a sample_; stopped, "The meter has stopped" in
    red, why in its tooltip; its chevron opens Settings.
  - **The details panel** (pointed at a row; in a tooltip, so it sits over everything and needs no
    window): 236 wide, beside the meter on the side with room (right, else left), level with the row and
    kept on the screen, in the meter's theme: the job icon, the name, "DRG · M1 · #1 of 8 · in P2" (the
    spot lives here now), six figures (Damage: ENCDPS, DPS, share, damage, crit · DH, max hit; Healing:
    HPS, overheal, share, healing, crit, max heal; Taken: taken, a second, share, max hit, deaths, time
    dead), the top four abilities with a slim bar in the role's colour, "0 deaths · 74 GCDs · 1 potion",
    and "Click: the full table and recap". Worked out once a snapshot for the member pointed at.
  - **Themes** (Settings → Damage meter → _Theme_; `MeterTheme`, Ready Check at first): a token table each
    (`Model/MeterTheme.cs`, `MeterThemes.All`): the faces (name, number, small, head, label: face, size,
    outlined as the game's words are, digits drawn in cells for a face without tabular figures, capitals
    spaced), the window (its ground or a vertical gradient, its **floor**, corners, padding, the head's
    height, its width, the one line's height), the frame (an edge and its width, an outer line, an inner
    gilt rule, alpha rings, a sticker's shadow), the row (height, gap, corners, plate and its alpha,
    words' colours, an edge or a rule), the bar (one of six styles), the roles, you (one of six marks),
    a death (one of six), the accents, the job icon's socket and the ornaments. The draw code reads only
    the table. The six (the mock-ups' values; their contrast and colour-blindness figures are tests over
    the tokens):

    | Theme | Width | The idea | Bar | You | A death | Faces |
    | --- | --- | --- | --- | --- | --- | --- |
    | Ready Check | 248 | basalt, white hairlines, ember for you | fill (role at 30%, 2px foot) | ember notch and outline | skull and time down | Geist, Geist Mono |
    | Mochi | 268 | a cream sticker (white edge, pink line, a pink shadow), a pink pill clock with a heart, sparkles | pastel pill | pink outline and a heart | broken heart | Zen Maru Gothic 700/900 (names 14, labels 12), the numbers and clock Geist 600 (13, 11, 14) |
    | Native | 262 | slate gradient, a black edge with a gilt rule, a title band ("◆ Damage 3:11 Phase 2"), every word outlined | HP-bar shading | gold outline | red cross | the game's Axis (names, numbers, labels), Jupiter (the title) |
    | Readout | 248 | a terminal: "> 3:11▌", rank "01" and a role letter on each row, scanlines | 16 lit cells | reverse video | "DEAD 2:51", the name struck | Geist Mono |
    | Neon | 256 | indigo gradient, a magenta edge with alpha-ring glow, a horizon grid in the head | brightening to a hot cap | white outline with rings | glitch (faint red and cyan copies a pixel apart, at 35%) | Geist, Geist Mono, Archivo Expanded capitals |
    | Grimoire | 268 | parchment, a brown frame, a gilt rule and corner pips, a red rubric capital ("Damage"), ink rules | ink underline over a wash | a red ribbon bookmark (6 wide, 2 in; the icon 6 past it) | dagger (†), the name struck | Fraunces 500/600 |

    Rows are 20 tall (Readout 17, Grimoire 21, Native 22, Mochi 24); the head 24 (Readout 20, Neon 28,
    Mochi 30, Native 26, Grimoire 32). **The floor**: Mochi 90%, Readout 82%, Grimoire 92% (their rows have
    no plate of their own, or a light one): the Background slider can't take them lower, so their words
    stay readable over a bright arena (every theme's names are at least 4.5:1 over its rows at the
    slider's lowest, over black and over white behind: a test). **Role colours** (`MeterRoleColours`):
    each theme's own colour-blind-safe set (Ready Check's tank #7c85de, healer #47e19e, DPS #e8402f: ΔE at
    least 40 apart for a protan, deutan and tritan, Machado 2009; the game's blue, green and red are 14
    apart for a deutan), or _The game's_ (#3d6fd9, #3e9b54, #c9453c) for every theme. **Decorations**
    (`MeterDecorations`, on): Mochi's sparkles, Native's title band, Neon's horizon, Readout's scanlines,
    Grimoire's corner pips and rubric capital, and the flourishes for you and the dead (Mochi's heart,
    Grimoire's ribbon, Neon's glitch); off, the theme keeps its colours, faces and frame, you a plain
    outline and a death a cross.
  - **Their faces** load only while their theme is picked (`Fonts.Want`, a delegate font handle each,
    the last theme's let go; since 0.7.2 with the opened-up window's sizes, `DetailFonts.Faces`): Zen Maru Gothic 700 and 900 (Mochi; ~28 KB each) and Fraunces 500 and 600
    at opsz 14 (Grimoire; ~76 KB each), static latin and latin-ext instances made with fontTools from
    fontsource's files, OFL (`Resources/Fonts/OFL-ZenMaruGothic.txt`, `OFL-Fraunces.txt`, beside the
    plugin); Chinese, Korean and Japanese words in them fall back to Geist (`Txt.For`), as the rest of
    the windows draw them. Native draws in the game's own fonts through Dalamud (`NewGameFontHandle`:
    Axis for names, numbers and labels, Jupiter for the title; MiedingerMid, the party list's numbers,
    drew no decimal point and a 0 like a C at the meter's sizes), nothing shipped, and they have Japanese.
    Mochi's words are a step up from the mock-ups (Zen Maru Gothic's small x-height read small in the
    game) and its numbers and clock are Geist SemiBold (Zen Maru's digits were wide and loose).
  - **Drawn** with what ImGui draws (`Kit/PlateArt.cs`): words outlined (eight offsets in black, then the
    words, and since 0.7.2 a pixel more between the glyphs and at each end, so two outlines never merge:
    Native's "9k"; measured with it, so the columns allow for it), each line's capitals' ink centred in its
    line box (`InkCentre`, tested: the pushed font's 'H' from ImGui's glyph, Y0 to Y1, once a font and
    build; Geist's capitals are centred in its line to the unit so it doesn't move, Zen Maru Gothic's sat
    0.086 em low by its line, 1.2px at 14px, and now line up with it, the game's Axis and Jupiter too),
    digits in cells (centred; the cell the theme's or the widest digit, measured once a font
    build), words whose first glyph reaches left of its pen or last right of its advance (the game's
    Jupiter) moved in and measured with it, the head's and foot's words drawn only as they fit whole
    before the buttons (the foot's right side first, the party's line in what's left), spaced capitals, filled glyphs from the mock-ups' 16-unit paths cut into triangles once (the
    roles' marks, hearts, a star, the ribbon, a diamond, the limit break's bars), a rounded rectangle with
    a vertical gradient (its outline's vertices shaded), two-colour gradients on square ends, glows as
    rings of alpha (never a blur). Nothing is allocated in a frame once warm (the words are made once a
    snapshot; the ranks' hold allocates nothing: a test); about six calls a row.
- **The picker** (the normal meter's; the site's menu, 300 wide, 6px off the head and 14 in from the meter's side: down
  from a head on top, up from one at the foot, the other way when there isn't room; the tab menu of a
  meter too narrow for its tabs the same, by its button; compact's one menu holds the picker's rows): the shown fight's duty's fights newest first ("Tonight
  · Example Arena"), the one shown ticked, the live one tagged _Now_, the best (the one that got
  furthest) starred ("Pull 4 · the best", added at the end when it's older), each with its length in
  mono and its outcome's tag; other duties' after; and _Pace against_ (Best, Last, Off) and _Compact_
  (a switch) at its foot. While the sample is on, _Stop the sample_ first.
- **The sample fight** (`Model/MeterPreview`): Settings → Damage meter's _Play a sample fight_, the empty
  meter's _See a sample_, or `/raidcodex meter preview` show the meter mid-fight with made-up numbers,
  to place it (and size the normal meter): the mock-ups' pull 12 in Example Arena, eight players, played from 2:15 with
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
- **The Healing tab since 0.9.0**, both meters: each row's bar is the healing that landed (solid) and then, striped in
  the same colour, what the barriers they put on absorbed (an estimate: _The meter's numbers_), both against the most of
  the two together (`MeterView.HealingTop`, `HealBar`, `ShieldBar`; the rows still ranked by HPS, the number still HPS,
  its second column overheal %); the normal meter has a legend under the rows (a solid swatch "Healing that landed", a
  striped one "Shields absorbed"; pointed at, that they're an estimate and never healing) and says the party's overheal
  on the foot's right; a row pointed at says its healing, overheal and shields absorbed. The compact meter's details panel
  has "Shields est." in crit's place. Readout's cells for the stripes are half lit.
- **Opened up** (`MeterDetailWindow`, "Raid Codex meter", 1180×560 at first, 1100×360 at the least
  since 0.9.0, for the sixth tab (980 since 0.4.0); `/raidcodex meter big`). **Since 0.9.0** the tabs are Damage,
  Healing, **Mitigation**, Taken, Deaths and Events (Mitigation's saved number is the last: `MeterTab.Mitigation`; the
  small meter doesn't show it, and Taken's icon is a bolt now). On **Healing** the table has _Shields_ (what their
  barriers absorbed, an estimate) in max heal's place, and the picked player's side (from about 1100 wide, the table
  given 45% of it) has their head ("Name · JOB", "Healing · 6 abilities", their HPS), four cards (`MeterGuard.HealerCards`:
  healing that landed and a second; overheal and the ability that wasted most, in the warning colour from 30%; shields
  absorbed and the barrier that soaked most, "Estimate"; heals cast on and off the GCD) and their heals by ability
  (landed, share, overheal %, casts, the biggest; a HoT tagged, a pet's tagged, much overheal in the warning colour). On
  **Mitigation** (for everyone; it opens on the first tank) the table ranks by damage taken with taken, %, busters,
  _Bare_ (busters with nothing up, red when any), _Mit up_ (their own mitigation's time up while they held a boss) and
  self-healing; the side has four cards (`MeterGuard.GuardCards`: damage taken and their share; mitigation up "of the
  1:40 they held a boss"; tank busters, red with "1 with nothing up"; self-healing and with what) and "Busters and
  raid-wides on Name", oldest first, scrolling: the time, the enemy's action over "Buster", "Shared buster" or
  "Raid-wide · 8 hit", the game's amount and (a barrier soaked some) "→" the HP it took (an estimate; its tooltip says
  so), the mitigations up as chips (another's with their spot, "+2" past the room) or "nothing up" in red, and the tag:
  MITIGATED on a light wash, LIGHT in the warning colour, UNMITIGATED and DIED in red (`MeterGuard.Line`). The sample
  fight has shields, heals and big hits for both (`MeterPreview.Protection`).
  Before 0.9.0: the five tabs full size with their icons and
  the deaths and events counted; the clock,
  where in the fight ("P2 · 0:41 in"; these two only as they fit beside the tabs), which pull ("Pull
  12 · now ⌄"), ENCDPS or DPS, a quiet square _Copy as text_ (0.4.0: the copy mark, a tick while it
  says "Copied to the clipboard" for 2.5 s; the small meter's text) and _Small_ back to the meter
  (turning _Show the meter_ on if it was
  off). Since 0.7.0 every player in it (the table, the abilities' head, the deaths and their recap, the
  events) has their job's icon where their spot was (the spot in its tooltip; the game's own icon, as
  compact's). A slab with every column (Damage: ENCDPS, DPS, damage, %, crit, DH, max hit, deaths;
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
  event picks them in the table, a death's opens its recap on Deaths. **In the theme** (since 0.7.2; the
  meter's theme, compact or not): a theme draws the window's ground and frame
  (`PlateMeter.Ground`, opaque, its sparkles left out), its panels a shade off its ground in its
  hairline, the rows on its plates (Grimoire's ruled, Readout's bare) with its hover wash, its faces
  (`DetailFonts`, tested: Ready Check's are the window as it was; another's its name's face for words,
  its number's for numbers, its small one's for small numbers and the heads in capitals, each as far
  from Ready Check's sizes as its compact faces are from Ready Check's there), its role colours and job
  icons in its sockets, the limit break's mark in its colour, the picked row in its mark for you
  (`PlateMeter.YouMark`; Readout's reverse video as its colour's wash and outline), a dead name in its
  death mark (struck through for the dagger, glitched faintly for Neon), the abilities' bars in its bar
  style in the player's role colour (under their words), the tabs, the segments, the buttons and the
  pull menu in its chips with its accent (the tab you're on, the segment picked, the pull shown), the
  events' marks in its good, bad and accent, the recap's headline in its death colour. The table's and
  the abilities' columns are as wide as the widest words they show, the head's and every cell's,
  measured in the face drawn (`DetailColumns`, tested; again for a new snapshot, theme, scale or font
  build): the limit break's "–" and "1,337,000" no longer run together. Settings → Damage meter → _Theme_ says
  it's both windows' look. A line under each tab
  says how its numbers are worked out (Events: casts are GCDs, an interrupt is credited to whoever's
  interrupt hit the cast in the second before, time dead leaves out the wipe).
- **Settings → Damage meter** (0.8.0; `/raidcodex meter settings` opens it). **When it shows**: _Show the meter_
  (`/raidcodex meter` too) with _Where_ (In duties, Everywhere), _Keep it open in duties_ (since 0.8.2) and _Hide it
  out of combat_ under it. **How it
  looks**, one slab: _Size_ (Compact, Detailed: `MeterCompact`, Detailed a fresh install's since 0.8.0; the
  meter's own menu has it too), _Theme_ (the six as tiles in three columns, two when narrow: a swatch of each
  one's ground with two bars in its roles' colours and its name under it, the one picked edged in ember, where it
  sits in the range and its idea in a tile's tooltip, the picked one's idea under them) and _Role colours_ (Theme's,
  Game's: the bars and job icons' sockets, in both meters, the opened-up window and the cooldown tracker too);
  beside them (under them when narrow) the live **Preview**, the sample fight playing in that theme at its real
  size, as the meter draws it, from the same drawer (`PlateMeter`, not interactive; following Head, Order and the
  gauge), and _Play a sample fight_ under it (_Stop the sample_ while it's on; dimmed while a real fight is on,
  "After this fight"). **The numbers**: _What it ranks by_ (ENCDPS, DPS, with what each counts) and _Show_ (Names,
  Jobs only). **Death recap** (since 0.9.0): _Show a death recap_, _Recap deaths of_ (Me, Everyone: Me at first) under
  it, _See it_ · Show a sample (a made-up wipe's three deaths). Then "12 more": _Grows_ (Down, Up: "The edge that stays put as it grows"; since 0.7.4 three small
  choices in place of 0.7.3's one, which flipped the head and the order with it), _Head_ (Top, Bottom) and _Order_
  (Highest first, Lowest first), both meters'; _Limit break gauge_ (Head, Foot, Off; 0.4.0; compact's pips are in its
  head, or none with Off); _Background_ (a slider with its value in mono, 20% to 100%, as the meter draws it; a theme
  with a floor says "Readout keeps 82% at least"); _A fight ends after_ (2 to 15 s) and _Keep the last_ (5 to 50),
  read by the meter's session; _Pace against_; _Decorations_ (on; both meters and the opened-up window); _Whole
  numbers_ (compact's alone: dimmed with Detailed, saying so); _Pets in their owners' rows_ and _Limit break on its
  own row_ (always, as ACT counts them: shown on and dimmed). _Lock it where it is_ (its tooltip: compact is never
  sized by hand) and _Click-through when locked_ are on Windows, with _Grows_ and _Background_ again; _Use ACT's
  numbers_ (not in this version) and _Kept fights_ · Clear on Advanced. When the meter has stopped itself
  (`IMeterSource.Problem`), an amber warning box under the page's head says why.

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
or sizing; click-through when locked, the mouse goes to the game (`/raidcodex cooldowns unlock`,
`/raidcodex callouts unlock`). They never take focus, make no window sounds and ignore Escape; while
you hold one (unlocked) it's neither placed nor sized. A sample of each shows under a _Preview_ tag
(pressed, it stops), from Settings or `… preview`; the tag is kept clear of the resizing edges and
grips as the meter's buttons are (`ResizeGuard`: moved in when it would be in their reach, and the
window not resizable while the mouse is over it). Each has its settings apart in the config
(`Configuration.Cooldowns`, `.CalloutOverlay`: shown, where, locked, click-through, Grows, compact,
background, width, anchor), and the plugin keeps each window open while it's shown or sampled.

**The party cooldown tracker** (`CooldownWindow`; Settings → Party cooldowns; `/raidcodex cooldowns`; off at
first, in duties or everywhere). Small rows, one a party member's tracked cooldown, by kind (raid
buffs, party mitigation, the healers' party heals and mitigation, Swiftcast, the tanks' Provoke,
Shirk and invulnerabilities, interrupts and stuns), then as the catalogue lists them, then in spot
order. Since 0.8.3 in the callouts' look (until then rows on the window's ground, the recharge filling
behind them, hairlines between kinds): 10px in all round; each row on a dark ground of its own (8px
corners, as solid as 0.3 plus 0.4 of the window's background), 3 apart, 8 between kinds. A row (28
tall, 22 compact): the action's own icon from the game (20, 16 compact;
`ITextureProvider`, its corners rounded 4px, at 45% while it recharges; its kind's Lucide mark in a
quiet square while it loads), the member's job icon (16, 14 compact; since 0.7.0, their spot in its tooltip; the role's mark in a socket while it loads), the action's
name in the client's language (SansMedium 13, 12 compact; white when ready, the text colour while
recharging), and at the right a **countdown ring** (18, 14 compact; `Kit.CountdownRing`, as the
callouts') emptying as the cooldown comes back, white at 75% (yours ember), whole and green when it's
ready; before it the time to ready in mono ("Ready" in green, "42" with a small "s", "1:05"; yours
ember-light), and "1/2" before that in faint mono for an action with charges. A ready row is washed
green at 6%. Pointed at, whose it is and how it's known. The
limit break gauge last (_Limit break gauge_, on at first: "Limit break" and its bars, the meter's,
from `IMeterSource.LimitBreak` while the meter's numbers are read; since 0.7.8, with the meter off or stopped, read for
the tracker alone, `CombatMeter.GaugeNow`, where before it froze; the meter stays off). Shown when it has a row; with
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
  Leg Sweep (off). Settings → Party cooldowns lists them by kind with a switch each (`CooldownPicks`
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
  recast is the sheet's base with the traits at the member's level the sheet doesn't know (since 0.9.0,
  `CooldownCatalog.RecastAt`, from the official Job Guide: Enhanced Swiftcast, Lv. 94, 60 → 40 s; Enhanced
  Tactician, Troubadour and Shield Samba, Lv. 88, 120 → 90 s; a level not known is taken to be the top), or for a
  role action you have too (Reprisal, Addle, Feint, Swiftcast…) and they're at your level, the game's for you; the
  row counts down by the same recast the use went in with (before 0.9.0 the row used the sheet's, so a healer's
  view of another's Swiftcast said 60 s for good); until a use comes more than a second before the recast says it could
  (SPEC4 §2.13: a trait, a level the sheet doesn't know), when the time since the recharge began is
  learned as their recast. A use within a second of their last of the same cooldown is the same one
  seen twice (a pet's echo). One they haven't used since the tracker began is taken to be ready, and
  its tooltip says so; a learned or base recast says it's an estimate.
- **Planned soon** (since 0.5.0): while your plan follows a pull (its clock running), a row whose
  member the plan has use that cooldown in the next 15 seconds (or did in the last 2:
  `PlannedUses.Next`, the soonest) is washed ember with an ember edge, its ring ember, and (since 0.8.3,
  until then "planned in 8 s" in ember mono) a chip before its time, "PLAN 8 s" ("PLAN now" at its time;
  `Kit.Chip`, as the callouts' "YOU"; left out where the row is too narrow for it and its name), and its tooltip adds "Planned: Reprisal at
  3:02, for Akh Morn." The plan's entry is the row's cooldown when its action is the row's, or one of the
  cooldown's rows on the sheet, or its English name is one of the cooldown's (`CooldownView.Matches`).
  Such a row is listed even when ready with _Hide those ready_ on. Only tracked cooldowns (the catalogue,
  as picked) are marked: a planned Rampart, which the tracker doesn't follow, isn't. The sample marks
  the MT's Reprisal every half a minute.

**The on-screen callouts** (`CalloutWindow`; Settings → Plan and callouts → On-screen callouts;
`/raidcodex callouts`; on at first). While your plan follows a pull (`PlanService.Following`, its
clock running: a cue has matched, `PlanService.PlanClock`; with recording off, the callouts being on
is enough for a pull to be followed, as the plan window being open is), its next steps over the fight, each with
a countdown to when it goes off: a step's time is its earliest cue's (`CalloutTimeline.StepTimes`;
a step with no cue has none and isn't shown), counted on the plan's own clock, which the cue follower
keeps to the fight (a cast matched moves it). The steps going off within _How far ahead_ (15 s at
first, 5 to 60), at most _How many_ (3, 1 to 5), soonest first; one that has just gone off stays two
seconds as "NOW" (`CalloutTimeline.Upcoming`). Since 0.8.3 (`CalloutWindow.DrawContent`; until then
a countdown column beside the words, the first step big, the next in smaller capitals, a slim cast bar
under the first) the first step is on a **card** of its own (raised, 12px corners, a 12% hairline; as
solid as 0.55 plus 0.4 of the window's background, so its words read over a busy fight; 12px over and
under, 12 in at its left, 14 at its right; 8, 9 and 12 smaller): at its left its countdown in a
**ring** 64px wide (44 smaller) on a dark well, a 4px (3) arc in ember for the time left of the time
looked ahead, emptying clockwise from twelve o'clock, the seconds in mono 600 at 30 (18) with a small
"s" (a time of a minute or more, "1:05", smaller with none); in its last seconds (the sound's lead,
else 3) the seconds ember and the card washed ember with an ember edge; at its time an ember disc with
"NOW" in it and the card's edge ember with a glow. Beside the ring, 14 from it (11) and centred down
the card: the mechanic's name over the callout as an eyebrow (mono capitals, faint; ember-light in its
last seconds; Sans 12 for words the mono hasn't the glyphs for) when the callout isn't just the name;
the callout in Archivo Expanded ExtraBold in capitals at 30 (22 smaller, or for a callout in Chinese
or Korean, which the 30 hasn't the glyphs for; 16 smaller), two lines at most, white; and your
cooldowns in a **chip** (below). The next steps under the card, 8 from it, as **rows** 32 tall (26),
4 apart, on a dark ground with 8px corners: their times centred under the ring in mono 600 at 15 (13),
muted, ember in their last seconds; their words in SansSemibold 15 (13) in the text colour, and the
mechanic's name after them, faint, where there's room. 12px down and 14 in. Settings' preview beside
the callouts' settings draws the same in small. Since 0.5.0 your spot's planned cooldowns in the first
step (`CalloutLine.You`, `PlannedUses.YouLine`; your spot is `PartyMembers.YourSpot`, read once a
second), since 0.8.3 in an ember-washed chip with an ember edge, 22 tall: "YOU" in ember mono capitals
and what to use in white
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
- **Settings → Plan and callouts** (0.8.0; `/raidcodex callouts settings` opens it). **Plan window**: _Show my
  plan in duties_; _Plan for this duty_ (in a duty with plans for it, a button with the plan shown, opening a menu
  of your plans for the duty, the one shown ticked, and _Read them again_; picked here it's remembered for the duty
  as the plan window's list does; else it says why); _The plan window_ · Show or Hide; _Say each step in chat_ (the
  setting once called Callouts). **On-screen callouts**, one slab: _Show the callouts_ with _How far ahead_, _How many
  at once_, _A sound before each step_ and _Which sound_ (`<se.1>` to `<se.16>`, stepping it plays it, _Hear it_
  beside it: its icon alone where the row is narrow) under it; beside them (under them when narrow) a small
  **Preview** of the callouts as they'll look (as many as _How many at once_, at most three, on their background,
  smaller with _Smaller callouts_; made-up steps and "You: Rampart, Reprisal") and _Show sample callouts_ under it
  (made-up steps a few seconds apart, counting down and starting again every 50 seconds; _Stop the sample_ while
  on). **With your static**: _Share my step with the site_ and _Send my party to my roster_ (both waiting for a
  link; the roster's result under the slab as a sent line or a red box). **Ready checks** (since 0.9.0): _Show at
  ready checks_ (on) and _See it_ · Show a sample. Then "3 more": _How early the sound
  plays_ (1 to 10 s), _Smaller callouts_ (the callouts' Compact), _The callouts' background_ (0 to 100%, 45% at
  first; on Windows too). The callouts' and the plan window's _Lock it where it is_, the callouts' _Click-through
  when locked_ and _Grows_ are on Windows.
- **Settings → Party cooldowns** (0.8.0; `/raidcodex cooldowns settings` opens it). **What shows**: _Show the
  tracker_ with _Where_ under it, _Yours too_, _See it_ · Show a sample (the meter's sample party, each cooldown
  used now and then). Then each kind's section: its eyebrow, "3 of 12" and _Turn all on_ (or off) at its right, and
  a switch for each of its cooldowns (its name over its jobs). Then "3 more": _Hide those ready_, _Limit break
  gauge_, _Smaller rows_ (the tracker's Compact). Its _Lock it where it is_, _Click-through when locked_, _Grows_ and
  _Background_ are on Windows.

## The ready check card and the death recap (`UI/ReadyCheckWindow.cs`, `UI/DeathRecapWindow.cs`, `UI/Model/ReadyCheck.cs`, `UI/Model/DeathRecap.cs`; since 0.9.0)

Two more overlays (`OverlayWindow`, as the callouts and the tracker: their own _Show_, lock, click-through, Grows,
background and width in `OverlaySettings`, `Configuration.ReadyCheck` and `Configuration.DeathRecap`; both on at
first, background 30%), each drawn as a card on its own dark ground (rgb 24 24 29, more solid than the window's
background, 14px corners and a white hairline at 12%), the callouts' look. Both are in Arrange windows (their
samples shown there) and on Settings → Windows. Neither reads a key: the pin and the rest are pressed with the mouse.

**The ready check card** (`ReadyCheckWindow`, 340 wide at first, a third down the screen left of centre: beside the
party list). The plugin only ever reads the game's ready check, never answers it (`Game/ReadyCheckReader.cs`):

- **When it shows** (`ReadyCheckWatch`, tested): from the frame a ready check is seen going on, read from the game's
  ready check agent (`AgentReadyCheck`'s entries, a member each with their answer: while one is still
  `AwaitingResponse` the check is on [verify: the entries are answered or emptied when it ends]; the fields are read,
  no game function is called). It stays while the check goes on and 30 seconds after it ends (`Linger`), and goes at
  once when the pull starts (in combat). A check seen for over a minute counts as over (`Longest`: the game's lasts
  30 seconds), so a reading that stuck can't hold it up. One that starts in combat isn't shown. The pin (top right,
  ember while on) keeps it, through the pull too, until it's pressed again; unpinned after the check it goes a few
  seconds later.
- **Each member** (`ReadyCheckReader.Members`, twice a second while it shows): the party (`PartyMembers`, in the
  party list's order) with their job's icon (`PlateArt.JobBadge`), their name (their job with the meter's _Show:
  Jobs only_), and their food: Well Fed on their character's status list (the Status sheet's rows named "Well Fed",
  and 48) and its time left ("52:10", the fork-and-knife before it). Green; **amber** when it would run out before
  the average length of your kept pulls of this duty is over (`ReadyCheckView.AveragePull`: the kept pulls' list,
  `RecordingInfo.Duration`, those of the duty you're in; with none kept, nothing is amber: only the time shows);
  **red** "No food"; "can't see" (quiet) for one who isn't near (out of range, in another zone).
- **Its head**: "Ready check" (an ember eyebrow), "7 of 8 fed · one runs out mid-pull" ("Everyone's fed" when all
  are; "· one out of sight" when nobody's short but someone can't be seen), and "Your average pull here is 6:40."
  under it when there's one.
- **You** (in a well under the rows; `ReadyCheckReader.You`, every two seconds): the tinctures in your four
  inventory pages (Medicine whose use gives Medicated, so not potions or elixirs [verify: the item action's first
  value is the status it gives]), the meals (the Meal category), and your equipped gear's lowest condition
  (Dalamud's `IGameInventory`, condition 30,000 is 100%, the soul crystal left out [verify: the scale]): "Gear 84%"
  quietly, or under **20%** (`ReadyCheckView.GearLow`) an amber line "Gear 18%: repair soon" ("Gear 0%: broken,
  repair it"), always.
- **One line** when everyone's fed for longer than the average pull (or simply fed, with no pulls kept here) and
  your gear is fine: a green tick, "Everyone's fed" and "The shortest has 27 minutes left · you have 14 tinctures".
- Its foot: "Goes when the pull starts" over a thin ember bar that empties over the 30 seconds after the check
  ends ("…, or in 24 s"); "Pinned" while it's kept. Its sample (Settings, Arrange windows) is a made-up party, one
  of them short of food and one with none, with 18% gear.

**The death recap** (`DeathRecapWindow`, 440 wide at first, at the right a fifth down the screen). Built from the
meter's own deaths (`MeterEncounter.Deaths`, the opened-up meter's Deaths tab: `DeathEntry`, its recap lines, the
killing blow `MeterView.KillingLine` finds), so the meter has to listen: with the recap on it does, where the meter
would (in duties, or everywhere), even with _Show the meter_ off (`CombatMeter.AlsoWanted`).

- **Whose** (`DeathRecapQueue`, tested): _Recap deaths of_ **Me** (at first: the user's choice) or **Everyone**. A
  death is new when it comes after the encounter was first looked at (one there when the plugin loaded, or the recap
  was turned on, isn't). They go 8 seconds after the last one came (`Lasts`), the time standing still while the
  mouse is over the window (held); the pin keeps them until it's pressed again. Several at once (a wipe) are one-line
  rows, newest on top ("Fenn Hollow · Hello World (raid-wide)", the time and a chevron), at most eight; a row pressed
  opens into the full card in its place (its chevron folds it), and under the rows "Goes in 8 s" and the pin.
- **The card** (`DeathRecapView.Card`): the job's icon, "Velka Moor died" (their spot with _Jobs only_), "4:12 · P3"
  (the clock, and the phase in a fight of more than one), a skull. In a red box: "Killed by **Latent Defect** for
  **182,400** (41,200 more than they had), a tank buster, with no mitigation up." (the kind when it's told, "no
  mitigation" when their statuses were read and nothing that lowers damage was up: a barrier doesn't count). "The
  last 6 seconds": each hit, heal and barrier from six seconds before (the killing blow whenever it came), its time
  before the death ("−4.1 s"), a mark (a sword red, a heart green, a shield amber), what it was and from whom (a
  heal's healer, a barrier's: their first name; none with _Jobs only_), its amount (a barrier's isn't known:
  "shield") and the HP after it as a small bar against their max HP (its number in the tooltip; a heal's amount has
  its overheal in it, so the HP after it is never more than their max; when the HP before wasn't seen, none, and the
  tooltip says so), the killing blow on a red wash. "Up at the hit:" the mitigations up as chips (theirs first, then the party's on the boss, the barriers
  last), "Nothing", or "Not seen: they were out of sight"; for a tank, the big cooldowns of their job that weren't
  up, red: "No Rampart, Guardian or Hallowed Ground" (`Mitigations.MissingFor`); "Had Weakness" (amber) when they
  died with Weakness or Brink of Death on. Its foot: **Open in the meter** (the opened-up meter on that death:
  `MeterDetailWindow.Show(MeterTab.Deaths, fight, member, index)`), and alone, "Goes in 8 s ·" and the pin.
- **What was up** (`Core/Meter/Mitigations.cs`): a catalogue of the mitigations by their English Status names (as
  the meter's other lists: the game side finds their rows on the Status sheet read in English, `Game/PrepData.cs`): a
  tank's own (Rampart, Guardian/Sentinel, Hallowed Ground, Damnation/Vengeance, Holmgang, Shadowed Vigil/Shadow Wall,
  Living Dead, Great Nebula/Nebula, Superbolide: the notable ones; Bulwark, Holy Sheltron, Thrill of Battle,
  Bloodwhetting, Dark Mind, Oblation, Camouflage, Heart of Corundum), others' own (Third Eye, Riddle of Earth), the
  party's (Passage of Arms, Dark Missionary, Heart of Light, Troubadour, Nature's Minne, Tactician, Shield Samba, Magick
  Barrier, Temperance, Aquaveil, Sacred Soil, Fey Illumination, Expedient, Collective Unconscious, Exaltation, Neutral
  Sect, The Bole, Sun Sign, Kerachole, Holos, Taurochole), the barriers (Divine Veil, Shake It Off, The Blackest Night,
  Divine Benison, Galvanize, Intersection, the Sage's, Radiant Aegis, Improvised Finish, Tempera Coat, Stem the Tide,
  Shade Shift, Arcane Crest, Manaward) and those on the enemy (Reprisal, Addle, Feint, Dismantle) [verify:
  each status's name as the sheet has it]. A set of them is a 64-bit mask, so a frame's reading makes nothing. The
  meter's game side reads each party member's (during an encounter, each frame, with Weakness: `PartyState.Mitigation`)
  and the party's on the enemies (four times a second, with their targetability: `MeterSession.EnemyMitigation`); a
  death keeps theirs as last read alive (the frame they're seen dead has lost them) with the enemies' then
  (`DeathEntry.Mitigation`, `MitigationKnown`).
- **What the killing blow was** (`DeathHits.Kind`, tested; `DeathEntry.Hit`): at the death, the members whose last
  hits have the same action within a second of the killing blow (one cast lands on them all in one message, before
  the HP that says the death is read). **Raid-wide** when it hit half the party or more (three at least, in a party
  of four or more); **a tank buster** when it hit only tanks (one or two), the one who died among them, for 40% of
  their max HP or more. Anything else (a spread, a puddle, an auto-attack, an instant death) isn't named.

## Arrange windows (`UI/ArrangeWindow.cs`, `UI/ArrangeMode.cs`, `UI/Model/ArrangeWindows.cs`; since 0.8.0)

One mode that shows every overlay at once on the game's screen to drag into place, in place of unlocking,
moving and locking each from its own settings. Settings → Windows and the setup guide start it (the main
window's `StartArrange`; `Arranging` says whether it's on), and so does `/raidcodex arrange`. Its state is
`ArrangeSession` (tested): on or off, the windows taking part, the one picked, each one's place as the mode
started (`WindowPlace`) and the samples it started. The windows read it through `ArrangeMode` (one session).

**Who takes part** (`ArrangeParts.Taking`): each window that's turned on: the meter with _Show the meter_
on; the plan window with _Show my plan in duties_ on, or open now; the callouts and the cooldown tracker with
their _Show_ on; since 0.9.0 the ready check card and the death recap with theirs ("Ready check", "Death recap": their
samples shown, Grows, Click-through and Background in the options panel). With none on, the toolbar says so (Settings → Features turns them on) and has only Cancel and
Done to press. Each shows even out of a duty, with something in it: the meter its sample fight (unless a real
fight is on: then what it shows now), the callouts and the tracker their samples, the plan window as it is (its
plan, or empty), opened if it was closed. Their _Preview_ tags can't be pressed meanwhile (the sample stays).

**A window taking part** is unlocked whatever its lock says (`Locked` reads its setting and the mode: the
setting isn't changed until Done), never click-through, has no title bar (so no ×) but its arranging head
(`ArrangeMode.Head`: 26px across its top, the move icon and its name, "Meter", "Plan", "Callouts", "Party
cooldowns", on the head's own dark ground washed white at 6%, ember at 18% when picked; nothing in it takes the
mouse, so a drag there moves the window), and a 1.5px dashed outline round it (`ArrangeMode.Outline`, the dashes
of `Dashes`, worked out again only when it moves or changes size: white at 55%, ember when picked). The head is
its chrome as the title bar was (the meter's and the overlays' heights are their content's and the head's), so
they're kept at their anchors as ever (`MeterLayout.Place`), and moved and sized as when unlocked: their anchor and
width (the normal meter's height you give it) are saved as you let go, as always. The plan window is ImGui's to
place and size, as ever. A press anywhere in a window picks it (`ArrangeSession.Pick`); the first taking part is
picked at first.

**The toolbar** (`ArrangeWindow`, its own window): near the bottom of the screen, centred, its foot 28px up
(`ArrangeLayout.Toolbar`); raised near-solid with a white hairline at 20%, 14px corners and a faked shadow;
"Arranging windows" and "Drag them where you want. They lock when you're done." and three buttons:

- **Reset places** (quiet): each window to its first place, the mode going on: the callouts and the tracker
  where `OverlayLayout.FirstAnchor` puts them before they're ever moved; the meter at ImGui's first place for a
  window (60px in from the screen's top-left corner, `ArrangeLayout.MeterFirst`; the meter never set one itself);
  the plan window at the right of the screen, 60px in, centred down it (`ArrangeLayout.PlanFirst`; its ImGui first
  place is the meter's), its size kept. Each is placed at its next frame, for its size then, as the corner its
  Grows keeps.
- **Cancel** (outline): every window's place, size, lock, click-through, Grows and background put back as the mode
  found them (`WindowPlace.PutBack` for the overlays; the meter's settings, its anchor, the height you gave it and
  its width, which is ImGui's: `MeterWindow.PutWidth`; the plan window's lock, and its place and size as last
  drawn before, or as first drawn when the mode opened it: `PlanWindow.PutAt`). A window never placed before goes
  back to never placed (the meter to its first place). The plugin unloading mid-way cancels too.
- **Done** (the telegraph): each window kept where it is (already saved as it moved) and locked (its _Lock it
  where it is_, the meter's `MeterLocked`, the plan window's `PlanWindowLocked`).

Done and Cancel end the mode: the samples it started stop (the meter's sample fight, the callouts' and the
tracker's samples) and the plan window it opened closes; samples already on before it stay. The toolbar and the
panel take no focus as they appear, can't be moved, make no sounds and ignore Escape (no keys: every action is a
button or the command).

**The options panel** (`ArrangePanel`, its own window, 250 wide, 24px in from the screen's top-right corner): the
picked window's name, then its few options, the very settings Settings has for it: **Grows** (Down, Up: the
edge that stays put), **Click-through** (once it's locked) and **Background** (its percentage, and a slider; the
meter keeps 20% at least, as Settings does; saved once the knob is let go). The meter's are `MeterGrows`,
`MeterClickThrough` and `MeterBackground`; the overlays' their `OverlaySettings`. The plan window has none of them:
it says to drag its edges to size it.

## Profiles (`UI/Model/Profiles.cs`, `UI/Model/DutyKinds.cs`, `UI/Views/Profiles.cs`, `UI/ProfileNotice.cs`; since 0.9.0)

A setup for each kind of duty: going in puts its profile in effect, leaving puts your usual settings back. What a
profile doesn't change stays your usual setting, and a new profile changes nothing (it's a copy of your usual setup
until you turn its rows on). There are no profiles at first.

**Settings → Profiles** lists them as cards, in order: the name (**Now** on the one in effect), the duties it's used
in, a chip for each thing it changes ("Meter Detailed", "Callouts 3 at once", "Own window places"), Edit; **Usual**
(your settings as they are, Now when no profile is) always last, not edited there; **New profile** at the right of the
head. A profile's page: back, its name big, **Delete** (it asks first; in effect, your usual settings come back);
_Name_; _Used in_, a chip a kind of duty (one profile a kind: a kind another profile has is dimmed, its tooltip says
whose; the first in the list wins if a file ever has two); _What it changes_, a row a setting with a switch at the
right. Off, the row is dimmed and says "usual". Turned on, it starts at your usual value (`ProfileLayer.Override`),
and its control (Off/On, Compact/Detailed, a stepper for the theme, the count, the size) edits the profile's own.

**What a profile can change** (`ProfileItems.All`, a line each: a configuration field by its path, the Settings rows it
shadows, its chip's words): the meter shown, its size, its theme, hidden out of combat, kept open in duties; the
callouts shown, how many at once, smaller; whose deaths the death recap shows; the plan window in duties; the party
cooldowns shown; **Window places**;
the windows' size (fit, or 80–200%). Window places is many fields (`ProfileItems.PlaceFields`): the meter's anchor
and the height you gave it and which way it grows, each overlay's anchor, corner, width and which way it grows
(`OverlaySettings`; an anchor is only good with its Grows, so they go together), and the plan window's place and size,
which the plan window keeps in the settings as it's drawn since 0.9.0 (`PlanPlaceX`…`PlanPlaced`, saved as you let
go; its size in CSS px, so a profile's own window size grows or shrinks it once, not again at each switch; ImGui's own
memory of it isn't the profile's). The meter's width is ImGui's and isn't in it. A new overlay's
place is one more `Overlay("…")` line there; a new setting, one more line in `All`.

**Which kind a duty is** (`DutyKinds.Of`, tested; read by `GameData.Facts`): the zone's ContentFinderCondition's
ContentType row, its `HighEndDuty` flag, its English name and an alliance (the alliance roulette, or a
ContentMemberType of three parties). Ultimate: ContentType 28. Criterion: 30 ("V&C Dungeon Finder") when high-end,
"Another …" or "(Savage)"; else a Variant dungeon, Dungeons. Extreme: a trial (4) high-end or named "(Extreme)",
"(Unreal)" or "The Minstrel's Ballad: …" (the sheet's Extremes of that name, Sphene's Burden and Necron's Embrace
among them, say neither and aren't flagged high-end); other trials are Normal raids. Savage: a raid (5) high-end or
named "(Savage)" or "(Chaotic)", 37 (the sheet's "Chaotic Alliance Raid"), and a field operation's Savage duty
(Delubrum Reginae (Savage), intended use 53). Alliance raids: other raids of three parties, and a field operation's
raid in a zone of its own (Delubrum Reginae, intended use 52; 26, 29, 38 outside the open zones). The Baldesion
Arsenal, Castrum Lacus Litore, Dalriada and the Forked Tower have no zone of their own (they're in the open zones'),
so they're the open world. Rows checked against the game's ContentType and ContentFinderCondition sheets (xivapi,
24 November 2026). Normal raids: the rest of 5. Dungeons: 2, guildhests (3), deep dungeons (21). Open world: no duty, and the field operations'
open zones (the zones of intended use 41, 48, 61). Other duties: the rest (PvP, quest
battles, treasure dungeons…). Outside a duty-finder zone but bound by a duty (a quest's), it's Other.

**The layer** (`ProfileLayer`, tested). The plugin reads the configuration's fields directly everywhere, so a profile
is laid over it: put in effect (`Activate`), each field it changes is kept aside (`ProfileBackup`: your usual values)
and set to the profile's (`ProfileFields`: by path, as invariant text, by reflection; a path that isn't a setting, or a
value of the wrong kind, changes nothing). While it's in effect, a change to one of its fields, from anywhere (Settings,
the meter's menu, Arrange windows, a drag), is the profile's: `Configuration.Save` copies them into it first
(`Capture`), and so does going out of effect. A change to any other field is your usual settings', as ever. Going out
(`Deactivate`) captures, then puts your usual values back. `ProfileActive` and `ProfileBackup` are saved with the
settings, so a crash or a reload while one is in effect is undone as the plugin loads (`Recover`, before anything reads
the settings); the duty you're in puts the profile back silently. Unloading takes it out and saves, so the file never
holds a profile's values as yours (an older version reading it would keep them). A row turned on or off while in effect
is kept aside or put back at once; turned on while another profile is in effect, it starts at your usual value, not the
live one; a profile saved before a field was added to one of its rows gets that field from your settings as it comes in.

**Switching** (`ProfileSwitcher`, tested; asked each frame by the plugin with the zone and its kind): the first profile
used in the kind is put in effect, or your usual settings back; not while Arrange windows is on (its Cancel puts back
what it found) nor before you're logged in. Then the settings are saved and, when Window places is involved, the plan
window put at its place (`PlanWindow.PutAt`; the meter and the overlays follow their anchors by themselves). Going in,
**the notice** (`ProfileNotice`, its own window near the top of the screen, centred): the layers icon, "Ultimate
profile: meter Detailed, callouts 3 at once and 2 more" (`ProfileItems.Summary`) and **Undo**, which takes the profile
out until you leave that zone; 7 seconds, waiting while the mouse is on it, fading out. Leaving, "Back to your usual
settings" for a moment. Neither shows for a profile that changes nothing, nor for the switch made as the plugin loads.
It takes no focus, makes no sounds and reads no keys.

**Elsewhere**: a Settings row a profile in effect sets says so under it in ember ("The Ultimate profile sets this here;
changes go to it."), as does Damage meter's _How it looks_ head and _Arrange windows_ for Window places; Home's chips
end with "Profile: Ultimate" while one is in effect (it opens Profiles).

## Layout

```
RaidPlanner/
  Plugin.cs, Services.cs, Configuration.cs
  Core/       game-free (tested): the recording, its builder and its combat block (CombatLog), the
              store, spots, timeline, words, the site link and calls (SiteLink, SiteRpc, Pacing),
              uploading to the logs (LogUpload: the body, the answers, UploadQueue, UploadPrefs),
              the server's clock (ServerClock),
              plans (Plan, a real arena's rings: Rings, CueFollower), pull summaries and their numbers (PullSummary, PullStats),
              the roster and the diagnostics file (DiagnosticsLog), who the party is (PartyPick),
              the gear (Recording: Gear); Meter/: the combat parser (Effects, CombatParser), the sums (Tally),
              phases (PhaseClock), the meter's session (MeterSession) and its model (MeterModel),
              which is which (CombatLists, CombatKinds), the death recap's mitigations and a killing blow's
              kind (Mitigations), who interrupted, the moments before a pull
              and the countdown (CastWatch: CastWatch, PrePull, CountdownWatch), shields absorbed and the
              Mitigation tab's rules (Guard: ShieldBook, GuardRules; 0.9.0)
  Game/       the recorder, its hooks, the game's data, the meter's game side (CombatMeter), plans
              in the game (PlanService), summaries and the roster (Uploads), uploading every pull
              (LogUploader), the party (PartyMembers), your gear (GearReader), the game's version
              and server time (GameClock), the ready check's reads (ReadyCheckReader) and the statuses and items
              it and the death recap look for (PrepData)
  UI/         the windows: MainWindow (Views/: Home, Pulls, Pull, Send, Settings and SettingsRows (since 0.9.0 SettingsPrep: the ready check's and the death recap's rows), Setup), PlanWindow and its board
              (PlanBoard, BoardArt), MeterWindow (MeterWindow.Plate.cs: compact) and MeterDetailWindow,
              the compact meter's drawer in its themes (PlateMeter), the overlays (OverlayWindow:
              CooldownWindow with its game side PartyCooldowns, CalloutWindow, ReadyCheckWindow, DeathRecapWindow), Glyphs;
              Kit/ (tokens, fonts, text, controls, marks, the Boss, the frame, Home's and the guide's
              parts: HomeParts, the themes' drawing: PlateArt, Settings' panes, sections, more row and
              tags: SettingsParts; the windows' size: UiScale, ScaleFollower, CssLimits); Arrange windows (ArrangeWindow: the toolbar and ArrangePanel;
              ArrangeMode: what the windows read, their head and outline); Model/ (the
              faces: Faces; how big the windows are: WindowSize; the meter's themes' token tables: MeterTheme; compact's
              numbers, rows, ranks' hold, sizes, job icons and settings' move: MeterPlate; numbers and
              words, curves, polygons, the screens' states, the meter's and a pull's views, the
              meter's sizes and place (MeterLayout) and its sample fight (MeterPreview), its
              0.4.0 words (MeterText), the Healing and Mitigation tabs' cards and lines (MeterGuard; 0.9.0), the cooldown tracker's catalogue, book and rows (Cooldowns),
              the callouts' timeline (CalloutTimeline), the plan's cooldowns on its clock
              (PlannedUses), a real arena's floor cut into triangles (ArenaFloor), the overlays'
              settings (Overlay), the ready check's card and when it shows (ReadyCheck), the death recap's card and
              queue (DeathRecap), Arrange windows' state, places and layout (ArrangeWindows), Settings'
              pages, rows, search and the Features cards' tags (SettingsIndex), 0.8.0's main parts (Features), the setup guide's rules (SetupGuide), a pull in a sentence
              (PullGist), pulls by night (PullNights), the plan's next step (PlanProgress),
              /raidcodex's words (Commands), settings profiles and their layer (Profiles) and the duty kinds
              (DutyKinds), SVG paths, dashes; Replay/: a pull's track, shapes, arena, rotations, moments, statuses,
              lanes and steps, and the whole of it: ReplayModel), tested; Views/Profiles.cs and ProfileNotice, Replay's
              board (ReplayBoard) and view (Views/Replay.cs) (0.9.0)
Resources/Fonts/  the faces (Geist, Geist Mono, Archivo; since 0.7.0 Zen Maru Gothic and Fraunces for the meter's
              themes) and their OFL licences, embedded in the DLL (the licences beside it)
RaidPlanner.Tests/  xunit tests for Core and UI/Model
tools/      the icon (icon.svg → images/icon.png)
docs/       PUBLISHING.md, IN-GAME-CHECKS.md (what to try in the game to confirm what it reads)
```

## Commands

`/raidcodex` opens the window; `/raidcodex settings` opens Settings; `/raidcodex stop` ends the pull
being recorded (and keeps it); `/raidcodex plan` shows or hides the plan window; `/raidcodex next` and
`/raidcodex prev` move its step; `/raidcodex send last` sends the newest kept pull; `/raidcodex meter`
shows or hides the meter, `/raidcodex meter unlock` gives it the mouse back (and unlocks it),
`/raidcodex meter big` opens it up, `/raidcodex meter settings` opens Settings → Damage meter, `/raidcodex meter
preview` (or `meter sample`) shows the sample fight in it (again to stop; not while a real fight is on).
What follows `/raidcodex` is read in lower case with runs of spaces as one (`CommandWords.Normalize`).
Since 0.4.0: `/raidcodex cooldowns` (or `cds`, `cooldown`) shows or hides the party cooldown tracker and
`/raidcodex callouts` the on-screen callouts (each says in chat where it will show); after either,
`unlock` gives it the mouse back (and shows it), `preview` (or `sample`) shows or stops its sample,
`settings` opens Settings on its tab (Cooldowns, Plan window). Since 0.8.0 `/raidcodex arrange` (or `arrange
windows`) starts Arrange windows (above): every window that's on shown to drag into place, Done, Cancel and Reset
places on its toolbar (while it's on, nothing new). Since 0.8.1 `/raidcodex size` says how big the windows are;
`/raidcodex size fit` (or `auto`) fits them to the screen and `/raidcodex size 150` (or `150%`) makes them that size,
80 to 200 in tens (`WindowSize.Parse`; above, The windows' look, Size): the way back if a size makes Settings hard to
reach.

Since 0.5.1 the command is `/raidcodex` and `/raidplan` (from when the plugin was Raid Planner) still
works; since 0.7.4 `/rc` does too. All three are registered and each does the same
(`CommandWords.Names`: `Main`, `Short`, `Old`); Dalamud's command list shows `/raidcodex` only
(`CommandWords.Listed`), its line naming just `/raidcodex`, `/raidcodex settings`, `/raidcodex help`
and `/rc` (`CommandWords.HelpFor`). If another plugin has `/rc` already, it stays theirs (Dalamud refuses
it; logged) and the plugin removes only the commands it registered. `/raidcodex help` (or `?`) writes
every command in chat, a line each (`CommandWords.Help`, `HelpLines`). No command is bound to a key:
the plugin reads no keys at all (CLAUDE.md).

## The name, and what kept the old one

Since 0.5.1 the plugin is Raid Codex, as the site's tool is (its address `/raid-codex`; the old
`/raid-planner` addresses redirect there). Everything a player reads says Raid Codex, and the site's
links are built on `SiteLink.CodexUrl` (`https://www.kokos-tools.com/raid-codex`: the front page,
`?settings=raid-plugin` for tokens, `?from-game=<id>`, `/plan/<id>/review?pull=<sent id>`). What
Dalamud or the player's setup keys on keeps the old name, so installs, updates, the settings file and
window places carry on: `InternalName` `RaidPlanner`, the assembly, project, namespace and zip
`RaidPlanner`, the window ids after `###` (`###RaidPlannerMain` and the rest: ImGui keeps a window's
place and size by it), the `WindowSystem`'s name, the configuration, the recording's format, and the
GitHub repository (`koko-puffs/raid-planner-plugin`). The server info bar's entry is titled Raid Codex
(Dalamud keys its order and hiding in the bar by that title, so it starts again where Dalamud puts a new
entry).

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
  slots following the Marker sheet's row order (attack 1–5, bind 1–3, ignore 1–2, square, circle, cross, triangle,
  attack 6–8: the sheet's order, checked in 0.7.4's sweep; the memory's following it isn't yet); the hooks on out
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
- Summaries waiting to be sent are lost when the game closes (pulls waiting to be uploaded aren't: they're the kept
  pulls not sent yet).
- The 0.6.0 readings want checking in the game: `Framework.GameVersionString` ("2025.07.17.0000.0000"), `Framework.GetServerTime`
  (Unix seconds, ticking over with the server: `startedAt`'s milliseconds rest on it), the party list's `World` as the home
  world, the cross-world party's `HomeWorld`, and the World sheet's names.
- Head marker numbers are kept as the game sends them; the site names them from BossMod's data
  where it can (stack, spread, tankbuster…; since 0.7.4 a rotation's way round from BossMod's names). Since 0.7.4,
  to check in the game: a head marker's second argument as whom it points at (`tgt`, BossMod's reading), and a
  rotating mechanic's `turn` events (a boss turning between cleaves, a helper's heading over its cast) coming as
  often as the steps go, and few away from them.
- The compact meter and its themes (0.7.0, with 0.7.1's fixes) want checking in the game (the sample fight shows most of it:
  `/raidcodex meter preview`, then each theme in Settings): the job icons (62100 + the ClassJob row, framed, at
  16px; grey when dead) and their fallback; Zen Maru Gothic and Fraunces built in the atlas and their digits in
  cells; no word or number touching another in any theme (Mochi's bigger words, "9.3 ♥" and "💔 0:30"; Native's
  "◆ DAMAGE 2:32 PHASE 2" and its Axis numbers with their points, "7.66k" in the limit break's row); every bar's
  end, Neon's cap and Grimoire's underline stopping short of the share; Neon's dead name reading through its
  glitch; Grimoire's ribbon clear of your job icon; (0.7.2) every theme's names, labels and numbers level with
  each other and the icons (Mochi's Zen Maru no longer low; Geist not moved), Native's bars ending before the
  number and its outlined digits apart ("9k"), Mochi's and Neon's bars before the number too; the opened-up
  window in each theme (its ground, panels, plates, faces, the picked row's mark, a dead name, the abilities'
  bars, the tabs, segments, buttons and pull menu, the event log, the recap) and in Ready Check as it was; the
  normal meter (_Compact_ off) in each theme, growing down and up: its ground and frame, head band and clock,
  rows on plates (Mochi's 29, Native's 27), icons, bars (none under the number for Native, Mochi and Neon), you
  and the dead, the tabs, column heads, pace, the limit break's row, the foot and its pips, the tags, the
  picker and tab menu (a narrow meter), the lock and the fold's chevron, Deaths and Events, the empty and
  stopped meter, waiting; Ready Check close to the old normal meter; sizing it by its edges and its lock
  still pressable growing up; Settings' Theme row before Compact; the
  pull button's chevron 6 apart from its words there and on "DPS ⌄" and the normal meter's dropdowns; the
  limit break's "–" and "1,337,000" apart; the game's Axis and Jupiter at Geist's line for their size (`GameFontStyle(family, px ×
  1.3)`: too big or small?); the outlined words' weight; the ground, edge, rings and sparkles drawn outside the
  window (Mochi's sticker shadow and stars, Neon's glow) and the window's own corners matching its theme with the
  title bar showing; the details panel as a tooltip beside the meter, on the left when there's no room on the
  right, and never in the way of the menu; the menu off the head (down, and up growing up); growing up (the head
  at the foot, the rows mirrored, the limit break and foot at the top); you pinned when you're ninth or lower; the
  rank hold over a real fight (four snapshots a second); the fresh death's flash; the one line between pulls and by
  hand; compact unlocked showing no resize grip and not sizing from any edge or corner (moved by its title bar), its
  lock and menu pressed at once growing down and up; the normal meter's lock and picker growing up (in the
  bottom-right corner, moved in out of the grip) and the foot's _Preview_ tag growing down taking every press on
  them, while its edges and corners still size it right beside them (and a resize dragged across them not cut
  short), at 100% and a bigger Dalamud scale; the same for the plan window's Previous and Next and the overlays'
  _Preview_ tags; a 0.6 configuration landing on compact once (and an old compact height or a test build's width
  ignored); frame time with eight rows in each theme.
- The meter's layout (0.7.4) wants checking in the game, both meters, in each theme: the normal meter fitted to
  what it shows, growing down and up (one player, then the sample's eight: no empty band at either edge; pace's row
  and the limit break's adding their height as they come, the anchored edge not moving; waiting, Deaths live and
  kept, Events, the empty and stopped meter and the fold all fitted; a height given by its edge capping it, and
  the rows past it cut from the lowest; the height held while the mouse is on it or its menus, a tab picked not
  moving the head, settling once the mouse leaves; a resize by its edge still followed while held); each of the eight Grows, Head and Order settings (the head at the bottom
  growing down and on top growing up; highest and lowest first; the first death and the newest event with the
  top player; compact's pinned you at the end away from the top player, its dashed rule on the right side and
  its rows sliding without a jump); the lock, picker and fold's chevron with the head at the bottom growing down
  (in the bottom-right grip's corner: still moved in out of its reach and taking every press) and the foot's
  _Preview_ tag with the head on top; no words touching in the head or the foot at either edge; Native's title
  band and gilt rule, Neon's horizon and Grimoire's rules facing the rows with the head at either edge, and the
  frame's own ornaments (Mochi's sparkles, Grimoire's corner pips) unchanged; the menus opening away from the
  head; Settings' three rows and the preview following Head and Order; a 0.7.3 configuration growing up landing
  on Up, Bottom, Lowest first once and looking as it did.

## Release notes

### 0.9.0 (not released yet)

- **Replay a pull in the window**: a pull's page has _Summary · Replay_. Replay plays the pull back as the Raid Codex
  does: the arena with the party by job, the enemies, their mechanics as they're cast and land, tethers, markers,
  waymarks, HP and deaths; down the right each boss's HP and cast, the party's HP, barriers and debuffs, and what
  just happened; along the bottom Play and the pull's steps, the bosses' buffs, the party's debuffs and the deaths,
  pressed or dragged to go anywhere in it, and grown to fill the window. A line that runs through its caster (Seiryu's
  sigils) is drawn across it, as the Raid Codex draws it, not only in front. A boss's card says "Interrupted" once
  the cast is stopped, not from the moment it began (its time left counts down to the interrupt until then).

- **A ready check card**: as the game's ready check starts, a card beside the party list says who's fed and for how
  long (amber when their food would run out before your average pull here is over, red with none, "can't see" for
  someone out of range), "7 of 8 fed · one runs out mid-pull" over your average pull, and yours: tinctures, food and
  a warning when your gear's under 20%. When everyone's fed and your gear's fine it's one line. It goes when the pull
  starts or 30 seconds after the check; its pin keeps it. It only reads the ready check, never answers it. Settings →
  Plan and callouts → _Show at ready checks_ (on). Its one line no longer says your gear is fine when it couldn't be
  read.
- **A death recap**: when you die (or anyone, as Settings → Damage meter → _Recap deaths of_ says), a card from the
  meter's own recap: what killed you and by how much over, whether it was a tank buster or a raid-wide, the last six
  seconds' hits, heals and shields with your HP after each, what mitigation was up (and for a tank, the big cooldowns
  that weren't), Weakness, and Open in the meter. It goes after 8 seconds, held while you point at it; its pin keeps
  it. A wipe's deaths are one line each, newest on top; press one to open it. The meter listens with it on, even with
  its window off. A heal's line never says more HP after it than your max (its overheal left out), and a line whose HP
  wasn't seen says so rather than 0.
- Both are in Arrange windows and on Settings → Windows (show, lock, click-through, grows, background), each with a
  sample.

- **Settings profiles**: Settings → Profiles keeps a setup for each kind of duty (ultimates, savage, extreme,
  criterion, alliance raids, normal raids, dungeons, the open world, other duties). Going in puts its profile in
  effect, with a short notice and Undo; leaving puts your usual settings back. A profile changes only what you turn on
  in it, starting from your usual setting: the meter (shown, size, theme, out of combat, between pulls), the callouts
  (shown, how many, smaller), whose deaths are recapped, the plan window, the party cooldowns, the windows' size, and its own window places
  (Arrange windows in its duties moves those). Changes made in a duty to what its profile changes are the profile's.
  Kept safe across a crash or a reload. Home says which profile is in effect. The Extreme trials named "The
  Minstrel's Ballad" (Sphene's Burden, Necron's Embrace) are Extreme, not Normal raids.

- **The cooldown tracker's recasts at level 100**: the others' Swiftcast counts down from 40 s, and a machinist's
  Tactician, a bard's Troubadour and a dancer's Shield Samba from 90 s (the traits at Lv. 94 and 88 the game's Action
  sheet doesn't carry), from their first use rather than once learned; synced below those levels, the sheet's. A
  Swiftcast seen with your own role's recast now counts down by it too (it said 60 s for good before).

- **The meter for healers**: the Healing tab's bars show the healing that landed and, striped after it, what your shields
  absorbed (an estimate from the barrier the game shows before each hit, credited to whoever put the shield on; never
  counted as healing), with a legend and the party's overheal at the foot. Opened up, Healing has a Shields column and,
  for the player picked, cards (healing that landed, overheal and what wasted most, shields absorbed, heals cast on and
  off the GCD) over their heals by ability with overheal and casts.
- **The meter for tanks**: a new Mitigation tab in the opened-up meter: damage taken and your share, how much of the time
  you held the boss your own mitigation was up, the tank busters on you and how many met nothing, self-healing, and every
  buster and raid-wide on you with what was up (your mitigation, the party's, shields, Reprisal, Addle, Feint or Dismantle
  on the enemy) and a tag: Mitigated, Light, Unmitigated or Died.

### 0.8.3 (not released yet)

- **The cooldown tracker in the callouts' look**: each cooldown on a row of its own with a countdown ring that empties
  as it comes back (yours ember, whole and green when ready), the time with a small "s" beside it, a little space
  between kinds, and one your plan uses soon washed ember with a "PLAN 8 s" chip.

- **The callouts redesigned**: the next step is on a card of its own with its countdown in a ring that empties as it
  comes, the mechanic's name over what to do, and your cooldowns in a chip; in its last seconds the card turns ember,
  and at its time "NOW" sits in an ember disc. The steps after it are quiet rows under it, their times lined up under
  the ring. The card and rows stay readable over a busy fight, whatever the background.


### 0.8.2

- **DoT and HoT ticks are recorded and on the meter**: 0.8.1's diagnostics showed the game sends them as two messages
  the plugin didn't read (a DoT's on the enemy, a HoT's on the one healed, each with its source), so pulls and the
  meter had none of their damage or healing. They're read now, each credited to whoever put it on.
- **The meter stays open between pulls in duties** (_Keep it open in duties_, Settings → Damage meter, on at first):
  the pull just done stays up while you reset. Off, it folds to one line between pulls as before (or hides, with
  _Hide it out of combat_). Out of duties it folds as before.
- Diagnostics no longer write every actor-control message or the flying numbers (0.8.1's, which found the ticks).

- **The meter's column heads are gone** (ENCDPS and %, HPS and Over, Taken and %): the rows start under the tabs,
  and the meter is 14px shorter.

### 0.8.1

- **Diagnostics look further for DoT and HoT ticks** (`actor-control`, `fly-text`): a 0.8.0 log showed a White
  Mage's Dia and Regen ticks never come as the message the plugin reads them from, so the plugin now writes the
  other messages about the party and enemies, and each number flying off the screen, to find where they do.

- **Windows sized for your screen** (Settings → Windows → Size, `/raidcodex size`): on a screen taller than 1440 px
  every window, its words, icons and spacing are bigger, as much as the screen is taller (150% on 4K), unless
  Dalamud's own scale already does it; or pick a size from 80% to 200%. Open windows grow or shrink with it.
- **The main window is wider**: 1040 at first and at the least (it was 560, and 460 at the least), as far as the
  screen allows, so Settings' sidebar and the meter's preview always have room; Settings no longer widens it.

### 0.8.0

The plugin's windows made easier to use (the mock-ups: kokos-tools' canvas "Raid Codex plugin: setup guide, windows
and settings").

- **A setup guide on a fresh install** (above, The setup guide): five steps, about a minute. Welcome; what it should
  do (one switch for each main part, with Recommended, Everything and Recording only to start from); link your
  account (when a part you picked needs one); how the meter looks and where the windows go; and what's on, with a
  sample fight and sample callouts to try. Skip keeps the recommended setup. Existing players never get it: Settings
  → Features runs it again.
- **Tabs along the top of the window**: Home · Pulls · Settings. **Home** says what's on (a chip for each main part,
  opening its settings), the latest pull in a sentence ("Wiped at Hello World 2, P3. Three deaths…") with Open in
  Review, and tonight's pulls; while a pull is recorded, its clock and where you are in your plan, and nothing else
  that needs you. **Pulls** lists every kept pull by night. **A pull** reads as a sentence and a strip of the fight,
  with its upload and what happened.
- **Settings by what you want to do**: Features, Recording and uploads, Damage meter, Plan and callouts, Party
  cooldowns, Windows, Account and privacy, Advanced, in a sidebar (a menu on a narrow window), with _Find a setting_.
  Each page shows what most people change; the rest fold under one "N more" row. The meter and the callouts have a
  live preview beside their settings, and each page can be reset. Every setting is still there, some under plainer
  names (_Record fights by themselves_ is _Record my pulls_, _In every duty_ is _Which duties_). Opening Settings on
  a narrow window widens it for the sidebar (once each time the window opens).
- **Arrange windows** (Settings → Windows, the setup guide, `/raidcodex arrange`): every window that's on shown at
  once on your screen, with a sample in it, to drag into place; a window picked has its options beside it (which way
  it grows, click-through, background). _Done_ locks them where they are, _Cancel_ puts everything back, _Reset
  places_ moves them to where they first go. The main window steps aside while you arrange.
- **The meter is Detailed on a fresh install** (it was compact since 0.7.0); saved settings keep theirs.
- **Uploaded logs are said to be public**, as on FF Logs, wherever the plugin speaks of them (the guide included):
  you can make yours private on the site.

### 0.7.11

- **A cast is written once.** One whose start read a third of a second off was written again as a new cast (Ihuykatumu's
  Wing of Lightning: every helper's twice); while an enemy's cast of an action goes, it's that one.
- **One hook that can't be turned on no longer keeps the others off** for the rest of the session.
- **A pull with no attacks says more in /xllog:** the attacks' hook's state with the warning, and, when another program
  had hooked the game's function for attacks before the plugin did, that it had (a likely cause).
- **Diagnostics write each DoT and HoT tick message as sent** (`hot-dot`): recorded fights have had no ticks in their
  combat block, so what the game sends for them needs seeing.

### 0.7.10

- **A cast points the way its caster faced.** The game leaves a cast's own facing at 0, so every recorded cast faced
  south, and the Raid Codex's replay drew a line or a cone that no hit followed pointing the wrong way. Casts now carry
  the caster's facing as they began.
- **A pull with no attacks at all is said in /xllog.** Pulls made with 0.7.9 came through with no hits, damage or
  numbers (the game handed the plugin not one attack, though its other hooks worked); a pull of 30 s and more with none
  now writes a warning to report, so it can be found.

### 0.7.9

- **No full garbage collection stops the game during a pull.** Every plugin shares one memory heap; when it's big (other
  plugins' files: 3.2 GB in one report) a full collection can stop the game for a second, whoever set it off. While a
  pull goes the plugin now asks the runtime to do those in the background, and sets it back after. A collection a plugin
  forces itself can still pause the game, and /xllog says what kind each long pause was.

### 0.7.8

More of 0.7.6's and 0.7.7's work on stalls in long fights, and fixes from a scan of the whole plugin:

- **A mechanic's hit keeps its room.** Of 0.7.7's 16,000 for the many, only auto-attacks count now (with statuses and
  aggro): a helper's AoE often comes with no cast, and its hits were counted with the rest.
- **Less work each frame and each hit in a long fight:**
  - The fight's invisible helpers are no longer put in the frames while they've never been targetable (they were
    taken out at the end anyway): an ultimate's dozens a frame were tens of MB kept to the pull's end.
  - A frame keeps the positions it's given instead of copying them again.
  - A nameless enemy's name is read at most once a second, not every frame (most helpers never have one).
  - An enemy's hit on the party no longer makes a dozen small lists; past the cap it only notes what could kill.
- **The window no longer reads a whole pull's file to count its party.** The list of kept pulls now says how many
  were there; a pull kept before 0.7.8 has its file read once, as before.
- **Deleting a pull doesn't wait on the disk:** it leaves the list at once, the files are written on a worker.
- **Uploads during an outage wait properly.** With no connection or the site down, the wait between tries keeps
  growing to 15 minutes; before, each waiting pull was read, compressed and sent again back to back. A pull just
  uploaded can't be picked up and sent a second time before it's marked.
- **A save can't be lost to the plugin loading at the same moment,** and an empty file left by a crash is cleaned up.
- **A status on an enemy that leaves ends.** Before, it lasted to the end of the pull on the site.
- **The meter no longer turns itself off for the night** after three odd hits scattered through it: they count per
  stretch, and a stop lasts until the next zone. A game patch that breaks it still stops it at once.
- **The cooldown tracker's limit break gauge keeps moving** with the meter off.
- **Diagnostics** (if you turned it on) only makes the lines it keeps, and holds at most 256 KB waiting for the disk.
- **A game patch can't make the map-effect hooks crash the game:** each message is checked readable before it's
  read, and a handler that looks wrong is turned off for the session.

### 0.7.7

- **Saving and thinning a long pull leave far less garbage** (more of 0.7.6's work on stalls; the garbage collector's
  pauses stop the game even when a worker made the garbage):
  - Each step of thinning a pull too big for the site was written out whole to see whether it fit: up to a dozen
    strings of several MB each. Each step is now only counted, and the one that fits written once. What's sent is
    the same, byte for byte.
  - Writing a pull's file held all of it in the writer's buffer, then a copy, then a string, then its bytes: about
    seven times the file, on the large-object heap, at every pull kept. It's now written straight to the file, 64 KB
    at a time, with each frame's ids sorted and named once, not per frame: a 2.5 MB file went from 15 MB of
    garbage to under 1 MB. The file is the same, byte for byte.
- **A very long pull keeps its mechanics and deaths.** Of the 20,000 events a pull keeps, hits, statuses and aggro
  changes now stop at 16,000, so the casts, markers, tethers and the rest late in the pull aren't lost to them;
  deaths and raises are kept 1,000 past the 20,000. A hit that isn't kept still names what killed someone.

### 0.7.6

- **Smoother replays is on by default.** Where everyone stands is kept four times a second, so the site's replays move
  smoothly. Settings saved before 0.7.6 have it turned on once; turn it off and it stays off.
- **Less stalling in long fights** (a player's report from The Omega Protocol: inputs dropped, "people
  teleporting", worse as the night went on):
  - **A pull's end:** the mechanics check, taking the helpers out of every frame, and settling casts now run on
    the save's worker, not the game's thread. On a 15-minute pull they took a frame of 50 ms or more, plus tens of
    megabytes of garbage, at every wipe. Only your own recasts are still read on the game's thread (the game's
    to read).
  - **An upload's answer:** marking a pull sent or refused writes two small files, each forced to disk. That's now
    done on a worker, not the game's thread.
  - **Uploads wait for the pull to end.** Fitting a long pull's file to the site's 8 MB, then serialising and
    compressing it, leaves a lot of garbage. Though a worker does it, the garbage collector's pauses stop the game,
    so a new upload no longer starts during a pull.
  - **Stalls are written to /xllog.** When the plugin's own work in one frame takes over 20 ms, /xllog gets a
    warning naming the part (the recorder's frame, the game's messages read, the meter's frame, the plan's frame,
    drawing its windows), at most every 5 s each. So does a garbage collection that paused the game that long
    (`Game/Stalls.cs`).

### 0.7.5

- **The fight's helpers are recorded.** The invisible actors that cast nearly every real AoE (the game's helpers,
  a battle NPC kind of their own) were left out since the first version: only the boss's own casts were kept, so a
  replay showed almost no mechanics and the damage they did to the party was missing from Survival, fails and deaths.
  Their casts, hits, statuses and head markers are recorded now. Recordings made before can't get them back.
- **Diagnostics stays on.** Settings → Recording → _Diagnostics_ is kept as you leave it; it no longer turns itself
  off each time the plugin loads.

### 0.7.4

- **The normal meter is as tall as what it shows.** It no longer keeps room for pace and the limit break before
  they come, so there's no empty band at the edge away from its head (a meter growing up with one player had 53px
  over its row). Its rows, pace's row, the limit break's, waiting's party and the Deaths and Events lists set its
  height; the edge it's anchored at stays put and the other moves as they come and go. The height you give it by its
  edge is the most it grows to. While the mouse is on the meter (either meter, or its menus) its height is held, so
  picking a tab or a row coming doesn't move its head from under the mouse (Grows Up with the head on top); it
  settles once the mouse leaves.
- **Grows, Head and Order are three settings.** Settings → Meter's _Grows_ (Down, Up) is only the edge that stays
  put now; _Head_ (Top, Bottom) puts the clock, tabs, pull menu and lock (compact's head) at either edge, whichever
  way it grows; _Order_ (Highest first, Lowest first) is whether the top player is at the top or the bottom of the
  list, reading down. Both meters; compact's pinned you stays at the end away from the top player. A meter that grew
  up keeps its look (Up, Bottom, Lowest first).
- **The themes' head bands face the rows** at either edge: Native's title band no longer runs into the window's
  edge with the head at the bottom, nor Neon's horizon.
- **The meter's numbers, checked.** Damage a tank's retaliation (Vengeance, Damnation) sends back is the boss's to take
  even when the game sets only the "from the target" bit (before, without the "to the source" bit too it read as the
  tank hitting itself and was dropped). The healers' limit break isn't the party's healing any more: it has no row on the
  Healing tab (nor on the site), so the party's line was more than its rows and the shares fell short of 100. The
  summary's crit and direct hit rates and active time round halves up, as the site's server summary does (they were
  rounded to even: 0.25 s of active time was 0.2 here and 0.3 there).
- **`/rc`, and `/raidcodex help`.** Every command works with `/rc` in place of `/raidcodex` (unless another plugin
  has `/rc`: then `/raidcodex` still works); `/raidcodex help` lists them all in chat. Dalamud's command list and the
  plugin's description name just `/raidcodex`, `/raidcodex settings` and `/raidcodex help`; the description is shorter.
- **No keys.** The plugin reads no keys at all, so nothing can clash with your binds: a confirmation's Escape is gone
  (press No or Yes).
- **Rotating mechanics, recorded whole.** An enemy at a mechanic (its cast going, or within 1.5 seconds of its cast's
  end or its last hit, an auto-attack not counted) turning 2° or moving a yalm is a `turn` event every frame it does
  (at most ten a second each, 3,000 a pull, not counted towards the events' cap): a boss turning step by step through a rotation, and a helper's
  heading over its cast, which the half-second frames missed or (a helper's, not in the frames) never had. A head
  marker over a helper the frames hadn't met yet (one come between them, as a rotation's "clockwise" over its helper
  often is) was dropped; it's taken in now as a hit's is, and every marker keeps whom the game says it points at
  (`tgt` [verify]). The site finds the rotation, its way round, step and timing, and draws it.

### 0.7.3

- **The normal meter takes the theme too.** With _Compact_ off, the meter is the theme's bigger sibling of its compact
  one: its ground and frame, its head band and clock, its faces, the rows on its plates with its icons, bars and marks,
  and its tabs, column heads, pace, limit break row, foot, tags, dropdowns, lock and menus in its chips, accent and
  lines; its columns as wide as what they show. Its tabs, pace, columns, growing up or down, sizing and lock work as
  before. Ready Check is today's normal meter refined to match compact Ready Check (rows on plates, the bar in the
  role's colour, the ember notch for you). Settings → Meter's _Theme_ is its own row now, before _Compact_, for both
  meters and the opened-up window, with _Role colours_ and _Decorations_ under it.

### 0.7.2

- **The opened-up meter takes the theme.** The big window (Damage, Healing, Taken, Deaths, Events) is drawn in the
  compact meter's theme as the meter is: its ground and frame, its panels and plates, its faces, its role colours and
  icons, its bars, its marks for the picked row and the dead, its chips and accent for the tabs, the buttons and the
  pull menu. Ready Check looks as it did. Its columns are as wide as what they show ("–" and "1,337,000" no longer
  run together).
- **Words sit level.** Each line is centred by its capitals' ink, not by its font's line: Mochi's Zen Maru Gothic no
  longer sits a pixel or two low beside the icons and numbers; Geist doesn't move.
- **Native's numbers stand clear.** Its bars end before the number (as the game's party list has them), and its
  outlined digits have a pixel between them ("9k"). Mochi's pill and Neon's bright cap end before the number too.
- **Dropdowns have room for their chevron**: "Pull 12 · now ⌄", "DPS ⌄" and the normal meter's keep 6px between
  the words and the chevron.

### 0.7.1

- **The compact meter sizes itself.** It can't be resized by hand any more (it only snapped back): its width is its
  theme's, its height its rows'; a size given it before is let go.
- **The lock is easy to press again.** Dalamud lets a window be resized from every edge and corner, and the press
  went to the resize when a button sat in that reach (the meter's lock growing up, in the bottom-right corner). The
  meter's, the plan window's and the overlays' buttons are now kept out of it (worked out from the font and the
  window's corners, not guessed), and the window isn't resizable while the mouse is over one of them; it still sizes
  from everywhere else.
- **Every theme reads in the game.** The number and share columns are measured in the face the game draws (at
  least the mock-ups' widths), so a wider face never runs numbers together; the bars end before the share column;
  the head's and foot's words are drawn only as they fit. Mochi's words are a step bigger (its rows 24 tall, the
  meter 268 wide) and its numbers Geist SemiBold; Native's numbers are the game's Axis (MiedingerMid lost its
  decimal points); Neon's dead name glitches more faintly; Grimoire's ribbon is narrower and clear of your icon.

### 0.7.0

- **A new compact meter, and it's the default.** Round four's "Plate": one 24px head (the clock, the phase, the limit
  break's pips, a menu with the tabs, the pulls and pace, the lock), the top eight on near-solid plates that read over
  any arena, the bar in the role's colour, the job's own icon instead of M1, R1, OT…, numbers in three figures (27.8k)
  in fixed columns, you marked and pinned at the foot with your rank when you're below the eighth, a death's mark and
  how long they've been down, the limit break's row and the party with pace at the foot. 248 × 238 for eight and the
  limit break (the old compact was 330 × 272). Ranks swap only once a lead has held a second; rows slide and bars ease.
  Pointed at, a row opens a details panel beside the meter (the figures, the top abilities, deaths, GCDs, potions);
  pressed, the opened-up window as before. _One line_ (its menu) folds it to the clock, the phase, you and your rank,
  the deaths and the pips. It replaces the old compact; your settings move to it once (the normal meter is a switch
  away, unchanged).
- **Six themes** (Settings → Meter → _Theme_, with a live preview): Ready Check (the plugin's look, the default),
  Mochi (cute: a cream sticker, pastel pills, hearts and sparkles), Native (the game's own windows and fonts), Readout
  (a terminal), Neon (synthwave) and Grimoire (a parchment tome). _Decorations_ (on) turns their sparkles, scanlines,
  horizon, glitch and ribbon off; the plate-less ones keep a floor under the Background slider so their words stay
  readable over a bright arena.
- **Role colours you can tell apart with colour blindness**, on the bars and icons (each theme its own set), or the
  game's blue, green and red (Settings → Meter → _Role colours_). _Whole numbers_ writes 27,840 instead of 27.8k.
- **Job icons, not spots,** for every player in the meter, the opened-up window, the cooldown tracker and a pull's
  deaths (the spot in the icon's tooltip).
- Two fonts travel with the plugin for the themes (Zen Maru Gothic and Fraunces, OFL, latin; about 210 KB), loaded
  only while their theme is picked; Native uses the game's.

### 0.6.0

- **Every pull goes to your logs.** _Upload my logs_ (Settings → Recording; on, once linked) uploads each pull whole to
  your logs on the Raid Codex as soon as it's kept: gzipped, in the background, one at a time, the oldest first. Pulls
  kept while you're offline, or when the game closed first, go at the next chance; the site's hourly limit (60), its
  being busy or saving being off are waited out; a refused token or full logs stop it with a message. Pulls still to go
  are held past _Keep the last_ (up to 200). _Upload my older pulls_ sends the ones kept before. It replaces _Send each
  pull_ (your setting moves over: uploads start from the day you update). Your link's token from 0.5 works as it is.
- **Your logs are public on the site** with your party's names and home worlds, as on FFLogs; _Who sees your logs_ opens
  the site's Settings → Your logs, where you can make yours private (and unranked) or hide your character.
- **The recording keeps the party's names, home worlds and levels**, the game's version and the server's time as the
  pull began (for the logs: who, which patch, and two players' recordings of one pull told apart). Still format version 1.
- The progress summary (_Track my progress_) goes as before. "The site keeps your newest 30 sends" is gone: it keeps them all.

### 0.5.1

- **Renamed Raid Codex** (was Raid Planner), as the site's tool is: its name in the plugin installer,
  every window, button, tooltip and chat line. `/raidcodex` is the command; `/raidplan` still works.
- **The site moved to `/raid-codex`**: the plugin opens and sends its links there (the old
  `/raid-planner` addresses redirect). The plugin's internal name, its settings and kept pulls, its
  windows' places and the recording's file are as they were.

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
  - the cooldown tracker washes a cooldown the plan has its member use in the next 15 seconds in ember,
    with a "PLAN 8 s" chip (shown even with _Hide those ready_ on).
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
  a second (on by default since 0.7.6).
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
- **Settings → Recording → Diagnostics** (off; until 0.7.5 off again each time the plugin loaded): while on, the raw
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
