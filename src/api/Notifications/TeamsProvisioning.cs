using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using PAS.AIQuestPortal.Api.Authentication;
using PAS.AIQuestPortal.Api.Configuration;
using PAS.AIQuestPortal.Api.Data;
using PAS.AIQuestPortal.Api.Workflow;

namespace PAS.AIQuestPortal.Api.Notifications;

public sealed record ProvisionTeamsExternalIdentityRequest(Guid ParticipantId, Guid TenantId, Guid Oid);
public sealed record ProvisionTeamsExternalIdentityResponse(Guid ParticipantId, Guid TenantId, Guid Oid, DateTimeOffset VerifiedAt, bool Replay);
public sealed record AssignTeamsDestinationRequest(Guid CandidateId);
public sealed record TeamsDestinationCandidateResponse(Guid Id, Guid TenantId, string ServiceUrl, string ConversationId, string? TeamId, string? ChannelId, DateTimeOffset UpdatedAt);
public sealed record TeamsDestinationAssignmentResponse(string DestinationKey, TeamsDestinationCandidateResponse Destination, Guid AssignedByParticipantId, DateTimeOffset AssignedAt, bool Replay);
public interface ITeamsIdentityProvisioningConcurrencyHook
{
    Task BeforeLocksAsync(CancellationToken ct);
    Task AfterLocksAsync(CancellationToken ct);
}

public sealed class TeamsProvisioningService(QuestDbContext db, IQuestCurrentUser currentUser, IOptions<NotificationOptions> options,
    TimeProvider clock, ILogger<TeamsProvisioningService> logger, ITeamsIdentityProvisioningConcurrencyHook? concurrencyHook = null)
{
    public async Task<ProvisionTeamsExternalIdentityResponse> ProvisionIdentityAsync(ProvisionTeamsExternalIdentityRequest request, CancellationToken ct)
    {
        Guid actor = Manager();
        Guid configuredTenant = ConfiguredTenant();
        if (request.TenantId != configuredTenant) throw Bad("TeamsTenantInvalid", "The tenant is not the configured Teams tenant.");
        if (request.ParticipantId == Guid.Empty || request.Oid == Guid.Empty) throw Bad("TeamsIdentityInvalid", "ParticipantId and oid are required.");
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        try
        {
            if (concurrencyHook is not null) await concurrencyHook.BeforeLocksAsync(ct);
            string[] resources =
            [
                $"quest-teams-external-identity-participant:{configuredTenant:N}:{request.ParticipantId:N}",
                $"quest-teams-external-identity-subject:{configuredTenant:N}:{request.Oid:N}"
            ];
            foreach (string resource in resources.Order(StringComparer.Ordinal)) await AcquireLock(resource, transaction, ct);
            if (concurrencyHook is not null) await concurrencyHook.AfterLocksAsync(ct);

            if (!await db.Participants.AsNoTracking().AnyAsync(x => x.Id == request.ParticipantId, ct)) throw NotFound("TeamsParticipantNotFound", "The participant was not found.");
            ProvisionTeamsExternalIdentityResponse? existing = await ClassifyExistingAsync(request, configuredTenant, ct);
            if (existing is not null) { await transaction.CommitAsync(ct); return existing; }

            DateTimeOffset now = clock.GetUtcNow();
            db.ParticipantExternalIdentities.Add(new ParticipantExternalIdentity { Id = Guid.NewGuid(), ParticipantId = request.ParticipantId,
                Provider = "Entra", TenantId = configuredTenant, SubjectId = request.Oid, CreatedAt = now, VerifiedAt = now });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            logger.LogInformation("Manager {ActorId} provisioned verified Teams external identity for participant {ParticipantId}.", actor, request.ParticipantId);
            return new(request.ParticipantId, configuredTenant, request.Oid, now, false);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            await transaction.RollbackAsync(CancellationToken.None); db.ChangeTracker.Clear();
            return await ClassifyExistingAsync(request, configuredTenant, ct)
                ?? throw new WorkflowException(409, "TeamsExternalIdentityConflict", "The external identity was concurrently provisioned differently.");
        }
    }

    public async Task<IReadOnlyList<TeamsDestinationCandidateResponse>> CandidatesAsync(CancellationToken ct)
    {
        _ = Manager(); Guid tenant = ConfiguredTenant();
        return await db.TeamsChannelDestinationCandidates.AsNoTracking().Where(x => x.TenantId == tenant).OrderByDescending(x => x.UpdatedAt)
            .Select(x => new TeamsDestinationCandidateResponse(x.Id, x.TenantId, x.ServiceUrl, x.ConversationId, x.TeamId, x.ChannelId, x.UpdatedAt)).ToListAsync(ct);
    }

    public async Task<TeamsDestinationAssignmentResponse> AssignAsync(string destinationKey, AssignTeamsDestinationRequest request, CancellationToken ct)
    {
        Guid actor = Manager(); Guid tenant = ConfiguredTenant();
        if (destinationKey is not (NotificationDestinations.QuestGeneralAudience or NotificationDestinations.QuestManagerAudience))
            throw Bad("TeamsDestinationInvalid", "Only the General and Manager audience destinations can be assigned.");
        TeamsChannelDestinationCandidate candidate = await db.TeamsChannelDestinationCandidates.SingleOrDefaultAsync(x => x.Id == request.CandidateId && x.TenantId == tenant, ct)
            ?? throw NotFound("TeamsDestinationCandidateNotFound", "The verified Teams destination was not found.");
        TeamsChannelDestinationAssignment? assignment = await db.TeamsChannelDestinationAssignments.SingleOrDefaultAsync(x => x.DestinationKey == destinationKey, ct);
        bool replay = assignment?.CandidateId == candidate.Id;
        DateTimeOffset now = clock.GetUtcNow();
        if (assignment is null) { assignment = new() { DestinationKey = destinationKey }; db.TeamsChannelDestinationAssignments.Add(assignment); }
        if (!replay) { assignment.CandidateId = candidate.Id; assignment.AssignedByParticipantId = actor; assignment.AssignedAt = now; await db.SaveChangesAsync(ct); }
        logger.LogInformation("Manager {ActorId} assigned verified Teams destination {CandidateId} to {DestinationKey}.", actor, candidate.Id, destinationKey);
        return new(destinationKey, Map(candidate), assignment.AssignedByParticipantId, assignment.AssignedAt, replay);
    }

    private Guid Manager() => currentUser.Identity is { IsAuthenticated: true, ParticipantId: Guid id } identity && identity.Roles.Contains(QuestRoles.Manager, StringComparer.Ordinal)
        ? id : throw new WorkflowException(currentUser.Identity.IsAuthenticated ? 403 : 401, currentUser.Identity.IsAuthenticated ? "Forbidden" : "Unauthenticated", "Manager authorization is required.");
    private Guid ConfiguredTenant() => Guid.TryParse(options.Value.TeamsBot.TenantId, out Guid id) ? id : throw new WorkflowException(503, "TeamsProvisioningUnavailable", "The configured Teams tenant is unavailable.");
    private async Task<ProvisionTeamsExternalIdentityResponse?> ClassifyExistingAsync(ProvisionTeamsExternalIdentityRequest request, Guid tenant, CancellationToken ct)
    {
        ParticipantExternalIdentity? bySubject = await db.ParticipantExternalIdentities.AsNoTracking().SingleOrDefaultAsync(x => x.Provider == "Entra" && x.TenantId == tenant && x.SubjectId == request.Oid, ct);
        ParticipantExternalIdentity? byParticipant = await db.ParticipantExternalIdentities.AsNoTracking().SingleOrDefaultAsync(x => x.Provider == "Entra" && x.TenantId == tenant && x.ParticipantId == request.ParticipantId, ct);
        if (bySubject is null && byParticipant is null) return null;
        if (bySubject is not null && byParticipant is not null && bySubject.Id == byParticipant.Id)
            return new(bySubject.ParticipantId, bySubject.TenantId, bySubject.SubjectId, bySubject.VerifiedAt ?? bySubject.CreatedAt, true);
        throw Conflict("TeamsExternalIdentityConflict", "The participant or external identity is already mapped differently.");
    }
    private async Task AcquireLock(string resource, IDbContextTransaction transaction, CancellationToken ct)
    {
        await using var command=db.Database.GetDbConnection().CreateCommand(); command.Transaction=transaction.GetDbTransaction();
        command.CommandText="DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=-1,@DbPrincipal='public'; SELECT @result;";
        var parameter=command.CreateParameter();parameter.ParameterName="@resource";parameter.Value=resource;command.Parameters.Add(parameter);
        if(Convert.ToInt32(await command.ExecuteScalarAsync(ct),System.Globalization.CultureInfo.InvariantCulture)<0)throw new WorkflowException(409,"TeamsExternalIdentityConflict","The external identity provisioning lock could not be acquired.");
    }
    private static TeamsDestinationCandidateResponse Map(TeamsChannelDestinationCandidate x) => new(x.Id, x.TenantId, x.ServiceUrl, x.ConversationId, x.TeamId, x.ChannelId, x.UpdatedAt);
    private static WorkflowException Bad(string c,string m)=>new(400,c,m); private static WorkflowException NotFound(string c,string m)=>new(404,c,m); private static WorkflowException Conflict(string c,string m)=>new(409,c,m);
}

public static class TeamsProvisioningEndpoints
{
    public static void MapTeamsProvisioning(this WebApplication app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/manager/teams").RequireAuthorization(QuestPolicies.Manager);
        group.MapPost("/external-identities", (ProvisionTeamsExternalIdentityRequest request, TeamsProvisioningService service, CancellationToken ct) => Run(() => service.ProvisionIdentityAsync(request, ct)));
        group.MapGet("/destination-candidates", (TeamsProvisioningService service, CancellationToken ct) => Run(() => service.CandidatesAsync(ct)));
        group.MapPost("/destinations/{destinationKey}", (string destinationKey, AssignTeamsDestinationRequest request, TeamsProvisioningService service, CancellationToken ct) => Run(() => service.AssignAsync(destinationKey, request, ct)));
    }
    private static async Task<IResult> Run<T>(Func<Task<T>> action) { try { return Results.Ok(await action()); } catch (WorkflowException e) { return Results.Problem(statusCode:e.Status,title:e.Code,detail:e.Message,extensions:new Dictionary<string,object?>{{"code",e.Code}}); } }
}
