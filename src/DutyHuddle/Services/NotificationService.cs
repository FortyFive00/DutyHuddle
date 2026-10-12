using Dalamud.Interface.ImGuiNotification;
using Dalamud.Plugin.Services;
using DutyHuddle.Configuration;
using DutyHuddle.Domain.Analysis;

namespace DutyHuddle.Services;

/// <summary>Tells the player when a duty appears that every known journal in the party still needs.</summary>
public sealed class NotificationService
{
    private readonly INotificationManager notifications;
    private readonly PluginConfig config;
    private readonly HashSet<string> announced = new(StringComparer.Ordinal);

    public NotificationService(INotificationManager notifications, PluginConfig config)
    {
        this.notifications = notifications;
        this.config = config;
    }

    public void OnViewUpdated(PartyView view)
    {
        if (!config.NotifyCommonDuty)
            return;

        var common = view.Ranking
            .Where(r => r.Group == CoverageGroup.All && r.KnownMembers >= 2)
            .ToList();

        // Forget duties that are no longer common, so they can be announced again later.
        announced.IntersectWith(common.Select(r => r.Label));

        var fresh = common.Where(r => announced.Add(r.Label)).ToList();
        if (fresh.Count == 0)
            return;

        var names = string.Join(", ", fresh.Take(3).Select(r => r.Label)) + (fresh.Count > 3 ? $" and {fresh.Count - 3} more" : string.Empty);
        notifications.AddNotification(new Notification
        {
            Title = "DutyHuddle",
            Content = $"Every synced member needs {names} for Wondrous Tails.",
            Type = NotificationType.Info,
        });
    }
}
