using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PAS.AIQuestPortal.Api.Data;

namespace PAS.AIQuestPortal.Api.Notifications;

public static class TeamsBotActivityAuthentication
{
    public const string Scheme = "TeamsBotConnector";
    public const string Policy = "TeamsBotActivity";
}

public sealed record TeamsBotActivity(
    string? Type,
    string? ChannelId,
    string? ServiceUrl,
    TeamsBotActivityAccount? From,
    TeamsBotActivityAccount? Recipient,
    TeamsBotActivityConversation? Conversation,
    TeamsBotActivityChannelData? ChannelData);

public sealed record TeamsBotActivityAccount(
    string? Id,
    [property: JsonPropertyName("aadObjectId")] string? AadObjectId);
public sealed record TeamsBotActivityConversation(string? Id, string? ConversationType);
public sealed record TeamsBotActivityChannelData(TeamsBotActivityTenant? Tenant, TeamsBotActivityEntity? Team, TeamsBotActivityEntity? Channel);
public sealed record TeamsBotActivityTenant(string? Id);
public sealed record TeamsBotActivityEntity(string? Id);

public static class TeamsBotRecipientId
{
    private const string Prefix = "28:";
    public static bool TryParse(string? value, out Guid appId)
    {
        appId = default;
        return value is not null && value.StartsWith(Prefix, StringComparison.Ordinal)
            && Guid.TryParseExact(value.AsSpan(Prefix.Length), "D", out appId);
    }
}

public sealed class TeamsBotActivityCaptureService(
    ITeamsConversationReferenceWriter references,
    QuestDbContext db,
    TimeProvider clock,
    IOptions<NotificationOptions> options,
    IBotConnectorServiceUrlValidator serviceUrls,
    ILogger<TeamsBotActivityCaptureService> logger)
{
    private static readonly HashSet<string> CaptureActivityTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "conversationUpdate", "installationUpdate", "message"
    };

    public async Task<IResult> CaptureAsync(ClaimsPrincipal principal, TeamsBotActivity activity, CancellationToken ct)
    {
        if (!CaptureActivityTypes.Contains(activity.Type ?? string.Empty)
            || !string.Equals(activity.ChannelId, "msteams", StringComparison.OrdinalIgnoreCase)
            )
            return Results.Ok();

        if (!serviceUrls.TryValidate(activity.ServiceUrl, out Uri? serviceUrl)) return Results.BadRequest();
        string? authenticatedServiceUrl = principal.FindFirstValue("serviceurl");
        if (!serviceUrls.TryValidate(authenticatedServiceUrl, out Uri? claimedServiceUrl)
            || Uri.Compare(serviceUrl, claimedServiceUrl, UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) != 0)
            return Results.StatusCode(StatusCodes.Status403Forbidden);

        TeamsBotOptions configured = options.Value.TeamsBot;
        if (!Guid.TryParse(configured.TenantId, out Guid tenantId)
            || !Guid.TryParse(activity.ChannelData?.Tenant?.Id, out Guid activityTenant)
            || activityTenant != tenantId)
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (!Guid.TryParse(configured.MicrosoftAppId, out Guid botId)
            || !TeamsBotRecipientId.TryParse(activity.Recipient?.Id, out Guid recipientId)
            || recipientId != botId
            || string.IsNullOrWhiteSpace(activity.Conversation?.Id))
            return Results.BadRequest();

        if (!string.Equals(activity.Conversation.ConversationType, "personal", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(activity.ChannelData?.Team?.Id) || string.IsNullOrWhiteSpace(activity.ChannelData?.Channel?.Id))
                return Results.BadRequest();
            DateTimeOffset now = clock.GetUtcNow();
            TeamsChannelDestinationCandidate? candidate = await db.TeamsChannelDestinationCandidates.SingleOrDefaultAsync(
                x => x.TenantId == tenantId && x.ConversationId == activity.Conversation.Id, ct);
            if (candidate is null)
            {
                candidate = new TeamsChannelDestinationCandidate { Id = Guid.NewGuid(), TenantId = tenantId, ConversationId = activity.Conversation.Id,
                    CapturedAt = now };
                db.TeamsChannelDestinationCandidates.Add(candidate);
            }
            candidate.ServiceUrl = serviceUrl!.AbsoluteUri;
            candidate.TeamId = activity.ChannelData.Team.Id;
            candidate.ChannelId = activity.ChannelData.Channel.Id;
            candidate.BotId = botId.ToString("D");
            candidate.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Verified Teams channel destination candidate {CandidateId} captured for tenant {TenantId}.", candidate.Id, tenantId);
            return Results.Ok(new { captured = true, destinationCandidateId = candidate.Id });
        }

        if (!Guid.TryParse(activity.From?.AadObjectId, out Guid subjectId) || string.IsNullOrWhiteSpace(activity.From?.Id))
            return Results.BadRequest();

        TeamsConversationCaptureOutcome outcome;
        try
        {
            outcome = await references.RecordAsync(new(tenantId, subjectId, serviceUrl!, activity.Conversation.Id, botId.ToString("D"), activity.From.Id), ct);
        }
        catch (InvalidOperationException)
        {
            return Results.StatusCode(StatusCodes.Status409Conflict);
        }
        if (outcome == TeamsConversationCaptureOutcome.Unmapped)
            logger.LogInformation("Authenticated Teams personal installation was not bound because no verified participant identity exists.");
        return Results.Ok(new { captured = outcome == TeamsConversationCaptureOutcome.Stored });
    }
}

public static class TeamsBotActivityEndpoints
{
    public static Task<IResult> CaptureAsync(HttpContext http, TeamsBotActivity activity, TeamsBotActivityCaptureService service, CancellationToken ct) =>
        service.CaptureAsync(http.User, activity, ct);
}
