using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PAS.AIQuestPortal.Api.Authentication;
using PAS.AIQuestPortal.Api.Configuration;
using PAS.AIQuestPortal.Api.Data;
using PAS.AIQuestPortal.Api.Workflow;

namespace PAS.AIQuestPortal.Api.CycleAdministration;

public sealed record CycleParticipantCounts(int Active, int Withdrawn, int Inactive, int Total);
public sealed record ManagerCycleSummary(Guid Id, string Version, string Code, string Name, CycleStatus Status, DateTimeOffset StartsAt, DateTimeOffset EndsAt, CycleParticipantCounts ParticipantCounts);
public sealed record ManagerCycleList(IReadOnlyList<ManagerCycleSummary> Cycles);
public sealed record CycleAllowedActions(bool CanEdit, bool CanStartClosing, bool CanFinalise, bool CanAddParticipant);
public sealed record CycleParticipantAllowedActions(bool CanSetActive, bool CanSetWithdrawn, bool CanSetInactive);
public sealed record ManagerCycleParticipant(Guid ParticipantId, string DisplayName, CycleParticipantStatus Status, DateTimeOffset? JoinedAt, DateTimeOffset? LeftAt, string Version, CycleParticipantAllowedActions AllowedActions);
public sealed record ManagerCycleDetail(Guid Id, string Version, string Code, string Name, CycleStatus Status, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? ThemeConfiguration, DateTimeOffset CreatedAt, Guid CreatedByParticipantId, CycleAllowedActions AllowedActions, IReadOnlyList<ManagerCycleParticipant> Participants);
public sealed record CycleParticipantOption(Guid ParticipantId, string DisplayName);
public sealed record CycleParticipantOptions(IReadOnlyList<CycleParticipantOption> Participants);
public sealed record CreateCycleRequest(string Code, string Name, DateTimeOffset StartsAt, DateTimeOffset EndsAt);
public sealed record UpdateCycleRequest(string Version, string Code, string Name, DateTimeOffset StartsAt, DateTimeOffset EndsAt);
public sealed record CycleTransitionRequest(string Version, string Reason);
public sealed record AddCycleParticipantRequest(Guid ParticipantId, string Reason);
public sealed record ChangeCycleParticipantStatusRequest(string Version, CycleParticipantStatus Status, string Reason);

public sealed class CycleAdministrationService(QuestDbContext db, IQuestCurrentUser currentUser, TimeProvider clock)
{
    public async Task<ManagerCycleList> ListAsync(CancellationToken ct)
    {
        Manager();
        var counts = db.CycleParticipants.AsNoTracking().GroupBy(x => x.CycleId).Select(g => new { CycleId = g.Key, Active = g.Sum(x => x.Status == CycleParticipantStatus.Active ? (int?)1 : 0), Withdrawn = g.Sum(x => x.Status == CycleParticipantStatus.Withdrawn ? (int?)1 : 0), Inactive = g.Sum(x => x.Status == CycleParticipantStatus.Inactive ? (int?)1 : 0), Total = (int?)g.Count() });
        var rows = await (from cycle in db.Cycles.AsNoTracking() join count in counts on cycle.Id equals count.CycleId into grouped from count in grouped.DefaultIfEmpty()
            orderby cycle.StartsAt descending, cycle.Id
            select new { cycle, Active = count.Active ?? 0, Withdrawn = count.Withdrawn ?? 0, Inactive = count.Inactive ?? 0, Total = count.Total ?? 0 }).ToListAsync(ct);
        return new(rows.Select(x => new ManagerCycleSummary(x.cycle.Id, Version(x.cycle.RowVersion), x.cycle.Code, x.cycle.Name, x.cycle.Status, x.cycle.StartsAt, x.cycle.EndsAt, new(x.Active, x.Withdrawn, x.Inactive, x.Total))).ToArray());
    }

    public async Task<ManagerCycleDetail> GetAsync(Guid id, CancellationToken ct) { Manager(); return await Detail(await Find(id, true, ct), ct); }

    public async Task<CycleParticipantOptions> ParticipantOptionsAsync(Guid cycleId, CancellationToken ct)
    {
        Manager(); Cycle cycle = await Find(cycleId, true, ct); if (cycle.Status != CycleStatus.Active) return new([]);
        List<CycleParticipantOption> rows = await db.Participants.AsNoTracking().Where(x => x.IsActive && !db.CycleParticipants.Any(cp => cp.CycleId == cycleId && cp.ParticipantId == x.Id)).Select(x => new CycleParticipantOption(x.Id, x.DisplayName)).ToListAsync(ct);
        return new(rows.OrderBy(x => Normalize(x.DisplayName), StringComparer.Ordinal).ThenBy(x => x.ParticipantId).ToArray());
    }

    public async Task<ManagerCycleDetail> CreateAsync(CreateCycleRequest request, CancellationToken ct)
    {
        Guid manager = Manager(); (string code, string name) = ValidateConfiguration(request.Code, request.Name, request.StartsAt, request.EndsAt); DateTimeOffset now = clock.GetUtcNow();
        if (await db.Cycles.AsNoTracking().AnyAsync(x => x.Code == code, ct)) throw Conflict("CycleCodeConflict", "Cycle code is already in use.");
        await using IDbContextTransaction tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var cycle = new Cycle { Id = Guid.NewGuid(), Code = code, Name = name, Status = CycleStatus.Active, StartsAt = request.StartsAt, EndsAt = request.EndsAt, CreatedAt = now, CreatedByParticipantId = manager };
        db.Cycles.Add(cycle); db.CycleEvents.Add(new CycleEvent { Id = Guid.NewGuid(), CycleId = cycle.Id, SequenceNumber = 1, EventType = CycleEventType.Created, FromStatus = null, ToStatus = CycleStatus.Active, Reason = "Cycle created", ActorId = manager, OccurredAt = now });
        await SaveCycle(ct, "CycleCodeConflict"); await tx.CommitAsync(ct); return await Detail(cycle, ct);
    }

    public async Task<ManagerCycleDetail> UpdateAsync(Guid id, UpdateCycleRequest request, CancellationToken ct)
    {
        Manager(); Cycle cycle = await Find(id, false, ct); if (cycle.Status != CycleStatus.Active) throw Conflict("CycleConfigurationLocked", "Cycle configuration is editable only while Active."); CheckVersion(cycle.RowVersion, request.Version, "InvalidCycleVersion", "CycleVersionConflict");
        (string code, string name) = ValidateConfiguration(request.Code, request.Name, request.StartsAt, request.EndsAt); if (await db.Cycles.AsNoTracking().AnyAsync(x => x.Id != id && x.Code == code, ct)) throw Conflict("CycleCodeConflict", "Cycle code is already in use.");
        cycle.Code = code; cycle.Name = name; cycle.StartsAt = request.StartsAt; cycle.EndsAt = request.EndsAt; await SaveCycle(ct, "CycleCodeConflict"); return await Detail(cycle, ct);
    }

    public Task<ManagerCycleDetail> StartClosingAsync(Guid id, CycleTransitionRequest request, CancellationToken ct) => Transition(id, request, CycleStatus.Active, CycleStatus.Closing, ct);
    public Task<ManagerCycleDetail> FinaliseAsync(Guid id, CycleTransitionRequest request, CancellationToken ct) => Transition(id, request, CycleStatus.Closing, CycleStatus.Finalised, ct);

    public async Task<ManagerCycleParticipant> AddParticipantAsync(Guid cycleId, AddCycleParticipantRequest request, CancellationToken ct)
    {
        Guid manager = Manager(); string reason = Reason(request.Reason); DateTimeOffset now = clock.GetUtcNow(); await using IDbContextTransaction tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await AcquireLock($"quest-cycle-admin:{cycleId:N}", tx, ct);
        Cycle cycle = await Find(cycleId, false, ct); if (cycle.Status != CycleStatus.Active) throw Conflict("CycleParticipantTransitionNotAllowed", "Enrollment is available only while the cycle is Active.");
        Participant participant = await db.Participants.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ParticipantId && x.IsActive, ct) ?? throw NotFound("ParticipantNotFound", "The active participant was not found.");
        if (await db.CycleParticipants.AnyAsync(x => x.CycleId == cycleId && x.ParticipantId == participant.Id, ct)) throw Conflict("CycleParticipantAlreadyEnrolled", "The participant is already enrolled in this cycle.");
        var enrollment = new CycleParticipant { CycleId = cycleId, ParticipantId = participant.Id, Status = CycleParticipantStatus.Active, JoinedAt = now, LeftAt = null };
        db.CycleParticipants.Add(enrollment); db.CycleParticipantEvents.Add(new CycleParticipantEvent { Id = Guid.NewGuid(), CycleId = cycleId, ParticipantId = participant.Id, SequenceNumber = 1, EventType = CycleParticipantEventType.Enrolled, FromStatus = null, ToStatus = CycleParticipantStatus.Active, Reason = reason, ActorId = manager, OccurredAt = now });
        try { await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); } catch (DbUpdateException e) when (IsUnique(e)) { throw Conflict("CycleParticipantAlreadyEnrolled", "The participant is already enrolled in this cycle."); }
        return ParticipantView(enrollment, participant.DisplayName, true);
    }

    public async Task<ManagerCycleParticipant> ChangeParticipantStatusAsync(Guid cycleId, Guid participantId, ChangeCycleParticipantStatusRequest request, CancellationToken ct)
    {
        Guid manager = Manager(); string reason = Reason(request.Reason); DateTimeOffset now = clock.GetUtcNow(); await using IDbContextTransaction tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await AcquireLock($"quest-cycle-admin:{cycleId:N}", tx, ct); await AcquireLock($"quest-cycle-participant:{cycleId:N}:{participantId:N}", tx, ct);
        Cycle cycle = await Find(cycleId, false, ct); if (cycle.Status != CycleStatus.Active) throw Conflict("CycleParticipantTransitionNotAllowed", "Enrollment status is frozen unless the cycle is Active.");
        CycleParticipant enrollment = await db.CycleParticipants.SingleOrDefaultAsync(x => x.CycleId == cycleId && x.ParticipantId == participantId, ct) ?? throw NotFound("CycleParticipantNotFound", "The cycle participant was not found.");
        CheckVersion(enrollment.RowVersion, request.Version, "InvalidCycleParticipantVersion", "CycleParticipantVersionConflict"); if (enrollment.Status == request.Status) throw Conflict("CycleParticipantTransitionNotAllowed", "The requested status is already current.");
        CycleParticipantStatus from = enrollment.Status; enrollment.Status = request.Status; enrollment.LeftAt = request.Status == CycleParticipantStatus.Active ? null : now;
        int sequence = (await db.CycleParticipantEvents.Where(x => x.CycleId == cycleId && x.ParticipantId == participantId).MaxAsync(x => (int?)x.SequenceNumber, ct) ?? 0) + 1;
        db.CycleParticipantEvents.Add(new CycleParticipantEvent { Id = Guid.NewGuid(), CycleId = cycleId, ParticipantId = participantId, SequenceNumber = sequence, EventType = CycleParticipantEventType.StatusChanged, FromStatus = from, ToStatus = request.Status, Reason = reason, ActorId = manager, OccurredAt = now });
        try { await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); } catch (DbUpdateConcurrencyException) { throw Conflict("CycleParticipantVersionConflict", "The enrollment changed; refresh and try again."); }
        string displayName = await db.Participants.AsNoTracking().Where(x => x.Id == participantId).Select(x => x.DisplayName).SingleAsync(ct); return ParticipantView(enrollment, displayName, true);
    }

    private async Task<ManagerCycleDetail> Transition(Guid id, CycleTransitionRequest request, CycleStatus from, CycleStatus to, CancellationToken ct)
    {
        Guid manager = Manager(); string reason = Reason(request.Reason); DateTimeOffset now = clock.GetUtcNow(); await using IDbContextTransaction tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await AcquireLock($"quest-cycle-admin:{id:N}", tx, ct);
        Cycle cycle = await Find(id, false, ct); CheckVersion(cycle.RowVersion, request.Version, "InvalidCycleVersion", "CycleVersionConflict"); if (cycle.Status != from) throw Conflict("CycleTransitionNotAllowed", $"Only {from} cycles can transition to {to}.");
        cycle.Status = to; int sequence = (await db.CycleEvents.Where(x => x.CycleId == id).MaxAsync(x => (int?)x.SequenceNumber, ct) ?? 0) + 1; db.CycleEvents.Add(new CycleEvent { Id = Guid.NewGuid(), CycleId = id, SequenceNumber = sequence, EventType = CycleEventType.StatusChanged, FromStatus = from, ToStatus = to, Reason = reason, ActorId = manager, OccurredAt = now });
        try { await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); } catch (DbUpdateConcurrencyException) { throw Conflict("CycleVersionConflict", "The cycle changed; refresh and try again."); } return await Detail(cycle, ct);
    }

    private async Task<ManagerCycleDetail> Detail(Cycle cycle, CancellationToken ct)
    {
        List<ManagerCycleParticipant> participants = await (from enrollment in db.CycleParticipants.AsNoTracking() join participant in db.Participants.AsNoTracking() on enrollment.ParticipantId equals participant.Id where enrollment.CycleId == cycle.Id select new ManagerCycleParticipant(participant.Id, participant.DisplayName, enrollment.Status, enrollment.JoinedAt, enrollment.LeftAt, Version(enrollment.RowVersion), Actions(cycle.Status, enrollment.Status))).ToListAsync(ct);
        return new(cycle.Id, Version(cycle.RowVersion), cycle.Code, cycle.Name, cycle.Status, cycle.StartsAt, cycle.EndsAt, cycle.ThemeConfiguration, cycle.CreatedAt, cycle.CreatedByParticipantId, Actions(cycle.Status), participants.OrderBy(x => Normalize(x.DisplayName), StringComparer.Ordinal).ThenBy(x => x.ParticipantId).ToArray());
    }

    private async Task<Cycle> Find(Guid id, bool noTracking, CancellationToken ct) { IQueryable<Cycle> query = noTracking ? db.Cycles.AsNoTracking() : db.Cycles; return await query.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound("CycleNotFound", "The cycle was not found."); }
    private async Task SaveCycle(CancellationToken ct, string uniqueCode) { try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { throw Conflict("CycleVersionConflict", "The cycle changed; refresh and try again."); } catch (DbUpdateException e) when (IsUnique(e)) { throw Conflict(uniqueCode, "Cycle code is already in use."); } }
    private async Task AcquireLock(string resourceName, IDbContextTransaction transaction, CancellationToken ct) { await using var command = db.Database.GetDbConnection().CreateCommand(); command.Transaction = transaction.GetDbTransaction(); command.CommandText = "DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = -1, @DbPrincipal = 'public'; SELECT @result;"; var resource = command.CreateParameter(); resource.ParameterName = "@Resource"; resource.Value = resourceName; command.Parameters.Add(resource); int result = Convert.ToInt32(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture); if (result < 0) throw Conflict("CycleAdministrationConflict", "The cycle administration lock could not be acquired."); }
    private static bool IsUnique(DbUpdateException e) => e.InnerException is SqlException { Number: 2601 or 2627 };
    private static (string Code, string Name) ValidateConfiguration(string? code, string? name, DateTimeOffset startsAt, DateTimeOffset endsAt) { if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 50 || string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200 || startsAt >= endsAt) throw Bad("CycleValidationFailed", "Code and name are required within their limits and startsAt must be before endsAt."); return (code.Trim(), name.Trim()); }
    private static string Reason(string? value) { if (string.IsNullOrWhiteSpace(value)) throw Bad("CycleAdministrationReasonRequired", "A reason is required."); string reason = value.Trim(); if (reason.Length > 1000) throw Bad("CycleAdministrationReasonTooLong", "The reason cannot exceed 1,000 characters."); return reason; }
    private static void CheckVersion(byte[] current, string? value, string invalidCode, string conflictCode) { byte[] supplied; try { supplied = Convert.FromBase64String(value ?? ""); } catch (FormatException) { throw Bad(invalidCode, "Version must be valid base64."); } if (supplied.Length == 0) throw Bad(invalidCode, "Version is required."); if (!current.SequenceEqual(supplied)) throw Conflict(conflictCode, "The resource changed; refresh and try again."); }
    private static CycleAllowedActions Actions(CycleStatus status) => status switch { CycleStatus.Active => new(true, true, false, true), CycleStatus.Closing => new(false, false, true, false), _ => new(false, false, false, false) };
    private static CycleParticipantAllowedActions Actions(CycleStatus cycle, CycleParticipantStatus current) => cycle != CycleStatus.Active ? new(false, false, false) : new(current != CycleParticipantStatus.Active, current != CycleParticipantStatus.Withdrawn, current != CycleParticipantStatus.Inactive);
    private static ManagerCycleParticipant ParticipantView(CycleParticipant value, string displayName, bool activeCycle) => new(value.ParticipantId, displayName, value.Status, value.JoinedAt, value.LeftAt, Version(value.RowVersion), activeCycle ? Actions(CycleStatus.Active, value.Status) : new(false, false, false));
    private Guid Manager() { if (currentUser.Identity is not { IsAuthenticated: true, ParticipantId: Guid id } identity) throw new WorkflowException(401, "Unauthenticated", "Authentication is required."); if (!identity.Roles.Contains(QuestRoles.Manager, StringComparer.Ordinal)) throw new WorkflowException(403, "Forbidden", "Manager authorization is required."); return id; }
    private static string Version(byte[] value) => Convert.ToBase64String(value);
    private static string Normalize(string value) => value.Normalize(System.Text.NormalizationForm.FormKC).Trim().ToUpperInvariant();
    private static WorkflowException Bad(string code, string message) => new(400, code, message); private static WorkflowException NotFound(string code, string message) => new(404, code, message); private static WorkflowException Conflict(string code, string message) => new(409, code, message);
}
