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

## Manager Challenge Administration Decisions

### Lifecycle and availability

Persist `Draft`, `Published`, `Closed` and `Archived` only. The allowed irreversible lifecycle is `Draft → Published → Closed → Archived`; Published cannot return to Draft, only Closed may be Archived, and restore is unavailable in this chunk.

Open is derived rather than persisted. The general challenge Open state requires Published status and current time from `openAt` through `closeAt` inclusive. Participant submission/resubmission eligibility additionally applies the existing deadline/override rules; an explicit BA-006 override may extend that participant's effective close boundary without changing challenge status. UI labels such as Scheduled and Open are presentation only.

### Editing and dates

- Draft challenges permit editing of every challenge, task and configuration field.
- Published challenges lock `openAt`, task list/identity/order, task XP, evidence requirements, scoring modes and all participation/team-policy fields.
- Published name/title, description and supported hero image remain editable.
- Published `dueAt` and `closeAt` may only be extended, never shortened.
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

## Implementation Gates

| Area | Gate |
|------|------|
| Step 5A authentication | **SAFE TO CONTINUE** |
| Submission / review / beneficiary scoring | **BA-001 RESOLVED — SAFE TO BEGIN WHEN STEP 6 IS AUTHORISED** |
| Participant submission/beneficiary eligibility | **BA-005 RESOLVED — SAFE TO IMPLEMENT** |
| Submission/resubmission date eligibility | **BA-006 RESOLVED — SAFE TO IMPLEMENT** |
| Solo challenge participation | **BA-002 RESOLVED — SAFE TO IMPLEMENT** |
| Manager challenge administration | **BA-011 RESOLVED — SAFE TO IMPLEMENT** |
| Manager review ownership | **BA-009 RESOLVED — SAFE TO IMPLEMENT** |
| Beneficiary-specific XP correction | **DECIDED — SAFE TO IMPLEMENT USING THE APPROVED APPEND-ONLY LEDGER** |
| Step 7 evidence/attachment capability | **BUSINESS READY** |
| `Custom` evidence validation | **DEFERRED — NOT IN INITIAL STEP 7 SHOWCASE** |
| Production malware-scanner integration | **DEFERRED TO PRODUCTION READINESS; PRODUCTION ATTACHMENTS MUST REMAIN DISABLED WITHOUT IT** |
| Historical Go Pass 3 expansion/re-import | **BLOCKED ON BA-007** |
| Team leaderboard | **BLOCKED ON BA-003** |
| Evidence retention implementation | **BLOCKED ON BA-010** |
