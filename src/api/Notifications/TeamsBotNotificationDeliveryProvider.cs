using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PAS.AIQuestPortal.Api.Data;

namespace PAS.AIQuestPortal.Api.Notifications;

public interface IBotConnectorServiceUrlValidator
{
    bool TryValidate(string? value, out Uri? serviceUrl);
    bool IsTrusted(Uri serviceUrl);
}

public sealed class BotConnectorServiceUrlValidator : IBotConnectorServiceUrlValidator
{
    private const string PublicTeamsConnectorHost = "smba.trafficmanager.net";

    public bool TryValidate(string? value, out Uri? serviceUrl)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out serviceUrl) || !IsTrusted(serviceUrl))
        {
            serviceUrl = null;
            return false;
        }
        return true;
    }

    public bool IsTrusted(Uri serviceUrl) => serviceUrl.Scheme == Uri.UriSchemeHttps
        && serviceUrl.IsDefaultPort
        && string.IsNullOrEmpty(serviceUrl.UserInfo)
        && string.IsNullOrEmpty(serviceUrl.Query)
        && string.IsNullOrEmpty(serviceUrl.Fragment)
        && string.Equals(serviceUrl.IdnHost, PublicTeamsConnectorHost, StringComparison.OrdinalIgnoreCase);
}

public sealed record TeamsResolvedDestination(Guid TenantId, Uri ServiceUrl, string ConversationId, string BotId, string? UserId);
public sealed record TeamsTransportResult(NotificationDeliveryOutcome Outcome, string? ProviderMessageId = null, string? Code = null, TimeSpan? RetryAfter = null, string? Summary = null);
public interface ITeamsProactiveTransport { Task<TeamsTransportResult> SendAsync(TeamsResolvedDestination destination, RenderedNotification notification, CancellationToken ct); }

public sealed record VerifiedTeamsConversation(Guid TenantId, Guid SubjectId, Uri ServiceUrl, string ConversationId, string BotId, string UserId);
public enum TeamsConversationCaptureOutcome { Stored, Unmapped }
public interface ITeamsConversationReferenceWriter { Task<TeamsConversationCaptureOutcome> RecordAsync(VerifiedTeamsConversation conversation, CancellationToken ct); }

public sealed class TeamsConversationReferenceWriter(
    QuestDbContext db,
    TimeProvider clock,
    IOptions<NotificationOptions> options,
    IBotConnectorServiceUrlValidator serviceUrls) : ITeamsConversationReferenceWriter
{
    public async Task<TeamsConversationCaptureOutcome> RecordAsync(VerifiedTeamsConversation conversation, CancellationToken ct)
    {
        TeamsBotOptions configured = options.Value.TeamsBot;
        if (!Guid.TryParse(configured.TenantId, out Guid tenantId) || conversation.TenantId != tenantId)
            throw new InvalidOperationException("The Teams conversation tenant is not approved.");
        if (!Guid.TryParse(configured.MicrosoftAppId, out Guid botId) || !Guid.TryParse(conversation.BotId, out Guid suppliedBotId) || suppliedBotId != botId)
            throw new InvalidOperationException("The Teams conversation recipient is not this bot.");
        if (!serviceUrls.IsTrusted(conversation.ServiceUrl) || string.IsNullOrWhiteSpace(conversation.ConversationId) || string.IsNullOrWhiteSpace(conversation.UserId))
            throw new InvalidOperationException("The Teams conversation reference is invalid.");

        ParticipantExternalIdentity? identity = await db.ParticipantExternalIdentities.SingleOrDefaultAsync(x =>
            x.Provider == "Entra" && x.TenantId == tenantId && x.SubjectId == conversation.SubjectId && x.VerifiedAt != null, ct);
        if (identity is null) return TeamsConversationCaptureOutcome.Unmapped;

        bool belongsToAnotherIdentity = await db.TeamsConversationReferences.AnyAsync(x =>
            x.IsActive && x.TenantId == tenantId && x.ConversationId == conversation.ConversationId && x.ParticipantExternalIdentityId != identity.Id, ct);
        if (belongsToAnotherIdentity) throw new InvalidOperationException("The Teams conversation is already associated with another verified identity.");

        DateTimeOffset now = clock.GetUtcNow();
        TeamsConversationReference? reference = await db.TeamsConversationReferences.SingleOrDefaultAsync(x => x.ParticipantExternalIdentityId == identity.Id && x.IsActive, ct);
        if (reference is null)
        {
            reference = new() { Id = Guid.NewGuid(), ParticipantExternalIdentityId = identity.Id, CreatedAt = now, IsActive = true };
            db.TeamsConversationReferences.Add(reference);
        }
        reference.TenantId = tenantId;
        reference.ServiceUrl = conversation.ServiceUrl.AbsoluteUri;
        reference.ConversationId = conversation.ConversationId;
        reference.BotId = botId.ToString("D");
        reference.UserId = conversation.UserId;
        reference.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return TeamsConversationCaptureOutcome.Stored;
    }
}

public sealed class TeamsBotNotificationDeliveryProvider(
    QuestDbContext db,
    ITeamsProactiveTransport transport,
    IOptions<NotificationOptions> options,
    IBotConnectorServiceUrlValidator serviceUrls) : INotificationDeliveryProvider
{
    public async Task<NotificationDeliveryResult> DeliverAsync(NotificationDeliveryRequest request, CancellationToken cancellationToken)
    {
        TeamsResolvedDestination? destination = request.DestinationKey switch
        {
            NotificationDestinations.QuestGeneralAudience => Configured(options.Value.TeamsBot.GeneralDestination),
            NotificationDestinations.QuestManagerAudience => Configured(options.Value.TeamsBot.ManagerDestination),
            _ when request.DestinationType == NotificationDestinations.ParticipantPrivate && request.RecipientParticipantId is Guid recipient => await PrivateAsync(recipient, cancellationToken),
            _ => null
        };
        if (destination is null) return new(NotificationDeliveryOutcome.PermanentFailure, Code: "TeamsDestinationUnavailable", Summary: "No verified Teams destination is available.");
        if (!serviceUrls.IsTrusted(destination.ServiceUrl))
            return new(NotificationDeliveryOutcome.PermanentFailure, Code: "TeamsServiceUrlUntrusted", Summary: "The Teams destination is not trusted.");
        TeamsTransportResult result = await transport.SendAsync(destination, request.Notification, cancellationToken);
        return new(result.Outcome, result.ProviderMessageId, result.Code, result.RetryAfter, result.Summary);
    }

    private TeamsResolvedDestination? Configured(TeamsDestinationOptions destination)
    {
        TeamsBotOptions bot = options.Value.TeamsBot;
        return Guid.TryParse(bot.TenantId, out Guid tenantId)
            && Guid.TryParse(destination.TenantId, out Guid destinationTenant) && destinationTenant == tenantId
            && serviceUrls.TryValidate(destination.ServiceUrl, out Uri? serviceUrl)
            && !string.IsNullOrWhiteSpace(destination.ConversationId)
            ? new(tenantId, serviceUrl!, destination.ConversationId, bot.MicrosoftAppId, null)
            : null;
    }

    private async Task<TeamsResolvedDestination?> PrivateAsync(Guid participantId, CancellationToken ct)
    {
        TeamsBotOptions bot = options.Value.TeamsBot;
        if (!options.Value.PrivateDeliveryEnabled || !Guid.TryParse(bot.TenantId, out Guid tenantId)) return null;
        var reference = await (from identity in db.ParticipantExternalIdentities.AsNoTracking()
            join conversation in db.TeamsConversationReferences.AsNoTracking() on identity.Id equals conversation.ParticipantExternalIdentityId
            where identity.ParticipantId == participantId && identity.Provider == "Entra" && identity.VerifiedAt != null
                && identity.TenantId == tenantId && conversation.IsActive && conversation.TenantId == tenantId
            orderby conversation.UpdatedAt descending
            select new { conversation.ServiceUrl, conversation.ConversationId, conversation.UserId }).FirstOrDefaultAsync(ct);
        return reference is not null && serviceUrls.TryValidate(reference.ServiceUrl, out Uri? serviceUrl)
            ? new(tenantId, serviceUrl!, reference.ConversationId, bot.MicrosoftAppId, reference.UserId)
            : null;
    }
}

public sealed class BotConnectorTeamsProactiveTransport(
    HttpClient client,
    IOptions<NotificationOptions> options,
    IBotConnectorServiceUrlValidator serviceUrls) : ITeamsProactiveTransport
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<TeamsTransportResult> SendAsync(TeamsResolvedDestination destination, RenderedNotification notification, CancellationToken ct)
    {
        if (!Guid.TryParse(options.Value.TeamsBot.TenantId, out Guid tenantId) || destination.TenantId != tenantId || !serviceUrls.IsTrusted(destination.ServiceUrl))
            return new(NotificationDeliveryOutcome.PermanentFailure, Code: "TeamsDestinationUntrusted", Summary: "The Teams destination is not approved.");

        (string? token, TeamsTransportResult? failure) = await AccessTokenAsync(tenantId, ct);
        if (failure is not null) return failure;

        var activity = new
        {
            type = "message",
            attachments = new[] { new { contentType = "application/vnd.microsoft.card.adaptive", content = new { type = "AdaptiveCard", version = "1.5", body = new object[] { new { type = "TextBlock", size = "Medium", weight = "Bolder", text = notification.Title }, new { type = "TextBlock", wrap = true, text = notification.Body } }, actions = new object[] { new { type = "Action.OpenUrl", title = notification.ActionLabel, url = notification.ActionUrl } } } } }
        };
        Uri endpoint = new(destination.ServiceUrl, $"v3/conversations/{Uri.EscapeDataString(destination.ConversationId)}/activities");
        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = JsonContent.Create(activity, options: JsonOptions) };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        HttpResponseMessage response;
        try { response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return Unknown("TeamsDeliveryTimeout"); }
        catch (HttpRequestException) { return Unknown("TeamsTransportAmbiguous"); }
        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                string json = await response.Content.ReadAsStringAsync(ct); string? id = null;
                try { id = JsonDocument.Parse(json).RootElement.TryGetProperty("id", out JsonElement element) ? element.GetString() : null; } catch (JsonException) { }
                return new(NotificationDeliveryOutcome.Delivered, id, "TeamsDelivered");
            }
            TimeSpan? retry = response.Headers.RetryAfter?.Delta;
            if (response.StatusCode == HttpStatusCode.TooManyRequests || response.StatusCode == HttpStatusCode.RequestTimeout || (int)response.StatusCode >= 500)
                return new(NotificationDeliveryOutcome.RetryableFailure, Code: $"TeamsHttp{(int)response.StatusCode}", RetryAfter: retry, Summary: "Teams temporarily rejected delivery.");
            return new(NotificationDeliveryOutcome.PermanentFailure, Code: $"TeamsHttp{(int)response.StatusCode}", Summary: "Teams permanently rejected delivery.");
        }
    }

    private async Task<(string? Token, TeamsTransportResult? Failure)> AccessTokenAsync(Guid tenantId, CancellationToken ct)
    {
        TeamsBotOptions bot = options.Value.TeamsBot;
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://login.microsoftonline.com/{tenantId:D}/oauth2/v2.0/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["client_id"] = bot.MicrosoftAppId, ["client_secret"] = bot.ClientSecret, ["grant_type"] = "client_credentials", ["scope"] = "https://api.botframework.com/.default" })
        };
        HttpResponseMessage response;
        try { response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { return (null, Retry("TeamsAuthenticationTimeout")); }
        catch (HttpRequestException) { return (null, Retry("TeamsAuthenticationUnavailable")); }
        using (response)
        {
            TimeSpan? retryAfter = response.Headers.RetryAfter?.Delta;
            if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                return (null, Retry($"TeamsAuthenticationHttp{(int)response.StatusCode}", retryAfter));
            if (!response.IsSuccessStatusCode)
                return (null, new(NotificationDeliveryOutcome.PermanentFailure, Code: $"TeamsAuthenticationHttp{(int)response.StatusCode}", Summary: "Bot credentials were rejected."));
            try
            {
                using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                string? token = document.RootElement.TryGetProperty("access_token", out JsonElement element) ? element.GetString() : null;
                return string.IsNullOrWhiteSpace(token)
                    ? (null, new(NotificationDeliveryOutcome.PermanentFailure, Code: "TeamsAuthenticationMalformed", Summary: "Bot credential response was invalid."))
                    : (token, null);
            }
            catch (JsonException)
            {
                return (null, new(NotificationDeliveryOutcome.PermanentFailure, Code: "TeamsAuthenticationMalformed", Summary: "Bot credential response was invalid."));
            }
        }
    }

    private static TeamsTransportResult Retry(string code, TimeSpan? retryAfter = null) => new(NotificationDeliveryOutcome.RetryableFailure, Code: code, RetryAfter: retryAfter, Summary: "Bot authentication is temporarily unavailable.");
    private static TeamsTransportResult Unknown(string code) => new(NotificationDeliveryOutcome.DeliveryUnknown, Code: code, Summary: "The Teams delivery outcome is unknown.");
}
