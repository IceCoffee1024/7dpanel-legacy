import type { App } from 'vue'

import { flushPromises } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp } from 'vue'

import { useSchedules } from './useSchedules'

const api = vi.hoisted(() => ({
  createSchedule: vi.fn(),
  deleteSchedule: vi.fn(),
  disableSchedule: vi.fn(),
  enableSchedule: vi.fn(),
  listSchedules: vi.fn(),
  sendAnnouncement: vi.fn(),
  updateSchedule: vi.fn(),
}))

vi.mock('../../../shared/api/generated', () => api)

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((resolvePromise) => {
    resolve = resolvePromise
  })
  return { promise, resolve }
}

function schedules(id: string) {
  return [{
    id,
    name: id,
    cronExpression: '0 4 * * *',
    timeZoneId: 'UTC',
    enabled: true,
    concurrencyPolicy: 'SkipIfRunning',
    kind: 'ScheduledAnnouncement',
    commandText: null,
    countdownSeconds: null,
    messageText: 'hello',
    nextOccurrenceUtc: '2026-07-27T04:00:00Z',
    lastOccurrenceUtc: null,
    rowVersion: 1,
  }]
}

function mountComposable() {
  let result!: ReturnType<typeof useSchedules>
  const app = createApp({
    setup() {
      result = useSchedules()
      return () => null
    },
  })
  app.use(createPinia())
  app.mount(document.createElement('div'))
  return { app, result }
}

describe('useSchedules', () => {
  const apps: App[] = []

  beforeEach(() => vi.clearAllMocks())
  afterEach(() => {
    while (apps.length > 0)
      apps.pop()!.unmount()
  })

  it('does not let an older response overwrite a newer refresh', async () => {
    const older = deferred<ReturnType<typeof schedules>>()
    const newer = deferred<ReturnType<typeof schedules>>()
    api.listSchedules.mockReturnValueOnce(older.promise).mockReturnValueOnce(newer.promise)
    const mounted = mountComposable()
    apps.push(mounted.app)

    const refresh = mounted.result.refresh()
    newer.resolve(schedules('newer'))
    await refresh
    older.resolve(schedules('older'))
    await flushPromises()

    expect(mounted.result.schedules.value[0]?.id).toBe('newer')
  })
})
