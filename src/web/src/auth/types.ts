export type CurrentUser = {
  isAuthenticated: boolean
  participantId: string | null
  displayName: string | null
  roles: string[]
}

export type DemoProfile = { key: string; label: string }
export type AuthStatus = 'initializing' | 'unauthenticated' | 'authenticating' | 'session_recovery' | 'loading_profile' | 'authenticated' | 'unprovisioned' | 'access_denied' | 'session_error' | 'error'

export const QUEST_PARTICIPANT_ROLE = 'Quest.Participant'
export const QUEST_MANAGER_ROLE = 'Quest.Manager'
