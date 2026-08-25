import type { EligibleChallenge, EvidenceUpload, ResubmitRequest, ReviewRequest, SubmissionView, SubmitRequest } from './types'

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
      headers: init?.body && !(init.body instanceof FormData) ? { 'Content-Type': 'application/json', ...init.headers } : init?.headers,
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
  createSubmission: (body: SubmitRequest, uploads: EvidenceUpload[] = []) => request<SubmissionView>('/api/submissions', { method: 'POST', body: requestBody(body, uploads) }),
  resubmit: (id: string, body: ResubmitRequest, uploads: EvidenceUpload[] = []) => request<SubmissionView>(`/api/submissions/${id}/resubmission`, { method: 'PUT', body: requestBody(body, uploads) }),
  getReviewQueue: () => request<SubmissionView[]>('/api/submissions/review-queue'),
  review: (id: string, body: ReviewRequest) => request<SubmissionView>(`/api/submissions/${id}/review`, { method: 'POST', body: JSON.stringify(body) }),
}

export type WorkflowApi = typeof workflowApi

export function requestBody(payload: SubmitRequest | ResubmitRequest, uploads: EvidenceUpload[]): BodyInit {
  if (!uploads.length) return JSON.stringify(payload)
  const body = new FormData()
  body.append('payload', new Blob([JSON.stringify(payload)], { type: 'application/json' }))
  uploads.forEach(({ fileKey, file }) => body.append(fileKey, file, file.name))
  return body
}

export function workflowErrorMessage(error: unknown) {
  if (!(error instanceof WorkflowApiError)) return 'Something went wrong. Please try again.'
  if (error.status === 401) return 'Your session has expired. Refresh or sign in again.'
  if (error.status === 403) return 'You do not have permission to perform this action.'
  if (error.status === 409) return error.detail || 'This submission changed elsewhere. Refresh and try again.'
  if (error.status === 413) return error.detail || 'The selected attachment data is too large. Check the file limits and try again.'
  if (error.status === 415) return error.detail || 'One or more attachment types are not supported.'
  if (error.status === 422 || error.status === 400) return error.detail || 'Check the submission details and try again.'
  if (error.status === 503) return error.detail || 'Attachment scanning or storage is temporarily unavailable. Your submission was not saved; please try again.'
  if (error.status === 0) return 'Network connection failed. Check your connection and try again.'
  return error.detail || 'The request could not be completed. Please try again.'
}
