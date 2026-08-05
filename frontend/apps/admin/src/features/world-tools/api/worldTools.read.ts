import type { WorldSummary } from './worldTools.types'

import { get, nullableInteger, nullableText, nullableUtc, parseExtent, record, sourceState } from './worldTools.protocol'

const worldSummaryKeys = ['sourceState', 'worldId', 'worldVersion', 'seed', 'width', 'height', 'gameVersion', 'mapResourceVersion', 'availableExtent', 'observedAtUtc'] as const

export function parseWorldSummary(value: unknown): WorldSummary {
  const source = record(value, worldSummaryKeys)
  return Object.freeze({
    sourceState: sourceState(source.sourceState),
    worldId: nullableText(source.worldId),
    worldVersion: nullableText(source.worldVersion),
    seed: nullableText(source.seed),
    width: nullableInteger(source.width),
    height: nullableInteger(source.height),
    gameVersion: nullableText(source.gameVersion),
    mapResourceVersion: nullableText(source.mapResourceVersion),
    availableExtent: source.availableExtent === null ? null : parseExtent(source.availableExtent),
    observedAtUtc: nullableUtc(source.observedAtUtc),
  })
}

export function fetchWorldSummary(authorizationHeader: string, signal?: AbortSignal) {
  return get('/api/v1/world/summary', authorizationHeader, parseWorldSummary, signal)
}
