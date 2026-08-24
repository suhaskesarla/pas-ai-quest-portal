import type { CurrentUser, DemoProfile } from './types'

export class AuthApiError extends Error {
  constructor(public readonly status: number) {
    super(`Authentication request failed (${status})`)
    this.name = 'AuthApiError'
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    credentials: 'same-origin',
    ...init,
    headers: init?.body ? { 'Content-Type': 'application/json', ...init.headers } : init?.headers,
  })
  if (!response.ok) throw new AuthApiError(response.status)
  if (response.status === 204) return undefined as T
  return response.json() as Promise<T>
}

export const authApi = {
  getCurrentUser: () => request<CurrentUser>('/api/auth/me'),
  getDemoProfiles: () => request<DemoProfile[]>('/api/auth/demo/profiles'),
  establishDemoSession: (profileKey: string) => request<void>('/api/auth/demo/session', {
    method: 'POST',
    body: JSON.stringify({ profileKey }),
  }),
  clearDemoSession: () => request<void>('/api/auth/demo/session', { method: 'DELETE' }),
}

export type AuthApi = typeof authApi
