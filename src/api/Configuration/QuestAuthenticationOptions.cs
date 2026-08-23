using System.ComponentModel.DataAnnotations;

namespace PAS.AIQuestPortal.Api.Configuration;

public static class AuthenticationModes
{
    public const string Stub = "Stub";
}

public sealed class QuestAuthenticationOptions
{
    public const string SectionName = "Authentication";

    [Required]
    public string Mode { get; init; } = AuthenticationModes.Stub;

    [Required]
    public StubIdentityOptions Stub { get; init; } = new();
}

public sealed class StubIdentityOptions
{
    [Required]
    public string Subject { get; init; } = "local-developer";

    [Required]
    public string DisplayName { get; init; } = "Local Developer";

    [MinLength(1)]
    public string[] Roles { get; init; } = ["Quest.Participant", "Quest.Manager"];
}
