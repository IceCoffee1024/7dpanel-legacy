import type { ResolvedRequestOptions } from './generated/client/types.gen'

import { client } from './generated/client.gen'
import { HttpError } from './http'

export interface GeneratedClientOptions {
  fetch?: typeof fetch
  getAuthorizationHeader: () => string | null
  onUnauthorized?: () => void
  origin?: string
  timeoutMs?: number
}

function isAllowedApiUrl(url: string, origin: string): boolean {
  const parsed = new URL(url, origin)
  return parsed.origin === origin && parsed.pathname.startsWith('/api/v1/')
}

function acceptsServerSentEvents(request: Request): boolean {
  return request.headers.get('Accept')
    ?.split(',')
    .some(value => value.trim().split(';', 1)[0]?.toLowerCase() === 'text/event-stream')
    ?? false
}

function safeProblemFields(error: unknown) {
  if (typeof error !== 'object' || error === null)
    return {}
  const problem = error as Record<string, unknown>
  return {
    ...(typeof problem.code === 'string' ? { problemCode: problem.code } : {}),
    ...(typeof problem.traceId === 'string' ? { traceId: problem.traceId } : {}),
  }
}

function restrictedFetch(
  origin: string,
  timeoutMs: number,
  runtimeFetch: typeof fetch,
): typeof fetch {
  return async (input, init) => {
    const request = new Request(input, init)
    if (!isAllowedApiUrl(request.url, origin))
      throw new HttpError('invalid', 'Request path must start with /api/v1/')
    if (request.signal.aborted)
      throw new HttpError('aborted', 'Request was aborted')

    const controller = new AbortController()
    const isServerSentEventsRequest = acceptsServerSentEvents(request)
    let callerAborted = false
    let timedOut = false
    const abortFromCaller = () => {
      callerAborted = true
      controller.abort()
    }
    request.signal.addEventListener('abort', abortFromCaller, { once: true })
    const timeout = isServerSentEventsRequest
      ? null
      : setTimeout(() => {
          timedOut = true
          controller.abort()
        }, timeoutMs)

    try {
      return await runtimeFetch(new Request(request, {
        credentials: 'omit',
        signal: controller.signal,
      }))
    }
    catch (error) {
      if (error instanceof HttpError)
        throw error
      if (timedOut)
        throw new HttpError('timeout', 'Request timed out')
      if (callerAborted || request.signal.aborted)
        throw new HttpError('aborted', 'Request was aborted')
      throw new HttpError('network', 'Network request failed')
    }
    finally {
      if (timeout !== null)
        clearTimeout(timeout)
      request.signal.removeEventListener('abort', abortFromCaller)
    }
  }
}

function needsAuthorization(options: ResolvedRequestOptions): boolean {
  return (options.security?.length ?? 0) > 0
}

export function configureGeneratedClient(options: GeneratedClientOptions): void {
  const origin = new URL(options.origin ?? location.origin).origin
  const timeoutMs = options.timeoutMs ?? 10_000

  client.interceptors.request.clear()
  client.interceptors.response.clear()
  client.interceptors.error.clear()
  client.setConfig({
    baseUrl: origin,
    credentials: 'omit',
    fetch: restrictedFetch(origin, timeoutMs, options.fetch ?? globalThis.fetch),
    throwOnError: true,
  })

  client.interceptors.request.use((request, requestOptions) => {
    if (!isAllowedApiUrl(request.url, origin))
      throw new HttpError('invalid', 'Request path must start with /api/v1/')
    if (!needsAuthorization(requestOptions))
      return request

    const authorizationHeader = options.getAuthorizationHeader()
    if (authorizationHeader === null)
      return request
    const headers = new Headers(request.headers)
    headers.set('Authorization', authorizationHeader)
    return new Request(request, { credentials: 'omit', headers })
  })

  client.interceptors.response.use(async (response) => {
    const contentType = response.headers.get('content-type')
      ?.split(';', 1)[0]
      ?.trim()
      .toLowerCase()
    if (response.ok && response.status !== 204
      && (contentType === 'application/json' || contentType?.endsWith('+json'))) {
      const body = await response.clone().text()
      if (body.trim() !== '') {
        try {
          JSON.parse(body)
        }
        catch {
          throw new HttpError('invalid', 'Response body is not valid JSON')
        }
      }
    }
    return response
  })

  client.interceptors.error.use((error, response) => {
    if (error instanceof HttpError)
      return error
    if (response === undefined)
      return new HttpError('network', 'Network request failed')

    if (response.status === 401)
      options.onUnauthorized?.()
    return new HttpError(
      'http',
      `HTTP request failed with status ${response.status}`,
      { status: response.status, ...safeProblemFields(error) },
    )
  })
}
