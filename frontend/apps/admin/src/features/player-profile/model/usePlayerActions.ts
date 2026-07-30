import type { DeepReadonly, MaybeRefOrGetter, ShallowRef } from 'vue'
import type {
  ClearPlayerInventoryRequest,
  GrantPlayerItemRequest,
  PlayerActionOperation,
  PlayerActionSubmission,
  PlayerActionTarget,
  RemovePlayerItemRequest,
  ResetPlayerDataRequest,
  ResetPlayerSkillsRequest,
} from '../api/playerActions'

import { computed, onScopeDispose, readonly, shallowRef, toValue } from 'vue'

import { HttpError } from '../../../shared/api/http'
import { useAuthStore } from '../../auth'
import {
  clearPlayerInventory,
  fetchPlayerActionOperation,
  grantPlayerItem,
  removePlayerItem,
  resetPlayerData,
  resetPlayerSkills,
} from '../api/playerActions'

export type PlayerActionStatus = 'Pending' | 'Succeeded' | 'Rejected' | 'Failed' | 'Cancelled' | 'ResultUnknown'

export interface PlayerActionFeedback {
  readonly status: PlayerActionStatus
  readonly operationId: string | null
  readonly failureCode: string | null
  readonly manualVerificationRequired?: boolean
}

type GrantItemInput = Omit<GrantPlayerItemRequest, 'target' | 'clientRequestKey'>
type RemoveItemInput = Omit<RemovePlayerItemRequest, 'target' | 'clientRequestKey'>
type ResetSkillsInput = Omit<ResetPlayerSkillsRequest, 'target' | 'clientRequestKey'>
type ClearInventoryInput = Omit<ClearPlayerInventoryRequest, 'target' | 'clientRequestKey'>
type ResetPlayerDataInput = Omit<ResetPlayerDataRequest, 'target' | 'clientRequestKey'>

interface PlayerActionAuth {
  readonly authorizationHeader: string | null
  expireSession: () => void
}

export interface UsePlayerActionsOptions {
  auth?: PlayerActionAuth
  freshTarget: MaybeRefOrGetter<PlayerActionTarget | null>
  onSessionExpired?: () => void
  pollIntervalMs?: number
  createClientRequestKey?: () => string
}

export interface PlayerActionsController {
  readonly target: DeepReadonly<ShallowRef<PlayerActionTarget | null>>
  readonly targetValid: Readonly<{ value: boolean }>
  readonly isSubmitting: DeepReadonly<ShallowRef<boolean>>
  readonly feedback: DeepReadonly<ShallowRef<PlayerActionFeedback | null>>
  lockTarget: () => void
  clearTarget: () => void
  clearFeedback: () => void
  grantItem: (input: GrantItemInput) => Promise<void>
  removeItem: (input: RemoveItemInput) => Promise<void>
  resetSkills: (input: ResetSkillsInput) => Promise<void>
  clearInventory: (input: ClearInventoryInput) => Promise<void>
  resetPlayerData: (input: ResetPlayerDataInput) => Promise<void>
  dispose: () => void
}

function sameTarget(left: PlayerActionTarget | null, right: PlayerActionTarget | null): boolean {
  return left !== null && right !== null
    && left.crossplatformId === right.crossplatformId
    && left.entityId === right.entityId
    && left.onlineObservedAtUtc === right.onlineObservedAtUtc
    && left.worldId === right.worldId
}

function isTerminal(status: PlayerActionStatus): boolean {
  return status !== 'Pending'
}

function feedbackFrom(value: PlayerActionSubmission | PlayerActionOperation): PlayerActionFeedback {
  return Object.freeze({
    status: value.status,
    operationId: value.operationId,
    failureCode: value.failureCode ?? null,
    manualVerificationRequired: value.status === 'ResultUnknown'
      || ('manualVerificationRequired' in value && value.manualVerificationRequired === true),
  })
}

function defaultRequestKey(): string {
  return globalThis.crypto.randomUUID()
}

export function usePlayerActions(options: UsePlayerActionsOptions): PlayerActionsController {
  const auth = options.auth ?? useAuthStore()
  const pollIntervalMs = options.pollIntervalMs ?? 1_000
  const createClientRequestKey = options.createClientRequestKey ?? defaultRequestKey
  const target = shallowRef<PlayerActionTarget | null>(null)
  const isSubmitting = shallowRef(false)
  const feedback = shallowRef<PlayerActionFeedback | null>(null)
  const targetValid = computed(() => sameTarget(target.value, toValue(options.freshTarget)))
  let controller: AbortController | null = null
  let version = 0
  let disposed = false
  let sessionExpiryNotified = false

  function expireSession() {
    auth.expireSession()
    if (!sessionExpiryNotified) {
      sessionExpiryNotified = true
      options.onSessionExpired?.()
    }
  }

  function lockTarget() {
    const current = toValue(options.freshTarget)
    target.value = current === null ? null : Object.freeze({ ...current })
  }

  function clearTarget() {
    target.value = null
  }

  function clearFeedback() {
    feedback.value = null
  }

  async function poll(operationId: string, requestVersion: number, signal: AbortSignal): Promise<void> {
    while (true) {
      if (disposed || requestVersion !== version || signal.aborted)
        return

      await new Promise(resolve => setTimeout(resolve, pollIntervalMs))
      if (disposed || requestVersion !== version || signal.aborted)
        return
      const authorizationHeader = auth.authorizationHeader
      if (authorizationHeader === null) {
        expireSession()
        return
      }
      const operation = await fetchPlayerActionOperation(authorizationHeader, operationId, signal)
      if (disposed || requestVersion !== version)
        return
      feedback.value = feedbackFrom(operation)
      if (isTerminal(operation.status))
        return
    }
  }

  async function submit<TInput>(
    input: TInput,
    request: (
      authorizationHeader: string,
      value: TInput & { readonly target: PlayerActionTarget, readonly clientRequestKey: string },
      signal: AbortSignal,
    ) => Promise<PlayerActionSubmission>,
  ): Promise<void> {
    if (disposed || isSubmitting.value)
      return
    const fixedTarget = target.value
    if (fixedTarget === null || !targetValid.value) {
      feedback.value = Object.freeze({ status: 'Rejected', operationId: null, failureCode: 'fixed_target_stale' })
      return
    }
    const authorizationHeader = auth.authorizationHeader
    if (authorizationHeader === null) {
      expireSession()
      feedback.value = Object.freeze({ status: 'Failed', operationId: null, failureCode: 'authentication_required' })
      return
    }

    controller?.abort()
    const nextController = new AbortController()
    controller = nextController
    const requestVersion = ++version
    isSubmitting.value = true
    feedback.value = null
    try {
      const result = await request(authorizationHeader, {
        ...input,
        target: fixedTarget,
        clientRequestKey: createClientRequestKey(),
      }, nextController.signal)
      if (disposed || requestVersion !== version)
        return
      feedback.value = feedbackFrom(result)
      sessionExpiryNotified = false
      if (!isTerminal(result.status))
        await poll(result.operationId, requestVersion, nextController.signal)
    }
    catch (error) {
      if (disposed || requestVersion !== version || (error instanceof HttpError && error.code === 'aborted'))
        return
      if (error instanceof HttpError && error.status === 401)
        expireSession()
      feedback.value = Object.freeze({
        status: 'Failed',
        operationId: feedback.value?.operationId ?? null,
        failureCode: error instanceof HttpError ? (error.problemCode ?? error.code) : 'protocol_error',
      })
    }
    finally {
      if (requestVersion === version) {
        controller = null
        isSubmitting.value = false
      }
    }
  }

  function dispose() {
    if (disposed)
      return
    disposed = true
    version++
    controller?.abort()
    controller = null
    isSubmitting.value = false
  }

  onScopeDispose(dispose, true)

  return {
    target: readonly(target),
    targetValid,
    isSubmitting: readonly(isSubmitting),
    feedback: readonly(feedback),
    lockTarget,
    clearTarget,
    clearFeedback,
    grantItem: input => submit(input, grantPlayerItem),
    removeItem: input => submit(input, removePlayerItem),
    resetSkills: input => submit(input, resetPlayerSkills),
    clearInventory: input => submit(input, clearPlayerInventory),
    resetPlayerData: input => submit(input, resetPlayerData),
    dispose,
  }
}
