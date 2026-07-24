import type { DeepReadonly, ShallowRef } from 'vue'
import type { KickPlayerInput, KickPlayerResponse } from '../api/kickPlayer'
import type { OnlinePlayer } from '../api/onlinePlayers'

import { onUnmounted, readonly, shallowRef } from 'vue'

import { HttpError } from '../../../shared/api/http'
import { useAuthStore } from '../../auth'
import { kickPlayer } from '../api/kickPlayer'

export type KickPlayerFeedbackCode
  = | 'session_expired'
    | 'forbidden'
    | 'player_not_online'
    | 'player_identity_changed'
    | 'player_action_busy'
    | 'game_not_ready'
    | 'game_thread_timeout'
    | 'audit_unavailable'
    | 'player_kick_failed'
    | 'unknown'

export interface KickPlayerFeedback {
  code: KickPlayerFeedbackCode
}

export interface KickPlayerController {
  isSubmitting: DeepReadonly<ShallowRef<boolean>>
  feedback: DeepReadonly<ShallowRef<KickPlayerFeedback | null>>
  submit: (player: OnlinePlayer, reason: string) => Promise<KickPlayerResponse | null>
  clearFeedback: () => void
  dispose: () => void
}

export interface UseKickPlayerOptions {
  auth?: {
    authorizationHeader: string | null
    expireSession: () => void
  }
  kick?: (
    authorizationHeader: string,
    input: KickPlayerInput,
    signal?: AbortSignal,
  ) => Promise<KickPlayerResponse>
  onSessionExpired?: () => void
}

const feedbackByProblemCode: Partial<Record<string, KickPlayerFeedbackCode>> = {
  player_not_online: 'player_not_online',
  player_identity_changed: 'player_identity_changed',
  player_action_busy: 'player_action_busy',
  game_not_ready: 'game_not_ready',
  game_thread_timeout: 'game_thread_timeout',
  audit_unavailable: 'audit_unavailable',
  player_kick_failed: 'player_kick_failed',
}

const sessionExpiredFeedback: KickPlayerFeedback = { code: 'session_expired' }
const forbiddenFeedback: KickPlayerFeedback = { code: 'forbidden' }
const unknownFeedback: KickPlayerFeedback = { code: 'unknown' }

function mapError(error: unknown): KickPlayerFeedback {
  if (!(error instanceof HttpError))
    return unknownFeedback
  if (error.status === 403)
    return forbiddenFeedback
  if (error.problemCode === 'audit_completion_unavailable')
    return unknownFeedback
  if (error.problemCode !== undefined) {
    const stableCode = feedbackByProblemCode[error.problemCode]
    if (stableCode !== undefined)
      return { code: stableCode }
  }
  return unknownFeedback
}

export function useKickPlayer(options: UseKickPlayerOptions = {}): KickPlayerController {
  const auth = options.auth ?? useAuthStore()
  const kick = options.kick ?? kickPlayer
  const onSessionExpired = options.onSessionExpired ?? (() => {})
  const isSubmitting = shallowRef(false)
  const feedback = shallowRef<KickPlayerFeedback | null>(null)
  let inFlight: Promise<KickPlayerResponse | null> | null = null
  let controller: AbortController | null = null
  let disposed = false

  function clearFeedback() {
    feedback.value = null
  }

  function submit(player: OnlinePlayer, reason: string): Promise<KickPlayerResponse | null> {
    if (inFlight !== null)
      return inFlight
    if (disposed)
      return Promise.resolve(null)
    if (auth.authorizationHeader === null) {
      feedback.value = sessionExpiredFeedback
      onSessionExpired()
      return Promise.resolve(null)
    }

    const authorizationHeader = auth.authorizationHeader
    controller = new AbortController()
    isSubmitting.value = true
    feedback.value = null
    const request = kick(authorizationHeader, {
      entityId: player.entityId,
      expectedPlatformIdentity: player.platformIdentity,
      reason,
    }, controller.signal)
    const requestPromise = request.then((result) => {
      if (disposed)
        return null
      return result
    }).catch((error: unknown) => {
      if (disposed || (error instanceof HttpError && error.code === 'aborted'))
        return null
      if (error instanceof HttpError && error.status === 401) {
        auth.expireSession()
        feedback.value = sessionExpiredFeedback
        onSessionExpired()
        return null
      }
      feedback.value = mapError(error)
      return null
    }).finally(() => {
      isSubmitting.value = false
      controller = null
      inFlight = null
    })
    inFlight = requestPromise
    return requestPromise
  }

  function dispose() {
    if (disposed)
      return
    disposed = true
    controller?.abort()
    controller = null
    isSubmitting.value = false
  }

  onUnmounted(dispose)

  return {
    isSubmitting: readonly(isSubmitting),
    feedback: readonly(feedback),
    submit,
    clearFeedback,
    dispose,
  }
}
