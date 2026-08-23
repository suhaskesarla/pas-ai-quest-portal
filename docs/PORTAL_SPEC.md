# PAS AI Quest Portal — Build Specification (FINAL)

This is the corrected source of truth after an independent review (round 1: 20 findings, all accepted; round 2: refinements on team scope and team-scoring) checked the original spec against the actual Teams chat export and the real July/August score-sheet CSVs. Several core assumptions in v1 didn't survive contact with the source data — this version reflects what's actually true, what's a deliberate product decision, and what's still genuinely unresolved.

A working prototype (`pas-quest-portal.jsx`) exists as a UX/visual reference. **Treat this document, not the prototype, as authoritative for the data model** — the prototype was built before this review and its internal state does not yet fully implement everything below (see the companion "prototype scope note" delivered alongside this document).

---

## 1. What this is, and who it's for

PAS AI Quest is an internal, gamified AI-learning program, run in monthly cycles. Participants complete AI-themed challenges, submit evidence, earn XP, and compete individually and — **confirmed in scope** — as teams. It currently runs through a Teams channel plus a manually-maintained Excel score sheet; both have become the bottleneck as participation has grown. This portal replaces that manual workflow while keeping Teams as the social/discussion layer.

**Stakeholders:** Preety (runs the program, is the primary user and the person whose adoption determines success), Kathy (leadership audience for reporting), participants (e.g. Diane, Arun, Nikhil, ~40 people total). Nikhil has already built a self-hosted Teams reminder bot (Gemma model, Jira card creation) — useful precedent, worth coordinating with rather than duplicating.

**A note on evidence quality, applied throughout this document:** claims below are labeled by how directly they're supported, following the review's correction on this point (finding #11):
- **Directly evidenced** — literally visible in the chat export or CSVs.
- **Reasonable inference** — a plausible read of directly-evidenced facts, but not itself stated outright.
- **Stakeholder decision** — a product choice the source material doesn't and can't settle; needs a person to decide it.

---

## 2. Core architectural correction: three independent lifecycles, not one

This is the single most important fix from the review, and it invalidates v1's central simplification. **Directly evidenced**: the actual Teams history shows July's "Go Pass 3" challenge being extended twice (once to a later July date, then explicitly to 21 August) while the August challenge — with its new guide characters, Vega/Aria/Lumen/Nova — was already live and accepting submissions. A participant was still trying to claim rewards under July's character ("Master Prompt-Fu") in August, before being told "Prompt Fu has gone to China for vacation, we have Vega now."

**The correction:** calendar month is reporting/grouping metadata. It is not a state-transition gate. There are three genuinely independent lifecycles:

```
Cycle lifecycle:       Active → Closing → Finalised (with audited reopen/correction)
Challenge lifecycle:   openAt → dueAt → closeAt/status (each challenge has its own dates)
Submission lifecycle:  Submitted → UnderReview → NeedsEvidence → Resubmitted → Approved/Rejected
```

**Challenge gets an explicit status, not just dates:** `Draft → Published/Open → Closed → Archived`, with the dates in §2 separately determining eligibility. This matters in practice — a manager may build a challenge today but want it invisible until it goes live next Monday, which dates alone don't cleanly express.

**Submission history needs a lightweight, explicit event log**, since rejection, `NeedsEvidence`, resubmission, and manager corrections all reference "the submission's audit trail" throughout this document without one being defined anywhere:

```
SubmissionEvent
  submissionId, eventType, fromStatus?, toStatus?, comment?, actorId, occurredAt
```

This is deliberately separate from the `XPEntry` ledger (§4) — one records workflow/status history, the other records score movements. Every place above that says "the submission's audit trail" means this table.

**Explicit guardrails, to be stated verbatim in any implementation ticket, because this exact bug already had to be fixed once in the prototype and is easy to silently reintroduce:**

> Activating or finalising a cycle must not automatically open or close its challenges.
> Challenge eligibility is evaluated from the challenge's own dates/status plus any participant-specific deadline override — never from `cycle.status === "closed"` or `challenge.cycleId === CURRENT_CYCLE`.
> A cycle may be finalised for normal participant activity while managers retain audited correction capability. Corrections create new ledger entries (§4) — they never rewrite historical XP in place.

**Guide/mascot characters** (Vega, Aria, Lumen, Nova, Master Prompt-Fu) follow the same correction: they attach to a challenge or an announcement, not to one mutually exclusive calendar month. A character transition (like Prompt-Fu → Vega) is itself an in-story event that can overlap a cycle boundary, not a hard switch.

---

## 3. Roster: participants are a first-class table from day one

**Directly evidenced, and a real gap in v1**: both CSVs maintain an explicit roster of ~40 people, including people with zero score that cycle. The v1 prototype instead reconstructs its roster from "whoever is on a team, or has a submission, or has an award" — which means a real participant who simply hasn't done anything yet this cycle silently disappears from every view. That's the opposite of what the actual spreadsheet does.

**The correction:**
- `Participant` is a standalone table, not derived. Every screen that lists people (Scoresheet, Leaderboard, roster pickers) starts from this table, not from activity records.
- **Identity key is a durable ID, not display name.** The real roster already contains name collisions/variants (two different people named Arun — "Arun V" and "Arun Rajamani" — plus abbreviation variants elsewhere). Use Entra `objectId` as the durable key once real auth exists; treat display names as aliases layered on top, never as the primary key.
- **`Participant` alone is insufficient — add `CycleParticipant` as the enrollment record.** A global `Participant` row tells you someone exists in the program; it does not tell you they're enrolled in August specifically. Without a per-cycle enrollment record, a legitimate August zero-score participant is indistinguishable from someone who only ever did July. The August Scoresheet and Leaderboard should be built starting from `CycleParticipant`, not from every participant who has ever existed.

```
CycleParticipant
  cycleId, participantId
  status: Active | Withdrawn | Inactive
  joinedAt?, leftAt?
```
- This was also an internal inconsistency in v1: the spec's own suggested backend listed "participants/roles," but the build playbook's Step 1 instruction to Codex omitted participants entirely. Fixed in the corrected playbook (see the companion `CODEX_BUILD_PLAYBOOK.md`).

---

## 4. Scoring model: two entry paths, one typed ledger

**Keep exactly two ways XP gets awarded — do not add a third:**
1. **Submission-based** — participant submits evidence for a task, manager approves it, the task's fixed XP is awarded.
2. **Manual bonus award** — for anything with no evidence file (raid attendance, early-bird bonus, buddy-enrolment bonus, birthday shout-outs, Friday Funny vote winners). Manager grants directly: participant, award category, reason, amount. **No manager-assignable "team" field on this form** — see below.

**What v1 got wrong underneath those two paths, verified directly against the July CSV:** the real spreadsheet has separate, meaningfully distinct bonus categories — Early Bird, Buddy Enrolment, Raid 1/2/3, Funny Bonus 1/2, "Funny David," David's Bday bonus — and several have **variable amounts per person**, confirmed by parsing the actual data: Buddy Enrolment ranges 5/10/15/20, Raid 2 ranges 8/12/13, Raid 3 ranges 10/15, Funny Bonus 1 ranges 10/20, Funny Bonus 2 ranges 10/15. Collapsing all of this into one flat "Bonus XP" column (as the v1 prototype does) loses information Preety currently has and uses.

**The correction — replace mutable score fields with an append-only, typed ledger:**

**`XPEntry.cycleId` means the reporting cycle the score belongs to — not the calendar cycle active when the XP was awarded.** This is the same overlap problem §2 already fixed for challenges, and without stating it explicitly here too, an otherwise-reasonable implementation could quietly reintroduce it by writing `CycleId = currentCycle.Id` at approval time. Lock the rule:

> For `TaskApproval` entries, `cycleId` inherits the originating challenge's `cycleId` — even when that challenge remains open, or gets approved, in a later calendar month. Manual awards and Raid XP must explicitly resolve their reporting cycle from their originating event/category/session, not from whatever cycle happens to be active. `awardedAt` records *when* the score movement occurred; it must never be used to infer *which cycle the score belongs to*.

Concretely: if Go Pass 3 belongs to the July cycle but gets approved on 15 August, `XPEntry.cycleId = July` while `XPEntry.awardedAt = 15 August` — two separate facts, not one inferred from the other.

```
XPEntry
  id
  participantId
  cycleId                          — reporting cycle, see rule above; NOT derived from awardedAt
  amount
  entryType: Grant | Reversal | Correction
  sourceType: TaskApproval | ManualAward | Raid
  awardCategoryId?                — see below; only for ManualAward
  challengeId?
  taskId?
  submissionId?
  raidSessionId?
  cycleTeamId?                    — attribution snapshot, see §6
  challengeParticipationId?       — attribution snapshot, see §6
  reason
  awardedBy
  awardedAt
  reversesEntryId?                — set only on a Reversal/Correction entry
```

**This must be genuinely append-only, not append-only in name only.** An earlier draft of this ledger put `reversedBy?/reversedAt?/correctionReason?` fields directly on the entry being reversed — that's a mutation in disguise. The correct pattern is that nothing about an existing entry ever changes; a correction is a *new* entry that references the one it reverses:

```
Entry 101: +20  Grant, TaskApproval
Entry 145: -20  Reversal, reversesEntryId=101
Entry 146: +15  Correction, reversesEntryId=101
```

**Rejection is not an XP ledger entry.** A rejected submission awards zero XP — it's a workflow event, not a score movement, and belongs in the submission's own audit trail (status changes, reviewer comments, timestamps), not the financial-style ledger. Keeping these separate is what makes the reconciliation test in §4 below clean: the ledger only ever contains things that actually moved someone's score.

**Manual award categories are data, not a hardcoded code enum.** The real program invents new bonus categories casually — "Funny David," a birthday bonus, a new raid — and a hardcoded `sourceType` list would require a code deployment every time Preety does that again, which doesn't match how she actually runs the program.

```
AwardCategory
  id, name, code, cycleId?
```
e.g. `EARLY_BIRD`, `BUDDY_ENROLMENT`, `FRIDAY_FUNNY`, `DAVID_BIRTHDAY` — addable by Preety without an engineering deployment, while still giving the Scoresheet (§4 below) structured, groupable reporting.

**Manual awards do not take a manager-chosen team field, because §10 (team scoring) is explicitly unresolved.** If the UI let Preety type in a team on a bonus award, that would silently answer §10's question 2 ("do manual bonuses count toward team score?") through a side door, before she's actually decided it. The `cycleTeamId`/participation attribution on a manual award's `XPEntry` stays nullable and gets populated according to whatever team-scoring rule she settles on — not invented ad hoc, award by award, through a form field.

**XP movements are append-only.** Grants, reversals, and corrections are each written as new `XPEntry` records — existing entries are never mutated in place. This closes the exact audit-trail gap this whole project exists to fix; the v1 prototype's `status = Approved; xp = ...` direct field overwrite would have quietly reintroduced the same weakness under a nicer UI. (Workflow transitions like rejection live in `SubmissionEvent`, §2 — not here.)

**Multi-beneficiary approval writes one `XPEntry` per beneficiary, transactionally.** Approving Bhoomi's group submission (herself + two teammates, 5 XP task) must produce three separate `Grant / TaskApproval` entries — one per beneficiary — all referencing the same `submissionId`, `challengeId`, `taskId`, and participation attribution. Approval either writes every beneficiary's grant or none of them; a partial write (e.g. two grants succeed, one fails) is not a valid end state. Enforce this with an idempotency constraint conceptually equivalent to a unique index on `(submissionId, participantId)` for `TaskApproval` grants, so a retried approval can't double-award the same person. **At implementation time this must be a filtered/conditional uniqueness rule** (`WHERE entryType = Grant AND sourceType = TaskApproval`), not a blanket unique constraint on `(submissionId, participantId)` — a blanket constraint would incorrectly block the legitimate `Reversal`/`Correction` entries that reference the same submission and participant later.

- **Idempotent.** A double-click or retried request on "Approve" must not create a duplicate entry.
- **Concurrency-safe.** Two managers approving the same submission in two open tabs must not both succeed.
- The Scoresheet becomes a **pivot over this ledger** (by challenge→task, by award category, by raid, with a total) rather than the v1 design's hardcoded "one column per challenge + one Bonus XP column" — which was directly checked against the real CSVs and found to lose real granularity Preety currently has (per-task columns, not per-challenge).

**Migration/reconciliation, added as a required pre-UI phase (v1 omitted this entirely despite calling the portal "the system of record"):** import roster, challenge/task structures, historical XP ledger, and raid-pass allocations/usage from July and August; map legacy names to durable IDs; flag anything whose exact origin can't be reconstructed. Assign `XPEntry.cycleId` per the rule above during import — the source CSVs already get this right on their own terms (Go Pass 3's XP sits in the *July* Total column regardless of exactly when Preety got around to approving it), so import should preserve that placement, not recompute cycle attribution from any date field. Then **reconcile**: recompute every participant's July and August totals from the imported ledger and assert they match `July Total` / `August Total` exactly. This is a strong, ready-made acceptance test — independently verified: the real July CSV's displayed totals equal the exact sum of that row's XP columns for all 40 rows, zero mismatches. A correct `cycleId` rule is actually necessary for this test to pass at all, not just a nice-to-have alongside it.

---

## 5. Raid passes are a separate tracked resource, not XP

**Directly evidenced and independently verified**: the August CSV has four columns — `Physical Raid Pass Assigned`, `Physical Raid Pass Used`, `Remote Raid Pass Assigned`, `Remote Raid Pass Used` — and these are **excluded from `August Total`** (confirmed arithmetically: for every row checked, the XP columns alone sum exactly to the displayed total; pass columns contribute nothing to it). v1's data model had nowhere to put this at all.

**The correction — a small domain area separate from the XP ledger:**

```
RaidEntitlement
  participantId, cycleId, type (physical|remote), assignedCount

RaidParticipation
  participantId, raidSessionId, passType, usedAt
```

**Don't store `xpAwarded` here.** §4 already establishes the `XPEntry` ledger as the single source of truth for awarded XP — duplicating an amount on `RaidParticipation` risks the two disagreeing (`RaidParticipation.xpAwarded = 12` vs. a corrected `XPEntry.amount = 13`, with no way to know which is right). If a raid produces XP, that's an `XPEntry` with `sourceType: Raid` and `raidSessionId` set — query the ledger by `raidSessionId`, don't cache the amount a second place.

A raid participation can optionally *produce* an XP ledger entry (`sourceType: Raid`) — but the pass entitlement/usage tracking itself is not XP and must not be conflated with it.

---

## 6. Team model: cycle-scoped identity + challenge-scoped participation rules

**#7, closed.** V1's rule — "teams reset each cycle, participants create or join one" — is true but incomplete. **Directly evidenced**: the chat contains the literal line *"Go Pass 3 - 2 : August Challenge is 3"* — team-size requirements differ **by challenge**, not just by month (pairs for one challenge, trios for the next). The chat also shows a poll asking whether participants should self-form groups or have them assigned, someone asking whether a solo team is allowed, and Preety actively hand-placing people into groups — meaning formation mode itself varies, not just size.

**The correction — three distinct concepts, not one flat `Team`:**

```
CycleTeam
  — the stable "who's my team this month" identity
  id, cycleId, name

CycleTeamMember
  — membership as a relation, not an inline array, so mid-cycle
  team changes are representable and historical attribution stays stable
  cycleTeamId, participantId, joinedAt, leftAt?

ChallengeTeamPolicy
  — per-challenge rules, since size/formation mode vary by challenge
  challengeId, mode (self-form | manager-assigned | either),
  minMembers, maxMembers, allowSolo, formationDeadline, lockAfterStart

ChallengeParticipation
  — an (effectively immutable) snapshot of who actually worked together
  on one specific challenge — may be a subset of a CycleTeam,
  or span multiple CycleTeams (see below)
  id, challengeId

ChallengeParticipationMember
  challengeParticipationId, participantId
  cycleTeamIdAtParticipation?     — that member's cycle team at the time, if any
```

**Why membership is a relation, not an inline array:** this is a relational database, and the spec already commits (§10) to historical XP attribution staying stable when someone changes teams mid-cycle. An inline `members[]` array can't represent *when* someone joined or left, or answer "who was on this team as of the date this XP was earned" — a proper `CycleTeamMember` table with `joinedAt`/`leftAt` gives clean support for mid-cycle roster changes, "current team" queries, and a reliable source for `cycleTeamIdAtParticipation` in §6 below.

**Why participation is a related table, not a single `cycleTeamId` on the participation record:** a challenge's `ChallengeTeamPolicy` can allow a pairing that isn't anyone's normal cycle team — for instance two people from *different* `CycleTeam`s partnering because that challenge requires pairs. A single `cycleTeamId` field can't represent that (it would force pretending the pairing belongs to one team or the other). With `ChallengeParticipationMember` recording each person's own cycle-team-at-the-time individually, a pair like Suhas (AI-Migos) + Arun (Bulls-AI) is representable without distorting either team's membership. This also means Preety's eventual answer to §10's third open question (which team gets the points when a challenge crosses cycle-team lines) is actually implementable without a further schema migration.

**Why the snapshot layer matters at all, concretely:** a person's normal cycle team might be a trio, but one specific challenge requires pairs — two of the three participate on that challenge while the third sits it out. Without `ChallengeParticipation`, this is either impossible to represent (single flat team) or forces duplicate team objects per challenge (defeats the point of "my team this month" being a stable concept). This also directly resolves the earlier ambiguity in the submission model — see §7.

**Terminology note, adopted from the review:** call this "cycle-scoped identity + challenge-scoped participation rules," not "policy override" — "override" wrongly implies the challenge is mutating the team itself, when it's really just declaring separate rules that a snapshot of participation gets checked against.

---

## 7. Submission model: claimant, beneficiaries, and participation are three separate facts

**#2, directly evidenced.** The real August chat shows Bhoomi Dholakia claiming Task 2 "for the whole group," Preety asking "is this for whole group or only for you Bhoomi," Bhoomi confirming "whole group," and Preety then asking for proof of all members individually. Elsewhere, teammates are asked to send separate individual proof to Preety for the same shared claim. **v1's data model had a single `member` field on a submission — no way to represent one submission earning XP for multiple people.**

**The correction:**

```
Submission
  id
  claimantId                      — who actually submitted this
  challengeId
  taskId
  challengeParticipationId?       — who worked together (§6), if applicable
  beneficiaries[]                 — who is claiming/receiving XP from this submission
  evidence[]                      — see §8, not a single rigid file
  comment
  status: Submitted → UnderReview → NeedsEvidence → Resubmitted → Approved/Rejected
  reviewerComment?
```

**Relational implementation note for Azure SQL / EF Core:** the array notation above is functional shorthand, not an instruction to serialize important relationships as opaque JSON. Model `Challenge → ChallengeTask`, `Submission → SubmissionBeneficiary`, `ChallengeParticipation → ChallengeParticipationMember`, and `CycleTeam → CycleTeamMember` as relational entities. `Submission.taskId` references a real `ChallengeTask` row. Evidence metadata should also use child records where appropriate so it remains queryable and auditable.

**Why `challengeParticipationId` and `beneficiaries[]` are kept separate rather than one implying the other:** the Bhoomi scenario shows a whole-group claim can exist *before* all individual evidence has arrived — Preety has to ask for the remaining proof after the claim is already made. "Who worked together" and "who's confirmed to receive XP right now" are not always the same set at the same moment; don't infer beneficiaries blindly from team/participation membership.

**A multi-beneficiary submission has a single, all-or-nothing approval outcome — decided explicitly, not left for a coding agent to guess.** If Bhoomi claims for herself and two teammates and only her own evidence is solid so far, the submission stays `NeedsEvidence` for the whole group; it is not partially approved for the beneficiaries who happen to have proof in first. This matches Preety's actual observed pattern in the chat — she waits for the remaining artifact, then awards together, rather than scoring people one at a time as their proof trickles in. `beneficiaries[]` therefore does not need a per-beneficiary status field; one `Submission.status` covers the whole group's outcome.

A task must declare its scoring mode: **individual / whole-team / claimant-selects-beneficiaries / attendance-based (manager-recorded)**.

---

## 8. Evidence: flexible shape, not a rigid three-value enum

**Directly evidenced.** Real evidence in the chat includes screenshots, video, plain text descriptions typed directly into chat (e.g. team mission statements), links, and multiple proof items submitted together — not always exactly one file of exactly one predetermined type, which is what v1's `image | doc | video` enum assumed and would have caused Codex to build unnecessary rejection logic against normal, valid submissions.

**The correction:**
```
evidenceRequirement = None | Text | Link | Attachment | Multiple | Custom
```
A submission carries zero or more `attachments[]`, zero or more `links[]`, and an optional `textResponse` — not exactly one typed file.

---

## 9. Deadlines, extensions, and resubmission are real workflow states

**Directly evidenced.** The chat contains: an individual extension request granted; a global Go Pass 3 extension; a *second* extension to 21 August; revised August dates being reposted; Paul & Saurabh resubmitting/completing their Go Pass 3 entry after Preety asked for "the remaining artifact for all the points." v1 modeled only a single fixed `dueDate` per challenge and Submitted → Approved/Rejected — neither can represent any of the above.

**The correction (right-sized — not a full versioning system on day one):** the `NeedsEvidence → Resubmitted` states already present in §7's submission status list, plus a per-participant deadline override, plus a change history on the challenge's own deadline (who changed it, when). Expand further only if real usage shows it's needed — don't over-build this on the first pass.

---

## 10. Team scoring: one genuinely open business-policy question (formerly #13)

**This is the only unresolved PAS AI Quest business/scoring rule from the full review — everything else above is a settled architectural correction. A separate operational policy on evidence retention is still pending organisational confirmation; see §13 and `DECISIONS.md`.**

The source material establishes that individual XP exists and that group/team submissions exist. It does **not** establish a team-scoring formula, and v1 quietly invented one (sum an approved submission's XP into `submission.team` once). There is no evidence to confirm or deny that formula.

**What is already settled, and should not be re-asked:** a team leaderboard is confirmed in scope. The original proposal document explicitly defines "Team Leaderboard" alongside the Individual Leaderboard as a feature, and the prototype already implements separate Individual/Team leaderboard modes on that basis. The earlier review round raised "does Preety even want team rankings" as a question — that's resolved; it's not open.

**What genuinely needs Preety's decision — three concrete questions, not an abstract "how should team XP work":**

1. **Aggregation formula.** If three teammates each earn 5 XP from one shared task submission, does the team receive 15 (sum of members), 5 (one completion, regardless of team size), or a separately-defined team score decoupled from summed individual XP?
2. **Bonus treatment.** Do individual/manual bonus awards (Early Bird, Friday Funny votes, Raid XP) count toward that person's team score, or only challenge/task XP?
3. **Cross-team challenge assignment.** If a challenge's team policy allows a pairing that isn't someone's normal cycle team (e.g. two people from different `CycleTeam`s partner up for one challenge because it requires pairs), which team receives the resulting points?

**What does *not* need to be asked — resolved by engineering default, not stakeholder decision:** membership-timing attribution. If someone changes cycle teams mid-cycle, their already-earned XP does not retroactively move. This is standard, expected behavior and doesn't need Preety's input.

**How this is made mechanically enforceable, not just documented:** `XPEntry` (§4) carries `cycleTeamId` and/or `challengeParticipationId` as an **attribution snapshot at the moment the XP is earned** — team totals are never recalculated from a participant's *current* membership. This is what actually prevents a later roster change from silently rewriting historical team scores; stating the rule in prose alone wouldn't stop a future engineer from taking the more obvious-looking (but wrong) shortcut of joining against current membership.

**Explicit build instruction:** the schema and ledger should be built generically enough to support any of the three formula options now — do not block schema design on Preety's answer. But **the actual leaderboard calculation must be marked `BUSINESS_RULE_PENDING` and must not default to `SUM(member XP)` "because it seems obvious."** Wire it up only once Preety has answered the three questions above.

---

## 11. Cycle administration: a real workflow, not a hardcoded constant

**Directly evidenced as a gap in the prototype itself**: `CURRENT_CYCLE = "aug26"` is a literal hardcoded string, and every write action in the demo is forced against it. Despite "cycle" being called the foundational entity of the whole architecture, v1 never defined how a new cycle actually gets created.

**The correction — an explicit manager workflow for:** creating a new cycle, activating it, opening/configuring its challenges, configuring that cycle's visual theme and available character roster (while actual guide/character *assignment* stays attached to individual challenges or announcements, per §2 — a cycle does not own one exclusive cast), loading its roster (or confirming it against the standing roster), assigning that cycle's raid-pass entitlements, setting default team policies, and finalising/closing it (with the audited-correction capability from §2 and §4 remaining available afterward).

---

## 12. Roles and authorization: enforced server-side, not just hidden in the UI

**The correction to a real gap in v1:** "role derived from group membership or a role column" was stated too loosely to actually be secure. The thing that must enforce "a participant cannot approve submissions" or "cannot award XP" is the **API**, not which sidebar buttons the frontend chooses to render — a participant hitting a manager-only endpoint directly must be rejected server-side regardless of what the UI shows them.

- Use **Entra app roles** (e.g. `Quest.Participant`, `Quest.Manager`) rather than raw group-membership claims. Group claims have a documented overage condition once a user belongs to more than 200 groups, where group claims silently stop being present in the token — the kind of thing that works in testing and breaks for one real person later. App roles are Microsoft's preferred, simpler mechanism for this kind of per-app authorization.
- Step 2 of the build sequence (§15) must explicitly test hitting manager-only endpoints with a participant token and confirming rejection — not merely confirming the UI looks different per role.
- In the real build, this is a hard requirement (unlike the prototype's harmless self-select "View as" toggle, which only exists to demo both experiences from one login and must not carry forward as a real access-control mechanism).

---

## 13. File storage: private references, not permanent public URLs

**The correction:** don't persist a permanent, publicly-accessible blob URL on a submission record — a leaked link would let anyone view someone's evidence with no authentication, quietly undermining the privacy boundary already designed into My Activity (§17). Persist a private reference (`storageAccount/container/blobKey`) and issue time-limited, authorized access on demand — a **user-delegation SAS**, not an account-key SAS, per current Azure guidance. Also required and previously unspecified: file size limits, MIME/type validation, a malware-scanning approach, and a retention/deletion policy.

**`POLICY_PENDING` — evidence retention:** the application must not invent or hardcode how long approved/rejected evidence is retained. The retention period, whether rejected evidence is automatically purged, and any CPA Australia records-management requirements must be confirmed and recorded in `DECISIONS.md`. Until then, make retention configurable and do not implement destructive automatic deletion.

---

## 14. Reference implementation architecture — decided now, not left to Codex

Earlier drafts said only "full custom web app hosted on Azure," which is too much latitude to leave open once a coding agent starts implementing the backend — different sessions could reasonably pick Node vs .NET, SQL vs Cosmos, different hosting models, and end up with an inconsistent foundation. These choices are made here, once, so they don't drift:

```
Frontend        React + TypeScript
Backend         ASP.NET Core Web API
Database        Azure SQL, EF Core migrations
Authentication  Entra ID / MSAL, Entra app roles (§12), API validates
                tenant + audience + roles server-side
Files           Private Azure Blob Storage, Managed Identity,
                short-lived user-delegation SAS (§13)
Hosting         Azure Static Web Apps (frontend) + Azure App Service (API)
Observability   Application Insights
Secrets         Managed Identity / Azure Key Vault where required
Infrastructure  Bicep
CI/CD           GitHub Actions
Teams           Deliberately deferred to Phase 2 (§18) — not an open fork
                now. Codex must propose Teams SDK vs. Microsoft 365 Agents
                SDK with justification when Phase 2 starts, and use exactly
                one, never both. The archived Bot Framework SDK is excluded
                regardless of which is chosen.
```

The frontend hosting choice matters now, since it affects Phase 1 scaffolding — hence a single answer (Static Web Apps), not "or." The Teams SDK choice genuinely doesn't affect the Phase 1 data model, so it's fine to resolve deliberately later rather than now — the difference from before is that this is now a stated decision to defer, not an unstated gap.

These are reasonable, defensible defaults for this org's environment (already Microsoft/Azure-native) — not the only possible choices. What matters is that they're **decided before Codex starts**, so every session builds against the same foundation instead of each one opportunistically picking its own.

---

## 15. Suggested build sequence (updated)

1. **Freeze requirements + acceptance tests before code.** Commit synthetic July/August-shaped CSV fixtures for automated tests and CI; keep the real July/August source CSVs outside version control in a gitignored local-source-evidence folder. Write explicit rules for challenge/cycle overlap, group claims, XP source types, raid-pass usage, deadlines, and team scoring status (`BUSINESS_RULE_PENDING` for §10). Keep a `DECISIONS.md` — an agent should not resolve an open domain or operational-policy question itself.
2. **Data model + migrations + tests.** Participants (§3), cycle/challenge/submission lifecycles (§2), teams/policies/participation (§6–§7), the XP ledger (§4), raid entitlements (§5), audit/correction metadata. This step must explicitly include `participants` — an earlier draft of this playbook omitted it, which was itself flagged as an inconsistency.
3. **Historical import + reconciliation.** Import July/August and prove all ~40 participants' totals recompute exactly against the source CSVs before any UI work begins — this is the strongest available proof the domain model represents reality.
4. **Entra authentication + server-side authorization** (§12). Test participant-token rejection on manager endpoints, not just UI differences.
5. **Core UI, one workflow at a time, end-to-end before secondary features.** Submit → Review → Score → Correction should work completely before Analytics or other secondary views are built.
6. **Secure file storage** (§13).
7. **CI + deployment to a non-production Azure environment.** Lint, typecheck, tests, build, migration dry-run, dependency scan, secret scan as required checks before merge.
8. **UAT with Preety, using deliberately awkward real scenarios**, not just happy-path ones: a challenge still open past the calendar month boundary, one person claiming for three teammates, partial evidence arriving late, an extension, a resubmission, variable raid XP, a zero-score participant, a manager correction after the fact.
9. **Teams outbound sync** — see §18 for the corrected technology choice.
10. **Teams inbound capture — only after 1–9 are proven and stable**, using a structured trigger (bot/@mention), not passive free-text channel monitoring.

---

## 16. Screen inventory

### Participant-facing
- **Dashboard**
- **Challenges**
- **Submit Work**
- **My Activity**
- **My Team**
- **Leaderboard**

### Manager-facing
- **Dashboard**
- **Challenges**
- **New Challenge**
- **Review Queue**
- **Scoresheet**
- **Leaderboard**
- **Analytics**
- **Cycle Administration**

The prototype is the approved visual/UX reference for these screens. The production implementation must follow the data, lifecycle, persistence, and authorization rules in this specification even where the prototype uses simplified mock state.

---

## 17. Reporting surfaces stay separate on purpose

Unchanged from v1, reaffirmed: **Leaderboard** (public, minimal — rank + name/team + total; keep it glanceable, don't add itemized detail here), **My Activity** (participant's own itemized ledger only — avoids inviting score-comparison at a level of detail the Leaderboard is deliberately designed to avoid), **Scoresheet** (manager-only, full pivot over the ledger — now correctly reflecting §4's granularity rather than v1's over-simplified one-column-per-challenge).

---

## 18. Teams integration — corrected technology, still phased

**Technology correction, independently verified via current Microsoft documentation (not just taken from the review on faith):** the **Bot Framework SDK and Bot Framework Emulator are archived**, no longer maintained, and stopped receiving support tickets after December 31, 2025. Microsoft's current guidance points to the **Teams SDK** (Teams-specific agents) or the **Microsoft 365 Agents SDK** (broader M365 agents) instead. Anywhere this spec or a prompt to a coding agent previously said "Bot Framework + Graph API," read "Teams SDK / Microsoft 365 Agents SDK, with Graph only where appropriate."

**A related implementation trap to avoid:** don't design outbound posting as "background service holds an app-only Graph token, POSTs to the channel" — normal channel posting typically needs *delegated* `ChannelMessage.Send` permission; application-level permission is mostly reserved for migration scenarios. The realistic pattern is **proactive messaging**: the Teams app/agent must be installed in the target team, and the conversation reference must be persisted up front so the app can post to it later unprompted.

**Phase 2 (outbound):** publishing a challenge in the portal posts a formatted announcement to the Teams channel; a submission can post a confirmation reply. Requires a real Entra app registration and bot/agent registration — IT/admin-dependent regardless of who writes the code. Publishing a challenge should be a database transaction first; the Teams notification is enqueued/sent separately, with a `teamsPublishStatus: Pending | Sent | Failed` and retry/idempotency — a Teams outage must never make the authoritative challenge record disappear or fail to save.

**Phase 3 (inbound, deliberately deferred until Phase 1–2 are proven):** explicitly commit to a **trigger-based** design (bot invocation or @mention with explicit challenge/task tags) rather than passive channel monitoring. Passive monitoring would require Graph subscription/webhook handling, subscription renewal, duplicate-event handling, message edit/delete handling, retry logic, and mapping Teams message IDs to portal records — a substantially larger and fundamentally different engineering effort, not a minor variant of the trigger-based approach. Have the bot post a confirmation back rather than silently trusting its own parse of a free-text message.

---

## 19. Visual design system

Unchanged from v1 — sourced directly from cpaaustralia.com screenshots, not an arbitrary palette:

```
Page background:      #F4F5F7        Navy (chrome/text):  #0A1F44
Surface (cards):       #FFFFFF        Primary blue:         #1B5FCE
Gold/brand (XP):        #FFC72C        Purple (category):    #6A2C91
Teal (category):         #00A19A        Orange (category):    #E8631C
```
Typography: `Sora` (display/headings), `Inter` (body), `JetBrains Mono` (XP values, timestamps, eyebrow labels). Pill-shaped buttons throughout. Cards: white surface, hairline border, 3px colored top-border as category accent. This should carry forward directly into the real build — it was reviewed and approved separately from the data-model corrections in this document.

---

## 20. AI-assisted authoring — unchanged scope, restated for clarity

A challenge has `category`, `name`, `description`, `dueAt` (now independent of cycle boundary, §2), an optional `heroImage`, and `tasks[]` (each with `name`, `xp`, `evidenceRequirement` per §8, and `scoringMode` per §7). AI assistance is scoped strictly to **polishing the description's wording** — it must never generate or alter XP values, dates, or the task list; those remain the manager's authoritative input. The structured data is the single source of truth that generates both the portal card and the Teams announcement (once §18's Phase 2 exists) — this replaces Preety's current manual poster-design work rather than imitating it.

---

## 21. What's real vs. simulated in the current prototype

The prototype (`prototype/pas-quest-portal.jsx`) remains a UX/visual reference rather than an implementation of the production data model.

It has now been updated to visibly demonstrate the main post-review workflow corrections: an explicit mock cycle roster including zero-XP participants; claimant vs beneficiaries on submissions; overlapping open challenges across cycle boundaries; Needs Evidence / Resubmitted review states; configurable manual-award categories; raid-pass information kept separate from XP; and a Team Leaderboard that deliberately shows `BUSINESS_RULE_PENDING` rather than inventing a team-scoring formula.

It intentionally does **not** implement the production persistence and infrastructure model. In particular, it does not implement real `XPEntry` persistence, `SubmissionEvent` persistence, `CycleParticipant`, relational `CycleTeamMember`, `ChallengeParticipationMember`, transactional/idempotent multi-beneficiary writes, Entra authorization, private Blob Storage, historical import/reconciliation, real cycle administration, or Teams integration.

Treat the prototype as the source of truth for approved UX/visual direction only. Treat this specification as authoritative for the production data model and business rules.
