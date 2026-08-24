export type SubmissionStatus = 'Submitted' | 'UnderReview' | 'NeedsEvidence' | 'Resubmitted' | 'Approved' | 'Rejected'
export type EvidenceInputKind = 'Text' | 'Link'

export type PersonSummary = { participantId: string; displayName: string }
export type EvidenceInput = { kind: EvidenceInputKind; label: string; required: boolean; instructions?: string }
export type ParticipationOption = {
  participationId: string
  members: PersonSummary[]
  claimantIsMember: boolean
  requiresCompleteParticipation: boolean
  allowsBeneficiarySubset: boolean
}
export type TaskSummary = {
  id: string
  name: string
  xp: number
  scoringMode: 'Individual' | 'WholeTeam' | 'ClaimantSelectsBeneficiaries' | 'AttendanceBased'
  evidenceInputs: EvidenceInput[]
  participations: ParticipationOption[]
}
export type EligibleChallenge = {
  id: string
  name: string
  description: string
  category: string
  status: 'Published' | 'Open' | 'Closed' | 'Archived'
  openAt: string
  dueAt: string
  closeAt: string
  effectiveDeadline: string
  isEligible: boolean
  ineligibilityReason?: string
  tasks: TaskSummary[]
}
export type EvidenceItem = { id?: string; kind: EvidenceInputKind; label: string; value: string }
export type SubmissionEvent = { eventType: SubmissionStatus; comment?: string; actorDisplayName: string; occurredAt: string }
export type SubmissionView = {
  id: string
  version: string
  status: SubmissionStatus
  claimant: PersonSummary
  beneficiaries: PersonSummary[]
  challengeId: string
  challengeName: string
  taskId: string
  taskName: string
  taskXp: number
  evidence: EvidenceItem[]
  participantComment?: string
  managerComment?: string
  submittedAt: string
  lastUpdatedAt: string
  history: SubmissionEvent[]
}

export type SubmitRequest = { challengeId: string; taskId: string; challengeParticipationId?: string; beneficiaryIds: string[]; evidence: Omit<EvidenceItem, 'id'>[]; comment?: string }
export type ResubmitRequest = { version: string; evidence: Omit<EvidenceItem, 'id'>[]; comment?: string }
export type ReviewAction = 'NeedsEvidence' | 'Approve' | 'Reject'
export type ReviewRequest = { version: string; action: ReviewAction; comment?: string }
