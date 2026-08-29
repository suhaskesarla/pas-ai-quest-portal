using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PAS.AIQuestPortal.Api.Authentication;
using PAS.AIQuestPortal.Api.Configuration;
using PAS.AIQuestPortal.Api.Data;
using PAS.AIQuestPortal.Api.Reporting;
using PAS.AIQuestPortal.Api.Workflow;
using Xunit;

namespace PAS.AIQuestPortal.Api.Tests;

public sealed class ParticipantReportingTests : IAsyncLifetime
{
    private readonly string connection;
    private QuestDbContext db = null!;
    private TestUser user = null!;
    private ParticipantReportingService service = null!;
    private readonly DateTimeOffset now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid current = Guid.Parse("10000000-0000-4000-8000-000000000001"), manager = Guid.Parse("10000000-0000-4000-8000-000000000002");
    private readonly Guid cycle = Guid.Parse("20000000-0000-4000-8000-000000000001"), challenge = Guid.Parse("30000000-0000-4000-8000-000000000001"), task = Guid.Parse("30000000-0000-4000-8000-000000000002"), participation = Guid.Parse("30000000-0000-4000-8000-000000000003");

    public ParticipantReportingTests()
    {
        string basis = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION") ?? "Server=localhost,1433;Database=master;User Id=sa;Password=Local-only-validation-Passw0rd!;TrustServerCertificate=True";
        connection = new SqlConnectionStringBuilder(basis) { InitialCatalog = $"PasAiQuestReporting_{Guid.NewGuid():N}" }.ConnectionString;
    }

    public async Task InitializeAsync()
    {
        db = Context(); await db.Database.EnsureCreatedAsync(); await Seed();
        user = new TestUser(current);
        var workflow = new SubmissionWorkflowService(db, user, new TestClock(now));
        service = new ParticipantReportingService(db, user, workflow);
    }

    public async Task DisposeAsync() { await db.DisposeAsync(); await using QuestDbContext cleanup = Context(); await cleanup.Database.EnsureDeletedAsync(); }

    [Fact]
    public async Task Reporting_cycles_are_membership_scoped_and_apply_frozen_default_precedence()
    {
        ReportingCyclesView result = await service.ReportingCyclesAsync(default);
        Assert.Equal(cycle, result.DefaultCycleId);
        Assert.Equal(3, result.Cycles.Count);
        Assert.DoesNotContain(result.Cycles, x => x.Code == "NOT-ENROLLED");

        Cycle selected = await db.Cycles.FindAsync(cycle) ?? throw new InvalidOperationException(); selected.Status = CycleStatus.Finalised;
        db.CycleEvents.Add(new CycleEvent { Id = Guid.NewGuid(), CycleId = cycle, SequenceNumber = 1, EventType = CycleEventType.StatusChanged, FromStatus = CycleStatus.Active, ToStatus = CycleStatus.Finalised, Reason = "Synthetic default fallback", ActorId = manager, OccurredAt = now });
        await db.SaveChangesAsync();
        Assert.Equal(Guid.Parse("20000000-0000-4000-8000-000000000002"), (await service.ReportingCyclesAsync(default)).DefaultCycleId);

        WorkflowException error = await Assert.ThrowsAsync<WorkflowException>(() => service.DashboardAsync(Guid.Parse("20000000-0000-4000-8000-000000000099"), default));
        Assert.Equal(404, error.Status);
    }

    [Fact]
    public async Task Dashboard_and_leaderboard_use_raw_cycle_ledger_active_roster_and_non_xp_passes()
    {
        ParticipantDashboardView dashboard = await service.DashboardAsync(cycle, default);
        Assert.Equal(100, dashboard.TotalXp); // grants + reversal + correction
        Assert.Equal(1, dashboard.IndividualRank);
        Assert.Equal(1, dashboard.EligibleChallengeCount);
        Assert.Equal(1, dashboard.SubmissionStatusCounts[SubmissionStatus.Approved.ToString()]);
        Assert.Equal(1, dashboard.SubmissionStatusCounts[SubmissionStatus.NeedsEvidence.ToString()]);
        RaidPassBalanceView passes = Assert.Single(dashboard.RaidPassBalance);
        Assert.Equal((2, 1, 1), (passes.Assigned, passes.Used, passes.Remaining));
        Assert.Equal(100, dashboard.TotalXp); // raid-pass usage contributes no XP

        IReadOnlyList<LeaderboardEntry> board = await service.LeaderboardAsync(cycle, default);
        Assert.Equal(6, board.Count);
        Assert.Equal([1, 1, 3, 3, 5, 5], board.Select(x => x.Rank));
        Assert.Equal([100, 100, 90, 90, 0, 0], board.Select(x => x.TotalXp));
        Assert.DoesNotContain(board, x => x.DisplayName is "Withdrawn Synthetic" or "Inactive Synthetic");
        Assert.Contains(board, x => x.DisplayName == "Zero Synthetic" && x.TotalXp == 0);
        LeaderboardEntry[] tied = board.Where(x => x.TotalXp == 90).ToArray();
        Assert.True(tied[0].ParticipantId.CompareTo(tied[1].ParticipantId) < 0); // normalized names are equal
    }

    [Fact]
    public async Task Activity_returns_source_labels_raw_adjustments_keyset_pages_and_only_current_ledger()
    {
        var collected = new List<XpActivityItem>(); string? cursor = null;
        do
        {
            XpActivityPage page = await service.XpActivityAsync(cycle, 2, cursor, default);
            collected.AddRange(page.Items); cursor = page.NextCursor;
        } while (cursor is not null);

        Assert.Equal(5, collected.Count);
        Assert.Equal(5, collected.Select(x => x.Id).Distinct().Count());
        Assert.DoesNotContain(collected, x => x.Reason == "Other participant ledger");
        Assert.Contains(collected, x => x.EntryType == XPEntryType.Reversal && x.Amount == -20 && x.ReversesEntryId.HasValue);
        Assert.Contains(collected, x => x.EntryType == XPEntryType.Correction && x.Amount == 10 && x.ReversesEntryId.HasValue);
        Assert.Contains(collected, x => x.SourceType == XPSourceType.TaskApproval && x.Source.Label == "Synthetic Challenge · Synthetic Task");
        Assert.Contains(collected, x => x.SourceType == XPSourceType.ManualAward && x.Source.Label == "Synthetic Award");
        Assert.Contains(collected, x => x.SourceType == XPSourceType.Raid && x.Source.Label == "Synthetic Raid");
        Assert.Contains(collected, x => x.AwardedAt > (db.Cycles.Local.Single(c => c.Id == cycle).EndsAt)); // attribution follows CycleId, not date

        WorkflowException cursorError = await Assert.ThrowsAsync<WorkflowException>(() => service.XpActivityAsync(cycle, 25, "not-a-cursor", default));
        WorkflowException limitError = await Assert.ThrowsAsync<WorkflowException>(() => service.XpActivityAsync(cycle, 101, null, default));
        Assert.Equal(400, cursorError.Status); Assert.Equal(400, limitError.Status);
    }

    [Fact]
    public async Task Team_keeps_current_cycle_team_and_challenge_snapshots_separate_without_scores()
    {
        ParticipantTeamView result = await service.TeamAsync(cycle, default);
        Assert.Equal("Synthetic Cycle Team", result.Team?.Name);
        Assert.Equal(2, result.Team?.Members.Count);
        ChallengeGroupView group = Assert.Single(result.ChallengeGroups);
        Assert.Equal(participation, group.ParticipationId); Assert.Equal(2, group.Members.Count);
        string json = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("totalXp", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rank", json, StringComparison.OrdinalIgnoreCase);
    }

    private async Task Seed()
    {
        Guid alpha1 = Guid.Parse("10000000-0000-4000-8000-000000000010"), alpha2 = Guid.Parse("10000000-0000-4000-8000-000000000011"), tied100 = Guid.Parse("10000000-0000-4000-8000-000000000012"), zero = Guid.Parse("10000000-0000-4000-8000-000000000013"), withdrawn = Guid.Parse("10000000-0000-4000-8000-000000000014"), inactive = Guid.Parse("10000000-0000-4000-8000-000000000015");
        Participant[] people = [new() { Id = current, DisplayName = "Current Synthetic", CreatedAt = now }, new() { Id = manager, DisplayName = "Manager Synthetic", CreatedAt = now }, new() { Id = alpha1, DisplayName = "alpha", CreatedAt = now }, new() { Id = alpha2, DisplayName = "Alpha ", CreatedAt = now }, new() { Id = tied100, DisplayName = "Tied 100", CreatedAt = now }, new() { Id = zero, DisplayName = "Zero Synthetic", CreatedAt = now }, new() { Id = withdrawn, DisplayName = "Withdrawn Synthetic", CreatedAt = now }, new() { Id = inactive, DisplayName = "Inactive Synthetic", CreatedAt = now }];
        db.Participants.AddRange(people);
        Guid closing = Guid.Parse("20000000-0000-4000-8000-000000000002"), finalised = Guid.Parse("20000000-0000-4000-8000-000000000003"), inaccessible = Guid.Parse("20000000-0000-4000-8000-000000000099");
        db.Cycles.AddRange(new Cycle { Id = cycle, Code = "AUG-2026", Name = "August 2026", Status = CycleStatus.Active, StartsAt = now.AddDays(-14), EndsAt = now.AddDays(10), CreatedAt = now, CreatedByParticipantId = manager }, new Cycle { Id = closing, Code = "JUL-2026", Name = "July 2026", Status = CycleStatus.Closing, StartsAt = now.AddMonths(-1), EndsAt = now, CreatedAt = now, CreatedByParticipantId = manager }, new Cycle { Id = finalised, Code = "JUN-2026", Name = "June 2026", Status = CycleStatus.Finalised, StartsAt = now.AddMonths(-2), EndsAt = now.AddMonths(-1), CreatedAt = now, CreatedByParticipantId = manager }, new Cycle { Id = inaccessible, Code = "NOT-ENROLLED", Name = "Not enrolled", Status = CycleStatus.Active, StartsAt = now, EndsAt = now.AddDays(1), CreatedAt = now, CreatedByParticipantId = manager });
        (Guid CycleId, Guid ParticipantId, CycleParticipantStatus FinalStatus)[] enrollments = [(cycle, current, CycleParticipantStatus.Active), (cycle, manager, CycleParticipantStatus.Active), (cycle, alpha1, CycleParticipantStatus.Active), (cycle, alpha2, CycleParticipantStatus.Active), (cycle, tied100, CycleParticipantStatus.Active), (cycle, zero, CycleParticipantStatus.Active), (cycle, withdrawn, CycleParticipantStatus.Withdrawn), (cycle, inactive, CycleParticipantStatus.Inactive), (closing, current, CycleParticipantStatus.Active), (finalised, current, CycleParticipantStatus.Inactive), (inaccessible, manager, CycleParticipantStatus.Active)];
        foreach ((Guid cycleId, Guid participantId, _) in enrollments)
        {
            db.CycleParticipants.Add(new CycleParticipant { CycleId = cycleId, ParticipantId = participantId, Status = CycleParticipantStatus.Active, JoinedAt = now });
            db.CycleParticipantEvents.Add(new CycleParticipantEvent { Id = Guid.NewGuid(), CycleId = cycleId, ParticipantId = participantId, SequenceNumber = 1, EventType = CycleParticipantEventType.Enrolled, FromStatus = null, ToStatus = CycleParticipantStatus.Active, Reason = "Synthetic reporting enrollment", ActorId = manager, OccurredAt = now });
        }
        db.Challenges.Add(new Challenge { Id = challenge, CycleId = cycle, Name = "Synthetic Challenge", Description = "Reporting test", Category = "Synthetic", Status = ChallengeStatus.Open, OpenAt = now.AddDays(-1), DueAt = now.AddDays(2), CloseAt = now.AddDays(3), CreatedAt = now, CreatedByParticipantId = manager });
        db.ChallengeTasks.Add(new ChallengeTask { Id = task, ChallengeId = challenge, Name = "Synthetic Task", XP = 100, EvidenceRequirement = EvidenceRequirement.Text, ScoringMode = ScoringMode.WholeTeam, SortOrder = 1 });
        db.ChallengeTeamPolicies.Add(new ChallengeTeamPolicy { ChallengeId = challenge, FormationMode = FormationMode.Either, MinMembers = 2, MaxMembers = 2 });
        db.ChallengeParticipations.Add(new ChallengeParticipation { Id = participation, ChallengeId = challenge, CycleId = cycle, CreatedAt = now, CreatedByParticipantId = current });
        db.ChallengeParticipationMembers.AddRange(new ChallengeParticipationMember { ChallengeParticipationId = participation, ChallengeId = challenge, CycleId = cycle, ParticipantId = current, JoinedSnapshotAt = now }, new ChallengeParticipationMember { ChallengeParticipationId = participation, ChallengeId = challenge, CycleId = cycle, ParticipantId = zero, JoinedSnapshotAt = now });
        Guid team = Guid.Parse("40000000-0000-4000-8000-000000000001"); db.CycleTeams.Add(new CycleTeam { Id = team, CycleId = cycle, Name = "Synthetic Cycle Team", CreatedAt = now });
        db.CycleTeamMembers.AddRange(new CycleTeamMember { Id = Guid.NewGuid(), CycleTeamId = team, CycleId = cycle, ParticipantId = current, JoinedAt = now }, new CycleTeamMember { Id = Guid.NewGuid(), CycleTeamId = team, CycleId = cycle, ParticipantId = zero, JoinedAt = now });
        Guid submission = Guid.Parse("50000000-0000-4000-8000-000000000001"); db.Submissions.AddRange(new Submission { Id = submission, CycleId = cycle, ClaimantId = current, ChallengeId = challenge, TaskId = task, ChallengeParticipationId = participation, Status = SubmissionStatus.Approved, SubmittedAt = now, LastUpdatedAt = now }, new Submission { Id = Guid.NewGuid(), CycleId = cycle, ClaimantId = current, ChallengeId = challenge, TaskId = task, ChallengeParticipationId = participation, Status = SubmissionStatus.NeedsEvidence, SubmittedAt = now, LastUpdatedAt = now });
        db.SubmissionBeneficiaries.Add(new SubmissionBeneficiary { SubmissionId = submission, ParticipantId = current, CycleId = cycle, AddedAt = now, AddedByParticipantId = current });
        Guid award = Guid.Parse("60000000-0000-4000-8000-000000000001"), raid = Guid.Parse("60000000-0000-4000-8000-000000000002"); db.AwardCategories.Add(new AwardCategory { Id = award, CycleId = cycle, Code = "SYNTH", Name = "Synthetic Award" }); db.RaidSessions.Add(new RaidSession { Id = raid, CycleId = cycle, Name = "Synthetic Raid", OccurredAt = now });
        db.RaidEntitlements.Add(new RaidEntitlement { ParticipantId = current, CycleId = cycle, PassType = PassType.Physical, AssignedCount = 2 }); db.RaidParticipations.Add(new RaidParticipation { Id = Guid.NewGuid(), ParticipantId = current, CycleId = cycle, RaidSessionId = raid, PassType = PassType.Physical, UsedAt = now });
        Guid grant = Guid.Parse("70000000-0000-4000-8000-000000000001"); DateTimeOffset outside = now.AddMonths(2);
        db.XPEntries.AddRange(Xp(grant, current, 100, XPEntryType.Grant, XPSourceType.TaskApproval, outside, submissionId: submission), Xp(Guid.Parse("70000000-0000-4000-8000-000000000002"), current, -20, XPEntryType.Reversal, XPSourceType.TaskApproval, outside.AddMinutes(1), grant, submission), Xp(Guid.Parse("70000000-0000-4000-8000-000000000003"), current, 10, XPEntryType.Correction, XPSourceType.TaskApproval, outside.AddMinutes(2), grant, submission), Xp(Guid.Parse("70000000-0000-4000-8000-000000000004"), current, 5, XPEntryType.Grant, XPSourceType.ManualAward, outside.AddMinutes(3), awardId: award), Xp(Guid.Parse("70000000-0000-4000-8000-000000000005"), current, 5, XPEntryType.Grant, XPSourceType.Raid, outside.AddMinutes(4), raidId: raid));
        db.XPEntries.AddRange(SimpleGrant(tied100, 100), SimpleGrant(alpha1, 90), SimpleGrant(alpha2, 90), SimpleGrant(withdrawn, 1000, "Other participant ledger"), SimpleGrant(inactive, 1000));
        await db.SaveChangesAsync();
        foreach ((Guid cycleId, Guid participantId, CycleParticipantStatus status) in enrollments.Where(x => x.FinalStatus != CycleParticipantStatus.Active))
        {
            CycleParticipant membership = (await db.CycleParticipants.FindAsync(cycleId, participantId))!;
            membership.Status = status; membership.LeftAt = now;
            db.CycleParticipantEvents.Add(new CycleParticipantEvent { Id = Guid.NewGuid(), CycleId = cycleId, ParticipantId = participantId, SequenceNumber = 2, EventType = CycleParticipantEventType.StatusChanged, FromStatus = CycleParticipantStatus.Active, ToStatus = status, Reason = "Synthetic reporting status", ActorId = manager, OccurredAt = now });
        }
        await db.SaveChangesAsync();
    }

    private XPEntry Xp(Guid id, Guid participant, int amount, XPEntryType type, XPSourceType source, DateTimeOffset at, Guid? reverses = null, Guid? submissionId = null, Guid? awardId = null, Guid? raidId = null) => new() { Id = id, ParticipantId = participant, CycleId = cycle, Amount = amount, EntryType = type, SourceType = source, ChallengeId = source == XPSourceType.TaskApproval ? challenge : null, TaskId = source == XPSourceType.TaskApproval ? task : null, SubmissionId = submissionId, AwardCategoryId = awardId, RaidSessionId = raidId, Reason = type.ToString(), AwardedByParticipantId = manager, AwardedAt = at, ReversesEntryId = reverses };
    private XPEntry SimpleGrant(Guid participant, int amount, string reason = "Synthetic leaderboard")
    {
        XPEntry entry = Xp(Guid.NewGuid(), participant, amount, XPEntryType.Grant, XPSourceType.ManualAward, now, awardId: Guid.Parse("60000000-0000-4000-8000-000000000001"));
        entry.Reason = reason;
        return entry;
    }
    private QuestDbContext Context() => new(new DbContextOptionsBuilder<QuestDbContext>().UseSqlServer(connection).Options);
    private sealed class TestUser(Guid id) : IQuestCurrentUser { public QuestUserIdentity Identity { get; } = new(true, id, "Synthetic", [QuestRoles.Participant]); }
    private sealed class TestClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
}
