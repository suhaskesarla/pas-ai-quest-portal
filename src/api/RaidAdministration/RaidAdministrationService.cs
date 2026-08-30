using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PAS.AIQuestPortal.Api.Authentication;
using PAS.AIQuestPortal.Api.Configuration;
using PAS.AIQuestPortal.Api.Data;
using PAS.AIQuestPortal.Api.Workflow;

namespace PAS.AIQuestPortal.Api.RaidAdministration;

public sealed record RaidCycleView(Guid Id, string Code, string Name, CycleStatus Status, DateTimeOffset StartsAt, DateTimeOffset EndsAt);
public sealed record RaidCycleList(Guid? DefaultCycleId, IReadOnlyList<RaidCycleView> Cycles);
public sealed record RaidAllowedActions(bool CanEdit, bool CanManagePasses, bool CanRecordParticipation, bool CanAwardXp);
public sealed record RaidSessionView(Guid Id, Guid CycleId, string CycleName, CycleStatus CycleStatus, string Name, DateTimeOffset OccurredAt, string RowVersion, bool HasReferences, RaidAllowedActions AllowedActions);
public sealed record RaidSessionList(RaidCycleView Cycle, IReadOnlyList<RaidSessionView> Raids);
public sealed record CreateRaidSessionRequest(Guid CycleId, string Name, DateTimeOffset OccurredAt);
public sealed record UpdateRaidSessionRequest(string RowVersion, string Name, DateTimeOffset OccurredAt);
public sealed record RaidPassBalance(int Assigned, int Used, int Remaining, string? EntitlementRowVersion);
public sealed record RaidParticipationView(Guid ParticipationId, Guid ParticipantId, Guid CycleId, Guid RaidSessionId, PassType PassType, DateTimeOffset UsedAt);
public sealed record RaidParticipantAllowedActions(bool CanManagePasses, bool CanRecordParticipation, bool CanAwardXp);
public sealed record RaidParticipantView(Guid ParticipantId, string DisplayName, CycleParticipantStatus CycleParticipantStatus, RaidPassBalance Physical, RaidPassBalance Remote, RaidParticipationView? Participation, RaidParticipantAllowedActions AllowedActions);
public sealed record RaidParticipantList(RaidSessionView Raid, IReadOnlyList<RaidParticipantView> Participants);
public sealed record UpdateRaidEntitlementRequest(int AssignedCount, string? RowVersion);
public sealed record RaidEntitlementView(Guid CycleId, Guid ParticipantId, PassType PassType, int Assigned, int Used, int Remaining, string RowVersion);
public sealed record CreateRaidParticipationRequest(Guid ParticipantId, PassType PassType);
public sealed record CreateRaidXpRequest(Guid RequestId, Guid ParticipantId, int Amount, string Reason);
public sealed record RaidXpView(Guid Id, Guid RequestId, Guid ParticipantId, Guid CycleId, Guid RaidSessionId, int Amount, XPEntryType EntryType, XPSourceType SourceType, string Reason, Guid AwardedByParticipantId, DateTimeOffset AwardedAt);

public interface IRaidAdministrationConcurrencyHook { Task AfterLocksAcquiredAsync(string point, CancellationToken ct); }

public sealed class RaidAdministrationService(QuestDbContext db, IQuestCurrentUser currentUser, TimeProvider clock, IRaidAdministrationConcurrencyHook? concurrencyHook = null)
{
    public async Task<RaidCycleList> CyclesAsync(CancellationToken ct)
    {
        Manager(); List<RaidCycleView> cycles = await db.Cycles.AsNoTracking().OrderByDescending(x => x.StartsAt).ThenBy(x => x.Id).Select(x => new RaidCycleView(x.Id, x.Code, x.Name, x.Status, x.StartsAt, x.EndsAt)).ToListAsync(ct);
        Guid? selected = cycles.FirstOrDefault(x => x.Status == CycleStatus.Active)?.Id ?? cycles.FirstOrDefault(x => x.Status == CycleStatus.Closing)?.Id ?? cycles.FirstOrDefault()?.Id;
        return new(selected, cycles);
    }

    public async Task<RaidSessionList> ListAsync(Guid cycleId, CancellationToken ct)
    {
        Manager(); Cycle cycle = await Cycle(cycleId, ct); List<RaidSession> rows = await db.RaidSessions.AsNoTracking().Where(x => x.CycleId == cycleId).OrderByDescending(x => x.OccurredAt).ThenBy(x => x.Id).ToListAsync(ct);
        Guid[] referenced = await ReferencedSessionIds(rows.Select(x => x.Id).ToArray(), ct);
        return new(CycleView(cycle), rows.Select(x => View(x, cycle, referenced.Contains(x.Id))).ToArray());
    }

    public async Task<RaidSessionView> GetAsync(Guid id, CancellationToken ct)
    {
        Manager(); RaidSession session = await Session(id, true, ct); Cycle cycle = await Cycle(session.CycleId, ct); return View(session, cycle, await Referenced(id, ct));
    }

    public async Task<RaidSessionView> CreateAsync(CreateRaidSessionRequest request, CancellationToken ct)
    {
        Manager(); string name = Name(request.Name); await using IDbContextTransaction tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await Lock(CycleKey(request.CycleId), tx, ct); Cycle cycle = await WritableCycle(request.CycleId, ct);
        var session = new RaidSession { Id = Guid.NewGuid(), CycleId = cycle.Id, Name = name, OccurredAt = request.OccurredAt }; db.RaidSessions.Add(session); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return View(session, cycle, false);
    }

    public async Task<RaidSessionView> UpdateAsync(Guid id, UpdateRaidSessionRequest request, CancellationToken ct)
    {
        Manager(); string name = Name(request.Name); RaidSession identity = await Session(id, true, ct); await using IDbContextTransaction tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await Lock(CycleKey(identity.CycleId), tx, ct); await Lock(SessionKey(identity.CycleId, id), tx, ct); await Hook("SessionUpdateLocks", ct); Cycle cycle = await WritableCycle(identity.CycleId, ct); RaidSession session = await Session(id, false, ct);
        CheckVersion(session.RowVersion, request.RowVersion, "InvalidRaidSessionVersion", "RaidSessionVersionConflict"); if (await Referenced(id, ct)) throw Conflict("RaidSessionImmutable", "The raid session is immutable after its first participation or Raid XP reference.");
        session.Name = name; session.OccurredAt = request.OccurredAt; try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { throw Conflict("RaidSessionVersionConflict", "The raid session changed; refresh and try again."); } await tx.CommitAsync(ct); return View(session, cycle, false);
    }

    public async Task<RaidParticipantList> ParticipantsAsync(Guid sessionId, CancellationToken ct)
    {
        Manager(); RaidSession session = await Session(sessionId, true, ct); Cycle cycle = await Cycle(session.CycleId, ct);
        var roster = await (from cp in db.CycleParticipants.AsNoTracking() join p in db.Participants.AsNoTracking() on cp.ParticipantId equals p.Id where cp.CycleId == cycle.Id select new { cp.ParticipantId, p.DisplayName, cp.Status }).ToListAsync(ct);
        Guid[] ids = roster.Select(x => x.ParticipantId).ToArray(); List<RaidEntitlement> entitlements = await db.RaidEntitlements.AsNoTracking().Where(x => x.CycleId == cycle.Id && ids.Contains(x.ParticipantId)).ToListAsync(ct);
        var usages = await db.RaidParticipations.AsNoTracking().Where(x => x.CycleId == cycle.Id && ids.Contains(x.ParticipantId)).GroupBy(x => new { x.ParticipantId, x.PassType }).Select(g => new { g.Key.ParticipantId, g.Key.PassType, Used = g.Count() }).ToListAsync(ct);
        List<RaidParticipation> selected = await db.RaidParticipations.AsNoTracking().Where(x => x.RaidSessionId == sessionId).ToListAsync(ct); bool writable = Available(cycle);
        RaidPassBalance Balance(Guid participant, PassType type) { RaidEntitlement? e = entitlements.SingleOrDefault(x => x.ParticipantId == participant && x.PassType == type); int used = usages.SingleOrDefault(x => x.ParticipantId == participant && x.PassType == type)?.Used ?? 0; int assigned = e?.AssignedCount ?? 0; return new(assigned, used, assigned - used, e is null ? null : Version(e.RowVersion)); }
        var rows = roster.OrderBy(x => Normalize(x.DisplayName), StringComparer.Ordinal).ThenBy(x => x.ParticipantId).Select(x => { RaidPassBalance physical = Balance(x.ParticipantId, PassType.Physical), remote = Balance(x.ParticipantId, PassType.Remote); RaidParticipation? use = selected.SingleOrDefault(y => y.ParticipantId == x.ParticipantId); bool active = writable && x.Status == CycleParticipantStatus.Active; return new RaidParticipantView(x.ParticipantId, x.DisplayName, x.Status, physical, remote, use is null ? null : ParticipationView(use), new(active, active && use is null && (physical.Remaining > 0 || remote.Remaining > 0), active)); }).ToArray();
        return new(View(session, cycle, await Referenced(sessionId, ct)), rows);
    }

    public async Task<RaidEntitlementView> UpdateEntitlementAsync(Guid cycleId, Guid participantId, PassType passType, UpdateRaidEntitlementRequest request, CancellationToken ct)
    {
        Manager(); ValidPass(passType); if (request.AssignedCount < 0) throw Bad("InvalidRaidAssignedCount", "assignedCount must be a non-negative Int32 integer."); await using IDbContextTransaction tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await Lock(CycleKey(cycleId), tx, ct); await Lock(ParticipantKey(cycleId, participantId), tx, ct); await Lock(EntitlementKey(cycleId, participantId, passType), tx, ct); await Hook("EntitlementLocks", ct);
        await WritableCycle(cycleId, ct); await ActiveParticipant(cycleId, participantId, ct); RaidEntitlement? entitlement = await db.RaidEntitlements.SingleOrDefaultAsync(x => x.CycleId == cycleId && x.ParticipantId == participantId && x.PassType == passType, ct);
        int used = await Used(cycleId, participantId, passType, ct); if (request.AssignedCount < used) throw Conflict("RaidEntitlementBelowUsed", "assignedCount cannot be lower than persisted usage.");
        if (entitlement is null) { if (!string.IsNullOrWhiteSpace(request.RowVersion)) throw Conflict("RaidEntitlementVersionConflict", "The entitlement does not exist at the supplied version."); entitlement = new RaidEntitlement { CycleId = cycleId, ParticipantId = participantId, PassType = passType, AssignedCount = request.AssignedCount }; db.RaidEntitlements.Add(entitlement); }
        else { CheckVersion(entitlement.RowVersion, request.RowVersion, "InvalidRaidEntitlementVersion", "RaidEntitlementVersionConflict"); entitlement.AssignedCount = request.AssignedCount; }
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { throw Conflict("RaidEntitlementVersionConflict", "The entitlement changed; refresh and try again."); } await tx.CommitAsync(ct); return new(cycleId, participantId, passType, entitlement.AssignedCount, used, entitlement.AssignedCount - used, Version(entitlement.RowVersion));
    }

    public async Task<RaidParticipationView> CreateParticipationAsync(Guid sessionId, CreateRaidParticipationRequest request, CancellationToken ct)
    {
        Manager(); ValidPass(request.PassType); RaidSession identity = await Session(sessionId, true, ct); Guid cycleId = identity.CycleId; await using IDbContextTransaction tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await Lock(CycleKey(cycleId), tx, ct); await Lock(ParticipantKey(cycleId, request.ParticipantId), tx, ct); await Lock(SessionKey(cycleId, sessionId), tx, ct); await Lock(EntitlementKey(cycleId, request.ParticipantId, request.PassType), tx, ct); await Hook("ParticipationLocks", ct);
        await WritableCycle(cycleId, ct); await ActiveParticipant(cycleId, request.ParticipantId, ct); await SessionInCycle(sessionId, cycleId, ct);
        RaidParticipation? existing = await db.RaidParticipations.AsNoTracking().SingleOrDefaultAsync(x => x.ParticipantId == request.ParticipantId && x.RaidSessionId == sessionId, ct);
        if (existing is not null) { if (existing.PassType != request.PassType) throw Conflict("RaidParticipationConflict", "The participant already used a different pass type for this session."); await tx.CommitAsync(ct); return ParticipationView(existing); }
        RaidEntitlement? entitlement = await db.RaidEntitlements.AsNoTracking().SingleOrDefaultAsync(x => x.CycleId == cycleId && x.ParticipantId == request.ParticipantId && x.PassType == request.PassType, ct) ?? throw NotFound("RaidEntitlementNotFound", "A matching raid entitlement was not found.");
        int used = await Used(cycleId, request.ParticipantId, request.PassType, ct); if (used >= entitlement.AssignedCount) throw Conflict("RaidPassExhausted", "No remaining pass is available.");
        var participation = new RaidParticipation { Id = Guid.NewGuid(), CycleId = cycleId, ParticipantId = request.ParticipantId, RaidSessionId = sessionId, PassType = request.PassType, UsedAt = clock.GetUtcNow() }; db.RaidParticipations.Add(participation);
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException e) when (Unique(e)) { throw Conflict("RaidParticipationConflict", "The participant already has a participation for this raid session."); } await tx.CommitAsync(ct); return ParticipationView(participation);
    }

    public async Task<RaidXpView> CreateXpAsync(Guid sessionId, CreateRaidXpRequest request, CancellationToken ct)
    {
        Guid manager = Manager(); if (request.RequestId == Guid.Empty) throw Bad("InvalidRaidXpRequestId", "requestId must be a valid GUID."); if (request.ParticipantId == Guid.Empty) throw Bad("InvalidRaidParticipantId", "participantId must be a valid GUID."); if (request.Amount <= 0) throw Bad("InvalidRaidXpAmount", "amount must be a positive Int32 integer."); string reason = Reason(request.Reason);
        RaidSession identity = await Session(sessionId, true, ct); Guid cycleId = identity.CycleId; await using IDbContextTransaction tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct); await Lock($"quest-raid-xp:{request.RequestId:N}", tx, ct); await Hook("RaidXpRequestLock", ct);
        XPEntry? existing = await db.XPEntries.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.RequestId, ct); if (existing is not null) { if (!XpMatches(existing, cycleId, sessionId, request.ParticipantId, request.Amount, reason)) throw Conflict("RaidXpRequestConflict", "requestId was already used for a different Raid XP award."); await tx.CommitAsync(ct); return XpView(existing); }
        await Lock(CycleKey(cycleId), tx, ct); await Lock(ParticipantKey(cycleId, request.ParticipantId), tx, ct); await Lock(SessionKey(cycleId, sessionId), tx, ct); await Hook("RaidXpDomainLocks", ct); await WritableCycle(cycleId, ct); await ActiveParticipant(cycleId, request.ParticipantId, ct); await SessionInCycle(sessionId, cycleId, ct);
        var entry = new XPEntry { Id = request.RequestId, ParticipantId = request.ParticipantId, CycleId = cycleId, Amount = request.Amount, EntryType = XPEntryType.Grant, SourceType = XPSourceType.Raid, RaidSessionId = sessionId, Reason = reason, AwardedByParticipantId = manager, AwardedAt = clock.GetUtcNow() }; db.XPEntries.Add(entry); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return XpView(entry);
    }

    private async Task<Cycle> Cycle(Guid id, CancellationToken ct) => await db.Cycles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound("RaidCycleNotFound", "The cycle was not found.");
    private async Task<Cycle> WritableCycle(Guid id, CancellationToken ct) { Cycle cycle = await Cycle(id, ct); if (!Available(cycle)) throw Conflict("RaidCycleUnavailable", "Raid changes are available only while the cycle is Active or Closing."); return cycle; }
    private async Task<RaidSession> Session(Guid id, bool noTracking, CancellationToken ct) { IQueryable<RaidSession> query = noTracking ? db.RaidSessions.AsNoTracking() : db.RaidSessions; return await query.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound("RaidSessionNotFound", "The raid session was not found."); }
    private async Task<RaidSession> SessionInCycle(Guid id, Guid cycleId, CancellationToken ct) => await db.RaidSessions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.CycleId == cycleId, ct) ?? throw NotFound("RaidSessionNotFound", "The raid session was not found.");
    private async Task ActiveParticipant(Guid cycleId, Guid participantId, CancellationToken ct) { CycleParticipant? cp = await db.CycleParticipants.AsNoTracking().SingleOrDefaultAsync(x => x.CycleId == cycleId && x.ParticipantId == participantId, ct); if (cp is null) throw NotFound("RaidParticipantNotFound", "The cycle participant was not found."); if (cp.Status != CycleParticipantStatus.Active) throw Conflict("RaidParticipantIneligible", "The participant is not active in the raid cycle."); }
    private async Task<int> Used(Guid cycle, Guid participant, PassType type, CancellationToken ct) => await db.RaidParticipations.CountAsync(x => x.CycleId == cycle && x.ParticipantId == participant && x.PassType == type, ct);
    private async Task<bool> Referenced(Guid id, CancellationToken ct) => await db.RaidParticipations.AsNoTracking().AnyAsync(x => x.RaidSessionId == id, ct) || await db.XPEntries.AsNoTracking().AnyAsync(x => x.RaidSessionId == id && x.SourceType == XPSourceType.Raid, ct);
    private async Task<Guid[]> ReferencedSessionIds(Guid[] ids, CancellationToken ct) { Guid[] uses = await db.RaidParticipations.AsNoTracking().Where(x => ids.Contains(x.RaidSessionId)).Select(x => x.RaidSessionId).Distinct().ToArrayAsync(ct); Guid[] xp = await db.XPEntries.AsNoTracking().Where(x => x.RaidSessionId != null && ids.Contains(x.RaidSessionId.Value) && x.SourceType == XPSourceType.Raid).Select(x => x.RaidSessionId!.Value).Distinct().ToArrayAsync(ct); return uses.Concat(xp).Distinct().ToArray(); }
    private async Task Lock(string resource, IDbContextTransaction tx, CancellationToken ct) { await using var command = db.Database.GetDbConnection().CreateCommand(); command.Transaction = tx.GetDbTransaction(); command.CommandText = "DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource=@Resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=-1,@DbPrincipal='public'; SELECT @result;"; var p = command.CreateParameter(); p.ParameterName = "@Resource"; p.Value = resource; command.Parameters.Add(p); int result = Convert.ToInt32(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture); if (result < 0) throw new WorkflowException(503, "RaidAdministrationDependencyUnavailable", "The raid administration lock could not be acquired."); }
    private Task Hook(string point, CancellationToken ct) => concurrencyHook?.AfterLocksAcquiredAsync(point, ct) ?? Task.CompletedTask;
    private static string CycleKey(Guid cycle) => $"quest-cycle-admin:{cycle:N}"; private static string ParticipantKey(Guid cycle, Guid participant) => $"quest-cycle-participant:{cycle:N}:{participant:N}"; private static string SessionKey(Guid cycle, Guid session) => $"quest-raid-session:{cycle:N}:{session:N}"; private static string EntitlementKey(Guid cycle, Guid participant, PassType type) => $"quest-raid-entitlement:{cycle:N}:{participant:N}:{type}";
    private static RaidCycleView CycleView(Cycle x) => new(x.Id, x.Code, x.Name, x.Status, x.StartsAt, x.EndsAt); private static RaidAllowedActions Actions(Cycle cycle, bool referenced) { bool writable = Available(cycle); return new(writable && !referenced, writable, writable, writable); } private static RaidSessionView View(RaidSession x, Cycle cycle, bool referenced) => new(x.Id, x.CycleId, cycle.Name, cycle.Status, x.Name, x.OccurredAt, Version(x.RowVersion), referenced, Actions(cycle, referenced));
    private static RaidParticipationView ParticipationView(RaidParticipation x) => new(x.Id, x.ParticipantId, x.CycleId, x.RaidSessionId, x.PassType, x.UsedAt); private static RaidXpView XpView(XPEntry x) => new(x.Id, x.Id, x.ParticipantId, x.CycleId, x.RaidSessionId!.Value, x.Amount, x.EntryType, x.SourceType, x.Reason, x.AwardedByParticipantId, x.AwardedAt);
    private static bool XpMatches(XPEntry x, Guid cycle, Guid session, Guid participant, int amount, string reason) => x.CycleId == cycle && x.ParticipantId == participant && x.RaidSessionId == session && x.Amount == amount && x.Reason == reason && x.EntryType == XPEntryType.Grant && x.SourceType == XPSourceType.Raid && x.AwardCategoryId is null && x.SubmissionId is null && x.TaskId is null && x.ChallengeId is null && x.ReversesEntryId is null;
    private static bool Available(Cycle x) => x.Status is CycleStatus.Active or CycleStatus.Closing; private static void ValidPass(PassType type) { if (type is not (PassType.Physical or PassType.Remote)) throw Bad("InvalidRaidPassType", "passType must be Physical or Remote."); }
    private static string Name(string? value) { if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 200) throw Bad("RaidSessionValidationFailed", "name is required and cannot exceed 200 characters."); return value.Trim(); } private static string Reason(string? value) { if (string.IsNullOrWhiteSpace(value)) throw Bad("RaidXpReasonRequired", "A reason is required."); string reason = value.Trim(); if (reason.Length > 2000) throw Bad("RaidXpReasonTooLong", "The reason cannot exceed 2,000 characters."); return reason; }
    private static void CheckVersion(byte[] current, string? supplied, string invalid, string conflict) { byte[] value; try { value = Convert.FromBase64String(supplied ?? ""); } catch (FormatException) { throw Bad(invalid, "rowVersion must be valid base64."); } if (value.Length == 0) throw Bad(invalid, "rowVersion is required."); if (!current.SequenceEqual(value)) throw Conflict(conflict, "The resource changed; refresh and try again."); }
    private Guid Manager() { if (currentUser.Identity is not { IsAuthenticated: true, ParticipantId: Guid id } identity) throw new WorkflowException(401, "Unauthenticated", "Authentication is required."); if (!identity.Roles.Contains(QuestRoles.Manager, StringComparer.Ordinal)) throw new WorkflowException(403, "Forbidden", "Manager authorization is required."); return id; }
    private static string Version(byte[] x) => Convert.ToBase64String(x); private static string Normalize(string x) => x.Normalize(System.Text.NormalizationForm.FormKC).Trim().ToUpperInvariant(); private static bool Unique(DbUpdateException e) => e.InnerException is SqlException { Number: 2601 or 2627 };
    private static WorkflowException Bad(string code, string message) => new(400, code, message); private static WorkflowException NotFound(string code, string message) => new(404, code, message); private static WorkflowException Conflict(string code, string message) => new(409, code, message);
}
