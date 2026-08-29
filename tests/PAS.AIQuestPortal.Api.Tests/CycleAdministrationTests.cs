using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PAS.AIQuestPortal.Api.Authentication;
using PAS.AIQuestPortal.Api.Configuration;
using PAS.AIQuestPortal.Api.CycleAdministration;
using PAS.AIQuestPortal.Api.Data;
using PAS.AIQuestPortal.Api.Development;
using PAS.AIQuestPortal.Api.Workflow;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace PAS.AIQuestPortal.Api.Tests;

public sealed class CycleAdministrationTests : IAsyncLifetime
{
    private readonly string connection;
    private readonly Guid manager = Guid.NewGuid(), activePerson = Guid.NewGuid(), inactivePerson = Guid.NewGuid(), enrolled = Guid.NewGuid();
    private readonly DateTimeOffset now = new(2026, 8, 28, 10, 0, 0, TimeSpan.Zero);
    private QuestDbContext db = null!;
    public CycleAdministrationTests() { string basis = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION") ?? "Server=localhost,1433;Database=master;User Id=sa;Password=Local-only-validation-Passw0rd!;TrustServerCertificate=True"; connection = new SqlConnectionStringBuilder(basis) { InitialCatalog = $"PasAiQuestCycleAdmin_{Guid.NewGuid():N}" }.ConnectionString; }
    public async Task InitializeAsync() { db = Context(); await db.Database.EnsureCreatedAsync(); db.Participants.AddRange(new Participant { Id = manager, DisplayName = "Manager", CreatedAt = now }, new Participant { Id = activePerson, DisplayName = "Active Option", CreatedAt = now }, new Participant { Id = inactivePerson, DisplayName = "Inactive Option", IsActive = false, CreatedAt = now }, new Participant { Id = enrolled, DisplayName = "Enrolled", CreatedAt = now }); await db.SaveChangesAsync(); }
    public async Task DisposeAsync() { await db.DisposeAsync(); await using QuestDbContext cleanup = Context(); await cleanup.Database.EnsureDeletedAsync(); }

    [Fact]
    public async Task Create_edit_and_one_way_lifecycle_are_rowversioned_and_audited()
    {
        CycleAdministrationService service = Service();
        ManagerCycleDetail created = await service.CreateAsync(new(" CYCLE-1 ", " Cycle One ", now, now.AddMonths(1)), default);
        Assert.Equal(("CYCLE-1", "Cycle One", CycleStatus.Active), (created.Code, created.Name, created.Status)); Assert.NotEmpty(Convert.FromBase64String(created.Version));
        CycleEvent createdEvent = Assert.Single(await db.CycleEvents.Where(x => x.CycleId == created.Id).ToListAsync()); Assert.Equal((CycleEventType.Created, (CycleStatus?)null, CycleStatus.Active), (createdEvent.EventType, createdEvent.FromStatus, createdEvent.ToStatus));
        await AssertCode("CycleValidationFailed", () => service.CreateAsync(new("BAD", "Bad", now, now), default));
        await AssertCode("CycleCodeConflict", () => service.CreateAsync(new("CYCLE-1", "Duplicate", now, now.AddDays(1)), default));

        ManagerCycleDetail edited = await service.UpdateAsync(created.Id, new(created.Version, "CYCLE-EDIT", "Edited", now.AddDays(1), now.AddMonths(2)), default); Assert.NotEqual(created.Version, edited.Version);
        await AssertCode("CycleVersionConflict", () => service.UpdateAsync(created.Id, new(created.Version, "STALE", "Stale", now, now.AddDays(2)), default));
        ManagerCycleDetail closing = await service.StartClosingAsync(created.Id, new(edited.Version, " Start closing "), default); Assert.Equal(CycleStatus.Closing, closing.Status);
        await AssertCode("CycleConfigurationLocked", () => service.UpdateAsync(created.Id, new(closing.Version, "LOCKED", "Locked", now, now.AddDays(2)), default));
        ManagerCycleDetail finalised = await service.FinaliseAsync(created.Id, new(closing.Version, "Finalise"), default); Assert.Equal(CycleStatus.Finalised, finalised.Status);
        await AssertCode("CycleTransitionNotAllowed", () => service.FinaliseAsync(created.Id, new(finalised.Version, "Again"), default));
        Assert.Equal(new[] { CycleEventType.Created, CycleEventType.StatusChanged, CycleEventType.StatusChanged }, (await db.CycleEvents.Where(x => x.CycleId == created.Id).OrderBy(x => x.SequenceNumber).ToListAsync()).Select(x => x.EventType));
    }

    [Fact]
    public async Task Enrollment_options_status_transitions_timestamps_and_events_follow_contract()
    {
        CycleAdministrationService service = Service(); ManagerCycleDetail cycle = await service.CreateAsync(new("ENROLL", "Enrollment", now, now.AddMonths(1)), default);
        CycleParticipantOptions options = await service.ParticipantOptionsAsync(cycle.Id, default); Assert.Contains(options.Participants, x => x.ParticipantId == activePerson); Assert.DoesNotContain(options.Participants, x => x.ParticipantId == inactivePerson);
        ManagerCycleParticipant added = await service.AddParticipantAsync(cycle.Id, new(activePerson, " Enrolled for showcase "), default);
        Assert.Equal((CycleParticipantStatus.Active, now, null), (added.Status, added.JoinedAt, added.LeftAt)); Assert.NotEmpty(Convert.FromBase64String(added.Version));
        CycleParticipantEvent enrolledEvent = Assert.Single(await db.CycleParticipantEvents.Where(x => x.CycleId == cycle.Id && x.ParticipantId == activePerson).ToListAsync()); Assert.Equal((CycleParticipantEventType.Enrolled, 1, now), (enrolledEvent.EventType, enrolledEvent.SequenceNumber, enrolledEvent.OccurredAt));
        await AssertCode("CycleParticipantAlreadyEnrolled", () => service.AddParticipantAsync(cycle.Id, new(activePerson, "Again"), default));
        await AssertCode("ParticipantNotFound", () => service.AddParticipantAsync(cycle.Id, new(inactivePerson, "Inactive"), default));

        ManagerCycleParticipant withdrawn = await service.ChangeParticipantStatusAsync(cycle.Id, activePerson, new(added.Version, CycleParticipantStatus.Withdrawn, "Withdraw"), default); Assert.Equal(now, withdrawn.LeftAt); Assert.Equal(added.JoinedAt, withdrawn.JoinedAt);
        await AssertCode("CycleParticipantVersionConflict", () => service.ChangeParticipantStatusAsync(cycle.Id, activePerson, new(added.Version, CycleParticipantStatus.Active, "Stale"), default));
        ManagerCycleParticipant inactive = await service.ChangeParticipantStatusAsync(cycle.Id, activePerson, new(withdrawn.Version, CycleParticipantStatus.Inactive, "Inactive"), default); Assert.Equal(now, inactive.LeftAt);
        ManagerCycleParticipant reactivated = await service.ChangeParticipantStatusAsync(cycle.Id, activePerson, new(inactive.Version, CycleParticipantStatus.Active, "Return"), default); Assert.Null(reactivated.LeftAt); Assert.Equal(added.JoinedAt, reactivated.JoinedAt);
        Assert.Equal(4, await db.CycleParticipantEvents.CountAsync(x => x.CycleId == cycle.Id && x.ParticipantId == activePerson));
        await AssertCode("CycleParticipantTransitionNotAllowed", () => service.ChangeParticipantStatusAsync(cycle.Id, activePerson, new(reactivated.Version, CycleParticipantStatus.Active, "No-op"), default));
        ManagerCycleDetail closing = await service.StartClosingAsync(cycle.Id, new((await service.GetAsync(cycle.Id, default)).Version, "Close"), default);
        await AssertCode("CycleParticipantTransitionNotAllowed", () => service.ChangeParticipantStatusAsync(cycle.Id, activePerson, new(reactivated.Version, CycleParticipantStatus.Inactive, "Frozen"), default));
        Assert.False(closing.AllowedActions.CanAddParticipant);
    }

    [Fact]
    public async Task Participant_events_are_append_only_and_schema_constraints_fail_closed()
    {
        CycleAdministrationService service = Service(); ManagerCycleDetail cycle = await service.CreateAsync(new("GUARDS", "Guards", now, now.AddDays(1)), default); await service.AddParticipantAsync(cycle.Id, new(activePerson, "Enroll"), default);
        CycleParticipantEvent item = await db.CycleParticipantEvents.SingleAsync(); item.Reason = "Changed"; await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync()); db.Entry(item).State = EntityState.Unchanged;
        var invalid = new CycleParticipantEvent { Id = Guid.NewGuid(), CycleId = cycle.Id, ParticipantId = activePerson, SequenceNumber = 2, EventType = CycleParticipantEventType.StatusChanged, FromStatus = CycleParticipantStatus.Active, ToStatus = CycleParticipantStatus.Active, Reason = "Invalid", ActorId = manager, OccurredAt = now };
        db.CycleParticipantEvents.Add(invalid); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.Entry(invalid).State = EntityState.Detached;
        await using (QuestDbContext eventDeleteDb = Context())
        {
            CycleParticipantEvent eventToDelete = await eventDeleteDb.CycleParticipantEvents.SingleAsync();
            eventDeleteDb.CycleParticipantEvents.Remove(eventToDelete);
            await Assert.ThrowsAsync<InvalidOperationException>(() => eventDeleteDb.SaveChangesAsync());
        }
        await using (QuestDbContext enrollmentDeleteDb = Context())
        {
            CycleParticipant enrollmentToDelete = await enrollmentDeleteDb.CycleParticipants.SingleAsync();
            enrollmentDeleteDb.CycleParticipants.Remove(enrollmentToDelete);
            await Assert.ThrowsAsync<InvalidOperationException>(() => enrollmentDeleteDb.SaveChangesAsync());
        }
        db.ChangeTracker.Clear();
        CycleParticipant enrollment = await db.CycleParticipants.SingleAsync();
        enrollment.JoinedAt = now.AddDays(-1); enrollment.Status = CycleParticipantStatus.Withdrawn; enrollment.LeftAt = now;
        db.CycleParticipantEvents.Add(new CycleParticipantEvent { Id = Guid.NewGuid(), CycleId = cycle.Id, ParticipantId = activePerson, SequenceNumber = 2, EventType = CycleParticipantEventType.StatusChanged, FromStatus = CycleParticipantStatus.Active, ToStatus = CycleParticipantStatus.Withdrawn, Reason = "Timestamp tamper", ActorId = manager, OccurredAt = now });
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.CycleParticipants.Add(new CycleParticipant { CycleId = cycle.Id, ParticipantId = enrolled, Status = CycleParticipantStatus.Active, JoinedAt = now });
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Development_seed_creates_missing_history_once_and_preserves_administered_enrollment()
    {
        Guid demoParticipant = Guid.NewGuid(), demoManager = Guid.NewGuid();
        var auth = Options.Create(new QuestAuthenticationOptions { Mode = AuthenticationModes.Demo, Demo = new DemoAuthenticationOptions { AllowedEnvironments = ["Development"], Profiles = [new DemoProfileOptions { Key = "participant", Label = "Participant", Subject = "demo:participant", DisplayName = "Synthetic Participant", ParticipantId = demoParticipant, Roles = [QuestRoles.Participant] }, new DemoProfileOptions { Key = "manager", Label = "Manager", Subject = "demo:manager", DisplayName = "Synthetic Manager", ParticipantId = demoManager, Roles = [QuestRoles.Manager] }] } });
        var seeder = new DevelopmentDemoDataSeeder(db, new TestEnvironment(), auth, new Clock(now)); await seeder.SeedAsync();
        Assert.Equal(3, await db.CycleParticipantEvents.CountAsync(x => x.CycleId == DevelopmentDemoDataSeeder.CycleId && x.EventType == CycleParticipantEventType.Enrolled));
        CycleParticipant membership = await db.CycleParticipants.SingleAsync(x => x.CycleId == DevelopmentDemoDataSeeder.CycleId && x.ParticipantId == demoParticipant); DateTimeOffset joined = membership.JoinedAt!.Value;
        membership.Status = CycleParticipantStatus.Withdrawn; membership.LeftAt = now.AddHours(1); db.CycleParticipantEvents.Add(new CycleParticipantEvent { Id = Guid.NewGuid(), CycleId = membership.CycleId, ParticipantId = membership.ParticipantId, SequenceNumber = 2, EventType = CycleParticipantEventType.StatusChanged, FromStatus = CycleParticipantStatus.Active, ToStatus = CycleParticipantStatus.Withdrawn, Reason = "Administered test withdrawal", ActorId = demoManager, OccurredAt = now.AddHours(1) }); await db.SaveChangesAsync();
        await seeder.SeedAsync(); db.ChangeTracker.Clear(); membership = await db.CycleParticipants.SingleAsync(x => x.CycleId == DevelopmentDemoDataSeeder.CycleId && x.ParticipantId == demoParticipant);
        Assert.Equal(CycleParticipantStatus.Withdrawn, membership.Status); Assert.Equal(joined, membership.JoinedAt); Assert.Equal(now.AddHours(1), membership.LeftAt); Assert.Equal(2, await db.CycleParticipantEvents.CountAsync(x => x.CycleId == membership.CycleId && x.ParticipantId == membership.ParticipantId));
    }

    private CycleAdministrationService Service(IQuestCurrentUser? user = null) => new(db, user ?? new User(manager, QuestRoles.Manager), new Clock(now));
    private QuestDbContext Context() => new(new DbContextOptionsBuilder<QuestDbContext>().UseSqlServer(connection).Options);
    private static async Task AssertCode(string code, Func<Task> action) { WorkflowException error = await Assert.ThrowsAsync<WorkflowException>(action); Assert.Equal(code, error.Code); }
    private sealed class User(Guid id, string role) : IQuestCurrentUser { public QuestUserIdentity Identity { get; } = new(true, id, "Synthetic", [role]); }
    private sealed class Clock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    private sealed class TestEnvironment : IHostEnvironment { public string EnvironmentName { get; set; } = "Development"; public string ApplicationName { get; set; } = "Tests"; public string ContentRootPath { get; set; } = AppContext.BaseDirectory; public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider(); }
}

public sealed class CycleAdministrationMigrationTests
{
    private const string PreviousMigration = "20260824215739_AddChallengeRowVersion";

    [Fact]
    public async Task Actual_migration_preserves_valid_existing_cycle_and_enrollment()
    {
        (QuestDbContext db, string connection) = Context(); await using (db)
        {
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            (Guid manager, Guid cycle, Guid participant) = await InsertPreMigrationData(db, equalDates: false);
            await db.GetService<IMigrator>().MigrateAsync();
            await using var command = db.Database.GetDbConnection().CreateCommand();
            await db.Database.OpenConnectionAsync();
            command.CommandText = "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('Cycles') AND name = 'RowVersion'; SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID('CycleParticipants') AND name = 'RowVersion'; SELECT COUNT(*) FROM sys.tables WHERE name = 'CycleParticipantEvents'; SELECT COUNT(*) FROM Cycles WHERE Id = @CycleId; SELECT COUNT(*) FROM CycleParticipants WHERE CycleId = @CycleId AND ParticipantId = @ParticipantId;";
            Add(command, "@CycleId", cycle); Add(command, "@ParticipantId", participant);
            await using var reader = await command.ExecuteReaderAsync();
            foreach (int expected in new[] { 1, 1, 1, 1, 1 }) { Assert.True(await reader.ReadAsync()); Assert.Equal(expected, reader.GetInt32(0)); Assert.True(await reader.NextResultAsync() || expected == 1); }
            Assert.NotEqual(Guid.Empty, manager);
            await reader.DisposeAsync();
            await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Cycles SET EndsAt = StartsAt WHERE Id = {cycle}"));
        }
        await Delete(connection);
    }

    [Fact]
    public async Task Actual_migration_fails_closed_without_rewriting_invalid_existing_dates()
    {
        (QuestDbContext db, string connection) = Context(); await using (db)
        {
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            (_, Guid cycle, _) = await InsertPreMigrationData(db, equalDates: true);
            await Assert.ThrowsAsync<SqlException>(() => db.GetService<IMigrator>().MigrateAsync());
            await using var command = db.Database.GetDbConnection().CreateCommand(); await db.Database.OpenConnectionAsync();
            command.CommandText = "SELECT CASE WHEN StartsAt = EndsAt THEN 1 ELSE 0 END FROM Cycles WHERE Id = @CycleId"; Add(command, "@CycleId", cycle);
            Assert.Equal(1, Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture));
        }
        await Delete(connection);
    }

    private static async Task<(Guid Manager, Guid Cycle, Guid Participant)> InsertPreMigrationData(QuestDbContext db, bool equalDates)
    {
        Guid manager = Guid.NewGuid(), participant = Guid.NewGuid(), cycle = Guid.NewGuid(); DateTimeOffset start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), end = equalDates ? start : start.AddMonths(1);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Participants (Id, DisplayName, IsActive, CreatedAt) VALUES ({manager}, {"Manager"}, {true}, {start}), ({participant}, {"Participant"}, {true}, {start}); INSERT INTO Cycles (Id, Code, Name, Status, StartsAt, EndsAt, CreatedAt, CreatedByParticipantId) VALUES ({cycle}, {"MIGRATION"}, {"Migration"}, {"Active"}, {start}, {end}, {start}, {manager}); INSERT INTO CycleParticipants (CycleId, ParticipantId, Status, JoinedAt) VALUES ({cycle}, {participant}, {"Active"}, {start});");
        return (manager, cycle, participant);
    }

    private static (QuestDbContext Db, string Connection) Context()
    {
        string basis = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION") ?? "Server=localhost,1433;Database=master;User Id=sa;Password=Local-only-validation-Passw0rd!;TrustServerCertificate=True";
        string connection = new SqlConnectionStringBuilder(basis) { InitialCatalog = $"PasAiQuestCycleMigration_{Guid.NewGuid():N}" }.ConnectionString;
        return (new QuestDbContext(new DbContextOptionsBuilder<QuestDbContext>().UseSqlServer(connection).Options), connection);
    }

    private static void Add(System.Data.Common.DbCommand command, string name, object value) { var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value; command.Parameters.Add(parameter); }
    private static async Task Delete(string connection) { await using var db = new QuestDbContext(new DbContextOptionsBuilder<QuestDbContext>().UseSqlServer(connection).Options); await db.Database.EnsureDeletedAsync(); }
}
