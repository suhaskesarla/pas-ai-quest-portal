using System.Data.Common;
using PAS.AIQuestPortal.Api.Configuration;
using PAS.AIQuestPortal.Api.Data;
using PAS.AIQuestPortal.Api.Workflow;

namespace PAS.AIQuestPortal.Api.ChallengeAdministration;

public static class ChallengeAdministrationEndpoints
{
    public static IServiceCollection AddChallengeAdministration(this IServiceCollection services) => services.AddScoped<ChallengeAdministrationService>();
    public static void MapChallengeAdministration(this WebApplication app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/manager").RequireAuthorization(QuestPolicies.Manager);
        group.MapGet("/challenge-options", (ChallengeAdministrationService service, CancellationToken ct) => Run(() => service.OptionsAsync(ct)));
        group.MapGet("/challenges", (Guid? cycleId, ChallengeStatus? status, ChallengeAdministrationService service, CancellationToken ct) => Run(() => service.ListAsync(cycleId, status, ct)));
        group.MapGet("/challenges/{id:guid}", (Guid id, ChallengeAdministrationService service, CancellationToken ct) => Run(() => service.GetAsync(id, ct)));
        group.MapPost("/challenges", (CreateChallengeRequest request, ChallengeAdministrationService service, CancellationToken ct) => Run(() => service.CreateAsync(request, ct)));
        group.MapPut("/challenges/{id:guid}", (Guid id, UpdateChallengeRequest request, ChallengeAdministrationService service, CancellationToken ct) => Run(() => service.UpdateAsync(id, request, ct)));
        group.MapPost("/challenges/{id:guid}/publish", (Guid id, PublishChallengeRequest request, ChallengeAdministrationService service, CancellationToken ct) => Run(() => service.PublishAsync(id, request, ct)));
    }
    private static async Task<IResult> Run<T>(Func<Task<T>> action)
    {
        try { return Results.Ok(await action()); }
        catch (WorkflowException error) { return Results.Problem(statusCode: error.Status, title: error.Code, detail: error.Message, extensions: new Dictionary<string, object?> { ["code"] = error.Code }); }
        catch (DbException) { return Results.Problem(statusCode: 503, title: "ReadDependencyUnavailable", detail: "The data store is unavailable.", extensions: new Dictionary<string, object?> { ["code"] = "ReadDependencyUnavailable" }); }
    }
}
