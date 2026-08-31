import { expect, test, type APIResponse, type Page } from '@playwright/test'

type Capture = { captureId: string; eventType: string; destinationClass: string; destinationKey: string; title: string; body: string; actionUrl: string }
const json = async <T>(response: APIResponse) => { expect(response.ok(), await response.text()).toBeTruthy(); return response.json() as Promise<T> }
const profile = async (page: Page, profileKey: 'manager' | 'participant') => {
  const response = await page.request.post('/api/auth/demo/session', {
    data: { profileKey },
    headers: { Origin: 'http://localhost:5173' },
  })
  expect(response.ok(), await response.text()).toBeTruthy()
}
const captures = async (page: Page) => (await json<{ notifications: Capture[] }>(await page.request.get('/api/dev/notifications/captured'))).notifications
const waitForCapture = async (page: Page, eventType: string, bodyText: string, baseline: number) => {
  let found: Capture | undefined
  await expect.poll(async () => { const rows = await captures(page); found = rows.slice(baseline).find(x => x.eventType === eventType && x.body.includes(bodyText)); return found }, { timeout: 30_000 }).toBeTruthy()
  return found!
}

test('all BA-017 events reach Capture with server-owned privacy-safe routing', async ({ page }) => {
  await page.goto('/'); const baseline = (await captures(page)).length; const suffix = Date.now().toString(); const name = `Synthetic Teams Quest ${suffix}`
  await profile(page, 'manager')
  const cycles = await json<{ defaultCycleId: string }>(await page.request.get('/api/manager/reporting-cycles')); const cycleId = cycles.defaultCycleId
  const now = Date.now(); const draft = await json<any>(await page.request.post('/api/manager/challenges', { data: {
    cycleId, name, description: 'Synthetic notification-safe challenge.', category: 'Synthetic', openAt: new Date(now - 86_400_000).toISOString(), dueAt: new Date(now + 5 * 86_400_000).toISOString(), closeAt: new Date(now + 7 * 86_400_000).toISOString(), heroImageReference: null,
    tasks: [{ id: null, name: 'Notify task', description: null, xp: 25, scoringMode: 'Individual', evidenceRequirement: 'Text', sortOrder: 1 }], participationPolicy: null,
  } })); const published = await json<any>(await page.request.post(`/api/manager/challenges/${draft.id}/publish`, { data: { version: draft.version } }))
  const challengeCapture = await waitForCapture(page, 'ChallengePublished', name, baseline); expect(challengeCapture.destinationKey).toBe('QUEST_GENERAL_AUDIENCE'); expect(challengeCapture.actionUrl).toContain(`/challenges/${draft.id}`)

  await profile(page, 'participant'); const me = await json<{ participantId: string }>(await page.request.get('/api/auth/me')); const eligible = await json<any[]>(await page.request.get('/api/challenges/eligible')); const challenge = eligible.find(x => x.id === draft.id); expect(challenge).toBeTruthy(); const task = challenge.tasks[0]
  const submission = await json<any>(await page.request.post('/api/submissions', { data: { challengeId: draft.id, taskId: task.id, challengeParticipationId: null, beneficiaryIds: [me.participantId], evidence: [{ kind: 'Text', label: 'Evidence', value: 'private evidence must not notify' }], comment: 'Submit' } }))
  const submittedCapture = await waitForCapture(page, 'SubmissionSubmitted', name, baseline); expect(submittedCapture.destinationKey).toBe('QUEST_MANAGER_AUDIENCE'); expect(submittedCapture.body).not.toContain('private evidence')

  await profile(page, 'manager'); const needs = await json<any>(await page.request.post(`/api/submissions/${submission.id}/review`, { data: { version: submission.version, action: 'NeedsEvidence', comment: 'Please clarify the outcome.' } }))
  const needsCapture = await waitForCapture(page, 'SubmissionNeedsEvidence', name, baseline); expect(needsCapture.destinationKey).toBe(`participant:${me.participantId.replaceAll('-', '')}`); expect(needsCapture.body).toContain('Please clarify')

  await profile(page, 'participant'); const resubmitted = await json<any>(await page.request.put(`/api/submissions/${submission.id}/resubmission`, { data: { version: needs.version, evidence: [{ kind: 'Text', label: 'Evidence', value: 'revised private evidence' }], comment: 'Updated' } }))
  const resubmitCapture = await waitForCapture(page, 'SubmissionResubmitted', name, baseline); expect(resubmitCapture.destinationKey).toBe('QUEST_MANAGER_AUDIENCE'); expect(resubmitCapture.body).not.toContain('revised private evidence')

  await profile(page, 'manager'); const approved = await json<any>(await page.request.post(`/api/submissions/${submission.id}/review`, { data: { version: resubmitted.version, action: 'Approve', comment: 'Approved' } })); expect(approved.status).toBe('Approved')
  const approvedCapture = await waitForCapture(page, 'SubmissionApproved', name, baseline); expect(approvedCapture.destinationKey).toBe(`participant:${me.participantId.replaceAll('-', '')}`); expect(approvedCapture.body).toContain('25 XP')

  await profile(page, 'participant'); const rejectedSubmission = await json<any>(await page.request.post('/api/submissions', { data: { challengeId: draft.id, taskId: task.id, challengeParticipationId: null, beneficiaryIds: [me.participantId], evidence: [{ kind: 'Text', label: 'Evidence', value: 'second private evidence' }], comment: null } }))
  await profile(page, 'manager'); await json(await page.request.post(`/api/submissions/${rejectedSubmission.id}/review`, { data: { version: rejectedSubmission.version, action: 'Reject', comment: 'No XP for this attempt.' } }))
  const rejectedCapture = await waitForCapture(page, 'SubmissionRejected', name, baseline); expect(rejectedCapture.destinationKey).toBe(`participant:${me.participantId.replaceAll('-', '')}`); expect(rejectedCapture.body).toContain('No XP')

  const announcement = await page.request.post(`/api/manager/leaderboards/individual/${cycleId}/teams-announcements`, { data: { requestId: crypto.randomUUID() } }); expect(announcement.status()).toBe(202)
  const leaderboardCapture = await waitForCapture(page, 'LeaderboardAnnouncement', 'Synthetic Showcase Cycle', baseline); expect(leaderboardCapture.destinationKey).toBe('QUEST_GENERAL_AUDIENCE'); expect(leaderboardCapture.actionUrl).toContain(`/leaderboard?cycleId=${cycleId}`)
  const expectedCaptureIds = new Set([challengeCapture, submittedCapture, needsCapture, resubmitCapture, approvedCapture, rejectedCapture, leaderboardCapture].map(x => x.captureId))
  const relevant = (await captures(page)).slice(baseline).filter(x => expectedCaptureIds.has(x.captureId)); expect(relevant).toHaveLength(7)
})
