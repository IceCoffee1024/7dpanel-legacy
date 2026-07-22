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
  message: string
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

const feedbackByProblemCode: Partial<Record<string, KickPlayerFeedback>> = {
  player_not_online: {
    code: 'player_not_online',
    message: '玩家已不在线',
  },
  player_identity_changed: {
    code: 'player_identity_changed',
    message: '玩家身份已变化，请刷新后重试',
  },
  player_action_busy: {
    code: 'player_action_busy',
    message: '另一个踢出操作正在进行，请稍后重试',
  },
  game_not_ready: {
    code: 'game_not_ready',
    message: '游戏服务尚未就绪，请稍后重试',
  },
  game_thread_timeout: {
    code: 'game_thread_timeout',
    message: '游戏响应超时，请稍后重试',
  },
  audit_unavailable: {
    code: 'audit_unavailable',
    message: '审计服务暂不可用，请稍后重试',
  },
  player_kick_failed: {
    code: 'player_kick_failed',
    message: '踢出玩家失败',
  },
}

const sessionExpiredFeedback: KickPlayerFeedback = {
  code: 'session_expired',
  message: '会话已失效，请重新登录',
}

const forbiddenFeedback: KickPlayerFeedback = {
  code: 'forbidden',
  message: '无权踢出玩家',
}

const unknownFeedback: KickPlayerFeedback = {
  code: 'unknown',
  message: '结果尚无法确认',
}

function mapError(error: unknown): KickPlayerFeedback {
  if (!(error instanceof HttpError))
    return unknownFeedback
  if (error.status === 403)
    return forbiddenFeedback
  if (error.problemCode === 'audit_completion_unavailable')
    return unknownFeedback
  if (error.problemCode !== undefined) {
    const stableFeedback = feedbackByProblemCode[error.problemCode]
    if (stableFeedback !== undefined)
      return stableFeedback
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
