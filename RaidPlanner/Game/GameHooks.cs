using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Network;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Network;
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

    /// <summary>An action, not an item or a mount (the effect's action type).</summary>
    private const byte ActionTypeAction = 1;

    /// <summary>The id the game gives objects that exist only on your screen.</summary>
    private const uint InvalidId = 0xE0000000;

    private readonly FightRecorder recorder;
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

    private readonly List<Hook<BatchedMapEffects>> batched = new();
    private readonly EffectBatch batch = new();
    private readonly uint[] targetIds = new uint[EffectBatch.MaxTargets];
    private bool enabled;

    public GameHooks(FightRecorder recorder)
    {
        this.recorder = recorder;
        var missing = new List<string>();
        try
        {
            effects = Services.GameInterop.HookFromAddress<ActionEffectHandler.Delegates.Receive>(
                (nint)ActionEffectHandler.MemberFunctionPointers.Receive, OnEffect);
        }
        catch (Exception ex)
        {
            missing.Add("where attacks land, whom they hit and the party's own numbers");
            Services.Log.Error(ex, "Raid Planner couldn't hook action effects");
        }
        try
        {
            control = Services.GameInterop.HookFromAddress<PacketDispatcher.Delegates.HandleActorControlPacket>(
                (nint)PacketDispatcher.MemberFunctionPointers.HandleActorControlPacket, OnActorControl);
        }
        catch (Exception ex)
        {
            missing.Add("head markers and damage-over-time ticks");
            Services.Log.Error(ex, "Raid Planner couldn't hook actor control");
        }
        try
        {
            mapEffects = Services.GameInterop.HookFromAddress<PacketDispatcher.Delegates.HandleMapEffectPacket>(
                (nint)PacketDispatcher.MemberFunctionPointers.HandleMapEffectPacket, OnMapEffect);
        }
        catch (Exception ex)
        {
            missing.Add("arena changes");
            Services.Log.Error(ex, "Raid Planner couldn't hook map effects");
        }
        try
        {
            var found = Services.SigScanner.ScanAllText(BatchedMapEffectPattern);
            if (found.Length == BatchedLayout.Length)
            {
                for (var i = 0; i < found.Length; i++)
                {
                    var layout = BatchedLayout[i];
                    Hook<BatchedMapEffects>? hook = null;
                    hook = Services.GameInterop.HookFromAddress<BatchedMapEffects>(found[i], (a1, a2, a3, a4) =>
                    {
                        var result = hook!.Original(a1, a2, a3, a4);
                        if (recorder.Listening) OnBatchedMapEffects((byte*)a2, layout.Low, layout.Index);
                        return result;
                    });
                    batched.Add(hook);
                }
            }
            else
            {
                Services.Log.Warning($"Raid Planner found {found.Length} batched map-effect handlers, not 3; arena changes sent in batches aren't recorded");
            }
        }
        catch (Exception ex)
        {
            Services.Log.Warning(ex, "Raid Planner couldn't hook batched map effects");
        }
        if (missing.Count > 0)
            Problem = $"This version of the game hides {string.Join(" and ", missing)} from the plugin, so pulls are recorded without them. An update to the plugin should bring them back.";
    }

    public string? Problem { get; }

    /// <summary>The hooks on (a pull recorded or followed, or the meter on) or off; only a change does anything.</summary>
    public void Set(bool on)
    {
        if (on == enabled) return;
        enabled = on;
        if (on)
        {
            effects?.Enable();
            control?.Enable();
            mapEffects?.Enable();
            foreach (var h in batched) h.Enable();
        }
        else
        {
            effects?.Disable();
            control?.Disable();
            mapEffects?.Disable();
            foreach (var h in batched) h.Disable();
        }
    }

    public void Enable() => Set(true);

    public void Disable() => Set(false);

    private void OnEffect(uint casterEntityId, Character* casterPtr, Vector3* targetPos, ActionEffectHandler.Header* header,
        ActionEffectHandler.TargetEffects* targetEffects, GameObjectId* targetEntityIds)
    {
        effects!.Original(casterEntityId, casterPtr, targetPos, header, targetEffects, targetEntityIds);
        try
        {
            if (header == null || header->ActionType != ActionTypeAction || header->ActionId == 0) return;
            var count = targetEntityIds == null ? 0 : Math.Min((int)header->NumTargets, EffectBatch.MaxTargets);
            for (var i = 0; i < count; i++) targetIds[i] = (uint)targetEntityIds[i].ObjectId;
            var recording = recorder.Listening;
            var parser = recorder.Combat;
            var owner = OwnerOf(casterEntityId, casterPtr);
            var concerns = parser.Concerns(casterEntityId, owner, targetIds.AsSpan(0, count));
            if (!concerns && !recording) return;

            batch.Begin(recorder.Now, casterEntityId, owner, header->ActionId, header->ActionType);
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
            if (concerns) parser.Action(batch);

            if (!recording) return;
            var at = targetPos != null ? *targetPos : Vector3.Zero;
            // The action's own direction, as the server sent it (0 to 65535 for -pi to pi, BossMod's
            // reading): not the caster's facing by the time the message is handled (a boss can turn
            // as soon as a cone is snapshotted, and a helper may be gone already).
            var rotation = header->RotationInt / 65535f * (2 * MathF.PI) - MathF.PI;
            recorder.OnHit(casterEntityId, casterPtr, at.X, at.Z, rotation, header->ActionId, batch);
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Raid Planner couldn't read an action effect");
        }
    }

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

    /// <summary>A target's HP, max HP and whether it's an enemy (a combatant battle NPC no one owns), into the buffer.</summary>
    private void ReadTarget(int k, uint entityId)
    {
        var bc = Look(entityId);
        if (bc == null) return;
        var c = (Character*)bc;
        batch.Hp[k] = c->CharacterData.Health;
        batch.MaxHp[k] = c->CharacterData.MaxHealth;
        batch.Hostile[k] = c->GameObject.ObjectKind == ObjectKind.BattleNpc
            && (SubKind)c->GameObject.SubKind is SubKind.Combatant or SubKind.BNpcPart
            && c->GameObject.OwnerId is 0 or InvalidId;
    }

    private void OnActorControl(uint entityId, uint category, uint arg1, uint arg2, uint arg3, uint arg4, uint arg5, uint arg6, uint arg7, uint arg8,
        GameObjectId targetId, bool isRecorded)
    {
        control!.Original(entityId, category, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, targetId, isRecorded);
        try
        {
            if (category == HeadMarker) recorder.OnMarker(entityId, arg1);
            else if (category == HotDot && arg2 is TickDamage or TickHeal) OnTick(entityId, arg1, arg2 == TickHeal, arg3, arg4, arg5);
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Raid Planner couldn't read an actor control message");
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
            Services.Log.Error(ex, "Raid Planner couldn't read a map effect");
        }
    }

    /// <summary>Several map effects at once: their count first, then each one's state halves and index (BossMod's reading).</summary>
    private void OnBatchedMapEffects(byte* data, byte offLow, byte offIndex)
    {
        try
        {
            if (data == null) return;
            // More than the message holds: not the message this was taken for (the game changed); nothing's read.
            var count = (int)*data;
            if (count > (offLow - 2) / 2) return;
            for (var i = 0; i < count; i++)
            {
                var low = *(ushort*)(data + 2 * i + offLow);
                var high = *(ushort*)(data + 2 * i + 2);
                recorder.OnMapEffect(data[i + offIndex], low | ((uint)high << 16));
            }
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Raid Planner couldn't read batched map effects");
        }
    }

    public void Dispose()
    {
        effects?.Dispose();
        control?.Dispose();
        mapEffects?.Dispose();
        foreach (var h in batched) h.Dispose();
    }
}
