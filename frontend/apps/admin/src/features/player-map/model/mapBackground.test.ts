import type { MapMetadata } from '../api/playerMap'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import ImageStatic from 'ol/source/ImageStatic.js'

import { expect, it } from 'vitest'

import { createLocalBackgroundLayer } from './mapBackground'

const metadata: MapMetadata = {
  availability: 'available',
  observedAtUtc: '2026-07-26T08:29:00Z',
  worldId: 'world-navezgane',
  worldName: 'Navezgane',
  extent: { minimumX: -100, minimumZ: -200, maximumX: 300, maximumZ: 400 },
  axes: { xAxisDirection: 'east', zAxisDirection: 'north' },
  availableZoomLevels: [0, 1, 2],
  tileSize: 256,
  mapResourceVersion: null,
}

it('creates an always-visible project-owned local background across the finite world extent', () => {
  const layer = createLocalBackgroundLayer(metadata)

  expect(layer.getVisible()).toBe(true)
  expect(layer.getSource()).toBeInstanceOf(ImageStatic)
  expect(layer.getSource()?.getImageExtent()).toEqual([-100, -200, 300, 400])
  layer.getSource()?.dispose()
  layer.setSource(null)
})

it('records the original background asset source in the SVG itself', () => {
  const source = readFileSync(resolve(process.cwd(), 'src/assets/images/map-background.svg'), 'utf8')

  expect(source).toContain('Original asset created for 7DPanel, no external source')
})
