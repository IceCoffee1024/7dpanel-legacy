import type { DeepReadonly, MaybeRefOrGetter, ShallowRef } from 'vue'
import type {
  FetchHistoricalSnapshotsOptions,
  HistoricalPlayerDetails,
  HistoricalPlayerSnapshot,
  HistoricalPlayerSnapshotsPage,
  PlayerHistoryGap,
} from '../api/historyPlayers'

import { onMounted, onUnmounted, readonly, shallowRef, toValue, watch } from 'vue'

import { HttpError } from '../../../shared/api/http'
import { useAuthStore } from '../../auth'
import { fetchHistoricalPlayer, fetchHistoricalSnapshots } from '../api/historyPlayers'

export type HistoricalPlayerState = 'loading' | 'ready' | 'empty' | 'forbidden' | 'not-found' | 'failed' | 'stale'
export type HistoricalPlayerErrorCode = 'network' | 'unavailable' | null

export interface HistoricalPlayerController {
  state: DeepReadonly<ShallowRef<HistoricalPlayerState>>
  details: DeepReadonly<ShallowRef<HistoricalPlayerDetails | null>>
  snapshots: DeepReadonly<ShallowRef<readonly HistoricalPlayerSnapshot[]>>
  gaps: DeepReadonly<ShallowRef<readonly PlayerHistoryGap[]>>
  nextBeforeSnapshotId: DeepReadonly<ShallowRef<number | null>>
  errorCode: DeepReadonly<ShallowRef<HistoricalPlayerErrorCode>>
  isRefreshing: DeepReadonly<ShallowRef<boolean>>
  isLoadingMore: DeepReadonly<ShallowRef<boolean>>
  refresh: () => Promise<void>
  loadMore: () => Promise<void>
  retry: () => Promise<void>
  dispose: () => void
}

export interface UseHistoricalPlayerOptions {
  crossplatformId: MaybeRefOrGetter<string>
  auth?: {
    authorizationHeader: string | null
    expireSession: () => void
  }
  fetchPlayer?: (
    authorizationHeader: string,
    crossplatformId: string,
    signal?: AbortSignal,
  ) => Promise<HistoricalPlayerDetails>
  fetchSnapshots?: (
    authorizationHeader: string,
    crossplatformId: string,
    options: FetchHistoricalSnapshotsOptions,
    signal?: AbortSignal,
  ) => Promise<HistoricalPlayerSnapshotsPage>
  onSessionExpired?: () => void
}

function mapError(error: unknown): Exclude<HistoricalPlayerErrorCode, null> {
  return error instanceof HttpError && error.code === 'network' ? 'network' : 'unavailable'
}

function uniqueSnapshots(snapshots: readonly HistoricalPlayerSnapshot[]): readonly HistoricalPlayerSnapshot[] {
  const seen = new Set<number>()
  return Object.freeze(snapshots.filter((snapshot) => {
    if (seen.has(snapshot.snapshotId))
      return false
    seen.add(snapshot.snapshotId)
    return true
  }))
}

function uniqueGaps(gaps: readonly PlayerHistoryGap[]): readonly PlayerHistoryGap[] {
  const seen = new Set<string>()
  return Object.freeze(gaps.filter((gap) => {
    if (seen.has(gap.gapId))
      return false
    seen.add(gap.gapId)
    return true
  }))
}

export function useHistoricalPlayer(options: UseHistoricalPlayerOptions): HistoricalPlayerController {
  const auth = options.auth ?? useAuthStore()
  const fetchPlayer = options.fetchPlayer ?? fetchHistoricalPlayer
  const fetchSnapshots = options.fetchSnapshots ?? fetchHistoricalSnapshots
  const onSessionExpired = options.onSessionExpired ?? (() => {})
  const state = shallowRef<HistoricalPlayerState>('loading')
  const details = shallowRef<HistoricalPlayerDetails | null>(null)
  const snapshots = shallowRef<readonly HistoricalPlayerSnapshot[]>(Object.freeze([]))
  const gaps = shallowRef<readonly PlayerHistoryGap[]>(Object.freeze([]))
  const nextBeforeSnapshotId = shallowRef<number | null>(null)
  const errorCode = shallowRef<HistoricalPlayerErrorCode>(null)
  const isRefreshing = shallowRef(false)
  const isLoadingMore = shallowRef(false)
  let inFlight: Promise<void> | null = null
  let requestController: AbortController | null = null
  let requestSequence = 0
  let lastFailure: 'refresh' | 'load-more' | null = null
  let disposed = false
  let sessionExpiryNotified = false

  function currentCrossplatformId(): string {
    return toValue(options.crossplatformId)
  }

  function expireSession() {
    auth.expireSession()
    if (!sessionExpiryNotified) {
      sessionExpiryNotified = true
      onSessionExpired()
    }
  }

  function clearHistory() {
    details.value = null
    snapshots.value = Object.freeze([])
    gaps.value = Object.freeze([])
    nextBeforeSnapshotId.value = null
  }

  function abortActiveRequest() {
    requestController?.abort()
    requestController = null
    inFlight = null
  }

  function hasLoadedData(): boolean {
    return details.value !== null || snapshots.value.length > 0
  }

  function applyFailure(error: unknown, kind: 'refresh' | 'load-more', sequence: number) {
    if (disposed || sequence !== requestSequence || (error instanceof HttpError && error.code === 'aborted'))
      return
    if (error instanceof HttpError && error.status === 401) {
      expireSession()
      state.value = hasLoadedData() ? 'stale' : 'failed'
      return
    }
    if (error instanceof HttpError && error.status === 403) {
      clearHistory()
      errorCode.value = null
      state.value = 'forbidden'
      return
    }
    if (error instanceof HttpError && error.status === 404) {
      clearHistory()
      errorCode.value = null
      state.value = 'not-found'
      return
    }
    errorCode.value = mapError(error)
    state.value = hasLoadedData() ? 'stale' : 'failed'
    lastFailure = kind
  }

  function startRefresh(clearExisting: boolean): Promise<void> {
    if (disposed)
      return Promise.resolve()
    abortActiveRequest()
    const sequence = ++requestSequence
    const crossplatformId = currentCrossplatformId()
    if (clearExisting) {
      clearHistory()
      state.value = 'loading'
      errorCode.value = null
    }
    else if (!hasLoadedData()) {
      state.value = 'loading'
      errorCode.value = null
    }

    if (crossplatformId.trim() === '') {
      state.value = 'not-found'
      return Promise.resolve()
    }
    if (auth.authorizationHeader === null) {
      isRefreshing.value = false
      state.value = hasLoadedData() ? 'stale' : 'failed'
      expireSession()
      return Promise.resolve()
    }

    const controller = new AbortController()
    requestController = controller
    isRefreshing.value = true
    const playerRequest = fetchPlayer(auth.authorizationHeader, crossplatformId, controller.signal)
    const snapshotsRequest = fetchSnapshots(auth.authorizationHeader, crossplatformId, { pageSize: 100 }, controller.signal)
    const promise = Promise.all([playerRequest, snapshotsRequest]).then(([nextDetails, page]) => {
      if (disposed || sequence !== requestSequence)
        return
      details.value = nextDetails
      snapshots.value = uniqueSnapshots(page.snapshots)
      gaps.value = uniqueGaps(page.gaps)
      nextBeforeSnapshotId.value = page.nextBeforeSnapshotId
      state.value = snapshots.value.length === 0 ? 'empty' : 'ready'
      errorCode.value = null
      lastFailure = null
    }).catch((error: unknown) => {
      controller.abort()
      applyFailure(error, 'refresh', sequence)
    }).finally(() => {
      if (sequence === requestSequence) {
        isRefreshing.value = false
        requestController = null
        inFlight = null
      }
    })
    inFlight = promise
    return promise
  }

  function refresh(): Promise<void> {
    if (inFlight !== null)
      return inFlight
    return startRefresh(false)
  }

  function loadMore(): Promise<void> {
    if (inFlight !== null || nextBeforeSnapshotId.value === null || disposed)
      return inFlight ?? Promise.resolve()
    if (auth.authorizationHeader === null) {
      state.value = hasLoadedData() ? 'stale' : 'failed'
      expireSession()
      return Promise.resolve()
    }

    const sequence = ++requestSequence
    const controller = new AbortController()
    requestController = controller
    isLoadingMore.value = true
    const request = fetchSnapshots(auth.authorizationHeader, currentCrossplatformId(), {
      pageSize: 100,
      beforeSnapshotId: nextBeforeSnapshotId.value,
    }, controller.signal)
    const promise = request.then((page) => {
      if (disposed || sequence !== requestSequence)
        return
      snapshots.value = uniqueSnapshots([...snapshots.value, ...page.snapshots])
      gaps.value = uniqueGaps([...gaps.value, ...page.gaps])
      nextBeforeSnapshotId.value = page.nextBeforeSnapshotId
      state.value = snapshots.value.length === 0 ? 'empty' : 'ready'
      errorCode.value = null
      lastFailure = null
    }).catch((error: unknown) => {
      applyFailure(error, 'load-more', sequence)
    }).finally(() => {
      if (sequence === requestSequence) {
        isLoadingMore.value = false
        requestController = null
        inFlight = null
      }
    })
    inFlight = promise
    return promise
  }

  function retry(): Promise<void> {
    return lastFailure === 'load-more' ? loadMore() : refresh()
  }

  function dispose() {
    if (disposed)
      return
    disposed = true
    requestSequence++
    abortActiveRequest()
    isRefreshing.value = false
    isLoadingMore.value = false
  }

  watch(() => currentCrossplatformId(), () => {
    if (!disposed)
      void startRefresh(true)
  })
  onMounted(() => {
    void startRefresh(true)
  })
  onUnmounted(dispose)

  return {
    state: readonly(state),
    details: readonly(details),
    snapshots: readonly(snapshots),
    gaps: readonly(gaps),
    nextBeforeSnapshotId: readonly(nextBeforeSnapshotId),
    errorCode: readonly(errorCode),
    isRefreshing: readonly(isRefreshing),
    isLoadingMore: readonly(isLoadingMore),
    refresh,
    loadMore,
    retry,
    dispose,
  }
}
