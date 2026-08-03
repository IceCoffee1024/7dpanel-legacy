export type OperationStatusSemantic
  = | 'queued'
    | 'running'
    | 'succeeded'
    | 'failed'
    | 'cancelled'
    | 'interrupted'
    | 'result-unknown'
    | 'rollback-failed'
    | 'unavailable'
    | 'unknown'

export type OperationStatusTone = 'neutral' | 'info' | 'success' | 'warning' | 'error'

export interface OperationStatusProtocolError {
  readonly code: 'unknown_operation_status'
  readonly received: string
}

export interface OperationStatusPresentation {
  readonly semantic: OperationStatusSemantic
  readonly i18nKey: `operationStatus.${OperationStatusSemantic}`
  readonly tone: OperationStatusTone
  readonly terminal: boolean
  readonly safeToRetry: boolean
  readonly protocolError: OperationStatusProtocolError | null
}

interface KnownStatusPresentation extends Omit<OperationStatusPresentation, 'protocolError'> {}

const knownStatuses: Readonly<Record<string, KnownStatusPresentation>> = {
  queued: { semantic: 'queued', i18nKey: 'operationStatus.queued', tone: 'info', terminal: false, safeToRetry: false },
  pending: { semantic: 'queued', i18nKey: 'operationStatus.queued', tone: 'info', terminal: false, safeToRetry: false },
  pendingrestart: { semantic: 'queued', i18nKey: 'operationStatus.queued', tone: 'info', terminal: false, safeToRetry: false },
  accepted: { semantic: 'queued', i18nKey: 'operationStatus.queued', tone: 'info', terminal: false, safeToRetry: false },
  running: { semantic: 'running', i18nKey: 'operationStatus.running', tone: 'info', terminal: false, safeToRetry: false },
  submitting: { semantic: 'running', i18nKey: 'operationStatus.running', tone: 'info', terminal: false, safeToRetry: false },
  succeeded: { semantic: 'succeeded', i18nKey: 'operationStatus.succeeded', tone: 'success', terminal: true, safeToRetry: false },
  failed: { semantic: 'failed', i18nKey: 'operationStatus.failed', tone: 'error', terminal: true, safeToRetry: true },
  rejected: { semantic: 'failed', i18nKey: 'operationStatus.failed', tone: 'error', terminal: true, safeToRetry: true },
  cancelled: { semantic: 'cancelled', i18nKey: 'operationStatus.cancelled', tone: 'neutral', terminal: true, safeToRetry: true },
  skipped: { semantic: 'cancelled', i18nKey: 'operationStatus.cancelled', tone: 'neutral', terminal: true, safeToRetry: true },
  interrupted: { semantic: 'interrupted', i18nKey: 'operationStatus.interrupted', tone: 'warning', terminal: true, safeToRetry: false },
  resultunknown: { semantic: 'result-unknown', i18nKey: 'operationStatus.result-unknown', tone: 'error', terminal: true, safeToRetry: false },
  rollbackfailed: { semantic: 'rollback-failed', i18nKey: 'operationStatus.rollback-failed', tone: 'error', terminal: true, safeToRetry: false },
  unavailable: { semantic: 'unavailable', i18nKey: 'operationStatus.unavailable', tone: 'warning', terminal: false, safeToRetry: false },
}

function normalizedStatus(value: unknown): string | null {
  if (typeof value !== 'string')
    return null
  const normalized = value.trim().replace(/[\s_-]/g, '').toLowerCase()
  return normalized === '' ? null : normalized
}

/** Projects a feature-owned operation state into stable, user-visible semantics. */
export function operationStatus(value: unknown): OperationStatusPresentation {
  const received = typeof value === 'string' ? value : String(value)
  const known = normalizedStatus(value)
  const presentation = known === null ? undefined : knownStatuses[known]
  if (presentation !== undefined)
    return { ...presentation, protocolError: null }

  return {
    semantic: 'unknown',
    i18nKey: 'operationStatus.unknown',
    tone: 'error',
    terminal: false,
    safeToRetry: false,
    protocolError: { code: 'unknown_operation_status', received },
  }
}
