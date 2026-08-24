using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PAS.AIQuestPortal.Api.Configuration;
using PAS.AIQuestPortal.Api.Data;

namespace PAS.AIQuestPortal.Api.Development;

public sealed class DevelopmentDemoDataSeeder(
    QuestDbContext database,
    IHostEnvironment environment,
    IOptions<QuestAuthenticationOptions> authentication,
    TimeProvider clock)
{
    public static readonly Guid TeammateParticipantId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    public static readonly Guid CycleId = Guid.Parse("60000000-0000-4000-8000-000000000001");
    public static readonly Guid ChallengeId = Guid.Parse("60000000-0000-4000-8000-000000000002");
    public static readonly Guid ParticipationId = Guid.Parse("60000000-0000-4000-8000-000000000003");
    public static readonly Guid TaskId = Guid.Parse("60000000-0000-4000-8000-000000000004");

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        QuestAuthenticationOptions options = authentication.Value;
        if (!environment.IsDevelopment() || !string.Equals(options.Mode, AuthenticationModes.Demo, StringComparison.OrdinalIgnoreCase))
            return;

        DemoProfileOptions participantProfile = RequiredProfile(options, QuestRoles.Participant);
        DemoProfileOptions managerProfile = RequiredProfile(options, QuestRoles.Manager);
        DateTimeOffset now = clock.GetUtcNow();

        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        Participant participant = await UpsertParticipantAsync(participantProfile.ParticipantId, participantProfile.DisplayName, now, cancellationToken);
        Participant manager = await UpsertParticipantAsync(managerProfile.ParticipantId, managerProfile.DisplayName, now, cancellationToken);
        Participant teammate = await UpsertParticipantAsync(TeammateParticipantId, "Synthetic Teammate", now, cancellationToken);

        Cycle? cycle = await database.Cycles.FindAsync([CycleId], cancellationToken);
        if (cycle is null)
        {
            cycle = new Cycle
            {
                Id = CycleId, Code = "DEMO-SHOWCASE", Name = "Synthetic Showcase Cycle",
                Status = CycleStatus.Active, StartsAt = now.AddDays(-30), EndsAt = now.AddDays(30),
                CreatedAt = now, CreatedByParticipantId = manager.Id
            };
            database.Cycles.Add(cycle);
        }

        foreach (Participant member in new[] { participant, manager, teammate })
        {
            CycleParticipant? membership = await database.CycleParticipants.FindAsync([CycleId, member.Id], cancellationToken);
            if (membership is null)
            {
                membership = new CycleParticipant { CycleId = CycleId, ParticipantId = member.Id, JoinedAt = now };
                database.CycleParticipants.Add(membership);
            }
            membership.Status = CycleParticipantStatus.Active;
            membership.LeftAt = null;
        }

        Challenge? challenge = await database.Challenges.FindAsync([ChallengeId], cancellationToken);
        if (challenge is null)
        {
            challenge = new Challenge
            {
                Id = ChallengeId, CycleId = CycleId, CreatedAt = now, CreatedByParticipantId = manager.Id,
                Name = "Synthetic Shared Challenge", Description = "A local-only challenge for demonstrating the Step 6 workflow.",
                Category = "Synthetic Showcase", Status = ChallengeStatus.Open,
                OpenAt = now.AddDays(-7), DueAt = now.AddDays(14), CloseAt = now.AddDays(21)
            };
            database.Challenges.Add(challenge);
        }

        ChallengeTeamPolicy? policy = await database.ChallengeTeamPolicies.FindAsync([ChallengeId], cancellationToken);
        if (policy is null)
        {
            policy = new ChallengeTeamPolicy
            {
                ChallengeId = ChallengeId, FormationMode = FormationMode.ManagerAssigned,
                MinMembers = 2, MaxMembers = 2, AllowSolo = false,
                FormationDeadline = challenge.DueAt, LockAfterStart = true
            };
            database.ChallengeTeamPolicies.Add(policy);
        }

        ChallengeParticipation? participation = await database.ChallengeParticipations.FindAsync([ParticipationId], cancellationToken);
        if (participation is null)
        {
            participation = new ChallengeParticipation
            {
                Id = ParticipationId, ChallengeId = ChallengeId, CycleId = CycleId,
                CreatedAt = now, CreatedByParticipantId = manager.Id
            };
            database.ChallengeParticipations.Add(participation);
        }

        foreach (Guid memberId in new[] { participant.Id, teammate.Id })
        {
            if (await database.ChallengeParticipationMembers.FindAsync([ParticipationId, memberId], cancellationToken) is null)
            {
                database.ChallengeParticipationMembers.Add(new ChallengeParticipationMember
                {
                    ChallengeParticipationId = ParticipationId, ChallengeId = ChallengeId, CycleId = CycleId,
                    ParticipantId = memberId, JoinedSnapshotAt = now
                });
            }
        }

        ChallengeTask? task = await database.ChallengeTasks.FindAsync([TaskId], cancellationToken);
        if (task is null)
        {
            task = new ChallengeTask { Id = TaskId, ChallengeId = ChallengeId, Name = "Complete the synthetic shared task" };
            database.ChallengeTasks.Add(task);
        }
        task.Name = "Complete the synthetic shared task";
        task.Description = "Submit text evidence, respond to manager feedback, and earn 25 XP together.";
        task.XP = 25;
        task.EvidenceRequirement = EvidenceRequirement.Text;
        task.ScoringMode = ScoringMode.WholeTeam;
        task.SortOrder = 1;

        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<Participant> UpsertParticipantAsync(Guid id, string displayName, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Participant? participant = await database.Participants.FindAsync([id], cancellationToken);
        if (participant is null)
        {
            participant = new Participant { Id = id, DisplayName = displayName, CreatedAt = now };
            database.Participants.Add(participant);
        }
        participant.DisplayName = displayName;
        participant.IsActive = true;
        return participant;
    }

    private static DemoProfileOptions RequiredProfile(QuestAuthenticationOptions options, string role)
    {
        DemoProfileOptions[] matches = options.Demo.Profiles
            .Where(profile => profile.Enabled && profile.Roles.Contains(role, StringComparer.Ordinal))
            .ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException($"Development demo showcase requires exactly one enabled '{role}' profile; found {matches.Length}.");
    }
}
