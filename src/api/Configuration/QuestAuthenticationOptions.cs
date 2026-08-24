using System.ComponentModel.DataAnnotations;

namespace PAS.AIQuestPortal.Api.Configuration;

public static class AuthenticationModes
{
    public const string Demo = "Demo";
    public const string Entra = "Entra";
}

public static class QuestRoles
{
    public const string Participant = "Quest.Participant";
    public const string Manager = "Quest.Manager";
}

public static class QuestPolicies
{
    public const string Authenticated = "QuestAuthenticated";
    public const string Participant = "QuestParticipant";
    public const string Manager = "QuestManager";
}

public sealed class QuestAuthenticationOptions
{
    public const string SectionName = "Authentication";

    [Required]
    public string Mode { get; init; } = "";

    public DemoAuthenticationOptions Demo { get; init; } = new();
}

public sealed class DemoAuthenticationOptions
{
    public string[] AllowedEnvironments { get; init; } = [];
    public DemoProfileOptions[] Profiles { get; init; } = [];
}

public sealed class DemoProfileOptions
{
    [Required] public string Key { get; init; } = "";
    [Required] public string Label { get; init; } = "";
    [Required] public string Subject { get; init; } = "";
    [Required] public string DisplayName { get; init; } = "";
    public Guid ParticipantId { get; init; }
    public string[] Roles { get; init; } = [];
    public bool Enabled { get; init; } = true;
}
