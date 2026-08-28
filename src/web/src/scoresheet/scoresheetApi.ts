import type { CorrectionRequest, CorrectionResponse, ManagerReportingCycles, ManualAwardCommand, ManualAwardOptions, ManualAwardResponse, ScoresheetDetail, ScoresheetSummary } from './types'

export class ScoresheetApiError extends Error {
  constructor(public readonly status: number, public readonly detail?: string, public readonly code?: string) {
    super(detail || `Request failed (${status})`)
    this.name = 'ScoresheetApiError'
  }
}

async function request<T>(path: string, command?: { method: 'POST'; payload: unknown }): Promise<T> {
  let response: Response
  try { response = await fetch(path, { credentials: 'same-origin', method: command?.method, headers: command ? { 'Content-Type': 'application/json' } : undefined, body: command ? JSON.stringify(command.payload) : undefined }) }
  catch { throw new ScoresheetApiError(0, 'Network request failed.') }
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as { detail?: string; title?: string; code?: string } | null
    throw new ScoresheetApiError(response.status, problem?.detail || problem?.title, problem?.code || problem?.title)
  }
  return response.json() as Promise<T>
}

export const scoresheetApi = {
  getReportingCycles: () => request<ManagerReportingCycles>('/api/manager/reporting-cycles'),
  getScoresheet: (cycleId: string) => request<ScoresheetSummary>(`/api/manager/scoresheet?cycleId=${encodeURIComponent(cycleId)}`),
  getParticipant: (participantId: string, cycleId: string, cursor?: string | null) => request<ScoresheetDetail>(`/api/manager/scoresheet/${encodeURIComponent(participantId)}?cycleId=${encodeURIComponent(cycleId)}&limit=50${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''}`),
  correctXp: (entryId: string, payload: CorrectionRequest) => request<CorrectionResponse>(`/api/manager/xp/${encodeURIComponent(entryId)}/corrections`, { method: 'POST', payload }),
  getManualAwardOptions: (cycleId: string) => request<ManualAwardOptions>(`/api/manager/manual-awards/options?cycleId=${encodeURIComponent(cycleId)}`),
  createManualAward: (payload: ManualAwardCommand) => request<ManualAwardResponse>('/api/manager/manual-awards', { method: 'POST', payload }),
}
export type ScoresheetApi = typeof scoresheetApi

export function scoresheetErrorMessage(error: unknown) {
  if (!(error instanceof ScoresheetApiError)) return 'The scoresheet could not be loaded. Please try again.'
  if (error.status === 401) return 'Your session has expired. Refresh or sign in again.'
  if (error.status === 403) return 'Your manager role does not permit access to this scoresheet.'
  if (error.status === 404) return error.detail || 'That participant or reporting cycle was not found.'
  if (error.status === 503) return error.detail || 'Scoresheet data is temporarily unavailable.'
  if (error.status === 0) return 'Network connection failed. Check your connection and try again.'
  return error.detail || 'The scoresheet could not be loaded.'
}
