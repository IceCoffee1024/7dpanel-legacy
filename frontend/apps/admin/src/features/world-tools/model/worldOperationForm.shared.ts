import type {
  ConfirmedWorldRequest,
  StrongConfirmedWorldRequest,
  WorldCoordinateRequest,
  WorldMapBoundsRequest,
  WorldOperationSubmission,
  WorldRegionRequest,
  WorldSummary,
} from '../api/worldTools.types'
import type { WorldOperationFormState, WorldOperationReview } from './worldOperationForm.types'

import { WorldOperationFormError } from './worldOperationForm.types'

export function requiredText(value: string, label: string): string {
  const normalized = value.trim()
  if (normalized === '')
    throw new WorldOperationFormError(`${label} is required.`)
  return normalized
}

export function requiredNumber(value: number | null, label: string): number {
  if (value === null || !Number.isFinite(value))
    throw new WorldOperationFormError(`${label} is required.`)
  return value
}

export function coordinate(x: number | null, y: number | null, z: number | null, label: string): WorldCoordinateRequest {
  return {
    x: requiredNumber(x, `${label} X`),
    y: requiredNumber(y, `${label} Y`),
    z: requiredNumber(z, `${label} Z`),
  }
}

export function observed(form: WorldOperationFormState): WorldCoordinateRequest {
  return coordinate(form.observedX, form.observedY, form.observedZ, 'Observed position')
}

export function destination(form: WorldOperationFormState): WorldCoordinateRequest {
  return coordinate(form.destinationX, form.destinationY, form.destinationZ, 'Destination')
}

export function region(form: WorldOperationFormState): WorldRegionRequest {
  return {
    first: coordinate(form.firstX, form.firstY, form.firstZ, 'First corner'),
    second: coordinate(form.secondX, form.secondY, form.secondZ, 'Second corner'),
  }
}

export function bounds(form: WorldOperationFormState): WorldMapBoundsRequest | null {
  if (!form.boundsEnabled)
    return null
  return {
    minimumX: requiredNumber(form.minimumX, 'Minimum X'),
    minimumZ: requiredNumber(form.minimumZ, 'Minimum Z'),
    maximumX: requiredNumber(form.maximumX, 'Maximum X'),
    maximumZ: requiredNumber(form.maximumZ, 'Maximum Z'),
  }
}

export function normalBase(summary: WorldSummary): ConfirmedWorldRequest {
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

export function strongBase(summary: WorldSummary): StrongConfirmedWorldRequest {
  return { ...normalBase(summary), strongConfirmed: true }
}

export function positionLabel(value: WorldCoordinateRequest): string {
  return `${value.x}, ${value.y}, ${value.z}`
}

export function regionLabel(value: WorldRegionRequest): string {
  return `${positionLabel(value.first)} → ${positionLabel(value.second)}`
}

export function boundsLabel(value: WorldMapBoundsRequest | null): string {
  return value === null
    ? 'Entire available map'
    : `X ${value.minimumX}…${value.maximumX}; Z ${value.minimumZ}…${value.maximumZ}`
}

export function review(
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
