import { expect, test } from '@playwright/test'
import { captureEvidence } from './evidence'

test('manager administers and publishes a challenge visible to a participant', async ({ page }) => {
  const runSuffix = Date.now().toString()
  const draftName = `Synthetic Admin Quest ${runSuffix}`
  const publishedName = `${draftName} Updated`
  const now = new Date()
  const localDate = (offsetDays: number) => {
    const value = new Date(now.getTime() + offsetDays * 86_400_000)
    const local = new Date(value.getTime() - value.getTimezoneOffset() * 60_000)
    return local.toISOString().slice(0, 16)
  }

  await page.goto('/')
  const identity = page.getByRole('combobox', { name: 'Demo identity' })
  await identity.selectOption('manager')
  await page.getByRole('button', { name: 'Challenges' }).click()
  await expect(page.getByRole('heading', { name: 'Challenge administration' })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Synthetic Shared Challenge' })).toBeVisible()
  await captureEvidence(page, 'docker-manager-challenges', '01-manager-challenge-list.png')

  await page.getByLabel('Cycle filter').selectOption({ label: 'Synthetic Showcase Cycle' })
  const draftFilter = page.waitForResponse(response => response.url().includes('/api/manager/challenges?') && response.url().includes('status=Draft') && response.ok())
  await page.getByLabel('Status filter').selectOption('Draft')
  await draftFilter
  await expect(page.getByRole('heading', { name: 'Synthetic Shared Challenge' })).toHaveCount(0)
  const openFilter = page.waitForResponse(response => response.url().includes('/api/manager/challenges?') && response.url().includes('status=Open') && response.ok())
  await page.getByLabel('Status filter').selectOption('Open')
  await openFilter
  await expect(page.getByRole('heading', { name: 'Synthetic Shared Challenge' })).toBeVisible()
  await page.getByLabel('Status filter').selectOption('')

  await page.getByRole('button', { name: 'Create Challenge' }).click()
  await page.getByLabel('Name').fill(draftName)
  await page.getByLabel('Description (optional)').fill('Synthetic manager administration acceptance draft.')
  await page.getByLabel('Category (optional)').fill('Synthetic QA')
  await page.getByLabel('Opens').fill(localDate(-1))
  await page.getByLabel('Due').fill(localDate(7))
  await page.getByLabel('Closes').fill(localDate(14))
  await page.getByRole('button', { name: 'Add task' }).click()
  await page.getByRole('button', { name: 'Add task' }).click()
  const tasks = page.locator('.admin-task')
  await tasks.nth(0).getByLabel('Task name').fill('Observe as team without XP')
  await tasks.nth(0).getByLabel('Scoring mode').selectOption('WholeTeam')
  await tasks.nth(0).getByLabel('Evidence requirement').selectOption('Text')
  await tasks.nth(1).getByLabel('Task name').fill('Upload evidence for points')
  await tasks.nth(1).getByLabel('XP').fill('25')
  await tasks.nth(1).getByLabel('Evidence requirement').selectOption('Attachment')
  await page.getByLabel('Formation mode').selectOption('ManagerAssigned')
  await page.getByLabel('Minimum members').fill('2')
  await page.getByLabel('Maximum members').fill('4')
  await page.getByRole('button', { name: 'Create draft' }).click()
  await expect(page.getByText('Draft', { exact: true })).toBeVisible()
  await expect(page.getByRole('heading', { name: draftName })).toBeVisible()
  await captureEvidence(page, 'docker-manager-challenges', '02-created-draft.png')

  await page.getByLabel('Name', { exact: true }).fill(publishedName)
  await page.locator('textarea').fill('Edited synthetic acceptance challenge.')
  await tasks.nth(1).getByLabel('XP').fill('30')
  await tasks.nth(1).getByRole('button', { name: '↑' }).click()
  await expect(tasks.nth(0).getByLabel('Task name')).toHaveValue('Upload evidence for points')
  await page.getByRole('button', { name: 'Save full draft' }).click()
  await expect(page.getByRole('heading', { name: publishedName })).toBeVisible()
  await expect(tasks.nth(0).getByLabel('XP')).toHaveValue('30')
  await captureEvidence(page, 'docker-manager-challenges', '03-edited-reordered-draft.png')

  page.once('dialog', async dialog => {
    expect(dialog.message()).toContain('Publishing freezes task, scoring, evidence and participation configuration.')
    await dialog.accept()
  })
  await page.getByRole('button', { name: 'Publish challenge' }).click()
  await expect(page.getByText(/Published challenges are read-only/)).toBeVisible()
  await expect(page.getByRole('button', { name: 'Save full draft' })).toHaveCount(0)
  await captureEvidence(page, 'docker-manager-challenges', '04-publish-confirmed-read-only.png')

  await identity.selectOption('participant')
  await page.getByRole('button', { name: 'Challenges' }).click()
  const publishedChallenge = page.getByRole('heading', { name: publishedName }).locator('xpath=ancestor::article[1]')
  await expect(publishedChallenge).toBeVisible()
  await expect(publishedChallenge.getByText('Upload evidence for points')).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Synthetic Shared Challenge' })).toBeVisible()
  await captureEvidence(page, 'docker-manager-challenges', '05-participant-visibility.png')

  await page.getByRole('button', { name: 'Dashboard' }).click()
  await expect(page.getByText('Total XP', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'My activity' }).click()
  await expect(page.getByRole('heading', { name: 'My activity', level: 1 })).toBeVisible()
})
