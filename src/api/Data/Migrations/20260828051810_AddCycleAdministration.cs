using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PAS.AIQuestPortal.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCycleAdministration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [Cycles] WHERE [StartsAt] >= [EndsAt]) THROW 51000, 'Cycle Administration migration requires every existing cycle to satisfy StartsAt < EndsAt; no historical dates were rewritten.', 1;");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Cycles_DateRange",
                table: "Cycles");

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Cycles",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "CycleParticipants",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateTable(
                name: "CycleParticipantEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SequenceNumber = table.Column<int>(type: "int", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ToStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CycleParticipantEvents", x => x.Id);
                    table.UniqueConstraint("AK_CycleParticipantEvents_CycleId_ParticipantId_SequenceNumber", x => new { x.CycleId, x.ParticipantId, x.SequenceNumber });
                    table.CheckConstraint("CK_CycleParticipantEvents_EventType", "[EventType] IN ('Enrolled','StatusChanged')");
                    table.CheckConstraint("CK_CycleParticipantEvents_Reason", "LEN(LTRIM(RTRIM([Reason]))) > 0 AND DATALENGTH([Reason]) = DATALENGTH(LTRIM(RTRIM([Reason])))");
                    table.CheckConstraint("CK_CycleParticipantEvents_Sequence", "[SequenceNumber] > 0");
                    table.CheckConstraint("CK_CycleParticipantEvents_Shape", "([EventType] = 'Enrolled' AND [FromStatus] IS NULL AND [ToStatus] = 'Active') OR ([EventType] = 'StatusChanged' AND [FromStatus] IS NOT NULL AND [FromStatus] <> [ToStatus])");
                    table.CheckConstraint("CK_CycleParticipantEvents_StatusValues", "([FromStatus] IS NULL OR [FromStatus] IN ('Active','Withdrawn','Inactive')) AND [ToStatus] IN ('Active','Withdrawn','Inactive')");
                    table.ForeignKey(
                        name: "FK_CycleParticipantEvents_CycleParticipants_CycleId_ParticipantId",
                        columns: x => new { x.CycleId, x.ParticipantId },
                        principalTable: "CycleParticipants",
                        principalColumns: new[] { "CycleId", "ParticipantId" });
                    table.ForeignKey(
                        name: "FK_CycleParticipantEvents_Participants_ActorId",
                        column: x => x.ActorId,
                        principalTable: "Participants",
                        principalColumn: "Id");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Cycles_DateRange",
                table: "Cycles",
                sql: "[StartsAt] < [EndsAt]");

            migrationBuilder.CreateIndex(
                name: "IX_CycleParticipantEvents_ActorId",
                table: "CycleParticipantEvents",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_CycleParticipantEvents_CycleId_ParticipantId_OccurredAt_Id",
                table: "CycleParticipantEvents",
                columns: new[] { "CycleId", "ParticipantId", "OccurredAt", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CycleParticipantEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Cycles_DateRange",
                table: "Cycles");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Cycles");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "CycleParticipants");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Cycles_DateRange",
                table: "Cycles",
                sql: "[StartsAt] <= [EndsAt]");
        }
    }
}
