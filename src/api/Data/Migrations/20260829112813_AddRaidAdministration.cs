using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PAS.AIQuestPortal.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRaidAdministration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT 1
                    FROM [RaidParticipations]
                    GROUP BY [ParticipantId], [RaidSessionId]
                    HAVING COUNT(*) > 1)
                BEGIN
                    THROW 51002, 'AddRaidAdministration cannot strengthen raid participation uniqueness because duplicate participant/session rows exist.', 1;
                END
                """);

            migrationBuilder.DropIndex(
                name: "IX_RaidParticipations_ParticipantId_RaidSessionId_PassType",
                table: "RaidParticipations");

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "RaidSessions",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "RaidEntitlements",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateIndex(
                name: "IX_RaidParticipations_ParticipantId_CycleId_PassType",
                table: "RaidParticipations",
                columns: new[] { "ParticipantId", "CycleId", "PassType" });

            migrationBuilder.CreateIndex(
                name: "IX_RaidParticipations_ParticipantId_RaidSessionId",
                table: "RaidParticipations",
                columns: new[] { "ParticipantId", "RaidSessionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RaidParticipations_ParticipantId_CycleId_PassType",
                table: "RaidParticipations");

            migrationBuilder.DropIndex(
                name: "IX_RaidParticipations_ParticipantId_RaidSessionId",
                table: "RaidParticipations");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "RaidSessions");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "RaidEntitlements");

            migrationBuilder.CreateIndex(
                name: "IX_RaidParticipations_ParticipantId_RaidSessionId_PassType",
                table: "RaidParticipations",
                columns: new[] { "ParticipantId", "RaidSessionId", "PassType" },
                unique: true);
        }
    }
}
