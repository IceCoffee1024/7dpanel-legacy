import type { DeepReadonly, ShallowRef } from 'vue'
import type { DiscordBinding, DiscordBindingCode, DiscordCommand, DiscordConfiguration, DiscordConfigurationDraft, DiscordDelivery, DiscordHealth, SecretOperation } from '../api/discord'

import { onMounted, onUnmounted, readonly, shallowRef } from 'vue'
import { HttpError } from '../../../shared/api/http'
import { useAuthStore } from '../../auth'
import { createDiscordBindingCode, deleteDiscordBinding, getDiscordConfiguration, getDiscordHealth, listDiscordBindings, listDiscordCommands, listDiscordDeliveries, retryDiscordDelivery, saveDiscordConfiguration, testDiscordDelivery, updateDiscordSecret } from '../api/discord'

export type DiscordViewState = 'loading' | 'ready' | 'failed' | 'forbidden' | 'stale'
export type DiscordSectionState = 'loading' | 'ready' | 'empty' | 'unavailable'
export interface DiscordController {
  state: DeepReadonly<ShallowRef<DiscordViewState>>
  configuration: DeepReadonly<ShallowRef<DiscordConfiguration | null>>
  health: DeepReadonly<ShallowRef<DiscordHealth | null>>
  healthState: DeepReadonly<ShallowRef<DiscordSectionState>>
  deliveries: DeepReadonly<ShallowRef<readonly DiscordDelivery[]>>
  deliveryState: DeepReadonly<ShallowRef<DiscordSectionState>>
  bindings: DeepReadonly<ShallowRef<readonly DiscordBinding[]>>
  bindingState: DeepReadonly<ShallowRef<DiscordSectionState>>
  commands: DeepReadonly<ShallowRef<readonly DiscordCommand[]>>
  commandState: DeepReadonly<ShallowRef<DiscordSectionState>>
  isMutating: DeepReadonly<ShallowRef<boolean>>
  errorCode: DeepReadonly<ShallowRef<string | null>>
  lastDelivery: DeepReadonly<ShallowRef<DiscordDelivery | null>>
  bindingCode: DeepReadonly<ShallowRef<DiscordBindingCode | null>>
  refresh: () => Promise<void>
  save: (draft: DiscordConfigurationDraft) => Promise<boolean>
  updateSecret: (secretKey: string, operation: SecretOperation) => Promise<boolean>
  testDelivery: (targetKey: string) => Promise<boolean>
  retryDelivery: (deliveryId: string) => Promise<boolean>
  createBindingCode: (crossplatformId: string) => Promise<boolean>
  removeBinding: (discordSubject: string) => Promise<boolean>
  clearBindingCode: () => void
  dispose: () => void
}

export function useDiscord(options: { onSessionExpired?: () => void } = {}): DiscordController {
  const auth = useAuthStore()
  const state = shallowRef<DiscordViewState>('loading')
  const configuration = shallowRef<DiscordConfiguration | null>(null)
  const health = shallowRef<DiscordHealth | null>(null)
  const healthState = shallowRef<DiscordSectionState>('loading')
  const deliveries = shallowRef<readonly DiscordDelivery[]>(Object.freeze([]))
  const deliveryState = shallowRef<DiscordSectionState>('loading')
  const bindings = shallowRef<readonly DiscordBinding[]>(Object.freeze([]))
  const bindingState = shallowRef<DiscordSectionState>('loading')
  const commands = shallowRef<readonly DiscordCommand[]>(Object.freeze([]))
  const commandState = shallowRef<DiscordSectionState>('loading')
  const isMutating = shallowRef(false)
  const errorCode = shallowRef<string | null>(null)
  const lastDelivery = shallowRef<DiscordDelivery | null>(null)
  const bindingCode = shallowRef<DiscordBindingCode | null>(null)
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
    if (disposed || (error instanceof HttpError && error.code === 'aborted')) return
    errorCode.value = stableErrorCode(error)
    if (error instanceof HttpError && error.status === 401) {
      auth.expireSession()
      options.onSessionExpired?.()
    }
    if (error instanceof HttpError && error.status === 403) {
      configuration.value = null
      state.value = 'forbidden'
      return
    }
    state.value = configuration.value === null ? 'failed' : 'stale'
  }

  async function loadList<T>(
    operation: () => Promise<readonly T[]>,
    target: ShallowRef<readonly T[]>,
    targetState: ShallowRef<DiscordSectionState>,
    current: number,
  ) {
    try {
      const values = await operation()
      if (disposed || current !== requestVersion) return
      target.value = values
      targetState.value = values.length === 0 ? 'empty' : 'ready'
    }
    catch (error) {
      if (disposed || current !== requestVersion || (error instanceof HttpError && error.code === 'aborted')) return
      if (error instanceof HttpError && (error.status === 401 || error.status === 403)) fail(error)
      targetState.value = 'unavailable'
    }
  }

  async function loadHealth(authorizationHeader: string, signal: AbortSignal, current: number) {
    try {
      const next = await getDiscordHealth(authorizationHeader, signal)
      if (disposed || current !== requestVersion) return
      health.value = next
      healthState.value = 'ready'
    }
    catch (error) {
      if (disposed || current !== requestVersion || (error instanceof HttpError && error.code === 'aborted')) return
      if (error instanceof HttpError && (error.status === 401 || error.status === 403)) fail(error)
      health.value = null
      healthState.value = 'unavailable'
    }
  }

  async function refresh() {
    if (disposed) return
    const authorizationHeader = authorization()
    if (authorizationHeader === null) return
    loadController?.abort()
    const current = ++requestVersion
    const controller = new AbortController()
    loadController = controller
    if (configuration.value === null) state.value = 'loading'
    healthState.value = 'loading'
    deliveryState.value = 'loading'
    bindingState.value = 'loading'
    commandState.value = 'loading'
    try {
      const next = await getDiscordConfiguration(authorizationHeader, controller.signal)
      if (disposed || current !== requestVersion) return
      configuration.value = next
      errorCode.value = null
      state.value = 'ready'
      await Promise.all([
        loadHealth(authorizationHeader, controller.signal, current),
        loadList(() => listDiscordDeliveries(authorizationHeader, controller.signal), deliveries, deliveryState, current),
        loadList(() => listDiscordBindings(authorizationHeader, controller.signal), bindings, bindingState, current),
        loadList(() => listDiscordCommands(authorizationHeader, controller.signal), commands, commandState, current),
      ])
    }
    catch (error) {
      if (current === requestVersion) fail(error)
    }
    finally {
      if (current === requestVersion) loadController = null
    }
  }

  async function mutate(operation: (authorizationHeader: string, signal: AbortSignal) => Promise<void>, refreshAfter = false) {
    if (disposed || isMutating.value) return false
    const authorizationHeader = authorization()
    if (authorizationHeader === null) return false
    isMutating.value = true
    errorCode.value = null
    const controller = new AbortController()
    mutationController = controller
    try {
      await operation(authorizationHeader, controller.signal)
      if (disposed) return false
      if (refreshAfter) await refresh()
      return !disposed
    }
    catch (error) {
      fail(error)
      return false
    }
    finally {
      if (mutationController === controller) mutationController = null
      isMutating.value = false
    }
  }

  function save(draft: DiscordConfigurationDraft) {
    return mutate(async (authorizationHeader, signal) => { await saveDiscordConfiguration(authorizationHeader, draft, signal) }, true)
  }
  function updateSecret(secretKey: string, operation: SecretOperation) {
    return mutate(async (authorizationHeader, signal) => { await updateDiscordSecret(authorizationHeader, secretKey, operation, signal) }, operation.operation !== 'Keep')
  }
  function testDelivery(targetKey: string) {
    return mutate(async (authorizationHeader, signal) => { lastDelivery.value = await testDiscordDelivery(authorizationHeader, targetKey.trim(), signal) })
  }
  function retryDelivery(deliveryId: string) {
    return mutate(async (authorizationHeader, signal) => { lastDelivery.value = await retryDiscordDelivery(authorizationHeader, deliveryId, signal) }, true)
  }
  function createBindingCodeForPlayer(crossplatformId: string) {
    return mutate(async (authorizationHeader, signal) => { bindingCode.value = await createDiscordBindingCode(authorizationHeader, crossplatformId.trim(), signal) })
  }
  function removeBinding(discordSubject: string) {
    return mutate(async (authorizationHeader, signal) => { await deleteDiscordBinding(authorizationHeader, discordSubject, signal) }, true)
  }
  function clearBindingCode() { bindingCode.value = null }
  function dispose() {
    if (disposed) return
    disposed = true
    requestVersion++
    loadController?.abort()
    mutationController?.abort()
    loadController = null
    mutationController = null
    bindingCode.value = null
  }

  onMounted(() => void refresh())
  onUnmounted(dispose)
  return {
    state: readonly(state), configuration: readonly(configuration), health: readonly(health), healthState: readonly(healthState),
    deliveries: readonly(deliveries), deliveryState: readonly(deliveryState), bindings: readonly(bindings), bindingState: readonly(bindingState),
    commands: readonly(commands), commandState: readonly(commandState), isMutating: readonly(isMutating), errorCode: readonly(errorCode),
    lastDelivery: readonly(lastDelivery), bindingCode: readonly(bindingCode), refresh, save, updateSecret, testDelivery, retryDelivery,
    createBindingCode: createBindingCodeForPlayer, removeBinding, clearBindingCode, dispose,
  }
}
