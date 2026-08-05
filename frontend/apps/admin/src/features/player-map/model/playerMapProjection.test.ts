import type { MapMetadata, PlayerTrack } from '../api/playerMap'

import { describe, expect, it } from 'vitest'

import {
  mapPlayerMapPageState,
  playerMapWorldIdentity,
  playerTrackFitExtent,
  restorePlayerMapFilters,
  restorePlayerMapObservation,
} from './playerMapProjection'

const metadata: MapMetadata = {
  availability: 'available',
  observedAtUtc: '2026-07-26T08:29:00Z',
  worldId: 'world-navezgane',
  worldName: 'Navezgane',
  extent: { minimumX: -4096, minimumZ: -4096, maximumX: 4096, maximumZ: 4096 },
  axes: { xAxisDirection: 'east', zAxisDirection: 'north' },
  availableZoomLevels: [0, 1, 2],
  tileSize: 256,
  mapResourceVersion: null,
}

describe('player map projections', () => {
  it('restores only a valid UTC range and positive observation id', () => {
    const query = new URLSearchParams('player=%20EOS_ada%20&from=2026-07-25T00%3A00%3A00Z&to=2026-07-26T00%3A00%3A00Z&observation=4')

    expect(restorePlayerMapFilters(query)).toEqual({
      player: 'EOS_ada',
      fromUtc: '2026-07-25T00:00:00Z',
      toUtc: '2026-07-26T00:00:00Z',
    })
    expect(restorePlayerMapObservation(query)).toBe(4)
    expect(restorePlayerMapObservation(new URLSearchParams('observation=0'))).toBeNull()
    expect(restorePlayerMapFilters(new URLSearchParams('from=bad&to=bad')).fromUtc).toBeNull()
  })

  it('uses world identity and finite track extents as stable projections', () => {
    const track: PlayerTrack = {
      crossplatformId: 'EOS_ada',
      segments: [{ points: [
        { snapshotId: 1, name: 'Ada', x: 2, y: 9, z: 5, observedAtUtc: '2026-07-26T08:00:00Z' },
        { snapshotId: 2, name: 'Ada', x: 8, y: 9, z: 11, observedAtUtc: '2026-07-26T08:10:00Z' },
      ] }],
    }

    expect(playerMapWorldIdentity(metadata)).toContain('world-navezgane')
    expect(playerTrackFitExtent(track)).toEqual([2, 5, 8, 11])
    expect(playerTrackFitExtent({ ...track, segments: [{ points: [] }] })).toBeNull()
  })

  it('keeps page status projection independent from source mutation', () => {
    expect(mapPlayerMapPageState(null, 1, 1, 0, false)).toBe('failed')
    expect(mapPlayerMapPageState(metadata, 0, 0, 1, false)).toBe('partial')
    expect(mapPlayerMapPageState(metadata, 1, 0, 1, true)).toBe('stale')
    expect(mapPlayerMapPageState(metadata, 1, 0, 0, false)).toBe('ready')
  })
})
