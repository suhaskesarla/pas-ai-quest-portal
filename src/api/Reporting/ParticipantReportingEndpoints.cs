using System.Data.Common;
using PAS.AIQuestPortal.Api.Authentication;
using PAS.AIQuestPortal.Api.Configuration;
using PAS.AIQuestPortal.Api.Workflow;

namespace PAS.AIQuestPortal.Api.Reporting;

public static class ParticipantReportingEndpoints
{
    public static IServiceCollection AddParticipantReporting(this IServiceCollection services) => services.AddScoped<ParticipantReportingService>();

    public static void MapParticipantReporting(this WebApplication app)
    {
        app.MapGet("/api/participant/reporting-cycles", (ParticipantReportingService service, CancellationToken ct) => Run(() => service.ReportingCyclesAsync(ct))).RequireAuthorization(QuestPolicies.Participant);
        app.MapGet("/api/participant/dashboard", (Guid cycleId, ParticipantReportingService service, CancellationToken ct) => Run(() => service.DashboardAsync(cycleId, ct))).RequireAuthorization(QuestPolicies.Participant);
        app.MapGet("/api/leaderboards/individual", (Guid cycleId, ParticipantReportingService service, CancellationToken ct) => Run(() => service.LeaderboardAsync(cycleId, ct))).RequireAuthorization(QuestPolicies.Participant);
        app.MapGet("/api/participant/xp-activity", (Guid cycleId, int? limit, string? cursor, ParticipantReportingService service, CancellationToken ct) => Run(() => service.XpActivityAsync(cycleId, limit ?? 25, cursor, ct))).RequireAuthorization(QuestPolicies.Participant);
        app.MapGet("/api/participant/team", (Guid cycleId, ParticipantReportingService service, CancellationToken ct) => Run(() => service.TeamAsync(cycleId, ct))).RequireAuthorization(QuestPolicies.Participant);
    }

    private static async Task<IResult> Run<T>(Func<Task<T>> operation)
    {
        try { return Results.Ok(await operation()); }
        catch (WorkflowException error) { return Results.Problem(statusCode: error.Status, title: error.Code, detail: error.Message, extensions: new Dictionary<string, object?> { ["code"] = error.Code }); }
        catch (DbException) { return Results.Problem(statusCode: 503, title: "ReadDependencyUnavailable", detail: "The reporting data store is unavailable.", extensions: new Dictionary<string, object?> { ["code"] = "ReadDependencyUnavailable" }); }
    }
}
