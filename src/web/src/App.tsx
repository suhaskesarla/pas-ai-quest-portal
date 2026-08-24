import { useState } from 'react'
import { useAuth } from './auth/AuthContext'
import { QUEST_MANAGER_ROLE, QUEST_PARTICIPANT_ROLE } from './auth/types'

const navigation = [
  { id: 'dashboard', label: 'Dashboard', roles: [QUEST_PARTICIPANT_ROLE, QUEST_MANAGER_ROLE] },
  { id: 'challenges', label: 'Challenges', roles: [QUEST_PARTICIPANT_ROLE, QUEST_MANAGER_ROLE] },
  { id: 'submit', label: 'Submit work', roles: [QUEST_PARTICIPANT_ROLE] },
  { id: 'activity', label: 'My activity', roles: [QUEST_PARTICIPANT_ROLE] },
  { id: 'team', label: 'My team', roles: [QUEST_PARTICIPANT_ROLE] },
  { id: 'leaderboard', label: 'Leaderboard', roles: [QUEST_PARTICIPANT_ROLE, QUEST_MANAGER_ROLE] },
  { id: 'new-challenge', label: 'New challenge', roles: [QUEST_MANAGER_ROLE] },
  { id: 'review', label: 'Review queue', roles: [QUEST_MANAGER_ROLE] },
  { id: 'scoresheet', label: 'Scoresheet', roles: [QUEST_MANAGER_ROLE] },
  { id: 'analytics', label: 'Analytics', roles: [QUEST_MANAGER_ROLE] },
  { id: 'cycles', label: 'Cycle administration', roles: [QUEST_MANAGER_ROLE] },
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

function AuthenticatedShell() {
  const { currentUser, demoModeAvailable, switching } = useAuth()
  const [activePage, setActivePage] = useState('dashboard')
  if (!currentUser) return null
  const roles = new Set(currentUser.roles)
  const items = navigation.filter((item) => item.roles.some((role) => roles.has(role)))

  return <div className={`portal-shell${switching ? ' portal-shell--switching' : ''}`} aria-busy={switching}>
    <aside className="sidebar">
      <div className="brand"><span>PAS</span><strong>AI<br />QUEST</strong></div>
      <nav aria-label="Primary navigation">{items.map((item) => <button className={activePage === item.id ? 'nav-active' : ''} type="button" key={item.id} onClick={() => setActivePage(item.id)} disabled={switching}>{item.label}</button>)}</nav>
      {demoModeAvailable && <DemoAuthControl onSwitched={() => setActivePage('dashboard')} />}
    </aside>
    {demoModeAvailable && <div className="demo-badge">DEVELOPMENT · DEMO AUTH ACTIVE</div>}
    <main className="content">
      <header className="page-header"><div><p className="eyebrow">AUTH SCAFFOLDING</p><h1>{navigation.find((item) => item.id === activePage)?.label ?? 'Dashboard'}</h1></div>
        <div className="identity" aria-label="Active identity"><span>Active identity</span><strong>{currentUser.displayName}</strong><div>{currentUser.roles.map((role) => <span className="role" key={role}>{readableRole(role)}</span>)}</div></div>
      </header>
      <section className="card"><p className="eyebrow">STEP 5A</p><h2>Welcome, {currentUser.displayName}</h2><p>Your identity and available navigation were confirmed by the API. Portal workflows are intentionally deferred.</p></section>
    </main>
  </div>
}

export function App() {
  const { status, error, demoModeAvailable, refreshCurrentUser } = useAuth()
  if (status === 'loading') return <main className="state-page" aria-live="polite"><div className="spinner" /><h1>Confirming your identity…</h1></main>
  if (status === 'authenticated') return <AuthenticatedShell />
  return <main className="state-page">
    {demoModeAvailable && <DemoAuthControl onSwitched={() => undefined} />}
    <section className="card state-card"><p className="eyebrow">PAS AI QUEST</p><h1>{status === 'unauthenticated' ? 'You are not signed in' : 'We could not confirm your identity'}</h1><p>{error ?? 'Use the approved authentication method to continue. Development users can choose a synthetic demo identity above.'}</p>{status === 'error' && <button className="button" type="button" onClick={() => void refreshCurrentUser().catch(() => undefined)}>Try again</button>}</section>
  </main>
}
