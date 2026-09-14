import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { AccountInfo, IPublicClientApplication } from '@azure/msal-browser'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { App } from '../App'
import { AuthProvider, useAuth } from './AuthContext'
import { AuthApiError, type AuthApi } from './authApi'
import { apiFetch, configureAuthenticatedApi, resetAuthenticatedApiForTests } from './apiClient'
import { readAuthRuntimeConfig, safeSameOriginReturnUrl, type EntraFrontendConfig } from './authConfig'
import { clearAuthenticationRecovery, hasAuthenticationRecoveryMarker, recoverAuthentication, resetAuthenticationRecoveryRuntimeForTests } from './authRecovery'
import { createAccessTokenProvider } from './authToken'
import { LoginPage } from './LoginPage'

const config: EntraFrontendConfig = { tenantId: 'tenant', clientId: 'spa', redirectUri: 'https://portal.example/auth', apiScope: 'api://quest/access' }
const account = { homeAccountId: 'home', localAccountId: 'local', environment: 'login.microsoftonline.com', tenantId: 'tenant', username: 'user@example.test' } as AccountInfo

function msal(overrides: Record<string, unknown> = {}) {
  return {
    getActiveAccount: vi.fn(() => account), getAllAccounts: vi.fn(() => [account]),
    acquireTokenSilent: vi.fn().mockResolvedValue({ accessToken: 'access-token' }),
    loginRedirect: vi.fn().mockResolvedValue(undefined), logoutRedirect: vi.fn().mockResolvedValue(undefined),
    ...overrides,
  } as unknown as IPublicClientApplication
}

function api(overrides: Partial<AuthApi> = {}): AuthApi {
  return { getCurrentUser: vi.fn().mockResolvedValue({ isAuthenticated: true, participantId: null, displayName: 'Morgan Person', roles: ['Quest.Manager'] }), getDemoProfiles: vi.fn(), establishDemoSession: vi.fn(), clearDemoSession: vi.fn(), ...overrides } as AuthApi
}

function Probe() {
  const auth = useAuth()
  return <><p data-testid="status">{auth.status}</p><p>{auth.currentUser?.displayName}</p><p>{auth.currentUser?.roles.join(',')}</p><button onClick={() => void auth.login()}>Login</button><button onClick={() => void auth.logout()}>Logout</button></>
}

afterEach(() => { cleanup(); resetAuthenticatedApiForTests(); clearAuthenticationRecovery(); resetAuthenticationRecoveryRuntimeForTests(); vi.restoreAllMocks(); window.history.replaceState({}, '', '/') })

describe('Entra login and authoritative profile', () => {
  it('renders the approved login design without credential fields and starts redirect sign-in', async () => {
    const onSignIn = vi.fn()
    render(<LoginPage signingIn={false} error={null} onSignIn={onSignIn} />)
    expect(screen.getByRole('heading', { name: 'Sign in to PAS AI Quest' })).toBeInTheDocument()
    expect(screen.getByText('Use your company Microsoft account to continue.')).toBeInTheDocument()
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument()
    expect(document.querySelector('input[type="password"]')).toBeNull()
    await userEvent.click(screen.getByRole('button', { name: 'Sign in with Microsoft' }))
    expect(onSignIn).toHaveBeenCalledOnce()
  })

  it('shows signing and safe error states', () => {
    const { rerender } = render(<LoginPage signingIn error={null} onSignIn={() => undefined} />)
    expect(screen.getByRole('button', { name: 'Signing in…' })).toBeDisabled()
    rerender(<LoginPage signingIn={false} error="Microsoft sign-in could not be started." onSignIn={() => undefined} />)
    expect(screen.getByRole('alert')).toHaveTextContent('Microsoft sign-in could not be started.')
  })

  it('restores a cached account, loads the backend profile, and uses backend roles', async () => {
    const authApi = api()
    render(<AuthProvider mode="entra" demoModeAvailable={false} msal={msal()} entraConfig={config} api={authApi}><Probe /></AuthProvider>)
    expect(screen.getByTestId('status')).toHaveTextContent(/initializing|loading_profile/)
    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('authenticated'))
    expect(authApi.getCurrentUser).toHaveBeenCalledOnce()
    expect(screen.getByText('Morgan Person')).toBeInTheDocument()
    expect(screen.getByText('Quest.Manager')).toBeInTheDocument()
  })

  it('clears a redirect-surviving recovery marker after a successful unprovisioned profile response', async () => {
    await recoverAuthentication(() => Promise.resolve())
    expect(hasAuthenticationRecoveryMarker()).toBe(true)
    resetAuthenticationRecoveryRuntimeForTests()
    const instance = msal()
    const authApi = api({ getCurrentUser: vi.fn().mockResolvedValue({ isAuthenticated: true, participantId: null, displayName: 'Unprovisioned', roles: [] }) })
    render(<AuthProvider mode="entra" demoModeAvailable={false} msal={instance} entraConfig={config} api={authApi}><App /></AuthProvider>)
    expect(await screen.findByRole('heading', { name: 'PAS AI Quest access required' })).toBeInTheDocument()
    expect(screen.getByText("Your Microsoft account is signed in, but you don't currently have access to PAS AI Quest.")).toBeInTheDocument()
    expect(hasAuthenticationRecoveryMarker()).toBe(false)
    expect(instance.loginRedirect).not.toHaveBeenCalled()
    expect(instance.logoutRedirect).not.toHaveBeenCalled()
  })

  it('keeps an authenticated 403 as access denied without a redirect loop', async () => {
    const instance = msal()
    const authApi = api({ getCurrentUser: vi.fn().mockRejectedValue(new AuthApiError(403)) })
    render(<AuthProvider mode="entra" demoModeAvailable={false} msal={instance} entraConfig={config} api={authApi}><App /></AuthProvider>)
    expect(await screen.findByRole('heading', { name: 'Access denied' })).toBeInTheDocument()
    expect(screen.getByText('Your account is authenticated, but you are not authorized to access PAS AI Quest.')).toBeInTheDocument()
    expect(instance.loginRedirect).not.toHaveBeenCalled()
  })

  it('renders backend-authorized manager navigation without production role switching', async () => {
    const workflow = { getReviewQueue: vi.fn().mockResolvedValue([]) }
    render(<AuthProvider mode="entra" demoModeAvailable={false} msal={msal()} entraConfig={config} api={api()}><App api={workflow as never} /></AuthProvider>)
    await screen.findByRole('heading', { name: 'Manager Dashboard' })
    expect(screen.getByRole('button', { name: 'Review Queue' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Sign out' })).toBeInTheDocument()
    expect(screen.queryByText(/view as/i)).not.toBeInTheDocument()
    expect(screen.queryByText('Demo authentication')).not.toBeInTheDocument()
  })

  it('preserves the protected same-origin path and query for redirect login', async () => {
    window.history.replaceState({}, '', '/manager/submissions/123?from=teams')
    const instance = msal({ getActiveAccount: vi.fn(() => null), getAllAccounts: vi.fn(() => []) })
    render(<AuthProvider mode="entra" demoModeAvailable={false} msal={instance} entraConfig={config} api={api()}><Probe /></AuthProvider>)
    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('unauthenticated'))
    await userEvent.click(screen.getByRole('button', { name: 'Login' }))
    expect(instance.loginRedirect).toHaveBeenCalledWith(expect.objectContaining({ redirectStartPage: `${window.location.origin}/manager/submissions/123?from=teams` }))
  })

  it('clears portal identity before MSAL redirect sign out', async () => {
    const instance = msal()
    render(<AuthProvider mode="entra" demoModeAvailable={false} msal={instance} entraConfig={config} api={api()}><Probe /></AuthProvider>)
    await screen.findByText('Morgan Person')
    await userEvent.click(screen.getByRole('button', { name: 'Logout' }))
    expect(screen.queryByText('Morgan Person')).not.toBeInTheDocument()
    expect(instance.logoutRedirect).toHaveBeenCalledOnce()
  })
})

describe('Entra configuration and central API token handling', () => {
  it.each(['Development', 'Test'])('allows explicitly enabled demo authentication in %s', (environment) => {
    const runtime = readAuthRuntimeConfig({ VITE_APP_ENVIRONMENT: environment, VITE_DEMO_AUTH_ENABLED: 'true' })
    expect(runtime.mode).toBe('demo')
    expect(runtime.demoModeAvailable).toBe(true)
  })

  it.each([
    ['Development', 'false'],
    ['Production', 'false'],
    ['Production', 'true'],
  ])('does not enable demo authentication in %s with demo flag %s', (environment, demoFlag) => {
    const runtime = readAuthRuntimeConfig({ VITE_APP_ENVIRONMENT: environment, VITE_DEMO_AUTH_ENABLED: demoFlag })
    expect(runtime.mode).toBe('entra')
    expect(runtime.demoModeAvailable).toBe(false)
  })

  it('fails closed with missing Production Entra configuration, even when the demo flag is true', () => {
    const runtime = readAuthRuntimeConfig({ VITE_APP_ENVIRONMENT: 'Production', VITE_DEMO_AUTH_ENABLED: 'true' })
    expect(runtime.mode).toBe('entra')
    expect(runtime.error).toMatch(/not configured/)
  })

  it('allows the explicit Development/Demo deployment in a production-optimized Vite bundle', () => {
    const runtime = readAuthRuntimeConfig({ PROD: true, VITE_APP_ENVIRONMENT: 'Development', VITE_DEMO_AUTH_ENABLED: 'true' })
    expect(runtime.mode).toBe('demo')
  })

  it('fails closed for an absent or unknown deployment environment', () => {
    for (const environment of [undefined, 'Unknown']) {
      const runtime = readAuthRuntimeConfig({ VITE_APP_ENVIRONMENT: environment, VITE_DEMO_AUTH_ENABLED: 'true' })
      expect(runtime.mode).toBe('entra')
      expect(runtime.error).toMatch(/not configured/)
    }
  })

  it('rejects external return URLs while retaining same-origin query strings', () => {
    expect(safeSameOriginReturnUrl('/xp-activity?cycleId=abc', 'https://portal.example')).toBe('/xp-activity?cycleId=abc')
    expect(safeSameOriginReturnUrl('https://evil.example/steal', 'https://portal.example')).toBe('/')
  })

  it('acquires silently and adds the bearer token centrally', async () => {
    const instance = msal()
    configureAuthenticatedApi(createAccessTokenProvider(instance, config.apiScope, () => Promise.resolve()))
    const fetchMock = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(null, { status: 204 }))
    await apiFetch('/api/example')
    expect(instance.acquireTokenSilent).toHaveBeenCalledWith({ account, scopes: [config.apiScope] })
    expect(new Headers(fetchMock.mock.calls[0][1]?.headers).get('Authorization')).toBe('Bearer access-token')
  })

  it('routes 401 to authentication recovery and leaves feature 403 global-state neutral', async () => {
    const unauthorized = vi.fn()
    configureAuthenticatedApi(null, { unauthorized })
    vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(new Response(null, { status: 401 })).mockResolvedValueOnce(new Response(null, { status: 403 }))
    await apiFetch('/api/expired'); await apiFetch('/api/forbidden')
    expect(unauthorized).toHaveBeenCalledOnce()
    expect(unauthorized).toHaveBeenCalledTimes(1)
  })

  it('starts interactive redirect when silent token acquisition requires interaction', async () => {
    const interactionError = Object.assign(new Error('interaction required'), { name: 'InteractionRequiredAuthError' })
    const instance = msal({ acquireTokenSilent: vi.fn().mockRejectedValue(interactionError) })
    window.history.replaceState({}, '', '/leaderboard?cycleId=cycle')
    const recover = () => recoverAuthentication(() => instance.loginRedirect({ scopes: [config.apiScope] }))
    await expect(createAccessTokenProvider(instance, config.apiScope, recover)()).rejects.toThrow('Interactive authentication started.')
    expect(instance.loginRedirect).toHaveBeenCalledWith(expect.objectContaining({ scopes: [config.apiScope] }))
  })
})

describe('guarded authentication recovery', () => {
  it('starts exactly one redirect for concurrent API 401 responses', async () => {
    const instance = msal()
    render(<AuthProvider mode="entra" demoModeAvailable={false} msal={instance} entraConfig={config} api={api()}><Probe /></AuthProvider>)
    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('authenticated'))
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(null, { status: 401 }))
    const [first, second] = await Promise.all([apiFetch('/api/first'), apiFetch('/api/second')])
    expect(first.status).toBe(401); expect(second.status).toBe(401)
    expect(instance.loginRedirect).toHaveBeenCalledTimes(1)
    expect(hasAuthenticationRecoveryMarker()).toBe(true)
  })

  it('clears the redirect-surviving marker only after authoritative profile success', async () => {
    await recoverAuthentication(() => Promise.resolve())
    expect(hasAuthenticationRecoveryMarker()).toBe(true)
    resetAuthenticationRecoveryRuntimeForTests()
    render(<AuthProvider mode="entra" demoModeAvailable={false} msal={msal()} entraConfig={config} api={api()}><Probe /></AuthProvider>)
    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('authenticated'))
    expect(hasAuthenticationRecoveryMarker()).toBe(false)
  })

  it('shows a safe session error without a second redirect after persistent profile 401', async () => {
    await recoverAuthentication(() => Promise.resolve())
    resetAuthenticationRecoveryRuntimeForTests()
    const instance = msal()
    render(<AuthProvider mode="entra" demoModeAvailable={false} msal={instance} entraConfig={config} api={api({ getCurrentUser: vi.fn().mockRejectedValue(new AuthApiError(401)) })}><App /></AuthProvider>)
    expect(await screen.findByRole('heading', { name: "We couldn't establish your session" })).toBeInTheDocument()
    expect(screen.getByRole('alert')).toHaveTextContent("We couldn't establish a valid PAS AI Quest session")
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Sign out' })).toBeInTheDocument()
    expect(instance.loginRedirect).not.toHaveBeenCalled()
  })

  it('single-flights concurrent interaction-required token failures through the same guard', async () => {
    const interactionError = Object.assign(new Error('interaction required'), { name: 'InteractionRequiredAuthError' })
    const instance = msal({ acquireTokenSilent: vi.fn().mockRejectedValue(interactionError) })
    const recover = () => recoverAuthentication(() => instance.loginRedirect({ scopes: [config.apiScope] }))
    const provider = createAccessTokenProvider(instance, config.apiScope, recover)
    const outcomes = await Promise.allSettled([provider(), provider(), provider()])
    expect(outcomes.every((outcome) => outcome.status === 'rejected')).toBe(true)
    expect(instance.loginRedirect).toHaveBeenCalledTimes(1)
  })

  it('propagates feature 403 without clearing the authenticated profile or redirecting', async () => {
    const instance = msal()
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(JSON.stringify({ detail: 'You do not have permission to perform this action.' }), { status: 403, headers: { 'Content-Type': 'application/json' } }))
    render(<AuthProvider mode="entra" demoModeAvailable={false} msal={instance} entraConfig={config} api={api()}><App /></AuthProvider>)
    await screen.findByRole('heading', { name: 'Manager Dashboard' })
    await userEvent.click(screen.getByRole('button', { name: 'Review Queue' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('You do not have permission to perform this action.')
    expect(screen.getByText('Morgan Person')).toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Access denied' })).not.toBeInTheDocument()
    expect(instance.loginRedirect).not.toHaveBeenCalled()
    expect(instance.logoutRedirect).not.toHaveBeenCalled()
  })
})
