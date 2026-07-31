<script setup lang="ts">
import type { PlayerActionTarget } from './playerProfileUi'

import { computed, onUnmounted, shallowRef, toRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'

import { useAuthStore } from '../../auth'
import { useOnlinePlayers } from '../../players/model/useOnlinePlayers'
import { usePlayerActions } from '../model/usePlayerActions'
import { usePlayerEvidence } from '../model/usePlayerEvidence'
import { usePlayerProfile } from '../model/usePlayerProfile'
import GrantItemDialog from './GrantItemDialog.vue'
import PlayerActivityPanel from './PlayerActivityPanel.vue'
import PlayerInventoryPanel from './PlayerInventoryPanel.vue'
import PlayerProfileSummary from './PlayerProfileSummary.vue'
import PlayerSkillsPanel from './PlayerSkillsPanel.vue'
import RemoveItemDialog from './RemoveItemDialog.vue'
import ResetFullDialog from './ResetFullDialog.vue'
import ResetPartialDialog from './ResetPartialDialog.vue'
import ResetSkillsDialog from './ResetSkillsDialog.vue'

const props = defineProps<{ crossplatformId: string }>()
const router = useRouter()
const auth = useAuthStore()
const { t } = useI18n()
const crossplatformId = toRef(props, 'crossplatformId')
const onSessionExpired = () => router.replace({ path: '/login', query: { redirect: `/players/profile/${encodeURIComponent(props.crossplatformId)}` } })
const profileController = usePlayerProfile(crossplatformId, { onSessionExpired })
const evidenceController = usePlayerEvidence(crossplatformId, { onSessionExpired })
const onlineController = useOnlinePlayers({ onSessionExpired })
const profile = computed(() => profileController.profile.value)
const currentWorldId = computed(() => profile.value?.inventory?.value?.worldId
  ?? profile.value?.skills?.value?.worldId
  ?? profile.value?.sessions?.value?.find(session => session.endedAtUtc === null)?.worldId
  ?? profile.value?.activity?.value?.[0]?.worldId
  ?? null)
const freshTarget = computed<PlayerActionTarget | null>(() => {
  if (onlineController.state.value !== 'fresh' || currentWorldId.value === null)
    return null
  const player = onlineController.snapshot.value?.players.find(candidate =>
    candidate.crossplatformIdentity?.combinedId === props.crossplatformId,
  )
  if (player?.crossplatformIdentity === null || player === undefined)
    return null
  return Object.freeze({
    crossplatformId: player.crossplatformIdentity.combinedId,
    entityId: player.entityId,
    onlineObservedAtUtc: player.observedAtUtc,
    worldId: currentWorldId.value,
    name: player.name,
  })
})
const actions = usePlayerActions({ freshTarget, onSessionExpired })

type DialogName = 'grant' | 'remove' | 'reset-skills' | 'reset-partial' | 'reset-full'
const activeDialog = shallowRef<DialogName | null>(null)
const catalogVersion = computed(() => profile.value?.inventory?.value?.catalogVersion ?? null)
const canOpenActions = computed(() => auth.role === 'Owner' && freshTarget.value !== null)

function openDialog(name: DialogName) {
  if (!canOpenActions.value)
    return
  actions.clearFeedback()
  actions.lockTarget()
  activeDialog.value = name
}

function closeDialog() {
  if (actions.isSubmitting.value)
    return
  activeDialog.value = null
  actions.clearTarget()
  actions.clearFeedback()
}

async function refreshAll() {
  await Promise.all([profileController.refresh(), evidenceController.refresh(), onlineController.refresh()])
}

onUnmounted(() => {
  profileController.dispose()
  evidenceController.dispose()
  onlineController.dispose()
  actions.dispose()
})
</script>

<template>
  <UDashboardPanel id="player-profile">
    <template #header>
      <div class="flex flex-wrap items-center justify-between gap-3 p-3 sm:p-4">
        <div>
          <h1 class="text-lg font-semibold text-highlighted">
            {{ profile?.summary?.value?.latestName ?? t('players.profile.title') }}
          </h1>
          <p class="break-all text-xs text-muted">
            {{ crossplatformId }}
          </p>
        </div>
        <UButton
          color="neutral"
          icon="i-lucide-refresh-cw"
          variant="outline"
          :label="t('common.reload')"
          :loading="profileController.isRefreshing.value"
          @click="refreshAll"
        />
      </div>
    </template>
    <template #body>
      <div class="space-y-6 p-3 sm:p-4 lg:p-6">
        <div v-if="profileController.state.value === 'loading'" class="space-y-3">
          <USkeleton class="h-24 w-full" /><USkeleton class="h-48 w-full" />
        </div>
        <UAlert v-else-if="profileController.state.value === 'forbidden'" color="warning" :title="t('players.profile.state.forbiddenTitle')" />
        <UAlert v-else-if="profileController.state.value === 'unavailable' || !profile" color="error" :title="t('players.profile.state.failedTitle')" />
        <template v-else>
          <UAlert v-if="profileController.state.value === 'stale' || profileController.state.value === 'partial'" color="warning" :title="t('players.profile.state.staleTitle')" />
          <PlayerProfileSummary :profile="profile" />
          <UCard>
            <template #header>
              <div class="flex flex-wrap items-center justify-between gap-2">
                <h2 class="font-semibold">
                  {{ t('players.profile.actions.title') }}
                </h2><UBadge :color="canOpenActions ? 'success' : 'neutral'" variant="subtle">
                  {{ canOpenActions ? t('players.profile.actions.freshTarget') : t('players.profile.readOnlyNotice') }}
                </UBadge>
              </div>
            </template>
            <UAlert
              v-if="!canOpenActions"
              color="neutral"
              :title="t('players.profile.readOnlyNotice')"
              :description="t('players.profile.actions.freshRequired')"
            />
            <div v-else class="flex flex-wrap gap-2">
              <UButton icon="i-lucide-package-plus" :label="t('players.profile.actions.grant.title')" @click="openDialog('grant')" />
              <UButton
                color="error"
                variant="soft"
                icon="i-lucide-package-minus"
                :label="t('players.profile.actions.remove.title')"
                @click="openDialog('remove')"
              />
              <UButton
                color="error"
                variant="soft"
                :label="t('players.profile.actions.resetSkills.title')"
                @click="openDialog('reset-skills')"
              />
              <UButton
                color="error"
                variant="soft"
                :label="t('players.profile.actions.resetPartial.title')"
                @click="openDialog('reset-partial')"
              />
              <UButton color="error" :label="t('players.profile.actions.resetFull.title')" @click="openDialog('reset-full')" />
            </div>
          </UCard>
          <PlayerInventoryPanel
            :section="profile.inventory"
            :diffs="evidenceController.inventoryDiffs.items.value"
            :gaps="evidenceController.inventoryDiffs.gaps.value"
            @load-more="evidenceController.inventoryDiffs.loadMore"
          />
          <PlayerSkillsPanel :section="profile.skills" :snapshots="evidenceController.skills.items.value" @load-more="evidenceController.skills.loadMore" />
          <PlayerActivityPanel :sessions="profile.sessions" :activity="profile.activity" :daily-activity="profile.dailyActivity" />
        </template>
      </div>
    </template>
  </UDashboardPanel>

  <GrantItemDialog
    :open="activeDialog === 'grant'"
    :target="actions.target.value"
    :target-valid="actions.targetValid.value"
    :pending="actions.isSubmitting.value"
    :feedback="actions.feedback.value"
    :catalog-version="catalogVersion"
    @close="closeDialog"
    @submit="actions.grantItem"
  />
  <RemoveItemDialog
    :open="activeDialog === 'remove'"
    :target="actions.target.value"
    :target-valid="actions.targetValid.value"
    :pending="actions.isSubmitting.value"
    :feedback="actions.feedback.value"
    :catalog-version="catalogVersion"
    @close="closeDialog"
    @submit="actions.removeItem"
  />
  <ResetSkillsDialog
    :open="activeDialog === 'reset-skills'"
    :target="actions.target.value"
    :target-valid="actions.targetValid.value"
    :pending="actions.isSubmitting.value"
    :feedback="actions.feedback.value"
    @close="closeDialog"
    @submit="actions.resetSkills"
  />
  <ResetPartialDialog
    :open="activeDialog === 'reset-partial'"
    :target="actions.target.value"
    :target-valid="actions.targetValid.value"
    :pending="actions.isSubmitting.value"
    :feedback="actions.feedback.value"
    @close="closeDialog"
    @submit="actions.clearInventory"
  />
  <ResetFullDialog
    :open="activeDialog === 'reset-full'"
    :target="actions.target.value"
    :target-valid="actions.targetValid.value"
    :pending="actions.isSubmitting.value"
    :feedback="actions.feedback.value"
    @close="closeDialog"
    @submit="actions.resetPlayerData"
  />
</template>
