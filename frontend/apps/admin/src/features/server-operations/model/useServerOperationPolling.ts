import type { ServerOperationKind, ServerOperationStatusRecord } from '../api/serverOperations'

import { onUnmounted } from 'vue'

import { HttpError } from '../../../shared/api/http'

export interface UseServerOperationPollingOptions {
  kind: ServerOperationKind
  authorizationHeader: () => string | null
  getOperation: (authorizationHeader: string, operationId: string, signal?: AbortSignal) => Promise<ServerOperationStatusRecord>
  onOperation: (operation: ServerOperationStatusRecord) => void
  onUnauthorized: () => void
  onForbidden: () => void
  onTransientFailure: () => void
  intervalMs?: number
}

export interface ServerOperationPollingController {
  resume: (operationId: string | null) => void
  dispose: () => void
}

function isAbortError(cause: unknown): boolean {
  return (cause instanceof HttpError && cause.code === 'aborted')
    || (cause instanceof Error && cause.name === 'AbortError')
}

export function useServerOperationPolling(options: UseServerOperationPollingOptions): ServerOperationPollingController {
  const intervalMs = options.intervalMs ?? 1000
  let operationId: string | null = null
  let request: AbortController | null = null
  let timer: ReturnType<typeof setTimeout> | null = null
  let generation = 0
  let disposed = false

  function isTerminal(operation: ServerOperationStatusRecord): boolean {
    return operation.status === 'succeeded' || operation.status === 'failed'
      || operation.status === 'cancelled' || operation.status === 'result-unknown'
  }

  function clearPendingRequest() {
    request?.abort()
    request = null
    if (timer !== null) {
      clearTimeout(timer)
      timer = null
    }
  }

  function schedule(id: string, expectedGeneration: number) {
    if (disposed || expectedGeneration !== generation)
      return
    timer = setTimeout(() => {
      timer = null
      void poll(id, expectedGeneration)
    }, intervalMs)
  }

  async function poll(id: string, expectedGeneration: number): Promise<void> {
    if (disposed || expectedGeneration !== generation || operationId !== id)
      return
    const authorizationHeader = options.authorizationHeader()
    if (authorizationHeader === null)
      return
    const controller = new AbortController()
    request = controller
    try {
      const operation = await options.getOperation(authorizationHeader, id, controller.signal)
      if (disposed || expectedGeneration !== generation || operationId !== id || operation.kind !== options.kind)
        return
      options.onOperation(operation)
      if (!isTerminal(operation))
        schedule(id, expectedGeneration)
    }
    catch (cause: unknown) {
      if (disposed || expectedGeneration !== generation || isAbortError(cause))
        return
      if (cause instanceof HttpError && cause.status === 401) {
        options.onUnauthorized()
      }
      else if (cause instanceof HttpError && cause.status === 403) {
        options.onForbidden()
      }
      else {
        options.onTransientFailure()
        schedule(id, expectedGeneration)
      }
    }
    finally {
      if (request === controller)
        request = null
    }
  }

  function resume(nextOperationId: string | null) {
    if (disposed)
      return
    operationId = nextOperationId
    generation++
    clearPendingRequest()
    if (nextOperationId !== null)
      void poll(nextOperationId, generation)
  }

  function dispose() {
    if (disposed)
      return
    disposed = true
    generation++
    clearPendingRequest()
  }

  onUnmounted(dispose)
  return { resume, dispose }
}
