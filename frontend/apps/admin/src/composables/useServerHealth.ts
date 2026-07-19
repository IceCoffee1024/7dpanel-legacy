import type { ServerHealth } from '../api/serverHealth'
import { onMounted, onUnmounted, readonly, shallowRef } from 'vue'
import { fetchServerHealth, ServerHealthError } from '../api/serverHealth'

export type ServerHealthState = 'loading' | 'fresh' | 'stale' | 'offline'

const DEFAULT_STALE_AFTER_MS = 60_000

interface UseServerHealthOptions {
  staleAfterMs?: number
}

export function useServerHealth(options: UseServerHealthOptions = {}) {
  const staleAfterMs = options.staleAfterMs ?? DEFAULT_STALE_AFTER_MS
  const state = shallowRef<ServerHealthState>('loading')
  const data = shallowRef<ServerHealth | null>(null)
  const error = shallowRef<ServerHealthError | null>(null)
  const lastSuccessfulAt = shallowRef<number | null>(null)
  let activeController: AbortController | null = null
  let staleTimer: ReturnType<typeof setTimeout> | null = null
  let disposed = false

  function clearStaleTimer() {
    if (staleTimer !== null) {
      clearTimeout(staleTimer)
      staleTimer = null
    }
  }

  function scheduleStaleState(sampleTime: number) {
    clearStaleTimer()
    staleTimer = setTimeout(() => {
      if (!disposed && lastSuccessfulAt.value === sampleTime && data.value !== null) {
        state.value = 'stale'
      }
    }, staleAfterMs)
  }

  async function refresh() {
    activeController?.abort()
    const controller = new AbortController()
    activeController = controller
    error.value = null

    if (data.value === null) {
      state.value = 'loading'
    }

    try {
      const result = await fetchServerHealth(controller.signal)
      if (disposed || controller.signal.aborted) {
        return
      }

      data.value = result
      lastSuccessfulAt.value = Date.now()
      state.value = 'fresh'
      scheduleStaleState(lastSuccessfulAt.value)
    }
    catch (caughtError) {
      if (disposed || controller.signal.aborted) {
        return
      }

      error.value = caughtError instanceof ServerHealthError
        ? caughtError
        : new ServerHealthError('network', 'Health request failed.')
      state.value = data.value === null ? 'offline' : 'stale'
    }
    finally {
      if (activeController === controller) {
        activeController = null
      }
    }
  }

  function dispose() {
    disposed = true
    activeController?.abort()
    activeController = null
    clearStaleTimer()
  }

  onMounted(() => {
    void refresh()
  })
  onUnmounted(dispose)

  return {
    state: readonly(state),
    data: readonly(data),
    error: readonly(error),
    lastSuccessfulAt: readonly(lastSuccessfulAt),
    refresh,
    dispose,
  }
}
