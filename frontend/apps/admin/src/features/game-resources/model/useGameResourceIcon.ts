import type { DeepReadonly, MaybeRefOrGetter, ShallowRef } from 'vue'
import type { GameResourceIconStatus } from '../api/gameResources'

import { onMounted, onUnmounted, readonly, shallowRef, toValue, watch } from 'vue'

export interface GameResourceIntersectionObserver {
  readonly observe: (target: Element) => void
  readonly disconnect: () => void
}

export type GameResourceIntersectionObserverFactory = (
  onVisible: () => void,
) => GameResourceIntersectionObserver

export interface UseGameResourceIconOptions {
  readonly resourceId: MaybeRefOrGetter<string>
  readonly iconStatus: MaybeRefOrGetter<GameResourceIconStatus>
  readonly authorizationHeader: MaybeRefOrGetter<string | null>
  readonly target: ShallowRef<Element | null>
  readonly fetch?: typeof fetch
  readonly createObjectURL?: (blob: Blob) => string
  readonly revokeObjectURL?: (url: string) => void
  readonly createObserver?: GameResourceIntersectionObserverFactory
}

export interface GameResourceIconController {
  readonly src: DeepReadonly<ShallowRef<string | null>>
  readonly loading: DeepReadonly<ShallowRef<boolean>>
  readonly failed: DeepReadonly<ShallowRef<boolean>>
  readonly retry: () => void
  readonly dispose: () => void
}

export function gameResourceIconUrl(resourceId: string): string {
  return `/api/v1/game-resources/${encodeURIComponent(resourceId)}/icon`
}

function browserObserver(onVisible: () => void): GameResourceIntersectionObserver | null {
  if (typeof IntersectionObserver === 'undefined')
    return null
  const observer = new IntersectionObserver((entries) => {
    if (entries.some(entry => entry.isIntersecting))
      onVisible()
  })
  return observer
}

export function useGameResourceIcon(options: UseGameResourceIconOptions): GameResourceIconController {
  const fetchImpl = options.fetch ?? globalThis.fetch
  const createObjectURL = options.createObjectURL ?? (blob => URL.createObjectURL(blob))
  const revokeObjectURL = options.revokeObjectURL ?? (url => URL.revokeObjectURL(url))
  const src = shallowRef<string | null>(null)
  const loading = shallowRef(false)
  const failed = shallowRef(toValue(options.iconStatus) !== 'available')

  let observer: GameResourceIntersectionObserver | null = null
  let controller: AbortController | null = null
  let generation = 0
  let visible = false
  let mounted = false
  let disposed = false

  function releaseRequest() {
    generation++
    controller?.abort()
    controller = null
    loading.value = false
  }

  function releaseObjectUrl() {
    if (src.value !== null) {
      revokeObjectURL(src.value)
      src.value = null
    }
  }

  function releaseCurrent() {
    releaseRequest()
    releaseObjectUrl()
  }

  async function load() {
    if (disposed || !mounted || !visible || loading.value
      || toValue(options.iconStatus) !== 'available') {
      return
    }
    const authorizationHeader = toValue(options.authorizationHeader)
    const resourceId = toValue(options.resourceId)
    if (authorizationHeader === null || resourceId.trim() === '') {
      failed.value = true
      return
    }

    const currentGeneration = ++generation
    const currentController = new AbortController()
    controller = currentController
    loading.value = true
    failed.value = false
    try {
      const response = await fetchImpl(gameResourceIconUrl(resourceId), {
        credentials: 'omit',
        headers: { Authorization: authorizationHeader },
        signal: currentController.signal,
      })
      if (!response.ok || response.headers.get('Content-Type')?.trim().toLowerCase() !== 'image/png')
        throw new Error('Game resource icon unavailable')
      const blob = await response.blob()
      if (disposed || currentController.signal.aborted || currentGeneration !== generation)
        return
      const objectUrl = createObjectURL(blob)
      if (disposed || currentController.signal.aborted || currentGeneration !== generation) {
        revokeObjectURL(objectUrl)
        return
      }
      releaseObjectUrl()
      src.value = objectUrl
      failed.value = false
    }
    catch {
      if (!disposed && !currentController.signal.aborted && currentGeneration === generation) {
        releaseObjectUrl()
        failed.value = true
      }
    }
    finally {
      if (controller === currentController) {
        controller = null
        loading.value = false
      }
    }
  }

  function markVisible() {
    if (visible)
      return
    visible = true
    observer?.disconnect()
    observer = null
    void load()
  }

  function setupObserver() {
    observer?.disconnect()
    observer = null
    const target = options.target.value
    if (target === null)
      return
    observer = options.createObserver?.(markVisible) ?? browserObserver(markVisible)
    if (observer === null) {
      markVisible()
      return
    }
    observer.observe(target)
  }

  function resetForInput() {
    releaseCurrent()
    failed.value = toValue(options.iconStatus) !== 'available'
      || toValue(options.authorizationHeader) === null
      || toValue(options.resourceId).trim() === ''
    if (visible)
      void load()
  }

  watch(
    [
      () => toValue(options.resourceId),
      () => toValue(options.iconStatus),
      () => toValue(options.authorizationHeader),
    ],
    resetForInput,
  )

  watch(options.target, () => {
    if (mounted && !visible)
      setupObserver()
  })

  function retry() {
    if (disposed)
      return
    releaseCurrent()
    failed.value = false
    void load()
  }

  function dispose() {
    if (disposed)
      return
    disposed = true
    observer?.disconnect()
    observer = null
    releaseCurrent()
  }

  onMounted(() => {
    mounted = true
    setupObserver()
  })
  onUnmounted(dispose)

  return {
    src: readonly(src),
    loading: readonly(loading),
    failed: readonly(failed),
    retry,
    dispose,
  }
}
