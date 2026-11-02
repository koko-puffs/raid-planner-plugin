using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Network;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Network;

namespace RaidPlanner.Game;

/// <summary>
/// The three things a recording needs from the game's network messages, through FFXIVClientStructs'
/// own addresses (kept current with each Dalamud release): an action landing (who it came from,
/// where, and whom it hit: how a stack is told from a spread, and how the many mechanics with no
/// cast bar are seen at all), the actor-control message that puts a head marker over someone (read
/// after the game has unscrambled it), and a map effect (the arena changing).
/// The plugin only listens: each detour calls the game's own function first and changes nothing.
/// The hooks are on only while a pull is recorded. If one can't be made (the game changed), the
/// rest of the recording still works and <see cref="Problem"/> says what's missing.
/// </summary>
public sealed unsafe class GameHooks : IDisposable
{
    /// <summary>The actor-control category of a head marker (the game's "target icon").</summary>
    private const uint HeadMarker = 34;

    /// <summary>An action, not an item or a mount (the effect's action type).</summary>
    private const byte ActionTypeAction = 1;

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
    private readonly List<uint> targetBuffer = new(32);

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
            missing.Add("where attacks land and whom they hit");
            Services.Log.Error(ex, "Raid Planner couldn't hook action effects");
        }
        try
        {
            control = Services.GameInterop.HookFromAddress<PacketDispatcher.Delegates.HandleActorControlPacket>(
                (nint)PacketDispatcher.MemberFunctionPointers.HandleActorControlPacket, OnActorControl);
        }
        catch (Exception ex)
        {
            missing.Add("head markers");
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

    public void Enable()
    {
        effects?.Enable();
        control?.Enable();
        mapEffects?.Enable();
        foreach (var h in batched) h.Enable();
    }

    public void Disable()
    {
        effects?.Disable();
        control?.Disable();
        mapEffects?.Disable();
        foreach (var h in batched) h.Disable();
    }

    private void OnEffect(uint casterEntityId, Character* casterPtr, Vector3* targetPos, ActionEffectHandler.Header* header,
        ActionEffectHandler.TargetEffects* targetEffects, GameObjectId* targetEntityIds)
    {
        effects!.Original(casterEntityId, casterPtr, targetPos, header, targetEffects, targetEntityIds);
        try
        {
            if (header == null || header->ActionType != ActionTypeAction || header->ActionId == 0) return;
            targetBuffer.Clear();
            if (targetEntityIds != null)
                for (var i = 0; i < header->NumTargets && i < 32; i++)
                    targetBuffer.Add(targetEntityIds[i].ObjectId);
            var at = targetPos != null ? *targetPos : Vector3.Zero;
            recorder.OnHit(casterEntityId, casterPtr, at.X, at.Z, header->ActionId, targetBuffer);
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Raid Planner couldn't read an action effect");
        }
    }

    private void OnActorControl(uint entityId, uint category, uint arg1, uint arg2, uint arg3, uint arg4, uint arg5, uint arg6, uint arg7, uint arg8,
        GameObjectId targetId, bool isRecorded)
    {
        control!.Original(entityId, category, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, targetId, isRecorded);
        try
        {
            if (category == HeadMarker) recorder.OnMarker(entityId, arg1);
        }
        catch (Exception ex)
        {
            Services.Log.Error(ex, "Raid Planner couldn't read a head marker");
        }
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
