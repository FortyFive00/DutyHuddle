using DutyHuddle.Domain.Duty;

namespace DutyHuddle.Domain.Tests;

/// <summary>A small hand-built catalog with the shapes of the real sheets.</summary>
internal static class TestCatalog
{
    public const uint Sastasha = 4;
    public const uint TamTara = 2;
    public const uint CastrumMeridianum = 15;
    public const uint DeadEnds = 783;
    public const uint Aetherfont = 822;
    public const uint Endsinger = 846;
    public const uint CopiedFactory = 676;
    public const uint PuppetsBunker = 713;
    public const uint Frontline = 599;
    public const uint PalaceOfTheDead = 174;
    public const uint P12N = 942;

    public const byte OrderDeadEnds = 200;
    public const byte OrderDungeons1To49 = 1;
    public const byte OrderDungeons90 = 109;
    public const byte OrderDungeons50 = 2;
    public const byte OrderFrontline = 54;
    public const byte OrderDeepDungeons = 53;
    public const byte OrderShbAlliance = 210;
    public const byte OrderEndsinger = 211;
    public const byte OrderP12N = 212;
    public const byte OrderTrials70To100 = 173;

    public static DutyCatalog Build()
    {
        DutyInfo[] duties =
        [
            new() { Id = Sastasha, Name = "Sastasha", Level = 15, ContentTypeId = ContentTypeIds.Dungeon, InstanceContentId = 1, Roulettes = RoulettePool.Leveling, ExpansionId = 0 },
            new() { Id = TamTara, Name = "The Tam-Tara Deepcroft", Level = 16, ContentTypeId = ContentTypeIds.Dungeon, InstanceContentId = 2, Roulettes = RoulettePool.Leveling, ExpansionId = 0 },
            new() { Id = CastrumMeridianum, Name = "Castrum Meridianum", Level = 50, ContentTypeId = ContentTypeIds.Dungeon, InstanceContentId = 15, Roulettes = RoulettePool.MainScenario, ExpansionId = 0 },
            new() { Id = DeadEnds, Name = "The Dead Ends", Level = 90, ContentTypeId = ContentTypeIds.Dungeon, InstanceContentId = 783, Roulettes = RoulettePool.HighLevel, ExpansionId = 4 },
            new() { Id = Aetherfont, Name = "The Aetherfont", Level = 90, ContentTypeId = ContentTypeIds.Dungeon, InstanceContentId = 822, Roulettes = RoulettePool.HighLevel, ExpansionId = 4 },
            new() { Id = Endsinger, Name = "The Minstrel's Ballad: Endsinger's Aria", Level = 90, ContentTypeId = ContentTypeIds.Trial, InstanceContentId = 846, ExpansionId = 4 },
            new() { Id = CopiedFactory, Name = "The Copied Factory", Level = 80, ContentTypeId = ContentTypeIds.Raid, InstanceContentId = 676, Roulettes = RoulettePool.Alliance, ExpansionId = 3 },
            new() { Id = PuppetsBunker, Name = "The Puppets' Bunker", Level = 80, ContentTypeId = ContentTypeIds.Raid, InstanceContentId = 713, Roulettes = RoulettePool.Alliance, ExpansionId = 3 },
            new() { Id = Frontline, Name = "Frontline: Seal Rock", Level = 30, ContentTypeId = ContentTypeIds.PvP, MemberTypeId = 7, Roulettes = RoulettePool.Frontline, ExpansionId = 0 },
            new() { Id = PalaceOfTheDead, Name = "The Palace of the Dead", Level = 1, ContentTypeId = ContentTypeIds.DeepDungeon, ExpansionId = 1 },
            new() { Id = P12N, Name = "Anabaseios: The Twelfth Circle", Level = 90, ContentTypeId = ContentTypeIds.Raid, InstanceContentId = 942, Roulettes = RoulettePool.NormalRaid, ExpansionId = 4 },
        ];

        RouletteInfo[] roulettes =
        [
            new() { Id = 1, Name = "Duty Roulette: Leveling", MembersPerParty = 4, Pool = RoulettePool.Leveling, SortKey = 1 },
            new() { Id = 2, Name = "Duty Roulette: High-level Dungeons", MembersPerParty = 4, Pool = RoulettePool.HighLevel, SortKey = 2 },
            new() { Id = 6, Name = "Duty Roulette: Trials", MembersPerParty = 8, Pool = RoulettePool.Trial, SortKey = 5 },
            new() { Id = 7, Name = "Daily Challenge: Frontline", MembersPerParty = 4, Pool = RoulettePool.Frontline, IsPvP = true, SortKey = 6 },
            new() { Id = 9, Name = "Duty Roulette: Mentor", MembersPerParty = 1, Pool = RoulettePool.Mentor, IsMentorOnly = true, SortKey = 9 },
            new() { Id = 15, Name = "Duty Roulette: Alliance Raids", MembersPerParty = 8, Pool = RoulettePool.Alliance, SortKey = 10 },
        ];

        WtOrderInfo[] orders =
        [
            new() { Id = OrderDeadEnds, Type = WtObjectiveRules.SpecificDuty, Data = 783, Text = "Dammies" },
            new() { Id = OrderDungeons1To49, Type = WtObjectiveRules.LevelingDungeons, Data = 49, MinLevel = 1, Text = "Dungeons (Lv. 1-49)" },
            new() { Id = OrderDungeons90, Type = WtObjectiveRules.DungeonExactLevel, Data = 90, Text = "Dungeons (Lv. 90)" },
            new() { Id = OrderDungeons50, Type = WtObjectiveRules.DungeonExactLevel, Data = 50, Text = "Dungeons (Lv. 50)" },
            new() { Id = OrderFrontline, Type = WtObjectiveRules.SpecialContent, Data = 6, Text = "Frontline" },
            new() { Id = OrderDeepDungeons, Type = WtObjectiveRules.SpecialContent, Data = 9, Text = "Deep Dungeons" },
            new() { Id = OrderShbAlliance, Type = WtObjectiveRules.MultipleDuties, Data = 5, Text = "Alliance Raids (Shadowbringers)", MultipleOrderDuties = [CopiedFactory, PuppetsBunker] },
            new() { Id = OrderEndsinger, Type = WtObjectiveRules.SpecificDuty, Data = 846, Text = "Endsinger's Aria" },
            new() { Id = OrderP12N, Type = WtObjectiveRules.SpecificDuty, Data = 942, Text = "Anabaseios: The Twelfth Circle" },
            new() { Id = OrderTrials70To100, Type = WtObjectiveRules.Trials, Data = 100, MinLevel = 70, Text = "Trials (Lv. 70-100)" },
        ];

        return new DutyCatalog(duties, roulettes, orders);
    }
}
