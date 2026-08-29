import { expect, test, type APIRequestContext, type Page } from '@playwright/test'

const unknownId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'
const managerMutations = [
  { method: 'POST', path: `/api/submissions/${unknownId}/review`, data: { action: 'Approve', version: 'AQAAAA==' } },
  { method: 'POST', path: `/api/manager/xp/${unknownId}/corrections`, data: { newAmount: 0, reason: 'Authorization boundary' } },
  { method: 'POST', path: '/api/manager/challenges', data: {} },
  { method: 'PUT', path: `/api/manager/challenges/${unknownId}`, data: {} },
  { method: 'POST', path: `/api/manager/challenges/${unknownId}/publish`, data: {} },
  { method: 'POST', path: '/api/manager/cycles', data: {} },
  { method: 'PUT', path: `/api/manager/cycles/${unknownId}`, data: {} },
  { method: 'POST', path: `/api/manager/cycles/${unknownId}/start-closing`, data: {} },
  { method: 'POST', path: `/api/manager/cycles/${unknownId}/finalise`, data: {} },
  { method: 'POST', path: `/api/manager/cycles/${unknownId}/participants`, data: {} },
  { method: 'POST', path: `/api/manager/cycles/${unknownId}/participants/${unknownId}/status`, data: {} },
] as const

async function send(api: APIRequestContext, operation: typeof managerMutations[number]) {
  return api.fetch(operation.path, { method: operation.method, data: operation.data })
}

async function selectDemo(page: Page, profile: 'manager' | 'participant') {
  await page.goto('/')
  await page.getByRole('combobox', { name: 'Demo identity' }).selectOption(profile)
  const response = await page.request.get('/api/auth/me')
  expect(response.status()).toBe(200)
  expect((await response.json()).isAuthenticated).toBe(true)
}

test.describe('server-side role boundaries', () => {
  test('anonymous requests receive 401 across protected endpoint families', async ({ request }) => {
    for (const path of [
      '/api/challenges/eligible', '/api/submissions/mine', '/api/participant/reporting-cycles',
      '/api/submissions/review-queue', '/api/manager/reporting-cycles', '/api/manager/challenges',
      '/api/manager/manual-awards/options?cycleId=00000000-0000-0000-0000-000000000001', '/api/manager/cycles',
      `/api/submission-evidence/${unknownId}/content`,
    ]) expect((await request.get(path)).status(), path).toBe(401)
    for (const operation of managerMutations) expect((await send(request, operation)).status(), operation.path).toBe(401)
  })

  test('participant session cannot invoke manager APIs even by direct URL', async ({ page }) => {
    await selectDemo(page, 'participant')
    for (const path of ['/api/submissions/review-queue', '/api/manager/reporting-cycles', '/api/manager/challenges', '/api/manager/cycles']) {
      expect((await page.request.get(path)).status(), path).toBe(403)
    }
    expect((await page.request.post('/api/manager/manual-awards', { data: {} })).status()).toBe(403)
    for (const operation of managerMutations) expect((await send(page.request, operation)).status(), operation.path).toBe(403)
  })

  test('authorized manager reaches review, correction, challenge and cycle command handlers', async ({ page }) => {
    await selectDemo(page, 'manager')
    expect((await page.request.get('/api/submissions/review-queue')).status()).toBe(200)
    for (const operation of managerMutations) {
      const status = (await send(page.request, operation)).status()
      expect(status, `${operation.method} ${operation.path}`).not.toBe(401)
      expect(status, `${operation.method} ${operation.path}`).not.toBe(403)
    }
  })

  test('manager role does not implicitly grant participant APIs', async ({ page }) => {
    await selectDemo(page, 'manager')
    for (const path of ['/api/challenges/eligible', '/api/submissions/mine', '/api/participant/reporting-cycles']) {
      expect((await page.request.get(path)).status(), path).toBe(403)
    }
  })

  test('role switch replaces navigation and removes stale privileged content', async ({ page }) => {
    await selectDemo(page, 'manager')
    await page.getByRole('button', { name: 'Scoresheet' }).click()
    await expect(page.getByRole('heading', { name: 'Scoresheet summary' })).toBeVisible()
    await page.getByRole('combobox', { name: 'Demo identity' }).selectOption('participant')
    await expect(page.getByRole('heading', { name: 'Welcome, Synthetic Participant' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Scoresheet' })).toHaveCount(0)
    await expect(page.getByRole('heading', { name: 'Scoresheet summary' })).toHaveCount(0)
    await expect(page.getByText('DEVELOPMENT · DEMO AUTH ACTIVE')).toBeVisible()
  })
})

test.describe('evidence concealment contract', () => {
  test('authenticated manager and participant reach evidence authorization logic for unknown IDs', async ({ page }) => {
    await selectDemo(page, 'participant')
    expect((await page.request.get(`/api/submission-evidence/${unknownId}/content`)).status()).toBe(404)
    await page.getByRole('combobox', { name: 'Demo identity' }).selectOption('manager')
    expect((await page.request.get(`/api/submission-evidence/${unknownId}/content`)).status()).toBe(404)
  })

  test.fixme('beneficiary who is not claimant and unrelated participant both receive concealed 404', async () => {
    // Requires separate server-issued Participant Alpha/Beta/Gamma sessions and an attachment
    // owned by Alpha. Use the HTTP contract helper; never inject ParticipantId client-side.
  })
})

// Claimant and Manager access to a real private attachment, plus anonymous 401 and secure headers,
// are executable in docker-step7.spec.ts. The non-claimant/unrelated distinction needs additional
// server-issued synthetic participant sessions before it can become executable here.
