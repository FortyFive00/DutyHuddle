using System.Numerics;

namespace DutyHuddle.Domain.Models;

/// <summary>Identifies a character by name and home world, the identity every party member can see.</summary>
public readonly record struct MemberId(string Name, ushort HomeWorldId)
{
    /// <summary>Stable key used for slots and lookups. Never contains account or content IDs.</summary>
    public string Key => $"{Name}@{HomeWorldId}";

    public override string ToString() => Key;
}

public enum Availability : byte
{
    Unknown = 0,
    Available = 1,
    Completed = 2,
    /// <summary>Not unlocked or not eligible (for example the Mentor roulette for non-mentors).</summary>
    Locked = 3,
}

/// <summary>Mirrors <c>PlayerState.WeeklyBingoTaskStatus</c>.</summary>
public enum WtTaskStatus : byte
{
    Open = 0,
    Claimable = 1,
    Claimed = 2,
}

public enum SnapshotSource : byte
{
    Local = 0,
    Relay = 1,
    ChatCode = 2,
    Manual = 3,
}

/// <summary>State of one Wondrous Tails journal.</summary>
public sealed record WtBoard
{
    public const int CellCount = 16;
    public const int SealsToComplete = 9;

    /// <summary>16 row IDs of the WeeklyBingoOrderData sheet, one per objective.</summary>
    public required IReadOnlyList<byte> OrderIds { get; init; }

    /// <summary>16 objective states, same order as <see cref="OrderIds"/>.</summary>
    public required IReadOnlyList<WtTaskStatus> Status { get; init; }

    /// <summary>Bit i set = a seal is placed on cell i (row-major 4x4 grid).</summary>
    public required ushort StickerMask { get; init; }

    public required byte SecondChancePoints { get; init; }

    public required DateTime ExpiresUtc { get; init; }

    public int PlacedSeals => BitOperations.PopCount(StickerMask);

    public int ClaimableCount => Status.Count(s => s == WtTaskStatus.Claimable);

    public int OpenCount => Status.Count(s => s == WtTaskStatus.Open);

    public bool IsExpired(DateTime nowUtc) => nowUtc >= ExpiresUtc;

    public bool IsComplete => PlacedSeals >= SealsToComplete;
}

/// <summary>Everything one member shares: today's roulettes and the current journal.</summary>
public sealed record MemberSnapshot(
    MemberId Member,
    DateTime CapturedUtc,
    DateOnly DailyEpoch,
    IReadOnlyDictionary<uint, Availability> Roulettes,
    WtBoard? WondrousTails,
    SnapshotSource Source)
{
    /// <summary>
    /// Roulette state as of <paramref name="currentEpoch"/>. A snapshot from an earlier daily epoch
    /// still tells us something: the reset made every completed roulette available again.
    /// </summary>
    public Availability GetRoulette(uint rouletteId, DateOnly currentEpoch)
    {
        if (!Roulettes.TryGetValue(rouletteId, out var state))
            return Availability.Unknown;

        if (DailyEpoch < currentEpoch && state == Availability.Completed)
            return Availability.Available;

        return state;
    }

    /// <summary>The journal, or null if it is missing or expired.</summary>
    public WtBoard? GetBoard(DateTime nowUtc)
        => WondrousTails is { } board && !board.IsExpired(nowUtc) ? board : null;
}
