import type { DeepReadonly, ShallowRef } from 'vue'
import type { GeoIpCredentials, GeoIpCredentialsDraft, GeoIpDiagnostics, GeoIpPolicy, GeoIpPolicyDraft, GeoIpTestResult } from '../api/geoip'

import { onMounted, onUnmounted, readonly, shallowRef } from 'vue'
import { HttpError } from '../../../shared/api/http'
import { useAuthStore } from '../../auth'
import { getGeoIpCredentials, getGeoIpDiagnostics, getGeoIpPolicy, saveGeoIpPolicy, testGeoIpPolicy, updateGeoIpCredentials } from '../api/geoip'

export type GeoIpViewState = 'loading' | 'ready' | 'failed' | 'forbidden' | 'stale'
export type GeoIpDiagnosticsState = 'loading' | 'ready' | 'unavailable'
export type GeoIpCredentialsState = 'loading' | 'ready' | 'unavailable'
export interface GeoIpController {
  state: DeepReadonly<ShallowRef<GeoIpViewState>>
  policy: DeepReadonly<ShallowRef<GeoIpPolicy | null>>
  diagnostics: DeepReadonly<ShallowRef<GeoIpDiagnostics | null>>
  diagnosticsState: DeepReadonly<ShallowRef<GeoIpDiagnosticsState>>
  credentials: DeepReadonly<ShallowRef<GeoIpCredentials | null>>
  credentialsState: DeepReadonly<ShallowRef<GeoIpCredentialsState>>
  testResult: DeepReadonly<ShallowRef<GeoIpTestResult | null>>
  isMutating: DeepReadonly<ShallowRef<boolean>>
  errorCode: DeepReadonly<ShallowRef<string | null>>
  refresh: () => Promise<void>
  save: (draft: GeoIpPolicyDraft) => Promise<boolean>
  test: (ipAddress: string) => Promise<boolean>
  updateCredentials: (draft: GeoIpCredentialsDraft) => Promise<boolean>
  dispose: () => void
}

export function useGeoIp(options: { onSessionExpired?: () => void } = {}): GeoIpController {
  const auth = useAuthStore()
  const state = shallowRef<GeoIpViewState>('loading')
  const policy = shallowRef<GeoIpPolicy | null>(null)
  const diagnostics = shallowRef<GeoIpDiagnostics | null>(null)
  const diagnosticsState = shallowRef<GeoIpDiagnosticsState>('loading')
  const credentials = shallowRef<GeoIpCredentials | null>(null)
  const credentialsState = shallowRef<GeoIpCredentialsState>('loading')
  const testResult = shallowRef<GeoIpTestResult | null>(null)
  const isMutating = shallowRef(false)
  const errorCode = shallowRef<string | null>(null)
  let loadController: AbortController | null = null
  let mutationController: AbortController | null = null
  let requestVersion = 0
  let disposed = false

  function authorization() {
    const value = auth.authorizationHeader
    if (value === null) {
      auth.expireSession()
      options.onSessionExpired?.()
    }
    return value
  }
  function stableErrorCode(error: unknown) {
    return error instanceof HttpError ? (error.problemCode ?? error.code) : 'protocol_error'
  }
  function fail(error: unknown) {
    if (disposed || (error instanceof HttpError && error.code === 'aborted'))
      return
    errorCode.value = stableErrorCode(error)
    if (error instanceof HttpError && error.status === 401) {
      auth.expireSession()
      options.onSessionExpired?.()
    }
    if (error instanceof HttpError && error.status === 403) {
      policy.value = null
      state.value = 'forbidden'
      return
    }
    state.value = policy.value === null ? 'failed' : 'stale'
  }

  async function loadDiagnostics(authorizationHeader: string, signal: AbortSignal, current: number) {
    try {
      const next = await getGeoIpDiagnostics(authorizationHeader, signal)
      if (disposed || current !== requestVersion)
        return
      diagnostics.value = next
      diagnosticsState.value = 'ready'
    }
    catch (error) {
      if (disposed || current !== requestVersion || (error instanceof HttpError && error.code === 'aborted'))
        return
      if (error instanceof HttpError && (error.status === 401 || error.status === 403))
        fail(error)
      diagnostics.value = null
      diagnosticsState.value = 'unavailable'
    }
  }
  async function loadCredentials(authorizationHeader: string, signal: AbortSignal, current: number) {
    try {
      const next = await getGeoIpCredentials(authorizationHeader, signal)
      if (disposed || current !== requestVersion)
        return
      credentials.value = next
      credentialsState.value = 'ready'
    }
    catch (error) {
      if (disposed || current !== requestVersion || (error instanceof HttpError && error.code === 'aborted'))
        return
      if (error instanceof HttpError && (error.status === 401 || error.status === 403))
        fail(error)
      credentials.value = null
      credentialsState.value = 'unavailable'
    }
  }
  async function refresh() {
    if (disposed)
      return
    const authorizationHeader = authorization()
    if (authorizationHeader === null)
      return
    loadController?.abort()
    const current = ++requestVersion
    const controller = new AbortController()
    loadController = controller
    if (policy.value === null)
      state.value = 'loading'
    diagnosticsState.value = 'loading'
    credentialsState.value = 'loading'
    try {
      const next = await getGeoIpPolicy(authorizationHeader, controller.signal)
      if (disposed || current !== requestVersion)
        return
      policy.value = next
      errorCode.value = null
      state.value = 'ready'
      await Promise.all([
        loadDiagnostics(authorizationHeader, controller.signal, current),
        loadCredentials(authorizationHeader, controller.signal, current),
      ])
    }
    catch (error) {
      if (current === requestVersion)
        fail(error)
    }
    finally {
      if (current === requestVersion)
        loadController = null
    }
  }

  async function mutate(operation: (authorizationHeader: string, signal: AbortSignal) => Promise<void>, refreshAfter = false) {
    if (disposed || isMutating.value)
      return false
    const authorizationHeader = authorization()
    if (authorizationHeader === null)
      return false
    isMutating.value = true
    errorCode.value = null
    const controller = new AbortController()
    mutationController = controller
    try {
      await operation(authorizationHeader, controller.signal)
      if (disposed)
        return false
      if (refreshAfter)
        await refresh()
      return !disposed
    }
    catch (error) {
      fail(error)
      return false
    }
    finally {
      if (mutationController === controller)
        mutationController = null
      isMutating.value = false
    }
  }
  function save(draft: GeoIpPolicyDraft) {
    return mutate(async (authorizationHeader, signal) => {
      await saveGeoIpPolicy(authorizationHeader, draft, signal)
    }, true)
  }
  function test(ipAddress: string) {
    return mutate(async (authorizationHeader, signal) => {
      testResult.value = await testGeoIpPolicy(authorizationHeader, ipAddress.trim(), signal)
    })
  }
  function updateCredentials(draft: GeoIpCredentialsDraft) {
    return mutate(async (authorizationHeader, signal) => {
      credentials.value = await updateGeoIpCredentials(authorizationHeader, draft, signal)
      credentialsState.value = 'ready'
    })
  }
  function dispose() {
    if (disposed)
      return
    disposed = true
    requestVersion++
    loadController?.abort()
    mutationController?.abort()
    loadController = null
    mutationController = null
  }
  onMounted(() => void refresh())
  onUnmounted(dispose)
  return {
    state: readonly(state),
    policy: readonly(policy),
    diagnostics: readonly(diagnostics),
    diagnosticsState: readonly(diagnosticsState),
    credentials: readonly(credentials),
    credentialsState: readonly(credentialsState),
    testResult: readonly(testResult),
    isMutating: readonly(isMutating),
    errorCode: readonly(errorCode),
    refresh,
    save,
    test,
    updateCredentials,
    dispose,
  }
}
