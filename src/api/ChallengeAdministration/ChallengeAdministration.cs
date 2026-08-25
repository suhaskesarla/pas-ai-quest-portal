using Microsoft.EntityFrameworkCore;
using PAS.AIQuestPortal.Api.Authentication;
using PAS.AIQuestPortal.Api.Configuration;
using PAS.AIQuestPortal.Api.Data;
using PAS.AIQuestPortal.Api.Workflow;

namespace PAS.AIQuestPortal.Api.ChallengeAdministration;

public sealed record ChallengeOptionCycle(Guid Id, string Code, string Name, CycleStatus Status, DateTimeOffset StartsAt, DateTimeOffset EndsAt);
public sealed record ChallengeOptionsView(IReadOnlyList<ChallengeOptionCycle> Cycles, IReadOnlyList<ScoringMode> ScoringModes, IReadOnlyList<EvidenceRequirement> EvidenceRequirements, IReadOnlyList<FormationMode> FormationModes);
public sealed record ChallengeTaskWrite(Guid? Id, string Name, string? Description, int XP, ScoringMode ScoringMode, EvidenceRequirement EvidenceRequirement, int SortOrder);
public sealed record ChallengePolicyWrite(FormationMode FormationMode, int MinMembers, int MaxMembers, bool AllowSolo, DateTimeOffset? FormationDeadline, bool LockAfterStart);
public sealed record CreateChallengeRequest(Guid CycleId, string Name, string? Description, string? Category, DateTimeOffset OpenAt, DateTimeOffset DueAt, DateTimeOffset CloseAt, string? HeroImageReference, IReadOnlyList<ChallengeTaskWrite> Tasks, ChallengePolicyWrite? ParticipationPolicy);
public sealed record UpdateChallengeRequest(string Version, Guid CycleId, string Name, string? Description, string? Category, DateTimeOffset OpenAt, DateTimeOffset DueAt, DateTimeOffset CloseAt, string? HeroImageReference, IReadOnlyList<ChallengeTaskWrite> Tasks, ChallengePolicyWrite? ParticipationPolicy);
public sealed record PublishChallengeRequest(string Version);
public sealed record ChallengeTaskView(Guid Id, string Name, string? Description, int XP, ScoringMode ScoringMode, EvidenceRequirement EvidenceRequirement, int SortOrder);
public sealed record ChallengePolicyView(FormationMode FormationMode, int MinMembers, int MaxMembers, bool AllowSolo, DateTimeOffset? FormationDeadline, bool LockAfterStart);
public sealed record ManagerChallengeView(Guid Id, string Version, Guid CycleId, string CycleCode, string CycleName, string Name, string? Description, string? Category, ChallengeStatus Status, DateTimeOffset OpenAt, DateTimeOffset DueAt, DateTimeOffset CloseAt, string? HeroImageReference, IReadOnlyList<ChallengeTaskView> Tasks, ChallengePolicyView? ParticipationPolicy);

public sealed class ChallengeAdministrationService(QuestDbContext db, IQuestCurrentUser currentUser, TimeProvider clock)
{
    private static readonly ScoringMode[] SupportedScoring = [ScoringMode.Individual, ScoringMode.WholeTeam, ScoringMode.ClaimantSelectsBeneficiaries];
    private static readonly EvidenceRequirement[] SupportedEvidence = [EvidenceRequirement.None, EvidenceRequirement.Text, EvidenceRequirement.Link, EvidenceRequirement.Attachment, EvidenceRequirement.Multiple];

    public async Task<ChallengeOptionsView> OptionsAsync(CancellationToken ct)
    {
        Manager();
        var cycles = await db.Cycles.AsNoTracking().OrderByDescending(x => x.StartsAt).Select(x => new ChallengeOptionCycle(x.Id, x.Code, x.Name, x.Status, x.StartsAt, x.EndsAt)).ToListAsync(ct);
        return new(cycles, SupportedScoring, SupportedEvidence, Enum.GetValues<FormationMode>());
    }

    public async Task<IReadOnlyList<ManagerChallengeView>> ListAsync(Guid? cycleId, ChallengeStatus? status, CancellationToken ct)
    {
        Manager(); IQueryable<Challenge> query = db.Challenges.AsNoTracking();
        if (cycleId.HasValue) query = query.Where(x => x.CycleId == cycleId); if (status.HasValue) query = query.Where(x => x.Status == status);
        return await BuildMany(await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Name).ToListAsync(ct), ct);
    }

    public async Task<ManagerChallengeView> GetAsync(Guid id, CancellationToken ct) { Manager(); return await Build(await Find(id, true, ct), ct); }

    public async Task<ManagerChallengeView> CreateAsync(CreateChallengeRequest request, CancellationToken ct)
    {
        Guid manager = Manager(); DateTimeOffset openAt = NormalizeDate(request.OpenAt), dueAt = NormalizeDate(request.DueAt), closeAt = NormalizeDate(request.CloseAt); await ValidateAggregate(request.CycleId, request.Name, openAt, dueAt, closeAt, request.Tasks, request.ParticipationPolicy, false, ct); if (request.Tasks.Any(x => x.Id.HasValue)) throw Validation("New challenge tasks must have null IDs.");
        var challenge = new Challenge { Id = Guid.NewGuid(), CycleId = request.CycleId, Name = request.Name.Trim(), Description = Clean(request.Description), Category = Clean(request.Category), Status = ChallengeStatus.Draft, OpenAt = openAt, DueAt = dueAt, CloseAt = closeAt, HeroImageReference = Clean(request.HeroImageReference), CreatedAt = clock.GetUtcNow(), CreatedByParticipantId = manager };
        db.Challenges.Add(challenge); AddTasks(challenge.Id, request.Tasks); SetPolicy(challenge.Id, request.ParticipationPolicy); await SaveConflict(ct); return await Build(challenge, ct);
    }

    public async Task<ManagerChallengeView> UpdateAsync(Guid id, UpdateChallengeRequest request, CancellationToken ct)
    {
        Manager(); Challenge challenge = await Find(id, false, ct); if (challenge.Status != ChallengeStatus.Draft) throw Conflict("ChallengeNotDraft", "Only Draft challenges can be edited.");
        CheckVersion(challenge, request.Version); if (await HasDependencies(id, ct)) throw Conflict("ChallengeHasOperationalDependencies", "The Draft challenge has operational dependencies.");
        DateTimeOffset openAt = PreserveDate(challenge.OpenAt, request.OpenAt), dueAt = PreserveDate(challenge.DueAt, request.DueAt), closeAt = PreserveDate(challenge.CloseAt, request.CloseAt);
        await ValidateAggregate(request.CycleId, request.Name, openAt, dueAt, closeAt, request.Tasks, request.ParticipationPolicy, false, ct);
        ChallengeTask[] existing = await db.ChallengeTasks.Where(x => x.ChallengeId == id).ToArrayAsync(ct); Guid[] supplied = request.Tasks.Where(x => x.Id.HasValue).Select(x => x.Id!.Value).ToArray();
        if (supplied.Distinct().Count() != supplied.Length) throw Validation("Task IDs must be unique.");
        if (supplied.Any(taskId => existing.All(x => x.Id != taskId))) throw Validation("An existing task ID does not belong to this challenge.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        for (int index = 0; index < existing.Length; index++) existing[index].SortOrder = -(index + 1);
        if (existing.Length > 0) await db.SaveChangesAsync(ct);
        challenge.CycleId = request.CycleId; challenge.Name = request.Name.Trim(); challenge.Description = Clean(request.Description); challenge.Category = Clean(request.Category); challenge.OpenAt = openAt; challenge.DueAt = dueAt; challenge.CloseAt = closeAt; challenge.HeroImageReference = Clean(request.HeroImageReference);
        db.Entry(challenge).Property(x => x.Name).IsModified = true;
        db.ChallengeTasks.RemoveRange(existing.Where(x => !supplied.Contains(x.Id)));
        foreach (ChallengeTaskWrite item in request.Tasks)
        {
            ChallengeTask task = item.Id is Guid taskId ? existing.Single(x => x.Id == taskId) : new ChallengeTask { Id = Guid.NewGuid(), ChallengeId = id, Name = item.Name };
            if (item.Id is null) db.ChallengeTasks.Add(task); Apply(task, item);
        }
        ChallengeTeamPolicy? policy = await db.ChallengeTeamPolicies.SingleOrDefaultAsync(x => x.ChallengeId == id, ct); if (policy is not null && request.ParticipationPolicy is null) db.ChallengeTeamPolicies.Remove(policy); else if (request.ParticipationPolicy is not null) { policy ??= new ChallengeTeamPolicy { ChallengeId = id }; if (db.Entry(policy).State == EntityState.Detached) db.ChallengeTeamPolicies.Add(policy); Apply(policy, request.ParticipationPolicy); }
        await SaveConflict(ct); await transaction.CommitAsync(ct); return await Build(challenge, ct);
    }

    public async Task<ManagerChallengeView> PublishAsync(Guid id, PublishChallengeRequest request, CancellationToken ct)
    {
        Manager(); Challenge challenge = await Find(id, false, ct); if (challenge.Status == ChallengeStatus.Open) return await Build(challenge, ct);
        if (challenge.Status != ChallengeStatus.Draft) throw Conflict("InvalidChallengeTransition", "Only a Draft challenge can be published."); CheckVersion(challenge, request.Version);
        ChallengeTask[] tasks = await db.ChallengeTasks.AsNoTracking().Where(x => x.ChallengeId == id).OrderBy(x => x.SortOrder).ToArrayAsync(ct); ChallengeTeamPolicy? policy = await db.ChallengeTeamPolicies.AsNoTracking().SingleOrDefaultAsync(x => x.ChallengeId == id, ct);
        await ValidateAggregate(challenge.CycleId, challenge.Name, challenge.OpenAt, challenge.DueAt, challenge.CloseAt, tasks.Select(x => new ChallengeTaskWrite(x.Id, x.Name, x.Description, x.XP, x.ScoringMode, x.EvidenceRequirement, x.SortOrder)).ToArray(), policy is null ? null : new(policy.FormationMode, policy.MinMembers, policy.MaxMembers, policy.AllowSolo, policy.FormationDeadline, policy.LockAfterStart), true, ct);
        challenge.Status = ChallengeStatus.Open; await SaveConflict(ct); return await Build(challenge, ct);
    }

    private async Task ValidateAggregate(Guid cycleId, string name, DateTimeOffset openAt, DateTimeOffset dueAt, DateTimeOffset closeAt, IReadOnlyList<ChallengeTaskWrite>? tasks, ChallengePolicyWrite? policy, bool publishing, CancellationToken ct)
    {
        if (!await db.Cycles.AsNoTracking().AnyAsync(x => x.Id == cycleId, ct)) throw Validation("CycleId is invalid."); if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200) throw Validation("Name is required and must not exceed 200 characters."); if (!(openAt < dueAt && dueAt <= closeAt)) throw Validation("Dates must satisfy openAt < dueAt <= closeAt.");
        tasks ??= []; if (publishing && tasks.Count == 0) throw Validation("At least one task is required before publish."); if (tasks.Select(x => x.SortOrder).Distinct().Count() != tasks.Count || !tasks.Select(x => x.SortOrder).Order().SequenceEqual(Enumerable.Range(1, tasks.Count))) throw Validation("Task sortOrder values must be unique and contiguous from 1.");
        foreach (ChallengeTaskWrite task in tasks) { if (string.IsNullOrWhiteSpace(task.Name) || task.Name.Trim().Length > 200 || task.XP < 0) throw Validation("Every task requires a name of at most 200 characters and non-negative XP."); if (!SupportedScoring.Contains(task.ScoringMode)) throw Validation("The task scoring mode is not supported."); if (!SupportedEvidence.Contains(task.EvidenceRequirement)) throw Validation("The task evidence requirement is not supported."); }
        bool needsPolicy = tasks.Any(x => x.ScoringMode != ScoringMode.Individual); if (needsPolicy && policy is null) throw Validation("A participation policy is required for non-Individual tasks."); if (!needsPolicy && policy is not null) throw Validation("An Individual-only challenge must not define a participation policy.");
        if (policy is not null && (policy.MinMembers < 1 || policy.MinMembers > policy.MaxMembers || (policy.AllowSolo && policy.MinMembers != 1) || (!policy.AllowSolo && policy.MinMembers < 2) || !Enum.IsDefined(policy.FormationMode))) throw Validation("The participation policy is invalid.");
    }

    private async Task<bool> HasDependencies(Guid id, CancellationToken ct) => await db.Submissions.AsNoTracking().AnyAsync(x => x.ChallengeId == id, ct) || await db.ChallengeParticipations.AsNoTracking().AnyAsync(x => x.ChallengeId == id, ct) || await db.ParticipantChallengeDeadlineEvents.AsNoTracking().AnyAsync(x => x.ChallengeId == id, ct) || await db.XPEntries.AsNoTracking().AnyAsync(x => x.ChallengeId == id, ct);
    private async Task<Challenge> Find(Guid id, bool noTracking, CancellationToken ct) { IQueryable<Challenge> query = noTracking ? db.Challenges.AsNoTracking() : db.Challenges; return await query.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new WorkflowException(404, "ChallengeNotFound", "The challenge was not found."); }
    private async Task<IReadOnlyList<ManagerChallengeView>> BuildMany(IReadOnlyList<Challenge> challenges, CancellationToken ct) { var result = new List<ManagerChallengeView>(); foreach (Challenge item in challenges) result.Add(await Build(item, ct)); return result; }
    private async Task<ManagerChallengeView> Build(Challenge challenge, CancellationToken ct) { var cycle = await db.Cycles.AsNoTracking().Where(x => x.Id == challenge.CycleId).Select(x => new { x.Code, x.Name }).SingleAsync(ct); ChallengeTaskView[] tasks = await db.ChallengeTasks.AsNoTracking().Where(x => x.ChallengeId == challenge.Id).OrderBy(x => x.SortOrder).Select(x => new ChallengeTaskView(x.Id, x.Name, x.Description, x.XP, x.ScoringMode, x.EvidenceRequirement, x.SortOrder)).ToArrayAsync(ct); ChallengePolicyView? policy = await db.ChallengeTeamPolicies.AsNoTracking().Where(x => x.ChallengeId == challenge.Id).Select(x => new ChallengePolicyView(x.FormationMode, x.MinMembers, x.MaxMembers, x.AllowSolo, x.FormationDeadline, x.LockAfterStart)).SingleOrDefaultAsync(ct); return new(challenge.Id, Convert.ToBase64String(challenge.RowVersion), challenge.CycleId, cycle.Code, cycle.Name, challenge.Name, challenge.Description, challenge.Category, challenge.Status, challenge.OpenAt, challenge.DueAt, challenge.CloseAt, challenge.HeroImageReference, tasks, policy); }
    private void AddTasks(Guid challengeId, IReadOnlyList<ChallengeTaskWrite> tasks) { foreach (ChallengeTaskWrite item in tasks) { var task = new ChallengeTask { Id = item.Id ?? Guid.NewGuid(), ChallengeId = challengeId, Name = item.Name }; Apply(task, item); db.ChallengeTasks.Add(task); } }
    private void SetPolicy(Guid challengeId, ChallengePolicyWrite? item) { if (item is null) return; var policy = new ChallengeTeamPolicy { ChallengeId = challengeId }; Apply(policy, item); db.ChallengeTeamPolicies.Add(policy); }
    private static void Apply(ChallengeTask task, ChallengeTaskWrite item) { task.Name = item.Name.Trim(); task.Description = Clean(item.Description); task.XP = item.XP; task.ScoringMode = item.ScoringMode; task.EvidenceRequirement = item.EvidenceRequirement; task.CustomEvidenceRequirement = null; task.SortOrder = item.SortOrder; }
    private static void Apply(ChallengeTeamPolicy policy, ChallengePolicyWrite item) { policy.FormationMode = item.FormationMode; policy.MinMembers = item.MinMembers; policy.MaxMembers = item.MaxMembers; policy.AllowSolo = item.AllowSolo; policy.FormationDeadline = item.FormationDeadline; policy.LockAfterStart = item.LockAfterStart; }
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static DateTimeOffset NormalizeDate(DateTimeOffset value) { long ticks = value.UtcTicks - value.UtcTicks % TimeSpan.TicksPerMillisecond; return new DateTimeOffset(ticks, TimeSpan.Zero); }
    private static DateTimeOffset PreserveDate(DateTimeOffset persisted, DateTimeOffset incoming) => NormalizeDate(persisted) == NormalizeDate(incoming) ? persisted : NormalizeDate(incoming);
    private Guid Manager() => currentUser.Identity is { IsAuthenticated: true, ParticipantId: Guid id } identity && identity.Roles.Contains(QuestRoles.Manager, StringComparer.Ordinal) ? id : throw new WorkflowException(403, "Forbidden", "Manager authorization is required.");
    private static void CheckVersion(Challenge challenge, string version) { byte[] supplied; try { supplied = Convert.FromBase64String(version); } catch (FormatException) { throw Validation("Version must be valid base64."); } if (!challenge.RowVersion.SequenceEqual(supplied)) throw Conflict("ChallengeVersionConflict", "The challenge was changed by another manager."); }
    private async Task SaveConflict(CancellationToken ct) { try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { throw Conflict("ChallengeVersionConflict", "The challenge was changed by another manager."); } }
    private static WorkflowException Validation(string message) => new(400, "ChallengeValidationFailed", message);
    private static WorkflowException Conflict(string code, string message) => new(409, code, message);
}
