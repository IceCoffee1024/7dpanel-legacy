<script setup lang="ts">
import type { DropdownMenuItem, TableColumn } from '@nuxt/ui'
import type { OnlinePlayer } from '../api/onlinePlayers'

import { computed } from 'vue'
import { useI18n } from 'vue-i18n'

import { isOnlinePlayerObservationStale } from '../model/onlinePlayerFreshness'

const props = withDefaults(defineProps<{
  players: readonly OnlinePlayer[]
  canKick?: boolean
}>(), {
  canKick: true,
})

const emit = defineEmits<{
  copyIdentity: [combinedId: string]
  kickPlayer: [player: OnlinePlayer]
}>()
const { d, t } = useI18n()

const columns = computed<TableColumn<OnlinePlayer>[]>(() => [
  { accessorKey: 'name', header: t('players.fields.player') },
  { id: 'platform', header: t('players.fields.platform') },
  { id: 'crossplatform', header: t('players.fields.crossplatformIdentity') },
  { accessorKey: 'level', header: t('players.fields.level') },
  { accessorKey: 'health', header: t('players.fields.health') },
  { accessorKey: 'ping', header: t('players.fields.ping') },
  { id: 'actions', header: t('players.fields.actions') },
])

const tableData = computed(() => props.players as OnlinePlayer[])

function playerActions(player: OnlinePlayer): DropdownMenuItem[] {
  return [{
    label: t('players.actions.kick'),
    icon: 'i-lucide-log-out',
    onSelect: () => emit('kickPlayer', player),
  }]
}
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
          <p class="mt-1 text-xs text-muted">
            {{ t('players.fields.updatedAt', { time: d(new Date(row.original.observedAtUtc), 'playerObservation') }) }}
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

      <template #platform-cell="{ row }">
        <div class="identity-value">
          <span class="text-xs font-medium text-muted">{{ row.original.platformIdentity.platform }}</span>
          <div class="identity-value__content">
            <code>{{ row.original.platformIdentity.combinedId }}</code>
            <UButton
              :aria-label="t('players.actions.copyIdentity', { platform: row.original.platformIdentity.platform })"
              color="neutral"
              :data-testid="`copy-platform-identity-table-${row.original.entityId}`"
              icon="i-lucide-copy"
              size="xs"
              square
              variant="ghost"
              @click="$emit('copyIdentity', row.original.platformIdentity.combinedId)"
            />
          </div>
        </div>
      </template>

      <template #crossplatform-cell="{ row }">
        <div v-if="row.original.crossplatformIdentity" class="identity-value">
          <span class="text-xs font-medium text-muted">{{ row.original.crossplatformIdentity.platform }}</span>
          <div class="identity-value__content">
            <code>{{ row.original.crossplatformIdentity.combinedId }}</code>
            <UButton
              :aria-label="t('players.actions.copyIdentity', { platform: row.original.crossplatformIdentity.platform })"
              color="neutral"
              icon="i-lucide-copy"
              size="xs"
              square
              variant="ghost"
              @click="$emit('copyIdentity', row.original.crossplatformIdentity.combinedId)"
            />
          </div>
        </div>
        <span v-else class="text-sm text-dimmed">{{ t('players.fields.unbound') }}</span>
      </template>

      <template #level-cell="{ row }">
        <span class="block text-right font-mono tabular-nums">{{ row.original.level }}</span>
      </template>
      <template #health-cell="{ row }">
        <span class="block text-right font-mono tabular-nums">{{ row.original.health }}</span>
      </template>
      <template #ping-cell="{ row }">
        <span class="block text-right font-mono tabular-nums">{{ row.original.ping }} ms</span>
      </template>
      <template #actions-cell="{ row }">
        <UDropdownMenu v-if="canKick" :items="playerActions(row.original)">
          <UButton
            :aria-label="t('players.actions.playerActions', { name: row.original.name })"
            class="size-8"
            color="neutral"
            icon="i-lucide-ellipsis-vertical"
            square
            variant="ghost"
          />
        </UDropdownMenu>
      </template>
    </UTable>
  </div>
</template>

<style scoped>
.identity-value {
  min-width: 0;
}

.identity-value__content {
  display: flex;
  min-width: 0;
  align-items: flex-start;
  gap: 0.25rem;
  margin-top: 0.25rem;
}

.identity-value__content code {
  min-width: 0;
  overflow-wrap: anywhere;
  color: var(--ui-text-highlighted);
  font-size: 0.75rem;
  line-height: 1rem;
}
</style>
