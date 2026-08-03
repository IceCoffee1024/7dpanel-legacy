import type { RestartServerAccepted } from '../api/serverOperations'
import { PiniaColada } from '@pinia/colada'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { defineComponent, reactive } from 'vue'

import { configureGeneratedClient } from '../../../shared/api/generatedClient'
import { HttpError } from '../../../shared/api/http'
import { useRestartServer } from './useRestartServer'

const accepted: RestartServerAccepted = {
  operationId: 'restart-1',
  code: 'restart_script_started',
  requestedAtUtc: '2026-07-25T01:02:03Z',
  scriptStartedAtUtc: '2026-07-25T01:02:04Z',
  auditStatus: 'recorded',
}

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((resolvePromise) => {
    resolve = resolvePromise
  })
  return { promise, resolve }
}

describe('useRestartServer', () => {
  afterEach(() => vi.restoreAllMocks())

  function mountRestart(submit = vi.fn().mockResolvedValue(accepted)) {
    const auth = { authorizationHeader: 'Bearer owner' as string | null, expireSession: vi.fn() }
    const onSessionExpired = vi.fn()
    let operation!: ReturnType<typeof useRestartServer>
    const Host = defineComponent({
      setup() {
        operation = useRestartServer({ auth, onSessionExpired, restartServer: submit })
        return () => null
      },
    })
    return { auth, onSessionExpired, operation: () => operation, submit, wrapper: mount(Host) }
  }

  it('moves through idle, confirming, submitting and accepted', async () => {
    const mounted = mountRestart()

    expect(mounted.operation().state.value).toBe('idle')
    mounted.operation().startConfirmation()
    expect(mounted.operation().state.value).toBe('confirming')

    const result = await mounted.operation().confirm()

    expect(result).toBe(accepted)
    expect(mounted.operation().state.value).toBe('accepted')
    expect(mounted.operation().result.value).toEqual(accepted)
    expect(mounted.operation().error.value).toBeNull()
    expect(mounted.submit).toHaveBeenCalledOnce()
    mounted.wrapper.unmount()
  })

  it('uses the generated Colada mutation for the production request path', async () => {
    const fetchMock = vi.fn().mockResolvedValue(Response.json(accepted, { status: 202 })) as typeof fetch
    const auth = { authorizationHeader: 'Bearer generated-owner' as string | null, expireSession: vi.fn() }
    configureGeneratedClient({
      fetch: fetchMock,
      getAuthorizationHeader: () => auth.authorizationHeader,
      origin: location.origin,
    })
    let operation!: ReturnType<typeof useRestartServer>
    const Host = defineComponent({
      setup() {
        operation = useRestartServer({ auth })
        return () => null
      },
    })
    const wrapper = mount(Host, {
      global: { plugins: [createPinia(), PiniaColada] },
    })

    operation.startConfirmation()
    await expect(operation.confirm()).resolves.toEqual(accepted)

    const request = vi.mocked(fetchMock).mock.calls[0]?.[0] as Request
    expect(request.headers.get('Authorization')).toBe('Bearer generated-owner')
    expect(await request.json()).toEqual({ confirmed: true })
    expect(operation.state.value).toBe('accepted')
    wrapper.unmount()
  })

  it('cancels only confirmation and never submits', async () => {
    const mounted = mountRestart()
    mounted.operation().startConfirmation()
    mounted.operation().cancelConfirmation()

    expect(mounted.operation().state.value).toBe('idle')
    await expect(mounted.operation().confirm()).resolves.toBeNull()
    expect(mounted.submit).not.toHaveBeenCalled()
    mounted.wrapper.unmount()
  })

  it('locks duplicate submission to one in-flight request', async () => {
    const pending = deferred<RestartServerAccepted>()
    const mounted = mountRestart(vi.fn().mockReturnValue(pending.promise))
    mounted.operation().startConfirmation()

    const first = mounted.operation().confirm()
    const second = mounted.operation().confirm()

    expect(first).toBe(second)
    expect(mounted.operation().state.value).toBe('submitting')
    expect(mounted.submit).toHaveBeenCalledOnce()
    pending.resolve(accepted)
    await first
    mounted.wrapper.unmount()
  })

  it('enters failed with a stable code and can retry after confirming again', async () => {
    const submit = vi.fn()
      .mockRejectedValueOnce(new HttpError('http', 'sensitive', { problemCode: 'restart_script_missing', status: 503 }))
      .mockResolvedValueOnce(accepted)
    const mounted = mountRestart(submit)
    mounted.operation().startConfirmation()

    await mounted.operation().confirm()
    expect(mounted.operation().state.value).toBe('failed')
    expect(mounted.operation().error.value).toEqual({ code: 'restart_script_missing' })

    mounted.operation().startConfirmation()
    await mounted.operation().confirm()
    expect(mounted.operation().state.value).toBe('accepted')
    expect(submit).toHaveBeenCalledTimes(2)
    mounted.wrapper.unmount()
  })

  it('expires the session once on 401 without leaking detail', async () => {
    const mounted = mountRestart(vi.fn().mockRejectedValue(
      new HttpError('http', 'secret backend detail', { problemCode: 'authentication_required', status: 401 }),
    ))
    mounted.operation().startConfirmation()

    await mounted.operation().confirm()

    expect(mounted.auth.expireSession).toHaveBeenCalledOnce()
    expect(mounted.onSessionExpired).toHaveBeenCalledOnce()
    expect(mounted.operation().error.value).toEqual({ code: 'session_expired' })
    expect(JSON.stringify(mounted.operation().error.value)).not.toContain('secret')
    mounted.wrapper.unmount()
  })

  it('aborts an in-flight submission on unmount without entering failed', async () => {
    const pending = deferred<RestartServerAccepted>()
    const mounted = mountRestart(vi.fn().mockReturnValue(pending.promise))
    mounted.operation().startConfirmation()
    void mounted.operation().confirm()
    const signal = mounted.submit.mock.calls[0]?.[1] as AbortSignal

    mounted.wrapper.unmount()
    pending.resolve(accepted)
    await flushPromises()

    expect(signal.aborted).toBe(true)
    expect(mounted.operation().state.value).not.toBe('failed')
  })

  it.each(['succeeded', 'failed', 'cancelled', 'result-unknown'] as const)(
    'retains the accepted ID in controlled query state and restores the %s terminal result',
    async (status) => {
      const auth = { authorizationHeader: 'Bearer owner' as string | null, expireSession: vi.fn() }
      const route = reactive({ query: {} as Record<string, string> })
      const replace = vi.fn(async (location: { query: Record<string, string> }) => {
        Object.assign(route.query, location.query)
      })
      const getOperation = vi.fn().mockResolvedValue({
        operationId: accepted.operationId,
        kind: 'restart_script' as const,
        status,
        requestedAtUtc: accepted.requestedAtUtc,
        startedAtUtc: accepted.scriptStartedAtUtc,
        completedAtUtc: '2026-07-25T01:02:05Z',
        completionDeadlineUtc: '2026-07-25T01:07:04Z',
        failureCode: status === 'succeeded' ? null : 'restart_script_start_failed',
        auditStatus: 'recorded' as const,
      })
      let operation!: ReturnType<typeof useRestartServer>
      const Host = defineComponent({
        setup() {
          operation = useRestartServer({
            auth,
            restartServer: vi.fn().mockResolvedValue(accepted),
            getOperation,
            route,
            router: { replace } as never,
          })
          return () => null
        },
      })
      const wrapper = mount(Host)

      operation.startConfirmation()
      await operation.confirm()
      await flushPromises()

      expect(replace).toHaveBeenCalledWith({
        query: { operationId: accepted.operationId, operationKind: 'restart_script' },
      })
      expect(getOperation).toHaveBeenCalledWith('Bearer owner', accepted.operationId, expect.any(AbortSignal))
      expect(operation.state.value).toBe(status)
      wrapper.unmount()
    },
  )

  it('keeps polling after a short transient disconnect without cancelling the operation', async () => {
    vi.useFakeTimers()
    const auth = { authorizationHeader: 'Bearer owner' as string | null, expireSession: vi.fn() }
    const route = reactive({ query: { operationId: accepted.operationId, operationKind: 'restart_script' } })
    const getOperation = vi.fn()
      .mockRejectedValueOnce(new HttpError('http', 'temporarily disconnected', { status: 503 }))
      .mockResolvedValueOnce({
        operationId: accepted.operationId,
        kind: 'restart_script' as const,
        status: 'succeeded' as const,
        requestedAtUtc: accepted.requestedAtUtc,
        startedAtUtc: accepted.scriptStartedAtUtc,
        completedAtUtc: '2026-07-25T01:02:05Z',
        completionDeadlineUtc: '2026-07-25T01:07:04Z',
        failureCode: null,
        auditStatus: 'recorded' as const,
      })
    let operation!: ReturnType<typeof useRestartServer>
    const wrapper = mount(defineComponent({
      setup() {
        operation = useRestartServer({ auth, getOperation, route })
        return () => null
      },
    }))

    await flushPromises()
    await vi.advanceTimersByTimeAsync(1000)

    expect(getOperation).toHaveBeenCalledTimes(2)
    expect(operation.state.value).toBe('succeeded')
    wrapper.unmount()
    vi.useRealTimers()
  })
})
