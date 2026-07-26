import type {
  ComputedRef,
  DeepReadonly,
  MaybeRefOrGetter,
  ShallowRef,
} from 'vue'
import type {
  GameResourcePage,
  GameResourceViewState,
  LoadGameResources,
} from '../api/gameResources'
import type {
  GameResourceFilters,
  GameResourceKindFilter,
  GameResourceRouteQuery,
  GameResourceRouteQueryOutput,
} from './gameResourceFilters'

import { computed, onMounted, onUnmounted, readonly, shallowRef, toValue, watch } from 'vue'

import {
  gameResourceFiltersToRouteQuery,
  normalizeGameResourceLanguage,
  restoreGameResourceFilters,
  toGameResourceRequestQuery,
} from './gameResourceFilters'

const SEARCH_DEBOUNCE_MS = 250
const DEFAULT_BUILDING_RETRY_SECONDS = 2
const MIN_BUILDING_RETRY_SECONDS = 1
const MAX_BUILDING_RETRY_SECONDS = 10
const MAX_BUILDING_RETRIES = 3

interface RequestFailure {
  readonly code?: unknown
  readonly status?: unknown
  readonly problemCode?: unknown
  readonly retryAfterSeconds?: unknown
}

export interface UseGameResourcesOptions {
  readonly load: LoadGameResources
  readonly locale: MaybeRefOrGetter<string>
  readonly isOwner: MaybeRefOrGetter<boolean>
  readonly initialQuery?: GameResourceRouteQuery
  readonly replaceQuery?: (query: GameResourceRouteQueryOutput) => unknown
  readonly onSessionExpired?: () => void
}

export interface GameResourcesController {
  readonly state: DeepReadonly<ShallowRef<GameResourceViewState>>
  readonly page: DeepReadonly<ShallowRef<GameResourcePage | null>>
  readonly filters: DeepReadonly<ShallowRef<GameResourceFilters>>
  readonly isRefreshing: DeepReadonly<ShallowRef<boolean>>
  readonly totalPages: ComputedRef<number>
  readonly setSearch: (search: string) => void
  readonly setKind: (kind: GameResourceKindFilter) => void
  readonly setIncludeHidden: (includeHidden: boolean) => void
  readonly setPage: (page: number) => void
  readonly clearFilters: () => void
  readonly refresh: () => Promise<void>
  readonly retry: () => Promise<void>
  readonly dispose: () => void
}

function failureFields(error: unknown): RequestFailure {
  return typeof error === 'object' && error !== null ? error as RequestFailure : {}
}

function isAborted(error: unknown): boolean {
  const fields = failureFields(error)
  return fields.code === 'aborted'
    || (error instanceof DOMException && error.name === 'AbortError')
}

function buildingRetryMilliseconds(error: unknown): number {
  const raw = failureFields(error).retryAfterSeconds
  const seconds = typeof raw === 'number' && Number.isFinite(raw)
    ? Math.trunc(raw)
    : DEFAULT_BUILDING_RETRY_SECONDS
  return Math.min(
    MAX_BUILDING_RETRY_SECONDS,
    Math.max(MIN_BUILDING_RETRY_SECONDS, seconds),
  ) * 1_000
}

function queryKey(filters: GameResourceFilters, locale: string): string {
  return JSON.stringify(toGameResourceRequestQuery(
    filters,
    normalizeGameResourceLanguage(locale),
  ))
}

export function useGameResources(options: UseGameResourcesOptions): GameResourcesController {
  const initialQuery = options.initialQuery ?? {}
  const replaceQuery = options.replaceQuery ?? (() => {})
  const onSessionExpired = options.onSessionExpired ?? (() => {})
  const filters = shallowRef<GameResourceFilters>(restoreGameResourceFilters(
    initialQuery,
    toValue(options.isOwner),
  ))
  const state = shallowRef<GameResourceViewState>('loading')
  const page = shallowRef<GameResourcePage | null>(null)
  const isRefreshing = shallowRef(false)
  const totalPages = computed(() => page.value === null
    ? 0
    : Math.ceil(page.value.total / page.value.pageSize))

  let controller: AbortController | null = null
  let inFlight: Promise<void> | null = null
  let inFlightKey: string | null = null
  let requestVersion = 0
  let searchTimer: ReturnType<typeof setTimeout> | null = null
  let retryTimer: ReturnType<typeof setTimeout> | null = null
  let buildingRetries = 0
  let disposed = false

  function clearSearchTimer() {
    if (searchTimer !== null) {
      clearTimeout(searchTimer)
      searchTimer = null
    }
  }

  function clearRetryTimer() {
    if (retryTimer !== null) {
      clearTimeout(retryTimer)
      retryTimer = null
    }
  }

  function abortActiveRequest() {
    controller?.abort()
    controller = null
  }

  function scheduleBuildingRetry(error: unknown) {
    clearRetryTimer()
    if (disposed || buildingRetries >= MAX_BUILDING_RETRIES)
      return
    buildingRetries++
    retryTimer = setTimeout(() => {
      retryTimer = null
      void run()
    }, buildingRetryMilliseconds(error))
  }

  function handleFailure(error: unknown, version: number) {
    if (disposed || version !== requestVersion || isAborted(error))
      return
    const fields = failureFields(error)
    if (fields.status === 401)
      onSessionExpired()
    if (fields.status === 403) {
      clearRetryTimer()
      page.value = null
      state.value = 'forbidden'
      return
    }
    if (fields.status === 503 && fields.problemCode === 'game-resource-catalog-building' && page.value === null) {
      state.value = 'building'
      scheduleBuildingRetry(error)
      return
    }
    clearRetryTimer()
    state.value = page.value === null ? 'unavailable' : 'stale'
  }

  function run(): Promise<void> {
    if (disposed)
      return Promise.resolve()
    const currentKey = queryKey(filters.value, toValue(options.locale))
    if (inFlight !== null && inFlightKey === currentKey)
      return inFlight

    clearRetryTimer()
    abortActiveRequest()
    const currentController = new AbortController()
    controller = currentController
    const version = ++requestVersion
    inFlightKey = currentKey
    isRefreshing.value = page.value !== null
    if (page.value === null)
      state.value = 'loading'
    const requestQuery = toGameResourceRequestQuery(
      filters.value,
      normalizeGameResourceLanguage(toValue(options.locale)),
    )
    const request = (async () => {
      try {
        const nextPage = await options.load(requestQuery, currentController.signal)
        if (disposed || version !== requestVersion || currentController.signal.aborted)
          return
        page.value = nextPage
        buildingRetries = 0
        state.value = nextPage.warnings.length > 0
          ? 'partial'
          : nextPage.items.length === 0
            ? 'empty'
            : 'success'
      }
      catch (error) {
        handleFailure(error, version)
      }
      finally {
        if (controller === currentController)
          controller = null
        if (version === requestVersion) {
          inFlight = null
          inFlightKey = null
          isRefreshing.value = false
        }
      }
    })()
    inFlight = request
    return request
  }

  async function syncQueryAndRun() {
    await replaceQuery(gameResourceFiltersToRouteQuery(filters.value))
    if (!disposed)
      await run()
  }

  function applyImmediate(next: GameResourceFilters) {
    clearSearchTimer()
    clearRetryTimer()
    buildingRetries = 0
    filters.value = Object.freeze(next)
    void syncQueryAndRun()
  }

  function setSearch(search: string) {
    clearSearchTimer()
    const bounded = search.slice(0, 100)
    filters.value = Object.freeze({ ...filters.value, search: bounded, page: 1 })
    searchTimer = setTimeout(() => {
      searchTimer = null
      filters.value = Object.freeze({
        ...filters.value,
        search: filters.value.search.trim(),
      })
      buildingRetries = 0
      void syncQueryAndRun()
    }, SEARCH_DEBOUNCE_MS)
  }

  function setKind(kind: GameResourceKindFilter) {
    if (kind !== 'all' && kind !== 'item' && kind !== 'block')
      return
    applyImmediate({ ...filters.value, kind, page: 1 })
  }

  function setIncludeHidden(includeHidden: boolean) {
    applyImmediate({
      ...filters.value,
      includeHidden: toValue(options.isOwner) && includeHidden,
      page: 1,
    })
  }

  function setPage(nextPage: number) {
    if (!Number.isSafeInteger(nextPage) || nextPage < 1 || nextPage > 100_000)
      return
    applyImmediate({ ...filters.value, page: nextPage })
  }

  function clearFilters() {
    applyImmediate(restoreGameResourceFilters({}, toValue(options.isOwner)))
  }

  function refresh() {
    return run()
  }

  function retry() {
    buildingRetries = 0
    clearRetryTimer()
    return run()
  }

  watch(
    () => toValue(options.isOwner),
    (owner) => {
      if (!owner && filters.value.includeHidden)
        applyImmediate({ ...filters.value, includeHidden: false, page: 1 })
    },
  )

  watch(
    () => normalizeGameResourceLanguage(toValue(options.locale)),
    () => applyImmediate({ ...filters.value, page: 1 }),
  )

  function dispose() {
    if (disposed)
      return
    disposed = true
    requestVersion++
    clearSearchTimer()
    clearRetryTimer()
    abortActiveRequest()
    inFlight = null
    inFlightKey = null
    isRefreshing.value = false
  }

  onMounted(() => void run())
  onUnmounted(dispose)

  return {
    state: readonly(state),
    page: readonly(page),
    filters: readonly(filters),
    isRefreshing: readonly(isRefreshing),
    totalPages,
    setSearch,
    setKind,
    setIncludeHidden,
    setPage,
    clearFilters,
    refresh,
    retry,
    dispose,
  }
}
