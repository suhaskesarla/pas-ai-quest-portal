import { expect, test, type Locator, type Page } from '@playwright/test'
import { captureEvidence } from '../evidence'

const cycleId = '60000000-0000-4000-8000-000000000001'
const seededXpEntryId = '60000000-0000-4000-8000-00000000000a'

type SummaryRow = {
  participantId: string
  displayName: string
  totalXp: number
  bySource: { taskApprovalXp: number; manualAwardXp: number; raidXp: number }
  byEntryType: { grantXp: number; reversalXp: number; correctionXp: number; netAdjustmentXp: number }
}

async function switchIdentity(page: Page, profile: 'participant' | 'manager') {
  await page.getByRole('combobox', { name: 'Demo identity' }).selectOption(profile)
  await expect(page.getByLabel('Active identity')).toContainText(profile === 'manager' ? 'Manager' : 'Participant')
  await expect(page.getByRole('heading', { name: profile === 'manager' ? 'Manager Dashboard' : /Welcome, Synthetic Participant/ })).toBeVisible()
}

async function summaryRow(page: Page, participantId: string) {
  const response = await page.request.get(`/api/manager/scoresheet?cycleId=${cycleId}`)
  expect(response.status()).toBe(200)
  const body = await response.json() as { rows: SummaryRow[] }
  const row = body.rows.find(item => item.participantId === participantId)
  expect(row, 'clean demo scoresheet must include the synthetic participant').toBeTruthy()
  return row!
}

function scoresheetRow(page: Page, displayName = 'Synthetic Participant') {
  return page.getByRole('table').getByRole('row').filter({ hasText: displayName })
}

function participantRaidCard(page: Page, displayName = 'Synthetic Participant') {
  return page.getByRole('heading', { name: displayName, level: 4 }).locator('xpath=ancestor::article[1]')
}

async function expectBalance(region: Locator, assigned: number, used: number, remaining: number) {
  await expect(region.getByText('Assigned', { exact: true }).locator('..')).toContainText(String(assigned))
  await expect(region.getByText('Used', { exact: true }).locator('..')).toContainText(String(used))
  await expect(region.getByText('Remaining', { exact: true }).locator('..')).toContainText(String(remaining))
}

test('canonical clean-Docker PAS AI Quest demo reconciles the complete business story', async ({ page }) => {
  const suffix = `${Date.now()}-${crypto.randomUUID().slice(0, 6)}`
  const challengeName = `Canonical Demo Quest ${suffix}`
  const taskName = `Canonical evidence task ${suffix}`
  const firstEvidence = `Canonical initial evidence ${suffix}`
  const replacementEvidence = `Canonical replacement evidence ${suffix}`
  const feedback = `Please strengthen the canonical evidence ${suffix}`
  const manualReason = `Canonical manual award ${suffix}`
  const correctionReason = `Canonical task correction ${suffix}`
  const raidName = `Canonical Raid ${suffix}`
  const raidReason = `Canonical Raid XP ${suffix}`
  const now = new Date()
  const localDate = (offsetDays: number) => {
    const value = new Date(now.getTime() + offsetDays * 86_400_000)
    return new Date(value.getTime() - value.getTimezoneOffset() * 60_000).toISOString().slice(0, 16)
  }

  await page.goto('/')
  await expect(page.getByText('Demo authentication', { exact: false })).toBeVisible()
  await switchIdentity(page, 'manager')
  const managerNav = page.getByRole('navigation', { name: 'Primary navigation' })
  await expect(managerNav.getByRole('button', { name: 'Challenges' })).toBeVisible()
  await expect(managerNav.getByRole('button', { name: 'Scoresheet' })).toBeVisible()
  await expect(managerNav.getByRole('button', { name: 'Raid Administration' })).toBeVisible()
  await expect(managerNav.getByRole('button', { name: 'My activity' })).toHaveCount(0)
  await captureEvidence(page, 'docker-full-demo', '01-manager-authentication.png')

  await switchIdentity(page, 'participant')
  const meResponse = await page.request.get('/api/auth/me')
  expect(meResponse.status()).toBe(200)
  const me = await meResponse.json() as { participantId: string; displayName: string }
  expect(me.displayName).toBe('Synthetic Participant')
  await switchIdentity(page, 'manager')

  const baseline = await summaryRow(page, me.participantId)
  expect(baseline.totalXp).toBe(10)
  expect(baseline.bySource).toEqual({ taskApprovalXp: 0, manualAwardXp: 10, raidXp: 0 })
  const seededDetail = await page.request.get(`/api/manager/scoresheet/${me.participantId}?cycleId=${cycleId}&limit=25`)
  expect(seededDetail.status()).toBe(200)
  const seededLedger = await seededDetail.json() as { items: Array<{ id: string; amount: number; sourceType: string }> }
  expect(seededLedger.items).toContainEqual(expect.objectContaining({ id: seededXpEntryId, amount: 10, sourceType: 'ManualAward' }))

  await managerNav.getByRole('button', { name: 'Challenges' }).click()
  await expect(page.getByRole('heading', { name: 'Challenge Administration', level: 2 })).toBeVisible()
  await page.getByRole('button', { name: 'Create Challenge' }).click()
  await page.getByLabel('Cycle').selectOption(cycleId)
  await page.getByLabel('Name').fill(challengeName)
  await page.getByLabel('Description (optional)').fill('Synthetic canonical release-gate challenge.')
  await page.getByLabel('Category (optional)').fill('Canonical Demo')
  await page.getByLabel('Opens').fill(localDate(-1))
  await page.getByLabel('Due').fill(localDate(7))
  await page.getByLabel('Closes').fill(localDate(14))
  await page.getByRole('button', { name: 'Add task' }).click()
  const taskEditor = page.locator('.admin-task').first()
  await taskEditor.getByLabel('Task name').fill(taskName)
  await taskEditor.getByLabel('XP').fill('25')
  await taskEditor.getByLabel('Scoring mode').selectOption('Individual')
  await taskEditor.getByLabel('Evidence requirement').selectOption('Text')
  const createResponse = page.waitForResponse(response => response.url().endsWith('/api/manager/challenges') && response.request().method() === 'POST')
  await page.getByRole('button', { name: 'Create draft' }).click()
  const created = await createResponse
  expect(created.status()).toBe(200)
  const createdChallenge = await created.json() as { id: string; status: string; tasks: Array<{ id: string; xp: number }> }
  expect(createdChallenge.status).toBe('Draft')
  expect(createdChallenge.tasks).toHaveLength(1)
  await expect(page.getByRole('heading', { name: challengeName })).toBeVisible()
  page.once('dialog', dialog => void dialog.accept())
  const publishResponse = page.waitForResponse(response => response.url().endsWith(`/api/manager/challenges/${createdChallenge.id}/publish`) && response.request().method() === 'POST')
  await page.getByRole('button', { name: 'Publish challenge' }).click()
  const published = await publishResponse
  expect(published.status()).toBe(200)
  expect((await published.json()).status).toBe('Open')
  await expect(page.getByText(/Published challenges are read-only/)).toBeVisible()
  await captureEvidence(page, 'docker-full-demo', '02-challenge-published-open.png')

  await switchIdentity(page, 'participant')
  await page.getByRole('button', { name: 'Challenges' }).click()
  const challengeCard = page.getByRole('heading', { name: challengeName }).locator('xpath=ancestor::article[1]')
  await expect(challengeCard).toBeVisible()
  await expect(challengeCard).toContainText(taskName)
  await expect(challengeCard).toContainText('25 XP')
  await expect(challengeCard).toContainText('Evidence')
  await expect(challengeCard.getByText('Your effective deadline', { exact: true }).locator('..')).not.toContainText('—')
  await captureEvidence(page, 'docker-full-demo', '03-participant-discovery.png')

  await challengeCard.getByRole('button', { name: 'Submit work' }).click()
  await expect(page.getByText(`Claimant: ${me.displayName}`)).toBeVisible()
  await page.getByRole('textbox', { name: 'Evidence *' }).fill(firstEvidence)
  const submitResponse = page.waitForResponse(response => response.url().endsWith('/api/submissions') && response.request().method() === 'POST')
  await page.getByRole('button', { name: 'Submit for review' }).click()
  expect((await submitResponse).status()).toBe(200)
  const activityCard = page.getByRole('heading', { name: taskName }).locator('xpath=ancestor::article[1]')
  await expect(activityCard).toContainText('Submitted')
  await expect(activityCard).toContainText(firstEvidence)
  expect((await summaryRowAfterManagerSwitch()).totalXp).toBe(10)
  await switchIdentity(page, 'participant')
  await page.getByRole('button', { name: 'My activity' }).click()
  await captureEvidence(page, 'docker-full-demo', '04-initial-submission-no-xp.png')

  await switchIdentity(page, 'manager')
  await page.getByRole('navigation', { name: 'Primary navigation' }).getByRole('button', { name: 'Review Queue', exact: true }).click()
  const reviewCard = page.getByRole('heading', { name: taskName }).locator('xpath=ancestor::article[1]')
  await expect(reviewCard).toContainText(firstEvidence)
  await reviewCard.getByLabel('Manager comment').fill(feedback)
  await reviewCard.getByRole('button', { name: 'Needs evidence' }).click()
  await expect(reviewCard).toHaveCount(0)
  await captureEvidence(page, 'docker-full-demo', '05-manager-needs-evidence.png')

  await switchIdentity(page, 'participant')
  await page.getByRole('button', { name: 'Challenges' }).click()
  await expect(page.getByRole('heading', { name: challengeName }).locator('xpath=ancestor::article[1]').getByText('Your effective deadline', { exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'My activity' }).click()
  const resubmitCard = page.getByRole('heading', { name: taskName }).locator('xpath=ancestor::article[1]')
  await expect(resubmitCard).toContainText('Needs evidence')
  await expect(resubmitCard).toContainText(feedback)
  await expect(resubmitCard.getByLabel('Submission history')).toContainText('Needs evidence')
  await resubmitCard.getByRole('textbox', { name: 'Evidence *' }).fill(replacementEvidence)
  await resubmitCard.getByLabel('Response to manager').fill('Canonical evidence updated for review.')
  await resubmitCard.getByRole('button', { name: 'Resubmit shared submission' }).click()
  await expect(resubmitCard).toContainText('Resubmitted')
  await expect(resubmitCard).toContainText(replacementEvidence)
  await expect(resubmitCard).not.toContainText(firstEvidence)
  await captureEvidence(page, 'docker-full-demo', '06-participant-resubmitted.png')

  await switchIdentity(page, 'manager')
  await page.getByRole('navigation', { name: 'Primary navigation' }).getByRole('button', { name: 'Review Queue', exact: true }).click()
  const approvalCard = page.getByRole('heading', { name: taskName }).locator('xpath=ancestor::article[1]')
  await expect(approvalCard).toContainText(replacementEvidence)
  page.once('dialog', dialog => void dialog.accept())
  await approvalCard.getByRole('button', { name: 'Approve all 1' }).click()
  await expect(approvalCard).toHaveCount(0)
  const afterApproval = await summaryRow(page, me.participantId)
  expect(afterApproval.totalXp).toBe(35)
  expect(afterApproval.bySource.taskApprovalXp).toBe(25)
  await captureEvidence(page, 'docker-full-demo', '07-manager-approved.png')

  await switchIdentity(page, 'participant')
  await page.getByRole('button', { name: 'My activity' }).click()
  const approvedCard = page.getByRole('heading', { name: taskName }).locator('xpath=ancestor::article[1]')
  await expect(approvedCard).toContainText('Approved')
  await expect(approvedCard).toContainText('Awarded task result: 25 XP')
  await expect(approvedCard.getByLabel('Submission history')).toContainText('Resubmitted')
  await page.getByRole('button', { name: 'Dashboard' }).click()
  await expect(page.locator('.reporting-stats article').filter({ hasText: 'Total XP' })).toContainText('35')
  await page.getByRole('button', { name: 'XP Activity' }).click()
  await expect(page.locator('article').filter({ hasText: taskName }).first()).toContainText('+25 XP')
  await page.getByRole('button', { name: 'Leaderboard' }).click()
  await expect(page.getByRole('row').filter({ hasText: me.displayName })).toContainText('35 XP')
  await captureEvidence(page, 'docker-full-demo', '08-participant-approved-reporting.png')

  await switchIdentity(page, 'manager')
  await page.getByRole('navigation', { name: 'Primary navigation' }).getByRole('button', { name: 'Scoresheet', exact: true }).click()
  await page.getByLabel('Scoresheet reporting cycle').selectOption(cycleId)
  let tableRow = scoresheetRow(page)
  await expect(tableRow.getByRole('cell').nth(2)).toHaveText('25')
  await expect(tableRow.getByRole('cell').nth(6)).toHaveText('35')

  await page.getByRole('button', { name: 'Award XP' }).click()
  let dialog = page.getByRole('dialog', { name: 'Award XP' })
  await dialog.getByLabel('Award participant').selectOption({ label: me.displayName })
  await dialog.getByLabel('Award category').selectOption('60000000-0000-4000-8000-000000000009')
  await dialog.getByLabel('Award XP amount').fill('10')
  await dialog.getByLabel('Manual Award reason').fill(manualReason)
  await dialog.getByRole('button', { name: 'Review award' }).click()
  await expect(dialog).toContainText('+10 XP')
  await expect(dialog).toContainText(manualReason)
  await captureEvidence(page, 'docker-full-demo', '09-manual-award-confirmation.png')
  await dialog.getByRole('button', { name: 'Confirm award' }).click()
  await expect(page.getByText(/Manual XP award for Synthetic Participant recorded/)).toBeVisible()
  let afterManual = await summaryRow(page, me.participantId)
  expect(afterManual.totalXp).toBe(45)
  expect(afterManual.bySource.manualAwardXp).toBe(20)

  await tableRow.getByRole('button', { name: me.displayName }).click()
  let ledger = page.getByLabel('Participant Scoresheet detail')
  const taskGrant = ledger.locator('article').filter({ hasText: taskName }).filter({ hasText: 'Grant · TaskApproval' }).first()
  await expect(taskGrant).toContainText('+25 XP')
  await taskGrant.getByRole('button', { name: 'Correct XP' }).click()
  dialog = page.getByRole('dialog', { name: 'Correct XP' })
  await expect(dialog).toContainText('Current effective XP')
  await expect(dialog).toContainText('25 XP')
  await dialog.getByLabel('New effective XP').fill('20')
  await dialog.getByLabel('Correction reason').fill(correctionReason)
  await dialog.getByRole('button', { name: 'Review correction' }).click()
  await expect(dialog).toContainText('Resulting change')
  await expect(dialog).toContainText('−5 XP')
  await captureEvidence(page, 'docker-full-demo', '10-task-correction-confirmation.png')
  await dialog.getByRole('button', { name: 'Confirm correction' }).click()
  await expect(dialog).toBeHidden()
  ledger = page.getByLabel('Participant Scoresheet detail')
  await expect(ledger.locator('article').filter({ hasText: taskName }).filter({ hasText: 'Grant · TaskApproval' }).first()).toContainText('+25 XP')
  await expect(ledger.locator('article').filter({ hasText: correctionReason }).first()).toContainText('−5 XP')
  const afterCorrection = await summaryRow(page, me.participantId)
  expect(afterCorrection.totalXp).toBe(40)
  expect(afterCorrection.bySource.taskApprovalXp).toBe(20)
  expect(afterCorrection.byEntryType.netAdjustmentXp).toBe(-5)
  await captureEvidence(page, 'docker-full-demo', '11-ledger-after-correction.png')

  await page.getByRole('navigation', { name: 'Primary navigation' }).getByRole('button', { name: 'Raid Administration', exact: true }).click()
  await page.getByLabel('Reporting cycle').selectOption(cycleId)
  await page.getByRole('button', { name: 'Create Raid Session' }).click()
  dialog = page.getByRole('dialog', { name: 'Create Raid Session' })
  await dialog.getByLabel('Name').fill(raidName)
  await dialog.getByLabel('Occurred At').fill(localDate(0))
  await dialog.getByRole('button', { name: 'Save Raid Session' }).click()
  await expect(page.getByRole('heading', { name: raidName, level: 3 })).toBeVisible()
  let raidCard = participantRaidCard(page)
  let physical = raidCard.getByRole('region', { name: 'Physical pass' })
  await expectBalance(physical, 2, 1, 1)
  await physical.getByRole('button', { name: 'Update Physical assignment' }).click()
  dialog = page.getByRole('dialog', { name: 'Update Physical assignment' })
  await dialog.getByLabel('Assigned').fill('3')
  await dialog.getByRole('button', { name: 'Save assignment' }).click()
  await expectBalance(physical, 3, 1, 2)
  await raidCard.getByRole('button', { name: 'Record Raid Participation' }).click()
  dialog = page.getByRole('dialog', { name: 'Record Raid Participation' })
  await dialog.getByLabel('Pass type').selectOption('Physical')
  await dialog.getByRole('button', { name: 'Confirm participation' }).click()
  await expect(page.getByText('Raid participation recorded. Pass usage refreshed; no XP was awarded.')).toBeVisible()
  raidCard = participantRaidCard(page)
  physical = raidCard.getByRole('region', { name: 'Physical pass' })
  await expectBalance(physical, 3, 2, 1)
  expect((await summaryRow(page, me.participantId)).totalXp).toBe(40)
  await raidCard.getByRole('button', { name: 'Award Raid XP' }).click()
  dialog = page.getByRole('dialog', { name: 'Award Raid XP' })
  await dialog.getByLabel('XP amount').fill('7')
  await dialog.getByLabel('Reason').fill(raidReason)
  await dialog.getByRole('button', { name: 'Review Raid XP' }).click()
  await expect(dialog).toContainText('+7 XP')
  await dialog.getByRole('button', { name: 'Confirm Raid XP' }).click()
  await expect(page.getByText('Raid XP recorded. Updated XP is available in Scoresheet and participant reporting.')).toBeVisible()
  const finalSummary = await summaryRow(page, me.participantId)
  expect(finalSummary.bySource).toEqual({ taskApprovalXp: 20, manualAwardXp: 20, raidXp: 7 })
  expect(finalSummary.totalXp).toBe(47)
  await captureEvidence(page, 'docker-full-demo', '12-raid-smoke-complete.png')

  await switchIdentity(page, 'participant')
  await page.getByLabel('Reporting cycle').selectOption(cycleId)
  await expect(page.locator('.reporting-stats article').filter({ hasText: 'Total XP' })).toContainText('47')
  await page.getByRole('button', { name: 'XP Activity' }).click()
  await expect(page.locator('article').filter({ hasText: manualReason }).first()).toContainText('Manual Award · Grant')
  await expect(page.locator('article').filter({ hasText: correctionReason }).first()).toContainText('−5 XP')
  await expect(page.locator('article').filter({ hasText: raidReason }).first()).toContainText('Raid · Grant')
  await page.getByRole('button', { name: 'Leaderboard' }).click()
  await expect(page.getByRole('row').filter({ hasText: me.displayName })).toContainText('47 XP')
  await captureEvidence(page, 'docker-full-demo', '13-final-participant-reconciliation.png')

  await page.reload()
  await expect(page.locator('.reporting-stats article').filter({ hasText: 'Total XP' })).toContainText('47')
  await page.getByRole('button', { name: 'Leaderboard' }).click()
  await expect(page.getByRole('row').filter({ hasText: me.displayName })).toContainText('47 XP')
  await page.getByRole('button', { name: 'My activity' }).click()
  await expect(page.getByRole('heading', { name: taskName }).locator('xpath=ancestor::article[1]')).toContainText('Approved')

  await switchIdentity(page, 'manager')
  await page.getByRole('navigation', { name: 'Primary navigation' }).getByRole('button', { name: 'Scoresheet', exact: true }).click()
  await page.getByLabel('Scoresheet reporting cycle').selectOption(cycleId)
  tableRow = scoresheetRow(page)
  await expect(tableRow.getByRole('cell').nth(2)).toHaveText('20')
  await expect(tableRow.getByRole('cell').nth(3)).toHaveText('20')
  await expect(tableRow.getByRole('cell').nth(4)).toHaveText('7')
  await expect(tableRow.getByRole('cell').nth(5)).toHaveText('-5')
  await expect(tableRow.getByRole('cell').nth(6)).toHaveText('47')
  await page.getByRole('navigation', { name: 'Primary navigation' }).getByRole('button', { name: 'Raid Administration', exact: true }).click()
  await page.getByLabel('Reporting cycle').selectOption(cycleId)
  await page.getByRole('button', { name: new RegExp(raidName) }).click()
  await expect(participantRaidCard(page)).toContainText('Participation: Physical')
  await captureEvidence(page, 'docker-full-demo', '14-refresh-persistence-final.png')

  async function summaryRowAfterManagerSwitch() {
    await switchIdentity(page, 'manager')
    return summaryRow(page, me.participantId)
  }
})
