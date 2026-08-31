import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
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
const item = (id: string, amount: number) => ({ id, amount, entryType: amount < 0 ? 'Reversal' as const : 'Grant' as const, sourceType: 'TaskApproval' as const, reason: 'Manager decision', awardedAt: '2026-08-20T00:00:00Z', reversesEntryId: amount < 0 ? 'grant-1' : null, source, correction: amount < 0 ? null : { currentEffectiveAmount: amount } })
const summary: ScoresheetSummary = { cycle: cycles.cycles[0], rows: [
  { participantId: 'p1', displayName: 'Avery Demo', participantStatus: 'Active', bySource: { taskApprovalXp: 25, manualAwardXp: 10, raidXp: 0 }, byEntryType: { grantXp: 35, reversalXp: -25, correctionXp: 0, netAdjustmentXp: -25 }, totalXp: 10 },
  { participantId: 'p2', displayName: 'Blake Demo', participantStatus: 'Withdrawn', bySource: { taskApprovalXp: 0, manualAwardXp: 0, raidXp: 0 }, byEntryType: { grantXp: 0, reversalXp: 0, correctionXp: 0, netAdjustmentXp: 0 }, totalXp: 0 },
  { participantId: 'p3', displayName: 'Casey Demo', participantStatus: 'Inactive', bySource: { taskApprovalXp: 0, manualAwardXp: 0, raidXp: 0 }, byEntryType: { grantXp: 0, reversalXp: 0, correctionXp: 0, netAdjustmentXp: 0 }, totalXp: 0 },
] }
const detail: ScoresheetDetail = { cycle: cycles.cycles[0], participant: { participantId: 'p1', displayName: 'Avery Demo', participantStatus: 'Active' }, bySource: summary.rows[0].bySource, byEntryType: summary.rows[0].byEntryType, totalXp: 10, items: [item('grant-1', 25), item('reverse-1', -25)], nextCursor: null }
const awardOptions = { cycle: { id: 'aug', code: 'AUG', name: 'August Quest', status: 'Active' as const }, participants: [{ participantId: 'p1', displayName: 'Avery Demo', participantStatus: 'Active' as const }], categories: [{ awardCategoryId: 'cat-1', code: 'EARLY', name: 'Early Bird' }] }
function api(overrides: Partial<ScoresheetApi> = {}): ScoresheetApi { return { getReportingCycles: vi.fn().mockResolvedValue(cycles), getScoresheet: vi.fn().mockResolvedValue(summary), getParticipant: vi.fn().mockResolvedValue(detail), correctXp: vi.fn().mockResolvedValue({ id: 'adjust-1', originalEntryId: 'grant-1', participantId: 'p1', cycleId: 'aug', amount: -5, entryType: 'Reversal', reason: 'Correction reason', awardedByParticipantId: 'm1', awardedAt: '2026-08-21T00:00:00Z' }), getManualAwardOptions: vi.fn().mockResolvedValue(awardOptions), createManualAward: vi.fn().mockResolvedValue({ id: 'request-1', requestId: 'request-1', participantId: 'p1', cycleId: 'aug', amount: 5, entryType: 'Grant', sourceType: 'ManualAward', awardCategory: awardOptions.categories[0], reason: 'Award reason', awardedByParticipantId: 'm1', awardedAt: '2026-08-21T00:00:00Z' }), postLeaderboard: vi.fn().mockResolvedValue({ requestId: 'announce-1', eventId: 'event-1', cycleId: 'aug', status: 'Pending', createdAt: '2026-08-21T00:00:00Z', replay: false }), ...overrides } }
function deferred<T>() { let resolve!: (value: T) => void; let reject!: (reason?: unknown) => void; const promise = new Promise<T>((pass, fail) => { resolve = pass; reject = fail }); return { promise, resolve, reject } }
async function prepareAward(user: ReturnType<typeof userEvent.setup>, amount = '5', reason = 'Great contribution') { await user.click(await screen.findByRole('button', { name: 'Award XP' })); await screen.findByRole('option', { name: 'Avery Demo' }); await user.selectOptions(screen.getByLabelText('Award participant'), 'p1'); await user.selectOptions(screen.getByLabelText('Award category'), 'cat-1'); await user.type(screen.getByLabelText('Award XP amount'), amount); await user.type(screen.getByLabelText('Manual Award reason'), reason) }

describe('manager Scoresheet', () => {
  it('posts only cycle context and a server-generated requestId, retaining it for retry', async () => {
    const post = vi.fn().mockRejectedValueOnce(new ScoresheetApiError(503)).mockResolvedValueOnce({ requestId: 'announce-1', eventId: 'event-1', cycleId: 'aug', status: 'Pending', createdAt: '2026-08-21T00:00:00Z', replay: true })
    const user = userEvent.setup(); render(<ManagerScoresheet api={api({ postLeaderboard: post })} newRequestId={() => 'announce-1'} />)
    await user.click(await screen.findByRole('button', { name: 'Post leaderboard to Teams' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('same request identifier')
    await user.click(screen.getByRole('button', { name: 'Retry' }))
    expect(post).toHaveBeenNthCalledWith(1, 'aug', 'announce-1'); expect(post).toHaveBeenNthCalledWith(2, 'aug', 'announce-1')
    expect(await screen.findByRole('status')).toHaveTextContent('Queued for notification')
  })

  it('keeps cycle-bound pending announcement intent and ignores late responses from another cycle', async () => {
    const august = deferred<Awaited<ReturnType<ScoresheetApi['postLeaderboard']>>>()
    const post = vi.fn()
      .mockReturnValueOnce(august.promise)
      .mockResolvedValueOnce({ requestId: 'july-1', eventId: 'event-july', cycleId: 'jul', status: 'Pending', createdAt: '2026-08-21T00:00:00Z', replay: false })
      .mockRejectedValueOnce(new ScoresheetApiError(503))
      .mockResolvedValueOnce({ requestId: 'august-2', eventId: 'event-august-2', cycleId: 'aug', status: 'Pending', createdAt: '2026-08-21T00:00:00Z', replay: false })
    const ids = vi.fn().mockReturnValueOnce('august-1').mockReturnValueOnce('july-1').mockReturnValueOnce('august-2')
    const user = userEvent.setup(); render(<ManagerScoresheet api={api({ postLeaderboard: post })} newRequestId={ids} />)
    await user.click(await screen.findByRole('button', { name: 'Post leaderboard to Teams' })); expect(screen.getByRole('button', { name: 'Queueing…' })).toBeDisabled()
    await user.selectOptions(screen.getByLabelText('Scoresheet reporting cycle'), 'jul'); await user.click(await screen.findByRole('button', { name: 'Post leaderboard to Teams' })); expect(await screen.findByText('Queued for notification.')).toBeInTheDocument()
    august.reject(new ScoresheetApiError(503)); await waitFor(() => expect(post).toHaveBeenCalledTimes(2)); expect(screen.queryByRole('alert')).not.toBeInTheDocument(); expect(screen.getByText('Queued for notification.')).toBeInTheDocument()
    await user.selectOptions(screen.getByLabelText('Scoresheet reporting cycle'), 'aug'); expect(await screen.findByRole('alert')).toHaveTextContent('same request identifier')
    await user.click(screen.getByRole('button', { name: 'Retry' })); await waitFor(() => expect(post).toHaveBeenNthCalledWith(3, 'aug', 'august-1'))
    await user.click(screen.getByRole('button', { name: 'Abandon and start new announcement' })); await waitFor(() => expect(post).toHaveBeenNthCalledWith(4, 'aug', 'august-2'))
  })

  it('uses a new requestId only for an explicit second post and handles privacy-disabled configuration', async () => {
    const ids = vi.fn().mockReturnValueOnce('announce-1').mockReturnValueOnce('announce-2')
    const post = vi.fn().mockResolvedValue({ requestId: 'announce-1', eventId: 'event-1', cycleId: 'aug', status: 'Pending', createdAt: '2026-08-21T00:00:00Z', replay: false })
    const user = userEvent.setup(); render(<ManagerScoresheet api={api({ postLeaderboard: post })} newRequestId={ids} />)
    await user.click(await screen.findByRole('button', { name: 'Post leaderboard to Teams' })); await screen.findByText('Queued for notification.')
    await user.click(screen.getByRole('button', { name: 'Post leaderboard again' }))
    expect(post).toHaveBeenNthCalledWith(1, 'aug', 'announce-1'); expect(post).toHaveBeenNthCalledWith(2, 'aug', 'announce-2')
    cleanup(); render(<ManagerScoresheet api={api({ postLeaderboard: vi.fn().mockRejectedValue(new ScoresheetApiError(409, 'Disabled', 'RealUserLeaderboardDisabled')) })} newRequestId={() => 'announce-3'} />)
    await user.click(await screen.findByRole('button', { name: 'Post leaderboard to Teams' })); expect(await screen.findByRole('alert')).toHaveTextContent('privacy approval')
  })
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

  it('shows correction only from server metadata and validates amount and reason before confirmation', async () => {
    const manual = { ...item('manual', 10), sourceType: 'ManualAward' as const, correction: null }
    const raid = { ...item('raid', 5), sourceType: 'Raid' as const, correction: null }
    const service = api({ getParticipant: vi.fn().mockResolvedValue({ ...detail, items: [item('grant-1', 25), item('reverse-1', -5), manual, raid] }) })
    const user = userEvent.setup(); render(<ManagerScoresheet api={service} />); await user.click(await screen.findByRole('button', { name: 'Avery Demo' }))
    expect(await screen.findAllByRole('button', { name: 'Correct XP' })).toHaveLength(1)
    await user.click(screen.getByRole('button', { name: 'Correct XP' }))
    expect(screen.getByLabelText('New effective XP')).toHaveValue(25)
    await user.clear(screen.getByLabelText('New effective XP')); await user.type(screen.getByLabelText('New effective XP'), '-1'); await user.click(screen.getByRole('button', { name: 'Review correction' })); expect(screen.getByRole('alert')).toHaveTextContent('whole number')
    await user.clear(screen.getByLabelText('New effective XP')); await user.type(screen.getByLabelText('New effective XP'), '1.5'); await user.click(screen.getByRole('button', { name: 'Review correction' })); expect(screen.getByRole('alert')).toHaveTextContent('whole number')
    await user.clear(screen.getByLabelText('New effective XP')); await user.type(screen.getByLabelText('New effective XP'), '0'); await user.click(screen.getByRole('button', { name: 'Review correction' })); expect(screen.getByRole('alert')).toHaveTextContent('reason')
    fireEvent.change(screen.getByLabelText('Correction reason'), { target: { value: 'x'.repeat(2001) } }); await user.click(screen.getByRole('button', { name: 'Review correction' })); expect(screen.getByRole('alert')).toHaveTextContent('2,000')
  })

  it('confirms original/current/new/delta/reason and prevents duplicate pending commands without optimistic totals', async () => {
    const command = deferred<Awaited<ReturnType<ScoresheetApi['correctXp']>>>()
    const service = api({ correctXp: vi.fn().mockReturnValue(command.promise) }); const user = userEvent.setup(); render(<ManagerScoresheet api={service} />)
    await user.click(await screen.findByRole('button', { name: 'Avery Demo' })); await user.click(await screen.findByRole('button', { name: 'Correct XP' })); await user.clear(screen.getByLabelText('New effective XP')); await user.type(screen.getByLabelText('New effective XP'), '20'); await user.type(screen.getByLabelText('Correction reason'), 'Evidence review'); await user.click(screen.getByRole('button', { name: 'Review correction' }))
    const confirmation = screen.getByRole('heading', { name: 'Confirm audited correction' }).closest('section')!; expect(confirmation).toHaveTextContent('Original award+25 XP'); expect(confirmation).toHaveTextContent('Current effective XP25 XP'); expect(confirmation).toHaveTextContent('New effective XP20 XP'); expect(confirmation).toHaveTextContent('Resulting change−5 XP'); expect(confirmation).toHaveTextContent('Evidence review')
    await user.click(screen.getByRole('button', { name: 'Confirm correction' })); expect(screen.getByRole('button', { name: 'Recording correction…' })).toBeDisabled(); expect(service.correctXp).toHaveBeenCalledTimes(1); const panel = screen.getByLabelText('Participant Scoresheet detail'); expect(within(within(panel).getByText('Total XP').closest('article')!).getByText('10')).toBeInTheDocument()
  })

  it('replaces loaded pages, refreshes detail then summary, and delays success until both complete', async () => {
    const refreshedSummary = deferred<ScoresheetSummary>(); const refreshedItem = { ...item('grant-1', 25), correction: { currentEffectiveAmount: 20 } }; const adjustment = { ...item('adjustment', -5), correction: null }
    const getParticipant = vi.fn().mockResolvedValueOnce({ ...detail, items: [item('grant-1', 25)], nextCursor: 'next' }).mockResolvedValueOnce({ ...detail, items: [{ ...item('later', 3), correction: null }], nextCursor: null }).mockResolvedValueOnce({ ...detail, totalXp: 5, items: [refreshedItem, adjustment], nextCursor: null })
    const getScoresheet = vi.fn().mockResolvedValueOnce(summary).mockReturnValueOnce(refreshedSummary.promise); const service = api({ getParticipant, getScoresheet }); const user = userEvent.setup(); render(<ManagerScoresheet api={service} />)
    await user.click(await screen.findByRole('button', { name: 'Avery Demo' })); await user.click(await screen.findByRole('button', { name: 'Load more' })); expect(await screen.findByText('+3 XP')).toBeInTheDocument(); await user.click(screen.getByRole('button', { name: 'Correct XP' })); await user.clear(screen.getByLabelText('New effective XP')); await user.type(screen.getByLabelText('New effective XP'), '20'); await user.type(screen.getByLabelText('Correction reason'), 'Correct evidence'); await user.click(screen.getByRole('button', { name: 'Review correction' })); await user.click(screen.getByRole('button', { name: 'Confirm correction' }))
    await waitFor(() => expect(getParticipant).toHaveBeenLastCalledWith('p1', 'aug', null)); expect(screen.queryByText('+3 XP')).not.toBeInTheDocument(); expect(screen.queryByText(/authoritative Scoresheet data refreshed/)).not.toBeInTheDocument(); refreshedSummary.resolve({ ...summary, rows: [{ ...summary.rows[0], totalXp: 5 }] }); expect(await screen.findByText(/authoritative Scoresheet data refreshed/)).toBeInTheDocument(); expect(getScoresheet).toHaveBeenCalledTimes(2)
  })

  it.each([['CorrectionNoChange', 'already matches'], ['CorrectionConflict', 'changed while you were editing']] as const)('keeps the form open and refreshes effective XP for %s', async (code, message) => {
    const refreshed = { ...detail, items: [{ ...item('grant-1', 25), correction: { currentEffectiveAmount: 15 } }] }; const service = api({ getParticipant: vi.fn().mockResolvedValueOnce(detail).mockResolvedValueOnce(refreshed), correctXp: vi.fn().mockRejectedValue(new ScoresheetApiError(409, 'Conflict', code)) }); const user = userEvent.setup(); render(<ManagerScoresheet api={service} />)
    await user.click(await screen.findByRole('button', { name: 'Avery Demo' })); await user.click(screen.getByRole('button', { name: 'Correct XP' })); await user.clear(screen.getByLabelText('New effective XP')); await user.type(screen.getByLabelText('New effective XP'), '20'); await user.type(screen.getByLabelText('Correction reason'), 'Keep this reason'); await user.click(screen.getByRole('button', { name: 'Review correction' })); await user.click(screen.getByRole('button', { name: 'Confirm correction' }))
    expect(await screen.findByRole('alert')).toHaveTextContent(message); expect(screen.getByRole('dialog')).toHaveTextContent('Current effective XP15 XP'); expect(screen.getByLabelText('Correction reason')).toHaveValue('Keep this reason')
  })

  it('follows refreshed pages sequentially to update a page-2 conflict target without automatic retry', async () => {
    const pageOneItem = { ...item('recent', -2), correction: null }; const target = { ...item('grant-page-2', 25), correction: { currentEffectiveAmount: 25 } }; const refreshedTarget = { ...target, correction: { currentEffectiveAmount: 12 } }
    const getParticipant = vi.fn()
      .mockResolvedValueOnce({ ...detail, items: [pageOneItem], nextCursor: 'initial-page-2' })
      .mockResolvedValueOnce({ ...detail, items: [target], nextCursor: null })
      .mockResolvedValueOnce({ ...detail, items: [pageOneItem], nextCursor: 'refresh-page-2' })
      .mockResolvedValueOnce({ ...detail, items: [refreshedTarget], nextCursor: null })
    const correctXp = vi.fn().mockRejectedValue(new ScoresheetApiError(409, 'Conflict', 'CorrectionConflict')); const service = api({ getParticipant, correctXp }); const user = userEvent.setup(); render(<ManagerScoresheet api={service} />)
    await user.click(await screen.findByRole('button', { name: 'Avery Demo' })); await user.click(await screen.findByRole('button', { name: 'Load more' })); await user.click(await screen.findByRole('button', { name: 'Correct XP' })); await user.clear(screen.getByLabelText('New effective XP')); await user.type(screen.getByLabelText('New effective XP'), '20'); await user.type(screen.getByLabelText('Correction reason'), 'Preserved page-two reason'); await user.click(screen.getByRole('button', { name: 'Review correction' })); await user.click(screen.getByRole('button', { name: 'Confirm correction' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('changed while you were editing'); expect(screen.getByRole('dialog')).toHaveTextContent('Current effective XP12 XP'); expect(screen.getByLabelText('Correction reason')).toHaveValue('Preserved page-two reason'); expect(getParticipant).toHaveBeenNthCalledWith(4, 'p1', 'aug', 'refresh-page-2'); expect(correctXp).toHaveBeenCalledTimes(1)
  })

  it('removes stale correction truth when the target is absent from every refreshed page', async () => {
    const target = item('missing-grant', 25); const unrelated = { ...item('unrelated', -1), correction: null }
    const getParticipant = vi.fn()
      .mockResolvedValueOnce({ ...detail, items: [target], nextCursor: null })
      .mockResolvedValueOnce({ ...detail, items: [unrelated], nextCursor: 'last-page' })
      .mockResolvedValueOnce({ ...detail, items: [], nextCursor: null })
    const correctXp = vi.fn().mockRejectedValue(new ScoresheetApiError(409, 'Conflict', 'CorrectionConflict')); const service = api({ getParticipant, correctXp }); const user = userEvent.setup(); render(<ManagerScoresheet api={service} />)
    await user.click(await screen.findByRole('button', { name: 'Avery Demo' })); await user.click(await screen.findByRole('button', { name: 'Correct XP' })); await user.type(screen.getByLabelText('Correction reason'), 'Stale target reason'); await user.click(screen.getByRole('button', { name: 'Review correction' })); await user.click(screen.getByRole('button', { name: 'Confirm correction' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('no longer available for correction'); expect(screen.queryByLabelText('New effective XP')).not.toBeInTheDocument(); expect(screen.queryByRole('button', { name: 'Review correction' })).not.toBeInTheDocument(); expect(within(screen.getByRole('dialog')).getByRole('button', { name: 'Close' })).toBeInTheDocument(); expect(correctXp).toHaveBeenCalledTimes(1)
  })

  it.each([['InvalidCorrectionAmount', 'whole number'], ['XPEntryNotFound', 'no longer exists']] as const)('keeps actionable UI for backend %s', async (code, message) => {
    const service = api({ correctXp: vi.fn().mockRejectedValue(new ScoresheetApiError(code === 'XPEntryNotFound' ? 404 : 400, 'Rejected', code)) }); const user = userEvent.setup(); render(<ManagerScoresheet api={service} />)
    await user.click(await screen.findByRole('button', { name: 'Avery Demo' })); await user.click(screen.getByRole('button', { name: 'Correct XP' })); await user.type(screen.getByLabelText('Correction reason'), 'Reason'); await user.click(screen.getByRole('button', { name: 'Review correction' })); await user.click(screen.getByRole('button', { name: 'Confirm correction' })); expect(await screen.findByRole('alert')).toHaveTextContent(message); expect(screen.getByRole('dialog')).toBeInTheDocument()
  })

  it('opens Award XP for the selected cycle and displays only server-returned options', async () => {
    const service = api(); const user = userEvent.setup(); render(<ManagerScoresheet api={service} newRequestId={() => 'request-1'} />)
    expect(await screen.findByRole('button', { name: 'Award XP' })).toBeInTheDocument(); await user.click(screen.getByRole('button', { name: 'Award XP' })); expect(service.getManualAwardOptions).toHaveBeenCalledWith('aug'); expect(await screen.findByText('August Quest · AUG')).toBeInTheDocument(); expect(screen.getByRole('option', { name: 'Avery Demo' })).toBeInTheDocument(); expect(screen.getByRole('option', { name: 'Early Bird · EARLY' })).toBeInTheDocument(); expect(within(screen.getByRole('dialog')).queryByRole('option', { name: 'Blake Demo' })).not.toBeInTheDocument(); expect(within(screen.getByRole('dialog')).queryByText(/team|bulk|raid|csv|dispute/i)).not.toBeInTheDocument(); await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Close' })); await user.selectOptions(screen.getByLabelText('Scoresheet reporting cycle'), 'jul'); await user.click(screen.getByRole('button', { name: 'Award XP' })); await waitFor(() => expect(service.getManualAwardOptions).toHaveBeenCalledWith('jul'))
  })

  it('validates required selections, positive integer amount, and reason length', async () => {
    const user = userEvent.setup(); render(<ManagerScoresheet api={api()} newRequestId={() => 'request-1'} />); await user.click(await screen.findByRole('button', { name: 'Award XP' })); await screen.findByLabelText('Award participant'); await user.click(screen.getByRole('button', { name: 'Review award' })); expect(screen.getByRole('alert')).toHaveTextContent('participant'); await user.selectOptions(screen.getByLabelText('Award participant'), 'p1'); await user.click(screen.getByRole('button', { name: 'Review award' })); expect(screen.getByRole('alert')).toHaveTextContent('category'); await user.selectOptions(screen.getByLabelText('Award category'), 'cat-1')
    for (const invalid of ['0', '-1', '1.5']) { fireEvent.change(screen.getByLabelText('Award XP amount'), { target: { value: invalid } }); await user.click(screen.getByRole('button', { name: 'Review award' })); expect(screen.getByRole('alert')).toHaveTextContent('positive whole-number') }
    fireEvent.change(screen.getByLabelText('Award XP amount'), { target: { value: '5' } }); await user.click(screen.getByRole('button', { name: 'Review award' })); expect(screen.getByRole('alert')).toHaveTextContent('reason'); fireEvent.change(screen.getByLabelText('Manual Award reason'), { target: { value: 'x'.repeat(2001) } }); await user.click(screen.getByRole('button', { name: 'Review award' })); expect(screen.getByRole('alert')).toHaveTextContent('2,000')
  })

  it('shows an explicit append-only confirmation with the complete command context', async () => {
    const user = userEvent.setup(); render(<ManagerScoresheet api={api()} newRequestId={() => 'request-1'} />); await prepareAward(user); await user.click(screen.getByRole('button', { name: 'Review award' })); const confirm = screen.getByRole('heading', { name: 'Confirm Manual Award' }).closest('section')!; expect(confirm).toHaveTextContent('Avery Demo'); expect(confirm).toHaveTextContent('August Quest · AUG'); expect(confirm).toHaveTextContent('Early Bird'); expect(confirm).toHaveTextContent('+5 XP'); expect(confirm).toHaveTextContent('Great contribution'); expect(confirm).toHaveTextContent('append a new audited XP ledger entry')
  })

  it('blocks duplicate submission and reuses the same requestId after a dependency failure', async () => {
    const pending = deferred<Awaited<ReturnType<ScoresheetApi['createManualAward']>>>(); const create = vi.fn().mockReturnValueOnce(pending.promise).mockResolvedValueOnce({ id: 'request-1' }); const service = api({ createManualAward: create }); const user = userEvent.setup(); render(<ManagerScoresheet api={service} newRequestId={() => 'request-1'} />); await prepareAward(user); await user.click(screen.getByRole('button', { name: 'Review award' })); await user.click(screen.getByRole('button', { name: 'Confirm award' })); expect(screen.getByRole('button', { name: 'Awarding XP…' })).toBeDisabled(); expect(create).toHaveBeenCalledTimes(1); expect(create.mock.calls[0][0]).toEqual({ requestId: 'request-1', cycleId: 'aug', participantId: 'p1', awardCategoryId: 'cat-1', amount: 5, reason: 'Great contribution' }); const row = screen.getByRole('button', { name: 'Avery Demo' }).closest('tr')!; expect(within(row).getAllByRole('cell').at(-1)).toHaveTextContent('10'); pending.reject(new ScoresheetApiError(503, 'Unavailable', 'ManualAwardDependencyUnavailable')); expect(await screen.findByRole('alert')).toHaveTextContent('request identifier'); await user.click(screen.getByRole('button', { name: 'Review award' })); await user.click(screen.getByRole('button', { name: 'Confirm award' })); expect(create.mock.calls[0][0].requestId).toBe('request-1'); expect(create.mock.calls[1][0].requestId).toBe('request-1')
  })

  it('keeps request conflict values until deliberate reset creates a new requestId', async () => {
    const ids = vi.fn().mockReturnValueOnce('request-1').mockReturnValueOnce('request-2'); const create = vi.fn().mockRejectedValue(new ScoresheetApiError(409, 'Conflict', 'ManualAwardRequestConflict')); const user = userEvent.setup(); render(<ManagerScoresheet api={api({ createManualAward: create })} newRequestId={ids} />); await prepareAward(user); await user.click(screen.getByRole('button', { name: 'Review award' })); await user.click(screen.getByRole('button', { name: 'Confirm award' })); expect(await screen.findByRole('alert')).toHaveTextContent('deliberately reset'); expect(ids).toHaveBeenCalledTimes(1); expect(screen.getByLabelText('Manual Award reason')).toHaveValue('Great contribution'); await user.click(screen.getByRole('button', { name: 'Reset / new award' })); expect(ids).toHaveBeenCalledTimes(2); expect(screen.getByLabelText('Manual Award reason')).toHaveValue('')
  })

  it.each([['ManualAwardCycleUnavailable', 'cycle'], ['ManualAwardParticipantIneligible', 'participant'], ['AwardCategoryNotFound', 'category']] as const)('keeps the command safe and refreshes options for %s', async (code, message) => {
    const getOptions = vi.fn().mockResolvedValue(awardOptions); const ids = vi.fn().mockReturnValue('request-1'); const user = userEvent.setup(); render(<ManagerScoresheet api={api({ getManualAwardOptions: getOptions, createManualAward: vi.fn().mockRejectedValue(new ScoresheetApiError(code === 'AwardCategoryNotFound' ? 404 : 409, 'Rejected', code)) })} newRequestId={ids} />); await prepareAward(user); await user.click(screen.getByRole('button', { name: 'Review award' })); await user.click(screen.getByRole('button', { name: 'Confirm award' })); expect(await screen.findByRole('alert')).toHaveTextContent(message); await waitFor(() => expect(getOptions).toHaveBeenCalledTimes(2)); expect(screen.getByLabelText('Manual Award reason')).toHaveValue('Great contribution'); expect(ids).toHaveBeenCalledTimes(1)
  })

  it('refreshes open detail from page one, replaces later pages, refreshes summary, then creates a new requestId', async () => {
    const ids = vi.fn().mockReturnValueOnce('request-1').mockReturnValueOnce('request-2'); const summaryRefresh = deferred<ScoresheetSummary>(); const getScoresheet = vi.fn().mockResolvedValueOnce(summary).mockReturnValueOnce(summaryRefresh.promise); const getParticipant = vi.fn().mockResolvedValueOnce({ ...detail, items: [item('grant-1', 25)], nextCursor: 'next' }).mockResolvedValueOnce({ ...detail, items: [{ ...item('later', 3), correction: null }], nextCursor: null }).mockResolvedValueOnce({ ...detail, totalXp: 15, items: [item('grant-1', 25)], nextCursor: null }); const service = api({ getScoresheet, getParticipant }); const user = userEvent.setup(); render(<ManagerScoresheet api={service} newRequestId={ids} />)
    await user.click(await screen.findByRole('button', { name: 'Avery Demo' })); await user.click(await screen.findByRole('button', { name: 'Load more' })); expect(await screen.findByText('+3 XP')).toBeInTheDocument(); await prepareAward(user); await user.click(screen.getByRole('button', { name: 'Review award' })); await user.click(screen.getByRole('button', { name: 'Confirm award' })); await waitFor(() => expect(getParticipant).toHaveBeenLastCalledWith('p1', 'aug', null)); expect(screen.queryByText('+3 XP')).not.toBeInTheDocument(); expect(screen.queryByText(/Manual XP award.*recorded/)).not.toBeInTheDocument(); summaryRefresh.resolve({ ...summary, rows: [{ ...summary.rows[0], totalXp: 15 }] }); expect(await screen.findByText(/Manual XP award for Avery Demo recorded/)).toBeInTheDocument(); expect(ids).toHaveBeenCalledTimes(2); await waitFor(() => expect(screen.getByLabelText('Manual Award reason')).toHaveValue(''))
  })
})
