import { describe, expect, it } from 'vitest'

import {
  parseMapGameTime,
  parseMapMetadata,
  parsePlayerTrack,
} from './playerMap'

const metadata = {
  availability: 'available',
  observedAtUtc: '2026-07-26T08:29:00Z',
  worldId: 'world-navezgane',
  worldName: 'Navezgane',
  extent: { minimumX: -4096, minimumZ: -4096, maximumX: 4096, maximumZ: 4096 },
  axes: { xAxisDirection: 'east', zAxisDirection: 'north' },
  availableZoomLevels: [0, 1, 2, 3, 4, 5, 6],
  tileSize: 256,
  mapResourceVersion: null,
}

const point = {
  snapshotId: 41,
  name: 'Ada',
  x: 120.5,
  y: 38,
  z: -20.25,
  observedAtUtc: '2026-07-26T08:30:00Z',
}

describe('player map runtime parsers', () => {
  it('accepts finite metadata with explicit X/Z axes and limits', () => {
    expect(parseMapMetadata(metadata)).toEqual(metadata)
  })

  it.each([
    { ...metadata, extent: { ...metadata.extent, maximumX: Number.NaN } },
    { ...metadata, extent: { ...metadata.extent, minimumX: 4096 } },
    { ...metadata, axes: { xAxisDirection: 'east', zAxisDirection: 'south' } },
    { ...metadata, availableZoomLevels: [0, 2, 1] },
    { ...metadata, unexpected: true },
  ])('rejects invalid or non-strict metadata', (value) => {
    expect(() => parseMapMetadata(value)).toThrow('Invalid map metadata response')
  })

  it('accepts only the exact unavailable metadata envelope with nullable fields', () => {
    const unavailable = {
      availability: 'unavailable',
      observedAtUtc: null,
      worldId: null,
      worldName: null,
      extent: null,
      axes: null,
      availableZoomLevels: null,
      tileSize: null,
      mapResourceVersion: null,
    }

    expect(parseMapMetadata(unavailable)).toEqual(unavailable)
    expect(() => parseMapMetadata({ ...unavailable, worldId: 'invented' }))
      .toThrow('Invalid map metadata response')
  })

  it('parses game day, time and its independent observation timestamp', () => {
    expect(parseMapGameTime({
      availability: 'available',
      day: 17,
      hour: 9,
      minute: 4,
      observedAtUtc: '2026-07-26T08:31:00Z',
    })).toEqual({
      availability: 'available',
      day: 17,
      hour: 9,
      minute: 4,
      observedAtUtc: '2026-07-26T08:31:00Z',
    })
  })

  it('accepts unavailable game time only when every value is null', () => {
    expect(parseMapGameTime({
      availability: 'unavailable',
      day: null,
      hour: null,
      minute: null,
      observedAtUtc: null,
    })).toEqual({
      availability: 'unavailable',
      day: null,
      hour: null,
      minute: null,
      observedAtUtc: null,
    })
  })

  it('preserves public track segments without exposing gap details', () => {
    expect(parsePlayerTrack({
      crossplatformId: 'EOS_ada',
      segments: [{ points: [point] }, { points: [{ ...point, snapshotId: 42 }] }],
    }).segments).toHaveLength(2)
  })

  it.each([
    { crossplatformId: 'EOS_ada', segments: [], gaps: [] },
    { crossplatformId: 'EOS_ada', segments: [], gapCount: 1 },
    { crossplatformId: 'EOS_ada', segments: [{ points: [point], reason: 'queue_full' }] },
    { crossplatformId: 'EOS_ada', segments: [{ points: [{ ...point, droppedCount: 2 }] }] },
  ])('rejects public responses containing gap details', (value) => {
    expect(() => parsePlayerTrack(value)).toThrow('Invalid player track response')
  })
})
