using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PAS.AIQuestPortal.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHistoricalImportControl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "import");

            migrationBuilder.CreateTable(
                name: "HistoricalImportDatasets",
                schema: "import",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DatasetKey = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    BoundaryKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CanonicalFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CanonicalizationVersion = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    EstablishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistoricalImportDatasets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HistoricalImportRuns",
                schema: "import",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DatasetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InputFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistoricalImportRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HistoricalImportRuns_HistoricalImportDatasets_DatasetId",
                        column: x => x.DatasetId,
                        principalSchema: "import",
                        principalTable: "HistoricalImportDatasets",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "HistoricalImportArtifacts",
                schema: "import",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DatasetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceCellKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ArtifactType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ArtifactId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistoricalImportArtifacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HistoricalImportArtifacts_HistoricalImportRuns_RunId",
                        column: x => x.RunId,
                        principalSchema: "import",
                        principalTable: "HistoricalImportRuns",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "HistoricalImportObservations",
                schema: "import",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DatasetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    ObservationKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistoricalImportObservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HistoricalImportObservations_HistoricalImportDatasets_DatasetId",
                        column: x => x.DatasetId,
                        principalSchema: "import",
                        principalTable: "HistoricalImportDatasets",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HistoricalImportObservations_HistoricalImportRuns_RunId",
                        column: x => x.RunId,
                        principalSchema: "import",
                        principalTable: "HistoricalImportRuns",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "HistoricalImportSourceRows",
                schema: "import",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DatasetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileKey = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    PhysicalRowNumber = table.Column<int>(type: "int", nullable: false),
                    SourceRowKey = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    RowHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistoricalImportSourceRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HistoricalImportSourceRows_HistoricalImportRuns_RunId",
                        column: x => x.RunId,
                        principalSchema: "import",
                        principalTable: "HistoricalImportRuns",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalImportArtifacts_DatasetId_SourceCellKey_ArtifactType",
                schema: "import",
                table: "HistoricalImportArtifacts",
                columns: new[] { "DatasetId", "SourceCellKey", "ArtifactType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalImportArtifacts_RunId",
                schema: "import",
                table: "HistoricalImportArtifacts",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalImportDatasets_BoundaryKey",
                schema: "import",
                table: "HistoricalImportDatasets",
                column: "BoundaryKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalImportDatasets_DatasetKey",
                schema: "import",
                table: "HistoricalImportDatasets",
                column: "DatasetKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalImportObservations_DatasetId_Category_ObservationKey",
                schema: "import",
                table: "HistoricalImportObservations",
                columns: new[] { "DatasetId", "Category", "ObservationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalImportObservations_RunId",
                schema: "import",
                table: "HistoricalImportObservations",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalImportRuns_DatasetId_StartedAt",
                schema: "import",
                table: "HistoricalImportRuns",
                columns: new[] { "DatasetId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalImportSourceRows_DatasetId_FileKey_SourceRowKey",
                schema: "import",
                table: "HistoricalImportSourceRows",
                columns: new[] { "DatasetId", "FileKey", "SourceRowKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalImportSourceRows_RunId",
                schema: "import",
                table: "HistoricalImportSourceRows",
                column: "RunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HistoricalImportArtifacts",
                schema: "import");

            migrationBuilder.DropTable(
                name: "HistoricalImportObservations",
                schema: "import");

            migrationBuilder.DropTable(
                name: "HistoricalImportSourceRows",
                schema: "import");

            migrationBuilder.DropTable(
                name: "HistoricalImportRuns",
                schema: "import");

            migrationBuilder.DropTable(
                name: "HistoricalImportDatasets",
                schema: "import");
        }
    }
}
