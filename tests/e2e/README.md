# PAS AI Quest E2E coverage pack

This directory contains two complementary layers:

1. focused existing Vite/Docker Playwright journeys for implemented browser workflows;
2. the exhaustive requirements/state/concurrency design pack added before final UAT.

Start with [COVERAGE_MATRIX.md](./COVERAGE_MATRIX.md), [STATE_MODEL.md](./STATE_MODEL.md), and [TEST_DATA_STRATEGY.md](./TEST_DATA_STRATEGY.md).

## Runtime tiers

- Frontend component tests own deterministic loading/error/validation presentation.
- SQL/API integration tests own transactions, arithmetic, constraints, idempotency, rowversion, and exact races.
- Vite Playwright is fast browser feedback.
- Docker Playwright is final showcase acceptance through production build → nginx → API → SQL/Azurite.
- Multi-context Playwright is reserved for cross-user state visibility and session isolation; it does not replace SQL concurrency tests.

## Discovery and commands

Existing focused Docker scripts remain unchanged. Structured specs are discovered by `playwright.regression.config.ts`, which **does not start Vite or Docker** and expects an already-running preserved production web/nginx/API stack at `http://localhost:5173`.

| Command | Discovers | Environment/prerequisite |
|---|---|---|
| `npm run e2e:regression` | all structured folders | Preserved Docker stack; fixme scenarios are discovered/skipped |
| `npm run e2e:security` | `security/` | Preserved Docker stack with deterministic Demo auth/bootstrap |
| `npm run e2e:reporting` | `reporting/` | Preserved stack; seed-required specs are labelled; unified spec remains fixme |
| `npm run e2e:cycle-admin` | `cycle-admin/` | Frozen backend contract; UI tests remain `CYCLE_ADMIN_PENDING_IMPLEMENTATION` |
| `npm run e2e:concurrency` | `concurrency/` | Catalog only until Test-only barriers and extra server-issued identities exist |
| `npm run e2e:persistence` | `persistence/` | Pending external serial Compose orchestrator; all scenarios fixme |
| `npm run e2e:full-demo` | canonical design | Pending isolated deterministic setup/Cycle Admin continuation; fixme |

The clean Docker acceptance remains owned by the existing `run-docker-showcase.ps1` scripts. Do not point the structured preserved-stack config at Vite and do not infer that discovery means a fixme scenario is covered.

## Suite layout

- `security/`: direct authorization/problem-contract specifications.
- `reporting/`: reporting HTTP contracts and cross-surface reconciliation.
- `cycle-admin/`: frozen BA-015 API contract plus pending UI catalog.
- `concurrency/`: explicit multi-user serial outcomes; scenarios needing a deterministic server barrier are marked `fixme`.
- `persistence/`: restart orchestration design; not safe for ordinary parallel runs.
- `regression/`: canonical full-system journey design.
- existing root Docker specs remain the accepted focused showcase journeys and are not duplicated.

## Cycle Administration

The backend contract is currently implemented and frozen under BA-015. The frontend is still active implementation. Any UI-dependent scenario is marked `CYCLE_ADMIN_PENDING_IMPLEMENTATION`; it is not a defect and not counted as covered. Replace those TODOs with selectors taken from the completed UI before running the complete suite.

The current Challenge Administration contract is also represented precisely: create/update/publish only, with publish persisting `ChallengeStatus.Open`. Frozen close/archive terminology is an `IMPLEMENTATION_GAP`; there are no invented close/archive tests.

## Clean versus preserved state

- **Clean deterministic tests** may assert exact seeded values only after explicit `docker compose down -v` followed by `docker compose up --build -d` (or an equivalent documented reset). They use normal application bootstrap, not QA SQL injection.
- **Preserved-state tests** read the authoritative starting API value and assert deltas/reconciliation. They never assume a clean 25/30 XP total.
- Cycle Admin API specs create Active cycles, which can change default-cycle ordering. They explicitly retain created IDs, explicitly select the intended cycle, and transition their created cycle through its test lifecycle. Reusable helpers never assume a non-null `defaultCycleId`.

## Legacy fixture boundary

`fixtures/step6-workflow.sql` is **FAST LOCAL / LEGACY FIXTURE ONLY**. It must not be used for final clean Docker acceptance, the canonical full demo, production-like E2E, or migration acceptance. Those gates use deterministic application bootstrap.

## Evidence identity

Run summaries record Git HEAD, `working_tree_dirty`, and a diff-stat hash/summary. A screenshot run from a dirty tree must never be described as evidence for a clean commit. Generated reports remain ignored and unstaged.

## Execution policy

This coverage-pack authoring change intentionally did not execute tests, builds, Docker, SQL, Playwright, or migrations. Before execution:

1. Developer reviews API paths and data helpers against the final shared tree.
2. Cycle Admin frontend TODOs are finalized.
3. QA peer reviews coverage statuses and removes no `fixme` without a deterministic synchronization method.
4. Run lower layers first, then focused browser specs, then exactly one serial clean Docker full-demo acceptance.
