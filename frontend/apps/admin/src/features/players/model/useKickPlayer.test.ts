import type { KickPlayerResponse } from '../api/kickPlayer'
import type { OnlinePlayer } from '../api/onlinePlayers'
import type { KickPlayerController } from './useKickPlayer'

import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { defineComponent, isReadonly } from 'vue'

import { HttpError } from '../../../shared/api/http'
import { useKickPlayer } from './useKickPlayer'

interface Deferred<T> {
  promise: Promise<T>
  resolve: (value: T) => void
  reject: (reason: unknown) => void
}

function deferred<T>(): Deferred<T> {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((resolvePromise, rejectPromise) => {
    resolve = resolvePromise
    reject = rejectPromise
  })
  return { promise, resolve, reject }
}

const player: OnlinePlayer = Object.freeze({
  entityId: 7,
  name: 'Ada',
  platformIdentity: Object.freeze({
    combinedId: 'Steam_123',
    platform: 'Steam',
  }),
  crossplatformIdentity: Object.freeze({
    combinedId: 'EOS_456',
    platform: 'EOS',
  }),
  ping: 23,
  level: 17,
  health: 96,
})

const response: KickPlayerResponse = Object.freeze({
  operationId: '8f742dcfe65a454d8f919e164ace77d7',
  status: 'succeeded',
  target: Object.freeze({
    entityId: 7,
    name: 'Ada',
    platformIdentity: player.platformIdentity,
  }),
  requestedAtUtc: '2026-07-22T08:00:00.0000000+00:00',
  completedAtUtc: '2026-07-22T08:00:00.1000000+00:00',
})

function mountComposable(options: Parameters<typeof useKickPlayer>[0]) {
  let controller!: KickPlayerController
  const wrapper = mount(defineComponent({
    setup() {
      controller = useKickPlayer(options)
      return () => null
    },
  }))
  return { controller, wrapper }
}

describe('useKickPlayer', () => {
  it('exposes readonly shallow state and explicit feedback actions', () => {
    const { controller, wrapper } = mountComposable({
      auth: { authorizationHeader: 'Bearer token', expireSession: vi.fn() },
      kick: vi.fn().mockResolvedValue(response),
    })

    expect(isReadonly(controller.isSubmitting)).toBe(true)
    expect(isReadonly(controller.feedback)).toBe(true)
    expect(controller.feedback.value).toBeNull()

    controller.clearFeedback()
    expect(controller.feedback.value).toBeNull()
    wrapper.unmount()
  })

  it('submits only the approved player snapshot fields and returns success', async () => {
    const kick = vi.fn().mockResolvedValue(response)
    const { controller, wrapper } = mountComposable({
      auth: { authorizationHeader: 'Bearer token', expireSession: vi.fn() },
      kick,
    })

    await expect(controller.submit(player, '违反服务器规则')).resolves.toBe(response)

    expect(kick).toHaveBeenCalledOnce()
    expect(kick).toHaveBeenCalledWith('Bearer token', {
      entityId: 7,
      expectedPlatformIdentity: player.platformIdentity,
      reason: '违反服务器规则',
    }, expect.any(AbortSignal))
    expect(controller.isSubmitting.value).toBe(false)
    expect(controller.feedback.value).toBeNull()
    wrapper.unmount()
  })

  it('uses one in-flight request and does not submit a second time', async () => {
    const pending = deferred<KickPlayerResponse>()
    const kick = vi.fn(() => pending.promise)
    const { controller, wrapper } = mountComposable({
      auth: { authorizationHeader: 'Bearer token', expireSession: vi.fn() },
      kick,
    })

    const first = controller.submit(player, 'first reason')
    const second = controller.submit(player, 'second reason')

    expect(kick).toHaveBeenCalledOnce()
    expect(controller.isSubmitting.value).toBe(true)
    pending.resolve(response)
    await expect(Promise.all([first, second])).resolves.toEqual([response, response])
    expect(controller.isSubmitting.value).toBe(false)
    wrapper.unmount()
  })

  it('maps a missing authorization header to an expired session without requesting', async () => {
    const expireSession = vi.fn()
    const onSessionExpired = vi.fn()
    const kick = vi.fn()
    const { controller, wrapper } = mountComposable({
      auth: { authorizationHeader: null, expireSession },
      kick,
      onSessionExpired,
    })

    await expect(controller.submit(player, 'reason')).resolves.toBeNull()

    expect(kick).not.toHaveBeenCalled()
    expect(expireSession).not.toHaveBeenCalled()
    expect(onSessionExpired).toHaveBeenCalledOnce()
    expect(controller.feedback.value).toEqual({
      code: 'session_expired',
      message: '会话已失效，请重新登录',
    })
    wrapper.unmount()
  })

  it('expires the session once on 401 without replaying the request', async () => {
    const expireSession = vi.fn()
    const onSessionExpired = vi.fn()
    const kick = vi.fn().mockRejectedValue(new HttpError('http', 'secret server detail', { status: 401 }))
    const { controller, wrapper } = mountComposable({
      auth: { authorizationHeader: 'Bearer token', expireSession },
      kick,
      onSessionExpired,
    })

    await expect(controller.submit(player, 'reason')).resolves.toBeNull()

    expect(kick).toHaveBeenCalledOnce()
    expect(expireSession).toHaveBeenCalledOnce()
    expect(onSessionExpired).toHaveBeenCalledOnce()
    expect(controller.feedback.value).toEqual({
      code: 'session_expired',
      message: '会话已失效，请重新登录',
    })
    expect(JSON.stringify(controller.feedback.value)).not.toContain('secret server detail')
    wrapper.unmount()
  })

  it.each([
    [new HttpError('http', 'forbidden', { status: 403 }), 'forbidden', '无权踢出玩家'],
    [new HttpError('http', 'offline', { status: 409, problemCode: 'player_not_online' }), 'player_not_online', '玩家已不在线'],
    [new HttpError('http', 'changed', { status: 409, problemCode: 'player_identity_changed' }), 'player_identity_changed', '玩家身份已变化，请刷新后重试'],
    [new HttpError('http', 'busy', { status: 503, problemCode: 'player_action_busy' }), 'player_action_busy', '另一个踢出操作正在进行，请稍后重试'],
    [new HttpError('http', 'not ready', { status: 503, problemCode: 'game_not_ready' }), 'game_not_ready', '游戏服务尚未就绪，请稍后重试'],
    [new HttpError('http', 'timeout', { status: 503, problemCode: 'game_thread_timeout' }), 'game_thread_timeout', '游戏响应超时，请稍后重试'],
    [new HttpError('http', 'audit', { status: 503, problemCode: 'audit_unavailable' }), 'audit_unavailable', '审计服务暂不可用，请稍后重试'],
    [new HttpError('http', 'failed', { status: 500, problemCode: 'player_kick_failed' }), 'player_kick_failed', '踢出玩家失败'],
  ] as const)('maps %s to stable feedback without retrying', async (error, code, message) => {
    const kick = vi.fn().mockRejectedValue(error)
    const { controller, wrapper } = mountComposable({
      auth: { authorizationHeader: 'Bearer token', expireSession: vi.fn() },
      kick,
    })

    await expect(controller.submit(player, 'reason')).resolves.toBeNull()
    await flushPromises()

    expect(kick).toHaveBeenCalledOnce()
    expect(controller.feedback.value).toEqual({ code, message })
    wrapper.unmount()
  })

  it.each([
    new HttpError('network', 'offline'),
    new HttpError('timeout', 'client timeout'),
    new HttpError('http', 'completion unavailable', { status: 503, problemCode: 'audit_completion_unavailable' }),
    new Error('raw failure'),
  ])('maps an indeterminate result to unknown without retrying', async (error) => {
    const kick = vi.fn().mockRejectedValue(error)
    const { controller, wrapper } = mountComposable({
      auth: { authorizationHeader: 'Bearer token', expireSession: vi.fn() },
      kick,
    })

    await expect(controller.submit(player, 'reason')).resolves.toBeNull()
    await flushPromises()

    expect(kick).toHaveBeenCalledOnce()
    expect(controller.feedback.value).toEqual({
      code: 'unknown',
      message: '结果尚无法确认',
    })
    wrapper.unmount()
  })

  it('clears feedback explicitly', async () => {
    const kick = vi.fn().mockRejectedValue(new HttpError('http', 'busy', { status: 503, problemCode: 'player_action_busy' }))
    const { controller, wrapper } = mountComposable({
      auth: { authorizationHeader: 'Bearer token', expireSession: vi.fn() },
      kick,
    })
    await controller.submit(player, 'reason')

    controller.clearFeedback()

    expect(controller.feedback.value).toBeNull()
    wrapper.unmount()
  })

  it('disposes idempotently, aborts the pending wait, and suppresses abort feedback', async () => {
    let requestSignal: AbortSignal | undefined
    const kick = vi.fn((_header: string, _input: unknown, signal?: AbortSignal) => {
      requestSignal = signal
      return new Promise<KickPlayerResponse>((_resolve, reject) => {
        signal?.addEventListener('abort', () => reject(new HttpError('aborted', 'cancelled')), { once: true })
      })
    })
    const { controller, wrapper } = mountComposable({
      auth: { authorizationHeader: 'Bearer token', expireSession: vi.fn() },
      kick,
    })
    const pending = controller.submit(player, 'reason')

    controller.dispose()
    controller.dispose()
    await expect(pending).resolves.toBeNull()

    expect(requestSignal?.aborted).toBe(true)
    expect(controller.isSubmitting.value).toBe(false)
    expect(controller.feedback.value).toBeNull()
    wrapper.unmount()
  })
})
