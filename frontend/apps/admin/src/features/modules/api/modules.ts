import { requestJson } from '../../../shared/api/http'

export const featureModuleIds = [
  'IdentityAndAuthorization',
  'Audit',
  'RuntimeHealth',
  'Overview',
  'PlayerHistoryAndMap',
  'Console',
  'Chat',
  'GameResources',
  'Backups',
  'AnnouncementsAndScheduling',
  'PlayerItems',
  'EconomyAndRewards',
  'TeleportAndVoting',
  'Automation',
  'Discord',
  'GeoIp',
  'WorldTools',
] as const

export type FeatureModuleId = typeof featureModuleIds[number]
export type FeatureModuleDisableMode = 'Immediate' | 'Drain' | 'RestartRequired'
export type FeatureModuleLifecycleState = 'Enabled' | 'Disabled' | 'Draining' | 'RestartRequired'

export interface FeatureModule {
  moduleId: FeatureModuleId
  isToggleable: boolean
  dependencies: readonly FeatureModuleId[]
  settingsSummaryFields: readonly string[]
  healthSource: string
  disableMode: FeatureModuleDisableMode
  dataRetentionSummary: string
  consumerIds: readonly string[]
  isEnabled: boolean
  lifecycleState: FeatureModuleLifecycleState
  updatedBy: string
  correlationId: string
  updatedAtUtc: string
  rowVersion: number
}

const moduleIdSet = new Set<string>(featureModuleIds)
const disableModes = new Set<FeatureModuleDisableMode>(['Immediate', 'Drain', 'RestartRequired'])
const lifecycleStates = new Set<FeatureModuleLifecycleState>(['Enabled', 'Disabled', 'Draining', 'RestartRequired'])

function record(value: unknown): Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value))
    throw new Error('Invalid feature module response')
  return value as Record<string, unknown>
}

function text(value: unknown): string {
  if (typeof value !== 'string' || value.trim() === '')
    throw new Error('Invalid feature module response')
  return value
}

function textArray(value: unknown): readonly string[] {
  if (!Array.isArray(value))
    throw new Error('Invalid feature module response')
  const items = value.map(text)
  if (new Set(items).size !== items.length)
    throw new Error('Invalid feature module response')
  return Object.freeze(items)
}

function moduleId(value: unknown): FeatureModuleId {
  if (typeof value !== 'string' || !moduleIdSet.has(value))
    throw new Error('Invalid feature module ID')
  return value as FeatureModuleId
}

function moduleIdArray(value: unknown): readonly FeatureModuleId[] {
  const items = textArray(value).map(moduleId)
  return Object.freeze(items)
}

function boolean(value: unknown): boolean {
  if (typeof value !== 'boolean')
    throw new Error('Invalid feature module response')
  return value
}

function rowVersion(value: unknown): number {
  if (typeof value !== 'number' || !Number.isSafeInteger(value) || value < 0)
    throw new Error('Invalid feature module row version')
  return value
}

function utc(value: unknown): string {
  const result = text(value)
  if (Number.isNaN(Date.parse(result)) || !/(?:Z|[+-]\d{2}:\d{2})$/i.test(result))
    throw new Error('Invalid feature module timestamp')
  return result
}

function disableMode(value: unknown): FeatureModuleDisableMode {
  if (typeof value !== 'string' || !disableModes.has(value as FeatureModuleDisableMode))
    throw new Error('Invalid feature module disable mode')
  return value as FeatureModuleDisableMode
}

function lifecycleState(value: unknown): FeatureModuleLifecycleState {
  if (typeof value !== 'string' || !lifecycleStates.has(value as FeatureModuleLifecycleState))
    throw new Error('Invalid feature module lifecycle state')
  return value as FeatureModuleLifecycleState
}

export function parseFeatureModule(value: unknown): FeatureModule {
  const source = record(value)
  return Object.freeze({
    moduleId: moduleId(source.moduleId),
    isToggleable: boolean(source.isToggleable),
    dependencies: moduleIdArray(source.dependencies),
    settingsSummaryFields: textArray(source.settingsSummaryFields),
    healthSource: text(source.healthSource),
    disableMode: disableMode(source.disableMode),
    dataRetentionSummary: text(source.dataRetentionSummary),
    consumerIds: textArray(source.consumerIds),
    isEnabled: boolean(source.isEnabled),
    lifecycleState: lifecycleState(source.lifecycleState),
    updatedBy: text(source.updatedBy),
    correlationId: text(source.correlationId),
    updatedAtUtc: utc(source.updatedAtUtc),
    rowVersion: rowVersion(source.rowVersion),
  })
}

export function parseFeatureModules(value: unknown): readonly FeatureModule[] {
  if (!Array.isArray(value))
    throw new Error('Invalid feature modules response')
  const parsed = value.map(parseFeatureModule)
  const byId = new Map(parsed.map(module => [module.moduleId, module]))
  if (parsed.length !== featureModuleIds.length || byId.size !== featureModuleIds.length)
    throw new Error('The feature module response is incomplete')
  return Object.freeze(featureModuleIds.map(id => byId.get(id)!))
}

export async function fetchFeatureModules(
  authorizationHeader: string,
  signal?: AbortSignal,
): Promise<readonly FeatureModule[]> {
  const response = await requestJson<unknown>('/api/v1/modules', {
    headers: { Authorization: authorizationHeader },
    signal,
  })
  return parseFeatureModules(response)
}

async function setFeatureModuleState(
  authorizationHeader: string,
  moduleIdValue: FeatureModuleId,
  action: 'enable' | 'disable',
  expectedRowVersion: number,
  signal?: AbortSignal,
): Promise<FeatureModule> {
  const response = await requestJson<unknown>(
    `/api/v1/modules/${encodeURIComponent(moduleIdValue)}/${action}`,
    {
      method: 'POST',
      headers: { 'Authorization': authorizationHeader, 'Content-Type': 'application/json' },
      body: JSON.stringify({ expectedRowVersion }),
      signal,
    },
  )
  return parseFeatureModule(response)
}

export function enableFeatureModule(
  authorizationHeader: string,
  moduleIdValue: FeatureModuleId,
  expectedRowVersion: number,
  signal?: AbortSignal,
): Promise<FeatureModule> {
  return setFeatureModuleState(authorizationHeader, moduleIdValue, 'enable', expectedRowVersion, signal)
}

export function disableFeatureModule(
  authorizationHeader: string,
  moduleIdValue: FeatureModuleId,
  expectedRowVersion: number,
  signal?: AbortSignal,
): Promise<FeatureModule> {
  return setFeatureModuleState(authorizationHeader, moduleIdValue, 'disable', expectedRowVersion, signal)
}
