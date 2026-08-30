# PAS AI Quest Delivery Status

> This file is updated at the completion/start of each major feature or delivery gate.

This is the authoritative delivery-status dashboard. It records implementation and delivery evidence; it does not replace [`DECISIONS.md`](./DECISIONS.md), [`REQUIREMENTS_TRACEABILITY.md`](./REQUIREMENTS_TRACEABILITY.md), the frozen product specification, or architecture decisions.

The Delivery Manager updates this file after:

- BA scope freeze;
- architecture approval;
- backend completion;
- frontend completion;
- QA acceptance;
- a defer/rescope decision; or
- a demo release.

Last audited: **2026-08-30**

Audited branch: **`feature/raid-administration` at `a1aa496`**

Working tree at update start: **clean except for this delivery-dashboard update**.

## Status Rules

Only these feature statuses are used:

- `DONE` — business-to-browser capability is complete for its stated local/demo scope and has implementation plus relevant automated evidence.
- `IN_PROGRESS` — active implementation or acceptance work is incomplete.
- `BLOCKED` — work cannot proceed without an external decision/dependency.
- `NOT_STARTED` — planned work has no meaningful implementation.
- `DEFERRED` — explicitly removed from current delivery scope.
- `BUSINESS_RULE_PENDING` — product behavior must not be invented before approval.
- `IMPLEMENTATION_GAP` — required/target behavior is documented, but the executable product contract is absent or incomplete.

`DONE` does not mean production-ready unless the row explicitly says so. Most completed capabilities currently use Development/Test demo authentication and local Docker services.

## Delivery Snapshot

| Status | Count |
|---|---:|
| `DONE` | 23 |
| `IN_PROGRESS` | 2 |
| `BLOCKED` | 1 |
| `NOT_STARTED` | 5 |
| `DEFERRED` | 4 |
| `BUSINESS_RULE_PENDING` | 1 |
| `IMPLEMENTATION_GAP` | 3 |
| **Total capabilities** | **39** |

## Master Feature Inventory

| Feature / Capability | Business decision | Architecture | Current status | Backend | Frontend | Automated tests | Browser QA | Demo readiness | Needed for next demo? | Production requirement? | Remaining work | Blocker / owner |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Development/Test demo authentication | Step 5A approved; synthetic identities only | Cookie/session seam and server roles implemented | `DONE` | Demo profiles, same-origin session endpoints, policies and fail-closed startup | Demo identity selector and unauthenticated/error states | Authentication/API/component tests | Auth smoke and multiple focused Docker journeys | Ready locally | Yes | No; must not be enabled in production | Retain isolation while Step 5B is added | None |
| Real Entra authentication and authorization (Step 5B) | Required by spec; deferred from local demo | Frozen direction: Entra app roles, MSAL, API token validation | `NOT_STARTED` | Entra mode intentionally fails startup; no JWT validation | No MSAL/login redirect | Only tests proving Entra is not implemented and demo mode is isolated | None | Not ready | No | Yes | Tenant configuration, JWT bearer validation, identity resolution and role enforcement | Security/Architect plus tenant/app-registration access |
| Production login experience | Required with Step 5B | Depends on Entra/MSAL design | `NOT_STARTED` | No login initiation/callback contract | Generic “not signed in” state only; no MSAL login page | Demo auth tests only | None | Not ready | No | Yes | Implement sign-in/sign-out/error/consent UX with MSAL | Step 5B / Frontend + Security |
| Deterministic local demo bootstrap | Approved normal demo path; no QA SQL injection | Application seeder over migrated SQL/Azurite | `DONE` | Idempotent Development seeder creates synthetic cycle/users/challenges/tasks/reporting data | Consumed through normal APIs | Seeder behavior covered indirectly/lower-level | Clean Docker focused suites use normal bootstrap | Ready | Yes | No | Keep seed compatible with new features | None |
| Participant challenge discovery | BA-005/006/011 resolved | Existing workflow/read model | `DONE` | Eligible challenge/task/participation API | Challenges and Submit Work navigation | Workflow/API/component tests | Step 6 and clean Docker showcase paths | Ready | Yes | Yes | None for current scope | None |
| Manager Challenge Administration: create/edit/publish | BA-011 resolved | Rowversion concurrency and relational task/policy model implemented | `DONE` | Options, list/detail, create, Draft update, publish | Manager challenge list/editor/publish UI | SQL/API and component tests | Clean Docker manager-challenges passed | Ready | Yes | Yes | None for approved initial contract | None |
| Challenge close/archive lifecycle | Frozen target mentions Closed/Archived; BA-011 initial contract intentionally stops at publish | No endpoint/state-transition contract implemented | `IMPLEMENTATION_GAP` | Enum/target language exists; no close/archive command | No controls | No executable tests; E2E state model marks target gap | None | Not part of current demo | No | Post-demo product requirement if retained | Approve exact lifecycle contract, then implement end to end | Product Owner/BA; Tech Lead if selected |
| Submission creation and beneficiary selection | BA-001/002/005/006 resolved | Transactional relational submission model | `DONE` | Individual/whole-team/selected-beneficiary creation with eligibility checks | Task selection, beneficiary UI and submission confirmation | SQL/API/component tests | Step 6 and Docker showcase executed | Ready | Yes | Yes | None for current scope | None |
| Structured text/link evidence | Evidence decisions approved | Relational evidence metadata | `DONE` | Validation and persistence | Text/link/multiple evidence controls and display | Evidence/workflow tests | Submission Docker journeys | Ready | Yes | Yes | `Custom` evidence remains deferred | None |
| Local private attachment evidence | Step 7 rules approved; retention remains separate | Private Blob/Azurite store, authorized retrieval, validation seam | `DONE` | Multipart upload, validation, immutable metadata, claimant/manager access | Upload, resubmission and authorized evidence link | Attachment, HTTP and component tests | Clean Docker Step 7 passed | Ready locally | Nice to have | Yes | Production scanner and cloud identity hardening are separate | None for local demo |
| `NeedsEvidence` and resubmission | BA-001 and evidence rules resolved | Audited submission-event lifecycle | `DONE` | Manager request and claimant resubmission; no XP before approval | Feedback, history, replacement/appended evidence and resubmit UI | Workflow/API/component tests | Step 6/Docker paths executed | Ready | Yes | Yes | None | None |
| Approval/rejection and atomic Task XP | BA-001/009 resolved | Transactional one-grant-per-beneficiary ledger design | `DONE` | Shared review queue, approve/reject, atomic/idempotent grants | Manager review actions and confirmations | SQL/API/component tests including rollback/idempotency | Step 6/Docker showcase executed | Ready | Yes | Yes | None | None |
| Append-only XP ledger and reporting attribution | Frozen core invariant | Implemented schema, constraints and append-only enforcement | `DONE` | Task, ManualAward, Raid, Reversal and Correction projections | Exposed through reporting/scoresheet/activity | Database, import, workflow and reporting suites | Exercised by multiple Docker journeys | Ready | Yes | Yes | None | None |
| Post-approval TaskApproval correction | BA-013 resolved; ManualAward/Raid correction excluded | Append-only direct adjustment to original grant | `DONE` | Manager correction command and audit event | Scoresheet participant drill-down correction dialog | SQL/API/component tests | Clean Docker scoresheet/correction passed | Ready | Yes | Yes | None for BA-013 scope | None |
| Manual XP Award | BA-014 resolved | Request-ID idempotent ManualAward grant | `DONE` | Options and create command | Scoresheet Award XP dialog/confirmation | SQL/API/component/concurrency tests | Clean and preserved Docker manual-award passed | Ready | Yes | Yes | Category administration is not part of this workflow | None |
| Manager Scoresheet and participant drill-down | BA-012 resolved | Ledger-based reporting read model | `DONE` | Cycle summary, all statuses/zero rows, paged detail | Summary, source totals, detail, correction/manual-award entry points | SQL/API/component tests | Clean Docker scoresheet passed | Ready | Yes | Yes | No export/rank/team XP by decision | None |
| Manager dashboard and navigation | Demo priority completed | Existing app shell | `DONE` | Uses review/reporting/admin APIs | Focused supported navigation, dashboard cards and Raid destination | App/component tests | Clean Docker manager-navigation and focused Raid navigation passed | Ready | Yes | Yes | None for current demo scope | None |
| Participant Dashboard | Participant reporting decisions resolved | Cycle-scoped ledger/read models | `DONE` | Total, rank, status, recent activity and raid-pass balances | Cycle selector/dashboard cards | SQL/API/component tests | Clean Docker reporting/manual-award paths passed | Ready | Yes | Yes | None for current scope | None |
| Participant XP Activity | Reporting rules resolved | Cursor-paged append-only ledger projection | `DONE` | Friendly source/provenance and signed entries | Paged activity view | SQL/API/component tests | Reporting and manual-award Docker paths | Ready | Yes | Yes | Full unified canonical regression remains pending | None |
| Individual leaderboard/ranking | BA-004 participant reporting decision resolved | Active roster, zero rows and competition ranking | `DONE` | Cycle-scoped individual ranking | Participant leaderboard view | SQL/API/component tests | Reporting/manual-award Docker coverage; isolated tie browser fixture remains fixme | Ready for seeded demo | Nice to have | Yes | Optional isolated 1,2,2,4 browser fixture | QA, post-demo |
| My Cycle Team and Challenge Group information | BA-004 resolved for participant presentation | Separate cycle-team and participation snapshots | `DONE` | Participant team read model | My Team view | SQL/API/component tests | Included in participant reporting focused coverage | Ready | Nice to have | Yes | No team score calculation | None |
| Team formation/manager team administration | Evidence supports variable groups; full operational management contract not implemented | Schema supports teams/participations | `IMPLEMENTATION_GAP` | No normal manager team-formation administration API | Read-only participant information only | Domain constraints only | None | Not demo-ready | No | Expected post-demo if retained | Define bounded workflow without touching team scoring | Product Owner + BA |
| Team leaderboard and team scoring | BA-003 unresolved | Schema can support later attribution | `BUSINESS_RULE_PENDING` | No calculation | Deliberately absent | No resolved-behavior tests | None | Must not be demonstrated as working | No | Only after business decision | Decide aggregation, bonus treatment and cross-team attribution | Product Owner/BA |
| Cycle Administration | BA-015 and audit amendment resolved | Rowversion plus append-only participant-event migration approved/implemented | `DONE` | Create/edit, Active→Closing→Finalised, enrollment/status commands | Full manager Cycle Administration UI | SQL/API/component/concurrency tests | Preserved production-Docker cycle-admin passed on 2026-08-29 | Ready, but optional in story | Nice to have | Yes | Update stale E2E checkpoint/TODO catalog during final regression maintenance | QA documentation maintenance |
| Participant deadline-override administration UI | BA-006 behavior resolved; initial challenge UI did not include it | Existing deadline override model available | `IMPLEMENTATION_GAP` | Eligibility consumes overrides; no complete manager administration workflow identified | No manager override UI | Lower-level eligibility tests | No focused manager browser path | Not ready | No | Post-demo if required operationally | Define and implement audited manager command/UI | Product Owner/BA then developers |
| Raid participant read models | BA-016 preserves current display; passes are non-XP | Existing reporting projections | `DONE` | Dashboard balances and Raid XP activity/reporting | Dashboard/XP Activity/Scoresheet/leaderboard display | Import/reporting tests | Focused reporting Docker coverage | Ready as read-only evidence | Nice to have | Yes | None | None |
| Raid Administration MVP | BA-016 resolved | Approved implementation uses rowversions, strengthened participant/session uniqueness, append-only participation and reviewed concurrency locking | `DONE` | Complete migration, session/pass/participation/Raid XP endpoints and final backend gate PASS | Complete manager navigation and Raid Administration UI: session create/edit, Physical/Remote passes, participation, Raid XP and Finalised read-only behavior | Backend suites plus deterministic controlled-overlap concurrency tests 11/11 PASS; frontend tests cover stale responses, timestamp round-trip and retry intent | Focused Raid Administration Playwright 1/1 PASS; no fixmes or product defects | Demo-ready | Yes | Optional product capability; not production prerequisite | None for current demo scope | None |
| Historical import and reconciliation | Step 4 approved | Fail-closed provenance/import-control design | `DONE` | Import command, immutable provenance, reconciliation, rerun/conflict/rollback | Operator CLI/report only; no portal UI required | Extensive SQL-backed import tests | Browser not applicable | Ready as completed foundation | No | Data migration requirement | BA-007 blocks only unsupported Go Pass 3 expansion | None for approved synthetic/canonical scope |
| Focused browser/Docker feature acceptance | Per-feature acceptance approach approved | Vite and production Docker/nginx/API/SQL/Azurite tiers | `DONE` | Runtime paths exercised | Implemented feature journeys exercised | Feature tests exist | Passing evidence exists for auth/workflow, attachments, reporting, challenges, scoresheet/correction, manual award, manager navigation, cycle admin and Raid Administration | Ready at focused-feature level | Yes | Yes | Preserve truthful evidence identity in canonical execution | QA |
| Canonical full-demo regression | Designed in E2E checkpoint; not accepted/executed | Clean Docker orchestration design exists | `IN_PROGRESS` | Required APIs mostly exist | Required core UI exists | Full-demo spec remains `test.fixme`; broad suite includes many fixmes | Final coherent clean-Docker journey has not run | Not ready as a release gate | Yes | Yes | Refresh stale E2E design for Cycle/Raid state, peer review, enable and execute one canonical journey | QA |
| Durable restart/persistence orchestration | Designed only | Compose persistence strategy documented | `NOT_STARTED` | Seeder/migrations are idempotent at lower layers | Not applicable | Restart specs are all `fixme` | Never executed | Not required for immediate demo | No | Yes | Build serial down/up-without-volume-removal acceptance | QA/DevOps |
| Production evidence/blob security | Step 7 architecture approved; production scanner required | Managed identity/user-delegation/private blob direction implemented in part | `IN_PROGRESS` | Azure path and validation exist; production scanner is deliberately disabled | Attachment UX exists | Security/config tests exist | Local Azurite Docker only | Local-ready, not production-ready | No | Yes | Integrate real malware scanner, Azure identity/storage configuration and security validation | Security + Backend/DevOps |
| Evidence retention/deletion | BA-010 policy unresolved | Configurable/no destructive default is frozen | `BLOCKED` | No automatic deletion, correctly | No retention controls | No assumed-policy tests | None | Safely excluded | No | Yes before enabling retention/purge | Obtain approved retention and records-management policy | Product Owner/BA/Security |
| Teams Notification MVP | BA-017 business rules **DONE**: seven outbound events, audiences, content/privacy, deduplication, failure, freshness and deep-link intent are approved | Architecture **DONE**; implementation has not started | `DEFERRED` | **NOT_STARTED:** no delivery, destination configuration, identity mapping or leaderboard-post command | **NOT_STARTED:** no Teams UI | None | None | **NOT_READY:** tenant/app registration, destinations and durable private-recipient identity mapping are external dependencies | No; remains outside canonical immediate demo | Production tenant/configuration required if later released | Keep deferred; implementation and tenant/configuration remain outstanding | Delivery prioritisation; tenant/admin dependency if reactivated |
| Advanced analytics | Listed in screen inventory but explicitly deferred from local demo | No dedicated read-model/API design | `DEFERRED` | No analytics endpoint | No Analytics navigation/screen | None | None | Not ready | No | Optional product scope | Product Owner must define bounded value before implementation | Product Owner |
| Advanced Raid capabilities | BA-016 explicitly excludes teams, correction, delete/restore, reversals, QR, bulk, analytics and integrations | Not designed for current MVP | `DEFERRED` | None beyond MVP | None | None | None | Excluded | No | No unless later selected | Re-scope individually after MVP feedback | Product Owner |
| Score-dispute workflow | BA-008 open and deliberately outside current workflow | Not designed | `DEFERRED` | None | None | None | None | Excluded | No | Optional | Decide whether disputes stay external | Product Owner/BA |
| CI/CD quality pipeline | Step 8 planned | GitHub Actions/Bicep direction frozen | `NOT_STARTED` | No repository workflow directory | Not applicable | Local commands only | No pipeline execution | Not required locally | No | Yes | Add lint/typecheck/test/build/migration/security gates | DevOps/Backend/Frontend |
| Azure deployment and operational readiness | Step 8 planned | Azure SWA/App Service/SQL/Blob/App Insights/Bicep frozen | `NOT_STARTED` | No IaC/deployment configuration found | No hosted environment | None | None | Not ready | No | Yes | Bicep, non-prod environment, secrets/managed identity, observability, deployment/UAT | DevOps + Security |

## Scope Buckets

### 1. Next Demo Must Have

- Deterministic clean Docker bootstrap through production-built frontend → nginx → API → SQL/Azurite.
- Demo authentication with participant and manager identities.
- Manager Challenge Administration create/edit/publish.
- Participant challenge discovery and submission.
- `NeedsEvidence` → resubmission → approval.
- Atomic Task XP visible in Participant Dashboard, XP Activity and Manager Scoresheet.
- Manual XP Award visible across the same reporting surfaces.
- Post-approval TaskApproval correction visible in the ledger and totals.
- Raid Administration continuity: select a cycle/session, demonstrate one pass operation and one Raid XP award, then verify reporting integration.
- One enabled, executed canonical clean-Docker browser journey with timestamped screenshots and summary.

### 2. Next Demo Nice to Have

- Cycle Administration as a short setup/close-out segment.
- My Team and Individual Leaderboard.
- Attachment evidence; use only if demo environment remains stable.
- Additional Raid Administration detail beyond the one concise integrated-demo segment; focused Raid QA already proves its edge cases.

### 3. Post-Demo Required

- Final regression execution beyond the single canonical demo journey.
- Durable restart/persistence acceptance.
- Participant deadline-override manager workflow if retained as an operational requirement.
- Team formation/manager team administration if retained.
- Real Entra Step 5B.
- Production evidence scanner/storage hardening.
- CI/CD, Azure deployment, observability and security readiness.
- Challenge close/archive only after bounded product scope is confirmed.

### 4. Deferred / Future

- Teams integration.
- Team leaderboard until BA-003 is resolved.
- Evidence purge/retention until BA-010 is resolved.
- Advanced analytics.
- Advanced Raid capabilities beyond BA-016 MVP.
- Score-dispute workflow, Wall of Fame/gallery and other unapproved discussion items.
- Expanded July Go Pass 3 import without authoritative BA-007 source evidence.

## Special Feature Gates

### Raid Administration

- **BA complete:** Yes, BA-016 is resolved for the current MVP.
- **Architecture/backend gate:** Complete. Migration preflight, rowversions, append-only participation, strengthened participant/session uniqueness and concurrency locking passed final review.
- **Tech Lead/concurrency gate:** Complete. Deterministic controlled-overlap tests passed 11/11; final GateHook review passed with no production behavior change.
- **Frontend:** Complete, including navigation, session/pass/participation/Raid XP workflows, Finalised read-only state, stale-response protection, timestamp round-trip and idempotent retry UX.
- **QA:** `RAID ADMIN BROWSER QA: PASS`; focused Playwright 1/1 passed with no fixmes or product defects.
- **Exact gate:** None for current demo scope. Advanced Raid features remain separately deferred.

### Teams Integration

| Item | Audit result |
|---|---|
| Approved product scope | BA-017 business rules are complete for the bounded outbound Teams Notification MVP. This has not promoted Teams into the canonical immediate demo. |
| BA notification rules | `DONE`: seven events, audiences, minimum/private content, duplicate/failure/freshness semantics and logical deep links are approved. |
| Architecture | `DONE`: approved Teams architecture review. No implementation is implied by this gate. |
| Teams manifest | Not present. |
| Teams tab | Not present. |
| Teams authentication | Not present. |
| Notification delivery | Not present. |
| Adaptive Cards | Not present. |
| Deep links | Not present. |
| Tenant/configuration | `NOT_READY`: real tenant/app registration, configured destinations and durable `(tenantId, oid)` private-recipient mapping remain external dependencies. |
| Tests | None; implementation is `NOT_STARTED`. |

Conclusion: BA-017 business rules and architecture are **DONE**. Implementation is **NOT_STARTED**, tenant/configuration is **NOT_READY**, and Teams remains **deferred from the canonical immediate demo**.

### Entra Authentication

| Item | Audit result |
|---|---|
| Login page | Generic unauthenticated state only; no Entra sign-in flow. |
| MSAL frontend | Not present. |
| JWT validation backend | Not present; `Authentication:Mode=Entra` intentionally fails startup. |
| Tenant configuration model | Not present for Entra. |
| App registration configuration | Not present. |
| Durable `tenantId + oid` mapping | Not present. The current participant model is not a complete multi-tenant Entra identity mapping. |
| App-role mapping | Role names/policies exist for demo auth, but no Entra token app-role mapping exists. |
| Development/Test demo-auth isolation | Implemented and tested fail-closed. |
| Tests | Strong Step 5A/demo tests; no real Entra integration tests. |

Conclusion: Step 5A is complete; real Entra Step 5B is **not started** and production-only for the current demo plan.

### E2E and Regression

- **Focused browser tests executed:** Yes. Passing evidence exists for Step 6 workflow, clean Docker showcase, private attachments, participant reporting, Challenge Administration, Manager Scoresheet/correction, Manual XP Award, manager navigation, Cycle Administration and Raid Administration.
- **Broad regression design:** Extensive and Tech Lead-reviewed, but the checkpoint explicitly says execution deferred and QA peer review incomplete.
- **Fixme scenarios:** Numerous concurrency, persistence, ranking/history, unified-ledger and canonical-demo scenarios remain `test.fixme`; they are not coverage.
- **Full clean-Docker demo:** Older focused clean-Docker suites ran successfully, but the current canonical all-feature `full-quest-demo.spec.ts` remains `test.fixme` and has not run.
- **Final integrated E2E:** Not completed against the current feature set including Raid Administration.
- **Exact gate:** update the canonical spec to include one concise Raid continuity segment, then execute once from `docker compose down -v` with production frontend/nginx and normal bootstrap. Focused suites already own Raid and Cycle edge cases.

## Current Delivery Risks

### HIGH

1. **The canonical integrated demo has not executed.** Focused suites, including Raid Administration, passed independently, but the current release story is still a `fixme` design.
2. **Delivery documents are stale and contradictory.** `AGENTS.md`, `CURRENT_STATE.md`, and the E2E checkpoint contain older build-step or feature-state claims. This dashboard governs delivery status until those documents are deliberately reconciled.

### MEDIUM

1. **Real Entra and tenant/app registration are completely outside the tested runtime.** This is acceptable for the local demo but remains a substantial production dependency.
2. **Production attachments cannot be enabled safely yet.** The production malware scanner is intentionally absent/disabled.
3. **Persistence restart automation is designed but not executable.** Seeder/migration behavior is tested below the full Compose restart level.
4. **Teams readiness depends on external configuration.** Business rules and architecture are complete, but implementation has not started and tenant/app registration, destinations and durable private-recipient mapping are not ready. It must not enter the immediate demo critical path.

### LOW

1. Individual leaderboard has strong lower-level coverage, but isolated competition-ranking browser coverage remains a fixme.
2. Challenge close/archive and deadline-override administration are visible product gaps but are outside the immediate demo story.

## Recommended Next Demo Story

1. Start from a clean Docker Compose environment using normal deterministic bootstrap.
2. Sign in as the demo Manager and show the focused Manager Dashboard.
3. Create or edit a Draft challenge and publish it.
4. Switch to the demo Participant, discover the challenge and submit evidence for the seeded group/task.
5. Switch to Manager, request more evidence.
6. Switch to Participant, update evidence and resubmit.
7. Switch to Manager, approve the shared submission atomically.
8. Open Scoresheet and show Task XP plus participant ledger provenance.
9. Award categorized Manual XP and show the updated total.
10. Correct the TaskApproval XP and demonstrate the immutable original plus signed adjustment.
11. Open Raid Administration, select the seeded cycle/session, perform one representative pass action and award Raid XP.
12. Return to Scoresheet or Participant Dashboard/XP Activity to prove the Raid XP is integrated while pass Assigned/Used remains non-XP.
13. Optionally show Cycle Administration, Individual Leaderboard or My Team. Do not include Teams or Entra.

## Demo Cut Line

| In-progress next-demo candidate | Remove from demo scope no later than | Demo that remains without it |
|---|---|---|
| Canonical full-demo regression | It cannot be cut as an acceptance gate. If it does not pass by release-candidate freeze, do not call the build demo-complete | Individual focused feature demos remain available, but there is no supported unified demo release |
| Production evidence/blob security | Keep out of local-demo scope now; use proven Azurite attachment path or structured text/link evidence | Complete local workflow still works; production deployment remains blocked |

## Decisions Needed

| Decision | Owner | Timing / impact |
|---|---|---|
| Team leaderboard scoring (BA-003) | Product Owner / BA | Post-demo; blocks only team score calculation |
| Evidence retention/deletion (BA-010) | Product Owner / BA with Security input | Before production retention/purge behavior |
| Decide whether challenge close/archive, deadline overrides and manager team administration remain committed product scope | Product Owner | Post-demo prioritisation; not needed for current story |

## Delivery Recommendation

### A. Exact next-demo must-have scope

- Clean deterministic Docker startup.
- Demo Manager and Participant authentication.
- Challenge create/edit/publish and participant discovery.
- Submit → Needs Evidence → resubmit → approve.
- Scoresheet and participant reporting update from atomic Task XP.
- Manual XP Award.
- TaskApproval correction with append-only provenance.
- A concise Raid Administration segment proving a pass operation, Raid XP and reporting continuity; focused Raid QA owns the edge cases.
- One enabled, passing canonical clean-Docker Playwright journey with screenshots and summary.

### B. Exact next-demo nice-to-have scope

- Cycle Administration.
- Individual Leaderboard and My Team as short reporting views.
- Attachment evidence.
- Additional Raid Administration detail beyond the concise integrated segment.

### C. Defer immediately

- Teams integration.
- Real Entra from the local demo.
- Advanced analytics.
- Advanced Raid capabilities.
- Team leaderboard.
- Evidence retention/purge.
- Challenge close/archive, deadline-override administration and manager team administration unless explicitly reprioritized.

### D. Correct implementation sequence from now

1. Reconcile the canonical E2E design with completed Cycle Admin and Raid Administration without duplicating their focused suites.
2. Enable and run the bounded canonical clean-Docker demo journey.
3. Fix only defects that break integration/demo continuity.
4. Declare the demo release and capture evidence.
5. After demo, decide whether to activate BA-017 Teams implementation and obtain tenant/configuration readiness.
6. Begin the production track: Entra, production blob/scanner, CI/CD and Azure deployment.

### E. Teams and Entra in the next demo

- **Teams: DEFER.** No implementation exists.
- **Entra: DEFER.** Step 5B is not started; demo authentication is already fit for the local story.

### F. Work that should stop

- Do not expand Raid beyond BA-016 MVP.
- Do not start Teams, Entra, analytics, team scoring, evidence purge or advanced Raid work before the demo release.
- Do not add new feature-development work before canonical E2E. Raid and Cycle focused suites have already proven their detailed behavior.

## Discussed but Not Formally Approved for Delivery

- Score-dispute portal workflow.
- Wall of Fame/gallery/offline artifact workflow.
- Teams implementation details and tenant configuration beyond the approved BA-017 business/architecture baseline.
- Team leaderboard formula.
- Evidence retention/purge periods.
- Advanced Raid operations beyond BA-016.
- Challenge close/archive delivery timing.
- Full manager team-formation administration scope.

These items must remain outside implementation estimates and demo commitments until the appropriate decision is recorded in `DECISIONS.md` or `REQUIREMENTS_TRACEABILITY.md`.
