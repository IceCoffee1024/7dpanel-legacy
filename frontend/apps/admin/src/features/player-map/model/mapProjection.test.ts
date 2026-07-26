import type { MapMetadata } from '../api/playerMap'

import { expect, it } from 'vitest'

import {
  createGameProjection,
  createTileResolutions,
  fromMapCoordinate,
  toMapCoordinate,
} from './mapProjection'

const metadata: MapMetadata = {
  availability: 'available',
  observedAtUtc: '2026-07-26T08:29:00Z',
  worldId: 'world-navezgane',
  worldName: 'Navezgane',
  extent: { minimumX: -4096, minimumZ: -2048, maximumX: 4096, maximumZ: 2048 },
  axes: { xAxisDirection: 'east', zAxisDirection: 'north' },
  availableZoomLevels: [0, 1, 2],
  tileSize: 256,
  mapResourceVersion: null,
}

it('creates a finite north-up game projection with same-direction X/Z coordinates', () => {
  const gameProjection = createGameProjection(metadata)

  expect(gameProjection.getExtent()).toEqual([-4096, -2048, 4096, 2048])
  expect(gameProjection.getCode()).toBe('7DPANEL:world-navezgane')
  expect(gameProjection.getAxisOrientation()).toBe('enu')
  expect(toMapCoordinate({ x: 123, z: 456 })).toEqual([123, 456])
  expect(fromMapCoordinate([123, 456])).toEqual({ x: 123, z: 456 })
})

it('derives bounded tile resolutions from metadata without guessing the world size', () => {
  expect(createTileResolutions(metadata)).toEqual([32, 16, 8])
})
