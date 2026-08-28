using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PAS.AIQuestPortal.Api.Authentication;
using PAS.AIQuestPortal.Api.Configuration;
using PAS.AIQuestPortal.Api.Data;
using PAS.AIQuestPortal.Api.Workflow;

namespace PAS.AIQuestPortal.Api.ManualAwards;

public sealed record ManualAwardCycleView(Guid Id, string Code, string Name, CycleStatus Status);
public sealed record ManualAwardParticipantOption(Guid ParticipantId, string DisplayName, CycleParticipantStatus ParticipantStatus);
public sealed record ManualAwardCategoryOption(Guid AwardCategoryId, string Code, string Name);
public sealed record ManualAwardOptionsView(ManualAwardCycleView Cycle, IReadOnlyList<ManualAwardParticipantOption> Participants, IReadOnlyList<ManualAwardCategoryOption> Categories);
public sealed record ManualAwardCommand(Guid RequestId, Guid CycleId, Guid ParticipantId, Guid AwardCategoryId, int Amount, string Reason);
public sealed record ManualAwardView(Guid Id, Guid RequestId, Guid ParticipantId, Guid CycleId, int Amount, XPEntryType EntryType, XPSourceType SourceType, ManualAwardCategoryOption AwardCategory, string Reason, Guid AwardedByParticipantId, DateTimeOffset AwardedAt);

public sealed class ManualAwardService(QuestDbContext db, IQuestCurrentUser currentUser, TimeProvider clock)
{
    public async Task<ManualAwardOptionsView> OptionsAsync(Guid cycleId, CancellationToken ct)
    {
        Manager();
        Cycle cycle = await AvailableCycle(cycleId, ct);
        List<ManualAwardParticipantOption> participants = await (from roster in db.CycleParticipants.AsNoTracking()
            join participant in db.Participants.AsNoTracking() on roster.ParticipantId equals participant.Id
            where roster.CycleId == cycleId && roster.Status == CycleParticipantStatus.Active
            select new ManualAwardParticipantOption(participant.Id, participant.DisplayName, roster.Status)).ToListAsync(ct);
        List<ManualAwardCategoryOption> categories = await db.AwardCategories.AsNoTracking()
            .Where(x => x.IsActive && (x.CycleId == null || x.CycleId == cycleId))
            .OrderBy(x => x.Code).ThenBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new ManualAwardCategoryOption(x.Id, x.Code, x.Name)).ToListAsync(ct);
        return new(new(cycle.Id, cycle.Code, cycle.Name, cycle.Status), participants.OrderBy(x => Normalize(x.DisplayName), StringComparer.Ordinal).ThenBy(x => x.ParticipantId).ToArray(), categories);
    }

    public async Task<ManualAwardView> CreateAsync(ManualAwardCommand command, CancellationToken ct)
    {
        Guid manager = Manager();
        if (command.RequestId == Guid.Empty) throw Bad("InvalidManualAwardRequestId", "requestId must be a valid GUID.");
        if (command.CycleId == Guid.Empty) throw Bad("InvalidManualAwardCycleId", "cycleId must be a valid GUID.");
        if (command.ParticipantId == Guid.Empty) throw Bad("InvalidManualAwardParticipantId", "participantId must be a valid GUID.");
        if (command.AwardCategoryId == Guid.Empty) throw Bad("InvalidAwardCategoryId", "awardCategoryId must be a valid GUID.");
        if (command.Amount <= 0) throw Bad("InvalidManualAwardAmount", "amount must be a positive Int32 integer.");
        if (string.IsNullOrWhiteSpace(command.Reason)) throw Bad("ManualAwardReasonRequired", "A reason is required.");
        string reason = command.Reason.Trim(); if (reason.Length > 2000) throw Bad("ManualAwardReasonTooLong", "The reason cannot exceed 2,000 characters.");
        command = command with { Reason = reason };
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await AcquireRequestLock(command.RequestId, transaction, ct);
        XPEntry? existing = await db.XPEntries.AsNoTracking().SingleOrDefaultAsync(x => x.Id == command.RequestId, ct);
        if (existing is not null)
        {
            if (!Matches(existing, command)) throw Conflict("ManualAwardRequestConflict", "requestId was already used for a different manual award.");
            AwardCategory existingCategory = await db.AwardCategories.AsNoTracking().SingleAsync(x => x.Id == existing.AwardCategoryId, ct);
            await transaction.CommitAsync(ct);
            return View(existing, existingCategory);
        }

        await AvailableCycle(command.CycleId, ct);
        CycleParticipant? roster = await db.CycleParticipants.AsNoTracking().SingleOrDefaultAsync(x => x.CycleId == command.CycleId && x.ParticipantId == command.ParticipantId, ct);
        if (roster is null) throw NotFound("ManualAwardParticipantNotFound", "The participant is not enrolled in the selected cycle.");
        if (roster.Status != CycleParticipantStatus.Active) throw Conflict("ManualAwardParticipantIneligible", "The participant is not active in the selected cycle.");
        AwardCategory category = await db.AwardCategories.AsNoTracking().SingleOrDefaultAsync(x => x.Id == command.AwardCategoryId && x.IsActive && (x.CycleId == null || x.CycleId == command.CycleId), ct) ?? throw NotFound("AwardCategoryNotFound", "The award category was not found.");
        DateTimeOffset now = clock.GetUtcNow();
        var entry = new XPEntry { Id = command.RequestId, ParticipantId = command.ParticipantId, CycleId = command.CycleId, Amount = command.Amount, EntryType = XPEntryType.Grant, SourceType = XPSourceType.ManualAward, AwardCategoryId = category.Id, Reason = command.Reason, AwardedByParticipantId = manager, AwardedAt = now };
        db.XPEntries.Add(entry); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return View(entry, category);
    }

    private async Task<Cycle> AvailableCycle(Guid cycleId, CancellationToken ct)
    {
        Cycle cycle = await db.Cycles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == cycleId, ct) ?? throw NotFound("ManualAwardCycleNotFound", "The reporting cycle was not found.");
        if (cycle.Status is not (CycleStatus.Active or CycleStatus.Closing)) throw Conflict("ManualAwardCycleUnavailable", "The reporting cycle is not available for new manual awards.");
        return cycle;
    }

    private async Task AcquireRequestLock(Guid requestId, IDbContextTransaction transaction, CancellationToken ct)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand(); command.Transaction = transaction.GetDbTransaction();
        command.CommandText = "DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = -1, @DbPrincipal = 'public'; SELECT @result;";
        var resource = command.CreateParameter(); resource.ParameterName = "@Resource"; resource.Value = $"quest-manual-award:{requestId:N}"; command.Parameters.Add(resource);
        int result = Convert.ToInt32(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
        if (result < 0) throw new WorkflowException(503, "ManualAwardDependencyUnavailable", "The manual-award idempotency lock could not be acquired.");
    }

    private static bool Matches(XPEntry entry, ManualAwardCommand command) => entry.EntryType == XPEntryType.Grant && entry.SourceType == XPSourceType.ManualAward && entry.CycleId == command.CycleId && entry.ParticipantId == command.ParticipantId && entry.AwardCategoryId == command.AwardCategoryId && entry.Amount == command.Amount && entry.Reason == command.Reason;
    private static ManualAwardView View(XPEntry entry, AwardCategory category) => new(entry.Id, entry.Id, entry.ParticipantId, entry.CycleId, entry.Amount, entry.EntryType, entry.SourceType, new(category.Id, category.Code, category.Name), entry.Reason, entry.AwardedByParticipantId, entry.AwardedAt);
    private Guid Manager() { if (currentUser.Identity is not { IsAuthenticated: true, ParticipantId: Guid id } identity) throw new WorkflowException(401, "Unauthenticated", "Authentication is required."); if (!identity.Roles.Contains(QuestRoles.Manager, StringComparer.Ordinal)) throw new WorkflowException(403, "Forbidden", "Manager authorization is required."); return id; }
    private static string Normalize(string value) => value.Normalize(System.Text.NormalizationForm.FormKC).Trim().ToUpperInvariant();
    private static WorkflowException NotFound(string code, string message) => new(404, code, message);
    private static WorkflowException Conflict(string code, string message) => new(409, code, message);
    private static WorkflowException Bad(string code, string message) => new(400, code, message);
}
