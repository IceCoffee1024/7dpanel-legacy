import type { App } from 'vue'

import { flushPromises } from '@vue/test-utils'
import { afterEach, expect, it, vi } from 'vitest'
import { createApp } from 'vue'

import { HttpError } from '../../../shared/api/http'
import { useBackupPolicies } from './useBackupPolicies'

const policy = {
  kind: 'World' as const,
  enabled: true,
  cronExpression: '0 4 * * *',
  timeZoneId: 'UTC',
  backupRootId: 'primary',
  retentionCount: 5,
  retentionDays: 14,
  compressionEnabled: true,
  rowVersion: 3,
}

function policies() {
  return [
    policy,
    { ...policy, kind: 'PanelDatabase' as const },
    { ...policy, kind: 'ServerConfiguration' as const },
  ]
}

function mountComposable(savePolicy = vi.fn()) {
  let result!: ReturnType<typeof useBackupPolicies>
  const app = createApp({
    setup() {
      result = useBackupPolicies({
        auth: { authorizationHeader: 'Bearer token', expireSession: vi.fn() },
        fetchPolicies: vi.fn().mockResolvedValue(policies()),
        savePolicy,
      })
      return () => null
    },
  })
  app.mount(document.createElement('div'))
  return { app, result }
}

const apps: App[] = []

afterEach(() => {
  while (apps.length > 0)
    apps.pop()!.unmount()
})

it('keeps an edited draft when the server reports an expected-version conflict', async () => {
  const savePolicy = vi.fn().mockRejectedValue(new HttpError('http', 'Conflict', {
    status: 409,
    problemCode: 'backup_policy_row_version_conflict',
  }))
  const mounted = mountComposable(savePolicy)
  apps.push(mounted.app)
  await flushPromises()

  const draft = { ...policy, cronExpression: '0 5 * * *' }
  mounted.result.updateDraft(draft)

  await expect(mounted.result.save('World')).resolves.toBe(false)

  expect(mounted.result.drafts.value.find(item => item.kind === 'World')).toEqual(draft)
  expect(mounted.result.saveError.value).toEqual({ kind: 'World', code: 'conflict' })
  expect(savePolicy).toHaveBeenCalledWith('Bearer token', draft, expect.any(AbortSignal))
})
