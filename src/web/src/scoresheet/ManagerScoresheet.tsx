import { useCallback, useEffect, useRef, useState } from 'react'
import type { XpActivityItem } from '../reporting/types'
import { scoresheetErrorMessage, type ScoresheetApi } from './scoresheetApi'
import type { ManagerReportingCycles, ScoresheetDetail, ScoresheetSummary } from './types'

const signed = (amount: number) => amount >= 0 ? `+${amount} XP` : `−${Math.abs(amount)} XP`
const date = (value: string) => new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value))
function State({ text, error, retry }: { text: string; error?: boolean; retry?: () => void }) { return <div className={`reporting-state${error ? ' reporting-state--error' : ''}`} role={error ? 'alert' : 'status'}><p>{text}</p>{retry && <button className="button button--quiet" type="button" onClick={retry}>Retry</button>}</div> }

export function ManagerScoresheet({ api }: { api: ScoresheetApi }) {
  const [cycles, setCycles] = useState<ManagerReportingCycles | null>(null)
  const [cycleId, setCycleId] = useState('')
  const [summary, setSummary] = useState<ScoresheetSummary | null>(null)
  const [loadingCycles, setLoadingCycles] = useState(true)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const summaryRequest = useRef(0)
  const loadCycles = useCallback(async () => { setLoadingCycles(true); setError(null); try { const result = await api.getReportingCycles(); setCycles(result); setCycleId((current) => result.cycles.some((cycle) => cycle.id === current) ? current : result.defaultCycleId ?? '') } catch (requestError) { setError(scoresheetErrorMessage(requestError)) } finally { setLoadingCycles(false) } }, [api])
  const loadSummary = useCallback(async () => { if (!cycleId) return; const request = ++summaryRequest.current; setLoading(true); setError(null); setSelectedId(null); try { const result = await api.getScoresheet(cycleId); if (request === summaryRequest.current) setSummary(result) } catch (requestError) { if (request === summaryRequest.current) setError(scoresheetErrorMessage(requestError)) } finally { if (request === summaryRequest.current) setLoading(false) } }, [api, cycleId])
  useEffect(() => { void loadCycles() }, [loadCycles])
  useEffect(() => { void loadSummary() }, [loadSummary])
  if (loadingCycles) return <State text="Loading manager reporting cycles…" />
  if (error && !cycles) return <State text={error} error retry={() => void loadCycles()} />
  if (!cycles?.cycles.length || !cycleId) return <State text="No reporting cycles are available for the Scoresheet." />
  const selectedCycle = cycles.cycles.find((cycle) => cycle.id === cycleId)!
  return <div className="reporting-area"><div className="cycle-context"><div><p className="eyebrow">MANAGER REPORTING</p><strong>{selectedCycle.name}</strong><span>Cycle selection changes this Scoresheet reporting context.</span></div><label><span>Reporting cycle</span><select aria-label="Scoresheet reporting cycle" value={cycleId} onChange={(event) => { setSelectedId(null); setCycleId(event.target.value) }}>{cycles.cycles.map((cycle) => <option key={cycle.id} value={cycle.id}>{cycle.name} · {cycle.status}</option>)}</select></label></div>
    {loading ? <State text="Loading Scoresheet…" /> : error ? <State text={error} error retry={() => void loadSummary()} /> : !summary?.rows.length ? <State text="No participants are enrolled in this reporting cycle." /> : <section><header className="section-heading"><p className="eyebrow">APPEND-ONLY XP LEDGER</p><h2>Scoresheet summary</h2><p>All cycle participants are shown, including zero-XP and inactive roster entries.</p></header><div className="table-wrap"><table className="scoresheet-table"><thead><tr><th>Participant</th><th>Status</th><th>Task Approval XP</th><th>Manual Award XP</th><th>Raid XP</th><th>Adjustments</th><th>Total XP</th></tr></thead><tbody>{summary.rows.map((row) => <tr key={row.participantId}><td><button className="participant-link" type="button" onClick={() => setSelectedId(row.participantId)}>{row.displayName}</button></td><td><span className="participant-status">{row.participantStatus}</span></td><td>{row.bySource.taskApprovalXp}</td><td>{row.bySource.manualAwardXp}</td><td>{row.bySource.raidXp}</td><td className={row.byEntryType.netAdjustmentXp < 0 ? 'xp-negative' : ''}>{row.byEntryType.netAdjustmentXp}</td><td><strong>{row.totalXp}</strong></td></tr>)}</tbody></table></div></section>}
    {selectedId && <ParticipantDetail key={`${cycleId}:${selectedId}`} api={api} cycleId={cycleId} participantId={selectedId} onClose={() => setSelectedId(null)} />}
  </div>
}

function LedgerRows({ items }: { items: XpActivityItem[] }) { return <div className="activity-rows scoresheet-ledger">{items.map((item) => <article key={item.id}><strong className={item.amount < 0 ? 'xp-negative' : 'xp-positive'}>{signed(item.amount)}</strong><div><h3>{item.source.label}</h3><p>{item.entryType} · {item.sourceType}</p><p>{item.reason}</p><span>{date(item.awardedAt)}{item.reversesEntryId ? ` · Reverses ${item.reversesEntryId}` : ''}</span></div></article>)}</div> }

function ParticipantDetail({ api, cycleId, participantId, onClose }: { api: ScoresheetApi; cycleId: string; participantId: string; onClose: () => void }) {
  const [detail, setDetail] = useState<ScoresheetDetail | null>(null); const [items, setItems] = useState<XpActivityItem[]>([]); const [nextCursor, setNextCursor] = useState<string | null>(null); const [loading, setLoading] = useState(true); const [loadingMore, setLoadingMore] = useState(false); const [error, setError] = useState<string | null>(null)
  const load = useCallback(async (cursor?: string | null) => { cursor ? setLoadingMore(true) : setLoading(true); setError(null); try { const result = await api.getParticipant(participantId, cycleId, cursor); setDetail(result); setItems((current) => cursor ? [...current, ...result.items.filter((item) => !current.some((existing) => existing.id === item.id))] : result.items); setNextCursor(result.nextCursor) } catch (requestError) { setError(scoresheetErrorMessage(requestError)) } finally { setLoading(false); setLoadingMore(false) } }, [api, cycleId, participantId])
  useEffect(() => { void load(null) }, [load])
  return <section className="scoresheet-detail reporting-card" aria-label="Participant Scoresheet detail">{loading ? <State text="Loading participant ledger…" /> : error && !detail ? <State text={error} error retry={() => void load(null)} /> : detail && <><header className="detail-heading"><div><p className="eyebrow">PARTICIPANT DRILL-DOWN</p><h2>{detail.participant.displayName}</h2><span className="participant-status">{detail.participant.participantStatus}</span></div><button className="button button--quiet" type="button" onClick={onClose}>Close</button></header><div className="reporting-stats scoresheet-breakdown"><article><span>Total XP</span><strong>{detail.totalXp}</strong></article><article><span>Task Approval</span><strong>{detail.bySource.taskApprovalXp}</strong></article><article><span>Manual Award</span><strong>{detail.bySource.manualAwardXp}</strong></article><article><span>Raid</span><strong>{detail.bySource.raidXp}</strong></article></div>{items.length ? <LedgerRows items={items} /> : <State text="This participant has no XP ledger entries in the selected cycle." />}{error && items.length > 0 && <State text={error} error retry={() => void load(nextCursor)} />}{!error && nextCursor && <button className="button button--quiet load-more" type="button" disabled={loadingMore} onClick={() => void load(nextCursor)}>{loadingMore ? 'Loading more…' : 'Load more'}</button>}</>}</section>
}
