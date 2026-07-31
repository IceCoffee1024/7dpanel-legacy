import type { DeepReadonly, ShallowRef } from 'vue'

import { onMounted, onUnmounted, readonly, shallowRef } from 'vue'

import {
  createPanelDatabaseBackup,
  createServerConfigurationBackup,
  createWorldBackup,
  deleteBackup,
  downloadBackup,
  getJob,
  listBackups,
  restoreBackup,
} from '../../../shared/api/generated'
import { HttpError } from '../../../shared/api/http'
import { useAuthStore } from '../../auth'

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

export interface BackupsController {
  state: DeepReadonly<ShallowRef<BackupsViewState>>
  backups: DeepReadonly<ShallowRef<readonly BackupRecord[]>>
  activeJob: DeepReadonly<ShallowRef<JobRecord | null>>
  isMutating: DeepReadonly<ShallowRef<boolean>>
  errorCode: DeepReadonly<ShallowRef<string | null>>
  create: (kind: BackupKind, worldName: string) => Promise<boolean>
  download: (backup: BackupRecord) => Promise<boolean>
  remove: (backup: BackupRecord) => Promise<boolean>
  restore: (backup: BackupRecord, restartAfterStage: boolean) => Promise<boolean>
  refresh: () => Promise<void>
  dispose: () => void
}

const backupKinds = new Set<BackupKind>(['World', 'PanelDatabase', 'ServerConfiguration'])
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
const terminalStatuses = new Set<JobStatus>(['Succeeded', 'Failed', 'Cancelled', 'Interrupted', 'ResultUnknown'])

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

function parseBackupPage(value: unknown): readonly BackupRecord[] {
  if (!isRecord(value) || !Array.isArray(value.items))
    throw new Error('Invalid server protocol')
  return Object.freeze(value.items.map(parseBackup))
}

function parseJob(value: unknown): JobRecord {
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

function idempotencyKey(): string {
  return crypto.randomUUID()
}

function waitForPoll(signal: AbortSignal): Promise<void> {
  return new Promise((resolve) => {
    const timeout = window.setTimeout(resolve, 1_000)
    signal.addEventListener('abort', () => {
      window.clearTimeout(timeout)
      resolve()
    }, { once: true })
  })
}

export function useBackups(options: { onSessionExpired?: () => void } = {}): BackupsController {
  const auth = useAuthStore()
  const state = shallowRef<BackupsViewState>('loading')
  const backups = shallowRef<readonly BackupRecord[]>(Object.freeze([]))
  const activeJob = shallowRef<JobRecord | null>(null)
  const isMutating = shallowRef(false)
  const errorCode = shallowRef<string | null>(null)
  let listController: AbortController | null = null
  let mutationController: AbortController | null = null
  let pollController: AbortController | null = null
  let requestVersion = 0
  let disposed = false

  function handleFailure(error: unknown) {
    if (disposed || (error instanceof HttpError && error.code === 'aborted'))
      return
    if (error instanceof HttpError && error.status === 401) {
      auth.expireSession()
      options.onSessionExpired?.()
    }
    if (error instanceof HttpError && error.status === 403) {
      backups.value = Object.freeze([])
      state.value = 'forbidden'
      return
    }
    errorCode.value = error instanceof HttpError ? (error.problemCode ?? error.code) : 'protocol_error'
    state.value = errorCode.value === 'protocol_error'
      ? 'protocol-error'
      : backups.value.length === 0 ? 'failed' : 'stale'
  }

  async function refresh() {
    if (disposed)
      return
    listController?.abort()
    const version = ++requestVersion
    const controller = new AbortController()
    listController = controller
    if (backups.value.length === 0)
      state.value = 'loading'
    try {
      const nextBackups = parseBackupPage(await listBackups({ signal: controller.signal }))
      if (disposed || version !== requestVersion)
        return
      backups.value = nextBackups
      errorCode.value = null
      state.value = 'ready'
    }
    catch (error) {
      if (version === requestVersion)
        handleFailure(error)
    }
    finally {
      if (version === requestVersion)
        listController = null
    }
  }

  async function pollJob(jobId: string) {
    pollController?.abort()
    const controller = new AbortController()
    pollController = controller
    try {
      while (true) {
        if (disposed || controller.signal.aborted)
          return

        const job = parseJob(await getJob({ path: { jobId }, signal: controller.signal }))
        activeJob.value = job
        if (terminalStatuses.has(job.status)) {
          if (job.status === 'Succeeded' && backupKinds.has(job.kind as BackupKind))
            await refresh()
          if (job.status !== 'Succeeded')
            errorCode.value = job.errorCode ?? job.status
          return
        }
        await waitForPoll(controller.signal)
      }
    }
    catch (error) {
      handleFailure(error)
    }
    finally {
      if (pollController === controller)
        pollController = null
    }
  }

  async function mutate(operation: (signal: AbortSignal) => Promise<unknown>): Promise<JobRecord | null> {
    if (disposed || isMutating.value)
      return null
    isMutating.value = true
    errorCode.value = null
    const controller = new AbortController()
    mutationController = controller
    try {
      return parseJob(await operation(controller.signal))
    }
    catch (error) {
      handleFailure(error)
      return null
    }
    finally {
      if (mutationController === controller)
        mutationController = null
      isMutating.value = false
    }
  }

  async function create(kind: BackupKind, worldName: string) {
    const key = idempotencyKey()
    const operation = kind === 'World'
      ? (signal: AbortSignal) => createWorldBackup({
          body: { worldName: worldName.trim(), idempotencyKey: key },
          signal,
        })
      : kind === 'PanelDatabase'
        ? (signal: AbortSignal) => createPanelDatabaseBackup({ body: { idempotencyKey: key }, signal })
        : (signal: AbortSignal) => createServerConfigurationBackup({ body: { idempotencyKey: key }, signal })
    const job = await mutate(operation)
    if (job === null)
      return false
    activeJob.value = job
    void pollJob(job.id)
    return true
  }

  async function download(backup: BackupRecord) {
    if (disposed || isMutating.value)
      return false
    isMutating.value = true
    const controller = new AbortController()
    mutationController = controller
    try {
      const blob = await downloadBackup({ path: { backupId: backup.id }, signal: controller.signal })
      if (!(blob instanceof Blob))
        throw new Error('Invalid server protocol')
      const url = URL.createObjectURL(blob)
      const anchor = document.createElement('a')
      anchor.href = url
      anchor.download = `${backup.kind}-${backup.id}.zip`
      document.body.append(anchor)
      anchor.click()
      anchor.remove()
      URL.revokeObjectURL(url)
      return true
    }
    catch (error) {
      handleFailure(error)
      return false
    }
    finally {
      if (mutationController === controller)
        mutationController = null
      isMutating.value = false
    }
  }

  async function remove(backup: BackupRecord) {
    if (disposed || isMutating.value)
      return false
    isMutating.value = true
    const controller = new AbortController()
    mutationController = controller
    try {
      await deleteBackup({ path: { backupId: backup.id }, signal: controller.signal })
      await refresh()
      return true
    }
    catch (error) {
      handleFailure(error)
      return false
    }
    finally {
      if (mutationController === controller)
        mutationController = null
      isMutating.value = false
    }
  }

  async function restore(backup: BackupRecord, restartAfterStage: boolean) {
    const job = await mutate(signal => restoreBackup({
      path: { backupId: backup.id },
      body: { idempotencyKey: idempotencyKey(), restartAfterStage, strongConfirmed: true },
      signal,
    }))
    if (job === null)
      return false
    activeJob.value = job
    void pollJob(job.id)
    return true
  }

  function dispose() {
    if (disposed)
      return
    disposed = true
    requestVersion++
    listController?.abort()
    mutationController?.abort()
    pollController?.abort()
    listController = null
    mutationController = null
    pollController = null
  }

  onMounted(() => void refresh())
  onUnmounted(dispose)

  return {
    state: readonly(state),
    backups: readonly(backups),
    activeJob: readonly(activeJob),
    isMutating: readonly(isMutating),
    errorCode: readonly(errorCode),
    create,
    download,
    remove,
    restore,
    refresh,
    dispose,
  }
}
