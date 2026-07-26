import type { DeepReadonly, ShallowRef } from 'vue'
import type { ServerEventNotification, ServerEventsConnectionStatus } from '../../../app/serverEvents'
import type { LoadRecentChatMessages } from '../api/chat'
import type { ChatChannelFilter, ChatMessage } from './chatMessage'

import { onMounted, onUnmounted, readonly, shallowRef } from 'vue'

import { subscribeServerEvents, subscribeServerEventsStatus } from '../../../app/serverEvents'
import { parseChatMessage } from '../api/chat'

export interface LiveChatController {
  snapshotLoading: DeepReadonly<ShallowRef<boolean>>
  connectionStatus: DeepReadonly<ShallowRef<ServerEventsConnectionStatus>>
  hasGap: DeepReadonly<ShallowRef<boolean>>
  messages: DeepReadonly<ShallowRef<readonly ChatMessage[]>>
  channelFilter: DeepReadonly<ShallowRef<ChatChannelFilter>>
  unreadCount: DeepReadonly<ShallowRef<number>>
  setChannelFilter: (filter: ChatChannelFilter) => void
  setFollowingLatest: (following: boolean) => void
}

export interface UseLiveChatOptions {
  loadRecent: LoadRecentChatMessages
  subscribeEvents?: (listener: (event: ServerEventNotification) => void) => () => void
  subscribeStatus?: (listener: (status: ServerEventsConnectionStatus) => void) => () => void
}

const snapshotLimit = 200
const messageCapacity = 1_000

export function useLiveChat(options: UseLiveChatOptions): LiveChatController {
  const snapshotLoading = shallowRef(true)
  const connectionStatus = shallowRef<ServerEventsConnectionStatus>('stopped')
  const hasGap = shallowRef(false)
  const messages = shallowRef<readonly ChatMessage[]>(Object.freeze([]))
  const channelFilter = shallowRef<ChatChannelFilter>('All')
  const unreadCount = shallowRef(0)
  let followingLatest = true
  let snapshotSettled = false
  let disposed = false
  let refreshInFlight = false
  let requestController: AbortController | null = null
  let unsubscribeEvents: (() => void) | null = null
  let unsubscribeStatus: (() => void) | null = null

  function insertMessage(message: ChatMessage): boolean {
    const current = messages.value
    let low = 0
    let high = current.length
    while (low < high) {
      const middle = Math.floor((low + high) / 2)
      if (current[middle]!.sequence < message.sequence)
        low = middle + 1
      else
        high = middle
    }
    if (current[low]?.sequence === message.sequence)
      return false

    const next = [...current]
    next.splice(low, 0, message)
    const retained = next.length <= messageCapacity || low > 0
    messages.value = Object.freeze(
      next.length > messageCapacity ? next.slice(next.length - messageCapacity) : next,
    )
    return retained
  }

  async function loadSnapshot(signal: AbortSignal, initial: boolean): Promise<void> {
    try {
      const snapshot = await options.loadRecent(snapshotLimit, signal)
      if (disposed || signal.aborted)
        return
      for (const item of snapshot)
        insertMessage(parseChatMessage(item))
    }
    catch {
      // The live stream remains useful when recent context is unavailable or invalid.
    }
    finally {
      if (!disposed && !signal.aborted) {
        refreshInFlight = false
        if (initial) {
          snapshotSettled = true
          snapshotLoading.value = false
        }
      }
    }
  }

  function refreshAfterGap(): void {
    if (disposed || refreshInFlight)
      return
    refreshInFlight = true
    const controller = new AbortController()
    requestController?.abort()
    requestController = controller
    void loadSnapshot(controller.signal, false)
  }

  function handleEvent(event: ServerEventNotification): void {
    if (event.type === 'gap') {
      hasGap.value = true
      if (snapshotSettled)
        refreshAfterGap()
      return
    }
    if (event.type !== 'chat-message')
      return
    try {
      const inserted = insertMessage(parseChatMessage(event.data))
      if (inserted && snapshotSettled && !followingLatest)
        unreadCount.value++
    }
    catch {
      // Invalid SSE payloads never become visible chat messages.
    }
  }

  function setChannelFilter(filter: ChatChannelFilter): void {
    channelFilter.value = filter
  }

  function setFollowingLatest(following: boolean): void {
    followingLatest = following
    if (following)
      unreadCount.value = 0
  }

  onMounted(() => {
    if (disposed)
      return
    unsubscribeEvents = (options.subscribeEvents ?? subscribeServerEvents)(handleEvent)
    unsubscribeStatus = (options.subscribeStatus ?? subscribeServerEventsStatus)((status) => {
      connectionStatus.value = status
    })
    const controller = new AbortController()
    requestController = controller
    refreshInFlight = true
    void loadSnapshot(controller.signal, true)
  })

  onUnmounted(() => {
    disposed = true
    requestController?.abort()
    requestController = null
    unsubscribeEvents?.()
    unsubscribeEvents = null
    unsubscribeStatus?.()
    unsubscribeStatus = null
  })

  return {
    snapshotLoading: readonly(snapshotLoading),
    connectionStatus: readonly(connectionStatus),
    hasGap: readonly(hasGap),
    messages: readonly(messages),
    channelFilter: readonly(channelFilter),
    unreadCount: readonly(unreadCount),
    setChannelFilter,
    setFollowingLatest,
  }
}
