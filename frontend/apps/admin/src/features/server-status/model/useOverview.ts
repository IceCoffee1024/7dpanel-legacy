import type { DeepReadonly, ShallowRef } from 'vue'
import type { OverviewSnapshot } from './overview'

import { onMounted, onUnmounted, readonly, shallowRef } from 'vue'

import { HttpError } from '../../../shared/api/http'
import { useAuthStore } from '../../auth'
import { fetchOverview } from '../api/overview'
import { usePageVisibilityRefresh } from './usePageVisibilityRefresh'

export type OverviewStatus = 'loading' | 'fresh' | 'partial' | 'stale' | 'offline'
export type OverviewLoadErrorCode = 'network' | 'timeout' | 'unavailable'

export interface OverviewLoadError {
  code: OverviewLoadErrorCode
}

export interface OverviewController {
  snapshot: DeepReadonly<ShallowRef<OverviewSnapshot | null>>
  status: DeepReadonly<ShallowRef<OverviewStatus>>
  error: DeepReadonly<ShallowRef<OverviewLoadError | null>>
  refresh: () => Promise<void>
  dispose: () => void
}

export interface UseOverviewOptions {
  auth?: {
    authorizationHeader: string | null
    expireSession: () => void
  }
  fetchOverview?: (
    authorizationHeader: string,
    signal?: AbortSignal,
  ) => Promise<OverviewSnapshot>
  onSessionExpired?: () => void
}

const availabilityProblemStates = new Set(['unavailable', 'forbidden'])

function mapSnapshotStatus(snapshot: OverviewSnapshot): Exclude<OverviewStatus, 'loading'> {
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

function isAbortError(error: unknown): boolean {
  return (error instanceof HttpError && error.code === 'aborted')
    || (error instanceof DOMException && error.name === 'AbortError')
    || (error instanceof Error && error.name === 'AbortError')
}

function mapError(error: unknown): OverviewLoadError {
  if (error instanceof HttpError && error.code === 'timeout')
    return Object.freeze({ code: 'timeout' })
  if (error instanceof HttpError && error.code === 'network')
    return Object.freeze({ code: 'network' })
  return Object.freeze({ code: 'unavailable' })
}

export function useOverview(options: UseOverviewOptions = {}): OverviewController {
  const auth = options.auth ?? useAuthStore()
  const requestOverview = options.fetchOverview ?? fetchOverview
  const onSessionExpired = options.onSessionExpired ?? (() => {})
  const snapshot = shallowRef<OverviewSnapshot | null>(null)
  const status = shallowRef<OverviewStatus>('loading')
  const error = shallowRef<OverviewLoadError | null>(null)
  let generation = 0
  let controller: AbortController | null = null
  let disposed = false
  let sessionExpiryNotified = false

  async function runRefresh(): Promise<void> {
    if (disposed)
      return

    const currentGeneration = ++generation
    controller?.abort()
    controller = null

    if (auth.authorizationHeader === null) {
      status.value = snapshot.value === null ? 'offline' : 'stale'
      error.value = null
      if (!sessionExpiryNotified) {
        sessionExpiryNotified = true
        onSessionExpired()
      }
      return
    }

    const authorizationHeader = auth.authorizationHeader
    const currentController = new AbortController()
    controller = currentController
    if (snapshot.value === null)
      status.value = 'loading'
    error.value = null

    try {
      const nextSnapshot = await requestOverview(authorizationHeader, currentController.signal)
      if (disposed || currentGeneration !== generation)
        return
      snapshot.value = nextSnapshot
      status.value = mapSnapshotStatus(nextSnapshot)
      error.value = null
      sessionExpiryNotified = false
    }
    catch (cause) {
      if (disposed || currentGeneration !== generation || isAbortError(cause))
        return
      if (cause instanceof HttpError && cause.status === 401) {
        auth.expireSession()
        error.value = null
        status.value = snapshot.value === null ? 'offline' : 'stale'
        if (!sessionExpiryNotified) {
          sessionExpiryNotified = true
          onSessionExpired()
        }
        return
      }
      error.value = mapError(cause)
      status.value = snapshot.value === null ? 'offline' : 'stale'
    }
    finally {
      if (currentGeneration === generation && controller === currentController)
        controller = null
    }
  }

  const scheduler = usePageVisibilityRefresh(runRefresh)

  function refresh(): Promise<void> {
    scheduler.resetPeriod()
    return runRefresh()
  }

  function dispose() {
    if (disposed)
      return
    disposed = true
    generation++
    controller?.abort()
    controller = null
    scheduler.dispose()
  }

  onMounted(() => {
    if (scheduler.visibility.value === 'visible')
      void runRefresh()
  })
  onUnmounted(dispose)

  return {
    snapshot: readonly(snapshot),
    status: readonly(status),
    error: readonly(error),
    refresh,
    dispose,
  }
}
