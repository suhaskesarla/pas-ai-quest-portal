import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import type { IPublicClientApplication } from '@azure/msal-browser'
import { AuthApiError, authApi, type AuthApi } from './authApi'
import { configureAuthenticatedApi } from './apiClient'
import { loginRequest, safeSameOriginReturnUrl, type EntraFrontendConfig, type FrontendAuthMode } from './authConfig'
import { AuthenticationRecoveryAlreadyAttempted, authenticationRecoveryStartedOnThisPage, clearAuthenticationRecovery, hasAuthenticationRecoveryMarker, recoverAuthentication } from './authRecovery'
import { createAccessTokenProvider } from './authToken'
import type { AuthStatus, CurrentUser, DemoProfile } from './types'

type AuthContextValue = {
  status: AuthStatus; currentUser: CurrentUser | null; error: string | null; mode: FrontendAuthMode
  demoModeAvailable: boolean; demoProfiles: DemoProfile[]; profilesLoading: boolean; switching: boolean
  refreshCurrentUser: () => Promise<void>; login: () => Promise<void>; logout: () => Promise<void>
  switchDemoProfile: (profileKey: string) => Promise<boolean>; clearDemoSession: () => Promise<void>
}
const AuthContext = createContext<AuthContextValue | null>(null)
const genericError = 'Authentication is temporarily unavailable. Please try again.'
const safeLoginError = 'Microsoft sign-in could not be started. Please try again or contact IT.'
const sessionError = "We couldn't establish a valid PAS AI Quest session. Please sign out and try again."
const messageFor = (error: unknown) => error instanceof AuthApiError && error.status === 403 ? 'You do not have permission to use that identity.' : genericError

export function AuthProvider({ children, demoModeAvailable, api = authApi, mode = 'demo', msal = null, entraConfig = null, configurationError = null }: {
  children: ReactNode; demoModeAvailable: boolean; api?: AuthApi; mode?: FrontendAuthMode
  msal?: IPublicClientApplication | null; entraConfig?: EntraFrontendConfig | null; configurationError?: string | null
}) {
  const [status, setStatus] = useState<AuthStatus>('initializing'), [currentUser, setCurrentUser] = useState<CurrentUser | null>(null), [error, setError] = useState<string | null>(configurationError)
  const [demoProfiles, setDemoProfiles] = useState<DemoProfile[]>([]), [profilesLoading, setProfilesLoading] = useState(mode === 'demo' && demoModeAvailable), [switching, setSwitching] = useState(false)
  const account = useCallback(() => msal?.getActiveAccount() ?? msal?.getAllAccounts()[0] ?? null, [msal])
  const recoverSession = useCallback(async () => {
    if (mode !== 'entra' || !msal || !entraConfig || configurationError) return
    setStatus('session_recovery'); setError(null)
    const path = safeSameOriginReturnUrl(`${window.location.pathname}${window.location.search}${window.location.hash}`)
    try { await recoverAuthentication(() => msal.loginRedirect({ ...loginRequest(entraConfig), redirectStartPage: `${window.location.origin}${path}` })) }
    catch (recoveryError) {
      if (recoveryError instanceof AuthenticationRecoveryAlreadyAttempted) { setStatus('session_error'); setError(sessionError) }
      else { setStatus('error'); setError(safeLoginError) }
      throw recoveryError
    }
  }, [configurationError, entraConfig, mode, msal])
  const login = useCallback(async () => {
    if (mode !== 'entra' || !msal || !entraConfig || configurationError) { setStatus('error'); setError(configurationError || safeLoginError); return }
    setStatus('authenticating'); setError(null)
    try { const path = safeSameOriginReturnUrl(`${window.location.pathname}${window.location.search}${window.location.hash}`); await msal.loginRedirect({ ...loginRequest(entraConfig), redirectStartPage: `${window.location.origin}${path}` }) }
    catch { setStatus('unauthenticated'); setError(safeLoginError) }
  }, [configurationError, entraConfig, mode, msal])
  const logout = useCallback(async () => {
    setCurrentUser(null); setError(null); clearAuthenticationRecovery()
    if (mode === 'demo') { await api.clearDemoSession(); setStatus('unauthenticated'); return }
    if (!msal) { setStatus('unauthenticated'); return }
    const active = account(); setStatus('initializing')
    try { await msal.logoutRedirect({ account: active ?? undefined, postLogoutRedirectUri: entraConfig?.redirectUri }) }
    catch { setStatus('error'); setError('Sign out could not be completed. Please try again.') }
  }, [account, api, entraConfig?.redirectUri, mode, msal])
  const refreshCurrentUser = useCallback(async () => {
    if (configurationError) { setCurrentUser(null); setStatus('error'); setError(configurationError); return }
    if (mode === 'entra' && !account()) { setCurrentUser(null); setStatus('unauthenticated'); setError(null); return }
    setStatus(mode === 'entra' ? 'loading_profile' : 'initializing')
    try {
      const user = await api.getCurrentUser(); setCurrentUser(user); clearAuthenticationRecovery()
      if (user.isAuthenticated && user.roles.length) { setStatus('authenticated'); setError(null) }
      else if (mode === 'entra') { setStatus('unprovisioned'); setError(null) }
      else { setStatus('unauthenticated'); setError(null) }
    } catch (requestError) {
      setCurrentUser(null)
      if (requestError instanceof AuthApiError && requestError.status === 403) {
        if (mode === 'entra') { setStatus('access_denied'); setError(null) }
        else { setStatus('error'); setError(messageFor(requestError)) }
      }
      else if (requestError instanceof AuthApiError && requestError.status === 401) {
        if (mode === 'entra') {
          try { await recoverSession() } catch { /* recoverSession owns the safe state. */ }
          if (hasAuthenticationRecoveryMarker() && !authenticationRecoveryStartedOnThisPage()) { setStatus('session_error'); setError(sessionError) }
        } else { setStatus('unauthenticated'); setError(null) }
      }
      else { setStatus('error'); setError(messageFor(requestError)) }
      throw requestError
    }
  }, [account, api, configurationError, mode, recoverSession])
  useEffect(() => {
    if (mode === 'entra' && msal && entraConfig && !configurationError) configureAuthenticatedApi(createAccessTokenProvider(msal, entraConfig.apiScope, recoverSession), { unauthorized: recoverSession })
    else configureAuthenticatedApi(null)
    return () => configureAuthenticatedApi(null)
  }, [configurationError, entraConfig, mode, msal, recoverSession])
  useEffect(() => { void refreshCurrentUser().catch(() => undefined) }, [refreshCurrentUser])
  useEffect(() => {
    if (mode !== 'demo' || !demoModeAvailable) { setDemoProfiles([]); setProfilesLoading(false); return }
    let active = true; setProfilesLoading(true)
    api.getDemoProfiles().then((profiles) => { if (active) setDemoProfiles(profiles) }).catch((requestError) => { if (active) setError(messageFor(requestError)) }).finally(() => { if (active) setProfilesLoading(false) })
    return () => { active = false }
  }, [api, demoModeAvailable, mode])
  const switchDemoProfile = useCallback(async (profileKey: string) => {
    if (mode !== 'demo' || !demoModeAvailable) return false
    setSwitching(true); setError(null)
    try { await api.establishDemoSession(profileKey); const confirmed = await api.getCurrentUser(); setCurrentUser(confirmed); setStatus(confirmed.isAuthenticated ? 'authenticated' : 'unauthenticated'); return confirmed.isAuthenticated }
    catch (requestError) { setError(messageFor(requestError)); return false } finally { setSwitching(false) }
  }, [api, demoModeAvailable, mode])
  const clearDemoSession = useCallback(async () => { setSwitching(true); setError(null); try { await api.clearDemoSession(); await refreshCurrentUser() } catch (requestError) { setError(messageFor(requestError)) } finally { setSwitching(false) } }, [api, refreshCurrentUser])
  const value = useMemo(() => ({ status, currentUser, error, mode, demoModeAvailable: mode === 'demo' && demoModeAvailable, demoProfiles, profilesLoading, switching, refreshCurrentUser, login, logout, switchDemoProfile, clearDemoSession }), [status, currentUser, error, mode, demoModeAvailable, demoProfiles, profilesLoading, switching, refreshCurrentUser, login, logout, switchDemoProfile, clearDemoSession])
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
export function useAuth() { const context = useContext(AuthContext); if (!context) throw new Error('useAuth must be used inside AuthProvider'); return context }
