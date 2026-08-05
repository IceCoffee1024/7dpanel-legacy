import type { Router } from 'vue-router'
import type { AreaGeometry, AreaInvestigationController } from '../model/useAreaInvestigation'
import type { MapBusinessFeature, MapLayerId } from '../model/useMapVectorLayer'
import type { PlayerMapController } from '../model/usePlayerMap'
import type { AreaInteractionMode, MapLayersRuntime } from './openLayersGameMapRuntime'

import { computed, onUnmounted, shallowRef, watch } from 'vue'

import { playerMapWorldIdentity } from '../model/playerMapProjection'

export interface PlayerMapViewModelOptions {
  controller: PlayerMapController
  investigation: AreaInvestigationController
  router: Router
  authorizationHeader: () => string | null
}

export function usePlayerMapViewModel(options: PlayerMapViewModelOptions) {
  const { controller, investigation, router } = options
  const selectedPlayer = shallowRef(controller.filters.value.player ?? '')
  const fromUtc = shallowRef(controller.filters.value.fromUtc ?? '')
  const toUtc = shallowRef(controller.filters.value.toUtc ?? '')
  const selectedOnlineCombinedId = shallowRef<string | null>(null)
  const selectedMapFeature = shallowRef<MapBusinessFeature | null>(null)
  const layerRuntime = shallowRef<MapLayersRuntime | null>(null)
  const areaMode = shallowRef<AreaGeometry['kind']>(investigation.geometry.value?.kind ?? 'rectangle')
  const areaInteractionMode = shallowRef<AreaInteractionMode>(null)

  const tilePanelState = computed(() => {
    const tile = layerRuntime.value?.tile
    return tile === undefined
      ? null
      : {
          enabled: tile.enabled.value,
          loading: tile.loading.value,
          error: tile.error.value,
        }
  })
  const vectorPanelStates = computed(() =>
    layerRuntime.value?.vectors.map(layer => ({
      id: layer.layerId,
      minimumZoom: layer.minimumZoom,
      enabled: layer.enabled.value,
      state: layer.state.value,
      count: layer.count.value,
      error: layer.error.value,
      items: layer.items.value,
    })) ?? [],
  )
  const playerOptions = computed(() => controller.historicalPlayers.value.map(player => ({
    label: `${player.latestName} · ${player.crossplatformId}`,
    value: player.crossplatformId,
  })))
  const observations = computed(() => controller.track.value?.segments.flatMap(segment => segment.points) ?? [])
  const selectedObservationIndex = computed(() => observations.value.findIndex(
    point => point.snapshotId === controller.selectedSnapshotId.value,
  ))
  const selectedObservation = computed(() => selectedObservationIndex.value < 0
    ? null
    : (observations.value[selectedObservationIndex.value] ?? null))

  watch(controller.filters, (value) => {
    selectedPlayer.value = value.player ?? ''
    fromUtc.value = value.fromUtc ?? ''
    toUtc.value = value.toUtc ?? ''
  })
  watch(controller.onlinePlayers, (players) => {
    if (selectedOnlineCombinedId.value !== null
      && !players.some(player => player.combinedId === selectedOnlineCombinedId.value)) {
      selectedOnlineCombinedId.value = null
    }
  })
  watch(investigation.geometry, (geometry) => {
    if (geometry === null)
      areaInteractionMode.value = null
    else
      areaMode.value = geometry.kind
  })
  watch(
    () => controller.metadata.value === null ? null : playerMapWorldIdentity(controller.metadata.value),
    (identity, previousIdentity) => {
      if (previousIdentity !== null && identity !== previousIdentity) {
        selectedOnlineCombinedId.value = null
        selectedMapFeature.value = null
        areaInteractionMode.value = null
        investigation.clear()
      }
    },
  )

  function setLayerRuntime(runtime: MapLayersRuntime | null) {
    layerRuntime.value = runtime
  }

  function vectorLayer(layerId: MapLayerId) {
    return layerRuntime.value?.vectors.find(layer => layer.layerId === layerId)
  }

  function featureLayerId(feature: MapBusinessFeature): MapLayerId {
    switch (feature.kind) {
      case 'historical-player': return 'historical-player-locations'
      case 'trader': return 'traders'
      case 'claim': return 'claims'
      case 'vehicle': return 'vehicles'
      case 'drone': return 'drones'
      case 'animal': return 'animals'
      case 'hostile': return 'hostiles'
    }
  }

  function toggleLayer(layerId: MapLayerId, enabled: boolean) {
    vectorLayer(layerId)?.setEnabled(enabled)
    if (!enabled && selectedMapFeature.value !== null && featureLayerId(selectedMapFeature.value) === layerId)
      selectedMapFeature.value = null
  }

  function drawArea(mode: AreaGeometry['kind']) {
    areaMode.value = mode
    areaInteractionMode.value = mode === 'rectangle' ? 'draw-rectangle' : 'draw-circle'
  }

  function modifyArea(geometry: AreaGeometry) {
    areaMode.value = geometry.kind
    areaInteractionMode.value = 'modify'
  }

  function updateAreaGeometry(geometry: AreaGeometry) {
    areaInteractionMode.value = null
    if (geometry.kind === 'rectangle') {
      investigation.setRectangle(geometry.minimumX, geometry.minimumZ, geometry.maximumX, geometry.maximumZ)
    }
    else {
      investigation.setCircle(geometry.centerX, geometry.centerZ, geometry.radius)
    }
  }

  function clearAreaInteraction() {
    areaInteractionMode.value = null
  }

  function setAreaLimit(limit: number) {
    investigation.setLimit(limit)
  }

  function selectAreaResult(combinedId: string) {
    investigation.selectResult(combinedId)
  }

  function openHistoryProfile(combinedId: string) {
    void router.push(`/players/history/${encodeURIComponent(combinedId)}`)
  }

  async function applyTrackFilters() {
    controller.setPlayer(selectedPlayer.value || null)
    controller.setRange(fromUtc.value || null, toUtc.value || null)
    await controller.refreshTrack()
  }

  async function loadAreaResultTrack(combinedId: string) {
    const range = investigation.timeRange.value
    if (range === null)
      return
    selectedPlayer.value = combinedId
    fromUtc.value = range.fromUtc
    toUtc.value = range.toUtc
    await applyTrackFilters()
  }

  function selectObservationAtIndex(event: Event) {
    const index = Number((event.currentTarget as HTMLInputElement).value)
    const observation = observations.value[index]
    if (observation !== undefined)
      controller.selectObservation(observation.snapshotId)
  }

  function searchPlayers(event: Event) {
    void controller.searchHistoricalPlayers((event.currentTarget as HTMLInputElement).value)
  }

  function dispose() {
    investigation.cancel()
  }

  onUnmounted(dispose)

  return {
    selectedPlayer,
    fromUtc,
    toUtc,
    selectedOnlineCombinedId,
    selectedMapFeature,
    layerRuntime,
    areaMode,
    areaInteractionMode,
    tilePanelState,
    vectorPanelStates,
    playerOptions,
    observations,
    selectedObservationIndex,
    selectedObservation,
    setLayerRuntime,
    vectorLayer,
    toggleLayer,
    drawArea,
    modifyArea,
    updateAreaGeometry,
    clearAreaInteraction,
    setAreaLimit,
    selectAreaResult,
    openHistoryProfile,
    loadAreaResultTrack,
    searchPlayers,
    selectObservationAtIndex,
    applyTrackFilters,
    authorizationHeader: options.authorizationHeader,
  }
}
