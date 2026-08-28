import { useCallback, useEffect, useState } from 'react'
import type { ReportingApi } from './reportingApi'
import { reportingErrorMessage } from './reportingApi'
import type { Dashboard, LeaderboardEntry, ParticipantTeam, ReportingCycles, XpActivityItem } from './types'

const date = (value: string) => new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value))
const signedXp = (amount: number) => amount >= 0 ? `+${amount} XP` : `−${Math.abs(amount)} XP`
const sourceTypeLabel = (sourceType: XpActivityItem['sourceType']) => ({ TaskApproval: 'Task Approval', ManualAward: 'Manual Award', Raid: 'Raid' })[sourceType]

function State({ children, error, retry }: { children: React.ReactNode; error?: boolean; retry?: () => void }) {
  return <div className={`reporting-state${error ? ' reporting-state--error' : ''}`} role={error ? 'alert' : 'status'}><p>{children}</p>{retry && <button className="button button--quiet" type="button" onClick={retry}>Retry</button>}</div>
}

function useCycleResource<T>(cycleId: string, load: (cycleId: string) => Promise<T>) {
  const [data, setData] = useState<T | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const reload = useCallback(async () => {
    setLoading(true); setError(null)
    try { setData(await load(cycleId)) }
    catch (requestError) { setError(reportingErrorMessage(requestError)) }
    finally { setLoading(false) }
  }, [cycleId, load])
  useEffect(() => { void reload() }, [reload])
  return { data, loading, error, reload }
}

export function ParticipantReportingArea({ page, api, onNavigate }: { page: 'dashboard' | 'team' | 'leaderboard' | 'xp-activity'; api: ReportingApi; onNavigate: (page: string) => void }) {
  const [cycles, setCycles] = useState<ReportingCycles | null>(null)
  const [cycleId, setCycleId] = useState('')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const loadCycles = useCallback(async () => {
    setLoading(true); setError(null)
    try {
      const response = await api.getReportingCycles()
      setCycles(response)
      setCycleId((current) => response.cycles.some((cycle) => cycle.id === current) ? current : response.defaultCycleId ?? '')
    } catch (requestError) { setError(reportingErrorMessage(requestError)) }
    finally { setLoading(false) }
  }, [api])
  useEffect(() => { void loadCycles() }, [loadCycles])

  if (loading) return <State>Loading reporting cycles…</State>
  if (error) return <State error retry={() => void loadCycles()}>{error}</State>
  if (!cycles?.cycles.length || !cycleId) return <State>You do not have any reporting cycles yet. Reporting views will appear after you are enrolled in a cycle.</State>
  const selected = cycles.cycles.find((cycle) => cycle.id === cycleId)!
  return <div className="reporting-area">
    <div className="cycle-context"><div><p className="eyebrow">REPORTING CONTEXT</p><strong>{selected.name}</strong><span>This selection changes reporting views only. Challenge eligibility still follows each challenge’s dates and status.</span></div><label><span>Reporting cycle</span><select aria-label="Reporting cycle" value={cycleId} onChange={(event) => setCycleId(event.target.value)}>{cycles.cycles.map((cycle) => <option value={cycle.id} key={cycle.id}>{cycle.name} · {cycle.status}</option>)}</select></label></div>
    {page === 'dashboard' && <DashboardView key={cycleId} cycleId={cycleId} api={api} onNavigate={onNavigate} />}
    {page === 'team' && <TeamView key={cycleId} cycleId={cycleId} api={api} />}
    {page === 'leaderboard' && <LeaderboardView key={cycleId} cycleId={cycleId} api={api} />}
    {page === 'xp-activity' && <XpActivityView key={cycleId} cycleId={cycleId} api={api} />}
  </div>
}

function DashboardView({ cycleId, api, onNavigate }: { cycleId: string; api: ReportingApi; onNavigate: (page: string) => void }) {
  const load = useCallback((id: string) => api.getDashboard(id), [api])
  const { data, loading, error, reload } = useCycleResource<Dashboard>(cycleId, load)
  if (loading) return <State>Loading dashboard…</State>
  if (error) return <State error retry={() => void reload()}>{error}</State>
  if (!data) return null
  const statusOrder = ['NeedsEvidence', 'Submitted', 'UnderReview', 'Resubmitted', 'Approved', 'Rejected']
  const statuses = statusOrder.filter((status) => data.submissionStatusCounts[status] !== undefined)
  return <section aria-labelledby="dashboard-heading"><header className="section-heading"><p className="eyebrow">{data.cycle.code}</p><h2 id="dashboard-heading">Welcome, {data.participant.displayName}</h2><p>Your selected-cycle progress at a glance.</p></header>
    <div className="reporting-stats"><article><span>Total XP</span><strong>{data.totalXp}</strong></article><article><span>Individual rank</span><strong>{data.individualRank === null ? 'Not ranked' : `#${data.individualRank}`}</strong></article><article><span>Eligible challenges</span><strong>{data.eligibleChallengeCount}</strong></article></div>
    <div className="dashboard-grid"><section className="reporting-card"><h3>Submission status</h3>{statuses.length ? <div className="status-summary">{statuses.map((status) => <button type="button" key={status} className={status === 'NeedsEvidence' ? 'status-summary__action' : ''} onClick={() => onNavigate('activity')}><span>{status === 'NeedsEvidence' ? 'Needs evidence' : status === 'UnderReview' ? 'Under review' : status}</span><strong>{data.submissionStatusCounts[status]}</strong></button>)}</div> : <p className="muted">No submissions in this reporting cycle.</p>}</section>
      <section className="reporting-card"><h3>Recent XP activity</h3>{data.recentActivity.length ? <ActivityRows items={data.recentActivity} compact /> : <p className="muted">No XP activity yet.</p>}</section></div>
    <section className="reporting-card non-xp"><header><div><p className="eyebrow">NON-XP RESOURCE</p><h3>Raid passes</h3></div><span>Not included in Total XP</span></header>{data.raidPassBalance.length ? <div className="raid-grid">{data.raidPassBalance.map((pass) => <div key={pass.passType}><strong>{pass.passType}</strong><span>{pass.remaining} remaining</span><small>{pass.assigned} assigned · {pass.used} used</small></div>)}</div> : <p className="muted">No raid passes assigned for this cycle.</p>}</section>
  </section>
}

function MemberList({ members }: { members: ParticipantTeam['challengeGroups'][number]['members'] }) {
  return <ul className="member-list">{members.map((member) => <li key={member.participantId}><span>{member.displayName}</span>{member.isCurrentParticipant && <mark>You</mark>}</li>)}</ul>
}

function TeamView({ cycleId, api }: { cycleId: string; api: ReportingApi }) {
  const load = useCallback((id: string) => api.getTeam(id), [api])
  const { data, loading, error, reload } = useCycleResource<ParticipantTeam>(cycleId, load)
  if (loading) return <State>Loading your team…</State>
  if (error) return <State error retry={() => void reload()}>{error}</State>
  if (!data) return null
  return <div className="team-sections"><section className="reporting-card cycle-team" aria-labelledby="cycle-team-heading"><p className="eyebrow">MY CYCLE TEAM</p><h2 id="cycle-team-heading">{data.team?.name ?? 'No cycle team assigned'}</h2>{data.team ? <MemberList members={data.team.members} /> : <p className="muted">You do not have an open cycle-team assignment for this reporting cycle.</p>}</section>
    <section aria-labelledby="challenge-groups-heading"><header className="section-heading"><p className="eyebrow">CHALLENGE GROUPS</p><h2 id="challenge-groups-heading">Challenge participation snapshots</h2><p>These groups are separate from your cycle team.</p></header>{data.challengeGroups.length ? <div className="group-grid">{data.challengeGroups.map((group) => <article className="reporting-card" key={group.participationId}><div className="group-heading"><h3>{group.challengeName}</h3><span className={`challenge-state challenge-state--${group.challengeStatus.toLowerCase()}`}>{group.challengeStatus}</span></div><MemberList members={group.members} /></article>)}</div> : <State>No challenge groups for this reporting cycle.</State>}</section>
  </div>
}

function LeaderboardView({ cycleId, api }: { cycleId: string; api: ReportingApi }) {
  const load = useCallback((id: string) => api.getIndividualLeaderboard(id), [api])
  const { data, loading, error, reload } = useCycleResource<LeaderboardEntry[]>(cycleId, load)
  if (loading) return <State>Loading individual leaderboard…</State>
  if (error) return <State error retry={() => void reload()}>{error}</State>
  if (!data?.length) return <State>No active participants are available for this reporting cycle.</State>
  return <section><header className="section-heading"><p className="eyebrow">INDIVIDUAL</p><h2>Leaderboard</h2><p>Ranks and XP are supplied by the reporting API.</p></header><div className="table-wrap"><table className="leaderboard-table"><thead><tr><th>Rank</th><th>Participant</th><th>XP</th></tr></thead><tbody>{data.map((entry) => <tr key={entry.participantId} className={entry.isCurrentParticipant ? 'current-participant' : ''}><td>{entry.rank}</td><td>{entry.displayName}{entry.isCurrentParticipant && <mark>You</mark>}</td><td>{entry.totalXp} XP</td></tr>)}</tbody></table></div></section>
}

function ActivityRows({ items, compact = false }: { items: XpActivityItem[]; compact?: boolean }) {
  return <div className={`activity-rows${compact ? ' activity-rows--compact' : ''}`}>{items.map((item) => <article key={item.id}><strong className={item.amount < 0 ? 'xp-negative' : 'xp-positive'}>{signedXp(item.amount)}</strong><div><h3>{item.source.label}</h3><p>{item.reason}</p><span>{sourceTypeLabel(item.sourceType)} · {item.entryType} · {date(item.awardedAt)}</span></div></article>)}</div>
}

function XpActivityView({ cycleId, api }: { cycleId: string; api: ReportingApi }) {
  const [items, setItems] = useState<XpActivityItem[]>([])
  const [nextCursor, setNextCursor] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [loadingMore, setLoadingMore] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const load = useCallback(async (cursor?: string | null) => {
    cursor ? setLoadingMore(true) : setLoading(true)
    setError(null)
    try { const page = await api.getXpActivity(cycleId, cursor); setItems((current) => cursor ? [...current, ...page.items] : page.items); setNextCursor(page.nextCursor) }
    catch (requestError) { setError(reportingErrorMessage(requestError)) }
    finally { setLoading(false); setLoadingMore(false) }
  }, [api, cycleId])
  useEffect(() => { setItems([]); setNextCursor(null); void load(null) }, [load])
  if (loading) return <State>Loading XP activity…</State>
  if (error && !items.length) return <State error retry={() => void load(null)}>{error}</State>
  return <section><header className="section-heading"><p className="eyebrow">APPEND-ONLY LEDGER</p><h2>XP Activity</h2><p>Grants, reversals and corrections remain visible as separate entries.</p></header>{items.length ? <ActivityRows items={items} /> : <State>No XP activity in this reporting cycle.</State>}
    {error && items.length > 0 && <State error retry={() => void load(nextCursor)}>{error}</State>}
    {!error && nextCursor && <button className="button button--quiet load-more" type="button" disabled={loadingMore} onClick={() => void load(nextCursor)}>{loadingMore ? 'Loading more…' : 'Load more'}</button>}
  </section>
}
