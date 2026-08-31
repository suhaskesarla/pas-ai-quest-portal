using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PAS.AIQuestPortal.Api.Data;
using PAS.AIQuestPortal.Api.Notifications;
using PAS.AIQuestPortal.Api.Authentication;
using PAS.AIQuestPortal.Api.Configuration;
using Xunit;

namespace PAS.AIQuestPortal.Api.Tests;

public sealed class TeamsNotificationTransportSecurityTests
{
    private readonly Guid tenant = Guid.NewGuid(), appId = Guid.NewGuid();
    private static readonly Uri Trusted = new("https://smba.trafficmanager.net/amer/");

    [Fact]
    public async Task Untrusted_service_url_is_rejected_before_token_or_message_request()
    {
        var handler = new StageHandler(); var transport = Transport(handler);
        TeamsTransportResult result = await transport.SendAsync(new(tenant, new Uri("https://attacker.example/"), "conversation", appId.ToString(), null), Card(), default);
        Assert.Equal(NotificationDeliveryOutcome.PermanentFailure, result.Outcome); Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(400, NotificationDeliveryOutcome.PermanentFailure)]
    [InlineData(401, NotificationDeliveryOutcome.PermanentFailure)]
    [InlineData(403, NotificationDeliveryOutcome.PermanentFailure)]
    [InlineData(429, NotificationDeliveryOutcome.RetryableFailure)]
    [InlineData(503, NotificationDeliveryOutcome.RetryableFailure)]
    public async Task Token_http_failures_are_pre_delivery_results(int status, NotificationDeliveryOutcome expected)
    {
        var handler = new StageHandler(tokenStatus: (HttpStatusCode)status); TeamsTransportResult result = await Transport(handler).SendAsync(Destination(), Card(), default);
        Assert.Equal(expected, result.Outcome); Assert.Equal(1, handler.Calls); Assert.Equal(0, handler.MessageCalls);
    }

    [Theory]
    [InlineData(true, false, NotificationDeliveryOutcome.RetryableFailure)]
    [InlineData(false, true, NotificationDeliveryOutcome.PermanentFailure)]
    public async Task Token_network_and_malformed_success_never_become_delivery_unknown(bool networkFailure, bool malformed, NotificationDeliveryOutcome expected)
    {
        var handler = new StageHandler(tokenNetworkFailure: networkFailure, malformedToken: malformed); TeamsTransportResult result = await Transport(handler).SendAsync(Destination(), Card(), default);
        Assert.Equal(expected, result.Outcome); Assert.Equal(0, handler.MessageCalls);
    }

    [Fact]
    public async Task Ambiguous_message_failure_is_delivery_unknown_and_permanent_message_4xx_is_permanent()
    {
        Assert.Equal(NotificationDeliveryOutcome.DeliveryUnknown, (await Transport(new StageHandler(messageNetworkFailure: true)).SendAsync(Destination(), Card(), default)).Outcome);
        Assert.Equal(NotificationDeliveryOutcome.PermanentFailure, (await Transport(new StageHandler(messageStatus: HttpStatusCode.BadRequest)).SendAsync(Destination(), Card(), default)).Outcome);
    }

    [Fact]
    public void Trusted_host_and_single_tenant_destination_configuration_are_required()
    {
        var validator = new BotConnectorServiceUrlValidator(); Assert.True(validator.IsTrusted(Trusted)); Assert.False(validator.IsTrusted(new("https://localhost/"))); Assert.False(validator.IsTrusted(new("https://smba.trafficmanager.net.attacker.example/")));
        Guid wrong = Guid.NewGuid(); var options = ValidOptions(); options.TeamsBot.ManagerDestination.TenantId = wrong.ToString();
        Assert.False(new NotificationOptionsValidator(new EnvironmentStub(), validator).Validate(null, options).Succeeded);
    }

    private NotificationOptions ValidOptions() => new() { Enabled=true, Provider="TeamsBot", PortalBaseUrl="https://portal.test", TeamsBot=new() { MicrosoftAppId=appId.ToString(), TenantId=tenant.ToString(), ClientSecret="secret", GeneralDestination=new(){TenantId=tenant.ToString(),ServiceUrl=Trusted.AbsoluteUri,ConversationId="general"}, ManagerDestination=new(){TenantId=tenant.ToString(),ServiceUrl=Trusted.AbsoluteUri,ConversationId="manager"} } };
    private BotConnectorTeamsProactiveTransport Transport(StageHandler handler) => new(new HttpClient(handler), Options.Create(ValidOptions()), new BotConnectorServiceUrlValidator());
    private TeamsResolvedDestination Destination() => new(tenant, Trusted, "conversation", appId.ToString(), null);
    private static RenderedNotification Card() => new("Title", "Body", "Open", "https://portal.test");
    private sealed class EnvironmentStub : Microsoft.Extensions.Hosting.IHostEnvironment { public string EnvironmentName { get; set; }="Production"; public string ApplicationName { get; set; }="Test"; public string ContentRootPath { get; set; }="."; public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }=new Microsoft.Extensions.FileProviders.NullFileProvider(); }
    private sealed class StageHandler(HttpStatusCode tokenStatus=HttpStatusCode.OK, HttpStatusCode messageStatus=HttpStatusCode.OK, bool tokenNetworkFailure=false, bool malformedToken=false, bool messageNetworkFailure=false) : HttpMessageHandler
    {
        public int Calls { get; private set; } public int MessageCalls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            if (request.RequestUri!.Host == "login.microsoftonline.com")
            {
                if (tokenNetworkFailure) throw new HttpRequestException("token network");
                var response = new HttpResponseMessage(tokenStatus) { Content = new StringContent(malformedToken ? "not-json" : "{\"access_token\":\"safe-token\"}") };
                if (tokenStatus == HttpStatusCode.TooManyRequests) response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(1));
                return Task.FromResult(response);
            }
            MessageCalls++; Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            if (messageNetworkFailure) throw new HttpRequestException("ambiguous message");
            return Task.FromResult(new HttpResponseMessage(messageStatus) { Content = new StringContent("{\"id\":\"message\"}") });
        }
    }
}

public sealed class TeamsBotActivityCaptureTests : IAsyncLifetime
{
    private readonly Guid tenant=Guid.NewGuid(), appId=Guid.NewGuid(), participant=Guid.NewGuid(), subject=Guid.NewGuid();
    private readonly string connection;
    private WebApplication app=null!; private HttpClient client=null!;
    private const string ServiceUrl="https://smba.trafficmanager.net/amer/";

    public TeamsBotActivityCaptureTests()
    {
        string basis=Environment.GetEnvironmentVariable("TEST_SQL_CONNECTION") ?? throw new InvalidOperationException("TEST_SQL_CONNECTION is required.");
        connection=new SqlConnectionStringBuilder(basis){InitialCatalog=$"PasAiQuestTeamsCapture_{Guid.NewGuid():N}"}.ConnectionString;
    }

    public async Task InitializeAsync()
    {
        await using (QuestDbContext db=Context()) { await db.Database.MigrateAsync(); db.Participants.Add(new(){Id=participant,DisplayName="Synthetic mapped",CreatedAt=DateTimeOffset.UtcNow}); db.ParticipantExternalIdentities.Add(new(){Id=Guid.NewGuid(),ParticipantId=participant,Provider="Entra",TenantId=tenant,SubjectId=subject,CreatedAt=DateTimeOffset.UtcNow,VerifiedAt=DateTimeOffset.UtcNow}); await db.SaveChangesAsync(); }
        WebApplicationBuilder builder=WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.UseTestServer(); builder.Services.AddLogging(); builder.Services.AddDbContext<QuestDbContext>(o=>o.UseSqlServer(connection)); builder.Services.AddSingleton(TimeProvider.System); builder.Services.AddSingleton<IBotConnectorServiceUrlValidator,BotConnectorServiceUrlValidator>(); builder.Services.AddSingleton<IOptions<NotificationOptions>>(Options.Create(OptionsValue())); builder.Services.AddScoped<ITeamsConversationReferenceWriter,TeamsConversationReferenceWriter>(); builder.Services.AddScoped<TeamsBotActivityCaptureService>(); builder.Services.AddScoped<TeamsProvisioningService>(); builder.Services.AddSingleton<IQuestCurrentUser>(new StaticCurrentUser(participant));
        builder.Services.AddAuthentication(TestBotHandler.SchemeName).AddScheme<AuthenticationSchemeOptions,TestBotHandler>(TestBotHandler.SchemeName,_=>{}); builder.Services.AddAuthorization(o=>{o.AddPolicy(TeamsBotActivityAuthentication.Policy,p=>p.AddAuthenticationSchemes(TestBotHandler.SchemeName).RequireAuthenticatedUser());o.AddPolicy(QuestPolicies.Manager,p=>p.RequireAuthenticatedUser().RequireRole(QuestRoles.Manager));});
        app=builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapPost("/api/teams/messages",async (HttpContext http,TeamsBotActivity activity,TeamsBotActivityCaptureService service,CancellationToken ct)=>await service.CaptureAsync(http.User,activity,ct)).RequireAuthorization(TeamsBotActivityAuthentication.Policy); app.MapTeamsProvisioning(); await app.StartAsync(); client=app.GetTestClient();
    }
    public async Task DisposeAsync(){client.Dispose();await app.DisposeAsync();await using QuestDbContext db=Context();await db.Database.EnsureDeletedAsync();}

    [Fact]
    public async Task Endpoint_requires_authenticated_bot_and_persists_only_verified_mapped_identity()
    {
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.PostAsJsonAsync("/api/teams/messages",Activity(subject))).StatusCode);
        using HttpRequestMessage request=Authenticated(Activity(subject)); HttpResponseMessage response=await client.SendAsync(request); Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        await using QuestDbContext verify=Context(); TeamsConversationReference reference=Assert.Single(await verify.TeamsConversationReferences.ToListAsync()); ParticipantExternalIdentity identity=await verify.ParticipantExternalIdentities.SingleAsync(); Assert.Equal(identity.Id,reference.ParticipantExternalIdentityId); Assert.Equal(tenant,reference.TenantId); Assert.Equal(ServiceUrl,reference.ServiceUrl);
        using HttpRequestMessage unknown=Authenticated(Activity(Guid.NewGuid(),conversation:"unknown")); Assert.Equal(HttpStatusCode.OK,(await client.SendAsync(unknown)).StatusCode); Assert.Single(await verify.TeamsConversationReferences.ToListAsync());
        Guid otherParticipant=Guid.NewGuid(),otherSubject=Guid.NewGuid(),otherIdentity=Guid.NewGuid(); verify.Participants.Add(new(){Id=otherParticipant,DisplayName="Synthetic other",CreatedAt=DateTimeOffset.UtcNow}); verify.ParticipantExternalIdentities.Add(new(){Id=otherIdentity,ParticipantId=otherParticipant,Provider="Entra",TenantId=tenant,SubjectId=otherSubject,CreatedAt=DateTimeOffset.UtcNow,VerifiedAt=DateTimeOffset.UtcNow}); await verify.SaveChangesAsync();
        using HttpRequestMessage overwrite=Authenticated(Activity(otherSubject)); Assert.Equal(HttpStatusCode.Conflict,(await client.SendAsync(overwrite)).StatusCode); verify.ChangeTracker.Clear(); Assert.Equal(identity.Id,(await verify.TeamsConversationReferences.SingleAsync()).ParticipantExternalIdentityId);
    }

    [Theory]
    [InlineData("https://attacker.example/",false,false)]
    [InlineData(ServiceUrl,true,false)]
    public async Task Endpoint_rejects_untrusted_service_url_and_wrong_tenant(string serviceUrl,bool wrongTenant,bool expectedOk)
    {
        using HttpRequestMessage request=Authenticated(Activity(subject,serviceUrl:serviceUrl,tenantId:wrongTenant?Guid.NewGuid():tenant),serviceUrl);
        HttpResponseMessage response=await client.SendAsync(request); Assert.Equal(expectedOk?HttpStatusCode.OK:wrongTenant?HttpStatusCode.Forbidden:HttpStatusCode.BadRequest,response.StatusCode);
        await using QuestDbContext verify=Context();Assert.Empty(await verify.TeamsConversationReferences.ToListAsync());
    }

    [Theory]
    [InlineData("raw", HttpStatusCode.BadRequest)]
    [InlineData("wrong-prefix", HttpStatusCode.BadRequest)]
    [InlineData("malformed", HttpStatusCode.BadRequest)]
    [InlineData("wrong-app", HttpStatusCode.BadRequest)]
    public async Task Recipient_requires_exact_real_Teams_28_app_id(string variant, HttpStatusCode expected)
    {
        string recipient = variant switch { "raw" => appId.ToString("D"), "wrong-prefix" => $"29:{appId:D}", "malformed" => "28:not-a-guid", "wrong-app" => $"28:{Guid.NewGuid():D}", _ => throw new InvalidOperationException() };
        using HttpRequestMessage request=Authenticated(Activity(subject,recipientId:recipient));
        Assert.Equal(expected,(await client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Recipient_is_required()
    {
        var activity=new{type="conversationUpdate",channelId="msteams",serviceUrl=ServiceUrl,from=new{id="29:user",aadObjectId=subject},conversation=new{id="personal",conversationType="personal"},channelData=new{tenant=new{id=tenant}}};
        using HttpRequestMessage request=Authenticated(activity); Assert.Equal(HttpStatusCode.BadRequest,(await client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Authenticated_channel_activity_creates_verified_candidate_not_private_reference()
    {
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.PostAsJsonAsync("/api/teams/messages",ChannelActivity())).StatusCode);
        using HttpRequestMessage request=Authenticated(ChannelActivity()); Assert.Equal(HttpStatusCode.OK,(await client.SendAsync(request)).StatusCode);
        await using QuestDbContext verify=Context(); TeamsChannelDestinationCandidate candidate=Assert.Single(await verify.TeamsChannelDestinationCandidates.ToListAsync());
        Assert.Equal("team-id",candidate.TeamId); Assert.Equal("channel-id",candidate.ChannelId); Assert.Empty(await verify.TeamsConversationReferences.ToListAsync());
    }

    [Theory]
    [InlineData("tenant", HttpStatusCode.Forbidden)]
    [InlineData("service", HttpStatusCode.BadRequest)]
    [InlineData("recipient", HttpStatusCode.BadRequest)]
    public async Task Channel_capture_rejects_wrong_tenant_untrusted_service_and_wrong_recipient(string variant,HttpStatusCode expected)
    {
        string service=variant=="service"?"https://attacker.example/":ServiceUrl;
        object activity=ChannelActivity(variant=="tenant"?Guid.NewGuid():tenant,variant=="recipient"?$"28:{Guid.NewGuid():D}":$"28:{appId:D}",service);
        using HttpRequestMessage request=Authenticated(activity,service);Assert.Equal(expected,(await client.SendAsync(request)).StatusCode);
        await using QuestDbContext verify=Context();Assert.Empty(await verify.TeamsChannelDestinationCandidates.ToListAsync());
    }

    [Fact]
    public async Task Manager_provisions_identity_replay_safely_and_assigns_only_verified_destination()
    {
        Guid newParticipant=Guid.NewGuid(), oid=Guid.NewGuid(); await using(QuestDbContext arrange=Context()){arrange.Participants.Add(new(){Id=newParticipant,DisplayName="Synthetic provisioned",CreatedAt=DateTimeOffset.UtcNow});await arrange.SaveChangesAsync();}
        var body=new{participantId=newParticipant,tenantId=tenant,oid};
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.PostAsJsonAsync("/api/manager/teams/external-identities",body)).StatusCode);
        using HttpRequestMessage first=Manager(HttpMethod.Post,"/api/manager/teams/external-identities",body);Assert.Equal(HttpStatusCode.OK,(await client.SendAsync(first)).StatusCode);
        using HttpRequestMessage replay=Manager(HttpMethod.Post,"/api/manager/teams/external-identities",body);var replayResponse=await client.SendAsync(replay);Assert.Equal(HttpStatusCode.OK,replayResponse.StatusCode);Assert.True((await replayResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("replay").GetBoolean());
        using HttpRequestMessage wrongTenant=Manager(HttpMethod.Post,"/api/manager/teams/external-identities",new{participantId=newParticipant,tenantId=Guid.NewGuid(),oid});Assert.Equal(HttpStatusCode.BadRequest,(await client.SendAsync(wrongTenant)).StatusCode);
        using HttpRequestMessage unknown=Manager(HttpMethod.Post,"/api/manager/teams/external-identities",new{participantId=Guid.NewGuid(),tenantId=tenant,oid=Guid.NewGuid()});Assert.Equal(HttpStatusCode.NotFound,(await client.SendAsync(unknown)).StatusCode);
        using HttpRequestMessage conflict=Manager(HttpMethod.Post,"/api/manager/teams/external-identities",new{participantId=newParticipant,tenantId=tenant,oid=subject});Assert.Equal(HttpStatusCode.Conflict,(await client.SendAsync(conflict)).StatusCode);
        using HttpRequestMessage channel=Authenticated(ChannelActivity());await client.SendAsync(channel);await using QuestDbContext verify=Context();Guid candidate=await verify.TeamsChannelDestinationCandidates.Select(x=>x.Id).SingleAsync();
        using HttpRequestMessage assign=Manager(HttpMethod.Post,$"/api/manager/teams/destinations/{NotificationDestinations.QuestGeneralAudience}",new{candidateId=candidate});Assert.Equal(HttpStatusCode.OK,(await client.SendAsync(assign)).StatusCode);
        using HttpRequestMessage arbitrary=Manager(HttpMethod.Post,$"/api/manager/teams/destinations/{NotificationDestinations.QuestManagerAudience}",new{candidateId=Guid.NewGuid(),serviceUrl="https://attacker.example/"});Assert.Equal(HttpStatusCode.NotFound,(await client.SendAsync(arbitrary)).StatusCode);
    }

    private NotificationOptions OptionsValue()=>new(){Enabled=true,Provider="TeamsBot",PrivateDeliveryEnabled=true,PortalBaseUrl="https://portal.test",TeamsBot=new(){MicrosoftAppId=appId.ToString(),TenantId=tenant.ToString(),ClientSecret="secret"}};
    private object Activity(Guid oid,string serviceUrl=ServiceUrl,Guid? tenantId=null,string conversation="personal",string? recipientId=null)=>new{type="conversationUpdate",channelId="msteams",serviceUrl,from=new{id="29:user",aadObjectId=oid},recipient=new{id=recipientId??$"28:{appId:D}"},conversation=new{id=conversation,conversationType="personal"},channelData=new{tenant=new{id=tenantId??tenant}}};
    private object ChannelActivity(Guid? tenantId=null,string? recipientId=null,string serviceUrl=ServiceUrl)=>new{type="installationUpdate",channelId="msteams",serviceUrl,from=new{id="29:installer"},recipient=new{id=recipientId??$"28:{appId:D}"},conversation=new{id="channel-conversation",conversationType="channel"},channelData=new{tenant=new{id=tenantId??tenant},team=new{id="team-id"},channel=new{id="channel-id"}}};
    private HttpRequestMessage Authenticated(object activity,string serviceUrl=ServiceUrl){var request=new HttpRequestMessage(HttpMethod.Post,"/api/teams/messages"){Content=JsonContent.Create(activity)};request.Headers.Add("X-Test-Bot","true");request.Headers.Add("X-Test-ServiceUrl",serviceUrl);return request;}
    private HttpRequestMessage Manager(HttpMethod method,string url,object body){var request=new HttpRequestMessage(method,url){Content=JsonContent.Create(body)};request.Headers.Add("X-Test-Manager","true");return request;}
    private QuestDbContext Context()=>new(new DbContextOptionsBuilder<QuestDbContext>().UseSqlServer(connection).Options);
    private sealed class TestBotHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder):AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
    {
        public const string SchemeName="TestBot";
        protected override Task<AuthenticateResult> HandleAuthenticateAsync(){if(Request.Headers.ContainsKey("X-Test-Manager")){var managerClaims=new[]{new Claim(ClaimTypes.Role,QuestRoles.Manager)};return Task.FromResult(AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity(managerClaims,SchemeName)),SchemeName)));}if(!Request.Headers.ContainsKey("X-Test-Bot"))return Task.FromResult(AuthenticateResult.NoResult());var claims=new[]{new Claim("serviceurl",Request.Headers["X-Test-ServiceUrl"].ToString())};var principal=new ClaimsPrincipal(new ClaimsIdentity(claims,SchemeName));return Task.FromResult(AuthenticateResult.Success(new(principal,SchemeName)));}
    }
    private sealed class StaticCurrentUser(Guid participantId):IQuestCurrentUser { public QuestUserIdentity Identity { get; }=new(true,participantId,"Synthetic manager",[QuestRoles.Manager]); }
}
