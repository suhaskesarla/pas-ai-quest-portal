using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PAS.AIQuestPortal.Api.HistoricalImport.Control;

public sealed class HistoricalImportDatasetConfiguration : IEntityTypeConfiguration<HistoricalImportDataset>
{
    public void Configure(EntityTypeBuilder<HistoricalImportDataset> b)
    {
        b.ToTable("HistoricalImportDatasets", "import"); b.HasKey(x => x.Id);
        b.Property(x => x.DatasetKey).HasMaxLength(150); b.Property(x => x.BoundaryKey).HasMaxLength(64);
        b.Property(x => x.CanonicalFingerprint).HasMaxLength(64); b.Property(x => x.CanonicalizationVersion).HasMaxLength(30);
        b.HasIndex(x => x.DatasetKey).IsUnique();
        b.HasIndex(x => x.BoundaryKey).IsUnique();
    }
}

public sealed class HistoricalImportRunConfiguration : IEntityTypeConfiguration<HistoricalImportRun>
{
    public void Configure(EntityTypeBuilder<HistoricalImportRun> b)
    {
        b.ToTable("HistoricalImportRuns", "import"); b.HasKey(x => x.Id);
        b.Property(x => x.InputFingerprint).HasMaxLength(64);
        b.Property(x => x.Status).HasMaxLength(30); b.Property(x => x.Summary).HasMaxLength(2000);
        b.HasOne<HistoricalImportDataset>().WithMany().HasForeignKey(x => x.DatasetId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => new { x.DatasetId, x.StartedAt });
    }
}

public sealed class HistoricalImportSourceRowConfiguration : IEntityTypeConfiguration<HistoricalImportSourceRow>
{
    public void Configure(EntityTypeBuilder<HistoricalImportSourceRow> b)
    {
        b.ToTable("HistoricalImportSourceRows", "import"); b.HasKey(x => x.Id);
        b.Property(x => x.FileKey).HasMaxLength(150);
        b.Property(x => x.SourceRowKey).HasMaxLength(300); b.Property(x => x.RowHash).HasMaxLength(64); b.Property(x => x.Outcome).HasMaxLength(30);
        b.HasOne<HistoricalImportRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => new { x.DatasetId, x.FileKey, x.SourceRowKey }).IsUnique();
    }
}

public sealed class HistoricalImportArtifactConfiguration : IEntityTypeConfiguration<HistoricalImportArtifact>
{
    public void Configure(EntityTypeBuilder<HistoricalImportArtifact> b)
    {
        b.ToTable("HistoricalImportArtifacts", "import"); b.HasKey(x => x.Id);
        b.Property(x => x.SourceCellKey).HasMaxLength(500);
        b.Property(x => x.ArtifactType).HasMaxLength(100); b.Property(x => x.SourceHash).HasMaxLength(64);
        b.HasOne<HistoricalImportRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => new { x.DatasetId, x.SourceCellKey, x.ArtifactType }).IsUnique();
    }
}

public sealed class HistoricalImportObservationConfiguration : IEntityTypeConfiguration<HistoricalImportObservation>
{
    public void Configure(EntityTypeBuilder<HistoricalImportObservation> b)
    {
        b.ToTable("HistoricalImportObservations", "import"); b.HasKey(x => x.Id);
        b.Property(x => x.Category).HasMaxLength(80); b.Property(x => x.ObservationKey).HasMaxLength(500); b.Property(x => x.ContentHash).HasMaxLength(64);
        b.HasOne<HistoricalImportDataset>().WithMany().HasForeignKey(x => x.DatasetId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<HistoricalImportRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => new { x.DatasetId, x.Category, x.ObservationKey }).IsUnique();
    }
}
