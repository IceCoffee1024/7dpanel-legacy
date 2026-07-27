import type { DeepReadonly, ShallowRef } from 'vue'

import type {
  ChatMuteCursor,
  ChatMuteRecord,
  ChatMuteWriteInput,
  CreateChatMute,
  CreateChatMuteInput,
  LoadChatMutes,
  ReleaseChatMute,
  UpdateChatMute,
} from '../api/chatMutes'
import { onMounted, onUnmounted, readonly, shallowRef } from 'vue'

import { HttpError } from '../../../shared/api/http'
import { useAuthStore } from '../../auth'
import {
  createChatMuteRecord,
  loadChatMutes,
  releaseChatMuteRecord,
  updateChatMuteRecord,
} from '../api/chatMutes'

const pageSize = 50
const emptyCursor = Object.freeze<ChatMuteCursor>({ updatedAtUtc: null, crossplatformId: null })

export type ChatMutesViewState = 'loading' | 'ready' | 'stale' | 'failed' | 'forbidden'

interface ChatMutesAuth {
  authorizationHeader: string | null
  expireSession: () => void
}

export interface ChatMutesController {
  state: DeepReadonly<ShallowRef<ChatMutesViewState>>
  mutes: DeepReadonly<ShallowRef<readonly ChatMuteRecord[]>>
  nextCursor: DeepReadonly<ShallowRef<ChatMuteCursor | null>>
  pageNumber: DeepReadonly<ShallowRef<number>>
  isMutating: DeepReadonly<ShallowRef<boolean>>
  create: (input: CreateChatMuteInput) => Promise<boolean>
  update: (crossplatformId: string, input: ChatMuteWriteInput) => Promise<boolean>
  release: (crossplatformId: string, correlationId: string | null) => Promise<boolean>
  goToPage: (page: number) => Promise<void>
  refresh: () => Promise<void>
  retry: () => Promise<void>
  dispose: () => void
}

export interface UseChatMutesOptions {
  auth?: ChatMutesAuth
  load?: LoadChatMutes
  create?: CreateChatMute
  update?: UpdateChatMute
  release?: ReleaseChatMute
  onSessionExpired?: () => void
}

export function useChatMutes(options: UseChatMutesOptions = {}): ChatMutesController {
  const auth = options.auth ?? useAuthStore()
  const requestPage = options.load ?? loadChatMutes
  const createRecord = options.create ?? createChatMuteRecord
  const updateRecord = options.update ?? updateChatMuteRecord
  const releaseRecord = options.release ?? releaseChatMuteRecord
  const onSessionExpired = options.onSessionExpired ?? (() => {})
  const state = shallowRef<ChatMutesViewState>('loading')
  const mutes = shallowRef<readonly ChatMuteRecord[]>(Object.freeze([]))
  const nextCursor = shallowRef<ChatMuteCursor | null>(null)
  const pageNumber = shallowRef(1)
  const isMutating = shallowRef(false)
  let cursorStack: ChatMuteCursor[] = [emptyCursor]
  let listController: AbortController | null = null
  let mutationController: AbortController | null = null
  let requestVersion = 0
  let disposed = false
  let sessionExpiryNotified = false

  function expireSession() {
    if (auth.authorizationHeader !== null)
      auth.expireSession()
    if (!sessionExpiryNotified) {
      sessionExpiryNotified = true
      onSessionExpired()
    }
  }

  function handleFailure(error: unknown, clearOnForbidden = true) {
    if (disposed || (error instanceof HttpError && error.code === 'aborted'))
      return
    if (error instanceof HttpError && error.status === 401)
      expireSession()
    if (error instanceof HttpError && error.status === 403) {
      if (clearOnForbidden) {
        mutes.value = Object.freeze([])
        nextCursor.value = null
      }
      state.value = 'forbidden'
      return
    }
    state.value = mutes.value.length === 0 ? 'failed' : 'stale'
  }

  async function run(targetPage: number, cursor: ChatMuteCursor): Promise<void> {
    if (disposed)
      return
    listController?.abort()
    const version = ++requestVersion
    const controller = new AbortController()
    listController = controller
    if (mutes.value.length === 0)
      state.value = 'loading'
    const authorizationHeader = auth.authorizationHeader
    if (authorizationHeader === null) {
      handleFailure(new HttpError('http', 'Authentication required', { status: 401 }))
      listController = null
      return
    }
    try {
      const page = await requestPage(authorizationHeader, cursor, pageSize, controller.signal)
      if (disposed || version !== requestVersion)
        return
      mutes.value = page.mutes
      nextCursor.value = page.nextCursor
      pageNumber.value = targetPage
      state.value = 'ready'
      sessionExpiryNotified = false
    }
    catch (error) {
      if (version === requestVersion)
        handleFailure(error)
    }
    finally {
      if (version === requestVersion)
        listController = null
    }
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
    return run(pageNumber.value, cursorStack[pageNumber.value - 1] ?? emptyCursor)
  }

  async function mutate(operation: (authorizationHeader: string, signal: AbortSignal) => Promise<unknown>): Promise<boolean> {
    if (disposed || isMutating.value)
      return false
    const authorizationHeader = auth.authorizationHeader
    if (authorizationHeader === null) {
      handleFailure(new HttpError('http', 'Authentication required', { status: 401 }))
      return false
    }
    isMutating.value = true
    const controller = new AbortController()
    mutationController = controller
    try {
      await operation(authorizationHeader, controller.signal)
      if (disposed)
        return false
      cursorStack = [emptyCursor]
      pageNumber.value = 1
      await run(1, emptyCursor)
      return true
    }
    catch (error) {
      handleFailure(error)
      return false
    }
    finally {
      if (mutationController === controller)
        mutationController = null
      isMutating.value = false
    }
  }

  function create(input: CreateChatMuteInput) {
    return mutate((authorizationHeader, signal) => createRecord(authorizationHeader, input, signal))
  }

  function update(crossplatformId: string, input: ChatMuteWriteInput) {
    return mutate((authorizationHeader, signal) => updateRecord(authorizationHeader, crossplatformId, input, signal))
  }

  function release(crossplatformId: string, correlationId: string | null) {
    return mutate((authorizationHeader, signal) => releaseRecord(authorizationHeader, crossplatformId, correlationId, signal))
  }

  function retry() {
    return refresh()
  }

  function dispose() {
    if (disposed)
      return
    disposed = true
    requestVersion++
    listController?.abort()
    mutationController?.abort()
    listController = null
    mutationController = null
    isMutating.value = false
  }

  onMounted(() => void refresh())
  onUnmounted(dispose)

  return {
    state: readonly(state),
    mutes: readonly(mutes),
    nextCursor: readonly(nextCursor),
    pageNumber: readonly(pageNumber),
    isMutating: readonly(isMutating),
    create,
    update,
    release,
    goToPage,
    refresh,
    retry,
    dispose,
  }
}
