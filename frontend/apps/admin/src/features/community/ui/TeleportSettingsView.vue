<script setup lang="ts">
import type {
  CommunityGameCommandConfiguration,
  CommunityGameCommandConfigurationInput,
  TeleportSettings,
  TeleportSettingsInput,
} from '../api/community'
import type { CommunityController } from '../model/useCommunity'

import { shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'

import CommunityMutationAlert from './CommunityMutationAlert.vue'
import CommunityStateAlert from './CommunityStateAlert.vue'
import FriendshipRecordsList from './FriendshipRecordsList.vue'
import GameCommandConfigurationForm from './GameCommandConfigurationForm.vue'
import TeleportOperationsList from './TeleportOperationsList.vue'
import TeleportSettingForm from './TeleportSettingForm.vue'

const props = defineProps<{ controller: CommunityController }>()
const emit = defineEmits<{
  refresh: []
  saveGameCommands: [current: CommunityGameCommandConfiguration, input: CommunityGameCommandConfigurationInput]
  save: [current: TeleportSettings, input: TeleportSettingsInput]
  queryHomes: [crossplatformId: string]
  queryFriendship: [firstCrossplatformId: string, secondCrossplatformId: string]
  queryOperation: [operationId: string]
  dismissMutation: []
}>()
const { t } = useI18n()
const homePlayerId = shallowRef('')
const firstFriendId = shallowRef('')
const secondFriendId = shallowRef('')
const operationId = shallowRef('')

function submitHomes() {
  const value = homePlayerId.value.trim()
  if (value !== '')
    emit('queryHomes', value)
}

function submitFriendship() {
  const first = firstFriendId.value.trim()
  const second = secondFriendId.value.trim()
  if (first !== '' && second !== '')
    emit('queryFriendship', first, second)
}

function submitOperation() {
  const value = operationId.value.trim()
  if (value !== '')
    emit('queryOperation', value)
}

function saveSetting(current: TeleportSettings, input: TeleportSettingsInput) {
  emit('save', current, input)
}

function saveGameCommands(current: CommunityGameCommandConfiguration, input: CommunityGameCommandConfigurationInput) {
  emit('saveGameCommands', current, input)
}

function operationColor(state: string) {
  if (state === 'Completed')
    return 'success' as const
  if (state === 'Failed' || state === 'Refunded')
    return 'error' as const
  if (state === 'PendingReconciliation')
    return 'warning' as const
  return 'neutral' as const
}
</script>

<template>
  <UDashboardPanel id="community-teleport">
    <template #header>
      <UDashboardNavbar :title="t('community.teleport.title')">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
        <template #right>
          <UButton
            color="neutral"
            icon="i-lucide-refresh-cw"
            :label="t('community.teleport.refresh')"
            variant="outline"
            :loading="props.controller.gameCommandConfigurationState.value === 'loading' || props.controller.teleportSettingsState.value === 'loading' || props.controller.friendshipRecordsState.value === 'loading' || props.controller.teleportOperationsState.value === 'loading'"
            @click="emit('refresh')"
          />
        </template>
      </UDashboardNavbar>
    </template>

    <template #body>
      <UContainer class="space-y-5 py-5">
        <UAlert
          color="neutral"
          icon="i-lucide-info"
          :title="t('community.teleport.verifiedDataTitle')"
          :description="t('community.teleport.verifiedDataDescription')"
        />
        <CommunityMutationAlert :state="props.controller.mutationState.value" @dismiss="emit('dismissMutation')" />
        <CommunityStateAlert
          :state="props.controller.teleportSettingsState.value"
          :subject="t('community.teleport.settingsSubject')"
          @retry="emit('refresh')"
        />

        <section class="space-y-3">
          <CommunityStateAlert
            :state="props.controller.gameCommandConfigurationState.value"
            :subject="t('community.gameCommands.subject')"
            @retry="emit('refresh')"
          />
          <div v-if="props.controller.gameCommandConfigurationState.value === 'loading' && props.controller.gameCommandConfiguration.value === null">
            <USkeleton class="h-96 w-full" />
          </div>
          <GameCommandConfigurationForm
            v-else-if="props.controller.gameCommandConfiguration.value !== null && props.controller.gameCommandConfigurationState.value !== 'forbidden' && props.controller.gameCommandConfigurationState.value !== 'unavailable'"
            :configuration="props.controller.gameCommandConfiguration.value"
            :saving="props.controller.mutationTarget.value?.kind === 'game-command-configuration'"
            @save="saveGameCommands"
          />
        </section>

        <section class="space-y-3" aria-labelledby="teleport-settings-heading">
          <div>
            <h2 id="teleport-settings-heading" class="text-base font-semibold text-highlighted">
              {{ t('community.teleport.rulesTitle') }}
            </h2>
            <p class="text-sm text-muted">
              {{ t('community.teleport.rulesDescription') }}
            </p>
          </div>
          <div v-if="props.controller.teleportSettingsState.value === 'loading' && props.controller.teleportSettings.value.length === 0" class="space-y-3">
            <USkeleton v-for="row in 3" :key="row" class="h-64 w-full" />
          </div>
          <UCard v-else-if="props.controller.teleportSettingsState.value === 'empty'">
            <p class="text-sm text-muted">
              {{ t('community.teleport.settingsEmpty') }}
            </p>
          </UCard>
          <div v-else-if="props.controller.teleportSettingsState.value !== 'forbidden' && props.controller.teleportSettingsState.value !== 'unavailable'" class="grid gap-4 xl:grid-cols-2">
            <TeleportSettingForm
              v-for="setting in props.controller.teleportSettings.value"
              :key="setting.kind"
              :setting="setting"
              :saving="props.controller.mutationTarget.value?.kind === 'teleport-setting' && props.controller.mutationTarget.value.id === setting.kind"
              @save="saveSetting"
            />
          </div>
        </section>

        <FriendshipRecordsList
          :records="props.controller.friendshipRecords.value"
          :state="props.controller.friendshipRecordsState.value"
          @retry="emit('refresh')"
        />

        <TeleportOperationsList
          :operations="props.controller.teleportOperations.value"
          :state="props.controller.teleportOperationsState.value"
          @retry="emit('refresh')"
        />

        <section class="space-y-3" aria-labelledby="teleport-query-heading">
          <div>
            <h2 id="teleport-query-heading" class="text-base font-semibold text-highlighted">
              {{ t('community.teleport.queriesTitle') }}
            </h2>
            <p class="text-sm text-muted">
              {{ t('community.teleport.queriesDescription') }}
            </p>
          </div>

          <div class="grid gap-4 xl:grid-cols-3">
            <UCard>
              <template #header>
                <h3 class="font-semibold">
                  {{ t('community.teleport.homesTitle') }}
                </h3>
              </template>
              <form class="flex min-w-0 flex-col gap-3 sm:flex-row" @submit.prevent="submitHomes">
                <UFormField class="min-w-0 flex-1" :label="t('community.teleport.playerIdentity')" required>
                  <UInput v-model="homePlayerId" class="w-full" />
                </UFormField>
                <UButton
                  class="self-end"
                  :label="t('community.common.query')"
                  :disabled="homePlayerId.trim() === ''"
                  :loading="props.controller.homesState.value === 'loading'"
                  type="submit"
                />
              </form>
            </UCard>

            <UCard>
              <template #header>
                <h3 class="font-semibold">
                  {{ t('community.teleport.friendshipTitle') }}
                </h3>
              </template>
              <form class="grid gap-3 sm:grid-cols-2" @submit.prevent="submitFriendship">
                <UFormField :label="t('community.teleport.playerA')" required>
                  <UInput v-model="firstFriendId" class="w-full" />
                </UFormField>
                <UFormField :label="t('community.teleport.playerB')" required>
                  <UInput v-model="secondFriendId" class="w-full" />
                </UFormField>
                <UButton
                  class="sm:col-span-2 sm:justify-self-end"
                  :label="t('community.common.query')"
                  :disabled="firstFriendId.trim() === '' || secondFriendId.trim() === ''"
                  :loading="props.controller.friendshipState.value === 'loading'"
                  type="submit"
                />
              </form>
            </UCard>

            <UCard>
              <template #header>
                <h3 class="font-semibold">
                  {{ t('community.teleport.operationTitle') }}
                </h3>
              </template>
              <form class="flex min-w-0 flex-col gap-3 sm:flex-row" @submit.prevent="submitOperation">
                <UFormField class="min-w-0 flex-1" :label="t('community.teleport.operationId')" required>
                  <UInput v-model="operationId" class="w-full" />
                </UFormField>
                <UButton
                  class="self-end"
                  :label="t('community.common.query')"
                  :disabled="operationId.trim() === ''"
                  :loading="props.controller.teleportOperationState.value === 'loading'"
                  type="submit"
                />
              </form>
            </UCard>
          </div>

          <CommunityStateAlert :state="props.controller.homesState.value" :subject="t('community.teleport.homesSubject')" @retry="submitHomes" />
          <div v-if="props.controller.homesState.value === 'empty'" class="text-sm text-muted">
            {{ t('community.teleport.homesEmpty') }}
          </div>
          <div v-else-if="props.controller.homesState.value !== 'forbidden' && props.controller.homes.value.length > 0" class="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
            <UCard v-for="home in props.controller.homes.value" :key="home.homeId">
              <template #header>
                <div>
                  <h3 class="font-semibold">
                    {{ home.name }}
                  </h3><p class="break-all text-xs text-muted">
                    {{ home.crossplatformId }}
                  </p>
                </div>
              </template>
              <p class="text-sm">
                {{ home.position.worldId }} · {{ t('community.common.coordinates', { x: home.position.x, y: home.position.y, z: home.position.z }) }}
              </p>
            </UCard>
          </div>

          <CommunityStateAlert :state="props.controller.friendshipState.value" :subject="t('community.teleport.friendshipSubject')" @retry="submitFriendship" />
          <UAlert
            v-if="props.controller.friendshipState.value !== 'forbidden' && props.controller.friendship.value"
            :color="props.controller.friendship.value.areFriends ? 'success' : 'neutral'"
            :title="props.controller.friendship.value.areFriends ? t('community.teleport.areFriends') : t('community.teleport.areNotFriends')"
            :description="`${props.controller.friendship.value.firstCrossplatformId} ↔ ${props.controller.friendship.value.secondCrossplatformId}`"
          />

          <CommunityStateAlert :state="props.controller.teleportOperationState.value" :subject="t('community.teleport.operationSubject')" @retry="submitOperation" />
          <UCard v-if="props.controller.teleportOperationState.value !== 'forbidden' && props.controller.teleportOperation.value">
            <template #header>
              <div class="flex min-w-0 flex-wrap items-start justify-between gap-2">
                <div class="min-w-0">
                  <h3 class="break-all font-semibold">
                    {{ props.controller.teleportOperation.value.operationId }}
                  </h3><p class="text-xs text-muted">
                    {{ props.controller.teleportOperation.value.kind }}
                  </p>
                </div>
                <UBadge :color="operationColor(props.controller.teleportOperation.value.state)" variant="subtle">
                  {{ props.controller.teleportOperation.value.state === 'PendingReconciliation' ? t('community.teleport.pendingReconciliation') : props.controller.teleportOperation.value.state }}
                </UBadge>
              </div>
            </template>
            <UAlert
              v-if="props.controller.teleportOperation.value.state === 'PendingReconciliation'"
              class="mb-4"
              color="warning"
              icon="i-lucide-triangle-alert"
              :title="t('community.teleport.pendingReconciliationTitle')"
              :description="t('community.teleport.pendingReconciliationDescription')"
            />
            <dl class="grid gap-3 text-sm sm:grid-cols-2 xl:grid-cols-4">
              <div>
                <dt class="text-muted">
                  {{ t('community.teleport.player') }}
                </dt><dd class="mt-1 break-all">
                  {{ props.controller.teleportOperation.value.crossplatformId }}
                </dd>
              </div>
              <div>
                <dt class="text-muted">
                  {{ t('community.teleport.updatedAt') }}
                </dt><dd class="mt-1 break-all">
                  {{ props.controller.teleportOperation.value.updatedAtUtc }}
                </dd>
              </div>
              <div>
                <dt class="text-muted">
                  {{ t('community.teleport.destinationWorld') }}
                </dt><dd class="mt-1">
                  {{ props.controller.teleportOperation.value.destination.worldId }}
                </dd>
              </div>
              <div>
                <dt class="text-muted">
                  {{ t('community.teleport.destinationCoordinates') }}
                </dt><dd class="mt-1">
                  {{ t('community.common.coordinates', { x: props.controller.teleportOperation.value.destination.x, y: props.controller.teleportOperation.value.destination.y, z: props.controller.teleportOperation.value.destination.z }) }}
                </dd>
              </div>
            </dl>
          </UCard>
        </section>
      </UContainer>
    </template>
  </UDashboardPanel>
</template>
