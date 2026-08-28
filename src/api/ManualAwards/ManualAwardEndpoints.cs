using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PAS.AIQuestPortal.Api.Configuration;
using PAS.AIQuestPortal.Api.Workflow;

namespace PAS.AIQuestPortal.Api.ManualAwards;

public static class ManualAwardEndpoints
{
    public static IServiceCollection AddManualAwards(this IServiceCollection services) => services.AddScoped<ManualAwardService>();

    public static void MapManualAwards(this WebApplication app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/manager/manual-awards").RequireAuthorization(QuestPolicies.Manager);
        group.MapGet("/options", (string? cycleId, ManualAwardService service, CancellationToken ct) => Run(() => service.OptionsAsync(ParseGuid(cycleId, "InvalidManualAwardCycleId", "cycleId"), ct)));
        group.MapPost("", (JsonElement body, ManualAwardService service, CancellationToken ct) => Run(() => service.CreateAsync(Parse(body), ct)));
    }

    private static ManualAwardCommand Parse(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object) throw Bad("InvalidManualAwardRequestId", "A manual-award request object is required.");
        Guid requestId = GuidProperty(body, "requestId", "InvalidManualAwardRequestId");
        Guid cycleId = GuidProperty(body, "cycleId", "InvalidManualAwardCycleId");
        Guid participantId = GuidProperty(body, "participantId", "InvalidManualAwardParticipantId");
        Guid categoryId = GuidProperty(body, "awardCategoryId", "InvalidAwardCategoryId");
        if (!body.TryGetProperty("amount", out JsonElement amountElement) || amountElement.ValueKind != JsonValueKind.Number || !amountElement.TryGetInt32(out int amount) || amount <= 0) throw Bad("InvalidManualAwardAmount", "amount must be a positive JSON integer in the Int32 range.");
        string? rawReason = body.TryGetProperty("reason", out JsonElement reasonElement) && reasonElement.ValueKind == JsonValueKind.String ? reasonElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(rawReason)) throw Bad("ManualAwardReasonRequired", "A reason is required.");
        string reason = rawReason.Trim(); if (reason.Length > 2000) throw Bad("ManualAwardReasonTooLong", "The reason cannot exceed 2,000 characters.");
        return new(requestId, cycleId, participantId, categoryId, amount, reason);
    }

    private static Guid GuidProperty(JsonElement body, string name, string code) => body.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out Guid parsed) && parsed != Guid.Empty ? parsed : throw Bad(code, $"{name} must be a valid GUID.");
    private static Guid ParseGuid(string? value, string code, string name) => Guid.TryParse(value, out Guid parsed) && parsed != Guid.Empty ? parsed : throw Bad(code, $"{name} must be a valid GUID.");
    private static WorkflowException Bad(string code, string message) => new(400, code, message);
    private static async Task<IResult> Run<T>(Func<Task<T>> action) { try { return Results.Ok(await action()); } catch (WorkflowException error) { return Results.Problem(statusCode: error.Status, title: error.Code, detail: error.Message, extensions: new Dictionary<string, object?> { ["code"] = error.Code }); } catch (DbException) { return Dependency(); } catch (DbUpdateException) { return Dependency(); } }
    private static IResult Dependency() => Results.Problem(statusCode: 503, title: "ManualAwardDependencyUnavailable", detail: "The manual-award data store is unavailable.", extensions: new Dictionary<string, object?> { ["code"] = "ManualAwardDependencyUnavailable" });
}
