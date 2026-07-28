import { requestJson } from '../../../shared/api/http'

export type DiscordMode = 'Webhook' | 'Bot'
export type DiscordDeliveryStatus = 'Pending' | 'Sending' | 'RetryScheduled' | 'Succeeded' | 'Failed' | 'ResultUnknown' | 'Cancelled'
export type DiscordHealthState = 'Disabled' | 'Connecting' | 'Connected' | 'Healthy' | 'Degraded' | 'Unavailable'
export type SecretOperation = Readonly<{ operation: 'Keep' } | { operation: 'Replace', value: string } | { operation: 'Clear' }>
export interface DiscordTarget {
  readonly targetKey: string
  readonly deliveryMode: DiscordMode
  readonly channelId: string | null
  readonly isEnabled: boolean
  readonly hasCredential: boolean
}
export interface DiscordConfiguration {
  readonly version: number
  readonly isEnabled: boolean
  readonly mode: DiscordMode
  readonly applicationId: string | null
  readonly guildId: string | null
  readonly publicChannelId: string | null
  readonly bridgeGameToDiscord: boolean
  readonly bridgeDiscordToGame: boolean
  readonly proxy: Readonly<{ isEnabled: boolean, endpoint: string | null, hasCredentials: boolean }>
  readonly hasBotToken: boolean
  readonly targets: readonly DiscordTarget[]
  readonly updatedAtUtc: string | null
}
export interface DiscordConfigurationDraft extends Omit<DiscordConfiguration, 'version' | 'hasBotToken' | 'updatedAtUtc' | 'targets'> {
  readonly expectedVersion: number
  readonly targets: readonly Omit<DiscordTarget, 'hasCredential'>[]
}
export interface DiscordDelivery {
  readonly deliveryId: string
  readonly businessKey: string
  readonly targetKey: string
  readonly status: DiscordDeliveryStatus
  readonly nextAttemptAtUtc: string | null
  readonly retryCount: number
  readonly createdAtUtc: string
  readonly completedAtUtc: string | null
}
export interface DiscordBinding {
  readonly discordSubject: string
  readonly crossplatformId: string
  readonly isActive: boolean
  readonly createdAtUtc: string
  readonly updatedAtUtc: string
}
export interface DiscordCommand { readonly commandKey: string, readonly isEnabled: boolean, readonly remoteAllowed: boolean }
export interface DiscordBindingCode { readonly code: string, readonly codePrefix: string, readonly expiresAtUtc: string }
export interface DiscordHealth {
  readonly gateway: Readonly<{ state: DiscordHealthState, errorCode: string | null, observedAtUtc: string | null }>
  readonly inbound: Readonly<{ state: DiscordHealthState, errorCode: string | null, observedAtUtc: string | null }>
}

const modes = new Set<DiscordMode>(['Webhook', 'Bot'])
const deliveryStatuses = new Set<DiscordDeliveryStatus>(['Pending', 'Sending', 'RetryScheduled', 'Succeeded', 'Failed', 'ResultUnknown', 'Cancelled'])
const healthStates = new Set<DiscordHealthState>(['Disabled', 'Connecting', 'Connected', 'Healthy', 'Degraded', 'Unavailable'])
function invalid(): never { throw new Error('Invalid server protocol') }
function record(value: unknown): Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) invalid()
  return value as Record<string, unknown>
}
function keys(value: Record<string, unknown>, allowed: readonly string[]) {
  if (Object.keys(value).some(key => !allowed.includes(key))) invalid()
}
function text(value: unknown): string { return typeof value === 'string' ? value : invalid() }
function boolean(value: unknown): boolean { return typeof value === 'boolean' ? value : invalid() }
function integer(value: unknown, minimum = 0): number { return typeof value === 'number' && Number.isSafeInteger(value) && value >= minimum ? value : invalid() }
function nullableText(value: unknown): string | null { return value === null ? null : text(value) }
function nullableUtc(value: unknown): string | null {
  if (value === null) return null
  const result = text(value)
  return Number.isFinite(Date.parse(result)) && /(?:Z|[+-]00:00)$/.test(result) ? result : invalid()
}

export function parseDiscordConfiguration(value: unknown): DiscordConfiguration {
  const source = record(value)
  keys(source, ['version', 'isEnabled', 'mode', 'applicationId', 'guildId', 'publicChannelId', 'bridgeGameToDiscord', 'bridgeDiscordToGame', 'proxy', 'hasBotToken', 'targets', 'updatedAtUtc'])
  if (typeof source.mode !== 'string' || !modes.has(source.mode as DiscordMode) || !Array.isArray(source.targets)) invalid()
  const proxy = record(source.proxy); keys(proxy, ['isEnabled', 'endpoint', 'hasCredentials'])
  const targets = Object.freeze(source.targets.map((value) => {
    const target = record(value); keys(target, ['targetKey', 'deliveryMode', 'channelId', 'isEnabled', 'hasCredential'])
    if (typeof target.deliveryMode !== 'string' || !modes.has(target.deliveryMode as DiscordMode)) invalid()
    return Object.freeze({
      targetKey: text(target.targetKey), deliveryMode: target.deliveryMode as DiscordMode, channelId: nullableText(target.channelId),
      isEnabled: boolean(target.isEnabled), hasCredential: boolean(target.hasCredential),
    })
  }))
  return Object.freeze({
    version: integer(source.version), isEnabled: boolean(source.isEnabled), mode: source.mode as DiscordMode,
    applicationId: nullableText(source.applicationId), guildId: nullableText(source.guildId), publicChannelId: nullableText(source.publicChannelId),
    bridgeGameToDiscord: boolean(source.bridgeGameToDiscord), bridgeDiscordToGame: boolean(source.bridgeDiscordToGame),
    proxy: Object.freeze({ isEnabled: boolean(proxy.isEnabled), endpoint: nullableText(proxy.endpoint), hasCredentials: boolean(proxy.hasCredentials) }),
    hasBotToken: boolean(source.hasBotToken), targets, updatedAtUtc: nullableUtc(source.updatedAtUtc),
  })
}

function parseDelivery(value: unknown): DiscordDelivery {
  const source = record(value); keys(source, ['deliveryId', 'businessKey', 'targetKey', 'status', 'nextAttemptAtUtc', 'retryCount', 'createdAtUtc', 'completedAtUtc'])
  if (typeof source.status !== 'string' || !deliveryStatuses.has(source.status as DiscordDeliveryStatus)) invalid()
  return Object.freeze({ deliveryId: text(source.deliveryId), businessKey: text(source.businessKey), targetKey: text(source.targetKey), status: source.status as DiscordDeliveryStatus, nextAttemptAtUtc: nullableUtc(source.nextAttemptAtUtc), retryCount: integer(source.retryCount), createdAtUtc: nullableUtc(source.createdAtUtc) ?? invalid(), completedAtUtc: nullableUtc(source.completedAtUtc) })
}

function parseDeliveries(value: unknown): readonly DiscordDelivery[] {
  if (!Array.isArray(value)) invalid()
  return Object.freeze(value.map(parseDelivery))
}

function parseBindings(value: unknown): readonly DiscordBinding[] {
  if (!Array.isArray(value)) invalid()
  return Object.freeze(value.map((entry) => {
    const source = record(entry); keys(source, ['discordSubject', 'crossplatformId', 'isActive', 'createdAtUtc', 'updatedAtUtc'])
    return Object.freeze({ discordSubject: text(source.discordSubject), crossplatformId: text(source.crossplatformId), isActive: boolean(source.isActive), createdAtUtc: nullableUtc(source.createdAtUtc) ?? invalid(), updatedAtUtc: nullableUtc(source.updatedAtUtc) ?? invalid() })
  }))
}

function parseCommands(value: unknown): readonly DiscordCommand[] {
  if (!Array.isArray(value)) invalid()
  return Object.freeze(value.map((entry) => {
    const source = record(entry); keys(source, ['commandKey', 'isEnabled', 'remoteAllowed'])
    const commandKey = text(source.commandKey)
    if (!['bind', 'status', 'players'].includes(commandKey)) invalid()
    return Object.freeze({ commandKey, isEnabled: boolean(source.isEnabled), remoteAllowed: boolean(source.remoteAllowed) })
  }))
}

function headers(authorization: string, json = false): HeadersInit {
  return { Authorization: authorization, ...(json ? { 'Content-Type': 'application/json' } : {}) }
}

export async function getDiscordConfiguration(authorization: string, signal?: AbortSignal): Promise<DiscordConfiguration> {
  return parseDiscordConfiguration(await requestJson<unknown>('/api/v1/integrations/discord', { headers: headers(authorization), signal }))
}
export async function saveDiscordConfiguration(authorization: string, draft: DiscordConfigurationDraft, signal?: AbortSignal): Promise<DiscordConfiguration> {
  const body = {
    expectedVersion: draft.expectedVersion, isEnabled: draft.isEnabled, mode: draft.mode, applicationId: draft.applicationId,
    guildId: draft.guildId, publicChannelId: draft.publicChannelId, bridgeGameToDiscord: draft.bridgeGameToDiscord,
    bridgeDiscordToGame: draft.bridgeDiscordToGame, proxyEnabled: draft.proxy.isEnabled, proxyEndpoint: draft.proxy.endpoint,
    targets: draft.targets,
  }
  return parseDiscordConfiguration(await requestJson<unknown>('/api/v1/integrations/discord', { method: 'PUT', headers: headers(authorization, true), body: JSON.stringify(body), signal }))
}
export async function testDiscordDelivery(authorization: string, targetKey: string, signal?: AbortSignal): Promise<DiscordDelivery> {
  return parseDelivery(await requestJson<unknown>('/api/v1/integrations/discord/test', { method: 'POST', headers: headers(authorization, true), body: JSON.stringify({ targetKey }), expectedStatus: 202, signal }))
}
export async function listDiscordDeliveries(authorization: string, signal?: AbortSignal): Promise<readonly DiscordDelivery[]> {
  return parseDeliveries(await requestJson<unknown>('/api/v1/integrations/discord/deliveries', { headers: headers(authorization), signal }))
}
export async function retryDiscordDelivery(authorization: string, deliveryId: string, signal?: AbortSignal): Promise<DiscordDelivery> {
  return parseDelivery(await requestJson<unknown>(`/api/v1/integrations/discord/deliveries/${encodeURIComponent(deliveryId)}/retry`, { method: 'POST', headers: headers(authorization), signal }))
}
export async function listDiscordBindings(authorization: string, signal?: AbortSignal): Promise<readonly DiscordBinding[]> {
  return parseBindings(await requestJson<unknown>('/api/v1/integrations/discord/bindings', { headers: headers(authorization), signal }))
}
export async function createDiscordBindingCode(authorization: string, crossplatformId: string, signal?: AbortSignal): Promise<DiscordBindingCode> {
  const value = record(await requestJson<unknown>('/api/v1/integrations/discord/binding-codes', { method: 'POST', headers: headers(authorization, true), body: JSON.stringify({ crossplatformId }), expectedStatus: 201, signal }))
  keys(value, ['code', 'codePrefix', 'expiresAtUtc'])
  return Object.freeze({ code: text(value.code), codePrefix: text(value.codePrefix), expiresAtUtc: nullableUtc(value.expiresAtUtc) ?? invalid() })
}
export async function deleteDiscordBinding(authorization: string, discordSubject: string, signal?: AbortSignal): Promise<void> {
  await requestJson<void>(`/api/v1/integrations/discord/bindings/${encodeURIComponent(discordSubject)}`, { method: 'DELETE', headers: headers(authorization), expectedStatus: 204, signal })
}
export async function listDiscordCommands(authorization: string, signal?: AbortSignal): Promise<readonly DiscordCommand[]> {
  return parseCommands(await requestJson<unknown>('/api/v1/integrations/discord/commands', { headers: headers(authorization), signal }))
}

function parseHealthSection(value: unknown) {
  const source = record(value); keys(source, ['state', 'errorCode', 'observedAtUtc'])
  if (typeof source.state !== 'string' || !healthStates.has(source.state as DiscordHealthState)) invalid()
  return Object.freeze({ state: source.state as DiscordHealthState, errorCode: nullableText(source.errorCode), observedAtUtc: nullableUtc(source.observedAtUtc) })
}

export function parseDiscordHealth(value: unknown): DiscordHealth {
  const source = record(value)
  keys(source, ['gateway', 'inbound'])
  return Object.freeze({ gateway: parseHealthSection(source.gateway), inbound: parseHealthSection(source.inbound) })
}

export async function getDiscordHealth(authorization: string, signal?: AbortSignal): Promise<DiscordHealth> {
  return parseDiscordHealth(await requestJson<unknown>('/api/v1/integrations/discord/health', { headers: headers(authorization), signal }))
}

export async function updateDiscordSecret(authorization: string, secretKey: string, operation: SecretOperation, signal?: AbortSignal): Promise<void> {
  if (operation.operation === 'Keep') return
  await requestJson<void>(`/api/v1/integrations/discord/secrets/${encodeURIComponent(secretKey)}`, {
    method: operation.operation === 'Clear' ? 'DELETE' : 'PUT',
    headers: headers(authorization, operation.operation === 'Replace'),
    ...(operation.operation === 'Replace' ? { body: JSON.stringify({ value: operation.value }) } : {}),
    expectedStatus: 204,
    signal,
  })
}
