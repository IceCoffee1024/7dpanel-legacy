<script setup lang="ts">
import type { TableColumn } from '@nuxt/ui'
import type { OnlinePlayer } from '../api/onlinePlayers'

import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

import { formatDeviceType } from '../model/onlinePlayerFormatting'
import { isOnlinePlayerObservationStale } from '../model/onlinePlayerFreshness'

const props = withDefaults(defineProps<{
  players: readonly OnlinePlayer[]
  canKick?: boolean
}>(), {
  canKick: true,
})

const emit = defineEmits<{
  viewDetails: [player: OnlinePlayer]
  kickPlayer: [player: OnlinePlayer]
}>()
const { d, t } = useI18n()

const columns = computed<TableColumn<OnlinePlayer>[]>(() => [
  { accessorKey: 'name', header: t('players.fields.player') },
  { id: 'state', header: t('players.fields.state') },
  { accessorKey: 'level', header: t('players.fields.level') },
  { accessorKey: 'ping', header: t('players.fields.ping') },
  { id: 'device', header: t('players.fields.device') },
  { id: 'updated', header: t('players.fields.observedAt') },
  { id: 'actions', header: t('players.fields.actions') },
])

const tableData = computed(() => props.players as OnlinePlayer[])
</script>

<template>
  <div class="hidden md:block">
    <UTable
      :columns="columns"
      :data="tableData"
      :ui="{
        root: 'overflow-visible',
        base: 'table-fixed',
        th: 'text-xs whitespace-normal',
        td: 'align-top whitespace-normal',
      }"
    >
      <template #name-cell="{ row }">
        <div class="min-w-0">
          <p class="wrap-break-word font-medium text-highlighted">
            {{ row.original.name }}
          </p>
          <p class="mt-1 font-mono text-xs text-dimmed">
            entity {{ row.original.entityId }}
          </p>
          <UBadge
            v-if="isOnlinePlayerObservationStale(row.original.observedAtUtc)"
            class="mt-2"
            color="warning"
            variant="subtle"
          >
            {{ t('players.fields.stale') }}
          </UBadge>
        </div>
      </template>

      <template #state-cell="{ row }">
        <div class="flex flex-col items-start gap-1">
          <UBadge :color="row.original.isDead ? 'error' : 'success'" variant="subtle">
            {{ row.original.isDead ? t('players.fields.dead') : t('players.fields.alive') }}
          </UBadge>
          <span class="font-mono text-xs tabular-nums text-muted">
            {{ row.original.health }} / {{ row.original.maxHealth }}
          </span>
        </div>
      </template>
      <template #level-cell="{ row }">
        <span class="block text-right font-mono tabular-nums">{{ row.original.level }}</span>
      </template>
      <template #ping-cell="{ row }">
        <span class="block text-right font-mono tabular-nums">{{ row.original.ping }} ms</span>
      </template>
      <template #device-cell="{ row }">
        <span class="text-sm text-default">{{ formatDeviceType(row.original.deviceType) }}</span>
      </template>
      <template #updated-cell="{ row }">
        <span class="block text-sm text-muted">
          {{ d(new Date(row.original.observedAtUtc), 'playerObservation') }}
        </span>
      </template>
      <template #actions-cell="{ row }">
        <div class="flex items-center justify-end gap-1">
          <UTooltip :text="t('players.actions.viewDetails', { name: row.original.name })">
            <UButton
              :aria-label="t('players.actions.viewDetails', { name: row.original.name })"
              class="size-8"
              color="neutral"
              icon="i-lucide-panel-right-open"
              square
              variant="ghost"
              @click="emit('viewDetails', row.original)"
            />
          </UTooltip>
          <UTooltip v-if="canKick" :text="t('players.actions.kick')">
            <UButton
              :aria-label="t('players.actions.kickPlayer', { name: row.original.name })"
              class="size-8"
              color="error"
              icon="i-lucide-log-out"
              square
              variant="ghost"
              @click="emit('kickPlayer', row.original)"
            />
          </UTooltip>
        </div>
      </template>
    </UTable>
  </div>
</template>
