import type { App } from 'vue'

import { flushPromises } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp } from 'vue'

import { useBackups } from './useBackups'

const api = vi.hoisted(() => ({
  createPanelDatabaseBackup: vi.fn(),
  createServerConfigurationBackup: vi.fn(),
  createWorldBackup: vi.fn(),
  deleteBackup: vi.fn(),
  downloadBackup: vi.fn(),
  getJob: vi.fn(),
  listBackups: vi.fn(),
  restoreBackup: vi.fn(),
}))

vi.mock('../../../shared/api/generated', () => api)

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((resolvePromise) => {
    resolve = resolvePromise
  })
  return { promise, resolve }
}

function page(id: string) {
  return {
    items: [{
      id,
      kind: 'PanelDatabase',
      sizeBytes: 10,
      sha256: 'a'.repeat(64),
      worldId: null,
      gameVersion: null,
      validationStatus: 'Verified',
      createdAtUtc: '2026-07-26T08:00:00Z',
      sourceJobId: `job-${id}`,
      manifestVersion: 1,
    }],
    nextCursor: null,
  }
}

function mountComposable() {
  let result!: ReturnType<typeof useBackups>
  const app = createApp({
    setup() {
      result = useBackups()
      return () => null
    },
  })
  app.use(createPinia())
  app.mount(document.createElement('div'))
  return { app, result }
}

describe('useBackups', () => {
  const apps: App[] = []

  beforeEach(() => vi.clearAllMocks())
  afterEach(() => {
    while (apps.length > 0)
      apps.pop()!.unmount()
  })

  it('does not let an older response overwrite a newer refresh', async () => {
    const older = deferred<ReturnType<typeof page>>()
    const newer = deferred<ReturnType<typeof page>>()
    api.listBackups.mockReturnValueOnce(older.promise).mockReturnValueOnce(newer.promise)
    const mounted = mountComposable()
    apps.push(mounted.app)

    const refresh = mounted.result.refresh()
    newer.resolve(page('newer'))
    await refresh
    older.resolve(page('older'))
    await flushPromises()

    expect(mounted.result.backups.value[0]?.id).toBe('newer')
  })
})
