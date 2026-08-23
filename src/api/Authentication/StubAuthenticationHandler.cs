using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using PAS.AIQuestPortal.Api.Configuration;

namespace PAS.AIQuestPortal.Api.Authentication;

public sealed class StubAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "DevelopmentStub";
    private readonly QuestAuthenticationOptions _authenticationOptions;

    public StubAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptions<QuestAuthenticationOptions> authenticationOptions)
        : base(options, logger, encoder)
    {
        _authenticationOptions = authenticationOptions.Value;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var stub = _authenticationOptions.Stub;
        var claims = new List<Claim>
        {
            new("sub", stub.Subject),
            new(ClaimTypes.NameIdentifier, stub.Subject),
            new(ClaimTypes.Name, stub.DisplayName)
        };
        claims.AddRange(stub.Roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var identity = new ClaimsIdentity(claims, SchemeName, ClaimTypes.Name, ClaimTypes.Role);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
