import type { Geometry } from 'ol/geom.js'
import type { MapMetadata } from '../api/playerMap'
import type { AreaGeometry } from '../model/useAreaInvestigation'

import Feature from 'ol/Feature.js'
import CircleGeometry from 'ol/geom/Circle.js'
import LineString from 'ol/geom/LineString.js'
import Polygon from 'ol/geom/Polygon.js'
import View from 'ol/View.js'

import { createGameProjection, mapExtent } from '../model/mapProjection'

export function createGameView(metadata: MapMetadata): View {
  const extent = mapExtent(metadata)
  const minimumZoom = metadata.availableZoomLevels[0]!
  const maximumZoom = metadata.availableZoomLevels[metadata.availableZoomLevels.length - 1]!
  return new View({
    projection: createGameProjection(metadata),
    extent,
    center: [(extent[0] + extent[2]) / 2, (extent[1] + extent[3]) / 2],
    zoom: minimumZoom,
    minZoom: minimumZoom,
    maxZoom: maximumZoom,
    rotation: 0,
    enableRotation: false,
    showFullExtent: true,
  })
}

export function createAreaFeature(geometry: AreaGeometry): Feature<CircleGeometry | Polygon> {
  if (geometry.kind === 'circle')
    return new Feature(new CircleGeometry([geometry.centerX, geometry.centerZ], geometry.radius))
  return new Feature(new Polygon([[[
    geometry.minimumX,
    geometry.minimumZ,
  ], [
    geometry.maximumX,
    geometry.minimumZ,
  ], [
    geometry.maximumX,
    geometry.maximumZ,
  ], [
    geometry.minimumX,
    geometry.maximumZ,
  ], [
    geometry.minimumX,
    geometry.minimumZ,
  ]]]))
}

export function areaGeometryFromOlGeometry(geometry: Geometry): AreaGeometry | null {
  if (geometry instanceof CircleGeometry) {
    const [centerX, centerZ] = geometry.getCenter()
    const radius = geometry.getRadius()
    if (centerX === undefined || centerZ === undefined || !Number.isFinite(centerX)
      || !Number.isFinite(centerZ) || !Number.isFinite(radius) || radius <= 0) {
      return null
    }
    return Object.freeze({ kind: 'circle', centerX, centerZ, radius })
  }
  if (geometry instanceof Polygon) {
    const [minimumX, minimumZ, maximumX, maximumZ] = geometry.getExtent()
    if (![minimumX, minimumZ, maximumX, maximumZ].every(Number.isFinite)
      || maximumX <= minimumX || maximumZ <= minimumZ) {
      return null
    }
    return Object.freeze({ kind: 'rectangle', minimumX, minimumZ, maximumX, maximumZ })
  }
  return null
}

export function buildGrid(metadata: MapMetadata): Feature<LineString>[] {
  const extent = mapExtent(metadata)
  const width = extent[2] - extent[0]
  const height = extent[3] - extent[1]
  const targetSpacing = Math.max(width, height) / 16
  const power = 10 ** Math.floor(Math.log10(targetSpacing))
  const spacing = Math.max(1, Math.ceil(targetSpacing / power) * power)
  const features: Feature<LineString>[] = []
  for (let x = Math.ceil(extent[0] / spacing) * spacing; x <= extent[2]; x += spacing)
    features.push(new Feature(new LineString([[x, extent[1]], [x, extent[3]]])))
  for (let y = Math.ceil(extent[1] / spacing) * spacing; y <= extent[3]; y += spacing)
    features.push(new Feature(new LineString([[extent[0], y], [extent[2], y]])))
  return features
}
