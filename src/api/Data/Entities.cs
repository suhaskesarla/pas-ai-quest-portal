namespace PAS.AIQuestPortal.Api.Data;

public interface IAppendOnly;

public enum CycleStatus { Active, Closing, Finalised }
public enum CycleParticipantStatus { Active, Withdrawn, Inactive }
public enum ChallengeStatus { Draft, Published, Open, Closed, Archived }
public enum EvidenceRequirement { None, Text, Link, Attachment, Multiple, Custom }
public enum ScoringMode { Individual, WholeTeam, ClaimantSelectsBeneficiaries, AttendanceBased }
public enum FormationMode { SelfForm, ManagerAssigned, Either }
public enum SubmissionStatus { Submitted, UnderReview, NeedsEvidence, Resubmitted, Approved, Rejected }
public enum EvidenceKind { Text, Link, Attachment }
public enum XPEntryType { Grant, Reversal, Correction }
public enum XPSourceType { TaskApproval, ManualAward, Raid }
public enum PassType { Physical, Remote }
public enum DeadlineEventType { OverrideSet, OverrideChanged, OverrideCleared }
public enum CycleEventType { Created, StatusChanged, Reopened, CorrectionAuthorised, CorrectionRecorded }
public enum CycleParticipantEventType { Enrolled, StatusChanged }

public sealed class Participant
{
    public Guid Id { get; set; }
    public Guid? EntraObjectId { get; set; }
    public required string DisplayName { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Cycle
{
    public Guid Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public CycleStatus Status { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public string? ThemeConfiguration { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedByParticipantId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class CycleParticipant
{
    public Guid CycleId { get; set; }
    public Guid ParticipantId { get; set; }
    public CycleParticipantStatus Status { get; set; }
    public DateTimeOffset? JoinedAt { get; set; }
    public DateTimeOffset? LeftAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class CycleParticipantEvent : IAppendOnly
{
    public Guid Id { get; set; }
    public Guid CycleId { get; set; }
    public Guid ParticipantId { get; set; }
    public int SequenceNumber { get; set; }
    public CycleParticipantEventType EventType { get; set; }
    public CycleParticipantStatus? FromStatus { get; set; }
    public CycleParticipantStatus ToStatus { get; set; }
    public required string Reason { get; set; }
    public Guid ActorId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}

public sealed class CycleEvent : IAppendOnly
{
    public Guid Id { get; set; }
    public Guid CycleId { get; set; }
    public int SequenceNumber { get; set; }
    public CycleEventType EventType { get; set; }
    public CycleStatus? FromStatus { get; set; }
    public CycleStatus? ToStatus { get; set; }
    public required string Reason { get; set; }
    public Guid ActorId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public Guid? SupersedesEventId { get; set; }
    public Guid? RelatedXPEntryId { get; set; }
    public Guid? CorrelationId { get; set; }
}

public sealed class CycleTeam
{
    public Guid Id { get; set; }
    public Guid CycleId { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class CycleTeamMember
{
    public Guid Id { get; set; }
    public Guid CycleTeamId { get; set; }
    public Guid CycleId { get; set; }
    public Guid ParticipantId { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
    public DateTimeOffset? LeftAt { get; set; }
}

public sealed class Challenge
{
    public Guid Id { get; set; }
    public Guid CycleId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? Category { get; set; }
    public ChallengeStatus Status { get; set; }
    public DateTimeOffset OpenAt { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public DateTimeOffset CloseAt { get; set; }
    public string? HeroImageReference { get; set; }
    public string? GuideCharacter { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedByParticipantId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class ChallengeTask
{
    public Guid Id { get; set; }
    public Guid ChallengeId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public int XP { get; set; }
    public EvidenceRequirement EvidenceRequirement { get; set; }
    public string? CustomEvidenceRequirement { get; set; }
    public ScoringMode ScoringMode { get; set; }
    public int SortOrder { get; set; }
}

public sealed class ChallengeTeamPolicy
{
    public Guid ChallengeId { get; set; }
    public FormationMode FormationMode { get; set; }
    public int MinMembers { get; set; }
    public int MaxMembers { get; set; }
    public bool AllowSolo { get; set; }
    public DateTimeOffset? FormationDeadline { get; set; }
    public bool LockAfterStart { get; set; }
}

public sealed class ChallengeDeadlineChange : IAppendOnly
{
    public Guid Id { get; set; }
    public Guid ChallengeId { get; set; }
    public int SequenceNumber { get; set; }
    public DateTimeOffset PreviousDueAt { get; set; }
    public DateTimeOffset NewDueAt { get; set; }
    public required string Reason { get; set; }
    public Guid ChangedByParticipantId { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public Guid? SupersedesChangeId { get; set; }
}

public sealed class ParticipantChallengeDeadlineEvent : IAppendOnly
{
    public Guid Id { get; set; }
    public Guid ChallengeId { get; set; }
    public Guid ParticipantId { get; set; }
    public int SequenceNumber { get; set; }
    public DeadlineEventType EventType { get; set; }
    public DateTimeOffset? PreviousOverrideDueAt { get; set; }
    public DateTimeOffset? NewOverrideDueAt { get; set; }
    public DateTimeOffset PreviousEffectiveDueAt { get; set; }
    public DateTimeOffset NewEffectiveDueAt { get; set; }
    public required string Reason { get; set; }
    public Guid ActorId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public Guid? SupersedesEventId { get; set; }
}

public sealed class ChallengeParticipation
{
    public Guid Id { get; set; }
    public Guid ChallengeId { get; set; }
    public Guid CycleId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedByParticipantId { get; set; }
}

public sealed class ChallengeParticipationMember
{
    public Guid ChallengeParticipationId { get; set; }
    public Guid ChallengeId { get; set; }
    public Guid CycleId { get; set; }
    public Guid ParticipantId { get; set; }
    public Guid? CycleTeamIdAtParticipation { get; set; }
    public DateTimeOffset JoinedSnapshotAt { get; set; }
}

public sealed class Submission
{
    public Guid Id { get; set; }
    public Guid CycleId { get; set; }
    public Guid ClaimantId { get; set; }
    public Guid ChallengeId { get; set; }
    public Guid TaskId { get; set; }
    public Guid? ChallengeParticipationId { get; set; }
    public string? Comment { get; set; }
    public SubmissionStatus Status { get; set; }
    public string? ReviewerComment { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public DateTimeOffset LastUpdatedAt { get; set; }
}

public sealed class SubmissionBeneficiary
{
    public Guid SubmissionId { get; set; }
    public Guid ParticipantId { get; set; }
    public Guid CycleId { get; set; }
    public DateTimeOffset AddedAt { get; set; }
    public Guid AddedByParticipantId { get; set; }
}

public sealed class SubmissionEvidence
{
    public Guid Id { get; set; }
    public Guid SubmissionId { get; set; }
    public EvidenceKind EvidenceKind { get; set; }
    public string? TextValue { get; set; }
    public string? LinkUrl { get; set; }
    public string? StorageAccount { get; set; }
    public string? Container { get; set; }
    public string? BlobKey { get; set; }
    public string? OriginalFileName { get; set; }
    public string? MimeType { get; set; }
    public long? SizeBytes { get; set; }
    public Guid ProvidedByParticipantId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? Description { get; set; }
}

public sealed class SubmissionEvent : IAppendOnly
{
    public Guid Id { get; set; }
    public Guid SubmissionId { get; set; }
    public required string EventType { get; set; }
    public SubmissionStatus? FromStatus { get; set; }
    public SubmissionStatus? ToStatus { get; set; }
    public string? Comment { get; set; }
    public Guid ActorId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}

public sealed class AwardCategory
{
    public Guid Id { get; set; }
    public Guid? CycleId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class RaidSession
{
    public Guid Id { get; set; }
    public Guid CycleId { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class RaidEntitlement
{
    public Guid ParticipantId { get; set; }
    public Guid CycleId { get; set; }
    public PassType PassType { get; set; }
    public int AssignedCount { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public sealed class RaidParticipation : IAppendOnly
{
    public Guid Id { get; set; }
    public Guid ParticipantId { get; set; }
    public Guid RaidSessionId { get; set; }
    public Guid CycleId { get; set; }
    public PassType PassType { get; set; }
    public DateTimeOffset UsedAt { get; set; }
}

public sealed class XPEntry : IAppendOnly
{
    public Guid Id { get; set; }
    public Guid ParticipantId { get; set; }
    public Guid CycleId { get; set; }
    public int Amount { get; set; }
    public XPEntryType EntryType { get; set; }
    public XPSourceType SourceType { get; set; }
    public Guid? AwardCategoryId { get; set; }
    public Guid? ChallengeId { get; set; }
    public Guid? TaskId { get; set; }
    public Guid? SubmissionId { get; set; }
    public Guid? RaidSessionId { get; set; }
    public Guid? CycleTeamId { get; set; }
    public Guid? ChallengeParticipationId { get; set; }
    public required string Reason { get; set; }
    public Guid AwardedByParticipantId { get; set; }
    public DateTimeOffset AwardedAt { get; set; }
    public Guid? ReversesEntryId { get; set; }
}
