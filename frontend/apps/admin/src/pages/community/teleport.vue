<route lang="json">
{
  "meta": {
    "requiresAuth": true,
    "roles": ["Owner"]
  }
}
</route>

<script setup lang="ts">
import type { TeleportSettings, TeleportSettingsInput } from '../../features/community'

import { onMounted, onUnmounted } from 'vue'

import { TeleportSettingsView, useCommunity } from '../../features/community'

const controller = useCommunity()

function refresh() {
  void Promise.all([
    controller.loadTeleportSettings(),
    controller.loadFriendshipRecords(),
    controller.loadTeleportOperations(),
  ])
}

function save(current: TeleportSettings, input: TeleportSettingsInput) {
  void controller.saveTeleportSetting(current, input)
}

function queryHomes(crossplatformId: string) {
  void controller.queryHomes(crossplatformId)
}

function queryFriendship(firstCrossplatformId: string, secondCrossplatformId: string) {
  void controller.queryFriendship(firstCrossplatformId, secondCrossplatformId)
}

function queryOperation(operationId: string) {
  void controller.queryTeleportOperation(operationId)
}

onMounted(refresh)
onUnmounted(controller.dispose)
</script>

<template>
  <TeleportSettingsView
    :controller="controller"
    @dismiss-mutation="controller.clearMutationState"
    @query-friendship="queryFriendship"
    @query-homes="queryHomes"
    @query-operation="queryOperation"
    @refresh="refresh"
    @save="save"
  />
</template>
