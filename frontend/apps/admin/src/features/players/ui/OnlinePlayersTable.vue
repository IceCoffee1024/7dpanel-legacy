<script setup lang="ts">
import type { DropdownMenuItem, TableColumn } from '@nuxt/ui'
import type { OnlinePlayer } from '../api/onlinePlayers'

import { computed } from 'vue'

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

const columns: TableColumn<OnlinePlayer>[] = [
  { accessorKey: 'name', header: '玩家' },
  { id: 'platform', header: '平台' },
  { id: 'crossplatform', header: '跨平台身份' },
  { accessorKey: 'level', header: '等级' },
  { accessorKey: 'health', header: '生命值' },
  { accessorKey: 'ping', header: '延迟' },
  { id: 'actions', header: '操作' },
]

const tableData = computed(() => props.players as OnlinePlayer[])

function playerActions(player: OnlinePlayer): DropdownMenuItem[] {
  return [{
    label: '踢出玩家',
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
        </div>
      </template>

      <template #platform-cell="{ row }">
        <div class="identity-value">
          <span class="text-xs font-medium text-muted">{{ row.original.platformIdentity.platform }}</span>
          <div class="identity-value__content">
            <code>{{ row.original.platformIdentity.combinedId }}</code>
            <UButton
              :aria-label="`复制 ${row.original.platformIdentity.platform} 身份`"
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
              :aria-label="`复制 ${row.original.crossplatformIdentity.platform} 身份`"
              color="neutral"
              icon="i-lucide-copy"
              size="xs"
              square
              variant="ghost"
              @click="$emit('copyIdentity', row.original.crossplatformIdentity.combinedId)"
            />
          </div>
        </div>
        <span v-else class="text-sm text-dimmed">未绑定</span>
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
            :aria-label="`玩家操作：${row.original.name}`"
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
