using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PAS.AIQuestPortal.Api.Authentication;
using PAS.AIQuestPortal.Api.Configuration;
using PAS.AIQuestPortal.Api.Data;
using PAS.AIQuestPortal.Api.Workflow;
using Xunit;

namespace PAS.AIQuestPortal.Api.Tests;

public sealed class SubmissionWorkflowTests : IAsyncLifetime
{
    private readonly string connection;
    private QuestDbContext db = null!;
    private MutableUser user = null!;
    private FixedClock clock = null!;
    private Seed data = null!;

    public SubmissionWorkflowTests()
    {
        string basis = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION") ?? "Server=localhost,1433;Database=master;User Id=sa;Password=Local-only-validation-Passw0rd!;TrustServerCertificate=True";
        connection = new SqlConnectionStringBuilder(basis) { InitialCatalog = $"PasAiQuestWorkflow_{Guid.NewGuid():N}" }.ConnectionString;
    }

    public async Task InitializeAsync()
    {
        db = Context(); await db.Database.EnsureCreatedAsync(); data = await SeedAsync(db);
        user = new(data.Claimant, QuestRoles.Participant); clock = new(new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero));
    }

    public async Task DisposeAsync() { await db.DisposeAsync(); await using QuestDbContext cleanup = Context(); await cleanup.Database.EnsureDeletedAsync(); }

    [Fact]
    public async Task Shared_lifecycle_is_audited_approved_atomically_idempotent_and_correctable_when_finalised()
    {
        SubmissionWorkflowService service = Service();
        SubmissionView created = await service.CreateAsync(Create(data.TeamTask, [data.Claimant, data.Beneficiary]), default);
        Assert.Equal(data.Claimant, created.Claimant.ParticipantId);
        Assert.Equal(2, created.Beneficiaries.Count);
        user.Set(data.Manager, QuestRoles.Manager); clock.UtcNow = new(2026, 8, 11, 9, 0, 0, TimeSpan.Zero);
        SubmissionView needs = await service.ReviewAsync(created.Id, new(created.Version, ReviewAction.NeedsEvidence, "Add the shared artefact"), default);
        Assert.Equal(SubmissionStatus.NeedsEvidence, needs.Status);
        Assert.Equal([SubmissionStatus.Submitted, SubmissionStatus.UnderReview, SubmissionStatus.NeedsEvidence], needs.History.Select(x => x.EventType));

        user.Set(data.Claimant, QuestRoles.Participant); clock.UtcNow = clock.UtcNow.AddHours(1);
        SubmissionView resubmitted = await service.ResubmitAsync(created.Id, new(needs.Version, [new(EvidenceKind.Text, "Evidence", "Completed shared evidence")], "Updated"), default);
        Assert.Equal(created.Id, resubmitted.Id);
        EvidenceItem currentEvidence = Assert.Single(resubmitted.Evidence);
        Assert.Equal("Completed shared evidence", currentEvidence.Value);
        user.Set(data.Manager, QuestRoles.Manager); clock.UtcNow = new(2026, 9, 2, 11, 0, 0, TimeSpan.Zero);
        SubmissionView approved = await service.ReviewAsync(created.Id, new(resubmitted.Version, ReviewAction.Approve, null), default);
        Assert.Equal(SubmissionStatus.Approved, approved.Status);
        Assert.Equal(2, await db.XPEntries.CountAsync(x => x.SubmissionId == created.Id && x.EntryType == XPEntryType.Grant));
        Assert.All(await db.XPEntries.Where(x => x.SubmissionId == created.Id).ToListAsync(), x => { Assert.Equal(data.Cycle, x.CycleId); Assert.Equal(clock.UtcNow, x.AwardedAt); });
        await service.ReviewAsync(created.Id, new(resubmitted.Version, ReviewAction.Approve, null), default);
        Assert.Equal(2, await db.XPEntries.CountAsync(x => x.SubmissionId == created.Id));

        XPEntry grant = await db.XPEntries.SingleAsync(x => x.SubmissionId == created.Id && x.ParticipantId == data.Beneficiary);
        await AssertCode("CorrectionReasonRequired", () => service.CorrectAsync(grant.Id, new(0, " "), default));
        Assert.Equal(data.TaskXp, grant.Amount);
        CorrectionView down = await service.CorrectAsync(grant.Id, new(0, "Beneficiary-specific correction"), default);
        Assert.Equal(XPEntryType.Reversal, down.EntryType); Assert.Equal(-data.TaskXp, down.Amount);
        CorrectionView up = await service.CorrectAsync(grant.Id, new(data.TaskXp + 3, "Restored with adjustment"), default);
        Assert.Equal(XPEntryType.Correction, up.EntryType); Assert.Equal(data.TaskXp + 3, grant.Amount + await db.XPEntries.Where(x => x.ReversesEntryId == grant.Id).SumAsync(x => x.Amount));
        Assert.Equal(2, await db.CycleEvents.CountAsync(x => x.EventType == CycleEventType.CorrectionRecorded));
        Assert.Equal(CycleStatus.Finalised, (await db.Cycles.FindAsync(data.Cycle))!.Status);
    }

    [Fact]
    public async Task Correction_validation_target_scope_repeat_chain_and_atomicity_are_enforced()
    {
        user.Set(data.Manager, QuestRoles.Manager);
        XPEntry grant = await AddCorrectableGrant();
        SubmissionWorkflowService service = Service();
        Guid categoryId = Guid.NewGuid(), raidId = Guid.NewGuid();
        db.AwardCategories.Add(new AwardCategory { Id = categoryId, CycleId = data.Cycle, Code = "DEFERRED", Name = "Deferred correction source" });
        db.RaidSessions.Add(new RaidSession { Id = raidId, CycleId = data.Cycle, Name = "Deferred raid correction", OccurredAt = clock.UtcNow });
        var manual = new XPEntry { Id = Guid.NewGuid(), ParticipantId = data.Beneficiary, CycleId = data.Cycle, Amount = 5, EntryType = XPEntryType.Grant, SourceType = XPSourceType.ManualAward, AwardCategoryId = categoryId, Reason = "Manual", AwardedByParticipantId = data.Manager, AwardedAt = clock.UtcNow };
        var raid = new XPEntry { Id = Guid.NewGuid(), ParticipantId = data.Beneficiary, CycleId = data.Cycle, Amount = 5, EntryType = XPEntryType.Grant, SourceType = XPSourceType.Raid, RaidSessionId = raidId, Reason = "Raid", AwardedByParticipantId = data.Manager, AwardedAt = clock.UtcNow };
        db.XPEntries.AddRange(manual, raid); await db.SaveChangesAsync();
        await AssertCode("InvalidCorrectionAmount", () => service.CorrectAsync(grant.Id, new(null, "reason"), default));
        await AssertCode("InvalidCorrectionAmount", () => service.CorrectAsync(grant.Id, new(-1, "reason"), default));
        await AssertCode("CorrectionReasonRequired", () => service.CorrectAsync(grant.Id, new(10, "  "), default));
        await AssertCode("CorrectionReasonTooLong", () => service.CorrectAsync(grant.Id, new(10, new string('x', 2001)), default));

        await using (QuestDbContext competing = Context())
        await using (var competingTransaction = await competing.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable))
        {
            string resource = $"quest-xp-correction:{grant.Id:N}";
            await competing.Database.ExecuteSqlInterpolatedAsync($"DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 0, @DbPrincipal = 'public';");
            await AssertCode("CorrectionConflict", () => service.CorrectAsync(grant.Id, new(15, "Concurrent correction"), default));
            await competingTransaction.RollbackAsync();
        }

        CorrectionView down = await service.CorrectAsync(grant.Id, new(15, "  Reduce award  "), default);
        CorrectionView up = await service.CorrectAsync(grant.Id, new(20, "Restore part"), default);
        Assert.Equal((-10, XPEntryType.Reversal, "Reduce award"), (down.Amount, down.EntryType, down.Reason));
        Assert.Equal((5, XPEntryType.Correction), (up.Amount, up.EntryType));
        Assert.Equal(25, (await db.XPEntries.FindAsync(grant.Id))!.Amount);
        Assert.Equal(20, grant.Amount + await db.XPEntries.Where(x => x.ReversesEntryId == grant.Id).SumAsync(x => x.Amount));

        int entries = await db.XPEntries.CountAsync(), events = await db.CycleEvents.CountAsync();
        await AssertCode("CorrectionNoChange", () => service.CorrectAsync(grant.Id, new(20, "No change"), default));
        Assert.Equal(entries, await db.XPEntries.CountAsync()); Assert.Equal(events, await db.CycleEvents.CountAsync());
        await AssertCode("XPEntryNotFound", () => service.CorrectAsync(down.Id, new(0, "Unsupported adjustment"), default));
        await AssertCode("XPEntryNotFound", () => service.CorrectAsync(up.Id, new(0, "Unsupported adjustment"), default));
        await AssertCode("XPEntryNotFound", () => service.CorrectAsync(manual.Id, new(0, "Unsupported source"), default));
        await AssertCode("XPEntryNotFound", () => service.CorrectAsync(raid.Id, new(0, "Unsupported source"), default));
        await AssertCode("XPEntryNotFound", () => service.CorrectAsync(Guid.NewGuid(), new(0, "Unknown"), default));

        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER TR_CorrectionEventFail ON CycleEvents AFTER INSERT AS IF EXISTS (SELECT 1 FROM inserted WHERE EventType = 'CorrectionRecorded') THROW 51000, 'synthetic correction event failure', 1;");
        await Assert.ThrowsAnyAsync<Exception>(() => service.CorrectAsync(grant.Id, new(0, "Atomic failure"), default));
        Assert.Equal(entries, await db.XPEntries.CountAsync()); Assert.Equal(events, await db.CycleEvents.CountAsync());
    }

    [Fact]
    public async Task Individual_claimant_selected_subset_and_link_evidence_are_enforced()
    {
        SubmissionWorkflowService service = Service();
        ChallengeTask task = (await db.ChallengeTasks.FindAsync(data.TeamTask))!;
        task.ScoringMode = ScoringMode.Individual;
        task.EvidenceRequirement = EvidenceRequirement.Link;
        await db.SaveChangesAsync();

        var individual = new CreateSubmissionRequest(data.Challenge, data.TeamTask, null, [data.Claimant], [new(EvidenceKind.Link, "Evidence link", "https://example.invalid/synthetic")], null);
        SubmissionView created = await service.CreateAsync(individual, default);
        Assert.Equal(data.Claimant, Assert.Single(created.Beneficiaries).ParticipantId);
        Assert.Equal(EvidenceKind.Link, Assert.Single(created.Evidence).Kind);
        await AssertCode("InvalidIndividualBeneficiaries", () => service.CreateAsync(individual with { ChallengeParticipationId = data.Participation, BeneficiaryIds = [data.Claimant, data.Beneficiary] }, default));

        task.ScoringMode = ScoringMode.ClaimantSelectsBeneficiaries;
        task.EvidenceRequirement = EvidenceRequirement.Text;
        await db.SaveChangesAsync();
        CreateSubmissionRequest subset = Create(data.TeamTask, [data.Claimant]);
        Assert.Equal([data.Claimant], (await service.CreateAsync(subset, default)).Beneficiaries.Select(x => x.ParticipantId));
        await AssertCode("InvalidParticipationBeneficiaries", () => service.CreateAsync(subset with { BeneficiaryIds = [data.Claimant, data.Manager] }, default));
    }

    [Fact]
    public async Task Submission_revalidates_all_memberships_inside_write_transaction()
    {
        var hook = new BeforeTransactionHook(async ct =>
        {
            await using QuestDbContext competing = Context(); CycleParticipant membership = await competing.CycleParticipants.SingleAsync(x => x.CycleId == data.Cycle && x.ParticipantId == data.Beneficiary, ct);
            membership.Status = CycleParticipantStatus.Inactive; membership.LeftAt = clock.UtcNow;
            competing.CycleParticipantEvents.Add(new CycleParticipantEvent { Id = Guid.NewGuid(), CycleId = data.Cycle, ParticipantId = data.Beneficiary, SequenceNumber = 2, EventType = CycleParticipantEventType.StatusChanged, FromStatus = CycleParticipantStatus.Active, ToStatus = CycleParticipantStatus.Inactive, Reason = "Synthetic concurrent deactivation", ActorId = data.Manager, OccurredAt = clock.UtcNow });
            await competing.SaveChangesAsync(ct);
        });
        var service = new SubmissionWorkflowService(db, user, clock, beforeWriteTransactionHook: hook);
        WorkflowException error = await Assert.ThrowsAsync<WorkflowException>(() => service.CreateAsync(Create(data.TeamTask, [data.Claimant, data.Beneficiary]), default));
        Assert.Equal("IneligibleBeneficiary", error.Code); Assert.Empty(await db.Submissions.ToListAsync()); Assert.Empty(await db.SubmissionBeneficiaries.ToListAsync());
    }

    [Fact]
    public async Task Submission_and_deactivation_overlap_on_shared_membership_locks_without_partial_writes()
    {
        await using QuestDbContext administration = Context();
        await using var administrationTransaction = await administration.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        string cycleResource = $"quest-cycle-admin:{data.Cycle:N}";
        string participantResource = $"quest-cycle-participant:{data.Cycle:N}:{data.Beneficiary:N}";
        await AcquireLock(administration, administrationTransaction, cycleResource);
        await AcquireLock(administration, administrationTransaction, participantResource);

        CycleParticipant membership = await administration.CycleParticipants.SingleAsync(x => x.CycleId == data.Cycle && x.ParticipantId == data.Beneficiary);
        membership.Status = CycleParticipantStatus.Inactive; membership.LeftAt = clock.UtcNow;
        administration.CycleParticipantEvents.Add(new CycleParticipantEvent { Id = Guid.NewGuid(), CycleId = data.Cycle, ParticipantId = data.Beneficiary, SequenceNumber = 2, EventType = CycleParticipantEventType.StatusChanged, FromStatus = CycleParticipantStatus.Active, ToStatus = CycleParticipantStatus.Inactive, Reason = "Controlled overlapping deactivation", ActorId = data.Manager, OccurredAt = clock.UtcNow });
        await administration.SaveChangesAsync();

        await using QuestDbContext submitting = Context();
        var submissionService = new SubmissionWorkflowService(submitting, new MutableUser(data.Claimant, QuestRoles.Participant), clock);
        Task<SubmissionView> submission = submissionService.CreateAsync(Create(data.TeamTask, [data.Beneficiary, data.Claimant]), default);
        await Task.Delay(100);
        Assert.False(submission.IsCompleted);

        await administrationTransaction.CommitAsync();
        WorkflowException error = await Assert.ThrowsAsync<WorkflowException>(() => submission);
        Assert.Equal("IneligibleBeneficiary", error.Code);
        Assert.Empty(await db.Submissions.ToListAsync());
        Assert.Empty(await db.SubmissionBeneficiaries.ToListAsync());
    }

    [Fact]
    public async Task Identity_eligibility_team_policy_and_evidence_fail_closed()
    {
        SubmissionWorkflowService service = Service();
        await AssertCode("ClaimantMustBenefit", () => service.CreateAsync(Create(data.TeamTask, [data.Beneficiary]), default));
        await AssertCode("IneligibleBeneficiary", () => service.CreateAsync(Create(data.TeamTask, [data.Claimant, data.Inactive]), default));
        await AssertCode("EvidenceRequired", () => service.CreateAsync(Create(data.TeamTask, [data.Claimant, data.Beneficiary]) with { Evidence = [] }, default));
        await AssertCode("InvalidChallengeTask", () => service.CreateAsync(Create(data.TeamTask, [data.Claimant, data.Beneficiary]) with { ChallengeId = Guid.NewGuid() }, default));
        TaskView shared = Assert.Single((await service.EligibleAsync(default)).SelectMany(x => x.Tasks).Where(x => x.Id == data.TeamTask));
        Assert.Equal(2, shared.Participations.Count);
        Assert.All(shared.Participations, x => Assert.True(x.RequiresCompleteParticipation));
        await AssertCode("InvalidParticipationBeneficiaries", () => service.CreateAsync(Create(data.TeamTask, [data.Claimant, data.Beneficiary, data.Manager]), default));

        Assert.DoesNotContain((await service.EligibleAsync(default)).SelectMany(x => x.Tasks), x => x.Id == data.SoloTask);
        await AssertCode("InvalidParticipationBeneficiaries", () => service.CreateAsync(Create(data.SoloTask, [data.Claimant]), default));
        (await db.ChallengeTeamPolicies.FindAsync(data.SoloChallenge))!.AllowSolo = true; await db.SaveChangesAsync();
        TaskView solo = Assert.Single((await service.EligibleAsync(default)).SelectMany(x => x.Tasks).Where(x => x.Id == data.SoloTask));
        Assert.Equal(data.SoloParticipation, Assert.Single(solo.Participations).ParticipationId);
        Assert.Equal(SubmissionStatus.Submitted, (await service.CreateAsync(Create(data.SoloTask, [data.Claimant]), default)).Status);
    }

    [Fact]
    public async Task Open_due_close_and_override_control_participant_activity_not_cycle_status()
    {
        SubmissionWorkflowService service = Service(); clock.UtcNow = new(2026, 8, 21, 12, 0, 0, TimeSpan.Zero);
        await AssertCode("SubmissionOutsideEligibilityWindow", () => service.CreateAsync(Create(data.TeamTask, [data.Claimant, data.Beneficiary]), default));
        db.ParticipantChallengeDeadlineEvents.Add(new ParticipantChallengeDeadlineEvent { Id = Guid.NewGuid(), ChallengeId = data.Challenge, ParticipantId = data.Claimant, SequenceNumber = 1, EventType = DeadlineEventType.OverrideSet, NewOverrideDueAt = clock.UtcNow.AddDays(2), PreviousEffectiveDueAt = clock.UtcNow.AddDays(-2), NewEffectiveDueAt = clock.UtcNow.AddDays(2), Reason = "Approved extension", ActorId = data.Manager, OccurredAt = clock.UtcNow });
        await db.SaveChangesAsync();
        Assert.Equal(SubmissionStatus.Submitted, (await service.CreateAsync(Create(data.TeamTask, [data.Claimant, data.Beneficiary]), default)).Status);
        Assert.Equal(CycleStatus.Finalised, (await db.Cycles.FindAsync(data.Cycle))!.Status);
        clock.UtcNow = clock.UtcNow.AddDays(3);
        await AssertCode("SubmissionOutsideEligibilityWindow", () => service.CreateAsync(Create(data.TeamTask, [data.Claimant, data.Beneficiary]), default));
    }

    [Fact]
    public async Task Review_is_manager_only_and_rejection_creates_no_xp()
    {
        SubmissionWorkflowService service = Service(); SubmissionView created = await service.CreateAsync(Create(data.TeamTask, [data.Claimant, data.Beneficiary]), default);
        await AssertCode("Forbidden", () => service.ReviewAsync(created.Id, new(created.Version, ReviewAction.Approve, null), default));
        user.Set(data.Manager, QuestRoles.Manager);
        SubmissionView rejected = await service.ReviewAsync(created.Id, new(created.Version, ReviewAction.Reject, "Insufficient evidence"), default);
        Assert.Equal(SubmissionStatus.Rejected, rejected.Status); Assert.Empty(await db.XPEntries.Where(x => x.SubmissionId == created.Id).ToListAsync());
    }

    [Fact]
    public async Task Approval_failure_rolls_back_status_events_and_every_beneficiary_grant()
    {
        SubmissionWorkflowService service = Service(); SubmissionView created = await service.CreateAsync(Create(data.TeamTask, [data.Claimant, data.Beneficiary]), default);
        string triggerSql = string.Format(System.Globalization.CultureInfo.InvariantCulture, "CREATE TRIGGER TR_XP_FailWorkflowTest ON XPEntries AFTER INSERT AS IF EXISTS (SELECT 1 FROM inserted WHERE ParticipantId = '{0}') THROW 51000, 'synthetic grant failure', 1;", data.Beneficiary);
        await db.Database.ExecuteSqlRawAsync(triggerSql);
        user.Set(data.Manager, QuestRoles.Manager);
        await Assert.ThrowsAnyAsync<Exception>(() => service.ReviewAsync(created.Id, new(created.Version, ReviewAction.Approve, null), default));
        await db.DisposeAsync(); db = Context();
        Assert.Equal(SubmissionStatus.Submitted, (await db.Submissions.FindAsync(created.Id))!.Status);
        Assert.Empty(await db.XPEntries.Where(x => x.SubmissionId == created.Id).ToListAsync());
        Assert.Single(await db.SubmissionEvents.Where(x => x.SubmissionId == created.Id).ToListAsync());
    }

    private SubmissionWorkflowService Service() => new(db, user, clock);
    private CreateSubmissionRequest Create(Guid task, Guid[] beneficiaries) => new(data.ChallengeFor(task), task, task == data.SoloTask ? data.SoloParticipation : data.Participation, beneficiaries, [new(EvidenceKind.Text, "Evidence", "Synthetic evidence")], null);
    private static async Task AssertCode(string code, Func<Task> operation) { WorkflowException error = await Assert.ThrowsAsync<WorkflowException>(operation); Assert.Equal(code, error.Code); }
    private QuestDbContext Context() => new(new DbContextOptionsBuilder<QuestDbContext>().UseSqlServer(connection).Options);

    private async Task<XPEntry> AddCorrectableGrant()
    {
        Guid submissionId = Guid.NewGuid();
        db.Submissions.Add(new Submission { Id = submissionId, CycleId = data.Cycle, ClaimantId = data.Claimant, ChallengeId = data.Challenge, TaskId = data.TeamTask, ChallengeParticipationId = data.Participation, Status = SubmissionStatus.Approved, SubmittedAt = clock.UtcNow, LastUpdatedAt = clock.UtcNow });
        db.SubmissionBeneficiaries.Add(new SubmissionBeneficiary { SubmissionId = submissionId, ParticipantId = data.Beneficiary, CycleId = data.Cycle, AddedAt = clock.UtcNow, AddedByParticipantId = data.Claimant });
        var grant = new XPEntry { Id = Guid.NewGuid(), ParticipantId = data.Beneficiary, CycleId = data.Cycle, Amount = data.TaskXp, EntryType = XPEntryType.Grant, SourceType = XPSourceType.TaskApproval, ChallengeId = data.Challenge, TaskId = data.TeamTask, SubmissionId = submissionId, ChallengeParticipationId = data.Participation, Reason = "Approved", AwardedByParticipantId = data.Manager, AwardedAt = clock.UtcNow };
        db.XPEntries.Add(grant); await db.SaveChangesAsync(); return grant;
    }

    private static async Task<Seed> SeedAsync(QuestDbContext db)
    {
        DateTimeOffset now = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        var s = new Seed();
        db.Participants.AddRange(new Participant { Id = s.Claimant, DisplayName = "Synthetic Claimant", CreatedAt = now }, new Participant { Id = s.Beneficiary, DisplayName = "Synthetic Beneficiary", CreatedAt = now }, new Participant { Id = s.Inactive, DisplayName = "Synthetic Inactive", CreatedAt = now }, new Participant { Id = s.Manager, DisplayName = "Synthetic Manager", CreatedAt = now });
        db.Cycles.Add(new Cycle { Id = s.Cycle, Code = "SYN-26", Name = "Synthetic cycle", Status = CycleStatus.Finalised, StartsAt = now, EndsAt = now.AddMonths(1), CreatedAt = now, CreatedByParticipantId = s.Manager });
        foreach (Guid participantId in new[] { s.Claimant, s.Beneficiary, s.Inactive, s.Manager })
        {
            db.CycleParticipants.Add(new CycleParticipant { CycleId = s.Cycle, ParticipantId = participantId, Status = CycleParticipantStatus.Active, JoinedAt = now });
            db.CycleParticipantEvents.Add(new CycleParticipantEvent { Id = Guid.NewGuid(), CycleId = s.Cycle, ParticipantId = participantId, SequenceNumber = 1, EventType = CycleParticipantEventType.Enrolled, FromStatus = null, ToStatus = CycleParticipantStatus.Active, Reason = "Synthetic test enrollment", ActorId = s.Manager, OccurredAt = now });
        }
        db.Challenges.AddRange(new Challenge { Id = s.Challenge, CycleId = s.Cycle, Name = "Shared quest", Description = "Synthetic", Category = "Build", Status = ChallengeStatus.Open, OpenAt = now, DueAt = now.AddDays(15), CloseAt = now.AddDays(20), CreatedAt = now, CreatedByParticipantId = s.Manager }, new Challenge { Id = s.SoloChallenge, CycleId = s.Cycle, Name = "Solo quest", Description = "Synthetic", Category = "Build", Status = ChallengeStatus.Open, OpenAt = now, DueAt = now.AddDays(15), CloseAt = now.AddDays(20), CreatedAt = now, CreatedByParticipantId = s.Manager });
        db.ChallengeTasks.AddRange(new ChallengeTask { Id = s.TeamTask, ChallengeId = s.Challenge, Name = "Shared task", XP = s.TaskXp, EvidenceRequirement = EvidenceRequirement.Text, ScoringMode = ScoringMode.WholeTeam, SortOrder = 1 }, new ChallengeTask { Id = s.SoloTask, ChallengeId = s.SoloChallenge, Name = "Solo task", XP = s.TaskXp, EvidenceRequirement = EvidenceRequirement.Text, ScoringMode = ScoringMode.WholeTeam, SortOrder = 1 });
        db.ChallengeTeamPolicies.AddRange(new ChallengeTeamPolicy { ChallengeId = s.Challenge, FormationMode = FormationMode.Either, MinMembers = 2, MaxMembers = 4 }, new ChallengeTeamPolicy { ChallengeId = s.SoloChallenge, FormationMode = FormationMode.Either, MinMembers = 2, MaxMembers = 4, AllowSolo = false });
        db.ChallengeParticipations.AddRange(new ChallengeParticipation { Id = s.Participation, ChallengeId = s.Challenge, CycleId = s.Cycle, CreatedAt = now, CreatedByParticipantId = s.Claimant }, new ChallengeParticipation { Id = s.AlternateParticipation, ChallengeId = s.Challenge, CycleId = s.Cycle, CreatedAt = now, CreatedByParticipantId = s.Claimant }, new ChallengeParticipation { Id = s.SoloParticipation, ChallengeId = s.SoloChallenge, CycleId = s.Cycle, CreatedAt = now, CreatedByParticipantId = s.Claimant });
        db.ChallengeParticipationMembers.AddRange(new ChallengeParticipationMember { ChallengeParticipationId = s.Participation, ChallengeId = s.Challenge, CycleId = s.Cycle, ParticipantId = s.Claimant, JoinedSnapshotAt = now }, new ChallengeParticipationMember { ChallengeParticipationId = s.Participation, ChallengeId = s.Challenge, CycleId = s.Cycle, ParticipantId = s.Beneficiary, JoinedSnapshotAt = now }, new ChallengeParticipationMember { ChallengeParticipationId = s.AlternateParticipation, ChallengeId = s.Challenge, CycleId = s.Cycle, ParticipantId = s.Claimant, JoinedSnapshotAt = now }, new ChallengeParticipationMember { ChallengeParticipationId = s.AlternateParticipation, ChallengeId = s.Challenge, CycleId = s.Cycle, ParticipantId = s.Manager, JoinedSnapshotAt = now }, new ChallengeParticipationMember { ChallengeParticipationId = s.SoloParticipation, ChallengeId = s.SoloChallenge, CycleId = s.Cycle, ParticipantId = s.Claimant, JoinedSnapshotAt = now });
        await db.SaveChangesAsync();
        CycleParticipant inactive = (await db.CycleParticipants.FindAsync(s.Cycle, s.Inactive))!;
        inactive.Status = CycleParticipantStatus.Withdrawn; inactive.LeftAt = now;
        db.CycleParticipantEvents.Add(new CycleParticipantEvent { Id = Guid.NewGuid(), CycleId = s.Cycle, ParticipantId = s.Inactive, SequenceNumber = 2, EventType = CycleParticipantEventType.StatusChanged, FromStatus = CycleParticipantStatus.Active, ToStatus = CycleParticipantStatus.Withdrawn, Reason = "Synthetic inactive fixture", ActorId = s.Manager, OccurredAt = now });
        await db.SaveChangesAsync(); return s;
    }

    private sealed class MutableUser(Guid participant, params string[] roles) : IQuestCurrentUser { public QuestUserIdentity Identity { get; private set; } = new(true, participant, "Synthetic", roles); public void Set(Guid id, params string[] newRoles) => Identity = new(true, id, "Synthetic", newRoles); }
    private sealed class FixedClock(DateTimeOffset value) : TimeProvider { public DateTimeOffset UtcNow { get; set; } = value; public override DateTimeOffset GetUtcNow() => UtcNow; }
    private sealed class BeforeTransactionHook(Func<CancellationToken, Task> callback) : ISubmissionBeforeWriteTransactionHook { public Task BeforeTransactionAsync(CancellationToken ct) => callback(ct); }
    private static async Task AcquireLock(QuestDbContext context, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction, string resourceName)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand(); command.Transaction = transaction.GetDbTransaction();
        command.CommandText = "DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = -1, @DbPrincipal = 'public'; SELECT @result;";
        var resource = command.CreateParameter(); resource.ParameterName = "@Resource"; resource.Value = resourceName; command.Parameters.Add(resource);
        Assert.True(Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) >= 0);
    }
    private sealed class Seed
    {
        public Guid Claimant { get; } = Guid.NewGuid(); public Guid Beneficiary { get; } = Guid.NewGuid(); public Guid Inactive { get; } = Guid.NewGuid(); public Guid Manager { get; } = Guid.NewGuid(); public Guid Cycle { get; } = Guid.NewGuid(); public Guid Challenge { get; } = Guid.NewGuid(); public Guid SoloChallenge { get; } = Guid.NewGuid(); public Guid TeamTask { get; } = Guid.NewGuid(); public Guid SoloTask { get; } = Guid.NewGuid(); public Guid Participation { get; } = Guid.NewGuid(); public Guid AlternateParticipation { get; } = Guid.NewGuid(); public Guid SoloParticipation { get; } = Guid.NewGuid(); public int TaskXp { get; } = 25;
        public Guid ChallengeFor(Guid task) => task == SoloTask ? SoloChallenge : Challenge;
    }
}
