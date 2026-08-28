import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ParticipantReportingArea } from './ParticipantReportingViews'
import { ReportingApiError, type ReportingApi } from './reportingApi'
import type { Dashboard, ParticipantTeam, ReportingCycles, XpActivityItem } from './types'

afterEach(() => { cleanup(); vi.restoreAllMocks() })

const cycles: ReportingCycles = { defaultCycleId: 'aug', cycles: [
  { id: 'aug', code: 'AUG26', name: 'August Quest', status: 'Active', startsAt: '2026-08-01T00:00:00Z', endsAt: '2026-08-31T00:00:00Z', participantStatus: 'Active' },
  { id: 'jul', code: 'JUL26', name: 'July Quest', status: 'Finalised', startsAt: '2026-07-01T00:00:00Z', endsAt: '2026-07-31T00:00:00Z', participantStatus: 'Active' },
] }
const activity = (id: string, amount: number, entryType: XpActivityItem['entryType'] = 'Grant'): XpActivityItem => ({
  id, amount, entryType, sourceType: 'ManualAward', reason: `${entryType} reason`, awardedAt: '2026-08-20T01:00:00Z', reversesEntryId: entryType === 'Grant' ? null : 'grant-1',
  source: { label: 'Early Bird', challengeId: null, challengeName: null, taskId: null, taskName: null, awardCategoryId: 'award-1', awardCategoryName: 'Early Bird', raidSessionId: null, raidSessionName: null },
})
const dashboard: Dashboard = {
  cycle: cycles.cycles[0], participant: { participantId: 'p1', displayName: 'Avery Demo' }, totalXp: 10, individualRank: 2, eligibleChallengeCount: 3,
  submissionStatusCounts: { NeedsEvidence: 1, Submitted: 2, UnderReview: 1, Approved: 4 },
  raidPassBalance: [{ passType: 'Physical', assigned: 2, used: 1, remaining: 1 }], recentActivity: [activity('a1', 10)],
}
const team: ParticipantTeam = { team: { cycleTeamId: 'team-1', name: 'Quest Builders', members: [
  { participantId: 'p1', displayName: 'Avery Demo', isCurrentParticipant: true, joinedAt: '2026-08-01T00:00:00Z' },
  { participantId: 'p2', displayName: 'Blake Demo', isCurrentParticipant: false, joinedAt: '2026-08-01T00:00:00Z' },
] }, challengeGroups: [{ participationId: 'group-1', challengeId: 'challenge-1', challengeName: 'Prompt Quest', challengeStatus: 'Open', members: [
  { participantId: 'p1', displayName: 'Avery Demo', isCurrentParticipant: true, joinedAt: '2026-08-05T00:00:00Z' },
  { participantId: 'p3', displayName: 'Casey Demo', isCurrentParticipant: false, joinedAt: '2026-08-05T00:00:00Z' },
] }] }

function api(overrides: Partial<ReportingApi> = {}): ReportingApi {
  return {
    getReportingCycles: vi.fn().mockResolvedValue(cycles), getDashboard: vi.fn().mockResolvedValue(dashboard), getTeam: vi.fn().mockResolvedValue(team),
    getIndividualLeaderboard: vi.fn().mockResolvedValue([{ rank: 1, participantId: 'p2', displayName: 'Blake Demo', totalXp: 10, isCurrentParticipant: false }, { rank: 1, participantId: 'p1', displayName: 'Avery Demo', totalXp: 10, isCurrentParticipant: true }, { rank: 3, participantId: 'p3', displayName: 'Casey Demo', totalXp: 0, isCurrentParticipant: false }]),
    getXpActivity: vi.fn().mockResolvedValue({ items: [activity('a1', 10), activity('a2', -20, 'Reversal')], nextCursor: null }), ...overrides,
  }
}

describe('participant reporting surfaces', () => {
  it('uses defaultCycleId and reloads the active surface when the reporting cycle changes', async () => {
    const service = api(); const user = userEvent.setup()
    render(<ParticipantReportingArea page="dashboard" api={service} onNavigate={vi.fn()} />)
    await waitFor(() => expect(service.getDashboard).toHaveBeenCalledWith('aug'))
    expect(screen.getByLabelText('Reporting cycle')).toHaveValue('aug')
    await user.selectOptions(screen.getByLabelText('Reporting cycle'), 'jul')
    await waitFor(() => expect(service.getDashboard).toHaveBeenCalledWith('jul'))
    expect(screen.getByText(/reporting views only/)).toBeInTheDocument()
  })

  it('shows a legitimate no-cycle empty state without loading a surface', async () => {
    const service = api({ getReportingCycles: vi.fn().mockResolvedValue({ defaultCycleId: null, cycles: [] }) })
    render(<ParticipantReportingArea page="dashboard" api={service} onNavigate={vi.fn()} />)
    expect(await screen.findByText(/do not have any reporting cycles yet/)).toBeInTheDocument()
    expect(service.getDashboard).not.toHaveBeenCalled()
  })

  it('shows dashboard XP, rank, actionable statuses, eligible challenges and raid passes separately from XP', async () => {
    const navigate = vi.fn(); const user = userEvent.setup()
    render(<ParticipantReportingArea page="dashboard" api={api()} onNavigate={navigate} />)
    expect(await screen.findByRole('heading', { name: 'Welcome, Avery Demo' })).toBeInTheDocument()
    const total = screen.getByText('Total XP').closest('article')!
    expect(within(total).getByText('10')).toBeInTheDocument()
    expect(screen.getByText('#2')).toBeInTheDocument()
    expect(screen.getByText('3')).toBeInTheDocument()
    expect(screen.getByText('Needs evidence')).toBeInTheDocument()
    const raid = screen.getByRole('heading', { name: 'Raid passes' }).closest('section')!
    expect(within(raid).getByText('NON-XP RESOURCE')).toBeInTheDocument()
    expect(within(raid).getByText('2 assigned · 1 used')).toBeInTheDocument()
    expect(within(raid).getByText('Not included in Total XP')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: /Needs evidence/ }))
    expect(navigate).toHaveBeenCalledWith('activity')
  })

  it('renders a server-returned dashboard zero XP value normally', async () => {
    render(<ParticipantReportingArea page="dashboard" api={api({ getDashboard: vi.fn().mockResolvedValue({ ...dashboard, totalXp: 0, individualRank: null }) })} onNavigate={vi.fn()} />)
    const total = (await screen.findByText('Total XP')).closest('article')!
    expect(within(total).getByText('0')).toBeInTheDocument()
    expect(screen.getByText('Not ranked')).toBeInTheDocument()
  })

  it('keeps My Cycle Team and Challenge Groups visibly separate without team scoring UI', async () => {
    render(<ParticipantReportingArea page="team" api={api()} onNavigate={vi.fn()} />)
    expect(await screen.findByText('MY CYCLE TEAM')).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Quest Builders' })).toBeInTheDocument()
    expect(screen.getByText('CHALLENGE GROUPS')).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Prompt Quest' })).toBeInTheDocument()
    expect(screen.getAllByText('You').length).toBeGreaterThan(0)
    expect(screen.queryByText(/team XP|team rank|team leaderboard/i)).not.toBeInTheDocument()
  })

  it('handles team:null and no challenge groups independently', async () => {
    render(<ParticipantReportingArea page="team" api={api({ getTeam: vi.fn().mockResolvedValue({ team: null, challengeGroups: [] }) })} onNavigate={vi.fn()} />)
    expect(await screen.findByRole('heading', { name: 'No cycle team assigned' })).toBeInTheDocument()
    expect(screen.getByText(/do not have an open cycle-team assignment/)).toBeInTheDocument()
    expect(screen.getByText(/No challenge groups/)).toBeInTheDocument()
  })

  it('preserves API competition ranks, highlights current participant, and renders zero XP', async () => {
    render(<ParticipantReportingArea page="leaderboard" api={api()} onNavigate={vi.fn()} />)
    const table = await screen.findByRole('table')
    const rows = within(table).getAllByRole('row').slice(1)
    expect(rows.map((row) => within(row).getAllByRole('cell')[0].textContent)).toEqual(['1', '1', '3'])
    expect(within(rows[1]).getByText('You')).toBeInTheDocument()
    expect(within(rows[2]).getByText('0 XP')).toBeInTheDocument()
  })

  it('renders signed ledger amounts and API source labels as separate rows', async () => {
    render(<ParticipantReportingArea page="xp-activity" api={api()} onNavigate={vi.fn()} />)
    expect(await screen.findByText('+10 XP')).toBeInTheDocument()
    expect(screen.getByText('−20 XP')).toBeInTheDocument()
    expect(screen.getAllByRole('heading', { name: 'Early Bird' })).toHaveLength(2)
    expect(screen.getByText(/Manual Award · Reversal ·/)).toBeInTheDocument()
  })

  it('shows friendly server source provenance without losing Manual Award details or adjustment semantics', async () => {
    const manual = activity('manual', 10)
    const task = { ...activity('task', 25, 'Correction'), sourceType: 'TaskApproval' as const, source: { ...manual.source, label: 'Prompt Quest · Prompt task' } }
    const raid = { ...activity('raid', 5, 'Grant'), sourceType: 'Raid' as const, source: { ...manual.source, label: 'Raid Session 1' } }
    render(<ParticipantReportingArea page="xp-activity" api={api({ getXpActivity: vi.fn().mockResolvedValue({ items: [manual, task, raid], nextCursor: null }) })} onNavigate={vi.fn()} />)
    expect(await screen.findByText('Manual Award · Grant ·', { exact: false })).toBeInTheDocument()
    expect(screen.getByText('Task Approval · Correction ·', { exact: false })).toBeInTheDocument()
    expect(screen.getByText('Raid · Grant ·', { exact: false })).toBeInTheDocument()
    expect(screen.getByText('+10 XP')).toBeInTheDocument()
    const manualRow = screen.getByRole('heading', { name: 'Early Bird' }).closest('article')!
    expect(within(manualRow).getByText('Grant reason')).toBeInTheDocument()
    expect(screen.queryByText(/^Grant ·/)).not.toBeInTheDocument()
  })

  it('loads the next activity page using nextCursor', async () => {
    const service = api({ getXpActivity: vi.fn().mockResolvedValueOnce({ items: [activity('a1', 10)], nextCursor: 'cursor-2' }).mockResolvedValueOnce({ items: [activity('a2', 5, 'Correction')], nextCursor: null }) }); const user = userEvent.setup()
    render(<ParticipantReportingArea page="xp-activity" api={service} onNavigate={vi.fn()} />)
    await user.click(await screen.findByRole('button', { name: 'Load more' }))
    await screen.findByText('+5 XP')
    expect(service.getXpActivity).toHaveBeenLastCalledWith('aug', 'cursor-2')
    expect(screen.getByText('+10 XP')).toBeInTheDocument()
  })

  it('retains loaded activity and offers retry when a later page fails', async () => {
    const service = api({ getXpActivity: vi.fn().mockResolvedValueOnce({ items: [activity('a1', 10)], nextCursor: 'cursor-2' }).mockRejectedValueOnce(new ReportingApiError(503)) }); const user = userEvent.setup()
    render(<ParticipantReportingArea page="xp-activity" api={service} onNavigate={vi.fn()} />)
    await user.click(await screen.findByRole('button', { name: 'Load more' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('temporarily unavailable')
    expect(screen.getByText('+10 XP')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument()
  })

  it('supports loading, empty, and API error with retry states', async () => {
    const pending = new Promise<Dashboard>(() => undefined)
    const view = render(<ParticipantReportingArea page="dashboard" api={api({ getDashboard: vi.fn().mockReturnValue(pending) })} onNavigate={vi.fn()} />)
    expect(await screen.findByText('Loading dashboard…')).toBeInTheDocument()
    view.unmount()
    render(<ParticipantReportingArea page="leaderboard" api={api({ getIndividualLeaderboard: vi.fn().mockResolvedValue([]) })} onNavigate={vi.fn()} />)
    expect(await screen.findByText(/No active participants/)).toBeInTheDocument()
    cleanup()
    render(<ParticipantReportingArea page="dashboard" api={api({ getDashboard: vi.fn().mockRejectedValue(new ReportingApiError(403)) })} onNavigate={vi.fn()} />)
    expect(await screen.findByRole('alert')).toHaveTextContent('participant role')
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument()
  })
})
