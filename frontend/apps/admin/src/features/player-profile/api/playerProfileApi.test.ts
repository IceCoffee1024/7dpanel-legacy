import { beforeEach, describe, expect, it, vi } from 'vitest'

const sdk = vi.hoisted(() => ({
  playerActionsGet: vi.fn(),
  playerActionsGrantItem: vi.fn(),
  playerActionsRemoveItem: vi.fn(),
  playerActionsResetSkills: vi.fn(),
  playerActionsClearInventory: vi.fn(),
  playerActionsResetPlayerData: vi.fn(),
  playerEvidenceGetProfile: vi.fn(),
  playerEvidenceGetInventorySnapshots: vi.fn(),
  playerEvidenceGetInventoryDiffs: vi.fn(),
  playerEvidenceGetSkills: vi.fn(),
}))

vi.mock('../../../shared/api/generated', () => sdk)

import { grantPlayerItem } from './playerActions'
import { fetchPlayerInventoryDiffs } from './playerEvidence'

describe('player profile API wrappers', () => {
  beforeEach(() => vi.clearAllMocks())

  it('forwards the opaque evidence cursor and cancellation signal unchanged', async () => {
    sdk.playerEvidenceGetInventoryDiffs.mockResolvedValue({ diffs: [] })
    const controller = new AbortController()

    await fetchPlayerInventoryDiffs('Bearer owner', 'EOS_123', {
      cursor: 'opaque.cursor/value',
      pageSize: 25,
    }, controller.signal)

    expect(sdk.playerEvidenceGetInventoryDiffs).toHaveBeenCalledWith({
      headers: { Authorization: 'Bearer owner' },
      path: { crossplatformId: 'EOS_123' },
      query: { cursor: 'opaque.cursor/value', pageSize: 25 },
      signal: controller.signal,
    })
  })

  it('rebuilds action bodies from the public whitelist', async () => {
    sdk.playerActionsGrantItem.mockResolvedValue({ operationId: 'op-1', status: 'Succeeded' })
    const input = {
      target: {
        crossplatformId: 'EOS_123',
        entityId: 7,
        onlineObservedAtUtc: '2026-07-27T01:00:00Z',
        worldId: 'world-1',
        operator: 'must-not-leak',
      },
      catalogVersion: 'catalog-1',
      resourceId: 'resource-1',
      quantity: 2,
      quality: null,
      hiddenItemConfirmed: false,
      clientRequestKey: 'request-1',
      correlation: 'must-not-leak',
      internalName: 'must-not-leak',
      itemKind: 'must-not-leak',
      command: 'must-not-leak',
      path: 'must-not-leak',
      token: 'must-not-leak',
    }

    await grantPlayerItem('Bearer owner', input)

    expect(sdk.playerActionsGrantItem).toHaveBeenCalledWith({
      headers: { Authorization: 'Bearer owner' },
      body: {
        target: {
          crossplatformId: 'EOS_123',
          entityId: 7,
          onlineObservedAtUtc: '2026-07-27T01:00:00Z',
          worldId: 'world-1',
        },
        catalogVersion: 'catalog-1',
        resourceId: 'resource-1',
        quantity: 2,
        quality: null,
        hiddenItemConfirmed: false,
        clientRequestKey: 'request-1',
      },
      signal: undefined,
    })
  })
})
