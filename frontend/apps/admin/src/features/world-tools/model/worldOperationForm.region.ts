import type { WorldSummary } from '../api/worldTools.types'
import type { WorldOperationFormState, WorldOperationReview } from './worldOperationForm.types'

import {
  normalBase,
  observed,
  positionLabel,
  region,
  regionLabel,
  requiredNumber,
  requiredText,
  review,
  strongBase,
} from './worldOperationForm.shared'

export function reviewRegionOperation(
  form: WorldOperationFormState,
  summary: WorldSummary,
): WorldOperationReview | undefined {
  switch (form.type) {
    case 'copyRegion': {
      const area = region(form)
      const request = { ...normalBase(summary), region: area }
      return review({ type: form.type, request }, summary, {
        label: 'Copy region',
        target: 'Server-managed change set',
        scope: regionLabel(area),
        catalogVersion: null,
        impact: 'Captures the fixed region into a server-managed change set without accepting a file path.',
        reversible: false,
        strongConfirmation: false,
      })
    }
    case 'fillRegion': {
      const area = region(form)
      const request = { ...strongBase(summary), region: area, catalogVersion: requiredText(form.catalogVersion, 'Catalog version'), blockInternalName: requiredText(form.blockInternalName, 'Block internal name') }
      return review({ type: form.type, request }, summary, {
        label: 'Fill region',
        target: request.blockInternalName,
        scope: regionLabel(area),
        catalogVersion: request.catalogVersion,
        impact: 'Replaces blocks throughout the bounded region and records a change set.',
        reversible: true,
        strongConfirmation: true,
      })
    }
    case 'clearRegion': {
      const area = region(form)
      const request = { ...strongBase(summary), region: area }
      return review({ type: form.type, request }, summary, {
        label: 'Clear region',
        target: 'All blocks in region',
        scope: regionLabel(area),
        catalogVersion: null,
        impact: 'Clears the bounded region and records a change set.',
        reversible: true,
        strongConfirmation: true,
      })
    }
    case 'pasteRegion': {
      const area = region(form)
      const request = { ...strongBase(summary), region: area, sourceChangeSetId: requiredText(form.sourceChangeSetId, 'Source change set ID') }
      return review({ type: form.type, request }, summary, {
        label: 'Paste region',
        target: request.sourceChangeSetId,
        scope: regionLabel(area),
        catalogVersion: null,
        impact: 'Applies the server-owned source change set to the bounded region.',
        reversible: true,
        strongConfirmation: true,
      })
    }
    case 'setBlock': {
      const at = observed(form)
      const request = {
        ...strongBase(summary),
        catalogVersion: requiredText(form.catalogVersion, 'Catalog version'),
        coordinate: at,
        blockInternalName: requiredText(form.blockInternalName, 'Block internal name'),
        rotation: requiredNumber(form.rotation, 'Rotation'),
        shape: form.blockShape,
      }
      return review({ type: form.type, request }, summary, {
        label: 'Set block',
        target: request.blockInternalName,
        scope: positionLabel(at),
        catalogVersion: request.catalogVersion,
        impact: 'Replaces the block at exactly one coordinate after catalog and world revalidation.',
        reversible: true,
        strongConfirmation: true,
      })
    }
    case 'placePrefab': {
      const anchor = observed(form)
      const area = region(form)
      const request = {
        ...strongBase(summary),
        catalogVersion: requiredText(form.catalogVersion, 'Catalog version'),
        prefabResourceId: requiredText(form.prefabResourceId, 'Prefab resource ID'),
        anchor,
        rotation: requiredNumber(form.rotation, 'Rotation'),
        knownBounds: area,
      }
      return review({ type: form.type, request }, summary, {
        label: 'Place prefab',
        target: request.prefabResourceId,
        scope: `${positionLabel(anchor)}; bounds ${regionLabel(area)}`,
        catalogVersion: request.catalogVersion,
        impact: 'Places the approved prefab at the fixed anchor and records the affected bounds.',
        reversible: true,
        strongConfirmation: true,
      })
    }
    case 'removePrefab': {
      const anchor = observed(form)
      const area = region(form)
      const request = {
        ...strongBase(summary),
        catalogVersion: requiredText(form.catalogVersion, 'Catalog version'),
        prefabResourceId: requiredText(form.prefabResourceId, 'Prefab resource ID'),
        prefabInstanceId: requiredText(form.prefabInstanceId, 'Prefab instance ID'),
        anchor,
        rotation: requiredNumber(form.rotation, 'Rotation'),
        knownBounds: area,
      }
      return review({ type: form.type, request }, summary, {
        label: 'Remove prefab',
        target: request.prefabInstanceId,
        scope: `${positionLabel(anchor)}; bounds ${regionLabel(area)}`,
        catalogVersion: request.catalogVersion,
        impact: 'Removes only the identity-matched prefab instance and records the affected bounds.',
        reversible: true,
        strongConfirmation: true,
      })
    }
    default:
      return undefined
  }
}
