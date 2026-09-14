type TokenProvider = () => Promise<string | null>
type AuthResponseHandlers = { unauthorized?: () => void | Promise<void> }
let tokenProvider: TokenProvider | null = null
let handlers: AuthResponseHandlers = {}

export function configureAuthenticatedApi(provider: TokenProvider | null, nextHandlers: AuthResponseHandlers = {}) { tokenProvider = provider; handlers = nextHandlers }

export async function apiFetch(input: RequestInfo | URL, init?: RequestInit) {
  const headers = new Headers(init?.headers)
  const token = tokenProvider ? await tokenProvider() : null
  if (token) headers.set('Authorization', `Bearer ${token}`)
  const response = await fetch(input, { ...init, credentials: init?.credentials ?? 'same-origin', headers })
  if (response.status === 401) {
    try { await handlers.unauthorized?.() }
    catch { /* The auth boundary owns recovery state; preserve the original API response. */ }
  }
  return response
}

export function resetAuthenticatedApiForTests() { tokenProvider = null; handlers = {} }
