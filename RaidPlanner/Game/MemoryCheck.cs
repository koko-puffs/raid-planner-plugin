using System;
using System.Runtime.InteropServices;

namespace RaidPlanner.Game;

/// <summary>
/// Can a few bytes at an address the game handed over be read without crashing the game? For the one hook found by a
/// byte pattern (<see cref="GameHooks"/>' batched map effects), whose argument is taken to be a pointer: if a patch moved
/// the pattern onto other functions, reading a wrong one would be an access violation, which .NET can't catch (the game
/// would close). The address must be a plausible user-mode one, and Windows (or Wine) must say every page the bytes are
/// on is committed and readable (VirtualQuery). Should VirtualQuery itself be missing, only the address is checked.
/// </summary>
internal static unsafe class MemoryCheck
{
    /// <summary>Below this nothing is ever mapped (Windows keeps the first 64 KB free).</summary>
    private const ulong Lowest = 0x10000;

    /// <summary>The highest user-mode address on 64-bit Windows.</summary>
    private const ulong Highest = 0x7FFF_FFFE_FFFF;

    private const uint MemCommit = 0x1000;
    private const uint PageNoAccess = 0x01;
    private const uint PageGuard = 0x100;

    /// <summary>Readable protections: read-only, read-write, write-copy and their executable kinds.</summary>
    private const uint Readable = 0x02 | 0x04 | 0x08 | 0x20 | 0x40 | 0x80;

    private static bool canQuery = OperatingSystem.IsWindows();

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryInfo
    {
        public nuint BaseAddress;
        public nuint AllocationBase;
        public uint AllocationProtect;
        public ushort PartitionId;
        public nuint RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern nuint VirtualQuery(nuint address, MemoryInfo* buffer, nuint length);

    /// <summary>
    /// Can <paramref name="length"/> bytes at <paramref name="address"/> be read? Never throws. Called once a message of a
    /// rare kind (map effects), so it asks Windows each time rather than remember an answer that could go stale.
    /// </summary>
    public static bool CanRead(nint address, int length)
    {
        var start = (ulong)address;
        if (length <= 0 || start < Lowest || start > Highest || Highest - start < (ulong)length) return false;
        if (!canQuery) return true;
        try
        {
            if (!Committed(start, out var regionEnd)) return false;
            var last = start + (ulong)length - 1;
            // The bytes run on past the region (a page or two at most): the next must be readable too.
            return last < regionEnd || Committed(last, out _);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            canQuery = false;
            Services.Log.Warning(ex, "Raid Codex can't ask Windows what memory is readable; batched map effects are checked by address only");
            return true;
        }
    }

    /// <summary>Is the page at <paramref name="at"/> committed and readable (and where does its region end)?</summary>
    private static bool Committed(ulong at, out ulong regionEnd)
    {
        regionEnd = 0;
        MemoryInfo info;
        if (VirtualQuery((nuint)at, &info, (nuint)sizeof(MemoryInfo)) == 0) return false;
        regionEnd = (ulong)info.BaseAddress + (ulong)info.RegionSize;
        return info.State == MemCommit && (info.Protect & Readable) != 0 && (info.Protect & (PageGuard | PageNoAccess)) == 0;
    }
}
