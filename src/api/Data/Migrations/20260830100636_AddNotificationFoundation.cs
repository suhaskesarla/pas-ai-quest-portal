using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PAS.AIQuestPortal.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NotificationOutbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DestinationType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    DestinationKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RecipientParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AggregateType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    AggregateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayloadVersion = table.Column<int>(type: "int", nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    DeliveryPhase = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProviderMessageId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LastErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastErrorSummary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TerminalReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationOutbox", x => x.Id);
                    table.CheckConstraint("CK_NotificationOutbox_Attempts", "[AttemptCount] >= 0");
                    table.CheckConstraint("CK_NotificationOutbox_Completed", "([Status] IN ('Sent','Captured','Suppressed','Failed','DeliveryUnknown') AND [CompletedAt] IS NOT NULL) OR ([Status] IN ('Pending','Processing','RetryPending') AND [CompletedAt] IS NULL)");
                    table.CheckConstraint("CK_NotificationOutbox_DeliveryPhase", "([Status] = 'Processing' AND [DeliveryPhase] IN ('PreDelivery','DeliveryStarted')) OR ([Status] <> 'Processing' AND [DeliveryPhase] IS NULL)");
                    table.CheckConstraint("CK_NotificationOutbox_Destination", "([DestinationType] = 'ParticipantPrivate' AND [RecipientParticipantId] IS NOT NULL AND [DestinationKey] COLLATE Latin1_General_100_BIN2 = ('participant:' + LOWER(REPLACE(CONVERT(varchar(36),[RecipientParticipantId]),'-',''))) COLLATE Latin1_General_100_BIN2) OR ([DestinationType] = 'ConfiguredAudience' AND [RecipientParticipantId] IS NULL AND [DestinationKey] IN ('QUEST_GENERAL_AUDIENCE','QUEST_MANAGER_AUDIENCE'))");
                    table.CheckConstraint("CK_NotificationOutbox_PayloadVersion", "[PayloadVersion] > 0");
                    table.CheckConstraint("CK_NotificationOutbox_Status", "[Status] IN ('Pending','Processing','RetryPending','Sent','Captured','Suppressed','Failed','DeliveryUnknown')");
                    table.ForeignKey(
                        name: "FK_NotificationOutbox_Participants_RecipientParticipantId",
                        column: x => x.RecipientParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutbox_EventId_DestinationType_DestinationKey",
                table: "NotificationOutbox",
                columns: new[] { "EventId", "DestinationType", "DestinationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutbox_EventType_CreatedAt",
                table: "NotificationOutbox",
                columns: new[] { "EventType", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutbox_LeaseExpiresAt",
                table: "NotificationOutbox",
                column: "LeaseExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutbox_RecipientParticipantId",
                table: "NotificationOutbox",
                column: "RecipientParticipantId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutbox_Status_NextAttemptAt",
                table: "NotificationOutbox",
                columns: new[] { "Status", "NextAttemptAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationOutbox");
        }
    }
}
