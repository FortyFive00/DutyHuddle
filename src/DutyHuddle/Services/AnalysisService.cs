using DutyHuddle.Configuration;
using DutyHuddle.Domain.Analysis;
using DutyHuddle.Domain.Duty;
using DutyHuddle.Domain.Time;

namespace DutyHuddle.Services;

/// <summary>Everything the three tabs display, computed once per state change and immutable afterwards.</summary>
public sealed record PartyView(
    DateTime ComputedUtc,
    IReadOnlyList<PartyMemberState> Members,
    IReadOnlyList<RouletteRow> RouletteGrid,
    IReadOnlyList<RouletteRecommendation> Recommendations,
    IReadOnlyList<WtMemberView> Journals,
    IReadOnlyList<WtRankingRow> Ranking);

/// <summary>Runs the domain analyzers on the current store contents and caches the result.</summary>
public sealed class AnalysisService
{
    private readonly PartyStateStore store;
    private readonly PluginConfig config;
    private readonly DutyCatalog catalog;
    private readonly WtObjectiveResolver resolver;
    private readonly Lock gate = new();

    private PartyView? cached;
    private long cachedVersion = -1;
    private DateOnly cachedEpoch;
    private int cachedSettings;

    public AnalysisService(PartyStateStore store, PluginConfig config, DutyCatalog catalog, WtObjectiveResolver resolver)
    {
        this.store = store;
        this.config = config;
        this.catalog = catalog;
        this.resolver = resolver;
        Durations = new DurationEstimator(catalog, config.GetMeasured, id => config.RouletteMinutesOverride.GetValueOrDefault(id));
    }

    public IDurationEstimator Durations { get; }

    /// <summary>Raised after a recomputation, with the new view.</summary>
    public event Action<PartyView>? Updated;

    public PartyView Current
    {
        get
        {
            var now = DateTime.UtcNow;
            var epoch = GameClock.DailyEpoch(now);
            var settings = SettingsHash();
            PartyView view;
            lock (gate)
            {
                if (cached is not null && cachedVersion == store.Version && cachedEpoch == epoch && cachedSettings == settings)
                    return cached;

                cachedVersion = store.Version;
                cachedEpoch = epoch;
                cachedSettings = settings;
                cached = view = Compute(now, epoch);
            }

            Updated?.Invoke(view);
            return view;
        }
    }

    private PartyView Compute(DateTime now, DateOnly epoch)
    {
        var members = store.GetStates();
        var grid = RouletteAnalyzer.BuildGrid(catalog, members, epoch);
        var engine = new RecommendationEngine(catalog, resolver, Durations);
        var recommendations = engine.Recommend(grid, members, config.ToRecommendationSettings(catalog.Roulettes.Select(r => r.Id)), now);
        var analyzer = new WtAnalyzer(catalog, resolver, Durations);
        var journals = analyzer.BuildMemberViews(members, now);
        var ranking = analyzer.Rank(journals, config.GroupCategories);
        return new PartyView(now, members, grid, recommendations, journals, ranking);
    }

    private int SettingsHash()
    {
        var hash = new HashCode();
        hash.Add(config.GroupCategories);
        hash.Add(config.IncludePvP);
        hash.Add(config.UnknownWeight);
        foreach (var (id, value) in config.RoulettePriority.OrderBy(kv => kv.Key))
            hash.Add((id, value));
        foreach (var (id, value) in config.RouletteMinutesOverride.OrderBy(kv => kv.Key))
            hash.Add((id, value));
        hash.Add(config.MeasuredDurations.Count);
        return hash.ToHashCode();
    }
}
