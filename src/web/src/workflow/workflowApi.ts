import type { EligibleChallenge, ResubmitRequest, ReviewRequest, SubmissionView, SubmitRequest } from './types'

export class WorkflowApiError extends Error {
  constructor(public readonly status: number, public readonly detail?: string) {
    super(detail || `Request failed (${status})`)
    this.name = 'WorkflowApiError'
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response
  try {
    response = await fetch(path, {
      credentials: 'same-origin',
      ...init,
      headers: init?.body ? { 'Content-Type': 'application/json', ...init.headers } : init?.headers,
    })
  } catch {
    throw new WorkflowApiError(0, 'Network request failed.')
  }
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as { detail?: string; title?: string } | null
    throw new WorkflowApiError(response.status, problem?.detail || problem?.title)
  }
  if (response.status === 204) return undefined as T
  return response.json() as Promise<T>
}

export const workflowApi = {
  getEligibleChallenges: () => request<EligibleChallenge[]>('/api/challenges/eligible'),
  getMySubmissions: () => request<SubmissionView[]>('/api/submissions/mine'),
  createSubmission: (body: SubmitRequest) => request<SubmissionView>('/api/submissions', { method: 'POST', body: JSON.stringify(body) }),
  resubmit: (id: string, body: ResubmitRequest) => request<SubmissionView>(`/api/submissions/${id}/resubmission`, { method: 'PUT', body: JSON.stringify(body) }),
  getReviewQueue: () => request<SubmissionView[]>('/api/submissions/review-queue'),
  review: (id: string, body: ReviewRequest) => request<SubmissionView>(`/api/submissions/${id}/review`, { method: 'POST', body: JSON.stringify(body) }),
}

export type WorkflowApi = typeof workflowApi

export function workflowErrorMessage(error: unknown) {
  if (!(error instanceof WorkflowApiError)) return 'Something went wrong. Please try again.'
  if (error.status === 401) return 'Your session has expired. Refresh or sign in again.'
  if (error.status === 403) return 'You do not have permission to perform this action.'
  if (error.status === 409) return error.detail || 'This submission changed elsewhere. Refresh and try again.'
  if (error.status === 422 || error.status === 400) return error.detail || 'Check the submission details and try again.'
  if (error.status === 0) return 'Network connection failed. Check your connection and try again.'
  return error.detail || 'The request could not be completed. Please try again.'
}
