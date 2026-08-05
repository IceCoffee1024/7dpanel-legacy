import type { WorldOperationSubmission } from '../api/worldTools.types'

export type WorldOperationFormType = WorldOperationSubmission['type']

export interface WorldOperationFormState {
  type: WorldOperationFormType
  targetId: string
  ownerStableIdentity: string
  entityId: number | null
  onlineObservedAtUtc: string
  entityTypeResourceId: string
  observedX: number | null
  observedY: number | null
  observedZ: number | null
  destinationX: number | null
  destinationY: number | null
  destinationZ: number | null
  firstX: number | null
  firstY: number | null
  firstZ: number | null
  secondX: number | null
  secondY: number | null
  secondZ: number | null
  catalogVersion: string
  blockInternalName: string
  rotation: number | null
  blockShape: 'Default' | 'Cube' | 'Ramp' | 'Wedge'
  prefabResourceId: string
  prefabInstanceId: string
  quantity: number | null
  radius: number | null
  maximumCount: number | null
  entityCategory: 'Animal' | 'Hostile' | 'Vehicle' | 'Drone' | 'DroppedItem'
  reloadResourceKind: 'Blocks' | 'Items' | 'EntityClasses' | 'Prefabs'
  sourceOperationId: string
  changeSetId: string
  currentRegionHash: string
  sourceChangeSetId: string
  boundsEnabled: boolean
  minimumX: number | null
  minimumZ: number | null
  maximumX: number | null
  maximumZ: number | null
}

export interface WorldOperationReview {
  submission: WorldOperationSubmission
  label: string
  target: string
  worldId: string
  scope: string
  worldVersion: string
  mapResourceVersion: string | null
  catalogVersion: string | null
  impact: string
  reversible: boolean
  strongConfirmation: boolean
}

export class WorldOperationFormError extends Error {
  constructor(message: string) {
    super(message)
    this.name = 'WorldOperationFormError'
  }
}
