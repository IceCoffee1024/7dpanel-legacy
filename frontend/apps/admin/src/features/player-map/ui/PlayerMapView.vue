<script setup lang="ts">
import type { LocationQueryRaw } from 'vue-router'

import { computed, onUnmounted, shallowRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'

import { useAuthStore } from '../../auth'
import PlayersSectionNavigation from '../../players/ui/PlayersSectionNavigation.vue'
import type { MapBusinessFeature, MapLayerId } from '../model/useMapVectorLayer'
import type { AreaGeometry } from '../model/useAreaInvestigation'
import {
  AREA_INVESTIGATION_URL_KEYS,
  createAreaInvestigationController,
  DEFAULT_AREA_INVESTIGATION_LIMIT,
} from '../model/useAreaInvestigation'
import { usePlayerMap } from '../model/usePlayerMap'
import type { AreaInteractionMode, MapLayersRuntime } from './openLayersGameMapRuntime'

import MapAreaInvestigation from './MapAreaInvestigation.vue'
import MapFeatureDetails from './MapFeatureDetails.vue'
import MapLayersPanel from './MapLayersPanel.vue'
import type { MapTilePanelState, MapVectorPanelState } from './MapLayersPanel.vue'
import OnlinePlayerMapList from './OnlinePlayerMapList.vue'
import OpenLayersGameMap from './OpenLayersGameMap.vue'
import PlayerTrackObservations from './PlayerTrackObservations.vue'

const { d, t } = useI18n()
const auth = useAuthStore()
const route = useRoute()
const router = useRouter()
const controller = usePlayerMap()
const initialAreaQuery = new URLSearchParams()
for (const [key, value] of Object.entries(route.query)) {
  if (typeof value === 'string')
    initialAreaQuery.set(key, value)
}
const investigation = createAreaInvestigationController({
  authorizationHeader: () => auth.authorizationHeader,
  initialQuery: initialAreaQuery,
  replaceQuery(query) {
    const next: LocationQueryRaw = { ...route.query }
    for (const key of AREA_INVESTIGATION_URL_KEYS)
      delete next[key]
    for (const [key, value] of query)
      next[key] = value
    void router.replace({ query: next })
  },
  limit: DEFAULT_AREA_INVESTIGATION_LIMIT,
})
const selectedPlayer = shallowRef(controller.filters.value.player ?? '')
const fromUtc = shallowRef(controller.filters.value.fromUtc ?? '')
const toUtc = shallowRef(controller.filters.value.toUtc ?? '')
const selectedOnlineCombinedId = shallowRef<string | null>(null)
const selectedMapFeature = shallowRef<MapBusinessFeature | null>(null)
const layerRuntime = shallowRef<MapLayersRuntime | null>(null)
const areaMode = shallowRef<AreaGeometry['kind']>(investigation.geometry.value?.kind ?? 'rectangle')
const areaInteractionMode = shallowRef<AreaInteractionMode>(null)

const tilePanelState = computed<MapTilePanelState | null>(() => {
  const tile = layerRuntime.value?.tile
  return tile === undefined
    ? null
    : {
        enabled: tile.enabled.value,
        loading: tile.loading.value,
        error: tile.error.value,
      }
})
const vectorPanelStates = computed<readonly MapVectorPanelState[]>(() =>
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

const pageStateTitle = computed(() => t(`players.map.state.${controller.state.value}`))
const observations = computed(() => controller.track.value?.segments.flatMap(segment => segment.points) ?? [])
const selectedObservationIndex = computed(() => observations.value.findIndex(
  point => point.snapshotId === controller.selectedSnapshotId.value,
))
const selectedObservation = computed(() => selectedObservationIndex.value < 0
  ? null
  : (observations.value[selectedObservationIndex.value] ?? null))
const gameTimeLabel = computed(() => {
  const value = controller.gameTime.value
  if (value === null)
    return t(`players.map.gameTime.${controller.gameTimeState.value}`)
  return t('players.map.gameTime.value', {
    day: value.day,
    hour: String(value.hour).padStart(2, '0'),
    minute: String(value.minute).padStart(2, '0'),
  })
})

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
  () => {
    const value = controller.metadata.value
    if (value === null)
      return null
    const { minimumX, minimumZ, maximumX, maximumZ } = value.extent
    return `${value.worldId}\n${minimumX}\n${minimumZ}\n${maximumX}\n${maximumZ}`
  },
  (identity, previousIdentity) => {
    if (previousIdentity !== null && identity !== previousIdentity)
      selectedOnlineCombinedId.value = null
    if (previousIdentity !== null && identity !== previousIdentity)
      selectedMapFeature.value = null
    if (previousIdentity !== null && identity !== previousIdentity) {
      areaInteractionMode.value = null
      investigation.clear()
    }
  },
)

onUnmounted(investigation.cancel)

function authorizationHeader(): string | null {
  return auth.authorizationHeader
}

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
    investigation.setRectangle(
      geometry.minimumX,
      geometry.minimumZ,
      geometry.maximumX,
      geometry.maximumZ,
    )
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

async function loadAreaResultTrack(combinedId: string) {
  const range = investigation.timeRange.value
  if (range === null)
    return
  selectedPlayer.value = combinedId
  fromUtc.value = range.fromUtc
  toUtc.value = range.toUtc
  await applyTrackFilters()
}

function searchPlayers(event: Event) {
  void controller.searchHistoricalPlayers((event.currentTarget as HTMLInputElement).value)
}

function selectObservationAtIndex(event: Event) {
  const index = Number((event.currentTarget as HTMLInputElement).value)
  const observation = observations.value[index]
  if (observation !== undefined)
    controller.selectObservation(observation.snapshotId)
}

async function applyTrackFilters() {
  controller.setPlayer(selectedPlayer.value || null)
  controller.setRange(fromUtc.value || null, toUtc.value || null)
  await controller.refreshTrack()
}
</script>

<template>
  <UDashboardPanel id="player-map">
    <template #header>
      <div class="space-y-3 p-3">
        <PlayersSectionNavigation />
        <div class="flex min-w-0 flex-wrap items-start justify-between gap-3">
          <div class="min-w-0">
            <h1 class="text-lg font-semibold">
              {{ t('players.map.title') }}
            </h1>
            <p class="text-sm text-muted">
              {{ t('players.map.description') }}
            </p>
          </div>
          <UButton
            color="neutral"
            icon="i-lucide-refresh-cw"
            :label="t('players.map.refresh')"
            variant="outline"
            @click="controller.refresh"
          />
        </div>
      </div>
    </template>

    <template #body>
      <main class="min-w-0 max-w-full space-y-4 overflow-x-hidden p-3" data-testid="player-map-layout">
        <section
          :data-state="controller.state.value"
          class="rounded-lg border border-default bg-default p-3"
          role="status"
        >
          <p class="font-medium">
            {{ pageStateTitle }}
          </p>
          <p v-if="controller.metadata.value" class="text-sm text-muted">
            {{ controller.metadata.value.worldName }}
          </p>
        </section>

        <section data-testid="player-map-filters" class="grid min-w-0 max-w-full grid-cols-1 gap-3 rounded-lg border border-default bg-default p-3 sm:grid-cols-2 xl:grid-cols-4">
          <div class="min-w-0">
            <p class="text-xs font-medium text-muted">
              {{ t('players.map.gameTime.title') }}
            </p>
            <p>{{ gameTimeLabel }}</p>
            <p v-if="controller.gameTime.value" class="text-xs text-muted">
              {{ t('players.map.observedAt', { time: d(new Date(controller.gameTime.value.observedAtUtc), 'playerObservation') }) }}
              <span v-if="controller.gameTimeState.value === 'stale'"> · {{ t('players.map.gameTime.stale') }}</span>
            </p>
          </div>
          <label class="min-w-0 text-sm">
            <span class="mb-1 block font-medium">{{ t('players.map.searchPlayers') }}</span>
            <input
              :value="controller.playerSearch.value"
              class="w-full min-w-0 rounded-md border border-default bg-default px-2 py-2"
              data-testid="player-search"
              :placeholder="t('players.map.searchPlayersPlaceholder')"
              type="search"
              @input="searchPlayers"
            >
          </label>
          <label class="min-w-0 text-sm">
            <span class="mb-1 block font-medium">{{ t('players.map.player') }}</span>
            <select v-model="selectedPlayer" class="w-full min-w-0 rounded-md border border-default bg-default px-2 py-2">
              <option value="">{{ t('players.map.selectPlayer') }}</option>
              <option v-for="option in playerOptions" :key="option.value" :value="option.value">
                {{ option.label }}
              </option>
            </select>
          </label>
          <label class="min-w-0 text-sm">
            <span class="mb-1 block font-medium">{{ t('players.map.fromUtc') }}</span>
            <input
              v-model="fromUtc"
              class="w-full rounded-md border border-default bg-default px-2 py-2"
              placeholder="2026-07-25T00:00:00Z"
              type="text"
            >
          </label>
          <label class="min-w-0 text-sm">
            <span class="mb-1 block font-medium">{{ t('players.map.toUtc') }}</span>
            <input
              v-model="toUtc"
              class="w-full rounded-md border border-default bg-default px-2 py-2"
              placeholder="2026-07-26T00:00:00Z"
              type="text"
            >
          </label>
          <div class="sm:col-span-2 xl:col-span-4">
            <UButton
              data-testid="load-track"
              :disabled="selectedPlayer === '' || fromUtc === '' || toUtc === ''"
              icon="i-lucide-route"
              :label="t('players.map.loadTrack')"
              @click="applyTrackFilters"
            />
          </div>
        </section>

        <div class="grid min-w-0 gap-4 xl:grid-cols-[minmax(0,2fr)_minmax(16rem,1fr)]">
          <OpenLayersGameMap
            v-if="controller.metadata.value"
            :authorization-header="authorizationHeader"
            :fit-request="controller.fitRequest.value"
            :area-geometry="investigation.geometry.value"
            :area-interaction-mode="areaInteractionMode"
            :metadata="controller.metadata.value"
            :online-players="controller.onlinePlayers.value"
            :selected-business-feature="selectedMapFeature"
            :selected-area-player="investigation.selectedPlayer.value"
            :selected-online-combined-id="selectedOnlineCombinedId"
            :selected-snapshot-id="controller.selectedSnapshotId.value"
            :track="controller.track.value"
            @select-observation="controller.selectObservation"
            @select-online-player="selectedOnlineCombinedId = $event"
            @select-business-feature="selectedMapFeature = $event"
            @layers-ready="setLayerRuntime"
            @update-area-geometry="updateAreaGeometry"
          />
          <section v-else class="min-h-72 rounded-lg border border-default bg-elevated p-4" data-testid="map-unavailable">
            <p class="font-medium">
              {{ t('players.map.unavailableTitle') }}
            </p>
            <p class="text-sm text-muted">
              {{ t('players.map.unavailableDescription') }}
            </p>
          </section>

          <aside class="min-w-0 space-y-4">
            <section class="rounded-lg border border-default bg-default p-3 text-sm">
              <p>{{ t('players.map.onlineStatus', { state: t(`players.map.dataState.${controller.onlineState?.value ?? 'loading'}`) }) }}</p>
              <p>{{ t('players.map.historyStatus', { state: t(`players.map.dataState.${controller.historyState?.value ?? 'loading'}`) }) }}</p>
              <p>{{ t('players.map.trackStatus', { state: t(`players.map.dataState.${controller.trackState?.value ?? 'empty'}`) }) }}</p>
              <p v-if="controller.track.value" class="font-medium">
                {{ t('players.map.observationCount', { count: controller.observationCount.value }) }}
              </p>
            </section>
            <p class="rounded-lg border border-default bg-default p-3 text-sm" data-testid="track-discrete-notice">
              {{ t('players.map.discreteNotice') }}
            </p>
            <MapAreaInvestigation
              :investigation="investigation"
              :limit="investigation.limit.value"
              :mode="areaMode"
              @clear-geometry="clearAreaInteraction"
              @draw-geometry="drawArea"
              @load-history-track="loadAreaResultTrack"
              @modify-geometry="modifyArea"
              @open-history-profile="openHistoryProfile"
              @select-result="selectAreaResult"
              @update:limit="setAreaLimit"
              @update:mode="areaMode = $event"
            />
            <MapLayersPanel
              :layers="vectorPanelStates"
              :selected-feature-id="selectedMapFeature?.id ?? null"
              :tile="tilePanelState"
              @reload-tiles="layerRuntime?.tile.reload()"
              @retry-layer="vectorLayer($event)?.retry()"
              @retry-tiles="layerRuntime?.tile.retry()"
              @select-feature="selectedMapFeature = $event"
              @toggle-layer="toggleLayer"
              @toggle-tile="layerRuntime?.tile.setEnabled($event)"
            />
            <MapFeatureDetails :feature="selectedMapFeature" />
            <OnlinePlayerMapList
              :players="controller.onlinePlayers.value"
              :selected-combined-id="selectedOnlineCombinedId"
              @select="selectedOnlineCombinedId = $event"
            />
            <section class="min-w-0 space-y-2 rounded-lg border border-default bg-default p-3">
              <label class="block text-sm font-medium" for="observation-time-control">
                {{ t('players.map.observationTimeControl') }}
              </label>
              <input
                id="observation-time-control"
                class="w-full min-w-0"
                data-testid="observation-time-control"
                :disabled="observations.length === 0 || selectedObservationIndex < 0"
                :max="Math.max(0, observations.length - 1)"
                min="0"
                :value="Math.max(0, selectedObservationIndex)"
                step="1"
                type="range"
                @input="selectObservationAtIndex"
              >
              <p data-testid="selected-observed-at" class="break-words text-sm text-muted">
                {{ selectedObservation
                  ? t('players.map.observedAt', { time: d(new Date(selectedObservation.observedAtUtc), 'playerObservation') })
                  : t('players.map.noObservationSelected') }}
              </p>
            </section>
            <PlayerTrackObservations
              :segments="controller.track.value?.segments ?? []"
              :selected-snapshot-id="controller.selectedSnapshotId.value"
              @select="controller.selectObservation"
            />
          </aside>
        </div>
      </main>
    </template>
  </UDashboardPanel>
</template>
