import { defineConfig, devices } from '@playwright/test'
import path from 'node:path'

const runId = process.env.QA_RUN_ID ?? new Date().toISOString().replace(/[:.]/g, '-')
const runRoot = process.env.QA_RUN_ROOT ?? path.resolve(__dirname, '..', 'reports', runId)
process.env.QA_RUN_ID = runId
process.env.QA_RUN_ROOT = runRoot
process.env.QA_SCREENSHOT_DIR = path.join(runRoot, 'structured-regression')
process.env.QA_TEST_MODE = 'Preserved Docker structured regression'
process.env.QA_BASE_URL = 'http://localhost:5173'
process.env.QA_CLEAN_DATABASE ??= 'false'
process.env.QA_FIXTURE_DATA ??= 'false'

export default defineConfig({
  testDir: '.',
  testMatch: [
    'security/**/*.spec.ts',
    'reporting/**/*.spec.ts',
    'cycle-admin/**/*.spec.ts',
    'concurrency/**/*.spec.ts',
    'persistence/**/*.spec.ts',
    'regression/**/*.spec.ts',
  ],
  timeout: 90_000,
  fullyParallel: false,
  workers: 1,
  reporter: [['list'], ['./evidence-reporter.ts']],
  outputDir: path.join(runRoot, 'structured-regression', 'artifacts'),
  use: {
    baseURL: 'http://localhost:5173',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    ...devices['Desktop Chrome'],
  },
})

// This config never starts Vite or Docker. It expects an already-running production web/nginx/API
// stack at localhost:5173. test.fixme scenarios remain discovered but skipped until their stated
// Cycle Admin UI, deterministic barrier, identity catalog, or restart orchestrator prerequisite exists.
