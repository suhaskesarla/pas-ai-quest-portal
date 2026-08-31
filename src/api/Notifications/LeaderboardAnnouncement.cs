using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using PAS.AIQuestPortal.Api.Authentication;
using PAS.AIQuestPortal.Api.Configuration;
using PAS.AIQuestPortal.Api.Data;
using PAS.AIQuestPortal.Api.Reporting;
using PAS.AIQuestPortal.Api.Workflow;

namespace PAS.AIQuestPortal.Api.Notifications;

public sealed record LeaderboardAnnouncementRequest(Guid RequestId);
public sealed record LeaderboardAnnouncementResult(Guid RequestId, Guid EventId, Guid CycleId, string Status, DateTimeOffset CreatedAt, bool Replay);

public sealed class LeaderboardAnnouncementService(
    QuestDbContext db,
    IQuestCurrentUser currentUser,
    INotificationOutboxWriter writer,
    IndividualLeaderboardQuery leaderboard,
    IOptions<NotificationOptions> options,
    IHostEnvironment environment,
    IConfiguration configuration,
    TimeProvider clock)
{
    public async Task<LeaderboardAnnouncementResult> CreateAsync(Guid cycleId, LeaderboardAnnouncementRequest request, CancellationToken ct)
    {
        Manager();
        if (request.RequestId == Guid.Empty) throw Bad("InvalidLeaderboardAnnouncementRequestId", "requestId must be a non-empty GUID.");
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await AcquireLock(request.RequestId, tx, ct);
        NotificationOutbox? existing = await db.NotificationOutbox.SingleOrDefaultAsync(x => x.AggregateType == "LeaderboardAnnouncementRequest" && x.AggregateId == request.RequestId, ct);
        if (existing is not null)
        {
            if (!TryCycle(existing.PayloadJson, out Guid existingCycle) || existingCycle != cycleId)
                throw Conflict("LeaderboardAnnouncementRequestConflict", "requestId was already used for another leaderboard announcement.");
            await tx.CommitAsync(ct);
            return new(request.RequestId, existing.EventId, cycleId, existing.Status, existing.CreatedAt, true);
        }

        if (!options.Value.Enabled) throw Conflict("NotificationsDisabled", "Notifications are disabled.");
        bool synthetic = (environment.IsDevelopment() || environment.IsEnvironment("Test")) && string.Equals(configuration["Authentication:Mode"], "Demo", StringComparison.Ordinal);
        if (!synthetic && !options.Value.RealUserLeaderboardEnabled) throw Conflict("RealUserLeaderboardDisabled", "Real-user leaderboard announcements are disabled pending privacy approval.");
        DateTimeOffset now = clock.GetUtcNow();

        Cycle cycle = await db.Cycles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == cycleId, ct)
            ?? throw new WorkflowException(404, "ReportingCycleNotFound", "The reporting cycle was not found.");
        IReadOnlyList<LeaderboardEntry> rows = await leaderboard.ExecuteAsync(cycleId, null, ct);
        Guid eventId = Guid.NewGuid();
        var payload = new LeaderboardAnnouncementPayload(cycleId, cycle.Name, now, synthetic,
            rows.Take(3).Select(x => new LeaderboardRowPayload(x.Rank, x.DisplayName, x.TotalXp)).ToArray());
        NotificationOutbox outbox = writer.Enqueue(eventId, NotificationEventType.LeaderboardAnnouncement, NotificationDestinations.General(), "LeaderboardAnnouncementRequest", request.RequestId, payload, now);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(request.RequestId, eventId, cycleId, outbox.Status, now, false);
    }

    private static bool TryCycle(string json, out Guid cycleId)
    {
        try { cycleId = System.Text.Json.JsonSerializer.Deserialize<LeaderboardAnnouncementPayload>(json, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))?.CycleId ?? Guid.Empty; return cycleId != Guid.Empty; }
        catch (System.Text.Json.JsonException) { cycleId = Guid.Empty; return false; }
    }

    private async Task AcquireLock(Guid requestId, IDbContextTransaction transaction, CancellationToken ct)
    {
        await using DbCommand command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = "DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=0,@DbPrincipal='public'; SELECT @result;";
        DbParameter parameter = command.CreateParameter(); parameter.ParameterName = "@resource"; parameter.Value = $"quest-leaderboard-announcement:{requestId:N}"; command.Parameters.Add(parameter);
        int result = Convert.ToInt32(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
        if (result < 0) throw Conflict("LeaderboardAnnouncementConflict", "Another announcement request is in progress.");
    }

    private void Manager()
    {
        if (currentUser.Identity is not { IsAuthenticated: true } identity) throw new WorkflowException(401, "Unauthenticated", "Authentication is required.");
        if (!identity.Roles.Contains(QuestRoles.Manager, StringComparer.Ordinal)) throw new WorkflowException(403, "Forbidden", "Manager authorization is required.");
    }
    private static WorkflowException Bad(string code, string message) => new(400, code, message);
    private static WorkflowException Conflict(string code, string message) => new(409, code, message);
}

public static class LeaderboardAnnouncementEndpoints
{
    public static IServiceCollection AddLeaderboardAnnouncements(this IServiceCollection services)
    {
        services.AddScoped<IndividualLeaderboardQuery>();
        services.AddScoped<LeaderboardAnnouncementService>();
        return services;
    }

    public static void MapLeaderboardAnnouncements(this WebApplication app)
    {
        app.MapPost("/api/manager/leaderboards/individual/{cycleId:guid}/teams-announcements", async (Guid cycleId, LeaderboardAnnouncementRequest request, LeaderboardAnnouncementService service, CancellationToken ct) =>
        {
            try { return Results.Accepted(value: await service.CreateAsync(cycleId, request, ct)); }
            catch (WorkflowException error) { return Results.Problem(statusCode: error.Status, title: error.Code, detail: error.Message, extensions: new Dictionary<string, object?> { ["code"] = error.Code }); }
            catch (DbException) { return Results.Problem(statusCode: 503, title: "NotificationDependencyUnavailable", detail: "Notification persistence is unavailable.", extensions: new Dictionary<string, object?> { ["code"] = "NotificationDependencyUnavailable" }); }
        }).RequireAuthorization(QuestPolicies.Manager);
    }
}
