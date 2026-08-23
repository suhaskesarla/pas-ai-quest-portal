# PAS AI Quest Portal

An internal web portal replacing the current Teams-chat-and-Excel workflow for the PAS AI Quest program — a gamified, monthly AI-learning initiative. Participants complete AI-themed challenges, submit evidence, earn XP, and compete individually and as teams. This repo holds the frozen specification, the architecture guide, the build playbook, and a UX-reference prototype — not yet the production application.

## Status

**Requirements are frozen.** Four rounds of independent review checked the spec against the actual program's Teams chat export and score-sheet CSVs before it was locked. The only deliberately open item is the Team Leaderboard scoring formula — see [`docs/DECISIONS.md`](docs/DECISIONS.md).

Implementation has not started yet. The next step is handing [`docs/BUILD_PLAYBOOK.md`](docs/BUILD_PLAYBOOK.md) to a coding agent (Codex) and working through it step by step.

## Where to look

| File | Purpose |
|---|---|
| [`docs/PORTAL_SPEC.md`](docs/PORTAL_SPEC.md) | **Start here.** The frozen functional and data-model specification — every entity, rule, and evidenced correction. This is the source of truth for *what* to build. |
| [`docs/TECHNICAL_ARCHITECTURE.md`](docs/TECHNICAL_ARCHITECTURE.md) | Implementation-facing companion — how the spec's rules map onto Azure SQL, Blob Storage, Entra ID, and the rest of the stack. |
| [`docs/DECISIONS.md`](docs/DECISIONS.md) | Running log of anything resolved after the freeze. Check here before treating any spec-marked-open question as still unanswered. |
| [`docs/BUILD_PLAYBOOK.md`](docs/BUILD_PLAYBOOK.md) | Step-by-step instructions for directing a coding agent through the build, with git-checkpoint discipline. |
| [`prototype/pas-quest-portal.jsx`](prototype/pas-quest-portal.jsx) | A working, clickable UX/visual-reference prototype (mock data, no real backend). See its own README for what's real vs. simulated. |

## Why this exists

The current program runs through a Teams channel plus a manually-maintained Excel score sheet, both of which have become the bottleneck as participation has grown — lost submissions, manual scoring with no audit trail, and recurring team-formation confusion every single cycle. This portal makes the app the system of record for challenges, submissions, teams, and scoring, while keeping Teams as the social/discussion layer rather than the tracking mechanism.

## A note on source material

The raw evidence this spec was built from (a Teams chat export and real score-sheet CSVs, both containing colleagues' names and internal conversations) is **deliberately not stored in this repo**, even though it's private. If you need to reference it, it's kept locally outside version control.
