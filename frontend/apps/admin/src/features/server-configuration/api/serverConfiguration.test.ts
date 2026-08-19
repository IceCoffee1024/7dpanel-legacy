import { beforeEach, describe, expect, it, vi } from 'vitest'

import { serverConfigurationGet, serverConfigurationPut } from '../../../shared/api/generated/sdk.gen'
import {
  fetchServerConfiguration,
  parseServerConfigurationSnapshot,
  updateServerConfigurationField,
} from './serverConfiguration'

vi.mock('../../../shared/api/generated/sdk.gen', () => ({
  serverConfigurationGet: vi.fn(),
  serverConfigurationPut: vi.fn(),
}))

const validSnapshot = {
  version: 'a'.repeat(64),
  readAtUtc: '2026-07-26T08:00:00Z',
  fields: [{
    key: 'ServerName',
    value: 'My server',
    group: 'Identity',
    valueType: 'text',
    editable: true,
    advanced: false,
    sensitive: false,
    isSet: true,
    restartRequired: true,
    allowedValues: [],
    minimum: null,
    maximum: null,
  }],
}

const validUpdateResult = {
  version: 'b'.repeat(64),
  savedAtUtc: '2026-07-26T08:01:00Z',
  restartRequired: false,
}

describe('server configuration API parser', () => {
  beforeEach(() => {
    vi.mocked(serverConfigurationGet).mockReset()
    vi.mocked(serverConfigurationPut).mockReset()
  })

  it('delegates snapshot fetches and their cancellation signal to the generated operation', async () => {
    const signal = new AbortController().signal
    vi.mocked(serverConfigurationGet).mockResolvedValue(validSnapshot)

    await expect(fetchServerConfiguration('Bearer owner', signal)).resolves.toMatchObject({
      version: validSnapshot.version,
    })

    expect(serverConfigurationGet).toHaveBeenCalledWith({ signal })
  })

  it('delegates key, update body, and cancellation signal to the generated operation', async () => {
    const signal = new AbortController().signal
    vi.mocked(serverConfigurationPut).mockResolvedValue(validUpdateResult)

    await expect(updateServerConfigurationField('Bearer owner', 'Server Name', 'My server', 'a'.repeat(64), signal)).resolves.toEqual(validUpdateResult)

    expect(serverConfigurationPut).toHaveBeenCalledWith({
      path: { key: 'Server Name' },
      body: { value: 'My server', version: 'a'.repeat(64) },
      signal,
    })
  })

  it('accepts and freezes a complete snapshot', () => {
    const snapshot = parseServerConfigurationSnapshot(validSnapshot)

    expect(snapshot.fields[0]?.key).toBe('ServerName')
    expect(snapshot.fields[0]?.advanced).toBe(false)
    expect(Object.isFrozen(snapshot)).toBe(true)
    expect(Object.isFrozen(snapshot.fields)).toBe(true)
  })

  it('rejects invalid field metadata and secret-bearing responses', () => {
    expect(() => parseServerConfigurationSnapshot({
      ...validSnapshot,
      fields: [{ ...validSnapshot.fields[0], editable: 'yes' }],
    })).toThrow('Invalid server configuration response')
    expect(() => parseServerConfigurationSnapshot({
      ...validSnapshot,
      fields: [{ ...validSnapshot.fields[0], advanced: 'yes' }],
    })).toThrow('Invalid server configuration response')
    expect(() => parseServerConfigurationSnapshot({
      ...validSnapshot,
      fields: [{ ...validSnapshot.fields[0], sensitive: true, value: 'leaked' }],
    })).toThrow('Invalid server configuration response')
  })
})
