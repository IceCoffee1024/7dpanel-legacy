import { describe, expect, it } from 'vitest'

import { parseAuthSession, serializeAuthSession } from './authSession'

const validRecord = {
  version: 1,
  token: '7dp_t_id.secret',
  expiresAt: 2_000,
  username: 'admin',
  role: 'Owner',
}

describe('parseAuthSession', () => {
  it('parses a current versioned session record', () => {
    expect(parseAuthSession(JSON.stringify(validRecord), 1_000)).toEqual({
      token: '7dp_t_id.secret',
      expiresAt: 2_000,
      username: 'admin',
      role: 'Owner',
    })
  })

  it.each([
    ['null', null],
    ['invalid JSON', '{'],
    ['an extra field', JSON.stringify({ ...validRecord, subject: 'sensitive-subject' })],
    ['a missing field', JSON.stringify({ ...validRecord, role: undefined })],
    ['an unsupported version', JSON.stringify({ ...validRecord, version: 2 })],
    ['a token without the access token prefix', JSON.stringify({ ...validRecord, token: 'opaque-token' })],
    ['an empty username', JSON.stringify({ ...validRecord, username: '  ' })],
    ['a fractional expiry', JSON.stringify({ ...validRecord, expiresAt: 1_000.5 })],
    ['an expired record', JSON.stringify({ ...validRecord, expiresAt: 1_000 })],
    ['an unknown role', JSON.stringify({ ...validRecord, role: 'Operator' })],
  ])('rejects %s', (_name, value) => {
    expect(parseAuthSession(value, 1_000)).toBeNull()
  })

  it('serializes only the approved versioned session fields', () => {
    expect(JSON.parse(serializeAuthSession({
      token: '7dp_t_id.secret',
      expiresAt: 2_000,
      username: 'admin',
      role: 'Owner',
    }))).toEqual(validRecord)
  })
})
