import type { WorldSummary } from '../api/worldTools.types'
import type { WorldOperationFormState, WorldOperationReview } from './worldOperationForm.types'

import {
  destination,
  normalBase,
  observed,
  positionLabel,
  requiredNumber,
  requiredText,
  review,
  strongBase,
} from './worldOperationForm.shared'

export function reviewIdentityOperation(
  form: WorldOperationFormState,
  summary: WorldSummary,
): WorldOperationReview | undefined {
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
        label: 'Delete land claim',
        target: request.claimId,
        scope: `Center ${positionLabel(center)}; radius ${request.protectionRadius}`,
        catalogVersion: null,
        impact: 'Permanently removes the fixed land claim after server-side identity revalidation.',
        reversible: false,
        strongConfirmation: false,
      })
    }
    case 'moveOnlinePlayer': {
      const target = requiredText(form.targetId, 'Cross-platform ID')
      const to = destination(form)
      const request = {
        ...normalBase(summary),
        crossplatformId: target,
        entityId: requiredNumber(form.entityId, 'Entity ID'),
        onlineObservedAtUtc: requiredText(form.onlineObservedAtUtc, 'Online observation time'),
        destination: to,
      }
      return review({ type: form.type, request }, summary, {
        label: 'Move online player',
        target,
        scope: `Destination ${positionLabel(to)}`,
        catalogVersion: null,
        impact: 'Moves the still-online, identity-matched player to the fixed destination.',
        reversible: false,
        strongConfirmation: false,
      })
    }
    case 'moveEntity': {
      const from = observed(form)
      const to = destination(form)
      const request = {
        ...normalBase(summary),
        targetId: requiredText(form.targetId, 'Target ID'),
        entityId: requiredNumber(form.entityId, 'Entity ID'),
        entityTypeResourceId: requiredText(form.entityTypeResourceId, 'Entity type resource ID'),
        ownerStableIdentity: form.ownerStableIdentity.trim() || null,
        observedPosition: from,
        destination: to,
      }
      return review({ type: form.type, request }, summary, {
        label: 'Move entity',
        target: request.targetId,
        scope: `${positionLabel(from)} → ${positionLabel(to)}`,
        catalogVersion: null,
        impact: 'Moves only the fixed entity if identity, type, owner, and observed position still match.',
        reversible: false,
        strongConfirmation: false,
      })
    }
    case 'deleteEntity': {
      const at = observed(form)
      const request = {
        ...strongBase(summary),
        catalogVersion: requiredText(form.catalogVersion, 'Catalog version'),
        targetId: requiredText(form.targetId, 'Target ID'),
        entityId: requiredNumber(form.entityId, 'Entity ID'),
        entityTypeResourceId: requiredText(form.entityTypeResourceId, 'Entity type resource ID'),
        ownerStableIdentity: form.ownerStableIdentity.trim() || null,
        observedPosition: at,
      }
      return review({ type: form.type, request }, summary, {
        label: 'Delete entity',
        target: request.targetId,
        scope: positionLabel(at),
        catalogVersion: request.catalogVersion,
        impact: 'Permanently deletes only the identity- and position-matched entity.',
        reversible: false,
        strongConfirmation: true,
      })
    }
    default:
      return undefined
  }
}
