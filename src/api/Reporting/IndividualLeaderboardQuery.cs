using System.Text;
using Microsoft.EntityFrameworkCore;
using PAS.AIQuestPortal.Api.Data;

namespace PAS.AIQuestPortal.Api.Reporting;

public sealed class IndividualLeaderboardQuery(QuestDbContext db)
{
    public async Task<IReadOnlyList<LeaderboardEntry>> ExecuteAsync(Guid cycleId, Guid? currentParticipantId, CancellationToken ct)
    {
        var rows = await (from membership in db.CycleParticipants.AsNoTracking()
            join participant in db.Participants.AsNoTracking() on membership.ParticipantId equals participant.Id
            where membership.CycleId == cycleId && membership.Status == CycleParticipantStatus.Active
            select new
            {
                participant.Id,
                participant.DisplayName,
                Total = db.XPEntries.Where(x => x.CycleId == cycleId && x.ParticipantId == participant.Id).Sum(x => (int?)x.Amount) ?? 0
            }).ToListAsync(ct);

        var ordered = rows.OrderByDescending(x => x.Total)
            .ThenBy(x => Normalize(x.DisplayName), StringComparer.Ordinal)
            .ThenBy(x => x.Id)
            .ToArray();
        var result = new List<LeaderboardEntry>(ordered.Length);
        int previousTotal = 0, rank = 0;
        for (int index = 0; index < ordered.Length; index++)
        {
            if (index == 0 || ordered[index].Total != previousTotal) rank = index + 1;
            previousTotal = ordered[index].Total;
            result.Add(new(rank, ordered[index].Id, ordered[index].DisplayName, ordered[index].Total, ordered[index].Id == currentParticipantId));
        }
        return result;
    }

    private static string Normalize(string value) => value.Normalize(NormalizationForm.FormKC).Trim().ToUpperInvariant();
}
