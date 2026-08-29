import { test } from '@playwright/test'

const expectedLedger = {
  taskApprovalGrant: 25,
  manualAwardGrant: 10,
  reversal: -10,
  correction: 5,
  taskApprovalEffective: 20,
  manualAwardEffective: 10,
  netAdjustments: -5,
  totalXp: 30,
} as const

test.describe('unified authoritative ledger reconciliation', () => {
  test.fixme('same isolated ledger reconciles across every manager and participant surface', async () => {
    // Required deterministic setup (supported commands only):
    // 1. Manager creates a uniquely coded Active cycle through POST /api/manager/cycles and retains its ID.
    // 2. Enroll Participant Alpha through POST /api/manager/cycles/{cycleId}/participants. Never use
    //    defaultCycleId and never count XP from the separate seeded cycle.
    // 3. In that same isolated CycleId, create/publish an Individual/Text challenge with one +25 task,
    //    then submit and approve it => TaskApproval Grant +25. Individual scoring deliberately avoids
    //    unsupported ChallengeParticipation/team construction.
    // 4. Create one ManualAward Grant +10 in that same CycleId with a unique requestId/category/reason.
    // 5. Correct the original task Grant 25→15 => Reversal -10.
    // 6. Correct that same original task Grant 15→20 => Correction +5.
    //
    // Exact arithmetic:
    // TaskApproval source = +25 -10 +5 = 20.
    // ManualAward source = +10.
    // Adjustments disclosure = -10 +5 = -5 (already included; never added twice).
    // Total XP = 20 +10 = 30.
    //
    // Authoritative assertions explicitly select the created CycleId on every surface:
    // - Manager Scoresheet row: Task 20, Manual 10, Adjustments -5, Total 30.
    // - Drill-down: four immutable rows +25/+10/-10/+5 with source/type/reason/reference.
    // - Dashboard: total 30 after navigation/refetch.
    // - XP Activity: friendly Task Approval and Manual Award labels with all signed rows/reasons.
    // - Individual Leaderboard: API-provided rank and total 30; no frontend renumbering.
    // - zero-XP roster peer remains in Scoresheet; appears in leaderboard only when Active.
    //
    // Raid is intentionally excluded: no supported Raid creation/admin command currently exists.
    // Raid source/pass projection remains covered by SQL/import/read-model tests.
    void expectedLedger
  })
})

// TEST_FIXME blocker: Cycle Administration can create the isolated cycle/enrollment and Challenge
// Administration can create the Individual +25 task, but the product has no supported AwardCategory
// creation endpoint. The only deterministic active category is scoped to the seeded cycle, so the
// required isolated-cycle ManualAward cannot be created without a direct database write. Enable only
// when a supported deterministic category setup contract exists; do not bypass the domain/API.
