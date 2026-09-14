using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using PAS.AIQuestPortal.Api.Configuration;
using PAS.AIQuestPortal.Api.Data;

namespace PAS.AIQuestPortal.Api.Authentication;

internal sealed class EntraQuestClaimsTransformation(QuestDbContext db) : IClaimsTransformation
{
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true || QuestInternalIdentity.HasMarker(principal, QuestClaimTypes.ResolutionComplete)) return principal;

        ClaimsIdentity identity = QuestInternalIdentity.CreateResolutionIdentity();
        identity.AddClaim(new Claim(QuestClaimTypes.ResolutionComplete, "true"));
        if (!Guid.TryParse(principal.FindFirstValue("tid"), out Guid tenantId) ||
            !Guid.TryParse(principal.FindFirstValue("oid"), out Guid objectId))
        {
            identity.AddClaim(new Claim(QuestClaimTypes.AccessDenied, "true"));
            principal.AddIdentity(identity);
            return principal;
        }

        var resolved = await (
            from external in db.ParticipantExternalIdentities.AsNoTracking()
            join participant in db.Participants.AsNoTracking() on external.ParticipantId equals participant.Id
            where external.Provider == AuthenticationModes.Entra && external.TenantId == tenantId && external.SubjectId == objectId && external.VerifiedAt != null
            select new { participant.Id, participant.DisplayName, participant.IsActive }).SingleOrDefaultAsync();

        if (resolved is null)
        {
            bool mappingExists = await db.ParticipantExternalIdentities.AsNoTracking().AnyAsync(x =>
                x.Provider == AuthenticationModes.Entra && x.TenantId == tenantId && x.SubjectId == objectId);
            if (mappingExists) identity.AddClaim(new Claim(QuestClaimTypes.AccessDenied, "true"));
            principal.AddIdentity(identity);
            return principal;
        }
        if (!resolved.IsActive)
        {
            identity.AddClaim(new Claim(QuestClaimTypes.AccessDenied, "true"));
            principal.AddIdentity(identity);
            return principal;
        }

        HashSet<string> tokenRoles = principal.Identities.Where(x => !string.Equals(x.AuthenticationType, QuestInternalIdentity.AuthenticationType, StringComparison.Ordinal))
            .SelectMany(x => x.FindAll("roles")).Select(x => x.Value).ToHashSet(StringComparer.Ordinal);
        var capabilities = new List<string> { QuestRoles.Participant };
        if (tokenRoles.Contains(QuestRoles.Manager)) capabilities.Add(QuestRoles.Manager);
        QuestInternalIdentity.AddResolvedClaims(identity, resolved.Id, resolved.DisplayName, capabilities);
        principal.AddIdentity(identity);
        return principal;
    }
}
