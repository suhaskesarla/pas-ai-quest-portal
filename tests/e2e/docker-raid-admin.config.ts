import { defineConfig, devices } from '@playwright/test'
import path from 'node:path'

const runId = process.env.QA_RUN_ID ?? new Date().toISOString().replace(/[:.]/g, '-')
const runRoot = process.env.QA_RUN_ROOT ?? path.resolve(__dirname, '..', 'reports', runId)
process.env.QA_RUN_ID = runId
process.env.QA_RUN_ROOT = runRoot
process.env.QA_SCREENSHOT_DIR = path.join(runRoot, 'docker-raid-admin')
process.env.QA_TEST_MODE = 'Preserved Docker Raid Administration'
process.env.QA_BASE_URL = 'http://localhost:5173'
process.env.QA_CLEAN_DATABASE = 'false'
process.env.QA_FIXTURE_DATA = 'false'

export default defineConfig({
  testDir: '.',
  testMatch: 'raid-admin/docker-raid-admin.spec.ts',
  timeout: 180_000,
  workers: 1,
  fullyParallel: false,
  reporter: [['list'], ['./evidence-reporter.ts']],
  outputDir: path.join(runRoot, 'docker-raid-admin', 'artifacts'),
  use: {
    baseURL: 'http://localhost:5173',
    timezoneId: 'Australia/Sydney',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    ...devices['Desktop Chrome'],
  },
  projects: [{ name: 'docker-chromium-sydney', use: {} }],
})
