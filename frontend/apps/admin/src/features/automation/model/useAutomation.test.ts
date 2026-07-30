import type { App } from 'vue'

import { flushPromises } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp } from 'vue'

import { HttpError } from '../../../shared/api/http'
import { useAutomation } from './useAutomation'

const api = vi.hoisted(() => ({
  deleteAutomationRule: vi.fn(),
  dryRunAutomationRule: vi.fn(),
  getAutomationExecution: vi.fn(),
  listAutomationRules: vi.fn(),
  queryAutomationExecutions: vi.fn(),
  saveAutomationRule: vi.fn(),
  validateAutomationRule: vi.fn(),
}))
const auth = vi.hoisted(() => ({ authorizationHeader: 'Bearer owner' as string | null, expireSession: vi.fn() }))

vi.mock('../api/automation', () => api)
vi.mock('../../auth', () => ({ useAuthStore: () => auth }))

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((resolvePromise) => {
    resolve = resolvePromise
  })
  return { promise, resolve }
}

function rule(id: string) {
  return Object.freeze({ id, version: 1, name: id, isEnabled: true, trigger: { type: 'PlayerJoined' }, condition: { nodeId: 'root', kind: 'Predicate', predicate: { fieldKey: 'actor.group', operator: 'Equals', scalarValue: 'member' } }, actions: [], cooldownSeconds: 0, cooldownScope: 'Rule', concurrencyPolicy: 'SkipIfRunning', failurePolicy: 'StopOnFailure', createdAtUtc: '2026-07-27T00:00:00Z', updatedAtUtc: '2026-07-27T00:00:00Z' })
}

function mountComposable() {
  let result!: ReturnType<typeof useAutomation>
  const app = createApp({
    setup() {
      result = useAutomation()
      return () => null
    },
  })
  app.mount(document.createElement('div'))
  return { app, result }
}

describe('useAutomation', () => {
  const apps: App[] = []

  beforeEach(() => {
    Object.values(api).forEach(mock => mock.mockReset())
    auth.authorizationHeader = 'Bearer owner'
    auth.expireSession.mockReset()
    api.queryAutomationExecutions.mockResolvedValue([])
  })

  afterEach(() => {
    while (apps.length > 0) apps.pop()!.unmount()
  })

  it('ignores an older refresh that completes after a newer response', async () => {
    const first = deferred<readonly ReturnType<typeof rule>[]>()
    const second = deferred<readonly ReturnType<typeof rule>[]>()
    api.listAutomationRules.mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise)
    const mounted = mountComposable()
    apps.push(mounted.app)
    await flushPromises()

    const latest = mounted.result.refresh()
    second.resolve([rule('new')])
    await latest
    first.resolve([rule('old')])
    await flushPromises()

    expect(mounted.result.rules.value.map(item => item.id)).toEqual(['new'])
  })

  it('aborts rule, execution, and dry-run requests when disposed', async () => {
    const signals: AbortSignal[] = []
    const pending = new Promise<never>(() => {})
    api.listAutomationRules.mockImplementation((_auth: string, signal: AbortSignal) => {
      signals.push(signal)
      return pending
    })
    api.queryAutomationExecutions.mockImplementation((_auth: string, signal: AbortSignal) => {
      signals.push(signal)
      return pending
    })
    api.dryRunAutomationRule.mockImplementation((_auth: string, _draft: unknown, _snapshot: unknown, signal: AbortSignal) => {
      signals.push(signal)
      return pending
    })
    const mounted = mountComposable()
    apps.push(mounted.app)
    await flushPromises()
    void mounted.result.dryRun({} as never, {} as never)
    await flushPromises()

    mounted.app.unmount()
    apps.pop()

    expect(signals).toHaveLength(3)
    expect(signals.every(signal => signal.aborted)).toBe(true)
  })

  it('exposes a stable server problem code for forbidden responses', async () => {
    api.listAutomationRules.mockRejectedValue(new HttpError('http', 'forbidden', { status: 403, problemCode: 'owner_required' }))
    const mounted = mountComposable()
    apps.push(mounted.app)
    await flushPromises()

    expect(mounted.result.state.value).toBe('forbidden')
    expect(mounted.result.errorCode.value).toBe('owner_required')
  })
})
