using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PAS.AIQuestPortal.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamsNotificationDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ParticipantExternalIdentities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParticipantExternalIdentities", x => x.Id);
                    table.CheckConstraint("CK_ParticipantExternalIdentities_Provider", "[Provider] = 'Entra'");
                    table.ForeignKey(
                        name: "FK_ParticipantExternalIdentities_Participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TeamsChannelDestinationCandidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ConversationId = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    TeamId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ChannelId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BotId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CapturedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamsChannelDestinationCandidates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TeamsConversationReferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParticipantExternalIdentityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ConversationId = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    BotId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamsConversationReferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamsConversationReferences_ParticipantExternalIdentities_ParticipantExternalIdentityId",
                        column: x => x.ParticipantExternalIdentityId,
                        principalTable: "ParticipantExternalIdentities",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TeamsChannelDestinationAssignments",
                columns: table => new
                {
                    DestinationKey = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CandidateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignedByParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamsChannelDestinationAssignments", x => x.DestinationKey);
                    table.CheckConstraint("CK_TeamsChannelDestinationAssignments_Key", "[DestinationKey] IN ('QUEST_GENERAL_AUDIENCE','QUEST_MANAGER_AUDIENCE')");
                    table.ForeignKey(
                        name: "FK_TeamsChannelDestinationAssignments_Participants_AssignedByParticipantId",
                        column: x => x.AssignedByParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TeamsChannelDestinationAssignments_TeamsChannelDestinationCandidates_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "TeamsChannelDestinationCandidates",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ParticipantExternalIdentities_Provider_TenantId_ParticipantId",
                table: "ParticipantExternalIdentities",
                columns: new[] { "Provider", "TenantId", "ParticipantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ParticipantExternalIdentities_Provider_TenantId_SubjectId",
                table: "ParticipantExternalIdentities",
                columns: new[] { "Provider", "TenantId", "SubjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamsChannelDestinationAssignments_AssignedByParticipantId",
                table: "TeamsChannelDestinationAssignments",
                column: "AssignedByParticipantId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamsChannelDestinationAssignments_CandidateId",
                table: "TeamsChannelDestinationAssignments",
                column: "CandidateId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamsChannelDestinationCandidates_TenantId_ConversationId",
                table: "TeamsChannelDestinationCandidates",
                columns: new[] { "TenantId", "ConversationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamsConversationReferences_ParticipantExternalIdentityId",
                table: "TeamsConversationReferences",
                column: "ParticipantExternalIdentityId",
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_TeamsConversationReferences_TenantId_ConversationId",
                table: "TeamsConversationReferences",
                columns: new[] { "TenantId", "ConversationId" },
                unique: true,
                filter: "[IsActive] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TeamsChannelDestinationAssignments");

            migrationBuilder.DropTable(
                name: "TeamsConversationReferences");

            migrationBuilder.DropTable(
                name: "TeamsChannelDestinationCandidates");

            migrationBuilder.DropTable(
                name: "ParticipantExternalIdentities");
        }
    }
}
