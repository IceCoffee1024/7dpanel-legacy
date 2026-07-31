import type { DeepReadonly, ShallowRef } from 'vue'
import type { UndoWorldChangeSetPreflight } from '../api/worldTools'

import { onUnmounted, readonly, shallowRef } from 'vue'
import { HttpError } from '../../../shared/api/http'
import { useAuthStore } from '../../auth'
import { fetchUndoWorldChangeSetPreflight } from '../api/worldTools'

export type UndoPreflightPhase = 'idle' | 'loading' | 'ready' | 'failed'
export type UndoPreflightErrorCode
  = | 'session-expired'
    | 'forbidden'
    | 'not-found'
    | 'conflict'
    | 'invalid-response'
    | 'unavailable'

export interface UndoPreflightController {
  phase: DeepReadonly<ShallowRef<UndoPreflightPhase>>
  data: DeepReadonly<ShallowRef<UndoWorldChangeSetPreflight | null>>
  errorCode: DeepReadonly<ShallowRef<UndoPreflightErrorCode | null>>
  load: (sourceOperationId: string) => Promise<UndoWorldChangeSetPreflight | null>
  clear: () => void
  dispose: () => void
}

export interface UseUndoPreflightOptions {
  auth?: { authorizationHeader: string | null, expireSession: () => void }
  fetchPreflight?: typeof fetchUndoWorldChangeSetPreflight
  onSessionExpired?: () => void
}

function toErrorCode(cause: unknown): UndoPreflightErrorCode {
  if (!(cause instanceof HttpError))
    return cause instanceof Error ? 'invalid-response' : 'unavailable'
  if (cause.status === 401)
    return 'session-expired'
  if (cause.status === 403)
    return 'forbidden'
  if (cause.status === 404)
    return 'not-found'
  if (cause.status === 409)
    return 'conflict'
  if (cause.code === 'invalid')
    return 'invalid-response'
  return 'unavailable'
}

function isAbortError(cause: unknown): boolean {
  return (cause instanceof HttpError && cause.code === 'aborted')
    || (cause instanceof Error && cause.name === 'AbortError')
}

export function useUndoPreflight(options: UseUndoPreflightOptions = {}): UndoPreflightController {
  const auth = options.auth ?? useAuthStore()
  const fetchPreflight = options.fetchPreflight ?? fetchUndoWorldChangeSetPreflight
  const onSessionExpired = options.onSessionExpired ?? (() => {})
  const phase = shallowRef<UndoPreflightPhase>('idle')
  const data = shallowRef<UndoWorldChangeSetPreflight | null>(null)
  const errorCode = shallowRef<UndoPreflightErrorCode | null>(null)
  let controller: AbortController | null = null
  let generation = 0
  let disposed = false
  let sessionExpiryNotified = false

  function stopActive() {
    generation++
    controller?.abort()
    controller = null
  }

  function fail(code: UndoPreflightErrorCode) {
    data.value = null
    errorCode.value = code
    phase.value = 'failed'
    if (code === 'session-expired') {
      auth.expireSession()
      if (!sessionExpiryNotified) {
        sessionExpiryNotified = true
        onSessionExpired()
      }
    }
  }

  async function load(sourceOperationId: string): Promise<UndoWorldChangeSetPreflight | null> {
    if (disposed)
      return null
    const normalizedId = sourceOperationId.trim()
    if (normalizedId === '') {
      clear()
      return null
    }
    stopActive()
    const currentGeneration = generation
    const header = auth.authorizationHeader
    data.value = null
    errorCode.value = null
    phase.value = 'loading'
    if (header === null) {
      fail('session-expired')
      return null
    }

    const currentController = new AbortController()
    controller = currentController
    try {
      const result = await fetchPreflight(header, normalizedId, currentController.signal)
      if (disposed || generation !== currentGeneration)
        return null
      data.value = result
      errorCode.value = null
      phase.value = 'ready'
      sessionExpiryNotified = false
      return result
    }
    catch (cause) {
      if (disposed || generation !== currentGeneration || isAbortError(cause))
        return null
      fail(toErrorCode(cause))
      return null
    }
    finally {
      if (controller === currentController)
        controller = null
    }
  }

  function clear() {
    if (disposed)
      return
    stopActive()
    data.value = null
    errorCode.value = null
    phase.value = 'idle'
  }

  function dispose() {
    if (disposed)
      return
    disposed = true
    stopActive()
  }

  onUnmounted(dispose)

  return {
    phase: readonly(phase),
    data: readonly(data),
    errorCode: readonly(errorCode),
    load,
    clear,
    dispose,
  }
}
