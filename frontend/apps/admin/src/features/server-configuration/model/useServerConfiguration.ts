import type { DeepReadonly, ShallowRef } from 'vue'
import type { ServerConfigurationSnapshot } from '../api/serverConfiguration'

import { onMounted, onUnmounted, readonly, shallowRef } from 'vue'

import { HttpError } from '../../../shared/api/http'
import { useAuthStore } from '../../auth'
import { fetchServerConfiguration, updateServerConfigurationField } from '../api/serverConfiguration'

export type ServerConfigurationState = 'loading' | 'empty' | 'fresh' | 'stale' | 'failed' | 'forbidden'
export interface ServerConfigurationFeedback { code: 'session-expired' | 'forbidden' | 'conflict' | 'invalid-value' | 'update-failed' }

export interface ServerConfigurationController {
  state: DeepReadonly<ShallowRef<ServerConfigurationState>>
  snapshot: DeepReadonly<ShallowRef<ServerConfigurationSnapshot | null>>
  feedback: DeepReadonly<ShallowRef<ServerConfigurationFeedback | null>>
  isRefreshing: DeepReadonly<ShallowRef<boolean>>
  updatingKey: DeepReadonly<ShallowRef<string | null>>
  refresh: () => Promise<void>
  update: (key: string, value: string) => Promise<boolean>
  clearFeedback: () => void
  dispose: () => void
}

interface Options {
  auth?: { authorizationHeader: string | null, expireSession: () => void }
  fetchConfiguration?: typeof fetchServerConfiguration
  updateField?: typeof updateServerConfigurationField
  onSessionExpired?: () => void
}

export function useServerConfiguration(options: Options = {}): ServerConfigurationController {
  const auth = options.auth ?? useAuthStore()
  const fetchConfiguration = options.fetchConfiguration ?? fetchServerConfiguration
  const updateField = options.updateField ?? updateServerConfigurationField
  const state = shallowRef<ServerConfigurationState>('loading')
  const snapshot = shallowRef<ServerConfigurationSnapshot | null>(null)
  const feedback = shallowRef<ServerConfigurationFeedback | null>(null)
  const isRefreshing = shallowRef(false)
  const updatingKey = shallowRef<string | null>(null)
  let refreshController: AbortController | null = null
  let updateController: AbortController | null = null
  let refreshInFlight: Promise<void> | null = null
  let updateInFlight: Promise<boolean> | null = null
  let disposed = false

  function expireSession() {
    auth.expireSession()
    feedback.value = { code: 'session-expired' }
    state.value = 'failed'
    options.onSessionExpired?.()
  }

  function authorization(): string | null {
    if (auth.authorizationHeader !== null)
      return auth.authorizationHeader
    expireSession()
    return null
  }

  function refresh(): Promise<void> {
    if (refreshInFlight)
      return refreshInFlight
    const header = authorization()
    if (header === null || disposed)
      return Promise.resolve()
    refreshController = new AbortController()
    isRefreshing.value = true
    if (snapshot.value === null)
      state.value = 'loading'
    const request = fetchConfiguration(header, refreshController.signal)
      .then((next) => {
        if (disposed || refreshController?.signal.aborted)
          return
        snapshot.value = next
        state.value = next.fields.length === 0 ? 'empty' : 'fresh'
        feedback.value = null
      })
      .catch((error: unknown) => {
        if (disposed || refreshController?.signal.aborted)
          return
        if (error instanceof HttpError && error.status === 401) {
          expireSession()
          return
        }
        if (error instanceof HttpError && error.status === 403) {
          state.value = 'forbidden'
          feedback.value = { code: 'forbidden' }
          return
        }
        state.value = snapshot.value === null ? 'failed' : 'stale'
      })
      .finally(() => {
        refreshController = null
        refreshInFlight = null
        isRefreshing.value = false
      })
    refreshInFlight = request
    return request
  }

  function update(key: string, value: string): Promise<boolean> {
    if (updateInFlight)
      return updateInFlight
    const header = authorization()
    const current = snapshot.value
    if (header === null || current === null || disposed)
      return Promise.resolve(false)
    updateController = new AbortController()
    updatingKey.value = key
    feedback.value = null
    const request = updateField(header, key, value, current.version, updateController.signal)
      .then(async () => {
        if (disposed || updateController?.signal.aborted)
          return false
        await refresh()
        return true
      })
      .catch((error: unknown) => {
        if (disposed || updateController?.signal.aborted)
          return false
        if (error instanceof HttpError && error.status === 401)
          expireSession()
        else if (error instanceof HttpError && error.status === 403)
          feedback.value = { code: 'forbidden' }
        else if (error instanceof HttpError && error.status === 409)
          feedback.value = { code: 'conflict' }
        else if (error instanceof HttpError && error.status === 400)
          feedback.value = { code: 'invalid-value' }
        else
          feedback.value = { code: 'update-failed' }
        return false
      })
      .finally(() => {
        updateController = null
        updateInFlight = null
        updatingKey.value = null
      })
    updateInFlight = request
    return request
  }

  function dispose() {
    disposed = true
    refreshController?.abort()
    updateController?.abort()
    refreshController = null
    updateController = null
  }

  onMounted(() => void refresh())
  onUnmounted(dispose)

  return {
    state: readonly(state),
    snapshot: readonly(snapshot),
    feedback: readonly(feedback),
    isRefreshing: readonly(isRefreshing),
    updatingKey: readonly(updatingKey),
    refresh,
    update,
    clearFeedback: () => { feedback.value = null },
    dispose,
  }
}
