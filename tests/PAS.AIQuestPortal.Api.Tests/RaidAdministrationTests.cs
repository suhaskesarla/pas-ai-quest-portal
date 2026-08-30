using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PAS.AIQuestPortal.Api.Authentication;
using PAS.AIQuestPortal.Api.Configuration;
using PAS.AIQuestPortal.Api.Data;
using PAS.AIQuestPortal.Api.RaidAdministration;
using PAS.AIQuestPortal.Api.CycleAdministration;
using PAS.AIQuestPortal.Api.Workflow;
using Xunit;

namespace PAS.AIQuestPortal.Api.Tests;

public sealed class RaidAdministrationTests : IAsyncLifetime
{
    private readonly string connection; private QuestDbContext db = null!;
    private readonly Guid manager = Guid.NewGuid(), participant = Guid.NewGuid(), inactive = Guid.NewGuid(), activeCycle = Guid.NewGuid(), closingCycle = Guid.NewGuid(), finalisedCycle = Guid.NewGuid();
    private readonly DateTimeOffset now = new(2026, 8, 29, 10, 0, 0, TimeSpan.Zero);
    public RaidAdministrationTests() { string basis = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION") ?? throw new InvalidOperationException("TEST_SQL_CONNECTION is required."); connection = new SqlConnectionStringBuilder(basis) { InitialCatalog = $"PasAiQuestRaid_{Guid.NewGuid():N}" }.ConnectionString; }
    public async Task InitializeAsync() { db = Context(); await db.Database.MigrateAsync(); await Seed(); }
    public async Task DisposeAsync() { await db.DisposeAsync(); await using QuestDbContext cleanup = Context(); await cleanup.Database.EnsureDeletedAsync(); }

    [Fact]
    public async Task Sessions_create_edit_version_and_reference_immutability_follow_cycle_rules()
    {
        RaidAdministrationService service = Service(); RaidSessionView active = await service.CreateAsync(new(activeCycle, " Active Raid ", now.AddDays(5)), default); Assert.Equal("Active Raid", active.Name); Assert.True(Convert.FromBase64String(active.RowVersion).Length > 0);
        RaidSessionView closing = await service.CreateAsync(new(closingCycle, "Closing Raid", now), default); Assert.True(closing.AllowedActions.CanEdit);
        await Code("RaidCycleUnavailable", () => service.CreateAsync(new(finalisedCycle, "No", now), default));
        RaidSessionView edited = await service.UpdateAsync(active.Id, new(active.RowVersion, "Edited", now.AddDays(6)), default); Assert.Equal("Edited", edited.Name); await Code("RaidSessionVersionConflict", () => service.UpdateAsync(active.Id, new(active.RowVersion, "Stale", now), default));
        await service.UpdateEntitlementAsync(activeCycle, participant, PassType.Physical, new(1, null), default); await service.CreateParticipationAsync(active.Id, new(participant, PassType.Physical), default);
        await Code("RaidSessionImmutable", () => service.UpdateAsync(active.Id, new(edited.RowVersion, "Blocked", now), default));
    }

    [Fact]
    public async Task Entitlement_and_participation_preserve_capacity_natural_key_and_append_only_rules()
    {
        RaidAdministrationService service = Service(); RaidSessionView session = await service.CreateAsync(new(activeCycle, "Use Raid", now), default);
        RaidEntitlementView created = await service.UpdateEntitlementAsync(activeCycle, participant, PassType.Physical, new(2, null), default); Assert.Equal((2, 0, 2), (created.Assigned, created.Used, created.Remaining));
        RaidEntitlementView increased = await service.UpdateEntitlementAsync(activeCycle, participant, PassType.Physical, new(3, created.RowVersion), default); await Code("RaidEntitlementVersionConflict", () => service.UpdateEntitlementAsync(activeCycle, participant, PassType.Physical, new(2, created.RowVersion), default));
        RaidParticipationView used = await service.CreateParticipationAsync(session.Id, new(participant, PassType.Physical), default); RaidParticipationView replay = await service.CreateParticipationAsync(session.Id, new(participant, PassType.Physical), default); Assert.Equal(used.ParticipationId, replay.ParticipationId);
        await Code("RaidParticipationConflict", () => service.CreateParticipationAsync(session.Id, new(participant, PassType.Remote), default));
        RaidEntitlementView reduced = await service.UpdateEntitlementAsync(activeCycle, participant, PassType.Physical, new(1, increased.RowVersion), default); Assert.Equal((1, 1, 0), (reduced.Assigned, reduced.Used, reduced.Remaining)); await Code("RaidEntitlementBelowUsed", () => service.UpdateEntitlementAsync(activeCycle, participant, PassType.Physical, new(0, reduced.RowVersion), default));
        Assert.Empty(await db.XPEntries.ToListAsync()); db.ChangeTracker.Clear(); RaidParticipation persisted = await db.RaidParticipations.SingleAsync(x => x.Id == used.ParticipationId); persisted.UsedAt = now.AddDays(1); await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear(); persisted = await db.RaidParticipations.SingleAsync(x => x.Id == used.ParticipationId); db.Remove(persisted); await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Raid_xp_is_idempotent_append_only_and_uses_authoritative_session_cycle()
    {
        RaidAdministrationService service = Service(); RaidSessionView session = await service.CreateAsync(new(closingCycle, "XP Raid", now.AddMonths(2)), default); Guid request = Guid.NewGuid();
        RaidXpView created = await service.CreateXpAsync(session.Id, new(request, participant, 12, " Raid contribution "), default); RaidXpView replay = await service.CreateXpAsync(session.Id, new(request, participant, 12, "Raid contribution"), default);
        Assert.Equal(created.Id, replay.Id); Assert.Equal((request, participant, closingCycle, 12, XPEntryType.Grant, XPSourceType.Raid), (created.Id, created.ParticipantId, created.CycleId, created.Amount, created.EntryType, created.SourceType)); Assert.Equal(1, await db.XPEntries.CountAsync(x => x.Id == request));
        XPEntry entry = await db.XPEntries.SingleAsync(x => x.Id == request); Assert.Equal(session.Id, entry.RaidSessionId); Assert.Null(entry.AwardCategoryId); Assert.Null(entry.SubmissionId); Assert.Null(entry.TaskId);
        await Code("RaidXpRequestConflict", () => service.CreateXpAsync(session.Id, new(request, participant, 13, "Raid contribution"), default)); await Code("InvalidRaidXpAmount", () => service.CreateXpAsync(session.Id, new(Guid.NewGuid(), participant, 0, "No"), default));
    }

    [Fact]
    public async Task Inactive_participants_and_finalised_cycles_are_readable_but_reject_writes()
    {
        RaidAdministrationService service = Service(); RaidSession final = new() { Id = Guid.NewGuid(), CycleId = finalisedCycle, Name = "Historic", OccurredAt = now }; db.RaidSessions.Add(final); await db.SaveChangesAsync(); Assert.Equal("Historic", (await service.GetAsync(final.Id, default)).Name);
        await Code("RaidCycleUnavailable", () => service.UpdateEntitlementAsync(finalisedCycle, participant, PassType.Physical, new(1, null), default)); RaidSessionView active = await service.CreateAsync(new(activeCycle, "Eligibility", now), default);
        await Code("RaidParticipantIneligible", () => service.UpdateEntitlementAsync(activeCycle, inactive, PassType.Physical, new(1, null), default)); await Code("RaidParticipantIneligible", () => service.CreateXpAsync(active.Id, new(Guid.NewGuid(), inactive, 5, "No"), default));
    }

    [Fact]
    public async Task Finalisation_and_participant_deactivation_serialize_with_raid_writes()
    {
        Cycle closing = await db.Cycles.AsNoTracking().SingleAsync(x => x.Id == closingCycle); string cycleVersion = Convert.ToBase64String(closing.RowVersion);
        await using QuestDbContext finaliseDb = Context(); await using QuestDbContext createDb = Context();
        Task<ManagerCycleDetail> finalise = new CycleAdministrationService(finaliseDb, new User(manager), new Clock(now)).FinaliseAsync(closingCycle, new(cycleVersion, "Concurrent finalisation"), default);
        Task<RaidSessionView> create = new RaidAdministrationService(createDb, new User(manager), new Clock(now)).CreateAsync(new(closingCycle, "Concurrent Raid", now), default);
        try { await create; } catch (WorkflowException e) { Assert.Equal("RaidCycleUnavailable", e.Code); } await finalise; Assert.Equal(CycleStatus.Finalised, (await db.Cycles.AsNoTracking().SingleAsync(x => x.Id == closingCycle)).Status);

        RaidSessionView session = await Service().CreateAsync(new(activeCycle, "Status Race", now), default); CycleParticipant membership = await db.CycleParticipants.AsNoTracking().SingleAsync(x => x.CycleId == activeCycle && x.ParticipantId == participant); string participantVersion = Convert.ToBase64String(membership.RowVersion);
        await using QuestDbContext statusDb = Context(); await using QuestDbContext xpDb = Context();
        Task<ManagerCycleParticipant> deactivate = new CycleAdministrationService(statusDb, new User(manager), new Clock(now)).ChangeParticipantStatusAsync(activeCycle, participant, new(participantVersion, CycleParticipantStatus.Inactive, "Concurrent deactivation"), default);
        Task<RaidXpView> xp = new RaidAdministrationService(xpDb, new User(manager), new Clock(now)).CreateXpAsync(session.Id, new(Guid.NewGuid(), participant, 5, "Concurrent Raid XP"), default);
        try { await xp; } catch (WorkflowException e) { Assert.Equal("RaidParticipantIneligible", e.Code); } await deactivate; Assert.Equal(CycleParticipantStatus.Inactive, (await db.CycleParticipants.AsNoTracking().SingleAsync(x => x.CycleId == activeCycle && x.ParticipantId == participant)).Status);
    }

    [Fact]
    public async Task Entitlement_decrease_and_pass_use_overlap_never_make_remaining_negative()
    {
        RaidAdministrationService service = Service(); RaidSessionView session = await service.CreateAsync(new(activeCycle, "Capacity Race", now), default); RaidEntitlementView entitlement = await service.UpdateEntitlementAsync(activeCycle, participant, PassType.Physical, new(1, null), default);
        await using QuestDbContext decreaseDb = Context(); await using QuestDbContext useDb = Context();
        Task<RaidEntitlementView> decrease = new RaidAdministrationService(decreaseDb, new User(manager), new Clock(now)).UpdateEntitlementAsync(activeCycle, participant, PassType.Physical, new(0, entitlement.RowVersion), default);
        Task<RaidParticipationView> use = new RaidAdministrationService(useDb, new User(manager), new Clock(now)).CreateParticipationAsync(session.Id, new(participant, PassType.Physical), default);
        try { await decrease; } catch (WorkflowException e) { Assert.Equal("RaidEntitlementBelowUsed", e.Code); }
        try { await use; } catch (WorkflowException e) { Assert.Contains(e.Code, new[] { "RaidPassExhausted", "RaidEntitlementNotFound" }); }
        db.ChangeTracker.Clear(); RaidEntitlement persisted = await db.RaidEntitlements.AsNoTracking().SingleAsync(x => x.CycleId == activeCycle && x.ParticipantId == participant && x.PassType == PassType.Physical); int used = await db.RaidParticipations.CountAsync(x => x.CycleId == activeCycle && x.ParticipantId == participant && x.PassType == PassType.Physical); Assert.True(used <= persisted.AssignedCount);
    }

    private async Task Seed()
    {
        db.Participants.AddRange(new Participant { Id = manager, DisplayName = "Synthetic Manager", CreatedAt = now }, new Participant { Id = participant, DisplayName = "Synthetic Participant", CreatedAt = now }, new Participant { Id = inactive, DisplayName = "Synthetic Inactive", CreatedAt = now });
        db.Cycles.AddRange(Cycle(activeCycle, "RAID-A", CycleStatus.Active), Cycle(closingCycle, "RAID-C", CycleStatus.Closing), Cycle(finalisedCycle, "RAID-F", CycleStatus.Finalised));
        foreach ((Guid cycleId, Guid person) in new[] { (activeCycle, participant), (closingCycle, participant), (finalisedCycle, participant), (activeCycle, inactive) }) { db.CycleParticipants.Add(new CycleParticipant { CycleId = cycleId, ParticipantId = person, Status = CycleParticipantStatus.Active, JoinedAt = now }); db.CycleParticipantEvents.Add(new CycleParticipantEvent { Id = Guid.NewGuid(), CycleId = cycleId, ParticipantId = person, SequenceNumber = 1, EventType = CycleParticipantEventType.Enrolled, FromStatus = null, ToStatus = CycleParticipantStatus.Active, Reason = "Synthetic raid enrollment", ActorId = manager, OccurredAt = now }); }
        await db.SaveChangesAsync(); CycleParticipant row = (await db.CycleParticipants.FindAsync(activeCycle, inactive))!; row.Status = CycleParticipantStatus.Inactive; row.LeftAt = now; db.CycleParticipantEvents.Add(new CycleParticipantEvent { Id = Guid.NewGuid(), CycleId = activeCycle, ParticipantId = inactive, SequenceNumber = 2, EventType = CycleParticipantEventType.StatusChanged, FromStatus = CycleParticipantStatus.Active, ToStatus = CycleParticipantStatus.Inactive, Reason = "Synthetic inactive", ActorId = manager, OccurredAt = now }); await db.SaveChangesAsync();
    }
    private Cycle Cycle(Guid id, string code, CycleStatus status) => new() { Id = id, Code = code, Name = code, Status = status, StartsAt = now.AddMonths(-1), EndsAt = now.AddMonths(1), CreatedAt = now, CreatedByParticipantId = manager };
    private RaidAdministrationService Service() => new(db, new User(manager), new Clock(now)); private QuestDbContext Context() => new(new DbContextOptionsBuilder<QuestDbContext>().UseSqlServer(connection).Options);
    private static async Task Code(string expected, Func<Task> action) { WorkflowException error = await Assert.ThrowsAsync<WorkflowException>(action); Assert.Equal(expected, error.Code); }
    private sealed class User(Guid id) : IQuestCurrentUser { public QuestUserIdentity Identity { get; } = new(true, id, "Manager", [QuestRoles.Manager]); } private sealed class Clock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
}

public sealed class RaidAdministrationMigrationTests
{
    private const string Previous = "20260828051810_AddCycleAdministration";
    [Fact] public async Task Migration_preserves_compatible_history_and_adds_rowversions_and_indexes() => await WithDatabase(async (db, _) => { await db.GetService<IMigrator>().MigrateAsync(Previous); (Guid participant, Guid session) = await SeedPreMigration(db, duplicate: false); await db.GetService<IMigrator>().MigrateAsync(); Assert.Equal(1, await Scalar(db, "SELECT COUNT(*) FROM RaidParticipations WHERE ParticipantId=@p AND RaidSessionId=@s", participant, session)); Assert.Equal(2, await Scalar(db, "SELECT COUNT(*) FROM sys.columns WHERE name='RowVersion' AND object_id IN (OBJECT_ID('RaidSessions'),OBJECT_ID('RaidEntitlements'))")); Assert.Equal(1, await Scalar(db, "SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID('RaidParticipations') AND name='IX_RaidParticipations_ParticipantId_RaidSessionId' AND is_unique=1")); });
    [Fact] public async Task Migration_fails_closed_for_cross_pass_duplicate_without_rewriting_history() => await WithDatabase(async (db, _) => { await db.GetService<IMigrator>().MigrateAsync(Previous); (Guid participant, Guid session) = await SeedPreMigration(db, duplicate: true); await Assert.ThrowsAsync<SqlException>(() => db.GetService<IMigrator>().MigrateAsync()); Assert.Equal(2, await Scalar(db, "SELECT COUNT(*) FROM RaidParticipations WHERE ParticipantId=@p AND RaidSessionId=@s", participant, session)); });

    private static async Task<(Guid Participant, Guid Session)> SeedPreMigration(QuestDbContext db, bool duplicate)
    {
        Guid manager = Guid.NewGuid(), participant = Guid.NewGuid(), cycle = Guid.NewGuid(), session = Guid.NewGuid(); DateTimeOffset now = new(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT Participants(Id,DisplayName,IsActive,CreatedAt) VALUES({manager},{"Manager"},{true},{now}),({participant},{"Participant"},{true},{now}); INSERT Cycles(Id,Code,Name,Status,StartsAt,EndsAt,CreatedAt,CreatedByParticipantId) VALUES({cycle},{"RAID-MIG"},{"Raid Migration"},{"Active"},{now},{now.AddMonths(1)},{now},{manager}); INSERT CycleParticipants(CycleId,ParticipantId,Status,JoinedAt) VALUES({cycle},{participant},{"Active"},{now}); INSERT RaidSessions(Id,CycleId,Name,OccurredAt) VALUES({session},{cycle},{"Historic Raid"},{now}); INSERT RaidEntitlements(ParticipantId,CycleId,PassType,AssignedCount) VALUES({participant},{cycle},{"Physical"},{2}),({participant},{cycle},{"Remote"},{2}); INSERT RaidParticipations(Id,ParticipantId,RaidSessionId,CycleId,PassType,UsedAt) VALUES({Guid.NewGuid()},{participant},{session},{cycle},{"Physical"},{now});");
        if (duplicate) await db.Database.ExecuteSqlInterpolatedAsync($"INSERT RaidParticipations(Id,ParticipantId,RaidSessionId,CycleId,PassType,UsedAt) VALUES({Guid.NewGuid()},{participant},{session},{cycle},{"Remote"},{now.AddMinutes(1)});"); return (participant, session);
    }
    private static async Task<int> Scalar(QuestDbContext db, string sql, Guid participant = default, Guid session = default) { await db.Database.OpenConnectionAsync(); await using var command = db.Database.GetDbConnection().CreateCommand(); command.CommandText = sql; var p = command.CreateParameter(); p.ParameterName = "@p"; p.Value = participant; command.Parameters.Add(p); var s = command.CreateParameter(); s.ParameterName = "@s"; s.Value = session; command.Parameters.Add(s); return Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture); }
    private static async Task WithDatabase(Func<QuestDbContext, string, Task> test) { string basis = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION") ?? throw new InvalidOperationException("TEST_SQL_CONNECTION is required."); string connection = new SqlConnectionStringBuilder(basis) { InitialCatalog = $"PasAiQuestRaidMigration_{Guid.NewGuid():N}" }.ConnectionString; await using var db = new QuestDbContext(new DbContextOptionsBuilder<QuestDbContext>().UseSqlServer(connection).Options); try { await test(db, connection); } finally { await db.Database.CloseConnectionAsync(); await db.Database.EnsureDeletedAsync(); } }
}
