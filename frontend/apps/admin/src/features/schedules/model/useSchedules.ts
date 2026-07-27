import type { DeepReadonly, ShallowRef } from 'vue'

import { onMounted, onUnmounted, readonly, shallowRef } from 'vue'

import {
  createSchedule,
  deleteSchedule,
  disableSchedule,
  enableSchedule,
  listSchedules,
  sendAnnouncement,
  updateSchedule,
} from '../../../shared/api/generated'
import { HttpError } from '../../../shared/api/http'
import { useAuthStore } from '../../auth'

export type ScheduleKind = 'ScheduledConsoleCommand' | 'ScheduledRestart' | 'ScheduledAnnouncement'
export type ScheduleConcurrencyPolicy = 'SkipIfRunning' | 'QueueOne'
export type SchedulesViewState = 'loading' | 'ready' | 'stale' | 'failed' | 'forbidden' | 'protocol-error'

export interface ScheduleRecord {
  readonly id: string
  readonly name: string
  readonly cronExpression: string
  readonly timeZoneId: string
  readonly enabled: boolean
  readonly concurrencyPolicy: ScheduleConcurrencyPolicy
  readonly kind: ScheduleKind
  readonly commandText: string | null
  readonly countdownSeconds: number | null
  readonly messageText: string | null
  readonly nextOccurrenceUtc: string | null
  readonly lastOccurrenceUtc: string | null
  readonly rowVersion: number
}

export interface ScheduleDraft {
  readonly id?: string
  readonly rowVersion?: number
  readonly name: string
  readonly cronExpression: string
  readonly timeZoneId: string
  readonly enabled: boolean
  readonly concurrencyPolicy: ScheduleConcurrencyPolicy
  readonly kind: ScheduleKind
  readonly commandText: string | null
  readonly countdownSeconds: number | null
  readonly messageText: string | null
}

export interface SchedulesController {
  state: DeepReadonly<ShallowRef<SchedulesViewState>>
  schedules: DeepReadonly<ShallowRef<readonly ScheduleRecord[]>>
  isMutating: DeepReadonly<ShallowRef<boolean>>
  errorCode: DeepReadonly<ShallowRef<string | null>>
  announce: (message: string) => Promise<boolean>
  save: (draft: ScheduleDraft) => Promise<boolean>
  setEnabled: (schedule: ScheduleRecord, enabled: boolean) => Promise<boolean>
  remove: (schedule: ScheduleRecord) => Promise<boolean>
  refresh: () => Promise<void>
  dispose: () => void
}

const scheduleKinds = new Set<ScheduleKind>(['ScheduledConsoleCommand', 'ScheduledRestart', 'ScheduledAnnouncement'])
const concurrencyPolicies = new Set<ScheduleConcurrencyPolicy>(['SkipIfRunning', 'QueueOne'])

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function nullableString(value: unknown): string | null {
  if (value === null)
    return null
  if (typeof value !== 'string')
    throw new Error('Invalid server protocol')
  return value
}

function nullableUtc(value: unknown): string | null {
  const result = nullableString(value)
  if (result !== null && (!/(?:Z|\+00:00)$/.test(result) || !Number.isFinite(Date.parse(result))))
    throw new Error('Invalid server protocol')
  return result
}

function parseSchedule(value: unknown): ScheduleRecord {
  if (!isRecord(value)
    || typeof value.id !== 'string'
    || typeof value.name !== 'string'
    || typeof value.cronExpression !== 'string'
    || typeof value.timeZoneId !== 'string'
    || typeof value.enabled !== 'boolean'
    || typeof value.concurrencyPolicy !== 'string'
    || !concurrencyPolicies.has(value.concurrencyPolicy as ScheduleConcurrencyPolicy)
    || typeof value.kind !== 'string'
    || !scheduleKinds.has(value.kind as ScheduleKind)
    || typeof value.rowVersion !== 'number'
    || !Number.isSafeInteger(value.rowVersion)
    || value.rowVersion < 1) {
    throw new Error('Invalid server protocol')
  }

  const record: ScheduleRecord = Object.freeze({
    id: value.id,
    name: value.name,
    cronExpression: value.cronExpression,
    timeZoneId: value.timeZoneId,
    enabled: value.enabled,
    concurrencyPolicy: value.concurrencyPolicy as ScheduleConcurrencyPolicy,
    kind: value.kind as ScheduleKind,
    commandText: nullableString(value.commandText),
    countdownSeconds: value.countdownSeconds === null ? null : Number(value.countdownSeconds),
    messageText: nullableString(value.messageText),
    nextOccurrenceUtc: nullableUtc(value.nextOccurrenceUtc),
    lastOccurrenceUtc: nullableUtc(value.lastOccurrenceUtc),
    rowVersion: value.rowVersion,
  })
  if (record.kind === 'ScheduledConsoleCommand' && (record.commandText === null || record.countdownSeconds !== null || record.messageText !== null))
    throw new Error('Invalid server protocol')
  if (record.kind === 'ScheduledRestart' && (!Number.isSafeInteger(record.countdownSeconds) || record.commandText !== null || record.messageText !== null))
    throw new Error('Invalid server protocol')
  if (record.kind === 'ScheduledAnnouncement' && (record.messageText === null || record.commandText !== null || record.countdownSeconds !== null))
    throw new Error('Invalid server protocol')
  return record
}

function parseSchedules(value: unknown): readonly ScheduleRecord[] {
  if (!Array.isArray(value))
    throw new Error('Invalid server protocol')
  return Object.freeze(value.map(parseSchedule))
}

export function useSchedules(options: { onSessionExpired?: () => void } = {}): SchedulesController {
  const auth = useAuthStore()
  const state = shallowRef<SchedulesViewState>('loading')
  const schedules = shallowRef<readonly ScheduleRecord[]>(Object.freeze([]))
  const isMutating = shallowRef(false)
  const errorCode = shallowRef<string | null>(null)
  let listController: AbortController | null = null
  let mutationController: AbortController | null = null
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
      schedules.value = Object.freeze([])
      state.value = 'forbidden'
      return
    }
    errorCode.value = error instanceof HttpError ? (error.problemCode ?? error.code) : 'protocol_error'
    state.value = errorCode.value === 'protocol_error'
      ? 'protocol-error'
      : schedules.value.length === 0 ? 'failed' : 'stale'
  }

  async function refresh() {
    if (disposed)
      return
    listController?.abort()
    const version = ++requestVersion
    const controller = new AbortController()
    listController = controller
    if (schedules.value.length === 0)
      state.value = 'loading'
    try {
      const nextSchedules = parseSchedules(await listSchedules({ signal: controller.signal }))
      if (disposed || version !== requestVersion)
        return
      schedules.value = nextSchedules
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

  async function mutate(operation: (signal: AbortSignal) => Promise<unknown>) {
    if (disposed || isMutating.value)
      return false
    isMutating.value = true
    errorCode.value = null
    const controller = new AbortController()
    mutationController = controller
    try {
      await operation(controller.signal)
      if (!disposed)
        await refresh()
      return !disposed
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

  function announce(message: string) {
    const normalized = message.trim()
    if (Array.from(normalized).length < 1 || Array.from(normalized).length > 500) {
      errorCode.value = 'announcement_invalid'
      return Promise.resolve(false)
    }
    return mutate(signal => sendAnnouncement({ body: { messageText: normalized }, signal }))
  }

  function save(draft: ScheduleDraft) {
    const body = {
      name: draft.name.trim(),
      cronExpression: draft.cronExpression.trim(),
      timeZoneId: draft.timeZoneId.trim(),
      enabled: draft.enabled,
      concurrencyPolicy: draft.concurrencyPolicy,
      kind: draft.kind,
      commandText: draft.kind === 'ScheduledConsoleCommand' ? draft.commandText?.trim() : null,
      countdownSeconds: draft.kind === 'ScheduledRestart' ? draft.countdownSeconds : null,
      messageText: draft.kind === 'ScheduledAnnouncement' ? draft.messageText?.trim() : null,
      ...(draft.rowVersion === undefined ? {} : { rowVersion: draft.rowVersion }),
    }
    return draft.id === undefined
      ? mutate(signal => createSchedule({ body, signal }))
      : mutate(signal => updateSchedule({ path: { scheduleId: draft.id! }, body, signal }))
  }

  function setEnabled(schedule: ScheduleRecord, enabled: boolean) {
    const operation = enabled ? enableSchedule : disableSchedule
    return mutate(signal => operation({
      path: { scheduleId: schedule.id },
      body: { rowVersion: schedule.rowVersion },
      signal,
    }))
  }

  function remove(schedule: ScheduleRecord) {
    return mutate(signal => deleteSchedule({
      path: { scheduleId: schedule.id },
      query: { rowVersion: schedule.rowVersion },
      signal,
    }))
  }

  function dispose() {
    if (disposed)
      return
    disposed = true
    requestVersion++
    listController?.abort()
    mutationController?.abort()
    listController = null
    mutationController = null
  }

  onMounted(() => void refresh())
  onUnmounted(dispose)

  return {
    state: readonly(state),
    schedules: readonly(schedules),
    isMutating: readonly(isMutating),
    errorCode: readonly(errorCode),
    announce,
    save,
    setEnabled,
    remove,
    refresh,
    dispose,
  }
}
