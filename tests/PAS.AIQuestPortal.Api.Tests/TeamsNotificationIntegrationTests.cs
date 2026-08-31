using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using System.Net;
using System.Net.Http.Headers;
using PAS.AIQuestPortal.Api.Authentication;
using PAS.AIQuestPortal.Api.ChallengeAdministration;
using PAS.AIQuestPortal.Api.Configuration;
using PAS.AIQuestPortal.Api.Data;
using PAS.AIQuestPortal.Api.Notifications;
using PAS.AIQuestPortal.Api.Workflow;
using Xunit;

namespace PAS.AIQuestPortal.Api.Tests;

public sealed class TeamsNotificationIntegrationTests : IAsyncLifetime
{
    private readonly string connection;
    private readonly DateTimeOffset now = new(2026, 8, 31, 2, 0, 0, TimeSpan.Zero);
    private readonly Guid manager = Guid.NewGuid(), claimant = Guid.NewGuid(), beneficiary = Guid.NewGuid(), cycle = Guid.NewGuid(), challenge = Guid.NewGuid(), task = Guid.NewGuid(), participation = Guid.NewGuid();
    private QuestDbContext db = null!;
    private readonly MutableUser user;
    private readonly IOptions<NotificationOptions> notificationOptions = Options.Create(new NotificationOptions { Enabled = true, Provider = "Capture", PrivateDeliveryEnabled = true, PortalBaseUrl = "https://portal.test" });

    public TeamsNotificationIntegrationTests()
    {
        string basis = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION") ?? throw new InvalidOperationException("TEST_SQL_CONNECTION is required.");
        connection = new SqlConnectionStringBuilder(basis) { InitialCatalog = $"PasAiQuestTeamsIntegration_{Guid.NewGuid():N}" }.ConnectionString;
        user = new(manager, QuestRoles.Manager);
    }

    public async Task InitializeAsync()
    {
        db = Context(); await db.Database.MigrateAsync();
        db.Participants.AddRange(Person(manager, "Synthetic Manager"), Person(claimant, "Synthetic Claimant"), Person(beneficiary, "Synthetic Beneficiary"));
        db.Cycles.Add(new Cycle { Id = cycle, Code = "TEAM-NOTIFY", Name = "Teams Cycle", Status = CycleStatus.Active, StartsAt = now.AddDays(-5), EndsAt = now.AddDays(20), CreatedAt = now, CreatedByParticipantId = manager });
        db.CycleEvents.Add(new CycleEvent { Id = Guid.NewGuid(), CycleId = cycle, SequenceNumber = 1, EventType = CycleEventType.Created, ToStatus = CycleStatus.Active, Reason = "Fixture", ActorId = manager, OccurredAt = now });
        foreach (Guid id in new[] { claimant, beneficiary })
        {
            db.CycleParticipants.Add(new CycleParticipant { CycleId = cycle, ParticipantId = id, Status = CycleParticipantStatus.Active, JoinedAt = now });
            db.CycleParticipantEvents.Add(new CycleParticipantEvent { Id = Guid.NewGuid(), CycleId = cycle, ParticipantId = id, SequenceNumber = 1, EventType = CycleParticipantEventType.Enrolled, ToStatus = CycleParticipantStatus.Active, Reason = "Fixture", ActorId = manager, OccurredAt = now });
        }
        db.Challenges.Add(new Challenge { Id = challenge, CycleId = cycle, Name = "Notification Challenge", Description = "Safe public description", Status = ChallengeStatus.Draft, OpenAt = now.AddDays(-1), DueAt = now.AddDays(3), CloseAt = now.AddDays(5), CreatedAt = now, CreatedByParticipantId = manager });
        db.ChallengeTasks.Add(new ChallengeTask { Id = task, ChallengeId = challenge, Name = "Shared notification task", XP = 25, ScoringMode = ScoringMode.WholeTeam, EvidenceRequirement = EvidenceRequirement.Text, SortOrder = 1 });
        db.ChallengeTeamPolicies.Add(new ChallengeTeamPolicy { ChallengeId = challenge, FormationMode = FormationMode.ManagerAssigned, MinMembers = 2, MaxMembers = 2 });
        db.ChallengeParticipations.Add(new ChallengeParticipation { Id = participation, ChallengeId = challenge, CycleId = cycle, CreatedAt = now, CreatedByParticipantId = manager });
        foreach (Guid id in new[] { claimant, beneficiary }) db.ChallengeParticipationMembers.Add(new ChallengeParticipationMember { ChallengeParticipationId = participation, ChallengeId = challenge, CycleId = cycle, ParticipantId = id, JoinedSnapshotAt = now });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
    }

    public async Task DisposeAsync() { await db.DisposeAsync(); await using QuestDbContext cleanup = Context(); await cleanup.Database.EnsureDeletedAsync(); }

    [Fact]
    public async Task All_six_transactional_events_are_enqueued_once_with_privacy_safe_payloads()
    {
        var writer = new NotificationOutboxWriter(db); var clock = new FixedClock(now);
        Challenge current = await db.Challenges.SingleAsync(x => x.Id == challenge);
        var challengeService = new ChallengeAdministrationService(db, user, clock, writer, notificationOptions);
        await challengeService.PublishAsync(challenge, new(Convert.ToBase64String(current.RowVersion)), default);
        await challengeService.PublishAsync(challenge, new(Convert.ToBase64String((await db.Challenges.AsNoTracking().SingleAsync(x => x.Id == challenge)).RowVersion)), default);
        Assert.Single(await db.NotificationOutbox.Where(x => x.EventType == nameof(NotificationEventType.ChallengePublished)).ToListAsync());

        user.Set(claimant, QuestRoles.Participant);
        var workflow = new SubmissionWorkflowService(db, user, clock, notificationWriter: writer, notificationOptions: notificationOptions);
        SubmissionView created = await workflow.CreateAsync(new(challenge, task, participation, [claimant, beneficiary], [new(EvidenceKind.Text, "Evidence", "safe text")], "Participant comment"), default);
        NotificationOutbox submitted = await db.NotificationOutbox.SingleAsync(x => x.EventType == nameof(NotificationEventType.SubmissionSubmitted));
        Assert.Equal(NotificationDestinations.QuestManagerAudience, submitted.DestinationKey); Assert.Contains("beneficiaryCount", submitted.PayloadJson); Assert.DoesNotContain("Synthetic Beneficiary", submitted.PayloadJson); Assert.DoesNotContain("safe text", submitted.PayloadJson);

        user.Set(manager, QuestRoles.Manager);
        SubmissionView needs = await workflow.ReviewAsync(created.Id, new(created.Version, ReviewAction.NeedsEvidence, "Please clarify safely"), default);
        NotificationOutbox needsRow = await db.NotificationOutbox.SingleAsync(x => x.EventType == nameof(NotificationEventType.SubmissionNeedsEvidence));
        Assert.Equal($"participant:{claimant:N}", needsRow.DestinationKey); SubmissionNeedsEvidencePayload needsPayload = System.Text.Json.JsonSerializer.Deserialize<SubmissionNeedsEvidencePayload>(needsRow.PayloadJson, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!; Assert.Equal(now.AddDays(3), needsPayload.EffectiveDeadline);

        user.Set(claimant, QuestRoles.Participant);
        SubmissionView resubmitted = await workflow.ResubmitAsync(created.Id, new(needs.Version, [new(EvidenceKind.Text, "Evidence", "revised private evidence")], "Updated"), default);
        NotificationOutbox resubmittedRow = await db.NotificationOutbox.SingleAsync(x => x.EventType == nameof(NotificationEventType.SubmissionResubmitted));
        Assert.Equal(NotificationDestinations.QuestManagerAudience, resubmittedRow.DestinationKey); Assert.DoesNotContain("revised private evidence", resubmittedRow.PayloadJson);

        user.Set(manager, QuestRoles.Manager);
        SubmissionView approved = await workflow.ReviewAsync(created.Id, new(resubmitted.Version, ReviewAction.Approve, "Approved"), default);
        Assert.Equal(SubmissionStatus.Approved, approved.Status);
        NotificationOutbox[] approvals = await db.NotificationOutbox.Where(x => x.EventType == nameof(NotificationEventType.SubmissionApproved)).OrderBy(x => x.DestinationKey).ToArrayAsync();
        Assert.Equal(2, approvals.Length); Assert.Equal(2, approvals.Select(x => x.DestinationKey).Distinct().Count()); Assert.All(approvals, x => { Assert.Contains("\"awardedXp\":25", x.PayloadJson); Assert.DoesNotContain("Synthetic", x.PayloadJson); });
        Assert.Equal(2, await db.XPEntries.CountAsync(x => x.SubmissionId == created.Id));
        await workflow.ReviewAsync(created.Id, new(approved.Version, ReviewAction.Approve, "Replay"), default);
        Assert.Equal(2, await db.NotificationOutbox.CountAsync(x => x.EventType == nameof(NotificationEventType.SubmissionApproved)));

        user.Set(claimant, QuestRoles.Participant);
        SubmissionView second = await workflow.CreateAsync(new(challenge, task, participation, [claimant, beneficiary], [new(EvidenceKind.Text, "Evidence", "other")], null), default);
        user.Set(manager, QuestRoles.Manager);
        await workflow.ReviewAsync(second.Id, new(second.Version, ReviewAction.Reject, "Not accepted"), default);
        NotificationOutbox rejected = await db.NotificationOutbox.SingleAsync(x => x.EventType == nameof(NotificationEventType.SubmissionRejected));
        Assert.Equal($"participant:{claimant:N}", rejected.DestinationKey); Assert.Contains("Not accepted", rejected.PayloadJson); Assert.Equal(0, await db.XPEntries.CountAsync(x => x.SubmissionId == second.Id));
        Assert.Equal(1, await db.NotificationOutbox.CountAsync(x => x.EventType == nameof(NotificationEventType.SubmissionNeedsEvidence)));
    }

    [Fact]
    public async Task Leaderboard_command_is_server_computed_idempotent_and_demo_privacy_safe()
    {
        Guid requestId = Guid.NewGuid(); var writer = new NotificationOutboxWriter(db);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Authentication:Mode"] = "Demo" }).Build();
        var service = new LeaderboardAnnouncementService(db, user, writer, new PAS.AIQuestPortal.Api.Reporting.IndividualLeaderboardQuery(db), notificationOptions, new EnvironmentStub("Test"), configuration, new FixedClock(now));
        LeaderboardAnnouncementResult first = await service.CreateAsync(cycle, new(requestId), default);
        LeaderboardAnnouncementResult replay = await service.CreateAsync(cycle, new(requestId), default);
        Assert.False(first.Replay); Assert.True(replay.Replay); Assert.Equal(first.EventId, replay.EventId); Assert.Single(await db.NotificationOutbox.Where(x => x.AggregateId == requestId).ToListAsync());
        NotificationOutbox row = await db.NotificationOutbox.SingleAsync(x => x.AggregateId == requestId); Assert.Contains("\"isSynthetic\":true", row.PayloadJson); Assert.Contains("Synthetic Beneficiary", row.PayloadJson); Assert.Contains("Synthetic Claimant", row.PayloadJson);
        WorkflowException conflict = await Assert.ThrowsAsync<WorkflowException>(() => service.CreateAsync(Guid.NewGuid(), new(requestId), default)); Assert.Equal("LeaderboardAnnouncementRequestConflict", conflict.Code);
        await service.CreateAsync(cycle, new(Guid.NewGuid()), default); Assert.Equal(2, await db.NotificationOutbox.CountAsync(x => x.EventType == nameof(NotificationEventType.LeaderboardAnnouncement)));

        var production = new LeaderboardAnnouncementService(db, user, writer, new PAS.AIQuestPortal.Api.Reporting.IndividualLeaderboardQuery(db), notificationOptions, new EnvironmentStub("Production"), new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Authentication:Mode"] = "Entra" }).Build(), new FixedClock(now));
        WorkflowException disabled = await Assert.ThrowsAsync<WorkflowException>(() => production.CreateAsync(cycle, new(Guid.NewGuid()), default)); Assert.Equal("RealUserLeaderboardDisabled", disabled.Code);

        var disabledOptions = Options.Create(new NotificationOptions { Enabled=false, Provider="Capture", PortalBaseUrl="https://portal.test" });
        var replayAfterDisable = new LeaderboardAnnouncementService(db, user, writer, new PAS.AIQuestPortal.Api.Reporting.IndividualLeaderboardQuery(db), disabledOptions, new EnvironmentStub("Production"), new ConfigurationBuilder().Build(), new FixedClock(now));
        LeaderboardAnnouncementResult stableReplay = await replayAfterDisable.CreateAsync(cycle, new(requestId), default);
        Assert.True(stableReplay.Replay); Assert.Equal(first.EventId, stableReplay.EventId); Assert.Single(await db.NotificationOutbox.Where(x=>x.AggregateId==requestId).ToListAsync());
    }

    [Fact]
    public async Task Approval_xp_status_and_notifications_roll_back_together_after_sql_staging()
    {
        var writer=new NotificationOutboxWriter(db); var clock=new FixedClock(now); user.Set(manager,QuestRoles.Manager);
        Challenge current=await db.Challenges.SingleAsync(x=>x.Id==challenge); await new ChallengeAdministrationService(db,user,clock,writer,notificationOptions).PublishAsync(challenge,new(Convert.ToBase64String(current.RowVersion)),default);
        user.Set(claimant,QuestRoles.Participant); var createService=new SubmissionWorkflowService(db,user,clock,notificationWriter:writer,notificationOptions:notificationOptions); SubmissionView created=await createService.CreateAsync(new(challenge,task,participation,[claimant,beneficiary],[new(EvidenceKind.Text,"Evidence","safe")],null),default);
        db.ChangeTracker.Clear(); user.Set(manager,QuestRoles.Manager); var failing=new SubmissionWorkflowService(db,user,clock,preCommitHook:new ThrowingPreCommitHook(),notificationWriter:writer,notificationOptions:notificationOptions);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>failing.ReviewAsync(created.Id,new(created.Version,ReviewAction.Approve,"Approved"),default));
        await using QuestDbContext verify=Context(); Assert.Equal(SubmissionStatus.Submitted,(await verify.Submissions.SingleAsync(x=>x.Id==created.Id)).Status); Assert.Empty(await verify.XPEntries.Where(x=>x.SubmissionId==created.Id).ToListAsync()); Assert.Empty(await verify.NotificationOutbox.Where(x=>x.EventType==nameof(NotificationEventType.SubmissionApproved)&&x.AggregateId==created.Id).ToListAsync());
    }

    [Theory]
    [InlineData(NotificationDeliveryOutcome.Delivered)]
    [InlineData(NotificationDeliveryOutcome.RetryableFailure)]
    [InlineData(NotificationDeliveryOutcome.PermanentFailure)]
    [InlineData(NotificationDeliveryOutcome.DeliveryUnknown)]
    public async Task Teams_provider_maps_transport_results_and_resolves_routes_server_side(NotificationDeliveryOutcome outcome)
    {
        var options = Options.Create(new NotificationOptions { TeamsBot = new TeamsBotOptions { MicrosoftAppId = manager.ToString(), TenantId = cycle.ToString(), GeneralDestination = new() { TenantId = cycle.ToString(), ServiceUrl = "https://smba.trafficmanager.net/amer/", ConversationId = "general-conversation" }, ManagerDestination = new() { TenantId = cycle.ToString(), ServiceUrl = "https://smba.trafficmanager.net/amer/", ConversationId = "manager-conversation" } } });
        var transport = new RecordingTransport(new(outcome, "message-id", "typed-code")); var provider = new TeamsBotNotificationDeliveryProvider(db, transport, options, new BotConnectorServiceUrlValidator());
        NotificationDeliveryResult result = await provider.DeliverAsync(new(Guid.NewGuid(), NotificationEventType.ChallengePublished, NotificationDestinations.ConfiguredAudience, NotificationDestinations.QuestGeneralAudience, null, new("Title", "Body", "Open", "https://portal.test")), default);
        Assert.Equal(outcome, result.Outcome); Assert.Equal("general-conversation", Assert.Single(transport.Destinations).ConversationId);
    }

    [Fact]
    public async Task Teams_private_delivery_requires_verified_identity_and_active_conversation_reference()
    {
        Guid externalId = Guid.NewGuid();
        db.ParticipantExternalIdentities.Add(new ParticipantExternalIdentity { Id = externalId, ParticipantId = claimant, Provider = "Entra", TenantId = cycle, SubjectId = Guid.NewGuid(), CreatedAt = now, VerifiedAt = now });
        db.TeamsConversationReferences.Add(new TeamsConversationReference { Id = Guid.NewGuid(), ParticipantExternalIdentityId = externalId, TenantId = cycle, ServiceUrl = "https://smba.trafficmanager.net/amer/", ConversationId = "private-conversation", BotId = manager.ToString(), UserId = "29:verified-user", IsActive = true, CreatedAt = now, UpdatedAt = now }); await db.SaveChangesAsync();
        var transport = new RecordingTransport(new(NotificationDeliveryOutcome.Delivered, "private-message")); var provider = new TeamsBotNotificationDeliveryProvider(db, transport, Options.Create(new NotificationOptions { PrivateDeliveryEnabled = true, TeamsBot = new TeamsBotOptions { MicrosoftAppId = manager.ToString(), TenantId = cycle.ToString() } }), new BotConnectorServiceUrlValidator());
        NotificationDeliveryResult result = await provider.DeliverAsync(new(Guid.NewGuid(), NotificationEventType.SubmissionApproved, NotificationDestinations.ParticipantPrivate, $"participant:{claimant:N}", claimant, new("Approved", "25 XP", "View", "https://portal.test")), default);
        Assert.Equal(NotificationDeliveryOutcome.Delivered, result.Outcome); TeamsResolvedDestination destination = Assert.Single(transport.Destinations); Assert.Equal("private-conversation", destination.ConversationId); Assert.Equal("29:verified-user", destination.UserId);
        NotificationDeliveryResult missing = await provider.DeliverAsync(new(Guid.NewGuid(), NotificationEventType.SubmissionApproved, NotificationDestinations.ParticipantPrivate, $"participant:{beneficiary:N}", beneficiary, new("Approved", "25 XP", "View", "https://portal.test")), default);
        Assert.Equal(NotificationDeliveryOutcome.PermanentFailure, missing.Outcome); Assert.Equal("TeamsDestinationUnavailable", missing.Code);
    }

    [Theory]
    [InlineData(200, NotificationDeliveryOutcome.Delivered)]
    [InlineData(429, NotificationDeliveryOutcome.RetryableFailure)]
    [InlineData(503, NotificationDeliveryOutcome.RetryableFailure)]
    [InlineData(400, NotificationDeliveryOutcome.PermanentFailure)]
    public async Task Bot_connector_transport_maps_http_outcomes(int status, NotificationDeliveryOutcome expected)
    {
        var handler = new BotHttpHandler((HttpStatusCode)status); var options = Options.Create(new NotificationOptions { TeamsBot = new TeamsBotOptions { MicrosoftAppId = manager.ToString(), TenantId = cycle.ToString(), ClientSecret = "test-only" } });
        var transport = new BotConnectorTeamsProactiveTransport(new HttpClient(handler), options, new BotConnectorServiceUrlValidator());
        TeamsTransportResult result = await transport.SendAsync(new(cycle, new Uri("https://smba.trafficmanager.net/amer/"), "conversation/one", manager.ToString(), null), new("Title", "Body", "Open", "https://portal.test"), default);
        Assert.Equal(expected, result.Outcome); Assert.Equal(2, handler.Calls); Assert.Contains("v3/conversations/conversation%2Fone/activities", handler.LastRequestUri!.AbsoluteUri); Assert.DoesNotContain("client_secret", handler.LastRequestBody ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Bot_connector_ambiguous_send_failure_is_delivery_unknown()
    {
        var handler = new BotHttpHandler(HttpStatusCode.OK, throwOnDelivery: true); var transport = new BotConnectorTeamsProactiveTransport(new HttpClient(handler), Options.Create(new NotificationOptions { TeamsBot = new TeamsBotOptions { MicrosoftAppId = manager.ToString(), TenantId = cycle.ToString(), ClientSecret = "test-only" } }), new BotConnectorServiceUrlValidator());
        TeamsTransportResult result = await transport.SendAsync(new(cycle, new Uri("https://smba.trafficmanager.net/amer/"), "conversation", manager.ToString(), null), new("Title", "Body", "Open", "https://portal.test"), default);
        Assert.Equal(NotificationDeliveryOutcome.DeliveryUnknown, result.Outcome);
    }

    private QuestDbContext Context() => new(new DbContextOptionsBuilder<QuestDbContext>().UseSqlServer(connection).Options);
    private Participant Person(Guid id, string name) => new() { Id = id, DisplayName = name, CreatedAt = now };
    private sealed class MutableUser(Guid id, params string[] roles) : IQuestCurrentUser { public QuestUserIdentity Identity { get; private set; } = new(true, id, "Synthetic", roles); public void Set(Guid next, params string[] nextRoles) => Identity = new(true, next, "Synthetic", nextRoles); }
    private sealed class FixedClock(DateTimeOffset value) : TimeProvider { public override DateTimeOffset GetUtcNow() => value; }
    private sealed class EnvironmentStub(string name) : IHostEnvironment { public string EnvironmentName { get; set; } = name; public string ApplicationName { get; set; } = "Tests"; public string ContentRootPath { get; set; } = "."; public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider(); }
    private sealed class RecordingTransport(TeamsTransportResult result) : ITeamsProactiveTransport { public List<TeamsResolvedDestination> Destinations { get; } = []; public Task<TeamsTransportResult> SendAsync(TeamsResolvedDestination destination, RenderedNotification notification, CancellationToken ct) { Destinations.Add(destination); return Task.FromResult(result); } }
    private sealed class ThrowingPreCommitHook:ISubmissionPreCommitHook { public Task BeforeCommitAsync(Guid submissionId,CancellationToken ct)=>throw new InvalidOperationException("Synthetic rollback after SQL staging."); }
    private sealed class BotHttpHandler(HttpStatusCode deliveryStatus, bool throwOnDelivery = false) : HttpMessageHandler
    {
        public int Calls { get; private set; } public Uri? LastRequestUri { get; private set; } public string? LastRequestBody { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (request.RequestUri!.Host == "login.microsoftonline.com") return new(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"token\"}") };
            LastRequestUri = request.RequestUri; LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            if (throwOnDelivery) throw new HttpRequestException("Ambiguous after send began");
            var response = new HttpResponseMessage(deliveryStatus) { Content = new StringContent(deliveryStatus == HttpStatusCode.OK ? "{\"id\":\"message-id\"}" : "{}") };
            if (deliveryStatus == HttpStatusCode.TooManyRequests) response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(2));
            return response;
        }
    }
}
