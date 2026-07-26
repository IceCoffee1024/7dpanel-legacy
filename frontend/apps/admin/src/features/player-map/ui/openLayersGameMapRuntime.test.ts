import type { MapMetadata } from '../api/playerMap'
import type { FitRequest, OnlineMapPlayer } from '../model/usePlayerMap'

import Feature from 'ol/Feature.js'
import Circle from 'ol/geom/Circle.js'
import Point from 'ol/geom/Point.js'
import Polygon from 'ol/geom/Polygon.js'
import { describe, expect, it, vi } from 'vitest'

import {
  applyFitOnce,
  areaGeometryFromOlGeometry,
  createAreaFeature,
  createGameView,
  disposeGameMapResources,
  handleMapSingleClick,
} from './openLayersGameMapRuntime'

const metadata: MapMetadata = {
  availability: 'available',
  observedAtUtc: '2026-07-26T08:29:00Z',
  worldId: 'world-navezgane',
  worldName: 'Navezgane',
  extent: { minimumX: -100, minimumZ: -200, maximumX: 300, maximumZ: 400 },
  axes: { xAxisDirection: 'east', zAxisDirection: 'north' },
  availableZoomLevels: [0, 1, 2, 3, 4],
  tileSize: 256,
  mapResourceVersion: null,
}

describe('openLayers game map runtime', () => {
  it('creates a north-up view that rejects rotation changes', () => {
    const view = createGameView(metadata)

    view.adjustRotation(Math.PI / 2)

    expect(view.getRotation()).toBe(0)
    expect(view.getProjection().getExtent()).toEqual([-100, -200, 300, 400])
  })

  it('separates map coordinate selection from online and observation feature selection', () => {
    const online: OnlineMapPlayer = {
      combinedId: 'EOS_ada',
      name: 'Ada',
      position: { x: 1, y: 2, z: 3 },
      observedAtUtc: '2026-07-26T08:00:00Z',
    }
    const onCoordinate = vi.fn()
    const onOnline = vi.fn()
    const onObservation = vi.fn()
    const feature = new Feature({ geometry: new Point([1, -3]), role: 'online-player', player: online })
    const fakeMap = {
      forEachFeatureAtPixel: vi.fn((_pixel, callback: (value: Feature<Point>) => boolean) => callback(feature)),
    }

    handleMapSingleClick(fakeMap, [5, 6], [12, -34], {
      onSelectedCoordinate: onCoordinate,
      onSelectOnlinePlayer: onOnline,
      onSelectObservation: onObservation,
    })

    expect(onCoordinate).toHaveBeenCalledWith({ x: 12, z: -34 })
    expect(onOnline).toHaveBeenCalledWith('EOS_ada')
    expect(onObservation).not.toHaveBeenCalled()
  })

  it('fits each request key at most once', () => {
    const fit = vi.fn()
    const view = { fit }
    const request: FitRequest = { queryKey: 'one', extent: [0, 0, 10, 10] }

    let previous: string | null = null
    previous = applyFitOnce(view, request, previous)
    previous = applyFitOnce(view, request, previous)

    expect(previous).toBe('one')
    expect(fit).toHaveBeenCalledOnce()
  })

  it('converts rectangle and circle investigation geometry in both directions', () => {
    const rectangle = { kind: 'rectangle', minimumX: -10, minimumZ: -20, maximumX: 30, maximumZ: 40 } as const
    const circle = { kind: 'circle', centerX: 5, centerZ: -6, radius: 7 } as const

    expect(areaGeometryFromOlGeometry(createAreaFeature(rectangle).getGeometry()!)).toEqual(rectangle)
    expect(areaGeometryFromOlGeometry(createAreaFeature(circle).getGeometry()!)).toEqual(circle)
    expect(areaGeometryFromOlGeometry(new Polygon([[[0, 0], [4, 0], [3, 2], [0, 0]]]))).toEqual({
      kind: 'rectangle',
      minimumX: 0,
      minimumZ: 0,
      maximumX: 4,
      maximumZ: 2,
    })
    expect(areaGeometryFromOlGeometry(new Circle([2, 3], 0))).toBeNull()
  })

  it('detaches the target and releases listeners, sources and layers', () => {
    const setTarget = vi.fn()
    const dispose = vi.fn()
    const clearLayers = vi.fn()
    const clear = vi.fn()
    const setSource = vi.fn()
    const unlisten = vi.fn()

    disposeGameMapResources({
      map: { setTarget, dispose, getLayers: () => ({ clear: clearLayers }) },
      eventKeys: [{ type: 'singleclick' }],
      sources: [{ clear }],
      layers: [{ setSource }],
      unlisten,
    })

    expect(unlisten).toHaveBeenCalledOnce()
    expect(clear).toHaveBeenCalledWith(true)
    expect(setSource).toHaveBeenCalledWith(null)
    expect(setTarget).toHaveBeenCalledWith(undefined)
    expect(clearLayers).toHaveBeenCalledOnce()
    expect(dispose).toHaveBeenCalledOnce()
  })
})
