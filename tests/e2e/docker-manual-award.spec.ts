import { expect, test } from '@playwright/test'
import { captureEvidence } from './evidence'

type SummaryRow = {
  participantId: string
  displayName: string
  totalXp: number
  bySource: { taskApprovalXp: number; manualAwardXp: number; raidXp: number }
}

test('manager awards append-only Manual XP and participant reporting refreshes', async ({ page }) => {
  await page.goto('/')
  const identity = page.getByRole('combobox', { name: 'Demo identity' })
  await identity.selectOption('manager')
  await page.getByRole('button', { name: 'Scoresheet' }).click()

  const cyclesResponse = await page.request.get('/api/manager/reporting-cycles')
  expect(cyclesResponse.status()).toBe(200)
  const cycles = await cyclesResponse.json() as { defaultCycleId: string }
  const cycleSelector = page.getByLabel('Scoresheet reporting cycle')
  await expect(cycleSelector).toHaveValue(cycles.defaultCycleId)

  const summary = async () => {
    const response = await page.request.get(`/api/manager/scoresheet?cycleId=${cycles.defaultCycleId}`)
    expect(response.status()).toBe(200)
    return await response.json() as { rows: SummaryRow[] }
  }
  const before = await summary()
  const targetBefore = before.rows.find(row => row.displayName === 'Synthetic Participant')!
  expect(targetBefore).toBeTruthy()
  const table = page.getByRole('table')
  const participantRow = table.getByRole('row').filter({ hasText: 'Synthetic Participant' })
  await expect(participantRow).toBeVisible()

  await page.getByRole('button', { name: 'Award XP' }).click()
  const dialog = page.getByRole('dialog', { name: 'Award XP' })
  const optionsResponse = await page.request.get(`/api/manager/manual-awards/options?cycleId=${cycles.defaultCycleId}`)
  expect(optionsResponse.status()).toBe(200)
  const options = await optionsResponse.json() as {
    cycle: { name: string; code: string }
    participants: { participantId: string; displayName: string }[]
    categories: { awardCategoryId: string; name: string }[]
  }
  await expect(dialog).toContainText(`${options.cycle.name} · ${options.cycle.code}`)
  await expect(dialog).toContainText('append-only XP award')
  const participantSelect = dialog.getByLabel('Award participant')
  const categorySelect = dialog.getByLabel('Award category')
  expect(await participantSelect.locator('option:not([value=""])').allTextContents()).toEqual(options.participants.map(item => item.displayName))
  for (const option of options.categories) await expect(categorySelect.locator(`option[value="${option.awardCategoryId}"]`)).toContainText(option.name)
  await participantSelect.selectOption({ label: 'Synthetic Participant' })
  await categorySelect.selectOption(options.categories[0].awardCategoryId)

  const amount = dialog.getByLabel('Award XP amount')
  const reason = dialog.getByLabel('Manual Award reason')
  for (const invalid of ['0', '-1', '1.5']) {
    await amount.fill(invalid)
    await dialog.getByRole('button', { name: 'Review award' }).click()
    await expect(dialog.getByRole('alert')).toHaveText('Enter a positive whole-number XP amount.')
  }
  await amount.fill('10')
  await reason.fill(' ')
  await dialog.getByRole('button', { name: 'Review award' }).click()
  await expect(dialog.getByRole('alert')).toHaveText('Enter a reason for this award.')
  await reason.fill('QA manual award')
  await captureEvidence(page, 'docker-manual-award', '01-award-xp-form.png')
  await dialog.getByRole('button', { name: 'Review award' }).click()
  await expect(dialog).toContainText('Synthetic Participant')
  await expect(dialog).toContainText(options.categories[0].name)
  await expect(dialog).toContainText('+10 XP')
  await expect(dialog).toContainText('QA manual award')
  await expect(dialog).toContainText('append a new audited XP ledger entry')
  await captureEvidence(page, 'docker-manual-award', '02-award-confirmation.png')

  let command: { requestId: string; cycleId: string; participantId: string; awardCategoryId: string; amount: number; reason: string } | undefined
  await page.route('**/api/manager/manual-awards', async route => {
    if (route.request().method() === 'POST') {
      command = route.request().postDataJSON()
      await new Promise(resolve => setTimeout(resolve, 600))
    }
    await route.continue()
  })
  const createResponse = page.waitForResponse(response => response.url().endsWith('/api/manager/manual-awards') && response.request().method() === 'POST')
  await dialog.getByRole('button', { name: 'Confirm award' }).click()
  await expect(dialog.getByRole('button', { name: 'Awarding XP…' })).toBeDisabled()
  await expect(participantRow.getByRole('cell').nth(3)).toHaveText(String(targetBefore.bySource.manualAwardXp))
  await expect(participantRow.getByRole('cell').nth(6)).toHaveText(String(targetBefore.totalXp))
  expect((await createResponse).status()).toBe(200)
  await page.unroute('**/api/manager/manual-awards')
  await expect(page.getByText(/Manual XP award for Synthetic Participant recorded/)).toBeVisible()
  expect(command).toBeTruthy()

  const after = await summary()
  const targetAfter = after.rows.find(row => row.participantId === targetBefore.participantId)!
  expect(targetAfter.totalXp).toBe(targetBefore.totalXp + 10)
  expect(targetAfter.bySource.manualAwardXp).toBe(targetBefore.bySource.manualAwardXp + 10)
  await expect(participantRow.getByRole('cell').nth(3)).toHaveText(String(targetAfter.bySource.manualAwardXp))
  await expect(participantRow.getByRole('cell').nth(6)).toHaveText(String(targetAfter.totalXp))
  await captureEvidence(page, 'docker-manual-award', '03-scoresheet-after-award.png')

  await participantRow.getByRole('button', { name: 'Synthetic Participant' }).click()
  const ledger = page.getByLabel('Participant Scoresheet detail')
  const awardRow = ledger.locator('article').filter({ hasText: 'QA manual award' }).first()
  await expect(awardRow).toContainText('+10 XP')
  await expect(awardRow).toContainText('Grant · ManualAward')
  await expect(awardRow).toContainText(options.categories[0].name)
  await expect(awardRow.getByRole('button', { name: 'Correct XP' })).toHaveCount(0)
  await captureEvidence(page, 'docker-manual-award', '04-manual-award-ledger.png')

  const replay = await page.request.post('/api/manager/manual-awards', { data: command })
  expect(replay.status()).toBe(200)
  expect((await replay.json()).id).toBe(command!.requestId)
  const conflict = await page.request.post('/api/manager/manual-awards', { data: { ...command!, amount: 11 } })
  expect(conflict.status()).toBe(409)
  expect((await conflict.json()).code).toBe('ManualAwardRequestConflict')
  const detailResponse = await page.request.get(`/api/manager/scoresheet/${targetBefore.participantId}?cycleId=${cycles.defaultCycleId}&limit=25`)
  const detail = await detailResponse.json() as { items: { id: string; sourceType: string }[] }
  expect(detail.items.filter(item => item.id === command!.requestId && item.sourceType === 'ManualAward')).toHaveLength(1)
  expect((await summary()).rows.find(row => row.participantId === targetBefore.participantId)!.totalXp).toBe(targetAfter.totalXp)

  await identity.selectOption('participant')
  await expect(page.locator('.reporting-stats article').filter({ hasText: 'Total XP' }).first()).toContainText(String(targetAfter.totalXp))
  await captureEvidence(page, 'docker-manual-award', '05-participant-dashboard.png')
  await page.getByRole('button', { name: 'XP Activity' }).click()
  const activity = page.locator('article').filter({ hasText: 'QA manual award' }).first()
  await expect(activity).toContainText('+10 XP')
  await expect(activity).toContainText('Manual Award · Grant ·')
  await expect(activity).not.toContainText(/^Grant ·/)
  await expect(activity).toContainText(options.categories[0].name)
  await captureEvidence(page, 'docker-manual-award', '06-participant-xp-activity.png')
  await page.getByRole('button', { name: 'Leaderboard' }).click()
  await expect(page.getByRole('row').filter({ hasText: 'Synthetic Participant' })).toContainText(`${targetAfter.totalXp} XP`)
  await captureEvidence(page, 'docker-manual-award', '07-participant-leaderboard.png')

  await identity.selectOption('manager')
  await page.getByRole('button', { name: 'Scoresheet' }).click()
  await expect(page.getByRole('button', { name: 'Award XP' })).toBeVisible()
  await page.getByRole('button', { name: 'Challenges' }).click()
  await expect(page.getByRole('heading', { name: 'Challenge administration', level: 2 })).toBeVisible()
  await page.getByRole('button', { name: 'Review queue' }).click()
  await expect(page.getByRole('heading', { name: 'Review queue', level: 1 })).toBeVisible()
  await expect(page.getByText(/team award|bulk award|raid award|csv import/i)).toHaveCount(0)
})
