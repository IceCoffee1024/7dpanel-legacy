import type { InferOutput } from 'valibot'

import type { AuditEntry, AuditFilters, AuditSourceGap } from '../model/audit'
import * as v from 'valibot'

import { listAuditEntriesQuery } from '../../../shared/api/generated/@pinia/colada.gen'
import { auditSourceKinds } from '../model/audit'

export interface AuditPage {
  readonly entries: readonly AuditEntry[]
  readonly nextCursor: string | null
  readonly sourceGaps: readonly AuditSourceGap[]
}

export type LoadAuditEntries = (
  authorizationHeader: string,
  filters: AuditFilters,
  cursor: string | null,
  limit: number,
  signal?: AbortSignal,
) => Promise<AuditPage>

const nonBlankString = v.pipe(v.string(), v.check(value => value.trim() !== ''))
const nullableNonBlankString = v.nullable(nonBlankString)
const utcTimestamp = v.pipe(v.string(), v.check((value) => {
  if (!/(?:Z|\+00:00)$/.test(value) || !Number.isFinite(Date.parse(value)))
    return false
  return /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|\+00:00)$/.test(value)
}))
const sourceKind = v.picklist(auditSourceKinds)
const positiveSafeInteger = v.pipe(v.number(), v.integer(), v.minValue(1), v.maxValue(Number.MAX_SAFE_INTEGER))
const auditEntrySchema = v.strictObject({
  sourceKind,
  sourceId: nonBlankString,
  actorSubject: nullableNonBlankString,
  targetRef: nullableNonBlankString,
  action: nonBlankString,
  occurredAtUtc: utcTimestamp,
  status: nonBlankString,
  correlationId: nullableNonBlankString,
  hasDetails: v.boolean(),
})
const auditGapSchema = v.strictObject({
  sourceKind,
  startedAtUtc: utcTimestamp,
  endedAtUtc: v.nullable(utcTimestamp),
  affectedCount: positiveSafeInteger,
  reason: nonBlankString,
})
const auditPageSchema = v.strictObject({
  entries: v.array(auditEntrySchema),
  nextCursor: nullableNonBlankString,
  sourceGaps: v.array(auditGapSchema),
})

type ParsedAuditPage = InferOutput<typeof auditPageSchema>

export function parseAuditPage(value: unknown): AuditPage {
  let parsed: ParsedAuditPage
  try {
    parsed = v.parse(auditPageSchema, value)
  }
  catch {
    throw new Error('Invalid audit page response')
  }
  return Object.freeze({
    entries: Object.freeze(parsed.entries.map(entry => Object.freeze(entry))),
    nextCursor: parsed.nextCursor,
    sourceGaps: Object.freeze(parsed.sourceGaps.map(gap => Object.freeze(gap))),
  })
}

export const loadAuditEntries: LoadAuditEntries = async (
  authorizationHeader,
  filters,
  cursor,
  limit,
  signal,
) => {
  const query = {
    ...(filters.fromUtc === '' ? {} : { fromUtc: filters.fromUtc }),
    ...(filters.toUtc === '' ? {} : { toUtc: filters.toUtc }),
    ...(filters.actor === '' ? {} : { actor: filters.actor }),
    ...(filters.target === '' ? {} : { target: filters.target }),
    ...(filters.action === '' ? {} : { action: filters.action }),
    ...(filters.sourceKind === '' ? {} : { sourceKind: filters.sourceKind }),
    ...(filters.status === '' ? {} : { status: filters.status }),
    ...(cursor === null ? {} : { cursor }),
    limit: String(limit),
  }
  const definition = listAuditEntriesQuery({
    headers: { Authorization: authorizationHeader },
    query,
  })
  return parseAuditPage(await definition.query({
    signal,
  } as Parameters<typeof definition.query>[0]))
}
