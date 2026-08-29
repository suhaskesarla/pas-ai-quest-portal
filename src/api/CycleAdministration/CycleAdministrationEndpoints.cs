using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PAS.AIQuestPortal.Api.Configuration;
using PAS.AIQuestPortal.Api.Workflow;

namespace PAS.AIQuestPortal.Api.CycleAdministration;

public static class CycleAdministrationEndpoints
{
    public static IServiceCollection AddCycleAdministration(this IServiceCollection services) => services.AddScoped<CycleAdministrationService>();
    public static void MapCycleAdministration(this WebApplication app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/manager/cycles").RequireAuthorization(QuestPolicies.Manager);
        group.MapGet("", (CycleAdministrationService service, CancellationToken ct) => Run(() => service.ListAsync(ct)));
        group.MapGet("/{cycleId:guid}", (Guid cycleId, CycleAdministrationService service, CancellationToken ct) => Run(() => service.GetAsync(cycleId, ct)));
        group.MapGet("/{cycleId:guid}/participant-options", (Guid cycleId, CycleAdministrationService service, CancellationToken ct) => Run(() => service.ParticipantOptionsAsync(cycleId, ct)));
        group.MapPost("", (CreateCycleRequest request, CycleAdministrationService service, CancellationToken ct) => Run(() => service.CreateAsync(request, ct)));
        group.MapPut("/{cycleId:guid}", (Guid cycleId, UpdateCycleRequest request, CycleAdministrationService service, CancellationToken ct) => Run(() => service.UpdateAsync(cycleId, request, ct)));
        group.MapPost("/{cycleId:guid}/start-closing", (Guid cycleId, CycleTransitionRequest request, CycleAdministrationService service, CancellationToken ct) => Run(() => service.StartClosingAsync(cycleId, request, ct)));
        group.MapPost("/{cycleId:guid}/finalise", (Guid cycleId, CycleTransitionRequest request, CycleAdministrationService service, CancellationToken ct) => Run(() => service.FinaliseAsync(cycleId, request, ct)));
        group.MapPost("/{cycleId:guid}/participants", (Guid cycleId, AddCycleParticipantRequest request, CycleAdministrationService service, CancellationToken ct) => Run(() => service.AddParticipantAsync(cycleId, request, ct)));
        group.MapPost("/{cycleId:guid}/participants/{participantId:guid}/status", (Guid cycleId, Guid participantId, ChangeCycleParticipantStatusRequest request, CycleAdministrationService service, CancellationToken ct) => Run(() => service.ChangeParticipantStatusAsync(cycleId, participantId, request, ct)));
    }

    private static async Task<IResult> Run<T>(Func<Task<T>> action)
    {
        try { return Results.Ok(await action()); }
        catch (WorkflowException error) { return Problem(error.Status, error.Code, error.Message); }
        catch (SqlException error) when (error.Number is 1205 or 3960) { return Problem(409, "CycleAdministrationConflict", "A concurrent cycle administration operation won; refresh and try again."); }
        catch (DbUpdateException error) when (error.InnerException is SqlException { Number: 1205 or 3960 }) { return Problem(409, "CycleAdministrationConflict", "A concurrent cycle administration operation won; refresh and try again."); }
        catch (DbException) { return Problem(503, "CycleAdministrationDependencyUnavailable", "The cycle administration data store is unavailable."); }
        catch (DbUpdateException) { return Problem(503, "CycleAdministrationDependencyUnavailable", "The cycle administration data store is unavailable."); }
    }
    private static IResult Problem(int status, string code, string detail) => Results.Problem(statusCode: status, title: code, detail: detail, extensions: new Dictionary<string, object?> { ["code"] = code });
}
