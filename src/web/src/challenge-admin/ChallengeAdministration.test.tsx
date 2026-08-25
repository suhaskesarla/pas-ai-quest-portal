import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ChallengeAdministration } from './ChallengeAdministration'
import { ChallengeAdminApiError, type ChallengeAdminApi } from './challengeAdminApi'
import type { ChallengeAggregate, ChallengeOptions } from './types'

afterEach(() => { cleanup(); vi.restoreAllMocks() })
const options: ChallengeOptions = { cycles: [{ id: 'c1', code: 'AUG26', name: 'August Quest', status: 'Active', startsAt: '2026-08-01T00:00:00Z', endsAt: '2026-08-31T00:00:00Z' }], scoringModes: ['Individual', 'WholeTeam', 'ClaimantSelectsBeneficiaries'], evidenceRequirements: ['None', 'Text', 'Link', 'Attachment', 'Multiple'], formationModes: ['SelfForm', 'ManagerAssigned', 'Either'] }
const draft: ChallengeAggregate = { id: 'q1', version: 'b3BhcXVl', cycleId: 'c1', cycleCode: 'AUG26', cycleName: 'August Quest', name: 'Prompt Quest', description: null, category: null, status: 'Draft', openAt: '2026-08-10T00:00:00Z', dueAt: '2026-08-20T00:00:00Z', closeAt: '2026-08-25T00:00:00Z', heroImageReference: null, tasks: [{ id: 't1', name: 'Try prompting', description: null, xp: 0, scoringMode: 'Individual', evidenceRequirement: 'Text', sortOrder: 1 }], participationPolicy: null }
function api(overrides: Partial<ChallengeAdminApi> = {}): ChallengeAdminApi { return { getOptions: vi.fn().mockResolvedValue(options), getChallenges: vi.fn().mockResolvedValue([draft]), getChallenge: vi.fn().mockResolvedValue(draft), createChallenge: vi.fn().mockResolvedValue(draft), updateChallenge: vi.fn().mockResolvedValue(draft), publishChallenge: vi.fn().mockResolvedValue({ ...draft, status: 'Open' }), ...overrides } }

async function openCreate(service: ChallengeAdminApi) { const user = userEvent.setup(); render(<ChallengeAdministration api={service} />); await user.click(await screen.findByRole('button', { name: 'Create Challenge' })); return user }
const setDates = () => { fireEvent.change(screen.getByLabelText('Opens'), { target: { value: '2026-08-10T09:00' } }); fireEvent.change(screen.getByLabelText('Due'), { target: { value: '2026-08-20T09:00' } }); fireEvent.change(screen.getByLabelText('Closes'), { target: { value: '2026-08-25T09:00' } }) }

describe('manager challenge administration', () => {
  it('shows the manager challenge list and applies cycle/status filters through the API', async () => {
    const service = api(); const user = userEvent.setup(); render(<ChallengeAdministration api={service} />)
    expect(await screen.findByRole('heading', { name: 'Prompt Quest' })).toBeInTheDocument()
    expect(screen.getByText('1 tasks')).toBeInTheDocument(); expect(screen.getByText('0 XP available')).toBeInTheDocument()
    await user.selectOptions(screen.getByLabelText('Cycle filter'), 'c1'); await user.selectOptions(screen.getByLabelText('Status filter'), 'Draft')
    await waitFor(() => expect(service.getChallenges).toHaveBeenLastCalledWith('c1', 'Draft'))
  })

  it('creates a full aggregate with optional description/category and a zero-XP task', async () => {
    const service = api(); const user = await openCreate(service)
    await user.type(screen.getByLabelText('Name'), 'New Draft'); setDates(); await user.click(screen.getByRole('button', { name: 'Add task' })); await user.type(screen.getByLabelText('Task name'), 'Zero XP task')
    await user.click(screen.getByRole('button', { name: 'Create draft' }))
    await waitFor(() => expect(service.createChallenge).toHaveBeenCalled())
    expect(service.createChallenge).toHaveBeenCalledWith(expect.objectContaining({ name: 'New Draft', description: null, category: null, tasks: [expect.objectContaining({ id: null, xp: 0, sortOrder: 1 })], participationPolicy: null }))
  })

  it('supports add/remove/reorder and reveals participation policy including allowSolo only for group scoring', async () => {
    const service = api(); const user = await openCreate(service)
    await user.click(screen.getByRole('button', { name: 'Add task' })); await user.click(screen.getByRole('button', { name: 'Add task' }))
    const tasks = document.querySelectorAll('.admin-task'); await user.type(within(tasks[0] as HTMLElement).getByLabelText('Task name'), 'First'); await user.type(within(tasks[1] as HTMLElement).getByLabelText('Task name'), 'Second')
    await user.selectOptions(within(tasks[0] as HTMLElement).getByLabelText('Scoring mode'), 'WholeTeam')
    expect(screen.getByRole('group', { name: 'Participation policy' })).toBeInTheDocument(); expect(screen.getByLabelText('Allow solo')).toBeInTheDocument()
    await user.click(within(tasks[0] as HTMLElement).getByRole('button', { name: '↓' })); expect(within(document.querySelectorAll('.admin-task')[0] as HTMLElement).getByLabelText('Task name')).toHaveValue('Second')
    await user.click(within(document.querySelectorAll('.admin-task')[0] as HTMLElement).getByRole('button', { name: 'Remove' })); expect(document.querySelectorAll('.admin-task')).toHaveLength(1)
  })

  it('maps server field errors and preserves entered form values', async () => {
    const service = api({ createChallenge: vi.fn().mockRejectedValue(new ChallengeAdminApiError(400, 'ChallengeValidationFailed', 'Validation failed.', { Name: ['Name is invalid.'] })) }); const user = await openCreate(service)
    await user.type(screen.getByLabelText('Name'), 'Keep me'); setDates(); await user.click(screen.getByRole('button', { name: 'Create draft' }))
    expect(await screen.findByText(/Name is invalid/)).toBeInTheDocument(); expect(screen.getByLabelText('Name')).toHaveValue('Keep me')
  })

  it.each([['ChallengeVersionConflict', 'changed by another manager'], ['ChallengeHasOperationalDependencies', 'operational data depends']] as const)('handles %s without replacing local draft', async (code, message) => {
    const service = api({ updateChallenge: vi.fn().mockRejectedValue(new ChallengeAdminApiError(409, code, 'Conflict')) }); const user = userEvent.setup(); render(<ChallengeAdministration api={service} />)
    await user.click(await screen.findByRole('button', { name: 'Edit' })); const name = await screen.findByLabelText('Name'); await user.clear(name); await user.type(name, 'Local edit'); await user.click(screen.getByRole('button', { name: 'Save full draft' }))
    expect(await screen.findByRole('alert')).toHaveTextContent(message); expect(name).toHaveValue('Local edit')
    if (code === 'ChallengeVersionConflict') expect(screen.getByRole('button', { name: 'Reload latest draft' })).toBeInTheDocument()
  })

  it('confirms publish, waits for API success, and becomes read-only using returned state', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true); let resolvePublish!: (value: ChallengeAggregate) => void; const pending = new Promise<ChallengeAggregate>((resolve) => { resolvePublish = resolve }); const service = api({ publishChallenge: vi.fn().mockReturnValue(pending) }); const user = userEvent.setup(); render(<ChallengeAdministration api={service} />)
    await user.click(await screen.findByRole('button', { name: 'Edit' })); await user.click(await screen.findByRole('button', { name: 'Publish challenge' }))
    expect(window.confirm).toHaveBeenCalledWith(expect.stringContaining('Publishing freezes task, scoring, evidence and participation configuration.'))
    expect(screen.queryByText(/Published challenges are read-only/)).not.toBeInTheDocument(); resolvePublish({ ...draft, status: 'Open' })
    expect(await screen.findByText(/Published challenges are read-only/)).toBeInTheDocument(); expect(screen.queryByRole('button', { name: 'Save full draft' })).not.toBeInTheDocument()
  })

  it('renders an already-published challenge read-only and never offers unsupported/deferred choices', async () => {
    const open = { ...draft, status: 'Open' as const }; const service = api({ getChallenges: vi.fn().mockResolvedValue([open]), getChallenge: vi.fn().mockResolvedValue(open) }); const user = userEvent.setup(); render(<ChallengeAdministration api={service} />)
    await user.click(await screen.findByRole('button', { name: 'View' })); expect(await screen.findByText(/Published challenges are read-only/)).toBeInTheDocument()
    expect(screen.queryByRole('option', { name: 'Custom' })).not.toBeInTheDocument(); expect(screen.queryByRole('option', { name: 'AttendanceBased' })).not.toBeInTheDocument(); expect(screen.queryByText(/team leaderboard|team XP/i)).not.toBeInTheDocument()
  })
})
