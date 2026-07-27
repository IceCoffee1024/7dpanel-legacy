export type ProfileSectionState = 'Available' | 'Partial' | 'Unavailable' | 'Forbidden'
export type EvidenceLevel = 'Confirmed' | 'ObservedChange'
export type ActionStatus = 'Pending' | 'Succeeded' | 'Rejected' | 'Failed' | 'Cancelled' | 'ResultUnknown'

export interface EvidenceGap {
  gapId: number
  startedAtUtc: string
  endedAtUtc: string
  reason: string
  estimatedLostCount: number
}

export interface ProfileSection<T> {
  state: ProfileSectionState
  observedAtUtc: string | null
  value: T | null
  gapMetadata: readonly EvidenceGap[]
}

export interface PlayerActionTarget {
  crossplatformId: string
  entityId: number
  onlineObservedAtUtc: string
  worldId: string
  name?: string
}

export interface InventoryItem {
  container: string
  slot: number
  internalName: string
  count: number
  quality: number | null
  useAmount: number | null
  modInternalNames: readonly string[]
}

export interface InventorySnapshot {
  snapshotId: number
  worldId: string
  observedAtUtc: string
  gameVersion: string
  catalogVersion: string | null
  catalogResolution: 'Resolved' | 'Unavailable'
  items: readonly InventoryItem[]
}

export interface InventoryDiffEntry {
  kind: string
  previousItem: InventoryItem | null
  currentItem: InventoryItem | null
  evidenceLevel: EvidenceLevel
  sourceOperationIds: readonly string[]
}

export interface InventoryDiff {
  currentSnapshotId: number
  currentObservedAtUtc: string
  isComplete: boolean
  changes: readonly InventoryDiffEntry[]
}

export interface SkillValue {
  skillKey: string
  state: 'Known' | 'UnsupportedByVersion' | 'NotLoaded' | 'Unknown'
  value: number | null
  minimum: number | null
  maximum: number | null
}

export interface SkillSnapshot {
  snapshotId: number
  worldId: string
  observedAtUtc: string
  gameVersion: string
  level: number | null
  skillPoints: number | null
  values: readonly SkillValue[]
}

export interface PlayerProfileData {
  crossplatformId: string
  summary: ProfileSection<{
    latestName: string
    firstObservedAtUtc: string
    lastObservedAtUtc: string
    totalObservationCount: number
  }>
  sessions: ProfileSection<ReadonlyArray<{
    sessionId: number
    worldId: string
    startedAtUtc: string
    endedAtUtc: string | null
    endReason: string | null
    completeness: ProfileSectionState
  }>>
  activity: ProfileSection<ReadonlyArray<{
    activityId: number
    kind: string
    worldId: string
    observedAtUtc: string
    completeness: ProfileSectionState
  }>>
  inventory: ProfileSection<InventorySnapshot>
  skills: ProfileSection<SkillSnapshot>
  dailyActivity: ProfileSection<ReadonlyArray<{
    localDate: string
    sessionCount: number | null
    loginCount: number | null
    chatMessageCount: number | null
    deathCount: number | null
    killCount: number | null
    inventoryObservationCount: number | null
  }>>
}

export interface PlayerActionFeedback {
  status: ActionStatus
  operationId: string | null
  failureCode: string | null
  manualVerificationRequired?: boolean
}

export function sameTarget(left: PlayerActionTarget | null, right: PlayerActionTarget | null): boolean {
  return left !== null && right !== null
    && left.crossplatformId === right.crossplatformId
    && left.entityId === right.entityId
    && left.onlineObservedAtUtc === right.onlineObservedAtUtc
    && left.worldId === right.worldId
}

export function copyTarget(target: PlayerActionTarget): PlayerActionTarget {
  return Object.freeze({ ...target })
}
