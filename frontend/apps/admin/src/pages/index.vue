<route lang="json">
{ "meta": { "requiresAuth": true } }
</route>

<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useAuthStore } from '../features/auth'
import RestartPolicySummary from '../features/server-operations/ui/RestartPolicySummary.vue'
import QuickActionsPanel from '../features/server-operations/ui/QuickActionsPanel.vue'
import RestartServerDialog from '../features/server-operations/ui/RestartServerDialog.vue'
import ShutdownServerDialog from '../features/server-operations/ui/ShutdownServerDialog.vue'
import { useRestartServer } from '../features/server-operations/model/useRestartServer'
import { useShutdownServer } from '../features/server-operations/model/useShutdownServer'
import AttentionPanel from '../features/server-status/ui/AttentionPanel.vue'
import HostPlatformPanel from '../features/server-status/ui/HostPlatformPanel.vue'
import OverviewStatusSummary from '../features/server-status/ui/OverviewStatusSummary.vue'
import RecentActivityPanel from '../features/server-status/ui/RecentActivityPanel.vue'
import ResourceCapacityPanel from '../features/server-status/ui/ResourceCapacityPanel.vue'
import ServerInformationPanel from '../features/server-status/ui/ServerInformationPanel.vue'
import { useOverview } from '../features/server-status/model/useOverview'

const { t } = useI18n()
const auth = useAuthStore()
const overview = useOverview()
const restart = useRestartServer()
const shutdown = useShutdownServer()
const isOwner = computed(() => auth.role === 'Owner')
</script>

<template>
  <UDashboardPanel id="overview">
    <template #header><UDashboardNavbar :title="t('overview.title')"><template #leading><UDashboardSidebarCollapse /></template></UDashboardNavbar></template>
    <template #body>
      <div class="mx-auto w-full max-w-7xl space-y-4 sm:space-y-6">
        <OverviewStatusSummary :status="overview.status.value" :game="overview.snapshot.value?.game ?? null" :host="overview.snapshot.value?.host ?? null" @refresh="overview.refresh" />

        <div v-if="overview.snapshot.value" class="grid gap-4 sm:gap-6 lg:grid-cols-2">
          <ServerInformationPanel :game="overview.snapshot.value.game" />
          <HostPlatformPanel :host="overview.snapshot.value.host" :is-owner="isOwner" />
          <ResourceCapacityPanel class="lg:col-span-2" :host="overview.snapshot.value.host" :is-owner="isOwner" />
          <AttentionPanel :attention="overview.snapshot.value.attention" />
          <RecentActivityPanel :activity="overview.snapshot.value.recentActivity" />
          <RestartPolicySummary :policy="overview.snapshot.value.restartPolicy" />
          <QuickActionsPanel v-if="isOwner" @restart="restart.startConfirmation" @shutdown="shutdown.startConfirmation" />
        </div>
        <div v-else-if="overview.status.value === 'loading'" class="grid gap-4 sm:grid-cols-2"><UCard v-for="index in 4" :key="index" class="rounded-md"><USkeleton class="h-40 rounded-md" /></UCard></div>

        <RestartServerDialog v-if="isOwner" :state="restart.state.value" :error-code="restart.error.value?.code ?? null" @cancel="restart.cancelConfirmation" @confirm="restart.confirm" />
        <ShutdownServerDialog v-if="isOwner" :state="shutdown.state.value" :error-code="shutdown.error.value?.code ?? null" @cancel="shutdown.cancelConfirmation" @confirm="shutdown.confirm" />
      </div>
    </template>
  </UDashboardPanel>
</template>
