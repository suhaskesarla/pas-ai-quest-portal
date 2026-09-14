import { BrowserCacheLocation, LogLevel, type Configuration, type RedirectRequest } from '@azure/msal-browser'

export type FrontendAuthMode = 'demo' | 'entra'
export type EntraFrontendConfig = { tenantId: string; clientId: string; redirectUri: string; apiScope: string }
type FrontendEnvironment = Record<string, string | boolean | undefined>

const required = (value: unknown) => typeof value === 'string' ? value.trim() : ''

export function readAuthRuntimeConfig(env: FrontendEnvironment) {
  const requestedDemo = env.VITE_DEMO_AUTH_ENABLED === 'true'
  const demoModeAvailable = (env.VITE_APP_ENVIRONMENT === 'Development' || env.VITE_APP_ENVIRONMENT === 'Test') && requestedDemo
  if (demoModeAvailable) return { mode: 'demo' as const, demoModeAvailable, entra: null, error: null }
  const entra: EntraFrontendConfig = {
    tenantId: required(env.VITE_ENTRA_TENANT_ID), clientId: required(env.VITE_ENTRA_CLIENT_ID),
    redirectUri: required(env.VITE_ENTRA_REDIRECT_URI), apiScope: required(env.VITE_ENTRA_API_SCOPE),
  }
  const error = Object.values(entra).some((value) => !value) ? 'Microsoft sign-in is not configured for this environment. Contact IT.' : null
  return { mode: 'entra' as const, demoModeAvailable: false, entra, error }
}

export function msalConfiguration(config: EntraFrontendConfig): Configuration {
  return {
    auth: { clientId: config.clientId, authority: `https://login.microsoftonline.com/${config.tenantId}`, redirectUri: config.redirectUri, postLogoutRedirectUri: config.redirectUri, navigateToLoginRequestUrl: true },
    cache: { cacheLocation: BrowserCacheLocation.SessionStorage, storeAuthStateInCookie: false },
    system: { loggerOptions: { logLevel: LogLevel.Warning, piiLoggingEnabled: false, loggerCallback: () => undefined } },
  }
}

export const loginRequest = (config: EntraFrontendConfig): RedirectRequest => ({ scopes: [config.apiScope], prompt: 'select_account' })

export function safeSameOriginReturnUrl(candidate: string, origin = window.location.origin): string {
  try { const parsed = new URL(candidate, origin); return parsed.origin === origin && parsed.pathname.startsWith('/') && !parsed.pathname.startsWith('//') ? `${parsed.pathname}${parsed.search}${parsed.hash}` : '/' }
  catch { return '/' }
}
