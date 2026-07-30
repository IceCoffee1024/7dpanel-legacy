import type { App } from 'vue'

import { flushPromises } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp } from 'vue'

import { HttpError } from '../../../shared/api/http'
import { useDiscord } from './useDiscord'

const api = vi.hoisted(() => ({
  createDiscordBindingCode: vi.fn(),
  deleteDiscordBinding: vi.fn(),
  getDiscordConfiguration: vi.fn(),
  getDiscordHealth: vi.fn(),
  listDiscordBindings: vi.fn(),
  listDiscordCommands: vi.fn(),
  listDiscordDeliveries: vi.fn(),
  retryDiscordDelivery: vi.fn(),
  saveDiscordConfiguration: vi.fn(),
  testDiscordDelivery: vi.fn(),
  updateDiscordSecret: vi.fn(),
}))
const auth = vi.hoisted(() => ({ authorizationHeader: 'Bearer owner' as string | null, expireSession: vi.fn() }))

vi.mock('../api/discord', () => api)
vi.mock('../../auth', () => ({ useAuthStore: () => auth }))

const configuration = Object.freeze({
  version: 1,
  isEnabled: true,
  mode: 'Bot',
  applicationId: 'app',
  guildId: 'guild',
  publicChannelId: 'channel',
  bridgeGameToDiscord: true,
  bridgeDiscordToGame: true,
  proxy: { isEnabled: false, endpoint: null, hasCredentials: false },
  hasBotToken: true,
  targets: [],
  updatedAtUtc: '2026-07-27T00:00:00Z',
})
const delivery = (id: string) => Object.freeze({ deliveryId: id, businessKey: id, targetKey: 'public', status: 'Pending', nextAttemptAtUtc: null, retryCount: 0, createdAtUtc: '2026-07-27T00:00:00Z', completedAtUtc: null })

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => {
    resolve = done
  })
  return { promise, resolve }
}
function mountComposable() {
  let result!: ReturnType<typeof useDiscord>
  const app = createApp({ setup() {
    result = useDiscord()
    return () => null
  } })
  app.mount(document.createElement('div'))
  return { app, result }
}

describe('useDiscord', () => {
  const apps: App[] = []
  beforeEach(() => {
    Object.values(api).forEach(mock => mock.mockReset())
    auth.authorizationHeader = 'Bearer owner'
    auth.expireSession.mockReset()
    api.getDiscordConfiguration.mockResolvedValue(configuration)
    api.getDiscordHealth.mockResolvedValue({ gateway: { state: 'Connected', errorCode: null, observedAtUtc: null }, inbound: { state: 'Healthy', errorCode: null, observedAtUtc: null } })
    api.listDiscordBindings.mockResolvedValue([])
    api.listDiscordCommands.mockResolvedValue([])
    api.listDiscordDeliveries.mockResolvedValue([])
  })
  afterEach(() => {
    while (apps.length > 0) apps.pop()!.unmount()
  })

  it('does not let a late optional response overwrite newer delivery data', async () => {
    const old = deferred<readonly ReturnType<typeof delivery>[]>()
    api.listDiscordDeliveries.mockReturnValueOnce(old.promise).mockResolvedValueOnce([delivery('new')])
    const mounted = mountComposable()
    apps.push(mounted.app)
    await flushPromises()

    await mounted.result.refresh()
    old.resolve([delivery('old')])
    await flushPromises()

    expect(mounted.result.deliveries.value.map(item => item.deliveryId)).toEqual(['new'])
  })

  it('aborts all in-flight sections and exposes stable 403 errors', async () => {
    const signals: AbortSignal[] = []
    const pending = new Promise<never>(() => {})
    api.getDiscordConfiguration.mockImplementation((_auth: string, signal: AbortSignal) => {
      signals.push(signal)
      return Promise.resolve(configuration)
    })
    api.getDiscordHealth.mockImplementation((_auth: string, signal: AbortSignal) => {
      signals.push(signal)
      return pending
    })
    api.listDiscordDeliveries.mockImplementation((_auth: string, signal: AbortSignal) => {
      signals.push(signal)
      return pending
    })
    api.listDiscordBindings.mockImplementation((_auth: string, signal: AbortSignal) => {
      signals.push(signal)
      return pending
    })
    api.listDiscordCommands.mockImplementation((_auth: string, signal: AbortSignal) => {
      signals.push(signal)
      return pending
    })
    const mounted = mountComposable()
    apps.push(mounted.app)
    await flushPromises()
    mounted.app.unmount()
    apps.pop()

    expect(signals).toHaveLength(5)
    expect(signals.every(signal => signal.aborted)).toBe(true)

    api.getDiscordConfiguration.mockRejectedValue(new HttpError('http', 'forbidden', { status: 403, problemCode: 'owner_required' }))
    const forbidden = mountComposable()
    apps.push(forbidden.app)
    await flushPromises()
    expect(forbidden.result.state.value).toBe('forbidden')
    expect(forbidden.result.errorCode.value).toBe('owner_required')
  })

  it('keeps configuration usable when the independent health contract is unavailable', async () => {
    api.getDiscordHealth.mockRejectedValue(new HttpError('http', 'http_error', { status: 503, problemCode: 'discord_health_unavailable' }))

    const mounted = mountComposable()
    apps.push(mounted.app)
    await flushPromises()

    expect(mounted.result.state.value).toBe('ready')
    expect(mounted.result.configuration.value).toBe(configuration)
    expect(mounted.result.health.value).toBeNull()
    expect(mounted.result.healthState.value).toBe('unavailable')
  })
})
