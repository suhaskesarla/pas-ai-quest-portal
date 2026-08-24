# Working with Codex on this build — step-by-step playbook

This is the corrected playbook, written against `PORTAL_SPEC.md` (frozen) and `TECHNICAL_ARCHITECTURE.md`. An earlier draft of this playbook was written before the domain-model review and is now stale — this version replaces it. If Codex or anyone else has a copy of the earlier draft, discard it; this file and the frozen spec are the only source of truth.

## Before you open Codex at all

1. **Push `PORTAL_SPEC.md`, `TECHNICAL_ARCHITECTURE.md`, and `DECISIONS.md` as the first commit**, before any code exists. Point every Codex session at these files directly — don't re-paste context into chat.

   ```
   git add docs/ prototype/
   git commit -m "Frozen spec, architecture guide, and reference prototype"
   git push -u origin main
   ```

2. **`.gitignore` is already in this repo** — confirm it's in place before Codex writes any code, so secrets and build output never get committed by accident.

3. **Do not commit raw source evidence** (the Teams chat export, the score-sheet CSVs) into this repo, even as private. Keep those local — reference them in prompts as needed, but they contain real colleagues' names and internal conversations that don't belong in version control.

## The core safety rule: one small step, one commit, one check-in

- Never ask Codex for more than one phase in a single session.
- **You** review the diff, run it, confirm it actually works, **then** commit. Don't let Codex commit its own unreviewed work as "done."
- If a session goes sideways, first run `git status` and review the diff. Preserve any useful or unrelated work before discarding changes. On a disposable Codex branch/worktree with nothing worth keeping, reset to the last known-good commit and restart the step in a fresh session. Never run `git reset --hard` blindly on `main` or on a working tree containing uncommitted work you may need.
- Use a branch per phase (`git checkout -b <phase-name>`), merge to `main` only once verified. `main` is always the last known-good state.
- Tag stable checkpoints (`git tag v0.1-data-model`) after each phase is verified.

---

## The stack is already decided — don't let Codex re-litigate it

Per `PORTAL_SPEC.md` §14, the reference architecture is frozen:

```
Frontend        React + TypeScript
Backend         ASP.NET Core Web API
Database        Azure SQL, EF Core migrations
Authentication  Entra ID / MSAL, Entra app roles
Files           Private Azure Blob Storage, user-delegation SAS
Hosting         Azure Static Web Apps (frontend) + Azure App Service (API)
Observability   Application Insights
Infrastructure  Bicep
CI/CD           GitHub Actions
Teams           Deferred to Phase 2 — Teams SDK or Microsoft 365 Agents SDK,
                never Bot Framework SDK (archived)
```

Local development substitutes exist for everything except Entra ID — see the "Local development" section below. Codex should never be asked to choose a database, backend language, or hosting model; that decision is already made.

---

## Step-by-step build sequence

Give these to Codex **one at a time**, in order, each pointing at the actual repo files rather than a re-explained summary.

### Step 1 — Freeze acceptance tests before code

> "Read `docs/PORTAL_SPEC.md` and `docs/TECHNICAL_ARCHITECTURE.md`. Before writing any code, write out explicit acceptance-test scenarios covering: challenge/cycle overlap (a challenge from one cycle still open and approvable in a later cycle), a multi-beneficiary group claim with partial evidence (the Bhoomi scenario in spec §7), XP source types, raid-pass usage, deadline extensions and resubmission, and the `BUSINESS_RULE_PENDING` status of team scoring per `docs/DECISIONS.md`. Don't write implementation code yet — just the test scenarios, for me to review."

**Check-in:** do the test scenarios actually reflect the spec's real examples (Go Pass 3, Bhoomi, the July/August CSV structure), or has Codex invented generic placeholder scenarios? Preserve the real edge-case behavior, but use synthetic committed fixture data for CI; the real source CSVs stay local-only.

### Step 2 — Solution scaffolding + local development setup

> "Set up the solution and local development environment before implementing the domain model. Use SQL Server LocalDB (or Dockerized SQL Server) in place of Azure SQL, Azurite in place of Blob Storage, and a stub auth handler returning a fake `Quest.Participant`/`Quest.Manager` identity in place of real Entra ID. Use environment-based configuration so swapping to real Azure SQL, real Blob Storage, and real Entra ID at deployment time requires configuration changes rather than business-logic rewrites. Keep credentials out of committed configuration."

**Check-in:** can a new developer clone the repo, follow the documented local setup, start the frontend/API/database/storage emulator, and reach a basic health endpoint without any Azure resources?

### Step 3 — Data model + migrations

> "Per `docs/PORTAL_SPEC.md` §2–§10, implement the data model in EF Core against local SQL Server (production target: Azure SQL): `Participant`, `CycleParticipant`, `Cycle`, `Challenge` with independent lifecycle status (§2), `CycleTeam`/`CycleTeamMember`, `ChallengeTeamPolicy`, `ChallengeParticipation`/`ChallengeParticipationMember` (§6), `Submission` with `claimantId`/`challengeParticipationId` and relational beneficiaries (§7), `SubmissionEvent` (§2), the append-only `XPEntry` ledger with `entryType`/`sourceType`/`reversesEntryId` (§4), `AwardCategory` (§4), `RaidEntitlement`/`RaidParticipation` (§5, no `xpAwarded` field). Propose the schema/ERD first and wait for my confirmation before generating migrations."

**Relational implementation requirement:** do not turn important relationships into opaque JSON merely because the functional spec uses array notation. Model at least:
- `Challenge` → `ChallengeTask`
- `Submission` → `SubmissionBeneficiary`
- `CycleTeam` → `CycleTeamMember`
- `ChallengeParticipation` → `ChallengeParticipationMember`
- evidence metadata as relational child records where appropriate

`Submission.taskId` must reference a real `ChallengeTask` row.

**Check-in — this is the highest-stakes review in the whole build:**
- Does `XPEntry` actually enforce `cycleId` as reporting-cycle attribution, not derived from `awardedAt`? (spec §4's critical rule — read the schema migration itself, don't take a summary on faith.)
- Is the multi-beneficiary approval idempotency constraint a **filtered** unique index (`WHERE entryType = Grant AND sourceType = TaskApproval`), not a blanket one that would block legitimate reversals?
- Does cycle/challenge/submission status stay genuinely independent, or did a `cycle.status === "closed"` shortcut sneak into challenge eligibility logic anywhere?
- Are `ChallengeTask` and `SubmissionBeneficiary` real relations rather than JSON blobs?

### Step 4 — Historical import + reconciliation (before any UI work)

> "Use committed synthetic July/August-shaped CSV fixtures for normal automated tests and CI. Keep the real July and August score-sheet CSVs outside version control in a gitignored `local-source-evidence/` folder. Write the import/reconciliation code so it can also be run locally against those real files. When run against the real source CSVs, populate the data model, assign `XPEntry.cycleId` per spec §4 (the score's reporting cycle, never the calendar date of approval), recompute every participant's July and August totals from the imported ledger, and assert they match the source `July Total` / `August Total` columns exactly."

This is the strongest available proof the data model is correct. Synthetic fixtures make CI safe and repeatable; the local real-data reconciliation remains the final source-grounded acceptance check without putting colleagues' data into Git history or CI artifacts.

### Step 5 — Entra authentication + server-side authorization

> "Add Entra ID (Azure AD) authentication via MSAL, per spec §12. Role must be derived from Entra app roles (`Quest.Participant`, `Quest.Manager`), never self-selected and never from raw group-membership claims. Every manager-only API endpoint must reject a participant token, checked server-side — not just hidden in the UI."

**Check-in:** actually call a manager-only endpoint (approve a submission, award XP) with a participant token and confirm it's rejected. Don't accept "the UI looks right" as proof.

### Step 6 — Core UI, one workflow at a time

Rebuild the screens using `prototype/pas-quest-portal.jsx` as the UX/visual reference (see its own README for what's real vs. simulated in it) and spec §16 for the full screen list. Do **Submit → Review → Score → Correction** completely, end-to-end, before touching Analytics or other secondary views. One screen or workflow per session — stop and review after each.

> "Using `prototype/pas-quest-portal.jsx` as the visual/UX reference and `docs/PORTAL_SPEC.md` §16 for the full screen list, rebuild [screen name] against the real API and auth from Steps 2–5. Keep the visual design system from the **Visual Design System** section of `docs/PORTAL_SPEC.md` (currently §19) — it's already approved; don't redesign it."

**Final showcase acceptance retro:** Vite Playwright remains the fast feedback loop, but it is not the final runtime gate. Before accepting a showcase step, QA must run the production-built web image through the real Docker Compose stack from clean volumes (`docker compose down -v`, then `docker compose up --build -d`). The browser journey must use `http://localhost:5173`, traverse nginx to the API, rely only on normal deterministic Development/Demo bootstrap data, and produce timestamped synthetic screenshots plus a run summary under ignored `tests/reports/` output. Do not load an E2E SQL fixture for this clean-start showcase check. Run this slower gate for final acceptance and whenever nginx, Docker, Compose, auth/runtime configuration, demo bootstrap, or cross-service integration changes.

### Step 7 — Secure file storage

> "Replace any local file handling with uploads to Azure Blob Storage (Azurite locally), per spec §13: private blob references only, never permanent public URLs. Issue access via short-lived user-delegation SAS. Add file size limits and MIME/type validation. Check `docs/DECISIONS.md` for `POLICY_PENDING — Evidence retention`; do not invent or hardcode a retention period. Keep retention configurable and do not enable destructive automatic deletion until that policy is resolved."

### Step 8 — CI + deployment to non-production

> "Set up GitHub Actions per spec §14: lint, typecheck, tests, build, EF Core migration dry-run, dependency scan, and secret scan as required checks before merge. Deploy to a non-production Azure environment using Bicep."

### Step 9 — UAT with deliberately awkward scenarios

Not a Codex step — pause and pilot with Preety here, using real edge cases, not just happy paths: a challenge still open past the calendar month boundary, one person claiming for three teammates with partial evidence, an extension, a resubmission, variable raid XP, a zero-score participant, a manager correction after the fact. This is the checkpoint spec §15 calls for before building anything further.

### Step 10 — Teams outbound sync (Phase 2)

> "Per spec §18, build outbound Teams sync: publishing a challenge posts a formatted announcement, a submission approval posts a confirmation reply. Propose Teams SDK vs. Microsoft 365 Agents SDK with justification — use exactly one, never both, and never the archived Bot Framework SDK. Design outbound posting as proactive messaging (app installed in the target team, conversation reference persisted up front), not a naive app-only Graph token POST. Publishing a challenge must be a database transaction first; the Teams notification is enqueued separately with `teamsPublishStatus: Pending | Sent | Failed` and retry/idempotency — a Teams outage must never make the challenge record disappear."

### Step 11 — Teams inbound capture (only after 9 and 10 are proven and stable)

> "Per spec §18 Phase 3, build inbound capture using a structured trigger (bot invocation or @mention with explicit challenge/task tags) — not passive free-text channel monitoring. Have the bot post a confirmation back rather than silently trusting its own parse."

---

## Extra hallucination-guarding habits

- **Point Codex at the actual repo files**, not a paraphrase in chat — `docs/PORTAL_SPEC.md`, `docs/TECHNICAL_ARCHITECTURE.md`, `docs/DECISIONS.md`.
- **Ask it to restate the plan before writing code** on anything non-trivial — already built into Steps 1 and 2 above.
- **Be suspicious of any step where Codex says it "assumed" something.** Check the assumption against the spec.
- **Keep sessions scoped to one step.** Scope creep ("while I was at it I also changed X") is exactly how quiet regressions sneak in.
- **Never let Codex resolve `BUSINESS_RULE_PENDING` (spec §10) itself**, even when a default seems obvious. Check `docs/DECISIONS.md` first — if it's still open there, the team-leaderboard calculation stays disabled.

## Quick reference: commit cadence

```
git checkout -b <phase-name>
# ... work with Codex on ONE step from above ...
# ... you review the diff and test it yourself ...
git add -A
git commit -m "<what actually works now>"
git checkout main
git merge <phase-name>
git tag v0.<n>-<phase-name>
git push origin main --follow-tags
```
