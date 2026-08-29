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
using PAS.AIQuestPortal.Api.Evidence;
using PAS.AIQuestPortal.Api.Reporting;
using PAS.AIQuestPortal.Api.Workflow;
using PAS.AIQuestPortal.Api.ChallengeAdministration;
using PAS.AIQuestPortal.Api.ManualAwards;
using PAS.AIQuestPortal.Api.CycleAdministration;
using Xunit;

namespace PAS.AIQuestPortal.Api.Tests;

public sealed class WorkflowHttpContractTests : IAsyncLifetime
{
    private readonly string connection;
    private readonly ContractClock clock = new(new DateTimeOffset(2026, 8, 10, 10, 0, 0, TimeSpan.Zero));
    private WebApplication app = null!;
    private HttpClient client = null!;
    private readonly Guid claimant = Guid.NewGuid(), beneficiary = Guid.NewGuid(), manager = Guid.NewGuid(), cycle = Guid.NewGuid(), challenge = Guid.NewGuid(), task = Guid.NewGuid(), attachmentTask = Guid.NewGuid(), participation = Guid.NewGuid(), awardCategory = Guid.NewGuid();
    private readonly ContractBlobStore blobs = new();
    private readonly ContractCommitHook commitHook = new();
    private readonly ContractSecurityLogger securityLogger=new();

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
            x.AddPolicy(QuestPolicies.Authenticated, p => p.RequireAuthenticatedUser());
            x.AddPolicy(QuestPolicies.Participant, p => p.RequireAuthenticatedUser().RequireRole(QuestRoles.Participant));
            x.AddPolicy(QuestPolicies.Manager, p => p.RequireAuthenticatedUser().RequireRole(QuestRoles.Manager));
        });
        builder.Services.AddSubmissionWorkflow();
        builder.Services.AddManagerScoresheet();
        builder.Services.AddChallengeAdministration();
        builder.Services.AddManualAwards();
        builder.Services.AddCycleAdministration();
        builder.Services.Configure<StorageOptions>(x => { });
        builder.Services.AddSingleton<IEvidenceMalwareScanner, DeterministicPassThroughEvidenceMalwareScanner>();
        builder.Services.AddSingleton<IEvidenceBlobStore>(blobs);
        builder.Services.AddSingleton<EvidenceAttachmentValidator>();
        builder.Services.AddSingleton<ISubmissionPreCommitHook>(commitHook); builder.Services.AddSingleton<ISubmissionPostCommitHook>(commitHook);
        builder.Services.AddSingleton<ILogger<SubmissionWorkflowService>>(securityLogger);
        builder.Services.RemoveAll<TimeProvider>(); builder.Services.AddSingleton<TimeProvider>(clock);
        app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapSubmissionWorkflow(); app.MapManagerScoresheet(); app.MapChallengeAdministration(); app.MapManualAwards(); app.MapCycleAdministration();
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
        JsonElement challengeJson = Assert.Single(root.EnumerateArray()); JsonElement taskJson = challengeJson.GetProperty("tasks").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == task);
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

    [Fact]
    public async Task Manager_scoresheet_routes_enforce_policy_and_serialize_contract()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/manager/reporting-cycles")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "/api/manager/scoresheet?cycleId=" + cycle, claimant, QuestRoles.Participant)).StatusCode);

        JsonElement cycles = await OkJson(await Send(HttpMethod.Get, "/api/manager/reporting-cycles", manager, QuestRoles.Manager));
        Assert.Equal(cycle, cycles.GetProperty("defaultCycleId").GetGuid());
        JsonElement scoresheet = await OkJson(await Send(HttpMethod.Get, "/api/manager/scoresheet?cycleId=" + cycle, manager, QuestRoles.Manager));
        JsonElement row = scoresheet.GetProperty("rows").EnumerateArray().Single(x => x.GetProperty("participantId").GetGuid() == claimant);
        Assert.Equal(0, row.GetProperty("totalXp").GetInt32());
        JsonElement bySource = row.GetProperty("bySource");
        Assert.Equal(0, bySource.GetProperty("taskApprovalXp").GetInt32());
        Assert.Equal(0, bySource.GetProperty("manualAwardXp").GetInt32());
        Assert.Equal(0, bySource.GetProperty("raidXp").GetInt32());
        JsonElement byEntryType = row.GetProperty("byEntryType");
        Assert.Equal(0, byEntryType.GetProperty("grantXp").GetInt32());
        Assert.Equal(0, byEntryType.GetProperty("reversalXp").GetInt32());
        Assert.Equal(0, byEntryType.GetProperty("correctionXp").GetInt32());
        Assert.Equal(0, byEntryType.GetProperty("netAdjustmentXp").GetInt32());

        using HttpResponseMessage invalid = await Send(HttpMethod.Get, "/api/manager/scoresheet?cycleId=invalid", manager, QuestRoles.Manager);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("InvalidCycleId", (await invalid.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Correction_http_contract_requires_explicit_integer_amount_and_reason()
    {
        JsonElement created = await CreateSubmission();
        JsonElement approved = await OkJson(await SendJson(HttpMethod.Post, $"/api/submissions/{created.GetProperty("id").GetGuid()}/review", manager, QuestRoles.Manager, new { version = created.GetProperty("version").GetString(), action = "Approve" }));
        Guid submissionId = approved.GetProperty("id").GetGuid();
        await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
        QuestDbContext db = scope.ServiceProvider.GetRequiredService<QuestDbContext>();
        Guid grantId = await db.XPEntries.Where(x => x.SubmissionId == submissionId && x.ParticipantId == claimant).Select(x => x.Id).SingleAsync();

        await AssertProblem(await SendJson(HttpMethod.Post, $"/api/manager/xp/{grantId}/corrections", manager, QuestRoles.Manager, new { reason = "missing" }), HttpStatusCode.BadRequest, "InvalidCorrectionAmount");
        await AssertProblem(await SendJson(HttpMethod.Post, $"/api/manager/xp/{grantId}/corrections", manager, QuestRoles.Manager, new { newAmount = 1.5, reason = "decimal" }), HttpStatusCode.BadRequest, "InvalidCorrectionAmount");
        await AssertProblem(await SendJson(HttpMethod.Post, $"/api/manager/xp/{grantId}/corrections", manager, QuestRoles.Manager, new { newAmount = -1, reason = "negative" }), HttpStatusCode.BadRequest, "InvalidCorrectionAmount");
        await AssertProblem(await SendJson(HttpMethod.Post, $"/api/manager/xp/{grantId}/corrections", manager, QuestRoles.Manager, new { newAmount = 0 }), HttpStatusCode.BadRequest, "CorrectionReasonRequired");
        await AssertProblem(await SendJson(HttpMethod.Post, $"/api/manager/xp/{grantId}/corrections", manager, QuestRoles.Manager, new { newAmount = 0, reason = new string('x', 2001) }), HttpStatusCode.BadRequest, "CorrectionReasonTooLong");
        Assert.Equal(HttpStatusCode.Forbidden, (await SendJson(HttpMethod.Post, $"/api/manager/xp/{grantId}/corrections", claimant, QuestRoles.Participant, new { newAmount = 0, reason = "forbidden" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync($"/api/manager/xp/{grantId}/corrections", new { newAmount = 0, reason = "anonymous" })).StatusCode);

        JsonElement correction = await OkJson(await SendJson(HttpMethod.Post, $"/api/manager/xp/{grantId}/corrections", manager, QuestRoles.Manager, new { newAmount = 0, reason = "  Remove award  " }));
        Assert.Equal(-25, correction.GetProperty("amount").GetInt32());
        Assert.Equal("Remove award", correction.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Manual_award_http_contract_validates_binding_authorization_and_replay()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/manager/manual-awards/options?cycleId={cycle}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, $"/api/manager/manual-awards/options?cycleId={cycle}", claimant, QuestRoles.Participant)).StatusCode);
        JsonElement options = await OkJson(await Send(HttpMethod.Get, $"/api/manager/manual-awards/options?cycleId={cycle}", manager, QuestRoles.Manager));
        Assert.Contains(options.GetProperty("participants").EnumerateArray(), x => x.GetProperty("participantId").GetGuid() == claimant);
        Assert.Equal(awardCategory, Assert.Single(options.GetProperty("categories").EnumerateArray()).GetProperty("awardCategoryId").GetGuid());

        Guid requestId = Guid.NewGuid();
        var valid = new { requestId, cycleId = cycle, participantId = claimant, awardCategoryId = awardCategory, amount = 10, reason = "  Great contribution.  " };
        await AssertProblem(await SendJson(HttpMethod.Post, "/api/manager/manual-awards", manager, QuestRoles.Manager, new { requestId, cycleId = cycle, participantId = claimant, awardCategoryId = awardCategory, reason = "missing" }), HttpStatusCode.BadRequest, "InvalidManualAwardAmount");
        await AssertProblem(await SendJson(HttpMethod.Post, "/api/manager/manual-awards", manager, QuestRoles.Manager, new { requestId, cycleId = cycle, participantId = claimant, awardCategoryId = awardCategory, amount = 1.5, reason = "fractional" }), HttpStatusCode.BadRequest, "InvalidManualAwardAmount");
        JsonElement first = await OkJson(await SendJson(HttpMethod.Post, "/api/manager/manual-awards", manager, QuestRoles.Manager, valid));
        JsonElement replay = await OkJson(await SendJson(HttpMethod.Post, "/api/manager/manual-awards", manager, QuestRoles.Manager, valid));
        Assert.Equal(requestId, first.GetProperty("id").GetGuid()); Assert.Equal(first.ToString(), replay.ToString());
        Assert.Equal("Grant", first.GetProperty("entryType").GetString()); Assert.Equal("ManualAward", first.GetProperty("sourceType").GetString()); Assert.Equal("Great contribution.", first.GetProperty("reason").GetString());
        await AssertProblem(await SendJson(HttpMethod.Post, "/api/manager/manual-awards", manager, QuestRoles.Manager, new { requestId, cycleId = cycle, participantId = claimant, awardCategoryId = awardCategory, amount = 11, reason = "Great contribution." }), HttpStatusCode.Conflict, "ManualAwardRequestConflict");
    }

    [Fact]
    public async Task Cycle_administration_http_contract_serializes_versions_actions_and_policy()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/manager/cycles")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "/api/manager/cycles", claimant, QuestRoles.Participant)).StatusCode);
        JsonElement created = await OkJson(await SendJson(HttpMethod.Post, "/api/manager/cycles", manager, QuestRoles.Manager, new { code = "HTTP-CYCLE", name = "HTTP Cycle", startsAt = clock.UtcNow, endsAt = clock.UtcNow.AddMonths(1) }));
        Assert.Equal("Active", created.GetProperty("status").GetString()); Assert.NotEmpty(Convert.FromBase64String(created.GetProperty("version").GetString()!)); Assert.True(created.GetProperty("allowedActions").GetProperty("canEdit").GetBoolean());
        Guid id = created.GetProperty("id").GetGuid();
        JsonElement enrolled = await OkJson(await SendJson(HttpMethod.Post, $"/api/manager/cycles/{id}/participants", manager, QuestRoles.Manager, new { participantId = claimant, reason = "Enroll" }));
        Assert.Equal("Active", enrolled.GetProperty("status").GetString()); Assert.NotEmpty(Convert.FromBase64String(enrolled.GetProperty("version").GetString()!));
        await AssertProblem(await SendJson(HttpMethod.Post, $"/api/manager/cycles/{id}/participants/{claimant}/status", manager, QuestRoles.Manager, new { version = enrolled.GetProperty("version").GetString(), status = "Active", reason = "No-op" }), HttpStatusCode.Conflict, "CycleParticipantTransitionNotAllowed");
    }

    [Fact]
    public async Task Manager_challenge_wire_contract_serializes_base64_version_and_enforces_manager_policy()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(HttpMethod.Get, "/api/manager/challenge-options", claimant, QuestRoles.Participant)).StatusCode);
        using HttpResponseMessage response = await SendJson(HttpMethod.Post, "/api/manager/challenges", manager, QuestRoles.Manager, new
        {
            cycleId = cycle, name = "HTTP Draft", description = (string?)null, category = (string?)null,
            openAt = clock.UtcNow.AddHours(1), dueAt = clock.UtcNow.AddDays(1), closeAt = clock.UtcNow.AddDays(2), heroImageReference = (string?)null,
            tasks = new[] { new { id = (Guid?)null, name = "Draft Task", description = (string?)null, xp = 0, scoringMode = "Individual", evidenceRequirement = "None", sortOrder = 1 } },
            participationPolicy = (object?)null
        });
        JsonElement created = await OkJson(response);
        Assert.Equal("Draft", created.GetProperty("status").GetString());
        Assert.NotEmpty(Convert.FromBase64String(created.GetProperty("version").GetString()!));
        Assert.Equal(0, Assert.Single(created.GetProperty("tasks").EnumerateArray()).GetProperty("xp").GetInt32());
    }

    [Fact]
    public async Task Multipart_attachment_round_trip_authorizes_content_and_resubmission_appends()
    {
        JsonElement created = await OkJson(await SendMultipart(HttpMethod.Post, "/api/submissions", claimant, QuestRoles.Participant,
            new { challengeId = challenge, taskId = attachmentTask, challengeParticipationId = participation, beneficiaryIds = new[] { claimant, beneficiary }, evidence = new[] { new { kind = "Attachment", label = "Proof", fileKey = "proof-1" } }, comment = "attachment" },
            ("proof-1", "../synthetic.png", "image/png", Png())));
        JsonElement attachment = Assert.Single(created.GetProperty("evidence").EnumerateArray());
        Assert.Equal("Attachment", attachment.GetProperty("kind").GetString()); Assert.Equal("synthetic.png", attachment.GetProperty("originalFileName").GetString());
        Assert.False(attachment.TryGetProperty("blobKey", out _)); Assert.StartsWith("/api/submission-evidence/", attachment.GetProperty("contentUrl").GetString());

        string contentPath = attachment.GetProperty("contentUrl").GetString()!;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(contentPath)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, contentPath, claimant, QuestRoles.Participant)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Get, contentPath, manager, QuestRoles.Manager)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Get, contentPath, beneficiary, QuestRoles.Participant)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Send(HttpMethod.Get, contentPath, Guid.NewGuid(), QuestRoles.Participant)).StatusCode);

        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        JsonElement needs = await OkJson(await SendJson(HttpMethod.Post, $"/api/submissions/{created.GetProperty("id").GetGuid()}/review", manager, QuestRoles.Manager, new { version = created.GetProperty("version").GetString(), action = "NeedsEvidence", comment = "More proof" }));
        int before = blobs.Count;
        using HttpResponseMessage stale = await SendMultipart(HttpMethod.Put, $"/api/submissions/{created.GetProperty("id").GetGuid()}/resubmission", claimant, QuestRoles.Participant,
            new { version = created.GetProperty("version").GetString(), evidence = new[] { new { kind = "Attachment", label = "More", fileKey = "proof-2" } } }, ("proof-2", "more.pdf", "application/pdf", Pdf()));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode); Assert.Equal(before, blobs.Count);

        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        JsonElement resubmitted = await OkJson(await SendMultipart(HttpMethod.Put, $"/api/submissions/{created.GetProperty("id").GetGuid()}/resubmission", claimant, QuestRoles.Participant,
            new { version = needs.GetProperty("version").GetString(), evidence = new[] { new { kind = "Attachment", label = "More", fileKey = "proof-2" } } }, ("proof-2", "more.pdf", "application/pdf", Pdf())));
        Assert.Equal(2, resubmitted.GetProperty("evidence").GetArrayLength()); Assert.Equal(before + 1, blobs.Count);
    }

    [Fact]
    public async Task Multi_file_storage_failure_compensates_and_accepts_no_submission()
    {
        blobs.FailAfter = 1;
        using HttpResponseMessage response = await SendMultipart(HttpMethod.Post, "/api/submissions", claimant, QuestRoles.Participant,
            new { challengeId = challenge, taskId = attachmentTask, challengeParticipationId = participation, beneficiaryIds = new[] { claimant, beneficiary }, evidence = new[] { new { kind = "Attachment", label = "One", fileKey = "one" }, new { kind = "Attachment", label = "Two", fileKey = "two" } } },
            ("one", "one.png", "image/png", Png()), ("two", "two.pdf", "application/pdf", Pdf()));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode); Assert.Equal(0, blobs.Count);Assert.True(blobs.DeleteAttempts>=2);
        await using AsyncServiceScope scope = app.Services.CreateAsyncScope(); Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<QuestDbContext>().Submissions.CountAsync());
    }

    [Fact]
    public async Task Cancellation_still_compensates_and_cleanup_failure_is_critical_log()
    {
        commitHook.CancelBeforeCommit=true;blobs.FailDeletes=true;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>SendMultipart(HttpMethod.Post,"/api/submissions",claimant,QuestRoles.Participant,
            new{challengeId=challenge,taskId=attachmentTask,challengeParticipationId=participation,beneficiaryIds=new[]{claimant,beneficiary},evidence=new[]{new{kind="Attachment",label="Proof",fileKey="proof"}}},("proof","proof.png","image/png",Png())));
        Assert.Equal(3,blobs.DeleteAttempts);Assert.True(securityLogger.CriticalObserved);
    }

    [Fact]
    public async Task Sql_failure_after_upload_compensates_created_blob()
    {
        commitHook.FailBeforeCommit = true;
        await Assert.ThrowsAnyAsync<Exception>(() => SendMultipart(HttpMethod.Post, "/api/submissions", claimant, QuestRoles.Participant,
            new { challengeId = challenge, taskId = attachmentTask, challengeParticipationId = participation, beneficiaryIds = new[] { claimant, beneficiary }, evidence = new[] { new { kind = "Attachment", label = "Proof", fileKey = "proof" } } },
            ("proof", "proof.png", "image/png", Png())));
        Assert.Equal(0, blobs.Count);
        await using AsyncServiceScope scope = app.Services.CreateAsyncScope(); Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<QuestDbContext>().Submissions.CountAsync());
    }

    [Fact]
    public async Task Failure_after_commit_does_not_compensate_accepted_blob()
    {
        commitHook.FailAfterCommit=true;
        await Assert.ThrowsAnyAsync<Exception>(()=>SendMultipart(HttpMethod.Post,"/api/submissions",claimant,QuestRoles.Participant,
            new{challengeId=challenge,taskId=attachmentTask,challengeParticipationId=participation,beneficiaryIds=new[]{claimant,beneficiary},evidence=new[]{new{kind="Attachment",label="Proof",fileKey="proof"}}},("proof","proof.png","image/png",Png())));
        Assert.Equal(1,blobs.Count);await using AsyncServiceScope scope=app.Services.CreateAsyncScope();Assert.Equal(1,await scope.ServiceProvider.GetRequiredService<QuestDbContext>().SubmissionEvidence.CountAsync(x=>x.EvidenceKind==EvidenceKind.Attachment));
    }

    [Fact]
    public async Task Multipart_correlation_rejects_missing_and_unexpected_file_parts()
    {
        object missingPayload = new { challengeId = challenge, taskId = attachmentTask, challengeParticipationId = participation, beneficiaryIds = new[] { claimant, beneficiary }, evidence = new[] { new { kind = "Attachment", label = "Proof", fileKey = "missing" } } };
        using HttpResponseMessage missing = await SendMultipart(HttpMethod.Post, "/api/submissions", claimant, QuestRoles.Participant, missingPayload);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode); Assert.Equal("AttachmentFileMissing", (await missing.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        object unexpectedPayload = new { challengeId = challenge, taskId = attachmentTask, challengeParticipationId = participation, beneficiaryIds = new[] { claimant, beneficiary }, evidence = Array.Empty<object>() };
        using HttpResponseMessage unexpected = await SendMultipart(HttpMethod.Post, "/api/submissions", claimant, QuestRoles.Participant, unexpectedPayload, ("surprise", "proof.png", "image/png", Png()));
        Assert.Equal(HttpStatusCode.BadRequest, unexpected.StatusCode); Assert.Equal("AttachmentFileUnexpected", (await unexpected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Multipart_payload_accepts_browser_blob_disposition_and_never_enters_file_correlation()
    {
        object payload = new { challengeId = challenge, taskId = attachmentTask, challengeParticipationId = participation, beneficiaryIds = new[] { claimant, beneficiary }, evidence = new[] { new { kind = "Attachment", label = "Proof", fileKey = "proof" } } };
        using HttpResponseMessage response = await SendMultipartRaw(HttpMethod.Post, "/api/submissions", claimant, QuestRoles.Participant,
            JsonSerializer.Serialize(payload), "application/json", "blob", false, ("proof", "proof.png", "image/png", Png()));

        JsonElement created = await OkJson(response);
        JsonElement evidence = Assert.Single(created.GetProperty("evidence").EnumerateArray());
        Assert.Equal("proof.png", evidence.GetProperty("originalFileName").GetString());
        Assert.Equal(1, blobs.Count);
    }

    [Fact]
    public async Task Multipart_payload_rejects_duplicate_wrong_type_malformed_and_oversized_parts()
    {
        string valid = JsonSerializer.Serialize(new { challengeId = challenge, taskId = attachmentTask, challengeParticipationId = participation, beneficiaryIds = new[] { claimant, beneficiary }, evidence = Array.Empty<object>() });

        using HttpResponseMessage duplicate = await SendMultipartRaw(HttpMethod.Post, "/api/submissions", claimant, QuestRoles.Participant, valid, "application/json", "blob", true);
        await AssertProblem(duplicate, HttpStatusCode.BadRequest, "InvalidMultipartEvidence");

        using HttpResponseMessage wrongType = await SendMultipartRaw(HttpMethod.Post, "/api/submissions", claimant, QuestRoles.Participant, valid, "text/plain", "blob", false);
        await AssertProblem(wrongType, HttpStatusCode.BadRequest, "InvalidMultipartEvidence");

        using HttpResponseMessage malformed = await SendMultipartRaw(HttpMethod.Post, "/api/submissions", claimant, QuestRoles.Participant, "{not-json", "application/json", "blob", false);
        await AssertProblem(malformed, HttpStatusCode.BadRequest, "InvalidMultipartEvidence");

        using HttpResponseMessage oversized = await SendMultipartRaw(HttpMethod.Post, "/api/submissions", claimant, QuestRoles.Participant, $"{{\"padding\":\"{new string('x', 1024 * 1024)}\"}}", "application/json", "blob", false);
        await AssertProblem(oversized, HttpStatusCode.RequestEntityTooLarge, "InvalidMultipartEvidence");
    }

    private async Task<JsonElement> CreateSubmission() => await OkJson(await SendJson(HttpMethod.Post, "/api/submissions", claimant, QuestRoles.Participant, new { challengeId = challenge, taskId = task, challengeParticipationId = participation, beneficiaryIds = new[] { claimant, beneficiary }, evidence = new[] { new { kind = "Text", label = "Evidence", value = "initial evidence" } }, comment = "claim" }));
    private async Task<JsonElement> Mine() => Assert.Single((await OkJson(await Send(HttpMethod.Get, "/api/submissions/mine", claimant, QuestRoles.Participant))).EnumerateArray());
    private async Task<HttpResponseMessage> SendJson(HttpMethod method, string path, Guid id, string role, object body) { using var request = Request(method, path, id, role); request.Content = JsonContent.Create(body); return await client.SendAsync(request); }
    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, Guid id, string role) { using var request = Request(method, path, id, role); return await client.SendAsync(request); }
    private async Task<HttpResponseMessage> SendMultipart(HttpMethod method, string path, Guid id, string role, object payload, params (string Key, string Name, string Mime, byte[] Content)[] files)
        => await SendMultipartRaw(method, path, id, role, JsonSerializer.Serialize(payload), "application/json", null, false, files);
    private async Task<HttpResponseMessage> SendMultipartRaw(HttpMethod method, string path, Guid id, string role, string payload, string payloadMime, string? payloadFileName, bool duplicatePayload, params (string Key, string Name, string Mime, byte[] Content)[] files)
    {
        using var request = Request(method, path, id, role); var content = new MultipartFormDataContent();
        AddPayload(content, payload, payloadMime, payloadFileName);
        if (duplicatePayload) AddPayload(content, payload, "application/json", null);
        foreach (var file in files) { var part = new ByteArrayContent(file.Content); part.Headers.ContentType = new(file.Mime); content.Add(part, file.Key, file.Name); }
        request.Content = content; return await client.SendAsync(request);
    }
    private static void AddPayload(MultipartFormDataContent content, string payload, string mime, string? fileName)
    {
        var part = new StringContent(payload, System.Text.Encoding.UTF8, mime);
        if (fileName is null) content.Add(part, "payload"); else content.Add(part, "payload", fileName);
    }
    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }
    private static HttpRequestMessage Request(HttpMethod method, string path, Guid id, string role) { var request = new HttpRequestMessage(method, path); request.Headers.Add("X-Contract-Participant", id.ToString()); request.Headers.Add("X-Contract-Role", role); return request; }
    private static async Task<JsonElement> OkJson(HttpResponseMessage response) { using (response) { Assert.Equal(HttpStatusCode.OK, response.StatusCode); return await response.Content.ReadFromJsonAsync<JsonElement>(); } }

    private async Task Seed(QuestDbContext db)
    {
        DateTimeOffset now = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        db.Participants.AddRange(new Participant { Id = claimant, DisplayName = "Contract Claimant", CreatedAt = now }, new Participant { Id = beneficiary, DisplayName = "Contract Beneficiary", CreatedAt = now }, new Participant { Id = manager, DisplayName = "Contract Manager", CreatedAt = now });
        db.Cycles.Add(new Cycle { Id = cycle, Code = "HTTP-26", Name = "Contract Cycle", Status = CycleStatus.Active, StartsAt = now, EndsAt = now.AddMonths(1), CreatedAt = now, CreatedByParticipantId = manager });
        foreach (Guid participantId in new[] { claimant, beneficiary, manager })
        {
            db.CycleParticipants.Add(new CycleParticipant { CycleId = cycle, ParticipantId = participantId, Status = CycleParticipantStatus.Active, JoinedAt = now });
            db.CycleParticipantEvents.Add(new CycleParticipantEvent { Id = Guid.NewGuid(), CycleId = cycle, ParticipantId = participantId, SequenceNumber = 1, EventType = CycleParticipantEventType.Enrolled, FromStatus = null, ToStatus = CycleParticipantStatus.Active, Reason = "Synthetic HTTP fixture enrollment", ActorId = manager, OccurredAt = now });
        }
        db.Challenges.Add(new Challenge { Id = challenge, CycleId = cycle, Name = "Contract Challenge", Description = "HTTP contract", Category = "Build", Status = ChallengeStatus.Open, OpenAt = now, DueAt = now.AddDays(20), CloseAt = now.AddDays(25), CreatedAt = now, CreatedByParticipantId = manager });
        db.ChallengeTasks.AddRange(new ChallengeTask { Id = task, ChallengeId = challenge, Name = "Contract Task", XP = 25, EvidenceRequirement = EvidenceRequirement.Text, ScoringMode = ScoringMode.WholeTeam, SortOrder = 1 }, new ChallengeTask { Id = attachmentTask, ChallengeId = challenge, Name = "Attachment Task", XP = 25, EvidenceRequirement = EvidenceRequirement.Attachment, ScoringMode = ScoringMode.WholeTeam, SortOrder = 2 });
        db.ChallengeTeamPolicies.Add(new ChallengeTeamPolicy { ChallengeId = challenge, FormationMode = FormationMode.Either, MinMembers = 2, MaxMembers = 4 });
        db.ChallengeParticipations.Add(new ChallengeParticipation { Id = participation, ChallengeId = challenge, CycleId = cycle, CreatedAt = now, CreatedByParticipantId = claimant });
        db.ChallengeParticipationMembers.AddRange(new ChallengeParticipationMember { ChallengeParticipationId = participation, ChallengeId = challenge, CycleId = cycle, ParticipantId = claimant, JoinedSnapshotAt = now }, new ChallengeParticipationMember { ChallengeParticipationId = participation, ChallengeId = challenge, CycleId = cycle, ParticipantId = beneficiary, JoinedSnapshotAt = now });
        db.AwardCategories.Add(new AwardCategory { Id = awardCategory, CycleId = cycle, Code = "BONUS", Name = "Bonus Award", IsActive = true });
        await db.SaveChangesAsync();
    }

    private sealed class ContractClock(DateTimeOffset now) : TimeProvider { public DateTimeOffset UtcNow { get; set; } = now; public override DateTimeOffset GetUtcNow() => UtcNow; }
    private static byte[] Png() => [0x89,0x50,0x4e,0x47,0x0d,0x0a,0x1a,0x0a, 0,0,0,13, 0x49,0x48,0x44,0x52, 0,0,0,1,0,0,0,1,8,2,0,0,0, 0,0,0,0, 0,0,0,0,0x49,0x45,0x4e,0x44,0,0,0,0];
    private static byte[] Pdf()=>"%PDF-1.7\n1 0 obj\n<< /Type /Catalog >>\nendobj\nstartxref\n0\n%%EOF"u8.ToArray();
    private sealed class ContractBlobStore : IEvidenceBlobStore
    {
        private readonly Dictionary<string, byte[]> values = [];
        public int Count => values.Count;
        public int? FailAfter { get; set; }
        public bool FailDeletes{get;set;} public int DeleteAttempts{get;private set;}
        public async Task PutAsync(EvidenceBlobWrite write, CancellationToken ct) { if (values.Count >= FailAfter) throw new IOException("Synthetic storage failure"); using var memory = new MemoryStream(); await write.Content.CopyToAsync(memory, ct); values.Add(write.BlobKey, memory.ToArray()); }
        public Task<EvidenceBlobRead> OpenReadAsync(string key, CancellationToken ct) => Task.FromResult(new EvidenceBlobRead(new MemoryStream(values[key]), "application/octet-stream", values[key].Length));
        public async Task<EvidenceReadAccess> CreateReadAccessAsync(string key, string mime, long size, string name, CancellationToken ct) => new(await OpenReadAsync(key, ct), null, name);
        public Task DeleteUncommittedAsync(string key, CancellationToken ct) {DeleteAttempts++;if(FailDeletes)throw new IOException("Synthetic cleanup failure");values.Remove(key); return Task.CompletedTask; }
    }
    private sealed class ContractCommitHook:ISubmissionPreCommitHook,ISubmissionPostCommitHook
    {
        public bool FailBeforeCommit{get;set;} public bool FailAfterCommit{get;set;} public bool CancelBeforeCommit{get;set;}
        public Task BeforeCommitAsync(Guid id,CancellationToken ct)=>CancelBeforeCommit?Task.FromException(new OperationCanceledException()):FailBeforeCommit?Task.FromException(new InvalidOperationException("Synthetic pre-commit failure")):Task.CompletedTask;
        public Task AfterCommitAsync(Guid id,CancellationToken ct)=>FailAfterCommit?Task.FromException(new InvalidOperationException("Synthetic post-commit failure")):Task.CompletedTask;
    }
    private sealed class ContractSecurityLogger:ILogger<SubmissionWorkflowService>
    {
        public bool CriticalObserved{get;private set;}public IDisposable? BeginScope<TState>(TState state)where TState:notnull=>null;public bool IsEnabled(LogLevel level)=>true;public void Log<TState>(LogLevel level,EventId id,TState state,Exception? error,Func<TState,Exception?,string> formatter){if(level==LogLevel.Critical&&formatter(state,error).Contains("orphan reconciliation",StringComparison.Ordinal))CriticalObserved=true;}
    }
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
