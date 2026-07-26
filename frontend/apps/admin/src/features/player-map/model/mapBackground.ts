import type { MapMetadata } from '../api/playerMap'

import ImageLayer from 'ol/layer/Image.js'
import ImageStatic from 'ol/source/ImageStatic.js'

import backgroundUrl from '../../../assets/images/map-background.svg'
import { createGameProjection, mapExtent } from './mapProjection'

export function createLocalBackgroundLayer(metadata: MapMetadata): ImageLayer<ImageStatic> {
  const projection = createGameProjection(metadata)
  return new ImageLayer({
    visible: true,
    source: new ImageStatic({
      url: backgroundUrl,
      projection,
      imageExtent: mapExtent(metadata),
      interpolate: true,
    }),
  })
}
