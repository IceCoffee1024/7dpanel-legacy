import type {
  HistoricalPlayerDetails,
  HistoricalPlayerSnapshot,
  HistoricalPlayerSnapshotsPage,
  PlayerHistoryGap,
} from '../api/historyPlayers'

import type { HistoricalPlayerController } from './useHistoricalPlayer'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { defineComponent, shallowRef } from 'vue'
import { HttpError } from '../../../shared/api/http'
import { useHistoricalPlayer } from './useHistoricalPlayer'

function details(): HistoricalPlayerDetails {
  return {
    player: {
      crossplatformId: 'EOS_ada',
      latestName: 'Ada',
      firstObservedAtUtc: '2026-07-22T08:00:00Z',
      lastObservedAtUtc: '2026-07-22T08:30:00Z',
      totalObservationCount: 2,
      retainedSnapshotCount: 2,
      compactedSnapshotCount: 0,
      hasGaps: false,
    },
    gapSummary: { gapCount: 0, droppedObservationCount: 0 },
  }
}

function snapshot(snapshotId = 2): HistoricalPlayerSnapshot {
  return {
    snapshotId,
    player: {
      entityId: 42,
      name: 'Ada',
      platformIdentity: { combinedId: 'Steam_ada', platform: 'Steam' },
      crossplatformIdentity: { combinedId: 'EOS_ada', platform: 'EOS' },
      deviceType: 'windows',
      ip: null,
      ping: 23,
      compatibilityVersion: null,
      discordUserId: null,
      permissionLevel: 1000,
      position: { x: 1, y: 2, z: 3 },
      isDead: false,
      health: 96,
      maxHealth: 100,
      level: 17,
      playGroup: null,
      lastLoginUtc: null,
      gameStage: null,
      expToNextLevel: null,
      skillPoints: null,
      bedroll: null,
      score: 827,
      zombieKills: 317,
      playerKills: 2,
      deaths: 4,
      totalTimePlayedMinutes: 1,
      distanceWalkedMeters: 2,
      totalItemsCrafted: 3,
      longestLifeMinutes: 4,
      currentLifeMinutes: 5,
      observedAtUtc: '2026-07-22T08:30:00Z',
    },
  }
}

function gap(gapId = 'gap-1'): PlayerHistoryGap {
  return { gapId, crossplatformId: 'EOS_ada', startedAtUtc: '2026-07-22T08:10:00Z', completedAtUtc: '2026-07-22T08:12:00Z', droppedCount: 3, reason: 'queue_full', recordedAtUtc: '2026-07-22T08:12:01Z' }
}

function snapshotPage(
  snapshots: HistoricalPlayerSnapshot[],
  nextBeforeSnapshotId: number | null = null,
  gaps: PlayerHistoryGap[] = [],
): HistoricalPlayerSnapshotsPage {
  return { snapshots, nextBeforeSnapshotId, gaps }
}

function mountComposable(options: Omit<Parameters<typeof useHistoricalPlayer>[0], 'crossplatformId'> & { crossplatformId?: string } = {}) {
  const crossplatformId = shallowRef(options.crossplatformId ?? 'EOS_ada')
  let controller!: HistoricalPlayerController
  const wrapper = mount(defineComponent({
    setup() {
      controller = useHistoricalPlayer({ ...options, crossplatformId })
      return () => null
    },
  }))
  return { controller, crossplatformId, wrapper }
}

describe('useHistoricalPlayer', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  it('loads player metadata and the first snapshot page together without polling', async () => {
    const fetchPlayer = vi.fn().mockResolvedValue(details())
    const fetchSnapshots = vi.fn().mockResolvedValue(snapshotPage([snapshot(), snapshot()], null, [gap(), gap()]))
    const setInterval = vi.spyOn(globalThis, 'setInterval')
    const { controller, wrapper } = mountComposable({
      auth: { authorizationHeader: 'Bearer token', expireSession: vi.fn() },
      fetchPlayer,
      fetchSnapshots,
    })
    await flushPromises()

    expect(fetchPlayer).toHaveBeenCalledOnce()
    expect(fetchSnapshots).toHaveBeenCalledOnce()
    expect(controller.state.value).toBe('ready')
    expect(controller.snapshots.value).toEqual([snapshot()])
    expect(controller.gaps.value).toEqual([gap()])
    expect(setInterval).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('loads only later snapshot pages and deduplicates snapshot and gap IDs', async () => {
    const fetchPlayer = vi.fn().mockResolvedValue(details())
    const fetchSnapshots = vi.fn()
      .mockResolvedValueOnce(snapshotPage([snapshot(2)], 1, [gap('gap-1')]))
      .mockResolvedValueOnce(snapshotPage([snapshot(2), snapshot(1)], null, [gap('gap-1'), gap('gap-2')]))
    const { controller, wrapper } = mountComposable({
      auth: { authorizationHeader: 'Bearer token', expireSession: vi.fn() },
      fetchPlayer,
      fetchSnapshots,
    })
    await flushPromises()

    await controller.loadMore()

    expect(fetchPlayer).toHaveBeenCalledOnce()
    expect(controller.snapshots.value.map(value => value.snapshotId)).toEqual([2, 1])
    expect(controller.gaps.value.map(value => value.gapId)).toEqual(['gap-1', 'gap-2'])
    wrapper.unmount()
  })

  it('preserves loaded detail on refresh failure as stale', async () => {
    const fetchPlayer = vi.fn().mockResolvedValue(details())
    const fetchSnapshots = vi.fn()
      .mockResolvedValueOnce(snapshotPage([snapshot()]))
      .mockRejectedValueOnce(new HttpError('network', 'offline'))
    const { controller, wrapper } = mountComposable({
      auth: { authorizationHeader: 'Bearer token', expireSession: vi.fn() },
      fetchPlayer,
      fetchSnapshots,
    })
    await flushPromises()

    await controller.refresh()

    expect(controller.state.value).toBe('stale')
    expect(controller.details.value).toEqual(details())
    expect(controller.snapshots.value).toEqual([snapshot()])
    wrapper.unmount()
  })

  it('maps not-found and forbidden responses to distinct states', async () => {
    const notFound = mountComposable({
      auth: { authorizationHeader: 'Bearer token', expireSession: vi.fn() },
      fetchPlayer: vi.fn().mockRejectedValue(new HttpError('http', 'missing', { status: 404 })),
      fetchSnapshots: vi.fn().mockResolvedValue(snapshotPage([])),
    })
    await flushPromises()
    expect(notFound.controller.state.value).toBe('not-found')
    notFound.wrapper.unmount()

    const forbidden = mountComposable({
      auth: { authorizationHeader: 'Bearer token', expireSession: vi.fn() },
      fetchPlayer: vi.fn().mockRejectedValue(new HttpError('http', 'forbidden', { status: 403 })),
      fetchSnapshots: vi.fn().mockResolvedValue(snapshotPage([])),
    })
    await flushPromises()
    expect(forbidden.controller.state.value).toBe('forbidden')
    forbidden.wrapper.unmount()
  })

  it('clears the refresh indicator when an ID change cancels work after session expiry', async () => {
    const auth = { authorizationHeader: 'Bearer token' as string | null, expireSession: vi.fn() }
    const pending = new Promise<HistoricalPlayerDetails>(() => {})
    const { controller, crossplatformId, wrapper } = mountComposable({
      auth,
      fetchPlayer: vi.fn(() => pending),
      fetchSnapshots: vi.fn().mockResolvedValue(snapshotPage([])),
    })

    auth.authorizationHeader = null
    crossplatformId.value = 'EOS_bob'
    await flushPromises()

    expect(controller.isRefreshing.value).toBe(false)
    wrapper.unmount()
  })
})
