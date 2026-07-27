import type {
  ConfirmedWorldRequest,
  StrongConfirmedWorldRequest,
  WorldCoordinateRequest,
  WorldMapBoundsRequest,
  WorldOperationSubmission,
  WorldRegionRequest,
  WorldSummary,
} from '../api/worldTools'

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

function requiredText(value: string, label: string): string {
  const normalized = value.trim()
  if (normalized === '')
    throw new WorldOperationFormError(`${label} is required.`)
  return normalized
}

function requiredNumber(value: number | null, label: string): number {
  if (value === null || !Number.isFinite(value))
    throw new WorldOperationFormError(`${label} is required.`)
  return value
}

function coordinate(x: number | null, y: number | null, z: number | null, label: string): WorldCoordinateRequest {
  return {
    x: requiredNumber(x, `${label} X`),
    y: requiredNumber(y, `${label} Y`),
    z: requiredNumber(z, `${label} Z`),
  }
}

function observed(form: WorldOperationFormState): WorldCoordinateRequest {
  return coordinate(form.observedX, form.observedY, form.observedZ, 'Observed position')
}

function destination(form: WorldOperationFormState): WorldCoordinateRequest {
  return coordinate(form.destinationX, form.destinationY, form.destinationZ, 'Destination')
}

function region(form: WorldOperationFormState): WorldRegionRequest {
  return {
    first: coordinate(form.firstX, form.firstY, form.firstZ, 'First corner'),
    second: coordinate(form.secondX, form.secondY, form.secondZ, 'Second corner'),
  }
}

function bounds(form: WorldOperationFormState): WorldMapBoundsRequest | null {
  if (!form.boundsEnabled)
    return null
  return {
    minimumX: requiredNumber(form.minimumX, 'Minimum X'),
    minimumZ: requiredNumber(form.minimumZ, 'Minimum Z'),
    maximumX: requiredNumber(form.maximumX, 'Maximum X'),
    maximumZ: requiredNumber(form.maximumZ, 'Maximum Z'),
  }
}

function normalBase(summary: WorldSummary): ConfirmedWorldRequest {
  if ((summary.sourceState !== 'Success' && summary.sourceState !== 'Partial')
    || summary.worldId === null
    || summary.worldVersion === null
    || summary.observedAtUtc === null) {
    throw new WorldOperationFormError('A current world snapshot is required before reviewing an operation.')
  }
  return {
    worldId: summary.worldId,
    worldVersion: summary.worldVersion,
    mapResourceVersion: summary.mapResourceVersion,
    confirmed: true,
  }
}

function strongBase(summary: WorldSummary): StrongConfirmedWorldRequest {
  return { ...normalBase(summary), strongConfirmed: true }
}

function positionLabel(value: WorldCoordinateRequest): string {
  return `${value.x}, ${value.y}, ${value.z}`
}

function regionLabel(value: WorldRegionRequest): string {
  return `${positionLabel(value.first)} → ${positionLabel(value.second)}`
}

function boundsLabel(value: WorldMapBoundsRequest | null): string {
  return value === null
    ? 'Entire available map'
    : `X ${value.minimumX}…${value.maximumX}; Z ${value.minimumZ}…${value.maximumZ}`
}

function review(
  submission: WorldOperationSubmission,
  summary: WorldSummary,
  details: Omit<WorldOperationReview, 'submission' | 'worldId' | 'worldVersion' | 'mapResourceVersion'>,
): WorldOperationReview {
  return Object.freeze({
    submission,
    worldId: summary.worldId!,
    worldVersion: summary.worldVersion!,
    mapResourceVersion: summary.mapResourceVersion,
    ...details,
  })
}

export function createWorldOperationReview(
  form: WorldOperationFormState,
  summary: WorldSummary,
): WorldOperationReview {
  switch (form.type) {
    case 'deleteLandClaim': {
      const center = observed(form)
      const request = {
        ...normalBase(summary),
        claimId: requiredText(form.targetId, 'Claim ID'),
        ownerStableIdentity: requiredText(form.ownerStableIdentity, 'Owner stable identity'),
        center,
        protectionRadius: requiredNumber(form.radius, 'Protection radius'),
      }
      return review({ type: form.type, request }, summary, {
        label: 'Delete land claim', target: request.claimId, scope: `Center ${positionLabel(center)}; radius ${request.protectionRadius}`,
        catalogVersion: null, impact: 'Permanently removes the fixed land claim after server-side identity revalidation.', reversible: false, strongConfirmation: false,
      })
    }
    case 'moveOnlinePlayer': {
      const target = requiredText(form.targetId, 'Cross-platform ID')
      const to = destination(form)
      const request = {
        ...normalBase(summary), crossplatformId: target, entityId: requiredNumber(form.entityId, 'Entity ID'),
        onlineObservedAtUtc: requiredText(form.onlineObservedAtUtc, 'Online observation time'), destination: to,
      }
      return review({ type: form.type, request }, summary, {
        label: 'Move online player', target, scope: `Destination ${positionLabel(to)}`,
        catalogVersion: null, impact: 'Moves the still-online, identity-matched player to the fixed destination.', reversible: false, strongConfirmation: false,
      })
    }
    case 'moveEntity': {
      const from = observed(form)
      const to = destination(form)
      const request = {
        ...normalBase(summary), targetId: requiredText(form.targetId, 'Target ID'), entityId: requiredNumber(form.entityId, 'Entity ID'),
        entityTypeResourceId: requiredText(form.entityTypeResourceId, 'Entity type resource ID'), ownerStableIdentity: form.ownerStableIdentity.trim() || null,
        observedPosition: from, destination: to,
      }
      return review({ type: form.type, request }, summary, {
        label: 'Move entity', target: request.targetId, scope: `${positionLabel(from)} → ${positionLabel(to)}`,
        catalogVersion: null, impact: 'Moves only the fixed entity if identity, type, owner, and observed position still match.', reversible: false, strongConfirmation: false,
      })
    }
    case 'copyRegion': {
      const area = region(form)
      const request = { ...normalBase(summary), region: area }
      return review({ type: form.type, request }, summary, {
        label: 'Copy region', target: 'Server-managed change set', scope: regionLabel(area), catalogVersion: null,
        impact: 'Captures the fixed region into a server-managed change set without accepting a file path.', reversible: false, strongConfirmation: false,
      })
    }
    case 'fillRegion': {
      const area = region(form)
      const request = { ...strongBase(summary), region: area, catalogVersion: requiredText(form.catalogVersion, 'Catalog version'), blockInternalName: requiredText(form.blockInternalName, 'Block internal name') }
      return review({ type: form.type, request }, summary, {
        label: 'Fill region', target: request.blockInternalName, scope: regionLabel(area), catalogVersion: request.catalogVersion,
        impact: 'Replaces blocks throughout the bounded region and records a change set.', reversible: true, strongConfirmation: true,
      })
    }
    case 'clearRegion': {
      const area = region(form)
      const request = { ...strongBase(summary), region: area }
      return review({ type: form.type, request }, summary, {
        label: 'Clear region', target: 'All blocks in region', scope: regionLabel(area), catalogVersion: null,
        impact: 'Clears the bounded region and records a change set.', reversible: true, strongConfirmation: true,
      })
    }
    case 'pasteRegion': {
      const area = region(form)
      const request = { ...strongBase(summary), region: area, sourceChangeSetId: requiredText(form.sourceChangeSetId, 'Source change set ID') }
      return review({ type: form.type, request }, summary, {
        label: 'Paste region', target: request.sourceChangeSetId, scope: regionLabel(area), catalogVersion: null,
        impact: 'Applies the server-owned source change set to the bounded region.', reversible: true, strongConfirmation: true,
      })
    }
    case 'setBlock': {
      const at = observed(form)
      const request = {
        ...strongBase(summary), catalogVersion: requiredText(form.catalogVersion, 'Catalog version'), coordinate: at,
        blockInternalName: requiredText(form.blockInternalName, 'Block internal name'), rotation: requiredNumber(form.rotation, 'Rotation'), shape: form.blockShape,
      }
      return review({ type: form.type, request }, summary, {
        label: 'Set block', target: request.blockInternalName, scope: positionLabel(at), catalogVersion: request.catalogVersion,
        impact: 'Replaces the block at exactly one coordinate after catalog and world revalidation.', reversible: true, strongConfirmation: true,
      })
    }
    case 'placePrefab': {
      const anchor = observed(form)
      const area = region(form)
      const request = {
        ...strongBase(summary), catalogVersion: requiredText(form.catalogVersion, 'Catalog version'), prefabResourceId: requiredText(form.prefabResourceId, 'Prefab resource ID'),
        anchor, rotation: requiredNumber(form.rotation, 'Rotation'), knownBounds: area,
      }
      return review({ type: form.type, request }, summary, {
        label: 'Place prefab', target: request.prefabResourceId, scope: `${positionLabel(anchor)}; bounds ${regionLabel(area)}`, catalogVersion: request.catalogVersion,
        impact: 'Places the approved prefab at the fixed anchor and records the affected bounds.', reversible: true, strongConfirmation: true,
      })
    }
    case 'removePrefab': {
      const anchor = observed(form)
      const area = region(form)
      const request = {
        ...strongBase(summary), catalogVersion: requiredText(form.catalogVersion, 'Catalog version'), prefabResourceId: requiredText(form.prefabResourceId, 'Prefab resource ID'),
        prefabInstanceId: requiredText(form.prefabInstanceId, 'Prefab instance ID'), anchor, rotation: requiredNumber(form.rotation, 'Rotation'), knownBounds: area,
      }
      return review({ type: form.type, request }, summary, {
        label: 'Remove prefab', target: request.prefabInstanceId, scope: `${positionLabel(anchor)}; bounds ${regionLabel(area)}`, catalogVersion: request.catalogVersion,
        impact: 'Removes only the identity-matched prefab instance and records the affected bounds.', reversible: true, strongConfirmation: true,
      })
    }
    case 'spawnEntity': {
      const center = observed(form)
      const request = {
        ...strongBase(summary), catalogVersion: requiredText(form.catalogVersion, 'Catalog version'), entityTypeResourceId: requiredText(form.entityTypeResourceId, 'Entity type resource ID'),
        quantity: requiredNumber(form.quantity, 'Quantity'), center, radius: requiredNumber(form.radius, 'Radius'),
      }
      return review({ type: form.type, request }, summary, {
        label: 'Spawn entity', target: request.entityTypeResourceId, scope: `Center ${positionLabel(center)}; radius ${request.radius}; quantity ${request.quantity}`, catalogVersion: request.catalogVersion,
        impact: 'Spawns the approved entity type inside the fixed bounded area.', reversible: false, strongConfirmation: true,
      })
    }
    case 'deleteEntity': {
      const at = observed(form)
      const request = {
        ...strongBase(summary), catalogVersion: requiredText(form.catalogVersion, 'Catalog version'), targetId: requiredText(form.targetId, 'Target ID'),
        entityId: requiredNumber(form.entityId, 'Entity ID'), entityTypeResourceId: requiredText(form.entityTypeResourceId, 'Entity type resource ID'),
        ownerStableIdentity: form.ownerStableIdentity.trim() || null, observedPosition: at,
      }
      return review({ type: form.type, request }, summary, {
        label: 'Delete entity', target: request.targetId, scope: positionLabel(at), catalogVersion: request.catalogVersion,
        impact: 'Permanently deletes only the identity- and position-matched entity.', reversible: false, strongConfirmation: true,
      })
    }
    case 'cleanupEntities': {
      const center = observed(form)
      const request = {
        ...strongBase(summary), category: form.entityCategory, center, radius: requiredNumber(form.radius, 'Radius'), maximumCount: requiredNumber(form.maximumCount, 'Maximum count'),
      }
      return review({ type: form.type, request }, summary, {
        label: 'Clean up entities', target: request.category, scope: `Center ${positionLabel(center)}; radius ${request.radius}; maximum ${request.maximumCount}`, catalogVersion: null,
        impact: 'Deletes up to the fixed maximum of matching, non-protected entities in the bounded area.', reversible: false, strongConfirmation: true,
      })
    }
    case 'reloadResource': {
      const request = { ...strongBase(summary), resourceKind: form.reloadResourceKind }
      return review({ type: form.type, request }, summary, {
        label: 'Reload game resource', target: request.resourceKind, scope: 'Current server process', catalogVersion: null,
        impact: 'Reloads only the selected compile-time resource category; no XML, path, or command text is accepted.', reversible: false, strongConfirmation: true,
      })
    }
    case 'collectGarbage': {
      const request = normalBase(summary)
      return review({ type: form.type, request }, summary, {
        label: 'Collect game garbage', target: 'Managed game runtime', scope: 'Current server process', catalogVersion: null,
        impact: 'Requests one bounded garbage collection operation without claiming a performance improvement.', reversible: false, strongConfirmation: false,
      })
    }
    case 'undoChangeSet': {
      const request = {
        ...strongBase(summary), sourceOperationId: requiredText(form.sourceOperationId, 'Source operation ID'),
        changeSetId: requiredText(form.changeSetId, 'Change set ID'), currentRegionHash: requiredText(form.currentRegionHash, 'Current region hash'),
      }
      return review({ type: form.type, request }, summary, {
        label: 'Undo change set', target: request.changeSetId, scope: `Source operation ${request.sourceOperationId}`, catalogVersion: null,
        impact: 'Applies a new rollback operation only when the current world version and region hash still match.', reversible: false, strongConfirmation: true,
      })
    }
    case 'refreshMapResources': {
      const area = bounds(form)
      const request = { ...normalBase(summary), bounds: area }
      return review({ type: form.type, request }, summary, {
        label: 'Refresh map resources', target: 'Published map resources', scope: boundsLabel(area), catalogVersion: null,
        impact: 'Builds a new server-managed map resource version; the 202 receipt is not completion.', reversible: false, strongConfirmation: false,
      })
    }
    case 'renderExploredMap': {
      const area = bounds(form)
      const request = { ...normalBase(summary), bounds: area }
      return review({ type: form.type, request }, summary, {
        label: 'Render explored map', target: 'Explored map tiles', scope: boundsLabel(area), catalogVersion: null,
        impact: 'Renders explored map resources into a new server-managed version.', reversible: false, strongConfirmation: false,
      })
    }
    case 'renderFullMap': {
      const area = bounds(form)
      const request = { ...strongBase(summary), bounds: area }
      return review({ type: form.type, request }, summary, {
        label: 'Render full map', target: 'Complete map tiles', scope: boundsLabel(area), catalogVersion: null,
        impact: 'Renders all requested map tiles as a long-running job and can consume substantial server resources.', reversible: false, strongConfirmation: true,
      })
    }
  }
}
