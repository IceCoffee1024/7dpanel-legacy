import { createApp, nextTick, shallowRef } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const evidenceApi = vi.hoisted(() => ({
  fetchPlayerProfile: vi.fn(),
  fetchPlayerInventorySnapshots: vi.fn(),
  fetchPlayerInventoryDiffs: vi.fn(),
  fetchPlayerSkills: vi.fn(),
}))
const actionsApi = vi.hoisted(() => ({
  grantPlayerItem: vi.fn(),
  removePlayerItem: vi.fn(),
  resetPlayerSkills: vi.fn(),
  clearPlayerInventory: vi.fn(),
  resetPlayerData: vi.fn(),
  fetchPlayerActionOperation: vi.fn(),
}))

vi.mock('../api/playerEvidence', () => evidenceApi)
vi.mock('../api/playerActions', () => actionsApi)

import { usePlayerActions } from './usePlayerActions'
import { usePlayerEvidence } from './usePlayerEvidence'
import { usePlayerProfile } from './usePlayerProfile'

function mountComposable<T>(factory: () => T) {
  let result!: T
  const app = createApp({
    setup() {
      result = factory()
      return () => null
    },
  })
  app.mount(document.createElement('div'))
  return { app, result }
}

function pending<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((next) => { resolve = next })
  return { promise, resolve }
}

function profile(crossplatformId: string) {
  const section = { state: 'Available', observedAtUtc: null, value: null, gapMetadata: [] }
  return {
    crossplatformId,
    summary: section,
    sessions: section,
    activity: section,
    inventory: section,
    skills: section,
    dailyActivity: section,
  }
}

describe('player profile composables', () => {
  const auth = {
    authorizationHeader: 'Bearer owner' as string | null,
    role: 'Owner' as string | null,
    expireSession: vi.fn(),
  }

  beforeEach(() => vi.clearAllMocks())

  it('suppresses a stale profile response after the player changes', async () => {
    const first = pending<ReturnType<typeof profile>>()
    evidenceApi.fetchPlayerProfile
      .mockReturnValueOnce(first.promise)
      .mockResolvedValueOnce(profile('player-b'))
    const playerId = shallowRef('player-a')
    const { app, result } = mountComposable(() => usePlayerProfile(playerId, { auth }))

    playerId.value = 'player-b'
    await nextTick()
    await Promise.resolve()
    first.resolve(profile('player-a'))
    await Promise.resolve()

    expect(result.profile.value?.crossplatformId).toBe('player-b')
    expect(evidenceApi.fetchPlayerProfile.mock.calls[0]?.[2].aborted).toBe(true)
    app.unmount()
  })

  it('aborts all evidence requests when the consumer unmounts', () => {
    const never = () => new Promise(() => {})
    evidenceApi.fetchPlayerInventorySnapshots.mockImplementation(never)
    evidenceApi.fetchPlayerInventoryDiffs.mockImplementation(never)
    evidenceApi.fetchPlayerSkills.mockImplementation(never)
    const { app } = mountComposable(() => usePlayerEvidence(shallowRef('player-a'), { auth }))
    const signals = [
      evidenceApi.fetchPlayerInventorySnapshots.mock.calls[0]?.[3],
      evidenceApi.fetchPlayerInventoryDiffs.mock.calls[0]?.[3],
      evidenceApi.fetchPlayerSkills.mock.calls[0]?.[3],
    ]

    app.unmount()

    expect(signals.every(signal => signal?.aborted)).toBe(true)
  })

  it('keeps a locked target unchanged and invalidates it after refresh selects another player', () => {
    const freshTarget = shallowRef({
      crossplatformId: 'player-a',
      entityId: 7,
      onlineObservedAtUtc: '2026-07-27T01:00:00Z',
      worldId: 'world-1',
    })
    const { app, result } = mountComposable(() => usePlayerActions({ auth, freshTarget }))

    result.lockTarget()
    freshTarget.value = {
      crossplatformId: 'player-b',
      entityId: 8,
      onlineObservedAtUtc: '2026-07-27T01:01:00Z',
      worldId: 'world-1',
    }

    expect(result.target.value?.crossplatformId).toBe('player-a')
    expect(result.targetValid.value).toBe(false)
    app.unmount()
  })
})
