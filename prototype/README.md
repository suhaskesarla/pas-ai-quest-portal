# Prototype — UX/visual reference only

`pas-quest-portal.jsx` is a working, fully-clickable single-file React prototype demonstrating the intended UX and visual direction for the portal. **It is not the data model** — [`../docs/PORTAL_SPEC.md`](../docs/PORTAL_SPEC.md) is the source of truth for that, and this prototype predates several of the spec's corrections.

## What's real vs. simulated here

This prototype correctly demonstrates:
- The visual design system (colors, typography, component patterns — see spec §13)
- The general shape of cycle-scoping, submission review, and bonus awards
- Claimant vs. beneficiaries in the submission flow
- `Needs Evidence` / `Resubmitted` states
- A challenge remaining open past its calendar-month cycle boundary
- `BUSINESS_RULE_PENDING` treatment on the Team Leaderboard
- A full cycle roster including zero-XP participants

It does **not** implement:
- A real append-only `XPEntry` ledger (uses in-memory mock arrays instead)
- `CycleParticipant` as a real enrollment record
- Relational `CycleTeamMember` / `ChallengeParticipationMember` (uses simple arrays)
- Any backend, database, authentication, or file storage — everything resets on page reload
- Real Teams integration (the "Teams announcement preview" is a static mockup)

See [`PORTAL_SPEC.md`](../docs/PORTAL_SPEC.md) §20 for the full, authoritative list.

## Running it locally

This is a bare React component, not a standalone app — it needs a host to render into. Quick setup with Vite:

```bash
npm create vite@latest quest-preview -- --template react
cd quest-preview
npm install lucide-react recharts
# Copy pas-quest-portal.jsx into src/, then in src/App.jsx:
#   import PASQuestPortal from './pas-quest-portal.jsx'
#   export default function App() { return <PASQuestPortal /> }
npm run dev
```

## Using this during the real build

Per [`BUILD_PLAYBOOK.md`](../docs/BUILD_PLAYBOOK.md) Step 6, this file is the visual/UX reference when rebuilding real screens against the actual backend — keep the design system and interaction patterns, rebuild the data layer against the real API.
