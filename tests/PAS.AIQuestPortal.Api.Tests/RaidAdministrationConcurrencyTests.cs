using System.Data;
using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PAS.AIQuestPortal.Api.Authentication;
using PAS.AIQuestPortal.Api.Configuration;
using PAS.AIQuestPortal.Api.CycleAdministration;
using PAS.AIQuestPortal.Api.Data;
using PAS.AIQuestPortal.Api.RaidAdministration;
using PAS.AIQuestPortal.Api.Workflow;
using Xunit;

namespace PAS.AIQuestPortal.Api.Tests;

public sealed class RaidAdministrationConcurrencyTests : IAsyncLifetime
{
    private readonly string connection;
    private readonly Guid manager = Guid.NewGuid();
    private readonly DateTimeOffset now = new(2026, 8, 30, 9, 0, 0, TimeSpan.Zero);
    private QuestDbContext db = null!;

    public RaidAdministrationConcurrencyTests()
    {
        string basis = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION") ?? throw new InvalidOperationException("TEST_SQL_CONNECTION is required.");
        connection = new SqlConnectionStringBuilder(basis) { InitialCatalog = $"PasAiQuestRaidConcurrency_{Guid.NewGuid():N}" }.ConnectionString;
    }

    public async Task InitializeAsync() { db = Context("raid-concurrency-seed"); await db.Database.MigrateAsync(); db.Participants.Add(new Participant { Id = manager, DisplayName = "Concurrency Manager", CreatedAt = now }); await db.SaveChangesAsync(); }
    public async Task DisposeAsync() { await db.DisposeAsync(); await using QuestDbContext cleanup = Context("raid-concurrency-cleanup"); await cleanup.Database.EnsureDeletedAsync(); }

    [Fact]
    public async Task Session_edit_and_first_participation_overlap_on_exact_session_lock()
    {
        State state = await ArrangeState(CycleStatus.Active, physical: 1); RaidSessionView editable = await ProveEditUsesExactSessionLock(state);
        string appA = App("participation-a"), appB = App("edit-b"); await using QuestDbContext aDb = Context(appA); await using QuestDbContext bDb = Context(appB);
        // Reverse disposal order is intentional: hook joins service tasks before either operation context is disposed.
        await using var hook = new GateHook("ParticipationLocks");
        await using AppLockBarrier barrier = await AppLockBarrier.Hold(connection, SessionKey(state), "barrier-participation-session");
        Task<RaidParticipationView> participation = hook.Track(Service(aDb, hook).CreateParticipationAsync(state.Session, new(state.Participant, PassType.Physical), hook.Token));
        await WaitForApplicationLock(appA); await barrier.ReleaseAsync(); await hook.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        // A now owns cycle/participant/session/entitlement locks. B is proven waiting, not merely started.
        Task<RaidSessionView> edit = hook.Track(Service(bDb).UpdateAsync(state.Session, new(editable.RowVersion, "Forbidden late edit", now.AddHours(2)), hook.Token)); await WaitForApplicationLock(appB);
        hook.Release(); await participation; WorkflowException conflict = await Assert.ThrowsAsync<WorkflowException>(() => edit); hook.MarkObserved(edit); Assert.Equal("RaidSessionImmutable", conflict.Code);
        await using QuestDbContext verify = Context(App("verify-session-participation")); Assert.Equal("Pre-reference edit", (await verify.RaidSessions.SingleAsync(x => x.Id == state.Session)).Name); Assert.Equal(1, await verify.RaidParticipations.CountAsync(x => x.RaidSessionId == state.Session));
    }

    [Fact]
    public async Task Session_edit_and_first_raid_xp_overlap_on_exact_session_lock()
    {
        State state = await ArrangeState(CycleStatus.Active); RaidSessionView editable = await ProveEditUsesExactSessionLock(state); Guid request = Guid.NewGuid();
        string appA = App("xp-reference-a"), appB = App("edit-after-xp-b"); await using QuestDbContext aDb = Context(appA); await using QuestDbContext bDb = Context(appB); await using var hook = new GateHook("RaidXpDomainLocks");
        await using AppLockBarrier barrier = await AppLockBarrier.Hold(connection, SessionKey(state), "barrier-xp-session");
        Task<RaidXpView> xp = hook.Track(Service(aDb, hook).CreateXpAsync(state.Session, new(request, state.Participant, 7, "Concurrent reference"), hook.Token));
        await WaitForApplicationLock(appA); await barrier.ReleaseAsync(); await hook.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Task<RaidSessionView> edit = hook.Track(Service(bDb).UpdateAsync(state.Session, new(editable.RowVersion, "Forbidden XP edit", now.AddHours(3)), hook.Token)); await WaitForApplicationLock(appB);
        hook.Release(); await xp; WorkflowException conflict = await Assert.ThrowsAsync<WorkflowException>(() => edit); hook.MarkObserved(edit); Assert.Equal("RaidSessionImmutable", conflict.Code);
        await using QuestDbContext verify = Context(App("verify-session-xp")); Assert.Equal("Pre-reference edit", (await verify.RaidSessions.SingleAsync(x => x.Id == state.Session)).Name); Assert.Equal(1, await verify.XPEntries.CountAsync(x => x.Id == request));
    }

    [Fact]
    public async Task Entitlement_decrease_and_use_overlap_on_exact_entitlement_lock()
    {
        State state = await ArrangeState(CycleStatus.Active, physical: 1); string version = Convert.ToBase64String((await db.RaidEntitlements.AsNoTracking().SingleAsync(x => x.CycleId == state.Cycle && x.ParticipantId == state.Participant && x.PassType == PassType.Physical)).RowVersion);
        string appA = App("decrease-a"), appB = App("use-b"); await using QuestDbContext aDb = Context(appA); await using QuestDbContext bDb = Context(appB); await using var hook = new GateHook("EntitlementLocks");
        await using AppLockBarrier barrier = await AppLockBarrier.Hold(connection, EntitlementKey(state, PassType.Physical), "barrier-entitlement");
        Task<RaidEntitlementView> decrease = hook.Track(Service(aDb, hook).UpdateEntitlementAsync(state.Cycle, state.Participant, PassType.Physical, new(0, version), hook.Token));
        await WaitForApplicationLock(appA); await barrier.ReleaseAsync(); await hook.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Task<RaidParticipationView> use = hook.Track(Service(bDb).CreateParticipationAsync(state.Session, new(state.Participant, PassType.Physical), hook.Token)); await WaitForApplicationLock(appB);
        hook.Release(); await decrease; WorkflowException unavailable = await Assert.ThrowsAsync<WorkflowException>(() => use); hook.MarkObserved(use); Assert.Equal("RaidPassExhausted", unavailable.Code);
        await using QuestDbContext verify = Context(App("verify-capacity")); RaidEntitlement entitlement = await verify.RaidEntitlements.SingleAsync(x => x.CycleId == state.Cycle && x.ParticipantId == state.Participant && x.PassType == PassType.Physical); int used = await verify.RaidParticipations.CountAsync(x => x.CycleId == state.Cycle && x.ParticipantId == state.Participant && x.PassType == PassType.Physical); Assert.Equal(0, used); Assert.Equal(0, entitlement.AssignedCount); Assert.True(used <= entitlement.AssignedCount);
    }

    [Fact]
    public async Task Duplicate_same_pass_participations_overlap_and_both_resolve_to_one_natural_key()
    {
        State state = await ArrangeState(CycleStatus.Active, physical: 2); string appA = App("same-pass-a"), appB = App("same-pass-b"); await using QuestDbContext aDb = Context(appA); await using QuestDbContext bDb = Context(appB); await using var hook = new GateHook("ParticipationLocks");
        await using AppLockBarrier barrier = await AppLockBarrier.Hold(connection, SessionKey(state), "barrier-same-pass-session");
        Task<RaidParticipationView> first = hook.Track(Service(aDb, hook).CreateParticipationAsync(state.Session, new(state.Participant, PassType.Physical), hook.Token));
        await WaitForApplicationLock(appA); await barrier.ReleaseAsync(); await hook.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Task<RaidParticipationView> second = hook.Track(Service(bDb).CreateParticipationAsync(state.Session, new(state.Participant, PassType.Physical), hook.Token)); await WaitForApplicationLock(appB);
        hook.Release(); RaidParticipationView[] results = await Task.WhenAll(first, second); Assert.Equal(results[0].ParticipationId, results[1].ParticipationId);
        await using QuestDbContext verify = Context(App("verify-same-pass")); Assert.Equal(1, await verify.RaidParticipations.CountAsync(x => x.ParticipantId == state.Participant && x.RaidSessionId == state.Session));
    }

    [Theory]
    [InlineData(PassType.Physical)]
    [InlineData(PassType.Remote)]
    public async Task Duplicate_different_pass_participations_are_winner_independent(PassType winningPass)
    {
        PassType losingPass = winningPass == PassType.Physical ? PassType.Remote : PassType.Physical;
        State state = await ArrangeState(CycleStatus.Active, physical: 1, remote: 1); string appA = App($"{winningPass}-winner"), appB = App($"{losingPass}-loser"); await using QuestDbContext aDb = Context(appA); await using QuestDbContext bDb = Context(appB); await using var hook = new GateHook("ParticipationLocks");
        await using AppLockBarrier barrier = await AppLockBarrier.Hold(connection, SessionKey(state), "barrier-different-pass-session");
        Task<RaidParticipationView> winner = hook.Track(Service(aDb, hook).CreateParticipationAsync(state.Session, new(state.Participant, winningPass), hook.Token));
        await WaitForApplicationLock(appA); await barrier.ReleaseAsync(); await hook.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Task<RaidParticipationView> loser = hook.Track(Service(bDb).CreateParticipationAsync(state.Session, new(state.Participant, losingPass), hook.Token)); await WaitForApplicationLock(appB);
        hook.Release(); RaidParticipationView successful = await winner; WorkflowException conflict = await Assert.ThrowsAsync<WorkflowException>(() => loser); hook.MarkObserved(loser); Assert.Equal("RaidParticipationConflict", conflict.Code); Assert.Equal(winningPass, successful.PassType);
        await using QuestDbContext verify = Context(App("verify-different-pass")); RaidParticipation row = await verify.RaidParticipations.SingleAsync(x => x.ParticipantId == state.Participant && x.RaidSessionId == state.Session); Assert.Equal(winningPass, row.PassType);
    }

    [Fact]
    public async Task Same_request_id_same_raid_xp_command_overlaps_on_exact_request_lock()
    {
        State state = await ArrangeState(CycleStatus.Active); Guid request = Guid.NewGuid(); string appA = App("same-xp-a"), appB = App("same-xp-b"); await using QuestDbContext aDb = Context(appA); await using QuestDbContext bDb = Context(appB); await using var hook = new GateHook("RaidXpRequestLock");
        await using AppLockBarrier barrier = await AppLockBarrier.Hold(connection, $"quest-raid-xp:{request:N}", "barrier-same-xp-request");
        Task<RaidXpView> first = hook.Track(Service(aDb, hook).CreateXpAsync(state.Session, new(request, state.Participant, 9, " Same command "), hook.Token));
        await WaitForApplicationLock(appA); await barrier.ReleaseAsync(); await hook.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Task<RaidXpView> second = hook.Track(Service(bDb).CreateXpAsync(state.Session, new(request, state.Participant, 9, "Same command"), hook.Token)); await WaitForApplicationLock(appB);
        hook.Release(); RaidXpView[] results = await Task.WhenAll(first, second); Assert.Equal(results[0], results[1]);
        await using QuestDbContext verify = Context(App("verify-same-xp")); Assert.Equal(1, await verify.XPEntries.CountAsync(x => x.Id == request));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Same_request_id_changed_raid_xp_command_is_winner_independent(bool commandAWins)
    {
        State state = await ArrangeState(CycleStatus.Active); Guid request = Guid.NewGuid(); var commandA = new CreateRaidXpRequest(request, state.Participant, 9, "Command A"); var commandB = new CreateRaidXpRequest(request, state.Participant, 10, "Command B"); CreateRaidXpRequest winning = commandAWins ? commandA : commandB, losing = commandAWins ? commandB : commandA;
        string appA = App(commandAWins ? "command-a-winner" : "command-b-winner"), appB = App("changed-command-loser"); await using QuestDbContext aDb = Context(appA); await using QuestDbContext bDb = Context(appB); await using var hook = new GateHook("RaidXpRequestLock");
        await using AppLockBarrier barrier = await AppLockBarrier.Hold(connection, $"quest-raid-xp:{request:N}", "barrier-changed-xp-request");
        Task<RaidXpView> winner = hook.Track(Service(aDb, hook).CreateXpAsync(state.Session, winning, hook.Token));
        await WaitForApplicationLock(appA); await barrier.ReleaseAsync(); await hook.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Task<RaidXpView> loser = hook.Track(Service(bDb).CreateXpAsync(state.Session, losing, hook.Token)); await WaitForApplicationLock(appB);
        hook.Release(); RaidXpView successful = await winner; WorkflowException conflict = await Assert.ThrowsAsync<WorkflowException>(() => loser); hook.MarkObserved(loser); Assert.Equal("RaidXpRequestConflict", conflict.Code); Assert.Equal((winning.Amount, winning.Reason), (successful.Amount, successful.Reason));
        await using QuestDbContext verify = Context(App("verify-changed-xp")); XPEntry row = await verify.XPEntries.SingleAsync(x => x.Id == request); Assert.Equal((winning.Amount, winning.Reason), (row.Amount, row.Reason)); Assert.Equal(1, await verify.XPEntries.CountAsync(x => x.Id == request));
    }

    [Fact]
    public async Task Raid_write_and_cycle_finalisation_overlap_on_exact_cycle_lock()
    {
        State state = await ArrangeState(CycleStatus.Closing); Guid request = Guid.NewGuid(); string appA = App("xp-before-finalise-a"), appB = App("finalise-b"); await using QuestDbContext aDb = Context(appA); await using QuestDbContext bDb = Context(appB); await using var hook = new GateHook("RaidXpDomainLocks");
        await using AppLockBarrier barrier = await AppLockBarrier.Hold(connection, CycleKey(state), "barrier-finalise-cycle");
        Task<RaidXpView> xp = hook.Track(Service(aDb, hook).CreateXpAsync(state.Session, new(request, state.Participant, 11, "Before finalisation"), hook.Token));
        await WaitForApplicationLock(appA); await barrier.ReleaseAsync(); await hook.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Cycle cycle = await db.Cycles.AsNoTracking().SingleAsync(x => x.Id == state.Cycle); Task<ManagerCycleDetail> finalise = hook.Track(new CycleAdministrationService(bDb, new User(manager), new Clock(now)).FinaliseAsync(state.Cycle, new(Convert.ToBase64String(cycle.RowVersion), "Concurrent finalisation"), hook.Token)); await WaitForApplicationLock(appB);
        hook.Release(); await xp; await finalise;
        await using QuestDbContext verify = Context(App("verify-finalisation")); Assert.Equal(CycleStatus.Finalised, (await verify.Cycles.SingleAsync(x => x.Id == state.Cycle)).Status); Assert.Equal(1, await verify.XPEntries.CountAsync(x => x.Id == request));
    }

    [Fact]
    public async Task Raid_write_and_participant_deactivation_overlap_on_exact_participant_lock()
    {
        State state = await ArrangeState(CycleStatus.Active); Guid request = Guid.NewGuid(); string appA = App("xp-before-deactivate-a"), appB = App("deactivate-b"); await using QuestDbContext aDb = Context(appA); await using QuestDbContext bDb = Context(appB); await using var hook = new GateHook("RaidXpDomainLocks");
        await using AppLockBarrier barrier = await AppLockBarrier.Hold(connection, ParticipantKey(state), "barrier-deactivate-participant");
        Task<RaidXpView> xp = hook.Track(Service(aDb, hook).CreateXpAsync(state.Session, new(request, state.Participant, 6, "Before deactivation"), hook.Token));
        await WaitForApplicationLock(appA); await barrier.ReleaseAsync(); await hook.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
        CycleParticipant membership = await db.CycleParticipants.AsNoTracking().SingleAsync(x => x.CycleId == state.Cycle && x.ParticipantId == state.Participant); Task<ManagerCycleParticipant> deactivate = hook.Track(new CycleAdministrationService(bDb, new User(manager), new Clock(now)).ChangeParticipantStatusAsync(state.Cycle, state.Participant, new(Convert.ToBase64String(membership.RowVersion), CycleParticipantStatus.Inactive, "Concurrent deactivation"), hook.Token)); await WaitForApplicationLock(appB);
        hook.Release(); await xp; await deactivate;
        await using QuestDbContext verify = Context(App("verify-deactivation")); Assert.Equal(CycleParticipantStatus.Inactive, (await verify.CycleParticipants.SingleAsync(x => x.CycleId == state.Cycle && x.ParticipantId == state.Participant)).Status); Assert.Equal(1, await verify.XPEntries.CountAsync(x => x.Id == request));
    }

    private async Task<RaidSessionView> ProveEditUsesExactSessionLock(State state)
    {
        RaidSession current = await db.RaidSessions.AsNoTracking().SingleAsync(x => x.Id == state.Session); string app = App("edit-lock-proof"); await using QuestDbContext editDb = Context(app); await using var hook = new GateHook("SessionUpdateLocks");
        await using AppLockBarrier barrier = await AppLockBarrier.Hold(connection, SessionKey(state), "barrier-edit-session");
        Task<RaidSessionView> edit = hook.Track(Service(editDb, hook).UpdateAsync(state.Session, new(Convert.ToBase64String(current.RowVersion), "Pre-reference edit", now.AddHours(1)), hook.Token));
        await WaitForApplicationLock(app); await barrier.ReleaseAsync(); await hook.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15)); hook.Release(); return await edit;
    }

    private async Task<State> ArrangeState(CycleStatus status, int physical = 0, int remote = 0)
    {
        Guid cycle = Guid.NewGuid(), participant = Guid.NewGuid(), session = Guid.NewGuid(); string code = $"RACE-{cycle:N}"[..20];
        db.Participants.Add(new Participant { Id = participant, DisplayName = $"Participant {participant:N}", CreatedAt = now });
        db.Cycles.Add(new Cycle { Id = cycle, Code = code, Name = code, Status = status, StartsAt = now.AddDays(-1), EndsAt = now.AddDays(1), CreatedAt = now, CreatedByParticipantId = manager });
        db.CycleEvents.Add(new CycleEvent { Id = Guid.NewGuid(), CycleId = cycle, SequenceNumber = 1, EventType = CycleEventType.Created, FromStatus = null, ToStatus = CycleStatus.Active, Reason = "Concurrency fixture", ActorId = manager, OccurredAt = now });
        db.CycleParticipants.Add(new CycleParticipant { CycleId = cycle, ParticipantId = participant, Status = CycleParticipantStatus.Active, JoinedAt = now });
        db.CycleParticipantEvents.Add(new CycleParticipantEvent { Id = Guid.NewGuid(), CycleId = cycle, ParticipantId = participant, SequenceNumber = 1, EventType = CycleParticipantEventType.Enrolled, FromStatus = null, ToStatus = CycleParticipantStatus.Active, Reason = "Concurrency enrollment", ActorId = manager, OccurredAt = now });
        db.RaidSessions.Add(new RaidSession { Id = session, CycleId = cycle, Name = "Unreferenced session", OccurredAt = now }); if (physical > 0) db.RaidEntitlements.Add(new RaidEntitlement { CycleId = cycle, ParticipantId = participant, PassType = PassType.Physical, AssignedCount = physical }); if (remote > 0) db.RaidEntitlements.Add(new RaidEntitlement { CycleId = cycle, ParticipantId = participant, PassType = PassType.Remote, AssignedCount = remote }); await db.SaveChangesAsync(); db.ChangeTracker.Clear(); return new(cycle, participant, session);
    }

    private async Task WaitForApplicationLock(string applicationName)
    {
        await using QuestDbContext monitor = Context(App("lock-monitor")); await monitor.Database.OpenConnectionAsync(); Stopwatch timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(15))
        {
            await using var command = monitor.Database.GetDbConnection().CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM sys.dm_tran_locks l INNER JOIN sys.dm_exec_sessions s ON s.session_id=l.request_session_id WHERE s.program_name=@app AND l.resource_type='APPLICATION' AND l.request_status='WAIT';"; var parameter = command.CreateParameter(); parameter.ParameterName = "@app"; parameter.Value = applicationName; command.Parameters.Add(parameter);
            if (Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) > 0) return; await Task.Yield();
        }
        throw new TimeoutException($"SQL session '{applicationName}' did not enter an application-lock wait.");
    }

    private QuestDbContext Context(string applicationName) { string value = new SqlConnectionStringBuilder(connection) { ApplicationName = applicationName }.ConnectionString; return new(new DbContextOptionsBuilder<QuestDbContext>().UseSqlServer(value).Options); }
    private RaidAdministrationService Service(QuestDbContext context, IRaidAdministrationConcurrencyHook? hook = null) => new(context, new User(manager), new Clock(now), hook);
    private string App(string purpose) => $"raid-test-{purpose}-{Guid.NewGuid():N}"; private static string CycleKey(State x) => $"quest-cycle-admin:{x.Cycle:N}"; private static string ParticipantKey(State x) => $"quest-cycle-participant:{x.Cycle:N}:{x.Participant:N}"; private static string SessionKey(State x) => $"quest-raid-session:{x.Cycle:N}:{x.Session:N}"; private static string EntitlementKey(State x, PassType type) => $"quest-raid-entitlement:{x.Cycle:N}:{x.Participant:N}:{type}";
    private sealed record State(Guid Cycle, Guid Participant, Guid Session);
    private sealed class User(Guid id) : IQuestCurrentUser { public QuestUserIdentity Identity { get; } = new(true, id, "Concurrency Manager", [QuestRoles.Manager]); }
    private sealed class Clock(DateTimeOffset value) : TimeProvider { public override DateTimeOffset GetUtcNow() => value; }
    private sealed class GateHook(string point) : IRaidAdministrationConcurrencyHook, IAsyncDisposable
    {
        private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously), release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<Task> pending = [];
        private readonly HashSet<Task> observedFaults = [];
        private readonly CancellationTokenSource lifetime = new(TimeSpan.FromSeconds(45));
        private Task? cleanup;
        public TaskCompletionSource Entered => entered; public CancellationToken Token => lifetime.Token; public void Release() => release.TrySetResult();
        public bool AllTrackedTasksCompleted => pending.All(x => x.IsCompleted);
        public Task<T> Track<T>(Task<T> task) { lock (pending) { if (cleanup is not null) throw new InvalidOperationException("Cannot track a task after GateHook cleanup has started."); pending.Add(task); } return task; }
        public void MarkObserved(Task task) { _ = task.Exception; lock (pending) observedFaults.Add(task); }
        public async Task AfterLocksAcquiredAsync(string current, CancellationToken ct) { if (current != point) return; entered.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(30), ct); }
        public ValueTask DisposeAsync()
        {
            lock (pending) cleanup ??= CleanupAsync();
            return new(cleanup);
        }
        private async Task CleanupAsync()
        {
            Release(); await lifetime.CancelAsync();
            Task[] tasks; lock (pending) tasks = [.. pending];
            Task[] settled = tasks.Select(task => task.ContinueWith(static completed => _ = completed.Exception, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default)).ToArray();
            try { await Task.WhenAll(settled).WaitAsync(TimeSpan.FromSeconds(30)); }
            catch (TimeoutException ex) { throw new TimeoutException("GateHook cleanup timed out while service operations were still using their DbContexts.", ex); }
            List<Exception> unexpected = [];
            lock (pending)
                foreach (Task task in tasks.Where(x => x.IsFaulted && !observedFaults.Contains(x)))
                    unexpected.Add(task.Exception!.Flatten());
            lifetime.Dispose();
            if (unexpected.Count > 0) throw new AggregateException("A tracked GateHook service operation faulted without being asserted by the test.", unexpected);
            Assert.True(AllTrackedTasksCompleted, "GateHook disposal completed while a tracked service task was still running.");
        }
    }

    private sealed class AppLockBarrier : IAsyncDisposable
    {
        private readonly QuestDbContext context; private readonly IDbContextTransaction transaction; private bool released;
        private AppLockBarrier(QuestDbContext context, IDbContextTransaction transaction) { this.context = context; this.transaction = transaction; }
        public static async Task<AppLockBarrier> Hold(string connection, string resource, string applicationName)
        {
            string value = new SqlConnectionStringBuilder(connection) { ApplicationName = applicationName }.ConnectionString; var context = new QuestDbContext(new DbContextOptionsBuilder<QuestDbContext>().UseSqlServer(value).Options); IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await using var command = context.Database.GetDbConnection().CreateCommand(); command.Transaction = transaction.GetDbTransaction(); command.CommandText = "DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=15000,@DbPrincipal='public'; SELECT @result;"; var parameter = command.CreateParameter(); parameter.ParameterName = "@resource"; parameter.Value = resource; command.Parameters.Add(parameter); Assert.True(Convert.ToInt32(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) >= 0); return new(context, transaction);
        }
        public async Task ReleaseAsync() { if (released) return; released = true; await transaction.CommitAsync(); }
        public async ValueTask DisposeAsync() { if (!released) await transaction.RollbackAsync(); await transaction.DisposeAsync(); await context.DisposeAsync(); }
    }
}
