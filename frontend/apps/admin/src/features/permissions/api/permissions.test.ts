import { describe, expect, it } from 'vitest'
import { parseGameAdmin, parsePanelUser } from './permissions'

describe('permissions parsers', () => {
  it('keeps native game levels separate from panel roles', () => {
    const parsed = parseGameAdmin({ playerId: 'EOS_1', displayName: 'Player', permissionLevel: 0 })
    expect(parsed).toEqual({ playerId: 'EOS_1', displayName: 'Player', permissionLevel: 0 })
    expect('role' in parsed).toBe(false)
  })

  it('rejects password material in a panel user response', () => {
    expect(() => parsePanelUser({
      subject: 'owner',
      username: 'admin',
      role: 'Owner',
      enabled: true,
      updatedAtUtc: '2026-07-26T00:00:00Z',
      passwordHash: 'secret',
    })).toThrow('Invalid panel user response')
  })
})
