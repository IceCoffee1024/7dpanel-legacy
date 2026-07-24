import type { DeepReadonly, ShallowRef } from 'vue'
import type {
  ApiKeyMetadata,
  CreateApiKeyInput,
  CreatedApiKey,
} from '../api/apiKeys'

import { onMounted, onUnmounted, readonly, shallowRef } from 'vue'

import { HttpError } from '../../../shared/api/http'
import { useAuthStore } from '../../auth'
import {
  createApiKey,
  fetchApiKeys,
  revokeApiKey,
} from '../api/apiKeys'

export type ApiKeysState = 'loading' | 'empty' | 'fresh' | 'failed' | 'forbidden'
export type ApiKeysFeedbackCode
  = | 'session-expired'
    | 'forbidden'
    | 'load-failed'
    | 'create-failed'
    | 'revoke-failed'

export interface ApiKeysFeedback {
  code: ApiKeysFeedbackCode
}

export interface ApiKeysController {
  state: DeepReadonly<ShallowRef<ApiKeysState>>
  apiKeys: DeepReadonly<ShallowRef<readonly ApiKeyMetadata[]>>
  feedback: DeepReadonly<ShallowRef<ApiKeysFeedback | null>>
  createdApiKey: DeepReadonly<ShallowRef<CreatedApiKey | null>>
  isRefreshing: DeepReadonly<ShallowRef<boolean>>
  isCreating: DeepReadonly<ShallowRef<boolean>>
  revokingKeyId: DeepReadonly<ShallowRef<string | null>>
  refresh: () => Promise<void>
  create: (input: CreateApiKeyInput) => Promise<boolean>
  revoke: (apiKey: ApiKeyMetadata) => Promise<boolean>
  clearFeedback: () => void
  clearCreatedApiKey: () => void
  dispose: () => void
}

export interface UseApiKeysOptions {
  auth?: {
    authorizationHeader: string | null
    expireSession: () => void
  }
  fetchKeys?: (authorizationHeader: string, signal?: AbortSignal) => Promise<readonly ApiKeyMetadata[]>
  createKey?: (
    authorizationHeader: string,
    input: CreateApiKeyInput,
    signal?: AbortSignal,
  ) => Promise<CreatedApiKey>
  revokeKey?: (authorizationHeader: string, keyId: string, signal?: AbortSignal) => Promise<void>
  onSessionExpired?: () => void
}

const sessionExpiredFeedback: ApiKeysFeedback = { code: 'session-expired' }
const forbiddenFeedback: ApiKeysFeedback = { code: 'forbidden' }

function genericFeedback(action: 'load' | 'create' | 'revoke'): ApiKeysFeedback {
  if (action === 'load')
    return { code: 'load-failed' }
  if (action === 'create')
    return { code: 'create-failed' }
  return { code: 'revoke-failed' }
}

export function useApiKeys(options: UseApiKeysOptions = {}): ApiKeysController {
  const auth = options.auth ?? useAuthStore()
  const fetchKeys = options.fetchKeys ?? fetchApiKeys
  const createKey = options.createKey ?? createApiKey
  const revokeKey = options.revokeKey ?? revokeApiKey
  const onSessionExpired = options.onSessionExpired ?? (() => {})
  const state = shallowRef<ApiKeysState>('loading')
  const apiKeys = shallowRef<readonly ApiKeyMetadata[]>(Object.freeze([]))
  const feedback = shallowRef<ApiKeysFeedback | null>(null)
  const createdApiKey = shallowRef<CreatedApiKey | null>(null)
  const isRefreshing = shallowRef(false)
  const isCreating = shallowRef(false)
  const revokingKeyId = shallowRef<string | null>(null)
  let refreshInFlight: Promise<void> | null = null
  let createInFlight: Promise<boolean> | null = null
  let revokeInFlight: Promise<boolean> | null = null
  let refreshController: AbortController | null = null
  let createController: AbortController | null = null
  let revokeController: AbortController | null = null
  let sessionExpiryNotified = false
  let disposed = false

  function clearFeedback() {
    feedback.value = null
  }

  function clearCreatedApiKey() {
    createdApiKey.value = null
  }

  function notifySessionExpired() {
    if (sessionExpiryNotified)
      return
    sessionExpiryNotified = true
    onSessionExpired()
  }

  function handleUnauthorized() {
    auth.expireSession()
    feedback.value = sessionExpiredFeedback
    state.value = 'failed'
    notifySessionExpired()
  }

  function authorize(): string | null {
    if (auth.authorizationHeader !== null)
      return auth.authorizationHeader

    feedback.value = sessionExpiredFeedback
    state.value = 'failed'
    notifySessionExpired()
    return null
  }

  function refresh(): Promise<void> {
    if (refreshInFlight !== null)
      return refreshInFlight
    if (disposed)
      return Promise.resolve()

    const authorizationHeader = authorize()
    if (authorizationHeader === null)
      return Promise.resolve()

    refreshController = new AbortController()
    isRefreshing.value = true
    if (apiKeys.value.length === 0 && state.value !== 'forbidden')
      state.value = 'loading'

    const request = fetchKeys(authorizationHeader, refreshController.signal)
      .then((nextApiKeys) => {
        if (disposed || refreshController?.signal.aborted)
          return
        apiKeys.value = Object.freeze([...nextApiKeys])
        state.value = nextApiKeys.length === 0 ? 'empty' : 'fresh'
        feedback.value = null
      })
      .catch((error: unknown) => {
        if (disposed || refreshController?.signal.aborted || (error instanceof HttpError && error.code === 'aborted'))
          return
        if (error instanceof HttpError && error.status === 401) {
          handleUnauthorized()
          return
        }
        if (error instanceof HttpError && error.status === 403) {
          apiKeys.value = Object.freeze([])
          state.value = 'forbidden'
          feedback.value = forbiddenFeedback
          return
        }
        state.value = 'failed'
        feedback.value = genericFeedback('load')
      })
      .finally(() => {
        if (refreshInFlight === request) {
          refreshController = null
          refreshInFlight = null
          isRefreshing.value = false
        }
      })
    refreshInFlight = request
    return request
  }

  function create(input: CreateApiKeyInput): Promise<boolean> {
    if (createInFlight !== null)
      return createInFlight
    if (disposed)
      return Promise.resolve(false)

    const authorizationHeader = authorize()
    if (authorizationHeader === null)
      return Promise.resolve(false)

    createController = new AbortController()
    isCreating.value = true
    feedback.value = null
    const request = createKey(authorizationHeader, input, createController.signal)
      .then((created) => {
        if (disposed || createController?.signal.aborted)
          return false

        createdApiKey.value = created
        const metadata: ApiKeyMetadata = Object.freeze({
          id: created.id,
          displayPrefix: `7dp_k_${created.id}`,
          name: created.name,
          createdAtUtc: created.createdAtUtc,
          lastUsedAtUtc: null,
          expiresAtUtc: created.expiresAtUtc,
          status: 'active',
        })
        apiKeys.value = Object.freeze([
          metadata,
          ...apiKeys.value.filter(existing => existing.id !== metadata.id),
        ])
        state.value = 'fresh'
        return true
      })
      .catch((error: unknown) => {
        if (disposed || createController?.signal.aborted || (error instanceof HttpError && error.code === 'aborted'))
          return false
        if (error instanceof HttpError && error.status === 401) {
          handleUnauthorized()
          return false
        }
        if (error instanceof HttpError && error.status === 403) {
          state.value = 'forbidden'
          feedback.value = forbiddenFeedback
          return false
        }
        feedback.value = genericFeedback('create')
        return false
      })
      .finally(() => {
        if (createInFlight === request) {
          createController = null
          createInFlight = null
          isCreating.value = false
        }
      })
    createInFlight = request
    return request
  }

  function revoke(apiKey: ApiKeyMetadata): Promise<boolean> {
    if (revokeInFlight !== null)
      return revokeInFlight
    if (disposed)
      return Promise.resolve(false)

    const authorizationHeader = authorize()
    if (authorizationHeader === null)
      return Promise.resolve(false)

    revokeController = new AbortController()
    revokingKeyId.value = apiKey.id
    feedback.value = null
    const request = revokeKey(authorizationHeader, apiKey.id, revokeController.signal)
      .then(() => {
        if (disposed || revokeController?.signal.aborted)
          return false
        apiKeys.value = Object.freeze(apiKeys.value.map((current) => {
          if (current.id !== apiKey.id)
            return current
          return Object.freeze({ ...current, status: 'revoked' as const })
        }))
        return true
      })
      .catch((error: unknown) => {
        if (disposed || revokeController?.signal.aborted || (error instanceof HttpError && error.code === 'aborted'))
          return false
        if (error instanceof HttpError && error.status === 401) {
          handleUnauthorized()
          return false
        }
        if (error instanceof HttpError && error.status === 403) {
          state.value = 'forbidden'
          feedback.value = forbiddenFeedback
          return false
        }
        feedback.value = genericFeedback('revoke')
        return false
      })
      .finally(() => {
        if (revokeInFlight === request) {
          revokeController = null
          revokeInFlight = null
          revokingKeyId.value = null
        }
      })
    revokeInFlight = request
    return request
  }

  function dispose() {
    if (disposed)
      return
    disposed = true
    refreshController?.abort()
    createController?.abort()
    revokeController?.abort()
    refreshController = null
    createController = null
    revokeController = null
    refreshInFlight = null
    createInFlight = null
    revokeInFlight = null
    isRefreshing.value = false
    isCreating.value = false
    revokingKeyId.value = null
    clearCreatedApiKey()
  }

  onMounted(() => {
    void refresh()
  })
  onUnmounted(dispose)

  return {
    state: readonly(state),
    apiKeys: readonly(apiKeys),
    feedback: readonly(feedback),
    createdApiKey: readonly(createdApiKey),
    isRefreshing: readonly(isRefreshing),
    isCreating: readonly(isCreating),
    revokingKeyId: readonly(revokingKeyId),
    refresh,
    create,
    revoke,
    clearFeedback,
    clearCreatedApiKey,
    dispose,
  }
}
