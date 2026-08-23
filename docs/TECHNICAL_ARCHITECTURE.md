# PAS AI Quest Portal — Technical Architecture & Data Storage Guide

**Status: FROZEN companion document.** `PORTAL_SPEC.md` remains authoritative if any conflict appears.

Purpose: explain, in implementation-friendly plain English, how PAS AI
Quest data is stored, which Azure/Microsoft technologies are involved,
how scoring and submissions flow through the system, and which rules
must not be reinterpreted during implementation.

Source of truth: [`PORTAL_SPEC.md`](./PORTAL_SPEC.md). This guide is an
architecture/data companion, not a replacement for the frozen functional
specification.

## 1. Architecture at a glance

| **User / Browser**   | →   | React + TypeScript portal hosted in Azure Static Web Apps      |
|----------------------|-----|----------------------------------------------------------------|
| **React frontend**   | →   | ASP.NET Core Web API over HTTPS                                |
| **ASP.NET Core API** | →   | Azure SQL for business data                                    |
| **ASP.NET Core API** | →   | Private Azure Blob Storage for evidence files                  |
| **ASP.NET Core API** | →   | Microsoft Entra ID for identity and app roles                  |
| **ASP.NET Core API** | →   | Application Insights for logs, errors and performance          |
| **Deployment**       | →   | GitHub Actions + Bicep for repeatable build and infrastructure |

Core principle: the browser never becomes the system of record. The API
owns business rules; Azure SQL owns structured business history; Blob
Storage owns binary evidence files.

## 2. What is saved where

| **Storage**          | **What it contains**                                                                                                                                        | **Why**                                              |
|----------------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------|------------------------------------------------------|
| Azure SQL            | Participants, cycle enrollment, teams, challenge rules, submissions, audit events, XP ledger, award categories, raid entitlements/usage, reporting metadata | Queryable, relational, transactional source of truth |
| Azure Blob Storage   | Screenshots, documents, videos and other uploaded evidence                                                                                                  | Binary files stay outside SQL and remain private     |
| Entra ID             | User identity and Quest.Participant / Quest.Manager app-role assignment                                                                                     | Company-native sign-in and server-side authorization |
| Application Insights | Operational telemetry, errors, traces and performance                                                                                                       | Production support and diagnostics                   |

## 3. Core Azure SQL data model

### People and cycle enrollment

```text
Participant
Cycle
CycleParticipant -> cycleId, participantId, status, joinedAt?,
leftAt?
```

Participant is global. CycleParticipant says who is actually enrolled in
a specific cycle, including zero-XP people. Cycle views start from
CycleParticipant, not from 'who happened to have activity'.

### Teams and challenge-specific participation

```text
CycleTeam
CycleTeamMember -> cycleTeamId, participantId, joinedAt,
leftAt?
ChallengeTeamPolicy -> min/max size, solo rule, formation mode,
deadlines
ChallengeParticipation
ChallengeParticipationMember -> participant +
cycleTeamIdAtParticipation?
```

A person's normal monthly team and the people who worked together on a
specific challenge are separate facts. This allows pairs, trios,
cross-team challenge pairings and mid-cycle team changes without
rewriting history.

### Challenges and submissions

```text
Challenge -> cycleId, status, openAt, dueAt, closeAt
ChallengeTask -> challengeId, name, xp, evidenceRequirement, scoringMode

Submission -> claimantId, challengeId, taskId,
challengeParticipationId?, status
SubmissionBeneficiary -> submissionId, participantId
SubmissionEvidence -> submissionId, type/reference/metadata
SubmissionEvent -> status/audit history
```

`challengeParticipationId` answers 'who worked together'.
`SubmissionBeneficiary` rows answer 'who is claiming/receiving XP from this
submission'. They are intentionally not inferred from each other. The relational
child tables keep task, beneficiary and evidence data queryable/auditable in Azure SQL.

## 4. XP is an append-only ledger

There are exactly two XP entry paths: task approval and manual bonus
award. Both create XPEntry records; totals are calculated from the
ledger rather than typed into a mutable Total field.

```text
XPEntry
participantId
cycleId # reporting cycle; not inferred from awardedAt
amount
entryType # Grant | Reversal | Correction
sourceType # TaskApproval | ManualAward | Raid
awardCategoryId?
challengeId? / taskId? / submissionId? / raidSessionId?
cycleTeamId? / challengeParticipationId? # attribution snapshots
reason, awardedBy, awardedAt, reversesEntryId?
```

### Critical cycle-attribution rule

> **If Go Pass 3 belongs to July but Preety approves it on 15 August,**
> XPEntry.cycleId = July and awardedAt = 15 August. The reporting cycle
> comes from the originating challenge/event/session, never from the
> currently active cycle or the award date.

### Corrections never overwrite history

```text
Grant +20 original award
Reversal -20 reversesEntryId = original
Correction +15 reversesEntryId = original
```

The original +20 row remains unchanged. Rejection is not an XP ledger
entry because it moves no score; rejection belongs in SubmissionEvent.

### Multi-beneficiary approval

```text
Submission S101 approved for a 5 XP task:
XPEntry +5 -> Beneficiary A
XPEntry +5 -> Beneficiary B
XPEntry +5 -> Beneficiary C
```

All beneficiary grants are written transactionally: either every
beneficiary receives the grant or none do. TaskApproval idempotency uses
a filtered uniqueness rule for Grant + TaskApproval so retries cannot
double-award while later reversals/corrections remain valid.

## 5. Manual awards and reporting categories

Manual award input is: participant + award category + reason + amount.
There is no manager-selected team field while team-scoring policy is
unresolved.

```text
AwardCategory
EARLY_BIRD
BUDDY_ENROLMENT
FRIDAY_FUNNY
DAVID_BIRTHDAY
...future categories created as data, not code
```

This preserves the category-level detail present in the current score
sheets while avoiding a deployment whenever Preety invents a new bonus
category.

## 6. Raid passes are tracked separately from XP

```text
RaidEntitlement -> participantId, cycleId, type,
assignedCount
RaidParticipation -> participantId, raidSessionId, passType,
usedAt
```

Assigned/used raid passes are operational resources, not score. If raid
participation earns XP, the score exists only as an XPEntry with
sourceType = Raid and raidSessionId set. No duplicate xpAwarded value is
stored on the raid record.

## 7. Evidence files: private Blob Storage

1.  Participant selects evidence in the React portal.

2.  The ASP.NET Core API validates the request and authorized user.

3.  The file is uploaded to private Azure Blob Storage.

4.  Azure SQL stores a private blob reference (container/blob key) on
    the submission/evidence record.

5.  When an authorized user later views evidence, the API issues
    short-lived access using a user-delegation SAS.

Permanent public blob URLs are not part of the design. File size limits,
MIME/type validation and malware scanning are implementation requirements.

**`POLICY_PENDING` — evidence retention:** the retention/deletion period for approved
and rejected evidence is not yet a settled product/records-management rule. Check
`DECISIONS.md`; do not invent or hardcode a duration, and do not enable destructive
automatic deletion until the policy is confirmed. Retention should be configurable.

## 8. Authentication and authorization

Users sign in with Microsoft Entra ID using MSAL. The frontend may hide
manager-only controls for usability, but the API is the security
boundary.

- Quest.Participant: view challenges, submit work, view own activity,
  view permitted leaderboard/team information.

- Quest.Manager: create/manage challenges, review submissions,
  award/correct XP, administer cycles and reporting.

- Manager-only API endpoints must reject participant tokens even if the
  request is manually crafted outside the UI.

App roles are used instead of raw Entra group claims for authorization.

## 9. End-to-end example: participant submission

- 1\. User signs in through Entra ID.

- 2\. React loads eligible challenges through the ASP.NET Core API.

- 3\. User submits Task 2 evidence and selects beneficiaries where
  allowed.

- 4\. Evidence file goes to private Blob Storage; structured submission
  data goes to Azure SQL.

- 5\. SubmissionEvent records Submitted / UnderReview / NeedsEvidence /
  Resubmitted / Approved as the workflow progresses.

- 6\. On approval, the API creates one TaskApproval XPEntry per
  beneficiary in a single database transaction.

- 7\. Individual totals and the manager Scoresheet are calculated from
  XPEntry data.

## 10. Reporting

The same underlying data supports three deliberately different views:

| **View**    | **Purpose**                                                                    |
|-------------|--------------------------------------------------------------------------------|
| Leaderboard | Public/minimal: rank + person/team + total.                                    |
| My Activity | Participant's own itemized ledger and submission history.                      |
| Scoresheet  | Manager-only pivot over challenge/task XP, award categories, raids and totals. |

## 11. Teams integration is later-phase, not the core storage mechanism

- Phase 1: portal is the system of record; Teams remains the
  social/discussion layer.

- Phase 2: outbound Teams announcements/confirmations using one chosen
  modern SDK: Teams SDK or Microsoft 365 Agents SDK. Bot Framework SDK
  is excluded.

- Phase 3: inbound capture is trigger-based (bot invocation/@mention),
  not passive free-text channel monitoring.

Teams posting is asynchronous from the authoritative database save. A
Teams outage must never roll back challenge/submission data.

## 12. Frozen implementation stack

| **Technology**               | **Role**                                                   |
|------------------------------|------------------------------------------------------------|
| React + TypeScript           | Portal UI                                                  |
| Azure Static Web Apps        | Frontend hosting                                           |
| ASP.NET Core Web API         | Business rules and APIs                                    |
| Azure App Service            | Backend hosting                                            |
| Azure SQL                    | Structured business data                                   |
| EF Core migrations           | Relational mapping and schema changes                      |
| Azure Blob Storage           | Private evidence files                                     |
| Microsoft Entra ID + MSAL    | Company sign-in                                            |
| Entra app roles              | Participant vs Manager authorization                       |
| Managed Identity / Key Vault | Service authentication and secret handling                 |
| Application Insights         | Logs, errors, performance                                  |
| Bicep                        | Infrastructure as code                                     |
| GitHub Actions               | Build, test and deployment pipeline                        |
| Teams SDK or M365 Agents SDK | Phase-2 Teams integration; one is chosen later, never both |

## 13. Non-negotiable guardrails

- Cycle, challenge and submission lifecycles are independent.

- Activating/finalising a cycle must not automatically open or close its
  challenges.

- Challenge submission eligibility comes from challenge dates/status
  plus any participant-specific deadline override.

- XPEntry.cycleId is reporting-cycle attribution; awardedAt never
  determines cycle ownership.

- XP is append-only; corrections and reversals are new rows, never edits
  to prior score rows.

- Multi-beneficiary approvals are all-or-nothing and create one XPEntry
  per beneficiary.

- Raid-pass allocation/usage is not XP.

- Evidence files remain private; authorization is enforced before access
  is granted.

- Manager permissions are enforced server-side through Entra app roles.

- Team Leaderboard exists, but its scoring formula must not be invented.

## 14. One deliberately open business rule

**BUSINESS_RULE_PENDING — Team Leaderboard scoring**

- Aggregation formula: sum of member XP, one completion score, or a
  separate team-score model?

- Bonus treatment: do Early Bird / Friday Funny / Raid / other manual
  awards contribute to team score?

- Cross-team challenge assignment: when challenge partners come from
  different CycleTeams, which team receives points?

The schema is intentionally designed so implementation can proceed
without answering these questions. The actual team-leaderboard
calculation must remain disabled/marked pending until Preety confirms
the policy.

## 15. Freeze statement

> **FROZEN**
> This document freezes the technical architecture/data-storage
> explanation for the PAS AI Quest Portal. The functional/data-model
> authority remains [`PORTAL_SPEC.md`](./PORTAL_SPEC.md). The only intentionally
> unresolved policy is Team Leaderboard scoring.
