using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using PAS.AIQuestPortal.Api.Authentication;
using PAS.AIQuestPortal.Api.Configuration;
using PAS.AIQuestPortal.Api.Health;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<QuestAuthenticationOptions>()
    .Bind(builder.Configuration.GetSection(QuestAuthenticationOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<StorageOptions>()
    .Bind(builder.Configuration.GetSection(StorageOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

var authenticationMode = builder.Configuration[$"{QuestAuthenticationOptions.SectionName}:Mode"];
if (!string.Equals(authenticationMode, AuthenticationModes.Stub, StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException(
        "Step 2 supports Authentication:Mode=Stub only. Entra authentication is intentionally deferred to Step 5.");
}

builder.Services
    .AddAuthentication(StubAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, StubAuthenticationHandler>(
        StubAuthenticationHandler.SchemeName,
        _ => { });
builder.Services.AddAuthorization();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var databaseConnectionString = builder.Configuration.GetConnectionString("QuestDatabase")
    ?? throw new InvalidOperationException("ConnectionStrings:QuestDatabase is required.");
var storageConnectionString = builder.Configuration["Storage:ConnectionString"]
    ?? throw new InvalidOperationException("Storage:ConnectionString is required.");

builder.Services.AddSingleton(new BlobServiceClient(storageConnectionString));
builder.Services
    .AddHealthChecks()
    .AddSqlServer(databaseConnectionString, name: "sqlserver", tags: ["ready"])
    .AddCheck<BlobStorageHealthCheck>("blob-storage", tags: ["ready"]);

var app = builder.Build();

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
app.MapGet("/api/whoami", (System.Security.Claims.ClaimsPrincipal user) => new
    {
        subject = user.FindFirst("sub")?.Value,
        name = user.Identity?.Name,
        roles = user.FindAll(System.Security.Claims.ClaimTypes.Role).Select(claim => claim.Value)
    })
    .RequireAuthorization();

app.Run();

public partial class Program;
