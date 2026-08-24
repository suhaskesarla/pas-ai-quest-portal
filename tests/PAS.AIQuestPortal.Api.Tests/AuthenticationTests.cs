using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using PAS.AIQuestPortal.Api.Authentication;
using PAS.AIQuestPortal.Api.Configuration;
using PAS.AIQuestPortal.Api.Workflow;
using Xunit;

namespace PAS.AIQuestPortal.Api.Tests;

public sealed class AuthenticationTests : IAsyncLifetime
{
    private const string Origin = "http://localhost";
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Test" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(DemoConfiguration());
        builder.AddQuestAuthentication();
        builder.Services.AddSubmissionWorkflow();
        builder.Services.RemoveAll<IQuestIdentityResolver>();
        builder.Services.AddScoped<IQuestIdentityResolver, FakeIdentityResolver>();
        _app = builder.Build();
        _app.UseAuthentication(); _app.UseAuthorization();
        _app.MapQuestAuthenticationEndpoints();
        _app.MapSubmissionWorkflow();
        _app.MapGet("/test/participant", (IQuestCurrentUser user) => Results.Ok(user.Identity.ParticipantId)).RequireAuthorization(QuestPolicies.Participant);
        _app.MapGet("/test/manager", (IQuestCurrentUser user) => Results.Ok(user.Identity.ParticipantId)).RequireAuthorization(QuestPolicies.Manager);
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync() { _client.Dispose(); await _app.DisposeAsync(); }

    [Fact]
    public async Task Participant_authenticates_resolves_id_and_cannot_access_manager()
    {
        string cookie = await CreateSessionAsync("participant");
        using HttpResponseMessage me = await SendAsync(HttpMethod.Get, "/api/auth/me", cookie);
        JsonElement identity = await me.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(identity.GetProperty("isAuthenticated").GetBoolean());
        Assert.Equal(FakeIdentityResolver.ParticipantId, identity.GetProperty("participantId").GetGuid());
        Assert.Equal(new string?[] { QuestRoles.Participant }, identity.GetProperty("roles").EnumerateArray().Select(x => x.GetString()).ToArray());
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/test/participant", cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, "/test/manager", cookie)).StatusCode);
    }

    [Fact]
    public async Task Manager_authenticates_and_does_not_implicitly_satisfy_participant()
    {
        string cookie = await CreateSessionAsync("manager");
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/test/manager", cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, "/test/participant", cookie)).StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_is_401_authenticated_unauthorized_is_403()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/test/manager")).StatusCode);
        string participant = await CreateSessionAsync("participant");
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, "/test/manager", participant)).StatusCode);
    }

    [Fact]
    public async Task Workflow_endpoints_enforce_participant_and_manager_policies_before_business_code()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/challenges/eligible")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/submissions/review-queue")).StatusCode);
        string participant = await CreateSessionAsync("participant");
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, "/api/submissions/review-queue", participant)).StatusCode);
        string manager = await CreateSessionAsync("manager");
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, "/api/challenges/eligible", manager)).StatusCode);
    }

    [Fact]
    public async Task Unknown_disabled_or_missing_participant_profile_is_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await SessionRequestAsync("unknown")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SessionRequestAsync("disabled")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SessionRequestAsync("missing-participant")).StatusCode);
    }

    [Fact]
    public async Task Caller_supplied_identity_headers_cannot_elevate_access()
    {
        string cookie = await CreateSessionAsync("participant");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/test/manager");
        request.Headers.Add("Cookie", cookie); request.Headers.Add("X-Role", QuestRoles.Manager); request.Headers.Add("X-ParticipantId", FakeIdentityResolver.ManagerId.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Session_switching_changes_api_confirmed_identity_and_delete_clears_it()
    {
        string participant = await CreateSessionAsync("participant");
        string manager = await CreateSessionAsync("manager", participant);
        JsonElement me = await (await SendAsync(HttpMethod.Get, "/api/auth/me", manager)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(FakeIdentityResolver.ManagerId, me.GetProperty("participantId").GetGuid());
        using var delete = new HttpRequestMessage(HttpMethod.Delete, "/api/auth/demo/session"); delete.Headers.Add("Origin", Origin); delete.Headers.Add("Cookie", manager);
        HttpResponseMessage response = await _client.SendAsync(delete); Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        string expiredCookie = Cookie(response);
        JsonElement anonymous = await (await SendAsync(HttpMethod.Get, "/api/auth/me", expiredCookie)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(anonymous.GetProperty("isAuthenticated").GetBoolean());
    }

    [Fact]
    public async Task Profiles_return_only_safe_selector_fields_and_session_requires_same_origin()
    {
        JsonElement profiles = await _client.GetFromJsonAsync<JsonElement>("/api/auth/demo/profiles");
        Assert.All(profiles.EnumerateArray(), profile => Assert.Equal(new[] { "key", "label" }, profile.EnumerateObject().Select(x => x.Name).Order().ToArray()));
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.PostAsJsonAsync("/api/auth/demo/session", new { profileKey = "participant", roles = new[] { QuestRoles.Manager } })).StatusCode);
    }

    [Theory]
    [InlineData("", "Development", "required")]
    [InlineData("Unknown", "Development", "Unknown")]
    [InlineData("Entra", "Development", "Step 5B")]
    [InlineData("Demo", "Production", "not allowed")]
    public void Invalid_mode_or_environment_fails_startup(string mode, string environment, string message)
    {
        QuestAuthenticationOptions options = ValidOptions(mode);
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => QuestAuthenticationStartupValidator.Validate(options, environment));
        Assert.Contains(message, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Duplicate_or_invalid_demo_configuration_fails_startup()
    {
        DemoProfileOptions profile = ValidOptions("Demo").Demo.Profiles[0];
        var duplicate = new QuestAuthenticationOptions { Mode = "Demo", Demo = new() { AllowedEnvironments = ["Test"], Profiles = [profile, profile] } };
        Assert.Contains("duplicate", Assert.Throws<InvalidOperationException>(() => QuestAuthenticationStartupValidator.Validate(duplicate, "Test")).Message, StringComparison.OrdinalIgnoreCase);
        var invalidSubject = ValidOptions("Demo"); invalidSubject.Demo.Profiles[0] = new DemoProfileOptions { Key = "x", Label = "x", Subject = "real-user", DisplayName = "x", ParticipantId = Guid.NewGuid(), Roles = [QuestRoles.Participant] };
        Assert.Contains("namespaced", Assert.Throws<InvalidOperationException>(() => QuestAuthenticationStartupValidator.Validate(invalidSubject, "Test")).Message, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> CreateSessionAsync(string profile, string? cookie = null)
    {
        HttpResponseMessage response = await SessionRequestAsync(profile, cookie); Assert.Equal(HttpStatusCode.NoContent, response.StatusCode); return Cookie(response);
    }
    private async Task<HttpResponseMessage> SessionRequestAsync(string profile, string? cookie = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/demo/session") { Content = JsonContent.Create(new { profileKey = profile }) };
        request.Headers.Add("Origin", Origin); if (cookie is not null) request.Headers.Add("Cookie", cookie); return await _client.SendAsync(request);
    }
    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string cookie)
    {
        var request = new HttpRequestMessage(method, path); request.Headers.Add("Cookie", cookie); return await _client.SendAsync(request);
    }
    private static string Cookie(HttpResponseMessage response) => response.Headers.GetValues("Set-Cookie").Single().Split(';')[0];

    private static Dictionary<string, string?> DemoConfiguration() => new()
    {
        ["Authentication:Mode"] = "Demo", ["Authentication:Demo:AllowedEnvironments:0"] = "Test",
        ["Authentication:Demo:Profiles:0:Key"] = "participant", ["Authentication:Demo:Profiles:0:Label"] = "Synthetic participant", ["Authentication:Demo:Profiles:0:Subject"] = "demo:pas-ai-quest:participant", ["Authentication:Demo:Profiles:0:DisplayName"] = "Synthetic Participant", ["Authentication:Demo:Profiles:0:ParticipantId"] = FakeIdentityResolver.ParticipantId.ToString(), ["Authentication:Demo:Profiles:0:Roles:0"] = QuestRoles.Participant,
        ["Authentication:Demo:Profiles:1:Key"] = "manager", ["Authentication:Demo:Profiles:1:Label"] = "Synthetic manager", ["Authentication:Demo:Profiles:1:Subject"] = "demo:pas-ai-quest:manager", ["Authentication:Demo:Profiles:1:DisplayName"] = "Synthetic Manager", ["Authentication:Demo:Profiles:1:ParticipantId"] = FakeIdentityResolver.ManagerId.ToString(), ["Authentication:Demo:Profiles:1:Roles:0"] = QuestRoles.Manager,
        ["Authentication:Demo:Profiles:2:Key"] = "disabled", ["Authentication:Demo:Profiles:2:Label"] = "Disabled", ["Authentication:Demo:Profiles:2:Subject"] = "demo:pas-ai-quest:disabled", ["Authentication:Demo:Profiles:2:DisplayName"] = "Disabled Synthetic", ["Authentication:Demo:Profiles:2:ParticipantId"] = Guid.Parse("33333333-3333-4333-8333-333333333333").ToString(), ["Authentication:Demo:Profiles:2:Roles:0"] = QuestRoles.Participant, ["Authentication:Demo:Profiles:2:Enabled"] = "false",
        ["Authentication:Demo:Profiles:3:Key"] = "missing-participant", ["Authentication:Demo:Profiles:3:Label"] = "Missing", ["Authentication:Demo:Profiles:3:Subject"] = "demo:pas-ai-quest:missing", ["Authentication:Demo:Profiles:3:DisplayName"] = "Missing Synthetic", ["Authentication:Demo:Profiles:3:ParticipantId"] = Guid.Parse("44444444-4444-4444-8444-444444444444").ToString(), ["Authentication:Demo:Profiles:3:Roles:0"] = QuestRoles.Participant
    };

    private static QuestAuthenticationOptions ValidOptions(string mode) => new() { Mode = mode, Demo = new() { AllowedEnvironments = ["Development", "Test"], Profiles = [new DemoProfileOptions { Key = "p", Label = "P", Subject = "demo:pas-ai-quest:p", DisplayName = "P", ParticipantId = Guid.NewGuid(), Roles = [QuestRoles.Participant] }] } };

    private sealed class FakeIdentityResolver : IQuestIdentityResolver
    {
        public static readonly Guid ParticipantId = Guid.Parse("11111111-1111-4111-8111-111111111111"); public static readonly Guid ManagerId = Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa");
        public Task<QuestResolvedIdentity?> ResolveAsync(QuestProviderIdentity providerIdentity, CancellationToken cancellationToken = default) => Task.FromResult<QuestResolvedIdentity?>(providerIdentity.ProfileKey switch
        {
            "participant" when providerIdentity is { Provider: AuthenticationModes.Demo, Subject: "demo:pas-ai-quest:participant" } => new(providerIdentity.Subject, "Synthetic Participant", ParticipantId, [QuestRoles.Participant]),
            "manager" when providerIdentity is { Provider: AuthenticationModes.Demo, Subject: "demo:pas-ai-quest:manager" } => new(providerIdentity.Subject, "Synthetic Manager", ManagerId, [QuestRoles.Manager]),
            _ => null
        });
    }
}
