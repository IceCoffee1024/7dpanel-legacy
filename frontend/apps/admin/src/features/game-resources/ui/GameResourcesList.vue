<script setup lang="ts">
import type { GameResourceItem } from '../api/gameResources'

import { useI18n } from 'vue-i18n'

import GameResourceIcon from './GameResourceIcon.vue'

defineProps<{
  items: readonly GameResourceItem[]
}>()

const emit = defineEmits<{
  copy: [internalName: string]
}>()

const { t } = useI18n()

function qualityLabel(value: boolean | null): string {
  if (value === null)
    return t('gameResources.values.unavailable')
  return value ? t('gameResources.values.yes') : t('gameResources.values.no')
}
</script>

<template>
  <ul class="space-y-3 md:hidden">
    <li
      v-for="item in items"
      :key="item.resourceId"
      class="rounded-md border border-default bg-elevated/25 p-4"
    >
      <div class="flex items-start gap-3">
        <GameResourceIcon
          :alt="item.localizedName ?? item.internalName"
          :icon-status="item.iconStatus"
          :resource-id="item.resourceId"
        />
        <div class="min-w-0 flex-1">
          <div class="flex flex-wrap items-start justify-between gap-2">
            <div class="min-w-0">
              <p class="font-medium text-highlighted">
                {{ item.localizedName ?? item.internalName }}
              </p>
              <code class="mt-1 block break-all text-xs text-default">{{ item.internalName }}</code>
            </div>
            <UButton
              color="neutral"
              :data-testid="`copy-${item.internalName}`"
              icon="i-lucide-copy"
              :label="t('gameResources.copy.action')"
              size="xs"
              variant="ghost"
              @click="emit('copy', item.internalName)"
            />
          </div>

          <dl class="mt-4 grid grid-cols-2 gap-x-4 gap-y-3 text-sm">
            <div>
              <dt class="text-muted">
                {{ t('gameResources.table.kind') }}
              </dt>
              <dd class="mt-1 text-default">
                {{ t(`gameResources.kind.${item.kind}`) }}
              </dd>
            </div>
            <div>
              <dt class="text-muted">
                {{ t('gameResources.table.visibility') }}
              </dt>
              <dd class="mt-1 text-default">
                {{ t(`gameResources.visibility.${item.visibility}`) }}
              </dd>
            </div>
            <div>
              <dt class="text-muted">
                {{ t('gameResources.table.maxStack') }}
              </dt>
              <dd class="mt-1 text-default">
                {{ item.maxStack ?? t('gameResources.values.unavailable') }}
              </dd>
            </div>
            <div>
              <dt class="text-muted">
                {{ t('gameResources.table.hasQuality') }}
              </dt>
              <dd class="mt-1 text-default">
                {{ qualityLabel(item.hasQuality) }}
              </dd>
            </div>
            <div class="col-span-2">
              <dt class="text-muted">
                {{ t('gameResources.table.tint') }}
              </dt>
              <dd v-if="item.iconTintHex" class="mt-1 inline-flex items-center gap-2 text-default">
                <span
                  :data-tint="item.iconTintHex"
                  class="size-4 rounded-sm border border-default"
                  :style="{ backgroundColor: `#${item.iconTintHex}` }"
                />
                <code class="text-xs">#{{ item.iconTintHex }}</code>
              </dd>
              <dd v-else class="mt-1 text-muted">
                {{ t('gameResources.values.none') }}
              </dd>
            </div>
          </dl>
        </div>
      </div>
    </li>
  </ul>
</template>
