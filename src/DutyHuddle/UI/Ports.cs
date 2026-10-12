using DutyHuddle.Domain.Models;
using DutyHuddle.Sync;

namespace DutyHuddle.UI;

/// <summary>What the pages need from the sharing services.</summary>
public interface ISharing
{
    SharingState State { get; }

    string Status { get; }

    bool HasServer { get; }

    string? BuildShareMessage(DateTime nowUtc);

    void ForgetReceived();
}

/// <summary>Opens the Duty Finder; only ever called from a click.</summary>
public interface IDutyFinder
{
    void OpenDuty(uint contentFinderConditionId);

    void OpenRoulette(uint rouletteId);
}

public interface IRouletteHistory
{
    IReadOnlyList<(DateOnly Day, IReadOnlySet<uint> Roulettes)> Recent(MemberId member, int count);
}
