<route lang="json">
{
  "meta": {
    "requiresAuth": true,
    "roles": ["Owner"]
  }
}
</route>

<script setup lang="ts">
import type {
  CommunityGameCommandConfiguration,
  CommunityGameCommandConfigurationInput,
  TeleportSettings,
  TeleportSettingsInput,
} from '../../features/community'

import { onMounted, onUnmounted } from 'vue'

import { TeleportSettingsView, useCommunity } from '../../features/community'

const controller = useCommunity()

function refresh() {
  void Promise.all([
    controller.loadGameCommandConfiguration(),
    controller.loadTeleportSettings(),
    controller.loadFriendshipRecords(),
    controller.loadTeleportOperations(),
  ])
}

async function save(current: TeleportSettings, input: TeleportSettingsInput) {
  if (await controller.saveTeleportSetting(current, input) && current.kind === 'Home')
    await controller.loadGameCommandConfiguration()
}

async function saveGameCommands(
  current: CommunityGameCommandConfiguration,
  input: CommunityGameCommandConfigurationInput,
) {
  if (await controller.saveGameCommandConfiguration(current, input))
    await controller.loadTeleportSettings()
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
    @save-game-commands="saveGameCommands"
  />
</template>
