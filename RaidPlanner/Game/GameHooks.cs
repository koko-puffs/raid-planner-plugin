using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Network;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Network;
using RaidPlanner.Core;
using RaidPlanner.Core.Meter;
using SubKind = Dalamud.Game.ClientState.Objects.Enums.BattleNpcSubKind;

namespace RaidPlanner.Game;

/// <summary>
/// What the plugin needs from the game's network messages, through FFXIVClientStructs' own addresses
/// (kept current with each Dalamud release): an action landing (who it came from, where, which way it
/// faced, whom it hit and what it did to each: how a stack is told from a spread, how the many
/// mechanics with no cast bar are seen at all, and every number the meter and the combat block
/// have), the actor-control messages that put a head marker over someone (category 34, read after
/// the game has unscrambled it) and that carry a DoT's or HoT's tick (category 23), and a map effect
/// (the arena changing). The plugin only listens: each detour calls the game's own function first
/// and changes nothing. The hooks are on while a pull is recorded or followed, and while the meter
/// is on (<see cref="Set"/>). If one can't be made (the game changed), the rest still works and
/// <see cref="Problem"/> says what's missing.
///
/// An action's message is read into one buffer kept for it (<see cref="EffectBatch"/>: no allocation
/// per message), and most messages stop at the first question (the party's? landing on the party?).
/// HP is read only where it's wanted: a party member's (hits and heals on them), anyone the party
/// heals, a lifesteal's caster.
///
/// Since 0.4.0 also: a party member's item (the message's action type 2: a potion) for the parser only;
/// the header's global sequence and, for a ground-placed action, where it was put; a party target's
/// barrier and place read with their HP; and the actor-control message that says a cast was stopped
/// (category 15: cancelled, or interrupted). Every detour calls the game's function first and never
/// lets anything it does throw back into the game (and says what went wrong once in a while, not on every
/// message). With Settings → Recording → Diagnostics on, the raw values of what's read here that hasn't been
/// checked in the game are written to the diagnostics file (<see cref="DiagnosticsLog"/>): off, that's one bool asked.
/// </summary>
public sealed unsafe class GameHooks : IDisposable
{
    /// <summary>The actor-control category of a head marker (the game's "target icon").</summary>
    private const uint HeadMarker = 34;

    /// <summary>The actor-control category of a DoT's or HoT's tick (the game's "HP floating text").</summary>
    private const uint HotDot = 23;

    /// <summary>A tick's kind (the category's second argument): damage, or a heal.</summary>
    private const uint TickDamage = 3;

    private const uint TickHeal = 4;

    /// <summary>The actor-control category that says a cast stopped (BossMod's CancelCast; its second argument the action type, third the action, fourth 1 if interrupted).</summary>
    private const uint CancelCast = 15;

    /// <summary>An item (the effect's action type): a party member's potion.</summary>
    private const byte ActionTypeItem = 2;

    /// <summary>An action, not an item or a mount (the effect's action type).</summary>
    private const byte ActionTypeAction = 1;

    /// <summary>The id the game gives objects that exist only on your screen.</summary>
    private const uint InvalidId = 0xE0000000;

    private readonly FightRecorder recorder;
    private readonly DiagnosticsLog diag;
    private DateTime failedAt;
    private readonly Hook<ActionEffectHandler.Delegates.Receive>? effects;
    private readonly Hook<PacketDispatcher.Delegates.HandleActorControlPacket>? control;
    private readonly Hook<PacketDispatcher.Delegates.HandleMapEffectPacket>? mapEffects;

    /// <summary>
    /// The game's three handlers for several map effects at once (4, 8 or 12 in one message). Declared
    /// with four plain arguments and a result, all handed on untouched: the game's function gets exactly
    /// what it was called with (the first four arguments go in registers), even if a patch has changed
    /// what it takes, so a wrong guess about it can never break the game's own call. Only the second
    /// (the message) is read, and only while a pull is recorded.
    /// </summary>
    private delegate nint BatchedMapEffects(nint a1, nint a2, nint a3, nint a4);

    /// <summary>
    /// The batched handlers' code, as BossMod finds them (its WorldStateGameSync): three functions that
    /// begin alike. FFXIVClientStructs doesn't name them, so this is the plugin's one byte pattern; if
    /// it stops matching after a patch, only arena changes sent in batches are missed.
    /// </summary>
    private const string BatchedMapEffectPattern = "40 55 41 57 48 83 EC ?? 48 83 B9";

    /// <summary>
    /// Where each batched handler's states and indexes start in its message (BossMod's offsets), and so
    /// how many it holds: the high halves run from 2 to the low ones, two bytes each (4, 8 and 12).
    /// </summary>
    private static readonly (byte Low, byte Index)[] BatchedLayout = { (10, 18), (18, 34), (26, 50) };

    /// <summary>
    /// One batched handler's hook, its layout, and whether its messages are still read. The pattern is the plugin's
    /// only guess at where code is, so what's read is checked before it's read (<see cref="OnBatchedMapEffects"/>), and
    /// a handler whose messages don't look like map effects isn't read again this session (the game's own call goes on).
    /// </summary>
    private sealed class BatchedHandler(byte low, byte index)
    {
        public readonly byte Low = low;
        public readonly byte Index = index;

        /// <summary>The most map effects its message holds (4, 8 or 12).</summary>
        public int Most => (Low - 2) / 2;

        /// <summary>The bytes its message can take up: the count, the halves and the indexes.</summary>
        public int Extent => Index + Most;

        public Hook<BatchedMapEffects>? Hook;

        /// <summary>Given up on: its messages aren't read any more.</summary>
        public volatile bool Off;

        /// <summary>Messages whose count was more than it holds.</summary>
        public int OutOfRange;

        /// <summary>Messages read in the second that began at <see cref="SecondFrom"/> (Environment.TickCount64).</summary>
        public int ThisSecond;

        public long SecondFrom;
    }

    /// <summary>A batched handler's count past what it holds this many times: not the message it was taken for.</summary>
    private const int MostOutOfRange = 3;

    /// <summary>More batched map-effect messages than this in a second: not map effects (they come a few a pull).</summary>
    private const int MostASecond = 200;

    private readonly List<Hook<BatchedMapEffects>> batched = new();
    private readonly EffectBatch batch = new();
    private readonly uint[] targetIds = new uint[EffectBatch.MaxTargets];
    private bool enabled;

    /// <summary>The detours' own time since the frame's start (stopwatch ticks; the game's function's not counted): <see cref="TakeSpentMs"/>.</summary>
    private long spent;

    /// <summary>The detours' own time over the frame so far (ms), and start counting afresh (the recorder's frame asks, for <see cref="Stalls"/>).</summary>
    public double TakeSpentMs()
    {
        var ms = spent * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        spent = 0;
        return ms;
    }

    public GameHooks(FightRecorder recorder)
    {
        this.recorder = recorder;
        diag = recorder.Diagnostics;
        var missing = new List<string>();
        try
        {
            effectsAt = (nint)ActionEffectHandler.MemberFunctionPointers.Receive;
            effects = Services.GameInterop.HookFromAddress<ActionEffectHandler.Delegates.Receive>(effectsAt, OnEffect);
            // Dalamud follows a jump it finds at the function (one put there by something that isn't Dalamud: another program's
            // hook) and hooks where it leads. Said, since 0.7.10: when that other hook doesn't hand the game's call on, or is
            // taken away, the plugin hears no attacks at all (pulls with no hits, no damage, no numbers).
            if (effects.Address != effectsAt)
                Services.Log.Warning($"Raid Codex: the game's function for attacks ({effectsAt:X}) already jumps elsewhere ({effects.Address:X}), so something else hooked it first; if pulls come with no hits or damage, that is likely why");
        }
        catch (Exception ex)
        {
            missing.Add("where attacks land, whom they hit and the party's own numbers");
            Services.Log.Error(ex, "Raid Codex couldn't hook action effects");
        }
        try
        {
            control = Services.GameInterop.HookFromAddress<PacketDispatcher.Delegates.HandleActorControlPacket>(
                (nint)PacketDispatcher.MemberFunctionPointers.HandleActorControlPacket, OnActorControl);
        }
        catch (Exception ex)
        {
            missing.Add("head markers and damage-over-time ticks");
            Services.Log.Error(ex, "Raid Codex couldn't hook actor control");
        }
        try
        {
            mapEffects = Services.GameInterop.HookFromAddress<PacketDispatcher.Delegates.HandleMapEffectPacket>(
                (nint)PacketDispatcher.MemberFunctionPointers.HandleMapEffectPacket, OnMapEffect);
        }
        catch (Exception ex)
        {
            missing.Add("arena changes");
            Services.Log.Error(ex, "Raid Codex couldn't hook map effects");
        }
        try
        {
            var found = Services.SigScanner.ScanAllText(BatchedMapEffectPattern);
            if (found.Length == BatchedLayout.Length)
            {
                for (var i = 0; i < found.Length; i++)
                {
                    var handler = new BatchedHandler(BatchedLayout[i].Low, BatchedLayout[i].Index);
                    handler.Hook = Services.GameInterop.HookFromAddress<BatchedMapEffects>(found[i], (a1, a2, a3, a4) =>
                    {
                        var result = handler.Hook!.Original(a1, a2, a3, a4);
                        if (!handler.Off && recorder.Listening) OnBatchedMapEffects(handler, a2);
                        return result;
                    });
                    batched.Add(handler.Hook);
                }
            }
            else
            {
                Services.Log.Warning($"Raid Codex found {found.Length} batched map-effect handlers, not 3; arena changes sent in batches aren't recorded");
            }
        }
        catch (Exception ex)
        {
            Services.Log.Warning(ex, "Raid Codex couldn't hook batched map effects");
        }
        if (missing.Count > 0)
            Problem = $"This version of the game hides {string.Join(" and ", missing)} from the plugin, so pulls are recorded without them. An update to the plugin should bring them back.";
    }

    public string? Problem { get; }

    /// <summary>
    /// How many attacks the game has handed the hook (every one, before anything is read from it): the recorder says in
    /// /xllog when a whole pull went by with none (since 0.7.10, after pulls came through 0.7.9 without a single hit).
    /// </summary>
    public long EffectsSeen { get; private set; }

    /// <summary>Where FFXIVClientStructs says the game's function for attacks is (0 when it couldn't say).</summary>
    private readonly nint effectsAt;

    /// <summary>
    /// The attacks' hook as it is now, in words for /xllog (since 0.7.10, with a pull that had none): made or not, on or not,
    /// where it was put against where the game's function is, and how many attacks it has had since the plugin loaded.
    /// </summary>
    public string EffectsState()
    {
        if (effects == null) return "its hook couldn't be made";
        bool on;
        try
        {
            on = effects.IsEnabled;
        }
        catch (Exception)
        {
            on = false;
        }
        var where = effects.Address == effectsAt ? $"at {effectsAt:X}" : $"at {effects.Address:X}, the game's function at {effectsAt:X} jumping there";
        return $"its hook {(on ? "on" : "off")} {where}, {EffectsSeen} attacks since the plugin loaded";
    }

    /// <summary>The hooks on (a pull recorded or followed, or the meter on) or off; only a change does anything.</summary>
    public void Set(bool on)
    {
        if (on == enabled) return;
        enabled = on;
        // Each on its own (since 0.7.10): before, one that threw left those after it as they were for the session, as this
        // isn't asked again until the wish changes.
        Turn(effects, on);
        Turn(control, on);
        Turn(mapEffects, on);
        foreach (var h in batched) Turn(h, on);
    }

    private void Turn<T>(Hook<T>? hook, bool on) where T : Delegate
    {
        if (hook == null) return;
        try
        {
            if (on) hook.Enable();
            else hook.Disable();
        }
        catch (Exception ex)
        {
            Failed(ex, on ? "Raid Codex couldn't turn a hook on" : "Raid Codex couldn't turn a hook off");
        }
    }

    public void Enable() => Set(true);

    public void Disable() => Set(false);

    private void OnEffect(uint casterEntityId, Character* casterPtr, Vector3* targetPos, ActionEffectHandler.Header* header,
        ActionEffectHandler.TargetEffects* targetEffects, GameObjectId* targetEntityIds)
    {
        effects!.Original(casterEntityId, casterPtr, targetPos, header, targetEffects, targetEntityIds);
        EffectsSeen++;
        var started = Stalls.Start();
        try
        {
            if (header == null || header->ActionId == 0) return;
            var type = header->ActionType;
            if (diag.On && type != ActionTypeAction) DiagnoseType(casterEntityId, casterPtr, header);
            if (type != ActionTypeAction && type != ActionTypeItem) return;
            var count = targetEntityIds == null ? 0 : Math.Min((int)header->NumTargets, EffectBatch.MaxTargets);
            for (var i = 0; i < count; i++) targetIds[i] = (uint)targetEntityIds[i].ObjectId;
            var recording = recorder.Listening;
            var parser = recorder.Combat;
            var owner = OwnerOf(casterEntityId, casterPtr);
            var concerns = parser.Concerns(casterEntityId, owner, targetIds.AsSpan(0, count));
            // An item is only read as a party member's use (never an enemy's hit).
            if (type == ActionTypeItem && (!concerns || parser.MemberOf(casterEntityId, owner) == 0)) return;
            if (!concerns && !recording) return;

            batch.Begin(recorder.Now, casterEntityId, owner, header->ActionId, type);
            batch.Sequence = header->GlobalSequence;
            if (targetPos != null && float.IsFinite(targetPos->X) && float.IsFinite(targetPos->Z))
            {
                batch.HasGround = true;
                batch.GroundX = targetPos->X;
                batch.GroundZ = targetPos->Z;
            }
            var wantsCaster = false;
            for (var i = 0; i < count; i++)
            {
                var k = batch.Add(targetIds[i]);
                if (k < 0) break;
                var into = batch.EffectsOf(k);
                if (targetEffects != null)
                {
                    var from = targetEffects[i].Effects;
                    for (var j = 0; j < from.Length && j < into.Length; j++)
                    {
                        var e = from[j];
                        into[j] = new RawEffect(e.Type, e.Param0, e.Param1, e.Param2, e.Param3, e.Param4, e.Value);
                    }
                }
                if (concerns && parser.WantsTarget(targetIds[i], into)) ReadTarget(k, targetIds[i]);
                if (concerns && !wantsCaster) wantsCaster = CombatParser.WantsCaster(into);
            }
            if (wantsCaster)
            {
                var me = Look(casterEntityId);
                if (me != null)
                {
                    batch.CasterHp = ((Character*)me)->CharacterData.Health;
                    batch.CasterMaxHp = ((Character*)me)->CharacterData.MaxHealth;
                }
            }
            if (diag.On && concerns) DiagnoseEffects(targetPos);
            if (concerns) parser.Action(batch);

            if (!recording || type != ActionTypeAction) return;
            var at = targetPos != null ? *targetPos : Vector3.Zero;
            // The action's own direction, as the server sent it (0 to 65535 for -pi to pi, BossMod's
            // reading): not the caster's facing by the time the message is handled (a boss can turn
            // as soon as a cone is snapshotted, and a helper may be gone already).
            var rotation = header->RotationInt / 65535f * (2 * MathF.PI) - MathF.PI;
            recorder.OnHit(casterEntityId, casterPtr, at.X, at.Z, rotation, header->ActionId, batch);
        }
        catch (Exception ex)
        {
            Failed(ex, "Raid Codex couldn't read an action effect");
        }
        finally
        {
            spent += System.Diagnostics.Stopwatch.GetTimestamp() - started;
        }
    }

    /// <summary>What went wrong in a detour, logged once in a while (a message that can't be read is likely to come again and again).</summary>
    private void Failed(Exception ex, string what)
    {
        if (DateTime.UtcNow - failedAt < TimeSpan.FromSeconds(10)) return;
        failedAt = DateTime.UtcNow;
        Services.Log.Error(ex, what);
    }

    // ---------------------------------------------------------------------------------------------
    // Diagnostics (Settings → Recording → Diagnostics): the raw values, only while it's on.

    /// <summary>A party member's message that isn't an action (an item, [verify] type 2 and +1,000,000 for high quality): its type and id as sent.</summary>
    private void DiagnoseType(uint caster, Character* casterPtr, ActionEffectHandler.Header* header)
    {
        var parser = recorder.Combat;
        var owner = OwnerOf(caster, casterPtr);
        var member = parser.MemberOf(caster, owner);
        if (member == 0 || !diag.Room()) return;
        diag.Write("action-type", $"caster {caster:X8} member {member:X8} type {header->ActionType} id {header->ActionId} seq {header->GlobalSequence} targets {header->NumTargets}");
    }

    /// <summary>
    /// The message just read into the buffer: a party member's ground action's place as sent ([verify]), and each of their damage
    /// effects' bonus % (Param2) with its flags and Param4 (autos left out); or an enemy's hit on the party: every effect on each
    /// member as sent (knockbacks, Param4 &amp; 4 "partly absorbed" [verify]) with the barrier read.
    /// </summary>
    private void DiagnoseEffects(Vector3* targetPos)
    {
        var parser = recorder.Combat;
        var member = parser.MemberOf(batch.Caster, batch.CasterOwner);
        if (member != 0)
        {
            if (batch.ActionType != ActionTypeAction) return;
            // Each line only when it would be kept (DiagnosticsLog.Room): nothing is formatted for one that would be dropped.
            if (parser.IsArea(batch.Action) && diag.Room())
            {
                if (targetPos != null) diag.Write("ground", $"caster {batch.Caster:X8} action {batch.Action} at {targetPos->X:0.00} {targetPos->Y:0.00} {targetPos->Z:0.00}");
                else diag.Write("ground", $"caster {batch.Caster:X8} action {batch.Action} at none");
            }
            if (CombatLists.IsAutoAttack(parser.Category(batch.Action))) return;
            for (var i = 0; i < batch.Count; i++)
                foreach (var e in batch.EffectsOf(i))
                    if ((e.Type is Effects.Damage or Effects.BlockedDamage or Effects.ParriedDamage) && diag.Room())
                        diag.Write("party-hit", $"caster {batch.Caster:X8} action {batch.Action} target {batch.Targets[i]:X8} type {e.Type} p0 {e.Param0:X2} p1 {e.Param1:X2} bonus {unchecked((sbyte)e.Param2)} p3 {e.Param3:X2} p4 {e.Param4:X2} value {e.Value}");
            return;
        }
        for (var i = 0; i < batch.Count; i++)
        {
            if (!parser.IsParty(batch.Targets[i]) || !diag.Room()) continue;
            // One builder kept for it (the detours run on the game's thread), each effect appended the invariant way in place.
            var text = diagEffects.Clear();
            foreach (var e in batch.EffectsOf(i))
                if (e.Type != 0)
                    text.Append(CultureInfo.InvariantCulture, $" [{e.Type} {e.Param0:X2} {e.Param1:X2} {e.Param2:X2} {e.Param3:X2} {e.Param4:X2} {e.Value}]");
            diag.Write("enemy-hit", $"source {batch.Caster:X8} action {batch.Action} target {batch.Targets[i]:X8} hp {batch.Hp[i]} shield {batch.Shield[i]} effects{text}");
        }
    }

    /// <summary>An enemy hit's effects as text, for its diagnostics line (made again for each, never a new builder).</summary>
    private readonly System.Text.StringBuilder diagEffects = new(256);

    /// <summary>The game's own look-up of a character by entity id (its battle characters only), or null.</summary>
    private static BattleChara* Look(uint entityId)
    {
        if (entityId is 0 or InvalidId) return null;
        var manager = CharacterManager.Instance();
        return manager == null ? null : manager->LookupBattleCharaByEntityId(entityId);
    }

    /// <summary>Whose pet or summon the caster is (0 for none): from the caster the message came with, else looked up.</summary>
    private static uint OwnerOf(uint casterId, Character* caster)
    {
        if (caster == null) caster = (Character*)Look(casterId);
        var owner = caster != null ? caster->GameObject.OwnerId : 0;
        return owner is InvalidId ? 0 : owner;
    }

    /// <summary>
    /// A target's HP, max HP and whether it's an enemy (a combatant battle NPC no one owns that the game
    /// calls hostile: a FATE's escort or a quest's ally is a combatant too, and the party may heal it), into
    /// the buffer; since 0.4.0 its barrier (% of max HP) and where it stands.
    /// </summary>
    private void ReadTarget(int k, uint entityId)
    {
        var bc = Look(entityId);
        if (bc == null) return;
        var c = (Character*)bc;
        batch.Hp[k] = c->CharacterData.Health;
        batch.MaxHp[k] = c->CharacterData.MaxHealth;
        batch.Shield[k] = c->CharacterData.ShieldValue;
        batch.X[k] = c->GameObject.Position.X;
        batch.Z[k] = c->GameObject.Position.Z;
        batch.Hostile[k] = c->GameObject.ObjectKind == ObjectKind.BattleNpc
            && (SubKind)c->GameObject.SubKind is SubKind.Combatant or SubKind.BNpcPart
            && c->GameObject.OwnerId is 0 or InvalidId
            && c->CharacterData.IsHostile;
    }

    private void OnActorControl(uint entityId, uint category, uint arg1, uint arg2, uint arg3, uint arg4, uint arg5, uint arg6, uint arg7, uint arg8,
        GameObjectId targetId, bool isRecorded)
    {
        control!.Original(entityId, category, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, targetId, isRecorded);
        var started = Stalls.Start();
        try
        {
            // A cast stopped, as sent ([verify] arg2 the action type, arg3 the action, arg4 1 when interrupted).
            if (category == CancelCast && diag.Room())
                diag.Write("cancel-cast", $"actor {entityId:X8} {(recorder.Combat.IsParty(entityId) ? "party" : "other")} args {arg1} {arg2} {arg3} {arg4} {arg5} {arg6}");
            // A DoT's or HoT's tick as sent (since 0.7.10): a 0.7.8 recording of a 7-minute fight with three jobs' DoTs up had
            // not one tick row, so what (if anything) comes as category 23 wants seeing ([verify] the arguments' order).
            if (category == HotDot && diag.Room())
                diag.Write("hot-dot", $"actor {entityId:X8} {(recorder.Combat.IsParty(entityId) ? "party" : "other")} args {arg1} {arg2} {arg3} {arg4} {arg5} {arg6}");
            // A head marker: its icon, and ([verify], BossMod's reading) whom it points at.
            if (category == HeadMarker) recorder.OnMarker(entityId, arg1, arg2);
            else if (category == HotDot && arg2 is TickDamage or TickHeal) OnTick(entityId, arg1, arg2 == TickHeal, arg3, arg4, arg5);
            // A cast stopped: only an action's ([verify] that arg4 == 1 is "interrupted").
            else if (category == CancelCast && arg2 == ActionTypeAction && arg3 != 0) recorder.OnCancel(entityId, arg3, arg4 == 1);
        }
        catch (Exception ex)
        {
            Failed(ex, "Raid Codex couldn't read an actor control message");
        }
        finally
        {
            spent += System.Diagnostics.Stopwatch.GetTimestamp() - started;
        }
    }

    /// <summary>
    /// A DoT's or HoT's tick: the target, the status (often 0), a heal or damage, the amount and the
    /// source. The arguments' order is FFXIVClientStructs' <c>StatusManager.ProcessHotDot(target,
    /// statusId, tickMode, value, sourceEntityId, damageType)</c>; should the source and the damage
    /// type ever come the other way round (a small number where an entity id should be), they're
    /// taken that way. [verify in game]
    /// </summary>
    private void OnTick(uint target, uint status, bool heal, uint amount, uint arg4, uint arg5)
    {
        var parser = recorder.Combat;
        if (!parser.Listening) return;
        var source = arg4 < 0x100 && arg5 >= 0x100 ? arg5 : arg4;
        if (source == InvalidId) source = 0;
        long hp = -1, max = -1;
        if (heal || parser.IsParty(target))
        {
            var bc = Look(target);
            if (bc != null)
            {
                hp = ((Character*)bc)->CharacterData.Health;
                max = ((Character*)bc)->CharacterData.MaxHealth;
            }
        }
        parser.Tick(recorder.Now, target, status, heal, amount, source, parser.IsParty(source) ? 0 : OwnerOf(source, null), hp, max);
    }

    /// <summary>
    /// A map effect: its index, and its state as one number (the packet's state in the low half,
    /// its second half above it: the "flags" ACT logs and BossMod's modules compare against).
    /// </summary>
    private void OnMapEffect(MapEffectPacket* packet)
    {
        mapEffects!.Original(packet);
        try
        {
            if (packet != null) recorder.OnMapEffect(packet->Index, packet->State | ((uint)packet->TimelineIndex << 16));
        }
        catch (Exception ex)
        {
            Failed(ex, "Raid Codex couldn't read a map effect");
        }
    }

    /// <summary>
    /// Several map effects at once: their count first, then each one's state halves and index (BossMod's reading). The
    /// handler was found by a byte pattern, so before anything's read: called far more often than map effects come, or a
    /// message that isn't readable memory (<see cref="MemoryCheck"/>: a wrong pointer would crash the game, uncatchably),
    /// and the handler isn't the one it was taken for; nor is it after a few counts past what its message holds. Such a
    /// handler is given up on for the session (logged once); the game's own call is never touched.
    /// </summary>
    private void OnBatchedMapEffects(BatchedHandler h, nint message)
    {
        try
        {
            if (message == 0) return;
            var now = Environment.TickCount64;
            if (now - h.SecondFrom >= 1000)
            {
                h.SecondFrom = now;
                h.ThisSecond = 0;
            }
            if (++h.ThisSecond > MostASecond)
            {
                GiveUp(h, $"called more than {MostASecond} times in a second");
                return;
            }
            if (!MemoryCheck.CanRead(message, h.Extent))
            {
                GiveUp(h, $"its message at {message:X} isn't readable memory");
                return;
            }
            var data = (byte*)message;
            // More than the message holds: not the message this was taken for (the game changed); nothing's read.
            var count = (int)*data;
            if (count > h.Most)
            {
                if (++h.OutOfRange >= MostOutOfRange) GiveUp(h, $"its message said {count} map effects, more than the {h.Most} it holds");
                return;
            }
            for (var i = 0; i < count; i++)
            {
                var low = *(ushort*)(data + 2 * i + h.Low);
                var high = *(ushort*)(data + 2 * i + 2);
                recorder.OnMapEffect(data[i + h.Index], low | ((uint)high << 16));
            }
        }
        catch (Exception ex)
        {
            Failed(ex, "Raid Codex couldn't read batched map effects");
        }
    }

    /// <summary>A batched handler that isn't what the pattern took it for: not read again this session (said once).</summary>
    private static void GiveUp(BatchedHandler h, string why)
    {
        if (h.Off) return;
        h.Off = true;
        Services.Log.Warning($"Raid Codex stopped reading a batched map-effect handler ({why}): the game has probably changed, so arena changes sent in batches aren't recorded");
    }

    /// <summary>Every hook let go of, each whatever happens to the others (one left in the game would call into an unloaded plugin).</summary>
    public void Dispose()
    {
        enabled = false;
        Release(effects);
        Release(control);
        Release(mapEffects);
        foreach (var h in batched) Release(h);
    }

    private static void Release(IDisposable? hook)
    {
        try
        {
            hook?.Dispose();
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Raid Codex couldn't let go of a hook");
        }
    }
}
