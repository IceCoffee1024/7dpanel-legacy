import { afterEach, describe, expect, it, vi } from 'vitest'

import { requestJson } from '../../../shared/api/http'
import {
  createApiKey,
  fetchApiKeys,
  parseApiKeyList,
  parseCreatedApiKey,
  revokeApiKey,
} from './apiKeys'

vi.mock('../../../shared/api/http', () => ({
  requestJson: vi.fn(),
}))

const metadata = {
  id: 's0m3K3y1d3nt1f13r00000',
  displayPrefix: '7dp_k_s0m3K3y1d3nt1f13r00000',
  name: 'Server backup automation',
  createdAtUtc: '2026-07-23T08:00:00.0000000+00:00',
  lastUsedAtUtc: null,
  expiresAtUtc: '2026-08-23T08:00:00.0000000+00:00',
  status: 'active',
}

const created = {
  id: metadata.id,
  name: metadata.name,
  apiKey: '7dp_k_s0m3K3y1d3nt1f13r00000_1234567890123456789012345678901234567890123',
  createdAtUtc: metadata.createdAtUtc,
  expiresAtUtc: metadata.expiresAtUtc,
}

describe('aPI Key response parsing', () => {
  it('parses only approved metadata fields and copies the list', () => {
    const response = [{
      ...metadata,
      serverOnlyValue: 'must-not-reach-the-UI',
    }]

    const result = parseApiKeyList(response)
    response[0]!.name = 'changed'

    expect(result).toEqual([metadata])
    expect(result).not.toBe(response)
    expect(Object.isFrozen(result)).toBe(true)
    expect(Object.isFrozen(result[0])).toBe(true)
  })

  it('parses the one-time complete API Key creation response', () => {
    expect(parseCreatedApiKey(created)).toEqual(created)
  })

  it.each([
    ['a non-array list', {}],
    ['an unknown status', [{ ...metadata, status: 'disabled' }]],
    ['a metadata response containing the complete API Key', [{ ...metadata, apiKey: created.apiKey }]],
    ['a metadata response containing a secret hash', [{ ...metadata, secretHash: 'must-not-reach-the-UI' }]],
  ])('rejects %s', (_name, value) => {
    expect(() => parseApiKeyList(value)).toThrow('Invalid API Key response')
  })

  it.each([
    ['a missing one-time key', { ...created, apiKey: '' }],
    ['a non-Key creation value', { ...created, apiKey: 'Bearer token' }],
    ['a non-UTC creation time', { ...created, createdAtUtc: '2026-07-23T08:00:00+08:00' }],
  ])('rejects %s in a creation response', (_name, value) => {
    expect(() => parseCreatedApiKey(value)).toThrow('Invalid API Key response')
  })
})

describe('aPI Key requests', () => {
  afterEach(() => {
    vi.clearAllMocks()
  })

  it('lists API Keys with the supplied Authorization header only', async () => {
    vi.mocked(requestJson).mockResolvedValue([metadata])
    const authorizationHeader = 'Bearer website-access-token'

    await expect(fetchApiKeys(authorizationHeader)).resolves.toEqual([metadata])

    expect(requestJson).toHaveBeenCalledWith('/api/v1/api-keys', {
      headers: { Authorization: authorizationHeader },
      signal: undefined,
    })
    const [path, options] = vi.mocked(requestJson).mock.calls[0]!
    expect(path).not.toContain(authorizationHeader)
    expect(options).not.toHaveProperty('body')
  })

  it('creates an API Key with a JSON request and never puts the token in the URL or body', async () => {
    vi.mocked(requestJson).mockResolvedValue(created)
    const authorizationHeader = 'Bearer website-access-token'
    const controller = new AbortController()

    await expect(createApiKey(authorizationHeader, {
      name: '  Server backup automation  ',
      expiresAtUtc: '2026-08-23T08:00:00.0000000+00:00',
    }, controller.signal)).resolves.toEqual(created)

    expect(requestJson).toHaveBeenCalledWith('/api/v1/api-keys', {
      method: 'POST',
      headers: {
        'Authorization': authorizationHeader,
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        name: 'Server backup automation',
        expiresAtUtc: '2026-08-23T08:00:00.0000000+00:00',
      }),
      signal: controller.signal,
    })
    const [path, options] = vi.mocked(requestJson).mock.calls[0]!
    expect(path).not.toContain(authorizationHeader)
    expect(String(options?.body)).not.toContain(authorizationHeader)
  })

  it('revokes only by a safely encoded route segment and accepts 204 No Content', async () => {
    vi.mocked(requestJson).mockResolvedValue(undefined)
    const authorizationHeader = 'Bearer website-access-token'

    await expect(revokeApiKey(authorizationHeader, 'key id/with?reserved')).resolves.toBeUndefined()

    expect(requestJson).toHaveBeenCalledWith('/api/v1/api-keys/key%20id%2Fwith%3Freserved', {
      method: 'DELETE',
      headers: { Authorization: authorizationHeader },
      signal: undefined,
    })
  })

  it.each([
    ['', undefined, 'API Key name must contain between 1 and 80 Unicode characters'],
    ['   ', undefined, 'API Key name must contain between 1 and 80 Unicode characters'],
    ['a'.repeat(81), undefined, 'API Key name must contain between 1 and 80 Unicode characters'],
    ['valid', '2026-07-23T08:00:00+08:00', 'API Key expiration must be a UTC timestamp'],
  ])('rejects invalid create input before making a request', async (name, expiresAtUtc, message) => {
    await expect(createApiKey('Bearer website-access-token', { name, expiresAtUtc })).rejects.toThrow(message)
    expect(requestJson).not.toHaveBeenCalled()
  })
})
