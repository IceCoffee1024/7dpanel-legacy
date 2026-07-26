import type { DeepReadonly, ShallowRef } from 'vue'
import type { LocationQuery, LocationQueryRaw, Router } from 'vue-router'
import type { ChatHistoryPage } from '../api/chat'
import type { ChatHistoryFilters, ChatHistoryMessage, GameChatManagementState } from './gameChatManagement'

import { onMounted, onUnmounted, readonly, shallowRef } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import { chatGetMessagesQuery } from '../../../shared/api/generated/@pinia/colada.gen'
import { HttpError } from '../../../shared/api/http'
import { useAuthStore } from '../../auth'
import { parseChatHistoryPage } from '../api/chat'
import { createEmptyHistoryFilters } from './gameChatManagement'

const pageSize = 100

export interface ChatHistoryController {
  state: DeepReadonly<ShallowRef<GameChatManagementState>>
  messages: DeepReadonly<ShallowRef<readonly ChatHistoryMessage[]>>
  filters: DeepReadonly<ShallowRef<ChatHistoryFilters>>
  nextCursor: DeepReadonly<ShallowRef<string | null>>
  isLoadingMore: DeepReadonly<ShallowRef<boolean>>
  applyFilters: (filters: ChatHistoryFilters) => Promise<void>
  loadMore: () => Promise<void>
  retry: () => Promise<void>
  refresh: () => Promise<void>
  dispose: () => void
}

interface HistoryLocation {
  query: LocationQuery
}

export interface UseChatHistoryOptions {
  auth?: { authorizationHeader: string | null, expireSession: () => void }
  route?: HistoryLocation
  replaceQuery?: (query: LocationQueryRaw) => Promise<unknown> | unknown
  fetchHistory?: (
    authorizationHeader: string,
    filters: ChatHistoryFilters,
    cursor: string | null,
    limit: number,
    signal?: AbortSignal,
  ) => Promise<ChatHistoryPage>
  onSessionExpired?: () => void
}

function firstQueryValue(value: LocationQuery[string]): string {
  return Array.isArray(value) ? value[0] ?? '' : value ?? ''
}

function filtersFromQuery(query: LocationQuery): ChatHistoryFilters {
  const empty = createEmptyHistoryFilters()
  const chatType = firstQueryValue(query.chatType)
  const sourceKind = firstQueryValue(query.sourceKind)
  return {
    crossplatformId: firstQueryValue(query.crossplatformId),
    senderName: firstQueryValue(query.senderName),
    chatType: ['Global', 'Friends', 'Party', 'Whisper', 'Unknown'].includes(chatType)
      ? chatType as ChatHistoryFilters['chatType']
      : empty.chatType,
    sourceKind: ['Player', 'Administrator', 'System'].includes(sourceKind)
      ? sourceKind as ChatHistoryFilters['sourceKind']
      : empty.sourceKind,
    startUtc: firstQueryValue(query.startUtc),
    endUtc: firstQueryValue(query.endUtc),
  }
}

function historyQuery(filters: ChatHistoryFilters, cursor: string | null): LocationQueryRaw {
  const query: LocationQueryRaw = {}
  for (const [key, value] of Object.entries(filters)) {
    const normalized = value.trim()
    if (normalized !== '')
      query[key] = normalized
  }
  if (cursor !== null)
    query.cursor = cursor
  return query
}

async function fetchHistoryDefault(
  authorizationHeader: string,
  filters: ChatHistoryFilters,
  cursor: string | null,
  limit: number,
  signal?: AbortSignal,
): Promise<ChatHistoryPage> {
  const definition = chatGetMessagesQuery({
    headers: { Authorization: authorizationHeader },
    query: {
      limit,
      ...(cursor === null ? {} : { cursor }),
      ...(filters.crossplatformId === '' ? {} : { crossplatformId: filters.crossplatformId }),
      ...(filters.senderName === '' ? {} : { senderName: filters.senderName }),
      ...(filters.chatType === '' ? {} : { chatType: filters.chatType }),
      ...(filters.sourceKind === '' ? {} : { sourceKind: filters.sourceKind }),
      ...(filters.startUtc === '' ? {} : { startUtc: filters.startUtc }),
      ...(filters.endUtc === '' ? {} : { endUtc: filters.endUtc }),
    },
  })
  return parseChatHistoryPage(await definition.query({
    signal,
  } as Parameters<typeof definition.query>[0]))
}

function uniqueMessages(messages: readonly ChatHistoryMessage[]): readonly ChatHistoryMessage[] {
  const sequences = new Set<number>()
  return Object.freeze(messages.filter(message => !sequences.has(message.sequence) && sequences.add(message.sequence)))
}

export function useChatHistory(options: UseChatHistoryOptions = {}): ChatHistoryController {
  const auth = options.auth ?? useAuthStore()
  const route = options.route ?? useRoute()
  const router: Router | null = options.replaceQuery === undefined && options.route === undefined ? useRouter() : null
  const replaceQuery = options.replaceQuery ?? (query => router!.replace({ query }))
  const requestHistory = options.fetchHistory ?? fetchHistoryDefault
  const onSessionExpired = options.onSessionExpired ?? (() => {})
  const filters = shallowRef<ChatHistoryFilters>(Object.freeze(filtersFromQuery(route.query)))
  const state = shallowRef<GameChatManagementState>('loading')
  const messages = shallowRef<readonly ChatHistoryMessage[]>(Object.freeze([]))
  const nextCursor = shallowRef<string | null>(firstQueryValue(route.query.cursor) || null)
  const isLoadingMore = shallowRef(false)
  let controller: AbortController | null = null
  let requestVersion = 0
  let disposed = false
  let failedOperation: 'refresh' | 'load-more' = 'refresh'
  let sessionExpiryNotified = false

  function abortRequest() {
    controller?.abort()
    controller = null
  }

  function expireSession() {
    if (auth.authorizationHeader !== null)
      auth.expireSession()
    if (!sessionExpiryNotified) {
      sessionExpiryNotified = true
      onSessionExpired()
    }
  }

  function handleFailure(error: unknown, version: number, operation: 'refresh' | 'load-more') {
    if (disposed || version !== requestVersion || (error instanceof HttpError && error.code === 'aborted'))
      return
    failedOperation = operation
    if (error instanceof HttpError && error.status === 401)
      expireSession()
    if (error instanceof HttpError && error.status === 403) {
      messages.value = Object.freeze([])
      nextCursor.value = null
      state.value = 'forbidden'
      return
    }
    state.value = messages.value.length === 0 ? 'failed' : 'stale'
  }

  async function run(cursor: string | null, append: boolean): Promise<void> {
    if (disposed)
      return
    abortRequest()
    const version = ++requestVersion
    const current = new AbortController()
    controller = current
    isLoadingMore.value = append
    if (!append && messages.value.length === 0)
      state.value = 'loading'
    const authorizationHeader = auth.authorizationHeader
    if (authorizationHeader === null) {
      handleFailure(new HttpError('http', 'Authentication required', { status: 401 }), version, append ? 'load-more' : 'refresh')
      isLoadingMore.value = false
      return
    }
    try {
      const page = await requestHistory(authorizationHeader, filters.value, cursor, pageSize, current.signal)
      if (disposed || version !== requestVersion)
        return
      messages.value = uniqueMessages(append ? [...messages.value, ...page.messages] : page.messages)
      nextCursor.value = page.nextCursor
      state.value = messages.value.length === 0 ? 'empty' : 'ready'
      sessionExpiryNotified = false
      await replaceQuery(historyQuery(filters.value, append ? cursor : null))
    }
    catch (error) {
      handleFailure(error, version, append ? 'load-more' : 'refresh')
    }
    finally {
      if (version === requestVersion) {
        controller = null
        isLoadingMore.value = false
      }
    }
  }

  async function applyFilters(value: ChatHistoryFilters) {
    filters.value = Object.freeze({
      ...value,
      crossplatformId: value.crossplatformId.trim(),
      senderName: value.senderName.trim(),
      startUtc: value.startUtc.trim(),
      endUtc: value.endUtc.trim(),
    })
    messages.value = Object.freeze([])
    nextCursor.value = null
    await replaceQuery(historyQuery(filters.value, null))
    await run(null, false)
  }

  function refresh() {
    return run(null, false)
  }

  function loadMore() {
    if (nextCursor.value === null || isLoadingMore.value)
      return Promise.resolve()
    return run(nextCursor.value, true)
  }

  function retry() {
    return failedOperation === 'load-more' ? loadMore() : refresh()
  }

  function dispose() {
    if (disposed)
      return
    disposed = true
    requestVersion++
    abortRequest()
    isLoadingMore.value = false
  }

  onMounted(() => void run(nextCursor.value, false))
  onUnmounted(dispose)

  return {
    state: readonly(state),
    messages: readonly(messages),
    filters: readonly(filters),
    nextCursor: readonly(nextCursor),
    isLoadingMore: readonly(isLoadingMore),
    applyFilters,
    loadMore,
    retry,
    refresh,
    dispose,
  }
}
