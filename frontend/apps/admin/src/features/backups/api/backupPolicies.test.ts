import { afterEach, expect, it, vi } from 'vitest'

import { requestJson } from '../../../shared/api/http'
import { saveBackupPolicy } from './backupPolicies'

vi.mock('../../../shared/api/http', () => ({ requestJson: vi.fn() }))

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

afterEach(() => vi.clearAllMocks())

it('sends only the fixed policy update DTO with the expected row version', async () => {
  vi.mocked(requestJson).mockResolvedValue(policy)

  await expect(saveBackupPolicy('Bearer token', policy)).resolves.toEqual(policy)

  expect(requestJson).toHaveBeenCalledWith('/api/v1/backups/policies/World', {
    method: 'PUT',
    headers: { Authorization: 'Bearer token', 'Content-Type': 'application/json' },
    body: JSON.stringify({
      enabled: true,
      cronExpression: '0 4 * * *',
      timeZoneId: 'UTC',
      backupRootId: 'primary',
      retentionCount: 5,
      retentionDays: 14,
      compressionEnabled: true,
      expectedRowVersion: 3,
    }),
    signal: undefined,
  })
})
