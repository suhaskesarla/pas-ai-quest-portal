export type CycleStatus = 'Active' | 'Closing' | 'Finalised'
export type ParticipantStatus = 'Active' | 'Withdrawn' | 'Inactive'
export type PassType = 'Physical' | 'Remote'
export type XpEntryType = 'Grant' | 'Reversal' | 'Correction'
export type XpSourceType = 'TaskApproval' | 'ManualAward' | 'Raid'
export type ChallengeStatus = 'Draft' | 'Published' | 'Open' | 'Closed' | 'Archived'

export type ReportingCycle = {
  id: string
  code: string
  name: string
  status: CycleStatus
  startsAt: string
  endsAt: string
  participantStatus: ParticipantStatus
}

export type ReportingCycles = { defaultCycleId: string | null; cycles: ReportingCycle[] }
export type ParticipantSummary = { participantId: string; displayName: string }
export type RaidPassBalance = { passType: PassType; assigned: number; used: number; remaining: number }
export type XpSource = {
  label: string
  challengeId: string | null
  challengeName: string | null
  taskId: string | null
  taskName: string | null
  awardCategoryId: string | null
  awardCategoryName: string | null
  raidSessionId: string | null
  raidSessionName: string | null
}
export type XpActivityItem = {
  id: string
  amount: number
  entryType: XpEntryType
  sourceType: XpSourceType
  reason: string
  awardedAt: string
  reversesEntryId: string | null
  source: XpSource
}
export type XpActivityPage = { items: XpActivityItem[]; nextCursor: string | null }
export type Dashboard = {
  cycle: ReportingCycle
  participant: ParticipantSummary
  totalXp: number
  individualRank: number | null
  eligibleChallengeCount: number
  submissionStatusCounts: Record<string, number>
  raidPassBalance: RaidPassBalance[]
  recentActivity: XpActivityItem[]
}
export type TeamMember = { participantId: string; displayName: string; isCurrentParticipant: boolean; joinedAt: string }
export type CycleTeam = { cycleTeamId: string; name: string; members: TeamMember[] }
export type ChallengeGroup = { participationId: string; challengeId: string; challengeName: string; challengeStatus: ChallengeStatus; members: TeamMember[] }
export type ParticipantTeam = { team: CycleTeam | null; challengeGroups: ChallengeGroup[] }
export type LeaderboardEntry = { rank: number; participantId: string; displayName: string; totalXp: number; isCurrentParticipant: boolean }
