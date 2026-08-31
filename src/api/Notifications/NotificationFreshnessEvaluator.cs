using Microsoft.EntityFrameworkCore;
using PAS.AIQuestPortal.Api.Data;

namespace PAS.AIQuestPortal.Api.Notifications;

public sealed record NotificationFreshness(bool ShouldDeliver, string? SuppressionReason = null);
public interface INotificationFreshnessEvaluator { Task<NotificationFreshness> EvaluateAsync(NotificationOutbox notification, CancellationToken cancellationToken); }

public sealed class NotificationFreshnessEvaluator(QuestDbContext db) : INotificationFreshnessEvaluator
{
    public async Task<NotificationFreshness> EvaluateAsync(NotificationOutbox notification, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse(notification.EventType, out NotificationEventType type)) return new(false, "UnknownEventType");
        return type switch
        {
            NotificationEventType.ChallengePublished => await ChallengeAsync(notification.AggregateId, cancellationToken),
            NotificationEventType.SubmissionSubmitted => await SubmissionAsync(notification.AggregateId, SubmissionStatus.Submitted, cancellationToken),
            NotificationEventType.SubmissionResubmitted => await SubmissionAsync(notification.AggregateId, SubmissionStatus.Resubmitted, cancellationToken),
            NotificationEventType.SubmissionNeedsEvidence => await SubmissionAsync(notification.AggregateId, SubmissionStatus.NeedsEvidence, cancellationToken),
            NotificationEventType.SubmissionApproved or NotificationEventType.SubmissionRejected or NotificationEventType.LeaderboardAnnouncement => new(true),
            _ => new(false, "UnknownEventType")
        };
    }
    private async Task<NotificationFreshness> ChallengeAsync(Guid id, CancellationToken ct)
    {
        ChallengeStatus? status = await db.Challenges.AsNoTracking().Where(x => x.Id == id).Select(x => (ChallengeStatus?)x.Status).SingleOrDefaultAsync(ct);
        return status is ChallengeStatus.Closed or ChallengeStatus.Archived ? new(false, "ChallengeNoLongerActionable") : status is null ? new(false, "ChallengeMissing") : new(true);
    }
    private async Task<NotificationFreshness> SubmissionAsync(Guid id, SubmissionStatus expected, CancellationToken ct)
    {
        SubmissionStatus? status = await db.Submissions.AsNoTracking().Where(x => x.Id == id).Select(x => (SubmissionStatus?)x.Status).SingleOrDefaultAsync(ct);
        return status == expected ? new(true) : new(false, status is null ? "SubmissionMissing" : $"SubmissionSupersededBy{status}");
    }
}
