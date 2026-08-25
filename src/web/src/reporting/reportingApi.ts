import type { Dashboard, LeaderboardEntry, ParticipantTeam, ReportingCycles, XpActivityPage } from './types'

export class ReportingApiError extends Error {
  constructor(public readonly status: number, public readonly detail?: string) {
    super(detail || `Request failed (${status})`)
    this.name = 'ReportingApiError'
  }
}

async function request<T>(path: string): Promise<T> {
  let response: Response
  try { response = await fetch(path, { credentials: 'same-origin' }) }
  catch { throw new ReportingApiError(0, 'Network request failed.') }
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as { detail?: string; title?: string } | null
    throw new ReportingApiError(response.status, problem?.detail || problem?.title)
  }
  return response.json() as Promise<T>
}

const cycleQuery = (cycleId: string) => `cycleId=${encodeURIComponent(cycleId)}`

export const reportingApi = {
  getReportingCycles: () => request<ReportingCycles>('/api/participant/reporting-cycles'),
  getDashboard: (cycleId: string) => request<Dashboard>(`/api/participant/dashboard?${cycleQuery(cycleId)}`),
  getTeam: (cycleId: string) => request<ParticipantTeam>(`/api/participant/team?${cycleQuery(cycleId)}`),
  getIndividualLeaderboard: (cycleId: string) => request<LeaderboardEntry[]>(`/api/leaderboards/individual?${cycleQuery(cycleId)}`),
  getXpActivity: (cycleId: string, cursor?: string | null) => request<XpActivityPage>(`/api/participant/xp-activity?${cycleQuery(cycleId)}&limit=25${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''}`),
}

export type ReportingApi = typeof reportingApi

export function reportingErrorMessage(error: unknown) {
  if (!(error instanceof ReportingApiError)) return 'Something went wrong. Please try again.'
  if (error.status === 401) return 'Your session has expired. Refresh or sign in again.'
  if (error.status === 403) return 'Your participant role does not permit access to this reporting view.'
  if (error.status === 404) return error.detail || 'That reporting cycle is not available to this participant.'
  if (error.status === 503) return error.detail || 'Reporting data is temporarily unavailable.'
  if (error.status === 0) return 'Network connection failed. Check your connection and try again.'
  return error.detail || 'The reporting view could not be loaded.'
}
