export const auditSourceKinds = [
  'playerAction',
  'consoleCommand',
  'serverOperation',
  'chatOperation',
  'chatMuteOperation',
] as const

export type AuditSourceKind = typeof auditSourceKinds[number]
export type EvidenceViewState = 'loading' | 'ready' | 'stale' | 'failed' | 'forbidden'

export interface AuditEntry {
  readonly sourceKind: AuditSourceKind
  readonly sourceId: string
  readonly actorSubject: string | null
  readonly targetRef: string | null
  readonly action: string
  readonly occurredAtUtc: string
  readonly status: string
  readonly correlationId: string | null
  readonly hasDetails: boolean
}

export interface AuditSourceGap {
  readonly sourceKind: AuditSourceKind
  readonly startedAtUtc: string
  readonly endedAtUtc: string | null
  readonly affectedCount: number
  readonly reason: string
}

export interface AuditFilters {
  readonly fromUtc: string
  readonly toUtc: string
  readonly actor: string
  readonly target: string
  readonly action: string
  readonly sourceKind: AuditSourceKind | ''
  readonly status: string
}

export function createEmptyAuditFilters(): AuditFilters {
  return Object.freeze({
    fromUtc: '',
    toUtc: '',
    actor: '',
    target: '',
    action: '',
    sourceKind: '',
    status: '',
  })
}

export function normalizeAuditFilters(filters: AuditFilters): AuditFilters {
  return Object.freeze({
    fromUtc: filters.fromUtc.trim(),
    toUtc: filters.toUtc.trim(),
    actor: filters.actor.trim(),
    target: filters.target.trim(),
    action: filters.action.trim(),
    sourceKind: filters.sourceKind,
    status: filters.status.trim(),
  })
}
