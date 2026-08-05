import type { BackupKind, BackupRecord, JobKind, JobRecord, JobStatus } from './backups.types'

export const backupKinds = new Set<BackupKind>(['World', 'PanelDatabase', 'ServerConfiguration'])
const jobKinds = new Set<JobKind>([
  'World',
  'PanelDatabase',
  'ServerConfiguration',
  'Restore',
  'ScheduledConsoleCommand',
  'ScheduledRestart',
  'ScheduledAnnouncement',
])
const jobStatuses = new Set<JobStatus>([
  'Queued',
  'Running',
  'PendingRestart',
  'Succeeded',
  'Failed',
  'Cancelled',
  'Interrupted',
  'ResultUnknown',
])
export const terminalStatuses = new Set<JobStatus>(['Succeeded', 'Failed', 'Cancelled', 'Interrupted', 'ResultUnknown'])

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function isUtc(value: unknown): value is string {
  return typeof value === 'string'
    && /(?:Z|\+00:00)$/.test(value)
    && Number.isFinite(Date.parse(value))
}

function nullableString(value: unknown): string | null {
  if (value === null)
    return null
  if (typeof value !== 'string')
    throw new Error('Invalid server protocol')
  return value
}

function nullableUtc(value: unknown): string | null {
  if (value === null)
    return null
  if (!isUtc(value))
    throw new Error('Invalid server protocol')
  return value
}

function parseBackup(value: unknown): BackupRecord {
  if (!isRecord(value)
    || typeof value.id !== 'string'
    || typeof value.kind !== 'string'
    || !backupKinds.has(value.kind as BackupKind)
    || typeof value.sizeBytes !== 'number'
    || !Number.isSafeInteger(value.sizeBytes)
    || value.sizeBytes < 0
    || typeof value.sha256 !== 'string'
    || !/^[a-f\d]{64}$/i.test(value.sha256)
    || typeof value.validationStatus !== 'string'
    || value.validationStatus.trim() === ''
    || !isUtc(value.createdAtUtc)
    || typeof value.sourceJobId !== 'string'
    || typeof value.manifestVersion !== 'number'
    || !Number.isSafeInteger(value.manifestVersion)
    || value.manifestVersion < 1) {
    throw new Error('Invalid server protocol')
  }

  return Object.freeze({
    id: value.id,
    kind: value.kind as BackupKind,
    sizeBytes: value.sizeBytes,
    sha256: value.sha256,
    worldId: nullableString(value.worldId),
    gameVersion: nullableString(value.gameVersion),
    validationStatus: value.validationStatus,
    createdAtUtc: value.createdAtUtc,
    sourceJobId: value.sourceJobId,
    manifestVersion: value.manifestVersion,
  })
}

export function parseBackupPage(value: unknown): readonly BackupRecord[] {
  if (!isRecord(value) || !Array.isArray(value.items))
    throw new Error('Invalid server protocol')
  return Object.freeze(value.items.map(parseBackup))
}

export function parseJob(value: unknown): JobRecord {
  if (!isRecord(value)
    || typeof value.id !== 'string'
    || typeof value.kind !== 'string'
    || !jobKinds.has(value.kind as JobKind)
    || typeof value.status !== 'string'
    || !jobStatuses.has(value.status as JobStatus)
    || !isUtc(value.createdAtUtc)
    || (value.progress !== null && (!isRecord(value.progress)
      || (value.progress.current !== null && typeof value.progress.current !== 'number')
      || (value.progress.total !== null && typeof value.progress.total !== 'number')))) {
    throw new Error('Invalid server protocol')
  }

  return Object.freeze({
    id: value.id,
    kind: value.kind as JobKind,
    status: value.status as JobStatus,
    createdAtUtc: value.createdAtUtc,
    startedAtUtc: nullableUtc(value.startedAtUtc),
    completedAtUtc: nullableUtc(value.completedAtUtc),
    progress: value.progress === null
      ? null
      : Object.freeze({
          current: value.progress.current as number | null,
          total: value.progress.total as number | null,
        }),
    errorCode: nullableString(value.errorCode),
  })
}
