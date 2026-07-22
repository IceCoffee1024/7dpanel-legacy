export type HttpErrorCode = 'aborted' | 'network' | 'timeout' | 'http' | 'invalid'

interface HttpErrorFields {
  status?: number
  problemCode?: string
  traceId?: string
}

export class HttpError extends Error {
  readonly code: HttpErrorCode
  declare readonly status?: number
  declare readonly problemCode?: string
  declare readonly traceId?: string

  constructor(
    code: HttpErrorCode,
    message: string,
    fields: HttpErrorFields = {},
  ) {
    super(message)
    this.name = 'HttpError'
    this.code = code
    for (const [field, value] of Object.entries(fields)) {
      if (value !== undefined)
        Object.defineProperty(this, field, { enumerable: true, value })
    }
  }
}

export interface RequestJsonOptions extends Omit<RequestInit, 'signal'> {
  signal?: AbortSignal
  timeoutMs?: number
}

export async function requestJson<T>(
  path: string,
  options: RequestJsonOptions = {},
): Promise<T> {
  const normalizedUrl = new URL(path, location.origin)
  const rawPathname = path.split(/[?#]/, 1)[0] ?? ''
  let decodedPathname: string
  try {
    decodedPathname = decodeURIComponent(rawPathname)
  }
  catch {
    throw new HttpError('invalid', 'Request path must start with /api/v1/')
  }
  if (
    normalizedUrl.origin !== location.origin
    || !normalizedUrl.pathname.startsWith('/api/v1/')
    || !path.startsWith('/api/v1/')
    || /[a-z][a-z\d+.-]*:\/\//i.test(decodedPathname)
  ) {
    throw new HttpError('invalid', 'Request path must start with /api/v1/')
  }

  const { signal: callerSignal, timeoutMs = 10_000, ...requestOptions } = options
  if (callerSignal?.aborted) {
    throw new HttpError('aborted', 'Request was aborted')
  }

  const controller = new AbortController()
  let timedOut = false
  let callerAborted = false
  const abortFromCaller = () => {
    callerAborted = true
    controller.abort()
  }
  callerSignal?.addEventListener('abort', abortFromCaller, { once: true })

  const timeout = setTimeout(() => {
    timedOut = true
    controller.abort()
  }, timeoutMs)

  try {
    const response = await fetch(path, {
      ...requestOptions,
      credentials: 'omit',
      signal: controller.signal,
    })

    if (!response.ok) {
      let parsedProblemCode: string | undefined
      let parsedTraceId: string | undefined

      if (response.headers.get('content-type')?.split(';', 1)[0]?.trim().toLowerCase() === 'application/problem+json') {
        try {
          const problem: unknown = await response.json()
          if (typeof problem === 'object' && problem !== null) {
            const { code, traceId } = problem as Record<string, unknown>
            parsedProblemCode = typeof code === 'string' ? code : undefined
            parsedTraceId = typeof traceId === 'string' ? traceId : undefined
          }
        }
        catch {
          // Invalid error bodies still map to the HTTP status only.
        }
      }

      const fields: HttpErrorFields = {
        status: response.status,
        ...(parsedProblemCode === undefined ? {} : { problemCode: parsedProblemCode }),
        ...(parsedTraceId === undefined ? {} : { traceId: parsedTraceId }),
      }
      throw new HttpError('http', `HTTP request failed with status ${response.status}`, fields)
    }

    try {
      return await response.json() as T
    }
    catch {
      throw new HttpError('invalid', 'Response body is not valid JSON')
    }
  }
  catch (error) {
    if (error instanceof HttpError)
      throw error
    if (timedOut)
      throw new HttpError('timeout', 'Request timed out')
    if (callerAborted || callerSignal?.aborted)
      throw new HttpError('aborted', 'Request was aborted')
    throw new HttpError('network', 'Network request failed')
  }
  finally {
    clearTimeout(timeout)
    callerSignal?.removeEventListener('abort', abortFromCaller)
  }
}
