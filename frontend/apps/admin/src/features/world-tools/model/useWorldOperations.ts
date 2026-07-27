import type { ComputedRef, DeepReadonly, ShallowRef } from 'vue'
import type { AuthRole } from '../../auth/model/authSession'
import type {
  WorldOperationReceipt,
  WorldOperationRecord,
  WorldOperationSubmission,
} from '../api/worldTools'

import { computed, onUnmounted, readonly, shallowRef } from 'vue'
import { HttpError } from '../../../shared/api/http'
import { useAuthStore } from '../../auth'
import {
  fetchWorldOperation as requestWorldOperation,
  submitWorldOperation as requestSubmitWorldOperation,
} from '../api/worldTools'

export type WorldOperationsState = 'idle' | 'submitting' | 'polling' | 'terminal' | 'failed'
export type WorldOperationsErrorCode
  = | 'session-expired'
    | 'forbidden'
    | 'confirmation-required'
    | 'strong-confirmation-required'
    | 'conflict'
    | 'not-found'
    | 'unavailable'
    | 'invalid-response'

export interface WorldOperationsController {
  state: DeepReadonly<ShallowRef<WorldOperationsState>>
  receipt: DeepReadonly<ShallowRef<WorldOperationReceipt | null>>
  operation: DeepReadonly<ShallowRef<WorldOperationRecord | null>>
  errorCode: DeepReadonly<ShallowRef<WorldOperationsErrorCode | null>>
  canMutate: Readonly<ComputedRef<boolean>>
  submit: (submission: WorldOperationSubmission) => Promise<WorldOperationRecord | null>
  resume: (operationId: string) => Promise<WorldOperationRecord | null>
  clear: () => void
  dispose: () => void
}

export interface UseWorldOperationsOptions {
  auth?: { authorizationHeader: string | null, role: AuthRole | null, expireSession: () => void }
  submitOperation?: typeof requestSubmitWorldOperation
  fetchOperation?: typeof requestWorldOperation
  replaceOperationId?: (operationId: string | null) => void
  onSessionExpired?: () => void
  pollIntervalMs?: number
}

const terminalStatuses = new Set<WorldOperationRecord['status']>([
  'Succeeded', 'Failed', 'Cancelled', 'Interrupted', 'ResultUnknown', 'RollbackFailed',
])

function isAbortError(cause: unknown): boolean {
  return (cause instanceof HttpError && cause.code === 'aborted')
    || (cause instanceof Error && cause.name === 'AbortError')
}

function operationErrorCode(cause: unknown): WorldOperationsErrorCode {
  if (!(cause instanceof HttpError))
    return 'unavailable'
  if (cause.status === 401)
    return 'session-expired'
  if (cause.status === 403)
    return 'forbidden'
  if (cause.status === 404)
    return 'not-found'
  if (cause.status === 409)
    return 'conflict'
  if (cause.problemCode === 'confirmation_required')
    return 'confirmation-required'
  if (cause.problemCode === 'strong_confirmation_required')
    return 'strong-confirmation-required'
  if (cause.code === 'invalid')
    return 'invalid-response'
  return 'unavailable'
}

export function useWorldOperations(options: UseWorldOperationsOptions = {}): WorldOperationsController {
  const auth = options.auth ?? useAuthStore()
  const submitOperation = options.submitOperation ?? requestSubmitWorldOperation
  const fetchOperation = options.fetchOperation ?? requestWorldOperation
  const replaceOperationId = options.replaceOperationId ?? (() => {})
  const onSessionExpired = options.onSessionExpired ?? (() => {})
  const pollIntervalMs = options.pollIntervalMs ?? 1_500
  const state = shallowRef<WorldOperationsState>('idle')
  const receipt = shallowRef<WorldOperationReceipt | null>(null)
  const operation = shallowRef<WorldOperationRecord | null>(null)
  const errorCode = shallowRef<WorldOperationsErrorCode | null>(null)
  const canMutate = computed(() => auth.role === 'Owner')
  let controller: AbortController | null = null
  let pollTimer: ReturnType<typeof setTimeout> | null = null
  let generation = 0
  let disposed = false
  let sessionExpiryNotified = false

  function stopActive() {
    generation++
    if (pollTimer !== null) {
      clearTimeout(pollTimer)
      pollTimer = null
    }
    controller?.abort()
    controller = null
  }

  function notifySessionExpired() {
    auth.expireSession()
    if (!sessionExpiryNotified) {
      sessionExpiryNotified = true
      onSessionExpired()
    }
  }

  function fail(cause: unknown) {
    const code = operationErrorCode(cause)
    errorCode.value = code
    state.value = 'failed'
    if (code === 'session-expired')
      notifySessionExpired()
  }

  function schedulePoll(operationId: string, currentGeneration: number) {
    if (disposed || generation !== currentGeneration)
      return
    pollTimer = setTimeout(() => {
      pollTimer = null
      void poll(operationId, currentGeneration)
    }, pollIntervalMs)
  }

  async function poll(operationId: string, currentGeneration: number): Promise<WorldOperationRecord | null> {
    if (disposed || generation !== currentGeneration)
      return null
    const header = auth.authorizationHeader
    if (header === null) {
      fail(new HttpError('http', 'Authentication required', { status: 401 }))
      return null
    }
    const currentController = new AbortController()
    controller = currentController
    try {
      const next = await fetchOperation(header, operationId, currentController.signal)
      if (disposed || generation !== currentGeneration)
        return null
      operation.value = next
      errorCode.value = null
      sessionExpiryNotified = false
      if (terminalStatuses.has(next.status))
        state.value = 'terminal'
      else {
        state.value = 'polling'
        schedulePoll(operationId, currentGeneration)
      }
      return next
    }
    catch (cause) {
      if (disposed || generation !== currentGeneration || isAbortError(cause))
        return null
      fail(cause)
      return null
    }
    finally {
      if (controller === currentController)
        controller = null
    }
  }

  async function submit(submission: WorldOperationSubmission): Promise<WorldOperationRecord | null> {
    if (disposed || !canMutate.value)
      return null
    const header = auth.authorizationHeader
    if (header === null) {
      fail(new HttpError('http', 'Authentication required', { status: 401 }))
      return null
    }
    stopActive()
    const currentGeneration = generation
    const currentController = new AbortController()
    controller = currentController
    state.value = 'submitting'
    receipt.value = null
    operation.value = null
    errorCode.value = null
    try {
      const accepted = await submitOperation(header, submission, currentController.signal)
      if (disposed || generation !== currentGeneration)
        return null
      receipt.value = Object.freeze({ ...accepted })
      replaceOperationId(accepted.operationId)
      state.value = 'polling'
      controller = null
      return await poll(accepted.operationId, currentGeneration)
    }
    catch (cause) {
      if (disposed || generation !== currentGeneration || isAbortError(cause))
        return null
      fail(cause)
      return null
    }
    finally {
      if (controller === currentController)
        controller = null
    }
  }

  async function resume(operationId: string): Promise<WorldOperationRecord | null> {
    if (disposed || operationId.trim() === '')
      return null
    stopActive()
    const currentGeneration = generation
    receipt.value = null
    operation.value = null
    errorCode.value = null
    state.value = 'polling'
    return await poll(operationId, currentGeneration)
  }

  function clear() {
    if (disposed)
      return
    stopActive()
    receipt.value = null
    operation.value = null
    errorCode.value = null
    state.value = 'idle'
    replaceOperationId(null)
  }

  function dispose() {
    if (disposed)
      return
    disposed = true
    stopActive()
  }

  onUnmounted(dispose)

  return {
    state: readonly(state),
    receipt: readonly(receipt),
    operation: readonly(operation),
    errorCode: readonly(errorCode),
    canMutate,
    submit,
    resume,
    clear,
    dispose,
  }
}
