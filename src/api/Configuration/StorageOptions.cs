using System.ComponentModel.DataAnnotations;

namespace PAS.AIQuestPortal.Api.Configuration;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    [Required]
    public string BlobServiceUri { get; init; } = string.Empty;

    [Required]
    public string ConnectionString { get; init; } = string.Empty;
}
