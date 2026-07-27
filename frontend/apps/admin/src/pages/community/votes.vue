<route lang="json">
{
  "meta": {
    "requiresAuth": true,
    "roles": ["Owner"]
  }
}
</route>

<script setup lang="ts">
import type { VoteConfiguration, VoteConfigurationInput } from '../../features/community'

import { onMounted, onUnmounted } from 'vue'

import { useCommunity, VoteConfigurationView } from '../../features/community'

const controller = useCommunity()

function refresh() {
  void Promise.all([
    controller.loadVoteConfigurations(),
    controller.loadVoteRounds(),
    controller.loadAllVoteRounds(),
  ])
}

function save(current: VoteConfiguration, input: VoteConfigurationInput) {
  void controller.saveVoteConfiguration(current, input)
}

function queryRound(roundId: string) {
  void controller.queryVoteRound(roundId)
}

async function settle(roundId: string) {
  if (await controller.settleVote(roundId))
    await controller.loadAllVoteRounds()
}

onMounted(refresh)
onUnmounted(controller.dispose)
</script>

<template>
  <VoteConfigurationView
    :controller="controller"
    @dismiss-mutation="controller.clearMutationState"
    @query-round="queryRound"
    @refresh="refresh"
    @save="save"
    @settle="settle"
  />
</template>
