using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PAS.AIQuestPortal.Api.Configuration;
using PAS.AIQuestPortal.Api.Data;
using PAS.AIQuestPortal.Api.Workflow;

namespace PAS.AIQuestPortal.Api.RaidAdministration;

public static class RaidAdministrationEndpoints
{
    public static IServiceCollection AddRaidAdministration(this IServiceCollection services) => services.AddScoped<RaidAdministrationService>();
    public static void MapRaidAdministration(this WebApplication app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/manager/raids").RequireAuthorization(QuestPolicies.Manager);
        group.MapGet("/cycles", (RaidAdministrationService service, CancellationToken ct) => Run(() => service.CyclesAsync(ct)));
        group.MapGet("", (string? cycleId, RaidAdministrationService service, CancellationToken ct) => Run(() => service.ListAsync(GuidValue(cycleId, "InvalidRaidCycleId", "cycleId"), ct)));
        group.MapGet("/{raidSessionId:guid}", (Guid raidSessionId, RaidAdministrationService service, CancellationToken ct) => Run(() => service.GetAsync(raidSessionId, ct)));
        group.MapPost("", (JsonElement body, RaidAdministrationService service, CancellationToken ct) => Run(() => service.CreateAsync(ParseCreate(body), ct)));
        group.MapPut("/{raidSessionId:guid}", (Guid raidSessionId, JsonElement body, RaidAdministrationService service, CancellationToken ct) => Run(() => service.UpdateAsync(raidSessionId, ParseUpdate(body), ct)));
        group.MapGet("/{raidSessionId:guid}/participants", (Guid raidSessionId, RaidAdministrationService service, CancellationToken ct) => Run(() => service.ParticipantsAsync(raidSessionId, ct)));
        group.MapPut("/cycles/{cycleId:guid}/participants/{participantId:guid}/entitlements/{passType}", (Guid cycleId, Guid participantId, string passType, JsonElement body, RaidAdministrationService service, CancellationToken ct) => Run(() => service.UpdateEntitlementAsync(cycleId, participantId, Pass(passType), ParseEntitlement(body), ct)));
        group.MapPost("/{raidSessionId:guid}/participations", (Guid raidSessionId, JsonElement body, RaidAdministrationService service, CancellationToken ct) => Run(() => service.CreateParticipationAsync(raidSessionId, ParseParticipation(body), ct)));
        group.MapPost("/{raidSessionId:guid}/xp-awards", (Guid raidSessionId, JsonElement body, RaidAdministrationService service, CancellationToken ct) => Run(() => service.CreateXpAsync(raidSessionId, ParseXp(body), ct)));
    }

    private static CreateRaidSessionRequest ParseCreate(JsonElement b) => new(GuidProperty(b, "cycleId", "InvalidRaidCycleId"), StringProperty(b, "name"), DateProperty(b, "occurredAt"));
    private static UpdateRaidSessionRequest ParseUpdate(JsonElement b) => new(StringProperty(b, "rowVersion"), StringProperty(b, "name"), DateProperty(b, "occurredAt"));
    private static UpdateRaidEntitlementRequest ParseEntitlement(JsonElement b) { Object(b); if (!b.TryGetProperty("assignedCount", out JsonElement a) || a.ValueKind != JsonValueKind.Number || !a.TryGetInt32(out int count)) throw Bad("InvalidRaidAssignedCount", "assignedCount must be a JSON integer in the Int32 range."); string? version = b.TryGetProperty("rowVersion", out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null; return new(count, version); }
    private static CreateRaidParticipationRequest ParseParticipation(JsonElement b) => new(GuidProperty(b, "participantId", "InvalidRaidParticipantId"), Pass(StringProperty(b, "passType")));
    private static CreateRaidXpRequest ParseXp(JsonElement b) { Guid request = GuidProperty(b, "requestId", "InvalidRaidXpRequestId"), participant = GuidProperty(b, "participantId", "InvalidRaidParticipantId"); if (!b.TryGetProperty("amount", out JsonElement a) || a.ValueKind != JsonValueKind.Number || !a.TryGetInt32(out int amount) || amount <= 0) throw Bad("InvalidRaidXpAmount", "amount must be a positive JSON integer in the Int32 range."); return new(request, participant, amount, b.TryGetProperty("reason", out JsonElement r) && r.ValueKind == JsonValueKind.String ? r.GetString()! : ""); }
    private static Guid GuidProperty(JsonElement b, string name, string code) { Object(b); return b.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out Guid id) && id != Guid.Empty ? id : throw Bad(code, $"{name} must be a valid GUID."); }
    private static Guid GuidValue(string? value, string code, string name) => Guid.TryParse(value, out Guid id) && id != Guid.Empty ? id : throw Bad(code, $"{name} must be a valid GUID.");
    private static DateTimeOffset DateProperty(JsonElement b, string name) { Object(b); return b.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(v.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out DateTimeOffset date) ? date : throw Bad("RaidSessionValidationFailed", $"{name} is required."); }
    private static string StringProperty(JsonElement b, string name) { Object(b); return b.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : ""; }
    private static void Object(JsonElement b) { if (b.ValueKind != JsonValueKind.Object) throw Bad("RaidValidationFailed", "A JSON request object is required."); }
    private static PassType Pass(string? value) => Enum.TryParse(value, true, out PassType type) && type is PassType.Physical or PassType.Remote ? type : throw Bad("InvalidRaidPassType", "passType must be Physical or Remote.");
    private static WorkflowException Bad(string code, string message) => new(400, code, message);
    private static async Task<IResult> Run<T>(Func<Task<T>> action) { try { return Results.Ok(await action()); } catch (WorkflowException e) { return Results.Problem(statusCode: e.Status, title: e.Code, detail: e.Message, extensions: new Dictionary<string, object?> { ["code"] = e.Code }); } catch (DbUpdateConcurrencyException) { return Problem(409, "RaidAdministrationConflict", "The raid data changed; refresh and try again."); } catch (DbException) { return Problem(503, "RaidAdministrationDependencyUnavailable", "The raid data store is unavailable."); } catch (DbUpdateException) { return Problem(503, "RaidAdministrationDependencyUnavailable", "The raid data store is unavailable."); } }
    private static IResult Problem(int status, string code, string detail) => Results.Problem(statusCode: status, title: code, detail: detail, extensions: new Dictionary<string, object?> { ["code"] = code });
}
