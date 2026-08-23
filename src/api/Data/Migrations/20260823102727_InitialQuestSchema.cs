using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PAS.AIQuestPortal.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialQuestSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Participants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntraObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Participants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Cycles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ThemeConfiguration = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cycles", x => x.Id);
                    table.CheckConstraint("CK_Cycles_DateRange", "[StartsAt] <= [EndsAt]");
                    table.CheckConstraint("CK_Cycles_Status", "[Status] IN ('Active','Closing','Finalised')");
                    table.ForeignKey(
                        name: "FK_Cycles_Participants_CreatedByParticipantId",
                        column: x => x.CreatedByParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AwardCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Code = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AwardCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AwardCategories_Cycles_CycleId",
                        column: x => x.CycleId,
                        principalTable: "Cycles",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Challenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    OpenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CloseAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    HeroImageReference = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    GuideCharacter = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Challenges", x => x.Id);
                    table.UniqueConstraint("AK_Challenges_Id_CycleId", x => new { x.Id, x.CycleId });
                    table.CheckConstraint("CK_Challenges_Dates", "[OpenAt] <= [DueAt] AND [DueAt] <= [CloseAt]");
                    table.CheckConstraint("CK_Challenges_Status", "[Status] IN ('Draft','Published','Open','Closed','Archived')");
                    table.ForeignKey(
                        name: "FK_Challenges_Cycles_CycleId",
                        column: x => x.CycleId,
                        principalTable: "Cycles",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Challenges_Participants_CreatedByParticipantId",
                        column: x => x.CreatedByParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CycleParticipants",
                columns: table => new
                {
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LeftAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CycleParticipants", x => new { x.CycleId, x.ParticipantId });
                    table.CheckConstraint("CK_CycleParticipants_Dates", "[LeftAt] IS NULL OR [JoinedAt] IS NULL OR [LeftAt] >= [JoinedAt]");
                    table.CheckConstraint("CK_CycleParticipants_Status", "[Status] IN ('Active','Withdrawn','Inactive')");
                    table.ForeignKey(
                        name: "FK_CycleParticipants_Cycles_CycleId",
                        column: x => x.CycleId,
                        principalTable: "Cycles",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CycleParticipants_Participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CycleTeams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CycleTeams", x => x.Id);
                    table.UniqueConstraint("AK_CycleTeams_Id_CycleId", x => new { x.Id, x.CycleId });
                    table.ForeignKey(
                        name: "FK_CycleTeams_Cycles_CycleId",
                        column: x => x.CycleId,
                        principalTable: "Cycles",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RaidSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RaidSessions", x => x.Id);
                    table.UniqueConstraint("AK_RaidSessions_Id_CycleId", x => new { x.Id, x.CycleId });
                    table.ForeignKey(
                        name: "FK_RaidSessions_Cycles_CycleId",
                        column: x => x.CycleId,
                        principalTable: "Cycles",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ChallengeDeadlineChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChallengeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SequenceNumber = table.Column<int>(type: "int", nullable: false),
                    PreviousDueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    NewDueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ChangedByParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChangedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SupersedesChangeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChallengeDeadlineChanges", x => x.Id);
                    table.UniqueConstraint("AK_ChallengeDeadlineChanges_ChallengeId_SequenceNumber", x => new { x.ChallengeId, x.SequenceNumber });
                    table.UniqueConstraint("AK_ChallengeDeadlineChanges_Id_ChallengeId", x => new { x.Id, x.ChallengeId });
                    table.CheckConstraint("CK_ChallengeDeadlineChanges_Sequence", "[SequenceNumber] > 0");
                    table.ForeignKey(
                        name: "FK_ChallengeDeadlineChanges_ChallengeDeadlineChanges_SupersedesChangeId_ChallengeId",
                        columns: x => new { x.SupersedesChangeId, x.ChallengeId },
                        principalTable: "ChallengeDeadlineChanges",
                        principalColumns: new[] { "Id", "ChallengeId" });
                    table.ForeignKey(
                        name: "FK_ChallengeDeadlineChanges_Challenges_ChallengeId",
                        column: x => x.ChallengeId,
                        principalTable: "Challenges",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ChallengeDeadlineChanges_Participants_ChangedByParticipantId",
                        column: x => x.ChangedByParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ChallengeParticipations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChallengeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChallengeParticipations", x => x.Id);
                    table.UniqueConstraint("AK_ChallengeParticipations_Id_ChallengeId", x => new { x.Id, x.ChallengeId });
                    table.UniqueConstraint("AK_ChallengeParticipations_Id_ChallengeId_CycleId", x => new { x.Id, x.ChallengeId, x.CycleId });
                    table.ForeignKey(
                        name: "FK_ChallengeParticipations_Challenges_ChallengeId_CycleId",
                        columns: x => new { x.ChallengeId, x.CycleId },
                        principalTable: "Challenges",
                        principalColumns: new[] { "Id", "CycleId" });
                    table.ForeignKey(
                        name: "FK_ChallengeParticipations_Participants_CreatedByParticipantId",
                        column: x => x.CreatedByParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ChallengeTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChallengeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    XP = table.Column<int>(type: "int", nullable: false),
                    EvidenceRequirement = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CustomEvidenceRequirement = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ScoringMode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChallengeTasks", x => x.Id);
                    table.UniqueConstraint("AK_ChallengeTasks_Id_ChallengeId", x => new { x.Id, x.ChallengeId });
                    table.CheckConstraint("CK_ChallengeTasks_EvidenceRequirement", "[EvidenceRequirement] IN ('None','Text','Link','Attachment','Multiple','Custom')");
                    table.CheckConstraint("CK_ChallengeTasks_ScoringMode", "[ScoringMode] IN ('Individual','WholeTeam','ClaimantSelectsBeneficiaries','AttendanceBased')");
                    table.CheckConstraint("CK_ChallengeTasks_XP", "[XP] >= 0");
                    table.ForeignKey(
                        name: "FK_ChallengeTasks_Challenges_ChallengeId",
                        column: x => x.ChallengeId,
                        principalTable: "Challenges",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ChallengeTeamPolicies",
                columns: table => new
                {
                    ChallengeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FormationMode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    MinMembers = table.Column<int>(type: "int", nullable: false),
                    MaxMembers = table.Column<int>(type: "int", nullable: false),
                    AllowSolo = table.Column<bool>(type: "bit", nullable: false),
                    FormationDeadline = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LockAfterStart = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChallengeTeamPolicies", x => x.ChallengeId);
                    table.CheckConstraint("CK_ChallengeTeamPolicies_FormationMode", "[FormationMode] IN ('SelfForm','ManagerAssigned','Either')");
                    table.CheckConstraint("CK_ChallengeTeamPolicies_Size", "[MinMembers] > 0 AND [MaxMembers] > 0 AND [MinMembers] <= [MaxMembers]");
                    table.CheckConstraint("CK_ChallengeTeamPolicies_Solo", "[AllowSolo] = 1 OR [MinMembers] > 1");
                    table.ForeignKey(
                        name: "FK_ChallengeTeamPolicies_Challenges_ChallengeId",
                        column: x => x.ChallengeId,
                        principalTable: "Challenges",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ParticipantChallengeDeadlineEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChallengeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SequenceNumber = table.Column<int>(type: "int", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    PreviousOverrideDueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    NewOverrideDueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PreviousEffectiveDueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    NewEffectiveDueAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SupersedesEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParticipantChallengeDeadlineEvents", x => x.Id);
                    table.UniqueConstraint("AK_ParticipantChallengeDeadlineEvents_ChallengeId_ParticipantId_SequenceNumber", x => new { x.ChallengeId, x.ParticipantId, x.SequenceNumber });
                    table.UniqueConstraint("AK_ParticipantChallengeDeadlineEvents_Id_ChallengeId_ParticipantId", x => new { x.Id, x.ChallengeId, x.ParticipantId });
                    table.CheckConstraint("CK_ParticipantDeadlineEvents_Sequence", "[SequenceNumber] > 0");
                    table.CheckConstraint("CK_ParticipantDeadlineEvents_Shape", "([EventType] = 'OverrideCleared' AND [NewOverrideDueAt] IS NULL) OR ([EventType] IN ('OverrideSet','OverrideChanged') AND [NewOverrideDueAt] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_ParticipantChallengeDeadlineEvents_Challenges_ChallengeId",
                        column: x => x.ChallengeId,
                        principalTable: "Challenges",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ParticipantChallengeDeadlineEvents_ParticipantChallengeDeadlineEvents_SupersedesEventId_ChallengeId_ParticipantId",
                        columns: x => new { x.SupersedesEventId, x.ChallengeId, x.ParticipantId },
                        principalTable: "ParticipantChallengeDeadlineEvents",
                        principalColumns: new[] { "Id", "ChallengeId", "ParticipantId" });
                    table.ForeignKey(
                        name: "FK_ParticipantChallengeDeadlineEvents_Participants_ActorId",
                        column: x => x.ActorId,
                        principalTable: "Participants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ParticipantChallengeDeadlineEvents_Participants_ParticipantId",
                        column: x => x.ParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RaidEntitlements",
                columns: table => new
                {
                    ParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PassType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    AssignedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RaidEntitlements", x => new { x.ParticipantId, x.CycleId, x.PassType });
                    table.CheckConstraint("CK_RaidEntitlements_AssignedCount", "[AssignedCount] >= 0");
                    table.CheckConstraint("CK_RaidEntitlements_PassType", "[PassType] IN ('Physical','Remote')");
                    table.ForeignKey(
                        name: "FK_RaidEntitlements_CycleParticipants_CycleId_ParticipantId",
                        columns: x => new { x.CycleId, x.ParticipantId },
                        principalTable: "CycleParticipants",
                        principalColumns: new[] { "CycleId", "ParticipantId" });
                });

            migrationBuilder.CreateTable(
                name: "CycleTeamMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleTeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LeftAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CycleTeamMembers", x => x.Id);
                    table.CheckConstraint("CK_CycleTeamMembers_Dates", "[LeftAt] IS NULL OR [LeftAt] >= [JoinedAt]");
                    table.ForeignKey(
                        name: "FK_CycleTeamMembers_CycleParticipants_CycleId_ParticipantId",
                        columns: x => new { x.CycleId, x.ParticipantId },
                        principalTable: "CycleParticipants",
                        principalColumns: new[] { "CycleId", "ParticipantId" });
                    table.ForeignKey(
                        name: "FK_CycleTeamMembers_CycleTeams_CycleTeamId_CycleId",
                        columns: x => new { x.CycleTeamId, x.CycleId },
                        principalTable: "CycleTeams",
                        principalColumns: new[] { "Id", "CycleId" });
                });

            migrationBuilder.CreateTable(
                name: "RaidParticipations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RaidSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PassType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RaidParticipations", x => x.Id);
                    table.CheckConstraint("CK_RaidParticipations_PassType", "[PassType] IN ('Physical','Remote')");
                    table.ForeignKey(
                        name: "FK_RaidParticipations_CycleParticipants_CycleId_ParticipantId",
                        columns: x => new { x.CycleId, x.ParticipantId },
                        principalTable: "CycleParticipants",
                        principalColumns: new[] { "CycleId", "ParticipantId" });
                    table.ForeignKey(
                        name: "FK_RaidParticipations_RaidSessions_RaidSessionId_CycleId",
                        columns: x => new { x.RaidSessionId, x.CycleId },
                        principalTable: "RaidSessions",
                        principalColumns: new[] { "Id", "CycleId" });
                });

            migrationBuilder.CreateTable(
                name: "ChallengeParticipationMembers",
                columns: table => new
                {
                    ChallengeParticipationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChallengeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleTeamIdAtParticipation = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    JoinedSnapshotAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChallengeParticipationMembers", x => new { x.ChallengeParticipationId, x.ParticipantId });
                    table.ForeignKey(
                        name: "FK_ChallengeParticipationMembers_ChallengeParticipations_ChallengeParticipationId_ChallengeId_CycleId",
                        columns: x => new { x.ChallengeParticipationId, x.ChallengeId, x.CycleId },
                        principalTable: "ChallengeParticipations",
                        principalColumns: new[] { "Id", "ChallengeId", "CycleId" });
                    table.ForeignKey(
                        name: "FK_ChallengeParticipationMembers_CycleParticipants_CycleId_ParticipantId",
                        columns: x => new { x.CycleId, x.ParticipantId },
                        principalTable: "CycleParticipants",
                        principalColumns: new[] { "CycleId", "ParticipantId" });
                    table.ForeignKey(
                        name: "FK_ChallengeParticipationMembers_CycleTeams_CycleTeamIdAtParticipation_CycleId",
                        columns: x => new { x.CycleTeamIdAtParticipation, x.CycleId },
                        principalTable: "CycleTeams",
                        principalColumns: new[] { "Id", "CycleId" });
                });

            migrationBuilder.CreateTable(
                name: "Submissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChallengeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChallengeParticipationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ReviewerComment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastUpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Submissions", x => x.Id);
                    table.UniqueConstraint("AK_Submissions_Id_ChallengeId_TaskId_CycleId", x => new { x.Id, x.ChallengeId, x.TaskId, x.CycleId });
                    table.UniqueConstraint("AK_Submissions_Id_CycleId", x => new { x.Id, x.CycleId });
                    table.CheckConstraint("CK_Submissions_Status", "[Status] IN ('Submitted','UnderReview','NeedsEvidence','Resubmitted','Approved','Rejected')");
                    table.ForeignKey(
                        name: "FK_Submissions_ChallengeParticipations_ChallengeParticipationId_ChallengeId_CycleId",
                        columns: x => new { x.ChallengeParticipationId, x.ChallengeId, x.CycleId },
                        principalTable: "ChallengeParticipations",
                        principalColumns: new[] { "Id", "ChallengeId", "CycleId" });
                    table.ForeignKey(
                        name: "FK_Submissions_ChallengeTasks_TaskId_ChallengeId",
                        columns: x => new { x.TaskId, x.ChallengeId },
                        principalTable: "ChallengeTasks",
                        principalColumns: new[] { "Id", "ChallengeId" });
                    table.ForeignKey(
                        name: "FK_Submissions_Challenges_ChallengeId_CycleId",
                        columns: x => new { x.ChallengeId, x.CycleId },
                        principalTable: "Challenges",
                        principalColumns: new[] { "Id", "CycleId" });
                    table.ForeignKey(
                        name: "FK_Submissions_CycleParticipants_CycleId_ClaimantId",
                        columns: x => new { x.CycleId, x.ClaimantId },
                        principalTable: "CycleParticipants",
                        principalColumns: new[] { "CycleId", "ParticipantId" });
                });

            migrationBuilder.CreateTable(
                name: "SubmissionBeneficiaries",
                columns: table => new
                {
                    SubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AddedByParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubmissionBeneficiaries", x => new { x.SubmissionId, x.ParticipantId });
                    table.UniqueConstraint("AK_SubmissionBeneficiaries_SubmissionId_ParticipantId_CycleId", x => new { x.SubmissionId, x.ParticipantId, x.CycleId });
                    table.ForeignKey(
                        name: "FK_SubmissionBeneficiaries_CycleParticipants_CycleId_ParticipantId",
                        columns: x => new { x.CycleId, x.ParticipantId },
                        principalTable: "CycleParticipants",
                        principalColumns: new[] { "CycleId", "ParticipantId" });
                    table.ForeignKey(
                        name: "FK_SubmissionBeneficiaries_Participants_AddedByParticipantId",
                        column: x => x.AddedByParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SubmissionBeneficiaries_Submissions_SubmissionId_CycleId",
                        columns: x => new { x.SubmissionId, x.CycleId },
                        principalTable: "Submissions",
                        principalColumns: new[] { "Id", "CycleId" });
                });

            migrationBuilder.CreateTable(
                name: "SubmissionEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ToStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubmissionEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubmissionEvents_Participants_ActorId",
                        column: x => x.ActorId,
                        principalTable: "Participants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SubmissionEvents_Submissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "Submissions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SubmissionEvidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EvidenceKind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    TextValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LinkUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    StorageAccount = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Container = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BlobKey = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    MimeType = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    ProvidedByParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubmissionEvidence", x => x.Id);
                    table.CheckConstraint("CK_SubmissionEvidence_Shape", "([EvidenceKind] = 'Text' AND [TextValue] IS NOT NULL AND [LinkUrl] IS NULL AND [BlobKey] IS NULL) OR ([EvidenceKind] = 'Link' AND [LinkUrl] IS NOT NULL AND [TextValue] IS NULL AND [BlobKey] IS NULL) OR ([EvidenceKind] = 'Attachment' AND [StorageAccount] IS NOT NULL AND [Container] IS NOT NULL AND [BlobKey] IS NOT NULL AND [OriginalFileName] IS NOT NULL AND [MimeType] IS NOT NULL AND [SizeBytes] IS NOT NULL AND [SizeBytes] >= 0 AND [TextValue] IS NULL AND [LinkUrl] IS NULL)");
                    table.ForeignKey(
                        name: "FK_SubmissionEvidence_Participants_ProvidedByParticipantId",
                        column: x => x.ProvidedByParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SubmissionEvidence_Submissions_SubmissionId",
                        column: x => x.SubmissionId,
                        principalTable: "Submissions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "XPEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<int>(type: "int", nullable: false),
                    EntryType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    AwardCategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChallengeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RaidSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CycleTeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChallengeParticipationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    AwardedByParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AwardedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReversesEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_XPEntries", x => x.Id);
                    table.UniqueConstraint("AK_XPEntries_Id_ParticipantId_CycleId_SourceType", x => new { x.Id, x.ParticipantId, x.CycleId, x.SourceType });
                    table.CheckConstraint("CK_XPEntries_AmountAndReversal", "([EntryType] = 'Grant' AND [ReversesEntryId] IS NULL AND [Amount] > 0) OR ([EntryType] = 'Reversal' AND [ReversesEntryId] IS NOT NULL AND [Amount] < 0) OR ([EntryType] = 'Correction' AND [ReversesEntryId] IS NOT NULL AND [Amount] > 0)");
                    table.CheckConstraint("CK_XPEntries_ParticipationChallenge", "[ChallengeParticipationId] IS NULL OR [ChallengeId] IS NOT NULL");
                    table.CheckConstraint("CK_XPEntries_SourceShape", "([SourceType] = 'TaskApproval' AND [SubmissionId] IS NOT NULL AND [ChallengeId] IS NOT NULL AND [TaskId] IS NOT NULL AND [AwardCategoryId] IS NULL AND [RaidSessionId] IS NULL) OR ([SourceType] = 'ManualAward' AND [AwardCategoryId] IS NOT NULL AND [SubmissionId] IS NULL AND [TaskId] IS NULL AND [RaidSessionId] IS NULL) OR ([SourceType] = 'Raid' AND [RaidSessionId] IS NOT NULL AND [AwardCategoryId] IS NULL AND [SubmissionId] IS NULL AND [TaskId] IS NULL)");
                    table.ForeignKey(
                        name: "FK_XPEntries_AwardCategories_AwardCategoryId",
                        column: x => x.AwardCategoryId,
                        principalTable: "AwardCategories",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_XPEntries_ChallengeParticipations_ChallengeParticipationId_ChallengeId_CycleId",
                        columns: x => new { x.ChallengeParticipationId, x.ChallengeId, x.CycleId },
                        principalTable: "ChallengeParticipations",
                        principalColumns: new[] { "Id", "ChallengeId", "CycleId" });
                    table.ForeignKey(
                        name: "FK_XPEntries_CycleParticipants_CycleId_ParticipantId",
                        columns: x => new { x.CycleId, x.ParticipantId },
                        principalTable: "CycleParticipants",
                        principalColumns: new[] { "CycleId", "ParticipantId" });
                    table.ForeignKey(
                        name: "FK_XPEntries_CycleTeams_CycleTeamId_CycleId",
                        columns: x => new { x.CycleTeamId, x.CycleId },
                        principalTable: "CycleTeams",
                        principalColumns: new[] { "Id", "CycleId" });
                    table.ForeignKey(
                        name: "FK_XPEntries_Participants_AwardedByParticipantId",
                        column: x => x.AwardedByParticipantId,
                        principalTable: "Participants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_XPEntries_RaidSessions_RaidSessionId_CycleId",
                        columns: x => new { x.RaidSessionId, x.CycleId },
                        principalTable: "RaidSessions",
                        principalColumns: new[] { "Id", "CycleId" });
                    table.ForeignKey(
                        name: "FK_XPEntries_SubmissionBeneficiaries_SubmissionId_ParticipantId",
                        columns: x => new { x.SubmissionId, x.ParticipantId },
                        principalTable: "SubmissionBeneficiaries",
                        principalColumns: new[] { "SubmissionId", "ParticipantId" });
                    table.ForeignKey(
                        name: "FK_XPEntries_Submissions_SubmissionId_ChallengeId_TaskId_CycleId",
                        columns: x => new { x.SubmissionId, x.ChallengeId, x.TaskId, x.CycleId },
                        principalTable: "Submissions",
                        principalColumns: new[] { "Id", "ChallengeId", "TaskId", "CycleId" });
                    table.ForeignKey(
                        name: "FK_XPEntries_XPEntries_ReversesEntryId_ParticipantId_CycleId_SourceType",
                        columns: x => new { x.ReversesEntryId, x.ParticipantId, x.CycleId, x.SourceType },
                        principalTable: "XPEntries",
                        principalColumns: new[] { "Id", "ParticipantId", "CycleId", "SourceType" });
                });

            migrationBuilder.CreateTable(
                name: "CycleEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CycleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SequenceNumber = table.Column<int>(type: "int", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ToStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ActorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SupersedesEventId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RelatedXPEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CycleEvents", x => x.Id);
                    table.UniqueConstraint("AK_CycleEvents_CycleId_SequenceNumber", x => new { x.CycleId, x.SequenceNumber });
                    table.UniqueConstraint("AK_CycleEvents_Id_CycleId", x => new { x.Id, x.CycleId });
                    table.CheckConstraint("CK_CycleEvents_Sequence", "[SequenceNumber] > 0");
                    table.CheckConstraint("CK_CycleEvents_StatusShape", "([EventType] = 'Created' AND [FromStatus] IS NULL AND [ToStatus] IS NOT NULL) OR ([EventType] IN ('StatusChanged','Reopened') AND [FromStatus] IS NOT NULL AND [ToStatus] IS NOT NULL AND [FromStatus] <> [ToStatus]) OR ([EventType] IN ('CorrectionAuthorised','CorrectionRecorded') AND [FromStatus] IS NULL AND [ToStatus] IS NULL)");
                    table.CheckConstraint("CK_CycleEvents_StatusValues", "([FromStatus] IS NULL OR [FromStatus] IN ('Active','Closing','Finalised')) AND ([ToStatus] IS NULL OR [ToStatus] IN ('Active','Closing','Finalised'))");
                    table.ForeignKey(
                        name: "FK_CycleEvents_CycleEvents_SupersedesEventId_CycleId",
                        columns: x => new { x.SupersedesEventId, x.CycleId },
                        principalTable: "CycleEvents",
                        principalColumns: new[] { "Id", "CycleId" });
                    table.ForeignKey(
                        name: "FK_CycleEvents_Cycles_CycleId",
                        column: x => x.CycleId,
                        principalTable: "Cycles",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CycleEvents_Participants_ActorId",
                        column: x => x.ActorId,
                        principalTable: "Participants",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CycleEvents_XPEntries_RelatedXPEntryId",
                        column: x => x.RelatedXPEntryId,
                        principalTable: "XPEntries",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "UX_AwardCategories_CycleCode",
                table: "AwardCategories",
                columns: new[] { "CycleId", "Code" },
                unique: true,
                filter: "[CycleId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_AwardCategories_GlobalCode",
                table: "AwardCategories",
                column: "Code",
                unique: true,
                filter: "[CycleId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ChallengeDeadlineChanges_ChallengeId_ChangedAt",
                table: "ChallengeDeadlineChanges",
                columns: new[] { "ChallengeId", "ChangedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChallengeDeadlineChanges_ChangedByParticipantId",
                table: "ChallengeDeadlineChanges",
                column: "ChangedByParticipantId");

            migrationBuilder.CreateIndex(
                name: "IX_ChallengeDeadlineChanges_SupersedesChangeId",
                table: "ChallengeDeadlineChanges",
                column: "SupersedesChangeId",
                unique: true,
                filter: "[SupersedesChangeId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ChallengeDeadlineChanges_SupersedesChangeId_ChallengeId",
                table: "ChallengeDeadlineChanges",
                columns: new[] { "SupersedesChangeId", "ChallengeId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChallengeParticipationMembers_ChallengeParticipationId_ChallengeId_CycleId",
                table: "ChallengeParticipationMembers",
                columns: new[] { "ChallengeParticipationId", "ChallengeId", "CycleId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChallengeParticipationMembers_CycleId_ParticipantId",
                table: "ChallengeParticipationMembers",
                columns: new[] { "CycleId", "ParticipantId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChallengeParticipationMembers_CycleTeamIdAtParticipation_CycleId",
                table: "ChallengeParticipationMembers",
                columns: new[] { "CycleTeamIdAtParticipation", "CycleId" },
                filter: "[CycleTeamIdAtParticipation] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ChallengeParticipationMembers_ParticipantId_ChallengeId",
                table: "ChallengeParticipationMembers",
                columns: new[] { "ParticipantId", "ChallengeId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChallengeParticipations_ChallengeId_CycleId",
                table: "ChallengeParticipations",
                columns: new[] { "ChallengeId", "CycleId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChallengeParticipations_CreatedByParticipantId",
                table: "ChallengeParticipations",
                column: "CreatedByParticipantId");

            migrationBuilder.CreateIndex(
                name: "IX_Challenges_CreatedByParticipantId",
                table: "Challenges",
                column: "CreatedByParticipantId");

            migrationBuilder.CreateIndex(
                name: "IX_Challenges_CycleId",
                table: "Challenges",
                column: "CycleId");

            migrationBuilder.CreateIndex(
                name: "IX_ChallengeTasks_ChallengeId_SortOrder",
                table: "ChallengeTasks",
                columns: new[] { "ChallengeId", "SortOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CycleEvents_ActorId",
                table: "CycleEvents",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_CycleEvents_CorrelationId",
                table: "CycleEvents",
                column: "CorrelationId",
                filter: "[CorrelationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CycleEvents_CycleId_OccurredAt_Id",
                table: "CycleEvents",
                columns: new[] { "CycleId", "OccurredAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_CycleEvents_RelatedXPEntryId",
                table: "CycleEvents",
                column: "RelatedXPEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_CycleEvents_SupersedesEventId",
                table: "CycleEvents",
                column: "SupersedesEventId",
                unique: true,
                filter: "[SupersedesEventId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CycleEvents_SupersedesEventId_CycleId",
                table: "CycleEvents",
                columns: new[] { "SupersedesEventId", "CycleId" });

            migrationBuilder.CreateIndex(
                name: "IX_CycleParticipants_CycleId_Status",
                table: "CycleParticipants",
                columns: new[] { "CycleId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CycleParticipants_ParticipantId_CycleId",
                table: "CycleParticipants",
                columns: new[] { "ParticipantId", "CycleId" });

            migrationBuilder.CreateIndex(
                name: "IX_Cycles_Code",
                table: "Cycles",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cycles_CreatedByParticipantId",
                table: "Cycles",
                column: "CreatedByParticipantId");

            migrationBuilder.CreateIndex(
                name: "IX_CycleTeamMembers_CycleTeamId_CycleId",
                table: "CycleTeamMembers",
                columns: new[] { "CycleTeamId", "CycleId" });

            migrationBuilder.CreateIndex(
                name: "IX_CycleTeamMembers_CycleTeamId_ParticipantId_JoinedAt",
                table: "CycleTeamMembers",
                columns: new[] { "CycleTeamId", "ParticipantId", "JoinedAt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_CycleTeamMembers_OpenMembership",
                table: "CycleTeamMembers",
                columns: new[] { "CycleId", "ParticipantId" },
                unique: true,
                filter: "[LeftAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CycleTeams_CycleId_Name",
                table: "CycleTeams",
                columns: new[] { "CycleId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ParticipantChallengeDeadlineEvents_ActorId",
                table: "ParticipantChallengeDeadlineEvents",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_ParticipantChallengeDeadlineEvents_ChallengeId_ParticipantId_OccurredAt_Id",
                table: "ParticipantChallengeDeadlineEvents",
                columns: new[] { "ChallengeId", "ParticipantId", "OccurredAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ParticipantChallengeDeadlineEvents_ParticipantId",
                table: "ParticipantChallengeDeadlineEvents",
                column: "ParticipantId");

            migrationBuilder.CreateIndex(
                name: "IX_ParticipantChallengeDeadlineEvents_SupersedesEventId",
                table: "ParticipantChallengeDeadlineEvents",
                column: "SupersedesEventId",
                unique: true,
                filter: "[SupersedesEventId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ParticipantChallengeDeadlineEvents_SupersedesEventId_ChallengeId_ParticipantId",
                table: "ParticipantChallengeDeadlineEvents",
                columns: new[] { "SupersedesEventId", "ChallengeId", "ParticipantId" });

            migrationBuilder.CreateIndex(
                name: "IX_Participants_EntraObjectId",
                table: "Participants",
                column: "EntraObjectId",
                unique: true,
                filter: "[EntraObjectId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RaidEntitlements_CycleId_ParticipantId",
                table: "RaidEntitlements",
                columns: new[] { "CycleId", "ParticipantId" });

            migrationBuilder.CreateIndex(
                name: "IX_RaidParticipations_CycleId_ParticipantId",
                table: "RaidParticipations",
                columns: new[] { "CycleId", "ParticipantId" });

            migrationBuilder.CreateIndex(
                name: "IX_RaidParticipations_ParticipantId_RaidSessionId_PassType",
                table: "RaidParticipations",
                columns: new[] { "ParticipantId", "RaidSessionId", "PassType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RaidParticipations_RaidSessionId_CycleId",
                table: "RaidParticipations",
                columns: new[] { "RaidSessionId", "CycleId" });

            migrationBuilder.CreateIndex(
                name: "IX_RaidSessions_CycleId",
                table: "RaidSessions",
                column: "CycleId");

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionBeneficiaries_AddedByParticipantId",
                table: "SubmissionBeneficiaries",
                column: "AddedByParticipantId");

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionBeneficiaries_CycleId_ParticipantId",
                table: "SubmissionBeneficiaries",
                columns: new[] { "CycleId", "ParticipantId" });

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionBeneficiaries_SubmissionId_CycleId",
                table: "SubmissionBeneficiaries",
                columns: new[] { "SubmissionId", "CycleId" });

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionEvents_ActorId",
                table: "SubmissionEvents",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionEvents_SubmissionId_OccurredAt_Id",
                table: "SubmissionEvents",
                columns: new[] { "SubmissionId", "OccurredAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionEvidence_ProvidedByParticipantId",
                table: "SubmissionEvidence",
                column: "ProvidedByParticipantId");

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionEvidence_SubmissionId_CreatedAt",
                table: "SubmissionEvidence",
                columns: new[] { "SubmissionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_ChallengeId_CycleId",
                table: "Submissions",
                columns: new[] { "ChallengeId", "CycleId" });

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_ChallengeParticipationId_ChallengeId_CycleId",
                table: "Submissions",
                columns: new[] { "ChallengeParticipationId", "ChallengeId", "CycleId" });

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_ClaimantId_SubmittedAt",
                table: "Submissions",
                columns: new[] { "ClaimantId", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_CycleId_ClaimantId",
                table: "Submissions",
                columns: new[] { "CycleId", "ClaimantId" });

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_Status_SubmittedAt",
                table: "Submissions",
                columns: new[] { "Status", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Submissions_TaskId_ChallengeId",
                table: "Submissions",
                columns: new[] { "TaskId", "ChallengeId" });

            migrationBuilder.CreateIndex(
                name: "IX_XPEntries_AwardCategoryId_CycleId",
                table: "XPEntries",
                columns: new[] { "AwardCategoryId", "CycleId" });

            migrationBuilder.CreateIndex(
                name: "IX_XPEntries_AwardedByParticipantId",
                table: "XPEntries",
                column: "AwardedByParticipantId");

            migrationBuilder.CreateIndex(
                name: "IX_XPEntries_ChallengeId_TaskId",
                table: "XPEntries",
                columns: new[] { "ChallengeId", "TaskId" });

            migrationBuilder.CreateIndex(
                name: "IX_XPEntries_ChallengeParticipationId",
                table: "XPEntries",
                column: "ChallengeParticipationId");

            migrationBuilder.CreateIndex(
                name: "IX_XPEntries_ChallengeParticipationId_ChallengeId_CycleId",
                table: "XPEntries",
                columns: new[] { "ChallengeParticipationId", "ChallengeId", "CycleId" });

            migrationBuilder.CreateIndex(
                name: "IX_XPEntries_CycleId_ParticipantId",
                table: "XPEntries",
                columns: new[] { "CycleId", "ParticipantId" });

            migrationBuilder.CreateIndex(
                name: "IX_XPEntries_CycleTeamId_CycleId",
                table: "XPEntries",
                columns: new[] { "CycleTeamId", "CycleId" });

            migrationBuilder.CreateIndex(
                name: "IX_XPEntries_RaidSessionId_CycleId",
                table: "XPEntries",
                columns: new[] { "RaidSessionId", "CycleId" });

            migrationBuilder.CreateIndex(
                name: "IX_XPEntries_RaidSessionId_ParticipantId",
                table: "XPEntries",
                columns: new[] { "RaidSessionId", "ParticipantId" });

            migrationBuilder.CreateIndex(
                name: "IX_XPEntries_ReversesEntryId",
                table: "XPEntries",
                column: "ReversesEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_XPEntries_ReversesEntryId_ParticipantId_CycleId_SourceType",
                table: "XPEntries",
                columns: new[] { "ReversesEntryId", "ParticipantId", "CycleId", "SourceType" });

            migrationBuilder.CreateIndex(
                name: "IX_XPEntries_SubmissionId_ChallengeId_TaskId_CycleId",
                table: "XPEntries",
                columns: new[] { "SubmissionId", "ChallengeId", "TaskId", "CycleId" });

            migrationBuilder.CreateIndex(
                name: "UX_XPEntries_TaskApprovalGrant_Submission_Participant",
                table: "XPEntries",
                columns: new[] { "SubmissionId", "ParticipantId" },
                unique: true,
                filter: "[EntryType] = 'Grant' AND [SourceType] = 'TaskApproval'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChallengeDeadlineChanges");

            migrationBuilder.DropTable(
                name: "ChallengeParticipationMembers");

            migrationBuilder.DropTable(
                name: "ChallengeTeamPolicies");

            migrationBuilder.DropTable(
                name: "CycleEvents");

            migrationBuilder.DropTable(
                name: "CycleTeamMembers");

            migrationBuilder.DropTable(
                name: "ParticipantChallengeDeadlineEvents");

            migrationBuilder.DropTable(
                name: "RaidEntitlements");

            migrationBuilder.DropTable(
                name: "RaidParticipations");

            migrationBuilder.DropTable(
                name: "SubmissionEvents");

            migrationBuilder.DropTable(
                name: "SubmissionEvidence");

            migrationBuilder.DropTable(
                name: "XPEntries");

            migrationBuilder.DropTable(
                name: "AwardCategories");

            migrationBuilder.DropTable(
                name: "CycleTeams");

            migrationBuilder.DropTable(
                name: "RaidSessions");

            migrationBuilder.DropTable(
                name: "SubmissionBeneficiaries");

            migrationBuilder.DropTable(
                name: "Submissions");

            migrationBuilder.DropTable(
                name: "ChallengeParticipations");

            migrationBuilder.DropTable(
                name: "ChallengeTasks");

            migrationBuilder.DropTable(
                name: "CycleParticipants");

            migrationBuilder.DropTable(
                name: "Challenges");

            migrationBuilder.DropTable(
                name: "Cycles");

            migrationBuilder.DropTable(
                name: "Participants");
        }
    }
}
