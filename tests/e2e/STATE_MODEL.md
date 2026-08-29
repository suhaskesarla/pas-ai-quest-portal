# Implemented product state model

This inventory distinguishes durable states from temporal UI views. Challenge Administration persists the pre-publish `Draft` state and persists `Open` when publish succeeds. While status remains `Open`, dates derive scheduled/not-yet-open, currently eligible/open-window, overdue/past-due, and beyond-close views. Forbidden transitions must be rejected by the API even when controls are hidden.

## Authentication (5 states)

| State | Entered by | Visible to | Allowed outgoing transitions | Forbidden / effects |
|---|---|---|---|---|
| Unauthenticated | No/cleared demo session | Anonymous | Select allowlisted demo profile | Protected APIs return 401 |
| Demo Participant | Server profile switch | Participant | Clear; switch to Manager | Manager endpoints 403; participant reporting/workflow enabled |
| Demo Manager | Server profile switch | Manager | Clear; switch to Participant | Participant-only APIs are not implied; manager navigation only |
| Switching | Client begins session change | Current browser | Authoritative `/api/auth/me` refresh completes/fails | Old privileged page/data must not leak into new role |
| Forbidden | Authenticated wrong role | Caller | Navigate to permitted area | Server returns 403; hidden navigation is not proof |

## Cycles and enrollment (6 durable combinations)

- `CycleStatus`: `Active → Closing → Finalised`; creation starts Active. No Draft, reopen, automatic clock transition, delete, or reverse transition.
- Active permits configuration edits, enrollment, and status changes. Closing permits finalisation but freezes configuration/enrollment. Finalised is historical/read-only except the separately approved TaskApproval correction workflow.
- `CycleParticipantStatus`: Active, Withdrawn, Inactive. While the cycle is Active, each status may move to either other status; a no-op is forbidden. Enrollment begins Active.
- `JoinedAt` is server assigned at enrollment and retained. `LeftAt=null` while Active; status departure/change to non-Active sets server time; reactivation clears it.
- Every enrollment/status change appends a sequenced `CycleParticipantEvent`. No update/delete of history.
- **Cycle Admin browser entry/edit/transition/enrollment states: `CYCLE_ADMIN_PENDING_IMPLEMENTATION`.** BA-015 API/SQL is frozen; UI labels/routes are intentionally unspecified.

## Challenge lifecycle: current implementation versus target (6 current states + target gaps)

| State | Entry/view | Allowed outgoing | Forbidden / reporting effect |
|---|---|---|---|
| Draft | `POST /api/manager/challenges` | `PUT` draft update; `POST .../publish` | Participant discovery; update after publish |
| Open-Scheduled (persisted Open, derived temporal label) | Publish before OpenAt | Time reaches OpenAt | Submission before OpenAt |
| Open-eligible (persisted Open) | OpenAt reached and deadline rules allow | Submit/resubmit; manager review | Cycle/month gating |
| Open-overdue (persisted Open) | now > DueAt | Submit only with applicable override | Ordinary submit/resubmit |
| Open-beyond-close (persisted Open) | now > CloseAt | Manager review/correction; participant only with explicit beyond-close override | Automatic persisted status rewrite |
| Participant override active | Manager-audited override | Submit until overridden effective boundary | Implicitly changing challenge status/dates |

**Current implemented contract:** only create, update and publish endpoints exist. Publish persists `ChallengeStatus.Open`. There is no Close or Archive endpoint. Current automated tests must assert Draft→Open and idempotent repeat publish.

**Frozen/target contract:** documentation describing Published/Closed/Archived behavior is `TARGET_NOT_IMPLEMENTED` / `IMPLEMENTATION_GAP` where it requires close/archive endpoints or a different persisted publish status. Enum members alone are not executable behavior. No close/archive browser or API tests are authored until implementation exists.

## CycleEvent model (5 enum values)

- `Created`: sequence 1 for cycle creation, `FromStatus=null`, `ToStatus=Active`, append-only.
- `StatusChanged`: sequenced lifecycle event for Active→Closing or Closing→Finalised with actor, reason and server timestamp.
- `Reopened`: technically present in the enum but **DORMANT / NOT EXPOSED BY CYCLE ADMIN**. BA-015 forbids reopen; no UI/API test is allowed.
- `CorrectionAuthorised`: technically present in the enum but no current correction endpoint emits it; **DORMANT / NOT CURRENTLY EXPOSED**. Do not fabricate a transition/test.
- `CorrectionRecorded`: emitted by the current XP-correction workflow with lifecycle statuses null, manager actor/reason/time, `RelatedXPEntryId`, and correlation ID. It is not a cycle lifecycle transition.

Sequence numbers are unique and increasing within a cycle. Cycle lifecycle events and XP-correction events share the append-only stream but retain distinct shapes/purposes. No event may be updated or deleted.

## Participation and submission (12 states)

- Participation: Individual claimant-only; WholeTeam complete selected participation; ClaimantSelectsBeneficiaries subset of one participation; AttendanceBased manager-recorded/no participant submission.
- Membership snapshots are challenge-specific and never inferred from Cycle Team.
- Submission statuses and transitions:
  - none → `Submitted` (creates Submitted event; supported current evidence)
  - `Submitted` → `NeedsEvidence`, `Approved`, or `Rejected`
  - `NeedsEvidence` → `Resubmitted` on claimant evidence update
  - `Resubmitted` → `NeedsEvidence`, `Approved`, or `Rejected`
  - `Approved` and `Rejected` are terminal for the submission workflow
  - `UnderReview` exists in the domain enum but no separate public transition is currently implemented; do not invent one
- Approval is atomic across beneficiaries and appends TaskApproval Grants. NeedsEvidence and Rejected award zero XP.
- Review remains permitted after challenge close. Submission/resubmission remains subject to BA-006 effective deadline and Active membership revalidation inside the transaction.

## Evidence (7 states)

| Requirement/state | Accepted content | Transition/effect |
|---|---|---|
| None | No evidence | Submission permitted without evidence |
| Text | Text only | Current text replaced on resubmission, history audited by submission events |
| Link | Link only | Current link replaced on resubmission |
| Attachment | Attachment only | Accepted blob/metadata immutable; later attachment appended |
| Multiple | Supported Text + Link + Attachment combination | Type/count/size rules all apply |
| Custom | None | Explicitly unsupported/deferred |
| Storage disabled/scanner unavailable | Text/Link unaffected; attachment rejected | Fail closed; never bypass scanner or persist body |

Attachment content is visible only to claimant and Managers. A beneficiary who is not claimant and unrelated participants receive concealed not-found behavior. Valid downloads are private, authorized, no-store/nosniff, safe Content-Disposition.

## XP ledger (9 states)

- TaskApproval Grant: one per submission/beneficiary; correctable; original immutable.
- ManualAward Grant: explicit cycle/category/reason/requestId; not correctable in BA-013.
- Raid Grant: explicit raid provenance; not correctable in BA-013.
- Reversal: signed append-only direct adjustment to original TaskApproval Grant.
- Correction: signed append-only direct adjustment to original TaskApproval Grant.
- Effective XP: original plus all direct signed adjustments; may be zero, never represented by mutating original.
- Zero-XP participant: roster/reporting row exists without fabricated ledger entry.
- Multiple entries: source totals include signed movements; Adjustments is disclosure, not an extra addend.
- Historical attribution: `CycleId` is authoritative regardless of `AwardedAt`.

## Reporting (8 view states)

- Participant Dashboard: selected enrolled cycle total/rank/status/recent activity/raid pass balance.
- XP Activity: ordered/paged raw Grant/Reversal/Correction with friendly Task Approval/Manual Award/Raid source.
- Individual Leaderboard: Active roster only, zero included, competition ranking.
- My Cycle Team: current open cycle-team assignment.
- Challenge Groups: immutable/factual participation snapshots, separate from Cycle Team.
- Manager Scoresheet: all enrollment statuses, zero rows, signed source/entry totals.
- Participant drill-down: paged append-only ledger and correction metadata.
- Raid Pass: Physical/Remote Assigned/Used shown as non-XP.

All reporting views have loading, legitimate empty, error/retry, selected-cycle, and stale-request-discard states. Writes become visible only after authoritative reload/refetch; no real-time push is required.

## Raid state and projections

| Model | Current write source | Current product use |
|---|---|---|
| `RaidSession` | Historical import/deterministic seed; no Raid Admin endpoint | Labels Raid XP provenance in reporting |
| `RaidEntitlement` | Historical import/deterministic seed; no participant/manager write UI | Physical/Remote assigned balance read model |
| `RaidParticipation` | Historical import/deterministic seed; no Raid Admin endpoint | Physical/Remote usage count/read model |
| `XPEntry` with `SourceType=Raid` | Historical import/seeded lower-level data; no supported Raid award command | Signed Raid XP source projection in Scoresheet, Dashboard, Activity and leaderboard totals |
| Participant Raid Pass display | Read-only participant Dashboard | Assigned/Used/balance shown explicitly as non-XP |

Raid passes never create XP and must not be inferred as XP from Assigned/Used counts. Raid browser creation is excluded because Raid Administration is unimplemented. Unified browser ledger reconciliation excludes Raid unless a supported deterministic write/setup contract becomes available; lower-level reporting/import tests own Raid projection coverage.

## Cross-feature transition effects

- Cycle status never rewrites challenge lifecycle or dates.
- Enrollment status changes gate future submissions/beneficiary selection/Manual Awards but never erase historical submissions or XP.
- Approval updates Scoresheet, Dashboard, XP Activity, and Leaderboard atomically through XPEntry.
- Correction and Manual Award propagate through the same authoritative ledger reporting.
- Raid entitlement/usage never changes XP.
- Restart preserves administered state; development seeding only creates missing deterministic records and must not reset user-created/administered data.
