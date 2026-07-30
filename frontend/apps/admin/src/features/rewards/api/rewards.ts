import { requestJson } from '../../../shared/api/http'

export type RewardEntryKind = 'Item' | 'Currency' | 'RegisteredAction'
export type GameResourceKind = 'Item' | 'Block'
export type GrantOperationState = 'Reserved' | 'Dispatching' | 'PendingReconciliation' | 'Completed' | 'Failed' | 'Refunded' | 'Compensated'

export interface RewardPackageEntry {
  readonly entryId: string
  readonly ordinal: number
  readonly kind: RewardEntryKind
  readonly itemInternalName: string | null
  readonly itemKind: GameResourceKind | null
  readonly quantity: number | null
  readonly minQuality: number | null
  readonly maxQuality: number | null
  readonly catalogVersion: string | null
  readonly currencyAmount: bigint | null
  readonly registeredAction: string | null
}

export interface RewardPackage {
  readonly packageId: string
  readonly name: string
  readonly description: string
  readonly enabled: boolean
  readonly sortOrder: number
  readonly createdAtUtc: string
  readonly updatedAtUtc: string
  readonly rowVersion: bigint
  readonly entries: readonly RewardPackageEntry[]
}

export interface RewardPackageEntryDraft {
  readonly entryId: string
  readonly kind: RewardEntryKind
  readonly itemInternalName?: string
  readonly itemKind?: GameResourceKind
  readonly quantity?: number
  readonly minQuality?: number | null
  readonly maxQuality?: number | null
  readonly catalogVersion?: string
  readonly currencyAmount?: bigint
  readonly registeredAction?: string
}

export interface RewardPackageDraft {
  readonly packageId: string
  readonly name: string
  readonly description: string
  readonly enabled: boolean
  readonly sortOrder: number
  readonly entries: readonly RewardPackageEntryDraft[]
}

export interface GrantOperationEntry {
  readonly operationEntryId: string
  readonly packageEntryId: string
  readonly ordinal: number
  readonly kind: RewardEntryKind
  readonly state: GrantOperationState
  readonly deliveryOperationId: string | null
  readonly ledgerTransactionId: string | null
  readonly errorCode: string | null
  readonly updatedAtUtc: string
  readonly rowVersion: bigint
}

export interface GrantOperation {
  readonly operationId: string
  readonly packageId: string
  readonly crossplatformId: string
  readonly expectedEntityId: number
  readonly expectedWorldId: string
  readonly state: GrantOperationState
  readonly sourceKind: string | null
  readonly sourceId: string | null
  readonly actorKind: string
  readonly actorId: string
  readonly reservationId: string | null
  readonly compensatesOperationId: string | null
  readonly correlationId: string | null
  readonly errorCode: string | null
  readonly createdAtUtc: string
  readonly updatedAtUtc: string
  readonly completedAtUtc: string | null
  readonly reconciledAtUtc: string | null
  readonly reconciledBy: string | null
  readonly rowVersion: bigint
  readonly reused: boolean | null
  readonly entries: readonly GrantOperationEntry[]
}

export interface GrantRewardInput {
  readonly packageId: string
  readonly crossplatformId: string
  readonly expectedEntityId: number
  readonly expectedWorldId: string
  readonly clientRequestKey: string
}

const entryKeys = ['entryId', 'ordinal', 'kind', 'itemInternalName', 'itemKind', 'quantity', 'minQuality', 'maxQuality', 'catalogVersion', 'currencyAmount', 'registeredAction'] as const
const packageKeys = ['packageId', 'name', 'description', 'enabled', 'sortOrder', 'createdAtUtc', 'updatedAtUtc', 'rowVersion', 'entries'] as const
const grantEntryKeys = ['operationEntryId', 'packageEntryId', 'ordinal', 'kind', 'state', 'deliveryOperationId', 'ledgerTransactionId', 'errorCode', 'updatedAtUtc', 'rowVersion'] as const
const grantKeys = ['operationId', 'packageId', 'crossplatformId', 'expectedEntityId', 'expectedWorldId', 'state', 'sourceKind', 'sourceId', 'actorKind', 'actorId', 'reservationId', 'compensatesOperationId', 'correlationId', 'errorCode', 'createdAtUtc', 'updatedAtUtc', 'completedAtUtc', 'reconciledAtUtc', 'reconciledBy', 'rowVersion', 'reused', 'entries'] as const

function record(value: unknown, keys: readonly string[]): Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value))
    throw new Error('Invalid rewards response')
  const source = value as Record<string, unknown>
  const actual = Object.keys(source).sort()
  const expected = [...keys].sort()
  if (actual.length !== expected.length || actual.some((key, index) => key !== expected[index]))
    throw new Error('Invalid rewards response')
  return source
}

function text(value: unknown, nullable = false): string | null {
  if (value === null && nullable)
    return null
  if (typeof value !== 'string' || (value.trim() === '' && !nullable))
    throw new Error('Invalid rewards response')
  return value
}

function bool(value: unknown, nullable = false): boolean | null {
  if (value === null && nullable)
    return null
  if (typeof value !== 'boolean')
    throw new Error('Invalid rewards response')
  return value
}

function number(value: unknown, nullable = false): number | null {
  if (value === null && nullable)
    return null
  if (typeof value !== 'number' || !Number.isSafeInteger(value))
    throw new Error('Invalid rewards response')
  return value
}

function big(value: unknown, nullable = false): bigint | null {
  if (value === null && nullable)
    return null
  if (typeof value === 'number' && Number.isSafeInteger(value))
    return BigInt(value)
  if (typeof value === 'string' && /^-?\d+$/.test(value))
    return BigInt(value)
  throw new Error('Invalid rewards response')
}

function enumValue<T extends string>(value: unknown, values: readonly T[], nullable = false): T | null {
  if (value === null && nullable)
    return null
  if (typeof value === 'number' && Number.isInteger(value) && value >= 0 && value < values.length)
    return values[value]!
  if (typeof value === 'string' && values.includes(value as T))
    return value as T
  throw new Error('Invalid rewards response')
}

function utc(value: unknown, nullable = false): string | null {
  const candidate = text(value, nullable)
  if (candidate === null)
    return null
  if (!Number.isFinite(Date.parse(candidate)) || !/Z$|[+-]00:00$/.test(candidate))
    throw new Error('Invalid rewards response')
  return candidate
}

function wire(value: bigint): number | string {
  const candidate = Number(value)
  return Number.isSafeInteger(candidate) ? candidate : value.toString()
}

export function parseRewardPackage(value: unknown): RewardPackage {
  const source = record(value, packageKeys)
  if (!Array.isArray(source.entries))
    throw new Error('Invalid rewards response')
  return Object.freeze({
    packageId: text(source.packageId)!,
    name: text(source.name)!,
    description: text(source.description, true)!,
    enabled: bool(source.enabled)!,
    sortOrder: number(source.sortOrder)!,
    createdAtUtc: utc(source.createdAtUtc)!,
    updatedAtUtc: utc(source.updatedAtUtc)!,
    rowVersion: big(source.rowVersion)!,
    entries: Object.freeze(source.entries.map((value): RewardPackageEntry => {
      const entry = record(value, entryKeys)
      return Object.freeze({
        entryId: text(entry.entryId)!,
        ordinal: number(entry.ordinal)!,
        kind: enumValue(entry.kind, ['Item', 'Currency', 'RegisteredAction'])!,
        itemInternalName: text(entry.itemInternalName, true),
        itemKind: enumValue(entry.itemKind, ['Item', 'Block'], true),
        quantity: number(entry.quantity, true),
        minQuality: number(entry.minQuality, true),
        maxQuality: number(entry.maxQuality, true),
        catalogVersion: text(entry.catalogVersion, true),
        currencyAmount: big(entry.currencyAmount, true),
        registeredAction: text(entry.registeredAction, true),
      })
    })),
  })
}

export function parseGrantOperation(value: unknown): GrantOperation {
  const source = record(value, grantKeys)
  if (!Array.isArray(source.entries))
    throw new Error('Invalid rewards response')
  return Object.freeze({
    operationId: text(source.operationId)!,
    packageId: text(source.packageId)!,
    crossplatformId: text(source.crossplatformId)!,
    expectedEntityId: number(source.expectedEntityId)!,
    expectedWorldId: text(source.expectedWorldId)!,
    state: enumValue(source.state, ['Reserved', 'Dispatching', 'PendingReconciliation', 'Completed', 'Failed', 'Refunded', 'Compensated'])!,
    sourceKind: text(source.sourceKind, true),
    sourceId: text(source.sourceId, true),
    actorKind: text(source.actorKind)!,
    actorId: text(source.actorId)!,
    reservationId: text(source.reservationId, true),
    compensatesOperationId: text(source.compensatesOperationId, true),
    correlationId: text(source.correlationId, true),
    errorCode: text(source.errorCode, true),
    createdAtUtc: utc(source.createdAtUtc)!,
    updatedAtUtc: utc(source.updatedAtUtc)!,
    completedAtUtc: utc(source.completedAtUtc, true),
    reconciledAtUtc: utc(source.reconciledAtUtc, true),
    reconciledBy: text(source.reconciledBy, true),
    rowVersion: big(source.rowVersion)!,
    reused: bool(source.reused, true),
    entries: Object.freeze(source.entries.map((value): GrantOperationEntry => {
      const entry = record(value, grantEntryKeys)
      return Object.freeze({
        operationEntryId: text(entry.operationEntryId)!,
        packageEntryId: text(entry.packageEntryId)!,
        ordinal: number(entry.ordinal)!,
        kind: enumValue(entry.kind, ['Item', 'Currency', 'RegisteredAction'])!,
        state: enumValue(entry.state, ['Reserved', 'Dispatching', 'PendingReconciliation', 'Completed', 'Failed', 'Refunded', 'Compensated'])!,
        deliveryOperationId: text(entry.deliveryOperationId, true),
        ledgerTransactionId: text(entry.ledgerTransactionId, true),
        errorCode: text(entry.errorCode, true),
        updatedAtUtc: utc(entry.updatedAtUtc)!,
        rowVersion: big(entry.rowVersion)!,
      })
    })),
  })
}

function headers(authorization: string) {
  return { 'Authorization': authorization, 'Content-Type': 'application/json' }
}

function packageEntryBody(entry: RewardPackageEntryDraft) {
  return {
    entryId: entry.entryId,
    kind: ['Item', 'Currency', 'RegisteredAction'].indexOf(entry.kind),
    itemInternalName: entry.itemInternalName ?? null,
    itemKind: entry.itemKind === undefined ? null : ['Item', 'Block'].indexOf(entry.itemKind),
    quantity: entry.quantity ?? null,
    minQuality: entry.minQuality ?? null,
    maxQuality: entry.maxQuality ?? null,
    catalogVersion: entry.catalogVersion ?? null,
    currencyAmount: entry.currencyAmount === undefined ? null : wire(entry.currencyAmount),
    registeredAction: entry.registeredAction ?? null,
  }
}

export async function fetchRewardPackage(authorization: string, packageId: string, signal?: AbortSignal): Promise<RewardPackage> {
  return parseRewardPackage(await requestJson<unknown>(`/api/v1/reward-packages/${encodeURIComponent(packageId)}`, { headers: { Authorization: authorization }, signal }))
}

export async function saveRewardPackage(authorization: string, draft: RewardPackageDraft, signal?: AbortSignal): Promise<RewardPackage> {
  const response = await requestJson<unknown>(`/api/v1/reward-packages/${encodeURIComponent(draft.packageId)}`, {
    method: 'PUT',
    headers: headers(authorization),
    signal,
    body: JSON.stringify({ name: draft.name, description: draft.description, enabled: draft.enabled, sortOrder: draft.sortOrder, entries: draft.entries.map(packageEntryBody) }),
  })
  return parseRewardPackage(response)
}

export async function fetchPendingGrantOperations(authorization: string, take = 50, signal?: AbortSignal): Promise<readonly GrantOperation[]> {
  const response = await requestJson<unknown>(`/api/v1/grant-operations?take=${encodeURIComponent(String(take))}`, { headers: { Authorization: authorization }, signal })
  const source = record(response, ['operations'])
  if (!Array.isArray(source.operations))
    throw new Error('Invalid rewards response')
  return Object.freeze(source.operations.map(parseGrantOperation))
}

export async function createGrantOperation(authorization: string, input: GrantRewardInput, signal?: AbortSignal): Promise<GrantOperation> {
  const response = await requestJson<unknown>('/api/v1/grant-operations', { method: 'POST', headers: headers(authorization), signal, body: JSON.stringify(input) })
  return parseGrantOperation(response)
}

async function mutateGrant(authorization: string, operationId: string, action: 'confirm' | 'refund' | 'compensate', clientRequestKey?: string, signal?: AbortSignal): Promise<GrantOperation> {
  const response = await requestJson<unknown>(`/api/v1/grant-operations/${encodeURIComponent(operationId)}/${action}`, {
    method: 'POST',
    headers: headers(authorization),
    signal,
    body: action === 'confirm' ? undefined : JSON.stringify({ clientRequestKey }),
  })
  return parseGrantOperation(response)
}

export const confirmGrantOperation = (authorization: string, operationId: string, signal?: AbortSignal) => mutateGrant(authorization, operationId, 'confirm', undefined, signal)
export const refundGrantOperation = (authorization: string, operationId: string, clientRequestKey: string, signal?: AbortSignal) => mutateGrant(authorization, operationId, 'refund', clientRequestKey, signal)
export const compensateGrantOperation = (authorization: string, operationId: string, clientRequestKey: string, signal?: AbortSignal) => mutateGrant(authorization, operationId, 'compensate', clientRequestKey, signal)
