import type { App } from 'vue'

import { flushPromises } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp } from 'vue'

import { HttpError } from '../../../shared/api/http'
import { useGeoIp } from './useGeoIp'

const api = vi.hoisted(() => ({ getGeoIpCredentials: vi.fn(), getGeoIpDiagnostics: vi.fn(), getGeoIpPolicy: vi.fn(), saveGeoIpPolicy: vi.fn(), testGeoIpPolicy: vi.fn(), updateGeoIpCredentials: vi.fn() }))
const auth = vi.hoisted(() => ({ authorizationHeader: 'Bearer owner' as string | null, expireSession: vi.fn() }))
vi.mock('../api/geoip', () => api)
vi.mock('../../auth', () => ({ useAuthStore: () => auth }))

const policy = Object.freeze({ version: 1, isEnabled: true, provider: 'LocalMmdb', failureMode: 'FailOpen', bypassAdmins: true, rejectionMessage: 'Denied', networkRules: [], countryRules: [], cacheHealth: { queueDepth: 0, rejectedRefreshCount: 0, lastCompletedAtUtc: null, lastLookupStatus: null, severity: 'Information', statusCode: 'ready' }, providers: [], recentDecisions: [] })
const diagnostics = (statusCode: string) => Object.freeze({ isEnabled: true, failureMode: 'FailOpen', provider: 'LocalMmdb', severity: 'Information', statusCode, queueDepth: 0, rejectedRefreshCount: 0, lastCompletedAtUtc: null, lastLookupStatus: null, providers: [] })
const credentials = Object.freeze({ accountId: Object.freeze({ isSet: true, fingerprint: 'account-fingerprint', updatedAtUtc: '2026-07-27T08:00:00Z' }), licenseKey: Object.freeze({ isSet: false, fingerprint: null, updatedAtUtc: null }) })
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>((done) => { resolve = done }); return { promise, resolve } }
function mountComposable() { let result!: ReturnType<typeof useGeoIp>; const app = createApp({ setup() { result = useGeoIp(); return () => null } }); app.mount(document.createElement('div')); return { app, result } }

describe('useGeoIp', () => {
  const apps: App[] = []
  beforeEach(() => {
    Object.values(api).forEach(mock => mock.mockReset())
    auth.authorizationHeader = 'Bearer owner'; auth.expireSession.mockReset()
    api.getGeoIpPolicy.mockResolvedValue(policy); api.getGeoIpDiagnostics.mockResolvedValue(diagnostics('ready')); api.getGeoIpCredentials.mockResolvedValue(credentials)
  })
  afterEach(() => { while (apps.length > 0) apps.pop()!.unmount() })

  it('ignores diagnostics from an older refresh that completes late', async () => {
    const old = deferred<ReturnType<typeof diagnostics>>()
    api.getGeoIpDiagnostics.mockReturnValueOnce(old.promise).mockResolvedValueOnce(diagnostics('new'))
    const mounted = mountComposable(); apps.push(mounted.app)
    await flushPromises()

    await mounted.result.refresh()
    old.resolve(diagnostics('old'))
    await flushPromises()

    expect(mounted.result.diagnostics.value?.statusCode).toBe('new')
  })

  it('loads credential metadata and sends both credential intents without retaining the replacement locally', async () => {
    const updated = Object.freeze({ accountId: Object.freeze({ isSet: true, fingerprint: 'updated-account-fingerprint', updatedAtUtc: '2026-07-27T08:01:00Z' }), licenseKey: Object.freeze({ isSet: true, fingerprint: 'license-fingerprint', updatedAtUtc: '2026-07-27T08:01:00Z' }) })
    const replacement = '12345'
    api.updateGeoIpCredentials.mockResolvedValue(updated)
    const mounted = mountComposable(); apps.push(mounted.app)
    await flushPromises()
    const controller = mounted.result as typeof mounted.result & { credentials?: { value: unknown }, updateCredentials?: (draft: { accountId: { operation: 'Replace', value: string }, licenseKey: { operation: 'Keep' } }) => Promise<boolean> }

    expect(controller.updateCredentials).toBeTypeOf('function')
    if (controller.updateCredentials === undefined)
      return

    await controller.updateCredentials({ accountId: { operation: 'Replace', value: replacement }, licenseKey: { operation: 'Keep' } })

    expect(api.updateGeoIpCredentials).toHaveBeenCalledWith('Bearer owner', { accountId: { operation: 'Replace', value: replacement }, licenseKey: { operation: 'Keep' } }, expect.any(AbortSignal))
    expect(controller.credentials?.value).toEqual(updated)
    expect(JSON.stringify(controller.credentials?.value)).not.toContain(replacement)
  })

  it('aborts policy, diagnostics, credential reads, and credential mutations and exposes stable 403 errors', async () => {
    const signals: AbortSignal[] = []
    const pending = new Promise<never>(() => {})
    api.getGeoIpPolicy.mockImplementation((_auth: string, signal: AbortSignal) => { signals.push(signal); return Promise.resolve(policy) })
    api.getGeoIpDiagnostics.mockImplementation((_auth: string, signal: AbortSignal) => { signals.push(signal); return pending })
    api.getGeoIpCredentials.mockImplementation((_auth: string, signal: AbortSignal) => { signals.push(signal); return Promise.resolve(credentials) })
    api.updateGeoIpCredentials.mockImplementation((_auth: string, _draft: unknown, signal: AbortSignal) => { signals.push(signal); return pending })
    const mounted = mountComposable(); apps.push(mounted.app)
    await flushPromises()
    const controller = mounted.result as typeof mounted.result & { updateCredentials?: (draft: { accountId: { operation: 'Keep' }, licenseKey: { operation: 'Clear' } }) => Promise<boolean> }
    expect(controller.updateCredentials).toBeTypeOf('function')
    if (controller.updateCredentials === undefined)
      return
    void controller.updateCredentials({ accountId: { operation: 'Keep' }, licenseKey: { operation: 'Clear' } })
    await flushPromises()
    mounted.app.unmount(); apps.pop()

    expect(signals).toHaveLength(4)
    expect(signals.every(signal => signal.aborted)).toBe(true)

    api.getGeoIpPolicy.mockRejectedValue(new HttpError('http', 'forbidden', { status: 403, problemCode: 'owner_required' }))
    const forbidden = mountComposable(); apps.push(forbidden.app)
    await flushPromises()
    expect(forbidden.result.state.value).toBe('forbidden')
    expect(forbidden.result.errorCode.value).toBe('owner_required')
  })
})
