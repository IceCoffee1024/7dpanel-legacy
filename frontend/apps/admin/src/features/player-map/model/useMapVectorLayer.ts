import type { DeepReadonly, ShallowRef } from 'vue'
import type {
  MapBusinessFeature,
  MapFeatureKind,
  MapLayerId,
  MapLayerQuery,
  MapVectorLayerResponse,
} from './mapVectorLayerAdapter'

import Feature from 'ol/Feature.js'
import Point from 'ol/geom/Point.js'
import VectorLayer from 'ol/layer/Vector.js'
import VectorSource from 'ol/source/Vector.js'
import CircleStyle from 'ol/style/Circle.js'
import Fill from 'ol/style/Fill.js'
import Stroke from 'ol/style/Stroke.js'
import Style from 'ol/style/Style.js'
import { readonly, shallowRef } from 'vue'

import { fetchMapVectorLayer } from './mapVectorLayerAdapter'

export type {
  ClaimMapFeature,
  DroneMapFeature,
  HistoricalPlayerMapFeature,
  MapBusinessFeature,
  MapFeatureKind,
  MapLayerId,
  MapLayerQuery,
  MapVectorLayerResponse,
  TraderMapFeature,
  TransientEntityMapFeature,
  VehicleMapFeature,
} from './mapVectorLayerAdapter'
export {
  fetchMapVectorLayer,
  MAP_LAYER_IDS,
  mapLayerPath,
  parseMapVectorLayerResponse,
} from './mapVectorLayerAdapter'

export type MapVectorLayerState = 'off' | 'paused' | 'zoom-required' | 'loading' | 'ready' | 'empty' | 'stale' | 'failed'

export interface MapLayerVisibility {
  isVisible: () => boolean
  subscribe: (listener: () => void) => () => void
}

type MapLayerRequest = (
  authorizationHeader: string,
  query: MapLayerQuery,
  signal: AbortSignal,
) => Promise<MapVectorLayerResponse>

export interface MapVectorLayerControllerOptions {
  readonly layerId: MapLayerId
  readonly minimumZoom: number
  readonly authorizationHeader: () => string | null
  readonly request?: MapLayerRequest
  readonly visibility?: MapLayerVisibility
}

export interface MapVectorLayerController {
  readonly layerId: MapLayerId
  readonly minimumZoom: number
  readonly layer: VectorLayer<VectorSource<Feature<Point>>>
  readonly source: VectorSource<Feature<Point>>
  readonly enabled: DeepReadonly<ShallowRef<boolean>>
  readonly state: DeepReadonly<ShallowRef<MapVectorLayerState>>
  readonly count: DeepReadonly<ShallowRef<number | null>>
  readonly error: DeepReadonly<ShallowRef<string | null>>
  readonly items: DeepReadonly<ShallowRef<readonly MapBusinessFeature[]>>
  updateView: (query: MapLayerQuery) => void
  setEnabled: (enabled: boolean) => void
  refresh: () => Promise<void>
  retry: () => void
  dispose: () => void
}

const browserVisibility: MapLayerVisibility = {
  isVisible: () => document.visibilityState === 'visible',
  subscribe(listener) {
    document.addEventListener('visibilitychange', listener)
    return () => document.removeEventListener('visibilitychange', listener)
  },
}

const layerStyles: Record<MapFeatureKind, Style> = {
  'historical-player': markerStyle('#60a5fa'),
  'trader': markerStyle('#f59e0b'),
  'claim': markerStyle('#a78bfa'),
  'vehicle': markerStyle('#f97316'),
  'drone': markerStyle('#22d3ee'),
  'animal': markerStyle('#84cc16'),
  'hostile': markerStyle('#ef4444'),
}

function markerStyle(color: string): Style {
  return new Style({
    image: new CircleStyle({
      radius: 7,
      fill: new Fill({ color }),
      stroke: new Stroke({ color: '#18181b', width: 2 }),
    }),
  })
}

export function createMapVectorLayerController(options: MapVectorLayerControllerOptions): MapVectorLayerController {
  const visibility = options.visibility ?? browserVisibility
  const request = options.request ?? ((authorization, query, signal) =>
    fetchMapVectorLayer(options.layerId, authorization, query, signal))
  const source = new VectorSource<Feature<Point>>()
  const layer = new VectorLayer({
    source,
    style: feature => layerStyles[(feature.get('businessFeature') as MapBusinessFeature).kind],
    visible: false,
  })
  const enabled = shallowRef(false)
  const state = shallowRef<MapVectorLayerState>('off')
  const count = shallowRef<number | null>(null)
  const error = shallowRef<string | null>(null)
  const items = shallowRef<readonly MapBusinessFeature[]>(Object.freeze([]))
  let query: MapLayerQuery | null = null
  let activeController: AbortController | null = null
  let sequence = 0
  let disposed = false

  function abortActive() {
    sequence++
    activeController?.abort()
    activeController = null
  }

  function updateFeatures(nextItems: readonly MapBusinessFeature[]) {
    source.clear(true)
    source.addFeatures(nextItems.map(item => new Feature({
      geometry: new Point([item.x, item.z]),
      role: 'business-feature',
      businessFeature: item,
    })))
  }

  async function refresh(): Promise<void> {
    if (disposed || !enabled.value || query === null)
      return
    abortActive()
    if (!visibility.isVisible()) {
      state.value = 'paused'
      return
    }
    if (query.zoom < options.minimumZoom) {
      state.value = 'zoom-required'
      return
    }
    const authorization = options.authorizationHeader()
    if (authorization === null) {
      error.value = 'Layer could not be loaded'
      state.value = count.value === null ? 'failed' : 'stale'
      return
    }
    const controller = new AbortController()
    activeController = controller
    const requestSequence = ++sequence
    state.value = 'loading'
    error.value = null
    try {
      const response = await request(authorization, query, controller.signal)
      if (disposed || controller.signal.aborted || requestSequence !== sequence)
        return
      items.value = Object.freeze([...response.items])
      count.value = response.items.length
      updateFeatures(response.items)
      state.value = response.items.length === 0 ? 'empty' : 'ready'
    }
    catch {
      if (disposed || controller.signal.aborted || requestSequence !== sequence)
        return
      error.value = 'Layer could not be loaded'
      state.value = count.value === null ? 'failed' : 'stale'
    }
    finally {
      if (activeController === controller)
        activeController = null
    }
  }

  function updateView(nextQuery: MapLayerQuery) {
    query = Object.freeze({ ...nextQuery, extent: Object.freeze([...nextQuery.extent]) as MapLayerQuery['extent'] })
    if (enabled.value)
      void refresh()
  }

  function setEnabled(nextEnabled: boolean) {
    if (disposed || enabled.value === nextEnabled)
      return
    enabled.value = nextEnabled
    layer.setVisible(nextEnabled)
    error.value = null
    if (!nextEnabled) {
      abortActive()
      state.value = 'off'
      return
    }
    void refresh()
  }

  function retry() {
    error.value = null
    void refresh()
  }

  const unsubscribeVisibility = visibility.subscribe(() => {
    if (disposed || !enabled.value)
      return
    if (visibility.isVisible()) {
      void refresh()
    }
    else {
      abortActive()
      state.value = 'paused'
    }
  })

  function dispose() {
    if (disposed)
      return
    disposed = true
    abortActive()
    unsubscribeVisibility()
    source.clear(true)
    layer.setSource(null)
    source.dispose()
  }

  return {
    layerId: options.layerId,
    minimumZoom: options.minimumZoom,
    layer,
    source,
    enabled: readonly(enabled),
    state: readonly(state),
    count: readonly(count),
    error: readonly(error),
    items: readonly(items),
    updateView,
    setEnabled,
    refresh,
    retry,
    dispose,
  }
}
