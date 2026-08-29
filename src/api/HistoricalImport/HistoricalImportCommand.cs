using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PAS.AIQuestPortal.Api.Data;
using PAS.AIQuestPortal.Api.HistoricalImport.Control;

namespace PAS.AIQuestPortal.Api.HistoricalImport;

public static class HistoricalImportCommand
{
    private const string CanonicalizationVersion = "historical-import-semantic-v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    static HistoricalImportCommand() => JsonOptions.Converters.Add(new JsonStringEnumConverter());

    public static async Task<int> RunAsync(string[] args)
    {
        Dictionary<string, string> options = ParseOptions(args);
        if (!options.TryGetValue("manifest", out string? manifestPath))
        {
            Console.Error.WriteLine("Usage: historical-import --manifest <path> [--connection <connection-string>] [--report <directory>]");
            return 2;
        }

        string connection = options.GetValueOrDefault("connection")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__QuestDatabase")
            ?? throw new InvalidOperationException("A database connection is required through --connection or ConnectionStrings__QuestDatabase.");
        string reportDirectory = options.GetValueOrDefault("report") ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(manifestPath))!, "reports");

        var dbOptions = new DbContextOptionsBuilder<QuestDbContext>().UseSqlServer(connection).Options;
        await using var db = new QuestDbContext(dbOptions);
        await db.Database.MigrateAsync();
        HistoricalImportResult result = await ExecuteAsync(db, manifestPath, reportDirectory);
        Console.WriteLine(result.HumanReport);
        return result.Succeeded ? 0 : 1;
    }

    public static async Task<HistoricalImportResult> ExecuteAsync(QuestDbContext db, string manifestPath, string reportDirectory)
    {
        string absoluteManifest = Path.GetFullPath(manifestPath);
        string root = Path.GetDirectoryName(absoluteManifest)!;
        byte[] manifestBytes = await File.ReadAllBytesAsync(absoluteManifest);
        ImportManifest manifest = JsonSerializer.Deserialize<ImportManifest>(manifestBytes, JsonOptions)
            ?? throw new InvalidDataException("The import manifest is empty or invalid.");
        string manifestHash = Hash(manifestBytes);
        var report = new ReconciliationReport
        {
            DatasetKey = manifest.DatasetKey,
            ManifestHash = manifestHash,
            HeaderFidelity = manifest.HeaderFidelity,
            StartedAt = DateTimeOffset.UtcNow
        };

        ImportPlan? plan = null;
        try
        {
            plan = await BuildPlanAsync(db, manifest, root, report);
            BuildCanonicalProvenance(manifest, root, plan);
            report.ManifestHash = plan.CanonicalFingerprint;
            await DetectDatasetConflictAsync(db, manifest, plan, report);
            if (report.Errors.Count == 0)
            {
                await PersistAndReconcileAsync(db, manifest, plan, report);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            report.Errors.Add(new ReconciliationError("ImportException", ex.Message));
        }

        report.CompletedAt = DateTimeOffset.UtcNow;
        report.Succeeded = report.Errors.Count == 0;
        string human = RenderHumanReport(report);
        Directory.CreateDirectory(reportDirectory);
        await File.WriteAllTextAsync(Path.Combine(reportDirectory, "historical-import-report.json"), JsonSerializer.Serialize(report, JsonOptions));
        await File.WriteAllTextAsync(Path.Combine(reportDirectory, "historical-import-report.md"), human);
        return new HistoricalImportResult(report.Succeeded, report, human);
    }

    private static async Task<ImportPlan> BuildPlanAsync(QuestDbContext db, ImportManifest manifest, string root, ReconciliationReport report)
    {
        ValidateManifest(manifest, report);
        var plan = new ImportPlan(manifest.DatasetKey);
        LoadIdentityMap(Path.Combine(root, manifest.ParticipantMap), plan, report);

        foreach (SheetDefinition sheet in manifest.Sheets)
        {
            CycleDefinition? cycle = manifest.Cycles.SingleOrDefault(x => x.Key == sheet.CycleKey);
            if (cycle is null)
            {
                report.Errors.Add(new("UnknownCycleMapping", $"Sheet {sheet.FileKey} references unknown cycle {sheet.CycleKey}."));
                continue;
            }

            CsvDocument csv;
            try { csv = CsvDocument.Read(Path.Combine(root, sheet.Path)); }
            catch (Exception ex) { report.Errors.Add(new("CsvReadFailure", $"{sheet.FileKey}: {ex.Message}")); continue; }

            if (!csv.Headers.SequenceEqual(sheet.ExpectedHeaders, StringComparer.Ordinal))
            {
                report.Errors.Add(new("HeaderMismatch", $"{sheet.FileKey}: expected [{string.Join(" | ", sheet.ExpectedHeaders)}], found [{string.Join(" | ", csv.Headers)}]."));
                continue;
            }

            var mappings = sheet.Columns.GroupBy(x => x.Header, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.ToList(), StringComparer.Ordinal);
            foreach (string header in csv.Headers)
            {
                if (!mappings.TryGetValue(header, out List<ColumnDefinition>? matches))
                    report.Errors.Add(new("UnmappedColumn", $"{sheet.FileKey}: column '{header}' has no classification."));
                else if (matches.Count != 1)
                    report.Errors.Add(new("AmbiguousColumn", $"{sheet.FileKey}: column '{header}' has {matches.Count} classifications."));
            }
            foreach (ColumnDefinition mapping in sheet.Columns.Where(x => !csv.Headers.Contains(x.Header, StringComparer.Ordinal)))
                report.Errors.Add(new("MissingMappedColumn", $"{sheet.FileKey}: mapped column '{mapping.Header}' is absent."));
            if (report.Errors.Count > 0) continue;

            ColumnDefinition participantColumn = sheet.Columns.Single(x => x.Kind == ColumnKind.Participant);
            ColumnDefinition totalColumn = sheet.Columns.Single(x => x.Kind == ColumnKind.Total);
            var sheetReport = new SheetReport(sheet.FileKey, cycle.Key, csv.Rows.Count);
            report.Sheets.Add(sheetReport);

            foreach (CsvRow row in csv.Rows)
            {
                string sourceName = row[participantColumn.Header].Trim();
                string sourceRowKey = $"{sheet.FileKey}:row:{row.PhysicalRowNumber}";
                string rowHash = Hash(Encoding.UTF8.GetBytes(string.Join("\u001f", csv.Headers.Select(h => row[h]))));
                if (!plan.Aliases.TryGetValue(sourceName, out List<ParticipantIdentity>? identities) || identities.Count == 0)
                {
                    report.Errors.Add(new("UnmappedParticipant", $"{sourceRowKey}: participant '{sourceName}' is not mapped."));
                    continue;
                }
                if (identities.Count != 1)
                {
                    report.Errors.Add(new("AmbiguousParticipant", $"{sourceRowKey}: participant '{sourceName}' maps to {identities.Count} identities."));
                    continue;
                }

                ParticipantIdentity identity = identities[0];
                var plannedRow = new PlannedRow(sheet.FileKey, row.PhysicalRowNumber, sourceRowKey, rowHash, identity, cycle);
                plan.Rows.Add(plannedRow);
                int calculatedTotal = 0;

                foreach (ColumnDefinition column in sheet.Columns)
                {
                    string raw = row[column.Header].Trim();
                    string cellKey = $"{sheet.FileKey}:row:{row.PhysicalRowNumber}:column:{column.Header}";
                    sheetReport.CellsAccounted++;
                    if (column.Kind is ColumnKind.Participant or ColumnKind.Total or ColumnKind.Structural) continue;
                    if (!TryNonNegativeInt(raw, out int value))
                    {
                        report.Errors.Add(new("InvalidNumericValue", $"{cellKey}: '{raw}' is not a non-negative integer or blank."));
                        continue;
                    }
                    if (column.Kind is ColumnKind.TaskApproval or ColumnKind.ManualAward or ColumnKind.RaidXp)
                    {
                        calculatedTotal += value;
                        if (value == 0) continue;
                        if (column.AwardedAt is null || string.IsNullOrWhiteSpace(column.AwardedAtEvidence))
                        {
                            report.Errors.Add(new("AwardedAtUnresolved", $"{cellKey}: XP-producing mapping has no defensible AwardedAt and evidence reference."));
                            continue;
                        }
                        if (!DateTimeOffset.TryParse(column.AwardedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset awardedAt))
                        {
                            report.Errors.Add(new("AwardedAtAmbiguous", $"{cellKey}: AwardedAt '{column.AwardedAt}' is invalid or ambiguous."));
                            continue;
                        }
                        if (column.Kind == ColumnKind.TaskApproval)
                        {
                            TaskDefinition? task = manifest.Tasks.SingleOrDefault(x => x.Key == column.MappingKey);
                            if (task is null) { report.Errors.Add(new("UnknownTaskMapping", $"{cellKey}: task '{column.MappingKey}' is unknown.")); continue; }
                            if (task.CycleKey != cycle.Key) { report.Errors.Add(new("ReportingCycleMismatch", $"{cellKey}: task belongs to {task.CycleKey}, not {cycle.Key}.")); continue; }
                            if (task.Xp != value) { report.Errors.Add(new("TaskAmountMismatch", $"{cellKey}: source value {value} does not equal task XP {task.Xp}.")); continue; }
                            if (column.SubmittedAt is null || string.IsNullOrWhiteSpace(column.SubmittedAtEvidence) || !DateTimeOffset.TryParse(column.SubmittedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
                            { report.Errors.Add(new("SubmittedAtUnresolved", $"{cellKey}: task approval has no defensible SubmittedAt and evidence reference.")); continue; }
                        }
                        else if (column.Kind == ColumnKind.ManualAward && !manifest.AwardCategories.Any(x => x.Key == column.MappingKey && (x.CycleKey is null || x.CycleKey == cycle.Key)))
                        { report.Errors.Add(new("UnknownAwardCategoryMapping", $"{cellKey}: award category '{column.MappingKey}' is unknown or belongs to another cycle.")); continue; }
                        else if (column.Kind == ColumnKind.RaidXp && !manifest.RaidSessions.Any(x => x.Key == column.MappingKey && x.CycleKey == cycle.Key))
                        { report.Errors.Add(new("UnknownRaidSessionMapping", $"{cellKey}: raid session '{column.MappingKey}' is unknown or belongs to another cycle.")); continue; }
                        plannedRow.XpCells.Add(new PlannedXpCell(cellKey, HashCell(raw, column), column, value, awardedAt));
                    }
                    else if (column.Kind is ColumnKind.RaidAssigned or ColumnKind.RaidUsed)
                    {
                        if (column.PassType is null) { report.Errors.Add(new("PassTypeMissing", $"{cellKey}: pass mapping has no type.")); continue; }
                        plannedRow.PassCells.Add(new PlannedPassCell(cellKey, HashCell(raw, column), column, value));
                        if (column.Kind == ColumnKind.RaidAssigned) report.RaidAssignedCells++;
                        else report.RaidUsedCells++;
                    }
                }

                if (!TryNonNegativeInt(row[totalColumn.Header].Trim(), out int expectedTotal))
                    report.Errors.Add(new("InvalidSourceTotal", $"{sourceRowKey}: total '{row[totalColumn.Header]}' is invalid."));
                else if (calculatedTotal != expectedTotal)
                    report.Errors.Add(new("SourceTotalMismatch", $"{sourceRowKey}: XP columns total {calculatedTotal}, source total is {expectedTotal}."));
                plannedRow.ExpectedTotal = expectedTotal;
                report.Participants.Add(new ParticipantReport(cycle.Key, identity.SourceKey, expectedTotal, calculatedTotal));
                foreach (PlannedXpCell taskCell in plannedRow.XpCells.Where(x => x.Column.Kind == ColumnKind.TaskApproval && x.AwardedAt.Month != cycle.StartsAt.Month))
                    report.CrossMonthAttributions.Add(new CrossMonthAttribution(taskCell.Column.Header, cycle.Key, taskCell.AwardedAt, identity.SourceKey));
            }
        }

        LoadRaidUsage(Path.Combine(root, manifest.RaidUsageEvidence), manifest, plan, report);
        ReconcileRaidUsage(plan, report);
        await DetectChangedSourcesAsync(db, plan, report);
        return plan;
    }

    private static void BuildCanonicalProvenance(ImportManifest manifest, string root, ImportPlan plan)
    {
        JsonNode manifestNode = JsonSerializer.SerializeToNode(manifest, JsonOptions)!;
        RemoveRepresentationOnlyProperties(manifestNode);
        JsonNode canonicalManifest = SortJson(manifestNode);
        plan.Observations.Add(new("ManifestMetadata", "manifest", HashCanonical(canonicalManifest)));

        var sourceFiles = new JsonArray();
        AddCsvProvenance("ParticipantMap", "participant-map", Path.Combine(root, manifest.ParticipantMap), sourceFiles, plan);
        foreach (SheetDefinition sheet in manifest.Sheets)
        {
            string path = Path.Combine(root, sheet.Path);
            CsvDocument csv = CsvDocument.Read(path);
            AddCsvProvenance("SourceSheet", sheet.FileKey, path, sourceFiles, plan);
            foreach (CsvRow row in csv.Rows)
            foreach (ColumnDefinition column in sheet.Columns)
            {
                string cellKey = $"{sheet.FileKey}:row:{row.PhysicalRowNumber}:column:{column.Header}";
                var semanticCell = new JsonObject
                {
                    ["header"] = column.Header, ["kind"] = column.Kind.ToString(), ["mappingKey"] = column.MappingKey,
                    ["passType"] = column.PassType, ["ignoreReason"] = column.IgnoreReason, ["value"] = row[column.Header],
                    ["submittedAt"] = NormalizeTimestamp(column.SubmittedAt), ["submittedAtEvidence"] = column.SubmittedAtEvidence,
                    ["awardedAt"] = NormalizeTimestamp(column.AwardedAt), ["awardedAtEvidence"] = column.AwardedAtEvidence
                };
                plan.Observations.Add(new("MappedCell", cellKey, HashCanonical(semanticCell)));
                if (column.Kind == ColumnKind.TaskApproval)
                    plan.Observations.Add(new("SubmissionEventProvenance", cellKey, HashCanonical(new JsonObject
                    {
                        ["submittedAt"] = NormalizeTimestamp(column.SubmittedAt), ["submittedEvidence"] = column.SubmittedAtEvidence,
                        ["awardedAt"] = NormalizeTimestamp(column.AwardedAt), ["awardedEvidence"] = column.AwardedAtEvidence,
                        ["claimant"] = row[sheet.Columns.Single(x => x.Kind == ColumnKind.Participant).Header],
                        ["manager"] = manifest.Manager.SourceKey
                    })));
            }
        }
        AddCsvProvenance("RaidUsage", "raid-usage", Path.Combine(root, manifest.RaidUsageEvidence), sourceFiles, plan);

        JsonNode canonical = SortJson(new JsonObject
        {
            ["canonicalizationVersion"] = CanonicalizationVersion,
            ["manifest"] = canonicalManifest.DeepClone(),
            ["authoritativeSources"] = sourceFiles
        });
        plan.CanonicalFingerprint = HashCanonical(canonical);
        plan.BoundaryKey = Hash(Encoding.UTF8.GetBytes(string.Join("\u001f", manifest.Sheets.Select(x => x.FileKey).Order(StringComparer.Ordinal))));
    }

    private static void AddCsvProvenance(string category, string key, string path, JsonArray sources, ImportPlan plan)
    {
        CsvDocument csv = CsvDocument.Read(path);
        var rows = new JsonArray(csv.Rows.Select(row => (JsonNode)new JsonArray(csv.Headers.Select(h => (JsonNode?)JsonValue.Create(row[h])).ToArray())).ToArray());
        var document = new JsonObject { ["key"] = key, ["headers"] = new JsonArray(csv.Headers.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()), ["rows"] = rows };
        sources.Add(document.DeepClone());
        plan.Observations.Add(new(category, key, HashCanonical(document)));
    }

    private static async Task DetectDatasetConflictAsync(QuestDbContext db, ImportManifest manifest, ImportPlan plan, ReconciliationReport report)
    {
        HistoricalImportDataset? byKey = await db.HistoricalImportDatasets.AsNoTracking().SingleOrDefaultAsync(x => x.DatasetKey == manifest.DatasetKey);
        if (byKey is not null && (byKey.CanonicalizationVersion != CanonicalizationVersion || byKey.CanonicalFingerprint != plan.CanonicalFingerprint))
            report.Errors.Add(new("DatasetProvenanceConflict", $"DatasetKey '{manifest.DatasetKey}' is already bound to different canonical semantics."));
        HistoricalImportDataset? byBoundary = await db.HistoricalImportDatasets.AsNoTracking().SingleOrDefaultAsync(x => x.BoundaryKey == plan.BoundaryKey);
        if (byBoundary is not null && byBoundary.DatasetKey != manifest.DatasetKey)
            report.Errors.Add(new("DatasetKeySubstitution", $"Source boundary is already registered as DatasetKey '{byBoundary.DatasetKey}'; changing DatasetKey is not a correction workflow."));
    }

    private static void RemoveRepresentationOnlyProperties(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            foreach (string key in obj.Select(x => x.Key).ToArray())
            {
                if (key is "datasetKey" or "path" or "participantMap" or "raidUsageEvidence") obj.Remove(key);
                else if (obj[key] is not null) RemoveRepresentationOnlyProperties(obj[key]!);
            }
        }
        else if (node is JsonArray array) foreach (JsonNode? child in array) if (child is not null) RemoveRepresentationOnlyProperties(child);
    }

    private static JsonNode SortJson(JsonNode node) => node switch
    {
        JsonObject obj => new JsonObject(obj.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => KeyValuePair.Create(x.Key, x.Value is null ? null : SortJson(x.Value))).ToArray()),
        JsonArray array => new JsonArray(array.Select(x => x is null ? null : SortJson(x)).ToArray()),
        _ => node.DeepClone()
    };
    private static string? NormalizeTimestamp(string? value) => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed) ? parsed.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) : value;
    private static string HashCanonical(JsonNode node) => Hash(Encoding.UTF8.GetBytes(node.ToJsonString(new JsonSerializerOptions { WriteIndented = false })));

    private static void ValidateManifest(ImportManifest manifest, ReconciliationReport report)
    {
        if (string.IsNullOrWhiteSpace(manifest.DatasetKey)) report.Errors.Add(new("ManifestInvalid", "DatasetKey is required."));
        if (manifest.Sheets.Count == 0) report.Errors.Add(new("ManifestInvalid", "At least one sheet is required."));
        foreach (SheetDefinition sheet in manifest.Sheets)
        {
            if (sheet.Columns.Count(x => x.Kind == ColumnKind.Participant) != 1) report.Errors.Add(new("ManifestInvalid", $"{sheet.FileKey} must have exactly one Participant column."));
            if (sheet.Columns.Count(x => x.Kind == ColumnKind.Total) != 1) report.Errors.Add(new("ManifestInvalid", $"{sheet.FileKey} must have exactly one Total column."));
        }
        foreach (ColumnDefinition column in manifest.Sheets.SelectMany(x => x.Columns).Where(x => x.Kind == ColumnKind.Structural && string.IsNullOrWhiteSpace(x.IgnoreReason)))
            report.Errors.Add(new("ManifestInvalid", $"Structural column '{column.Header}' requires an explicit reason."));
    }

    private static void LoadIdentityMap(string path, ImportPlan plan, ReconciliationReport report)
    {
        CsvDocument csv;
        try { csv = CsvDocument.Read(path); }
        catch (Exception ex) { report.Errors.Add(new("IdentityMapReadFailure", ex.Message)); return; }
        string[] expected = ["SourceKey", "SourceName", "ParticipantId", "DisplayName"];
        if (!csv.Headers.SequenceEqual(expected, StringComparer.Ordinal)) { report.Errors.Add(new("IdentityMapHeaderMismatch", $"Expected [{string.Join(",", expected)}].")); return; }
        foreach (CsvRow row in csv.Rows)
        {
            if (!Guid.TryParse(row["ParticipantId"], out Guid participantId)) { report.Errors.Add(new("IdentityMapInvalid", $"Row {row.PhysicalRowNumber} has invalid ParticipantId.")); continue; }
            var identity = new ParticipantIdentity(row["SourceKey"], row["SourceName"], participantId, row["DisplayName"]);
            plan.Participants[identity.SourceKey] = identity;
            if (!plan.Aliases.TryGetValue(identity.SourceName, out List<ParticipantIdentity>? aliases)) plan.Aliases[identity.SourceName] = aliases = [];
            aliases.Add(identity);
        }
    }

    private static void LoadRaidUsage(string path, ImportManifest manifest, ImportPlan plan, ReconciliationReport report)
    {
        CsvDocument csv;
        try { csv = CsvDocument.Read(path); }
        catch (Exception ex) { report.Errors.Add(new("RaidEvidenceReadFailure", ex.Message)); return; }
        string[] expected = ["ParticipantSourceKey", "RaidSessionKey", "PassType", "UsedAt", "EvidenceKey"];
        if (!csv.Headers.SequenceEqual(expected, StringComparer.Ordinal)) { report.Errors.Add(new("RaidEvidenceHeaderMismatch", $"Expected [{string.Join(",", expected)}].")); return; }
        foreach (CsvRow row in csv.Rows)
        {
            if (!plan.Participants.TryGetValue(row["ParticipantSourceKey"], out ParticipantIdentity? participant)) { report.Errors.Add(new("UnmappedParticipant", $"Raid evidence row {row.PhysicalRowNumber} has unknown participant key.")); continue; }
            RaidSessionDefinition? session = manifest.RaidSessions.SingleOrDefault(x => x.Key == row["RaidSessionKey"]);
            if (session is null || string.IsNullOrWhiteSpace(session.EvidenceKey)) { report.Errors.Add(new("RaidSessionUnsupported", $"Raid evidence row {row.PhysicalRowNumber} has no source-supported session.")); continue; }
            if (!Enum.TryParse(row["PassType"], true, out PassType passType)) { report.Errors.Add(new("RaidPassTypeInvalid", $"Raid evidence row {row.PhysicalRowNumber} has invalid pass type.")); continue; }
            if (!DateTimeOffset.TryParse(row["UsedAt"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset usedAt) || string.IsNullOrWhiteSpace(row["EvidenceKey"])) { report.Errors.Add(new("RaidUsageUnsupported", $"Raid evidence row {row.PhysicalRowNumber} lacks a defensible timestamp/evidence key.")); continue; }
            plan.RaidUsages.Add(new PlannedRaidUsage(participant, session, passType, usedAt, row["EvidenceKey"]));
        }
    }

    private static void ReconcileRaidUsage(ImportPlan plan, ReconciliationReport report)
    {
        foreach (PlannedRow row in plan.Rows)
        foreach (PlannedPassCell cell in row.PassCells.Where(x => x.Column.Kind == ColumnKind.RaidUsed))
        {
            PassType passType = Enum.Parse<PassType>(cell.Column.PassType!, true);
            int supported = plan.RaidUsages.Count(x => x.Participant.SourceKey == row.Participant.SourceKey && x.Session.CycleKey == row.Cycle.Key && x.PassType == passType);
            if (supported != cell.Value)
                report.Errors.Add(new("RaidUsageUnaccounted", $"{cell.CellKey}: expected {cell.Value} supported uses, found {supported}."));
        }
    }

    private static async Task DetectChangedSourcesAsync(QuestDbContext db, ImportPlan plan, ReconciliationReport report)
    {
        string[] rowKeys = plan.Rows.Select(x => x.SourceRowKey).ToArray();
        Guid datasetId = DeterministicGuid(plan.DatasetKey, "dataset");
        var existingRows = await db.HistoricalImportSourceRows.AsNoTracking().Where(x => x.DatasetId == datasetId && rowKeys.Contains(x.SourceRowKey)).ToListAsync();
        foreach (HistoricalImportSourceRow existing in existingRows)
        {
            PlannedRow current = plan.Rows.Single(x => x.SourceRowKey == existing.SourceRowKey);
            if (existing.RowHash != current.RowHash) report.Errors.Add(new("ChangedSourceConflict", $"{current.SourceRowKey}: source hash differs from the previously imported row."));
        }
        Dictionary<string, string> currentCells = plan.Rows
            .SelectMany(x => x.XpCells.Select(c => (c.CellKey, c.SourceHash)).Concat(x.PassCells.Select(c => (c.CellKey, c.SourceHash))))
            .ToDictionary(x => x.CellKey, x => x.SourceHash, StringComparer.Ordinal);
        string[] cellKeys = currentCells.Keys.ToArray();
        var existingArtifacts = await db.HistoricalImportArtifacts.AsNoTracking()
            .Where(x => x.DatasetId == datasetId && cellKeys.Contains(x.SourceCellKey)).ToListAsync();
        foreach (HistoricalImportArtifact artifact in existingArtifacts)
            if (currentCells[artifact.SourceCellKey] != artifact.SourceHash)
                report.Errors.Add(new("ChangedSourceConflict", $"{artifact.SourceCellKey}: mapped source content differs from the previously imported artifact."));
    }

    private static async Task PersistAndReconcileAsync(QuestDbContext db, ImportManifest manifest, ImportPlan plan, ReconciliationReport report)
    {
        await using IDbContextTransaction tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        try
        {
            Guid datasetId = DeterministicGuid(plan.DatasetKey, "dataset");
            HistoricalImportDataset? dataset = await db.HistoricalImportDatasets.FindAsync(datasetId);
            if (dataset is null)
            {
                dataset = new HistoricalImportDataset { Id = datasetId, DatasetKey = plan.DatasetKey, BoundaryKey = plan.BoundaryKey, CanonicalFingerprint = plan.CanonicalFingerprint, CanonicalizationVersion = CanonicalizationVersion, EstablishedAt = report.StartedAt };
                db.HistoricalImportDatasets.Add(dataset);
            }
            else if (dataset.CanonicalFingerprint != plan.CanonicalFingerprint || dataset.CanonicalizationVersion != CanonicalizationVersion)
            {
                report.Errors.Add(new("DatasetProvenanceConflict", $"DatasetKey '{plan.DatasetKey}' changed while acquiring the import transaction."));
                await tx.RollbackAsync(); return;
            }

            Guid runId = DeterministicGuid(plan.DatasetKey, "run", plan.CanonicalFingerprint);
            HistoricalImportRun? run = await db.HistoricalImportRuns.FindAsync(runId);
            bool unchangedRerun = run is not null;
            if (run is null)
            {
                run = new HistoricalImportRun { Id = runId, DatasetId = datasetId, InputFingerprint = plan.CanonicalFingerprint, Status = "Running", StartedAt = report.StartedAt, Summary = "Historical import in progress." };
                db.HistoricalImportRuns.Add(run);
            }
            foreach (PlannedObservation observation in plan.Observations)
                await AddObservationIfMissingAsync(db, datasetId, runId, observation);

            ParticipantIdentity managerIdentity = manifest.Manager;
            await AddIfMissingAsync(db.Participants, managerIdentity.ParticipantId, () => new Participant { Id = managerIdentity.ParticipantId, DisplayName = managerIdentity.DisplayName, CreatedAt = manifest.ImportRecordedAt });
            foreach (ParticipantIdentity p in plan.Participants.Values.DistinctBy(x => x.ParticipantId))
                await AddIfMissingAsync(db.Participants, p.ParticipantId, () => new Participant { Id = p.ParticipantId, DisplayName = p.DisplayName, CreatedAt = manifest.ImportRecordedAt });

            foreach (CycleDefinition c in manifest.Cycles)
            {
                Guid id = Id(plan, "cycle", c.Key);
                await AddIfMissingAsync(db.Cycles, id, () => new Cycle { Id = id, Code = c.Code, Name = c.Name, Status = CycleStatus.Finalised, StartsAt = c.StartsAt, EndsAt = c.EndsAt, CreatedAt = manifest.ImportRecordedAt, CreatedByParticipantId = managerIdentity.ParticipantId });
            }
            await db.SaveChangesAsync();

            foreach (PlannedRow row in plan.Rows)
            {
                Guid cycleId = Id(plan, "cycle", row.Cycle.Key);
                var cpKey = new object[] { cycleId, row.Participant.ParticipantId };
                if (await db.CycleParticipants.FindAsync(cpKey) is null)
                {
                    db.CycleParticipants.Add(new CycleParticipant { CycleId = cycleId, ParticipantId = row.Participant.ParticipantId, Status = CycleParticipantStatus.Active, JoinedAt = row.Cycle.StartsAt });
                    db.CycleParticipantEvents.Add(new CycleParticipantEvent
                    {
                        Id = Id(plan, "cycle-participant-enrolled", $"{row.Cycle.Key}:{row.Participant.ParticipantId:N}"),
                        CycleId = cycleId,
                        ParticipantId = row.Participant.ParticipantId,
                        SequenceNumber = 1,
                        EventType = CycleParticipantEventType.Enrolled,
                        FromStatus = null,
                        ToStatus = CycleParticipantStatus.Active,
                        Reason = "Historical import enrollment",
                        ActorId = managerIdentity.ParticipantId,
                        OccurredAt = row.Cycle.StartsAt
                    });
                }
            }

            foreach (ChallengeDefinition c in manifest.Challenges)
            {
                Guid id = Id(plan, "challenge", c.Key); Guid cycleId = Id(plan, "cycle", c.CycleKey);
                await AddIfMissingAsync(db.Challenges, id, () => new Challenge { Id = id, CycleId = cycleId, Name = c.Name, Description = c.Description, Category = c.Category, Status = ChallengeStatus.Archived, OpenAt = c.OpenAt, DueAt = c.DueAt, CloseAt = c.CloseAt, CreatedAt = manifest.ImportRecordedAt, CreatedByParticipantId = managerIdentity.ParticipantId });
            }
            await db.SaveChangesAsync();

            foreach (TaskDefinition t in manifest.Tasks)
            {
                Guid id = Id(plan, "task", t.Key); Guid challengeId = Id(plan, "challenge", t.ChallengeKey);
                await AddIfMissingAsync(db.ChallengeTasks, id, () => new ChallengeTask { Id = id, ChallengeId = challengeId, Name = t.Name, XP = t.Xp, EvidenceRequirement = EvidenceRequirement.None, ScoringMode = ScoringMode.Individual, SortOrder = t.SortOrder });
            }
            foreach (AwardCategoryDefinition a in manifest.AwardCategories)
            {
                Guid id = Id(plan, "award-category", a.Key); Guid? cycleId = a.CycleKey is null ? null : Id(plan, "cycle", a.CycleKey);
                await AddIfMissingAsync(db.AwardCategories, id, () => new AwardCategory { Id = id, CycleId = cycleId, Code = a.Code, Name = a.Name, IsActive = false });
            }
            foreach (RaidSessionDefinition s in manifest.RaidSessions)
            {
                Guid id = Id(plan, "raid-session", s.Key); Guid cycleId = Id(plan, "cycle", s.CycleKey);
                await AddIfMissingAsync(db.RaidSessions, id, () => new RaidSession { Id = id, CycleId = cycleId, Name = s.Name, OccurredAt = s.OccurredAt });
            }
            await db.SaveChangesAsync();

            foreach (PlannedRow row in plan.Rows)
            {
                Guid cycleId = Id(plan, "cycle", row.Cycle.Key);
                foreach (PlannedXpCell cell in row.XpCells)
                {
                    Guid xpId = Id(plan, "xp", cell.CellKey);
                    if (cell.Column.Kind == ColumnKind.TaskApproval)
                    {
                        TaskDefinition task = manifest.Tasks.Single(x => x.Key == cell.Column.MappingKey);
                        Guid challengeId = Id(plan, "challenge", task.ChallengeKey); Guid taskId = Id(plan, "task", task.Key); Guid submissionId = Id(plan, "submission", cell.CellKey);
                        DateTimeOffset submittedAt = DateTimeOffset.Parse(cell.Column.SubmittedAt!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                        await AddIfMissingAsync(db.Submissions, submissionId, () => new Submission { Id = submissionId, CycleId = cycleId, ClaimantId = row.Participant.ParticipantId, ChallengeId = challengeId, TaskId = taskId, Status = SubmissionStatus.Approved, ReviewerComment = $"Historical import; submission evidence {cell.Column.SubmittedAtEvidence}", SubmittedAt = submittedAt, LastUpdatedAt = cell.AwardedAt });
                        await db.SaveChangesAsync();
                        await AddIfMissingAsync(db.SubmissionBeneficiaries, new object[] { submissionId, row.Participant.ParticipantId }, () => new SubmissionBeneficiary { SubmissionId = submissionId, ParticipantId = row.Participant.ParticipantId, CycleId = cycleId, AddedAt = cell.AwardedAt, AddedByParticipantId = managerIdentity.ParticipantId });
                        await db.SaveChangesAsync();
                        Guid submittedEventId = Id(plan, "submission-event-submitted", cell.CellKey);
                        Guid approvedEventId = Id(plan, "submission-event-approved", cell.CellKey);
                        await AddIfMissingAsync(db.SubmissionEvents, submittedEventId, () => new SubmissionEvent { Id = submittedEventId, SubmissionId = submissionId, EventType = "Submitted", FromStatus = null, ToStatus = SubmissionStatus.Submitted, Comment = cell.Column.SubmittedAtEvidence, ActorId = row.Participant.ParticipantId, OccurredAt = submittedAt });
                        await AddIfMissingAsync(db.SubmissionEvents, approvedEventId, () => new SubmissionEvent { Id = approvedEventId, SubmissionId = submissionId, EventType = "Approved", FromStatus = SubmissionStatus.Submitted, ToStatus = SubmissionStatus.Approved, Comment = cell.Column.AwardedAtEvidence, ActorId = managerIdentity.ParticipantId, OccurredAt = cell.AwardedAt });
                        await AddIfMissingAsync(db.XPEntries, xpId, () => new XPEntry { Id = xpId, ParticipantId = row.Participant.ParticipantId, CycleId = cycleId, Amount = cell.Value, EntryType = XPEntryType.Grant, SourceType = XPSourceType.TaskApproval, ChallengeId = challengeId, TaskId = taskId, SubmissionId = submissionId, Reason = $"Historical task approval; evidence {cell.Column.AwardedAtEvidence}", AwardedByParticipantId = managerIdentity.ParticipantId, AwardedAt = cell.AwardedAt });
                    }
                    else if (cell.Column.Kind == ColumnKind.ManualAward)
                    {
                        Guid categoryId = Id(plan, "award-category", cell.Column.MappingKey!);
                        await AddIfMissingAsync(db.XPEntries, xpId, () => new XPEntry { Id = xpId, ParticipantId = row.Participant.ParticipantId, CycleId = cycleId, Amount = cell.Value, EntryType = XPEntryType.Grant, SourceType = XPSourceType.ManualAward, AwardCategoryId = categoryId, Reason = $"Historical manual award; evidence {cell.Column.AwardedAtEvidence}", AwardedByParticipantId = managerIdentity.ParticipantId, AwardedAt = cell.AwardedAt });
                    }
                    else
                    {
                        Guid sessionId = Id(plan, "raid-session", cell.Column.MappingKey!);
                        await AddIfMissingAsync(db.XPEntries, xpId, () => new XPEntry { Id = xpId, ParticipantId = row.Participant.ParticipantId, CycleId = cycleId, Amount = cell.Value, EntryType = XPEntryType.Grant, SourceType = XPSourceType.Raid, RaidSessionId = sessionId, Reason = $"Historical raid XP; evidence {cell.Column.AwardedAtEvidence}", AwardedByParticipantId = managerIdentity.ParticipantId, AwardedAt = cell.AwardedAt });
                    }
                    await AddArtifactIfMissingAsync(db, datasetId, runId, plan.DatasetKey, cell.CellKey, "XPEntry", xpId, cell.SourceHash);
                }

                foreach (PlannedPassCell cell in row.PassCells.Where(x => x.Column.Kind == ColumnKind.RaidAssigned))
                {
                    PassType passType = Enum.Parse<PassType>(cell.Column.PassType!, true);
                    var key = new object[] { row.Participant.ParticipantId, cycleId, passType };
                    await AddIfMissingAsync(db.RaidEntitlements, key, () => new RaidEntitlement { ParticipantId = row.Participant.ParticipantId, CycleId = cycleId, PassType = passType, AssignedCount = cell.Value });
                    await AddArtifactIfMissingAsync(db, datasetId, runId, plan.DatasetKey, cell.CellKey, "RaidEntitlement", Id(plan, "entitlement", cell.CellKey), cell.SourceHash);
                }
                await AddSourceRowIfMissingAsync(db, datasetId, runId, plan.DatasetKey, row);
            }

            foreach (PlannedRaidUsage usage in plan.RaidUsages)
            {
                Guid id = Id(plan, "raid-participation", $"{usage.Participant.SourceKey}:{usage.Session.Key}:{usage.PassType}:{usage.EvidenceKey}");
                Guid cycleId = Id(plan, "cycle", usage.Session.CycleKey); Guid sessionId = Id(plan, "raid-session", usage.Session.Key);
                await AddIfMissingAsync(db.RaidParticipations, id, () => new RaidParticipation { Id = id, ParticipantId = usage.Participant.ParticipantId, CycleId = cycleId, RaidSessionId = sessionId, PassType = usage.PassType, UsedAt = usage.UsedAt });
            }
            await db.SaveChangesAsync();

            Guid[] importedXpIds = plan.Rows.SelectMany(x => x.XpCells).Select(x => Id(plan, "xp", x.CellKey)).ToArray();
            foreach (ParticipantReport participant in report.Participants)
            {
                ParticipantIdentity identity = plan.Participants[participant.ParticipantSourceKey];
                Guid cycleId = Id(plan, "cycle", participant.CycleKey);
                int persisted = await db.XPEntries.Where(x => importedXpIds.Contains(x.Id) && x.CycleId == cycleId && x.ParticipantId == identity.ParticipantId).SumAsync(x => (int?)x.Amount) ?? 0;
                participant.PersistedTotal = persisted;
                if (persisted != participant.ExpectedTotal) report.Errors.Add(new("DatabaseTotalMismatch", $"{participant.CycleKey}/{participant.ParticipantSourceKey}: expected {participant.ExpectedTotal}, persisted {persisted}."));
            }
            report.XpSourceTotals = await db.XPEntries.Where(x => importedXpIds.Contains(x.Id)).GroupBy(x => x.SourceType).ToDictionaryAsync(x => x.Key.ToString(), x => x.Sum(y => y.Amount));

            foreach (PlannedRow row in plan.Rows)
            foreach (PlannedPassCell cell in row.PassCells.Where(x => x.Column.Kind == ColumnKind.RaidUsed))
            {
                PassType passType = Enum.Parse<PassType>(cell.Column.PassType!, true); Guid cycleId = Id(plan, "cycle", row.Cycle.Key);
                int persisted = await db.RaidParticipations.CountAsync(x => x.CycleId == cycleId && x.ParticipantId == row.Participant.ParticipantId && x.PassType == passType);
                if (persisted != cell.Value) report.Errors.Add(new("DatabaseRaidUsageMismatch", $"{cell.CellKey}: expected {cell.Value}, persisted {persisted}."));
            }

            if (report.Errors.Count > 0) { await tx.RollbackAsync(); return; }
            run.Status = "Succeeded"; run.CompletedAt = DateTimeOffset.UtcNow; run.Summary = $"Imported/reconciled {plan.Rows.Count} source rows.";
            report.UnchangedRerun = unchangedRerun;
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch { await tx.RollbackAsync(); throw; }
    }

    private static async Task AddArtifactIfMissingAsync(QuestDbContext db, Guid datasetId, Guid runId, string dataset, string cellKey, string type, Guid artifactId, string hash)
    {
        if (!await db.HistoricalImportArtifacts.AnyAsync(x => x.DatasetId == datasetId && x.SourceCellKey == cellKey && x.ArtifactType == type))
            db.HistoricalImportArtifacts.Add(new HistoricalImportArtifact { Id = DeterministicGuid(dataset, "artifact", cellKey, type), DatasetId = datasetId, RunId = runId, SourceCellKey = cellKey, ArtifactType = type, ArtifactId = artifactId, SourceHash = hash });
    }

    private static async Task AddSourceRowIfMissingAsync(QuestDbContext db, Guid datasetId, Guid runId, string dataset, PlannedRow row)
    {
        if (!await db.HistoricalImportSourceRows.AnyAsync(x => x.DatasetId == datasetId && x.FileKey == row.FileKey && x.SourceRowKey == row.SourceRowKey))
            db.HistoricalImportSourceRows.Add(new HistoricalImportSourceRow { Id = DeterministicGuid(dataset, "source-row", row.SourceRowKey), DatasetId = datasetId, RunId = runId, FileKey = row.FileKey, PhysicalRowNumber = row.PhysicalRowNumber, SourceRowKey = row.SourceRowKey, RowHash = row.RowHash, Outcome = "Imported" });
    }

    private static async Task AddObservationIfMissingAsync(QuestDbContext db, Guid datasetId, Guid runId, PlannedObservation observation)
    {
        if (!await db.HistoricalImportObservations.AnyAsync(x => x.DatasetId == datasetId && x.Category == observation.Category && x.ObservationKey == observation.Key))
            db.HistoricalImportObservations.Add(new HistoricalImportObservation { Id = DeterministicGuid(datasetId.ToString(), "observation", observation.Category, observation.Key), DatasetId = datasetId, RunId = runId, Category = observation.Category, ObservationKey = observation.Key, ContentHash = observation.Hash });
    }

    private static async Task AddIfMissingAsync<TEntity>(DbSet<TEntity> set, object key, Func<TEntity> factory) where TEntity : class
    {
        object[] keys = key as object[] ?? [key];
        if (await set.FindAsync(keys) is null) set.Add(factory());
    }

    private static string RenderHumanReport(ReconciliationReport r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Historical import reconciliation").AppendLine();
        sb.AppendLine($"- Dataset: `{r.DatasetKey}`"); sb.AppendLine($"- Status: **{(r.Succeeded ? "PASS" : "FAIL")}**");
        sb.AppendLine($"- Header fidelity: {r.HeaderFidelity}"); sb.AppendLine($"- Unchanged rerun: {r.UnchangedRerun}"); sb.AppendLine();
        sb.AppendLine($"- Raid Assigned cells accounted: {r.RaidAssignedCells}"); sb.AppendLine($"- Raid Used cells accounted: {r.RaidUsedCells}");
        sb.AppendLine($"- Raid pass columns included in XP: **{r.RaidPassXpContribution}**");
        foreach (CrossMonthAttribution item in r.CrossMonthAttributions) sb.AppendLine($"- Cross-month attribution: {item.SourceColumn} awarded {item.AwardedAt:O} reports to `{item.ReportingCycleKey}` for `{item.ParticipantSourceKey}`");
        sb.AppendLine();
        sb.AppendLine("| Cycle | Participant key | Source total | Pre-write total | Persisted total |").AppendLine("|---|---|---:|---:|---:|");
        foreach (ParticipantReport p in r.Participants) sb.AppendLine($"| {p.CycleKey} | {p.ParticipantSourceKey} | {p.ExpectedTotal} | {p.CalculatedTotal} | {p.PersistedTotal?.ToString(CultureInfo.InvariantCulture) ?? "-"} |");
        if (r.Errors.Count > 0) { sb.AppendLine().AppendLine("## Failures").AppendLine(); foreach (ReconciliationError e in r.Errors) sb.AppendLine($"- `{e.Code}`: {e.Message}"); }
        return sb.ToString();
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i += 2)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length) throw new ArgumentException($"Invalid option near '{args[i]}'.");
            result[args[i][2..]] = args[i + 1];
        }
        return result;
    }

    private static bool TryNonNegativeInt(string value, out int parsed)
    {
        if (string.IsNullOrWhiteSpace(value)) { parsed = 0; return true; }
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed) && parsed >= 0;
    }
    private static string HashCell(string raw, ColumnDefinition c) => Hash(Encoding.UTF8.GetBytes($"{c.Header}\u001f{c.Kind}\u001f{c.MappingKey}\u001f{raw}\u001f{c.SubmittedAt}\u001f{c.SubmittedAtEvidence}\u001f{c.AwardedAt}\u001f{c.AwardedAtEvidence}"));
    private static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
    private static Guid Id(ImportPlan p, string type, string key) => DeterministicGuid(p.DatasetKey, type, key);
    private static Guid DeterministicGuid(params string[] parts)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\u001f", parts)));
        Span<byte> bytes = hash.AsSpan(0, 16); bytes[6] = (byte)((bytes[6] & 0x0f) | 0x50); bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80); return new Guid(bytes);
    }
}

public sealed record HistoricalImportResult(bool Succeeded, ReconciliationReport Report, string HumanReport);
public sealed class ReconciliationReport
{
    public required string DatasetKey { get; set; } public required string ManifestHash { get; set; } public required string HeaderFidelity { get; set; }
    public DateTimeOffset StartedAt { get; set; } public DateTimeOffset CompletedAt { get; set; } public bool Succeeded { get; set; } public bool UnchangedRerun { get; set; }
    public int RaidAssignedCells { get; set; } public int RaidUsedCells { get; set; } public int RaidPassXpContribution { get; set; }
    public Dictionary<string, int> XpSourceTotals { get; set; } = []; public List<CrossMonthAttribution> CrossMonthAttributions { get; } = [];
    public List<SheetReport> Sheets { get; } = []; public List<ParticipantReport> Participants { get; } = []; public List<ReconciliationError> Errors { get; } = [];
}
public sealed record SheetReport(string FileKey, string CycleKey, int ParticipantRows) { public int CellsAccounted { get; set; } }
public sealed record ParticipantReport(string CycleKey, string ParticipantSourceKey, int ExpectedTotal, int CalculatedTotal) { public int? PersistedTotal { get; set; } }
public sealed record ReconciliationError(string Code, string Message);
public sealed record CrossMonthAttribution(string SourceColumn, string ReportingCycleKey, DateTimeOffset AwardedAt, string ParticipantSourceKey);

public sealed class ImportManifest
{
    public required string DatasetKey { get; set; } public required string HeaderFidelity { get; set; } public required string ParticipantMap { get; set; } public required string RaidUsageEvidence { get; set; }
    public DateTimeOffset ImportRecordedAt { get; set; } public required ParticipantIdentity Manager { get; set; }
    public List<CycleDefinition> Cycles { get; set; } = []; public List<ChallengeDefinition> Challenges { get; set; } = []; public List<TaskDefinition> Tasks { get; set; } = [];
    public List<AwardCategoryDefinition> AwardCategories { get; set; } = []; public List<RaidSessionDefinition> RaidSessions { get; set; } = []; public List<SheetDefinition> Sheets { get; set; } = [];
}
public sealed record ParticipantIdentity(string SourceKey, string SourceName, Guid ParticipantId, string DisplayName);
public sealed record CycleDefinition(string Key, string Code, string Name, DateTimeOffset StartsAt, DateTimeOffset EndsAt);
public sealed record ChallengeDefinition(string Key, string CycleKey, string Name, string Description, string Category, DateTimeOffset OpenAt, DateTimeOffset DueAt, DateTimeOffset CloseAt);
public sealed record TaskDefinition(string Key, string ChallengeKey, string CycleKey, string Name, int Xp, int SortOrder);
public sealed record AwardCategoryDefinition(string Key, string? CycleKey, string Code, string Name);
public sealed record RaidSessionDefinition(string Key, string CycleKey, string Name, DateTimeOffset OccurredAt, string EvidenceKey);
public sealed class SheetDefinition { public required string FileKey { get; set; } public required string Path { get; set; } public required string CycleKey { get; set; } public List<string> ExpectedHeaders { get; set; } = []; public List<ColumnDefinition> Columns { get; set; } = []; }
public sealed class ColumnDefinition
{
    public required string Header { get; set; } public ColumnKind Kind { get; set; } public string? MappingKey { get; set; } public string? PassType { get; set; }
    public string? SubmittedAt { get; set; } public string? SubmittedAtEvidence { get; set; }
    public string? AwardedAt { get; set; } public string? AwardedAtEvidence { get; set; } public string? IgnoreReason { get; set; }
}
public enum ColumnKind { Participant, Structural, TaskApproval, ManualAward, RaidXp, RaidAssigned, RaidUsed, Total }

internal sealed class ImportPlan(string datasetKey)
{
    public string DatasetKey { get; } = datasetKey; public Dictionary<string, ParticipantIdentity> Participants { get; } = [];
    public Dictionary<string, List<ParticipantIdentity>> Aliases { get; } = new(StringComparer.Ordinal); public List<PlannedRow> Rows { get; } = []; public List<PlannedRaidUsage> RaidUsages { get; } = [];
    public string CanonicalFingerprint { get; set; } = ""; public string BoundaryKey { get; set; } = ""; public List<PlannedObservation> Observations { get; } = [];
}
internal sealed record PlannedObservation(string Category, string Key, string Hash);
internal sealed record PlannedRow(string FileKey, int PhysicalRowNumber, string SourceRowKey, string RowHash, ParticipantIdentity Participant, CycleDefinition Cycle)
{ public int ExpectedTotal { get; set; } public List<PlannedXpCell> XpCells { get; } = []; public List<PlannedPassCell> PassCells { get; } = []; }
internal sealed record PlannedXpCell(string CellKey, string SourceHash, ColumnDefinition Column, int Value, DateTimeOffset AwardedAt);
internal sealed record PlannedPassCell(string CellKey, string SourceHash, ColumnDefinition Column, int Value);
internal sealed record PlannedRaidUsage(ParticipantIdentity Participant, RaidSessionDefinition Session, PassType PassType, DateTimeOffset UsedAt, string EvidenceKey);

internal sealed class CsvDocument
{
    public required IReadOnlyList<string> Headers { get; init; } public required IReadOnlyList<CsvRow> Rows { get; init; }
    public static CsvDocument Read(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, true);
        var records = new List<List<string>>(); var field = new StringBuilder(); var record = new List<string>(); bool quoted = false; int value;
        while ((value = reader.Read()) >= 0)
        {
            char c = (char)value;
            if (quoted && c == '"') { if (reader.Peek() == '"') { reader.Read(); field.Append('"'); } else quoted = false; }
            else if (!quoted && c == '"' && field.Length == 0) quoted = true;
            else if (!quoted && c == ',') { record.Add(field.ToString()); field.Clear(); }
            else if (!quoted && (c == '\r' || c == '\n')) { if (c == '\r' && reader.Peek() == '\n') reader.Read(); record.Add(field.ToString()); field.Clear(); if (record.Any(x => x.Length > 0)) records.Add(record); record = []; }
            else field.Append(c);
        }
        if (field.Length > 0 || record.Count > 0) { record.Add(field.ToString()); records.Add(record); }
        if (records.Count == 0) throw new InvalidDataException($"CSV '{path}' is empty.");
        string[] headers = records[0].ToArray();
        var rows = new List<CsvRow>();
        for (int i = 1; i < records.Count; i++) { if (records[i].Count != headers.Length) throw new InvalidDataException($"CSV '{path}' row {i + 1} has {records[i].Count} fields; expected {headers.Length}."); rows.Add(new CsvRow(i + 1, headers.Zip(records[i]).ToDictionary(x => x.First, x => x.Second, StringComparer.Ordinal))); }
        return new CsvDocument { Headers = headers, Rows = rows };
    }
}
internal sealed record CsvRow(int PhysicalRowNumber, IReadOnlyDictionary<string, string> Values) { public string this[string header] => Values[header]; }
