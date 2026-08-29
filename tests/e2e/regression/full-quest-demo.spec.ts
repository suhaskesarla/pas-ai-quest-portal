import { test } from '@playwright/test'

const demoDesign = {
  actors: { manager: 'Manager Alpha', participant: 'Participant Alpha', optionalBeneficiary: 'Participant Beta' },
  cycle: {
    strategy: 'DETERMINISTIC_DEMO_SEED_REQUIRED',
    rule: 'Use the seeded Active cycle explicitly by stable code/id; do not create a cycle or rely on defaultCycleId.',
  },
  challenge: {
    strategy: 'create through Manager Challenge Administration in the seeded cycle',
    task: { name: 'Canonical 25 XP task', xp: 25, scoringMode: 'Individual', evidenceRequirement: 'Text' },
    dates: 'OpenAt before test time; DueAt and CloseAt after test time. Persisted publish result must be Open.',
    participation: 'Individual; no ChallengeParticipation, CycleTeam, or group construction is assumed.',
  },
  ledger: {
    seededManualAward: {
      amount: 10,
      reason: 'Synthetic local-development showcase award',
      category: 'Synthetic Welcome Award',
      sourceId: '60000000-0000-4000-8000-00000000000a',
    },
    taskApprovalGrant: 25,
    newManualAward: {
      amount: 10,
      reason: 'Canonical full-demo manual award',
      category: 'Synthetic Welcome Award',
      requestId: 'unique per full-demo run and distinct from the seeded source ID',
    },
    downwardReversal: -10, upwardCorrection: 5,
    effectiveTaskApproval: 20, effectiveManualAwards: 20, finalTotal: 40,
  },
} as const

test.describe('canonical full-system demo design', () => {
  test.fixme('seeded-cycle individual journey reconciles exact append-only ledger across roles', async () => {
    // Preconditions: clean deterministic bootstrap. Participant Alpha begins with the immutable seeded
    // ManualAward Grant +10 identified by source ID 60000000-0000-4000-8000-00000000000a,
    // reason "Synthetic local-development showcase award", and category "Synthetic Welcome Award".
    //
    // Manager Alpha explicitly selects the seeded cycle, creates the exact Individual/Text/+25 Draft,
    // and publishes it. Assert the API-confirmed persisted ChallengeStatus.Open.
    // Participant Alpha discovers it and submits text. Manager requests NeedsEvidence with feedback.
    // Participant views feedback/history, replaces text, and resubmits the same logical submission.
    // Manager approves; assert one TaskApproval Grant +25 and Approved history.
    // Manager creates a distinct ManualAward +10 with reason "Canonical full-demo manual award", the
    // server-returned Synthetic Welcome Award category identity, and a new recorded requestId. Exact
    // replay of that requestId is idempotent; it must not be confused with the seeded ManualAward.
    // Manager corrects the original task Grant 25→15 (-10), then 15→20 (+5).
    // Exact arithmetic: seeded ManualAward 10 + TaskApproval (25-10+5) 20 + new ManualAward 10 = 40.
    // Scoresheet must show Task 20, Manual 20, Adjustments -5, Total 40. Drill-down/activity must retain
    // the seeded +10, new +25, new +10, -10 and +5 rows and distinguish both ManualAwards by the stable
    // reason/category/source-or-request identities above.
    // Participant Dashboard must show 40; Leaderboard must show the API-provided rank and total 40.
    // No exact rank is asserted unless authoritative seeded competitor totals make it deterministic.
    //
    // Team/group assertions are excluded from this individual journey. Existing reporting specs own the
    // deterministic seeded My Cycle Team/Challenge Groups; Cycle Admin creates neither concept.
    // Raid is excluded because no supported deterministic Raid write contract exists.
    //
    // CYCLE_ADMIN_PENDING_IMPLEMENTATION optional continuation: after the frontend lands, transition the
    // explicitly selected cycle Active→Closing→Finalised with reasons, then VIEW historical reporting.
    // There is no reopen action. Challenge state/dates must remain unchanged.
    void demoDesign
  })
})

export { demoDesign }
