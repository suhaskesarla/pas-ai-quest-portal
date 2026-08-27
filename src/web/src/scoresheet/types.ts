import type { CycleStatus, ParticipantStatus, XpActivityItem } from '../reporting/types'

export type ManagerReportingCycle = {
  id: string
  code: string
  name: string
  status: CycleStatus
  startsAt: string
  endsAt: string
}

export type ManagerReportingCycles = { defaultCycleId: string | null; cycles: ManagerReportingCycle[] }
export type SourceXpBreakdown = { taskApprovalXp: number; manualAwardXp: number; raidXp: number }
export type EntryTypeBreakdown = { grantXp: number; reversalXp: number; correctionXp: number; netAdjustmentXp: number }
export type ScoresheetParticipant = {
  participantId: string
  displayName: string
  participantStatus: ParticipantStatus
  bySource: SourceXpBreakdown
  byEntryType: EntryTypeBreakdown
  totalXp: number
}
export type ScoresheetSummary = { cycle: ManagerReportingCycle; rows: ScoresheetParticipant[] }
export type ScoresheetDetail = {
  cycle: ManagerReportingCycle
  participant: { participantId: string; displayName: string; participantStatus: ParticipantStatus }
  bySource: SourceXpBreakdown
  byEntryType: EntryTypeBreakdown
  totalXp: number
  items: XpActivityItem[]
  nextCursor: string | null
}
