using System.Buffers.Binary;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using PAS.AIQuestPortal.Api.Authentication;
using PAS.AIQuestPortal.Api.Configuration;
using PAS.AIQuestPortal.Api.Data;
using PAS.AIQuestPortal.Api.Workflow;

namespace PAS.AIQuestPortal.Api.Reporting;

public sealed record ManagerReportingCycle(Guid Id, string Code, string Name, CycleStatus Status, DateTimeOffset StartsAt, DateTimeOffset EndsAt);
public sealed record ManagerReportingCyclesView(Guid? DefaultCycleId, IReadOnlyList<ManagerReportingCycle> Cycles);
public sealed record ScoresheetSourceTotals(int TaskApprovalXp, int ManualAwardXp, int RaidXp);
public sealed record ScoresheetEntryTotals(int GrantXp, int ReversalXp, int CorrectionXp, int NetAdjustmentXp);
public sealed record ScoresheetRow(Guid ParticipantId, string DisplayName, CycleParticipantStatus ParticipantStatus, int TotalXp, ScoresheetSourceTotals BySource, ScoresheetEntryTotals ByEntryType);
public sealed record ManagerScoresheetView(ManagerReportingCycle Cycle, IReadOnlyList<ScoresheetRow> Rows);
public sealed record ScoresheetParticipant(Guid ParticipantId, string DisplayName, CycleParticipantStatus ParticipantStatus);
public sealed record ScoresheetCorrectionView(int CurrentEffectiveAmount);
public sealed record ScoresheetLedgerItem(Guid Id, int Amount, XPEntryType EntryType, XPSourceType SourceType, string Reason, DateTimeOffset AwardedAt, Guid? ReversesEntryId, XpSourceView Source, ScoresheetCorrectionView? Correction);
public sealed record ScoresheetParticipantDetail(ManagerReportingCycle Cycle, ScoresheetParticipant Participant, int TotalXp, ScoresheetSourceTotals BySource, ScoresheetEntryTotals ByEntryType, IReadOnlyList<ScoresheetLedgerItem> Items, string? NextCursor);

public sealed class ManagerScoresheetService(QuestDbContext db, IQuestCurrentUser currentUser)
{
    public async Task<ManagerReportingCyclesView> ReportingCyclesAsync(CancellationToken ct)
    {
        Manager(); List<ManagerReportingCycle> cycles = await db.Cycles.AsNoTracking().OrderByDescending(x => x.StartsAt).ThenBy(x => x.Id).Select(CycleProjection()).ToListAsync(ct);
        Guid? defaultId = cycles.Where(x => x.Status == CycleStatus.Active).Select(x => (Guid?)x.Id).FirstOrDefault() ?? cycles.Where(x => x.Status == CycleStatus.Closing).Select(x => (Guid?)x.Id).FirstOrDefault() ?? cycles.Select(x => (Guid?)x.Id).FirstOrDefault();
        return new(defaultId, cycles);
    }

    public async Task<ManagerScoresheetView> ScoresheetAsync(Guid cycleId, CancellationToken ct)
    {
        Manager(); ManagerReportingCycle cycle = await Cycle(cycleId, ct);
        var aggregates = db.XPEntries.AsNoTracking().Where(x => x.CycleId == cycleId).GroupBy(x => x.ParticipantId).Select(group => new
        {
            ParticipantId = group.Key, Total = group.Sum(x => (int?)x.Amount),
            Task = group.Sum(x => x.SourceType == XPSourceType.TaskApproval ? (int?)x.Amount : 0), Manual = group.Sum(x => x.SourceType == XPSourceType.ManualAward ? (int?)x.Amount : 0), Raid = group.Sum(x => x.SourceType == XPSourceType.Raid ? (int?)x.Amount : 0),
            Grant = group.Sum(x => x.EntryType == XPEntryType.Grant ? (int?)x.Amount : 0), Reversal = group.Sum(x => x.EntryType == XPEntryType.Reversal ? (int?)x.Amount : 0), Correction = group.Sum(x => x.EntryType == XPEntryType.Correction ? (int?)x.Amount : 0)
        });
        var rows = await (from roster in db.CycleParticipants.AsNoTracking() join participant in db.Participants.AsNoTracking() on roster.ParticipantId equals participant.Id
            join aggregate in aggregates on roster.ParticipantId equals aggregate.ParticipantId into totals from aggregate in totals.DefaultIfEmpty()
            where roster.CycleId == cycleId
            select new
            {
                participant.Id,
                participant.DisplayName,
                roster.Status,
                Total = aggregate.Total ?? 0,
                Task = aggregate.Task ?? 0,
                Manual = aggregate.Manual ?? 0,
                Raid = aggregate.Raid ?? 0,
                Grant = aggregate.Grant ?? 0,
                Reversal = aggregate.Reversal ?? 0,
                Correction = aggregate.Correction ?? 0
            }).ToListAsync(ct);
        ScoresheetRow[] result = rows.OrderBy(x => Normalize(x.DisplayName), StringComparer.Ordinal).ThenBy(x => x.Id).Select(x => Row(x.Id, x.DisplayName, x.Status, x.Total, x.Task, x.Manual, x.Raid, x.Grant, x.Reversal, x.Correction)).ToArray();
        return new(cycle, result);
    }

    public async Task<ScoresheetParticipantDetail> ParticipantAsync(Guid participantId, Guid cycleId, int limit, string? cursor, CancellationToken ct)
    {
        Manager(); if (limit is < 1 or > 100) throw Bad("InvalidScoresheetCursor", "limit must be between 1 and 100."); ManagerReportingCycle cycle = await Cycle(cycleId, ct);
        ScoresheetParticipant? participant = await (from roster in db.CycleParticipants.AsNoTracking() join person in db.Participants.AsNoTracking() on roster.ParticipantId equals person.Id where roster.CycleId == cycleId && roster.ParticipantId == participantId select new ScoresheetParticipant(person.Id, person.DisplayName, roster.Status)).SingleOrDefaultAsync(ct);
        if (participant is null) throw new WorkflowException(404, "ScoresheetParticipantNotFound", "The scoresheet participant was not found.");
        Cursor? position = string.IsNullOrWhiteSpace(cursor) ? null : Decode(cursor);
        IQueryable<XPEntry> query = db.XPEntries.AsNoTracking().Where(x => x.CycleId == cycleId && x.ParticipantId == participantId); if (position is not null) query = query.Where(x => x.AwardedAt < position.At || (x.AwardedAt == position.At && x.Id.CompareTo(position.Id) < 0));
        LedgerTotals totals = await db.XPEntries.AsNoTracking()
            .Where(x => x.CycleId == cycleId && x.ParticipantId == participantId)
            .GroupBy(_ => 1)
            .Select(group => new LedgerTotals(
                group.Sum(x => x.Amount),
                group.Sum(x => x.SourceType == XPSourceType.TaskApproval ? x.Amount : 0),
                group.Sum(x => x.SourceType == XPSourceType.ManualAward ? x.Amount : 0),
                group.Sum(x => x.SourceType == XPSourceType.Raid ? x.Amount : 0),
                group.Sum(x => x.EntryType == XPEntryType.Grant ? x.Amount : 0),
                group.Sum(x => x.EntryType == XPEntryType.Reversal ? x.Amount : 0),
                group.Sum(x => x.EntryType == XPEntryType.Correction ? x.Amount : 0)))
            .SingleOrDefaultAsync(ct) ?? new(0, 0, 0, 0, 0, 0, 0);
        List<XPEntry> page = await query.OrderByDescending(x => x.AwardedAt).ThenByDescending(x => x.Id).Take(limit + 1).ToListAsync(ct); bool more = page.Count > limit; if (more) page.RemoveAt(page.Count - 1);
        IReadOnlyList<ScoresheetLedgerItem> items = await Items(page, ct);
        ScoresheetRow summary = Row(participant.ParticipantId, participant.DisplayName, participant.ParticipantStatus, totals.Total, totals.Task, totals.Manual, totals.Raid, totals.Grant, totals.Reversal, totals.Correction);
        return new(cycle, participant, summary.TotalXp, summary.BySource, summary.ByEntryType, items, more && page.Count > 0 ? Encode(page[^1].AwardedAt, page[^1].Id) : null);
    }

    private async Task<IReadOnlyList<ScoresheetLedgerItem>> Items(IReadOnlyList<XPEntry> rows, CancellationToken ct)
    {
        Guid[] challengeIds = rows.Where(x => x.ChallengeId.HasValue).Select(x => x.ChallengeId!.Value).Distinct().ToArray(), taskIds = rows.Where(x => x.TaskId.HasValue).Select(x => x.TaskId!.Value).Distinct().ToArray(), awardIds = rows.Where(x => x.AwardCategoryId.HasValue).Select(x => x.AwardCategoryId!.Value).Distinct().ToArray(), raidIds = rows.Where(x => x.RaidSessionId.HasValue).Select(x => x.RaidSessionId!.Value).Distinct().ToArray();
        Guid[] correctableIds = rows.Where(x => x.EntryType == XPEntryType.Grant && x.SourceType == XPSourceType.TaskApproval).Select(x => x.Id).ToArray();
        Dictionary<Guid, int> adjustments = await db.XPEntries.AsNoTracking().Where(x => x.ReversesEntryId.HasValue && correctableIds.Contains(x.ReversesEntryId.Value)).GroupBy(x => x.ReversesEntryId!.Value).Select(x => new { Id = x.Key, Amount = x.Sum(y => y.Amount) }).ToDictionaryAsync(x => x.Id, x => x.Amount, ct);
        Dictionary<Guid, string> challenges = await db.Challenges.AsNoTracking().Where(x => challengeIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct), tasks = await db.ChallengeTasks.AsNoTracking().Where(x => taskIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct), awards = await db.AwardCategories.AsNoTracking().Where(x => awardIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct), raids = await db.RaidSessions.AsNoTracking().Where(x => raidIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        return rows.Select(row => { string? challenge = row.ChallengeId is Guid a ? challenges.GetValueOrDefault(a) : null, task = row.TaskId is Guid b ? tasks.GetValueOrDefault(b) : null, award = row.AwardCategoryId is Guid c ? awards.GetValueOrDefault(c) : null, raid = row.RaidSessionId is Guid d ? raids.GetValueOrDefault(d) : null; string label = row.SourceType switch { XPSourceType.TaskApproval => $"{challenge} · {task}", XPSourceType.ManualAward => award ?? "Manual award", XPSourceType.Raid => raid ?? "Raid", _ => row.SourceType.ToString() }; ScoresheetCorrectionView? correction = row.EntryType == XPEntryType.Grant && row.SourceType == XPSourceType.TaskApproval ? new(row.Amount + adjustments.GetValueOrDefault(row.Id)) : null; return new ScoresheetLedgerItem(row.Id, row.Amount, row.EntryType, row.SourceType, row.Reason, row.AwardedAt, row.ReversesEntryId, new(label, row.ChallengeId, challenge, row.TaskId, task, row.AwardCategoryId, award, row.RaidSessionId, raid), correction); }).ToArray();
    }

    private async Task<ManagerReportingCycle> Cycle(Guid id, CancellationToken ct) => await db.Cycles.AsNoTracking().Where(x => x.Id == id).Select(CycleProjection()).SingleOrDefaultAsync(ct) ?? throw new WorkflowException(404, "ReportingCycleNotFound", "The reporting cycle was not found.");
    private static System.Linq.Expressions.Expression<Func<Cycle, ManagerReportingCycle>> CycleProjection() => x => new(x.Id, x.Code, x.Name, x.Status, x.StartsAt, x.EndsAt);
    private static ScoresheetRow Row(Guid id, string name, CycleParticipantStatus status, int total, int task, int manual, int raid, int grant, int reversal, int correction) => new(id, name, status, total, new(task, manual, raid), new(grant, reversal, correction, reversal + correction));
    private void Manager() { if (currentUser.Identity is not { IsAuthenticated: true } identity) throw new WorkflowException(401, "Unauthenticated", "Authentication is required."); if (!identity.Roles.Contains(QuestRoles.Manager, StringComparer.Ordinal)) throw new WorkflowException(403, "Forbidden", "Manager authorization is required."); }
    private static string Normalize(string value) => value.Normalize(NormalizationForm.FormKC).Trim().ToUpperInvariant();
    private static WorkflowException Bad(string code, string message) => new(400, code, message);
    private sealed record Cursor(DateTimeOffset At, Guid Id);
    private sealed record LedgerTotals(int Total, int Task, int Manual, int Raid, int Grant, int Reversal, int Correction);
    private static string Encode(DateTimeOffset at, Guid id) { byte[] bytes = new byte[24]; BinaryPrimitives.WriteInt64BigEndian(bytes, at.UtcTicks); id.TryWriteBytes(bytes.AsSpan(8)); return WebEncoders.Base64UrlEncode(bytes); }
    private static Cursor Decode(string value) { try { byte[] bytes = WebEncoders.Base64UrlDecode(value); if (bytes.Length != 24) throw new FormatException(); return new(new DateTimeOffset(BinaryPrimitives.ReadInt64BigEndian(bytes), TimeSpan.Zero), new Guid(bytes.AsSpan(8))); } catch (Exception error) when (error is FormatException or ArgumentException) { throw Bad("InvalidScoresheetCursor", "cursor is malformed."); } }
}
