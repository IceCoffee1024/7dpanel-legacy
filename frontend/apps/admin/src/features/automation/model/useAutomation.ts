import type { DeepReadonly, ShallowRef } from 'vue'
import type { AutomationDryRunResult, AutomationExecution, AutomationRule, AutomationRuleDraft, AutomationTriggerSnapshot, AutomationValidation } from '../api/automation'

import { onMounted, onUnmounted, readonly, shallowRef } from 'vue'
import { HttpError } from '../../../shared/api/http'
import { useAuthStore } from '../../auth'
import { deleteAutomationRule, dryRunAutomationRule, getAutomationExecution, listAutomationRules, queryAutomationExecutions, saveAutomationRule, validateAutomationRule } from '../api/automation'

export type AutomationViewState = 'loading' | 'ready' | 'empty' | 'failed' | 'forbidden' | 'stale'
export type AutomationExecutionState = 'loading' | 'available' | 'unavailable'
export type AutomationExecutionDetailState = 'idle' | 'loading' | 'ready' | 'unavailable'

export interface AutomationController {
  state: DeepReadonly<ShallowRef<AutomationViewState>>
  rules: DeepReadonly<ShallowRef<readonly AutomationRule[]>>
  selected: DeepReadonly<ShallowRef<AutomationRule | null>>
  executions: DeepReadonly<ShallowRef<readonly AutomationExecution[]>>
  selectedExecution: DeepReadonly<ShallowRef<AutomationExecution | null>>
  executionState: DeepReadonly<ShallowRef<AutomationExecutionState>>
  executionDetailState: DeepReadonly<ShallowRef<AutomationExecutionDetailState>>
  isMutating: DeepReadonly<ShallowRef<boolean>>
  errorCode: DeepReadonly<ShallowRef<string | null>>
  validation: DeepReadonly<ShallowRef<AutomationValidation | null>>
  dryRunResult: DeepReadonly<ShallowRef<AutomationDryRunResult | null>>
  select: (rule: AutomationRule | null) => void
  refresh: () => Promise<void>
  loadExecution: (executionId: string) => Promise<void>
  save: (draft: AutomationRuleDraft) => Promise<boolean>
  remove: (rule: AutomationRule) => Promise<boolean>
  validate: (draft: AutomationRuleDraft) => Promise<boolean>
  dryRun: (draft: AutomationRuleDraft, snapshot: AutomationTriggerSnapshot) => Promise<boolean>
  dispose: () => void
}

export function useAutomation(options: { onSessionExpired?: () => void } = {}): AutomationController {
  const auth = useAuthStore()
  const state = shallowRef<AutomationViewState>('loading')
  const rules = shallowRef<readonly AutomationRule[]>(Object.freeze([]))
  const selected = shallowRef<AutomationRule | null>(null)
  const executions = shallowRef<readonly AutomationExecution[]>(Object.freeze([]))
  const selectedExecution = shallowRef<AutomationExecution | null>(null)
  const executionState = shallowRef<AutomationExecutionState>('loading')
  const executionDetailState = shallowRef<AutomationExecutionDetailState>('idle')
  const isMutating = shallowRef(false)
  const errorCode = shallowRef<string | null>(null)
  const validation = shallowRef<AutomationValidation | null>(null)
  const dryRunResult = shallowRef<AutomationDryRunResult | null>(null)
  let loadController: AbortController | null = null
  let detailController: AbortController | null = null
  let mutationController: AbortController | null = null
  let requestVersion = 0
  let detailVersion = 0
  let disposed = false

  function authorization(): string | null {
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
      rules.value = Object.freeze([])
      state.value = 'forbidden'
      return
    }
    state.value = rules.value.length === 0 ? 'failed' : 'stale'
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
    if (rules.value.length === 0)
      state.value = 'loading'
    executionState.value = 'loading'

    const executionPromise = queryAutomationExecutions(authorizationHeader, controller.signal)
      .then((next) => {
        if (disposed || current !== requestVersion)
          return
        executions.value = next
        executionState.value = 'available'
      })
      .catch((error: unknown) => {
        if (disposed || current !== requestVersion || (error instanceof HttpError && error.code === 'aborted'))
          return
        if (error instanceof HttpError && (error.status === 401 || error.status === 403))
          fail(error)
        executionState.value = 'unavailable'
      })

    try {
      const next = await listAutomationRules(authorizationHeader, controller.signal)
      if (disposed || current !== requestVersion)
        return
      rules.value = next
      selected.value = selected.value === null ? null : next.find(rule => rule.id === selected.value?.id) ?? null
      errorCode.value = null
      state.value = next.length === 0 ? 'empty' : 'ready'
      await executionPromise
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

  async function loadExecution(executionId: string) {
    if (disposed)
      return
    const authorizationHeader = authorization()
    if (authorizationHeader === null)
      return
    detailController?.abort()
    const current = ++detailVersion
    const controller = new AbortController()
    detailController = controller
    executionDetailState.value = 'loading'
    try {
      const next = await getAutomationExecution(authorizationHeader, executionId, controller.signal)
      if (disposed || current !== detailVersion)
        return
      selectedExecution.value = next
      executionDetailState.value = 'ready'
    }
    catch (error) {
      if (disposed || current !== detailVersion || (error instanceof HttpError && error.code === 'aborted'))
        return
      errorCode.value = stableErrorCode(error)
      executionDetailState.value = 'unavailable'
    }
    finally {
      if (current === detailVersion)
        detailController = null
    }
  }

  async function mutate(operation: (authorizationHeader: string, signal: AbortSignal) => Promise<unknown>, refreshAfter = false) {
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

  function save(draft: AutomationRuleDraft) {
    return mutate((authorizationHeader, signal) => saveAutomationRule(authorizationHeader, draft, signal), true)
  }
  function remove(rule: AutomationRule) {
    return mutate((authorizationHeader, signal) => deleteAutomationRule(authorizationHeader, rule, signal), true)
  }
  function validate(draft: AutomationRuleDraft) {
    return mutate(async (authorizationHeader, signal) => {
      validation.value = await validateAutomationRule(authorizationHeader, draft, signal)
    })
  }
  function dryRun(draft: AutomationRuleDraft, snapshot: AutomationTriggerSnapshot) {
    return mutate(async (authorizationHeader, signal) => {
      dryRunResult.value = await dryRunAutomationRule(authorizationHeader, draft, snapshot, signal)
    })
  }
  function select(rule: AutomationRule | null) {
    selected.value = rule
    validation.value = null
    dryRunResult.value = null
  }
  function dispose() {
    if (disposed)
      return
    disposed = true
    requestVersion++
    detailVersion++
    loadController?.abort()
    detailController?.abort()
    mutationController?.abort()
    loadController = null
    detailController = null
    mutationController = null
  }

  onMounted(() => void refresh())
  onUnmounted(dispose)
  return {
    state: readonly(state),
    rules: readonly(rules),
    selected: readonly(selected),
    executions: readonly(executions),
    selectedExecution: readonly(selectedExecution),
    executionState: readonly(executionState),
    executionDetailState: readonly(executionDetailState),
    isMutating: readonly(isMutating),
    errorCode: readonly(errorCode),
    validation: readonly(validation),
    dryRunResult: readonly(dryRunResult),
    select,
    refresh,
    loadExecution,
    save,
    remove,
    validate,
    dryRun,
    dispose,
  }
}
