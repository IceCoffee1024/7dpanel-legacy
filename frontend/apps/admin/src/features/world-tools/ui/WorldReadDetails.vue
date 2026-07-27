<script setup lang="ts">
import type {
  WorldCatalog,
  WorldCollection,
  WorldContainer,
  WorldDrone,
  WorldLandClaim,
  WorldSummary,
  WorldVehicle,
} from '../api/worldTools'
import type { WorldResourceState } from '../model/useWorldResources'

import { useI18n } from 'vue-i18n'

const props = defineProps<{
  summary: WorldResourceState<WorldSummary>
  landClaims: WorldResourceState<WorldCollection<WorldLandClaim>>
  vehicles: WorldResourceState<WorldCollection<WorldVehicle>>
  drones: WorldResourceState<WorldCollection<WorldDrone>>
  containers: WorldResourceState<WorldCollection<WorldContainer>>
  blockCatalog: WorldResourceState<WorldCatalog>
  prefabCatalog: WorldResourceState<WorldCatalog>
  entityTypeCatalog: WorldResourceState<WorldCatalog>
}>()
const { t } = useI18n()

function display(value: string | number | boolean | null | undefined): string {
  if (value === null || value === undefined)
    return '—'
  if (typeof value === 'boolean')
    return value ? t('worldTools.common.yes') : t('worldTools.common.no')
  return String(value)
}

function observed(value: { observedAtUtc?: string | null } | null): string {
  return value?.observedAtUtc ? new Date(value.observedAtUtc).toLocaleString() : '—'
}

function sourceColor(state: WorldResourceState<unknown>['sourceState']): 'success' | 'info' | 'warning' | 'error' {
  if (state === 'Success') return 'success'
  if (state === 'Partial') return 'info'
  if (state === 'Stale') return 'warning'
  return 'error'
}
</script>

<template>
  <div class="space-y-6">
    <section class="space-y-3 rounded-lg border border-default p-4">
      <div class="flex flex-wrap items-start justify-between gap-3">
        <div><h2 class="font-semibold text-highlighted">{{ t('worldTools.read.summary.title') }}</h2><p class="text-xs text-muted">{{ t('worldTools.read.observed', { time: observed(props.summary.data) }) }}</p></div>
        <UBadge :color="sourceColor(props.summary.sourceState)" variant="subtle">{{ props.summary.sourceState }}</UBadge>
      </div>
      <UAlert v-if="props.summary.phase === 'failed'" color="error" :title="t('worldTools.read.summary.unavailable')" :description="props.summary.errorCode ?? undefined" variant="subtle" />
      <dl v-else class="grid gap-3 text-sm sm:grid-cols-2 lg:grid-cols-4">
        <div><dt class="text-xs text-muted">{{ t('worldTools.read.summary.worldId') }}</dt><dd class="break-all">{{ display(props.summary.data?.worldId) }}</dd></div>
        <div><dt class="text-xs text-muted">{{ t('worldTools.read.summary.worldVersion') }}</dt><dd class="break-all">{{ display(props.summary.data?.worldVersion) }}</dd></div>
        <div><dt class="text-xs text-muted">{{ t('worldTools.read.summary.mapResourceVersion') }}</dt><dd class="break-all">{{ display(props.summary.data?.mapResourceVersion) }}</dd></div>
        <div><dt class="text-xs text-muted">{{ t('worldTools.read.summary.gameVersion') }}</dt><dd>{{ display(props.summary.data?.gameVersion) }}</dd></div>
        <div><dt class="text-xs text-muted">{{ t('worldTools.read.summary.seed') }}</dt><dd class="break-all">{{ display(props.summary.data?.seed) }}</dd></div>
        <div><dt class="text-xs text-muted">{{ t('worldTools.read.summary.width') }}</dt><dd>{{ display(props.summary.data?.width) }}</dd></div>
        <div><dt class="text-xs text-muted">{{ t('worldTools.read.summary.height') }}</dt><dd>{{ display(props.summary.data?.height) }}</dd></div>
        <div><dt class="text-xs text-muted">{{ t('worldTools.read.summary.availableExtent') }}</dt><dd>{{ props.summary.data?.availableExtent ? t('worldTools.read.summary.availableExtentValue', { minimumX: props.summary.data.availableExtent.minimumX, maximumX: props.summary.data.availableExtent.maximumX, minimumZ: props.summary.data.availableExtent.minimumZ, maximumZ: props.summary.data.availableExtent.maximumZ }) : '—' }}</dd></div>
      </dl>
    </section>

    <section class="space-y-3">
      <div class="flex flex-wrap items-start justify-between gap-3"><div><h2 class="font-semibold text-highlighted">{{ t('worldTools.read.landClaims.title') }}</h2><p class="text-xs text-muted">{{ t('worldTools.read.observed', { time: observed(props.landClaims.data) }) }}</p></div><UBadge :color="sourceColor(props.landClaims.sourceState)" variant="subtle">{{ props.landClaims.sourceState }}</UBadge></div>
      <UAlert v-if="props.landClaims.phase === 'failed'" color="error" :title="t('worldTools.read.landClaims.unavailable')" variant="subtle" />
      <div v-else class="grid gap-3 lg:grid-cols-2">
        <article v-for="claim in props.landClaims.data?.items ?? []" :key="claim.stableIdentity" class="space-y-2 rounded-lg border border-default p-3 text-sm">
          <div class="flex flex-wrap justify-between gap-2"><strong class="break-all text-highlighted">{{ claim.stableIdentity }}</strong><UBadge :color="claim.isValid === false ? 'warning' : 'neutral'" variant="subtle">{{ display(claim.isValid) }}</UBadge></div>
          <p class="text-muted">{{ t('worldTools.read.landClaims.owner', { owner: display(claim.ownerStableIdentity) }) }}</p>
          <p>{{ t('worldTools.read.landClaims.position', { x: claim.position.x, y: claim.position.y, z: claim.position.z, radius: display(claim.protectionRadius) }) }}</p>
          <p class="text-xs text-muted">{{ t('worldTools.read.landClaims.ownerDetails', { lastLogin: display(claim.ownerLastLoginUtc), serverId: claim.serverId }) }}</p>
        </article>
        <p v-if="props.landClaims.data?.items.length === 0" class="text-sm text-muted">{{ t('worldTools.read.landClaims.empty') }}</p>
      </div>
    </section>

    <section class="space-y-3">
      <div class="flex flex-wrap items-start justify-between gap-3"><div><h2 class="font-semibold text-highlighted">{{ t('worldTools.read.vehicles.title') }}</h2><p class="text-xs text-muted">{{ t('worldTools.read.observed', { time: observed(props.vehicles.data) }) }}</p></div><UBadge :color="sourceColor(props.vehicles.sourceState)" variant="subtle">{{ props.vehicles.sourceState }}</UBadge></div>
      <UAlert v-if="props.vehicles.phase === 'failed'" color="error" :title="t('worldTools.read.vehicles.unavailable')" variant="subtle" />
      <div v-else class="grid gap-3 lg:grid-cols-2">
        <article v-for="vehicle in props.vehicles.data?.items ?? []" :key="vehicle.stableIdentity" class="space-y-2 rounded-lg border border-default p-3 text-sm">
          <div class="flex flex-wrap justify-between gap-2"><strong class="break-all text-highlighted">{{ vehicle.stableIdentity }}</strong><UBadge color="neutral" variant="subtle">{{ vehicle.loadState }}</UBadge></div>
          <p>{{ t('worldTools.read.vehicles.typeAndOwner', { type: display(vehicle.entityTypeResourceId), owner: display(vehicle.ownerStableIdentity) }) }}</p>
          <p>{{ t('worldTools.read.position', { x: vehicle.position.x, y: vehicle.position.y, z: vehicle.position.z }) }}</p>
          <p class="text-muted">{{ t('worldTools.read.vehicles.details', { locked: display(vehicle.isLocked), fuel: display(vehicle.fuelPercentage), quality: display(vehicle.quality), container: display(vehicle.container?.stableIdentity) }) }}</p>
        </article>
        <p v-if="props.vehicles.data?.items.length === 0" class="text-sm text-muted">{{ t('worldTools.read.vehicles.empty') }}</p>
      </div>
    </section>

    <section class="space-y-3">
      <div class="flex flex-wrap items-start justify-between gap-3"><div><h2 class="font-semibold text-highlighted">{{ t('worldTools.read.drones.title') }}</h2><p class="text-xs text-muted">{{ t('worldTools.read.observed', { time: observed(props.drones.data) }) }}</p></div><UBadge :color="sourceColor(props.drones.sourceState)" variant="subtle">{{ props.drones.sourceState }}</UBadge></div>
      <UAlert v-if="props.drones.phase === 'failed'" color="error" :title="t('worldTools.read.drones.unavailable')" variant="subtle" />
      <div v-else class="grid gap-3 lg:grid-cols-2">
        <article v-for="drone in props.drones.data?.items ?? []" :key="drone.stableIdentity" class="space-y-2 rounded-lg border border-default p-3 text-sm">
          <div class="flex flex-wrap justify-between gap-2"><strong class="break-all text-highlighted">{{ drone.stableIdentity }}</strong><UBadge color="neutral" variant="subtle">{{ drone.loadState }}</UBadge></div>
          <p>{{ t('worldTools.read.drones.typeAndOwner', { type: display(drone.entityTypeResourceId), owner: display(drone.ownerStableIdentity) }) }}</p>
          <p>{{ t('worldTools.read.position', { x: drone.position.x, y: drone.position.y, z: drone.position.z }) }}</p>
          <p class="text-muted">{{ t('worldTools.read.drones.details', { locked: display(drone.isLocked), quality: display(drone.quality), container: display(drone.container?.stableIdentity) }) }}</p>
        </article>
        <p v-if="props.drones.data?.items.length === 0" class="text-sm text-muted">{{ t('worldTools.read.drones.empty') }}</p>
      </div>
    </section>

    <section class="space-y-3">
      <div class="flex flex-wrap items-start justify-between gap-3"><div><h2 class="font-semibold text-highlighted">{{ t('worldTools.read.containers.title') }}</h2><p class="text-xs text-muted">{{ t('worldTools.read.observed', { time: observed(props.containers.data) }) }}</p></div><UBadge :color="sourceColor(props.containers.sourceState)" variant="subtle">{{ props.containers.sourceState }}</UBadge></div>
      <UAlert v-if="props.containers.phase === 'failed'" color="error" :title="t('worldTools.read.containers.unavailable')" variant="subtle" />
      <div v-else class="grid gap-3 lg:grid-cols-2">
        <article v-for="container in props.containers.data?.items ?? []" :key="container.stableIdentity" class="space-y-2 rounded-lg border border-default p-3 text-sm">
          <div class="flex flex-wrap justify-between gap-2"><strong class="break-all text-highlighted">{{ container.stableIdentity }}</strong><UBadge color="neutral" variant="subtle">{{ container.loadState }}</UBadge></div>
          <p>{{ t('worldTools.read.containers.parentAndPosition', { parent: container.parentStableIdentity, x: container.position.x, y: container.position.y, z: container.position.z }) }}</p>
          <p class="text-muted">{{ t('worldTools.read.containers.details', { locked: display(container.isLocked), used: display(container.usedSlotCount), total: display(container.slotCount) }) }}</p>
          <ul v-if="container.items" class="space-y-1"><li v-for="item in container.items" :key="`${container.stableIdentity}:${item.resourceId}`">{{ t('worldTools.read.containers.item', { resourceId: item.resourceId, count: item.count, quality: display(item.quality) }) }}</li></ul>
          <p v-else class="text-xs text-muted">{{ t('worldTools.read.containers.itemDetailsUnavailable') }}</p>
        </article>
        <p v-if="props.containers.data?.items.length === 0" class="text-sm text-muted">{{ t('worldTools.read.containers.empty') }}</p>
      </div>
    </section>

    <section class="space-y-3">
      <h2 class="font-semibold text-highlighted">{{ t('worldTools.read.catalogs.title') }}</h2>
      <div class="grid gap-3 lg:grid-cols-3">
        <article v-for="catalog in [{ id: 'blocks', label: t('worldTools.read.catalogs.blocks'), state: props.blockCatalog }, { id: 'prefabs', label: t('worldTools.read.catalogs.prefabs'), state: props.prefabCatalog }, { id: 'entityTypes', label: t('worldTools.read.catalogs.entityTypes'), state: props.entityTypeCatalog }]" :key="catalog.id" class="space-y-2 rounded-lg border border-default p-3">
          <div class="flex flex-wrap items-center justify-between gap-2"><strong>{{ catalog.label }}</strong><UBadge :color="sourceColor(catalog.state.sourceState)" variant="subtle">{{ catalog.state.sourceState }}</UBadge></div>
          <p class="break-all text-xs text-muted">{{ t('worldTools.read.catalogs.versionObserved', { version: display(catalog.state.data?.catalogVersion), time: observed(catalog.state.data) }) }}</p>
          <p class="text-sm">{{ t('worldTools.read.catalogs.approvedIdentifiers', { count: catalog.state.data?.items.length ?? 0 }) }}</p>
        </article>
      </div>
    </section>
  </div>
</template>
