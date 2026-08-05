export type BackupKind = 'World' | 'PanelDatabase' | 'ServerConfiguration'
export type JobKind = BackupKind | 'Restore' | 'ScheduledConsoleCommand' | 'ScheduledRestart' | 'ScheduledAnnouncement'
export type JobStatus = 'Queued' | 'Running' | 'PendingRestart' | 'Succeeded' | 'Failed' | 'Cancelled' | 'Interrupted' | 'ResultUnknown'
export type BackupsViewState = 'loading' | 'ready' | 'stale' | 'failed' | 'forbidden' | 'protocol-error'

export interface BackupRecord {
  readonly id: string
  readonly kind: BackupKind
  readonly sizeBytes: number
  readonly sha256: string
  readonly worldId: string | null
  readonly gameVersion: string | null
  readonly validationStatus: string
  readonly createdAtUtc: string
  readonly sourceJobId: string
  readonly manifestVersion: number
}

export interface JobRecord {
  readonly id: string
  readonly kind: JobKind
  readonly status: JobStatus
  readonly createdAtUtc: string
  readonly startedAtUtc: string | null
  readonly completedAtUtc: string | null
  readonly progress: Readonly<{ current: number | null, total: number | null }> | null
  readonly errorCode: string | null
}
