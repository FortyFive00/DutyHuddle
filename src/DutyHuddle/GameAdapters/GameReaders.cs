using Dalamud.Plugin.Services;
using DutyHuddle.Domain.Duty;
using DutyHuddle.Domain.Models;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;

namespace DutyHuddle.GameAdapters;

/// <summary>A party member as read from the client. The content ID never leaves this process.</summary>
public sealed record PartyMemberInfo(MemberId Id, ulong ContentId, bool IsLocal, uint ClassJobId = 0);

/// <summary>Reads today's roulette completion for the local player. Call on the framework thread.</summary>
public sealed unsafe class RouletteReader
{
    public Dictionary<uint, Availability> Read(DutyCatalog catalog)
    {
        var result = new Dictionary<uint, Availability>();
        var instance = InstanceContent.Instance();
        foreach (var roulette in catalog.Roulettes)
        {
            if (instance is null || roulette.Id > byte.MaxValue)
            {
                result[roulette.Id] = Availability.Unknown;
                continue;
            }

            if (!HasUnlockedDuty(catalog, roulette))
            {
                result[roulette.Id] = Availability.Locked;
                continue;
            }

            result[roulette.Id] = instance->IsRouletteComplete((byte)roulette.Id)
                ? Availability.Completed
                : Availability.Available;
        }

        return result;
    }

    /// <summary>
    /// A roulette only appears in the Duty Finder once at least one of its duties is unlocked
    /// (Expert and Level Cap need the latest main scenario, Alliance Raids an alliance raid, and so on).
    /// Pools without instance content (PvP) count as unlocked.
    /// </summary>
    private static bool HasUnlockedDuty(DutyCatalog catalog, RouletteInfo roulette)
    {
        var anyInstance = false;
        foreach (var duty in catalog.GetPool(roulette))
        {
            if (duty.InstanceContentId == 0)
                continue;

            anyInstance = true;
            if (UIState.IsInstanceContentUnlocked(duty.InstanceContentId))
                return true;
        }

        return !anyInstance;
    }
}

/// <summary>Reads the Wondrous Tails journal from PlayerState. Call on the framework thread.</summary>
public sealed unsafe class WondrousTailsReader
{
    public WtBoard? Read()
    {
        var state = PlayerState.Instance();
        if (state is null || !state->HasWeeklyBingoJournal || state->IsWeeklyBingoExpired())
            return null;

        var orders = new byte[WtBoard.CellCount];
        var status = new WtTaskStatus[WtBoard.CellCount];
        ushort stickers = 0;
        for (var i = 0; i < WtBoard.CellCount; i++)
        {
            orders[i] = state->WeeklyBingoOrderData[i];
            status[i] = (WtTaskStatus)(byte)state->GetWeeklyBingoTaskStatus(i);

            // Index base to be confirmed in-game (the struct comment says 1, VanillaPlus uses 0-15).
            if (state->IsWeeklyBingoStickerPlaced(i))
                stickers |= (ushort)(1 << i);
        }

        return new WtBoard
        {
            OrderIds = orders,
            Status = status,
            StickerMask = stickers,
            SecondChancePoints = (byte)Math.Min(9u, state->WeeklyBingoNumSecondChancePoints),
            ExpiresUtc = state->WeeklyBingoExpireDateTime,
        };
    }
}

/// <summary>Lists the light party: the normal group, or the cross-world group when in one.</summary>
public sealed unsafe class PartyScanner
{
    private readonly IPlayerState player;
    private readonly IPartyList partyList;

    public PartyScanner(IPlayerState player, IPartyList partyList)
    {
        this.player = player;
        this.partyList = partyList;
    }

    public MemberId? LocalId => player.IsLoaded
        ? new MemberId(player.CharacterName, (ushort)player.HomeWorld.RowId)
        : null;

    public IReadOnlyList<PartyMemberInfo> Read()
    {
        if (!player.IsLoaded)
            return [];

        var local = new PartyMemberInfo(LocalId!.Value, player.ContentId, true, player.ClassJob.RowId);
        var members = ReadCrossWorld() ?? ReadNormal();

        var result = new List<PartyMemberInfo> { local };
        foreach (var member in members)
            if (member.ContentId != local.ContentId && result.All(m => m.Id != member.Id))
                result.Add(member);

        return result;
    }

    private List<PartyMemberInfo>? ReadCrossWorld()
    {
        var proxy = InfoProxyCrossRealm.Instance();
        if (proxy is null || !proxy->IsInCrossRealmParty || proxy->IsInAllianceRaid)
            return null;

        var index = proxy->LocalPlayerGroupIndex;
        if (index >= proxy->GroupCount)
            return null;

        var group = proxy->CrossRealmGroups[index];
        var result = new List<PartyMemberInfo>(group.GroupMemberCount);
        for (var i = 0; i < group.GroupMemberCount; i++)
        {
            var member = group.GroupMembers[i];
            if (member.ContentId == 0)
                continue;

            result.Add(new PartyMemberInfo(new MemberId(member.NameString, (ushort)member.HomeWorld), member.ContentId, false, member.ClassJobId));
        }

        return result;
    }

    private List<PartyMemberInfo> ReadNormal()
    {
        var result = new List<PartyMemberInfo>(partyList.Length);
        if (partyList.IsAlliance)
            return result;

        for (var i = 0; i < partyList.Length; i++)
        {
            var member = partyList[i];
            if (member is null || member.ContentId == 0)
                continue;

            result.Add(new PartyMemberInfo(new MemberId(member.Name.TextValue, (ushort)member.World.RowId), member.ContentId, false, member.ClassJob.RowId));
        }

        return result;
    }
}

/// <summary>Opens the Duty Finder on a duty or roulette. Only ever triggered by a click.</summary>
public sealed unsafe class DutyFinderLauncher : DutyHuddle.UI.IDutyFinder
{
    private readonly IFramework framework;

    public DutyFinderLauncher(IFramework framework)
    {
        this.framework = framework;
    }

    public void OpenDuty(uint contentFinderConditionId)
        => framework.RunOnFrameworkThread(() =>
        {
            var agent = AgentContentsFinder.Instance();
            if (agent is not null)
                agent->OpenRegularDuty(contentFinderConditionId, false);
        });

    public void OpenRoulette(uint rouletteId)
        => framework.RunOnFrameworkThread(() =>
        {
            var agent = AgentContentsFinder.Instance();
            if (agent is not null && rouletteId <= byte.MaxValue)
                agent->OpenRouletteDuty((byte)rouletteId, false);
        });
}
