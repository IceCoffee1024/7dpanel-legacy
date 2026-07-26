import type { PlayerTrack } from '../api/playerMap'
import LineString from 'ol/geom/LineString.js'
import Point from 'ol/geom/Point.js'

import { expect, it } from 'vitest'

import { createTrackFeatures } from './mapFeatures'

const track: PlayerTrack = {
  crossplatformId: 'EOS_ada',
  segments: [
    {
      points: [
        { snapshotId: 1, name: 'Ada', x: 0, y: 10, z: 0, observedAtUtc: '2026-07-26T08:00:00Z' },
        { snapshotId: 2, name: 'Ada', x: 5, y: 11, z: 10, observedAtUtc: '2026-07-26T08:01:00Z' },
      ],
    },
    {
      points: [
        { snapshotId: 8, name: 'Ada', x: 100, y: 12, z: 100, observedAtUtc: '2026-07-26T08:10:00Z' },
      ],
    },
  ],
}

it('creates one line per multi-point segment and never bridges separate segments', () => {
  const features = createTrackFeatures(track)
  const lines = features.filter(feature => feature.get('role') === 'track')

  expect(lines).toHaveLength(1)
  expect(lines[0]?.getGeometry()).toBeInstanceOf(LineString)
  expect((lines[0]?.getGeometry() as LineString).getCoordinates()).toEqual([[0, 0], [5, 10]])
})

it('marks the first and final returned observations while preserving single-point segments', () => {
  const features = createTrackFeatures(track)
  const start = features.find(feature => feature.get('role') === 'start')
  const end = features.find(feature => feature.get('role') === 'end')
  const observations = features.filter(feature => feature.get('role') === 'observation')

  expect(start?.getGeometry()).toBeInstanceOf(Point)
  expect(start?.get('snapshotId')).toBe(1)
  expect(end?.get('snapshotId')).toBe(8)
  expect(observations).toHaveLength(3)
})
