import { defineConfig, devices } from '@playwright/test'
import path from 'node:path'

const runId = process.env.QA_RUN_ID ?? new Date().toISOString().replace(/[:.]/g, '-')
const runRoot = process.env.QA_RUN_ROOT ?? path.resolve(__dirname, '..', 'reports', runId)
process.env.QA_RUN_ID = runId
process.env.QA_RUN_ROOT = runRoot
process.env.QA_SCREENSHOT_DIR = path.join(runRoot, 'docker-showcase')
process.env.QA_TEST_MODE = 'Docker Compose'
process.env.QA_BASE_URL = 'http://localhost:5173'
process.env.QA_CLEAN_DATABASE = 'true'
process.env.QA_FIXTURE_DATA = 'false'

export default defineConfig({
  testDir: '.',
  testMatch: 'docker-showcase.spec.ts',
  workers: 1,
  fullyParallel: false,
  reporter: [['list'], ['./evidence-reporter.ts']],
  outputDir: path.join(runRoot, 'docker-showcase', 'artifacts'),
  use: {
    baseURL: 'http://localhost:5173',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [{ name: 'docker-chromium', use: { ...devices['Desktop Chrome'] } }],
})
