import { test } from '@playwright/test'

test.describe('serial Compose persistence acceptance', () => {
  test.fixme('down/up without volume removal preserves administered state and append-only history', async () => {
    // External serial orchestrator creates enrollment/status events, submission/evidence, TaskApproval,
    // correction and ManualAward; records IDs/timestamps; restarts API/Compose without -v; re-queries all.
  })
  test.fixme('development seeder fills missing deterministic records without resetting administered state', async () => {})
  test.fixme('clean down -v restores exactly one deterministic baseline', async () => {})
})

// PERSISTENCE_AUTOMATION_PENDING. Lower-level seeder and migration tests are partial evidence only.
// Never execute destructive Compose lifecycle operations from a parallel Playwright worker.
