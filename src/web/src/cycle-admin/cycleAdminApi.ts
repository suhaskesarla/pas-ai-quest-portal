import type { CreateCycleRequest, CycleDetail, CycleList, ParticipantOptions, ParticipantStatus, UpdateCycleRequest } from './types'

export class CycleAdminApiError extends Error {
  constructor(public status: number, public code?: string, detail?: string) { super(detail || `Request failed (${status})`) }
}
async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response
  try { response = await fetch(path, { credentials: 'same-origin', ...init, headers: init?.body ? { 'Content-Type': 'application/json', ...init.headers } : init?.headers }) }
  catch { throw new CycleAdminApiError(0, 'NetworkFailure', 'Network request failed.') }
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as { code?: string; title?: string; detail?: string } | null
    throw new CycleAdminApiError(response.status, problem?.code || problem?.title, problem?.detail || problem?.title)
  }
  return response.json() as Promise<T>
}
export const cycleAdminApi = {
  getCycles: () => request<CycleList>('/api/manager/cycles'),
  getCycle: (id: string) => request<CycleDetail>(`/api/manager/cycles/${id}`),
  getParticipantOptions: (id: string) => request<ParticipantOptions>(`/api/manager/cycles/${id}/participant-options`),
  createCycle: (body: CreateCycleRequest) => request<CycleDetail>('/api/manager/cycles', { method: 'POST', body: JSON.stringify(body) }),
  updateCycle: (id: string, body: UpdateCycleRequest) => request<CycleDetail>(`/api/manager/cycles/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  transition: (id: string, action: 'start-closing' | 'finalise', version: string, reason: string) => request<CycleDetail>(`/api/manager/cycles/${id}/${action}`, { method: 'POST', body: JSON.stringify({ version, reason }) }),
  enroll: (id: string, participantId: string, reason: string) => request(`/api/manager/cycles/${id}/participants`, { method: 'POST', body: JSON.stringify({ participantId, reason }) }),
  changeStatus: (id: string, participantId: string, version: string, status: ParticipantStatus, reason: string) => request(`/api/manager/cycles/${id}/participants/${participantId}/status`, { method: 'POST', body: JSON.stringify({ version, status, reason }) }),
}
export type CycleAdminApi = typeof cycleAdminApi
export const cycleError = (error: unknown) => error instanceof CycleAdminApiError ? error.status === 401 ? 'Your session has expired.' : error.status === 403 ? 'Manager authorization is required.' : error.message : 'Something went wrong. Please try again.'
