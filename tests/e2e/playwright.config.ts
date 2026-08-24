import { defineConfig, devices } from '@playwright/test'
import path from 'node:path'

const runId = process.env.QA_RUN_ID ?? new Date().toISOString().replace(/[:.]/g, '-')
const runRoot = path.resolve(__dirname, '..', 'reports', runId)
process.env.QA_RUN_ID = runId
process.env.QA_RUN_ROOT = runRoot
process.env.QA_SCREENSHOT_DIR = path.join(runRoot, 'vite')
process.env.QA_TEST_MODE = 'Vite'
process.env.QA_BASE_URL = 'http://127.0.0.1:5173'
process.env.QA_CLEAN_DATABASE = 'false'
process.env.QA_FIXTURE_DATA = 'true'

export default defineConfig({
  testDir: '.',
  testMatch: ['auth-smoke.spec.ts', 'step6-workflow.spec.ts'],
  fullyParallel: false,
  workers: 1,
  reporter: [['list'], ['./evidence-reporter.ts']],
  outputDir: path.join(runRoot, 'vite', 'artifacts'),
  use: {
    baseURL: 'http://127.0.0.1:5173',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: {
    command: 'npm run dev --prefix ../../src/web -- --host 0.0.0.0',
    url: 'http://127.0.0.1:5173',
    reuseExistingServer: true,
    env: {
      VITE_DEMO_AUTH_ENABLED: 'true',
      VITE_API_PROXY_TARGET: 'http://127.0.0.1:8080',
    },
  },
})
