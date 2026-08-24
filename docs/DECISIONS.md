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
