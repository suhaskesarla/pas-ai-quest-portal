# Deterministic E2E test-data strategy

## Synthetic identities

Use synthetic-only identities. Never copy local source evidence, employee names, tenant identifiers, or real attachments.

| Logical identity | Purpose |
|---|---|
| Manager Alpha | Primary manager commands and browser context |
| Manager Beta | Stale/concurrent manager commands |
| Participant Alpha | Primary claimant and reporting target |
| Participant Beta | Teammate/beneficiary/concurrent participant |
| Participant Gamma | Other participation and concealment checks |
| Withdrawn Participant | Historical visibility and new-write rejection |
| Inactive Participant | Historical visibility and new-write rejection |

The current Development bootstrap may expose fewer fixed profiles. Tests requiring Manager Beta or additional participant sessions must use an approved Test-only identity catalog or API host fixture; they must not manufacture role/ParticipantId client-side.

## Isolation model

1. SQL/API integration tests create a unique database per test class and delete it afterward.
2. Fast browser tests use immutable deterministic bootstrap state or create uniquely coded data with a run/test UUID.
3. Commands use unique request IDs. An idempotency test deliberately reuses exactly one captured ID.
4. Specs never depend on execution order or another spec's mutable output.
5. Clean showcase suites use normal Development/Demo bootstrap only—never `step6-workflow.sql`.
6. Preserved-volume diagnostics reconcile UI values to authoritative API state rather than hard-coded clean totals.
7. `fixtures/step6-workflow.sql` is **FAST LOCAL / LEGACY FIXTURE ONLY**. It is prohibited for final clean Docker, canonical demo, production-like E2E, and migration acceptance.

## Stable helper contracts

Helpers may wrap, but must not weaken, real contracts:

- `openDemoSession(profileKey)` uses `POST /api/auth/demo/session`, then confirms `/api/auth/me`.
- `createCycle`, `enrollParticipant`, and status/lifecycle commands use server-returned base64 row versions and reasons.
- `createChallenge` preserves durable task IDs and server versions; publish waits for confirmed API response.
- `submit`, `resubmit`, `review`, `correctXp`, and `manualAward` wait for their command response and then query authoritative state.
- `xpSnapshot` reads Scoresheet/participant reporting; arithmetic assertions use API totals plus persisted-ledger integration tests.
- `barrier` is a deterministic Test-only server/database synchronization facility. Until one exists, races needing exact overlap remain `test.fixme`, not sleep-based pseudo-concurrency.

## Concurrency synchronization

Permitted techniques:

- two Playwright BrowserContexts with independent server-issued sessions;
- two EF DbContexts/HTTP clients released by `TaskCompletionSource`/database application locks;
- request interception that pauses a request before sending, when the race is at the client boundary;
- polling an authoritative API/SQL predicate with a bounded deadline.

Forbidden: arbitrary multi-second sleeps, client-created roles, direct writes that bypass the domain command under test, or automatic retry of row-version commands.

Each race declares valid serial outcomes. Assertions focus on impossible states: partial beneficiaries, duplicate grants/events, a successful stale write after the competing status won, torn evidence, or mutated append-only history.

## Files and evidence

- Use small committed synthetic TXT/PNG/PDF/OOXML fixtures constructed for validation.
- Invalid files must be synthetic and non-sensitive.
- Generated screenshots/reports remain under ignored `tests/reports/<run-id>/`.
- Attachment tests assert private storage and API-mediated access; never preserve SAS/query credentials in reports.

## Restart suites

- `down/up` without `-v` proves durable state.
- exactly one `down -v` clean acceptance proves deterministic baseline restoration.
- Restart scenarios must record container health, run ID, git SHA, whether fixtures were used, and pre/post authoritative identifiers.
- Destructive Compose orchestration lives in an explicit serial acceptance script, never inside a parallel Playwright worker.

## Clean and preserved arithmetic

- A clean deterministic test may assert a known seeded value such as 25 XP only after an explicit volume reset and normal application bootstrap.
- A preserved-state test obtains `before` from the authoritative API, performs one command, then asserts `after = before + expected delta` and reconciles source/entry totals. It does not hard-code the baseline.
- Unified ledger acceptance is designed for an explicit isolated CycleId with exact rows +25 TaskApproval, +10 ManualAward, -10 Reversal and +5 Correction, giving Task source 20, Manual source 10, net adjustments -5, total 30. It remains `TEST_FIXME`: cycle/enrollment/challenge creation are supported, but no supported AwardCategory creation contract can place the ManualAward in the isolated cycle. Direct SQL setup is prohibited for this production-like reconciliation.
- The seeded-cycle canonical full demo includes the existing +10 "Synthetic local-development showcase award" baseline. Its new +25 TaskApproval, new +10 ManualAward, -10 Reversal and +5 Correction produce a final total of 40, not 30; the seeded and new ManualAwards are identified separately by reason, category, and source/request identity.

## Default-cycle safety

- `defaultCycleId` is nullable unless the spec itself declares `DETERMINISTIC_DEMO_SEED_REQUIRED` and verifies the prerequisite enrolled cycle.
- Reusable helpers require an explicit cycle ID and never select a default implicitly.
- Cycle Admin contract tests can create more recently started Active cycles and thereby change default ordering. Such tests keep the returned ID, explicitly address it, and finalise/contain it where the scenario permits.

## Cycle Administration limitation

BA-015 API data may be created through its frozen HTTP contract for API/concurrency tests. Do not seed a permanent Draft concept or invent Cycle Admin UI locators. UI journeys remain `CYCLE_ADMIN_PENDING_IMPLEMENTATION` until the frontend is complete.
