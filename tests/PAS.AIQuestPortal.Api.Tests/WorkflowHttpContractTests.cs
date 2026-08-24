using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PAS.AIQuestPortal.Api.Authentication;
using PAS.AIQuestPortal.Api.Configuration;
using PAS.AIQuestPortal.Api.Data;
using PAS.AIQuestPortal.Api.Workflow;
using Xunit;

namespace PAS.AIQuestPortal.Api.Tests;

public sealed class WorkflowHttpContractTests : IAsyncLifetime
{
    private readonly string connection;
    private readonly ContractClock clock = new(new DateTimeOffset(2026, 8, 10, 10, 0, 0, TimeSpan.Zero));
    private WebApplication app = null!;
    private HttpClient client = null!;
    private readonly Guid claimant = Guid.NewGuid(), beneficiary = Guid.NewGuid(), manager = Guid.NewGuid(), cycle = Guid.NewGuid(), challenge = Guid.NewGuid(), task = Guid.NewGuid(), participation = Guid.NewGuid();

    public WorkflowHttpContractTests()
    {
        string basis = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION") ?? "Server=localhost,1433;Database=master;User Id=sa;Password=Local-only-validation-Passw0rd!;TrustServerCertificate=True";
        connection = new SqlConnectionStringBuilder(basis) { InitialCatalog = $"PasAiQuestHttp_{Guid.NewGuid():N}" }.ConnectionString;
    }

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Test" });
        builder.WebHost.UseTestServer();
        builder.Services.AddDbContext<QuestDbContext>(x => x.UseSqlServer(connection));
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IQuestCurrentUser, ContractCurrentUser>();
        builder.Services.AddAuthentication("Contract").AddScheme<AuthenticationSchemeOptions, ContractAuthenticationHandler>("Contract", null);
        builder.Services.AddAuthorization(x =>
        {
            x.AddPolicy(QuestPolicies.Participant, p => p.RequireAuthenticatedUser().RequireRole(QuestRoles.Participant));
            x.AddPolicy(QuestPolicies.Manager, p => p.RequireAuthenticatedUser().RequireRole(QuestRoles.Manager));
        });
        builder.Services.AddSubmissionWorkflow();
        builder.Services.RemoveAll<TimeProvider>(); builder.Services.AddSingleton<TimeProvider>(clock);
        app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapSubmissionWorkflow();
        await app.StartAsync(); client = app.GetTestClient();
        await using AsyncServiceScope scope = app.Services.CreateAsyncScope(); QuestDbContext db = scope.ServiceProvider.GetRequiredService<QuestDbContext>();
        await db.Database.EnsureCreatedAsync(); await Seed(db);
    }

    public async Task DisposeAsync()
    {
        client.Dispose(); await app.DisposeAsync();
        await using var db = new QuestDbContext(new DbContextOptionsBuilder<QuestDbContext>().UseSqlServer(connection).Options); await db.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task Eligible_challenge_serializes_participation_scoped_contract()
    {
        using HttpResponseMessage response = await Send(HttpMethod.Get, "/api/challenges/eligible", claimant, QuestRoles.Participant);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement root = await response.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement challengeJson = Assert.Single(root.EnumerateArray()); JsonElement taskJson = Assert.Single(challengeJson.GetProperty("tasks").EnumerateArray());
        Assert.Equal("WholeTeam", taskJson.GetProperty("scoringMode").GetString());
        Assert.False(taskJson.TryGetProperty("eligibleBeneficiaries", out _));
        JsonElement option = Assert.Single(taskJson.GetProperty("participations").EnumerateArray());
        Assert.Equal(participation, option.GetProperty("participationId").GetGuid());
        Assert.True(option.GetProperty("claimantIsMember").GetBoolean()); Assert.True(option.GetProperty("requiresCompleteParticipation").GetBoolean()); Assert.False(option.GetProperty("allowsBeneficiarySubset").GetBoolean());
        Assert.Equal(new[] { claimant, beneficiary }.Order(), option.GetProperty("members").EnumerateArray().Select(x => x.GetProperty("participantId").GetGuid()).Order());
        Assert.True(DateTimeOffset.TryParse(challengeJson.GetProperty("effectiveDeadline").GetString(), out _));
    }

    [Fact]
    public async Task React_shaped_requests_cover_needs_evidence_resubmission_approval_rejection_and_problem_contract()
    {
        JsonElement created = await CreateSubmission(); string originalVersion = created.GetProperty("version").GetString()!;
        Assert.Equal(25, created.GetProperty("taskXp").GetInt32());
        Assert.False(created.TryGetProperty("taskXP", out _));
        Assert.True(DateTimeOffset.TryParseExact(originalVersion, "O", null, System.Globalization.DateTimeStyles.RoundtripKind, out _));

        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        JsonElement needs = await OkJson(await SendJson(HttpMethod.Post, $"/api/submissions/{created.GetProperty("id").GetGuid()}/review", manager, QuestRoles.Manager, new { version = originalVersion, action = "NeedsEvidence", comment = "Replace evidence" }));
        Assert.Equal("NeedsEvidence", needs.GetProperty("status").GetString()); string needsVersion = needs.GetProperty("version").GetString()!;
        JsonElement mineNeeds = await Mine();
        Assert.Equal("NeedsEvidence", mineNeeds.GetProperty("status").GetString());
        Assert.Equal("Replace evidence", mineNeeds.GetProperty("managerComment").GetString());
        Assert.Equal(new[] { "Submitted", "UnderReview", "NeedsEvidence" }, mineNeeds.GetProperty("history").EnumerateArray().Select(x => x.GetProperty("eventType").GetString()));

        using HttpResponseMessage stale = await SendJson(HttpMethod.Put, $"/api/submissions/{created.GetProperty("id").GetGuid()}/resubmission", claimant, QuestRoles.Participant, new { version = originalVersion, evidence = new[] { new { kind = "Text", label = "Evidence", value = "replacement" } }, comment = "updated" });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode); JsonElement problem = await stale.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal("SubmissionVersionConflict", problem.GetProperty("code").GetString());

        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        JsonElement resubmitted = await OkJson(await SendJson(HttpMethod.Put, $"/api/submissions/{created.GetProperty("id").GetGuid()}/resubmission", claimant, QuestRoles.Participant, new { version = needsVersion, evidence = new[] { new { kind = "Text", label = "Replacement", value = "current evidence only" } }, comment = "updated" }));
        JsonElement evidence = Assert.Single(resubmitted.GetProperty("evidence").EnumerateArray()); Assert.Equal("current evidence only", evidence.GetProperty("value").GetString());
        Assert.Equal(4, resubmitted.GetProperty("history").GetArrayLength());
        Assert.Equal("Resubmitted", (await Mine()).GetProperty("status").GetString());

        clock.UtcNow = clock.UtcNow.AddMonths(1);
        JsonElement approved = await OkJson(await SendJson(HttpMethod.Post, $"/api/submissions/{created.GetProperty("id").GetGuid()}/review", manager, QuestRoles.Manager, new { version = resubmitted.GetProperty("version").GetString(), action = "Approve" }));
        Assert.Equal("Approved", approved.GetProperty("status").GetString());
        JsonElement mineApproved = await Mine();
        Assert.Equal("Approved", mineApproved.GetProperty("status").GetString());
        Assert.Equal(25, mineApproved.GetProperty("taskXp").GetInt32());

        clock.UtcNow = new(2026, 8, 12, 10, 0, 0, TimeSpan.Zero); JsonElement second = await CreateSubmission(); clock.UtcNow = clock.UtcNow.AddMinutes(1);
        JsonElement rejected = await OkJson(await SendJson(HttpMethod.Post, $"/api/submissions/{second.GetProperty("id").GetGuid()}/review", manager, QuestRoles.Manager, new { version = second.GetProperty("version").GetString(), action = "Reject", comment = "Not acceptable" }));
        Assert.Equal("Rejected", rejected.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Workflow_routes_return_representative_401_and_403()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/challenges/eligible")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "/api/submissions/review-queue", claimant, QuestRoles.Participant)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "/api/challenges/eligible", manager, QuestRoles.Manager)).StatusCode);
    }

    private async Task<JsonElement> CreateSubmission() => await OkJson(await SendJson(HttpMethod.Post, "/api/submissions", claimant, QuestRoles.Participant, new { challengeId = challenge, taskId = task, challengeParticipationId = participation, beneficiaryIds = new[] { claimant, beneficiary }, evidence = new[] { new { kind = "Text", label = "Evidence", value = "initial evidence" } }, comment = "claim" }));
    private async Task<JsonElement> Mine() => Assert.Single((await OkJson(await Send(HttpMethod.Get, "/api/submissions/mine", claimant, QuestRoles.Participant))).EnumerateArray());
    private async Task<HttpResponseMessage> SendJson(HttpMethod method, string path, Guid id, string role, object body) { using var request = Request(method, path, id, role); request.Content = JsonContent.Create(body); return await client.SendAsync(request); }
    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, Guid id, string role) { using var request = Request(method, path, id, role); return await client.SendAsync(request); }
    private static HttpRequestMessage Request(HttpMethod method, string path, Guid id, string role) { var request = new HttpRequestMessage(method, path); request.Headers.Add("X-Contract-Participant", id.ToString()); request.Headers.Add("X-Contract-Role", role); return request; }
    private static async Task<JsonElement> OkJson(HttpResponseMessage response) { using (response) { Assert.Equal(HttpStatusCode.OK, response.StatusCode); return await response.Content.ReadFromJsonAsync<JsonElement>(); } }

    private async Task Seed(QuestDbContext db)
    {
        DateTimeOffset now = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        db.Participants.AddRange(new Participant { Id = claimant, DisplayName = "Contract Claimant", CreatedAt = now }, new Participant { Id = beneficiary, DisplayName = "Contract Beneficiary", CreatedAt = now }, new Participant { Id = manager, DisplayName = "Contract Manager", CreatedAt = now });
        db.Cycles.Add(new Cycle { Id = cycle, Code = "HTTP-26", Name = "Contract Cycle", Status = CycleStatus.Finalised, StartsAt = now, EndsAt = now.AddMonths(1), CreatedAt = now, CreatedByParticipantId = manager });
        db.CycleParticipants.AddRange(new CycleParticipant { CycleId = cycle, ParticipantId = claimant, Status = CycleParticipantStatus.Active }, new CycleParticipant { CycleId = cycle, ParticipantId = beneficiary, Status = CycleParticipantStatus.Active }, new CycleParticipant { CycleId = cycle, ParticipantId = manager, Status = CycleParticipantStatus.Active });
        db.Challenges.Add(new Challenge { Id = challenge, CycleId = cycle, Name = "Contract Challenge", Description = "HTTP contract", Category = "Build", Status = ChallengeStatus.Open, OpenAt = now, DueAt = now.AddDays(20), CloseAt = now.AddDays(25), CreatedAt = now, CreatedByParticipantId = manager });
        db.ChallengeTasks.Add(new ChallengeTask { Id = task, ChallengeId = challenge, Name = "Contract Task", XP = 25, EvidenceRequirement = EvidenceRequirement.Text, ScoringMode = ScoringMode.WholeTeam, SortOrder = 1 });
        db.ChallengeTeamPolicies.Add(new ChallengeTeamPolicy { ChallengeId = challenge, FormationMode = FormationMode.Either, MinMembers = 2, MaxMembers = 4 });
        db.ChallengeParticipations.Add(new ChallengeParticipation { Id = participation, ChallengeId = challenge, CycleId = cycle, CreatedAt = now, CreatedByParticipantId = claimant });
        db.ChallengeParticipationMembers.AddRange(new ChallengeParticipationMember { ChallengeParticipationId = participation, ChallengeId = challenge, CycleId = cycle, ParticipantId = claimant, JoinedSnapshotAt = now }, new ChallengeParticipationMember { ChallengeParticipationId = participation, ChallengeId = challenge, CycleId = cycle, ParticipantId = beneficiary, JoinedSnapshotAt = now });
        await db.SaveChangesAsync();
    }

    private sealed class ContractClock(DateTimeOffset now) : TimeProvider { public DateTimeOffset UtcNow { get; set; } = now; public override DateTimeOffset GetUtcNow() => UtcNow; }
    private sealed class ContractCurrentUser(IHttpContextAccessor accessor) : IQuestCurrentUser
    {
        public QuestUserIdentity Identity { get { ClaimsPrincipal? principal = accessor.HttpContext?.User; return principal?.Identity?.IsAuthenticated == true && Guid.TryParse(principal.FindFirstValue(QuestClaimTypes.ParticipantId), out Guid id) ? new(true, id, principal.Identity.Name, principal.FindAll(ClaimTypes.Role).Select(x => x.Value).ToArray()) : QuestUserIdentity.Anonymous; } }
    }
    private sealed class ContractAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Contract-Participant", out var id) || !Request.Headers.TryGetValue("X-Contract-Role", out var role)) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim(QuestClaimTypes.ParticipantId, id.ToString()), new Claim(ClaimTypes.Name, "Contract identity"), new Claim(ClaimTypes.Role, role.ToString())], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
