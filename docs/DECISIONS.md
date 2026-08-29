# Decisions Log

This file records anything resolved **after** `PORTAL_SPEC.md` was frozen. The spec is the baseline; this file is the append-only record of what's changed or been settled since. An implementing agent (Codex or otherwise) should check here before making any judgment call the spec marks as open — a decision made once and logged here should never need re-deriving.

**Format for each entry:** date, what was decided, who decided it, and which section of the spec it resolves or amends.

---

## Open

### `BUSINESS_RULE_PENDING` — Team Leaderboard scoring (spec §10)

Not yet answered. Three questions are owed to Preety before the team-leaderboard calculation can be implemented:

1. **Aggregation formula** — sum of member XP, one flat completion score, or a separate team-score model?
2. **Bonus treatment** — do manual awards (Early Bird, Friday Funny, Raid, etc.) count toward team score, or only task/challenge XP?
3. **Cross-team challenge assignment** — when a challenge pairing crosses `CycleTeam` lines, which team receives the points?

**Do not implement a default here.** The schema (§4, §6, §10) is built generically enough to support any answer — `XPEntry.cycleTeamId` / `challengeParticipationId` already carry the attribution snapshot needed. The calculation itself stays disabled/pending until this section is updated with Preety's answer.

---


### `POLICY_PENDING` — Evidence retention (spec §13)

The storage architecture is settled (private Azure Blob Storage with authorized short-lived access), but the retention/deletion policy must be confirmed before production rollout.

Decision required:

1. **Approved evidence retention** — how long should approved submission evidence be retained?
2. **Rejected evidence retention** — how long should rejected submission evidence be retained?
3. **Automatic purge** — should rejected evidence be deleted automatically after a defined period?
4. **Records-management requirements** — do CPA Australia information-retention, privacy, legal, or records-management policies prescribe the retention period or deletion process?

**Do not invent a default retention period.** Until this is resolved, implementation should make retention configurable and must not enable destructive automatic deletion based on an assumed period.

---

## Resolved

### 2026-08-28 — BA-015 CycleParticipant audit amendment (supersedes part of the initial Cycle Administration decision)

Required by: Senior Architect review

This amendment supersedes the earlier BA-015 statement that Cycle Administration would not add participant-membership history. Enrollment and manager-controlled status changes directly affect submission and Manual Award eligibility and therefore require narrowly scoped append-only audit history.

#### `CycleParticipantEvent`

`CycleParticipantEvent` records CycleParticipant enrollment and manager-controlled status administration only. It is not a general audit framework.

- **Enrolled:** `FromStatus = null`, `ToStatus = Active`; record the manager actor, mandatory reason and server timestamp.
- **StatusChanged:** `FromStatus = previous status`, `ToStatus = new status`; record the manager actor, mandatory reason and server timestamp.
- Every reason is trimmed, non-empty and at most 1,000 characters.
- Events are append-only and preserve the full enrollment/status history.

#### Current-row timestamp semantics

- New enrollment: `JoinedAt = server timestamp`, `LeftAt = null`.
- Active to Withdrawn or Inactive: preserve `JoinedAt`; set `LeftAt = server timestamp`.
- Withdrawn or Inactive to Active: preserve `JoinedAt`; set `LeftAt = null`.
- Withdrawn to Inactive or Inactive to Withdrawn: preserve `JoinedAt`; set `LeftAt = latest transition server timestamp`.
- `LeftAt` represents current-row operational state only; `CycleParticipantEvent` is the authoritative full history.

All other BA-015 rules remain unchanged: enrollment and status changes occur only while the cycle is Active; enrollment begins Active; reactivation is supported; CycleParticipant rows are never deleted; and Closing or Finalised cycles freeze enrollment and status.

---

### 2026-08-28 — Initial Cycle Administration workflow (resolves BA-015; clarifies spec §§2, 3 and 17)

Decided by: User

#### Creation, lifecycle and active-cycle rules

- Do not introduce Draft. A new cycle is created directly with `CycleStatus = Active`, including a future-dated cycle.
- Status is controlled by manager action; dates never automatically transition lifecycle state.
- The only supported lifecycle is one-way: `Active → Closing → Finalised`.
- No reopen or backward transition is exposed. Existing technical `CycleEventType.Reopened` support is not an approved initial operational action.
- Multiple Active cycles are permitted. Do not introduce global Active-status uniqueness; deterministic selector defaults remain presentation behavior only.

#### Fields and dates

- Creation requires code, name, `StartsAt` and `EndsAt`, with `StartsAt < EndsAt`.
- While Active, code, name and dates may be edited subject to validation.
- Closing and Finalised cycle metadata is read-only.
- Transitioning status never changes dates automatically.

#### Enrollment and participant status

- Cycle Administration enrolls existing durable `Participant` records only. It does not create identities, accounts or Entra users.
- New `CycleParticipant` enrollment is permitted only while the cycle is Active and begins with status Active.
- Managers may explicitly move an existing enrollment between Active, Withdrawn and Inactive while the cycle is Active, including reactivation from Withdrawn or Inactive.
- Once the cycle is Closing or Finalised, enrollment membership and participant status are read-only.
- Never physically delete a `CycleParticipant`; deletion must not depend on whether related activity exists.
- BA-005 and BA-014 remain unchanged: Active participants may submit or be beneficiaries and receive new ManualAward Grants; Withdrawn and Inactive participants remain historically visible but cannot perform those new actions.

#### Finalisation and challenge independence

- Finalised closes cycle configuration and membership, while reporting, Scoresheet and historical participant visibility remain available.
- New ManualAward Grants remain prohibited in Finalised cycles.
- Approved post-approval TaskApproval correction remains allowed as an explicit exception and does not reopen the cycle.
- Cycle transitions do not publish, close or otherwise modify challenges and do not determine challenge eligibility.
- Challenges need not be Closed before cycle Finalisation.

#### Confirmation, audit and deletion

- Require explicit confirmation for `Active → Closing`, `Closing → Finalised`, and every `CycleParticipant` status change.
- Lifecycle transitions require a mandatory manager reason and use the existing append-only Cycle lifecycle event mechanism.
- Cycle creation uses existing Cycle creation audit behavior where supported.
- **Superseded by the Senior Architect-required BA-015 audit amendment above:** enrollment and participant-status changes require the narrowly scoped append-only `CycleParticipantEvent` history.
- Hard deletion of Cycle and CycleParticipant is unsupported.

#### Explicit deferrals

- Draft status; cycle reopening; clock-driven lifecycle transitions; hard delete/restore; participant or account creation; Entra provisioning; bulk CSV enrollment; recurring cycle creation; cloning; team scoring, team XP and Team Leaderboard; score disputes; Raid Administration; Teams integration; and Azure administration.

---

### 2026-08-27 — Manual XP Award workflow (resolves BA-014; clarifies spec §§3 and 4)

Decided by: User

#### Recipient and cycle eligibility

- A new Manual XP Award may be created for exactly one `CycleParticipant` whose status is Active in the explicitly selected reporting cycle.
- Withdrawn and Inactive participants remain visible historically but cannot receive a new `ManualAward` Grant. No historical-manager override is included.
- The selected cycle must be Active or Closing. Finalised cycles do not accept new ManualAward Grants.
- Finalised cycles remain manager-correctable through the approved append-only correction workflow where that workflow applies.
- `CycleId` is explicit and is never inferred from `AwardedAt`.

#### Award category, amount and reason

- A configured category is required; free-text category entry is unavailable.
- A category is selectable only when `IsActive = true` and its `CycleId` is null or equals the selected cycle.
- Active global and active selected-cycle categories are valid. Inactive and other-cycle categories are invalid for new awards.
- Inactive or historical categories remain usable as labels for existing ledger entries.
- Amount must be a positive integer. Zero and negative ManualAward Grants are invalid.
- Reason is mandatory and limited to 2,000 characters.

#### Ledger, authorization and idempotency

- Creation writes one append-only `XPEntry` with `entryType = Grant` and `sourceType = ManualAward`; it never edits an existing entry.
- Only an authorized `Quest.Manager` may create the award.
- Each create command carries a client-generated unique `requestId`.
- First use of a `requestId` creates the award. An exact replay with identical command fields returns the already-created result without another XP entry. Reuse with different command data returns a conflict.
- After success or deliberate form reset, the client generates a new `requestId`.
- Otherwise-identical awards are valid distinct business actions when they use different request IDs; matching participant, category, amount and reason does not itself constitute a duplicate.

#### Reporting and exclusions

- The ledger entry updates participant reporting, Individual Leaderboard and Manager Scoresheet through their existing `XPEntry` calculations.
- ManualAward correction remains deferred under BA-013.
- Team awards, bulk awards, Raid XP creation, CSV import, approval workflow and score-dispute behavior are excluded.

---

### 2026-08-27 — Initial post-approval correction target scope (resolves BA-013; clarifies spec §4)

Decided by: User

- Only an original `XPEntry` with `entryType = Grant` and `sourceType = TaskApproval` is directly correctable in the initial workflow.
- TaskApproval Grant rows may expose a Correct XP action.
- ManualAward and Raid grants are not correctable in this chunk. Their correction semantics are deferred until separately approved.
- Reversal and Correction rows are never direct correction targets.
- Repeat corrections target the same original TaskApproval Grant.
- Corrections remain beneficiary-specific because every beneficiary has a distinct original Grant entry.
- The manager enters the intended new effective amount, not a delta.
- Current effective amount is the original grant plus all signed direct adjustments referencing that grant. A repeated correction calculates from this current effective amount.
- The intended effective amount must be a non-negative integer; zero is allowed.
- A reason is mandatory and storage permits at most 2,000 characters.
- Original and adjustment entries remain append-only and visible; no ledger entry is mutated or deleted.
- Participant and manager reporting reflects the resulting signed ledger movements.

---

### 2026-08-27 — Manager Scoresheet reporting rules (resolves BA-012; amends spec §§3, 4, 5, 10 and 17)

Decided by: User

#### Roster and purpose

- For the selected reporting cycle, include every `CycleParticipant`: Active, Withdrawn and Inactive.
- Keep zero-XP participants visible and expose `participantStatus`.
- The Scoresheet is a historical/audit reporting surface; later status changes must not make historical XP or roster reporting disappear.
- The Individual Leaderboard remains Active-only and is not changed by this decision.

#### Summary and totals

- Show Task Approval XP, Manual Award XP, Raid XP, Adjustments and Total XP.
- Every value derives exclusively from append-only `XPEntry` rows for the selected `CycleId`; signed `XPEntry.Amount` is authoritative and `AwardedAt` never determines cycle attribution.
- Each source total includes the effective signed ledger movements belonging to its `TaskApproval`, `ManualAward` or `Raid` source type.
- `netAdjustmentXp = reversalXp + correctionXp`, preserving each ledger row's sign. A Correction must not be assumed positive unless the domain model enforces that independently.
- Adjustments are a cross-cutting disclosure of movements already included in the source totals, not an additional component to add to those totals again.
- Corrections and Reversals remain visible and never replace or rewrite earlier ledger rows.
- Raid passes are separate non-XP resources and do not appear in the Scoresheet.

#### Participant drill-down

- Include participant drill-down in the initial chunk.
- Show the itemized append-only ledger with signed amount; Grant, Reversal or Correction entry type; TaskApproval, ManualAward or Raid source type; applicable Challenge and Task, Manual Award category or Raid session; reason; timestamp; and reversal reference where available.
- Do not collapse correction/reversal chains.
- Do not recompute the participant summary total from only the currently loaded detail page.

#### Rank, filters, export and teams

- Do not show rank in Manager Scoresheet; competition ranking remains owned by the Individual Leaderboard.
- Reporting-cycle selection is required. Participant search and simple source-type filtering within drill-down are useful but not mandatory business gates.
- Advanced filters and CSV/export are deferred from this chunk.
- Do not calculate team XP or a team leaderboard. BA-003 remains unresolved and out of scope.

---

### 2026-08-25 — Manager challenge administration lifecycle and validation (resolves BA-011; amends spec §§2, 6, 8, 9, 16 and 20)

Decided by: User

#### Lifecycle

- Persist only `Draft`, `Published`, `Closed` and `Archived` challenge statuses.
- Open is not a persisted manager-controlled status. The general challenge Open state is derived when `Challenge.Status == Published`, current time is at or after `openAt`, and current time is at or before `closeAt`.
- Participant submission/resubmission eligibility additionally applies the existing deadline/override rules. An explicit participant override may extend that participant's effective close boundary beyond the general `closeAt` under BA-006; it does not change the persisted challenge status.
- The UI may show derived labels such as Scheduled or Open without persisting them as challenge statuses.
- The only lifecycle is `Draft → Published → Closed → Archived`.
- Publishing is irreversible; Published cannot return to Draft.
- Only Closed challenges may be Archived. Archive is irreversible in this chunk and restore is unsupported.

#### Editing

- All challenge, task and configuration fields are editable while Draft.
- After publication, `openAt`, task identity/list/order, task XP, evidence requirement, scoring mode, participation/team policy, `allowSolo`, `minMembers` and `maxMembers` are immutable.
- After publication, challenge name/title, description and supported hero image remain editable.
- After publication, `dueAt` and `closeAt` may only be extended; they cannot be shortened.
- Closed and Archived challenges are read-only in this chunk.
- Existing participant deadline overrides remain authoritative and unchanged.

#### Dates

- `openAt`, `dueAt` and `closeAt` are required.
- Require `openAt < dueAt` and `dueAt <= closeAt`; `dueAt == closeAt` is valid.
- Managers may continue reviewing after `closeAt` under the existing decision.

#### Tasks, XP and evidence

- Publication requires at least one task.
- Every task has a durable ID, name, explicit ordering, non-negative integer XP, scoring mode and evidence requirement.
- Task names need not be unique; durable IDs are authoritative.
- Zero-XP tasks are allowed. No business maximum task count is introduced in this chunk.
- Each task selects exactly one supported evidence requirement: `None`, `Text`, `Link`, `Attachment` or `Multiple`. `Custom` remains deferred and unavailable.

#### Participation

- Each task explicitly selects `Individual`, `WholeTeam`, `ClaimantSelectsBeneficiaries` or `AttendanceBased`.
- `ChallengeTeamPolicy` is required only when at least one task is non-Individual.
- When a policy applies, require `1 <= minMembers <= maxMembers`.
- If `allowSolo` is true, `minMembers` must equal 1. If false, `minMembers` must be at least 2.
- Individual-only challenges do not require `ChallengeTeamPolicy`.
- Never infer `CycleTeam` from challenge participation.

#### Publish validation and deletion

- Publish requires a non-empty challenge name/title, valid required dates, at least one valid task, explicit task ordering, non-negative integer XP, supported evidence and scoring modes, and a valid participation policy when required.
- Description, category and hero image are optional.
- Draft deletion and all hard deletion are deferred. This chunk supports only `Closed → Archived`, with no restore.

---

### 2026-08-24 — Participant reporting surfaces and cycle-team presentation (resolves BA-004; amends spec §§3, 6, 10, 16 and 17)

Decided by: User

#### Reporting-cycle context

- Participant Dashboard, My Team, Individual Leaderboard and XP Activity use an explicit reporting-cycle selector.
- The selector defaults to the participant's most recently started enrolled Active cycle; otherwise an enrolled Closing cycle; otherwise the participant's most recently started enrolled cycle.
- The selector is presentation/reporting context only. It never affects challenge eligibility, challenge lifecycle or `XPEntry.cycleId` attribution.

#### Initial participant dashboard

- Show total XP for the selected reporting cycle.
- Show the participant's individual rank.
- Show an actionable submission-status summary and recent XP activity.
- Show raid-pass balance as a clearly separate non-XP resource.
- Do not add trends, charts or deep analytics in this chunk.

#### My Team and BA-004

- `CycleTeam` is the participant's cycle-level team identity.
- **My Cycle Team** shows the current open `CycleTeamMember` assignment for the selected cycle.
- **Challenge Groups** separately shows factual `ChallengeParticipation` snapshots involving the participant.
- Do not infer `CycleTeam` from `ChallengeParticipation`, merge the concepts, calculate team XP or calculate a team leaderboard.

#### Individual leaderboard

- Include Active `CycleParticipant`s only for the selected reporting cycle; Active participants with zero XP remain visible.
- Exclude Withdrawn and Inactive `CycleParticipant`s from the current leaderboard without changing historical XP.
- Use competition ranking: totals `100, 90, 90, 70` produce ranks `1, 2, 2, 4`.
- Within an equal XP total, order deterministically by normalized `displayName` ascending, then `participantId` ascending. This ordering does not break the tie.

This decision resolves BA-004 for participant presentation. Team XP and team-leaderboard scoring remain `BUSINESS_RULE_PENDING` under BA-003.

---

### 2026-08-24 — All-or-nothing multi-beneficiary approval (resolves BA-001; amends spec §§4 and 7)

Decided by: User

Decision:

- A multi-beneficiary submission has one overall review outcome and all beneficiaries are reviewed together.
- If evidence is insufficient for any beneficiary, the entire submission moves to `NeedsEvidence` and no beneficiary receives XP.
- Resubmission updates the shared submission.
- Approval creates one TaskApproval XP grant per beneficiary together in one atomic transaction; partial beneficiary approval is not supported.
- Corrections and reversals after approval remain append-only and may target the affected beneficiary's XP ledger entry where required.

Rationale: Historical evidence shows that partial beneficiary awarding occurred operationally at least once. The portal deliberately treats that occurrence as an operational exception or workaround and does not reproduce it. A single group-submission outcome is an intentional product simplification that provides clearer UX, auditability, idempotency and review behaviour.

Implementation note: `Submission` retains one status; `SubmissionBeneficiary` does not require beneficiary-level review status. The existing one-grant-per-submission-and-beneficiary ledger structure remains appropriate, with all grants created atomically on approval.

---

### 2026-08-24 — Active cycle-participant eligibility (resolves BA-005; amends spec §§3 and 7)

Decided by: User

- Only Active `CycleParticipant`s may create new submissions or be selected as beneficiaries.
- Withdrawn and Inactive participants cannot participate in new submissions.
- A later participant-status change does not retroactively alter historical submissions or XP already awarded.

---

### 2026-08-24 — Submission date boundaries and overrides (resolves BA-006; amends spec §§2 and 9)

Decided by: User

- `openAt` controls when participant submission activity may begin.
- `dueAt` is the normal participant submission/resubmission deadline.
- After `dueAt`, participant submission/resubmission requires an applicable participant deadline override extending the effective deadline.
- `closeAt` is the hard participant submission/resubmission boundary.
- Going beyond normal `closeAt` requires an explicit participant override that also extends the effective close boundary.
- Managers may continue reviewing, approving, rejecting and performing authorized corrections after `closeAt`.
- Challenge lifecycle remains independent of reporting-cycle lifecycle; current month/current cycle never determines eligibility.
- Global challenge extensions are audited challenge-level date changes. Individual extensions remain participant-specific deadline history.

---

### 2026-08-24 — Per-challenge solo participation (resolves BA-002; amends spec §6)

Decided by: User

- `ChallengeTeamPolicy.allowSolo = true` permits a one-person `ChallengeParticipation`.
- `allowSolo = false` requires the configured minimum membership.
- Challenge configuration must state this explicitly; there is no universal PAS AI Quest solo rule.

---

### 2026-08-24 — Shared manager review queue (resolves BA-009; amends spec §§7 and 12)

Decided by: User

- Any authorized `Quest.Manager` may review an eligible submission through a shared queue.
- `SubmissionEvent` records the manager who actually performs each action.
- The current product does not assign submissions to designated managers. This may be reconsidered if later operational evidence demonstrates a need.

---

### 2026-08-24 — Post-approval XP correction semantics (amends spec §§2 and 4)

Decided by: User

- Managers may correct beneficiary-specific awarded XP after approval, with an explicit reason.
- Existing XP ledger rows are never edited or deleted; reversals and corrections are append-only and retain traceability to the affected original grant.
- Effective awarded XP may be adjusted upward, downward or to zero.
- Finalised reporting cycles remain manager-correctable with audit.
- Participants cannot perform corrections.

Implementation note: Use the approved `XPEntry` Reversal/Correction architecture. This decision defines functional behaviour and does not prescribe additional implementation mechanics.

---

### 2026-08-24 — Step 7 evidence and attachment rules (amends spec §§8 and 13)

Decided by: User

#### Evidence requirement semantics

- `None`: no evidence required.
- `Text`: Text evidence only.
- `Link`: Link evidence only.
- `Attachment`: Attachment evidence only.
- `Multiple`: may combine supported Text, Link and Attachment evidence.
- `Custom` is excluded from the initial Step 7 showcase until machine-readable validation semantics are explicitly defined.
- A specific evidence requirement does not permit arbitrary evidence types.

#### Evidence visibility

- Evidence content is accessible only to the submission claimant and authorized `Quest.Manager` users in Step 7.
- Submission beneficiaries who are not the claimant do not receive evidence-content access.
- Unknown and unauthorized evidence IDs must not disclose whether the evidence exists.

#### Resubmission attachments

- Existing accepted attachments are immutable and retained.
- Resubmission may append new attachments to the same logical submission.
- Existing attachments are not automatically removed or physically replaced.
- Text/link evidence retains the Step 6 replacement semantics.
- A failed resubmission leaves existing evidence unchanged.
- Attachment deletion and supersession remain deferred until retention/audit policy is decided.

#### Initial configurable validation policy

- Maximum 5 attachments per request.
- Maximum 25 MiB per file.
- Maximum 50 MiB combined per request.
- Initial MIME allowlist: `image/jpeg`, `image/png`, `image/webp`, `application/pdf`, `text/plain`, `application/vnd.openxmlformats-officedocument.wordprocessingml.document`, `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`, `application/vnd.openxmlformats-officedocument.presentationml.presentation`, `video/mp4`.
- These limits and MIME types are configurable technical defaults, not retention policy.

#### Malware scanning

- Step 7 introduces `IEvidenceMalwareScanner`.
- Development/Test may use an explicitly identified deterministic pass-through scanner.
- Step 7 does not require quarantine or pending-state persistence.
- Detected malware rejects the request; scanner failure fails closed.
- Production attachment capability must not start or be enabled without a real configured scanner.
- Real production scanner integration may be completed during production-readiness work.

#### Explicit deferrals

- Evidence retention and deletion.
- `Custom` evidence semantics.
- Real Entra.
- Teams integration.
- Team leaderboard.
- Score disputes.

---

*(When §10 is answered, add an entry below in this format:)*

```
### YYYY-MM-DD — Team Leaderboard scoring formula (resolves spec §10)

Decided by: Preety
Answer:
  1. Aggregation formula: [answer]
  2. Bonus treatment: [answer]
  3. Cross-team assignment: [answer]

Implementation note: [anything an implementing agent needs to know that
isn't obvious from the answer alone]
```
