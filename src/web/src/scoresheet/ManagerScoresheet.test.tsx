import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ManagerScoresheet } from './ManagerScoresheet'
import { ScoresheetApiError, type ScoresheetApi } from './scoresheetApi'
import type { ManagerReportingCycles, ScoresheetDetail, ScoresheetSummary } from './types'

afterEach(cleanup)
const cycles: ManagerReportingCycles = { defaultCycleId: 'aug', cycles: [
  { id: 'aug', code: 'AUG', name: 'August Quest', status: 'Active', startsAt: '2026-08-01T00:00:00Z', endsAt: '2026-08-31T00:00:00Z' },
  { id: 'jul', code: 'JUL', name: 'July Quest', status: 'Finalised', startsAt: '2026-07-01T00:00:00Z', endsAt: '2026-07-31T00:00:00Z' },
] }
const source = { label: 'Prompt task', challengeId: 'c1', challengeName: 'Prompt Quest', taskId: 't1', taskName: 'Prompt task', awardCategoryId: null, awardCategoryName: null, raidSessionId: null, raidSessionName: null }
const item = (id: string, amount: number) => ({ id, amount, entryType: amount < 0 ? 'Reversal' as const : 'Grant' as const, sourceType: 'TaskApproval' as const, reason: 'Manager decision', awardedAt: '2026-08-20T00:00:00Z', reversesEntryId: amount < 0 ? 'grant-1' : null, source })
const summary: ScoresheetSummary = { cycle: cycles.cycles[0], rows: [
  { participantId: 'p1', displayName: 'Avery Demo', participantStatus: 'Active', bySource: { taskApprovalXp: 25, manualAwardXp: 10, raidXp: 0 }, byEntryType: { grantXp: 35, reversalXp: -25, correctionXp: 0, netAdjustmentXp: -25 }, totalXp: 10 },
  { participantId: 'p2', displayName: 'Blake Demo', participantStatus: 'Withdrawn', bySource: { taskApprovalXp: 0, manualAwardXp: 0, raidXp: 0 }, byEntryType: { grantXp: 0, reversalXp: 0, correctionXp: 0, netAdjustmentXp: 0 }, totalXp: 0 },
  { participantId: 'p3', displayName: 'Casey Demo', participantStatus: 'Inactive', bySource: { taskApprovalXp: 0, manualAwardXp: 0, raidXp: 0 }, byEntryType: { grantXp: 0, reversalXp: 0, correctionXp: 0, netAdjustmentXp: 0 }, totalXp: 0 },
] }
const detail: ScoresheetDetail = { cycle: cycles.cycles[0], participant: { participantId: 'p1', displayName: 'Avery Demo', participantStatus: 'Active' }, bySource: summary.rows[0].bySource, byEntryType: summary.rows[0].byEntryType, totalXp: 10, items: [item('grant-1', 25), item('reverse-1', -25)], nextCursor: null }
function api(overrides: Partial<ScoresheetApi> = {}): ScoresheetApi { return { getReportingCycles: vi.fn().mockResolvedValue(cycles), getScoresheet: vi.fn().mockResolvedValue(summary), getParticipant: vi.fn().mockResolvedValue(detail), ...overrides } }
function deferred<T>() { let resolve!: (value: T) => void; let reject!: (reason?: unknown) => void; const promise = new Promise<T>((pass, fail) => { resolve = pass; reject = fail }); return { promise, resolve, reject } }

describe('manager Scoresheet', () => {
  it('uses the default cycle, switches cycles, and renders every status and API total including zero', async () => {
    const service = api(); const user = userEvent.setup(); render(<ManagerScoresheet api={service} />)
    const table = await screen.findByRole('table'); expect(service.getScoresheet).toHaveBeenCalledWith('aug')
    expect(within(table).getByText('Active')).toBeInTheDocument(); expect(within(table).getByText('Withdrawn')).toBeInTheDocument(); expect(within(table).getByText('Inactive')).toBeInTheDocument()
    const zeroRow = screen.getByText('Blake Demo').closest('tr')!; expect(within(zeroRow).getAllByText('0').length).toBeGreaterThan(0)
    expect(screen.getByText('-25')).toHaveClass('xp-negative'); expect(within(screen.getByText('Avery Demo').closest('tr')!).getAllByRole('cell').at(-1)).toHaveTextContent('10')
    await user.selectOptions(screen.getByLabelText('Scoresheet reporting cycle'), 'jul'); await waitFor(() => expect(service.getScoresheet).toHaveBeenCalledWith('jul'))
    expect(screen.queryByText(/rank|team XP|team leaderboard/i)).not.toBeInTheDocument()
  })

  it.each(['resolve', 'reject'] as const)('ignores a stale Cycle A %s after Cycle B succeeds', async (outcome) => {
    const cycleA = deferred<ScoresheetSummary>()
    const cycleB = deferred<ScoresheetSummary>()
    const julySummary: ScoresheetSummary = { ...summary, cycle: cycles.cycles[1], rows: [{ ...summary.rows[1], displayName: 'July Current Demo', totalXp: 77 }] }
    const service = api({ getScoresheet: vi.fn((cycleId: string) => cycleId === 'aug' ? cycleA.promise : cycleB.promise) })
    const user = userEvent.setup()
    render(<ManagerScoresheet api={service} />)
    expect(await screen.findByText('Loading Scoresheet…')).toBeInTheDocument()
    await user.selectOptions(screen.getByLabelText('Scoresheet reporting cycle'), 'jul')
    cycleB.resolve(julySummary)
    expect(await screen.findByRole('button', { name: 'July Current Demo' })).toBeInTheDocument()
    if (outcome === 'resolve') cycleA.resolve(summary)
    else cycleA.reject(new ScoresheetApiError(503, 'Old cycle failed.'))
    await waitFor(() => expect(service.getScoresheet).toHaveBeenCalledTimes(2))
    expect(screen.getByRole('button', { name: 'July Current Demo' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Avery Demo' })).not.toBeInTheDocument()
    expect(screen.queryByText('Loading Scoresheet…')).not.toBeInTheDocument()
    expect(screen.queryByText('Old cycle failed.')).not.toBeInTheDocument()
  })

  it('loads participant detail lazily with source labels, signed ledger and API summary total', async () => {
    const service = api(); const user = userEvent.setup(); render(<ManagerScoresheet api={service} />)
    expect(service.getParticipant).not.toHaveBeenCalled(); await user.click(await screen.findByRole('button', { name: 'Avery Demo' }))
    const panel = await screen.findByLabelText('Participant Scoresheet detail'); expect(service.getParticipant).toHaveBeenCalledWith('p1', 'aug', null)
    expect(within(panel).getByText('+25 XP')).toBeInTheDocument(); expect(within(panel).getByText('−25 XP')).toBeInTheDocument(); expect(within(panel).getAllByRole('heading', { name: 'Prompt task' })).toHaveLength(2); expect(within(panel).getByText(/Reverses grant-1/)).toBeInTheDocument()
    expect(within(within(panel).getByText('Total XP').closest('article')!).getByText('10')).toBeInTheDocument()
  })

  it('appends a successful page without duplicating ledger rows', async () => {
    const service = api({ getParticipant: vi.fn().mockResolvedValueOnce({ ...detail, items: [item('grant-1', 25)], nextCursor: 'next' }).mockResolvedValueOnce({ ...detail, items: [item('grant-1', 25), item('bonus-2', 10)], nextCursor: null }) }); const user = userEvent.setup(); render(<ManagerScoresheet api={service} />)
    await user.click(await screen.findByRole('button', { name: 'Avery Demo' })); await user.click(await screen.findByRole('button', { name: 'Load more' }))
    await screen.findByText('+10 XP'); expect(screen.getAllByText('+25 XP')).toHaveLength(1); expect(service.getParticipant).toHaveBeenLastCalledWith('p1', 'aug', 'next')
  })

  it('retains ledger entries and offers retry after a later page failure', async () => {
    const service = api({ getParticipant: vi.fn().mockResolvedValueOnce({ ...detail, items: [item('grant-1', 25)], nextCursor: 'next' }).mockRejectedValueOnce(new ScoresheetApiError(503)) }); const user = userEvent.setup(); render(<ManagerScoresheet api={service} />)
    await user.click(await screen.findByRole('button', { name: 'Avery Demo' })); await user.click(await screen.findByRole('button', { name: 'Load more' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('temporarily unavailable'); expect(screen.getByText('+25 XP')).toBeInTheDocument(); expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument()
  })

  it('supports empty roster and retryable summary errors', async () => {
    const view = render(<ManagerScoresheet api={api({ getScoresheet: vi.fn().mockResolvedValue({ ...summary, rows: [] }) })} />); expect(await screen.findByText(/No participants are enrolled/)).toBeInTheDocument(); view.unmount()
    render(<ManagerScoresheet api={api({ getScoresheet: vi.fn().mockRejectedValue(new ScoresheetApiError(403)) })} />); expect(await screen.findByRole('alert')).toHaveTextContent('manager role'); expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument()
  })
})
