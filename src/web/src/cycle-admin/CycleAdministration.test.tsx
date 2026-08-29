import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { CycleAdministration } from './CycleAdministration'
import { CycleAdminApiError, type CycleAdminApi } from './cycleAdminApi'
import type { CycleDetail, CycleSummary } from './types'

afterEach(cleanup)
const summary = (id: string, name: string, status: 'Active' | 'Closing' | 'Finalised' = 'Active'): CycleSummary => ({ id, version: `${id}-v`, code: id.toUpperCase(), name, status, startsAt: '2026-08-01T00:00:00Z', endsAt: '2026-08-31T00:00:00Z', participantCounts: { active: 1, withdrawn: 0, inactive: 0, total: 1 } })
const detail = (id = 'c1', name = 'August Quest', status: 'Active' | 'Closing' | 'Finalised' = 'Active'): CycleDetail => ({ ...summary(id, name, status), themeConfiguration: null, createdAt: '2026-07-01T00:00:00Z', createdByParticipantId: 'manager', allowedActions: { canEdit: status === 'Active', canStartClosing: status === 'Active', canFinalise: status === 'Closing', canAddParticipant: status === 'Active' }, participants: [{ participantId: 'p1', displayName: 'Avery Demo', status: 'Active', joinedAt: '2026-08-01T00:00:00Z', leftAt: null, version: 'pv1', allowedActions: { canSetActive: false, canSetWithdrawn: status === 'Active', canSetInactive: status === 'Active' } }] })
function service(overrides: Partial<CycleAdminApi> = {}): CycleAdminApi { return { getCycles: vi.fn().mockResolvedValue({ cycles: [summary('c1', 'August Quest')] }), getCycle: vi.fn().mockResolvedValue(detail()), getParticipantOptions: vi.fn().mockResolvedValue({ participants: [{ participantId: 'p2', displayName: 'Blake Demo' }] }), createCycle: vi.fn().mockResolvedValue(detail('c2', 'September Quest')), updateCycle: vi.fn().mockResolvedValue(detail()), transition: vi.fn().mockResolvedValue(detail()), enroll: vi.fn().mockResolvedValue({}), changeStatus: vi.fn().mockResolvedValue({}), ...overrides } as CycleAdminApi }
const deferred = <T,>() => { let resolve!: (value: T) => void; let reject!: (reason: unknown) => void; const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no }); return { promise, resolve, reject } }

describe('Cycle Administration', () => {
  it('renders the authoritative cycle list/detail and protects rapid switching from stale responses', async () => {
    const a = deferred<CycleDetail>(), b = deferred<CycleDetail>(); const api = service({ getCycles: vi.fn().mockResolvedValue({ cycles: [summary('a', 'Cycle A'), summary('b', 'Cycle B')] }), getCycle: vi.fn((id: string) => id === 'a' ? a.promise : b.promise) })
    const user = userEvent.setup(); render(<CycleAdministration api={api} />); await user.click(await screen.findByRole('button', { name: /Cycle B/ })); b.resolve(detail('b', 'Cycle B')); expect(await screen.findByRole('heading', { name: 'Cycle B' })).toBeInTheDocument(); a.resolve(detail('a', 'Cycle A')); await waitFor(() => expect(screen.queryByRole('heading', { name: 'Cycle A' })).not.toBeInTheDocument())
  })

  it('creates an Active cycle after validation and authoritative refresh', async () => {
    const api = service(); const user = userEvent.setup(); render(<CycleAdministration api={api} />); await user.click(await screen.findByRole('button', { name: 'Create Cycle' })); await user.click(screen.getByRole('button', { name: 'Create Active cycle' })); expect(await screen.findByRole('alert')).toHaveTextContent('Starts At must be before Ends At')
    await user.type(screen.getByLabelText('Code'), 'SEP26'); await user.type(screen.getByLabelText('Name'), 'September Quest'); fireEvent.change(screen.getByLabelText('Starts At'), { target: { value: '2026-09-01T09:00' } }); fireEvent.change(screen.getByLabelText('Ends At'), { target: { value: '2026-09-30T17:00' } }); await user.click(screen.getByRole('button', { name: 'Create Active cycle' })); await waitFor(() => expect(api.createCycle).toHaveBeenCalledWith(expect.objectContaining({ code: 'SEP26', name: 'September Quest' }))); expect(api.getCycles).toHaveBeenCalledTimes(2)
  })

  it('does not let a late selected-cycle detail response overwrite an in-progress create form', async () => {
    const oldDetail = deferred<CycleDetail>(), getCycle = vi.fn().mockReturnValue(oldDetail.promise), createCycle = vi.fn().mockResolvedValue(detail('new', 'Unique Cycle')), api = service({ getCycle, createCycle }); const user = userEvent.setup(); render(<CycleAdministration api={api} />)
    await waitFor(() => expect(getCycle).toHaveBeenCalledTimes(1)); expect(getCycle).toHaveBeenCalledWith('c1')
    await user.click(screen.getByRole('button', { name: 'Create Cycle' })); await user.type(screen.getByLabelText('Code'), 'UNIQUE-26'); await user.type(screen.getByLabelText('Name'), 'Unique Cycle'); fireEvent.change(screen.getByLabelText('Starts At'), { target: { value: '2026-10-01T09:30' } }); fireEvent.change(screen.getByLabelText('Ends At'), { target: { value: '2026-10-31T17:45' } })
    expect(screen.getByLabelText('Code')).toHaveValue('UNIQUE-26'); expect(screen.getByLabelText('Name')).toHaveValue('Unique Cycle'); expect(screen.getByLabelText('Starts At')).toHaveValue('2026-10-01T09:30'); expect(screen.getByLabelText('Ends At')).toHaveValue('2026-10-31T17:45')
    await act(async () => { oldDetail.resolve(detail('c1', 'Seeded Cycle')); await oldDetail.promise })
    expect(screen.getByLabelText('Code')).toHaveValue('UNIQUE-26'); expect(screen.getByLabelText('Name')).toHaveValue('Unique Cycle'); expect(screen.getByLabelText('Starts At')).toHaveValue('2026-10-01T09:30'); expect(screen.getByLabelText('Ends At')).toHaveValue('2026-10-31T17:45')
    await user.click(screen.getByRole('button', { name: 'Create Active cycle' })); await waitFor(() => expect(createCycle).toHaveBeenCalledWith({ code: 'UNIQUE-26', name: 'Unique Cycle', startsAt: new Date('2026-10-01T09:30').toISOString(), endsAt: new Date('2026-10-31T17:45').toISOString() })); expect(createCycle).not.toHaveBeenCalledWith(expect.objectContaining({ code: 'C1', name: 'Seeded Cycle' }))
  })

  it('edits only when allowedActions permits and preserves values on stale 409', async () => {
    const api = service({ updateCycle: vi.fn().mockRejectedValue(new CycleAdminApiError(409, 'CycleVersionConflict', 'Changed')) }); const user = userEvent.setup(); render(<CycleAdministration api={api} />); await user.click(await screen.findByRole('button', { name: 'Edit cycle' })); const name = screen.getByLabelText('Name'); await user.clear(name); await user.type(name, 'Local attempted name'); await user.click(screen.getByRole('button', { name: 'Save cycle' })); expect(await screen.findByRole('alert')).toHaveTextContent('values are preserved'); expect(name).toHaveValue('Local attempted name'); expect(screen.getByRole('button', { name: 'Reload authoritative cycle' })).toBeInTheDocument()
  })

  it('preserves exact server timestamp precision when only metadata changes in Australia/Sydney', async () => {
    vi.stubEnv('TZ', 'Australia/Sydney')
    try {
      const precise = detail(); precise.startsAt = '2026-08-01T00:00:30.500Z'; precise.endsAt = '2026-08-31T04:45:59.750Z'
      const api = service({ getCycle: vi.fn().mockResolvedValue(precise) }); const user = userEvent.setup(); render(<CycleAdministration api={api} />); await user.click(await screen.findByRole('button', { name: 'Edit cycle' })); expect(screen.getByLabelText('Starts At')).toHaveValue('2026-08-01T10:00'); expect(screen.getByLabelText('Ends At')).toHaveValue('2026-08-31T14:45'); const name = screen.getByLabelText('Name'); await user.clear(name); await user.type(name, 'Updated metadata'); await user.click(screen.getByRole('button', { name: 'Save cycle' })); await waitFor(() => expect(api.updateCycle).toHaveBeenCalledWith('c1', expect.objectContaining({ name: 'Updated metadata', startsAt: '2026-08-01T00:00:30.500Z', endsAt: '2026-08-31T04:45:59.750Z' })))
    } finally { vi.unstubAllEnvs() }
  })

  it('converts a changed Sydney start while preserving the exact untouched end', async () => {
    vi.stubEnv('TZ', 'Australia/Sydney')
    try {
      const precise = detail(); precise.startsAt = '2026-08-01T00:00:30.500Z'; precise.endsAt = '2026-08-31T04:45:59.750Z'
      const api = service({ getCycle: vi.fn().mockResolvedValue(precise) }); const user = userEvent.setup(); render(<CycleAdministration api={api} />); await user.click(await screen.findByRole('button', { name: 'Edit cycle' })); fireEvent.change(screen.getByLabelText('Starts At'), { target: { value: '2026-08-02T09:15' } }); await user.click(screen.getByRole('button', { name: 'Save cycle' })); await waitFor(() => expect(api.updateCycle).toHaveBeenCalledWith('c1', expect.objectContaining({ startsAt: '2026-08-01T23:15:00.000Z', endsAt: '2026-08-31T04:45:59.750Z' })))
    } finally { vi.unstubAllEnvs() }
  })

  it('resets timestamp baselines after an authoritative refresh', async () => {
    vi.stubEnv('TZ', 'Australia/Sydney')
    try {
      const original = detail(); original.startsAt = '2026-08-01T00:00:30.500Z'; original.endsAt = '2026-08-31T04:45:59.750Z'
      const refreshed = detail(); refreshed.startsAt = '2026-08-02T01:02:03.400Z'; refreshed.endsAt = '2026-09-01T05:06:07.800Z'
      const getCycle = vi.fn().mockResolvedValueOnce(original).mockResolvedValue(refreshed), updateCycle = vi.fn().mockResolvedValue(refreshed), api = service({ getCycle, updateCycle }); const user = userEvent.setup(); render(<CycleAdministration api={api} />)
      await user.click(await screen.findByRole('button', { name: 'Edit cycle' })); await user.click(screen.getByRole('button', { name: 'Save cycle' })); await waitFor(() => expect(getCycle).toHaveBeenCalledTimes(2)); await user.click(await screen.findByRole('button', { name: 'Edit cycle' })); const name = screen.getByLabelText('Name'); await user.clear(name); await user.type(name, 'After refresh'); await user.click(screen.getByRole('button', { name: 'Save cycle' })); await waitFor(() => expect(updateCycle).toHaveBeenNthCalledWith(2, 'c1', expect.objectContaining({ startsAt: '2026-08-02T01:02:03.400Z', endsAt: '2026-09-01T05:06:07.800Z' })))
    } finally { vi.unstubAllEnvs() }
  })

  it.each([['Closing', true], ['Finalised', false]] as const)('renders %s cycles read-only from server actions', async (status, canFinalise) => {
    render(<CycleAdministration api={service({ getCycles: vi.fn().mockResolvedValue({ cycles: [summary('c1', status, status)] }), getCycle: vi.fn().mockResolvedValue(detail('c1', status, status)) })} />); await screen.findByRole('heading', { name: status }); expect(screen.queryByRole('button', { name: 'Edit cycle' })).not.toBeInTheDocument(); expect(Boolean(screen.queryByRole('button', { name: 'Finalise Cycle' }))).toBe(canFinalise); expect(screen.queryByText(/Delete|Reopen|Draft/)).not.toBeInTheDocument()
  })

  it('requires confirmation reasons for Closing and Finalise and refreshes after success', async () => {
    const api = service(); const user = userEvent.setup(); render(<CycleAdministration api={api} />); await user.click(await screen.findByRole('button', { name: 'Start Closing' })); expect(screen.getByRole('dialog')).toHaveTextContent('Move this cycle to Closing?'); await user.click(screen.getByRole('button', { name: 'Confirm' })); expect(await within(screen.getByRole('dialog')).findByRole('alert')).toHaveTextContent('reason is required'); await user.type(screen.getByLabelText('Reason'), 'Cycle review begins'); await user.click(screen.getByRole('button', { name: 'Confirm' })); await waitFor(() => expect(api.transition).toHaveBeenCalledWith('c1', 'start-closing', 'c1-v', 'Cycle review begins'))
  })

  it('requires explicit reason confirmation before finalising and explains challenge independence', async () => {
    const closing = detail('c1', 'Closing Cycle', 'Closing'); const api = service({ getCycles: vi.fn().mockResolvedValue({ cycles: [summary('c1', 'Closing Cycle', 'Closing')] }), getCycle: vi.fn().mockResolvedValue(closing) }); const user = userEvent.setup(); render(<CycleAdministration api={api} />); await user.click(await screen.findByRole('button', { name: 'Finalise Cycle' })); expect(screen.getByRole('dialog')).toHaveTextContent('does not automatically close challenges'); await user.type(screen.getByLabelText('Reason'), 'Reporting is complete'); await user.click(screen.getByRole('button', { name: 'Confirm' })); await waitFor(() => expect(api.transition).toHaveBeenCalledWith('c1', 'finalise', 'c1-v', 'Reporting is complete'))
  })

  it('enrolls only server-returned participant options with a mandatory reason', async () => {
    const api = service(); const user = userEvent.setup(); render(<CycleAdministration api={api} />); await user.click(await screen.findByRole('button', { name: 'Enroll participant' })); expect(await screen.findByRole('option', { name: 'Blake Demo' })).toBeInTheDocument(); await user.selectOptions(screen.getByLabelText('Participant'), 'p2'); await user.click(screen.getByRole('button', { name: 'Confirm' })); expect(await screen.findByRole('alert')).toHaveTextContent('reason is required'); await user.type(screen.getByLabelText('Reason'), 'Joining September'); await user.click(screen.getByRole('button', { name: 'Confirm' })); await waitFor(() => expect(api.enroll).toHaveBeenCalledWith('c1', 'p2', 'Joining September'))
  })

  it('clears and request-scopes enrollment options across cycle changes and failures', async () => {
    const cycleA = deferred<{ participants: { participantId: string; displayName: string }[] }>(), cycleB = deferred<{ participants: { participantId: string; displayName: string }[] }>()
    const api = service({ getCycles: vi.fn().mockResolvedValue({ cycles: [summary('a', 'Cycle A'), summary('b', 'Cycle B')] }), getCycle: vi.fn((id: string) => Promise.resolve(detail(id, id === 'a' ? 'Cycle A' : 'Cycle B'))), getParticipantOptions: vi.fn((id: string) => id === 'a' ? cycleA.promise : cycleB.promise) })
    const user = userEvent.setup(); render(<CycleAdministration api={api} />); await user.click(await screen.findByRole('button', { name: 'Enroll participant' })); expect(screen.getByText('Loading participants…')).toBeInTheDocument(); await user.keyboard('{Escape}'); await user.click(screen.getByRole('button', { name: /Cycle B/ })); await user.click(await screen.findByRole('button', { name: 'Enroll participant' })); expect(screen.queryByRole('option', { name: 'A-only Demo' })).not.toBeInTheDocument(); cycleA.resolve({ participants: [{ participantId: 'pa', displayName: 'A-only Demo' }] }); await Promise.resolve(); expect(screen.queryByRole('option', { name: 'A-only Demo' })).not.toBeInTheDocument(); cycleB.reject(new CycleAdminApiError(503, 'Unavailable', 'Options unavailable')); expect(await screen.findByText(/Error loading participants/)).toBeInTheDocument(); expect(screen.queryByRole('option', { name: 'A-only Demo' })).not.toBeInTheDocument()
  })

  it('removes previously loaded enrollment options immediately in a new cycle context', async () => {
    const b = deferred<{ participants: { participantId: string; displayName: string }[] }>(); const api = service({ getCycles: vi.fn().mockResolvedValue({ cycles: [summary('a', 'Cycle A'), summary('b', 'Cycle B')] }), getCycle: vi.fn((id: string) => Promise.resolve(detail(id, id === 'a' ? 'Cycle A' : 'Cycle B'))), getParticipantOptions: vi.fn((id: string) => id === 'a' ? Promise.resolve({ participants: [{ participantId: 'pa', displayName: 'A-only Demo' }] }) : b.promise) }); const user = userEvent.setup(); render(<CycleAdministration api={api} />); await user.click(await screen.findByRole('button', { name: 'Enroll participant' })); expect(await screen.findByRole('option', { name: 'A-only Demo' })).toBeInTheDocument(); await user.keyboard('{Escape}'); await user.click(screen.getByRole('button', { name: /Cycle B/ })); await user.click(await screen.findByRole('button', { name: 'Enroll participant' })); expect(screen.getByText('Loading participants…')).toBeInTheDocument(); expect(screen.queryByRole('option', { name: 'A-only Demo' })).not.toBeInTheDocument(); b.resolve({ participants: [] }); expect(await screen.findByText('No eligible participants.')).toBeInTheDocument()
  })

  it('keeps a newer selected cycle visible when an older edit mutation completes', async () => {
    const update = deferred<CycleDetail>(); const api = service({ getCycles: vi.fn().mockResolvedValue({ cycles: [summary('a', 'Cycle A'), summary('b', 'Cycle B')] }), getCycle: vi.fn((id: string) => Promise.resolve(detail(id, id === 'a' ? 'Cycle A' : 'Cycle B'))), updateCycle: vi.fn().mockReturnValue(update.promise) }); const user = userEvent.setup(); render(<CycleAdministration api={api} />); await user.click(await screen.findByRole('button', { name: 'Edit cycle' })); await user.click(screen.getByRole('button', { name: 'Save cycle' })); await user.click(screen.getByRole('button', { name: /Cycle B/ })); expect(await screen.findByRole('heading', { name: 'Cycle B' })).toBeInTheDocument(); update.resolve(detail('a', 'Cycle A')); await waitFor(() => expect(screen.getByRole('heading', { name: 'Cycle B' })).toBeInTheDocument()); expect(screen.queryByRole('heading', { name: 'Cycle A' })).not.toBeInTheDocument()
  })

  it('uses participant allowedActions and rowversion, handles conflict without retry, and blocks duplicate clicks', async () => {
    const pending = deferred<unknown>(); const changeStatus = vi.fn().mockReturnValue(pending.promise); const api = service({ changeStatus }); const user = userEvent.setup(); render(<CycleAdministration api={api} />); await user.click(await screen.findByRole('button', { name: 'Set Withdrawn' })); expect(screen.queryByRole('button', { name: 'Set Active' })).not.toBeInTheDocument(); await user.type(screen.getByLabelText('Reason'), 'Participant withdrew'); const confirm = screen.getByRole('button', { name: 'Confirm' }); await user.dblClick(confirm); expect(changeStatus).toHaveBeenCalledTimes(1); expect(changeStatus).toHaveBeenCalledWith('c1', 'p1', 'pv1', 'Withdrawn', 'Participant withdrew'); pending.resolve({})
  })

  it('keeps participant status confirmation open on stale rowversion and never retries automatically', async () => {
    const changeStatus = vi.fn().mockRejectedValue(new CycleAdminApiError(409, 'CycleParticipantVersionConflict', 'Stale')); const api = service({ changeStatus }); const user = userEvent.setup(); render(<CycleAdministration api={api} />); await user.click(await screen.findByRole('button', { name: 'Set Inactive' })); await user.type(screen.getByLabelText('Reason'), 'No longer participating'); await user.click(screen.getByRole('button', { name: 'Confirm' })); expect(await screen.findByRole('alert')).toHaveTextContent('Authoritative state changed'); expect(changeStatus).toHaveBeenCalledTimes(1); expect(screen.getByRole('dialog')).toBeInTheDocument(); expect(screen.getByRole('button', { name: 'Reload / Refresh' })).toBeInTheDocument()
  })

  it('moves focus into action dialogs, traps Tab, closes on Escape, and restores invoking focus', async () => {
    const user = userEvent.setup(); render(<CycleAdministration api={service()} />); const trigger = await screen.findByRole('button', { name: 'Start Closing' }); trigger.focus(); await user.click(trigger); const dialog = screen.getByRole('dialog'); const reason = within(dialog).getByLabelText('Reason'); expect(reason).toHaveFocus(); await user.keyboard('{Shift>}{Tab}{/Shift}'); expect(within(dialog).getByRole('button', { name: 'Confirm' })).toHaveFocus(); await user.tab(); expect(reason).toHaveFocus(); await user.keyboard('{Escape}'); expect(screen.queryByRole('dialog')).not.toBeInTheDocument(); await waitFor(() => expect(trigger).toHaveFocus())
  })

  it.each([
    ['Active', 'Withdrawn'], ['Active', 'Inactive'], ['Withdrawn', 'Active'], ['Withdrawn', 'Inactive'], ['Inactive', 'Active'], ['Inactive', 'Withdrawn'],
  ] as const)('exposes and refreshes the server-allowed %s to %s transition', async (from, to) => {
    // Restrict the fixture to the one transition under test; the UI must follow these server flags.
    const allowed = { canSetActive: to === 'Active', canSetWithdrawn: to === 'Withdrawn', canSetInactive: to === 'Inactive' }
    const before = detail(); before.participants = [{ ...before.participants[0], status: from, allowedActions: allowed }]
    const after = detail(); after.participants = [{ ...after.participants[0], status: to, version: 'pv2', leftAt: to === 'Active' ? null : '2026-08-10T00:00:00Z', allowedActions: { canSetActive: false, canSetWithdrawn: false, canSetInactive: false } }]
    const getCycle = vi.fn().mockResolvedValueOnce(before).mockResolvedValue(after), api = service({ getCycle }); const user = userEvent.setup(); render(<CycleAdministration api={api} />); const action = await screen.findByRole('button', { name: `Set ${to}` }); expect(screen.queryByRole('button', { name: `Set ${from}` })).not.toBeInTheDocument(); await user.click(action); await user.click(screen.getByRole('button', { name: 'Confirm' })); expect(await screen.findByRole('alert')).toHaveTextContent('reason is required'); await user.type(screen.getByLabelText('Reason'), `${from} to ${to}`); await user.click(screen.getByRole('button', { name: 'Confirm' })); await waitFor(() => expect(api.changeStatus).toHaveBeenCalledWith('c1', 'p1', 'pv1', to, `${from} to ${to}`)); expect(await screen.findByText(to, { selector: '.participant-status' })).toBeInTheDocument()
  })

  it('shows empty, loading, retryable error and no-option states', async () => {
    const load = deferred<{ cycles: CycleSummary[] }>(); const api = service({ getCycles: vi.fn().mockReturnValue(load.promise) }); const { rerender } = render(<CycleAdministration api={api} />); expect(screen.getByText('Loading cycles…')).toBeInTheDocument(); load.resolve({ cycles: [] }); expect(await screen.findByText('No cycles yet. Create the first Active cycle.')).toBeInTheDocument(); rerender(<CycleAdministration api={service({ getCycles: vi.fn().mockRejectedValue(new Error('down')) })} />); expect(await screen.findByRole('alert')).toHaveTextContent('Something went wrong'); expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument()
  })
})
