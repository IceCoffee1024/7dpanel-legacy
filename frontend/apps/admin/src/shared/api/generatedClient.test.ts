import { afterEach, describe, expect, it, vi } from 'vitest'

import { overviewGetQueryKey } from './generated/@pinia/colada.gen'
import { overviewGet, serverEventsGet } from './generated/sdk.gen'
import { configureGeneratedClient } from './generatedClient'
import { HttpError } from './http'

function abortingFetch(): typeof fetch {
  return vi.fn((input: RequestInfo | URL) => {
    const request = new Request(input)
    return new Promise<Response>((_resolve, reject) => {
      request.signal.addEventListener('abort', () => {
        reject(new DOMException('aborted', 'AbortError'))
      }, { once: true })
    })
  }) as typeof fetch
}

describe('generated API client', () => {
  afterEach(() => {
    vi.useRealTimers()
    vi.restoreAllMocks()
  })

  it('sends protected requests only to the same-origin API with omitted credentials', async () => {
    const fetchMock = vi.fn().mockResolvedValue(Response.json({ availability: 'unavailable' })) as typeof fetch
    configureGeneratedClient({
      fetch: fetchMock,
      getAuthorizationHeader: () => 'Bearer generated-token',
      origin: 'https://panel.example',
    })

    await overviewGet()

    const request = vi.mocked(fetchMock).mock.calls[0]?.[0] as Request
    expect(request.url).toBe('https://panel.example/api/v1/overview')
    expect(request.credentials).toBe('omit')
    expect(request.headers.get('Authorization')).toBe('Bearer generated-token')
    expect(JSON.stringify(overviewGetQueryKey())).not.toContain('generated-token')
  })

  it('rejects generated requests outside the same-origin API boundary', async () => {
    const fetchMock = vi.fn() as typeof fetch
    configureGeneratedClient({
      fetch: fetchMock,
      getAuthorizationHeader: () => null,
      origin: 'https://panel.example',
    })

    await expect(overviewGet({ baseUrl: 'https://elsewhere.example' })).rejects.toMatchObject({
      code: 'invalid',
    })
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('maps caller cancellation and the default timeout to stable HttpError codes', async () => {
    const callerController = new AbortController()
    configureGeneratedClient({
      fetch: abortingFetch(),
      getAuthorizationHeader: () => null,
      origin: 'https://panel.example',
    })
    const cancelled = overviewGet({ signal: callerController.signal })
    callerController.abort()
    await expect(cancelled).rejects.toMatchObject({ code: 'aborted' })

    vi.useFakeTimers()
    configureGeneratedClient({
      fetch: abortingFetch(),
      getAuthorizationHeader: () => null,
      origin: 'https://panel.example',
    })
    const timedOut = overviewGet()
    const timeoutExpectation = expect(timedOut).rejects.toMatchObject({ code: 'timeout' })
    await vi.advanceTimersByTimeAsync(10_000)
    await timeoutExpectation
  })

  it('keeps an SSE request alive past the ordinary timeout and still honors caller cancellation', async () => {
    vi.useFakeTimers()
    const streamedRequests: Request[] = []
    const fetchMock = vi.fn((input: RequestInfo | URL) => {
      const streamedRequest = new Request(input)
      streamedRequests.push(streamedRequest)
      return new Promise<Response>((_resolve, reject) => {
        streamedRequest.signal.addEventListener('abort', () => {
          reject(new DOMException('aborted', 'AbortError'))
        }, { once: true })
      })
    }) as typeof fetch
    const onSseError = vi.fn()
    const callerController = new AbortController()
    configureGeneratedClient({
      fetch: fetchMock,
      getAuthorizationHeader: () => 'Bearer generated-token',
      origin: 'https://panel.example',
    })

    const result = await serverEventsGet({
      headers: { Accept: 'text/event-stream' },
      onSseError,
      signal: callerController.signal,
      sseMaxRetryAttempts: 1,
    })
    const pendingEvent = result.stream.next()
    await vi.advanceTimersByTimeAsync(10_000)

    expect(fetchMock).toHaveBeenCalledOnce()
    expect(streamedRequests[0]?.signal.aborted).toBe(false)

    callerController.abort()
    await pendingEvent

    expect(streamedRequests[0]?.signal.aborted).toBe(true)
    expect(onSseError).toHaveBeenCalledWith(expect.objectContaining({ code: 'aborted' }))
  })

  it('maps network failures without exposing their original message', async () => {
    configureGeneratedClient({
      fetch: vi.fn().mockRejectedValue(new Error('private network detail')) as typeof fetch,
      getAuthorizationHeader: () => null,
      origin: 'https://panel.example',
    })

    const failure = await overviewGet().catch(error => error)
    expect(failure).toBeInstanceOf(HttpError)
    expect(failure).toMatchObject({ code: 'network' })
    expect(JSON.stringify(failure)).not.toContain('private network detail')
  })

  it('maps malformed successful JSON to a stable invalid response error', async () => {
    configureGeneratedClient({
      fetch: vi.fn().mockResolvedValue(new Response('{not-json', {
        headers: { 'content-type': 'application/json' },
        status: 200,
      })) as typeof fetch,
      getAuthorizationHeader: () => null,
      origin: 'https://panel.example',
    })

    await expect(overviewGet()).rejects.toMatchObject({ code: 'invalid' })
  })

  it('retains only safe Problem Details fields and reports unauthorized once', async () => {
    const onUnauthorized = vi.fn()
    configureGeneratedClient({
      fetch: vi.fn().mockResolvedValue(Response.json({
        code: 'authentication_required',
        detail: 'private backend detail',
        traceId: 'trace-1',
      }, {
        headers: { 'content-type': 'application/problem+json' },
        status: 401,
      })) as typeof fetch,
      getAuthorizationHeader: () => 'Bearer expired',
      onUnauthorized,
      origin: 'https://panel.example',
    })

    const failure = await overviewGet().catch(error => error)
    expect(failure).toBeInstanceOf(HttpError)
    expect(failure).toMatchObject({
      code: 'http',
      problemCode: 'authentication_required',
      status: 401,
      traceId: 'trace-1',
    })
    expect(failure.message).not.toContain('private backend detail')
    expect(JSON.stringify(failure)).not.toContain('private backend detail')
    expect(onUnauthorized).toHaveBeenCalledOnce()
  })
})
