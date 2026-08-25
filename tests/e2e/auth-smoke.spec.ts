import { expect, test } from '@playwright/test'
import { captureEvidence } from './evidence'

test('Step 5A demo identity switching remains API-confirmed and fail-safe', async ({ page }) => {
  await page.goto('/')
  await captureEvidence(page, 'auth', '01-login.png')

  await expect(page.getByText('DEVELOPMENT ONLY')).toBeVisible()
  const selector = page.getByRole('combobox', { name: 'Demo identity' })
  await expect(selector.getByRole('option', { name: 'Synthetic participant' })).toBeAttached()
  await expect(selector.getByRole('option', { name: 'Synthetic manager' })).toBeAttached()

  await selector.selectOption('participant')
  await expect(page.getByRole('heading', { name: 'Welcome, Synthetic Participant' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Submit work' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Review queue' })).toHaveCount(0)
  await captureEvidence(page, 'auth', '02-participant.png')

  await page.getByRole('button', { name: 'My activity' }).click()
  await expect(page.getByRole('heading', { name: 'My activity' })).toBeVisible()
  await page.route('**/api/auth/demo/session', async route => {
    if (route.request().method() === 'POST') await route.fulfill({ status: 401 })
    else await route.continue()
  })
  await selector.selectOption('manager')
  await expect(page.getByRole('alert')).toContainText('Authentication is temporarily unavailable')
  await expect(page.getByRole('heading', { name: 'My activity' })).toBeVisible()
  await expect(page.getByLabel('Active identity').getByText('Synthetic Participant')).toBeVisible()

  await page.unroute('**/api/auth/demo/session')
  await selector.selectOption('participant')
  await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible()
  await page.getByRole('button', { name: 'My activity' }).click()
  await selector.selectOption('manager')
  await expect(page.getByRole('heading', { name: 'Welcome, Synthetic Manager' })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Review queue' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Submit work' })).toHaveCount(0)
  await captureEvidence(page, 'auth', '03-manager.png')

  await selector.selectOption('participant')
  await expect(page.getByRole('heading', { name: 'Welcome, Synthetic Participant' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Submit work' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Review queue' })).toHaveCount(0)

  await page.setViewportSize({ width: 390, height: 844 })
  await expect(page.getByRole('combobox', { name: 'Demo identity' })).toBeVisible()
  await expect(page.getByLabel('Active identity').getByText('Synthetic Participant')).toBeVisible()
  await captureEvidence(page, 'auth', '04-mobile-participant.png')
})
