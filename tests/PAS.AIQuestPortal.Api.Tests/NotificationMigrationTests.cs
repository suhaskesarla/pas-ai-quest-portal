using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PAS.AIQuestPortal.Api.Data;
using Xunit;

namespace PAS.AIQuestPortal.Api.Tests;

public sealed class NotificationMigrationTests
{
    private const string PreviousMigration = "20260829112813_AddRaidAdministration";
    private const string NotificationMigration = "20260830100636_AddNotificationFoundation";
    private const string DeliveryMigration = "20260831023455_AddTeamsNotificationDelivery";

    [Fact]
    public async Task Real_upgrade_preserves_history_adds_no_notifications_and_enforces_physical_constraints()
    {
        string basis = Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION") ?? throw new InvalidOperationException("TEST_SQL_CONNECTION is required.");
        string connection = new SqlConnectionStringBuilder(basis) { InitialCatalog = $"PasAiQuestNotificationMigration_{Guid.NewGuid():N}" }.ConnectionString;
        await using QuestDbContext db = Context(connection);
        try
        {
            IMigrator migrator = db.GetService<IMigrator>(); await migrator.MigrateAsync(PreviousMigration);
            Assert.Null(await db.Database.SqlQueryRaw<int?>("SELECT OBJECT_ID('NotificationOutbox') AS [Value]").SingleAsync());
            (Guid participant, Guid cycle, Guid challenge, Guid submission, Guid xp, Guid raid) = await SeedHistory(db);

            await migrator.MigrateAsync(NotificationMigration);

            Assert.Equal(0, await db.NotificationOutbox.CountAsync());
            Assert.True(await db.Participants.AnyAsync(x => x.Id == participant)); Assert.True(await db.Cycles.AnyAsync(x => x.Id == cycle)); Assert.True(await db.Challenges.AnyAsync(x => x.Id == challenge)); Assert.True(await db.Submissions.AnyAsync(x => x.Id == submission)); Assert.True(await db.XPEntries.AnyAsync(x => x.Id == xp)); Assert.True(await db.RaidSessions.AnyAsync(x => x.Id == raid));

            Guid eventId = Guid.NewGuid(); await Insert(db, Guid.NewGuid(), eventId, "ConfiguredAudience", "QUEST_GENERAL_AUDIENCE", null, 1, "Pending", 0, null);
            await Insert(db, Guid.NewGuid(), Guid.NewGuid(), "ParticipantPrivate", $"participant:{participant:N}", participant, 1, "Pending", 0, null);
            await Assert.ThrowsAsync<SqlException>(() => Insert(db, Guid.NewGuid(), eventId, "ConfiguredAudience", "QUEST_GENERAL_AUDIENCE", null, 1, "Pending", 0, null));
            await Assert.ThrowsAsync<SqlException>(() => Insert(db, Guid.NewGuid(), Guid.NewGuid(), "ConfiguredAudience", "QUEST_GENERAL_AUDIENCE", null, 1, "Pending", -1, null));
            await Assert.ThrowsAsync<SqlException>(() => Insert(db, Guid.NewGuid(), Guid.NewGuid(), "ConfiguredAudience", "QUEST_GENERAL_AUDIENCE", null, 1, "Captured", 0, null));
            await Assert.ThrowsAsync<SqlException>(() => Insert(db, Guid.NewGuid(), Guid.NewGuid(), "ConfiguredAudience", "QUEST_GENERAL_AUDIENCE", null, 0, "Pending", 0, null));
            await Assert.ThrowsAsync<SqlException>(() => Insert(db, Guid.NewGuid(), Guid.NewGuid(), "ParticipantPrivate", $"participant:{Guid.NewGuid():N}", participant, 1, "Pending", 0, null));
            await Assert.ThrowsAsync<SqlException>(() => Insert(db, Guid.NewGuid(), Guid.NewGuid(), "ParticipantPrivate", "participant:not-a-guid", participant, 1, "Pending", 0, null));
            await Assert.ThrowsAsync<SqlException>(() => Insert(db, Guid.NewGuid(), Guid.NewGuid(), "ConfiguredAudience", "QUEST_MANAGER_AUDIENCE", participant, 1, "Pending", 0, null));

            await migrator.MigrateAsync(DeliveryMigration);
            Assert.NotNull(await db.Database.SqlQueryRaw<int?>("SELECT OBJECT_ID('ParticipantExternalIdentities') AS [Value]").SingleAsync());
            Assert.NotNull(await db.Database.SqlQueryRaw<int?>("SELECT OBJECT_ID('TeamsConversationReferences') AS [Value]").SingleAsync());
            Assert.NotNull(await db.Database.SqlQueryRaw<int?>("SELECT OBJECT_ID('TeamsChannelDestinationCandidates') AS [Value]").SingleAsync());
            Assert.NotNull(await db.Database.SqlQueryRaw<int?>("SELECT OBJECT_ID('TeamsChannelDestinationAssignments') AS [Value]").SingleAsync());
            Assert.Equal(0, await db.ParticipantExternalIdentities.CountAsync()); Assert.Equal(0, await db.TeamsConversationReferences.CountAsync());
            Assert.Equal(0, await db.TeamsChannelDestinationCandidates.CountAsync()); Assert.Equal(0, await db.TeamsChannelDestinationAssignments.CountAsync());
            Guid tenant=Guid.NewGuid(),subject=Guid.NewGuid();Guid secondParticipant=await db.Participants.Where(x=>x.Id!=participant).Select(x=>x.Id).FirstAsync();
            await InsertExternalIdentity(db,Guid.NewGuid(),participant,tenant,subject);
            await Assert.ThrowsAsync<SqlException>(()=>InsertExternalIdentity(db,Guid.NewGuid(),participant,tenant,Guid.NewGuid()));
            await Assert.ThrowsAsync<SqlException>(()=>InsertExternalIdentity(db,Guid.NewGuid(),secondParticipant,tenant,subject));
        }
        finally { await db.Database.EnsureDeletedAsync(); }
    }

    private static async Task<(Guid Participant, Guid Cycle, Guid Challenge, Guid Submission, Guid Xp, Guid Raid)> SeedHistory(QuestDbContext db)
    {
        DateTimeOffset at = new(2026,8,29,9,0,0,TimeSpan.Zero); Guid manager=Guid.NewGuid(), participant=Guid.NewGuid(), cycle=Guid.NewGuid(), challenge=Guid.NewGuid(), task=Guid.NewGuid(), submission=Guid.NewGuid(), xp=Guid.NewGuid(), raid=Guid.NewGuid();
        db.Participants.AddRange(new Participant{Id=manager,DisplayName="Historical manager",CreatedAt=at},new Participant{Id=participant,DisplayName="Historical participant",CreatedAt=at});
        db.Cycles.Add(new Cycle{Id=cycle,Code=$"UP-{cycle:N}"[..20],Name="Upgrade cycle",Status=CycleStatus.Active,StartsAt=at.AddDays(-1),EndsAt=at.AddDays(2),CreatedAt=at,CreatedByParticipantId=manager});
        db.CycleEvents.Add(new CycleEvent{Id=Guid.NewGuid(),CycleId=cycle,SequenceNumber=1,EventType=CycleEventType.Created,ToStatus=CycleStatus.Active,Reason="Upgrade fixture",ActorId=manager,OccurredAt=at});
        db.CycleParticipants.Add(new CycleParticipant{CycleId=cycle,ParticipantId=participant,Status=CycleParticipantStatus.Active,JoinedAt=at});
        db.CycleParticipantEvents.Add(new CycleParticipantEvent{Id=Guid.NewGuid(),CycleId=cycle,ParticipantId=participant,SequenceNumber=1,EventType=CycleParticipantEventType.Enrolled,ToStatus=CycleParticipantStatus.Active,Reason="Upgrade fixture",ActorId=manager,OccurredAt=at});
        db.Challenges.Add(new Challenge{Id=challenge,CycleId=cycle,Name="Upgrade challenge",Status=ChallengeStatus.Open,OpenAt=at.AddDays(-1),DueAt=at.AddDays(1),CloseAt=at.AddDays(2),CreatedByParticipantId=manager,CreatedAt=at});
        db.ChallengeTasks.Add(new ChallengeTask{Id=task,ChallengeId=challenge,Name="Upgrade task",XP=25,EvidenceRequirement=EvidenceRequirement.Text,ScoringMode=ScoringMode.Individual,SortOrder=1});
        db.Submissions.Add(new Submission{Id=submission,ChallengeId=challenge,TaskId=task,CycleId=cycle,ClaimantId=participant,Status=SubmissionStatus.Approved,SubmittedAt=at,LastUpdatedAt=at});
        db.SubmissionBeneficiaries.Add(new SubmissionBeneficiary{SubmissionId=submission,ParticipantId=participant,CycleId=cycle,AddedByParticipantId=participant});
        db.RaidSessions.Add(new RaidSession{Id=raid,CycleId=cycle,Name="Upgrade raid",OccurredAt=at});
        db.XPEntries.Add(new XPEntry{Id=xp,ParticipantId=participant,CycleId=cycle,Amount=25,EntryType=XPEntryType.Grant,SourceType=XPSourceType.TaskApproval,ChallengeId=challenge,TaskId=task,SubmissionId=submission,Reason="Upgrade award",AwardedByParticipantId=manager,AwardedAt=at});
        await db.SaveChangesAsync(); db.ChangeTracker.Clear(); return(participant,cycle,challenge,submission,xp,raid);
    }

    private static Task<int> Insert(QuestDbContext db, Guid id, Guid eventId, string destinationType, string destinationKey, Guid? participantId, int payloadVersion, string status, int attempts, DateTimeOffset? completedAt) => db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO [NotificationOutbox] ([Id],[EventId],[EventType],[DestinationType],[DestinationKey],[RecipientParticipantId],[AggregateType],[AggregateId],[PayloadVersion],[PayloadJson],[Status],[AttemptCount],[NextAttemptAt],[CreatedAt],[CompletedAt]) VALUES ({id},{eventId},'LeaderboardAnnouncement',{destinationType},{destinationKey},{participantId},'Leaderboard',{Guid.NewGuid()},{payloadVersion},'{{}}',{status},{attempts},{DateTimeOffset.UtcNow},{DateTimeOffset.UtcNow},{completedAt})");
    private static Task<int> InsertExternalIdentity(QuestDbContext db,Guid id,Guid participant,Guid tenant,Guid subject)=>db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO [ParticipantExternalIdentities] ([Id],[ParticipantId],[Provider],[TenantId],[SubjectId],[CreatedAt],[VerifiedAt]) VALUES ({id},{participant},'Entra',{tenant},{subject},{DateTimeOffset.UtcNow},{DateTimeOffset.UtcNow})");
    private static QuestDbContext Context(string connection) => new(new DbContextOptionsBuilder<QuestDbContext>().UseSqlServer(connection).Options);
}
