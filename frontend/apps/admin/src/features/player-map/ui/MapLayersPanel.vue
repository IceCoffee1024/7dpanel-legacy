<script setup lang="ts">
import type { MapBusinessFeature, MapLayerId, MapVectorLayerState } from '../model/useMapVectorLayer'

import { useI18n } from 'vue-i18n'

export interface MapTilePanelState {
  readonly enabled: boolean
  readonly loading: boolean
  readonly error: string | null
}

export interface MapVectorPanelState {
  readonly id: MapLayerId
  readonly minimumZoom: number
  readonly enabled: boolean
  readonly state: MapVectorLayerState
  readonly count: number | null
  readonly error: string | null
  readonly items: readonly MapBusinessFeature[]
}

const props = defineProps<{
  tile: MapTilePanelState | null
  layers: readonly MapVectorPanelState[]
  selectedFeatureId: string | null
}>()

const emit = defineEmits<{
  toggleTile: [enabled: boolean]
  reloadTiles: []
  retryTiles: []
  toggleLayer: [layerId: MapLayerId, enabled: boolean]
  retryLayer: [layerId: MapLayerId]
  selectFeature: [feature: MapBusinessFeature]
}>()

const { locale } = useI18n()

function l(zh: string, en: string): string {
  return locale.value.startsWith('zh') ? zh : en
}

const layerLabels: Record<MapLayerId, readonly [string, string]> = {
  'historical-player-locations': ['历史玩家最后保留位置', 'Last retained player locations'],
  'traders': ['商人', 'Traders'],
  'claims': ['领地石', 'Land claims'],
  'vehicles': ['载具', 'Vehicles'],
  'drones': ['无人机', 'Drones'],
  'animals': ['动物', 'Animals'],
  'hostiles': ['敌对实体', 'Hostile entities'],
}

function layerLabel(layerId: MapLayerId): string {
  const label = layerLabels[layerId]
  return l(label[0], label[1])
}

function stateLabel(state: MapVectorLayerState, minimumZoom: number): string {
  const labels: Record<MapVectorLayerState, string> = {
    'off': l('已关闭', 'Off'),
    'paused': l('页面隐藏，已暂停', 'Paused while the page is hidden'),
    'zoom-required': l(`请放大到缩放级别 ${minimumZoom}`, `Zoom to level ${minimumZoom}`),
    'loading': l('正在加载', 'Loading'),
    'ready': l('已就绪', 'Ready'),
    'empty': l('没有返回对象', 'No objects returned'),
    'stale': l('刷新失败，显示上次结果', 'Refresh failed; showing the last result'),
    'failed': l('加载失败', 'Load failed'),
  }
  return labels[state]
}

function featureLabel(feature: MapBusinessFeature): string {
  switch (feature.kind) {
    case 'historical-player':
      return feature.name
    case 'trader':
      return feature.name ?? l('未知商人', 'Unknown trader')
    case 'claim':
      return feature.ownerCrossplatformId ?? l('未知所有者的领地', 'Claim with unknown owner')
    case 'vehicle':
      return feature.vehicleType ?? l('未知载具', 'Unknown vehicle')
    case 'drone':
      return feature.ownerCrossplatformId ?? l('未知所有者的无人机', 'Drone with unknown owner')
    case 'animal':
    case 'hostile':
      return feature.entityType
  }
}

function checked(event: Event): boolean {
  return (event.currentTarget as HTMLInputElement).checked
}
</script>

<template>
  <section aria-labelledby="map-layers-title" class="min-w-0 space-y-3 rounded-lg border border-default bg-default p-3 text-sm">
    <div>
      <h2 id="map-layers-title" class="font-semibold">
        {{ l('地图图层', 'Map layers') }}
      </h2>
      <p class="text-xs text-muted">
        {{ l('所有可选图层默认关闭，且仅提供只读信息。', 'Optional layers default off and provide read-only information only.') }}
      </p>
    </div>

    <div class="space-y-2 rounded-md border border-default p-2">
      <label class="flex items-center gap-2 font-medium">
        <input
          :checked="props.tile?.enabled ?? false"
          :disabled="props.tile === null"
          type="checkbox"
          @change="emit('toggleTile', checked($event))"
        >
        <span>{{ l('认证世界瓦片', 'Authenticated world tiles') }}</span>
      </label>
      <p v-if="props.tile?.loading" class="text-xs text-muted" role="status">
        {{ l('正在加载瓦片', 'Loading tiles') }}
      </p>
      <p v-if="props.tile?.error" class="text-xs text-error" role="alert">
        {{ l('瓦片加载失败，参考背景仍保持可见。', 'Tiles failed to load; the reference background remains visible.') }}
      </p>
      <div class="flex flex-wrap gap-2">
        <UButton
          color="neutral"
          :disabled="!props.tile?.enabled"
          size="xs"
          variant="outline"
          @click="emit('reloadTiles')"
        >
          {{ l('重新加载瓦片', 'Reload tiles') }}
        </UButton>
        <UButton
          v-if="props.tile?.error"
          color="neutral"
          size="xs"
          variant="outline"
          @click="emit('retryTiles')"
        >
          {{ l('重试', 'Retry') }}
        </UButton>
      </div>
    </div>

    <div v-for="layer in props.layers" :key="layer.id" class="space-y-2 rounded-md border border-default p-2">
      <label class="flex items-center justify-between gap-2 font-medium">
        <span class="flex min-w-0 items-center gap-2">
          <input
            :checked="layer.enabled"
            type="checkbox"
            @change="emit('toggleLayer', layer.id, checked($event))"
          >
          <span class="truncate">{{ layerLabel(layer.id) }}</span>
        </span>
        <span v-if="layer.count !== null" class="shrink-0 rounded bg-elevated px-1.5 py-0.5 text-xs tabular-nums">
          {{ layer.count }}
        </span>
      </label>
      <p class="text-xs text-muted" :role="layer.state === 'failed' || layer.state === 'stale' ? 'alert' : 'status'">
        {{ stateLabel(layer.state, layer.minimumZoom) }}
      </p>
      <UButton
        v-if="layer.error"
        color="neutral"
        size="xs"
        variant="outline"
        @click="emit('retryLayer', layer.id)"
      >
        {{ l('重试此图层', 'Retry this layer') }}
      </UButton>
      <ul v-if="layer.enabled && layer.items.length > 0" class="max-h-48 space-y-1 overflow-y-auto" :aria-label="layerLabel(layer.id)">
        <li v-for="feature in layer.items" :key="feature.id">
          <button
            class="w-full rounded px-2 py-1.5 text-left hover:bg-elevated focus-visible:outline-2 focus-visible:outline-primary"
            :class="feature.id === props.selectedFeatureId ? 'bg-elevated font-medium' : ''"
            type="button"
            @click="emit('selectFeature', feature)"
          >
            <span class="block truncate">{{ featureLabel(feature) }}</span>
            <span class="block font-mono text-xs text-muted">X {{ feature.x.toFixed(1) }} · Z {{ feature.z.toFixed(1) }}</span>
          </button>
        </li>
      </ul>
    </div>
  </section>
</template>
