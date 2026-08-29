using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PAS.AIQuestPortal.Api.Authentication;
using PAS.AIQuestPortal.Api.ChallengeAdministration;
using PAS.AIQuestPortal.Api.Configuration;
using PAS.AIQuestPortal.Api.Data;
using PAS.AIQuestPortal.Api.Workflow;
using Xunit;

namespace PAS.AIQuestPortal.Api.Tests;

public sealed class ChallengeAdministrationTests : IAsyncLifetime
{
    private readonly string connection; private QuestDbContext db = null!; private MutableClock clock = new(new(2026, 8, 25, 10, 0, 0, TimeSpan.Zero));
    private readonly Guid manager = Guid.Parse("81000000-0000-4000-8000-000000000001"), participant = Guid.Parse("81000000-0000-4000-8000-000000000002"), cycle = Guid.Parse("82000000-0000-4000-8000-000000000001");
    public ChallengeAdministrationTests() { string basis = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION") ?? "Server=localhost,1433;Database=master;User Id=sa;Password=Local-only-validation-Passw0rd!;TrustServerCertificate=True"; connection = new SqlConnectionStringBuilder(basis) { InitialCatalog = $"PasAiQuestAdmin_{Guid.NewGuid():N}" }.ConnectionString; }
    public async Task InitializeAsync() { db = Context(); await db.Database.MigrateAsync(); DateTimeOffset now = clock.GetUtcNow(); db.Participants.AddRange(new Participant { Id = manager, DisplayName = "Synthetic Manager", CreatedAt = now }, new Participant { Id = participant, DisplayName = "Synthetic Participant", CreatedAt = now }); db.Cycles.Add(new Cycle { Id = cycle, Code = "ADMIN-26", Name = "Administration Cycle", Status = CycleStatus.Active, StartsAt = now.AddDays(-1), EndsAt = now.AddMonths(1), CreatedAt = now, CreatedByParticipantId = manager }); db.CycleParticipants.Add(new CycleParticipant { CycleId = cycle, ParticipantId = participant, Status = CycleParticipantStatus.Active, JoinedAt = now }); db.CycleParticipantEvents.Add(new CycleParticipantEvent { Id = Guid.NewGuid(), CycleId = cycle, ParticipantId = participant, SequenceNumber = 1, EventType = CycleParticipantEventType.Enrolled, FromStatus = null, ToStatus = CycleParticipantStatus.Active, Reason = "Synthetic test enrollment", ActorId = manager, OccurredAt = now }); await db.SaveChangesAsync(); }
    public async Task DisposeAsync() { await db.DisposeAsync(); await using QuestDbContext cleanup = Context(); await cleanup.Database.EnsureDeletedAsync(); }

    [Fact]
    public async Task Aggregate_create_update_add_remove_reorder_and_wire_version_are_atomic()
    {
        ChallengeAdministrationService service = Service(manager, QuestRoles.Manager); ManagerChallengeView created = await service.CreateAsync(Create(), default);
        Assert.Equal(ChallengeStatus.Draft, created.Status); Assert.True(Convert.FromBase64String(created.Version).Length > 0); Assert.Equal(2, created.Tasks.Count);
        Guid retained = created.Tasks[1].Id;
        UpdateChallengeRequest update = new(created.Version, cycle, "Updated", null, null, created.OpenAt, created.DueAt, created.CloseAt, null,
            [new(retained, "Retained first", null, 0, ScoringMode.Individual, EvidenceRequirement.None, 1), new(null, "New second", null, 15, ScoringMode.Individual, EvidenceRequirement.Text, 2)], null);
        ManagerChallengeView changed = await service.UpdateAsync(created.Id, update, default);
        Assert.NotEqual(created.Version, changed.Version); Assert.Equal(["Retained first", "New second"], changed.Tasks.Select(x => x.Name)); Assert.DoesNotContain(changed.Tasks, x => x.Id == created.Tasks[0].Id);
        Assert.Contains("\"version\":", JsonSerializer.Serialize(changed, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(2, await db.ChallengeTasks.CountAsync(x => x.ChallengeId == created.Id));
    }

    [Fact]
    public async Task Stale_two_manager_update_conflicts_and_publish_is_irreversible_idempotent_and_time_gated()
    {
        ManagerChallengeView created = await Service(manager, QuestRoles.Manager).CreateAsync(Create(openAt: clock.GetUtcNow().AddHours(1)), default); string stale = created.Version;
        await using QuestDbContext secondDb = Context(); var second = new ChallengeAdministrationService(secondDb, new User(manager, QuestRoles.Manager), clock);
        ManagerChallengeView first = await Service(manager, QuestRoles.Manager).UpdateAsync(created.Id, Update(created, name: "Manager one"), default);
        WorkflowException conflict = await Assert.ThrowsAsync<WorkflowException>(() => second.UpdateAsync(created.Id, Update(created, version: stale, name: "Manager two"), default)); Assert.Equal("ChallengeVersionConflict", conflict.Code);
        ManagerChallengeView published = await Service(manager, QuestRoles.Manager).PublishAsync(created.Id, new(first.Version), default); Assert.Equal(ChallengeStatus.Open, published.Status);
        Assert.Equal(published.Version, (await Service(manager, QuestRoles.Manager).PublishAsync(created.Id, new(stale), default)).Version);
        WorkflowException edit = await Assert.ThrowsAsync<WorkflowException>(() => Service(manager, QuestRoles.Manager).UpdateAsync(created.Id, Update(published), default)); Assert.Equal("ChallengeNotDraft", edit.Code);
        var workflow = new SubmissionWorkflowService(db, new User(participant, QuestRoles.Participant), clock); Assert.DoesNotContain(await workflow.EligibleAsync(default), x => x.Id == created.Id);
        clock.UtcNow = created.OpenAt.AddMinutes(1); Assert.Contains(await workflow.EligibleAsync(default), x => x.Id == created.Id);
    }

    [Fact]
    public async Task Validation_rejects_custom_attendance_invalid_policy_foreign_duplicate_tasks_and_dependencies()
    {
        ChallengeAdministrationService service = Service(manager, QuestRoles.Manager);
        await AssertCode("ChallengeValidationFailed", () => service.CreateAsync(Create(tasks: [Task(null, 1) with { EvidenceRequirement = EvidenceRequirement.Custom }]), default));
        await AssertCode("ChallengeValidationFailed", () => service.CreateAsync(Create(tasks: [Task(null, 1) with { ScoringMode = ScoringMode.AttendanceBased }]), default));
        await AssertCode("ChallengeValidationFailed", () => service.CreateAsync(Create(tasks: [Task(null, 1) with { ScoringMode = ScoringMode.WholeTeam }], policy: new(FormationMode.Either, 1, 2, false, null, false)), default));
        ManagerChallengeView draft = await service.CreateAsync(Create(), default); Guid foreign = Guid.NewGuid();
        await AssertCode("ChallengeValidationFailed", () => service.UpdateAsync(draft.Id, Update(draft) with { Tasks = [Task(foreign, 1)] }, default));
        await AssertCode("ChallengeValidationFailed", () => service.UpdateAsync(draft.Id, Update(draft) with { Tasks = [Task(draft.Tasks[0].Id, 1), Task(draft.Tasks[0].Id, 2)] }, default));
        db.ChallengeParticipations.Add(new ChallengeParticipation { Id = Guid.NewGuid(), ChallengeId = draft.Id, CycleId = cycle, CreatedAt = clock.GetUtcNow(), CreatedByParticipantId = manager }); await db.SaveChangesAsync();
        await AssertCode("ChallengeHasOperationalDependencies", () => service.UpdateAsync(draft.Id, Update(draft), default));
    }

    [Fact]
    public async Task Publish_requires_tasks_and_manager_role_is_enforced()
    {
        ManagerChallengeView empty = await Service(manager, QuestRoles.Manager).CreateAsync(Create(tasks: []), default);
        await AssertCode("ChallengeValidationFailed", () => Service(manager, QuestRoles.Manager).PublishAsync(empty.Id, new(empty.Version), default));
        WorkflowException forbidden = await Assert.ThrowsAsync<WorkflowException>(() => Service(participant, QuestRoles.Participant).OptionsAsync(default)); Assert.Equal(403, forbidden.Status);
    }

    [Fact]
    public async Task Browser_millisecond_round_trip_preserves_draft_dates_while_real_draft_date_edits_persist()
    {
        DateTimeOffset precise = clock.GetUtcNow().AddDays(1).AddTicks(4321);
        ManagerChallengeView created = await Service(manager, QuestRoles.Manager).CreateAsync(new(cycle, "Precision Draft", null, null, precise, precise.AddDays(1), precise.AddDays(2), null, [Task(null, 1), Task(null, 2)], null), default);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Challenges SET OpenAt={precise}, DueAt={precise.AddDays(1)}, CloseAt={precise.AddDays(2)} WHERE Id={created.Id}");
        db.ChangeTracker.Clear(); created = await Service(manager, QuestRoles.Manager).GetAsync(created.Id, default);
        DateTimeOffset persistedOpen = created.OpenAt, persistedDue = created.DueAt, persistedClose = created.CloseAt;
        static DateTimeOffset Browser(DateTimeOffset value) => new(value.UtcTicks - value.UtcTicks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
        UpdateChallengeRequest roundTrip = Update(created) with { OpenAt = Browser(created.OpenAt), DueAt = Browser(created.DueAt), CloseAt = Browser(created.CloseAt), Tasks = [Task(created.Tasks[1].Id, 1), Task(created.Tasks[0].Id, 2)] };
        ManagerChallengeView reordered = await Service(manager, QuestRoles.Manager).UpdateAsync(created.Id, roundTrip, default);
        Assert.Equal((persistedOpen, persistedDue, persistedClose), (reordered.OpenAt, reordered.DueAt, reordered.CloseAt));
        Assert.Equal(created.Tasks[1].Id, reordered.Tasks[0].Id);
        Assert.Empty(await db.ChallengeDeadlineChanges.Where(x => x.ChallengeId == created.Id).ToListAsync());

        DateTimeOffset changedDue = reordered.DueAt.AddMinutes(1);
        ManagerChallengeView changed = await Service(manager, QuestRoles.Manager).UpdateAsync(created.Id, Update(reordered) with { DueAt = changedDue }, default);
        Assert.Equal(Browser(changedDue), changed.DueAt);
        Assert.Empty(await db.ChallengeDeadlineChanges.Where(x => x.ChallengeId == created.Id).ToListAsync());
    }

    private CreateChallengeRequest Create(DateTimeOffset? openAt = null, IReadOnlyList<ChallengeTaskWrite>? tasks = null, ChallengePolicyWrite? policy = null) => new(cycle, "Synthetic Draft", null, null, openAt ?? clock.GetUtcNow().AddMinutes(-1), clock.GetUtcNow().AddDays(2), clock.GetUtcNow().AddDays(3), null, tasks ?? [Task(null, 1), Task(null, 2)], policy);
    private static ChallengeTaskWrite Task(Guid? id, int order) => new(id, $"Task {order}", null, order * 5, ScoringMode.Individual, EvidenceRequirement.Text, order);
    private static UpdateChallengeRequest Update(ManagerChallengeView view, string? version = null, string? name = null) => new(version ?? view.Version, view.CycleId, name ?? view.Name, view.Description, view.Category, view.OpenAt, view.DueAt, view.CloseAt, view.HeroImageReference, view.Tasks.Select(x => new ChallengeTaskWrite(x.Id, x.Name, x.Description, x.XP, x.ScoringMode, x.EvidenceRequirement, x.SortOrder)).ToArray(), view.ParticipationPolicy is null ? null : new(view.ParticipationPolicy.FormationMode, view.ParticipationPolicy.MinMembers, view.ParticipationPolicy.MaxMembers, view.ParticipationPolicy.AllowSolo, view.ParticipationPolicy.FormationDeadline, view.ParticipationPolicy.LockAfterStart));
    private ChallengeAdministrationService Service(Guid id, string role) => new(db, new User(id, role), clock);
    private QuestDbContext Context() => new(new DbContextOptionsBuilder<QuestDbContext>().UseSqlServer(connection).Options);
    private static async Task AssertCode(string code, Func<Task> action) { WorkflowException error = await Assert.ThrowsAsync<WorkflowException>(action); Assert.Equal(code, error.Code); }
    private sealed class User(Guid id, string role) : IQuestCurrentUser { public QuestUserIdentity Identity { get; } = new(true, id, "Synthetic", [role]); }
    private sealed class MutableClock(DateTimeOffset now) : TimeProvider { public DateTimeOffset UtcNow { get; set; } = now; public override DateTimeOffset GetUtcNow() => UtcNow; }
}
