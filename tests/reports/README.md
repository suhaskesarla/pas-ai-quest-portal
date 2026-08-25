# Local Playwright evidence

Playwright creates one timestamped folder per execution under `tests/reports/`.
Generated screenshots, traces, container-health snapshots and summaries are local QA evidence and are ignored by Git. Only this README is tracked.

- `npm test --prefix tests/e2e` runs the fast Vite feedback suite.
- `npm run test:docker-showcase --prefix tests/e2e` destroys Compose volumes, rebuilds the real stack, waits for readiness, and runs the final showcase through `http://localhost:5173` without loading an E2E SQL fixture.

Every run writes `summary.txt` with the commit SHA, mode, base URL, pass/fail counts, data-source flags, screenshot paths and final result.
