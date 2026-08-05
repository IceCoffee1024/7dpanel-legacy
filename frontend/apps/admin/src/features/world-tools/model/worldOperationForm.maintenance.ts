import type { WorldSummary } from '../api/worldTools.types'
import type { WorldOperationFormState, WorldOperationReview } from './worldOperationForm.types'

import {
  bounds,
  boundsLabel,
  normalBase,
  observed,
  positionLabel,
  requiredNumber,
  requiredText,
  review,
  strongBase,
} from './worldOperationForm.shared'

export function reviewMaintenanceOperation(
  form: WorldOperationFormState,
  summary: WorldSummary,
): WorldOperationReview | undefined {
  switch (form.type) {
    case 'spawnEntity': {
      const center = observed(form)
      const request = {
        ...strongBase(summary),
        catalogVersion: requiredText(form.catalogVersion, 'Catalog version'),
        entityTypeResourceId: requiredText(form.entityTypeResourceId, 'Entity type resource ID'),
        quantity: requiredNumber(form.quantity, 'Quantity'),
        center,
        radius: requiredNumber(form.radius, 'Radius'),
      }
      return review({ type: form.type, request }, summary, {
        label: 'Spawn entity',
        target: request.entityTypeResourceId,
        scope: `Center ${positionLabel(center)}; radius ${request.radius}; quantity ${request.quantity}`,
        catalogVersion: request.catalogVersion,
        impact: 'Spawns the approved entity type inside the fixed bounded area.',
        reversible: false,
        strongConfirmation: true,
      })
    }
    case 'cleanupEntities': {
      const center = observed(form)
      const request = {
        ...strongBase(summary),
        category: form.entityCategory,
        center,
        radius: requiredNumber(form.radius, 'Radius'),
        maximumCount: requiredNumber(form.maximumCount, 'Maximum count'),
      }
      return review({ type: form.type, request }, summary, {
        label: 'Clean up entities',
        target: request.category,
        scope: `Center ${positionLabel(center)}; radius ${request.radius}; maximum ${request.maximumCount}`,
        catalogVersion: null,
        impact: 'Deletes up to the fixed maximum of matching, non-protected entities in the bounded area.',
        reversible: false,
        strongConfirmation: true,
      })
    }
    case 'reloadResource': {
      const request = { ...strongBase(summary), resourceKind: form.reloadResourceKind }
      return review({ type: form.type, request }, summary, {
        label: 'Reload game resource',
        target: request.resourceKind,
        scope: 'Current server process',
        catalogVersion: null,
        impact: 'Reloads only the selected compile-time resource category; no XML, path, or command text is accepted.',
        reversible: false,
        strongConfirmation: true,
      })
    }
    case 'collectGarbage': {
      const request = normalBase(summary)
      return review({ type: form.type, request }, summary, {
        label: 'Collect game garbage',
        target: 'Managed game runtime',
        scope: 'Current server process',
        catalogVersion: null,
        impact: 'Requests one bounded garbage collection operation without claiming a performance improvement.',
        reversible: false,
        strongConfirmation: false,
      })
    }
    case 'undoChangeSet': {
      const request = {
        ...strongBase(summary),
        sourceOperationId: requiredText(form.sourceOperationId, 'Source operation ID'),
        changeSetId: requiredText(form.changeSetId, 'Change set ID'),
        currentRegionHash: requiredText(form.currentRegionHash, 'Current region hash'),
      }
      return review({ type: form.type, request }, summary, {
        label: 'Undo change set',
        target: request.changeSetId,
        scope: `Source operation ${request.sourceOperationId}`,
        catalogVersion: null,
        impact: 'Applies a new rollback operation only when the current world version and region hash still match.',
        reversible: false,
        strongConfirmation: true,
      })
    }
    case 'refreshMapResources': {
      const area = bounds(form)
      const request = { ...normalBase(summary), bounds: area }
      return review({ type: form.type, request }, summary, {
        label: 'Refresh map resources',
        target: 'Published map resources',
        scope: boundsLabel(area),
        catalogVersion: null,
        impact: 'Builds a new server-managed map resource version; the 202 receipt is not completion.',
        reversible: false,
        strongConfirmation: false,
      })
    }
    case 'renderExploredMap': {
      const area = bounds(form)
      const request = { ...normalBase(summary), bounds: area }
      return review({ type: form.type, request }, summary, {
        label: 'Render explored map',
        target: 'Explored map tiles',
        scope: boundsLabel(area),
        catalogVersion: null,
        impact: 'Renders explored map resources into a new server-managed version.',
        reversible: false,
        strongConfirmation: false,
      })
    }
    case 'renderFullMap': {
      const area = bounds(form)
      const request = { ...strongBase(summary), bounds: area }
      return review({ type: form.type, request }, summary, {
        label: 'Render full map',
        target: 'Complete map tiles',
        scope: boundsLabel(area),
        catalogVersion: null,
        impact: 'Renders all requested map tiles as a long-running job and can consume substantial server resources.',
        reversible: false,
        strongConfirmation: true,
      })
    }
    default:
      return undefined
  }
}
