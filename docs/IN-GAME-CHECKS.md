# In-game checks

What the plugin reads from the game that hasn't been confirmed in the game yet, as things to do while playing. For
each session: turn **Diagnostics** on (Settings → Advanced), play, then send `diagnostics-<date>.log` from the
recordings folder with a short note of what you did and roughly when (the log has character names; trim them if you
like, the numbers are what matter). A recording of the same pull helps too (it uploads by itself, or send its link).

Already confirmed: DoT ticks (actor control 1541) and HoT ticks (1540) with their source; enemy casts, helpers and
hits in real pulls; actor control 23's layout (Lucid Dreaming's MP).

The list is ordered by how much each affects the numbers. The details of each reading are in DETAILS.md (Not yet,
Diagnostics).

## 1. DoTs and HoTs in a full party (the meter's damage and healing)

Since 0.8.2 ticks are read, but only a White Mage's Dia and Regen were seen. The game sends no status with a tick, so
the plugin names each tick after the caster's latest DoT on the target.

- A party where several jobs keep DoTs up: Bard (two DoTs), Paladin, Gunbreaker, Samurai, Dragoon, Scholar,
  Astrologian, Sage, Black Mage. Check: each job's DoT damage shows under the right player on the meter.
- Two players with DoTs on the same striking dummy (outside housing): each gets their own.
- Ground effects: Salted Earth, Doton, Asylum, Sacred Soil, Earthly Star, Kerachole. Check: the healing or damage is
  credited to the player who placed it (it might come from the ground effect rather than the player).
- A fairy's or Seraph's HoTs (Whispering Dawn, Consolation): credited to the Scholar.
- A HoT on someone else (Regen on the tank, Medica II on the party).
- A DoT tick's number against the number shown on screen (only heals were compared).
- A boss bleed that kills someone: does the death say what killed them? (Right now a death to a bleed tick can't name
  the bleed, because the tick carries no status.)

## 2. The numbers next to ACT or FF Logs

- One pull with ACT (IINACT) or an FF Logs upload alongside: damage per player within about 1%, and the same crit
  and direct-hit rates. This checks the plugin's reading of every hit.
- A melee job hitting and missing positionals (Monk, Dragoon, Ninja, Samurai, Reaper, Viper): the bonus % the
  battle log shows ("+61%") should match `party-hit`'s bonus.
- Pets and summons: Summoner, Scholar's fairy, Machinist's Automaton Queen, Dark Knight's Living Shadow,
  Ninja's Bunshin. All should count for their owner.
- Vengeance or Damnation (the tank's reflect), Bloodbath (healing its user); Holmgang or Hallowed Ground taking
  nothing.
- Limit breaks 1, 2 and 3, and a healer's LB3 raising the party.

## 3. Deaths, raises and Weakness

- Someone dies and is raised by a healer, by Swiftcast + Raise, and by a Red Mage's Verraise. Check: who raised whom.
- The same person dies again with Weakness, then again with Brink of Death.
- A death to an instant kill (doom running out, an enrage).

## 4. Interrupts, cancelled casts and stuns

- An interruptible boss cast, interrupted with Interject or Head Graze (the `cancel-cast` line).
- A party member cancelling their own cast by moving, and one killed mid-cast.
- Low Blow or Leg Sweep on a boss that's immune (it should show as resisted).

## 5. Potions

- Your own tincture, NQ and HQ, in a pull and pre-pull (on the countdown).
- Someone else drinking one (`action-type`).

## 6. Tank swaps and the enmity list

- A tank swap with Provoke and Shirk while you have the boss targeted (`hate`, and the meter's tank-swap event).
- A DPS pulling aggro off the tank.

## 7. Markers and the arena (the replay on the site)

- A fight with head markers: limit cut numbers, stack and spread markers, tank busters, and the rotating
  clockwise/counter-clockwise markers. Check on the site: the right marker over the right player.
- Head signs placed by players (attack 1–8, bind, ignore, shapes; `signs`): each should come out as the one placed.
- A fight whose arena changes (tiles breaking, walls appearing): the replay should change with it.
- A rotating cleave or a boss that turns a lot: cones and lines should point the right way in the replay.
- Ground placements (Sacred Soil, Asylum, Earthly Star, Doton): drawn where they were put (`ground`).

## 8. The limit break gauge

- A light party (2 bars) and a full party or alliance (3 bars), filling and spending it (`lb-gauge`): the meter's
  and the tracker's bars should match the game's.

## 9. Pull starts and ends

- A pull started from a /countdown, one with the countdown cancelled, and a precast or tincture before zero
  (`countdown`).
- A wipe and a clear: each should end one encounter on the meter, and the recording should end with the duty's
  result.
- A wipe and restart: the cooldown tracker should reset everyone's cooldowns.

## 10. Your gear, the party and worlds

- Your `gear` line against the character window: item level (with a two-handed weapon: Dragoon, Black Mage,
  Machinist…) and attributes, also in a synced duty.
- A cross-world party outside a duty (a Party Finder group at striking dummies): everyone on the meter, with the
  right home worlds.
- An alliance raid: your own group chosen.
- A party member in another zone while you're in a duty.
- Two people recording the same pull: the site should match them as one pull.

## 11. Your cooldowns in the tracker

- A synced duty (a lower-level trial): recast times with traits at the synced level (`own-recast`).
- Actions with charges (Oblation, Divine Benison, Tetragrammaton): "1/2" and the time to the next charge.
- Ninja's Dokumori (replaces Mug) and other level-replaced actions.

## 12. Client language and the windows

- A Japanese, German or French client: everything that's found by its English name still works (interrupts, raises,
  DoTs, shields, job names).
- A Chinese or Korean client, if possible (they may not have the English sheets).
- The callout sound follows the game's volume and chat-sound setting.
- The new looks (0.8.x): the window size on a 4K screen, the callouts' ring and NOW, the cooldown tracker's rings, the
  meter staying open between pulls in duties.

## 13. The ready check card and the death recap (0.9.0)

- Start a ready check, and have someone else start one: the card comes as it starts (you answering or not), stays
  while it goes on, goes 30 seconds after it ends, and goes at once when the pull starts. This checks that the ready
  check agent's entries say "awaiting" only while a check is on.
- A party member without food, one whose food runs out in a few minutes, one out of range or in another zone: red
  "No food", amber (with pulls kept for the duty), "can't see". The time left should match the game's.
- Your tinctures and meals: the counts match your inventory (potions and elixirs aren't tinctures). Gear under 20%
  (or nearly broken): the amber warning, and the percentage matches the game's repair window.
- Die to a tank buster with Rampart (or another big cooldown) up, and with nothing up; die to a raid-wide: the
  recap says "a tank buster" or "a raid-wide", lists what was up (Reprisal on the boss too) and, for a tank, what
  wasn't. Statuses are found by their English names: check one each on a non-English client.
- A wipe with _Recap deaths of: Everyone_: one line each, newest on top; Open in the meter opens the right death.

## 14. Settings profiles (0.9.0)

- Make a profile for each kind and go into one of each: an ultimate, a savage raid, an extreme and an unreal trial, a
  criterion dungeon (normal and Savage), a variant dungeon (it should count as a dungeon), an alliance raid, the
  Chaotic alliance raid (Savage), a normal raid and a normal trial (Normal raids), a dungeon, a deep dungeon, Eureka
  or Bozja or the Occult Crescent (Open world), a PvP match (Other duties). Home's chip says which profile is in
  effect. The kind is read from ContentType, HighEndDuty, the English name and the alliance roulette (`DutyKinds.Of`).
- The notice going in, its Undo (your usual settings until you leave), "Back to your usual settings" going out.
- Window places: Arrange windows in a duty with a profile that keeps its own, then leave: every window back where it
  was, the plan window too. Reload the plugin, and close the game, in such a duty: your usual places and settings
  are back after, and the profile's again in the duty.

## 15. Shields absorbed and the Mitigation tab (0.9.0)

The meter estimates what a barrier soaked from the barrier the game shows on a member as an enemy's hit arrives
(the `shield` on each `enemy-hit` diagnostics line). It takes that reading to be the barrier before the hit.

- A Scholar's Adloquium (Galvanize) or a Sage's Eukrasian Diagnosis on the tank, then a buster: the Healing tab's
  striped part for the healer, against FF Logs' "absorbed" for the same pull (roughly; it's an estimate to about 1%
  of max HP a hit).
- Two barriers on one player from two healers (Galvanize and Haima): each healer gets some.
- A Seraph's or a fairy's barrier (Seraphic Veil, Consolation): credited to the Scholar.
- A raid-wide just after a party barrier (Succor, Panhaima): every member's hit shares the barrier as the game shows it.
- The opened-up Mitigation tab on a tank: the busters listed are the boss's busters (not auto-attacks), what's listed
  as up matches what was on (Rampart, Guardian, Reprisal on the boss), and "Mitigation up" looks right for a tank who
  held the boss the whole time.
- A raid-wide from an invisible helper: Reprisal, Addle and Feint on the boss still show as up for it.
