import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { AuthApiError, authApi, type AuthApi } from './authApi'
import type { AuthStatus, CurrentUser, DemoProfile } from './types'

type AuthContextValue = {
  status: AuthStatus
  currentUser: CurrentUser | null
  error: string | null
  demoModeAvailable: boolean
  demoProfiles: DemoProfile[]
  profilesLoading: boolean
  switching: boolean
  refreshCurrentUser: () => Promise<void>
  switchDemoProfile: (profileKey: string) => Promise<boolean>
  clearDemoSession: () => Promise<void>
}

const AuthContext = createContext<AuthContextValue | null>(null)

function messageFor(error: unknown) {
  return error instanceof AuthApiError && error.status === 403
    ? 'You do not have permission to use that identity.'
    : 'Authentication is temporarily unavailable. Please try again.'
}

export function AuthProvider({ children, demoModeAvailable, api = authApi }: {
  children: ReactNode
  demoModeAvailable: boolean
  api?: AuthApi
}) {
  const [status, setStatus] = useState<AuthStatus>('loading')
  const [currentUser, setCurrentUser] = useState<CurrentUser | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [demoProfiles, setDemoProfiles] = useState<DemoProfile[]>([])
  const [profilesLoading, setProfilesLoading] = useState(demoModeAvailable)
  const [switching, setSwitching] = useState(false)

  const refreshCurrentUser = useCallback(async () => {
    try {
      const user = await api.getCurrentUser()
      setCurrentUser(user)
      setStatus(user.isAuthenticated ? 'authenticated' : 'unauthenticated')
      setError(null)
    } catch (requestError) {
      setCurrentUser(null)
      if (requestError instanceof AuthApiError && requestError.status === 401) {
        setStatus('unauthenticated')
        setError(null)
      } else {
        setStatus('error')
        setError(messageFor(requestError))
      }
      throw requestError
    }
  }, [api])

  useEffect(() => { void refreshCurrentUser().catch(() => undefined) }, [refreshCurrentUser])

  useEffect(() => {
    if (!demoModeAvailable) {
      setDemoProfiles([])
      setProfilesLoading(false)
      return
    }
    let active = true
    setProfilesLoading(true)
    api.getDemoProfiles()
      .then((profiles) => { if (active) setDemoProfiles(profiles) })
      .catch((requestError) => { if (active) setError(messageFor(requestError)) })
      .finally(() => { if (active) setProfilesLoading(false) })
    return () => { active = false }
  }, [api, demoModeAvailable])

  const switchDemoProfile = useCallback(async (profileKey: string) => {
    setSwitching(true)
    setError(null)
    try {
      await api.establishDemoSession(profileKey)
      const confirmedUser = await api.getCurrentUser()
      setCurrentUser(confirmedUser)
      setStatus(confirmedUser.isAuthenticated ? 'authenticated' : 'unauthenticated')
      setError(null)
      return true
    } catch (requestError) {
      setError(messageFor(requestError))
      return false
    } finally {
      setSwitching(false)
    }
  }, [api])

  const clearDemoSession = useCallback(async () => {
    setSwitching(true)
    setError(null)
    try {
      await api.clearDemoSession()
      await refreshCurrentUser()
    } catch (requestError) {
      setError(messageFor(requestError))
    } finally {
      setSwitching(false)
    }
  }, [api, refreshCurrentUser])

  const value = useMemo(() => ({ status, currentUser, error, demoModeAvailable, demoProfiles,
    profilesLoading, switching, refreshCurrentUser, switchDemoProfile, clearDemoSession }),
    [status, currentUser, error, demoModeAvailable, demoProfiles, profilesLoading, switching,
      refreshCurrentUser, switchDemoProfile, clearDemoSession])

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth() {
  const context = useContext(AuthContext)
  if (!context) throw new Error('useAuth must be used inside AuthProvider')
  return context
}
