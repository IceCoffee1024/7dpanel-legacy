import { requestJson } from '../../../shared/api/http'

export type PurchaseState = 'Reserved' | 'Dispatching' | 'PendingReconciliation' | 'Completed' | 'Failed' | 'Refunded'
export type PurchaseRequestStatus = 'Reserved' | 'Completed' | 'PendingReconciliation' | 'Failed' | 'ProductDisabled' | 'AccountDisabled' | 'AccountFrozen' | 'InsufficientFunds' | 'OutOfStock' | 'PlayerLimitReached'
export type AchievementStatistic = 'Level' | 'ZombieKills' | 'PlayerKills' | 'Deaths'
export type EvidenceGapPolicy = 'Paused' | 'Incomplete'
export type RewardEligibilityState = 'Eligible' | 'GrantReserved' | 'Granted' | 'Paused' | 'Incomplete' | 'PendingReconciliation' | 'Failed'

export interface ShopProduct {
  readonly productId: string
  readonly name: string
  readonly description: string
  readonly enabled: boolean
  readonly priceAmount: bigint
  readonly stockRemaining: bigint | null
  readonly perPlayerLimit: number | null
  readonly rewardPackageId: string
  readonly sortOrder: number
  readonly createdAtUtc: string
  readonly updatedAtUtc: string
  readonly rowVersion: bigint
}

export interface ShopProductDraft {
  readonly productId: string
  readonly name: string
  readonly description: string
  readonly enabled: boolean
  readonly priceAmount: bigint
  readonly stockRemaining: bigint | null
  readonly perPlayerLimit: number | null
  readonly rewardPackageId: string
  readonly sortOrder: number
}

export interface ShopPurchase {
  readonly purchaseId: string
  readonly productId: string
  readonly rewardPackageId: string
  readonly crossplatformId: string
  readonly quantity: number
  readonly unitPrice: bigint
  readonly totalAmount: bigint
  readonly state: PurchaseState
  readonly reservationId: string | null
  readonly capturedTransactionId: string | null
  readonly grantOperationId: string | null
  readonly correlationId: string | null
  readonly errorCode: string | null
  readonly createdAtUtc: string
  readonly updatedAtUtc: string
  readonly completedAtUtc: string | null
  readonly rowVersion: bigint
}

export interface PurchaseProductInput {
  readonly productId: string
  readonly crossplatformId: string
  readonly expectedEntityId: number
  readonly expectedWorldId: string
  readonly quantity: number
  readonly clientRequestKey: string
}

export interface RedeemCodeDefinition {
  readonly codeId: string
  readonly maskedCode: string
  readonly rewardPackageId: string
  readonly enabled: boolean
  readonly validFromUtc: string | null
  readonly expiresAtUtc: string | null
  readonly maxRedemptions: number | null
  readonly perPlayerLimit: number | null
  readonly redemptionCount: number
  readonly createdAtUtc: string
  readonly updatedAtUtc: string
  readonly rowVersion: bigint
}

export interface CreateRedeemCodeInput {
  readonly rewardPackageId: string
  readonly enabled: boolean
  readonly validFromUtc: string | null
  readonly expiresAtUtc: string | null
  readonly maxRedemptions: number | null
  readonly perPlayerLimit: number | null
}

export interface GeneratedRedeemCode { readonly code: string, readonly definition: RedeemCodeDefinition }

export interface AchievementDefinition {
  readonly achievementId: string
  readonly name: string
  readonly description: string
  readonly statistic: AchievementStatistic
  readonly thresholdValue: bigint
  readonly rewardPackageId: string
  readonly enabled: boolean
  readonly sortOrder: number
  readonly createdAtUtc: string
  readonly updatedAtUtc: string
  readonly rowVersion: bigint
}

export interface AchievementDefinitionDraft {
  readonly achievementId: string
  readonly name: string
  readonly description: string
  readonly statistic: AchievementStatistic
  readonly thresholdValue: bigint
  readonly rewardPackageId: string
  readonly enabled: boolean
  readonly sortOrder: number
}

export interface AchievementRecord {
  readonly achievementId: string
  readonly crossplatformId: string
  readonly currentValue: bigint
  readonly eligibilityKey: string | null
  readonly grantOperationId: string | null
  readonly completedAtUtc: string | null
  readonly updatedAtUtc: string
  readonly rowVersion: bigint
}

export interface OnlineRewardRule {
  readonly ruleId: string
  readonly name: string
  readonly requiredOnlineSeconds: bigint
  readonly repeatIntervalSeconds: bigint | null
  readonly gapPolicy: EvidenceGapPolicy
  readonly rewardPackageId: string
  readonly enabled: boolean
  readonly sortOrder: number
  readonly createdAtUtc: string
  readonly updatedAtUtc: string
  readonly rowVersion: bigint
}

export interface OnlineRewardRuleDraft {
  readonly ruleId: string
  readonly name: string
  readonly requiredOnlineSeconds: bigint
  readonly repeatIntervalSeconds: bigint | null
  readonly gapPolicy: EvidenceGapPolicy
  readonly rewardPackageId: string
  readonly enabled: boolean
  readonly sortOrder: number
}

export interface OnlineRewardRecord {
  readonly eligibilityId: string
  readonly ruleKind: string
  readonly ruleId: string
  readonly rewardPackageId: string
  readonly crossplatformId: string
  readonly eligibilityKey: string
  readonly state: RewardEligibilityState
  readonly grantOperationId: string | null
  readonly correlationId: string | null
  readonly evidenceFromUtc: string | null
  readonly evidenceToUtc: string | null
  readonly createdAtUtc: string
  readonly updatedAtUtc: string
  readonly rowVersion: bigint
}

export interface ManualOnlineRewardInput {
  readonly ruleId: string
  readonly crossplatformId: string
  readonly expectedEntityId: number
  readonly expectedWorldId: string
  readonly clientRequestKey: string
}

function record(value: unknown, keys: readonly string[]): Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value))
    throw new Error('Invalid commerce response')
  const source = value as Record<string, unknown>
  const actual = Object.keys(source).sort()
  const expected = [...keys].sort()
  if (actual.length !== expected.length || actual.some((key, index) => key !== expected[index]))
    throw new Error('Invalid commerce response')
  return source
}
function text(value: unknown, nullable = false): string | null {
  if (value === null && nullable)
    return null
  if (typeof value !== 'string' || (value.trim() === '' && !nullable))
    throw new Error('Invalid commerce response')
  return value
}
function bool(value: unknown): boolean {
  if (typeof value !== 'boolean')
    throw new Error('Invalid commerce response')
  return value
}
function int(value: unknown, nullable = false): number | null {
  if (value === null && nullable)
    return null
  if (typeof value !== 'number' || !Number.isSafeInteger(value))
    throw new Error('Invalid commerce response')
  return value
}
function big(value: unknown, nullable = false): bigint | null {
  if (value === null && nullable)
    return null
  if (typeof value === 'number' && Number.isSafeInteger(value))
    return BigInt(value)
  if (typeof value === 'string' && /^-?\d+$/.test(value))
    return BigInt(value)
  throw new Error('Invalid commerce response')
}
function utc(value: unknown, nullable = false): string | null {
  const candidate = text(value, nullable)
  if (candidate === null)
    return null
  if (!Number.isFinite(Date.parse(candidate)) || !/Z$|[+-]00:00$/.test(candidate))
    throw new Error('Invalid commerce response')
  return candidate
}
function enumValue<T extends string>(value: unknown, values: readonly T[]): T {
  if (typeof value === 'number' && Number.isInteger(value) && value >= 0 && value < values.length)
    return values[value]!
  if (typeof value === 'string' && values.includes(value as T))
    return value as T
  throw new Error('Invalid commerce response')
}
function wire(value: bigint | null): number | string | null {
  if (value === null)
    return null
  const candidate = Number(value)
  return Number.isSafeInteger(candidate) ? candidate : value.toString()
}
function headers(authorization: string) {
  return { 'Authorization': authorization, 'Content-Type': 'application/json' }
}

const productKeys = ['productId', 'name', 'description', 'enabled', 'priceAmount', 'stockRemaining', 'perPlayerLimit', 'rewardPackageId', 'sortOrder', 'createdAtUtc', 'updatedAtUtc', 'rowVersion'] as const
const purchaseKeys = ['purchaseId', 'productId', 'rewardPackageId', 'crossplatformId', 'quantity', 'unitPrice', 'totalAmount', 'state', 'reservationId', 'capturedTransactionId', 'grantOperationId', 'correlationId', 'errorCode', 'createdAtUtc', 'updatedAtUtc', 'completedAtUtc', 'rowVersion'] as const
const codeKeys = ['codeId', 'maskedCode', 'rewardPackageId', 'enabled', 'validFromUtc', 'expiresAtUtc', 'maxRedemptions', 'perPlayerLimit', 'redemptionCount', 'createdAtUtc', 'updatedAtUtc', 'rowVersion'] as const
const achievementKeys = ['achievementId', 'name', 'description', 'statistic', 'thresholdValue', 'rewardPackageId', 'enabled', 'sortOrder', 'createdAtUtc', 'updatedAtUtc', 'rowVersion'] as const
const achievementRecordKeys = ['achievementId', 'crossplatformId', 'currentValue', 'eligibilityKey', 'grantOperationId', 'completedAtUtc', 'updatedAtUtc', 'rowVersion'] as const
const ruleKeys = ['ruleId', 'name', 'requiredOnlineSeconds', 'repeatIntervalSeconds', 'gapPolicy', 'rewardPackageId', 'enabled', 'sortOrder', 'createdAtUtc', 'updatedAtUtc', 'rowVersion'] as const
const onlineRecordKeys = ['eligibilityId', 'ruleKind', 'ruleId', 'rewardPackageId', 'crossplatformId', 'eligibilityKey', 'state', 'grantOperationId', 'correlationId', 'evidenceFromUtc', 'evidenceToUtc', 'createdAtUtc', 'updatedAtUtc', 'rowVersion'] as const

export function parseShopProduct(value: unknown): ShopProduct {
  const s = record(value, productKeys)
  return Object.freeze({ productId: text(s.productId)!, name: text(s.name)!, description: text(s.description, true)!, enabled: bool(s.enabled), priceAmount: big(s.priceAmount)!, stockRemaining: big(s.stockRemaining, true), perPlayerLimit: int(s.perPlayerLimit, true), rewardPackageId: text(s.rewardPackageId)!, sortOrder: int(s.sortOrder)!, createdAtUtc: utc(s.createdAtUtc)!, updatedAtUtc: utc(s.updatedAtUtc)!, rowVersion: big(s.rowVersion)! })
}
export function parseShopPurchase(value: unknown): ShopPurchase {
  const s = record(value, purchaseKeys)
  return Object.freeze({ purchaseId: text(s.purchaseId)!, productId: text(s.productId)!, rewardPackageId: text(s.rewardPackageId)!, crossplatformId: text(s.crossplatformId)!, quantity: int(s.quantity)!, unitPrice: big(s.unitPrice)!, totalAmount: big(s.totalAmount)!, state: enumValue(s.state, ['Reserved', 'Dispatching', 'PendingReconciliation', 'Completed', 'Failed', 'Refunded']), reservationId: text(s.reservationId, true), capturedTransactionId: text(s.capturedTransactionId, true), grantOperationId: text(s.grantOperationId, true), correlationId: text(s.correlationId, true), errorCode: text(s.errorCode, true), createdAtUtc: utc(s.createdAtUtc)!, updatedAtUtc: utc(s.updatedAtUtc)!, completedAtUtc: utc(s.completedAtUtc, true), rowVersion: big(s.rowVersion)! })
}
export function parseRedeemCode(value: unknown): RedeemCodeDefinition {
  const s = record(value, codeKeys)
  return Object.freeze({ codeId: text(s.codeId)!, maskedCode: text(s.maskedCode)!, rewardPackageId: text(s.rewardPackageId)!, enabled: bool(s.enabled), validFromUtc: utc(s.validFromUtc, true), expiresAtUtc: utc(s.expiresAtUtc, true), maxRedemptions: int(s.maxRedemptions, true), perPlayerLimit: int(s.perPlayerLimit, true), redemptionCount: int(s.redemptionCount)!, createdAtUtc: utc(s.createdAtUtc)!, updatedAtUtc: utc(s.updatedAtUtc)!, rowVersion: big(s.rowVersion)! })
}
export function parseAchievementDefinition(value: unknown): AchievementDefinition {
  const s = record(value, achievementKeys)
  return Object.freeze({ achievementId: text(s.achievementId)!, name: text(s.name)!, description: text(s.description, true)!, statistic: enumValue(s.statistic, ['Level', 'ZombieKills', 'PlayerKills', 'Deaths']), thresholdValue: big(s.thresholdValue)!, rewardPackageId: text(s.rewardPackageId)!, enabled: bool(s.enabled), sortOrder: int(s.sortOrder)!, createdAtUtc: utc(s.createdAtUtc)!, updatedAtUtc: utc(s.updatedAtUtc)!, rowVersion: big(s.rowVersion)! })
}
export function parseAchievementRecord(value: unknown): AchievementRecord {
  const s = record(value, achievementRecordKeys)
  return Object.freeze({ achievementId: text(s.achievementId)!, crossplatformId: text(s.crossplatformId)!, currentValue: big(s.currentValue)!, eligibilityKey: text(s.eligibilityKey, true), grantOperationId: text(s.grantOperationId, true), completedAtUtc: utc(s.completedAtUtc, true), updatedAtUtc: utc(s.updatedAtUtc)!, rowVersion: big(s.rowVersion)! })
}
export function parseOnlineRewardRule(value: unknown): OnlineRewardRule {
  const s = record(value, ruleKeys)
  return Object.freeze({ ruleId: text(s.ruleId)!, name: text(s.name)!, requiredOnlineSeconds: big(s.requiredOnlineSeconds)!, repeatIntervalSeconds: big(s.repeatIntervalSeconds, true), gapPolicy: enumValue(s.gapPolicy, ['Paused', 'Incomplete']), rewardPackageId: text(s.rewardPackageId)!, enabled: bool(s.enabled), sortOrder: int(s.sortOrder)!, createdAtUtc: utc(s.createdAtUtc)!, updatedAtUtc: utc(s.updatedAtUtc)!, rowVersion: big(s.rowVersion)! })
}
export function parseOnlineRewardRecord(value: unknown): OnlineRewardRecord {
  const s = record(value, onlineRecordKeys)
  return Object.freeze({ eligibilityId: text(s.eligibilityId)!, ruleKind: text(s.ruleKind)!, ruleId: text(s.ruleId)!, rewardPackageId: text(s.rewardPackageId)!, crossplatformId: text(s.crossplatformId)!, eligibilityKey: text(s.eligibilityKey)!, state: enumValue(s.state, ['Eligible', 'GrantReserved', 'Granted', 'Paused', 'Incomplete', 'PendingReconciliation', 'Failed']), grantOperationId: text(s.grantOperationId, true), correlationId: text(s.correlationId, true), evidenceFromUtc: utc(s.evidenceFromUtc, true), evidenceToUtc: utc(s.evidenceToUtc, true), createdAtUtc: utc(s.createdAtUtc)!, updatedAtUtc: utc(s.updatedAtUtc)!, rowVersion: big(s.rowVersion)! })
}

export async function fetchShopProduct(auth: string, id: string, signal?: AbortSignal) {
  return parseShopProduct(await requestJson<unknown>(`/api/v1/shop/products/${encodeURIComponent(id)}`, { headers: { Authorization: auth }, signal }))
}
export async function saveShopProduct(auth: string, draft: ShopProductDraft, signal?: AbortSignal) {
  return parseShopProduct(await requestJson<unknown>(`/api/v1/shop/products/${encodeURIComponent(draft.productId)}`, { method: 'PUT', headers: headers(auth), signal, body: JSON.stringify({ ...draft, priceAmount: wire(draft.priceAmount), stockRemaining: wire(draft.stockRemaining) }) }))
}
export async function purchaseShopProduct(auth: string, input: PurchaseProductInput, signal?: AbortSignal): Promise<{ status: PurchaseRequestStatus, purchase: ShopPurchase | null }> {
  const s = record(await requestJson<unknown>(`/api/v1/shop/products/${encodeURIComponent(input.productId)}/purchases`, { method: 'POST', headers: headers(auth), signal, body: JSON.stringify({ crossplatformId: input.crossplatformId, expectedEntityId: input.expectedEntityId, expectedWorldId: input.expectedWorldId, quantity: input.quantity, clientRequestKey: input.clientRequestKey }) }), ['status', 'purchase'])
  return Object.freeze({ status: enumValue(s.status, ['Reserved', 'Completed', 'PendingReconciliation', 'Failed', 'ProductDisabled', 'AccountDisabled', 'AccountFrozen', 'InsufficientFunds', 'OutOfStock', 'PlayerLimitReached']), purchase: s.purchase === null ? null : parseShopPurchase(s.purchase) })
}
export async function fetchRedeemCode(auth: string, id: string, signal?: AbortSignal) {
  return parseRedeemCode(await requestJson<unknown>(`/api/v1/redeem-codes/${encodeURIComponent(id)}`, { headers: { Authorization: auth }, signal }))
}
export async function createRedeemCode(auth: string, input: CreateRedeemCodeInput, signal?: AbortSignal): Promise<GeneratedRedeemCode> {
  const s = record(await requestJson<unknown>('/api/v1/redeem-codes', { method: 'POST', headers: headers(auth), signal, body: JSON.stringify(input), expectedStatus: 201 }), ['code', 'definition'])
  return Object.freeze({ code: text(s.code)!, definition: parseRedeemCode(s.definition) })
}
export async function saveAchievementDefinition(auth: string, draft: AchievementDefinitionDraft, signal?: AbortSignal) {
  return parseAchievementDefinition(await requestJson<unknown>(`/api/v1/achievements/definitions/${encodeURIComponent(draft.achievementId)}`, { method: 'PUT', headers: headers(auth), signal, body: JSON.stringify({ ...draft, statistic: ['Level', 'ZombieKills', 'PlayerKills', 'Deaths'].indexOf(draft.statistic), thresholdValue: wire(draft.thresholdValue) }) }))
}
export async function fetchAchievementRecord(auth: string, achievementId: string, playerId: string, signal?: AbortSignal) {
  return parseAchievementRecord(await requestJson<unknown>(`/api/v1/achievements/records/${encodeURIComponent(achievementId)}/${encodeURIComponent(playerId)}`, { headers: { Authorization: auth }, signal }))
}
export async function saveOnlineRewardRule(auth: string, draft: OnlineRewardRuleDraft, signal?: AbortSignal) {
  return parseOnlineRewardRule(await requestJson<unknown>(`/api/v1/online-rewards/rules/${encodeURIComponent(draft.ruleId)}`, { method: 'PUT', headers: headers(auth), signal, body: JSON.stringify({ ...draft, requiredOnlineSeconds: wire(draft.requiredOnlineSeconds), repeatIntervalSeconds: wire(draft.repeatIntervalSeconds), gapPolicy: ['Paused', 'Incomplete'].indexOf(draft.gapPolicy) }) }))
}
export async function fetchOnlineRewardRecords(auth: string, ruleId: string, playerId: string, signal?: AbortSignal): Promise<readonly OnlineRewardRecord[]> {
  const query = new URLSearchParams({ ruleId, crossplatformId: playerId })
  const s = record(await requestJson<unknown>(`/api/v1/online-rewards/records?${query}`, { headers: { Authorization: auth }, signal }), ['records'])
  if (!Array.isArray(s.records))
    throw new Error('Invalid commerce response')
  return Object.freeze(s.records.map(parseOnlineRewardRecord))
}
export async function grantManualOnlineReward(auth: string, input: ManualOnlineRewardInput, signal?: AbortSignal) {
  return parseOnlineRewardRecord(await requestJson<unknown>('/api/v1/online-rewards/records/manual', { method: 'POST', headers: headers(auth), signal, body: JSON.stringify(input) }))
}
export function formatCommerceAmount(value: bigint) {
  return new Intl.NumberFormat(undefined, { maximumFractionDigits: 0 }).format(value)
}
