using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using PAS.AIQuestPortal.Api.HistoricalImport.Control;

namespace PAS.AIQuestPortal.Api.Data;

public sealed class QuestDbContext(DbContextOptions<QuestDbContext> options) : DbContext(options)
{
    public DbSet<Participant> Participants => Set<Participant>();
    public DbSet<Cycle> Cycles => Set<Cycle>();
    public DbSet<CycleParticipant> CycleParticipants => Set<CycleParticipant>();
    public DbSet<CycleEvent> CycleEvents => Set<CycleEvent>();
    public DbSet<CycleParticipantEvent> CycleParticipantEvents => Set<CycleParticipantEvent>();
    public DbSet<CycleTeam> CycleTeams => Set<CycleTeam>();
    public DbSet<CycleTeamMember> CycleTeamMembers => Set<CycleTeamMember>();
    public DbSet<Challenge> Challenges => Set<Challenge>();
    public DbSet<ChallengeTask> ChallengeTasks => Set<ChallengeTask>();
    public DbSet<ChallengeTeamPolicy> ChallengeTeamPolicies => Set<ChallengeTeamPolicy>();
    public DbSet<ChallengeDeadlineChange> ChallengeDeadlineChanges => Set<ChallengeDeadlineChange>();
    public DbSet<ParticipantChallengeDeadlineEvent> ParticipantChallengeDeadlineEvents => Set<ParticipantChallengeDeadlineEvent>();
    public DbSet<ChallengeParticipation> ChallengeParticipations => Set<ChallengeParticipation>();
    public DbSet<ChallengeParticipationMember> ChallengeParticipationMembers => Set<ChallengeParticipationMember>();
    public DbSet<Submission> Submissions => Set<Submission>();
    public DbSet<SubmissionBeneficiary> SubmissionBeneficiaries => Set<SubmissionBeneficiary>();
    public DbSet<SubmissionEvidence> SubmissionEvidence => Set<SubmissionEvidence>();
    public DbSet<SubmissionEvent> SubmissionEvents => Set<SubmissionEvent>();
    public DbSet<AwardCategory> AwardCategories => Set<AwardCategory>();
    public DbSet<RaidSession> RaidSessions => Set<RaidSession>();
    public DbSet<RaidEntitlement> RaidEntitlements => Set<RaidEntitlement>();
    public DbSet<RaidParticipation> RaidParticipations => Set<RaidParticipation>();
    public DbSet<XPEntry> XPEntries => Set<XPEntry>();
    public DbSet<HistoricalImportDataset> HistoricalImportDatasets => Set<HistoricalImportDataset>();
    public DbSet<HistoricalImportRun> HistoricalImportRuns => Set<HistoricalImportRun>();
    public DbSet<HistoricalImportSourceRow> HistoricalImportSourceRows => Set<HistoricalImportSourceRow>();
    public DbSet<HistoricalImportArtifact> HistoricalImportArtifacts => Set<HistoricalImportArtifact>();
    public DbSet<HistoricalImportObservation> HistoricalImportObservations => Set<HistoricalImportObservation>();
    public DbSet<NotificationOutbox> NotificationOutbox => Set<NotificationOutbox>();
    public DbSet<ParticipantExternalIdentity> ParticipantExternalIdentities => Set<ParticipantExternalIdentity>();
    public DbSet<TeamsConversationReference> TeamsConversationReferences => Set<TeamsConversationReference>();
    public DbSet<TeamsChannelDestinationCandidate> TeamsChannelDestinationCandidates => Set<TeamsChannelDestinationCandidate>();
    public DbSet<TeamsChannelDestinationAssignment> TeamsChannelDestinationAssignments => Set<TeamsChannelDestinationAssignment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(QuestDbContext).Assembly);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        RejectAppendOnlyMutations();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        RejectAppendOnlyMutations();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void RejectAppendOnlyMutations()
    {
        IEnumerable<EntityEntry> invalidEntries = ChangeTracker.Entries()
            .Where(entry => entry.Entity is IAppendOnly
                && entry.State is EntityState.Modified or EntityState.Deleted);

        EntityEntry? invalidEntry = invalidEntries.FirstOrDefault();
        if (invalidEntry is not null)
        {
            throw new InvalidOperationException(
                $"{invalidEntry.Metadata.ClrType.Name} is append-only and cannot be updated or deleted.");
        }

        if (ChangeTracker.Entries<CycleParticipant>().Any(x => x.State == EntityState.Deleted))
            throw new InvalidOperationException("CycleParticipant enrollment is durable and cannot be deleted.");

        string[] immutableNotificationProperties = [nameof(PAS.AIQuestPortal.Api.Data.NotificationOutbox.EventId), nameof(PAS.AIQuestPortal.Api.Data.NotificationOutbox.EventType), nameof(PAS.AIQuestPortal.Api.Data.NotificationOutbox.DestinationType), nameof(PAS.AIQuestPortal.Api.Data.NotificationOutbox.DestinationKey), nameof(PAS.AIQuestPortal.Api.Data.NotificationOutbox.RecipientParticipantId), nameof(PAS.AIQuestPortal.Api.Data.NotificationOutbox.AggregateType), nameof(PAS.AIQuestPortal.Api.Data.NotificationOutbox.AggregateId), nameof(PAS.AIQuestPortal.Api.Data.NotificationOutbox.PayloadVersion), nameof(PAS.AIQuestPortal.Api.Data.NotificationOutbox.PayloadJson), nameof(PAS.AIQuestPortal.Api.Data.NotificationOutbox.CreatedAt)];
        foreach (EntityEntry<NotificationOutbox> entry in ChangeTracker.Entries<NotificationOutbox>().Where(x => x.State == EntityState.Modified))
            if (immutableNotificationProperties.Any(property => entry.Property(property).IsModified))
                throw new InvalidOperationException("Notification outbox event, destination, aggregate and payload semantics are immutable after insertion.");

        foreach (EntityEntry<CycleParticipant> enrollment in ChangeTracker.Entries<CycleParticipant>().Where(x => x.State == EntityState.Added))
        {
            bool validRow = enrollment.Entity.Status == CycleParticipantStatus.Active
                && enrollment.Entity.JoinedAt is not null
                && enrollment.Entity.LeftAt is null;
            int matchingEnrollmentEvents = ChangeTracker.Entries<CycleParticipantEvent>().Count(x => x.State == EntityState.Added
                && x.Entity.CycleId == enrollment.Entity.CycleId
                && x.Entity.ParticipantId == enrollment.Entity.ParticipantId
                && x.Entity.EventType == CycleParticipantEventType.Enrolled
                && x.Entity.FromStatus is null
                && x.Entity.ToStatus == CycleParticipantStatus.Active
                && x.Entity.OccurredAt == enrollment.Entity.JoinedAt);
            if (!validRow || matchingEnrollmentEvents != 1)
                throw new InvalidOperationException("A new CycleParticipant requires matching Active enrollment state and an Enrolled event in the same unit of work.");
        }

        foreach (EntityEntry<Cycle> cycleEntry in ChangeTracker.Entries<Cycle>().Where(x => x.State == EntityState.Modified && x.Property(y => y.Status).IsModified))
        {
            CycleStatus previous = cycleEntry.Property(x => x.Status).OriginalValue;
            CycleStatus current = cycleEntry.Property(x => x.Status).CurrentValue;
            bool hasAuditEvent = ChangeTracker.Entries<CycleEvent>().Any(x => x.State == EntityState.Added
                && x.Entity.CycleId == cycleEntry.Entity.Id
                && x.Entity.FromStatus == previous
                && x.Entity.ToStatus == current
                && x.Entity.EventType is CycleEventType.StatusChanged or CycleEventType.Reopened);
            if (!hasAuditEvent)
            {
                throw new InvalidOperationException("A Cycle status change requires a matching append-only CycleEvent in the same unit of work.");
            }
        }

        foreach (EntityEntry<CycleParticipant> enrollment in ChangeTracker.Entries<CycleParticipant>().Where(x => x.State == EntityState.Modified && (x.Property(y => y.Status).IsModified || x.Property(y => y.JoinedAt).IsModified || x.Property(y => y.LeftAt).IsModified)))
        {
            CycleParticipantStatus previous = enrollment.Property(x => x.Status).OriginalValue, current = enrollment.Property(x => x.Status).CurrentValue;
            DateTimeOffset? originalJoinedAt = enrollment.Property(x => x.JoinedAt).OriginalValue;
            DateTimeOffset? currentJoinedAt = enrollment.Property(x => x.JoinedAt).CurrentValue;
            DateTimeOffset? currentLeftAt = enrollment.Property(x => x.LeftAt).CurrentValue;
            CycleParticipantEvent? statusEvent = previous == current ? null : ChangeTracker.Entries<CycleParticipantEvent>()
                .Where(x => x.State == EntityState.Added
                    && x.Entity.CycleId == enrollment.Entity.CycleId
                    && x.Entity.ParticipantId == enrollment.Entity.ParticipantId
                    && x.Entity.EventType == CycleParticipantEventType.StatusChanged
                    && x.Entity.FromStatus == previous
                    && x.Entity.ToStatus == current)
                .Select(x => x.Entity)
                .SingleOrDefault();
            bool validTimestamps = originalJoinedAt == currentJoinedAt
                && (current == CycleParticipantStatus.Active
                    ? currentLeftAt is null
                    : currentLeftAt is not null && statusEvent is not null && currentLeftAt == statusEvent.OccurredAt);
            if (statusEvent is null || !validTimestamps)
                throw new InvalidOperationException("A CycleParticipant state change requires a matching StatusChanged event and approved timestamp semantics in the same unit of work.");
        }

        foreach (EntityEntry<Challenge> challengeEntry in ChangeTracker.Entries<Challenge>().Where(x => x.State == EntityState.Modified && x.Property(y => y.DueAt).IsModified && x.Property(y => y.Status).OriginalValue != ChallengeStatus.Draft))
        {
            DateTimeOffset previous = challengeEntry.Property(x => x.DueAt).OriginalValue;
            DateTimeOffset current = challengeEntry.Property(x => x.DueAt).CurrentValue;
            bool hasAuditEvent = ChangeTracker.Entries<ChallengeDeadlineChange>().Any(x => x.State == EntityState.Added
                && x.Entity.ChallengeId == challengeEntry.Entity.Id
                && x.Entity.PreviousDueAt == previous
                && x.Entity.NewDueAt == current);
            if (!hasAuditEvent)
            {
                throw new InvalidOperationException("A Challenge deadline change requires a matching append-only ChallengeDeadlineChange in the same unit of work.");
            }
        }
    }
}
