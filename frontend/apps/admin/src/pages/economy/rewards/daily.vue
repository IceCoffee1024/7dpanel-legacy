<script setup lang="ts">
import type { DailyRewardPolicyUpdateRequest } from '../../../features/rewards'

import { onUnmounted } from 'vue'

import NavigationSectionTabs from '../../../components/navigation/NavigationSectionTabs.vue'
import { DailyRewardPolicyView, useDailyRewardPolicy } from '../../../features/rewards'

const controller = useDailyRewardPolicy()

function save(draft: DailyRewardPolicyUpdateRequest) {
  controller.updateDraft(draft)
  void controller.save()
}

onUnmounted(controller.dispose)
</script>

<template>
  <NavigationSectionTabs />
  <DailyRewardPolicyView :controller="controller" @refresh="controller.load" @save="save" />
</template>

<route lang="json">
{ "meta": { "requiresAuth": true, "roles": ["Owner"] } }
</route>
