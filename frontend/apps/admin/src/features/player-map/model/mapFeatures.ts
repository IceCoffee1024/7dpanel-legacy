import type { Geometry } from 'ol/geom.js'
import type { PlayerTrack } from '../api/playerMap'

import Feature from 'ol/Feature.js'
import LineString from 'ol/geom/LineString.js'
import Point from 'ol/geom/Point.js'

import { toMapCoordinate } from './mapProjection'

export function createTrackFeatures(track: PlayerTrack): Feature<Geometry>[] {
  const features: Feature<Geometry>[] = []
  const points = track.segments.flatMap(segment => segment.points)

  for (const segment of track.segments) {
    if (segment.points.length > 1) {
      features.push(new Feature({
        geometry: new LineString(segment.points.map(toMapCoordinate)),
        role: 'track',
      }))
    }
    for (const point of segment.points) {
      features.push(new Feature({
        geometry: new Point(toMapCoordinate(point)),
        role: 'observation',
        snapshotId: point.snapshotId,
        observation: point,
      }))
    }
  }

  const first = points[0]
  const last = points[points.length - 1]
  if (first !== undefined) {
    features.push(new Feature({
      geometry: new Point(toMapCoordinate(first)),
      role: 'start',
      snapshotId: first.snapshotId,
    }))
  }
  if (last !== undefined) {
    features.push(new Feature({
      geometry: new Point(toMapCoordinate(last)),
      role: 'end',
      snapshotId: last.snapshotId,
    }))
  }
  return features
}
