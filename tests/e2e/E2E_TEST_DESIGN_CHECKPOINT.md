# PAS AI Quest E2E test-design checkpoint

## 1. Checkpoint status

**E2E design status: DESIGN REVIEWED / EXECUTION DEFERRED**

- Tests executed: **NO**.
- Execution is deferred because additional PAS AI Quest features are still being implemented and the E2E suite will be amended before formal execution.
- Tech Lead technical review was completed.
- The identified E2E design remediations were completed.
- Final targeted Tech Lead review result: **TECHNICAL E2E FINAL TARGETED REVIEW: PASS**.
- Final targeted findings: stable identity valid **YES**; pagination valid **YES**; preserved-state safe **YES**; safe for QA peer review **YES**.
- QA peer review is intentionally deferred; it has **not** passed.
- Product Owner coverage review is intentionally deferred until the suite is closer to feature freeze; it has **not** passed.

This checkpoint records design state only. It is not test-execution evidence or product approval.

## 2. Current coverage artifacts

- [`COVERAGE_MATRIX.md`](./COVERAGE_MATRIX.md) maps requirements and BA decisions to test layers, existing evidence, authored specs, and truthful executable/pending statuses.
- [`STATE_MODEL.md`](./STATE_MODEL.md) inventories implemented durable states, derived views, allowed/forbidden transitions, CycleEvent behavior, ledger behavior, evidence, reporting, and Raid projections.
- [`TEST_DATA_STRATEGY.md`](./TEST_DATA_STRATEGY.md) defines synthetic identities, isolation, clean-versus-preserved rules, stable helper contracts, concurrency synchronization, fixtures, restart rules, and explicit-cycle safety.
- [`README.md`](./README.md) describes suite discovery, commands, runtime expectations, pending Cycle Administration work, lifecycle boundaries, and evidence/report conventions.

Major authored or designed suite areas currently include:

- authentication and direct authorization/security boundaries;
- Challenge Administration create/update/publish;
- participant submission, evidence, review, NeedsEvidence, resubmission, approval, and rejection;
- attachment upload, private retrieval, retained evidence, and storage-disabled behavior;
- Manager Scoresheet and participant drill-down;
- post-approval TaskApproval correction;
- Manual XP Award and request-id idempotency;
- Cycle Administration API contracts and pending UI coverage;
- Participant Dashboard, reporting-cycle selection, XP Activity, and Individual Leaderboard;
- Cycle Team, Challenge Group, Raid XP, and Raid Pass read models;
- historical import, reconciliation, provenance, idempotency, and rollback;
- concurrency ownership and pending deterministic overlap scenarios;
- persistence/restart orchestration design;
- unified-ledger reconciliation design; and
- the canonical full-demo journey design.

## 3. Executable versus pending

An authored test is coverage only when it is executable and discovered by the intended configuration. A markdown scenario or `test.fixme` is not executable coverage.

- `EXECUTABLE/AUTHORED`: implemented test code intended to compile, discover, and execute under its documented runtime prerequisites.
- `TEST_FIXME`: scenario is designed but skipped; it is not covered automation.
- `CYCLE_ADMIN_PENDING_IMPLEMENTATION`: UI-dependent Cycle Administration behavior awaits the completed frontend; selectors/routes must not be invented.
- `PERSISTENCE_AUTOMATION_PENDING`: lower-level seed/migration evidence may exist, but serial Compose restart orchestration is not executable.
- `IMPLEMENTATION_GAP`: target/frozen behavior has no current product contract or endpoint.
- `DEFERRED`: product behavior is explicitly outside current implementation/testing scope.
- `BUSINESS_RULE_PENDING`: behavior must not be implemented or tested as resolved until an approved rule exists.

Known pending areas are:

- Cycle Administration UI scenarios;
- deterministic transaction barriers for genuine overlap tests;
- additional server-issued synthetic identities for some multi-session scenarios;
- unified-ledger browser reconciliation setup;
- Compose restart orchestration;
- full canonical demo execution;
- Raid Administration;
- Challenge Close/Archive (`IMPLEMENTATION_GAP` / `TARGET_NOT_IMPLEMENTED`);
- BA-003 team leaderboard/scoring (`BUSINESS_RULE_PENDING`);
- BA-007 expanded Go Pass 3 source evidence (`DEFERRED` pending evidence);
- BA-008 score-dispute workflow (`DEFERRED`); and
- BA-010 evidence retention/deletion (`DEFERRED` / policy pending).

The authoritative row-by-row classification remains in `COVERAGE_MATRIX.md`.

## 4. Important test-data assumptions

- The deterministic Participant Alpha seed already has a +10 `ManualAward` in the seeded cycle.
- Canonical seeded full-demo arithmetic includes that existing baseline; it does not start from zero.
- The current canonical full-demo final expected participant total is **40 XP**.
- Exact seeded-value assertions are valid only after a documented clean deterministic bootstrap.
- Preserved-stack tests read authoritative starting API/ledger state and assert deltas/reconciliation; they must not hard-code a clean baseline.
- XP Activity tests must not assume ordering or that a seeded item remains on the first page.
- Cursor pagination follows `nextCursor` until the required item is found or pagination is exhausted. A repeated cursor is an explicit malformed-response failure, not a silent stop.
- The deterministic seeded ManualAward is identified by stable XPEntry ID `60000000-0000-4000-8000-00000000000a`; business fields are checked only after identity is established.
- Creating additional Active cycles can change default-cycle ordering. Reusable helpers must not assume `defaultCycleId` is non-null or identifies the intended cycle.
- Isolated scenarios should retain and explicitly pass their created `CycleId` to every command and reporting query.
- Demo roles and Participant IDs are server issued. Tests must not manufacture identities or roles in the browser.

Stable deterministic seed identifiers currently referenced by the test design include:

| Seed record | Stable ID |
|---|---|
| Showcase cycle | `60000000-0000-4000-8000-000000000001` |
| Showcase challenge | `60000000-0000-4000-8000-000000000002` |
| Showcase text task | `60000000-0000-4000-8000-000000000004` |
| Showcase attachment task | `60000000-0000-4000-8000-000000000005` |
| Synthetic Welcome Award category | `60000000-0000-4000-8000-000000000009` |
| Seeded +10 ManualAward XPEntry | `60000000-0000-4000-8000-00000000000a` |
| Synthetic Practice Raid session | `60000000-0000-4000-8000-00000000000b` |

Logical actors remain Manager Alpha, Participant Alpha, and other synthetic identities documented in `TEST_DATA_STRATEGY.md`. Tests resolve their current server-issued identity rather than assuming a client-selected Participant ID.

## 5. Current full-demo design

The canonical design uses the deterministic seeded cycle and the following ledger:

| Contribution | XP |
|---|---:|
| Existing seeded ManualAward baseline | +10 |
| Demo TaskApproval Grant | +25 |
| Demo new ManualAward Grant | +10 |
| TaskApproval Reversal (25 to 15) | -10 |
| TaskApproval Correction (15 to 20) | +5 |

Effective TaskApproval contribution: `25 - 10 + 5 = 20`.

Final participant total: seeded ManualAward `10` + effective TaskApproval `20` + new ManualAward `10` = **40 XP**.

The two ManualAwards must be distinguished by their reason, category, and stable source/request identity. Exact leaderboard rank is asserted only when authoritative competitor totals make it deterministic.

The full-demo spec remains `TEST_FIXME` and **has not been executed**.

## 6. Unified-ledger reconciliation status

The desired isolated-cycle arithmetic is:

- TaskApproval Grant: +25;
- ManualAward Grant: +10;
- Reversal: -10;
- Correction: +5;
- effective TaskApproval: 20;
- final total: **30 XP**.

Cycle Administration can create an isolated Active cycle and enroll Participant Alpha. Challenge Administration can create and publish the required individual +25 task in that cycle. There is currently no supported API for creating the AwardCategory required to issue the isolated-cycle ManualAward; the deterministic category is scoped to the seeded cycle.

Therefore:

- the scenario remains `TEST_FIXME`;
- no unsupported direct database write is used; and
- `COVERAGE_MATRIX.md` must not count it as executable browser coverage.

## 7. Concurrency strategy

SQL/API deterministic integration tests own genuine transactional overlap, including:

- two managers reviewing the same submission;
- submission versus participant or beneficiary deactivation;
- Manual Award versus cycle finalisation;
- Manual Award versus participant deactivation;
- same-request-id concurrent Manual Award;
- concurrent correction effective-state changes;
- duplicate enrollment and enrollment-status overlap;
- cycle concurrent update/transition; and
- challenge concurrent update.

Playwright owns user-visible multi-context consequences, including:

- multi-browser visibility after one actor wins;
- stale-screen behavior and authoritative refresh;
- role switching during an in-flight user journey;
- the same user in multiple server-issued sessions; and
- refresh/recovery after another user completes a command.

Frontend unit/component tests own deterministic request ordering, rapid reporting-cycle switching, request-generation behavior, and stale-response suppression.

Arbitrary sleeps must not be used as a substitute for deterministic concurrency. Exact overlaps require a supported barrier, request hold/release mechanism, or database synchronization primitive.

## 8. Cycle Administration status

Cycle Administration remains under implementation. API/SQL coverage may use the frozen supported contract, but frontend-dependent tests remain `CYCLE_ADMIN_PENDING_IMPLEMENTATION`.

Pending frontend coverage includes:

- navigation entry;
- cycle list, detail, and participant counts;
- create and Active edit;
- stale row-version conflict UX;
- Start Closing confirmation;
- Finalise confirmation;
- read-only Closing and Finalised states;
- participant options;
- enrollment and duplicate-enrollment UX;
- all supported participant status transitions;
- status history presentation if the completed UI exposes it;
- stale participant-version handling;
- authoritative refresh after commands; and
- challenge lifecycle independence.

Do not freeze or invent selectors, labels, routes, or UI structure before the frontend is complete.

## 9. Challenge lifecycle status

The current implemented Manager Challenge Administration contract is:

- create;
- update while Draft;
- publish; and
- successful publish persists `ChallengeStatus.Open`.

While persisted status remains `Open`, dates may derive scheduled/not-yet-open, eligible/open-window, overdue/past-due, and beyond-close temporal views.

Challenge Close and Archive remain `IMPLEMENTATION_GAP` / `TARGET_NOT_IMPLEMENTED`. They must not be represented as current functionality or executable coverage.

## 10. Reporting and preserved-state safety

The deterministic seeded ManualAward is identified by stable XPEntry ID:

`60000000-0000-4000-8000-00000000000a`

The reporting contract must:

1. request XP Activity for the explicit intended cycle;
2. search each page by this stable ID;
3. follow `nextCursor` until the entry is found or pagination is exhausted;
4. never assume the entry is on the first page;
5. never establish identity using only reason, category, amount, or source; and
6. after the ID match, verify the expected source, entry type, +10 amount, reason, category label, and AwardCategory ID.

This design supports preserved stacks containing many additional or business-identical awards. It passed the final targeted Tech Lead review.

## 11. When to resume E2E work

Reopen E2E design after a meaningful feature batch rather than after every small code change. Triggers include:

- completion of the Cycle Administration frontend;
- implementation of Raid Administration;
- another major manager or participant workflow;
- changes to domain states or approved business rules; or
- new or materially changed authorization boundaries.

When work resumes:

1. update requirements traceability;
2. amend `COVERAGE_MATRIX.md`;
3. amend `STATE_MODEL.md` if domain states changed;
4. amend `TEST_DATA_STRATEGY.md` if seed/setup assumptions changed;
5. add or update only relevant tests;
6. run focused Tech Lead review for changed contracts;
7. run QA peer review;
8. run Product Owner coverage review;
9. freeze the reviewed design; and
10. only then begin formal execution.

## 12. Pre-execution gate

Before any formal suite execution:

- resolve Cycle Administration pending tests as appropriate for the completed UI;
- recheck package scripts and Playwright discovery/configuration;
- verify all intended executable specs compile and discover;
- ensure no `test.fixme` or pending scenario is counted as coverage;
- select and document clean-versus-preserved execution modes;
- provide required deterministic barriers before enabling concurrency scenarios;
- document Docker/Compose, SQL, API, nginx, frontend, and Azurite prerequisites;
- confirm generated evidence under `tests/reports/<run-id>/` remains Git-ignored;
- record Git HEAD, dirty-working-tree state, and useful diff identity in evidence summaries; and
- complete final coverage-matrix review.

## 13. Recommended later execution sequence

No phase is executed at this checkpoint.

1. **Phase 1 — Static discovery/config validation:** package scripts, config discovery, TypeScript compilation/discovery, and pending-status audit.
2. **Phase 2 — Backend SQL/API focused tests:** contracts, authorization, transactions, idempotency, concurrency, reporting, import, and migrations as applicable.
3. **Phase 3 — Frontend unit/component tests:** state, validation, request ordering, error handling, and stale-response suppression.
4. **Phase 4 — Focused Playwright feature suites:** targeted participant, manager, admin, reporting, and evidence journeys against their documented runtime.
5. **Phase 5 — Multi-user/concurrency suites:** only scenarios with deterministic synchronization and required server-issued identities.
6. **Phase 6 — Persistence/restart suite:** serial Compose restart without deleting volumes, with authoritative pre/post identifiers.
7. **Phase 7 — Clean Docker canonical full demo:** one deterministic volume reset, production frontend/nginx/API/SQL/Azurite path, browser journey, screenshots, and summary.
8. **Phase 8 — Final integrated regression:** review results, reconcile coverage, and produce final execution evidence.

## 14. Change management

This checkpoint is not a forever-frozen test plan. Future features may add states, routes, roles, approved business rules, or test-data requirements.

When assumptions change:

- amend the existing artifacts rather than rewriting the design from scratch;
- preserve historical decisions and review outcomes;
- mark superseded assumptions explicitly;
- reclassify affected executable/pending coverage truthfully; and
- do not silently delete pending scenarios or coverage gaps.

## CURRENT E2E CHECKPOINT

Design reviewed: YES  
Tech Lead final targeted review: PASS  
Tests executed: NO  
QA peer review completed: NO  
PO coverage review completed: NO  
Execution intentionally deferred: YES  
Safe to amend when features change: YES  
Safe to run without another review: NO

Next major trigger:  
Complete/add next feature batch, then update and review E2E coverage before execution.
