using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PAS.AIQuestPortal.Api.Configuration;
using PAS.AIQuestPortal.Api.Data;

namespace PAS.AIQuestPortal.Api.Authentication;

public static class QuestClaimTypes
{
    public const string ParticipantId = "urn:pas-ai-quest:participant-id";
    public const string DemoProfileKey = "urn:pas-ai-quest:demo-profile-key";
}

public sealed record QuestUserIdentity(bool IsAuthenticated, Guid? ParticipantId, string? DisplayName, IReadOnlyList<string> Roles)
{
    public static QuestUserIdentity Anonymous { get; } = new(false, null, null, []);
}

public interface IQuestCurrentUser { QuestUserIdentity Identity { get; } }

internal sealed class HttpQuestCurrentUser(IHttpContextAccessor accessor) : IQuestCurrentUser
{
    public QuestUserIdentity Identity
    {
        get
        {
            ClaimsPrincipal? principal = accessor.HttpContext?.User;
            if (principal?.Identity?.IsAuthenticated != true || !Guid.TryParse(principal.FindFirstValue(QuestClaimTypes.ParticipantId), out Guid participantId))
                return QuestUserIdentity.Anonymous;
            return new(true, participantId, principal.Identity.Name, principal.FindAll(ClaimTypes.Role).Select(x => x.Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
        }
    }
}

public interface IQuestIdentityResolver
{
    Task<QuestResolvedIdentity?> ResolveAsync(QuestProviderIdentity providerIdentity, CancellationToken cancellationToken = default);
}

public sealed record QuestProviderIdentity(string Provider, string Subject, string? TenantId = null, string? ProfileKey = null);
public sealed record QuestResolvedIdentity(string Subject, string DisplayName, Guid ParticipantId, IReadOnlyList<string> Roles);
public sealed record DemoProfile(string Key, string Label, string Subject, string DisplayName, Guid ParticipantId, IReadOnlyList<string> Roles, bool Enabled);

internal sealed class DemoProfileCatalog
{
    private readonly IReadOnlyDictionary<string, DemoProfile> _profiles;
    public DemoProfileCatalog(IOptions<QuestAuthenticationOptions> options) => _profiles = options.Value.Demo.Profiles.ToDictionary(
        x => x.Key, x => new DemoProfile(x.Key, x.Label, x.Subject, x.DisplayName, x.ParticipantId, x.Roles, x.Enabled), StringComparer.Ordinal);
    public IReadOnlyCollection<DemoProfile> Profiles => _profiles.Values.ToArray();
    public DemoProfile? Find(string key) => _profiles.GetValueOrDefault(key);
}

internal sealed class DemoQuestIdentityResolver(DemoProfileCatalog catalog, QuestDbContext db) : IQuestIdentityResolver
{
    public async Task<QuestResolvedIdentity?> ResolveAsync(QuestProviderIdentity providerIdentity, CancellationToken cancellationToken = default)
    {
        if (providerIdentity.Provider != AuthenticationModes.Demo || providerIdentity.ProfileKey is null) return null;
        DemoProfile? profile = catalog.Find(providerIdentity.ProfileKey);
        if (profile is null || !profile.Enabled || profile.Subject != providerIdentity.Subject) return null;
        bool exists = await db.Participants.AsNoTracking().AnyAsync(x => x.Id == profile.ParticipantId && x.IsActive, cancellationToken);
        return exists ? new(profile.Subject, profile.DisplayName, profile.ParticipantId, profile.Roles) : null;
    }
}

internal sealed class DemoCookieEvents(IQuestIdentityResolver resolver) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        string? profileKey = context.Principal?.FindFirstValue(QuestClaimTypes.DemoProfileKey);
        string? subject = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        QuestResolvedIdentity? identity = profileKey is null || subject is null ? null : await resolver.ResolveAsync(new(AuthenticationModes.Demo, subject, ProfileKey: profileKey), context.HttpContext.RequestAborted);
        if (identity is null || context.Principal?.FindFirstValue(QuestClaimTypes.ParticipantId) != identity.ParticipantId.ToString())
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync();
        }
    }

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context) { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; }
    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context) { context.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; }
}

public static class QuestAuthenticationStartupValidator
{
    public static void Validate(QuestAuthenticationOptions options, string environmentName)
    {
        if (string.IsNullOrWhiteSpace(options.Mode)) throw new InvalidOperationException("Authentication:Mode is required; authentication never falls back to Demo.");
        if (string.Equals(options.Mode, AuthenticationModes.Entra, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Authentication:Mode=Entra is reserved for Step 5B and is not implemented in Step 5A.");
        if (!string.Equals(options.Mode, AuthenticationModes.Demo, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unknown Authentication:Mode '{options.Mode}'. Supported in Step 5A: Demo.");
        if (environmentName is not ("Development" or "Test") || !options.Demo.AllowedEnvironments.Contains(environmentName, StringComparer.Ordinal))
            throw new InvalidOperationException($"Demo authentication is not allowed in environment '{environmentName}'. Explicitly allowlist Development or Test only.");
        if (options.Demo.Profiles.Length == 0) throw new InvalidOperationException("Authentication:Demo:Profiles must contain at least one synthetic profile.");
        ValidateUnique(options.Demo.Profiles.Select(x => x.Key), "profile key");
        ValidateUnique(options.Demo.Profiles.Select(x => x.Subject), "subject");
        ValidateUnique(options.Demo.Profiles.Select(x => x.ParticipantId.ToString()), "ParticipantId");
        foreach (DemoProfileOptions profile in options.Demo.Profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Key) || string.IsNullOrWhiteSpace(profile.Label) || string.IsNullOrWhiteSpace(profile.DisplayName) || profile.ParticipantId == Guid.Empty)
                throw new InvalidOperationException("Every demo profile requires key, label, display name, and ParticipantId.");
            if (!profile.Subject.StartsWith("demo:pas-ai-quest:", StringComparison.Ordinal)) throw new InvalidOperationException($"Demo profile '{profile.Key}' must use a namespaced synthetic subject.");
            if (profile.Roles.Length == 0 || profile.Roles.Any(x => x is not QuestRoles.Participant and not QuestRoles.Manager)) throw new InvalidOperationException($"Demo profile '{profile.Key}' contains missing or unsupported roles.");
        }
    }

    private static void ValidateUnique(IEnumerable<string> values, string description)
    {
        if (values.GroupBy(x => x, StringComparer.Ordinal).Any(x => x.Count() > 1)) throw new InvalidOperationException($"Demo authentication contains a duplicate {description}.");
    }
}

public static class QuestAuthenticationExtensions
{
    public const string DemoCookieScheme = "QuestDemoCookie";

    public static void AddQuestAuthentication(this WebApplicationBuilder builder)
    {
        QuestAuthenticationOptions options = builder.Configuration.GetSection(QuestAuthenticationOptions.SectionName).Get<QuestAuthenticationOptions>() ?? new();
        QuestAuthenticationStartupValidator.Validate(options, builder.Environment.EnvironmentName);

        builder.Services.AddOptions<QuestAuthenticationOptions>().Bind(builder.Configuration.GetSection(QuestAuthenticationOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IQuestCurrentUser, HttpQuestCurrentUser>();
        builder.Services.AddSingleton<DemoProfileCatalog>();
        builder.Services.AddScoped<IQuestIdentityResolver, DemoQuestIdentityResolver>();
        builder.Services.AddScoped<DemoCookieEvents>();
        builder.Services.AddAuthentication(DemoCookieScheme).AddCookie(DemoCookieScheme, cookie =>
        {
            cookie.Cookie.Name = "PasAiQuestDemo";
            cookie.Cookie.HttpOnly = true;
            cookie.Cookie.SameSite = SameSiteMode.Strict;
            cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            cookie.Cookie.Path = "/";
            cookie.SlidingExpiration = false;
            cookie.ExpireTimeSpan = TimeSpan.FromHours(8);
            cookie.EventsType = typeof(DemoCookieEvents);
        });
        builder.Services.AddAuthorization(auth =>
        {
            auth.AddPolicy(QuestPolicies.Authenticated, policy => policy.RequireAuthenticatedUser());
            auth.AddPolicy(QuestPolicies.Participant, policy => policy.RequireAuthenticatedUser().RequireRole(QuestRoles.Participant));
            auth.AddPolicy(QuestPolicies.Manager, policy => policy.RequireAuthenticatedUser().RequireRole(QuestRoles.Manager));
        });
    }

    public static void MapQuestAuthenticationEndpoints(this WebApplication app)
    {
        app.MapGet("/api/auth/me", (IQuestCurrentUser currentUser) => Results.Ok(currentUser.Identity));
        app.MapGet("/api/auth/demo/profiles", (DemoProfileCatalog catalog) => Results.Ok(catalog.Profiles.Where(x => x.Enabled).OrderBy(x => x.Key).Select(x => new { x.Key, x.Label })));
        app.MapPost("/api/auth/demo/session", async (DemoSessionRequest request, HttpContext http, DemoProfileCatalog catalog, IQuestIdentityResolver resolver) =>
        {
            if (!IsSameOrigin(http.Request)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            DemoProfile? profile = string.IsNullOrWhiteSpace(request.ProfileKey) ? null : catalog.Find(request.ProfileKey);
            QuestResolvedIdentity? identity = profile is null ? null : await resolver.ResolveAsync(new(AuthenticationModes.Demo, profile.Subject, ProfileKey: profile.Key), http.RequestAborted);
            if (profile is null || identity is null) return Results.Unauthorized();
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, identity.Subject), new(ClaimTypes.Name, identity.DisplayName),
                new(QuestClaimTypes.ParticipantId, identity.ParticipantId.ToString()), new(QuestClaimTypes.DemoProfileKey, profile.Key)
            };
            claims.AddRange(identity.Roles.Select(x => new Claim(ClaimTypes.Role, x)));
            await http.SignInAsync(DemoCookieScheme, new ClaimsPrincipal(new ClaimsIdentity(claims, DemoCookieScheme, ClaimTypes.Name, ClaimTypes.Role)));
            return Results.NoContent();
        });
        app.MapDelete("/api/auth/demo/session", async (HttpContext http) =>
        {
            if (!IsSameOrigin(http.Request)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            await http.SignOutAsync(DemoCookieScheme);
            return Results.NoContent();
        });
    }

    private static bool IsSameOrigin(HttpRequest request)
    {
        if (!request.Headers.TryGetValue("Origin", out var origin) || !Uri.TryCreate(origin.ToString(), UriKind.Absolute, out Uri? uri)) return false;
        return string.Equals(uri.Scheme, request.Scheme, StringComparison.OrdinalIgnoreCase) && string.Equals(uri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record DemoSessionRequest(string ProfileKey);
