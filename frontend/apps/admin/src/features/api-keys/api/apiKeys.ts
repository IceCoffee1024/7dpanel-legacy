import { requestJson } from '../../../shared/api/http'

export type ApiKeyStatus = 'active' | 'expired' | 'revoked'

export interface ApiKeyMetadata {
  id: string
  displayPrefix: string
  name: string
  createdAtUtc: string
  lastUsedAtUtc: string | null
  expiresAtUtc: string | null
  status: ApiKeyStatus
}

export interface CreatedApiKey {
  id: string
  name: string
  apiKey: string
  createdAtUtc: string
  expiresAtUtc: string | null
}

export interface CreateApiKeyInput {
  name: string
  expiresAtUtc?: string
}

const keyIdPattern = /^[\w-]{22}$/
const apiKeyPattern = /^7dp_k_[\w-]{22}_[\w-]{43}$/
const statuses = new Set<ApiKeyStatus>(['active', 'expired', 'revoked'])

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function hasOwnProperty(value: Record<string, unknown>, property: string): boolean {
  return Object.getOwnPropertyDescriptor(value, property) !== undefined
}

function isUtcTimestamp(value: string): boolean {
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.(\d{1,7}))?(?:Z|[+-]00:00)$/.exec(value)
  if (!match)
    return false

  const [, yearText, monthText, dayText, hourText, minuteText, secondText, fractionText] = match
  const normalized = value.endsWith('Z') ? value : `${value.slice(0, -6)}Z`
  const timestamp = Date.parse(normalized)
  if (!Number.isFinite(timestamp))
    return false

  const date = new Date(timestamp)
  const millisecond = Number((fractionText ?? '').padEnd(3, '0').slice(0, 3) || 0)
  return date.getUTCFullYear() === Number(yearText)
    && date.getUTCMonth() + 1 === Number(monthText)
    && date.getUTCDate() === Number(dayText)
    && date.getUTCHours() === Number(hourText)
    && date.getUTCMinutes() === Number(minuteText)
    && date.getUTCSeconds() === Number(secondText)
    && date.getUTCMilliseconds() === millisecond
}

function parseOptionalUtcTimestamp(value: unknown): string | null {
  if (value === null)
    return null
  if (typeof value !== 'string' || !isUtcTimestamp(value))
    throw new Error('Invalid API Key response')
  return value
}

function parseMetadata(value: unknown): ApiKeyMetadata {
  if (!isRecord(value)
    || typeof value.id !== 'string'
    || !keyIdPattern.test(value.id)
    || typeof value.displayPrefix !== 'string'
    || value.displayPrefix !== `7dp_k_${value.id}`
    || typeof value.name !== 'string'
    || value.name.trim() === ''
    || !Array.from(value.name).length
    || Array.from(value.name).length > 80
    || typeof value.createdAtUtc !== 'string'
    || !isUtcTimestamp(value.createdAtUtc)
    || typeof value.status !== 'string'
    || !statuses.has(value.status as ApiKeyStatus)
    || hasOwnProperty(value, 'apiKey')
    || hasOwnProperty(value, 'secret')
    || hasOwnProperty(value, 'secretHash')) {
    throw new Error('Invalid API Key response')
  }

  return Object.freeze({
    id: value.id,
    displayPrefix: value.displayPrefix,
    name: value.name,
    createdAtUtc: value.createdAtUtc,
    lastUsedAtUtc: parseOptionalUtcTimestamp(value.lastUsedAtUtc),
    expiresAtUtc: parseOptionalUtcTimestamp(value.expiresAtUtc),
    status: value.status as ApiKeyStatus,
  })
}

export function parseApiKeyList(value: unknown): readonly ApiKeyMetadata[] {
  if (!Array.isArray(value))
    throw new Error('Invalid API Key response')
  return Object.freeze(value.map(parseMetadata))
}

export function parseCreatedApiKey(value: unknown): CreatedApiKey {
  if (!isRecord(value)
    || typeof value.id !== 'string'
    || !keyIdPattern.test(value.id)
    || typeof value.name !== 'string'
    || value.name.trim() === ''
    || typeof value.apiKey !== 'string'
    || !apiKeyPattern.test(value.apiKey)
    || !value.apiKey.startsWith(`7dp_k_${value.id}_`)
    || typeof value.createdAtUtc !== 'string'
    || !isUtcTimestamp(value.createdAtUtc)) {
    throw new Error('Invalid API Key response')
  }

  return Object.freeze({
    id: value.id,
    name: value.name,
    apiKey: value.apiKey,
    createdAtUtc: value.createdAtUtc,
    expiresAtUtc: parseOptionalUtcTimestamp(value.expiresAtUtc),
  })
}

function normalizeCreateInput(input: CreateApiKeyInput): { name: string, expiresAtUtc?: string } {
  const name = input.name.trim()
  const nameLength = Array.from(name).length
  if (nameLength < 1 || nameLength > 80)
    throw new Error('API Key name must contain between 1 and 80 Unicode characters')

  if (input.expiresAtUtc === undefined || input.expiresAtUtc === '')
    return { name }
  if (!isUtcTimestamp(input.expiresAtUtc))
    throw new Error('API Key expiration must be a UTC timestamp')
  return { name, expiresAtUtc: input.expiresAtUtc }
}

export async function fetchApiKeys(
  authorizationHeader: string,
  signal?: AbortSignal,
): Promise<readonly ApiKeyMetadata[]> {
  const response = await requestJson<unknown>('/api/v1/api-keys', {
    headers: { Authorization: authorizationHeader },
    signal,
  })
  return parseApiKeyList(response)
}

export async function createApiKey(
  authorizationHeader: string,
  input: CreateApiKeyInput,
  signal?: AbortSignal,
): Promise<CreatedApiKey> {
  const body = normalizeCreateInput(input)
  const response = await requestJson<unknown>('/api/v1/api-keys', {
    method: 'POST',
    headers: {
      'Authorization': authorizationHeader,
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(body),
    signal,
  })
  return parseCreatedApiKey(response)
}

export async function revokeApiKey(
  authorizationHeader: string,
  keyId: string,
  signal?: AbortSignal,
): Promise<void> {
  await requestJson<void>(`/api/v1/api-keys/${encodeURIComponent(keyId)}`, {
    method: 'DELETE',
    headers: { Authorization: authorizationHeader },
    signal,
  })
}
