import { beforeEach, describe, expect, it, vi } from 'vitest'

import * as geoIp from './geoip'

const requestJson = vi.hoisted(() => vi.fn())

vi.mock('../../../shared/api/http', () => ({ requestJson }))

describe('geoIP credentials API', () => {
  beforeEach(() => requestJson.mockReset())

  it('submits both credential intents and returns metadata without secret values', async () => {
    const response = Object.freeze({
      accountId: Object.freeze({ isSet: true, fingerprint: 'account-fingerprint', updatedAtUtc: '2026-07-27T08:00:00Z' }),
      licenseKey: Object.freeze({ isSet: false, fingerprint: null, updatedAtUtc: null }),
    })
    const draft = {
      accountId: { operation: 'Replace' as const, value: '12345' },
      licenseKey: { operation: 'Clear' as const },
    }
    requestJson.mockResolvedValue(response)

    expect(geoIp.updateGeoIpCredentials).toBeTypeOf('function')
    if (geoIp.updateGeoIpCredentials === undefined)
      return

    const credentials = await geoIp.updateGeoIpCredentials('Bearer owner', draft)

    expect(requestJson).toHaveBeenCalledWith(
      '/api/v1/access-policies/geoip/credentials',
      expect.objectContaining({
        method: 'PUT',
        headers: { 'Authorization': 'Bearer owner', 'Content-Type': 'application/json' },
        body: JSON.stringify(draft),
      }),
    )
    expect(credentials).toEqual(response)
    expect(JSON.stringify(credentials)).not.toContain('12345')
  })
})
