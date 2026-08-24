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

**Status:** `OPEN BUSINESS QUESTION`

**Rule:** `allowSolo` remains configurable and no default may be inferred.

## Missing / Future Product Decisions

| Area | Classification | Evidence and decision needed |
|------|----------------|------------------------------|
| Challenge-date discoverability | `PRODUCT DESIGN CHOICE` | Teams page 7 shows difficulty finding the latest challenge dates. Decide how authoritative current and revised dates are surfaced and how superseded announcements are identified. |
| Score-dispute workflow | `OPEN BUSINESS QUESTION` | Teams pages 7 and 17 show participants reporting missing scores. Decide whether disputes are tracked in the portal or remain an external Teams process. |
| Wall of Fame / offline artifacts | `OUT OF SCOPE CANDIDATE` | Teams page 14 shows group artifacts being used for an offline display. Confirm whether any portal gallery or artifact workflow is required. |
| Stable `CycleTeam` semantics | `OPEN BUSINESS QUESTION` | Evidence confirms named challenge groups and group reuse, but not a formally stable monthly team distinct from challenge participation. Clarify the intended meaning. |
| Manager assignment | `OPEN BUSINESS QUESTION` | Evidence shows manager actions but does not establish review ownership or whether every manager shares one queue. |
| Due versus close behaviour | `OPEN BUSINESS QUESTION` | Historical messages communicate final dates but do not clearly distinguish `dueAt` from `closeAt` or define late-submission behaviour. |
| Roster eligibility/status semantics | `OPEN BUSINESS QUESTION` | CSVs confirm rostered zero-XP participants but do not define enrollment authority or the meanings of Active, Withdrawn and Inactive. |

## Intentional Product Decisions

The following approved design areas deliberately improve or generalise the historical Teams-and-score-sheet process:

- A typed, append-only XP ledger with grants, reversals and corrections instead of mutable score cells.
- Durable participant identifiers rather than display names as identity keys.
- Server-side authorization using Entra app roles.
- Relational cycle teams, challenge participation, beneficiaries and evidence metadata.
- One all-or-nothing outcome for multi-beneficiary submissions, intentionally simplifying historical partial-award behaviour.
- Private evidence storage with authorized, time-limited access rather than public URLs.
- Configurable award categories represented as data rather than fixed code or permanent spreadsheet columns.
- Explicit cycle, challenge and submission lifecycles with audit events.
- Raid entitlement and usage modeled separately from raid XP.
- The portal as system of record, with Teams retained as the social and discussion layer.

These items do not imply that unresolved business rules have been decided.

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
| BA-002 | When is solo participation allowed? | `OPEN BUSINESS QUESTION` | Any challenge that permits or rejects solo participation |
| BA-003 | How are team leaderboard points aggregated, how are bonuses treated, and how are cross-team points attributed? | `BUSINESS_RULE_PENDING` | Team leaderboard calculation |
| BA-004 | Does `CycleTeam` represent a stable monthly identity distinct from challenge groups? | `OPEN BUSINESS QUESTION` | Final team-management semantics and related UI |
| BA-005 | Who is eligible/enrolled and what do roster statuses mean? | `OPEN BUSINESS QUESTION` | Roster administration behaviour |
| BA-006 | How do due date, close date and late submission differ? | `OPEN BUSINESS QUESTION` | Final submission eligibility/lifecycle rules |
| BA-007 | Where is authoritative historical score evidence for July Go Pass 3 awards? | `SOURCE EVIDENCE GAP` | Historical Go Pass 3 expansion/re-import |
| BA-008 | Is a missing-score dispute tracked in the portal or handled externally? | `OPEN BUSINESS QUESTION` | Score-dispute workflow |
| BA-009 | Are reviews assigned to a manager or shared across all managers? | `OPEN BUSINESS QUESTION` | Manager assignment/queue behaviour |
| BA-010 | How long is approved/rejected evidence retained and when may it be purged? | `POLICY_PENDING` | Evidence retention/deletion implementation |

## Implementation Gates

| Area | Gate |
|------|------|
| Step 5A authentication | **SAFE TO CONTINUE** |
| Submission / review / beneficiary scoring | **BA-001 RESOLVED — SAFE TO BEGIN WHEN STEP 6 IS AUTHORISED** |
| Historical Go Pass 3 expansion/re-import | **BLOCKED ON BA-007** |
| Team leaderboard | **BLOCKED ON BA-003** |
| Evidence retention implementation | **BLOCKED ON BA-010** |
