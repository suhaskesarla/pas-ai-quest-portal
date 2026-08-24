import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ActivityList, ChallengeList, ReviewQueue, SubmissionForm } from './WorkflowViews'
import { WorkflowApiError, type WorkflowApi } from './workflowApi'
import type { EligibleChallenge, SubmissionStatus, SubmissionView } from './types'

afterEach(() => { cleanup(); vi.restoreAllMocks() })

const people = [{ participantId: 'p1', displayName: 'Avery Demo' }, { participantId: 'p2', displayName: 'Jordan Demo' }]
const secondGroup = [{ participantId: 'p1', displayName: 'Avery Demo' }, { participantId: 'p3', displayName: 'Riley Demo' }]
const challenge: EligibleChallenge = {
  id: 'c1', name: 'Prompt Quest', description: 'Build and document a useful prompt.', category: 'Go Pass', status: 'Open',
  openAt: '2026-08-01T00:00:00Z', dueAt: '2026-08-20T00:00:00Z', closeAt: '2026-09-01T00:00:00Z', effectiveDeadline: '2026-08-25T00:00:00Z', isEligible: true,
  tasks: [{ id: 't1', name: 'Prompt journal', xp: 10, scoringMode: 'ClaimantSelectsBeneficiaries', participations: [
    { participationId: 'part-a', members: people, claimantIsMember: true, requiresCompleteParticipation: false, allowsBeneficiarySubset: true },
    { participationId: 'part-b', members: secondGroup, claimantIsMember: true, requiresCompleteParticipation: false, allowsBeneficiarySubset: true },
  ], evidenceInputs: [{ kind: 'Text', label: 'What you learned', required: true }, { kind: 'Link', label: 'Supporting link', required: false }] }],
}

function submission(status: SubmissionStatus = 'Submitted'): SubmissionView {
  return { id: 's1', version: 'v1', status, claimant: people[0], beneficiaries: people, challengeId: 'c1', challengeName: 'Prompt Quest', taskId: 't1', taskName: 'Prompt journal', taskXp: 10,
    evidence: [{ id: 'e1', kind: 'Text', label: 'What you learned', value: 'A synthetic response' }], managerComment: status === 'NeedsEvidence' ? 'Add the supporting link for the whole group.' : undefined,
    submittedAt: '2026-08-10T01:00:00Z', lastUpdatedAt: '2026-08-10T01:00:00Z', history: [{ eventType: status, actorDisplayName: 'Avery Demo', occurredAt: '2026-08-10T01:00:00Z' }] }
}

function api(overrides: Partial<WorkflowApi> = {}): WorkflowApi {
  return { getEligibleChallenges: vi.fn().mockResolvedValue([challenge]), getMySubmissions: vi.fn().mockResolvedValue([]), createSubmission: vi.fn().mockResolvedValue(submission()), resubmit: vi.fn().mockResolvedValue(submission('Resubmitted')), getReviewQueue: vi.fn().mockResolvedValue([submission('UnderReview')]), review: vi.fn().mockResolvedValue(submission('Approved')), ...overrides }
}

describe('participant workflow', () => {
  it('shows eligible challenge dates, task XP, evidence expectations, and opens task flow', async () => {
    const onSelect = vi.fn(); const user = userEvent.setup()
    render(<ChallengeList challenges={[challenge]} loading={false} error={null} onSelectTask={onSelect} />)
    expect(screen.getByText('Your effective deadline')).toBeInTheDocument()
    expect(screen.getByText('10 XP')).toBeInTheDocument()
    expect(screen.getByText(/What you learned/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Submit work' }))
    expect(onSelect).toHaveBeenCalledWith(challenge, challenge.tasks[0])
  })

  it('validates required evidence and explains multi-beneficiary all-or-nothing approval', async () => {
    const user = userEvent.setup(); const service = api()
    render(<SubmissionForm currentUser={{ isAuthenticated: true, participantId: 'p1', displayName: 'Avery Demo', roles: ['Quest.Participant'] }} challenge={challenge} task={challenge.tasks[0]} api={service} onSubmitted={vi.fn()} onCancel={vi.fn()} />)
    expect(screen.getByText(/Nobody receives XP until the entire submission is approved/)).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Submit for review' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Complete every required evidence field')
    expect(service.createSubmission).not.toHaveBeenCalled()
  })

  it('uses the AttendanceBased contract value and renders only API-supported Step 6 evidence inputs', () => {
    const attendanceChallenge = { ...challenge, tasks: [{ ...challenge.tasks[0], id: 'attendance', scoringMode: 'AttendanceBased' as const }] }
    render(<ChallengeList challenges={[attendanceChallenge]} loading={false} error={null} onSelectTask={vi.fn()} />)
    expect(screen.getByRole('button', { name: 'Manager recorded' })).toBeDisabled()
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument()
  })

  it('limits claimant-selected beneficiaries to one selected participation', async () => {
    const user = userEvent.setup(); const service = api()
    render(<SubmissionForm currentUser={{ isAuthenticated: true, participantId: 'p1', displayName: 'Avery Demo', roles: ['Quest.Participant'] }} challenge={challenge} task={challenge.tasks[0]} api={service} onSubmitted={vi.fn()} onCancel={vi.fn()} />)
    expect(screen.getByRole('option', { name: /Participation 1: Avery Demo, Jordan Demo/ })).toBeInTheDocument()
    expect(screen.getByRole('option', { name: /Participation 2: Avery Demo, Riley Demo/ })).toBeInTheDocument()
    await user.click(screen.getByLabelText('Jordan Demo'))
    await user.selectOptions(screen.getByLabelText('Participation'), 'part-b')
    expect(screen.queryByLabelText('Jordan Demo')).not.toBeInTheDocument()
    expect(screen.getByLabelText('Riley Demo')).not.toBeChecked()
    await user.click(screen.getByLabelText('Riley Demo'))
    await user.type(screen.getByLabelText(/What you learned/), 'Scoped evidence')
    await user.click(screen.getByRole('button', { name: 'Submit for review' }))
    await waitFor(() => expect(service.createSubmission).toHaveBeenCalled())
    expect(service.createSubmission).toHaveBeenCalledWith(expect.objectContaining({ challengeParticipationId: 'part-b', beneficiaryIds: ['p1', 'p3'] }))
  })

  it('automatically uses the complete selected participation for WholeTeam', async () => {
    const user = userEvent.setup(); const service = api(); const task = { ...challenge.tasks[0], scoringMode: 'WholeTeam' as const,
      participations: challenge.tasks[0].participations.map((option) => ({ ...option, requiresCompleteParticipation: true, allowsBeneficiarySubset: false })) }
    render(<SubmissionForm currentUser={{ isAuthenticated: true, participantId: 'p1', displayName: 'Avery Demo', roles: ['Quest.Participant'] }} challenge={challenge} task={task} api={service} onSubmitted={vi.fn()} onCancel={vi.fn()} />)
    expect(screen.getByText('Complete participation: Avery Demo, Jordan Demo')).toBeInTheDocument()
    expect(screen.queryByRole('checkbox')).not.toBeInTheDocument()
    await user.type(screen.getByLabelText(/What you learned/), 'Whole team evidence')
    await user.click(screen.getByRole('button', { name: 'Submit for review' }))
    await waitFor(() => expect(service.createSubmission).toHaveBeenCalled())
    expect(service.createSubmission).toHaveBeenCalledWith(expect.objectContaining({ challengeParticipationId: 'part-a', beneficiaryIds: ['p1', 'p2'] }))
  })

  it('submits only after API confirmation with claimant and beneficiary context', async () => {
    const user = userEvent.setup(); const service = api(); const completed = vi.fn()
    render(<SubmissionForm currentUser={{ isAuthenticated: true, participantId: 'p1', displayName: 'Avery Demo', roles: ['Quest.Participant'] }} challenge={challenge} task={challenge.tasks[0]} api={service} onSubmitted={completed} onCancel={vi.fn()} />)
    await user.click(screen.getByLabelText('Jordan Demo'))
    await user.type(screen.getByLabelText(/What you learned/), 'Useful synthetic evidence')
    await user.click(screen.getByRole('button', { name: 'Submit for review' }))
    await waitFor(() => expect(completed).toHaveBeenCalled())
    expect(service.createSubmission).toHaveBeenCalledWith(expect.objectContaining({ beneficiaryIds: ['p1', 'p2'] }))
  })

  it.each(['NeedsEvidence', 'Approved', 'Rejected'] as SubmissionStatus[])('renders %s status clearly', (status) => {
    render(<ActivityList submissions={[submission(status)]} loading={false} error={null} api={api()} onRefresh={vi.fn()} />)
      expect(
        screen.getAllByText(status === 'NeedsEvidence' ? 'Needs evidence' : status).length,
      ).toBeGreaterThan(0)
    if (status === 'NeedsEvidence') expect(screen.getByText(/Add the supporting link/)).toBeInTheDocument()
  })

  it('renders API history chronologically with actor and returned comments', () => {
    const approved = submission('Approved')
    approved.history = [
      { eventType: 'Approved', actorDisplayName: 'Morgan Demo Manager', occurredAt: '2026-08-10T03:00:00Z', comment: 'Approved together.' },
      { eventType: 'Submitted', actorDisplayName: 'Avery Demo Submitter', occurredAt: '2026-08-10T01:00:00Z', comment: 'Initial submission.' },
      { eventType: 'UnderReview', actorDisplayName: 'Morgan Demo Manager', occurredAt: '2026-08-10T02:00:00Z' },
    ]
    render(<ActivityList submissions={[approved]} loading={false} error={null} api={api()} onRefresh={vi.fn()} />)
    const history = screen.getByRole('region', { name: 'Submission history' })
    const events = within(history).getAllByRole('listitem')
    expect(events[0]).toHaveTextContent('Submitted')
    expect(events[0]).toHaveTextContent('Avery Demo Submitter')
    expect(events[0]).toHaveTextContent('Initial submission.')
    expect(events[1]).toHaveTextContent('Under review')
    expect(events[2]).toHaveTextContent('Approved')
    expect(events[2]).toHaveTextContent('Morgan Demo Manager')
    expect(events[2]).toHaveTextContent('Approved together.')
  })

  it('renders returned manager feedback in My Activity', () => {
    render(<ActivityList submissions={[submission('NeedsEvidence')]} loading={false} error={null} api={api()} onRefresh={vi.fn()} />)
    expect(screen.getByText('Manager feedback')).toBeInTheDocument()
    expect(screen.getByText('Add the supporting link for the whole group.')).toBeInTheDocument()
  })

  it('shows API-returned task XP and shared result only for Approved submissions', () => {
    const { rerender } = render(<ActivityList submissions={[submission('Approved')]} loading={false} error={null} api={api()} onRefresh={vi.fn()} />)
    expect(screen.getByText('Awarded task result: 10 XP')).toBeInTheDocument()
    expect(screen.getByText(/approved shared submission covers all 2 beneficiaries/)).toBeInTheDocument()
    rerender(<ActivityList submissions={[submission('UnderReview')]} loading={false} error={null} api={api()} onRefresh={vi.fn()} />)
    expect(screen.queryByText(/Awarded task result/)).not.toBeInTheDocument()
  })

  it('resubmits the existing NeedsEvidence submission and refreshes from API', async () => {
    const user = userEvent.setup(); const service = api(); const refresh = vi.fn().mockResolvedValue(undefined)
    render(<ActivityList submissions={[submission('NeedsEvidence')]} loading={false} error={null} api={service} onRefresh={refresh} />)
    const evidence = screen.getByLabelText(/What you learned/)
    await user.clear(evidence)
    await user.type(evidence, 'Replacement evidence set')
    await user.type(screen.getByLabelText('Response to manager'), 'Updated for everyone')
    await user.click(screen.getByRole('button', { name: 'Resubmit shared submission' }))
    await waitFor(() => expect(refresh).toHaveBeenCalled())
    expect(service.resubmit).toHaveBeenCalledWith('s1', expect.objectContaining({ version: 'v1' }))
    expect(service.resubmit).toHaveBeenCalledWith('s1', expect.objectContaining({ evidence: [{ kind: 'Text', label: 'What you learned', value: 'Replacement evidence set' }] }))
    expect(screen.getByText(/replaces the current evidence set/)).toBeInTheDocument()
  })

  it('shows deadline/eligibility API failure and never reports false success', async () => {
    const user = userEvent.setup(); const completed = vi.fn(); const service = api({ createSubmission: vi.fn().mockRejectedValue(new WorkflowApiError(422, 'The participant deadline has passed.')) })
    render(<SubmissionForm currentUser={{ isAuthenticated: true, participantId: 'p1', displayName: 'Avery Demo', roles: ['Quest.Participant'] }} challenge={challenge} task={challenge.tasks[0]} api={service} onSubmitted={completed} onCancel={vi.fn()} />)
    await user.type(screen.getByLabelText(/What you learned/), 'Evidence')
    await user.click(screen.getByRole('button', { name: 'Submit for review' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('deadline has passed')
    expect(completed).not.toHaveBeenCalled()
  })
})

describe('manager workflow', () => {
  it('shows queue details, claimant, beneficiaries, evidence, history, and no partial controls', async () => {
    render(<ReviewQueue submissions={[submission('UnderReview')]} loading={false} error={null} api={api()} onRefresh={vi.fn()} />)
    expect(screen.getByText('Avery Demo')).toBeInTheDocument()
    expect(screen.getByText('Avery Demo, Jordan Demo')).toBeInTheDocument()
    expect(screen.getByText('A synthetic response')).toBeInTheDocument()
    expect(screen.getByText(/Partial approval is not available/)).toBeInTheDocument()
    expect(screen.queryByText(/Approve Avery/)).not.toBeInTheDocument()
  })

  it.each([['Approve', 'Approve all 2'], ['NeedsEvidence', 'Needs evidence'], ['Reject', 'Reject']] as const)('performs %s once and refreshes after confirmation', async (action, buttonName) => {
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    const user = userEvent.setup(); const service = api(); const refresh = vi.fn().mockResolvedValue(undefined)
    render(<ReviewQueue submissions={[submission('UnderReview')]} loading={false} error={null} api={service} onRefresh={refresh} />)
    if (action === 'NeedsEvidence') await user.type(screen.getByLabelText('Manager comment'), 'Please add evidence for everyone.')
    await user.click(screen.getByRole('button', { name: buttonName }))
    await waitFor(() => expect(refresh).toHaveBeenCalled())
    expect(service.review).toHaveBeenCalledWith('s1', expect.objectContaining({ action }))
  })

  it.each([[401, 'session has expired'], [403, 'do not have permission']] as const)('handles %s without false success', async (status, message) => {
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    const user = userEvent.setup(); const refresh = vi.fn(); const service = api({ review: vi.fn().mockRejectedValue(new WorkflowApiError(status)) })
    render(<ReviewQueue submissions={[submission('UnderReview')]} loading={false} error={null} api={service} onRefresh={refresh} />)
    await user.click(screen.getByRole('button', { name: 'Approve all 2' }))
    expect(await screen.findByRole('alert')).toHaveTextContent(message)
    expect(refresh).not.toHaveBeenCalled()
  })
})
