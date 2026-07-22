import type { DeepReadonly, ShallowRef } from 'vue'
import type { OnlinePlayersSnapshot } from '../api/onlinePlayers'

import { onMounted, onUnmounted, readonly, shallowRef } from 'vue'

import { HttpError } from '../../../shared/api/http'
import { useAuthStore } from '../../auth'
import { fetchOnlinePlayers } from '../api/onlinePlayers'

export type OnlinePlayersState = 'loading' | 'fresh' | 'stale' | 'offline' | 'forbidden'
export type OnlinePlayersErrorCode = 'game-not-ready' | 'busy' | 'timeout' | 'unavailable' | 'network' | null

export interface VisibilitySource {
  isVisible: () => boolean
  subscribe: (listener: () => void) => () => void
}

export interface OnlinePlayersController {
  state: DeepReadonly<ShallowRef<OnlinePlayersState>>
  snapshot: DeepReadonly<ShallowRef<OnlinePlayersSnapshot | null>>
  errorCode: DeepReadonly<ShallowRef<OnlinePlayersErrorCode>>
  isRefreshing: DeepReadonly<ShallowRef<boolean>>
  refresh: () => Promise<void>
  dispose: () => void
}

export interface UseOnlinePlayersOptions {
  auth?: {
    authorizationHeader: string | null
    expireSession: () => void
  }
  fetchPlayers?: (authorizationHeader: string, signal?: AbortSignal) => Promise<OnlinePlayersSnapshot>
  visibility?: VisibilitySource
  onSessionExpired?: () => void
}

const documentVisibility: VisibilitySource = {
  isVisible: () => document.visibilityState === 'visible',
  subscribe(listener) {
    document.addEventListener('visibilitychange', listener)
    return () => document.removeEventListener('visibilitychange', listener)
  },
}

function mapError(error: unknown): Exclude<OnlinePlayersErrorCode, null> {
  if (!(error instanceof HttpError))
    return 'network'
  if (error.code === 'timeout')
    return 'timeout'
  if (error.code === 'network')
    return 'network'
  if (error.status === 503) {
    const problemCodes: Record<string, Exclude<OnlinePlayersErrorCode, null>> = {
      game_not_ready: 'game-not-ready',
      online_player_query_busy: 'busy',
      game_thread_timeout: 'timeout',
      online_player_snapshot_unavailable: 'unavailable',
    }
    if (error.problemCode !== undefined && problemCodes[error.problemCode] !== undefined)
      return problemCodes[error.problemCode]
  }
  return 'unavailable'
}

export function useOnlinePlayers(options: UseOnlinePlayersOptions = {}): OnlinePlayersController {
  const auth = options.auth ?? useAuthStore()
  const fetchPlayers = options.fetchPlayers ?? fetchOnlinePlayers
  const visibility = options.visibility ?? documentVisibility
  const onSessionExpired = options.onSessionExpired ?? (() => {})
  const state = shallowRef<OnlinePlayersState>('loading')
  const snapshot = shallowRef<OnlinePlayersSnapshot | null>(null)
  const errorCode = shallowRef<OnlinePlayersErrorCode>(null)
  const isRefreshing = shallowRef(false)
  let inFlight: Promise<void> | null = null
  let requestSequence = 0
  let controller: AbortController | null = null
  let interval: ReturnType<typeof setInterval> | null = null
  let unsubscribeVisibility: (() => void) | null = null
  let disposed = false
  let automaticRefreshEnabled = true
  let sessionExpiryNotified = false

  function clearPeriod() {
    if (interval === null)
      return
    clearInterval(interval)
    interval = null
  }

  function startPeriod() {
    clearPeriod()
    if (disposed || !visibility.isVisible())
      return
    interval = setInterval(() => {
      if (visibility.isVisible() && automaticRefreshEnabled)
        void refresh()
    }, 10_000)
  }

  function refresh(): Promise<void> {
    if (inFlight !== null)
      return inFlight
    if (disposed || !visibility.isVisible())
      return Promise.resolve()
    if (auth.authorizationHeader === null) {
      clearPeriod()
      state.value = snapshot.value === null ? 'offline' : 'stale'
      errorCode.value = null
      if (!sessionExpiryNotified) {
        sessionExpiryNotified = true
        onSessionExpired()
      }
      return Promise.resolve()
    }

    const sequence = ++requestSequence
    controller = new AbortController()
    isRefreshing.value = true
    const request = fetchPlayers(auth.authorizationHeader, controller.signal)
    const requestPromise = request.then((nextSnapshot) => {
      if (disposed || sequence !== requestSequence)
        return
      snapshot.value = nextSnapshot
      state.value = 'fresh'
      errorCode.value = null
      automaticRefreshEnabled = true
    }).catch((error: unknown) => {
      if (disposed || sequence !== requestSequence)
        return
      if (error instanceof HttpError && error.code === 'aborted')
        return
      if (error instanceof HttpError && error.status === 401) {
        auth.expireSession()
        sessionExpiryNotified = true
        onSessionExpired()
        return
      }
      if (error instanceof HttpError && error.status === 403) {
        snapshot.value = null
        state.value = 'forbidden'
        errorCode.value = null
        automaticRefreshEnabled = false
        return
      }
      errorCode.value = mapError(error)
      state.value = snapshot.value === null ? 'offline' : 'stale'
    }).finally(() => {
      if (sequence === requestSequence) {
        isRefreshing.value = false
        controller = null
        inFlight = null
      }
    })
    inFlight = requestPromise
    return requestPromise
  }

  function handleVisibilityChange() {
    if (!visibility.isVisible()) {
      clearPeriod()
      return
    }
    startPeriod()
    if (automaticRefreshEnabled)
      void refresh()
  }

  function dispose() {
    if (disposed)
      return
    disposed = true
    requestSequence++
    controller?.abort()
    controller = null
    inFlight = null
    isRefreshing.value = false
    clearPeriod()
    unsubscribeVisibility?.()
    unsubscribeVisibility = null
  }

  onMounted(() => {
    unsubscribeVisibility = visibility.subscribe(handleVisibilityChange)
    startPeriod()
    if (visibility.isVisible())
      void refresh()
  })
  onUnmounted(dispose)

  return {
    state: readonly(state),
    snapshot: readonly(snapshot),
    errorCode: readonly(errorCode),
    isRefreshing: readonly(isRefreshing),
    refresh,
    dispose,
  }
}
