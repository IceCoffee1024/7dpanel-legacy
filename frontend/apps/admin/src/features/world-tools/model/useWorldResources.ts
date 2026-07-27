import type { DeepReadonly, ShallowRef } from 'vue'
import type {
  WorldCatalog,
  WorldCollection,
  WorldContainer,
  WorldDrone,
  WorldLandClaim,
  WorldResourcesTransport,
  WorldSourceState,
  WorldSummary,
  WorldVehicle,
} from '../api/worldTools'

import { onMounted, onUnmounted, readonly, shallowRef } from 'vue'
import { HttpError } from '../../../shared/api/http'
import { useAuthStore } from '../../auth'
import { worldResourcesTransport } from '../api/worldTools'

export type WorldResourcePhase = 'loading' | 'ready' | 'failed'
export type WorldResourceErrorCode = 'session-expired' | 'forbidden' | 'unavailable' | 'invalid-response'

export interface WorldResourceState<T> {
  phase: WorldResourcePhase
  sourceState: WorldSourceState
  data: T | null
  errorCode: WorldResourceErrorCode | null
}

export interface WorldResourcesController {
  summary: DeepReadonly<ShallowRef<WorldResourceState<WorldSummary>>>
  landClaims: DeepReadonly<ShallowRef<WorldResourceState<WorldCollection<WorldLandClaim>>>>
  vehicles: DeepReadonly<ShallowRef<WorldResourceState<WorldCollection<WorldVehicle>>>>
  drones: DeepReadonly<ShallowRef<WorldResourceState<WorldCollection<WorldDrone>>>>
  containers: DeepReadonly<ShallowRef<WorldResourceState<WorldCollection<WorldContainer>>>>
  blockCatalog: DeepReadonly<ShallowRef<WorldResourceState<WorldCatalog>>>
  prefabCatalog: DeepReadonly<ShallowRef<WorldResourceState<WorldCatalog>>>
  entityTypeCatalog: DeepReadonly<ShallowRef<WorldResourceState<WorldCatalog>>>
  refresh: () => Promise<void>
  dispose: () => void
}

export interface UseWorldResourcesOptions {
  auth?: { authorizationHeader: string | null, expireSession: () => void }
  transport?: WorldResourcesTransport
  onSessionExpired?: () => void
}

function initialState<T>(): WorldResourceState<T> {
  return Object.freeze({ phase: 'loading', sourceState: 'Unavailable', data: null, errorCode: null })
}

function failedState<T>(errorCode: WorldResourceErrorCode): WorldResourceState<T> {
  return Object.freeze({ phase: 'failed', sourceState: 'Unavailable', data: null, errorCode })
}

function errorCode(cause: unknown): WorldResourceErrorCode {
  if (cause instanceof HttpError && cause.status === 401)
    return 'session-expired'
  if (cause instanceof HttpError && cause.status === 403)
    return 'forbidden'
  if (cause instanceof HttpError && cause.code === 'invalid')
    return 'invalid-response'
  return 'unavailable'
}

function isAbortError(cause: unknown): boolean {
  return (cause instanceof HttpError && cause.code === 'aborted')
    || (cause instanceof Error && cause.name === 'AbortError')
}

export function useWorldResources(options: UseWorldResourcesOptions = {}): WorldResourcesController {
  const auth = options.auth ?? useAuthStore()
  const transport = options.transport ?? worldResourcesTransport
  const onSessionExpired = options.onSessionExpired ?? (() => {})
  const summary = shallowRef<WorldResourceState<WorldSummary>>(initialState())
  const landClaims = shallowRef<WorldResourceState<WorldCollection<WorldLandClaim>>>(initialState())
  const vehicles = shallowRef<WorldResourceState<WorldCollection<WorldVehicle>>>(initialState())
  const drones = shallowRef<WorldResourceState<WorldCollection<WorldDrone>>>(initialState())
  const containers = shallowRef<WorldResourceState<WorldCollection<WorldContainer>>>(initialState())
  const blockCatalog = shallowRef<WorldResourceState<WorldCatalog>>(initialState())
  const prefabCatalog = shallowRef<WorldResourceState<WorldCatalog>>(initialState())
  const entityTypeCatalog = shallowRef<WorldResourceState<WorldCatalog>>(initialState())
  let controller: AbortController | null = null
  let generation = 0
  let disposed = false
  let sessionExpiryNotified = false

  function markAllFailed(code: WorldResourceErrorCode) {
    summary.value = failedState(code)
    landClaims.value = failedState(code)
    vehicles.value = failedState(code)
    drones.value = failedState(code)
    containers.value = failedState(code)
    blockCatalog.value = failedState(code)
    prefabCatalog.value = failedState(code)
    entityTypeCatalog.value = failedState(code)
  }

  function notifySessionExpired() {
    auth.expireSession()
    if (!sessionExpiryNotified) {
      sessionExpiryNotified = true
      onSessionExpired()
    }
  }

  async function load<T>(
    target: ShallowRef<WorldResourceState<T>>,
    request: () => Promise<T>,
    currentGeneration: number,
  ) {
    target.value = initialState()
    try {
      const data = await request()
      if (disposed || generation !== currentGeneration)
        return
      const state = (data as { sourceState?: WorldSourceState }).sourceState ?? 'Success'
      target.value = Object.freeze({ phase: 'ready', sourceState: state, data, errorCode: null })
      sessionExpiryNotified = false
    }
    catch (cause) {
      if (disposed || generation !== currentGeneration || isAbortError(cause))
        return
      const code = errorCode(cause)
      target.value = failedState(code)
      if (code === 'session-expired')
        notifySessionExpired()
    }
  }

  async function refresh() {
    if (disposed)
      return
    controller?.abort()
    const currentController = new AbortController()
    controller = currentController
    const currentGeneration = ++generation
    const header = auth.authorizationHeader
    if (header === null) {
      markAllFailed('session-expired')
      notifySessionExpired()
      return
    }

    await Promise.all([
      load(summary, () => transport.fetchSummary(header, currentController.signal), currentGeneration),
      load(landClaims, () => transport.fetchLandClaims(header, currentController.signal), currentGeneration),
      load(vehicles, () => transport.fetchVehicles(header, currentController.signal), currentGeneration),
      load(drones, () => transport.fetchDrones(header, currentController.signal), currentGeneration),
      load(containers, () => transport.fetchContainers(header, currentController.signal), currentGeneration),
      load(blockCatalog, () => transport.fetchBlockCatalog(header, currentController.signal), currentGeneration),
      load(prefabCatalog, () => transport.fetchPrefabCatalog(header, currentController.signal), currentGeneration),
      load(entityTypeCatalog, () => transport.fetchEntityTypeCatalog(header, currentController.signal), currentGeneration),
    ])
    if (controller === currentController)
      controller = null
  }

  function dispose() {
    if (disposed)
      return
    disposed = true
    generation++
    controller?.abort()
    controller = null
  }

  onMounted(() => void refresh())
  onUnmounted(dispose)

  return {
    summary: readonly(summary),
    landClaims: readonly(landClaims),
    vehicles: readonly(vehicles),
    drones: readonly(drones),
    containers: readonly(containers),
    blockCatalog: readonly(blockCatalog),
    prefabCatalog: readonly(prefabCatalog),
    entityTypeCatalog: readonly(entityTypeCatalog),
    refresh,
    dispose,
  }
}
