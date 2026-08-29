import { useCallback, useEffect, useRef, useState } from 'react'
import { CycleAdminApiError, cycleError, type CycleAdminApi } from './cycleAdminApi'
import type { CreateCycleRequest, CycleDetail, CycleParticipant, CycleSummary, ParticipantOption, ParticipantStatus } from './types'

const date = (value: string) => new Date(value).toLocaleDateString()
const dateTime = (value: string | null) => value ? new Date(value).toLocaleString() : '—'
const two = (value: number) => String(value).padStart(2, '0')
const inputDate = (value: string) => { const local = new Date(value); return `${local.getFullYear()}-${two(local.getMonth() + 1)}-${two(local.getDate())}T${two(local.getHours())}:${two(local.getMinutes())}` }
const outputDate = (value: string) => new Date(value).toISOString()
const emptyForm = (): CreateCycleRequest => ({ code: '', name: '', startsAt: '', endsAt: '' })
const conflict = (error: unknown) => error instanceof CycleAdminApiError && error.status === 409
type DateBaseline = { startsAtServer: string; startsAtInput: string; endsAtServer: string; endsAtInput: string }

export function CycleAdministration({ api }: { api: CycleAdminApi }) {
  const [cycles, setCycles] = useState<CycleSummary[]>([]), [selectedId, setSelectedId] = useState(''), [detail, setDetail] = useState<CycleDetail | null>(null)
  const [listLoading, setListLoading] = useState(true), [detailLoading, setDetailLoading] = useState(false), [error, setError] = useState<string | null>(null)
  const [creating, setCreating] = useState(false), [editing, setEditing] = useState(false), [form, setForm] = useState<CreateCycleRequest>(emptyForm), [pending, setPending] = useState(false)
  const [dateBaseline, setDateBaseline] = useState<DateBaseline | null>(null)
  const [action, setAction] = useState<{ kind: 'closing' | 'finalise' | 'enroll' | 'status'; participant?: CycleParticipant; status?: ParticipantStatus } | null>(null)
  const [reason, setReason] = useState(''), [participantId, setParticipantId] = useState(''), [options, setOptions] = useState<ParticipantOption[]>([]), [optionsLoading, setOptionsLoading] = useState(false), [optionsError, setOptionsError] = useState<string | null>(null), [actionError, setActionError] = useState<string | null>(null)
  const detailGeneration = useRef(0), optionsGeneration = useRef(0), selectedIdRef = useRef(''), viewModeRef = useRef<'detail' | 'create'>('detail'), actionTriggerRef = useRef<HTMLElement | null>(null)
  useEffect(() => { selectedIdRef.current = selectedId }, [selectedId])

  const loadList = useCallback(async () => {
    setListLoading(true); setError(null)
    try { const result = await api.getCycles(); setCycles(result.cycles); setSelectedId((current) => current || result.cycles[0]?.id || '') }
    catch (requestError) { setError(cycleError(requestError)) }
    finally { setListLoading(false) }
  }, [api])
  const loadDetail = useCallback(async (id: string) => {
    const generation = ++detailGeneration.current; setDetailLoading(true); setError(null); setDetail(null); setEditing(false)
    try { const result = await api.getCycle(id); if (generation === detailGeneration.current && viewModeRef.current === 'detail') { const startsAtInput = inputDate(result.startsAt), endsAtInput = inputDate(result.endsAt); setDetail(result); setForm({ code: result.code, name: result.name, startsAt: startsAtInput, endsAt: endsAtInput }); setDateBaseline({ startsAtServer: result.startsAt, startsAtInput, endsAtServer: result.endsAt, endsAtInput }) } }
    catch (requestError) { if (generation === detailGeneration.current) setError(cycleError(requestError)) }
    finally { if (generation === detailGeneration.current) setDetailLoading(false) }
  }, [api])
  useEffect(() => { void loadList() }, [loadList])
  const closeAction = useCallback(() => { ++optionsGeneration.current; setAction(null); setOptions([]); setOptionsLoading(false); setOptionsError(null); setParticipantId(''); queueMicrotask(() => actionTriggerRef.current?.focus()) }, [])
  useEffect(() => { closeAction(); if (selectedId) void loadDetail(selectedId); else { ++detailGeneration.current; setDetail(null) } }, [selectedId, loadDetail, closeAction])

  const mutationRefresh = async (mutationCycleId: string) => { await loadList(); if (selectedIdRef.current === mutationCycleId) await loadDetail(mutationCycleId) }
  const validate = () => !form.code.trim() || !form.name.trim() || !form.startsAt || !form.endsAt || new Date(form.startsAt) >= new Date(form.endsAt) ? 'Code, name, and dates are required, and Starts At must be before Ends At.' : null
  const save = async (event: React.FormEvent) => {
    event.preventDefault(); const validation = validate(); if (validation) { setActionError(validation); return } setPending(true); setActionError(null)
    const body = { code: form.code.trim(), name: form.name.trim(), startsAt: dateBaseline && form.startsAt === dateBaseline.startsAtInput ? dateBaseline.startsAtServer : outputDate(form.startsAt), endsAt: dateBaseline && form.endsAt === dateBaseline.endsAtInput ? dateBaseline.endsAtServer : outputDate(form.endsAt) }
    const mutationCycleId = creating ? null : detail!.id
    try { const saved = creating ? await api.createCycle(body) : await api.updateCycle(mutationCycleId!, { ...body, version: detail!.version }); viewModeRef.current = 'detail'; setCreating(false); setEditing(false); if (!mutationCycleId) { setSelectedId(saved.id); await loadList() } else await mutationRefresh(mutationCycleId) }
    catch (requestError) { setActionError(conflict(requestError) ? 'This cycle changed. Your values are preserved; reload authoritative state before trying again.' : cycleError(requestError)) }
    finally { setPending(false) }
  }
  const openAction = async (next: typeof action) => {
    actionTriggerRef.current = document.activeElement instanceof HTMLElement ? document.activeElement : null
    const generation = ++optionsGeneration.current; const cycleId = detail?.id
    setAction(next); setReason(''); setParticipantId(''); setActionError(null); setOptions([]); setOptionsError(null)
    if (next?.kind === 'enroll' && cycleId) {
      setOptionsLoading(true)
      try { const result = await api.getParticipantOptions(cycleId); if (generation === optionsGeneration.current && selectedIdRef.current === cycleId) setOptions(result.participants) }
      catch (e) { if (generation === optionsGeneration.current && selectedIdRef.current === cycleId) setOptionsError(cycleError(e)) }
      finally { if (generation === optionsGeneration.current && selectedIdRef.current === cycleId) setOptionsLoading(false) }
    } else setOptionsLoading(false)
  }
  const submitAction = async () => {
    if (!detail || !action) return
    if (!reason.trim()) { setActionError('A reason is required.'); return }
    if (reason.trim().length > 1000) { setActionError('Reason cannot exceed 1,000 characters.'); return }
    if (action.kind === 'enroll' && !participantId) { setActionError('Select a participant.'); return }
    const mutationCycleId = detail.id; setPending(true); setActionError(null)
    try {
      if (action.kind === 'closing') await api.transition(detail.id, 'start-closing', detail.version, reason.trim())
      if (action.kind === 'finalise') await api.transition(detail.id, 'finalise', detail.version, reason.trim())
      if (action.kind === 'enroll') await api.enroll(detail.id, participantId, reason.trim())
      if (action.kind === 'status' && action.participant && action.status) await api.changeStatus(detail.id, action.participant.participantId, action.participant.version, action.status, reason.trim())
      closeAction(); await mutationRefresh(mutationCycleId)
    } catch (requestError) { setActionError(conflict(requestError) ? 'Authoritative state changed. Review and reload before retrying; this action was not retried.' : cycleError(requestError)) }
    finally { setPending(false) }
  }

  const selectCycle = (id: string) => { viewModeRef.current = 'detail'; ++detailGeneration.current; setCreating(false); setEditing(false); if (id === selectedId) void loadDetail(id); else setSelectedId(id) }
  const startCreate = () => { viewModeRef.current = 'create'; ++detailGeneration.current; setDetailLoading(false); setCreating(true); setEditing(false); setForm(emptyForm()); setDateBaseline(null); setActionError(null) }
  const cancelCreate = () => { viewModeRef.current = 'detail'; setCreating(false); if (selectedId) void loadDetail(selectedId) }

  return <section className="cycle-admin"><header className="section-heading admin-heading"><div><p className="eyebrow">MANAGER</p><h2>Cycle Administration</h2><p>Manage reporting-cycle configuration and durable participant enrollment. Challenge lifecycle remains independent.</p></div><button className="button" type="button" onClick={startCreate}>Create Cycle</button></header>
    <div className="cycle-admin-layout"><aside className="cycle-list" aria-label="Cycle list">
      {listLoading ? <State>Loading cycles…</State> : error && !cycles.length ? <State error>{error}<button className="button button--quiet" onClick={() => void loadList()}>Retry</button></State> : !cycles.length ? <State>No cycles yet. Create the first Active cycle.</State> : cycles.map((cycle) => <button type="button" className={selectedId === cycle.id ? 'cycle-list__item cycle-list__item--selected' : 'cycle-list__item'} key={cycle.id} onClick={() => selectCycle(cycle.id)}><strong>{cycle.name}</strong><span>{cycle.code} · {cycle.status}</span><small>{date(cycle.startsAt)} → {date(cycle.endsAt)}</small><small>{cycle.participantCounts.total} participants</small></button>)}
    </aside><div className="cycle-detail">
      {creating ? <CycleForm title="Create Cycle" form={form} setForm={setForm} pending={pending} error={actionError} onSubmit={save} onCancel={cancelCreate} submit="Create Active cycle" /> : detailLoading ? <State>Loading cycle detail…</State> : error && selectedId ? <State error>{error}<button className="button button--quiet" onClick={() => void loadDetail(selectedId)}>Retry</button></State> : !detail ? <State>Select a cycle to view its details.</State> : editing ? <CycleForm title="Edit Active Cycle" form={form} setForm={setForm} pending={pending} error={actionError} onSubmit={save} onCancel={() => setEditing(false)} onReload={() => void loadDetail(detail.id)} submit="Save cycle" /> : <>
        <article className="workflow-card"><div className="workflow-card__header"><div><span className="category">{detail.code}</span><h3>{detail.name}</h3><p>Status: <strong>{detail.status}</strong></p></div><span className={`cycle-status cycle-status--${detail.status.toLowerCase()}`}>{detail.status}</span></div><dl className="cycle-facts"><div><dt>Starts At</dt><dd>{dateTime(detail.startsAt)}</dd></div><div><dt>Ends At</dt><dd>{dateTime(detail.endsAt)}</dd></div><div><dt>Participants</dt><dd>{detail.participants.length}</dd></div></dl><p className="muted">Changing this reporting cycle does not open or close its challenges.</p><div className="form-actions">{detail.allowedActions.canEdit && <button className="button button--quiet" onClick={() => { setEditing(true); setActionError(null) }}>Edit cycle</button>}{detail.allowedActions.canStartClosing && <button className="button button--warning" onClick={() => void openAction({ kind: 'closing' })}>Start Closing</button>}{detail.allowedActions.canFinalise && <button className="button button--danger-quiet" onClick={() => void openAction({ kind: 'finalise' })}>Finalise Cycle</button>}</div></article>
        <section className="cycle-participants"><header><div><p className="eyebrow">CYCLE ROSTER</p><h3>Enrolled participants</h3></div>{detail.allowedActions.canAddParticipant && <button className="button button--quiet" onClick={() => void openAction({ kind: 'enroll' })}>Enroll participant</button>}</header>{!detail.participants.length ? <State>No participants are enrolled in this cycle.</State> : <div className="table-wrap"><table className="cycle-participant-table"><thead><tr><th>Participant</th><th>Status</th><th>Joined</th><th>Left</th><th>Actions</th></tr></thead><tbody>{detail.participants.map((person) => <tr key={person.participantId}><td>{person.displayName}</td><td><span className="participant-status">{person.status}</span></td><td>{dateTime(person.joinedAt)}</td><td>{dateTime(person.leftAt)}</td><td><StatusActions person={person} onSelect={(status) => void openAction({ kind: 'status', participant: person, status })} /></td></tr>)}</tbody></table></div>}</section>
      </>}
      {action && detail && <ActionPanel action={action} cycle={detail} reason={reason} setReason={setReason} options={options} optionsLoading={optionsLoading} optionsError={optionsError} participantId={participantId} setParticipantId={setParticipantId} pending={pending} error={actionError} onCancel={closeAction} onConfirm={() => void submitAction()} onReload={() => void mutationRefresh(detail.id)} />}
    </div></div>
  </section>
}

function CycleForm({ title, form, setForm, pending, error, onSubmit, onCancel, onReload, submit }: { title: string; form: CreateCycleRequest; setForm: React.Dispatch<React.SetStateAction<CreateCycleRequest>>; pending: boolean; error: string | null; onSubmit: (e: React.FormEvent) => void; onCancel: () => void; onReload?: () => void; submit: string }) { const set = (key: keyof CreateCycleRequest, value: string) => setForm((current) => ({ ...current, [key]: value })); return <form className="workflow-card cycle-form" onSubmit={onSubmit}><h3>{title}</h3>{error && <div className="inline-error" role="alert">{error}{error.includes('changed') && onReload && <button type="button" onClick={onReload}>Reload authoritative cycle</button>}</div>}<div className="form-grid"><label className="field"><span>Code</span><input value={form.code} onChange={(e) => set('code', e.target.value)} /></label><label className="field"><span>Name</span><input value={form.name} onChange={(e) => set('name', e.target.value)} /></label><label className="field"><span>Starts At</span><input type="datetime-local" value={form.startsAt} onChange={(e) => set('startsAt', e.target.value)} /></label><label className="field"><span>Ends At</span><input type="datetime-local" value={form.endsAt} onChange={(e) => set('endsAt', e.target.value)} /></label></div><div className="form-actions"><button type="button" className="button button--quiet" onClick={onCancel} disabled={pending}>Cancel</button><button className="button" disabled={pending}>{pending ? 'Saving…' : submit}</button></div></form> }
function StatusActions({ person, onSelect }: { person: CycleParticipant; onSelect: (status: ParticipantStatus) => void }) { return <div className="status-actions">{person.allowedActions.canSetActive && <button onClick={() => onSelect('Active')}>Set Active</button>}{person.allowedActions.canSetWithdrawn && <button onClick={() => onSelect('Withdrawn')}>Set Withdrawn</button>}{person.allowedActions.canSetInactive && <button onClick={() => onSelect('Inactive')}>Set Inactive</button>}{!person.allowedActions.canSetActive && !person.allowedActions.canSetWithdrawn && !person.allowedActions.canSetInactive && <span>Read-only</span>}</div> }
function ActionPanel({ action, cycle, reason, setReason, options, optionsLoading, optionsError, participantId, setParticipantId, pending, error, onCancel, onConfirm, onReload }: { action: { kind: 'closing' | 'finalise' | 'enroll' | 'status'; participant?: CycleParticipant; status?: ParticipantStatus }; cycle: CycleDetail; reason: string; setReason: (v: string) => void; options: ParticipantOption[]; optionsLoading: boolean; optionsError: string | null; participantId: string; setParticipantId: (v: string) => void; pending: boolean; error: string | null; onCancel: () => void; onConfirm: () => void; onReload: () => void }) {
  const panel = useRef<HTMLElement>(null), title = action.kind === 'closing' ? 'Move this cycle to Closing?' : action.kind === 'finalise' ? 'Finalise this cycle?' : action.kind === 'enroll' ? 'Enroll participant' : `Set ${action.participant?.displayName} to ${action.status}?`
  useEffect(() => { const controls = panel.current?.querySelectorAll<HTMLElement>('select:not(:disabled), textarea:not(:disabled), button:not(:disabled)'); controls?.[0]?.focus() }, [])
  const keyDown = (event: React.KeyboardEvent) => {
    if (event.key === 'Escape' && !pending) { event.preventDefault(); onCancel(); return }
    if (event.key !== 'Tab') return
    const controls = [...(panel.current?.querySelectorAll<HTMLElement>('select:not(:disabled), textarea:not(:disabled), button:not(:disabled)') ?? [])]
    if (!controls.length) return
    const first = controls[0], last = controls[controls.length - 1]
    if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus() }
    else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus() }
  }
  return <div className="cycle-modal-backdrop"><section ref={panel} className="cycle-action" role="dialog" aria-modal="true" aria-labelledby="cycle-action-title" onKeyDown={keyDown}><h3 id="cycle-action-title">{title}</h3>{action.kind === 'finalise' && <p>Finalisation freezes cycle configuration and roster. It does not automatically close challenges.</p>}{action.kind === 'enroll' && (optionsLoading ? <p role="status">Loading participants…</p> : optionsError ? <p className="inline-error" role="alert">Error loading participants: {optionsError}</p> : <><label className="field"><span>Participant</span><select value={participantId} onChange={(e) => setParticipantId(e.target.value)}><option value="">Select participant…</option>{options.map((option) => <option value={option.participantId} key={option.participantId}>{option.displayName}</option>)}</select></label>{!options.length && <p className="muted">No eligible participants.</p>}</>)}<label className="field"><span>Reason</span><textarea maxLength={1000} value={reason} onChange={(e) => setReason(e.target.value)} /></label><small>{reason.length}/1000</small>{error && <div className="inline-error" role="alert">{error}{error.includes('Authoritative') && <button type="button" onClick={onReload}>Reload / Refresh</button>}</div>}<div className="form-actions"><button className="button button--quiet" onClick={onCancel} disabled={pending}>Cancel</button><button className="button" onClick={onConfirm} disabled={pending || (action.kind === 'enroll' && (optionsLoading || Boolean(optionsError) || !options.length))}>{pending ? 'Saving…' : 'Confirm'}</button></div><span className="sr-only">Cycle {cycle.name}</span></section></div>
}
function State({ children, error }: { children: React.ReactNode; error?: boolean }) { return <div className={error ? 'workflow-state inline-error' : 'workflow-state'} role={error ? 'alert' : 'status'}>{children}</div> }
