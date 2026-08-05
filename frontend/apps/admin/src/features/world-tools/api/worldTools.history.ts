import type { WorldOperationRecord } from './worldTools.types'

import { requestJson } from '../../../shared/api/http'

import { nullableInteger, nullableText, nullableUtc, parseOperationKind, parseOperationStatus, record, text, utc } from './worldTools.protocol'

const operationKeys = ['operationId', 'jobId', 'kind', 'worldId', 'worldVersion', 'mapResourceVersion', 'correlationId', 'confirmationSummary', 'isReversible', 'changeSetId', 'status', 'progress', 'errorCode', 'createdAtUtc', 'startedAtUtc', 'completedAtUtc'] as const
const progressKeys = ['current', 'total'] as const

export function parseWorldOperation(value: unknown): WorldOperationRecord {
  const source = record(value, operationKeys)
  let progress: WorldOperationRecord['progress'] = null
  if (source.progress !== null) {
    const progressSource = record(source.progress, progressKeys)
    progress = Object.freeze({
      current: nullableInteger(progressSource.current),
      total: nullableInteger(progressSource.total),
    })
  }
  if (typeof source.isReversible !== 'boolean')
    throw new Error('Invalid world operation response')
  return Object.freeze({
    operationId: text(source.operationId),
    jobId: text(source.jobId),
    kind: parseOperationKind(source.kind),
    worldId: text(source.worldId),
    worldVersion: text(source.worldVersion),
    mapResourceVersion: nullableText(source.mapResourceVersion),
    correlationId: text(source.correlationId),
    confirmationSummary: text(source.confirmationSummary),
    isReversible: source.isReversible,
    changeSetId: nullableText(source.changeSetId),
    status: parseOperationStatus(source.status),
    progress,
    errorCode: nullableText(source.errorCode),
    createdAtUtc: utc(source.createdAtUtc),
    startedAtUtc: nullableUtc(source.startedAtUtc),
    completedAtUtc: nullableUtc(source.completedAtUtc),
  })
}

export async function fetchWorldOperation(
  authorizationHeader: string,
  operationId: string,
  signal?: AbortSignal,
): Promise<WorldOperationRecord> {
  const response = await requestJson<unknown>(`/api/v1/world-operations/${encodeURIComponent(operationId)}`, {
    headers: { Authorization: authorizationHeader },
    signal,
  })
  return parseWorldOperation(response)
}
