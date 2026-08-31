using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace PAS.AIQuestPortal.Api.Notifications;

public sealed record CapturedNotification(string CaptureId, Guid EventId, string EventType, string DestinationClass, string Title, string Body, string ActionLabel, string ActionUrl, DateTimeOffset CapturedAt, string? DestinationKey = null);
public interface ICapturedNotificationStore { IReadOnlyCollection<CapturedNotification> Read(); void Add(CapturedNotification notification); }
public sealed class CapturedNotificationStore(IOptions<NotificationOptions> options) : ICapturedNotificationStore
{
    private readonly Queue<CapturedNotification> notifications = new();
    private readonly object sync = new();
    public IReadOnlyCollection<CapturedNotification> Read() { lock (sync) return notifications.ToArray(); }
    public void Add(CapturedNotification notification) { lock (sync) { notifications.Enqueue(notification); while (notifications.Count > options.Value.CaptureMaxItems) notifications.Dequeue(); } }
}

public sealed class CaptureNotificationDeliveryProvider(ICapturedNotificationStore store, TimeProvider clock, ILogger<CaptureNotificationDeliveryProvider> logger) : INotificationDeliveryProvider
{
    public Task<NotificationDeliveryResult> DeliverAsync(NotificationDeliveryRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested(); string captureId = Guid.NewGuid().ToString("N");
        store.Add(new(captureId, request.EventId, request.EventType.ToString(), request.DestinationType, request.Notification.Title, request.Notification.Body, request.Notification.ActionLabel, request.Notification.ActionUrl, clock.GetUtcNow(), request.DestinationKey));
        logger.LogInformation("Captured notification EventId={EventId} EventType={EventType} DestinationClass={DestinationClass} CaptureId={CaptureId}", request.EventId, request.EventType, request.DestinationType, captureId);
        return Task.FromResult(NotificationDeliveryResult.Captured(captureId));
    }
}
