import type { InferOutput } from 'valibot'

import * as v from 'valibot'

import { listGameEventsQuery } from '../../../shared/api/generated/@pinia/colada.gen'

export const gameEventTypes = ['PlayerJoined', 'PlayerLeft', 'PlayerKilledEntity', 'PlayerDied'] as const
export const gameEventGapReasons = ['QueueFull', 'StoreFailure', 'DrainTimeout'] as const

export type GameEventType = typeof gameEventTypes[number]
export type GameEventGapReason = typeof gameEventGapReasons[number]
export type GameEventViewState = 'loading' | 'ready' | 'stale' | 'failed' | 'forbidden'

export interface GameEventSubject {
  readonly crossplatformId: string | null
  readonly platformId: string | null
  readonly entityId: number | null
  readonly displayName: string | null
}

export interface GameEventRecord {
  readonly eventId: string
  readonly eventType: GameEventType
  readonly occurredAtUtc: string
  readonly observedAtUtc: string
  readonly actor: GameEventSubject | null
  readonly target: GameEventSubject | null
  readonly gameShuttingDown: boolean | null
}

export interface GameEventGap {
  readonly gapId: string
  readonly reason: GameEventGapReason
  readonly startedAtUtc: string
  readonly endedAtUtc: string | null
  readonly affectedCount: number
}

export interface GameEventPage {
  readonly events: readonly GameEventRecord[]
  readonly gaps: readonly GameEventGap[]
  readonly nextCursor: string | null
}

export interface GameEventFilters {
  readonly fromUtc: string
  readonly toUtc: string
  readonly eventType: GameEventType | ''
  readonly crossplatformId: string
}

export type LoadGameEvents = (
  authorizationHeader: string,
  filters: GameEventFilters,
  cursor: string | null,
  limit: number,
  signal?: AbortSignal,
) => Promise<GameEventPage>

export function createEmptyGameEventFilters(): GameEventFilters {
  return Object.freeze({ fromUtc: '', toUtc: '', eventType: '', crossplatformId: '' })
}

export function normalizeGameEventFilters(filters: GameEventFilters): GameEventFilters {
  return Object.freeze({
    fromUtc: filters.fromUtc.trim(),
    toUtc: filters.toUtc.trim(),
    eventType: filters.eventType,
    crossplatformId: filters.crossplatformId.trim(),
  })
}

const nonBlankString = v.pipe(v.string(), v.check(value => value.trim() !== ''))
const nullableNonBlankString = v.nullable(nonBlankString)
const utcTimestamp = v.pipe(v.string(), v.check(value =>
  /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|\+00:00)$/.test(value)
  && Number.isFinite(Date.parse(value)),
))
const canonicalGuid = v.pipe(nonBlankString, v.regex(/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i))
const nullableEntityId = v.nullable(v.pipe(v.number(), v.integer(), v.minValue(0), v.maxValue(Number.MAX_SAFE_INTEGER)))
const positiveSafeInteger = v.pipe(v.number(), v.integer(), v.minValue(1), v.maxValue(Number.MAX_SAFE_INTEGER))
const subjectSchema = v.strictObject({
  crossplatformId: nullableNonBlankString,
  platformId: nullableNonBlankString,
  entityId: nullableEntityId,
  displayName: nullableNonBlankString,
})
const eventSchema = v.strictObject({
  eventId: canonicalGuid,
  eventType: v.picklist(gameEventTypes),
  occurredAtUtc: utcTimestamp,
  observedAtUtc: utcTimestamp,
  actor: v.nullable(subjectSchema),
  target: v.nullable(subjectSchema),
  gameShuttingDown: v.nullable(v.boolean()),
})
const gapSchema = v.strictObject({
  gapId: canonicalGuid,
  reason: v.picklist(gameEventGapReasons),
  startedAtUtc: utcTimestamp,
  endedAtUtc: v.nullable(utcTimestamp),
  affectedCount: positiveSafeInteger,
})
const pageSchema = v.strictObject({
  events: v.array(eventSchema),
  gaps: v.array(gapSchema),
  nextCursor: nullableNonBlankString,
})

type ParsedGameEventPage = InferOutput<typeof pageSchema>

export function parseGameEventPage(value: unknown): GameEventPage {
  let parsed: ParsedGameEventPage
  try {
    parsed = v.parse(pageSchema, value)
  }
  catch {
    throw new Error('Invalid game event page response')
  }
  return Object.freeze({
    events: Object.freeze(parsed.events.map(event => Object.freeze({
      ...event,
      actor: event.actor === null ? null : Object.freeze(event.actor),
      target: event.target === null ? null : Object.freeze(event.target),
    }))),
    gaps: Object.freeze(parsed.gaps.map(gap => Object.freeze(gap))),
    nextCursor: parsed.nextCursor,
  })
}

export const loadGameEvents: LoadGameEvents = async (
  authorizationHeader,
  filters,
  cursor,
  limit,
  signal,
) => {
  const definition = listGameEventsQuery({
    headers: { Authorization: authorizationHeader },
    query: {
      ...(filters.fromUtc === '' ? {} : { fromUtc: filters.fromUtc }),
      ...(filters.toUtc === '' ? {} : { toUtc: filters.toUtc }),
      ...(filters.eventType === '' ? {} : { eventType: filters.eventType }),
      ...(filters.crossplatformId === '' ? {} : { crossplatformId: filters.crossplatformId }),
      ...(cursor === null ? {} : { cursor }),
      limit: String(limit),
    },
  })
  return parseGameEventPage(await definition.query({
    signal,
  } as Parameters<typeof definition.query>[0]))
}
