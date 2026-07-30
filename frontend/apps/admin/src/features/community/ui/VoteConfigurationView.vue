<script setup lang="ts">
import type { VoteConfiguration, VoteConfigurationInput } from '../api/community'
import type { CommunityController } from '../model/useCommunity'

import { shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'

import CommunityMutationAlert from './CommunityMutationAlert.vue'
import CommunityStateAlert from './CommunityStateAlert.vue'
import VoteConfigurationForm from './VoteConfigurationForm.vue'
import VoteRoundCard from './VoteRoundCard.vue'

const props = defineProps<{ controller: CommunityController }>()
const emit = defineEmits<{
  refresh: []
  save: [current: VoteConfiguration, input: VoteConfigurationInput]
  queryRound: [roundId: string]
  settle: [roundId: string]
  dismissMutation: []
}>()
const { t } = useI18n()
const roundId = shallowRef('')

function submitRoundQuery() {
  const value = roundId.value.trim()
  if (value !== '')
    emit('queryRound', value)
}

function saveConfiguration(current: VoteConfiguration, input: VoteConfigurationInput) {
  emit('save', current, input)
}
</script>

<template>
  <UDashboardPanel id="community-votes">
    <template #header>
      <UDashboardNavbar :title="t('community.votes.title')">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
        <template #right>
          <UButton
            color="neutral"
            icon="i-lucide-refresh-cw"
            :label="t('community.votes.refresh')"
            variant="outline"
            :loading="props.controller.voteConfigurationsState.value === 'loading' || props.controller.voteRoundsState.value === 'loading' || props.controller.fullVoteRoundListState.value === 'loading'"
            @click="emit('refresh')"
          />
        </template>
      </UDashboardNavbar>
    </template>

    <template #body>
      <UContainer class="space-y-5 py-5">
        <CommunityMutationAlert :state="props.controller.mutationState.value" @dismiss="emit('dismissMutation')" />

        <section class="space-y-3" aria-labelledby="vote-configurations-heading">
          <div>
            <h2 id="vote-configurations-heading" class="text-base font-semibold text-highlighted">
              {{ t('community.votes.configurationTitle') }}
            </h2>
            <p class="text-sm text-muted">
              {{ t('community.votes.configurationDescription') }}
            </p>
          </div>
          <CommunityStateAlert :state="props.controller.voteConfigurationsState.value" :subject="t('community.votes.configurationSubject')" @retry="emit('refresh')" />
          <div v-if="props.controller.voteConfigurationsState.value === 'loading' && props.controller.voteConfigurations.value.length === 0" class="space-y-3">
            <USkeleton v-for="row in 2" :key="row" class="h-72 w-full" />
          </div>
          <UCard v-else-if="props.controller.voteConfigurationsState.value === 'empty'">
            <p class="text-sm text-muted">
              {{ t('community.votes.configurationEmpty') }}
            </p>
          </UCard>
          <div v-else-if="props.controller.voteConfigurationsState.value !== 'forbidden' && props.controller.voteConfigurationsState.value !== 'unavailable'" class="grid gap-4 xl:grid-cols-2">
            <VoteConfigurationForm
              v-for="configuration in props.controller.voteConfigurations.value"
              :key="configuration.kind"
              :configuration="configuration"
              :saving="props.controller.mutationTarget.value?.kind === 'vote-configuration' && props.controller.mutationTarget.value.id === configuration.kind"
              @save="saveConfiguration"
            />
          </div>
        </section>

        <section class="space-y-3" aria-labelledby="vote-history-heading">
          <div>
            <h2 id="vote-history-heading" class="text-base font-semibold text-highlighted">
              {{ t('community.votes.historyTitle') }}
            </h2>
            <p class="text-sm text-muted">
              {{ t('community.votes.historyDescription') }}
            </p>
          </div>
          <CommunityStateAlert :state="props.controller.fullVoteRoundListState.value" :subject="t('community.votes.historySubject')" @retry="emit('refresh')" />
          <div v-if="props.controller.fullVoteRoundListState.value === 'loading' && props.controller.fullVoteRounds.value.length === 0" class="space-y-3">
            <USkeleton v-for="row in 2" :key="row" class="h-44 w-full" />
          </div>
          <UCard v-else-if="props.controller.fullVoteRoundListState.value === 'empty'">
            <p class="text-sm text-muted">
              {{ t('community.votes.historyEmpty') }}
            </p>
          </UCard>
          <div v-else-if="props.controller.fullVoteRoundListState.value !== 'forbidden' && props.controller.fullVoteRoundListState.value !== 'unavailable'" class="grid gap-3 xl:grid-cols-2">
            <VoteRoundCard v-for="round in props.controller.fullVoteRounds.value" :key="round.roundId" :round="round" />
          </div>
        </section>

        <section class="space-y-3" aria-labelledby="queued-votes-heading">
          <div>
            <h2 id="queued-votes-heading" class="text-base font-semibold text-highlighted">
              {{ t('community.votes.queuedTitle') }}
            </h2>
            <p class="text-sm text-muted">
              {{ t('community.votes.queuedDescription') }}
            </p>
          </div>
          <CommunityStateAlert :state="props.controller.voteRoundsState.value" :subject="t('community.votes.queuedSubject')" @retry="emit('refresh')" />
          <div v-if="props.controller.voteRoundsState.value === 'loading' && props.controller.voteRounds.value.length === 0" class="space-y-3">
            <USkeleton v-for="row in 2" :key="row" class="h-44 w-full" />
          </div>
          <UCard v-else-if="props.controller.voteRoundsState.value === 'empty'">
            <p class="text-sm text-muted">
              {{ t('community.votes.queuedEmpty') }}
            </p>
          </UCard>
          <div v-else-if="props.controller.voteRoundsState.value !== 'forbidden' && props.controller.voteRoundsState.value !== 'unavailable'" class="grid gap-3 xl:grid-cols-2">
            <VoteRoundCard v-for="round in props.controller.voteRounds.value" :key="round.roundId" :round="round" />
          </div>
        </section>

        <section class="space-y-3" aria-labelledby="vote-round-query-heading">
          <div>
            <h2 id="vote-round-query-heading" class="text-base font-semibold text-highlighted">
              {{ t('community.votes.queryTitle') }}
            </h2>
            <p class="text-sm text-muted">
              {{ t('community.votes.queryDescription') }}
            </p>
          </div>
          <UCard>
            <form class="flex min-w-0 flex-col gap-3 sm:flex-row" @submit.prevent="submitRoundQuery">
              <UFormField class="min-w-0 flex-1" :label="t('community.votes.roundId')" required>
                <UInput v-model="roundId" class="w-full" />
              </UFormField>
              <UButton
                class="self-end"
                :label="t('community.votes.queryRound')"
                :disabled="roundId.trim() === ''"
                :loading="props.controller.voteRoundState.value === 'loading'"
                type="submit"
              />
            </form>
          </UCard>
          <CommunityStateAlert :state="props.controller.voteRoundState.value" :subject="t('community.votes.roundSubject')" @retry="submitRoundQuery" />
          <VoteRoundCard
            v-if="props.controller.voteRoundState.value !== 'forbidden' && props.controller.voteRound.value"
            allow-settle
            :round="props.controller.voteRound.value"
            :settling="props.controller.mutationTarget.value?.kind === 'vote-settlement'"
            @settle="emit('settle', $event)"
          />
          <UAlert
            v-if="props.controller.settlement.value"
            :color="props.controller.settlement.value.status === 'Settled' ? 'success' : 'neutral'"
            :title="props.controller.settlement.value.status === 'Settled' ? t('community.votes.settlementCompleted') : t('community.votes.alreadySettled')"
            :description="t('community.votes.settlementCounts', { participantCount: props.controller.settlement.value.participantCount, yesCount: props.controller.settlement.value.yesCount, noCount: props.controller.settlement.value.noCount })"
          />
        </section>
      </UContainer>
    </template>
  </UDashboardPanel>
</template>
