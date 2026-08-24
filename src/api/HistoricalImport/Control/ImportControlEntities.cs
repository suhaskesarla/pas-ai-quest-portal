namespace PAS.AIQuestPortal.Api.HistoricalImport.Control;

// Infrastructure-only provenance records. Core domain entities do not navigate to these tables.
public sealed class HistoricalImportDataset
{
    public Guid Id { get; set; }
    public required string DatasetKey { get; set; }
    public required string BoundaryKey { get; set; }
    public required string CanonicalFingerprint { get; set; }
    public required string CanonicalizationVersion { get; set; }
    public DateTimeOffset EstablishedAt { get; set; }
}

public sealed class HistoricalImportRun
{
    public Guid Id { get; set; }
    public Guid DatasetId { get; set; }
    public required string InputFingerprint { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public required string Summary { get; set; }
}

public sealed class HistoricalImportSourceRow
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public Guid DatasetId { get; set; }
    public required string FileKey { get; set; }
    public int PhysicalRowNumber { get; set; }
    public required string SourceRowKey { get; set; }
    public required string RowHash { get; set; }
    public required string Outcome { get; set; }
}

public sealed class HistoricalImportArtifact
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public Guid DatasetId { get; set; }
    public required string SourceCellKey { get; set; }
    public required string ArtifactType { get; set; }
    public Guid ArtifactId { get; set; }
    public required string SourceHash { get; set; }
}

public sealed class HistoricalImportObservation
{
    public Guid Id { get; set; }
    public Guid DatasetId { get; set; }
    public Guid RunId { get; set; }
    public required string Category { get; set; }
    public required string ObservationKey { get; set; }
    public required string ContentHash { get; set; }
}
