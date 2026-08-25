using System.ComponentModel.DataAnnotations;

namespace PAS.AIQuestPortal.Api.Configuration;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    [Required]
    public string BlobServiceUri { get; init; } = string.Empty;

    public string ConnectionString { get; init; } = string.Empty;

    public EvidenceAttachmentOptions Evidence { get; init; } = new();
}

public sealed class EvidenceAttachmentOptions
{
    public bool Enabled { get; init; } = true;
    public int MaxAttachmentsPerRequest { get; init; } = 5;
    public long MaxBytesPerFile { get; init; } = 25L * 1024 * 1024;
    public long MaxBytesPerRequest { get; init; } = 50L * 1024 * 1024;
    public string MalwareScanner { get; init; } = "DeterministicPassThrough";
}
