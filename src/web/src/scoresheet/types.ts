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
export type ScoresheetLedgerItem = XpActivityItem & { correction: { currentEffectiveAmount: number } | null }
export type ScoresheetDetail = {
  cycle: ManagerReportingCycle
  participant: { participantId: string; displayName: string; participantStatus: ParticipantStatus }
  bySource: SourceXpBreakdown
  byEntryType: EntryTypeBreakdown
  totalXp: number
  items: ScoresheetLedgerItem[]
  nextCursor: string | null
}
export type CorrectionRequest = { newAmount: number; reason: string }
export type CorrectionResponse = { id: string; originalEntryId: string; participantId: string; cycleId: string; amount: number; entryType: 'Reversal' | 'Correction'; reason: string; awardedByParticipantId: string; awardedAt: string }
export type ManualAwardOptions = {
  cycle: { id: string; code: string; name: string; status: CycleStatus }
  participants: Array<{ participantId: string; displayName: string; participantStatus: ParticipantStatus }>
  categories: Array<{ awardCategoryId: string; code: string; name: string }>
}
export type ManualAwardCommand = { requestId: string; cycleId: string; participantId: string; awardCategoryId: string; amount: number; reason: string }
export type ManualAwardResponse = { id: string; requestId: string; participantId: string; cycleId: string; amount: number; entryType: 'Grant'; sourceType: 'ManualAward'; awardCategory: ManualAwardOptions['categories'][number]; reason: string; awardedByParticipantId: string; awardedAt: string }
