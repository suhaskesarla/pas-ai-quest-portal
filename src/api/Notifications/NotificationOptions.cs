using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace PAS.AIQuestPortal.Api.Notifications;

public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";
    public bool Enabled { get; set; }
    public string Provider { get; set; } = string.Empty;
    public bool PrivateDeliveryEnabled { get; set; }
    public bool RealUserLeaderboardEnabled { get; set; }
    [Required] public string PortalBaseUrl { get; set; } = "https://localhost";
    [Range(1, 1000)] public int CaptureMaxItems { get; set; } = 100;
    public TeamsBotOptions TeamsBot { get; set; } = new();
}

public sealed class TeamsBotOptions
{
    public string MicrosoftAppId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public TeamsDestinationOptions GeneralDestination { get; set; } = new();
    public TeamsDestinationOptions ManagerDestination { get; set; } = new();
}

public sealed class TeamsDestinationOptions
{
    public string TenantId { get; set; } = string.Empty;
    public string ServiceUrl { get; set; } = string.Empty;
    public string ConversationId { get; set; } = string.Empty;
    public string? TeamId { get; set; }
    public string? ChannelId { get; set; }
}

public sealed class NotificationOptionsValidator(IHostEnvironment environment, IBotConnectorServiceUrlValidator? serviceUrls = null) : IValidateOptions<NotificationOptions>
{
    public ValidateOptionsResult Validate(string? name, NotificationOptions options)
    {
        if (options.Enabled && string.IsNullOrWhiteSpace(options.Provider)) return ValidateOptionsResult.Fail("Notifications:Provider is required when notifications are enabled.");
        if (!string.IsNullOrWhiteSpace(options.Provider) && options.Provider is not ("Capture" or "TeamsBot")) return ValidateOptionsResult.Fail("Notifications:Provider must be Capture or TeamsBot.");
        if (options.Provider == "Capture" && !environment.IsDevelopment() && !environment.IsEnvironment("Test")) return ValidateOptionsResult.Fail("Notifications:Provider=Capture is allowed only in Development/Test.");
        if (!Uri.TryCreate(options.PortalBaseUrl, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https")) return ValidateOptionsResult.Fail("Notifications:PortalBaseUrl must be an absolute HTTP(S) URL.");
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Test") && uri.Scheme != "https") return ValidateOptionsResult.Fail("Notifications:PortalBaseUrl must use HTTPS outside Development/Test.");
        if (options.Enabled && options.Provider == "TeamsBot")
        {
            if (!Guid.TryParse(options.TeamsBot.MicrosoftAppId, out _) || !Guid.TryParse(options.TeamsBot.TenantId, out Guid tenantId) || string.IsNullOrWhiteSpace(options.TeamsBot.ClientSecret))
                return ValidateOptionsResult.Fail("Notifications:TeamsBot requires MicrosoftAppId, TenantId and a configured client credential.");
            IBotConnectorServiceUrlValidator validator = serviceUrls ?? new BotConnectorServiceUrlValidator();
            if (!ValidDestination(options.TeamsBot.GeneralDestination, tenantId, validator) || !ValidDestination(options.TeamsBot.ManagerDestination, tenantId, validator))
                return ValidateOptionsResult.Fail("Notifications:TeamsBot requires trusted General and Manager destinations in the configured tenant.");
        }
        return ValidateOptionsResult.Success;
    }

    private static bool ValidDestination(TeamsDestinationOptions destination, Guid tenantId, IBotConnectorServiceUrlValidator validator) => Guid.TryParse(destination.TenantId, out Guid destinationTenant) && destinationTenant == tenantId
        && validator.TryValidate(destination.ServiceUrl, out _)
        && !string.IsNullOrWhiteSpace(destination.ConversationId);
}
