import type { WorldSummary } from '../api/worldTools.types'
import type { WorldOperationFormState, WorldOperationReview } from './worldOperationForm.types'

import { reviewIdentityOperation } from './worldOperationForm.identity'
import { reviewMaintenanceOperation } from './worldOperationForm.maintenance'
import { reviewRegionOperation } from './worldOperationForm.region'

export type { WorldOperationFormState, WorldOperationFormType, WorldOperationReview } from './worldOperationForm.types'
export { WorldOperationFormError } from './worldOperationForm.types'

export function createInitialWorldOperationForm(): WorldOperationFormState {
  return {
    type: 'deleteLandClaim',
    targetId: '',
    ownerStableIdentity: '',
    entityId: null,
    onlineObservedAtUtc: '',
    entityTypeResourceId: '',
    observedX: null,
    observedY: null,
    observedZ: null,
    destinationX: null,
    destinationY: null,
    destinationZ: null,
    firstX: null,
    firstY: null,
    firstZ: null,
    secondX: null,
    secondY: null,
    secondZ: null,
    catalogVersion: '',
    blockInternalName: '',
    rotation: 0,
    blockShape: 'Default',
    prefabResourceId: '',
    prefabInstanceId: '',
    quantity: 1,
    radius: 1,
    maximumCount: 1,
    entityCategory: 'Hostile',
    reloadResourceKind: 'Blocks',
    sourceOperationId: '',
    changeSetId: '',
    currentRegionHash: '',
    sourceChangeSetId: '',
    boundsEnabled: false,
    minimumX: null,
    minimumZ: null,
    maximumX: null,
    maximumZ: null,
  }
}

export function createWorldOperationReview(
  form: WorldOperationFormState,
  summary: WorldSummary,
): WorldOperationReview {
  switch (form.type) {
    case 'deleteLandClaim':
    case 'moveOnlinePlayer':
    case 'moveEntity':
    case 'deleteEntity':
      return reviewIdentityOperation(form, summary)!
    case 'copyRegion':
    case 'fillRegion':
    case 'clearRegion':
    case 'pasteRegion':
    case 'setBlock':
    case 'placePrefab':
    case 'removePrefab':
      return reviewRegionOperation(form, summary)!
    case 'spawnEntity':
    case 'cleanupEntities':
    case 'reloadResource':
    case 'collectGarbage':
    case 'undoChangeSet':
    case 'refreshMapResources':
    case 'renderExploredMap':
    case 'renderFullMap':
      return reviewMaintenanceOperation(form, summary)!
  }
}
