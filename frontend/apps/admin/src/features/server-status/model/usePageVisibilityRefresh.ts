import type { DeepReadonly, ShallowRef } from 'vue'

import { useDocumentVisibility, useIntervalFn } from '@vueuse/core'
import { onMounted, onUnmounted, readonly, watch } from 'vue'

export interface PageVisibilityRefreshController {
  visibility: DeepReadonly<ShallowRef<DocumentVisibilityState>>
  resetPeriod: () => void
  dispose: () => void
}

export interface UsePageVisibilityRefreshOptions {
  intervalMs?: number
}

export function usePageVisibilityRefresh(
  refresh: () => void | Promise<void>,
  options: UsePageVisibilityRefreshOptions = {},
): PageVisibilityRefreshController {
  const visibility = useDocumentVisibility()
  const intervalMs = options.intervalMs ?? 3_000
  const { pause, resume } = useIntervalFn(
    () => void refresh(),
    intervalMs,
    { immediate: false },
  )
  let disposed = false

  function resetPeriod() {
    pause()
    if (!disposed && visibility.value === 'visible')
      resume()
  }

  const stopVisibilityWatch = watch(visibility, (current, previous) => {
    if (current === 'hidden') {
      pause()
      return
    }
    if (current === 'visible' && previous !== 'visible') {
      resetPeriod()
      void refresh()
    }
  }, { flush: 'sync' })

  function dispose() {
    if (disposed)
      return
    disposed = true
    stopVisibilityWatch()
    pause()
  }

  onMounted(() => {
    if (visibility.value === 'visible')
      resume()
  })
  onUnmounted(dispose)

  return {
    visibility: readonly(visibility),
    resetPeriod,
    dispose,
  }
}
