using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PAS.AIQuestPortal.Api.Authentication;
using PAS.AIQuestPortal.Api.Configuration;
using PAS.AIQuestPortal.Api.Data;
using PAS.AIQuestPortal.Api.Notifications;
using PAS.AIQuestPortal.Api.Workflow;
using Xunit;

namespace PAS.AIQuestPortal.Api.Tests;

public sealed class TeamsIdentityProvisioningConcurrencyTests : IAsyncLifetime
{
    private readonly Guid tenant=Guid.NewGuid(), manager=Guid.NewGuid(), participantA=Guid.NewGuid(), participantB=Guid.NewGuid();
    private readonly string connection;
    private readonly DateTimeOffset now=new(2026,8,31,12,0,0,TimeSpan.Zero);
    public TeamsIdentityProvisioningConcurrencyTests()
    {
        string basis=Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION")??throw new InvalidOperationException("TEST_SQL_CONNECTION is required.");
        connection=new SqlConnectionStringBuilder(basis){InitialCatalog=$"PasAiQuestTeamsIdentityRace_{Guid.NewGuid():N}"}.ConnectionString;
    }
    public async Task InitializeAsync(){await using QuestDbContext db=Context(App("setup"));await db.Database.MigrateAsync();db.Participants.AddRange(
        new Participant{Id=manager,DisplayName="Synthetic manager",CreatedAt=now},new Participant{Id=participantA,DisplayName="Synthetic A",CreatedAt=now},new Participant{Id=participantB,DisplayName="Synthetic B",CreatedAt=now});await db.SaveChangesAsync();}
    public async Task DisposeAsync(){await using QuestDbContext db=Context(App("cleanup"));await db.Database.EnsureDeletedAsync();}

    [Fact]
    public async Task Concurrent_exact_replay_creates_one_mapping_and_both_succeed()
    {
        Guid oid=Guid.NewGuid(); var command=new ProvisionTeamsExternalIdentityRequest(participantA,tenant,oid);
        (object first,object second)=await Race(command,command);
        var responses=new[]{Assert.IsType<ProvisionTeamsExternalIdentityResponse>(first),Assert.IsType<ProvisionTeamsExternalIdentityResponse>(second)};
        Assert.Single(responses,x=>!x.Replay);Assert.Single(responses,x=>x.Replay);
        await using QuestDbContext verify=Context(App("verify"));Assert.Single(await verify.ParticipantExternalIdentities.ToListAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Concurrent_same_participant_different_oid_has_one_winner(bool firstOidWins)
    {
        Guid oidA=Guid.NewGuid(),oidB=Guid.NewGuid();var winner=new ProvisionTeamsExternalIdentityRequest(participantA,tenant,firstOidWins?oidA:oidB);var loser=new ProvisionTeamsExternalIdentityRequest(participantA,tenant,firstOidWins?oidB:oidA);
        (object first,object second)=await Race(winner,loser);Assert.IsType<ProvisionTeamsExternalIdentityResponse>(first);AssertConflict(second);
        await using QuestDbContext verify=Context(App("verify"));ParticipantExternalIdentity row=Assert.Single(await verify.ParticipantExternalIdentities.ToListAsync());Assert.Equal(winner.Oid,row.SubjectId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Concurrent_same_oid_different_participant_has_one_winner(bool participantAWins)
    {
        Guid oid=Guid.NewGuid();Guid winnerId=participantAWins?participantA:participantB,loserId=participantAWins?participantB:participantA;
        (object first,object second)=await Race(new(winnerId,tenant,oid),new(loserId,tenant,oid));Assert.IsType<ProvisionTeamsExternalIdentityResponse>(first);AssertConflict(second);
        await using QuestDbContext verify=Context(App("verify"));ParticipantExternalIdentity row=Assert.Single(await verify.ParticipantExternalIdentities.ToListAsync());Assert.Equal(winnerId,row.ParticipantId);
    }

    private async Task<(object First,object Second)> Race(ProvisionTeamsExternalIdentityRequest firstCommand,ProvisionTeamsExternalIdentityRequest secondCommand)
    {
        string firstApp=App("winner"),secondApp=App("waiter");await using QuestDbContext firstDb=Context(firstApp);await using QuestDbContext secondDb=Context(secondApp);var gate=new AfterLocksGate();
        Task<ProvisionTeamsExternalIdentityResponse> first=Service(firstDb,gate).ProvisionIdentityAsync(firstCommand,gate.Token);
        Task<ProvisionTeamsExternalIdentityResponse>? second=null;
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            second=Service(secondDb).ProvisionIdentityAsync(secondCommand,gate.Token);
            await WaitForApplicationLock(secondApp); // SQL DMV proves the second transaction is waiting while the first owns the shared identity applock.
            gate.Release();
            object firstResult=await Capture(first);object secondResult=await Capture(second);return(firstResult,secondResult);
        }
        finally { gate.Release(); await CaptureCleanup(first); if(second is not null)await CaptureCleanup(second); gate.Dispose(); }
    }

    private async Task WaitForApplicationLock(string applicationName)
    {
        await using QuestDbContext monitor=Context(App("monitor"));await monitor.Database.OpenConnectionAsync();Stopwatch timeout=Stopwatch.StartNew();
        while(timeout.Elapsed<TimeSpan.FromSeconds(15)){await using var command=monitor.Database.GetDbConnection().CreateCommand();command.CommandText="SELECT COUNT(*) FROM sys.dm_tran_locks l INNER JOIN sys.dm_exec_sessions s ON s.session_id=l.request_session_id WHERE s.program_name=@app AND l.resource_type='APPLICATION' AND l.request_status='WAIT';";var p=command.CreateParameter();p.ParameterName="@app";p.Value=applicationName;command.Parameters.Add(p);if(Convert.ToInt32(await command.ExecuteScalarAsync(),System.Globalization.CultureInfo.InvariantCulture)>0)return;await Task.Yield();}throw new TimeoutException("The competing identity command did not enter the application-lock wait.");
    }
    private TeamsProvisioningService Service(QuestDbContext db,ITeamsIdentityProvisioningConcurrencyHook? hook=null)=>new(db,new User(manager),Options.Create(new NotificationOptions{Provider="TeamsBot",PortalBaseUrl="https://portal.test",TeamsBot=new(){TenantId=tenant.ToString("D")}}),new Clock(now),NullLogger<TeamsProvisioningService>.Instance,hook);
    private QuestDbContext Context(string app)=>new(new DbContextOptionsBuilder<QuestDbContext>().UseSqlServer(new SqlConnectionStringBuilder(connection){ApplicationName=app}.ConnectionString).Options);
    private string App(string value)=>$"teams-identity-{value}-{Guid.NewGuid():N}";
    private static async Task<object> Capture(Task<ProvisionTeamsExternalIdentityResponse> task){try{return await task;}catch(WorkflowException error){return error;}}
    private static async Task CaptureCleanup(Task task){try{await task.WaitAsync(TimeSpan.FromSeconds(15));}catch(WorkflowException){} }
    private static void AssertConflict(object result){WorkflowException error=Assert.IsType<WorkflowException>(result);Assert.Equal(409,error.Status);Assert.Equal("TeamsExternalIdentityConflict",error.Code);}
    private sealed class User(Guid id):IQuestCurrentUser{public QuestUserIdentity Identity{get;}=new(true,id,"Synthetic manager",[QuestRoles.Manager]);}
    private sealed class Clock(DateTimeOffset value):TimeProvider{public override DateTimeOffset GetUtcNow()=>value;}
    private sealed class AfterLocksGate:ITeamsIdentityProvisioningConcurrencyHook,IDisposable
    {
        private readonly TaskCompletionSource release=new(TaskCreationOptions.RunContinuationsAsynchronously);private readonly CancellationTokenSource lifetime=new(TimeSpan.FromSeconds(45));
        public TaskCompletionSource Entered{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);public CancellationToken Token=>lifetime.Token;
        public Task BeforeLocksAsync(CancellationToken ct)=>Task.CompletedTask;public async Task AfterLocksAsync(CancellationToken ct){Entered.TrySetResult();await release.Task.WaitAsync(TimeSpan.FromSeconds(30),ct);}public void Release()=>release.TrySetResult();public void Dispose(){Release();lifetime.Dispose();}
    }
}
