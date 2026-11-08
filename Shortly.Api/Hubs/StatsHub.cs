using Microsoft.AspNetCore.SignalR;
using Shortly.Infrastructure.BackgroundJobs;

namespace Shortly.Api.Hubs;

public sealed class StatsHub : Hub
{
    public Task SubscribeToLink(string code)
        => Groups.AddToGroupAsync(Context.ConnectionId, GroupName(code));

    public Task UnsubscribeFromLink(string code)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(code));

    public static string GroupName(string code) => $"link:{code}";
}

/// <summary>Реализация уведомителя из инфраструктуры через SignalR.</summary>
public sealed class SignalRClickFlushNotifier : IClickFlushNotifier
{
    private readonly IHubContext<StatsHub> _hub;

    public SignalRClickFlushNotifier(IHubContext<StatsHub> hub) => _hub = hub;

    public Task NotifyAsync(IReadOnlyCollection<long> linkIds, int clicks, CancellationToken ct)
        => _hub.Clients.All.SendAsync(
            "clicksFlushed",
            new { linkIds, clicks, atUtc = DateTime.UtcNow },
            ct);
}