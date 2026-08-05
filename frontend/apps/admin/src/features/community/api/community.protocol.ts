import type { WorldPosition } from './community.types'

export function invalid(): never {
  throw new Error('Invalid community response')
}

export function record(value: unknown, keys: readonly string[]): Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value))
    return invalid()
  const source = value as Record<string, unknown>
  const actual = Object.keys(source).sort()
  const expected = [...keys].sort()
  if (actual.length !== expected.length || actual.some((key, index) => key !== expected[index]))
    return invalid()
  return source
}

export function text(value: unknown, allowEmpty = false): string {
  if (typeof value !== 'string' || (!allowEmpty && value.trim() === ''))
    return invalid()
  return value
}

export function nullableText(value: unknown): string | null {
  return value === null ? null : text(value)
}

export function nullableCode(value: unknown): string | null {
  if (value === null)
    return null
  const candidate = text(value)
  if (!/^[a-z][a-z0-9_]*$/.test(candidate))
    return invalid()
  return candidate
}

export function bool(value: unknown): boolean {
  if (typeof value !== 'boolean')
    return invalid()
  return value
}

export function finite(value: unknown): number {
  if (typeof value !== 'number' || !Number.isFinite(value))
    return invalid()
  return value
}

export function integer(value: unknown, minimum = Number.MIN_SAFE_INTEGER): number {
  if (typeof value !== 'number' || !Number.isSafeInteger(value) || value < minimum)
    return invalid()
  return value
}

export function long(value: unknown, minimum = 0n): bigint {
  let parsed: bigint
  if (typeof value === 'number' && Number.isSafeInteger(value))
    parsed = BigInt(value)
  else if (typeof value === 'string' && /^-?\d+$/.test(value))
    parsed = BigInt(value)
  else
    return invalid()
  if (parsed < minimum || parsed > 9_223_372_036_854_775_807n)
    return invalid()
  return parsed
}

export function enumValue<T extends string>(value: unknown, values: readonly T[]): T {
  if (typeof value !== 'string' || !values.includes(value as T))
    return invalid()
  return value as T
}

export function utc(value: unknown): string {
  if (typeof value !== 'string')
    return invalid()
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.(\d{1,7}))?(?:Z|\+00:00)$/.exec(value)
  if (match === null)
    return invalid()
  const [year, month, day, hour, minute, second] = match.slice(1, 7).map(Number)
  const milliseconds = Number((match[7] ?? '').padEnd(3, '0').slice(0, 3))
  const timestamp = Date.parse(value)
  const parsed = new Date(timestamp)
  if (!Number.isFinite(timestamp)
    || parsed.getUTCFullYear() !== year
    || parsed.getUTCMonth() + 1 !== month
    || parsed.getUTCDate() !== day
    || parsed.getUTCHours() !== hour
    || parsed.getUTCMinutes() !== minute
    || parsed.getUTCSeconds() !== second
    || parsed.getUTCMilliseconds() !== milliseconds) {
    return invalid()
  }
  return value
}

export function nullableUtc(value: unknown): string | null {
  return value === null ? null : utc(value)
}

export function ensureChronology(first: string, second: string): void {
  if (Date.parse(second) < Date.parse(first))
    invalid()
}

export function collection<T>(value: unknown, parser: (item: unknown) => T): readonly T[] {
  if (!Array.isArray(value))
    return invalid()
  return Object.freeze(value.map(parser))
}

export function wireInteger(value: bigint): number | string {
  const candidate = Number(value)
  return Number.isSafeInteger(candidate) ? candidate : value.toString()
}

export function headers(authorization: string, json = false): Record<string, string> {
  return json
    ? { 'Authorization': authorization, 'Content-Type': 'application/json' }
    : { Authorization: authorization }
}

export function queryPath(path: string, query: Record<string, string | boolean>): string {
  const parameters = new URLSearchParams()
  for (const [key, value] of Object.entries(query))
    parameters.set(key, String(value))
  return `${path}?${parameters.toString()}`
}

export function parseWorldPosition(value: unknown): WorldPosition {
  const source = record(value, ['worldId', 'x', 'y', 'z', 'yaw'])
  return Object.freeze({
    worldId: text(source.worldId),
    x: finite(source.x),
    y: finite(source.y),
    z: finite(source.z),
    yaw: finite(source.yaw),
  })
}
