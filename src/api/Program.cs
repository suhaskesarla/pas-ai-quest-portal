using Azure.Storage.Blobs;
using Azure.Identity;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using PAS.AIQuestPortal.Api.Authentication;
using PAS.AIQuestPortal.Api.Configuration;
using PAS.AIQuestPortal.Api.Data;
using PAS.AIQuestPortal.Api.Development;
using PAS.AIQuestPortal.Api.Health;
using PAS.AIQuestPortal.Api.Evidence;
using PAS.AIQuestPortal.Api.HistoricalImport;
using PAS.AIQuestPortal.Api.Workflow;
using PAS.AIQuestPortal.Api.Reporting;
using PAS.AIQuestPortal.Api.ChallengeAdministration;

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
builder.Services.AddSingleton<IValidateOptions<StorageOptions>,StorageOptionsValidator>();

builder.AddQuestAuthentication();
builder.Services.AddSubmissionWorkflow();
builder.Services.AddParticipantReporting();
builder.Services.AddManagerScoresheet();
builder.Services.AddChallengeAdministration();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var databaseConnectionString = builder.Configuration.GetConnectionString("QuestDatabase")
    ?? throw new InvalidOperationException("ConnectionStrings:QuestDatabase is required.");

builder.Services.AddSingleton(serviceProvider =>
{
    StorageOptions options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<StorageOptions>>().Value;
    return !string.IsNullOrWhiteSpace(options.ConnectionString)
        ? new BlobServiceClient(options.ConnectionString)
        : new BlobServiceClient(new Uri(options.BlobServiceUri), new DefaultAzureCredential());
});
builder.Services.AddSingleton<IEvidenceMalwareScanner>(serviceProvider =>
    builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Test")
        ? new DeterministicPassThroughEvidenceMalwareScanner()
        : new DisabledEvidenceMalwareScanner());
builder.Services.AddSingleton<AzureEvidenceBlobStore>();
builder.Services.AddSingleton<IEvidenceBlobStore>(sp=>sp.GetRequiredService<AzureEvidenceBlobStore>());
builder.Services.AddSingleton<IEvidenceBlobStoreInitializer>(sp=>sp.GetRequiredService<AzureEvidenceBlobStore>());
builder.Services.AddSingleton<EvidenceAttachmentValidator>();
builder.Services.AddDbContext<QuestDbContext>(options => options.UseSqlServer(databaseConnectionString));
builder.Services.AddScoped<DevelopmentDemoDataSeeder>();
builder.Services
    .AddHealthChecks()
    .AddSqlServer(databaseConnectionString, name: "sqlserver", tags: ["ready"])
    .AddCheck<BlobStorageHealthCheck>("blob-storage", tags: ["ready"]);

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<QuestDbContext>();
    await database.Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<DevelopmentDemoDataSeeder>().SeedAsync();
    StorageOptions storage=scope.ServiceProvider.GetRequiredService<IOptions<StorageOptions>>().Value;
    if(storage.Evidence.Enabled)await scope.ServiceProvider.GetRequiredService<IEvidenceBlobStoreInitializer>().EnsurePrivateContainerAsync(CancellationToken.None);
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
app.MapParticipantReporting();
app.MapManagerScoresheet();
app.MapChallengeAdministration();

app.Run();
return 0;

public partial class Program;
