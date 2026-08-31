using System.Text.Json;
using Microsoft.Extensions.Options;

namespace PAS.AIQuestPortal.Api.Notifications;

public interface INotificationDeepLinkBuilder
{
    string Challenge(Guid challengeId); string ManagerSubmission(Guid submissionId); string ParticipantSubmission(Guid submissionId); string XpActivity(Guid cycleId); string SubmissionHistory(Guid submissionId); string IndividualLeaderboard(Guid cycleId);
}

public sealed class NotificationDeepLinkBuilder(IOptions<NotificationOptions> options) : INotificationDeepLinkBuilder
{
    private readonly Uri root = new(options.Value.PortalBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
    private string Build(string path) => new Uri(root, path.TrimStart('/')).AbsoluteUri;
    public string Challenge(Guid id) => Build($"challenges/{id:D}");
    public string ManagerSubmission(Guid id) => Build($"manager/submissions/{id:D}");
    public string ParticipantSubmission(Guid id) => Build($"activity/submissions/{id:D}");
    public string XpActivity(Guid cycleId) => Build($"xp-activity?cycleId={cycleId:D}");
    public string SubmissionHistory(Guid id) => Build($"activity/submissions/{id:D}/history");
    public string IndividualLeaderboard(Guid cycleId) => Build($"leaderboard?cycleId={cycleId:D}");
}

public interface INotificationRenderer { RenderedNotification Render(NotificationEventType eventType, int payloadVersion, string payloadJson); }

public sealed class NotificationRenderer(INotificationDeepLinkBuilder links) : INotificationRenderer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public RenderedNotification Render(NotificationEventType type, int version, string json)
    {
        if (version != 1) throw new InvalidDataException("Unsupported notification payload version.");
        return type switch
        {
            NotificationEventType.ChallengePublished => Challenge(Deserialize<ChallengePublishedPayload>(json)),
            NotificationEventType.SubmissionSubmitted => Submitted(Deserialize<SubmissionSubmittedPayload>(json)),
            NotificationEventType.SubmissionResubmitted => Resubmitted(Deserialize<SubmissionResubmittedPayload>(json)),
            NotificationEventType.SubmissionNeedsEvidence => NeedsEvidence(Deserialize<SubmissionNeedsEvidencePayload>(json)),
            NotificationEventType.SubmissionApproved => Approved(Deserialize<SubmissionApprovedPayload>(json)),
            NotificationEventType.SubmissionRejected => Rejected(Deserialize<SubmissionRejectedPayload>(json)),
            NotificationEventType.LeaderboardAnnouncement => Leaderboard(Deserialize<LeaderboardAnnouncementPayload>(json)),
            _ => throw new InvalidDataException("Unsupported notification event type.")
        };
    }
    private static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonOptions) ?? throw new InvalidDataException("Malformed notification payload.");
    private RenderedNotification Challenge(ChallengePublishedPayload p) => new("Challenge published", $"{p.ChallengeName}\n{Truncate(p.ShortDescription, 240)}\nOpen {p.OpenAt:u}; due {p.DueAt:u}; closes {p.CloseAt:u}.\n{string.Join(", ", p.Tasks.Select(x => $"{x.Name} ({x.Xp} XP)"))}", "View challenge", links.Challenge(p.ChallengeId));
    private RenderedNotification Submitted(SubmissionSubmittedPayload p) => new("Submission received", $"{p.ClaimantDisplayName} submitted {p.ChallengeName} · {p.TaskName} for {p.BeneficiaryCount} beneficiary/beneficiaries at {p.SubmittedAt:u}.", "Review submission", links.ManagerSubmission(p.SubmissionId));
    private RenderedNotification Resubmitted(SubmissionResubmittedPayload p) => new("Submission resubmitted", $"{p.ClaimantDisplayName} resubmitted {p.ChallengeName} · {p.TaskName} for {p.BeneficiaryCount} beneficiary/beneficiaries at {p.ResubmittedAt:u}.", "Review submission", links.ManagerSubmission(p.SubmissionId));
    private RenderedNotification NeedsEvidence(SubmissionNeedsEvidencePayload p) => new("More evidence required", $"{p.ChallengeName} · {p.TaskName} needs evidence by {p.EffectiveDeadline:u}. {p.ParticipantVisibleComment ?? "Please review the portal for details."}", "Update submission", links.ParticipantSubmission(p.SubmissionId));
    private RenderedNotification Approved(SubmissionApprovedPayload p) => new("Submission approved", $"{p.ChallengeName} · {p.TaskName} was approved. You received {p.AwardedXp} XP at {p.ApprovedAt:u}.", "View XP activity", links.XpActivity(p.CycleId));
    private RenderedNotification Rejected(SubmissionRejectedPayload p) => new("Submission rejected", $"{p.ChallengeName} · {p.TaskName} was rejected with no XP. {p.ParticipantVisibleReason ?? "View submission history for details."}", "View submission history", links.SubmissionHistory(p.SubmissionId));
    private RenderedNotification Leaderboard(LeaderboardAnnouncementPayload p) => new("Leaderboard update", $"{p.CycleName} · generated {p.GeneratedAt:u}\n{string.Join("\n", p.TopParticipants.Take(3).Select(x => $"{x.Rank}. {x.DisplayName} — {x.TotalXp} XP"))}", "View leaderboard", links.IndividualLeaderboard(p.CycleId));
    private static string Truncate(string? value, int max) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Length <= max ? value : value[..max];
}
