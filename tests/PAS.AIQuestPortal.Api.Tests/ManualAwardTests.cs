using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PAS.AIQuestPortal.Api.Authentication;
using PAS.AIQuestPortal.Api.Configuration;
using PAS.AIQuestPortal.Api.Data;
using PAS.AIQuestPortal.Api.ManualAwards;
using PAS.AIQuestPortal.Api.Reporting;
using PAS.AIQuestPortal.Api.Workflow;
using Xunit;

namespace PAS.AIQuestPortal.Api.Tests;

public sealed class ManualAwardTests : IAsyncLifetime
{
    private readonly string connection;
    private readonly DateTimeOffset now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private readonly Guid manager = Guid.NewGuid(), active = Guid.NewGuid(), withdrawn = Guid.NewGuid(), inactive = Guid.NewGuid(), outsider = Guid.NewGuid();
    private readonly Guid cycle = Guid.NewGuid(), closing = Guid.NewGuid(), finalised = Guid.NewGuid(), otherCycle = Guid.NewGuid();
    private readonly Guid globalCategory = Guid.NewGuid(), cycleCategory = Guid.NewGuid(), inactiveCategory = Guid.NewGuid(), otherCategory = Guid.NewGuid();
    private QuestDbContext db = null!;

    public ManualAwardTests()
    {
        string basis = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION") ?? "Server=localhost,1433;Database=master;User Id=sa;Password=Local-only-validation-Passw0rd!;TrustServerCertificate=True";
        connection = new SqlConnectionStringBuilder(basis) { InitialCatalog = $"PasAiQuestManualAward_{Guid.NewGuid():N}" }.ConnectionString;
    }

    public async Task InitializeAsync() { db = Context(); await db.Database.EnsureCreatedAsync(); await Seed(); }
    public async Task DisposeAsync() { await db.DisposeAsync(); await using QuestDbContext cleanup = Context(); await cleanup.Database.EnsureDeletedAsync(); }

    [Fact]
    public async Task Options_filter_roster_categories_and_unavailable_cycles()
    {
        ManualAwardOptionsView options = await Service().OptionsAsync(cycle, default);
        Assert.Equal(cycle, options.Cycle.Id);
        Assert.Equal(new[] { active }, options.Participants.Select(x => x.ParticipantId));
        Assert.Equal(new[] { cycleCategory, globalCategory }.Order(), options.Categories.Select(x => x.AwardCategoryId).Order());
        await AssertCode("ManualAwardCycleUnavailable", () => Service().OptionsAsync(finalised, default));
        await AssertCode("ManualAwardCycleNotFound", () => Service().OptionsAsync(Guid.NewGuid(), default));
        await AssertCode("Forbidden", () => Service(new User(active, QuestRoles.Participant)).OptionsAsync(cycle, default));
        await AssertCode("Unauthenticated", () => Service(new Anonymous()).OptionsAsync(cycle, default));
    }

    [Fact]
    public async Task Create_is_append_only_idempotent_and_updates_scoresheet_by_explicit_cycle()
    {
        Guid requestId = Guid.NewGuid();
        var command = new ManualAwardCommand(requestId, cycle, active, globalCategory, 10, "  Great contribution.  ");
        ManualAwardView first = await Service().CreateAsync(command, default);
        ManualAwardView replay = await Service().CreateAsync(command, default);
        Assert.Equal(requestId, first.Id); Assert.Equal(first, replay);
        Assert.Equal((XPEntryType.Grant, XPSourceType.ManualAward, "Great contribution."), (first.EntryType, first.SourceType, first.Reason));
        XPEntry entry = await db.XPEntries.SingleAsync(x => x.Id == requestId);
        Assert.Equal((cycle, active, globalCategory, manager, now), (entry.CycleId, entry.ParticipantId, entry.AwardCategoryId!.Value, entry.AwardedByParticipantId, entry.AwardedAt));
        Assert.Null(entry.ChallengeId); Assert.Null(entry.TaskId); Assert.Null(entry.SubmissionId); Assert.Null(entry.RaidSessionId); Assert.Null(entry.ReversesEntryId);
        await AssertCode("ManualAwardRequestConflict", () => Service().CreateAsync(command with { Amount = 11 }, default));
        Assert.Single(await db.XPEntries.Where(x => x.Id == requestId).ToListAsync()); Assert.Empty(await db.CycleEvents.ToListAsync());

        ScoresheetRow row = Assert.Single((await new ManagerScoresheetService(db, new User(manager, QuestRoles.Manager)).ScoresheetAsync(cycle, default)).Rows, x => x.ParticipantId == active);
        Assert.Equal(10, row.TotalXp); Assert.Equal(10, row.BySource.ManualAwardXp);
        var participantUser = new User(active, QuestRoles.Participant);
        var workflow = new SubmissionWorkflowService(db, participantUser, new FixedClock(now));
        var reporting = new ParticipantReportingService(db, participantUser, workflow);
        Assert.Equal(10, (await reporting.DashboardAsync(cycle, default)).TotalXp);
        XpActivityItem activity = Assert.Single((await reporting.XpActivityAsync(cycle, 25, null, default)).Items);
        Assert.Equal((XPSourceType.ManualAward, "Global"), (activity.SourceType, activity.Source.Label));
        Assert.Equal(10, Assert.Single(await reporting.LeaderboardAsync(cycle, default)).TotalXp);

        Guid concurrentId = Guid.NewGuid(); var concurrent = command with { RequestId = concurrentId, AwardCategoryId = cycleCategory };
        await using QuestDbContext secondDb = Context();
        ManualAwardView[] results = await Task.WhenAll(Service().CreateAsync(concurrent, default), new ManualAwardService(secondDb, new User(manager, QuestRoles.Manager), new FixedClock(now)).CreateAsync(concurrent, default));
        Assert.Equal(results[0], results[1]); Assert.Equal(1, await db.XPEntries.CountAsync(x => x.Id == concurrentId));
    }

    [Fact]
    public async Task Create_rejects_ineligible_cycle_participant_category_amount_and_reason_without_rows()
    {
        ManualAwardCommand Valid() => new(Guid.NewGuid(), cycle, active, globalCategory, 10, "Reason");
        await AssertCode("ManualAwardParticipantIneligible", () => Service().CreateAsync(Valid() with { ParticipantId = withdrawn }, default));
        await AssertCode("ManualAwardParticipantIneligible", () => Service().CreateAsync(Valid() with { ParticipantId = inactive }, default));
        await AssertCode("ManualAwardParticipantNotFound", () => Service().CreateAsync(Valid() with { ParticipantId = outsider }, default));
        await AssertCode("ManualAwardCycleUnavailable", () => Service().CreateAsync(Valid() with { CycleId = finalised }, default));
        Assert.Equal(closing, (await Service().CreateAsync(Valid() with { RequestId = Guid.NewGuid(), CycleId = closing }, default)).CycleId);
        await AssertCode("AwardCategoryNotFound", () => Service().CreateAsync(Valid() with { AwardCategoryId = inactiveCategory }, default));
        await AssertCode("AwardCategoryNotFound", () => Service().CreateAsync(Valid() with { AwardCategoryId = otherCategory }, default));
        await AssertCode("AwardCategoryNotFound", () => Service().CreateAsync(Valid() with { AwardCategoryId = Guid.NewGuid() }, default));
        await AssertCode("InvalidManualAwardAmount", () => Service().CreateAsync(Valid() with { Amount = 0 }, default));
        await AssertCode("InvalidManualAwardAmount", () => Service().CreateAsync(Valid() with { Amount = -1 }, default));
        await AssertCode("ManualAwardReasonRequired", () => Service().CreateAsync(Valid() with { Reason = " " }, default));
        await AssertCode("ManualAwardReasonTooLong", () => Service().CreateAsync(Valid() with { Reason = new string('x', 2001) }, default));
    }

    private async Task Seed()
    {
        db.Participants.AddRange(new Participant { Id = manager, DisplayName = "Manager", CreatedAt = now }, new Participant { Id = active, DisplayName = "Active", CreatedAt = now }, new Participant { Id = withdrawn, DisplayName = "Withdrawn", CreatedAt = now }, new Participant { Id = inactive, DisplayName = "Inactive", CreatedAt = now }, new Participant { Id = outsider, DisplayName = "Outsider", CreatedAt = now });
        db.Cycles.AddRange(Cycle(cycle, "ACTIVE", CycleStatus.Active), Cycle(closing, "CLOSING", CycleStatus.Closing), Cycle(finalised, "FINAL", CycleStatus.Finalised), Cycle(otherCycle, "OTHER", CycleStatus.Active));
        foreach (Guid id in new[] { cycle, closing }) db.CycleParticipants.Add(new CycleParticipant { CycleId = id, ParticipantId = active, Status = CycleParticipantStatus.Active, JoinedAt = now });
        db.CycleParticipants.AddRange(new CycleParticipant { CycleId = cycle, ParticipantId = withdrawn, Status = CycleParticipantStatus.Withdrawn, JoinedAt = now }, new CycleParticipant { CycleId = cycle, ParticipantId = inactive, Status = CycleParticipantStatus.Inactive, JoinedAt = now });
        db.AwardCategories.AddRange(new AwardCategory { Id = globalCategory, Code = "GLOBAL", Name = "Global", IsActive = true }, new AwardCategory { Id = cycleCategory, CycleId = cycle, Code = "CYCLE", Name = "Cycle", IsActive = true }, new AwardCategory { Id = inactiveCategory, CycleId = cycle, Code = "INACTIVE", Name = "Inactive", IsActive = false }, new AwardCategory { Id = otherCategory, CycleId = otherCycle, Code = "OTHER", Name = "Other", IsActive = true });
        await db.SaveChangesAsync();
    }

    private Cycle Cycle(Guid id, string code, CycleStatus status) => new() { Id = id, Code = code, Name = code, Status = status, StartsAt = now.AddMonths(-2), EndsAt = now.AddMonths(-1), CreatedAt = now, CreatedByParticipantId = manager };
    private ManualAwardService Service(IQuestCurrentUser? user = null) => new(db, user ?? new User(manager, QuestRoles.Manager), new FixedClock(now));
    private QuestDbContext Context() => new(new DbContextOptionsBuilder<QuestDbContext>().UseSqlServer(connection).Options);
    private static async Task AssertCode(string code, Func<Task> action) { WorkflowException error = await Assert.ThrowsAsync<WorkflowException>(action); Assert.Equal(code, error.Code); }
    private sealed class User(Guid id, string role) : IQuestCurrentUser { public QuestUserIdentity Identity { get; } = new(true, id, "Synthetic", [role]); }
    private sealed class Anonymous : IQuestCurrentUser { public QuestUserIdentity Identity => QuestUserIdentity.Anonymous; }
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
}
