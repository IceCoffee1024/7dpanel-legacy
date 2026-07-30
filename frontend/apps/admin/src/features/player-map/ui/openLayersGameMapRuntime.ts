import type { Coordinate } from 'ol/coordinate.js'
import type { EventsKey } from 'ol/events.js'
import type { Extent } from 'ol/extent.js'
import type { FeatureLike } from 'ol/Feature.js'
import type { Geometry } from 'ol/geom.js'
import type { Pixel } from 'ol/pixel.js'
import type { StyleFunction } from 'ol/style/Style.js'
import type { FitOptions } from 'ol/View.js'
import type { MapMetadata, PlayerTrack } from '../api/playerMap'
import type { AreaGeometry, AreaInvestigationPlayer } from '../model/useAreaInvestigation'
import type { AuthenticatedTileLayerController } from '../model/useAuthenticatedTileLayer'
import type { MapBusinessFeature, MapVectorLayerController } from '../model/useMapVectorLayer'
import type { FitRequest, OnlineMapPlayer } from '../model/usePlayerMap'

import { defaults as defaultControls } from 'ol/control/defaults.js'
import Feature from 'ol/Feature.js'
import CircleGeometry from 'ol/geom/Circle.js'
import LineString from 'ol/geom/LineString.js'
import Point from 'ol/geom/Point.js'
import Polygon from 'ol/geom/Polygon.js'
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
import View from 'ol/View.js'

import { fetchCurrentMapLayer } from '../api/mapLayerAdapter'
import { createLocalBackgroundLayer } from '../model/mapBackground'
import { createTrackFeatures } from '../model/mapFeatures'
import { createGameProjection, fromMapCoordinate, mapExtent, toMapCoordinate } from '../model/mapProjection'
import { createAuthenticatedTileLayerController } from '../model/useAuthenticatedTileLayer'
import { createMapVectorLayerController } from '../model/useMapVectorLayer'

export interface GameMapCoordinate {
  readonly x: number
  readonly z: number
}

export type AreaInteractionMode = 'draw-rectangle' | 'draw-circle' | 'modify' | null

interface FeaturePixelMap {
  forEachFeatureAtPixel: (pixel: Pixel, callback: (feature: FeatureLike) => boolean) => unknown
}

export interface MapSelectionCallbacks {
  onSelectedCoordinate: (coordinate: GameMapCoordinate) => void
  onSelectOnlinePlayer: (combinedId: string) => void
  onSelectObservation: (snapshotId: number) => void
  onSelectBusinessFeature?: (feature: MapBusinessFeature) => void
}

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

interface FitView {
  fit: (extent: Extent, options?: FitOptions) => void
}

interface DisposableMap {
  setTarget: (target: undefined) => void
  getLayers: () => { clear: () => void }
  dispose: () => void
}

interface DisposableSource {
  clear: (fast: boolean) => void
}

interface DisposableLayer {
  setSource: (source: null) => void
}

export interface GameMapResources {
  map: DisposableMap
  eventKeys: unknown[]
  sources: DisposableSource[]
  layers: DisposableLayer[]
  unlisten: (keys: unknown[]) => void
}

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

export function applyFitOnce(view: FitView, request: FitRequest | null, previousQueryKey: string | null): string | null {
  if (request === null || request.queryKey === previousQueryKey)
    return previousQueryKey
  view.fit([...request.extent], { padding: [48, 48, 48, 48], duration: 0 })
  return request.queryKey
}

export function disposeGameMapResources(resources: GameMapResources) {
  resources.unlisten(resources.eventKeys)
  for (const source of resources.sources)
    source.clear(true)
  for (const layer of resources.layers)
    layer.setSource(null)
  resources.map.setTarget(undefined)
  resources.map.getLayers().clear()
  resources.map.dispose()
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

function buildGrid(metadata: MapMetadata): Feature<LineString>[] {
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
