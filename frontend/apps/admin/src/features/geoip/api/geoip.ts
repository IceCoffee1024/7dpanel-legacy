import { requestJson } from '../../../shared/api/http'

export type GeoIpProvider = 'LocalMmdb' | 'MaxMindWebService'
export type GeoIpFailureMode = 'FailOpen' | 'FailClosed'
export type GeoIpEffect = 'Allow' | 'Deny'
export type GeoIpDiagnosticSeverity = 'Information' | 'Warning' | 'Error'
export type GeoIpLookupStatus = 'Found' | 'Unknown' | 'Private' | 'Invalid' | 'Unavailable'
export type GeoIpSecretOperation = Readonly<{ operation: 'Keep' } | { operation: 'Replace', value: string } | { operation: 'Clear' }>
export interface GeoIpCredentialState { readonly isSet: boolean, readonly fingerprint: string | null, readonly updatedAtUtc: string | null }
export interface GeoIpCredentials { readonly accountId: GeoIpCredentialState, readonly licenseKey: GeoIpCredentialState }
export interface GeoIpCredentialsDraft { readonly accountId: GeoIpSecretOperation, readonly licenseKey: GeoIpSecretOperation }
export interface GeoIpNetworkRule { readonly ruleId: string, readonly networkCidr: string, readonly effect: GeoIpEffect, readonly ordinal: number }
export interface GeoIpCountryRule { readonly countryCode: string, readonly effect: GeoIpEffect }
export interface GeoIpProviderMetadata { readonly provider: GeoIpProvider, readonly isExternal: boolean, readonly dataVersion: string | null, readonly buildEpoch: string | null }
export interface GeoIpPolicy {
  readonly version: number
  readonly isEnabled: boolean
  readonly provider: GeoIpProvider
  readonly failureMode: GeoIpFailureMode
  readonly bypassAdmins: boolean
  readonly rejectionMessage: string
  readonly networkRules: readonly GeoIpNetworkRule[]
  readonly countryRules: readonly GeoIpCountryRule[]
  readonly cacheHealth: Readonly<{ queueDepth: number, rejectedRefreshCount: number, lastCompletedAtUtc: string | null, lastLookupStatus: GeoIpLookupStatus | null, severity: GeoIpDiagnosticSeverity, statusCode: string }>
  readonly providers: readonly GeoIpProviderMetadata[]
  readonly recentDecisions: readonly Readonly<{ occurredAtUtc: string, maskedIp: string, decision: string, reasonCode: string, lookupStatus: string }>[]
}
export interface GeoIpPolicyDraft extends Pick<GeoIpPolicy, 'isEnabled' | 'provider' | 'failureMode' | 'bypassAdmins' | 'rejectionMessage' | 'networkRules' | 'countryRules'> { readonly expectedVersion: number }
export interface GeoIpDiagnostics extends Pick<GeoIpPolicy, 'isEnabled' | 'failureMode' | 'provider' | 'providers'> { readonly severity: GeoIpDiagnosticSeverity, readonly statusCode: string, readonly queueDepth: number, readonly rejectedRefreshCount: number, readonly lastCompletedAtUtc: string | null, readonly lastLookupStatus: GeoIpLookupStatus | null }
export interface GeoIpTestResult { readonly accepted: boolean, readonly maskedIp: string, readonly state: string }

const providers = new Set<GeoIpProvider>(['LocalMmdb', 'MaxMindWebService'])
const modes = new Set<GeoIpFailureMode>(['FailOpen', 'FailClosed'])
const effects = new Set<GeoIpEffect>(['Allow', 'Deny'])
const severities = new Set<GeoIpDiagnosticSeverity>(['Information', 'Warning', 'Error'])
const lookupStatuses = new Set<GeoIpLookupStatus>(['Found', 'Unknown', 'Private', 'Invalid', 'Unavailable'])
function invalid(): never { throw new Error('Invalid server protocol') }
function record(value: unknown): Record<string, unknown> { if (typeof value !== 'object' || value === null || Array.isArray(value)) invalid(); return value as Record<string, unknown> }
function keys(value: Record<string, unknown>, allowed: readonly string[]) { if (Object.keys(value).some(key => !allowed.includes(key))) invalid() }
function text(value: unknown): string { return typeof value === 'string' ? value : invalid() }
function bool(value: unknown): boolean { return typeof value === 'boolean' ? value : invalid() }
function integer(value: unknown): number { return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0 ? value : invalid() }
function nullableText(value: unknown): string | null { return value === null ? null : text(value) }
function nullableUtc(value: unknown): string | null { if (value === null) return null; const result = text(value); return Number.isFinite(Date.parse(result)) ? result : invalid() }
function parseProvider(value: unknown): GeoIpProvider { return typeof value === 'string' && providers.has(value as GeoIpProvider) ? value as GeoIpProvider : invalid() }
function parseMode(value: unknown): GeoIpFailureMode { return typeof value === 'string' && modes.has(value as GeoIpFailureMode) ? value as GeoIpFailureMode : invalid() }
function parseEffect(value: unknown): GeoIpEffect { return typeof value === 'string' && effects.has(value as GeoIpEffect) ? value as GeoIpEffect : invalid() }
function parseSeverity(value: unknown): GeoIpDiagnosticSeverity { return typeof value === 'string' && severities.has(value as GeoIpDiagnosticSeverity) ? value as GeoIpDiagnosticSeverity : invalid() }
function parseLookupStatus(value: unknown): GeoIpLookupStatus | null { return value === null ? null : typeof value === 'string' && lookupStatuses.has(value as GeoIpLookupStatus) ? value as GeoIpLookupStatus : invalid() }
function parseCredential(value: unknown): GeoIpCredentialState {
  const source = record(value); keys(source, ['isSet', 'fingerprint', 'updatedAtUtc'])
  const isSet = bool(source.isSet)
  const fingerprint = nullableText(source.fingerprint)
  const updatedAtUtc = nullableUtc(source.updatedAtUtc)
  if (isSet !== (fingerprint !== null && updatedAtUtc !== null)) invalid()
  return Object.freeze({ isSet, fingerprint, updatedAtUtc })
}
function parseProviders(value: unknown): readonly GeoIpProviderMetadata[] {
  if (!Array.isArray(value)) invalid()
  return Object.freeze(value.map((entry) => { const item = record(entry); keys(item, ['provider', 'isExternal', 'dataVersion', 'buildEpoch']); return Object.freeze({ provider: parseProvider(item.provider), isExternal: bool(item.isExternal), dataVersion: nullableText(item.dataVersion), buildEpoch: nullableText(item.buildEpoch) }) }))
}

export function parseGeoIpPolicy(value: unknown): GeoIpPolicy {
  const source = record(value)
  keys(source, ['version', 'isEnabled', 'provider', 'failureMode', 'bypassAdmins', 'rejectionMessage', 'networkRules', 'countryRules', 'cacheHealth', 'providers', 'recentDecisions'])
  if (!Array.isArray(source.networkRules) || !Array.isArray(source.countryRules) || !Array.isArray(source.recentDecisions)) invalid()
  const cache = record(source.cacheHealth); keys(cache, ['queueDepth', 'rejectedRefreshCount', 'lastCompletedAtUtc', 'lastLookupStatus', 'severity', 'statusCode'])
  return Object.freeze({
    version: integer(source.version), isEnabled: bool(source.isEnabled), provider: parseProvider(source.provider), failureMode: parseMode(source.failureMode), bypassAdmins: bool(source.bypassAdmins), rejectionMessage: text(source.rejectionMessage),
    networkRules: Object.freeze(source.networkRules.map((entry) => { const item = record(entry); keys(item, ['ruleId', 'networkCidr', 'effect', 'ordinal']); return Object.freeze({ ruleId: text(item.ruleId), networkCidr: text(item.networkCidr), effect: parseEffect(item.effect), ordinal: integer(item.ordinal) }) })),
    countryRules: Object.freeze(source.countryRules.map((entry) => { const item = record(entry); keys(item, ['countryCode', 'effect']); return Object.freeze({ countryCode: text(item.countryCode), effect: parseEffect(item.effect) }) })),
    cacheHealth: Object.freeze({ queueDepth: integer(cache.queueDepth), rejectedRefreshCount: integer(cache.rejectedRefreshCount), lastCompletedAtUtc: nullableUtc(cache.lastCompletedAtUtc), lastLookupStatus: parseLookupStatus(cache.lastLookupStatus), severity: parseSeverity(cache.severity), statusCode: text(cache.statusCode) }),
    providers: parseProviders(source.providers),
    recentDecisions: Object.freeze(source.recentDecisions.map((entry) => { const item = record(entry); keys(item, ['occurredAtUtc', 'maskedIp', 'decision', 'reasonCode', 'lookupStatus']); return Object.freeze({ occurredAtUtc: nullableUtc(item.occurredAtUtc) ?? invalid(), maskedIp: text(item.maskedIp), decision: text(item.decision), reasonCode: text(item.reasonCode), lookupStatus: text(item.lookupStatus) }) })),
  })
}

export function parseGeoIpCredentials(value: unknown): GeoIpCredentials {
  const source = record(value); keys(source, ['accountId', 'licenseKey'])
  return Object.freeze({ accountId: parseCredential(source.accountId), licenseKey: parseCredential(source.licenseKey) })
}

function headers(auth: string, json = false): HeadersInit { return { Authorization: auth, ...(json ? { 'Content-Type': 'application/json' } : {}) } }
export async function getGeoIpPolicy(auth: string, signal?: AbortSignal): Promise<GeoIpPolicy> { return parseGeoIpPolicy(await requestJson<unknown>('/api/v1/access-policies/geoip', { headers: headers(auth), signal })) }
export async function saveGeoIpPolicy(auth: string, draft: GeoIpPolicyDraft, signal?: AbortSignal): Promise<void> { await requestJson('/api/v1/access-policies/geoip', { method: 'PUT', headers: headers(auth, true), body: JSON.stringify(draft), signal }) }
export async function testGeoIpPolicy(auth: string, ipAddress: string, signal?: AbortSignal): Promise<GeoIpTestResult> {
  const source = record(await requestJson<unknown>('/api/v1/access-policies/geoip/test', { method: 'POST', headers: headers(auth, true), body: JSON.stringify({ ipAddress }), expectedStatus: 202, signal })); keys(source, ['accepted', 'maskedIp', 'state'])
  return Object.freeze({ accepted: bool(source.accepted), maskedIp: text(source.maskedIp), state: text(source.state) })
}
export async function getGeoIpDiagnostics(auth: string, signal?: AbortSignal): Promise<GeoIpDiagnostics> {
  const source = record(await requestJson<unknown>('/api/v1/access-policies/geoip/diagnostics', { headers: headers(auth), signal })); keys(source, ['isEnabled', 'failureMode', 'provider', 'severity', 'statusCode', 'queueDepth', 'rejectedRefreshCount', 'lastCompletedAtUtc', 'lastLookupStatus', 'providers'])
  return Object.freeze({ isEnabled: bool(source.isEnabled), failureMode: parseMode(source.failureMode), provider: parseProvider(source.provider), severity: parseSeverity(source.severity), statusCode: text(source.statusCode), queueDepth: integer(source.queueDepth), rejectedRefreshCount: integer(source.rejectedRefreshCount), lastCompletedAtUtc: nullableUtc(source.lastCompletedAtUtc), lastLookupStatus: parseLookupStatus(source.lastLookupStatus), providers: parseProviders(source.providers) })
}

export async function updateGeoIpCredentials(auth: string, draft: GeoIpCredentialsDraft, signal?: AbortSignal): Promise<GeoIpCredentials> {
  return parseGeoIpCredentials(await requestJson<unknown>('/api/v1/access-policies/geoip/credentials', { method: 'PUT', headers: headers(auth, true), body: JSON.stringify(draft), signal }))
}

export function getGeoIpCredentials(auth: string, signal?: AbortSignal): Promise<GeoIpCredentials> {
  return updateGeoIpCredentials(auth, { accountId: { operation: 'Keep' }, licenseKey: { operation: 'Keep' } }, signal)
}
