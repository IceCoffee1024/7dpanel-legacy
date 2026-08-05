import type { ShallowRef } from 'vue'

import type { CommunityAuth, CommunityViewState } from './community.types'

import { unref } from 'vue'
import { HttpError } from '../../../shared/api/http'

type QueryResource = 'homes' | 'friendship' | 'teleport-operation' | 'vote-round'
type ResourceKey = 'game-command-configuration' | 'teleport-settings' | 'friendship-records' | 'teleport-operations' | 'cities' | 'all-cities' | 'vote-configurations' | 'vote-rounds' | 'all-vote-rounds' | `${QueryResource}:${string}`

export interface CommunityLoader {
  load: <T>(
    key: ResourceKey,
    state: ShallowRef<CommunityViewState>,
    hasData: () => boolean,
    request: (authorization: string, signal: AbortSignal) => Promise<T>,
    apply: (value: T) => number,
  ) => Promise<void>
  loadQuery: <T>(
    resource: QueryResource,
    parameter: string,
    state: ShallowRef<CommunityViewState>,
    hasData: () => boolean,
    request: (authorization: string, signal: AbortSignal) => Promise<T>,
    apply: (value: T) => number,
  ) => Promise<void>
  invalidate: (key: ResourceKey, state: ShallowRef<CommunityViewState>) => void
  dispose: () => void
}

function stateAfterFailure(error: unknown, auth: CommunityAuth, hasData: boolean): CommunityViewState {
  if (error instanceof HttpError && error.status === 401) {
    auth.expireSession()
    return hasData ? 'stale' : 'unavailable'
  }
  if (error instanceof HttpError && error.status === 403)
    return 'forbidden'
  return hasData ? 'stale' : 'unavailable'
}

export function createCommunityLoader(auth: CommunityAuth, isDisposed: () => boolean): CommunityLoader {
  const requests: Partial<Record<ResourceKey, Promise<void>>> = {}
  const controllers: Partial<Record<ResourceKey, AbortController>> = {}
  const currentQueryKeys: Partial<Record<QueryResource, ResourceKey>> = {}

  function load<T>(
    key: ResourceKey,
    state: ShallowRef<CommunityViewState>,
    hasData: () => boolean,
    request: (authorization: string, signal: AbortSignal) => Promise<T>,
    apply: (value: T) => number,
  ): Promise<void> {
    const active = requests[key]
    if (active !== undefined)
      return active
    const token = unref(auth.authorizationHeader)
    if (isDisposed() || token === null) {
      state.value = hasData() ? 'stale' : 'unavailable'
      return Promise.resolve()
    }
    const controller = new AbortController()
    controllers[key] = controller
    state.value = 'loading'
    const pending = request(token, controller.signal)
      .then((value) => {
        if (isDisposed() || controller.signal.aborted)
          return
        state.value = apply(value) === 0 ? 'empty' : 'ready'
      })
      .catch((error: unknown) => {
        if (isDisposed() || controller.signal.aborted)
          return
        state.value = stateAfterFailure(error, auth, hasData())
      })
      .finally(() => {
        if (requests[key] === pending) {
          delete requests[key]
          delete controllers[key]
        }
      })
    requests[key] = pending
    return pending
  }

  function loadQuery<T>(
    resource: QueryResource,
    parameter: string,
    state: ShallowRef<CommunityViewState>,
    hasData: () => boolean,
    request: (authorization: string, signal: AbortSignal) => Promise<T>,
    apply: (value: T) => number,
  ): Promise<void> {
    const key = `${resource}:${parameter}` as ResourceKey
    const previousKey = currentQueryKeys[resource]
    if (previousKey !== undefined && previousKey !== key) {
      controllers[previousKey]?.abort()
      delete controllers[previousKey]
      delete requests[previousKey]
    }
    currentQueryKeys[resource] = key
    return load(key, state, hasData, request, apply)
  }

  function dispose() {
    for (const controller of Object.values(controllers))
      controller?.abort()
  }

  function invalidate(key: ResourceKey, state: ShallowRef<CommunityViewState>) {
    controllers[key]?.abort()
    delete controllers[key]
    delete requests[key]
    state.value = 'unavailable'
  }

  return { load, loadQuery, invalidate, dispose }
}
