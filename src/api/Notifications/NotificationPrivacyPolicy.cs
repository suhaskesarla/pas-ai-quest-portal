using System.Text.Json;
using Microsoft.Extensions.Options;
using PAS.AIQuestPortal.Api.Data;

namespace PAS.AIQuestPortal.Api.Notifications;

public sealed record NotificationPolicyDecision(bool Allowed, string? SuppressionReason = null);
public interface INotificationPrivacyPolicy { NotificationPolicyDecision Evaluate(NotificationOutbox row); NotificationPolicyDecision EvaluateLeaderboard(LeaderboardAnnouncementPayload payload); }
public sealed class NotificationPrivacyPolicy(IOptions<NotificationOptions> options) : INotificationPrivacyPolicy
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public NotificationPolicyDecision Evaluate(NotificationOutbox row)
    {
        if (row.EventType != NotificationEventType.LeaderboardAnnouncement.ToString()) return new(true);
        LeaderboardAnnouncementPayload payload = JsonSerializer.Deserialize<LeaderboardAnnouncementPayload>(row.PayloadJson, JsonOptions) ?? throw new InvalidDataException("Malformed leaderboard payload.");
        return EvaluateLeaderboard(payload);
    }
    public NotificationPolicyDecision EvaluateLeaderboard(LeaderboardAnnouncementPayload payload) => payload.IsSynthetic || options.Value.RealUserLeaderboardEnabled ? new(true) : new(false, "RealUserLeaderboardDisabled");
}
