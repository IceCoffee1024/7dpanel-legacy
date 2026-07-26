import type { Coordinate } from 'ol/coordinate.js'
import type { MapMetadata } from '../api/playerMap'

import Projection from 'ol/proj/Projection.js'

export interface GameXZ {
  readonly x: number
  readonly z: number
}

export function toMapCoordinate(position: GameXZ): Coordinate {
  return [position.x, position.z]
}

export function fromMapCoordinate(coordinate: Coordinate): GameXZ {
  return Object.freeze({ x: coordinate[0] ?? 0, z: coordinate[1] ?? 0 })
}

export function mapExtent(metadata: MapMetadata): [number, number, number, number] {
  return [
    metadata.extent.minimumX,
    metadata.extent.minimumZ,
    metadata.extent.maximumX,
    metadata.extent.maximumZ,
  ]
}

export function createGameProjection(metadata: MapMetadata): Projection {
  return new Projection({
    code: `7DPANEL:${metadata.worldId}`,
    units: 'm',
    axisOrientation: 'enu',
    extent: mapExtent(metadata),
  })
}

export function createTileResolutions(metadata: MapMetadata): readonly number[] {
  const width = metadata.extent.maximumX - metadata.extent.minimumX
  const height = metadata.extent.maximumZ - metadata.extent.minimumZ
  const initial = Math.max(width, height) / metadata.tileSize
  return Object.freeze(metadata.availableZoomLevels.map(level => initial / 2 ** level))
}
