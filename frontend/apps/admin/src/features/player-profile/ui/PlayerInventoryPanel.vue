<script setup lang="ts">
import type { InventoryDiff, InventorySnapshot, ProfileSection } from './playerProfileUi'

import { useI18n } from 'vue-i18n'

import PlayerEvidenceBadge from './PlayerEvidenceBadge.vue'

defineProps<{
  section: ProfileSection<InventorySnapshot>
  diffs: readonly InventoryDiff[]
  gaps: readonly unknown[]
}>()
const emit = defineEmits<{ loadMore: [] }>()
const { d, t } = useI18n()

function itemLabel(diff: InventoryDiff['changes'][number]): string {
  return diff.currentItem?.internalName ?? diff.previousItem?.internalName ?? t('common.unknown')
}
</script>

<template>
  <section class="space-y-3" aria-labelledby="player-inventory-title">
    <div class="flex flex-wrap items-center justify-between gap-2">
      <h2 id="player-inventory-title" class="font-semibold text-highlighted">
        {{ t('players.profile.inventory.title') }}
      </h2>
      <div class="flex gap-2">
        <PlayerEvidenceBadge :state="section.state" />
        <PlayerEvidenceBadge v-if="section.gapMetadata.length || gaps.length" gap />
      </div>
    </div>
    <UAlert
      v-if="section.state === 'Partial' || section.gapMetadata.length || gaps.length"
      color="warning"
      :title="t('players.profile.evidence.gap')"
      :description="t('players.profile.evidence.incompleteDescription')"
    />
    <template v-if="section.value">
      <p class="text-sm text-muted">
        {{ d(new Date(section.value.observedAtUtc), 'playerObservation') }} · {{ section.value.gameVersion }} ·
        {{ section.value.catalogVersion ?? t('players.profile.inventory.catalogUnavailable') }}
      </p>
      <div class="hidden overflow-x-auto md:block">
        <table class="w-full text-sm">
          <thead>
            <tr class="border-b border-default text-left text-muted">
              <th class="p-2">
                {{ t('players.profile.inventory.item') }}
              </th><th class="p-2">
                {{ t('players.profile.inventory.container') }}
              </th><th class="p-2">
                {{ t('players.profile.inventory.quantity') }}
              </th><th class="p-2">
                {{ t('players.profile.inventory.quality') }}
              </th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="item in section.value.items" :key="`${item.container}:${item.slot}`" class="border-b border-muted">
              <td class="p-2 font-mono">
                {{ item.internalName }}
              </td><td class="p-2">
                {{ item.container }} · {{ item.slot }}
              </td><td class="p-2">
                {{ item.count }}
              </td><td class="p-2">
                {{ item.quality ?? t('common.unknown') }}
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <ul class="space-y-2 md:hidden">
        <li v-for="item in section.value.items" :key="`${item.container}:${item.slot}`" class="rounded-lg border border-default p-3">
          <p class="break-all font-mono text-sm">
            {{ item.internalName }}
          </p>
          <p class="text-sm text-muted">
            {{ item.container }} · {{ item.slot }} · ×{{ item.count }}
          </p>
        </li>
      </ul>
    </template>
    <UAlert v-else color="neutral" :title="t(`players.profile.section.${section.state.toLowerCase()}`)" />
    <div v-if="diffs.length" class="space-y-2">
      <h3 class="text-sm font-semibold">
        {{ t('players.profile.inventory.changes') }}
      </h3>
      <article v-for="diff in diffs" :key="diff.currentSnapshotId" class="rounded-lg border border-default p-3">
        <div class="mb-2 flex flex-wrap items-center justify-between gap-2">
          <span class="text-sm text-muted">{{ d(new Date(diff.currentObservedAtUtc), 'playerObservation') }}</span>
          <PlayerEvidenceBadge v-if="!diff.isComplete" gap />
        </div>
        <ul class="space-y-2">
          <li v-for="(change, index) in diff.changes" :key="`${diff.currentSnapshotId}:${index}`" class="flex flex-wrap items-center justify-between gap-2 text-sm">
            <span>{{ itemLabel(change) }} · {{ change.kind === 'Uncomparable' ? t('players.profile.inventory.uncomparable') : change.kind }}</span>
            <PlayerEvidenceBadge :level="change.evidenceLevel" />
          </li>
        </ul>
      </article>
    </div>
    <UButton
      color="neutral"
      size="sm"
      variant="outline"
      :label="t('players.history.loadMore')"
      @click="emit('loadMore')"
    />
  </section>
</template>
