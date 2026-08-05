<script setup lang="ts">
import type { AchievementDefinitionDraft, ManualOnlineRewardInput, OnlineRewardRuleDraft } from '../../../features/commerce/api/commerce'
import NavigationSectionTabs from '../../../components/navigation/NavigationSectionTabs.vue'
import { AchievementOnlineRewardsView, useAchievementOnlineRewards } from '../../../features/commerce'

const controller = useAchievementOnlineRewards()
function saveAchievement(draft: AchievementDefinitionDraft) {
  void controller.saveAchievement(draft)
}
function loadAchievementRecord(id: string, player: string) {
  void controller.loadAchievementRecord(id, player)
}
function saveRule(draft: OnlineRewardRuleDraft) {
  void controller.saveRule(draft)
}
function loadRecords(id: string, player: string) {
  void controller.loadRecords(id, player)
}
function manualGrant(input: ManualOnlineRewardInput) {
  void controller.manualGrant(input)
}
</script>

<template>
  <NavigationSectionTabs />
  <AchievementOnlineRewardsView
    :controller="controller"
    @save-achievement="saveAchievement"
    @load-achievement-record="loadAchievementRecord"
    @save-rule="saveRule"
    @load-records="loadRecords"
    @manual-grant="manualGrant"
  />
</template>

<route lang="json">
{ "meta": { "requiresAuth": true, "roles": ["Owner"] } }
</route>
