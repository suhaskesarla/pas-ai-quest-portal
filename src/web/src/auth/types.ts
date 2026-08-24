export type CurrentUser = {
  isAuthenticated: boolean
  participantId: string | null
  displayName: string | null
  roles: string[]
}

export type DemoProfile = { key: string; label: string }
export type AuthStatus = 'loading' | 'authenticated' | 'unauthenticated' | 'error'

export const QUEST_PARTICIPANT_ROLE = 'Quest.Participant'
export const QUEST_MANAGER_ROLE = 'Quest.Manager'
