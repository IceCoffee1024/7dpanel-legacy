import type { OverviewSnapshot } from './overview'

import { HttpError } from '../../../shared/api/http'

export type OverviewStatus = 'loading' | 'fresh' | 'partial' | 'stale' | 'offline'
export type OverviewLoadErrorCode = 'network' | 'timeout' | 'unavailable'

export interface OverviewLoadError {
  code: OverviewLoadErrorCode
}

const availabilityProblemStates = new Set(['unavailable', 'forbidden'])

export function mapSnapshotStatus(snapshot: OverviewSnapshot): Exclude<OverviewStatus, 'loading'> {
  const partitions = [
    snapshot.game.availability,
    snapshot.host.availability,
    snapshot.restartPolicy.availability,
    snapshot.recentActivity.availability,
  ]
  if (snapshot.availability === 'unavailable')
    return 'offline'
  if (snapshot.availability === 'forbidden'
    || partitions.some(value => availabilityProblemStates.has(value))) {
    return 'partial'
  }
  if (snapshot.availability === 'stale' || partitions.includes('stale'))
    return 'stale'
  return 'fresh'
}

export function isOverviewAbortError(error: unknown): boolean {
  return (error instanceof HttpError && error.code === 'aborted')
    || (error instanceof DOMException && error.name === 'AbortError')
    || (error instanceof Error && error.name === 'AbortError')
}

export function toSafeOverviewError(error: unknown): OverviewLoadError {
  if (error instanceof HttpError && error.code === 'timeout')
    return Object.freeze({ code: 'timeout' })
  if (error instanceof HttpError && error.code === 'network')
    return Object.freeze({ code: 'network' })
  return Object.freeze({ code: 'unavailable' })
}
