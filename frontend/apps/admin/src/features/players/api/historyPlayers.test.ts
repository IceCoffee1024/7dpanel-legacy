import { afterEach, describe, expect, it, vi } from 'vitest'

import { requestJson } from '../../../shared/api/http'

import {
  fetchHistoricalPlayer,
  fetchHistoricalPlayers,
  fetchHistoricalSnapshots,
  parseHistoricalPlayer,
  parseHistoricalPlayers,
  parseHistoricalSnapshots,
} from './historyPlayers'

vi.mock('../../../shared/api/http', () => ({
  requestJson: vi.fn(),
}))

afterEach(() => {
  vi.clearAllMocks()
})

function playerSnapshot() {
  return {
    entityId: 42,
    name: 'Ada',
    platformIdentity: { combinedId: 'Steam_123', platform: 'Steam' },
    crossplatformIdentity: { combinedId: 'EOS_0002d12af0fe4add9c7de0fbc238d431', platform: 'EOS' },
    deviceType: 'windows',
    ip: '192.0.2.10',
    ping: 23,
    compatibilityVersion: 'V 3.0.1',
    discordUserId: '18446744073709551615',
    permissionLevel: 1000,
    position: { x: 100.5, y: 51, z: 200.25 },
    isDead: false,
    health: 96,
    maxHealth: 100,
    level: 17,
    playGroup: 'Survivalists',
    lastLoginUtc: '2026-07-22T08:00:00.000Z',
    gameStage: 143,
    expToNextLevel: 1200,
    skillPoints: 4,
    bedroll: { x: 100, y: 70, z: 200 },
    score: 827,
    zombieKills: 317,
    playerKills: 2,
    deaths: 4,
    totalTimePlayedMinutes: 4823.5,
    distanceWalkedMeters: 127540.75,
    totalItemsCrafted: 2360,
    longestLifeMinutes: 920.25,
    currentLifeMinutes: 134.5,
    observedAtUtc: '2026-07-22T08:30:00.123Z',
  }
}

function playerSummary() {
  return {
    crossplatformId: 'EOS_0002d12af0fe4add9c7de0fbc238d431',
    latestName: 'Ada',
    firstObservedAtUtc: '2026-07-22T08:00:00.000Z',
    lastObservedAtUtc: '2026-07-22T08:30:00.123Z',
    totalObservationCount: 8,
    retainedSnapshotCount: 5,
    compactedSnapshotCount: 3,
    hasGaps: true,
  }
}

function snapshotsResponse() {
  return {
    snapshots: [{ snapshotId: 19, ...playerSnapshot() }],
    nextBeforeSnapshotId: 18,
    gaps: [{
      gapId: 'b73b440e-aee4-424f-bb7b-76e5b42351d4',
      crossplatformId: 'EOS_0002d12af0fe4add9c7de0fbc238d431',
      startedAtUtc: '2026-07-22T08:10:00.000Z',
      completedAtUtc: '2026-07-22T08:12:00.000Z',
      droppedCount: 3,
      reason: 'queue_full',
      recordedAtUtc: '2026-07-22T08:12:01.000Z',
    }],
  }
}

describe('historical player response parsers', () => {
  it('parses and deeply freezes a historical player page', () => {
    const response = { players: [playerSummary()], nextCursor: 'eyJmIjoxfQ' }

    const result = parseHistoricalPlayers(response)
    response.players[0]!.latestName = 'Changed'
    response.players.push(playerSummary())

    expect(result).toEqual({ players: [playerSummary()], nextCursor: 'eyJmIjoxfQ' })
    expect(Object.isFrozen(result)).toBe(true)
    expect(Object.isFrozen(result.players)).toBe(true)
    expect(Object.isFrozen(result.players[0])).toBe(true)
  })

  it('parses and freezes historical details and gap counts', () => {
    const result = parseHistoricalPlayer({
      player: playerSummary(),
      gapSummary: { gapCount: 2, droppedObservationCount: 9 },
    })

    expect(result).toEqual({
      player: playerSummary(),
      gapSummary: { gapCount: 2, droppedObservationCount: 9 },
    })
    expect(Object.isFrozen(result.player)).toBe(true)
    expect(Object.isFrozen(result.gapSummary)).toBe(true)
  })

  it('parses the complete 31-field historical snapshots and gaps', () => {
    const result = parseHistoricalSnapshots(snapshotsResponse())

    expect(result.snapshots[0]).toMatchObject({
      snapshotId: 19,
      player: playerSnapshot(),
    })
    expect(result.gaps[0]).toEqual(snapshotsResponse().gaps[0])
    expect(Object.isFrozen(result)).toBe(true)
    expect(Object.isFrozen(result.snapshots)).toBe(true)
    expect(Object.isFrozen(result.snapshots[0])).toBe(true)
    expect(Object.isFrozen(result.snapshots[0]!.player)).toBe(true)
    expect(Object.isFrozen(result.gaps)).toBe(true)
    expect(Object.isFrozen(result.gaps[0])).toBe(true)
  })

  it.each([
    ['a non-object list root', () => parseHistoricalPlayers(null)],
    ['a blank cross-platform ID', () => parseHistoricalPlayers({
      players: [{ ...playerSummary(), crossplatformId: ' ' }],
      nextCursor: null,
    })],
    ['a negative summary count', () => parseHistoricalPlayers({
      players: [{ ...playerSummary(), totalObservationCount: -1 }],
      nextCursor: null,
    })],
    ['an invalid summary timestamp', () => parseHistoricalPlayers({
      players: [{ ...playerSummary(), firstObservedAtUtc: '2026-02-29T08:00:00Z' }],
      nextCursor: null,
    })],
    ['a non-integer snapshot ID', () => parseHistoricalSnapshots({
      ...snapshotsResponse(),
      snapshots: [{ snapshotId: 19.5, ...playerSnapshot() }],
    })],
    ['a snapshot without its cross-platform identity', () => parseHistoricalSnapshots({
      ...snapshotsResponse(),
      snapshots: [{ snapshotId: 19, ...playerSnapshot(), crossplatformIdentity: null }],
    })],
    ['a partial bedroll', () => parseHistoricalSnapshots({
      ...snapshotsResponse(),
      snapshots: [{ snapshotId: 19, ...playerSnapshot(), bedroll: { x: 1, y: 2 } }],
    })],
    ['an unknown gap reason', () => parseHistoricalSnapshots({
      ...snapshotsResponse(),
      gaps: [{ ...snapshotsResponse().gaps[0], reason: 'network' }],
    })],
    ['an unsafe next snapshot ID', () => parseHistoricalSnapshots({
      ...snapshotsResponse(),
      nextBeforeSnapshotId: Number.MAX_SAFE_INTEGER + 1,
    })],
  ])('rejects %s', (_description, parse) => {
    expect(parse).toThrow('Invalid')
  })

  it('uses URLSearchParams and the authorization header for historical list requests', async () => {
    vi.mocked(requestJson).mockResolvedValue({ players: [playerSummary()], nextCursor: null })
    const controller = new AbortController()

    await expect(fetchHistoricalPlayers('Bearer token', {
      query: 'Ada & Bob',
      pageSize: 25,
      cursor: 'eyJmIjoxfQ',
    }, controller.signal)).resolves.toEqual({ players: [playerSummary()], nextCursor: null })

    expect(requestJson).toHaveBeenCalledWith(
      '/api/v1/players/history?query=Ada+%26+Bob&pageSize=25&cursor=eyJmIjoxfQ',
      { headers: { Authorization: 'Bearer token' }, signal: controller.signal },
    )
  })

  it('encodes cross-platform IDs only in paths and paginates snapshots by query', async () => {
    vi.mocked(requestJson)
      .mockResolvedValueOnce({
        player: playerSummary(),
        gapSummary: { gapCount: 0, droppedObservationCount: 0 },
      })
      .mockResolvedValueOnce(snapshotsResponse())
    const crossplatformId = 'EOS_0002d12af0fe4add9c7de0fbc238d431/alt'

    await fetchHistoricalPlayer('Bearer token', crossplatformId)
    await fetchHistoricalSnapshots('Bearer token', crossplatformId, {
      pageSize: 100,
      beforeSnapshotId: 19,
    })

    expect(requestJson).toHaveBeenNthCalledWith(1, '/api/v1/players/history/EOS_0002d12af0fe4add9c7de0fbc238d431%2Falt', { headers: { Authorization: 'Bearer token' }, signal: undefined })
    expect(requestJson).toHaveBeenNthCalledWith(2, '/api/v1/players/history/EOS_0002d12af0fe4add9c7de0fbc238d431%2Falt/snapshots?pageSize=100&beforeSnapshotId=19', { headers: { Authorization: 'Bearer token' }, signal: undefined })
  })
})
