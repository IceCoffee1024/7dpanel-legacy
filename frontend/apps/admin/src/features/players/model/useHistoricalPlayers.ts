import type { DeepReadonly, ShallowRef } from 'vue'
import type { FetchHistoricalPlayersOptions, HistoricalPlayersPage, HistoricalPlayerSummary } from '../api/historyPlayers'

import { onMounted, onUnmounted, readonly, shallowRef, watch } from 'vue'

import { HttpError } from '../../../shared/api/http'
import { useAuthStore } from '../../auth'
import { fetchHistoricalPlayers } from '../api/historyPlayers'

export type HistoricalPlayersState = 'loading' | 'ready' | 'empty' | 'forbidden' | 'failed' | 'stale'
export type HistoricalPlayersErrorCode = 'network' | 'unavailable' | null

export interface HistoricalPlayersController {
  state: DeepReadonly<ShallowRef<HistoricalPlayersState>>
  players: DeepReadonly<ShallowRef<readonly HistoricalPlayerSummary[]>>
  nextCursor: DeepReadonly<ShallowRef<string | null>>
  search: ShallowRef<string>
  errorCode: DeepReadonly<ShallowRef<HistoricalPlayersErrorCode>>
  isRefreshing: DeepReadonly<ShallowRef<boolean>>
  isLoadingMore: DeepReadonly<ShallowRef<boolean>>
  refresh: () => Promise<void>
  loadMore: () => Promise<void>
  retry: () => Promise<void>
  dispose: () => void
}

export interface UseHistoricalPlayersOptions {
  auth?: {
    authorizationHeader: string | null
    expireSession: () => void
  }
  fetchPlayers?: (
    authorizationHeader: string,
    options: FetchHistoricalPlayersOptions,
    signal?: AbortSignal,
  ) => Promise<HistoricalPlayersPage>
  onSessionExpired?: () => void
}

function mapError(error: unknown): Exclude<HistoricalPlayersErrorCode, null> {
  return error instanceof HttpError && error.code === 'network' ? 'network' : 'unavailable'
}

function uniquePlayers(players: readonly HistoricalPlayerSummary[]): readonly HistoricalPlayerSummary[] {
  const seen = new Set<string>()
  return Object.freeze(players.filter((player) => {
    if (seen.has(player.crossplatformId))
      return false
    seen.add(player.crossplatformId)
    return true
  }))
}

export function useHistoricalPlayers(options: UseHistoricalPlayersOptions = {}): HistoricalPlayersController {
  const auth = options.auth ?? useAuthStore()
  const fetchPlayers = options.fetchPlayers ?? fetchHistoricalPlayers
  const onSessionExpired = options.onSessionExpired ?? (() => {})
  const state = shallowRef<HistoricalPlayersState>('loading')
  const players = shallowRef<readonly HistoricalPlayerSummary[]>(Object.freeze([]))
  const nextCursor = shallowRef<string | null>(null)
  const search = shallowRef('')
  const errorCode = shallowRef<HistoricalPlayersErrorCode>(null)
  const isRefreshing = shallowRef(false)
  const isLoadingMore = shallowRef(false)
  let inFlight: Promise<void> | null = null
  let requestController: AbortController | null = null
  let requestSequence = 0
  let lastFailure: 'refresh' | 'load-more' | null = null
  let disposed = false
  let sessionExpiryNotified = false

  function expireSession() {
    auth.expireSession()
    if (!sessionExpiryNotified) {
      sessionExpiryNotified = true
      onSessionExpired()
    }
  }

  function clearPage() {
    players.value = Object.freeze([])
    nextCursor.value = null
  }

  function abortActiveRequest() {
    requestController?.abort()
    requestController = null
    inFlight = null
  }

  function applyFailure(error: unknown, kind: 'refresh' | 'load-more', sequence: number) {
    if (disposed || sequence !== requestSequence || (error instanceof HttpError && error.code === 'aborted'))
      return
    if (error instanceof HttpError && error.status === 401) {
      expireSession()
      state.value = players.value.length === 0 ? 'failed' : 'stale'
      return
    }
    if (error instanceof HttpError && error.status === 403) {
      clearPage()
      errorCode.value = null
      state.value = 'forbidden'
      return
    }
    errorCode.value = mapError(error)
    state.value = players.value.length === 0 ? 'failed' : 'stale'
    lastFailure = kind
  }

  function startRefresh(clearExisting: boolean): Promise<void> {
    if (disposed)
      return Promise.resolve()
    abortActiveRequest()
    const sequence = ++requestSequence
    if (clearExisting) {
      clearPage()
      state.value = 'loading'
      errorCode.value = null
    }
    else if (players.value.length === 0) {
      state.value = 'loading'
      errorCode.value = null
    }

    if (auth.authorizationHeader === null) {
      isRefreshing.value = false
      state.value = players.value.length === 0 ? 'failed' : 'stale'
      expireSession()
      return Promise.resolve()
    }

    const query = search.value.trim()
    const controller = new AbortController()
    requestController = controller
    isRefreshing.value = true
    const request = fetchPlayers(auth.authorizationHeader, {
      query: query === '' ? null : query,
      pageSize: 50,
      cursor: null,
    }, controller.signal)
    const promise = request.then((page) => {
      if (disposed || sequence !== requestSequence)
        return
      players.value = uniquePlayers(page.players)
      nextCursor.value = page.nextCursor
      state.value = players.value.length === 0 ? 'empty' : 'ready'
      errorCode.value = null
      lastFailure = null
    }).catch((error: unknown) => {
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
    if (inFlight !== null || nextCursor.value === null || disposed)
      return inFlight ?? Promise.resolve()
    if (auth.authorizationHeader === null) {
      state.value = players.value.length === 0 ? 'failed' : 'stale'
      expireSession()
      return Promise.resolve()
    }

    const sequence = ++requestSequence
    const controller = new AbortController()
    requestController = controller
    isLoadingMore.value = true
    const query = search.value.trim()
    const request = fetchPlayers(auth.authorizationHeader, {
      query: query === '' ? null : query,
      pageSize: 50,
      cursor: nextCursor.value,
    }, controller.signal)
    const promise = request.then((page) => {
      if (disposed || sequence !== requestSequence)
        return
      players.value = uniquePlayers([...players.value, ...page.players])
      nextCursor.value = page.nextCursor
      state.value = players.value.length === 0 ? 'empty' : 'ready'
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

  watch(search, () => {
    if (!disposed)
      void startRefresh(true)
  })
  onMounted(() => {
    void startRefresh(true)
  })
  onUnmounted(dispose)

  return {
    state: readonly(state),
    players: readonly(players),
    nextCursor: readonly(nextCursor),
    search,
    errorCode: readonly(errorCode),
    isRefreshing: readonly(isRefreshing),
    isLoadingMore: readonly(isLoadingMore),
    refresh,
    loadMore,
    retry,
    dispose,
  }
}
