using System.Text.Json;
using PAS.AIQuestPortal.Api.Data;

namespace PAS.AIQuestPortal.Api.Notifications;

public interface INotificationOutboxWriter
{
    NotificationOutbox Enqueue<TPayload>(Guid eventId, NotificationEventType eventType, NotificationDestination destination, string aggregateType, Guid aggregateId, TPayload payload, DateTimeOffset createdAt) where TPayload : INotificationPayload;
}

public sealed class NotificationOutboxWriter(QuestDbContext db) : INotificationOutboxWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public NotificationOutbox Enqueue<TPayload>(Guid eventId, NotificationEventType eventType, NotificationDestination destination, string aggregateType, Guid aggregateId, TPayload payload, DateTimeOffset createdAt) where TPayload : INotificationPayload
    {
        if (eventId == Guid.Empty || aggregateId == Guid.Empty) throw new ArgumentException("Notification event and aggregate IDs are required.");
        NotificationRoutingPolicy.Validate(eventType, destination);
        var row = new NotificationOutbox { Id = Guid.NewGuid(), EventId = eventId, EventType = eventType.ToString(), DestinationType = destination.Type, DestinationKey = destination.Key, RecipientParticipantId = destination.RecipientParticipantId, AggregateType = aggregateType, AggregateId = aggregateId, PayloadVersion = TPayload.Version, PayloadJson = JsonSerializer.Serialize(payload, JsonOptions), Status = NotificationStatuses.Pending, AttemptCount = 0, NextAttemptAt = createdAt, CreatedAt = createdAt };
        db.NotificationOutbox.Add(row);
        return row;
    }
}

public static class NotificationRoutingPolicy
{
    public static NotificationDestination Resolve(NotificationEventType eventType, Guid? recipientParticipantId = null) => eventType switch
    {
        NotificationEventType.ChallengePublished or NotificationEventType.LeaderboardAnnouncement when recipientParticipantId is null => NotificationDestinations.General(),
        NotificationEventType.SubmissionSubmitted or NotificationEventType.SubmissionResubmitted when recipientParticipantId is null => NotificationDestinations.Managers(),
        NotificationEventType.SubmissionNeedsEvidence or NotificationEventType.SubmissionApproved or NotificationEventType.SubmissionRejected when recipientParticipantId is Guid recipient => NotificationDestinations.Participant(recipient),
        _ => throw new ArgumentException("The notification event and recipient do not define a valid BA-017 destination.")
    };
    public static void Validate(NotificationEventType eventType, NotificationDestination destination)
    {
        NotificationDestination expected = Resolve(eventType, destination.RecipientParticipantId);
        if (destination != expected) throw new ArgumentException($"Destination is not permitted for {eventType}.");
    }
}

public static class NotificationStatuses
{
    public const string Pending = "Pending"; public const string Processing = "Processing"; public const string RetryPending = "RetryPending"; public const string Sent = "Sent"; public const string Captured = "Captured"; public const string Suppressed = "Suppressed"; public const string Failed = "Failed"; public const string DeliveryUnknown = "DeliveryUnknown";
}
