import { expect, test, type Locator, type Page } from '@playwright/test'

type CycleDetail = {
  id: string
  version: string
  code: string
  name: string
  status: 'Active' | 'Closing' | 'Finalised'
  startsAt: string
  endsAt: string
  participants: Array<{
    participantId: string
    displayName: string
    status: 'Active' | 'Withdrawn' | 'Inactive'
    joinedAt: string | null
    leftAt: string | null
    version: string
  }>
}

async function selectDemoIdentity(page: Page, key: 'participant' | 'manager') {
  await page.getByRole('combobox', { name: 'Demo identity' }).selectOption(key)
  await expect(page.getByLabel('Active identity')).toContainText(key === 'manager' ? 'Manager' : 'Participant')
}

async function getCycle(page: Page, id: string) {
  const response = await page.request.get(`/api/manager/cycles/${id}`)
  expect(response.status()).toBe(200)
  return response.json() as Promise<CycleDetail>
}

async function openCycle(page: Page, name: string) {
  if (await page.getByRole('heading', { name: 'Cycle Administration', level: 2 }).count() === 0)
    await page.getByRole('navigation', { name: 'Primary navigation' }).getByRole('button', { name: 'Cycle Administration' }).click()
  await page.getByRole('button', { name: new RegExp(name) }).click()
  await expect(page.getByRole('heading', { name, level: 3 })).toBeVisible()
}

async function confirmAction(page: Page, title: RegExp, reason: string) {
  const dialog = page.getByRole('dialog', { name: title })
  await expect(dialog).toBeVisible()
  await dialog.getByLabel('Reason').fill(reason)
  await dialog.getByRole('button', { name: 'Confirm' }).click()
  await expect(dialog).toBeHidden()
}

async function participantRow(page: Page, displayName: string): Promise<Locator> {
  const row = page.getByRole('row').filter({ has: page.getByRole('cell', { name: displayName, exact: true }) })
  await expect(row).toBeVisible()
  return row
}

test('manager administers one isolated cycle through the production Docker runtime', async ({ page }) => {
  const suffix = `${Date.now()}-${crypto.randomUUID().slice(0, 6)}`
  const code = `QA-${suffix}`.slice(0, 50)
  const initialName = `QA Cycle ${suffix}`
  const editedName = `${initialName} Edited`
  const externalName = `${initialName} External`
  const finalName = `${initialName} Authoritative`
  const localInput = (offsetDays: number) => {
    const value = new Date(Date.now() + offsetDays * 86_400_000)
    const local = new Date(value.getTime() - value.getTimezoneOffset() * 60_000)
    return local.toISOString().slice(0, 16)
  }

  await page.goto('/?page=cycle-administration')
  await selectDemoIdentity(page, 'participant')
  await expect(page.getByRole('navigation', { name: 'Primary navigation' }).getByRole('button', { name: 'Cycle Administration' })).toHaveCount(0)
  await expect(page.getByRole('heading', { name: 'Cycle Administration' })).toHaveCount(0)
  const participantApi = await page.request.get('/api/manager/cycles')
  expect(participantApi.status()).toBe(403)

  await selectDemoIdentity(page, 'manager')
  const navigation = page.getByRole('navigation', { name: 'Primary navigation' })
  await expect(navigation.getByRole('button', { name: 'Cycle Administration' })).toBeVisible()
  await navigation.getByRole('button', { name: 'Cycle Administration' }).click()
  await expect(page.getByRole('heading', { name: 'Cycle Administration', level: 2 })).toBeVisible()

  await page.getByRole('button', { name: 'Create Cycle' }).click()
  await page.getByLabel('Code').fill(code)
  await page.getByLabel('Name').fill(initialName)
  await page.getByLabel('Starts At').fill(localInput(60))
  await page.getByLabel('Ends At').fill(localInput(90))
  const createResponse = page.waitForResponse(response => response.url().endsWith('/api/manager/cycles') && response.request().method() === 'POST')
  await page.getByRole('button', { name: 'Create Active cycle' }).click()
  const createdResponse = await createResponse
  expect(createdResponse.status()).toBe(200)
  const created = await createdResponse.json() as CycleDetail
  expect(created).toMatchObject({ code, name: initialName, status: 'Active' })
  await expect(page.getByRole('heading', { name: initialName, level: 3 })).toBeVisible()
  await expect(page.getByText('Status:').locator('..')).toContainText('Active')
  await expect(page.getByText('Starts At').locator('..')).not.toContainText('—')
  await expect(page.getByText('Ends At').locator('..')).not.toContainText('—')

  const beforeMetadataEdit = await getCycle(page, created.id)
  await page.getByRole('button', { name: 'Edit cycle' }).click()
  await page.getByLabel('Name').fill(editedName)
  const editResponse = page.waitForResponse(response => response.url().endsWith(`/api/manager/cycles/${created.id}`) && response.request().method() === 'PUT')
  await page.getByRole('button', { name: 'Save cycle' }).click()
  expect((await editResponse).status()).toBe(200)
  const afterMetadataEdit = await getCycle(page, created.id)
  expect(afterMetadataEdit.name).toBe(editedName)
  expect(Date.parse(afterMetadataEdit.startsAt)).toBe(Date.parse(beforeMetadataEdit.startsAt))
  expect(Date.parse(afterMetadataEdit.endsAt)).toBe(Date.parse(beforeMetadataEdit.endsAt))

  await page.reload()
  await openCycle(page, editedName)
  await expect(page.getByText(code, { exact: true })).toBeVisible()

  await page.getByRole('button', { name: 'Edit cycle' }).click()
  const browserVersion = (await getCycle(page, created.id)).version
  const independentUpdate = await page.request.put(`/api/manager/cycles/${created.id}`, {
    data: { version: browserVersion, code, name: externalName, startsAt: afterMetadataEdit.startsAt, endsAt: afterMetadataEdit.endsAt },
  })
  expect(independentUpdate.status()).toBe(200)
  await page.getByLabel('Name').fill(`${editedName} Stale Attempt`)
  const staleResponse = page.waitForResponse(response => response.url().endsWith(`/api/manager/cycles/${created.id}`) && response.request().method() === 'PUT')
  await page.getByRole('button', { name: 'Save cycle' }).click()
  expect((await staleResponse).status()).toBe(409)
  await expect(page.getByRole('alert')).toContainText('This cycle changed')
  const reloadAuthoritative = page.getByRole('button', { name: 'Reload authoritative cycle' })
  await expect(reloadAuthoritative).toBeVisible()
  await reloadAuthoritative.click()
  await expect(page.getByRole('heading', { name: externalName, level: 3 })).toBeVisible()

  await page.getByRole('button', { name: 'Edit cycle' }).click()
  await page.getByLabel('Name').fill(finalName)
  await page.getByRole('button', { name: 'Save cycle' }).click()
  await expect(page.getByRole('heading', { name: finalName, level: 3 })).toBeVisible()

  const participantOptionsResponse = await page.request.get(`/api/manager/cycles/${created.id}/participant-options`)
  expect(participantOptionsResponse.status()).toBe(200)
  const participantOptions = await participantOptionsResponse.json() as { participants: Array<{ participantId: string; displayName: string }> }
  expect(participantOptions.participants.length).toBeGreaterThan(0)
  const option = participantOptions.participants[0]

  let releaseOptions!: () => void
  const optionsHeld = new Promise<void>(resolve => { releaseOptions = resolve })
  let markOptionsHandlerEntered!: () => void
  const optionsHandlerEntered = new Promise<void>(resolve => { markOptionsHandlerEntered = resolve })
  let markOptionsHandlerComplete!: () => void
  const optionsHandlerComplete = new Promise<void>(resolve => { markOptionsHandlerComplete = resolve })
  const optionsPattern = `**/api/manager/cycles/${created.id}/participant-options`
  await page.route(optionsPattern, async route => {
    markOptionsHandlerEntered()
    await optionsHeld
    await route.continue()
    markOptionsHandlerComplete()
  })
  const optionsResponseComplete = page.waitForResponse(response => response.url().endsWith(`/api/manager/cycles/${created.id}/participant-options`) && response.request().method() === 'GET')
  const enrollTrigger = page.getByRole('button', { name: 'Enroll participant' })
  await enrollTrigger.click()
  await optionsHandlerEntered
  const enrollmentDialog = page.getByRole('dialog', { name: 'Enroll participant' })
  await expect(enrollmentDialog).toBeVisible()
  await expect(enrollmentDialog.getByRole('status')).toHaveText('Loading participants…')
  await expect(enrollmentDialog.getByLabel('Participant')).toHaveCount(0)
  expect(await enrollmentDialog.evaluate((dialog) => {
    const active = document.activeElement
    return active instanceof HTMLElement
      && dialog.contains(active)
      && active.matches('button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex]:not([tabindex="-1"])')
  }), 'initial focus must be on an enabled interactive control inside the named modal').toBe(true)
  await page.keyboard.press('Escape')
  await expect(enrollmentDialog).toBeHidden()
  await expect(enrollTrigger).toBeFocused()
  releaseOptions()
  await Promise.all([optionsHandlerComplete, optionsResponseComplete])
  await page.unroute(optionsPattern)

  await enrollTrigger.click()
  await enrollmentDialog.getByLabel('Participant').selectOption(option.participantId)
  await enrollmentDialog.getByLabel('Reason').fill('Focused Cycle Administration browser acceptance enrollment')
  await enrollmentDialog.getByRole('button', { name: 'Confirm' }).click()
  await expect(enrollmentDialog).toBeHidden()
  let row = await participantRow(page, option.displayName)
  await expect(row.getByRole('cell').nth(1)).toHaveText('Active')
  await expect(row.getByRole('cell').nth(2)).not.toHaveText('—')
  await expect(row.getByRole('cell').nth(3)).toHaveText('—')
  const enrolled = await getCycle(page, created.id)
  const enrollment = enrolled.participants.find(person => person.participantId === option.participantId)!
  expect(enrollment.joinedAt).not.toBeNull()
  expect(enrollment.leftAt).toBeNull()

  for (const transition of [
    { button: 'Set Withdrawn', status: 'Withdrawn', reason: 'QA representative withdrawal' },
    { button: 'Set Inactive', status: 'Inactive', reason: 'QA representative inactivity' },
    { button: 'Set Active', status: 'Active', reason: 'QA representative reactivation' },
  ] as const) {
    row = await participantRow(page, option.displayName)
    await row.getByRole('button', { name: transition.button }).click()
    await confirmAction(page, new RegExp(`to ${transition.status}`), transition.reason)
    row = await participantRow(page, option.displayName)
    await expect(row.getByRole('cell').nth(1)).toHaveText(transition.status)
    await expect(row.getByRole('cell').nth(2)).not.toHaveText('—')
    if (transition.status === 'Active') await expect(row.getByRole('cell').nth(3)).toHaveText('—')
    else await expect(row.getByRole('cell').nth(3)).not.toHaveText('—')
    const authoritative = await getCycle(page, created.id)
    const current = authoritative.participants.find(person => person.participantId === option.participantId)!
    expect(current.joinedAt).toBe(enrollment.joinedAt)
    if (transition.status === 'Active') expect(current.leftAt).toBeNull()
    else expect(current.leftAt).not.toBeNull()
  }

  const now = new Date()
  const challengeCreate = await page.request.post('/api/manager/challenges', { data: {
    cycleId: created.id,
    name: `QA Cycle Independence ${suffix}`,
    description: 'Synthetic browser acceptance challenge.',
    category: 'Synthetic QA',
    openAt: new Date(now.getTime() - 86_400_000).toISOString(),
    dueAt: new Date(now.getTime() + 7 * 86_400_000).toISOString(),
    closeAt: new Date(now.getTime() + 14 * 86_400_000).toISOString(),
    heroImageReference: null,
    tasks: [{ id: null, name: 'Cycle-independent task', description: null, xp: 0, scoringMode: 'Individual', evidenceRequirement: 'None', sortOrder: 1 }],
    participationPolicy: null,
  } })
  expect(challengeCreate.status()).toBe(200)
  const challenge = await challengeCreate.json() as { id: string; status: string; openAt: string; dueAt: string; closeAt: string }
  expect(challenge.status).toBe('Draft')

  await page.reload()
  await openCycle(page, finalName)
  await page.getByRole('button', { name: 'Start Closing' }).click()
  await confirmAction(page, /Move this cycle to Closing/, 'QA lifecycle transition to Closing')
  await expect(page.getByText('Status:').locator('..')).toContainText('Closing')
  await expect(page.getByRole('button', { name: 'Edit cycle' })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Enroll participant' })).toHaveCount(0)
  row = await participantRow(page, option.displayName)
  await expect(row.getByText('Read-only')).toBeVisible()

  await page.getByRole('button', { name: 'Finalise Cycle' }).click()
  await confirmAction(page, /Finalise this cycle/, 'QA lifecycle finalisation')
  await expect(page.getByText('Status:').locator('..')).toContainText('Finalised')
  const cycleAdminContent = page.getByRole('main')
  for (const actionName of ['Edit cycle', 'Enroll participant', 'Start Closing', 'Finalise Cycle', 'Reopen', 'Delete', 'Create Draft'])
    await expect(cycleAdminContent.getByRole('button', { name: actionName, exact: true })).toHaveCount(0)
  await expect(cycleAdminContent.getByRole('button', { name: /^Set (Active|Withdrawn|Inactive)$/ })).toHaveCount(0)
  row = await participantRow(page, option.displayName)
  await expect(row.getByText('Read-only', { exact: true })).toBeVisible()

  await page.reload()
  await openCycle(page, finalName)
  await expect(page.getByText('Status:').locator('..')).toContainText('Finalised')
  const finalCycle = await getCycle(page, created.id)
  expect(finalCycle.status).toBe('Finalised')
  const challengeAfter = await page.request.get(`/api/manager/challenges/${challenge.id}`)
  expect(challengeAfter.status()).toBe(200)
  expect(await challengeAfter.json()).toMatchObject({ status: challenge.status, openAt: challenge.openAt, dueAt: challenge.dueAt, closeAt: challenge.closeAt })
})
