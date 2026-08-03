<script setup lang="ts">
import { computed } from 'vue'

import { useAuthStore } from '../../auth'
import { useOverview } from '../../server-status/model/useOverview'
import OverviewStatusSummary from '../../server-status/ui/OverviewStatusSummary.vue'
import { useRestartServer } from '../model/useRestartServer'
import { useShutdownServer } from '../model/useShutdownServer'
import QuickActionsPanel from './QuickActionsPanel.vue'
import RestartPolicySummary from './RestartPolicySummary.vue'
import RestartServerDialog from './RestartServerDialog.vue'
import ShutdownServerDialog from './ShutdownServerDialog.vue'

const auth = useAuthStore()
const overview = useOverview()
const restart = useRestartServer()
const shutdown = useShutdownServer()
const isOwner = computed(() => auth.role === 'Owner')
</script>

<template>
  <div class="mx-auto w-full max-w-7xl space-y-4 sm:space-y-6">
    <OverviewStatusSummary
      :status="overview.status.value"
      :game="overview.snapshot.value?.game ?? null"
      :host="overview.snapshot.value?.host ?? null"
      @refresh="overview.refresh"
    />

    <div v-if="overview.snapshot.value" class="grid gap-4 sm:gap-6 lg:grid-cols-2">
      <RestartPolicySummary :policy="overview.snapshot.value.restartPolicy" />
      <QuickActionsPanel
        v-if="isOwner"
        @restart="restart.startConfirmation"
        @shutdown="shutdown.startConfirmation"
      />
    </div>

    <RestartServerDialog
      v-if="isOwner"
      :state="restart.state.value"
      :error-code="restart.error.value?.code ?? null"
      @cancel="restart.cancelConfirmation"
      @confirm="restart.confirm"
    />
    <ShutdownServerDialog
      v-if="isOwner"
      :state="shutdown.state.value"
      :error-code="shutdown.error.value?.code ?? null"
      @cancel="shutdown.cancelConfirmation"
      @confirm="shutdown.confirm"
    />
  </div>
</template>
