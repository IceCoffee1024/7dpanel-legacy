export interface ServerHealth {
  status: 'ok'
  product: string
  version: string
}

export type ServerHealthErrorCode = 'aborted' | 'network' | 'http' | 'invalid'

export class ServerHealthError extends Error {
  readonly code: ServerHealthErrorCode
  readonly status?: number

  constructor(code: ServerHealthErrorCode, message: string, status?: number) {
    super(message)
    this.name = 'ServerHealthError'
    this.code = code
    this.status = status
  }
}

export function parseServerHealth(value: unknown): ServerHealth {
  if (typeof value !== 'object' || value === null) {
    throw new ServerHealthError('invalid', 'Health response must be an object.')
  }

  const response = value as Record<string, unknown>
  if (
    response.status !== 'ok'
    || typeof response.product !== 'string'
    || response.product.trim() === ''
    || typeof response.version !== 'string'
    || response.version.trim() === ''
  ) {
    throw new ServerHealthError('invalid', 'Health response has an invalid shape.')
  }

  return {
    status: 'ok',
    product: response.product,
    version: response.version,
  }
}

export async function fetchServerHealth(signal?: AbortSignal): Promise<ServerHealth> {
  let response: Response
  try {
    response = await fetch('/api/v1/health', { signal })
  }
  catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') {
      throw new ServerHealthError('aborted', 'Health request was cancelled.')
    }
    throw new ServerHealthError('network', 'Health request could not reach the server.')
  }

  if (!response.ok) {
    throw new ServerHealthError('http', 'Health request failed.', response.status)
  }

  let value: unknown
  try {
    value = await response.json()
  }
  catch {
    throw new ServerHealthError('invalid', 'Health response was not valid JSON.')
  }

  return parseServerHealth(value)
}
