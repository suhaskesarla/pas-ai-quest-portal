import { useEffect, useRef, useState } from 'react'
import type { CurrentUser } from '../auth/types'
import type { WorkflowApi } from './workflowApi'
import { workflowErrorMessage } from './workflowApi'
import type { EligibleChallenge, EvidenceInput, EvidenceItem, ReviewAction, SubmissionView, TaskSummary } from './types'

const statusLabels = { Submitted: 'Submitted', UnderReview: 'Under review', NeedsEvidence: 'Needs evidence', Resubmitted: 'Resubmitted', Approved: 'Approved', Rejected: 'Rejected' }
const formatDate = (value: string) => new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value))

export function StatusBadge({ status }: { status: SubmissionView['status'] }) {
  return <span className={`workflow-status workflow-status--${status.toLowerCase()}`}>{statusLabels[status]}</span>
}

export function ChallengeList({ challenges, loading, error, onSelectTask }: { challenges: EligibleChallenge[]; loading: boolean; error: string | null; onSelectTask: (challenge: EligibleChallenge, task: TaskSummary) => void }) {
  if (loading) return <WorkflowState text="Loading eligible challenges…" />
  if (error) return <WorkflowError message={error} />
  if (!challenges.length) return <WorkflowState text="No challenges are currently available to you." />
  return <div><header className="section-heading"><p className="eyebrow">QUEST LOG</p><h2>Eligible challenges</h2><p>Eligibility comes from each challenge’s lifecycle and your effective deadline—not the calendar month.</p></header>
    <div className="workflow-stack">{challenges.map((challenge) => <article className="workflow-card" key={challenge.id}>
      <div className="workflow-card__header"><div><span className="category">{challenge.category}</span><h3>{challenge.name}</h3><p>{challenge.description}</p></div><span className={`challenge-state challenge-state--${challenge.status.toLowerCase()}`}>{challenge.status}</span></div>
      <dl className="date-grid"><div><dt>Opens</dt><dd>{formatDate(challenge.openAt)}</dd></div><div><dt>Due</dt><dd>{formatDate(challenge.dueAt)}</dd></div><div><dt>Your effective deadline</dt><dd>{formatDate(challenge.effectiveDeadline)}</dd></div><div><dt>Closes</dt><dd>{formatDate(challenge.closeAt)}</dd></div></dl>
      {!challenge.isEligible && <p className="inline-error" role="status">Not eligible: {challenge.ineligibilityReason}</p>}
      <div className="task-list">{challenge.tasks.map((task) => <div className="task-row" key={task.id}><div><strong>{task.name}</strong><span>{task.evidenceInputs.map((input) => input.label).join(' · ') || 'No evidence required'}</span></div><strong>{task.xp} XP</strong><button className="button" type="button" disabled={!challenge.isEligible || task.scoringMode === 'AttendanceBased'} onClick={() => onSelectTask(challenge, task)}>{task.scoringMode === 'AttendanceBased' ? 'Manager recorded' : 'Submit work'}</button></div>)}</div>
    </article>)}</div>
  </div>
}

function EvidenceFields({ inputs, values, onChange }: { inputs: EvidenceInput[]; values: Record<string, string>; onChange: (key: string, value: string) => void }) {
  return <>{inputs.map((input, index) => { const key = `${input.kind}:${index}`; return <label className="field" key={key}><span>{input.label}{input.required ? ' *' : ''}</span>{input.instructions && <small>{input.instructions}</small>}
    {input.kind === 'Text' ? <textarea rows={4} value={values[key] ?? ''} onChange={(event) => onChange(key, event.target.value)} /> : <input type="url" placeholder="https://…" value={values[key] ?? ''} onChange={(event) => onChange(key, event.target.value)} />}
  </label> })}</>
}

function evidenceFrom(inputs: EvidenceInput[], values: Record<string, string>): Omit<EvidenceItem, 'id'>[] {
  return inputs.map((input, index) => ({ kind: input.kind, label: input.label, value: (values[`${input.kind}:${index}`] ?? '').trim() })).filter((item) => item.value)
}

export function SubmissionForm({ currentUser, challenge, task, api, onSubmitted, onCancel }: { currentUser: CurrentUser; challenge: EligibleChallenge; task: TaskSummary; api: WorkflowApi; onSubmitted: () => void; onCancel: () => void }) {
  const firstParticipation = task.participations[0]
  const initialBeneficiaries = firstParticipation?.requiresCompleteParticipation
    ? firstParticipation?.members.map((member) => member.participantId) ?? []
    : currentUser.participantId ? [currentUser.participantId] : []
  const [participationId, setParticipationId] = useState(firstParticipation?.participationId ?? '')
  const [beneficiaries, setBeneficiaries] = useState<string[]>(initialBeneficiaries)
  const [values, setValues] = useState<Record<string, string>>({})
  const [comment, setComment] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)
  const errorRef = useRef<HTMLDivElement>(null)
  const selectedParticipation = task.participations.find((option) => option.participationId === participationId)

  const submit = async (event: React.FormEvent) => {
    event.preventDefault()
    const missingEvidence = task.evidenceInputs.some((input, index) => input.required && !(values[`${input.kind}:${index}`] ?? '').trim())
    if (!beneficiaries.length || missingEvidence) {
      setError(!beneficiaries.length ? 'Choose at least one beneficiary.' : 'Complete every required evidence field.')
      queueMicrotask(() => errorRef.current?.focus())
      return
    }
    setPending(true); setError(null)
    try {
      await api.createSubmission({ challengeId: challenge.id, taskId: task.id, challengeParticipationId: participationId || undefined, beneficiaryIds: beneficiaries, evidence: evidenceFrom(task.evidenceInputs, values), comment: comment.trim() || undefined })
      onSubmitted()
    } catch (requestError) {
      setError(workflowErrorMessage(requestError)); queueMicrotask(() => errorRef.current?.focus())
    } finally { setPending(false) }
  }

  return <form className="workflow-card form-card" onSubmit={submit} noValidate><header className="section-heading"><p className="eyebrow">{challenge.name}</p><h2>{task.name}</h2><p>{task.xp} XP · effective deadline {formatDate(challenge.effectiveDeadline)}</p></header>
    <p><strong>Claimant:</strong> {currentUser.displayName}</p>
    <div className="shared-outcome"><strong>One shared submission outcome</strong><span>All beneficiaries are reviewed together. Nobody receives XP until the entire submission is approved.</span></div>
    {task.scoringMode !== 'Individual' && <label className="field"><span>Participation</span><select value={participationId} onChange={(event) => {
      const nextId = event.target.value
      const next = task.participations.find((option) => option.participationId === nextId)
      setParticipationId(nextId)
      setBeneficiaries(next?.requiresCompleteParticipation ? next.members.map((member) => member.participantId) : next?.members.filter((member) => member.participantId === currentUser.participantId).map((member) => member.participantId) ?? [])
    }}>{task.participations.map((option, index) => <option value={option.participationId} key={option.participationId}>Participation {index + 1}: {option.members.map((member) => member.displayName).join(', ')}</option>)}</select></label>}
    <fieldset><legend>Beneficiaries</legend>{task.scoringMode === 'Individual'
      ? <p>{currentUser.displayName}</p>
      : selectedParticipation?.requiresCompleteParticipation
        ? <><p className="participation-members">Complete participation: {selectedParticipation.members.map((person) => person.displayName).join(', ')}</p><p className="shared-note">Whole-team submissions always include every member of the selected participation.</p></>
        : selectedParticipation?.allowsBeneficiarySubset && selectedParticipation.members.map((person) => <label className="check-row" key={person.participantId}><input type="checkbox" checked={beneficiaries.includes(person.participantId)} disabled={person.participantId === currentUser.participantId} onChange={(event) => setBeneficiaries((current) => event.target.checked ? [...new Set([...current, person.participantId])] : current.filter((id) => id !== person.participantId))} />{person.displayName}</label>)}</fieldset>
    <EvidenceFields inputs={task.evidenceInputs} values={values} onChange={(key, value) => setValues((current) => ({ ...current, [key]: value }))} />
    <label className="field"><span>Comment (optional)</span><textarea rows={3} value={comment} onChange={(event) => setComment(event.target.value)} /></label>
    {error && <div className="inline-error" role="alert" tabIndex={-1} ref={errorRef}>{error}</div>}
    <div className="form-actions"><button className="button button--quiet" type="button" onClick={onCancel} disabled={pending}>Cancel</button><button className="button" type="submit" disabled={pending}>{pending ? 'Submitting…' : 'Submit for review'}</button></div>
  </form>
}

function EvidenceList({ evidence }: { evidence: EvidenceItem[] }) {
  return <div className="evidence-list"><h4>Evidence</h4>{evidence.length ? evidence.map((item, index) => <div key={item.id ?? `${item.kind}-${index}`}><strong>{item.label}</strong>{item.kind === 'Link' ? <a href={item.value} rel="noreferrer">{item.value}</a> : <span>{item.value}</span>}</div>) : <p>No evidence items.</p>}</div>
}

export function ActivityList({ submissions, loading, error, api, onRefresh }: { submissions: SubmissionView[]; loading: boolean; error: string | null; api: WorkflowApi; onRefresh: () => Promise<void> }) {
  if (loading) return <WorkflowState text="Loading your submissions…" />
  if (error) return <WorkflowError message={error} />
  if (!submissions.length) return <WorkflowState text="You have not submitted work yet." />
  return <div><header className="section-heading"><p className="eyebrow">YOUR QUEST</p><h2>My activity</h2></header><div className="workflow-stack">{submissions.map((submission) => <SubmissionCard key={submission.id} submission={submission}>{submission.status === 'NeedsEvidence' && <ResubmitForm submission={submission} api={api} onRefresh={onRefresh} />}</SubmissionCard>)}</div></div>
}

function SubmissionCard({ submission, children }: { submission: SubmissionView; children?: React.ReactNode }) {
  return <article className="workflow-card"><div className="workflow-card__header"><div><h3>{submission.taskName}</h3><p>{submission.challengeName} · submitted {formatDate(submission.submittedAt)}</p></div><StatusBadge status={submission.status} /></div>
    <p><strong>Claimant:</strong> {submission.claimant.displayName}</p><p><strong>Beneficiaries:</strong> {submission.beneficiaries.map((person) => person.displayName).join(', ')}</p>
    {submission.beneficiaries.length > 1 && <p className="shared-note">Shared all-or-nothing review: this status applies to every beneficiary.</p>}
    {submission.managerComment && <div className="manager-feedback"><strong>Manager feedback</strong><span>{submission.managerComment}</span></div>}
    <EvidenceList evidence={submission.evidence} />{children}
  </article>
}

function ResubmitForm({ submission, api, onRefresh }: { submission: SubmissionView; api: WorkflowApi; onRefresh: () => Promise<void> }) {
  const inputs = submission.evidence.map((item) => ({ kind: item.kind, label: item.label, required: true }))
  const [values, setValues] = useState<Record<string, string>>(() => Object.fromEntries(submission.evidence.map((item, index) => [`${item.kind}:${index}`, item.value])))
  const [comment, setComment] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)
  const submit = async (event: React.FormEvent) => { event.preventDefault(); setPending(true); setError(null); try { await api.resubmit(submission.id, { version: submission.version, evidence: evidenceFrom(inputs, values), comment: comment.trim() || undefined }); await onRefresh() } catch (requestError) { setError(workflowErrorMessage(requestError)) } finally { setPending(false) } }
  return <form className="resubmit" onSubmit={submit}><h4>Update evidence</h4><p>The evidence below replaces the current evidence set for this submission.</p><EvidenceFields inputs={inputs} values={values} onChange={(key, value) => setValues((current) => ({ ...current, [key]: value }))} /><label className="field"><span>Response to manager</span><textarea rows={2} value={comment} onChange={(event) => setComment(event.target.value)} /></label>{error && <div className="inline-error" role="alert">{error}</div>}<button className="button" disabled={pending}>{pending ? 'Resubmitting…' : 'Resubmit shared submission'}</button></form>
}

export function ReviewQueue({ submissions, loading, error, api, onRefresh }: { submissions: SubmissionView[]; loading: boolean; error: string | null; api: WorkflowApi; onRefresh: () => Promise<void> }) {
  if (loading) return <WorkflowState text="Loading review queue…" />
  if (error) return <WorkflowError message={error} />
  if (!submissions.length) return <WorkflowState text="All caught up—nothing is waiting for review." />
  return <div><header className="section-heading"><p className="eyebrow">{submissions.length} PENDING</p><h2>Review queue</h2></header><div className="workflow-stack">{submissions.map((submission) => <ManagerReview key={submission.id} submission={submission} api={api} onRefresh={onRefresh} />)}</div></div>
}

function ManagerReview({ submission, api, onRefresh }: { submission: SubmissionView; api: WorkflowApi; onRefresh: () => Promise<void> }) {
  const [comment, setComment] = useState('')
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const act = async (action: ReviewAction) => {
    if (action === 'NeedsEvidence' && !comment.trim()) { setError('Add a comment explaining what evidence is needed.'); return }
    if ((action === 'Approve' || action === 'Reject') && !window.confirm(action === 'Approve' ? `Approve this shared submission and award ${submission.taskXp} XP to all ${submission.beneficiaries.length} beneficiaries?` : 'Reject this entire shared submission?')) return
    setPending(true); setError(null)
    try { await api.review(submission.id, { version: submission.version, action, comment: comment.trim() || undefined }); await onRefresh() } catch (requestError) { setError(workflowErrorMessage(requestError)) } finally { setPending(false) }
  }
  return <article className="workflow-card"><div className="workflow-card__header"><div><h3>{submission.taskName}</h3><p>{submission.challengeName}</p></div><StatusBadge status={submission.status} /></div>
    <div className="review-people"><p><strong>Claimant</strong><span>{submission.claimant.displayName}</span></p><p><strong>Beneficiaries ({submission.beneficiaries.length})</strong><span>{submission.beneficiaries.map((person) => person.displayName).join(', ')}</span></p></div>
    <div className="shared-outcome"><strong>Approve everyone together</strong><span>Approval awards {submission.taskXp} XP to every beneficiary in one transaction. Partial approval is not available.</span></div>
    <EvidenceList evidence={submission.evidence} />
    {submission.history.length > 0 && <details><summary>Submission history</summary><ol className="history">{submission.history.map((event, index) => <li key={`${event.occurredAt}-${index}`}><StatusBadge status={event.eventType} /> <span>{formatDate(event.occurredAt)} · {event.actorDisplayName}{event.comment ? ` · ${event.comment}` : ''}</span></li>)}</ol></details>}
    <label className="field"><span>Manager comment</span><textarea rows={3} value={comment} onChange={(event) => setComment(event.target.value)} /></label>{error && <div className="inline-error" role="alert">{error}</div>}
    <div className="review-actions"><button className="button button--danger-quiet" type="button" disabled={pending} onClick={() => void act('Reject')}>Reject</button><button className="button button--warning" type="button" disabled={pending} onClick={() => void act('NeedsEvidence')}>Needs evidence</button><button className="button button--approve" type="button" disabled={pending} onClick={() => void act('Approve')}>{pending ? 'Updating…' : `Approve all ${submission.beneficiaries.length}`}</button></div>
  </article>
}

export const WorkflowState = ({ text }: { text: string }) => <div className="workflow-state" role="status">{text}</div>
export const WorkflowError = ({ message }: { message: string }) => <div className="workflow-state inline-error" role="alert">{message}</div>
