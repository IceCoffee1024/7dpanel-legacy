import type { UndoWorldChangeSetPreflight } from './worldTools.types'

import { get, nullableBoolean, nullableText, record, text } from './worldTools.protocol'

const preflightKeys = ['sourceOperationId', 'changeSetId', 'worldId', 'worldVersion', 'afterHash', 'currentRegionHash', 'currentHashMatches', 'status'] as const

export function parseUndoWorldChangeSetPreflight(value: unknown): UndoWorldChangeSetPreflight {
  const source = record(value, preflightKeys, 'Invalid undo preflight response')
  return Object.freeze({
    sourceOperationId: text(source.sourceOperationId, 'Invalid undo preflight response'),
    changeSetId: text(source.changeSetId, 'Invalid undo preflight response'),
    worldId: text(source.worldId, 'Invalid undo preflight response'),
    worldVersion: text(source.worldVersion, 'Invalid undo preflight response'),
    afterHash: text(source.afterHash, 'Invalid undo preflight response'),
    currentRegionHash: nullableText(source.currentRegionHash),
    currentHashMatches: nullableBoolean(source.currentHashMatches),
    status: text(source.status, 'Invalid undo preflight response'),
  })
}

export function fetchUndoWorldChangeSetPreflight(
  authorizationHeader: string,
  operationId: string,
  signal?: AbortSignal,
) {
  return get(
    `/api/v1/world-operations/${encodeURIComponent(operationId)}/undo-preflight`,
    authorizationHeader,
    parseUndoWorldChangeSetPreflight,
    signal,
  )
}
