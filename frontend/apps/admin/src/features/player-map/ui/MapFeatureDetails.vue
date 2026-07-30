<script setup lang="ts">
import type { MapBusinessFeature, TraderMapFeature } from '../model/useMapVectorLayer'

import { useI18n } from 'vue-i18n'

const props = defineProps<{
  feature: MapBusinessFeature | null
}>()

const { d, locale } = useI18n()

function l(zh: string, en: string): string {
  return locale.value.startsWith('zh') ? zh : en
}

function known(value: string | number | null): string {
  return value === null ? l('未知', 'Unknown') : String(value)
}

function yesNoUnknown(value: boolean | null): string {
  if (value === null)
    return l('未知', 'Unknown')
  return value ? l('是', 'Yes') : l('否', 'No')
}

function featureTitle(feature: MapBusinessFeature): string {
  switch (feature.kind) {
    case 'historical-player': return feature.name
    case 'trader': return feature.name ?? l('未知商人', 'Unknown trader')
    case 'claim': return l('领地石', 'Land claim')
    case 'vehicle': return feature.vehicleType ?? l('未知载具', 'Unknown vehicle')
    case 'drone': return l('无人机', 'Drone')
    case 'animal': return feature.entityType
    case 'hostile': return feature.entityType
  }
}

function profilePath(crossplatformId: string): string {
  return `/players/history/${encodeURIComponent(crossplatformId)}`
}

function traderBounds(feature: TraderMapFeature): string {
  const value = (feature as TraderMapFeature & {
    prefabBounds?: Readonly<{ minimumX?: unknown, minimumZ?: unknown, maximumX?: unknown, maximumZ?: unknown }> | null
  }).prefabBounds
  if (value === null || value === undefined)
    return l('未知', 'Unknown')
  const coordinates = [value.minimumX, value.minimumZ, value.maximumX, value.maximumZ]
  return coordinates.every(item => typeof item === 'number' && Number.isFinite(item))
    ? coordinates.join(' · ')
    : l('未知', 'Unknown')
}
</script>

<template>
  <section aria-labelledby="map-feature-details-title" class="min-w-0 rounded-lg border border-default bg-default p-3 text-sm">
    <h2 id="map-feature-details-title" class="font-semibold">
      {{ l('对象详情（只读）', 'Feature details (read-only)') }}
    </h2>
    <p v-if="props.feature === null" class="mt-2 text-muted">
      {{ l('在地图或同步对象列表中选择一个对象。', 'Select an object on the map or in the synchronized list.') }}
    </p>

    <div v-else class="mt-3 min-w-0 space-y-3">
      <div>
        <p class="break-words font-medium">
          {{ featureTitle(props.feature) }}
        </p>
        <p class="break-all font-mono text-xs text-muted">
          {{ props.feature.id }}
        </p>
      </div>

      <dl class="grid min-w-0 grid-cols-[max-content_minmax(0,1fr)] gap-x-3 gap-y-1">
        <dt class="text-muted">
          {{ l('坐标', 'Coordinates') }}
        </dt>
        <dd class="break-words font-mono">
          X {{ props.feature.x.toFixed(1) }} · Z {{ props.feature.z.toFixed(1) }}
        </dd>
        <dt class="text-muted">
          {{ l('观察时间', 'Observed') }}
        </dt>
        <dd class="break-words">
          {{ d(new Date(props.feature.observedAtUtc), 'playerObservation') }}
        </dd>

        <template v-if="props.feature.kind === 'historical-player'">
          <dt class="text-muted">
            {{ l('规范身份', 'Canonical identity') }}
          </dt>
          <dd class="break-all">
            {{ props.feature.playerCombinedId }}
          </dd>
        </template>

        <template v-else-if="props.feature.kind === 'trader'">
          <dt class="text-muted">
            {{ l('营业状态', 'Trader status') }}
          </dt>
          <dd>{{ props.feature.isOpen === null ? l('未知', 'Unknown') : (props.feature.isOpen ? l('营业中', 'Open') : l('已关闭', 'Closed')) }}</dd>
          <dt class="text-muted">
            Prefab
          </dt>
          <dd class="break-words">
            {{ known(props.feature.prefab) }}
          </dd>
          <dt class="text-muted">
            {{ l('Prefab 范围', 'Prefab bounds') }}
          </dt>
          <dd class="break-words font-mono">
            {{ traderBounds(props.feature) }}
          </dd>
          <dt class="text-muted">
            {{ l('保护半径', 'Protection radius') }}
          </dt>
          <dd>{{ known(props.feature.protectionRadius) }}</dd>
        </template>

        <template v-else-if="props.feature.kind === 'claim'">
          <dt class="text-muted">
            {{ l('所有者', 'Owner') }}
          </dt>
          <dd class="break-all">
            {{ known(props.feature.ownerCrossplatformId) }}
          </dd>
          <dt class="text-muted">
            {{ l('保护半径', 'Protection radius') }}
          </dt>
          <dd>{{ known(props.feature.protectionRadius) }}</dd>
          <dt class="text-muted">
            {{ l('有效', 'Valid') }}
          </dt>
          <dd>{{ yesNoUnknown(props.feature.isValid) }}</dd>
          <dt class="text-muted">
            {{ l('所有者最近登录', 'Owner last login') }}
          </dt>
          <dd>{{ props.feature.ownerLastLoginUtc ? d(new Date(props.feature.ownerLastLoginUtc), 'playerObservation') : l('未知', 'Unknown') }}</dd>
        </template>

        <template v-else-if="props.feature.kind === 'vehicle'">
          <dt class="text-muted">
            {{ l('类型', 'Type') }}
          </dt>
          <dd>{{ known(props.feature.vehicleType) }}</dd>
          <dt class="text-muted">
            {{ l('所有者', 'Owner') }}
          </dt>
          <dd class="break-all">
            {{ known(props.feature.ownerCrossplatformId) }}
          </dd>
          <dt class="text-muted">
            {{ l('加载状态', 'Load state') }}
          </dt>
          <dd>{{ props.feature.loadState === 'loaded' ? l('已加载', 'Loaded') : l('未加载', 'Unloaded') }}</dd>
          <dt class="text-muted">
            {{ l('燃油', 'Fuel') }}
          </dt>
          <dd>{{ props.feature.fuelPercentage === null ? l('未知', 'Unknown') : `${props.feature.fuelPercentage}%` }}</dd>
          <dt class="text-muted">
            {{ l('品质', 'Quality') }}
          </dt>
          <dd>{{ known(props.feature.quality) }}</dd>
          <dt class="text-muted">
            {{ l('已锁定', 'Locked') }}
          </dt>
          <dd>{{ yesNoUnknown(props.feature.isLocked) }}</dd>
          <dt class="text-muted">
            {{ l('储物数量', 'Storage item count') }}
          </dt>
          <dd>{{ known(props.feature.storageItemCount) }}</dd>
        </template>

        <template v-else-if="props.feature.kind === 'drone'">
          <dt class="text-muted">
            {{ l('所有者', 'Owner') }}
          </dt>
          <dd class="break-all">
            {{ known(props.feature.ownerCrossplatformId) }}
          </dd>
          <dt class="text-muted">
            {{ l('加载状态', 'Load state') }}
          </dt>
          <dd>{{ props.feature.loadState === 'loaded' ? l('已加载', 'Loaded') : l('未加载', 'Unloaded') }}</dd>
        </template>

        <template v-else-if="props.feature.kind === 'animal' || props.feature.kind === 'hostile'">
          <dt class="text-muted">
            {{ l('实体类型', 'Entity type') }}
          </dt>
          <dd class="break-words">
            {{ props.feature.entityType }}
          </dd>
        </template>
      </dl>

      <RouterLink
        v-if="props.feature.kind === 'historical-player'"
        class="inline-flex rounded text-primary hover:underline focus-visible:outline-2 focus-visible:outline-primary"
        :to="profilePath(props.feature.playerCombinedId)"
      >
        {{ l('查看历史玩家资料', 'View historical player profile') }}
      </RouterLink>
      <RouterLink
        v-else-if="'ownerCrossplatformId' in props.feature && props.feature.ownerCrossplatformId"
        class="inline-flex rounded text-primary hover:underline focus-visible:outline-2 focus-visible:outline-primary"
        :to="profilePath(props.feature.ownerCrossplatformId)"
      >
        {{ l('查看所有者资料', 'View owner profile') }}
      </RouterLink>
    </div>
  </section>
</template>
