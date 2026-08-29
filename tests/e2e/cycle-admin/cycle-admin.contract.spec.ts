import { expect, test, type Page } from '@playwright/test'

async function manager(page: Page) {
  await page.goto('/')
  await page.getByRole('combobox', { name: 'Demo identity' }).selectOption('manager')
}

test.describe.serial('BA-015 frozen HTTP contract', () => {
  test('manager creates, edits and transitions a cycle with row versions and reasons', async ({ page }) => {
    await manager(page)
    const suffix = crypto.randomUUID().slice(0, 8).toUpperCase()
    const startsAt = '2027-01-01T00:00:00Z'; const endsAt = '2027-02-01T00:00:00Z'
    const createdResponse = await page.request.post('/api/manager/cycles', { data: { code: `E2E-${suffix}`, name: `E2E Cycle ${suffix}`, startsAt, endsAt } })
    expect(createdResponse.status()).toBe(200)
    const created = await createdResponse.json() as { id: string; version: string; status: string; allowedActions: { canEdit: boolean; canStartClosing: boolean } }
    expect(created.status).toBe('Active'); expect(created.version).toBeTruthy(); expect(created.allowedActions.canEdit).toBe(true)
    const editedResponse = await page.request.put(`/api/manager/cycles/${created.id}`, { data: { version: created.version, code: `E2E-${suffix}`, name: `Edited E2E Cycle ${suffix}`, startsAt, endsAt } })
    expect(editedResponse.status()).toBe(200)
    const edited = await editedResponse.json() as { version: string }
    const stale = await page.request.put(`/api/manager/cycles/${created.id}`, { data: { version: created.version, code: `E2E-${suffix}`, name: 'Stale', startsAt, endsAt } })
    expect(stale.status()).toBe(409); expect((await stale.json()).code).toBe('CycleVersionConflict')
    const closingResponse = await page.request.post(`/api/manager/cycles/${created.id}/start-closing`, { data: { version: edited.version, reason: 'E2E lifecycle coverage' } })
    expect(closingResponse.status()).toBe(200)
    const closing = await closingResponse.json() as { version: string; status: string; allowedActions: { canFinalise: boolean; canAddParticipant: boolean } }
    expect(closing.status).toBe('Closing'); expect(closing.allowedActions).toMatchObject({ canFinalise: true, canAddParticipant: false })
    const finalResponse = await page.request.post(`/api/manager/cycles/${created.id}/finalise`, { data: { version: closing.version, reason: 'E2E finalisation coverage' } })
    expect(finalResponse.status()).toBe(200); expect((await finalResponse.json()).status).toBe('Finalised')
  })

  test('strict dates and participant authorization fail closed', async ({ page }) => {
    await manager(page)
    const invalid = await page.request.post('/api/manager/cycles', { data: { code: `BAD-${crypto.randomUUID()}`, name: 'Bad dates', startsAt: '2027-01-01T00:00:00Z', endsAt: '2027-01-01T00:00:00Z' } })
    expect(invalid.status()).toBe(400); expect((await invalid.json()).code).toBe('CycleValidationFailed')
    await page.getByRole('combobox', { name: 'Demo identity' }).selectOption('participant')
    expect((await page.request.get('/api/manager/cycles')).status()).toBe(403)
  })

  test.fixme('enrollment and every status transition append the complete event sequence', async () => {
    // Requires a Test-only durable active non-enrolled Participant option. SQL tests currently own
    // event shapes, JoinedAt/LeftAt, no-op, freeze, append-only update/delete constraints and restart.
  })
})
