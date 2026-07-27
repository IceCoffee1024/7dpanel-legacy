import type { DeepReadonly, ShallowRef } from 'vue'

import type { AuditPage, LoadAuditEntries } from '../api/audit'
import type { AuditEntry, AuditFilters, AuditSourceGap, EvidenceViewState } from './audit'
import { onMounted, onUnmounted, readonly, shallowRef } from 'vue'

import { HttpError } from '../../../shared/api/http'
import { useAuthStore } from '../../auth'
import { loadAuditEntries } from '../api/audit'
import { createEmptyAuditFilters, normalizeAuditFilters } from './audit'

const pageSize = 50

interface AuditAuth {
  authorizationHeader: string | null
  expireSession: () => void
}

export interface AuditWorkspaceController {
  state: DeepReadonly<ShallowRef<EvidenceViewState>>
  entries: DeepReadonly<ShallowRef<readonly AuditEntry[]>>
  sourceGaps: DeepReadonly<ShallowRef<readonly AuditSourceGap[]>>
  filters: DeepReadonly<ShallowRef<AuditFilters>>
  nextCursor: DeepReadonly<ShallowRef<string | null>>
  pageNumber: DeepReadonly<ShallowRef<number>>
  applyFilters: (filters: AuditFilters) => Promise<void>
  goToPage: (page: number) => Promise<void>
  refresh: () => Promise<void>
  retry: () => Promise<void>
  dispose: () => void
}

export interface UseAuditWorkspaceOptions {
  auth?: AuditAuth
  load?: LoadAuditEntries
  onSessionExpired?: () => void
}

export function useAuditWorkspace(options: UseAuditWorkspaceOptions = {}): AuditWorkspaceController {
  const auth = options.auth ?? useAuthStore()
  const requestPage = options.load ?? loadAuditEntries
  const onSessionExpired = options.onSessionExpired ?? (() => {})
  const state = shallowRef<EvidenceViewState>('loading')
  const entries = shallowRef<readonly AuditEntry[]>(Object.freeze([]))
  const sourceGaps = shallowRef<readonly AuditSourceGap[]>(Object.freeze([]))
  const filters = shallowRef<AuditFilters>(createEmptyAuditFilters())
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
      entries.value = Object.freeze([])
      sourceGaps.value = Object.freeze([])
      nextCursor.value = null
      state.value = 'forbidden'
      return
    }
    state.value = entries.value.length === 0 ? 'failed' : 'stale'
  }

  async function run(targetPage: number, cursor: string | null): Promise<void> {
    if (disposed)
      return
    abortRequest()
    const version = ++requestVersion
    const controller = new AbortController()
    requestController = controller
    if (entries.value.length === 0)
      state.value = 'loading'
    const authorizationHeader = auth.authorizationHeader
    if (authorizationHeader === null) {
      handleFailure(new HttpError('http', 'Authentication required', { status: 401 }), version)
      requestController = null
      return
    }
    try {
      const page: AuditPage = await requestPage(
        authorizationHeader,
        filters.value,
        cursor,
        pageSize,
        controller.signal,
      )
      if (disposed || version !== requestVersion)
        return
      entries.value = page.entries
      sourceGaps.value = page.sourceGaps
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

  async function applyFilters(value: AuditFilters) {
    filters.value = normalizeAuditFilters(value)
    cursorStack = [null]
    pageNumber.value = 1
    nextCursor.value = null
    entries.value = Object.freeze([])
    sourceGaps.value = Object.freeze([])
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
    entries: readonly(entries),
    sourceGaps: readonly(sourceGaps),
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
