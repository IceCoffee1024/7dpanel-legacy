import type { DeepReadonly, ShallowRef } from 'vue'
import type { GrantOperation, GrantRewardInput, RewardPackage, RewardPackageDraft } from '../api/rewards'

import { onUnmounted, readonly, shallowRef } from 'vue'
import { HttpError } from '../../../shared/api/http'
import { useAuthStore } from '../../auth/model/authStore'
import * as api from '../api/rewards'

export type RewardsState = 'idle' | 'loading' | 'empty' | 'fresh' | 'stale' | 'failed' | 'forbidden'
interface Auth { readonly authorizationHeader: string | null, expireSession: () => void }

export interface RewardPackagesController {
  readonly state: DeepReadonly<ShallowRef<RewardsState>>
  readonly rewardPackage: DeepReadonly<ShallowRef<RewardPackage | null>>
  readonly isMutating: DeepReadonly<ShallowRef<boolean>>
  readonly errorCode: DeepReadonly<ShallowRef<string | null>>
  load: (packageId: string) => Promise<boolean>
  save: (draft: RewardPackageDraft) => Promise<boolean>
  dispose: () => void
}

export interface RewardOperationsController {
  readonly state: DeepReadonly<ShallowRef<RewardsState>>
  readonly operations: DeepReadonly<ShallowRef<readonly GrantOperation[]>>
  readonly mutatingOperationId: DeepReadonly<ShallowRef<string | null>>
  readonly errorCode: DeepReadonly<ShallowRef<string | null>>
  refresh: () => Promise<void>
  grant: (input: GrantRewardInput) => Promise<boolean>
  confirm: (operation: GrantOperation) => Promise<boolean>
  refund: (operation: GrantOperation) => Promise<boolean>
  compensate: (operation: GrantOperation) => Promise<boolean>
  dispose: () => void
}

function failure(error: unknown, auth: Auth, stale: boolean): { state: RewardsState, code: string } {
  if (error instanceof HttpError && error.status === 401) {
    auth.expireSession()
    return { state: 'failed', code: 'session_expired' }
  }
  if (error instanceof HttpError && error.status === 403)
    return { state: 'forbidden', code: 'forbidden' }
  return { state: stale ? 'stale' : 'failed', code: error instanceof HttpError ? (error.problemCode ?? error.code) : 'invalid_response' }
}

export function useRewardPackages(options: { auth?: Auth } = {}): RewardPackagesController {
  const auth = options.auth ?? useAuthStore()
  const state = shallowRef<RewardsState>('idle')
  const rewardPackage = shallowRef<RewardPackage | null>(null)
  const isMutating = shallowRef(false)
  const errorCode = shallowRef<string | null>(null)
  let request: Promise<boolean> | null = null
  let controller: AbortController | null = null
  let disposed = false

  function run(action: (token: string, signal: AbortSignal) => Promise<RewardPackage>, mutating: boolean): Promise<boolean> {
    if (request !== null)
      return request
    const token = auth.authorizationHeader
    if (disposed || token === null)
      return Promise.resolve(false)
    controller = new AbortController()
    const current = controller
    if (mutating)
      isMutating.value = true
    else state.value = 'loading'
    errorCode.value = null
    const pending = action(token, current.signal)
      .then((next) => {
        if (disposed || current.signal.aborted)
          return false
        rewardPackage.value = next
        state.value = 'fresh'
        return true
      })
      .catch((error: unknown) => {
        if (disposed || current.signal.aborted)
          return false
        const result = failure(error, auth, rewardPackage.value !== null)
        state.value = result.state
        errorCode.value = result.code
        return false
      })
      .finally(() => {
        if (request === pending) {
          request = null
          controller = null
          isMutating.value = false
        }
      })
    request = pending
    return pending
  }
  function dispose() {
    disposed = true
    controller?.abort()
  }
  onUnmounted(dispose)
  return {
    state: readonly(state),
    rewardPackage: readonly(rewardPackage),
    isMutating: readonly(isMutating),
    errorCode: readonly(errorCode),
    load: packageId => run((token, signal) => api.fetchRewardPackage(token, packageId, signal), false),
    save: draft => run((token, signal) => api.saveRewardPackage(token, draft, signal), true),
    dispose,
  }
}

export function useRewardOperations(options: { auth?: Auth } = {}): RewardOperationsController {
  const auth = options.auth ?? useAuthStore()
  const state = shallowRef<RewardsState>('loading')
  const operations = shallowRef<readonly GrantOperation[]>(Object.freeze([]))
  const mutatingOperationId = shallowRef<string | null>(null)
  const errorCode = shallowRef<string | null>(null)
  let refreshRequest: Promise<void> | null = null
  let controller: AbortController | null = null
  let disposed = false

  function refresh(): Promise<void> {
    if (refreshRequest !== null)
      return refreshRequest
    const token = auth.authorizationHeader
    if (disposed || token === null)
      return Promise.resolve()
    controller = new AbortController()
    const current = controller
    if (operations.value.length === 0)
      state.value = 'loading'
    const pending = api.fetchPendingGrantOperations(token, 50, current.signal)
      .then((next) => {
        if (!disposed && !current.signal.aborted) {
          operations.value = Object.freeze([...next])
          state.value = next.length === 0 ? 'empty' : 'fresh'
          errorCode.value = null
        }
      })
      .catch((error: unknown) => {
        if (!disposed && !current.signal.aborted) {
          const result = failure(error, auth, operations.value.length > 0)
          state.value = result.state
          errorCode.value = result.code
        }
      })
      .finally(() => {
        if (refreshRequest === pending) {
          refreshRequest = null
          controller = null
        }
      })
    refreshRequest = pending
    return pending
  }

  async function mutate(id: string, action: (token: string, signal: AbortSignal) => Promise<GrantOperation>): Promise<boolean> {
    const token = auth.authorizationHeader
    if (disposed || token === null || mutatingOperationId.value !== null)
      return false
    controller = new AbortController()
    const current = controller
    mutatingOperationId.value = id
    errorCode.value = null
    try {
      const operation = await action(token, current.signal)
      if (disposed || current.signal.aborted)
        return false
      operations.value = Object.freeze([operation, ...operations.value.filter(item => item.operationId !== operation.operationId)])
      state.value = 'fresh'
      return true
    }
    catch (error) {
      const result = failure(error, auth, operations.value.length > 0)
      state.value = result.state
      errorCode.value = result.code
      return false
    }
    finally {
      mutatingOperationId.value = null
      controller = null
    }
  }
  function dispose() {
    disposed = true
    controller?.abort()
  }
  onUnmounted(dispose)
  return {
    state: readonly(state),
    operations: readonly(operations),
    mutatingOperationId: readonly(mutatingOperationId),
    errorCode: readonly(errorCode),
    refresh,
    grant: input => mutate('new', (token, signal) => api.createGrantOperation(token, input, signal)),
    confirm: operation => mutate(operation.operationId, (token, signal) => api.confirmGrantOperation(token, operation.operationId, signal)),
    refund: operation => mutate(operation.operationId, (token, signal) => api.refundGrantOperation(token, operation.operationId, crypto.randomUUID(), signal)),
    compensate: operation => mutate(operation.operationId, (token, signal) => api.compensateGrantOperation(token, operation.operationId, crypto.randomUUID(), signal)),
    dispose,
  }
}
