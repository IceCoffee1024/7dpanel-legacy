import type { OnlinePlayer, PlayerIdentity } from './onlinePlayers'

import { requestJson } from '../../../shared/api/http'

export interface KickPlayerInput {
  entityId: number
  expectedPlatformIdentity: PlayerIdentity
  reason: string
}

export interface KickPlayerResponse {
  operationId: string
  status: 'succeeded'
  target: Pick<OnlinePlayer, 'entityId' | 'name' | 'platformIdentity'>
  requestedAtUtc: string
  completedAtUtc: string
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function isValidUtcTimestamp(value: string): boolean {
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

function parseIdentity(value: unknown): PlayerIdentity {
  if (!isRecord(value)
    || typeof value.combinedId !== 'string'
    || value.combinedId.trim() === ''
    || typeof value.platform !== 'string'
    || value.platform.trim() === '') {
    throw new Error('Invalid kick player response')
  }

  return Object.freeze({
    combinedId: value.combinedId,
    platform: value.platform,
  })
}

export function parseKickPlayerResponse(value: unknown): KickPlayerResponse {
  if (!isRecord(value)
    || typeof value.operationId !== 'string'
    || !/^[0-9a-f]{32}$/.test(value.operationId)
    || value.status !== 'succeeded'
    || !isRecord(value.target)
    || typeof value.target.entityId !== 'number'
    || !Number.isInteger(value.target.entityId)
    || value.target.entityId < 0
    || typeof value.target.name !== 'string'
    || value.target.name.trim() === ''
    || typeof value.requestedAtUtc !== 'string'
    || !isValidUtcTimestamp(value.requestedAtUtc)
    || typeof value.completedAtUtc !== 'string'
    || !isValidUtcTimestamp(value.completedAtUtc)) {
    throw new Error('Invalid kick player response')
  }

  return Object.freeze({
    operationId: value.operationId,
    status: 'succeeded',
    target: Object.freeze({
      entityId: value.target.entityId,
      name: value.target.name,
      platformIdentity: parseIdentity(value.target.platformIdentity),
    }),
    requestedAtUtc: value.requestedAtUtc,
    completedAtUtc: value.completedAtUtc,
  })
}

export async function kickPlayer(
  authorizationHeader: string,
  input: KickPlayerInput,
  signal?: AbortSignal,
): Promise<KickPlayerResponse> {
  if (!Number.isSafeInteger(input.entityId) || input.entityId < 0)
    throw new Error('Invalid kick player entity id')

  const response = await requestJson<unknown>(`/api/v1/players/${input.entityId}/kick`, {
    method: 'POST',
    headers: {
      'Authorization': authorizationHeader,
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({
      expectedPlatformIdentity: {
        combinedId: input.expectedPlatformIdentity.combinedId,
        platform: input.expectedPlatformIdentity.platform,
      },
      reason: input.reason,
      confirmed: true,
    }),
    signal,
  })
  return parseKickPlayerResponse(response)
}
