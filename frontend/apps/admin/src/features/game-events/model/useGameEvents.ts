import type { DeepReadonly, ShallowRef } from 'vue'

import type {
  GameEventFilters,
  GameEventGap,
  GameEventRecord,
  GameEventViewState,
  LoadGameEvents,
} from '../api/gameEvents'
import { onMounted, onUnmounted, readonly, shallowRef } from 'vue'

import { HttpError } from '../../../shared/api/http'
import { useAuthStore } from '../../auth'
import {
  createEmptyGameEventFilters,
  loadGameEvents,
  normalizeGameEventFilters,
} from '../api/gameEvents'

const pageSize = 50

interface GameEventsAuth {
  authorizationHeader: string | null
  expireSession: () => void
}

export interface GameEventsController {
  state: DeepReadonly<ShallowRef<GameEventViewState>>
  events: DeepReadonly<ShallowRef<readonly GameEventRecord[]>>
  gaps: DeepReadonly<ShallowRef<readonly GameEventGap[]>>
  filters: DeepReadonly<ShallowRef<GameEventFilters>>
  nextCursor: DeepReadonly<ShallowRef<string | null>>
  pageNumber: DeepReadonly<ShallowRef<number>>
  applyFilters: (filters: GameEventFilters) => Promise<void>
  goToPage: (page: number) => Promise<void>
  refresh: () => Promise<void>
  retry: () => Promise<void>
  dispose: () => void
}

export interface UseGameEventsOptions {
  auth?: GameEventsAuth
  load?: LoadGameEvents
  onSessionExpired?: () => void
}

export function useGameEvents(options: UseGameEventsOptions = {}): GameEventsController {
  const auth = options.auth ?? useAuthStore()
  const requestPage = options.load ?? loadGameEvents
  const onSessionExpired = options.onSessionExpired ?? (() => {})
  const state = shallowRef<GameEventViewState>('loading')
  const events = shallowRef<readonly GameEventRecord[]>(Object.freeze([]))
  const gaps = shallowRef<readonly GameEventGap[]>(Object.freeze([]))
  const filters = shallowRef<GameEventFilters>(createEmptyGameEventFilters())
  const nextCursor = shallowRef<string | null>(null)
  const pageNumber = shallowRef(1)
  let cursorStack: Array<string | null> = [null]
  let requestController: AbortController | null = null
  let requestVersion = 0
  let disposed = false
  let sessionExpiryNotified = false

  function abortRequest() {
    requestController?.abort()
    requestController = null
  }

  function expireSession() {
    if (auth.authorizationHeader !== null)
      auth.expireSession()
    if (!sessionExpiryNotified) {
      sessionExpiryNotified = true
      onSessionExpired()
    }
  }

  function handleFailure(error: unknown, version: number) {
    if (disposed || version !== requestVersion || (error instanceof HttpError && error.code === 'aborted'))
      return
    if (error instanceof HttpError && error.status === 401)
      expireSession()
    if (error instanceof HttpError && error.status === 403) {
      events.value = Object.freeze([])
      gaps.value = Object.freeze([])
      nextCursor.value = null
      state.value = 'forbidden'
      return
    }
    state.value = events.value.length === 0 && gaps.value.length === 0 ? 'failed' : 'stale'
  }

  async function run(targetPage: number, cursor: string | null): Promise<void> {
    if (disposed)
      return
    abortRequest()
    const version = ++requestVersion
    const controller = new AbortController()
    requestController = controller
    if (events.value.length === 0 && gaps.value.length === 0)
      state.value = 'loading'
    const authorizationHeader = auth.authorizationHeader
    if (authorizationHeader === null) {
      handleFailure(new HttpError('http', 'Authentication required', { status: 401 }), version)
      requestController = null
      return
    }
    try {
      const page = await requestPage(
        authorizationHeader,
        filters.value,
        cursor,
        pageSize,
        controller.signal,
      )
      if (disposed || version !== requestVersion)
        return
      events.value = page.events
      gaps.value = page.gaps
      nextCursor.value = page.nextCursor
      pageNumber.value = targetPage
      state.value = 'ready'
      sessionExpiryNotified = false
    }
    catch (error) {
      handleFailure(error, version)
    }
    finally {
      if (version === requestVersion)
        requestController = null
    }
  }

  async function applyFilters(value: GameEventFilters) {
    filters.value = normalizeGameEventFilters(value)
    cursorStack = [null]
    pageNumber.value = 1
    nextCursor.value = null
    events.value = Object.freeze([])
    gaps.value = Object.freeze([])
    await run(1, null)
  }

  async function goToPage(targetPage: number) {
    if (targetPage === pageNumber.value)
      return
    if (targetPage === pageNumber.value + 1) {
      if (nextCursor.value === null)
        return
      cursorStack[targetPage - 1] = nextCursor.value
    }
    if (targetPage < 1 || targetPage > pageNumber.value + 1)
      return
    const cursor = cursorStack[targetPage - 1]
    if (cursor === undefined)
      return
    await run(targetPage, cursor)
  }

  function refresh() {
    return run(pageNumber.value, cursorStack[pageNumber.value - 1] ?? null)
  }

  function retry() {
    return refresh()
  }

  function dispose() {
    if (disposed)
      return
    disposed = true
    requestVersion++
    abortRequest()
  }

  onMounted(() => void refresh())
  onUnmounted(dispose)

  return {
    state: readonly(state),
    events: readonly(events),
    gaps: readonly(gaps),
    filters: readonly(filters),
    nextCursor: readonly(nextCursor),
    pageNumber: readonly(pageNumber),
    applyFilters,
    goToPage,
    refresh,
    retry,
    dispose,
  }
}
