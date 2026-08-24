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
