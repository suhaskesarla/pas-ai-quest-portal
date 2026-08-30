import type { CreateRaidSessionRequest, RaidCycleList, RaidParticipantList, RaidPassType, RaidSession, RaidSessionList, RaidXpRequest, UpdateRaidSessionRequest } from './types'

export class RaidAdminApiError extends Error { constructor(public status: number, public code?: string, detail?: string) { super(detail || `Request failed (${status})`) } }
async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response
  try { response = await fetch(path, { credentials: 'same-origin', ...init, headers: init?.body ? { 'Content-Type': 'application/json', ...init.headers } : init?.headers }) }
  catch { throw new RaidAdminApiError(0, 'NetworkFailure', 'Network request failed.') }
  if (!response.ok) { const problem = await response.json().catch(() => null) as { code?: string; title?: string; detail?: string } | null; throw new RaidAdminApiError(response.status, problem?.code || problem?.title, problem?.detail || problem?.title) }
  return response.json() as Promise<T>
}
export const raidAdminApi = {
  getCycles: () => request<RaidCycleList>('/api/manager/raids/cycles'),
  getSessions: (cycleId: string) => request<RaidSessionList>(`/api/manager/raids?cycleId=${encodeURIComponent(cycleId)}`),
  getSession: (id: string) => request<RaidSession>(`/api/manager/raids/${id}`),
  getParticipants: (id: string) => request<RaidParticipantList>(`/api/manager/raids/${id}/participants`),
  createSession: (body: CreateRaidSessionRequest) => request<RaidSession>('/api/manager/raids', { method: 'POST', body: JSON.stringify(body) }),
  updateSession: (id: string, body: UpdateRaidSessionRequest) => request<RaidSession>(`/api/manager/raids/${id}`, { method: 'PUT', body: JSON.stringify(body) }),
  updateEntitlement: (cycleId: string, participantId: string, passType: RaidPassType, assignedCount: number, rowVersion: string | null) => request(`/api/manager/raids/cycles/${cycleId}/participants/${participantId}/entitlements/${passType}`, { method: 'PUT', body: JSON.stringify({ assignedCount, rowVersion }) }),
  recordParticipation: (sessionId: string, participantId: string, passType: RaidPassType) => request(`/api/manager/raids/${sessionId}/participations`, { method: 'POST', body: JSON.stringify({ participantId, passType }) }),
  awardXp: (sessionId: string, body: RaidXpRequest) => request(`/api/manager/raids/${sessionId}/xp-awards`, { method: 'POST', body: JSON.stringify(body) }),
}
export type RaidAdminApi = typeof raidAdminApi

const messages: Record<string, string> = {
  RaidSessionVersionConflict: 'This Raid Session changed. Reload the authoritative session before retrying.', RaidSessionImmutable: 'This Raid Session can no longer be edited because participation or Raid XP has already been recorded.',
  RaidEntitlementVersionConflict: 'This pass assignment changed. Refresh the participant balances before retrying.', RaidEntitlementBelowUsed: 'Assigned passes cannot be lower than the authoritative Used count.',
  RaidParticipationConflict: 'Participation conflicts with an existing record for this Raid Session.', RaidPassExhausted: 'No remaining pass is available.', RaidEntitlementNotFound: 'A matching pass entitlement is required before participation can be recorded.',
  RaidXpRequestConflict: 'This request identifier conflicts with another Raid XP attempt. Review the values or deliberately start a new award.', RaidParticipantIneligible: 'This participant is not active in the selected cycle.', RaidCycleUnavailable: 'This cycle is Finalised and Raid Administration is read-only.',
  InvalidRaidXpAmount: 'Enter a positive whole-number XP amount.', RaidXpReasonRequired: 'Enter a reason for the Raid XP award.', RaidXpReasonTooLong: 'Reason must be 2,000 characters or fewer.', RaidSessionValidationFailed: 'Enter a session name of 200 characters or fewer and an occurrence time.',
}
export const raidError = (error: unknown) => error instanceof RaidAdminApiError ? error.status === 401 ? 'Your session has expired.' : error.status === 403 ? 'Manager authorization is required.' : error.code && messages[error.code] ? messages[error.code] : error.message : 'Something went wrong. Please try again.'
