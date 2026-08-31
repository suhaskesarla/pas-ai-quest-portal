using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PAS.AIQuestPortal.Api.Data;
using PAS.AIQuestPortal.Api.Notifications;
using Xunit;

namespace PAS.AIQuestPortal.Api.Tests;

public sealed class NotificationFoundationTests : IAsyncLifetime
{
    private readonly string connection;
    private readonly DateTimeOffset now = new(2026, 8, 30, 10, 0, 0, TimeSpan.Zero);
    private QuestDbContext db = null!;
    public NotificationFoundationTests() { string basis = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION") ?? throw new InvalidOperationException("TEST_SQL_CONNECTION is required."); connection = new SqlConnectionStringBuilder(basis) { InitialCatalog = $"PasAiQuestNotifications_{Guid.NewGuid():N}" }.ConnectionString; }
    public async Task InitializeAsync() { db = Context(); await db.Database.MigrateAsync(); }
    public async Task DisposeAsync() { await db.DisposeAsync(); await using QuestDbContext cleanup = Context(); await cleanup.Database.EnsureDeletedAsync(); }

    [Fact]
    public async Task Migration_creates_empty_constrained_outbox_with_required_indexes()
    {
        Assert.Equal(0, await db.NotificationOutbox.CountAsync());
        string[] indexes = await db.Database.SqlQueryRaw<string>("SELECT [name] AS [Value] FROM sys.indexes WHERE object_id=OBJECT_ID('NotificationOutbox') AND [name] IS NOT NULL").ToArrayAsync();
        Assert.Contains("IX_NotificationOutbox_EventId_DestinationType_DestinationKey", indexes); Assert.Contains("IX_NotificationOutbox_Status_NextAttemptAt", indexes);
    }

    [Fact]
    public async Task Writer_versions_payload_and_database_prevents_duplicate_destination_and_invalid_routing()
    {
        Guid participant = Guid.NewGuid(); db.Participants.Add(new Participant { Id = participant, DisplayName = "Synthetic recipient", CreatedAt = now }); await db.SaveChangesAsync();
        var writer = new NotificationOutboxWriter(db); Guid eventId = Guid.NewGuid(), aggregate = Guid.NewGuid(); var payload = new LeaderboardAnnouncementPayload(aggregate, "August", now, true, [new(1, "Synthetic A", 25)]);
        NotificationOutbox row = writer.Enqueue(eventId, NotificationEventType.LeaderboardAnnouncement, NotificationDestinations.General(), "Leaderboard", aggregate, payload, now); await db.SaveChangesAsync();
        Assert.Equal(1, row.PayloadVersion); Assert.Contains("Synthetic A", row.PayloadJson); Assert.Equal(NotificationStatuses.Pending, row.Status);
        writer.Enqueue(eventId, NotificationEventType.LeaderboardAnnouncement, NotificationDestinations.General(), "Leaderboard", aggregate, payload, now);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        Assert.Throws<ArgumentException>(() => writer.Enqueue(Guid.NewGuid(), NotificationEventType.SubmissionApproved, new(NotificationDestinations.ParticipantPrivate, $"participant:{participant:N}", null), "Submission", aggregate, new SubmissionApprovedPayload(aggregate, aggregate, "C", aggregate, "T", aggregate, 25, now), now));
        Assert.Throws<ArgumentException>(() => writer.Enqueue(Guid.NewGuid(), NotificationEventType.LeaderboardAnnouncement, new(NotificationDestinations.ConfiguredAudience, NotificationDestinations.QuestGeneralAudience, participant), "Leaderboard", aggregate, payload, now));
    }

    [Fact]
    public async Task Capture_provider_and_configuration_are_environment_safe()
    {
        Guid tenant = Guid.NewGuid();
        var store = new CapturedNotificationStore(Options.Create(new NotificationOptions { CaptureMaxItems = 100 })); var provider = new CaptureNotificationDeliveryProvider(store, new FixedClock(now), NullLogger<CaptureNotificationDeliveryProvider>.Instance);
        NotificationDeliveryResult result = await provider.DeliverAsync(new(Guid.NewGuid(), NotificationEventType.LeaderboardAnnouncement, NotificationDestinations.ConfiguredAudience, NotificationDestinations.QuestGeneralAudience, null, new("Title", "Safe body", "Open", "https://portal.test/leaderboard")), default);
        Assert.Equal(NotificationDeliveryOutcome.Captured, result.Outcome); Assert.Single(store.Read()); Assert.NotEmpty(result.ProviderMessageId!);
        Assert.False(new NotificationOptionsValidator(new EnvironmentStub("Production")).Validate(null, new() { Provider = "Capture", PortalBaseUrl = "https://portal.test" }).Succeeded);
        Assert.True(new NotificationOptionsValidator(new EnvironmentStub("Test")).Validate(null, new() { Provider = "Capture", PortalBaseUrl = "https://portal.test" }).Succeeded);
        Assert.False(new NotificationOptionsValidator(new EnvironmentStub("Production")).Validate(null, new() { Provider = "TeamsBot", PortalBaseUrl = "http://portal.test" }).Succeeded);
        Assert.True(new NotificationOptionsValidator(new EnvironmentStub("Production")).Validate(null, new() { Enabled=false, Provider = "TeamsBot", PortalBaseUrl = "https://portal.test" }).Succeeded);
        Assert.False(new NotificationOptionsValidator(new EnvironmentStub("Production")).Validate(null, new() { Enabled=true, Provider = "TeamsBot", PortalBaseUrl = "https://portal.test" }).Succeeded);
        Assert.True(new NotificationOptionsValidator(new EnvironmentStub("Production")).Validate(null, new() { Enabled=true, Provider = "TeamsBot", PortalBaseUrl = "https://portal.test", TeamsBot = new() { MicrosoftAppId = Guid.NewGuid().ToString(), TenantId = tenant.ToString(), ClientSecret = "configured-through-secret-store", GeneralDestination = new() { TenantId = tenant.ToString(), ServiceUrl = "https://smba.trafficmanager.net/amer/", ConversationId = "general" }, ManagerDestination = new() { TenantId = tenant.ToString(), ServiceUrl = "https://smba.trafficmanager.net/emea/", ConversationId = "manager" } } }).Succeeded);
        Assert.False(new NotificationOptionsValidator(new EnvironmentStub("Production")).Validate(null, new() { Enabled=true, Provider = "", PortalBaseUrl = "https://portal.test" }).Succeeded);
        Assert.True(new NotificationOptionsValidator(new EnvironmentStub("Production")).Validate(null, new() { Enabled=false, Provider = "", PortalBaseUrl = "https://portal.test" }).Succeeded);
    }

    [Fact]
    public void Capture_store_evicts_oldest_at_configured_bound()
    {
        var store = new CapturedNotificationStore(Options.Create(new NotificationOptions { CaptureMaxItems=2 }));
        foreach (string id in new[]{"first","second","third"}) store.Add(new(id,Guid.NewGuid(),"Event","ConfiguredAudience",id,"Body","Open","https://portal.test",now));
        Assert.Equal(["second","third"], store.Read().Select(x=>x.CaptureId));
    }

    [Fact]
    public void Renderer_handles_all_typed_payloads_without_evidence_or_storage_data()
    {
        var renderer = new NotificationRenderer(new NotificationDeepLinkBuilder(Options.Create(new NotificationOptions { PortalBaseUrl = "https://portal.test/root/" })));
        var cases = new (NotificationEventType Type, object Payload)[]
        {
            (NotificationEventType.ChallengePublished, new ChallengePublishedPayload(Guid.NewGuid(), "Challenge", "Short", now, now.AddDays(1), now.AddDays(2), [new("Task",25)])),
            (NotificationEventType.SubmissionSubmitted, new SubmissionSubmittedPayload(Guid.NewGuid(), "Claimant", Guid.NewGuid(), "Challenge", Guid.NewGuid(), "Task", 2, now)),
            (NotificationEventType.SubmissionResubmitted, new SubmissionResubmittedPayload(Guid.NewGuid(), "Claimant", Guid.NewGuid(), "Challenge", Guid.NewGuid(), "Task", 2, now)),
            (NotificationEventType.SubmissionNeedsEvidence, new SubmissionNeedsEvidencePayload(Guid.NewGuid(), Guid.NewGuid(), "Challenge", Guid.NewGuid(), "Task", now.AddDays(1), "Please clarify")),
            (NotificationEventType.SubmissionApproved, new SubmissionApprovedPayload(Guid.NewGuid(), Guid.NewGuid(), "Challenge", Guid.NewGuid(), "Task", Guid.NewGuid(), 25, now)),
            (NotificationEventType.SubmissionRejected, new SubmissionRejectedPayload(Guid.NewGuid(), Guid.NewGuid(), "Challenge", Guid.NewGuid(), "Task", "Reason", now)),
            (NotificationEventType.LeaderboardAnnouncement, new LeaderboardAnnouncementPayload(Guid.NewGuid(), "August", now, true, [new(1,"Synthetic",25)]))
        };
        foreach ((NotificationEventType type, object payload) in cases)
        {
            string json = System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)); RenderedNotification rendered = renderer.Render(type, 1, json);
            Assert.StartsWith("https://portal.test/", rendered.ActionUrl); string content = rendered.Title + rendered.Body + rendered.ActionUrl; Assert.DoesNotContain("blob", content, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("attachment", content, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("storage", content, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Deep_link_builder_uses_existing_spa_routes_and_standard_guid_segments()
    {
        Guid id=Guid.Parse("11111111-1111-4111-8111-111111111111"); var links=new NotificationDeepLinkBuilder(Options.Create(new NotificationOptions{PortalBaseUrl="https://portal.test/"}));
        Assert.Equal($"https://portal.test/challenges/{id:D}",links.Challenge(id)); Assert.Equal($"https://portal.test/manager/submissions/{id:D}",links.ManagerSubmission(id)); Assert.Equal($"https://portal.test/activity/submissions/{id:D}",links.ParticipantSubmission(id)); Assert.Equal($"https://portal.test/activity/submissions/{id:D}/history",links.SubmissionHistory(id)); Assert.Equal($"https://portal.test/xp-activity?cycleId={id:D}",links.XpActivity(id)); Assert.Equal($"https://portal.test/leaderboard?cycleId={id:D}",links.IndividualLeaderboard(id));
    }

    [Fact]
    public async Task Freshness_suppresses_superseded_actionable_events_but_not_terminal_or_snapshot_events()
    {
        (Guid challenge, Guid submission) = await ArrangeSubmissionAsync(); var evaluator = new NotificationFreshnessEvaluator(db);
        await AssertFresh(evaluator, Row(NotificationEventType.ChallengePublished, challenge), true);
        await AssertFresh(evaluator, Row(NotificationEventType.SubmissionSubmitted, submission), true); await SetSubmissionStatus(submission, SubmissionStatus.NeedsEvidence); await AssertFresh(evaluator, Row(NotificationEventType.SubmissionSubmitted, submission), false); await AssertFresh(evaluator, Row(NotificationEventType.SubmissionNeedsEvidence, submission), true);
        await SetSubmissionStatus(submission, SubmissionStatus.Resubmitted); await AssertFresh(evaluator, Row(NotificationEventType.SubmissionNeedsEvidence, submission), false); await AssertFresh(evaluator, Row(NotificationEventType.SubmissionResubmitted, submission), true);
        Challenge challengeRow = await db.Challenges.SingleAsync(x => x.Id == challenge); challengeRow.Status = ChallengeStatus.Closed; await db.SaveChangesAsync(); db.ChangeTracker.Clear(); await AssertFresh(evaluator, Row(NotificationEventType.ChallengePublished, challenge), false);
        challengeRow = await db.Challenges.SingleAsync(x => x.Id == challenge); challengeRow.Status = ChallengeStatus.Archived; await db.SaveChangesAsync(); db.ChangeTracker.Clear(); await AssertFresh(evaluator, Row(NotificationEventType.ChallengePublished, challenge), false);
        await AssertFresh(evaluator, Row(NotificationEventType.SubmissionApproved, submission), true); await AssertFresh(evaluator, Row(NotificationEventType.SubmissionRejected, submission), true); await AssertFresh(evaluator, Row(NotificationEventType.LeaderboardAnnouncement, Guid.NewGuid()), true);
    }

    [Fact]
    public async Task Worker_claims_once_across_two_workers_and_capture_is_terminal()
    {
        Guid id = await AddLeaderboardOutbox(); var gate = new GatedProvider(NotificationDeliveryResult.Captured("capture-1")); await using ServiceProvider services = Services(gate); var processor = Processor(services);
        Task<bool> first = processor.ProcessOnceAsync(default); await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15)); Task<bool> second = processor.ProcessOnceAsync(default); Assert.False(await second); gate.Release(); Assert.True(await first);
        await using QuestDbContext verify = Context(); NotificationOutbox row = await verify.NotificationOutbox.SingleAsync(x => x.Id == id); Assert.Equal(NotificationStatuses.Captured, row.Status); Assert.Equal(1, row.AttemptCount); Assert.NotNull(row.CompletedAt); Assert.True(gate.Calls == 1);
    }

    [Theory]
    [InlineData(NotificationDeliveryOutcome.RetryableFailure, "RetryPending")]
    [InlineData(NotificationDeliveryOutcome.PermanentFailure, "Failed")]
    [InlineData(NotificationDeliveryOutcome.DeliveryUnknown, "DeliveryUnknown")]
    public async Task Worker_maps_provider_results_and_attempts(NotificationDeliveryOutcome outcome, string expectedStatus)
    {
        Guid id = await AddLeaderboardOutbox(); var provider = new StaticProvider(new(outcome, Code: "ProviderCode", Summary: "bounded")); await using ServiceProvider services = Services(provider); Assert.True(await Processor(services).ProcessOnceAsync(default));
        await using QuestDbContext verify = Context(); NotificationOutbox row = await verify.NotificationOutbox.SingleAsync(x => x.Id == id); Assert.Equal(expectedStatus, row.Status); Assert.Equal(1, row.AttemptCount); Assert.Equal(expectedStatus == NotificationStatuses.RetryPending, row.CompletedAt is null);
    }

    [Fact]
    public async Task Worker_recovers_expired_processing_as_delivery_unknown_without_redelivery()
    {
        Guid id = await AddLeaderboardOutbox(); Guid futureId = await AddLeaderboardOutbox(now.AddHours(1)); NotificationOutbox row = await db.NotificationOutbox.SingleAsync(x => x.Id == id); row.Status = NotificationStatuses.Processing; row.DeliveryPhase = "DeliveryStarted"; row.AttemptCount = 1; row.LeaseId = Guid.NewGuid(); row.LeaseExpiresAt = now.AddMinutes(-1); row.LastAttemptAt = now.AddMinutes(-3); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var provider = new StaticProvider(NotificationDeliveryResult.Captured("must-not-send")); await using ServiceProvider services = Services(provider); Assert.False(await Processor(services).ProcessOnceAsync(default));
        await using QuestDbContext verify = Context(); row = await verify.NotificationOutbox.SingleAsync(x => x.Id == id); Assert.Equal(NotificationStatuses.DeliveryUnknown, row.Status); Assert.Equal(0, provider.Calls); Assert.NotNull(row.CompletedAt); Assert.Equal(NotificationStatuses.Pending, (await verify.NotificationOutbox.SingleAsync(x => x.Id == futureId)).Status);
    }

    [Fact]
    public async Task Worker_recovers_expired_pre_delivery_lease_for_retry_without_delivery_unknown()
    {
        Guid id = await AddLeaderboardOutbox(); NotificationOutbox row = await db.NotificationOutbox.SingleAsync(x=>x.Id==id); row.Status=NotificationStatuses.Processing; row.DeliveryPhase="PreDelivery"; row.AttemptCount=1; row.LeaseId=Guid.NewGuid(); row.LeaseExpiresAt=now.AddMinutes(-1); row.LastAttemptAt=now.AddMinutes(-3); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var provider = new StaticProvider(NotificationDeliveryResult.Captured("must-not-send")); await using ServiceProvider services=Services(provider); Assert.False(await Processor(services).ProcessOnceAsync(default));
        await using QuestDbContext verify=Context(); row=await verify.NotificationOutbox.SingleAsync(x=>x.Id==id); Assert.Equal(NotificationStatuses.RetryPending,row.Status); Assert.Null(row.CompletedAt); Assert.Equal(0,provider.Calls); Assert.Equal(1,row.AttemptCount);
    }

    [Fact]
    public async Task Unexpected_provider_exception_after_delivery_started_becomes_delivery_unknown()
    {
        Guid id=await AddLeaderboardOutbox(); await using ServiceProvider services=Services(new ThrowingProvider()); Assert.True(await Processor(services).ProcessOnceAsync(default));
        await using QuestDbContext verify=Context(); NotificationOutbox row=await verify.NotificationOutbox.SingleAsync(x=>x.Id==id); Assert.Equal(NotificationStatuses.DeliveryUnknown,row.Status); Assert.Equal("UnexpectedProviderException",row.LastErrorCode); Assert.NotNull(row.CompletedAt);
    }

    [Fact]
    public async Task Real_user_leaderboard_is_suppressed_before_render_and_capture_when_privacy_disabled()
    {
        Guid id=await AddLeaderboardOutbox(isSynthetic:false); var provider=new StaticProvider(NotificationDeliveryResult.Captured("must-not-send")); await using ServiceProvider services=Services(provider); Assert.True(await Processor(services).ProcessOnceAsync(default));
        await using QuestDbContext verify=Context(); NotificationOutbox row=await verify.NotificationOutbox.SingleAsync(x=>x.Id==id); Assert.Equal(NotificationStatuses.Suppressed,row.Status); Assert.Equal("RealUserLeaderboardDisabled",row.TerminalReason); Assert.Equal(0,provider.Calls);
    }

    [Fact]
    public async Task Fifth_retryable_attempt_becomes_failed_without_infinite_retry()
    {
        Guid id = await AddLeaderboardOutbox(); NotificationOutbox row = await db.NotificationOutbox.SingleAsync(x => x.Id == id); row.AttemptCount = 4; await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await using ServiceProvider services = Services(new StaticProvider(new(NotificationDeliveryOutcome.RetryableFailure, Code: "StillTransient"))); Assert.True(await Processor(services).ProcessOnceAsync(default));
        await using QuestDbContext verify = Context(); row = await verify.NotificationOutbox.SingleAsync(x => x.Id == id); Assert.Equal(NotificationStatuses.Failed, row.Status); Assert.Equal(5, row.AttemptCount); Assert.NotNull(row.CompletedAt);
    }

    private async Task<Guid> AddLeaderboardOutbox(DateTimeOffset? nextAttemptAt = null, bool isSynthetic = true)
    {
        Guid id = Guid.NewGuid(); db.NotificationOutbox.Add(new NotificationOutbox { Id=id, EventId=Guid.NewGuid(), EventType=NotificationEventType.LeaderboardAnnouncement.ToString(), DestinationType=NotificationDestinations.ConfiguredAudience, DestinationKey=NotificationDestinations.QuestGeneralAudience, AggregateType="Leaderboard", AggregateId=Guid.NewGuid(), PayloadVersion=1, PayloadJson=System.Text.Json.JsonSerializer.Serialize(new LeaderboardAnnouncementPayload(Guid.NewGuid(), "August", now, isSynthetic, [new(1,"Synthetic",25)]), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)), Status=NotificationStatuses.Pending, NextAttemptAt=nextAttemptAt ?? now, CreatedAt=now }); await db.SaveChangesAsync(); db.ChangeTracker.Clear(); return id;
    }
    private ServiceProvider Services(INotificationDeliveryProvider provider)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddDbContext<QuestDbContext>(o=>o.UseSqlServer(connection)); services.AddSingleton<IOptions<NotificationOptions>>(Options.Create(new NotificationOptions { Enabled=true, Provider="Capture", PrivateDeliveryEnabled=true, RealUserLeaderboardEnabled=false, PortalBaseUrl="https://portal.test" })); services.AddScoped<INotificationFreshnessEvaluator, NotificationFreshnessEvaluator>(); services.AddSingleton<INotificationPrivacyPolicy, NotificationPrivacyPolicy>(); services.AddSingleton<INotificationDeepLinkBuilder, NotificationDeepLinkBuilder>(); services.AddSingleton<INotificationRenderer, NotificationRenderer>(); services.AddSingleton(provider); return services.BuildServiceProvider();
    }
    private NotificationOutboxProcessor Processor(ServiceProvider services) => new(services.GetRequiredService<IServiceScopeFactory>(), new FixedClock(now), NullLogger<NotificationOutboxProcessor>.Instance);
    private QuestDbContext Context() => new(new DbContextOptionsBuilder<QuestDbContext>().UseSqlServer(connection).Options);
    private NotificationOutbox Row(NotificationEventType type, Guid aggregate) => new() { EventType=type.ToString(), AggregateId=aggregate };
    private static async Task AssertFresh(INotificationFreshnessEvaluator evaluator, NotificationOutbox row, bool expected) => Assert.Equal(expected, (await evaluator.EvaluateAsync(row, default)).ShouldDeliver);
    private async Task SetSubmissionStatus(Guid id, SubmissionStatus status) { Submission row = await db.Submissions.SingleAsync(x=>x.Id==id); row.Status=status; await db.SaveChangesAsync(); db.ChangeTracker.Clear(); }
    private async Task<(Guid Challenge, Guid Submission)> ArrangeSubmissionAsync()
    {
        Guid manager=Guid.NewGuid(), claimant=Guid.NewGuid(), cycle=Guid.NewGuid(), challenge=Guid.NewGuid(), task=Guid.NewGuid(), submission=Guid.NewGuid(); db.Participants.AddRange(new Participant{Id=manager,DisplayName="Manager",CreatedAt=now},new Participant{Id=claimant,DisplayName="Claimant",CreatedAt=now}); db.Cycles.Add(new Cycle{Id=cycle,Code=$"N-{cycle:N}"[..20],Name="Notification",Status=CycleStatus.Active,StartsAt=now.AddDays(-1),EndsAt=now.AddDays(2),CreatedAt=now,CreatedByParticipantId=manager}); db.CycleEvents.Add(new CycleEvent{Id=Guid.NewGuid(),CycleId=cycle,SequenceNumber=1,EventType=CycleEventType.Created,ToStatus=CycleStatus.Active,Reason="Fixture",ActorId=manager,OccurredAt=now}); db.CycleParticipants.Add(new CycleParticipant{CycleId=cycle,ParticipantId=claimant,Status=CycleParticipantStatus.Active,JoinedAt=now}); db.CycleParticipantEvents.Add(new CycleParticipantEvent{Id=Guid.NewGuid(),CycleId=cycle,ParticipantId=claimant,SequenceNumber=1,EventType=CycleParticipantEventType.Enrolled,ToStatus=CycleParticipantStatus.Active,Reason="Fixture",ActorId=manager,OccurredAt=now}); db.Challenges.Add(new Challenge{Id=challenge,CycleId=cycle,Name="Challenge",Status=ChallengeStatus.Open,OpenAt=now.AddDays(-1),DueAt=now.AddDays(1),CloseAt=now.AddDays(2),CreatedByParticipantId=manager,CreatedAt=now}); db.ChallengeTasks.Add(new ChallengeTask{Id=task,ChallengeId=challenge,Name="Task",XP=25,EvidenceRequirement=EvidenceRequirement.Text,ScoringMode=ScoringMode.Individual,SortOrder=1}); db.Submissions.Add(new Submission{Id=submission,ChallengeId=challenge,TaskId=task,CycleId=cycle,ClaimantId=claimant,Status=SubmissionStatus.Submitted,SubmittedAt=now,LastUpdatedAt=now}); await db.SaveChangesAsync(); db.ChangeTracker.Clear(); return(challenge,submission);
    }
    private sealed class FixedClock(DateTimeOffset value):TimeProvider { public override DateTimeOffset GetUtcNow()=>value; }
    private sealed class EnvironmentStub(string name):IHostEnvironment { public string EnvironmentName{get;set;}=name; public string ApplicationName{get;set;}="Tests"; public string ContentRootPath{get;set;}="."; public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider(); }
    private sealed class StaticProvider(NotificationDeliveryResult result):INotificationDeliveryProvider { public int Calls{get;private set;} public Task<NotificationDeliveryResult> DeliverAsync(NotificationDeliveryRequest request,CancellationToken ct){Calls++;return Task.FromResult(result);} }
    private sealed class ThrowingProvider:INotificationDeliveryProvider { public Task<NotificationDeliveryResult> DeliverAsync(NotificationDeliveryRequest request,CancellationToken ct)=>throw new InvalidOperationException("Ambiguous provider crash"); }
    private sealed class GatedProvider(NotificationDeliveryResult result):INotificationDeliveryProvider { private readonly TaskCompletionSource release=new(TaskCreationOptions.RunContinuationsAsynchronously); public TaskCompletionSource Entered{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously); public int Calls{get;private set;} public void Release()=>release.TrySetResult(); public async Task<NotificationDeliveryResult> DeliverAsync(NotificationDeliveryRequest request,CancellationToken ct){Calls++;Entered.TrySetResult();await release.Task.WaitAsync(ct);return result;} }
}
