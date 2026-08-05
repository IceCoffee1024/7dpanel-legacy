import type { Extent } from 'ol/extent.js'
import type { FitOptions } from 'ol/View.js'
import type { FitRequest } from '../model/usePlayerMap'

interface FitView {
  fit: (extent: Extent, options?: FitOptions) => void
}

export function applyFitOnce(view: FitView, request: FitRequest | null, previousQueryKey: string | null): string | null {
  if (request === null || request.queryKey === previousQueryKey)
    return previousQueryKey
  view.fit([...request.extent], { padding: [48, 48, 48, 48], duration: 0 })
  return request.queryKey
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
