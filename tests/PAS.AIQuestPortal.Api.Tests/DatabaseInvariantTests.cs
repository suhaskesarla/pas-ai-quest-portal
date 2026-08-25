using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PAS.AIQuestPortal.Api.Data;
using Xunit;

namespace PAS.AIQuestPortal.Api.Tests;

public sealed class DatabaseInvariantTests : IAsyncLifetime
{
    private readonly string _connectionString;
    private QuestDbContext _db = null!;
    private TestData _data = null!;

    public DatabaseInvariantTests()
    {
        string baseConnection = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION")
            ?? "Server=localhost,1433;Database=master;User Id=sa;Password=Local-only-validation-Passw0rd!;TrustServerCertificate=True";
        var builder = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"PasAiQuestTests_{Guid.NewGuid():N}"
        };
        _connectionString = builder.ConnectionString;
    }

    public async Task InitializeAsync()
    {
        _db = CreateContext();
        await _db.Database.EnsureCreatedAsync();
        _data = await SeedAsync(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await using QuestDbContext cleanup = CreateContext();
        await cleanup.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task July_task_approval_cannot_be_attributed_to_August()
    {
        XPEntry invalid = TaskGrant(_data.SubmissionA, _data.Beneficiary, _data.August);
        await AssertConstraintFailureAsync(invalid);
    }

    [Fact]
    public async Task Task_approval_cannot_mix_submission_with_another_task()
    {
        XPEntry invalid = TaskGrant(_data.SubmissionA, _data.Beneficiary, _data.July);
        invalid.ChallengeId = _data.ChallengeB;
        invalid.TaskId = _data.TaskB;
        await AssertConstraintFailureAsync(invalid);
    }

    [Fact]
    public async Task Task_approval_recipient_must_be_submission_beneficiary()
    {
        await AssertConstraintFailureAsync(TaskGrant(_data.SubmissionA, _data.OtherParticipant, _data.July));
    }

    [Fact]
    public async Task Filtered_index_rejects_duplicate_task_approval_grant()
    {
        _db.XPEntries.Add(TaskGrant(_data.SubmissionA, _data.Beneficiary, _data.July));
        await _db.SaveChangesAsync();
        _db.XPEntries.Add(TaskGrant(_data.SubmissionA, _data.Beneficiary, _data.July));
        await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
    }

    [Fact]
    public async Task Filtered_index_allows_reversal_and_correction_rows()
    {
        XPEntry grant = TaskGrant(_data.SubmissionA, _data.Beneficiary, _data.July);
        _db.XPEntries.Add(grant);
        await _db.SaveChangesAsync();

        _db.XPEntries.AddRange(
            TaskAdjustment(grant, XPEntryType.Reversal, -5),
            TaskAdjustment(grant, XPEntryType.Correction, 3));
        await _db.SaveChangesAsync();

        Assert.Equal(3, await _db.XPEntries.CountAsync(x => x.SubmissionId == _data.SubmissionA));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Participation_member_must_match_parent_challenge_and_cycle(bool mismatchChallenge)
    {
        var member = new ChallengeParticipationMember
        {
            ChallengeParticipationId = _data.Participation,
            ChallengeId = mismatchChallenge ? _data.ChallengeB : _data.ChallengeA,
            CycleId = mismatchChallenge ? _data.July : _data.August,
            ParticipantId = _data.OtherParticipant,
            JoinedSnapshotAt = DateTimeOffset.UtcNow
        };
        await AssertConstraintFailureAsync(member);
    }

    [Fact]
    public async Task XP_cycle_team_must_belong_to_XP_cycle()
    {
        XPEntry invalid = ManualGrant(_data.Beneficiary, _data.July);
        invalid.CycleTeamId = _data.AugustTeam;
        await AssertConstraintFailureAsync(invalid);
    }

    [Fact]
    public async Task Scored_participant_must_be_enrolled_in_reporting_cycle()
    {
        await AssertConstraintFailureAsync(ManualGrant(_data.UnenrolledParticipant, _data.July));
    }

    [Fact]
    public async Task Closed_team_membership_is_retained_and_only_one_open_membership_is_allowed()
    {
        var oldMembership = new CycleTeamMember { Id = Guid.NewGuid(), CycleId = _data.July, CycleTeamId = _data.JulyTeam, ParticipantId = _data.Beneficiary, JoinedAt = DateTimeOffset.UtcNow.AddDays(-10), LeftAt = DateTimeOffset.UtcNow.AddDays(-2) };
        var currentMembership = new CycleTeamMember { Id = Guid.NewGuid(), CycleId = _data.July, CycleTeamId = _data.SecondJulyTeam, ParticipantId = _data.Beneficiary, JoinedAt = DateTimeOffset.UtcNow.AddDays(-1) };
        _db.CycleTeamMembers.AddRange(oldMembership, currentMembership);
        await _db.SaveChangesAsync();
        Assert.Equal(2, await _db.CycleTeamMembers.CountAsync(x => x.ParticipantId == _data.Beneficiary && x.CycleId == _data.July));

        _db.CycleTeamMembers.Add(new CycleTeamMember { Id = Guid.NewGuid(), CycleId = _data.July, CycleTeamId = _data.JulyTeam, ParticipantId = _data.Beneficiary, JoinedAt = DateTimeOffset.UtcNow });
        await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
    }

    [Fact]
    public async Task Cycle_and_participant_deadline_events_are_append_only()
    {
        var cycleEvent = new CycleEvent { Id = Guid.NewGuid(), CycleId = _data.July, SequenceNumber = 1, EventType = CycleEventType.StatusChanged, FromStatus = CycleStatus.Active, ToStatus = CycleStatus.Closing, Reason = "Closing review", ActorId = _data.Manager, OccurredAt = DateTimeOffset.UtcNow };
        var deadlineEvent = new ParticipantChallengeDeadlineEvent { Id = Guid.NewGuid(), ChallengeId = _data.ChallengeA, ParticipantId = _data.Beneficiary, SequenceNumber = 1, EventType = DeadlineEventType.OverrideSet, NewOverrideDueAt = DateTimeOffset.UtcNow.AddDays(3), PreviousEffectiveDueAt = DateTimeOffset.UtcNow, NewEffectiveDueAt = DateTimeOffset.UtcNow.AddDays(3), Reason = "Approved extension", ActorId = _data.Manager, OccurredAt = DateTimeOffset.UtcNow };
        _db.AddRange(cycleEvent, deadlineEvent);
        await _db.SaveChangesAsync();

        cycleEvent.Reason = "mutated";
        await Assert.ThrowsAsync<InvalidOperationException>(() => _db.SaveChangesAsync());
        _db.Entry(cycleEvent).State = EntityState.Unchanged;
        _db.Remove(deadlineEvent);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _db.SaveChangesAsync());
    }

    [Fact]
    public async Task Operational_challenge_due_date_change_still_requires_deadline_audit()
    {
        Challenge challenge = await _db.Challenges.SingleAsync(x => x.Id == _data.ChallengeB);
        challenge.DueAt = challenge.DueAt.AddMinutes(1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _db.SaveChangesAsync());
    }

    private QuestDbContext CreateContext() => new(new DbContextOptionsBuilder<QuestDbContext>().UseSqlServer(_connectionString).Options);

    private async Task AssertConstraintFailureAsync(object entity)
    {
        _db.Add(entity);
        await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
    }

    private XPEntry TaskGrant(Guid submissionId, Guid participantId, Guid cycleId) => new()
    {
        Id = Guid.NewGuid(), ParticipantId = participantId, CycleId = cycleId, Amount = 5,
        EntryType = XPEntryType.Grant, SourceType = XPSourceType.TaskApproval,
        SubmissionId = submissionId, ChallengeId = _data.ChallengeA, TaskId = _data.TaskA,
        Reason = "Approved", AwardedByParticipantId = _data.Manager, AwardedAt = DateTimeOffset.UtcNow
    };

    private XPEntry TaskAdjustment(XPEntry grant, XPEntryType type, int amount) => new()
    {
        Id = Guid.NewGuid(), ParticipantId = grant.ParticipantId, CycleId = grant.CycleId, Amount = amount,
        EntryType = type, SourceType = grant.SourceType, SubmissionId = grant.SubmissionId,
        ChallengeId = grant.ChallengeId, TaskId = grant.TaskId, ReversesEntryId = grant.Id,
        Reason = type.ToString(), AwardedByParticipantId = _data.Manager, AwardedAt = DateTimeOffset.UtcNow
    };

    private XPEntry ManualGrant(Guid participantId, Guid cycleId) => new()
    {
        Id = Guid.NewGuid(), ParticipantId = participantId, CycleId = cycleId, Amount = 5,
        EntryType = XPEntryType.Grant, SourceType = XPSourceType.ManualAward,
        AwardCategoryId = _data.GlobalAwardCategory, Reason = "Manual award",
        AwardedByParticipantId = _data.Manager, AwardedAt = DateTimeOffset.UtcNow
    };

    private static async Task<TestData> SeedAsync(QuestDbContext db)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var d = new TestData();
        db.Participants.AddRange(
            new Participant { Id = d.Manager, DisplayName = "Manager", CreatedAt = now },
            new Participant { Id = d.Beneficiary, DisplayName = "Beneficiary", CreatedAt = now },
            new Participant { Id = d.OtherParticipant, DisplayName = "Other", CreatedAt = now },
            new Participant { Id = d.UnenrolledParticipant, DisplayName = "Unenrolled", CreatedAt = now });
        db.Cycles.AddRange(
            new Cycle { Id = d.July, Code = "2026-07", Name = "July", Status = CycleStatus.Active, StartsAt = now.AddMonths(-1), EndsAt = now, CreatedAt = now, CreatedByParticipantId = d.Manager },
            new Cycle { Id = d.August, Code = "2026-08", Name = "August", Status = CycleStatus.Active, StartsAt = now, EndsAt = now.AddMonths(1), CreatedAt = now, CreatedByParticipantId = d.Manager });
        db.CycleParticipants.AddRange(
            new CycleParticipant { CycleId = d.July, ParticipantId = d.Manager, Status = CycleParticipantStatus.Active },
            new CycleParticipant { CycleId = d.July, ParticipantId = d.Beneficiary, Status = CycleParticipantStatus.Active },
            new CycleParticipant { CycleId = d.July, ParticipantId = d.OtherParticipant, Status = CycleParticipantStatus.Active },
            new CycleParticipant { CycleId = d.August, ParticipantId = d.Manager, Status = CycleParticipantStatus.Active },
            new CycleParticipant { CycleId = d.August, ParticipantId = d.Beneficiary, Status = CycleParticipantStatus.Active },
            new CycleParticipant { CycleId = d.August, ParticipantId = d.OtherParticipant, Status = CycleParticipantStatus.Active });
        db.Challenges.AddRange(
            new Challenge { Id = d.ChallengeA, CycleId = d.July, Name = "A", Description = "A", Category = "Learning", Status = ChallengeStatus.Open, OpenAt = now.AddDays(-5), DueAt = now.AddDays(5), CloseAt = now.AddDays(10), CreatedAt = now, CreatedByParticipantId = d.Manager },
            new Challenge { Id = d.ChallengeB, CycleId = d.July, Name = "B", Description = "B", Category = "Learning", Status = ChallengeStatus.Open, OpenAt = now.AddDays(-5), DueAt = now.AddDays(5), CloseAt = now.AddDays(10), CreatedAt = now, CreatedByParticipantId = d.Manager });
        db.ChallengeTasks.AddRange(
            new ChallengeTask { Id = d.TaskA, ChallengeId = d.ChallengeA, Name = "Task A", XP = 5, EvidenceRequirement = EvidenceRequirement.Text, ScoringMode = ScoringMode.Individual, SortOrder = 1 },
            new ChallengeTask { Id = d.TaskB, ChallengeId = d.ChallengeB, Name = "Task B", XP = 5, EvidenceRequirement = EvidenceRequirement.Text, ScoringMode = ScoringMode.Individual, SortOrder = 1 });
        db.ChallengeParticipations.Add(new ChallengeParticipation { Id = d.Participation, ChallengeId = d.ChallengeA, CycleId = d.July, CreatedAt = now, CreatedByParticipantId = d.Manager });
        db.Submissions.Add(new Submission { Id = d.SubmissionA, CycleId = d.July, ClaimantId = d.Beneficiary, ChallengeId = d.ChallengeA, TaskId = d.TaskA, ChallengeParticipationId = d.Participation, Status = SubmissionStatus.Submitted, SubmittedAt = now, LastUpdatedAt = now });
        db.SubmissionBeneficiaries.Add(new SubmissionBeneficiary { SubmissionId = d.SubmissionA, ParticipantId = d.Beneficiary, CycleId = d.July, AddedAt = now, AddedByParticipantId = d.Beneficiary });
        db.CycleTeams.AddRange(
            new CycleTeam { Id = d.JulyTeam, CycleId = d.July, Name = "July One", CreatedAt = now },
            new CycleTeam { Id = d.SecondJulyTeam, CycleId = d.July, Name = "July Two", CreatedAt = now },
            new CycleTeam { Id = d.AugustTeam, CycleId = d.August, Name = "August One", CreatedAt = now });
        db.AwardCategories.Add(new AwardCategory { Id = d.GlobalAwardCategory, Code = "TEST", Name = "Test" });
        await db.SaveChangesAsync();
        return d;
    }

    private sealed class TestData
    {
        public Guid Manager { get; } = Guid.NewGuid(); public Guid Beneficiary { get; } = Guid.NewGuid(); public Guid OtherParticipant { get; } = Guid.NewGuid(); public Guid UnenrolledParticipant { get; } = Guid.NewGuid();
        public Guid July { get; } = Guid.NewGuid(); public Guid August { get; } = Guid.NewGuid(); public Guid ChallengeA { get; } = Guid.NewGuid(); public Guid ChallengeB { get; } = Guid.NewGuid(); public Guid TaskA { get; } = Guid.NewGuid(); public Guid TaskB { get; } = Guid.NewGuid();
        public Guid Participation { get; } = Guid.NewGuid(); public Guid SubmissionA { get; } = Guid.NewGuid(); public Guid JulyTeam { get; } = Guid.NewGuid(); public Guid SecondJulyTeam { get; } = Guid.NewGuid(); public Guid AugustTeam { get; } = Guid.NewGuid(); public Guid GlobalAwardCategory { get; } = Guid.NewGuid();
    }
}
