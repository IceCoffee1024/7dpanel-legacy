import type {
  HomeTeleportExperience,
  PlayerHome,
  TeleportOperation,
  TeleportSettings,
  TeleportSettingsInput,
} from './community.types'

import { requestJson } from '../../../shared/api/http'

import {
  bool,
  collection,
  ensureChronology,
  enumValue,
  headers,
  integer,
  invalid,
  long,
  nullableCode,
  nullableText,
  nullableUtc,
  parseWorldPosition,
  queryPath,
  record,
  text,
  utc,
  wireInteger,
} from './community.protocol'
import { TELEPORT_KINDS, TELEPORT_OPERATION_STATES } from './community.types'

const teleportSettingsKeys = ['kind', 'enabled', 'maxHomes', 'cooldownMs', 'globalCooldownMs', 'denyDuringBloodMoon', 'feeAmount', 'homeExperience', 'updatedAtUtc', 'rowVersion'] as const
const homeExperienceKeys = ['setFeeAmount', 'listCommandName', 'setCommandName', 'deleteCommandName', 'teleportCommandName', 'noHomesMessage', 'limitMessage', 'setSuccessMessage', 'overwriteMessage', 'deleteSuccessMessage', 'notFoundMessage', 'cooldownMessage', 'teleportSuccessMessage', 'setInsufficientFundsMessage', 'teleportInsufficientFundsMessage', 'bloodMoonMessage'] as const
const homeKeys = ['homeId', 'crossplatformId', 'name', 'position', 'createdAtUtc', 'updatedAtUtc', 'rowVersion'] as const
const operationKeys = ['operationId', 'kind', 'crossplatformId', 'targetCrossplatformId', 'destination', 'origin', 'state', 'errorCode', 'correlationId', 'createdAtUtc', 'updatedAtUtc', 'completedAtUtc', 'rowVersion'] as const

function parseHomeExperience(value: unknown): HomeTeleportExperience {
  const source = record(value, homeExperienceKeys)
  return Object.freeze({
    setFeeAmount: long(source.setFeeAmount),
    listCommandName: text(source.listCommandName),
    setCommandName: text(source.setCommandName),
    deleteCommandName: text(source.deleteCommandName),
    teleportCommandName: text(source.teleportCommandName),
    noHomesMessage: text(source.noHomesMessage),
    limitMessage: text(source.limitMessage),
    setSuccessMessage: text(source.setSuccessMessage),
    overwriteMessage: text(source.overwriteMessage),
    deleteSuccessMessage: text(source.deleteSuccessMessage),
    notFoundMessage: text(source.notFoundMessage),
    cooldownMessage: text(source.cooldownMessage),
    teleportSuccessMessage: text(source.teleportSuccessMessage),
    setInsufficientFundsMessage: text(source.setInsufficientFundsMessage),
    teleportInsufficientFundsMessage: text(source.teleportInsufficientFundsMessage),
    bloodMoonMessage: text(source.bloodMoonMessage),
  })
}

export function parseTeleportSettings(value: unknown): TeleportSettings {
  const source = record(value, teleportSettingsKeys)
  const kind = enumValue(source.kind, TELEPORT_KINDS)
  const maxHomes = source.maxHomes === null ? null : integer(source.maxHomes, 0)
  if (kind !== 'Home' && maxHomes !== null)
    return invalid()
  const homeExperience = source.homeExperience === null ? null : parseHomeExperience(source.homeExperience)
  if ((kind === 'Home') !== (homeExperience !== null))
    return invalid()
  return Object.freeze({
    kind,
    enabled: bool(source.enabled),
    maxHomes,
    cooldownMs: long(source.cooldownMs),
    globalCooldownMs: long(source.globalCooldownMs),
    denyDuringBloodMoon: bool(source.denyDuringBloodMoon),
    feeAmount: long(source.feeAmount),
    homeExperience,
    updatedAtUtc: utc(source.updatedAtUtc),
    rowVersion: long(source.rowVersion),
  })
}

export function parsePlayerHome(value: unknown): PlayerHome {
  const source = record(value, homeKeys)
  const createdAtUtc = utc(source.createdAtUtc)
  const updatedAtUtc = utc(source.updatedAtUtc)
  ensureChronology(createdAtUtc, updatedAtUtc)
  return Object.freeze({
    homeId: text(source.homeId),
    crossplatformId: text(source.crossplatformId),
    name: text(source.name),
    position: parseWorldPosition(source.position),
    createdAtUtc,
    updatedAtUtc,
    rowVersion: long(source.rowVersion),
  })
}

export function parseTeleportOperation(value: unknown): TeleportOperation {
  const source = record(value, operationKeys)
  const createdAtUtc = utc(source.createdAtUtc)
  const updatedAtUtc = utc(source.updatedAtUtc)
  const completedAtUtc = nullableUtc(source.completedAtUtc)
  ensureChronology(createdAtUtc, updatedAtUtc)
  if (completedAtUtc !== null)
    ensureChronology(createdAtUtc, completedAtUtc)
  return Object.freeze({
    operationId: text(source.operationId),
    kind: enumValue(source.kind, TELEPORT_KINDS),
    crossplatformId: text(source.crossplatformId),
    targetCrossplatformId: nullableText(source.targetCrossplatformId),
    destination: parseWorldPosition(source.destination),
    origin: source.origin === null ? null : parseWorldPosition(source.origin),
    state: enumValue(source.state, TELEPORT_OPERATION_STATES),
    errorCode: nullableCode(source.errorCode),
    correlationId: nullableText(source.correlationId),
    createdAtUtc,
    updatedAtUtc,
    completedAtUtc,
    rowVersion: long(source.rowVersion),
  })
}

export async function fetchTeleportSettings(authorization: string, signal?: AbortSignal): Promise<readonly TeleportSettings[]> {
  const settings = collection(await requestJson<unknown>('/api/v1/community/teleport-settings', {
    headers: headers(authorization),
    expectedStatus: 200,
    signal,
  }), parseTeleportSettings)
  if (settings.length !== TELEPORT_KINDS.length
    || new Set(settings.map(value => value.kind)).size !== TELEPORT_KINDS.length) {
    return invalid()
  }
  return settings
}

export async function updateTeleportSetting(
  authorization: string,
  current: TeleportSettings,
  input: TeleportSettingsInput,
  signal?: AbortSignal,
): Promise<TeleportSettings> {
  const response = await requestJson<unknown>(`/api/v1/community/teleport-settings/${current.kind}`, {
    method: 'PUT',
    headers: headers(authorization, true),
    body: JSON.stringify({
      enabled: input.enabled,
      maxHomes: input.maxHomes,
      cooldownMs: wireInteger(input.cooldownMs),
      globalCooldownMs: wireInteger(input.globalCooldownMs),
      denyDuringBloodMoon: input.denyDuringBloodMoon,
      feeAmount: wireInteger(input.feeAmount),
      homeExperience: input.homeExperience == null
        ? null
        : { ...input.homeExperience, setFeeAmount: wireInteger(input.homeExperience.setFeeAmount) },
      expectedRowVersion: wireInteger(current.rowVersion),
    }),
    expectedStatus: 200,
    signal,
  })
  const authoritative = parseTeleportSettings(response)
  if (authoritative.kind !== current.kind || authoritative.rowVersion <= current.rowVersion)
    return invalid()
  return authoritative
}

export async function fetchHomes(authorization: string, crossplatformId: string, signal?: AbortSignal): Promise<readonly PlayerHome[]> {
  const result = collection(await requestJson<unknown>(queryPath('/api/v1/community/homes', { crossplatformId }), {
    headers: headers(authorization),
    expectedStatus: 200,
    signal,
  }), parsePlayerHome)
  if (result.some(value => value.crossplatformId !== crossplatformId))
    return invalid()
  return result
}

export async function fetchTeleportOperation(authorization: string, operationId: string, signal?: AbortSignal): Promise<TeleportOperation> {
  const response = await requestJson<unknown>(`/api/v1/community/teleport-operations/${encodeURIComponent(operationId)}`, {
    headers: headers(authorization),
    expectedStatus: 200,
    signal,
  })
  const authoritative = parseTeleportOperation(response)
  if (authoritative.operationId !== operationId)
    return invalid()
  return authoritative
}

export async function fetchTeleportOperations(authorization: string, signal?: AbortSignal): Promise<readonly TeleportOperation[]> {
  return collection(await requestJson<unknown>('/api/v1/community/teleport-operations', {
    headers: headers(authorization),
    expectedStatus: 200,
    signal,
  }), parseTeleportOperation)
}
