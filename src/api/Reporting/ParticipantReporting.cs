using System.Buffers.Binary;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using PAS.AIQuestPortal.Api.Authentication;
using PAS.AIQuestPortal.Api.Data;
using PAS.AIQuestPortal.Api.Workflow;

namespace PAS.AIQuestPortal.Api.Reporting;

public sealed record ReportingCycleItem(Guid Id, string Code, string Name, CycleStatus Status, DateTimeOffset StartsAt, DateTimeOffset EndsAt, CycleParticipantStatus ParticipantStatus);
public sealed record ReportingCyclesView(Guid? DefaultCycleId, IReadOnlyList<ReportingCycleItem> Cycles);
public sealed record ParticipantSummary(Guid ParticipantId, string DisplayName);
public sealed record RaidPassBalanceView(PassType PassType, int Assigned, int Used, int Remaining);
public sealed record XpSourceView(string Label, Guid? ChallengeId, string? ChallengeName, Guid? TaskId, string? TaskName, Guid? AwardCategoryId, string? AwardCategoryName, Guid? RaidSessionId, string? RaidSessionName);
public sealed record XpActivityItem(Guid Id, int Amount, XPEntryType EntryType, XPSourceType SourceType, string Reason, DateTimeOffset AwardedAt, Guid? ReversesEntryId, XpSourceView Source);
public sealed record XpActivityPage(IReadOnlyList<XpActivityItem> Items, string? NextCursor);
public sealed record LeaderboardEntry(int Rank, Guid ParticipantId, string DisplayName, int TotalXp, bool IsCurrentParticipant);
public sealed record ParticipantDashboardView(ReportingCycleItem Cycle, ParticipantSummary Participant, int TotalXp, int? IndividualRank, int EligibleChallengeCount, IReadOnlyDictionary<string, int> SubmissionStatusCounts, IReadOnlyList<RaidPassBalanceView> RaidPassBalance, IReadOnlyList<XpActivityItem> RecentActivity);
public sealed record TeamMemberView(Guid ParticipantId, string DisplayName, bool IsCurrentParticipant, DateTimeOffset JoinedAt);
public sealed record CycleTeamView(Guid CycleTeamId, string Name, IReadOnlyList<TeamMemberView> Members);
public sealed record ChallengeGroupView(Guid ParticipationId, Guid ChallengeId, string ChallengeName, ChallengeStatus ChallengeStatus, IReadOnlyList<TeamMemberView> Members);
public sealed record ParticipantTeamView(CycleTeamView? Team, IReadOnlyList<ChallengeGroupView> ChallengeGroups);

public sealed class ParticipantReportingService(QuestDbContext db, IQuestCurrentUser currentUser, SubmissionWorkflowService workflow)
{
    public async Task<ReportingCyclesView> ReportingCyclesAsync(CancellationToken ct)
    {
        Guid participantId = ParticipantId();
        List<ReportingCycleItem> cycles = await (from membership in db.CycleParticipants.AsNoTracking()
            join cycle in db.Cycles.AsNoTracking() on membership.CycleId equals cycle.Id
            where membership.ParticipantId == participantId
            orderby cycle.StartsAt descending, cycle.Id
            select new ReportingCycleItem(cycle.Id, cycle.Code, cycle.Name, cycle.Status, cycle.StartsAt, cycle.EndsAt, membership.Status)).ToListAsync(ct);
        Guid? defaultId = cycles.Where(x => x.Status == CycleStatus.Active).Select(x => (Guid?)x.Id).FirstOrDefault()
            ?? cycles.Where(x => x.Status == CycleStatus.Closing).Select(x => (Guid?)x.Id).FirstOrDefault()
            ?? cycles.Select(x => (Guid?)x.Id).FirstOrDefault();
        return new(defaultId, cycles);
    }

    public async Task<ParticipantDashboardView> DashboardAsync(Guid cycleId, CancellationToken ct)
    {
        (Guid participantId, ReportingCycleItem cycle) = await AccessibleCycle(cycleId, ct);
        ParticipantSummary participant = await db.Participants.AsNoTracking().Where(x => x.Id == participantId).Select(x => new ParticipantSummary(x.Id, x.DisplayName)).SingleAsync(ct);
        int total = await db.XPEntries.AsNoTracking().Where(x => x.ParticipantId == participantId && x.CycleId == cycleId).SumAsync(x => (int?)x.Amount, ct) ?? 0;
        int? rank = (await LeaderboardAsync(cycleId, ct)).SingleOrDefault(x => x.ParticipantId == participantId)?.Rank;
        Guid[] eligibleIds = (await workflow.EligibleAsync(ct)).Select(x => x.Id).ToArray();
        int eligibleCount = await db.Challenges.AsNoTracking().CountAsync(x => x.CycleId == cycleId && eligibleIds.Contains(x.Id), ct);
        Dictionary<string, int> statuses = await db.Submissions.AsNoTracking().Where(x => x.ClaimantId == participantId && x.CycleId == cycleId)
            .GroupBy(x => x.Status).Select(x => new { Status = x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.Status.ToString(), x => x.Count, ct);
        List<RaidPassBalanceView> passes = await RaidBalances(participantId, cycleId, ct);
        XpActivityPage recent = await XpActivityAsync(cycleId, 5, null, ct);
        return new(cycle, participant, total, rank, eligibleCount, statuses, passes, recent.Items);
    }

    public async Task<IReadOnlyList<LeaderboardEntry>> LeaderboardAsync(Guid cycleId, CancellationToken ct)
    {
        Guid current = (await AccessibleCycle(cycleId, ct)).ParticipantId;
        var rows = await (from membership in db.CycleParticipants.AsNoTracking()
            join participant in db.Participants.AsNoTracking() on membership.ParticipantId equals participant.Id
            where membership.CycleId == cycleId && membership.Status == CycleParticipantStatus.Active
            select new { participant.Id, participant.DisplayName, Total = db.XPEntries.Where(x => x.CycleId == cycleId && x.ParticipantId == participant.Id).Sum(x => (int?)x.Amount) ?? 0 }).ToListAsync(ct);
        var ordered = rows.OrderByDescending(x => x.Total).ThenBy(x => Normalize(x.DisplayName), StringComparer.Ordinal).ThenBy(x => x.Id).ToArray();
        var result = new List<LeaderboardEntry>(ordered.Length); int previousTotal = 0; int rank = 0;
        for (int index = 0; index < ordered.Length; index++)
        {
            if (index == 0 || ordered[index].Total != previousTotal) rank = index + 1;
            previousTotal = ordered[index].Total;
            result.Add(new(rank, ordered[index].Id, ordered[index].DisplayName, ordered[index].Total, ordered[index].Id == current));
        }
        return result;
    }

    public async Task<XpActivityPage> XpActivityAsync(Guid cycleId, int limit, string? cursor, CancellationToken ct)
    {
        Guid participantId = (await AccessibleCycle(cycleId, ct)).ParticipantId;
        if (limit is < 1 or > 100) throw Bad("InvalidActivityLimit", "limit must be between 1 and 100.");
        ActivityCursor? position = string.IsNullOrWhiteSpace(cursor) ? null : DecodeCursor(cursor);
        IQueryable<XPEntry> query = db.XPEntries.AsNoTracking().Where(x => x.ParticipantId == participantId && x.CycleId == cycleId);
        if (position is not null) query = query.Where(x => x.AwardedAt < position.AwardedAt || (x.AwardedAt == position.AwardedAt && x.Id.CompareTo(position.Id) < 0));
        List<XPEntry> rows = await query.OrderByDescending(x => x.AwardedAt).ThenByDescending(x => x.Id).Take(limit + 1).ToListAsync(ct);
        bool more = rows.Count > limit; if (more) rows.RemoveAt(rows.Count - 1);
        IReadOnlyList<XpActivityItem> items = await BuildActivity(rows, ct);
        string? next = more && rows.Count > 0 ? EncodeCursor(rows[^1].AwardedAt, rows[^1].Id) : null;
        return new(items, next);
    }

    public async Task<ParticipantTeamView> TeamAsync(Guid cycleId, CancellationToken ct)
    {
        Guid participantId = (await AccessibleCycle(cycleId, ct)).ParticipantId;
        var ownMembership = await (from member in db.CycleTeamMembers.AsNoTracking()
            join team in db.CycleTeams.AsNoTracking() on new { member.CycleTeamId, member.CycleId } equals new { CycleTeamId = team.Id, team.CycleId }
            where member.CycleId == cycleId && member.ParticipantId == participantId && member.LeftAt == null
            select new { team.Id, team.Name }).SingleOrDefaultAsync(ct);
        CycleTeamView? teamView = null;
        if (ownMembership is not null)
        {
            List<TeamMemberView> members = await (from member in db.CycleTeamMembers.AsNoTracking()
                join person in db.Participants.AsNoTracking() on member.ParticipantId equals person.Id
                where member.CycleTeamId == ownMembership.Id && member.CycleId == cycleId && member.LeftAt == null
                orderby person.DisplayName, person.Id
                select new TeamMemberView(person.Id, person.DisplayName, person.Id == participantId, member.JoinedAt)).ToListAsync(ct);
            teamView = new(ownMembership.Id, ownMembership.Name, members);
        }

        var groups = await (from own in db.ChallengeParticipationMembers.AsNoTracking()
            join participation in db.ChallengeParticipations.AsNoTracking() on own.ChallengeParticipationId equals participation.Id
            join challenge in db.Challenges.AsNoTracking() on participation.ChallengeId equals challenge.Id
            where own.ParticipantId == participantId && own.CycleId == cycleId
            orderby challenge.Name, participation.Id
            select new { ParticipationId = participation.Id, ChallengeId = challenge.Id, challenge.Name, challenge.Status }).ToListAsync(ct);
        Guid[] groupIds = groups.Select(x => x.ParticipationId).ToArray();
        var memberRows = await (from member in db.ChallengeParticipationMembers.AsNoTracking()
            join person in db.Participants.AsNoTracking() on member.ParticipantId equals person.Id
            where groupIds.Contains(member.ChallengeParticipationId)
            select new { member.ChallengeParticipationId, person.Id, person.DisplayName, member.JoinedSnapshotAt }).ToListAsync(ct);
        IReadOnlyList<ChallengeGroupView> groupViews = groups.Select(group => new ChallengeGroupView(group.ParticipationId, group.ChallengeId, group.Name, group.Status,
            memberRows.Where(x => x.ChallengeParticipationId == group.ParticipationId).OrderBy(x => Normalize(x.DisplayName), StringComparer.Ordinal).ThenBy(x => x.Id)
                .Select(x => new TeamMemberView(x.Id, x.DisplayName, x.Id == participantId, x.JoinedSnapshotAt)).ToArray())).ToArray();
        return new(teamView, groupViews);
    }

    private async Task<(Guid ParticipantId, ReportingCycleItem Cycle)> AccessibleCycle(Guid cycleId, CancellationToken ct)
    {
        Guid participantId = ParticipantId();
        ReportingCycleItem? cycle = await (from membership in db.CycleParticipants.AsNoTracking()
            join item in db.Cycles.AsNoTracking() on membership.CycleId equals item.Id
            where membership.ParticipantId == participantId && membership.CycleId == cycleId
            select new ReportingCycleItem(item.Id, item.Code, item.Name, item.Status, item.StartsAt, item.EndsAt, membership.Status)).SingleOrDefaultAsync(ct);
        return cycle is null ? throw new WorkflowException(404, "ReportingCycleNotFound", "The reporting cycle was not found.") : (participantId, cycle);
    }

    private async Task<List<RaidPassBalanceView>> RaidBalances(Guid participantId, Guid cycleId, CancellationToken ct)
    {
        var assigned = await db.RaidEntitlements.AsNoTracking().Where(x => x.ParticipantId == participantId && x.CycleId == cycleId).ToListAsync(ct);
        var used = await db.RaidParticipations.AsNoTracking().Where(x => x.ParticipantId == participantId && x.CycleId == cycleId).GroupBy(x => x.PassType).Select(x => new { Type = x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.Type, x => x.Count, ct);
        return assigned.OrderBy(x => x.PassType).Select(x => new RaidPassBalanceView(x.PassType, x.AssignedCount, used.GetValueOrDefault(x.PassType), Math.Max(0, x.AssignedCount - used.GetValueOrDefault(x.PassType)))).ToList();
    }

    private async Task<IReadOnlyList<XpActivityItem>> BuildActivity(IReadOnlyList<XPEntry> rows, CancellationToken ct)
    {
        Guid[] challengeIds = rows.Where(x => x.ChallengeId.HasValue).Select(x => x.ChallengeId!.Value).Distinct().ToArray();
        Guid[] taskIds = rows.Where(x => x.TaskId.HasValue).Select(x => x.TaskId!.Value).Distinct().ToArray();
        Guid[] awardIds = rows.Where(x => x.AwardCategoryId.HasValue).Select(x => x.AwardCategoryId!.Value).Distinct().ToArray();
        Guid[] raidIds = rows.Where(x => x.RaidSessionId.HasValue).Select(x => x.RaidSessionId!.Value).Distinct().ToArray();
        Dictionary<Guid, string> challenges = await db.Challenges.AsNoTracking().Where(x => challengeIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        Dictionary<Guid, string> tasks = await db.ChallengeTasks.AsNoTracking().Where(x => taskIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        Dictionary<Guid, string> awards = await db.AwardCategories.AsNoTracking().Where(x => awardIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        Dictionary<Guid, string> raids = await db.RaidSessions.AsNoTracking().Where(x => raidIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        return rows.Select(row =>
        {
            string? challenge = row.ChallengeId is Guid challengeId ? challenges.GetValueOrDefault(challengeId) : null;
            string? task = row.TaskId is Guid taskId ? tasks.GetValueOrDefault(taskId) : null;
            string? award = row.AwardCategoryId is Guid awardId ? awards.GetValueOrDefault(awardId) : null;
            string? raid = row.RaidSessionId is Guid raidId ? raids.GetValueOrDefault(raidId) : null;
            string label = row.SourceType switch { XPSourceType.TaskApproval => $"{challenge} · {task}", XPSourceType.ManualAward => award ?? "Manual award", XPSourceType.Raid => raid ?? "Raid", _ => row.SourceType.ToString() };
            return new XpActivityItem(row.Id, row.Amount, row.EntryType, row.SourceType, row.Reason, row.AwardedAt, row.ReversesEntryId, new(label, row.ChallengeId, challenge, row.TaskId, task, row.AwardCategoryId, award, row.RaidSessionId, raid));
        }).ToArray();
    }

    private Guid ParticipantId() => currentUser.Identity is { IsAuthenticated: true, ParticipantId: Guid id } ? id : throw new WorkflowException(401, "Unauthenticated", "Authentication is required.");
    private static string Normalize(string value) => value.Normalize(NormalizationForm.FormKC).Trim().ToUpperInvariant();
    private static WorkflowException Bad(string code, string message) => new(400, code, message);
    private sealed record ActivityCursor(DateTimeOffset AwardedAt, Guid Id);
    private static string EncodeCursor(DateTimeOffset awardedAt, Guid id)
    {
        byte[] bytes = new byte[24]; BinaryPrimitives.WriteInt64BigEndian(bytes, awardedAt.UtcTicks); id.TryWriteBytes(bytes.AsSpan(8)); return WebEncoders.Base64UrlEncode(bytes);
    }
    private static ActivityCursor DecodeCursor(string value)
    {
        try { byte[] bytes = WebEncoders.Base64UrlDecode(value); if (bytes.Length != 24) throw new FormatException(); return new(new DateTimeOffset(BinaryPrimitives.ReadInt64BigEndian(bytes), TimeSpan.Zero), new Guid(bytes.AsSpan(8))); }
        catch (Exception error) when (error is FormatException or ArgumentException) { throw Bad("InvalidActivityCursor", "cursor is malformed."); }
    }
}
