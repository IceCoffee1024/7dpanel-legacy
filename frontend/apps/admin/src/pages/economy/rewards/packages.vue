<script setup lang="ts">
import type { RewardPackageDraft } from '../../../features/rewards/api/rewards'
import NavigationSectionTabs from '../../../components/navigation/NavigationSectionTabs.vue'
import { RewardPackagesView, useRewardPackages } from '../../../features/rewards'

const controller = useRewardPackages()
function load(id: string) {
  void controller.load(id)
}
function save(draft: RewardPackageDraft) {
  void controller.save(draft)
}
</script>

<template>
  <NavigationSectionTabs />
  <RewardPackagesView :controller="controller" @load="load" @save="save" />
</template>

<route lang="json">
{ "meta": { "requiresAuth": true, "roles": ["Owner"] } }
</route>
