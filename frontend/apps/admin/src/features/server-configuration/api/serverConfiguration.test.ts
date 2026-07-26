import { describe, expect, it } from 'vitest'

import { parseServerConfigurationSnapshot } from './serverConfiguration'

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

describe('server configuration API parser', () => {
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
