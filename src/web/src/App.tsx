import { useCallback, useEffect, useState } from 'react'
import { useAuth } from './auth/AuthContext'
import { QUEST_MANAGER_ROLE, QUEST_PARTICIPANT_ROLE } from './auth/types'
import { ChallengeAdministration } from './challenge-admin/ChallengeAdministration'
import { challengeAdminApi, type ChallengeAdminApi } from './challenge-admin/challengeAdminApi'
import { CycleAdministration } from './cycle-admin/CycleAdministration'
import { cycleAdminApi, type CycleAdminApi } from './cycle-admin/cycleAdminApi'
import { ParticipantReportingArea } from './reporting/ParticipantReportingViews'
import { reportingApi, type ReportingApi } from './reporting/reportingApi'
import { RaidAdministration } from './raid-admin/RaidAdministration'
import { raidAdminApi, type RaidAdminApi } from './raid-admin/raidAdminApi'
import { ManagerScoresheet } from './scoresheet/ManagerScoresheet'
import { scoresheetApi, type ScoresheetApi } from './scoresheet/scoresheetApi'
import { ActivityList, ChallengeList, ReviewQueue, SubmissionForm, WorkflowState } from './workflow/WorkflowViews'
import { workflowApi, workflowErrorMessage, type WorkflowApi } from './workflow/workflowApi'
import type { EligibleChallenge, SubmissionView, TaskSummary } from './workflow/types'

const navigation = [
  { id: 'dashboard', label: 'Dashboard', roles: [QUEST_PARTICIPANT_ROLE, QUEST_MANAGER_ROLE] },
  { id: 'challenges', label: 'Challenges', roles: [QUEST_PARTICIPANT_ROLE, QUEST_MANAGER_ROLE] },
  { id: 'submit', label: 'Submit work', roles: [QUEST_PARTICIPANT_ROLE] },
  { id: 'activity', label: 'My activity', roles: [QUEST_PARTICIPANT_ROLE] },
  { id: 'xp-activity', label: 'XP Activity', roles: [QUEST_PARTICIPANT_ROLE] },
  { id: 'team', label: 'My team', roles: [QUEST_PARTICIPANT_ROLE] },
  { id: 'leaderboard', label: 'Leaderboard', roles: [QUEST_PARTICIPANT_ROLE] },
  { id: 'review', label: 'Review Queue', roles: [QUEST_MANAGER_ROLE] },
  { id: 'scoresheet', label: 'Scoresheet', roles: [QUEST_MANAGER_ROLE] },
  { id: 'cycle-administration', label: 'Cycle Administration', roles: [QUEST_MANAGER_ROLE] },
  { id: 'raid-administration', label: 'Raid Administration', roles: [QUEST_MANAGER_ROLE] },
]

const readableRole = (role: string) => role.startsWith('Quest.') ? role.slice(6) : role

function DemoAuthControl({ onSwitched }: { onSwitched: () => void }) {
  const { currentUser, demoProfiles, profilesLoading, switching, error, switchDemoProfile, clearDemoSession } = useAuth()
  const [selectedProfile, setSelectedProfile] = useState('')
  const [lastAttempt, setLastAttempt] = useState('')

  const switchTo = async (profileKey: string) => {
    if (!profileKey) return
    setLastAttempt(profileKey)
    if (await switchDemoProfile(profileKey)) onSwitched()
  }

  return (
    <section className="demo-auth" aria-labelledby="demo-auth-heading">
      <p className="demo-auth__flag">DEVELOPMENT ONLY</p>
      <h2 id="demo-auth-heading">Demo authentication</h2>
      <label><span>Server profile</span>
        <select aria-label="Demo identity" disabled={profilesLoading || switching} value={selectedProfile}
          onChange={(event) => { setSelectedProfile(event.target.value); void switchTo(event.target.value) }}>
          <option value="">{profilesLoading ? 'Loading profiles…' : 'Select demo identity…'}</option>
          {demoProfiles.map((profile) => <option key={profile.key} value={profile.key}>{profile.label}</option>)}
        </select>
      </label>
      {switching && <p className="demo-auth__status" role="status">Switching demo identity…</p>}
      {currentUser?.isAuthenticated && <div className="demo-auth__identity"><span>Active identity</span><strong>{currentUser.displayName}</strong><span>{currentUser.roles.map(readableRole).join(', ')}</span></div>}
      {error && <div className="demo-auth__error" role="alert"><span>{error}</span>{lastAttempt && <button type="button" onClick={() => void switchTo(lastAttempt)} disabled={switching}>Retry</button>}</div>}
      {currentUser?.isAuthenticated && <button className="demo-auth__clear" type="button" onClick={() => void clearDemoSession()} disabled={switching}>Clear session</button>}
    </section>
  )
}

function ManagerDashboard({ loading, error, reviewCount, onNavigate }: { loading: boolean; error: string | null; reviewCount: number; onNavigate: (page: string) => void }) {
  const reviewText = loading ? 'Checking submissions awaiting review…' : error ? 'Review queue status is currently unavailable.' : reviewCount === 0 ? 'No submissions are waiting for review.' : reviewCount === 1 ? '1 submission waiting for review.' : `${reviewCount} submissions waiting for review.`
  return <section aria-labelledby="manager-dashboard-heading"><header className="section-heading"><p className="eyebrow">MANAGER</p><h2 id="manager-dashboard-heading">Manager Dashboard</h2><p>Manage challenges, review participant submissions, and maintain the authoritative XP record.</p></header><div className="manager-dashboard-grid"><article className="reporting-card"><h3>Manage Challenges</h3><p>Create, edit, publish, and review PAS AI Quest challenges.</p><button className="button button--quiet" type="button" onClick={() => onNavigate('challenges')}>Manage challenges</button></article><article className="reporting-card"><h3>Review Submissions</h3><p>{reviewText}</p><button className="button button--quiet" type="button" onClick={() => onNavigate('review')}>Open review queue</button></article><article className="reporting-card"><h3>Scoresheet &amp; XP</h3><p>View cycle scores, record manual XP awards, and correct approved task XP through the audited ledger.</p><button className="button button--quiet" type="button" onClick={() => onNavigate('scoresheet')}>View scoresheet</button></article></div></section>
}

function AuthenticatedShell({ api, reports, challengeAdmin, scoresheet, cycleAdmin, raidAdmin }: { api: WorkflowApi; reports: ReportingApi; challengeAdmin: ChallengeAdminApi; scoresheet: ScoresheetApi; cycleAdmin: CycleAdminApi; raidAdmin: RaidAdminApi }) {
  const { currentUser, demoModeAvailable, switching } = useAuth()
  const [activePage, setActivePage] = useState('dashboard')
  const [challenges, setChallenges] = useState<EligibleChallenge[]>([])
  const [submissions, setSubmissions] = useState<SubmissionView[]>([])
  const [reviewQueue, setReviewQueue] = useState<SubmissionView[]>([])
  const [selected, setSelected] = useState<{ challenge: EligibleChallenge; task: TaskSummary } | null>(null)
  const [loading, setLoading] = useState(true)
  const [workflowError, setWorkflowError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const roles = new Set(currentUser?.roles ?? [])
  const items = navigation.filter((item) => item.roles.some((role) => roles.has(role)))
  const isParticipant = roles.has(QUEST_PARTICIPANT_ROLE)
  const isManager = roles.has(QUEST_MANAGER_ROLE)

  const loadParticipant = useCallback(async () => {
    const [nextChallenges, nextSubmissions] = await Promise.all([api.getEligibleChallenges(), api.getMySubmissions()])
    setChallenges(nextChallenges); setSubmissions(nextSubmissions)
  }, [api])
  const loadManager = useCallback(async () => { setReviewQueue(await api.getReviewQueue()) }, [api])
  const loadWorkflow = useCallback(async () => {
    setLoading(true); setWorkflowError(null)
    try { if (isParticipant) await loadParticipant(); if (isManager) await loadManager() }
    catch (error) { setWorkflowError(workflowErrorMessage(error)) }
    finally { setLoading(false) }
  }, [isParticipant, isManager, loadParticipant, loadManager])

  useEffect(() => { void loadWorkflow() }, [loadWorkflow])

  if (!currentUser) return null

  const navigate = (page: string) => { setNotice(null); if (page !== 'submit') setSelected(null); setActivePage(page) }

  let pageContent: React.ReactNode
  if (activePage === 'dashboard' && isManager) pageContent = <ManagerDashboard loading={loading} error={workflowError} reviewCount={reviewQueue.length} onNavigate={navigate} />
  else if (isParticipant && (activePage === 'dashboard' || activePage === 'team' || activePage === 'leaderboard' || activePage === 'xp-activity')) pageContent = <ParticipantReportingArea page={activePage} api={reports} onNavigate={navigate} />
  else if (activePage === 'challenges' && isParticipant) pageContent = <ChallengeList challenges={challenges} loading={loading} error={workflowError} onSelectTask={(challenge, task) => { setSelected({ challenge, task }); setActivePage('submit') }} />
  else if (activePage === 'submit' && isParticipant) pageContent = selected
    ? <SubmissionForm currentUser={currentUser} challenge={selected.challenge} task={selected.task} api={api} onCancel={() => navigate('challenges')} onSubmitted={() => { setNotice('Submission confirmed by the API.'); setSelected(null); setActivePage('activity'); void loadParticipant() }} />
    : <ChallengeList challenges={challenges} loading={loading} error={workflowError} onSelectTask={(challenge, task) => setSelected({ challenge, task })} />
  else if (activePage === 'activity' && isParticipant) pageContent = <ActivityList submissions={submissions} loading={loading} error={workflowError} api={api} onRefresh={async () => { await loadParticipant(); setNotice('Submission updated from the API.') }} />
  else if (activePage === 'challenges' && isManager) pageContent = <ChallengeAdministration api={challengeAdmin} />
  else if (activePage === 'review' && isManager) pageContent = <ReviewQueue submissions={reviewQueue} loading={loading} error={workflowError} api={api} onRefresh={async () => { await loadManager(); setNotice('Review outcome confirmed by the API.') }} />
  else if (activePage === 'scoresheet' && isManager) pageContent = <ManagerScoresheet api={scoresheet} />
  else if (activePage === 'cycle-administration' && isManager) pageContent = <CycleAdministration api={cycleAdmin} />
  else if (activePage === 'raid-administration' && isManager) pageContent = <RaidAdministration api={raidAdmin} />
  else pageContent = <WorkflowState text="This portal area is outside the current Step 6 workflow." />

  return <div className={`portal-shell${switching ? ' portal-shell--switching' : ''}`} aria-busy={switching}>
    <aside className="sidebar">
      <div className="brand"><span>PAS</span><strong>AI<br />QUEST</strong></div>
      <nav aria-label="Primary navigation">{items.map((item) => <button className={activePage === item.id ? 'nav-active' : ''} type="button" key={item.id} onClick={() => navigate(item.id)} disabled={switching}>{item.label}</button>)}</nav>
      {demoModeAvailable && <DemoAuthControl onSwitched={() => setActivePage('dashboard')} />}
    </aside>
    {demoModeAvailable && <div className="demo-badge">DEVELOPMENT · DEMO AUTH ACTIVE</div>}
    <main className="content">
      <header className="page-header"><div><p className="eyebrow">PAS AI QUEST</p><h1>{navigation.find((item) => item.id === activePage)?.label ?? 'Dashboard'}</h1></div>
        <div className="identity" aria-label="Active identity"><span>Active identity</span><strong>{currentUser.displayName}</strong><div>{currentUser.roles.map((role) => <span className="role" key={role}>{readableRole(role)}</span>)}</div></div>
      </header>
      {notice && <div className="success-notice" role="status">{notice}</div>}
      {pageContent}
    </main>
  </div>
}

export function App({ api = workflowApi, reports = reportingApi, challengeAdmin = challengeAdminApi, scoresheet = scoresheetApi, cycleAdmin = cycleAdminApi, raidAdmin = raidAdminApi }: { api?: WorkflowApi; reports?: ReportingApi; challengeAdmin?: ChallengeAdminApi; scoresheet?: ScoresheetApi; cycleAdmin?: CycleAdminApi; raidAdmin?: RaidAdminApi }) {
  const { status, error, demoModeAvailable, refreshCurrentUser } = useAuth()
  if (status === 'loading') return <main className="state-page" aria-live="polite"><div className="spinner" /><h1>Confirming your identity…</h1></main>
  if (status === 'authenticated') return <AuthenticatedShell api={api} reports={reports} challengeAdmin={challengeAdmin} scoresheet={scoresheet} cycleAdmin={cycleAdmin} raidAdmin={raidAdmin} />
  return <main className="state-page">
    {demoModeAvailable && <DemoAuthControl onSwitched={() => undefined} />}
    <section className="card state-card"><p className="eyebrow">PAS AI QUEST</p><h1>{status === 'unauthenticated' ? 'You are not signed in' : 'We could not confirm your identity'}</h1><p>{error ?? 'Use the approved authentication method to continue. Development users can choose a synthetic demo identity above.'}</p>{status === 'error' && <button className="button" type="button" onClick={() => void refreshCurrentUser().catch(() => undefined)}>Try again</button>}</section>
  </main>
}
