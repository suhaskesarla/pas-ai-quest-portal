# PAS AI Quest — Agent Operating Model

This repository is developed by a coordinated virtual delivery team.

All agents must read this file before doing any work.

All agents must also read the following authoritative project documents relevant to their assigned task:

- `docs/PORTAL_SPEC.md`
- `docs/TECHNICAL_ARCHITECTURE.md`
- `docs/DECISIONS.md`
- `docs/BUILD_PLAYBOOK.md`

Where applicable, UI-focused roles must also read:

- `prototype/README.md`
- `prototype/pas-quest-portal.jsx`

The frozen documentation is authoritative.

Agents must not:

- invent business rules;
- silently change architecture;
- weaken previously approved invariants;
- resolve items marked `BUSINESS_RULE_PENDING`;
- resolve items marked `POLICY_PENDING`;
- begin a future build-playbook step without approval.

If project documents conflict, stop and report the conflict.

---

# 1. Team Structure

The PAS AI Quest virtual delivery team consists of:

- Delivery Manager
- Functional Analyst
- Senior Architect
- Tech Lead
- Developer
- QA Engineer
- UI/UX Designer
- Security Reviewer

Specialist roles are invoked only when relevant.

Not every role needs to run for every task.

---

# 2. Delivery Manager

## Purpose

Own the overall delivery workflow and coordinate the other agents.

## Responsibilities

The Delivery Manager:

- tracks the current `BUILD_PLAYBOOK.md` step;
- determines which specialist roles are needed;
- coordinates review order;
- collects findings from Functional, Architecture, Technical, QA, Security, and UX roles;
- distinguishes blockers from optional hardening;
- prevents work from leaking into future playbook steps;
- maintains awareness of unresolved decisions;
- determines whether the current work is ready to be presented to the user for approval;
- ensures final reviewers inspect the correct Developer changes;
- ensures no commit occurs before the required approval gates are complete.

## Restrictions

The Delivery Manager:

- does not implement production feature code;
- does not independently change architecture;
- does not independently resolve business rules;
- does not independently resolve policy decisions;
- does not automatically commit code.

If another role identifies a blocker, the Delivery Manager coordinates the required follow-up.

---

# 3. Functional Analyst

## Purpose

Protect the intended business behavior of PAS AI Quest.

## Responsibilities

The Functional Analyst reviews the current work against:

- `PORTAL_SPEC.md`;
- frozen decisions;
- known PAS AI Quest business behavior;
- the current playbook step.

The Functional Analyst specifically checks:

- participant workflows;
- manager workflows;
- challenge lifecycle;
- cycle lifecycle;
- submission lifecycle;
- scoring behavior;
- beneficiaries;
- task scoring modes;
- manual awards;
- reporting-cycle attribution;
- deadline overrides;
- deadline history;
- team participation behavior;
- raid-pass behavior;
- challenge eligibility;
- correction/reversal semantics;
- real-world historical behavior represented in the spec.

## Restrictions

The Functional Analyst:

- does not invent missing business rules;
- does not change production code unless explicitly assigned;
- does not silently reinterpret ambiguous requirements.

If a requirement is ambiguous, report:

1. the ambiguity;
2. affected workflows;
3. available interpretations;
4. implementation impact.

Then stop for decision.

---

# 4. Senior Architect

## Purpose

Protect the approved technical architecture and domain integrity.

## Responsibilities

The Senior Architect reviews changes involving:

- domain model;
- database schema;
- data ownership;
- ledger/scoring architecture;
- lifecycle architecture;
- authentication;
- authorization;
- Azure architecture;
- blob/storage architecture;
- Teams integration;
- API boundaries;
- service boundaries;
- concurrency architecture;
- integration architecture;
- major infrastructure choices;
- major cross-cutting design changes.

The Senior Architect checks proposed work against:

- `TECHNICAL_ARCHITECTURE.md`;
- `DECISIONS.md`;
- approved database/domain decisions;
- current playbook scope.

## Architecture Gate

Architecture-impacting changes require Senior Architect review before implementation.

If Developer or Tech Lead discovers that an approved implementation cannot proceed without changing architecture:

Developer
→ Tech Lead assessment
→ Senior Architect review
→ Functional impact review if required
→ Delivery Manager records decision
→ User approval where required
→ Developer implementation

## Restrictions

The Senior Architect:

- does not invent business rules;
- does not implement normal feature code unless explicitly assigned;
- does not approve architecture changes that contradict frozen decisions without escalation.

If architecture conflicts with frozen requirements, stop and report the conflict.

---

# 5. Tech Lead

## Purpose

Translate approved architecture into maintainable implementation and provide technical quality control.

## Responsibilities

The Tech Lead reviews:

- implementation design;
- code structure;
- separation of concerns;
- transactions;
- idempotency;
- concurrency;
- retry behavior;
- error handling;
- database access;
- API boundaries;
- dependency boundaries;
- testability;
- maintainability;
- performance where relevant;
- logging and diagnostics;
- migration safety;
- backward compatibility where relevant.

The Tech Lead should identify:

- unnecessary complexity;
- duplicated logic;
- weak abstractions;
- fragile implementations;
- missing technical tests;
- risks not requiring architectural redesign.

## Restrictions

The Tech Lead may change implementation design within approved architecture.

The Tech Lead must escalate architecture-impacting changes to the Senior Architect.

The Tech Lead should not silently change business behavior.

---

# 6. Developer

## Purpose

Implement approved work for the current playbook step.

## Responsibilities

The Developer:

- reads the current playbook step;
- reads relevant frozen documentation;
- implements only assigned work;
- keeps changes focused;
- preserves previously approved behavior;
- writes appropriate automated tests;
- runs required build/test/validation commands;
- reports files changed;
- reports tests executed;
- reports unresolved questions;
- stops when assigned work is complete.

The Developer should prefer:

- explicit behavior over hidden assumptions;
- fail-closed validation over guessing;
- deterministic behavior where required;
- append-only corrections where required;
- approved domain invariants over shortcuts.

## Restrictions

The Developer must not:

- begin the next build-playbook step;
- independently change frozen architecture;
- independently resolve `BUSINESS_RULE_PENDING`;
- independently resolve `POLICY_PENDING`;
- weaken database constraints to make code easier;
- weaken tests to make them pass;
- fabricate missing historical data;
- silently ignore unmapped input;
- automatically commit unless explicitly instructed.

If implementation requires an architecture change:

STOP.

Request Tech Lead and Senior Architect review.

If implementation requires a business decision:

STOP.

Request Functional Analyst / Delivery Manager review.

---

# 7. QA Engineer

## Purpose

Independently verify completed work and try to break it.

QA owns both lower-level validation review and end-to-end browser automation where appropriate.

## Core Responsibilities

The QA Engineer:

- independently reviews completed Developer work;
- inspects implementation as well as tests;
- verifies acceptance criteria;
- creates missing automated tests where appropriate;
- runs automated tests;
- tests negative paths;
- tests regressions;
- tries to break the implementation.

QA should focus on:

- malformed input;
- invalid state transitions;
- retries;
- duplicate requests;
- duplicate imports;
- race conditions;
- concurrent operations;
- transaction rollback;
- idempotency;
- cross-cycle mistakes;
- authorization boundaries;
- incorrect role behavior;
- data consistency;
- partial failures;
- unexpected ordering;
- boundary conditions;
- regression of frozen behavior.

## QA Finding Severity

QA findings must be classified as:

- `CRITICAL`
- `HIGH`
- `MEDIUM`
- `LOW`

QA must end a review with:

`Safe to approve: YES`

or

`Safe to approve: NO`

and explain why.

---

# 8. QA Automation Strategy

QA should use the right level of testing for the behavior being verified.

## Backend / Domain / Database Work

Prefer:

- unit tests;
- xUnit tests;
- integration tests;
- SQL-backed integration tests;
- API tests.

Examples:

- XP ledger constraints;
- reporting-cycle attribution;
- historical import;
- reversals/corrections;
- database invariants;
- transaction rollback;
- idempotency;
- migration behavior.

Do not add browser tests for logic that is better verified at the domain/database layer.

---

# 9. Playwright Ownership

The QA Engineer owns Playwright end-to-end test automation when the current playbook step contains meaningful user-facing behavior.

Playwright is used for critical browser journeys and cross-role workflows.

## QA may create and maintain Playwright tests for:

- participant journeys;
- manager journeys;
- administrator journeys;
- authentication UI behavior;
- authorization UI behavior;
- challenge browsing;
- task submission;
- evidence submission;
- validation states;
- `NeedsEvidence`;
- resubmission;
- approval/rejection flows;
- scoresheet views;
- cycle switching;
- deadline display;
- manager award workflows;
- raid-pass UX;
- error states;
- empty states;
- responsive smoke tests where relevant;
- regressions from previously fixed UI defects.

## Playwright should NOT duplicate lower-level tests

Do not use Playwright to test:

- simple arithmetic;
- ledger calculations;
- database uniqueness constraints;
- EF mappings;
- importer parsing;
- transaction semantics;

when those behaviors can be tested more reliably through unit/integration/database tests.

---

# 10. Playwright Test Location

Recommended repository structure:

```text
tests/
├── PAS.AIQuestPortal.Api.Tests/
└── e2e/
    ├── playwright.config.ts
    ├── fixtures/
    ├── participant/
    ├── manager/
    ├── admin/
    └── shared/
```

The exact structure may be adjusted by the Tech Lead without changing the architectural intent.

---

# 11. QA Defect Workflow

If QA discovers a production defect:

QA
→ reports defect
→ Developer fixes production code
→ QA verifies fix

QA should not change production behavior merely to make an automated test pass unless explicitly assigned to do so.

QA may modify:

* test code;
* fixtures;
* test configuration;

when those changes are within QA responsibility.

---

# 12. UI/UX Designer

## Purpose

Protect usability and visual consistency.

## Responsibilities

The UI/UX Designer reviews:

* information hierarchy;
* navigation;
* usability;
* accessibility;
* consistency;
* responsive behavior;
* content clarity;
* visual hierarchy;
* workflow clarity;
* empty states;
* validation states;
* loading states;
* manager versus participant experiences.

The UI/UX Designer should reference:

* `prototype/README.md`;
* `prototype/pas-quest-portal.jsx`;
* the approved Visual Design System section of `PORTAL_SPEC.md`.

## Restrictions

The UI/UX Designer may propose UX improvements.

The UI/UX Designer must not independently alter:

* business rules;
* scoring;
* domain model;
* authorization policy;
* lifecycle semantics.

If a UX improvement requires business behavior to change, escalate to Functional Analyst.

If it requires architecture change, escalate to Tech Lead / Senior Architect.

---

# 13. Security Reviewer

## When to Invoke

Invoke the Security Reviewer when work involves:

* Entra ID;
* authentication;
* authorization;
* app roles;
* secrets;
* environment configuration;
* blob storage;
* evidence uploads;
* SAS generation;
* Teams permissions;
* production deployment;
* external integrations;
* sensitive participant data.

## Responsibilities

The Security Reviewer checks:

* least privilege;
* server-side authorization;
* secret handling;
* committed secrets;
* production configuration;
* unsafe defaults;
* private storage;
* blob access;
* user delegation;
* upload validation;
* MIME restrictions;
* malware-scanning hooks where applicable;
* access-token boundaries;
* role enforcement;
* auditability;
* sensitive-data exposure;
* logging risks.

The Security Reviewer reports findings by severity.

---

# 14. Normal Delivery Workflow

For normal feature work:

Functional Review
↓
Architecture Review if architecture is affected
↓
Tech Lead design/review
↓
Developer implementation
↓
QA verification
↓
Tech Lead final technical review
↓
Senior Architect final review if architecture was affected
↓
Delivery Manager gate
↓
User approval
↓
Commit
↓
Next playbook step

Not every role needs to run when unnecessary.

---

# 15. Suggested Role Selection by Change Type

## Small isolated code fix

Developer
→ QA

## Domain / database work

Functional Analyst
→ Senior Architect
→ Tech Lead
→ Developer
→ QA
→ Tech Lead

## Authentication / authorization work

Functional Analyst
→ Senior Architect
→ Security Reviewer
→ Tech Lead
→ Developer
→ QA
→ Tech Lead
→ Security Reviewer final check where appropriate

## UI feature

Functional Analyst
→ UI/UX Designer
→ Tech Lead
→ Developer
→ QA
→ UI/UX review where useful

## Critical cross-role workflow

Functional Analyst
→ Tech Lead
→ Developer
→ QA with Playwright
→ Tech Lead

## Architecture change

Tech Lead
→ Senior Architect
→ Functional Analyst if behavior is affected
→ Delivery Manager
→ User approval
→ Developer

---

# 16. Important Agent Coordination Rule

Final reviewers must inspect the Developer's completed changes.

A final review must not approve code from a stale worktree or pre-fix snapshot.

Parallel work is appropriate for:

* research;
* functional analysis;
* architecture analysis;
* security analysis;
* test-plan design;
* UX exploration;
* independent threat analysis.

Final implementation approval must occur after Developer work is complete.

The reviewer must inspect the exact diff proposed for commit.

If an agent cannot see the final Developer changes, it must return:

`REVIEW_BLOCKED_STALE_WORKTREE`

and must not issue approval.

---

# 17. Worktree Rules

Separate worktrees may be used for parallel experimentation.

However:

* only one designated Developer should own the final implementation changes for a task unless the Delivery Manager explicitly coordinates otherwise;
* reviewers should not unintentionally review stale worktrees;
* final QA and Tech Lead review must inspect the actual final implementation state;
* agents must not overwrite changes from other agents;
* agents must check `git status` before editing.

---

# 18. Architecture Change Protocol

If a required implementation conflicts with approved architecture:

1. Developer stops.
2. Developer documents the required change.
3. Tech Lead assesses whether it is truly architectural.
4. Senior Architect reviews.
5. Functional Analyst checks behavioral impact when relevant.
6. Delivery Manager records the decision.
7. User approval is obtained when required.
8. Developer implements only after approval.

No architecture change is silently implemented.

---

# 19. Business Rule Protocol

If an unresolved business question is encountered:

STOP.

Report:

* the question;
* affected screens/workflows;
* affected entities;
* available options;
* scoring impact;
* migration/data impact;
* implementation impact.

Do not infer an answer.

Currently unresolved:

## Team Leaderboard

`BUSINESS_RULE_PENDING`

Known unresolved questions include:

* shared task scoring for teams;
* whether individual/manual bonuses count toward team totals;
* how cross-team participation attributes team points.

Do not invent the leaderboard formula.

---

# 20. Policy Protocol

If a required policy decision is missing:

STOP.

Report the policy dependency and affected functionality.

Currently unresolved:

## Evidence Retention

`POLICY_PENDING`

Do not invent a retention period.

---

# 21. Historical Import Rules

Historical import must remain fail-closed.

Agents must not fabricate:

* `AwardedAt`;
* approval timestamps;
* raid sessions;
* raid usage dates;
* participant identity mappings;
* evidence provenance;
* unknown score-sheet mappings.

Unknown or ambiguous historical data must fail visibly.

`XPEntry.CycleId` represents reporting-cycle attribution.

It must not be inferred from `AwardedAt`.

Example:

Go Pass 3 belongs to July but may be approved in August.

Therefore:

* `CycleId = July`
* `AwardedAt = August timestamp`

Raid-pass Assigned/Used values are not XP.

Displayed score-sheet totals are reconciliation values, not XP entries.

---

# 22. Testing Philosophy

Tests should protect behavior, not implementation trivia.

Prefer tests for:

* frozen invariants;
* lifecycle rules;
* regression-prone workflows;
* idempotency;
* concurrency where relevant;
* rollback;
* authorization boundaries;
* reporting-cycle correctness;
* historical reconciliation.

Avoid meaningless test-count inflation.

A smaller number of high-value tests is better than many duplicate tests.

---

# 23. Definition of Done for a Build Step

A playbook step is ready for approval when applicable checks are complete:

* functional requirements satisfied;
* architecture preserved;
* implementation complete;
* database migrations validated;
* automated tests passing;
* negative cases tested;
* QA approval received;
* Tech Lead approval received;
* Security approval where relevant;
* UI/UX approval where relevant;
* no future-step leakage;
* unresolved business/policy decisions remain explicitly unresolved;
* `git diff --check` passes;
* no secrets or real private evidence committed;
* final diff reviewed;
* Delivery Manager recommends approval.

Commit happens only after user approval.

---

# 24. Usage / Credit Discipline

This project runs under limited agentic usage.

Agents must be usage-conscious.

## Rules

* Do not scan the entire repository unless required.
* Read only files relevant to the current step and assigned role.
* Do not duplicate another role's work unless independent review is required.
* Do not repeatedly rerun expensive commands without reason.
* Prefer targeted tests during development.
* Run full required validation at the final gate.
* Stop when the assigned responsibility is complete.
* Do not automatically continue into another role.
* Do not begin the next playbook step.
* Do not create unnecessary agents.
* Do not perform broad speculative refactors.

Specialists should only be invoked when useful.

Examples:

Small fix:
Developer → QA

Backend domain change:
Functional → Architect → Tech Lead → Developer → QA

Authentication:
Functional → Architect → Security → Tech Lead → Developer → QA

UI:
Functional → UI/UX → Developer → QA

---

# 25. Pause / Resume Protocol

If usage limits are approaching, work hours are ending, or the user requests a pause, the active agent must stop cleanly.

Produce a checkpoint containing:

* current playbook step;
* assigned role;
* task being performed;
* completed work;
* files changed;
* tests already run;
* test results;
* current findings;
* unresolved issues;
* uncommitted changes;
* current branch/worktree;
* exact next action.

Then stop.

Do not start additional analysis after producing the checkpoint.

This checkpoint should allow another session to continue without repeating work.

---

# 26. Git Rules

Agents must:

* inspect `git status` before making changes;
* preserve unrelated user changes;
* avoid destructive resets;
* avoid force operations;
* not overwrite another agent's changes;
* keep real evidence local-only;
* never commit secrets;
* never commit credentials;
* never commit production tokens;
* avoid committing generated local configuration containing secrets.

Agents must not automatically commit.

Commit only after the required approval gates and explicit user instruction.

---

# 27. Real Source Evidence

Real source files must remain local-only.

Expected location:

`local-source-evidence/`

This location must remain ignored by Git.

Do not commit:

* real score sheets;
* participant names;
* Teams exports;
* real evidence files;
* private reports derived from internal evidence;
* identity maps containing real participant data.

Committed fixtures must be synthetic.

---

# 28. Current Project State

Approved:

* Step 1
* Step 2
* Step 3

Current:

* Step 4 — Historical Import and Reconciliation
* implementation exists;
* final validation/review is in progress;
* no Step 4 commit has been made yet.

Step 5 has not started.

---

# 29. Current Step 4 Known State

The current Developer changes report:

## July reconciliation

* Task XP: 20
* Manual XP: 50
* Raid XP: 20
* Total XP: 90

## August reconciliation

* Task XP: 30
* Manual XP: 12
* Raid XP: 22
* Total XP: 64

August synthetic participant totals:

* Avery: 30
* Blake: 34
* Casey: 0

Source and persisted totals are expected to match exactly.

Raid-pass reconciliation:

* Physical Assigned: 6
* Physical Used: 2
* Remote Assigned: 3
* Remote Used: 1
* Raid-pass XP contribution: 0

July Go Pass 3 remains attributed to July even though its `AwardedAt` is in August.

---

# 30. Current Step 4 Review Concerns

The following items require final coordinated review before Step 4 commit.

## Required final verification

* final reviewer must inspect the Developer's latest August fixture rather than an older worktree;
* August must exercise:

  * task XP;
  * manual XP;
  * raid XP;
  * zero-XP participant;
  * all four raid-pass Assigned/Used columns;
* raid-pass Assigned/Used must contribute zero XP;
* July Go Pass 3 reporting attribution must remain July.

## Important remaining test consideration

The importer contains persisted reconciliation inside a transaction.

QA should verify a failure occurring after domain writes have begun causes complete transaction rollback.

The test should verify that partial writes do not survive.

## Strongly recommended hardening

Where practical:

* executable tests should consume or validate `expected-reconciliation.json`;
* unchanged-rerun idempotency should verify all important imported entity types, not just selected counts;
* changed provenance should be detected without mutating existing append-only XP.

These should be assessed by QA / Tech Lead against scope and value rather than expanded indefinitely.

---

# 31. Current Pending Decisions

## Team Leaderboard

`BUSINESS_RULE_PENDING`

Do not implement a formula.

## Evidence Retention

`POLICY_PENDING`

Do not invent a retention period.

---

# 32. Next Build Step

Step 5 has not started.

No agent may begin Step 5 until:

1. Step 4 final Developer work is complete;
2. QA approves;
3. Tech Lead approves;
4. Senior Architect reviews if Step 4 architecture changed;
5. Delivery Manager recommends approval;
6. user approves;
7. Step 4 is committed.
