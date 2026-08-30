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

### 2026-08-30 — Teams Notification MVP (resolves BA-017; supersedes the Teams deferral only for this scope)

Decided by: Product Owner notification policy, converted into precise business rules after review of the current portal workflows.

Teams remains an awareness, call-to-action and deep-link surface. The portal/API remains authoritative for challenge state, submissions, evidence, reviews, XP, corrections and leaderboard calculation. This decision supersedes the earlier Teams deferral only for the seven outbound MVP notifications below; inbound Teams capture, Teams actions and all broader integration remain deferred. It does not mark implementation started or complete.

#### Verified business events and triggers

| Event | Authoritative trigger | Current workflow verification | Commit rule |
|---|---|---|---|
| Challenge Published | The first successful `Draft` to published/available transition through the manager publish command | `POST /api/manager/challenges/{id}/publish` and `ChallengeAdministrationService.PublishAsync`; the current implementation names the persisted available state `Open` although the frozen lifecycle calls it `Published` | Notification becomes eligible only from the committed transition; Draft saves, edits, reads, seed/import and a replay that performs no new transition do not trigger it |
| Participant Submitted | Successful initial submission creation | `POST /api/submissions`; `CreateAsync` writes `Submission`, beneficiaries, evidence and `Submitted` event in one serializable transaction | After the entire transaction commits |
| Participant Resubmitted | Successful `NeedsEvidence` to `Resubmitted` transition | `PUT /api/submissions/{id}/resubmission`; `ResubmitAsync` replaces text/link evidence, appends attachments and writes `Resubmitted` in one serializable transaction | After the entire transaction commits; it is a new event, not another initial submission |
| Needs Evidence Requested | Successful review transition to `NeedsEvidence` | `POST /api/submissions/{id}/review`; `ReviewAsync` supports `ReviewAction.NeedsEvidence` and requires a manager comment | After the review transaction commits |
| Submission Approved + XP | Successful review transition to `Approved` together with all beneficiary TaskApproval grants | The same review endpoint/service creates beneficiary `XPEntry` Grants and the `Approved` event in one serializable transaction | After approval and every XP grant commit; rollback produces no notification |
| Submission Rejected | Successful review transition to terminal `Rejected` | The same review endpoint/service supports `ReviewAction.Reject`, requires a manager comment and writes a `Rejected` event; it is distinct from `NeedsEvidence` | After the review transaction commits |
| Leaderboard Announcement | A new explicit manager command selecting a reporting cycle and choosing **Post leaderboard to Teams** | The authoritative Individual Leaderboard query and competition-ranking calculation exist; no announcement command exists yet | The command creates one intentional announcement event from a server-read snapshot; XP changes never trigger it automatically |

All notification eligibility has outbox-after-commit business semantics: a notification may be recorded atomically with its source transaction and delivered later, but Teams must never communicate an event that failed to persist. The architecture/mechanism is intentionally not decided here.

#### Logical audiences and identity

- `QUEST_GENERAL_AUDIENCE` is the configured Quest communication destination. It is not dynamically reconstructed from Active CycleParticipants, and membership of that Teams destination does not grant portal access. Challenge Published and Leaderboard Announcement use this audience.
- `QUEST_MANAGER_AUDIENCE` is the configured operational manager destination. It is not an assumption that every portal `Quest.Manager` role holder shares one Teams conversation. Submitted and Resubmitted use this audience.
- `PARTICIPANT_PRIVATE` is a private destination resolved from the durable Participant identity. Needs Evidence and Rejected target the claimant only.
- `BENEFICIARY_PRIVATE` is one private destination per distinct approved beneficiary. Approval + XP targets every beneficiary, including the claimant when the claimant is a beneficiary, with one notification per distinct Participant ID.
- Display name and email are not durable routing keys. The intended real Entra/Teams correlation key is `(tenantId, oid)`, mapped to the durable Participant. The current demo authenticates synthetic subjects and the current Participant record stores only optional `EntraObjectId`; it cannot supply genuine private Teams recipient identities. Real private delivery therefore depends on approved Entra identity mapping and configured proactive-delivery destinations. No identities may be inferred.

#### Message content and privacy

- **Challenge Published:** challenge name, safely truncated short description, `openAt`, `dueAt`, `closeAt`, compact ordered task names and public task XP, and Challenge Detail link. Do not include full content, evidence details, beneficiaries, challenge-group membership or team-policy details.
- **Submitted:** claimant display name, challenge, task, beneficiary count, submitted timestamp and manager submission-review link. **Resubmitted:** the same minimum context, explicit Resubmitted wording and resubmission timestamp. Do not include beneficiary names, evidence content, attachment names, private blob URLs or manager-private metadata.
- **Needs Evidence:** challenge, task, Needs Evidence status, effective participant deadline and participant submission-detail/resubmit link. The deadline must come from the same authoritative portal/API calculation that applies challenge dates and participant overrides; Teams must not calculate it independently. The manager comment may be copied into the private message because the current field is participant-visible in submission detail/history. If no participant-visible feedback is available, use only a generic statement that more evidence is required.
- **Approved + XP:** one combined message, never separate Approved and XP Awarded messages. Each distinct beneficiary sees challenge/task, Approved status, only that recipient's awarded XP, approval time and XP Activity/My Activity link. Never list other beneficiaries or their amounts. A claimant who is also a beneficiary receives one logical notification.
- **Rejected:** private claimant only; terminal Rejected status, participant-visible reason, no-XP wording and submission-history link. Beneficiaries other than the claimant are not notified in this MVP.
- **Leaderboard Announcement:** selected cycle, server generated-at timestamp, the first three participants in the authoritative deterministic leaderboard ordering with their existing competition ranks and total XP, and selected-cycle Individual Leaderboard link. The frontend does not supply standings. Fewer than three eligible participants produces the available rows. XP changes never post automatically.
- Teams messages contain no evidence body, attachment metadata/URL, private storage link or other sensitive submission material. Every deep link is still subject to normal portal authorization.

For the synthetic Monday demo, synthetic Top 3 names and XP may be posted. For real users, public-name/XP leaderboard posting remains configuration-disabled until an explicit privacy/visibility approval is recorded. This is separate from the existing in-portal leaderboard rule.

#### Duplicate, failure and freshness semantics

- One logical domain event produces at most one intended notification per logical destination or distinct recipient. Architect review must provide stable conceptual `EventId` and destination/recipient-specific `NotificationId` correlation; storage design is not frozen here.
- Command/API retries do not create another notification when no new state transition/event occurred. An exact approval retry cannot notify twice. A committed resubmission is a new event and may notify again. Each explicit manager leaderboard-post command is a new intentional event and may create another announcement even when its values equal an earlier snapshot.
- Teams delivery failure never rolls back or changes the portal operation. Fail asynchronously, record delivery state, retry safely, and retain enough operational status for manager/support visibility when needed. Portal success does not depend on Teams availability.
- Freshness is rechecked immediately before sending actionable notifications. Challenge Published is suppressed if the challenge has since become Closed or Archived. Submitted is suppressed if the submission is no longer `Submitted`; Resubmitted is suppressed if it is no longer `Resubmitted`; Needs Evidence is suppressed unless it is still `NeedsEvidence`. Approved and Rejected are durable terminal facts and are sent even if delayed. Leaderboard posts use their command-time snapshot and generated-at timestamp and are sent even if delayed.

#### Deep-link intents and required configuration

Routes are logical and use a configured portal base URL; business rules do not hardcode environment hostnames.

| Event | Logical destination |
|---|---|
| Challenge Published | Challenge Detail |
| Submitted / Resubmitted | Manager Review Queue focused on submission detail |
| Needs Evidence | Participant My Activity submission detail/resubmit |
| Approved + XP | Participant My Activity or XP Activity in the attributed cycle |
| Rejected | Participant submission history |
| Leaderboard Announcement | Selected-cycle Individual Leaderboard |

Required logical configuration is: General Quest destination; Manager destination; app/deployment identity; portal base URL; private proactive participant delivery enabled/disabled; durable participant `(tenantId, oid)` mapping; and real-user leaderboard public-name/XP visibility enabled/disabled. The Teams SDK, Graph, bot/agent, manifest, webhook, Adaptive Card, storage and delivery architecture remain for Architect review.

#### Monday scope

Core MUST HAVE events are the seven verified events above. ManualAward XP, Raid XP and TaskApproval correction notifications are `OPTIONAL_LATER`; if promoted, they are private to the affected recipient only and must follow the same committed-event, privacy, duplicate and delivery-failure rules. They have no Monday acceptance criteria under BA-017.

Still deferred: inbound Teams capture or actions; evidence exchange in Teams; ManualAward/Raid/correction notifications; public real-user leaderboard posting pending privacy approval; dynamic channel membership management; Teams approvals/scoring; and all technology/architecture choices.

---

### 2026-08-29 — MVP Raid Administration (resolves BA-016; supersedes the earlier Raid Administration deferral for this scope)

Decided by: User request for a Monday-ready BA design, using current schema/import/reporting evidence and established product patterns.

The BA-015 deferral of Raid Administration is superseded only for this MVP. Raid correction, deletion, richer audit, analytics and integrations remain deferred.

#### Existing model meaning

- `RaidSession` identifies one cycle-attributed raid occurrence: durable `Id`, required `CycleId`, `Name`, and `OccurredAt`. It has no lifecycle/status field.
- `RaidEntitlement` is the participant's cycle-level assigned pass count for one `Physical` or `Remote` type. Its key is `(ParticipantId, CycleId, PassType)` and `AssignedCount >= 0`.
- `RaidParticipation` is one recorded use of a pass for a participant at a RaidSession. It records `PassType` and `UsedAt`; it is not an XP value.
- Raid XP is an append-only `XPEntry` Grant with `SourceType = Raid`, the originating `RaidSessionId`, and the same `CycleId`. It is independent from pass assignment/use.
- Seed data currently creates a synthetic session, Physical entitlement and participation. Historical import creates sessions, entitlements, evidenced participation/pass-use rows, and separate Raid XP ledger grants. Current participant Dashboard reads pass balances; XP Activity, Scoresheet and Leaderboard read Raid XP through the ledger. No normal manager Raid write workflow exists yet.

#### RaidSession — `NEW_DECISION`

- Manager list/detail includes historical sessions from all cycle statuses.
- Create and edit are allowed only while the owning cycle is Active or Closing. Finalised is read-only.
- Create requires a selected cycle, non-empty trimmed name of at most 200 characters, and `OccurredAt`. The cycle is immutable after creation; durable IDs are authoritative and names need not be unique.
- `OccurredAt` is operational time and need not fall within cycle dates; `CycleId` is explicit reporting attribution.
- Name and `OccurredAt` may be edited only before the session has any RaidParticipation or Raid XP entry. After either exists, the session is read-only.
- There is no RaidSession Draft/Open/Closed state and no delete/restore.

#### Raid entitlement and pass use — `NEW_DECISION`

- Managers may assign/update Physical or Remote passes for an Active CycleParticipant while the cycle is Active or Closing. Historical rows remain readable after participant/cycle status changes.
- Assigned is the entitlement's current non-negative integer `AssignedCount`. Used is the count of RaidParticipation rows for the participant/cycle/pass type. Remaining is `Assigned - Used` and must never be negative.
- Assignment may increase or decrease, including to zero, but never below Used. Creating/updating an entitlement does not award XP.
- Recording participation requires an Active CycleParticipant, an Active or Closing owning cycle, a RaidSession in that same cycle, a selected Physical/Remote type, a matching entitlement, and at least one remaining pass.
- One RaidParticipation consumes exactly one pass and uses a server timestamp for `UsedAt`.
- A participant may have at most one RaidParticipation per RaidSession in total; `PassType` records which single pass was consumed. This strengthens the current narrower uniqueness on `(ParticipantId, RaidSessionId, PassType)` and requires an Architect-reviewed database invariant on `(ParticipantId, RaidSessionId)`.
- RaidParticipation is append-only for this MVP: no edit, delete, replacement or pass-use reversal workflow.

#### Raid XP — `NEW_DECISION`

- Only an authorized `Quest.Manager` may award Raid XP, one participant per command.
- The participant must be an Active CycleParticipant in the RaidSession's cycle, and that cycle must be Active or Closing. Finalised cycles reject new Raid XP.
- The selected RaidSession supplies and must belong to the explicit `CycleId`; `AwardedAt` is the server timestamp and never determines attribution.
- RaidParticipation and pass entitlement are not prerequisites for Raid XP. This preserves the established separation between Raid scoring and pass operations.
- Create exactly one append-only `XPEntry`: `EntryType = Grant`, `SourceType = Raid`, selected `RaidSessionId`, positive Int32 `Amount`, mandatory trimmed reason of at most 2,000 characters, and no AwardCategory, Submission or Task reference.
- Zero and negative grants are invalid. Legitimate repeated awards are allowed with new request IDs.
- Reuse Manual Award idempotency semantics: client-generated `requestId`; first use creates; exact replay of the same command returns the existing result; reuse with different command data conflicts. The request ID may serve as the XPEntry ID.
- Raid XP correction remains deferred and existing XP entries are never mutated or deleted.

#### Cycle interaction

| Manager action | Active | Closing | Finalised |
|---|---|---|---|
| View historical Raid data | Yes | Yes | Yes |
| Create/edit eligible RaidSession | Yes | Yes | No |
| Assign/update passes | Yes | Yes | No |
| Record participation/pass use | Yes | Yes | No |
| Award Raid XP | Yes | Yes | No |

All mutating commands recheck cycle and participant status atomically. Cycle transitions do not delete or rewrite Raid data and do not affect challenge lifecycle.

#### Participant and manager presentation

- No dedicated participant Raids screen is required for Monday. Dashboard shows Physical/Remote Assigned, Used and Remaining separately from XP; XP Activity already shows Raid source, signed amount, RaidSession name, reason and timestamp.
- Raid XP contributes through the existing Scoresheet Raid subtotal, participant total and Individual Leaderboard. Pass assignment/use contributes zero XP and never changes rank.
- Manager Raid Administration is one compact screen: cycle selector and session list; create/select session; participant table with pass balances and Record Participation; Award Raid XP action with confirmation.

#### Audit limitations and concurrency outcomes

- XP audit is complete through append-only XPEntry actor, reason and timestamp.
- Current RaidSession and RaidEntitlement entities have no audit history; RaidParticipation records the use timestamp but not the manager actor. The MVP does not add a generic Raid audit subsystem. These are explicit limitations for later review.
- Concurrent session or entitlement edits must not silently overwrite; stale commands return conflict. Rowversion is the recommended Architect-reviewed mechanism.
- Entitlement update and participation creation must serialize on the matching entitlement so `Used <= Assigned` always holds.
- Duplicate participation is rejected by a database uniqueness invariant on participant/session.
- Concurrent Raid XP commands with one request ID create at most one entry and follow exact-replay/conflict semantics, consistent with Manual Award.
- Raid writes racing Cycle Finalisation must lock/recheck status in the transaction: either the write commits before Finalisation or it fails after Finalisation; no post-Finalised write is accepted.

#### Explicitly out of scope

- Raid teams/team scoring, Raid leaderboard, approval workflow, participant self-registration, QR scanning, venue management, calendar integration, Teams notifications, bulk CSV operations, Raid XP correction, delete/restore, pass-use reversal, sophisticated analytics, and a dedicated participant Raid management screen.

---

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
