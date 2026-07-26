import type { DeepReadonly, ShallowRef } from 'vue'
import type { RestartServerAccepted } from '../api/serverOperations'

import { useMutation, useQueryCache } from '@pinia/colada'
import { onUnmounted, readonly, shallowRef } from 'vue'

import {
  overviewGetQueryKey,
  serverOperationsRestartMutation,
} from '../../../shared/api/generated/@pinia/colada.gen'
import { HttpError } from '../../../shared/api/http'
import { useAuthStore } from '../../auth'
import { parseRestartAccepted } from '../api/serverOperations'

type GeneratedRestartDefinition = ReturnType<typeof serverOperationsRestartMutation>
type GeneratedRestartVariables = Parameters<GeneratedRestartDefinition['mutation']>[0]

export type RestartServerState = 'idle' | 'confirming' | 'submitting' | 'accepted' | 'failed'
export type RestartServerErrorCode
  = | 'session_expired'
    | 'forbidden'
    | 'confirmation_required'
    | 'operation_in_progress'
    | 'audit_unavailable'
    | 'restart_script_not_configured'
    | 'restart_script_missing'
    | 'restart_script_platform_unsupported'
    | 'restart_script_start_failed'
    | 'operation_cancelled'
    | 'unknown'

export interface RestartServerError {
  code: RestartServerErrorCode
}

export interface RestartServerController {
  state: DeepReadonly<ShallowRef<RestartServerState>>
  result: DeepReadonly<ShallowRef<RestartServerAccepted | null>>
  error: DeepReadonly<ShallowRef<RestartServerError | null>>
  startConfirmation: () => void
  cancelConfirmation: () => void
  confirm: () => Promise<RestartServerAccepted | null>
  dispose: () => void
}

export interface UseRestartServerOptions {
  auth?: {
    authorizationHeader: string | null
    expireSession: () => void
  }
  restartServer?: (
    authorizationHeader: string,
    signal?: AbortSignal,
  ) => Promise<RestartServerAccepted>
  onSessionExpired?: () => void
}

const restartProblemCodes = new Set<RestartServerErrorCode>([
  'confirmation_required',
  'operation_in_progress',
  'audit_unavailable',
  'restart_script_not_configured',
  'restart_script_missing',
  'restart_script_platform_unsupported',
  'restart_script_start_failed',
  'operation_cancelled',
])

function errorCode(cause: unknown): RestartServerErrorCode {
  if (!(cause instanceof HttpError))
    return 'unknown'
  if (cause.status === 403)
    return 'forbidden'
  if (cause.problemCode !== undefined && restartProblemCodes.has(cause.problemCode as RestartServerErrorCode))
    return cause.problemCode as RestartServerErrorCode
  return 'unknown'
}

function isAbortError(cause: unknown): boolean {
  return (cause instanceof HttpError && cause.code === 'aborted')
    || (cause instanceof Error && cause.name === 'AbortError')
}

export function useRestartServer(options: UseRestartServerOptions = {}): RestartServerController {
  const auth = options.auth ?? useAuthStore()
  const generatedDefinition = options.restartServer === undefined
    ? serverOperationsRestartMutation()
    : null
  const generatedQueryCache = generatedDefinition === null ? null : useQueryCache()
  const generatedMutation = generatedDefinition === null
    ? null
    : useMutation<RestartServerAccepted, GeneratedRestartVariables, HttpError>({
        mutation: async (variables, context) => parseRestartAccepted(
          await generatedDefinition.mutation(variables, context),
        ),
        onSuccess: async () => {
          await generatedQueryCache!.invalidateQueries({
            exact: true,
            key: overviewGetQueryKey(),
          })
        },
      })
  const requestRestart = options.restartServer ?? (async (_authorizationHeader, signal) => {
    return await generatedMutation!.mutateAsync({
      body: { confirmed: true },
      signal,
    })
  })
  const onSessionExpired = options.onSessionExpired ?? (() => {})
  const state = shallowRef<RestartServerState>('idle')
  const result = shallowRef<RestartServerAccepted | null>(null)
  const error = shallowRef<RestartServerError | null>(null)
  let inFlight: Promise<RestartServerAccepted | null> | null = null
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

  function confirm(): Promise<RestartServerAccepted | null> {
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

    const promise = requestRestart(authorizationHeader, currentController.signal)
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
          if (auth.authorizationHeader !== null)
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
