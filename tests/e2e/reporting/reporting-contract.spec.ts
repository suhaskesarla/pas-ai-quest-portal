import { expect, test, type Page } from '@playwright/test'

async function participant(page: Page) {
  await page.goto('/')
  await page.getByRole('combobox', { name: 'Demo identity' }).selectOption('participant')
}

test('DETERMINISTIC_DEMO_SEED_REQUIRED reporting smoke reconciles identity total and Manual Award provenance', async ({ page }) => {
  await participant(page)
  const cycles = await (await page.request.get('/api/participant/reporting-cycles')).json() as { defaultCycleId: string | null }
  expect(cycles.defaultCycleId, 'deterministic Demo bootstrap must enroll the profile in a reporting cycle').not.toBeNull()
  const cycleId = cycles.defaultCycleId!
  const dashboardResponse = await page.request.get(`/api/participant/dashboard?cycleId=${cycleId}`)
  const leaderboardResponse = await page.request.get(`/api/leaderboards/individual?cycleId=${cycleId}`)
  expect([dashboardResponse.status(), leaderboardResponse.status()]).toEqual([200, 200])
  const dashboard = await dashboardResponse.json() as { totalXp: number; raidPassBalance: unknown }
  type ActivityItem = { id: string; amount: number; sourceType: string; entryType: string; reason: string; source: { label: string; awardCategoryId?: string | null } }
  const seededXpEntryId = '60000000-0000-4000-8000-00000000000a'
  const seenCursors = new Set<string>()
  let cursor: string | null = null
  let seededManualAward: ActivityItem | undefined
  do {
    const cursorQuery = cursor === null ? '' : `&cursor=${encodeURIComponent(cursor)}`
    const activityResponse = await page.request.get(`/api/participant/xp-activity?cycleId=${cycleId}&limit=25${cursorQuery}`)
    expect(activityResponse.status(), `XP Activity page failed for cursor ${cursor ?? '<first-page>'}`).toBe(200)
    const activity = await activityResponse.json() as { items: ActivityItem[]; nextCursor: string | null }
    expect(activity.items.every(item => ['TaskApproval', 'ManualAward', 'Raid'].includes(item.sourceType))).toBe(true)
    expect(activity.items.every(item => ['Grant', 'Reversal', 'Correction'].includes(item.entryType))).toBe(true)
    seededManualAward = activity.items.find(item => item.id === seededXpEntryId)
    if (seededManualAward || activity.nextCursor === null) break
    if (seenCursors.has(activity.nextCursor))
      throw new Error(`XP Activity returned a repeated nextCursor before seeded XPEntry ${seededXpEntryId} was found: ${activity.nextCursor}`)
    seenCursors.add(activity.nextCursor)
    cursor = activity.nextCursor
  } while (true)
  const leaderboard = await leaderboardResponse.json() as { participantId: string; rank: number; totalXp: number }[]
  const me = await (await page.request.get('/api/auth/me')).json() as { participantId: string }
  expect(leaderboard.find(row => row.participantId === me.participantId)?.totalXp).toBe(dashboard.totalXp)
  expect(seededManualAward, `seeded XPEntry ${seededXpEntryId} must exist before XP Activity pagination is exhausted`).toMatchObject({
    id: seededXpEntryId,
    sourceType: 'ManualAward',
    entryType: 'Grant',
    amount: 10,
    reason: 'Synthetic local-development showcase award',
    source: {
      label: 'Synthetic Welcome Award',
      awardCategoryId: '60000000-0000-4000-8000-000000000009',
    },
  })
  expect(dashboard.raidPassBalance).toBeTruthy()
})

test.fixme('competition ranking fixture produces 1,2,2,4 and includes Active zero-XP participant', async () => {
  // Owned by SQL ParticipantReportingTests until an isolated browser/API data factory exists.
})

test.fixme('Finalised historical cycle remains selectable without affecting challenge eligibility', async () => {
  // Requires isolated enrolled historical-cycle data; selection is reporting context only.
})

// This file is a seed-contract smoke, not exhaustive reporting coverage. Pagination, status filtering,
// cycle isolation and competition ranking remain owned by focused SQL/API and frontend tests until an
// isolated browser data factory exists. Unified signed-source arithmetic is specified separately.
