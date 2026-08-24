using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Json;
using PAS.AIQuestPortal.Api.Authentication;
using PAS.AIQuestPortal.Api.Configuration;

namespace PAS.AIQuestPortal.Api.Workflow;

public static class SubmissionWorkflowEndpoints
{
    public static IServiceCollection AddSubmissionWorkflow(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<SubmissionWorkflowService>();
        services.Configure<JsonOptions>(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        return services;
    }

    public static void MapSubmissionWorkflow(this WebApplication app)
    {
        app.MapGet("/api/challenges/eligible", (SubmissionWorkflowService service, CancellationToken ct) => Run(() => service.EligibleAsync(ct))).RequireAuthorization(QuestPolicies.Participant);
        app.MapGet("/api/submissions/mine", (SubmissionWorkflowService service, CancellationToken ct) => Run(() => service.MineAsync(ct))).RequireAuthorization(QuestPolicies.Participant);
        app.MapPost("/api/submissions", (SubmissionWorkflowService service, CreateSubmissionRequest request, CancellationToken ct) => Run(() => service.CreateAsync(request, ct))).RequireAuthorization(QuestPolicies.Participant);
        app.MapPut("/api/submissions/{id:guid}/resubmission", (SubmissionWorkflowService service, Guid id, ResubmitRequest request, CancellationToken ct) => Run(() => service.ResubmitAsync(id, request, ct))).RequireAuthorization(QuestPolicies.Participant);
        app.MapGet("/api/submissions/review-queue", (SubmissionWorkflowService service, CancellationToken ct) => Run(() => service.QueueAsync(ct))).RequireAuthorization(QuestPolicies.Manager);
        app.MapPost("/api/submissions/{id:guid}/review", (SubmissionWorkflowService service, Guid id, ReviewRequest request, CancellationToken ct) => Run(() => service.ReviewAsync(id, request, ct))).RequireAuthorization(QuestPolicies.Manager);
        app.MapPost("/api/manager/xp/{entryId:guid}/corrections", (SubmissionWorkflowService service, Guid entryId, CorrectionRequest request, CancellationToken ct) => Run(() => service.CorrectAsync(entryId, request, ct))).RequireAuthorization(QuestPolicies.Manager);
    }

    private static async Task<IResult> Run<T>(Func<Task<T>> operation)
    {
        try { return Results.Ok(await operation()); }
        catch (WorkflowException error)
        {
            return Results.Problem(statusCode: error.Status, title: error.Code, detail: error.Message, extensions: new Dictionary<string, object?> { ["code"] = error.Code });
        }
    }
}
