<script setup lang="ts">
import type OlMap from 'ol/Map.js'
import type { MapMetadata, PlayerTrack } from '../api/playerMap'
import type { AreaGeometry, AreaInvestigationPlayer } from '../model/useAreaInvestigation'
import type { MapBusinessFeature } from '../model/useMapVectorLayer'
import type { FitRequest, OnlineMapPlayer } from '../model/usePlayerMap'

import type { AreaInteractionMode, GameMapCoordinate, MapLayersRuntime, OpenLayersGameMapRuntime } from './openLayersGameMapRuntime'

import { onMounted, onUnmounted, shallowRef, useTemplateRef, watch } from 'vue'
import { createOpenLayersGameMapRuntime } from './openLayersGameMapRuntime'
import 'ol/ol.css'

const props = defineProps<{
  metadata: MapMetadata
  onlinePlayers: readonly OnlineMapPlayer[]
  track: PlayerTrack | null
  selectedSnapshotId: number | null
  selectedOnlineCombinedId: string | null
  fitRequest: FitRequest | null
  authorizationHeader?: () => string | null
  selectedBusinessFeature?: MapBusinessFeature | null
  areaGeometry?: AreaGeometry | null
  areaInteractionMode?: AreaInteractionMode
  selectedAreaPlayer?: AreaInvestigationPlayer | null
}>()

const emit = defineEmits<{
  selectObservation: [snapshotId: number]
  selectOnlinePlayer: [combinedId: string]
  selectBusinessFeature: [feature: MapBusinessFeature]
  layersReady: [layers: MapLayersRuntime | null]
  updateAreaGeometry: [geometry: AreaGeometry]
}>()

const target = useTemplateRef<HTMLDivElement>('target')
const map = shallowRef<OlMap | null>(null)
const runtime = shallowRef<OpenLayersGameMapRuntime | null>(null)
const pointerCoordinate = shallowRef<GameMapCoordinate | null>(null)
const selectedCoordinate = shallowRef<GameMapCoordinate | null>(null)
let resizeObserver: ResizeObserver | null = null
let activeWorldIdentity: string | null = null

function worldIdentity(metadata: MapMetadata): string {
  return JSON.stringify({
    worldId: metadata.worldId,
    extent: metadata.extent,
  })
}

function runtimeSignature(metadata: MapMetadata): string {
  return JSON.stringify({
    worldId: metadata.worldId,
    extent: metadata.extent,
    axes: metadata.axes,
    availableZoomLevels: metadata.availableZoomLevels,
    tileSize: metadata.tileSize,
    mapResourceVersion: metadata.mapResourceVersion,
  })
}

function disposeRuntime() {
  resizeObserver?.disconnect()
  resizeObserver = null
  runtime.value?.dispose()
  runtime.value = null
  map.value = null
  emit('layersReady', null)
}

function rebuildRuntime(resetWorldBoundState = false) {
  if (target.value === null)
    return
  const isReplacement = runtime.value !== null
  if (isReplacement && resetWorldBoundState) {
    pointerCoordinate.value = null
    selectedCoordinate.value = null
  }
  disposeRuntime()
  const nextRuntime = createOpenLayersGameMapRuntime({
    target: target.value,
    metadata: props.metadata,
    onlinePlayers: props.onlinePlayers,
    track: resetWorldBoundState ? null : props.track,
    selectedSnapshotId: resetWorldBoundState ? null : props.selectedSnapshotId,
    selectedOnlineCombinedId: resetWorldBoundState ? null : props.selectedOnlineCombinedId,
    fitRequest: resetWorldBoundState ? null : props.fitRequest,
    areaGeometry: resetWorldBoundState ? null : (props.areaGeometry ?? null),
    areaInteractionMode: resetWorldBoundState ? null : (props.areaInteractionMode ?? null),
    selectedAreaPlayer: resetWorldBoundState ? null : (props.selectedAreaPlayer ?? null),
    authorizationHeader: props.authorizationHeader ?? (() => null),
    onPointerCoordinate: coordinate => pointerCoordinate.value = coordinate,
    onSelectedCoordinate: coordinate => selectedCoordinate.value = coordinate,
    onSelectOnlinePlayer: combinedId => emit('selectOnlinePlayer', combinedId),
    onSelectObservation: snapshotId => emit('selectObservation', snapshotId),
    onSelectBusinessFeature: feature => emit('selectBusinessFeature', feature),
    onAreaGeometryChange: geometry => emit('updateAreaGeometry', geometry),
  })
  runtime.value = nextRuntime
  map.value = nextRuntime.map
  nextRuntime.updateBusinessSelection?.(resetWorldBoundState ? null : (props.selectedBusinessFeature ?? null))
  emit('layersReady', nextRuntime.layers ?? null)
  if (typeof ResizeObserver !== 'undefined') {
    resizeObserver = new ResizeObserver(() => nextRuntime.map.updateSize())
    resizeObserver.observe(target.value)
  }
}

watch(
  () => runtimeSignature(props.metadata),
  () => {
    const nextWorldIdentity = worldIdentity(props.metadata)
    const resetWorldBoundState = activeWorldIdentity !== null && activeWorldIdentity !== nextWorldIdentity
    activeWorldIdentity = nextWorldIdentity
    rebuildRuntime(resetWorldBoundState)
  },
)
watch(() => props.onlinePlayers, players => runtime.value?.updateOnlinePlayers(players))
watch(() => props.track, track => runtime.value?.updateTrack(track))
watch(
  () => [props.selectedSnapshotId, props.selectedOnlineCombinedId] as const,
  ([snapshotId, onlineCombinedId]) => runtime.value?.updateSelection(snapshotId, onlineCombinedId),
)
watch(() => props.fitRequest, request => runtime.value?.applyFit(request))
watch(() => props.selectedBusinessFeature, feature => runtime.value?.updateBusinessSelection(feature ?? null))
watch(() => props.areaGeometry, geometry => runtime.value?.updateAreaGeometry(geometry ?? null))
watch(() => props.areaInteractionMode, mode => runtime.value?.updateAreaInteractionMode(mode ?? null))
watch(() => props.selectedAreaPlayer, player => runtime.value?.updateAreaResultSelection(player ?? null))

onMounted(() => {
  activeWorldIdentity = worldIdentity(props.metadata)
  rebuildRuntime()
})
onUnmounted(disposeRuntime)
</script>

<template>
  <div class="relative min-h-72 min-w-0 overflow-hidden rounded-lg border border-default bg-elevated sm:min-h-96">
    <div
      ref="target"
      :aria-label="$t('players.map.canvasLabel')"
      class="absolute inset-0"
      data-testid="openlayers-map"
      role="application"
      tabindex="0"
    />
    <p class="pointer-events-none absolute top-2 left-2 max-w-[calc(100%-1rem)] rounded bg-default/90 px-2 py-1 text-xs shadow">
      {{ $t('players.map.backgroundNotice') }}
    </p>
    <div class="pointer-events-none absolute right-2 bottom-2 max-w-[calc(100%-1rem)] space-y-1 text-right font-mono text-xs">
      <p v-if="pointerCoordinate" class="rounded bg-default/90 px-2 py-1 shadow" data-testid="pointer-coordinate">
        {{ $t('players.map.pointerCoordinate') }}: X {{ pointerCoordinate.x.toFixed(1) }} · Z {{ pointerCoordinate.z.toFixed(1) }}
      </p>
      <p v-if="selectedCoordinate" class="rounded bg-default/90 px-2 py-1 shadow" data-testid="selected-coordinate">
        {{ $t('players.map.selectedCoordinate') }}: X {{ selectedCoordinate.x.toFixed(1) }} · Z {{ selectedCoordinate.z.toFixed(1) }}
      </p>
    </div>
  </div>
</template>
