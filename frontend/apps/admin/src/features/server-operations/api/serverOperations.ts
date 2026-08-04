import { requestJson } from '../../../shared/api/http'

export type ServerOperationAuditStatus = 'recorded' | 'audit_degraded'
export type ServerOperationKind = 'restart_script' | 'shutdown'
export type ServerOperationStatus = 'queued' | 'running' | 'succeeded' | 'failed' | 'cancelled' | 'result-unknown'

export interface ServerOperationStatusRecord {
  operationId: string
  kind: ServerOperationKind
  status: ServerOperationStatus
  requestedAtUtc: string
  startedAtUtc: string | null
  completedAtUtc: string | null
  completionDeadlineUtc: string
  failureCode: string | null
  auditStatus: ServerOperationAuditStatus
}

export interface RestartServerAccepted {
  operationId: string
  code: 'restart_script_started'
  requestedAtUtc: string
  scriptStartedAtUtc: string
  auditStatus: ServerOperationAuditStatus
}

export interface ShutdownServerAccepted {
  operationId: string
  code: 'shutdown_requested'
  requestedAtUtc: string
  acceptedAtUtc: string
  auditStatus: ServerOperationAuditStatus
}

export class ServerOperationError extends Error {
  readonly code = 'invalid-response' as const

  constructor() {
    super('Invalid server operation response')
    this.name = 'ServerOperationError'
  }
}

function invalid(): never {
  throw new ServerOperationError()
}

function record(value: unknown, keys: readonly string[]): Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value))
    return invalid()
  const source = value as Record<string, unknown>
  if (Object.keys(source).some(key => !keys.includes(key)))
    return invalid()
  return source
}

function requiredString(value: unknown): string {
  if (typeof value === 'string' && value.trim() !== '')
    return value
  return invalid()
}

function utcTimestamp(value: unknown): string {
  if (typeof value !== 'string')
    return invalid()
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.(\d{1,7}))?(?:Z|[+-]00:00)$/.exec(value)
  if (!match)
    return invalid()
  const [, yearText, monthText, dayText, hourText, minuteText, secondText, fractionText] = match
  const normalized = value.endsWith('Z') ? value : `${value.slice(0, -6)}Z`
  const timestamp = Date.parse(normalized)
  if (!Number.isFinite(timestamp))
    return invalid()
  const date = new Date(timestamp)
  const millisecond = Number((fractionText ?? '').padEnd(3, '0').slice(0, 3) || 0)
  if (date.getUTCFullYear() !== Number(yearText)
    || date.getUTCMonth() + 1 !== Number(monthText)
    || date.getUTCDate() !== Number(dayText)
    || date.getUTCHours() !== Number(hourText)
    || date.getUTCMinutes() !== Number(minuteText)
    || date.getUTCSeconds() !== Number(secondText)
    || date.getUTCMilliseconds() !== millisecond) {
    return invalid()
  }
  return value
}

function auditStatus(value: unknown): ServerOperationAuditStatus {
  if (value === 'recorded' || value === 'audit_degraded')
    return value
  return invalid()
}

function optionalUtcTimestamp(value: unknown): string | null {
  if (value === null)
    return null
  return utcTimestamp(value)
}

function optionalString(value: unknown): string | null {
  if (value === null)
    return null
  return requiredString(value)
}

export function parseServerOperationStatus(value: unknown): ServerOperationStatusRecord {
  const source = record(value, [
    'operationId',
    'kind',
    'status',
    'requestedAtUtc',
    'startedAtUtc',
    'completedAtUtc',
    'completionDeadlineUtc',
    'failureCode',
    'auditStatus',
  ])
  if (source.kind !== 'restart_script' && source.kind !== 'shutdown')
    return invalid()
  if (source.status !== 'queued' && source.status !== 'running' && source.status !== 'succeeded'
    && source.status !== 'failed' && source.status !== 'cancelled' && source.status !== 'result-unknown') {
    return invalid()
  }
  return Object.freeze({
    operationId: requiredString(source.operationId),
    kind: source.kind,
    status: source.status,
    requestedAtUtc: utcTimestamp(source.requestedAtUtc),
    startedAtUtc: optionalUtcTimestamp(source.startedAtUtc),
    completedAtUtc: optionalUtcTimestamp(source.completedAtUtc),
    completionDeadlineUtc: utcTimestamp(source.completionDeadlineUtc),
    failureCode: optionalString(source.failureCode),
    auditStatus: auditStatus(source.auditStatus),
  })
}

export function parseRestartAccepted(value: unknown): RestartServerAccepted {
  const source = record(value, ['operationId', 'code', 'requestedAtUtc', 'scriptStartedAtUtc', 'auditStatus'])
  if (source.code !== 'restart_script_started')
    return invalid()
  return Object.freeze({
    operationId: requiredString(source.operationId),
    code: source.code,
    requestedAtUtc: utcTimestamp(source.requestedAtUtc),
    scriptStartedAtUtc: utcTimestamp(source.scriptStartedAtUtc),
    auditStatus: auditStatus(source.auditStatus),
  })
}

function parseShutdownAccepted(value: unknown): ShutdownServerAccepted {
  const source = record(value, ['operationId', 'code', 'requestedAtUtc', 'acceptedAtUtc', 'auditStatus'])
  if (source.code !== 'shutdown_requested')
    return invalid()
  return Object.freeze({
    operationId: requiredString(source.operationId),
    code: source.code,
    requestedAtUtc: utcTimestamp(source.requestedAtUtc),
    acceptedAtUtc: utcTimestamp(source.acceptedAtUtc),
    auditStatus: auditStatus(source.auditStatus),
  })
}

const requestBody = JSON.stringify({ confirmed: true })

function requestOptions(authorizationHeader: string, signal?: AbortSignal) {
  return {
    body: requestBody,
    expectedStatus: 202,
    headers: {
      'Authorization': authorizationHeader,
      'Content-Type': 'application/json',
    },
    method: 'POST',
    signal,
  } as const
}

export async function restartServer(
  authorizationHeader: string,
  signal?: AbortSignal,
): Promise<RestartServerAccepted> {
  const response = await requestJson<unknown>(
    '/api/v1/server-operations/restart',
    requestOptions(authorizationHeader, signal),
  )
  return parseRestartAccepted(response)
}

export async function shutdownServer(
  authorizationHeader: string,
  signal?: AbortSignal,
): Promise<ShutdownServerAccepted> {
  const response = await requestJson<unknown>(
    '/api/v1/server-operations/shutdown',
    requestOptions(authorizationHeader, signal),
  )
  return parseShutdownAccepted(response)
}

export async function getServerOperation(
  authorizationHeader: string,
  operationId: string,
  signal?: AbortSignal,
): Promise<ServerOperationStatusRecord> {
  const response = await requestJson<unknown>(
    `/api/v1/server-operations/${encodeURIComponent(operationId)}`,
    {
      expectedStatus: 200,
      headers: { Authorization: authorizationHeader },
      method: 'GET',
      signal,
    },
  )
  return parseServerOperationStatus(response)
}
