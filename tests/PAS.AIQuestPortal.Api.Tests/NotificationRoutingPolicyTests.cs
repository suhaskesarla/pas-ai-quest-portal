using PAS.AIQuestPortal.Api.Notifications;
using Xunit;

namespace PAS.AIQuestPortal.Api.Tests;

public sealed class NotificationRoutingPolicyTests
{
    public static IEnumerable<object[]> InvalidEventDestinations()
    {
        Guid participant = Guid.Parse("11111111-1111-4111-8111-111111111111"); NotificationDestination[] destinations = [NotificationDestinations.General(), NotificationDestinations.Managers(), NotificationDestinations.Participant(participant)];
        foreach (NotificationEventType type in Enum.GetValues<NotificationEventType>())
        {
            NotificationDestination valid = type switch { NotificationEventType.ChallengePublished or NotificationEventType.LeaderboardAnnouncement => destinations[0], NotificationEventType.SubmissionSubmitted or NotificationEventType.SubmissionResubmitted => destinations[1], _ => destinations[2] };
            foreach (NotificationDestination destination in destinations.Where(x => x != valid)) yield return [type, destination];
        }
    }

    [Theory]
    [MemberData(nameof(InvalidEventDestinations))]
    public void Event_destination_matrix_rejects_every_invalid_pair(NotificationEventType eventType, NotificationDestination destination) => Assert.Throws<ArgumentException>(() => NotificationRoutingPolicy.Validate(eventType, destination));
}
