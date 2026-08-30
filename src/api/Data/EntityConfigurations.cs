using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PAS.AIQuestPortal.Api.Data;

internal static class ConfigurationExtensions
{
    public static PropertyBuilder<TEnum> AsString<TEnum>(this PropertyBuilder<TEnum> property, int length = 40)
        where TEnum : struct, Enum => property.HasConversion<string>().HasMaxLength(length);

    public static ReferenceCollectionBuilder Restrict(this ReferenceCollectionBuilder relationship) =>
        relationship.OnDelete(DeleteBehavior.NoAction);

    public static ReferenceReferenceBuilder Restrict(this ReferenceReferenceBuilder relationship) =>
        relationship.OnDelete(DeleteBehavior.NoAction);
}

public sealed class ParticipantConfiguration : IEntityTypeConfiguration<Participant>
{
    public void Configure(EntityTypeBuilder<Participant> b)
    {
        b.ToTable("Participants"); b.HasKey(x => x.Id);
        b.Property(x => x.DisplayName).HasMaxLength(200);
        b.HasIndex(x => x.EntraObjectId).IsUnique().HasFilter("[EntraObjectId] IS NOT NULL");
    }
}

public sealed class CycleConfiguration : IEntityTypeConfiguration<Cycle>
{
    public void Configure(EntityTypeBuilder<Cycle> b)
    {
        b.ToTable("Cycles", t => { t.HasCheckConstraint("CK_Cycles_DateRange", "[StartsAt] < [EndsAt]"); t.HasCheckConstraint("CK_Cycles_Status", "[Status] IN ('Active','Closing','Finalised')"); });
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(50); b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Name).HasMaxLength(200); b.Property(x => x.Status).AsString();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne<Participant>().WithMany().HasForeignKey(x => x.CreatedByParticipantId).Restrict();
    }
}

public sealed class CycleParticipantConfiguration : IEntityTypeConfiguration<CycleParticipant>
{
    public void Configure(EntityTypeBuilder<CycleParticipant> b)
    {
        b.ToTable("CycleParticipants", t => { t.HasCheckConstraint("CK_CycleParticipants_Dates", "[LeftAt] IS NULL OR [JoinedAt] IS NULL OR [LeftAt] >= [JoinedAt]"); t.HasCheckConstraint("CK_CycleParticipants_Status", "[Status] IN ('Active','Withdrawn','Inactive')"); });
        b.HasKey(x => new { x.CycleId, x.ParticipantId }); b.Property(x => x.Status).AsString();
        b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne<Cycle>().WithMany().HasForeignKey(x => x.CycleId).Restrict();
        b.HasOne<Participant>().WithMany().HasForeignKey(x => x.ParticipantId).Restrict();
        b.HasIndex(x => new { x.CycleId, x.Status }); b.HasIndex(x => new { x.ParticipantId, x.CycleId });
    }
}

public sealed class CycleParticipantEventConfiguration : IEntityTypeConfiguration<CycleParticipantEvent>
{
    public void Configure(EntityTypeBuilder<CycleParticipantEvent> b)
    {
        b.ToTable("CycleParticipantEvents", t =>
        {
            t.HasCheckConstraint("CK_CycleParticipantEvents_Sequence", "[SequenceNumber] > 0");
            t.HasCheckConstraint("CK_CycleParticipantEvents_EventType", "[EventType] IN ('Enrolled','StatusChanged')");
            t.HasCheckConstraint("CK_CycleParticipantEvents_StatusValues", "([FromStatus] IS NULL OR [FromStatus] IN ('Active','Withdrawn','Inactive')) AND [ToStatus] IN ('Active','Withdrawn','Inactive')");
            t.HasCheckConstraint("CK_CycleParticipantEvents_Shape", "([EventType] = 'Enrolled' AND [FromStatus] IS NULL AND [ToStatus] = 'Active') OR ([EventType] = 'StatusChanged' AND [FromStatus] IS NOT NULL AND [FromStatus] <> [ToStatus])");
            t.HasCheckConstraint("CK_CycleParticipantEvents_Reason", "LEN(LTRIM(RTRIM([Reason]))) > 0 AND DATALENGTH([Reason]) = DATALENGTH(LTRIM(RTRIM([Reason])))");
        });
        b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CycleId, x.ParticipantId, x.SequenceNumber });
        b.Property(x => x.EventType).AsString(); b.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(40); b.Property(x => x.ToStatus).AsString(); b.Property(x => x.Reason).HasMaxLength(1000);
        b.HasOne<CycleParticipant>().WithMany().HasForeignKey(x => new { x.CycleId, x.ParticipantId }).Restrict(); b.HasOne<Participant>().WithMany().HasForeignKey(x => x.ActorId).Restrict();
        b.HasIndex(x => new { x.CycleId, x.ParticipantId, x.OccurredAt, x.Id });
    }
}

public sealed class CycleEventConfiguration : IEntityTypeConfiguration<CycleEvent>
{
    public void Configure(EntityTypeBuilder<CycleEvent> b)
    {
        b.ToTable("CycleEvents", t =>
        {
            t.HasCheckConstraint("CK_CycleEvents_Sequence", "[SequenceNumber] > 0");
            t.HasCheckConstraint("CK_CycleEvents_StatusShape", "([EventType] = 'Created' AND [FromStatus] IS NULL AND [ToStatus] IS NOT NULL) OR ([EventType] IN ('StatusChanged','Reopened') AND [FromStatus] IS NOT NULL AND [ToStatus] IS NOT NULL AND [FromStatus] <> [ToStatus]) OR ([EventType] IN ('CorrectionAuthorised','CorrectionRecorded') AND [FromStatus] IS NULL AND [ToStatus] IS NULL)");
            t.HasCheckConstraint("CK_CycleEvents_StatusValues", "([FromStatus] IS NULL OR [FromStatus] IN ('Active','Closing','Finalised')) AND ([ToStatus] IS NULL OR [ToStatus] IN ('Active','Closing','Finalised'))");
        });
        b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CycleId, x.SequenceNumber }); b.HasAlternateKey(x => new { x.Id, x.CycleId });
        b.Property(x => x.EventType).AsString(); b.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(40); b.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(40); b.Property(x => x.Reason).HasMaxLength(1000);
        b.HasOne<Cycle>().WithMany().HasForeignKey(x => x.CycleId).Restrict(); b.HasOne<Participant>().WithMany().HasForeignKey(x => x.ActorId).Restrict();
        b.HasOne<CycleEvent>().WithMany().HasForeignKey(x => new { x.SupersedesEventId, x.CycleId }).HasPrincipalKey(x => new { x.Id, x.CycleId }).Restrict();
        b.HasOne<XPEntry>().WithMany().HasForeignKey(x => x.RelatedXPEntryId).Restrict();
        b.HasIndex(x => x.SupersedesEventId).IsUnique().HasFilter("[SupersedesEventId] IS NOT NULL"); b.HasIndex(x => new { x.CycleId, x.OccurredAt, x.Id }); b.HasIndex(x => x.CorrelationId).HasFilter("[CorrelationId] IS NOT NULL");
    }
}

public sealed class CycleTeamConfiguration : IEntityTypeConfiguration<CycleTeam>
{
    public void Configure(EntityTypeBuilder<CycleTeam> b)
    {
        b.ToTable("CycleTeams"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.Id, x.CycleId }); b.Property(x => x.Name).HasMaxLength(200);
        b.HasIndex(x => new { x.CycleId, x.Name }).IsUnique(); b.HasOne<Cycle>().WithMany().HasForeignKey(x => x.CycleId).Restrict();
    }
}

public sealed class CycleTeamMemberConfiguration : IEntityTypeConfiguration<CycleTeamMember>
{
    public void Configure(EntityTypeBuilder<CycleTeamMember> b)
    {
        b.ToTable("CycleTeamMembers", t => t.HasCheckConstraint("CK_CycleTeamMembers_Dates", "[LeftAt] IS NULL OR [LeftAt] >= [JoinedAt]")); b.HasKey(x => x.Id);
        b.HasOne<CycleTeam>().WithMany().HasForeignKey(x => new { x.CycleTeamId, x.CycleId }).HasPrincipalKey(x => new { x.Id, x.CycleId }).Restrict();
        b.HasOne<CycleParticipant>().WithMany().HasForeignKey(x => new { x.CycleId, x.ParticipantId }).Restrict();
        b.HasIndex(x => new { x.CycleTeamId, x.ParticipantId, x.JoinedAt }).IsUnique();
        b.HasIndex(x => new { x.CycleId, x.ParticipantId }).IsUnique().HasFilter("[LeftAt] IS NULL").HasDatabaseName("UX_CycleTeamMembers_OpenMembership");
    }
}

public sealed class ChallengeConfiguration : IEntityTypeConfiguration<Challenge>
{
    public void Configure(EntityTypeBuilder<Challenge> b)
    {
        b.ToTable("Challenges", t => { t.HasCheckConstraint("CK_Challenges_Dates", "[OpenAt] <= [DueAt] AND [DueAt] <= [CloseAt]"); t.HasCheckConstraint("CK_Challenges_Status", "[Status] IN ('Draft','Published','Open','Closed','Archived')"); }); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.Id, x.CycleId });
        b.Property(x => x.Name).HasMaxLength(200); b.Property(x => x.Category).HasMaxLength(100); b.Property(x => x.Status).AsString(); b.Property(x => x.HeroImageReference).HasMaxLength(1000); b.Property(x => x.GuideCharacter).HasMaxLength(100); b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne<Cycle>().WithMany().HasForeignKey(x => x.CycleId).Restrict(); b.HasOne<Participant>().WithMany().HasForeignKey(x => x.CreatedByParticipantId).Restrict();
    }
}

public sealed class ChallengeTaskConfiguration : IEntityTypeConfiguration<ChallengeTask>
{
    public void Configure(EntityTypeBuilder<ChallengeTask> b)
    {
        b.ToTable("ChallengeTasks", t => { t.HasCheckConstraint("CK_ChallengeTasks_XP", "[XP] >= 0"); t.HasCheckConstraint("CK_ChallengeTasks_EvidenceRequirement", "[EvidenceRequirement] IN ('None','Text','Link','Attachment','Multiple','Custom')"); t.HasCheckConstraint("CK_ChallengeTasks_ScoringMode", "[ScoringMode] IN ('Individual','WholeTeam','ClaimantSelectsBeneficiaries','AttendanceBased')"); }); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.Id, x.ChallengeId });
        b.Property(x => x.Name).HasMaxLength(200); b.Property(x => x.EvidenceRequirement).AsString(); b.Property(x => x.ScoringMode).AsString(); b.HasIndex(x => new { x.ChallengeId, x.SortOrder }).IsUnique();
        b.HasOne<Challenge>().WithMany().HasForeignKey(x => x.ChallengeId).Restrict();
    }
}

public sealed class ChallengeTeamPolicyConfiguration : IEntityTypeConfiguration<ChallengeTeamPolicy>
{
    public void Configure(EntityTypeBuilder<ChallengeTeamPolicy> b)
    {
        b.ToTable("ChallengeTeamPolicies", t => { t.HasCheckConstraint("CK_ChallengeTeamPolicies_Size", "[MinMembers] > 0 AND [MaxMembers] > 0 AND [MinMembers] <= [MaxMembers]"); t.HasCheckConstraint("CK_ChallengeTeamPolicies_Solo", "[AllowSolo] = 1 OR [MinMembers] > 1"); t.HasCheckConstraint("CK_ChallengeTeamPolicies_FormationMode", "[FormationMode] IN ('SelfForm','ManagerAssigned','Either')"); }); b.HasKey(x => x.ChallengeId); b.Property(x => x.FormationMode).AsString();
        b.HasOne<Challenge>().WithOne().HasForeignKey<ChallengeTeamPolicy>(x => x.ChallengeId).Restrict();
    }
}

public sealed class ChallengeDeadlineChangeConfiguration : IEntityTypeConfiguration<ChallengeDeadlineChange>
{
    public void Configure(EntityTypeBuilder<ChallengeDeadlineChange> b)
    {
        b.ToTable("ChallengeDeadlineChanges", t => t.HasCheckConstraint("CK_ChallengeDeadlineChanges_Sequence", "[SequenceNumber] > 0")); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.ChallengeId, x.SequenceNumber }); b.HasAlternateKey(x => new { x.Id, x.ChallengeId }); b.Property(x => x.Reason).HasMaxLength(1000);
        b.HasOne<Challenge>().WithMany().HasForeignKey(x => x.ChallengeId).Restrict(); b.HasOne<Participant>().WithMany().HasForeignKey(x => x.ChangedByParticipantId).Restrict();
        b.HasOne<ChallengeDeadlineChange>().WithMany().HasForeignKey(x => new { x.SupersedesChangeId, x.ChallengeId }).HasPrincipalKey(x => new { x.Id, x.ChallengeId }).Restrict();
        b.HasIndex(x => x.SupersedesChangeId).IsUnique().HasFilter("[SupersedesChangeId] IS NOT NULL"); b.HasIndex(x => new { x.ChallengeId, x.ChangedAt });
    }
}

public sealed class ParticipantChallengeDeadlineEventConfiguration : IEntityTypeConfiguration<ParticipantChallengeDeadlineEvent>
{
    public void Configure(EntityTypeBuilder<ParticipantChallengeDeadlineEvent> b)
    {
        b.ToTable("ParticipantChallengeDeadlineEvents", t => { t.HasCheckConstraint("CK_ParticipantDeadlineEvents_Sequence", "[SequenceNumber] > 0"); t.HasCheckConstraint("CK_ParticipantDeadlineEvents_Shape", "([EventType] = 'OverrideCleared' AND [NewOverrideDueAt] IS NULL) OR ([EventType] IN ('OverrideSet','OverrideChanged') AND [NewOverrideDueAt] IS NOT NULL)"); });
        b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.ChallengeId, x.ParticipantId, x.SequenceNumber }); b.HasAlternateKey(x => new { x.Id, x.ChallengeId, x.ParticipantId }); b.Property(x => x.EventType).AsString(); b.Property(x => x.Reason).HasMaxLength(1000);
        b.HasOne<Challenge>().WithMany().HasForeignKey(x => x.ChallengeId).Restrict(); b.HasOne<Participant>().WithMany().HasForeignKey(x => x.ParticipantId).Restrict(); b.HasOne<Participant>().WithMany().HasForeignKey(x => x.ActorId).Restrict();
        b.HasOne<ParticipantChallengeDeadlineEvent>().WithMany().HasForeignKey(x => new { x.SupersedesEventId, x.ChallengeId, x.ParticipantId }).HasPrincipalKey(x => new { x.Id, x.ChallengeId, x.ParticipantId }).Restrict();
        b.HasIndex(x => x.SupersedesEventId).IsUnique().HasFilter("[SupersedesEventId] IS NOT NULL"); b.HasIndex(x => new { x.ChallengeId, x.ParticipantId, x.OccurredAt, x.Id });
    }
}

public sealed class ChallengeParticipationConfiguration : IEntityTypeConfiguration<ChallengeParticipation>
{
    public void Configure(EntityTypeBuilder<ChallengeParticipation> b)
    {
        b.ToTable("ChallengeParticipations"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.Id, x.ChallengeId }); b.HasAlternateKey(x => new { x.Id, x.ChallengeId, x.CycleId });
        b.HasOne<Challenge>().WithMany().HasForeignKey(x => new { x.ChallengeId, x.CycleId }).HasPrincipalKey(x => new { x.Id, x.CycleId }).Restrict(); b.HasOne<Participant>().WithMany().HasForeignKey(x => x.CreatedByParticipantId).Restrict();
    }
}

public sealed class ChallengeParticipationMemberConfiguration : IEntityTypeConfiguration<ChallengeParticipationMember>
{
    public void Configure(EntityTypeBuilder<ChallengeParticipationMember> b)
    {
        b.ToTable("ChallengeParticipationMembers"); b.HasKey(x => new { x.ChallengeParticipationId, x.ParticipantId });
        b.HasOne<ChallengeParticipation>().WithMany().HasForeignKey(x => new { x.ChallengeParticipationId, x.ChallengeId, x.CycleId }).HasPrincipalKey(x => new { x.Id, x.ChallengeId, x.CycleId }).Restrict();
        b.HasOne<CycleParticipant>().WithMany().HasForeignKey(x => new { x.CycleId, x.ParticipantId }).Restrict();
        b.HasOne<CycleTeam>().WithMany().HasForeignKey(x => new { CycleTeamId = x.CycleTeamIdAtParticipation, x.CycleId }).HasPrincipalKey(x => new { x.Id, x.CycleId }).Restrict();
        b.HasIndex(x => new { x.ParticipantId, x.ChallengeId }); b.HasIndex(x => new { x.CycleTeamIdAtParticipation, x.CycleId }).HasFilter("[CycleTeamIdAtParticipation] IS NOT NULL");
    }
}

public sealed class SubmissionConfiguration : IEntityTypeConfiguration<Submission>
{
    public void Configure(EntityTypeBuilder<Submission> b)
    {
        b.ToTable("Submissions", t => t.HasCheckConstraint("CK_Submissions_Status", "[Status] IN ('Submitted','UnderReview','NeedsEvidence','Resubmitted','Approved','Rejected')")); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.Id, x.CycleId }); b.HasAlternateKey(x => new { x.Id, x.ChallengeId, x.TaskId, x.CycleId });
        b.Property(x => x.Status).AsString(); b.Property(x => x.Comment).HasMaxLength(2000); b.Property(x => x.ReviewerComment).HasMaxLength(2000);
        b.HasOne<Challenge>().WithMany().HasForeignKey(x => new { x.ChallengeId, x.CycleId }).HasPrincipalKey(x => new { x.Id, x.CycleId }).Restrict();
        b.HasOne<ChallengeTask>().WithMany().HasForeignKey(x => new { x.TaskId, x.ChallengeId }).HasPrincipalKey(x => new { x.Id, x.ChallengeId }).Restrict();
        b.HasOne<CycleParticipant>().WithMany().HasForeignKey(x => new { x.CycleId, ParticipantId = x.ClaimantId }).HasPrincipalKey(x => new { x.CycleId, x.ParticipantId }).Restrict();
        b.HasOne<ChallengeParticipation>().WithMany().HasForeignKey(x => new { x.ChallengeParticipationId, x.ChallengeId, x.CycleId }).HasPrincipalKey(x => new { Id = (Guid?)x.Id, x.ChallengeId, x.CycleId }).Restrict();
        b.HasIndex(x => new { x.Status, x.SubmittedAt }); b.HasIndex(x => new { x.ClaimantId, x.SubmittedAt });
    }
}

public sealed class SubmissionBeneficiaryConfiguration : IEntityTypeConfiguration<SubmissionBeneficiary>
{
    public void Configure(EntityTypeBuilder<SubmissionBeneficiary> b)
    {
        b.ToTable("SubmissionBeneficiaries"); b.HasKey(x => new { x.SubmissionId, x.ParticipantId }); b.HasAlternateKey(x => new { x.SubmissionId, x.ParticipantId, x.CycleId });
        b.HasOne<Submission>().WithMany().HasForeignKey(x => new { x.SubmissionId, x.CycleId }).HasPrincipalKey(x => new { x.Id, x.CycleId }).Restrict();
        b.HasOne<CycleParticipant>().WithMany().HasForeignKey(x => new { x.CycleId, x.ParticipantId }).Restrict();
        b.HasOne<Participant>().WithMany().HasForeignKey(x => x.AddedByParticipantId).Restrict();
    }
}

public sealed class SubmissionEvidenceConfiguration : IEntityTypeConfiguration<SubmissionEvidence>
{
    public void Configure(EntityTypeBuilder<SubmissionEvidence> b)
    {
        b.ToTable("SubmissionEvidence", t =>
        {
            t.HasCheckConstraint("CK_SubmissionEvidence_Shape", "([EvidenceKind] = 'Text' AND [TextValue] IS NOT NULL AND [LinkUrl] IS NULL AND [BlobKey] IS NULL) OR ([EvidenceKind] = 'Link' AND [LinkUrl] IS NOT NULL AND [TextValue] IS NULL AND [BlobKey] IS NULL) OR ([EvidenceKind] = 'Attachment' AND [StorageAccount] IS NOT NULL AND [Container] IS NOT NULL AND [BlobKey] IS NOT NULL AND [OriginalFileName] IS NOT NULL AND [MimeType] IS NOT NULL AND [SizeBytes] IS NOT NULL AND [SizeBytes] >= 0 AND [TextValue] IS NULL AND [LinkUrl] IS NULL)");
        });
        b.HasKey(x => x.Id); b.Property(x => x.EvidenceKind).AsString(); b.Property(x => x.LinkUrl).HasMaxLength(2048); b.Property(x => x.StorageAccount).HasMaxLength(100); b.Property(x => x.Container).HasMaxLength(100); b.Property(x => x.BlobKey).HasMaxLength(1024); b.Property(x => x.OriginalFileName).HasMaxLength(255); b.Property(x => x.MimeType).HasMaxLength(255); b.Property(x => x.Description).HasMaxLength(1000);
        b.HasOne<Submission>().WithMany().HasForeignKey(x => x.SubmissionId).Restrict(); b.HasOne<Participant>().WithMany().HasForeignKey(x => x.ProvidedByParticipantId).Restrict(); b.HasIndex(x => new { x.SubmissionId, x.CreatedAt });
    }
}

public sealed class SubmissionEventConfiguration : IEntityTypeConfiguration<SubmissionEvent>
{
    public void Configure(EntityTypeBuilder<SubmissionEvent> b)
    {
        b.ToTable("SubmissionEvents"); b.HasKey(x => x.Id); b.Property(x => x.EventType).HasMaxLength(50); b.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(40); b.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(40); b.Property(x => x.Comment).HasMaxLength(2000);
        b.HasOne<Submission>().WithMany().HasForeignKey(x => x.SubmissionId).Restrict(); b.HasOne<Participant>().WithMany().HasForeignKey(x => x.ActorId).Restrict(); b.HasIndex(x => new { x.SubmissionId, x.OccurredAt, x.Id });
    }
}

public sealed class AwardCategoryConfiguration : IEntityTypeConfiguration<AwardCategory>
{
    public void Configure(EntityTypeBuilder<AwardCategory> b)
    {
        b.ToTable("AwardCategories"); b.HasKey(x => x.Id); b.Property(x => x.Code).HasMaxLength(100); b.Property(x => x.Name).HasMaxLength(200); b.HasOne<Cycle>().WithMany().HasForeignKey(x => x.CycleId).Restrict();
        b.HasIndex(x => x.Code).IsUnique().HasFilter("[CycleId] IS NULL").HasDatabaseName("UX_AwardCategories_GlobalCode"); b.HasIndex(x => new { x.CycleId, x.Code }).IsUnique().HasFilter("[CycleId] IS NOT NULL").HasDatabaseName("UX_AwardCategories_CycleCode");
    }
}

public sealed class RaidSessionConfiguration : IEntityTypeConfiguration<RaidSession>
{
    public void Configure(EntityTypeBuilder<RaidSession> b)
    {
        b.ToTable("RaidSessions"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.Id, x.CycleId }); b.Property(x => x.Name).HasMaxLength(200); b.Property(x => x.RowVersion).IsRowVersion(); b.HasOne<Cycle>().WithMany().HasForeignKey(x => x.CycleId).Restrict();
    }
}

public sealed class RaidEntitlementConfiguration : IEntityTypeConfiguration<RaidEntitlement>
{
    public void Configure(EntityTypeBuilder<RaidEntitlement> b)
    {
        b.ToTable("RaidEntitlements", t => { t.HasCheckConstraint("CK_RaidEntitlements_AssignedCount", "[AssignedCount] >= 0"); t.HasCheckConstraint("CK_RaidEntitlements_PassType", "[PassType] IN ('Physical','Remote')"); }); b.HasKey(x => new { x.ParticipantId, x.CycleId, x.PassType }); b.Property(x => x.PassType).AsString(); b.Property(x => x.RowVersion).IsRowVersion();
        b.HasOne<CycleParticipant>().WithMany().HasForeignKey(x => new { x.CycleId, x.ParticipantId }).Restrict();
    }
}

public sealed class RaidParticipationConfiguration : IEntityTypeConfiguration<RaidParticipation>
{
    public void Configure(EntityTypeBuilder<RaidParticipation> b)
    {
        b.ToTable("RaidParticipations", t => t.HasCheckConstraint("CK_RaidParticipations_PassType", "[PassType] IN ('Physical','Remote')")); b.HasKey(x => x.Id); b.Property(x => x.PassType).AsString();
        b.HasOne<RaidSession>().WithMany().HasForeignKey(x => new { x.RaidSessionId, x.CycleId }).HasPrincipalKey(x => new { x.Id, x.CycleId }).Restrict(); b.HasOne<CycleParticipant>().WithMany().HasForeignKey(x => new { x.CycleId, x.ParticipantId }).Restrict(); b.HasIndex(x => new { x.ParticipantId, x.RaidSessionId }).IsUnique(); b.HasIndex(x => new { x.ParticipantId, x.CycleId, x.PassType });
    }
}

public sealed class XPEntryConfiguration : IEntityTypeConfiguration<XPEntry>
{
    public void Configure(EntityTypeBuilder<XPEntry> b)
    {
        b.ToTable("XPEntries", t =>
        {
            t.HasCheckConstraint("CK_XPEntries_AmountAndReversal", "([EntryType] = 'Grant' AND [ReversesEntryId] IS NULL AND [Amount] > 0) OR ([EntryType] = 'Reversal' AND [ReversesEntryId] IS NOT NULL AND [Amount] < 0) OR ([EntryType] = 'Correction' AND [ReversesEntryId] IS NOT NULL AND [Amount] > 0)");
            t.HasCheckConstraint("CK_XPEntries_SourceShape", "([SourceType] = 'TaskApproval' AND [SubmissionId] IS NOT NULL AND [ChallengeId] IS NOT NULL AND [TaskId] IS NOT NULL AND [AwardCategoryId] IS NULL AND [RaidSessionId] IS NULL) OR ([SourceType] = 'ManualAward' AND [AwardCategoryId] IS NOT NULL AND [SubmissionId] IS NULL AND [TaskId] IS NULL AND [RaidSessionId] IS NULL) OR ([SourceType] = 'Raid' AND [RaidSessionId] IS NOT NULL AND [AwardCategoryId] IS NULL AND [SubmissionId] IS NULL AND [TaskId] IS NULL)");
            t.HasCheckConstraint("CK_XPEntries_ParticipationChallenge", "[ChallengeParticipationId] IS NULL OR [ChallengeId] IS NOT NULL");
        });
        b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.Id, x.ParticipantId, x.CycleId, x.SourceType }); b.Property(x => x.EntryType).AsString(); b.Property(x => x.SourceType).AsString(); b.Property(x => x.Reason).HasMaxLength(2000);
        b.HasOne<CycleParticipant>().WithMany().HasForeignKey(x => new { x.CycleId, x.ParticipantId }).Restrict(); b.HasOne<Participant>().WithMany().HasForeignKey(x => x.AwardedByParticipantId).Restrict();
        b.HasOne<AwardCategory>().WithMany().HasForeignKey(x => x.AwardCategoryId).Restrict();
        b.HasOne<Submission>().WithMany().HasForeignKey(x => new { x.SubmissionId, x.ChallengeId, x.TaskId, x.CycleId }).HasPrincipalKey(x => new { Id = (Guid?)x.Id, ChallengeId = (Guid?)x.ChallengeId, TaskId = (Guid?)x.TaskId, x.CycleId }).Restrict();
        b.HasOne<SubmissionBeneficiary>().WithMany().HasForeignKey(x => new { x.SubmissionId, x.ParticipantId }).HasPrincipalKey(x => new { SubmissionId = (Guid?)x.SubmissionId, x.ParticipantId }).Restrict();
        b.HasOne<RaidSession>().WithMany().HasForeignKey(x => new { x.RaidSessionId, x.CycleId }).HasPrincipalKey(x => new { Id = (Guid?)x.Id, x.CycleId }).Restrict();
        b.HasOne<CycleTeam>().WithMany().HasForeignKey(x => new { x.CycleTeamId, x.CycleId }).HasPrincipalKey(x => new { Id = (Guid?)x.Id, x.CycleId }).Restrict();
        b.HasOne<ChallengeParticipation>().WithMany().HasForeignKey(x => new { x.ChallengeParticipationId, x.ChallengeId, x.CycleId }).HasPrincipalKey(x => new { Id = (Guid?)x.Id, ChallengeId = (Guid?)x.ChallengeId, x.CycleId }).Restrict();
        b.HasOne<XPEntry>().WithMany().HasForeignKey(x => new { x.ReversesEntryId, x.ParticipantId, x.CycleId, x.SourceType }).HasPrincipalKey(x => new { Id = (Guid?)x.Id, x.ParticipantId, x.CycleId, x.SourceType }).Restrict();
        b.HasIndex(x => new { x.SubmissionId, x.ParticipantId }).IsUnique().HasFilter("[EntryType] = 'Grant' AND [SourceType] = 'TaskApproval'").HasDatabaseName("UX_XPEntries_TaskApprovalGrant_Submission_Participant");
        b.HasIndex(x => new { x.CycleId, x.ParticipantId }); b.HasIndex(x => new { x.ChallengeId, x.TaskId }); b.HasIndex(x => new { x.AwardCategoryId, x.CycleId }); b.HasIndex(x => new { x.RaidSessionId, x.ParticipantId }); b.HasIndex(x => new { x.CycleTeamId, x.CycleId }); b.HasIndex(x => x.ChallengeParticipationId); b.HasIndex(x => x.ReversesEntryId);
    }
}
