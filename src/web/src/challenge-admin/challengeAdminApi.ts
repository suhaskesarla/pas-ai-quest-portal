import type { ChallengeAggregate, ChallengeOptions, ChallengeStatus, CreateChallengeRequest, UpdateChallengeRequest } from './types'

export class ChallengeAdminApiError extends Error {
  constructor(public status: number, public code?: string, detail?: string, public fieldErrors?: Record<string, string[]>) { super(detail || `Request failed (${status})`) }
}
async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response
  try { response = await fetch(path, { credentials: 'same-origin', ...init, headers: init?.body ? { 'Content-Type': 'application/json', ...init.headers } : init?.headers }) }
  catch { throw new ChallengeAdminApiError(0, 'NetworkFailure', 'Network request failed.') }
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as { code?: string; title?: string; detail?: string; errors?: Record<string, string[]> } | null
    throw new ChallengeAdminApiError(response.status, problem?.code || problem?.title, problem?.detail || problem?.title, problem?.errors)
  }
  return response.json() as Promise<T>
}
export const challengeAdminApi = {
  getOptions: () => request<ChallengeOptions>('/api/manager/challenge-options'),
  getChallenges: (cycleId?: string, status?: ChallengeStatus | '') => request<ChallengeAggregate[]>(`/api/manager/challenges?${new URLSearchParams({ ...(cycleId ? { cycleId } : {}), ...(status ? { status } : {}) })}`),
  getChallenge: (id: string) => request<ChallengeAggregate>(`/api/manager/challenges/${id}`),
  createChallenge: (body: CreateChallengeRequest) => request<ChallengeAggregate>('/api/manager/challenges', { method: 'POST', body: JSON.stringify(body) }),
  updateChallenge: (id: string, body: UpdateChallengeRequest) => request<ChallengeAggregate>(`/api/manager/challenges/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  publishChallenge: (id: string, version: string) => request<ChallengeAggregate>(`/api/manager/challenges/${id}/publish`, { method: 'POST', body: JSON.stringify({ version }) }),
}
export type ChallengeAdminApi = typeof challengeAdminApi
export const adminError = (error: unknown) => error instanceof ChallengeAdminApiError
  ? error.status === 401 ? 'Your session has expired.' : error.status === 403 ? 'Manager authorization is required.' : error.message
  : 'Something went wrong. Please try again.'
