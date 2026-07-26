import { describe, expect, it, vi } from 'vitest'

import { HttpError } from '../../../shared/api/http'
import { useAccessLists } from './useAccessLists'

const auth = {
  authorizationHeader: 'Bearer token' as string | null,
  role: 'Owner' as 'Owner' | 'Admin' | 'Viewer' | null,
  expireSession: vi.fn(),
}

describe('useAccessLists', () => {
  it('keeps viewers read-only while allowing refresh', async () => {
    const fetchBans = vi.fn().mockResolvedValue([])
    const controller = useAccessLists({
      auth: { ...auth, role: 'Viewer' },
      fetchBans,
      fetchWhitelist: vi.fn().mockResolvedValue([]),
    })

    await controller.refreshBans()

    expect(controller.canMutate.value).toBe(false)
    expect(controller.banState.value).toBe('empty')
    expect(fetchBans).toHaveBeenCalledOnce()
  })

  it('maps game-not-ready without discarding stale data', async () => {
    const item = { playerId: 'EOS_1', displayName: 'Player', bannedUntilUtc: null, reason: null }
    const fetchBans = vi.fn()
      .mockResolvedValueOnce([item])
      .mockRejectedValueOnce(new HttpError('http', 'not ready', { status: 503, problemCode: 'game_not_ready' }))
    const controller = useAccessLists({ auth, fetchBans, fetchWhitelist: vi.fn().mockResolvedValue([]) })

    await controller.refreshBans()
    await controller.refreshBans()

    expect(controller.bans.value).toEqual([item])
    expect(controller.banState.value).toBe('game-not-ready')
  })

  it('prevents concurrent mutations and refreshes only the changed list after success', async () => {
    let release!: () => void
    const upsertBan = vi.fn(() => new Promise<void>((resolve) => { release = resolve }))
    const fetchBans = vi.fn().mockResolvedValue([])
    const controller = useAccessLists({ auth, fetchBans, fetchWhitelist: vi.fn().mockResolvedValue([]), upsertBan })
    const input = { playerId: 'EOS_1', displayName: 'Player', bannedUntilUtc: null, reason: null }

    const first = controller.saveBan(input)
    const second = controller.saveBan({ ...input, playerId: 'EOS_2' })
    expect(controller.mutationTarget.value).toEqual({ list: 'ban', playerId: 'EOS_1' })
    await expect(second).resolves.toBe(false)
    release()
    await expect(first).resolves.toBe(true)

    expect(upsertBan).toHaveBeenCalledOnce()
    expect(fetchBans).toHaveBeenCalledOnce()
  })
})
