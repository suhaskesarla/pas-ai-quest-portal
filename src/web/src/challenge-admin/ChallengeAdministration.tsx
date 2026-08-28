import { useCallback, useEffect, useState } from 'react'
import { adminError, ChallengeAdminApiError, type ChallengeAdminApi } from './challengeAdminApi'
import type { ChallengeAggregate, ChallengeOptions, ChallengePolicy, ChallengeStatus, ChallengeTaskWrite, CreateChallengeRequest, ScoringMode } from './types'

type LocalTask = ChallengeTaskWrite & { localKey: string }
type FormState = Omit<CreateChallengeRequest, 'tasks'> & { tasks: LocalTask[] }
const localKey = () => crypto.randomUUID()
const inputDate = (value?: string | null) => value ? new Date(value).toISOString().slice(0, 16) : ''
const outputDate = (value: string) => new Date(value).toISOString()
const clean = (value: string) => value.trim() || null
const needsPolicy = (tasks: LocalTask[]) => tasks.some((task) => task.scoringMode !== 'Individual')

export function ChallengeAdministration({ api, startCreating = false }: { api: ChallengeAdminApi; startCreating?: boolean }) {
  const [options, setOptions] = useState<ChallengeOptions | null>(null)
  const [items, setItems] = useState<ChallengeAggregate[]>([])
  const [cycleId, setCycleId] = useState('')
  const [status, setStatus] = useState<ChallengeStatus | ''>('')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [selected, setSelected] = useState<ChallengeAggregate | 'create' | null>(startCreating ? 'create' : null)
  const load = useCallback(async () => {
    setLoading(true); setError(null)
    try { const [nextOptions, challenges] = await Promise.all([api.getOptions(), api.getChallenges(cycleId, status)]); setOptions(nextOptions); setItems(challenges) }
    catch (requestError) { setError(adminError(requestError)) }
    finally { setLoading(false) }
  }, [api, cycleId, status])
  useEffect(() => { void load() }, [load])
  const open = async (item: ChallengeAggregate) => { setError(null); try { setSelected(await api.getChallenge(item.id)) } catch (requestError) { setError(adminError(requestError)) } }
  const replace = (saved: ChallengeAggregate) => { setSelected(saved); setItems((current) => current.some((item) => item.id === saved.id) ? current.map((item) => item.id === saved.id ? saved : item) : [saved, ...current]) }
  if (selected && options) return <ChallengeEditor value={selected} options={options} api={api} onBack={() => { setSelected(null); void load() }} onSaved={replace} />
  return <section><header className="section-heading admin-heading"><div><p className="eyebrow">MANAGER</p><h2>Challenge Administration</h2></div><button className="button" type="button" onClick={() => setSelected('create')} disabled={!options}>Create Challenge</button></header>
    <div className="admin-filters"><label>Cycle<select aria-label="Cycle filter" value={cycleId} onChange={(event) => setCycleId(event.target.value)}><option value="">All cycles</option>{options?.cycles.map((cycle) => <option key={cycle.id} value={cycle.id}>{cycle.name}</option>)}</select></label><label>Status<select aria-label="Status filter" value={status} onChange={(event) => setStatus(event.target.value as ChallengeStatus | '')}><option value="">All statuses</option>{['Draft', 'Published', 'Open', 'Closed', 'Archived'].map((item) => <option key={item}>{item}</option>)}</select></label></div>
    {loading ? <AdminState>Loading manager challenges…</AdminState> : error ? <AdminState error retry={() => void load()}>{error}</AdminState> : !items.length ? <AdminState>No challenges match these filters.</AdminState> : <div className="admin-list">{items.map((item) => <article className="workflow-card" key={item.id}><div className="workflow-card__header"><div><span className="category">{item.cycleCode}</span><h3>{item.name}</h3><p>{item.cycleName} · {item.status}</p></div><span className={`challenge-state challenge-state--${item.status.toLowerCase()}`}>{item.status}</span></div><div className="admin-summary"><span>{new Date(item.openAt).toLocaleDateString()} → {new Date(item.closeAt).toLocaleDateString()}</span><span>{item.tasks.length} tasks</span><strong>{item.tasks.reduce((sum, task) => sum + task.xp, 0)} XP available</strong></div><div className="form-actions"><button className="button button--quiet" type="button" onClick={() => void open(item)}>{item.status === 'Draft' ? 'Edit' : 'View'}</button>{item.status === 'Draft' && <button className="button" type="button" onClick={() => void open(item)}>Publish</button>}</div></article>)}</div>}
  </section>
}

function AdminState({ children, error, retry }: { children: React.ReactNode; error?: boolean; retry?: () => void }) { return <div className={error ? 'workflow-state inline-error' : 'workflow-state'} role={error ? 'alert' : 'status'}>{children}{retry && <div><button className="button button--quiet" onClick={retry}>Retry</button></div>}</div> }

function initial(value: ChallengeAggregate | 'create', options: ChallengeOptions): FormState {
  if (value === 'create') return { cycleId: options.cycles[0]?.id ?? '', name: '', description: null, category: null, openAt: '', dueAt: '', closeAt: '', heroImageReference: null, tasks: [], participationPolicy: null }
  return { cycleId: value.cycleId, name: value.name, description: value.description, category: value.category, openAt: inputDate(value.openAt), dueAt: inputDate(value.dueAt), closeAt: inputDate(value.closeAt), heroImageReference: value.heroImageReference, tasks: value.tasks.map((task) => ({ ...task, localKey: task.id ?? localKey() })), participationPolicy: value.participationPolicy ? { ...value.participationPolicy, formationDeadline: inputDate(value.participationPolicy.formationDeadline) } : null }
}

function ChallengeEditor({ value, options, api, onBack, onSaved }: { value: ChallengeAggregate | 'create'; options: ChallengeOptions; api: ChallengeAdminApi; onBack: () => void; onSaved: (value: ChallengeAggregate) => void }) {
  const [form, setForm] = useState(() => initial(value, options))
  const [server, setServer] = useState(value === 'create' ? null : value)
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({})
  const editable = !server || server.status === 'Draft'
  const dirty = server ? JSON.stringify(form) !== JSON.stringify(initial(server, options)) : true
  const policyNeeded = needsPolicy(form.tasks)
  const set = <K extends keyof FormState>(key: K, next: FormState[K]) => setForm((current) => ({ ...current, [key]: next }))
  const updateTask = (key: string, patch: Partial<LocalTask>) => setForm((current) => ({ ...current, tasks: current.tasks.map((task) => task.localKey === key ? { ...task, ...patch } : task) }))
  const normalizePolicy = (tasks: LocalTask[]) => {
    const required = needsPolicy(tasks)
    return required ? form.participationPolicy ?? { formationMode: options.formationModes[0], minMembers: 2, maxMembers: 4, allowSolo: false, formationDeadline: null, lockAfterStart: true } : null
  }
  const payload = (): CreateChallengeRequest => ({ ...form, description: clean(form.description ?? ''), category: clean(form.category ?? ''), heroImageReference: clean(form.heroImageReference ?? ''), openAt: outputDate(form.openAt), dueAt: outputDate(form.dueAt), closeAt: outputDate(form.closeAt), tasks: form.tasks.map(({ localKey: _, ...task }, index) => ({ ...task, description: clean(task.description ?? ''), sortOrder: index + 1 })), participationPolicy: form.participationPolicy ? { ...form.participationPolicy, formationDeadline: form.participationPolicy.formationDeadline ? outputDate(form.participationPolicy.formationDeadline) : null } : null })
  const save = async (event: React.FormEvent) => {
    event.preventDefault(); setError(null); setFieldErrors({})
    if (!form.name.trim() || !form.openAt || !form.dueAt || !form.closeAt || form.tasks.some((task) => !task.name.trim() || task.xp < 0)) { setError('Complete the challenge name, dates, and valid task fields.'); return }
    setPending(true)
    try { const body = payload(); const saved = server ? await api.updateChallenge(server.id, { ...body, version: server.version }) : await api.createChallenge(body); setServer(saved); setForm(initial(saved, options)); onSaved(saved) }
    catch (requestError) { if (requestError instanceof ChallengeAdminApiError) setFieldErrors(requestError.fieldErrors ?? {}); setError(conflictMessage(requestError)) }
    finally { setPending(false) }
  }
  const reload = async () => { if (!server) return; setPending(true); try { const latest = await api.getChallenge(server.id); setServer(latest); setForm(initial(latest, options)); setError(null); onSaved(latest) } catch (requestError) { setError(adminError(requestError)) } finally { setPending(false) } }
  const publish = async () => {
    if (!server) return
    const taskSummary = server.tasks.map((task) => `${task.name}: ${task.xp} XP, ${task.evidenceRequirement}`).join('\n') || 'No tasks'
    const policy = server.participationPolicy ? `${server.participationPolicy.formationMode}, ${server.participationPolicy.minMembers}–${server.participationPolicy.maxMembers} members` : 'Individual participation'
    if (!window.confirm(`Publish ${server.name}?\n${new Date(server.openAt).toLocaleString()} → ${new Date(server.closeAt).toLocaleString()}\n${taskSummary}\nTotal: ${server.tasks.reduce((sum, task) => sum + task.xp, 0)} XP\nParticipation: ${policy}\n\nPublishing freezes task, scoring, evidence and participation configuration.`)) return
    setPending(true); setError(null)
    try { const published = await api.publishChallenge(server.id, server.version); setServer(published); setForm(initial(published, options)); onSaved(published) } catch (requestError) { setError(conflictMessage(requestError)) } finally { setPending(false) }
  }
  return <section><header className="section-heading admin-heading"><div><p className="eyebrow">{server?.status ?? 'NEW DRAFT'}</p><h2>{server ? server.name : 'Create challenge'}</h2></div><button className="button button--quiet" type="button" onClick={onBack}>Back to list</button></header>{error && <div className="inline-error" role="alert">{error}{server && error.includes('another manager') && <button type="button" onClick={() => void reload()}>Reload latest draft</button>}</div>}{Object.entries(fieldErrors).map(([field, errors]) => <div className="inline-error" key={field}>{field}: {errors.join(' ')}</div>)}
    {!editable && <div className="success-notice">Status: {server?.status}. Published challenges are read-only.</div>}
    <form className="admin-form" onSubmit={save}><fieldset disabled={!editable || pending}><legend>Challenge</legend><div className="form-grid"><label className="field"><span>Cycle</span><select value={form.cycleId} onChange={(e) => set('cycleId', e.target.value)}>{options.cycles.map((cycle) => <option value={cycle.id} key={cycle.id}>{cycle.name}</option>)}</select></label><label className="field"><span>Name</span><input value={form.name} onChange={(e) => set('name', e.target.value)} /></label><label className="field"><span>Description (optional)</span><textarea value={form.description ?? ''} onChange={(e) => set('description', e.target.value)} /></label><label className="field"><span>Category (optional)</span><input value={form.category ?? ''} onChange={(e) => set('category', e.target.value)} /></label><label className="field"><span>Opens</span><input type="datetime-local" value={form.openAt} onChange={(e) => set('openAt', e.target.value)} /></label><label className="field"><span>Due</span><input type="datetime-local" value={form.dueAt} onChange={(e) => set('dueAt', e.target.value)} /></label><label className="field"><span>Closes</span><input type="datetime-local" value={form.closeAt} onChange={(e) => set('closeAt', e.target.value)} /></label><label className="field"><span>Hero image reference (optional)</span><input value={form.heroImageReference ?? ''} onChange={(e) => set('heroImageReference', e.target.value)} /></label></div></fieldset>
      <fieldset disabled={!editable || pending}><legend>Tasks</legend>{form.tasks.map((task, index) => <div className="admin-task" key={task.localKey}><div className="admin-task__heading"><strong>Task {index + 1}</strong><div><button type="button" disabled={index === 0} onClick={() => set('tasks', move(form.tasks, index, index - 1))}>↑</button><button type="button" disabled={index === form.tasks.length - 1} onClick={() => set('tasks', move(form.tasks, index, index + 1))}>↓</button><button type="button" onClick={() => { const tasks = form.tasks.filter((item) => item.localKey !== task.localKey); setForm((current) => ({ ...current, tasks, participationPolicy: normalizePolicy(tasks) })) }}>Remove</button></div></div><div className="form-grid"><label className="field"><span>Task name</span><input value={task.name} onChange={(e) => updateTask(task.localKey, { name: e.target.value })} /></label><label className="field"><span>Description (optional)</span><input value={task.description ?? ''} onChange={(e) => updateTask(task.localKey, { description: e.target.value })} /></label><label className="field"><span>XP</span><input type="number" min="0" value={task.xp} onChange={(e) => updateTask(task.localKey, { xp: Number(e.target.value) })} /></label><label className="field"><span>Scoring mode</span><select value={task.scoringMode} onChange={(e) => { const scoringMode = e.target.value as ScoringMode; const tasks = form.tasks.map((item) => item.localKey === task.localKey ? { ...item, scoringMode } : item); setForm((current) => ({ ...current, tasks, participationPolicy: normalizePolicy(tasks) })) }}>{options.scoringModes.map((mode) => <option key={mode}>{mode}</option>)}</select></label><label className="field"><span>Evidence requirement</span><select value={task.evidenceRequirement} onChange={(e) => updateTask(task.localKey, { evidenceRequirement: e.target.value as LocalTask['evidenceRequirement'] })}>{options.evidenceRequirements.map((requirement) => <option key={requirement}>{requirement}</option>)}</select></label></div></div>)}<button className="button button--quiet" type="button" onClick={() => set('tasks', [...form.tasks, { localKey: localKey(), id: null, name: '', description: null, xp: 0, scoringMode: options.scoringModes[0], evidenceRequirement: options.evidenceRequirements[0], sortOrder: form.tasks.length + 1 }])}>Add task</button></fieldset>
      {policyNeeded && form.participationPolicy && <PolicyFields value={form.participationPolicy} options={options} disabled={!editable || pending} onChange={(participationPolicy) => set('participationPolicy', participationPolicy)} />}
      <div className="form-actions">{editable && <button className="button" disabled={pending}>{pending ? 'Saving…' : server ? 'Save full draft' : 'Create draft'}</button>}{server?.status === 'Draft' && <button className="button button--approve" type="button" disabled={pending || dirty} title={dirty ? 'Save the full draft before publishing.' : undefined} onClick={() => void publish()}>{dirty ? 'Save before publish' : 'Publish challenge'}</button>}</div></form>
  </section>
}

function PolicyFields({ value, options, disabled, onChange }: { value: ChallengePolicy; options: ChallengeOptions; disabled: boolean; onChange: (value: ChallengePolicy) => void }) { const patch = (next: Partial<ChallengePolicy>) => onChange({ ...value, ...next }); return <fieldset disabled={disabled}><legend>Participation policy</legend><div className="form-grid"><label className="field"><span>Formation mode</span><select value={value.formationMode} onChange={(e) => patch({ formationMode: e.target.value as ChallengePolicy['formationMode'] })}>{options.formationModes.map((mode) => <option key={mode}>{mode}</option>)}</select></label><label className="field"><span>Minimum members</span><input type="number" min="1" value={value.minMembers} onChange={(e) => patch({ minMembers: Number(e.target.value) })} /></label><label className="field"><span>Maximum members</span><input type="number" min="1" value={value.maxMembers} onChange={(e) => patch({ maxMembers: Number(e.target.value) })} /></label><label className="check-row"><input type="checkbox" checked={value.allowSolo} onChange={(e) => patch({ allowSolo: e.target.checked, minMembers: e.target.checked ? 1 : Math.max(2, value.minMembers) })} />Allow solo</label><label className="field"><span>Formation deadline</span><input type="datetime-local" value={value.formationDeadline ?? ''} onChange={(e) => patch({ formationDeadline: e.target.value || null })} /></label><label className="check-row"><input type="checkbox" checked={value.lockAfterStart} onChange={(e) => patch({ lockAfterStart: e.target.checked })} />Lock after start</label></div></fieldset> }
function move<T>(items: T[], from: number, to: number) { const next = [...items]; const [item] = next.splice(from, 1); next.splice(to, 0, item); return next }
function conflictMessage(error: unknown) { if (error instanceof ChallengeAdminApiError && error.code === 'ChallengeVersionConflict') return 'This draft was changed by another manager. Reload the latest draft before editing again.'; if (error instanceof ChallengeAdminApiError && error.code === 'ChallengeHasOperationalDependencies') return 'This Draft can no longer be edited because operational data depends on it.'; return adminError(error) }
