import type { DeepReadonly, ShallowRef } from 'vue'
import type {
  ServerEventNotification,
  ServerEventsConnectionStatus,
} from '../../../app/serverEvents'
import type { LoadRecentConsoleLogs } from '../api/consoleLogs'
import type { ConsoleLogEntry } from './consoleLog'

import { onMounted, onUnmounted, readonly, shallowRef } from 'vue'

import {
  subscribeServerEvents,
  subscribeServerEventsStatus,
} from '../../../app/serverEvents'
import { parseConsoleLogEntry } from './consoleLog'

export interface ConsoleLogsController {
  snapshotLoading: DeepReadonly<ShallowRef<boolean>>
  connectionStatus: DeepReadonly<ShallowRef<ServerEventsConnectionStatus>>
  hasGap: DeepReadonly<ShallowRef<boolean>>
  entries: DeepReadonly<ShallowRef<readonly ConsoleLogEntry[]>>
  unreadCount: DeepReadonly<ShallowRef<number>>
  clearEntries: () => void
  setFollowingLatest: (following: boolean) => void
}

export interface UseConsoleLogsOptions {
  loadRecent: LoadRecentConsoleLogs
  subscribeEvents?: (
    listener: (event: ServerEventNotification) => void,
  ) => () => void
  subscribeStatus?: (
    listener: (status: ServerEventsConnectionStatus) => void,
  ) => () => void
}

const snapshotLimit = 1_000
const entryCapacity = 2_000

export function useConsoleLogs(options: UseConsoleLogsOptions): ConsoleLogsController {
  const snapshotLoading = shallowRef(true)
  const connectionStatus = shallowRef<ServerEventsConnectionStatus>('stopped')
  const hasGap = shallowRef(false)
  const entries = shallowRef<readonly ConsoleLogEntry[]>(Object.freeze([]))
  const unreadCount = shallowRef(0)
  let followingLatest = true
  let snapshotSettled = false
  let disposed = false
  let requestController: AbortController | null = null
  let unsubscribeEvents: (() => void) | null = null
  let unsubscribeStatus: (() => void) | null = null

  function insertEntry(entry: ConsoleLogEntry): boolean {
    const current = entries.value
    let low = 0
    let high = current.length
    while (low < high) {
      const middle = Math.floor((low + high) / 2)
      const sequence = current[middle]!.sequence
      if (sequence < entry.sequence)
        low = middle + 1
      else
        high = middle
    }
    if (current[low]?.sequence === entry.sequence)
      return false

    const next = [...current]
    next.splice(low, 0, entry)
    const retained = next.length <= entryCapacity || low > 0
    entries.value = Object.freeze(
      next.length > entryCapacity ? next.slice(next.length - entryCapacity) : next,
    )
    return retained
  }

  function handleEvent(event: ServerEventNotification): void {
    if (event.type === 'gap') {
      hasGap.value = true
      return
    }
    if (event.type !== 'console-log')
      return

    try {
      const inserted = insertEntry(parseConsoleLogEntry(event.data))
      if (inserted && snapshotSettled && !followingLatest)
        unreadCount.value++
    }
    catch {
      // Invalid events cannot become visible console entries.
    }
  }

  async function loadSnapshot(signal: AbortSignal): Promise<void> {
    try {
      const snapshot = await options.loadRecent(snapshotLimit, signal)
      if (disposed || signal.aborted)
        return
      const parsedSnapshot = snapshot.map(parseConsoleLogEntry)
      for (const entry of parsedSnapshot)
        insertEntry(entry)
    }
    catch {
      // Live entries remain useful when the recent snapshot is unavailable or invalid.
    }
    finally {
      if (!disposed && !signal.aborted) {
        snapshotSettled = true
        snapshotLoading.value = false
      }
    }
  }

  function clearEntries(): void {
    if (snapshotLoading.value)
      return
    entries.value = Object.freeze([])
    unreadCount.value = 0
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
    requestController = new AbortController()
    void loadSnapshot(requestController.signal)
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
    entries: readonly(entries),
    unreadCount: readonly(unreadCount),
    clearEntries,
    setFollowingLatest,
  }
}
