using System.Data;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PAS.AIQuestPortal.Api.Authentication;
using PAS.AIQuestPortal.Api.Configuration;
using PAS.AIQuestPortal.Api.Data;

namespace PAS.AIQuestPortal.Api.Workflow;

public sealed record PersonView(Guid ParticipantId, string DisplayName);
public sealed record EvidenceInputView(EvidenceKind Kind, string Label, bool Required, string? Instructions = null);
public sealed record EvidenceItem(EvidenceKind Kind, string Label, string Value, Guid? Id = null);
public sealed record ParticipationOptionView(Guid ParticipationId, IReadOnlyList<PersonView> Members, bool ClaimantIsMember, bool RequiresCompleteParticipation, bool AllowsBeneficiarySubset);
public sealed record TaskView(Guid Id, string Name, int XP, ScoringMode ScoringMode, IReadOnlyList<EvidenceInputView> EvidenceInputs, IReadOnlyList<ParticipationOptionView> Participations);
public sealed record EligibleChallengeView(Guid Id, string Name, string Description, string Category, ChallengeStatus Status, DateTimeOffset OpenAt, DateTimeOffset DueAt, DateTimeOffset CloseAt, DateTimeOffset EffectiveDeadline, bool IsEligible, string? IneligibilityReason, IReadOnlyList<TaskView> Tasks);
public sealed record SubmissionHistoryView(SubmissionStatus EventType, string? Comment, string ActorDisplayName, DateTimeOffset OccurredAt);
public sealed record SubmissionView(Guid Id, string Version, SubmissionStatus Status, PersonView Claimant, IReadOnlyList<PersonView> Beneficiaries, Guid ChallengeId, string ChallengeName, Guid TaskId, string TaskName, [property: JsonPropertyName("taskXp")] int TaskXP, IReadOnlyList<EvidenceItem> Evidence, string? ParticipantComment, string? ManagerComment, DateTimeOffset SubmittedAt, DateTimeOffset LastUpdatedAt, IReadOnlyList<SubmissionHistoryView> History);
public sealed record CreateSubmissionRequest(Guid ChallengeId, Guid TaskId, Guid? ChallengeParticipationId, IReadOnlyList<Guid> BeneficiaryIds, IReadOnlyList<EvidenceItem> Evidence, string? Comment);
public sealed record ResubmitRequest(string Version, IReadOnlyList<EvidenceItem> Evidence, string? Comment);
public enum ReviewAction { NeedsEvidence, Approve, Reject }
public sealed record ReviewRequest(string Version, ReviewAction Action, string? Comment);
public sealed record CorrectionRequest(int NewAmount, string Reason);
public sealed record CorrectionView(Guid Id, Guid OriginalEntryId, Guid ParticipantId, Guid CycleId, int Amount, XPEntryType EntryType, string Reason, Guid AwardedByParticipantId, DateTimeOffset AwardedAt);

public sealed class WorkflowException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

public sealed class SubmissionWorkflowService(QuestDbContext db, IQuestCurrentUser user, TimeProvider clock)
{
    public async Task<IReadOnlyList<EligibleChallengeView>> EligibleAsync(CancellationToken ct)
    {
        Guid participant = Participant(); DateTimeOffset now = clock.GetUtcNow();
        Guid[] cycles = await db.CycleParticipants.AsNoTracking().Where(x => x.ParticipantId == participant && x.Status == CycleParticipantStatus.Active).Select(x => x.CycleId).ToArrayAsync(ct);
        List<Challenge> challenges = await db.Challenges.AsNoTracking().Where(x => cycles.Contains(x.CycleId) && x.Status == ChallengeStatus.Open && x.OpenAt <= now).OrderBy(x => x.DueAt).ToListAsync(ct);
        var result = new List<EligibleChallengeView>();
        foreach (Challenge challenge in challenges)
        {
            DateTimeOffset deadline = await EffectiveDeadline(challenge, participant, ct);
            if (now > deadline) continue;
            List<ChallengeTask> tasks = await db.ChallengeTasks.AsNoTracking().Where(x => x.ChallengeId == challenge.Id && x.XP > 0 && x.ScoringMode != ScoringMode.AttendanceBased && x.EvidenceRequirement != EvidenceRequirement.Attachment).OrderBy(x => x.SortOrder).ToListAsync(ct);
            var views = new List<TaskView>();
            foreach (ChallengeTask task in tasks)
            {
                IReadOnlyList<ParticipationOptionView> participations = await ParticipationOptions(challenge, task, participant, ct);
                if (task.ScoringMode == ScoringMode.Individual || participations.Count > 0) views.Add(new(task.Id, task.Name, task.XP, task.ScoringMode, EvidenceInputs(task), participations));
            }
            if (views.Count > 0) result.Add(new(challenge.Id, challenge.Name, challenge.Description, challenge.Category, challenge.Status, challenge.OpenAt, challenge.DueAt, challenge.CloseAt, deadline, true, null, views));
        }
        return result;
    }

    public async Task<IReadOnlyList<SubmissionView>> MineAsync(CancellationToken ct) => await BuildMany(await db.Submissions.AsNoTracking().Where(x => x.ClaimantId == Participant()).OrderByDescending(x => x.LastUpdatedAt).ToListAsync(ct), ct);
    public async Task<IReadOnlyList<SubmissionView>> QueueAsync(CancellationToken ct)
    {
        Manager();
        return await BuildMany(await db.Submissions.AsNoTracking().Where(x => x.Status == SubmissionStatus.Submitted || x.Status == SubmissionStatus.Resubmitted || x.Status == SubmissionStatus.UnderReview).OrderBy(x => x.SubmittedAt).ToListAsync(ct), ct);
    }

    public async Task<SubmissionView> CreateAsync(CreateSubmissionRequest request, CancellationToken ct)
    {
        Guid claimant = Participant(); DateTimeOffset now = clock.GetUtcNow();
        ChallengeTask task = await db.ChallengeTasks.SingleOrDefaultAsync(x => x.Id == request.TaskId && x.ChallengeId == request.ChallengeId, ct) ?? throw Bad("InvalidChallengeTask", "The task does not belong to the selected challenge.");
        if (task.XP <= 0) throw Bad("TaskNotSubmittable", "Participant submissions require a positive-XP task.");
        Challenge challenge = await db.Challenges.SingleAsync(x => x.Id == request.ChallengeId, ct);
        await ValidateWindow(challenge, claimant, now, ct);
        Guid[] beneficiaries = Unique(request.BeneficiaryIds);
        Guid? participation = await ValidateBeneficiaries(challenge, task, claimant, request.ChallengeParticipationId, beneficiaries, ct);
        ValidateEvidence(task, request.Evidence);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var submission = new Submission { Id = Guid.NewGuid(), CycleId = challenge.CycleId, ClaimantId = claimant, ChallengeId = challenge.Id, TaskId = task.Id, ChallengeParticipationId = participation, Comment = Clean(request.Comment), Status = SubmissionStatus.Submitted, SubmittedAt = now, LastUpdatedAt = now };
        db.Submissions.Add(submission);
        db.SubmissionBeneficiaries.AddRange(beneficiaries.Select(x => new SubmissionBeneficiary { SubmissionId = submission.Id, ParticipantId = x, CycleId = challenge.CycleId, AddedAt = now, AddedByParticipantId = claimant }));
        AddEvidence(submission.Id, claimant, now, request.Evidence); AddEvent(submission.Id, "Submitted", null, SubmissionStatus.Submitted, request.Comment, claimant, now);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return await Build(submission, ct);
    }

    public async Task<SubmissionView> ResubmitAsync(Guid id, ResubmitRequest request, CancellationToken ct)
    {
        Guid claimant = Participant(); DateTimeOffset now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        Submission submission = await Find(id, ct);
        if (submission.ClaimantId != claimant) throw Forbidden();
        if (submission.Status != SubmissionStatus.NeedsEvidence) throw Conflict("InvalidSubmissionState", "Only a NeedsEvidence submission can be resubmitted.");
        CheckVersion(submission, request.Version);
        Challenge challenge = await db.Challenges.SingleAsync(x => x.Id == submission.ChallengeId, ct); ChallengeTask task = await db.ChallengeTasks.SingleAsync(x => x.Id == submission.TaskId, ct);
        await ValidateWindow(challenge, claimant, now, ct); ValidateEvidence(task, request.Evidence);
        submission.Comment = Clean(request.Comment); submission.Status = SubmissionStatus.Resubmitted; submission.LastUpdatedAt = now;
        SubmissionEvidence[] previousEvidence = await db.SubmissionEvidence.Where(x => x.SubmissionId == id).ToArrayAsync(ct);
        db.SubmissionEvidence.RemoveRange(previousEvidence);
        AddEvidence(id, claimant, now, request.Evidence); AddEvent(id, "Resubmitted", SubmissionStatus.NeedsEvidence, SubmissionStatus.Resubmitted, request.Comment, claimant, now);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return await Build(submission, ct);
    }

    public async Task<SubmissionView> ReviewAsync(Guid id, ReviewRequest request, CancellationToken ct)
    {
        Guid manager = Manager(); DateTimeOffset now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        Submission submission = await Find(id, ct);
        if (request.Action == ReviewAction.Approve && submission.Status == SubmissionStatus.Approved) { await tx.CommitAsync(ct); return await Build(submission, ct); }
        CheckVersion(submission, request.Version);
        if (submission.Status is not (SubmissionStatus.Submitted or SubmissionStatus.Resubmitted or SubmissionStatus.UnderReview)) throw Conflict("InvalidSubmissionState", "This submission is not available for review.");
        if (request.Action is ReviewAction.NeedsEvidence or ReviewAction.Reject && string.IsNullOrWhiteSpace(request.Comment)) throw Bad("ManagerCommentRequired", "A manager comment is required.");
        if (submission.Status != SubmissionStatus.UnderReview) { SubmissionStatus from = submission.Status; submission.Status = SubmissionStatus.UnderReview; AddEvent(id, "UnderReview", from, SubmissionStatus.UnderReview, null, manager, now); }
        if (request.Action == ReviewAction.NeedsEvidence) { submission.Status = SubmissionStatus.NeedsEvidence; AddEvent(id, "NeedsEvidence", SubmissionStatus.UnderReview, SubmissionStatus.NeedsEvidence, request.Comment, manager, now); }
        else if (request.Action == ReviewAction.Reject) { submission.Status = SubmissionStatus.Rejected; AddEvent(id, "Rejected", SubmissionStatus.UnderReview, SubmissionStatus.Rejected, request.Comment, manager, now); }
        else { await AddGrants(submission, manager, now, ct); submission.Status = SubmissionStatus.Approved; AddEvent(id, "Approved", SubmissionStatus.UnderReview, SubmissionStatus.Approved, request.Comment, manager, now); }
        submission.ReviewerComment = Clean(request.Comment); submission.LastUpdatedAt = now;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return await Build(submission, ct);
    }

    public async Task<CorrectionView> CorrectAsync(Guid entryId, CorrectionRequest request, CancellationToken ct)
    {
        Guid manager = Manager();
        if (request.NewAmount < 0) throw Bad("InvalidCorrectionAmount", "NewAmount cannot be negative.");
        if (string.IsNullOrWhiteSpace(request.Reason)) throw Bad("CorrectionReasonRequired", "A correction reason is required.");
        DateTimeOffset now = clock.GetUtcNow(); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        XPEntry original = await db.XPEntries.SingleOrDefaultAsync(x => x.Id == entryId && x.EntryType == XPEntryType.Grant && x.SourceType == XPSourceType.TaskApproval, ct) ?? throw NotFound("XPEntryNotFound", "The TaskApproval grant was not found.");
        int adjustments = await db.XPEntries.Where(x => x.ReversesEntryId == original.Id).SumAsync(x => (int?)x.Amount, ct) ?? 0;
        int delta = request.NewAmount - original.Amount - adjustments;
        if (delta == 0) throw Conflict("CorrectionNoChange", "The effective award already has that value.");
        var entry = new XPEntry { Id = Guid.NewGuid(), ParticipantId = original.ParticipantId, CycleId = original.CycleId, Amount = delta, EntryType = delta < 0 ? XPEntryType.Reversal : XPEntryType.Correction, SourceType = XPSourceType.TaskApproval, ChallengeId = original.ChallengeId, TaskId = original.TaskId, SubmissionId = original.SubmissionId, ChallengeParticipationId = original.ChallengeParticipationId, Reason = request.Reason.Trim(), AwardedByParticipantId = manager, AwardedAt = now, ReversesEntryId = original.Id };
        db.XPEntries.Add(entry);
        int sequence = (await db.CycleEvents.Where(x => x.CycleId == original.CycleId).MaxAsync(x => (int?)x.SequenceNumber, ct) ?? 0) + 1;
        db.CycleEvents.Add(new CycleEvent { Id = Guid.NewGuid(), CycleId = original.CycleId, SequenceNumber = sequence, EventType = CycleEventType.CorrectionRecorded, Reason = entry.Reason, ActorId = manager, OccurredAt = now, RelatedXPEntryId = entry.Id, CorrelationId = Guid.NewGuid() });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return new(entry.Id, original.Id, entry.ParticipantId, entry.CycleId, entry.Amount, entry.EntryType, entry.Reason, manager, now);
    }

    private async Task AddGrants(Submission submission, Guid manager, DateTimeOffset now, CancellationToken ct)
    {
        ChallengeTask task = await db.ChallengeTasks.SingleAsync(x => x.Id == submission.TaskId, ct);
        ValidateEvidence(task, (await db.SubmissionEvidence.AsNoTracking().Where(x => x.SubmissionId == submission.Id).ToListAsync(ct)).Select(ToItem).ToArray());
        Guid[] beneficiaries = await db.SubmissionBeneficiaries.Where(x => x.SubmissionId == submission.Id).Select(x => x.ParticipantId).ToArrayAsync(ct);
        if (beneficiaries.Length == 0) throw Conflict("MissingBeneficiaries", "The submission has no beneficiaries.");
        foreach (Guid participant in beneficiaries)
        {
            bool exists = await db.XPEntries.AnyAsync(x => x.SubmissionId == submission.Id && x.ParticipantId == participant && x.EntryType == XPEntryType.Grant && x.SourceType == XPSourceType.TaskApproval, ct);
            if (!exists) db.XPEntries.Add(new XPEntry { Id = Guid.NewGuid(), ParticipantId = participant, CycleId = submission.CycleId, Amount = task.XP, EntryType = XPEntryType.Grant, SourceType = XPSourceType.TaskApproval, ChallengeId = submission.ChallengeId, TaskId = submission.TaskId, SubmissionId = submission.Id, ChallengeParticipationId = submission.ChallengeParticipationId, Reason = "Task submission approved", AwardedByParticipantId = manager, AwardedAt = now });
        }
    }

    private async Task<Guid?> ValidateBeneficiaries(Challenge challenge, ChallengeTask task, Guid claimant, Guid? participationId, Guid[] beneficiaries, CancellationToken ct)
    {
        int active = await db.CycleParticipants.CountAsync(x => x.CycleId == challenge.CycleId && beneficiaries.Contains(x.ParticipantId) && x.Status == CycleParticipantStatus.Active, ct);
        if (active != beneficiaries.Length) throw Bad("IneligibleBeneficiary", "Every beneficiary must be active in the challenge cycle.");
        if (!beneficiaries.Contains(claimant)) throw Bad("ClaimantMustBenefit", "The authenticated claimant must be a beneficiary.");
        if (task.ScoringMode == ScoringMode.Individual) { if (participationId is not null || beneficiaries.Length != 1) throw Bad("InvalidIndividualBeneficiaries", "Individual tasks only benefit the claimant and do not use challenge participation."); return null; }
        if (task.ScoringMode == ScoringMode.AttendanceBased) throw Bad("AttendanceTaskNotSubmittable", "Attendance tasks are manager-recorded.");
        if (participationId is null) throw Bad("ParticipationRequired", "A challenge participation is required for this task.");
        ChallengeTeamPolicy policy = await db.ChallengeTeamPolicies.AsNoTracking().SingleOrDefaultAsync(x => x.ChallengeId == challenge.Id, ct) ?? throw Bad("MissingTeamPolicy", "The challenge has no team policy.");
        ChallengeParticipation exists = await db.ChallengeParticipations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == participationId && x.ChallengeId == challenge.Id && x.CycleId == challenge.CycleId, ct) ?? throw Bad("InvalidParticipation", "The selected participation does not belong to this challenge.");
        Guid[] members = await db.ChallengeParticipationMembers.AsNoTracking().Where(x => x.ChallengeParticipationId == exists.Id).Select(x => x.ParticipantId).ToArrayAsync(ct);
        int min = policy.AllowSolo && members.Length == 1 ? 1 : policy.MinMembers;
        bool claimantMember = members.Contains(claimant);
        bool match = task.ScoringMode == ScoringMode.WholeTeam ? members.Order().SequenceEqual(beneficiaries.Order()) : beneficiaries.All(members.Contains);
        if (claimantMember && members.Length >= min && members.Length <= policy.MaxMembers && match) return exists.Id;
        throw Bad("InvalidParticipationBeneficiaries", "The beneficiaries do not match an allowed challenge participation.");
    }

    private async Task<IReadOnlyList<ParticipationOptionView>> ParticipationOptions(Challenge challenge, ChallengeTask task, Guid claimant, CancellationToken ct)
    {
        if (task.ScoringMode == ScoringMode.Individual) return [];
        ChallengeTeamPolicy? policy = await db.ChallengeTeamPolicies.AsNoTracking().SingleOrDefaultAsync(x => x.ChallengeId == challenge.Id, ct);
        if (policy is null) return [];
        Guid[] participations = await db.ChallengeParticipationMembers.AsNoTracking().Where(x => x.ChallengeId == challenge.Id && x.ParticipantId == claimant).Select(x => x.ChallengeParticipationId).ToArrayAsync(ct);
        var result = new List<ParticipationOptionView>();
        foreach (Guid participation in participations)
        {
            Guid[] candidate = await db.ChallengeParticipationMembers.AsNoTracking().Where(x => x.ChallengeParticipationId == participation).Select(x => x.ParticipantId).ToArrayAsync(ct);
            int minimum = policy.AllowSolo && candidate.Length == 1 ? 1 : policy.MinMembers;
            if (candidate.Length < minimum || candidate.Length > policy.MaxMembers) continue;
            PersonView[] members = await db.CycleParticipants.AsNoTracking().Where(x => x.CycleId == challenge.CycleId && candidate.Contains(x.ParticipantId) && x.Status == CycleParticipantStatus.Active)
                .Join(db.Participants, x => x.ParticipantId, x => x.Id, (_, p) => p).OrderBy(x => x.DisplayName).Select(x => new PersonView(x.Id, x.DisplayName)).ToArrayAsync(ct);
            if (members.Length != candidate.Length) continue;
            result.Add(new(participation, members, true, task.ScoringMode == ScoringMode.WholeTeam, task.ScoringMode == ScoringMode.ClaimantSelectsBeneficiaries));
        }
        return result.OrderBy(x => x.ParticipationId).ToArray();
    }

    private async Task ValidateWindow(Challenge challenge, Guid participant, DateTimeOffset now, CancellationToken ct)
    {
        if (!await db.CycleParticipants.AnyAsync(x => x.CycleId == challenge.CycleId && x.ParticipantId == participant && x.Status == CycleParticipantStatus.Active, ct)) throw Forbidden("ParticipantNotActive", "Only active cycle participants may submit.");
        if (challenge.Status != ChallengeStatus.Open || now < challenge.OpenAt || now > await EffectiveDeadline(challenge, participant, ct)) throw Conflict("SubmissionOutsideEligibilityWindow", "The challenge is outside this participant's submission window.");
    }

    private async Task<DateTimeOffset> EffectiveDeadline(Challenge challenge, Guid participant, CancellationToken ct)
    {
        DateTimeOffset? value = await db.ParticipantChallengeDeadlineEvents.AsNoTracking().Where(x => x.ChallengeId == challenge.Id && x.ParticipantId == participant).OrderByDescending(x => x.SequenceNumber).Select(x => x.NewOverrideDueAt).FirstOrDefaultAsync(ct);
        DateTimeOffset due = value > challenge.DueAt ? value.Value : challenge.DueAt, close = value > challenge.CloseAt ? value.Value : challenge.CloseAt;
        return due < close ? due : close;
    }

    private static IReadOnlyList<EvidenceInputView> EvidenceInputs(ChallengeTask task) => task.EvidenceRequirement switch { EvidenceRequirement.None => [], EvidenceRequirement.Text => [new(EvidenceKind.Text, "Evidence", true)], EvidenceRequirement.Link => [new(EvidenceKind.Link, "Evidence link", true)], EvidenceRequirement.Multiple => [new(EvidenceKind.Text, "Evidence summary", true), new(EvidenceKind.Link, "Evidence link", true)], EvidenceRequirement.Custom => [new(EvidenceKind.Text, "Evidence", true, task.CustomEvidenceRequirement)], _ => [] };
    private static void ValidateEvidence(ChallengeTask task, IReadOnlyList<EvidenceItem> evidence)
    {
        if (evidence.Any(x => string.IsNullOrWhiteSpace(x.Label) || string.IsNullOrWhiteSpace(x.Value))) throw Bad("InvalidEvidence", "Evidence labels and values are required.");
        if (evidence.Any(x => x.Kind == EvidenceKind.Attachment)) throw Conflict("AttachmentEvidenceDeferred", "Secure attachment evidence belongs to Step 7.");
        if (evidence.Any(x => x.Kind == EvidenceKind.Link && (!Uri.TryCreate(x.Value, UriKind.Absolute, out Uri? uri) || uri.Scheme is not ("http" or "https")))) throw Bad("InvalidEvidence", "Evidence links must be absolute HTTP or HTTPS URLs.");
        bool valid = task.EvidenceRequirement switch { EvidenceRequirement.None => true, EvidenceRequirement.Text or EvidenceRequirement.Custom => evidence.Any(x => x.Kind == EvidenceKind.Text), EvidenceRequirement.Link => evidence.Any(x => x.Kind == EvidenceKind.Link), EvidenceRequirement.Multiple => evidence.Count >= 2, _ => false };
        if (!valid) throw Bad("EvidenceRequired", "The evidence requirement is not satisfied.");
    }

    private void AddEvidence(Guid submission, Guid actor, DateTimeOffset now, IEnumerable<EvidenceItem> evidence) { foreach (EvidenceItem x in evidence) db.SubmissionEvidence.Add(new SubmissionEvidence { Id = Guid.NewGuid(), SubmissionId = submission, EvidenceKind = x.Kind, TextValue = x.Kind == EvidenceKind.Text ? x.Value.Trim() : null, LinkUrl = x.Kind == EvidenceKind.Link ? x.Value.Trim() : null, Description = x.Label.Trim(), ProvidedByParticipantId = actor, CreatedAt = now }); }
    private void AddEvent(Guid submission, string type, SubmissionStatus? from, SubmissionStatus to, string? comment, Guid actor, DateTimeOffset now) => db.SubmissionEvents.Add(new SubmissionEvent { Id = Guid.NewGuid(), SubmissionId = submission, EventType = type, FromStatus = from, ToStatus = to, Comment = Clean(comment), ActorId = actor, OccurredAt = now });
    private static EvidenceItem ToItem(SubmissionEvidence x) => new(x.EvidenceKind, x.Description ?? "Evidence", x.TextValue ?? x.LinkUrl ?? x.BlobKey ?? string.Empty, x.Id);
    private async Task<IReadOnlyList<SubmissionView>> BuildMany(IEnumerable<Submission> submissions, CancellationToken ct) { var result = new List<SubmissionView>(); foreach (Submission s in submissions) result.Add(await Build(s, ct)); return result; }
    private async Task<SubmissionView> Build(Submission s, CancellationToken ct)
    {
        Challenge challenge = await db.Challenges.AsNoTracking().SingleAsync(x => x.Id == s.ChallengeId, ct); ChallengeTask task = await db.ChallengeTasks.AsNoTracking().SingleAsync(x => x.Id == s.TaskId, ct);
        PersonView claimant = await db.Participants.AsNoTracking().Where(x => x.Id == s.ClaimantId).Select(x => new PersonView(x.Id, x.DisplayName)).SingleAsync(ct);
        PersonView[] beneficiaries = await db.SubmissionBeneficiaries.AsNoTracking().Where(x => x.SubmissionId == s.Id).Join(db.Participants, x => x.ParticipantId, x => x.Id, (_, p) => p).OrderBy(x => x.DisplayName).Select(x => new PersonView(x.Id, x.DisplayName)).ToArrayAsync(ct);
        EvidenceItem[] evidence = (await db.SubmissionEvidence.AsNoTracking().Where(x => x.SubmissionId == s.Id).OrderBy(x => x.CreatedAt).ToListAsync(ct)).Select(ToItem).ToArray();
        var events = await db.SubmissionEvents.AsNoTracking().Where(x => x.SubmissionId == s.Id).Join(db.Participants, e => e.ActorId, p => p.Id, (e, p) => new { e.ToStatus, e.EventType, e.Comment, p.DisplayName, e.OccurredAt }).ToArrayAsync(ct);
        SubmissionHistoryView[] history = events.Select(x => new SubmissionHistoryView(x.ToStatus ?? Enum.Parse<SubmissionStatus>(x.EventType), x.Comment, x.DisplayName, x.OccurredAt)).OrderBy(x => x.OccurredAt).ThenBy(x => EventOrder(x.EventType)).ToArray();
        Submission current = await db.Submissions.AsNoTracking().SingleAsync(x => x.Id == s.Id, ct);
        return new(current.Id, Version(current), current.Status, claimant, beneficiaries, challenge.Id, challenge.Name, task.Id, task.Name, task.XP, evidence, current.Comment, current.ReviewerComment, current.SubmittedAt, current.LastUpdatedAt, history);
    }

    private async Task<Submission> Find(Guid id, CancellationToken ct) => await db.Submissions.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound("SubmissionNotFound", "Submission was not found.");
    private Guid Participant() => user.Identity.ParticipantId ?? throw new WorkflowException(401, "Unauthenticated", "Authentication is required.");
    private Guid Manager() { Guid id = Participant(); if (!user.Identity.Roles.Contains(QuestRoles.Manager, StringComparer.Ordinal)) throw Forbidden(); return id; }
    private static Guid[] Unique(IReadOnlyList<Guid> ids) { Guid[] result = ids.Distinct().ToArray(); if (result.Length == 0 || result.Length != ids.Count || result.Contains(Guid.Empty)) throw Bad("InvalidBeneficiaries", "At least one unique beneficiary is required."); return result; }
    private static string Version(Submission s) => s.LastUpdatedAt.ToUniversalTime().ToString("O");
    private static int EventOrder(SubmissionStatus status) => status switch { SubmissionStatus.Submitted => 0, SubmissionStatus.UnderReview => 1, SubmissionStatus.NeedsEvidence => 2, SubmissionStatus.Resubmitted => 3, SubmissionStatus.Approved => 4, SubmissionStatus.Rejected => 4, _ => 9 };
    private static void CheckVersion(Submission s, string value) { if (!DateTimeOffset.TryParse(value, out DateTimeOffset parsed) || parsed != s.LastUpdatedAt) throw Conflict("SubmissionVersionConflict", "The submission changed; refresh and retry."); }
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static WorkflowException Bad(string code, string message) => new(400, code, message);
    private static WorkflowException Forbidden(string code = "Forbidden", string message = "This identity cannot perform this action.") => new(403, code, message);
    private static WorkflowException NotFound(string code, string message) => new(404, code, message);
    private static WorkflowException Conflict(string code, string message) => new(409, code, message);
}
