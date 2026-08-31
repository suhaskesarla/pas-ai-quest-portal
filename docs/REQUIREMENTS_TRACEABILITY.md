# PAS AI Quest Requirements Traceability

## Purpose

This file records how current PAS AI Quest requirements relate to historical operational evidence. It is the Business Analyst / Functional Analyst evidence register, not a product-decision log.

Source evidence does not automatically dictate future product behaviour. Intentional product improvements and generalisations are allowed where they are explicitly designed and approved. Conflicts or gaps in the evidence must not be resolved by implementation assumption; they require an explicit decision. Final approved product decisions belong in `docs/DECISIONS.md`.

## Evidence Sources

- 18-page historical Teams conversation capture.
- July historical score-sheet CSV.
- August historical score-sheet CSV.

The source files remain local-only under `local-source-evidence/`. They may contain private participant information and must not be committed or reproduced in tracked documentation.

## Confirmed Behaviours

| Behaviour | Evidence reference | Traceability conclusion |
|-----------|--------------------|-------------------------|
| Reporting cycle is separate from operational, submission and approval dates. | Teams pages 7-12; July `Go Pass 3`; July `July Total` | A challenge and its XP retain their reporting-cycle attribution even when operational activity continues later. Award timestamps must not determine cycle ownership. |
| Challenges can overlap reporting periods. | Teams pages 7-12 | July challenge activity continued while August challenges were announced and accepting work. |
| Deadlines can be extended more than once, including participant-specific and general extensions. | Teams pages 7, 8 and 11 | Challenge deadline history and participant-specific overrides are required. |
| Team size varies by challenge. | Teams pages 1, 8 and 12-17 | Team-size rules belong to the challenge, not solely to the cycle. |
| Team formation combines participant self-formation with manager intervention. | Teams pages 8, 10-12 and 16 | Formation mode must support self-formation and manager involvement. |
| Claimant and beneficiaries are separate facts. | Teams page 13 | One person can submit or claim for multiple people; the submitter must not be assumed to be the only beneficiary. |
| Evidence is heterogeneous. | Teams pages 8-17 | Valid evidence includes text, links, screenshots, documents, certificates, video and multiple evidence items. |
| Managers request clarification or additional evidence before final scoring. | Teams pages 8, 9 and 13-16 | The review workflow requires `NeedsEvidence`-type behaviour and an auditable resubmission path. |
| Task, manual-award and raid XP are meaningfully distinct. | July headers from `July Challenge 1` through `Raid 3`; August headers `GoPass4- T1` through `GoPass-5`; Teams page 7 | Scores must preserve their source and category rather than collapse into one mutable total. |
| Raid XP can vary by participant/session. | July `Raid 1`, `Raid 2`, `Raid 3`; Teams page 7 | Raid XP belongs in the XP ledger with its originating session/category context. |
| Physical and remote raid-pass allocation/usage are not XP. | August `Physical Raid Pass Assigned`, `Physical Raid Pass Used`, `Remote Raid Pass Assigned`, `Remote Raid Pass Used`; `August Total` | Assigned/Used values are operational resources and contribute zero to XP totals. |
| Cycle rosters include participants with zero XP. | July `PARTICIPANT` and `July Total`; August `PARTICIPANT` and `August Total` | Cycle views must start from cycle enrollment, not activity records. |
| Participants can identify missing scores and managers can correct them. | Teams pages 7 and 17 | A correction capability is required; the product uses an audited append-only implementation. |
| Teams contains workflow context absent from the score sheets. | Teams pages 7-17 compared with both CSVs | Partner finding, formation, evidence, review, extensions, raid logistics and score queries cannot be reconstructed from Excel totals alone. |

## Evidence / Spec Conflicts

### Partial beneficiary approval

**Evidence:** Historical Teams evidence shows XP awarded to some beneficiaries while another member was still awaiting evidence (Teams page 15).

**Current spec:** Multi-beneficiary approval is all-or-nothing.

**Decision:** The portal deliberately uses one overall review outcome. All beneficiaries are reviewed together; if evidence is insufficient for any beneficiary, the entire submission moves to `NeedsEvidence`. No beneficiary receives XP until approval creates all beneficiary grants atomically. Resubmission updates the shared submission. Post-approval corrections and reversals remain append-only and beneficiary-specific through the XP ledger where required.

**Status:** `DECIDED` — Option A, all-or-nothing approval.

**Historical treatment:** The observed partial beneficiary award is treated as an operational exception or workaround. The new portal intentionally does not reproduce it.

**Blocking:** Resolved. BA-001 no longer blocks submission review / beneficiary scoring.

### Historical Go Pass 3 attribution evidence

**Evidence:** Teams pages 7-11 demonstrate that the July Go Pass 3 challenge continued into August.

**However:** The supplied July CSV `Go Pass 3` column is blank for every populated participant row.

**Status:** `SOURCE EVIDENCE GAP`

**Rule:** Reporting-cycle attribution remains July because the challenge belongs to July, but historical XP rows must not be fabricated without authoritative score evidence. Unknown mappings must fail visibly.

**Blocking:** Blocks expansion or re-import of those historical Go Pass 3 records, not normal portal development.

### Solo participation

**Evidence:** A question about one-person participation appears on Teams page 12, but the supplied evidence contains no clear answer.

**Decision:** Solo participation is configured per challenge through `ChallengeTeamPolicy.allowSolo`. `allowSolo = true` permits a one-person `ChallengeParticipation`; `allowSolo = false` requires the configured minimum membership. Challenge configuration must make the choice explicit.

**Status:** `DECIDED`

**Rule:** No universal PAS AI Quest solo rule may be inferred.

## Missing / Future Product Decisions

| Area | Classification | Evidence and decision needed |
|------|----------------|------------------------------|
| Challenge-date discoverability | `PRODUCT DESIGN CHOICE` | Teams page 7 shows difficulty finding the latest challenge dates. Decide how authoritative current and revised dates are surfaced and how superseded announcements are identified. |
| Score-dispute workflow | `OPEN BUSINESS QUESTION` | Teams pages 7 and 17 show participants reporting missing scores. Decide whether disputes are tracked in the portal or remain an external Teams process. |
| Wall of Fame / offline artifacts | `OUT OF SCOPE CANDIDATE` | Teams page 14 shows group artifacts being used for an offline display. Confirm whether any portal gallery or artifact workflow is required. |
| Stable `CycleTeam` semantics | `PRODUCT DESIGN CHOICE` — `DECIDED` | `CycleTeam` is the participant's cycle-level team identity. My Team presents the current open cycle-team assignment separately from factual challenge-participation snapshots. This resolves BA-004 for participant presentation without deciding team scoring. |
| Manager assignment | `PRODUCT DESIGN CHOICE` — `DECIDED` | Use a shared manager review queue. Any authorized `Quest.Manager` may act; the acting manager is captured in `SubmissionEvent`. No designated submission-manager assignment is introduced now. |
| Due versus close behaviour | `PRODUCT DESIGN CHOICE` — `DECIDED` | `openAt`, `dueAt`, `closeAt` and participant overrides have the eligibility meanings recorded below and in `DECISIONS.md`. |
| Roster eligibility/status semantics | `PRODUCT DESIGN CHOICE` — `DECIDED` | Only Active `CycleParticipant`s may create new submissions or be selected as beneficiaries. Later status changes do not rewrite historical awards. |

## Intentional Product Decisions

The following approved design areas deliberately improve or generalise the historical Teams-and-score-sheet process:

- A typed, append-only XP ledger with grants, reversals and corrections instead of mutable score cells.
- Durable participant identifiers rather than display names as identity keys.
- Server-side authorization using Entra app roles.
- Relational cycle teams, challenge participation, beneficiaries and evidence metadata.
- One all-or-nothing outcome for multi-beneficiary submissions, intentionally simplifying historical partial-award behaviour.
- Per-challenge solo participation through explicit `ChallengeTeamPolicy.allowSolo` configuration.
- Active-cycle-participant eligibility for new claimants and beneficiaries, without retroactive score changes.
- A shared authorized-manager review queue with the acting manager captured in the audit history.
- Explicit `openAt`, `dueAt`, `closeAt` and participant-override eligibility semantics independent of the reporting cycle.
- An explicit reporting-cycle selector for participant reporting surfaces that never controls challenge eligibility, lifecycle or XP attribution.
- Separate presentation of cycle-level team identity and challenge-specific participation snapshots.
- An Active-participant individual leaderboard with zero-XP participants retained and competition ranking for ties.
- Private evidence storage with authorized, time-limited access rather than public URLs.
- Configurable award categories represented as data rather than fixed code or permanent spreadsheet columns.
- Explicit cycle, challenge and submission lifecycles with audit events.
- Raid entitlement and usage modeled separately from raid XP.
- The portal as system of record, with Teams retained as the social and discussion layer.

These items do not imply that unresolved business rules have been decided.

## Raid Administration Decisions

### Teams Notification MVP — BA-017

The Product Owner approved a deliberately small outbound notification scope. This supersedes the historical Teams-integration deferral only for BA-017; Teams remains a non-authoritative awareness, call-to-action and deep-link surface. Portal authorization and persisted portal state remain authoritative.

Current workflow verification confirms seven Monday events: `ChallengePublished`; initial Submitted; Resubmitted after NeedsEvidence; Needs Evidence; Approved with resulting beneficiary XP; terminal Rejected; and explicit manager-triggered Leaderboard Announcement. `ChallengePublished` means the first successful manager publish command that commits persisted `Draft → Open`; it is a semantic business/notification event, not a persisted `Published` status. Rejected is supported by the current submission status, review action and review endpoint. The leaderboard query exists, but the explicit post command does not yet exist and is part of the future implementation scope.

#### Traceability rules

- Notifications become eligible only after their authoritative state/event transaction commits. Approval notification requires the approval and all beneficiary XP grants to commit together. Teams failures never roll back portal work.
- General posts target a configured Quest communication destination; manager alerts target a configured manager destination. Neither audience is dynamically inferred from portal enrollment or roles.
- Private delivery resolves a durable Participant through intended `(tenantId, oid)` identity, never display name or email. Current synthetic demo identity and optional `EntraObjectId` do not provide real private Teams recipients; real delivery requires explicit mapping/configuration.
- Submission/resubmission manager messages show claimant plus beneficiary count, not beneficiary names, and never expose evidence, attachment names or blob URLs.
- Needs Evidence may include the manager comment only because the current comment is participant-visible, and must use the portal/API's authoritative effective deadline calculation including overrides.
- Approval sends one combined Approved + XP message per distinct beneficiary showing only that beneficiary's XP. Claimant/beneficiary overlap never duplicates the notification.
- Rejected is private to the claimant, shows participant-visible reason and no-XP status, and is not confused with NeedsEvidence.
- Leaderboard posting is manager-triggered only. The server reads a command-time selected-cycle snapshot and posts the first three authoritative ordered rows with existing competition ranks and totals. Synthetic demo disclosure is allowed; real-user public names/XP remain disabled pending explicit privacy approval.
- One domain event yields at most one notification per intended destination/recipient. Retries without a new event do not repost; resubmission is a new event; every explicit leaderboard-post command is intentional and may post again.
- Actionable events are suppressed when superseded: Challenge Published after Closed/Archived; Submitted after leaving Submitted; Resubmitted after leaving Resubmitted; Needs Evidence after leaving NeedsEvidence. Approved, Rejected and timestamped leaderboard snapshots remain useful and send even if delayed.

#### Monday acceptance matrix

| Event | Trigger | Audience | Visibility | Deep link | Duplicate rule | Supersession rule | Monday status |
|---|---|---|---|---|---|---|---|
| `ChallengePublished` | First committed manager Publish transition `Draft → Open` | `QUEST_GENERAL_AUDIENCE` | Public challenge summary and task XP only | Challenge Detail | Once per transition/destination | Suppress if Closed/Archived before send | `MUST_HAVE` |
| Participant Submitted | Committed initial submission | `QUEST_MANAGER_AUDIENCE` | Claimant, challenge/task, beneficiary count, timestamp; no evidence | Manager Review/submission detail | Once per Submitted event/destination | Suppress unless still Submitted | `MUST_HAVE` |
| Participant Resubmitted | Committed NeedsEvidence-to-Resubmitted transition | `QUEST_MANAGER_AUDIENCE` | Claimant, challenge/task, Resubmitted wording and timestamp; no evidence | Manager Review/submission detail | Once per Resubmitted event/destination; later resubmission is new | Suppress unless still Resubmitted | `MUST_HAVE` |
| Needs Evidence | Committed NeedsEvidence review outcome | claimant `PARTICIPANT_PRIVATE` | Challenge/task, participant-visible feedback, authoritative effective deadline | My Activity submission/resubmit | Once per event/claimant | Suppress unless still NeedsEvidence | `MUST_HAVE` |
| Approved + XP | Committed approval and atomic beneficiary XP grants | each distinct `BENEFICIARY_PRIVATE` recipient | Recipient's own challenge/task XP only | My Activity / XP Activity | Once per approval event/distinct participant | Send even if delayed | `MUST_HAVE` |
| Rejected | Committed terminal Rejected review outcome | claimant `PARTICIPANT_PRIVATE` | Participant-visible reason and no-XP status | Submission history | Once per event/claimant | Send even if delayed | `MUST_HAVE` |
| Leaderboard Announcement | Explicit committed manager post command using server snapshot | `QUEST_GENERAL_AUDIENCE` | Selected cycle, generated time, first 3 ordered rows with rank and XP | Selected-cycle Individual Leaderboard | Once per command/destination; repeated commands are new | Send timestamped snapshot even if delayed | `MUST_HAVE` |
| ManualAward / Raid XP / Correction | Future committed event if promoted | affected participant privately | Recipient-specific only | XP Activity | Same event rule | To be defined with promotion | `OPTIONAL_LATER` |

Configuration dependency: General Quest destination, manager destination, app/deployment identity, portal base URL, private-delivery enablement, durable Participant-to-Teams identity mapping, and real-user leaderboard visibility flag. Delivery architecture remains for Teams Architect review. No `DELIVERY_STATUS.md` exists at the time of this decision, so no delivery-status file was changed and implementation is not marked started.

### Monday MVP — BA-016

This decision supersedes the earlier BA-015 Raid Administration deferral only for the MVP below. It does not reopen deferred Raid correction, deletion, advanced audit, analytics or integration scope.

#### Existing model trace

| Concept | Current representation and evidence |
|---|---|
| RaidSession | `Id`, `CycleId`, `Name`, `OccurredAt`; PK `Id`, alternate key `(Id, CycleId)`, FK to Cycle, name max 200, no lifecycle. Seed and historical import populate it; XP Activity and Scoresheet resolve its name. |
| RaidEntitlement | `(ParticipantId, CycleId, PassType)` key, `AssignedCount >= 0`, Physical/Remote only, FK to CycleParticipant. Seed/import populate it; Dashboard reads it. |
| RaidParticipation | `Id`, participant, session, cycle, Physical/Remote `PassType`, `UsedAt`; FKs require same-cycle session and enrolled participant. Current unique index is participant/session/pass type. Seed/import populate it; Dashboard counts rows as Used. |
| Raid XPEntry | Append-only positive Grant with `SourceType = Raid`, required same-cycle `RaidSessionId`, reason/actor/timestamp, and no category/submission/task. Seeded tests/import/reporting already support it; no manager creation endpoint exists yet. |

Conceptually: RaidSession is the occurrence; RaidEntitlement is assigned cycle-level pass capacity; RaidParticipation is one consumed pass/use at the session; Raid XPEntry is an independent score movement. Passes contribute zero XP.

#### Frozen MVP rules

- View Raid history in every cycle status. Create/edit RaidSession only in Active/Closing cycles; Finalised is read-only. Require explicit cycle, trimmed name up to 200 characters and `OccurredAt`. Cycle is immutable; duplicate names are allowed. Edit name/time only before any participation or Raid XP exists. No session lifecycle or deletion.
- Manage Physical/Remote entitlements only for Active CycleParticipants in Active/Closing cycles. Assigned is a non-negative integer; Used is participation count by type; Remaining is Assigned minus Used and cannot be negative. Assignment may change but never below Used.
- Record one pass use for an Active CycleParticipant against a same-cycle RaidSession and matching entitlement with Remaining greater than zero. One use consumes one pass and receives server `UsedAt`.
- `NEW_DECISION`: one participant may have only one participation per RaidSession total. `PassType` identifies the consumed pass. Architect review must strengthen the current database uniqueness to participant/session.
- Raid XP is manager-only, one active participant per command, against a same-cycle RaidSession in an Active/Closing cycle. Participation/pass possession is not required. Amount is positive Int32; reason is trimmed, mandatory and at most 2,000 characters. Write one append-only `Grant / Raid` XPEntry. Finalised rejects new Raid XP.
- Raid XP uses Manual Award-style client `requestId` idempotency. Exact replay returns the existing result; changed data with the same ID conflicts; a new ID permits an otherwise identical legitimate award.
- No Raid XP correction, ledger mutation or deletion.

#### Cycle matrix

| Action | Active | Closing | Finalised |
|---|---|---|---|
| Read Raid history | Yes | Yes | Yes |
| Create/edit eligible session | Yes | Yes | No |
| Assign/update passes | Yes | Yes | No |
| Record participation | Yes | Yes | No |
| Award Raid XP | Yes | Yes | No |

#### Reporting, UI, audit and concurrency

- Existing Dashboard pass balance and XP Activity are sufficient for the participant Monday journey; no dedicated participant Raids screen is required.
- Scoresheet and Leaderboard consume Raid XP through the ledger; passes never affect XP or rank.
- Manager uses one compact Raid Administration screen: session list/create/detail, participant pass balances and participation action, and participant-specific Award Raid XP confirmation.
- XPEntry provides Raid XP audit. Current session/entitlement changes have no history, and participation lacks manager actor; this MVP documents rather than expands those audit limitations.
- Stale session/entitlement writes conflict; rowversion is recommended for Architect review. Entitlement/use writes serialize so `Used <= Assigned`; participant/session uniqueness rejects duplicate use. Request locking provides at-most-once Raid XP. Every write racing Finalisation must atomically recheck cycle status.

#### Deferred after MVP

Raid teams/scoring/leaderboard, approvals, self-registration, QR, venue/calendar features, Teams notifications, bulk CSV, Raid XP correction, deletion/restore, pass-use reversal, advanced audit/analytics, and a dedicated participant Raid management screen.

## Cycle Administration Decisions

### Initial operational workflow — BA-015

#### Cycle configuration and lifecycle

- Create a cycle directly as Active with required unique code, name, `StartsAt` and `EndsAt`; require `StartsAt < EndsAt`.
- Future-dated Active cycles are valid. Dates never automatically control status.
- Support only `Active → Closing → Finalised`, controlled by manager action. No backward transition or reopen is available.
- Multiple Active cycles are valid; reporting selector defaults do not imply uniqueness.
- Code, name and dates are editable while Active and read-only in Closing or Finalised. Transitions do not alter dates.

#### Enrollment

- Enroll existing durable Participants only, and only while the cycle is Active. New enrollment begins Active.
- While the cycle is Active, managers may change an enrollment between Active, Withdrawn and Inactive, including reactivation.
- Closing and Finalised freeze enrollment membership and status.
- Never physically delete a CycleParticipant.
- BA-005 and BA-014 eligibility remains unchanged: only Active enrollment permits new submissions/beneficiary selection and ManualAward Grants; all statuses remain visible historically.

#### Finalisation and independent challenges

Finalised makes cycle metadata, membership and participant statuses read-only while preserving reporting, Scoresheet and historical visibility. New ManualAward Grants remain prohibited. Approved TaskApproval correction remains available without reopening the cycle.

Cycle lifecycle never publishes, closes or modifies challenges, never determines challenge eligibility, and does not require all challenges to be Closed before Finalisation.

#### Confirmation and audit

Require explicit confirmation for both lifecycle transitions and every participant-status change. Lifecycle transitions require a manager reason and use the existing Cycle event mechanism. Cycle creation uses existing creation audit behavior where supported.

The earlier BA-015 statement that no participant-membership history would be added is **superseded following Senior Architect review**. Enrollment and manager-controlled status changes directly affect submission and Manual Award eligibility and require narrowly scoped append-only `CycleParticipantEvent` history. This is not a general audit framework.

`CycleParticipantEvent` semantics:

- **Enrolled:** `FromStatus = null`, `ToStatus = Active`, with manager actor, reason and server timestamp.
- **StatusChanged:** previous and new statuses in `FromStatus` and `ToStatus`, with manager actor, reason and server timestamp.
- Reason is mandatory, trimmed and no longer than 1,000 characters.
- Events are append-only and preserve full enrollment/status history.

Current `CycleParticipant` timestamp semantics:

- Enrollment sets `JoinedAt` to the server timestamp and `LeftAt` to null.
- Active to Withdrawn/Inactive preserves `JoinedAt` and sets `LeftAt` to the server timestamp.
- Withdrawn/Inactive to Active preserves `JoinedAt` and clears `LeftAt`.
- Withdrawn and Inactive transitions preserve `JoinedAt` and set `LeftAt` to the latest transition server timestamp.
- `LeftAt` is current-row operational state; `CycleParticipantEvent` is the full history.

All other BA-015 enrollment restrictions remain unchanged.

Hard deletion of Cycle and CycleParticipant is unavailable. Draft, reopen, automatic transitions, identity/provisioning features, bulk/recurring/cloning operations, team scoring, disputes, Raid Administration, Teams integration and Azure administration remain out of scope.

## Manual XP Award Decisions

### Initial workflow — BA-014

- Create one Manual XP Award for one Active `CycleParticipant` in an explicitly selected Active or Closing reporting cycle.
- Withdrawn and Inactive participants remain visible historically but cannot receive a new ManualAward Grant. No historical-manager override applies.
- Finalised cycles reject new ManualAward Grants but remain correctable through the approved correction workflow where applicable.
- `CycleId` is never inferred from `AwardedAt`.
- Require a configured category with `IsActive = true` and `CycleId` either null or equal to the selected cycle. Active global and selected-cycle categories are valid; inactive and other-cycle categories are not selectable. Historical labels remain visible. Free-text category is unavailable.
- Require a positive integer amount and a mandatory reason of at most 2,000 characters. Zero and negative ManualAward Grants are invalid.
- Create one append-only `Grant / ManualAward` XP entry. Only `Quest.Manager` may perform the action.

Each command carries a unique client-generated `requestId`. First use creates the award; an exact replay of the same request and fields returns the existing result; reuse with different data conflicts. A new request ID follows success or deliberate reset. Identical award content remains valid as a separate business action when submitted with a different request ID.

Reporting surfaces update through the XP ledger. ManualAward correction remains deferred under BA-013. Team/bulk awards, Raid XP creation, CSV import, approval workflow and score disputes remain out of scope.

## Post-Approval Correction Decisions

### Initial correctable source scope — BA-013

Only original `Grant / TaskApproval` XP entries are direct correction targets in the initial workflow. TaskApproval Grant rows may show Correct XP; ManualAward and Raid grants may not. Reversal and Correction rows are never direct targets. ManualAward and Raid correction semantics are deferred pending separate approval.

Repeat correction targets the same original TaskApproval Grant. This remains beneficiary-specific because each beneficiary has a separate original Grant.

The manager supplies the intended new effective amount rather than a delta. Current effective amount is the original Grant plus every signed direct adjustment referencing it, and repeated correction is based on that current effective amount. The intended amount is a non-negative integer and may be zero. Reason is mandatory with a maximum storage length of 2,000 characters.

All history remains append-only and visible. No original or adjustment row is mutated or deleted, and participant and manager reporting reflects the resulting signed ledger.

## Manager Scoresheet Decisions

### Roster and reporting purpose

For a selected reporting cycle, include all Active, Withdrawn and Inactive `CycleParticipant`s, including zero-XP participants, and expose `participantStatus`. The Scoresheet is a historical/audit surface, so a later status change cannot remove historical roster or XP reporting. The Individual Leaderboard remains Active-only.

### Ledger summary

- Show Task Approval XP, Manual Award XP, Raid XP, Adjustments and Total XP.
- Derive all values only from append-only `XPEntry` rows for the selected `CycleId`; `XPEntry.Amount` is authoritative and signed, and `AwardedAt` never determines cycle attribution.
- Source totals include effective signed movements belonging to their source type.
- `netAdjustmentXp = reversalXp + correctionXp`, preserving the signs stored in the ledger. Correction is not presumed positive.
- Adjustments disclose movements already included in source totals and therefore must not be added to those source totals a second time.
- Corrections and Reversals stay visible and never rewrite previous rows.
- Raid-pass entitlement and usage remain separate non-XP resources and are absent from the Scoresheet.

### Participant drill-down

The initial chunk includes an itemized participant ledger showing signed amount, entry type, source type, applicable challenge/task, manual-award category or raid session, reason, timestamp and reversal reference. Correction/reversal chains remain uncollapsed. Summary totals must not be recomputed from only a loaded detail page.

### Presentation scope

- No rank appears in Manager Scoresheet; rank belongs to the Individual Leaderboard.
- Reporting-cycle selection is required.
- Participant search and simple drill-down source filtering are useful when straightforward, but advanced filters are deferred.
- CSV/export is deferred.
- Team XP and team leaderboard remain excluded under unresolved BA-003.

## Manager Challenge Administration Decisions

### Lifecycle and availability

**Implementation terminology reconciliation (2026-08-30):** older BA-011 wording that described `Published` as persisted is superseded. The current manager Publish command persists `Draft → Open`; Open cannot return to Draft. `ChallengePublished` is the semantic BA-017 event produced only by a successful committed publish command, not another persisted status. `Open → Closed → Archived` remains target behavior and an implementation gap until transition endpoints exist; restore remains unavailable.

Open is persisted by the successful manager Publish command. General availability additionally requires current time from `openAt` through `closeAt` inclusive. Participant submission/resubmission eligibility also applies the existing deadline/override rules; an explicit BA-006 override may extend that participant's effective close boundary without changing challenge status. Scheduled/overdue/beyond-close labels remain derived temporal presentation.

### Editing and dates

- Draft challenges permit editing of every challenge, task and configuration field.
- Open (successfully published) challenges lock `openAt`, task list/identity/order, task XP, evidence requirements, scoring modes and all participation/team-policy fields.
- Open (successfully published) challenge name/title, description and supported hero image remain editable.
- Open (successfully published) `dueAt` and `closeAt` may only be extended, never shortened.
- Closed and Archived challenges are read-only in this chunk.
- Existing participant deadline overrides remain authoritative and unchanged.
- `openAt`, `dueAt` and `closeAt` are required, with `openAt < dueAt <= closeAt`; equal due and close times are valid.
- Manager review after close remains allowed.

### Tasks, evidence and participation

- Publication requires at least one task.
- Each task has a durable ID, name, explicit order, non-negative integer XP, scoring mode and evidence requirement.
- Durable IDs are authoritative; task names need not be unique.
- Zero-XP tasks are valid and no business maximum task count applies in this chunk.
- A task selects exactly one of `None`, `Text`, `Link`, `Attachment` or `Multiple`; `Custom` remains deferred and unavailable.
- A task explicitly selects `Individual`, `WholeTeam`, `ClaimantSelectsBeneficiaries` or `AttendanceBased`.
- `ChallengeTeamPolicy` is required only when at least one task is non-Individual. Where required, `1 <= minMembers <= maxMembers`; `allowSolo = true` requires `minMembers = 1`, while `allowSolo = false` requires `minMembers >= 2`.
- Individual-only challenges require no team policy, and `CycleTeam` must not be inferred from challenge participation.

### Publication, archive and deletion

Publish requires a non-empty name/title, valid required dates, at least one valid task, explicit task ordering, non-negative integer XP, supported evidence and scoring modes, and a valid participation policy when applicable. Description, category and hero image are optional.

Draft deletion and hard deletion are deferred. Only `Closed → Archived` is supported, without restore.

## Participant Reporting-Surface Decisions

### Reporting-cycle selector

Participant Dashboard, My Team, Individual Leaderboard and XP Activity use an explicit reporting-cycle selector. It defaults to the participant's most recently started enrolled Active cycle; otherwise an enrolled Closing cycle; otherwise the participant's most recently started enrolled cycle.

The selector is presentation/reporting context only and must not affect challenge eligibility, challenge lifecycle or `XPEntry` reporting-cycle attribution.

### Initial dashboard

The initial participant dashboard shows selected-cycle total XP, individual rank, an actionable submission-status summary, recent XP activity, and raid-pass balance as a clearly separate non-XP resource. Trends, charts and deep analytics are excluded from this chunk.

### My Team — BA-004

- **My Cycle Team:** the current open `CycleTeamMember` assignment for the selected cycle.
- **Challenge Groups:** factual `ChallengeParticipation` snapshots involving the participant.

`CycleTeam` is not inferred from `ChallengeParticipation`, and the two concepts are not merged. This presentation does not calculate team XP or a team leaderboard.

### Individual leaderboard

- Include Active `CycleParticipant`s only for the selected reporting cycle.
- Keep Active participants with zero XP visible.
- Exclude Withdrawn and Inactive cycle participants without altering historical XP.
- Apply competition ranking, so totals `100, 90, 90, 70` receive ranks `1, 2, 2, 4`.
- Within tied totals, order by normalized `displayName` ascending and then `participantId` ascending; this display ordering does not break the tied rank.

## Step 7 Evidence Decisions

### Evidence requirement semantics

| Requirement | Permitted evidence |
|-------------|--------------------|
| `None` | No evidence required |
| `Text` | Text only |
| `Link` | Link only |
| `Attachment` | Attachment only |
| `Multiple` | A combination of supported Text, Link and Attachment evidence |
| `Custom` | Deferred from the initial Step 7 showcase pending explicit machine-readable validation semantics |

A specific evidence requirement does not permit arbitrary evidence types.

### Visibility and non-disclosure

- Step 7 evidence content is accessible only to the submission claimant and authorized `Quest.Manager` users.
- A beneficiary who is not the claimant does not receive evidence-content access.
- Unknown and unauthorized evidence IDs must not disclose existence.

### Resubmission attachments

- Existing accepted attachments are immutable and retained.
- Resubmission may append attachments to the same logical submission; it does not automatically remove or physically replace existing attachments.
- Text/link evidence retains the Step 6 replacement semantics.
- Failed resubmission leaves existing evidence unchanged.
- Attachment deletion and supersession are deferred pending retention/audit policy.

### Initial configurable validation policy

- Maximum 5 attachments per request.
- Maximum 25 MiB per file.
- Maximum 50 MiB combined per request.
- Allowed MIME types: `image/jpeg`, `image/png`, `image/webp`, `application/pdf`, `text/plain`, `application/vnd.openxmlformats-officedocument.wordprocessingml.document`, `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`, `application/vnd.openxmlformats-officedocument.presentationml.presentation`, `video/mp4`.

These are configurable technical defaults and do not resolve evidence-retention policy.

### Malware scanning

- Step 7 introduces `IEvidenceMalwareScanner`.
- Development/Test may use an explicitly identified deterministic pass-through scanner.
- No quarantine or pending-state persistence is required in Step 7.
- Detected malware rejects the request and scanner failure fails closed.
- Production attachments must not start or be enabled without a real configured scanner.
- Real production scanner integration is deferred to production-readiness work.

### Preserved deferrals

- Evidence retention/deletion (`BA-010`, `POLICY_PENDING`).
- `Custom` evidence semantics.
- Real Entra.
- Teams integration.
- Team leaderboard (`BA-003`, `BUSINESS_RULE_PENDING`).
- Score disputes (`BA-008`).

## CSV Source Rules

Future import and reconciliation work must preserve these historical conventions:

- Blank component cell = no recorded event; do not create a zero-valued XP/resource event merely because arithmetic treats the blank as zero.
- Explicit zero total = rostered participant with zero XP.
- Completely blank trailing row = not a participant.
- Raid Assigned/Used columns = non-XP resources/status.
- Displayed total columns are reconciliation values, not XP entries.
- Exact legacy header aliases contain meaningful spacing and hyphen inconsistencies; mappings must be explicit.
- Unknown or ambiguous mappings fail closed.

### July legacy headers

Identifiers: `#`, `PARTICIPANT`.

XP/component headers: `July Challenge 1`, `July Challenge 2`, `July Challenge 3`, `July Challenge Completion`, `Early Bird Bonus`, `Buddy Enrolment Bonus`, `Go Pass1 - T1`, `Go Pass1 - T2`, `Go Pass1-T3`, `Go Pass1-T4`, `Go Pass1-T5`, `Go Pass1 - Bonus`, `Funny - Bonus 1`, `Go Pass 2`, `Raid 1`, `Funny - Bonus2`, `Go Pass 3`, `Funny David`, `Friday Funny `, `David Bday`, `Raid 2`, `Raid 3`.

Total header: `July Total`.

### August legacy headers

Identifiers: `#`, `PARTICIPANT`.

XP/component headers: `August Challenge 1`, `August  Challenge 2`, `August  Challenge 3`, `August  Challenge Completion`, `GoPass4- T1`, `GoPass4- T2`, `GoPass4-  T3`, `GoPass4- T4`, `GoPass-4- T5`, `21st August Friday Funny`, `GoPass-5`.

Total header: `August Total`.

Non-XP headers: `Physical Raid Pass Assigned`, `Physical Raid Pass Used`, `Remote Raid Pass Assigned`, `Remote Raid Pass Used`.

## Open Decision Register

| ID | Question | Status | Blocks |
|----|----------|--------|--------|
| BA-001 | May a multi-beneficiary submission be approved for only the beneficiaries whose evidence is complete? Decision: no; approval is all-or-nothing. | `DECIDED` | Nothing; decision recorded in `DECISIONS.md` |
| BA-002 | When is solo participation allowed? Decision: explicitly per challenge through `ChallengeTeamPolicy.allowSolo`. | `DECIDED` | Nothing |
| BA-003 | How are team leaderboard points aggregated, how are bonuses treated, and how are cross-team points attributed? | `BUSINESS_RULE_PENDING` | Team leaderboard calculation |
| BA-004 | Does `CycleTeam` represent a stable cycle identity distinct from challenge groups? Decision: yes for participant presentation; show My Cycle Team and Challenge Groups separately. | `DECIDED` | Nothing; team scoring remains separately blocked by BA-003 |
| BA-005 | Who may create new submissions or be selected as a beneficiary? Decision: Active `CycleParticipant`s only. | `DECIDED` | Nothing |
| BA-006 | How do open, due, close and participant overrides determine submission eligibility? Decision recorded. | `DECIDED` | Nothing |
| BA-007 | Where is authoritative historical score evidence for July Go Pass 3 awards? | `SOURCE EVIDENCE GAP` | Historical Go Pass 3 expansion/re-import |
| BA-008 | Is a missing-score dispute tracked in the portal or handled externally? | `OPEN BUSINESS QUESTION` | Score-dispute workflow |
| BA-009 | Are reviews assigned to a manager or shared across all managers? Decision: shared authorized-manager queue. | `DECIDED` | Nothing |
| BA-010 | How long is approved/rejected evidence retained and when may it be purged? | `POLICY_PENDING` | Evidence retention/deletion implementation |
| BA-011 | What lifecycle, editability, validation, participation and deletion rules govern manager challenge administration? Decision recorded. | `DECIDED` | Nothing |
| BA-012 | Which roster, ledger breakdown, drill-down, ranking, filtering and export rules govern Manager Scoresheet? Decision recorded. | `DECIDED` | Nothing |
| BA-013 | Which XP sources are directly correctable in the initial Post-Approval Correction workflow? Decision: original TaskApproval Grants only. | `DECIDED` | Nothing; ManualAward and Raid correction semantics are deferred |
| BA-014 | What eligibility, category, amount and idempotency rules govern the initial Manual XP Award workflow? Decision recorded. | `DECIDED` | Nothing |
| BA-015 | What lifecycle, date, enrollment, finalisation and audit rules govern initial Cycle Administration? Decision recorded and audit model amended after Senior Architect review. | `DECIDED` | Nothing; implementation requires the approved `CycleParticipantEvent` schema addition |
| BA-016 | What session, pass, participation, Raid XP and cycle-interaction rules govern the Monday Raid Administration MVP? Decision recorded; earlier deferral superseded for this scope. | `DECIDED` | Nothing; Architect review required for concurrency/version and participant/session uniqueness invariants |
| BA-017 | What committed triggers, audiences, content, privacy, deduplication, failure and freshness rules govern the Teams Notification MVP? Decision recorded; prior Teams deferral superseded only for this scope. | `DECIDED` | Nothing at BA level; real private delivery depends on Entra `(tenantId, oid)` mapping/configuration and real-user leaderboard posting remains disabled pending privacy approval |

## Implementation Gates

| Area | Gate |
|------|------|
| Step 5A authentication | **SAFE TO CONTINUE** |
| Submission / review / beneficiary scoring | **BA-001 RESOLVED — SAFE TO BEGIN WHEN STEP 6 IS AUTHORISED** |
| Participant submission/beneficiary eligibility | **BA-005 RESOLVED — SAFE TO IMPLEMENT** |
| Submission/resubmission date eligibility | **BA-006 RESOLVED — SAFE TO IMPLEMENT** |
| Solo challenge participation | **BA-002 RESOLVED — SAFE TO IMPLEMENT** |
| Manager challenge administration | **BA-011 RESOLVED — SAFE TO IMPLEMENT** |
| Manager Scoresheet | **BA-012 RESOLVED — SAFE TO IMPLEMENT** |
| Manager review ownership | **BA-009 RESOLVED — SAFE TO IMPLEMENT** |
| Beneficiary-specific XP correction | **BA-013 RESOLVED — SAFE TO IMPLEMENT FOR ORIGINAL TASKAPPROVAL GRANTS** |
| Manual XP Award | **BA-014 RESOLVED — SAFE TO IMPLEMENT** |
| Cycle Administration | **BA-015 RESOLVED — SAFE TO IMPLEMENT WITH THE REQUIRED CYCLEPARTICIPANTEVENT AUDIT MODEL** |
| Raid Administration MVP | **BA-016 BUSINESS READY — SAFE FOR ARCHITECT REVIEW** |
| Teams Notification MVP | **BA-017 BUSINESS RULES, ARCHITECTURE AND CODE IMPLEMENTATION DONE; LIVE TENANT ACTIVATION AND REAL-USER LEADERBOARD PRIVACY APPROVAL PENDING** |
| Step 7 evidence/attachment capability | **BUSINESS READY** |
| `Custom` evidence validation | **DEFERRED — NOT IN INITIAL STEP 7 SHOWCASE** |
| Production malware-scanner integration | **DEFERRED TO PRODUCTION READINESS; PRODUCTION ATTACHMENTS MUST REMAIN DISABLED WITHOUT IT** |
| Historical Go Pass 3 expansion/re-import | **BLOCKED ON BA-007** |
| Team leaderboard | **BLOCKED ON BA-003** |
| Evidence retention implementation | **BLOCKED ON BA-010** |
