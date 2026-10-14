using DutyHuddle.Domain.Analysis;
using DutyHuddle.Domain.Duty;
using DutyHuddle.Domain.Models;
using DutyHuddle.GameAdapters;
using Lumina;
using Lumina.Data;
using Lumina.Excel;
using Lumina.Excel.Sheets;

var install = args.Length > 0 ? args[0] : @"C:\Program Files (x86)\Steam\steamapps\common\FINAL FANTASY XIV Online";
var sqpack = Path.Combine(install, "game", "sqpack");
if (!Directory.Exists(sqpack))
{
    Console.Error.WriteLine($"Game data not found at {sqpack}");
    return 2;
}

using var game = new GameData(sqpack, new LuminaOptions { PanicOnSheetChecksumMismatch = false, DefaultExcelLanguage = Language.English });
var repository = new GameDataRepository(new LuminaSheetSource(game));
var catalog = repository.Load();
var resolver = new WtObjectiveResolver(catalog);

Console.WriteLine($"Duties: {catalog.Duties.Count}");
Console.WriteLine();
Console.WriteLine("Daily roulettes:");
foreach (var roulette in catalog.Roulettes)
{
    var pool = catalog.GetPool(roulette).Count();
    Console.WriteLine($"  {roulette.Id,3} {roulette.Name,-40} party<= {roulette.MembersPerParty,-2} pool {pool,3} icon {roulette.IconId} pvp {roulette.IsPvP}");
}

var mentor = catalog.Roulettes.FirstOrDefault(r => r.IsMentorOnly);
if (mentor is not null)
{
    Console.WriteLine();
    Console.WriteLine("Mentor pool:");
    foreach (var duty in catalog.GetPool(mentor).OrderBy(d => d.InstanceContentId))
        Console.WriteLine($"  cfc {duty.Id,4} ic {duty.InstanceContentId,5} {duty.Name}  [{duty.Roulettes}]");
}

foreach (var id in new uint[] { 1, 9, 15 })
{
    var row = game.GetExcelSheet<ContentRoulette>()!.GetRow(id);
    Console.WriteLine();
    Console.WriteLine($"ContentRoulette {id} {row.Category.ExtractText()} openrule={row.OpenRule.RowId} instance={row.InstanceContent.RowId}");
    var rule = row.OpenRule.ValueNullable;
    if (rule is not null)
        foreach (var prop in typeof(ContentRouletteOpenRule).GetProperties())
        {
            if (prop.GetIndexParameters().Length > 0 || prop.Name is "ExcelPage" or "RowOffset") continue;
            try { Console.WriteLine($"  rule.{prop.Name} = {prop.GetValue(rule)}"); } catch { }
        }
}

Console.WriteLine();
Console.WriteLine("Wondrous Tails objectives:");
var problems = 0;
var byType = new SortedDictionary<uint, (int Total, int Resolved)>();
foreach (var row in game.GetExcelSheet<WeeklyBingoOrderData>()!)
{
    // Rows with type 0 and no data are unused placeholders: they never appear in a journal.
    if (row.RowId > byte.MaxValue || row.RowId == 0 || (row.Type == WtObjectiveRules.SpecificDuty && row.Data.RowId == 0))
        continue;

    var order = catalog.GetOrder((byte)row.RowId)!;
    var resolved = resolver.Resolve(order.Id);
    var stats = byType.GetValueOrDefault(order.Type);
    byType[order.Type] = (stats.Total + 1, stats.Resolved + (resolved.IsRecognized ? 1 : 0));

    var sample = string.Join(", ", resolved.DutyIds.Take(3).Select(id => catalog.GetDuty(id)!.Name));
    var flag = resolved.IsRecognized ? "  " : "!!";
    if (!resolved.IsRecognized)
        problems++;
    Console.WriteLine($"{flag} {order.Id,3} T{order.Type} data {order.Data,5} min {order.MinLevel,3} | {resolved.Text,-48} -> {resolved.DutyIds.Count,3} duties  {sample}");
}

Console.WriteLine();
foreach (var (type, (total, ok)) in byType)
    Console.WriteLine($"Type {type}: {ok}/{total} resolved");
Console.WriteLine(problems == 0 ? "All objectives resolved." : $"{problems} objective(s) did not resolve to any duty.");

Console.WriteLine();
Console.WriteLine("Expansions: " + string.Join(", ", repository.LoadExpansionNames().Select(kv => $"{kv.Key}={kv.Value}")));

// End-to-end: three random journals drawn from real objectives, ranked on the real catalog.
var playable = Enumerable.Range(1, 254)
    .Select(i => (byte)i)
    .Where(id => resolver.Resolve(id).IsRecognized)
    .ToArray();
var random = new Random(7);
var now = DateTime.UtcNow;
var members = new[] { "Tu", "Kaia", "Rhen" }.Select(name =>
{
    var orders = playable.OrderBy(_ => random.Next()).Take(16).ToArray();
    var status = Enumerable.Range(0, 16).Select(_ => random.Next(3) == 0 ? WtTaskStatus.Claimed : WtTaskStatus.Open).ToArray();
    var board = new WtBoard { OrderIds = orders, Status = status, StickerMask = 0, SecondChancePoints = 3, ExpiresUtc = now.AddDays(7) };
    var snapshot = new MemberSnapshot(new MemberId(name, 1), now, DateOnly.FromDateTime(now), new Dictionary<uint, Availability>(), board, SnapshotSource.Local);
    return new PartyMemberState(snapshot.Member, name == "Tu", snapshot);
}).ToList();

var durations = new DurationEstimator(catalog);
var analyzer = new WtAnalyzer(catalog, resolver, durations);
var watch = System.Diagnostics.Stopwatch.StartNew();
var ranking = analyzer.Rank(analyzer.BuildMemberViews(members, now), groupCategories: true);
watch.Stop();
Console.WriteLine();
Console.WriteLine($"Simulated party ranking ({ranking.Count} rows, {watch.ElapsedMilliseconds} ms):");
foreach (var row in ranking.Take(8))
    Console.WriteLine($"  {row.MemberCount}/{row.KnownMembers} {row.Group,-6} {(row.IsCategory ? $"[{row.DutyIds.Count} duties] " : string.Empty)}{row.Label} (~{row.EstimatedMinutes} min)");
return 0;

internal sealed class LuminaSheetSource(GameData game) : ISheetSource
{
    public ExcelSheet<T> GetSheet<T>()
        where T : struct, IExcelRow<T>
        => game.GetExcelSheet<T>(Language.English)!;
}
