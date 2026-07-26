import type { DeepReadonly, ShallowRef } from 'vue'
import type { MapMetadata } from '../api/playerMap'

import ImageTile from 'ol/ImageTile.js'
import TileLayer from 'ol/layer/Tile.js'
import XYZ from 'ol/source/XYZ.js'
import TileGrid from 'ol/tilegrid/TileGrid.js'
import { readonly, shallowRef } from 'vue'

import { createGameProjection, createTileResolutions, mapExtent } from './mapProjection'

export const AUTHENTICATED_TILE_ATTRIBUTION = 'Map tiles © The Fun Pimps LLC'

export interface TileLayerVisibility {
  isVisible: () => boolean
  subscribe: (listener: () => void) => () => void
}

export interface AuthenticatedTileLayerOptions {
  metadata: MapMetadata
  authorizationHeader: () => string | null
  fetchImpl?: typeof fetch
  createObjectURL?: (blob: Blob) => string
  revokeObjectURL?: (url: string) => void
  visibility?: TileLayerVisibility
}

export interface AuthenticatedTileLayerController {
  readonly layer: TileLayer<XYZ>
  readonly source: XYZ
  readonly enabled: DeepReadonly<ShallowRef<boolean>>
  readonly loading: DeepReadonly<ShallowRef<boolean>>
  readonly error: DeepReadonly<ShallowRef<string | null>>
  setEnabled: (enabled: boolean) => void
  reload: () => void
  retry: () => void
  dispose: () => void
}

interface ActiveTileRequest {
  readonly controller: AbortController
  objectUrl: string | null
}

const browserVisibility: TileLayerVisibility = {
  isVisible: () => document.visibilityState === 'visible',
  subscribe(listener) {
    document.addEventListener('visibilitychange', listener)
    return () => document.removeEventListener('visibilitychange', listener)
  },
}

function tilePath(worldId: string, tileCoord: readonly number[]): string {
  const [z = 0, x = 0, openLayersY = -1] = tileCoord
  const tmsY = -openLayersY - 1
  return `/api/v1/map/tiles/${encodeURIComponent(worldId)}/${z}/${x}/${tmsY}`
}

export function createAuthenticatedTileLayerController(
  options: AuthenticatedTileLayerOptions,
): AuthenticatedTileLayerController {
  const fetchImpl = options.fetchImpl ?? fetch
  const createObjectURL = options.createObjectURL ?? (blob => URL.createObjectURL(blob))
  const revokeObjectURL = options.revokeObjectURL ?? (url => URL.revokeObjectURL(url))
  const visibility = options.visibility ?? browserVisibility
  const extent = mapExtent(options.metadata)
  const tileGrid = new TileGrid({
    extent,
    origin: [extent[0], extent[3]],
    resolutions: [...createTileResolutions(options.metadata)],
    tileSize: options.metadata.tileSize,
  })
  const enabled = shallowRef(false)
  const loading = shallowRef(false)
  const error = shallowRef<string | null>(null)
  const requests = new Map<HTMLImageElement, ActiveTileRequest>()
  let activeRequests = 0
  let disposed = false

  function updateLoading() {
    loading.value = activeRequests > 0
  }

  function release(image: HTMLImageElement, abort: boolean) {
    const request = requests.get(image)
    if (request === undefined)
      return
    requests.delete(image)
    if (abort && !request.controller.signal.aborted)
      request.controller.abort()
    if (request.objectUrl !== null)
      revokeObjectURL(request.objectUrl)
  }

  function releaseAll(abort: boolean) {
    for (const image of [...requests.keys()])
      release(image, abort)
    activeRequests = 0
    updateLoading()
  }

  const source = new XYZ({
    attributions: AUTHENTICATED_TILE_ATTRIBUTION,
    projection: createGameProjection(options.metadata),
    tileGrid,
    tileUrlFunction: tileCoord => tileCoord === null ? undefined : tilePath(options.metadata.worldId, tileCoord),
    tileLoadFunction(tile, src) {
      if (!(tile instanceof ImageTile)) {
        error.value = 'Tiles could not be loaded'
        return
      }
      const image = tile.getImage()
      if (!(image instanceof HTMLImageElement)) {
        error.value = 'Tiles could not be loaded'
        return
      }
      release(image, true)
      if (disposed || !enabled.value || !visibility.isVisible())
        return
      const authorizationHeader = options.authorizationHeader()
      if (authorizationHeader === null) {
        error.value = 'Tiles could not be loaded'
        return
      }
      const controller = new AbortController()
      const request: ActiveTileRequest = { controller, objectUrl: null }
      requests.set(image, request)
      activeRequests++
      updateLoading()
      void fetchImpl(src, {
        credentials: 'omit',
        headers: { Authorization: authorizationHeader },
        signal: controller.signal,
      }).then(async (response) => {
        if (!response.ok)
          throw new Error(`HTTP ${response.status}`)
        const blob = await response.blob()
        if (requests.get(image) !== request || controller.signal.aborted)
          return
        request.objectUrl = createObjectURL(blob)
        image.src = request.objectUrl
        error.value = null
      }).catch(() => {
        if (!controller.signal.aborted && requests.get(image) === request)
          error.value = 'Tiles could not be loaded'
      }).finally(() => {
        activeRequests = Math.max(0, activeRequests - 1)
        updateLoading()
      })
    },
    wrapX: false,
  })
  const layer = new TileLayer({ source, visible: false })

  function reload() {
    if (disposed)
      return
    releaseAll(true)
    source.refresh()
  }

  function setEnabled(nextEnabled: boolean) {
    if (disposed || enabled.value === nextEnabled)
      return
    enabled.value = nextEnabled
    layer.setVisible(nextEnabled)
    error.value = null
    if (!nextEnabled) {
      releaseAll(true)
      source.refresh()
    }
    else if (visibility.isVisible()) {
      source.refresh()
    }
  }

  function retry() {
    error.value = null
    if (enabled.value && visibility.isVisible())
      reload()
  }

  const unsubscribeVisibility = visibility.subscribe(() => {
    if (!enabled.value || disposed)
      return
    if (visibility.isVisible())
      reload()
    else
      releaseAll(true)
  })

  function dispose() {
    if (disposed)
      return
    disposed = true
    unsubscribeVisibility()
    releaseAll(true)
    layer.setSource(null)
    source.dispose()
  }

  return {
    layer,
    source,
    enabled: readonly(enabled),
    loading: readonly(loading),
    error: readonly(error),
    setEnabled,
    reload,
    retry,
    dispose,
  }
}
