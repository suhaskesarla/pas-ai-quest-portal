using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PAS.AIQuestPortal.Api.Authentication;
using PAS.AIQuestPortal.Api.Configuration;
using PAS.AIQuestPortal.Api.Data;
using PAS.AIQuestPortal.Api.Reporting;
using PAS.AIQuestPortal.Api.Workflow;
using Xunit;

namespace PAS.AIQuestPortal.Api.Tests;

public sealed class ManagerScoresheetTests : IAsyncLifetime
{
    private readonly string connection;
    private readonly DateTimeOffset now = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid manager = Guid.Parse("81000000-0000-4000-8000-000000000001");
    private readonly Guid active = Guid.Parse("81000000-0000-4000-8000-000000000002");
    private readonly Guid withdrawn = Guid.Parse("81000000-0000-4000-8000-000000000003");
    private readonly Guid inactive = Guid.Parse("81000000-0000-4000-8000-000000000004");
    private readonly Guid outsider = Guid.Parse("81000000-0000-4000-8000-000000000005");
    private readonly Guid cycle = Guid.Parse("82000000-0000-4000-8000-000000000001");
    private readonly Guid emptyCycle = Guid.Parse("82000000-0000-4000-8000-000000000002");
    private readonly Guid challenge = Guid.Parse("83000000-0000-4000-8000-000000000001");
    private readonly Guid task = Guid.Parse("83000000-0000-4000-8000-000000000002");
    private readonly Guid submission = Guid.Parse("83000000-0000-4000-8000-000000000005");
    private readonly Guid award = Guid.Parse("83000000-0000-4000-8000-000000000003");
    private readonly Guid raid = Guid.Parse("83000000-0000-4000-8000-000000000004");
    private QuestDbContext db = null!;

    public ManagerScoresheetTests()
    {
        string basis = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION") ?? "Server=localhost,1433;Database=master;User Id=sa;Password=Local-only-validation-Passw0rd!;TrustServerCertificate=True";
        connection = new SqlConnectionStringBuilder(basis) { InitialCatalog = $"PasAiQuestScoresheet_{Guid.NewGuid():N}" }.ConnectionString;
    }

    public async Task InitializeAsync()
    {
        db = Context();
        await db.Database.EnsureCreatedAsync();
        await Seed();
    }

    public async Task DisposeAsync()
    {
        await db.DisposeAsync();
        await using QuestDbContext cleanup = Context();
        await cleanup.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task Manager_cycles_apply_default_rule_and_authorization_fails_closed()
    {
        ManagerScoresheetService service = Service(new User(manager, QuestRoles.Manager));
        ManagerReportingCyclesView result = await service.ReportingCyclesAsync(default);
        Assert.Equal(cycle, result.DefaultCycleId);
        Assert.Equal(3, result.Cycles.Count);

        WorkflowException participant = await Assert.ThrowsAsync<WorkflowException>(() => Service(new User(active, QuestRoles.Participant)).ReportingCyclesAsync(default));
        WorkflowException anonymous = await Assert.ThrowsAsync<WorkflowException>(() => Service(new Anonymous()).ReportingCyclesAsync(default));
        Assert.Equal((403, "Forbidden"), (participant.Status, participant.Code));
        Assert.Equal((401, "Unauthenticated"), (anonymous.Status, anonymous.Code));
    }

    [Fact]
    public async Task Summary_includes_complete_roster_and_reconciles_signed_cycle_ledger()
    {
        ManagerScoresheetView result = await Service().ScoresheetAsync(cycle, default);
        Assert.Equal(3, result.Rows.Count);
        ScoresheetRow row = Assert.Single(result.Rows, x => x.ParticipantId == active);
        Assert.Equal(100, row.TotalXp);
        Assert.Equal((90, 5, 5), (row.BySource.TaskApprovalXp, row.BySource.ManualAwardXp, row.BySource.RaidXp));
        Assert.Equal((110, -20, 10, -10), (row.ByEntryType.GrantXp, row.ByEntryType.ReversalXp, row.ByEntryType.CorrectionXp, row.ByEntryType.NetAdjustmentXp));
        Assert.Contains(result.Rows, x => x.ParticipantId == withdrawn && x.ParticipantStatus == CycleParticipantStatus.Withdrawn && x.TotalXp == 0);
        Assert.Contains(result.Rows, x => x.ParticipantId == inactive && x.ParticipantStatus == CycleParticipantStatus.Inactive && x.TotalXp == 0);
        Assert.DoesNotContain(result.Rows, x => x.ParticipantId == outsider);
        Assert.Equal([active, inactive, withdrawn], result.Rows.Select(x => x.ParticipantId));

        ManagerScoresheetView empty = await Service().ScoresheetAsync(emptyCycle, default);
        Assert.Empty(empty.Rows);
        WorkflowException missing = await Assert.ThrowsAsync<WorkflowException>(() => Service().ScoresheetAsync(Guid.NewGuid(), default));
        Assert.Equal((404, "ReportingCycleNotFound"), (missing.Status, missing.Code));
    }

    [Fact]
    public async Task Detail_returns_labels_raw_entries_and_stable_keyset_pages()
    {
        var seen = new List<XpActivityItem>();
        string? cursor = null;
        do
        {
            ScoresheetParticipantDetail page = await Service().ParticipantAsync(active, cycle, 2, cursor, default);
            Assert.Equal(100, page.TotalXp);
            seen.AddRange(page.Items);
            cursor = page.NextCursor;
        } while (cursor is not null);

        Assert.Equal(5, seen.Count);
        Assert.Equal(5, seen.Select(x => x.Id).Distinct().Count());
        Assert.Contains(seen, x => x.EntryType == XPEntryType.Reversal && x.Amount == -20);
        Assert.Contains(seen, x => x.EntryType == XPEntryType.Correction && x.Amount == 10);
        Assert.Contains(seen, x => x.Source.Label == "Scoresheet Challenge · Scoresheet Task");
        Assert.Contains(seen, x => x.Source.Label == "Scoresheet Award");
        Assert.Contains(seen, x => x.Source.Label == "Scoresheet Raid");
        Assert.All(seen, x => Assert.True(x.AwardedAt > now.AddMonths(1))); // CycleId, not AwardedAt, owns attribution.

        ScoresheetParticipantDetail empty = await Service().ParticipantAsync(inactive, cycle, 50, null, default);
        Assert.Empty(empty.Items);
        WorkflowException nonRoster = await Assert.ThrowsAsync<WorkflowException>(() => Service().ParticipantAsync(outsider, cycle, 50, null, default));
        WorkflowException badCursor = await Assert.ThrowsAsync<WorkflowException>(() => Service().ParticipantAsync(active, cycle, 50, "bad", default));
        Assert.Equal((404, "ScoresheetParticipantNotFound"), (nonRoster.Status, nonRoster.Code));
        Assert.Equal((400, "InvalidScoresheetCursor"), (badCursor.Status, badCursor.Code));
    }

    private async Task Seed()
    {
        db.Participants.AddRange(
            new Participant { Id = manager, DisplayName = "Manager Synthetic", CreatedAt = now },
            new Participant { Id = active, DisplayName = "Alpha", CreatedAt = now },
            new Participant { Id = withdrawn, DisplayName = "Zulu", CreatedAt = now },
            new Participant { Id = inactive, DisplayName = "Beta", CreatedAt = now },
            new Participant { Id = outsider, DisplayName = "Outsider", CreatedAt = now });
        db.Cycles.AddRange(
            new Cycle { Id = cycle, Code = "AUG-2026", Name = "August", Status = CycleStatus.Active, StartsAt = now.AddDays(-20), EndsAt = now.AddDays(10), CreatedAt = now, CreatedByParticipantId = manager },
            new Cycle { Id = emptyCycle, Code = "JUL-2026", Name = "July", Status = CycleStatus.Closing, StartsAt = now.AddMonths(-1), EndsAt = now, CreatedAt = now, CreatedByParticipantId = manager },
            new Cycle { Id = Guid.Parse("82000000-0000-4000-8000-000000000003"), Code = "JUN-2026", Name = "June", Status = CycleStatus.Finalised, StartsAt = now.AddMonths(-2), EndsAt = now.AddMonths(-1), CreatedAt = now, CreatedByParticipantId = manager });
        db.CycleParticipants.AddRange(
            new CycleParticipant { CycleId = cycle, ParticipantId = active, Status = CycleParticipantStatus.Active, JoinedAt = now },
            new CycleParticipant { CycleId = cycle, ParticipantId = withdrawn, Status = CycleParticipantStatus.Withdrawn, JoinedAt = now },
            new CycleParticipant { CycleId = cycle, ParticipantId = inactive, Status = CycleParticipantStatus.Inactive, JoinedAt = now });
        db.Challenges.Add(new Challenge { Id = challenge, CycleId = cycle, Name = "Scoresheet Challenge", Description = "Synthetic", Category = "Synthetic", Status = ChallengeStatus.Open, OpenAt = now.AddDays(-1), DueAt = now.AddDays(1), CloseAt = now.AddDays(2), CreatedAt = now, CreatedByParticipantId = manager });
        db.ChallengeTasks.Add(new ChallengeTask { Id = task, ChallengeId = challenge, Name = "Scoresheet Task", XP = 100, EvidenceRequirement = EvidenceRequirement.Text, ScoringMode = ScoringMode.Individual, SortOrder = 1 });
        db.Submissions.Add(new Submission { Id = submission, CycleId = cycle, ClaimantId = active, ChallengeId = challenge, TaskId = task, Status = SubmissionStatus.Approved, SubmittedAt = now, LastUpdatedAt = now });
        db.SubmissionBeneficiaries.Add(new SubmissionBeneficiary { SubmissionId = submission, ParticipantId = active, CycleId = cycle, AddedAt = now, AddedByParticipantId = active });
        db.AwardCategories.Add(new AwardCategory { Id = award, CycleId = cycle, Code = "SCORE", Name = "Scoresheet Award" });
        db.RaidSessions.Add(new RaidSession { Id = raid, CycleId = cycle, Name = "Scoresheet Raid", OccurredAt = now });
        Guid grant = Guid.Parse("84000000-0000-4000-8000-000000000001");
        DateTimeOffset outside = now.AddMonths(2);
        db.XPEntries.AddRange(
            Entry(grant, 100, XPEntryType.Grant, XPSourceType.TaskApproval, outside),
            Entry(Guid.Parse("84000000-0000-4000-8000-000000000002"), -20, XPEntryType.Reversal, XPSourceType.TaskApproval, outside.AddMinutes(1), grant),
            Entry(Guid.Parse("84000000-0000-4000-8000-000000000003"), 10, XPEntryType.Correction, XPSourceType.TaskApproval, outside.AddMinutes(2), grant),
            Entry(Guid.Parse("84000000-0000-4000-8000-000000000004"), 5, XPEntryType.Grant, XPSourceType.ManualAward, outside.AddMinutes(3)),
            Entry(Guid.Parse("84000000-0000-4000-8000-000000000005"), 5, XPEntryType.Grant, XPSourceType.Raid, outside.AddMinutes(4)));
        await db.SaveChangesAsync();
    }

    private XPEntry Entry(Guid id, int amount, XPEntryType entryType, XPSourceType sourceType, DateTimeOffset awardedAt, Guid? reverses = null) => new()
    {
        Id = id, ParticipantId = active, CycleId = cycle, Amount = amount, EntryType = entryType, SourceType = sourceType,
        ChallengeId = sourceType == XPSourceType.TaskApproval ? challenge : null, TaskId = sourceType == XPSourceType.TaskApproval ? task : null,
        SubmissionId = sourceType == XPSourceType.TaskApproval ? submission : null,
        AwardCategoryId = sourceType == XPSourceType.ManualAward ? award : null, RaidSessionId = sourceType == XPSourceType.Raid ? raid : null,
        Reason = entryType.ToString(), AwardedByParticipantId = manager, AwardedAt = awardedAt, ReversesEntryId = reverses
    };

    private ManagerScoresheetService Service(IQuestCurrentUser? user = null) => new(db, user ?? new User(manager, QuestRoles.Manager));
    private QuestDbContext Context() => new(new DbContextOptionsBuilder<QuestDbContext>().UseSqlServer(connection).Options);
    private sealed class User(Guid id, string role) : IQuestCurrentUser { public QuestUserIdentity Identity { get; } = new(true, id, "Synthetic", [role]); }
    private sealed class Anonymous : IQuestCurrentUser { public QuestUserIdentity Identity => QuestUserIdentity.Anonymous; }
}
