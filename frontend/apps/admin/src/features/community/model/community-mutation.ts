import type { ShallowRef } from 'vue'

import type { CommunityAuth, CommunityMutationState, CommunityMutationTarget } from './community.types'

import { shallowRef, unref } from 'vue'
import { HttpError } from '../../../shared/api/http'

export interface CommunityMutationController {
  readonly state: ShallowRef<CommunityMutationState>
  readonly target: ShallowRef<CommunityMutationTarget | null>
  mutate: <T>(
    target: CommunityMutationTarget,
    request: (authorization: string, signal: AbortSignal) => Promise<T>,
    apply: (value: T) => void,
  ) => Promise<boolean>
  clear: () => void
  dispose: () => void
}

function mutationStateAfterFailure(error: unknown, auth: CommunityAuth): CommunityMutationState {
  if (error instanceof HttpError && error.status === 401) {
    auth.expireSession()
    return 'unavailable'
  }
  if (error instanceof HttpError && error.status === 403)
    return 'forbidden'
  if (error instanceof HttpError && (error.status === 503 || error.code === 'network' || error.code === 'timeout'))
    return 'unavailable'
  return 'failed'
}

export function createCommunityMutation(auth: CommunityAuth, isDisposed: () => boolean): CommunityMutationController {
  const state = shallowRef<CommunityMutationState>('idle')
  const target = shallowRef<CommunityMutationTarget | null>(null)
  let controller: AbortController | null = null

  async function mutate<T>(
    mutationTarget: CommunityMutationTarget,
    request: (authorization: string, signal: AbortSignal) => Promise<T>,
    apply: (value: T) => void,
  ): Promise<boolean> {
    const token = unref(auth.authorizationHeader)
    if (isDisposed() || token === null || target.value !== null)
      return false
    const nextController = new AbortController()
    controller = nextController
    target.value = mutationTarget
    state.value = 'saving'
    try {
      const value = await request(token, nextController.signal)
      if (isDisposed() || nextController.signal.aborted)
        return false
      apply(value)
      state.value = 'confirmed'
      return true
    }
    catch (error) {
      if (!nextController.signal.aborted)
        state.value = mutationStateAfterFailure(error, auth)
      return false
    }
    finally {
      if (controller === nextController) {
        controller = null
        target.value = null
      }
    }
  }

  function clear() {
    if (target.value === null)
      state.value = 'idle'
  }

  function dispose() {
    controller?.abort()
    controller = null
    target.value = null
    state.value = 'idle'
  }

  return { state, target, mutate, clear, dispose }
}
