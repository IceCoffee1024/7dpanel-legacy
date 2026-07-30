import { requestJson } from '../../../shared/api/http'

export interface ModMetadata {
  directoryId: string
  name: string
  displayName: string
  author: string
  version: string
  website: string | null
  description: string | null
  isLoadedNow: boolean | null
  isEnabledNextStart: boolean
  isProtected: boolean
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function optionalString(value: unknown): string | null {
  if (value === null)
    return null
  if (typeof value !== 'string')
    throw new Error('Invalid mod response')
  return value
}

function validDirectoryId(value: string): boolean {
  return value.trim() === value
    && value.length > 0
    && value !== '.'
    && value !== '..'
    && !value.includes('/')
    && !value.includes('\\')
    && !/^[a-z]:/i.test(value)
}

function parseMod(value: unknown): ModMetadata {
  if (!isRecord(value)
    || typeof value.directoryId !== 'string'
    || !validDirectoryId(value.directoryId)
    || typeof value.name !== 'string'
    || value.name.trim() === ''
    || typeof value.displayName !== 'string'
    || typeof value.author !== 'string'
    || typeof value.version !== 'string'
    || (typeof value.isLoadedNow !== 'boolean' && value.isLoadedNow !== null)
    || typeof value.isEnabledNextStart !== 'boolean'
    || typeof value.isProtected !== 'boolean') {
    throw new Error('Invalid mod response')
  }

  return Object.freeze({
    directoryId: value.directoryId,
    name: value.name,
    displayName: value.displayName,
    author: value.author,
    version: value.version,
    website: optionalString(value.website),
    description: optionalString(value.description),
    isLoadedNow: value.isLoadedNow,
    isEnabledNextStart: value.isEnabledNextStart,
    isProtected: value.isProtected,
  })
}

export function parseModList(value: unknown): readonly ModMetadata[] {
  if (!Array.isArray(value))
    throw new Error('Invalid mod response')
  return Object.freeze(value.map(parseMod))
}

export async function fetchMods(authorizationHeader: string, signal?: AbortSignal): Promise<readonly ModMetadata[]> {
  const response = await requestJson<unknown>('/api/v1/mods', {
    headers: { Authorization: authorizationHeader },
    signal,
  })
  return parseModList(response)
}

export async function setModEnabled(
  authorizationHeader: string,
  directoryId: string,
  enabled: boolean,
  signal?: AbortSignal,
): Promise<void> {
  await requestJson(`/api/v1/mods/${encodeURIComponent(directoryId)}/state`, {
    method: 'PUT',
    headers: { 'Authorization': authorizationHeader, 'Content-Type': 'application/json' },
    body: JSON.stringify({ enabled }),
    signal,
  })
}
