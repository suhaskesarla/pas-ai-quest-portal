import { expect, test, type Locator, type Page } from '@playwright/test'

type RaidSession = {
  id: string
  cycleId: string
  name: string
  occurredAt: string
  rowVersion: string
  hasReferences: boolean
}
type PassBalance = { assigned: number; used: number; remaining: number; entitlementRowVersion: string | null }
type RaidParticipant = {
  participantId: string
  displayName: string
  physical: PassBalance
  remote: PassBalance
  participation: { passType: string; usedAt: string } | null
}

async function switchIdentity(page: Page, profile: 'participant' | 'manager') {
  await page.getByRole('combobox', { name: 'Demo identity' }).selectOption(profile)
  await expect(page.getByLabel('Active identity')).toContainText(profile === 'manager' ? 'Manager' : 'Participant')
}

async function openRaidAdmin(page: Page) {
  await page.getByRole('navigation', { name: 'Primary navigation' }).getByRole('button', { name: 'Raid Administration' }).click()
  await expect(page.getByRole('heading', { name: 'Raid Administration', level: 2 })).toBeVisible()
}

async function selectRaidSession(page: Page, name: string) {
  await page.getByRole('button', { name: new RegExp(name) }).click()
  await expect(page.getByRole('heading', { name, level: 3 })).toBeVisible()
}

function participantCard(page: Page, name: string) {
  return page.getByRole('heading', { name, level: 4 }).locator('xpath=ancestor::article[1]')
}

async function expectBalance(region: Locator, assigned: number, used: number, remaining: number) {
  await expect(region.getByText('Assigned', { exact: true }).locator('..')).toContainText(String(assigned))
  await expect(region.getByText('Used', { exact: true }).locator('..')).toContainText(String(used))
  await expect(region.getByText('Remaining', { exact: true }).locator('..')).toContainText(String(remaining))
}

async function getRaidParticipants(page: Page, raidId: string) {
  const response = await page.request.get(`/api/manager/raids/${raidId}/participants`)
  expect(response.status()).toBe(200)
  return response.json() as Promise<{ raid: RaidSession; participants: RaidParticipant[] }>
}

async function getScoresheetRow(page: Page, cycleId: string, participantId: string) {
  const response = await page.request.get(`/api/manager/scoresheet?cycleId=${cycleId}`)
  expect(response.status()).toBe(200)
  const body = await response.json() as { rows: Array<{ participantId: string; totalXp: number; bySource: { raidXp: number } }> }
  const row = body.rows.find(item => item.participantId === participantId)
  expect(row, 'isolated cycle must contain the enrolled demo participant').toBeTruthy()
  return row!
}

test('manager administers Raid sessions, passes, participation and XP through Docker', async ({ page }) => {
  const suffix = `${Date.now()}-${crypto.randomUUID().slice(0, 6)}`
  const cycleCode = `RAID-${suffix}`.slice(0, 50)
  const cycleName = `QA Raid Cycle ${suffix}`
  const raidName = `QA Raid ${suffix}`
  const editedRaidName = `${raidName} Edited`
  const xpReason = `QA Raid XP ${suffix}`

  await page.goto('/?page=raid-administration')
  await switchIdentity(page, 'participant')
  const me = await page.request.get('/api/auth/me')
  expect(me.status()).toBe(200)
  const participantIdentity = await me.json() as { participantId: string; displayName: string }
  await expect(page.getByRole('navigation', { name: 'Primary navigation' }).getByRole('button', { name: 'Raid Administration' })).toHaveCount(0)
  await expect(page.getByRole('heading', { name: 'Raid Administration' })).toHaveCount(0)
  expect((await page.request.get('/api/manager/raids/cycles')).status()).toBe(403)

  await switchIdentity(page, 'manager')
  const now = new Date()
  const createdCycleResponse = await page.request.post('/api/manager/cycles', { data: {
    code: cycleCode,
    name: cycleName,
    startsAt: new Date(now.getTime() - 86_400_000).toISOString(),
    endsAt: new Date(now.getTime() + 30 * 86_400_000).toISOString(),
  } })
  expect(createdCycleResponse.status()).toBe(200)
  const createdCycle = await createdCycleResponse.json() as { id: string; version: string }
  const enrollment = await page.request.post(`/api/manager/cycles/${createdCycle.id}/participants`, { data: {
    participantId: participantIdentity.participantId,
    reason: 'Focused Raid Administration browser acceptance enrollment',
  } })
  expect(enrollment.status()).toBe(200)

  const raidCycles = await page.request.get('/api/manager/raids/cycles')
  expect(raidCycles.status()).toBe(200)
  const cycles = await raidCycles.json() as { defaultCycleId: string | null; cycles: Array<{ id: string; name: string }> }
  const delayedCycle = cycles.cycles.find(cycle => cycle.id === cycles.defaultCycleId)
  expect(delayedCycle, 'Raid Administration must return a resolvable default cycle').toBeTruthy()
  const cycleAfterSwitch = delayedCycle!.id === createdCycle.id
    ? cycles.cycles.find(cycle => cycle.id !== delayedCycle!.id)
    : cycles.cycles.find(cycle => cycle.id === createdCycle.id)
  expect(cycleAfterSwitch, 'preserved stack must expose a second explicit cycle for stale-response coverage').toBeTruthy()

  let releaseCreatedSessions!: () => void
  const createdSessionsHeld = new Promise<void>(resolve => { releaseCreatedSessions = resolve })
  let markCreatedSessionsEntered!: () => void
  const createdSessionsEntered = new Promise<void>(resolve => { markCreatedSessionsEntered = resolve })
  let markCreatedSessionsComplete!: () => void
  const createdSessionsComplete = new Promise<void>(resolve => { markCreatedSessionsComplete = resolve })
  const createdSessionsPattern = `**/api/manager/raids?cycleId=${delayedCycle!.id}`
  await page.route(createdSessionsPattern, async route => {
    markCreatedSessionsEntered()
    await createdSessionsHeld
    await route.continue()
    markCreatedSessionsComplete()
  })
  const createdSessionsResponse = page.waitForResponse(response => response.url().endsWith(`/api/manager/raids?cycleId=${delayedCycle!.id}`))
  await openRaidAdmin(page)
  await createdSessionsEntered
  const cycleSelector = page.getByLabel('Reporting cycle')
  await expect(cycleSelector).toHaveValue(delayedCycle!.id)
  const otherSessionsResponse = page.waitForResponse(response => response.url().endsWith(`/api/manager/raids?cycleId=${cycleAfterSwitch!.id}`))
  await cycleSelector.selectOption(cycleAfterSwitch!.id)
  expect((await otherSessionsResponse).status()).toBe(200)
  await expect(cycleSelector).toHaveValue(cycleAfterSwitch!.id)
  await expect(page.getByRole('heading', { name: cycleAfterSwitch!.name, level: 3 })).toBeVisible()
  releaseCreatedSessions()
  await Promise.all([createdSessionsComplete, createdSessionsResponse])
  await page.unroute(createdSessionsPattern)
  await expect(cycleSelector).toHaveValue(cycleAfterSwitch!.id)
  await expect(page.getByRole('heading', { name: cycleAfterSwitch!.name, level: 3 })).toBeVisible()

  if (cycleAfterSwitch!.id !== createdCycle.id) {
    const isolatedSessionsResponse = page.waitForResponse(response => response.url().endsWith(`/api/manager/raids?cycleId=${createdCycle.id}`))
    await cycleSelector.selectOption(createdCycle.id)
    expect((await isolatedSessionsResponse).status()).toBe(200)
  }
  await expect(cycleSelector).toHaveValue(createdCycle.id)
  await expect(page.getByText('No Raid Sessions exist for this cycle.')).toBeVisible()

  const createTrigger = page.getByRole('button', { name: 'Create Raid Session' })
  await createTrigger.click()
  let dialog = page.getByRole('dialog', { name: 'Create Raid Session' })
  await expect(dialog).toBeVisible()
  expect(await dialog.evaluate(element => {
    const active = document.activeElement
    return active instanceof HTMLElement && element.contains(active) && !active.hasAttribute('disabled')
  }), 'focus must move to an enabled control inside the named dialog').toBe(true)
  await page.keyboard.press('Shift+Tab')
  expect(await dialog.evaluate(element => element.contains(document.activeElement))).toBe(true)
  await page.keyboard.press('Escape')
  await expect(dialog).toBeHidden()
  await expect(createTrigger).toBeFocused()

  await createTrigger.click()
  dialog = page.getByRole('dialog', { name: 'Create Raid Session' })
  const occurredAtInput = '2026-08-30T19:15'
  const expectedCreatedInstant = await page.evaluate(value => new Date(value).toISOString(), occurredAtInput)
  await dialog.getByLabel('Name').fill(raidName)
  await dialog.getByLabel('Occurred At').fill(occurredAtInput)
  const createRaidResponse = page.waitForResponse(response => response.url().endsWith('/api/manager/raids') && response.request().method() === 'POST')
  await dialog.getByRole('button', { name: 'Save Raid Session' }).click()
  const createdRaidResponse = await createRaidResponse
  expect(createdRaidResponse.status()).toBe(200)
  let raid = await createdRaidResponse.json() as RaidSession
  expect(raid).toMatchObject({ cycleId: createdCycle.id, name: raidName, hasReferences: false })
  expect(Date.parse(raid.occurredAt)).toBe(Date.parse(expectedCreatedInstant))
  const expectedLocalDisplay = await page.evaluate(value => new Date(value).toLocaleString(), raid.occurredAt)
  await expect(page.getByRole('button', { name: new RegExp(raidName) })).toContainText(expectedLocalDisplay)

  const preciseInstant = new Date(Date.parse(raid.occurredAt) + 123).toISOString()
  const precisionUpdate = await page.request.put(`/api/manager/raids/${raid.id}`, { data: {
    rowVersion: raid.rowVersion,
    name: raid.name,
    occurredAt: preciseInstant,
  } })
  expect(precisionUpdate.status()).toBe(200)
  raid = await precisionUpdate.json() as RaidSession
  await page.reload()
  await openRaidAdmin(page)
  await cycleSelector.selectOption(createdCycle.id)
  await selectRaidSession(page, raidName)
  await page.getByRole('button', { name: 'Edit Raid Session' }).click()
  dialog = page.getByRole('dialog', { name: 'Edit Raid Session' })
  await dialog.getByLabel('Name').fill(editedRaidName)
  await dialog.getByRole('button', { name: 'Save Raid Session' }).click()
  await expect(page.getByRole('heading', { name: editedRaidName, level: 3 })).toBeVisible()
  const authoritativeSession = await page.request.get(`/api/manager/raids/${raid.id}`)
  expect(authoritativeSession.status()).toBe(200)
  raid = await authoritativeSession.json() as RaidSession
  expect(raid.name).toBe(editedRaidName)
  expect(Date.parse(raid.occurredAt)).toBe(Date.parse(preciseInstant))

  const beforePassXp = await getScoresheetRow(page, createdCycle.id, participantIdentity.participantId)
  expect(beforePassXp.totalXp).toBe(0)
  let projection = await getRaidParticipants(page, raid.id)
  let target = projection.participants.find(person => person.participantId === participantIdentity.participantId)!
  expect(target).toBeTruthy()
  let card = participantCard(page, participantIdentity.displayName)
  let physical = card.getByRole('region', { name: 'Physical pass' })
  await physical.getByRole('button', { name: 'Update Physical assignment' }).click()
  dialog = page.getByRole('dialog', { name: 'Update Physical assignment' })
  await dialog.getByLabel('Assigned').fill('2')
  await dialog.getByRole('button', { name: 'Save assignment' }).click()
  await expectBalance(physical, 2, 0, 2)

  projection = await getRaidParticipants(page, raid.id)
  target = projection.participants.find(person => person.participantId === participantIdentity.participantId)!
  await physical.getByRole('button', { name: 'Update Physical assignment' }).click()
  const externalEntitlement = await page.request.put(`/api/manager/raids/cycles/${createdCycle.id}/participants/${participantIdentity.participantId}/entitlements/Physical`, { data: {
    assignedCount: 3,
    rowVersion: target.physical.entitlementRowVersion,
  } })
  expect(externalEntitlement.status()).toBe(200)
  dialog = page.getByRole('dialog', { name: 'Update Physical assignment' })
  await dialog.getByLabel('Assigned').fill('4')
  await dialog.getByRole('button', { name: 'Save assignment' }).click()
  await expect(dialog).toBeHidden()
  await expect(page.getByRole('alert')).toContainText('This pass assignment changed')
  card = participantCard(page, participantIdentity.displayName)
  physical = card.getByRole('region', { name: 'Physical pass' })
  await expectBalance(physical, 3, 0, 3)

  const remote = card.getByRole('region', { name: 'Remote pass' })
  await remote.getByRole('button', { name: 'Update Remote assignment' }).click()
  dialog = page.getByRole('dialog', { name: 'Update Remote assignment' })
  await dialog.getByLabel('Assigned').fill('2')
  await dialog.getByRole('button', { name: 'Save assignment' }).click()
  await expectBalance(remote, 2, 0, 2)
  expect((await getScoresheetRow(page, createdCycle.id, participantIdentity.participantId)).totalXp).toBe(0)

  await card.getByRole('button', { name: 'Record Raid Participation' }).click()
  dialog = page.getByRole('dialog', { name: 'Record Raid Participation' })
  await dialog.getByLabel('Pass type').selectOption('Physical')
  await dialog.getByRole('button', { name: 'Confirm participation' }).click()
  await expect(page.getByText('Raid participation recorded. Pass usage refreshed; no XP was awarded.')).toBeVisible()
  card = participantCard(page, participantIdentity.displayName)
  physical = card.getByRole('region', { name: 'Physical pass' })
  await expectBalance(physical, 3, 1, 2)
  await expect(card).toContainText('Participation: Physical')
  expect((await getScoresheetRow(page, createdCycle.id, participantIdentity.participantId)).totalXp).toBe(0)
  await expect(page.getByRole('button', { name: 'Edit Raid Session' })).toHaveCount(0)
  const immutable = await page.request.get(`/api/manager/raids/${raid.id}`)
  expect(immutable.status()).toBe(200)
  expect(await immutable.json()).toMatchObject({ hasReferences: true, allowedActions: { canEdit: false } })

  projection = await getRaidParticipants(page, raid.id)
  target = projection.participants.find(person => person.participantId === participantIdentity.participantId)!
  const passesBeforeXp = { physical: target.physical, remote: target.remote }
  await card.getByRole('button', { name: 'Award Raid XP' }).click()
  dialog = page.getByRole('dialog', { name: 'Award Raid XP' })
  await dialog.getByLabel('XP amount').fill('7')
  await dialog.getByLabel('Reason').fill(xpReason)
  await dialog.getByRole('button', { name: 'Review Raid XP' }).click()
  await expect(dialog).toContainText('+7 XP')
  await expect(dialog).toContainText(xpReason)

  const awardRequests: Array<{ requestId: string; participantId: string; amount: number; reason: string }> = []
  let awardAttempt = 0
  const awardPattern = `**/api/manager/raids/${raid.id}/xp-awards`
  await page.route(awardPattern, async route => {
    awardRequests.push(route.request().postDataJSON())
    awardAttempt += 1
    if (awardAttempt === 1) {
      const committed = await route.fetch()
      expect(committed.status()).toBe(200)
      await route.fulfill({
        status: 503,
        contentType: 'application/problem+json',
        body: JSON.stringify({ code: 'RaidAdministrationDependencyUnavailable', detail: 'Synthetic ambiguous response after commit.' }),
      })
      return
    }
    await route.continue()
  })
  await dialog.getByRole('button', { name: 'Confirm Raid XP' }).click()
  await expect(dialog.getByRole('alert')).toContainText('Synthetic ambiguous response after commit.')
  await expect(dialog).toContainText('This attempted award is frozen')
  await dialog.getByRole('button', { name: 'Close' }).click()
  await expect(dialog).toBeHidden()
  await card.getByRole('button', { name: 'Award Raid XP' }).click()
  dialog = page.getByRole('dialog', { name: 'Award Raid XP' })
  await expect(dialog).toContainText('This attempted award is frozen')
  await expect(dialog.getByLabel('XP amount')).toHaveCount(0)
  const retryResponse = page.waitForResponse(response => response.url().endsWith(`/api/manager/raids/${raid.id}/xp-awards`) && response.status() === 200)
  await dialog.getByRole('button', { name: 'Retry same award' }).click()
  await retryResponse
  await expect(page.getByText('Raid XP recorded. Updated XP is available in Scoresheet and participant reporting.')).toBeVisible()
  await page.unroute(awardPattern)
  expect(awardRequests).toHaveLength(2)
  expect(awardRequests[1]).toEqual(awardRequests[0])
  expect(awardRequests[0]).toMatchObject({ participantId: participantIdentity.participantId, amount: 7, reason: xpReason })

  projection = await getRaidParticipants(page, raid.id)
  target = projection.participants.find(person => person.participantId === participantIdentity.participantId)!
  expect(target.physical).toEqual(passesBeforeXp.physical)
  expect(target.remote).toEqual(passesBeforeXp.remote)
  const scored = await getScoresheetRow(page, createdCycle.id, participantIdentity.participantId)
  expect(scored.bySource.raidXp).toBe(7)
  expect(scored.totalXp).toBe(7)

  await page.getByRole('button', { name: 'Scoresheet' }).click()
  const scoresheetCycle = page.getByLabel('Scoresheet reporting cycle')
  await scoresheetCycle.selectOption(createdCycle.id)
  const scoresheetRow = page.getByRole('table').getByRole('row').filter({ hasText: participantIdentity.displayName })
  await expect(scoresheetRow.getByRole('cell').nth(4)).toHaveText('7')
  await expect(scoresheetRow.getByRole('cell').nth(6)).toHaveText('7')

  await switchIdentity(page, 'participant')
  const reportingCycle = page.getByLabel('Reporting cycle')
  await reportingCycle.selectOption(createdCycle.id)
  await expect(page.getByText('Total XP', { exact: true }).locator('..')).toContainText('7')
  await page.getByRole('button', { name: 'XP Activity' }).click()
  await expect(page.getByText('+7 XP')).toBeVisible()
  await expect(page.getByText(editedRaidName)).toBeVisible()
  await expect(page.getByText(xpReason)).toBeVisible()
  await expect(page.getByText(/Raid · Grant/)).toBeVisible()

  await switchIdentity(page, 'manager')
  let cycleDetailResponse = await page.request.get(`/api/manager/cycles/${createdCycle.id}`)
  expect(cycleDetailResponse.status()).toBe(200)
  let cycleDetail = await cycleDetailResponse.json() as { version: string }
  const closing = await page.request.post(`/api/manager/cycles/${createdCycle.id}/start-closing`, { data: { version: cycleDetail.version, reason: 'QA Raid history closing transition' } })
  expect(closing.status()).toBe(200)
  cycleDetail = await closing.json() as { version: string }
  const finalised = await page.request.post(`/api/manager/cycles/${createdCycle.id}/finalise`, { data: { version: cycleDetail.version, reason: 'QA Raid history finalisation' } })
  expect(finalised.status()).toBe(200)

  await openRaidAdmin(page)
  await page.getByLabel('Reporting cycle').selectOption(createdCycle.id)
  await selectRaidSession(page, editedRaidName)
  const raidMain = page.getByRole('main')
  for (const actionName of ['Create Raid Session', 'Edit Raid Session', 'Update Physical assignment', 'Update Remote assignment', 'Record Raid Participation', 'Award Raid XP'])
    await expect(raidMain.getByRole('button', { name: actionName, exact: true })).toHaveCount(0)
  card = participantCard(page, participantIdentity.displayName)
  await expect(card.getByText('Read-only', { exact: true })).toBeVisible()
  await expect(card).toContainText('Participation: Physical')
  await expectBalance(card.getByRole('region', { name: 'Physical pass' }), 3, 1, 2)

  await page.reload()
  await openRaidAdmin(page)
  await page.getByLabel('Reporting cycle').selectOption(createdCycle.id)
  await selectRaidSession(page, editedRaidName)
  card = participantCard(page, participantIdentity.displayName)
  await expect(card).toContainText('Participation: Physical')
  await expectBalance(card.getByRole('region', { name: 'Physical pass' }), 3, 1, 2)
  const persisted = await getScoresheetRow(page, createdCycle.id, participantIdentity.participantId)
  expect(persisted.bySource.raidXp).toBe(7)
  expect(persisted.totalXp).toBe(7)
})
