import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { App } from './App'
import { AuthProvider } from './auth/AuthContext'
import { AuthApiError, type AuthApi } from './auth/authApi'
import type { CurrentUser } from './auth/types'
import type { ReportingApi } from './reporting/reportingApi'
import type { ChallengeAdminApi } from './challenge-admin/challengeAdminApi'
import type { ScoresheetApi } from './scoresheet/scoresheetApi'
import type { CycleAdminApi } from './cycle-admin/cycleAdminApi'
import { WorkflowApiError, type WorkflowApi } from './workflow/workflowApi'

const participant: CurrentUser = { isAuthenticated: true, participantId: 'p-demo', displayName: 'Avery Demo', roles: ['Quest.Participant'] }
const manager: CurrentUser = { isAuthenticated: true, participantId: 'm-demo', displayName: 'Morgan Demo', roles: ['Quest.Manager'] }
const profiles = [{ key: 'participant', label: 'Avery Demo — Participant' }, { key: 'manager', label: 'Morgan Demo — Manager' }]

afterEach(cleanup)

function fakeApi(overrides: Partial<AuthApi> = {}): AuthApi {
  return {
    getCurrentUser: vi.fn().mockResolvedValue(participant),
    getDemoProfiles: vi.fn().mockResolvedValue(profiles),
    establishDemoSession: vi.fn().mockResolvedValue(undefined),
    clearDemoSession: vi.fn().mockResolvedValue(undefined),
    ...overrides,
  }
}

function fakeReports(): ReportingApi {
  const cycle = { id: 'cycle-1', code: 'AUG26', name: 'August Quest', status: 'Active' as const, startsAt: '2026-08-01T00:00:00Z', endsAt: '2026-08-31T00:00:00Z', participantStatus: 'Active' as const }
  return {
    getReportingCycles: vi.fn().mockResolvedValue({ defaultCycleId: cycle.id, cycles: [cycle] }),
    getDashboard: vi.fn().mockResolvedValue({ cycle, participant: { participantId: 'p-demo', displayName: 'Avery Demo' }, totalXp: 10, individualRank: 1, eligibleChallengeCount: 1, submissionStatusCounts: {}, raidPassBalance: [], recentActivity: [] }),
    getTeam: vi.fn().mockResolvedValue({ team: null, challengeGroups: [] }), getIndividualLeaderboard: vi.fn().mockResolvedValue([]), getXpActivity: vi.fn().mockResolvedValue({ items: [], nextCursor: null }),
  }
}

function fakeWorkflow(queue: Promise<unknown[]> | unknown[] = []): WorkflowApi {
  return { getEligibleChallenges: vi.fn().mockResolvedValue([]), getMySubmissions: vi.fn().mockResolvedValue([]), createSubmission: vi.fn(), resubmit: vi.fn(), getReviewQueue: vi.fn().mockImplementation(() => Promise.resolve(queue).then((value) => value)), review: vi.fn() } as unknown as WorkflowApi
}

function fakeChallengeAdmin(): ChallengeAdminApi {
  return { getOptions: vi.fn().mockResolvedValue({ cycles: [], scoringModes: [], evidenceRequirements: [], formationModes: [] }), getChallenges: vi.fn().mockResolvedValue([]), getChallenge: vi.fn(), createChallenge: vi.fn(), updateChallenge: vi.fn(), publishChallenge: vi.fn() } as unknown as ChallengeAdminApi
}

function fakeScoresheet(): ScoresheetApi {
  const cycle = { id: 'cycle-1', code: 'AUG26', name: 'August Quest', status: 'Active' as const, startsAt: '2026-08-01T00:00:00Z', endsAt: '2026-08-31T00:00:00Z' }
  return { getReportingCycles: vi.fn().mockResolvedValue({ defaultCycleId: cycle.id, cycles: [cycle] }), getScoresheet: vi.fn().mockResolvedValue({ cycle, rows: [] }), getParticipant: vi.fn(), correctXp: vi.fn(), getManualAwardOptions: vi.fn().mockResolvedValue({ cycle, participants: [], categories: [] }), createManualAward: vi.fn() } as unknown as ScoresheetApi
}
function fakeCycleAdmin(): CycleAdminApi { return { getCycles: vi.fn().mockResolvedValue({ cycles: [] }), getCycle: vi.fn(), getParticipantOptions: vi.fn(), createCycle: vi.fn(), updateCycle: vi.fn(), transition: vi.fn(), enroll: vi.fn(), changeStatus: vi.fn() } as unknown as CycleAdminApi }

function renderApp(api: AuthApi, demoModeAvailable = true, app: { workflow?: WorkflowApi; challengeAdmin?: ChallengeAdminApi; scoresheet?: ScoresheetApi; cycleAdmin?: CycleAdminApi } = {}) {
  return render(<AuthProvider api={api} demoModeAvailable={demoModeAvailable}><App api={app.workflow} challengeAdmin={app.challengeAdmin} scoresheet={app.scoresheet} cycleAdmin={app.cycleAdmin ?? fakeCycleAdmin()} reports={fakeReports()} /></AuthProvider>)
}

describe('Step 5A authentication shell', () => {
  it('loads safe demo profiles and displays the demo indicator', async () => {
    const api = fakeApi()
    renderApp(api)
    expect(await screen.findByRole('option', { name: 'Avery Demo — Participant' })).toBeInTheDocument()
    expect(api.getDemoProfiles).toHaveBeenCalledOnce()
    expect(screen.getByText('DEVELOPMENT · DEMO AUTH ACTIVE')).toBeInTheDocument()
  })

  it('switches Participant to Manager, refreshes me, and returns to Dashboard', async () => {
    const api = fakeApi({ getCurrentUser: vi.fn().mockResolvedValueOnce(participant).mockResolvedValueOnce(manager) })
    const user = userEvent.setup()
    renderApp(api)
    await screen.findByRole('heading', { name: 'Welcome, Avery Demo' })
    await user.click(screen.getByRole('button', { name: 'My activity' }))
    await user.selectOptions(screen.getByRole('combobox', { name: 'Demo identity' }), 'manager')
    await screen.findByRole('heading', { name: 'Manager Dashboard' })
    expect(api.establishDemoSession).toHaveBeenCalledWith('manager')
    expect(api.getCurrentUser).toHaveBeenCalledTimes(2)
    expect(screen.getByRole('heading', { name: 'Dashboard' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Review Queue' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Submit work' })).not.toBeInTheDocument()
  })

  it('uses API-confirmed roles instead of the selected profile label', async () => {
    const api = fakeApi({ getCurrentUser: vi.fn().mockResolvedValue(participant) })
    const user = userEvent.setup()
    renderApp(api)
    await screen.findByRole('heading', { name: 'Welcome, Avery Demo' })
    await user.selectOptions(screen.getByRole('combobox', { name: 'Demo identity' }), 'manager')
    await waitFor(() => expect(api.getCurrentUser).toHaveBeenCalledTimes(2))
    expect(screen.queryByRole('button', { name: 'Review Queue' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Submit work' })).toBeInTheDocument()
  })

  it('retains the confirmed identity and page when a switch is forbidden, with Retry', async () => {
    const api = fakeApi({ establishDemoSession: vi.fn().mockRejectedValue(new AuthApiError(403)) })
    const user = userEvent.setup()
    renderApp(api)
    await screen.findByRole('heading', { name: 'Welcome, Avery Demo' })
    await user.click(screen.getByRole('button', { name: 'My activity' }))
    await user.selectOptions(screen.getByRole('combobox', { name: 'Demo identity' }), 'manager')
    expect(await screen.findByText('You do not have permission to use that identity.')).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'My activity' })).toBeInTheDocument()
    expect(screen.getAllByText('Avery Demo').length).toBeGreaterThan(0)
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument()
  })

  it('shows an unauthenticated state for an unauthenticated me response', async () => {
    renderApp(fakeApi({ getCurrentUser: vi.fn().mockResolvedValue({ isAuthenticated: false, participantId: null, displayName: null, roles: [] }) }))
    expect(await screen.findByRole('heading', { name: 'You are not signed in' })).toBeInTheDocument()
  })

  it('handles a 401 from me as unauthenticated', async () => {
    renderApp(fakeApi({ getCurrentUser: vi.fn().mockRejectedValue(new AuthApiError(401)) }))
    expect(await screen.findByRole('heading', { name: 'You are not signed in' })).toBeInTheDocument()
    expect(screen.queryByText(/temporarily unavailable/i)).not.toBeInTheDocument()
  })

  it('handles a 403 from me with a clean access error', async () => {
    renderApp(fakeApi({ getCurrentUser: vi.fn().mockRejectedValue(new AuthApiError(403)) }), false)
    expect(await screen.findByRole('heading', { name: 'We could not confirm your identity' })).toBeInTheDocument()
    expect(screen.getByText('You do not have permission to use that identity.')).toBeInTheDocument()
  })

  it('does not render or load the selector when demo mode is unavailable', async () => {
    const api = fakeApi()
    renderApp(api, false)
    await screen.findByRole('heading', { name: 'Welcome, Avery Demo' })
    expect(screen.queryByText('Demo authentication')).not.toBeInTheDocument()
    expect(api.getDemoProfiles).not.toHaveBeenCalled()
    expect(within(screen.getByRole('navigation')).queryByText('Review Queue')).not.toBeInTheDocument()
  })
})

describe('manager navigation and dashboard', () => {
  const managerApp = (workflow = fakeWorkflow()) => renderApp(fakeApi({ getCurrentUser: vi.fn().mockResolvedValue(manager) }), false, { workflow, challengeAdmin: fakeChallengeAdmin(), scoresheet: fakeScoresheet() })

  it('adds the real Cycle Administration destination while keeping contextual/deferred actions out of navigation', async () => {
    managerApp(); const nav = await screen.findByRole('navigation'); expect(within(nav).getAllByRole('button').map((button) => button.textContent)).toEqual(['Dashboard', 'Challenges', 'Review Queue', 'Scoresheet', 'Cycle Administration']); for (const absent of ['New Challenge', 'Leaderboard', 'Analytics', 'Award XP', 'Correct XP', 'Raid Administration']) expect(within(nav).queryByRole('button', { name: absent })).not.toBeInTheDocument()
  })

  it('routes managers to Cycle Administration and does not expose it to participants', async () => { managerApp(); const user = userEvent.setup(); await user.click(await screen.findByRole('button', { name: 'Cycle Administration' })); expect(await screen.findByRole('heading', { name: 'Cycle Administration', level: 2 })).toBeInTheDocument(); cleanup(); renderApp(fakeApi(), false); await screen.findByRole('heading', { name: 'Welcome, Avery Demo' }); expect(within(screen.getByRole('navigation')).queryByRole('button', { name: 'Cycle Administration' })).not.toBeInTheDocument() })

  it('renders three manager action cards and navigates to each existing destination', async () => {
    managerApp(); const user = userEvent.setup(); expect(await screen.findByRole('heading', { name: 'Manager Dashboard' })).toBeInTheDocument(); expect(screen.getByText('Manage challenges, review participant submissions, and maintain the authoritative XP record.')).toBeInTheDocument(); expect(screen.getByRole('heading', { name: 'Manage Challenges' })).toBeInTheDocument(); expect(screen.getByRole('heading', { name: 'Review Submissions' })).toBeInTheDocument(); expect(screen.getByRole('heading', { name: 'Scoresheet & XP' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Manage challenges' })); expect(await screen.findByRole('heading', { name: 'Challenge Administration' })).toBeInTheDocument(); expect(screen.getByRole('button', { name: 'Create Challenge' })).toBeInTheDocument(); await user.click(screen.getByRole('button', { name: 'Dashboard' })); await user.click(screen.getByRole('button', { name: 'Open review queue' })); expect(await screen.findByText('All caught up—nothing is waiting for review.')).toBeInTheDocument(); await user.click(screen.getByRole('button', { name: 'Dashboard' })); await user.click(screen.getByRole('button', { name: 'View scoresheet' })); expect(await screen.findByRole('button', { name: 'Award XP' })).toBeInTheDocument()
  })

  it.each([[[], 'No submissions are waiting for review.'], [[{}], '1 submission waiting for review.'], [[{}, {}], '2 submissions waiting for review.']] as const)('renders the authoritative review count for %s', async (queue, message) => { managerApp(fakeWorkflow([...queue])); expect(await screen.findByText(message)).toBeInTheDocument() })

  it('shows loading without a false zero count', async () => {
    const pending = new Promise<unknown[]>(() => undefined); managerApp(fakeWorkflow(pending)); expect(await screen.findByText('Checking submissions awaiting review…')).toBeInTheDocument(); expect(screen.queryByText('No submissions are waiting for review.')).not.toBeInTheDocument()
  })

  it('distinguishes review failure from zero and keeps Review Queue accessible', async () => {
    const workflow = fakeWorkflow(); workflow.getReviewQueue = vi.fn().mockRejectedValue(new WorkflowApiError(503)); managerApp(workflow); const user = userEvent.setup(); expect(await screen.findByText('Review queue status is currently unavailable.')).toBeInTheDocument(); expect(screen.queryByText('No submissions are waiting for review.')).not.toBeInTheDocument(); expect(screen.getByRole('button', { name: 'Review Queue' })).toBeInTheDocument(); await user.click(screen.getByRole('button', { name: 'Open review queue' })); expect(await screen.findByRole('alert')).toBeInTheDocument()
  })
})
