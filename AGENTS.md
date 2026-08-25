# PAS AI Quest — Agent Operating Model

This repository is developed by a coordinated virtual delivery team.

The goal of the agent model is to accelerate delivery while preserving quality.

Agents are advisory gates, not mandatory ceremony.

Use the minimum number of roles necessary to safely deliver the current change.

A previously approved design does not require repeated approval unless the implementation materially deviates from it.

All agents must read this file before doing work.

Agents must also read the project documents relevant to their assigned task:

- `docs/PORTAL_SPEC.md`
- `docs/TECHNICAL_ARCHITECTURE.md`
- `docs/DECISIONS.md`
- `docs/BUILD_PLAYBOOK.md`

For UI-focused work also read:

- `prototype/README.md`
- `prototype/pas-quest-portal.jsx`

The frozen project documentation is authoritative.

Agents must not:

- invent business rules;
- silently change architecture;
- weaken approved invariants;
- resolve `BUSINESS_RULE_PENDING` items without approval;
- resolve `POLICY_PENDING` items without approval;
- begin a future build-playbook step without approval.

If project documents conflict, stop and report the conflict.

---

# 1. Operating Principle

The project should spend more time building than coordinating agents.

Default to the smallest useful delivery loop.

## Normal feature

Developer
→ QA
→ User approval
→ Commit

## Important backend/domain change

Tech Lead design check if needed
→ Developer
→ QA
→ User approval
→ Commit

## Architecture-impacting change

Senior Architect reviews the design before implementation
→ Developer
→ QA
→ User approval
→ Commit

## Authentication/security change

Senior Architect + Security Reviewer perform a short upfront review
→ Developer
→ QA
→ User approval
→ Commit

## UI feature

UI/UX Designer provides design direction
→ Developer
→ QA / Playwright
→ User approval
→ Commit

Do not run every role for every task.

---

# 2. Delivery Manager

## Purpose

Coordinate delivery only when coordination is useful.

The Delivery Manager is not a mandatory approval gate for every change.

## Responsibilities

The Delivery Manager may:

- identify the current `BUILD_PLAYBOOK.md` step;
- decide which specialist roles are actually needed;
- prevent work from leaking into future steps;
- summarize outstanding blockers;
- coordinate handoffs for unusually complex work;
- maintain a pause/resume checkpoint.

## Restrictions

The Delivery Manager:

- does not implement production feature code;
- does not duplicate QA or Tech Lead reviews;
- does not automatically require every specialist role;
- does not independently change architecture;
- does not independently resolve business or policy decisions;
- does not automatically commit.

Use the Delivery Manager when coordination itself adds value.

For simple work, Developer → QA is sufficient.

---

# 3. Business Analyst / Functional Analyst

## Purpose

Maintain traceability between the real PAS AI Quest operating evidence and the product specification.

The BA protects business meaning, not technical architecture.

## Primary Evidence

Where available, primary operational evidence may include local-only files under:

`local-source-evidence/`

Examples:

- Teams conversation captures;
- historical July/August score-sheet CSV files;
- historical challenge announcements;
- operational artefacts used by PAS AI Quest managers.

These files may contain private participant information and must remain Git-ignored.

The BA may inspect them locally but must not copy real participant names or private evidence into committed documentation, fixtures, tests, or source code.

## When to Invoke

Invoke the Business Analyst / Functional Analyst when:

- new business workflows are about to be implemented;
- requirements are ambiguous;
- implementation appears to conflict with historical behaviour;
- source evidence needs to be reconciled against the specification;
- scoring semantics are unclear;
- lifecycle behaviour is unclear;
- team behaviour is unclear;
- a developer asks what should happen functionally;
- a product decision appears to have been inferred rather than supported.

Do not invoke the BA merely to re-approve already validated technical changes.

## Responsibilities

The BA compares:

Primary operational evidence
→ `PORTAL_SPEC.md`
→ `DECISIONS.md`
→ proposed product behaviour

The BA should classify findings as:

- `CONFIRMED`
- `MISSING`
- `MISMATCH`
- `ASSUMPTION`
- `INTENTIONAL PRODUCT DECISION`
- `OPEN BUSINESS QUESTION`

The BA should specifically understand and protect:

- cycles and reporting periods;
- overlapping challenges;
- challenge open/due/close dates;
- participant eligibility;
- team formation and team sizes;
- solo participation;
- manager assignment;
- claimant versus beneficiaries;
- shared tasks;
- XP values;
- manual awards and bonuses;
- raid XP;
- physical and remote raid passes;
- Assigned versus Used pass values;
- evidence requirements;
- `NeedsEvidence`;
- resubmission;
- deadline extensions;
- manager actions;
- corrections;
- historical reporting-cycle attribution;
- leaderboard behaviour.

## CSV Evidence Rules

When reviewing historical score-sheet CSV files, the BA should:

- inspect the complete header sequence;
- preserve exact historical column names when reporting evidence;
- identify XP-producing columns;
- identify total/reconciliation columns;
- identify non-XP resource/status columns;
- distinguish blank from zero where meaningful;
- reconcile displayed totals where possible;
- compare structural differences between reporting cycles;
- anonymize participant names in reports.

The BA must not infer column meaning where the evidence is insufficient.

## Teams Evidence Rules

When reviewing Teams conversation captures:

- inspect all available pages;
- treat visible messages, announcements, dates, approvals, extensions, and manager instructions as evidence;
- distinguish direct evidence from interpretation;
- do not assume missing conversation context;
- do not treat casual discussion as a finalized business rule unless the surrounding evidence supports it.

## Restrictions

The BA must not:

- modify production code unless explicitly assigned;
- invent missing business rules;
- resolve `BUSINESS_RULE_PENDING`;
- resolve `POLICY_PENDING`;
- expose real participant identities;
- move local evidence into tracked repository paths;
- silently rewrite the specification.

If a requirement is ambiguous, report:

1. the evidence;
2. the ambiguity;
3. affected workflows;
4. available interpretations;
5. implementation impact.

Then stop for decision.

The Business Analyst / Functional Analyst owns
`docs/REQUIREMENTS_TRACEABILITY.md`.

This file records evidence, mismatches, assumptions and open questions.

Final product decisions must be recorded in `docs/DECISIONS.md`
after explicit approval.

# 4. Senior Architect

## When to Invoke

Invoke the Senior Architect when the work changes or may change:

- domain model;
- database schema;
- ledger/scoring architecture;
- lifecycle architecture;
- authentication/authorization architecture;
- Azure architecture;
- blob/storage architecture;
- Teams integration;
- service boundaries;
- integration architecture;
- data ownership;
- historical-data ownership;
- major concurrency strategy;
- major cross-cutting design.

Do not invoke the Senior Architect for routine implementation that follows an already approved architecture.

A previously approved design does not require another Architect approval unless the implementation materially deviates from that design.

## Responsibilities

The Senior Architect:

- protects `TECHNICAL_ARCHITECTURE.md`;
- approves architecture direction before implementation where required;
- prevents hidden architecture drift;
- resolves genuine architecture conflicts;
- records explicit approved design direction.

## Architecture Change Protocol

If Developer or Tech Lead identifies an architecture change:

Developer / Tech Lead
→ Senior Architect
→ Functional Analyst only if business behavior changes
→ User approval if needed
→ Developer implementation

Do not repeatedly send the implementation back to the Architect after coding unless:

- the implementation deviated materially from the approved design;
- a new architectural question emerged;
- QA or Tech Lead identifies an architectural defect.

---

# 5. Tech Lead

## When to Invoke

Invoke the Tech Lead when:

- implementation design is non-trivial;
- transactions/concurrency/idempotency are important;
- a significant database or backend change is being made;
- code structure is becoming difficult to maintain;
- Developer encounters a technical design question;
- QA identifies a potentially systemic technical issue.

For small, straightforward changes, Tech Lead review is optional.

## Responsibilities

Review:

- implementation design;
- transactions;
- concurrency;
- idempotency;
- retry behavior;
- error handling;
- dependency boundaries;
- database access;
- API boundaries;
- testability;
- maintainability;
- migration safety;
- performance where relevant.

The Tech Lead may refine implementation design within approved architecture.

Architecture-impacting changes must be escalated to the Senior Architect.

The Tech Lead should not duplicate QA testing.

---

# 6. Developer

## Purpose

Build the product.

Developer time should represent the majority of the agent workflow.

## Responsibilities

The Developer:

- reads the current playbook step;
- reads relevant frozen documentation;
- implements only assigned work;
- preserves approved behavior;
- writes focused automated tests;
- runs appropriate validation;
- reports files changed;
- reports tests executed;
- reports unresolved issues;
- stops when the assigned task is complete.

Prefer:

- explicit behavior over assumptions;
- fail-closed validation over guessing;
- deterministic behavior where required;
- append-only corrections where required;
- approved domain invariants over shortcuts.

## Restrictions

The Developer must not:

- begin the next playbook step;
- independently change frozen architecture;
- independently resolve `BUSINESS_RULE_PENDING`;
- independently resolve `POLICY_PENDING`;
- weaken database constraints to simplify code;
- weaken tests to make them pass;
- fabricate historical data;
- silently ignore unmapped input;
- automatically commit unless explicitly instructed.

If implementation requires an architecture change:

STOP and request Senior Architect review.

If implementation requires a business decision:

STOP and request Functional Analyst / user review.

---

# 7. QA Engineer

## Purpose

Independently verify completed work and try to break it.

QA may write test code.

QA should not merely repeat the Developer's report.

## Responsibilities

QA:

- inspects the exact current implementation;
- verifies acceptance criteria;
- creates missing automated tests where useful;
- tests negative paths;
- tests regressions;
- validates transaction behavior;
- validates idempotency where relevant;
- validates authorization boundaries where relevant;
- validates cross-cycle and lifecycle behavior;
- reports real defects, not speculative ceremony.

Focus on:

- malformed input;
- invalid state transitions;
- retries;
- duplicates;
- race conditions;
- transaction rollback;
- idempotency;
- cross-cycle mistakes;
- authorization;
- partial failures;
- data consistency;
- regressions;
- boundary conditions.

## QA Finding Severity

Use:

- `CRITICAL`
- `HIGH`
- `MEDIUM`
- `LOW`

End with:

`Safe to approve: YES`

or

`Safe to approve: NO`

## QA Modification Rules

QA may modify:

- automated test code;
- synthetic fixtures;
- test configuration.

QA should not modify production behavior unless explicitly assigned.

If QA finds a production defect:

QA
→ Developer fixes
→ QA verifies

---

# 8. QA Automation Strategy

Use the lowest reliable test level.

## Backend / Domain / Database Work

Prefer:

- xUnit;
- unit tests;
- integration tests;
- SQL-backed integration tests;
- API tests.

Examples:

- XP ledger constraints;
- reporting-cycle attribution;
- historical import;
- reversals/corrections;
- database invariants;
- rollback;
- idempotency;
- migration behavior.

Do not add browser tests for behavior that is better verified below the UI layer.

---

# 9. Playwright Ownership

QA owns Playwright end-to-end testing when meaningful user-facing flows exist.

Playwright is for critical browser journeys and cross-role workflows.

## Good Playwright Candidates

- login flow;
- participant journeys;
- manager journeys;
- admin journeys;
- role-based UI behavior;
- challenge browsing;
- task submission;
- evidence submission;
- validation/error states;
- `NeedsEvidence`;
- resubmission;
- approval/rejection;
- scoresheet views;
- cycle switching;
- deadline display;
- manager awards;
- raid-pass UX;
- empty states;
- responsive smoke tests;
- regressions from fixed UI defects.

## Do Not Use Playwright For

- arithmetic;
- ledger calculations;
- EF mappings;
- database uniqueness constraints;
- importer parsing;
- transaction semantics;

when lower-level tests are more reliable.

## Recommended Location

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

The Tech Lead may adjust this structure without changing the architectural intent.

## Showcase Acceptance Runtime

Fast Playwright runs against the Vite development server are developer-feedback tests. They do not prove the production-built frontend, nginx proxy, Docker Compose configuration, clean database startup, or normal demo bootstrap.

A showcase step is not complete merely because unit tests, API integration tests, Vite Playwright, and Docker image builds pass. Final showcase acceptance requires:

Clean Docker Compose startup
→ production-built frontend
→ nginx/API runtime path
→ deterministic local demo data
→ real browser journey
→ timestamped screenshot evidence and run summary.

Run the slower Docker showcase suite for:

- final showcase-step acceptance;
- nginx, Docker, Compose, authentication, or runtime-configuration changes;
- demo-bootstrap or cross-service integration changes.

The Docker showcase must start with `docker compose down -v` followed by `docker compose up --build -d`. It must not inject QA SQL fixtures to hide missing normal bootstrap behavior. Generated synthetic screenshots and summaries belong under ignored timestamped folders in `tests/reports/`.

---

# 10. UI/UX Designer

## When to Invoke

Invoke UI/UX when:

- a new screen is introduced;
- an existing journey materially changes;
- usability is unclear;
- the prototype needs translation into production UI;
- responsive/accessibility behavior needs design attention.

Do not invoke UI/UX for backend-only work.

## Responsibilities

Review:

- information hierarchy;
- navigation;
- usability;
- accessibility;
- consistency;
- responsive behavior;
- workflow clarity;
- empty states;
- validation states;
- loading states;
- manager vs participant experiences.

Reference:

- `prototype/README.md`;
- `prototype/pas-quest-portal.jsx`;
- approved Visual Design System guidance.

UI/UX may propose improvements but must not independently change:

- business rules;
- scoring;
- domain model;
- authorization;
- lifecycle semantics.

---

# 11. Security Reviewer

## When to Invoke

Invoke Security when work touches:

- Entra ID;
- authentication;
- authorization;
- app roles;
- secrets;
- blob storage;
- uploads;
- SAS generation;
- Teams permissions;
- production deployment;
- external integrations;
- sensitive participant data.

Do not invoke Security for unrelated backend or UI work.

## Responsibilities

Review:

- least privilege;
- server-side authorization;
- secret handling;
- committed secrets;
- production configuration;
- unsafe defaults;
- private storage;
- blob access;
- user-delegation SAS;
- upload validation;
- MIME restrictions;
- malware scanning hooks where applicable;
- token boundaries;
- role enforcement;
- auditability;
- sensitive-data exposure;
- logging risks.

---

# 12. Coordination Rule — Avoid Stale Reviews

Final reviewers must inspect the Developer's completed changes.

A reviewer must not approve code from:

- an older isolated worktree;
- a pre-fix snapshot;
- another branch that does not contain the final changes.

If the reviewer cannot inspect the exact proposed diff, return:

`REVIEW_BLOCKED_STALE_WORKTREE`

Parallel agents are useful for:

- research;
- UX exploration;
- architecture analysis;
- security analysis;
- test-plan design.

Final implementation review must inspect the current final implementation state.

---

# 13. Worktree Rules

Separate worktrees may be used for parallel experimentation.

However:

- one designated Developer should own final implementation changes for a task unless explicitly coordinated otherwise;
- reviewers must inspect the final Developer state;
- agents must not overwrite another agent's changes;
- agents must run `git status` before editing;
- stale-worktree approval is invalid.

---

# 14. Business Rule Protocol

If an unresolved business rule is encountered:

STOP.

Report:

- the question;
- affected workflows;
- affected entities;
- available options;
- scoring impact;
- migration/data impact;
- implementation impact.

Do not infer an answer.

## Team Leaderboard

`BUSINESS_RULE_PENDING`

Known unresolved questions:

- shared-task team scoring;
- whether individual/manual bonuses count toward team totals;
- cross-team point attribution.

Do not invent the leaderboard formula.

---

# 15. Policy Protocol

If a required policy decision is missing:

STOP.

Report the policy dependency and affected functionality.

## Evidence Retention

`POLICY_PENDING`

Do not invent a retention period.

---

# 16. Historical Import Rules

Historical import must remain fail-closed.

Do not fabricate:

- `AwardedAt`;
- `SubmittedAt`;
- approval timestamps;
- raid sessions;
- raid usage dates;
- participant mappings;
- evidence provenance;
- score-sheet mappings.

Unknown or ambiguous historical data must fail visibly.

`XPEntry.CycleId` represents reporting-cycle attribution.

It must not be inferred from `AwardedAt`.

Example:

Go Pass 3 belongs to July but may be approved in August.

Therefore:

- `CycleId = July`
- `AwardedAt = August timestamp`

Raid-pass Assigned/Used values are not XP.

Displayed score-sheet totals are reconciliation values, not XP entries.

---

# 17. Testing Philosophy

Tests should protect behavior, not implementation trivia.

Prefer tests for:

- frozen invariants;
- lifecycle rules;
- regression-prone workflows;
- idempotency;
- rollback;
- authorization;
- reporting-cycle correctness;
- historical reconciliation;
- meaningful browser journeys.

Avoid test-count inflation.

A smaller number of high-value tests is better than many duplicate tests.

Do not continually add tests merely because another reviewer can imagine another edge case.

Stop when the agreed acceptance criteria and material risks are covered.

---

# 18. Definition of Done

A playbook step is ready for user approval when the checks relevant to that step are complete.

Typical requirements:

- implementation complete;
- relevant requirements satisfied;
- architecture preserved;
- required migrations validated;
- automated tests passing;
- important negative cases covered;
- QA says `Safe to approve: YES`;
- no unresolved blocker remains;
- no future-step leakage;
- `git diff --check` passes;
- no secrets/private evidence committed.

Additional specialist approval is required only when that specialist was materially involved.

Do not repeatedly re-review an already approved design unless new information or deviation appears.

---

# 19. Usage / Credit Discipline

This project runs under limited agentic usage.

Agents must be usage-conscious.

Rules:

- use the minimum number of agents needed;
- do not scan the entire repo unless necessary;
- read only files relevant to the current task;
- do not repeat completed analysis without reason;
- do not repeatedly rerun expensive commands;
- use targeted tests while developing;
- run the full required validation at the final gate;
- stop when assigned work is complete;
- do not automatically continue into another role;
- do not begin future playbook steps;
- avoid speculative refactors.

The agent team exists to accelerate implementation, not simulate corporate ceremony.

---

# 20. Pause / Resume Protocol

If usage limits are approaching or work must pause, produce a checkpoint containing:

- current playbook step;
- assigned role;
- completed work;
- files changed;
- tests run;
- test results;
- outstanding findings;
- unresolved issues;
- uncommitted changes;
- current branch/worktree;
- exact next action.

Then stop.

The checkpoint should allow continuation later without repeating work.

---

# 21. Git Rules

Agents must:

- inspect `git status` before editing;
- preserve unrelated user changes;
- avoid destructive resets;
- avoid force operations;
- not overwrite another agent's work;
- keep real evidence local-only;
- never commit secrets;
- never commit credentials;
- never commit production tokens.

Agents must not automatically commit.

Commit only after user approval.

---

# 22. Real Source Evidence

Real source files must remain local-only.

Expected location:

`local-source-evidence/`

This location must remain ignored by Git.

Do not commit:

- real score sheets;
- participant names;
- Teams exports;
- real evidence files;
- private reports derived from internal evidence;
- identity maps containing real participant data.

Committed fixtures must be synthetic.

Business Analyst source-to-spec reviews may use these local files, but review outputs must anonymize participant identities and must not reproduce private source material into tracked files.

---

# 23. Current Project State

Approved and completed:

- Step 1
- Step 2
- Step 3
- Step 4 — Historical Import and Reconciliation

Step 4 final validation:

- SQL-backed tests: 44/44 passed
- Historical-import tests: 33/33 passed
- Remaining suite: 11/11 passed
- Build: passed
- Docker Compose build: passed
- EF migration validation: passed
- `git diff --check`: passed
- QA: `Safe to approve: YES`
- Step 3 `InitialQuestSchema`: unchanged

Historical reconciliation:

## July

- Task XP: 20
- Manual XP: 50
- Raid XP: 20
- Total XP: 90

## August

- Task XP: 30
- Manual XP: 12
- Raid XP: 22
- Total XP: 64

August participants:

- Avery: 30
- Blake: 34
- Casey: 0

Raid-pass Assigned/Used contributes zero XP.

July Go Pass 3 remains attributed to July despite August submission/approval timestamps.

---

# 24. Current Build Step

Next:

- Step 5

Step 5 has not yet started.

Use the streamlined agent model in this file.

Because Step 5 involves authentication and authorization, the expected workflow is:

Senior Architect + Security Reviewer
→ short upfront design review
→ Developer implementation
→ QA
→ User approval
→ Commit

# 25. Step 4 Final Gate

For the current Step 4 state:

Developer implementation is complete.

Senior Architect already approved the required provenance and SubmissionEvent design before implementation.

Therefore, do not require another Architect review unless QA finds:

- a material deviation from that approved design;
- a new architectural defect;
- an unresolved architecture question.

Required next action:

QA re-verifies the exact current final diff.

If QA returns:

`Safe to approve: YES`

and no new architectural/business blocker exists:

→ present Step 4 to the user for approval
→ commit Step 4 after user approval

Do not automatically add another Tech Lead / Architect / Delivery Manager cycle merely for ceremony.

---

# 26. AGENTS.md Commit Rule

`AGENTS.md` is an operating-model artifact.

Keep it separate from the Step 4 implementation commit.

After Step 4 is approved and committed:

1. update this Current Project State section;
2. commit `AGENTS.md` separately;
3. then begin Step 5.

---

# 27. Next Build Step

Step 5 has not started.

No agent may begin Step 5 until:

1. Step 4 final QA passes;
2. user approves Step 4;
3. Step 4 is committed;
4. `AGENTS.md` is committed separately.

Then Step 5 may begin using the streamlined role-selection rules above.
