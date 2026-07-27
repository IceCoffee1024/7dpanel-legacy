<script setup lang="ts">
import type { GameEventsController } from '../../game-events/model/useGameEvents'
import type { AuditWorkspaceController } from '../model/useAuditWorkspace'
import { shallowRef } from 'vue'

import GameEventsTable from '../../game-events/ui/GameEventsTable.vue'
import AuditEntriesTable from './AuditEntriesTable.vue'

defineProps<{
  audit: AuditWorkspaceController
  gameEvents: GameEventsController
}>()

const activeTab = shallowRef<'audit' | 'game-events'>('audit')
const tabs = [
  { label: '统一审计', value: 'audit', slot: 'audit' as const },
  { label: '游戏事件', value: 'game-events', slot: 'game-events' as const },
]
</script>

<template>
  <UDashboardPanel id="audit-workspace">
    <template #header>
      <UDashboardNavbar title="审计与事件">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
        <template #right>
          <UButton
            icon="i-lucide-refresh-cw"
            label="刷新当前标签"
            @click="activeTab === 'audit' ? audit.refresh() : gameEvents.refresh()"
          />
        </template>
      </UDashboardNavbar>
    </template>

    <template #body>
      <UTabs v-model="activeTab" :items="tabs" class="w-full">
        <template #audit>
          <AuditEntriesTable :controller="audit" class="pt-4" />
        </template>
        <template #game-events>
          <GameEventsTable :controller="gameEvents" class="pt-4" />
        </template>
      </UTabs>
    </template>
  </UDashboardPanel>
</template>
