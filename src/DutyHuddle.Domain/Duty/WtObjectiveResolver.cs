namespace DutyHuddle.Domain.Duty;

/// <summary>A journal objective expressed as the set of concrete duties that satisfy it.</summary>
public sealed record ResolvedObjective(
    byte OrderId,
    string Text,
    uint IconId,
    IReadOnlySet<uint> DutyIds,
    bool IsRecognized);

/// <summary>
/// Turns WeeklyBingoOrderData rows into duty sets. The rules for each <c>Type</c> are inferred from
/// the sheet (they are not documented by the game) and live in <see cref="WtObjectiveRules"/>.
/// </summary>
public sealed class WtObjectiveResolver
{
    private readonly DutyCatalog catalog;
    private readonly Dictionary<byte, ResolvedObjective> cache = [];
    private readonly Lock gate = new();

    public WtObjectiveResolver(DutyCatalog catalog)
    {
        this.catalog = catalog;
    }

    public ResolvedObjective Resolve(byte orderId)
    {
        lock (gate)
        {
            if (cache.TryGetValue(orderId, out var cached))
                return cached;

            var resolved = ResolveUncached(orderId);
            cache[orderId] = resolved;
            return resolved;
        }
    }

    private ResolvedObjective ResolveUncached(byte orderId)
    {
        var order = catalog.GetOrder(orderId);
        if (order is null)
            return new ResolvedObjective(orderId, $"Unknown objective #{orderId}", 0, new HashSet<uint>(), false);

        var predicate = WtObjectiveRules.GetPredicate(order);
        HashSet<uint> ids = order.Type switch
        {
            WtObjectiveRules.SpecificDuty => catalog.GetDutyByInstance(order.Data) is { } duty ? [duty.Id] : [],
            WtObjectiveRules.MultipleDuties => [.. order.MultipleOrderDuties.Where(id => catalog.GetDuty(id) is not null)],
            _ when predicate is not null => [.. catalog.Duties.Values.Where(predicate).Select(d => d.Id)],
            _ => [],
        };

        var recognized = order.Type is WtObjectiveRules.SpecificDuty or WtObjectiveRules.MultipleDuties || predicate is not null;

        // Single-duty objectives carry a placeholder text in the sheet ("Dammies"); the journal shows the duty name.
        var text = order.Type == WtObjectiveRules.SpecificDuty && ids.Count == 1
            ? catalog.GetDuty(ids.First())!.Name
            : order.Text;
        return new ResolvedObjective(orderId, text, order.IconId, ids, recognized && ids.Count > 0);
    }
}
