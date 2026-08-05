import type { Coordinate } from 'ol/coordinate.js'
import type { FeatureLike } from 'ol/Feature.js'
import type { Pixel } from 'ol/pixel.js'
import type { MapBusinessFeature } from '../model/useMapVectorLayer'
import type { OnlineMapPlayer } from '../model/usePlayerMap'

import { fromMapCoordinate } from '../model/mapProjection'

interface FeaturePixelMap {
  forEachFeatureAtPixel: (pixel: Pixel, callback: (feature: FeatureLike) => boolean) => unknown
}

export interface MapSelectionCallbacks {
  onSelectedCoordinate: (coordinate: { readonly x: number, readonly z: number }) => void
  onSelectOnlinePlayer: (combinedId: string) => void
  onSelectObservation: (snapshotId: number) => void
  onSelectBusinessFeature?: (feature: MapBusinessFeature) => void
}

export function handleMapSingleClick(
  map: FeaturePixelMap,
  pixel: Pixel,
  coordinate: Coordinate,
  callbacks: MapSelectionCallbacks,
) {
  callbacks.onSelectedCoordinate(fromMapCoordinate(coordinate))
  map.forEachFeatureAtPixel(pixel, (feature) => {
    if (feature.get('role') === 'online-player') {
      const player = feature.get('player') as OnlineMapPlayer | undefined
      if (player !== undefined)
        callbacks.onSelectOnlinePlayer(player.combinedId)
      return true
    }
    if (feature.get('role') === 'business-feature') {
      const businessFeature = feature.get('businessFeature') as MapBusinessFeature | undefined
      if (businessFeature !== undefined)
        callbacks.onSelectBusinessFeature?.(businessFeature)
      return true
    }
    const snapshotId = feature.get('snapshotId')
    if (typeof snapshotId === 'number')
      callbacks.onSelectObservation(snapshotId)
    return true
  })
}
