import { requestJson } from '../../../shared/api/http'

export type EconomyAccountKind = 'Player' | 'System'
export type LedgerSide = 'Debit' | 'Credit'

export interface EconomyAccount {
  readonly accountId: string
  readonly kind: EconomyAccountKind
  readonly crossplatformId: string | null
  readonly enabled: boolean
  readonly isFrozen: boolean
  readonly postedBalance: bigint
  readonly reservedDebit: bigint
  readonly availableBalance: bigint
  readonly createdAtUtc: string
  readonly updatedAtUtc: string
  readonly rowVersion: bigint
}

export interface EconomyAccountsPage {
  readonly accounts: readonly EconomyAccount[]
  readonly nextCursor: string | null
}

export interface LedgerEntry {
  readonly entryId: string
  readonly accountId: string
  readonly side: LedgerSide
  readonly amount: bigint
  readonly balanceAfter: bigint
}

export interface LedgerTransaction {
  readonly transactionId: string
  readonly type: string
  readonly occurredAtUtc: string
  readonly actorKind: string
  readonly actorId: string
  readonly relatedCrossplatformId: string | null
  readonly businessKind: string | null
  readonly businessId: string | null
  readonly correlationId: string | null
  readonly reason: string | null
  readonly status: string
  readonly entries: readonly LedgerEntry[]
}

export interface EconomyTransactionsPage {
  readonly transactions: readonly LedgerTransaction[]
  readonly nextCursor: string | null
}

export interface AccountQuery {
  readonly limit?: number
  readonly includeSystem?: boolean
  readonly search?: string
  readonly enabled?: boolean
  readonly frozen?: boolean
  readonly cursor?: string
}

export interface TransactionQuery {
  readonly limit?: number
  readonly relatedCrossplatformId?: string
  readonly accountId?: string
  readonly type?: string
  readonly businessKind?: string
  readonly cursor?: string
}

export interface BalanceAdjustmentInput {
  readonly crossplatformId: string
  readonly playerSide: LedgerSide
  readonly amount: bigint
  readonly clientRequestKey: string
  readonly reason: string
}

const accountKeys = ['accountId', 'kind', 'crossplatformId', 'enabled', 'isFrozen', 'postedBalance', 'reservedDebit', 'availableBalance', 'createdAtUtc', 'updatedAtUtc', 'rowVersion'] as const
const accountsPageKeys = ['accounts', 'nextCursor'] as const
const entryKeys = ['entryId', 'accountId', 'side', 'amount', 'balanceAfter'] as const
const transactionKeys = ['transactionId', 'type', 'occurredAtUtc', 'actorKind', 'actorId', 'relatedCrossplatformId', 'businessKind', 'businessId', 'correlationId', 'reason', 'status', 'entries'] as const
const transactionsPageKeys = ['transactions', 'nextCursor'] as const

function record(value: unknown, keys: readonly string[]): Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value))
    throw new Error('Invalid economy response')
  const source = value as Record<string, unknown>
  const actual = Object.keys(source).sort()
  const expected = [...keys].sort()
  if (actual.length !== expected.length || actual.some((key, index) => key !== expected[index]))
    throw new Error('Invalid economy response')
  return source
}

function text(value: unknown): string {
  if (typeof value !== 'string' || value.trim() === '')
    throw new Error('Invalid economy response')
  return value
}

function nullableText(value: unknown): string | null {
  return value === null ? null : text(value)
}

function bool(value: unknown): boolean {
  if (typeof value !== 'boolean')
    throw new Error('Invalid economy response')
  return value
}

function integer(value: unknown): bigint {
  if (typeof value === 'string' && /^-?\d+$/.test(value))
    return BigInt(value)
  if (typeof value === 'number' && Number.isSafeInteger(value))
    return BigInt(value)
  throw new Error('Invalid economy response')
}

function enumValue<T extends string>(value: unknown, values: readonly T[]): T {
  if (typeof value === 'number' && Number.isInteger(value) && value >= 0 && value < values.length)
    return values[value]!
  if (typeof value === 'string' && values.includes(value as T))
    return value as T
  throw new Error('Invalid economy response')
}

function utc(value: unknown): string {
  const candidate = text(value)
  if (!/^\d{4}-\d{2}-\d{2}T/.test(candidate) || !Number.isFinite(Date.parse(candidate)))
    throw new Error('Invalid economy response')
  return candidate
}

function cursor(value: unknown): string | null {
  if (value === null)
    return null
  const candidate = text(value)
  if (!/^[\w-]+$/.test(candidate))
    throw new Error('Invalid economy response')
  return candidate
}

function wireInteger(value: bigint): number | string {
  const number = Number(value)
  return Number.isSafeInteger(number) ? number : value.toString()
}

export function parseEconomyAccount(value: unknown): EconomyAccount {
  const source = record(value, accountKeys)
  return Object.freeze({
    accountId: text(source.accountId),
    kind: enumValue(source.kind, ['Player', 'System']),
    crossplatformId: nullableText(source.crossplatformId),
    enabled: bool(source.enabled),
    isFrozen: bool(source.isFrozen),
    postedBalance: integer(source.postedBalance),
    reservedDebit: integer(source.reservedDebit),
    availableBalance: integer(source.availableBalance),
    createdAtUtc: utc(source.createdAtUtc),
    updatedAtUtc: utc(source.updatedAtUtc),
    rowVersion: integer(source.rowVersion),
  })
}

export function parseEconomyAccountsPage(value: unknown): EconomyAccountsPage {
  const source = record(value, accountsPageKeys)
  if (!Array.isArray(source.accounts))
    throw new Error('Invalid economy response')
  return Object.freeze({
    accounts: Object.freeze(source.accounts.map(parseEconomyAccount)),
    nextCursor: cursor(source.nextCursor),
  })
}

function parseLedgerEntry(value: unknown): LedgerEntry {
  const source = record(value, entryKeys)
  return Object.freeze({
    entryId: text(source.entryId),
    accountId: text(source.accountId),
    side: enumValue(source.side, ['Debit', 'Credit']),
    amount: integer(source.amount),
    balanceAfter: integer(source.balanceAfter),
  })
}

export function parseLedgerTransaction(value: unknown): LedgerTransaction {
  const source = record(value, transactionKeys)
  if (!Array.isArray(source.entries))
    throw new Error('Invalid economy response')
  return Object.freeze({
    transactionId: text(source.transactionId),
    type: text(source.type),
    occurredAtUtc: utc(source.occurredAtUtc),
    actorKind: text(source.actorKind),
    actorId: text(source.actorId),
    relatedCrossplatformId: nullableText(source.relatedCrossplatformId),
    businessKind: nullableText(source.businessKind),
    businessId: nullableText(source.businessId),
    correlationId: nullableText(source.correlationId),
    reason: nullableText(source.reason),
    status: text(source.status),
    entries: Object.freeze(source.entries.map(parseLedgerEntry)),
  })
}

export function parseEconomyTransactionsPage(value: unknown): EconomyTransactionsPage {
  const source = record(value, transactionsPageKeys)
  if (!Array.isArray(source.transactions))
    throw new Error('Invalid economy response')
  return Object.freeze({
    transactions: Object.freeze(source.transactions.map(parseLedgerTransaction)),
    nextCursor: cursor(source.nextCursor),
  })
}

function queryPath(path: string, query: Record<string, string | number | boolean | undefined>): string {
  const parameters = new URLSearchParams()
  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined && value !== '')
      parameters.set(key, String(value))
  }
  const suffix = parameters.toString()
  return suffix === '' ? path : `${path}?${suffix}`
}

export async function fetchEconomyAccounts(authorization: string, query: AccountQuery = {}, signal?: AbortSignal): Promise<EconomyAccountsPage> {
  const response = await requestJson<unknown>(queryPath('/api/v1/economy/accounts', { ...query }), {
    headers: { Authorization: authorization },
    signal,
  })
  return parseEconomyAccountsPage(response)
}

export async function fetchEconomyTransactions(authorization: string, query: TransactionQuery = {}, signal?: AbortSignal): Promise<EconomyTransactionsPage> {
  const response = await requestJson<unknown>(queryPath('/api/v1/economy/transactions', { ...query }), {
    headers: { Authorization: authorization },
    signal,
  })
  return parseEconomyTransactionsPage(response)
}

export async function setEconomyAccountFrozen(authorization: string, account: EconomyAccount, isFrozen: boolean, signal?: AbortSignal): Promise<EconomyAccount> {
  const response = await requestJson<unknown>(`/api/v1/economy/accounts/${encodeURIComponent(account.accountId)}/freeze`, {
    method: 'POST',
    headers: { 'Authorization': authorization, 'Content-Type': 'application/json' },
    body: JSON.stringify({ isFrozen, expectedRowVersion: wireInteger(account.rowVersion) }),
    signal,
  })
  return parseEconomyAccount(response)
}

export async function adjustEconomyBalance(authorization: string, input: BalanceAdjustmentInput, signal?: AbortSignal): Promise<LedgerTransaction> {
  const response = await requestJson<unknown>(`/api/v1/economy/accounts/${encodeURIComponent(input.crossplatformId)}/adjust`, {
    method: 'POST',
    headers: { 'Authorization': authorization, 'Content-Type': 'application/json' },
    body: JSON.stringify({
      playerSide: input.playerSide === 'Debit' ? 0 : 1,
      amount: wireInteger(input.amount),
      clientRequestKey: input.clientRequestKey,
      reason: input.reason,
    }),
    signal,
  })
  return parseLedgerTransaction(response)
}

export function formatEconomyAmount(value: bigint): string {
  return new Intl.NumberFormat(undefined, { maximumFractionDigits: 0 }).format(value)
}
