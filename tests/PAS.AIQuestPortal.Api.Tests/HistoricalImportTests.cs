using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PAS.AIQuestPortal.Api.Data;
using PAS.AIQuestPortal.Api.HistoricalImport;
using Xunit;

namespace PAS.AIQuestPortal.Api.Tests;

public sealed class HistoricalImportTests : IAsyncLifetime
{
    private readonly string _connectionString;
    private QuestDbContext _db = null!;
    private string _outputRoot = null!;

    public HistoricalImportTests()
    {
        string baseConnection = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION")
            ?? "Server=localhost,1433;Database=master;User Id=sa;Password=Local-only-validation-Passw0rd!;TrustServerCertificate=True";
        _connectionString = new SqlConnectionStringBuilder(baseConnection) { InitialCatalog = $"PasAiQuestImportTests_{Guid.NewGuid():N}" }.ConnectionString;
    }

    public async Task InitializeAsync()
    {
        _db = new QuestDbContext(new DbContextOptionsBuilder<QuestDbContext>().UseSqlServer(_connectionString).Options);
        await _db.Database.MigrateAsync();
        _outputRoot = Path.Combine(Path.GetTempPath(), $"pas-import-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_outputRoot);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await using var cleanup = new QuestDbContext(new DbContextOptionsBuilder<QuestDbContext>().UseSqlServer(_connectionString).Options);
        await cleanup.Database.EnsureDeletedAsync();
        Directory.Delete(_outputRoot, true);
    }

    [Fact]
    public async Task Synthetic_import_reconciles_and_unchanged_rerun_is_idempotent()
    {
        string manifest = Fixture("import-manifest.json");
        JsonObject expected = JsonNode.Parse(await File.ReadAllTextAsync(Fixture("expected-reconciliation.json")))!.AsObject();
        HistoricalImportResult first = await HistoricalImportCommand.ExecuteAsync(_db, manifest, Path.Combine(_outputRoot, "first"));
        Assert.True(first.Succeeded, first.HumanReport);

        ImportCounts firstCounts = await GetImportCountsAsync();

        HistoricalImportResult second = await HistoricalImportCommand.ExecuteAsync(_db, manifest, Path.Combine(_outputRoot, "second"));
        Assert.True(second.Succeeded, second.HumanReport);
        Assert.True(second.Report.UnchangedRerun);
        Assert.Equal(firstCounts, await GetImportCountsAsync());

        Cycle july = await _db.Cycles.SingleAsync(x => x.Code == "SYN-2026-07");
        Cycle august = await _db.Cycles.SingleAsync(x => x.Code == "SYN-2026-08");

        Assert.Equal(Expected(expected, "july", "total"), await _db.XPEntries.Where(x => x.CycleId == july.Id).SumAsync(x => x.Amount));
        Assert.Equal(Expected(expected, "august", "total"), await _db.XPEntries.Where(x => x.CycleId == august.Id).SumAsync(x => x.Amount));
        Assert.Equal(Expected(expected, "sourceSubtotals", "july", "task"), await _db.XPEntries.Where(x => x.CycleId == july.Id && x.SourceType == XPSourceType.TaskApproval).SumAsync(x => x.Amount));
        Assert.Equal(Expected(expected, "sourceSubtotals", "july", "manual"), await _db.XPEntries.Where(x => x.CycleId == july.Id && x.SourceType == XPSourceType.ManualAward).SumAsync(x => x.Amount));
        Assert.Equal(Expected(expected, "sourceSubtotals", "july", "raid"), await _db.XPEntries.Where(x => x.CycleId == july.Id && x.SourceType == XPSourceType.Raid).SumAsync(x => x.Amount));
        Assert.Equal(Expected(expected, "sourceSubtotals", "august", "task"), await _db.XPEntries.Where(x => x.CycleId == august.Id && x.SourceType == XPSourceType.TaskApproval).SumAsync(x => x.Amount));
        Assert.Equal(Expected(expected, "sourceSubtotals", "august", "manual"), await _db.XPEntries.Where(x => x.CycleId == august.Id && x.SourceType == XPSourceType.ManualAward).SumAsync(x => x.Amount));
        Assert.Equal(Expected(expected, "sourceSubtotals", "august", "raid"), await _db.XPEntries.Where(x => x.CycleId == august.Id && x.SourceType == XPSourceType.Raid).SumAsync(x => x.Amount));
        Assert.Equal(6, await _db.CycleParticipants.CountAsync());
        Assert.Equal(3, await _db.RaidParticipations.CountAsync());
        Assert.Equal(6, await _db.RaidEntitlements.Where(x => x.CycleId == august.Id && x.PassType == PassType.Physical).SumAsync(x => x.AssignedCount));
        Assert.Equal(3, await _db.RaidEntitlements.Where(x => x.CycleId == august.Id && x.PassType == PassType.Remote).SumAsync(x => x.AssignedCount));
        Assert.Equal(2, await _db.RaidParticipations.CountAsync(x => x.CycleId == august.Id && x.PassType == PassType.Physical));
        Assert.Equal(1, await _db.RaidParticipations.CountAsync(x => x.CycleId == august.Id && x.PassType == PassType.Remote));
        Assert.Equal(Expected(expected, "august", "total"), second.Report.Participants.Where(x => x.CycleKey == "august").Sum(x => x.ExpectedTotal));
        Assert.Equal(Expected(expected, "august", "total"), second.Report.Participants.Where(x => x.CycleKey == "august").Sum(x => x.PersistedTotal));
        foreach (string participant in new[] { "synthetic-avery", "synthetic-blake", "synthetic-casey" })
            Assert.Contains(second.Report.Participants, x => x.CycleKey == "august" && x.ParticipantSourceKey == participant && x.PersistedTotal == Expected(expected, "august", participant));
        Assert.Equal(Expected(expected, "raidPassColumnsContributeXp"), second.Report.RaidPassXpContribution);

        XPEntry goPass = await _db.XPEntries.SingleAsync(x => x.CycleId == july.Id && x.SourceType == XPSourceType.TaskApproval && x.ParticipantId == Guid.Parse("11111111-1111-4111-8111-111111111111"));
        Assert.Equal(july.Id, goPass.CycleId);
        Assert.Equal(DateTimeOffset.Parse(expected["julyGoPass3AwardedAt"]!.GetValue<string>()), goPass.AwardedAt);
        Assert.Equal(expected["julyGoPass3ReportingCycle"]!.GetValue<string>(), second.Report.CrossMonthAttributions.Single(x => x.ParticipantSourceKey == "synthetic-avery").ReportingCycleKey);
        Assert.All(await _db.XPEntries.Where(x => x.CycleId == august.Id).ToListAsync(), x => Assert.Equal(8, x.AwardedAt.Month));
        Guid[] augustPhysicalRaidParticipants = await _db.RaidParticipations
            .Where(x => x.CycleId == august.Id && x.PassType == PassType.Physical)
            .Select(x => x.ParticipantId)
            .ToArrayAsync();
        Assert.All(await _db.XPEntries.Where(x => x.CycleId == august.Id && x.SourceType == XPSourceType.Raid).ToListAsync(),
            x => Assert.Contains(x.ParticipantId, augustPhysicalRaidParticipants));

        List<SubmissionEvent> events = await _db.SubmissionEvents.OrderBy(x => x.SubmissionId).ThenBy(x => x.OccurredAt).ToListAsync();
        Assert.Equal(await _db.XPEntries.CountAsync(x => x.SourceType == XPSourceType.TaskApproval) * 2, events.Count);
        foreach (IGrouping<Guid, SubmissionEvent> approvalEvents in events.GroupBy(x => x.SubmissionId))
        {
            SubmissionEvent submitted = Assert.Single(approvalEvents.Where(x => x.EventType == "Submitted"));
            SubmissionEvent approved = Assert.Single(approvalEvents.Where(x => x.EventType == "Approved"));
            Submission submission = await _db.Submissions.SingleAsync(x => x.Id == approvalEvents.Key);
            Assert.Null(submitted.FromStatus); Assert.Equal(SubmissionStatus.Submitted, submitted.ToStatus); Assert.Equal(submission.ClaimantId, submitted.ActorId); Assert.Equal(submission.SubmittedAt, submitted.OccurredAt);
            Assert.Equal(SubmissionStatus.Submitted, approved.FromStatus); Assert.Equal(SubmissionStatus.Approved, approved.ToStatus); Assert.Equal(Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"), approved.ActorId); Assert.Equal(submission.LastUpdatedAt, approved.OccurredAt);
        }
        Assert.DoesNotContain(events, x => x.ToStatus is SubmissionStatus.UnderReview or SubmissionStatus.NeedsEvidence or SubmissionStatus.Resubmitted or SubmissionStatus.Rejected);
        Assert.True(await _db.HistoricalImportObservations.AnyAsync(x => x.Category == "MappedCell"));
        Assert.True(await _db.HistoricalImportObservations.AnyAsync(x => x.Category == "SubmissionEventProvenance"));
    }

    [Fact]
    public async Task Persisted_reconciliation_failure_rolls_back_all_import_writes()
    {
        await _db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER [dbo].[TR_QA_ForceRaidReconciliationFailure]
            ON [dbo].[RaidParticipations]
            AFTER INSERT
            AS
            BEGIN
                SET NOCOUNT ON;
                DELETE target FROM [dbo].[RaidParticipations] target
                INNER JOIN inserted source ON source.[Id] = target.[Id];
            END
            """);

        HistoricalImportResult result = await HistoricalImportCommand.ExecuteAsync(
            _db, Fixture("import-manifest.json"), Path.Combine(_outputRoot, "forced-persisted-reconciliation-failure"));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Report.Errors, x => x.Code == "DatabaseRaidUsageMismatch");
        Assert.Equal(new ImportCounts(), await GetImportCountsAsync());
    }

    [Theory]
    [InlineData("unmapped", "UnmappedColumn")]
    [InlineData("ambiguous", "AmbiguousParticipant")]
    [InlineData("total", "SourceTotalMismatch")]
    [InlineData("raid", "RaidUsageUnaccounted")]
    [InlineData("awarded-at", "AwardedAtUnresolved")]
    public async Task Negative_fixture_fails_visibly(string scenario, string expectedCode)
    {
        string manifest = CreateNegativeManifest(scenario);
        HistoricalImportResult result = await HistoricalImportCommand.ExecuteAsync(_db, manifest, Path.Combine(_outputRoot, scenario, "report"));
        Assert.False(result.Succeeded);
        Assert.Contains(result.Report.Errors, x => x.Code == expectedCode);
        Assert.Empty(await _db.XPEntries.ToListAsync());
        int commandExit = await HistoricalImportCommand.RunAsync(["--manifest", manifest, "--connection", _connectionString, "--report", Path.Combine(_outputRoot, scenario, "command-report")]);
        Assert.Equal(1, commandExit);
    }

    [Fact]
    public async Task Changed_source_row_fails_without_mutating_append_only_xp()
    {
        string scenarioRoot = Path.Combine(_outputRoot, "changed-source");
        CopyDirectory(FixtureRoot(), scenarioRoot);
        string manifest = Path.Combine(scenarioRoot, "import-manifest.json");
        HistoricalImportResult first = await HistoricalImportCommand.ExecuteAsync(_db, manifest, Path.Combine(scenarioRoot, "first"));
        Assert.True(first.Succeeded, first.HumanReport);
        var originalXp = await _db.XPEntries.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.ParticipantId, x.CycleId, x.Amount, x.EntryType, x.SourceType, x.AwardCategoryId, x.ChallengeId, x.TaskId, x.SubmissionId, x.RaidSessionId, x.AwardedAt })
            .ToListAsync();

        string july = Path.Combine(scenarioRoot, "july-scoresheet.csv");
        File.WriteAllText(july, File.ReadAllText(july).Replace("Avery North,10,5,10,8,10,43", "Avery North,10,5,10,8,20,53", StringComparison.Ordinal));
        HistoricalImportResult second = await HistoricalImportCommand.ExecuteAsync(_db, manifest, Path.Combine(scenarioRoot, "second"));

        Assert.False(second.Succeeded);
        Assert.Contains(second.Report.Errors, x => x.Code == "ChangedSourceConflict");
        var persistedXp = await _db.XPEntries.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.ParticipantId, x.CycleId, x.Amount, x.EntryType, x.SourceType, x.AwardCategoryId, x.ChallengeId, x.TaskId, x.SubmissionId, x.RaidSessionId, x.AwardedAt })
            .ToListAsync();
        Assert.Equal(originalXp, persistedXp);
    }

    [Theory]
    [InlineData("participant-alias")]
    [InlineData("participant-id")]
    [InlineData("participant-display")]
    [InlineData("cycle")]
    [InlineData("challenge")]
    [InlineData("task-xp")]
    [InlineData("task-mapping")]
    [InlineData("award")]
    [InlineData("raid-metadata")]
    [InlineData("submitted-at")]
    [InlineData("submitted-evidence")]
    [InlineData("awarded-at")]
    [InlineData("awarded-evidence")]
    [InlineData("pass-type")]
    [InlineData("raid-used-at")]
    [InlineData("raid-evidence")]
    [InlineData("raid-session")]
    [InlineData("raid-participant")]
    [InlineData("raid-pass-type")]
    [InlineData("add-row")]
    [InlineData("remove-row")]
    [InlineData("zero-to-nonzero")]
    [InlineData("nonzero-to-zero")]
    public async Task Changed_authoritative_semantics_conflict_before_domain_writes(string scenario)
    {
        string root = Path.Combine(_outputRoot, $"provenance-{scenario}"); CopyDirectory(FixtureRoot(), root);
        string manifestPath = Path.Combine(root, "import-manifest.json");
        HistoricalImportResult first = await HistoricalImportCommand.ExecuteAsync(_db, manifestPath, Path.Combine(root, "first"));
        Assert.True(first.Succeeded, first.HumanReport);
        ImportCounts counts = await GetImportCountsAsync();
        await MutateAuthoritativeInputAsync(root, manifestPath, scenario);
        HistoricalImportResult second = await HistoricalImportCommand.ExecuteAsync(_db, manifestPath, Path.Combine(root, "second"));
        Assert.False(second.Succeeded);
        Assert.Contains(second.Report.Errors, x => x.Code is "DatasetProvenanceConflict" or "ChangedSourceConflict");
        Assert.Equal(counts, await GetImportCountsAsync());
    }

    [Fact]
    public async Task Formatting_only_manifest_change_is_semantically_idempotent()
    {
        string root = Path.Combine(_outputRoot, "format-only"); CopyDirectory(FixtureRoot(), root);
        string manifestPath = Path.Combine(root, "import-manifest.json");
        HistoricalImportResult first = await HistoricalImportCommand.ExecuteAsync(_db, manifestPath, Path.Combine(root, "first")); Assert.True(first.Succeeded, first.HumanReport);
        JsonNode manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!;
        await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
        HistoricalImportResult second = await HistoricalImportCommand.ExecuteAsync(_db, manifestPath, Path.Combine(root, "second"));
        Assert.True(second.Succeeded, second.HumanReport); Assert.True(second.Report.UnchangedRerun); Assert.Equal(first.Report.ManifestHash, second.Report.ManifestHash);
    }

    [Fact]
    public async Task DatasetKey_substitution_is_not_a_correction_bypass()
    {
        string root = Path.Combine(_outputRoot, "dataset-key-substitution"); CopyDirectory(FixtureRoot(), root);
        string manifestPath = Path.Combine(root, "import-manifest.json");
        Assert.True((await HistoricalImportCommand.ExecuteAsync(_db, manifestPath, Path.Combine(root, "first"))).Succeeded);
        JsonObject manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!.AsObject(); manifest["datasetKey"] = "attempted-correction-bypass";
        await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString());
        HistoricalImportResult second = await HistoricalImportCommand.ExecuteAsync(_db, manifestPath, Path.Combine(root, "second"));
        Assert.False(second.Succeeded); Assert.Contains(second.Report.Errors, x => x.Code == "DatasetKeySubstitution"); Assert.Equal(1, await _db.HistoricalImportDatasets.CountAsync());
    }

    [Fact]
    public async Task Duplicate_participant_and_raid_session_usage_is_rejected_before_persistence_even_across_pass_types()
    {
        string root = Path.Combine(_outputRoot, "duplicate-raid-participation"); CopyDirectory(FixtureRoot(), root);
        string evidence = Path.Combine(root, "raid-usage-evidence.csv");
        await File.AppendAllTextAsync(evidence, "synthetic-avery,august-physical-raid,Remote,2026-08-13T10:00:00+10:00,synthetic-raid-evidence-duplicate\n");
        HistoricalImportResult result = await HistoricalImportCommand.ExecuteAsync(_db, Path.Combine(root, "import-manifest.json"), Path.Combine(root, "report"));
        Assert.False(result.Succeeded); Assert.Contains(result.Report.Errors, x => x.Code == "RaidParticipationConflict"); Assert.Empty(await _db.RaidParticipations.ToListAsync()); Assert.Empty(await _db.XPEntries.ToListAsync());
    }

    private async Task<ImportCounts> GetImportCountsAsync() => new(
        Participants: await _db.Participants.CountAsync(),
        Cycles: await _db.Cycles.CountAsync(),
        CycleParticipants: await _db.CycleParticipants.CountAsync(),
        Challenges: await _db.Challenges.CountAsync(),
        Tasks: await _db.ChallengeTasks.CountAsync(),
        AwardCategories: await _db.AwardCategories.CountAsync(),
        RaidSessions: await _db.RaidSessions.CountAsync(),
        Submissions: await _db.Submissions.CountAsync(),
        SubmissionBeneficiaries: await _db.SubmissionBeneficiaries.CountAsync(),
        RaidEntitlements: await _db.RaidEntitlements.CountAsync(),
        RaidParticipations: await _db.RaidParticipations.CountAsync(),
        XPEntries: await _db.XPEntries.CountAsync(),
        SubmissionEvents: await _db.SubmissionEvents.CountAsync(),
        Datasets: await _db.HistoricalImportDatasets.CountAsync(),
        ImportRuns: await _db.HistoricalImportRuns.CountAsync(),
        SourceRows: await _db.HistoricalImportSourceRows.CountAsync(),
        Artifacts: await _db.HistoricalImportArtifacts.CountAsync(),
        Observations: await _db.HistoricalImportObservations.CountAsync());

    private static int Expected(JsonObject root, params string[] path)
    {
        JsonNode? node = root;
        foreach (string segment in path) node = node?[segment];
        return node?.GetValue<int>() ?? throw new InvalidDataException($"Missing expected reconciliation value: {string.Join('.', path)}");
    }

    private sealed record ImportCounts(
        int Participants = 0, int Cycles = 0, int CycleParticipants = 0, int Challenges = 0, int Tasks = 0,
        int AwardCategories = 0, int RaidSessions = 0, int Submissions = 0, int SubmissionBeneficiaries = 0,
        int RaidEntitlements = 0, int RaidParticipations = 0, int XPEntries = 0, int SubmissionEvents = 0, int Datasets = 0, int ImportRuns = 0,
        int SourceRows = 0, int Artifacts = 0, int Observations = 0);

    private static async Task MutateAuthoritativeInputAsync(string root, string manifestPath, string scenario)
    {
        JsonObject manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!.AsObject();
        JsonObject julySheet = manifest["sheets"]![0]!.AsObject(); JsonObject taskColumn = julySheet["columns"]![1]!.AsObject();
        switch (scenario)
        {
            case "participant-alias": ReplaceInFile(Path.Combine(root, "participant-map.csv"), "Avery North", "Avery N."); break;
            case "participant-id": ReplaceInFile(Path.Combine(root, "participant-map.csv"), "11111111-1111-4111-8111-111111111111", "99999999-9999-4999-8999-999999999999"); break;
            case "participant-display": ReplaceInFile(Path.Combine(root, "participant-map.csv"), "11111111-1111-4111-8111-111111111111,Avery North", "11111111-1111-4111-8111-111111111111,Avery Changed"); break;
            case "cycle": manifest["cycles"]![0]!["name"] = "Changed July"; break;
            case "challenge": manifest["challenges"]![0]!["description"] = "Changed challenge"; break;
            case "task-xp": manifest["tasks"]![0]!["xp"] = 11; break;
            case "task-mapping": taskColumn["mappingKey"] = "august-task-1"; break;
            case "award": manifest["awardCategories"]![0]!["name"] = "Changed Award"; break;
            case "raid-metadata": manifest["raidSessions"]![0]!["name"] = "Changed Raid"; break;
            case "submitted-at": taskColumn["submittedAt"] = "2026-08-14T17:00:00+10:00"; break;
            case "submitted-evidence": taskColumn["submittedAtEvidence"] = "changed-submission-evidence"; break;
            case "awarded-at": taskColumn["awardedAt"] = "2026-08-15T11:30:00+10:00"; break;
            case "awarded-evidence": taskColumn["awardedAtEvidence"] = "changed-award-evidence"; break;
            case "pass-type": manifest["sheets"]![1]!["columns"]![4]!["passType"] = "Remote"; break;
            case "raid-used-at": ReplaceInFile(Path.Combine(root, "raid-usage-evidence.csv"), "2026-08-12T10:00:00+10:00", "2026-08-12T11:00:00+10:00"); break;
            case "raid-evidence": ReplaceInFile(Path.Combine(root, "raid-usage-evidence.csv"), "synthetic-raid-evidence-001", "changed-raid-evidence"); break;
            case "raid-session": ReplaceInFile(Path.Combine(root, "raid-usage-evidence.csv"), "august-physical-raid", "august-remote-raid"); break;
            case "raid-participant": ReplaceInFile(Path.Combine(root, "raid-usage-evidence.csv"), "synthetic-avery", "synthetic-casey"); break;
            case "raid-pass-type": ReplaceInFile(Path.Combine(root, "raid-usage-evidence.csv"), "Physical", "Remote"); break;
            case "add-row": File.AppendAllText(Path.Combine(root, "july-scoresheet.csv"), "\nCasey Vale,0,0,0,0,0,0"); break;
            case "remove-row": RemoveLastDataRow(Path.Combine(root, "july-scoresheet.csv")); break;
            case "zero-to-nonzero": ReplaceInFile(Path.Combine(root, "august-scoresheet.csv"), "Casey Vale,0,0,0,2,0,1,0,0", "Casey Vale,15,0,0,2,0,1,0,15"); break;
            case "nonzero-to-zero": ReplaceInFile(Path.Combine(root, "august-scoresheet.csv"), "Avery North,15,5,10,2,1,1,1,30", "Avery North,0,5,10,2,1,1,1,15"); break;
        }
        await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
    private static void ReplaceInFile(string path, string oldValue, string newValue) => File.WriteAllText(path, File.ReadAllText(path).Replace(oldValue, newValue, StringComparison.Ordinal));
    private static void RemoveLastDataRow(string path) { string[] lines = File.ReadAllLines(path); File.WriteAllLines(path, lines[..^1]); }

    private string CreateNegativeManifest(string scenario)
    {
        string scenarioRoot = Path.Combine(_outputRoot, scenario);
        CopyDirectory(FixtureRoot(), scenarioRoot);
        string manifestPath = Path.Combine(scenarioRoot, "import-manifest.json");
        JsonNode root = JsonNode.Parse(File.ReadAllText(manifestPath))!;
        JsonObject manifest = root.AsObject();
        manifest["datasetKey"] = $"synthetic-negative-{scenario}";
        JsonObject july = manifest["sheets"]![0]!.AsObject();
        switch (scenario)
        {
            case "unmapped":
                july["path"] = "failures/unmapped-column-july.csv";
                july["expectedHeaders"]!.AsArray().Insert(6, "Mystery XP");
                break;
            case "ambiguous": manifest["participantMap"] = "failures/ambiguous-participant-map.csv"; break;
            case "total": july["path"] = "failures/total-mismatch-july.csv"; break;
            case "raid": manifest["raidUsageEvidence"] = "failures/unaccounted-raid-usage.csv"; break;
            case "awarded-at":
                JsonObject xp = july["columns"]![1]!.AsObject(); xp.Remove("awardedAt"); xp.Remove("awardedAtEvidence");
                break;
        }
        File.WriteAllText(manifestPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return manifestPath;
    }

    private static string Fixture(string file) => Path.Combine(FixtureRoot(), file);
    private static string FixtureRoot()
    {
        DirectoryInfo? directory = new(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "tests", "fixtures", "historical-import");
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Historical import fixture root was not found above the test working directory.");
    }
    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (string directory in Directory.GetDirectories(source)) CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
}
