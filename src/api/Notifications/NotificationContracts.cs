using System.Text.Json.Serialization;

namespace PAS.AIQuestPortal.Api.Notifications;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NotificationEventType { ChallengePublished, SubmissionSubmitted, SubmissionResubmitted, SubmissionNeedsEvidence, SubmissionApproved, SubmissionRejected, LeaderboardAnnouncement }

public static class NotificationDestinations
{
    public const string ConfiguredAudience = "ConfiguredAudience";
    public const string ParticipantPrivate = "ParticipantPrivate";
    public const string QuestGeneralAudience = "QUEST_GENERAL_AUDIENCE";
    public const string QuestManagerAudience = "QUEST_MANAGER_AUDIENCE";
    public static NotificationDestination General() => new(ConfiguredAudience, QuestGeneralAudience, null);
    public static NotificationDestination Managers() => new(ConfiguredAudience, QuestManagerAudience, null);
    public static NotificationDestination Participant(Guid participantId) => new(ParticipantPrivate, $"participant:{participantId:N}", participantId);
}

public sealed record NotificationDestination(string Type, string Key, Guid? RecipientParticipantId);
public interface INotificationPayload { static abstract int Version { get; } }
public sealed record NotificationTaskSummary(string Name, int Xp);
public sealed record ChallengePublishedPayload(Guid ChallengeId, string ChallengeName, string? ShortDescription, DateTimeOffset OpenAt, DateTimeOffset DueAt, DateTimeOffset CloseAt, IReadOnlyList<NotificationTaskSummary> Tasks) : INotificationPayload { public static int Version => 1; }
public sealed record SubmissionSubmittedPayload(Guid SubmissionId, string ClaimantDisplayName, Guid ChallengeId, string ChallengeName, Guid TaskId, string TaskName, int BeneficiaryCount, DateTimeOffset SubmittedAt) : INotificationPayload { public static int Version => 1; }
public sealed record SubmissionResubmittedPayload(Guid SubmissionId, string ClaimantDisplayName, Guid ChallengeId, string ChallengeName, Guid TaskId, string TaskName, int BeneficiaryCount, DateTimeOffset ResubmittedAt) : INotificationPayload { public static int Version => 1; }
public sealed record SubmissionNeedsEvidencePayload(Guid SubmissionId, Guid ChallengeId, string ChallengeName, Guid TaskId, string TaskName, DateTimeOffset EffectiveDeadline, string? ParticipantVisibleComment) : INotificationPayload { public static int Version => 1; }
public sealed record SubmissionApprovedPayload(Guid SubmissionId, Guid ChallengeId, string ChallengeName, Guid TaskId, string TaskName, Guid CycleId, int AwardedXp, DateTimeOffset ApprovedAt) : INotificationPayload { public static int Version => 1; }
public sealed record SubmissionRejectedPayload(Guid SubmissionId, Guid ChallengeId, string ChallengeName, Guid TaskId, string TaskName, string? ParticipantVisibleReason, DateTimeOffset RejectedAt) : INotificationPayload { public static int Version => 1; }
public sealed record LeaderboardRowPayload(int Rank, string DisplayName, int TotalXp);
public sealed record LeaderboardAnnouncementPayload(Guid CycleId, string CycleName, DateTimeOffset GeneratedAt, bool IsSynthetic, IReadOnlyList<LeaderboardRowPayload> TopParticipants) : INotificationPayload { public static int Version => 1; }

public enum NotificationDeliveryOutcome { Delivered, Captured, RetryableFailure, PermanentFailure, DeliveryUnknown }
public sealed record NotificationDeliveryResult(NotificationDeliveryOutcome Outcome, string? ProviderMessageId = null, string? Code = null, TimeSpan? RetryAfter = null, string? Summary = null)
{
    public static NotificationDeliveryResult Captured(string id) => new(NotificationDeliveryOutcome.Captured, id);
}
public sealed record RenderedNotification(string Title, string Body, string ActionLabel, string ActionUrl);
public sealed record NotificationDeliveryRequest(Guid EventId, NotificationEventType EventType, string DestinationType, string DestinationKey, Guid? RecipientParticipantId, RenderedNotification Notification);
public interface INotificationDeliveryProvider { Task<NotificationDeliveryResult> DeliverAsync(NotificationDeliveryRequest request, CancellationToken cancellationToken); }
