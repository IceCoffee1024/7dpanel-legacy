import { requestJson } from '../../../shared/api/http'

export type PanelRole = 'Owner' | 'Admin' | 'Viewer'

export interface PanelUser {
  subject: string
  username: string
  role: PanelRole
  enabled: boolean
  updatedAtUtc: string
}

export interface GameAdmin {
  playerId: string
  displayName: string
  permissionLevel: number
}

export interface CommandPermission {
  command: string
  permissionLevel: number
  description: string | null
}

const roles = new Set<PanelRole>(['Owner', 'Admin', 'Viewer'])

function record(value: unknown): Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value))
    throw new Error('Invalid permissions response')
  return value as Record<string, unknown>
}

function level(value: unknown): number {
  if (!Number.isInteger(value) || (value as number) < 0 || (value as number) > 2000)
    throw new Error('Invalid game permission level')
  return value as number
}

export function parsePanelUser(value: unknown): PanelUser {
  const source = record(value)
  if (typeof source.subject !== 'string' || !source.subject
    || typeof source.username !== 'string' || !source.username
    || typeof source.role !== 'string' || !roles.has(source.role as PanelRole)
    || typeof source.enabled !== 'boolean'
    || typeof source.updatedAtUtc !== 'string' || !Number.isFinite(Date.parse(source.updatedAtUtc))
    || 'password' in source || 'passwordHash' in source) {
    throw new Error('Invalid panel user response')
  }
  return Object.freeze(source as unknown as PanelUser)
}

export function parseGameAdmin(value: unknown): GameAdmin {
  const source = record(value)
  if (typeof source.playerId !== 'string' || !source.playerId
    || typeof source.displayName !== 'string')
    throw new Error('Invalid game administrator response')
  return Object.freeze({
    playerId: source.playerId,
    displayName: source.displayName,
    permissionLevel: level(source.permissionLevel),
  })
}

export function parseCommandPermission(value: unknown): CommandPermission {
  const source = record(value)
  if (typeof source.command !== 'string' || !source.command
    || (source.description !== null && typeof source.description !== 'string'))
    throw new Error('Invalid command permission response')
  return Object.freeze({
    command: source.command,
    permissionLevel: level(source.permissionLevel),
    description: source.description as string | null,
  })
}

async function list<T>(path: string, authorization: string, parser: (value: unknown) => T) {
  const value = await requestJson<unknown>(path, { headers: { Authorization: authorization } })
  if (!Array.isArray(value))
    throw new Error('Invalid permissions response')
  return Object.freeze(value.map(parser))
}

export const fetchPanelUsers = (authorization: string) =>
  list('/api/v1/panel-users', authorization, parsePanelUser)
export const fetchGameAdmins = (authorization: string) =>
  list('/api/v1/game-permissions/admins', authorization, parseGameAdmin)
export const fetchCommandPermissions = (authorization: string) =>
  list('/api/v1/game-permissions/commands', authorization, parseCommandPermission)

export async function createPanelUser(authorization: string, input: { username: string, password: string, role: PanelRole, enabled: boolean }) {
  return parsePanelUser(await requestJson('/api/v1/panel-users', {
    method: 'POST', headers: { Authorization: authorization, 'Content-Type': 'application/json' }, body: JSON.stringify(input),
  }))
}

export async function updatePanelUser(authorization: string, user: PanelUser) {
  return parsePanelUser(await requestJson(`/api/v1/panel-users/${encodeURIComponent(user.subject)}`, {
    method: 'PUT', headers: { Authorization: authorization, 'Content-Type': 'application/json' },
    body: JSON.stringify({ username: user.username, role: user.role, enabled: user.enabled }),
  }))
}

export const resetPanelUserPassword = (authorization: string, subject: string, password: string) =>
  requestJson<void>(`/api/v1/panel-users/${encodeURIComponent(subject)}/password`, {
    method: 'POST', headers: { Authorization: authorization, 'Content-Type': 'application/json' }, body: JSON.stringify({ password }),
  })

export const deletePanelUser = (authorization: string, subject: string) =>
  requestJson<void>(`/api/v1/panel-users/${encodeURIComponent(subject)}`, {
    method: 'DELETE', headers: { Authorization: authorization }, expectedStatus: 204,
  })

export async function upsertGameAdmin(authorization: string, admin: GameAdmin) {
  return requestJson<void>(`/api/v1/game-permissions/admins/${encodeURIComponent(admin.playerId)}`, {
    method: 'PUT', headers: { Authorization: authorization, 'Content-Type': 'application/json' }, body: JSON.stringify(admin),
  })
}

export async function removeGameAdmin(authorization: string, playerId: string) {
  return requestJson<void>(`/api/v1/game-permissions/admins/${encodeURIComponent(playerId)}`, {
    method: 'DELETE', headers: { Authorization: authorization }, expectedStatus: 204,
  })
}

export async function upsertCommandPermission(authorization: string, item: CommandPermission) {
  return requestJson<void>(`/api/v1/game-permissions/commands/${encodeURIComponent(item.command)}`, {
    method: 'PUT', headers: { Authorization: authorization, 'Content-Type': 'application/json' },
    body: JSON.stringify({ permissionLevel: item.permissionLevel }),
  })
}

export async function removeCommandPermission(authorization: string, command: string) {
  return requestJson<void>(`/api/v1/game-permissions/commands/${encodeURIComponent(command)}`, {
    method: 'DELETE', headers: { Authorization: authorization }, expectedStatus: 204,
  })
}
