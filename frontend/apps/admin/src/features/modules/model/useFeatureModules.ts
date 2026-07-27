import type { ComputedRef, DeepReadonly, ShallowRef } from 'vue'
import type { AuthRole } from '../../auth/model/authSession'
import type { FeatureModule, FeatureModuleId } from '../api/modules'

import { computed, onMounted, onUnmounted, readonly, shallowRef } from 'vue'
import { HttpError } from '../../../shared/api/http'
import { useAuthStore } from '../../auth'
import {
  disableFeatureModule as requestDisableFeatureModule,
  enableFeatureModule as requestEnableFeatureModule,
  fetchFeatureModules as requestFeatureModules,
} from '../api/modules'

export type FeatureModulesState = 'loading' | 'ready' | 'unavailable'
export type FeatureModulesErrorCode = 'session-expired' | 'forbidden' | 'conflict' | 'invalid-response' | 'unavailable'
export type FeatureModuleMutation = 'enable' | 'disable'

export interface FeatureModulesController {
  modules: DeepReadonly<ShallowRef<readonly FeatureModule[]>>
  state: DeepReadonly<ShallowRef<FeatureModulesState>>
  errorCode: DeepReadonly<ShallowRef<FeatureModulesErrorCode | null>>
  pendingModuleId: DeepReadonly<ShallowRef<FeatureModuleId | null>>
  pendingMutation: DeepReadonly<ShallowRef<FeatureModuleMutation | null>>
  canMutate: Readonly<ComputedRef<boolean>>
  refresh: () => Promise<void>
  enable: (moduleId: FeatureModuleId, expectedRowVersion: number) => Promise<boolean>
  disable: (moduleId: FeatureModuleId, expectedRowVersion: number) => Promise<boolean>
  dispose: () => void
}

export interface UseFeatureModulesOptions {
  auth?: { authorizationHeader: string | null, role: AuthRole | null, expireSession: () => void }
  fetchModules?: typeof requestFeatureModules
  enableModule?: typeof requestEnableFeatureModule
  disableModule?: typeof requestDisableFeatureModule
  onSessionExpired?: () => void
}

function isAbortError(cause: unknown): boolean {
  return (cause instanceof HttpError && cause.code === 'aborted')
    || (cause instanceof Error && cause.name === 'AbortError')
}

function errorCode(cause: unknown): FeatureModulesErrorCode {
  if (!(cause instanceof HttpError))
    return cause instanceof Error ? 'invalid-response' : 'unavailable'
  if (cause.status === 401)
    return 'session-expired'
  if (cause.status === 403)
    return 'forbidden'
  if (cause.status === 409)
    return 'conflict'
  if (cause.code === 'invalid')
    return 'invalid-response'
  return 'unavailable'
}

export function useFeatureModules(options: UseFeatureModulesOptions = {}): FeatureModulesController {
  const auth = options.auth ?? useAuthStore()
  const fetchModules = options.fetchModules ?? requestFeatureModules
  const enableModule = options.enableModule ?? requestEnableFeatureModule
  const disableModule = options.disableModule ?? requestDisableFeatureModule
  const onSessionExpired = options.onSessionExpired ?? (() => {})
  const modules = shallowRef<readonly FeatureModule[]>(Object.freeze([]))
  const state = shallowRef<FeatureModulesState>('loading')
  const currentErrorCode = shallowRef<FeatureModulesErrorCode | null>(null)
  const pendingModuleId = shallowRef<FeatureModuleId | null>(null)
  const pendingMutation = shallowRef<FeatureModuleMutation | null>(null)
  const canMutate = computed(() => auth.role === 'Owner')
  let controller: AbortController | null = null
  let generation = 0
  let disposed = false
  let sessionExpiryNotified = false

  function notifySessionExpired() {
    auth.expireSession()
    if (!sessionExpiryNotified) {
      sessionExpiryNotified = true
      onSessionExpired()
    }
  }

  function fail(cause: unknown) {
    const code = errorCode(cause)
    currentErrorCode.value = code
    state.value = modules.value.length === 0 ? 'unavailable' : 'ready'
    if (code === 'session-expired')
      notifySessionExpired()
  }

  function startRequest(): { controller: AbortController, generation: number } {
    controller?.abort()
    const nextController = new AbortController()
    controller = nextController
    return { controller: nextController, generation: ++generation }
  }

  async function refresh() {
    if (disposed)
      return
    const request = startRequest()
    const authorizationHeader = auth.authorizationHeader
    state.value = 'loading'
    currentErrorCode.value = null
    if (authorizationHeader === null) {
      fail(new HttpError('http', 'Authentication required', { status: 401 }))
      return
    }
    try {
      const result = await fetchModules(authorizationHeader, request.controller.signal)
      if (disposed || generation !== request.generation)
        return
      modules.value = Object.freeze([...result])
      state.value = 'ready'
      currentErrorCode.value = null
      sessionExpiryNotified = false
    }
    catch (cause) {
      if (disposed || generation !== request.generation || isAbortError(cause))
        return
      fail(cause)
    }
    finally {
      if (controller === request.controller)
        controller = null
    }
  }

  async function mutate(
    moduleId: FeatureModuleId,
    expectedRowVersion: number,
    mutation: FeatureModuleMutation,
  ): Promise<boolean> {
    if (disposed || !canMutate.value || pendingModuleId.value !== null)
      return false
    const current = modules.value.find(module => module.moduleId === moduleId)
    if (current === undefined
      || !current.isToggleable
      || current.rowVersion !== expectedRowVersion
      || (mutation === 'enable' ? current.isEnabled : !current.isEnabled)) {
      currentErrorCode.value = 'conflict'
      return false
    }
    const authorizationHeader = auth.authorizationHeader
    if (authorizationHeader === null) {
      fail(new HttpError('http', 'Authentication required', { status: 401 }))
      return false
    }

    const request = startRequest()
    pendingModuleId.value = moduleId
    pendingMutation.value = mutation
    currentErrorCode.value = null
    try {
      const updated = mutation === 'enable'
        ? await enableModule(authorizationHeader, moduleId, expectedRowVersion, request.controller.signal)
        : await disableModule(authorizationHeader, moduleId, expectedRowVersion, request.controller.signal)
      if (disposed || generation !== request.generation)
        return false
      modules.value = Object.freeze(modules.value.map(module => module.moduleId === moduleId ? updated : module))
      state.value = 'ready'
      sessionExpiryNotified = false
      return true
    }
    catch (cause) {
      if (disposed || generation !== request.generation || isAbortError(cause))
        return false
      fail(cause)
      return false
    }
    finally {
      if (generation === request.generation) {
        pendingModuleId.value = null
        pendingMutation.value = null
      }
      if (controller === request.controller)
        controller = null
    }
  }

  function enable(moduleId: FeatureModuleId, expectedRowVersion: number) {
    return mutate(moduleId, expectedRowVersion, 'enable')
  }

  function disable(moduleId: FeatureModuleId, expectedRowVersion: number) {
    return mutate(moduleId, expectedRowVersion, 'disable')
  }

  function dispose() {
    if (disposed)
      return
    disposed = true
    generation++
    controller?.abort()
    controller = null
    pendingModuleId.value = null
    pendingMutation.value = null
  }

  onMounted(() => void refresh())
  onUnmounted(dispose)

  return {
    modules: readonly(modules),
    state: readonly(state),
    errorCode: readonly(currentErrorCode),
    pendingModuleId: readonly(pendingModuleId),
    pendingMutation: readonly(pendingMutation),
    canMutate,
    refresh,
    enable,
    disable,
    dispose,
  }
}
