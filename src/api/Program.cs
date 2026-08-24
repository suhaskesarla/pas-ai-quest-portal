using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using PAS.AIQuestPortal.Api.Authentication;
using PAS.AIQuestPortal.Api.Configuration;
using PAS.AIQuestPortal.Api.Data;
using PAS.AIQuestPortal.Api.Health;
using PAS.AIQuestPortal.Api.HistoricalImport;
using PAS.AIQuestPortal.Api.Workflow;

if (args.Length > 0 && string.Equals(args[0], "historical-import", StringComparison.OrdinalIgnoreCase))
{
    return await HistoricalImportCommand.RunAsync(args[1..]);
}

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<StorageOptions>()
    .Bind(builder.Configuration.GetSection(StorageOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.AddQuestAuthentication();
builder.Services.AddSubmissionWorkflow();

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
builder.Services.AddDbContext<QuestDbContext>(options => options.UseSqlServer(databaseConnectionString));
builder.Services
    .AddHealthChecks()
    .AddSqlServer(databaseConnectionString, name: "sqlserver", tags: ["ready"])
    .AddCheck<BlobStorageHealthCheck>("blob-storage", tags: ["ready"]);

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<QuestDbContext>();
    await database.Database.MigrateAsync();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
app.MapQuestAuthenticationEndpoints();
app.MapSubmissionWorkflow();

app.Run();
return 0;

public partial class Program;
