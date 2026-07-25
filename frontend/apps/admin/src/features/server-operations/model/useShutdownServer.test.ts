import type { ShutdownServerAccepted } from '../api/serverOperations'
import { mount } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { defineComponent } from 'vue'

import { HttpError } from '../../../shared/api/http'
import { useRestartServer } from './useRestartServer'
import { useShutdownServer } from './useShutdownServer'

const shutdownAccepted: ShutdownServerAccepted = {
  operationId: 'shutdown-1',
  code: 'shutdown_requested',
  requestedAtUtc: '2026-07-25T01:02:03Z',
  acceptedAtUtc: '2026-07-25T01:02:04Z',
  auditStatus: 'recorded',
}

describe('useShutdownServer', () => {
  afterEach(() => vi.restoreAllMocks())

  function mountShutdown(submit = vi.fn().mockResolvedValue(shutdownAccepted)) {
    const auth = { authorizationHeader: 'Bearer owner' as string | null, expireSession: vi.fn() }
    let operation!: ReturnType<typeof useShutdownServer>
    const Host = defineComponent({
      setup() {
        operation = useShutdownServer({ auth, shutdownServer: submit })
        return () => null
      },
    })
    return { auth, operation: () => operation, submit, wrapper: mount(Host) }
  }

  it('uses its own confirmation and shutdown accepted state', async () => {
    const mounted = mountShutdown()
    mounted.operation().startConfirmation()

    await expect(mounted.operation().confirm()).resolves.toBe(shutdownAccepted)

    expect(mounted.operation().state.value).toBe('accepted')
    expect(mounted.operation().result.value?.code).toBe('shutdown_requested')
    expect(mounted.operation().result.value).not.toHaveProperty('scriptStartedAtUtc')
    mounted.wrapper.unmount()
  })

  it('locks duplicate submit and permits retry after a shutdown-specific failure', async () => {
    const submit = vi.fn()
      .mockRejectedValueOnce(new HttpError('http', 'safe', { problemCode: 'shutdown_timeout', status: 503 }))
      .mockResolvedValueOnce(shutdownAccepted)
    const mounted = mountShutdown(submit)
    mounted.operation().startConfirmation()

    const first = mounted.operation().confirm()
    const duplicate = mounted.operation().confirm()
    expect(first).toBe(duplicate)
    expect(submit).toHaveBeenCalledOnce()
    await first
    expect(mounted.operation().state.value).toBe('failed')
    expect(mounted.operation().error.value).toEqual({ code: 'shutdown_timeout' })

    mounted.operation().startConfirmation()
    await mounted.operation().confirm()
    expect(mounted.operation().state.value).toBe('accepted')
    mounted.wrapper.unmount()
  })

  it('does not reuse restart success or failure semantics', async () => {
    const mounted = mountShutdown(vi.fn().mockRejectedValue(
      new HttpError('http', 'safe', { problemCode: 'restart_script_start_failed', status: 503 }),
    ))
    mounted.operation().startConfirmation()
    await mounted.operation().confirm()

    expect(mounted.operation().state.value).toBe('failed')
    expect(mounted.operation().error.value).toEqual({ code: 'unknown' })
    mounted.wrapper.unmount()
  })

  it('keeps restart and shutdown state machines independent', async () => {
    let restart!: ReturnType<typeof useRestartServer>
    let shutdown!: ReturnType<typeof useShutdownServer>
    const auth = { authorizationHeader: 'Bearer owner' as string | null, expireSession: vi.fn() }
    const restartRequest = vi.fn().mockResolvedValue({
      operationId: 'restart-1',
      code: 'restart_script_started' as const,
      requestedAtUtc: '2026-07-25T01:02:03Z',
      scriptStartedAtUtc: '2026-07-25T01:02:04Z',
      auditStatus: 'recorded' as const,
    })
    const shutdownRequest = vi.fn().mockResolvedValue(shutdownAccepted)
    const Host = defineComponent({
      setup() {
        restart = useRestartServer({ auth, restartServer: restartRequest })
        shutdown = useShutdownServer({ auth, shutdownServer: shutdownRequest })
        return () => null
      },
    })
    const wrapper = mount(Host)

    restart.startConfirmation()
    expect(restart.state.value).toBe('confirming')
    expect(shutdown.state.value).toBe('idle')
    await restart.confirm()
    expect(restart.state.value).toBe('accepted')
    expect(shutdown.state.value).toBe('idle')
    expect(shutdownRequest).not.toHaveBeenCalled()

    shutdown.startConfirmation()
    await shutdown.confirm()
    expect(shutdown.state.value).toBe('accepted')
    expect(restart.state.value).toBe('accepted')
    wrapper.unmount()
  })
})
