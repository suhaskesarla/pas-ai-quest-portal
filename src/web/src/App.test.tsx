import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { App } from './App'
import { AuthProvider } from './auth/AuthContext'
import { AuthApiError, type AuthApi } from './auth/authApi'
import type { CurrentUser } from './auth/types'
import type { ReportingApi } from './reporting/reportingApi'

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

function renderApp(api: AuthApi, demoModeAvailable = true) {
  return render(<AuthProvider api={api} demoModeAvailable={demoModeAvailable}><App reports={fakeReports()} /></AuthProvider>)
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
    await screen.findByRole('heading', { name: 'Welcome, Morgan Demo' })
    expect(api.establishDemoSession).toHaveBeenCalledWith('manager')
    expect(api.getCurrentUser).toHaveBeenCalledTimes(2)
    expect(screen.getByRole('heading', { name: 'Dashboard' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Review queue' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Submit work' })).not.toBeInTheDocument()
  })

  it('uses API-confirmed roles instead of the selected profile label', async () => {
    const api = fakeApi({ getCurrentUser: vi.fn().mockResolvedValue(participant) })
    const user = userEvent.setup()
    renderApp(api)
    await screen.findByRole('heading', { name: 'Welcome, Avery Demo' })
    await user.selectOptions(screen.getByRole('combobox', { name: 'Demo identity' }), 'manager')
    await waitFor(() => expect(api.getCurrentUser).toHaveBeenCalledTimes(2))
    expect(screen.queryByRole('button', { name: 'Review queue' })).not.toBeInTheDocument()
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
    expect(within(screen.getByRole('navigation')).queryByText('Review queue')).not.toBeInTheDocument()
  })
})
