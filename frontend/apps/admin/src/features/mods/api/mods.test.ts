import { afterEach, describe, expect, it, vi } from 'vitest'

import { requestJson } from '../../../shared/api/http'
import { fetchMods, parseModList, setModEnabled } from './mods'

vi.mock('../../../shared/api/http', () => ({ requestJson: vi.fn() }))

const mod = {
  directoryId: 'Example-Mod',
  name: 'Example',
  displayName: 'Example Mod',
  author: 'Author',
  version: '1.0',
  website: 'https://example.test',
  description: 'Description',
  isLoadedNow: true,
  isEnabledNextStart: false,
  isProtected: false,
}

describe('mod API', () => {
  afterEach(() => vi.clearAllMocks())

  it('strictly parses nullable runtime and next-start state', () => {
    expect(parseModList([mod])).toEqual([mod])
    expect(parseModList([{ ...mod, isLoadedNow: null }])[0]?.isLoadedNow).toBeNull()
    expect(() => parseModList([{ ...mod, isEnabledNextStart: null }])).toThrow('Invalid mod response')
    expect(() => parseModList([{ ...mod, directoryId: '../escape' }])).toThrow('Invalid mod response')
  })

  it('lists and changes only an encoded directory identifier', async () => {
    vi.mocked(requestJson).mockResolvedValueOnce([mod]).mockResolvedValueOnce({})
    await expect(fetchMods('Bearer token')).resolves.toEqual([mod])
    await setModEnabled('Bearer token', 'Example Mod', false)

    expect(requestJson).toHaveBeenNthCalledWith(2, '/api/v1/mods/Example%20Mod/state', {
      method: 'PUT',
      headers: { 'Authorization': 'Bearer token', 'Content-Type': 'application/json' },
      body: JSON.stringify({ enabled: false }),
      signal: undefined,
    })
  })
})
