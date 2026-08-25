SET QUOTED_IDENTIFIER ON;

DECLARE @Now datetimeoffset = '2026-08-24T08:00:00+00:00';

INSERT INTO Participants (Id, EntraObjectId, DisplayName, IsActive, CreatedAt) VALUES
('11111111-1111-4111-8111-111111111111', NULL, 'Synthetic Participant', 1, @Now),
('22222222-2222-4222-8222-222222222222', NULL, 'Synthetic Beneficiary', 1, @Now),
('aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', NULL, 'Synthetic Manager', 1, @Now);

INSERT INTO Cycles (Id, Code, Name, Status, StartsAt, EndsAt, ThemeConfiguration, CreatedAt, CreatedByParticipantId) VALUES
('60000000-0000-4000-8000-000000000001', 'SYN-OLD', 'Synthetic prior reporting cycle', 'Finalised', '2025-07-01T00:00:00+00:00', '2025-07-31T23:59:59+00:00', NULL, @Now, 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa');

INSERT INTO CycleParticipants (CycleId, ParticipantId, Status, JoinedAt, LeftAt) VALUES
('60000000-0000-4000-8000-000000000001', '11111111-1111-4111-8111-111111111111', 'Active', @Now, NULL),
('60000000-0000-4000-8000-000000000001', '22222222-2222-4222-8222-222222222222', 'Active', @Now, NULL),
('60000000-0000-4000-8000-000000000001', 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', 'Active', @Now, NULL);

INSERT INTO Challenges (Id, CycleId, Name, Description, Category, Status, OpenAt, DueAt, CloseAt, HeroImageReference, GuideCharacter, CreatedAt, CreatedByParticipantId) VALUES
('61000000-0000-4000-8000-000000000001', '60000000-0000-4000-8000-000000000001', 'Synthetic shared quest', 'A deterministic whole-team Step 6 journey.', 'QA', 'Open', '2026-01-01T00:00:00+00:00', '2029-12-01T00:00:00+00:00', '2029-12-31T00:00:00+00:00', NULL, NULL, @Now, 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa');

INSERT INTO ChallengeTasks (Id, ChallengeId, Name, Description, XP, EvidenceRequirement, CustomEvidenceRequirement, ScoringMode, SortOrder) VALUES
('62000000-0000-4000-8000-000000000001', '61000000-0000-4000-8000-000000000001', 'Synthetic shared task', 'Submit one shared text response.', 25, 'Text', NULL, 'WholeTeam', 1);

INSERT INTO ChallengeTeamPolicies (ChallengeId, FormationMode, MinMembers, MaxMembers, AllowSolo, FormationDeadline, LockAfterStart) VALUES
('61000000-0000-4000-8000-000000000001', 'Either', 2, 4, 0, NULL, 0);

INSERT INTO ChallengeParticipations (Id, ChallengeId, CycleId, CreatedAt, CreatedByParticipantId) VALUES
('63000000-0000-4000-8000-000000000001', '61000000-0000-4000-8000-000000000001', '60000000-0000-4000-8000-000000000001', @Now, '11111111-1111-4111-8111-111111111111');

INSERT INTO ChallengeParticipationMembers (ChallengeParticipationId, ChallengeId, CycleId, ParticipantId, CycleTeamIdAtParticipation, JoinedSnapshotAt) VALUES
('63000000-0000-4000-8000-000000000001', '61000000-0000-4000-8000-000000000001', '60000000-0000-4000-8000-000000000001', '11111111-1111-4111-8111-111111111111', NULL, @Now),
('63000000-0000-4000-8000-000000000001', '61000000-0000-4000-8000-000000000001', '60000000-0000-4000-8000-000000000001', '22222222-2222-4222-8222-222222222222', NULL, @Now);
