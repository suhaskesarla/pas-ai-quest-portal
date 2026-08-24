using Microsoft.Extensions.Options;

namespace PAS.AIQuestPortal.Api.Configuration;

public sealed class StorageOptionsValidator(IHostEnvironment environment):IValidateOptions<StorageOptions>
{
    public ValidateOptionsResult Validate(string? name,StorageOptions options)
    {
        bool local=environment.IsDevelopment()||environment.IsEnvironment("Test");
        if(options.Evidence.MaxAttachmentsPerRequest<=0||options.Evidence.MaxBytesPerFile<=0||options.Evidence.MaxBytesPerRequest<options.Evidence.MaxBytesPerFile)return ValidateOptionsResult.Fail("Evidence attachment limits are invalid.");
        if(!local&&!string.IsNullOrWhiteSpace(options.ConnectionString))return ValidateOptionsResult.Fail("Storage connection strings/shared-key credentials are forbidden outside Development/Test; use BlobServiceUri with managed identity.");
        if(options.Evidence.Enabled&&!local)return ValidateOptionsResult.Fail("Production attachment capability requires a real malware scanner, which is not implemented in Step 7.");
        if(options.Evidence.Enabled&&!string.Equals(options.Evidence.MalwareScanner,"DeterministicPassThrough",StringComparison.OrdinalIgnoreCase))return ValidateOptionsResult.Fail("Development/Test attachment capability requires the explicitly configured deterministic pass-through scanner.");
        return ValidateOptionsResult.Success;
    }
}
