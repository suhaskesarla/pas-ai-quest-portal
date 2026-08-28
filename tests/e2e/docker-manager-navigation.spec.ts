import { expect, test } from '@playwright/test'
import { captureEvidence } from './evidence'

test('manager dashboard exposes only supported navigation and real workflow destinations', async ({ page }) => {
  await page.goto('/')
  const identity = page.getByRole('combobox', { name: 'Demo identity' })
  await identity.selectOption('manager')
  await expect(page.getByText('DEVELOPMENT · DEMO AUTH ACTIVE')).toBeVisible()
  await expect(page.getByLabel('Active identity')).toContainText('Synthetic Manager')
  await expect(page.getByLabel('Active identity')).toContainText('Manager')

  const navigation = page.getByRole('navigation', { name: 'Primary navigation' })
  await expect(navigation.getByRole('button')).toHaveText(['Dashboard', 'Challenges', 'Review Queue', 'Scoresheet'])
  for (const absent of ['New Challenge', 'Leaderboard', 'Analytics', 'Cycle Administration', 'Manual Award', 'Correction']) {
    await expect(navigation.getByRole('button', { name: absent, exact: true })).toHaveCount(0)
  }
  await expect(page.getByText('This portal area is outside the current Step 6 workflow.')).toHaveCount(0)

  await expect(page.getByRole('heading', { name: 'Manager Dashboard', level: 2 })).toBeVisible()
  await expect(page.getByText('Manage challenges, review participant submissions, and maintain the authoritative XP record.')).toBeVisible()
  const cards = page.locator('.manager-dashboard-grid > article')
  await expect(cards).toHaveCount(3)
  await expect(cards.nth(0)).toContainText('Manage Challenges')
  await expect(cards.nth(1)).toContainText('Review Submissions')
  await expect(cards.nth(2)).toContainText('Scoresheet & XP')
  await expect(cards.nth(2)).toContainText('manual XP awards')
  await expect(cards.nth(2)).toContainText('correct approved task XP')
  await expect(cards.nth(1)).toContainText('No submissions are waiting for review.')
  await captureEvidence(page, 'docker-manager-navigation', '01-manager-dashboard-clean-navigation.png')

  const layout = await cards.evaluateAll(nodes => ({ viewportWidth: window.innerWidth, boxes: nodes.map(node => node.getBoundingClientRect()).map(({ left, right, top, bottom }) => ({ left, right, top, bottom })) }))
  const boxes = layout.boxes
  expect(boxes.every(box => box.left >= 0 && box.right <= layout.viewportWidth)).toBe(true)
  expect(boxes[0].right <= boxes[1].left || boxes[0].bottom <= boxes[1].top).toBe(true)
  const navBox = await navigation.boundingBox()
  expect(navBox).not.toBeNull()
  expect(navBox!.height).toBeLessThanOrEqual(720)

  await page.getByRole('button', { name: 'Manage challenges' }).click()
  await expect(page.getByRole('heading', { name: 'Challenge Administration', level: 2 })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Create Challenge' })).toBeVisible()
  await captureEvidence(page, 'docker-manager-navigation', '02-manage-challenges.png')

  await navigation.getByRole('button', { name: 'Dashboard' }).click()
  await page.getByRole('button', { name: 'Open review queue' }).click()
  await expect(page.getByText('All caught up—nothing is waiting for review.')).toBeVisible()
  await captureEvidence(page, 'docker-manager-navigation', '03-review-queue-zero.png')

  await navigation.getByRole('button', { name: 'Dashboard' }).click()
  await page.getByRole('button', { name: 'View scoresheet' }).click()
  await expect(page.getByRole('heading', { name: 'Scoresheet summary', level: 2 })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Award XP' })).toBeVisible()
  await captureEvidence(page, 'docker-manager-navigation', '04-scoresheet-award-xp.png')

  await identity.selectOption('participant')
  await expect(page.getByRole('heading', { name: 'Welcome, Synthetic Participant' })).toBeVisible()
  await page.getByRole('button', { name: 'Challenges' }).click()
  const task = page.locator('.task-row').filter({ hasText: 'Complete the synthetic shared task' })
  await task.getByRole('button', { name: 'Submit work' }).click()
  await page.getByRole('textbox', { name: 'Evidence *' }).fill('Synthetic manager navigation regression evidence')
  await page.getByRole('button', { name: 'Submit for review' }).click()
  await expect(page.getByText('Submitted', { exact: true }).first()).toBeVisible()

  await identity.selectOption('manager')
  await expect(page.getByRole('heading', { name: 'Manager Dashboard', level: 2 })).toBeVisible()
  await expect(cards.nth(1)).toContainText('1 submission waiting for review.')
  await page.getByRole('button', { name: 'Open review queue' }).click()
  await expect(page.getByText('Synthetic manager navigation regression evidence')).toBeVisible()
  page.once('dialog', dialog => void dialog.accept())
  await page.getByRole('button', { name: 'Approve all 2' }).click()
  await expect(page.getByText('All caught up—nothing is waiting for review.')).toBeVisible()

  await navigation.getByRole('button', { name: 'Scoresheet' }).click()
  const participantRow = page.getByRole('table').getByRole('row').filter({ hasText: 'Synthetic Participant' })
  await participantRow.getByRole('button', { name: 'Synthetic Participant' }).click()
  await expect(page.getByLabel('Participant Scoresheet detail').getByRole('button', { name: 'Correct XP' })).toBeVisible()
  await captureEvidence(page, 'docker-manager-navigation', '05-correction-reachable.png')
  await expect(page.getByText('This portal area is outside the current Step 6 workflow.')).toHaveCount(0)
})
