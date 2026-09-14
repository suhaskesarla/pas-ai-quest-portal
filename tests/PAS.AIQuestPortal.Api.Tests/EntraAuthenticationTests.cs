using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using PAS.AIQuestPortal.Api.Authentication;
using PAS.AIQuestPortal.Api.Configuration;
using PAS.AIQuestPortal.Api.Data;
using Xunit;

namespace PAS.AIQuestPortal.Api.Tests;

public sealed class EntraAuthenticationTests : IAsyncLifetime
{
    private static readonly Guid TenantId = Guid.Parse("10000000-0000-4000-8000-000000000001");
    private static readonly Guid ParticipantOid = Guid.Parse("20000000-0000-4000-8000-000000000001");
    private static readonly Guid ManagerOid = Guid.Parse("20000000-0000-4000-8000-000000000002");
    private static readonly Guid InactiveOid = Guid.Parse("20000000-0000-4000-8000-000000000003");
    private static readonly Guid ParticipantId = Guid.Parse("30000000-0000-4000-8000-000000000001");
    private static readonly Guid ManagerId = Guid.Parse("30000000-0000-4000-8000-000000000002");
    private static readonly Guid InactiveId = Guid.Parse("30000000-0000-4000-8000-000000000003");
    private const string Audience = "api://pas-ai-quest-test";
    private const string RequiredScope = "access_as_user";
    private static readonly string Issuer = $"https://login.microsoftonline.com/{TenantId:D}/v2.0";
    private readonly RSA rsa = RSA.Create(2048);
    private readonly string connection;
    private WebApplication app = null!;
    private HttpClient client = null!;

    public EntraAuthenticationTests()
    {
        string basis = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION") ?? "Server=localhost,1433;Database=master;User Id=sa;Password=Local-only-validation-Passw0rd!;TrustServerCertificate=True";
        connection = new SqlConnectionStringBuilder(basis) { InitialCatalog = $"PasAiQuestEntra_{Guid.NewGuid():N}" }.ConnectionString;
    }

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Test" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(Configuration());
        builder.AddQuestAuthentication();
        builder.Services.AddDbContext<QuestDbContext>(options => options.UseSqlServer(connection));
        var key = new RsaSecurityKey(rsa) { KeyId = "entra-test-key" };
        builder.Services.PostConfigure<JwtBearerOptions>(QuestAuthenticationExtensions.EntraBearerScheme, options =>
        {
            var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
            configuration.SigningKeys.Add(key);
            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
        });
        app = builder.Build();
        app.UseAuthentication(); app.UseAuthorization();
        app.MapQuestAuthenticationEndpoints();
        app.MapGet("/test/participant", () => Results.NoContent()).RequireAuthorization(QuestPolicies.Participant);
        app.MapGet("/test/manager", () => Results.NoContent()).RequireAuthorization(QuestPolicies.Manager);

        await using (var scope = app.Services.CreateAsyncScope())
        {
            QuestDbContext db = scope.ServiceProvider.GetRequiredService<QuestDbContext>();
            await db.Database.EnsureCreatedAsync();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            db.Participants.AddRange(
                new Participant { Id = ParticipantId, DisplayName = "Portal Participant", IsActive = true, CreatedAt = now },
                new Participant { Id = ManagerId, DisplayName = "Portal Manager", IsActive = true, CreatedAt = now },
                new Participant { Id = InactiveId, DisplayName = "Inactive Participant", IsActive = false, CreatedAt = now });
            db.ParticipantExternalIdentities.AddRange(
                new ParticipantExternalIdentity { Id = Guid.NewGuid(), ParticipantId = ParticipantId, Provider = AuthenticationModes.Entra, TenantId = TenantId, SubjectId = ParticipantOid, CreatedAt = now, VerifiedAt = now },
                new ParticipantExternalIdentity { Id = Guid.NewGuid(), ParticipantId = ManagerId, Provider = AuthenticationModes.Entra, TenantId = TenantId, SubjectId = ManagerOid, CreatedAt = now, VerifiedAt = now },
                new ParticipantExternalIdentity { Id = Guid.NewGuid(), ParticipantId = InactiveId, Provider = AuthenticationModes.Entra, TenantId = TenantId, SubjectId = InactiveOid, CreatedAt = now, VerifiedAt = now });
            await db.SaveChangesAsync();
        }
        await app.StartAsync();
        client = app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        client.Dispose();
        await using (var scope = app.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<QuestDbContext>().Database.EnsureDeletedAsync();
        await app.DisposeAsync(); rsa.Dispose();
    }

    [Fact]
    public async Task Valid_tokens_resolve_authoritative_participant_and_manager_roles()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        using HttpResponseMessage participantProfile = await GetAsync("/api/auth/me", Token(ParticipantOid, [QuestRoles.Participant], name: "Untrusted token name"));
        Assert.Equal(HttpStatusCode.OK, participantProfile.StatusCode);
        JsonElement profile = await participantProfile.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(profile.GetProperty("isAuthenticated").GetBoolean());
        Assert.Equal(ParticipantId, profile.GetProperty("participantId").GetGuid());
        Assert.Equal("Portal Participant", profile.GetProperty("displayName").GetString());
        Assert.Equal(new string?[] { QuestRoles.Participant }, profile.GetProperty("roles").EnumerateArray().Select(x => x.GetString()).ToArray());
        Assert.Equal(HttpStatusCode.NoContent, (await GetAsync("/test/participant", Token(ParticipantOid, [QuestRoles.Participant]))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync("/test/manager", Token(ParticipantOid, [QuestRoles.Participant]))).StatusCode);

        string manager = Token(ManagerOid, [QuestRoles.Manager]);
        JsonElement managerProfile = await (await GetAsync("/api/auth/me", manager)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(new string?[] { QuestRoles.Manager, QuestRoles.Participant }, managerProfile.GetProperty("roles").EnumerateArray().Select(x => x.GetString()).ToArray());
        Assert.Equal(HttpStatusCode.NoContent, (await GetAsync("/test/participant", manager)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await GetAsync("/test/manager", manager)).StatusCode);
    }

    [Fact]
    public async Task Valid_unprovisioned_user_is_authenticated_without_portal_roles_and_login_is_read_only()
    {
        Guid unknown = Guid.NewGuid();
        using HttpResponseMessage response = await GetAsync("/api/auth/me", Token(unknown, [QuestRoles.Manager], email: "same@example.test"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement profile = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(profile.GetProperty("isAuthenticated").GetBoolean());
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("participantId").ValueKind);
        Assert.Empty(profile.GetProperty("roles").EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync("/test/manager", Token(unknown, [QuestRoles.Manager]))).StatusCode);
        await using var scope = app.Services.CreateAsyncScope();
        QuestDbContext db = scope.ServiceProvider.GetRequiredService<QuestDbContext>();
        Assert.Equal(3, await db.ParticipantExternalIdentities.CountAsync());
        Assert.Equal(3, await db.Participants.CountAsync());
    }

    [Fact]
    public async Task Inactive_mapped_participant_is_forbidden_and_demo_endpoints_are_absent()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync("/api/auth/me", Token(InactiveOid, [QuestRoles.Participant]))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/auth/demo/profiles")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/auth/demo/session", new { profileKey = "manager" })).StatusCode);
    }

    [Theory]
    [InlineData("wrong-audience")]
    [InlineData("wrong-issuer")]
    [InlineData("wrong-tenant")]
    [InlineData("expired")]
    [InlineData("missing-oid")]
    [InlineData("missing-tid")]
    [InlineData("app-only")]
    [InlineData("bad-signature")]
    [InlineData("malformed")]
    public async Task Invalid_or_non_user_access_tokens_are_rejected(string scenario)
    {
        string token = scenario switch
        {
            "wrong-audience" => Token(ParticipantOid, [QuestRoles.Participant], audience: "api://other"),
            "wrong-issuer" => Token(ParticipantOid, [QuestRoles.Participant], issuer: "https://issuer.invalid/v2.0"),
            "wrong-tenant" => Token(ParticipantOid, [QuestRoles.Participant], tenantId: Guid.NewGuid()),
            "expired" => Token(ParticipantOid, [QuestRoles.Participant], expires: DateTime.UtcNow.AddMinutes(-10), notBefore: DateTime.UtcNow.AddMinutes(-20)),
            "missing-oid" => Token(null, [QuestRoles.Participant]),
            "missing-tid" => Token(ParticipantOid, [QuestRoles.Participant], includeTenant: false),
            "app-only" => Token(ParticipantOid, [QuestRoles.Manager], includeScope: false),
            "bad-signature" => Token(ParticipantOid, [QuestRoles.Participant], signingKey: new RsaSecurityKey(RSA.Create(2048))),
            _ => "not-a-jwt"
        };
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync("/api/auth/me", token)).StatusCode);
    }

    [Fact]
    public async Task Unknown_roles_and_caller_headers_cannot_elevate_authorization()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/test/manager");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token(ParticipantOid, ["Admin", "Quest.SuperUser"]));
        request.Headers.Add("X-Role", QuestRoles.Manager);
        request.Headers.Add("X-ParticipantId", ManagerId.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Signed_external_participant_capability_cannot_provision_or_authorize_user()
    {
        Guid unprovisionedOid = Guid.NewGuid();
        string attackToken = Token(unprovisionedOid, [], additionalClaims:
            [new Claim(QuestClaimTypes.Capability, QuestRoles.Participant)]);

        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync("/test/participant", attackToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync("/test/manager", attackToken)).StatusCode);

        using HttpResponseMessage response = await GetAsync("/api/auth/me", attackToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement profile = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(profile.GetProperty("isAuthenticated").GetBoolean());
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("participantId").ValueKind);
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("displayName").ValueKind);
        Assert.Empty(profile.GetProperty("roles").EnumerateArray());

        await using var scope = app.Services.CreateAsyncScope();
        QuestDbContext db = scope.ServiceProvider.GetRequiredService<QuestDbContext>();
        Assert.Equal(3, await db.Participants.CountAsync());
        Assert.Equal(3, await db.ParticipantExternalIdentities.CountAsync());
        Assert.False(await db.ParticipantExternalIdentities.AnyAsync(x => x.Provider == AuthenticationModes.Entra && x.TenantId == TenantId && x.SubjectId == unprovisionedOid));
    }

    [Fact]
    public async Task Signed_external_claims_cannot_forge_pas_identity_capabilities_or_resolution_state()
    {
        Guid unprovisionedOid = Guid.NewGuid();
        Claim[] injected =
        [
            new(QuestClaimTypes.ParticipantId, ManagerId.ToString("D")),
            new(QuestClaimTypes.Capability, QuestRoles.Manager),
            new(ClaimTypes.Role, QuestRoles.Manager),
            new(QuestClaimTypes.ResolutionComplete, "true")
        ];
        string attackToken = Token(unprovisionedOid, [], additionalClaims: injected);
        JsonElement profile = await (await GetAsync("/api/auth/me", attackToken)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(profile.GetProperty("isAuthenticated").GetBoolean());
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("participantId").ValueKind);
        Assert.Empty(profile.GetProperty("roles").EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync("/test/participant", attackToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync("/test/manager", attackToken)).StatusCode);

        string provisionedWithFakeMarker = Token(ParticipantOid, [], additionalClaims: [new Claim(QuestClaimTypes.ResolutionComplete, "true")]);
        JsonElement resolved = await (await GetAsync("/api/auth/me", provisionedWithFakeMarker)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(ParticipantId, resolved.GetProperty("participantId").GetGuid());
        Assert.Equal(new string?[] { QuestRoles.Participant }, resolved.GetProperty("roles").EnumerateArray().Select(x => x.GetString()).ToArray());
    }

    [Fact]
    public async Task Jwt_challenge_does_not_disclose_validation_or_configuration_details()
    {
        string token = Token(ParticipantOid, [QuestRoles.Participant], audience: "api://wrong-audience");
        using HttpResponseMessage response = await GetAsync("/api/auth/me", token);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        string disclosed = string.Join("\n", response.Headers.SelectMany(x => x.Value).Append(await response.Content.ReadAsStringAsync()));
        Assert.DoesNotContain(token, disclosed, StringComparison.Ordinal);
        Assert.DoesNotContain(TenantId.ToString("D"), disclosed, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Issuer, disclosed, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Audience, disclosed, StringComparison.Ordinal);
        Assert.DoesNotContain("SecurityToken", disclosed, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("invalid_token", disclosed, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Entra_and_demo_configuration_fail_closed()
    {
        QuestAuthenticationOptions missing = new() { Mode = AuthenticationModes.Entra };
        Assert.Contains("TenantId", Assert.Throws<InvalidOperationException>(() => QuestAuthenticationStartupValidator.Validate(missing, "Production")).Message);
        QuestAuthenticationOptions demo = new() { Mode = AuthenticationModes.Demo, Demo = new() { AllowedEnvironments = ["Development"], Profiles = [new DemoProfileOptions { Key = "p", Label = "p", Subject = "demo:pas-ai-quest:p", DisplayName = "p", ParticipantId = Guid.NewGuid(), Roles = [QuestRoles.Participant] }] } };
        Assert.Throws<InvalidOperationException>(() => QuestAuthenticationStartupValidator.Validate(demo, "Production"));
    }

    private async Task<HttpResponseMessage> GetAsync(string path, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private string Token(Guid? oid, string[] roles, string? audience = null, string? issuer = null, Guid? tenantId = null,
        DateTime? expires = null, DateTime? notBefore = null, bool includeTenant = true, SecurityKey? signingKey = null,
        string? name = null, string? email = null, bool includeScope = true, IEnumerable<Claim>? additionalClaims = null)
    {
        var claims = new List<Claim>();
        if (includeTenant) claims.Add(new Claim("tid", (tenantId ?? TenantId).ToString("D")));
        if (oid.HasValue) claims.Add(new Claim("oid", oid.Value.ToString("D")));
        if (name is not null) claims.Add(new Claim("name", name));
        if (email is not null) claims.Add(new Claim("email", email));
        if (includeScope) claims.Add(new Claim("scp", RequiredScope));
        claims.AddRange(roles.Select(role => new Claim("roles", role)));
        if (additionalClaims is not null) claims.AddRange(additionalClaims);
        SecurityKey key = signingKey ?? new RsaSecurityKey(rsa) { KeyId = "entra-test-key" };
        var jwt = new JwtSecurityToken(issuer ?? Issuer, audience ?? Audience, claims,
            notBefore ?? DateTime.UtcNow.AddMinutes(-1), expires ?? DateTime.UtcNow.AddMinutes(10),
            new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private static Dictionary<string, string?> Configuration() => new()
    {
        ["Authentication:Mode"] = AuthenticationModes.Entra,
        ["Authentication:Entra:TenantId"] = TenantId.ToString("D"),
        ["Authentication:Entra:Audience"] = Audience,
        ["Authentication:Entra:RequiredScope"] = RequiredScope,
        ["Authentication:Entra:AuthorityHost"] = "https://login.microsoftonline.com"
    };
}
