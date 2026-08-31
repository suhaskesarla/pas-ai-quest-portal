using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace PAS.AIQuestPortal.Api.Notifications;

public static class NotificationRegistration
{
    public static IServiceCollection AddNotificationFoundation(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IBotConnectorServiceUrlValidator, BotConnectorServiceUrlValidator>();
        services.AddOptions<NotificationOptions>().Bind(configuration.GetSection(NotificationOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<IValidateOptions<NotificationOptions>, NotificationOptionsValidator>();
        services.AddScoped<INotificationOutboxWriter, NotificationOutboxWriter>();
        services.AddScoped<INotificationFreshnessEvaluator, NotificationFreshnessEvaluator>();
        services.AddSingleton<INotificationPrivacyPolicy, NotificationPrivacyPolicy>();
        services.AddSingleton<INotificationDeepLinkBuilder, NotificationDeepLinkBuilder>();
        services.AddSingleton<INotificationRenderer, NotificationRenderer>();
        services.AddSingleton<ICapturedNotificationStore, CapturedNotificationStore>();
        services.AddHttpClient<ITeamsProactiveTransport, BotConnectorTeamsProactiveTransport>();
        services.AddScoped<ITeamsConversationReferenceWriter, TeamsConversationReferenceWriter>();
        services.AddScoped<TeamsBotActivityCaptureService>();
        services.AddScoped<TeamsProvisioningService>();
        services.AddAuthentication().AddJwtBearer(TeamsBotActivityAuthentication.Scheme, jwt =>
        {
            jwt.MetadataAddress = "https://login.botframework.com/v1/.well-known/openidconfiguration";
            jwt.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = "https://api.botframework.com",
                ValidateAudience = true,
                ValidAudience = configuration[$"{NotificationOptions.SectionName}:TeamsBot:MicrosoftAppId"],
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true
            };
        });
        services.AddAuthorization(auth => auth.AddPolicy(TeamsBotActivityAuthentication.Policy,
            policy => policy.AddAuthenticationSchemes(TeamsBotActivityAuthentication.Scheme).RequireAuthenticatedUser()));
        services.AddScoped<INotificationDeliveryProvider>(sp =>
        {
            NotificationOptions options = sp.GetRequiredService<IOptions<NotificationOptions>>().Value;
            return options.Provider switch
            {
                "Capture" => ActivatorUtilities.CreateInstance<CaptureNotificationDeliveryProvider>(sp),
                "TeamsBot" => ActivatorUtilities.CreateInstance<TeamsBotNotificationDeliveryProvider>(sp),
                _ => throw new InvalidOperationException("A notification provider is not configured.")
            };
        });
        services.AddHostedService<NotificationOutboxBackgroundService>();
        return services;
    }

    public static void MapTeamsBotActivityCapture(this WebApplication app)
    {
        NotificationOptions options = app.Services.GetRequiredService<IOptions<NotificationOptions>>().Value;
        if (options.Provider != "TeamsBot" || !Guid.TryParse(options.TeamsBot.MicrosoftAppId, out _) || !Guid.TryParse(options.TeamsBot.TenantId, out _)) return;
        app.MapPost("/api/teams/messages", TeamsBotActivityEndpoints.CaptureAsync)
            .RequireAuthorization(TeamsBotActivityAuthentication.Policy);
    }

    public static void MapNotificationDiagnostics(this WebApplication app)
    {
        NotificationOptions options = app.Services.GetRequiredService<IOptions<NotificationOptions>>().Value;
        if ((app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Test")) && options.Provider == "Capture")
            app.MapGet("/api/dev/notifications/captured", (ICapturedNotificationStore store) => Results.Ok(new { notifications = store.Read() }));
    }
}
