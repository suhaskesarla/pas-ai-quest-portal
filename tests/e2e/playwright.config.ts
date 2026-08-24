import { defineConfig, devices } from '@playwright/test'

export default defineConfig({
  testDir: '.',
  testMatch: 'auth-smoke.spec.ts',
  fullyParallel: false,
  workers: 1,
  reporter: 'list',
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
