import type { DeepReadonly, ShallowRef } from 'vue'
import type { RouteLocationNormalizedLoaded, Router } from 'vue-router'
import type { RestartServerAccepted, ServerOperationStatusRecord } from '../api/serverOperations'

import { useMutation, useQueryCache } from '@pinia/colada'
import { onUnmounted, readonly, shallowRef, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import {
  overviewGetQueryKey,
  serverOperationsRestartMutation,
} from '../../../shared/api/generated/@pinia/colada.gen'
import { HttpError } from '../../../shared/api/http'
import { useAuthStore } from '../../auth'
import { getServerOperation, parseRestartAccepted } from '../api/serverOperations'
import { useServerOperationPolling } from './useServerOperationPolling'

type GeneratedRestartDefinition = ReturnType<typeof serverOperationsRestartMutation>
type GeneratedRestartVariables = Parameters<GeneratedRestartDefinition['mutation']>[0]

export type RestartServerState = 'idle' | 'confirming' | 'submitting' | 'accepted' | 'queued' | 'running' | 'succeeded' | 'failed' | 'cancelled' | 'result-unknown'
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
  operationId: DeepReadonly<ShallowRef<string | null>>
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
  getOperation?: (authorizationHeader: string, operationId: string, signal?: AbortSignal) => Promise<ServerOperationStatusRecord>
  route?: Pick<RouteLocationNormalizedLoaded, 'query'>
  router?: Pick<Router, 'replace'>
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
  const route = options.route ?? optionalRoute()
  const router = options.router ?? optionalRouter()
  const state = shallowRef<RestartServerState>('idle')
  const result = shallowRef<RestartServerAccepted | null>(null)
  const operationId = shallowRef<string | null>(null)
  const error = shallowRef<RestartServerError | null>(null)
  let inFlight: Promise<RestartServerAccepted | null> | null = null
  let controller: AbortController | null = null
  let disposed = false
  let sessionExpiryNotified = false
  const polling = useServerOperationPolling({
    kind: 'restart_script',
    authorizationHeader: () => auth.authorizationHeader,
    getOperation: options.getOperation ?? getServerOperation,
    onOperation(operation) {
      operationId.value = operation.operationId
      state.value = operation.status
      error.value = operation.failureCode === null ? null : Object.freeze({ code: errorCodeFromFailure(operation.failureCode) })
    },
    onUnauthorized() {
      expireSession()
    },
    onForbidden() {
      state.value = 'failed'
      error.value = Object.freeze({ code: 'forbidden' })
    },
    onTransientFailure() {
      if (state.value === 'accepted' || state.value === 'queued')
        state.value = 'running'
    },
  })

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
        operationId.value = accepted.operationId
        state.value = 'accepted'
        sessionExpiryNotified = false
        rememberOperation(accepted.operationId)
        return accepted
      })
      .catch((cause: unknown) => {
        if (disposed || isAbortError(cause))
          return null
        if (cause instanceof HttpError && cause.status === 401) {
          expireSession()
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
    polling.dispose()
  }

  function rememberOperation(operationId: string) {
    if (route === null)
      return
    void router?.replace({ query: { ...route.query, operationId, operationKind: 'restart_script' } })
    polling.resume(operationId)
  }

  function resumeFromRoute() {
    const routedOperationId = routeOperationId(route, 'restart_script')
    operationId.value = routedOperationId
    polling.resume(routedOperationId)
    if (routedOperationId !== null && state.value === 'idle')
      state.value = 'running'
  }

  function expireSession() {
    if (auth.authorizationHeader !== null)
      auth.expireSession()
    state.value = 'failed'
    error.value = Object.freeze({ code: 'session_expired' })
    if (!sessionExpiryNotified) {
      sessionExpiryNotified = true
      onSessionExpired()
    }
  }

  watch(() => [route?.query.operationId, route?.query.operationKind], resumeFromRoute, { immediate: true })
  watch(() => auth.authorizationHeader, resumeFromRoute)

  onUnmounted(dispose)

  return {
    state: readonly(state),
    result: readonly(result),
    operationId: readonly(operationId),
    error: readonly(error),
    startConfirmation,
    cancelConfirmation,
    confirm,
    dispose,
  }
}

function errorCodeFromFailure(value: string): RestartServerErrorCode {
  return restartProblemCodes.has(value as RestartServerErrorCode) ? value as RestartServerErrorCode : 'unknown'
}

function routeOperationId(route: Pick<RouteLocationNormalizedLoaded, 'query'> | null, kind: string): string | null {
  if (route?.query.operationKind !== kind || typeof route.query.operationId !== 'string' || route.query.operationId.trim() === '')
    return null
  return route.query.operationId
}

function optionalRoute(): Pick<RouteLocationNormalizedLoaded, 'query'> | null {
  try {
    return useRoute() ?? null
  }
  catch {
    return null
  }
}

function optionalRouter(): Pick<Router, 'replace'> | null {
  try {
    return useRouter() ?? null
  }
  catch {
    return null
  }
}
