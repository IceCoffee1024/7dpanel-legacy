import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { HttpError, requestJson } from './http'

function fetchMock() {
  return vi.mocked(fetch)
}

function abortableFetch() {
  return vi.fn((_input: RequestInfo | URL, init?: RequestInit) => new Promise<Response>((_resolve, reject) => {
    init?.signal?.addEventListener('abort', () => {
      reject(new DOMException('The operation was aborted.', 'AbortError'))
    }, { once: true })
  }))
}

describe('requestJson', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('returns parsed JSON and omits credentials', async () => {
    fetchMock().mockResolvedValue(new Response(JSON.stringify({ players: 3 }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }))

    await expect(requestJson<{ players: number }>('/api/v1/players')).resolves.toEqual({ players: 3 })
    expect(fetchMock()).toHaveBeenCalledWith('/api/v1/players', expect.objectContaining({
      credentials: 'omit',
    }))
  })

  it('returns undefined for a successful no-content response', async () => {
    fetchMock().mockResolvedValue(new Response(null, { status: 204 }))

    await expect(requestJson<void>('/api/v1/api-keys/key-1', {
      method: 'DELETE',
    })).resolves.toBeUndefined()
  })

  it('maps only stable Problem Details fields without retaining detail', async () => {
    fetchMock().mockResolvedValue(new Response(JSON.stringify({
      status: 403,
      code: 'forbidden',
      traceId: 'trace-123',
      detail: 'secret backend detail',
    }), {
      status: 403,
      headers: { 'Content-Type': 'application/problem+json' },
    }))

    const error = await requestJson('/api/v1/players').catch(value => value)

    expect(error).toBeInstanceOf(HttpError)
    expect(error).toMatchObject({
      code: 'http',
      status: 403,
      problemCode: 'forbidden',
      traceId: 'trace-123',
    })
    expect(String(error)).not.toContain('secret backend detail')
    expect(error).not.toHaveProperty('detail')
  })

  it('does not retain OAuth error descriptions from non-Problem Details bodies', async () => {
    fetchMock().mockResolvedValue(new Response(JSON.stringify({
      error: 'invalid_grant',
      error_description: 'sensitive identity provider detail',
    }), {
      status: 401,
      headers: { 'Content-Type': 'application/json' },
    }))

    const error = await requestJson('/api/v1/session').catch(value => value)

    expect(error).toMatchObject({ code: 'http', status: 401 })
    expect(error).not.toHaveProperty('problemCode')
    expect(String(error)).not.toContain('sensitive identity provider detail')
  })

  it('rejects external absolute URLs without calling fetch', async () => {
    await expect(requestJson('https://example.com/api/v1/players')).rejects.toMatchObject({
      code: 'invalid',
    })
    expect(fetchMock()).not.toHaveBeenCalled()
  })

  it('rejects API paths containing a scheme and host without calling fetch', async () => {
    await expect(requestJson('/api/v1/https://example.com/players')).rejects.toMatchObject({
      code: 'invalid',
    })
    expect(fetchMock()).not.toHaveBeenCalled()
  })

  it.each([
    '/api/v1/https:%2F%2Fevil.test/players',
    '/api/v1/https%3A%2F%2Fevil.test/players',
  ])('rejects an encoded scheme and host in the API path %s', async (path) => {
    await expect(requestJson(path)).rejects.toMatchObject({
      code: 'invalid',
    })
    expect(fetchMock()).not.toHaveBeenCalled()
  })

  it('rejects API paths that normalize outside the versioned root', async () => {
    await expect(requestJson('/api/v1/../../health')).rejects.toMatchObject({
      code: 'invalid',
    })
    expect(fetchMock()).not.toHaveBeenCalled()
  })

  it('allows a URL value in the query of a versioned API path', async () => {
    fetchMock().mockResolvedValue(new Response(JSON.stringify({ ok: true }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }))

    await expect(requestJson('/api/v1/players?returnUrl=https://example.test/a')).resolves.toEqual({ ok: true })
  })

  it('maps caller cancellation to aborted', async () => {
    const controller = new AbortController()
    vi.stubGlobal('fetch', abortableFetch())

    const request = requestJson('/api/v1/players', { signal: controller.signal })
    controller.abort()

    await expect(request).rejects.toMatchObject({ code: 'aborted' })
  })

  it.each([
    ['the default timeout', undefined, 10_000],
    ['an explicit timeout', 250, 250],
  ])('maps %s to timeout and aborts the internal request', async (_name, timeoutMs, elapsedMs) => {
    vi.useFakeTimers()
    const pendingFetch = abortableFetch()
    vi.stubGlobal('fetch', pendingFetch)

    const request = requestJson('/api/v1/players', { timeoutMs })
    const signal = pendingFetch.mock.calls[0]?.[1]?.signal
    const errorPromise = request.catch(error => error)

    await vi.advanceTimersByTimeAsync(elapsedMs)

    expect(signal?.aborted).toBe(true)
    await expect(errorPromise).resolves.toMatchObject({ code: 'timeout' })
  })

  it('maps fetch failures to network', async () => {
    fetchMock().mockRejectedValue(new TypeError('Failed to fetch'))

    await expect(requestJson('/api/v1/players')).rejects.toMatchObject({
      code: 'network',
    })
  })

  it('maps non-JSON success responses to invalid', async () => {
    fetchMock().mockResolvedValue(new Response('not json', {
      status: 200,
      headers: { 'Content-Type': 'text/plain' },
    }))

    await expect(requestJson('/api/v1/players')).rejects.toMatchObject({
      code: 'invalid',
    })
  })

  it('rejects a pre-aborted signal without calling fetch', async () => {
    const controller = new AbortController()
    controller.abort()

    await expect(requestJson('/api/v1/players', { signal: controller.signal })).rejects.toMatchObject({
      code: 'aborted',
    })
    expect(fetchMock()).not.toHaveBeenCalled()
  })
})
