import type { DeepReadonly, ShallowRef } from 'vue'
import type { SendChatInput, SendChatMessage } from '../api/chat'

import { useMutation } from '@pinia/colada'
import { onUnmounted, readonly, shallowRef } from 'vue'

import { HttpError } from '../../../shared/api/http'
import { useAuthStore } from '../../auth'
import { sendChatMessage } from '../api/chat'

export type SendChatErrorCode
  = | 'session_expired'
    | 'forbidden'
    | 'disabled'
    | 'not_ready'
    | 'queue_full'
    | 'target_offline'
    | 'cancelled'
    | 'unknown'

export interface SendChatError { code: SendChatErrorCode }

export interface SendChatController {
  draft: DeepReadonly<ShallowRef<string>>
  targetCrossplatformId: DeepReadonly<ShallowRef<string | null>>
  isSubmitting: DeepReadonly<ShallowRef<boolean>>
  error: DeepReadonly<ShallowRef<SendChatError | null>>
  sendHistory: DeepReadonly<ShallowRef<readonly string[]>>
  setDraft: (value: string) => void
  setTarget: (crossplatformId: string) => void
  clearTarget: () => void
  navigateHistory: (direction: -1 | 1) => void
  submit: () => Promise<boolean>
}

export interface UseSendChatOptions {
  auth?: { authorizationHeader: string | null, expireSession: () => void }
  send?: SendChatMessage
}

const historyCapacity = 50
const problemCodes: Record<string, SendChatErrorCode> = {
  chat_disabled: 'disabled',
  chat_not_ready: 'not_ready',
  chat_queue_full: 'queue_full',
  chat_target_offline: 'target_offline',
  chat_cancelled: 'cancelled',
}

function mapError(cause: unknown): SendChatErrorCode {
  if (!(cause instanceof HttpError))
    return 'unknown'
  if (cause.status === 401)
    return 'session_expired'
  if (cause.status === 403)
    return 'forbidden'
  return cause.problemCode === undefined ? 'unknown' : (problemCodes[cause.problemCode] ?? 'unknown')
}

export function useSendChat(options: UseSendChatOptions = {}): SendChatController {
  const auth = options.auth ?? useAuthStore()
  const request = options.send ?? sendChatMessage
  const draft = shallowRef('')
  const targetCrossplatformId = shallowRef<string | null>(null)
  const isSubmitting = shallowRef(false)
  const error = shallowRef<SendChatError | null>(null)
  const sendHistory = shallowRef<readonly string[]>(Object.freeze([]))
  let historyIndex = 0
  let disposed = false
  let controller: AbortController | null = null

  const mutation = useMutation<void, { authorization: string, input: SendChatInput, signal: AbortSignal }, unknown>({
    mutation: variables => request(variables.authorization, variables.input, variables.signal),
  })

  function setDraft(value: string): void {
    draft.value = value
    historyIndex = sendHistory.value.length
    error.value = null
  }

  function setTarget(crossplatformId: string): void {
    targetCrossplatformId.value = crossplatformId
    error.value = null
  }

  function clearTarget(): void {
    targetCrossplatformId.value = null
  }

  function navigateHistory(direction: -1 | 1): void {
    const history = sendHistory.value
    if (history.length === 0)
      return
    historyIndex = Math.min(history.length, Math.max(0, historyIndex + direction))
    draft.value = historyIndex === history.length ? '' : history[historyIndex]!
  }

  async function submit(): Promise<boolean> {
    if (disposed || isSubmitting.value)
      return false
    const message = draft.value.trim()
    if (message.length === 0 || message.length > 500)
      return false
    const authorization = auth.authorizationHeader
    if (authorization === null) {
      auth.expireSession()
      error.value = Object.freeze({ code: 'session_expired' })
      return false
    }

    const currentController = new AbortController()
    controller = currentController
    isSubmitting.value = true
    error.value = null
    try {
      await mutation.mutateAsync({
        authorization,
        input: { message, targetCrossplatformId: targetCrossplatformId.value },
        signal: currentController.signal,
      })
      if (disposed)
        return false
      sendHistory.value = Object.freeze([...sendHistory.value, message].slice(-historyCapacity))
      historyIndex = sendHistory.value.length
      draft.value = ''
      targetCrossplatformId.value = null
      return true
    }
    catch (cause) {
      if (disposed || currentController.signal.aborted)
        return false
      const code = mapError(cause)
      if (code === 'session_expired')
        auth.expireSession()
      error.value = Object.freeze({ code })
      return false
    }
    finally {
      if (controller === currentController)
        controller = null
      if (!disposed)
        isSubmitting.value = false
    }
  }

  onUnmounted(() => {
    disposed = true
    controller?.abort()
    controller = null
  })

  return {
    draft: readonly(draft),
    targetCrossplatformId: readonly(targetCrossplatformId),
    isSubmitting: readonly(isSubmitting),
    error: readonly(error),
    sendHistory: readonly(sendHistory),
    setDraft,
    setTarget,
    clearTarget,
    navigateHistory,
    submit,
  }
}
