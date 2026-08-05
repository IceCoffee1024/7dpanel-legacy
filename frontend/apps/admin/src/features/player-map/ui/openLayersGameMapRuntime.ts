import type { EventsKey } from 'ol/events.js'
import type { Geometry } from 'ol/geom.js'
import type { StyleFunction } from 'ol/style/Style.js'
import type { MapMetadata, PlayerTrack } from '../api/playerMap'
import type { AreaGeometry, AreaInvestigationPlayer } from '../model/useAreaInvestigation'
import type { AuthenticatedTileLayerController } from '../model/useAuthenticatedTileLayer'
import type { MapBusinessFeature, MapVectorLayerController } from '../model/useMapVectorLayer'
import type { FitRequest, OnlineMapPlayer } from '../model/usePlayerMap'
import type { MapSelectionCallbacks } from './openLayersGameMapInteraction'

import { defaults as defaultControls } from 'ol/control/defaults.js'
import Feature from 'ol/Feature.js'
import Point from 'ol/geom/Point.js'
import { defaults as defaultInteractions } from 'ol/interaction/defaults.js'
import Draw, { createBox } from 'ol/interaction/Draw.js'
import Modify from 'ol/interaction/Modify.js'
import VectorLayer from 'ol/layer/Vector.js'
import OlMap from 'ol/Map.js'
import { unByKey } from 'ol/Observable.js'
import VectorSource from 'ol/source/Vector.js'
import CircleStyle from 'ol/style/Circle.js'
import Fill from 'ol/style/Fill.js'
import Stroke from 'ol/style/Stroke.js'
import Style from 'ol/style/Style.js'

import { fetchCurrentMapLayer } from '../api/mapLayerAdapter'
import { createLocalBackgroundLayer } from '../model/mapBackground'
import { createTrackFeatures } from '../model/mapFeatures'
import { fromMapCoordinate, toMapCoordinate } from '../model/mapProjection'
import { createAuthenticatedTileLayerController } from '../model/useAuthenticatedTileLayer'
import { createMapVectorLayerController } from '../model/useMapVectorLayer'
import { handleMapSingleClick } from './openLayersGameMapInteraction'
import {
  applyFitOnce,
  disposeGameMapResources,
} from './openLayersGameMapLifecycle'
import {
  areaGeometryFromOlGeometry,
  buildGrid,
  createAreaFeature,
  createGameView,
} from './openLayersGameMapProjection'

export type { MapSelectionCallbacks } from './openLayersGameMapInteraction'
export type { GameMapResources } from './openLayersGameMapLifecycle'
export {
  applyFitOnce,
  areaGeometryFromOlGeometry,
  createAreaFeature,
  createGameView,
  disposeGameMapResources,
  handleMapSingleClick,
}

export interface GameMapCoordinate {
  readonly x: number
  readonly z: number
}

export type AreaInteractionMode = 'draw-rectangle' | 'draw-circle' | 'modify' | null

export interface CreateOpenLayersGameMapRuntimeOptions extends MapSelectionCallbacks {
  target: HTMLElement
  metadata: MapMetadata
  onlinePlayers: readonly OnlineMapPlayer[]
  track: PlayerTrack | null
  selectedSnapshotId: number | null
  selectedOnlineCombinedId: string | null
  fitRequest: FitRequest | null
  areaGeometry: AreaGeometry | null
  areaInteractionMode: AreaInteractionMode
  selectedAreaPlayer: AreaInvestigationPlayer | null
  authorizationHeader: () => string | null
  onPointerCoordinate: (coordinate: GameMapCoordinate) => void
  onAreaGeometryChange: (geometry: AreaGeometry) => void
}

export interface MapLayersRuntime {
  readonly tile: AuthenticatedTileLayerController
  readonly vectors: readonly MapVectorLayerController[]
}

export interface OpenLayersGameMapRuntime {
  readonly map: OlMap
  readonly layers: MapLayersRuntime
  updateOnlinePlayers: (players: readonly OnlineMapPlayer[]) => void
  updateTrack: (track: PlayerTrack | null) => void
  updateSelection: (snapshotId: number | null, onlineCombinedId: string | null) => void
  updateBusinessSelection: (feature: MapBusinessFeature | null) => void
  updateAreaGeometry: (geometry: AreaGeometry | null) => void
  updateAreaInteractionMode: (mode: AreaInteractionMode) => void
  updateAreaResultSelection: (player: AreaInvestigationPlayer | null) => void
  applyFit: (request: FitRequest | null) => void
  dispose: () => void
}

const observationStyle = new Style({
  image: new CircleStyle({ radius: 4, fill: new Fill({ color: '#e4e4e7' }), stroke: new Stroke({ color: '#27272a', width: 1 }) }),
})
const selectedObservationStyle = new Style({
  image: new CircleStyle({ radius: 7, fill: new Fill({ color: '#facc15' }), stroke: new Stroke({ color: '#713f12', width: 2 }) }),
})
const trackLineStyle = new Style({ stroke: new Stroke({ color: '#60a5fa', width: 3 }) })
const startStyle = new Style({
  image: new CircleStyle({ radius: 8, fill: new Fill({ color: '#22c55e' }), stroke: new Stroke({ color: '#ffffff', width: 2 }) }),
})
const endStyle = new Style({
  image: new CircleStyle({ radius: 8, fill: new Fill({ color: '#ef4444' }), stroke: new Stroke({ color: '#ffffff', width: 2 }) }),
})
const onlineStyle = new Style({
  image: new CircleStyle({ radius: 7, fill: new Fill({ color: '#22c55e' }), stroke: new Stroke({ color: '#052e16', width: 2 }) }),
})
const selectedOnlineStyle = new Style({
  image: new CircleStyle({ radius: 10, fill: new Fill({ color: '#4ade80' }), stroke: new Stroke({ color: '#facc15', width: 3 }) }),
})
const selectedBusinessStyle = new Style({
  image: new CircleStyle({ radius: 12, fill: new Fill({ color: 'rgba(250, 204, 21, .18)' }), stroke: new Stroke({ color: '#facc15', width: 3 }) }),
})
const areaStyle = new Style({
  fill: new Fill({ color: 'rgba(59, 130, 246, .12)' }),
  stroke: new Stroke({ color: '#3b82f6', width: 2, lineDash: [8, 5] }),
})
const selectedAreaResultStyle = new Style({
  image: new CircleStyle({ radius: 10, fill: new Fill({ color: '#facc15' }), stroke: new Stroke({ color: '#713f12', width: 3 }) }),
})
const gridStyle = new Style({ stroke: new Stroke({ color: 'rgba(228, 228, 231, .2)', width: 1 }) })

export function createOpenLayersGameMapRuntime(options: CreateOpenLayersGameMapRuntimeOptions): OpenLayersGameMapRuntime {
  const backgroundLayer = createLocalBackgroundLayer(options.metadata)
  const tile = createAuthenticatedTileLayerController({
    metadata: options.metadata,
    authorizationHeader: options.authorizationHeader,
  })
  const vectorDefinitions = [
    ['historical-player-locations', 1],
    ['traders', 1],
    ['claims', 2],
    ['vehicles', 3],
    ['drones', 3],
    ['animals', 3],
    ['hostiles', 3],
  ] as const
  const vectors = vectorDefinitions.map(([layerId, minimumZoom]) => createMapVectorLayerController({
    layerId,
    minimumZoom,
    authorizationHeader: options.authorizationHeader,
    request: (authorizationHeader, query, signal) => fetchCurrentMapLayer(
      layerId,
      authorizationHeader,
      query,
      signal,
    ),
  }))
  const gridSource = new VectorSource({ features: buildGrid(options.metadata) })
  const onlineSource = new VectorSource<Feature<Point>>()
  const trackSource = new VectorSource<Feature<Geometry>>()
  const selectedBusinessSource = new VectorSource<Feature<Point>>()
  const areaSource = new VectorSource<Feature<Geometry>>()
  const selectedAreaResultSource = new VectorSource<Feature<Point>>()
  let selectedSnapshotId = options.selectedSnapshotId
  let selectedOnlineCombinedId = options.selectedOnlineCombinedId
  let lastFitQueryKey: string | null = null
  const trackStyle: StyleFunction = (feature) => {
    if (feature.get('role') === 'track')
      return trackLineStyle
    if (feature.get('role') === 'start')
      return startStyle
    if (feature.get('role') === 'end')
      return endStyle
    return feature.get('snapshotId') === selectedSnapshotId ? selectedObservationStyle : observationStyle
  }
  const onlineFeatureStyle: StyleFunction = feature =>
    (feature.get('player') as OnlineMapPlayer | undefined)?.combinedId === selectedOnlineCombinedId
      ? selectedOnlineStyle
      : onlineStyle
  const gridLayer = new VectorLayer({ source: gridSource, style: gridStyle })
  const onlineLayer = new VectorLayer({ source: onlineSource, style: onlineFeatureStyle })
  const trackLayer = new VectorLayer({ source: trackSource, style: trackStyle })
  const selectedBusinessLayer = new VectorLayer({ source: selectedBusinessSource, style: selectedBusinessStyle })
  const areaLayer = new VectorLayer({ source: areaSource, style: areaStyle })
  const selectedAreaResultLayer = new VectorLayer({ source: selectedAreaResultSource, style: selectedAreaResultStyle })
  const view = createGameView(options.metadata)
  const map = new OlMap({
    target: options.target,
    layers: [
      backgroundLayer,
      tile.layer,
      gridLayer,
      ...vectors.map(controller => controller.layer),
      areaLayer,
      onlineLayer,
      trackLayer,
      selectedBusinessLayer,
      selectedAreaResultLayer,
    ],
    view,
    controls: defaultControls({ rotate: false }),
    interactions: defaultInteractions({ altShiftDragRotate: false, pinchRotate: false }),
  })
  const eventKeys: EventsKey[] = [
    map.on('pointermove', event => options.onPointerCoordinate(fromMapCoordinate(event.coordinate))),
    map.on('singleclick', event => handleMapSingleClick(map, event.pixel, event.coordinate, options)),
    map.on('moveend', updateLayerView),
  ]
  let areaInteraction: Draw | Modify | null = null
  let areaInteractionEventKeys: EventsKey[] = []

  function removeAreaInteraction() {
    if (areaInteraction === null)
      return
    unByKey(areaInteractionEventKeys)
    areaInteractionEventKeys = []
    map.removeInteraction(areaInteraction)
    areaInteraction.dispose()
    areaInteraction = null
  }

  function publishAreaGeometry(feature: Feature<Geometry>) {
    const geometry = feature.getGeometry()
    if (geometry !== undefined) {
      const next = areaGeometryFromOlGeometry(geometry)
      if (next !== null)
        options.onAreaGeometryChange(next)
    }
  }

  function updateAreaGeometry(geometry: AreaGeometry | null) {
    areaSource.clear(true)
    if (geometry !== null)
      areaSource.addFeature(createAreaFeature(geometry))
  }

  function updateAreaInteractionMode(mode: AreaInteractionMode) {
    removeAreaInteraction()
    if (mode === null)
      return
    if (mode === 'modify') {
      if (areaSource.isEmpty())
        return
      const interaction = new Modify({ source: areaSource })
      areaInteractionEventKeys = [interaction.on('modifyend', (event) => {
        const feature = event.features.item(0)
        if (feature !== undefined)
          publishAreaGeometry(feature as Feature<Geometry>)
      })]
      areaInteraction = interaction
    }
    else {
      const interaction = new Draw({
        source: areaSource,
        type: 'Circle',
        geometryFunction: mode === 'draw-rectangle' ? createBox() : undefined,
      })
      areaInteractionEventKeys = [
        interaction.on('drawstart', () => areaSource.clear(true)),
        interaction.on('drawend', event => publishAreaGeometry(event.feature as Feature<Geometry>)),
      ]
      areaInteraction = interaction
    }
    map.addInteraction(areaInteraction)
  }

  function updateAreaResultSelection(player: AreaInvestigationPlayer | null) {
    selectedAreaResultSource.clear(true)
    if (player !== null) {
      selectedAreaResultSource.addFeature(new Feature({
        geometry: new Point(toMapCoordinate(player.lastMatchingObservation.position)),
        role: 'selected-area-result',
      }))
    }
  }

  function updateLayerView() {
    const size = map.getSize()
    if (size === undefined)
      return
    const extent = view.calculateExtent(size)
    const zoom = Math.max(0, Math.floor(view.getZoom() ?? 0))
    for (const controller of vectors) {
      controller.updateView({
        worldId: options.metadata.worldId,
        extent: [extent[0], extent[1], extent[2], extent[3]],
        zoom,
        limit: 500,
      })
    }
  }

  function updateOnlinePlayers(players: readonly OnlineMapPlayer[]) {
    onlineSource.clear(true)
    onlineSource.addFeatures(players.map(player => new Feature({
      geometry: new Point(toMapCoordinate(player.position)),
      role: 'online-player',
      player,
    })))
  }

  function updateTrack(track: PlayerTrack | null) {
    trackSource.clear(true)
    if (track !== null)
      trackSource.addFeatures(createTrackFeatures(track))
  }

  function updateSelection(nextSnapshotId: number | null, nextOnlineCombinedId: string | null) {
    selectedSnapshotId = nextSnapshotId
    selectedOnlineCombinedId = nextOnlineCombinedId
    trackSource.changed()
    onlineSource.changed()
  }

  function updateBusinessSelection(feature: MapBusinessFeature | null) {
    selectedBusinessSource.clear(true)
    if (feature !== null) {
      selectedBusinessSource.addFeature(new Feature({
        geometry: new Point([feature.x, feature.z]),
        role: 'selected-business-feature',
      }))
    }
  }

  function applyFit(request: FitRequest | null) {
    lastFitQueryKey = applyFitOnce(view, request, lastFitQueryKey)
  }

  function dispose() {
    removeAreaInteraction()
    tile.dispose()
    for (const controller of vectors)
      controller.dispose()
    disposeGameMapResources({
      map,
      eventKeys,
      sources: [onlineSource, trackSource, gridSource, selectedBusinessSource, areaSource, selectedAreaResultSource],
      layers: [backgroundLayer, gridLayer, onlineLayer, trackLayer, selectedBusinessLayer, areaLayer, selectedAreaResultLayer],
      unlisten: keys => unByKey(keys as EventsKey[]),
    })
  }

  updateOnlinePlayers(options.onlinePlayers)
  updateTrack(options.track)
  updateAreaGeometry(options.areaGeometry)
  updateAreaInteractionMode(options.areaInteractionMode)
  updateAreaResultSelection(options.selectedAreaPlayer)
  updateLayerView()
  applyFit(options.fitRequest)
  return {
    map,
    layers: { tile, vectors: Object.freeze(vectors) },
    updateOnlinePlayers,
    updateTrack,
    updateSelection,
    updateBusinessSelection,
    updateAreaGeometry,
    updateAreaInteractionMode,
    updateAreaResultSelection,
    applyFit,
    dispose,
  }
}
