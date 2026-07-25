import type { DeepReadonly, ShallowRef } from 'vue'
import type { ShutdownServerAccepted } from '../api/serverOperations'

import { onUnmounted, readonly, shallowRef } from 'vue'

import { HttpError } from '../../../shared/api/http'
import { useAuthStore } from '../../auth'
import { shutdownServer } from '../api/serverOperations'

export type ShutdownServerState = 'idle' | 'confirming' | 'submitting' | 'accepted' | 'failed'
export type ShutdownServerErrorCode
  = | 'session_expired'
    | 'forbidden'
    | 'confirmation_required'
    | 'operation_in_progress'
    | 'audit_unavailable'
    | 'shutdown_unavailable'
    | 'shutdown_timeout'
    | 'shutdown_cancelled'
    | 'shutdown_failed'
    | 'unknown'

export interface ShutdownServerError {
  code: ShutdownServerErrorCode
}

export interface ShutdownServerController {
  state: DeepReadonly<ShallowRef<ShutdownServerState>>
  result: DeepReadonly<ShallowRef<ShutdownServerAccepted | null>>
  error: DeepReadonly<ShallowRef<ShutdownServerError | null>>
  startConfirmation: () => void
  cancelConfirmation: () => void
  confirm: () => Promise<ShutdownServerAccepted | null>
  dispose: () => void
}

export interface UseShutdownServerOptions {
  auth?: {
    authorizationHeader: string | null
    expireSession: () => void
  }
  shutdownServer?: (
    authorizationHeader: string,
    signal?: AbortSignal,
  ) => Promise<ShutdownServerAccepted>
  onSessionExpired?: () => void
}

const shutdownProblemCodes = new Set<ShutdownServerErrorCode>([
  'confirmation_required',
  'operation_in_progress',
  'audit_unavailable',
  'shutdown_unavailable',
  'shutdown_timeout',
  'shutdown_cancelled',
  'shutdown_failed',
])

function errorCode(cause: unknown): ShutdownServerErrorCode {
  if (!(cause instanceof HttpError))
    return 'unknown'
  if (cause.status === 403)
    return 'forbidden'
  if (cause.problemCode !== undefined && shutdownProblemCodes.has(cause.problemCode as ShutdownServerErrorCode))
    return cause.problemCode as ShutdownServerErrorCode
  return 'unknown'
}

function isAbortError(cause: unknown): boolean {
  return (cause instanceof HttpError && cause.code === 'aborted')
    || (cause instanceof Error && cause.name === 'AbortError')
}

export function useShutdownServer(options: UseShutdownServerOptions = {}): ShutdownServerController {
  const auth = options.auth ?? useAuthStore()
  const requestShutdown = options.shutdownServer ?? shutdownServer
  const onSessionExpired = options.onSessionExpired ?? (() => {})
  const state = shallowRef<ShutdownServerState>('idle')
  const result = shallowRef<ShutdownServerAccepted | null>(null)
  const error = shallowRef<ShutdownServerError | null>(null)
  let inFlight: Promise<ShutdownServerAccepted | null> | null = null
  let controller: AbortController | null = null
  let disposed = false
  let sessionExpiryNotified = false

  function startConfirmation() {
    if (disposed || state.value === 'submitting')
      return
    state.value = 'confirming'
    error.value = null
  }

  function cancelConfirmation() {
    if (disposed || state.value !== 'confirming')
      return
    state.value = 'idle'
    error.value = null
  }

  function confirm(): Promise<ShutdownServerAccepted | null> {
    if (inFlight !== null)
      return inFlight
    if (disposed || state.value !== 'confirming')
      return Promise.resolve(null)
    if (auth.authorizationHeader === null) {
      state.value = 'failed'
      error.value = Object.freeze({ code: 'session_expired' })
      if (!sessionExpiryNotified) {
        sessionExpiryNotified = true
        onSessionExpired()
      }
      return Promise.resolve(null)
    }

    const authorizationHeader = auth.authorizationHeader
    const currentController = new AbortController()
    controller = currentController
    state.value = 'submitting'
    error.value = null
    result.value = null

    const promise = requestShutdown(authorizationHeader, currentController.signal)
      .then((accepted) => {
        if (disposed)
          return null
        result.value = accepted
        state.value = 'accepted'
        sessionExpiryNotified = false
        return accepted
      })
      .catch((cause: unknown) => {
        if (disposed || isAbortError(cause))
          return null
        if (cause instanceof HttpError && cause.status === 401) {
          auth.expireSession()
          state.value = 'failed'
          error.value = Object.freeze({ code: 'session_expired' })
          if (!sessionExpiryNotified) {
            sessionExpiryNotified = true
            onSessionExpired()
          }
          return null
        }
        state.value = 'failed'
        error.value = Object.freeze({ code: errorCode(cause) })
        return null
      })
      .finally(() => {
        if (controller === currentController)
          controller = null
        if (inFlight === promise)
          inFlight = null
      })
    inFlight = promise
    return promise
  }

  function dispose() {
    if (disposed)
      return
    disposed = true
    controller?.abort()
    controller = null
  }

  onUnmounted(dispose)

  return {
    state: readonly(state),
    result: readonly(result),
    error: readonly(error),
    startConfirmation,
    cancelConfirmation,
    confirm,
    dispose,
  }
}
