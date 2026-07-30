import type { DeepReadonly, ShallowRef } from 'vue'
import type { DailyRewardPolicy, DailyRewardPolicyUpdateRequest } from '../api/dailyRewardPolicy'

import { onUnmounted, readonly, shallowRef } from 'vue'

import { HttpError } from '../../../shared/api/http'
import { useAuthStore } from '../../auth/model/authStore'
import * as api from '../api/dailyRewardPolicy'

export type DailyRewardPolicyState = 'loading' | 'ready' | 'not-configured' | 'stale' | 'failed' | 'forbidden'
export type DailyRewardPolicySaveError = { readonly code: 'conflict' | 'invalid' | 'unavailable' | 'forbidden' | 'session_expired' } | null

interface DailyRewardPolicyAuth {
  readonly authorizationHeader: string | null
  expireSession: () => void
}

export interface DailyRewardPolicyController {
  readonly state: DeepReadonly<ShallowRef<DailyRewardPolicyState>>
  readonly policy: DeepReadonly<ShallowRef<DailyRewardPolicy | null>>
  readonly draft: DeepReadonly<ShallowRef<DailyRewardPolicyUpdateRequest>>
  readonly isSaving: DeepReadonly<ShallowRef<boolean>>
  readonly saveError: DeepReadonly<ShallowRef<DailyRewardPolicySaveError>>
  load: () => Promise<void>
  updateDraft: (draft: DailyRewardPolicyUpdateRequest) => void
  save: () => Promise<boolean>
  dispose: () => void
}

export interface UseDailyRewardPolicyOptions {
  readonly auth?: DailyRewardPolicyAuth
  readonly fetchPolicy?: typeof api.fetchDailyRewardPolicy
  readonly savePolicy?: typeof api.saveDailyRewardPolicy
}

function emptyDraft(): DailyRewardPolicyUpdateRequest {
  return { rewardPackageId: '', enabled: true, expectedRowVersion: null }
}

function draftFor(policy: DailyRewardPolicy): DailyRewardPolicyUpdateRequest {
  return {
    rewardPackageId: policy.rewardPackageId,
    enabled: policy.enabled,
    expectedRowVersion: policy.rowVersion,
  }
}

export function useDailyRewardPolicy(
  options: UseDailyRewardPolicyOptions = {},
): DailyRewardPolicyController {
  const auth = options.auth ?? useAuthStore()
  const state = shallowRef<DailyRewardPolicyState>('loading')
  const policy = shallowRef<DailyRewardPolicy | null>(null)
  const draft = shallowRef<DailyRewardPolicyUpdateRequest>(emptyDraft())
  const isSaving = shallowRef(false)
  const saveError = shallowRef<DailyRewardPolicySaveError>(null)
  let request: Promise<void> | null = null
  let controller: AbortController | null = null
  let disposed = false

  function unavailable(error: unknown): DailyRewardPolicyState {
    if (error instanceof HttpError && error.status === 401) {
      auth.expireSession()
      return policy.value === null ? 'failed' : 'stale'
    }
    if (error instanceof HttpError && error.status === 403)
      return 'forbidden'
    return policy.value === null ? 'failed' : 'stale'
  }

  function load(): Promise<void> {
    if (request !== null)
      return request
    const token = auth.authorizationHeader
    if (disposed || token === null) {
      state.value = policy.value === null ? 'failed' : 'stale'
      return Promise.resolve()
    }
    controller = new AbortController()
    const current = controller
    state.value = 'loading'
    const pending = (options.fetchPolicy ?? api.fetchDailyRewardPolicy)(token, current.signal)
      .then((next) => {
        if (disposed || current.signal.aborted)
          return
        policy.value = next
        draft.value = draftFor(next)
        state.value = 'ready'
      })
      .catch((error: unknown) => {
        if (disposed || current.signal.aborted)
          return
        if (error instanceof HttpError && error.status === 404) {
          policy.value = null
          draft.value = emptyDraft()
          state.value = 'not-configured'
          return
        }
        state.value = unavailable(error)
      })
      .finally(() => {
        if (request === pending) {
          request = null
          controller = null
        }
      })
    request = pending
    return pending
  }

  function updateDraft(next: DailyRewardPolicyUpdateRequest) {
    draft.value = {
      rewardPackageId: next.rewardPackageId,
      enabled: next.enabled,
      expectedRowVersion: next.expectedRowVersion,
    }
  }

  async function save(): Promise<boolean> {
    const token = auth.authorizationHeader
    if (disposed || token === null || isSaving.value || draft.value.rewardPackageId.trim() === '')
      return false
    controller = new AbortController()
    const current = controller
    isSaving.value = true
    saveError.value = null
    try {
      const saved = await (options.savePolicy ?? api.saveDailyRewardPolicy)(token, draft.value, current.signal)
      if (disposed || current.signal.aborted)
        return false
      policy.value = saved
      draft.value = draftFor(saved)
      state.value = 'ready'
      return true
    }
    catch (error) {
      if (disposed || current.signal.aborted)
        return false
      if (error instanceof HttpError && error.status === 401) {
        auth.expireSession()
        saveError.value = { code: 'session_expired' }
        state.value = policy.value === null ? 'failed' : 'stale'
      }
      else if (error instanceof HttpError && error.status === 403) {
        saveError.value = { code: 'forbidden' }
        state.value = 'forbidden'
      }
      else if (error instanceof HttpError && error.status === 409) {
        saveError.value = { code: 'conflict' }
        state.value = 'stale'
      }
      else if (error instanceof HttpError && (error.status === 400 || error.status === 404)) {
        saveError.value = { code: 'invalid' }
        state.value = policy.value === null ? 'not-configured' : 'ready'
      }
      else {
        saveError.value = { code: 'unavailable' }
        state.value = policy.value === null ? 'failed' : 'stale'
      }
      return false
    }
    finally {
      if (controller === current)
        controller = null
      isSaving.value = false
    }
  }

  function dispose() {
    disposed = true
    controller?.abort()
  }

  onUnmounted(dispose)
  void load()
  return {
    state: readonly(state),
    policy: readonly(policy),
    draft: readonly(draft),
    isSaving: readonly(isSaving),
    saveError: readonly(saveError),
    load,
    updateDraft,
    save,
    dispose,
  }
}
