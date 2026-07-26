import { requestJson } from '../../../shared/api/http'

export type ServerConfigurationValueType = 'text' | 'integer' | 'decimal' | 'boolean' | 'enum'

export interface ServerConfigurationField {
  key: string
  value: string
  group: string
  valueType: ServerConfigurationValueType
  editable: boolean
  advanced: boolean
  sensitive: boolean
  isSet: boolean
  restartRequired: boolean
  allowedValues: readonly string[]
  minimum: number | null
  maximum: number | null
}

export interface ServerConfigurationSnapshot {
  version: string
  readAtUtc: string
  fields: readonly ServerConfigurationField[]
}

export interface ServerConfigurationUpdateResult {
  version: string
  savedAtUtc: string
  restartRequired: boolean
}

const valueTypes = new Set<ServerConfigurationValueType>(['text', 'integer', 'decimal', 'boolean', 'enum'])

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function isTimestamp(value: unknown): value is string {
  return typeof value === 'string' && /(?:Z|[+-]00:00)$/.test(value) && Number.isFinite(Date.parse(value))
}

function parseNullableNumber(value: unknown): number | null {
  if (value === null)
    return null
  if (typeof value !== 'number' || !Number.isFinite(value))
    throw new Error('Invalid server configuration response')
  return value
}

function parseField(value: unknown): ServerConfigurationField {
  if (!isRecord(value)
    || typeof value.key !== 'string'
    || value.key.length === 0
    || typeof value.value !== 'string'
    || typeof value.group !== 'string'
    || value.group.length === 0
    || typeof value.valueType !== 'string'
    || !valueTypes.has(value.valueType as ServerConfigurationValueType)
    || typeof value.editable !== 'boolean'
    || typeof value.advanced !== 'boolean'
    || typeof value.sensitive !== 'boolean'
    || typeof value.isSet !== 'boolean'
    || typeof value.restartRequired !== 'boolean'
    || !Array.isArray(value.allowedValues)
    || !value.allowedValues.every(item => typeof item === 'string')
    || (value.sensitive && value.value !== '')) {
    throw new Error('Invalid server configuration response')
  }

  return Object.freeze({
    key: value.key,
    value: value.value,
    group: value.group,
    valueType: value.valueType as ServerConfigurationValueType,
    editable: value.editable,
    advanced: value.advanced,
    sensitive: value.sensitive,
    isSet: value.isSet,
    restartRequired: value.restartRequired,
    allowedValues: Object.freeze([...value.allowedValues] as string[]),
    minimum: parseNullableNumber(value.minimum),
    maximum: parseNullableNumber(value.maximum),
  })
}

export function parseServerConfigurationSnapshot(value: unknown): ServerConfigurationSnapshot {
  if (!isRecord(value)
    || typeof value.version !== 'string'
    || !/^[a-f\d]{64}$/i.test(value.version)
    || !isTimestamp(value.readAtUtc)
    || !Array.isArray(value.fields)) {
    throw new Error('Invalid server configuration response')
  }
  return Object.freeze({
    version: value.version,
    readAtUtc: value.readAtUtc,
    fields: Object.freeze(value.fields.map(parseField)),
  })
}

function parseUpdateResult(value: unknown): ServerConfigurationUpdateResult {
  if (!isRecord(value)
    || typeof value.version !== 'string'
    || !/^[a-f\d]{64}$/i.test(value.version)
    || !isTimestamp(value.savedAtUtc)
    || typeof value.restartRequired !== 'boolean') {
    throw new Error('Invalid server configuration update response')
  }
  return Object.freeze({
    version: value.version,
    savedAtUtc: value.savedAtUtc,
    restartRequired: value.restartRequired,
  })
}

export async function fetchServerConfiguration(authorizationHeader: string, signal?: AbortSignal) {
  const response = await requestJson<unknown>('/api/v1/server-configuration', {
    headers: { Authorization: authorizationHeader },
    signal,
  })
  return parseServerConfigurationSnapshot(response)
}

export async function updateServerConfigurationField(
  authorizationHeader: string,
  key: string,
  value: string,
  version: string,
  signal?: AbortSignal,
) {
  const response = await requestJson<unknown>(`/api/v1/server-configuration/${encodeURIComponent(key)}`, {
    method: 'PUT',
    headers: { 'Authorization': authorizationHeader, 'Content-Type': 'application/json' },
    body: JSON.stringify({ value, version }),
    signal,
  })
  return parseUpdateResult(response)
}
