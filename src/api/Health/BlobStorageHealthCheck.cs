using Azure.Storage.Blobs;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PAS.AIQuestPortal.Api.Health;

public sealed class BlobStorageHealthCheck(BlobServiceClient blobServiceClient) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await blobServiceClient.GetPropertiesAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Blob storage is unavailable.", exception);
        }
    }
}
