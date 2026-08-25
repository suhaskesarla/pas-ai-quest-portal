export type CycleStatus = 'Active' | 'Closing' | 'Finalised'
export type ChallengeStatus = 'Draft' | 'Published' | 'Open' | 'Closed' | 'Archived'
export type ScoringMode = 'Individual' | 'WholeTeam' | 'ClaimantSelectsBeneficiaries'
export type EvidenceRequirement = 'None' | 'Text' | 'Link' | 'Attachment' | 'Multiple'
export type FormationMode = 'SelfForm' | 'ManagerAssigned' | 'Either'
export type ChallengeOptionCycle = { id: string; code: string; name: string; status: CycleStatus; startsAt: string; endsAt: string }
export type ChallengeOptions = { cycles: ChallengeOptionCycle[]; scoringModes: ScoringMode[]; evidenceRequirements: EvidenceRequirement[]; formationModes: FormationMode[] }
export type ChallengeTaskWrite = { id: string | null; name: string; description: string | null; xp: number; scoringMode: ScoringMode; evidenceRequirement: EvidenceRequirement; sortOrder: number }
export type ChallengePolicy = { formationMode: FormationMode; minMembers: number; maxMembers: number; allowSolo: boolean; formationDeadline: string | null; lockAfterStart: boolean }
export type ChallengeAggregate = {
  id: string; version: string; cycleId: string; cycleCode: string; cycleName: string; name: string; description: string | null; category: string | null
  status: ChallengeStatus; openAt: string; dueAt: string; closeAt: string; heroImageReference: string | null; tasks: ChallengeTaskWrite[]; participationPolicy: ChallengePolicy | null
}
export type CreateChallengeRequest = Omit<ChallengeAggregate, 'id' | 'version' | 'cycleCode' | 'cycleName' | 'status'>
export type UpdateChallengeRequest = CreateChallengeRequest & { version: string }
