using System.Data.Common;
using PAS.AIQuestPortal.Api.Configuration;
using PAS.AIQuestPortal.Api.Workflow;

namespace PAS.AIQuestPortal.Api.Reporting;

public static class ManagerScoresheetEndpoints
{
    public static IServiceCollection AddManagerScoresheet(this IServiceCollection services) => services.AddScoped<ManagerScoresheetService>();
    public static void MapManagerScoresheet(this WebApplication app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/manager").RequireAuthorization(QuestPolicies.Manager);
        group.MapGet("/reporting-cycles", (ManagerScoresheetService service, CancellationToken ct) => Run(() => service.ReportingCyclesAsync(ct)));
        group.MapGet("/scoresheet", (string? cycleId, ManagerScoresheetService service, CancellationToken ct) => Run(() => service.ScoresheetAsync(ParseCycle(cycleId), ct)));
        group.MapGet("/scoresheet/{participantId:guid}", (Guid participantId, string? cycleId, int? limit, string? cursor, ManagerScoresheetService service, CancellationToken ct) => Run(() => service.ParticipantAsync(participantId, ParseCycle(cycleId), limit ?? 50, cursor, ct)));
    }
    private static Guid ParseCycle(string? value) => Guid.TryParse(value, out Guid id) ? id : throw new WorkflowException(400, "InvalidCycleId", "cycleId must be a valid GUID.");
    private static async Task<IResult> Run<T>(Func<Task<T>> action) { try { return Results.Ok(await action()); } catch (WorkflowException error) { return Results.Problem(statusCode: error.Status, title: error.Code, detail: error.Message, extensions: new Dictionary<string, object?> { ["code"] = error.Code }); } catch (DbException) { return Results.Problem(statusCode: 503, title: "ReadDependencyUnavailable", detail: "The reporting data store is unavailable.", extensions: new Dictionary<string, object?> { ["code"] = "ReadDependencyUnavailable" }); } }
}
